using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelPro.Tests;

public class DsmShiftRepaymentAndFeatureVerificationTests
{
    private readonly ReportService _reportService;
    private readonly ShiftAggregationService _aggregation;

    public DsmShiftRepaymentAndFeatureVerificationTests()
    {
        _aggregation = new ShiftAggregationService();
        _reportService = new ReportService(_aggregation);
    }

    private static FuelProDbContext CreateInMemoryDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new FuelProDbContext(options);
    }

    [Fact]
    public async Task ShiftResolution_ShouldResolveDistinctShiftAAndShiftB()
    {
        using var db = CreateInMemoryDbContext("ShiftResolutionDb");
        var shiftRepo = new ShiftRepository(db);
        var date = new DateTime(2026, 9, 14);

        // Resolve Shift A
        var shiftAResult = await shiftRepo.GetOrCreateShiftAsync(date, "A");
        Assert.True(shiftAResult.Success);
        Assert.NotNull(shiftAResult.Data);
        Assert.Equal("A", shiftAResult.Data.ShiftType);
        Assert.Equal(date.Date, shiftAResult.Data.ShiftDate.Date);

        // Resolve Shift B
        var shiftBResult = await shiftRepo.GetOrCreateShiftAsync(date, "B");
        Assert.True(shiftBResult.Success);
        Assert.NotNull(shiftBResult.Data);
        Assert.Equal("B", shiftBResult.Data.ShiftType);
        Assert.Equal(date.Date, shiftBResult.Data.ShiftDate.Date);

        // Verify distinct IDs
        Assert.NotEqual(shiftAResult.Data.ShiftId, shiftBResult.Data.ShiftId);

        // Idempotency check
        var shiftASecondCall = await shiftRepo.GetOrCreateShiftAsync(date, "A");
        Assert.Equal(shiftAResult.Data.ShiftId, shiftASecondCall.Data!.ShiftId);
    }

    [Fact]
    public void ShiftRepayment_ShiftACashRepayment_ShouldAffectOnlyShiftAAndDayTotal_NotShiftB()
    {
        var date = new DateTime(2026, 9, 14);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                DsmName = "Ramesh Kumar",
                PumpId = 1,
                GrossSales = 5000,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 50, Amount = 5000, Rate = 100 }
                },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash2", TotalAmount = 2000 }
                },
                PaymentCollection = new PaymentCollection { CashDeposit = 3000 }
            }
        };

        // Shift A repayment of 500 Cash
        var shiftARepayments = new List<CreditorRepayment>
        {
            new CreditorRepayment
            {
                CreditorRepaymentId = 101,
                CreditorName = "DSM Loss (Ramesh Kumar)",
                RepaymentDate = date,
                Amount = 500,
                PaymentMode = "Cash",
                ShiftNumber = "A"
            }
        };

        // Calculate Shift A report
        var reportA = _reportService.CalculateShiftReport(
            date: date,
            shiftType: "A",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: shiftARepayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // 1. Shift A asserts
        Assert.Equal(500, reportA.CashRepayments);
        Assert.Equal(5500, reportA.ExpectedCollection); // 5000 fuel + 500 recovery
        var cashInHandA = reportA.CollectionBreakdown.First(c => c.Category == "Cash In Hand").Amount;
        Assert.Equal(2500, cashInHandA); // 2000 base + 500 repayment
        Assert.True(reportA.IsBalanced);
        Assert.Equal(0, reportA.Difference, precision: 2);

        // Print table check for Shift A: exactly 1 DSM Loss repayment of 500
        Assert.Single(reportA.PersonalDebtorRepayments);
        Assert.Equal("Ramesh Kumar", reportA.PersonalDebtorRepayments[0].DsmName);
        Assert.Equal(500, reportA.PersonalDebtorRepayments[0].Amount);
        Assert.Equal("Cash", reportA.PersonalDebtorRepayments[0].PaymentMethod);
        // Excluded from Customer Debtor Repayments (Table C)
        Assert.Empty(reportA.DebtorRepayments);

        // 2. Shift B report with same repayment passed: should NOT match Shift B
        var reportB = _reportService.CalculateShiftReport(
            date: date,
            shiftType: "B",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: shiftARepayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        Assert.Equal(0, reportB.CashRepayments);
        Assert.Equal(5000, reportB.ExpectedCollection); // No recovery in Shift B
        var cashInHandB = reportB.CollectionBreakdown.First(c => c.Category == "Cash In Hand").Amount;
        Assert.Equal(2000, cashInHandB); // unchanged base
        Assert.Empty(reportB.PersonalDebtorRepayments); // Shift B print table has 0

        // 3. Day Total report: MUST aggregate the 500 repayment
        var dayReport = _reportService.CalculateDayReport(
            startDate: date,
            endDate: date,
            entries: entries,
            allExpenses: new List<Expense>(),
            repayments: shiftARepayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            stationName: "Mitali Service Station"
        );

        Assert.Equal(500, dayReport.CashRepayments);
        Assert.Equal(5500, dayReport.ExpectedCollection);
        var dayCashInHand = dayReport.CollectionBreakdown.First(c => c.Category == "Cash In Hand").Amount;
        Assert.Equal(2500, dayCashInHand);
        Assert.Single(dayReport.PersonalDebtorRepayments);
        Assert.Equal(500, dayReport.PersonalDebtorRepayments[0].Amount);
        Assert.True(dayReport.IsBalanced);
    }

    [Fact]
    public void PaymentModes_PhonePeAndCard_ShouldGoToDigitalAndNotCash()
    {
        var date = new DateTime(2026, 9, 14);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                DsmName = "Suresh Patel",
                PumpId = 1,
                GrossSales = 10000,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 100, Amount = 10000, Rate = 100 }
                },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash2", TotalAmount = 5000 }
                },
                PaymentCollection = new PaymentCollection 
                { 
                    PhonePeMorning = 3000, 
                    CreditCardMorning = 2000 
                }
            }
        };

        var digitalRepayments = new List<CreditorRepayment>
        {
            new CreditorRepayment
            {
                CreditorRepaymentId = 201,
                CreditorName = "DSM Loss (Suresh Patel)",
                RepaymentDate = date,
                Amount = 700,
                PaymentMode = "PhonePe",
                CardTid = "UPI12345",
                ShiftNumber = "A"
            },
            new CreditorRepayment
            {
                CreditorRepaymentId = 202,
                CreditorName = "DSM Loss (Suresh Patel)",
                RepaymentDate = date,
                Amount = 800,
                PaymentMode = "PineLabs Card",
                CardTid = "TID999",
                CardBatch = "B01",
                ShiftNumber = "A"
            }
        };

        var report = _reportService.CalculateShiftReport(
            date: date,
            shiftType: "A",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: digitalRepayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // Physical Cash in Hand MUST NOT be increased by digital repayments!
        var cashInHand = report.CollectionBreakdown.First(c => c.Category == "Cash In Hand").Amount;
        Assert.Equal(5000, cashInHand); // Exactly base 5000, NOT 5000 + 700 + 800

        // Digital categories MUST be increased
        Assert.Equal(700, report.PhonePeRepayments);
        Assert.Equal(800, report.CreditCardRepayments);
        Assert.Equal(0, report.CashRepayments);

        var phonePeCat = report.CollectionBreakdown.First(c => c.Category.Contains("PhonePe")).Amount;
        Assert.Equal(3700, phonePeCat); // 3000 base + 700 recovery

        var cardCat = report.CollectionBreakdown.First(c => c.Category.Contains("Card") || c.Category.Contains("PineLab")).Amount;
        Assert.Equal(2800, cardCat); // 2000 base + 800 recovery

        // ExpectedCollection = 10000 + 700 + 800 = 11500
        Assert.Equal(11500, report.ExpectedCollection);
        // ActualCollection = 5000 (cash) + 3700 (phonepe) + 2800 (card) = 11500
        Assert.Equal(11500, report.ActualCollection);
        Assert.True(report.IsBalanced);
        Assert.Equal(0, report.Difference, precision: 2);

        // Verify printed table has both rows
        Assert.Equal(2, report.PersonalDebtorRepayments.Count);
        Assert.Contains(report.PersonalDebtorRepayments, r => r.PaymentMethod == "PhonePe" && r.Amount == 700);
        Assert.Contains(report.PersonalDebtorRepayments, r => r.PaymentMethod == "PineLabs Card" && r.Amount == 800);
    }

    [Fact]
    public void HistoricalNullShiftRepayment_ShouldBeInDayTotal_AndExcludedFromShiftAAndShiftB()
    {
        var date = new DateTime(2026, 9, 14);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                DsmName = "Amit Singh",
                PumpId = 1,
                GrossSales = 4000,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 40, Amount = 4000, Rate = 100 }
                },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash2", TotalAmount = 4000 }
                }
            }
        };

        // Historical record with ShiftNumber = null
        var historicalRepayments = new List<CreditorRepayment>
        {
            new CreditorRepayment
            {
                CreditorRepaymentId = 301,
                CreditorName = "DSM Loss (Amit Singh)",
                RepaymentDate = date,
                Amount = 1200,
                PaymentMode = "Cash",
                ShiftNumber = null // Historical record
            }
        };

        // Shift A: excluded
        var reportA = _reportService.CalculateShiftReport(
            date: date,
            shiftType: "A",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: historicalRepayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );
        Assert.Equal(0, reportA.CashRepayments);
        Assert.Equal(4000, reportA.ExpectedCollection);
        Assert.Empty(reportA.PersonalDebtorRepayments);

        // Shift B: excluded
        var reportB = _reportService.CalculateShiftReport(
            date: date,
            shiftType: "B",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: historicalRepayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );
        Assert.Equal(0, reportB.CashRepayments);
        Assert.Equal(4000, reportB.ExpectedCollection);
        Assert.Empty(reportB.PersonalDebtorRepayments);

        // Day Total: INCLUDED
        var dayReport = _reportService.CalculateDayReport(
            startDate: date,
            endDate: date,
            entries: entries,
            allExpenses: new List<Expense>(),
            repayments: historicalRepayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            stationName: "Mitali Service Station"
        );
        Assert.Equal(1200, dayReport.CashRepayments);
        Assert.Equal(5200, dayReport.ExpectedCollection);
        Assert.Single(dayReport.PersonalDebtorRepayments);
        Assert.Equal(1200, dayReport.PersonalDebtorRepayments[0].Amount);
    }
}
