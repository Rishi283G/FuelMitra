using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace FuelPro.UI.ViewModels;

/// <summary>
/// DSM-wise monthly performance: mismatch frequency, short amounts per DSM.
/// </summary>
public partial class DsmPerformanceViewModel : ObservableObject
{
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IDsmCalculationService _calcService;
    private readonly IOwnerCalculationService _ownerCalcService;

    [ObservableProperty] private DateTime _selectedMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<DsmMonthlyRow> DsmRows { get; } = new();

    [ObservableProperty] private double _totalSale;
    [ObservableProperty] private double _totalLitres;
    [ObservableProperty] private double _totalMismatch;
    [ObservableProperty] private int _totalShifts;

    public DsmPerformanceViewModel()
    {
        _dsmEntryRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
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

            var entriesResult = await _dsmEntryRepo.GetEntriesForMonthAsync(year, month);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            DsmRows.Clear();
            TotalSale = 0; TotalLitres = 0; TotalMismatch = 0; TotalShifts = 0;

            var byDsm = entries.GroupBy(e => e.DsmName ?? "Unknown");
            foreach (var dsmGroup in byDsm.OrderBy(g => g.Key))
            {
                int shortCount = 0;
                double totalShort = 0;
                double highestShort = 0;
                int mismatchCount = 0;

                foreach (var entry in dsmGroup)
                {
                    var entryResult = _ownerCalcService.Calculate(new[] { entry }, null, null);
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

                var dsmResult = _ownerCalcService.Calculate(dsmGroup, null, null);

                DsmRows.Add(new DsmMonthlyRow
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
                    MismatchCount = mismatchCount
                });

                TotalSale += dsmResult.GrossSales;
                TotalLitres += dsmResult.TotalLitres;
                TotalMismatch += dsmResult.Mismatch;
                TotalShifts += dsmGroup.Count();
            }
        }
        finally { IsLoading = false; }
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
}
