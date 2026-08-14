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

            var headers = new List<string> { "DSM Name", "Shifts", "Sale", "Litres", "Collection", "Mismatch" };
            var rows = new List<List<string>>();

            foreach (var row in DsmBreakdown)
            {
                rows.Add(new List<string>
                {
                    row.DsmName,
                    row.ShiftCount.ToString(),
                    "₹" + row.TotalSale.ToString("N2"),
                    row.TotalLitres.ToString("N2") + " L",
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

            var headers = new List<string> { "DSM Name", "Shifts", "Sale", "Litres", "Collection", "Mismatch" };
            var rows = new List<List<string>>();

            foreach (var row in DsmBreakdown)
            {
                rows.Add(new List<string>
                {
                    row.DsmName,
                    row.ShiftCount.ToString(),
                    "₹" + row.TotalSale.ToString("N2"),
                    row.TotalLitres.ToString("N2") + " L",
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

            TotalSale = dayReport.TotalFuelAmount + dayReport.OtherCashTotal + dayReport.OilDefSalesTotal - dayReport.DsmSummaryTotals.Testing;
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

            // Group by DSM to populate breakdown rows
            var dsmGroups = entries.GroupBy(e => e.DsmName ?? "Unknown");
            foreach (var group in dsmGroups)
            {
                double groupSale = 0, groupLitres = 0, groupCollection = 0;

                foreach (var entry in group)
                {
                    var cash1 = entry.CashDenominations.Where(x => x.CashType == "Cash1").Sum(x => x.TotalAmount);
                    var cash2 = entry.CashDenominations.Where(x => x.CashType == "Cash2").Sum(x => x.TotalAmount);

                    var calc = _calcService.Calculate(new DsmEntryDto
                    {
                        DSMEntryId = entry.DsmEntryId,
                        NozzleReadings = entry.NozzleReadings.Select(r => new NozzleReadingDto { Amount = (decimal)r.Amount }).ToList(),
                        PaymentCollection = new PaymentCollectionDto
                        {
                            PhonePe = (decimal)((entry.PaymentCollection?.PhonePe ?? 0) + (entry.PaymentCollection?.PhonePeCardMorning ?? 0) + (entry.PaymentCollection?.PhonePeCardNight ?? 0)),
                            CreditCard = (decimal)((entry.PaymentCollection?.CreditCard ?? 0) + (entry.PaymentCollection?.PetroCard ?? 0)),
                            CashDeposit = (decimal)(cash1 + cash2 + (entry.PaymentCollection?.CashDeposit ?? 0)),
                            PhysicalCash = 0
                        },
                        DebitEntries = entry.DebitEntries.Select(d => new DebitEntryDto { Amount = (decimal)d.Amount }).ToList(),
                        TestingEntries = entry.TestingEntries.Select(t => new TestingEntryDto { FuelType = t.FuelType, Amount = (decimal)t.Amount }).ToList(),
                        Expenses = entry.Expenses.Select(e => new ExpenseDto { Amount = (decimal)e.Amount }).ToList()
                    });

                    groupSale += (double)calc.GrossSales;
                    groupCollection += (double)calc.TotalCollection;

                    foreach (var nr in entry.NozzleReadings)
                    {
                        groupLitres += nr.SaleLitres;
                    }
                }

                DsmBreakdown.Add(new DsmDailyRow
                {
                    DsmName = group.Key,
                    ShiftCount = group.Count(),
                    TotalSale = groupSale,
                    TotalLitres = groupLitres,
                    TotalCollection = groupCollection,
                    Mismatch = groupCollection - groupSale
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
    public double TotalCollection { get; set; }
    public double Mismatch { get; set; }
}
