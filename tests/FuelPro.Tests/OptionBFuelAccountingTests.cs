using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using Xunit;

namespace FuelPro.Tests;

public class OptionBFuelAccountingTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly string _tempDir;

    public OptionBFuelAccountingTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_OptionBTests_" + Guid.NewGuid().ToString("N"));
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
        services.AddSingleton<IDsmCalculationService, DsmCalculationService>();
        services.AddScoped<IShiftAggregationService, ShiftAggregationService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddTransient<IStationConfigurationService, StationConfigurationService>();
        services.AddTransient<DsmEntryService>();

        _serviceProvider = services.BuildServiceProvider();

        // 10-nozzle / 5-pump configuration for tests:
        // Pump 1: N1, N2
        // Pump 2: N3, N4
        // Pump 3: N5, N6
        // Pump 4: N7, N8
        // Pump 5: N9, N10
        var testMappings = new List<PumpMapping>
        {
            new() { PumpMappingId = 1, PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 2, PumpId = 1, NozzleNumber = 2, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 3, PumpId = 2, NozzleNumber = 3, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 4, PumpId = 2, NozzleNumber = 4, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 5, PumpId = 3, NozzleNumber = 5, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 6, PumpId = 3, NozzleNumber = 6, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 7, PumpId = 4, NozzleNumber = 7, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 8, PumpId = 4, NozzleNumber = 8, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 9, PumpId = 5, NozzleNumber = 9, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 10, PumpId = 5, NozzleNumber = 10, FuelType = "HSD", IsActive = true }
        };
        PumpConfiguration.InitializeFromDb(testMappings);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        db.Database.EnsureCreated();
        db.PumpMappings.AddRange(testMappings);
        db.SaveChanges();
    }

    public void Dispose()
    {
        PumpConfiguration.ResetToDefaults();
        try { _serviceProvider?.Dispose(); } catch { }
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    [Fact]
    public void Test_01_TestingExcludedFromTotalCollection()
    {
        var calcService = _serviceProvider.GetRequiredService<IDsmCalculationService>();

        var dto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto>
            {
                new() { Amount = 10000m }
            },
            TestingEntries = new List<TestingEntryDto>
            {
                new() { FuelType = "MS", Litres = 5m, Rate = 100m, Amount = 500m }
            },
            PaymentCollection = new PaymentCollectionDto
            {
                PhysicalCash = 8300m,
                Others = 200m // Must NOT be added to direct collection
            },
            DebitEntries = new List<DebitEntryDto>
            {
                new() { Amount = 700m }
            },
            Expenses = new List<ExpenseDto>
            {
                new() { Amount = 500m }
            }
        };

        var result = calcService.Calculate(dto);

        // TotalCollection = 8,300 (Direct) + 700 (Debtors) + 500 (Expenses) = 9,500
        // Testing (500) is strictly EXCLUDED from TotalCollection
        Assert.Equal(9500m, result.TotalCollection);
        Assert.Equal(8300m, result.TotalInDirect);
        Assert.Equal(700m, result.TotalCreditors);
        Assert.Equal(500m, result.TotalExpenses);
    }

    [Fact]
    public void Test_02_NetGrossSalesSubtractsTesting()
    {
        var calcService = _serviceProvider.GetRequiredService<IDsmCalculationService>();

        var dto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto>
            {
                new() { Amount = 10000m }
            },
            TestingEntries = new List<TestingEntryDto>
            {
                new() { FuelType = "MS", Litres = 5m, Rate = 100m, Amount = 500m }
            }
        };

        var result = calcService.Calculate(dto);

        // Physical meter gross
        Assert.Equal(10000m, result.GrossSales);
        Assert.Equal(10000m, result.MeterGrossSales);
        // Testing
        Assert.Equal(500m, result.TotalTesting);
        // Net Gross Sales = 10,000 - 500 = 9,500
        Assert.Equal(9500m, result.NetGrossSales);
    }

    [Fact]
    public async Task Test_03_BalancedTestingShift_ProducesZeroMismatch_AndZeroSalaryDeduction()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 9, 10);
        string shiftType = "A";

        // Meter = 10,000, Testing = 500 -> Net = 9,500
        // Remitted: Cash 8,300, Debtors 700, Expenses 500 -> TotalCollection = 9,500
        var nozzles = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, OpeningReading = 1000, ClosingReading = 1100, Rate = 100, SaleLitres = 100, Amount = 10000 }
        };
        var testing = new List<TestingEntry>
        {
            new() { FuelType = "MS", Litres = 5, Rate = 100, Amount = 500 }
        };
        var payment = new PaymentCollection { CashDeposit = 0 };
        var debits = new List<DebitEntry> { new() { DebtorName = "Test Debtor", Amount = 700 } };
        var expenses = new List<Expense> { new() { Description = "Tea", Amount = 500 } };
        var cashDenoms = new List<CashDenomination>
        {
            new() { CashType = "Cash2", TotalAmount = 8300 }
        };

        var saveResult = await dsmService.SaveCompleteEntryAsync(
            date, shiftType, "Ramesh", 1, nozzles, payment, debits, testing, expenses, cashDenoms);

        Assert.True(saveResult.Success, $"Save failed: {saveResult.Error}");
        var entry = saveResult.Data!;

        Assert.Equal(10000m, entry.GrossSales);
        Assert.Equal(9500m, entry.NetGrossSales);
        Assert.Equal(9500m, entry.TotalCollection);
        Assert.Equal(0m, entry.Mismatch);

        // Verify that NO salary shortage personal debtor was created
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var pdList = await db.DsmPersonalDebtors.Where(p => p.DsmEntryId == entry.DsmEntryId).ToListAsync();
        Assert.Empty(pdList);
    }

    [Fact]
    public async Task Test_04_GenuineShortageWithTesting_MaintainsSalaryDeductionPolicy()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 9, 11);
        string shiftType = "A";

        // Meter = 20,000, Testing = 500 -> Net = 19,500
        // Remitted: Cash 18,000, Expenses 1,000 -> TotalCollection = 19,000
        // Mismatch = 19,000 - 19,500 = -500 (Genuine Shortage)
        // Expected Salary Deduction = 500 (full shortage, no ₹10 tolerance)
        var nozzles = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, OpeningReading = 1000, ClosingReading = 1200, Rate = 100, SaleLitres = 200, Amount = 20000 }
        };
        var testing = new List<TestingEntry>
        {
            new() { FuelType = "MS", Litres = 5, Rate = 100, Amount = 500 }
        };
        var payment = new PaymentCollection();
        var debits = new List<DebitEntry>();
        var expenses = new List<Expense> { new() { Description = "Generator Oil", Amount = 1000 } };
        var cashDenoms = new List<CashDenomination>
        {
            new() { CashType = "Cash2", TotalAmount = 18000 }
        };

        var saveResult = await dsmService.SaveCompleteEntryAsync(
            date, shiftType, "Suresh", 1, nozzles, payment, debits, testing, expenses, cashDenoms);

        Assert.True(saveResult.Success, $"Save failed: {saveResult.Error}");
        var entry = saveResult.Data!;

        Assert.Equal(20000m, entry.GrossSales);
        Assert.Equal(19500m, entry.NetGrossSales);
        Assert.Equal(19000m, entry.TotalCollection);
        Assert.Equal(-500m, entry.Mismatch);

        // Verify that automatic shortage personal debtor of 500 was created
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var pdList = await db.DsmPersonalDebtors.Where(p => p.DsmEntryId == entry.DsmEntryId).ToListAsync();
        Assert.Single(pdList);
        Assert.Equal(500.0, pdList[0].Amount);
        Assert.Contains("Shortage", pdList[0].Remarks ?? "");
    }

    [Fact]
    public async Task Test_05_FourPumpGroup_WithTestingOnSlavePump()
    {
        // 4-Pump Scenario:
        // P1 = ₹10,000 (Nozzle 1)
        // P2 = ₹8,000 (Nozzle 3)
        // P3 = ₹7,000 (Nozzle 5)
        // P4 = ₹5,000 (Nozzle 7)
        // Testing on P3 = ₹500
        // Direct = ₹28,000 (PhonePe 15k, Card 5k, PetroCard 3k, Cash 5k)
        // Debtors = ₹1,000
        // Expenses = ₹1,200 (Expense 700 + Khandhare 500)
        // Others = ₹300 (Excluded)
        //
        // Expected:
        // MeterGrossSales = ₹30,000
        // GroupTesting = ₹500
        // NetGrossSales = ₹29,500
        // TotalInDirect = ₹28,000
        // Debtors = ₹1,000
        // Expenses = ₹1,200
        // TotalCollection = ₹30,200
        // Mismatch = +₹700

        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var shiftAggService = _serviceProvider.GetRequiredService<IShiftAggregationService>();
        var calcService = _serviceProvider.GetRequiredService<IDsmCalculationService>();

        var date = new DateTime(2026, 9, 12);
        string shiftType = "A";

        var nozzles = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, OpeningReading = 1000, ClosingReading = 1100, Rate = 100, SaleLitres = 100, Amount = 10000 },
            new() { NozzleNumber = 3, OpeningReading = 2000, ClosingReading = 2080, Rate = 100, SaleLitres = 80, Amount = 8000 },
            new() { NozzleNumber = 5, OpeningReading = 3000, ClosingReading = 3070, Rate = 100, SaleLitres = 70, Amount = 7000 },
            new() { NozzleNumber = 7, OpeningReading = 4000, ClosingReading = 4050, Rate = 100, SaleLitres = 50, Amount = 5000 }
        };

        var testingEntries = new List<TestingEntry>
        {
            new() { FuelType = "MS (P3)", Litres = 5, Rate = 100, Amount = 500 }
        };

        var payment = new PaymentCollection
        {
            PhonePeMorning = 15000,
            CreditCardMorning = 5000,
            PetroCardMorning = 3000,
            Others = 300 // Must be excluded
        };

        var debits = new List<DebitEntry>
        {
            new() { DebtorName = "Fleet Customer A", Amount = 1000 }
        };

        var expenses = new List<Expense>
        {
            new() { Description = "Tea & Refreshments", Amount = 700 }
        };
        var khandhare = new List<KhandharePetroleumEntry>
        {
            new() { Name = "Owner Drawing", Amount = 500 }
        };

        var cashDenominations = new List<CashDenomination>
        {
            new() { CashType = "Cash2", TotalAmount = 5000 } // Total Direct = 15k + 5k + 3k + 5k = 28,000
        };

        // 1. Layer: DsmCalculationService directly
        var dto = new DsmEntryDto
        {
            NozzleReadings = nozzles.Select(n => new NozzleReadingDto { Amount = (decimal)n.Amount }).ToList(),
            TestingEntries = testingEntries.Select(t => new TestingEntryDto { Amount = (decimal)t.Amount }).ToList(),
            PaymentCollection = new PaymentCollectionDto
            {
                PhonePe = 15000m,
                CreditCard = 5000m,
                PetroCard = 3000m,
                PhysicalCash = 5000m,
                Others = 300m
            },
            DebitEntries = debits.Select(d => new DebitEntryDto { Amount = (decimal)d.Amount }).ToList(),
            Expenses = expenses.Select(e => new ExpenseDto { Amount = (decimal)e.Amount })
                        .Concat(khandhare.Select(k => new ExpenseDto { Amount = (decimal)k.Amount }))
                        .ToList()
        };
        var directCalc = calcService.Calculate(dto);
        Assert.Equal(30000m, directCalc.GrossSales);
        Assert.Equal(30000m, directCalc.MeterGrossSales);
        Assert.Equal(500m, directCalc.TotalTesting);
        Assert.Equal(29500m, directCalc.NetGrossSales);
        Assert.Equal(28000m, directCalc.TotalInDirect);
        Assert.Equal(1000m, directCalc.TotalCreditors);
        Assert.Equal(1200m, directCalc.TotalExpenses);
        Assert.Equal(30200m, directCalc.TotalCollection);
        Assert.Equal(700m, directCalc.Mismatch);

        // 2. Layer: DsmEntryService Save and SQLite DB Persistence
        var saveRes = await dsmService.SaveCompleteEntryAsync(
            date, shiftType, "Mahesh", 1, nozzles, payment, debits, testingEntries, expenses,
            cashDenominations, connectedPumpId: 2, existingEntryId: null, startTime: null, endTime: null,
            personalDebtors: null, khandharePetroleumEntries: khandhare, qrPayments: null,
            connectedPumpIds: new List<int> { 2, 3, 4 });

        Assert.True(saveRes.Success, $"Save failed: {saveRes.Error}");
        var primaryEntry = saveRes.Data!;

        // Check persisted values in primary entry
        Assert.Equal(30000m, primaryEntry.GrossSales); // Physical meter gross preserved
        Assert.Equal(29500m, primaryEntry.NetGrossSales); // Commercial net sales
        Assert.Equal(28000m, primaryEntry.TotalInDirect);
        Assert.Equal(1000m, primaryEntry.TotalCreditors);
        Assert.Equal(30200m, primaryEntry.TotalCollection);
        Assert.Equal(700m, primaryEntry.Mismatch);

        // 3. Layer: Re-fetch full entry from DB to verify raw persisted state
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var persistedPrimary = await db.DsmEntries
            .Include(e => e.NozzleReadings)
            .Include(e => e.TestingEntries)
            .Include(e => e.Expenses)
            .Include(e => e.KhandharePetroleumEntries)
            .Include(e => e.CashDenominations)
            .FirstOrDefaultAsync(e => e.DsmEntryId == primaryEntry.DsmEntryId);

        Assert.NotNull(persistedPrimary);
        Assert.Equal(30000m, persistedPrimary!.GrossSales);
        Assert.Equal(29500m, persistedPrimary.NetGrossSales);
        Assert.Equal(30200m, persistedPrimary.TotalCollection);
        Assert.Equal(700m, persistedPrimary.Mismatch);

        // 4. Layer: ShiftAggregationService
        var allShiftEntries = await db.DsmEntries
            .Include(e => e.NozzleReadings)
            .Include(e => e.PaymentCollection)
            .Include(e => e.DebitEntries)
            .Include(e => e.TestingEntries)
            .Include(e => e.Expenses)
            .Include(e => e.CashDenominations)
            .Include(e => e.KhandharePetroleumEntries)
            .Where(e => e.ShiftId == primaryEntry.ShiftId)
            .ToListAsync();

        var summaryRows = shiftAggService.BuildDsmSummaryRows(allShiftEntries);
        Assert.Single(summaryRows); // 1 consolidated row for group [1,2,3,4]
        var row = summaryRows[0];
        Assert.Equal(30000, row.GrossSales);
        Assert.Equal(500, row.Testing);
        Assert.Equal(29500, row.NetGrossSales);
        Assert.Equal(1200, row.Expenses);
        Assert.Equal(1000, row.Debit);
        Assert.Equal(30200, row.TotalCollection);
        Assert.Equal(700, row.Difference);
        Assert.Equal(700, row.Mismatch);

        // 5. Layer: ReportService CalculateEntryTotals
        var (repMeterGross, repTotalColl, repMismatch) = ReportService.CalculateEntryTotals(persistedPrimary);
        Assert.Equal(30000, repMeterGross);
        Assert.Equal(30200, repTotalColl);
        Assert.Equal(700, repMismatch);

        // 6. Layer: PWA Canonical Formula Parity
        double pwaGrossSales = 30000;
        double pwaTotalTesting = 500;
        double pwaGrandProductSales = 0;
        double pwaCash = 5000;
        double pwaCashDeposit = 0;
        double pwaUpiTotal = 15000;
        double pwaCardTotal = 5000;
        double pwaPetroCardTotal = 3000;
        double pwaQrTotal = 0;
        double pwaCreditTotal = 1000;
        double pwaExpense = 700;
        double pwaKpTotal = 500;

        double pwaNetFuelSales = Math.Max(0, pwaGrossSales - pwaTotalTesting);
        double pwaTotalSalesLiability = pwaNetFuelSales + pwaGrandProductSales;
        double pwaTotalExpenses = pwaExpense + pwaKpTotal;
        double pwaTotalCollections = pwaCash + pwaCashDeposit + pwaUpiTotal + pwaCardTotal + pwaPetroCardTotal + pwaQrTotal + pwaCreditTotal + pwaTotalExpenses;
        double pwaMismatch = pwaTotalCollections - pwaTotalSalesLiability;

        Assert.Equal(29500, pwaNetFuelSales);
        Assert.Equal(30200, pwaTotalCollections);
        Assert.Equal(700, pwaMismatch);
    }

    [Fact]
    public async Task Test_06_GroupTestingSummedAcrossPrimaryAndSlaves()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var shiftAggService = _serviceProvider.GetRequiredService<IShiftAggregationService>();

        var date = new DateTime(2026, 9, 13);
        string shiftType = "A";

        // P1 nozzle 1: 10,000; P2 nozzle 3: 8,000
        var nozzles = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, OpeningReading = 100, ClosingReading = 200, Rate = 100, SaleLitres = 100, Amount = 10000 },
            new() { NozzleNumber = 3, OpeningReading = 300, ClosingReading = 380, Rate = 100, SaleLitres = 80, Amount = 8000 }
        };

        // Testing on both nozzles: P1 = 200, P2 = 300 -> Total Group Testing = 500
        var testing = new List<TestingEntry>
        {
            new() { FuelType = "Nozzle 1 (MS)", Litres = 2, Rate = 100, Amount = 200 },
            new() { FuelType = "Nozzle 3 (HSD)", Litres = 3, Rate = 100, Amount = 300 }
        };

        var payment = new PaymentCollection { CashDeposit = 0 };
        var debits = new List<DebitEntry>();
        var expenses = new List<Expense>();
        var cashDenoms = new List<CashDenomination>
        {
            new() { CashType = "Cash2", TotalAmount = 17500 } // Net sales = 18,000 - 500 = 17,500
        };

        var res = await dsmService.SaveCompleteEntryAsync(
            date, shiftType, "Ganesh", 1, nozzles, payment, debits, testing, expenses, cashDenoms,
            connectedPumpId: 2, existingEntryId: null, startTime: null, endTime: null,
            personalDebtors: null, khandharePetroleumEntries: null, qrPayments: null,
            connectedPumpIds: new List<int> { 2 });

        Assert.True(res.Success, $"Save failed: {res.Error}");

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var entries = await db.DsmEntries
            .Include(e => e.NozzleReadings)
            .Include(e => e.TestingEntries)
            .Include(e => e.Expenses)
            .Include(e => e.CashDenominations)
            .Where(e => e.ShiftId == res.Data!.ShiftId)
            .ToListAsync();

        var rows = shiftAggService.BuildDsmSummaryRows(entries);
        Assert.Single(rows);
        Assert.Equal(18000, rows[0].GrossSales);
        Assert.Equal(500, rows[0].Testing);
        Assert.Equal(17500, rows[0].NetGrossSales);
        Assert.Equal(17500, rows[0].TotalCollection);
        Assert.Equal(0, rows[0].Difference);
    }

    [Fact]
    public void Test_07_HistoricalRecordParity()
    {
        // Historical record in DB:
        // MeterGrossSales = 30,000
        // Testing = 500
        // Legacy TotalCollection stored = 30,700 (Direct 28k + Debtors 1k + Expenses 1.2k + Testing 500)
        // Legacy Mismatch stored = +700 (30,700 - 30,000)
        //
        // Under Option B:
        // Clean TotalCollection = 30,200
        // NetGrossSales = 29,500
        // Option B Mismatch = 30,200 - 29,500 = +700
        //
        // Parity proof: Legacy Mismatch === Option B Mismatch
        var historicalEntry = new DsmEntry
        {
            GrossSales = 30000m,
            TotalInDirect = 28000m,
            TotalCreditors = 1000m,
            TotalCollection = 30700m, // legacy stored collection
            Mismatch = 700m,          // legacy stored mismatch
            TestingEntries = new List<TestingEntry>
            {
                new() { FuelType = "MS", Amount = 500 }
            },
            Expenses = new List<Expense>
            {
                new() { Amount = 1200 }
            }
        };

        // Recalculating with Option B:
        decimal cleanCollection = historicalEntry.TotalInDirect 
                                + historicalEntry.TotalCreditors 
                                + (decimal)historicalEntry.Expenses.Sum(e => e.Amount);
        Assert.Equal(30200m, cleanCollection);

        decimal netGrossSales = historicalEntry.NetGrossSales;
        Assert.Equal(29500m, netGrossSales);

        decimal recalculatedMismatch = cleanCollection - netGrossSales;
        Assert.Equal(historicalEntry.Mismatch, recalculatedMismatch);
    }

    [Fact]
    public void Test_08_PwaDesktopFormulaParity()
    {
        var calcService = _serviceProvider.GetRequiredService<IDsmCalculationService>();

        // Test varying boundary conditions
        (decimal meterGross, decimal testing, decimal direct, decimal credit, decimal expense)[] cases =
        {
            (10000m, 500m, 8000m, 1000m, 500m),
            (50000m, 0m, 45000m, 4000m, 1000m),
            (15000.75m, 350.25m, 12000.50m, 2000m, 650m)
        };

        foreach (var c in cases)
        {
            var dto = new DsmEntryDto
            {
                NozzleReadings = new List<NozzleReadingDto> { new() { Amount = c.meterGross } },
                TestingEntries = new List<TestingEntryDto> { new() { Amount = c.testing } },
                PaymentCollection = new PaymentCollectionDto { PhysicalCash = c.direct },
                DebitEntries = new List<DebitEntryDto> { new() { Amount = c.credit } },
                Expenses = new List<ExpenseDto> { new() { Amount = c.expense } }
            };

            var desktopRes = calcService.Calculate(dto);

            // PWA formula
            double pwaGrossSales = (double)c.meterGross;
            double pwaTotalTesting = (double)c.testing;
            double pwaGrandProductSales = 0;
            double pwaTotalExpenses = (double)c.expense;
            double pwaTotalCollections = (double)c.direct + (double)c.credit + pwaTotalExpenses;
            double pwaNetFuelSales = Math.Max(0, pwaGrossSales - pwaTotalTesting);
            double pwaMismatch = pwaTotalCollections - (pwaNetFuelSales + pwaGrandProductSales);

            Assert.Equal((decimal)pwaNetFuelSales, desktopRes.NetGrossSales);
            Assert.Equal((decimal)pwaTotalCollections, desktopRes.TotalCollection);
            Assert.Equal((decimal)pwaMismatch, desktopRes.Mismatch);
        }
    }

    [Fact]
    public void Test_09_OthersRemainsInformational()
    {
        var calcService = _serviceProvider.GetRequiredService<IDsmCalculationService>();

        var dto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 10000m } },
            PaymentCollection = new PaymentCollectionDto
            {
                PhysicalCash = 9000m,
                Others = 1500m // Must NOT count toward TotalInDirect or TotalCollection
            },
            DebitEntries = new List<DebitEntryDto> { new() { Amount = 1000m } }
        };

        var res = calcService.Calculate(dto);

        Assert.Equal(9000m, res.TotalInDirect);
        Assert.Equal(10000m, res.TotalCollection);
        Assert.Equal(0m, res.Mismatch);
    }

    [Fact]
    public void Test_10_ExpensesCountedExactlyOnce()
    {
        var calcService = _serviceProvider.GetRequiredService<IDsmCalculationService>();

        var dto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 10000m } },
            PaymentCollection = new PaymentCollectionDto { PhysicalCash = 8500m },
            Expenses = new List<ExpenseDto>
            {
                new() { Amount = 500m },
                new() { Amount = 1000m }
            }
        };

        var res = calcService.Calculate(dto);

        Assert.Equal(1500m, res.TotalExpenses);
        Assert.Equal(8500m, res.TotalInDirect);
        // TotalCollection = 8,500 + 1,500 = 10,000 (counted once)
        Assert.Equal(10000m, res.TotalCollection);
        Assert.Equal(0m, res.Mismatch);
    }

    [Fact]
    public async Task Test_11_NoDoubleCountingOfGrossSales()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var shiftAggService = _serviceProvider.GetRequiredService<IShiftAggregationService>();

        var date = new DateTime(2026, 9, 14);
        string shiftType = "A";

        var nozzles = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, OpeningReading = 100, ClosingReading = 200, Rate = 100, SaleLitres = 100, Amount = 10000 },
            new() { NozzleNumber = 3, OpeningReading = 300, ClosingReading = 380, Rate = 100, SaleLitres = 80, Amount = 8000 }
        };

        var payment = new PaymentCollection { PhonePeMorning = 18000 };
        var debits = new List<DebitEntry>();
        var expenses = new List<Expense>();
        var cashDenoms = new List<CashDenomination>();

        var res = await dsmService.SaveCompleteEntryAsync(
            date, shiftType, "Sanjay", 1, nozzles, payment, debits, new List<TestingEntry>(), expenses, cashDenoms,
            connectedPumpId: 2, existingEntryId: null, startTime: null, endTime: null,
            personalDebtors: null, khandharePetroleumEntries: null, qrPayments: null,
            connectedPumpIds: new List<int> { 2 });

        Assert.True(res.Success);

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var entries = await db.DsmEntries
            .Include(e => e.NozzleReadings)
            .Include(e => e.PaymentCollection)
            .Include(e => e.DebitEntries)
            .Include(e => e.TestingEntries)
            .Include(e => e.Expenses)
            .Include(e => e.CashDenominations)
            .Include(e => e.KhandharePetroleumEntries)
            .Where(e => e.ShiftId == res.Data!.ShiftId)
            .ToListAsync();

        // 2 entries: 1 primary, 1 slave
        Assert.Equal(2, entries.Count);

        // Shift summary rows must group them into 1 row of ₹18,000 NOT ₹36,000 (no double counting)
        var rows = shiftAggService.BuildDsmSummaryRows(entries);
        Assert.Single(rows);
        Assert.Equal(18000, rows[0].GrossSales);
        Assert.Equal(18000, rows[0].NetGrossSales);
        Assert.Equal(18000, rows[0].TotalCollection);
        Assert.Equal(0, rows[0].Difference);
    }

    [Fact]
    public void Test_12_NoDoubleCountingOfExpenses()
    {
        var calcService = _serviceProvider.GetRequiredService<IDsmCalculationService>();

        // Expenses list has vouchers (800) and owner drawings (400)
        var dto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 10000m } },
            PaymentCollection = new PaymentCollectionDto { PhysicalCash = 8800m },
            Expenses = new List<ExpenseDto>
            {
                new() { Amount = 800m },
                new() { Amount = 400m }
            }
        };

        var res = calcService.Calculate(dto);

        Assert.Equal(1200m, res.TotalExpenses);
        Assert.Equal(8800m, res.TotalInDirect);
        // TotalCollection = 8800 + 1200 = 10000
        Assert.Equal(10000m, res.TotalCollection);
        Assert.Equal(0m, res.Mismatch);
    }

    [Fact]
    public void Test_13_MultiPumpReportsPreservePhysicalAndCommercialDistinction()
    {
        var row = new DsmSummaryRowDto
        {
            PumpId = 1,
            GrossSales = 30000,  // Physical meter gross
            Testing = 500,       // Calibration testing
            CashInHand = 28000,  // Direct payment
            Debit = 1000,
            Expenses = 1200
        };

        // Assert that physical meter sales, testing, and commercial sales are all individually accessible
        Assert.Equal(30000, row.GrossSales);
        Assert.Equal(500, row.Testing);
        Assert.Equal(29500, row.NetGrossSales);
        Assert.Equal(30200, row.TotalCollection);
        Assert.Equal(700, row.Difference);

        var shiftTotal = new DsmShiftTotalDto
        {
            GrossSales = 30000,
            Testing = 500,
            TotalCollection = 30200,
            Mismatch = 700
        };

        Assert.Equal(30000, shiftTotal.GrossSales);
        Assert.Equal(29500, shiftTotal.NetGrossSales);
        Assert.Equal(500, shiftTotal.Testing);
        Assert.Equal(30200, shiftTotal.TotalCollection);
        Assert.Equal(700, shiftTotal.Mismatch);
    }
}
