using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using Xunit;

namespace FuelPro.Tests;

public class OpeningBalanceRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FuelProDbContext> _options;

    public OpeningBalanceRepositoryTests()
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

    [Fact]
    public async Task TestA_Create_DsmOpeningBalance_CreatesRecordAndAnchor()
    {
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        var ob = new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Ramesh",
            Amount = 8500,
            OpeningDate = new DateTime(2026, 10, 1),
            Notes = "Opening balance for Ramesh"
        };

        var result = await repo.SaveOpeningBalanceAsync(ob);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(8500, result.Data.Amount);

        // Verify OpeningBalances table
        var savedOb = await context.OpeningBalances.FirstOrDefaultAsync(o => o.EntityIdentifier == "Ramesh");
        Assert.NotNull(savedOb);
        Assert.Equal("DsmLoss", savedOb.EntityType);
        Assert.Equal(8500, savedOb.Amount);
        Assert.True(savedOb.IsActive);

        // Verify DsmPersonalDebtors table (backing anchor)
        var anchor = await context.DsmPersonalDebtors.FirstOrDefaultAsync(d => d.DsmName == "Ramesh");
        Assert.NotNull(anchor);
        Assert.Equal("OpeningBalance", anchor.EntryType);
        Assert.Equal(8500, anchor.Amount);
        Assert.Null(anchor.DsmEntryId);
        Assert.False(anchor.DeductFromSalary);
        Assert.Equal("Historical Opening Balance", anchor.Remarks);
        Assert.Equal("Opening Balance", anchor.FuelProduct);
        Assert.Equal(new DateTime(2026, 10, 1), anchor.Date);
    }

    [Fact]
    public async Task TestB_DuplicateSave_IsIdempotent_NoDuplicates()
    {
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        var ob1 = new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Ramesh",
            Amount = 8500,
            OpeningDate = new DateTime(2026, 10, 1)
        };
        await repo.SaveOpeningBalanceAsync(ob1);

        // Save same DSM opening balance again
        var ob2 = new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Ramesh",
            Amount = 8500,
            OpeningDate = new DateTime(2026, 10, 1)
        };
        await repo.SaveOpeningBalanceAsync(ob2);

        var obCount = await context.OpeningBalances.CountAsync(o => o.EntityIdentifier == "Ramesh" && o.IsActive);
        var anchorCount = await context.DsmPersonalDebtors.CountAsync(d => d.DsmName == "Ramesh" && d.EntryType == "OpeningBalance");

        Assert.Equal(1, obCount);
        Assert.Equal(1, anchorCount);
    }

    [Fact]
    public async Task TestC_Update_AmountChangesBothOpeningBalanceAndAnchor()
    {
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        var ob = new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Ramesh",
            Amount = 8500,
            OpeningDate = new DateTime(2026, 10, 1)
        };
        var createResult = await repo.SaveOpeningBalanceAsync(ob);
        Assert.True(createResult.Success);

        // Update amount from 8500 to 9000
        var updateOb = new OpeningBalance
        {
            OpeningBalanceId = createResult.Data!.OpeningBalanceId,
            EntityType = "DsmLoss",
            EntityIdentifier = "Ramesh",
            Amount = 9000,
            OpeningDate = new DateTime(2026, 10, 1)
        };
        var updateResult = await repo.SaveOpeningBalanceAsync(updateOb);
        Assert.True(updateResult.Success);

        // Verify OpeningBalance updated
        var refreshedOb = await context.OpeningBalances.FirstAsync(o => o.EntityIdentifier == "Ramesh");
        Assert.Equal(9000, refreshedOb.Amount);

        // Verify Anchor updated
        var refreshedAnchor = await context.DsmPersonalDebtors.FirstAsync(d => d.DsmName == "Ramesh");
        Assert.Equal(9000, refreshedAnchor.Amount);
        Assert.Equal("OpeningBalance", refreshedAnchor.EntryType);
    }

    [Fact]
    public async Task TestD_Deactivate_SetsActiveFalseAndZerosAnchor_PreservesRepayments()
    {
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        var ob = new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Ramesh",
            Amount = 8500,
            OpeningDate = new DateTime(2026, 10, 1)
        };
        var createResult = await repo.SaveOpeningBalanceAsync(ob);

        // Create a repayment against this anchor
        var anchor = await context.DsmPersonalDebtors.FirstAsync(d => d.DsmName == "Ramesh");
        var repayment = new DsmPersonalDebtorRepayment
        {
            DsmPersonalDebtorId = anchor.Id,
            Amount = 2000,
            Date = DateTime.Now,
            PaymentMethod = "Cash"
        };
        context.DsmPersonalDebtorRepayments.Add(repayment);
        await context.SaveChangesAsync();

        // Deactivate opening balance
        var deactivateResult = await repo.DeactivateOpeningBalanceAsync(createResult.Data!.OpeningBalanceId);
        Assert.True(deactivateResult.Success);

        // Verify OpeningBalance is inactive
        var refreshedOb = await context.OpeningBalances.FirstAsync(o => o.OpeningBalanceId == createResult.Data.OpeningBalanceId);
        Assert.False(refreshedOb.IsActive);

        // Verify Anchor still exists in DB, Amount is 0, EntryType is DeactivatedOpening
        var refreshedAnchor = await context.DsmPersonalDebtors.FirstOrDefaultAsync(d => d.DsmName == "Ramesh");
        Assert.NotNull(refreshedAnchor);
        Assert.Equal(0, refreshedAnchor.Amount);
        Assert.Equal("DeactivatedOpening", refreshedAnchor.EntryType);

        // Verify Repayment record was NOT deleted
        var repaymentExists = await context.DsmPersonalDebtorRepayments.AnyAsync(r => r.DsmPersonalDebtorId == anchor.Id);
        Assert.True(repaymentExists);
    }

    [Fact]
    public async Task TestE_ExistingOperationalLoss_RemainsUntouched()
    {
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        // Create existing operational shortage
        var operationalShortage = new DsmPersonalDebtor
        {
            DsmName = "Ramesh",
            Amount = 1500,
            Date = new DateTime(2026, 10, 5),
            Time = "14:30",
            EntryType = "Operational",
            DeductFromSalary = true,
            Remarks = "Shortage on shift 1"
        };
        context.DsmPersonalDebtors.Add(operationalShortage);
        await context.SaveChangesAsync();

        // Create opening balance
        var ob = new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Ramesh",
            Amount = 8500,
            OpeningDate = new DateTime(2026, 10, 1)
        };
        await repo.SaveOpeningBalanceAsync(ob);

        // Verify operational record remains untouched
        var opRecord = await context.DsmPersonalDebtors.FirstAsync(d => d.Id == operationalShortage.Id);
        Assert.Equal("Operational", opRecord.EntryType);
        Assert.Equal(1500, opRecord.Amount);
        Assert.True(opRecord.DeductFromSalary);
        Assert.Equal("Shortage on shift 1", opRecord.Remarks);

        // Verify total records for Ramesh is 2: 1 operational + 1 anchor
        var totalRecords = await context.DsmPersonalDebtors.Where(d => d.DsmName == "Ramesh").ToListAsync();
        Assert.Equal(2, totalRecords.Count);
        Assert.Single(totalRecords.Where(d => d.EntryType == "Operational"));
        Assert.Single(totalRecords.Where(d => d.EntryType == "OpeningBalance"));
    }

    [Fact]
    public async Task TestF_TransactionRollback_OnFailure_LeavesNoPartialRecords()
    {
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        // Negative amount should fail validation before any write
        var invalidOb = new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Suresh",
            Amount = -500,
            OpeningDate = DateTime.Now
        };
        var result = await repo.SaveOpeningBalanceAsync(invalidOb);

        Assert.False(result.Success);
        Assert.False(await context.OpeningBalances.AnyAsync(o => o.EntityIdentifier == "Suresh"));
        Assert.False(await context.DsmPersonalDebtors.AnyAsync(d => d.DsmName == "Suresh"));
    }

    [Fact]
    public async Task TestG_DebtorOpeningBalance_CreatesOnlyOpeningBalanceRecord()
    {
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        // Add a Creditor master record
        var creditor = new Creditor { Name = "ABC Transport", IsActive = true };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        var ob = new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = "ABC Transport",
            CreditorId = creditor.CreditorId,
            Amount = 15000,
            OpeningDate = new DateTime(2026, 10, 1),
            Notes = "Historical ledger balance"
        };

        var result = await repo.SaveOpeningBalanceAsync(ob);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        // Verify OpeningBalance saved
        var saved = await context.OpeningBalances.FirstOrDefaultAsync(o => o.EntityIdentifier == "ABC Transport");
        Assert.NotNull(saved);
        Assert.Equal("Debtor", saved.EntityType);
        Assert.Equal(15000, saved.Amount);
        Assert.Equal(creditor.CreditorId, saved.CreditorId);

        // Verify NO DsmPersonalDebtors created
        Assert.False(await context.DsmPersonalDebtors.AnyAsync(d => d.DsmName == "ABC Transport"));
        // Verify NO DebitEntries created
        Assert.False(await context.DebitEntries.AnyAsync());
    }
}
