using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

/// <summary>
/// Collection summary: payment mode breakdown for selected date range.
/// </summary>
public partial class CollectionSummaryViewModel : ObservableObject, IDisposable
{
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IOwnerCalculationService _ownerCalcService;
    private readonly ITidCalculationService _tidService;
    private readonly PrintService _printService;
    private readonly ExcelExportService _excelExportService;
    private readonly IShiftRepository _shiftRepo;
    private readonly IExpenseRepository _expenseRepo;
    private readonly ICreditorRepaymentRepository _repaymentRepo;
    private readonly IReportService _reportService;
    private readonly ISettingsRepository _settingsRepo;

    [ObservableProperty] private DateTime _startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private bool _isLoading;

    // Payment mode totals
    [ObservableProperty] private double _totalCashDeposit;
    [ObservableProperty] private double _totalCashInHand;
    [ObservableProperty] private double _totalPhonePe;
    [ObservableProperty] private double _totalPhonePeCard;
    [ObservableProperty] private double _totalCreditCard;
    [ObservableProperty] private double _totalPetroCard;
    [ObservableProperty] private double _totalDebit;
    [ObservableProperty] private double _grandTotal;
    [ObservableProperty] private double _totalDigital;

    public ObservableCollection<CollectionDayRow> DayRows { get; } = new();

    public CollectionSummaryViewModel()
    {
        _dsmEntryRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _ownerCalcService = App.Services.GetRequiredService<IOwnerCalculationService>();
        _tidService = App.Services.GetRequiredService<ITidCalculationService>();
        _printService = App.Services.GetRequiredService<PrintService>();
        _excelExportService = App.Services.GetRequiredService<ExcelExportService>();
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _repaymentRepo = App.Services.GetRequiredService<ICreditorRepaymentRepository>();
        _reportService = App.Services.GetRequiredService<IReportService>();
        _settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();

        DsmEntryService.DsmEntryChanged += OnDataChanged;
        DsmEntryService.PettyCashChanged += OnDataChanged;
        DsmEntryService.DebtorChanged += OnDataChanged;

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
        GC.SuppressFinalize(this);
    }

    partial void OnStartDateChanged(DateTime value) => _ = LoadAsync();
    partial void OnEndDateChanged(DateTime value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var startDate = StartDate.Date;
            var endDate = EndDate.Date;

            var entriesResult = await _dsmEntryRepo.GetEntriesForDateRangeAsync(startDate, endDate);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(startDate, endDate);
            var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();

            var expensesResult = await _expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
            var expenses = expensesResult.Success && expensesResult.Data != null ? expensesResult.Data : new List<Expense>();

            var repaymentsResult = await _repaymentRepo.GetByDateRangeAsync(startDate, endDate);
            var repayments = repaymentsResult.Success && repaymentsResult.Data != null ? repaymentsResult.Data : new List<CreditorRepayment>();

            var settingsResult = await _settingsRepo.GetSettingsAsync();
            var settings = settingsResult.Success && settingsResult.Data != null ? settingsResult.Data : new Setting();
            double defaultHsd = settings.HsdRate;
            double defaultMsI = settings.MsIRate;
            double defaultMsII = settings.MsIIRate;
            double defaultCng = settings.CngRate;
            string stationName = settings.StationDisplayName;

            TotalCashDeposit = 0; TotalCashInHand = 0;
            TotalPhonePe = 0; TotalPhonePeCard = 0;
            TotalCreditCard = 0; TotalPetroCard = 0;
            TotalDebit = 0; GrandTotal = 0; TotalDigital = 0;
            DayRows.Clear();

            // Loop day by day from startDate to endDate
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                var dayEntries = entries.Where(e => (e.Shift != null ? e.Shift.ShiftDate.Date : DateTime.Today) == date).ToList();
                if (dayEntries.Count == 0) continue;

                var dayExpenses = expenses.Where(e => (e.Shift != null ? e.Shift.ShiftDate.Date : DateTime.Today) == date).ToList();
                var dayRepayments = repayments.Where(r => r.RepaymentDate.Date == date).ToList();

                var dayReport = _reportService.CalculateDayReport(
                    date, date,
                    dayEntries,
                    dayExpenses,
                    dayRepayments,
                    defaultHsd, defaultMsI, defaultMsII, defaultCng,
                    stationName);

                var cashDeposit = dayReport.Cash1.GrandTotal;
                var cashInHand = dayReport.Cash2.GrandTotal + dayReport.CashRepayments;
                var phonePe = dayReport.DsmSummaryTotals.PhonePe + dayReport.PhonePeRepayments;
                var phonePeCard = dayReport.DsmSummaryTotals.PhonePeCard;
                var creditCard = dayReport.DsmSummaryTotals.CreditCardMorning + dayReport.DsmSummaryTotals.CreditCardNight + dayReport.CreditCardRepayments;
                var petroCard = dayReport.DsmSummaryTotals.PetroCard + dayReport.PetroCardRepayments;
                var debit = dayReport.CreditorsTotal;

                DayRows.Add(new CollectionDayRow
                {
                    Date = date,
                    CashDeposit = cashDeposit,
                    CashInHand = cashInHand,
                    PhonePe = phonePe,
                    PhonePeCard = phonePeCard,
                    CreditCard = creditCard,
                    PetroCard = petroCard,
                    Debit = debit
                });

                TotalCashDeposit += cashDeposit;
                TotalCashInHand += cashInHand;
                TotalPhonePe += phonePe;
                TotalPhonePeCard += phonePeCard;
                TotalCreditCard += creditCard;
                TotalPetroCard += petroCard;
                TotalDebit += debit;
            }

            GrandTotal = TotalCashDeposit + TotalCashInHand + TotalPhonePe + TotalPhonePeCard
                         + TotalCreditCard + TotalPetroCard + TotalDebit;
            TotalDigital = TotalPhonePe + TotalPhonePeCard + TotalCreditCard + TotalPetroCard;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load Collection Summary data");
            MessageBox.Show($"Failed to load collection summary: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                new() { Label = "Grand Total Collection", Value = "₹" + GrandTotal.ToString("N2"), Highlight = true },
                new() { Label = "Total Bank Cash", Value = "₹" + TotalCashDeposit.ToString("N2"), Highlight = false },
                new() { Label = "Total Cash in Hand", Value = "₹" + TotalCashInHand.ToString("N2"), Highlight = false }
            };

            var headers = new List<string> { "Date", "Bank Cash", "Cash In Hand", "PhonePe", "PhonePe Card", "PineLabs Card", "Petro Card", "Debitors", "Day Total" };
            var rows = new List<List<string>>();

            foreach (var row in DayRows)
            {
                rows.Add(new List<string>
                {
                    row.DateDisplay,
                    "₹" + row.CashDeposit.ToString("N2"),
                    "₹" + row.CashInHand.ToString("N2"),
                    "₹" + row.PhonePe.ToString("N2"),
                    "₹" + row.PhonePeCard.ToString("N2"),
                    "₹" + row.CreditCard.ToString("N2"),
                    "₹" + row.PetroCard.ToString("N2"),
                    "₹" + row.Debit.ToString("N2"),
                    "₹" + row.DayTotal.ToString("N2")
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Collection Summary Statement",
                Subtitle = $"Date Range: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            _printService.PrintGenericGrid(printData);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print Collection Summary report");
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
                new() { Label = "Grand Total Collection", Value = "₹" + GrandTotal.ToString("N2"), Highlight = true },
                new() { Label = "Total Bank Cash", Value = "₹" + TotalCashDeposit.ToString("N2"), Highlight = false },
                new() { Label = "Total Cash in Hand", Value = "₹" + TotalCashInHand.ToString("N2"), Highlight = false }
            };

            var headers = new List<string> { "Date", "Bank Cash", "Cash In Hand", "PhonePe", "PhonePe Card", "PineLabs Card", "Petro Card", "Debitors", "Day Total" };
            var rows = new List<List<string>>();

            foreach (var row in DayRows)
            {
                rows.Add(new List<string>
                {
                    row.DateDisplay,
                    "₹" + row.CashDeposit.ToString("N2"),
                    "₹" + row.CashInHand.ToString("N2"),
                    "₹" + row.PhonePe.ToString("N2"),
                    "₹" + row.PhonePeCard.ToString("N2"),
                    "₹" + row.CreditCard.ToString("N2"),
                    "₹" + row.PetroCard.ToString("N2"),
                    "₹" + row.Debit.ToString("N2"),
                    "₹" + row.DayTotal.ToString("N2")
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Collection Summary Statement",
                Subtitle = $"Date Range: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var path = await _excelExportService.ExportGenericGridAsync(printData, "CollectionSummary");
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to export Collection Summary to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public class CollectionDayRow
{
    public DateTime Date { get; set; }
    public string DateDisplay => Date.ToString("dd MMM");
    public double CashDeposit { get; set; }
    public double CashInHand { get; set; }
    public double PhonePe { get; set; }
    public double PhonePeCard { get; set; }
    public double CreditCard { get; set; }
    public double PetroCard { get; set; }
    public double Debit { get; set; }
    public double DayTotal => CashDeposit + CashInHand + PhonePe + PhonePeCard + CreditCard + PetroCard + Debit;
}
