using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using Xunit;

namespace FuelPro.Tests;

public class ConnectedPumpsReconciliationTests
{
    private readonly ShiftAggregationService _aggregationService;
    private readonly ReportService _reportService;

    public ConnectedPumpsReconciliationTests()
    {
        PumpConfiguration.ResetToDefaults();
        _aggregationService = new ShiftAggregationService(new ShiftCycleService());
        _reportService = new ReportService(_aggregationService);
    }

    [Fact]
    public void Test1_Shift45_MasterSlaveNonTheft_RugvedAndRaniOwnership()
    {
        // Arrange Shift 45 entities matching the forensic database records:
        // Primary 251: Rugved Bhise, Pump 5, legacy ConnectedPumpId = 2
        var entry251 = new DsmEntry
        {
            DsmEntryId = 251,
            DsmName = "Rugved Bhise",
            PumpId = 5,
            ConnectedPumpId = 2, // Legacy database value
            ReconciledToPumpId = null,
            GrossSales = 113342.74m,
            TotalCollection = 113335.55m,
            Mismatch = -7.19m
        };

        // Primary 254: Rani Gadhave, Pump 1, ConnectedPumpId = 2
        var entry254 = new DsmEntry
        {
            DsmEntryId = 254,
            DsmName = "Rani Gadhave",
            PumpId = 1,
            ConnectedPumpId = 2,
            ReconciledToPumpId = null,
            GrossSales = 177983.82m,
            TotalCollection = 235846.36m,
            Mismatch = 57862.54m // Primary before merge
        };

        // Slave 255: Rani Gadhave, Pump 2, explicitly reconciled to Pump 1
        var entry255 = new DsmEntry
        {
            DsmEntryId = 255,
            DsmName = "Rani Gadhave",
            PumpId = 2,
            ConnectedPumpId = null,
            ReconciledToPumpId = 1,
            GrossSales = 59088.45m,
            TotalCollection = 0m,
            Mismatch = -59088.45m
        };

        // Primary 252: Rahul Shingvi, Pump 3, ConnectedPumpId = 4
        var entry252 = new DsmEntry
        {
            DsmEntryId = 252,
            DsmName = "Rahul Shingvi",
            PumpId = 3,
            ConnectedPumpId = 4,
            ReconciledToPumpId = null,
            GrossSales = 33319.46m,
            TotalCollection = 81432.53m,
            Mismatch = 48113.07m
        };

        // Slave 253: Rahul Shingvi, Pump 4, explicitly reconciled to Pump 3
        var entry253 = new DsmEntry
        {
            DsmEntryId = 253,
            DsmName = "Rahul Shingvi",
            PumpId = 4,
            ConnectedPumpId = null,
            ReconciledToPumpId = 3,
            GrossSales = 48115.41m,
            TotalCollection = 0m,
            Mismatch = -48115.41m
        };

        // Standalone 250: Suvarna Shelke, Pump 6
        var entry250 = new DsmEntry
        {
            DsmEntryId = 250,
            DsmName = "Suvarna Shelke",
            PumpId = 6,
            ConnectedPumpId = null,
            ReconciledToPumpId = null,
            GrossSales = 181763.39m,
            TotalCollection = 181767.83m,
            Mismatch = 4.44m
        };

        var allEntries = new List<DsmEntry> { entry250, entry251, entry252, entry253, entry254, entry255 };

        // Act 1: Build DSM Summary Rows
        var summaryRows = _aggregationService.BuildDsmSummaryRows(allEntries);

        // Assert 1: Exactly 4 attendants/pumps represented
        Assert.Equal(4, summaryRows.Count);

        var rugvedRow = summaryRows.FirstOrDefault(r => r.DsmName == "Rugved Bhise");
        Assert.NotNull(rugvedRow);
        Assert.Equal("5", rugvedRow.PumpNoDisplay); // Must NOT be "5 & 2"
        Assert.Equal("Pump 5", rugvedRow.PumpLabel);
        Assert.Equal(113342.74, rugvedRow.GrossSale, 2);
        Assert.Equal(-7.19, rugvedRow.Difference, 2);

        var raniRow = summaryRows.FirstOrDefault(r => r.DsmName == "Rani Gadhave");
        Assert.NotNull(raniRow);
        Assert.Equal("1 & 2", raniRow.PumpNoDisplay);
        Assert.Equal("Pump 1 & 2", raniRow.PumpLabel);
        Assert.Equal(237072.27, raniRow.GrossSale, 2); // 177983.82 + 59088.45
        Assert.Equal(235846.36, raniRow.TotalCollection, 2);
        Assert.Equal(-1225.91, raniRow.Difference, 2);

        var rahulRow = summaryRows.FirstOrDefault(r => r.DsmName == "Rahul Shingvi");
        Assert.NotNull(rahulRow);
        Assert.Equal("3 & 4", rahulRow.PumpNoDisplay);
        Assert.Equal("Pump 3 & 4", rahulRow.PumpLabel);
        Assert.Equal(81434.87, rahulRow.GrossSale, 2); // 33319.46 + 48115.41
        Assert.Equal(81432.53, rahulRow.TotalCollection, 2);
        Assert.Equal(-2.34, rahulRow.Difference, 2);

        var suvarnaRow = summaryRows.FirstOrDefault(r => r.DsmName == "Suvarna Shelke");
        Assert.NotNull(suvarnaRow);
        Assert.Equal("6", suvarnaRow.PumpNoDisplay);
        Assert.Equal("Pump 6", suvarnaRow.PumpLabel);
        Assert.Equal(181763.39, suvarnaRow.GrossSale, 2);
        Assert.Equal(4.44, suvarnaRow.Difference, 2);

        // Act 2: ReportService.MergeConnectedPumpEntries
        var merged = ReportService.MergeConnectedPumpEntries(allEntries);
        Assert.Equal(4, merged.Count);

        var mergedRugved = merged.FirstOrDefault(m => m.DsmName == "Rugved Bhise");
        Assert.NotNull(mergedRugved);
        Assert.Null(mergedRugved.ConnectedPumpId); // Must not link to Pump 2

        var mergedRani = merged.FirstOrDefault(m => m.DsmName == "Rani Gadhave");
        Assert.NotNull(mergedRani);
        Assert.Equal(2, mergedRani.ConnectedPumpId);
    }

    [Fact]
    public void Test2_Shift45_TotalDsmShort_Equals_1235_44()
    {
        // Arrange Shift 45 entries with their respective sales and collections
        var entry251 = new DsmEntry
        {
            DsmEntryId = 251,
            DsmName = "Rugved Bhise",
            PumpId = 5,
            ConnectedPumpId = 2, // Legacy
            ReconciledToPumpId = null,
            GrossSales = 113342.74m,
            TotalCollection = 113335.55m,
            Mismatch = -7.19m
        };

        var entry254 = new DsmEntry
        {
            DsmEntryId = 254,
            DsmName = "Rani Gadhave",
            PumpId = 1,
            ConnectedPumpId = 2,
            ReconciledToPumpId = null,
            GrossSales = 177983.82m,
            TotalCollection = 235846.36m,
            Mismatch = 57862.54m
        };

        var entry255 = new DsmEntry
        {
            DsmEntryId = 255,
            DsmName = "Rani Gadhave",
            PumpId = 2,
            ConnectedPumpId = null,
            ReconciledToPumpId = 1,
            GrossSales = 59088.45m,
            TotalCollection = 0m,
            Mismatch = -59088.45m
        };

        var entry252 = new DsmEntry
        {
            DsmEntryId = 252,
            DsmName = "Rahul Shingvi",
            PumpId = 3,
            ConnectedPumpId = 4,
            ReconciledToPumpId = null,
            GrossSales = 33319.46m,
            TotalCollection = 81432.53m,
            Mismatch = 48113.07m
        };

        var entry253 = new DsmEntry
        {
            DsmEntryId = 253,
            DsmName = "Rahul Shingvi",
            PumpId = 4,
            ConnectedPumpId = null,
            ReconciledToPumpId = 3,
            GrossSales = 48115.41m,
            TotalCollection = 0m,
            Mismatch = -48115.41m
        };

        var entry250 = new DsmEntry
        {
            DsmEntryId = 250,
            DsmName = "Suvarna Shelke",
            PumpId = 6,
            ConnectedPumpId = null,
            ReconciledToPumpId = null,
            GrossSales = 181763.39m,
            TotalCollection = 181767.83m,
            Mismatch = 4.44m
        };

        var allEntries = new List<DsmEntry> { entry250, entry251, entry252, entry253, entry254, entry255 };

        var shift = new Shift
        {
            ShiftId = 45,
            ShiftDate = new DateTime(2026, 11, 1),
            ShiftType = "A",
            DsmEntries = allEntries
        };
        foreach (var e in allEntries) e.Shift = shift;

        // Act: Run CalculateShiftReport
        var report = _reportService.CalculateShiftReport(
            shift.ShiftDate,
            shift.ShiftType,
            allEntries,
            new List<Expense>(),
            new List<ShiftOtherCash>(),
            new List<CreditorRepayment>(),
            89.62, 103.50, 103.50, 85.00,
            new BusinessDayTidSheet(),
            new BusinessDayTidSheet(),
            "Shree Mahakaleshwar Petroleum"
        );

        // Assert:
        // Sum of shortages: 7.19 (Rugved) + 1225.91 (Rani) + 2.34 (Rahul) = 1235.44
        Assert.Equal(1235.44, report.TotalDsmShort, 2);

        // Net difference across DSM attendants: -1235.44 (shortages) + 4.44 (excess) = -1231.00
        var summaryRows = _aggregationService.BuildDsmSummaryRows(allEntries);
        var summaryTotals = _aggregationService.BuildDsmSummaryTotalRow(summaryRows);
        Assert.Equal(-1231.00, summaryTotals.Difference, 2);
    }

    [Fact]
    public void Test3_HistoricalShifts_Shift41_And_Shift37_OwnershipPreserved()
    {
        // Primary 101: Attendant X, Pump 5, legacy ConnectedPumpId = 2
        var entry101 = new DsmEntry
        {
            DsmEntryId = 101,
            DsmName = "Attendant X",
            PumpId = 5,
            ConnectedPumpId = 2,
            ReconciledToPumpId = null,
            GrossSales = 100000m,
            TotalCollection = 100000m
        };

        // Primary 102: Attendant Y, Pump 1, ConnectedPumpId = 2
        var entry102 = new DsmEntry
        {
            DsmEntryId = 102,
            DsmName = "Attendant Y",
            PumpId = 1,
            ConnectedPumpId = 2,
            ReconciledToPumpId = null,
            GrossSales = 150000m,
            TotalCollection = 200000m
        };

        // Slave 103: Attendant Y, Pump 2, reconciled to Pump 1
        var entry103 = new DsmEntry
        {
            DsmEntryId = 103,
            DsmName = "Attendant Y",
            PumpId = 2,
            ConnectedPumpId = null,
            ReconciledToPumpId = 1,
            GrossSales = 50000m,
            TotalCollection = 0m
        };

        var entries = new List<DsmEntry> { entry101, entry102, entry103 };

        var rows = _aggregationService.BuildDsmSummaryRows(entries);

        Assert.Equal(2, rows.Count);
        var rowX = rows.FirstOrDefault(r => r.DsmName == "Attendant X");
        Assert.NotNull(rowX);
        Assert.Equal("5", rowX.PumpNoDisplay);
        Assert.Equal("Pump 5", rowX.PumpLabel);

        var rowY = rows.FirstOrDefault(r => r.DsmName == "Attendant Y");
        Assert.NotNull(rowY);
        Assert.Equal("1 & 2", rowY.PumpNoDisplay);
        Assert.Equal("Pump 1 & 2", rowY.PumpLabel);
        Assert.Equal(200000, rowY.GrossSale);
    }

    [Fact]
    public void Test4_ExplicitlyLinkedSlave_CannotBeStolenByEarlierPrimary()
    {
        // Primary A (earlier ID 1, pump 10, wants pump 20)
        var primaryA = new DsmEntry
        {
            DsmEntryId = 1,
            PumpId = 10,
            ConnectedPumpId = 20,
            DsmName = "Operator A",
            GrossSales = 10000m
        };

        // Primary B (later ID 2, pump 11, wants pump 20)
        var primaryB = new DsmEntry
        {
            DsmEntryId = 2,
            PumpId = 11,
            ConnectedPumpId = 20,
            DsmName = "Operator B",
            GrossSales = 20000m
        };

        // Slave C (pump 20, explicitly linked to pump 11)
        var slaveC = new DsmEntry
        {
            DsmEntryId = 3,
            PumpId = 20,
            ReconciledToPumpId = 11,
            DsmName = "Operator B",
            GrossSales = 5000m
        };

        var entries = new List<DsmEntry> { primaryA, primaryB, slaveC };

        var merged = ReportService.MergeConnectedPumpEntries(entries);

        Assert.Equal(2, merged.Count);

        var mergedA = merged.First(m => m.DsmEntryId == 1);
        Assert.Null(mergedA.ConnectedPumpId);
        Assert.Equal(10000m, mergedA.GrossSales);

        var mergedB = merged.First(m => m.DsmEntryId == 2);
        Assert.Equal(20, mergedB.ConnectedPumpId);
        Assert.Equal(25000m, mergedB.GrossSales);
    }

    [Fact]
    public void Test5_LegacyUnlinkedSlave_MatchesByAttendantAndConnectedPump()
    {
        // When ReconciledToPumpId is null (legacy data):
        var primaryAlice = new DsmEntry
        {
            DsmEntryId = 10,
            PumpId = 1,
            ConnectedPumpId = 2,
            DsmName = "Alice",
            GrossSales = 10000m
        };

        var primaryBob = new DsmEntry
        {
            DsmEntryId = 20,
            PumpId = 3,
            ConnectedPumpId = 2,
            DsmName = "Bob",
            GrossSales = 15000m
        };

        var slaveBob = new DsmEntry
        {
            DsmEntryId = 30,
            PumpId = 2,
            ReconciledToPumpId = 3, // legacy unlinked from entry ID, references PumpId 3
            DsmName = "Bob",
            GrossSales = 8000m
        };

        var entries = new List<DsmEntry> { primaryAlice, primaryBob, slaveBob };

        var merged = ReportService.MergeConnectedPumpEntries(entries);

        Assert.Equal(2, merged.Count);

        var aliceMerged = merged.First(m => m.DsmEntryId == 10);
        Assert.Null(aliceMerged.ConnectedPumpId);
        Assert.Equal(10000m, aliceMerged.GrossSales);

        var bobMerged = merged.First(m => m.DsmEntryId == 20);
        Assert.Equal(2, bobMerged.ConnectedPumpId);
        Assert.Equal(23000m, bobMerged.GrossSales);
    }

    [Fact]
    public void Test6_PhysicalNozzleDeduplication_UsesPumpIdAndNozzleNumber()
    {
        var primary = new DsmEntry
        {
            DsmEntryId = 1,
            PumpId = 1,
            ConnectedPumpId = 2,
            DsmName = "Test Attendant",
            NozzleReadings = new List<NozzleReading>
            {
                new NozzleReading { NozzleReadingId = 1, DsmEntryId = 1, NozzleNumber = 1, FuelType = "MS", SaleLitres = 100, Rate = 100, Amount = 10000 },
                new NozzleReading { NozzleReadingId = 2, DsmEntryId = 1, NozzleNumber = 2, FuelType = "HSD", SaleLitres = 50, Rate = 90, Amount = 4500 }
            }
        };

        var slave = new DsmEntry
        {
            DsmEntryId = 2,
            PumpId = 2,
            ReconciledToPumpId = 1,
            DsmName = "Test Attendant",
            NozzleReadings = new List<NozzleReading>
            {
                new NozzleReading { NozzleReadingId = 3, DsmEntryId = 2, NozzleNumber = 1, FuelType = "MS", SaleLitres = 80, Rate = 100, Amount = 8000 },
                new NozzleReading { NozzleReadingId = 4, DsmEntryId = 2, NozzleNumber = 2, FuelType = "HSD", SaleLitres = 40, Rate = 90, Amount = 3600 },
                // Duplicate reading on Pump 2, Nozzle 1
                new NozzleReading { NozzleReadingId = 5, DsmEntryId = 2, NozzleNumber = 1, FuelType = "MS", SaleLitres = 80, Rate = 100, Amount = 8000 }
            }
        };

        var mergedList = ReportService.MergeConnectedPumpEntries(new List<DsmEntry> { primary, slave });
        Assert.Single(mergedList);

        var merged = mergedList[0];
        // Must contain exactly 4 distinct physical nozzles: (Pump 1 Nozzle 1 & 2) and (Pump 2 Nozzle 1 & 2)
        Assert.Equal(4, merged.NozzleReadings.Count);

        // Verify that Nozzle 1 from primary (ID 1) and Nozzle 1 from slave (ID 3) are both present
        Assert.Contains(merged.NozzleReadings, n => n.NozzleReadingId == 1);
        Assert.Contains(merged.NozzleReadings, n => n.NozzleReadingId == 3);
        // Duplicate nozzle reading (ID 5) must be deduplicated
        Assert.DoesNotContain(merged.NozzleReadings, n => n.NozzleReadingId == 5);
    }

    [Fact]
    public void Test7_PrintTemplates_Contain_TotalDsmShort_Row()
    {
        string baseDir = AppContext.BaseDirectory;
        // Search upward to find src/FuelPro.UI/Printing
        string current = baseDir;
        string? printDir = null;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "src", "FuelPro.UI", "Printing");
            if (Directory.Exists(candidate))
            {
                printDir = candidate;
                break;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        Assert.NotNull(printDir);

        string finalCalcPath = Path.Combine(printDir!, "FinalCalculationPrintTemplate.html");
        string dayTotalPath = Path.Combine(printDir!, "DayTotalPrintTemplate.html");

        Assert.True(File.Exists(finalCalcPath), "FinalCalculationPrintTemplate.html exists");
        Assert.True(File.Exists(dayTotalPath), "DayTotalPrintTemplate.html exists");

        string finalCalcHtml = File.ReadAllText(finalCalcPath);
        string dayTotalHtml = File.ReadAllText(dayTotalPath);

        // Assert Total DSM Short row in FinalCalculationPrintTemplate.html
        Assert.Contains("id=\"trDsmShort\"", finalCalcHtml);
        Assert.Contains("Total DSM Short", finalCalcHtml);
        Assert.Contains("#c62828", finalCalcHtml);

        // Assert pure meter gross header in FinalCalculationPrintTemplate.html
        Assert.Contains("totalFuelAmount", finalCalcHtml);

        // Assert Total DSM Short row in DayTotalPrintTemplate.html
        Assert.Contains("id=\"trDsmShort\"", dayTotalHtml);
        Assert.Contains("Total DSM Short", dayTotalHtml);
        Assert.Contains("#c62828", dayTotalHtml);
    }

    [Fact]
    public void Test8_WpfViews_Contain_TotalDsmShort_Binding_And_Converter()
    {
        string baseDir = AppContext.BaseDirectory;
        string current = baseDir;
        string? viewsDir = null;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "src", "FuelPro.UI", "Views");
            if (Directory.Exists(candidate))
            {
                viewsDir = candidate;
                break;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }

        Assert.NotNull(viewsDir);

        string finalCalcViewPath = Path.Combine(viewsDir!, "FinalCalculationView.xaml");
        string dayTotalViewPath = Path.Combine(viewsDir!, "DayTotalView.xaml");

        Assert.True(File.Exists(finalCalcViewPath), "FinalCalculationView.xaml exists");
        Assert.True(File.Exists(dayTotalViewPath), "DayTotalView.xaml exists");

        string finalCalcXaml = File.ReadAllText(finalCalcViewPath);
        string dayTotalXaml = File.ReadAllText(dayTotalViewPath);

        // Assert PositiveToVisibilityConverter defined and bound to TotalDsmShort
        Assert.Contains("PositiveToVisibilityConverter", finalCalcXaml);
        Assert.Contains("TotalDsmShort", finalCalcXaml);
        Assert.Contains("Total DSM Short", finalCalcXaml);

        Assert.Contains("PositiveToVisibilityConverter", dayTotalXaml);
        Assert.Contains("TotalDsmShort", dayTotalXaml);
        Assert.Contains("Total DSM Short", dayTotalXaml);
    }

    [Fact]
    public void Test9_DayTotal_Aggregates_TotalDsmShort_WithoutDoubleCounting()
    {
        var shiftADate = new DateTime(2026, 11, 1);
        var entryA = new DsmEntry
        {
            DsmEntryId = 1,
            PumpId = 1,
            GrossSales = 10000m,
            TotalCollection = 9500m, // Shortage of 500
            Mismatch = -500m
        };

        var entryB = new DsmEntry
        {
            DsmEntryId = 2,
            PumpId = 2,
            GrossSales = 20000m,
            TotalCollection = 19700m, // Shortage of 300
            Mismatch = -300m
        };

        var entryC = new DsmEntry
        {
            DsmEntryId = 3,
            PumpId = 3,
            GrossSales = 15000m,
            TotalCollection = 15200m, // Excess of 200
            Mismatch = 200m
        };

        var allEntries = new List<DsmEntry> { entryA, entryB, entryC };

        var dayReport = _reportService.CalculateDayReport(
            shiftADate,
            shiftADate,
            allEntries,
            new List<Expense>(),
            new List<CreditorRepayment>(),
            89.62, 103.50, 103.50, 85.00,
            "Shree Mahakaleshwar Petroleum"
        );

        // Shortages sum: 500 + 300 = 800. The excess of 200 is not subtracted from TotalDsmShort.
        Assert.Equal(800.0, dayReport.TotalDsmShort, 2);
    }

    [Fact]
    public void Test10_MultiDsmConnectedPumps_NozzleDeduplication_PerDsmAndOverallShiftTotals()
    {
        var shiftDate = new DateTime(2026, 11, 20);
        var shift = new Shift { ShiftId = 90, ShiftDate = shiftDate, ShiftType = "B" };

        // DSM 1 (Rani): Pump 1 (primary, ConnectedPumpId = 2) + Pump 2 (slave with duplicate nozzle reading on Nozzle 1)
        var raniPrimary = new DsmEntry
        {
            DsmEntryId = 501,
            ShiftId = shift.ShiftId,
            Shift = shift,
            DsmName = "Rani",
            PumpId = 1,
            ConnectedPumpId = 2,
            GrossSales = 10000m,
            TotalCollection = 18000m,
            PaymentCollection = new PaymentCollection { PhonePeDay = 18000 },
            NozzleReadings = new List<NozzleReading>
            {
                new() { NozzleReadingId = 1, DsmEntryId = 501, NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 100, Rate = 100, Amount = 10000 }
            }
        };
        var raniSlave = new DsmEntry
        {
            DsmEntryId = 502,
            ShiftId = shift.ShiftId,
            Shift = shift,
            DsmName = "Rani",
            PumpId = 2,
            ReconciledToPumpId = 1,
            GrossSales = 8000m,
            TotalCollection = 0m,
            NozzleReadings = new List<NozzleReading>
            {
                new() { NozzleReadingId = 2, DsmEntryId = 502, NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 80, Rate = 100, Amount = 8000 },
                new() { NozzleReadingId = 3, DsmEntryId = 502, NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 80, Rate = 100, Amount = 8000 } // duplicate physical nozzle on Pump 2
            }
        };

        // DSM 2 (Rahul): Pump 3 (primary, ConnectedPumpId = 4) + Pump 4 (slave whose ReconciledToPumpId == 501 collides numerically with raniPrimary.DsmEntryId)
        var rahulPrimary = new DsmEntry
        {
            DsmEntryId = 503,
            ShiftId = shift.ShiftId,
            Shift = shift,
            DsmName = "Rahul",
            PumpId = 3,
            ConnectedPumpId = 4,
            GrossSales = 5000m,
            TotalCollection = 9000m,
            PaymentCollection = new PaymentCollection { PhonePeDay = 9000 },
            NozzleReadings = new List<NozzleReading>
            {
                new() { NozzleReadingId = 4, DsmEntryId = 503, NozzleNumber = 5, FuelType = "HSD", SaleLitres = 50, Rate = 100, Amount = 5000 }
            }
        };
        var rahulSlave = new DsmEntry
        {
            DsmEntryId = 504,
            ShiftId = shift.ShiftId,
            Shift = shift,
            DsmName = "Rahul",
            PumpId = 4,
            ReconciledToPumpId = 501, // numeric collision with Rani's DsmEntryId, MUST NOT cross-assign!
            GrossSales = 4000m,
            TotalCollection = 0m,
            NozzleReadings = new List<NozzleReading>
            {
                new() { NozzleReadingId = 5, DsmEntryId = 504, NozzleNumber = 6, FuelType = "HSD", SaleLitres = 40, Rate = 100, Amount = 4000 }
            }
        };

        var allEntries = new List<DsmEntry> { raniPrimary, raniSlave, rahulPrimary, rahulSlave };
        var summaryRows = _aggregationService.BuildDsmSummaryRows(allEntries);
        var totals = _aggregationService.BuildDsmSummaryTotalRow(summaryRows);

        Assert.Equal(2, summaryRows.Count);
        var raniRow = summaryRows.First(r => r.DsmName == "Rani");
        var rahulRow = summaryRows.First(r => r.DsmName == "Rahul");

        Assert.Equal("1 & 2", raniRow.PumpNoDisplay);
        Assert.Equal(18000.0, raniRow.GrossSale, 2); // Deduplicates duplicate (Pump 2, Nozzle 1) reading
        Assert.Equal(0.0, raniRow.Difference, 2);

        Assert.Equal("3 & 4", rahulRow.PumpNoDisplay);
        Assert.Equal(9000.0, rahulRow.GrossSale, 2);
        Assert.Equal(0.0, rahulRow.Difference, 2);

        Assert.Equal(27000.0, totals.GrossSale, 2);
        Assert.Equal(27000.0, totals.TotalCollection, 2);

        var report = _reportService.CalculateShiftReport(
            shiftDate, "B", allEntries,
            new List<Expense>(), new List<ShiftOtherCash>(), new List<CreditorRepayment>(),
            100, 100, 100, 85, new BusinessDayTidSheet(), new BusinessDayTidSheet(), "Mitali Service Station");
        Assert.Equal(27000.0, report.TotalFuelAmount, 2);
    }
}
