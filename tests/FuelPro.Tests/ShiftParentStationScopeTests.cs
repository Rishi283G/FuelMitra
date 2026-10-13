using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using FuelPro.Core.Models;
using FuelPro.Data;
using FuelPro.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelPro.Tests;

public class ShiftParentStationScopeTests
{
    [Fact]
    public async Task CorrectStationShiftSyncGuid_IsResolvedAndImported()
    {
        var guid = Guid.NewGuid().ToString();
        const string station = "station-A";
        var (context, engine, requests) = CreateHarness($"[{{\"SyncGuid\":\"{guid}\",\"station_id\":\"{station}\",\"ShiftDate\":\"2026-10-07T00:00:00\",\"ShiftType\":\"A\"}}]");
        await using (context)
        {
            var localId = await FetchShiftAsync(engine, context, guid, station);
            Assert.NotNull(localId);
            var shift = await context.Shifts.SingleAsync();
            Assert.Equal(localId, shift.ShiftId);
            Assert.Equal(new DateTime(2026, 10, 7), shift.ShiftDate.Date);
            Assert.Equal("A", shift.ShiftType);
            Assert.Contains($"station_id=eq.{station}", Assert.Single(requests));
        }
    }

    [Fact]
    public async Task ShiftSyncGuidForDifferentStation_IsRejectedWithoutImport()
    {
        var guid = Guid.NewGuid().ToString();
        var (context, engine, requests) = CreateHarness($"[{{\"SyncGuid\":\"{guid}\",\"station_id\":\"station-B\",\"ShiftDate\":\"2026-10-07T00:00:00\",\"ShiftType\":\"A\"}}]");
        await using (context)
        {
            var localId = await FetchShiftAsync(engine, context, guid, "station-A");
            Assert.Null(localId);
            Assert.Empty(await context.Shifts.ToListAsync());
            Assert.Empty(await context.SyncIdMappings.ToListAsync());
            Assert.Contains("station_id=eq.station-A", Assert.Single(requests));
        }
    }

    [Fact]
    public async Task MissingShiftSyncGuid_RemainsUnresolvedWithoutImport()
    {
        var guid = Guid.NewGuid().ToString();
        var (context, engine, requests) = CreateHarness("[]");
        await using (context)
        {
            var localId = await FetchShiftAsync(engine, context, guid, "station-A");
            Assert.Null(localId);
            Assert.Empty(await context.Shifts.ToListAsync());
            Assert.Empty(await context.SyncIdMappings.ToListAsync());
            Assert.Contains("station_id=eq.station-A", Assert.Single(requests));
        }
    }

    [Fact]
    public async Task OilLogReferencingAnotherStationsShift_IsNotAssignedOrImported()
    {
        var guid = Guid.NewGuid().ToString();
        var (context, engine, _) = CreateHarness($"[{{\"SyncGuid\":\"{guid}\",\"station_id\":\"station-B\",\"ShiftDate\":\"2026-10-07T00:00:00\",\"ShiftType\":\"B\"}}]");
        await using (context)
        {
            var existingLocalShift = new Shift { ShiftDate = new DateTime(2026, 10, 7), ShiftType = "B" };
            context.Shifts.Add(existingLocalShift);
            await context.SaveChangesAsync();
            context.SyncIdMappings.Add(new SyncIdMapping
            {
                TableName = "Shifts",
                RemoteGuid = guid,
                LocalId = existingLocalShift.ShiftId
            });
            await context.SaveChangesAsync();

            var oilLog = new OilDefDailyLog { LogDate = new DateTime(2026, 10, 7), ShiftId = null };
            var localShiftId = await FetchShiftAsync(engine, context, guid, "station-A");

            if (localShiftId.HasValue)
                oilLog.ShiftId = localShiftId.Value;

            Assert.Null(oilLog.ShiftId);
            Assert.Single(await context.Shifts.ToListAsync());
            Assert.Single(await context.SyncIdMappings.ToListAsync());
        }
    }

    private static (FuelProDbContext Context, SyncEngine Engine, List<string> Requests) CreateHarness(string responseJson)
    {
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var context = new FuelProDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        var services = new ServiceCollection();
        services.AddSingleton<SyncConfigService>();
        var provider = services.BuildServiceProvider();
        var requests = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add(request.RequestUri!.PathAndQuery);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        });
        var httpClient = new SupabaseHttpClient(new HttpClient(handler));
        httpClient.Configure("https://example.invalid", "test-key");
        return (context, new SyncEngine(provider, httpClient), requests);
    }

    private static async Task<int?> FetchShiftAsync(SyncEngine engine, FuelProDbContext context, string guid, string stationId)
    {
        var method = typeof(SyncEngine).GetMethod("FetchAndImportMissingParentAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<int?>)method.Invoke(engine, new object[]
        {
            context,
            new Dictionary<string, Dictionary<string, int>>(),
            "Shifts",
            guid,
            new SyncSettings { StationId = stationId }
        })!;
        return await task;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(responseFactory(request));
    }
}
