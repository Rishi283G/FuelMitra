using FuelPro.Core.DTOs;
using FuelPro.Core.Services;

namespace FuelPro.Tests;

public class DsmCalculationServiceTests
{
    private readonly DsmCalculationService _service = new();

    [Fact]
    public void BalancedEntry()
    {
        var result = _service.Calculate(BuildEntry(10000m, 6000m, 4000m));
        Assert.Equal(10000m, result.TotalCollection);
        Assert.Equal(0m, result.Mismatch);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public void ShortEntry()
    {
        var result = _service.Calculate(BuildEntry(10000m, 5000m, 3000m));
        Assert.Equal(2000m, result.Mismatch);
        Assert.False(result.IsBalanced);
    }

    [Fact]
    public void ExpensesNotDeducted()
    {
        var result = _service.Calculate(BuildEntry(10000m, 6000m, 4000m, expenses: 500m));
        Assert.Equal(0m, result.Mismatch);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public void TestingNotDeducted()
    {
        var result = _service.Calculate(BuildEntry(10000m, 6000m, 4000m, testing: 200m));
        Assert.Equal(0m, result.Mismatch);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public void NegativeSaleBlocked()
    {
        var ex = Assert.Throws<ArgumentException>(() => DsmCalculationService.EnsureNozzleReadingValid(200m, 100m));
        Assert.Equal("Closing cannot be less than opening", ex.Message);
    }

    private static DsmEntryDto BuildEntry(decimal grossSales, decimal totalIn, decimal creditors, decimal expenses = 0m, decimal testing = 0m)
    {
        return new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = grossSales } },
            PaymentCollection = new PaymentCollectionDto
            {
                PhonePe = totalIn,
                CreditCard = 0,
                CashDeposit = 0,
                PhysicalCash = 0
            },
            DebitEntries = new List<DebitEntryDto> { new() { Amount = creditors } },
            Expenses = new List<ExpenseDto> { new() { Amount = expenses } },
            TestingEntries = new List<TestingEntryDto> { new() { Amount = testing } }
        };
    }
}
