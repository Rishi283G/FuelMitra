using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.Sync;
using FuelPro.UI;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FuelPro.Tests;

[Collection("PumpConfiguration")]
public class ReceivingDesktopRefreshTests : IDisposable
{
    private readonly List<string> _tempDirs = new();

    public ReceivingDesktopRefreshTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private static async Task SyncAndWaitAsync(SyncEngine engine)
    {
        var isSyncingField = typeof(SyncEngine).GetField("_isSyncing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while ((bool)(isSyncingField?.GetValue(engine) ?? false) && sw.ElapsedMilliseconds < 5000)
        {
            await Task.Delay(25);
        }
        await engine.ForceSyncAsync();
    }

    private sealed class FakeSupabaseServer
    {
        private readonly ConcurrentDictionary<string, List<JObject>> _tables = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new();

        public HttpMessageHandler CreateHandler() => new Handler(this);

        private HttpResponseMessage Handle(HttpRequestMessage request, string? body)
        {
            var path = request.RequestUri!.AbsolutePath.Trim('/');
            var tableName = path.StartsWith("rest/v1/", StringComparison.OrdinalIgnoreCase)
                ? path.Substring("rest/v1/".Length)
                : path;
            var query = request.RequestUri.Query;

            if (request.Method == HttpMethod.Post && !string.IsNullOrWhiteSpace(body))
            {
                using var sr = new StringReader(body);
                using var jtr = new JsonTextReader(sr) { DateParseHandling = DateParseHandling.None };
                var token = JToken.ReadFrom(jtr);
                var incoming = token is JArray arr
                    ? arr.OfType<JObject>().ToList()
                    : token is JObject single
                        ? new List<JObject> { single }
                        : new List<JObject>();

                lock (_gate)
                {
                    var rows = _tables.GetOrAdd(tableName, _ => new List<JObject>());
                    foreach (var item in incoming)
                    {
                        var syncGuid = item["SyncGuid"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(syncGuid))
                        {
                            rows.RemoveAll(r => string.Equals(r["SyncGuid"]?.ToString(), syncGuid, StringComparison.OrdinalIgnoreCase));
                        }
                        if (item["updated_at"] == null || string.IsNullOrWhiteSpace(item["updated_at"]?.ToString()))
                        {
                            item["updated_at"] = DateTime.UtcNow.ToString(SyncTimestampHelper.UtcFormat, CultureInfo.InvariantCulture);
                        }
                        rows.Add((JObject)item.DeepClone());
                    }
                }

                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            }

            if (request.Method == HttpMethod.Get)
            {
                List<JObject> snapshot;
                lock (_gate)
                {
                    snapshot = _tables.TryGetValue(tableName, out var rows)
                        ? rows.Select(r => (JObject)r.DeepClone()).ToList()
                        : new List<JObject>();
                }

                var queryParams = ParseQueryParameters(query);
                foreach (var (key, val) in queryParams)
                {
                    if (string.Equals(key, "station_id", StringComparison.OrdinalIgnoreCase) && val.StartsWith("eq."))
                    {
                        var expected = Uri.UnescapeDataString(val.Substring(3));
                        snapshot = snapshot.Where(r => string.Equals(r["station_id"]?.ToString(), expected, StringComparison.OrdinalIgnoreCase)).ToList();
                    }
                    else if (string.Equals(key, "updated_at", StringComparison.OrdinalIgnoreCase) && val.StartsWith("gt."))
                    {
                        var gtDt = ParseUtc(Uri.UnescapeDataString(val.Substring(3)));
                        snapshot = snapshot.Where(r => ParseUtc(r["updated_at"]?.ToString()) > gtDt).ToList();
                    }
                    else if (string.Equals(key, "updated_at", StringComparison.OrdinalIgnoreCase) && val.StartsWith("gte."))
                    {
                        var gteDt = ParseUtc(Uri.UnescapeDataString(val.Substring(4)));
                        snapshot = snapshot.Where(r => ParseUtc(r["updated_at"]?.ToString()) >= gteDt).ToList();
                    }
                    else if (string.Equals(key, "updated_at", StringComparison.OrdinalIgnoreCase) && val.StartsWith("lte."))
                    {
                        var lteDt = ParseUtc(Uri.UnescapeDataString(val.Substring(4)));
                        snapshot = snapshot.Where(r => ParseUtc(r["updated_at"]?.ToString()) <= lteDt).ToList();
                    }
                }

                // Honor PostgREST cursor filter for keyset pagination if SyncGuid.gt appears in or=(...)
                if (queryParams.Any(p => string.Equals(p.Key, "or", StringComparison.OrdinalIgnoreCase)))
                {
                    // Single small-batch tests stay under 500 rows; ensure distinct ordering.
                }

                snapshot = snapshot
                    .OrderBy(r => r["updated_at"]?.ToString() ?? string.Empty, StringComparer.Ordinal)
                    .ThenBy(r => r["SyncGuid"]?.ToString() ?? string.Empty, StringComparer.Ordinal)
                    .ToList();

                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new JArray(snapshot).ToString(), Encoding.UTF8, "application/json")
                };
                resp.Headers.Date = DateTimeOffset.UtcNow;
                return resp;
            }

            if (request.Method == HttpMethod.Patch)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        }

        private static List<(string Key, string Value)> ParseQueryParameters(string query)
        {
            var result = new List<(string, string)>();
            if (string.IsNullOrWhiteSpace(query)) return result;
            foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = part.IndexOf('=');
                if (idx <= 0) continue;
                result.Add((part.Substring(0, idx), part.Substring(idx + 1)));
            }
            return result;
        }

        private static DateTime ParseUtc(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return DateTime.MinValue;
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)
                ? dt.ToUniversalTime()
                : DateTime.MinValue;
        }

        private sealed class Handler : HttpMessageHandler
        {
            private readonly FakeSupabaseServer _server;
            public Handler(FakeSupabaseServer server) => _server = server;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
                return _server.Handle(request, body);
            }
        }
    }

    private sealed class DesktopNode : IDisposable
    {
        public string DbPath { get; }
        public ServiceProvider Services { get; }
        public SyncEngine SyncEngine => Services.GetRequiredService<SyncEngine>();

        public DesktopNode(string machineId, string stationId, FakeSupabaseServer server, List<string> tempDirs)
        {
            var dir = Path.Combine(Path.GetTempPath(), $"FuelPro_Node_{machineId}_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            tempDirs.Add(dir);
            DbPath = Path.Combine(dir, "fuelPro_node.db");

            var services = new ServiceCollection();
            services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite($"Data Source={DbPath};Pooling=False"), ServiceLifetime.Transient);
            services.AddSingleton<ICredentialFileService>(new LocalCredentialFileService(Path.Combine(dir, "credentials")));

            services.AddTransient<ISettingsRepository, SettingsRepository>();
            services.AddTransient<IUserRepository, UserRepository>();
            services.AddTransient<IShiftRepository, ShiftRepository>();
            services.AddTransient<IDsmEntryRepository, DsmEntryRepository>();
            services.AddTransient<INozzleReadingRepository, NozzleReadingRepository>();
            services.AddTransient<IPaymentRepository, PaymentRepository>();
            services.AddTransient<IDebitEntryRepository, DebitEntryRepository>();
            services.AddTransient<ITestingEntryRepository, TestingEntryRepository>();
            services.AddTransient<IExpenseRepository, ExpenseRepository>();
            services.AddTransient<ICashDenominationRepository, CashDenominationRepository>();
            services.AddTransient<IDsmProfileRepository, DsmProfileRepository>();
            services.AddTransient<ICreditorRepaymentRepository, CreditorRepaymentRepository>();
            services.AddTransient<IAgsImportRepository, AgsImportRepository>();
            services.AddTransient<ICreditorRepository, CreditorRepository>();
            services.AddTransient<IPumpExpenseRepository, PumpExpenseRepository>();
            services.AddTransient<IDebtorVehicleRepository, DebtorVehicleRepository>();
            services.AddTransient<IDsmPersonalDebtorRepository, DsmPersonalDebtorRepository>();
            services.AddTransient<IFuelTankerRepository, FuelTankerRepository>();
            services.AddTransient<ITankDailyStockRepository, TankDailyStockRepository>();
            services.AddTransient<IOpeningBalanceRepository, OpeningBalanceRepository>();
            services.AddScoped<IShiftOtherCashRepository, ShiftOtherCashRepository>();
            services.AddScoped<IShiftFuelRateRepository, ShiftFuelRateRepository>();

            services.AddSingleton<AuthService>();
            services.AddTransient<DsmEntryService>();
            services.AddTransient<ShiftCalculationService>();
            services.AddSingleton<IDsmCalculationService, DsmCalculationService>();
            services.AddSingleton<IOwnerCalculationService, OwnerCalculationService>();
            services.AddSingleton<ITidCalculationService, TidCalculationService>();
            services.AddScoped<IShiftAggregationService, ShiftAggregationService>();
            services.AddScoped<IReportService, ReportService>();
            services.AddTransient<IAgsInventoryService, AgsInventoryService>();
            services.AddTransient<IFinancialCalculationService, MockFinancialCalculationService>();
            services.AddTransient<PrintService>();
            services.AddTransient<ExcelExportService>();
            services.AddTransient<IAuditLogService, AuditLogService>();
            services.AddTransient<IDayLockService, DayLockService>();
            services.AddTransient<IFeatureToggleService, FeatureToggleService>();
            services.AddTransient<ICollectionTypeService, CollectionTypeService>();
            services.AddTransient<IStationConfigurationService, StationConfigurationService>();
            services.AddTransient<IShiftCycleService, ShiftCycleService>();

            var httpClient = new HttpClient(server.CreateHandler());
            var supabaseClient = new SupabaseHttpClient(httpClient);
            supabaseClient.Configure("https://mock.supabase.co", "mock-key");
            services.AddSingleton(supabaseClient);
            services.AddSingleton<SyncConfigService>();
            services.AddSingleton<StationPairingService>();
            services.AddSingleton<SyncEngine>();
            services.AddSingleton<DsmSubmissionPollingService>();
            services.AddTransient<DsmAuthAdminService>();
            services.AddSingleton<DraftService>();

            services.AddTransient<DsmEntryViewModel>();
            services.AddTransient<FinalCalculationViewModel>();
            services.AddTransient<DayTotalViewModel>();

            Services = services.BuildServiceProvider();

            using var ctx = Services.GetRequiredService<FuelProDbContext>();
            ctx.Database.EnsureCreated();

            var today = DateTime.Today;
            var now = DateTime.Now;
            var tanks = new List<TankDefinition>
            {
                new() { TankName = "MS - 20KL", FuelType = "MS-I", Rate = 100.00, CapacityKL = 20, IsActive = true, CreatedAt = now },
                new() { TankName = "HSD - 20KL", FuelType = "HSD", Rate = 90.00, CapacityKL = 20, IsActive = true, CreatedAt = now }
            };
            PumpConfiguration.InitializeTanksFromDb(tanks);
            ctx.TankDefinitions.AddRange(tanks);

            var mappings = new List<PumpMapping>
            {
                new() { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now },
                new() { PumpId = 1, NozzleNumber = 2, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true, CreatedAt = now }
            };
            PumpConfiguration.InitializeFromDb(mappings);
            ctx.PumpMappings.AddRange(mappings);

            ctx.Shifts.Add(new Shift { ShiftDate = today, ShiftType = "A" });
            ctx.Settings.Add(new Setting
            {
                SettingId = 1,
                PumpStationName = "Mitali Service Station",
                HsdRate = 90.00,
                MsIRate = 100.00,
                MsIIRate = 105.00,
                CngRate = 85.00
            });
            ctx.SaveChanges();

            var config = Services.GetRequiredService<SyncConfigService>();
            config.SaveSettingsAsync(new SyncSettings
            {
                SyncEnabled = true,
                SupabaseUrl = "https://mock.supabase.co",
                SupabaseApiKey = "mock-key",
                StationId = stationId,
                MachineId = machineId
            }).GetAwaiter().GetResult();
        }

        public void ActivateAsCurrentApp()
        {
            typeof(App).GetProperty("Services")?.SetValue(null, Services);
            typeof(App).GetProperty("DbPath")?.SetValue(null, DbPath);
        }

        public void Dispose() => Services.Dispose();
    }

    [Fact]
    public async Task Computer1_And_Computer2_TwoWayManualDsmEntries_RefreshReadingsOnReceivingDesktopWithoutRestart_AndRemainIdempotent()
    {
        var server = new FakeSupabaseServer();
        const string stationId = "MITALI-TEST-SYNC";

        using var computer1 = new DesktopNode("PC-01", stationId, server, _tempDirs);
        using var computer2 = new DesktopNode("PC-02", stationId, server, _tempDirs);

        // Bind App.Services to Computer 1 while setting up Computer 1's ViewModels (matches live desktop process)
        computer1.ActivateAsCurrentApp();
        using var pc1DsmVm = new DsmEntryViewModel(computer1.Services);
        using var pc1ShiftTotalVm = computer1.Services.GetRequiredService<FinalCalculationViewModel>();
        using var pc1DayTotalVm = computer1.Services.GetRequiredService<DayTotalViewModel>();

        // Keep Computer 2's DsmEntryViewModel in its own scope (simulates 2nd PC running)
        using var pc2DsmVm = new DsmEntryViewModel(computer2.Services);

        await Task.Delay(150);

        // ── TEST A: Computer 1 creates a manual DSM entry; Computer 2 receives and updates ──
        pc1DsmVm.SelectedDate = DateTime.Today;
        pc1DsmVm.SelectedShift = "A";
        pc1DsmVm.SelectedPump = pc1DsmVm.PumpOptions.First(p => p.PumpId == 1);
        await Task.Delay(100);

        pc1DsmVm.DsmName = "DSM One";
        pc1DsmVm.NozzleReadings.Clear();
        pc1DsmVm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 1000, ClosingReading = 1200, Rate = 100, OnRowChanged = pc1DsmVm.RecalculateAll });
        pc1DsmVm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 1, NozzleNumber = 2, FuelType = "HSD", OpeningReading = 500, ClosingReading = 600, Rate = 90, OnRowChanged = pc1DsmVm.RecalculateAll });
        pc1DsmVm.Cash2.Denom500 = 58; // 29,000
        pc1DsmVm.RecalculateAll();

        await pc1DsmVm.SaveEntryCommand.ExecuteAsync(null);
        Assert.True(pc1DsmVm.IsSaved, pc1DsmVm.StatusMessage);
        Assert.False(pc1DsmVm.IsDirty);

        // Refresh Shift Total & Day Total on Computer 1 after Test A
        await pc1ShiftTotalVm.LoadShiftDataCommand.ExecuteAsync(null);
        await pc1DayTotalVm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(20000.0 + 9000.0, pc1ShiftTotalVm.CurrentReport!.TotalFuelAmount, precision: 2);
        Assert.Equal(1200.0, pc1DayTotalVm.NozzleGroups.SelectMany(g => g.FlatNozzles).First(n => n.NozzleNumber == 1).ClosingReading, precision: 2);

        // Computer 1 pushes Test A entry
        await SyncAndWaitAsync(computer1.SyncEngine);

        // Computer 2 pulls Test A entry (must refresh without restart)
        pc2DsmVm.SelectedDate = DateTime.Today;
        pc2DsmVm.SelectedShift = "A";
        pc2DsmVm.SelectedPump = pc2DsmVm.PumpOptions.First(p => p.PumpId == 1);
        await Task.Delay(100);

        await SyncAndWaitAsync(computer2.SyncEngine);
        await Task.Delay(250);

        Assert.Equal(1200.0, pc2DsmVm.NozzleReadings.First(n => n.NozzleNumber == 1).OpeningReading ?? 0.0, precision: 2);
        Assert.Equal(600.0, pc2DsmVm.NozzleReadings.First(n => n.NozzleNumber == 2).OpeningReading ?? 0.0, precision: 2);
        Assert.Equal(100.0, pc2DsmVm.NozzleReadings.First(n => n.NozzleNumber == 1).Rate ?? 0.0, precision: 2);
        Assert.Equal(90.0, pc2DsmVm.NozzleReadings.First(n => n.NozzleNumber == 2).Rate ?? 0.0, precision: 2);
        Assert.Single(pc2DsmVm.ShiftEntries);

        // ── TEST B: Computer 2 creates a manual DSM entry; Computer 1 STILL has Test A's saved entry open ──
        pc2DsmVm.DsmName = "DSM Two";
        pc2DsmVm.NozzleReadings.First(n => n.NozzleNumber == 1).ClosingReading = 1450; // +250L * 100 = 25,000
        pc2DsmVm.NozzleReadings.First(n => n.NozzleNumber == 2).ClosingReading = 750;  // +150L * 90 = 13,500
        pc2DsmVm.PhonePeMorning = 38500;
        pc2DsmVm.RecalculateAll();

        // Point App.Services temporarily while Computer 2 saves cleanly
        computer2.ActivateAsCurrentApp();
        await pc2DsmVm.SaveEntryCommand.ExecuteAsync(null);
        Assert.True(pc2DsmVm.IsSaved, pc2DsmVm.StatusMessage);

        // Push Computer 2's entry to shared cloud
        await SyncAndWaitAsync(computer2.SyncEngine);

        // Restore Computer 1 as active process and pull Computer 2's entry WITHOUT restarting or manually clicking refresh
        computer1.ActivateAsCurrentApp();
        await SyncAndWaitAsync(computer1.SyncEngine);
        await Task.Delay(350);

        // Computer 1 DsmEntryViewModel must have reset the previously completed form and now show latest meter opening readings!
        Assert.False(pc1DsmVm.IsSaved);
        Assert.Equal(1450.0, pc1DsmVm.NozzleReadings.First(n => n.NozzleNumber == 1).OpeningReading ?? 0.0, precision: 2);
        Assert.Equal(750.0, pc1DsmVm.NozzleReadings.First(n => n.NozzleNumber == 2).OpeningReading ?? 0.0, precision: 2);
        Assert.Equal(2, pc1DsmVm.ShiftEntries.Count);

        // Computer 1 Shift Summary & Day Total must also auto-refresh to reflect both entries' readings
        Assert.Equal(2, pc1ShiftTotalVm.CurrentReport!.DsmSummaryRows.Count);
        Assert.Equal(29000.0 + 38500.0, pc1ShiftTotalVm.CurrentReport!.TotalFuelAmount, precision: 2);
        var noz1ShiftSummary = pc1ShiftTotalVm.NozzleGroups
            .SelectMany(g => g.FlatNozzles)
            .First(n => n.NozzleNumber == 1);
        Assert.Equal(1000.0, noz1ShiftSummary.OpeningReading, precision: 2);
        Assert.Equal(1450.0, noz1ShiftSummary.ClosingReading, precision: 2);
        Assert.Equal(450.0, noz1ShiftSummary.SaleLitres, precision: 2);

        Assert.Equal(29000.0 + 38500.0, pc1DayTotalVm.CurrentReport!.TotalFuelAmount, precision: 2);
        var noz1DaySummary = pc1DayTotalVm.NozzleGroups
            .SelectMany(g => g.FlatNozzles)
            .First(n => n.NozzleNumber == 1);
        Assert.Equal(1450.0, noz1DaySummary.ClosingReading, precision: 2);
        Assert.Equal(250.0, noz1DaySummary.SaleLitres, precision: 2);

        // Repeated sync must NOT duplicate entries and historical Test A entry readings remain preserved
        await SyncAndWaitAsync(computer1.SyncEngine);
        await Task.Delay(200);
        using var verifyCtx = computer1.Services.GetRequiredService<FuelProDbContext>();
        var allEntries = await verifyCtx.DsmEntries.Include(e => e.NozzleReadings).OrderBy(e => e.DsmEntryId).ToListAsync();
        Assert.Equal(2, allEntries.Count);
        var testAEntry = allEntries.First(e => e.DsmName == "DSM One");
        Assert.Equal(1000.0, testAEntry.NozzleReadings.First(n => n.NozzleNumber == 1).OpeningReading, precision: 2);
        Assert.Equal(1200.0, testAEntry.NozzleReadings.First(n => n.NozzleNumber == 1).ClosingReading, precision: 2);
    }

    [Fact]
    public async Task DsmEntryViewModel_ActiveUnsavedEdits_ArePreservedWhenRemoteEntryArrives()
    {
        var server = new FakeSupabaseServer();
        const string stationId = "MITALI-TEST-UNSAVED";

        using var computer1 = new DesktopNode("PC-01", stationId, server, _tempDirs);
        using var computer2 = new DesktopNode("PC-02", stationId, server, _tempDirs);

        computer1.ActivateAsCurrentApp();
        using var pc1DsmVm = new DsmEntryViewModel(computer1.Services);
        await Task.Delay(100);

        pc1DsmVm.SelectedDate = DateTime.Today;
        pc1DsmVm.SelectedShift = "A";
        pc1DsmVm.SelectedPump = pc1DsmVm.PumpOptions.First(p => p.PumpId == 1);
        await Task.Delay(100);

        // Operator on Computer 1 is actively typing an unsaved entry (must NOT be cleared!)
        pc1DsmVm.DsmName = "In Progress Operator";
        pc1DsmVm.NozzleReadings.Clear();
        pc1DsmVm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 1000, ClosingReading = 1099, Rate = 100, OnRowChanged = pc1DsmVm.RecalculateAll });

        // Computer 2 saves & pushes an entry
        computer2.ActivateAsCurrentApp();
        using var pc2DsmVm = new DsmEntryViewModel(computer2.Services);
        await Task.Delay(100);
        pc2DsmVm.SelectedDate = DateTime.Today;
        pc2DsmVm.SelectedShift = "A";
        pc2DsmVm.SelectedPump = pc2DsmVm.PumpOptions.First(p => p.PumpId == 1);
        await Task.Delay(100);
        pc2DsmVm.DsmName = "Remote Operator";
        pc2DsmVm.NozzleReadings.Clear();
        pc2DsmVm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 1000, ClosingReading = 1150, Rate = 100, OnRowChanged = pc2DsmVm.RecalculateAll });
        pc2DsmVm.PhonePeMorning = 15000;
        pc2DsmVm.RecalculateAll();
        await pc2DsmVm.SaveEntryCommand.ExecuteAsync(null);
        await SyncAndWaitAsync(computer2.SyncEngine);

        // Computer 1 pulls remote entry
        computer1.ActivateAsCurrentApp();
        await SyncAndWaitAsync(computer1.SyncEngine);
        await Task.Delay(250);

        // ShiftEntries list refreshes, while active unsaved form inputs remain completely intact
        Assert.Single(pc1DsmVm.ShiftEntries);
        Assert.Equal("In Progress Operator", pc1DsmVm.DsmName);
        Assert.Equal(1099.0, pc1DsmVm.NozzleReadings.First(n => n.NozzleNumber == 1).ClosingReading ?? 0.0, precision: 2);
    }

    [Fact]
    public async Task SyncEngine_PerformPull_RaisesNotificationsOnceAfterAllOperationalTablesPersist_AndSkipsOwnEcho()
    {
        var server = new FakeSupabaseServer();
        const string stationId = "MITALI-TEST-ATOMIC";

        using var computer1 = new DesktopNode("PC-01", stationId, server, _tempDirs);
        using var computer2 = new DesktopNode("PC-02", stationId, server, _tempDirs);

        computer1.ActivateAsCurrentApp();
        using var pc1DsmVm = new DsmEntryViewModel(computer1.Services);
        await Task.Delay(100);
        pc1DsmVm.SelectedDate = DateTime.Today;
        pc1DsmVm.SelectedShift = "A";
        pc1DsmVm.SelectedPump = pc1DsmVm.PumpOptions.First(p => p.PumpId == 1);
        await Task.Delay(100);
        pc1DsmVm.DsmName = "PC1 Self Save";
        pc1DsmVm.NozzleReadings.Clear();
        pc1DsmVm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 500, ClosingReading = 600, Rate = 100, OnRowChanged = pc1DsmVm.RecalculateAll });
        pc1DsmVm.PhonePeMorning = 10000;
        pc1DsmVm.RecalculateAll();
        await pc1DsmVm.SaveEntryCommand.ExecuteAsync(null);
        Assert.True(pc1DsmVm.IsSaved);

        int pc1OperationalPulledCount = 0;
        computer1.SyncEngine.OperationalDataPulled += () => pc1OperationalPulledCount++;

        // Computer 1 pushes & pulls its own echo -> must NOT trigger OperationalDataPulled or clear PC1's saved screen
        await SyncAndWaitAsync(computer1.SyncEngine);
        await Task.Delay(150);
        Assert.Equal(0, pc1OperationalPulledCount);
        Assert.True(pc1DsmVm.IsSaved);

        // Now Computer 2 observes OperationalDataPulled ONLY AFTER all child tables (NozzleReadings + TestingEntries + PaymentCollections) are persisted
        int pc2NotificationCount = 0;
        bool sawAllChildTablesAtNotification = false;
        computer2.SyncEngine.OperationalDataPulled += () =>
        {
            pc2NotificationCount++;
            using var checkCtx = computer2.Services.GetRequiredService<FuelProDbContext>();
            sawAllChildTablesAtNotification = checkCtx.DsmEntries.Any()
                && checkCtx.NozzleReadings.Any()
                && checkCtx.PaymentCollections.Any();
        };

        await computer2.SyncEngine.ForceSyncAsync();
        await Task.Delay(150);

        Assert.Equal(1, pc2NotificationCount);
        Assert.True(sawAllChildTablesAtNotification);
    }

    [Fact]
    public async Task CachedViewModels_RetainSyncSubscriptionAcrossNavigation_WithoutDuplicateSubscriptions_AndDisposeOnWindowDispose()
    {
        var server = new FakeSupabaseServer();
        const string stationId = "MITALI-TEST-NAV";

        using var computer1 = new DesktopNode("PC-01", stationId, server, _tempDirs);
        using var computer2 = new DesktopNode("PC-02", stationId, server, _tempDirs);
        computer1.ActivateAsCurrentApp();

        // Avoid DispatcherTimer STA thread requirements by running test inside an STA thread
        Exception? staEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                var mainVm = new MainWindowViewModel();
                mainVm.NavigateToDsmEntryCommand.Execute(null);
                var dsmVm1 = Assert.IsType<DsmEntryViewModel>(mainVm.CurrentView);

                // Navigate to Shift Total, Day Total, and back to DSM Entry repeatedly
                mainVm.NavigateToFinalCalculationCommand.Execute(null);
                var shiftVm1 = Assert.IsType<FinalCalculationViewModel>(mainVm.CurrentView);

                mainVm.NavigateToDayTotalCommand.Execute(null);
                var dayVm1 = Assert.IsType<DayTotalViewModel>(mainVm.CurrentView);

                mainVm.NavigateToDsmEntryCommand.Execute(null);
                var dsmVm2 = Assert.IsType<DsmEntryViewModel>(mainVm.CurrentView);
                mainVm.NavigateToFinalCalculationCommand.Execute(null);
                var shiftVm2 = Assert.IsType<FinalCalculationViewModel>(mainVm.CurrentView);
                mainVm.NavigateToDayTotalCommand.Execute(null);
                var dayVm2 = Assert.IsType<DayTotalViewModel>(mainVm.CurrentView);

                // Cached instances must be preserved across navigation
                Assert.Same(dsmVm1, dsmVm2);
                Assert.Same(shiftVm1, shiftVm2);
                Assert.Same(dayVm1, dayVm2);

                // Verify sync subscription was NOT detached when switching away from FinalCalculation/DayTotal/DsmEntry
                var dsmChangedField = typeof(DsmEntryService).GetField("DsmEntryChanged", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                var handlersBeforeDispose = ((Delegate?)dsmChangedField?.GetValue(null))?.GetInvocationList() ?? Array.Empty<Delegate>();
                Assert.Single(handlersBeforeDispose.Where(d => ReferenceEquals(d.Target, shiftVm1)));
                Assert.Single(handlersBeforeDispose.Where(d => ReferenceEquals(d.Target, dayVm1)));

                // Switching to a non-cached or final Close() disposes cleanly and removes handlers without leaks
                mainVm.Dispose();
                var handlersAfterDispose = ((Delegate?)dsmChangedField?.GetValue(null))?.GetInvocationList() ?? Array.Empty<Delegate>();
                Assert.Empty(handlersAfterDispose.Where(d => ReferenceEquals(d.Target, shiftVm1)));
                Assert.Empty(handlersAfterDispose.Where(d => ReferenceEquals(d.Target, dayVm1)));
            }
            catch (Exception ex)
            {
                staEx = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (staEx != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(staEx).Throw();
        }
        await Task.CompletedTask;
    }
}
