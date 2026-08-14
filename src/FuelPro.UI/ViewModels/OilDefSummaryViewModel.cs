using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Core.DTOs;
using FuelPro.Data;
using FuelPro.UI.Printing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FuelPro.UI.ViewModels;

/// <summary>
/// Owner-only read-only summary for Oil &amp; DEF operations.
/// Shows sales, profit, stock and losses. No data entry.
/// </summary>
public partial class OilDefSummaryViewModel : ObservableObject
{
    private readonly FuelProDbContext _dbContext;
    private readonly IFinancialCalculationService _financialCalcService;
    private readonly PrintService _printService;
    private readonly ExcelExportService _excelExportService;
    private readonly System.Threading.SemaphoreSlim _dbLock = new(1, 1);
    private bool _isApplyingPreset;

    [ObservableProperty] private DateTime _startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private string _selectedPreset = "Monthly";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _periodLabel = string.Empty;

    // Oil Summary
    [ObservableProperty] private double _oilOpening;
    [ObservableProperty] private double _oilPurchasedQty;
    [ObservableProperty] private double _oilSoldQty;
    [ObservableProperty] private double _oilClosing;
    [ObservableProperty] private double _oilPurchaseValue;
    [ObservableProperty] private double _oilSalesRevenue;
    [ObservableProperty] private double _oilProfit;

    // DEF Summary
    [ObservableProperty] private double _defOpening;
    [ObservableProperty] private double _defPurchasedQty;
    [ObservableProperty] private double _defSoldQty;
    [ObservableProperty] private double _defClosing;
    [ObservableProperty] private double _defPurchaseValue;
    [ObservableProperty] private double _defSalesRevenue;
    [ObservableProperty] private double _defProfit;

    // Combined
    [ObservableProperty] private double _totalSalesRevenue;
    [ObservableProperty] private double _totalProfit;
    [ObservableProperty] private double _totalAdjustmentLoss;

    // Product-wise breakdown
    public ObservableCollection<OilDefProductSummaryRow> ProductBreakdownRows { get; } = new();

    // Adjustment (damage/loss) details
    public ObservableCollection<AdjustmentSummaryRow> AdjustmentRows { get; } = new();

    // Recent daily sales log (read-only)
    public ObservableCollection<OilDefDailyLog> RecentSalesLogs { get; } = new();

    // Recent purchases registry (read-only)
    public ObservableCollection<OilDefPurchase> RecentPurchases { get; } = new();

    public OilDefSummaryViewModel()
    {
        _dbContext = App.Services.GetRequiredService<FuelProDbContext>();
        _financialCalcService = App.Services.GetRequiredService<IFinancialCalculationService>();
        _excelExportService = App.Services.GetRequiredService<ExcelExportService>();
        _printService = new PrintService();

        // Wire Sync Status to trigger auto-reload
        var syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
        syncEngine.SyncStatusChanged += (status) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(async () =>
            {
                if (status.StatusMessage == "Synced")
                {
                    await LoadSummaryAsync();
                }
            });
        };

        _ = SetPresetAsync(SelectedPreset);
    }

    [RelayCommand]
    public async Task SetPresetAsync(string preset)
    {
        _isApplyingPreset = true;
        try
        {
            SelectedPreset = preset;
            switch (preset)
            {
                case "Today":
                    StartDate = DateTime.Today;
                    EndDate = DateTime.Today;
                    break;
                case "Yesterday":
                    StartDate = DateTime.Today.AddDays(-1);
                    EndDate = DateTime.Today.AddDays(-1);
                    break;
                case "Weekly":
                    StartDate = DateTime.Today.AddDays(-6);
                    EndDate = DateTime.Today;
                    break;
                case "Monthly":
                    StartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                    EndDate = DateTime.Today;
                    break;
                case "PreviousMonth":
                    var prev = DateTime.Today.AddMonths(-1);
                    StartDate = new DateTime(prev.Year, prev.Month, 1);
                    EndDate = new DateTime(prev.Year, prev.Month, DateTime.DaysInMonth(prev.Year, prev.Month));
                    break;
            }
        }
        finally
        {
            _isApplyingPreset = false;
        }
        await LoadSummaryAsync();
    }

    partial void OnStartDateChanged(DateTime value)
    {
        if (!_isApplyingPreset)
            _ = LoadSummaryAsync();
    }

    partial void OnEndDateChanged(DateTime value)
    {
        if (!_isApplyingPreset)
            _ = LoadSummaryAsync();
    }

    [RelayCommand]
    public async Task LoadSummaryAsync()
    {
        await _dbLock.WaitAsync();
        try
        {
            IsLoading = true;
            PeriodLabel = StartDate.Date == EndDate.Date
                ? StartDate.ToString("dd MMM yyyy")
                : $"{StartDate:dd MMM yyyy} — {EndDate:dd MMM yyyy}";

            var result = await _financialCalcService.CalculateFinancialsAsync(StartDate, EndDate);

            // Oil
            OilOpening = result.OilProfit.OpeningStock;
            OilPurchasedQty = result.OilProfit.PurchasesQuantity;
            OilSoldQty = result.OilProfit.SalesQuantity;
            OilClosing = result.OilProfit.ClosingStock;
            OilPurchaseValue = result.OilProfit.CostOfGoodsSold;
            OilSalesRevenue = result.OilProfit.SalesRevenue;
            OilProfit = result.OilProfit.TotalProfit;

            // DEF
            DefOpening = result.DefProfit.OpeningStock;
            DefPurchasedQty = result.DefProfit.PurchasesQuantity;
            DefSoldQty = result.DefProfit.SalesQuantity;
            DefClosing = result.DefProfit.ClosingStock;
            DefPurchaseValue = result.DefProfit.CostOfGoodsSold;
            DefSalesRevenue = result.DefProfit.SalesRevenue;
            DefProfit = result.DefProfit.TotalProfit;

            // Combined
            TotalSalesRevenue = OilSalesRevenue + DefSalesRevenue;
            TotalProfit = OilProfit + DefProfit;

            // Load adjustments (losses/damages)
            await LoadAdjustmentsAsync();

            // Load recent sales logs
            await LoadRecentSalesAsync();

            // Load recent purchase entries
            await LoadRecentPurchasesAsync();

            // Load product breakdown
            await LoadProductBreakdownAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load Oil/DEF summary");
        }
        finally 
        { 
            IsLoading = false; 
            _dbLock.Release();
        }
    }

    private async Task LoadAdjustmentsAsync()
    {
        AdjustmentRows.Clear();
        try
        {
            var start = StartDate.Date;
            var end = EndDate.Date.AddDays(1);
            var adjustments = await _dbContext.OilDefDailyLogs
                .Include(l => l.Product)
                .Where(l => l.LogDate >= start && l.LogDate < end
                            && l.AdjustmentQuantity != 0)
                .OrderByDescending(l => l.LogDate)
                .ThenByDescending(l => l.Id)
                .ToListAsync();

            double totalLoss = 0;
            foreach (var adj in adjustments)
            {
                AdjustmentRows.Add(new AdjustmentSummaryRow
                {
                    Date = adj.LogDate,
                    ProductName = adj.Product?.ProductName ?? adj.ProductType,
                    AdjustmentQty = adj.AdjustmentQuantity,
                    Type = adj.AdjustmentType ?? "—",
                    Remarks = adj.Remarks ?? ""
                });
                // Negative adjustments are losses
                if (adj.AdjustmentQuantity < 0)
                {
                    // Estimate loss value using sale rate or default
                    double rate = adj.OverrideSaleRate ?? adj.Product?.DefaultSaleRate ?? 0;
                    totalLoss += Math.Abs(adj.AdjustmentQuantity) * rate;
                }
            }
            TotalAdjustmentLoss = totalLoss;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load adjustment data");
        }
    }

    private async Task LoadRecentSalesAsync()
    {
        RecentSalesLogs.Clear();
        try
        {
            var logs = await _dbContext.OilDefDailyLogs
                .Include(l => l.Product)
                .Where(l => l.LogDate >= StartDate.Date && l.LogDate <= EndDate.Date
                            && l.SoldQuantity > 0)
                .OrderByDescending(l => l.LogDate)
                .Take(100)
                .ToListAsync();

            foreach (var l in logs) RecentSalesLogs.Add(l);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load recent sales log");
        }
    }

    private async Task LoadRecentPurchasesAsync()
    {
        RecentPurchases.Clear();
        try
        {
            var purchases = await _dbContext.OilDefPurchases
                .Include(p => p.Product)
                .Where(p => p.PurchaseDate >= StartDate.Date && p.PurchaseDate <= EndDate.Date)
                .OrderByDescending(p => p.PurchaseDate)
                .Take(100)
                .ToListAsync();

            foreach (var p in purchases) RecentPurchases.Add(p);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load recent purchases");
        }
    }

    [RelayCommand]
    private async Task PrintReportAsync()
    {
        try
        {
            IsLoading = true;

            // Build the print DTO structure
            var products = await _dbContext.ProductMasters.Where(p => p.IsActive).ToListAsync();
            var oilProducts = products.Where(p => p.Category == "Oil").ToList();
            var defProducts = products.Where(p => p.Category == "DEF").ToList();

            async Task<List<object>> BuildProductPrintRows(List<ProductMaster> prodList)
            {
                var rows = new List<object>();
                foreach (var product in prodList)
                {
                    // opening
                    var lastLogBefore = await _dbContext.OilDefDailyLogs
                        .Where(l => l.ProductId == product.Id && l.LogDate < StartDate.Date)
                        .OrderByDescending(l => l.LogDate)
                        .FirstOrDefaultAsync();
                    double opening = lastLogBefore?.RemainingStock ?? 
                                     (await _dbContext.OilDefInventories
                                         .Where(i => i.ProductId == product.Id && i.Year == StartDate.Year && i.Month == StartDate.Month)
                                         .Select(i => (double?)i.OpeningStock)
                                         .FirstOrDefaultAsync()) ?? 0.0;

                    // closing
                    var lastLogInRange = await _dbContext.OilDefDailyLogs
                        .Where(l => l.ProductId == product.Id && l.LogDate >= StartDate.Date && l.LogDate <= EndDate.Date)
                        .OrderByDescending(l => l.LogDate)
                        .FirstOrDefaultAsync();
                    double closing = lastLogInRange?.RemainingStock ?? opening;

                    // purchases
                    var purchases = await _dbContext.OilDefPurchases
                        .Where(p => p.ProductId == product.Id && p.PurchaseDate >= StartDate.Date && p.PurchaseDate <= EndDate.Date)
                        .ToListAsync();
                    double pQty = purchases.Sum(p => p.Quantity);
                    double pVal = purchases.Sum(p => p.TotalCost);

                    double avgCost = 0.0;
                    if (pQty > 0)
                    {
                        avgCost = pVal / pQty;
                    }
                    else
                    {
                        var lastPurchase = await _dbContext.OilDefPurchases
                            .Where(p => p.ProductId == product.Id && p.PurchaseDate < StartDate.Date)
                            .OrderByDescending(p => p.PurchaseDate)
                            .FirstOrDefaultAsync();
                        avgCost = lastPurchase?.UnitPrice ?? product.DefaultSaleRate * 0.8;
                    }

                    // sales
                    var logs = await _dbContext.OilDefDailyLogs
                        .Where(l => l.ProductId == product.Id && l.LogDate >= StartDate.Date && l.LogDate <= EndDate.Date)
                        .ToListAsync();
                    double sQty = logs.Sum(l => l.SoldQuantity);
                    double sVal = logs.Sum(l => l.SoldQuantity * (l.OverrideSaleRate ?? product.DefaultSaleRate));

                    double profit = sVal - (sQty * avgCost);

                    rows.Add(new
                    {
                        productName = product.ProductName,
                        unit = product.Unit,
                        openingStock = opening,
                        purchasedQty = pQty,
                        soldQty = sQty,
                        closingStock = closing,
                        purchaseValue = pVal,
                        salesValue = sVal,
                        profit = profit
                    });
                }
                return rows;
            }

            var oilRows = await BuildProductPrintRows(oilProducts);
            var defRows = await BuildProductPrintRows(defProducts);

            var settings = await _dbContext.Settings.FirstOrDefaultAsync();
            var stationName = settings?.StationDisplayName ?? "Shree Mahakaleshwar Petroleum";

            var payload = new
            {
                stationName = stationName,
                startDate = StartDate.ToString("dd-MMM-yyyy"),
                endDate = EndDate.ToString("dd-MMM-yyyy"),
                oilProducts = oilRows,
                defProducts = defRows,
                oilSummary = new
                {
                    openingStock = OilOpening,
                    purchasedQty = OilPurchasedQty,
                    soldQty = OilSoldQty,
                    closingStock = OilClosing,
                    purchaseValue = OilPurchaseValue,
                    salesValue = OilSalesRevenue,
                    profit = OilProfit
                },
                defSummary = new
                {
                    openingStock = DefOpening,
                    purchasedQty = DefPurchasedQty,
                    soldQty = DefSoldQty,
                    closingStock = DefClosing,
                    purchaseValue = DefPurchaseValue,
                    salesValue = DefSalesRevenue,
                    profit = DefProfit
                }
            };

            _printService.PrintOilDefSummary(payload);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print Oil & DEF summary report");
            MessageBox.Show($"Failed to print report: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadProductBreakdownAsync()
    {
        ProductBreakdownRows.Clear();
        try
        {
            var products = await _dbContext.ProductMasters.Where(p => p.IsActive).ToListAsync();
            
            foreach (var product in products)
            {
                // opening
                var lastLogBefore = await _dbContext.OilDefDailyLogs
                    .Where(l => l.ProductId == product.Id && l.LogDate < StartDate.Date)
                    .OrderByDescending(l => l.LogDate)
                    .FirstOrDefaultAsync();
                double opening = lastLogBefore?.RemainingStock ?? 
                                 (await _dbContext.OilDefInventories
                                     .Where(i => i.ProductId == product.Id && i.Year == StartDate.Year && i.Month == StartDate.Month)
                                     .Select(i => (double?)i.OpeningStock)
                                     .FirstOrDefaultAsync()) ?? 0.0;

                // closing
                var lastLogInRange = await _dbContext.OilDefDailyLogs
                    .Where(l => l.ProductId == product.Id && l.LogDate >= StartDate.Date && l.LogDate <= EndDate.Date)
                    .OrderByDescending(l => l.LogDate)
                    .FirstOrDefaultAsync();
                double closing = lastLogInRange?.RemainingStock ?? opening;

                // purchases
                var purchases = await _dbContext.OilDefPurchases
                    .Where(p => p.ProductId == product.Id && p.PurchaseDate >= StartDate.Date && p.PurchaseDate <= EndDate.Date)
                    .ToListAsync();
                double pQty = purchases.Sum(p => p.Quantity);
                double pVal = purchases.Sum(p => p.TotalCost);

                double avgCost = 0.0;
                if (pQty > 0)
                {
                    avgCost = pVal / pQty;
                }
                else
                {
                    var lastPurchase = await _dbContext.OilDefPurchases
                        .Where(p => p.ProductId == product.Id && p.PurchaseDate < StartDate.Date)
                        .OrderByDescending(p => p.PurchaseDate)
                        .FirstOrDefaultAsync();
                    avgCost = lastPurchase?.UnitPrice ?? product.DefaultSaleRate * 0.8;
                }

                // sales
                var logs = await _dbContext.OilDefDailyLogs
                    .Where(l => l.ProductId == product.Id && l.LogDate >= StartDate.Date && l.LogDate <= EndDate.Date)
                    .ToListAsync();
                double sQty = logs.Sum(l => l.SoldQuantity);
                double sVal = logs.Sum(l => l.SoldQuantity * (l.OverrideSaleRate ?? product.DefaultSaleRate));

                double profit = sVal - (sQty * avgCost);

                ProductBreakdownRows.Add(new OilDefProductSummaryRow
                {
                    ProductName = product.ProductName,
                    Category = product.Category,
                    Unit = product.Unit,
                    OpeningStock = opening,
                    PurchasedQty = pQty,
                    PurchaseValue = pVal,
                    SoldQty = sQty,
                    SalesValue = sVal,
                    ClosingStock = closing,
                    Profit = profit
                });
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load product breakdown");
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        try
        {
            IsLoading = true;
            var settings = await _dbContext.Settings.FirstOrDefaultAsync();
            var stationName = settings?.StationDisplayName ?? "Shree Mahakaleshwar Petroleum";

            var data = new GenericGridPrintData
            {
                Title = stationName,
                Subtitle = $"Oil & DEF Product-wise Summary: {PeriodLabel}",
                Headers = new List<string> { "Product Name", "Category", "Unit", "Opening Stock", "Purchased Qty", "Purchase Value", "Sold Qty", "Sales Value", "Closing Stock", "Profit" },
                SummaryCards = new List<GenericGridPrintCard>
                {
                    new() { Label = "Total Sales Revenue", Value = $"₹{TotalSalesRevenue:N2}", Highlight = true },
                    new() { Label = "Total Profit", Value = $"₹{TotalProfit:N2}", Highlight = false },
                    new() { Label = "Estimated Adjustment Loss", Value = $"₹{TotalAdjustmentLoss:N2}", Highlight = false }
                },
                Rows = ProductBreakdownRows.Select(r => new List<string>
                {
                    r.ProductName,
                    r.Category,
                    r.Unit,
                    r.OpeningStock.ToString("N2"),
                    r.PurchasedQty.ToString("N2"),
                    $"₹{r.PurchaseValue:N2}",
                    r.SoldQty.ToString("N2"),
                    $"₹{r.SalesValue:N2}",
                    r.ClosingStock.ToString("N2"),
                    $"₹{r.Profit:N2}"
                }).ToList()
            };

            var filePath = await _excelExportService.ExportGenericGridAsync(data, "Oil_DEF_Summary");
            MessageBox.Show($"Exported successfully to:\n{filePath}", "Export Excel", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to export Oil & DEF summary to Excel");
            MessageBox.Show($"Failed to export: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public class OilDefProductSummaryRow
{
    public string ProductName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public double OpeningStock { get; set; }
    public double PurchasedQty { get; set; }
    public double PurchaseValue { get; set; }
    public double SoldQty { get; set; }
    public double SalesValue { get; set; }
    public double ClosingStock { get; set; }
    public double Profit { get; set; }
}

/// <summary>
/// Simple row DTO for displaying stock adjustment/loss details.
/// </summary>
public class AdjustmentSummaryRow
{
    public DateTime Date { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public double AdjustmentQty { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
}
