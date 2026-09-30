using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

public class LiveWorkflowVerificationTests
{
    private readonly string _liveDbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FuelPro", "fuelPro.db");

    private ServiceProvider CreateLiveServices()
    {
        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application();
        }

        var services = new ServiceCollection();

        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_liveDbPath}"),
            ServiceLifetime.Transient);

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
        services.AddTransient<PrintService>();
        services.AddTransient<ExcelExportService>();
        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddTransient<AuthService>();
        services.AddTransient<ICredentialFileService>(sp => new LocalCredentialFileService(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro")));
        services.AddSingleton<IFeatureToggleService, FeatureToggleService>();

        // ViewModel
        services.AddTransient<DsmPersonalDebtorViewModel>();

        var sp = services.BuildServiceProvider();
        typeof(App).GetProperty("Services", BindingFlags.Public | BindingFlags.Static)!.SetValue(null, sp);
        return sp;
    }

    [Fact]
    public async Task Verify_LiveDb_RameshJadhav_StateAndLedger_ShiftA250_ShiftB2260_Total2510()
    {
        Assert.True(File.Exists(_liveDbPath), $"Live database missing at {_liveDbPath}");

        var sp = CreateLiveServices();
        var vm = sp.GetRequiredService<DsmPersonalDebtorViewModel>();

        await vm.LoadDataAsync();

        var rameshSummary = vm.DsmSummaries.FirstOrDefault(s =>
            string.Equals(s.DsmName, "Ramesh Jadhav", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(rameshSummary);
        Assert.Equal(25.10, rameshSummary.TotalBorrowed, precision: 2);

        // Select Ramesh Jadhav and inspect ledger
        vm.SelectedSummary = rameshSummary;
        vm.LedgerStartDate = new DateTime(2026, 9, 1);
        vm.LedgerEndDate = new DateTime(2026, 9, 30);
        await vm.LoadLedgerAsync();

        Assert.True(vm.DsmLedger.Count >= 2, $"Expected at least 2 ledger rows, found {vm.DsmLedger.Count}");

        var shiftARow = vm.DsmLedger.FirstOrDefault(r => r.Remarks != null && r.Remarks.Contains("Shift A"));
        Assert.NotNull(shiftARow);
        Assert.Equal(2.50, shiftARow.Amount, precision: 2);

        var shiftBRow = vm.DsmLedger.FirstOrDefault(r => r.Remarks != null && r.Remarks.Contains("Shift B"));
        Assert.NotNull(shiftBRow);
        Assert.Equal(22.60, shiftBRow.Amount, precision: 2);
    }

    [Fact]
    public async Task Verify_Below10Shortage_CreatesExactShortage_NotZero()
    {
        var sp = CreateLiveServices();
        var dsmService = sp.GetRequiredService<DsmEntryService>();
        var db = sp.GetRequiredService<FuelProDbContext>();

        string testDsmName = "Controlled Test DSM " + Guid.NewGuid().ToString("N")[..6];
        var testDate = DateTime.Today;

        try
        {
            // 1. Controlled shift with shortage below ₹10 (Shortage = ₹7.50)
            // Gross: 1000.00, Collection: 992.50 -> Mismatch = -7.50
            var result = await dsmService.SaveCompleteEntryWithContextAsync(
                db, testDate, "A", testDsmName, pumpId: 1,
                nozzleReadings: new List<NozzleReading>
                {
                    new() { NozzleNumber = 1, OpeningReading = 0, ClosingReading = 10, SaleLitres = 10, Rate = 100, Amount = 1000.00 }
                },
                payment: new PaymentCollection { CashDeposit = 992.50 },
                debits: new List<DebitEntry>(),
                testingEntries: new List<TestingEntry>(),
                expenses: new List<Expense>(),
                cashDenominations: new List<CashDenomination>()
            );

            Assert.True(result.Success, $"Failed to save entry: {result.Error}");

            // Verify DsmPersonalDebtors in database
            var debtors = await db.DsmPersonalDebtors
                .Where(d => d.DsmName == testDsmName)
                .ToListAsync();

            Assert.Single(debtors);
            Assert.Equal(7.50, debtors[0].Amount, precision: 2);
            Assert.Contains("Auto Shift Shortage", debtors[0].Remarks);

            // 2. Controlled shift with shortage above ₹10 (Shortage = ₹18.40)
            // Gross: 2000.00, Collection: 1981.60 -> Mismatch = -18.40
            var resultB = await dsmService.SaveCompleteEntryWithContextAsync(
                db, testDate, "B", testDsmName, pumpId: 1,
                nozzleReadings: new List<NozzleReading>
                {
                    new() { NozzleNumber = 1, OpeningReading = 10, ClosingReading = 30, SaleLitres = 20, Rate = 100, Amount = 2000.00 }
                },
                payment: new PaymentCollection { CashDeposit = 1981.60 },
                debits: new List<DebitEntry>(),
                testingEntries: new List<TestingEntry>(),
                expenses: new List<Expense>(),
                cashDenominations: new List<CashDenomination>()
            );

            Assert.True(resultB.Success, $"Failed to save second entry: {resultB.Error}");

            debtors = await db.DsmPersonalDebtors
                .Where(d => d.DsmName == testDsmName)
                .OrderBy(d => d.Id)
                .ToListAsync();

            Assert.Equal(2, debtors.Count);
            Assert.Equal(7.50, debtors[0].Amount, precision: 2);
            Assert.Equal(18.40, debtors[1].Amount, precision: 2);

            double total = debtors.Sum(d => d.Amount);
            Assert.Equal(25.90, total, precision: 2);
        }
        finally
        {
            // Clean up test entries so live database stays spotless
            var testDebtors = await db.DsmPersonalDebtors.Where(d => d.DsmName == testDsmName).ToListAsync();
            var testEntries = await db.DsmEntries.Where(e => e.DsmName == testDsmName).ToListAsync();
            db.DsmPersonalDebtors.RemoveRange(testDebtors);
            db.DsmEntries.RemoveRange(testEntries);
            await db.SaveChangesAsync();
        }
    }
}
