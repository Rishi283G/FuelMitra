using System;
using System.Collections.Generic;
using System.Linq;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        // ExpectedCollection = DsrTotalFuelAmount(6095.85 - 500 testing = 5595.85), repayments excluded per client specification
        Assert.Equal(5595.85, report.ExpectedCollection, precision: 2);

        // Verify the collection breakdown categories exist
        Assert.True(report.CollectionBreakdown.Count >= 5);
        
        var deposit = report.CollectionBreakdown.First(c => c.Category == "Cash Deposit").Amount;
        var cashInHandCat = report.CollectionBreakdown.First(c => c.Category == "Cash In Hand");
        
        Assert.Equal(500, deposit);
        Assert.Equal(150, cashInHandCat.Amount); // Pure operational cash in hand
        Assert.Equal(1000, cashInHandCat.RecoveryAmount); // Repayment preserved in recovery metadata
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

        // Actual Collections = 4000 (deposit) + 3000 (cash in hand) + 2500 (phonepe) + 1000 (oil) = 10500 (repayments excluded)
        Assert.Equal(10500, report.ActualCollection, precision: 2);

        // Expected Collections = DsrTotalFuelAmount(9500) + OilDefSalesTotal(1000) = 10500 (repayments excluded)
        Assert.Equal(10500, report.ExpectedCollection, precision: 2);

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

    [Fact]
    public void CalculateShiftReport_ShouldExcludeDebtorRepaymentsFromSalesTotalsAndPaymentMethods()
    {
        var date = new DateTime(2026, 7, 11);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                DsmName = "Test DSM",
                PumpId = 1,
                GrossSales = 5000,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 50, Amount = 5000, Rate = 100 }
                },
                PaymentCollection = new PaymentCollection 
                { 
                    PhonePeMorning = 1000, 
                    CreditCardMorning = 1500,
                    PetroCardMorning = 500
                },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash2", TotalAmount = 2000 }
                }
            }
        };

        var repayments = new List<CreditorRepayment>
        {
            new CreditorRepayment { Amount = 800, PaymentMode = "Cash", ShiftNumber = "A", RepaymentDate = date },
            new CreditorRepayment { Amount = 400, PaymentMode = "PhonePe", ShiftNumber = "A", RepaymentDate = date },
            new CreditorRepayment { Amount = 600, PaymentMode = "Credit Card", ShiftNumber = "A", RepaymentDate = date },
            new CreditorRepayment { Amount = 200, PaymentMode = "PetroCard", ShiftNumber = "A", RepaymentDate = date }
        };

        var report = _sut.CalculateShiftReport(
            date: date,
            shiftType: "A",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: repayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // Expected collection must NOT be inflated by the 2000 repayments
        Assert.Equal(5000, report.ExpectedCollection, precision: 2);
        Assert.Equal(5000, report.GrandTotalSaleAmount, precision: 2);

        // Payment method category amounts must be pure operational sales, NOT inflated
        var cashInHand = report.CollectionBreakdown.First(c => c.Category == "Cash In Hand");
        Assert.Equal(2000, cashInHand.Amount);
        Assert.Equal(800, cashInHand.RecoveryAmount);

        var phonePe = report.CollectionBreakdown.First(c => c.Category.Contains("PhonePe"));
        Assert.Equal(1000, phonePe.Amount);
        Assert.Equal(400, phonePe.RecoveryAmount);

        var creditCard = report.CollectionBreakdown.First(c => c.Category.Contains("Card") && !c.Category.Contains("Petro"));
        Assert.Equal(1500, creditCard.Amount);
        Assert.Equal(600, creditCard.RecoveryAmount);

        var petroCard = report.CollectionBreakdown.First(c => c.Category.Contains("PetroCard"));
        Assert.Equal(500, petroCard.Amount);
        Assert.Equal(200, petroCard.RecoveryAmount);

        // Operational reconciliation difference must remain 0 (balanced)
        Assert.Equal(0, report.Difference, precision: 2);
        Assert.True(report.IsBalanced);
    }

    [Fact]
    public void CalculateDayReport_ShouldExcludeDebtorRepaymentsFromSalesTotalsAndPaymentMethods()
    {
        var date = new DateTime(2026, 7, 11);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                DsmName = "Test DSM",
                PumpId = 1,
                GrossSales = 4000,
                Shift = new Shift { ShiftDate = date, ShiftType = "A" },
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 40, Amount = 4000, Rate = 100 }
                },
                PaymentCollection = new PaymentCollection 
                { 
                    PhonePeMorning = 1000, 
                    CreditCardMorning = 1000
                },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash2", TotalAmount = 2000 }
                }
            }
        };

        var repayments = new List<CreditorRepayment>
        {
            new CreditorRepayment { Amount = 500, PaymentMode = "Cash", RepaymentDate = date },
            new CreditorRepayment { Amount = 300, PaymentMode = "PhonePe", RepaymentDate = date }
        };

        var report = _sut.CalculateDayReport(
            startDate: date,
            endDate: date,
            entries: entries,
            allExpenses: new List<Expense>(),
            repayments: repayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            stationName: "Mitali Service Station"
        );

        // Day Expected Collection must NOT be inflated by the 800 repayments
        Assert.Equal(4000, report.ExpectedCollection, precision: 2);
        Assert.Equal(4000, report.GrandTotalSaleAmount, precision: 2);

        var cashInHand = report.CollectionBreakdown.First(c => c.Category == "Cash In Hand");
        Assert.Equal(2000, cashInHand.Amount);
        Assert.Equal(500, cashInHand.RecoveryAmount);

        var phonePe = report.CollectionBreakdown.First(c => c.Category.Contains("PhonePe"));
        Assert.Equal(1000, phonePe.Amount);
        Assert.Equal(300, phonePe.RecoveryAmount);

        Assert.Equal(0, report.Difference, precision: 2);
        Assert.True(report.IsBalanced);
    }

    [Fact]
    public void NozzleGroupDto_StockVariation_CalculationsAndDisplay()
    {
        // 1. Normal positive variation (manual > calculated = Excess)
        var dto = new NozzleGroupDto
        {
            OpeningStock = 1000,
            FuelDispensed = 200,
            TestingLitres = 10,
            Receipts = 500,
            Stock = 1320 // Calculated = 1000 - 200 + 10 + 500 = 1310. Variance = +10
        };

        Assert.True(dto.HasValidOpeningStock);
        Assert.Equal(1310, dto.CalculatedStock);
        Assert.Equal("1,310.00", dto.AutomaticStockDisplay);
        Assert.Equal(10, dto.StockVariance, precision: 2);
        Assert.Equal("+10.00", dto.StockVarianceDisplay);
        Assert.Equal("Excess", dto.StockVarianceStatus);

        // 2. Negative variation (manual < calculated = Shortage)
        dto.Stock = 1300;
        Assert.Equal(-10, dto.StockVariance, precision: 2);
        Assert.Equal("-10.00", dto.StockVarianceDisplay);
        Assert.Equal("Shortage", dto.StockVarianceStatus);

        // 3. Zero variation
        dto.Stock = 1310;
        Assert.Equal(0, dto.StockVariance, precision: 2);
        Assert.Equal("+0.00", dto.StockVarianceDisplay);

        // 4. Missing opening stock (<= 0)
        var dtoNoOpening = new NozzleGroupDto
        {
            OpeningStock = 0,
            FuelDispensed = 100,
            Stock = 500
        };
        Assert.False(dtoNoOpening.HasValidOpeningStock);
        Assert.Equal("—", dtoNoOpening.AutomaticStockDisplay);
        Assert.Equal("—", dtoNoOpening.StockVarianceDisplay);
    }

    [Fact]
    public void PineLabsCard_And_PhonePe_ItemCollections_ReflectedInDsmSummaryAndReconciliation()
    {
        var date = new DateTime(2026, 9, 15);
        var entry = new DsmEntry
        {
            DsmEntryId = 1,
            DsmName = "Santosh",
            PumpId = 1,
            GrossSales = 5000,
            Shift = new Shift { ShiftDate = date, ShiftType = "A" },
            NozzleReadings = new List<NozzleReading>
            {
                new() { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 50, Amount = 5000, Rate = 100 }
            },
            PaymentCollection = new PaymentCollection
            {
                Items = new List<PaymentCollectionItem>
                {
                    new() { CollectionTypeCode = "PINELABS_CARD", Amount = 3000, Slot = "Morning" },
                    new() { CollectionTypeCode = "PHONEPE", Amount = 2000, Slot = "Morning" }
                }
            }
        };

        var entries = new List<DsmEntry> { entry };

        // 1. Verify ShiftAggregationService.BuildDsmSummaryRows
        var summaryRows = _aggregation.BuildDsmSummaryRows(entries);
        Assert.Single(summaryRows);
        var row = summaryRows[0];
        Assert.Equal(3000, row.PineLabsCard);
        Assert.Equal(3000, row.CreditCardTotal);
        Assert.Equal(2000, row.PhonePeTotal);
        Assert.Equal(0, row.DynamicCollectionsTotal);
        Assert.Equal(5000, row.TotalCollection);
        Assert.Equal(0, row.Difference);

        // 2. Verify Shift Report
        var shiftReport = _sut.CalculateShiftReport(
            date, "A", entries,
            new List<Expense>(), new List<ShiftOtherCash>(), new List<CreditorRepayment>(),
            100, 100, 100, 100,
            new BusinessDayTidSheet(), new BusinessDayTidSheet(),
            "Mitali Service Station");

        Assert.Equal(5000, shiftReport.TotalFuelAmount);
        Assert.Equal(5000, shiftReport.ExpectedCollection);
        Assert.Equal(5000, shiftReport.ActualCollection);
        Assert.Equal(0, shiftReport.Difference);
        Assert.True(shiftReport.IsBalanced);

        var cardCategoriesShift = shiftReport.CollectionBreakdown
            .Where(c => c.Category.Contains("PineLab", StringComparison.OrdinalIgnoreCase) || c.Category.Contains("Credit", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(cardCategoriesShift);
        Assert.Equal(3000, cardCategoriesShift[0].Amount);

        var phonePeCategoriesShift = shiftReport.CollectionBreakdown
            .Where(c => c.Category.Contains("PhonePe", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(phonePeCategoriesShift);
        Assert.Equal(2000, phonePeCategoriesShift[0].Amount);

        // 3. Verify Day Report
        var dayReport = _sut.CalculateDayReport(
            date, date, entries,
            new List<Expense>(), new List<CreditorRepayment>(),
            100, 100, 100, 100,
            "Mitali Service Station");

        Assert.Equal(5000, dayReport.ExpectedCollection);
        Assert.Equal(5000, dayReport.ActualCollection);
        Assert.Equal(0, dayReport.Difference);
        Assert.True(dayReport.IsBalanced);

        var cardCategoriesDay = dayReport.CollectionBreakdown
            .Where(c => c.Category.Contains("PineLab", StringComparison.OrdinalIgnoreCase) || c.Category.Contains("Credit", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(cardCategoriesDay);
        Assert.Equal(3000, cardCategoriesDay[0].Amount);

        var phonePeCategoriesDay = dayReport.CollectionBreakdown
            .Where(c => c.Category.Contains("PhonePe", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Single(phonePeCategoriesDay);
        Assert.Equal(2000, phonePeCategoriesDay[0].Amount);
    }

    [Fact]
    public void CalculateShiftReport_And_DayReport_WithActiveCollectionTypeService_PineLabsCardNeverDuplicated()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var types = new List<CollectionTypeMaster>
        {
            new() { CollectionTypeId = 1, Code = "PINELABS_CARD", DisplayName = "PineLabs Card", IsActive = true, DisplayOrder = 1 },
            new() { CollectionTypeId = 2, Code = "PHONEPE", DisplayName = "PhonePe Direct", IsActive = true, DisplayOrder = 2 },
            new() { CollectionTypeId = 3, Code = "PETROCARD", DisplayName = "PetroCard", IsActive = true, DisplayOrder = 3 },
            new() { CollectionTypeId = 4, Code = "CASH_DEPOSIT", DisplayName = "Cash Deposit", IsActive = true, DisplayOrder = 4 },
            new() { CollectionTypeId = 5, Code = "SBI_REDEEM", DisplayName = "SBI Redeem", IsActive = true, DisplayOrder = 5 }
        };
        services.AddSingleton<ICollectionTypeService>(new StubCollectionTypeService(types));
        var sp = services.BuildServiceProvider();

        var sutWithSp = new ReportService(_aggregation, sp);

        var date = new DateTime(2026, 9, 15);
        var entry = new DsmEntry
        {
            DsmEntryId = 1,
            DsmName = "Santosh",
            PumpId = 1,
            GrossSales = 5000,
            Shift = new Shift { ShiftDate = date, ShiftType = "A" },
            NozzleReadings = new List<NozzleReading>
            {
                new() { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 50, Amount = 5000, Rate = 100 }
            },
            PaymentCollection = new PaymentCollection
            {
                Items = new List<PaymentCollectionItem>
                {
                    new() { CollectionTypeCode = "PINELABS_CARD", Amount = 2000, Slot = "Morning" },
                    new() { CollectionTypeCode = "PHONEPE", Amount = 1500, Slot = "Morning" },
                    new() { CollectionTypeCode = "SBI_REDEEM", Amount = 500, Slot = "Morning" }
                }
            },
            CashDenominations = new List<CashDenomination>
            {
                new() { CashType = "Cash2", TotalAmount = 1000 }
            }
        };

        var entries = new List<DsmEntry> { entry };

        // Test Shift Report
        var shiftReport = sutWithSp.CalculateShiftReport(
            date, "A", entries,
            new List<Expense>(), new List<ShiftOtherCash>(), new List<CreditorRepayment>(),
            100, 100, 100, 100,
            new BusinessDayTidSheet(), new BusinessDayTidSheet(),
            "Mitali Service Station");

        Assert.Single(shiftReport.CollectionBreakdown.Where(c => c.Category.Contains("PineLab", StringComparison.OrdinalIgnoreCase)));
        Assert.Single(shiftReport.CollectionBreakdown.Where(c => c.Category.Contains("PhonePe", StringComparison.OrdinalIgnoreCase)));
        Assert.Single(shiftReport.CollectionBreakdown.Where(c => c.Category.Contains("SBI", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2000, shiftReport.CollectionBreakdown.First(c => c.Category.Contains("PineLab", StringComparison.OrdinalIgnoreCase)).Amount);
        Assert.Equal(1500, shiftReport.CollectionBreakdown.First(c => c.Category.Contains("PhonePe", StringComparison.OrdinalIgnoreCase)).Amount);
        Assert.Equal(500, shiftReport.CollectionBreakdown.First(c => c.Category.Contains("SBI", StringComparison.OrdinalIgnoreCase)).Amount);
        Assert.Equal(5000, shiftReport.ActualCollection);
        Assert.Equal(5000, shiftReport.ExpectedCollection);
        Assert.Equal(0, shiftReport.Difference);
        Assert.True(shiftReport.IsBalanced);

        // Test Day Report
        var dayReport = sutWithSp.CalculateDayReport(
            date, date, entries,
            new List<Expense>(), new List<CreditorRepayment>(),
            100, 100, 100, 100,
            "Mitali Service Station");

        Assert.Single(dayReport.CollectionBreakdown.Where(c => c.Category.Contains("PineLab", StringComparison.OrdinalIgnoreCase)));
        Assert.Single(dayReport.CollectionBreakdown.Where(c => c.Category.Contains("PhonePe", StringComparison.OrdinalIgnoreCase)));
        Assert.Single(dayReport.CollectionBreakdown.Where(c => c.Category.Contains("SBI", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(2000, dayReport.CollectionBreakdown.First(c => c.Category.Contains("PineLab", StringComparison.OrdinalIgnoreCase)).Amount);
        Assert.Equal(1500, dayReport.CollectionBreakdown.First(c => c.Category.Contains("PhonePe", StringComparison.OrdinalIgnoreCase)).Amount);
        Assert.Equal(500, dayReport.CollectionBreakdown.First(c => c.Category.Contains("SBI", StringComparison.OrdinalIgnoreCase)).Amount);
        Assert.Equal(5000, dayReport.ActualCollection);
        Assert.Equal(5000, dayReport.ExpectedCollection);
        Assert.Equal(0, dayReport.Difference);
        Assert.True(dayReport.IsBalanced);
    }

    [Fact]
    public void DsmSummaryRowDto_GetAmount_ReturnsPineLabsCard_ForVariousPineLabAliases()
    {
        var row = new DsmSummaryRowDto
        {
            CreditCard = 0,
            PineLabsCard = 1850,
            PhonePe = 1200,
            CashDeposit = 500,
            CashInHand = 450,
            GrossSales = 4000
        };

        Assert.Equal(1850, row.GetAmount("PINELABCARD"));
        Assert.Equal(1850, row.GetAmount("PINELABS"));
        Assert.Equal(1850, row.GetAmount("PINELABSCARD"));
        Assert.Equal(1850, row.GetAmount("PINELABS_CARD"));
        Assert.Equal(1850, row.GetAmount("CREDITCARD"));
        Assert.Equal(1200, row.GetAmount("PHONEPE"));
        Assert.Equal(4000, row.TotalCollection);
        Assert.Equal(0, row.Difference);
        Assert.Equal(0, row.Mismatch);
    }

    [Fact]
    public void CalculateShiftReport_And_DayReport_WhenCreditCardInactiveAndPineLabsCardActive_IncludesPineLabsCardInActualCollectionAndBalances()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var types = new List<CollectionTypeMaster>
        {
            new() { CollectionTypeId = 1, Code = "CASH_DEPOSIT", DisplayName = "Cash Deposit", IsActive = true, DisplayOrder = 1 },
            new() { CollectionTypeId = 2, Code = "CREDIT_CARD", DisplayName = "Credit / Debit Card", IsActive = false, DisplayOrder = 2 },
            new() { CollectionTypeId = 3, Code = "PHONEPE", DisplayName = "PhonePe Direct", IsActive = true, DisplayOrder = 3 },
            new() { CollectionTypeId = 4, Code = "PINELABS_CARD", DisplayName = "Pine Labs Card", IsActive = true, DisplayOrder = 4 }
        };
        services.AddSingleton<ICollectionTypeService>(new StubCollectionTypeService(types));
        var sp = services.BuildServiceProvider();

        var sutWithSp = new ReportService(_aggregation, sp);

        var date = new DateTime(2026, 10, 25);
        var entry = new DsmEntry
        {
            DsmEntryId = 10,
            DsmName = "Santosh",
            PumpId = 1,
            GrossSales = 5000,
            Shift = new Shift { ShiftDate = date, ShiftType = "A" },
            NozzleReadings = new List<NozzleReading>
            {
                new() { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 50, Amount = 5000, Rate = 100 }
            },
            PaymentCollection = new PaymentCollection
            {
                Items = new List<PaymentCollectionItem>
                {
                    new() { CollectionTypeCode = "PINELABS_CARD", Amount = 3000, Slot = "Morning" },
                    new() { CollectionTypeCode = "PHONEPE", Amount = 2000, Slot = "Morning" }
                }
            }
        };

        var entries = new List<DsmEntry> { entry };

        // Test Shift Report
        var shiftReport = sutWithSp.CalculateShiftReport(
            date, "A", entries,
            new List<Expense>(), new List<ShiftOtherCash>(), new List<CreditorRepayment>(),
            100, 100, 100, 100,
            new BusinessDayTidSheet(), new BusinessDayTidSheet(),
            "Mitali Service Station");

        var shiftPineCard = shiftReport.CollectionBreakdown.FirstOrDefault(c => c.Category.Contains("Pine", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(shiftPineCard);
        Assert.Equal(3000, shiftPineCard.Amount);
        Assert.Equal(5000, shiftReport.ActualCollection);
        Assert.Equal(5000, shiftReport.ExpectedCollection);
        Assert.Equal(0, shiftReport.Difference);
        Assert.True(shiftReport.IsBalanced);

        // Test Day Report
        var dayReport = sutWithSp.CalculateDayReport(
            date, date, entries,
            new List<Expense>(), new List<CreditorRepayment>(),
            100, 100, 100, 100,
            "Mitali Service Station");

        var dayPineCard = dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category.Contains("Pine", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(dayPineCard);
        Assert.Equal(3000, dayPineCard.Amount);
        Assert.Equal(5000, dayReport.ActualCollection);
        Assert.Equal(5000, dayReport.ExpectedCollection);
        Assert.Equal(0, dayReport.Difference);
        Assert.True(dayReport.IsBalanced);
    }

    private class StubCollectionTypeService : ICollectionTypeService
    {
        private readonly List<CollectionTypeMaster> _types;
        public StubCollectionTypeService(List<CollectionTypeMaster> types) => _types = types;
        public event Action? CollectionTypesChanged;
        public void NotifyCollectionTypesChanged() => CollectionTypesChanged?.Invoke();
        public Task<List<CollectionTypeMaster>> GetActiveCollectionTypesAsync() => Task.FromResult(_types.Where(t => t.IsActive).ToList());
        public Task<List<CollectionTypeMaster>> GetAllCollectionTypesAsync() => Task.FromResult(_types.ToList());
        public Task<bool> SaveCollectionTypeAsync(CollectionTypeMaster item) => Task.FromResult(true);
        public Task<bool> DeleteCollectionTypeAsync(int collectionTypeId) => Task.FromResult(true);
        public Task<bool> UpdateDisplayOrdersAsync(IEnumerable<(int Id, int Order)> orders) => Task.FromResult(true);
    }
}

