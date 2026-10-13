using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using Xunit;

namespace FuelPro.Tests;

/// <summary>
/// Phase 2.30 Regression Suite:
/// Verifies the architectural separation of Pure Gross Fuel Sale (TotalFuelAmount)
/// from the Reconciliation Normalization Target (ExpectedCollection).
/// </summary>
public class DebtorReconciliationSeparationTests
{
    private readonly ReportService _sut;
    private readonly ShiftAggregationService _aggregation;

    public DebtorReconciliationSeparationTests()
    {
        _aggregation = new ShiftAggregationService();
        _sut = new ReportService(_aggregation);
    }

    [Fact]
    public void PureGrossFuelSale_Equals_TotalFuelAmount_NeverIncludesDebtorRecovery()
    {
        // Arrange: Shift with 5,000 fuel sales and a 500 cash debtor recovery
        var date = new DateTime(2026, 10, 1);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 101,
                DsmName = "Test DSM",
                PumpId = 1,
                GrossSales = 5000m,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 50.0, Amount = 5000.0, Rate = 100 }
                },
                PaymentCollection = new PaymentCollection
                {
                    PhonePeMorning = 2000,
                    CashDeposit = 1500
                },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash1", TotalAmount = 1500 }
                },
                DebitEntries = new List<DebitEntry>(),
                Expenses = new List<Expense>(),
                TestingEntries = new List<TestingEntry>()
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

        // Assert:
        // 1. Pure Gross Fuel Sale MUST be strictly nozzle sales (5,000)
        Assert.Equal(5000.0, report.TotalFuelAmount, precision: 2);
        Assert.Equal(5000.0, report.GrandTotalSaleAmount, precision: 2);

        // 2. Debtor Recovery must be 500
        Assert.Equal(500.0, report.DebtorRepaymentsTotal, precision: 2);

        // 3. ExpectedCollection is the reconciliation-normalization target (5,000 + 500 = 5,500)
        Assert.Equal(5500.0, report.ExpectedCollection, precision: 2);

        // 4. Verify separation: ExpectedCollection != TotalFuelAmount when recovery exists
        Assert.NotEqual(report.TotalFuelAmount, report.ExpectedCollection);
    }

    [Fact]
    public void DebtorRegister_IsPartiallyOrFullyCharged_NotAddedToGrossSaleTwice()
    {
        // Arrange: Shift with fuel sales 10,000, of which 3,000 is on credit (Debtor Register)
        var date = new DateTime(2026, 10, 1);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 102,
                DsmName = "Test DSM",
                PumpId = 1,
                GrossSales = 10000m,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "HSD", SaleLitres = 100.0, Amount = 10000.0, Rate = 100 }
                },
                PaymentCollection = new PaymentCollection
                {
                    CashDeposit = 7000
                },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash1", TotalAmount = 7000 }
                },
                DebitEntries = new List<DebitEntry>
                {
                    new DebitEntry { Amount = 3000, DebtorName = "Fleet Customer A" }
                },
                Expenses = new List<Expense>(),
                TestingEntries = new List<TestingEntry>()
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
            hsdRate: 100, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // Assert:
        // Fuel sale is 10,000. Debtor register is 3,000.
        // TotalFuelAmount is NOT 13,000 (debtor register is not added on top of nozzle sale).
        Assert.Equal(10000.0, report.TotalFuelAmount, precision: 2);
        Assert.Equal(3000.0, report.CreditorsTotal, precision: 2);

        // Collections: 7,000 cash deposit + 3,000 debtors = 10,000
        Assert.Equal(10000.0, report.ActualCollection, precision: 2);
        Assert.Equal(10000.0, report.ExpectedCollection, precision: 2);
        Assert.Equal(0.0, report.Difference, precision: 2);
        Assert.True(report.IsBalanced);
    }

    [Fact]
    public void ReconciliationTarget_NeutralizesDebtorRecovery_PreservingBalancedShift()
    {
        // Arrange: Fuel sales = 10,000. Debtor Recovery = 2,000.
        // Cash in drawer = 10,000 (from current sales) + 2,000 (from recovery) = 12,000.
        var date = new DateTime(2026, 10, 1);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 103,
                DsmName = "Test DSM",
                PumpId = 1,
                GrossSales = 10000m,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "HSD", SaleLitres = 100.0, Amount = 10000.0, Rate = 100 }
                },
                PaymentCollection = new PaymentCollection
                {
                    CashDeposit = 10000
                },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash1", TotalAmount = 10000 }
                },
                DebitEntries = new List<DebitEntry>(),
                Expenses = new List<Expense>(),
                TestingEntries = new List<TestingEntry>()
            }
        };

        var repayments = new List<CreditorRepayment>
        {
            new CreditorRepayment { Amount = 2000, PaymentMode = "Cash", ShiftNumber = "A", RepaymentDate = date }
        };

        // Act
        var report = _sut.CalculateShiftReport(
            date: date,
            shiftType: "A",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: repayments,
            hsdRate: 100, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // Assert:
        // Pure Gross Fuel Sale is strictly 10,000
        Assert.Equal(10000.0, report.TotalFuelAmount, precision: 2);

        // ActualCollection has 10,000 (deposit) + 2,000 (cash repayment) = 12,000
        Assert.Equal(12000.0, report.ActualCollection, precision: 2);

        // ExpectedCollection neutralizes recovery: 10,000 + 2,000 = 12,000
        Assert.Equal(12000.0, report.ExpectedCollection, precision: 2);

        // Shift is perfectly balanced! Difference is 0!
        Assert.Equal(0.0, report.Difference, precision: 2);
        Assert.True(report.IsBalanced);
    }

    [Fact]
    public void RealMitaliShiftB_ProductionValues_Verification()
    {
        // Arrange: Exact production data from Mitali Service Station Shift B (2026-10-01)
        var date = new DateTime(2026, 10, 1);

        // Total fuel sales across nozzles: 10,27,854.39
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 9,
                DsmName = "Shift B DSMs",
                PumpId = 1,
                GrossSales = 1027854.39m,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "HSD", SaleLitres = 4000.0, Rate = 90.0, Amount = 360000.0 },
                    new NozzleReading { NozzleNumber = 2, FuelType = "MS-I", SaleLitres = 3000.0, Rate = 104.0, Amount = 312000.0 },
                    new NozzleReading { NozzleNumber = 3, FuelType = "MS-II", SaleLitres = 2000.0, Rate = 104.0, Amount = 208000.0 },
                    new NozzleReading { NozzleNumber = 4, FuelType = "SPEED", SaleLitres = 1358.34, Rate = 108.85, Amount = 147854.39 }
                },
                PaymentCollection = new PaymentCollection
                {
                    CreditCardDay = 163273.0,
                    PetroCardDay = 51042.0,
                    Others = 0
                },
                CashDenominations = new List<CashDenomination>
                {
                    // Cash In Hand without recovery = 746573.0
                    new CashDenomination { CashType = "Cash2", TotalAmount = 746573.0 }
                },
                DebitEntries = new List<DebitEntry>
                {
                    // 8 debtor slips totaling 1,09,980.00
                    new DebitEntry { Amount = 109980.0, DebtorName = "Total Debtors Register" }
                },
                Expenses = new List<Expense>(),
                TestingEntries = new List<TestingEntry>()
            }
        };

        // 1 Debtor Recovery: MOKATE ₹30,740.00 in Cash
        var repayments = new List<CreditorRepayment>
        {
            new CreditorRepayment
            {
                CreditorName = "MOKATE",
                PaymentMode = "Cash",
                Amount = 30740.0,
                ShiftNumber = "B",
                RepaymentDate = date
            }
        };

        // Act
        var report = _sut.CalculateShiftReport(
            date: date,
            shiftType: "B",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: repayments,
            hsdRate: 90, msIRate: 104, msIIRate: 104, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // Assert:
        // 1. True Gross Fuel Sale = 10,27,854.39
        Assert.Equal(1027854.39, report.TotalFuelAmount, precision: 2);
        Assert.Equal(1027854.39, report.GrandTotalSaleAmount, precision: 2);

        // 2. Debtors Register = 1,09,980.00
        Assert.Equal(109980.0, report.CreditorsTotal, precision: 2);

        // 3. Debtor Recovery = 30,740.00
        Assert.Equal(30740.0, report.DebtorRepaymentsTotal, precision: 2);

        // 4. Expected Collection (Reconciliation Target) = 10,27,854.39 + 30,740.00 = 10,58,594.39
        Assert.Equal(1058594.39, report.ExpectedCollection, precision: 2);

        // 5. Total Collection (A):
        //    Cash In Hand (746573.0 + 30740.0 = 777313.0) + Card (163273.0) + Petro (51042.0) + Debtors (109980.0) = 11,01,608.00
        Assert.Equal(1101608.0, report.ActualCollection, precision: 2);

        // 6. Reconciliation Difference = 11,01,608.00 - 10,58,594.39 = +43,013.61
        Assert.Equal(43013.61, report.Difference, precision: 2);
    }

    [Fact]
    public void DayTotalReport_SeparatesGrossFuelSaleFromExpectedCollection()
    {
        // Arrange: Shift A (fuel 5,000, recovery 500) + Shift B (fuel 6,000, recovery 700)
        var date = new DateTime(2026, 10, 1);
        var entriesA = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 201,
                DsmName = "DSM A",
                PumpId = 1,
                GrossSales = 5000m,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "HSD", SaleLitres = 50.0, Rate = 100.0, Amount = 5000.0 }
                },
                PaymentCollection = new PaymentCollection { CashDeposit = 5000 },
                CashDenominations = new List<CashDenomination> { new CashDenomination { CashType = "Cash1", TotalAmount = 5000 } },
                DebitEntries = new List<DebitEntry>(),
                Expenses = new List<Expense>(),
                TestingEntries = new List<TestingEntry>()
            }
        };

        var entriesB = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 202,
                DsmName = "DSM B",
                PumpId = 2,
                GrossSales = 6000m,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 60.0, Rate = 100.0, Amount = 6000.0 }
                },
                PaymentCollection = new PaymentCollection { CashDeposit = 6000 },
                CashDenominations = new List<CashDenomination> { new CashDenomination { CashType = "Cash1", TotalAmount = 6000 } },
                DebitEntries = new List<DebitEntry>(),
                Expenses = new List<Expense>(),
                TestingEntries = new List<TestingEntry>()
            }
        };

        var allEntries = entriesA.Concat(entriesB).ToList();
        var allRepayments = new List<CreditorRepayment>
        {
            new CreditorRepayment { Amount = 500, PaymentMode = "Cash", ShiftNumber = "A", RepaymentDate = date },
            new CreditorRepayment { Amount = 700, PaymentMode = "Cash", ShiftNumber = "B", RepaymentDate = date }
        };

        // Act
        var dayReport = _sut.CalculateDayReport(
            startDate: date,
            endDate: date,
            entries: allEntries,
            allExpenses: new List<Expense>(),
            repayments: allRepayments,
            hsdRate: 100, msIRate: 100, msIIRate: 100, cngRate: 85,
            stationName: "Mitali Service Station"
        );

        // Assert:
        // Pure Gross Fuel Sale = 5,000 + 6,000 = 11,000
        Assert.Equal(11000.0, dayReport.TotalFuelAmount, precision: 2);

        // Debtor Recovery = 500 + 700 = 1,200
        Assert.Equal(1200.0, dayReport.DebtorRepaymentsTotal, precision: 2);

        // Expected Collection = 11,000 + 1,200 = 12,200
        Assert.Equal(12200.0, dayReport.ExpectedCollection, precision: 2);

        // Actual Collection = (5000+500) + (6000+700) = 12,200
        Assert.Equal(12200.0, dayReport.ActualCollection, precision: 2);

        // Difference = 0 (Balanced)
        Assert.Equal(0.0, dayReport.Difference, precision: 2);
        Assert.True(dayReport.IsBalanced);
    }

    [Fact]
    public void PresentationAndExportLayers_MapGrossSaleToTotalFuelAmount()
    {
        // Find repository root
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "FuelPro.sln")))
        {
            dir = dir.Parent;
        }
        var solutionDir = dir?.FullName ?? AppDomain.CurrentDomain.BaseDirectory;

        // 1. Verify FinalCalculationViewModel.cs
        var fcvmPath = Path.Combine(solutionDir, "src", "FuelPro.UI", "ViewModels", "FinalCalculationViewModel.cs");
        var fcvmContent = File.ReadAllText(fcvmPath);
        Assert.Contains("GrossSaleTotal = report.GrandTotalSaleAmount;", fcvmContent);
        Assert.DoesNotContain("GrossSaleTotal = report.ExpectedCollection;", fcvmContent);

        // 2. Verify DayTotalViewModel.cs
        var dtvmPath = Path.Combine(solutionDir, "src", "FuelPro.UI", "ViewModels", "DayTotalViewModel.cs");
        var dtvmContent = File.ReadAllText(dtvmPath);
        Assert.Contains("GrossDaySaleTotal = report.GrandTotalSaleAmount;", dtvmContent);
        Assert.DoesNotContain("GrossDaySaleTotal = report.ExpectedCollection;", dtvmContent);

        // 3. Verify FinalCalculationPrintTemplate.html
        var htmlPath = Path.Combine(solutionDir, "src", "FuelPro.UI", "Printing", "FinalCalculationPrintTemplate.html");
        var htmlContent = File.ReadAllText(htmlPath);
        Assert.Contains("var grossFuel = (d.grandTotalSaleAmount !== undefined && d.grandTotalSaleAmount !== null) ? d.grandTotalSaleAmount : ((d.dsrTotalFuelAmount !== undefined && d.dsrTotalFuelAmount !== null) ? d.dsrTotalFuelAmount : (d.totalFuelAmount || 0));", htmlContent);
        Assert.DoesNotContain("var grossFuel = d.expectedCollection || 0;", htmlContent);

        // 4. Verify ExcelExportService.cs
        var excelPath = Path.Combine(solutionDir, "src", "FuelPro.Core", "Services", "ExcelExportService.cs");
        var excelContent = File.ReadAllText(excelPath).Replace("\r\n", "\n");
        Assert.Contains("worksheet.Cell(r, 1).Value = \"Gross Sale (B)\";\n                worksheet.Cell(r, 2).Value = report.GrandTotalSaleAmount;", excelContent);
        Assert.Contains("worksheet.Cell(r, 1).Value = \"Total Day Sale (B)\";\n                worksheet.Cell(r, 2).Value = report.GrandTotalSaleAmount;", excelContent);
    }

    [Fact]
    public void DebtorRepayments_MultiplePaymentMethods_IncrementRespectiveCategories_ExactlyOnce()
    {
        var date = new DateTime(2026, 10, 1);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 201,
                DsmName = "Shift DSM",
                PumpId = 1,
                GrossSales = 20000m,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "HSD", SaleLitres = 200.0, Amount = 20000.0, Rate = 100 }
                },
                PaymentCollection = new PaymentCollection
                {
                    CashDeposit = 5000,
                    PhonePeDay = 2000,
                    CreditCardDay = 3000,
                    PetroCardDay = 1000
                },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash1", TotalAmount = 5000 },
                    new CashDenomination { CashType = "Cash2", TotalAmount = 6000 }
                },
                DebitEntries = new List<DebitEntry>
                {
                    new DebitEntry { Amount = 3000, DebtorName = "Account Customer XYZ" }
                },
                Expenses = new List<Expense>(),
                TestingEntries = new List<TestingEntry>()
            }
        };

        var repayments = new List<CreditorRepayment>
        {
            new CreditorRepayment { Amount = 500, PaymentMode = "Cash", ShiftNumber = "B", RepaymentDate = date },
            new CreditorRepayment { Amount = 1000, PaymentMode = "PhonePe", ShiftNumber = "B", RepaymentDate = date },
            new CreditorRepayment { Amount = 2000, PaymentMode = "PineLabs Card", ShiftNumber = "B", RepaymentDate = date },
            new CreditorRepayment { Amount = 1500, PaymentMode = "PetroCard", ShiftNumber = "B", RepaymentDate = date },
            new CreditorRepayment { Amount = 4000, PaymentMode = "Bank Transfer", ShiftNumber = "B", RepaymentDate = date }
        };

        var report = _sut.CalculateShiftReport(
            date: date,
            shiftType: "B",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: repayments,
            hsdRate: 100, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // 1. Pure nozzle fuel sale remains exactly 20,000 (never polluted by recoveries)
        Assert.Equal(20000.0, report.TotalFuelAmount, precision: 2);

        // 2. Original debtor credit sale (3,000) remains an uncollected receivable in Debtors
        Assert.Equal(3000.0, report.CreditorsTotal, precision: 2);
        var debtorsCat = report.CollectionBreakdown.First(c => c.Category == "Debtors");
        Assert.Equal(3000.0, debtorsCat.Amount);

        // 3. Cash In Hand increments by Cash repayment (6000 base + 500 recovery = 6500)
        var cashInHand = report.CollectionBreakdown.First(c => c.Category == "Cash In Hand");
        Assert.Equal(6500.0, cashInHand.Amount);
        Assert.Equal(6000.0, cashInHand.BaseAmount);
        Assert.Equal(500.0, cashInHand.RecoveryAmount);

        // 4. PhonePe Direct increments by PhonePe repayment (2000 base + 1000 recovery = 3000)
        var phonePe = report.CollectionBreakdown.First(c => c.Category.Contains("PhonePe"));
        Assert.Equal(3000.0, phonePe.Amount);
        Assert.Equal(2000.0, phonePe.BaseAmount);
        Assert.Equal(1000.0, phonePe.RecoveryAmount);

        // 5. PineLabs Card increments by Card repayment (3000 base + 2000 recovery = 5000)
        var card = report.CollectionBreakdown.First(c => c.Category.Contains("PineLab") || c.Category.Contains("Card"));
        Assert.Equal(5000.0, card.Amount);
        Assert.Equal(3000.0, card.BaseAmount);
        Assert.Equal(2000.0, card.RecoveryAmount);

        // 6. PetroCard increments by PetroCard repayment (1000 base + 1500 recovery = 2500)
        var petroCard = report.CollectionBreakdown.First(c => c.Category.Contains("PetroCard"));
        Assert.Equal(2500.0, petroCard.Amount);
        Assert.Equal(1000.0, petroCard.BaseAmount);
        Assert.Equal(1500.0, petroCard.RecoveryAmount);

        // 7. Cash Deposit MUST NOT have Bank Transfer added to it (BaseAmount 5000, RecoveryAmount 0)
        var cashDeposit = report.CollectionBreakdown.First(c => c.Category.Contains("Cash Deposit"));
        Assert.Equal(5000.0, cashDeposit.Amount);
        Assert.Equal(5000.0, cashDeposit.BaseAmount);
        Assert.Equal(0.0, cashDeposit.RecoveryAmount);

        // 8. Bank Transfer recovery is distinctly represented as Informational row
        var bankRecovery = report.CollectionBreakdown.First(c => c.Category.Contains("Bank / Cheque Recovery"));
        Assert.Equal(4000.0, bankRecovery.Amount);
        Assert.True(bankRecovery.IsInformational);

        // 9. Total debtor repayments tracked in DTO
        Assert.Equal(9000.0, report.DebtorRepaymentsTotal, precision: 2);
    }

    [Fact]
    public void ShiftCRepayment_NightWindowClassification_MatchesShiftC_AndNeverShiftA()
    {
        var date = new DateTime(2026, 10, 1);
        var entries = new List<DsmEntry>();

        var nightRepaymentExplicit = new CreditorRepayment
        {
            Amount = 1200,
            PaymentMode = "Cash",
            ShiftNumber = "C",
            RepaymentDate = date
        };

        var nightRepaymentString = new CreditorRepayment
        {
            Amount = 800,
            PaymentMode = "Cash",
            ShiftNumber = "Shift C",
            RepaymentDate = date
        };

        var morningRepaymentExplicit = new CreditorRepayment
        {
            Amount = 500,
            PaymentMode = "Cash",
            ShiftNumber = "A",
            RepaymentDate = date
        };

        var repayments = new List<CreditorRepayment> { nightRepaymentExplicit, nightRepaymentString, morningRepaymentExplicit };

        // Calculate Shift C report
        var shiftCReport = _sut.CalculateShiftReport(
            date: date,
            shiftType: "C",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: repayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // Both Shift C repayments belong to Shift C (1200 + 800 = 2000)
        Assert.Equal(2000.0, shiftCReport.DebtorRepaymentsTotal, precision: 2);

        // Calculate Shift A report with same repayments -> must only capture morningRepaymentExplicit (500), never Shift C!
        var shiftAReport = _sut.CalculateShiftReport(
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

        Assert.Equal(500.0, shiftAReport.DebtorRepaymentsTotal, precision: 2);
    }
}

