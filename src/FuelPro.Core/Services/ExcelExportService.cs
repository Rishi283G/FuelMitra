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

                bool isOpeningRow = !string.IsNullOrEmpty(tx.Description) && (
                    tx.Description.Equals("Opening Balance", StringComparison.OrdinalIgnoreCase) ||
                    tx.Description.Equals("Historical Opening Balance", StringComparison.OrdinalIgnoreCase) ||
                    tx.Description.IndexOf("Opening Balance", StringComparison.OrdinalIgnoreCase) >= 0
                );
                
                worksheet.Cell(currentRow, 3).Value = (!isOpeningRow && tx.Debit > 0) ? tx.Debit : "";
                worksheet.Cell(currentRow, 3).Style.NumberFormat.Format = "₹#,##0.00";
                worksheet.Cell(currentRow, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                worksheet.Cell(currentRow, 4).Value = (!isOpeningRow && tx.Credit > 0) ? tx.Credit : "";
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
                    if (isOpeningRow)
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F1F5F9");
                        cell.Style.Font.Bold = true;
                    }
                    else if (isAlternate)
                    {
                        cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE4EC");
                    }
                }

                if (!isOpeningRow)
                {
                    isAlternate = !isAlternate;
                }
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
                ("Net Mismatch (Excess/Shortage)", financials.TotalMismatch, false),
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

    public async Task<string> ExportTidSheetAsync(BusinessDayTidSheet data, string stationName)
    {
        return await Task.Run(() =>
        {
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloadsPath))
            {
                downloadsPath = AppDomain.CurrentDomain.BaseDirectory;
            }

            var safePrefix = string.Concat(stationName.Split(Path.GetInvalidFileNameChars()));
            var fileName = $"TID_Sheet_{data.Date:yyyyMMdd}_{DateTime.Now:HHmmss}.xlsx";
            var filePath = Path.Combine(downloadsPath, fileName);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("TID Sheet");

            worksheet.ShowGridLines = true;
            int currentRow = 1;

            // Title Block
            worksheet.Cell(currentRow, 1).Value = $"{stationName.ToUpper()} - TID SHEET";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 16;
            worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.White;
            worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#004D40");
            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(currentRow, 1, currentRow, 5).Merge();
            currentRow++;

            // Subtitle
            worksheet.Cell(currentRow, 1).Value = $"Date: {data.Date:dd-MMM-yyyy} | Generated: {DateTime.Now:dd-MMM-yyyy HH:mm}";
            worksheet.Cell(currentRow, 1).Style.Font.Italic = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 11;
            worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.White;
            worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#00695C");
            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(currentRow, 1, currentRow, 5).Merge();
            currentRow++;

            currentRow += 2; // Blank space

            // Define collection types dynamically
            var collections = (data.CollectionGroups != null && data.CollectionGroups.Count > 0)
                ? data.CollectionGroups.Select(g => new { Name = g.DisplayName, Items = g.Items }).ToList()
                : new[]
                {
                    new { Name = "PhonePe", Items = data.PhonePePayments },
                    new { Name = "PineLabs Card", Items = data.CardPayments },
                    new { Name = "Petro Card", Items = data.PetroCardPayments }
                }.ToList();

            foreach (var col in collections)
            {
                // Collection Type Header
                worksheet.Cell(currentRow, 1).Value = col.Name.ToUpper();
                worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                worksheet.Cell(currentRow, 1).Style.Font.FontSize = 13;
                worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.White;
                worksheet.Cell(currentRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1565C0");
                worksheet.Range(currentRow, 1, currentRow, 5).Merge();
                currentRow++;

                // Table headers
                var headers = new[] { "Business Period", "DSM Name", "TID", "Batch No.", "Amount" };
                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = worksheet.Cell(currentRow, i + 1);
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    if (i == 4) cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }
                currentRow++;

                // Group items by Business Period
                var periods = new[] { "Morning", "Day", "Night" };
                double colTotal = 0;

                foreach (var period in periods)
                {
                    var periodItems = col.Items.Where(x => x.Slot == period).ToList();
                    if (periodItems.Count == 0) continue;

                    double periodSubtotal = 0;
                    foreach (var item in periodItems)
                    {
                        worksheet.Cell(currentRow, 1).Value = period;
                        worksheet.Cell(currentRow, 2).Value = item.DsmName;
                        worksheet.Cell(currentRow, 3).Value = item.Tid;
                        worksheet.Cell(currentRow, 4).Value = item.Batch;
                        
                        var amtCell = worksheet.Cell(currentRow, 5);
                        amtCell.Value = item.Amount;
                        amtCell.Style.NumberFormat.Format = "₹#,##0.00";
                        amtCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                        for (int colIdx = 1; colIdx <= 5; colIdx++)
                        {
                            worksheet.Cell(currentRow, colIdx).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            worksheet.Cell(currentRow, colIdx).Style.Border.OutsideBorderColor = XLColor.LightGray;
                        }

                        periodSubtotal += item.Amount;
                        currentRow++;
                    }

                    // Period Subtotal Row
                    worksheet.Cell(currentRow, 1).Value = $"{period} Subtotal";
                    worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                    worksheet.Range(currentRow, 1, currentRow, 4).Merge();
                    
                    var subCell = worksheet.Cell(currentRow, 5);
                    subCell.Value = periodSubtotal;
                    subCell.Style.Font.Bold = true;
                    subCell.Style.NumberFormat.Format = "₹#,##0.00";
                    subCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                    for (int colIdx = 1; colIdx <= 5; colIdx++)
                    {
                        worksheet.Cell(currentRow, colIdx).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        worksheet.Cell(currentRow, colIdx).Style.Fill.BackgroundColor = XLColor.FromHtml("#E3F2FD");
                    }
                    currentRow++;

                    colTotal += periodSubtotal;
                }

                // Collection Type Total Row
                worksheet.Cell(currentRow, 1).Value = $"{col.Name} Total";
                worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
                worksheet.Cell(currentRow, 1).Style.Font.FontSize = 11;
                worksheet.Range(currentRow, 1, currentRow, 4).Merge();

                var colTotalCell = worksheet.Cell(currentRow, 5);
                colTotalCell.Value = colTotal;
                colTotalCell.Style.Font.Bold = true;
                colTotalCell.Style.Font.FontSize = 11;
                colTotalCell.Style.NumberFormat.Format = "₹#,##0.00";
                colTotalCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                for (int colIdx = 1; colIdx <= 5; colIdx++)
                {
                    worksheet.Cell(currentRow, colIdx).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    worksheet.Cell(currentRow, colIdx).Style.Fill.BackgroundColor = XLColor.FromHtml("#BBDEFB");
                }
                currentRow += 2; // Blank row after collection type
            }

            // Grand Total block
            worksheet.Cell(currentRow, 1).Value = "GRAND TOTAL DIGITAL COLLECTION";
            worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 1).Style.Font.FontSize = 12;
            worksheet.Range(currentRow, 1, currentRow, 4).Merge();

            var grandCell = worksheet.Cell(currentRow, 5);
            grandCell.Value = data.GrandTotal;
            grandCell.Style.Font.Bold = true;
            grandCell.Style.Font.FontSize = 12;
            grandCell.Style.Font.FontColor = XLColor.White;
            grandCell.Style.NumberFormat.Format = "₹#,##0.00";
            grandCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            for (int colIdx = 1; colIdx <= 5; colIdx++)
            {
                worksheet.Cell(currentRow, colIdx).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                worksheet.Cell(currentRow, colIdx).Style.Fill.BackgroundColor = XLColor.FromHtml("#2E7D32");
                if (colIdx != 5) worksheet.Cell(currentRow, colIdx).Style.Font.FontColor = XLColor.White;
            }
            currentRow += 3;

            // Signatures
            worksheet.Cell(currentRow, 1).Value = "Supervisor Signature";
            worksheet.Range(currentRow, 1, currentRow, 2).Merge();

            worksheet.Cell(currentRow, 4).Value = "Manager Signature";
            worksheet.Range(currentRow, 4, currentRow, 5).Merge();

            worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Cell(currentRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            
            worksheet.Cell(currentRow, 1).Style.Font.Italic = true;
            worksheet.Cell(currentRow, 4).Style.Font.Italic = true;

            // Auto-fit columns
            worksheet.Columns(1, 5).AdjustToContents();

            workbook.SaveAs(filePath);
            return filePath;
        });
    }

    #region Shift Total & Day Total Excel Exports

    public async Task<string> ExportShiftTotalReportAsync(ShiftReportDto report, string? customFilePath = null)
    {
        return await Task.Run(() =>
        {
            string filePath = customFilePath ?? "";
            if (string.IsNullOrWhiteSpace(filePath))
            {
                var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloadsPath)) downloadsPath = AppDomain.CurrentDomain.BaseDirectory;
                var fileName = $"ShiftTotal_Shift{report.ShiftLabel}_{report.Date:yyyyMMdd}_{DateTime.Now:HHmmss}.xlsx";
                filePath = Path.Combine(downloadsPath, fileName);
            }

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Shift Total");
            worksheet.ShowGridLines = true;

            int r = 1;
            int maxCols = 14;

            // Header Block
            worksheet.Cell(r, 1).Value = string.IsNullOrWhiteSpace(report.StationName) ? "Mitali Service Station" : report.StationName;
            worksheet.Cell(r, 1).Style.Font.Bold = true;
            worksheet.Cell(r, 1).Style.Font.FontSize = 16;
            worksheet.Cell(r, 1).Style.Font.FontColor = XLColor.White;
            worksheet.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#004D40");
            worksheet.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(r, 1, r, maxCols).Merge();
            r++;

            worksheet.Cell(r, 1).Value = $"SHIFT TOTAL REPORT — Shift {report.ShiftLabel} — {report.DateString}";
            worksheet.Cell(r, 1).Style.Font.Bold = true;
            worksheet.Cell(r, 1).Style.Font.FontSize = 12;
            worksheet.Cell(r, 1).Style.Font.FontColor = XLColor.White;
            worksheet.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#00695C");
            worksheet.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(r, 1, r, maxCols).Merge();
            r++;

            if (!string.IsNullOrWhiteSpace(report.ManagerName))
            {
                worksheet.Cell(r, 1).Value = $"Shift Manager: {report.ManagerName}";
                worksheet.Cell(r, 1).Style.Font.Italic = true;
                worksheet.Cell(r, 1).Style.Font.FontSize = 10;
                worksheet.Cell(r, 1).Style.Font.FontColor = XLColor.White;
                worksheet.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#00796B");
                worksheet.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                worksheet.Range(r, 1, r, maxCols).Merge();
                r++;
            }
            r++;

            // 1. Tank Dip & Stock Status
            if (report.TankSummary != null && report.TankSummary.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "1. Tank Dip & Stock Status", maxCols);
                var tankHeaders = new List<string> { "Fuel / Tank", "Dip (mm)", "Stock (Ltr)", "Opening (Ltr)", "Receipts (Ltr)", "Sales (Ltr)", "Testing (Ltr)", "Calculated Stock (Ltr)", "Variance (Ltr)" };
                WriteTableHeaders(worksheet, ref r, tankHeaders);

                bool isAlt = false;
                foreach (var tank in report.TankSummary)
                {
                    worksheet.Cell(r, 1).Value = tank.FuelType;
                    worksheet.Cell(r, 2).Value = tank.DipMm; worksheet.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 3).Value = tank.StockLtr; worksheet.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 4).Value = tank.OpeningStock; worksheet.Cell(r, 4).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 5).Value = tank.Receipts; worksheet.Cell(r, 5).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 6).Value = tank.SaleLitres; worksheet.Cell(r, 6).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 7).Value = tank.TestingLitres; worksheet.Cell(r, 7).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 8).Value = tank.CalculatedStock; worksheet.Cell(r, 8).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 9).Value = tank.StockVariance; worksheet.Cell(r, 9).Style.NumberFormat.Format = "#,##0.00";
                    
                    ApplyRowBorders(worksheet, r, 9, isAlt);
                    isAlt = !isAlt;
                    r++;
                }
                r++;
            }

            // 2. Nozzle Readings
            if (report.NozzleGroups != null && report.NozzleGroups.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "2. Nozzle Opening & Closing Readings", maxCols);
                var nozzleHeaders = new List<string> { "Pump No", "Nozzle Name", "Fuel Type", "Opening Reading", "Closing Reading", "Sale Litres" };
                WriteTableHeaders(worksheet, ref r, nozzleHeaders);

                bool isAlt = false;
                foreach (var group in report.NozzleGroups)
                {
                    foreach (var n in group.Nozzles)
                    {
                        worksheet.Cell(r, 1).Value = $"Pump #{group.PumpId}";
                        worksheet.Cell(r, 2).Value = n.NozzleName;
                        worksheet.Cell(r, 3).Value = group.FuelType;
                        worksheet.Cell(r, 4).Value = n.OpeningReading; worksheet.Cell(r, 4).Style.NumberFormat.Format = "#,##0.00";
                        worksheet.Cell(r, 5).Value = n.ClosingReading; worksheet.Cell(r, 5).Style.NumberFormat.Format = "#,##0.00";
                        worksheet.Cell(r, 6).Value = n.SaleLitres; worksheet.Cell(r, 6).Style.NumberFormat.Format = "#,##0.00";
                        ApplyRowBorders(worksheet, r, 6, isAlt);
                        isAlt = !isAlt;
                        r++;
                    }
                }
                r++;
            }

            // 3. DSM Sales & Collection Summary (Table A)
            if (report.DsmSummaryRows != null && report.DsmSummaryRows.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "3. DSM Sales & Collection Summary", maxCols);
                var dsmHeaders = new List<string> { "DSM Name", "Pump", "PhonePe (M)", "PhonePe (N)", "Card (M)", "Card (N)", "PetroCard", "Bank Cash", "Cash In Hand", "Debtors", "Expenses", "Testing", "Others", "Gross Sales", "Difference" };
                WriteTableHeaders(worksheet, ref r, dsmHeaders);

                bool isAlt = false;
                foreach (var row in report.DsmSummaryRows)
                {
                    worksheet.Cell(r, 1).Value = row.DsmName;
                    worksheet.Cell(r, 2).Value = row.PumpId > 0 ? $"Pump #{row.PumpId}" : "—";
                    worksheet.Cell(r, 3).Value = row.PhonePeMorning; worksheet.Cell(r, 3).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 4).Value = row.PhonePeNight; worksheet.Cell(r, 4).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 5).Value = row.CreditCardMorning; worksheet.Cell(r, 5).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 6).Value = row.CreditCardNight; worksheet.Cell(r, 6).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 7).Value = row.PetroCard; worksheet.Cell(r, 7).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 8).Value = row.BankCash; worksheet.Cell(r, 8).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 9).Value = row.CashInHand; worksheet.Cell(r, 9).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 10).Value = row.DebtorSales; worksheet.Cell(r, 10).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 11).Value = row.Expenses; worksheet.Cell(r, 11).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 12).Value = row.Testing; worksheet.Cell(r, 12).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 13).Value = row.Others; worksheet.Cell(r, 13).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 14).Value = row.GrossSale; worksheet.Cell(r, 14).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 15).Value = row.Difference; worksheet.Cell(r, 15).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyRowBorders(worksheet, r, 15, isAlt);
                    isAlt = !isAlt;
                    r++;
                }

                if (report.DsmSummaryTotals != null)
                {
                    var t = report.DsmSummaryTotals;
                    worksheet.Cell(r, 1).Value = "TOTAL";
                    worksheet.Cell(r, 2).Value = "";
                    worksheet.Cell(r, 3).Value = t.PhonePeMorning; worksheet.Cell(r, 3).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 4).Value = t.PhonePeNight; worksheet.Cell(r, 4).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 5).Value = t.CreditCardMorning; worksheet.Cell(r, 5).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 6).Value = t.CreditCardNight; worksheet.Cell(r, 6).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 7).Value = t.PetroCard; worksheet.Cell(r, 7).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 8).Value = t.BankCash; worksheet.Cell(r, 8).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 9).Value = t.CashInHand; worksheet.Cell(r, 9).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 10).Value = t.DebtorSales; worksheet.Cell(r, 10).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 11).Value = t.Expenses; worksheet.Cell(r, 11).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 12).Value = t.Testing; worksheet.Cell(r, 12).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 13).Value = t.Others; worksheet.Cell(r, 13).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 14).Value = t.GrossSale; worksheet.Cell(r, 14).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 15).Value = t.Difference; worksheet.Cell(r, 15).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyTotalRowBorders(worksheet, r, 15);
                    r++;
                }
                r++;
            }

            // 4. Debtors / Credit Sales & Recoveries
            if (report.CreditorRows != null && report.CreditorRows.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "4. Debtor Credit Sales", maxCols);
                var debHeaders = new List<string> { "DSM Name", "Pump", "Debtor Name", "Cheque / Vehicle No", "Amount (₹)" };
                WriteTableHeaders(worksheet, ref r, debHeaders);

                bool isAlt = false;
                foreach (var c in report.CreditorRows)
                {
                    worksheet.Cell(r, 1).Value = c.DsmName;
                    worksheet.Cell(r, 2).Value = c.PumpId > 0 ? $"Pump #{c.PumpId}" : "—";
                    worksheet.Cell(r, 3).Value = c.DebtorName;
                    worksheet.Cell(r, 4).Value = c.ChequeNo;
                    worksheet.Cell(r, 5).Value = c.Amount; worksheet.Cell(r, 5).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyRowBorders(worksheet, r, 5, isAlt);
                    isAlt = !isAlt;
                    r++;
                }
                r++;
            }

            if (report.DebtorRepayments != null && report.DebtorRepayments.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "4B. Debtor Recoveries (Collections)", maxCols);
                var repHeaders = new List<string> { "Debtor Name", "Payment Mode", "TID / Batch / Details", "Amount (₹)" };
                WriteTableHeaders(worksheet, ref r, repHeaders);

                bool isAlt = false;
                foreach (var dr in report.DebtorRepayments)
                {
                    worksheet.Cell(r, 1).Value = dr.DebtorName;
                    worksheet.Cell(r, 2).Value = dr.PaymentMode;
                    worksheet.Cell(r, 3).Value = string.IsNullOrWhiteSpace(dr.RefNo) ? "—" : dr.RefNo;
                    worksheet.Cell(r, 4).Value = dr.Amount; worksheet.Cell(r, 4).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyRowBorders(worksheet, r, 4, isAlt);
                    isAlt = !isAlt;
                    r++;
                }
                r++;
            }

            // 5. Expenses & Oil/DEF Sales
            if (report.ExpenseRows != null && report.ExpenseRows.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "5. Shift Expenses Register", maxCols);
                var expHeaders = new List<string> { "DSM Name", "Pump", "Description", "Amount (₹)" };
                WriteTableHeaders(worksheet, ref r, expHeaders);

                bool isAlt = false;
                foreach (var exp in report.ExpenseRows)
                {
                    worksheet.Cell(r, 1).Value = exp.DsmName;
                    worksheet.Cell(r, 2).Value = exp.PumpId > 0 ? $"Pump #{exp.PumpId}" : "—";
                    worksheet.Cell(r, 3).Value = exp.Description;
                    worksheet.Cell(r, 4).Value = exp.Amount; worksheet.Cell(r, 4).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyRowBorders(worksheet, r, 4, isAlt);
                    isAlt = !isAlt;
                    r++;
                }
                r++;
            }

            if (report.OilDefSales != null && report.OilDefSales.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "5B. Oil & DEF Product Sales", maxCols);
                var oilHeaders = new List<string> { "Product Name", "Category", "Quantity", "Rate (₹)", "Amount (₹)" };
                WriteTableHeaders(worksheet, ref r, oilHeaders);

                bool isAlt = false;
                foreach (var oil in report.OilDefSales)
                {
                    worksheet.Cell(r, 1).Value = oil.ProductName;
                    worksheet.Cell(r, 2).Value = oil.Category;
                    worksheet.Cell(r, 3).Value = oil.Quantity; worksheet.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 4).Value = oil.Rate; worksheet.Cell(r, 4).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 5).Value = oil.Total; worksheet.Cell(r, 5).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyRowBorders(worksheet, r, 5, isAlt);
                    isAlt = !isAlt;
                    r++;
                }
                r++;
            }

            // 6. Fuel Dispensed Summary
            var dsrFuelSales = report.DsrFuelSales ?? report.FuelSales;
            var dsrTotalLitres = report.DsrFuelSales != null ? report.DsrTotalFuelLitres : report.TotalFuelLitres;
            var dsrTotalAmount = report.DsrFuelSales != null ? report.DsrTotalFuelAmount : report.TotalFuelAmount;
            if (dsrFuelSales != null && dsrFuelSales.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "6. Fuel Dispensed Summary", maxCols);
                var fuelHeaders = new List<string> { "Product / Fuel Type", "Litres Dispensed", "Rate (₹)", "Total Amount (₹)" };
                WriteTableHeaders(worksheet, ref r, fuelHeaders);

                bool isAlt = false;
                foreach (var fs in dsrFuelSales)
                {
                    worksheet.Cell(r, 1).Value = fs.Description;
                    worksheet.Cell(r, 2).Value = fs.Litres; worksheet.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 3).Value = fs.Rate; worksheet.Cell(r, 3).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 4).Value = fs.Amount; worksheet.Cell(r, 4).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyRowBorders(worksheet, r, 4, isAlt);
                    isAlt = !isAlt;
                    r++;
                }

                worksheet.Cell(r, 1).Value = "TOTAL FUEL SALE";
                worksheet.Cell(r, 2).Value = dsrTotalLitres; worksheet.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(r, 3).Value = "";
                worksheet.Cell(r, 4).Value = dsrTotalAmount; worksheet.Cell(r, 4).Style.NumberFormat.Format = "₹#,##0.00";
                ApplyTotalRowBorders(worksheet, r, 4);
                r++;
                r++;
            }

            // 7. Final Reconciliation
            if (report.CollectionBreakdown != null && report.CollectionBreakdown.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "7. Final Reconciliation", maxCols);
                var recHeaders = new List<string> { "Category / Description", "Amount (₹)" };
                WriteTableHeaders(worksheet, ref r, recHeaders);

                bool isAlt = false;
                foreach (var cat in report.CollectionBreakdown)
                {
                    worksheet.Cell(r, 1).Value = cat.DescriptionWithBreakdown;
                    worksheet.Cell(r, 2).Value = cat.Amount; worksheet.Cell(r, 2).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyRowBorders(worksheet, r, 2, isAlt);
                    isAlt = !isAlt;
                    r++;
                }

                worksheet.Cell(r, 1).Value = "Total Amount (A)";
                worksheet.Cell(r, 2).Value = report.ActualCollection; worksheet.Cell(r, 2).Style.NumberFormat.Format = "₹#,##0.00";
                ApplyTotalRowBorders(worksheet, r, 2);
                r++;

                worksheet.Cell(r, 1).Value = "Gross Sale (B)";
                worksheet.Cell(r, 2).Value = report.ExpectedCollection; worksheet.Cell(r, 2).Style.NumberFormat.Format = "₹#,##0.00";
                ApplyTotalRowBorders(worksheet, r, 2);
                r++;

                worksheet.Cell(r, 1).Value = "Difference";
                var diffStr = report.Difference >= 0 ? $"Short ₹{Math.Abs(report.Difference):N2}" : $"Excess ₹{Math.Abs(report.Difference):N2}";
                worksheet.Cell(r, 2).Value = diffStr;
                worksheet.Cell(r, 2).Style.Font.Bold = true;
                worksheet.Cell(r, 2).Style.Font.FontColor = report.Difference > 0.01 ? XLColor.Red : XLColor.FromHtml("#2E7D32");
                ApplyTotalRowBorders(worksheet, r, 2);
                r++;
                r++;
            }

            WriteSignatures(worksheet, ref r);

            worksheet.Columns(1, maxCols).AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        });
    }

    public async Task<string> ExportDayTotalReportAsync(DayReportDto report, string? customFilePath = null)
    {
        return await Task.Run(() =>
        {
            string filePath = customFilePath ?? "";
            if (string.IsNullOrWhiteSpace(filePath))
            {
                var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloadsPath)) downloadsPath = AppDomain.CurrentDomain.BaseDirectory;
                var fileName = $"DayTotal_{report.StartDate:yyyyMMdd}_to_{report.EndDate:yyyyMMdd}_{DateTime.Now:HHmmss}.xlsx";
                filePath = Path.Combine(downloadsPath, fileName);
            }

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Day Total");
            worksheet.ShowGridLines = true;

            int r = 1;
            int maxCols = 13;

            // Header Block
            worksheet.Cell(r, 1).Value = string.IsNullOrWhiteSpace(report.StationName) ? "Mitali Service Station" : report.StationName;
            worksheet.Cell(r, 1).Style.Font.Bold = true;
            worksheet.Cell(r, 1).Style.Font.FontSize = 16;
            worksheet.Cell(r, 1).Style.Font.FontColor = XLColor.White;
            worksheet.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#004D40");
            worksheet.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(r, 1, r, maxCols).Merge();
            r++;

            worksheet.Cell(r, 1).Value = $"DAY TOTAL RECONCILIATION REPORT — {report.DateString}";
            worksheet.Cell(r, 1).Style.Font.Bold = true;
            worksheet.Cell(r, 1).Style.Font.FontSize = 12;
            worksheet.Cell(r, 1).Style.Font.FontColor = XLColor.White;
            worksheet.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#00695C");
            worksheet.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(r, 1, r, maxCols).Merge();
            r++;

            var managersText = $"Shift 1 Manager: {report.Shift1Manager ?? "—"} | Shift 2 Manager: {report.Shift2Manager ?? "—"}";
            worksheet.Cell(r, 1).Value = managersText;
            worksheet.Cell(r, 1).Style.Font.Italic = true;
            worksheet.Cell(r, 1).Style.Font.FontSize = 10;
            worksheet.Cell(r, 1).Style.Font.FontColor = XLColor.White;
            worksheet.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#00796B");
            worksheet.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            worksheet.Range(r, 1, r, maxCols).Merge();
            r++;
            r++;

            // 1. Tank Dip & Stock Status
            if (report.TankSummary != null && report.TankSummary.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "1. Tank Dip & Stock Status", maxCols);
                var tankHeaders = new List<string> { "Fuel / Tank", "Dip (mm)", "Stock (Ltr)", "Opening (Ltr)", "Receipts (Ltr)", "Sales (Ltr)", "Testing (Ltr)", "Calculated Stock (Ltr)", "Variance (Ltr)" };
                WriteTableHeaders(worksheet, ref r, tankHeaders);

                bool isAlt = false;
                foreach (var tank in report.TankSummary)
                {
                    worksheet.Cell(r, 1).Value = tank.FuelType;
                    worksheet.Cell(r, 2).Value = tank.DipMm; worksheet.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 3).Value = tank.StockLtr; worksheet.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 4).Value = tank.OpeningStock; worksheet.Cell(r, 4).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 5).Value = tank.Receipts; worksheet.Cell(r, 5).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 6).Value = tank.SaleLitres; worksheet.Cell(r, 6).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 7).Value = tank.TestingLitres; worksheet.Cell(r, 7).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 8).Value = tank.CalculatedStock; worksheet.Cell(r, 8).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 9).Value = tank.StockVariance; worksheet.Cell(r, 9).Style.NumberFormat.Format = "#,##0.00";
                    ApplyRowBorders(worksheet, r, 9, isAlt);
                    isAlt = !isAlt;
                    r++;
                }
                r++;
            }

            // 2. DSM Sales & Collection Summary
            if (report.DsmSummaryRows != null && report.DsmSummaryRows.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "2. DSM Sales & Collection Summary", maxCols);
                var dsmHeaders = new List<string> { "DSM Name", "Shift", "Pump", "PhonePe", "Card", "PetroCard", "Cash 1 (Bank)", "Cash 2 (Hand)", "Debtors", "Expenses", "Testing", "Gross Sales", "Difference" };
                WriteTableHeaders(worksheet, ref r, dsmHeaders);

                bool isAlt = false;
                foreach (var row in report.DsmSummaryRows)
                {
                    worksheet.Cell(r, 1).Value = row.DsmName;
                    worksheet.Cell(r, 2).Value = string.IsNullOrWhiteSpace(row.ShiftLabel) ? "—" : $"Shift {row.ShiftLabel}";
                    worksheet.Cell(r, 3).Value = row.PumpId > 0 ? $"Pump #{row.PumpId}" : "—";
                    worksheet.Cell(r, 4).Value = row.PhonePeTotal; worksheet.Cell(r, 4).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 5).Value = row.CreditCardTotal; worksheet.Cell(r, 5).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 6).Value = row.PetroCard; worksheet.Cell(r, 6).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 7).Value = row.BankCash; worksheet.Cell(r, 7).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 8).Value = row.CashInHand; worksheet.Cell(r, 8).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 9).Value = row.DebtorSales; worksheet.Cell(r, 9).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 10).Value = row.Expenses; worksheet.Cell(r, 10).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 11).Value = row.Testing; worksheet.Cell(r, 11).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 12).Value = row.GrossSale; worksheet.Cell(r, 12).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 13).Value = row.Difference; worksheet.Cell(r, 13).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyRowBorders(worksheet, r, 13, isAlt);
                    isAlt = !isAlt;
                    r++;
                }

                if (report.DsmSummaryTotals != null)
                {
                    var t = report.DsmSummaryTotals;
                    worksheet.Cell(r, 1).Value = "TOTAL";
                    worksheet.Cell(r, 2).Value = "";
                    worksheet.Cell(r, 3).Value = "";
                    worksheet.Cell(r, 4).Value = t.PhonePeTotal; worksheet.Cell(r, 4).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 5).Value = t.CreditCardTotal; worksheet.Cell(r, 5).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 6).Value = t.PetroCard; worksheet.Cell(r, 6).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 7).Value = t.BankCash; worksheet.Cell(r, 7).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 8).Value = t.CashInHand; worksheet.Cell(r, 8).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 9).Value = t.DebtorSales; worksheet.Cell(r, 9).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 10).Value = t.Expenses; worksheet.Cell(r, 10).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 11).Value = t.Testing; worksheet.Cell(r, 11).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 12).Value = t.GrossSale; worksheet.Cell(r, 12).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 13).Value = t.Difference; worksheet.Cell(r, 13).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyTotalRowBorders(worksheet, r, 13);
                    r++;
                }
                r++;
            }

            // 3. Debtors / Credit Sales & Recoveries
            if (report.CreditorRows != null && report.CreditorRows.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "3. Debtor Credit Sales", maxCols);
                var debHeaders = new List<string> { "DSM Name", "Pump", "Debtor Name", "Cheque / Vehicle No", "Amount (₹)" };
                WriteTableHeaders(worksheet, ref r, debHeaders);

                bool isAlt = false;
                foreach (var c in report.CreditorRows)
                {
                    worksheet.Cell(r, 1).Value = c.DsmName;
                    worksheet.Cell(r, 2).Value = c.PumpId > 0 ? $"Pump #{c.PumpId}" : "—";
                    worksheet.Cell(r, 3).Value = c.DebtorName;
                    worksheet.Cell(r, 4).Value = c.ChequeNo;
                    worksheet.Cell(r, 5).Value = c.Amount; worksheet.Cell(r, 5).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyRowBorders(worksheet, r, 5, isAlt);
                    isAlt = !isAlt;
                    r++;
                }
                r++;
            }

            if (report.DebtorRepayments != null && report.DebtorRepayments.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "3B. Debtor Recoveries (Collections)", maxCols);
                var repHeaders = new List<string> { "Debtor Name", "Payment Mode", "TID / Batch / Details", "Amount (₹)" };
                WriteTableHeaders(worksheet, ref r, repHeaders);

                bool isAlt = false;
                foreach (var dr in report.DebtorRepayments)
                {
                    worksheet.Cell(r, 1).Value = dr.DebtorName;
                    worksheet.Cell(r, 2).Value = dr.PaymentMode;
                    worksheet.Cell(r, 3).Value = string.IsNullOrWhiteSpace(dr.RefNo) ? "—" : dr.RefNo;
                    worksheet.Cell(r, 4).Value = dr.Amount; worksheet.Cell(r, 4).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyRowBorders(worksheet, r, 4, isAlt);
                    isAlt = !isAlt;
                    r++;
                }
                r++;
            }

            // 4. Expenses & Oil/DEF Sales
            if (report.ExpenseRows != null && report.ExpenseRows.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "4. Day Expenses Register", maxCols);
                var expHeaders = new List<string> { "DSM / Shift", "Description", "Amount (₹)" };
                WriteTableHeaders(worksheet, ref r, expHeaders);

                bool isAlt = false;
                foreach (var exp in report.ExpenseRows)
                {
                    worksheet.Cell(r, 1).Value = exp.DsmName;
                    worksheet.Cell(r, 2).Value = exp.Description;
                    worksheet.Cell(r, 3).Value = exp.Amount; worksheet.Cell(r, 3).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyRowBorders(worksheet, r, 3, isAlt);
                    isAlt = !isAlt;
                    r++;
                }
                r++;
            }

            if (report.OilDefSales != null && report.OilDefSales.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "4B. Oil & DEF Product Sales", maxCols);
                var oilHeaders = new List<string> { "Product Name", "Category", "Quantity", "Rate (₹)", "Amount (₹)" };
                WriteTableHeaders(worksheet, ref r, oilHeaders);

                bool isAlt = false;
                foreach (var oil in report.OilDefSales)
                {
                    worksheet.Cell(r, 1).Value = oil.ProductName;
                    worksheet.Cell(r, 2).Value = oil.Category;
                    worksheet.Cell(r, 3).Value = oil.Quantity; worksheet.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 4).Value = oil.Rate; worksheet.Cell(r, 4).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 5).Value = oil.Total; worksheet.Cell(r, 5).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyRowBorders(worksheet, r, 5, isAlt);
                    isAlt = !isAlt;
                    r++;
                }
                r++;
            }

            // 5. Fuel Sales Summary
            var dayDsrFuelSales = report.DsrFuelSales ?? report.FuelSales;
            var dayDsrTotalLitres = report.DsrFuelSales != null ? report.DsrTotalFuelLitres : report.TotalFuelLitres;
            var dayDsrTotalAmount = report.DsrFuelSales != null ? report.DsrTotalFuelAmount : report.TotalFuelAmount;
            if (dayDsrFuelSales != null && dayDsrFuelSales.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "5. Fuel Dispensed Summary", maxCols);
                var fuelHeaders = new List<string> { "Product / Fuel Type", "Litres Dispensed", "Rate (₹)", "Total Amount (₹)" };
                WriteTableHeaders(worksheet, ref r, fuelHeaders);

                bool isAlt = false;
                foreach (var fs in dayDsrFuelSales)
                {
                    worksheet.Cell(r, 1).Value = fs.Description;
                    worksheet.Cell(r, 2).Value = fs.Litres; worksheet.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
                    worksheet.Cell(r, 3).Value = fs.Rate; worksheet.Cell(r, 3).Style.NumberFormat.Format = "₹#,##0.00";
                    worksheet.Cell(r, 4).Value = fs.Amount; worksheet.Cell(r, 4).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyRowBorders(worksheet, r, 4, isAlt);
                    isAlt = !isAlt;
                    r++;
                }

                worksheet.Cell(r, 1).Value = "TOTAL FUEL SALE";
                worksheet.Cell(r, 2).Value = dayDsrTotalLitres; worksheet.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(r, 3).Value = "";
                worksheet.Cell(r, 4).Value = dayDsrTotalAmount; worksheet.Cell(r, 4).Style.NumberFormat.Format = "₹#,##0.00";
                ApplyTotalRowBorders(worksheet, r, 4);
                r++;
                r++;
            }

            // 6. Final Day Reconciliation
            if (report.CollectionBreakdown != null && report.CollectionBreakdown.Count > 0)
            {
                WriteSectionHeader(worksheet, ref r, "6. Final Day Reconciliation", maxCols);
                var recHeaders = new List<string> { "Category / Description", "Amount (₹)" };
                WriteTableHeaders(worksheet, ref r, recHeaders);

                bool isAlt = false;
                foreach (var cat in report.CollectionBreakdown)
                {
                    worksheet.Cell(r, 1).Value = cat.DescriptionWithBreakdown;
                    worksheet.Cell(r, 2).Value = cat.Amount; worksheet.Cell(r, 2).Style.NumberFormat.Format = "₹#,##0.00";
                    ApplyRowBorders(worksheet, r, 2, isAlt);
                    isAlt = !isAlt;
                    r++;
                }

                worksheet.Cell(r, 1).Value = "Total Collection (A)";
                worksheet.Cell(r, 2).Value = report.ActualCollection; worksheet.Cell(r, 2).Style.NumberFormat.Format = "₹#,##0.00";
                ApplyTotalRowBorders(worksheet, r, 2);
                r++;

                worksheet.Cell(r, 1).Value = "Total Day Sale (B)";
                worksheet.Cell(r, 2).Value = report.ExpectedCollection; worksheet.Cell(r, 2).Style.NumberFormat.Format = "₹#,##0.00";
                ApplyTotalRowBorders(worksheet, r, 2);
                r++;

                worksheet.Cell(r, 1).Value = "Difference";
                var diffStr = report.Difference >= 0 ? $"Short ₹{Math.Abs(report.Difference):N2}" : $"Excess ₹{Math.Abs(report.Difference):N2}";
                worksheet.Cell(r, 2).Value = diffStr;
                worksheet.Cell(r, 2).Style.Font.Bold = true;
                worksheet.Cell(r, 2).Style.Font.FontColor = report.Difference > 0.01 ? XLColor.Red : XLColor.FromHtml("#2E7D32");
                ApplyTotalRowBorders(worksheet, r, 2);
                r++;
                r++;
            }

            WriteSignatures(worksheet, ref r);

            worksheet.Columns(1, maxCols).AdjustToContents();
            workbook.SaveAs(filePath);
            return filePath;
        });
    }

    private static void WriteSectionHeader(IXLWorksheet ws, ref int r, string title, int maxCols)
    {
        ws.Cell(r, 1).Value = title;
        ws.Cell(r, 1).Style.Font.Bold = true;
        ws.Cell(r, 1).Style.Font.FontSize = 11;
        ws.Cell(r, 1).Style.Font.FontColor = XLColor.White;
        ws.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#2E7D32");
        ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Range(r, 1, r, maxCols).Merge();
        r++;
    }

    private static void WriteTableHeaders(IXLWorksheet ws, ref int r, List<string> headers)
    {
        for (int col = 0; col < headers.Count; col++)
        {
            var cell = ws.Cell(r, col + 1);
            cell.Value = headers[col];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 10;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#00796B");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#004D40");
        }
        r++;
    }

    private static void ApplyRowBorders(IXLWorksheet ws, int r, int colCount, bool isAlt)
    {
        for (int col = 1; col <= colCount; col++)
        {
            var cell = ws.Cell(r, col);
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.LightGray;
            if (isAlt)
            {
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F9F9F9");
            }
        }
    }

    private static void ApplyTotalRowBorders(IXLWorksheet ws, int r, int colCount)
    {
        for (int col = 1; col <= colCount; col++)
        {
            var cell = ws.Cell(r, col);
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F5E9");
            cell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Double;
            cell.Style.Border.TopBorderColor = XLColor.FromHtml("#2E7D32");
            cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#2E7D32");
        }
    }

    private static void WriteSignatures(IXLWorksheet ws, ref int r)
    {
        r += 2;
        ws.Cell(r, 1).Value = "Supervisor Signature";
        ws.Range(r, 1, r, 3).Merge();
        ws.Cell(r, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(r, 1).Style.Font.Italic = true;

        ws.Cell(r, 5).Value = "Manager Signature";
        ws.Range(r, 5, r, 7).Merge();
        ws.Cell(r, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(r, 5).Style.Font.Italic = true;
        r++;
    }

    #endregion
}
