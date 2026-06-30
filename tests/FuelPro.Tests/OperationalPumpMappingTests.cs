using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Services;

namespace FuelPro.Tests;

public class OperationalPumpMappingTests
{
    [Fact]
    public void VerifyNozzlesForPump4()
    {
        // 1. Assign DSM to P4
        var nozzles = PumpConfiguration.GetNozzlesForPump(4, new DateTime(2026, 6, 20));

        // 2. Verify only N10, N11, N12 appear
        Assert.Equal(new[] { 10, 11, 12 }, nozzles);
    }

    [Fact]
    public void VerifyFuelTypesForPump4Nozzles()
    {
        var histDate = new DateTime(2026, 6, 20);
        // 3. Verify fuel classification
        Assert.Equal(FuelType.MS_I, PumpConfiguration.GetFuelType(4, 10, histDate));
        Assert.Equal(FuelType.MS_II, PumpConfiguration.GetFuelType(4, 11, histDate));
        Assert.Equal(FuelType.HSD, PumpConfiguration.GetFuelType(4, 12, histDate));

        Assert.Equal("MS-I", PumpConfiguration.GetFuelTypeDisplayName(4, 10, histDate));
        Assert.Equal("MS-II", PumpConfiguration.GetFuelTypeDisplayName(4, 11, histDate));
        Assert.Equal("HSD", PumpConfiguration.GetFuelTypeDisplayName(4, 12, histDate));
    }

    [Fact]
    public void VerifyStrictNozzleValidation()
    {
        var histDate = new DateTime(2026, 6, 20);
        // Verify that invalid/mismatched combinations resolve to their actual nozzle fuel type
        var fuelType = PumpConfiguration.GetFuelType(1, 8, histDate);
        Assert.Equal(FuelType.MS_II, fuelType);

        // Verify that a completely nonexistent nozzle number returns the fallback
        var fallbackType = PumpConfiguration.GetFuelType(1, 99, histDate);
        Assert.Equal(FuelType.MS_I, fallbackType);
    }

    [Fact]
    public void VerifyShiftAggregationWithPump4()
    {
        // 4. Verify DSR / Shift aggregation calculations
        var aggregation = new ShiftAggregationService();
        var histShift = new Shift { ShiftDate = new DateTime(2026, 6, 20) };
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                PumpId = 4,
                Shift = histShift,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 10, SaleLitres = 100, Rate = 100, Amount = 10000 },
                    new NozzleReading { NozzleNumber = 11, SaleLitres = 50, Rate = 100, Amount = 5000 },
                    new NozzleReading { NozzleNumber = 12, SaleLitres = 200, Rate = 90, Amount = 18000 }
                }
            }
        };

        var (hsdLitres, hsdAmount) = aggregation.GetFuelTotals(entries, "HSD", null);
        var (msILitres, msIAmount) = aggregation.GetFuelTotals(entries, "MS-I", null);
        var (msIILitres, msIIAmount) = aggregation.GetFuelTotals(entries, "MS-II", null);

        Assert.Equal(200, hsdLitres);
        Assert.Equal(18000, hsdAmount);
        Assert.Equal(100, msILitres);
        Assert.Equal(10000, msIAmount);
        Assert.Equal(50, msIILitres);
        Assert.Equal(5000, msIIAmount);
    }

    [Fact]
    public void VerifyOwnerCalculationsWithNewPumps()
    {
        // 5. Verify Owner Dashboard / P&L aggregation totals
        var calculationService = new OwnerCalculationService();
        var histShift = new Shift { ShiftDate = new DateTime(2026, 6, 20) };
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                PumpId = 4,
                Shift = histShift,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 10, SaleLitres = 100, Rate = 100, Amount = 10000 },
                    new NozzleReading { NozzleNumber = 11, SaleLitres = 50, Rate = 100, Amount = 5000 },
                    new NozzleReading { NozzleNumber = 12, SaleLitres = 200, Rate = 90, Amount = 18000 }
                }
            }
        };

        var result = calculationService.Calculate(entries, Enumerable.Empty<Expense>(), Enumerable.Empty<ShiftOtherCash>());
        Assert.Equal(100, result.MsILitres);
        Assert.Equal(50, result.MsIILitres);
        Assert.Equal(200, result.HsdLitres);
        Assert.Equal(33000, result.GrossSales);
    }
}

