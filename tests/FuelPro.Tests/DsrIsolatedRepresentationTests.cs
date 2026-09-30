using System;
using System.Collections.Generic;
using System.Linq;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using Xunit;

namespace FuelPro.Tests;

public class DsrIsolatedRepresentationTests
{
    private readonly ReportService _reportService;
    private readonly ShiftAggregationService _aggregation;

    public DsrIsolatedRepresentationTests()
    {
        _aggregation = new ShiftAggregationService();
        _reportService = new ReportService(_aggregation);
    }

    [Fact]
    public void Test1_NoTesting_DsrEqualsGrossMeterSales()
    {
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
                PaymentCollection = new PaymentCollection { CashDeposit = 10000 },
                TestingEntries = new List<TestingEntry>()
            }
        };

        var report = _reportService.CalculateShiftReport(
            date: date, shiftType: "A", entries: entries,
            shiftExpenses: new List<Expense>(), otherCashList: new List<ShiftOtherCash>(), repayments: new List<CreditorRepayment>(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(), stationName: "Mitali Service Station"
        );

        // DSR Presentation Values
        Assert.NotNull(report.DsrFuelSales);
        Assert.Equal(100.0, report.DsrTotalFuelLitres, precision: 2);
        Assert.Equal(10000.0, report.DsrTotalFuelAmount, precision: 2);
        
        var msRow = report.DsrFuelSales.First(f => f.Description.Contains("MS") || f.FuelType == "MS-I");
        Assert.Equal(100.0, msRow.Litres, precision: 2);
        Assert.Equal(10000.0, msRow.Amount, precision: 2);
        Assert.Equal(100.0, msRow.Rate, precision: 2);

        // Canonical Accounting Invariance
        Assert.Equal(100.0, report.TotalFuelLitres, precision: 2);
        Assert.Equal(10000.0, report.TotalFuelAmount, precision: 2);
        Assert.Equal(10000.0, report.ExpectedCollection, precision: 2);
        Assert.Empty(report.TestingSummaryItems);
        Assert.True(report.IsBalanced);
    }

    [Fact]
    public void Test2_MsTesting_DsrDeductsTestingOnlyFromMs()
    {
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

        var report = _reportService.CalculateShiftReport(
            date: date, shiftType: "A", entries: entries,
            shiftExpenses: new List<Expense>(), otherCashList: new List<ShiftOtherCash>(), repayments: new List<CreditorRepayment>(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(), stationName: "Mitali Service Station"
        );

        // DSR Presentation Values: Deducts testing
        Assert.Equal(95.0, report.DsrTotalFuelLitres, precision: 2);
        Assert.Equal(9500.0, report.DsrTotalFuelAmount, precision: 2);
        
        var msRow = report.DsrFuelSales.First(f => f.Description.Contains("MS") || f.FuelType == "MS-I");
        Assert.Equal(95.0, msRow.Litres, precision: 2);
        Assert.Equal(9500.0, msRow.Amount, precision: 2);
        Assert.Equal(100.0, msRow.Rate, precision: 2);

        // Canonical Accounting Invariance: Gross remains untouched
        Assert.Equal(100.0, report.TotalFuelLitres, precision: 2);
        Assert.Equal(10000.0, report.TotalFuelAmount, precision: 2);
        Assert.Equal(10000.0, report.ExpectedCollection, precision: 2);
        Assert.Single(report.TestingSummaryItems);
        Assert.Equal(500.0, report.TestingSummaryItems[0].Amount, precision: 2);
        Assert.Equal(5.0, report.TestingSummaryItems[0].VolumeLitres, precision: 2);

        // Final reconciliation testing category exists
        Assert.Contains(report.CollectionBreakdown, c => c.Category.Contains("Testing") && c.Amount == 500.0);
    }

    [Fact]
    public void Test3_HsdTesting_DsrDeductsTestingOnlyFromHsd()
    {
        var date = new DateTime(2026, 7, 11);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                PumpId = 1,
                GrossSales = 18000m,
                NozzleReadings = new List<NozzleReading>
                {
                    // Nozzle 3 on Pump 1 is HSD
                    new NozzleReading { NozzleNumber = 3, FuelType = "HSD", SaleLitres = 200.0, Amount = 18000.0, Rate = 90.0 }
                },
                PaymentCollection = new PaymentCollection { CashDeposit = 17100 },
                TestingEntries = new List<TestingEntry>
                {
                    new TestingEntry { FuelType = "HSD", Litres = 10.0, Rate = 90.0, Amount = 900.0 }
                }
            }
        };

        var report = _reportService.CalculateShiftReport(
            date: date, shiftType: "A", entries: entries,
            shiftExpenses: new List<Expense>(), otherCashList: new List<ShiftOtherCash>(), repayments: new List<CreditorRepayment>(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(), stationName: "Mitali Service Station"
        );

        // DSR HSD is net customer sales
        Assert.Equal(190.0, report.DsrTotalFuelLitres, precision: 2);
        Assert.Equal(17100.0, report.DsrTotalFuelAmount, precision: 2);

        var hsdRow = report.DsrFuelSales.First(f => f.Description.Contains("HSD") || f.FuelType == "HSD");
        Assert.Equal(190.0, hsdRow.Litres, precision: 2);
        Assert.Equal(17100.0, hsdRow.Amount, precision: 2);
        Assert.Equal(90.0, hsdRow.Rate, precision: 2);

        // Canonical FuelSales remains gross
        Assert.Equal(200.0, report.TotalFuelLitres, precision: 2);
        Assert.Equal(18000.0, report.TotalFuelAmount, precision: 2);
        Assert.Equal(18000.0, report.ExpectedCollection, precision: 2);
    }

    [Fact]
    public void Test4_MultipleProducts_TestingDeductedIndependently()
    {
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
            },
            new DsmEntry
            {
                DsmEntryId = 2,
                PumpId = 1,
                GrossSales = 18000m,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 3, FuelType = "HSD", SaleLitres = 200.0, Amount = 18000.0, Rate = 90.0 }
                },
                PaymentCollection = new PaymentCollection { CashDeposit = 17100 },
                TestingEntries = new List<TestingEntry>
                {
                    new TestingEntry { FuelType = "HSD", Litres = 10.0, Rate = 90.0, Amount = 900.0 }
                }
            }
        };

        var report = _reportService.CalculateShiftReport(
            date: date, shiftType: "A", entries: entries,
            shiftExpenses: new List<Expense>(), otherCashList: new List<ShiftOtherCash>(), repayments: new List<CreditorRepayment>(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(), stationName: "Mitali Service Station"
        );

        // DSR Totals: 285 L and 26,600
        Assert.Equal(285.0, report.DsrTotalFuelLitres, precision: 2);
        Assert.Equal(26600.0, report.DsrTotalFuelAmount, precision: 2);

        var msRow = report.DsrFuelSales.First(f => (f.FuelType == "MS-I" || f.Description.Contains("MS")) && !f.Description.Contains("II"));
        var hsdRow = report.DsrFuelSales.First(f => (f.FuelType == "HSD" || f.Description.Contains("HSD")) && !f.Description.Contains("II"));

        Assert.Equal(95.0, msRow.Litres, precision: 2);
        Assert.Equal(9500.0, msRow.Amount, precision: 2);
        Assert.Equal(190.0, hsdRow.Litres, precision: 2);
        Assert.Equal(17100.0, hsdRow.Amount, precision: 2);

        // Canonical Totals: 300 L and 28,000
        Assert.Equal(300.0, report.TotalFuelLitres, precision: 2);
        Assert.Equal(28000.0, report.TotalFuelAmount, precision: 2);
        Assert.Equal(28000.0, report.ExpectedCollection, precision: 2);
    }

    [Fact]
    public void Test5_ZeroSaleZeroTesting_EdgeCase()
    {
        // USER REQUIREMENT 4: Meter MS = 5 L, Testing MS = 5 L -> DSR MS = 0 L / ₹0
        var date = new DateTime(2026, 7, 11);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                PumpId = 1,
                GrossSales = 500m,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 5.0, Amount = 500.0, Rate = 100.0 }
                },
                PaymentCollection = new PaymentCollection { CashDeposit = 0 },
                TestingEntries = new List<TestingEntry>
                {
                    new TestingEntry { FuelType = "MS-I", Litres = 5.0, Rate = 100.0, Amount = 500.0 }
                }
            }
        };

        var report = _reportService.CalculateShiftReport(
            date: date, shiftType: "A", entries: entries,
            shiftExpenses: new List<Expense>(), otherCashList: new List<ShiftOtherCash>(), repayments: new List<CreditorRepayment>(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(), stationName: "Mitali Service Station"
        );

        // DSR Presentation Values: Exactly 0 L and ₹0 (DOES NOT fall back to gross!)
        Assert.Equal(0.0, report.DsrTotalFuelLitres, precision: 2);
        Assert.Equal(0.0, report.DsrTotalFuelAmount, precision: 2);
        
        var msRow = report.DsrFuelSales.First(f => f.Description.Contains("MS") || f.FuelType == "MS-I");
        Assert.Equal(0.0, msRow.Litres, precision: 2);
        Assert.Equal(0.0, msRow.Amount, precision: 2);
        Assert.Equal(100.0, msRow.Rate, precision: 2);

        // Canonical Values remain gross
        Assert.Equal(5.0, report.TotalFuelLitres, precision: 2);
        Assert.Equal(500.0, report.TotalFuelAmount, precision: 2);
        Assert.Equal(500.0, report.ExpectedCollection, precision: 2);
        Assert.Single(report.TestingSummaryItems);
        Assert.Equal(500.0, report.TestingSummaryItems[0].Amount, precision: 2);

        // Balance Check: Testing 500 in collection, Expected 500 -> Balanced
        Assert.Equal(500.0, report.ActualCollection, precision: 2);
        Assert.Equal(0.0, report.Difference, precision: 2);
        Assert.True(report.IsBalanced);
    }

    [Fact]
    public void Test6_DayReport_DsrMatchesActualSalesOnly()
    {
        var date = new DateTime(2026, 7, 11);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                PumpId = 1,
                GrossSales = 10000m,
                Shift = new Shift { ShiftDate = date, ShiftType = "A" },
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 100.0, Amount = 10000.0, Rate = 100.0 }
                },
                PaymentCollection = new PaymentCollection { CashDeposit = 9500 },
                TestingEntries = new List<TestingEntry>
                {
                    new TestingEntry { FuelType = "MS-I", Litres = 5.0, Rate = 100.0, Amount = 500.0 }
                }
            },
            new DsmEntry
            {
                DsmEntryId = 2,
                PumpId = 1,
                GrossSales = 18000m,
                Shift = new Shift { ShiftDate = date, ShiftType = "B" },
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 3, FuelType = "HSD", SaleLitres = 200.0, Amount = 18000.0, Rate = 90.0 }
                },
                PaymentCollection = new PaymentCollection { CashDeposit = 17100 },
                TestingEntries = new List<TestingEntry>
                {
                    new TestingEntry { FuelType = "HSD", Litres = 10.0, Rate = 90.0, Amount = 900.0 }
                }
            }
        };

        var dayReport = _reportService.CalculateDayReport(
            startDate: date, endDate: date, entries: entries,
            allExpenses: new List<Expense>(), repayments: new List<CreditorRepayment>(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            stationName: "Mitali Service Station"
        );

        // Day DSR: Net Customer Sales Only
        Assert.Equal(285.0, dayReport.DsrTotalFuelLitres, precision: 2);
        Assert.Equal(26600.0, dayReport.DsrTotalFuelAmount, precision: 2);

        // Canonical Day Report: Gross
        Assert.Equal(300.0, dayReport.TotalFuelLitres, precision: 2);
        Assert.Equal(28000.0, dayReport.TotalFuelAmount, precision: 2);
        Assert.Equal(28000.0, dayReport.ExpectedCollection, precision: 2);
        Assert.Equal(2, dayReport.TestingSummaryItems.Count);
        Assert.Equal(1400.0, dayReport.TestingSummaryItems.Sum(t => t.Amount), precision: 2);
    }
}
