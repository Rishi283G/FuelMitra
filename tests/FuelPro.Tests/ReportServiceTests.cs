using System;
using System.Collections.Generic;
using System.Linq;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelPro.Tests;

public class ReportServiceTests : IDisposable
{
    private readonly ReportService _sut;
    private readonly ShiftAggregationService _aggregation;

    public ReportServiceTests()
    {
        PumpConfiguration.ResetToDefaults();
        _aggregation = new ShiftAggregationService();
        _sut = new ReportService(_aggregation);
    }

    public void Dispose()
    {
        PumpConfiguration.ResetToDefaults();
    }

    [Fact]
    public void CalculateShiftReport_ShouldConsolidateAllFinancialTotals()
    {
        // Arrange
        var date = new DateTime(2026, 7, 11);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                DsmName = "Peter Parker",
                PumpId = 1,
                GrossSales = 6095.85m,
                NozzleReadings = new List<NozzleReading>
                {
                    // NozzleNumber=1 on PumpId=1 maps to MS-I by default PumpConfiguration.
                    // Amount pre-calculated so TotalFuelAmount == GrossSales.
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 60.9585, Amount = 6095.85, Rate = 100 }
                },
                PaymentCollection = new PaymentCollection 
                { 
                    PhonePeMorning = 300, 
                    CreditCardMorning = 362,
                    PetroCardMorning = 247.98,
                    CashDeposit = 500,
                    Others = 50000 // should be excluded from total collections
                },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash1", TotalAmount = 500 },
                    new CashDenomination { CashType = "Cash2", TotalAmount = 150 }
                },
                DebitEntries = new List<DebitEntry> { new DebitEntry { Amount = 3600 } },
                Expenses = new List<Expense> { new Expense { Amount = 119.99 } },
                TestingEntries = new List<TestingEntry> { new TestingEntry { FuelType = "MS-I", Litres = 5, Rate = 100, Amount = 500 } }
            }
        };

        var repayments = new List<CreditorRepayment>
        {
            new CreditorRepayment { Amount = 1000, PaymentMode = "Cash", ShiftNumber = "B", RepaymentDate = date }
        };

        // Act
        var report = _sut.CalculateShiftReport(
            date: date,
            shiftType: "B",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: repayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // Assert
        Assert.Equal("II", report.ShiftLabel);
        Assert.Equal("Mitali Service Station", report.StationName);
        // ExpectedCollection = DsrTotalFuelAmount(6095.85 - 500 testing = 5595.85) + reconcilableRecoveries(1000 cash repayment)
        Assert.Equal(6595.85, report.ExpectedCollection, precision: 2);

        // Verify the collection breakdown categories exist
        Assert.True(report.CollectionBreakdown.Count >= 5);
        
        var deposit = report.CollectionBreakdown.First(c => c.Category == "Cash Deposit").Amount;
        var cashInHand = report.CollectionBreakdown.First(c => c.Category == "Cash In Hand").Amount;
        
        Assert.Equal(500, deposit);
        Assert.Equal(1150, cashInHand); // 150 + 1000 repayment
    }

    [Fact]
    public void CalculateShiftReport_WithTestingAndOilDefSales_ShouldExcludeTestingAndIncludeOilSalesInReconciliation()
    {
        // Arrange
        var date = new DateTime(2026, 7, 11);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                DsmName = "Peter Parker",
                PumpId = 1,
                GrossSales = 10000m,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 100, Amount = 10000, Rate = 100 }
                },
                TestingEntries = new List<TestingEntry>
                {
                    new TestingEntry { FuelType = "MS-I", Litres = 5, Rate = 100, Amount = 500 }
                },
                PaymentCollection = new PaymentCollection 
                { 
                    PhonePeDay = 2500, 
                    CashDeposit = 4000
                },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash2", TotalAmount = 3000 }
                },
                DebitEntries = new List<DebitEntry>(),
                Expenses = new List<Expense>(),
                ConnectedPumpIdsJson = "{\"oilDefSales\": [{\"productName\": \"Servo 4T 1L\", \"quantity\": 2, \"price\": 500, \"total\": 1000}]}"
            }
        };

        var repayments = new List<CreditorRepayment>
        {
            new CreditorRepayment { Amount = 500, PaymentMode = "Cash", ShiftNumber = "B", RepaymentDate = date }
        };

        // Act
        var report = _sut.CalculateShiftReport(
            date: date,
            shiftType: "B",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: repayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // Assert
        // Gross meter sales = 10000, Testing = 500 => DsrTotalFuelAmount = 9500
        Assert.Equal(10000, report.TotalFuelAmount);
        Assert.Equal(9500, report.DsrTotalFuelAmount);

        // Oil & DEF Sales Total = 1000
        Assert.Equal(1000, report.OilDefSalesTotal);

        // GrandTotalSaleAmount = DsrTotalFuelAmount(9500) + OilDefSalesTotal(1000) = 10500
        Assert.Equal(10500, report.GrandTotalSaleAmount);

        // CollectionBreakdown: Must contain "Oil & DEF Sales" and informational "Testing"
        var oilCat = report.CollectionBreakdown.FirstOrDefault(c => c.Category == "Oil & DEF Sales");
        Assert.NotNull(oilCat);
        Assert.Equal(1000, oilCat.Amount);
        var testCat = report.CollectionBreakdown.FirstOrDefault(c => c.Category.Contains("Testing", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(testCat);
        Assert.True(testCat.IsInformational);
        Assert.Equal(500, testCat.Amount);

        // Actual Collections = 4000 (deposit) + (3000 + 500 repayment) + 2500 (phonepe) + 1000 (oil) = 11000
        Assert.Equal(11000, report.ActualCollection, precision: 2);

        // Expected Collections = DsrTotalFuelAmount(9500) + OilDefSalesTotal(1000) + Repayments(500) = 11000
        Assert.Equal(11000, report.ExpectedCollection, precision: 2);

        // Perfectly balanced with no difference
        Assert.Equal(0, report.Difference, precision: 2);
        Assert.True(report.IsBalanced);
    }

    [Fact]
    public void CalculateDayReport_ShouldAggregateShifts()
    {
        // Arrange
        var date = new DateTime(2026, 7, 11);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                DsmName = "Peter Parker",
                PumpId = 1,
                GrossSales = 3000,
                Shift = new Shift { ShiftDate = date, ShiftType = "A" },
                PaymentCollection = new PaymentCollection { PhonePeMorning = 500 },
                CashDenominations = new List<CashDenomination> { new CashDenomination { CashType = "Cash2", TotalAmount = 2500 } },
                DebitEntries = new List<DebitEntry>(),
                Expenses = new List<Expense>(),
                TestingEntries = new List<TestingEntry>()
            }
        };

        // Act
        var report = _sut.CalculateDayReport(
            startDate: date,
            endDate: date,
            entries: entries,
            allExpenses: new List<Expense>(),
            repayments: new List<CreditorRepayment>(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            stationName: "Mitali Service Station"
        );

        // Assert
        Assert.Equal(3000, report.ActualCollection); // 500 PhonePe + 2500 Cash (today's shift entries only)
        // ExpectedCollection comes from NozzleReadings-based fuel totals; no nozzle readings → 0.
        Assert.Equal(0, report.ExpectedCollection);
    }

    [Fact]
    public void CalculateShiftReport_ShouldShowFullDsmShortRegardlessOfDsmLoss()
    {
        // Arrange
        var date = new DateTime(2026, 8, 21);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                DsmName = "Matin Pathan",
                PumpId = 1,
                GrossSales = 15221.31m,
                TotalCollection = 15170.68m,
                Mismatch = -50.63m,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 152.2131, Amount = 15221.31, Rate = 100 }
                },
                PaymentCollection = new PaymentCollection 
                { 
                    CashDeposit = 5000,
                    PhonePeMorning = 2122,
                    PhonePeNight = 211,
                    CreditCardMorning = 600,
                    CreditCardNight = 521,
                    PetroCardMorning = 200,
                    PetroCardNight = 200
                },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash1", TotalAmount = 5000 },
                    new CashDenomination { CashType = "Cash2", TotalAmount = 5656.68 }
                },
                DebitEntries = new List<DebitEntry>(),
                Expenses = new List<Expense> { new Expense { Amount = 60 } },
                TestingEntries = new List<TestingEntry>(),
                PersonalDebtors = new List<DsmPersonalDebtor>
                {
                    new DsmPersonalDebtor { DsmName = "Matin Pathan", Amount = 40.63, Remarks = "Auto Shift Shortage" }
                },
                KhandharePetroleumEntries = new List<KhandharePetroleumEntry>
                {
                    new KhandharePetroleumEntry { Name = "Owner", Amount = 600 }
                }
            }
        };

        // Act
        var report = _sut.CalculateShiftReport(
            date: date,
            shiftType: "A",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: new List<CreditorRepayment>(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Kandhare Petroleum"
        );

        // Assert
        // Total DSM Short must equal the full shortage of 50.63 (not reduced to 10.00 by 40.63 DSM loss)
        Assert.Equal(50.63, report.TotalDsmShort, precision: 2);
        var shortCategory = report.CollectionBreakdown.First(c => c.Category == "DSM Short");
        Assert.Equal(50.63, shortCategory.Amount, precision: 2);
        
        // Difference must equal -50.63
        Assert.Equal(-50.63, report.Difference, precision: 2);
    }
}
