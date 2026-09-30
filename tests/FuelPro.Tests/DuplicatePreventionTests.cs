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
}
