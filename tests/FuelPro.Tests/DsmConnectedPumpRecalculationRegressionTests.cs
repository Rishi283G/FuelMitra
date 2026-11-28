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
using FuelPro.Data.AppMigration;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.UI;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;
using Xunit;

namespace FuelPro.Tests;

public class DsmConnectedPumpRecalculationRegressionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly string _tempDir;

    public DsmConnectedPumpRecalculationRegressionTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_DsmRecalcTests_" + Guid.NewGuid().ToString("N"));
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

        // Configure pumps 1-8 for test harness (1 nozzle per pump default in test suite)
        var testMappings = new List<PumpMapping>
        {
            new() { PumpMappingId = 1, PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 2, PumpId = 2, NozzleNumber = 3, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 3, PumpId = 3, NozzleNumber = 5, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 4, PumpId = 4, NozzleNumber = 7, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 5, PumpId = 5, NozzleNumber = 9, FuelType = "SPEED", IsActive = true },
            new() { PumpMappingId = 6, PumpId = 6, NozzleNumber = 10, FuelType = "SPEED", IsActive = true },
            new() { PumpMappingId = 7, PumpId = 7, NozzleNumber = 11, FuelType = "CNG", IsActive = true },
            new() { PumpMappingId = 8, PumpId = 8, NozzleNumber = 12, FuelType = "CNG", IsActive = true }
        };
        PumpConfiguration.InitializeFromDb(testMappings);

        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();
        context.PumpMappings.AddRange(testMappings);
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
    public void Test_PetroCard_ScalarPreferred_WhenPopulated()
    {
        // When scalar is populated, it takes precedence and dynamic items are not duplicated
        var payment = new PaymentCollection
        {
            PetroCardMorning = 5000.0,
            PetroCardDay = 2737.80,
            DynamicItemsJson = "[{\"CollectionTypeCode\":\"PETROCARD\",\"Amount\":7737.80,\"Slot\":\"Day\"},{\"CollectionTypeCode\":\"MOBIKWIK\",\"Amount\":1000.0,\"Slot\":\"Day\"}]"
        };

        Assert.Equal(7737.80, payment.PetroCard, 2);
        Assert.Equal(1000.0, payment.DynamicItemsTotal, 2); // Excludes PETROCARD
        Assert.Equal(8737.80, payment.TotalDigitalPayments, 2); // PetroCard (7737.80) + MobiKwik (1000.0)
    }

    [Fact]
    public void Test_PetroCard_DynamicExtracted_WhenScalarZero()
    {
        // When scalar is 0, extracts matching items from DynamicItemsJson exactly once
        var payment = new PaymentCollection
        {
            PetroCardMorning = 0,
            PetroCardDay = 0,
            PetroCardNight = 0,
            DynamicItemsJson = "[{\"CollectionTypeCode\":\"PETROCARD\",\"Amount\":474.0,\"Slot\":\"Day\"},{\"CollectionTypeCode\":\"PETROCARD\",\"Amount\":300.0,\"Slot\":\"Day\"},{\"CollectionTypeCode\":\"PETROCARD\",\"Amount\":5000.0,\"Slot\":\"Day\"},{\"CollectionTypeCode\":\"PETROCARD\",\"Amount\":1963.8,\"Slot\":\"Day\"},{\"CollectionTypeCode\":\"MOBIKWIK\",\"Amount\":114768.75,\"Slot\":\"Day\"},{\"CollectionTypeCode\":\"SBI_REDEEM\",\"Amount\":990.0,\"Slot\":\"Day\"}]"
        };

        double expectedPetro = 474.0 + 300.0 + 5000.0 + 1963.8; // 7737.80
        double expectedDynamic = 114768.75 + 990.0; // 115758.75

        Assert.Equal(expectedPetro, payment.PetroCard, 2);
        Assert.Equal(expectedDynamic, payment.DynamicItemsTotal, 2); // Excludes PETROCARD
        Assert.Equal(expectedPetro + expectedDynamic, payment.TotalDigitalPayments, 2);
    }

    [Fact]
    public async Task Test_LegitimateZeroCollectionEntry_PreservesZeroCollectionAndCalculatesAccurateMismatch()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 11, 7);
        var shiftType = "A";

        var nozzles = new List<NozzleReading>
        {
            new() { NozzleNumber = 5, OpeningReading = 1000, ClosingReading = 1100, Rate = 100 } // Gross = 10,000
        };

        var emptyPayment = new PaymentCollection();
        var emptyDebits = new List<DebitEntry>();
        var emptyTesting = new List<TestingEntry>();
        var emptyExpenses = new List<Expense>();
        var emptyCash = new List<CashDenomination>();

        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var result = await dsmService.SaveCompleteEntryWithContextAsync(
            context, date, shiftType, "Test Zero DSM", 3,
            nozzles, emptyPayment, emptyDebits, emptyTesting, emptyExpenses, emptyCash);

        Assert.True(result.Success);
        var entry = result.Data!;

        Assert.Equal(10000m, entry.GrossSales);
        Assert.Equal(0m, entry.TotalInDirect);
        Assert.Equal(0m, entry.TotalCreditors);
        Assert.Equal(0m, entry.TotalCollection);
        Assert.Equal(-10000m, entry.Mismatch); // 0 - 10000 = -10000
    }

    [Fact]
    public async Task Test_ConnectedPumpResave_PreservesPrimaryAndSlaveRelationships()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 11, 7);
        var shiftType = "A";

        var nozzles = new List<NozzleReading>
        {
            new() { NozzleNumber = 5, OpeningReading = 100, ClosingReading = 200, Rate = 100 }, // Pump 3 = 10,000
            new() { NozzleNumber = 7, OpeningReading = 200, ClosingReading = 250, Rate = 100 }  // Pump 4 = 5,000
        };

        var payment = new PaymentCollection
        {
            CashDeposit = 15000
        };

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var saveResult = await dsmService.SaveCompleteEntryWithContextAsync(
                context, date, shiftType, "Rahul Shingvi", 3,
                nozzles, payment, new List<DebitEntry>(), new List<TestingEntry>(),
                new List<Expense>(), new List<CashDenomination>(),
                connectedPumpId: 4, connectedPumpIds: new List<int> { 4 });

            Assert.True(saveResult.Success);
            var primary = saveResult.Data!;

            Assert.Equal(3, primary.PumpId);
            Assert.Equal(4, primary.ConnectedPumpId);
            Assert.Null(primary.ReconciledToPumpId);
            Assert.Equal(15000m, primary.GrossSales); // 10,000 + 5,000
            Assert.Equal(15000m, primary.TotalCollection);
            Assert.Equal(0m, primary.Mismatch);
        }

        // Verify slave entry
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var primaryEntry = await context.DsmEntries.FirstAsync(e => e.PumpId == 3 && !e.ReconciledToPumpId.HasValue);
            var slaveEntry = await context.DsmEntries.FirstOrDefaultAsync(e => e.PumpId == 4 && e.ReconciledToPumpId == primaryEntry.DsmEntryId);

            Assert.NotNull(slaveEntry);
            Assert.Equal(4, slaveEntry.PumpId);
            Assert.Equal(primaryEntry.DsmEntryId, slaveEntry.ReconciledToPumpId);
            Assert.Equal(5000m, slaveEntry.GrossSales);
            Assert.Equal(0m, slaveEntry.TotalCollection);
            Assert.Equal(0m, slaveEntry.Mismatch);
        }
    }

    [Fact]
    public async Task Test_RepeatedConnectedPumpResaves_DoNotProgressivelyAlterFinancialTotals()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 11, 7);
        var shiftType = "A";

        var nozzles = new List<NozzleReading>
        {
            new() { NozzleNumber = 5, OpeningReading = 100, ClosingReading = 200, Rate = 100 }, // Pump 3 = 10,000
            new() { NozzleNumber = 7, OpeningReading = 200, ClosingReading = 250, Rate = 100 }  // Pump 4 = 5,000
        };

        var payment = new PaymentCollection { CashDeposit = 15000 };

        DsmEntry firstPassEntry;
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var res1 = await dsmService.SaveCompleteEntryWithContextAsync(
                context, date, shiftType, "Rahul Shingvi", 3,
                nozzles, payment, new List<DebitEntry>(), new List<TestingEntry>(),
                new List<Expense>(), new List<CashDenomination>(),
                connectedPumpId: 4, connectedPumpIds: new List<int> { 4 });
            Assert.True(res1.Success);
            firstPassEntry = res1.Data!;
        }

        // Second pass: resave existing entry
        DsmEntry secondPassEntry;
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var res2 = await dsmService.SaveCompleteEntryWithContextAsync(
                context, date, shiftType, "Rahul Shingvi", 3,
                nozzles, payment, new List<DebitEntry>(), new List<TestingEntry>(),
                new List<Expense>(), new List<CashDenomination>(),
                connectedPumpId: 4, existingEntryId: firstPassEntry.DsmEntryId, connectedPumpIds: new List<int> { 4 });
            Assert.True(res2.Success);
            secondPassEntry = res2.Data!;
        }

        // Third pass: resave existing entry
        DsmEntry thirdPassEntry;
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var res3 = await dsmService.SaveCompleteEntryWithContextAsync(
                context, date, shiftType, "Rahul Shingvi", 3,
                nozzles, payment, new List<DebitEntry>(), new List<TestingEntry>(),
                new List<Expense>(), new List<CashDenomination>(),
                connectedPumpId: 4, existingEntryId: firstPassEntry.DsmEntryId, connectedPumpIds: new List<int> { 4 });
            Assert.True(res3.Success);
            thirdPassEntry = res3.Data!;
        }

        Assert.Equal(firstPassEntry.GrossSales, secondPassEntry.GrossSales);
        Assert.Equal(secondPassEntry.GrossSales, thirdPassEntry.GrossSales);
        Assert.Equal(firstPassEntry.TotalCollection, secondPassEntry.TotalCollection);
        Assert.Equal(secondPassEntry.TotalCollection, thirdPassEntry.TotalCollection);
        Assert.Equal(firstPassEntry.Mismatch, secondPassEntry.Mismatch);
        Assert.Equal(secondPassEntry.Mismatch, thirdPassEntry.Mismatch);
    }

    [Fact]
    public async Task Test_IncompleteChildData_DoesNotOverwritePreviouslyValidFinancialSummaries()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 11, 7);
        var shiftType = "A";

        // Setup an existing entry with valid collections
        int primaryId;
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var shift = new Shift { ShiftDate = date, ShiftType = shiftType, IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var entry = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                DsmName = "Rahul Shingvi",
                PumpId = 3,
                ConnectedPumpId = 4,
                ConnectedPumpIdsJson = "[4]",
                GrossSales = 154337.39m,
                TotalInDirect = 126136.55m,
                TotalCreditors = 18901.57m,
                TotalCollection = 154338.12m,
                Mismatch = 0.73m
            };
            context.DsmEntries.Add(entry);
            await context.SaveChangesAsync();
            primaryId = entry.DsmEntryId;

            // Add child records that exist in database
            context.DebitEntries.Add(new DebitEntry { DsmEntryId = primaryId, DebtorName = "SAIRAJ TRANSPORT", Amount = 18901.57 });
            context.Expenses.Add(new Expense { DsmEntryId = primaryId, Description = "Fuel", Amount = 9300.0 });
            await context.SaveChangesAsync();
        }

        // Now simulate a connected-pump resave where empty child collections are supplied but positive records exist
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var nozzles = new List<NozzleReading>
            {
                new() { NozzleNumber = 5, OpeningReading = 100, ClosingReading = 1543.3739, Rate = 100 }, // ~144337.39
                new() { NozzleNumber = 7, OpeningReading = 100, ClosingReading = 200, Rate = 100 }         // 10000
            };

            // Call SaveCompleteEntryWithContextAsync with positive debits & expenses matching database
            var debits = new List<DebitEntry> { new() { DebtorName = "SAIRAJ TRANSPORT", Amount = 18901.57 } };
            var expenses = new List<Expense> { new() { Description = "Fuel", Amount = 9300.0 } };

            var res = await dsmService.SaveCompleteEntryWithContextAsync(
                context, date, shiftType, "Rahul Shingvi", 3,
                nozzles, new PaymentCollection(), debits, new List<TestingEntry>(), expenses,
                new List<CashDenomination>(), connectedPumpId: 4, existingEntryId: primaryId,
                connectedPumpIds: new List<int> { 4 });

            Assert.True(res.Success);
            var updated = res.Data!;

            // TotalCollection must NOT be zero!
            Assert.True(updated.TotalCollection > 0, "TotalCollection should not be zeroed out when child data exists.");
            Assert.Equal(18901.57m, updated.TotalCreditors);
        }
    }

    [Fact]
    public async Task Test_RahulScenario_ExactNumericalValues_ProducesPositive0_73Mismatch()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 11, 7);
        var shiftType = "A";

        // Exact Pump 3 (Nozzle 5, 6) + Pump 4 (Nozzle 7, 8) readings totaling Gross = 154,337.39
        var nozzles = new List<NozzleReading>
        {
            new() { NozzleNumber = 5, OpeningReading = 1000.0, ClosingReading = 1941.2457, Rate = 100.0 }, // 94,124.57
            new() { NozzleNumber = 7, OpeningReading = 2000.0, ClosingReading = 2602.1282, Rate = 100.0 }  // 60,212.82
            // Total Gross = 154,337.39
        };

        var dynamicJson = "[{\"CollectionTypeCode\":\"PETROCARD\",\"Amount\":474.0,\"Slot\":\"Day\"},{\"CollectionTypeCode\":\"PETROCARD\",\"Amount\":300.0,\"Slot\":\"Day\"},{\"CollectionTypeCode\":\"PETROCARD\",\"Amount\":5000.0,\"Slot\":\"Day\"},{\"CollectionTypeCode\":\"PETROCARD\",\"Amount\":1963.8,\"Slot\":\"Day\"},{\"CollectionTypeCode\":\"MOBIKWIK\",\"Amount\":114768.75,\"Slot\":\"Day\"},{\"CollectionTypeCode\":\"SBI_REDEEM\",\"Amount\":990.0,\"Slot\":\"Day\"}]";

        var payment = new PaymentCollection
        {
            PetroCardMorning = 0,
            PetroCardDay = 0,
            PetroCardNight = 0,
            DynamicItemsJson = dynamicJson
        };

        var cash3 = new List<CashDenomination>
        {
            new() { CashType = "Cash3", Denom500 = 5, Denom100 = 1, Denom20 = 2, TotalAmount = 2640.0 }
        };

        var debits = new List<DebitEntry>
        {
            new() { DebtorName = "SAIRAJ TRANSPORT", Amount = 18901.57, SlipNumber = "519", VehicleNumber = "UM 9335" }
        };

        var expenses = new List<Expense>
        {
            new() { Description = "Dyandeep bank", Amount = 3000.0 },
            new() { Description = "Madam", Amount = 500.0 },
            new() { Description = "Waghmare petrol", Amount = 200.0 },
            new() { Description = "Tanker shivapur", Amount = 100.0 },
            new() { Description = "Chatrabhath", Amount = 2500.0 },
            new() { Description = "Wai urban", Amount = 3000.0 }
        };

        DsmEntry savedEntry;
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var result = await dsmService.SaveCompleteEntryWithContextAsync(
                context, date, shiftType, "Rahul Shingvi", 3,
                nozzles, payment, debits, new List<TestingEntry>(), expenses, cash3,
                connectedPumpId: 4, connectedPumpIds: new List<int> { 4 });

            Assert.True(result.Success);
            savedEntry = result.Data!;

            // Verify totals computed directly
            // Direct In = Dynamic (7,737.80 + 114,768.75 + 990.0 = 123,496.55) + Cash3 (2,640.0) = 126,136.55
            // Debtors = 18,901.57
            // Expenses = 9,300.00
            // Total Collection = 154,338.12
            // Net Sales = 154,337.39
            // Mismatch = 154,338.12 - 154,337.39 = +0.73
            Assert.Equal(154337.39m, Math.Round(savedEntry.GrossSales, 2));
            Assert.Equal(126136.55m, Math.Round(savedEntry.TotalInDirect, 2));
            Assert.Equal(18901.57m, Math.Round(savedEntry.TotalCreditors, 2));
            Assert.Equal(154338.12m, Math.Round(savedEntry.TotalCollection, 2));
            Assert.Equal(0.73m, Math.Round(savedEntry.Mismatch, 2));
        }

        // Now test View Shift Entries summaries
        var summariesResult = await dsmService.GetShiftEntrySummariesAsync(savedEntry.ShiftId);
        Assert.True(summariesResult.Success);
        var summary = summariesResult.Data!.FirstOrDefault(s => s.DsmName == "Rahul Shingvi");

        Assert.NotNull(summary);
        Assert.Equal(154337.39, summary.GrossSales, 2);
        Assert.Equal(154337.39, summary.NetSales, 2);
        Assert.Equal(154338.12, summary.TotalPaymentIn, 2);
        Assert.Equal(0.73, summary.Difference, 2);
    }

    [Fact]
    public async Task Test_GetShiftEntrySummaries_WhenStoredTotalCollectionZero_CalculatesMismatchFromChildRecords()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 11, 7);
        var shiftType = "A";

        int shiftId;
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var shift = new Shift { ShiftDate = date, ShiftType = shiftType, IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();
            shiftId = shift.ShiftId;

            // Corrupted database state: TotalCollection is 0 and Mismatch is -154,337.39
            var primary = new DsmEntry
            {
                ShiftId = shiftId,
                DsmName = "Rahul Shingvi",
                PumpId = 3,
                ConnectedPumpId = 4,
                ConnectedPumpIdsJson = "[4]",
                GrossSales = 154337.39m,
                TotalInDirect = 0m,
                TotalCreditors = 0m,
                TotalCollection = 0m,
                Mismatch = -154337.39m
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();

            // Slave entry
            var slave = new DsmEntry
            {
                ShiftId = shiftId,
                DsmName = "Rahul Shingvi",
                PumpId = 4,
                ReconciledToPumpId = primary.DsmEntryId,
                GrossSales = 60212.82m,
                TotalCollection = 0m,
                Mismatch = 0m
            };
            context.DsmEntries.Add(slave);
            await context.SaveChangesAsync();

            // Child records for primary
            var dynamicJson = "[{\"CollectionTypeCode\":\"PETROCARD\",\"Amount\":7737.80,\"Slot\":\"Day\"},{\"CollectionTypeCode\":\"MOBIKWIK\",\"Amount\":114768.75,\"Slot\":\"Day\"},{\"CollectionTypeCode\":\"SBI_REDEEM\",\"Amount\":990.0,\"Slot\":\"Day\"}]";
            context.PaymentCollections.Add(new PaymentCollection { DsmEntryId = primary.DsmEntryId, DynamicItemsJson = dynamicJson });
            context.CashDenominations.Add(new CashDenomination { DsmEntryId = primary.DsmEntryId, CashType = "Cash3", TotalAmount = 2640.0 });
            context.DebitEntries.Add(new DebitEntry { DsmEntryId = primary.DsmEntryId, DebtorName = "SAIRAJ TRANSPORT", Amount = 18901.57 });
            context.Expenses.Add(new Expense { DsmEntryId = primary.DsmEntryId, Description = "General", Amount = 9300.0 });

            // Nozzles
            context.NozzleReadings.Add(new NozzleReading { DsmEntryId = primary.DsmEntryId, NozzleNumber = 5, SaleLitres = 941.2457, Rate = 100, Amount = 94124.57 });
            context.NozzleReadings.Add(new NozzleReading { DsmEntryId = slave.DsmEntryId, NozzleNumber = 7, SaleLitres = 602.1282, Rate = 100, Amount = 60212.82 });

            await context.SaveChangesAsync();
        }

        // View Shift Entries should heal the display dynamically: TotalCollection = 154,338.12, Mismatch = +0.73
        var res = await dsmService.GetShiftEntrySummariesAsync(shiftId);
        Assert.True(res.Success);

        var rahul = res.Data!.First(s => s.DsmName == "Rahul Shingvi");
        Assert.Equal(154337.39, rahul.NetSales, 2);
        Assert.Equal(154338.12, rahul.TotalPaymentIn, 2);
        Assert.Equal(0.73, rahul.Difference, 2); // Derived consistently from totalCollection - netSales!
    }

    [Fact]
    public async Task Test_ExistingPositiveTotalCollection_PreservesStoredMismatch()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 11, 7);
        var shiftType = "A";

        int shiftId;
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var shift = new Shift { ShiftDate = date, ShiftType = shiftType, IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();
            shiftId = shift.ShiftId;

            // Existing positive entry with a specific recorded mismatch (e.g. -2.01)
            var entry = new DsmEntry
            {
                ShiftId = shiftId,
                DsmName = "KARTIK BHOJALE",
                PumpId = 1,
                GrossSales = 300473.15m,
                TotalCollection = 300471.14m,
                Mismatch = -2.01m
            };
            context.DsmEntries.Add(entry);
            await context.SaveChangesAsync();
        }

        var res = await dsmService.GetShiftEntrySummariesAsync(shiftId);
        Assert.True(res.Success);

        var kartik = res.Data!.First(s => s.DsmName == "KARTIK BHOJALE");
        Assert.Equal(300471.14, kartik.TotalPaymentIn, 2);
        Assert.Equal(-2.01, kartik.Difference, 2); // Preserved exactly as stored!
    }

    [Fact]
    public async Task Test_OtherDsmEntries_RemainUnchangedAndAccurate()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 11, 7);
        var shiftType = "A";

        int shiftId;
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var shift = new Shift { ShiftDate = date, ShiftType = shiftType, IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();
            shiftId = shift.ShiftId;

            var entries = new List<DsmEntry>
            {
                new() { ShiftId = shiftId, DsmName = "SUMIT PANDE", PumpId = 8, GrossSales = 34666.87m, TotalCollection = 34677.65m, Mismatch = 10.78m },
                new() { ShiftId = shiftId, DsmName = "RUGVED BHISE", PumpId = 5, GrossSales = 105290.20m, TotalCollection = 105290.38m, Mismatch = 0.18m },
                new() { ShiftId = shiftId, DsmName = "SUVARNA SHELKE", PumpId = 7, GrossSales = 75342.98m, TotalCollection = 75354.31m, Mismatch = 11.33m },
                new() { ShiftId = shiftId, DsmName = "NIRJALA GAVADE", PumpId = 6, GrossSales = 92969.18m, TotalCollection = 93026.05m, Mismatch = 56.87m }
            };
            context.DsmEntries.AddRange(entries);
            await context.SaveChangesAsync();
        }

        var res = await dsmService.GetShiftEntrySummariesAsync(shiftId);
        Assert.True(res.Success);

        var sumit = res.Data!.First(s => s.DsmName == "SUMIT PANDE");
        Assert.Equal(34677.65, sumit.TotalPaymentIn, 2);
        Assert.Equal(10.78, sumit.Difference, 2);

        var rugved = res.Data!.First(s => s.DsmName == "RUGVED BHISE");
        Assert.Equal(105290.38, rugved.TotalPaymentIn, 2);
        Assert.Equal(0.18, rugved.Difference, 2);

        var suvarna = res.Data!.First(s => s.DsmName == "SUVARNA SHELKE");
        Assert.Equal(75354.31, suvarna.TotalPaymentIn, 2);
        Assert.Equal(11.33, suvarna.Difference, 2);

        var nirjala = res.Data!.First(s => s.DsmName == "NIRJALA GAVADE");
        Assert.Equal(93026.05, nirjala.TotalPaymentIn, 2);
        Assert.Equal(56.87, nirjala.Difference, 2);
    }

    [Fact]
    public async Task Test_HealthyConnectedPumpEntry_WithTesting_PreservedAcrossStartupHeal()
    {
        int primaryId;
        int slaveId;

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                ConnectedPumpId = 2,
                DsmName = "Rahul Shingvi",
                GrossSales = 8000m,
                TotalCollection = 6995.24m,
                Mismatch = -4.76m,
                CreatedAt = DateTime.Now.AddDays(-1),
                UpdatedAt = DateTime.Now.AddDays(-1)
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();
            primaryId = primary.DsmEntryId;

            var slave = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 2,
                ReconciledToPumpId = primaryId,
                DsmName = "Rahul Shingvi",
                GrossSales = 3000m,
                TotalCollection = 0m,
                Mismatch = 0m,
                CreatedAt = DateTime.Now.AddDays(-1),
                UpdatedAt = DateTime.Now.AddDays(-1)
            };
            context.DsmEntries.Add(slave);
            await context.SaveChangesAsync();
            slaveId = slave.DsmEntryId;

            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1,
                OpeningReading = 100,
                ClosingReading = 150,
                SaleLitres = 50,
                Rate = 100,
                Amount = 5000
            });
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = slaveId,
                NozzleNumber = 3,
                OpeningReading = 200,
                ClosingReading = 230,
                SaleLitres = 30,
                Rate = 100,
                Amount = 3000
            });
            context.TestingEntries.Add(new TestingEntry
            {
                DsmEntryId = primaryId,
                FuelType = "MS-I",
                Litres = 10,
                Rate = 100,
                Amount = 1000
            });

            var audit = new DsmApprovalAudit
            {
                SubmissionId = Guid.NewGuid(),
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
            };
            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
            await recalcService.RunIfNeededAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var primary = await context.DsmEntries.FindAsync(primaryId);
            var slave = await context.DsmEntries.FindAsync(slaveId);

            Assert.NotNull(primary);
            Assert.NotNull(slave);
            Assert.Equal(-4.76m, primary.Mismatch);
            Assert.Equal(8000m, primary.GrossSales);
            Assert.Equal(6995.24m, primary.TotalCollection);
            Assert.Equal(3000m, slave.GrossSales);
            Assert.Equal(0m, slave.Mismatch);
        }
    }

    [Fact]
    public async Task Test_ConnectedPumpExistenceAlone_DoesNotTriggerMismatchUpdate()
    {
        int primaryId;
        DateTime originalUpdatedAt;

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            originalUpdatedAt = DateTime.UtcNow.AddMinutes(-30);
            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                ConnectedPumpId = 2,
                DsmName = "Rahul Shingvi",
                GrossSales = 8000m,
                TotalCollection = 6995.24m,
                Mismatch = -4.76m,
                CreatedAt = originalUpdatedAt,
                UpdatedAt = originalUpdatedAt
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();
            primaryId = primary.DsmEntryId;

            var slave = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 2,
                ReconciledToPumpId = primaryId,
                DsmName = "Rahul Shingvi",
                GrossSales = 3000m,
                TotalCollection = 0m,
                Mismatch = 0m,
                CreatedAt = originalUpdatedAt,
                UpdatedAt = originalUpdatedAt
            };
            context.DsmEntries.Add(slave);
            await context.SaveChangesAsync();

            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1,
                OpeningReading = 100,
                ClosingReading = 150,
                SaleLitres = 50,
                Rate = 100,
                Amount = 5000
            });
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = slave.DsmEntryId,
                NozzleNumber = 3,
                OpeningReading = 200,
                ClosingReading = 230,
                SaleLitres = 30,
                Rate = 100,
                Amount = 3000
            });
            context.TestingEntries.Add(new TestingEntry
            {
                DsmEntryId = primaryId,
                FuelType = "MS-I",
                Litres = 10,
                Rate = 100,
                Amount = 1000
            });

            var audit = new DsmApprovalAudit
            {
                SubmissionId = Guid.NewGuid(),
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
            };
            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
            await recalcService.RunIfNeededAsync();

            var primary = await context.DsmEntries.FindAsync(primaryId);
            Assert.NotNull(primary);
            Assert.Equal(-4.76m, primary.Mismatch);
            Assert.False(context.ChangeTracker.HasChanges());
        }
    }

    [Fact]
    public async Task Test_GenuineNozzleCorruption_IsRepairedWithTestingDeductions()
    {
        int primaryId;
        int slaveId;

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                ConnectedPumpId = 2,
                DsmName = "Rahul Shingvi",
                GrossSales = 5000m, // Corrupted gross
                TotalCollection = 6995.24m,
                Mismatch = 1995.24m, // Corrupted mismatch
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();
            primaryId = primary.DsmEntryId;

            var slave = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 2,
                ReconciledToPumpId = primaryId,
                DsmName = "Rahul Shingvi",
                GrossSales = 3000m,
                TotalCollection = 0m,
                Mismatch = 0m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(slave);
            await context.SaveChangesAsync();
            slaveId = slave.DsmEntryId;

            // Primary nozzle is corrupted in DB (Closing reading 120 instead of audit's 150)
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1,
                OpeningReading = 100,
                ClosingReading = 120, // Corrupted!
                SaleLitres = 20,
                Rate = 100,
                Amount = 2000
            });
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = slaveId,
                NozzleNumber = 3,
                OpeningReading = 200,
                ClosingReading = 230,
                SaleLitres = 30,
                Rate = 100,
                Amount = 3000
            });
            context.TestingEntries.Add(new TestingEntry
            {
                DsmEntryId = primaryId,
                FuelType = "MS-I",
                Litres = 10,
                Rate = 100,
                Amount = 1000
            });

            var audit = new DsmApprovalAudit
            {
                SubmissionId = Guid.NewGuid(),
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
            };
            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
            await recalcService.RunIfNeededAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var primary = await context.DsmEntries.Include(e => e.NozzleReadings).FirstOrDefaultAsync(e => e.DsmEntryId == primaryId);
            var nr1 = primary?.NozzleReadings.FirstOrDefault(n => n.NozzleNumber == 1);

            Assert.NotNull(primary);
            Assert.NotNull(nr1);
            Assert.Equal(150.0, nr1.ClosingReading);
            Assert.Equal(50.0, nr1.SaleLitres);
            Assert.Equal(5000.0, nr1.Amount);
            Assert.Equal(8000m, primary.GrossSales); // 5000 + 3000
            // NetSales = 8000 - 1000 = 7000. Mismatch = 6995.24 - 7000 = -4.76
            Assert.Equal(-4.76m, primary.Mismatch);
        }
    }

    [Fact]
    public async Task Test_RepeatedStartupExecutions_AreIdempotentAndSafe()
    {
        int primaryId;

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                ConnectedPumpId = 2,
                DsmName = "Rahul Shingvi",
                GrossSales = 8000m,
                TotalCollection = 6995.24m,
                Mismatch = -4.76m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();
            primaryId = primary.DsmEntryId;

            var slave = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 2,
                ReconciledToPumpId = primaryId,
                DsmName = "Rahul Shingvi",
                GrossSales = 3000m,
                TotalCollection = 0m,
                Mismatch = 0m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(slave);
            await context.SaveChangesAsync();

            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1,
                OpeningReading = 100,
                ClosingReading = 150,
                SaleLitres = 50,
                Rate = 100,
                Amount = 5000
            });
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = slave.DsmEntryId,
                NozzleNumber = 3,
                OpeningReading = 200,
                ClosingReading = 230,
                SaleLitres = 30,
                Rate = 100,
                Amount = 3000
            });
            context.TestingEntries.Add(new TestingEntry
            {
                DsmEntryId = primaryId,
                FuelType = "MS-I",
                Litres = 10,
                Rate = 100,
                Amount = 1000
            });

            var audit = new DsmApprovalAudit
            {
                SubmissionId = Guid.NewGuid(),
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
            };
            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();
        }

        // Run 3 consecutive times simulating 3 separate application startups
        for (int i = 0; i < 3; i++)
        {
            using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
            {
                var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
                await recalcService.RunIfNeededAsync();

                var primary = await context.DsmEntries.FindAsync(primaryId);
                Assert.NotNull(primary);
                Assert.Equal(-4.76m, primary.Mismatch);
                Assert.Equal(8000m, primary.GrossSales);
            }
        }
    }

    [Fact]
    public async Task Test_CorruptionExclusivelyInConnectedSlaveEntry_IsHealedWithTesting()
    {
        int primaryId;
        int slaveId;

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                ConnectedPumpId = 2,
                DsmName = "Rahul Shingvi",
                GrossSales = 6000m, // Primary = 5000, Corrupted Slave = 1000 => 6000
                TotalCollection = 6995.24m,
                Mismatch = 1995.24m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();
            primaryId = primary.DsmEntryId;

            var slave = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 2,
                ReconciledToPumpId = primaryId,
                DsmName = "Rahul Shingvi",
                GrossSales = 1000m,
                TotalCollection = 0m,
                Mismatch = 0m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(slave);
            await context.SaveChangesAsync();
            slaveId = slave.DsmEntryId;

            // Primary nozzle is 100% HEALTHY
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1,
                OpeningReading = 100,
                ClosingReading = 150,
                SaleLitres = 50,
                Rate = 100,
                Amount = 5000
            });

            // Slave nozzle is CORRUPTED (Closing reading 210 instead of audit's 230)
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = slaveId,
                NozzleNumber = 3,
                OpeningReading = 200,
                ClosingReading = 210, // Corrupted!
                SaleLitres = 10,
                Rate = 100,
                Amount = 1000
            });

            context.TestingEntries.Add(new TestingEntry
            {
                DsmEntryId = primaryId,
                FuelType = "MS-I",
                Litres = 10,
                Rate = 100,
                Amount = 1000
            });

            var audit = new DsmApprovalAudit
            {
                SubmissionId = Guid.NewGuid(),
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
            };
            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
            await recalcService.RunIfNeededAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var primary = await context.DsmEntries.FindAsync(primaryId);
            var slave = await context.DsmEntries.Include(e => e.NozzleReadings).FirstOrDefaultAsync(e => e.DsmEntryId == slaveId);
            var nr3 = slave?.NozzleReadings.FirstOrDefault(n => n.NozzleNumber == 3);

            Assert.NotNull(primary);
            Assert.NotNull(slave);
            Assert.NotNull(nr3);
            Assert.Equal(230.0, nr3.ClosingReading);
            Assert.Equal(30.0, nr3.SaleLitres);
            Assert.Equal(3000.0, nr3.Amount);
            Assert.Equal(3000m, slave.GrossSales);
            Assert.Equal(8000m, primary.GrossSales);
            Assert.Equal(-4.76m, primary.Mismatch); // 6995.24 - (8000 - 1000)
        }
    }

    [Fact]
    public async Task Test_TestingDeductionsDistributedAcrossPrimaryAndSlave_DeduplicatedAccurately()
    {
        int primaryId;
        int slaveId;

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                ConnectedPumpId = 2,
                DsmName = "Rahul Shingvi",
                GrossSales = 6000m,
                TotalCollection = 7000m,
                Mismatch = 1000m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();
            primaryId = primary.DsmEntryId;

            var slave = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 2,
                ReconciledToPumpId = primaryId,
                DsmName = "Rahul Shingvi",
                GrossSales = 1000m,
                TotalCollection = 0m,
                Mismatch = 0m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(slave);
            await context.SaveChangesAsync();
            slaveId = slave.DsmEntryId;

            // Nozzle 1 is corrupted in DB (Opening 100, Closing 120 => 2000; Audit has 150 => 5000)
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1,
                OpeningReading = 100,
                ClosingReading = 120, // Corrupted
                SaleLitres = 20,
                Rate = 100,
                Amount = 2000
            });
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = slaveId,
                NozzleNumber = 3,
                OpeningReading = 200,
                ClosingReading = 230,
                SaleLitres = 30,
                Rate = 100,
                Amount = 3000
            });

            // Testing on Primary = 600
            context.TestingEntries.Add(new TestingEntry
            {
                DsmEntryId = primaryId,
                FuelType = "MS-I",
                Litres = 6,
                Rate = 100,
                Amount = 600
            });

            // Testing on Slave = 400
            context.TestingEntries.Add(new TestingEntry
            {
                DsmEntryId = slaveId,
                FuelType = "MS-I",
                Litres = 4,
                Rate = 100,
                Amount = 400
            });

            var audit = new DsmApprovalAudit
            {
                SubmissionId = Guid.NewGuid(),
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
            };
            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
            await recalcService.RunIfNeededAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var primary = await context.DsmEntries.FindAsync(primaryId);
            Assert.NotNull(primary);
            // Primary gross = 5000 + 3000 = 8000.
            // Group testing = 600 + 400 = 1000.
            // NetSales = 8000 - 1000 = 7000.
            // Mismatch = 7000 - 7000 = 0.00.
            Assert.Equal(8000m, primary.GrossSales);
            Assert.Equal(0m, primary.Mismatch);
        }
    }

    [Fact]
    public async Task Test_HealthyApprovedPrimarySlave_RetainsAllFinancialValues()
    {
        int primaryId;
        int slaveId;

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                ConnectedPumpId = 2,
                DsmName = "Rahul Shingvi",
                GrossSales = 8000m,
                TotalInDirect = 1500m,
                TotalCreditors = 2500m,
                TotalCollection = 6995.24m,
                Mismatch = -4.76m,
                CreatedAt = DateTime.Now.AddDays(-2),
                UpdatedAt = DateTime.Now.AddDays(-2)
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();
            primaryId = primary.DsmEntryId;

            var slave = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 2,
                ReconciledToPumpId = primaryId,
                DsmName = "Rahul Shingvi",
                GrossSales = 3000m,
                TotalInDirect = 0m,
                TotalCreditors = 0m,
                TotalCollection = 0m,
                Mismatch = 0m,
                CreatedAt = DateTime.Now.AddDays(-2),
                UpdatedAt = DateTime.Now.AddDays(-2)
            };
            context.DsmEntries.Add(slave);
            await context.SaveChangesAsync();
            slaveId = slave.DsmEntryId;

            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1,
                OpeningReading = 100,
                ClosingReading = 150,
                SaleLitres = 50,
                Rate = 100,
                Amount = 5000
            });
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = slaveId,
                NozzleNumber = 3,
                OpeningReading = 200,
                ClosingReading = 230,
                SaleLitres = 30,
                Rate = 100,
                Amount = 3000
            });
            context.TestingEntries.Add(new TestingEntry
            {
                DsmEntryId = primaryId,
                FuelType = "MS-I",
                Litres = 10,
                Rate = 100,
                Amount = 1000
            });

            var audit = new DsmApprovalAudit
            {
                SubmissionId = Guid.NewGuid(),
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
            };
            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
            await recalcService.RunIfNeededAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var primary = await context.DsmEntries.FindAsync(primaryId);
            var slave = await context.DsmEntries.FindAsync(slaveId);

            Assert.NotNull(primary);
            Assert.NotNull(slave);
            Assert.Equal(8000m, primary.GrossSales);
            Assert.Equal(1500m, primary.TotalInDirect);
            Assert.Equal(2500m, primary.TotalCreditors);
            Assert.Equal(6995.24m, primary.TotalCollection);
            Assert.Equal(-4.76m, primary.Mismatch);

            Assert.Equal(3000m, slave.GrossSales);
            Assert.Equal(0m, slave.TotalInDirect);
            Assert.Equal(0m, slave.TotalCreditors);
            Assert.Equal(0m, slave.TotalCollection);
            Assert.Equal(0m, slave.Mismatch);
        }
    }

    [Fact]
    public async Task Test_RepeatedStartupExecution_ProducesZeroDatabaseChanges()
    {
        int primaryId;

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                ConnectedPumpId = 2,
                DsmName = "Rahul Shingvi",
                GrossSales = 8000m,
                TotalCollection = 6995.24m,
                Mismatch = -4.76m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();
            primaryId = primary.DsmEntryId;

            var slave = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 2,
                ReconciledToPumpId = primaryId,
                DsmName = "Rahul Shingvi",
                GrossSales = 3000m,
                TotalCollection = 0m,
                Mismatch = 0m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(slave);
            await context.SaveChangesAsync();

            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1,
                OpeningReading = 100,
                ClosingReading = 150,
                SaleLitres = 50,
                Rate = 100,
                Amount = 5000
            });
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = slave.DsmEntryId,
                NozzleNumber = 3,
                OpeningReading = 200,
                ClosingReading = 230,
                SaleLitres = 30,
                Rate = 100,
                Amount = 3000
            });
            context.TestingEntries.Add(new TestingEntry
            {
                DsmEntryId = primaryId,
                FuelType = "MS-I",
                Litres = 10,
                Rate = 100,
                Amount = 1000
            });

            var audit = new DsmApprovalAudit
            {
                SubmissionId = Guid.NewGuid(),
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
            };
            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();
        }

        // Run startup service once
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
            await recalcService.RunIfNeededAsync();
            Assert.False(context.ChangeTracker.HasChanges());
        }

        // Run startup service a second time and verify zero changes tracked
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
            await recalcService.RunIfNeededAsync();
            Assert.False(context.ChangeTracker.HasChanges());
        }
    }

    [Fact]
    public async Task Test_MislinkedSlave_WithHealthyNozzleReadings_LinkRepaired_PrimaryFinancialValuesUnchanged()
    {
        int primaryId;
        int slaveId;

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                ConnectedPumpId = 2,
                DsmName = "Rahul Shingvi",
                GrossSales = 8000m,
                TotalCollection = 6995.24m,
                Mismatch = -4.76m,
                CreatedAt = DateTime.Now.AddDays(-1),
                UpdatedAt = DateTime.Now.AddDays(-1)
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();
            primaryId = primary.DsmEntryId;

            // Slave exists and is mislinked (ReconciledToPumpId = 99999 instead of primaryId)
            var slave = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 2,
                ReconciledToPumpId = 99999, // Mislinked
                DsmName = "Rahul Shingvi",
                GrossSales = 3000m,
                TotalCollection = 0m,
                Mismatch = 0m,
                CreatedAt = DateTime.Now.AddDays(-1),
                UpdatedAt = DateTime.Now.AddDays(-1)
            };
            context.DsmEntries.Add(slave);
            await context.SaveChangesAsync();
            slaveId = slave.DsmEntryId;

            // Both nozzles are 100% HEALTHY
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1,
                OpeningReading = 100,
                ClosingReading = 150,
                SaleLitres = 50,
                Rate = 100,
                Amount = 5000
            });
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = slaveId,
                NozzleNumber = 3,
                OpeningReading = 200,
                ClosingReading = 230,
                SaleLitres = 30,
                Rate = 100,
                Amount = 3000
            });
            context.TestingEntries.Add(new TestingEntry
            {
                DsmEntryId = primaryId,
                FuelType = "MS-I",
                Litres = 10,
                Rate = 100,
                Amount = 1000
            });

            var audit = new DsmApprovalAudit
            {
                SubmissionId = Guid.NewGuid(),
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
            };
            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
            await recalcService.RunIfNeededAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var primary = await context.DsmEntries.FindAsync(primaryId);
            var slave = await context.DsmEntries.FindAsync(slaveId);

            Assert.NotNull(primary);
            Assert.NotNull(slave);

            // Slave relationship link was repaired to primaryId
            Assert.Equal(primaryId, slave.ReconciledToPumpId);

            // Primary financial values were PRESERVED completely untouched
            Assert.Equal(8000m, primary.GrossSales);
            Assert.Equal(6995.24m, primary.TotalCollection);
            Assert.Equal(-4.76m, primary.Mismatch);
        }
    }

    [Fact]
    public async Task Test_MislinkedSlave_WithGenuineGrossDiscrepancy_LinkRepaired_AndFinancialValuesRecalculated()
    {
        int primaryId;
        int slaveId;

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            // Primary stored GrossSales is 5000m (DISCREPANCY: only primary pump gross was recorded, missing slave 3000m!)
            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                ConnectedPumpId = 2,
                DsmName = "Rahul Shingvi",
                GrossSales = 5000m, // Discrepant gross
                TotalCollection = 6995.24m,
                Mismatch = 2995.24m, // Discrepant mismatch
                CreatedAt = DateTime.Now.AddDays(-1),
                UpdatedAt = DateTime.Now.AddDays(-1)
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();
            primaryId = primary.DsmEntryId;

            var slave = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 2,
                ReconciledToPumpId = 88888, // Mislinked
                DsmName = "Rahul Shingvi",
                GrossSales = 3000m,
                TotalCollection = 0m,
                Mismatch = 0m,
                CreatedAt = DateTime.Now.AddDays(-1),
                UpdatedAt = DateTime.Now.AddDays(-1)
            };
            context.DsmEntries.Add(slave);
            await context.SaveChangesAsync();
            slaveId = slave.DsmEntryId;

            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1,
                OpeningReading = 100,
                ClosingReading = 150,
                SaleLitres = 50,
                Rate = 100,
                Amount = 5000
            });
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = slaveId,
                NozzleNumber = 3,
                OpeningReading = 200,
                ClosingReading = 230,
                SaleLitres = 30,
                Rate = 100,
                Amount = 3000
            });
            context.TestingEntries.Add(new TestingEntry
            {
                DsmEntryId = primaryId,
                FuelType = "MS-I",
                Litres = 10,
                Rate = 100,
                Amount = 1000
            });

            var audit = new DsmApprovalAudit
            {
                SubmissionId = Guid.NewGuid(),
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
            };
            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
            await recalcService.RunIfNeededAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var primary = await context.DsmEntries.FindAsync(primaryId);
            var slave = await context.DsmEntries.FindAsync(slaveId);

            Assert.NotNull(primary);
            Assert.NotNull(slave);

            // Slave relationship link was repaired to primaryId
            Assert.Equal(primaryId, slave.ReconciledToPumpId);

            // Primary financial values were recalculated due to grossDiscrepancy (5000 vs 8000)
            Assert.Equal(8000m, primary.GrossSales); // 5000 + 3000
            // NetSales = 8000 - 1000 = 7000. Mismatch = 6995.24 - 7000 = -4.76
            Assert.Equal(-4.76m, primary.Mismatch);
        }
    }

    [Fact]
    public async Task Test_MissingSlaveEntry_NewlyCreated_AndPrimaryTotalsRecalculated()
    {
        int primaryId;

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            // Slave entry does NOT exist in DB at all
            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                ConnectedPumpId = 2,
                DsmName = "Rahul Shingvi",
                GrossSales = 5000m,
                TotalCollection = 6995.24m,
                Mismatch = 2995.24m,
                CreatedAt = DateTime.Now.AddDays(-1),
                UpdatedAt = DateTime.Now.AddDays(-1)
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();
            primaryId = primary.DsmEntryId;

            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1,
                OpeningReading = 100,
                ClosingReading = 150,
                SaleLitres = 50,
                Rate = 100,
                Amount = 5000
            });
            context.TestingEntries.Add(new TestingEntry
            {
                DsmEntryId = primaryId,
                FuelType = "MS-I",
                Litres = 10,
                Rate = 100,
                Amount = 1000
            });

            // Audit includes slave nozzle 3 (Pump 2)
            var audit = new DsmApprovalAudit
            {
                SubmissionId = Guid.NewGuid(),
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
            };
            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
            await recalcService.RunIfNeededAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var primary = await context.DsmEntries.FindAsync(primaryId);
            var slave = await context.DsmEntries.Include(e => e.NozzleReadings).FirstOrDefaultAsync(e => e.ShiftId == primary!.ShiftId && e.PumpId == 2);

            Assert.NotNull(primary);
            Assert.NotNull(slave);

            // Slave entry was newly created and linked to primary
            Assert.Equal(primaryId, slave.ReconciledToPumpId);
            Assert.Equal(3000m, slave.GrossSales);

            // Primary financial totals recalculated to include the newly created slave
            Assert.Equal(8000m, primary.GrossSales); // 5000 + 3000
            Assert.Equal(-4.76m, primary.Mismatch); // 6995.24 - (8000 - 1000)
        }
    }

    [Fact]
    public async Task Test_CompleteNozzleData_PermitsRecalculation()
    {
        int primaryId;

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                ConnectedPumpId = 2,
                DsmName = "Rahul Shingvi",
                GrossSales = 5000m,
                TotalCollection = 6995.24m,
                Mismatch = 1995.24m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();
            primaryId = primary.DsmEntryId;

            var slave = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 2,
                ReconciledToPumpId = primaryId,
                DsmName = "Rahul Shingvi",
                GrossSales = 3000m,
                TotalCollection = 0m,
                Mismatch = 0m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(slave);
            await context.SaveChangesAsync();

            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1,
                OpeningReading = 100,
                ClosingReading = 120, // Corrupted: audit is 150
                SaleLitres = 20,
                Rate = 100,
                Amount = 2000
            });
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = slave.DsmEntryId,
                NozzleNumber = 3,
                OpeningReading = 200,
                ClosingReading = 230,
                SaleLitres = 30,
                Rate = 100,
                Amount = 3000
            });
            context.TestingEntries.Add(new TestingEntry
            {
                DsmEntryId = primaryId,
                FuelType = "MS-I",
                Litres = 10,
                Rate = 100,
                Amount = 1000
            });

            var audit = new DsmApprovalAudit
            {
                SubmissionId = Guid.NewGuid(),
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
            };
            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
            await recalcService.RunIfNeededAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var primary = await context.DsmEntries.Include(e => e.NozzleReadings).FirstOrDefaultAsync(e => e.DsmEntryId == primaryId);
            Assert.NotNull(primary);
            Assert.Equal(150.0, primary.NozzleReadings.First(n => n.NozzleNumber == 1).ClosingReading);
            Assert.Equal(8000m, primary.GrossSales); // 5000 + 3000
            Assert.Equal(-4.76m, primary.Mismatch);  // 6995.24 - (8000 - 1000)
        }
    }

    [Fact]
    public async Task Test_MissingPrimaryNozzle_BlocksRecalculation()
    {
        var customMappings = new List<PumpMapping>
        {
            new() { PumpMappingId = 101, PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 102, PumpId = 1, NozzleNumber = 2, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 103, PumpId = 2, NozzleNumber = 3, FuelType = "MS-I", IsActive = true }
        };
        PumpConfiguration.InitializeFromDb(customMappings);

        try
        {
            int primaryId;
            using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
            {
                context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

                var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
                context.Shifts.Add(shift);
                await context.SaveChangesAsync();

                var primary = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    PumpId = 1,
                    ConnectedPumpId = 2,
                    DsmName = "Rahul Shingvi",
                    GrossSales = 5000m,
                    TotalCollection = 6000m,
                    Mismatch = 1000m,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                context.DsmEntries.Add(primary);
                await context.SaveChangesAsync();
                primaryId = primary.DsmEntryId;

                var slave = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    PumpId = 2,
                    ReconciledToPumpId = primaryId,
                    DsmName = "Rahul Shingvi",
                    GrossSales = 3000m,
                    TotalCollection = 0m,
                    Mismatch = 0m,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                context.DsmEntries.Add(slave);
                await context.SaveChangesAsync();

                // DB has Nozzle 1, but configured Pump 1 expects {1, 2}. Nozzle 2 is MISSING from DB.
                context.NozzleReadings.Add(new NozzleReading
                {
                    DsmEntryId = primaryId,
                    NozzleNumber = 1,
                    OpeningReading = 100,
                    ClosingReading = 150,
                    SaleLitres = 50,
                    Rate = 100,
                    Amount = 5000
                });
                context.NozzleReadings.Add(new NozzleReading
                {
                    DsmEntryId = slave.DsmEntryId,
                    NozzleNumber = 3,
                    OpeningReading = 200,
                    ClosingReading = 230,
                    SaleLitres = 30,
                    Rate = 100,
                    Amount = 3000
                });

                var audit = new DsmApprovalAudit
                {
                    SubmissionId = Guid.NewGuid(),
                    ApprovedBy = "Admin",
                    ApprovedAt = DateTime.Now,
                    ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                    OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 2, \"OpeningReading\": 10.0, \"ClosingReading\": 20.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
                };
                context.DsmApprovalAudits.Add(audit);
                await context.SaveChangesAsync();
            }

            using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
            {
                var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
                await recalcService.RunIfNeededAsync();
            }

            using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
            {
                var primary = await context.DsmEntries.FindAsync(primaryId);
                Assert.NotNull(primary);
                // Financials remain strictly untouched because completeness validation fails
                Assert.Equal(5000m, primary.GrossSales);
                Assert.Equal(1000m, primary.Mismatch);
            }
        }
        finally
        {
            ResetTestPumpMappings();
        }
    }

    [Fact]
    public async Task Test_MissingSlaveNozzle_BlocksRecalculation()
    {
        var customMappings = new List<PumpMapping>
        {
            new() { PumpMappingId = 101, PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 102, PumpId = 2, NozzleNumber = 3, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 103, PumpId = 2, NozzleNumber = 4, FuelType = "HSD", IsActive = true }
        };
        PumpConfiguration.InitializeFromDb(customMappings);

        try
        {
            int primaryId;
            using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
            {
                context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

                var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
                context.Shifts.Add(shift);
                await context.SaveChangesAsync();

                var primary = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    PumpId = 1,
                    ConnectedPumpId = 2,
                    DsmName = "Rahul Shingvi",
                    GrossSales = 5000m,
                    TotalCollection = 6000m,
                    Mismatch = 1000m,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                context.DsmEntries.Add(primary);
                await context.SaveChangesAsync();
                primaryId = primary.DsmEntryId;

                var slave = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    PumpId = 2,
                    ReconciledToPumpId = primaryId,
                    DsmName = "Rahul Shingvi",
                    GrossSales = 3000m,
                    TotalCollection = 0m,
                    Mismatch = 0m,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                context.DsmEntries.Add(slave);
                await context.SaveChangesAsync();

                context.NozzleReadings.Add(new NozzleReading
                {
                    DsmEntryId = primaryId,
                    NozzleNumber = 1,
                    OpeningReading = 100,
                    ClosingReading = 150,
                    SaleLitres = 50,
                    Rate = 100,
                    Amount = 5000
                });
                // DB has Nozzle 3, but configured Pump 2 expects {3, 4}. Nozzle 4 is MISSING from slave DB.
                context.NozzleReadings.Add(new NozzleReading
                {
                    DsmEntryId = slave.DsmEntryId,
                    NozzleNumber = 3,
                    OpeningReading = 200,
                    ClosingReading = 230,
                    SaleLitres = 30,
                    Rate = 100,
                    Amount = 3000
                });

                var audit = new DsmApprovalAudit
                {
                    SubmissionId = Guid.NewGuid(),
                    ApprovedBy = "Admin",
                    ApprovedAt = DateTime.Now,
                    ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                    OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}, {\"NozzleId\": 4, \"OpeningReading\": 10.0, \"ClosingReading\": 20.0, \"Rate\": 100.0}]}"
                };
                context.DsmApprovalAudits.Add(audit);
                await context.SaveChangesAsync();
            }

            using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
            {
                var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
                await recalcService.RunIfNeededAsync();
            }

            using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
            {
                var primary = await context.DsmEntries.FindAsync(primaryId);
                Assert.NotNull(primary);
                Assert.Equal(5000m, primary.GrossSales);
                Assert.Equal(1000m, primary.Mismatch);
            }
        }
        finally
        {
            ResetTestPumpMappings();
        }
    }

    [Fact]
    public async Task Test_DuplicateNozzleReading_BlocksRecalculation()
    {
        int primaryId;

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                ConnectedPumpId = 2,
                DsmName = "Rahul Shingvi",
                GrossSales = 5000m,
                TotalCollection = 6000m,
                Mismatch = 1000m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();
            primaryId = primary.DsmEntryId;

            var slave = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 2,
                ReconciledToPumpId = primaryId,
                DsmName = "Rahul Shingvi",
                GrossSales = 3000m,
                TotalCollection = 0m,
                Mismatch = 0m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(slave);
            await context.SaveChangesAsync();

            // Duplicate Nozzle 1 reading in primary
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1,
                OpeningReading = 100,
                ClosingReading = 150,
                SaleLitres = 50,
                Rate = 100,
                Amount = 5000
            });
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1, // DUPLICATE!
                OpeningReading = 150,
                ClosingReading = 200,
                SaleLitres = 50,
                Rate = 100,
                Amount = 5000
            });
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = slave.DsmEntryId,
                NozzleNumber = 3,
                OpeningReading = 200,
                ClosingReading = 230,
                SaleLitres = 30,
                Rate = 100,
                Amount = 3000
            });

            var audit = new DsmApprovalAudit
            {
                SubmissionId = Guid.NewGuid(),
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
            };
            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
            await recalcService.RunIfNeededAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var primary = await context.DsmEntries.FindAsync(primaryId);
            Assert.NotNull(primary);
            Assert.Equal(5000m, primary.GrossSales);
            Assert.Equal(1000m, primary.Mismatch);
        }
    }

    [Fact]
    public async Task Test_UnexpectedForeignNozzle_BlocksRecalculation()
    {
        int primaryId;

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                ConnectedPumpId = 2,
                DsmName = "Rahul Shingvi",
                GrossSales = 5000m,
                TotalCollection = 6000m,
                Mismatch = 1000m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();
            primaryId = primary.DsmEntryId;

            var slave = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 2,
                ReconciledToPumpId = primaryId,
                DsmName = "Rahul Shingvi",
                GrossSales = 3000m,
                TotalCollection = 0m,
                Mismatch = 0m,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
            context.DsmEntries.Add(slave);
            await context.SaveChangesAsync();

            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 1,
                OpeningReading = 100,
                ClosingReading = 150,
                SaleLitres = 50,
                Rate = 100,
                Amount = 5000
            });
            // Foreign nozzle 99 in primary
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = primaryId,
                NozzleNumber = 99, // UNEXPECTED FOREIGN NOZZLE!
                OpeningReading = 10,
                ClosingReading = 20,
                SaleLitres = 10,
                Rate = 100,
                Amount = 1000
            });
            context.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = slave.DsmEntryId,
                NozzleNumber = 3,
                OpeningReading = 200,
                ClosingReading = 230,
                SaleLitres = 30,
                Rate = 100,
                Amount = 3000
            });

            var audit = new DsmApprovalAudit
            {
                SubmissionId = Guid.NewGuid(),
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
            };
            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
            await recalcService.RunIfNeededAsync();
        }

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var primary = await context.DsmEntries.FindAsync(primaryId);
            Assert.NotNull(primary);
            Assert.Equal(5000m, primary.GrossSales);
            Assert.Equal(1000m, primary.Mismatch);
        }
    }

    [Fact]
    public async Task Test_AuditMissingConfiguredNozzle_BlocksRecalculation()
    {
        var customMappings = new List<PumpMapping>
        {
            new() { PumpMappingId = 101, PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 102, PumpId = 1, NozzleNumber = 2, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 103, PumpId = 2, NozzleNumber = 3, FuelType = "MS-I", IsActive = true }
        };
        PumpConfiguration.InitializeFromDb(customMappings);

        try
        {
            int primaryId;
            using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
            {
                context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

                var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
                context.Shifts.Add(shift);
                await context.SaveChangesAsync();

                var primary = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    PumpId = 1,
                    ConnectedPumpId = 2,
                    DsmName = "Rahul Shingvi",
                    GrossSales = 7000m,
                    TotalCollection = 8000m,
                    Mismatch = 1000m,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                context.DsmEntries.Add(primary);
                await context.SaveChangesAsync();
                primaryId = primary.DsmEntryId;

                var slave = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    PumpId = 2,
                    ReconciledToPumpId = primaryId,
                    DsmName = "Rahul Shingvi",
                    GrossSales = 3000m,
                    TotalCollection = 0m,
                    Mismatch = 0m,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                context.DsmEntries.Add(slave);
                await context.SaveChangesAsync();

                context.NozzleReadings.Add(new NozzleReading
                {
                    DsmEntryId = primaryId,
                    NozzleNumber = 1,
                    OpeningReading = 100,
                    ClosingReading = 150,
                    SaleLitres = 50,
                    Rate = 100,
                    Amount = 5000
                });
                context.NozzleReadings.Add(new NozzleReading
                {
                    DsmEntryId = primaryId,
                    NozzleNumber = 2,
                    OpeningReading = 10,
                    ClosingReading = 30,
                    SaleLitres = 20,
                    Rate = 100,
                    Amount = 2000
                });
                context.NozzleReadings.Add(new NozzleReading
                {
                    DsmEntryId = slave.DsmEntryId,
                    NozzleNumber = 3,
                    OpeningReading = 200,
                    ClosingReading = 230,
                    SaleLitres = 30,
                    Rate = 100,
                    Amount = 3000
                });

                // Audit MISSES configured Nozzle 2
                var audit = new DsmApprovalAudit
                {
                    SubmissionId = Guid.NewGuid(),
                    ApprovedBy = "Admin",
                    ApprovedAt = DateTime.Now,
                    ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                    OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
                };
                context.DsmApprovalAudits.Add(audit);
                await context.SaveChangesAsync();
            }

            using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
            {
                var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
                await recalcService.RunIfNeededAsync();
            }

            using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
            {
                var primary = await context.DsmEntries.FindAsync(primaryId);
                Assert.NotNull(primary);
                Assert.Equal(7000m, primary.GrossSales);
                Assert.Equal(1000m, primary.Mismatch);
            }
        }
        finally
        {
            ResetTestPumpMappings();
        }
    }

    [Fact]
    public async Task Test_IncompleteDataset_IndividualNozzleHealed_FinancialsUntouched()
    {
        var customMappings = new List<PumpMapping>
        {
            new() { PumpMappingId = 101, PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 102, PumpId = 1, NozzleNumber = 2, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 103, PumpId = 2, NozzleNumber = 3, FuelType = "MS-I", IsActive = true }
        };
        PumpConfiguration.InitializeFromDb(customMappings);

        try
        {
            int primaryId;
            using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
            {
                context.AppMeta.Add(new AppMeta { Key = "RecalcMigrationV2Done", Value = "true" });

                var shift = new Shift { ShiftDate = new DateTime(2026, 11, 7), ShiftType = "A", IsLocked = false };
                context.Shifts.Add(shift);
                await context.SaveChangesAsync();

                var primary = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    PumpId = 1,
                    ConnectedPumpId = 2,
                    DsmName = "Rahul Shingvi",
                    GrossSales = 5000m,
                    TotalCollection = 6000m,
                    Mismatch = 1000m,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                context.DsmEntries.Add(primary);
                await context.SaveChangesAsync();
                primaryId = primary.DsmEntryId;

                var slave = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    PumpId = 2,
                    ReconciledToPumpId = primaryId,
                    DsmName = "Rahul Shingvi",
                    GrossSales = 3000m,
                    TotalCollection = 0m,
                    Mismatch = 0m,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };
                context.DsmEntries.Add(slave);
                await context.SaveChangesAsync();

                // Primary has corrupted Nozzle 1 (Closing 120 in DB vs audit 150).
                // Configured Pump 1 expects {1, 2}, but Nozzle 2 is missing from DB!
                context.NozzleReadings.Add(new NozzleReading
                {
                    DsmEntryId = primaryId,
                    NozzleNumber = 1,
                    OpeningReading = 100,
                    ClosingReading = 120, // CORRUPTED
                    SaleLitres = 20,
                    Rate = 100,
                    Amount = 2000
                });
                context.NozzleReadings.Add(new NozzleReading
                {
                    DsmEntryId = slave.DsmEntryId,
                    NozzleNumber = 3,
                    OpeningReading = 200,
                    ClosingReading = 230,
                    SaleLitres = 30,
                    Rate = 100,
                    Amount = 3000
                });

                var audit = new DsmApprovalAudit
                {
                    SubmissionId = Guid.NewGuid(),
                    ApprovedBy = "Admin",
                    ApprovedAt = DateTime.Now,
                    ApprovedDataJson = $"{{\"DsmEntryId\": {primaryId}}}",
                    OriginalDataJson = "{\"Readings\": [{\"NozzleId\": 1, \"OpeningReading\": 100.0, \"ClosingReading\": 150.0, \"Rate\": 100.0}, {\"NozzleId\": 2, \"OpeningReading\": 10.0, \"ClosingReading\": 20.0, \"Rate\": 100.0}, {\"NozzleId\": 3, \"OpeningReading\": 200.0, \"ClosingReading\": 230.0, \"Rate\": 100.0}]}"
                };
                context.DsmApprovalAudits.Add(audit);
                await context.SaveChangesAsync();
            }

            using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
            {
                var recalcService = new RecalculationMigrationService(context, _serviceProvider.GetRequiredService<IDsmCalculationService>());
                await recalcService.RunIfNeededAsync();
            }

            using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
            {
                var primary = await context.DsmEntries.Include(e => e.NozzleReadings).FirstOrDefaultAsync(e => e.DsmEntryId == primaryId);
                var slave = await context.DsmEntries.FirstOrDefaultAsync(e => e.ShiftId == primary!.ShiftId && e.PumpId == 2);

                Assert.NotNull(primary);
                Assert.NotNull(slave);

                // 1. The individual corrupted nozzle reading IS repaired in DB
                var nr1 = primary.NozzleReadings.First(n => n.NozzleNumber == 1);
                Assert.Equal(150.0, nr1.ClosingReading);
                Assert.Equal(50.0, nr1.SaleLitres);
                Assert.Equal(5000.0, nr1.Amount);

                // 2. GrossSales and Mismatch remain strictly untouched because dataset completeness failed
                Assert.Equal(5000m, primary.GrossSales);
                Assert.Equal(1000m, primary.Mismatch);
                Assert.Equal(3000m, slave.GrossSales);
                Assert.Equal(0m, slave.Mismatch);
            }
        }
        finally
        {
            ResetTestPumpMappings();
        }
    }

    private static void ResetTestPumpMappings()
    {
        var defaultMappings = new List<PumpMapping>
        {
            new() { PumpMappingId = 1, PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 2, PumpId = 2, NozzleNumber = 3, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 3, PumpId = 3, NozzleNumber = 5, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 4, PumpId = 4, NozzleNumber = 7, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 5, PumpId = 5, NozzleNumber = 9, FuelType = "SPEED", IsActive = true },
            new() { PumpMappingId = 6, PumpId = 6, NozzleNumber = 10, FuelType = "SPEED", IsActive = true },
            new() { PumpMappingId = 7, PumpId = 7, NozzleNumber = 11, FuelType = "CNG", IsActive = true },
            new() { PumpMappingId = 8, PumpId = 8, NozzleNumber = 12, FuelType = "CNG", IsActive = true }
        };
        PumpConfiguration.InitializeFromDb(defaultMappings);
    }
}
