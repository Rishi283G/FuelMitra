using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace FuelPro.UI.ViewModels;

public partial class TankStockHistoryViewModel : ObservableObject
{
    private readonly ITankDailyStockRepository _tankStockRepo;

    [ObservableProperty] private DateTime _startDate = DateTime.Today.AddDays(-7);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private string _selectedPreset = "Last 7 Days";
    [ObservableProperty] private string _selectedViewMode = "Daily Summary";
    [ObservableProperty] private string _selectedFuelFilter = "All Fuels";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasData;

    [ObservableProperty] private ObservableCollection<TankDailyHistoryItemDto> _dailyRows = new();
    [ObservableProperty] private ObservableCollection<TankShiftHistoryItemDto> _shiftRows = new();
    [ObservableProperty] private ObservableCollection<TankDateComparisonDto> _dateComparisons = new();
    [ObservableProperty] private TankDateComparisonDto? _selectedComparison;

    // Backward-compatible row collection
    [ObservableProperty] private ObservableCollection<TankStockHistoryRow> _historyRows = new();

    public string[] Presets { get; } = { "Today", "Yesterday", "This Week", "This Month", "Last 7 Days", "Last 30 Days", "Custom" };
    public string[] ViewModes { get; } = { "Daily Summary", "Shift-Wise Detail" };
    public string[] FuelFilters { get; } = { "All Fuels", "HSD", "MS-I", "MS-II", "CNG" };

    public bool IsDailySummaryMode => SelectedViewMode == "Daily Summary";
    public bool IsShiftWiseMode => SelectedViewMode == "Shift-Wise Detail";

    public TankStockHistoryViewModel()
    {
        _tankStockRepo = App.Services.GetRequiredService<ITankDailyStockRepository>();
        _ = LoadHistoryAsync();
    }

    private bool _isUpdatingPreset;

    partial void OnSelectedViewModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsDailySummaryMode));
        OnPropertyChanged(nameof(IsShiftWiseMode));
    }

    partial void OnSelectedFuelFilterChanged(string value)
    {
        _ = LoadHistoryAsync();
    }

    partial void OnSelectedPresetChanged(string value)
    {
        if (value == "Custom") return;

        _isUpdatingPreset = true;
        var today = DateTime.Today;
        DateTime newStart = today;
        DateTime newEnd = today;
        switch (value)
        {
            case "Today":
                newStart = today;
                newEnd = today;
                break;
            case "Yesterday":
                newStart = today.AddDays(-1);
                newEnd = today.AddDays(-1);
                break;
            case "This Week":
                int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                newStart = today.AddDays(-1 * diff).Date;
                newEnd = today;
                break;
            case "This Month":
                newStart = new DateTime(today.Year, today.Month, 1);
                newEnd = today;
                break;
            case "Last 7 Days":
                newStart = today.AddDays(-6);
                newEnd = today;
                break;
            case "Last 30 Days":
                newStart = today.AddDays(-29);
                newEnd = today;
                break;
        }

        StartDate = newStart;
        EndDate = newEnd;
        _isUpdatingPreset = false;

        _ = LoadHistoryAsync();
    }

    partial void OnStartDateChanged(DateTime value)
    {
        if (!_isUpdatingPreset)
        {
            SelectedPreset = "Custom";
            _ = LoadHistoryAsync();
        }
    }

    partial void OnEndDateChanged(DateTime value)
    {
        if (!_isUpdatingPreset)
        {
            SelectedPreset = "Custom";
            _ = LoadHistoryAsync();
        }
    }

    [RelayCommand]
    public async Task LoadHistoryAsync()
    {
        IsLoading = true;
        StatusMessage = "";
        try
        {
            string? fuelFilterArg = SelectedFuelFilter == "All Fuels" ? null : SelectedFuelFilter;
            var detailedResult = await _tankStockRepo.GetDetailedHistoryAsync(StartDate, EndDate, fuelFilterArg);

            if (!detailedResult.Success || detailedResult.Data == null)
            {
                DailyRows.Clear();
                ShiftRows.Clear();
                DateComparisons.Clear();
                HistoryRows.Clear();
                HasData = false;
                StatusMessage = detailedResult.Error ?? "Failed to load tank stock history.";
                return;
            }

            var data = detailedResult.Data;
            var orderedDaily = data.DailyRows.OrderByDescending(r => r.Date).ThenBy(r => r.FuelType).ToList();
            var orderedShift = data.ShiftRows
                .OrderByDescending(r => r.OperationalDate)
                .ThenBy(r => r.ShiftType switch { "B" => 1, "C" => 2, "A" => 3, _ => 4 })
                .ThenBy(r => r.FuelType)
                .ToList();
            var orderedComparisons = data.DateComparisons.OrderByDescending(c => c.Date).ToList();

            DailyRows = new ObservableCollection<TankDailyHistoryItemDto>(orderedDaily);
            ShiftRows = new ObservableCollection<TankShiftHistoryItemDto>(orderedShift);
            DateComparisons = new ObservableCollection<TankDateComparisonDto>(orderedComparisons);
            SelectedComparison = orderedComparisons.FirstOrDefault();

            // Populate legacy HistoryRows for backwards compatibility
            var legacyList = new List<TankStockHistoryRow>();
            var groupedByDate = orderedDaily.GroupBy(r => r.Date.Date);
            foreach (var g in groupedByDate)
            {
                var row = new TankStockHistoryRow { Date = g.Key };
                var hsd = g.FirstOrDefault(x => x.FuelType == "HSD");
                if (hsd != null)
                {
                    row.HsdOpening = hsd.DayOpeningStock;
                    row.HsdSale = hsd.DaySales;
                    row.HsdTesting = hsd.DayTesting;
                    row.HsdPurchased = hsd.DayReceipts;
                    row.HsdClosing = hsd.DayClosingStock;
                    row.HsdManualStock = hsd.DayMeasuredStock ?? hsd.DayClosingStock;
                }
                var ms1 = g.FirstOrDefault(x => x.FuelType == "MS-I");
                if (ms1 != null)
                {
                    row.MsIOpening = ms1.DayOpeningStock;
                    row.MsISale = ms1.DaySales;
                    row.MsITesting = ms1.DayTesting;
                    row.MsIPurchased = ms1.DayReceipts;
                    row.MsIClosing = ms1.DayClosingStock;
                    row.MsIManualStock = ms1.DayMeasuredStock ?? ms1.DayClosingStock;
                }
                var ms2 = g.FirstOrDefault(x => x.FuelType == "MS-II");
                if (ms2 != null)
                {
                    row.MsIIOpening = ms2.DayOpeningStock;
                    row.MsIISale = ms2.DaySales;
                    row.MsIITesting = ms2.DayTesting;
                    row.MsIIPurchased = ms2.DayReceipts;
                    row.MsIIClosing = ms2.DayClosingStock;
                    row.MsIIManualStock = ms2.DayMeasuredStock ?? ms2.DayClosingStock;
                }
                var cng = g.FirstOrDefault(x => x.FuelType == "CNG");
                if (cng != null)
                {
                    row.CngOpening = cng.DayOpeningStock;
                    row.CngSale = cng.DaySales;
                    row.CngTesting = cng.DayTesting;
                    row.CngPurchased = cng.DayReceipts;
                    row.CngClosing = cng.DayClosingStock;
                    row.CngManualStock = cng.DayMeasuredStock ?? cng.DayClosingStock;
                }
                legacyList.Add(row);
            }
            HistoryRows = new ObservableCollection<TankStockHistoryRow>(legacyList);

            HasData = DailyRows.Count > 0 || ShiftRows.Count > 0;
            if (!HasData)
            {
                StatusMessage = "No tank stock records found in the selected range.";
            }
        }
        catch (Exception ex)
        {
            HasData = false;
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

public class TankStockHistoryRow
{
    public DateTime Date { get; set; }

    // HSD
    public double HsdOpening { get; set; }
    public double HsdSale { get; set; }
    public double HsdTesting { get; set; }
    public double HsdPurchased { get; set; }
    public double HsdClosing { get; set; }
    public double HsdDip { get; set; }
    public double HsdManualStock { get; set; }

    // MS-I
    public double MsIOpening { get; set; }
    public double MsISale { get; set; }
    public double MsITesting { get; set; }
    public double MsIPurchased { get; set; }
    public double MsIClosing { get; set; }
    public double MsIDip { get; set; }
    public double MsIManualStock { get; set; }

    // MS-II
    public double MsIIOpening { get; set; }
    public double MsIISale { get; set; }
    public double MsIITesting { get; set; }
    public double MsIIPurchased { get; set; }
    public double MsIIClosing { get; set; }
    public double MsIIDip { get; set; }
    public double MsIIManualStock { get; set; }

    // CNG
    public double CngOpening { get; set; }
    public double CngSale { get; set; }
    public double CngTesting { get; set; }
    public double CngPurchased { get; set; }
    public double CngClosing { get; set; }
    public double CngDip { get; set; }
    public double CngManualStock { get; set; }
}
