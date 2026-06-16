using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;

namespace FuelPro.UI.ViewModels;

/// <summary>
/// Collection summary: payment mode breakdown for selected date range.
/// </summary>
public partial class CollectionSummaryViewModel : ObservableObject
{
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IOwnerCalculationService _ownerCalcService;

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

    public ObservableCollection<CollectionDayRow> DayRows { get; } = new();

    public CollectionSummaryViewModel()
    {
        _dsmEntryRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _ownerCalcService = App.Services.GetRequiredService<IOwnerCalculationService>();
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

            TotalCashDeposit = 0; TotalCashInHand = 0;
            TotalPhonePe = 0; TotalPhonePeCard = 0;
            TotalCreditCard = 0; TotalPetroCard = 0;
            TotalDebit = 0; GrandTotal = 0;
            DayRows.Clear();

            var byDay = entries.GroupBy(e =>
            {
                var shift = e.Shift;
                return shift != null ? shift.ShiftDate.Date : DateTime.Today;
            });

            foreach (var dayGroup in byDay.OrderBy(g => g.Key))
            {
                var dayResult = _ownerCalcService.Calculate(dayGroup, Array.Empty<Expense>(), Array.Empty<ShiftOtherCash>());

                DayRows.Add(new CollectionDayRow
                {
                    Date = dayGroup.Key,
                    CashDeposit = dayResult.CashDeposit,
                    CashInHand = dayResult.CashInHand,
                    PhonePe = dayResult.PhonePeDirect,
                    PhonePeCard = dayResult.PhonePeCard,
                    CreditCard = dayResult.CreditCard,
                    PetroCard = dayResult.PetroCard,
                    Debit = dayResult.Debit
                });

                TotalCashDeposit += dayResult.CashDeposit;
                TotalCashInHand += dayResult.CashInHand;
                TotalPhonePe += dayResult.PhonePeDirect;
                TotalPhonePeCard += dayResult.PhonePeCard;
                TotalCreditCard += dayResult.CreditCard;
                TotalPetroCard += dayResult.PetroCard;
                TotalDebit += dayResult.Debit;
            }

            GrandTotal = TotalCashDeposit + TotalCashInHand + TotalPhonePe + TotalPhonePeCard
                         + TotalCreditCard + TotalPetroCard + TotalDebit;
        }
        finally { IsLoading = false; }
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
