using FuelPro.UI.ViewModels;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using Xunit;

namespace FuelPro.Tests;

public class ClientSiteBugFixesTests
{
    private readonly ShiftAggregationService _aggregationService;

    public ClientSiteBugFixesTests()
    {
        _aggregationService = new ShiftAggregationService();
    }

    [Fact]
    public void PetroCard_ItemizedTransactions_AggregatedIntoPetroCardTotal_AndAllocatedBySlot()
    {
        // Arrange: Shift A entry with itemized PetroCard transactions in PaymentCollection.Items
        var entry = new DsmEntry
        {
            DsmName = "Attendant 1",
            PumpId = 1,
            GrossSales = 5000,
            Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
            PaymentCollection = new PaymentCollection
            {
                // Pure itemized, scalar PetroCard is 0
                PetroCardMorning = 0,
                PetroCardNight = 0,
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 1200, Slot = "Morning", Tid = "TID01" },
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 800, Slot = "Night", Tid = "TID02" }
                }
            }
        };

        // Act
        var rows = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entry });

        // Assert
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal(1200, r.PetroCardMorning);
        Assert.Equal(800, r.PetroCardNight);
        Assert.Equal(2000, r.PetroCardTotal);
        Assert.Equal(2000, r.PetroCard);
    }

    [Fact]
    public void PetroCard_ScalarTransactions_PreservedWithoutRegression()
    {
        // Arrange: Purely scalar PetroCard entries (legacy or direct entry)
        var entry = new DsmEntry
        {
            DsmName = "Attendant 2",
            PumpId = 2,
            GrossSales = 3000,
            Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
            PaymentCollection = new PaymentCollection
            {
                PetroCardMorning = 500,
                PetroCardNight = 300,
                Items = new List<PaymentCollectionItem>()
            }
        };

        // Act
        var rows = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entry });

        // Assert
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal(500, r.PetroCardMorning);
        Assert.Equal(300, r.PetroCardNight);
        Assert.Equal(800, r.PetroCardTotal);
    }

    [Fact]
    public void PetroCard_MixedRepresentation_DoesNotDoubleCount()
    {
        // Arrange: Scalar PetroCard is set to 1500, and itemized items also total 1500 (mirroring)
        var entry = new DsmEntry
        {
            DsmName = "Attendant 3",
            PumpId = 1,
            GrossSales = 4000,
            Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
            PaymentCollection = new PaymentCollection
            {
                PetroCardMorning = 1500,
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 1500, Slot = "Morning", Tid = "TID99" }
                }
            }
        };

        // Act
        var rows = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entry });

        // Assert: Total must remain 1500, NOT double-counted to 3000
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal(1500, r.PetroCardTotal);
    }

    [Fact]
    public void PetroCard_ShiftB_AllocatesToDaySlot()
    {
        // Arrange: Shift B (Day shift)
        var entry = new DsmEntry
        {
            DsmName = "Attendant 4",
            PumpId = 3,
            GrossSales = 6000,
            Shift = new Shift { ShiftType = "B", ShiftDate = DateTime.Today },
            PaymentCollection = new PaymentCollection
            {
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 2500, Slot = "Day", Tid = "TID55" }
                }
            }
        };

        // Act
        var rows = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entry });

        // Assert
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal(2500, r.PetroCardDay);
        Assert.Equal(2500, r.PetroCardTotal);
    }

    [Fact]
    public void PineLabsCard_ItemizedTransactions_AggregatedIntoPineLabsCard()
    {
        // Arrange: Entry with itemized PINELABS_CARD transaction
        var entry = new DsmEntry
        {
            DsmName = "Attendant 5",
            PumpId = 1,
            GrossSales = 5000,
            Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
            PaymentCollection = new PaymentCollection
            {
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PINELABS_CARD", Amount = 1800, Slot = "Morning", Tid = "PL01" }
                }
            }
        };

        // Act
        var rows = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entry });

        // Assert
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal(1800, r.PineLabsCard);
    }

    [Fact]
    public void ShiftAndDayTotals_IncludePetroCard_FromItemizedItems()
    {
        // Arrange: Multiple entries across pumps
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmName = "Attendant A",
                PumpId = 1,
                GrossSales = 10000,
                Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
                PaymentCollection = new PaymentCollection
                {
                    Items = new List<PaymentCollectionItem>
                    {
                        new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 1500, Slot = "Morning" }
                    }
                }
            },
            new DsmEntry
            {
                DsmName = "Attendant B",
                PumpId = 2,
                GrossSales = 8000,
                Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
                PaymentCollection = new PaymentCollection
                {
                    Items = new List<PaymentCollectionItem>
                    {
                        new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 700, Slot = "Night" }
                    }
                }
            }
        };

        // Act
        var summaryRows = _aggregationService.BuildDsmSummaryRows(entries);
        var totalRow = _aggregationService.BuildDsmSummaryTotalRow(summaryRows);
        var shiftTotals = _aggregationService.BuildDsmShiftTotals(summaryRows);

        // Assert: Total PetroCard across both attendants is 2200
        Assert.Equal(2200, totalRow.PetroCard);
        Assert.Equal(1500, totalRow.PetroCardMorning);
        Assert.Equal(700, totalRow.PetroCardNight);

        Assert.Equal(2, shiftTotals.Count);
        Assert.Equal(2200, shiftTotals.Sum(s => s.PetroCard));
    }

    [Fact]
    public void CrossSectionDeduplication_PreservesDistinctTransactions_WithIdenticalAmount()
    {
        // Verify that an OTP transaction of 500 and a distinct manual TID transaction of 500
        // (with different TID / Batch) are both preserved and not falsely collapsed.
        var cardSwipeDetails = new List<DsmCardSwipeRow>
        {
            new DsmCardSwipeRow
            {
                Mode = "PINELABS_CARD",
                Amount = 500.0,
                Tid = "TID_OTP_1",
                Batch = "B001",
                Otp = "123456",
                TransactionId = "tx-otp-1"
            }
        };

        // Incoming manual TID transaction with SAME amount and SAME mode, but different TID/Batch
        string normDCode = "PINELABS_CARD";
        double dAmt = 500.0;
        string dTid = "TID_MANUAL_2";
        string dBatch = "B002";
        string? dTxId = "tx-manual-2";

        bool inOtp = cardSwipeDetails.Any(c =>
            (!string.IsNullOrWhiteSpace(c.TransactionId) && !string.IsNullOrWhiteSpace(dTxId) && string.Equals(c.TransactionId, dTxId, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(c.Otp) && string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normDCode, StringComparison.OrdinalIgnoreCase) && Math.Abs(c.Amount - dAmt) < 0.001
                && !string.IsNullOrWhiteSpace(c.Tid) && string.Equals(c.Tid.Trim(), dTid.Trim(), StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(c.Batch) && string.Equals(c.Batch.Trim(), dBatch.Trim(), StringComparison.OrdinalIgnoreCase)));

        Assert.False(inOtp, "Distinct manual TID transaction with same amount must NOT be identified as inOtp duplicate");
    }
}
