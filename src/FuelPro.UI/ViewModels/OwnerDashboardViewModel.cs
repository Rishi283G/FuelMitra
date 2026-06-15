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
/// Owner Dashboard ViewModel — read-only KPI overview with prominent sync status.
/// </summary>
public partial class OwnerDashboardViewModel : ObservableObject
{
    private readonly ShiftCalculationService _calcService;
    private readonly IShiftRepository _shiftRepository;
    private readonly IDsmEntryRepository _dsmEntryRepository;
    private readonly IDsmCalculationService _dsmCalculationService;
    private readonly IExpenseRepository _expenseRepo;
    private readonly IOwnerCalculationService _ownerCalcService;
    private readonly FuelPro.Sync.SyncEngine _syncEngine;

    // KPI Properties
    [ObservableProperty] private double _todayTotalSale;
    [ObservableProperty] private double _todayTotalLitres;
    [ObservableProperty] private double _todayTotalCollection;
    [ObservableProperty] private double _todayTotalExpenses;
    [ObservableProperty] private double _todayTotalMismatch;
    [ObservableProperty] private int _totalDsmEntries;

    // Fuel-wise breakdown
    [ObservableProperty] private double _todayHsdLitres;
    [ObservableProperty] private double _todayMsILitres;
    [ObservableProperty] private double _todayMsIILitres;

    // Payment breakdown
    [ObservableProperty] private double _todayTotalCash;
    [ObservableProperty] private double _todayTotalPhonePe;
    [ObservableProperty] private double _todayTotalCreditCard;
    [ObservableProperty] private double _todayTotalPetroCard;
    [ObservableProperty] private double _todayTotalDebit;

    // Shift summaries
    [ObservableProperty] private ShiftSummaryDto? _morningShift;
    [ObservableProperty] private ShiftSummaryDto? _afternoonShift;
    [ObservableProperty] private ShiftSummaryDto? _nightShift;

    // Sync status (bound prominently on the dashboard)
    [ObservableProperty] private string _lastSyncDisplay = "—";
    [ObservableProperty] private int _recordsPending;
    [ObservableProperty] private bool _isSyncHealthy = true;

    // Date selection
    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private bool _isLoading;

    public OwnerDashboardViewModel()
    {
        _calcService = App.Services.GetRequiredService<ShiftCalculationService>();
        _shiftRepository = App.Services.GetRequiredService<IShiftRepository>();
        _dsmEntryRepository = App.Services.GetRequiredService<IDsmEntryRepository>();
        _dsmCalculationService = App.Services.GetRequiredService<IDsmCalculationService>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _ownerCalcService = App.Services.GetRequiredService<IOwnerCalculationService>();
        _syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();

        // Wire Sync Status
        _syncEngine.SyncStatusChanged += (status) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(async () =>
            {
                UpdateSyncDisplay(status.LastSyncTime == default ? (DateTime?)null : status.LastSyncTime, status.PendingRecords);
                if (status.StatusMessage == "Synced")
                {
                    await LoadDataAsync();
                }
            });
        };
        // Initial sync state update
        UpdateSyncDisplay(_syncEngine.CurrentStatus.LastSyncTime == default ? (DateTime?)null : _syncEngine.CurrentStatus.LastSyncTime, _syncEngine.CurrentStatus.PendingRecords);

        _ = LoadDataAsync();
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        _ = LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var entriesResult = await _dsmEntryRepository.GetEntriesForDateRangeAsync(SelectedDate, SelectedDate);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            var shiftsResult = await _shiftRepository.GetShiftsByDateRangeAsync(SelectedDate, SelectedDate);
            var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();

            var shiftExpensesResult = await _expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
            var shiftExpenses = shiftExpensesResult.Success && shiftExpensesResult.Data != null ? shiftExpensesResult.Data : new List<Expense>();

            var otherCashResult = await App.Services.GetRequiredService<IShiftOtherCashRepository>().GetByDateRangeAsync(SelectedDate, SelectedDate);
            var otherCashList = otherCashResult.Success && otherCashResult.Data != null ? otherCashResult.Data : new List<ShiftOtherCash>();

            TotalDsmEntries = entries.Count;

            var result = _ownerCalcService.Calculate(entries, shiftExpenses, otherCashList);

            TodayTotalSale = result.GrossSales;
            TodayTotalCollection = result.AdjustedCollection;
            TodayTotalExpenses = result.Expenses;
            TodayTotalCash = result.TotalCash;
            TodayTotalPhonePe = result.TotalPhonePe;
            TodayTotalCreditCard = result.CreditCard;
            TodayTotalPetroCard = result.PetroCard;
            TodayTotalDebit = result.Debit;
            TodayHsdLitres = result.HsdLitres;
            TodayMsILitres = result.MsILitres;
            TodayMsIILitres = result.MsIILitres;
            TodayTotalLitres = result.TotalLitres;
            TodayTotalMismatch = result.Mismatch;
        }
        finally { IsLoading = false; }
    }

    public void UpdateSyncDisplay(DateTime? lastSync, int pending)
    {
        if (lastSync.HasValue)
        {
            LastSyncDisplay = lastSync.Value.ToString("dd MMM yyyy hh:mm tt");
        }
        else
        {
            LastSyncDisplay = "Never";
        }
        RecordsPending = pending;
        IsSyncHealthy = pending == 0;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsLoading = true;
        try
        {
            await _syncEngine.ForceSyncAsync();
            await LoadDataAsync();
            var status = _syncEngine.CurrentStatus;
            UpdateSyncDisplay(status.LastSyncTime == default ? (DateTime?)null : status.LastSyncTime, status.PendingRecords);
        }
        catch (System.Exception)
        {
            // Ignore/handle
        }
        finally
        {
            IsLoading = false;
        }
    }
}
