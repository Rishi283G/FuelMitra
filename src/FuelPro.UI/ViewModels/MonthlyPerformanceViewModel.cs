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
using System.Windows;

namespace FuelPro.UI.ViewModels;

/// <summary>
/// Monthly performance overview: day-by-day totals for the selected month.
/// </summary>
public partial class MonthlyPerformanceViewModel : ObservableObject, IDisposable
{
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IShiftRepository _shiftRepo;
    private readonly IDsmCalculationService _calcService;
    private readonly IOwnerCalculationService _ownerCalcService;
    private readonly ITidCalculationService _tidService;
    private readonly PrintService _printService;
    private readonly ExcelExportService _excelExportService;
    private readonly ICreditorRepaymentRepository _repaymentRepo;
    private readonly IReportService _reportService;
    private readonly ISettingsRepository _settingsRepo;

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
        _tidService = App.Services.GetRequiredService<ITidCalculationService>();
        _printService = App.Services.GetRequiredService<PrintService>();
        _excelExportService = App.Services.GetRequiredService<ExcelExportService>();
        _repaymentRepo = App.Services.GetRequiredService<ICreditorRepaymentRepository>();
        _reportService = App.Services.GetRequiredService<IReportService>();
        _settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();

        DsmEntryService.DsmEntryChanged += OnDataChanged;
        DsmEntryService.PettyCashChanged += OnDataChanged;
        DsmEntryService.DebtorChanged += OnDataChanged;
        DsmEntryService.PayrollChanged += OnDataChanged;
        DsmEntryService.InventoryChanged += OnDataChanged;

        _ = LoadAsync();
    }

    private void OnDataChanged()
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await LoadAsync());
    }

    public void Dispose()
    {
        DsmEntryService.DsmEntryChanged -= OnDataChanged;
        DsmEntryService.PettyCashChanged -= OnDataChanged;
        DsmEntryService.DebtorChanged -= OnDataChanged;
        DsmEntryService.PayrollChanged -= OnDataChanged;
        DsmEntryService.InventoryChanged -= OnDataChanged;
        GC.SuppressFinalize(this);
    }

    partial void OnSelectedMonthChanged(DateTime value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            try { var syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>(); await syncEngine.ForceSyncAsync(); } catch { }

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

            var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(startDate, endDate);
            var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();

            var expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
            var shiftExpensesResult = await expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
            var shiftExpenses = shiftExpensesResult.Success && shiftExpensesResult.Data != null ? shiftExpensesResult.Data : new List<Expense>();

            var repaymentsResult = await _repaymentRepo.GetByDateRangeAsync(startDate, endDate);
            var repayments = repaymentsResult.Success && repaymentsResult.Data != null ? repaymentsResult.Data : new List<CreditorRepayment>();

            var settingsResult = await _settingsRepo.GetSettingsAsync();
            var settings = settingsResult.Success && settingsResult.Data != null ? settingsResult.Data : new Setting();
            double defaultHsd = settings.HsdRate;
            double defaultMsI = settings.MsIRate;
            double defaultMsII = settings.MsIIRate;
            double defaultCng = settings.CngRate;
            string stationName = settings.StationDisplayName;

            double totalNetSales = 0;

            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                var dayGroupEntries = entries.Where(e => (e.Shift != null ? e.Shift.ShiftDate.Date : DateTime.Today) == date).ToList();
                if (dayGroupEntries.Count == 0) continue;

                var dayExpenses = shiftExpenses.Where(e => (e.Shift != null ? e.Shift.ShiftDate.Date : DateTime.Today) == date).ToList();
                var dayRepayments = repayments.Where(r => r.RepaymentDate.Date == date).ToList();

                var dayReport = _reportService.CalculateDayReport(
                    date, date,
                    dayGroupEntries,
                    dayExpenses,
                    dayRepayments,
                    defaultHsd, defaultMsI, defaultMsII, defaultCng,
                    stationName);

                double dayNetSale = dayReport.TotalFuelAmount + dayReport.OtherCashTotal + dayReport.OilDefSalesTotal - dayReport.DsmSummaryTotals.Testing;
                double dayCollection = dayReport.ActualCollection;

                DayRows.Add(new MonthDayRow
                {
                    Date = date,
                    DsmEntryCount = dayGroupEntries.Count,
                    TotalSale = dayNetSale,
                    TotalLitres = dayReport.TotalFuelLitres,
                    TotalCollection = dayCollection,
                    Mismatch = dayReport.Difference
                });

                totalNetSales += dayNetSale;
                MonthTotalLitres += dayReport.TotalFuelLitres;
                MonthTotalCollection += dayCollection;
            }

            MonthTotalSale = totalNetSales;
            MonthAvgDailySale = DayRows.Count > 0 ? MonthTotalSale / DayRows.Count : 0;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load Monthly Performance data");
            MessageBox.Show($"Failed to load monthly performance: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void Print()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Sale", Value = "₹" + MonthTotalSale.ToString("N2"), Highlight = true },
                new() { Label = "Total Volume", Value = MonthTotalLitres.ToString("N2") + " L", Highlight = false },
                new() { Label = "Total Collection", Value = "₹" + MonthTotalCollection.ToString("N2"), Highlight = false },
                new() { Label = "Avg Daily Sale", Value = "₹" + MonthAvgDailySale.ToString("N2"), Highlight = false },
                new() { Label = "Total Entries", Value = MonthTotalEntries.ToString(), Highlight = false }
            };

            var headers = new List<string> { "Date", "Day", "DSM Entries", "Total Sales", "Volume Sold (L)", "Collection", "Mismatch" };
            var rows = new List<List<string>>();

            foreach (var row in DayRows)
            {
                rows.Add(new List<string>
                {
                    row.DateDisplay,
                    row.DayName,
                    row.DsmEntryCount.ToString(),
                    "₹" + row.TotalSale.ToString("N2"),
                    row.TotalLitres.ToString("N2") + " L",
                    "₹" + row.TotalCollection.ToString("N2"),
                    "₹" + row.Mismatch.ToString("N2")
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Monthly Performance Statement",
                Subtitle = $"Selected Month: {SelectedMonth:MMMM yyyy}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            _printService.PrintGenericGrid(printData);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print Monthly Performance report");
            MessageBox.Show($"Print failed: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Sales", Value = "₹" + MonthTotalSale.ToString("N2"), Highlight = true },
                new() { Label = "Total Volume", Value = MonthTotalLitres.ToString("N2") + " L", Highlight = false },
                new() { Label = "Total Collection", Value = "₹" + MonthTotalCollection.ToString("N2"), Highlight = false },
                new() { Label = "Avg Daily Sale", Value = "₹" + MonthAvgDailySale.ToString("N2"), Highlight = false },
                new() { Label = "Total Entries", Value = MonthTotalEntries.ToString(), Highlight = false }
            };

            var headers = new List<string> { "Date", "Day", "DSM Entries", "Total Sales", "Volume Sold (L)", "Collection", "Mismatch" };
            var rows = new List<List<string>>();

            foreach (var row in DayRows)
            {
                rows.Add(new List<string>
                {
                    row.DateDisplay,
                    row.DayName,
                    row.DsmEntryCount.ToString(),
                    "₹" + row.TotalSale.ToString("N2"),
                    row.TotalLitres.ToString("N2"),
                    "₹" + row.TotalCollection.ToString("N2"),
                    "₹" + row.Mismatch.ToString("N2")
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Monthly Performance Statement",
                Subtitle = $"Selected Month: {SelectedMonth:MMMM yyyy}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var path = await _excelExportService.ExportGenericGridAsync(printData, "MonthlyPerformance");
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to export Monthly Performance to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
