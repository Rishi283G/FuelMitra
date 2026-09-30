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
public partial class OilDefSummaryViewModel : ObservableObject, IDisposable
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

        DsmEntryService.InventoryChanged += OnDataChanged;
        DsmEntryService.DsmEntryChanged += OnDataChanged;

        _ = SetPresetAsync(SelectedPreset);
    }

    private void OnDataChanged()
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await LoadSummaryAsync());
    }

    public void Dispose()
    {
        DsmEntryService.InventoryChanged -= OnDataChanged;
        DsmEntryService.DsmEntryChanged -= OnDataChanged;
        GC.SuppressFinalize(this);
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
                    LogId = adj.Id,
                    ProductId = adj.ProductId,
                    Date = adj.LogDate,
                    ProductName = adj.Product?.ProductName ?? adj.ProductType,
                    AdjustmentQty = adj.AdjustmentQuantity,
                    RemainingStock = adj.RemainingStock,
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

    [RelayCommand]
    private async Task DeleteAdjustmentAsync(AdjustmentSummaryRow row)
    {
        if (row == null) return;

        var result = MessageBox.Show($"Are you sure you want to delete this stock adjustment of {row.AdjustmentQty:N2} unit(s) for '{row.ProductName}'?",
            "Confirm Delete Adjustment", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            var entity = await _dbContext.OilDefDailyLogs.FindAsync(row.LogId);
            if (entity != null)
            {
                var prodId = entity.ProductId;
                var logDate = entity.LogDate.Date;
                _dbContext.OilDefDailyLogs.Remove(entity);
                await _dbContext.SaveChangesAsync();
                await RecalculateRunningBalancesAsync(prodId, logDate);
            }

            await LoadSummaryAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to delete adjustment {LogId}", row.LogId);
            MessageBox.Show($"Failed to delete adjustment: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task EditAdjustmentAsync(AdjustmentSummaryRow row)
    {
        if (row == null) return;

        var editWindow = new FuelPro.UI.Views.EditStockAdjustmentWindow(row.ProductName, row.Date, row.AdjustmentQty, row.Type, row.Remarks);
        if (Application.Current?.MainWindow != null)
        {
            editWindow.Owner = Application.Current.MainWindow;
        }

        if (editWindow.ShowDialog() != true) return;

        IsLoading = true;
        try
        {
            var entity = await _dbContext.OilDefDailyLogs.FindAsync(row.LogId);
            if (entity != null)
            {
                entity.AdjustmentQuantity = editWindow.Quantity;
                entity.AdjustmentType = editWindow.AdjustmentType;
                entity.Remarks = editWindow.Remarks;
                entity.LogDate = editWindow.SelectedDate.Date.Add(entity.LogDate.TimeOfDay);
                _dbContext.Entry(entity).State = EntityState.Modified;
                await _dbContext.SaveChangesAsync();

                await RecalculateRunningBalancesAsync(entity.ProductId, entity.LogDate.Date);
            }

            await LoadSummaryAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to edit adjustment {LogId}", row.LogId);
            MessageBox.Show($"Failed to update adjustment: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RecalculateRunningBalancesAsync(int productId, DateTime fromDate)
    {
        try
        {
            var from = fromDate.Date;
            var prevLog = await _dbContext.OilDefDailyLogs
                .Where(l => l.ProductId == productId && l.LogDate.Date < from)
                .OrderByDescending(l => l.LogDate)
                .ThenByDescending(l => l.Id)
                .FirstOrDefaultAsync();

            double prevRemaining = 0.0;
            if (prevLog != null)
            {
                prevRemaining = prevLog.RemainingStock;
            }
            else
            {
                var monthInv = await _dbContext.OilDefInventories
                    .Where(i => i.ProductId == productId && i.Year == from.Year && i.Month == from.Month)
                    .FirstOrDefaultAsync();
                prevRemaining = monthInv?.OpeningStock ?? 0.0;
            }

            var subsequentLogs = await _dbContext.OilDefDailyLogs
                .Where(l => l.ProductId == productId && l.LogDate.Date >= from)
                .OrderBy(l => l.LogDate)
                .ThenBy(l => l.Id)
                .ToListAsync();

            double running = prevRemaining;
            foreach (var log in subsequentLogs)
            {
                running = running + log.AddedQuantity - log.SoldQuantity + log.AdjustmentQuantity;
                log.RemainingStock = running;
                _dbContext.Entry(log).State = EntityState.Modified;
            }

            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to recalculate running balances for product {ProductId}", productId);
        }
    }

    private async Task LoadRecentSalesAsync()
    {
        RecentSalesLogs.Clear();
        try
        {
            var start = StartDate.Date;
            var end = EndDate.Date.AddDays(1);
            var logs = await _dbContext.OilDefDailyLogs
                .Include(l => l.Product)
                .Where(l => l.LogDate >= start && l.LogDate < end
                            && l.SoldQuantity > 0)
                .OrderByDescending(l => l.LogDate)
                .ThenByDescending(l => l.Id)
                .Take(100)
                .ToListAsync();

            var products = await _dbContext.ProductMasters.ToListAsync();
            foreach (var l in logs)
            {
                if (l.Product == null && l.ProductId > 0)
                {
                    l.Product = products.FirstOrDefault(p => p.Id == l.ProductId);
                }
                RecentSalesLogs.Add(l);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load recent sales log");
        }
    }

    [RelayCommand]
    private async Task DeleteSaleLogAsync(OilDefDailyLog log)
    {
        if (log == null) return;

        var prodName = log.Product?.ProductName ?? log.ProductType;
        var result = MessageBox.Show($"Are you sure you want to delete this sales entry of {log.SoldQuantity:N2} unit(s) for '{prodName}' on {log.LogDate:dd-MMM-yyyy}?",
            "Confirm Delete Sale", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            var entity = await _dbContext.OilDefDailyLogs.FindAsync(log.Id);
            if (entity != null)
            {
                var prodId = entity.ProductId;
                var logDate = entity.LogDate.Date;

                if (entity.AddedQuantity == 0 && entity.AdjustmentQuantity == 0)
                {
                    _dbContext.OilDefDailyLogs.Remove(entity);
                }
                else
                {
                    entity.SoldQuantity = 0;
                    entity.OverrideSaleRate = null;
                    _dbContext.Entry(entity).State = EntityState.Modified;
                }

                await _dbContext.SaveChangesAsync();
                await RecalculateRunningBalancesAsync(prodId, logDate);
            }

            await LoadSummaryAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to delete sale log {LogId}", log.Id);
            MessageBox.Show($"Failed to delete sale log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task EditSaleLogAsync(OilDefDailyLog log)
    {
        if (log == null) return;

        var prodName = log.Product?.ProductName ?? log.ProductType;
        var editWindow = new FuelPro.UI.Views.EditOilDefSaleWindow(prodName, log.LogDate, log.SoldQuantity, log.EffectiveRate);
        if (Application.Current?.MainWindow != null)
        {
            editWindow.Owner = Application.Current.MainWindow;
        }

        if (editWindow.ShowDialog() != true) return;

        IsLoading = true;
        try
        {
            var entity = await _dbContext.OilDefDailyLogs.Include(l => l.Product).FirstOrDefaultAsync(l => l.Id == log.Id);
            if (entity != null)
            {
                var oldDate = entity.LogDate.Date;
                var newDate = editWindow.SelectedDate.Date;
                var prodId = entity.ProductId;
                var defaultRate = entity.Product?.DefaultSaleRate ?? 0;
                double? overrideRate = Math.Abs(editWindow.Rate - defaultRate) > 0.01 ? editWindow.Rate : null;

                entity.LogDate = newDate == DateTime.Today ? DateTime.Now : newDate;
                entity.SoldQuantity = editWindow.Quantity;
                entity.OverrideSaleRate = overrideRate;
                _dbContext.Entry(entity).State = EntityState.Modified;
                await _dbContext.SaveChangesAsync();

                if (oldDate != newDate)
                {
                    await RecalculateRunningBalancesAsync(prodId, oldDate);
                }
                await RecalculateRunningBalancesAsync(prodId, newDate);
            }

            await LoadSummaryAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to edit sale log {LogId}", log.Id);
            MessageBox.Show($"Failed to update sale log: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadRecentPurchasesAsync()
    {
        RecentPurchases.Clear();
        try
        {
            var start = StartDate.Date;
            var end = EndDate.Date.AddDays(1);
            var purchases = await _dbContext.OilDefPurchases
                .Include(p => p.Product)
                .Where(p => p.PurchaseDate >= start && p.PurchaseDate < end)
                .OrderByDescending(p => p.PurchaseDate)
                .ThenByDescending(p => p.Id)
                .Take(100)
                .ToListAsync();

            var products = await _dbContext.ProductMasters.ToListAsync();
            foreach (var p in purchases)
            {
                if (p.Product == null && p.ProductId > 0)
                {
                    p.Product = products.FirstOrDefault(prod => prod.Id == p.ProductId);
                }
                RecentPurchases.Add(p);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load recent purchases");
        }
    }

    [RelayCommand]
    private async Task DeletePurchaseAsync(OilDefPurchase purchase)
    {
        if (purchase == null) return;

        var prodName = purchase.Product?.ProductName ?? purchase.ProductType;
        var result = MessageBox.Show($"Are you sure you want to delete purchase of {purchase.Quantity:N2} unit(s) for '{prodName}' (Invoice: {purchase.InvoiceNumber}) on {purchase.PurchaseDate:dd-MMM-yyyy}?",
            "Confirm Delete Purchase", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            var entity = await _dbContext.OilDefPurchases.FindAsync(purchase.Id);
            if (entity != null)
            {
                var prodId = entity.ProductId;
                var purchaseDate = entity.PurchaseDate.Date;

                _dbContext.OilDefPurchases.Remove(entity);
                await _dbContext.SaveChangesAsync();

                // Recalculate day's AddedQuantity on OilDefDailyLog
                var dayPurchasesSum = await _dbContext.OilDefPurchases
                    .Where(p => p.ProductId == prodId && p.PurchaseDate == purchaseDate)
                    .SumAsync(p => p.Quantity);

                var dailyLog = await _dbContext.OilDefDailyLogs
                    .FirstOrDefaultAsync(l => l.ProductId == prodId && l.LogDate == purchaseDate);

                if (dailyLog != null)
                {
                    dailyLog.AddedQuantity = dayPurchasesSum;
                    if (dailyLog.AddedQuantity == 0 && dailyLog.SoldQuantity == 0 && dailyLog.AdjustmentQuantity == 0)
                    {
                        _dbContext.OilDefDailyLogs.Remove(dailyLog);
                    }
                    else
                    {
                        _dbContext.Entry(dailyLog).State = EntityState.Modified;
                    }
                    await _dbContext.SaveChangesAsync();
                }

                await RecalculateRunningBalancesAsync(prodId, purchaseDate);
            }

            await LoadSummaryAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to delete purchase {PurchaseId}", purchase.Id);
            MessageBox.Show($"Failed to delete purchase: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task EditPurchaseAsync(OilDefPurchase purchase)
    {
        if (purchase == null) return;

        var prodName = purchase.Product?.ProductName ?? purchase.ProductType;
        var editWindow = new FuelPro.UI.Views.EditOilDefPurchaseWindow(prodName, purchase.PurchaseDate, purchase.SupplierName, purchase.InvoiceNumber, purchase.Quantity, purchase.UnitPrice);
        if (Application.Current?.MainWindow != null)
        {
            editWindow.Owner = Application.Current.MainWindow;
        }

        if (editWindow.ShowDialog() != true) return;

        IsLoading = true;
        try
        {
            var entity = await _dbContext.OilDefPurchases.FindAsync(purchase.Id);
            if (entity != null)
            {
                var prodId = entity.ProductId;
                var oldDate = entity.PurchaseDate.Date;
                var newDate = editWindow.SelectedDate.Date;

                entity.PurchaseDate = newDate;
                entity.SupplierName = editWindow.SupplierName;
                entity.InvoiceNumber = editWindow.InvoiceNumber;
                entity.Quantity = editWindow.Quantity;
                entity.UnitPrice = editWindow.UnitPrice;
                entity.TotalCost = editWindow.TotalCost;
                _dbContext.Entry(entity).State = EntityState.Modified;
                await _dbContext.SaveChangesAsync();

                if (oldDate != newDate)
                {
                    // Recalculate old day
                    var oldSum = await _dbContext.OilDefPurchases
                        .Where(p => p.ProductId == prodId && p.PurchaseDate == oldDate)
                        .SumAsync(p => p.Quantity);

                    var oldLog = await _dbContext.OilDefDailyLogs
                        .FirstOrDefaultAsync(l => l.ProductId == prodId && l.LogDate == oldDate);

                    if (oldLog != null)
                    {
                        oldLog.AddedQuantity = oldSum;
                        if (oldLog.AddedQuantity == 0 && oldLog.SoldQuantity == 0 && oldLog.AdjustmentQuantity == 0)
                        {
                            _dbContext.OilDefDailyLogs.Remove(oldLog);
                        }
                        else
                        {
                            _dbContext.Entry(oldLog).State = EntityState.Modified;
                        }
                        await _dbContext.SaveChangesAsync();
                    }
                    await RecalculateRunningBalancesAsync(prodId, oldDate);
                }

                // Recalculate new day
                var newSum = await _dbContext.OilDefPurchases
                    .Where(p => p.ProductId == prodId && p.PurchaseDate == newDate)
                    .SumAsync(p => p.Quantity);

                var newLog = await _dbContext.OilDefDailyLogs
                    .FirstOrDefaultAsync(l => l.ProductId == prodId && l.LogDate == newDate);

                if (newLog != null)
                {
                    newLog.AddedQuantity = newSum;
                    _dbContext.Entry(newLog).State = EntityState.Modified;
                }
                else if (newSum > 0)
                {
                    newLog = new OilDefDailyLog
                    {
                        LogDate = newDate,
                        ProductId = prodId,
                        ProductType = entity.ProductType,
                        AddedQuantity = newSum
                    };
                    _dbContext.OilDefDailyLogs.Add(newLog);
                }
                await _dbContext.SaveChangesAsync();
                await RecalculateRunningBalancesAsync(prodId, newDate);
            }

            await LoadSummaryAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to edit purchase {PurchaseId}", purchase.Id);
            MessageBox.Show($"Failed to update purchase: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PrintReportAsync()
    {
        try
        {
            IsLoading = true;

            var start = StartDate.Date;
            var endOfDay = EndDate.Date.AddDays(1);

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
                        .Where(l => l.ProductId == product.Id && l.LogDate < start)
                        .OrderByDescending(l => l.LogDate)
                        .ThenByDescending(l => l.Id)
                        .FirstOrDefaultAsync();
                    double opening = lastLogBefore?.RemainingStock ?? 
                                     (await _dbContext.OilDefInventories
                                         .Where(i => i.ProductId == product.Id && i.Year == StartDate.Year && i.Month == StartDate.Month)
                                         .Select(i => (double?)i.OpeningStock)
                                         .FirstOrDefaultAsync()) ?? 0.0;

                    // closing
                    var lastLogInRange = await _dbContext.OilDefDailyLogs
                        .Where(l => l.ProductId == product.Id && l.LogDate >= start && l.LogDate < endOfDay)
                        .OrderByDescending(l => l.LogDate)
                        .ThenByDescending(l => l.Id)
                        .FirstOrDefaultAsync();
                    double closing = lastLogInRange?.RemainingStock ?? opening;

                    // purchases
                    var purchases = await _dbContext.OilDefPurchases
                        .Where(p => p.ProductId == product.Id && p.PurchaseDate >= start && p.PurchaseDate < endOfDay)
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
                            .Where(p => p.ProductId == product.Id && p.PurchaseDate < start)
                            .OrderByDescending(p => p.PurchaseDate)
                            .FirstOrDefaultAsync();
                        avgCost = lastPurchase?.UnitPrice ?? product.DefaultSaleRate * 0.8;
                    }

                    // sales and adjustments
                    var logs = await _dbContext.OilDefDailyLogs
                        .Where(l => l.ProductId == product.Id && l.LogDate >= start && l.LogDate < endOfDay)
                        .ToListAsync();
                    double sQty = logs.Sum(l => l.SoldQuantity);
                    double sVal = logs.Sum(l => l.SoldQuantity * (l.OverrideSaleRate ?? product.DefaultSaleRate));
                    double adjQty = logs.Sum(l => l.AdjustmentQuantity);

                    double profit = sVal - (sQty * avgCost);

                    rows.Add(new
                    {
                        productName = product.ProductName,
                        unit = product.Unit,
                        openingStock = opening,
                        purchasedQty = pQty,
                        soldQty = sQty,
                        adjustedQty = adjQty,
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

            // Fetch adjustments in range
            var adjustmentsList = await _dbContext.OilDefDailyLogs
                .Include(l => l.Product)
                .Where(l => l.LogDate >= start && l.LogDate < endOfDay && l.AdjustmentQuantity != 0)
                .OrderByDescending(l => l.LogDate)
                .ThenByDescending(l => l.Id)
                .Select(adj => new
                {
                    date = adj.LogDate.ToString("dd-MMM-yyyy hh:mm tt"),
                    productName = adj.Product != null ? adj.Product.ProductName : adj.ProductType,
                    category = adj.Product != null ? adj.Product.Category : adj.ProductType,
                    unit = adj.Product != null ? adj.Product.Unit : "Units",
                    adjustmentQty = adj.AdjustmentQuantity,
                    remainingStock = adj.RemainingStock,
                    type = !string.IsNullOrWhiteSpace(adj.AdjustmentType) ? adj.AdjustmentType : "—",
                    remarks = adj.Remarks ?? ""
                })
                .ToListAsync();

            var oilProdIds = oilProducts.Select(p => p.Id).ToList();
            var defProdIds = defProducts.Select(p => p.Id).ToList();

            var oilAdjustedQty = await _dbContext.OilDefDailyLogs
                .Where(l => l.LogDate >= start && l.LogDate < endOfDay && oilProdIds.Contains(l.ProductId))
                .SumAsync(l => l.AdjustmentQuantity);

            var defAdjustedQty = await _dbContext.OilDefDailyLogs
                .Where(l => l.LogDate >= start && l.LogDate < endOfDay && defProdIds.Contains(l.ProductId))
                .SumAsync(l => l.AdjustmentQuantity);

            var settings = await _dbContext.Settings.FirstOrDefaultAsync();
            var stationName = settings?.StationDisplayName ?? "Mitali Service Station";

            var payload = new
            {
                stationName = stationName,
                startDate = StartDate.ToString("dd-MMM-yyyy"),
                endDate = EndDate.ToString("dd-MMM-yyyy"),
                oilProducts = oilRows,
                defProducts = defRows,
                adjustments = adjustmentsList,
                totalAdjustmentLoss = TotalAdjustmentLoss,
                totalSalesRevenue = TotalSalesRevenue,
                totalProfit = TotalProfit,
                oilSummary = new
                {
                    openingStock = OilOpening,
                    purchasedQty = OilPurchasedQty,
                    soldQty = OilSoldQty,
                    adjustedQty = oilAdjustedQty,
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
                    adjustedQty = defAdjustedQty,
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
            var start = StartDate.Date;
            var endOfDay = EndDate.Date.AddDays(1);
            var products = await _dbContext.ProductMasters.Where(p => p.IsActive).ToListAsync();
            
            foreach (var product in products)
            {
                // opening
                var lastLogBefore = await _dbContext.OilDefDailyLogs
                    .Where(l => l.ProductId == product.Id && l.LogDate < start)
                    .OrderByDescending(l => l.LogDate)
                    .ThenByDescending(l => l.Id)
                    .FirstOrDefaultAsync();
                double opening = lastLogBefore?.RemainingStock ?? 
                                 (await _dbContext.OilDefInventories
                                     .Where(i => i.ProductId == product.Id && i.Year == StartDate.Year && i.Month == StartDate.Month)
                                     .Select(i => (double?)i.OpeningStock)
                                     .FirstOrDefaultAsync()) ?? 0.0;

                // closing
                var lastLogInRange = await _dbContext.OilDefDailyLogs
                    .Where(l => l.ProductId == product.Id && l.LogDate >= start && l.LogDate < endOfDay)
                    .OrderByDescending(l => l.LogDate)
                    .ThenByDescending(l => l.Id)
                    .FirstOrDefaultAsync();
                double closing = lastLogInRange?.RemainingStock ?? opening;

                // purchases
                var purchases = await _dbContext.OilDefPurchases
                    .Where(p => p.ProductId == product.Id && p.PurchaseDate >= start && p.PurchaseDate < endOfDay)
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
                        .Where(p => p.ProductId == product.Id && p.PurchaseDate < start)
                        .OrderByDescending(p => p.PurchaseDate)
                        .FirstOrDefaultAsync();
                    avgCost = lastPurchase?.UnitPrice ?? product.DefaultSaleRate * 0.8;
                }

                // sales and adjustments
                var logs = await _dbContext.OilDefDailyLogs
                    .Where(l => l.ProductId == product.Id && l.LogDate >= start && l.LogDate < endOfDay)
                    .ToListAsync();
                double sQty = logs.Sum(l => l.SoldQuantity);
                double sVal = logs.Sum(l => l.SoldQuantity * (l.OverrideSaleRate ?? product.DefaultSaleRate));
                double adjQty = logs.Sum(l => l.AdjustmentQuantity);

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
                    AdjustedQty = adjQty,
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
            var stationName = settings?.StationDisplayName ?? "Mitali Service Station";

            var data = new GenericGridPrintData
            {
                Title = stationName,
                Subtitle = $"Oil & DEF Product-wise Summary: {PeriodLabel}",
                Headers = new List<string> { "Product Name", "Category", "Unit", "Opening Stock", "Purchased Qty", "Purchase Value", "Sold Qty", "Sales Value", "Adjusted Qty", "Closing Stock", "Profit" },
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
                    r.AdjustedQty.ToString("N2"),
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
    public double AdjustedQty { get; set; }
    public double ClosingStock { get; set; }
    public double Profit { get; set; }
}

/// <summary>
/// Simple row DTO for displaying stock adjustment/loss details.
/// </summary>
public class AdjustmentSummaryRow
{
    public int LogId { get; set; }
    public int ProductId { get; set; }
    public DateTime Date { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public double AdjustmentQty { get; set; }
    public double RemainingStock { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
}
