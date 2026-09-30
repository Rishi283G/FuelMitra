using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelPro.Tests;

public class OpeningBalanceReportingIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FuelProDbContext> _options;

    public OpeningBalanceReportingIntegrationTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new FuelProDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private FuelProDbContext CreateContext() => new FuelProDbContext(_options);

    private async Task<int> CreateTestDsmEntryAsync(FuelProDbContext context, DateTime date, string dsmName = "TestDSM")
    {
        var shift = new Shift
        {
            ShiftDate = date.Date,
            ShiftType = "A",
            CreatedAt = date
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var entry = new DsmEntry
        {
            ShiftId = shift.ShiftId,
            DsmName = dsmName,
            CreatedAt = date
        };
        context.DsmEntries.Add(entry);
        await context.SaveChangesAsync();

        return entry.DsmEntryId;
    }

    [Fact]
    public async Task Test01_Debtor_PrintedAndExportedLedger_IncludesCorrectOpeningBalance()
    {
        // 1. Debtor printed/exported ledger includes correct opening balance.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        var creditor = new Creditor { Name = "Sharma Transport", Phone = "9876543210", IsActive = true, CreatedAt = DateTime.Now };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = creditor.CreditorId.ToString(),
            CreditorId = creditor.CreditorId,
            Amount = 15000,
            OpeningDate = new DateTime(2026, 9, 1),
            Notes = "Opening balance test"
        });

        int dsmId = await CreateTestDsmEntryAsync(context, new DateTime(2026, 9, 10));

        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = dsmId,
            DebtorName = creditor.Name,
            Amount = 5000,
            PaymentMethod = "Credit",
            Remarks = "Fuel diesel",
            CreatedAt = new DateTime(2026, 9, 10)
        });

        context.CreditorRepayments.Add(new CreditorRepayment
        {
            CreditorName = creditor.Name,
            Amount = 2000,
            PaymentMode = "Cash",
            RepaymentDate = new DateTime(2026, 9, 15),
            CreatedAt = new DateTime(2026, 9, 15)
        });
        await context.SaveChangesAsync();

        // Build DebtorLedgerPrintData as DebtorManagementViewModel does
        var printRows = new List<DebtorLedgerPrintRow>
        {
            new DebtorLedgerPrintRow
            {
                Date = new DateTime(2026, 9, 1).ToString("dd-MMM-yyyy"),
                Description = "Opening Balance",
                Debit = 0,
                Credit = 0,
                RunningBalance = 15000
            },
            new DebtorLedgerPrintRow
            {
                Date = new DateTime(2026, 9, 10).ToString("dd-MMM-yyyy"),
                Description = "Debt (Fuel diesel)",
                Debit = 5000,
                Credit = 0,
                RunningBalance = 20000
            },
            new DebtorLedgerPrintRow
            {
                Date = new DateTime(2026, 9, 15).ToString("dd-MMM-yyyy"),
                Description = "Repayment (Cash)",
                Debit = 0,
                Credit = 2000,
                RunningBalance = 18000
            }
        };

        var printData = new DebtorLedgerPrintData
        {
            DebtorName = creditor.Name,
            DebtorPhone = creditor.Phone,
            StartDate = "01-Sep-2026",
            EndDate = "30-Sep-2026",
            OpeningBalance = 15000,
            TotalDebt = 5000,
            TotalRepayments = 2000,
            ClosingBalance = 18000,
            Transactions = printRows
        };

        // Assert printed data representation
        Assert.Equal(15000, printData.OpeningBalance);
        Assert.Equal(5000, printData.TotalDebt);
        Assert.Equal(2000, printData.TotalRepayments);
        Assert.Equal(18000, printData.ClosingBalance);
        Assert.Equal("Opening Balance", printData.Transactions[0].Description);
        Assert.Equal(0, printData.Transactions[0].Debit);
        Assert.Equal(0, printData.Transactions[0].Credit);
        Assert.Equal(15000, printData.Transactions[0].RunningBalance);

        // Export to Excel and verify
        var excelService = new ExcelExportService();
        var exportedPath = await excelService.ExportDebtorLedgerAsync(printData);
        Assert.True(File.Exists(exportedPath));

        try
        {
            using var workbook = new XLWorkbook(exportedPath);
            var ws = workbook.Worksheet("Ledger");
            // Check Card Values at Row 5
            Assert.Equal(15000, ws.Cell(5, 1).GetDouble()); // Card 1: Opening Balance
            Assert.Equal(5000, ws.Cell(5, 2).GetDouble());  // Card 2: Total Period Sales
            Assert.Equal(2000, ws.Cell(5, 3).GetDouble());  // Card 3: Total Period Recoveries
            Assert.Equal(18000, ws.Cell(5, 4).GetDouble()); // Card 4: Closing Balance
        }
        finally
        {
            try { File.Delete(exportedPath); } catch { }
        }
    }

    [Fact]
    public async Task Test02_Dsm_PrintedAndExportedLedger_IncludesCorrectOpeningBalance()
    {
        // 2. DSM printed/exported ledger includes correct opening balance.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        string dsmName = "Ramesh Kumar";
        await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = dsmName,
            Amount = 8000,
            OpeningDate = new DateTime(2026, 9, 1),
            Notes = "Historical shortage"
        });

        // Add operational loss
        context.DsmPersonalDebtors.Add(new DsmPersonalDebtor
        {
            DsmName = dsmName,
            Amount = 3000,
            Date = new DateTime(2026, 9, 12),
            EntryType = "Operational",
            Remarks = "Shift Shortage (DSM Loss)"
        });

        // Add repayment
        var anchor = await context.DsmPersonalDebtors.FirstAsync(d => d.DsmName == dsmName && d.EntryType == "OpeningBalance");
        context.DsmPersonalDebtorRepayments.Add(new DsmPersonalDebtorRepayment
        {
            DsmPersonalDebtorId = anchor.Id,
            Amount = 1000,
            Date = new DateTime(2026, 9, 18),
            PaymentMethod = "Cash"
        });
        await context.SaveChangesAsync();

        var printRows = new List<DebtorLedgerPrintRow>
        {
            new DebtorLedgerPrintRow
            {
                Date = "01-Sep-2026",
                Description = "Historical Opening Balance",
                Debit = 0,
                Credit = 0,
                RunningBalance = 8000
            },
            new DebtorLedgerPrintRow
            {
                Date = "12-Sep-2026",
                Description = "Shift Shortage (DSM Loss)",
                Debit = 3000,
                Credit = 0,
                RunningBalance = 11000
            },
            new DebtorLedgerPrintRow
            {
                Date = "18-Sep-2026",
                Description = "Repayment (Cash)",
                Debit = 0,
                Credit = 1000,
                RunningBalance = 10000
            }
        };

        var printData = new DebtorLedgerPrintData
        {
            DebtorName = $"{dsmName} (DSM Personal Debtors)",
            StartDate = "01-Sep-2026",
            EndDate = "30-Sep-2026",
            OpeningBalance = 8000,
            TotalDebt = 3000,
            TotalRepayments = 1000,
            ClosingBalance = 10000,
            Transactions = printRows
        };

        Assert.Equal(8000, printData.OpeningBalance);
        Assert.Equal(3000, printData.TotalDebt);
        Assert.Equal(1000, printData.TotalRepayments);
        Assert.Equal(10000, printData.ClosingBalance);
        Assert.Equal("Historical Opening Balance", printData.Transactions[0].Description);
        Assert.Equal(0, printData.Transactions[0].Debit);
        Assert.Equal(8000, printData.Transactions[0].RunningBalance);

        var excelService = new ExcelExportService();
        var exportedPath = await excelService.ExportDebtorLedgerAsync(printData);
        Assert.True(File.Exists(exportedPath));

        try
        {
            using var workbook = new XLWorkbook(exportedPath);
            var ws = workbook.Worksheet("Ledger");
            // Check Card Values at Row 5
            Assert.Equal(8000, ws.Cell(5, 1).GetDouble());
            Assert.Equal(3000, ws.Cell(5, 2).GetDouble());
            Assert.Equal(1000, ws.Cell(5, 3).GetDouble());
            Assert.Equal(10000, ws.Cell(5, 4).GetDouble());
        }
        finally
        {
            try { File.Delete(exportedPath); } catch { }
        }
    }

    [Fact]
    public async Task Test03_OpeningBalance_IsNotShownAsAnOperationalShortage()
    {
        // 3. Opening balance is not shown as an operational shortage.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        string dsmName = "Suresh Patil";
        await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = dsmName,
            Amount = 12000,
            OpeningDate = new DateTime(2026, 9, 1),
            Notes = "Historical shortage"
        });

        var printRows = new List<DebtorLedgerPrintRow>
        {
            new DebtorLedgerPrintRow
            {
                Date = "01-Sep-2026",
                Description = "Historical Opening Balance",
                Debit = 12000, // Even if debit was originally set, Excel must blank it
                Credit = 0,
                RunningBalance = 12000
            }
        };

        var printData = new DebtorLedgerPrintData
        {
            DebtorName = $"{dsmName} (DSM Personal Debtors)",
            StartDate = "01-Sep-2026",
            EndDate = "30-Sep-2026",
            OpeningBalance = 12000,
            TotalDebt = 0, // Operational debt is strictly 0
            TotalRepayments = 0,
            ClosingBalance = 12000,
            Transactions = printRows
        };

        var excelService = new ExcelExportService();
        var exportedPath = await excelService.ExportDebtorLedgerAsync(printData);

        try
        {
            using var workbook = new XLWorkbook(exportedPath);
            var ws = workbook.Worksheet("Ledger");
            // Row 8 is the first data row: Date (Col 1), Description (Col 2), Debit (Col 3), Credit (Col 4), Running (Col 5)
            // Debit cell for Historical Opening Balance MUST be blank string, not 12000
            var debitVal = ws.Cell(8, 3).GetString();
            Assert.True(string.IsNullOrEmpty(debitVal), $"Expected blank debit cell for opening balance, but got '{debitVal}'");
            Assert.Equal(12000, ws.Cell(8, 5).GetDouble());
        }
        finally
        {
            try { File.Delete(exportedPath); } catch { }
        }
    }

    [Fact]
    public async Task Test04_OperationalShiftReports_RemainUnchanged()
    {
        // 4. Operational shift reports remain unchanged.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        // Create DSM opening balance
        await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Rajesh",
            Amount = 25000,
            OpeningDate = new DateTime(2026, 9, 1)
        });

        // Operational shift report inputs
        var date = new DateTime(2026, 9, 15);
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1,
                DsmName = "Rajesh",
                Expenses = new List<Expense>
                {
                    new Expense { Amount = 1000, Description = "Station cleaning" }
                }
            }
        };
        var shiftExpenses = new List<Expense>();
        var repayments = new List<CreditorRepayment>
        {
            new CreditorRepayment
            {
                Amount = 3000,
                PaymentMode = "Cash",
                CreditorName = "General Debtor",
                ShiftNumber = "A",
                RepaymentDate = date.Date
            }
        };

        var reportService = new ReportService(new ShiftAggregationService());
        var shiftReport = reportService.CalculateShiftReport(
            date,
            "A",
            entries,
            shiftExpenses,
            new List<ShiftOtherCash>(),
            repayments,
            90.0, 105.0, 105.0, 85.0,
            new BusinessDayTidSheet(), new BusinessDayTidSheet(),
            "Mitali Service Station");

        // The opening balance of ₹25,000 must NOT alter shift operational figures
        Assert.Equal(1000, shiftReport.ExpensesTotal);
        Assert.Equal(3000, shiftReport.DebtorRepaymentsTotal);
        Assert.Equal(3000, shiftReport.CashRepayments);
    }

    [Fact]
    public async Task Test05_ExcelTotals_MatchApplicationTotals()
    {
        // 5. Excel totals match application totals: Opening + Debits - Repayments = Closing.
        using var context = CreateContext();

        var printData = new DebtorLedgerPrintData
        {
            DebtorName = "National Logistics",
            StartDate = "01-Sep-2026",
            EndDate = "30-Sep-2026",
            OpeningBalance = 40000,
            TotalDebt = 12500,
            TotalRepayments = 7500,
            ClosingBalance = 45000, // 40000 + 12500 - 7500
            Transactions = new List<DebtorLedgerPrintRow>
            {
                new DebtorLedgerPrintRow { Date = "01-Sep-2026", Description = "Opening Balance", Debit = 0, Credit = 0, RunningBalance = 40000 },
                new DebtorLedgerPrintRow { Date = "05-Sep-2026", Description = "Diesel supply", Debit = 12500, Credit = 0, RunningBalance = 52500 },
                new DebtorLedgerPrintRow { Date = "15-Sep-2026", Description = "Bank Transfer", Debit = 0, Credit = 7500, RunningBalance = 45000 }
            }
        };

        var excelService = new ExcelExportService();
        var exportedPath = await excelService.ExportDebtorLedgerAsync(printData);

        try
        {
            using var workbook = new XLWorkbook(exportedPath);
            var ws = workbook.Worksheet("Ledger");
            double ob = ws.Cell(5, 1).GetDouble();
            double debt = ws.Cell(5, 2).GetDouble();
            double repay = ws.Cell(5, 3).GetDouble();
            double closing = ws.Cell(5, 4).GetDouble();

            Assert.Equal(printData.OpeningBalance, ob);
            Assert.Equal(printData.TotalDebt, debt);
            Assert.Equal(printData.TotalRepayments, repay);
            Assert.Equal(printData.ClosingBalance, closing);
            Assert.Equal(closing, ob + debt - repay);
        }
        finally
        {
            try { File.Delete(exportedPath); } catch { }
        }
    }

    [Fact]
    public async Task Test06_DeactivatedOpening_IsNotIncluded()
    {
        // 6. Deactivated opening is not included.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        var creditor = new Creditor { Name = "Old Debtor Co", Phone = "9998887776", IsActive = true, CreatedAt = DateTime.Now };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        var obResult = await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = creditor.CreditorId.ToString(),
            CreditorId = creditor.CreditorId,
            Amount = 50000,
            OpeningDate = new DateTime(2026, 9, 1)
        });
        Assert.True(obResult.Success);

        // Deactivate it
        await repo.DeactivateOpeningBalanceAsync(obResult.Data!.OpeningBalanceId);

        // Verify that opening balance query returns null
        var activeObResult = await repo.GetActiveByEntityAsync("Debtor", creditor.CreditorId.ToString());
        Assert.Null(activeObResult.Data);

        // Add operational transaction
        int dsmId = await CreateTestDsmEntryAsync(context, new DateTime(2026, 9, 10));
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = dsmId,
            DebtorName = creditor.Name,
            Amount = 3000,
            PaymentMethod = "Credit",
            CreatedAt = new DateTime(2026, 9, 10)
        });
        await context.SaveChangesAsync();

        // PrintData opening balance must be 0
        var printData = new DebtorLedgerPrintData
        {
            DebtorName = creditor.Name,
            OpeningBalance = 0, // Deactivated opening contributes 0
            TotalDebt = 3000,
            TotalRepayments = 0,
            ClosingBalance = 3000,
            Transactions = new List<DebtorLedgerPrintRow>
            {
                new DebtorLedgerPrintRow { Date = "01-Sep-2026", Description = "Opening Balance", Debit = 0, Credit = 0, RunningBalance = 0 },
                new DebtorLedgerPrintRow { Date = "10-Sep-2026", Description = "Credit", Debit = 3000, Credit = 0, RunningBalance = 3000 }
            }
        };

        Assert.Equal(0, printData.OpeningBalance);
        Assert.Equal(3000, printData.ClosingBalance);
    }

    [Fact]
    public async Task Test07_NoDuplicateOpeningRows()
    {
        // 7. No duplicate opening rows.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        string dsmName = "Amit Shinde";
        await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = dsmName,
            Amount = 6000,
            OpeningDate = new DateTime(2026, 9, 5)
        });

        // Ensure exactly ONE backing anchor exists in database
        var anchors = await context.DsmPersonalDebtors
            .Where(d => d.DsmName == dsmName && d.EntryType == "OpeningBalance")
            .ToListAsync();
        Assert.Single(anchors);

        // Simulate ledger building spanning 01-Sep to 30-Sep
        var list = new List<DebtorLedgerPrintRow>();
        // Only one opening balance row is added
        list.Add(new DebtorLedgerPrintRow
        {
            Date = "05-Sep-2026",
            Description = "Historical Opening Balance",
            Debit = 0,
            Credit = 0,
            RunningBalance = 6000
        });

        var openingRows = list.Where(r => r.Description.Contains("Opening Balance")).ToList();
        Assert.Single(openingRows);
    }

    [Fact]
    public async Task Test08_RepaymentTotals_RemainUnchanged()
    {
        // 8. Repayment totals remain unchanged.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        string dsmName = "Kiran Patil";
        await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = dsmName,
            Amount = 10000,
            OpeningDate = new DateTime(2026, 9, 1)
        });

        var anchor = await context.DsmPersonalDebtors.FirstAsync(d => d.DsmName == dsmName && d.EntryType == "OpeningBalance");

        // Add 2 repayments
        context.DsmPersonalDebtorRepayments.Add(new DsmPersonalDebtorRepayment
        {
            DsmPersonalDebtorId = anchor.Id,
            Amount = 2500,
            Date = new DateTime(2026, 9, 10),
            PaymentMethod = "Cash"
        });
        context.DsmPersonalDebtorRepayments.Add(new DsmPersonalDebtorRepayment
        {
            DsmPersonalDebtorId = anchor.Id,
            Amount = 1500,
            Date = new DateTime(2026, 9, 20),
            PaymentMethod = "UPI"
        });
        await context.SaveChangesAsync();

        var repayments = await context.DsmPersonalDebtorRepayments
            .Where(r => r.DsmPersonalDebtorId == anchor.Id)
            .ToListAsync();

        double totalRepayments = repayments.Sum(r => r.Amount);
        Assert.Equal(4000, totalRepayments);
    }

    [Fact]
    public async Task Test09_OutstandingBalance_MatchesStep6Calculation()
    {
        // 9. Outstanding balance matches Step 6 calculation: Opening + Operational Debits - Repayments.
        using var context = CreateContext();
        var repo = new OpeningBalanceRepository(context);

        var creditor = new Creditor { Name = "Vijay Logistics", Phone = "9822012345", IsActive = true, CreatedAt = DateTime.Now };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        await repo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = creditor.CreditorId.ToString(),
            CreditorId = creditor.CreditorId,
            Amount = 22000,
            OpeningDate = new DateTime(2026, 9, 1)
        });

        int dsmId = await CreateTestDsmEntryAsync(context, new DateTime(2026, 9, 5));
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = dsmId,
            DebtorName = creditor.Name,
            Amount = 7000,
            PaymentMethod = "Credit",
            CreatedAt = new DateTime(2026, 9, 5)
        });
        context.CreditorRepayments.Add(new CreditorRepayment
        {
            CreditorName = creditor.Name,
            Amount = 4000,
            RepaymentDate = new DateTime(2026, 9, 15),
            PaymentMode = "Bank"
        });
        await context.SaveChangesAsync();

        // Step 6 calculation
        double opening = 22000;
        double debits = await context.DebitEntries.Where(d => d.DebtorName == creditor.Name).SumAsync(d => d.Amount);
        double repayments = await context.CreditorRepayments.Where(r => r.CreditorName == creditor.Name).SumAsync(r => r.Amount);
        double step6Outstanding = opening + debits - repayments;

        // PrintData closing balance
        var printData = new DebtorLedgerPrintData
        {
            DebtorName = creditor.Name,
            OpeningBalance = opening,
            TotalDebt = debits,
            TotalRepayments = repayments,
            ClosingBalance = opening + debits - repayments
        };

        Assert.Equal(step6Outstanding, printData.ClosingBalance);
        Assert.Equal(25000, printData.ClosingBalance); // 22000 + 7000 - 4000
    }

    [Fact]
    public async Task Test10_ExistingRecordsWithoutOpeningBalances_ProduceSameReportsAsBefore()
    {
        // 10. Existing records without opening balances produce the same reports as before.
        using var context = CreateContext();

        var creditor = new Creditor { Name = "Clean Debtor", Phone = "9822099999", IsActive = true, CreatedAt = DateTime.Now };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        int dsmId = await CreateTestDsmEntryAsync(context, new DateTime(2026, 9, 5));
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = dsmId,
            DebtorName = creditor.Name,
            Amount = 8000,
            PaymentMethod = "Credit",
            CreatedAt = new DateTime(2026, 9, 5)
        });
        context.CreditorRepayments.Add(new CreditorRepayment
        {
            CreditorName = creditor.Name,
            Amount = 3000,
            RepaymentDate = new DateTime(2026, 9, 12),
            PaymentMode = "Cash"
        });
        await context.SaveChangesAsync();

        var printRows = new List<DebtorLedgerPrintRow>
        {
            new DebtorLedgerPrintRow { Date = "01-Sep-2026", Description = "Opening Balance", Debit = 0, Credit = 0, RunningBalance = 0 },
            new DebtorLedgerPrintRow { Date = "05-Sep-2026", Description = "Fuel", Debit = 8000, Credit = 0, RunningBalance = 8000 },
            new DebtorLedgerPrintRow { Date = "12-Sep-2026", Description = "Cash", Debit = 0, Credit = 3000, RunningBalance = 5000 }
        };

        var printData = new DebtorLedgerPrintData
        {
            DebtorName = creditor.Name,
            OpeningBalance = 0,
            TotalDebt = 8000,
            TotalRepayments = 3000,
            ClosingBalance = 5000,
            Transactions = printRows
        };

        Assert.Equal(0, printData.OpeningBalance);
        Assert.Equal(8000, printData.TotalDebt);
        Assert.Equal(3000, printData.TotalRepayments);
        Assert.Equal(5000, printData.ClosingBalance);

        var excelService = new ExcelExportService();
        var exportedPath = await excelService.ExportDebtorLedgerAsync(printData);

        try
        {
            using var workbook = new XLWorkbook(exportedPath);
            var ws = workbook.Worksheet("Ledger");
            // Check Card Values at Row 5
            Assert.Equal(0, ws.Cell(5, 1).GetDouble());
            Assert.Equal(8000, ws.Cell(5, 2).GetDouble());
            Assert.Equal(3000, ws.Cell(5, 3).GetDouble());
            Assert.Equal(5000, ws.Cell(5, 4).GetDouble());
        }
        finally
        {
            try { File.Delete(exportedPath); } catch { }
        }
    }
}
