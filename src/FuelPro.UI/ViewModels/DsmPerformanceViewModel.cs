using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.UI.Printing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FuelPro.UI.ViewModels;

/// <summary>
/// DSM-wise performance with date range presets, filter options, and search capabilities.
/// </summary>
public partial class DsmPerformanceViewModel : ObservableObject
{
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IDsmCalculationService _calcService;
    private readonly IOwnerCalculationService _ownerCalcService;
    private readonly PrintService _printService;
    private readonly ExcelExportService _excelExportService;

    [ObservableProperty] private DateTime _startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private string _selectedPreset = "Monthly";
    [ObservableProperty] private bool _isLoading;

    // Search and filter properties
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedDsmFilter = "All DSMs";
    public ObservableCollection<string> DsmFilterOptions { get; } = new() { "All DSMs" };

    public ObservableCollection<DsmMonthlyRow> DsmRows { get; } = new();
    private readonly List<DsmMonthlyRow> _allDsmRows = new();

    [ObservableProperty] private double _totalSale;
    [ObservableProperty] private double _totalLitres;
    [ObservableProperty] private double _totalMismatch;
    [ObservableProperty] private int _totalShifts;

    public bool IsCustomRange => SelectedPreset == "Custom";

    public DsmPerformanceViewModel()
    {
        _dsmEntryRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _calcService = App.Services.GetRequiredService<IDsmCalculationService>();
        _ownerCalcService = App.Services.GetRequiredService<IOwnerCalculationService>();
        _printService = App.Services.GetRequiredService<PrintService>();
        _excelExportService = App.Services.GetRequiredService<ExcelExportService>();
        _ = LoadAsync();
    }

    partial void OnSelectedPresetChanged(string value) => OnPropertyChanged(nameof(IsCustomRange));

    partial void OnStartDateChanged(DateTime value)
    {
        if (SelectedPreset == "Custom") _ = LoadAsync();
    }

    partial void OnEndDateChanged(DateTime value)
    {
        if (SelectedPreset == "Custom") _ = LoadAsync();
    }

    partial void OnSearchTextChanged(string value) => FilterAndCalculate();
    partial void OnSelectedDsmFilterChanged(string value) => FilterAndCalculate();

    [RelayCommand]
    private void SetPreset(string preset)
    {
        SelectedPreset = preset;
        switch (preset)
        {
            case "Daily":
                StartDate = DateTime.Today;
                EndDate = DateTime.Today;
                break;
            case "Weekly":
                StartDate = DateTime.Today.AddDays(-6);
                EndDate = DateTime.Today;
                break;
            case "Monthly":
                StartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                EndDate = DateTime.Today;
                break;
            case "Custom":
                // Don't change dates, let the user change
                break;
        }
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var entriesResult = await _dsmEntryRepo.GetEntriesForDateRangeAsync(StartDate, EndDate);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            _allDsmRows.Clear();

            var byDsm = entries.GroupBy(e => e.DsmName ?? "Unknown");
            foreach (var dsmGroup in byDsm.OrderBy(g => g.Key))
            {
                int shortCount = 0;
                double totalShort = 0;
                double highestShort = 0;
                int mismatchCount = 0;

                foreach (var entry in dsmGroup)
                {
                    var entryResult = _ownerCalcService.Calculate(new[] { entry }, Array.Empty<Expense>(), Array.Empty<ShiftOtherCash>());
                    var mismatch = entryResult.Mismatch;

                    if (Math.Abs(mismatch) > 0.01)
                    {
                        mismatchCount++;
                    }

                    if (mismatch < -0.01)
                    {
                        shortCount++;
                        var shortAmt = Math.Abs(mismatch);
                        totalShort += shortAmt;
                        if (shortAmt > highestShort)
                        {
                            highestShort = shortAmt;
                        }
                    }
                }

                var dsmResult = _ownerCalcService.Calculate(dsmGroup, Array.Empty<Expense>(), Array.Empty<ShiftOtherCash>());

                _allDsmRows.Add(new DsmMonthlyRow
                {
                    DsmName = dsmGroup.Key,
                    ShiftCount = dsmGroup.Count(),
                    TotalSale = dsmResult.GrossSales,
                    TotalLitres = dsmResult.TotalLitres,
                    TotalCollection = dsmResult.AdjustedCollection,
                    Mismatch = dsmResult.Mismatch,
                    ShortCount = shortCount,
                    TotalShortAmount = totalShort,
                    HighestShort = highestShort,
                    MismatchCount = mismatchCount,
                    CashDeposit = dsmResult.CashDeposit,
                    CashInHand = dsmResult.CashInHand,
                    PhonePe = dsmResult.PhonePeDirect,
                    PhonePeCard = dsmResult.PhonePeCard,
                    CreditCard = dsmResult.CreditCard,
                    PetroCard = dsmResult.PetroCard,
                    Debit = dsmResult.Debit
                });
            }

            // Update DSM Filter Options
            var currentSelected = SelectedDsmFilter;
            
            DsmFilterOptions.Clear();
            DsmFilterOptions.Add("All DSMs");
            
            var distinctNames = entries
                .Select(e => e.DsmName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct()
                .OrderBy(name => name);

            foreach (var name in distinctNames)
            {
                DsmFilterOptions.Add(name);
            }

            if (DsmFilterOptions.Contains(currentSelected))
            {
                SelectedDsmFilter = currentSelected;
            }
            else
            {
                SelectedDsmFilter = "All DSMs";
            }

            FilterAndCalculate();
        }
        finally { IsLoading = false; }
    }

    private void FilterAndCalculate()
    {
        DsmRows.Clear();
        TotalSale = 0;
        TotalLitres = 0;
        TotalMismatch = 0;
        TotalShifts = 0;

        var filtered = _allDsmRows.AsEnumerable();

        if (!string.IsNullOrEmpty(SelectedDsmFilter) && SelectedDsmFilter != "All DSMs")
        {
            filtered = filtered.Where(r => string.Equals(r.DsmName, SelectedDsmFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(SearchText))
        {
            filtered = filtered.Where(r => r.DsmName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var row in filtered)
        {
            DsmRows.Add(row);
            TotalSale += row.TotalSale;
            TotalLitres += row.TotalLitres;
            TotalMismatch += row.Mismatch;
            TotalShifts += row.ShiftCount;
        }
    }

    [RelayCommand]
    private void Print()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Sales", Value = "₹" + TotalSale.ToString("N2") },
                new() { Label = "Total Volume", Value = TotalLitres.ToString("N2") + " L" },
                new() { Label = "Total Mismatch", Value = "₹" + TotalMismatch.ToString("N2"), Highlight = Math.Abs(TotalMismatch) > 1 },
                new() { Label = "Total Shifts", Value = TotalShifts.ToString() }
            };

            var headers = new List<string> { "DSM Name", "Shifts Worked", "Total Sales", "Total Collection", "Total Short", "Avg Short", "Highest Short", "Accuracy", "Mismatch Count" };
            var rows = new List<List<string>>();

            foreach (var r in DsmRows)
            {
                rows.Add(new List<string>
                {
                    r.DsmName,
                    r.ShiftCount.ToString(),
                    "₹" + r.TotalSale.ToString("N2"),
                    "₹" + r.TotalCollection.ToString("N2"),
                    "₹" + r.TotalShortAmount.ToString("N2"),
                    "₹" + r.AverageShort.ToString("N2"),
                    "₹" + r.HighestShort.ToString("N2"),
                    r.CollectionAccuracy.ToString("F2") + "%",
                    r.MismatchCount.ToString()
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "DSM Performance Report",
                Subtitle = $"Period: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}" + 
                           (string.IsNullOrWhiteSpace(SearchText) ? "" : $" (Filtered by: '{SearchText}')"),
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            _printService.PrintGenericGrid(printData);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print DSM performance");
            MessageBox.Show($"Failed to print report: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Sales", Value = "₹" + TotalSale.ToString("N2"), Highlight = true },
                new() { Label = "Total Volume", Value = TotalLitres.ToString("N2") + " L", Highlight = false },
                new() { Label = "Total Collection", Value = "₹" + TotalMismatch.ToString("N2"), Highlight = false },
                new() { Label = "Total Shifts", Value = TotalShifts.ToString(), Highlight = false }
            };

            var headers = new List<string> { "DSM Name", "Shifts Worked", "Total Sales", "Total Collection", "Total Short", "Avg Short", "Highest Short", "Accuracy", "Mismatch Count" };
            var rows = new List<List<string>>();

            foreach (var r in DsmRows)
            {
                rows.Add(new List<string>
                {
                    r.DsmName,
                    r.ShiftCount.ToString(),
                    "₹" + r.TotalSale.ToString("N2"),
                    "₹" + r.TotalCollection.ToString("N2"),
                    "₹" + r.TotalShortAmount.ToString("N2"),
                    "₹" + r.AverageShort.ToString("N2"),
                    "₹" + r.HighestShort.ToString("N2"),
                    r.CollectionAccuracy.ToString("F2") + "%",
                    r.MismatchCount.ToString()
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "DSM Performance Report",
                Subtitle = $"Period: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}" + 
                           (string.IsNullOrWhiteSpace(SearchText) ? "" : $" (Filtered by: '{SearchText}')"),
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var path = await _excelExportService.ExportGenericGridAsync(printData, "DsmPerformance");
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to export DSM performance to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public class DsmMonthlyRow
{
    public string DsmName { get; set; } = "";
    public int ShiftCount { get; set; }
    public double TotalSale { get; set; }
    public double TotalLitres { get; set; }
    public double TotalCollection { get; set; }
    public double Mismatch { get; set; }
    public int ShortCount { get; set; }
    public double TotalShortAmount { get; set; }
    public double AverageShort => ShiftCount > 0 ? TotalShortAmount / ShiftCount : 0;
    public double HighestShort { get; set; }
    public double CollectionAccuracy => TotalSale > 0 ? (TotalCollection / TotalSale) * 100 : 0;
    public int MismatchCount { get; set; }

    // Detailed collections split
    public double CashDeposit { get; set; }
    public double CashInHand { get; set; }
    public double PhonePe { get; set; }
    public double PhonePeCard { get; set; }
    public double CreditCard { get; set; }
    public double PetroCard { get; set; }
    public double Debit { get; set; }
    public double TotalCash => CashDeposit + CashInHand;
}
