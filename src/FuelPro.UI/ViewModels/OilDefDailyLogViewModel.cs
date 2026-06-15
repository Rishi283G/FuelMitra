using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
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

public partial class OilDefDailyLogViewModel : ObservableObject
{
    private readonly FuelProDbContext _dbContext;
    private readonly IFinancialCalculationService _financialCalcService;
    private readonly PrintService _printService;
    private readonly System.Threading.SemaphoreSlim _dbLock = new(1, 1);
    private bool _isApplyingPreset;

    [ObservableProperty] private DateTime _startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private string _selectedPreset = "Monthly";
    [ObservableProperty] private bool _isLoading;

    // Oil Summary
    [ObservableProperty] private double _oilOpening;
    [ObservableProperty] private double _oilPurchasedQty;
    [ObservableProperty] private double _oilSoldQty;
    [ObservableProperty] private double _oilClosing;
    [ObservableProperty] private double _oilPurchaseValue;
    [ObservableProperty] private double _oilSalesValue;
    [ObservableProperty] private double _oilGrossProfit;
    [ObservableProperty] private double _oilNetProfit;

    // DEF Summary
    [ObservableProperty] private double _defOpening;
    [ObservableProperty] private double _defPurchasedQty;
    [ObservableProperty] private double _defSoldQty;
    [ObservableProperty] private double _defClosing;
    [ObservableProperty] private double _defPurchaseValue;
    [ObservableProperty] private double _defSalesValue;
    [ObservableProperty] private double _defGrossProfit;
    [ObservableProperty] private double _defNetProfit;

    // Combined Summary
    [ObservableProperty] private double _totalOilSales;
    [ObservableProperty] private double _totalDefSales;
    [ObservableProperty] private double _totalOilProfit;
    [ObservableProperty] private double _totalDefProfit;

    // Active Product Catalog
    public ObservableCollection<ProductMaster> ActiveProducts { get; } = new();
    [ObservableProperty] private ProductMaster? _selectedProduct;
    [ObservableProperty] private ProductMaster? _selectedProductForPurchase;

    // Record Sales Form
    [ObservableProperty] private DateTime _saleDate = DateTime.Today;
    [ObservableProperty] private double _saleQty;
    [ObservableProperty] private double _saleRate;

    // Add Purchases Form
    [ObservableProperty] private DateTime _purchaseDate = DateTime.Today;
    [ObservableProperty] private string _purchaseSupplierName = string.Empty;
    [ObservableProperty] private string _purchaseInvoiceNumber = string.Empty;
    [ObservableProperty] private double _purchaseQty;
    [ObservableProperty] private double _purchaseUnitPrice;

    // Stock Adjustments Form
    [ObservableProperty] private DateTime _adjustmentDate = DateTime.Today;
    [ObservableProperty] private double _adjustmentQty;
    [ObservableProperty] private string _selectedAdjustmentType = "Physical Count Correction";
    [ObservableProperty] private string _adjustmentRemarks = string.Empty;

    public ObservableCollection<string> AdjustmentTypes { get; } = new()
    {
        "Physical Count Correction",
        "Damaged Stock",
        "Expired Item",
        "Supplier Return",
        "Opening Balance Correction"
    };

    // Product Catalog Entry Form
    [ObservableProperty] private string _newProductName = string.Empty;
    [ObservableProperty] private string _newProductCategory = "Oil"; // "Oil" or "DEF"
    [ObservableProperty] private string _newProductUnit = "Litre"; // "Litre", "Bottle", "Piece"
    [ObservableProperty] private double _newProductDefaultSaleRate;

    public ObservableCollection<string> ProductCategories { get; } = new() { "Oil", "DEF" };
    public ObservableCollection<string> ProductUnits { get; } = new() { "Litre", "Bottle", "Piece" };

    // Grids History Lists
    public ObservableCollection<OilDefDailyLog> HistoryLogs { get; } = new();
    public ObservableCollection<OilDefPurchase> HistoryPurchases { get; } = new();

    public OilDefDailyLogViewModel()
    {
        _dbContext = App.Services.GetRequiredService<FuelProDbContext>();
        _financialCalcService = App.Services.GetRequiredService<IFinancialCalculationService>();
        _printService = new PrintService();

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        await LoadProductsAsync();
        await SetPresetAsync(SelectedPreset);
    }

    [RelayCommand]
    public async Task LoadProductsAsync()
    {
        try
        {
            ActiveProducts.Clear();
            var products = await _dbContext.ProductMasters
                .Where(p => p.IsActive)
                .OrderBy(p => p.ProductName)
                .ToListAsync();
            foreach (var p in products) ActiveProducts.Add(p);
            SelectedProduct = ActiveProducts.FirstOrDefault();
            SelectedProductForPurchase = ActiveProducts.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load active products");
        }
    }

    /// <summary>
    /// Persists the DefaultSaleRate values from the top rate card back to ProductMaster.
    /// Called when manager clicks "Save Rates".
    /// </summary>
    [RelayCommand]
    private async Task SaveProductRatesAsync()
    {
        try
        {
            foreach (var product in ActiveProducts)
            {
                var tracked = await _dbContext.ProductMasters.FindAsync(product.Id);
                if (tracked != null)
                {
                    tracked.DefaultSaleRate = product.DefaultSaleRate;
                    _dbContext.Entry(tracked).State = EntityState.Modified;
                }
            }
            await _dbContext.SaveChangesAsync();
            MessageBox.Show("Sale rates saved successfully.", "FuelPro — Rates Updated", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save product rates");
            MessageBox.Show($"Error saving rates: {ex.Message}", "FuelPro — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
            }
        }
        finally
        {
            _isApplyingPreset = false;
        }
        await LoadDashboardDataAsync();
        await LoadHistoryAsync();
    }

    partial void OnStartDateChanged(DateTime value)
    {
        if (!_isApplyingPreset)
            _ = LoadDashboardDataAsync();
    }

    partial void OnEndDateChanged(DateTime value)
    {
        if (!_isApplyingPreset)
            _ = LoadDashboardDataAsync();
    }
    partial void OnSelectedProductChanged(ProductMaster? value)
    {
        if (value != null)
        {
            SaleRate = value.DefaultSaleRate;
        }
    }

    [RelayCommand]
    public async Task LoadDashboardDataAsync()
    {
        await _dbLock.WaitAsync();
        try
        {
            IsLoading = true;
            var result = await _financialCalcService.CalculateFinancialsAsync(StartDate, EndDate);

            // Oil
            OilOpening = result.OilProfit.OpeningStock;
            OilPurchasedQty = result.OilProfit.PurchasesQuantity;
            OilSoldQty = result.OilProfit.SalesQuantity;
            OilClosing = result.OilProfit.ClosingStock;
            OilPurchaseValue = result.OilProfit.CostOfGoodsSold; // or PurchasesCost
            OilSalesValue = result.OilProfit.SalesRevenue;
            OilGrossProfit = result.OilProfit.TotalProfit;
            OilNetProfit = result.OilProfit.TotalProfit;

            // DEF
            DefOpening = result.DefProfit.OpeningStock;
            DefPurchasedQty = result.DefProfit.PurchasesQuantity;
            DefSoldQty = result.DefProfit.SalesQuantity;
            DefClosing = result.DefProfit.ClosingStock;
            DefPurchaseValue = result.DefProfit.CostOfGoodsSold;
            DefSalesValue = result.DefProfit.SalesRevenue;
            DefGrossProfit = result.DefProfit.TotalProfit;
            DefNetProfit = result.DefProfit.TotalProfit;

            // Combined
            TotalOilSales = OilSalesValue;
            TotalDefSales = DefSalesValue;
            TotalOilProfit = OilGrossProfit;
            TotalDefProfit = DefGrossProfit;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load dashboard data");
        }
        finally
        {
            IsLoading = false;
            _dbLock.Release();
        }
    }

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        await _dbLock.WaitAsync();
        try
        {
            HistoryLogs.Clear();
            var logs = await _dbContext.OilDefDailyLogs
                .Include(l => l.Product)
                .OrderByDescending(l => l.LogDate)
                .Take(50)
                .ToListAsync();
            foreach (var l in logs) HistoryLogs.Add(l);

            HistoryPurchases.Clear();
            var purchases = await _dbContext.OilDefPurchases
                .Include(p => p.Product)
                .OrderByDescending(p => p.PurchaseDate)
                .Take(50)
                .ToListAsync();
            foreach (var p in purchases) HistoryPurchases.Add(p);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load history lists");
        }
        finally
        {
            _dbLock.Release();
        }
    }

    [RelayCommand]
    private async Task SaveSaleAsync()
    {
        if (SelectedProduct == null)
        {
            MessageBox.Show("Please select a product to log sales.", "FuelPro — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (SaleQty <= 0)
        {
            MessageBox.Show("Sales quantity must be greater than zero.", "FuelPro — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            var log = await _dbContext.OilDefDailyLogs
                .FirstOrDefaultAsync(l => l.ProductId == SelectedProduct.Id && l.LogDate == SaleDate.Date);

            if (log != null)
            {
                log.SoldQuantity = SaleQty;
                log.OverrideSaleRate = Math.Abs(SaleRate - SelectedProduct.DefaultSaleRate) > 0.01 ? SaleRate : null;
                _dbContext.Entry(log).State = EntityState.Modified;
            }
            else
            {
                log = new OilDefDailyLog
                {
                    LogDate = SaleDate.Date,
                    ProductId = SelectedProduct.Id,
                    ProductType = SelectedProduct.Category,
                    SoldQuantity = SaleQty,
                    OverrideSaleRate = Math.Abs(SaleRate - SelectedProduct.DefaultSaleRate) > 0.01 ? SaleRate : null
                };
                _dbContext.OilDefDailyLogs.Add(log);
            }

            await _dbContext.SaveChangesAsync();
            await RecalculateRunningBalancesAsync(SelectedProduct.Id, SaleDate.Date);

            MessageBox.Show("Sales logged and stock recalculated successfully!", "FuelPro — Success", MessageBoxButton.OK, MessageBoxImage.Information);
            
            // Reset
            SaleQty = 0;
            SaleRate = SelectedProduct.DefaultSaleRate;

            await LoadDashboardDataAsync();
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save sale");
            MessageBox.Show($"Error saving sale: {ex.Message}", "FuelPro — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task SavePurchaseAsync()
    {
        if (SelectedProductForPurchase == null)
        {
            MessageBox.Show("Please select a product.", "FuelPro — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(PurchaseSupplierName) || string.IsNullOrWhiteSpace(PurchaseInvoiceNumber))
        {
            MessageBox.Show("Supplier Name and Invoice Number are required.", "FuelPro — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (PurchaseQty <= 0 || PurchaseUnitPrice <= 0)
        {
            MessageBox.Show("Quantity and Unit Price must be greater than zero.", "FuelPro — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            var newPurchase = new OilDefPurchase
            {
                ProductId = SelectedProductForPurchase!.Id,
                ProductType = SelectedProductForPurchase.Category,
                PurchaseDate = PurchaseDate.Date,
                SupplierName = PurchaseSupplierName,
                InvoiceNumber = PurchaseInvoiceNumber,
                Quantity = PurchaseQty,
                UnitPrice = PurchaseUnitPrice,
                TotalCost = Math.Round(PurchaseQty * PurchaseUnitPrice, 2)
            };
            _dbContext.OilDefPurchases.Add(newPurchase);
            await _dbContext.SaveChangesAsync();

            // Aggregate total purchases of this product on this day to update AddedQuantity in log
            var totalPurchasedOnDay = await _dbContext.OilDefPurchases
                .Where(p => p.ProductId == SelectedProductForPurchase.Id && p.PurchaseDate == PurchaseDate.Date)
                .SumAsync(p => p.Quantity);

            var log = await _dbContext.OilDefDailyLogs
                .FirstOrDefaultAsync(l => l.ProductId == SelectedProductForPurchase.Id && l.LogDate == PurchaseDate.Date);

            if (log != null)
            {
                log.AddedQuantity = totalPurchasedOnDay;
                _dbContext.Entry(log).State = EntityState.Modified;
            }
            else
            {
                log = new OilDefDailyLog
                {
                    LogDate = PurchaseDate.Date,
                    ProductId = SelectedProductForPurchase.Id,
                    ProductType = SelectedProductForPurchase.Category,
                    AddedQuantity = totalPurchasedOnDay
                };
                _dbContext.OilDefDailyLogs.Add(log);
            }

            await _dbContext.SaveChangesAsync();
            await RecalculateRunningBalancesAsync(SelectedProductForPurchase.Id, PurchaseDate.Date);

            MessageBox.Show("Purchase invoice logged and stock updated!", "FuelPro — Success", MessageBoxButton.OK, MessageBoxImage.Information);

            // Reset
            PurchaseSupplierName = string.Empty;
            PurchaseInvoiceNumber = string.Empty;
            PurchaseQty = 0;
            PurchaseUnitPrice = 0;

            await LoadDashboardDataAsync();
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save purchase");
            MessageBox.Show($"Error saving purchase: {ex.Message}", "FuelPro — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task SaveAdjustmentAsync()
    {
        if (SelectedProduct == null)
        {
            MessageBox.Show("Please select a product.", "FuelPro — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (AdjustmentQty == 0)
        {
            MessageBox.Show("Adjustment quantity cannot be zero.", "FuelPro — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            var log = await _dbContext.OilDefDailyLogs
                .FirstOrDefaultAsync(l => l.ProductId == SelectedProduct.Id && l.LogDate == AdjustmentDate.Date);

            if (log != null)
            {
                log.AdjustmentQuantity = AdjustmentQty;
                log.AdjustmentType = SelectedAdjustmentType;
                log.Remarks = AdjustmentRemarks;
                _dbContext.Entry(log).State = EntityState.Modified;
            }
            else
            {
                log = new OilDefDailyLog
                {
                    LogDate = AdjustmentDate.Date,
                    ProductId = SelectedProduct.Id,
                    ProductType = SelectedProduct.Category,
                    AdjustmentQuantity = AdjustmentQty,
                    AdjustmentType = SelectedAdjustmentType,
                    Remarks = AdjustmentRemarks
                };
                _dbContext.OilDefDailyLogs.Add(log);
            }

            await _dbContext.SaveChangesAsync();
            await RecalculateRunningBalancesAsync(SelectedProduct.Id, AdjustmentDate.Date);

            MessageBox.Show("Stock adjustment applied successfully!", "FuelPro — Success", MessageBoxButton.OK, MessageBoxImage.Information);

            // Reset
            AdjustmentQty = 0;
            AdjustmentRemarks = string.Empty;

            await LoadDashboardDataAsync();
            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save adjustment");
            MessageBox.Show($"Error saving adjustment: {ex.Message}", "FuelPro — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task SaveProductMasterAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProductName))
        {
            MessageBox.Show("Product Name is required.", "FuelPro — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (NewProductDefaultSaleRate <= 0)
        {
            MessageBox.Show("Default Sale Rate must be greater than zero.", "FuelPro — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            var product = new ProductMaster
            {
                ProductName = NewProductName,
                Category = NewProductCategory,
                Unit = NewProductUnit,
                DefaultSaleRate = NewProductDefaultSaleRate,
                IsActive = true
            };
            _dbContext.ProductMasters.Add(product);
            await _dbContext.SaveChangesAsync();

            MessageBox.Show("Product added to catalog successfully!", "FuelPro — Success", MessageBoxButton.OK, MessageBoxImage.Information);

            // Reset
            NewProductName = string.Empty;
            NewProductDefaultSaleRate = 0;

            await LoadProductsAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save product master");
            MessageBox.Show($"Error saving product: {ex.Message}", "FuelPro — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task PrintReportAsync()
    {
        IsLoading = true;
        try
        {
            // Build the print DTO structure
            var oilProductsList = new List<object>();
            var defProductsList = new List<object>();

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
                    double opening = lastLogBefore?.RemainingStock ?? 0.0;

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

            var payload = new
            {
                stationName = _dbContext.Settings.Select(s => s.PumpStationName).FirstOrDefault() ?? "VKD Petroleum",
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
                    salesValue = OilSalesValue,
                    profit = OilGrossProfit
                },
                defSummary = new
                {
                    openingStock = DefOpening,
                    purchasedQty = DefPurchasedQty,
                    soldQty = DefSoldQty,
                    closingStock = DefClosing,
                    purchaseValue = DefPurchaseValue,
                    salesValue = DefSalesValue,
                    profit = DefGrossProfit
                }
            };

            _printService.PrintOilDefSummary(payload);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print Oil & DEF summary report");
            MessageBox.Show($"Failed to print report: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsLoading = false; }
    }

    private async Task RecalculateRunningBalancesAsync(int productId, DateTime fromDate)
    {
        try
        {
            var prevRemaining = await _dbContext.OilDefDailyLogs
                .Where(l => l.ProductId == productId && l.LogDate < fromDate)
                .OrderByDescending(l => l.LogDate)
                .Select(l => l.RemainingStock)
                .FirstOrDefaultAsync();

            var subsequentLogs = await _dbContext.OilDefDailyLogs
                .Where(l => l.ProductId == productId && l.LogDate >= fromDate)
                .OrderBy(l => l.LogDate)
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
}
