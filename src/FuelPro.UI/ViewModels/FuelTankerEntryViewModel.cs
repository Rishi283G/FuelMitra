using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Core.DTOs;
using FuelPro.UI.Printing;
using FuelPro.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FuelPro.UI.ViewModels;

public partial class FuelTankerEntryViewModel : ObservableObject
{
    private readonly FuelProDbContext _context;
    private readonly ExcelExportService _excelExportService;

    [ObservableProperty] private ObservableCollection<FuelTanker> _tankers = new();
    [ObservableProperty] private FuelTanker? _selectedTanker;

    // Form properties
    [ObservableProperty] private DateTime _tankerDate = DateTime.Today;
    [ObservableProperty] private string _tankerNumber = string.Empty;
    [ObservableProperty] private string _invoiceNumber = string.Empty;
    [ObservableProperty] private string _fuelType = "HSD";
    [ObservableProperty] private double? _quantity;
    [ObservableProperty] private double? _purchaseRate;
    [ObservableProperty] private double? _totalAmount;
    [ObservableProperty] private double? _density;
    [ObservableProperty] private string _remarks = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    private bool _isEditMode;

    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private DateTime _startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private string _selectedPreset = "Monthly";

    public string FormTitle => IsEditMode ? "Edit Fuel Tanker Entry" : "New Fuel Tanker Entry";

    // Adapt to Kandhare's actual tank configuration — update as needed
    public List<string> FuelTypes { get; } = new()
    {
        "HSD",
        "MS-I",
        "MS-II",
        "CNG"
    };

    public FuelTankerEntryViewModel()
    {
        _context = App.Services.GetRequiredService<FuelProDbContext>();
        _excelExportService = App.Services.GetRequiredService<ExcelExportService>();
        _ = LoadTankersAsync();
    }

    [RelayCommand]
    private async Task LoadTankersAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _context.FuelTankers
                .Where(t => t.TankerDate.Date >= StartDate.Date && t.TankerDate.Date <= EndDate.Date)
                .OrderByDescending(t => t.TankerDate)
                .ToListAsync();

            Tankers = new ObservableCollection<FuelTanker>(list);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load fuel tankers");
            MessageBox.Show($"Failed to load fuel tankers: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnStartDateChanged(DateTime value) => _ = LoadTankersAsync();
    partial void OnEndDateChanged(DateTime value) => _ = LoadTankersAsync();

    partial void OnQuantityChanged(double? value) => RecalculateTotal();
    partial void OnPurchaseRateChanged(double? value) => RecalculateTotal();

    private void RecalculateTotal()
    {
        TotalAmount = (Quantity ?? 0) * (PurchaseRate ?? 0);
    }

    [RelayCommand]
    private void SetPreset(string preset)
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
                var lastMonth = DateTime.Today.AddMonths(-1);
                StartDate = new DateTime(lastMonth.Year, lastMonth.Month, 1);
                EndDate = new DateTime(lastMonth.Year, lastMonth.Month, DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month));
                break;
        }
    }

    [RelayCommand]
    private void EditTanker(FuelTanker tanker)
    {
        SelectedTanker = tanker;
    }

    [RelayCommand]
    private void Print()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new GenericGridPrintCard { Label = "Total Litres/Kg", Value = Tankers.Sum(t => t.Quantity).ToString("N2"), Highlight = true },
                new GenericGridPrintCard { Label = "Total Amount", Value = "₹" + Tankers.Sum(t => t.TotalAmount).ToString("N2") },
                new GenericGridPrintCard { Label = "Total Entries", Value = Tankers.Count.ToString() }
            };

            var headers = new List<string> { "Date", "Invoice No", "Fuel Type", "Quantity", "Rate (₹)", "Total Amount (₹)", "Density", "Vehicle No", "Remarks" };
            var rows = new List<List<string>>();

            foreach (var t in Tankers)
            {
                rows.Add(new List<string>
                {
                    t.TankerDate.ToString("dd-MMM-yyyy"),
                    t.InvoiceNumber,
                    t.FuelType,
                    t.Quantity.ToString("N2"),
                    t.PurchaseRate.ToString("N2"),
                    "₹" + t.TotalAmount.ToString("N2"),
                    t.Density.ToString("N4"),
                    t.TankerNumber ?? "",
                    t.Remarks ?? ""
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Fuel Tanker Purchases Report",
                Subtitle = $"Period: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var printService = App.Services.GetRequiredService<PrintService>();
            printService.PrintGenericGrid(printData);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print fuel tanker report");
            MessageBox.Show($"Failed to print: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        try
        {
            IsLoading = true;
            var summaryCards = new List<GenericGridPrintCard>
            {
                new GenericGridPrintCard { Label = "Total Litres/Kg", Value = Tankers.Sum(t => t.Quantity).ToString("N2"), Highlight = true },
                new GenericGridPrintCard { Label = "Total Amount", Value = "₹" + Tankers.Sum(t => t.TotalAmount).ToString("N2") },
                new GenericGridPrintCard { Label = "Total Entries", Value = Tankers.Count.ToString() }
            };

            var headers = new List<string> { "Date", "Invoice No", "Fuel Type", "Quantity", "Rate (₹)", "Total Amount (₹)", "Density", "Vehicle No", "Remarks" };
            var rows = new List<List<string>>();

            foreach (var t in Tankers)
            {
                rows.Add(new List<string>
                {
                    t.TankerDate.ToString("dd-MMM-yyyy"),
                    t.InvoiceNumber,
                    t.FuelType,
                    t.Quantity.ToString("N2"),
                    t.PurchaseRate.ToString("N2"),
                    "₹" + t.TotalAmount.ToString("N2"),
                    t.Density.ToString("N4"),
                    t.TankerNumber ?? "",
                    t.Remarks ?? ""
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Fuel Tanker Purchases Report",
                Subtitle = $"Period: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows
            };

            var path = await _excelExportService.ExportGenericGridAsync(printData, "FuelTankers");
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Excel", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to export fuel tanker report to Excel");
            MessageBox.Show($"Failed to export: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedTankerChanged(FuelTanker? value)
    {
        if (value != null)
        {
            TankerDate = value.TankerDate;
            TankerNumber = value.TankerNumber ?? string.Empty;
            InvoiceNumber = value.InvoiceNumber;

            // Match display label if present, else use raw DB value
            var matching = FuelTypes.FirstOrDefault(f => f == value.FuelType);
            FuelType = matching ?? value.FuelType;

            Quantity = value.Quantity;
            PurchaseRate = value.PurchaseRate;
            TotalAmount = value.TotalAmount;
            Density = value.Density;
            Remarks = value.Remarks ?? string.Empty;
            IsEditMode = true;
        }
        else
        {
            ClearForm();
        }
    }

    [RelayCommand]
    private void ClearForm()
    {
        SelectedTanker = null;
        TankerDate = DateTime.Today;
        TankerNumber = string.Empty;
        InvoiceNumber = string.Empty;
        FuelType = FuelTypes.FirstOrDefault() ?? "HSD";
        Quantity = null;
        PurchaseRate = null;
        TotalAmount = null;
        Density = null;
        Remarks = string.Empty;
        IsEditMode = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(InvoiceNumber))
        {
            MessageBox.Show("Invoice number is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if ((Quantity ?? 0) <= 0)
        {
            MessageBox.Show("Quantity must be greater than zero.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if ((PurchaseRate ?? 0) <= 0)
        {
            MessageBox.Show("Purchase rate must be greater than zero.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            DateTime oldDate = DateTime.Today;
            bool wasEdit = false;

            FuelTanker tanker;
            if (IsEditMode && SelectedTanker != null)
            {
                oldDate = SelectedTanker.TankerDate;
                wasEdit = true;
                tanker = await _context.FuelTankers.FindAsync(SelectedTanker.FuelTankerId) ?? new FuelTanker();

                tanker.TankerDate = TankerDate;
                tanker.TankerNumber = TankerNumber;
                tanker.InvoiceNumber = InvoiceNumber;
                tanker.FuelType = FuelType;
                tanker.Quantity = Quantity ?? 0;
                tanker.PurchaseRate = PurchaseRate ?? 0;
                tanker.TotalAmount = TotalAmount ?? 0;
                tanker.Density = Density ?? 0;
                tanker.Remarks = Remarks;
                tanker.UpdatedAt = DateTime.Now;

                _context.FuelTankers.Update(tanker);

                _context.SyncChangeLogs.Add(new SyncChangeLog
                {
                    TableName = "FuelTankers",
                    RecordId = tanker.FuelTankerId,
                    Operation = "UPDATE",
                    IsSynced = false,
                    CreatedAt = DateTime.Now
                });
            }
            else
            {
                tanker = new FuelTanker
                {
                    TankerDate = TankerDate,
                    TankerNumber = TankerNumber,
                    InvoiceNumber = InvoiceNumber,
                    FuelType = FuelType,
                    Quantity = Quantity ?? 0,
                    PurchaseRate = PurchaseRate ?? 0,
                    TotalAmount = TotalAmount ?? 0,
                    Density = Density ?? 0,
                    Remarks = Remarks,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

                _context.FuelTankers.Add(tanker);
                await _context.SaveChangesAsync();

                _context.SyncChangeLogs.Add(new SyncChangeLog
                {
                    TableName = "FuelTankers",
                    RecordId = tanker.FuelTankerId,
                    Operation = "INSERT",
                    IsSynced = false,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            DateTime propagationStartDate = TankerDate;
            if (wasEdit && oldDate < TankerDate)
            {
                propagationStartDate = oldDate;
            }

            var inventoryService = App.Services.GetRequiredService<IAgsInventoryService>();
            await inventoryService.PropagateInventoryCalculationsAsync(propagationStartDate, "B");

            FuelPro.Core.Services.DsmEntryService.RaiseInventoryChanged();
            FuelPro.Core.Services.DsmEntryService.RaiseDsmEntryChanged();

            ClearForm();
            await LoadTankersAsync();
            MessageBox.Show("Fuel tanker entry saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save fuel tanker");
            MessageBox.Show($"Failed to save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedTanker == null) return;

        var confirm = MessageBox.Show(
            "Are you sure you want to delete this fuel tanker entry?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var tanker = await _context.FuelTankers.FindAsync(SelectedTanker.FuelTankerId);
            if (tanker != null)
            {
                var propagationStartDate = tanker.TankerDate;
                _context.FuelTankers.Remove(tanker);

                _context.SyncChangeLogs.Add(new SyncChangeLog
                {
                    TableName = "FuelTankers",
                    RecordId = tanker.FuelTankerId,
                    Operation = "DELETE",
                    IsSynced = false,
                    CreatedAt = DateTime.Now
                });

                await _context.SaveChangesAsync();

                var inventoryService = App.Services.GetRequiredService<IAgsInventoryService>();
                await inventoryService.PropagateInventoryCalculationsAsync(propagationStartDate, "B");

                FuelPro.Core.Services.DsmEntryService.RaiseInventoryChanged();
                FuelPro.Core.Services.DsmEntryService.RaiseDsmEntryChanged();

                ClearForm();
                await LoadTankersAsync();
                MessageBox.Show("Fuel tanker entry deleted successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to delete fuel tanker");
            MessageBox.Show($"Failed to delete: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
