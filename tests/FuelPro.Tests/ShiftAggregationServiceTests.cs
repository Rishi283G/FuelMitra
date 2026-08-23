using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;

namespace FuelPro.Tests;

public class ShiftAggregationServiceTests
{
    private readonly ShiftAggregationService _sut;

    public ShiftAggregationServiceTests()
    {
        _sut = new ShiftAggregationService();
    }

    [Fact]
    public void BuildDsmSummaryRows_ShouldAggregateCashAndExpenses()
    {
        // Arrange
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmName = "DSM 1",
                PumpId = 1,
                GrossSales = 5000,
                PaymentCollection = new PaymentCollection { PhonePeMorning = 1000, CreditCardMorning = 500 },
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash1", TotalAmount = 1000 },
                    new CashDenomination { CashType = "Cash2", TotalAmount = 500 }
                },
                DebitEntries = new List<DebitEntry> { new DebitEntry { Amount = 1500 } },
                Expenses = new List<Expense> { new Expense { Amount = 500 } },
                TestingEntries = new List<TestingEntry>()
            }
        };

        // Act
        var rows = _sut.BuildDsmSummaryRows(entries);

        // Assert
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal("DSM 1", r.DsmName);
        Assert.Equal(1, r.PumpId);
        Assert.Equal(1000, r.PhonePe);
        Assert.Equal(500, r.CreditCardMorning);
        Assert.Equal(0, r.CreditCardNight);
        Assert.Equal(1000, r.CashDeposit);
        Assert.Equal(500, r.CashInHand);
        Assert.Equal(1500, r.Debit);
        Assert.Equal(500, r.Expenses);
        Assert.Equal(5000, r.GrossSales);
    }

    [Fact]
    public void AggregateCash_ShouldSumDenominationsAcrossDSMs()
    {
        // Arrange
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash1", Denom500 = 10, TotalAmount = 5000 },
                    new CashDenomination { CashType = "Cash2", Denom500 = 5, TotalAmount = 2500 }
                }
            },
            new DsmEntry
            {
                CashDenominations = new List<CashDenomination>
                {
                    new CashDenomination { CashType = "Cash1", Denom500 = 20, TotalAmount = 10000 }
                }
            }
        };

        // Act
        var agg = _sut.AggregateCash(entries, "Cash1");

        // Assert
        Assert.Equal(30, agg.Total500); // 10 + 20
        Assert.Equal(15000, agg.GrandTotal); // 5000 + 10000
    }

    [Fact]
    public void GetFuelTotals_WithOverrideRate_ShouldCalculateBasedOnOverride()
    {
        // Arrange
        var histShift = new Shift { ShiftDate = new DateTime(2026, 6, 20) };
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                PumpId = 3,
                Shift = histShift,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 9, FuelType = "HSD", SaleLitres = 100, Rate = 90, Amount = 9000 },
                    new NozzleReading { NozzleNumber = 8, FuelType = "MS-II", SaleLitres = 50, Rate = 100, Amount = 5000 }
                }
            }
        };

        // Act - No override
        var (l1, a1) = _sut.GetFuelTotals(entries, "HSD", null);
        Assert.Equal(100, l1);
        Assert.Equal(9000, a1);

        // Act - With override
        var (l2, a2) = _sut.GetFuelTotals(entries, "HSD", 95.0);
        Assert.Equal(100, l2);
        Assert.Equal(9500, a2); // 100 * 95
    }

    [Fact]
    public void BuildExpenseRows_ShouldCombineDsmAndShiftExpenses()
    {
        // Arrange
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmName = "DSM 1",
                PumpId = 1,
                Expenses = new List<Expense>
                {
                    new Expense { ExpenseId = 1, Description = "Tea", Amount = 50 }
                }
            }
        };
        var shiftExpenses = new List<Expense>
        {
            new Expense { ExpenseId = 2, Description = "Stationery", Amount = 150 }
        };

        // Act
        var rows = _sut.BuildExpenseRows(entries, shiftExpenses);

        // Assert
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => !r.IsShiftLevel && r.Amount == 50 && r.DsmName == "DSM 1");
        Assert.Contains(rows, r => r.IsShiftLevel && r.Amount == 150 && r.DsmName == "— Shift —");
    }

    [Fact]
    public void CalculateDifference_BalancedReconciliation_ShouldReturnZero()
    {
        // Arrange
        double grossFuel = 50000;
        double reconTotal = 50000;

        // Act
        var diff = _sut.CalculateDifference(grossFuel, reconTotal);

        // Assert
        Assert.Equal(0, diff);
    }

    [Fact]
    public void BuildDsmShiftTotals_ShouldGroupMultipleSessionsForSameDSM()
    {
        // Arrange
        var rows = new List<DsmSummaryRowDto>
        {
            new DsmSummaryRowDto
            {
                DsmName = "Ramesh",
                PumpId = 1,
                GrossSales = 10000,
                PhonePe = 2000,
                CashDeposit = 3000,
                CashInHand = 1000,
                Debit = 4000
            },
            new DsmSummaryRowDto
            {
                DsmName = "Suresh",
                PumpId = 1,
                GrossSales = 5000,
                PhonePe = 1000,
                CashDeposit = 2000,
                CashInHand = 500,
                Debit = 1500
            },
            new DsmSummaryRowDto
            {
                DsmName = "Ramesh",
                PumpId = 1,
                GrossSales = 8000,
                PhonePe = 1500,
                CashDeposit = 2500,
                CashInHand = 1000,
                Debit = 3000
            }
        };

        // Act
        var totals = _sut.BuildDsmShiftTotals(rows);

        // Assert
        Assert.Equal(2, totals.Count);
        var ramesh = totals.FirstOrDefault(t => t.DsmName == "Ramesh");
        Assert.NotNull(ramesh);
        Assert.Equal(2, ramesh!.SessionsCount);
        Assert.Equal(18000, ramesh.GrossSales); // 10000 + 8000
        Assert.Equal(3500, ramesh.PhonePe);    // 2000 + 1500
        Assert.Equal(5500, ramesh.CashDeposit); // 3000 + 2500
        Assert.Equal(2000, ramesh.CashInHand);  // 1000 + 1000
        Assert.Equal(7000, ramesh.Debit);       // 4000 + 3000
        Assert.Equal(18000, ramesh.TotalCollection);
        Assert.Equal(0, ramesh.Mismatch);
    }

    [Fact]
    public void GetFuelTotals_WithMultipleEntriesInSameShift_ShouldAggregateAllReadings()
    {
        // Arrange
        var shift = new Shift { ShiftId = 1, ShiftDate = new DateTime(2026, 6, 20), ShiftType = "A" };
        var entry1 = new DsmEntry
        {
            DsmEntryId = 1,
            ShiftId = 1,
            Shift = shift,
            PumpId = 1,
            NozzleReadings = new List<NozzleReading>
            {
                new NozzleReading { NozzleNumber = 1, SaleLitres = 10, Amount = 1000, Rate = 100 } // MS-I on Pump 1
            }
        };
        var entry2 = new DsmEntry
        {
            DsmEntryId = 2,
            ShiftId = 1,
            Shift = shift,
            PumpId = 2,
            NozzleReadings = new List<NozzleReading>
            {
                new NozzleReading { NozzleNumber = 6, SaleLitres = 20, Amount = 1800, Rate = 90 }   // HSD on Pump 2
            }
        };
        var entry3 = new DsmEntry
        {
            DsmEntryId = 3,
            ShiftId = 1,
            Shift = shift,
            PumpId = 1,
            NozzleReadings = new List<NozzleReading>
            {
                new NozzleReading { NozzleNumber = 1, SaleLitres = 5, Amount = 500, Rate = 100 }  // MS-I on Pump 1 (2nd session)
            }
        };
        var entry4 = new DsmEntry
        {
            DsmEntryId = 4,
            ShiftId = 1,
            Shift = shift,
            PumpId = 2,
            NozzleReadings = new List<NozzleReading>
            {
                new NozzleReading { NozzleNumber = 6, SaleLitres = 15, Amount = 1350, Rate = 90 }  // HSD on Pump 2 (2nd session)
            }
        };

        var entries = new List<DsmEntry> { entry1, entry2, entry3, entry4 };

        // Act
        var (msLitres, msAmount) = _sut.GetFuelTotals(entries, "MS-I", null);
        var (hsdLitres, hsdAmount) = _sut.GetFuelTotals(entries, "HSD", null);

        // Assert: MS-I must be 10 + 5 = 15 litres, 1000 + 500 = 1500 amount
        Assert.Equal(15, msLitres);
        Assert.Equal(1500, msAmount);

        // Assert: HSD must be 20 + 15 = 35 litres, 1800 + 1350 = 3150 amount
        Assert.Equal(35, hsdLitres);
        Assert.Equal(3150, hsdAmount);
    }
}
