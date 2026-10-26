using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.UI.ViewModels;
using Xunit;

namespace FuelPro.Tests;

/// <summary>
/// Stage 1: Financial Core & Reconciliation Validation Suite.
/// Covers:
/// 1. Cash 1 and Cash 2 denomination breakdown, zero counts, and calculation.
/// 2. Exclusion of Debtor & DSM recoveries from final Table F collection reconciliation.
/// 3. Separate Debtor Recovery and DSM Recovery totals.
/// 4. Collection Reduction in manual-entry mode (calculation, validation, Table F reconciliation, legacy default).
/// 5. Desktop and PWA single-layer canonical deduction parity.
/// </summary>
public class Stage1FinancialCoreTests
{
    private readonly DsmCalculationService _calcService = new();
    private readonly ShiftAggregationService _aggregation = new();
    private readonly ReportService _reportService;

    public Stage1FinancialCoreTests()
    {
        _reportService = new ReportService(_aggregation);
    }

    [Fact]
    public void CollectionReduction_DeductedExactlyOnce_FromCanonicalCollection()
    {
        // Gross sales: 10,000. Collections: CashDeposit 5,000 + PhonePe 5,000 = 10,000.
        // Reduction: 500 with remark "Shortage settlement discount".
        var entry = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 10000m } },
            PaymentCollection = new PaymentCollectionDto
            {
                CashDeposit = 5000m,
                PhonePe = 5000m,
                CollectionReduction = 500m,
                CollectionReductionRemark = "Shortage settlement discount"
            },
            DebitEntries = new List<DebitEntryDto>(),
            Expenses = new List<ExpenseDto>(),
            TestingEntries = new List<TestingEntryDto>()
        };

        var result = _calcService.Calculate(entry);

        // Total collection before reduction = 10,000. With reduction = 9,500.
        Assert.Equal(9500m, result.TotalCollection);
        Assert.Equal(-500m, result.Mismatch);
        Assert.False(result.IsBalanced);
        Assert.Equal(500m, result.CollectionReduction);
        Assert.Equal("Shortage settlement discount", result.CollectionReductionRemark);
    }

    [Fact]
    public void CollectionReduction_ZeroOrNull_PreservesLegacyBehavior()
    {
        var entry = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 10000m } },
            PaymentCollection = new PaymentCollectionDto
            {
                CashDeposit = 6000m,
                PhysicalCash = 4000m,
                CollectionReduction = 0m,
                CollectionReductionRemark = null
            },
            DebitEntries = new List<DebitEntryDto>(),
            Expenses = new List<ExpenseDto>(),
            TestingEntries = new List<TestingEntryDto>()
        };

        var result = _calcService.Calculate(entry);

        Assert.Equal(10000m, result.TotalCollection);
        Assert.Equal(0m, result.Mismatch);
        Assert.True(result.IsBalanced);
        Assert.Equal(0m, result.CollectionReduction);
    }

    [Fact]
    public void ReportService_TableF_ExcludesDebtorAndDsmRecoveries_FromReconciliationBreakdown()
    {
        var date = new DateTime(2026, 10, 15);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                DsmName = "Test DSM",
                PumpId = 1,
                GrossSales = 10000m,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 100.0, Amount = 10000.0, Rate = 100 }
                },
                PaymentCollection = new PaymentCollection
                {
                    CashDeposit = 6000,
                    PhonePeMorning = 4000
                },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash1", TotalAmount = 6000 }
                },
                DebitEntries = new List<DebitEntry>(),
                Expenses = new List<Expense>(),
                TestingEntries = new List<TestingEntry>()
            }
        };

        // Two recoveries: general debtor repayment (1,500) and DSM loss recovery (500)
        var repayments = new List<CreditorRepayment>
        {
            new CreditorRepayment { CreditorName = "Customer A", Amount = 1500, PaymentMode = "Cash", ShiftNumber = "B", RepaymentDate = date },
            new CreditorRepayment { CreditorName = "DSM Loss", Amount = 500, PaymentMode = "Cash", ShiftNumber = "B", RepaymentDate = date }
        };

        var report = _reportService.CalculateShiftReport(
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

        // 1. Recoveries are preserved in dedicated recovery registers
        Assert.Equal(2000.0, report.DebtorRepaymentsTotal);
        Assert.Equal(1500.0, report.DebtorRecoveriesTotal);
        Assert.Equal(500.0, report.DsmRecoveriesTotal);

        // 2. Table F collection breakdown: Amount must be pure sales and never inflated by recoveries
        var depositCat = report.CollectionBreakdown.First(c => c.Category.Contains("Cash Deposit"));
        Assert.Equal(6000.0, depositCat.Amount);
        Assert.Equal(6000.0, depositCat.BaseAmount);

        var phonePeCat = report.CollectionBreakdown.First(c => c.Category.Contains("PhonePe"));
        Assert.Equal(4000.0, phonePeCat.Amount);
        Assert.Equal(4000.0, phonePeCat.BaseAmount);

        var cashInHandCat = report.CollectionBreakdown.First(c => c.Category == "Cash In Hand");
        Assert.Equal(0.0, cashInHandCat.Amount);
        Assert.Equal(2000.0, cashInHandCat.RecoveryAmount); // Cash recoveries (1500 + 500) tracked here, but Amount is pure sales (0)
        Assert.DoesNotContain(report.CollectionBreakdown, c => c.Category == "Debtor Recovery" || c.Category == "DSM Recovery");

        // 3. ExpectedCollection is pure operational gross sales (10,000)
        Assert.Equal(10000.0, report.ExpectedCollection);

        // 4. ActualCollection is pure shift collection (10,000), not inflated by recoveries
        Assert.Equal(10000.0, report.ActualCollection);
        Assert.Equal(0.0, report.Difference);
    }

    [Fact]
    public void ReportService_CollectionReduction_AppearsAsDeductionInTableF_AdjustsActualCollectionOnce()
    {
        var date = new DateTime(2026, 10, 16);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 2,
                DsmName = "Test DSM",
                PumpId = 1,
                GrossSales = 10000m,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 100.0, Amount = 10000.0, Rate = 100 }
                },
                PaymentCollection = new PaymentCollection
                {
                    CashDeposit = 6000,
                    PhonePeMorning = 4000,
                    CollectionReduction = 750.0,
                    CollectionReductionRemark = "Manager Approved Reduction"
                },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash1", TotalAmount = 6000 }
                },
                DebitEntries = new List<DebitEntry>(),
                Expenses = new List<Expense>(),
                TestingEntries = new List<TestingEntry>()
            }
        };

        var report = _reportService.CalculateShiftReport(
            date: date,
            shiftType: "B",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: new List<CreditorRepayment>(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // Report properties reflect reduction
        Assert.Equal(750.0, report.CollectionReduction);
        Assert.Equal("Manager Approved Reduction", report.CollectionReductionRemark);

        // Table F must contain the Collection Reduction row
        var reductionRow = report.CollectionBreakdown.FirstOrDefault(c => c.Category == "Collection Reduction");
        Assert.NotNull(reductionRow);
        Assert.Equal(-750.0, reductionRow.Amount);
        Assert.Contains("Manager Approved Reduction", reductionRow.DescriptionWithBreakdown);

        // ActualCollection is reduced exactly once: 10,000 - 750 = 9,250
        Assert.Equal(9250.0, report.ActualCollection);
        Assert.Equal(10000.0, report.ExpectedCollection);
        // Collection reconciliation net difference reflects the reduction: 9,250 - 10,000 = -750
        Assert.Equal(-750.0, report.ActualCollection - report.ExpectedCollection);
    }

    [Fact]
    public void CashDenomRow_ComputesTotalAccurately_AndHandlesNullsAndZeros()
    {
        var row = new CashDenomRow { CashType = "Cash2" };
        Assert.Equal(0.0, row.TotalAmount);

        row.Denom500 = 10; // 5000
        row.Denom200 = 5;  // 1000
        row.Denom100 = 4;  // 400
        row.Denom50 = 0;   // 0
        row.Denom20 = null;// 0
        row.Denom10 = 2;   // 20
        row.Coins = 15;    // 15

        Assert.Equal(6435.0, row.TotalAmount, precision: 2);

        // Also test Cash1 (bank deposit) restricted to 500/200/100
        var rowCash1 = new CashDenomRow { CashType = "Cash1" };
        rowCash1.Denom500 = 4; // 2000
        rowCash1.Denom200 = 2; // 400
        rowCash1.Denom100 = 1; // 100
        Assert.Equal(2500.0, rowCash1.TotalAmount, precision: 2);

        // Clearing all
        row.Denom500 = null;
        row.Denom200 = null;
        row.Denom100 = null;
        row.Denom50 = null;
        row.Denom20 = null;
        row.Denom10 = null;
        row.Coins = null;

        Assert.Equal(0.0, row.TotalAmount);
    }

    [Fact]
    public void DsmEntryViewModel_CollectionReduction_ValidationRules()
    {
        // Arrange DsmEntryViewModel instance
        var vm = new DsmEntryViewModel();

        // 1. Initial defaults
        Assert.Null(vm.CollectionReduction);
        Assert.Null(vm.CollectionReductionRemark);

        // 2. Positive reduction without remark should fail validation
        vm.CollectionReduction = 500.0;
        vm.CollectionReductionRemark = "";
        var canSaveWithoutRemark = vm.ValidateReduction(out var err1);
        Assert.False(canSaveWithoutRemark);
        Assert.NotNull(err1);

        // 3. Positive reduction with whitespace remark should fail
        vm.CollectionReductionRemark = "   ";
        Assert.False(vm.ValidateReduction(out var err2));
        Assert.NotNull(err2);

        // 4. Positive reduction with meaningful remark should succeed
        vm.CollectionReductionRemark = "Authorized discount";
        Assert.True(vm.ValidateReduction(out var err3));
        Assert.Null(err3);

        // 5. Zero/null reduction with empty remark should succeed (legacy default)
        vm.CollectionReduction = 0.0;
        vm.CollectionReductionRemark = "";
        Assert.True(vm.ValidateReduction(out var err4));
        Assert.Null(err4);

        vm.CollectionReduction = null;
        Assert.True(vm.ValidateReduction(out var err5));
        Assert.Null(err5);

        // 6. Negative reduction should be rejected
        vm.CollectionReduction = -100.0;
        Assert.False(vm.ValidateReduction(out var err6));
        Assert.NotNull(err6);
    }

    [Fact]
    public async Task PaymentCollection_Sqlite_SaveAndReload_PersistsCollectionReduction()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_Stage1_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "stage1_reduction.db");

        try
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<FuelPro.Data.FuelProDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;

            using (var context = new FuelPro.Data.FuelProDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();

                // Setup shift and dsm entry
                var shift = new Shift { ShiftDate = DateTime.Today, ShiftType = "B" };
                context.Shifts.Add(shift);
                await context.SaveChangesAsync();

                var entry = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    DsmName = "Test DSM",
                    PumpId = 1,
                    GrossSales = 5000m
                };
                context.DsmEntries.Add(entry);
                await context.SaveChangesAsync();

                var payment = new PaymentCollection
                {
                    DsmEntryId = entry.DsmEntryId,
                    CashDeposit = 2000.0,
                    CollectionReduction = 350.0,
                    CollectionReductionRemark = "Management Shortage Waiver"
                };
                context.PaymentCollections.Add(payment);
                await context.SaveChangesAsync();
            }

            // Reload in fresh context and verify persistence
            using (var reloadContext = new FuelPro.Data.FuelProDbContext(options))
            {
                var loaded = await reloadContext.PaymentCollections
                    .Include(p => p.DsmEntry)
                    .FirstOrDefaultAsync(p => p.CollectionReduction > 0);

                Assert.NotNull(loaded);
                Assert.Equal(350.0, loaded.CollectionReduction);
                Assert.Equal("Management Shortage Waiver", loaded.CollectionReductionRemark);
                Assert.Equal(2000.0, loaded.CashDeposit);
            }
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void PwaPayload_Deserialization_Preserves_CollectionReduction_And_FormatVersion()
    {
        var pwaPayloadJson = @"{
            ""formatVersion"": 3,
            ""shiftDate"": ""2026-10-23"",
            ""shiftType"": ""B"",
            ""pumpId"": 1,
            ""dsmName"": ""Ramesh Jadhav"",
            ""collectionReduction"": 450.0,
            ""collectionReductionRemark"": ""Discount Approved By Manager"",
            ""cashDeposit"": 5000.0,
            ""dynamicItems"": [
                {
                    ""collectionTypeCode"": ""PINELABS_CARD"",
                    ""amount"": 1500.0,
                    ""tid"": ""TID001"",
                    ""batch"": ""BATCH01"",
                    ""slot"": ""Morning"",
                    ""transactionId"": ""TX-PWA-001""
                }
            ]
        }";

        var metadata = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(pwaPayloadJson);
        Assert.NotNull(metadata);

        int formatVersion = (int)metadata.formatVersion;
        double reduction = (double)metadata.collectionReduction;
        string? remark = (string?)metadata.collectionReductionRemark;
        double cashDeposit = (double)metadata.cashDeposit;

        Assert.Equal(3, formatVersion);
        Assert.Equal(450.0, reduction);
        Assert.Equal("Discount Approved By Manager", remark);
        Assert.Equal(5000.0, cashDeposit);

        // Convert to DsmEntryDto payment collection
        var paymentDto = new FuelPro.Core.DTOs.PaymentCollectionDto
        {
            CashDeposit = (decimal)cashDeposit,
            CollectionReduction = (decimal)reduction,
            CollectionReductionRemark = remark
        };

        Assert.Equal(450.0m, paymentDto.CollectionReduction);
        Assert.Equal("Discount Approved By Manager", paymentDto.CollectionReductionRemark);

        // Calculate and verify single-layer canonical deduction
        var calcService = new DsmCalculationService();
        var calcResult = calcService.Calculate(new DsmEntryDto
        {
            NozzleReadings = new List<FuelPro.Core.DTOs.NozzleReadingDto> { new() { Amount = 5000.0m } },
            PaymentCollection = paymentDto
        });

        // Expected collections: 5000 CashDeposit - 450 Reduction = 4550
        Assert.Equal(450.0m, calcResult.CollectionReduction);
        Assert.Equal("Discount Approved By Manager", calcResult.CollectionReductionRemark);
        Assert.Equal(4550.0m, calcResult.TotalCollection);
    }
}
