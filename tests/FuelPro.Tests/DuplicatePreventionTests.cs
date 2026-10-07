using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
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
using FuelPro.Core.DTOs;
using FuelPro.UI;
using Xunit;

namespace FuelPro.Tests;

public class DuplicatePreventionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly string _tempDir;

    public DuplicatePreventionTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_DuplicatePreventionTests_" + Guid.NewGuid().ToString("N"));
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


        _serviceProvider = services.BuildServiceProvider();

        // Initialize App static fields for testing
        typeof(App).GetProperty("Services")?.SetValue(null, _serviceProvider);
        typeof(App).GetProperty("DbPath")?.SetValue(null, _dbPath);

        // Set up default pump config mappings
        var testMappings = new List<PumpMapping>
        {
            new() { PumpMappingId = 1, PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 2, PumpId = 1, NozzleNumber = 3, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 3, PumpId = 2, NozzleNumber = 2, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 4, PumpId = 2, NozzleNumber = 4, FuelType = "HSD", IsActive = true }
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

    private async Task<DsmEntry> SaveTestEntryAsync(
        DateTime date, string shiftType, string name, int pumpId, double cashDeposit)
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();

        int nozzleNumber = PumpConfiguration.GetNozzlesForPump(pumpId, date).FirstOrDefault();
        if (nozzleNumber == 0) nozzleNumber = 1;

        var nozzleReadings = new List<NozzleReading>
        {
            new() { NozzleNumber = nozzleNumber, FuelType = "MS-I", OpeningReading = 1000, ClosingReading = 1100, Rate = 100 }
        };

        var payment = new PaymentCollection
        {
            PhonePeMorning = 1000,
            CashDeposit = cashDeposit
        };

        var cashDenominations = new List<CashDenomination>
        {
            new() { CashType = "Cash1", TotalAmount = cashDeposit }
        };

        var debitEntries = new List<DebitEntry>
        {
            new() { DebtorName = "John Doe", Amount = 500, VehicleNumber = "MH12-1234" }
        };

        var res = await dsmService.SaveCompleteEntryAsync(
            date, shiftType, name, pumpId,
            nozzleReadings, payment, debitEntries, new List<TestingEntry>(),
            new List<Expense>(), cashDenominations);

        Assert.True(res.Success, $"Save failed: {res.Error}");
        return res.Data!;
    }

    [Fact]
    public async Task SaveTwice_ProducesExactlyOneDsmEntry()
    {
        var date = new DateTime(2026, 7, 12);

        // First Save
        var entry1 = await SaveTestEntryAsync(date, "A", "Tony Stark", 1, 5000);

        // Second Save (should be idempotent and return success with the existing entry)
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();

        var nozzleReadings = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 1000, ClosingReading = 1100, Rate = 100 }
        };

        var payment = new PaymentCollection
        {
            PhonePeMorning = 1000,
            CashDeposit = 5000
        };

        var cashDenominations = new List<CashDenomination>
        {
            new() { CashType = "Cash1", TotalAmount = 5000 }
        };

        var res = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Tony Stark", 1,
            nozzleReadings, payment, new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), cashDenominations,
            connectedPumpId: null, existingEntryId: entry1.DsmEntryId);

        Assert.True(res.Success);
        Assert.Equal(entry1.DsmEntryId, res.Data!.DsmEntryId);

        // Assert only 1 entry exists in the DB
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var count = await db.Set<DsmEntry>().CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SaveTenTimes_ProducesExactlyOneDsmEntry()
    {
        var date = new DateTime(2026, 7, 12);
        DsmEntry? lastEntry = null;

        for (int i = 0; i < 10; i++)
        {
            using var scope = _serviceProvider.CreateScope();
            var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();

            var nozzleReadings = new List<NozzleReading>
            {
                new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 1000, ClosingReading = 1100, Rate = 100 }
            };

            var payment = new PaymentCollection
            {
                PhonePeMorning = 1000,
                CashDeposit = 5000
            };

            var cashDenominations = new List<CashDenomination>
            {
                new() { CashType = "Cash1", TotalAmount = 5000 }
            };

            var res = await dsmService.SaveCompleteEntryAsync(
                date, "A", "Tony Stark", 1,
                nozzleReadings, payment, new List<DebitEntry>(), new List<TestingEntry>(),
                new List<Expense>(), cashDenominations,
                connectedPumpId: null, existingEntryId: lastEntry?.DsmEntryId);

            Assert.True(res.Success);
            if (lastEntry != null)
            {
                Assert.Equal(lastEntry.DsmEntryId, res.Data!.DsmEntryId);
            }
            lastEntry = res.Data;
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var count = await db.Set<DsmEntry>().CountAsync();
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task DuplicateDetector_FindsIntentionalDuplicates()
    {
        var date = new DateTime(2026, 7, 12);

        // Create one clean entry
        var entry1 = await SaveTestEntryAsync(date, "A", "Tony Stark", 1, 5000);

        // Manually bypass service check and insert a duplicate DsmEntry via DB Context
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var dupEntry = new DsmEntry
            {
                ShiftId = entry1.ShiftId,
                DsmName = entry1.DsmName,
                PumpId = entry1.PumpId,
                CreatedAt = DateTime.Now.AddMinutes(5)
            };
            db.Set<DsmEntry>().Add(dupEntry);
            await db.SaveChangesAsync();

            // Insert child table rows for the duplicate
            var dupPayment = new PaymentCollection
            {
                DsmEntryId = dupEntry.DsmEntryId,
                CashDeposit = 3000
            };
            db.Set<PaymentCollection>().Add(dupPayment);

            var dupDebit = new DebitEntry
            {
                DsmEntryId = dupEntry.DsmEntryId,
                DebtorName = "John Doe",
                Amount = 500
            };
            db.Set<DebitEntry>().Add(dupDebit);
            await db.SaveChangesAsync();
        }

        // Run inspection service
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var allEntries = await db.Set<DsmEntry>().ToListAsync();
            Console.WriteLine($"DIAGNOSTIC: total DsmEntries in DB = {allEntries.Count}");
            foreach (var ent in allEntries)
            {
                Console.WriteLine($"DIAGNOSTIC: Entry ID {ent.DsmEntryId}, ShiftId {ent.ShiftId}, PumpId {ent.PumpId}, DsmName '{ent.DsmName}'");
            }

            var inspectionService = scope.ServiceProvider.GetRequiredService<IDuplicateDataInspectionService>();
            var report = await inspectionService.RunFullScanAsync();

            Assert.True(report.HasIssues);
            Assert.Equal(1, report.DuplicateDsmEntries);
            Assert.Single(report.DsmEntryGroups);

            var group = report.DsmEntryGroups.First();
            Assert.Equal(entry1.DsmEntryId, group.OriginalRecord.DsmEntryId);
            Assert.Equal(1, group.DuplicateCount);
            Assert.Equal("Same ShiftId + PumpId + DsmName (case-insensitive)", group.Reason);
        }
    }

    [Fact]
    public async Task DuplicateResolver_RemovesOnlyDuplicate_AndPreservesOriginal()
    {
        var date = new DateTime(2026, 7, 12);
        var entry1 = await SaveTestEntryAsync(date, "A", "Tony Stark", 1, 5000);
        int dupEntryId;

        // Manually insert duplicate DsmEntry and children
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var dupEntry = new DsmEntry
            {
                ShiftId = entry1.ShiftId,
                DsmName = entry1.DsmName,
                PumpId = entry1.PumpId,
                CreatedAt = DateTime.Now.AddMinutes(5)
            };
            db.Set<DsmEntry>().Add(dupEntry);
            await db.SaveChangesAsync();
            dupEntryId = dupEntry.DsmEntryId;

            db.Set<PaymentCollection>().Add(new PaymentCollection { DsmEntryId = dupEntryId, CashDeposit = 5000 });
            db.Set<DebitEntry>().Add(new DebitEntry { DsmEntryId = dupEntryId, DebtorName = "John Doe", Amount = 500 });
            await db.SaveChangesAsync();
        }

        // Resolve the duplicate
        using (var scope = _serviceProvider.CreateScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<DuplicateResolutionService>();
            var result = await resolver.DeleteDuplicateDsmEntryAsync(dupEntryId, entry1.DsmEntryId);

            Assert.True(result.Success);
        }

        // Verify state
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

            // Duplicate entry is gone
            var dupExists = await db.Set<DsmEntry>().AnyAsync(e => e.DsmEntryId == dupEntryId);
            Assert.False(dupExists);

            // Duplicate child rows are gone
            var dupPaymentExists = await db.Set<PaymentCollection>().AnyAsync(p => p.DsmEntryId == dupEntryId);
            Assert.False(dupPaymentExists);

            // Original entry exists
            var orig = await db.Set<DsmEntry>()
                .Include(e => e.DebitEntries)
                .Include(e => e.PaymentCollection)
                .FirstOrDefaultAsync(e => e.DsmEntryId == entry1.DsmEntryId);

            Assert.NotNull(orig);
            Assert.Single(orig.DebitEntries);
            Assert.NotNull(orig.PaymentCollection);
        }
    }

    [Fact]
    public async Task DeleteMultipleDuplicateDsmEntriesAsync_BatchDeletesAllSpecifiedDuplicates()
    {
        var entry1 = await SaveTestEntryAsync(new DateTime(2026, 7, 10), "A", "BATCH_DSM", 2, 2000);

        int dup1Id, dup2Id;
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

            var dup1 = new DsmEntry { ShiftId = entry1.ShiftId, DsmName = entry1.DsmName, PumpId = entry1.PumpId, CreatedAt = DateTime.Now.AddMinutes(1) };
            var dup2 = new DsmEntry { ShiftId = entry1.ShiftId, DsmName = entry1.DsmName, PumpId = entry1.PumpId, CreatedAt = DateTime.Now.AddMinutes(2) };

            db.Set<DsmEntry>().AddRange(dup1, dup2);
            await db.SaveChangesAsync();

            dup1Id = dup1.DsmEntryId;
            dup2Id = dup2.DsmEntryId;

            db.Set<DebitEntry>().Add(new DebitEntry { DsmEntryId = dup1Id, DebtorName = "Debtor 1", Amount = 100 });
            db.Set<DebitEntry>().Add(new DebitEntry { DsmEntryId = dup2Id, DebtorName = "Debtor 2", Amount = 200 });
            await db.SaveChangesAsync();
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<DuplicateResolutionService>();
            var pairs = new List<(int, int)> { (dup1Id, entry1.DsmEntryId), (dup2Id, entry1.DsmEntryId) };
            var result = await resolver.DeleteMultipleDuplicateDsmEntriesAsync(pairs);

            Assert.True(result.Success);
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            Assert.False(await db.Set<DsmEntry>().AnyAsync(e => e.DsmEntryId == dup1Id));
            Assert.False(await db.Set<DsmEntry>().AnyAsync(e => e.DsmEntryId == dup2Id));
            Assert.True(await db.Set<DsmEntry>().AnyAsync(e => e.DsmEntryId == entry1.DsmEntryId));
        }
    }

    [Fact]
    public async Task TestA_DetectExactSyncedDuplicateDebitEntry()
    {
        var date = new DateTime(2026, 8, 1);
        var entry = await SaveTestEntryAsync(date, "A", "Operator 1", 1, 1000);

        int origId, dupeId;
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var origDebit = new DebitEntry
            {
                DsmEntryId = entry.DsmEntryId,
                DebtorName = "SUJATA MASTANE",
                Amount = 3100,
                SlipNumber = "2843",
                CreatedAt = entry.CreatedAt
            };
            var dupeDebit = new DebitEntry
            {
                DsmEntryId = entry.DsmEntryId,
                DebtorName = "SUJATA MASTANE",
                Amount = 3100,
                SlipNumber = "2843",
                CreatedAt = entry.CreatedAt.AddHours(5)
            };
            db.Set<DebitEntry>().AddRange(origDebit, dupeDebit);
            await db.SaveChangesAsync();
            origId = origDebit.DebitId;
            dupeId = dupeDebit.DebitId;
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var inspector = scope.ServiceProvider.GetRequiredService<IDuplicateDataInspectionService>();
            var dupes = await inspector.FindDuplicateDebitEntriesAsync();

            var match = dupes.FirstOrDefault(g => g.OriginalRecord.DebtorName == "SUJATA MASTANE");
            Assert.NotNull(match);
            Assert.Equal(origId, match.OriginalRecord.DebitId);
            Assert.Single(match.DuplicateRecords);
            Assert.Equal(dupeId, match.DuplicateRecords[0].DebitId);
        }
    }

    [Fact]
    public async Task TestB_DetectExactSyncedDuplicateExpense()
    {
        var date = new DateTime(2026, 8, 2);
        var entry = await SaveTestEntryAsync(date, "A", "Operator 2", 1, 1000);

        int origId, dupeId;
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var origExp = new Expense { DsmEntryId = entry.DsmEntryId, Description = "BANGLOW D G", Amount = 7852 };
            db.Set<Expense>().Add(origExp);
            await db.SaveChangesAsync();
            origId = origExp.ExpenseId;

            var origLog = await db.Set<SyncChangeLog>().FirstAsync(l => l.TableName == "Expenses" && l.RecordId == origId);
            origLog.CreatedAt = entry.CreatedAt;

            var dupeExp = new Expense { DsmEntryId = entry.DsmEntryId, Description = "BANGLOW D G", Amount = 7852 };
            db.Set<Expense>().Add(dupeExp);
            await db.SaveChangesAsync();
            dupeId = dupeExp.ExpenseId;

            var dupeLog = await db.Set<SyncChangeLog>().FirstAsync(l => l.TableName == "Expenses" && l.RecordId == dupeId);
            dupeLog.CreatedAt = entry.CreatedAt.AddMinutes(2);
            await db.SaveChangesAsync();
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var inspector = scope.ServiceProvider.GetRequiredService<IDuplicateDataInspectionService>();
            var dupes = await inspector.FindDuplicateExpensesAsync();

            var match = dupes.FirstOrDefault(g => g.OriginalRecord.Description == "BANGLOW D G");
            Assert.NotNull(match);
            Assert.Equal(origId, match.OriginalRecord.ExpenseId);
            Assert.Single(match.DuplicateRecords);
            Assert.Equal(dupeId, match.DuplicateRecords[0].ExpenseId);
        }
    }

    [Fact]
    public async Task TestC_PreserveSameAmountLegitimateDebtorTransactions()
    {
        var date = new DateTime(2026, 8, 3);
        var entry = await SaveTestEntryAsync(date, "A", "Operator 3", 1, 1000);

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var debit1 = new DebitEntry { DsmEntryId = entry.DsmEntryId, DebtorName = "Milenium School", Amount = 5200, SlipNumber = null, VehicleNumber = null, CreatedAt = entry.CreatedAt };
            var debit2 = new DebitEntry { DsmEntryId = entry.DsmEntryId, DebtorName = "Milenium School", Amount = 5200, SlipNumber = null, VehicleNumber = null, CreatedAt = entry.CreatedAt };
            db.Set<DebitEntry>().AddRange(debit1, debit2);
            await db.SaveChangesAsync();
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var inspector = scope.ServiceProvider.GetRequiredService<IDuplicateDataInspectionService>();
            var dupes = await inspector.FindDuplicateDebitEntriesAsync();

            var match = dupes.FirstOrDefault(g => g.OriginalRecord.DebtorName == "Milenium School");
            Assert.Null(match); // Preserved as legitimate
        }
    }

    [Fact]
    public async Task TestD_PreserveSameDescriptionSameAmountLegitimateExpenses()
    {
        var date = new DateTime(2026, 8, 4);
        var entry = await SaveTestEntryAsync(date, "A", "Operator 4", 1, 1000);

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var exp1 = new Expense { DsmEntryId = entry.DsmEntryId, Description = "Chai", Amount = 50 };
            var exp2 = new Expense { DsmEntryId = entry.DsmEntryId, Description = "Chai", Amount = 50 };
            db.Set<Expense>().AddRange(exp1, exp2);
            await db.SaveChangesAsync();
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var inspector = scope.ServiceProvider.GetRequiredService<IDuplicateDataInspectionService>();
            var dupes = await inspector.FindDuplicateExpensesAsync();

            var match = dupes.FirstOrDefault(g => g.OriginalRecord.Description == "Chai");
            Assert.Null(match); // Preserved as legitimate
        }
    }

    [Fact]
    public async Task TestE_PreserveTransactionsAcrossDifferentDsmEntries()
    {
        var date = new DateTime(2026, 8, 5);
        var entry1 = await SaveTestEntryAsync(date, "A", "Operator 5A", 1, 1000);
        var entry2 = await SaveTestEntryAsync(date, "A", "Operator 5B", 2, 1000);

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var debit1 = new DebitEntry { DsmEntryId = entry1.DsmEntryId, DebtorName = "OM BITCON", Amount = 7430, SlipNumber = "101", CreatedAt = entry1.CreatedAt };
            var debit2 = new DebitEntry { DsmEntryId = entry2.DsmEntryId, DebtorName = "OM BITCON", Amount = 7430, SlipNumber = "101", CreatedAt = entry2.CreatedAt };
            db.Set<DebitEntry>().AddRange(debit1, debit2);

            var exp1 = new Expense { DsmEntryId = entry1.DsmEntryId, Description = "Office Stationery", Amount = 500 };
            var exp2 = new Expense { DsmEntryId = entry2.DsmEntryId, Description = "Office Stationery", Amount = 500 };
            db.Set<Expense>().AddRange(exp1, exp2);
            await db.SaveChangesAsync();
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var inspector = scope.ServiceProvider.GetRequiredService<IDuplicateDataInspectionService>();
            var debitDupes = await inspector.FindDuplicateDebitEntriesAsync();
            var expDupes = await inspector.FindDuplicateExpensesAsync();

            Assert.DoesNotContain(debitDupes, g => g.OriginalRecord.DebtorName == "OM BITCON");
            Assert.DoesNotContain(expDupes, g => g.OriginalRecord.Description == "Office Stationery");
        }
    }

    [Fact]
    public async Task TestF_PreserveTransactionsAcrossDifferentShifts()
    {
        var date1 = new DateTime(2026, 8, 6);
        var date2 = new DateTime(2026, 8, 7);
        var entry1 = await SaveTestEntryAsync(date1, "A", "Operator 6A", 1, 1000);
        var entry2 = await SaveTestEntryAsync(date2, "B", "Operator 6B", 1, 1000);

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var debit1 = new DebitEntry { DsmEntryId = entry1.DsmEntryId, DebtorName = "MONTEROSA", Amount = 39260, SlipNumber = "000096", CreatedAt = entry1.CreatedAt };
            var debit2 = new DebitEntry { DsmEntryId = entry2.DsmEntryId, DebtorName = "MONTEROSA", Amount = 39260, SlipNumber = "000096", CreatedAt = entry2.CreatedAt };
            db.Set<DebitEntry>().AddRange(debit1, debit2);
            await db.SaveChangesAsync();
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var inspector = scope.ServiceProvider.GetRequiredService<IDuplicateDataInspectionService>();
            var debitDupes = await inspector.FindDuplicateDebitEntriesAsync();

            Assert.DoesNotContain(debitDupes, g => g.OriginalRecord.DebtorName == "MONTEROSA");
        }
    }

    [Fact]
    public async Task TestGH_DeletionIsTransactional_AndCanonicalRecordRemains()
    {
        var date = new DateTime(2026, 8, 8);
        var entry = await SaveTestEntryAsync(date, "A", "Operator 7", 1, 1000);

        int origId, dupeId;
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var orig = new DebitEntry { DsmEntryId = entry.DsmEntryId, DebtorName = "TEST DEBTOR", Amount = 1200, CreatedAt = entry.CreatedAt };
            var dupe = new DebitEntry { DsmEntryId = entry.DsmEntryId, DebtorName = "TEST DEBTOR", Amount = 1200, CreatedAt = entry.CreatedAt.AddHours(2) };
            db.Set<DebitEntry>().AddRange(orig, dupe);
            await db.SaveChangesAsync();
            origId = orig.DebitId;
            dupeId = dupe.DebitId;
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<DuplicateResolutionService>();
            var failResult = await resolver.DeleteDuplicateDebitEntriesAsync(new List<int> { 999999 });
            Assert.False(failResult.Success);

            var result = await resolver.DeleteDuplicateDebitEntriesAsync(new List<int> { dupeId });
            Assert.True(result.Success);
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            Assert.False(await db.Set<DebitEntry>().AnyAsync(d => d.DebitId == dupeId));
            var canonical = await db.Set<DebitEntry>().FirstOrDefaultAsync(d => d.DebitId == origId);
            Assert.NotNull(canonical);
            Assert.Equal(1200, canonical.Amount);
            Assert.Equal("TEST DEBTOR", canonical.DebtorName);
        }
    }

    [Fact]
    public async Task TestIJ_SyncChangeLogDeleteGeneratedWithRecordGuid_AndSyncIdMappingsCleaned()
    {
        var date = new DateTime(2026, 8, 9);
        var entry = await SaveTestEntryAsync(date, "A", "Operator 8", 1, 1000);

        int dupeDebitId, dupeExpId;
        string remoteDebitGuid = Guid.NewGuid().ToString();
        string remoteExpGuid = Guid.NewGuid().ToString();

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var dupeDebit = new DebitEntry { DsmEntryId = entry.DsmEntryId, DebtorName = "SYNC DEBTOR", Amount = 2500, CreatedAt = DateTime.Now };
            var dupeExp = new Expense { DsmEntryId = entry.DsmEntryId, Description = "SYNC EXPENSE", Amount = 400 };
            db.Set<DebitEntry>().Add(dupeDebit);
            db.Set<Expense>().Add(dupeExp);
            await db.SaveChangesAsync();

            dupeDebitId = dupeDebit.DebitId;
            dupeExpId = dupeExp.ExpenseId;

            db.Set<SyncIdMapping>().Add(new SyncIdMapping { TableName = "DebitEntries", LocalId = dupeDebitId, RemoteGuid = remoteDebitGuid });
            db.Set<SyncIdMapping>().Add(new SyncIdMapping { TableName = "Expenses", LocalId = dupeExpId, RemoteGuid = remoteExpGuid });
            await db.SaveChangesAsync();
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<DuplicateResolutionService>();
            var resDebit = await resolver.DeleteDuplicateDebitEntriesAsync(new List<int> { dupeDebitId });
            var resExp = await resolver.DeleteDuplicateExpensesAsync(new List<int> { dupeExpId });
            Assert.True(resDebit.Success);
            Assert.True(resExp.Success);
        }

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

            // Test I: Verify SyncChangeLog DELETE contains correct RemoteGuid
            var debitDeleteLog = await db.Set<SyncChangeLog>()
                .FirstOrDefaultAsync(l => l.TableName == "DebitEntries" && l.RecordId == dupeDebitId && l.Operation == "DELETE");
            Assert.NotNull(debitDeleteLog);
            Assert.Equal(remoteDebitGuid, debitDeleteLog.RecordGuid);

            var expDeleteLog = await db.Set<SyncChangeLog>()
                .FirstOrDefaultAsync(l => l.TableName == "Expenses" && l.RecordId == dupeExpId && l.Operation == "DELETE");
            Assert.NotNull(expDeleteLog);
            Assert.Equal(remoteExpGuid, expDeleteLog.RecordGuid);

            // Test J: Verify SyncIdMappings for deleted duplicates are completely removed
            var debitMappingExists = await db.Set<SyncIdMapping>().AnyAsync(m => m.TableName == "DebitEntries" && m.LocalId == dupeDebitId);
            Assert.False(debitMappingExists);

            var expMappingExists = await db.Set<SyncIdMapping>().AnyAsync(m => m.TableName == "Expenses" && m.LocalId == dupeExpId);
            Assert.False(expMappingExists);
        }
    }

    [Fact]
    public async Task TestK_DeletedDuplicateDoesNotResurrectDuringSyncPullSimulation()
    {
        var date = new DateTime(2026, 8, 10);
        var entry = await SaveTestEntryAsync(date, "A", "Operator 9", 1, 1000);

        int canonicalId, dupeId;
        string remoteGuid = Guid.NewGuid().ToString();

        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var canonical = new DebitEntry { DsmEntryId = entry.DsmEntryId, DebtorName = "NO RESURRECT", Amount = 1500, CreatedAt = entry.CreatedAt };
            var dupe = new DebitEntry { DsmEntryId = entry.DsmEntryId, DebtorName = "NO RESURRECT", Amount = 1500, CreatedAt = entry.CreatedAt.AddHours(1) };
            db.Set<DebitEntry>().AddRange(canonical, dupe);
            await db.SaveChangesAsync();

            canonicalId = canonical.DebitId;
            dupeId = dupe.DebitId;

            db.Set<SyncIdMapping>().Add(new SyncIdMapping { TableName = "DebitEntries", LocalId = dupeId, RemoteGuid = remoteGuid });
            await db.SaveChangesAsync();
        }

        // Delete the duplicate
        using (var scope = _serviceProvider.CreateScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<DuplicateResolutionService>();
            await resolver.DeleteDuplicateDebitEntriesAsync(new List<int> { dupeId });
        }

        // Simulate SyncEngine incoming pull for this remote record
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

            // 1. Check if mapping exists: it was deleted!
            var existingMapping = await db.Set<SyncIdMapping>().FirstOrDefaultAsync(m => m.TableName == "DebitEntries" && m.RemoteGuid == remoteGuid);
            Assert.Null(existingMapping);

            // 2. Business key fallback lookup in SyncEngine:
            var matchedLocal = await db.Set<DebitEntry>()
                .FirstOrDefaultAsync(d => d.DsmEntryId == entry.DsmEntryId && d.DebtorName.ToLower() == "no resurrect" && Math.Abs(d.Amount - 1500) < 0.01);

            Assert.NotNull(matchedLocal);
            Assert.Equal(canonicalId, matchedLocal.DebitId);

            var totalDebits = await db.Set<DebitEntry>().CountAsync(d => d.DsmEntryId == entry.DsmEntryId && d.DebtorName == "NO RESURRECT");
            Assert.Equal(1, totalDebits); // Exactly 1, no duplicate resurrected!
        }
    }

    [Fact]
    public async Task TestLM_RealVaijunathReconciliationReachesExactTarget_BothViewsAgree()
    {
        string cloneDbPath = @"d:\Projects\Pump_Automation\Clients\Mitali_Service_Station\forensic_investigation\DISPOSABLE_WORKING_CLONE\fuelPro.db";
        if (!File.Exists(cloneDbPath)) return;

        string tempTestDb = Path.Combine(_tempDir, "vaijunath_test_" + Guid.NewGuid().ToString("N") + ".db");
        File.Copy(cloneDbPath, tempTestDb);

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options => options.UseSqlite($"Data Source={tempTestDb}"), ServiceLifetime.Transient);
        services.AddTransient<DbContext>(sp => sp.GetRequiredService<FuelProDbContext>());
        services.AddTransient<IDuplicateDataInspectionService, DuplicateDataInspectionService>();
        services.AddTransient<DuplicateResolutionService>();
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
        services.AddTransient<ISettingsRepository, SettingsRepository>();
        services.AddTransient<IUserRepository, UserRepository>();
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

        var sp = services.BuildServiceProvider();

        // 1. Run inspection on DsmEntry 78
        using (var scope = sp.CreateScope())
        {
            var inspector = scope.ServiceProvider.GetRequiredService<IDuplicateDataInspectionService>();
            var debitDupes = await inspector.FindDuplicateDebitEntriesAsync();
            var expDupes = await inspector.FindDuplicateExpensesAsync();

            var debit78Groups = debitDupes.Where(g => g.OriginalRecord.DsmEntryId == 78).ToList();
            var exp78Groups = expDupes.Where(g => g.OriginalRecord.DsmEntryId == 78).ToList();

            Assert.Equal(4, debit78Groups.Count);
            Assert.Equal(2, exp78Groups.Count);

            var debitIdsToDelete = debit78Groups.SelectMany(g => g.DuplicateRecords.Select(d => d.DebitId)).OrderBy(id => id).ToList();
            var expIdsToDelete = exp78Groups.SelectMany(g => g.DuplicateRecords.Select(e => e.ExpenseId)).OrderBy(id => id).ToList();

            Assert.Equal(new List<int> { 50, 51, 52, 53 }, debitIdsToDelete);
            Assert.Equal(new List<int> { 42, 43 }, expIdsToDelete);

            // 2. Perform transactional resolution
            var resolver = scope.ServiceProvider.GetRequiredService<DuplicateResolutionService>();
            var resDeb = await resolver.DeleteDuplicateDebitEntriesAsync(debitIdsToDelete);
            var resExp = await resolver.DeleteDuplicateExpensesAsync(expIdsToDelete);

            Assert.True(resDeb.Success);
            Assert.True(resExp.Success);
        }

        // 3. Verify totals and both calculation views
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var remainingDebits = await db.Set<DebitEntry>().Where(d => d.DsmEntryId == 78).ToListAsync();
            var remainingExpenses = await db.Set<Expense>().Where(e => e.DsmEntryId == 78).ToListAsync();

            var debitTotal = remainingDebits.Sum(d => d.Amount);
            var expTotal = remainingExpenses.Sum(e => e.Amount);

            Assert.Equal(54990.0, debitTotal, 2);
            Assert.Equal(8022.0, expTotal, 2);

            // View 1: Shift Entries list calculation (GetShiftEntrySummariesAsync as displayed on UI)
            var dsmEntryService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
            var shiftRepo = scope.ServiceProvider.GetRequiredService<IShiftRepository>();
            var shiftDbResult = await shiftRepo.GetShiftAsync(new DateTime(2026, 10, 1), "B");
            Assert.True(shiftDbResult.Success);
            int shiftId = shiftDbResult.Data!.ShiftId;

            var summariesResult = await dsmEntryService.GetShiftEntrySummariesAsync(shiftId);
            Assert.True(summariesResult.Success);
            var vaijunathSummary = summariesResult.Data!.First(s => s.DsmName.Contains("VAIJUNATH"));

            Assert.Equal(154037.0, vaijunathSummary.TotalPaymentIn, 2);
            Assert.Equal(154037.71, (double)vaijunathSummary.NetSales, 2);
            Assert.Equal(-0.71, vaijunathSummary.Difference, 2);

            // View 2: DSM Edit Mode calculation (IDsmCalculationService with loaded DsmEntry)
            var dsmCalcService = scope.ServiceProvider.GetRequiredService<IDsmCalculationService>();
            var dsmRepo = scope.ServiceProvider.GetRequiredService<IDsmEntryRepository>();
            var entry78Result = await dsmRepo.GetFullEntryAsync(78);
            Assert.True(entry78Result.Success);
            var entry78 = entry78Result.Data!;

            var entry79Result = await dsmRepo.GetFullEntryAsync(79);
            Assert.True(entry79Result.Success);
            var entry79 = entry79Result.Data!;

            var allNozzles = entry78.NozzleReadings.Concat(entry79.NozzleReadings).ToList();

            var cash1 = entry78.CashDenominations.Where(c => c.CashType == "Cash1").Sum(c => c.TotalAmount);
            var cash2 = entry78.CashDenominations.Where(c => c.CashType == "Cash2").Sum(c => c.TotalAmount);
            var dsmDto = new DsmEntryDto
            {
                DSMEntryId = entry78.DsmEntryId,
                NozzleReadings = allNozzles.Select(n => new NozzleReadingDto { Amount = (decimal)n.Amount }).ToList(),
                PaymentCollection = new PaymentCollectionDto
                {
                    PhonePe = (decimal)((entry78.PaymentCollection?.PhonePeMorning ?? 0)
                              + (entry78.PaymentCollection?.PhonePeDay ?? 0)
                              + (entry78.PaymentCollection?.PhonePeNight ?? 0)
                              + (entry78.PaymentCollection?.PhonePeCardMorning ?? 0)
                              + (entry78.PaymentCollection?.PhonePeCardDay ?? 0)
                              + (entry78.PaymentCollection?.PhonePeCardNight ?? 0)
                              + (entry78.QrPayments?.Sum(q => q.Amount) ?? 0)),
                    CreditCard = (decimal)((entry78.PaymentCollection?.CreditCardMorning ?? 0)
                                 + (entry78.PaymentCollection?.CreditCardDay ?? 0)
                                 + (entry78.PaymentCollection?.CreditCardNight ?? 0)),
                    PetroCard = (decimal)((entry78.PaymentCollection?.PetroCardMorning ?? 0)
                                + (entry78.PaymentCollection?.PetroCardDay ?? 0)
                                + (entry78.PaymentCollection?.PetroCardNight ?? 0)),
                    CashDeposit = (decimal)(cash1 > 0 ? cash1 : (entry78.PaymentCollection?.CashDeposit ?? 0)),
                    PhysicalCash = (decimal)cash2,
                    DynamicPayments = (decimal)(entry78.PaymentCollection?.Items?.Sum(i => i.Amount) ?? 0),
                    Others = (decimal)(entry78.PaymentCollection?.Others ?? 0)
                },
                DebitEntries = entry78.DebitEntries.Select(d => new DebitEntryDto { Amount = (decimal)d.Amount }).ToList(),
                Expenses = entry78.Expenses.Select(e => new ExpenseDto { Amount = (decimal)e.Amount }).ToList(),
                TestingEntries = entry78.TestingEntries.Select(t => new TestingEntryDto { Amount = (decimal)t.Amount }).ToList()
            };

            var dsmCalc = dsmCalcService.Calculate(dsmDto);
            Assert.Equal(91025.00m, dsmCalc.TotalInDirect);
            Assert.Equal(54990.00m, dsmCalc.TotalCreditors);
            Assert.Equal(8022.00m, dsmCalc.TotalExpenses);
            Assert.Equal(154037.00m, dsmCalc.TotalCollection);
            Assert.Equal(154037.71m, Math.Round(dsmCalc.NetGrossSales, 2));
            Assert.Equal(-0.71m, Math.Round(dsmCalc.Mismatch, 2));

            // Both views agree on exactly -0.71!
            Assert.Equal(vaijunathSummary.Difference, (double)Math.Round(dsmCalc.Mismatch, 2), 2);
        }

        try { File.Delete(tempTestDb); } catch { }
    }
}
