using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
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

public partial class ReportsViewModel : ObservableObject
{
    private readonly ExportService _exportService;
    private readonly IShiftRepository _shiftRepo;
    private readonly IDsmEntryRepository _dsmRepo;
    private readonly IExpenseRepository _expenseRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IShiftAggregationService _aggregation;
    private readonly IFinancialCalculationService _financialCalcService;
    private readonly ITidCalculationService _tidService;
    private readonly IReportService _reportService;

    [ObservableProperty] private string _selectedReportType = "DSR"; // "DSR" or "MonthlyPL"
    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private int _selectedYear = DateTime.Today.Year;
    [ObservableProperty] private string _selectedMonthName = DateTime.Today.ToString("MMMM");

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<int> Years { get; } = new();
    public ObservableCollection<string> Months { get; } = new();

    private readonly string[] _monthNames = {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    };

    public ReportsViewModel()
    {
        _exportService = App.Services.GetRequiredService<ExportService>();
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _dsmRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();
        _aggregation = App.Services.GetRequiredService<IShiftAggregationService>();
        _financialCalcService = App.Services.GetRequiredService<IFinancialCalculationService>();
        _tidService = App.Services.GetRequiredService<ITidCalculationService>();
        _reportService = App.Services.GetRequiredService<IReportService>();

        // Populate Years (Current Year - 2 to Current Year + 2)
        var currYear = DateTime.Today.Year;
        for (int y = currYear - 2; y <= currYear + 2; y++)
        {
            Years.Add(y);
        }

        // Populate Months
        foreach (var m in _monthNames)
        {
            Months.Add(m);
        }
    }

    private int GetMonthNumber(string name)
    {
        int idx = Array.IndexOf(_monthNames, name);
        return idx >= 0 ? idx + 1 : DateTime.Today.Month;
    }

    [RelayCommand]
    private async Task PrintReportAsync()
    {
        IsLoading = true;
        StatusMessage = "Preparing report for printing...";
        try
        {
            if (SelectedReportType == "DSR")
            {
                // DSR: Daily Sales Register (Single Day)
                var entriesResult = await _dsmRepo.GetEntriesForDateRangeAsync(SelectedDate, SelectedDate);
                var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

                if (entries.Count == 0)
                {
                    MessageBox.Show("No entry data found for the selected date.", "PyroSync — Report", MessageBoxButton.OK, MessageBoxImage.Information);
                    StatusMessage = "No data found.";
                    return;
                }

                var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(SelectedDate, SelectedDate);
                var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
                var shiftIds = shifts.Select(s => s.ShiftId).ToList();

                var expResult = await _expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
                var allExpenses = expResult.Success && expResult.Data != null ? expResult.Data : new List<Expense>();

                var repaymentsRes = await App.Services.GetRequiredService<ICreditorRepaymentRepository>().GetByDateRangeAsync(SelectedDate.Date, SelectedDate.Date);
                var repayments = repaymentsRes.Success && repaymentsRes.Data != null ? repaymentsRes.Data : new List<CreditorRepayment>();

                var settings = await _settingsRepo.GetSettingsAsync();
                double defaultHsd = settings.Success ? settings.Data!.HsdRate : 90.35;
                double defaultMsI = settings.Success ? settings.Data!.MsIRate : 103.81;
                double defaultMsII = settings.Success ? settings.Data!.MsIIRate : 103.81;
                double defaultCng = settings.Success ? settings.Data!.CngRate : 85.0;
                string stationName = settings.Success ? settings.Data!.PumpStationName : "PyroSync";

                var report = _reportService.CalculateDayReport(
                    SelectedDate,
                    SelectedDate,
                    entries,
                    allExpenses,
                    repayments,
                    defaultHsd, defaultMsI, defaultMsII, defaultCng,
                    stationName);

                new PrintService().PrintDayTotal(report);
                StatusMessage = "DSR Report opened in browser.";
            }
            else if (SelectedReportType == "MonthlyPL")
            {
                // Monthly Profit & Loss Statement
                int monthNum = GetMonthNumber(SelectedMonthName);
                var startDate = new DateTime(SelectedYear, monthNum, 1);
                var endDate = new DateTime(SelectedYear, monthNum, DateTime.DaysInMonth(SelectedYear, monthNum));
                
                var financials = await _financialCalcService.CalculateFinancialsAsync(startDate, endDate);

                // Fetch operational expenses
                var entriesResult = await _dsmRepo.GetEntriesForDateRangeAsync(startDate, endDate);
                var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();
                var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(startDate, endDate);
                var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
                var shiftIds = shifts.Select(s => s.ShiftId).ToList();

                var expensesByCategory = new Dictionary<string, double>();
                foreach (var entry in entries)
                {
                    foreach (var exp in entry.Expenses)
                    {
                        var cat = exp.Description ?? "Other";
                        if (!expensesByCategory.ContainsKey(cat)) expensesByCategory[cat] = 0;
                        expensesByCategory[cat] += exp.Amount;
                    }
                }
                var shiftExpResult = await _expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
                if (shiftExpResult.Success && shiftExpResult.Data != null)
                {
                    foreach (var exp in shiftExpResult.Data)
                    {
                        var cat = exp.Description ?? "Shift Expense";
                        if (!expensesByCategory.ContainsKey(cat)) expensesByCategory[cat] = 0;
                        expensesByCategory[cat] += exp.Amount;
                    }
                }

                var expenseRows = expensesByCategory
                    .OrderByDescending(x => x.Value)
                    .Select(kvp => new { category = kvp.Key, amount = kvp.Value })
                    .ToList();

                var stationName = "PyroSync";
                var s = await _settingsRepo.GetSettingsAsync();
                if (s.Success && s.Data != null) stationName = s.Data.PumpStationName;

                var payload = new
                {
                    startDate = startDate.ToString("dd-MMM-yyyy"),
                    endDate = endDate.ToString("dd-MMM-yyyy"),
                    stationName,
                    fuelProfit = new
                    {
                        hsdLitres = financials.FuelProfit.HsdLitres,
                        hsdMargin = financials.FuelProfit.HsdMargin,
                        hsdProfit = financials.FuelProfit.HsdProfit,
                        msILitres = financials.FuelProfit.MsILitres,
                        msIMargin = financials.FuelProfit.MsIMargin,
                        msIProfit = financials.FuelProfit.MsIProfit,
                        msIILitres = financials.FuelProfit.MsIILitres,
                        msIIMargin = financials.FuelProfit.MsIIMargin,
                        msIIProfit = financials.FuelProfit.MsIIProfit,
                        totalLitres = financials.FuelProfit.TotalLitres,
                        totalFuelProfit = financials.FuelProfit.TotalFuelProfit
                    },
                    oilProfit = new
                    {
                        openingStock = financials.OilProfit.OpeningStock,
                        closingStock = financials.OilProfit.ClosingStock,
                        salesQuantity = financials.OilProfit.SalesQuantity,
                        averagePurchasePrice = financials.OilProfit.AveragePurchasePrice,
                        salePrice = financials.OilProfit.SalePrice,
                        totalProfit = financials.OilProfit.TotalProfit
                    },
                    defProfit = new
                    {
                        openingStock = financials.DefProfit.OpeningStock,
                        closingStock = financials.DefProfit.ClosingStock,
                        salesQuantity = financials.DefProfit.SalesQuantity,
                        averagePurchasePrice = financials.DefProfit.AveragePurchasePrice,
                        salePrice = financials.DefProfit.SalePrice,
                        totalProfit = financials.DefProfit.TotalProfit
                    },
                    expenses = expenseRows,
                    totalExpenses = financials.TotalExpenses,
                    totalDsmSalaries = financials.TotalDsmSalaries,
                    grossProfit = financials.GrossProfit,
                    ownerOuterExpenses = financials.OwnerOuterExpenses,
                    totalMismatch = financials.TotalMismatch,
                    netProfit = financials.NetProfit
                };

                new PrintService().PrintMonthlyPL(payload);
                StatusMessage = "Monthly P&L Statement opened in browser.";
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print report");
            MessageBox.Show($"Print failed: {ex.Message}", "PyroSync — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "Print failed.";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ExportMonthlyReportAsync()
    {
        IsLoading = true;
        try
        {
            DateTime startDate, endDate;
            if (SelectedReportType == "DSR")
            {
                StatusMessage = "Exporting DSR report to Excel...";
                startDate = SelectedDate.Date;
                endDate = SelectedDate.Date;
            }
            else
            {
                StatusMessage = "Exporting monthly Excel report...";
                int monthNum = GetMonthNumber(SelectedMonthName);
                startDate = new DateTime(SelectedYear, monthNum, 1);
                endDate = new DateTime(SelectedYear, monthNum, DateTime.DaysInMonth(SelectedYear, monthNum));
            }
            
            var result = await _exportService.ExportDailyDataAsync(startDate, endDate);
            StatusMessage = result.Success
                ? $"✅ Report exported: {result.Data}"
                : $"❌ Export failed: {result.Error}";
        }
        catch (System.Exception ex)
        {
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }
}
