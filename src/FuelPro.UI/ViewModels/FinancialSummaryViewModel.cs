using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

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

    // Repayment details
    public ObservableCollection<CreditorRepayment> Repayments { get; } = new();

    public FinancialSummaryViewModel()
    {
        _dsmEntryRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _repaymentRepo = App.Services.GetRequiredService<ICreditorRepaymentRepository>();
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
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

            foreach (var entry in entries)
            {
                TotalBankCash += entry.CashDenominations.Where(x => x.CashType == "Cash1").Sum(x => x.TotalAmount)
                                 + (entry.PaymentCollection?.CashDeposit ?? 0);
                TotalCashInHand += entry.CashDenominations.Where(x => x.CashType == "Cash2").Sum(x => x.TotalAmount);

                TotalDigitalPayments += (entry.PaymentCollection?.PhonePe ?? 0)
                                        + (entry.PaymentCollection?.PhonePeCardMorning ?? 0)
                                        + (entry.PaymentCollection?.PhonePeCardNight ?? 0)
                                        + (entry.PaymentCollection?.CreditCard ?? 0)
                                        + (entry.PaymentCollection?.PetroCard ?? 0);

                TotalExpenses += entry.Expenses.Sum(e => e.Amount);
                TotalDebitorsOutstanding += entry.DebitEntries.Sum(d => d.Amount);
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
        }
        finally { IsLoading = false; }
    }
}
