using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;
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
using FuelPro.UI.Helpers;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;

namespace FuelPro.Tests;

public class Batch1OperationalReliabilityTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public Batch1OperationalReliabilityTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_Batch1_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "fuelPro_batch1.db");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    private IServiceProvider BuildTestServices(HttpMessageHandler? handler = null, SyncSettings? syncSettings = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(
            opt => opt.UseSqlite($"Data Source={_dbPath}"),
            ServiceLifetime.Transient);

        services.AddSingleton<ICredentialFileService>(
            new LocalCredentialFileService(Path.Combine(_tempDir, "credentials")));

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
        services.AddTransient<IAgsInventoryService, MockAgsInventoryService>();
        services.AddTransient<IFinancialCalculationService, MockFinancialCalculationService>();
        services.AddTransient<PrintService>();
        services.AddTransient<ExcelExportService>();
        services.AddTransient<IAuditLogService, AuditLogService>();
        services.AddTransient<IDayLockService, DayLockService>();
        services.AddTransient<IFeatureToggleService, FeatureToggleService>();
        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddTransient<IStationConfigurationService, StationConfigurationService>();
        services.AddTransient<IShiftCycleService, ShiftCycleService>();

        services.AddSingleton<SyncConfigService>();
        var httpClient = handler != null ? new HttpClient(handler) : new HttpClient();
        var supabaseHttp = new SupabaseHttpClient(httpClient);
        var defaultSettings = syncSettings ?? new SyncSettings
        {
            SyncEnabled = handler != null,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "MITALI-TEST-01",
            MachineId = "PC-01"
        };
        supabaseHttp.Configure(defaultSettings.SupabaseUrl, defaultSettings.SupabaseApiKey);
        services.AddSingleton(supabaseHttp);
        services.AddSingleton<StationPairingService>();
        services.AddSingleton<SyncEngine>();
        services.AddSingleton<DsmSubmissionPollingService>();
        services.AddTransient<DsmAuthAdminService>();
        services.AddTransient<SupabaseDsmService, FakeSupabaseDsmService>();
        services.AddSingleton<DraftService>();

        services.AddTransient<DsmEntryViewModel>();
        services.AddTransient<DsmApprovalQueueViewModel>();

        var sp = services.BuildServiceProvider();
        typeof(App).GetProperty("Services")?.SetValue(null, sp);
        typeof(App).GetProperty("DbPath")?.SetValue(null, _dbPath);

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            db.Database.EnsureCreated();
            if (!db.Shifts.Any())
            {
                var d1 = DateTime.Today;
                var d2 = new DateTime(2026, 11, 20);
                var d3 = new DateTime(2026, 11, 21);
                foreach (var dt in new[] { d1, d2, d3 })
                {
                    db.Shifts.AddRange(
                        new Shift { ShiftDate = dt, ShiftType = "A" },
                        new Shift { ShiftDate = dt, ShiftType = "B" },
                        new Shift { ShiftDate = dt, ShiftType = "C" }
                    );
                }
            }
            if (!db.Settings.Any())
            {
                db.Settings.Add(new Setting
                {
                    PumpStationName = "Mitali Service Station",
                    StationDisplayName = "Mitali Service Station",
                    HsdRate = 90.0,
                    MsIRate = 100.0,
                    MsIIRate = 105.0,
                    CngRate = 85.0,
                    FuelRatesJson = JsonConvert.SerializeObject(new Dictionary<string, double>
                    {
                        ["Speed"] = 105.0,
                        ["Speed - 20KL"] = 105.0,
                        ["MS-I"] = 100.0,
                        ["HSD"] = 90.0
                    })
                });
            }
            if (!db.PumpMappings.Any())
            {
                var now = DateTime.Now;
                var mitaliMappings = new List<PumpMapping>
                {
                    new() { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now },
                    new() { PumpId = 2, NozzleNumber = 2, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now },
                    new() { PumpId = 4, NozzleNumber = 3, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now },
                    new() { PumpId = 5, NozzleNumber = 4, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now },
                    new() { PumpId = 1, NozzleNumber = 5, FuelType = "Speed", TankName = "Speed - 20KL", IsActive = true, CreatedAt = now },
                    new() { PumpId = 2, NozzleNumber = 6, FuelType = "Speed", TankName = "Speed - 20KL", IsActive = true, CreatedAt = now },
                    new() { PumpId = 4, NozzleNumber = 7, FuelType = "Speed", TankName = "Speed - 20KL", IsActive = true, CreatedAt = now },
                    new() { PumpId = 5, NozzleNumber = 8, FuelType = "Speed", TankName = "Speed - 20KL", IsActive = true, CreatedAt = now },
                    new() { PumpId = 4, NozzleNumber = 9, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true, CreatedAt = now },
                    new() { PumpId = 5, NozzleNumber = 10, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true, CreatedAt = now }
                };
                PumpConfiguration.InitializeFromDb(mitaliMappings);
                db.PumpMappings.AddRange(mitaliMappings);
            }
            db.SaveChanges();
        }
        sp.GetRequiredService<ICollectionTypeService>().GetAllCollectionTypesAsync().GetAwaiter().GetResult();
        _ = sp.GetRequiredService<IFeatureToggleService>().GetAllFeaturesAsync().GetAwaiter().GetResult();
        sp.GetRequiredService<SyncConfigService>().SaveSettingsAsync(defaultSettings).GetAwaiter().GetResult();

        return sp;
    }

    // ════════════════════════════════════════════════════════════════════════
    // ISSUE 1 — Cash1 Denomination Persistence & Dynamic Configuration
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Issue1_CashDenomRow_PreservesScalarTotal_WhenDenomsEmptyOrZeroedWithoutPriorInput()
    {
        var scalarCash1 = new CashDenomRow
        {
            CashType = "Cash1",
            Denom500 = null,
            Denom200 = null,
            Denom100 = null,
            TotalAmount = 5000.0
        };

        Assert.False(scalarCash1.HasDenominationCounts);
        Assert.Equal(5000.0, scalarCash1.TotalAmount, 2);

        // Simulating focus loss writing 0 into empty fields must NOT wipe the scalar total
        scalarCash1.Denom500 = 0;
        scalarCash1.Denom200 = 0;
        scalarCash1.Denom100 = 0;
        scalarCash1.Denom500 = null;
        Assert.Equal(5000.0, scalarCash1.TotalAmount, 2);

        // Once real non-zero denominations are entered, total tracks counts and resets cleanly if cleared
        scalarCash1.Denom500 = 4; // 2000
        Assert.True(scalarCash1.HasDenominationCounts);
        Assert.Equal(2000.0, scalarCash1.TotalAmount, 2);

        scalarCash1.Denom500 = null;
        Assert.False(scalarCash1.HasDenominationCounts);
        Assert.Equal(0.0, scalarCash1.TotalAmount, 2);
    }

    [Fact]
    public async Task Issue1_ApproveSubmission_PreservesCash1Denominations_AndHydratesBackIntoDsmEntry()
    {
        var sp = BuildTestServices();

        var queueVm = sp.GetRequiredService<DsmApprovalQueueViewModel>();
        var sub = new DsmPendingSubmission
        {
            Id = Guid.NewGuid(),
            DsmName = "CASH1 DENOM DSM",
            ShiftDate = new DateTime(2026, 11, 20),
            ShiftType = "A",
            PumpId = 1,
            SubmittedAt = DateTime.UtcNow,
            MetadataJson = JsonConvert.SerializeObject(new
            {
                formatVersion = 3,
                cash1Denominations = new
                {
                    denom500 = 4, // 2000
                    denom200 = 2, // 400
                    denom100 = 1, // 100 -> Total 2500
                    denom50 = 0,
                    denom20 = 0,
                    denom10 = 0,
                    coins = 0
                }
            })
        };

        queueVm.SelectedSubmission = sub;
        await Task.Delay(100);
        while (queueVm.IsLoadingDetails) await Task.Delay(30);

        Assert.Equal(4, queueVm.Cash1.Denom500);
        Assert.Equal(2, queueVm.Cash1.Denom200);
        Assert.Equal(1, queueVm.Cash1.Denom100);
        Assert.Equal(2500.0, queueVm.Cash1.TotalAmount, 2);
        Assert.True(queueVm.ShowCash1DenominationFields);

        queueVm.NozzleReadings.Clear();
        queueVm.NozzleReadings.Add(new DsmNozzleRow { NozzleId = 1, FuelType = "MS-I", OpeningReading = 100.0, ClosingReading = 125.0, Rate = 100.0 });
        queueVm.NozzleReadings.Add(new DsmNozzleRow { NozzleId = 5, FuelType = "Speed", OpeningReading = 50.0, ClosingReading = 50.0, Rate = 105.0 });
        queueVm.SubmissionTestingEntries.Clear();
        queueVm.DebtorEntries.Clear();
        queueVm.SubmissionExpenses.Clear();
        queueVm.KhandhareEntries.Clear();
        queueVm.CashAmount = 0;
        queueVm.UpiAmount = 0;
        queueVm.CardAmount = 0;
        queueVm.PetroCardAmount = 0;
        queueVm.ExpenseAmount = 0;
        queueVm.CreditAmount = 0;
        queueVm.OthersAmount = 0;
        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(queueVm, null);

        await queueVm.ApproveCommand.ExecuteAsync(null);

        using var db = sp.GetRequiredService<FuelProDbContext>();
        var saved = await db.DsmEntries
            .Include(e => e.CashDenominations)
            .Include(e => e.PaymentCollection)
            .FirstOrDefaultAsync(e => e.DsmName == "CASH1 DENOM DSM");
        Assert.True(saved != null, $"ApproveAsync did not save DsmEntry. StatusMessage='{queueVm.StatusMessage}'");
        var cash1Record = saved!.CashDenominations.FirstOrDefault(c => c.CashType == "Cash1");
        Assert.NotNull(cash1Record);
        Assert.Equal(4, cash1Record!.Denom500);
        Assert.Equal(2, cash1Record.Denom200);
        Assert.Equal(1, cash1Record.Denom100);
        Assert.Equal(2500.0, cash1Record.TotalAmount, 2);

        // Reopen in DSM Entry page and verify denominations & total are restored accurately
        var repo = sp.GetRequiredService<IDsmEntryRepository>();
        var fullEntry = (await repo.GetFullEntryAsync(saved.DsmEntryId)).Data!;
        var entryVm = sp.GetRequiredService<DsmEntryViewModel>();
        await entryVm.HydrateFromEntryAsync(fullEntry);

        Assert.Equal(4, entryVm.Cash1.Denom500);
        Assert.Equal(2, entryVm.Cash1.Denom200);
        Assert.Equal(1, entryVm.Cash1.Denom100);
        Assert.Equal(2500.0, entryVm.Cash1.TotalAmount, 2);
        Assert.True(entryVm.ShowCash1DenominationFields);
    }

    [Fact]
    public async Task Issue1_ApproveSubmission_ScalarOnlyCash1_DoesNotManufactureDenoms_AndPreservesTotalOnReopen()
    {
        var sp = BuildTestServices();

        var queueVm = sp.GetRequiredService<DsmApprovalQueueViewModel>();
        var sub = new DsmPendingSubmission
        {
            Id = Guid.NewGuid(),
            DsmName = "CASH1 SCALAR DSM",
            ShiftDate = new DateTime(2026, 11, 21),
            ShiftType = "A",
            PumpId = 1,
            SubmittedAt = DateTime.UtcNow,
            MetadataJson = JsonConvert.SerializeObject(new
            {
                formatVersion = 3,
                cash1Amount = 5000.0,
                cash1Denominations = (object?)null
            })
        };

        queueVm.SelectedSubmission = sub;
        await Task.Delay(100);
        while (queueVm.IsLoadingDetails) await Task.Delay(30);

        queueVm.NozzleReadings.Clear();
        queueVm.NozzleReadings.Add(new DsmNozzleRow { NozzleId = 1, FuelType = "MS-I", OpeningReading = 100.0, ClosingReading = 150.0, Rate = 100.0 });
        queueVm.NozzleReadings.Add(new DsmNozzleRow { NozzleId = 5, FuelType = "Speed", OpeningReading = 50.0, ClosingReading = 50.0, Rate = 105.0 });
        queueVm.SubmissionTestingEntries.Clear();
        queueVm.DebtorEntries.Clear();
        queueVm.SubmissionExpenses.Clear();
        queueVm.KhandhareEntries.Clear();
        queueVm.CashAmount = 0;
        queueVm.CashDepositAmount = 5000.0;
        queueVm.UpiAmount = 0;
        queueVm.CardAmount = 0;
        queueVm.PetroCardAmount = 0;
        queueVm.ExpenseAmount = 0;
        queueVm.CreditAmount = 0;
        queueVm.OthersAmount = 0;
        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(queueVm, null);

        await queueVm.ApproveCommand.ExecuteAsync(null);

        using var db = sp.GetRequiredService<FuelProDbContext>();
        var saved = await db.DsmEntries
            .Include(e => e.CashDenominations)
            .Include(e => e.PaymentCollection)
            .FirstOrDefaultAsync(e => e.DsmName == "CASH1 SCALAR DSM");
        Assert.True(saved != null, $"ApproveAsync did not save DsmEntry. StatusMessage='{queueVm.StatusMessage}'");
        Assert.Equal(5000.0, saved!.PaymentCollection!.CashDeposit, 2);

        var repo = sp.GetRequiredService<IDsmEntryRepository>();
        var fullEntry = (await repo.GetFullEntryAsync(saved.DsmEntryId)).Data!;
        var entryVm = sp.GetRequiredService<DsmEntryViewModel>();
        await entryVm.HydrateFromEntryAsync(fullEntry);

        // Scalar total preserved, denominations NOT manufactured, UI shows scalar input under default config
        Assert.Null(entryVm.Cash1.Denom500);
        Assert.Null(entryVm.Cash1.Denom200);
        Assert.Null(entryVm.Cash1.Denom100);
        Assert.Equal(5000.0, entryVm.Cash1.TotalAmount, 2);
        Assert.True(entryVm.ShowCash1ScalarField);

        // Simulated zero focus event does not destroy scalar total
        entryVm.Cash1.Denom500 = 0;
        Assert.Equal(5000.0, entryVm.Cash1.TotalAmount, 2);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ISSUE 2 — DSM Assignment Conflict Safety, Verification & Lock Serialization
    // ════════════════════════════════════════════════════════════════════════

    private sealed class DelegatingStubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _fn;
        public DelegatingStubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _fn(request, cancellationToken);
    }

    [Fact]
    public async Task Issue2_SyncEngine_409ConflictWithUnknownRemoteAssignment_DoesNotDeactivateRemoteOrMarkSynced()
    {
        bool patchAttempted = false;
        string otherDsmRemoteGuid = Guid.NewGuid().ToString();
        string conflictingRemoteAssignGuid = Guid.NewGuid().ToString();

        var handler = new DelegatingStubHandler(async (req, ct) =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Post && url.Contains("/rest/v1/DsmUsers"))
            {
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("[]") };
            }
            if (req.Method == HttpMethod.Post && url.Contains("/rest/v1/DsmPumpAssignments"))
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("{\"code\":\"23505\",\"message\":\"duplicate key value violates unique constraint \\\"idx_uq_assignments_active\\\"\"}")
                };
            }
            if (req.Method == HttpMethod.Get && url.Contains("/rest/v1/DsmPumpAssignments") && url.Contains("IsActive=eq.true"))
            {
                var remoteRows = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["SyncGuid"] = conflictingRemoteAssignGuid,
                        ["station_id"] = "MITALI-TEST-01",
                        ["machine_id"] = "PC-OTHER",
                        ["DsmUserId"] = otherDsmRemoteGuid,
                        ["PumpId"] = 5,
                        ["ShiftType"] = "A",
                        ["IsActive"] = true,
                        ["AssignedDate"] = SyncTimestampHelper.FormatUtcIso(DateTime.UtcNow.AddMinutes(5)),
                        ["updated_at"] = SyncTimestampHelper.FormatUtcIso(DateTime.UtcNow.AddMinutes(5))
                    }
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(remoteRows), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Patch && url.Contains("DsmPumpAssignments"))
            {
                patchAttempted = true;
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
        });

        var sp = BuildTestServices(handler);
        int localAssignId;
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var user = new DsmUser { FullName = "Nirjala", EmployeeCode = "NIR01", MobileNumber = "9000000001", IsActive = true };
            db.DsmUsers.Add(user);
            await db.SaveChangesAsync();

            var assignment = new DsmPumpAssignment
            {
                DsmUserId = user.DsmUserId,
                PumpId = 5,
                ShiftType = "A",
                IsActive = true,
                AssignedDate = DateTime.Now
            };
            db.DsmPumpAssignments.Add(assignment);
            await db.SaveChangesAsync();
            localAssignId = assignment.DsmPumpAssignmentId;
        }

        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var (ok, err) = await syncEngine.ForceSyncAndVerifyRecordAsync("DsmPumpAssignments", localAssignId);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(err));
        Assert.False(patchAttempted);

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var log = await db.SyncChangeLogs.FirstOrDefaultAsync(l => l.TableName == "DsmPumpAssignments" && l.RecordId == localAssignId && l.Operation == "INSERT");
            Assert.NotNull(log);
            Assert.False(log!.IsSynced);
        }
    }

    [Fact]
    public async Task Issue2_SyncEngine_409ConflictWithVerifiedLocallyDeactivatedAssignment_ReconcilesSafely()
    {
        string? patchedGuid = null;
        bool secondUpsertSucceeded = false;

        var oldGuid = Guid.NewGuid().ToString();
        var handler = new DelegatingStubHandler(async (req, ct) =>
        {
            var url = req.RequestUri!.PathAndQuery;
            var body = req.Content != null ? await req.Content.ReadAsStringAsync(ct) : "";
            if (req.Method == HttpMethod.Post && url.Contains("/rest/v1/DsmUsers"))
            {
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("[]") };
            }
            if (req.Method == HttpMethod.Post && url.Contains("/rest/v1/DsmPumpAssignments"))
            {
                if (patchedGuid == null)
                {
                    return new HttpResponseMessage(HttpStatusCode.Conflict)
                    {
                        Content = new StringContent("{\"code\":\"23505\",\"message\":\"duplicate key idx_uq_assignments_active\"}")
                    };
                }
                secondUpsertSucceeded = true;
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("[]") };
            }
            if (req.Method == HttpMethod.Get && url.Contains("/rest/v1/DsmPumpAssignments") && url.Contains("IsActive=eq.true"))
            {
                if (patchedGuid != null)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
                }
                var remoteRows = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["SyncGuid"] = oldGuid,
                        ["station_id"] = "MITALI-TEST-01",
                        ["machine_id"] = "PC-01",
                        ["PumpId"] = 6,
                        ["ShiftType"] = "B",
                        ["IsActive"] = true,
                        ["AssignedDate"] = SyncTimestampHelper.FormatUtcIso(DateTime.UtcNow.AddHours(-3)),
                        ["updated_at"] = SyncTimestampHelper.FormatUtcIso(DateTime.UtcNow.AddHours(-3))
                    }
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(remoteRows), Encoding.UTF8, "application/json")
                };
            }
            if (req.Method == HttpMethod.Patch && url.Contains("DsmPumpAssignments"))
            {
                patchedGuid = url;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
        });

        var sp = BuildTestServices(handler);
        int newAssignId;
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var user = new DsmUser { FullName = "Rugved", EmployeeCode = "RUG01", MobileNumber = "9000000002", IsActive = true };
            db.DsmUsers.Add(user);
            await db.SaveChangesAsync();

            var oldAssign = new DsmPumpAssignment
            {
                DsmUserId = user.DsmUserId,
                PumpId = 6,
                ShiftType = "B",
                IsActive = false,
                AssignedDate = DateTime.Now.AddHours(-3)
            };
            db.DsmPumpAssignments.Add(oldAssign);
            await db.SaveChangesAsync();

            db.SyncIdMappings.Add(new SyncIdMapping
            {
                TableName = "DsmPumpAssignments",
                LocalId = oldAssign.DsmPumpAssignmentId,
                RemoteGuid = oldGuid
            });

            var newAssign = new DsmPumpAssignment
            {
                DsmUserId = user.DsmUserId,
                PumpId = 6,
                ShiftType = "B",
                IsActive = true,
                AssignedDate = DateTime.Now
            };
            db.DsmPumpAssignments.Add(newAssign);
            await db.SaveChangesAsync();
            newAssignId = newAssign.DsmPumpAssignmentId;

            // Clear old deactivation log so we specifically test the 409 on active upsert
            var oldLog = await db.SyncChangeLogs.FirstOrDefaultAsync(l => l.TableName == "DsmPumpAssignments" && l.RecordId == oldAssign.DsmPumpAssignmentId);
            if (oldLog != null)
            {
                oldLog.IsSynced = true;
                await db.SaveChangesAsync();
            }
        }

        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var (ok, err) = await syncEngine.ForceSyncAndVerifyRecordAsync("DsmPumpAssignments", newAssignId);

        Assert.True(ok, err);
        Assert.NotNull(patchedGuid);
        Assert.Contains(oldGuid, patchedGuid!, StringComparison.OrdinalIgnoreCase);
        Assert.True(secondUpsertSucceeded);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ISSUE 3 — Historical Collection Retrieval, Legacy Fallback, Shift Include & Exports
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Issue3_GetEntriesForShiftAsync_IncludesShift_PreservingDaySlotCollectionsAndShiftActivation()
    {
        var sp = BuildTestServices();
        int shiftBId;
        var shiftDate = new DateTime(2026, 11, 18);

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var shiftB = new Shift { ShiftDate = shiftDate, ShiftType = "B" };
            db.Shifts.Add(shiftB);
            await db.SaveChangesAsync();
            shiftBId = shiftB.ShiftId;

            var entry = new DsmEntry
            {
                ShiftId = shiftBId,
                PumpId = 1,
                DsmName = "Shift B Historical",
                GrossSales = 2700m,
                TotalInDirect = 2700m,
                TotalCollection = 2700m,
                PaymentCollection = new PaymentCollection
                {
                    PhonePeDay = 1500.0,
                    CreditCardDay = 800.0,
                    PetroCardDay = 400.0
                }
            };
            db.DsmEntries.Add(entry);
            await db.SaveChangesAsync();
        }

        var entryRepo = sp.GetRequiredService<IDsmEntryRepository>();
        var loadedResult = await entryRepo.GetEntriesForShiftAsync(shiftBId);
        Assert.True(loadedResult.Success);
        var loaded = loadedResult.Data!;
        Assert.Single(loaded);
        Assert.NotNull(loaded[0].Shift);
        Assert.Equal("B", loaded[0].Shift!.ShiftType);

        var reportService = sp.GetRequiredService<IReportService>();
        var shiftReport = reportService.CalculateShiftReport(
            shiftDate,
            "B",
            loaded,
            new(),
            new(),
            new(),
            90, 103, 105, 85,
            new BusinessDayTidSheet(),
            new BusinessDayTidSheet(),
            "Mitali");
        Assert.Single(shiftReport.DsmSummaryRows);
        var row = shiftReport.DsmSummaryRows[0];
        Assert.Equal(1500.0, row.PhonePeTotal, 2);
        Assert.Equal(800.0, row.CreditCardTotal, 2);
        Assert.Equal(400.0, row.PetroCardTotal, 2);
    }

    [Fact]
    public async Task Issue3_SyncEngine_PullLegacyPetroCard_BackfillsZeroPaymentCollection_NeverOverwritesNonZero()
    {
        var entryGuid = Guid.NewGuid().ToString();
        var entry2Guid = Guid.NewGuid().ToString();
        var pcGuid = Guid.NewGuid().ToString();
        var shiftGuid = Guid.NewGuid().ToString();
        var existingNonZeroPcGuid = Guid.NewGuid().ToString();
        var fixedUpdatedAt = SyncTimestampHelper.FormatUtcIso(DateTime.UtcNow.AddMinutes(-1));

        var handler = new DelegatingStubHandler((req, ct) =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Get && url.Contains("/rest/v1/PaymentCollections"))
            {
                var remoteRows = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["SyncGuid"] = pcGuid,
                        ["station_id"] = "MITALI-TEST-01",
                        ["DsmEntryId"] = entryGuid,
                        ["PetroCard"] = 600.0,
                        ["CreditCard"] = 700.0,
                        ["PhonePe"] = 1000.0,
                        ["PetroCardMorning"] = 0.0,
                        ["PetroCardDay"] = 0.0,
                        ["PetroCardNight"] = 0.0,
                        ["CreditCardMorning"] = 0.0,
                        ["CreditCardDay"] = 0.0,
                        ["CreditCardNight"] = 0.0,
                        ["PhonePeMorning"] = 0.0,
                        ["PhonePeDay"] = 0.0,
                        ["PhonePeNight"] = 0.0,
                        ["updated_at"] = fixedUpdatedAt
                    },
                    new Dictionary<string, object?>
                    {
                        ["SyncGuid"] = existingNonZeroPcGuid,
                        ["station_id"] = "MITALI-TEST-01",
                        ["DsmEntryId"] = entry2Guid,
                        ["CashDeposit"] = 0.0, // incomplete zero on cloud must NOT wipe valid non-zero local 1200!
                        ["updated_at"] = fixedUpdatedAt
                    }
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(remoteRows), Encoding.UTF8, "application/json")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        });

        var sp = BuildTestServices(handler);
        int zeroPcId;
        int preservedPcId;

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var shift = new Shift { ShiftDate = new DateTime(2026, 10, 1), ShiftType = "B" };
            db.Shifts.Add(shift);
            await db.SaveChangesAsync();

            var entry1 = new DsmEntry { ShiftId = shift.ShiftId, PumpId = 1, DsmName = "Legacy Zero PC2", TotalInDirect = 2300m, TotalCollection = 2300m };
            var entry2 = new DsmEntry { ShiftId = shift.ShiftId, PumpId = 2, DsmName = "Valid NonZero PC2", TotalInDirect = 1200m, TotalCollection = 1200m };
            db.DsmEntries.AddRange(entry1, entry2);
            await db.SaveChangesAsync();

            var zeroPc = new PaymentCollection { DsmEntryId = entry1.DsmEntryId };
            var validPc = new PaymentCollection { DsmEntryId = entry2.DsmEntryId, CashDeposit = 1200.0 };
            db.PaymentCollections.AddRange(zeroPc, validPc);
            await db.SaveChangesAsync();

            zeroPcId = zeroPc.PaymentId;
            preservedPcId = validPc.PaymentId;

            db.SyncIdMappings.AddRange(
                new SyncIdMapping { TableName = "Shifts", LocalId = shift.ShiftId, RemoteGuid = shiftGuid },
                new SyncIdMapping { TableName = "DsmEntries", LocalId = entry1.DsmEntryId, RemoteGuid = entryGuid },
                new SyncIdMapping { TableName = "DsmEntries", LocalId = entry2.DsmEntryId, RemoteGuid = entry2Guid },
                new SyncIdMapping { TableName = "PaymentCollections", LocalId = zeroPcId, RemoteGuid = pcGuid },
                new SyncIdMapping { TableName = "PaymentCollections", LocalId = preservedPcId, RemoteGuid = existingNonZeroPcGuid });

            // Mark all generated local logs synced so ForceSync runs Pull + targeted backfill cleanly
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE SyncChangeLogs SET IsSynced = 1");
        }

        var syncEngine = sp.GetRequiredService<SyncEngine>();
        await syncEngine.ForceSyncAsync();

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var restored = await db.PaymentCollections.FirstAsync(p => p.PaymentId == zeroPcId);
            Assert.Equal(600.0, restored.PetroCardDay, 2);
            Assert.Equal(700.0, restored.CreditCardDay, 2);
            Assert.Equal(1000.0, restored.PhonePeDay, 2);

            var preserved = await db.PaymentCollections.FirstAsync(p => p.PaymentId == preservedPcId);
            Assert.Equal(1200.0, preserved.CashDeposit, 2);
        }
    }

    [Fact]
    public async Task Issue3_ExcelExport_ShiftAndDayTotal_IncludeDynamicCollectionsAndPhonePeCard()
    {
        var sp = BuildTestServices();
        var excelService = sp.GetRequiredService<ExcelExportService>();

        var rows = new List<DsmSummaryRowDto>
        {
            new()
            {
                DsmName = "DSM Dynamic & PhonePeCard",
                Shift = "B",
                PumpId = 2,
                PhonePeDay = 900,
                PhonePeCardDay = 300,
                CreditCardDay = 400,
                PineLabsCard = 1100,
                DynamicCollections = new Dictionary<string, double>
                {
                    ["PINELABS_CARD"] = 1100,
                    ["SBI_REDEEM"] = 250
                },
                DynamicCollectionsTotal = 1350,
                CashDeposit = 500,
                CashInHand = 600,
                GrossSales = 4050
            }
        };

        var totals = new DsmSummaryRowDto
        {
            DsmName = "TOTAL",
            PhonePeDay = 900,
            PhonePeCardDay = 300,
            CreditCardDay = 400,
            PineLabsCard = 1100,
            DynamicCollectionsTotal = 1350,
            CashDeposit = 500,
            CashInHand = 600,
            GrossSales = 4050
        };

        var shiftPath = Path.Combine(_tempDir, "ShiftTotal.xlsx");
        var dayPath = Path.Combine(_tempDir, "DayTotal.xlsx");

        await excelService.ExportShiftTotalReportAsync(new ShiftReportDto
        {
            DateString = "28 Nov 2026",
            ShiftLabel = "II",
            ManagerName = "Mgr",
            StationName = "Mitali",
            DsmSummaryRows = rows,
            DsmSummaryTotals = totals
        }, shiftPath);

        await excelService.ExportDayTotalReportAsync(new DayReportDto
        {
            DateString = "28 Nov 2026",
            StationName = "Mitali",
            DsmSummaryRows = rows,
            DsmSummaryTotals = totals
        }, dayPath);

        using (var wb = new XLWorkbook(shiftPath))
        {
            var ws = wb.Worksheets.First();
            var allCellStrings = ws.CellsUsed().Select(c => c.GetString()).ToList();
            Assert.Contains("PINELABS CARD", allCellStrings);
            Assert.Contains("SBI REDEEM", allCellStrings);
            Assert.Contains(ws.CellsUsed().Where(c => c.DataType == XLDataType.Number).Select(c => c.GetDouble()), v => Math.Abs(v - 1100.0) < 0.01);
            Assert.Contains(ws.CellsUsed().Where(c => c.DataType == XLDataType.Number).Select(c => c.GetDouble()), v => Math.Abs(v - 900.0) < 0.01);
            Assert.Contains(ws.CellsUsed().Where(c => c.DataType == XLDataType.Number).Select(c => c.GetDouble()), v => Math.Abs(v - 700.0) < 0.01);
        }

        using (var wb = new XLWorkbook(dayPath))
        {
            var ws = wb.Worksheets.First();
            var allCellStrings = ws.CellsUsed().Select(c => c.GetString()).ToList();
            Assert.Contains("PINELABS CARD", allCellStrings);
            Assert.Contains("SBI REDEEM", allCellStrings);
            Assert.Contains(ws.CellsUsed().Where(c => c.DataType == XLDataType.Number).Select(c => c.GetDouble()), v => Math.Abs(v - 250.0) < 0.01);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // ISSUE 4 — Global Desktop DatePicker Format (DD/MM/YYYY)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Issue4_DatePickerFormatHelper_FormatsAndParsesDdMmYyyy_UnderEnUsCulture_WithoutAlteringNumericCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        Exception? staException = null;
        string? dpLanguageTag = null;

        try
        {
            var usCulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentCulture = usCulture;
            Thread.CurrentThread.CurrentUICulture = usCulture;

            var targetDate = new DateTime(2026, 11, 5); // 05 Nov 2026 (ambiguous in MM/DD vs DD/MM)
            Assert.Equal("05/11/2026", DatePickerFormatHelper.FormatDate(targetDate));

            Assert.True(DatePickerFormatHelper.TryParseDateText("05/11/2026", out var parsed));
            Assert.Equal(2026, parsed.Year);
            Assert.Equal(11, parsed.Month);
            Assert.Equal(5, parsed.Day);

            // Numeric parsing / currency formatting must remain untouched on the caller thread
            Assert.Equal(1234.56, double.Parse("1234.56", CultureInfo.CurrentCulture), 2);

            var t = new Thread(() =>
            {
                try
                {
                    Thread.CurrentThread.CurrentCulture = usCulture;
                    DatePickerFormatHelper.RegisterGlobalFormat();
                    var dp = new DatePicker { SelectedDate = targetDate };
                    DatePickerFormatHelper.ApplyTo(dp);
                    dpLanguageTag = dp.Language.IetfLanguageTag;
                    Assert.Equal("en-gb", dpLanguageTag, ignoreCase: true);
                    Assert.Equal(targetDate, dp.SelectedDate);
                }
                catch (Exception ex)
                {
                    staException = ex;
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();

            Assert.Null(staException);
            Assert.Equal("en-gb", dpLanguageTag, ignoreCase: true);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
            Thread.CurrentThread.CurrentUICulture = originalUiCulture;
        }
    }
}
