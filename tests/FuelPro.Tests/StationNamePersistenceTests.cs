using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.DTOs;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;

namespace FuelPro.Tests;

public class StationNamePersistenceTests
{
    private DbContextOptions<FuelProDbContext> CreateInMemoryOptions(string dbName)
    {
        return new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
    }

    // =========================================================================
    // TEST 1 — New installation: Empty Settings table -> SeedData -> "Mitali Service Station"
    // =========================================================================
    [Fact]
    public async Task Test1_NewInstallation_SeedsDefaultMitaliServiceStation()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);

        using (var context = new FuelProDbContext(options))
        {
            // Verify empty
            var existing = await context.Settings.FirstOrDefaultAsync();
            Assert.Null(existing);

            // Initialize SeedData
            await SeedData.InitializeAsync(context);

            // Verify settings created with canonical default
            var settings = await context.Settings.FirstOrDefaultAsync();
            Assert.NotNull(settings);
            Assert.Equal("Mitali Service Station", settings.PumpStationName);
            Assert.Equal("Mitali Service Station", settings.StationDisplayName);
        }
    }

    // =========================================================================
    // TEST 2 — Existing custom name: "Mitali Fuel Station" -> SeedData -> MUST remain
    // =========================================================================
    [Fact]
    public async Task Test2_ExistingCustomName_SeedDataPreservesCustomName()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);

        using (var context = new FuelProDbContext(options))
        {
            var customSetting = new Setting
            {
                HsdRate = 90.35,
                MsIRate = 103.81,
                MsIIRate = 103.81,
                CngRate = 85.0,
                PumpStationName = "Mitali Fuel Station",
                LastUpdated = DateTime.Now
            };
            context.Settings.Add(customSetting);
            await context.SaveChangesAsync();
        }

        using (var context = new FuelProDbContext(options))
        {
            // Run startup seeder on existing database
            await SeedData.InitializeAsync(context);

            var settings = await context.Settings.FirstOrDefaultAsync();
            Assert.NotNull(settings);
            // Must NOT be overwritten to default or Kandhare Petroleum
            Assert.Equal("Mitali Fuel Station", settings.PumpStationName);
            Assert.Equal("Mitali Fuel Station", settings.StationDisplayName);
        }
    }

    // =========================================================================
    // TEST 3 — Restart simulation: Save "Mitali Service Station", fresh DbContext, startup init
    // =========================================================================
    [Fact]
    public async Task Test3_RestartSimulation_MitaliServiceStationPersistsAcrossRestart()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);

        // App Run 1: user saves "Mitali Service Station"
        using (var context = new FuelProDbContext(options))
        {
            var repo = new SettingsRepository(context);
            var setting = new Setting
            {
                PumpStationName = "Mitali Service Station",
                LastUpdated = DateTime.Now
            };
            await repo.SaveSettingsAsync(setting);
        }

        // App Run 2: Fresh startup lifecycle (new DbContext, InitializeAsync, read settings)
        using (var freshContext = new FuelProDbContext(options))
        {
            await SeedData.InitializeAsync(freshContext);

            var repo = new SettingsRepository(freshContext);
            var result = await repo.GetSettingsAsync();

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal("Mitali Service Station", result.Data.PumpStationName);
            Assert.Equal("Mitali Service Station", result.Data.StationDisplayName);
        }
    }

    // =========================================================================
    // TEST 4 — Custom name survives restart: Save "My Custom Petrol Pump", restart simulation
    // =========================================================================
    [Fact]
    public async Task Test4_CustomNameSurvivesRestart_ArbitraryCustomNameNeverOverwritten()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);

        // Session 1: User enters custom station name
        using (var context = new FuelProDbContext(options))
        {
            var repo = new SettingsRepository(context);
            var setting = new Setting
            {
                PumpStationName = "My Custom Petrol Pump",
                LastUpdated = DateTime.Now
            };
            await repo.SaveSettingsAsync(setting);
        }

        // Session 2: Application completely closed and restarted
        using (var freshContext = new FuelProDbContext(options))
        {
            // Startup seeder executes
            await SeedData.InitializeAsync(freshContext);

            var repo = new SettingsRepository(freshContext);
            var result = await repo.GetSettingsAsync();

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal("My Custom Petrol Pump", result.Data.PumpStationName);
            Assert.Equal("My Custom Petrol Pump", result.Data.StationDisplayName);
        }
    }

    // =========================================================================
    // TEST 5 — UI / Single Source of Truth: SettingsRepository -> Consumers
    // =========================================================================
    [Fact]
    public async Task Test5_UI_SingleSourceOfTruth_ReadsDirectlyFromRepository()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = CreateInMemoryOptions(dbName);

        // Initial setup
        using (var setupContext = new FuelProDbContext(options))
        {
            var repo = new SettingsRepository(setupContext);
            await repo.SaveSettingsAsync(new Setting { PumpStationName = "Mitali Service Station" });
        }

        // Verify initial read and update
        using (var readContext = new FuelProDbContext(options))
        {
            var repo = new SettingsRepository(readContext);
            var initial = await repo.GetSettingsAsync();
            Assert.Equal("Mitali Service Station", initial.Data!.StationDisplayName);

            // Update to new name
            var setting = initial.Data;
            setting.PumpStationName = "Mitali Fuel Station";
            var saveResult = await repo.SaveSettingsAsync(setting);
            Assert.True(saveResult.Success);
        }

        // Verify downstream repository load
        using (var reloadContext = new FuelProDbContext(options))
        {
            var repo = new SettingsRepository(reloadContext);
            var reloaded = await repo.GetSettingsAsync();
            Assert.Equal("Mitali Fuel Station", reloaded.Data!.StationDisplayName);
            Assert.Equal("Mitali Fuel Station", reloaded.Data!.PumpStationName);
        }
    }

    // =========================================================================
    // TEST 6 — Printing: Representative print templates receive persisted station name
    // =========================================================================
    [Fact]
    public void Test6_Printing_RepresentativePrintDataReceivesPersistedStationName()
    {
        string stationName = "Mitali Service Station";

        // Shift Summary
        var shiftSummary = new ShiftSummaryPrintData
        {
            StationName = stationName,
            Date = "21/09/2026",
            Shift = "A"
        };
        Assert.Equal("Mitali Service Station", shiftSummary.StationName);

        // DSM Sheet
        var dsmSheet = new DsmSheetPrintData
        {
            StationName = stationName,
            CompanyName = stationName,
            Date = "21/09/2026",
            Shift = "A"
        };
        Assert.Equal("Mitali Service Station", dsmSheet.StationName);
        Assert.Equal("Mitali Service Station", dsmSheet.CompanyName);

        // Default DTO fallbacks if not populated
        var defaultSummary = new ShiftSummaryPrintData();
        Assert.Equal("Mitali Service Station", defaultSummary.StationName);

        var defaultDsm = new DsmSheetPrintData();
        Assert.Equal("Mitali Service Station", defaultDsm.CompanyName);
    }

    // =========================================================================
    // TEST 7 — Excel: ExcelExportService uses persisted station name
    // =========================================================================
    [Fact]
    public async Task Test7_Excel_ShiftAndDayTotalExports_IncludePersistedStationName()
    {
        var exportService = new ExcelExportService();

        var shiftReport = new ShiftReportDto
        {
            StationName = "Mitali Service Station",
            Date = DateTime.Today,
            ShiftLabel = "A",
            DateString = DateTime.Today.ToString("dd-MMM-yyyy")
        };

        var tempPath = Path.Combine(Path.GetTempPath(), $"ShiftTotal_{Guid.NewGuid():N}.xlsx");
        try
        {
            var filePath = await exportService.ExportShiftTotalReportAsync(shiftReport, tempPath);
            Assert.True(File.Exists(filePath));
            using var workbook = new ClosedXML.Excel.XLWorkbook(filePath);
            var ws = workbook.Worksheets.First();
            var titleCell = ws.Cell(1, 1).GetString();
            Assert.Equal("Mitali Service Station", titleCell);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    // =========================================================================
    // TEST 8 — Existing DSR regression: DSR Actual Sales isolated, Financials gross
    // =========================================================================
    [Fact]
    public void Test8_DsrIsolatedRepresentation_DSRActualSalesOnly_CanonicalReconciliationGross()
    {
        var aggregation = new ShiftAggregationService();
        var reportService = new ReportService(aggregation);

        var date = new DateTime(2026, 7, 11);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                PumpId = 1,
                GrossSales = 10000m,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 100.0, Amount = 10000.0, Rate = 100.0 }
                },
                PaymentCollection = new PaymentCollection { CashDeposit = 9500 },
                TestingEntries = new List<TestingEntry>
                {
                    new TestingEntry { FuelType = "MS-I", Litres = 5.0, Rate = 100.0, Amount = 500.0 }
                }
            }
        };

        var report = reportService.CalculateShiftReport(
            date: date,
            shiftType: "A",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: new List<CreditorRepayment>(),
            hsdRate: 90.0,
            msIRate: 100.0,
            msIIRate: 100.0,
            cngRate: 85.0,
            todayTid: new BusinessDayTidSheet(),
            tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // 1. Station Name is preserved
        Assert.Equal("Mitali Service Station", report.StationName);

        // 2. Canonical FuelSales = Gross Meter (100 L, ₹10,000)
        Assert.Equal(100.0, report.TotalFuelLitres, precision: 2);
        Assert.Equal(10000.0, report.TotalFuelAmount, precision: 2);

        // 3. DSR FuelSales = Actual Customer Sales (95 L, ₹9,500)
        Assert.NotNull(report.DsrFuelSales);
        Assert.Equal(95.0, report.DsrTotalFuelLitres, precision: 2);
        Assert.Equal(9500.0, report.DsrTotalFuelAmount, precision: 2);

        var dsrMs = report.DsrFuelSales.FirstOrDefault(f => f.Description.Contains("MS") || f.FuelType == "MS-I");
        Assert.NotNull(dsrMs);
        Assert.Equal(95.0, dsrMs.Litres, precision: 2);
        Assert.Equal(9500.0, dsrMs.Amount, precision: 2);

        // 4. Expected Collection includes Gross Sales (10000)
        Assert.Equal(10000.0, report.ExpectedCollection, precision: 2);

        // 5. Actual Collection = Cash (9500) + Testing (500) = 10000
        Assert.Equal(10000.0, report.ActualCollection, precision: 2);

        // 6. Balanced Difference = 0
        Assert.Equal(0.0, report.Difference, precision: 2);
        Assert.True(report.IsBalanced);
    }
}
