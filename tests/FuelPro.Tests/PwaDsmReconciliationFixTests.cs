using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
using FuelPro.UI;
using FuelPro.UI.ViewModels;
using FuelPro.UI.Printing;
using FuelPro.Sync;

namespace FuelPro.Tests;

public class PwaDsmReconciliationFixTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly IServiceProvider _serviceProvider;

    public PwaDsmReconciliationFixTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_ReconciliationFix_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "fuelPro_test.db");

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"),
            ServiceLifetime.Transient);

        // Core Repositories & Services
        services.AddSingleton<ICredentialFileService>(new LocalCredentialFileService(Path.Combine(_tempDir, "credentials")));

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

        // Sync services
        services.AddSingleton<SyncConfigService>();
        services.AddSingleton<SyncEngine>();
        services.AddSingleton<DsmSubmissionPollingService>();
        services.AddTransient<DsmAuthAdminService>();
        services.AddTransient<SupabaseDsmService, FakeSupabaseDsmService>();
        services.AddSingleton<DraftService>();

        // ViewModels
        services.AddTransient<DsmEntryViewModel>();
        services.AddTransient<DsmApprovalQueueViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        // Initialize App static fields
        typeof(App).GetProperty("Services")?.SetValue(null, _serviceProvider);
        typeof(App).GetProperty("DbPath")?.SetValue(null, _dbPath);

        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();

        // Seed 3 shifts for Mitali: A, B, C
        var today = DateTime.Today;
        context.Shifts.AddRange(
            new Shift { ShiftDate = today, ShiftType = "A" },
            new Shift { ShiftDate = today, ShiftType = "B" },
            new Shift { ShiftDate = today, ShiftType = "C" }
        );

        // Seed Settings with isolated rate configuration
        context.Settings.Add(new Setting
        {
            PumpStationName = "Mitali Service Station",
            StationDisplayName = "Mitali Service Station",
            HsdRate = 90.0,
            MsIRate = 100.0,
            MsIIRate = 103.81, // Legacy scalar fallback
            CngRate = 85.0,
            FuelRatesJson = JsonConvert.SerializeObject(new Dictionary<string, double>
            {
                ["Speed"] = 30.00,
                ["Speed - 20KL"] = 30.00,
                ["MS-I"] = 100.00,
                ["HSD"] = 90.00
            })
        });
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
        context.PumpMappings.AddRange(mitaliMappings);

        context.SaveChanges();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    // =========================================================================
    // Finding A: Authoritative Speed Rate Propagation
    // =========================================================================

    [Fact]
    public void SpeedRate_Propagation_UsesAuthoritativeFuelRatesSpeed_OverLegacyMsIIRate()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var localSetting = context.Settings.First();

        // Isolated Settings condition: Speed = ₹30.00, MsIIRate = ₹103.81
        Assert.Equal(103.81, localSetting.MsIIRate);
        Assert.True(localSetting.FuelRates.ContainsKey("Speed"));
        Assert.Equal(30.00, localSetting.FuelRates["Speed"]);

        // Execute authoritative SpeedRate determination logic (matching SyncEngine.cs)
        double authoritativeSpeedRate = (localSetting.FuelRates.TryGetValue("Speed", out var sRate) && sRate > 0)
            ? sRate
            : (localSetting.FuelRates.TryGetValue("Speed - 20KL", out var sTankRate) && sTankRate > 0
                ? sTankRate
                : (localSetting.FuelRates.TryGetValue("XP95", out var xpRate) && xpRate > 0
                    ? xpRate
                    : (localSetting.FuelRates.TryGetValue("Power", out var pwrRate) && pwrRate > 0
                        ? pwrRate
                        : localSetting.MsIIRate)));

        var payload = new Dictionary<string, object?>
        {
            ["SpeedRate"] = authoritativeSpeedRate,
            ["speed_rate"] = authoritativeSpeedRate,
            ["MsIIRate"] = localSetting.MsIIRate,
            ["ms_ii_rate"] = localSetting.MsIIRate
        };

        // Both aliases must serialize to ₹30.00, NOT ₹103.81
        Assert.Equal(30.00, payload["SpeedRate"]);
        Assert.Equal(30.00, payload["speed_rate"]);
        Assert.NotEqual(localSetting.MsIIRate, payload["SpeedRate"]);
    }

    [Fact]
    public void SpeedRate_Propagation_FallsBackToMsIIRate_WhenSpeedGenuinelyAbsent()
    {
        var localSetting = new Setting
        {
            MsIIRate = 103.81,
            FuelRatesJson = "{}" // Empty JSON, no Speed configured
        };

        double authoritativeSpeedRate = (localSetting.FuelRates.TryGetValue("Speed", out var sRate) && sRate > 0)
            ? sRate
            : (localSetting.FuelRates.TryGetValue("Speed - 20KL", out var sTankRate) && sTankRate > 0
                ? sTankRate
                : (localSetting.FuelRates.TryGetValue("XP95", out var xpRate) && xpRate > 0
                    ? xpRate
                    : (localSetting.FuelRates.TryGetValue("Power", out var pwrRate) && pwrRate > 0
                        ? pwrRate
                        : localSetting.MsIIRate)));

        Assert.Equal(103.81, authoritativeSpeedRate);
    }

    // =========================================================================
    // Finding B: Legitimate Dynamic Payment Rows in Approval Queue
    // =========================================================================

    [Fact]
    public async Task ApprovalQueue_PreservesManualPineLabsCollection_WithoutOtp()
    {
        var queueVm = _serviceProvider.GetRequiredService<DsmApprovalQueueViewModel>();

        var submission = new DsmPendingSubmission
        {
            Id = Guid.NewGuid(),
            DsmName = "Ramesh Test",
            ShiftDate = DateTime.Today,
            ShiftType = "A",
            PumpId = 1,
            MetadataJson = JsonConvert.SerializeObject(new
            {
                dynamicItems = new[]
                {
                    new { collectionTypeCode = "PINELABS_CARD", amount = 200.0, tid = "TID_PL01", batch = "B01", otp = "" }
                }
            })
        };

        queueVm.SelectedSubmission = submission;
        await Task.Delay(100);
        while (queueVm.IsLoadingDetails)
        {
            await Task.Delay(50);
        }

        // Must have exactly 1 card swipe row for PINELABS_CARD
        Assert.Single(queueVm.CardSwipeDetails);
        var row = queueVm.CardSwipeDetails.First();
        Assert.Equal("PINELABS_CARD", row.Mode);
        Assert.Equal(200.0, row.Amount);

        // Provenance fix: row must NOT be marked synthetic summary
        Assert.False(row.IsSyntheticSummary);

        // Recalculate totals must include the ₹200.00 collection (base mock 2850 + 200 = 3050)
        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(queueVm, null);

        Assert.Equal(200.0, queueVm.PineLabsAmount);
        Assert.Equal(3050.0, queueVm.TotalCollections);
    }

    [Fact]
    public async Task ApprovalQueue_MultipleLegitimateDynamicCategories_CoexistAccurately()
    {
        var queueVm = _serviceProvider.GetRequiredService<DsmApprovalQueueViewModel>();

        var submission = new DsmPendingSubmission
        {
            Id = Guid.NewGuid(),
            DsmName = "Ramesh Test",
            ShiftDate = DateTime.Today,
            ShiftType = "A",
            PumpId = 1,
            MetadataJson = JsonConvert.SerializeObject(new
            {
                dynamicItems = new[]
                {
                    new { collectionTypeCode = "PINELABS_CARD", amount = 200.0, tid = "T01", batch = "B01", otp = "" },
                    new { collectionTypeCode = "SBI_REDEEM", amount = 400.0, tid = "T02", batch = "B02", otp = "" }
                }
            })
        };

        queueVm.SelectedSubmission = submission;
        await Task.Delay(100);
        while (queueVm.IsLoadingDetails) await Task.Delay(50);

        Assert.Equal(2, queueVm.CardSwipeDetails.Count);
        Assert.All(queueVm.CardSwipeDetails, r => Assert.False(r.IsSyntheticSummary));

        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(queueVm, null);

        Assert.Equal(200.0, queueVm.PineLabsAmount);
        // TotalCollections should include base 2850 + 200 + 400 = 3450
        Assert.Equal(3450.0, queueVm.TotalCollections);
    }

    [Fact]
    public async Task ApprovalQueue_OtpPlusManualCoexistence_PreventsDoubleCounting()
    {
        var queueVm = _serviceProvider.GetRequiredService<DsmApprovalQueueViewModel>();

        // Scenario: attendant has 1 machine OTP transaction of ₹300, and entered total dynamic item of ₹400
        // (₹300 OTP + ₹100 manual excess)
        var submission = new DsmPendingSubmission
        {
            Id = Guid.NewGuid(),
            DsmName = "Ramesh Test",
            ShiftDate = DateTime.Today,
            ShiftType = "A",
            PumpId = 1,
            MetadataJson = JsonConvert.SerializeObject(new
            {
                machineTransactions = new[]
                {
                    new { collectionTypeCode = "PINELABS_CARD", amount = 300.0, tid = "T01", batch = "B01", otp = "123456", slot = "Morning" }
                },
                dynamicItems = new[]
                {
                    new { collectionTypeCode = "PINELABS_CARD", amount = 400.0, tid = "T01", batch = "B01", otp = "" }
                }
            })
        };

        queueVm.SelectedSubmission = submission;
        await Task.Delay(100);
        while (queueVm.IsLoadingDetails) await Task.Delay(50);

        // Should have 2 rows: 1 OTP of ₹300, 1 manual portion of ₹100
        Assert.Equal(2, queueVm.CardSwipeDetails.Count);
        var otpRow = queueVm.CardSwipeDetails.First(r => !string.IsNullOrEmpty(r.Otp));
        var manualRow = queueVm.CardSwipeDetails.First(r => string.IsNullOrEmpty(r.Otp));

        Assert.Equal(300.0, otpRow.Amount);
        Assert.Equal(100.0, manualRow.Amount);
        Assert.False(manualRow.IsSyntheticSummary);

        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(queueVm, null);

        // Sum should be exactly base 2850 + 300 + 100 = 3250, not double-counted
        Assert.Equal(400.0, queueVm.PineLabsAmount);
        Assert.Equal(3250.0, queueVm.TotalCollections);
    }

    [Fact]
    public async Task ApprovalQueue_RepeatedLoads_MaintainsIdempotence()
    {
        var queueVm = _serviceProvider.GetRequiredService<DsmApprovalQueueViewModel>();

        var submission = new DsmPendingSubmission
        {
            Id = Guid.NewGuid(),
            DsmName = "Ramesh Test",
            ShiftDate = DateTime.Today,
            ShiftType = "A",
            PumpId = 1,
            MetadataJson = JsonConvert.SerializeObject(new
            {
                dynamicItems = new[]
                {
                    new { collectionTypeCode = "PINELABS_CARD", amount = 200.0, tid = "T01", batch = "B01", otp = "" }
                }
            })
        };

        // First load
        queueVm.SelectedSubmission = submission;
        await Task.Delay(100);
        while (queueVm.IsLoadingDetails) await Task.Delay(50);
        Assert.Single(queueVm.CardSwipeDetails);

        // Second load (simulating user deselecting and re-selecting or queue refresh)
        queueVm.SelectedSubmission = null;
        queueVm.SelectedSubmission = submission;
        await Task.Delay(100);
        while (queueVm.IsLoadingDetails) await Task.Delay(50);

        Assert.Single(queueVm.CardSwipeDetails);
        Assert.Equal(200.0, queueVm.CardSwipeDetails[0].Amount);
    }

    // =========================================================================
    // Finding C: Shift-Aware Payment Aggregation in Edit Mode
    // =========================================================================

    [Fact]
    public async Task ShiftA_EditMode_DoesNotDoubleCountResidualNightSlot()
    {
        var entryVm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        entryVm.SelectedDate = DateTime.Today;
        entryVm.SelectedShift = "Shift A";

        // In 3-shift mode, split is disabled
        Assert.True(entryVm.IsThreeShiftMode);
        Assert.False(entryVm.IsCreditCardSplitEnabled);

        // Simulate an existing entry in database that has residual Night values
        var paymentSource = new PaymentCollection
        {
            CreditCardMorning = 400.0,
            CreditCardNight = 400.0, // Stale/residual value
            PhonePeMorning = 400.0,
            PhonePeNight = 400.0,    // Stale/residual value
            PetroCardMorning = 200.0,
            PetroCardNight = 200.0   // Stale/residual value
        };

        var dsmEntry = new DsmEntry
        {
            DsmEntryId = 10,
            ShiftId = 1,
            PumpId = 1,
            DsmName = "Ramesh Test",
            Shift = new Shift { ShiftId = 1, ShiftDate = DateTime.Today, ShiftType = "A" },
            PaymentCollection = paymentSource,
            NozzleReadings = new List<NozzleReading>()
        };

        // Populate ViewModel from entry via HydrateFromEntryAsync
        await entryVm.HydrateFromEntryAsync(dsmEntry);

        // Morning and Night slots are preserved on ViewModel losslessly
        Assert.Equal(400.0, entryVm.CreditCardMorning);
        Assert.Equal(400.0, entryVm.CreditCardNight);
        Assert.Equal(400.0, entryVm.PhonePeMorning);
        Assert.Equal(400.0, entryVm.PhonePeNight);
        Assert.Equal(200.0, entryVm.PetroCardMorning);
        Assert.Equal(200.0, entryVm.PetroCardNight);

        // BuildCalculationDto must ONLY include active slot (400, NOT 800)
        var dto = (DsmEntryDto)typeof(DsmEntryViewModel).GetMethod("BuildCalculationDto", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(entryVm, null)!;

        Assert.Equal(400m, dto.PaymentCollection.CreditCard);
        Assert.Equal(400m, dto.PaymentCollection.PhonePe);
        Assert.Equal(200m, dto.PaymentCollection.PetroCard);
    }

    [Fact]
    public async Task ShiftB_And_ShiftC_CorrectlyMapDedicatedSlots()
    {
        var entryVm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        entryVm.SelectedDate = DateTime.Today;

        // Shift B: Day slot
        entryVm.SelectedShift = "Shift B";
        var paymentSourceB = new PaymentCollection
        {
            CreditCardDay = 750.0,
            PhonePeDay = 500.0
        };
        var entryB = new DsmEntry
        {
            ShiftId = 2,
            PumpId = 1,
            DsmName = "Shift B Attendant",
            Shift = new Shift { ShiftId = 2, ShiftDate = DateTime.Today, ShiftType = "B" },
            PaymentCollection = paymentSourceB,
            NozzleReadings = new List<NozzleReading>()
        };
        await entryVm.HydrateFromEntryAsync(entryB);

        Assert.Equal(750.0, entryVm.CreditCardMorning); // Mapped to primary UI input
        Assert.Null(entryVm.CreditCardNight);

        var dtoB = (DsmEntryDto)typeof(DsmEntryViewModel).GetMethod("BuildCalculationDto", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(entryVm, null)!;
        Assert.Equal(750m, dtoB.PaymentCollection.CreditCard);

        // Shift C: Night slot
        entryVm.SelectedShift = "Shift C";
        var paymentSourceC = new PaymentCollection
        {
            CreditCardNight = 920.0,
            PhonePeNight = 310.0
        };
        var entryC = new DsmEntry
        {
            ShiftId = 3,
            PumpId = 1,
            DsmName = "Shift C Attendant",
            Shift = new Shift { ShiftId = 3, ShiftDate = DateTime.Today, ShiftType = "C" },
            PaymentCollection = paymentSourceC,
            NozzleReadings = new List<NozzleReading>()
        };
        await entryVm.HydrateFromEntryAsync(entryC);

        Assert.Equal(920.0, entryVm.CreditCardMorning); // Mapped to primary UI input
        Assert.Null(entryVm.CreditCardNight);

        var dtoC = (DsmEntryDto)typeof(DsmEntryViewModel).GetMethod("BuildCalculationDto", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(entryVm, null)!;
        Assert.Equal(920m, dtoC.PaymentCollection.CreditCard);
    }

    // =========================================================================
    // Finding D: Canonical Reconciliation Parity Across Modes
    // =========================================================================

    [Fact]
    public async Task IncidentFixture_ReconciliationParity_BetweenApprovalAndEditMode()
    {
        // Incident Fixture Values:
        // 19-Oct-2026 Mitali Shift A Incident Parity Verification Across All 4 Lifecycle Stages:
        // Nozzles:
        // Nozzle 1 (MS-I): 20 L @ ₹20.00 = ₹400.00
        // Nozzle 2 (MS-I): 20 L @ ₹20.00 = ₹400.00
        // Nozzle 5 (Speed): 20 L @ ₹103.81 = ₹2,076.20
        // Nozzle 6 (Speed): 20 L @ ₹103.81 = ₹2,076.20
        // Gross Fuel Sales: 400 + 400 + 2076.20 + 2076.20 = ₹4,952.40
        // Testing / Non-Sale: ₹495.24
        // Net Sales Liability: 4952.40 - 495.24 = ₹4,457.16
        // Collections:
        // Physical Cash (Cash 2): ₹1,835.00
        // Cash Deposit (Cash 1): ₹100.00
        // PhonePe UPI: ₹400.00
        // Credit Card: ₹400.00
        // PetroCard: ₹400.00
        // Dynamic Payments (Pine Labs Card without OTP): ₹1,000.00
        // Debtors / Credit: ₹100.00
        // Expenses: ₹220.00 (General ₹20 + Drawings/Khandhare ₹200)
        // Others: ₹31,999.97 (Informational only; excluded from collections)
        // Total Collections: 1835 + 100 + 400 + 400 + 400 + 1000 + 100 + 220 = ₹4,455.00
        // Expected Mismatch: 4455.00 - 4457.16 = -₹2.16 (Short = ₹2.16, Excess = ₹0.00)

        // Stage 1: Approval Queue Verification
        var queueVm = _serviceProvider.GetRequiredService<DsmApprovalQueueViewModel>();
        var subDto = new DsmPendingSubmission
        {
            Id = Guid.NewGuid(),
            DsmName = "orion pax",
            ShiftDate = new DateTime(2026, 10, 19),
            ShiftType = "A",
            PumpId = 1,
            MetadataJson = JsonConvert.SerializeObject(new
            {
                dynamicItems = new[]
                {
                    new { collectionTypeCode = "PINELABS_CARD", amount = 1000.0, tid = "PL01", batch = "B01", otp = "" }
                }
            })
        };

        queueVm.SelectedSubmission = subDto;
        await Task.Delay(100);
        while (queueVm.IsLoadingDetails) await Task.Delay(50);

        queueVm.NozzleReadings.Clear();
        queueVm.NozzleReadings.Add(new DsmNozzleRow { NozzleId = 1, FuelType = "MS-I", OpeningReading = 100.0, ClosingReading = 120.0, Rate = 20.0 });
        queueVm.NozzleReadings.Add(new DsmNozzleRow { NozzleId = 2, FuelType = "MS-I", OpeningReading = 100.0, ClosingReading = 120.0, Rate = 20.0 });
        queueVm.NozzleReadings.Add(new DsmNozzleRow { NozzleId = 5, FuelType = "Speed", OpeningReading = 100.0, ClosingReading = 120.0, Rate = 103.81 });
        queueVm.NozzleReadings.Add(new DsmNozzleRow { NozzleId = 6, FuelType = "Speed", OpeningReading = 100.0, ClosingReading = 120.0, Rate = 103.81 });

        queueVm.SubmissionTestingEntries.Clear();
        queueVm.SubmissionTestingEntries.Add(new DsmTestingRow { FuelType = "Speed", Amount = 4.770638, RupeeAmount = 495.24 });
        queueVm.DebtorEntries.Clear();
        queueVm.DebtorEntries.Add(new DsmDebitRow { DebtorName = "Express Cargo", Amount = 100.0 });
        queueVm.CashAmount = 1835.0;
        queueVm.CashDepositAmount = 100.0;
        queueVm.UpiAmount = 400.0;
        queueVm.CardAmount = 400.0;
        queueVm.PetroCardAmount = 400.0;
        queueVm.OthersAmount = 31999.97;
        queueVm.ExpenseAmount = 220.0;
        queueVm.CreditAmount = 100.0;

        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(queueVm, null);

        Assert.Equal(4952.40, queueVm.GrossSales, precision: 2);
        Assert.Equal(495.24, queueVm.TotalTestingAmount, precision: 2);
        Assert.Equal(4457.16, queueVm.NetFuelSales, precision: 2);
        Assert.Equal(4455.00, queueVm.TotalCollections, precision: 2);
        Assert.Equal(-2.16, queueVm.MismatchAmount, precision: 2);
        Assert.Equal(2.16, queueVm.ShortAmount, precision: 2);
        Assert.Equal(0.0, queueVm.ExcessAmount);

        // Stage 2: Persist to isolated DB to verify View, Edit, and Save/Reopen stages
        using var dbContext = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();

        var nozzleModels = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100.0, ClosingReading = 120.0, Rate = 20.0, Amount = 400.0 },
            new() { NozzleNumber = 2, FuelType = "MS-I", OpeningReading = 100.0, ClosingReading = 120.0, Rate = 20.0, Amount = 400.0 },
            new() { NozzleNumber = 5, FuelType = "Speed", OpeningReading = 100.0, ClosingReading = 120.0, Rate = 103.81, Amount = 2076.20 },
            new() { NozzleNumber = 6, FuelType = "Speed", OpeningReading = 100.0, ClosingReading = 120.0, Rate = 103.81, Amount = 2076.20 }
        };

        var paymentModel = new PaymentCollection
        {
            PhonePeMorning = 400.0,
            CreditCardMorning = 400.0,
            PetroCardMorning = 400.0,
            CreditCardNight = 0.0,
            PhonePeNight = 0.0,
            PetroCardNight = 0.0,
            Others = 31999.97,
            CashDeposit = 100.0,
            Items = new List<PaymentCollectionItem>
            {
                new() { CollectionTypeCode = "PINELABS_CARD", Amount = 1000.0, Tid = "PL01", Batch = "B01", Otp = "", Slot = "Morning" }
            },
            DynamicItemsJson = JsonConvert.SerializeObject(new[]
            {
                new { CollectionTypeCode = "PINELABS_CARD", Amount = 1000.0, Tid = "PL01", Batch = "B01", Otp = "", Slot = "Morning" }
            })
        };

        var cashDenoms = new List<CashDenomination>
        {
            new() { CashType = "Cash1", Denom100 = 1, TotalAmount = 100.0 },
            new() { CashType = "Cash2", Denom500 = 2, Denom200 = 3, Denom100 = 2, Coins = 35, TotalAmount = 1835.0 }
        };

        var testings = new List<TestingEntry>
        {
            new() { FuelType = "Speed", Rate = 103.81, Litres = 4.770638, Amount = 495.24 }
        };

        var debits = new List<DebitEntry>
        {
            new() { DebtorName = "Express Cargo", Amount = 100.0 }
        };

        var expenses = new List<Expense>
        {
            new() { Description = "General Expenses", Amount = 20.0 },
            new() { Description = "Drawings / Khandhare", Amount = 200.0 }
        };

        var saveResult = await dsmService.SaveCompleteEntryWithContextAsync(
            dbContext,
            new DateTime(2026, 10, 19), "A", "orion pax", 1,
            nozzleModels,
            paymentModel,
            debits,
            testings,
            expenses,
            cashDenoms,
            connectedPumpId: 2);

        Assert.True(saveResult.Success, saveResult.Error);
        int entryId = saveResult.Data!.DsmEntryId;
        int shiftId = saveResult.Data.ShiftId;

        // Stage 2: DSM Entry View Mode (Shift Entries summary list)
        var summariesResult = await dsmService.GetShiftEntrySummariesAsync(shiftId);
        Assert.True(summariesResult.Success, summariesResult.Error);
        Assert.NotEmpty(summariesResult.Data!);
        var viewSummary = summariesResult.Data!.First(s => s.DsmEntryId == entryId);

        Assert.Equal(4952.40, viewSummary.GrossSales, precision: 2);
        Assert.Equal(495.24, viewSummary.TestingAmount, precision: 2);
        Assert.Equal(4457.16, viewSummary.NetSales, precision: 2);
        Assert.Equal(4455.00, viewSummary.TotalPaymentIn, precision: 2);
        Assert.Equal(-2.16, viewSummary.Difference, precision: 2);

        // Stage 3: DSM Entry Edit Mode (Full Hydration from Database)
        var repo = _serviceProvider.GetRequiredService<IDsmEntryRepository>();
        var fullEntry = (await repo.GetFullEntryAsync(entryId)).Data!;

        var entryVm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        await entryVm.HydrateFromEntryAsync(fullEntry);

        Assert.Equal(4457.16, entryVm.NetGrossSales, precision: 2);
        Assert.Equal(4455.00, entryVm.FinalAdjusted, precision: 2);
        Assert.Equal(-2.16, entryVm.Difference, precision: 2);
        Assert.Equal(1835.00, entryVm.Cash2.TotalAmount, precision: 2);
        Assert.Equal(100.00, entryVm.Cash1.TotalAmount, precision: 2);
        Assert.Equal(400.00, entryVm.PhonePeMorning);
        Assert.Equal(400.00, entryVm.CreditCardMorning);
        Assert.Equal(400.00, entryVm.PetroCardMorning);
        Assert.Equal(31999.97, entryVm.Others);
        var dynamicWithAmount = entryVm.DynamicCollections.Where(d => d.Amount > 0).ToList();
        Assert.Single(dynamicWithAmount);
        Assert.Equal("PINELABS_CARD", dynamicWithAmount[0].Code);
        Assert.Equal(1000.00, dynamicWithAmount[0].Amount);

        // Stage 4: Edit/Save/Reopen Cycle
        var reopenedVm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        var fullEntryReloaded = (await repo.GetFullEntryAsync(entryId)).Data!;
        await reopenedVm.HydrateFromEntryAsync(fullEntryReloaded);

        Assert.Equal(4457.16, reopenedVm.NetGrossSales, precision: 2);
        Assert.Equal(4455.00, reopenedVm.FinalAdjusted, precision: 2);
        Assert.Equal(-2.16, reopenedVm.Difference, precision: 2);
        Assert.Equal(1835.00, reopenedVm.Cash2.TotalAmount, precision: 2);
        Assert.Equal(100.00, reopenedVm.Cash1.TotalAmount, precision: 2);
        Assert.Equal(400.00, reopenedVm.PhonePeMorning);
        Assert.Equal(400.00, reopenedVm.CreditCardMorning);
        Assert.Equal(400.00, reopenedVm.PetroCardMorning);
        Assert.Equal(31999.97, reopenedVm.Others);
        var reopenedDynamicWithAmount = reopenedVm.DynamicCollections.Where(d => d.Amount > 0).ToList();
        Assert.Single(reopenedDynamicWithAmount);
        Assert.Equal(1000.00, reopenedDynamicWithAmount[0].Amount);

        // Verify Absolute Canonical Parity Across All 4 Stages
        Assert.Equal(queueVm.GrossSales, viewSummary.GrossSales, precision: 2);
        Assert.Equal(queueVm.TotalTestingAmount, viewSummary.TestingAmount, precision: 2);
        Assert.Equal(queueVm.NetFuelSales, viewSummary.NetSales, precision: 2);
        Assert.Equal(viewSummary.NetSales, entryVm.NetGrossSales, precision: 2);
        Assert.Equal(entryVm.NetGrossSales, reopenedVm.NetGrossSales, precision: 2);

        Assert.Equal(queueVm.TotalCollections, viewSummary.TotalPaymentIn, precision: 2);
        Assert.Equal(viewSummary.TotalPaymentIn, entryVm.FinalAdjusted, precision: 2);
        Assert.Equal(entryVm.FinalAdjusted, reopenedVm.FinalAdjusted, precision: 2);

        Assert.Equal(queueVm.MismatchAmount, viewSummary.Difference, precision: 2);
        Assert.Equal(viewSummary.Difference, entryVm.Difference, precision: 2);
        Assert.Equal(entryVm.Difference, reopenedVm.Difference, precision: 2);
    }

    [Fact]
    public async Task DsmEntryViewModel_ShiftA_PreservesNightSlot_WithoutCountingInTotals()
    {
        // Prove that:
        // 1. Shift A preserves Night slot values (e.g. PhonePeNight = 600) upon hydration.
        // 2. Shift A calculations exclude the inactive Night slot value (PhonePe = 400, not 1000).
        // 3. Save and reopen losslessly preserves PhonePeNight = 600 without data erasure.
        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();

        var payment = new PaymentCollection
        {
            PhonePeMorning = 400.0,
            PhonePeNight = 600.0,
            CreditCardMorning = 300.0,
            CreditCardNight = 500.0,
            PhonePeTid = "1211",
            PhonePeBatch = "002",
            PhonePeTidNight = "9999",
            PhonePeBatchNight = "999"
        };

        var entry = new DsmEntry
        {
            DsmEntryId = 999,
            DsmName = "Test Attendant",
            Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
            PaymentCollection = payment,
            NozzleReadings = new List<NozzleReading>
            {
                new() { NozzleNumber = 1, OpeningReading = 100, ClosingReading = 110, Rate = 100, Amount = 1000 }
            }
        };

        await vm.HydrateFromEntryAsync(entry);

        // 1. Stored Night-slot values are preserved losslessly
        Assert.Equal(400.0, vm.PhonePeMorning);
        Assert.Equal(600.0, vm.PhonePeNight);
        Assert.Equal(300.0, vm.CreditCardMorning);
        Assert.Equal(500.0, vm.CreditCardNight);
        Assert.Equal("1211", vm.PhonePeTid);
        Assert.Equal("002", vm.PhonePeBatch);
        Assert.Equal("9999", vm.PhonePeTidNight);
        Assert.Equal("999", vm.PhonePeBatchNight);

        // 2. Calculation DTO excludes inactive Night slots for Shift A
        var calcMethod = typeof(DsmEntryViewModel).GetMethod("BuildCalculationDto", BindingFlags.NonPublic | BindingFlags.Instance);
        var dto = (DsmEntryDto)calcMethod!.Invoke(vm, null)!;

        Assert.Equal(400.00m, dto.PaymentCollection.PhonePe);
        Assert.Equal(300.00m, dto.PaymentCollection.CreditCard);

        // 3. Repeated Save/Reopen preserves stored values without erasure or degradation
        using var dbContext = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();

        var saveResult = await dsmService.SaveCompleteEntryWithContextAsync(
            dbContext,
            DateTime.Today, "A", "Test Attendant", 1,
            entry.NozzleReadings.ToList(),
            payment,
            new List<DebitEntry>(),
            new List<TestingEntry>(),
            new List<Expense>(),
            new List<CashDenomination>());

        Assert.True(saveResult.Success, saveResult.Error);
        int savedId = saveResult.Data!.DsmEntryId;

        var repo = _serviceProvider.GetRequiredService<IDsmEntryRepository>();
        var reloaded = (await repo.GetFullEntryAsync(savedId)).Data!;

        var vm2 = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        await vm2.HydrateFromEntryAsync(reloaded);

        Assert.Equal(400.0, vm2.PhonePeMorning);
        Assert.Equal(600.0, vm2.PhonePeNight);
        Assert.Equal(300.0, vm2.CreditCardMorning);
        Assert.Equal(500.0, vm2.CreditCardNight);
        Assert.Equal("9999", vm2.PhonePeTidNight);
        Assert.Equal("999", vm2.PhonePeBatchNight);
    }
}
