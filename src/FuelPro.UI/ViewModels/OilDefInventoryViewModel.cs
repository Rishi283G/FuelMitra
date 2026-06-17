using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FuelPro.UI.ViewModels;

public partial class OilDefInventoryViewModel : ObservableObject
{
    private readonly FuelProDbContext _dbContext;

    [ObservableProperty] private int _selectedYear = DateTime.Today.Year;
    [ObservableProperty] private string _selectedMonthName = DateTime.Today.ToString("MMMM");
    [ObservableProperty] private bool _isLoading;

    // Oil Stock Take
    [ObservableProperty] private double _oilOpeningStock;
    [ObservableProperty] private double _oilClosingStock;
    [ObservableProperty] private double _oilSalePrice;

    // DEF Stock Take
    [ObservableProperty] private double _defOpeningStock;
    [ObservableProperty] private double _defClosingStock;
    [ObservableProperty] private double _defSalePrice;

    // New Purchase Form
    [ObservableProperty] private string _newPurchaseProductType = "Oil"; // "Oil" or "DEF"
    [ObservableProperty] private DateTime _newPurchaseDate = DateTime.Today;
    [ObservableProperty] private string _newPurchaseSupplierName = string.Empty;
    [ObservableProperty] private string _newPurchaseInvoiceNumber = string.Empty;
    [ObservableProperty] private double _newPurchaseQuantity;
    [ObservableProperty] private double _newPurchaseUnitPrice;

    public ObservableCollection<OilDefPurchase> Purchases { get; } = new();

    public ObservableCollection<int> Years { get; } = new();
    public ObservableCollection<string> Months { get; } = new();

    private readonly string[] _monthNames = {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    };

    public OilDefInventoryViewModel()
    {
        _dbContext = App.Services.GetRequiredService<FuelProDbContext>();

        // Populate Years (Current Year - 2 to Current Year + 2)
        var currYear = DateTime.Today.Year;
        for (int y = currYear - 2; y <= currYear + 2; y++)
        {
            Years.Add(y);
        }

        // Populate Months
        foreach (var m in _monthNames)
        {
            Months.Add(m);
        }

        _ = LoadDataAsync();
    }

    private int GetMonthNumber(string name)
    {
        int idx = Array.IndexOf(_monthNames, name);
        return idx >= 0 ? idx + 1 : DateTime.Today.Month;
    }

    partial void OnSelectedYearChanged(int value) => _ = LoadDataAsync();
    partial void OnSelectedMonthNameChanged(string value) => _ = LoadDataAsync();

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            int monthNum = GetMonthNumber(SelectedMonthName);

            // Load inventories
            var inventories = await _dbContext.OilDefInventories
                .Where(i => i.Year == SelectedYear && i.Month == monthNum)
                .ToListAsync();

            var oilInv = inventories.FirstOrDefault(i => string.Equals(i.ProductType, "Oil", StringComparison.OrdinalIgnoreCase));
            OilOpeningStock = oilInv?.OpeningStock ?? 0;
            OilClosingStock = oilInv?.ClosingStock ?? 0;
            OilSalePrice = oilInv?.SalePrice ?? 0;

            var defInv = inventories.FirstOrDefault(i => string.Equals(i.ProductType, "DEF", StringComparison.OrdinalIgnoreCase));
            DefOpeningStock = defInv?.OpeningStock ?? 0;
            DefClosingStock = defInv?.ClosingStock ?? 0;
            DefSalePrice = defInv?.SalePrice ?? 0;

            // Load purchases
            Purchases.Clear();
            var purchaseRecords = await _dbContext.OilDefPurchases
                .Where(p => p.PurchaseDate.Year == SelectedYear && p.PurchaseDate.Month == monthNum)
                .OrderByDescending(p => p.PurchaseDate)
                .ToListAsync();

            foreach (var p in purchaseRecords)
            {
                Purchases.Add(p);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load inventory data");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task SaveStockTakeAsync()
    {
        IsLoading = true;
        try
        {
            int monthNum = GetMonthNumber(SelectedMonthName);

            var inventories = await _dbContext.OilDefInventories
                .Where(i => i.Year == SelectedYear && i.Month == monthNum)
                .ToListAsync();

            // Oil
            var oilInv = inventories.FirstOrDefault(i => string.Equals(i.ProductType, "Oil", StringComparison.OrdinalIgnoreCase));
            if (oilInv != null)
            {
                oilInv.OpeningStock = OilOpeningStock;
                oilInv.ClosingStock = OilClosingStock;
                oilInv.SalePrice = OilSalePrice;
                _dbContext.Entry(oilInv).State = EntityState.Modified;
            }
            else
            {
                _dbContext.OilDefInventories.Add(new OilDefInventory
                {
                    Year = SelectedYear,
                    Month = monthNum,
                    ProductType = "Oil",
                    OpeningStock = OilOpeningStock,
                    ClosingStock = OilClosingStock,
                    SalePrice = OilSalePrice
                });
            }

            // DEF
            var defInv = inventories.FirstOrDefault(i => string.Equals(i.ProductType, "DEF", StringComparison.OrdinalIgnoreCase));
            if (defInv != null)
            {
                defInv.OpeningStock = DefOpeningStock;
                defInv.ClosingStock = DefClosingStock;
                defInv.SalePrice = DefSalePrice;
                _dbContext.Entry(defInv).State = EntityState.Modified;
            }
            else
            {
                _dbContext.OilDefInventories.Add(new OilDefInventory
                {
                    Year = SelectedYear,
                    Month = monthNum,
                    ProductType = "DEF",
                    OpeningStock = DefOpeningStock,
                    ClosingStock = DefClosingStock,
                    SalePrice = DefSalePrice
                });
            }

            await _dbContext.SaveChangesAsync();
            MessageBox.Show("Monthly stock take saved successfully!", "PyroSync — Success", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save monthly stock take");
            MessageBox.Show($"Error saving stock take: {ex.Message}", "PyroSync — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task AddPurchaseAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPurchaseSupplierName) || string.IsNullOrWhiteSpace(NewPurchaseInvoiceNumber))
        {
            MessageBox.Show("Supplier Name and Invoice Number are required.", "PyroSync — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (NewPurchaseQuantity <= 0 || NewPurchaseUnitPrice <= 0)
        {
            MessageBox.Show("Quantity and Unit Price must be greater than zero.", "PyroSync — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        try
        {
            var newPurchase = new OilDefPurchase
            {
                ProductType = NewPurchaseProductType,
                PurchaseDate = NewPurchaseDate,
                SupplierName = NewPurchaseSupplierName,
                InvoiceNumber = NewPurchaseInvoiceNumber,
                Quantity = NewPurchaseQuantity,
                UnitPrice = NewPurchaseUnitPrice,
                TotalCost = Math.Round(NewPurchaseQuantity * NewPurchaseUnitPrice, 2)
            };

            _dbContext.OilDefPurchases.Add(newPurchase);
            await _dbContext.SaveChangesAsync();

            MessageBox.Show("Purchase invoice logged successfully!", "PyroSync — Success", MessageBoxButton.OK, MessageBoxImage.Information);

            // Reset fields
            NewPurchaseSupplierName = string.Empty;
            NewPurchaseInvoiceNumber = string.Empty;
            NewPurchaseQuantity = 0;
            NewPurchaseUnitPrice = 0;

            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to add purchase invoice");
            MessageBox.Show($"Error saving purchase: {ex.Message}", "PyroSync — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task DeletePurchaseAsync(OilDefPurchase purchase)
    {
        if (purchase == null) return;

        var confirm = MessageBox.Show($"Are you sure you want to delete invoice '{purchase.InvoiceNumber}' from supplier '{purchase.SupplierName}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            _dbContext.OilDefPurchases.Remove(purchase);
            await _dbContext.SaveChangesAsync();
            MessageBox.Show("Invoice deleted successfully.", "PyroSync — Success", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to delete purchase invoice");
            MessageBox.Show($"Error deleting invoice: {ex.Message}", "PyroSync — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsLoading = false; }
    }
}
