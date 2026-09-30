using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.DTOs;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.UI.ViewModels;
using Xunit;

namespace FuelPro.Tests;

public class DsmLossNoToleranceRegressionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;

    public DsmLossNoToleranceRegressionTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_DsmLossTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "fuelPro.db");

        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application();
        }

        var services = new ServiceCollection();

        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"),
            ServiceLifetime.Transient);

        services.AddSingleton<ICredentialFileService>(new LocalCredentialFileService(Path.Combine(_tempDir, "credentials")));

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
        services.AddTransient<IDsmPersonalDebtorRepository, DsmPersonalDebtorRepository>();

        // Services
        services.AddTransient<IDsmCalculationService, DsmCalculationService>();
        services.AddTransient<IShiftAggregationService, ShiftAggregationService>();
        services.AddTransient<DsmEntryService>();
        services.AddTransient<IReportService, ReportService>();

        _serviceProvider = services.BuildServiceProvider();

        // Initialize schema
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    private async Task<(Shift ShiftA, Shift ShiftB)> CreateShiftsAsync(DateTime date)
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var shiftA = new Shift { ShiftDate = date, ShiftType = "A", CreatedAt = date };
        var shiftB = new Shift { ShiftDate = date, ShiftType = "B", CreatedAt = date };
        context.Shifts.AddRange(shiftA, shiftB);
        await context.SaveChangesAsync();
        return (shiftA, shiftB);
    }

    [Fact]
    public async Task Test1_ExactCurrentCase_ShiftA250_ShiftB2260_Total2510()
    {
        // Arrange
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 9, 11);
        var (shiftA, shiftB) = await CreateShiftsAsync(date);
        string dsmName = "Ramesh Jadhav";

        // Act: Save Shift A (Shortage ₹2.50)
        // Gross: 7632.50, TotalCollection: 7630.00 -> Mismatch = -2.50
        using (var contextA = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var resultA = await dsmService.SaveCompleteEntryWithContextAsync(
                contextA,
                date, "A", dsmName, pumpId: 1,
                nozzleReadings: new List<NozzleReading>
                {
                    new() { NozzleNumber = 1, OpeningReading = 1000, ClosingReading = 1076.325, SaleLitres = 76.325, Rate = 100.0, Amount = 7632.50 }
                },
                payment: new PaymentCollection { CashDeposit = 5030.00 },
                debits: new List<DebitEntry> { new() { DebtorName = "Credit Party", Amount = 2500.00 } },
                testingEntries: new List<TestingEntry>(),
                expenses: new List<Expense> { new() { Description = "Lunch", Amount = 100.00 } },
                cashDenominations: new List<CashDenomination>()
            );
            Assert.True(resultA.Success, $"Shift A save failed: {resultA.Error}");
        }

        // Act: Save Shift B (Shortage ₹22.60)
        // Gross: 14842.60, TotalCollection: 14820.00 -> Mismatch = -22.60
        using (var contextB = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var resultB = await dsmService.SaveCompleteEntryWithContextAsync(
                contextB,
                date, "B", dsmName, pumpId: 1,
                nozzleReadings: new List<NozzleReading>
                {
                    new() { NozzleNumber = 1, OpeningReading = 2000, ClosingReading = 2148.426, SaleLitres = 148.426, Rate = 100.0, Amount = 14842.60 }
                },
                payment: new PaymentCollection { CashDeposit = 13170.00 },
                debits: new List<DebitEntry> { new() { DebtorName = "Credit Party", Amount = 1500.00 } },
                testingEntries: new List<TestingEntry>(),
                expenses: new List<Expense> { new() { Description = "Dinner", Amount = 150.00 } },
                cashDenominations: new List<CashDenomination>()
            );
            Assert.True(resultB.Success, $"Shift B save failed: {resultB.Error}");
        }

        // Assert: Check DsmPersonalDebtors in storage
        using (var verifyContext = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var debtors = await verifyContext.DsmPersonalDebtors
                .Where(d => d.DsmName == dsmName)
                .OrderBy(d => d.Id)
                .ToListAsync();

            Assert.Equal(2, debtors.Count);
            
            // Shift A debtor
            Assert.Equal(2.50, debtors[0].Amount, precision: 2);
            Assert.Contains("Shift A", debtors[0].Remarks);

            // Shift B debtor
            Assert.Equal(22.60, debtors[1].Amount, precision: 2);
            Assert.Contains("Shift B", debtors[1].Remarks);

            // Total DSM Loss
            double totalLoss = debtors.Sum(d => d.Amount);
            Assert.Equal(25.10, totalLoss, precision: 2);
        }
    }

    [Fact]
    public async Task Test2_BothShiftsShort_ShiftA35_ShiftB25_Total60()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 9, 12);
        var (shiftA, shiftB) = await CreateShiftsAsync(date);
        string dsmName = "Ramesh Jadhav";

        // Shift A short by ₹35 (Gross: 1000, Coll: 965)
        using (var ctxA = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            await dsmService.SaveCompleteEntryWithContextAsync(
                ctxA, date, "A", dsmName, 1,
                nozzleReadings: new List<NozzleReading> { new() { NozzleNumber = 1, OpeningReading = 0, ClosingReading = 10, SaleLitres = 10, Rate = 100, Amount = 1000 } },
                payment: new PaymentCollection { CashDeposit = 965 },
                debits: new List<DebitEntry>(), testingEntries: new List<TestingEntry>(),
                expenses: new List<Expense>(), cashDenominations: new List<CashDenomination>()
            );
        }

        // Shift B short by ₹25 (Gross: 1000, Coll: 975)
        using (var ctxB = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            await dsmService.SaveCompleteEntryWithContextAsync(
                ctxB, date, "B", dsmName, 1,
                nozzleReadings: new List<NozzleReading> { new() { NozzleNumber = 1, OpeningReading = 10, ClosingReading = 20, SaleLitres = 10, Rate = 100, Amount = 1000 } },
                payment: new PaymentCollection { CashDeposit = 975 },
                debits: new List<DebitEntry>(), testingEntries: new List<TestingEntry>(),
                expenses: new List<Expense>(), cashDenominations: new List<CashDenomination>()
            );
        }

        using (var verifyCtx = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var debtors = await verifyCtx.DsmPersonalDebtors.Where(d => d.DsmName == dsmName).ToListAsync();
            Assert.Equal(2, debtors.Count);
            Assert.Contains(debtors, d => Math.Abs(d.Amount - 35.0) < 0.01 && d.Remarks!.Contains("Shift A"));
            Assert.Contains(debtors, d => Math.Abs(d.Amount - 25.0) < 0.01 && d.Remarks!.Contains("Shift B"));
            Assert.Equal(60.0, debtors.Sum(d => d.Amount), precision: 2);
        }
    }

    [Fact]
    public async Task Test3_OneShiftOnly_Shortage750_Produces750NotZero()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 9, 13);
        var (shiftA, _) = await CreateShiftsAsync(date);
        string dsmName = "Suresh Patil";

        // Shortage of ₹7.50 (Gross: 1000, Coll: 992.50)
        using (var ctx = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            await dsmService.SaveCompleteEntryWithContextAsync(
                ctx, date, "A", dsmName, 1,
                nozzleReadings: new List<NozzleReading> { new() { NozzleNumber = 1, OpeningReading = 0, ClosingReading = 10, SaleLitres = 10, Rate = 100, Amount = 1000 } },
                payment: new PaymentCollection { CashDeposit = 992.50 },
                debits: new List<DebitEntry>(), testingEntries: new List<TestingEntry>(),
                expenses: new List<Expense>(), cashDenominations: new List<CashDenomination>()
            );
        }

        using (var verifyCtx = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var debtors = await verifyCtx.DsmPersonalDebtors.Where(d => d.DsmName == dsmName).ToListAsync();
            Assert.Single(debtors);
            Assert.Equal(7.50, debtors[0].Amount, precision: 2);
        }
    }

    [Fact]
    public async Task Test4_NoShortage_ZeroOrPositiveMismatch_NoDsmLossTransaction()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 9, 14);
        var (shiftA, _) = await CreateShiftsAsync(date);
        string dsmName = "Mahesh Shinde";

        // Balanced (Gross: 1000, Coll: 1000)
        using (var ctx = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            await dsmService.SaveCompleteEntryWithContextAsync(
                ctx, date, "A", dsmName, 1,
                nozzleReadings: new List<NozzleReading> { new() { NozzleNumber = 1, OpeningReading = 0, ClosingReading = 10, SaleLitres = 10, Rate = 100, Amount = 1000 } },
                payment: new PaymentCollection { CashDeposit = 1000 },
                debits: new List<DebitEntry>(), testingEntries: new List<TestingEntry>(),
                expenses: new List<Expense>(), cashDenominations: new List<CashDenomination>()
            );
        }

        using (var verifyCtx = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var debtors = await verifyCtx.DsmPersonalDebtors.Where(d => d.DsmName == dsmName).ToListAsync();
            Assert.Empty(debtors);
        }

        // Excess by ₹15 (Gross: 1000, Coll: 1015)
        using (var ctx = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            await dsmService.SaveCompleteEntryWithContextAsync(
                ctx, date, "A", dsmName, 1,
                nozzleReadings: new List<NozzleReading> { new() { NozzleNumber = 1, OpeningReading = 0, ClosingReading = 10, SaleLitres = 10, Rate = 100, Amount = 1000 } },
                payment: new PaymentCollection { CashDeposit = 1015 },
                debits: new List<DebitEntry>(), testingEntries: new List<TestingEntry>(),
                expenses: new List<Expense>(), cashDenominations: new List<CashDenomination>()
            );
        }

        using (var verifyCtx = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var debtors = await verifyCtx.DsmPersonalDebtors.Where(d => d.DsmName == dsmName).ToListAsync();
            Assert.Empty(debtors);
        }
    }

    [Fact]
    public async Task Test5_EditShiftB_ShiftALossUntouched_ShiftBLossUpdates()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 9, 15);
        var (shiftA, shiftB) = await CreateShiftsAsync(date);
        string dsmName = "Ramesh Jadhav";

        // 1. Save Shift A (shortage ₹2.50)
        using (var ctxA = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            await dsmService.SaveCompleteEntryWithContextAsync(
                ctxA, date, "A", dsmName, 1,
                nozzleReadings: new List<NozzleReading> { new() { NozzleNumber = 1, OpeningReading = 0, ClosingReading = 10, SaleLitres = 10, Rate = 100, Amount = 1000 } },
                payment: new PaymentCollection { CashDeposit = 997.50 },
                debits: new List<DebitEntry>(), testingEntries: new List<TestingEntry>(),
                expenses: new List<Expense>(), cashDenominations: new List<CashDenomination>()
            );
        }

        // 2. Save Shift B initially (shortage ₹22.60)
        int shiftBEntryId;
        using (var ctxB = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var resB = await dsmService.SaveCompleteEntryWithContextAsync(
                ctxB, date, "B", dsmName, 1,
                nozzleReadings: new List<NozzleReading> { new() { NozzleNumber = 1, OpeningReading = 10, ClosingReading = 20, SaleLitres = 10, Rate = 100, Amount = 1000 } },
                payment: new PaymentCollection { CashDeposit = 977.40 },
                debits: new List<DebitEntry>(), testingEntries: new List<TestingEntry>(),
                expenses: new List<Expense>(), cashDenominations: new List<CashDenomination>()
            );
            shiftBEntryId = resB.Data!.DsmEntryId;
        }

        // Verify initial state: Shift A = 2.50, Shift B = 22.60
        using (var verifyCtx1 = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var debtors = await verifyCtx1.DsmPersonalDebtors.Where(d => d.DsmName == dsmName).ToListAsync();
            Assert.Equal(2, debtors.Count);
            Assert.Equal(2.50, debtors.First(d => d.Remarks!.Contains("Shift A")).Amount, precision: 2);
            Assert.Equal(22.60, debtors.First(d => d.Remarks!.Contains("Shift B")).Amount, precision: 2);
        }

        // 3. Edit Shift B: collection changed from 977.40 to 982.00 (shortage becomes ₹18.00 instead of ₹22.60)
        using (var ctxEditB = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            await dsmService.SaveCompleteEntryWithContextAsync(
                ctxEditB, date, "B", dsmName, 1,
                nozzleReadings: new List<NozzleReading> { new() { NozzleNumber = 1, OpeningReading = 10, ClosingReading = 20, SaleLitres = 10, Rate = 100, Amount = 1000 } },
                payment: new PaymentCollection { CashDeposit = 982.00 },
                debits: new List<DebitEntry>(), testingEntries: new List<TestingEntry>(),
                expenses: new List<Expense>(), cashDenominations: new List<CashDenomination>(),
                existingEntryId: shiftBEntryId
            );
        }

        // 4. Verify post-edit state: Shift A remains ₹2.50 untouched, Shift B is now ₹18.00
        using (var verifyCtx2 = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var debtors = await verifyCtx2.DsmPersonalDebtors.Where(d => d.DsmName == dsmName).ToListAsync();
            Assert.Equal(2, debtors.Count);
            
            var shiftADebtor = debtors.First(d => d.Remarks!.Contains("Shift A"));
            var shiftBDebtor = debtors.First(d => d.Remarks!.Contains("Shift B"));

            Assert.Equal(2.50, shiftADebtor.Amount, precision: 2);
            Assert.Equal(18.00, shiftBDebtor.Amount, precision: 2);
            Assert.Equal(20.50, debtors.Sum(d => d.Amount), precision: 2);
        }
    }

    [Fact]
    public async Task Test6_ReconciliationAndPrints_ValuesMatchNoToleranceRule()
    {
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var reportService = _serviceProvider.GetRequiredService<IReportService>();
        var date = new DateTime(2026, 9, 16);
        var (shiftA, shiftB) = await CreateShiftsAsync(date);
        string dsmName = "Ramesh Jadhav";

        // Shift A: Gross = 10,824.10, Testing = 3,191.60 -> Net = 7,632.50. Coll = 7,630.00 -> Mismatch = -2.50
        DsmEntry entryA;
        using (var ctxA = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var resA = await dsmService.SaveCompleteEntryWithContextAsync(
                ctxA, date, "A", dsmName, 1,
                nozzleReadings: new List<NozzleReading> { new() { NozzleNumber = 1, OpeningReading = 0, ClosingReading = 108.241, SaleLitres = 108.241, Rate = 100, Amount = 10824.10 } },
                payment: new PaymentCollection { CashDeposit = 5030.00 },
                debits: new List<DebitEntry> { new() { DebtorName = "Credit Party", Amount = 2500.00 } },
                testingEntries: new List<TestingEntry> { new() { FuelType = "MS-I", Litres = 31.916, Rate = 100, Amount = 3191.60 } },
                expenses: new List<Expense> { new() { Description = "Lunch", Amount = 100.00 } },
                cashDenominations: new List<CashDenomination>()
            );
            entryA = resA.Data!;
        }

        // Shift B: Gross = 14,842.60, Testing = 0 -> Net = 14,842.60. Coll = 14,820.00 -> Mismatch = -22.60
        DsmEntry entryB;
        using (var ctxB = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var resB = await dsmService.SaveCompleteEntryWithContextAsync(
                ctxB, date, "B", dsmName, 1,
                nozzleReadings: new List<NozzleReading> { new() { NozzleNumber = 1, OpeningReading = 200, ClosingReading = 348.426, SaleLitres = 148.426, Rate = 100, Amount = 14842.60 } },
                payment: new PaymentCollection { CashDeposit = 13170.00 },
                debits: new List<DebitEntry> { new() { DebtorName = "Credit Party", Amount = 1500.00 } },
                testingEntries: new List<TestingEntry>(),
                expenses: new List<Expense> { new() { Description = "Dinner", Amount = 150.00 } },
                cashDenominations: new List<CashDenomination>()
            );
            entryB = resB.Data!;
        }

        // Load complete entries with their navigation properties for report calculation
        using (var queryCtx = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var fullEntries = await queryCtx.DsmEntries
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                .Include(e => e.CashDenominations)
                .Include(e => e.DebitEntries)
                .Include(e => e.Expenses)
                .Include(e => e.TestingEntries)
                .Include(e => e.PersonalDebtors)
                .Include(e => e.Shift)
                .Where(e => e.ShiftId == shiftA.ShiftId || e.ShiftId == shiftB.ShiftId)
                .ToListAsync();

            var dayReport = reportService.CalculateDayReport(
                startDate: date,
                endDate: date,
                entries: fullEntries,
                allExpenses: new List<Expense>(),
                repayments: new List<CreditorRepayment>(),
                hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
                stationName: "Mitali Service Station"
            );

            // Assert: Reconciliation values
            Assert.Equal(25666.70, dayReport.TotalFuelAmount, precision: 2); // Gross: 10,824.10 + 14,842.60
            Assert.Equal(250.00, dayReport.ExpensesTotal, precision: 2);     // Expenses: 100.00 + 150.00
            Assert.Equal(4000.00, dayReport.CreditorsTotal, precision: 2);   // Debits: 2,500.00 + 1,500.00
            Assert.Equal(3191.60, dayReport.TestingSummaryItems.Sum(t => t.Amount), precision: 2); // Testing: 3,191.60

            // DSM Short in Day Report breakdown
            Assert.Equal(25.10, dayReport.TotalDsmShort, precision: 2);      // 2.50 + 22.60

            // Personal Debtors print list
            Assert.Equal(2, dayReport.PersonalDebtors.Count);
            Assert.Contains(dayReport.PersonalDebtors, pd => Math.Abs(pd.Amount - 2.50) < 0.01);
            Assert.Contains(dayReport.PersonalDebtors, pd => Math.Abs(pd.Amount - 22.60) < 0.01);
            Assert.Equal(25.10, dayReport.PersonalDebtors.Sum(pd => pd.Amount), precision: 2);
        }
    }
}
