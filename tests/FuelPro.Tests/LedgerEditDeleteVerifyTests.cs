using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Data;

namespace FuelPro.Tests;

public class LedgerEditDeleteVerifyTests
{
    private DbContextOptions<FuelProDbContext> GetDbOptions()
    {
        string dbPath = @"C:\Users\jadha\AppData\Local\FuelPro\fuelPro.db";
        return new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
    }

    [Fact]
    public async Task VerifyEditAndDeleteRepaymentFlow()
    {
        using var context = new FuelProDbContext(GetDbOptions());

        // 1. Create a dummy repayment dated today
        var repayment = new CreditorRepayment
        {
            CreditorName = "ARHAM CORPORATION",
            RepaymentDate = DateTime.Today.AddHours(12),
            PaymentMode = "Cash",
            Amount = 150.00,
            CreatedAt = DateTime.Now,
            ShiftNumber = "B"
        };

        context.CreditorRepayments.Add(repayment);
        await context.SaveChangesAsync();
        int repaymentId = repayment.CreditorRepaymentId;
        Assert.True(repaymentId > 0);

        try
        {
            // 2. Edit the repayment
            var tracked = await context.CreditorRepayments.FindAsync(repaymentId);
            Assert.NotNull(tracked);
            tracked.Amount = 250.00;
            tracked.ChequeNo = "TEST-REF-99";
            
            context.CreditorRepayments.Update(tracked);
            await context.SaveChangesAsync();

            // Verify update was written
            var updated = await context.CreditorRepayments.AsNoTracking().FirstOrDefaultAsync(r => r.CreditorRepaymentId == repaymentId);
            Assert.NotNull(updated);
            Assert.Equal(250.00, updated.Amount);
            Assert.Equal("TEST-REF-99", updated.ChequeNo);

            // Verify update sync log exists
            var updateLog = await context.SyncChangeLogs
                .AsNoTracking()
                .OrderByDescending(l => l.Id)
                .FirstOrDefaultAsync(l => l.TableName == "CreditorRepayments" && l.RecordId == repaymentId);
            Assert.NotNull(updateLog);
            Assert.Equal("UPDATE", updateLog.Operation);

            // 3. Delete the repayment
            context.CreditorRepayments.Remove(tracked);
            await context.SaveChangesAsync();

            // Verify deletion
            var deleted = await context.CreditorRepayments.AsNoTracking().FirstOrDefaultAsync(r => r.CreditorRepaymentId == repaymentId);
            Assert.Null(deleted);

            // Verify delete sync log exists
            var deleteLog = await context.SyncChangeLogs
                .AsNoTracking()
                .OrderByDescending(l => l.Id)
                .FirstOrDefaultAsync(l => l.TableName == "CreditorRepayments" && l.RecordId == repaymentId);
            Assert.NotNull(deleteLog);
            Assert.Equal("DELETE", deleteLog.Operation);
        }
        finally
        {
            // Cleanup just in case anything failed before delete
            var remaining = await context.CreditorRepayments.FindAsync(repaymentId);
            if (remaining != null)
            {
                context.CreditorRepayments.Remove(remaining);
                await context.SaveChangesAsync();
            }
        }
    }
}
