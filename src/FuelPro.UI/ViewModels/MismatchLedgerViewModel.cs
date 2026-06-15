using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace FuelPro.UI.ViewModels;

public partial class MismatchLedgerViewModel : ObservableObject
{
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IDsmCalculationService _calcService;

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

                var mismatch = (double)calc.Mismatch;
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
                    SalesAmount = (double)calc.GrossSales,
                    CollectionAmount = (double)calc.TotalCollection,
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
