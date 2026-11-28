using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelPro.Tests;

public class Stage4TankStockTests
{
    private readonly IShiftCycleService _cycleService = new ShiftCycleService();

    // ─────────────────────────────────────────────────────────────────────────────
    // 1. Mitali shift predecessor and continuity rules
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test01_MitaliShiftPredecessorSequence_B_Opens_C_Follows_A_Closes()
    {
        var date = new DateTime(2026, 10, 5);

        // Canonical Sequence for dynamic PyroSync: Prev(B,D) = (D-1, A), Prev(C,D) = (D, B), Prev(A,D) = (D, C)
        var prevB = _cycleService.GetPreviousShift(date, "B");
        Assert.Equal(date.AddDays(-1).Date, prevB.Date.Date);
        Assert.Equal("A", prevB.ShiftType);

        var prevC = _cycleService.GetPreviousShift(date, "C");
        Assert.Equal(date.Date, prevC.Date.Date);
        Assert.Equal("B", prevC.ShiftType);

        var prevA = _cycleService.GetPreviousShift(date, "A");
        Assert.Equal(date.Date, prevA.Date.Date);
        Assert.Equal("C", prevA.ShiftType);

        // Canonical Successor: Next(B,D) = (D, C), Next(C,D) = (D, A), Next(A,D) = (D+1, B)
        var nextB = _cycleService.GetNextShift(date, "B");
        Assert.Equal(date.Date, nextB.Date.Date);
        Assert.Equal("C", nextB.ShiftType);

        var nextC = _cycleService.GetNextShift(date, "C");
        Assert.Equal(date.Date, nextC.Date.Date);
        Assert.Equal("A", nextC.ShiftType);

        var nextA = _cycleService.GetNextShift(date, "A");
        Assert.Equal(date.AddDays(1).Date, nextA.Date.Date);
        Assert.Equal("B", nextA.ShiftType);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2. Daily opening from Shift B and closing from Shift A
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test02_DailyOpeningFromShiftB_DailyClosingFromShiftA()
    {
        var service = new AgsDailyAggregationService();
        var date = new DateTime(2026, 10, 5);

        var shiftB = new AgsShiftImport { ImportDate = date, ShiftType = "B", IsActive = true, HsdOpeningStock = 14500, HsdClosingStock = 13500 };
        var shiftC = new AgsShiftImport { ImportDate = date, ShiftType = "C", IsActive = true, HsdOpeningStock = 13500, HsdClosingStock = 12500 };
        var shiftA = new AgsShiftImport { ImportDate = date, ShiftType = "A", IsActive = true, HsdOpeningStock = 12500, HsdClosingStock = 11500 };

        var summary = service.Aggregate(date, new List<AgsShiftImport> { shiftB, shiftC, shiftA });

        Assert.Equal(14500, summary.HsdDayOpeningStock); // Shift B opening
        Assert.Equal(11500, summary.HsdDayClosingStock); // Shift A closing
        Assert.Equal("B", _cycleService.GetOpeningShiftForDate(date));
        Assert.Equal("A", _cycleService.GetClosingShiftForDate(date));
    }

    [Fact]
    public void Test02b_InProgressDay_ShowsProvisionalClosingFromLatestValidShift()
    {
        var service = new AgsDailyAggregationService();
        var date = new DateTime(2026, 10, 5);

        // Day in progress: Only Shift B and Shift C have completed
        var shiftB = new AgsShiftImport { ImportDate = date, ShiftType = "B", IsActive = true, HsdOpeningStock = 14500, HsdClosingStock = 13500 };
        var shiftC = new AgsShiftImport { ImportDate = date, ShiftType = "C", IsActive = true, HsdOpeningStock = 13500, HsdClosingStock = 12500 };

        var summary = service.Aggregate(date, new List<AgsShiftImport> { shiftB, shiftC });

        Assert.Equal(14500, summary.HsdDayOpeningStock); // Shift B opening
        Assert.Equal(12500, summary.HsdDayClosingStock); // Shift C provisional closing
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3. Book stock calculation: Opening - Dispensed + Testing + Receipts
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test03_BookStockCalculation_Opening_Minus_Sales_Plus_Testing_Plus_Receipts()
    {
        var group = new NozzleGroupDto
        {
            GroupName = "HSD (Tank 1)",
            FuelType = "HSD",
            OpeningStock = 10000,
            FuelDispensed = 2500,
            TestingLitres = 50,
            Receipts = 4000
        };

        // Book stock = 10000 - 2500 + 50 + 4000 = 11550
        Assert.Equal(11550, group.CalculatedStock);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 4. Positive excess and negative shortage variations
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test04_Variance_Excess_And_Shortage_Calculations()
    {
        // 1. Excess: Measured > Calculated
        var excessGroup = new NozzleGroupDto
        {
            GroupName = "MS-I",
            FuelType = "MS-I",
            OpeningStock = 5000,
            FuelDispensed = 1000,
            TestingLitres = 0,
            Receipts = 0,
            HasPhysicalDip = true,
            Dip = 650,
            Stock = 4050 // Measured stock 4050 > Book stock 4000
        };
        Assert.Equal(4000, excessGroup.CalculatedStock);
        Assert.Equal(50, excessGroup.StockVariance);
        Assert.Equal("+50.00", excessGroup.StockVarianceDisplay);
        Assert.Equal("Excess", excessGroup.StockVarianceStatus);

        // 2. Shortage: Measured < Calculated
        var shortageGroup = new NozzleGroupDto
        {
            GroupName = "MS-I",
            FuelType = "MS-I",
            OpeningStock = 5000,
            FuelDispensed = 1000,
            TestingLitres = 0,
            Receipts = 0,
            HasPhysicalDip = true,
            Dip = 630,
            Stock = 3970 // Measured stock 3970 < Book stock 4000
        };
        Assert.Equal(-30, shortageGroup.StockVariance);
        Assert.Equal("-30.00", shortageGroup.StockVarianceDisplay);
        Assert.Equal("Shortage", shortageGroup.StockVarianceStatus);

        // 3. Balanced: Measured == Calculated
        var balancedGroup = new NozzleGroupDto
        {
            GroupName = "MS-I",
            FuelType = "MS-I",
            OpeningStock = 5000,
            FuelDispensed = 1000,
            TestingLitres = 0,
            Receipts = 0,
            HasPhysicalDip = true,
            Dip = 640,
            Stock = 4000
        };
        Assert.Equal(0, balancedGroup.StockVariance);
        Assert.Equal("Balanced", balancedGroup.StockVarianceStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 5. Explicit manual zero dip through save/reload and next-shift handoff
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test05_ExplicitManualZeroDip_PreservedAndHandedOffToNextShift()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FuelProDbContext>().UseInMemoryDatabase(dbName).Options;
        var serviceProvider = new ServiceCollection()
            .AddDbContext<FuelProDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddScoped<IAgsImportRepository, AgsImportRepository>()
            .AddScoped<IDsmEntryRepository, DsmEntryRepository>()
            .AddScoped<IShiftCycleService, ShiftCycleService>()
            .AddScoped<IAgsDailyAggregationService, AgsDailyAggregationService>()
            .AddScoped<IFuelTankerRepository, FuelTankerRepository>()
            .AddScoped<IShiftRepository, ShiftRepository>()
            .AddScoped<IAgsInventoryService, AgsInventoryService>()
            .BuildServiceProvider();

        var invService = serviceProvider.GetRequiredService<IAgsInventoryService>();
        var date = new DateTime(2026, 10, 5);

        // Shift B: Tank empty (0.0 dip explicitly entered)
        var shiftBGroups = new List<NozzleGroupDto>
        {
            new()
            {
                GroupName = "Petrol (Tank 1)",
                FuelType = "MS-I",
                OpeningStock = 500,
                FuelDispensed = 500,
                TestingLitres = 0,
                Receipts = 0,
                HasManualDipEntry = true,
                HasPhysicalDip = true,
                Dip = 0.0,
                Stock = 0.0 // Genuine zero stock
            }
        };

        var saveRes = await invService.SaveAndPersistInventoryAsync(date, "B", shiftBGroups);
        Assert.True(saveRes.Success);
        var savedImport = saveRes.Data!;
        var savedTank = savedImport.TankStocks!.First(t => t.FuelType == "MS-I");

        // Verify explicit provenance persisted
        Assert.True(savedTank.HasPhysicalDip);
        Assert.Equal("Manual", savedTank.DipSource);
        Assert.Equal(0.0, savedTank.ClosingStockLitres);
        Assert.Equal(0.0, savedTank.ClosingDipMM);

        // Now load Shift C (the subsequent shift)
        var shiftCGroups = await invService.BuildNozzleGroupsAsync(date, "C");
        var cTank = shiftCGroups.First(g => g.FuelType == "Petrol" || g.GroupName.Contains("Tank 1"));

        // Must receive genuine 0.0 opening stock from Shift B (not discard it!)
        Assert.Equal(0.0, cTank.OpeningStock);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 6. Explicit AGS zero dip through import, sync payload, and remote reconstruction
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test06_AgsExplicitZeroDip_SyncPayloadSerializationAndReconstruction()
    {
        var aggService = new AgsDailyAggregationService();
        var date = new DateTime(2026, 10, 5);

        // Shift B has a verified AGS PDF import explicitly recording 0 dip and 0 stock
        var shiftB = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "B",
            PdfFileName = "AGS_Report_20261005_B.pdf",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new()
                {
                    FuelType = "HSD",
                    TankNumber = 1,
                    OpeningStockLitres = 200,
                    FuelDispensedLitres = 200,
                    ClosingDipMM = 0.0,
                    ClosingStockLitres = 0.0,
                    HasPhysicalDip = false // Local column was not present on remote machine
                }
            }
        };

        var summary = aggService.Aggregate(date, new List<AgsShiftImport> { shiftB });

        // Verify ShiftBreakdownJson carries the explicit gauge measurement
        Assert.NotNull(summary.ShiftBreakdownJson);
        Assert.Contains("\"tankMeasurements\"", summary.ShiftBreakdownJson);

        // Remote client parses ShiftBreakdownJson
        var measurement = AgsDailyAggregationService.ParseTankMeasurement(summary.ShiftBreakdownJson, "B", "HSD");
        Assert.NotNull(measurement);
        Assert.True(measurement!.HasMeasurement);
        Assert.Equal(0.0, measurement.MeasuredStock);
        Assert.Equal(0.0, measurement.DipMm);
        Assert.Equal("AgsGauge", measurement.Source);

        // Remote client 4-tier provenance resolver test (where local HasPhysicalDip is false)
        var unhydratedTank = new AgsTankStock
        {
            FuelType = "HSD",
            ClosingDipMM = 0.0,
            ClosingStockLitres = 0.0,
            HasPhysicalDip = false
        };

        var resolved = AgsInventoryService.ResolveMeasurementProvenance(
            unhydratedTank,
            null, // no local PDF import on this client
            summary.ShiftBreakdownJson,
            "B",
            "HSD");

        Assert.True(resolved.HasMeasurement);
        Assert.Equal(0.0, resolved.MeasuredStock);
        Assert.Equal("AgsGauge", resolved.Source);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 7. Missing/default zero remains Dip Not Recorded
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test07_DefaultZeroWithNoMeasurementEvidence_RemainsDipNotRecorded()
    {
        // Unverified default zero (no manual entry, no AGS PDF, no sync carrier)
        var unmeasuredTank = new AgsTankStock
        {
            FuelType = "HSD",
            OpeningStockLitres = 8000,
            FuelDispensedLitres = 2000,
            ClosingDipMM = 0.0,
            ClosingStockLitres = 6000, // book stock carried forward
            HasPhysicalDip = false,
            DipSource = null
        };

        var resolved = AgsInventoryService.ResolveMeasurementProvenance(
            unmeasuredTank,
            null,
            null,
            "B",
            "HSD");

        Assert.False(resolved.HasMeasurement);
        Assert.Null(resolved.Source);

        var group = new NozzleGroupDto
        {
            FuelType = "HSD",
            OpeningStock = 8000,
            FuelDispensed = 2000,
            HasPhysicalDip = false,
            HasManualDipEntry = false,
            Dip = 0.0,
            Stock = 6000
        };

        Assert.False(group.HasMeasurement);
        Assert.Equal("Dip Not Recorded", group.StockVarianceStatus);
        Assert.Equal("—", group.StockVarianceDisplay);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 8. Positive legacy dips remain compatible
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test08_PositiveLegacyDip_RemainsCompatibleWithoutNewColumns()
    {
        // Legacy record in SQLite where HasPhysicalDip was false/null, but positive dip was recorded
        var legacyTank = new AgsTankStock
        {
            FuelType = "HSD",
            OpeningStockLitres = 10000,
            FuelDispensedLitres = 3000,
            ClosingDipMM = 750.0,
            ClosingStockLitres = 7100.0,
            HasPhysicalDip = false,
            DipSource = null
        };

        var resolved = AgsInventoryService.ResolveMeasurementProvenance(
            legacyTank,
            null,
            null,
            "A",
            "HSD");

        Assert.True(resolved.HasMeasurement);
        Assert.Equal(7100.0, resolved.MeasuredStock);
        Assert.Equal(750.0, resolved.DipValue);
        Assert.Equal("Legacy", resolved.Source);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 9. Missing import summary with complete underlying data is reconstructable
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test09_MissingShiftCImport_ReconstructedFromUnderlyingDsmData()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FuelProDbContext>().UseInMemoryDatabase(dbName).Options;

        using var context = new FuelProDbContext(options);
        var repo = new TankDailyStockRepository(context);
        var date = new DateTime(2026, 10, 5);

        // 1. Shift B import exists on D-1
        var importB = new AgsShiftImport
        {
            AgsShiftImportId = 1,
            ImportDate = date.AddDays(-1),
            ShiftType = "B",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", OpeningStockLitres = 15000, FuelDispensedLitres = 3000, ClosingStockLitres = 12000, HasPhysicalDip = true, ClosingDipMM = 1200 }
            }
        };

        // 2. Shift C AGS import is MISSING on D-1! But DSM shift and nozzle readings exist for D-1:
        var shiftC = new Shift { ShiftId = 2, ShiftDate = date.AddDays(-1), ShiftType = "C" };
        var entryC = new DsmEntry { DsmEntryId = 20, ShiftId = 2, Shift = shiftC, PumpId = 1 };
        var nozzleC = new NozzleReading { NozzleReadingId = 201, DsmEntryId = 20, DsmEntry = entryC, FuelType = "HSD", SaleLitres = 2000 };
        var testC = new TestingEntry { TestingId = 202, DsmEntryId = 20, DsmEntry = entryC, FuelType = "HSD", Litres = 50 };

        // 3. Shift A import exists on D
        var importA = new AgsShiftImport
        {
            AgsShiftImportId = 3,
            ImportDate = date,
            ShiftType = "A",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", OpeningStockLitres = 10050, FuelDispensedLitres = 1000, ClosingStockLitres = 9050, HasPhysicalDip = true, ClosingDipMM = 950 }
            }
        };

        context.AddRange(importB, shiftC, entryC, nozzleC, testC, importA);
        await context.SaveChangesAsync();

        var historyRes = await repo.GetDetailedHistoryAsync(date, date, "HSD");
        Assert.True(historyRes.Success);
        var shiftARow = historyRes.Data!.ShiftRows.FirstOrDefault(r => r.ShiftType == "A" && r.FuelType == "HSD");

        Assert.NotNull(shiftARow);
        // Shift A opening should be reconstructed: ShiftB.Closing(12000) - ShiftC.Sales(2000) + ShiftC.Testing(50) = 10050
        Assert.Equal(10050, shiftARow!.OpeningStock);
        Assert.Equal("Reconstructed", shiftARow.ContinuityStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 10. Missing or unverifiable movements stop reconstruction
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test10_MissingInterveningMovements_MarksContinuityAsIncomplete()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FuelProDbContext>().UseInMemoryDatabase(dbName).Options;

        using var context = new FuelProDbContext(options);
        var repo = new TankDailyStockRepository(context);
        var date = new DateTime(2026, 10, 5);

        // Shift B import exists on D-1
        var importB = new AgsShiftImport
        {
            AgsShiftImportId = 1,
            ImportDate = date.AddDays(-1),
            ShiftType = "B",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", ClosingStockLitres = 12000 }
            }
        };

        // Shift C is COMPLETELY MISSING: No import AND no DSM entries on D-1!
        // Shift A exists on D
        var importA = new AgsShiftImport
        {
            AgsShiftImportId = 2,
            ImportDate = date,
            ShiftType = "A",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", OpeningStockLitres = 10000, FuelDispensedLitres = 1000, ClosingStockLitres = 9000 }
            }
        };

        context.AddRange(importB, importA);
        await context.SaveChangesAsync();

        var historyRes = await repo.GetDetailedHistoryAsync(date, date, "HSD");
        Assert.True(historyRes.Success);
        var shiftARow = historyRes.Data!.ShiftRows.FirstOrDefault(r => r.ShiftType == "A" && r.FuelType == "HSD");

        Assert.NotNull(shiftARow);
        // Unverifiable Shift C movements -> marks continuity as Incomplete
        Assert.Equal("Incomplete", shiftARow!.ContinuityStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 11. Genuine zero stock is recognized as valid when explicitly recorded
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test11_GenuineZeroStock_NotDiscardedOrReplacedWithCalculated()
    {
        var group = new NozzleGroupDto
        {
            GroupName = "Petrol",
            FuelType = "MS-I",
            OpeningStock = 100,
            FuelDispensed = 20,
            TestingLitres = 0,
            Receipts = 0,
            HasPhysicalDip = true,
            Dip = 0.0,
            Stock = 0.0 // Tank ran dry!
        };

        // Calculated book stock is 80, but physical dip proved 0.0
        Assert.Equal(80.0, group.CalculatedStock);
        Assert.Equal(0.0, group.Stock);
        Assert.Equal(-80.0, group.StockVariance);
        Assert.Equal("Shortage", group.StockVarianceStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 12. Historical queries are idempotent and read-only
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test12_HistoricalQueriesAreIdempotentAndReadOnly()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FuelProDbContext>().UseInMemoryDatabase(dbName).Options;

        using var context = new FuelProDbContext(options);
        var date = new DateTime(2026, 10, 5);

        var import = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "B",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", OpeningStockLitres = 10000, FuelDispensedLitres = 2000, ClosingStockLitres = 8000 }
            }
        };
        context.Add(import);
        await context.SaveChangesAsync();

        int initialImportCount = await context.AgsShiftImports.CountAsync();
        int initialStockCount = await context.TankDailyStocks.CountAsync();

        var repo = new TankDailyStockRepository(context);

        // Run query 1
        var res1 = await repo.GetDetailedHistoryAsync(date, date);
        Assert.True(res1.Success);

        // Run query 2
        var res2 = await repo.GetDetailedHistoryAsync(date, date);
        Assert.True(res2.Success);

        // Assert zero mutation to underlying tables
        int finalImportCount = await context.AgsShiftImports.CountAsync();
        int finalStockCount = await context.TankDailyStocks.CountAsync();

        Assert.Equal(initialImportCount, finalImportCount);
        Assert.Equal(initialStockCount, finalStockCount);
        Assert.Equal(res1.Data!.ShiftRows.Count, res2.Data!.ShiftRows.Count);
        Assert.Equal(res1.Data.DailyRows.Count, res2.Data.DailyRows.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 13. CNG quantities remain in kilograms without liquid-dip calculations
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test13_CngQuantitiesRemainInKilograms_DipAndVariationShownAsNA()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FuelProDbContext>().UseInMemoryDatabase(dbName).Options;

        using var context = new FuelProDbContext(options);
        var date = new DateTime(2026, 10, 5);

        // DSM entry with CNG sales
        var shiftB = new Shift { ShiftId = 1, ShiftDate = date, ShiftType = "B" };
        var entryB = new DsmEntry { DsmEntryId = 1, ShiftId = 1, Shift = shiftB, PumpId = 4 };
        var cngReading = new NozzleReading { NozzleReadingId = 1, DsmEntryId = 1, DsmEntry = entryB, FuelType = "CNG", SaleLitres = 350.50 };

        context.AddRange(shiftB, entryB, cngReading);
        await context.SaveChangesAsync();

        var repo = new TankDailyStockRepository(context);
        var res = await repo.GetDetailedHistoryAsync(date, date, "CNG");

        Assert.True(res.Success);
        var shiftRow = res.Data!.ShiftRows.FirstOrDefault(r => r.FuelType == "CNG");
        Assert.NotNull(shiftRow);
        Assert.Equal("Kg", shiftRow!.Unit);
        Assert.Equal(350.50, shiftRow.FuelDispensed);
        Assert.False(shiftRow.HasPhysicalMeasurement);
        Assert.Equal(0, shiftRow.DipMm);
        Assert.Null(shiftRow.MeasuredStock);
        Assert.Null(shiftRow.Variance);
        Assert.Equal("N/A", shiftRow.VarianceStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 14. Manual Stock entry without dip sets physical measurement and preserves stock
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test14_ManualStockEntryWithoutDip_SetsMeasurementAndPreservesStock()
    {
        var group = new NozzleGroupDto
        {
            FuelType = "HSD",
            OpeningStock = 10000,
            Receipts = 0,
            TestingLitres = 0
        };

        // Initially no measurement
        Assert.False(group.HasMeasurement);
        Assert.False(group.HasManualStockEntry);

        // User enters stock directly without entering dip
        group.Stock = 8550.25;

        Assert.True(group.HasManualStockEntry);
        Assert.True(group.HasMeasurement);
        Assert.True(group.HasPhysicalDip);
        Assert.Equal(8550.25, group.Stock);
        Assert.Equal("Manual", group.DipSource);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 15. Programmatic/calculated stock update does not set physical measurement
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test15_CalculatedStockUpdate_DoesNotSetPhysicalMeasurement()
    {
        var group = new NozzleGroupDto
        {
            FuelType = "MS-I",
            OpeningStock = 12000,
            Receipts = 0,
            TestingLitres = 0
        };

        // Engine calculates book stock and updates stock internally
        group.SetCalculatedStockInternal(11200.50);

        Assert.False(group.HasManualStockEntry);
        Assert.False(group.HasMeasurement);
        Assert.False(group.HasPhysicalDip.GetValueOrDefault(false));
        Assert.Equal(11200.50, group.Stock);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 16. Roman shift imports (I, II, III) populate Physical Stock & Variance
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test16_GetDetailedHistoryAsync_WithRomanShiftImports_PopulatesPhysicalStockAndVariance()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FuelProDbContext>().UseInMemoryDatabase(dbName).Options;

        using var context = new FuelProDbContext(options);
        var date = new DateTime(2026, 10, 5);

        // Shift II (B) with physical measurement
        var importB = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "II",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new()
                {
                    FuelType = "HSD",
                    OpeningStockLitres = 15000,
                    FuelDispensedLitres = 3000,
                    ClosingStockLitres = 12050, // Physical measurement entered
                    HasPhysicalDip = true,
                    ClosingDipMM = 1150,
                    DipSource = "Manual"
                }
            }
        };

        // Shift III (C) with physical measurement
        var importC = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "III",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new()
                {
                    FuelType = "HSD",
                    OpeningStockLitres = 12050,
                    FuelDispensedLitres = 2000,
                    ClosingStockLitres = 10020, // Physical measurement entered
                    HasPhysicalDip = true,
                    ClosingDipMM = 1020,
                    DipSource = "Manual"
                }
            }
        };

        context.AddRange(importB, importC);
        await context.SaveChangesAsync();

        var repo = new TankDailyStockRepository(context);
        var result = await repo.GetDetailedHistoryAsync(date, date, "HSD");

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.ShiftRows.Count);

        var shiftBRow = result.Data.ShiftRows.FirstOrDefault(r => r.ShiftType == "B");
        Assert.NotNull(shiftBRow);
        Assert.True(shiftBRow!.HasPhysicalMeasurement);
        Assert.Equal(12050, shiftBRow.MeasuredStock);
        Assert.Equal(50, shiftBRow.Variance); // 12050 - (15000 - 3000) = +50
        Assert.Equal("Excess", shiftBRow.VarianceStatus);

        var shiftCRow = result.Data.ShiftRows.FirstOrDefault(r => r.ShiftType == "C");
        Assert.NotNull(shiftCRow);
        Assert.True(shiftCRow!.HasPhysicalMeasurement);
        Assert.Equal(10020, shiftCRow.MeasuredStock);

        // Daily summary row should have physical stock from latest shift measurement (Shift C)
        var dailyRow = result.Data.DailyRows.FirstOrDefault(r => r.FuelType == "HSD");
        Assert.NotNull(dailyRow);
        Assert.Equal(10020, dailyRow!.DayMeasuredStock);
        Assert.NotNull(dailyRow.DayVariance);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 17. SPEED alias matches MS-II tank stock without duplicate rows
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test17_GetDetailedHistoryAsync_SpeedFuelAlias_MatchesSpeedTankStockWithoutDuplicates()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FuelProDbContext>().UseInMemoryDatabase(dbName).Options;

        using var context = new FuelProDbContext(options);
        var date = new DateTime(2026, 10, 5);

        // Tank 3 is configured as SPEED in AGS
        var import = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "B",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new()
                {
                    FuelType = "SPEED",
                    OpeningStockLitres = 8000,
                    FuelDispensedLitres = 1200,
                    ClosingStockLitres = 6780,
                    HasPhysicalDip = true,
                    ClosingDipMM = 890,
                    DipSource = "Manual"
                }
            }
        };

        context.Add(import);
        await context.SaveChangesAsync();

        var repo = new TankDailyStockRepository(context);

        // Query for MS-II (or All Fuels)
        var result = await repo.GetDetailedHistoryAsync(date, date, "MS-II");
        Assert.True(result.Success);

        // Should return exactly 1 shift row and 1 daily row for MS-II
        Assert.Single(result.Data!.ShiftRows);
        Assert.Single(result.Data.DailyRows);

        var shiftRow = result.Data.ShiftRows.First();
        Assert.Equal("MS-II", shiftRow.FuelType);
        Assert.Equal("Tank 3", shiftRow.TankName);
        Assert.True(shiftRow.HasPhysicalMeasurement);
        Assert.Equal(6780, shiftRow.MeasuredStock);
        Assert.Equal(-20, shiftRow.Variance); // 6780 - (8000 - 1200) = -20
        Assert.Equal("Shortage", shiftRow.VarianceStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 18. CNG physical stock and variance supported in Kg
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test18_GetDetailedHistoryAsync_CngPhysicalStockAndVariance_SupportedInKg()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FuelProDbContext>().UseInMemoryDatabase(dbName).Options;

        using var context = new FuelProDbContext(options);
        var date = new DateTime(2026, 10, 5);

        var import = new AgsShiftImport
        {
            ImportDate = date,
            ShiftType = "B",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new()
                {
                    FuelType = "CNG",
                    OpeningStockLitres = 1200,
                    FuelDispensedLitres = 400,
                    ClosingStockLitres = 810, // Physical stock in Kg
                    HasPhysicalDip = true,
                    ClosingDipMM = 0,
                    DipSource = "Manual"
                }
            }
        };

        context.Add(import);
        await context.SaveChangesAsync();

        var repo = new TankDailyStockRepository(context);
        var result = await repo.GetDetailedHistoryAsync(date, date, "CNG");

        Assert.True(result.Success);
        var shiftRow = result.Data!.ShiftRows.FirstOrDefault(r => r.FuelType == "CNG");
        Assert.NotNull(shiftRow);
        Assert.Equal("Kg", shiftRow!.Unit);
        Assert.True(shiftRow.HasPhysicalMeasurement);
        Assert.Equal(810, shiftRow.MeasuredStock);
        Assert.Equal(10, shiftRow.Variance); // 810 - (1200 - 400) = +10
        Assert.Equal("Excess", shiftRow.VarianceStatus);
        Assert.Equal(0, shiftRow.DipMm);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 19. Historical TankDailyStocks fallback surfaces manual stock
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test19_HistoricalTankDailyStocks_SurfacesManualStockWhenAgsImportMissing()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FuelProDbContext>().UseInMemoryDatabase(dbName).Options;

        using var context = new FuelProDbContext(options);
        var date = new DateTime(2026, 10, 5);

        // DSM entry to create a valid day with shift activity
        var shift = new Shift { ShiftId = 1, ShiftDate = date, ShiftType = "B" };
        var entry = new DsmEntry { DsmEntryId = 1, ShiftId = 1, Shift = shift, PumpId = 1 };
        var reading = new NozzleReading { NozzleReadingId = 1, DsmEntryId = 1, DsmEntry = entry, FuelType = "HSD", SaleLitres = 1500 };

        // Historical TankDailyStock populated from previous settlement
        var dailyStock = new TankDailyStock
        {
            Date = date,
            FuelType = "HSD",
            OpeningStock = 15000,
            DaySaleLitres = 1500,
            ClosingStock = 13550,
            ManualStock = 13550, // Physical stock entered
            DipMm = 1200,
            LastUpdated = DateTime.Now
        };

        context.AddRange(shift, entry, reading, dailyStock);
        await context.SaveChangesAsync();

        var repo = new TankDailyStockRepository(context);
        var result = await repo.GetDetailedHistoryAsync(date, date, "HSD");

        Assert.True(result.Success);
        var dailyRow = result.Data!.DailyRows.FirstOrDefault(r => r.FuelType == "HSD");
        Assert.NotNull(dailyRow);
        Assert.Equal(13550, dailyRow!.DayMeasuredStock);
        Assert.Equal(50, dailyRow.DayVariance); // 13550 - (15000 - 1500) = +50
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 20. Fresh entry leaves ManualStock and Dip null while showing CalculatedStock
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test20_FreshEntry_LeavesManualStockAndDipNull_ShowsCalculatedStock()
    {
        var group = new NozzleGroupDto
        {
            FuelType = "HSD",
            OpeningStock = 10000,
            FuelDispensed = 1500,
            TestingLitres = 5,
            Receipts = 2000
        };

        // Auto Stock = 10000 - 1500 + 5 + 2000 = 10505
        Assert.Equal(10505, group.CalculatedStock);
        Assert.Equal("10,505.00", group.AutomaticStockDisplay);

        // Manual inputs remain strictly null (unmeasured)
        Assert.Null(group.ManualStock);
        Assert.Null(group.Dip);
        Assert.Null(group.PhysicalStock);
        Assert.False(group.HasMeasurement);
        Assert.Equal("—", group.StockVarianceDisplay);
        Assert.Equal("Dip Not Recorded", group.StockVarianceStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 21. Negative calculated stock remains in AutoStock, never populates ManualStock
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test21_NegativeCalculatedStock_RemainsInAutoStock_NeverPopulatesManualStock()
    {
        var group = new NozzleGroupDto
        {
            FuelType = "MS-I",
            OpeningStock = 1000,
            FuelDispensed = 7275.66,
            TestingLitres = 0,
            Receipts = 0
        };

        // Book stock is -6,275.66
        Assert.Equal(-6275.66, group.CalculatedStock);
        Assert.Equal("-6,275.66", group.AutomaticStockDisplay);

        // Crucial requirement: ManualStock is NOT prepopulated with negative book stock!
        Assert.Null(group.ManualStock);
        Assert.Null(group.Dip);
        Assert.Null(group.PhysicalStock);
        Assert.False(group.HasMeasurement);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 22. Dip-only entry persists with DipSource='DipOnly', no physical stock/variance
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test22_DipOnly_PersistsAsDipOnly_DoesNotSetPhysicalStockOrVariance()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = new ServiceCollection()
            .AddDbContext<FuelProDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddScoped<IAgsImportRepository, AgsImportRepository>()
            .AddScoped<IDsmEntryRepository, DsmEntryRepository>()
            .AddScoped<IShiftCycleService, ShiftCycleService>()
            .AddScoped<IAgsDailyAggregationService, AgsDailyAggregationService>()
            .AddScoped<IFuelTankerRepository, FuelTankerRepository>()
            .AddScoped<IShiftRepository, ShiftRepository>()
            .AddScoped<IAgsInventoryService, AgsInventoryService>()
            .BuildServiceProvider();

        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var date = new DateTime(2026, 10, 6);

        var shift = new Shift { ShiftId = 1, ShiftDate = date, ShiftType = "A" };
        var entry = new DsmEntry { DsmEntryId = 1, ShiftId = 1, Shift = shift, PumpId = 1 };
        var reading = new NozzleReading { NozzleReadingId = 1, DsmEntryId = 1, DsmEntry = entry, FuelType = "HSD", SaleLitres = 1000 };
        context.AddRange(shift, entry, reading);
        await context.SaveChangesAsync();

        var group = new NozzleGroupDto
        {
            FuelType = "HSD",
            OpeningStock = 10000,
            FuelDispensed = 1000,
            TestingLitres = 0,
            Receipts = 0,
            Dip = 1150.0 // User entered dip only, no stock
        };

        var inventoryService = scope.ServiceProvider.GetRequiredService<IAgsInventoryService>();
        await inventoryService.SaveAndPersistInventoryAsync(date, "A", new List<NozzleGroupDto> { group });

        var tankStock = await context.AgsTankStocks.FirstOrDefaultAsync(t => t.FuelType == "HSD");
        Assert.NotNull(tankStock);
        Assert.Equal(1150.0, tankStock!.ClosingDipMM);
        Assert.Equal("DipOnly", tankStock.DipSource);
        Assert.True(tankStock.HasPhysicalDip);
        Assert.Equal(9000.0, tankStock.ClosingStockLitres); // Book stock preserved

        // Verify history report shows Dip but displays '—' for physical stock and variance
        var repo = new TankDailyStockRepository(context);
        var history = await repo.GetDetailedHistoryAsync(date, date, "HSD");
        Assert.True(history.Success);
        var shiftRow = history.Data!.ShiftRows.FirstOrDefault();
        Assert.NotNull(shiftRow);
        Assert.Equal(1150.0, shiftRow!.DipMm);
        Assert.Null(shiftRow.MeasuredStock);
        Assert.Null(shiftRow.Variance);
        Assert.Equal("Dip Recorded", shiftRow.VarianceStatus);
        Assert.False(shiftRow.HasPhysicalMeasurement);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 23. Manual stock without dip computes variance and persists correctly
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test23_ManualStockOnly_PersistsAndComputesVariance()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = new ServiceCollection()
            .AddDbContext<FuelProDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddScoped<IAgsImportRepository, AgsImportRepository>()
            .AddScoped<IDsmEntryRepository, DsmEntryRepository>()
            .AddScoped<IShiftCycleService, ShiftCycleService>()
            .AddScoped<IAgsDailyAggregationService, AgsDailyAggregationService>()
            .AddScoped<IFuelTankerRepository, FuelTankerRepository>()
            .AddScoped<IShiftRepository, ShiftRepository>()
            .AddScoped<IAgsInventoryService, AgsInventoryService>()
            .BuildServiceProvider();

        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var date = new DateTime(2026, 10, 6);

        var shift = new Shift { ShiftId = 1, ShiftDate = date, ShiftType = "A" };
        var entry = new DsmEntry { DsmEntryId = 1, ShiftId = 1, Shift = shift, PumpId = 1 };
        var reading = new NozzleReading { NozzleReadingId = 1, DsmEntryId = 1, DsmEntry = entry, FuelType = "HSD", SaleLitres = 1000 };
        context.AddRange(shift, entry, reading);
        await context.SaveChangesAsync();

        var group = new NozzleGroupDto
        {
            FuelType = "HSD",
            OpeningStock = 10000,
            FuelDispensed = 1000,
            ManualStock = 8950.0 // Physical stock 8950, no dip
        };

        Assert.True(group.HasManualStockEntry);
        Assert.True(group.HasMeasurement);
        Assert.Equal(-50.0, group.StockVariance);
        Assert.Equal("-50.00", group.StockVarianceDisplay);

        var inventoryService = scope.ServiceProvider.GetRequiredService<IAgsInventoryService>();
        await inventoryService.SaveAndPersistInventoryAsync(date, "A", new List<NozzleGroupDto> { group });

        var tankStock = await context.AgsTankStocks.FirstOrDefaultAsync(t => t.FuelType == "HSD");
        Assert.NotNull(tankStock);
        Assert.Equal(8950.0, tankStock!.ClosingStockLitres);
        Assert.Equal("ManualStockOnly", tankStock.DipSource);

        var repo = new TankDailyStockRepository(context);
        var history = await repo.GetDetailedHistoryAsync(date, date, "HSD");
        Assert.True(history.Success);
        var shiftRow = history.Data!.ShiftRows.FirstOrDefault();
        Assert.NotNull(shiftRow);
        Assert.Equal(8950.0, shiftRow!.MeasuredStock);
        Assert.Equal(-50.0, shiftRow.Variance);
        Assert.Equal("Shortage", shiftRow.VarianceStatus);
        Assert.True(shiftRow.HasPhysicalMeasurement);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 24. Both Manual Stock and Dip persist independently
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test24_BothManualStockAndDip_PersistsIndependently()
    {
        var dbName = Guid.NewGuid().ToString();
        var sp = new ServiceCollection()
            .AddDbContext<FuelProDbContext>(o => o.UseInMemoryDatabase(dbName))
            .AddScoped<IAgsImportRepository, AgsImportRepository>()
            .AddScoped<IDsmEntryRepository, DsmEntryRepository>()
            .AddScoped<IShiftCycleService, ShiftCycleService>()
            .AddScoped<IAgsDailyAggregationService, AgsDailyAggregationService>()
            .AddScoped<IFuelTankerRepository, FuelTankerRepository>()
            .AddScoped<IShiftRepository, ShiftRepository>()
            .AddScoped<IAgsInventoryService, AgsInventoryService>()
            .BuildServiceProvider();

        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var date = new DateTime(2026, 10, 6);

        var shift = new Shift { ShiftId = 1, ShiftDate = date, ShiftType = "A" };
        var entry = new DsmEntry { DsmEntryId = 1, ShiftId = 1, Shift = shift, PumpId = 1 };
        var reading = new NozzleReading { NozzleReadingId = 1, DsmEntryId = 1, DsmEntry = entry, FuelType = "HSD", SaleLitres = 1000 };
        context.AddRange(shift, entry, reading);
        await context.SaveChangesAsync();

        var group = new NozzleGroupDto
        {
            FuelType = "HSD",
            OpeningStock = 10000,
            FuelDispensed = 1000,
            Dip = 1200.0,
            ManualStock = 9050.0
        };

        Assert.Equal(50.0, group.StockVariance);
        Assert.Equal("+50.00", group.StockVarianceDisplay);

        var inventoryService = scope.ServiceProvider.GetRequiredService<IAgsInventoryService>();
        await inventoryService.SaveAndPersistInventoryAsync(date, "A", new List<NozzleGroupDto> { group });

        var tankStock = await context.AgsTankStocks.FirstOrDefaultAsync(t => t.FuelType == "HSD");
        Assert.NotNull(tankStock);
        Assert.Equal(9050.0, tankStock!.ClosingStockLitres);
        Assert.Equal(1200.0, tankStock.ClosingDipMM);
        Assert.Equal("Manual", tankStock.DipSource);

        var repo = new TankDailyStockRepository(context);
        var history = await repo.GetDetailedHistoryAsync(date, date, "HSD");
        Assert.True(history.Success);
        var shiftRow = history.Data!.ShiftRows.FirstOrDefault();
        Assert.NotNull(shiftRow);
        Assert.Equal(9050.0, shiftRow!.MeasuredStock);
        Assert.Equal(1200.0, shiftRow.DipMm);
        Assert.Equal(50.0, shiftRow.Variance);
        Assert.Equal("Excess", shiftRow.VarianceStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 25. Clearing manual input reverts to unmeasured state
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Test25_ClearingManualInput_RevertsToUnmeasured()
    {
        var group = new NozzleGroupDto
        {
            FuelType = "HSD",
            OpeningStock = 10000,
            FuelDispensed = 1000,
            ManualStock = 9500.0,
            Dip = 1250.0
        };

        Assert.True(group.HasMeasurement);
        Assert.NotNull(group.PhysicalStock);

        // User clears both fields
        group.ManualStock = null;
        group.Dip = null;

        Assert.False(group.HasMeasurement);
        Assert.False(group.HasManualStockEntry);
        Assert.Null(group.PhysicalStock);
        Assert.Equal(0.0, group.StockVariance);
        Assert.Equal("—", group.StockVarianceDisplay);
        Assert.Equal("Dip Not Recorded", group.StockVarianceStatus);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 26. November 1 single Shift A scenario matches Day Total exactly
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Test26_November1Scenario_SingleShiftA_ComputesExactDayBookStockAndVariance()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FuelProDbContext>().UseInMemoryDatabase(dbName).Options;

        using var context = new FuelProDbContext(options);
        var repo = new TankDailyStockRepository(context);
        var nov1 = new DateTime(2026, 11, 1);

        // Shift A on 1 Nov 2026 with exact figures from production
        var importA = new AgsShiftImport
        {
            AgsShiftImportId = 101,
            ImportDate = nov1,
            ShiftType = "A",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new()
                {
                    FuelType = "HSD",
                    OpeningStockLitres = 100010.00,
                    FuelDispensedLitres = 1974.95,
                    ClosingStockLitres = 98030.04, // Physical manual entry entered in Shift A
                    HasPhysicalDip = true,
                    ClosingDipMM = 1200,
                    DipSource = "Manual"
                },
                new()
                {
                    FuelType = "MS-I",
                    OpeningStockLitres = 50001.00,
                    FuelDispensedLitres = 3476.22,
                    ClosingStockLitres = 46520.00, // Physical manual entry entered in Shift A
                    HasPhysicalDip = true,
                    ClosingDipMM = 850,
                    DipSource = "Manual"
                },
                new()
                {
                    FuelType = "SPEED",
                    OpeningStockLitres = 200000.00,
                    FuelDispensedLitres = 265.17,
                    ClosingStockLitres = 199732.82, // Physical manual entry entered in Shift A
                    HasPhysicalDip = true,
                    ClosingDipMM = 1500,
                    DipSource = "Manual"
                }
            }
        };

        context.Add(importA);
        await context.SaveChangesAsync();

        var historyRes = await repo.GetDetailedHistoryAsync(nov1, nov1);
        Assert.True(historyRes.Success);
        Assert.NotNull(historyRes.Data);

        // HSD Daily Summary assertion: 
        // Opening = 100,010.00, Sales = 1,974.95, Auto Book Stock = 98,035.05
        // Physical Stock = 98,030.04, Variance = -5.01
        var hsdDaily = historyRes.Data!.DailyRows.FirstOrDefault(r => r.FuelType == "HSD");
        Assert.NotNull(hsdDaily);
        Assert.Equal(100010.00, hsdDaily!.DayOpeningStock);
        Assert.Equal(1974.95, hsdDaily.DaySales);
        Assert.Equal(98035.05, hsdDaily.DayBookStock);
        Assert.Equal(98030.04, hsdDaily.DayMeasuredStock);
        Assert.Equal(-5.01, hsdDaily.DayVariance);

        // MS-I Daily Summary assertion:
        // Opening = 50,001.00, Sales = 3,476.22, Auto Book Stock = 46,524.78
        // Physical Stock = 46,520.00, Variance = -4.78
        var msIDaily = historyRes.Data.DailyRows.FirstOrDefault(r => r.FuelType == "MS-I");
        Assert.NotNull(msIDaily);
        Assert.Equal(50001.00, msIDaily!.DayOpeningStock);
        Assert.Equal(3476.22, msIDaily.DaySales);
        Assert.Equal(46524.78, msIDaily.DayBookStock);
        Assert.Equal(46520.00, msIDaily.DayMeasuredStock);
        Assert.Equal(-4.78, msIDaily.DayVariance);

        // MS-II (SPEED alias) Daily Summary assertion:
        // Opening = 200,000.00, Sales = 265.17, Auto Book Stock = 199,734.83
        // Physical Stock = 199,732.82, Variance = -2.01
        var msIIDaily = historyRes.Data.DailyRows.FirstOrDefault(r => r.FuelType == "MS-II");
        Assert.NotNull(msIIDaily);
        Assert.Equal(200000.00, msIIDaily!.DayOpeningStock);
        Assert.Equal(265.17, msIIDaily.DaySales);
        Assert.Equal(199734.83, msIIDaily.DayBookStock);
        Assert.Equal(199732.82, msIIDaily.DayMeasuredStock);
        Assert.Equal(-2.01, msIIDaily.DayVariance);
    }
}
