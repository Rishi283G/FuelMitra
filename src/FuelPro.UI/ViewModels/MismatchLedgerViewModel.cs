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

public partial class MismatchLedgerViewModel : ObservableObject
{
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IDsmCalculationService _calcService;
    private readonly PrintService _printService;
    private readonly ExcelExportService _excelExportService;

    [ObservableProperty] private DateTime _startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNetMismatchNegative))]
    private double _netMismatch;

    public bool IsNetMismatchNegative => NetMismatch < -0.01;

    public ObservableCollection<MismatchLedgerRow> MismatchRows { get; } = new();

    private List<MismatchLedgerRow> _allLoadedRows = new();

    public MismatchLedgerViewModel()
    {
        _dsmEntryRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _calcService = App.Services.GetRequiredService<IDsmCalculationService>();
        _printService = App.Services.GetRequiredService<PrintService>();
        _excelExportService = App.Services.GetRequiredService<ExcelExportService>();
        _ = LoadAsync();
    }

    partial void OnStartDateChanged(DateTime value) => _ = LoadAsync();
    partial void OnEndDateChanged(DateTime value) => _ = LoadAsync();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var entriesResult = await _dsmEntryRepo.GetEntriesForDateRangeAsync(StartDate, EndDate);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            var rows = new List<MismatchLedgerRow>();

            foreach (var entry in entries)
            {
                var cash1 = entry.CashDenominations.Where(x => x.CashType == "Cash1").Sum(x => x.TotalAmount);
                var cash2 = entry.CashDenominations.Where(x => x.CashType == "Cash2").Sum(x => x.TotalAmount);

                var cashDeposit = cash1 > 0 ? cash1 : (entry.PaymentCollection?.CashDeposit ?? 0);
                var cashInHand = cash2;

                var pc = entry.PaymentCollection;
                var digital = (pc?.PhonePe ?? 0) + (pc?.PhonePeMorning ?? 0) + (pc?.PhonePeDay ?? 0) + (pc?.PhonePeNight ?? 0)
                            + (pc?.PhonePeCard ?? 0) + (pc?.PhonePeCardMorning ?? 0) + (pc?.PhonePeCardDay ?? 0) + (pc?.PhonePeCardNight ?? 0)
                            + (pc?.CreditCardMorning ?? 0) + (pc?.CreditCardDay ?? 0) + (pc?.CreditCardNight ?? 0)
                            + (pc?.PetroCard ?? 0) + (pc?.PetroCardMorning ?? 0) + (pc?.PetroCardDay ?? 0) + (pc?.PetroCardNight ?? 0)
                            + (pc?.Others ?? 0);

                var totalDebit = entry.DebitEntries.Sum(d => d.Amount);
                var totalExpenses = entry.Expenses.Sum(e => e.Amount) + (entry.KhandharePetroleumEntries != null ? entry.KhandharePetroleumEntries.Sum(kp => kp.Amount) : 0);
                var totalTesting = entry.TestingEntries.Sum(t => t.Amount);

                var grossSales = entry.NozzleReadings != null && entry.NozzleReadings.Count > 0
                    ? entry.NozzleReadings.Sum(n => (double)n.Amount)
                    : (double)entry.GrossSales;

                var totalCollection = cashDeposit + cashInHand + digital + totalDebit + totalExpenses + totalTesting;
                var mismatch = totalCollection - grossSales;

                var status = "Balanced";
                if (mismatch < -0.01) status = "Short";
                else if (mismatch > 0.01) status = "Excess";

                if (entry.ReconciledToPumpId.HasValue)
                {
                    status = "Reconciled";
                }

                rows.Add(new MismatchLedgerRow
                {
                    DsmEntryId = entry.DsmEntryId,
                    Date = entry.Shift?.ShiftDate ?? DateTime.Today,
                    ShiftType = entry.Shift?.ShiftType ?? "—",
                    DsmName = entry.DsmName ?? "Unknown",
                    SalesAmount = grossSales,
                    CollectionAmount = totalCollection,
                    MismatchAmount = mismatch,
                    Status = status
                });
            }

            _allLoadedRows = rows.OrderByDescending(r => r.Date).ThenBy(r => r.ShiftType).ToList();
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        MismatchRows.Clear();

        var query = _allLoadedRows.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(r => r.DsmName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || 
                                     r.Status.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        var filtered = query.ToList();
        foreach (var item in filtered)
        {
            MismatchRows.Add(item);
        }

        NetMismatch = filtered.Sum(r => r.MismatchAmount);
    }

    [RelayCommand]
    private void Print()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Net Position Mismatch", Value = "₹" + NetMismatch.ToString("N2"), Highlight = true }
            };

            var headers = new List<string> { "Date", "Shift", "DSM Name", "Gross Sales", "Total Collection", "Mismatch Amount", "Status" };
            var rows = new List<List<string>>();

            foreach (var row in MismatchRows)
            {
                rows.Add(new List<string>
                {
                    row.Date.ToString("dd-MMM-yyyy"),
                    row.ShiftType,
                    row.DsmName,
                    "₹" + row.SalesAmount.ToString("N2"),
                    "₹" + row.CollectionAmount.ToString("N2"),
                    "₹" + row.MismatchAmount.ToString("N2"),
                    row.Status
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Mismatch Ledger Statement",
                Subtitle = $"Date Range: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}" + 
                           (string.IsNullOrWhiteSpace(SearchText) ? "" : $" (Filtered by: '{SearchText}')"),
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            _printService.PrintGenericGrid(printData);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print Mismatch Ledger report");
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
                new() { Label = "Net Position Mismatch", Value = "₹" + NetMismatch.ToString("N2"), Highlight = true }
            };

            var headers = new List<string> { "Date", "Shift", "DSM Name", "Gross Sales", "Total Collection", "Mismatch Amount", "Status" };
            var rows = new List<List<string>>();

            foreach (var row in MismatchRows)
            {
                rows.Add(new List<string>
                {
                    row.Date.ToString("dd-MMM-yyyy"),
                    row.ShiftType,
                    row.DsmName,
                    "₹" + row.SalesAmount.ToString("N2"),
                    "₹" + row.CollectionAmount.ToString("N2"),
                    "₹" + row.MismatchAmount.ToString("N2"),
                    row.Status
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Mismatch Ledger Statement",
                Subtitle = $"Date Range: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}" + 
                           (string.IsNullOrWhiteSpace(SearchText) ? "" : $" (Filtered by: '{SearchText}')"),
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var path = await _excelExportService.ExportGenericGridAsync(printData, "MismatchLedger");
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to export Mismatch Ledger to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public class MismatchLedgerRow
{
    public int DsmEntryId { get; set; }
    public DateTime Date { get; set; }
    public string ShiftType { get; set; } = "";
    public string DsmName { get; set; } = "";
    public double SalesAmount { get; set; }
    public double CollectionAmount { get; set; }
    public double MismatchAmount { get; set; }
    public string Status { get; set; } = "";
}
