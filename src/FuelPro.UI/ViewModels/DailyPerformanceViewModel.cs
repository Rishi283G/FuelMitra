using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using FuelPro.UI.Printing;

namespace FuelPro.UI.ViewModels;

/// <summary>
/// Daily drill-down: shift-wise breakdown, fuel-wise sales, collection summary, DSM-wise details.
/// </summary>
public partial class DailyPerformanceViewModel : ObservableObject, IDisposable
{
    private readonly IShiftRepository _shiftRepo;
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IDsmCalculationService _calcService;
    private readonly IExpenseRepository _expenseRepo;
    private readonly PrintService _printService;
    private readonly ExcelExportService _excelExportService;
    private readonly IReportService _reportService;
    private readonly ICreditorRepaymentRepository _repaymentRepo;
    private readonly ISettingsRepository _settingsRepo;

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private bool _isLoading;

    // Day totals
    [ObservableProperty] private double _totalSale;
    [ObservableProperty] private double _totalLitres;
    [ObservableProperty] private double _totalCollection;
    [ObservableProperty] private double _totalExpenses;
    [ObservableProperty] private double _totalMismatch;
    [ObservableProperty] private double _totalHsd;
    [ObservableProperty] private double _totalMsI;
    [ObservableProperty] private double _totalMsII;
    [ObservableProperty] private double _oilDefSalesTotal;
    [ObservableProperty] private double _oilDefProfitTotal;
    [ObservableProperty] private double _totalDsmShort;

    // DSM-wise breakdown
    public ObservableCollection<DsmDailyRow> DsmBreakdown { get; } = new();

    public DailyPerformanceViewModel()
    {
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _dsmEntryRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _calcService = App.Services.GetRequiredService<IDsmCalculationService>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
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

    partial void OnSelectedDateChanged(DateTime value) => _ = LoadAsync();

    [RelayCommand]
    private void Print()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Sale", Value = "₹" + TotalSale.ToString("N2"), Highlight = true },
                new() { Label = "Total Volume", Value = TotalLitres.ToString("N2") + " L", Highlight = false },
                new() { Label = "Total Collection", Value = "₹" + TotalCollection.ToString("N2"), Highlight = false },
                new() { Label = "Total Mismatch", Value = "₹" + TotalMismatch.ToString("N2"), Highlight = false }
            };

            var headers = new List<string> { "DSM Name", "Shifts", "Sale", "Total Litres", "Expenses", "Diesel (L)", "Petrol (L)", "Speed (L)", "CNG (Kg)", "Collection", "Mismatch" };
            var rows = new List<List<string>>();

            foreach (var row in DsmBreakdown)
            {
                rows.Add(new List<string>
                {
                    row.DsmName,
                    row.ShiftCount.ToString(),
                    "₹" + row.TotalSale.ToString("N2"),
                    row.TotalLitres.ToString("N2") + " L",
                    "₹" + row.Expenses.ToString("N2"),
                    row.DieselLitres.ToString("N2"),
                    row.PetrolLitres.ToString("N2"),
                    row.SpeedLitres.ToString("N2"),
                    row.CngKg.ToString("N2"),
                    "₹" + row.TotalCollection.ToString("N2"),
                    "₹" + row.Mismatch.ToString("N2")
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Daily Performance Report",
                Subtitle = $"Date: {SelectedDate:dd-MMM-yyyy}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            _printService.PrintGenericGrid(printData);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print daily performance report");
            System.Windows.MessageBox.Show($"Print failed: {ex.Message}", "Print Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Sale", Value = "₹" + TotalSale.ToString("N2"), Highlight = true },
                new() { Label = "Total Volume", Value = TotalLitres.ToString("N2") + " L", Highlight = false },
                new() { Label = "Total Collection", Value = "₹" + TotalCollection.ToString("N2"), Highlight = false },
                new() { Label = "Total Mismatch", Value = "₹" + TotalMismatch.ToString("N2"), Highlight = false }
            };

            var headers = new List<string> { "DSM Name", "Shifts", "Sale", "Total Litres", "Expenses", "Diesel (L)", "Petrol (L)", "Speed (L)", "CNG (Kg)", "Collection", "Mismatch" };
            var rows = new List<List<string>>();

            foreach (var row in DsmBreakdown)
            {
                rows.Add(new List<string>
                {
                    row.DsmName,
                    row.ShiftCount.ToString(),
                    "₹" + row.TotalSale.ToString("N2"),
                    row.TotalLitres.ToString("N2") + " L",
                    "₹" + row.Expenses.ToString("N2"),
                    row.DieselLitres.ToString("N2"),
                    row.PetrolLitres.ToString("N2"),
                    row.SpeedLitres.ToString("N2"),
                    row.CngKg.ToString("N2"),
                    "₹" + row.TotalCollection.ToString("N2"),
                    "₹" + row.Mismatch.ToString("N2")
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Daily Performance Report",
                Subtitle = $"Date: {SelectedDate:dd-MMM-yyyy}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var path = await _excelExportService.ExportGenericGridAsync(printData, "DailyPerformance");
            System.Windows.MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to export daily performance report to Excel");
            System.Windows.MessageBox.Show($"Export failed: {ex.Message}", "Export Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            try { var syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>(); await syncEngine.ForceSyncAsync(); } catch { }

            var entriesResult = await _dsmEntryRepo.GetEntriesForDateRangeAsync(SelectedDate, SelectedDate);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(SelectedDate, SelectedDate);
            var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();

            var repaymentsResult = await _repaymentRepo.GetByDateRangeAsync(SelectedDate, SelectedDate);
            var repayments = repaymentsResult.Success && repaymentsResult.Data != null ? repaymentsResult.Data : new List<CreditorRepayment>();

            var shiftExpResult = await _expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
            var shiftExpenses = shiftExpResult.Success && shiftExpResult.Data != null ? shiftExpResult.Data : new List<Expense>();

            var settingsResult = await _settingsRepo.GetSettingsAsync();
            var settings = settingsResult.Success && settingsResult.Data != null ? settingsResult.Data : new Setting();
            double defaultHsd = settings.HsdRate;
            double defaultMsI = settings.MsIRate;
            double defaultMsII = settings.MsIIRate;
            double defaultCng = settings.CngRate;
            string stationName = settings.StationDisplayName;

            TotalSale = 0; TotalCollection = 0; TotalExpenses = 0;
            TotalHsd = 0; TotalMsI = 0; TotalMsII = 0;
            DsmBreakdown.Clear();

            // Run the centralized calculation
            var dayReport = _reportService.CalculateDayReport(
                SelectedDate, SelectedDate,
                entries,
                shiftExpenses,
                repayments,
                defaultHsd, defaultMsI, defaultMsII, defaultCng,
                stationName);

            TotalSale = dayReport.TotalFuelAmount + dayReport.OtherCashTotal + dayReport.OilDefSalesTotal;
            TotalLitres = dayReport.TotalFuelLitres;
            TotalCollection = dayReport.ActualCollection;
            TotalExpenses = dayReport.ExpensesTotal;
            TotalMismatch = dayReport.Difference;
            OilDefSalesTotal = dayReport.OilDefSalesTotal;
            TotalDsmShort = dayReport.TotalDsmShort;

            var finCalcService = App.Services.GetRequiredService<IFinancialCalculationService>();
            var finResult = await finCalcService.CalculateFinancialsAsync(SelectedDate, SelectedDate);
            OilDefProfitTotal = finResult.OilProfit.TotalProfit + finResult.DefProfit.TotalProfit;

            // Load liters breakdown
            foreach (var fRow in dayReport.FuelSales)
            {
                if (fRow.FuelType == "HSD") TotalHsd = fRow.Litres;
                else if (fRow.FuelType == "MS-I") TotalMsI = fRow.Litres;
                else if (fRow.FuelType == "MS-II") TotalMsII = fRow.Litres;
            }

            // Group by DSM to populate breakdown rows using centralized shift aggregation
            var aggregationService = App.Services.GetRequiredService<IShiftAggregationService>();
            var summaryRows = dayReport.DsmSummaryRows ?? aggregationService.BuildDsmSummaryRows(entries);
            var dsmTotals = dayReport.DsmShiftTotals ?? aggregationService.BuildDsmShiftTotals(summaryRows);

            foreach (var st in dsmTotals)
            {
                var dsmEntries = entries
                    .Where(e => string.Equals((e.DsmName ?? "").Trim(), st.DsmName.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var dsmNozzles = dsmEntries
                    .SelectMany(e => e.NozzleReadings ?? new List<NozzleReading>())
                    .ToList();

                double dieselLitres = dsmNozzles.Where(n => string.Equals(n.FuelType, "HSD", StringComparison.OrdinalIgnoreCase) || string.Equals(n.FuelType, "Diesel", StringComparison.OrdinalIgnoreCase)).Sum(n => n.SaleLitres);
                double petrolLitres = dsmNozzles.Where(n => string.Equals(n.FuelType, "MS-I", StringComparison.OrdinalIgnoreCase) || string.Equals(n.FuelType, "MS", StringComparison.OrdinalIgnoreCase) || string.Equals(n.FuelType, "Petrol", StringComparison.OrdinalIgnoreCase)).Sum(n => n.SaleLitres);
                double speedLitres = dsmNozzles.Where(n => string.Equals(n.FuelType, "MS-II", StringComparison.OrdinalIgnoreCase) || string.Equals(n.FuelType, "Speed", StringComparison.OrdinalIgnoreCase)).Sum(n => n.SaleLitres);
                double cngKg = dsmNozzles.Where(n => string.Equals(n.FuelType, "CNG", StringComparison.OrdinalIgnoreCase)).Sum(n => n.SaleLitres);
                double dsmLitres = dieselLitres + petrolLitres + speedLitres + cngKg;
                if (dsmLitres <= 0) dsmLitres = dsmNozzles.Sum(n => n.SaleLitres);

                double dsmExpenses = st.Expenses > 0 ? st.Expenses : dsmEntries.Sum(e => e.Expenses?.Sum(x => x.Amount) ?? 0);

                DsmBreakdown.Add(new DsmDailyRow
                {
                    DsmName = st.DsmName,
                    ShiftCount = st.SessionsCount,
                    TotalSale = st.GrossSales,
                    TotalLitres = dsmLitres,
                    Expenses = dsmExpenses,
                    DieselLitres = dieselLitres,
                    PetrolLitres = petrolLitres,
                    SpeedLitres = speedLitres,
                    CngKg = cngKg,
                    TotalCollection = st.TotalCollection,
                    Mismatch = st.Mismatch
                });
            }
        }
        finally { IsLoading = false; }
    }
}

public class DsmDailyRow
{
    public string DsmName { get; set; } = "";
    public int ShiftCount { get; set; }
    public double TotalSale { get; set; }
    public double TotalLitres { get; set; }
    public double Expenses { get; set; }
    public double DieselLitres { get; set; }
    public double PetrolLitres { get; set; }
    public double SpeedLitres { get; set; }
    public double CngKg { get; set; }
    public double TotalCollection { get; set; }
    public double Mismatch { get; set; }
}
