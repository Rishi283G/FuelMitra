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
/// Financial summary: cash flow overview — cash in hand, bank cash, digital, expenses, creditor repayments.
/// </summary>
public partial class FinancialSummaryViewModel : ObservableObject
{
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IExpenseRepository _expenseRepo;
    private readonly ICreditorRepaymentRepository _repaymentRepo;
    private readonly IShiftRepository _shiftRepo;
    private readonly PrintService _printService;
    private readonly ExcelExportService _excelExportService;

    [ObservableProperty] private DateTime _startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private bool _isLoading;

    // Cash position
    [ObservableProperty] private double _totalBankCash;
    [ObservableProperty] private double _totalCashInHand;
    [ObservableProperty] private double _totalDigitalPayments;
    [ObservableProperty] private double _totalExpenses;
    [ObservableProperty] private double _totalCreditorRepayments;
    [ObservableProperty] private double _totalDebitorsOutstanding;
    [ObservableProperty] private double _netCashPosition;
    [ObservableProperty] private double _overallOutstandingBalance;
    [ObservableProperty] private int _activeDebtorsCount;

    // Repayment details
    public ObservableCollection<CreditorRepayment> Repayments { get; } = new();
    public ObservableCollection<DebtorLogEntryDto> DebtorLogs { get; } = new();
    [ObservableProperty] private ObservableCollection<DebtorDisplayRow> _debtors = new();

    public FinancialSummaryViewModel()
    {
        _dsmEntryRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _repaymentRepo = App.Services.GetRequiredService<ICreditorRepaymentRepository>();
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _printService = App.Services.GetRequiredService<PrintService>();
        _excelExportService = App.Services.GetRequiredService<ExcelExportService>();
        _ = LoadAsync();
    }

    partial void OnStartDateChanged(DateTime value) => _ = LoadAsync();
    partial void OnEndDateChanged(DateTime value) => _ = LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var entriesResult = await _dsmEntryRepo.GetEntriesForDateRangeAsync(StartDate, EndDate);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(StartDate, EndDate);
            var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();

            TotalBankCash = 0; TotalCashInHand = 0; TotalDigitalPayments = 0; TotalExpenses = 0;
            TotalDebitorsOutstanding = 0;
            DebtorLogs.Clear();

            foreach (var entry in entries)
            {
                TotalBankCash += entry.CashDenominations.Where(x => x.CashType == "Cash1").Sum(x => x.TotalAmount)
                                 + (entry.PaymentCollection?.CashDeposit ?? 0);
                TotalCashInHand += entry.CashDenominations.Where(x => x.CashType == "Cash2").Sum(x => x.TotalAmount);

                TotalDigitalPayments += (entry.PaymentCollection?.PhonePe ?? 0)
                                        + (entry.PaymentCollection?.PhonePeCardMorning ?? 0)
                                        + (entry.PaymentCollection?.PhonePeCardNight ?? 0)
                                        + (entry.PaymentCollection?.CreditCard ?? 0)
                                        + (entry.PaymentCollection?.PetroCard ?? 0)
                                        + (entry.PaymentCollection?.DynamicItemsTotal ?? 0);

                TotalExpenses += entry.Expenses.Sum(e => e.Amount);
                TotalDebitorsOutstanding += entry.DebitEntries.Sum(d => d.Amount);

                foreach (var d in entry.DebitEntries)
                {
                    DebtorLogs.Add(new DebtorLogEntryDto
                    {
                        Date = entry.Shift?.ShiftDate ?? entry.CreatedAt,
                        DsmName = entry.DsmName,
                        DebtorName = d.DebtorName,
                        ChequeNo = d.ChequeNo,
                        Amount = d.Amount
                    });
                }
            }

            // Shift-level expenses
            var shiftExpResult = await _expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
            if (shiftExpResult.Success && shiftExpResult.Data != null)
                TotalExpenses += shiftExpResult.Data.Sum(e => e.Amount);

            // Creditor repayments
            var repResult = await _repaymentRepo.GetByDateRangeAsync(StartDate, EndDate);
            Repayments.Clear();
            if (repResult.Success && repResult.Data != null)
            {
                TotalCreditorRepayments = (double)repResult.Data.Sum(r => r.Amount);
                foreach (var r in repResult.Data)
                    Repayments.Add(r);
            }

            NetCashPosition = TotalBankCash + TotalCashInHand + TotalDigitalPayments - TotalExpenses;

            // Retrieve overall active debtors and overall outstanding balance
            var creditorRepo = App.Services.GetRequiredService<ICreditorRepository>();
            var creditorsRes = await creditorRepo.GetAllActiveAsync();
            var creditors = creditorsRes.Success ? creditorsRes.Data ?? new List<Creditor>() : new List<Creditor>();

            var allEntriesResult = await _dsmEntryRepo.GetEntriesForDateRangeAsync(new DateTime(2000, 1, 1), DateTime.Today.AddYears(5));
            var allDebits = allEntriesResult.Success && allEntriesResult.Data != null
                ? allEntriesResult.Data.SelectMany(e => e.DebitEntries).ToList()
                : new List<DebitEntry>();

            var debitsGrouped = allDebits
                .Where(d => !string.IsNullOrWhiteSpace(d.DebtorName))
                .GroupBy(d => d.DebtorName.Trim().ToLower())
                .ToDictionary(g => g.Key, g => g.Sum(d => d.Amount));

            var repaymentsResult = await _repaymentRepo.GetByDateRangeAsync(new DateTime(2000, 1, 1), DateTime.Today.AddYears(5));
            var allRepayments = repaymentsResult.Success ? repaymentsResult.Data ?? new List<CreditorRepayment>() : new List<CreditorRepayment>();

            var repaymentsGrouped = allRepayments
                .Where(r => !string.IsNullOrWhiteSpace(r.CreditorName))
                .GroupBy(r => r.CreditorName.Trim().ToLower())
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

            var allDebtorKeys = debitsGrouped.Keys
                .Concat(repaymentsGrouped.Keys)
                .Concat(creditors.Select(c => c.Name.Trim().ToLower()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var creditorsDict = creditors.ToDictionary(c => c.Name.Trim().ToLower(), c => c);

            double overallOutstanding = 0;
            int activeCount = 0;
            var rows = new List<DebtorDisplayRow>();

            foreach (var key in allDebtorKeys)
            {
                debitsGrouped.TryGetValue(key, out var totalDebt);
                repaymentsGrouped.TryGetValue(key, out var totalRepayment);
                var balance = totalDebt - totalRepayment;
                if (balance > 0.01)
                {
                    overallOutstanding += balance;
                    activeCount++;
                }

                creditorsDict.TryGetValue(key, out var c);
                string displayName = c != null ? c.Name.Trim() : (allDebits.FirstOrDefault(d => d.DebtorName.Trim().ToLower() == key)?.DebtorName.Trim() ?? allRepayments.FirstOrDefault(r => r.CreditorName.Trim().ToLower() == key)?.CreditorName.Trim() ?? key);

                rows.Add(new DebtorDisplayRow
                {
                    CreditorId = c?.CreditorId ?? 0,
                    Name = displayName,
                    Phone = c?.Phone ?? string.Empty,
                    TotalDebt = totalDebt,
                    TotalRepayment = totalRepayment
                });
            }

            OverallOutstandingBalance = overallOutstanding;
            ActiveDebtorsCount = activeCount;
            Debtors = new ObservableCollection<DebtorDisplayRow>(rows.OrderBy(r => r.Name));
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
                new() { Label = "Net Cash Position", Value = "₹" + NetCashPosition.ToString("N2"), Highlight = true },
                new() { Label = "Bank Cash", Value = "₹" + TotalBankCash.ToString("N2"), Highlight = false },
                new() { Label = "Cash In Hand", Value = "₹" + TotalCashInHand.ToString("N2"), Highlight = false },
                new() { Label = "Digital Collections", Value = "₹" + TotalDigitalPayments.ToString("N2"), Highlight = false },
                new() { Label = "Total Expenses", Value = "₹" + TotalExpenses.ToString("N2"), Highlight = false },
                new() { Label = "Debtor Repayments", Value = "₹" + TotalCreditorRepayments.ToString("N2"), Highlight = false },
                new() { Label = "Debtor Debits", Value = "₹" + TotalDebitorsOutstanding.ToString("N2"), Highlight = false },
                new() { Label = "Overall Outstanding", Value = "₹" + OverallOutstandingBalance.ToString("N2"), Highlight = false },
                new() { Label = "Active Debtors", Value = ActiveDebtorsCount.ToString(), Highlight = false }
            };

            var headers = new List<string> { "Date", "Type / Mode", "Debtor Name", "Reference / Cheque", "Debit (Sales on Credit)", "Credit (Repayments Received)" };
            var rows = new List<List<string>>();

            // Add debtor debits (Sales on Credit)
            foreach (var d in DebtorLogs)
            {
                rows.Add(new List<string>
                {
                    d.Date.ToString("dd-MMM-yyyy"),
                    "Debtor Debit",
                    d.DebtorName,
                    d.ChequeNo ?? "—",
                    "₹" + d.Amount.ToString("N2"),
                    "—"
                });
            }

            // Add debtor repayments
            foreach (var r in Repayments)
            {
                rows.Add(new List<string>
                {
                    r.RepaymentDate.ToString("dd-MMM-yyyy"),
                    $"Repayment ({r.PaymentMode})",
                    r.CreditorName,
                    r.ChequeNo ?? "—",
                    "—",
                    "₹" + r.Amount.ToString("N2")
                });
            }

            // Sort all rows by Date
            rows = rows.OrderBy(r => DateTime.ParseExact(r[0], "dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture)).ToList();

            var printData = new GenericGridPrintData
            {
                Title = "Financial Summary Statement",
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
            Serilog.Log.Error(ex, "Failed to print Financial Summary report");
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
                new() { Label = "Net Cash Position", Value = "₹" + NetCashPosition.ToString("N2"), Highlight = true },
                new() { Label = "Bank Cash", Value = "₹" + TotalBankCash.ToString("N2"), Highlight = false },
                new() { Label = "Cash In Hand", Value = "₹" + TotalCashInHand.ToString("N2"), Highlight = false },
                new() { Label = "Digital Collections", Value = "₹" + TotalDigitalPayments.ToString("N2"), Highlight = false },
                new() { Label = "Total Expenses", Value = "₹" + TotalExpenses.ToString("N2"), Highlight = false },
                new() { Label = "Debtor Repayments", Value = "₹" + TotalCreditorRepayments.ToString("N2"), Highlight = false },
                new() { Label = "Debtor Debits", Value = "₹" + TotalDebitorsOutstanding.ToString("N2"), Highlight = false },
                new() { Label = "Overall Outstanding", Value = "₹" + OverallOutstandingBalance.ToString("N2"), Highlight = false },
                new() { Label = "Active Debtors", Value = ActiveDebtorsCount.ToString(), Highlight = false }
            };

            var headers = new List<string> { "Date", "Type / Mode", "Debtor Name", "Reference / Cheque", "Debit (Sales on Credit)", "Credit (Repayments Received)" };
            var rows = new List<List<string>>();

            foreach (var d in DebtorLogs)
            {
                rows.Add(new List<string>
                {
                    d.Date.ToString("dd-MMM-yyyy"),
                    "Debtor Debit",
                    d.DebtorName,
                    d.ChequeNo ?? "—",
                    "₹" + d.Amount.ToString("N2"),
                    "—"
                });
            }

            foreach (var r in Repayments)
            {
                rows.Add(new List<string>
                {
                    r.RepaymentDate.ToString("dd-MMM-yyyy"),
                    $"Repayment ({r.PaymentMode})",
                    r.CreditorName,
                    r.ChequeNo ?? "—",
                    "—",
                    "₹" + r.Amount.ToString("N2")
                });
            }

            rows = rows.OrderBy(r => DateTime.ParseExact(r[0], "dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture)).ToList();

            var printData = new GenericGridPrintData
            {
                Title = "Financial Summary Statement",
                Subtitle = $"Date Range: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var path = await _excelExportService.ExportGenericGridAsync(printData, "FinancialSummary");
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to export Financial Summary to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public class DebtorLogEntryDto
{
    public DateTime Date { get; set; }
    public string DsmName { get; set; } = string.Empty;
    public string DebtorName { get; set; } = string.Empty;
    public string? ChequeNo { get; set; }
    public double Amount { get; set; }
}
