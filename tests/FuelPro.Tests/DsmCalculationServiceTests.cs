using FuelPro.Core.DTOs;
using FuelPro.Core.Services;
using Microsoft.EntityFrameworkCore;

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
        Assert.Equal(-2000m, result.Mismatch);
        Assert.False(result.IsBalanced);
    }

    [Fact]
    public void ExpensesNotDeducted()
    {
        var result = _service.Calculate(BuildEntry(10000m, 5500m, 4000m, expenses: 500m));
        Assert.Equal(0m, result.Mismatch);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public void TestingNotDeducted()
    {
        var result = _service.Calculate(BuildEntry(10000m, 5800m, 4000m, testing: 200m));
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

    [Fact]
    public void InspectDatabase07Aug2026()
    {
        var dbPath = @"C:\Users\jadha\AppData\Local\FuelPro\fuelPro.db";
        if (!System.IO.File.Exists(dbPath)) return;

        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<FuelPro.Data.FuelProDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        using var context = new FuelPro.Data.FuelProDbContext(options);

        var shifts = context.Shifts.AsNoTracking().OrderByDescending(s => s.ShiftId).Take(20).ToList();
        System.Console.WriteLine("=== SHIFTS ===");
        foreach (var s in shifts)
        {
            System.Console.WriteLine($"ShiftId: {s.ShiftId} | Date: {s.ShiftDate:yyyy-MM-dd HH:mm} | Type: {s.ShiftType}");
        }

        var entries = context.DsmEntries
            .Include(e => e.Shift)
            .AsNoTracking()
            .OrderByDescending(e => e.DsmEntryId)
            .Take(20)
            .ToList();

        System.Console.WriteLine("=== DSM ENTRIES ===");
        foreach (var e in entries)
        {
            System.Console.WriteLine($"EntryId: {e.DsmEntryId} | ShiftId: {e.ShiftId} | ShiftDate: {e.Shift?.ShiftDate:yyyy-MM-dd} | ShiftType: {e.Shift?.ShiftType} | Pump: {e.PumpId} | DSM: {e.DsmName} | Gross: {e.GrossSales} | Mismatch: {e.Mismatch}");
        }

        var audits = context.DsmApprovalAudits
            .AsNoTracking()
            .OrderByDescending(a => a.DsmApprovalAuditId)
            .Take(20)
            .ToList();

        System.Console.WriteLine("=== APPROVAL AUDITS ===");
        foreach (var a in audits)
        {
            System.Console.WriteLine($"AuditId: {a.DsmApprovalAuditId} | SubId: {a.SubmissionId} | By: {a.ApprovedBy} | At: {a.ApprovedAt:yyyy-MM-dd HH:mm} | Json: {a.ApprovedDataJson}");
        }
    }
}
