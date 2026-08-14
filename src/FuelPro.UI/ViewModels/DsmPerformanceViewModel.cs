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
using FuelPro.Data;
using Microsoft.EntityFrameworkCore;

namespace FuelPro.UI.ViewModels;

/// <summary>
/// DSM-wise performance with date range presets, filter options, and search capabilities.
/// </summary>
public partial class DsmPerformanceViewModel : ObservableObject
{
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IDsmCalculationService _calcService;
    private readonly IOwnerCalculationService _ownerCalcService;
    private readonly PrintService _printService;
    private readonly ExcelExportService _excelExportService;

    [ObservableProperty] private DateTime _startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private string _selectedPreset = "Monthly";
    [ObservableProperty] private bool _isLoading;

    // Search and filter properties
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedDsmFilter = "All DSMs";
    public ObservableCollection<string> DsmFilterOptions { get; } = new() { "All DSMs" };

    public ObservableCollection<DsmMonthlyRow> DsmRows { get; } = new();
    private readonly List<DsmMonthlyRow> _allDsmRows = new();

    [ObservableProperty] private double _totalSale;
    [ObservableProperty] private double _totalLitres;
    [ObservableProperty] private double _totalMismatch;
    [ObservableProperty] private int _totalShifts;

    public bool IsCustomRange => SelectedPreset == "Custom";

    public DsmPerformanceViewModel()
    {
        _dsmEntryRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _calcService = App.Services.GetRequiredService<IDsmCalculationService>();
        _ownerCalcService = App.Services.GetRequiredService<IOwnerCalculationService>();
        _printService = App.Services.GetRequiredService<PrintService>();
        _excelExportService = App.Services.GetRequiredService<ExcelExportService>();
        _ = LoadAsync();
    }

    partial void OnSelectedPresetChanged(string value) => OnPropertyChanged(nameof(IsCustomRange));

    partial void OnStartDateChanged(DateTime value)
    {
        if (SelectedPreset == "Custom") _ = LoadAsync();
    }

    partial void OnEndDateChanged(DateTime value)
    {
        if (SelectedPreset == "Custom") _ = LoadAsync();
    }

    partial void OnSearchTextChanged(string value) => FilterAndCalculate();
    partial void OnSelectedDsmFilterChanged(string value) => FilterAndCalculate();

    [RelayCommand]
    private void SetPreset(string preset)
    {
        SelectedPreset = preset;
        switch (preset)
        {
            case "Daily":
                StartDate = DateTime.Today;
                EndDate = DateTime.Today;
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
                // Don't change dates, let the user change
                break;
        }
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var entriesResult = await _dsmEntryRepo.GetEntriesForDateRangeAsync(StartDate, EndDate);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            _allDsmRows.Clear();

            var byDsm = entries.GroupBy(e => e.DsmName ?? "Unknown");
            foreach (var dsmGroup in byDsm.OrderBy(g => g.Key))
            {
                int shortCount = 0;
                double totalShort = 0;
                double highestShort = 0;
                int mismatchCount = 0;

                foreach (var entry in dsmGroup)
                {
                    var entryResult = _ownerCalcService.Calculate(new[] { entry }, Array.Empty<Expense>(), Array.Empty<ShiftOtherCash>());
                    var mismatch = entryResult.Mismatch;

                    if (Math.Abs(mismatch) > 0.01)
                    {
                        mismatchCount++;
                    }

                    if (mismatch < -0.01)
                    {
                        shortCount++;
                        var shortAmt = Math.Abs(mismatch);
                        totalShort += shortAmt;
                        if (shortAmt > highestShort)
                        {
                            highestShort = shortAmt;
                        }
                    }
                }

                var dsmResult = _ownerCalcService.Calculate(dsmGroup, Array.Empty<Expense>(), Array.Empty<ShiftOtherCash>());

                _allDsmRows.Add(new DsmMonthlyRow
                {
                    DsmName = dsmGroup.Key,
                    ShiftCount = dsmGroup.Count(),
                    TotalSale = dsmResult.GrossSales,
                    TotalLitres = dsmResult.TotalLitres,
                    TotalCollection = dsmResult.AdjustedCollection,
                    Mismatch = dsmResult.Mismatch,
                    ShortCount = shortCount,
                    TotalShortAmount = totalShort,
                    HighestShort = highestShort,
                    MismatchCount = mismatchCount,
                    CashDeposit = dsmResult.CashDeposit,
                    CashInHand = dsmResult.CashInHand,
                    PhonePe = dsmResult.PhonePeDirect,
                    PhonePeCard = dsmResult.PhonePeCard,
                    CreditCard = dsmResult.CreditCard,
                    PetroCard = dsmResult.PetroCard,
                    Debit = dsmResult.Debit
                });
            }

            // Update DSM Filter Options
            var currentSelected = SelectedDsmFilter;
            
            DsmFilterOptions.Clear();
            DsmFilterOptions.Add("All DSMs");
            
            var distinctNames = entries
                .Select(e => e.DsmName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct()
                .OrderBy(name => name);

            foreach (var name in distinctNames)
            {
                DsmFilterOptions.Add(name);
            }

            if (DsmFilterOptions.Contains(currentSelected))
            {
                SelectedDsmFilter = currentSelected;
            }
            else
            {
                SelectedDsmFilter = "All DSMs";
            }

            FilterAndCalculate();
            await LoadPersonalSummariesAsync();
        }
        finally { IsLoading = false; }
    }

    private void FilterAndCalculate()
    {
        DsmRows.Clear();
        TotalSale = 0;
        TotalLitres = 0;
        TotalMismatch = 0;
        TotalShifts = 0;

        var filtered = _allDsmRows.AsEnumerable();

        if (!string.IsNullOrEmpty(SelectedDsmFilter) && SelectedDsmFilter != "All DSMs")
        {
            filtered = filtered.Where(r => string.Equals(r.DsmName, SelectedDsmFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(SearchText))
        {
            filtered = filtered.Where(r => r.DsmName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var row in filtered)
        {
            DsmRows.Add(row);
            TotalSale += row.TotalSale;
            TotalLitres += row.TotalLitres;
            TotalMismatch += row.Mismatch;
            TotalShifts += row.ShiftCount;
        }
    }

    [RelayCommand]
    private void Print()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Sales", Value = "₹" + TotalSale.ToString("N2") },
                new() { Label = "Total Volume", Value = TotalLitres.ToString("N2") + " L" },
                new() { Label = "Total Mismatch", Value = "₹" + TotalMismatch.ToString("N2"), Highlight = Math.Abs(TotalMismatch) > 1 },
                new() { Label = "Total Shifts", Value = TotalShifts.ToString() }
            };

            var headers = new List<string> { "DSM Name", "Shifts Worked", "Total Sales", "Total Collection", "Total Short", "Avg Short", "Highest Short", "Accuracy", "Mismatch Count" };
            var rows = new List<List<string>>();

            foreach (var r in DsmRows)
            {
                rows.Add(new List<string>
                {
                    r.DsmName,
                    r.ShiftCount.ToString(),
                    "₹" + r.TotalSale.ToString("N2"),
                    "₹" + r.TotalCollection.ToString("N2"),
                    "₹" + r.TotalShortAmount.ToString("N2"),
                    "₹" + r.AverageShort.ToString("N2"),
                    "₹" + r.HighestShort.ToString("N2"),
                    r.CollectionAccuracy.ToString("F2") + "%",
                    r.MismatchCount.ToString()
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "DSM Performance Report",
                Subtitle = $"Period: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}" + 
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
            Serilog.Log.Error(ex, "Failed to print DSM performance");
            MessageBox.Show($"Failed to print report: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Sales", Value = "₹" + TotalSale.ToString("N2"), Highlight = true },
                new() { Label = "Total Volume", Value = TotalLitres.ToString("N2") + " L", Highlight = false },
                new() { Label = "Total Collection", Value = "₹" + TotalMismatch.ToString("N2"), Highlight = false },
                new() { Label = "Total Shifts", Value = TotalShifts.ToString(), Highlight = false }
            };

            var headers = new List<string> { "DSM Name", "Shifts Worked", "Total Sales", "Total Collection", "Total Short", "Avg Short", "Highest Short", "Accuracy", "Mismatch Count" };
            var rows = new List<List<string>>();

            foreach (var r in DsmRows)
            {
                rows.Add(new List<string>
                {
                    r.DsmName,
                    r.ShiftCount.ToString(),
                    "₹" + r.TotalSale.ToString("N2"),
                    "₹" + r.TotalCollection.ToString("N2"),
                    "₹" + r.TotalShortAmount.ToString("N2"),
                    "₹" + r.AverageShort.ToString("N2"),
                    "₹" + r.HighestShort.ToString("N2"),
                    r.CollectionAccuracy.ToString("F2") + "%",
                    r.MismatchCount.ToString()
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "DSM Performance Report",
                Subtitle = $"Period: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}" + 
                           (string.IsNullOrWhiteSpace(SearchText) ? "" : $" (Filtered by: '{SearchText}')"),
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var path = await _excelExportService.ExportGenericGridAsync(printData, "DsmPerformance");
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to export DSM performance to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // --- Personal Debtors Dashboard (Owner side) ---
    [ObservableProperty] private ObservableCollection<DsmPersonalDebtorSummaryRow> _personalDsmSummaries = new();
    [ObservableProperty] private DsmPersonalDebtorSummaryRow? _selectedPersonalSummary;
    [ObservableProperty] private ObservableCollection<DsmPersonalDebtorLedgerRow> _personalLedger = new();
    [ObservableProperty] private DateTime _personalLedgerStartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _personalLedgerEndDate = DateTime.Today;
    [ObservableProperty] private double _personalLedgerTotalDebt;
    [ObservableProperty] private double _personalLedgerTotalRepayments;
    [ObservableProperty] private double _personalLedgerOutstandingBalance;
    [ObservableProperty] private string _personalLedgerStatus = "";

    partial void OnSelectedPersonalSummaryChanged(DsmPersonalDebtorSummaryRow? value)
    {
        _ = LoadPersonalLedgerAsync();
    }

    public async Task LoadPersonalSummariesAsync()
    {
        try
        {
            using var context = App.Services.GetRequiredService<FuelProDbContext>();
            var allDsms = await context.DsmUsers.Select(u => u.FullName).Distinct().ToListAsync();
            var debtorsList = await context.DsmPersonalDebtors.ToListAsync();
            var repaymentsList = await context.DsmPersonalDebtorRepayments.ToListAsync();

            var debtorsGrouped = debtorsList.GroupBy(d => d.DsmName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var summaryList = new List<DsmPersonalDebtorSummaryRow>();
            foreach (var dsm in allDsms)
            {
                double totalBorrowed = 0;
                double totalRepaid = 0;

                if (debtorsGrouped.TryGetValue(dsm, out var debits))
                {
                    totalBorrowed = debits.Sum(d => d.Amount);
                    var personalDebtorIds = debits.Select(d => d.Id).ToList();
                    totalRepaid = repaymentsList
                        .Where(r => personalDebtorIds.Contains(r.DsmPersonalDebtorId))
                        .Sum(r => r.Amount);
                }

                summaryList.Add(new DsmPersonalDebtorSummaryRow
                {
                    DsmName = dsm,
                    TotalBorrowed = totalBorrowed,
                    TotalRepaid = totalRepaid
                });
            }

            // Fallback for any DSM names in personal debtors not present in DsmUsers
            foreach (var dsm in debtorsGrouped.Keys)
            {
                if (!summaryList.Any(s => string.Equals(s.DsmName, dsm, StringComparison.OrdinalIgnoreCase)))
                {
                    var debits = debtorsGrouped[dsm];
                    var totalBorrowed = debits.Sum(d => d.Amount);
                    var personalDebtorIds = debits.Select(d => d.Id).ToList();
                    var totalRepaid = repaymentsList
                        .Where(r => personalDebtorIds.Contains(r.DsmPersonalDebtorId))
                        .Sum(r => r.Amount);

                    summaryList.Add(new DsmPersonalDebtorSummaryRow
                    {
                        DsmName = dsm,
                        TotalBorrowed = totalBorrowed,
                        TotalRepaid = totalRepaid
                    });
                }
            }

            var summary = summaryList.OrderBy(s => s.DsmName).ToList();

            PersonalDsmSummaries.Clear();
            foreach (var s in summary)
            {
                PersonalDsmSummaries.Add(s);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load owner personal summaries");
        }
    }

    [RelayCommand]
    public async Task LoadPersonalLedgerAsync()
    {
        PersonalLedger.Clear();
        if (SelectedPersonalSummary == null) return;

        try
        {
            using var context = App.Services.GetRequiredService<FuelProDbContext>();
            var dsmName = SelectedPersonalSummary.DsmName;

            var debits = await context.DsmPersonalDebtors
                .Where(d => d.DsmName == dsmName && d.Date.Date >= PersonalLedgerStartDate.Date && d.Date.Date <= PersonalLedgerEndDate.Date)
                .ToListAsync();

            var repayments = await context.DsmPersonalDebtorRepayments
                .Include(r => r.DsmPersonalDebtor)
                .Where(r => r.DsmPersonalDebtor != null && r.DsmPersonalDebtor.DsmName == dsmName && r.Date.Date >= PersonalLedgerStartDate.Date && r.Date.Date <= PersonalLedgerEndDate.Date)
                .ToListAsync();

            var list = new List<DsmPersonalDebtorLedgerRow>();

            foreach (var d in debits)
            {
                list.Add(new DsmPersonalDebtorLedgerRow
                {
                    TransactionId = d.Id,
                    TransactionType = "Borrow",
                    Date = d.Date,
                    Description = $"Borrowed: {d.FuelProduct} ({(string.IsNullOrEmpty(d.Remarks) ? "No remarks" : d.Remarks)})",
                    Debit = d.Amount,
                    Credit = 0,
                    FuelProduct = d.FuelProduct,
                    Remarks = d.Remarks,
                    PaymentMethod = d.PaymentMethod,
                    CardTid = d.CardTid,
                    CardBatch = d.CardBatch,
                    Amount = d.Amount,
                    CreatedAt = d.CreatedAt
                });
            }

            foreach (var c in repayments)
            {
                string tidBatchInfo = "";
                if (!string.IsNullOrWhiteSpace(c.CardTid) || !string.IsNullOrWhiteSpace(c.CardBatch))
                {
                    var details = new List<string>();
                    if (!string.IsNullOrWhiteSpace(c.CardTid)) details.Add($"TID: {c.CardTid}");
                    if (!string.IsNullOrWhiteSpace(c.CardBatch)) details.Add($"Batch: {c.CardBatch}");
                    tidBatchInfo = $" ({string.Join(", ", details)})";
                }

                list.Add(new DsmPersonalDebtorLedgerRow
                {
                    TransactionId = c.Id,
                    TransactionType = "Repayment",
                    Date = c.Date,
                    Description = $"Repayment via {c.PaymentMethod}{tidBatchInfo}",
                    Debit = 0,
                    Credit = c.Amount,
                    PaymentMethod = c.PaymentMethod,
                    CardTid = c.CardTid,
                    CardBatch = c.CardBatch,
                    Amount = c.Amount,
                    Remarks = c.DsmPersonalDebtor?.Remarks,
                    CreatedAt = c.CreatedAt
                });
            }

            var sorted = list.OrderBy(x => x.Date).ThenBy(x => x.TransactionType == "Borrow" ? 0 : 1).ToList();

            double running = 0;
            foreach (var item in sorted)
            {
                running += (item.Debit - item.Credit);
                item.RunningBalance = running;
            }

            foreach (var item in sorted)
            {
                PersonalLedger.Add(item);
            }

            PersonalLedgerTotalDebt = sorted.Sum(x => x.Debit);
            PersonalLedgerTotalRepayments = sorted.Sum(x => x.Credit);
            PersonalLedgerOutstandingBalance = running;

            PersonalLedgerStatus = sorted.Count > 0 ? $"Loaded {sorted.Count} transactions." : "No transactions found in date range.";
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load owner personal ledger");
            PersonalLedgerStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void PrintPersonalLedger()
    {
        if (SelectedPersonalSummary == null) return;
        if (PersonalLedger == null || PersonalLedger.Count == 0) return;

        try
        {
            var printRows = PersonalLedger.Select(t => new DebtorLedgerPrintRow
            {
                Date = t.Date.ToString("dd-MMM-yyyy"),
                Description = t.Description,
                Debit = t.Debit,
                Credit = t.Credit,
                RunningBalance = t.RunningBalance
            }).ToList();

            var printData = new DebtorLedgerPrintData
            {
                DebtorName = $"{SelectedPersonalSummary.DsmName} (DSM Personal Debtors)",
                DebtorPhone = "N/A",
                StartDate = PersonalLedgerStartDate.ToString("dd-MMM-yyyy"),
                EndDate = PersonalLedgerEndDate.ToString("dd-MMM-yyyy"),
                OpeningBalance = PersonalLedger.FirstOrDefault()?.RunningBalance ?? 0,
                TotalDebt = PersonalLedgerTotalDebt,
                TotalRepayments = PersonalLedgerTotalRepayments,
                ClosingBalance = PersonalLedgerOutstandingBalance,
                Transactions = printRows
            };

            _printService.PrintDebtorLedger(printData);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print personal ledger");
        }
    }
}

public class DsmMonthlyRow
{
    public string DsmName { get; set; } = "";
    public int ShiftCount { get; set; }
    public double TotalSale { get; set; }
    public double TotalLitres { get; set; }
    public double TotalCollection { get; set; }
    public double Mismatch { get; set; }
    public int ShortCount { get; set; }
    public double TotalShortAmount { get; set; }
    public double AverageShort => ShiftCount > 0 ? TotalShortAmount / ShiftCount : 0;
    public double HighestShort { get; set; }
    public double CollectionAccuracy => TotalSale > 0 ? (TotalCollection / TotalSale) * 100 : 0;
    public int MismatchCount { get; set; }

    // Detailed collections split
    public double CashDeposit { get; set; }
    public double CashInHand { get; set; }
    public double PhonePe { get; set; }
    public double PhonePeCard { get; set; }
    public double CreditCard { get; set; }
    public double PetroCard { get; set; }
    public double Debit { get; set; }
    public double TotalCash => CashDeposit + CashInHand;
}
