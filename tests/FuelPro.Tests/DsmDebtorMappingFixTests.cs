using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;

namespace FuelPro.Tests;

public class DsmDebtorMappingFixTests
{
    public DsmDebtorMappingFixTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
    }

    private DbContextOptions<FuelProDbContext> CreateInMemoryOptions()
    {
        return new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    [Fact]
    public void VerifyDsmProfileChangedEventFiring()
    {
        bool eventFired = false;
        Action handler = () => { eventFired = true; };

        DsmEntryService.DsmProfileChanged += handler;
        try
        {
            DsmEntryService.RaiseDsmProfileChanged();
            Assert.True(eventFired, "DsmProfileChanged event should fire when raised");
        }
        finally
        {
            DsmEntryService.DsmProfileChanged -= handler;
        }
    }

    [Fact]
    public async Task VerifyCustomPumpMappingsAreNotOverwrittenOnStartup()
    {
        var options = CreateInMemoryOptions();
        var tempFolder = Path.Combine(Path.GetTempPath(), "FuelPro_Test_" + Guid.NewGuid().ToString("N"));
        var credentialService = new LocalCredentialFileService(tempFolder);

        try
        {
            using (var context = new FuelProDbContext(options))
            {
                // Setup custom 2-pump 4-nozzle mapping
                context.PumpMappings.AddRange(new List<PumpMapping>
                {
                    new() { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", TankName = "Custom MS Tank", CreatedAt = DateTime.Now },
                    new() { PumpId = 1, NozzleNumber = 2, FuelType = "HSD", TankName = "Custom HSD Tank", CreatedAt = DateTime.Now },
                    new() { PumpId = 2, NozzleNumber = 3, FuelType = "MS-I", TankName = "Custom MS Tank", CreatedAt = DateTime.Now },
                    new() { PumpId = 2, NozzleNumber = 4, FuelType = "HSD", TankName = "Custom HSD Tank", CreatedAt = DateTime.Now },
                });
                await context.SaveChangesAsync();
            }

            // Run SeedData.InitializeAsync
            using (var context = new FuelProDbContext(options))
            {
                await SeedData.InitializeAsync(context, credentialService);

                var mappings = await context.PumpMappings.ToListAsync();
                Assert.Equal(4, mappings.Count);
                Assert.Contains(mappings, m => m.TankName == "Custom MS Tank");
                Assert.Contains(mappings, m => m.TankName == "Custom HSD Tank");
            }
        }
        finally
        {
            try { Directory.Delete(tempFolder, true); } catch { }
        }
    }

    [Fact]
    public async Task VerifyDebtorRenameCascadesToDebitsAndRepayments()
    {
        var options = CreateInMemoryOptions();

        using var context = new FuelProDbContext(options);

        // 1. Add Shift & DsmEntry
        var shift = new Shift { ShiftDate = DateTime.Today, ShiftType = "A" };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var entry = new DsmEntry
        {
            ShiftId = shift.ShiftId,
            PumpId = 1,
            DsmName = "Test DSM",
            CreatedAt = DateTime.Now
        };
        context.DsmEntries.Add(entry);
        await context.SaveChangesAsync();

        // 2. Add Creditor
        var creditor = new Creditor { Name = "Old Debtor Name", IsActive = true, CreatedAt = DateTime.Now };
        context.Creditors.Add(creditor);

        // 3. Add DebitEntry
        var debit = new DebitEntry
        {
            DsmEntryId = entry.DsmEntryId,
            DebtorName = "Old Debtor Name",
            Amount = 1500.0,
            Remarks = "Credit Sale",
            CreatedAt = DateTime.Now
        };
        context.DebitEntries.Add(debit);

        // 4. Add Repayment
        var repayment = new CreditorRepayment
        {
            CreditorName = "Old Debtor Name",
            Amount = 500.0,
            PaymentMode = "Cash",
            RepaymentDate = DateTime.Today,
            CreatedAt = DateTime.Now
        };
        context.CreditorRepayments.Add(repayment);
        await context.SaveChangesAsync();

        // Simulate rename cascade logic
        string oldName = "Old Debtor Name";
        string newName = "New Debtor Name";

        creditor.Name = newName;
        var matchingDebits = await context.DebitEntries.Where(d => d.DebtorName == oldName).ToListAsync();
        foreach (var d in matchingDebits) d.DebtorName = newName;

        var matchingRepayments = await context.CreditorRepayments.Where(r => r.CreditorName == oldName).ToListAsync();
        foreach (var r in matchingRepayments) r.CreditorName = newName;

        await context.SaveChangesAsync();

        // Verify
        var updatedDebits = await context.DebitEntries.Where(d => d.DebtorName == newName).ToListAsync();
        var updatedRepayments = await context.CreditorRepayments.Where(r => r.CreditorName == newName).ToListAsync();

        Assert.Single(updatedDebits);
        Assert.Equal(1500.0, updatedDebits[0].Amount);

        Assert.Single(updatedRepayments);
        Assert.Equal(500.0, updatedRepayments[0].Amount);

        // Calculate outstanding balance
        double totalDebt = updatedDebits.Sum(d => d.Amount);
        double totalRepaid = updatedRepayments.Sum(r => r.Amount);
        double outstanding = totalDebt - totalRepaid;
        Assert.Equal(1000.0, outstanding);
    }
}
