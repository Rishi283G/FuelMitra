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
    private readonly ICollectionTypeService? _collectionTypeService;

    // Range-wise KPI Properties
    [ObservableProperty] private double _todayTotalSale;
    [ObservableProperty] private double _todayTotalLitres;
    [ObservableProperty] private double _todayTotalCollection;
    [ObservableProperty] private double _todayTotalExpenses;
    [ObservableProperty] private double _todayTotalMismatch;
    [ObservableProperty] private int _totalDsmEntries;

    // Dynamic Fuel-wise breakdown
    public ObservableCollection<FuelSaleRowDto> FuelSalesBreakdown { get; } = new();

    // Fuel-wise breakdown properties (backward-compatible)
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

    // Dynamic Payment Mode Breakdown
    public ObservableCollection<DayCollectionSummaryRow> PaymentBreakdown { get; } = new();

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
        _collectionTypeService = App.Services.GetService<ICollectionTypeService>();

        // Wire Sync Status
        _syncEngine.SyncStatusChanged += OnSyncStatusChanged;
        // Initial sync state update
        UpdateSyncDisplay(_syncEngine.CurrentStatus.LastSyncTime == default ? (DateTime?)null : _syncEngine.CurrentStatus.LastSyncTime, _syncEngine.CurrentStatus.PendingRecords);

        DsmEntryService.DsmEntryChanged += OnDataChanged;
        DsmEntryService.PettyCashChanged += OnDataChanged;
        DsmEntryService.DebtorChanged += OnDataChanged;
        DsmEntryService.PayrollChanged += OnDataChanged;
        DsmEntryService.InventoryChanged += OnDataChanged;
        DsmEntryService.StationConfigurationChanged += OnDataChanged;
        if (_collectionTypeService != null) _collectionTypeService.CollectionTypesChanged += OnDataChanged;

        _ = SetPresetAsync(SelectedPreset);
    }

    private void OnSyncStatusChanged(FuelPro.Sync.SyncStatusInfo status)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            UpdateSyncDisplay(status.LastSyncTime == default ? (DateTime?)null : status.LastSyncTime, status.PendingRecords);
        });
    }

    private void OnDataChanged()
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await LoadDataAsync(isBackgroundRefresh: true));
    }

    public void Dispose()
    {
        _syncEngine.SyncStatusChanged -= OnSyncStatusChanged;
        DsmEntryService.DsmEntryChanged -= OnDataChanged;
        DsmEntryService.PettyCashChanged -= OnDataChanged;
        DsmEntryService.DebtorChanged -= OnDataChanged;
        DsmEntryService.PayrollChanged -= OnDataChanged;
        DsmEntryService.InventoryChanged -= OnDataChanged;
        DsmEntryService.StationConfigurationChanged -= OnDataChanged;
        if (_collectionTypeService != null) _collectionTypeService.CollectionTypesChanged -= OnDataChanged;
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
        await LoadDataInternalAsync(false);
    }

    public async Task LoadDataAsync(bool isBackgroundRefresh)
    {
        await LoadDataInternalAsync(isBackgroundRefresh);
    }

    private async Task LoadDataInternalAsync(bool isBackgroundRefresh)
    {
        if (!isBackgroundRefresh)
        {
            IsLoading = true;
        }
        try
        {
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
            UpdateDsmShiftTotals(shiftTotals);

            TodayTotalSale = dayReport.TotalFuelAmount + dayReport.OtherCashTotal + dayReport.OilDefSalesTotal;
            TodayTotalCollection = dayReport.ActualCollection;
            TodayTotalExpenses = dayReport.ExpensesTotal;

            double cashDeposit = dayReport.CollectionBreakdown.FirstOrDefault(c => string.Equals(c.Category, "Cash Deposit", StringComparison.OrdinalIgnoreCase))?.Amount ?? 0;
            double cashInHand = dayReport.CollectionBreakdown.FirstOrDefault(c => string.Equals(c.Category, "Cash In Hand", StringComparison.OrdinalIgnoreCase))?.Amount ?? 0;
            TodayTotalCash = cashDeposit + cashInHand;

            TodayTotalPhonePe = dayReport.CollectionBreakdown.Where(c => c.Category.Contains("PhonePe", StringComparison.OrdinalIgnoreCase)).Sum(c => c.Amount);
            TodayTotalCreditCard = dayReport.CollectionBreakdown.Where(c => (c.Category.Contains("Card", StringComparison.OrdinalIgnoreCase) || c.Category.Contains("PineLab", StringComparison.OrdinalIgnoreCase)) && !c.Category.Contains("Petro", StringComparison.OrdinalIgnoreCase) && !c.Category.Contains("PhonePe", StringComparison.OrdinalIgnoreCase)).Sum(c => c.Amount);
            TodayTotalPetroCard = dayReport.CollectionBreakdown.Where(c => c.Category.Contains("Petro", StringComparison.OrdinalIgnoreCase)).Sum(c => c.Amount);
            TodayTotalDebit = dayReport.CreditorsTotal;

            // In-place smooth update for Fuel Sales Breakdown
            UpdateFuelSalesBreakdown(dayReport.FuelSales);

            // Populate Dynamic Payment Mode Breakdown (Always show all active configured payment modes)
            var breakdown = dayReport.CollectionBreakdown ?? new List<CollectionCategoryDto>();
            var activeTypes = _collectionTypeService != null ? await _collectionTypeService.GetActiveCollectionTypesAsync() : new List<CollectionTypeMaster>();
            var newPaymentRows = new List<DayCollectionSummaryRow>();

            // 1. Bank Cash (Deposit) - Merge all bank cash / cash deposit variations into one single option
            double cashDepositAmt = breakdown
                .Where(c => c.Category.Equals("Cash Deposit", StringComparison.OrdinalIgnoreCase) ||
                            c.Category.Equals("Cash Deposit (Bank)", StringComparison.OrdinalIgnoreCase) ||
                            c.Category.Equals("Bank Cash (Deposit)", StringComparison.OrdinalIgnoreCase) ||
                            c.Category.Equals("Bank Cash", StringComparison.OrdinalIgnoreCase) ||
                            c.Category.Equals("Cash (Deposit)", StringComparison.OrdinalIgnoreCase) ||
                            c.Category.Equals("CASH_DEPOSIT", StringComparison.OrdinalIgnoreCase))
                .Sum(c => c.Amount);

            newPaymentRows.Add(new DayCollectionSummaryRow
            {
                CollectionMode = "Bank Cash (Deposit)",
                Amount = cashDepositAmt,
                DisplayColor = "#2E7D32"
            });

            // 2. Cash In Hand - Merge all hand cash variations
            double cashInHandAmt = breakdown
                .Where(c => c.Category.Equals("Cash In Hand", StringComparison.OrdinalIgnoreCase) ||
                            c.Category.Equals("Hand Cash", StringComparison.OrdinalIgnoreCase) ||
                            c.Category.Equals("Cash 2", StringComparison.OrdinalIgnoreCase) ||
                            c.Category.Equals("CASH_IN_HAND", StringComparison.OrdinalIgnoreCase))
                .Sum(c => c.Amount);

            newPaymentRows.Add(new DayCollectionSummaryRow
            {
                CollectionMode = "Cash In Hand",
                Amount = cashInHandAmt,
                DisplayColor = "#388E3C"
            });

            // Mark all cash deposit, hand cash, and standard cash variations as handled so they are not duplicated
            var handledCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Cash Deposit",
                "Cash Deposit (Bank)",
                "Bank Cash (Deposit)",
                "Bank Cash",
                "Cash (Deposit)",
                "CASH_DEPOSIT",
                "CASH_DEPOSIT_BANK",
                "BANK_CASH",
                "Cash In Hand",
                "Hand Cash",
                "Cash 2",
                "CASH_IN_HAND",
                "HAND_CASH",
                "Cash"
            };

            bool IsCashVariant(CollectionTypeMaster t)
            {
                if (string.IsNullOrWhiteSpace(t.Code) && string.IsNullOrWhiteSpace(t.DisplayName)) return false;
                var code = t.Code ?? "";
                var name = t.DisplayName ?? "";
                return code.Equals("CASH_DEPOSIT", StringComparison.OrdinalIgnoreCase) ||
                       code.Equals("CASH_DEPOSIT_BANK", StringComparison.OrdinalIgnoreCase) ||
                       code.Equals("BANK_CASH", StringComparison.OrdinalIgnoreCase) ||
                       code.Equals("CASH_IN_HAND", StringComparison.OrdinalIgnoreCase) ||
                       code.Equals("HAND_CASH", StringComparison.OrdinalIgnoreCase) ||
                       code.Equals("CASH", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("Cash", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("Cash Deposit", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("Cash Deposit (Bank)", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("Bank Cash (Deposit)", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("Bank Cash", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("Cash In Hand", StringComparison.OrdinalIgnoreCase) ||
                       name.Equals("Hand Cash", StringComparison.OrdinalIgnoreCase);
            }

            // 3. Active configured collection types (excluding duplicate cash variants)
            if (activeTypes != null && activeTypes.Any())
            {
                foreach (var t in activeTypes.Where(t => !IsCashVariant(t)))
                {
                    var match = breakdown.FirstOrDefault(c => string.Equals(c.Category, t.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                                                              string.Equals(c.Category, t.Code, StringComparison.OrdinalIgnoreCase) ||
                                                              (t.DisplayName.Contains("PhonePe", StringComparison.OrdinalIgnoreCase) && c.Category.Contains("PhonePe", StringComparison.OrdinalIgnoreCase)) ||
                                                              (t.DisplayName.Contains("Petro", StringComparison.OrdinalIgnoreCase) && c.Category.Contains("Petro", StringComparison.OrdinalIgnoreCase)));

                    double amt = match?.Amount ?? 0;
                    if (match != null) handledCategories.Add(match.Category);
                    handledCategories.Add(t.DisplayName);
                    handledCategories.Add(t.Code);

                    string cat = t.DisplayName;
                    if (t.Code == "CARD" && (cat == "Credit / Debit Card" || cat == "Credit Card" || cat == "Card"))
                    {
                        cat = "PineLab Card";
                    }
                    bool isPetro = cat.Contains("Petro", StringComparison.OrdinalIgnoreCase);
                    bool isCard = cat.Contains("Card", StringComparison.OrdinalIgnoreCase) || cat.Contains("PineLab", StringComparison.OrdinalIgnoreCase);
                    bool isPhonePe = cat.Contains("PhonePe", StringComparison.OrdinalIgnoreCase);

                    string color = isPhonePe ? "#5E35B1" :
                                   isPetro ? "#6A1B9A" :
                                   isCard ? "#0288D1" : "#1565C0";

                    newPaymentRows.Add(new DayCollectionSummaryRow
                    {
                        CollectionMode = cat,
                        Amount = amt,
                        DisplayColor = color
                    });
                }
            }
            else
            {
                newPaymentRows.Add(new DayCollectionSummaryRow { CollectionMode = "PhonePe", Amount = TodayTotalPhonePe, DisplayColor = "#5E35B1" });
                newPaymentRows.Add(new DayCollectionSummaryRow { CollectionMode = "PineLabs Card", Amount = TodayTotalCreditCard, DisplayColor = "#0288D1" });
                newPaymentRows.Add(new DayCollectionSummaryRow { CollectionMode = "PetroCard", Amount = TodayTotalPetroCard, DisplayColor = "#6A1B9A" });
                handledCategories.Add("PhonePe");
                handledCategories.Add("PineLabs Card");
                handledCategories.Add("PetroCard");
            }

            // 4. Debtors (Credit)
            newPaymentRows.Add(new DayCollectionSummaryRow
            {
                CollectionMode = "Debtors (Credit)",
                Amount = dayReport.CreditorsTotal,
                DisplayColor = "#E65100"
            });
            handledCategories.Add("Debtors");
            handledCategories.Add("Debtors (Credit)");
            handledCategories.Add("Credit / Debtors");
            handledCategories.Add("Credit");

            // 5. Any extra unhandled dynamic items from dayReport.CollectionBreakdown
            foreach (var c in breakdown)
            {
                if (handledCategories.Contains(c.Category) ||
                    c.Category.Contains("Cash Deposit", StringComparison.OrdinalIgnoreCase) ||
                    c.Category.Contains("Bank Cash", StringComparison.OrdinalIgnoreCase) ||
                    c.Category.Contains("Cash In Hand", StringComparison.OrdinalIgnoreCase) ||
                    c.Category.Contains("Hand Cash", StringComparison.OrdinalIgnoreCase) ||
                    c.Category.Contains("Testing", StringComparison.OrdinalIgnoreCase) ||
                    c.Category.Equals("Expenses", StringComparison.OrdinalIgnoreCase) ||
                    c.Category.Contains("DSM Short", StringComparison.OrdinalIgnoreCase) ||
                    c.Category.Contains("Kandhare", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                newPaymentRows.Add(new DayCollectionSummaryRow
                {
                    CollectionMode = c.Category,
                    Amount = c.Amount,
                    DisplayColor = "#00838F"
                });
            }

            // Perform in-place smooth differential update for Payment Breakdown
            UpdatePaymentBreakdown(newPaymentRows);

            TodayHsdLitres = dayReport.FuelSales
                .Where(f => string.Equals(f.FuelType, "HSD", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(f.FuelType, "Diesel", StringComparison.OrdinalIgnoreCase) ||
                            (f.Description != null && f.Description.Contains("HSD", StringComparison.OrdinalIgnoreCase)))
                .Sum(f => f.Litres);

            TodayMsILitres = dayReport.FuelSales
                .Where(f => (string.Equals(f.FuelType, "MS-I", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(f.FuelType, "MS", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(f.FuelType, "Petrol", StringComparison.OrdinalIgnoreCase) ||
                             (f.Description != null && (f.Description.Contains("MS", StringComparison.OrdinalIgnoreCase) || f.Description.Contains("Petrol", StringComparison.OrdinalIgnoreCase))))
                            && !string.Equals(f.FuelType, "SPEED", StringComparison.OrdinalIgnoreCase)
                            && !(f.Description != null && (f.Description.Contains("SPEED", StringComparison.OrdinalIgnoreCase) || f.Description.Contains("20KL II", StringComparison.OrdinalIgnoreCase) || f.Description.Contains("XP", StringComparison.OrdinalIgnoreCase) || f.Description.Contains("Power", StringComparison.OrdinalIgnoreCase))))
                .Sum(f => f.Litres);

            TodayMsIILitres = dayReport.FuelSales
                .Where(f => string.Equals(f.FuelType, "SPEED", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(f.FuelType, "MS-II", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(f.FuelType, "Power", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(f.FuelType, "XP", StringComparison.OrdinalIgnoreCase) ||
                            (f.Description != null && (f.Description.Contains("SPEED", StringComparison.OrdinalIgnoreCase) ||
                                                       f.Description.Contains("XP", StringComparison.OrdinalIgnoreCase) ||
                                                       f.Description.Contains("Power", StringComparison.OrdinalIgnoreCase) ||
                                                       f.Description.Contains("20KL II", StringComparison.OrdinalIgnoreCase))))
                .Sum(f => f.Litres);

            TodayCngLitres = dayReport.FuelSales
                .Where(f => string.Equals(f.FuelType, "CNG", StringComparison.OrdinalIgnoreCase) ||
                            (f.Description != null && f.Description.Contains("CNG", StringComparison.OrdinalIgnoreCase)))
                .Sum(f => f.Litres);
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
        finally
        {
            if (!isBackgroundRefresh)
            {
                IsLoading = false;
            }
        }
    }

    private void UpdateFuelSalesBreakdown(List<FuelSaleRowDto> newSales)
    {
        for (int i = 0; i < newSales.Count; i++)
        {
            var newItem = newSales[i];
            var existing = FuelSalesBreakdown.FirstOrDefault(f => string.Equals(f.Description, newItem.Description, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (Math.Abs(existing.Litres - newItem.Litres) > 0.001) existing.Litres = newItem.Litres;
                if (Math.Abs(existing.Rate - newItem.Rate) > 0.001) existing.Rate = newItem.Rate;
                if (Math.Abs(existing.Amount - newItem.Amount) > 0.001) existing.Amount = newItem.Amount;
                if (existing.FuelType != newItem.FuelType) existing.FuelType = newItem.FuelType;
            }
            else
            {
                FuelSalesBreakdown.Insert(Math.Min(i, FuelSalesBreakdown.Count), new FuelSaleRowDto
                {
                    Description = newItem.Description,
                    FuelType = newItem.FuelType,
                    Litres = newItem.Litres,
                    Rate = newItem.Rate,
                    Amount = newItem.Amount
                });
            }
        }

        for (int i = FuelSalesBreakdown.Count - 1; i >= 0; i--)
        {
            var current = FuelSalesBreakdown[i];
            if (!newSales.Any(n => string.Equals(n.Description, current.Description, StringComparison.OrdinalIgnoreCase)))
            {
                FuelSalesBreakdown.RemoveAt(i);
            }
        }
    }

    private void UpdatePaymentBreakdown(List<DayCollectionSummaryRow> newItems)
    {
        for (int i = 0; i < newItems.Count; i++)
        {
            var newItem = newItems[i];
            var existing = PaymentBreakdown.FirstOrDefault(p => string.Equals(p.CollectionMode, newItem.CollectionMode, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (Math.Abs(existing.Amount - newItem.Amount) > 0.001) existing.Amount = newItem.Amount;
                if (existing.DisplayColor != newItem.DisplayColor) existing.DisplayColor = newItem.DisplayColor;
            }
            else
            {
                PaymentBreakdown.Insert(Math.Min(i, PaymentBreakdown.Count), new DayCollectionSummaryRow
                {
                    CollectionMode = newItem.CollectionMode,
                    Amount = newItem.Amount,
                    DisplayColor = newItem.DisplayColor
                });
            }
        }

        for (int i = PaymentBreakdown.Count - 1; i >= 0; i--)
        {
            var current = PaymentBreakdown[i];
            if (!newItems.Any(n => string.Equals(n.CollectionMode, current.CollectionMode, StringComparison.OrdinalIgnoreCase)))
            {
                PaymentBreakdown.RemoveAt(i);
            }
        }
    }

    private void UpdateDsmShiftTotals(List<DsmShiftTotalDto> newTotals)
    {
        for (int i = 0; i < newTotals.Count; i++)
        {
            var newItem = newTotals[i];
            if (i < DsmShiftTotals.Count)
            {
                var existing = DsmShiftTotals[i];
                if (existing.DsmName != newItem.DsmName ||
                    existing.SessionsCount != newItem.SessionsCount ||
                    Math.Abs(existing.GrossSales - newItem.GrossSales) > 0.01 ||
                    Math.Abs(existing.TotalCollection - newItem.TotalCollection) > 0.01 ||
                    Math.Abs(existing.Mismatch - newItem.Mismatch) > 0.01)
                {
                    DsmShiftTotals[i] = newItem;
                }
            }
            else
            {
                DsmShiftTotals.Add(newItem);
            }
        }

        while (DsmShiftTotals.Count > newTotals.Count)
        {
            DsmShiftTotals.RemoveAt(DsmShiftTotals.Count - 1);
        }
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
            string period = StartDate.Date == EndDate.Date 
                ? StartDate.ToString("dd-MMM-yyyy") 
                : $"{StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}";

            var payload = new
            {
                statementPeriod = period,
                presetLabel = SelectedPreset,
                generatedOn = DateTime.Now.ToString("dd-MMM-yyyy hh:mm tt"),
                kpis = new
                {
                    grossSales = TodayTotalSale,
                    totalVolume = TodayTotalLitres,
                    netCollection = TodayTotalCollection,
                    expenses = TodayTotalExpenses,
                    mismatch = TodayTotalMismatch,
                    oilDefSales = OilDefSalesTotal,
                    oilDefProfit = OilDefProfitTotal,
                    netProfit = RangeNetProfit,
                    debtorsOutstanding = OutstandingDebtors,
                    dsmEntries = TotalDsmEntries
                },
                fuelSales = FuelSalesBreakdown.Select(f => new
                {
                    description = f.Description,
                    fuelType = f.FuelType,
                    litres = f.Litres,
                    rate = f.Rate,
                    amount = f.Amount,
                    testingLitres = 0.0
                }).ToList(),
                paymentBreakdown = PaymentBreakdown.Select(p => new
                {
                    collectionMode = p.CollectionMode,
                    category = p.CollectionMode.Contains("Cash") ? "Cash Mode" : (p.CollectionMode.Contains("Credit") || p.CollectionMode.Contains("Debtor") ? "Credit Mode" : "Digital Mode"),
                    amount = p.Amount
                }).ToList(),
                dsmShiftTotals = DsmShiftTotals.Select(d => new
                {
                    dsmName = d.DsmName,
                    shiftCount = d.SessionsCount,
                    assignedPumps = d.AssignedPumpsDisplay,
                    grossSales = d.GrossSales,
                    digitalAmount = d.DigitalTotal,
                    bankCash = d.CashDeposit,
                    cashInHand = d.CashInHand,
                    debtors = d.Debit,
                    expenses = d.Expenses,
                    testingLitres = d.Testing,
                    totalCollection = d.TotalCollection,
                    mismatch = d.Mismatch
                }).ToList()
            };

            _printService.PrintOwnerDashboard(payload);
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
                new() { Label = "Net Mismatch", Value = "₹" + TodayTotalMismatch.ToString("N2"), Highlight = false },
                new() { Label = "Oil & DEF Sales", Value = "₹" + OilDefSalesTotal.ToString("N2"), Highlight = false },
                new() { Label = "Oil & DEF Profit", Value = "₹" + OilDefProfitTotal.ToString("N2"), Highlight = false },
                new() { Label = "Net Profit", Value = "₹" + RangeNetProfit.ToString("N2"), Highlight = true },
                new() { Label = "Debtors Outstanding", Value = "₹" + OutstandingDebtors.ToString("N2"), Highlight = false },
                new() { Label = "DSM Entries", Value = TotalDsmEntries.ToString(), Highlight = false }
            };

            var headers = new List<string> { "Section", "Item / Name", "Details", "Amount / Value" };
            var rows = new List<List<string>>();

            // 1. Fuel Sales Breakdown
            foreach (var fs in FuelSalesBreakdown)
            {
                rows.Add(new List<string>
                {
                    "1. Fuel Sales Breakdown",
                    fs.Description,
                    $"{fs.Litres:N2} L @ ₹{fs.Rate:N2}/L",
                    $"₹{fs.Amount:N2}"
                });
            }

            // 2. Payment Modes
            foreach (var pb in PaymentBreakdown)
            {
                rows.Add(new List<string>
                {
                    "2. Payment Modes",
                    pb.CollectionMode,
                    pb.CollectionMode.Contains("Cash") ? "Cash Mode" : (pb.CollectionMode.Contains("Credit") || pb.CollectionMode.Contains("Debtor") ? "Credit Mode" : "Digital Mode"),
                    $"₹{pb.Amount:N2}"
                });
            }

            // 3. DSM Shift Totals
            foreach (var st in DsmShiftTotals)
            {
                rows.Add(new List<string>
                {
                    "3. DSM Shift Summary",
                    st.DsmName,
                    $"Shifts: {st.SessionsCount}, Pumps: {st.AssignedPumpsDisplay}",
                    $"Gross: ₹{st.GrossSales:N2} | Coll: ₹{st.TotalCollection:N2} | Mis: ₹{st.Mismatch:N2}"
                });
            }

            string subtitle = StartDate.Date == EndDate.Date 
                ? $"Statement for Date: {StartDate:dd-MMM-yyyy}" 
                : $"Statement for Date Range: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}";

            var printData = new GenericGridPrintData
            {
                Title = "Owner Operations & Financial Dashboard Summary",
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


