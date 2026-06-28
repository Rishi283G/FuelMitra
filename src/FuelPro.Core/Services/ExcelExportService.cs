using ClosedXML.Excel;
using FuelPro.Core.DTOs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace FuelPro.Core.Services;

public class ExcelExportService
{
    private readonly ILogger _logger = Log.ForContext<ExcelExportService>();

    public async Task<string> ExportGenericGridAsync(GenericGridPrintData data, string filenamePrefix)
    {
        return await Task.Run(() =>
        {
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloadsPath))
            {
                downloadsPath = AppDomain.CurrentDomain.BaseDirectory;
            }

            var safePrefix = string.Concat(filenamePrefix.Split(Path.GetInvalidFileNameChars()));
            var fileName = $"{safePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var filePath = Path.Combine(downloadsPath, fileName);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Report");

            // Enable gridlines
            worksheet.ShowGridLines = true;

            int currentRow = 1;

            // 1. Title Block
            worksheet.Cell(currentRow, 1).Value = data.Title;
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 16;
            worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.White;
            worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#004D40");
            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(currentRow, 1, currentRow, Math.Max(data.Headers.Count, 4)).Merge();
            currentRow++;

            // Subtitle
            if (!string.IsNullOrWhiteSpace(data.Subtitle))
            {
                worksheet.Cell(currentRow, 1).Value = data.Subtitle;
                worksheet.Cell(currentRow, 1).Style.Font.Italic = true;
                worksheet.Cell(currentRow, 1).Style.Font.FontSize = 11;
                worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.White;
                worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#00695C");
                worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                worksheet.Range(currentRow, 1, currentRow, Math.Max(data.Headers.Count, 4)).Merge();
                currentRow++;
            }

            currentRow++; // Space

            // 2. Summary Cards
            if (data.SummaryCards != null && data.SummaryCards.Count > 0)
            {
                int cardsPerRow = 4;
                int startCol = 1;
                int cardRow = currentRow;

                for (int i = 0; i < data.SummaryCards.Count; i++)
                {
                    var card = data.SummaryCards[i];
                    int colIndex = startCol + (i % cardsPerRow) * 2;
                    if (i > 0 && i % cardsPerRow == 0)
                    {
                        cardRow += 3;
                    }

                    // Card label
                    var labelCell = worksheet.Cell(cardRow, colIndex);
                    labelCell.Value = card.Label;
                    labelCell.Style.Font.FontSize = 9;
                    labelCell.Style.Font.FontColor = XLColor.Gray;
                    labelCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    worksheet.Range(cardRow, colIndex, cardRow, colIndex + 1).Merge();

                    // Card value
                    var valCell = worksheet.Cell(cardRow + 1, colIndex);
                    valCell.Value = card.Value;
                    valCell.Style.Font.Bold = true;
                    valCell.Style.Font.FontSize = 12;
                    valCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    if (card.Highlight)
                    {
                        valCell.Style.Font.FontColor = XLColor.FromHtml("#004D40");
                        worksheet.Range(cardRow, colIndex, cardRow + 1, colIndex + 1).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                        worksheet.Range(cardRow, colIndex, cardRow + 1, colIndex + 1).Style.Border.OutsideBorderColor = XLColor.FromHtml("#004D40");
                    }
                    else
                    {
                        worksheet.Range(cardRow, colIndex, cardRow + 1, colIndex + 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        worksheet.Range(cardRow, colIndex, cardRow + 1, colIndex + 1).Style.Border.OutsideBorderColor = XLColor.LightGray;
                    }
                    worksheet.Range(cardRow + 1, colIndex, cardRow + 1, colIndex + 1).Merge();
                }

                currentRow = cardRow + 3; // Position below all cards
            }

            currentRow++; // Space

            // 3. Data Table Headers
            for (int col = 0; col < data.Headers.Count; col++)
            {
                var cell = worksheet.Cell(currentRow, col + 1);
                cell.Value = data.Headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#00796B");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#004D40");
            }
            currentRow++;

            // 4. Data Rows
            bool isAlternate = false;
            foreach (var rowData in data.Rows)
            {
                for (int col = 0; col < rowData.Count; col++)
                {
                    var cell = worksheet.Cell(currentRow, col + 1);
                    var val = rowData[col];

                    // Try to parse as double for currency/numeric formatting
                    if (val.StartsWith("₹"))
                    {
                        if (double.TryParse(val.Replace("₹", "").Replace(",", "").Trim(), out double doubleVal))
                        {
                            cell.Value = doubleVal;
                            cell.Style.NumberFormat.Format = "₹#,##0.00";
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                        }
                        else
                        {
                            cell.Value = val;
                        }
                    }
                    else if (double.TryParse(val, out double numericVal))
                    {
                        cell.Value = numericVal;
                        cell.Style.NumberFormat.Format = "#,##0.00";
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    }
                    else
                    {
                        cell.Value = val;
                        if (DateTime.TryParse(val, out _))
                        {
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        }
                    }

                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = XLColor.LightGray;

                    if (isAlternate)
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F5F5F5");
                    }
                }
                isAlternate = !isAlternate;
                currentRow++;
            }

            // Auto-fit columns
            worksheet.Columns(1, Math.Max(data.Headers.Count, 8)).AdjustToContents();

            workbook.SaveAs(filePath);
            return filePath;
        });
    }

    public async Task<string> ExportDebtorLedgerAsync(DebtorLedgerPrintData data)
    {
        return await Task.Run(() =>
        {
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloadsPath))
            {
                downloadsPath = AppDomain.CurrentDomain.BaseDirectory;
            }

            var safeName = string.Concat(data.DebtorName.Split(Path.GetInvalidFileNameChars()));
            var fileName = $"DebtorLedger_{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var filePath = Path.Combine(downloadsPath, fileName);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Ledger");
            worksheet.ShowGridLines = true;

            int currentRow = 1;

            // Title
            worksheet.Cell(currentRow, 1).Value = "Debtor Ledger Statement";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 16;
            worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.White;
            worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#880E4F");
            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(currentRow, 1, currentRow, 5).Merge();
            currentRow++;

            // Customer Details Sub-header
            worksheet.Cell(currentRow, 1).Value = $"Customer: {data.DebtorName} ({data.DebtorPhone})  |  Period: {data.StartDate} to {data.EndDate}";
            worksheet.Cell(currentRow, 1).Style.Font.Italic = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 11;
            worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.White;
            worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#AD1457");
            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(currentRow, 1, currentRow, 5).Merge();
            currentRow++;

            currentRow++; // Space

            // KPI Cards row
            int cardRow = currentRow;
            // Card 1: Opening Balance
            worksheet.Cell(cardRow, 1).Value = "Opening Balance";
            worksheet.Cell(cardRow, 1).Style.Font.FontSize = 9;
            worksheet.Cell(cardRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Cell(cardRow + 1, 1).Value = data.OpeningBalance;
            worksheet.Cell(cardRow + 1, 1).Style.Font.Bold = true;
            worksheet.Cell(cardRow + 1, 1).Style.NumberFormat.Format = "₹#,##0.00";
            worksheet.Cell(cardRow + 1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(cardRow, 1, cardRow + 1, 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            // Card 2: Total Period Credits (Sales)
            worksheet.Cell(cardRow, 2).Value = "Total Period Sales";
            worksheet.Cell(cardRow, 2).Style.Font.FontSize = 9;
            worksheet.Cell(cardRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Cell(cardRow + 1, 2).Value = data.TotalDebt;
            worksheet.Cell(cardRow + 1, 2).Style.Font.Bold = true;
            worksheet.Cell(cardRow + 1, 2).Style.NumberFormat.Format = "₹#,##0.00";
            worksheet.Cell(cardRow + 1, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(cardRow, 2, cardRow + 1, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            // Card 3: Total Period Repayments
            worksheet.Cell(cardRow, 3).Value = "Total Period Recoveries";
            worksheet.Cell(cardRow, 3).Style.Font.FontSize = 9;
            worksheet.Cell(cardRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Cell(cardRow + 1, 3).Value = data.TotalRepayments;
            worksheet.Cell(cardRow + 1, 3).Style.Font.Bold = true;
            worksheet.Cell(cardRow + 1, 3).Style.NumberFormat.Format = "₹#,##0.00";
            worksheet.Cell(cardRow + 1, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(cardRow, 3, cardRow + 1, 3).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            // Card 4: Closing Balance
            worksheet.Cell(cardRow, 4).Value = "Closing Balance";
            worksheet.Cell(cardRow, 4).Style.Font.FontSize = 9;
            worksheet.Cell(cardRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Cell(cardRow + 1, 4).Value = data.ClosingBalance;
            worksheet.Cell(cardRow + 1, 4).Style.Font.Bold = true;
            worksheet.Cell(cardRow + 1, 4).Style.Font.FontColor = XLColor.FromHtml("#880E4F");
            worksheet.Cell(cardRow + 1, 4).Style.NumberFormat.Format = "₹#,##0.00";
            worksheet.Cell(cardRow + 1, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(cardRow, 4, cardRow + 1, 4).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            worksheet.Range(cardRow, 4, cardRow + 1, 4).Style.Border.OutsideBorderColor = XLColor.FromHtml("#880E4F");

            currentRow = cardRow + 3;

            // Data Table Headers
            string[] headers = { "Date", "Description / Vehicle", "Debit (Sales)", "Credit (Repayment)", "Running Balance" };
            for (int col = 0; col < headers.Length; col++)
            {
                var cell = worksheet.Cell(currentRow, col + 1);
                cell.Value = headers[col];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#880E4F");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            currentRow++;

            // Data Rows
            bool isAlternate = false;
            foreach (var tx in data.Transactions)
            {
                worksheet.Cell(currentRow, 1).Value = tx.Date;
                worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

                worksheet.Cell(currentRow, 2).Value = tx.Description;
                
                worksheet.Cell(currentRow, 3).Value = tx.Debit > 0 ? tx.Debit : "";
                worksheet.Cell(currentRow, 3).Style.NumberFormat.Format = "₹#,##0.00";
                worksheet.Cell(currentRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                worksheet.Cell(currentRow, 4).Value = tx.Credit > 0 ? tx.Credit : "";
                worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "₹#,##0.00";
                worksheet.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                worksheet.Cell(currentRow, 5).Value = tx.RunningBalance;
                worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "₹#,##0.00";
                worksheet.Cell(currentRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                for (int col = 1; col <= 5; col++)
                {
                    var cell = worksheet.Cell(currentRow, col);
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = XLColor.LightGray;
                    if (isAlternate)
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE4EC");
                    }
                }

                isAlternate = !isAlternate;
                currentRow++;
            }

            worksheet.Columns(1, 5).AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        });
    }

    public async Task<string> ExportMonthlyPLAsync(
        string stationName,
        DateTime startDate,
        DateTime endDate,
        FinancialCalculationResult financials,
        List<(string Category, double Amount)> expenses)
    {
        return await Task.Run(() =>
        {
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloadsPath))
            {
                downloadsPath = AppDomain.CurrentDomain.BaseDirectory;
            }

            var safeStation = string.Concat(stationName.Split(Path.GetInvalidFileNameChars()));
            var fileName = $"MonthlyPL_{safeStation}_{startDate:yyyyMM}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var filePath = Path.Combine(downloadsPath, fileName);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("P&L Statement");
            worksheet.ShowGridLines = true;

            int currentRow = 1;

            // Title Block
            worksheet.Cell(currentRow, 1).Value = "Monthly Profit & Loss Statement";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 16;
            worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.White;
            worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#004D40");
            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(currentRow, 1, currentRow, 7).Merge();
            currentRow++;

            // Subtitle
            worksheet.Cell(currentRow, 1).Value = $"{stationName}  |  Period: {startDate:dd-MMM-yyyy} to {endDate:dd-MMM-yyyy}";
            worksheet.Cell(currentRow, 1).Style.Font.Italic = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 11;
            worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.White;
            worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#00695C");
            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(currentRow, 1, currentRow, 7).Merge();
            currentRow++;

            currentRow++; // Space

            // 1. Fuel Profit Section
            worksheet.Cell(currentRow, 1).Value = "1. Fuel Profit Breakdown";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 12;
            worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.FromHtml("#004D40");
            worksheet.Range(currentRow, 1, currentRow, 4).Merge();
            currentRow++;

            // Headers
            string[] fuelHeaders = { "Fuel Type", "Sales Qty", "Margin (₹/Qty)", "Total Profit" };
            for (int col = 0; col < fuelHeaders.Length; col++)
            {
                var cell = worksheet.Cell(currentRow, col + 1);
                cell.Value = fuelHeaders[col];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E0F2F1");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            currentRow++;

            // Rows
            var fuel = financials.FuelProfit;
            var fuelRows = new List<(string Name, double Litres, double Margin, double Profit, string Unit)>
            {
                ("Diesel (HSD)", fuel.HsdLitres, fuel.HsdMargin, fuel.HsdProfit, "L"),
                ("Petrol (MS-I)", fuel.MsILitres, fuel.MsIMargin, fuel.MsIProfit, "L"),
                ("Power (MS-II)", fuel.MsIILitres, fuel.MsIIMargin, fuel.MsIIProfit, "L"),
                ("CNG", fuel.CngLitres, fuel.CngMargin, fuel.CngProfit, "Kg")
            };

            foreach (var r in fuelRows)
            {
                worksheet.Cell(currentRow, 1).Value = r.Name;
                worksheet.Cell(currentRow, 2).Value = r.Litres;
                worksheet.Cell(currentRow, 2).Style.NumberFormat.Format = $"#,##0.00 \"{r.Unit}\"";
                worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                
                worksheet.Cell(currentRow, 3).Value = r.Margin;
                worksheet.Cell(currentRow, 3).Style.NumberFormat.Format = "₹#,##0.00";
                worksheet.Cell(currentRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                worksheet.Cell(currentRow, 4).Value = r.Profit;
                worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "₹#,##0.00";
                worksheet.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                for (int col = 1; col <= 4; col++)
                {
                    worksheet.Cell(currentRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    worksheet.Cell(currentRow, col).Style.Border.OutsideBorderColor = XLColor.LightGray;
                }
                currentRow++;
            }

            // Fuel Total
            worksheet.Cell(currentRow, 1).Value = "Total Fuel";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            
            worksheet.Cell(currentRow, 2).Value = fuel.TotalLitres;
            worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 2).Style.NumberFormat.Format = "#,##0.00 \"Qty\"";
            worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            worksheet.Cell(currentRow, 3).Value = "-";
            worksheet.Cell(currentRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            worksheet.Cell(currentRow, 4).Value = fuel.TotalFuelProfit;
            worksheet.Cell(currentRow, 4).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "₹#,##0.00";
            worksheet.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            for (int col = 1; col <= 4; col++)
            {
                worksheet.Cell(currentRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                worksheet.Cell(currentRow, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5E9");
            }
            currentRow++;

            currentRow++; // Space

            // 2. Oil & AdBlue Profit Section
            worksheet.Cell(currentRow, 1).Value = "2. Oil & DEF Inventory Profit";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 12;
            worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.FromHtml("#004D40");
            worksheet.Range(currentRow, 1, currentRow, 7).Merge();
            currentRow++;

            // Headers
            string[] invHeaders = { "Product Type", "Opening Qty", "Closing Qty", "Sales Qty", "Avg Purchase Price", "Sale Price", "Total Profit" };
            for (int col = 0; col < invHeaders.Length; col++)
            {
                var cell = worksheet.Cell(currentRow, col + 1);
                cell.Value = invHeaders[col];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E0F2F1");
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            currentRow++;

            // Rows
            var oil = financials.OilProfit;
            var def = financials.DefProfit;
            var invRows = new List<(string Name, ProductProfitDetail Detail)>
            {
                ("Lube Oil", oil),
                ("DEF (AdBlue)", def)
            };

            foreach (var r in invRows)
            {
                worksheet.Cell(currentRow, 1).Value = r.Name;
                worksheet.Cell(currentRow, 2).Value = r.Detail.OpeningStock;
                worksheet.Cell(currentRow, 3).Value = r.Detail.ClosingStock;
                worksheet.Cell(currentRow, 4).Value = r.Detail.SalesQuantity;
                worksheet.Cell(currentRow, 5).Value = r.Detail.AveragePurchasePrice;
                worksheet.Cell(currentRow, 6).Value = r.Detail.SalePrice;
                worksheet.Cell(currentRow, 7).Value = r.Detail.TotalProfit;

                for (int col = 2; col <= 4; col++)
                {
                    worksheet.Cell(currentRow, col).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(currentRow, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }
                for (int col = 5; col <= 7; col++)
                {
                    worksheet.Cell(currentRow, col).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(currentRow, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }

                for (int col = 1; col <= 7; col++)
                {
                    worksheet.Cell(currentRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    worksheet.Cell(currentRow, col).Style.Border.OutsideBorderColor = XLColor.LightGray;
                }
                currentRow++;
            }

            // Total Inventory Row
            worksheet.Cell(currentRow, 1).Value = "Total Inventory Profit";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            for (int col = 2; col <= 6; col++)
            {
                worksheet.Cell(currentRow, col).Value = "-";
                worksheet.Cell(currentRow, col).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            worksheet.Cell(currentRow, 7).Value = oil.TotalProfit + def.TotalProfit;
            worksheet.Cell(currentRow, 7).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 7).Style.NumberFormat.Format = "₹#,##0.00";
            worksheet.Cell(currentRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            for (int col = 1; col <= 7; col++)
            {
                worksheet.Cell(currentRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                worksheet.Cell(currentRow, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5E9");
            }
            currentRow++;

            currentRow++; // Space

            // 3. Operational Expenses
            worksheet.Cell(currentRow, 1).Value = "3. Operational Expenses";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 12;
            worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.FromHtml("#004D40");
            worksheet.Range(currentRow, 1, currentRow, 4).Merge();
            currentRow++;

            worksheet.Cell(currentRow, 1).Value = "Expense Category";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E0F2F1");
            worksheet.Cell(currentRow, 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            worksheet.Cell(currentRow, 2).Value = "Amount";
            worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#E0F2F1");
            worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            worksheet.Cell(currentRow, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            worksheet.Range(currentRow, 2, currentRow, 4).Merge();
            currentRow++;

            bool isAlt = false;
            foreach (var exp in expenses)
            {
                worksheet.Cell(currentRow, 1).Value = exp.Category;
                worksheet.Cell(currentRow, 2).Value = exp.Amount;
                worksheet.Cell(currentRow, 2).Style.NumberFormat.Format = "₹#,##0.00";
                worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                worksheet.Range(currentRow, 2, currentRow, 4).Merge();

                for (int col = 1; col <= 4; col++)
                {
                    worksheet.Cell(currentRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    worksheet.Cell(currentRow, col).Style.Border.OutsideBorderColor = XLColor.LightGray;
                    if (isAlt) worksheet.Cell(currentRow, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#F9F9F9");
                }
                isAlt = !isAlt;
                currentRow++;
            }

            worksheet.Cell(currentRow, 1).Value = "Total Operational Expenses";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 2).Value = financials.ManagerExpenses;
            worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 2).Style.NumberFormat.Format = "₹#,##0.00";
            worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            worksheet.Range(currentRow, 2, currentRow, 4).Merge();

            for (int col = 1; col <= 4; col++)
            {
                worksheet.Cell(currentRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                worksheet.Cell(currentRow, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFEBEE");
            }
            currentRow++;

            currentRow++; // Space

            // 4. Summary & Bottom Line
            worksheet.Cell(currentRow, 1).Value = "4. Summary & Bottom Line";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 12;
            worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.FromHtml("#004D40");
            worksheet.Range(currentRow, 1, currentRow, 4).Merge();
            currentRow++;

            var summaryData = new List<(string Label, double Val, bool Highlight)>
            {
                ("Gross Profit (Fuel + Inventory)", financials.GrossProfit, false),
                ("Total DSM Salaries Paid", financials.TotalDsmSalaries, false),
                ("Total Operational Expenses", financials.ManagerExpenses, false),
                ("Total Pump/Owner Expenses", financials.TotalPumpExpenses, false),
                ("Net Profit", financials.NetProfit, true)
            };

            foreach (var s in summaryData)
            {
                worksheet.Cell(currentRow, 1).Value = s.Label;
                worksheet.Cell(currentRow, 1).Style.Font.Bold = s.Highlight;

                worksheet.Cell(currentRow, 2).Value = s.Val;
                worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
                worksheet.Cell(currentRow, 2).Style.NumberFormat.Format = "₹#,##0.00";
                worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                worksheet.Range(currentRow, 2, currentRow, 4).Merge();

                for (int col = 1; col <= 4; col++)
                {
                    worksheet.Cell(currentRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    if (s.Highlight)
                    {
                        worksheet.Cell(currentRow, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#C8E6C9");
                        worksheet.Cell(currentRow, col).Style.Font.FontColor = XLColor.FromHtml("#1B5E20");
                        worksheet.Cell(currentRow, col).Style.Font.FontSize = 11;
                    }
                    else
                    {
                        worksheet.Cell(currentRow, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#ECEFF1");
                    }
                }
                currentRow++;
            }

            currentRow++; // Space

            // 5. Pump/Owner Expenses Breakdown
            worksheet.Cell(currentRow, 1).Value = "5. Pump / Owner Expenses";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 12;
            worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.FromHtml("#004D40");
            worksheet.Range(currentRow, 1, currentRow, 4).Merge();
            currentRow++;

            worksheet.Cell(currentRow, 1).Value = "Pump Expense Type";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E0F2F1");
            worksheet.Cell(currentRow, 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            worksheet.Cell(currentRow, 2).Value = "Amount";
            worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 2).Style.Fill.BackgroundColor = XLColor.FromHtml("#E0F2F1");
            worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            worksheet.Cell(currentRow, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            worksheet.Range(currentRow, 2, currentRow, 4).Merge();
            currentRow++;

            var pumpExps = new List<(string Label, double Val)>
            {
                ("Pump Rent", financials.PumpRent),
                ("Pump Salary", financials.PumpSalary),
                ("Pump Trip Sheet Loss", financials.PumpTripSheetLoss),
                ("Pump DSM Short", financials.PumpDsmShort),
                ("Pump Banking Expenses", financials.PumpBankingExpenses),
                ("Pump BPCL Portal Expenses", financials.PumpBpclPortalExpenses),
                ("Pump Fuel & Travel", financials.PumpFuelAndTravel),
                ("Pump Oil Purchase", financials.PumpOilPurchase),
                ("Pump Repairs & Maintenance", financials.PumpRepairsAndMaintenance),
                ("Pump Electricity", financials.PumpElectricity),
                ("Pump Office Expenses", financials.PumpOfficeExpenses),
                ("Pump Printing Expense", financials.PumpPrintingExpense),
                ("Pump Other Expense", financials.PumpOtherAmount)
            };

            isAlt = false;
            foreach (var pe in pumpExps)
            {
                worksheet.Cell(currentRow, 1).Value = pe.Label;
                worksheet.Cell(currentRow, 2).Value = pe.Val;
                worksheet.Cell(currentRow, 2).Style.NumberFormat.Format = "₹#,##0.00";
                worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                worksheet.Range(currentRow, 2, currentRow, 4).Merge();

                for (int col = 1; col <= 4; col++)
                {
                    worksheet.Cell(currentRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    worksheet.Cell(currentRow, col).Style.Border.OutsideBorderColor = XLColor.LightGray;
                    if (isAlt) worksheet.Cell(currentRow, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#F9F9F9");
                }
                isAlt = !isAlt;
                currentRow++;
            }

            worksheet.Cell(currentRow, 1).Value = "Total Pump Expenses";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 2).Value = financials.TotalPumpExpenses;
            worksheet.Cell(currentRow, 2).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 2).Style.NumberFormat.Format = "₹#,##0.00";
            worksheet.Cell(currentRow, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            worksheet.Range(currentRow, 2, currentRow, 4).Merge();

            for (int col = 1; col <= 4; col++)
            {
                worksheet.Cell(currentRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                worksheet.Cell(currentRow, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#FFEBEE");
            }
            currentRow++;

            // Columns adjust
            worksheet.Columns(1, 7).AdjustToContents();

            workbook.SaveAs(filePath);
            return filePath;
        });
    }
}
