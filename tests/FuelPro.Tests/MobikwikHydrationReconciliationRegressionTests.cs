using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.UI;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;
using Xunit;

namespace FuelPro.Tests;

public class MobikwikHydrationReconciliationRegressionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly string _tempDir;

    public MobikwikHydrationReconciliationRegressionTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_MobikwikTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "fuelPro.db");

        var services = new ServiceCollection();

        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"),
            ServiceLifetime.Transient);

        services.AddTransient<DbContext>(sp => sp.GetRequiredService<FuelProDbContext>());

        // Repositories
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

        // Services
        services.AddSingleton<AuthService>();
        services.AddTransient<DsmEntryService>();
        services.AddTransient<ShiftCalculationService>();
        services.AddSingleton<IDsmCalculationService, DsmCalculationService>();
        services.AddSingleton<IOwnerCalculationService, OwnerCalculationService>();
        services.AddSingleton<ITidCalculationService, TidCalculationService>();
        services.AddScoped<IShiftAggregationService, ShiftAggregationService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddTransient<IFeatureToggleService, FeatureToggleService>();
        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddTransient<IStationConfigurationService, StationConfigurationService>();
        services.AddTransient<IDuplicateDataInspectionService, DuplicateDataInspectionService>();
        services.AddTransient<DuplicateResolutionService>();
        services.AddTransient<DraftService>();
        services.AddTransient<PrintService>();
        services.AddTransient<DsmEntryViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        typeof(App).GetProperty("Services")?.SetValue(null, _serviceProvider);
        typeof(App).GetProperty("DbPath")?.SetValue(null, _dbPath);

        var testMappings = new List<PumpMapping>
        {
            new() { PumpMappingId = 1, PumpId = 1, NozzleNumber = 2, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 2, PumpId = 1, NozzleNumber = 4, FuelType = "SPEED", IsActive = true },
            new() { PumpMappingId = 3, PumpId = 1, NozzleNumber = 6, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 4, PumpId = 2, NozzleNumber = 1, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 5, PumpId = 2, NozzleNumber = 3, FuelType = "SPEED", IsActive = true },
            new() { PumpMappingId = 6, PumpId = 2, NozzleNumber = 5, FuelType = "MS-I", IsActive = true }
        };
        PumpConfiguration.InitializeFromDb(testMappings);

        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();
        context.PumpMappings.AddRange(testMappings);

        // Seed MOBIKWIK in CollectionTypeMasters if not present
        if (!context.CollectionTypes.Any(c => c.Code == "MOBIKWIK"))
        {
            context.CollectionTypes.Add(new CollectionTypeMaster
            {
                Code = "MOBIKWIK",
                DisplayName = "Mobikwik",
                Category = "Online",
                HasTidBatch = true,
                HasOtp = false,
                DisplayOrder = 7,
                IsActive = true,
                EnableMorning = true,
                EnableDay = true,
                EnableNight = true
            });
        }
        context.SaveChanges();
    }

    public void Dispose()
    {
        PumpConfiguration.ResetToDefaults();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task Test1_Mobikwik_HasTidBatch_BlankTidBatch_HydratesIntoTidTransactions()
    {
        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();

        var payment = new PaymentCollection
        {
            Items = new List<PaymentCollectionItem>
            {
                new()
                {
                    TransactionId = "f44c66cd-0d7c-4ecb-9300-90c0811cc72f",
                    CollectionTypeCode = "MOBIKWIK",
                    Amount = 46112.0,
                    Tid = null,
                    Batch = null,
                    Slot = "Morning",
                    Otp = null
                }
            }
        };

        var entry = new DsmEntry
        {
            DsmEntryId = 345,
            PumpId = 1,
            DsmName = "KARTIK BHOJALE",
            GrossSales = 56413.08m,
            PaymentCollection = payment,
            CashDenominations = new List<CashDenomination>
            {
                new() { CashType = "Cash3", Denom500 = 19, TotalAmount = 9500.0 }
            }
        };

        await vm.HydrateFromEntryAsync(entry);

        var mobiTx = vm.TidTransactions.FirstOrDefault(t => t.CollectionTypeCode == "MOBIKWIK");
        Assert.NotNull(mobiTx);
        Assert.Equal(46112.0, mobiTx.Amount);
        Assert.Equal("f44c66cd-0d7c-4ecb-9300-90c0811cc72f", mobiTx.TransactionId);
        Assert.Null(mobiTx.Tid);
        Assert.Null(mobiTx.Batch);
    }

    [Fact]
    public async Task Test2_HydratedMobikwik_RemainsPresent_AfterLoadDynamicCollectionTypesAsync()
    {
        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();

        var payment = new PaymentCollection
        {
            Items = new List<PaymentCollectionItem>
            {
                new()
                {
                    TransactionId = "f44c66cd-0d7c-4ecb-9300-90c0811cc72f",
                    CollectionTypeCode = "MOBIKWIK",
                    Amount = 46112.0,
                    Tid = null,
                    Batch = null,
                    Slot = "Morning",
                    Otp = null
                }
            }
        };

        var entry = new DsmEntry
        {
            DsmEntryId = 345,
            PumpId = 1,
            DsmName = "KARTIK BHOJALE",
            GrossSales = 56413.08m,
            PaymentCollection = payment,
            CashDenominations = new List<CashDenomination>
            {
                new() { CashType = "Cash3", Denom500 = 19, TotalAmount = 9500.0 }
            }
        };

        await vm.HydrateFromEntryAsync(entry);

        // Simulate subsequent dynamic collection reload (e.g. shift or collection type reload without existing payment)
        await vm.LoadDynamicCollectionTypesAsync();
        vm.RecalculateAll();

        var mobiTx = vm.TidTransactions.FirstOrDefault(t => t.CollectionTypeCode == "MOBIKWIK");
        Assert.NotNull(mobiTx);
        Assert.Equal(46112.0, mobiTx.Amount);
        Assert.Equal(55612.0, vm.FinalAdjusted);
    }

    [Fact]
    public async Task Test3_Mobikwik_NotDuplicated_IntoManualOrDynamicCollections()
    {
        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();

        var payment = new PaymentCollection
        {
            Items = new List<PaymentCollectionItem>
            {
                new()
                {
                    TransactionId = "f44c66cd-0d7c-4ecb-9300-90c0811cc72f",
                    CollectionTypeCode = "MOBIKWIK",
                    Amount = 46112.0,
                    Tid = null,
                    Batch = null,
                    Slot = "Morning",
                    Otp = null
                }
            }
        };

        var entry = new DsmEntry
        {
            DsmEntryId = 345,
            PumpId = 1,
            DsmName = "KARTIK BHOJALE",
            GrossSales = 56413.08m,
            PaymentCollection = payment,
            CashDenominations = new List<CashDenomination>
            {
                new() { CashType = "Cash3", Denom500 = 19, TotalAmount = 9500.0 }
            }
        };

        await vm.HydrateFromEntryAsync(entry);

        // MOBIKWIK must NOT appear in ManualCollections
        var manualMobi = vm.ManualCollections.FirstOrDefault(m => string.Equals(m.Code, "MOBIKWIK", StringComparison.OrdinalIgnoreCase));
        Assert.Null(manualMobi);

        // MOBIKWIK in DynamicCollections must have ManualAmount == null
        var dynMobi = vm.DynamicCollections.FirstOrDefault(d => string.Equals(d.Code, "MOBIKWIK", StringComparison.OrdinalIgnoreCase));
        if (dynMobi != null)
        {
            Assert.Null(dynMobi.ManualAmount);
        }
    }

    [Fact]
    public void Test4_SameCollection_InJsonAndItems_CountedExactlyOnce()
    {
        var payment = new PaymentCollection
        {
            DynamicItemsJson = "[{\"TransactionId\":\"f44c66cd-0d7c-4ecb-9300-90c0811cc72f\",\"CollectionTypeCode\":\"MOBIKWIK\",\"Amount\":46112.0,\"Tid\":null,\"Batch\":null,\"Slot\":\"Morning\",\"Otp\":null}]",
            Items = new List<PaymentCollectionItem>
            {
                new()
                {
                    TransactionId = "f44c66cd-0d7c-4ecb-9300-90c0811cc72f",
                    CollectionTypeCode = "MOBIKWIK",
                    Amount = 46112.0,
                    Tid = null,
                    Batch = null,
                    Slot = "Morning",
                    Otp = null
                }
            }
        };

        // GetAllItems prefers Items when populated, never concatenates
        var allItems = payment.GetAllItems().ToList();
        Assert.Single(allItems);
        Assert.Equal(46112.0, allItems[0].Amount);

        // DynamicItemsTotal calculates exactly ₹46,112.0 once
        Assert.Equal(46112.0, payment.DynamicItemsTotal);
    }

    [Fact]
    public async Task Test5_SuccessfulSave_SynchronizesReconciliationCard_WithSavedResult()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var shift = new Shift { ShiftDate = DateTime.Today, ShiftType = "A" };
        context.Shifts.Add(shift);
        context.SaveChanges();

        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        vm.SelectedDate = shift.ShiftDate;
        vm.SelectedShift = "A";
        vm.DsmName = "KARTIK BHOJALE";

        await vm.LoadSuggestionsAsync();
        vm.SelectedPump = vm.PumpOptions.First(p => p.PumpId == 1);

        // Set up Cash3 = 9500
        vm.Cash3.Denom500 = 19;
        vm.Cash3.TotalAmount = 9500;

        // Add Mobikwik = 46112 via TidTransactions
        vm.TidTransactions.Add(new DsmTidTransactionRow
        {
            TransactionId = Guid.NewGuid().ToString("D"),
            CollectionTypeCode = "MOBIKWIK",
            Amount = 46112.0,
            Slot = "Morning",
            Tid = null,
            Batch = null
        });

        // Set nozzle readings: Nozzle 2 = 16028.54, Nozzle 6 = 32301.32, Nozzle 4 = 0 -> Gross = 48329.86
        foreach (var nr in vm.NozzleReadings)
        {
            if (nr.NozzleNumber == 2) { nr.OpeningReading = 60329.72; nr.ClosingReading = 60492.96; nr.Rate = 98.19; }
            if (nr.NozzleNumber == 4) { nr.OpeningReading = 2894.0; nr.ClosingReading = 2894.0; nr.Rate = 120.64; }
            if (nr.NozzleNumber == 6) { nr.OpeningReading = 21736.4; nr.ClosingReading = 22026.02; nr.Rate = 111.53; }
        }
        vm.RecalculateAll();

        // Execute Save
        await vm.SaveEntryCommand.ExecuteAsync(null);
        Assert.True(vm.IsSaved);
        // Reconciliation Card must immediately agree with saved result
        Assert.Equal(55612.0, vm.FinalAdjusted);
        Assert.True(vm.NetGrossSales > 0);
        Assert.Equal(vm.FinalAdjusted - vm.NetGrossSales, vm.Difference, 2);
    }

    [Fact]
    public async Task Test6_ConfirmedClientArithmetic_ExactMatch()
    {
        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();

        var payment = new PaymentCollection
        {
            Items = new List<PaymentCollectionItem>
            {
                new()
                {
                    TransactionId = "f44c66cd-0d7c-4ecb-9300-90c0811cc72f",
                    CollectionTypeCode = "MOBIKWIK",
                    Amount = 46112.0,
                    Tid = null,
                    Batch = null,
                    Slot = "Morning",
                    Otp = null
                }
            }
        };

        var entry = new DsmEntry
        {
            DsmEntryId = 345,
            PumpId = 1,
            DsmName = "KARTIK BHOJALE",
            GrossSales = 56413.08m,
            PaymentCollection = payment,
            CashDenominations = new List<CashDenomination>
            {
                new() { CashType = "Cash3", Denom500 = 19, TotalAmount = 9500.0 }
            },
            NozzleReadings = new List<NozzleReading>
            {
                new() { NozzleNumber = 2, OpeningReading = 60329.72, ClosingReading = 60492.96, Rate = 98.19, Amount = 16028.5356 },
                new() { NozzleNumber = 4, OpeningReading = 2894.0, ClosingReading = 2894.0, Rate = 120.64, Amount = 0.0 },
                new() { NozzleNumber = 6, OpeningReading = 21736.4, ClosingReading = 22026.02, Rate = 111.53, Amount = 32301.3186 },
                new() { NozzleNumber = 1, OpeningReading = 31685.79, ClosingReading = 31747.61, Rate = 98.19, Amount = 6070.1058 },
                new() { NozzleNumber = 3, OpeningReading = 1459.62, ClosingReading = 1459.62, Rate = 120.64, Amount = 0.0 },
                new() { NozzleNumber = 5, OpeningReading = 6061.88, ClosingReading = 6079.93, Rate = 111.53, Amount = 2013.1165 }
            }
        };

        await vm.HydrateFromEntryAsync(entry);

        // Confirmed arithmetic:
        // Cash: ₹9,500
        // Mobikwik: ₹46,112
        // Total Collection: ₹55,612
        // Net Sales: ₹56,413.08
        // Mismatch: -₹801.08
        Assert.Equal(9500.0, vm.Cash3.TotalAmount);
        Assert.Equal(46112.0, vm.TidTransactions.First(t => t.CollectionTypeCode == "MOBIKWIK").Amount);
        Assert.Equal(55612.0, vm.FinalAdjusted);
        Assert.Equal(56413.08, Math.Round(vm.NetGrossSales, 2));
        Assert.Equal(-801.08, Math.Round(vm.Difference, 2));
    }

    [Fact]
    public async Task Test7_NonZeroTestingDeductions_PreserveDistinction_BetweenGrossSalesAndNetGrossSales()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var shift = new Shift { ShiftDate = DateTime.Today, ShiftType = "A" };
        context.Shifts.Add(shift);
        context.SaveChanges();

        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        vm.SelectedDate = shift.ShiftDate;
        vm.SelectedShift = "A";
        vm.DsmName = "KARTIK BHOJALE";

        await vm.LoadSuggestionsAsync();
        vm.SelectedPump = vm.PumpOptions.First(p => p.PumpId == 1);

        // Set up Cash3 = 9500
        vm.Cash3.Denom500 = 19;
        vm.Cash3.TotalAmount = 9500;

        // Set nozzle readings
        foreach (var nr in vm.NozzleReadings)
        {
            if (nr.NozzleNumber == 2) { nr.OpeningReading = 60329.72; nr.ClosingReading = 60492.96; nr.Rate = 98.19; } // 16028.54
            if (nr.NozzleNumber == 4) { nr.OpeningReading = 2894.0; nr.ClosingReading = 2894.0; nr.Rate = 120.64; }
            if (nr.NozzleNumber == 6) { nr.OpeningReading = 21736.4; nr.ClosingReading = 22026.02; nr.Rate = 111.53; } // 32301.32
        }

        // Set testing deduction: 5 Litres on HSD @ 98.19 = 490.95
        var testRow = vm.TestingRows.FirstOrDefault(t => t.FuelType.Contains("HSD", StringComparison.OrdinalIgnoreCase));
        if (testRow != null)
        {
            testRow.Litres = 5;
            testRow.Rate = 98.19;
        }

        vm.RecalculateAll();

        // Execute Save
        await vm.SaveEntryCommand.ExecuteAsync(null);

        Assert.True(vm.IsSaved);
        // GrossSales must be the full meter gross, while NetGrossSales must be Meter - Testing
        Assert.True(vm.GrossSales > vm.NetGrossSales, "GrossSales must be greater than NetGrossSales when testing exists");
        Assert.Equal(vm.TotalTestingAmount, vm.GrossSales - vm.NetGrossSales, 2);
        Assert.True(vm.HasTesting);
        Assert.False(string.IsNullOrWhiteSpace(vm.MeterGrossSalesSubtitle));
    }
}
