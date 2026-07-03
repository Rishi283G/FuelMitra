using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
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
    private readonly ITidCalculationService _tidService;
    private readonly FuelPro.Sync.SyncEngine _syncEngine;
    private readonly PrintService _printService;
    private readonly IFinancialCalculationService _financialCalcService;
    private readonly FuelProDbContext _dbContext;

    // Range-wise KPI Properties
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
    [ObservableProperty] private double _todayCngLitres;

    // Payment breakdown
    [ObservableProperty] private double _todayTotalCash;
    [ObservableProperty] private double _todayTotalPhonePe;
    [ObservableProperty] private double _todayTotalCreditCard;
    [ObservableProperty] private double _todayTotalPetroCard;
    [ObservableProperty] private double _todayTotalDebit;

    // Additional Range Metrics
    [ObservableProperty] private double _rangeNetProfit;
    [ObservableProperty] private double _outstandingDebtors;

    // Shift summaries
    [ObservableProperty] private ShiftSummaryDto? _morningShift;
    [ObservableProperty] private ShiftSummaryDto? _afternoonShift;
    [ObservableProperty] private ShiftSummaryDto? _nightShift;

    // Sync status (bound prominently on the dashboard)
    [ObservableProperty] private string _lastSyncDisplay = "—";
    [ObservableProperty] private int _recordsPending;
    [ObservableProperty] private bool _isSyncHealthy = true;

    // Date selection
    [ObservableProperty] private DateTime _startDate = DateTime.Today;
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private string _selectedPreset = "Today";
    [ObservableProperty] private bool _isLoading;

    public string[] Presets { get; } = { "Today", "Yesterday", "Weekly", "Monthly", "Custom" };

    partial void OnSelectedPresetChanged(string value)
    {
        if (value != "Custom" && !_isApplyingPreset)
        {
            _ = SetPresetAsync(value);
        }
    }

    public DateTime SelectedDate
    {
        get => StartDate;
        set
        {
            StartDate = value;
            EndDate = value;
            OnPropertyChanged(nameof(SelectedDate));
        }
    }


    private bool _isApplyingPreset;

    public OwnerDashboardViewModel()
    {
        _calcService = App.Services.GetRequiredService<ShiftCalculationService>();
        _shiftRepository = App.Services.GetRequiredService<IShiftRepository>();
        _dsmEntryRepository = App.Services.GetRequiredService<IDsmEntryRepository>();
        _dsmCalculationService = App.Services.GetRequiredService<IDsmCalculationService>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _ownerCalcService = App.Services.GetRequiredService<IOwnerCalculationService>();
        _tidService = App.Services.GetRequiredService<ITidCalculationService>();
        _syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
        _printService = App.Services.GetRequiredService<PrintService>();
        _financialCalcService = App.Services.GetRequiredService<IFinancialCalculationService>();
        _dbContext = App.Services.GetRequiredService<FuelProDbContext>();

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

        _ = SetPresetAsync(SelectedPreset);
    }

    partial void OnStartDateChanged(DateTime value)
    {
        if (!_isApplyingPreset)
            _ = LoadDataAsync();
    }

    partial void OnEndDateChanged(DateTime value)
    {
        if (!_isApplyingPreset)
            _ = LoadDataAsync();
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
                case "Custom":
                    break;
            }
        }
        finally
        {
            _isApplyingPreset = false;
        }
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var entriesResult = await _dsmEntryRepository.GetEntriesForDateRangeAsync(StartDate.Date, EndDate.Date);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            var shiftsResult = await _shiftRepository.GetShiftsByDateRangeAsync(StartDate.Date, EndDate.Date);
            var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();

            var shiftExpensesResult = await _expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
            var shiftExpenses = shiftExpensesResult.Success && shiftExpensesResult.Data != null ? shiftExpensesResult.Data : new List<Expense>();

            var otherCashResult = await App.Services.GetRequiredService<IShiftOtherCashRepository>().GetByDateRangeAsync(StartDate.Date, EndDate.Date);
            var otherCashList = otherCashResult.Success && otherCashResult.Data != null ? otherCashResult.Data : new List<ShiftOtherCash>();

            var tidSheets = await _tidService.GetTidSheetsForRangeAsync(StartDate.Date, EndDate.Date);
            var aggTidSheet = new BusinessDayTidSheet();
            foreach (var s in tidSheets.Values)
            {
                aggTidSheet.PhonePeDirectMorning += s.PhonePeDirectMorning;
                aggTidSheet.PhonePeDirectDay += s.PhonePeDirectDay;
                aggTidSheet.PhonePeDirectNight += s.PhonePeDirectNight;

                aggTidSheet.PhonePeCardMorning += s.PhonePeCardMorning;
                aggTidSheet.PhonePeCardDay += s.PhonePeCardDay;
                aggTidSheet.PhonePeCardNight += s.PhonePeCardNight;

                aggTidSheet.PineLabsCardMorning += s.PineLabsCardMorning;
                aggTidSheet.PineLabsCardDay += s.PineLabsCardDay;
                aggTidSheet.PineLabsCardNight += s.PineLabsCardNight;

                aggTidSheet.PetroCardMorning += s.PetroCardMorning;
                aggTidSheet.PetroCardDay += s.PetroCardDay;
                aggTidSheet.PetroCardNight += s.PetroCardNight;
            }

            TotalDsmEntries = entries.Count;
            var result = _ownerCalcService.Calculate(entries, shiftExpenses, otherCashList, aggTidSheet);

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
            TodayCngLitres = result.CngLitres;
            TodayTotalLitres = result.TotalLitres;
            TodayTotalMismatch = result.Mismatch;

            // Comprehensive Profit calculation from IFinancialCalculationService
            var finResult = await _financialCalcService.CalculateFinancialsAsync(StartDate.Date, EndDate.Date);
            RangeNetProfit = finResult.NetProfit;

            // Lifetime Outstanding Debtors (overall balance is a lifetime KPI)
            double totalDebits = await _dbContext.DebitEntries.SumAsync(d => d.Amount);
            double totalRepayments = await _dbContext.CreditorRepayments.SumAsync(r => r.Amount);
            OutstandingDebtors = totalDebits - totalRepayments;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load Owner Dashboard data");
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

    [RelayCommand]
    private void Print()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Gross Sales", Value = "₹" + TodayTotalSale.ToString("N2"), Highlight = true },
                new() { Label = "Volume Sold", Value = TodayTotalLitres.ToString("N2") + " L", Highlight = false },
                new() { Label = "Net Collection", Value = "₹" + TodayTotalCollection.ToString("N2"), Highlight = false },
                new() { Label = "Total Expenses", Value = "₹" + TodayTotalExpenses.ToString("N2"), Highlight = false },
                new() { Label = "Net Mismatch", Value = "₹" + TodayTotalMismatch.ToString("N2"), Highlight = false }
            };

            var headers = new List<string> { "Category / Section", "Item Name / Description", "Value" };
            var rows = new List<List<string>>
            {
                new() { "Fuel Sales Volume", "MS1 / Diesel (HSD)", TodayHsdLitres.ToString("N2") + " L" },
                new() { "Fuel Sales Volume", "MS-I (Petrol)", TodayMsILitres.ToString("N2") + " L" },
                new() { "Fuel Sales Volume", "MS-II (Power Petrol)", TodayMsIILitres.ToString("N2") + " L" },
                new() { "Fuel Sales Volume", "CNG", TodayCngLitres.ToString("N2") + " L" },
                new() { "Payment Mode Breakdown", "Cash", "₹" + TodayTotalCash.ToString("N2") },
                new() { "Payment Mode Breakdown", "PhonePe", "₹" + TodayTotalPhonePe.ToString("N2") },
                new() { "Payment Mode Breakdown", "PineLabs Card", "₹" + TodayTotalCreditCard.ToString("N2") },
                new() { "Payment Mode Breakdown", "Petro Card", "₹" + TodayTotalPetroCard.ToString("N2") },
                new() { "Payment Mode Breakdown", "Debit (Debtors)", "₹" + TodayTotalDebit.ToString("N2") }
            };

            string subtitle = StartDate.Date == EndDate.Date 
                ? $"Statement for Date: {StartDate:dd-MMM-yyyy}" 
                : $"Statement for Date Range: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}";

            var printData = new GenericGridPrintData
            {
                Title = "Owner Daily Dashboard Summary",
                Subtitle = subtitle,
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            _printService.PrintGenericGrid(printData);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print Owner Dashboard summary");
            MessageBox.Show($"Print failed: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        try
        {
            var exportService = App.Services.GetRequiredService<ExcelExportService>();
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Gross Sales", Value = "₹" + TodayTotalSale.ToString("N2"), Highlight = true },
                new() { Label = "Volume Sold", Value = TodayTotalLitres.ToString("N2") + " L", Highlight = false },
                new() { Label = "Net Collection", Value = "₹" + TodayTotalCollection.ToString("N2"), Highlight = false },
                new() { Label = "Total Expenses", Value = "₹" + TodayTotalExpenses.ToString("N2"), Highlight = false },
                new() { Label = "Net Mismatch", Value = "₹" + TodayTotalMismatch.ToString("N2"), Highlight = false }
            };

            var headers = new List<string> { "Category / Section", "Item Name / Description", "Value" };
            var rows = new List<List<string>>
            {
                new() { "Fuel Sales Volume", "MS1 / Diesel (HSD)", TodayHsdLitres.ToString("N2") + " L" },
                new() { "Fuel Sales Volume", "MS-I (Petrol)", TodayMsILitres.ToString("N2") + " L" },
                new() { "Fuel Sales Volume", "MS-II (Power Petrol)", TodayMsIILitres.ToString("N2") + " L" },
                new() { "Fuel Sales Volume", "CNG", TodayCngLitres.ToString("N2") + " L" },
                new() { "Payment Mode Breakdown", "Cash", "₹" + TodayTotalCash.ToString("N2") },
                new() { "Payment Mode Breakdown", "PhonePe", "₹" + TodayTotalPhonePe.ToString("N2") },
                new() { "Payment Mode Breakdown", "PineLabs Card", "₹" + TodayTotalCreditCard.ToString("N2") },
                new() { "Payment Mode Breakdown", "Petro Card", "₹" + TodayTotalPetroCard.ToString("N2") },
                new() { "Payment Mode Breakdown", "Debit (Debtors)", "₹" + TodayTotalDebit.ToString("N2") }
            };

            string subtitle = StartDate.Date == EndDate.Date 
                ? $"Statement for Date: {StartDate:dd-MMM-yyyy}" 
                : $"Statement for Date Range: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}";

            var printData = new GenericGridPrintData
            {
                Title = "Owner Daily Dashboard Summary",
                Subtitle = subtitle,
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var path = await exportService.ExportGenericGridAsync(printData, "OwnerDashboard");
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to export owner dashboard summary to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}


