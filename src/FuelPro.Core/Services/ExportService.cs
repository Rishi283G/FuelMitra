using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Repositories;
using FuelPro.Core.Models;
using CsvHelper;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace FuelPro.Core.Services;

public class ExportService
{
    private readonly ILogger _logger = Log.ForContext<ExportService>();
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IDsmCalculationService _dsmCalculationService;
    private readonly IShiftRepository _shiftRepo;
    private readonly IExpenseRepository _expenseRepo;

    public ExportService(
        IDsmEntryRepository dsmEntryRepo,
        IDsmCalculationService dsmCalculationService,
        IShiftRepository shiftRepo,
        IExpenseRepository expenseRepo)
    {
        _dsmEntryRepo = dsmEntryRepo;
        _dsmCalculationService = dsmCalculationService;
        _shiftRepo = shiftRepo;
        _expenseRepo = expenseRepo;
    }

    public async Task<Result<string>> ExportDailyDataAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            var entriesResult = await _dsmEntryRepo.GetEntriesForDateRangeAsync(startDate, endDate);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(startDate, endDate);
            var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();

            var shiftExpensesResult = await _expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
            var shiftExpenses = shiftExpensesResult.Success && shiftExpensesResult.Data != null ? shiftExpensesResult.Data : new List<Expense>();

            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloadsPath))
            {
                downloadsPath = AppDomain.CurrentDomain.BaseDirectory;
            }

            var filePath = Path.Combine(downloadsPath, $"FuelPro_DailyPerformance_{startDate:yyyyMMdd}_to_{endDate:yyyyMMdd}.xlsx");

            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Daily Performance");
            worksheet.ShowGridLines = true;

            int row = 1;

            // Title Block
            worksheet.Cell(row, 1).Value = "FuelPro Daily Performance Summary Report";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            worksheet.Cell(row, 1).Style.Font.FontSize = 14;
            worksheet.Cell(row, 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            worksheet.Cell(row, 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1a3a6b");
            worksheet.Cell(row, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            worksheet.Range(row, 1, row, 11).Merge();
            row++;

            worksheet.Cell(row, 1).Value = $"Period: {startDate:dd MMM yyyy} to {endDate:dd MMM yyyy}";
            worksheet.Cell(row, 1).Style.Font.Italic = true;
            worksheet.Cell(row, 1).Style.Font.FontSize = 10;
            worksheet.Cell(row, 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            worksheet.Cell(row, 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#0f4c81");
            worksheet.Cell(row, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            worksheet.Range(row, 1, row, 11).Merge();
            row += 2;

            // Headers
            string[] headers = { "Date", "Gross Sales", "Total Litres", "Collection", "Mismatch", "Cash", "PhonePe", "PineLabs Card", "Petro Card", "Debits", "Expenses" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = worksheet.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
                cell.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1a3a6b");
                cell.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
            }
            row++;

            var byDay = entries.GroupBy(e => e.Shift != null ? e.Shift.ShiftDate.Date : DateTime.Today);

            foreach (var dayGroup in byDay.OrderBy(g => g.Key))
            {
                double sale = 0, litres = 0, collection = 0, cash = 0, phonePe = 0, creditCard = 0, petroCard = 0, debits = 0, expenses = 0;

                foreach (var entry in dayGroup)
                {
                    var cash1 = entry.CashDenominations.Where(x => x.CashType == "Cash1").Sum(x => x.TotalAmount);
                    var cash2 = entry.CashDenominations.Where(x => x.CashType == "Cash2").Sum(x => x.TotalAmount);

                    var calc = _dsmCalculationService.Calculate(new DsmEntryDto
                    {
                        DSMEntryId = entry.DsmEntryId,
                        NozzleReadings = entry.NozzleReadings.Select(r => new NozzleReadingDto { Amount = (decimal)r.Amount }).ToList(),
                        PaymentCollection = new PaymentCollectionDto
                        {
                            PhonePe = (decimal)((entry.PaymentCollection?.PhonePe ?? 0) + (entry.PaymentCollection?.PhonePeCardMorning ?? 0) + (entry.PaymentCollection?.PhonePeCardNight ?? 0)),
                            CreditCard = (decimal)((entry.PaymentCollection?.CreditCard ?? 0) + (entry.PaymentCollection?.PetroCard ?? 0)),
                            CashDeposit = (decimal)(cash1 + cash2 + (entry.PaymentCollection?.CashDeposit ?? 0)),
                            PhysicalCash = 0
                        },
                        DebitEntries = entry.DebitEntries.Select(d => new DebitEntryDto { Amount = (decimal)d.Amount }).ToList(),
                        TestingEntries = entry.TestingEntries.Select(t => new TestingEntryDto { FuelType = t.FuelType, Amount = (decimal)t.Amount }).ToList(),
                        Expenses = entry.Expenses.Select(e => new ExpenseDto { Amount = (decimal)e.Amount }).ToList()
                    });

                    sale += (double)calc.GrossSales;
                    collection += (double)calc.TotalCollection;
                    debits += (double)calc.TotalCreditors;
                    expenses += entry.Expenses.Sum(x => x.Amount);

                    phonePe += (entry.PaymentCollection?.PhonePe ?? 0) + (entry.PaymentCollection?.PhonePeCardMorning ?? 0) + (entry.PaymentCollection?.PhonePeCardNight ?? 0);
                    creditCard += (entry.PaymentCollection?.CreditCard ?? 0);
                    petroCard += (entry.PaymentCollection?.PetroCard ?? 0);
                    cash += cash1 + cash2 + (entry.PaymentCollection?.CashDeposit ?? 0);
                    litres += entry.NozzleReadings.Sum(r => r.SaleLitres);
                }

                var dayShiftIds = shifts.Where(s => s.ShiftDate.Date == dayGroup.Key).Select(s => s.ShiftId).ToList();
                expenses += shiftExpenses.Where(e => e.ShiftId.HasValue && dayShiftIds.Contains(e.ShiftId.Value)).Sum(e => e.Amount);

                worksheet.Cell(row, 1).Value = dayGroup.Key.ToString("dd/MM/yyyy");
                worksheet.Cell(row, 2).Value = sale;
                worksheet.Cell(row, 3).Value = litres;
                worksheet.Cell(row, 4).Value = collection;
                worksheet.Cell(row, 5).Value = collection - sale;
                worksheet.Cell(row, 6).Value = cash;
                worksheet.Cell(row, 7).Value = phonePe;
                worksheet.Cell(row, 8).Value = creditCard;
                worksheet.Cell(row, 9).Value = petroCard;
                worksheet.Cell(row, 10).Value = debits;
                worksheet.Cell(row, 11).Value = expenses;

                worksheet.Cell(row, 1).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
                for (int col = 2; col <= 11; col++)
                {
                    worksheet.Cell(row, col).Style.NumberFormat.Format = "#,##0.00";
                }
                row++;
            }

            worksheet.Columns().AdjustToContents();
            workbook.SaveAs(filePath);

            return Result<string>.Ok(filePath);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Excel Daily data export failed");
            return Result<string>.Fail($"Export failed: {ex.Message}");
        }
    }

    public Result ExportFinalCalculationToCsv(FinalCalculationDto calc, string filePath)
    {
        try
        {
            using var writer = new StreamWriter(filePath, false, Encoding.UTF8);
            writer.WriteLine($"Shift Report - {calc.ShiftDate:dd/MM/yyyy} - Shift {calc.ShiftType}");
            writer.WriteLine();

            // TABLE A
            writer.WriteLine("DSM Summary");
            writer.WriteLine("DSM Name,Total In (Direct),Creditors,Total Collection,Mismatch");
            foreach (var r in calc.DsmSummaryRows)
            {
                var totalInDirect = r.PhonePeCardMorning + r.PhonePeCardNight + r.PhonePeMorning + r.PhonePeNight + r.CreditCardMorning + r.CreditCardNight + r.PetroCard + r.CashDeposit + r.CashInHand;
                var totalCollection = totalInDirect + r.Debit;
                var mismatch = calc.TotalFuelSaleAmount - totalCollection;
                writer.WriteLine($"{r.DsmName},{totalInDirect},{r.Debit},{totalCollection},{mismatch}");
            }
            writer.WriteLine();

            // TABLE C
            writer.WriteLine("Debit Register");
            writer.WriteLine("DSM Name,Debtor Name,Amount");
            foreach (var r in calc.DebitRegisterRows)
                writer.WriteLine($"{r.DsmName},{r.DebtorName},{r.Amount}");
            writer.WriteLine($"Total,,{calc.TotalDebit}");
            writer.WriteLine();

            // TABLE D
            writer.WriteLine("Expenses Register");
            writer.WriteLine("DSM Name,Description,Amount");
            foreach (var r in calc.ExpenseRegisterRows)
                writer.WriteLine($"{r.DsmName},{r.Description},{r.Amount}");
            writer.WriteLine($"Total,,{calc.TotalExpenses}");
            writer.WriteLine();

            // TABLE E
            writer.WriteLine("Fuel Dispensed");
            writer.WriteLine("Fuel Type,Litres,Rate,Amount");
            writer.WriteLine($"HSD,{calc.HsdLitres},{calc.HsdRate},{calc.HsdAmount}");
            writer.WriteLine($"MS-I,{calc.MsILitres},{calc.MsIRate},{calc.MsIAmount}");
            writer.WriteLine($"MS-II,{calc.MsIILitres},{calc.MsIIRate},{calc.MsIIAmount}");
            writer.WriteLine($"CNG,{calc.CngLitres},{calc.CngRate},{calc.CngAmount}");
            writer.WriteLine($"Total,{calc.TotalLitres},,{calc.TotalFuelSaleAmount}");
            writer.WriteLine();

            // TABLE F
            writer.WriteLine("Final Reconciliation");
            writer.WriteLine($"PhonePe Total,{calc.PhonePeTotal}");
            writer.WriteLine($"Petro Card,{calc.PetroCardTotal}");
            writer.WriteLine($"Debit,{calc.TotalDebit}");
            writer.WriteLine($"PineLab Card (Morning),{calc.CreditCardMorningTotal}");
            writer.WriteLine($"PineLab Card (Night),{calc.CreditCardNightTotal}");
            writer.WriteLine($"Bank Cash,{calc.BankCash}");
            writer.WriteLine($"Cash in Hand,{calc.CashInHand}");
            writer.WriteLine($"TOTAL,{calc.TotalAmounts}");
            writer.WriteLine($"Fuel Sale Total,{calc.TotalFuelSaleAmount}");
            writer.WriteLine($"Mismatch,{calc.ReconciliationDifference}");

            _logger.Information("CSV exported to {FilePath}", filePath);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "CSV export failed");
            return Result.Fail($"Export failed: {ex.Message}");
        }
    }
}
