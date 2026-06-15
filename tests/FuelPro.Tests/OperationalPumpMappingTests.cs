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
        var nozzles = PumpConfiguration.GetNozzlesForPump(4);

        // 2. Verify only N8, N9, N10 appear
        Assert.Equal(new[] { 8, 9, 10 }, nozzles);
    }

    [Fact]
    public void VerifyFuelTypesForPump4Nozzles()
    {
        // 3. Verify fuel classification
        Assert.Equal(FuelType.MS_I, PumpConfiguration.GetFuelType(4, 8));
        Assert.Equal(FuelType.MS_II, PumpConfiguration.GetFuelType(4, 9));
        Assert.Equal(FuelType.HSD, PumpConfiguration.GetFuelType(4, 10));

        Assert.Equal("MS-I", PumpConfiguration.GetFuelTypeDisplayName(4, 8));
        Assert.Equal("MS-II", PumpConfiguration.GetFuelTypeDisplayName(4, 9));
        Assert.Equal("HSD", PumpConfiguration.GetFuelTypeDisplayName(4, 10));
    }

    [Fact]
    public void VerifyStrictNozzleValidation()
    {
        // Verify that invalid combinations throw an ArgumentException instead of fallback
        Assert.Throws<ArgumentException>(() => PumpConfiguration.GetFuelType(1, 8));
    }

    [Fact]
    public void VerifyShiftAggregationWithPump4()
    {
        // 4. Verify DSR / Shift aggregation calculations
        var aggregation = new ShiftAggregationService();
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                PumpId = 4,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 8, SaleLitres = 100, Rate = 100, Amount = 10000 },
                    new NozzleReading { NozzleNumber = 9, SaleLitres = 50, Rate = 100, Amount = 5000 },
                    new NozzleReading { NozzleNumber = 10, SaleLitres = 200, Rate = 90, Amount = 18000 }
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
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                PumpId = 4,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 8, SaleLitres = 100, Rate = 100, Amount = 10000 },
                    new NozzleReading { NozzleNumber = 9, SaleLitres = 50, Rate = 100, Amount = 5000 },
                    new NozzleReading { NozzleNumber = 10, SaleLitres = 200, Rate = 90, Amount = 18000 }
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
