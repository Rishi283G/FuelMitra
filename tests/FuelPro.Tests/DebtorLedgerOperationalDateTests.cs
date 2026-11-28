using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.UI;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;

namespace FuelPro.Tests;

public class DebtorLedgerOperationalDateTests : IDisposable
{
    private readonly string _dbName;
    private readonly ServiceProvider _serviceProvider;
    private readonly IServiceProvider _originalServices;

    public DebtorLedgerOperationalDateTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");

        _dbName = "DebtorLedgerTests_" + Guid.NewGuid().ToString("N");

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: _dbName),
            ServiceLifetime.Transient);
        services.AddTransient<DbContext>(sp => sp.GetRequiredService<FuelProDbContext>());

        services.AddSingleton<ICreditorRepository, CreditorRepository>();
        services.AddSingleton<ICreditorRepaymentRepository, CreditorRepaymentRepository>();
        services.AddSingleton<IDsmEntryRepository, DsmEntryRepository>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<AuthService>();
        services.AddScoped<IDayLockService, DayLockService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddSingleton<PrintService>();
        services.AddSingleton<ExcelExportService>();
        services.AddSingleton<IShiftCycleService, ShiftCycleService>();

        _serviceProvider = services.BuildServiceProvider();

        // Save original App.Services if any, and set test service provider
        var appServicesProp = typeof(App).GetProperty("Services", BindingFlags.Public | BindingFlags.Static);
        _originalServices = (IServiceProvider)appServicesProp?.GetValue(null)!;
        appServicesProp?.SetValue(null, _serviceProvider);
    }

    private FuelProDbContext CreateContext()
    {
        return _serviceProvider.GetRequiredService<FuelProDbContext>();
    }

    public void Dispose()
    {
        var appServicesProp = typeof(App).GetProperty("Services", BindingFlags.Public | BindingFlags.Static);
        appServicesProp?.SetValue(null, _originalServices);
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task Scenario1_DsmEntry_SameDayApproval_UsesShiftDate()
    {
        // 1. DSM entry created and approved on the same operational date
        var debtor = new Creditor { Name = "TEST DEBTOR 1", Phone = "1234567890", IsActive = true };
        var shift = new Shift { ShiftDate = new DateTime(2026, 11, 1), ShiftType = "A" };

        using (var context = CreateContext())
        {
            context.Creditors.Add(debtor);
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var dsm = new DsmEntry { ShiftId = shift.ShiftId, CreatedAt = new DateTime(2026, 11, 1, 10, 0, 0) };
            context.DsmEntries.Add(dsm);
            await context.SaveChangesAsync();

            var debit = new DebitEntry
            {
                DebtorName = debtor.Name,
                Amount = 5000,
                DsmEntryId = dsm.DsmEntryId,
                CreatedAt = new DateTime(2026, 11, 1, 10, 0, 0)
            };
            context.DebitEntries.Add(debit);
            await context.SaveChangesAsync();
        }

        var vm = new DebtorManagementViewModel();
        vm.LedgerDebtorName = debtor.Name;
        vm.LedgerStartDate = new DateTime(2026, 11, 1);
        vm.LedgerEndDate = new DateTime(2026, 11, 1);

        await vm.LoadLedgerAsync();

        Assert.True(string.IsNullOrEmpty(vm.LedgerStatus), $"LedgerStatus was: {vm.LedgerStatus}");
        var row = vm.LedgerTransactions.FirstOrDefault(t => t.TransactionType == "Debt");
        Assert.NotNull(row);
        Assert.Equal(new DateTime(2026, 11, 1), row.Date);
        Assert.Equal(5000, row.Debit);
        Assert.Equal(5000, vm.LedgerOutstandingBalance);
    }

    [Fact]
    public async Task Scenario2_DsmEntry_DelayedApproval_UsesOperationalShiftDateNotCreatedAt()
    {
        // 2. DSM entry created several days after its operational shift (e.g. Nov 3 shift approved on Nov 6)
        var debtor = new Creditor { Name = "TEST DEBTOR 2", Phone = "1234567890", IsActive = true };
        var shift = new Shift { ShiftDate = new DateTime(2026, 11, 3), ShiftType = "B" };

        using (var context = CreateContext())
        {
            context.Creditors.Add(debtor);
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var dsm = new DsmEntry { ShiftId = shift.ShiftId, CreatedAt = new DateTime(2026, 11, 6, 11, 31, 41) };
            context.DsmEntries.Add(dsm);
            await context.SaveChangesAsync();

            var debit = new DebitEntry
            {
                DebtorName = debtor.Name,
                Amount = 18166.13,
                DsmEntryId = dsm.DsmEntryId,
                CreatedAt = new DateTime(2026, 11, 6, 11, 31, 41) // Created days later!
            };
            context.DebitEntries.Add(debit);
            await context.SaveChangesAsync();
        }

        var vm = new DebtorManagementViewModel();
        vm.LedgerDebtorName = debtor.Name;
        // Filter covers the operational shift window Nov 1 to Nov 4
        vm.LedgerStartDate = new DateTime(2026, 11, 1);
        vm.LedgerEndDate = new DateTime(2026, 11, 4);

        await vm.LoadLedgerAsync();

        Assert.True(string.IsNullOrEmpty(vm.LedgerStatus), $"LedgerStatus was: {vm.LedgerStatus}");
        // Prior bug: date would be Nov 6 and would NOT appear in Nov 1 - Nov 4 filter!
        // Fixed behavior: date is Nov 3 and MUST appear!
        var row = vm.LedgerTransactions.FirstOrDefault(t => t.TransactionType == "Debt");
        Assert.NotNull(row);
        Assert.Equal(new DateTime(2026, 11, 3), row.Date);
        Assert.Equal(18166.13, row.Debit);
        Assert.Equal(18166.13, vm.LedgerOutstandingBalance);

        // Verify that if we filter only for Nov 5 - Nov 6, this transaction does NOT appear in period rows
        vm.LedgerStartDate = new DateTime(2026, 11, 5);
        vm.LedgerEndDate = new DateTime(2026, 11, 6);
        await vm.LoadLedgerAsync();

        var rowExcluded = vm.LedgerTransactions.FirstOrDefault(t => t.TransactionType == "Debt");
        Assert.Null(rowExcluded);
        // It is instead absorbed into the opening balance
        Assert.Equal(18166.13, vm.LedgerOpeningBalance);
    }

    [Fact]
    public async Task Scenario3_ManualDebtorAdjustment_WithoutShift_UsesCreatedAt()
    {
        // 3. Manual debtor adjustment without a valid DSM shift date (ShiftDate is MinValue / default)
        var debtor = new Creditor { Name = "TEST DEBTOR 3", Phone = "1234567890", IsActive = true };
        var shiftWithoutDate = new Shift { ShiftDate = DateTime.MinValue, ShiftType = "—" };

        using (var context = CreateContext())
        {
            context.Creditors.Add(debtor);
            context.Shifts.Add(shiftWithoutDate);
            await context.SaveChangesAsync();

            var dsm = new DsmEntry { ShiftId = shiftWithoutDate.ShiftId, CreatedAt = new DateTime(2026, 11, 4, 17, 5, 43) };
            context.DsmEntries.Add(dsm);
            await context.SaveChangesAsync();

            var manualDebit = new DebitEntry
            {
                DebtorName = debtor.Name,
                Amount = 7500,
                DsmEntryId = dsm.DsmEntryId,
                CreatedAt = new DateTime(2026, 11, 4, 17, 5, 43),
                Remarks = "Manual Profile Adjustment"
            };
            context.DebitEntries.Add(manualDebit);
            await context.SaveChangesAsync();
        }

        var vm = new DebtorManagementViewModel();
        vm.LedgerDebtorName = debtor.Name;
        vm.LedgerStartDate = new DateTime(2026, 11, 1);
        vm.LedgerEndDate = new DateTime(2026, 11, 5);

        await vm.LoadLedgerAsync();

        Assert.True(string.IsNullOrEmpty(vm.LedgerStatus), $"LedgerStatus was: {vm.LedgerStatus}");
        var row = vm.LedgerTransactions.FirstOrDefault(t => t.TransactionType == "Debt");
        Assert.NotNull(row);
        Assert.Equal(new DateTime(2026, 11, 4), row.Date);
        Assert.Equal(7500, row.Debit);
        Assert.Equal(7500, vm.LedgerOutstandingBalance);
    }

    [Fact]
    public async Task Scenario4_RepaymentTransaction_Preserved()
    {
        // 4. Repayment transaction
        var debtor = new Creditor { Name = "TEST DEBTOR 4", Phone = "1234567890", IsActive = true };

        using (var context = CreateContext())
        {
            context.Creditors.Add(debtor);
            await context.SaveChangesAsync();

            var repayment = new CreditorRepayment
            {
                CreditorName = debtor.Name,
                Amount = 10000,
                RepaymentDate = new DateTime(2026, 11, 1),
                CreatedAt = new DateTime(2026, 11, 1, 19, 16, 21),
                PaymentMode = "BankTransfer",
                ChequeNo = "NEFT12345"
            };
            context.CreditorRepayments.Add(repayment);
            await context.SaveChangesAsync();
        }

        var vm = new DebtorManagementViewModel();
        vm.LedgerDebtorName = debtor.Name;
        vm.LedgerStartDate = new DateTime(2026, 11, 1);
        vm.LedgerEndDate = new DateTime(2026, 11, 2);

        await vm.LoadLedgerAsync();

        Assert.True(string.IsNullOrEmpty(vm.LedgerStatus), $"LedgerStatus was: {vm.LedgerStatus}");
        var row = vm.LedgerTransactions.FirstOrDefault(t => t.TransactionType == "Repayment");
        Assert.NotNull(row);
        Assert.Equal(new DateTime(2026, 11, 1), row.Date);
        Assert.Equal(10000, row.Credit);
        Assert.Equal(-10000, vm.LedgerOutstandingBalance);
    }

    [Fact]
    public async Task Scenario5_TransactionOutsideSelectedDateRange_ExcludedFromPeriodRows()
    {
        // 5. Transaction outside the selected date range
        var debtor = new Creditor { Name = "TEST DEBTOR 5", Phone = "1234567890", IsActive = true };
        var shift = new Shift { ShiftDate = new DateTime(2026, 11, 10), ShiftType = "A" };

        using (var context = CreateContext())
        {
            context.Creditors.Add(debtor);
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var dsm = new DsmEntry { ShiftId = shift.ShiftId, CreatedAt = new DateTime(2026, 11, 10, 8, 0, 0) };
            context.DsmEntries.Add(dsm);
            await context.SaveChangesAsync();

            var debit = new DebitEntry
            {
                DebtorName = debtor.Name,
                Amount = 3000,
                DsmEntryId = dsm.DsmEntryId,
                CreatedAt = new DateTime(2026, 11, 10, 8, 0, 0)
            };
            context.DebitEntries.Add(debit);
            await context.SaveChangesAsync();
        }

        var vm = new DebtorManagementViewModel();
        vm.LedgerDebtorName = debtor.Name;
        vm.LedgerStartDate = new DateTime(2026, 11, 1);
        vm.LedgerEndDate = new DateTime(2026, 11, 5);

        await vm.LoadLedgerAsync();

        Assert.True(string.IsNullOrEmpty(vm.LedgerStatus), $"LedgerStatus was: {vm.LedgerStatus}");
        // Transaction on Nov 10 is AFTER the filter end date Nov 5
        var row = vm.LedgerTransactions.FirstOrDefault(t => t.TransactionType == "Debt");
        Assert.Null(row);
        Assert.Equal(0, vm.LedgerOpeningBalance);
        Assert.Equal(0, vm.LedgerOutstandingBalance);
    }

    [Fact]
    public async Task Scenario6_OpeningBalanceBeforeSelectedDateRange_RollsUpCorrectly()
    {
        // 6. Opening balance before the selected date range
        var debtor = new Creditor { Name = "TEST DEBTOR 6", Phone = "1234567890", IsActive = true };
        var priorShift = new Shift { ShiftDate = new DateTime(2026, 10, 25), ShiftType = "A" };
        var currentShift = new Shift { ShiftDate = new DateTime(2026, 11, 2), ShiftType = "B" };

        using (var context = CreateContext())
        {
            context.Creditors.Add(debtor);
            context.Shifts.Add(priorShift);
            context.Shifts.Add(currentShift);
            await context.SaveChangesAsync();

            var priorDsm = new DsmEntry { ShiftId = priorShift.ShiftId, CreatedAt = new DateTime(2026, 10, 25, 9, 0, 0) };
            var currentDsm = new DsmEntry { ShiftId = currentShift.ShiftId, CreatedAt = new DateTime(2026, 11, 2, 14, 0, 0) };
            context.DsmEntries.AddRange(priorDsm, currentDsm);
            await context.SaveChangesAsync();

            var priorDebit = new DebitEntry
            {
                DebtorName = debtor.Name,
                Amount = 15000,
                DsmEntryId = priorDsm.DsmEntryId,
                CreatedAt = new DateTime(2026, 10, 25, 9, 0, 0)
            };
            var priorRepayment = new CreditorRepayment
            {
                CreditorName = debtor.Name,
                Amount = 5000,
                RepaymentDate = new DateTime(2026, 10, 28),
                CreatedAt = new DateTime(2026, 10, 28, 12, 0, 0)
            };
            var currentDebit = new DebitEntry
            {
                DebtorName = debtor.Name,
                Amount = 2500,
                DsmEntryId = currentDsm.DsmEntryId,
                CreatedAt = new DateTime(2026, 11, 2, 14, 0, 0)
            };
            context.DebitEntries.AddRange(priorDebit, currentDebit);
            context.CreditorRepayments.Add(priorRepayment);
            await context.SaveChangesAsync();
        }

        var vm = new DebtorManagementViewModel();
        vm.LedgerDebtorName = debtor.Name;
        // Period is Nov 1 to Nov 5
        vm.LedgerStartDate = new DateTime(2026, 11, 1);
        vm.LedgerEndDate = new DateTime(2026, 11, 5);

        await vm.LoadLedgerAsync();

        Assert.True(string.IsNullOrEmpty(vm.LedgerStatus), $"LedgerStatus was: {vm.LedgerStatus}");
        // Opening balance before Nov 1 must be: 15,000 (prior debit) - 5,000 (prior repayment) = 10,000
        Assert.Equal(10000, vm.LedgerOpeningBalance);

        // Period rows: exactly 1 current debit (2,500)
        var periodDebits = vm.LedgerTransactions.Where(t => t.TransactionType == "Debt").ToList();
        Assert.Single(periodDebits);
        Assert.Equal(2500, periodDebits[0].Debit);

        // Period running balance: 10,000 + 2,500 = 12,500
        Assert.Equal(12500, vm.LedgerOutstandingBalance);
    }

    [Fact]
    public async Task Scenario7_ProfileAdjustment_WithValidShiftDate_UsesHistoricalCreatedAt()
    {
        // 7. Profile adjustment with:
        //    - A valid DsmEntryId
        //    - A valid associated ShiftDate (e.g. 2026-11-05)
        //    - A historical CreatedAt date selected by the manager (e.g. 2026-10-15)
        //    - Remarks identifying it as a profile adjustment ("Profile Edit Adjustment")
        // Assert that the ledger uses CreatedAt (2026-10-15) rather than ShiftDate (2026-11-05).
        var debtor = new Creditor { Name = "TEST DEBTOR PROFILE ADJ", Phone = "9876543210", IsActive = true };
        var shiftOnNov5 = new Shift { ShiftDate = new DateTime(2026, 11, 5), ShiftType = "A" };
        var historicalCreatedAt = new DateTime(2026, 10, 15, 12, 30, 0);

        using (var context = CreateContext())
        {
            context.Creditors.Add(debtor);
            context.Shifts.Add(shiftOnNov5);
            await context.SaveChangesAsync();

            var dsm = new DsmEntry { ShiftId = shiftOnNov5.ShiftId, PumpId = 1, CreatedAt = new DateTime(2026, 11, 5, 20, 0, 0) };
            context.DsmEntries.Add(dsm);
            await context.SaveChangesAsync();

            var profileAdjDebit = new DebitEntry
            {
                DebtorName = debtor.Name,
                Amount = 14250,
                DsmEntryId = dsm.DsmEntryId, // Valid DsmEntryId linked to Nov 5 shift
                CreatedAt = historicalCreatedAt, // Historical date selected by manager (Oct 15)
                Remarks = "Profile Edit Adjustment"
            };
            context.DebitEntries.Add(profileAdjDebit);
            await context.SaveChangesAsync();
        }

        var vm = new DebtorManagementViewModel();
        vm.LedgerDebtorName = debtor.Name;
        // Filter covering the historical period: 2026-10-01 to 2026-10-31
        vm.LedgerStartDate = new DateTime(2026, 10, 1);
        vm.LedgerEndDate = new DateTime(2026, 10, 31);

        await vm.LoadLedgerAsync();

        Assert.True(string.IsNullOrEmpty(vm.LedgerStatus), $"LedgerStatus was: {vm.LedgerStatus}");
        var row = vm.LedgerTransactions.FirstOrDefault(t => t.TransactionType == "Debt");
        Assert.NotNull(row);
        // Must use historical CreatedAt (2026-10-15), NOT ShiftDate (2026-11-05)
        Assert.Equal(new DateTime(2026, 10, 15), row.Date);
        Assert.Equal(14250, row.Debit);
        Assert.Equal(14250, vm.LedgerOutstandingBalance);

        // Verification: If filter is set to Nov 1 to Nov 10 (which includes the ShiftDate Nov 5),
        // the transaction should NOT appear in the Nov period rows because its true date is Oct 15!
        // It must instead be aggregated into the Opening Balance before Nov 1.
        vm.LedgerStartDate = new DateTime(2026, 11, 1);
        vm.LedgerEndDate = new DateTime(2026, 11, 10);
        await vm.LoadLedgerAsync();

        var novRow = vm.LedgerTransactions.FirstOrDefault(t => t.TransactionType == "Debt");
        Assert.Null(novRow);
        Assert.Equal(14250, vm.LedgerOpeningBalance);
    }
}
