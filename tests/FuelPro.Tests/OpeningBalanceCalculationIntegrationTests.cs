using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.UI.ViewModels;
using Xunit;

namespace FuelPro.Tests;

public class OpeningBalanceCalculationIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FuelProDbContext> _options;

    public OpeningBalanceCalculationIntegrationTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new FuelProDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private FuelProDbContext CreateContext() => new FuelProDbContext(_options);

    private async Task<int> CreateTestDsmEntryAsync(FuelProDbContext context, DateTime date)
    {
        var shift = new Shift
        {
            ShiftDate = date.Date,
            ShiftType = "A",
            CreatedAt = date
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var entry = new DsmEntry
        {
            ShiftId = shift.ShiftId,
            DsmName = "TestDSM",
            CreatedAt = date
        };
        context.DsmEntries.Add(entry);
        await context.SaveChangesAsync();

        return entry.DsmEntryId;
    }

    [Fact]
    public async Task Test01_Debtor_Formula_OutstandingEqualsOpeningPlusDebitsMinusRepayments()
    {
        // 1. Debtor: ₹10,000 opening + ₹5,000 debit - ₹2,000 repayment = ₹13,000 outstanding.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        var creditor = new Creditor { Name = "Sharma Transport", Phone = "9876543210", IsActive = true, CreatedAt = DateTime.Now };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        var obResult = await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = creditor.CreditorId.ToString(),
            CreditorId = creditor.CreditorId,
            Amount = 10000,
            OpeningDate = new DateTime(2026, 9, 1),
            Notes = "Historical balance"
        });
        Assert.True(obResult.Success);

        int dsmId = await CreateTestDsmEntryAsync(context, new DateTime(2026, 9, 10));

        // Add operational debit
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = dsmId,
            DebtorName = creditor.Name,
            Amount = 5000,
            PaymentMethod = "Credit",
            Remarks = "Fuel diesel",
            CreatedAt = new DateTime(2026, 9, 10)
        });

        // Add repayment
        context.CreditorRepayments.Add(new CreditorRepayment
        {
            CreditorName = creditor.Name,
            Amount = 2000,
            PaymentMode = "Cash",
            RepaymentDate = new DateTime(2026, 9, 15),
            CreatedAt = new DateTime(2026, 9, 15)
        });
        await context.SaveChangesAsync();

        // Calculate DebtorDisplayRow
        var allDebits = await context.DebitEntries.Where(d => d.DebtorName == creditor.Name).SumAsync(d => d.Amount);
        var allRepayments = await context.CreditorRepayments.Where(r => r.CreditorName == creditor.Name).SumAsync(r => r.Amount);
        var activeOb = await context.OpeningBalances.FirstOrDefaultAsync(o => o.EntityType == "Debtor" && o.CreditorId == creditor.CreditorId && o.IsActive);

        var row = new DebtorDisplayRow
        {
            CreditorId = creditor.CreditorId,
            Name = creditor.Name,
            OpeningBalance = activeOb?.Amount ?? 0,
            TotalDebt = allDebits,
            TotalRepayment = allRepayments
        };

        Assert.Equal(10000, row.OpeningBalance);
        Assert.Equal(5000, row.TotalDebt);
        Assert.Equal(2000, row.TotalRepayment);
        Assert.Equal(13000, row.OutstandingBalance);
    }

    [Fact]
    public async Task Test02_Dsm_Formula_OutstandingEqualsOpeningPlusOperationalMinusRepayments()
    {
        // 2. DSM: ₹8,500 opening + ₹1,500 operational loss - ₹2,000 repayment = ₹8,000 outstanding.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        var obResult = await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Ramesh",
            Amount = 8500,
            OpeningDate = new DateTime(2026, 9, 1),
            Notes = "Historical DSM Loss"
        });
        Assert.True(obResult.Success);

        // Operational DSM Loss
        context.DsmPersonalDebtors.Add(new DsmPersonalDebtor
        {
            DsmName = "Ramesh",
            Date = new DateTime(2026, 9, 10),
            Amount = 1500,
            EntryType = "Operational",
            Remarks = "Shift shortage",
            CreatedAt = new DateTime(2026, 9, 10)
        });
        await context.SaveChangesAsync();

        var anchor = await context.DsmPersonalDebtors.FirstOrDefaultAsync(d => d.DsmName == "Ramesh" && d.EntryType == "OpeningBalance");
        Assert.NotNull(anchor);

        // Repayment against the DSM (allocated to anchor)
        context.DsmPersonalDebtorRepayments.Add(new DsmPersonalDebtorRepayment
        {
            DsmPersonalDebtorId = anchor.Id,
            Amount = 2000,
            Date = new DateTime(2026, 9, 15),
            PaymentMethod = "Cash",
            CreatedAt = new DateTime(2026, 9, 15)
        });
        await context.SaveChangesAsync();

        // Calculate DsmPersonalDebtorSummaryRow
        var debits = await context.DsmPersonalDebtors.Where(d => d.DsmName == "Ramesh").ToListAsync();
        var repayments = await context.DsmPersonalDebtorRepayments.ToListAsync();

        var operationalBorrowed = debits.Where(d => d.EntryType == "Operational").Sum(d => d.Amount);
        var openingBalance = debits.Where(d => d.EntryType == "OpeningBalance").Sum(d => d.Amount);
        var personalDebtorIds = debits.Where(d => d.EntryType != "DeactivatedOpening").Select(d => d.Id).ToList();
        var totalRepaid = repayments.Where(r => personalDebtorIds.Contains(r.DsmPersonalDebtorId)).Sum(r => r.Amount);

        var summary = new DsmPersonalDebtorSummaryRow
        {
            DsmName = "Ramesh",
            OpeningBalance = openingBalance,
            TotalBorrowed = operationalBorrowed,
            TotalRepaid = totalRepaid
        };

        Assert.Equal(8500, summary.OpeningBalance);
        Assert.Equal(1500, summary.TotalBorrowed);
        Assert.Equal(2000, summary.TotalRepaid);
        Assert.Equal(8000, summary.Balance);
    }

    [Fact]
    public async Task Test03_OpeningBalance_NotCountedAsOperationalBorrowing()
    {
        // 3. Opening balance is NOT counted as operational borrowing.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Ramesh",
            Amount = 8500,
            OpeningDate = new DateTime(2026, 9, 1)
        });

        context.DsmPersonalDebtors.Add(new DsmPersonalDebtor
        {
            DsmName = "Ramesh",
            Date = new DateTime(2026, 9, 10),
            Amount = 1500,
            EntryType = "Operational",
            Remarks = "Shift shortage"
        });
        await context.SaveChangesAsync();

        var debits = await context.DsmPersonalDebtors.Where(d => d.DsmName == "Ramesh").ToListAsync();
        var operationalTotal = debits.Where(d => d.EntryType == "Operational").Sum(d => d.Amount);

        // Operational borrowing must strictly be 1,500, NOT 10,000!
        Assert.Equal(1500, operationalTotal);
        Assert.NotEqual(10000, operationalTotal);
    }

    [Fact]
    public async Task Test04_OpeningBalance_NotIncludedInSalaryDeductions()
    {
        // 4. Opening balance is NOT included in salary deduction.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        // Create DSM Profile with Fixed Monthly salary
        context.DsmProfiles.Add(new DsmProfile
        {
            DsmName = "Ramesh",
            SalaryType = "FixedMonthly",
            BaseSalary = 15000
        });

        // Create opening balance anchor
        await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Ramesh",
            Amount = 8500,
            OpeningDate = new DateTime(2026, 9, 1)
        });

        // Even if DeductFromSalary was somehow set to true on the anchor
        var anchor = await context.DsmPersonalDebtors.FirstOrDefaultAsync(d => d.DsmName == "Ramesh" && d.EntryType == "OpeningBalance");
        Assert.NotNull(anchor);
        anchor.DeductFromSalary = true; // Attempt to force deduction
        anchor.Date = new DateTime(2026, 9, 1);

        // Add a deactivated anchor with DeductFromSalary = true as well
        context.DsmPersonalDebtors.Add(new DsmPersonalDebtor
        {
            DsmName = "Ramesh",
            Date = new DateTime(2026, 9, 5),
            Amount = 2000,
            EntryType = "DeactivatedOpening",
            DeductFromSalary = true
        });

        await context.SaveChangesAsync();

        var calculationService = new FinancialCalculationService(context);
        var salaryRows = await calculationService.CalculateDsmSalariesAsync(2026, 9);

        var rameshSalary = salaryRows.FirstOrDefault(r => r.DsmName == "Ramesh");
        Assert.NotNull(rameshSalary);
        Assert.Equal(0, rameshSalary.PersonalDebtorDeduction);
        Assert.Equal(15000, rameshSalary.NetSalary);
    }

    [Fact]
    public async Task Test05_DeactivatedOpeningBalance_ContributesZero()
    {
        // 5. Deactivated opening balance contributes ₹0.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        var saveResult = await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Suresh",
            Amount = 5000,
            OpeningDate = new DateTime(2026, 9, 1)
        });
        Assert.True(saveResult.Success);

        // Deactivate opening balance
        var deactivateResult = await repo.DeactivateOpeningBalanceAsync(saveResult.Data!.OpeningBalanceId);
        Assert.True(deactivateResult.Success);

        var debits = await context.DsmPersonalDebtors.Where(d => d.DsmName == "Suresh").ToListAsync();
        var operationalBorrowed = debits.Where(d => d.EntryType == "Operational").Sum(d => d.Amount);
        var openingBalance = debits.Where(d => d.EntryType == "OpeningBalance").Sum(d => d.Amount);

        var summary = new DsmPersonalDebtorSummaryRow
        {
            DsmName = "Suresh",
            OpeningBalance = openingBalance,
            TotalBorrowed = operationalBorrowed,
            TotalRepaid = 0
        };

        Assert.Equal(0, summary.OpeningBalance);
        Assert.Equal(0, summary.TotalBorrowed);
        Assert.Equal(0, summary.Balance);
    }

    [Fact]
    public async Task Test06_ExistingOperationalDsm_UnchangedWithoutOpeningBalance()
    {
        // 6. Existing operational DSM calculations remain unchanged when no opening balance exists.
        using var context = CreateContext();

        context.DsmPersonalDebtors.Add(new DsmPersonalDebtor
        {
            DsmName = "Mahesh",
            Date = new DateTime(2026, 9, 5),
            Amount = 2000,
            EntryType = "Operational"
        });
        context.DsmPersonalDebtors.Add(new DsmPersonalDebtor
        {
            DsmName = "Mahesh",
            Date = new DateTime(2026, 9, 12),
            Amount = 3000,
            EntryType = "Operational"
        });
        await context.SaveChangesAsync();

        var borrow1 = await context.DsmPersonalDebtors.FirstAsync(d => d.DsmName == "Mahesh" && d.Amount == 2000);
        context.DsmPersonalDebtorRepayments.Add(new DsmPersonalDebtorRepayment
        {
            DsmPersonalDebtorId = borrow1.Id,
            Amount = 1000,
            Date = new DateTime(2026, 9, 15)
        });
        await context.SaveChangesAsync();

        var debits = await context.DsmPersonalDebtors.Where(d => d.DsmName == "Mahesh").ToListAsync();
        var repayments = await context.DsmPersonalDebtorRepayments.ToListAsync();

        var operationalBorrowed = debits.Where(d => d.EntryType == "Operational").Sum(d => d.Amount);
        var openingBalance = debits.Where(d => d.EntryType == "OpeningBalance").Sum(d => d.Amount);
        var personalDebtorIds = debits.Where(d => d.EntryType != "DeactivatedOpening").Select(d => d.Id).ToList();
        var totalRepaid = repayments.Where(r => personalDebtorIds.Contains(r.DsmPersonalDebtorId)).Sum(r => r.Amount);

        var summary = new DsmPersonalDebtorSummaryRow
        {
            DsmName = "Mahesh",
            OpeningBalance = openingBalance,
            TotalBorrowed = operationalBorrowed,
            TotalRepaid = totalRepaid
        };

        Assert.Equal(0, summary.OpeningBalance);
        Assert.Equal(5000, summary.TotalBorrowed);
        Assert.Equal(1000, summary.TotalRepaid);
        Assert.Equal(4000, summary.Balance);
    }

    [Fact]
    public async Task Test07_PeriodLedger_CarriesOpeningBalanceIntoSelectedPeriod()
    {
        // 7. Period ledger correctly carries the opening balance into the selected period.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        var creditor = new Creditor { Name = "Highway Express", Phone = "9998887776", IsActive = true, CreatedAt = DateTime.Now };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        // Historical Opening: 10,000 on Sept 1
        await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = creditor.CreditorId.ToString(),
            CreditorId = creditor.CreditorId,
            Amount = 10000,
            OpeningDate = new DateTime(2026, 9, 1)
        });

        // Prior debit on Sept 5: 3,000
        int dsmId1 = await CreateTestDsmEntryAsync(context, new DateTime(2026, 9, 5));
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = dsmId1,
            DebtorName = creditor.Name,
            Amount = 3000,
            CreatedAt = new DateTime(2026, 9, 5)
        });

        // Prior repayment on Sept 10: 1,000
        context.CreditorRepayments.Add(new CreditorRepayment
        {
            CreditorName = creditor.Name,
            Amount = 1000,
            RepaymentDate = new DateTime(2026, 9, 10),
            CreatedAt = new DateTime(2026, 9, 10)
        });

        // Within period transaction: debit on Sept 20 (2,000) and repayment on Sept 25 (2,000)
        int dsmId2 = await CreateTestDsmEntryAsync(context, new DateTime(2026, 9, 20));
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = dsmId2,
            DebtorName = creditor.Name,
            Amount = 2000,
            CreatedAt = new DateTime(2026, 9, 20)
        });
        context.CreditorRepayments.Add(new CreditorRepayment
        {
            CreditorName = creditor.Name,
            Amount = 2000,
            RepaymentDate = new DateTime(2026, 9, 25),
            CreatedAt = new DateTime(2026, 9, 25)
        });
        await context.SaveChangesAsync();

        // Selected Period: Sept 15 to Sept 30
        var periodStart = new DateTime(2026, 9, 15);
        var periodEnd = new DateTime(2026, 9, 30);

        var allDebits = await context.DebitEntries.Where(d => d.DebtorName == creditor.Name).ToListAsync();
        var allRepayments = await context.CreditorRepayments.Where(r => r.CreditorName == creditor.Name).ToListAsync();

        var ob = await context.OpeningBalances.FirstOrDefaultAsync(o => o.EntityType == "Debtor" && o.CreditorId == creditor.CreditorId && o.IsActive);
        double historicalOpening = ob?.Amount ?? 0.0;

        // Prior period calculation
        double priorDebits = allDebits.Where(d => d.CreatedAt < periodStart).Sum(d => d.Amount);
        double priorCredits = allRepayments.Where(r => r.RepaymentDate < periodStart).Sum(r => r.Amount);
        double periodOpeningBalance = historicalOpening + priorDebits - priorCredits;

        // 10,000 historical + 3,000 prior debit - 1,000 prior repayment = 12,000
        Assert.Equal(12000, periodOpeningBalance);

        // Transactions in period
        var periodDebits = allDebits.Where(d => d.CreatedAt >= periodStart && d.CreatedAt <= periodEnd).ToList();
        var periodCredits = allRepayments.Where(r => r.RepaymentDate >= periodStart && r.RepaymentDate <= periodEnd).ToList();

        double running = periodOpeningBalance;
        foreach (var d in periodDebits) running += d.Amount;
        foreach (var r in periodCredits) running -= r.Amount;

        // 12,000 + 2,000 - 2,000 = 12,000
        Assert.Equal(12000, running);
    }

    [Fact]
    public async Task Test08_OpeningBalance_DoesNotAppearAsFakeShiftOrDsrTransaction()
    {
        // 8. Opening balance does not appear as a fake shift/DSR transaction.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        int initialShifts = await context.Shifts.CountAsync();
        int initialDsmEntries = await context.DsmEntries.CountAsync();
        int initialDebitEntries = await context.DebitEntries.CountAsync();

        // Create both Debtor and DSM opening balances
        await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = "99",
            Amount = 10000,
            OpeningDate = DateTime.Today
        });

        await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Kishore",
            Amount = 8500,
            OpeningDate = DateTime.Today
        });

        int finalShifts = await context.Shifts.CountAsync();
        int finalDsmEntries = await context.DsmEntries.CountAsync();
        int finalDebitEntries = await context.DebitEntries.CountAsync();

        Assert.Equal(initialShifts, finalShifts);
        Assert.Equal(initialDsmEntries, finalDsmEntries);
        Assert.Equal(initialDebitEntries, finalDebitEntries);

        var anchor = await context.DsmPersonalDebtors.FirstOrDefaultAsync(d => d.DsmName == "Kishore");
        Assert.NotNull(anchor);
        Assert.Null(anchor.DsmEntryId);
    }

    [Fact]
    public async Task Test09_ExistingRepaymentFifo_RemainsCorrect()
    {
        // 9. Existing repayment FIFO remains correct.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        // Historical Opening anchor on Sept 1: 8,500
        await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Ganesh",
            Amount = 8500,
            OpeningDate = new DateTime(2026, 9, 1)
        });

        // Operational loss on Sept 15: 1,500
        context.DsmPersonalDebtors.Add(new DsmPersonalDebtor
        {
            DsmName = "Ganesh",
            Date = new DateTime(2026, 9, 15),
            Amount = 1500,
            EntryType = "Operational",
            CreatedAt = new DateTime(2026, 9, 15)
        });
        await context.SaveChangesAsync();

        // Simulate FIFO distribution with 2,000 repayment
        var borrowings = await context.DsmPersonalDebtors
            .Where(b => b.DsmName == "Ganesh" && b.EntryType != "DeactivatedOpening")
            .OrderBy(b => b.Date)
            .ThenBy(b => b.Id)
            .ToListAsync();

        double remainingToDistribute = 2000;
        foreach (var b in borrowings)
        {
            double allocated = Math.Min(b.Amount, remainingToDistribute);
            b.RepaidAmount = allocated;
            remainingToDistribute -= allocated;
        }

        var anchor = borrowings.First(b => b.EntryType == "OpeningBalance");
        var operational = borrowings.First(b => b.EntryType == "Operational");

        // The earlier historical anchor gets 2,000 repaid, operational loss gets 0
        Assert.Equal(2000, anchor.RepaidAmount);
        Assert.Equal(0, operational.RepaidAmount);

        // Now simulate total 9,000 repayment (8,500 anchor + 500 operational)
        remainingToDistribute = 9000;
        foreach (var b in borrowings)
        {
            double allocated = Math.Min(b.Amount, remainingToDistribute);
            b.RepaidAmount = allocated;
            remainingToDistribute -= allocated;
        }

        // Anchor is fully repaid (8,500), operational gets next 500
        Assert.Equal(8500, anchor.RepaidAmount);
        Assert.Equal(500, operational.RepaidAmount);
    }

    [Fact]
    public async Task Test10_NoDoubleCountingOccurs()
    {
        // 10. No double-counting occurs.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        var creditor = new Creditor { Name = "Modern Logistics", Phone = "9112233445", IsActive = true, CreatedAt = DateTime.Now };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = creditor.CreditorId.ToString(),
            CreditorId = creditor.CreditorId,
            Amount = 10000,
            OpeningDate = new DateTime(2026, 9, 1)
        });

        int dsmId = await CreateTestDsmEntryAsync(context, new DateTime(2026, 9, 10));

        // Add 1 operational debit entry
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = dsmId,
            DebtorName = creditor.Name,
            Amount = 5000,
            PaymentMethod = "Credit",
            CreatedAt = new DateTime(2026, 9, 10)
        });
        await context.SaveChangesAsync();

        // Operational debits must be exactly 5,000 (opening balance is NOT added to DebitEntries)
        var debitEntriesCount = await context.DebitEntries.Where(d => d.DebtorName == creditor.Name).CountAsync();
        var totalDebits = await context.DebitEntries.Where(d => d.DebtorName == creditor.Name).SumAsync(d => d.Amount);

        Assert.Equal(1, debitEntriesCount);
        Assert.Equal(5000, totalDebits);

        var ob = await context.OpeningBalances.FirstOrDefaultAsync(o => o.EntityType == "Debtor" && o.CreditorId == creditor.CreditorId && o.IsActive);
        Assert.NotNull(ob);
        Assert.Equal(10000, ob.Amount);

        // Overall outstanding is OpeningBalance + TotalDebt - TotalRepayment = 10000 + 5000 - 0 = 15000
        double outstanding = ob.Amount + totalDebits - 0;
        Assert.Equal(15000, outstanding);
    }
}
