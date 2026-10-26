using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.UI.ViewModels;
using FuelPro.UI.Views;
using Xunit;

namespace FuelPro.Tests;

public class DsmCumulativeDsrAndApprovalUiTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FuelProDbContext> _options;

    public DsmCumulativeDsrAndApprovalUiTests()
    {
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
    public void DsmEntryViewModel_RecalculateAll_Computes_FuelTypeSummaries_Accurately()
    {
        // Arrange
        var vm = new DsmEntryViewModel();
        vm.NozzleReadings.Clear();

        // 2 nozzles of MS: 100L @ 105.50 and 50L @ 105.50 = 150L, ₹15,825.00
        vm.NozzleReadings.Add(new NozzleReadingRow { FuelType = "MS", OpeningReading = 1000, ClosingReading = 1100, Rate = 105.50, OnRowChanged = vm.RecalculateAll });
        vm.NozzleReadings.Add(new NozzleReadingRow { FuelType = "MS", OpeningReading = 2000, ClosingReading = 2050, Rate = 105.50, OnRowChanged = vm.RecalculateAll });

        // 3 nozzles of HSD: 200L, 300L, 100L @ 92.00 = 600L, ₹55,200.00
        vm.NozzleReadings.Add(new NozzleReadingRow { FuelType = "HSD", OpeningReading = 5000, ClosingReading = 5200, Rate = 92.00, OnRowChanged = vm.RecalculateAll });
        vm.NozzleReadings.Add(new NozzleReadingRow { FuelType = "HSD", OpeningReading = 6000, ClosingReading = 6300, Rate = 92.00, OnRowChanged = vm.RecalculateAll });
        vm.NozzleReadings.Add(new NozzleReadingRow { FuelType = "HSD", OpeningReading = 7000, ClosingReading = 7100, Rate = 92.00, OnRowChanged = vm.RecalculateAll });

        // 4 nozzles of SPEED: 50L, 50L, 50L, 50L @ 110.00 = 200L, ₹22,000.00
        vm.NozzleReadings.Add(new NozzleReadingRow { FuelType = "SPEED", OpeningReading = 100, ClosingReading = 150, Rate = 110.00, OnRowChanged = vm.RecalculateAll });
        vm.NozzleReadings.Add(new NozzleReadingRow { FuelType = "SPEED", OpeningReading = 200, ClosingReading = 250, Rate = 110.00, OnRowChanged = vm.RecalculateAll });
        vm.NozzleReadings.Add(new NozzleReadingRow { FuelType = "SPEED", OpeningReading = 300, ClosingReading = 350, Rate = 110.00, OnRowChanged = vm.RecalculateAll });
        vm.NozzleReadings.Add(new NozzleReadingRow { FuelType = "SPEED", OpeningReading = 400, ClosingReading = 450, Rate = 110.00, OnRowChanged = vm.RecalculateAll });

        // Act
        vm.RecalculateAll();

        // Assert
        Assert.Equal(3, vm.FuelTypeSummaries.Count);

        var hsd = vm.FuelTypeSummaries.FirstOrDefault(s => s.FuelType == "HSD");
        Assert.NotNull(hsd);
        Assert.Equal(3, hsd.NozzleCount);
        Assert.Equal(600.0, hsd.TotalLitres, precision: 2);
        Assert.Equal(92.00, hsd.Rate, precision: 2);
        Assert.Equal(55200.00, hsd.TotalAmount, precision: 2);

        var ms = vm.FuelTypeSummaries.FirstOrDefault(s => s.FuelType == "MS");
        Assert.NotNull(ms);
        Assert.Equal(2, ms.NozzleCount);
        Assert.Equal(150.0, ms.TotalLitres, precision: 2);
        Assert.Equal(105.50, ms.Rate, precision: 2);
        Assert.Equal(15825.00, ms.TotalAmount, precision: 2);

        var speed = vm.FuelTypeSummaries.FirstOrDefault(s => s.FuelType == "SPEED");
        Assert.NotNull(speed);
        Assert.Equal(4, speed.NozzleCount);
        Assert.Equal(200.0, speed.TotalLitres, precision: 2);
        Assert.Equal(110.00, speed.Rate, precision: 2);
        Assert.Equal(22000.00, speed.TotalAmount, precision: 2);

        // Overall
        Assert.Equal(950.0, vm.TotalLitres, precision: 2);
        Assert.Equal(93025.0, (double)vm.GrossSales, precision: 2);
    }

    [Fact]
    public async Task DsmEntryService_GetShiftFuelTypeSummariesAsync_Calculates_Cumulative_Totals_Accurately()
    {
        // Arrange
        using var context = CreateContext();
        var shift = new Shift
        {
            ShiftDate = DateTime.Today,
            ShiftType = "A"
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        // Entry 1: Pump 1 with MS & HSD
        var entry1 = new DsmEntry
        {
            ShiftId = shift.ShiftId,
            DsmName = "Rahul",
            PumpId = 1,
            GrossSales = 20000m,
            NozzleReadings = new List<NozzleReading>
            {
                new() { NozzleNumber = 1, FuelType = "MS", OpeningReading = 100, ClosingReading = 200, SaleLitres = 100, Rate = 100, Amount = 10000 },
                new() { NozzleNumber = 2, FuelType = "HSD", OpeningReading = 500, ClosingReading = 600, SaleLitres = 100, Rate = 90, Amount = 9000 }
            }
        };

        // Entry 2: Pump 2 with MS, HSD, SPEED
        var entry2 = new DsmEntry
        {
            ShiftId = shift.ShiftId,
            DsmName = "Kartik",
            PumpId = 2,
            GrossSales = 35000m,
            NozzleReadings = new List<NozzleReading>
            {
                new() { NozzleNumber = 1, FuelType = "MS", OpeningReading = 300, ClosingReading = 450, SaleLitres = 150, Rate = 100, Amount = 15000 },
                new() { NozzleNumber = 2, FuelType = "HSD", OpeningReading = 800, ClosingReading = 950, SaleLitres = 150, Rate = 90, Amount = 13500 },
                new() { NozzleNumber = 3, FuelType = "SPEED", OpeningReading = 50, ClosingReading = 100, SaleLitres = 50, Rate = 110, Amount = 5500 }
            }
        };

        context.DsmEntries.AddRange(entry1, entry2);
        await context.SaveChangesAsync();

        var dsmRepo = new DsmEntryRepository(context);
        var service = new DsmEntryService(
            dsmRepo,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);

        // Act
        var result = await service.GetShiftFuelTypeSummariesAsync(shift.ShiftId);

        // Assert
        Assert.True(result.Success);
        var list = result.Data;
        Assert.NotNull(list);
        Assert.Equal(3, list.Count);

        var hsd = list.FirstOrDefault(s => s.FuelType == "HSD");
        Assert.NotNull(hsd);
        Assert.Equal(2, hsd.NozzleCount);
        Assert.Equal(250.0, hsd.TotalLitres, precision: 2);
        Assert.Equal(90.0, hsd.Rate, precision: 2);
        Assert.Equal(22500.0, hsd.TotalAmount, precision: 2);

        var ms = list.FirstOrDefault(s => s.FuelType == "MS");
        Assert.NotNull(ms);
        Assert.Equal(2, ms.NozzleCount);
        Assert.Equal(250.0, ms.TotalLitres, precision: 2);
        Assert.Equal(100.0, ms.Rate, precision: 2);
        Assert.Equal(25000.0, ms.TotalAmount, precision: 2);

        var speed = list.FirstOrDefault(s => s.FuelType == "SPEED");
        Assert.NotNull(speed);
        Assert.Equal(1, speed.NozzleCount);
        Assert.Equal(50.0, speed.TotalLitres, precision: 2);
        Assert.Equal(110.0, speed.Rate, precision: 2);
        Assert.Equal(5500.0, speed.TotalAmount, precision: 2);
    }

    [Fact]
    public void FuelTypeReadingSummary_BadgeColors_AreCorrect_ForStandardFuelTypes()
    {
        var msSummary = new FuelTypeReadingSummary { FuelType = "MS" };
        Assert.Equal("#E3F2FD", msSummary.BadgeBackground);
        Assert.Equal("#0D47A1", msSummary.BadgeForeground);

        var hsdSummary = new FuelTypeReadingSummary { FuelType = "HSD" };
        Assert.Equal("#FFF3E0", hsdSummary.BadgeBackground);
        Assert.Equal("#E65100", hsdSummary.BadgeForeground);

        var speedSummary = new FuelTypeReadingSummary { FuelType = "SPEED" };
        Assert.Equal("#F3E5F5", speedSummary.BadgeBackground);
        Assert.Equal("#4A148C", speedSummary.BadgeForeground);

        var cngSummary = new FuelTypeReadingSummary { FuelType = "CNG" };
        Assert.Equal("#E8F5E9", cngSummary.BadgeBackground);
        Assert.Equal("#1B5E20", cngSummary.BadgeForeground);
    }
}
