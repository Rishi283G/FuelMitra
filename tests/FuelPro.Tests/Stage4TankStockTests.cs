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

        // Sequence: Prev(B,D) = (D-1, A), Prev(C,D) = (D, B), Prev(A,D) = (D, C)
        var prevB = _cycleService.GetPreviousShift(date, "B");
        Assert.Equal(date.AddDays(-1).Date, prevB.Date.Date);
        Assert.Equal("A", prevB.ShiftType);

        var prevC = _cycleService.GetPreviousShift(date, "C");
        Assert.Equal(date.Date, prevC.Date.Date);
        Assert.Equal("B", prevC.ShiftType);

        var prevA = _cycleService.GetPreviousShift(date, "A");
        Assert.Equal(date.Date, prevA.Date.Date);
        Assert.Equal("C", prevA.ShiftType);

        // Successor: Next(B,D) = (D, C), Next(C,D) = (D, A), Next(A,D) = (D+1, B)
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

        // 1. Shift B import exists
        var importB = new AgsShiftImport
        {
            AgsShiftImportId = 1,
            ImportDate = date,
            ShiftType = "B",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", OpeningStockLitres = 15000, FuelDispensedLitres = 3000, ClosingStockLitres = 12000, HasPhysicalDip = true, ClosingDipMM = 1200 }
            }
        };

        // 2. Shift C AGS import is MISSING! But DSM shift and nozzle readings exist:
        var shiftC = new Shift { ShiftId = 2, ShiftDate = date, ShiftType = "C" };
        var entryC = new DsmEntry { DsmEntryId = 20, ShiftId = 2, Shift = shiftC, PumpId = 1 };
        var nozzleC = new NozzleReading { NozzleReadingId = 201, DsmEntryId = 20, DsmEntry = entryC, FuelType = "HSD", SaleLitres = 2000 };
        var testC = new TestingEntry { TestingId = 202, DsmEntryId = 20, DsmEntry = entryC, FuelType = "HSD", Litres = 50 };

        // 3. Shift A import exists
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

        // Shift B import exists
        var importB = new AgsShiftImport
        {
            AgsShiftImportId = 1,
            ImportDate = date,
            ShiftType = "B",
            IsActive = true,
            TankStocks = new List<AgsTankStock>
            {
                new() { FuelType = "HSD", ClosingStockLitres = 12000 }
            }
        };

        // Shift C is COMPLETELY MISSING: No import AND no DSM entries!
        // Shift A exists
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
}
