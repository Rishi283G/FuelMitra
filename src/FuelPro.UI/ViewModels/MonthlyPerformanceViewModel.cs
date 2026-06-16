using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace FuelPro.UI.ViewModels;

/// <summary>
/// Monthly performance overview: day-by-day totals for the selected month.
/// </summary>
public partial class MonthlyPerformanceViewModel : ObservableObject
{
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IShiftRepository _shiftRepo;
    private readonly IDsmCalculationService _calcService;
    private readonly IOwnerCalculationService _ownerCalcService;

    [ObservableProperty] private DateTime _selectedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private bool _isLoading;

    // Month totals
    [ObservableProperty] private double _monthTotalSale;
    [ObservableProperty] private double _monthTotalLitres;
    [ObservableProperty] private double _monthTotalCollection;
    [ObservableProperty] private double _monthAvgDailySale;
    [ObservableProperty] private int _monthTotalEntries;

    public ObservableCollection<MonthDayRow> DayRows { get; } = new();

    public MonthlyPerformanceViewModel()
    {
        _dsmEntryRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _calcService = App.Services.GetRequiredService<IDsmCalculationService>();
        _ownerCalcService = App.Services.GetRequiredService<IOwnerCalculationService>();
        _ = LoadAsync();
    }

    partial void OnSelectedMonthChanged(DateTime value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var year = SelectedMonth.Year;
            var month = SelectedMonth.Month;
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var entriesResult = await _dsmEntryRepo.GetEntriesForMonthAsync(year, month);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            DayRows.Clear();
            MonthTotalSale = 0;
            MonthTotalLitres = 0;
            MonthTotalCollection = 0;
            MonthTotalEntries = entries.Count;

            var byDay = entries.GroupBy(e =>
            {
                var shift = e.Shift;
                return shift != null ? shift.ShiftDate.Date : DateTime.Today;
            });

            foreach (var dayGroup in byDay.OrderBy(g => g.Key))
            {
                var dayResult = _ownerCalcService.Calculate(dayGroup, Array.Empty<Expense>(), Array.Empty<ShiftOtherCash>());

                DayRows.Add(new MonthDayRow
                {
                    Date = dayGroup.Key,
                    DsmEntryCount = dayGroup.Count(),
                    TotalSale = dayResult.GrossSales,
                    TotalLitres = dayResult.TotalLitres,
                    TotalCollection = dayResult.AdjustedCollection,
                    Mismatch = dayResult.Mismatch
                });

                MonthTotalSale += dayResult.GrossSales;
                MonthTotalLitres += dayResult.TotalLitres;
                MonthTotalCollection += dayResult.AdjustedCollection;
            }

            MonthAvgDailySale = DayRows.Count > 0 ? MonthTotalSale / DayRows.Count : 0;
        }
        finally { IsLoading = false; }
    }
}

public class MonthDayRow
{
    public DateTime Date { get; set; }
    public string DateDisplay => Date.ToString("dd MMM");
    public string DayName => Date.ToString("ddd");
    public int DsmEntryCount { get; set; }
    public double TotalSale { get; set; }
    public double TotalLitres { get; set; }
    public double TotalCollection { get; set; }
    public double Mismatch { get; set; }
}
