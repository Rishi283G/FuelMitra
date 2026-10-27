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
}
