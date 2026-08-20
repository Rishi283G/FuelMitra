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
public partial class OwnerDashboardViewModel : ObservableObject, IDisposable
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
    private readonly IReportService _reportService;

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
    [ObservableProperty] private double _oilDefSalesTotal;
    [ObservableProperty] private double _oilDefProfitTotal;
    [ObservableProperty] private double _todayTotalDsmShort;

    // Shift summaries
    [ObservableProperty] private ShiftSummaryDto? _morningShift;
    [ObservableProperty] private ShiftSummaryDto? _afternoonShift;
    [ObservableProperty] private ShiftSummaryDto? _nightShift;
    [ObservableProperty] private ObservableCollection<DsmShiftTotalDto> _dsmShiftTotals = new();

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
        _reportService = App.Services.GetRequiredService<IReportService>();

        // Wire Sync Status
        _syncEngine.SyncStatusChanged += OnSyncStatusChanged;
        // Initial sync state update
        UpdateSyncDisplay(_syncEngine.CurrentStatus.LastSyncTime == default ? (DateTime?)null : _syncEngine.CurrentStatus.LastSyncTime, _syncEngine.CurrentStatus.PendingRecords);

        DsmEntryService.DsmEntryChanged += OnDataChanged;
        DsmEntryService.PettyCashChanged += OnDataChanged;
        DsmEntryService.DebtorChanged += OnDataChanged;
        DsmEntryService.PayrollChanged += OnDataChanged;
        DsmEntryService.InventoryChanged += OnDataChanged;

        _ = SetPresetAsync(SelectedPreset);
    }

    private void OnSyncStatusChanged(FuelPro.Sync.SyncStatusInfo status)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(async () =>
        {
            UpdateSyncDisplay(status.LastSyncTime == default ? (DateTime?)null : status.LastSyncTime, status.PendingRecords);
            if (status.StatusMessage == "Synced")
            {
                await LoadDataAsync();
            }
        });
    }

    private void OnDataChanged()
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await LoadDataAsync());
    }

    public void Dispose()
    {
        _syncEngine.SyncStatusChanged -= OnSyncStatusChanged;
        DsmEntryService.DsmEntryChanged -= OnDataChanged;
        DsmEntryService.PettyCashChanged -= OnDataChanged;
        DsmEntryService.DebtorChanged -= OnDataChanged;
        DsmEntryService.PayrollChanged -= OnDataChanged;
        DsmEntryService.InventoryChanged -= OnDataChanged;
        GC.SuppressFinalize(this);
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
            try { await _syncEngine.ForceSyncAsync(); } catch { }

            var entriesResult = await _dsmEntryRepository.GetEntriesForDateRangeAsync(StartDate.Date, EndDate.Date.AddDays(1));
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            var shiftsResult = await _shiftRepository.GetShiftsByDateRangeAsync(StartDate.Date, EndDate.Date.AddDays(1));
            var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();

            var todayShifts = shifts.Where(s => s.ShiftDate.Date >= StartDate.Date && s.ShiftDate.Date <= EndDate.Date).ToList();
            var todayShiftIds = todayShifts.Select(s => s.ShiftId).ToList();

            var shiftExpensesResult = await _expenseRepo.GetExpensesByShiftIdsAsync(todayShiftIds);
            var shiftExpenses = shiftExpensesResult.Success && shiftExpensesResult.Data != null ? shiftExpensesResult.Data : new List<Expense>();

            var otherCashResult = await App.Services.GetRequiredService<IShiftOtherCashRepository>().GetByDateRangeAsync(StartDate.Date, EndDate.Date);
            var otherCashList = otherCashResult.Success && otherCashResult.Data != null ? otherCashResult.Data : new List<ShiftOtherCash>();

            var repayments = await _dbContext.CreditorRepayments
                .Where(r => r.RepaymentDate >= StartDate.Date && r.RepaymentDate <= EndDate.Date.AddDays(1))
                .ToListAsync();

            var settings = await _dbContext.Settings.FirstOrDefaultAsync();
            double defaultHsd = settings != null ? settings.HsdRate : 90.35;
            double defaultMsI = settings != null ? settings.MsIRate : 103.81;
            double defaultMsII = settings != null ? settings.MsIIRate : 103.81;
            double defaultCng = settings != null ? settings.CngRate : 85.0;
            string stationName = settings != null ? settings.PumpStationName : "Mitali Service Station";

            var dayReport = _reportService.CalculateDayReport(
                StartDate.Date,
                EndDate.Date,
                entries,
                shiftExpenses,
                repayments,
                defaultHsd, defaultMsI, defaultMsII, defaultCng,
                stationName);

            TotalDsmEntries = entries.Count;

            var aggregationService = App.Services.GetRequiredService<IShiftAggregationService>();
            var summaryRows = dayReport.DsmSummaryRows ?? aggregationService.BuildDsmSummaryRows(entries);
            var shiftTotals = dayReport.DsmShiftTotals ?? aggregationService.BuildDsmShiftTotals(summaryRows);
            DsmShiftTotals = new ObservableCollection<DsmShiftTotalDto>(shiftTotals);

            TodayTotalSale = dayReport.TotalFuelAmount + dayReport.OtherCashTotal + dayReport.OilDefSalesTotal;
            TodayTotalCollection = dayReport.ActualCollection;
            TodayTotalExpenses = dayReport.ExpensesTotal;

            double cashDeposit = dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category == "Cash Deposit")?.Amount ?? 0;
            double cashInHand = dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category == "Cash In Hand")?.Amount ?? 0;
            TodayTotalCash = cashDeposit + cashInHand;

            double ppMorning = dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category == "PhonePe Morning")?.Amount ?? 0;
            double ppNight = dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category == "PhonePe Night")?.Amount ?? 0;
            double ppCardMorning = dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category == "PhonePe Card Morning")?.Amount ?? 0;
            double ppCardNight = dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category == "PhonePe Card Night")?.Amount ?? 0;
            double ppDirect = dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category == "PhonePe")?.Amount ?? 0;
            TodayTotalPhonePe = ppMorning + ppNight + ppCardMorning + ppCardNight + ppDirect;

            double ccMorning = (dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category == "PineLabs Morning")?.Amount ?? 0)
                             + (dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category == "Card Morning")?.Amount ?? 0);
            double ccNight = (dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category == "PineLabs Night")?.Amount ?? 0)
                           + (dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category == "Card Night")?.Amount ?? 0);
            TodayTotalCreditCard = ccMorning + ccNight;

            double pcMorning = dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category == "Petro Card Morning")?.Amount ?? 0;
            double pcNight = dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category == "Petro Card Night")?.Amount ?? 0;
            double pcDirect = dayReport.CollectionBreakdown.FirstOrDefault(c => c.Category == "Petro Card")?.Amount ?? 0;
            TodayTotalPetroCard = pcMorning + pcNight + pcDirect;
            TodayTotalDebit = dayReport.CreditorsTotal;

            TodayHsdLitres = dayReport.FuelSales.FirstOrDefault(f => f.FuelType == "HSD")?.Litres ?? 0;
            TodayMsILitres = dayReport.FuelSales.FirstOrDefault(f => f.FuelType == "MS-I")?.Litres ?? 0;
            TodayMsIILitres = dayReport.FuelSales.FirstOrDefault(f => f.FuelType == "MS-II")?.Litres ?? 0;
            TodayCngLitres = dayReport.FuelSales.FirstOrDefault(f => f.FuelType == "CNG")?.Litres ?? 0;
            TodayTotalLitres = dayReport.TotalFuelLitres;
            TodayTotalMismatch = dayReport.Difference;
            OilDefSalesTotal = dayReport.OilDefSalesTotal;
            TodayTotalDsmShort = dayReport.TotalDsmShort;

            // Comprehensive Profit calculation from IFinancialCalculationService
            var finResult = await _financialCalcService.CalculateFinancialsAsync(StartDate.Date, EndDate.Date);
            RangeNetProfit = finResult.NetProfit;
            OilDefProfitTotal = finResult.OilProfit.TotalProfit + finResult.DefProfit.TotalProfit;

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


