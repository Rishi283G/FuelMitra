using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.Tests;

/// <summary>
/// Exercises OilDefDailyLogs through the same public ForceSyncAsync entry point used by the app.
/// All HTTP is handled locally and each test uses an isolated SQLite file under the OS temp folder.
/// </summary>
public sealed class OilDefDailyLogSyncEngineIntegrationTests
{
    private const string StationId = "TEST-STATION";
    private const string MachineId = "TEST-MACHINE";
    private const string ProductGuid = "5da2c7d2-6b4d-4d4c-8fca-44dcb5d81201";
    private const string ShiftGuid = "f2677ce7-0b79-43b7-b05c-cf1b77d25702";
    private static readonly DateTime SaleDate = new(2026, 10, 7);

    [Fact]
    public async Task Push_MapsLocalShiftIdToExactRemoteSyncGuidAndPreservesFields()
    {
        await using var harness = await CreateHarness(owner: false);
        await SeedProductAsync(harness, localId: 17, mapToRemote: true);
        await SeedShiftAsync(harness, localId: 101, remoteGuid: ShiftGuid);
        await SeedOilLogAndChangeAsync(harness, localId: 501, shiftId: 101);

        await harness.Engine.ForceSyncAsync();

        var request = Assert.Single(harness.Requests.Where(r => r.Method == HttpMethod.Post && r.Table == "OilDefDailyLogs"));
        using var payload = JsonDocument.Parse(request.Body);
        var log = Assert.Single(payload.RootElement.EnumerateArray().ToArray());
        Assert.Equal(ShiftGuid, log.GetProperty("ShiftId").GetString());
        Assert.NotEqual("101", log.GetProperty("ShiftId").GetString());
        Assert.Equal(StationId, log.GetProperty("station_id").GetString());
        Assert.Equal(ProductGuid, log.GetProperty("ProductId").GetString());
        Assert.Equal("Oil", log.GetProperty("ProductType").GetString());
        Assert.Equal(20, log.GetProperty("OverrideSaleRate").GetDouble());
        Assert.Equal(7.5, log.GetProperty("SoldQuantity").GetDouble());
        Assert.Equal(12.5, log.GetProperty("RemainingStock").GetDouble());
        Assert.Equal(0, log.GetProperty("AddedQuantity").GetDouble());
        Assert.Equal(0, log.GetProperty("AdjustmentQuantity").GetDouble());
        Assert.Equal("Phase 2 sync integration", log.GetProperty("Remarks").GetString());
        Assert.Equal(SaleDate, log.GetProperty("LogDate").GetDateTime().Date);
    }

    [Fact]
    public async Task Push_NullShiftRemainsNullAndDoesNotPushOrAttachAShift()
    {
        await using var harness = await CreateHarness(owner: false);
        await SeedProductAsync(harness, localId: 17, mapToRemote: true);
        await SeedOilLogAndChangeAsync(harness, localId: 502, shiftId: null);

        await harness.Engine.ForceSyncAsync();

        var request = Assert.Single(harness.Requests.Where(r => r.Method == HttpMethod.Post && r.Table == "OilDefDailyLogs"));
        using var payload = JsonDocument.Parse(request.Body);
        var log = Assert.Single(payload.RootElement.EnumerateArray().ToArray());
        Assert.True(log.TryGetProperty("ShiftId", out var shiftId));
        Assert.Equal(JsonValueKind.Null, shiftId.ValueKind);
        Assert.Equal(ProductGuid, log.GetProperty("ProductId").GetString());
        Assert.Equal("Oil", log.GetProperty("ProductType").GetString());
        Assert.Equal(7.5, log.GetProperty("SoldQuantity").GetDouble());
        Assert.Equal(12.5, log.GetProperty("RemainingStock").GetDouble());
        Assert.Equal("Phase 2 sync integration", log.GetProperty("Remarks").GetString());
        Assert.DoesNotContain(harness.Requests, r => r.Method == HttpMethod.Post && r.Table == "Shifts");
    }

    [Fact]
    public async Task Push_PendingShiftParentIsSentBeforeOilChildWithSameGuid()
    {
        await using var harness = await CreateHarness(owner: false);
        await SeedProductAsync(harness, localId: 17, mapToRemote: true);
        await SeedUnmappedShiftAndOilLogAsync(harness);

        await harness.Engine.ForceSyncAsync();

        var parentRequest = Assert.Single(harness.Requests.Where(r => r.Method == HttpMethod.Post && r.Table == "Shifts"));
        var childRequest = Assert.Single(harness.Requests.Where(r => r.Method == HttpMethod.Post && r.Table == "OilDefDailyLogs"));
        Assert.True(harness.Requests.IndexOf(parentRequest) < harness.Requests.IndexOf(childRequest));
        using var parentPayload = JsonDocument.Parse(parentRequest.Body);
        using var childPayload = JsonDocument.Parse(childRequest.Body);
        var remoteShiftGuid = Assert.Single(parentPayload.RootElement.EnumerateArray().ToArray()).GetProperty("SyncGuid").GetString();
        var child = Assert.Single(childPayload.RootElement.EnumerateArray().ToArray());
        Assert.NotNull(remoteShiftGuid);
        Assert.Equal(remoteShiftGuid, child.GetProperty("ShiftId").GetString());
        Assert.Equal(StationId, Assert.Single(parentPayload.RootElement.EnumerateArray().ToArray()).GetProperty("station_id").GetString());
    }

    [Fact]
    public async Task Pull_ResolvesRemoteShiftGuidToExistingLocalIdAndLoadsNavigation()
    {
        await using var harness = await CreateHarness(owner: true, remoteOilShiftGuid: ShiftGuid,
            remoteShiftLookup: ShiftResponse(ShiftGuid, StationId), remoteShifts: "[]");
        await SeedProductAsync(harness, localId: 17, mapToRemote: true);
        await SeedShiftAsync(harness, localId: 101, remoteGuid: ShiftGuid);

        await harness.Engine.ForceSyncAsync();

        await using var context = harness.CreateContext();
        var imported = await context.OilDefDailyLogs.Include(l => l.Shift).SingleAsync();
        Assert.Equal(101, imported.ShiftId);
        Assert.NotEqual(ShiftGuid, imported.ShiftId?.ToString());
        Assert.Equal("A", imported.Shift?.ShiftType);
        Assert.Equal(1, await context.Shifts.CountAsync());
        Assert.Contains(harness.Requests, r => r.Method == HttpMethod.Get && r.Table == "Shifts" && r.Query.Contains("station_id=eq.TEST-STATION", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pull_ImportsShiftParentBeforeOilAndPersistsValidRelationship()
    {
        await using var harness = await CreateHarness(owner: true, remoteOilShiftGuid: ShiftGuid,
            remoteShiftLookup: ShiftResponse(ShiftGuid, StationId), remoteShifts: ShiftResponse(ShiftGuid, StationId));
        await SeedProductAsync(harness, localId: 17, mapToRemote: true);

        await harness.Engine.ForceSyncAsync();

        await using var context = harness.CreateContext();
        var importedShift = await context.Shifts.SingleAsync();
        var importedLog = await context.OilDefDailyLogs.Include(l => l.Shift).SingleAsync();
        Assert.Equal(ShiftGuid, await GetMappedGuidAsync(context, "Shifts", importedShift.ShiftId));
        Assert.Equal(importedShift.ShiftId, importedLog.ShiftId);
        Assert.Same(importedShift, importedLog.Shift);
        Assert.Equal(1, await context.Shifts.CountAsync());
        var shiftPullIndex = harness.Requests.FindIndex(r => r.Method == HttpMethod.Get && r.Table == "Shifts" && !r.Query.Contains("SyncGuid=eq.", StringComparison.Ordinal));
        var oilPullIndex = harness.Requests.FindIndex(r => r.Method == HttpMethod.Get && r.Table == "OilDefDailyLogs");
        Assert.True(shiftPullIndex >= 0 && shiftPullIndex < oilPullIndex);
    }

    [Fact]
    public async Task Pull_WrongStationShiftMappingFailsClosedWithoutImportingOilLog()
    {
        await using var harness = await CreateHarness(owner: true, remoteOilShiftGuid: ShiftGuid,
            remoteShiftLookup: ShiftResponse(ShiftGuid, "TEST-STATION-B"), remoteShifts: "[]");
        await SeedProductAsync(harness, localId: 17, mapToRemote: true);
        await SeedShiftAsync(harness, localId: 101, remoteGuid: ShiftGuid);
        await SeedOilLogAsync(harness, localId: 700, shiftId: null, date: SaleDate.AddDays(-1));

        await harness.Engine.ForceSyncAsync();

        await using var context = harness.CreateContext();
        var logs = await context.OilDefDailyLogs.OrderBy(l => l.Id).ToListAsync();
        var existing = Assert.Single(logs);
        Assert.Equal(700, existing.Id);
        Assert.Null(existing.ShiftId);
        Assert.Equal(1, await context.Shifts.CountAsync());
        Assert.Contains(harness.Requests, r => r.Method == HttpMethod.Get && r.Table == "Shifts" && r.Query.Contains("station_id=eq.TEST-STATION", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pull_MissingShiftParentFailsClosedAndPreservesUnrelatedOilRows()
    {
        await using var harness = await CreateHarness(owner: true, remoteOilShiftGuid: ShiftGuid,
            remoteShiftLookup: "[]", remoteShifts: "[]");
        await SeedProductAsync(harness, localId: 17, mapToRemote: true);
        await SeedOilLogAsync(harness, localId: 700, shiftId: null, date: SaleDate.AddDays(-1));

        await harness.Engine.ForceSyncAsync();

        await using var context = harness.CreateContext();
        var existing = Assert.Single(await context.OilDefDailyLogs.ToListAsync());
        Assert.Equal(700, existing.Id);
        Assert.Null(existing.ShiftId);
        Assert.Empty(await context.Shifts.ToListAsync());
    }

    [Fact]
    public async Task Pull_NullLegacyShiftImportsWithoutInventingShift()
    {
        await using var harness = await CreateHarness(owner: true, remoteOilShiftGuid: null,
            remoteShiftLookup: "[]", remoteShifts: "[]");
        await SeedProductAsync(harness, localId: 17, mapToRemote: true);

        await harness.Engine.ForceSyncAsync();

        await using var context = harness.CreateContext();
        var imported = await context.OilDefDailyLogs.SingleAsync();
        Assert.Null(imported.ShiftId);
        Assert.Null(imported.Shift);
        Assert.Empty(await context.Shifts.ToListAsync());
        Assert.DoesNotContain(harness.Requests, r => r.Method == HttpMethod.Get && r.Table == "Shifts" && r.Query.Contains("SyncGuid=eq.", StringComparison.Ordinal));
    }

    private static async Task<TestHarness> CreateHarness(
        bool owner,
        string? remoteOilShiftGuid = null,
        string? remoteShiftLookup = null,
        string? remoteShifts = null)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"fuelpro-oil-sync-{Guid.NewGuid():N}.db");
        var requests = new List<CapturedRequest>();
        var oilJson = OilLogResponse(remoteOilShiftGuid);
        var handler = new FakeHttpHandler(request =>
        {
            requests.Add(request);
            if (request.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
            if (request.Method != HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.NoContent);

            if (request.Table == "OilDefDailyLogs")
                return JsonResponse(oilJson);
            if (request.Table == "Shifts")
                return JsonResponse(request.Query.Contains("SyncGuid=eq.", StringComparison.Ordinal)
                    ? remoteShiftLookup ?? "[]"
                    : remoteShifts ?? "[]");
            return JsonResponse("[]");
        });

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options => options.UseSqlite($"Data Source={dbPath};Default Timeout=5"), ServiceLifetime.Transient);
        services.AddTransient<DbContext>(sp => sp.GetRequiredService<FuelProDbContext>());
        services.AddSingleton<IUserRepository, NoopUserRepository>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<SyncConfigService>();
        var provider = services.BuildServiceProvider();
        using (var context = provider.GetRequiredService<FuelProDbContext>())
        {
            await context.Database.EnsureCreatedAsync();
            FuelProDbContext.BypassTracking = true;
            try
            {
                context.AppMeta.AddRange(
                    new AppMeta { Key = "Sync.SupabaseUrl", Value = "https://offline.invalid" },
                    new AppMeta { Key = "Sync.SupabaseApiKey", Value = "offline-test-key" },
                    new AppMeta { Key = "Sync.StationId", Value = StationId },
                    new AppMeta { Key = "Sync.MachineId", Value = MachineId },
                    new AppMeta { Key = "Sync.IsEnabled", Value = "true" },
                    new AppMeta { Key = "Sync.LastSyncTime", Value = new DateTime(2026, 1, 1).ToString("o") });
                await context.SaveChangesAsync();
            }
            finally
            {
                FuelProDbContext.BypassTracking = false;
            }
        }

        var auth = provider.GetRequiredService<AuthService>();
        typeof(AuthService).GetProperty(nameof(AuthService.CurrentUser))!
            .SetValue(auth, new User { Username = "test", Role = owner ? "Owner" : "Manager" });
        var httpClient = new HttpClient(handler);
        var supabase = new SupabaseHttpClient(httpClient);
        var engine = new SyncEngine(provider, supabase);
        return new TestHarness(dbPath, provider, httpClient, engine, requests);
    }

    private static async Task SeedProductAsync(TestHarness harness, int localId, bool mapToRemote)
    {
        await using var context = harness.CreateContext();
        FuelProDbContext.BypassTracking = true;
        try
        {
            context.ProductMasters.Add(new ProductMaster { Id = localId, ProductName = "Test Oil", Category = "Oil", Unit = "Litre", DefaultSaleRate = 20 });
            if (mapToRemote)
                context.SyncIdMappings.Add(new SyncIdMapping { TableName = "ProductMasters", RemoteGuid = ProductGuid, LocalId = localId });
            await context.SaveChangesAsync();
        }
        finally { FuelProDbContext.BypassTracking = false; }
    }

    private static async Task SeedShiftAsync(TestHarness harness, int localId, string remoteGuid)
    {
        await using var context = harness.CreateContext();
        FuelProDbContext.BypassTracking = true;
        try
        {
            context.Shifts.Add(new Shift { ShiftId = localId, ShiftDate = SaleDate, ShiftType = "A" });
            context.SyncIdMappings.Add(new SyncIdMapping { TableName = "Shifts", RemoteGuid = remoteGuid, LocalId = localId });
            await context.SaveChangesAsync();
        }
        finally { FuelProDbContext.BypassTracking = false; }
    }

    private static async Task SeedOilLogAndChangeAsync(TestHarness harness, int localId, int? shiftId)
    {
        await using var context = harness.CreateContext();
        FuelProDbContext.BypassTracking = true;
        try
        {
            context.OilDefDailyLogs.Add(CreateOilLog(localId, shiftId, SaleDate));
            context.SyncChangeLogs.Add(NewChangeLog("OilDefDailyLogs", localId));
            await context.SaveChangesAsync();
        }
        finally { FuelProDbContext.BypassTracking = false; }
    }

    private static async Task SeedOilLogAsync(TestHarness harness, int localId, int? shiftId, DateTime date)
    {
        await using var context = harness.CreateContext();
        FuelProDbContext.BypassTracking = true;
        try
        {
            context.OilDefDailyLogs.Add(CreateOilLog(localId, shiftId, date));
            await context.SaveChangesAsync();
        }
        finally { FuelProDbContext.BypassTracking = false; }
    }

    private static async Task SeedUnmappedShiftAndOilLogAsync(TestHarness harness)
    {
        await using var context = harness.CreateContext();
        FuelProDbContext.BypassTracking = true;
        try
        {
            context.Shifts.Add(new Shift { ShiftId = 101, ShiftDate = SaleDate, ShiftType = "A" });
            context.OilDefDailyLogs.Add(CreateOilLog(501, 101, SaleDate));
            context.SyncChangeLogs.Add(NewChangeLog("Shifts", 101));
            context.SyncChangeLogs.Add(NewChangeLog("OilDefDailyLogs", 501));
            await context.SaveChangesAsync();
        }
        finally { FuelProDbContext.BypassTracking = false; }
    }

    private static OilDefDailyLog CreateOilLog(int localId, int? shiftId, DateTime date) => new()
    {
        Id = localId,
        LogDate = date,
        ProductType = "Oil",
        ProductId = 17,
        ShiftId = shiftId,
        OverrideSaleRate = 20,
        SoldQuantity = 7.5,
        RemainingStock = 12.5,
        AddedQuantity = 0,
        AdjustmentQuantity = 0,
        Remarks = "Phase 2 sync integration"
    };

    private static SyncChangeLog NewChangeLog(string table, int localId) => new()
    {
        TableName = table,
        RecordId = localId,
        Operation = "INSERT",
        IsSynced = false,
        StationId = StationId,
        MachineId = MachineId
    };

    private static string ShiftResponse(string guid, string station) => JsonSerializer.Serialize(new[]
    {
        new { SyncGuid = guid, station_id = station, ShiftDate = SaleDate.ToString("yyyy-MM-ddTHH:mm:ss"), ShiftType = "A" }
    });

    private static string OilLogResponse(string? shiftGuid) => JsonSerializer.Serialize(new[]
    {
        new
        {
            SyncGuid = "a177d178-d496-424f-8c3e-d6521108fc22",
            station_id = StationId,
            machine_id = "REMOTE-MACHINE",
            ProductId = ProductGuid,
            ShiftId = shiftGuid,
            LogDate = SaleDate.ToString("yyyy-MM-ddTHH:mm:ss"),
            ProductType = "Oil",
            OverrideSaleRate = 20,
            SoldQuantity = 7.5,
            RemainingStock = 12.5,
            AddedQuantity = 0,
            AdjustmentQuantity = 0,
            AdjustmentType = (string?)null,
            Remarks = "Remote Phase 2 oil sale"
        }
    });

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static async Task<string?> GetMappedGuidAsync(FuelProDbContext context, string table, int localId) =>
        await context.SyncIdMappings.Where(m => m.TableName == table && m.LocalId == localId)
            .Select(m => m.RemoteGuid).SingleOrDefaultAsync();

    private sealed record CapturedRequest(HttpMethod Method, string Table, string Query, string Body);

    private sealed class FakeHttpHandler(Func<CapturedRequest, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath[(request.RequestUri.AbsolutePath.LastIndexOf('/') + 1)..];
            var captured = new CapturedRequest(request.Method, path, request.RequestUri.Query,
                request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return responder(captured);
        }
    }

    private sealed class TestHarness(string dbPath, ServiceProvider provider, HttpClient httpClient, SyncEngine engine, List<CapturedRequest> requests) : IAsyncDisposable
    {
        public SyncEngine Engine { get; } = engine;
        public List<CapturedRequest> Requests { get; } = requests;
        public FuelProDbContext CreateContext() => provider.GetRequiredService<FuelProDbContext>();

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            httpClient.Dispose();
            try { File.Delete(dbPath); } catch (IOException) { }
        }
    }

    private sealed class NoopUserRepository : IUserRepository
    {
        public Task<Result<User>> GetByUsernameAsync(string username) => throw new NotSupportedException();
        public Task<Result<List<User>>> GetAllUsersAsync() => throw new NotSupportedException();
        public Task<Result<User>> CreateUserAsync(User user) => throw new NotSupportedException();
        public Task<Result> UpdateUserAsync(User user) => throw new NotSupportedException();
        public Task<Result> DeleteUserAsync(int userId) => throw new NotSupportedException();
    }
}
