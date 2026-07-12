using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private ObservableCollection<TankStockHistoryRow> _historyRows = new();

    public string[] Presets { get; } = { "Today", "Yesterday", "This Week", "This Month", "Last 7 Days", "Last 30 Days", "Custom" };

    public TankStockHistoryViewModel()
    {
        _tankStockRepo = App.Services.GetRequiredService<ITankDailyStockRepository>();
        _ = LoadHistoryAsync();
    }

    private bool _isUpdatingPreset;

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
            var result = await _tankStockRepo.GetByDateRangeAsync(StartDate, EndDate);
            if (!result.Success || result.Data == null || result.Data.Count == 0)
            {
                HistoryRows.Clear();
                HasData = false;
                StatusMessage = "No tank stock records found in the selected range.";
                return;
            }

            var grouped = result.Data.GroupBy(s => s.Date.Date).OrderByDescending(g => g.Key);
            var list = new List<TankStockHistoryRow>();

            foreach (var g in grouped)
            {
                var row = new TankStockHistoryRow { Date = g.Key };

                var hsd = g.FirstOrDefault(s => s.FuelType == "HSD" || s.FuelType == "Tank 1");
                if (hsd != null)
                {
                    row.HsdOpening = hsd.OpeningStock;
                    row.HsdSale = hsd.DaySaleLitres;
                    row.HsdTesting = hsd.TestingLitres;
                    row.HsdPurchased = hsd.PurchasedLitres;
                    row.HsdClosing = hsd.ClosingStock;
                    row.HsdDip = hsd.DipMm;
                    row.HsdManualStock = hsd.ManualStock;
                }

                var ms1 = g.FirstOrDefault(s => s.FuelType == "MS-I" || s.FuelType == "Tank 2");
                if (ms1 != null)
                {
                    row.MsIOpening = ms1.OpeningStock;
                    row.MsISale = ms1.DaySaleLitres;
                    row.MsITesting = ms1.TestingLitres;
                    row.MsIPurchased = ms1.PurchasedLitres;
                    row.MsIClosing = ms1.ClosingStock;
                    row.MsIDip = ms1.DipMm;
                    row.MsIManualStock = ms1.ManualStock;
                }

                var ms2 = g.FirstOrDefault(s => s.FuelType == "MS-II" || s.FuelType == "Tank 3");
                if (ms2 != null)
                {
                    row.MsIIOpening = ms2.OpeningStock;
                    row.MsIISale = ms2.DaySaleLitres;
                    row.MsIITesting = ms2.TestingLitres;
                    row.MsIIPurchased = ms2.PurchasedLitres;
                    row.MsIIClosing = ms2.ClosingStock;
                    row.MsIIDip = ms2.DipMm;
                    row.MsIIManualStock = ms2.ManualStock;
                }

                var cng = g.FirstOrDefault(s => s.FuelType == "CNG" || s.FuelType == "Tank 4");
                if (cng != null)
                {
                    row.CngOpening = cng.OpeningStock;
                    row.CngSale = cng.DaySaleLitres;
                    row.CngTesting = cng.TestingLitres;
                    row.CngPurchased = cng.PurchasedLitres;
                    row.CngClosing = cng.ClosingStock;
                    row.CngDip = cng.DipMm;
                    row.CngManualStock = cng.ManualStock;
                }

                list.Add(row);
            }

            HistoryRows = new ObservableCollection<TankStockHistoryRow>(list);
            HasData = list.Count > 0;
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
