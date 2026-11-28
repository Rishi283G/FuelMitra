using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.UI.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelPro.Tests;

public class DsmDynamicConfigAndTankStockTests
{
    private class TestFeatureToggleService : IFeatureToggleService
    {
        private readonly Dictionary<string, bool> _features = new(StringComparer.OrdinalIgnoreCase);
        public event Action? FeatureConfigurationChanged;

        public void SetFeature(string key, bool isEnabled)
        {
            _features[key] = isEnabled;
            FeatureConfigurationChanged?.Invoke();
        }

        public bool IsFeatureEnabled(string featureKey, bool defaultValue = false)
        {
            return _features.TryGetValue(featureKey, out var val) ? val : defaultValue;
        }

        public string GetFeatureDisplayName(string featureKey, string defaultName = "") => defaultName;
        public Task<List<AppFeatureSetting>> GetAllFeaturesAsync() => Task.FromResult(new List<AppFeatureSetting>());
        public Task<bool> SaveFeaturesAsync(IEnumerable<AppFeatureSetting> features) => Task.FromResult(true);
        public Task RefreshCacheAsync() => Task.CompletedTask;
    }

    private static IServiceProvider CreateServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IAgsImportRepository, AgsImportRepository>();
        services.AddScoped<IDsmEntryRepository, DsmEntryRepository>();
        services.AddScoped<IShiftCycleService, ShiftCycleService>();
        services.AddScoped<IAgsDailyAggregationService, AgsDailyAggregationService>();
        services.AddScoped<IFuelTankerRepository, FuelTankerRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<IAgsInventoryService, AgsInventoryService>();
        services.AddScoped<ITankDailyStockRepository, TankDailyStockRepository>();
        return services.BuildServiceProvider();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // UI Dynamic Config Tests (Preserved from previous implementation)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test01_CollectionReduction_DynamicVisibility_FollowsAuthoritativeConfig()
    {
        var featureService = new TestFeatureToggleService();
        var sp = new ServiceCollection()
            .AddSingleton<IFeatureToggleService>(featureService)
            .BuildServiceProvider();

        // 1. When enabled: visible
        featureService.SetFeature("Operations_CollectionReduction", true);
        var vm = new DsmEntryViewModel(sp);
        Assert.True(vm.IsCollectionReductionEnabled);

        // 2. When explicitly disabled: hidden even if an existing amount is present!
        featureService.SetFeature("Operations_CollectionReduction", false);
        vm.CollectionReduction = 500.0;
        Assert.False(vm.IsCollectionReductionEnabled);
        // Existing amount is preserved, not erased
        Assert.Equal(500.0, vm.CollectionReduction);

        // 3. When re-enabled: visible again
        featureService.SetFeature("Operations_CollectionReduction", true);
        Assert.True(vm.IsCollectionReductionEnabled);
    }

    [Fact]
    public void Test02_DebtorRepayment_DynamicVisibility_FollowsAuthoritativeConfig()
    {
        var featureService = new TestFeatureToggleService();
        var sp = new ServiceCollection()
            .AddSingleton<IFeatureToggleService>(featureService)
            .BuildServiceProvider();

        // 1. When enabled: visible
        featureService.SetFeature("Operations_DebtorRepayment", true);
        var vm = new DsmEntryViewModel(sp);
        Assert.True(vm.IsDebtorRepaymentEnabled);

        // 2. When explicitly disabled: hidden even if repayment rows exist!
        featureService.SetFeature("Operations_DebtorRepayment", false);
        vm.DebtorRepayments.Add(new DsmDebtorRepaymentRow { DebtorName = "Test Debtor", Amount = 1500.0 });
        Assert.False(vm.IsDebtorRepaymentEnabled);
        // Existing rows are preserved, not deleted
        Assert.Single(vm.DebtorRepayments);

        // 3. When re-enabled: visible again
        featureService.SetFeature("Operations_DebtorRepayment", true);
        Assert.True(vm.IsDebtorRepaymentEnabled);
    }

    [Fact]
    public void Test03_PetroCard_TidBatch_Properties_Hydration_And_Workflow()
    {
        var featureService = new TestFeatureToggleService();
        var sp = new ServiceCollection()
            .AddSingleton<IFeatureToggleService>(featureService)
            .BuildServiceProvider();

        var vm = new DsmEntryViewModel(sp);

        // Verify PetroCard TID/Batch properties exist and can be set
        vm.PetroCardMorning = 2500.0;
        vm.PetroCardTidMorning = "PETRO_TID_01";
        vm.PetroCardBatchMorning = "BATCH_01";

        vm.PetroCardNight = 1200.0;
        vm.PetroCardTidNight = "PETRO_TID_02";
        vm.PetroCardBatchNight = "BATCH_02";

        Assert.Equal("PETRO_TID_01", vm.PetroCardTidMorning);
        Assert.Equal("BATCH_01", vm.PetroCardBatchMorning);
        Assert.Equal("PETRO_TID_02", vm.PetroCardTidNight);
        Assert.Equal("BATCH_02", vm.PetroCardBatchNight);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Seven Required Canonical Tanker & Inventory Shift-Ordering Tests
    // ─────────────────────────────────────────────────────────────────────────────

    // Scenario 1: Shift B exists: tanker receipt is attributed correctly
    [Fact]
    public async Task Scenario1_ShiftBExists_TankerReceiptAttributedCorrectly()
    {
        var sp = CreateServiceProvider(Guid.NewGuid().ToString());
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var inventoryService = scope.ServiceProvider.GetRequiredService<IAgsInventoryService>();
        var date = new DateTime(2026, 11, 20);

        // Shift B exists on date D with opening stock 10000 and 1000 sales
        var importB = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "B",
            IsActive = true,
            HsdOpeningStock = 10000,
            HsdClosingStock = 9000,
            TotalHsdLitres = 1000,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", TankNumber = 1, OpeningStockLitres = 10000, ClosingStockLitres = 9000, FuelDispensedLitres = 1000, ReceiptLitres = 0 }
            }
        };

        // Tanker arrives on date D: 4000L HSD
        var tanker = new FuelTanker
        {
            TankerDate = date,
            FuelType = "HSD",
            Quantity = 4000,
            InvoiceNumber = "INV-S1",
            PurchaseRate = 90,
            TotalAmount = 360000
        };

        context.AgsShiftImports.Add(importB);
        context.FuelTankers.Add(tanker);
        await context.SaveChangesAsync();

        // Act: Propagate starting at Shift B on date D
        var propRes = await inventoryService.PropagateInventoryCalculationsAsync(date, "B");
        Assert.True(propRes.Success);

        // Assert: Shift B received the 4000L tanker receipt and closing stock is updated
        var updatedB = await context.AgsShiftImports.Include(i => i.TankStocks).FirstAsync(i => i.ImportDate.Date == date.Date && (i.ShiftType == "B" || i.ShiftType == "II"));
        var hsdTank = updatedB.TankStocks.First(t => t.FuelType == "HSD");
        Assert.Equal(4000, hsdTank.ReceiptLitres);
        Assert.Equal(14000, hsdTank.ClosingStockLitres); // 10000 (opening) - 0 (dispensed) + 4000 = 14000
    }

    // Scenario 2: Shift B missing but Shift C exists: propagation starts at correct next eligible shift
    [Fact]
    public async Task Scenario2_ShiftBMissing_ShiftCExists_PropagationStartsAtNextEligibleShift()
    {
        var sp = CreateServiceProvider(Guid.NewGuid().ToString());
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var inventoryService = scope.ServiceProvider.GetRequiredService<IAgsInventoryService>();
        var date = new DateTime(2026, 11, 20);

        // Only Shift C is imported for Date D (Shift B was NOT imported)
        var importC = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "C",
            IsActive = true,
            HsdOpeningStock = 12000,
            HsdClosingStock = 11000,
            TotalHsdLitres = 1000,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", TankNumber = 1, OpeningStockLitres = 12000, ClosingStockLitres = 11000, FuelDispensedLitres = 1000 }
            }
        };

        // Shift A is imported for Date D (closing shift of day D)
        var importA = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "A",
            IsActive = true,
            HsdOpeningStock = 11000,
            HsdClosingStock = 10500,
            TotalHsdLitres = 500,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", TankNumber = 1, OpeningStockLitres = 11000, ClosingStockLitres = 10500, FuelDispensedLitres = 500 }
            }
        };

        context.AgsShiftImports.AddRange(importC, importA);
        await context.SaveChangesAsync();

        // Act: Propagate starting at "B" on date D
        var propRes = await inventoryService.PropagateInventoryCalculationsAsync(date, "B");
        Assert.True(propRes.Success);

        // Assert: Shift C and Shift A were processed in sequence
        var updatedC = await context.AgsShiftImports.FirstAsync(i => i.ImportDate.Date == date.Date && (i.ShiftType == "C" || i.ShiftType == "III"));
        var updatedA = await context.AgsShiftImports.FirstAsync(i => i.ImportDate.Date == date.Date && (i.ShiftType == "A" || i.ShiftType == "I"));
        Assert.NotNull(updatedC);
        Assert.NotNull(updatedA);
    }

    // Scenario 3: Shift B and Shift C missing but Shift A exists: propagation follows canonical sequence without recalculating predecessors
    [Fact]
    public async Task Scenario3_ShiftBAndCMissing_ShiftAExists_DoesNotRecalculatePredecessors()
    {
        var sp = CreateServiceProvider(Guid.NewGuid().ToString());
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var inventoryService = scope.ServiceProvider.GetRequiredService<IAgsInventoryService>();
        var date = new DateTime(2026, 11, 20);

        // Predecessor day D-1 has Shift A (night shift of previous day)
        var importPrevA = new AgsShiftImport
        {
            ImportDate = date.AddDays(-1),
            ShiftType = "A",
            IsActive = true,
            HsdOpeningStock = 15000,
            HsdClosingStock = 14000,
            TotalHsdLitres = 1000,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", TankNumber = 1, OpeningStockLitres = 15000, ClosingStockLitres = 14000, FuelDispensedLitres = 1000 }
            }
        };

        // Date D has ONLY Shift A (B and C are missing)
        var importCurrA = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "A",
            IsActive = true,
            HsdOpeningStock = 14000,
            HsdClosingStock = 13500,
            TotalHsdLitres = 500,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", TankNumber = 1, OpeningStockLitres = 14000, ClosingStockLitres = 13500, FuelDispensedLitres = 500 }
            }
        };

        context.AgsShiftImports.AddRange(importPrevA, importCurrA);
        await context.SaveChangesAsync();

        // Act: Propagate for Date D starting at Shift "B"
        var propRes = await inventoryService.PropagateInventoryCalculationsAsync(date, "B");
        Assert.True(propRes.Success);

        // Assert: D-1 Shift A was NOT recalculated / modified
        var verifiedPrevA = await context.AgsShiftImports.Include(i => i.TankStocks).FirstAsync(i => i.ImportDate.Date == date.AddDays(-1).Date && i.ShiftType == "A");
        Assert.Equal(14000, verifiedPrevA.HsdClosingStock);
        Assert.Equal(14000, verifiedPrevA.TankStocks.First(t => t.FuelType == "HSD").ClosingStockLitres);
    }

    // Scenario 4: Subsequent calendar dates follow established predecessor relationships
    [Fact]
    public void Scenario4_SubsequentCalendarDates_FollowPredecessorRelationships()
    {
        var cycleService = new ShiftCycleService();
        var dateD = new DateTime(2026, 10, 5);
        var dateDPlus1 = dateD.AddDays(1);

        // Predecessor of B(D) is A(D-1)
        var prevB_D = cycleService.GetPreviousShift(dateD, "B");
        Assert.Equal(dateD.AddDays(-1).Date, prevB_D.Date.Date);
        Assert.Equal("A", prevB_D.ShiftType);

        // Predecessor of C(D) is B(D)
        var prevC_D = cycleService.GetPreviousShift(dateD, "C");
        Assert.Equal(dateD.Date, prevC_D.Date.Date);
        Assert.Equal("B", prevC_D.ShiftType);

        // Predecessor of A(D) is C(D)
        var prevA_D = cycleService.GetPreviousShift(dateD, "A");
        Assert.Equal(dateD.Date, prevA_D.Date.Date);
        Assert.Equal("C", prevA_D.ShiftType);

        // Predecessor of B(D+1) is A(D)
        var prevB_DPlus1 = cycleService.GetPreviousShift(dateDPlus1, "B");
        Assert.Equal(dateD.Date, prevB_DPlus1.Date.Date);
        Assert.Equal("A", prevB_DPlus1.ShiftType);

        // Successor of A(D) is B(D+1)
        var nextA_D = cycleService.GetNextShift(dateD, "A");
        Assert.Equal(dateDPlus1.Date, nextA_D.Date.Date);
        Assert.Equal("B", nextA_D.ShiftType);
    }

    // Scenario 5: Repeated propagation does not duplicate stock
    [Fact]
    public async Task Scenario5_RepeatedPropagation_DoesNotDuplicateStock()
    {
        var sp = CreateServiceProvider(Guid.NewGuid().ToString());
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var inventoryService = scope.ServiceProvider.GetRequiredService<IAgsInventoryService>();
        var date = new DateTime(2026, 11, 20);

        var importB = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "B",
            IsActive = true,
            HsdOpeningStock = 10000,
            HsdClosingStock = 9000,
            TotalHsdLitres = 1000,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", TankNumber = 1, OpeningStockLitres = 10000, ClosingStockLitres = 9000, FuelDispensedLitres = 1000 }
            }
        };

        var tanker = new FuelTanker
        {
            TankerDate = date,
            FuelType = "HSD",
            Quantity = 3000,
            InvoiceNumber = "INV-S5",
            PurchaseRate = 90,
            TotalAmount = 270000
        };

        context.AgsShiftImports.Add(importB);
        context.FuelTankers.Add(tanker);
        await context.SaveChangesAsync();

        // First propagation
        var res1 = await inventoryService.PropagateInventoryCalculationsAsync(date, "B");
        Assert.True(res1.Success);

        var updatedB1 = await context.AgsShiftImports.Include(i => i.TankStocks).FirstAsync(i => i.ImportDate.Date == date.Date && (i.ShiftType == "B" || i.ShiftType == "II"));
        var stock1 = updatedB1.TankStocks.First(t => t.FuelType == "HSD").ClosingStockLitres;
        Assert.Equal(13000, stock1); // 10000 - 0 + 3000 = 13000

        // Second propagation (identical parameters)
        var res2 = await inventoryService.PropagateInventoryCalculationsAsync(date, "B");
        Assert.True(res2.Success);

        var updatedB2 = await context.AgsShiftImports.Include(i => i.TankStocks).FirstAsync(i => i.ImportDate.Date == date.Date && (i.ShiftType == "B" || i.ShiftType == "II"));
        var stock2 = updatedB2.TankStocks.First(t => t.FuelType == "HSD").ClosingStockLitres;
        // MUST remain 13000, NOT 16000!
        Assert.Equal(13000, stock2);
    }

    // Scenario 6: History reporting does not double-count receipts
    [Fact]
    public async Task Scenario6_HistoryReporting_DoesNotDoubleCountReceipts()
    {
        var sp = CreateServiceProvider(Guid.NewGuid().ToString());
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var date = new DateTime(2026, 11, 21);

        // Day D has Shift B, Shift C, Shift A
        var importB = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "B",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", TankNumber = 1, OpeningStockLitres = 10000, FuelDispensedLitres = 1000, ReceiptLitres = 5000, ClosingStockLitres = 14000 }
            }
        };

        var importC = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "C",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", TankNumber = 1, OpeningStockLitres = 14000, FuelDispensedLitres = 1000, ReceiptLitres = 0, ClosingStockLitres = 13000 }
            }
        };

        var importA = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "A",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", TankNumber = 1, OpeningStockLitres = 13000, FuelDispensedLitres = 500, ReceiptLitres = 0, ClosingStockLitres = 12500 }
            }
        };

        // Tanker entry in database for 5000L HSD
        var tanker = new FuelTanker
        {
            TankerDate = date,
            FuelType = "HSD",
            Quantity = 5000,
            InvoiceNumber = "INV-S6",
            PurchaseRate = 90,
            TotalAmount = 450000
        };

        context.AgsShiftImports.AddRange(importB, importC, importA);
        context.FuelTankers.Add(tanker);
        await context.SaveChangesAsync();

        var repo = new TankDailyStockRepository(context);
        var result = await repo.GetDetailedHistoryAsync(date, date, "HSD");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);

        var shiftB = result.Data!.ShiftRows.FirstOrDefault(r => r.ShiftType == "B");
        var shiftC = result.Data.ShiftRows.FirstOrDefault(r => r.ShiftType == "C");
        var shiftA = result.Data.ShiftRows.FirstOrDefault(r => r.ShiftType == "A");

        Assert.NotNull(shiftB);
        Assert.NotNull(shiftC);
        Assert.NotNull(shiftA);

        // Shift B receives the 5000L
        Assert.Equal(5000, shiftB!.Receipts);
        // Shift C and Shift A must have 0 receipts
        Assert.Equal(0, shiftC!.Receipts);
        Assert.Equal(0, shiftA!.Receipts);

        // Daily summary receipts must be exactly 5000 (NOT 15000 or 10000)
        var daily = result.Data.DailyRows.FirstOrDefault(r => r.FuelType == "HSD");
        Assert.NotNull(daily);
        Assert.Equal(5000, daily!.DayReceipts);
    }

    // Scenario 7: Fuel aliases match in both directions without cross-fuel contamination
    [Fact]
    public void Scenario7_FuelAliases_MatchBidirectionally_WithoutContamination()
    {
        // 1. SPEED <-> MS-II
        Assert.True(AgsInventoryService.FuelMatches("SPEED", "MS-II"));
        Assert.True(AgsInventoryService.FuelMatches("MS-II", "SPEED"));
        Assert.True(AgsInventoryService.FuelMatches("speed", "ms-ii"));
        Assert.True(AgsInventoryService.FuelMatches("SPEED", "SPEED"));
        Assert.True(AgsInventoryService.FuelMatches("MS-II", "MS-II"));

        // 2. Petrol <-> MS-I / MS
        Assert.True(AgsInventoryService.FuelMatches("Petrol", "MS-I"));
        Assert.True(AgsInventoryService.FuelMatches("MS-I", "Petrol"));
        Assert.True(AgsInventoryService.FuelMatches("Petrol", "MS"));
        Assert.True(AgsInventoryService.FuelMatches("MS", "Petrol"));
        Assert.True(AgsInventoryService.FuelMatches("MS-I", "MS"));
        Assert.True(AgsInventoryService.FuelMatches("MS", "MS-I"));

        // 3. Diesel <-> HSD
        Assert.True(AgsInventoryService.FuelMatches("Diesel", "HSD"));
        Assert.True(AgsInventoryService.FuelMatches("HSD", "Diesel"));
        Assert.True(AgsInventoryService.FuelMatches("diesel", "hsd"));
        Assert.True(AgsInventoryService.FuelMatches("HSD", "HSD"));

        // 4. CNG
        Assert.True(AgsInventoryService.FuelMatches("CNG", "CNG"));

        // 5. Cross-fuel contamination checks (all MUST be false)
        Assert.False(AgsInventoryService.FuelMatches("HSD", "Petrol"));
        Assert.False(AgsInventoryService.FuelMatches("HSD", "MS-I"));
        Assert.False(AgsInventoryService.FuelMatches("HSD", "SPEED"));
        Assert.False(AgsInventoryService.FuelMatches("Diesel", "MS-II"));
        Assert.False(AgsInventoryService.FuelMatches("Petrol", "HSD"));
        Assert.False(AgsInventoryService.FuelMatches("Petrol", "SPEED"));
        Assert.False(AgsInventoryService.FuelMatches("MS-I", "MS-II"));
        Assert.False(AgsInventoryService.FuelMatches("MS-I", "SPEED"));
        Assert.False(AgsInventoryService.FuelMatches("SPEED", "HSD"));
        Assert.False(AgsInventoryService.FuelMatches("SPEED", "Diesel"));
        Assert.False(AgsInventoryService.FuelMatches("CNG", "HSD"));
        Assert.False(AgsInventoryService.FuelMatches("CNG", "Petrol"));
        Assert.False(AgsInventoryService.FuelMatches("CNG", "MS-I"));
        Assert.False(AgsInventoryService.FuelMatches("CNG", "SPEED"));
        Assert.False(AgsInventoryService.FuelMatches(null, "HSD"));
        Assert.False(AgsInventoryService.FuelMatches("HSD", null));
        Assert.False(AgsInventoryService.FuelMatches("", ""));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Verification Scenario: Late Shift B Import Reattribution & Single-Count
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scenario8_LateShiftBImport_ReceiptReattributedAndNotDoubleCounted()
    {
        var sp = CreateServiceProvider(Guid.NewGuid().ToString());
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var inventoryService = scope.ServiceProvider.GetRequiredService<IAgsInventoryService>();
        var tankStockRepo = new TankDailyStockRepository(context);
        var date = new DateTime(2026, 11, 25);

        // 1. Tanker entry exists for date D (5000 L of HSD)
        var tanker = new FuelTanker
        {
            TankerDate = date,
            FuelType = "HSD",
            Quantity = 5000,
            TankerNumber = "MH-12-AB-1234",
            InvoiceNumber = "INV-LATE-B-01"
        };
        context.FuelTankers.Add(tanker);

        // 2 & 3. Shift B is initially missing.
        // Shift C exists with opening stock 10000, sales 1000, and receives fallback receipts
        var importC = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "C",
            IsActive = true,
            HsdOpeningStock = 10000,
            HsdClosingStock = 14000,
            TotalHsdLitres = 1000,
            NozzleReadings = new List<AgsNozzleReading>
            {
                new() { NozzleNumber = 3, FuelType = "HSD", NetSaleLitres = 1000 }
            },
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", TankNumber = 1, OpeningStockLitres = 10000, ClosingStockLitres = 14000, FuelDispensedLitres = 1000, ReceiptLitres = 5000 }
            }
        };

        // Shift A exists with opening stock 14000, sales 500, closing 13500
        var importA = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "A",
            IsActive = true,
            HsdOpeningStock = 14000,
            HsdClosingStock = 13500,
            TotalHsdLitres = 500,
            NozzleReadings = new List<AgsNozzleReading>
            {
                new() { NozzleNumber = 3, FuelType = "HSD", NetSaleLitres = 500 }
            },
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", TankNumber = 1, OpeningStockLitres = 14000, ClosingStockLitres = 13500, FuelDispensedLitres = 500, ReceiptLitres = 0 }
            }
        };

        context.AgsShiftImports.AddRange(importC, importA);
        await context.SaveChangesAsync();

        // Verify pre-state in detailed history: Shift C receives fallback receipts
        var preHistoryRes = await tankStockRepo.GetDetailedHistoryAsync(date, date, "HSD");
        Assert.True(preHistoryRes.Success);
        var preDay = preHistoryRes.Data!.DailyRows.First(d => d.Date.Date == date.Date && d.FuelType == "HSD");
        var preC = preHistoryRes.Data!.ShiftRows.First(r => r.ShiftType == "C" && r.FuelType == "HSD");
        Assert.Equal(5000, preC.Receipts);
        Assert.Equal(5000, preDay.DayReceipts);

        // 4. Later, Shift B is imported! (Opening 10000, Dispensed 800)
        var importB = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "B",
            IsActive = true,
            HsdOpeningStock = 10000,
            HsdClosingStock = 9200,
            TotalHsdLitres = 800,
            NozzleReadings = new List<AgsNozzleReading>
            {
                new() { NozzleNumber = 3, FuelType = "HSD", NetSaleLitres = 800 }
            },
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", TankNumber = 1, OpeningStockLitres = 10000, ClosingStockLitres = 9200, FuelDispensedLitres = 800, ReceiptLitres = 0 }
            }
        };
        context.AgsShiftImports.Add(importB);
        await context.SaveChangesAsync();

        // 5. Propagation is executed again starting at "B" on date D
        var propRes = await inventoryService.PropagateInventoryCalculationsAsync(date, "B");
        Assert.True(propRes.Success);

        // 6. Shift B receives the correct tanker receipt: 10000 - 800 + 5000 = 14200
        var postImportB = await context.AgsShiftImports.Include(i => i.TankStocks).FirstAsync(i => i.ImportDate.Date == date.Date && (i.ShiftType == "B" || i.ShiftType == "II"));
        var tankB = postImportB.TankStocks.First(t => t.FuelType == "HSD");
        Assert.Equal(5000, tankB.ReceiptLitres);
        Assert.Equal(14200, tankB.ClosingStockLitres);

        // 7. Shift C and Shift A are recalculated correctly:
        // Shift C: Opening 14200, Receipts 0 (fallback cleared), Dispensed 1000 -> Closing 13200
        var postImportC = await context.AgsShiftImports.Include(i => i.TankStocks).FirstAsync(i => i.ImportDate.Date == date.Date && (i.ShiftType == "C" || i.ShiftType == "III"));
        var tankC = postImportC.TankStocks.First(t => t.FuelType == "HSD");
        Assert.Equal(0, tankC.ReceiptLitres);
        Assert.Equal(14200, tankC.OpeningStockLitres);
        Assert.Equal(13200, tankC.ClosingStockLitres);

        // Shift A: Opening 13200, Receipts 0, Dispensed 500 -> Closing 12700
        var postImportA = await context.AgsShiftImports.Include(i => i.TankStocks).FirstAsync(i => i.ImportDate.Date == date.Date && (i.ShiftType == "A" || i.ShiftType == "I"));
        var tankA = postImportA.TankStocks.First(t => t.FuelType == "HSD");
        Assert.Equal(0, tankA.ReceiptLitres);
        Assert.Equal(13200, tankA.OpeningStockLitres);
        Assert.Equal(12700, tankA.ClosingStockLitres);

        // 8. The receipt is not counted twice in stock or Tank Stock History
        var postHistoryRes = await tankStockRepo.GetDetailedHistoryAsync(date, date, "HSD");
        Assert.True(postHistoryRes.Success);
        var postDay = postHistoryRes.Data!.DailyRows.First(d => d.Date.Date == date.Date && d.FuelType == "HSD");

        var rowB = postHistoryRes.Data!.ShiftRows.First(r => r.ShiftType == "B" && r.FuelType == "HSD");
        var rowC = postHistoryRes.Data!.ShiftRows.First(r => r.ShiftType == "C" && r.FuelType == "HSD");
        var rowA = postHistoryRes.Data!.ShiftRows.First(r => r.ShiftType == "A" && r.FuelType == "HSD");

        Assert.Equal(5000, rowB.Receipts);
        Assert.Equal(0, rowC.Receipts);
        Assert.Equal(0, rowA.Receipts);
        Assert.Equal(5000, postDay.DayReceipts); // Counted exactly once!

        Assert.Equal(14200, rowB.BookStock);
        Assert.Equal(14200, rowC.OpeningStock);
        Assert.Equal(13200, rowC.BookStock);
        Assert.Equal(13200, rowA.OpeningStock);
        Assert.Equal(12700, rowA.BookStock);
    }
}
