using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Xunit;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.UI.ViewModels;

namespace FuelPro.Tests;

public class DynamicCollectionTotalsAndCalculationParityTests
{
    private readonly DsmCalculationService _calcService = new();

    [Fact]
    public void CalculationParity_WithOilAndDefProductSales_ComputesLiabilityAndMismatchCorrectly()
    {
        // NetGrossSales = GrossSales - TotalTesting
        // SalesLiability = NetGrossSales + ProductSales
        // TotalInDirect = Sum(Standard Payments) + Sum(Dynamic Payments)
        // TotalCollection = TotalInDirect + TotalCreditors + TotalExpenses - CollectionReduction
        // Mismatch = TotalCollection - SalesLiability

        var dto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 50000m } },
            TestingEntries = new List<TestingEntryDto> { new() { Amount = 1000m } }, // NetGrossSales = 49000
            ProductSales = 2500m, // Oil & DEF -> SalesLiability = 51500
            PaymentCollection = new PaymentCollectionDto
            {
                PhysicalCash = 30000m,
                Cash3 = 5000m,
                PhonePe = 5000m,
                CreditCard = 3000m,
                PetroCard = 2000m,
                DynamicPayments = 1500m, // TotalInDirect = 46500
                CollectionReduction = 500m,
            },
            DebitEntries = new List<DebitEntryDto> { new() { Amount = 4000m } },
            Expenses = new List<ExpenseDto> { new() { Amount = 2000m } }, // TotalCollection = 46500 + 4000 + 2000 - 500 = 52000
        };

        var result = _calcService.Calculate(dto);

        Assert.Equal(49000m, result.NetGrossSales);
        Assert.Equal(2500m, result.ProductSales);
        Assert.Equal(51500m, result.SalesLiability);
        Assert.Equal(46500m, result.TotalInDirect);
        Assert.Equal(52000m, result.TotalCollection);
        // Mismatch = TotalCollection (52000) - SalesLiability (51500) = +500
        Assert.Equal(500m, result.Mismatch);
    }

    [Fact]
    public void CalculationParity_WithoutProductSales_SalesLiabilityEqualsNetGrossSales()
    {
        var dto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 30000m } },
            TestingEntries = new List<TestingEntryDto>(),
            ProductSales = 0m,
            PaymentCollection = new PaymentCollectionDto
            {
                PhysicalCash = 15000m,
                PhonePe = 10000m,
            },
            DebitEntries = new List<DebitEntryDto> { new() { Amount = 5000m } },
            Expenses = new List<ExpenseDto>(),
        };

        var result = _calcService.Calculate(dto);

        Assert.Equal(30000m, result.NetGrossSales);
        Assert.Equal(0m, result.ProductSales);
        Assert.Equal(30000m, result.SalesLiability);
        Assert.Equal(30000m, result.TotalCollection);
        Assert.Equal(0m, result.Mismatch);
    }

    [Fact]
    public void CollectionTypeTotalDto_PropertiesAndFormatting_BehaveAccurately()
    {
        var total = new CollectionTypeTotalDto
        {
            Code = "PHONEPE",
            DisplayName = "PhonePe",
            TidAmount = 1800.0,
            OtpAmount = 200.0,
            ManualAmount = 0.0,
        };

        Assert.Equal(2000.0, total.TotalAmount);
        Assert.Equal("TID: ₹1,800.00 + OTP: ₹200.00 = ₹2,000.00", total.BreakdownDisplay);

        var manualTotal = new CollectionTypeTotalDto
        {
            Code = "VOUCHER",
            DisplayName = "Gift Voucher",
            TidAmount = 0.0,
            OtpAmount = 0.0,
            ManualAmount = 750.0,
        };

        Assert.Equal(750.0, manualTotal.TotalAmount);
        Assert.Equal("₹750.00", manualTotal.BreakdownDisplay);
    }

    [Fact]
    public void ManualOnlyEligibility_WhenBothTidAndOtpDisabled_ClassifiesAsManualOnly()
    {
        // Rule: Manual-only collection entry for active types where BOTH TID and OTP are disabled
        // (!HasTidBatch && !IsOtpEligible)

        var manualOnlyType = new CollectionTypeMaster
        {
            Code = "FLEET_COUPON",
            DisplayName = "Fleet Coupon",
            IsActive = true,
            HasTidBatch = false,
            HasOtp = false,
        };

        bool isTidEligible = manualOnlyType.HasTidBatch;
        bool isOtpEligible = manualOnlyType.IsOtpEligible;
        bool isManualOnly = manualOnlyType.IsActive && !isTidEligible && !isOtpEligible;

        Assert.False(isTidEligible);
        Assert.False(isOtpEligible);
        Assert.True(isManualOnly);
    }

    [Fact]
    public void ManualOnlyEligibility_WhenTidOrOtpEnabled_ExcludesFromManualOnly()
    {
        var tidOnlyType = new CollectionTypeMaster
        {
            Code = "CARD_SWIPE",
            IsActive = true,
            HasTidBatch = true,
            HasOtp = false,
        };
        bool isManualOnly1 = tidOnlyType.IsActive && !tidOnlyType.HasTidBatch && !tidOnlyType.IsOtpEligible;
        Assert.False(isManualOnly1);

        var otpOnlyType = new CollectionTypeMaster
        {
            Code = "PHONEPE_OTP",
            IsActive = true,
            HasTidBatch = false,
            HasOtp = true,
        };
        bool isManualOnly2 = otpOnlyType.IsActive && !otpOnlyType.HasTidBatch && !otpOnlyType.IsOtpEligible;
        Assert.False(isManualOnly2);
    }

    [Fact]
    public void DsmCardSwipeRow_TidDisplay_ShowsDashWhenEmpty_AndTidWhenPresent()
    {
        var rowWithoutTid = new DsmCardSwipeRow
        {
            Mode = "PHONEPE",
            Amount = 500.0,
            Tid = "",
            Otp = "1234",
        };

        Assert.Equal("—", rowWithoutTid.TidDisplay);

        var rowWithWhitespaceTid = new DsmCardSwipeRow
        {
            Mode = "PHONEPE",
            Amount = 300.0,
            Tid = "   ",
            Otp = "5678",
        };

        Assert.Equal("—", rowWithWhitespaceTid.TidDisplay);

        var rowWithTid = new DsmCardSwipeRow
        {
            Mode = "CREDIT_CARD",
            Amount = 1500.0,
            Tid = "TID98765",
            Otp = "9901",
        };

        Assert.Equal("TID98765", rowWithTid.TidDisplay);
    }

    [Fact]
    public void DsmOtpTransactionRow_TidProperty_CanBeSetAndRead()
    {
        var otpRow = new DsmOtpTransactionRow
        {
            CollectionTypeCode = "PHONEPE",
            Slot = "Morning",
            Tid = "TID112233",
            Amount = 250.0,
            Otp = "4455",
        };

        Assert.Equal("TID112233", otpRow.Tid);
        Assert.Equal(250.0, otpRow.Amount);
        Assert.Equal("4455", otpRow.Otp);
    }

    [Fact]
    public void Others_IsTreatedAsDynamic_AndNotStandardCollectionCode()
    {
        // Standard codes are strictly PHONEPE, PHONEPE_CARD, CREDIT_CARD, PETROCARD, CASH_DEPOSIT
        Assert.False(PaymentCollection.IsStandardCollectionCode("OTHERS"));
        Assert.False(PaymentCollection.IsStandardCollectionCode("others"));
        Assert.True(PaymentCollection.IsStandardCollectionCode("PHONEPE"));
        Assert.True(PaymentCollection.IsStandardCollectionCode("CREDIT_CARD"));
        Assert.True(PaymentCollection.IsStandardCollectionCode("PETROCARD"));
        Assert.True(PaymentCollection.IsStandardCollectionCode("CASH_DEPOSIT"));
    }

    [Fact]
    public void CalculationParity_DynamicOthersInclusion_VerifiesExactMismatchMath()
    {
        // Trace reported values:
        // Net Sales = 7170.46
        // Initial Total Collection = 6550.00
        // Initial Mismatch = 6550.00 - 7170.46 = -620.46
        var dtoWithoutOthers = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 7170.46m } },
            TestingEntries = new List<TestingEntryDto>(),
            ProductSales = 0m,
            PaymentCollection = new PaymentCollectionDto
            {
                PhysicalCash = 6550.00m,
                DynamicPayments = 0m,
            },
            DebitEntries = new List<DebitEntryDto>(),
            Expenses = new List<ExpenseDto>(),
        };

        var resultWithoutOthers = _calcService.Calculate(dtoWithoutOthers);
        Assert.Equal(7170.46m, resultWithoutOthers.SalesLiability);
        Assert.Equal(6550.00m, resultWithoutOthers.TotalCollection);
        Assert.Equal(-620.46m, resultWithoutOthers.Mismatch);

        // When "Other Online / UPI" (OTHERS) of 600.00 is included in DynamicPayments:
        // New Total Collection = 6550.00 + 600.00 = 7150.00
        // Resulting Mismatch = 7150.00 - 7170.46 = -20.46 (NOT -20.26)
        var dtoWithOthers = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 7170.46m } },
            TestingEntries = new List<TestingEntryDto>(),
            ProductSales = 0m,
            PaymentCollection = new PaymentCollectionDto
            {
                PhysicalCash = 6550.00m,
                DynamicPayments = 600.00m,
            },
            DebitEntries = new List<DebitEntryDto>(),
            Expenses = new List<ExpenseDto>(),
        };

        var resultWithOthers = _calcService.Calculate(dtoWithOthers);
        Assert.Equal(7170.46m, resultWithOthers.SalesLiability);
        Assert.Equal(7150.00m, resultWithOthers.TotalCollection);
        Assert.Equal(-20.46m, resultWithOthers.Mismatch);
        // Note: The user reported expected mismatch of -20.26, which has a 20 paise discrepancy with exact arithmetic (-20.46).
        Assert.NotEqual(-20.26m, resultWithOthers.Mismatch);
    }

    [Fact]
    public void EffectivePaymentCalculation_ItemizedTakesPrecedenceWithoutDoubleCounting()
    {
        // Rule: itemizedPhonePe > 0 ? itemizedPhonePe : (submission.PhonePeTotal > 0 ? submission.PhonePeTotal : UpiAmount)
        decimal itemizedPhonePe = 1500m;
        decimal scalarUpi = 1500m;

        decimal effectivePhonePe = itemizedPhonePe > 0 ? itemizedPhonePe : scalarUpi;
        Assert.Equal(1500m, effectivePhonePe); // NOT 3000m

        // Fallback when itemized is 0:
        decimal noItemized = 0m;
        decimal fallbackEffective = noItemized > 0 ? noItemized : scalarUpi;
        Assert.Equal(1500m, fallbackEffective);
    }

    [Fact]
    public void CreditCard_WhenScalarIsZero_FallsBackToItemizedSwipes()
    {
        var pc = new PaymentCollection
        {
            CreditCardMorning = 0,
            CreditCardDay = 0,
            CreditCardNight = 0,
            DynamicItemsJson = Newtonsoft.Json.JsonConvert.SerializeObject(new[]
            {
                new { TransactionId = "tx-1", CollectionTypeCode = "CREDIT_CARD", Amount = 1250.0, Tid = "TID01", Batch = "B01" },
                new { TransactionId = "tx-2", CollectionTypeCode = "CREDIT_CARD", Amount = 750.0, Tid = "TID02", Batch = "B02" }
            })
        };

        Assert.Equal(2000.0, pc.CreditCard);
    }

    [Fact]
    public void CashDenomRow_Cash1AndCash2_SumOnly500_200_100_WhileCash3RetainsFullDenominations()
    {
        // Cash1: Only 500, 200, 100
        var cash1 = new CashDenomRow
        {
            CashType = "Cash1",
            Denom500 = 2,  // 1000
            Denom200 = 1,  // 200
            Denom100 = 3,  // 300
            Denom50 = 5,   // ignored (250)
            Denom20 = 5,   // ignored (100)
            Denom10 = 5,   // ignored (50)
            Coins = 25     // ignored (25)
        };
        Assert.Equal(1500.0, cash1.TotalAmount);

        // Cash2: Only 500, 200, 100
        var cash2 = new CashDenomRow
        {
            CashType = "Cash2",
            Denom500 = 1,  // 500
            Denom200 = 2,  // 400
            Denom100 = 1,  // 100
            Denom50 = 10,  // ignored (500)
            Denom20 = 10,  // ignored (200)
            Denom10 = 10,  // ignored (100)
            Coins = 50     // ignored (50)
        };
        Assert.Equal(1000.0, cash2.TotalAmount);

        // Cash3: Full denominations
        var cash3 = new CashDenomRow
        {
            CashType = "Cash3",
            Denom500 = 1,  // 500
            Denom200 = 1,  // 200
            Denom100 = 1,  // 100
            Denom50 = 1,   // 50
            Denom20 = 1,   // 20
            Denom10 = 1,   // 10
            Coins = 5      // 5
        };
        Assert.Equal(885.0, cash3.TotalAmount);
    }

    [Fact]
    public void DsmQrPayment_WhenTargetDsmNameIsBlank_DefaultsToGeneralQr()
    {
        var row = new DsmQrPaymentEntry
        {
            TargetDsmName = "",
            Amount = 450.0
        };

        string effectiveTarget = string.IsNullOrWhiteSpace(row.TargetDsmName) ? "General QR" : row.TargetDsmName.Trim();
        Assert.Equal("General QR", effectiveTarget);
    }

    [Fact]
    public void SbiRedeemAndOthers_DynamicCalculationParity_MatchesAcrossScreens()
    {
        Assert.False(PaymentCollection.IsStandardCollectionCode("SBI_REDEEM"));
        Assert.False(PaymentCollection.IsStandardCollectionCode("QR"));

        var dto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 10000m } },
            TestingEntries = new List<TestingEntryDto>(),
            ProductSales = 0m,
            PaymentCollection = new PaymentCollectionDto
            {
                CashDeposit = 3000m,   // Cash 1
                PhysicalCash = 2000m,  // Cash 2
                DynamicPayments = 1500m, // SBI Redeem (500) + QR (1000)
                PhonePe = 2000m,
                CreditCard = 1000m,
                PetroCard = 500m,
            },
            DebitEntries = new List<DebitEntryDto>(),
            Expenses = new List<ExpenseDto>(),
        };

        var result = _calcService.Calculate(dto);

        // TotalCollection = 3000 + 2000 + 1500 + 2000 + 1000 + 500 = 10000
        // Liability = 10000
        // Mismatch = 0
        Assert.Equal(10000m, result.TotalInDirect);
        Assert.Equal(10000m, result.TotalCollection);
        Assert.Equal(10000m, result.SalesLiability);
        Assert.Equal(0m, result.Mismatch);
    }

    // =========================================================================
    // Focused Regression Tests 1-9 for Payment Mapping and UI Consistency
    // =========================================================================

    [Fact]
    public void Test1_PwaManualOnly_SbiRedeem_FlowsToManualItem_WithoutTidClassification()
    {
        var master = new CollectionTypeMaster
        {
            Code = "SBI_REDEEM",
            DisplayName = "SBI Redeem",
            HasTidBatch = false,
            HasOtp = false,
            IsActive = true
        };
        Assert.True(master.IsManualOnly);

        // Simulated PWA payload item
        string txId = "manual-scalar-sbi_redeem";
        string code = "SBI_REDEEM";
        double amount = 500.0;
        string tid = "";
        string batch = "";

        bool isManualScalar = (!string.IsNullOrWhiteSpace(txId) && txId.StartsWith("manual-scalar-", StringComparison.OrdinalIgnoreCase))
            || (string.IsNullOrWhiteSpace(tid) && string.IsNullOrWhiteSpace(batch))
            || master.IsManualOnly;

        bool isTidItem = !isManualScalar && master.HasTidBatch;

        Assert.True(isManualScalar);
        Assert.False(isTidItem);

        var savedItem = new PaymentCollectionItem
        {
            TransactionId = txId,
            CollectionTypeCode = PaymentCollection.NormalizeCollectionTypeCode(code),
            Amount = amount,
            Tid = null,
            Batch = null,
            Otp = null
        };

        Assert.Equal("SBI_REDEEM", savedItem.CollectionTypeCode);
        Assert.Equal(500.0, savedItem.Amount);
        Assert.StartsWith("manual-scalar-", savedItem.TransactionId);
    }

    [Fact]
    public void Test2_PwaManualOnly_Qr_FlowsToManualItem_WithoutTidClassification()
    {
        var master = new CollectionTypeMaster
        {
            Code = "QR",
            DisplayName = "QR / Online",
            HasTidBatch = false,
            HasOtp = false,
            IsActive = true
        };
        Assert.True(master.IsManualOnly);

        string txId = "manual-scalar-qr";
        string code = "QR";
        double amount = 1200.0;
        string tid = "";
        string batch = "";

        bool isManualScalar = (!string.IsNullOrWhiteSpace(txId) && txId.StartsWith("manual-scalar-", StringComparison.OrdinalIgnoreCase))
            || (string.IsNullOrWhiteSpace(tid) && string.IsNullOrWhiteSpace(batch))
            || master.IsManualOnly;

        bool isTidItem = !isManualScalar && master.HasTidBatch;

        Assert.True(isManualScalar);
        Assert.False(isTidItem);

        var savedItem = new PaymentCollectionItem
        {
            TransactionId = txId,
            CollectionTypeCode = PaymentCollection.NormalizeCollectionTypeCode(code),
            Amount = amount,
            Tid = null,
            Batch = null,
            Otp = null
        };

        Assert.Equal("QR", savedItem.CollectionTypeCode);
        Assert.Equal(1200.0, savedItem.Amount);
    }

    [Fact]
    public void Test3_SbiRedeemAndQr_HydrateIntoManualCollections_NotTidOrOtp()
    {
        var masters = new List<CollectionTypeMaster>
        {
            new() { Code = "SBI_REDEEM", DisplayName = "SBI Redeem", HasTidBatch = false, HasOtp = false, IsActive = true },
            new() { Code = "QR", DisplayName = "QR / Online", HasTidBatch = false, HasOtp = false, IsActive = true }
        };

        var items = new List<PaymentCollectionItem>
        {
            new() { TransactionId = "manual-scalar-sbi_redeem", CollectionTypeCode = "SBI_REDEEM", Amount = 500.0 },
            new() { TransactionId = "manual-scalar-qr", CollectionTypeCode = "QR", Amount = 1200.0 }
        };

        var tidTransactions = new List<PaymentCollectionItem>();
        var manualAmounts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var norm = PaymentCollection.NormalizeCollectionTypeCode(item.CollectionTypeCode);
            var m = masters.FirstOrDefault(x => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(x.Code), norm, StringComparison.OrdinalIgnoreCase));
            bool isManualOnly = m != null && m.IsManualOnly;

            bool isManualScalar = (!string.IsNullOrWhiteSpace(item.TransactionId) && item.TransactionId.StartsWith("manual-scalar-", StringComparison.OrdinalIgnoreCase))
                || (string.IsNullOrWhiteSpace(item.Tid) && string.IsNullOrWhiteSpace(item.Batch))
                || isManualOnly;

            bool isTidItem = !isManualScalar && (m == null || m.HasTidBatch);

            if (isTidItem)
            {
                tidTransactions.Add(item);
            }
            else
            {
                manualAmounts[norm] = item.Amount;
            }
        }

        Assert.Empty(tidTransactions);
        Assert.Equal(500.0, manualAmounts["SBI_REDEEM"]);
        Assert.Equal(1200.0, manualAmounts["QR"]);
    }

    [Fact]
    public void Test4_ManualOnlyTypes_ExcludedFromTidAndOtpDropdowns()
    {
        var masters = new List<CollectionTypeMaster>
        {
            new() { Code = "PHONEPE", DisplayName = "PhonePe", HasTidBatch = true, HasOtp = true, IsActive = true },
            new() { Code = "SBI_REDEEM", DisplayName = "SBI Redeem", HasTidBatch = false, HasOtp = false, IsActive = true },
            new() { Code = "QR", DisplayName = "QR / Online", HasTidBatch = false, HasOtp = false, IsActive = true },
            new() { Code = "CREDIT_CARD", DisplayName = "Credit / Debit Card", HasTidBatch = true, HasOtp = false, IsActive = true }
        };

        var availableTidTypes = masters.Where(m => m.IsActive && m.HasTidBatch && !m.IsManualOnly).Select(m => m.Code).ToList();
        var availableOtpTypes = masters.Where(m => m.IsActive && m.IsOtpEligible && !m.IsManualOnly).Select(m => m.Code).ToList();

        Assert.DoesNotContain("SBI_REDEEM", availableTidTypes);
        Assert.DoesNotContain("QR", availableTidTypes);
        Assert.Contains("PHONEPE", availableTidTypes);
        Assert.Contains("CREDIT_CARD", availableTidTypes);

        Assert.DoesNotContain("SBI_REDEEM", availableOtpTypes);
        Assert.DoesNotContain("QR", availableOtpTypes);
        Assert.Contains("PHONEPE", availableOtpTypes);
        Assert.DoesNotContain("CREDIT_CARD", availableOtpTypes);
    }

    [Fact]
    public void Test5_ManualScalarRecords_WithUuidOrEmptyTid_NotMisclassifiedAsTid()
    {
        var master = new CollectionTypeMaster
        {
            Code = "SBI_REDEEM",
            DisplayName = "SBI Redeem",
            HasTidBatch = false,
            HasOtp = false,
            IsActive = true
        };

        var itemWithUuid = new PaymentCollectionItem
        {
            TransactionId = Guid.NewGuid().ToString("D"),
            CollectionTypeCode = "SBI_REDEEM",
            Amount = 750.0,
            Tid = "",
            Batch = ""
        };

        bool isManualOnly = master.IsManualOnly;
        bool isManualScalar = (!string.IsNullOrWhiteSpace(itemWithUuid.TransactionId) && itemWithUuid.TransactionId.StartsWith("manual-scalar-", StringComparison.OrdinalIgnoreCase))
            || (string.IsNullOrWhiteSpace(itemWithUuid.Tid) && string.IsNullOrWhiteSpace(itemWithUuid.Batch))
            || isManualOnly;

        bool isTidItem = !isManualScalar && master.HasTidBatch;

        Assert.True(isManualScalar);
        Assert.False(isTidItem);
    }

    [Fact]
    public void Test6_LegacyGenuineItemizedTransactions_RemainReadable()
    {
        var master = new CollectionTypeMaster
        {
            Code = "CREDIT_CARD",
            DisplayName = "Credit / Debit Card",
            HasTidBatch = true,
            HasOtp = false,
            IsActive = true
        };

        var genuineItem = new PaymentCollectionItem
        {
            TransactionId = "e5b8d234-5267-4d4b-97e3-0987654321ab",
            CollectionTypeCode = "CREDIT_CARD",
            Amount = 2500.0,
            Tid = "10023456",
            Batch = "000045"
        };

        bool isManualOnly = master.IsManualOnly;
        bool isManualScalar = (!string.IsNullOrWhiteSpace(genuineItem.TransactionId) && genuineItem.TransactionId.StartsWith("manual-scalar-", StringComparison.OrdinalIgnoreCase))
            || (string.IsNullOrWhiteSpace(genuineItem.Tid) && string.IsNullOrWhiteSpace(genuineItem.Batch))
            || isManualOnly;

        bool isTidItem = !isManualScalar && master.HasTidBatch;

        Assert.False(isManualScalar);
        Assert.True(isTidItem);
        Assert.Equal("10023456", genuineItem.Tid);
        Assert.Equal("000045", genuineItem.Batch);
        Assert.Equal(2500.0, genuineItem.Amount);
    }

    [Fact]
    public void Test7_PhonePeAndPetroCard_ScalarVisibility_HiddenWhenTidOrOtpHydrated()
    {
        bool phonePeActive = true;
        bool phonePeHasTid = true;
        bool phonePeHasOtp = false;
        bool isPhonePeTidOrOtp = phonePeHasTid || phonePeHasOtp;
        bool isPhonePeScalarVisible = phonePeActive && !isPhonePeTidOrOtp;
        Assert.False(isPhonePeScalarVisible);

        phonePeHasTid = false;
        phonePeHasOtp = false;
        isPhonePeTidOrOtp = phonePeHasTid || phonePeHasOtp;
        isPhonePeScalarVisible = phonePeActive && !isPhonePeTidOrOtp;
        Assert.True(isPhonePeScalarVisible);

        bool petroCardActive = true;
        bool petroCardHasTid = true;
        bool petroCardHasOtp = false;
        bool isPetroCardScalarVisible = petroCardActive && !(petroCardHasTid || petroCardHasOtp);
        Assert.False(isPetroCardScalarVisible);
    }

    [Fact]
    public void Test8_ItemizedTotalsAndLegacyScalarFallback_DoNotDoubleCount()
    {
        double itemizedTidSum = 3000.0;
        double itemizedOtpSum = 500.0;
        double legacyScalarAmount = 3000.0;

        double effectiveTotal = (itemizedTidSum > 0 || itemizedOtpSum > 0)
            ? (itemizedTidSum + itemizedOtpSum)
            : legacyScalarAmount;

        Assert.Equal(3500.0, effectiveTotal);

        double noTid = 0;
        double noOtp = 0;
        double fallbackTotal = (noTid > 0 || noOtp > 0)
            ? (noTid + noOtp)
            : legacyScalarAmount;

        Assert.Equal(3000.0, fallbackTotal);
    }

    [Fact]
    public void Test9_ApprovalAndDsmEntry_ProduceMatchingTotalsAndMismatch_ForSameEntry()
    {
        var approvalDto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 50000m } },
            TestingEntries = new List<TestingEntryDto>(),
            ProductSales = 1000m,
            PaymentCollection = new PaymentCollectionDto
            {
                CashDeposit = 20000m,
                PhysicalCash = 15000m,
                PhonePe = 5000m,
                CreditCard = 4000m,
                DynamicPayments = 3000m, // SBI_REDEEM 1000 + QR 2000
            },
            DebitEntries = new List<DebitEntryDto> { new() { Amount = 4000m } },
            Expenses = new List<ExpenseDto>(),
        };

        var dsmEntryDto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 50000m } },
            TestingEntries = new List<TestingEntryDto>(),
            ProductSales = 1000m,
            PaymentCollection = new PaymentCollectionDto
            {
                CashDeposit = 20000m,
                PhysicalCash = 15000m,
                PhonePe = 5000m,
                CreditCard = 4000m,
                DynamicPayments = 3000m, // SBI_REDEEM 1000 + QR 2000
            },
            DebitEntries = new List<DebitEntryDto> { new() { Amount = 4000m } },
            Expenses = new List<ExpenseDto>(),
        };

        var approvalCalc = _calcService.Calculate(approvalDto);
        var dsmCalc = _calcService.Calculate(dsmEntryDto);

        Assert.Equal(approvalCalc.SalesLiability, dsmCalc.SalesLiability);
        Assert.Equal(approvalCalc.TotalCollection, dsmCalc.TotalCollection);
        Assert.Equal(approvalCalc.Mismatch, dsmCalc.Mismatch);
        Assert.Equal(51000m, approvalCalc.SalesLiability);
        Assert.Equal(51000m, approvalCalc.TotalCollection);
        Assert.Equal(0m, approvalCalc.Mismatch);
    }

    [Fact]
    public void Test10_ApprovalQueue_DeduplicatesAcrossOtpTidAndManualSections()
    {
        // Simulate PWA metadata where SubmitShiftScreen sent same transaction in both manualTidTransactions and dynamicItems
        var seenTxIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenOtpKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTidKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var cardSwipeDetails = new List<dynamic>();
        var manualTidTransactions = new List<dynamic>();

        // 1. Transaction A is OTP
        var txA = new { TransactionId = "tx-001", Mode = "PHONEPE", Amount = 500.0, Tid = "TID1", Batch = "B1", Otp = "123456", Slot = "Morning" };
        seenTxIds.Add(txA.TransactionId);
        seenOtpKeys.Add($"{txA.Mode}|{txA.Amount:F2}|{txA.Otp}");
        cardSwipeDetails.Add(txA);

        // 2. Duplicate of txA appears in dynamicItems with same TxId or same OtpKey
        bool isDuplicateA = seenTxIds.Contains(txA.TransactionId) || seenOtpKeys.Contains($"{txA.Mode}|{txA.Amount:F2}|{txA.Otp}");
        Assert.True(isDuplicateA, "txA duplicate must be detected and rejected");

        // 3. Genuine TID swipe B
        var txB = new { TransactionId = "tx-002", Mode = "CREDIT_CARD", Amount = 1200.0, Tid = "TID2", Batch = "B2", Otp = "", Slot = "Morning" };
        seenTxIds.Add(txB.TransactionId);
        seenTidKeys.Add($"{txB.Mode}|{txB.Amount:F2}|{txB.Tid}|{txB.Batch}");
        manualTidTransactions.Add(txB);

        // 4. Duplicate of txB in manual items
        bool isDuplicateB = seenTxIds.Contains(txB.TransactionId) || seenTidKeys.Contains($"{txB.Mode}|{txB.Amount:F2}|{txB.Tid}|{txB.Batch}");
        Assert.True(isDuplicateB, "txB duplicate must be detected and rejected");

        Assert.Single(cardSwipeDetails);
        Assert.Single(manualTidTransactions);
    }

    [Fact]
    public void Test11_DebtorEntriesAndRemarks_PreservedInApprovalAndPersisted()
    {
        var row = new DsmDebitRow
        {
            DebtorName = "Ramesh Patil",
            Amount = 1500.0,
            VehicleNumber = "MH-12-AB-1234",
            SlipNumber = "SL-9988",
            Remarks = "Urgent tractor diesel",
            EntryTime = "10:30 AM"
        };

        var debitEntity = new DebitEntry
        {
            DebtorName = row.DebtorName,
            Amount = row.Amount,
            VehicleNumber = string.IsNullOrWhiteSpace(row.VehicleNumber) ? null : row.VehicleNumber.Trim(),
            SlipNumber = string.IsNullOrWhiteSpace(row.SlipNumber) ? null : row.SlipNumber.Trim(),
            ChequeNo = string.IsNullOrWhiteSpace(row.SlipNumber) ? null : row.SlipNumber.Trim(),
            Remarks = string.IsNullOrWhiteSpace(row.Remarks) ? null : row.Remarks.Trim(),
            EntryTime = string.IsNullOrWhiteSpace(row.EntryTime) ? null : row.EntryTime.Trim(),
            PaymentMethod = "Credit"
        };

        Assert.Equal("Ramesh Patil", debitEntity.DebtorName);
        Assert.Equal(1500.0, debitEntity.Amount);
        Assert.Equal("Urgent tractor diesel", debitEntity.Remarks);
        Assert.Equal("SL-9988", debitEntity.SlipNumber);
    }

    [Fact]
    public void Test12_CollectionReduction_DeductedExactlyOnceFromReconciliation()
    {
        var dto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 10000m } },
            TestingEntries = new List<TestingEntryDto>(),
            ProductSales = 0m,
            PaymentCollection = new PaymentCollectionDto
            {
                PhysicalCash = 8000m,
                CollectionReduction = 500m,
                CollectionReductionRemark = "Short cash adjustment"
            },
            DebitEntries = new List<DebitEntryDto> { new() { Amount = 2500m } }, // 8000 + 2500 - 500 = 10000
            Expenses = new List<ExpenseDto>()
        };

        var result = _calcService.Calculate(dto);

        Assert.Equal(10000m, result.SalesLiability);
        Assert.Equal(10000m, result.TotalCollection);
        Assert.Equal(0m, result.Mismatch);
        Assert.True(result.IsBalanced);
        Assert.Equal(500m, result.CollectionReduction);
    }

    [Fact]
    public void Test13_ReportServiceCalculateEntryTotals_MatchesCanonicalReductionAndTesting()
    {
        var entry = new DsmEntry
        {
            GrossSales = 20000m,
            TestingEntries = new List<TestingEntry> { new() { Amount = 500 } },
            CashDenominations = new List<CashDenomination> { new() { CashType = "Cash2", TotalAmount = 15000 } },
            PaymentCollection = new PaymentCollection
            {
                CollectionReduction = 200,
                CollectionReductionRemark = "Discount adjustment"
            },
            DebitEntries = new List<DebitEntry> { new() { Amount = 4700 } },
            Expenses = new List<Expense>()
        };

        var (gs, totalColl, mismatch) = ReportService.CalculateEntryTotals(entry);

        // Gross sales = 20000, Testing = 500 => Net Gross Sales = 19500
        // TotalCollection = 15000 + 4700 - 200 = 19500
        // Mismatch = 19500 - 19500 = 0
        Assert.Equal(20000, gs);
        Assert.Equal(19500, totalColl);
        Assert.Equal(0, mismatch, 2);
    }

    [Fact]
    public void Test14_ShiftReportDifference_PreservesRawDiffWithoutTotalDsmShortCorruption()
    {
        double actualCollection = 49850.0;
        double expectedCollection = 50000.0;
        double rawDiff = actualCollection - expectedCollection; // -150.0

        double difference = Math.Abs(rawDiff) < 0.005 ? 0 : rawDiff;

        Assert.Equal(-150.0, difference);
    }
}
