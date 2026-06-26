using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.UI.Printing;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;
using Serilog;

namespace FuelPro.UI.ViewModels;

public partial class CardSettlementItem : ObservableObject
{
    public string RomanIndex { get; set; } = string.Empty;
    public string DsmName { get; set; } = string.Empty;
    public double Amount { get; set; }
    
    [ObservableProperty] private string? _tid;
    [ObservableProperty] private string? _batch;
    
    public PaymentCollection PaymentCollection { get; set; } = null!;
}

public partial class CardSettlementViewModel : ObservableObject
{
    private readonly IShiftRepository _shiftRepo;
    private readonly IDsmEntryRepository _dsmRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly PrintService _printService;
    private readonly ILogger _logger = Log.ForContext<CardSettlementViewModel>();

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private string _selectedShift = "A";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isShiftLocked;
    
    [ObservableProperty] private double _cardTotal;
    [ObservableProperty] private double _phonePeTotal;
    [ObservableProperty] private double _petroCardTotal;

    public string[] ShiftOptions { get; } = { "A", "B", "C" };

    public ObservableCollection<CardSettlementItem> CardPayments { get; } = new();
    public ObservableCollection<CardSettlementItem> PhonePePayments { get; } = new();
    public ObservableCollection<CardSettlementItem> PetroCardPayments { get; } = new();

    private Shift? _currentShift;

    public CardSettlementViewModel()
    {
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _dsmRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _paymentRepo = App.Services.GetRequiredService<IPaymentRepository>();
        _printService = App.Services.GetRequiredService<PrintService>();

        _ = LoadShiftDataAsync();
    }

    partial void OnSelectedDateChanged(DateTime value) => _ = LoadShiftDataAsync();
    partial void OnSelectedShiftChanged(string value) => _ = LoadShiftDataAsync();

    [RelayCommand]
    public async Task LoadShiftDataAsync()
    {
        IsLoading = true;
        StatusMessage = "";
        HasData = false;
        _currentShift = null;

        CardPayments.Clear();
        PhonePePayments.Clear();
        PetroCardPayments.Clear();
        CardTotal = 0;
        PhonePeTotal = 0;
        PetroCardTotal = 0;

        try
        {
            var shiftResult = await _shiftRepo.GetShiftAsync(SelectedDate, SelectedShift);
            if (!shiftResult.Success || shiftResult.Data == null)
            {
                StatusMessage = "No shift found for this date and shift.";
                return;
            }

            _currentShift = shiftResult.Data;
            IsShiftLocked = _currentShift.IsLocked;

            var entriesResult = await _dsmRepo.GetEntriesForShiftAsync(_currentShift.ShiftId);
            if (!entriesResult.Success || entriesResult.Data == null || entriesResult.Data.Count == 0)
            {
                StatusMessage = "No DSM entries found for this date and shift.";
                return;
            }

            var entries = entriesResult.Data.OrderBy(e => e.PumpId).ToList();

            foreach (var entry in entries)
            {
                var pc = entry.PaymentCollection ?? new PaymentCollection { DsmEntryId = entry.DsmEntryId };
                pc.DsmEntry = entry; // backlink

                // Card payments
                var cardAmount = pc.CreditCardMorning + pc.CreditCardNight;
                if (cardAmount > 0)
                {
                    CardPayments.Add(new CardSettlementItem
                    {
                        RomanIndex = ToRoman(entry.PumpId),
                        DsmName = entry.DsmName,
                        Amount = cardAmount,
                        Tid = pc.CardTid,
                        Batch = pc.CardBatch,
                        PaymentCollection = pc
                    });
                }

                // PhonePe payments
                var phonePeAmount = pc.PhonePeMorning + pc.PhonePeNight + pc.PhonePeCardMorning + pc.PhonePeCardNight;
                if (phonePeAmount > 0)
                {
                    PhonePePayments.Add(new CardSettlementItem
                    {
                        RomanIndex = ToRoman(entry.PumpId),
                        DsmName = entry.DsmName,
                        Amount = phonePeAmount,
                        Tid = pc.PhonePeTid,
                        Batch = pc.PhonePeBatch,
                        PaymentCollection = pc
                    });
                }

                // PetroCard payments
                var petroAmount = pc.PetroCard;
                if (petroAmount > 0)
                {
                    PetroCardPayments.Add(new CardSettlementItem
                    {
                        RomanIndex = ToRoman(entry.PumpId),
                        DsmName = entry.DsmName,
                        Amount = petroAmount,
                        Tid = pc.PetroCardTid,
                        Batch = pc.PetroCardBatch,
                        PaymentCollection = pc
                    });
                }
            }

            HasData = CardPayments.Count > 0 || PhonePePayments.Count > 0 || PetroCardPayments.Count > 0;
            if (!HasData)
            {
                StatusMessage = "No card, PhonePe, or Petro Card payments found for this shift.";
            }

            RecalculateTotals();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load card settlement data");
            StatusMessage = $"Error loading data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RecalculateTotals()
    {
        double cardSum = 0;
        foreach (var item in CardPayments) cardSum += item.Amount;
        
        double phoneSum = 0;
        foreach (var item in PhonePePayments) phoneSum += item.Amount;

        double petroSum = 0;
        foreach (var item in PetroCardPayments) petroSum += item.Amount;

        CardTotal = cardSum;
        PhonePeTotal = phoneSum;
        PetroCardTotal = petroSum;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_currentShift == null) return;

        if (IsShiftLocked)
        {
            MessageBox.Show("This shift is locked. Changes cannot be saved.", "Shift Locked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // No shift level POS total needed anymore

            // Sync payment collections
            var uniqueCollections = CardPayments.Select(x => x.PaymentCollection)
                .Concat(PhonePePayments.Select(x => x.PaymentCollection))
                .Concat(PetroCardPayments.Select(x => x.PaymentCollection))
                .GroupBy(x => x.DsmEntryId)
                .Select(g => g.First())
                .ToList();

            foreach (var pc in uniqueCollections)
            {
                var cardItem = CardPayments.FirstOrDefault(x => x.PaymentCollection.DsmEntryId == pc.DsmEntryId);
                if (cardItem != null)
                {
                    pc.CardTid = cardItem.Tid;
                    pc.CardBatch = cardItem.Batch;
                }

                var phonePeItem = PhonePePayments.FirstOrDefault(x => x.PaymentCollection.DsmEntryId == pc.DsmEntryId);
                if (phonePeItem != null)
                {
                    pc.PhonePeTid = phonePeItem.Tid;
                    pc.PhonePeBatch = phonePeItem.Batch;
                }

                var petroItem = PetroCardPayments.FirstOrDefault(x => x.PaymentCollection.DsmEntryId == pc.DsmEntryId);
                if (petroItem != null)
                {
                    pc.PetroCardTid = petroItem.Tid;
                    pc.PetroCardBatch = petroItem.Batch;
                }

                var saveResult = await _paymentRepo.SavePaymentAsync(pc);
                if (!saveResult.Success)
                {
                    MessageBox.Show($"Failed to save payments for salesperson: {saveResult.Error}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            MessageBox.Show("Card settlement saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadShiftDataAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save card settlement");
            MessageBox.Show($"Failed to save card settlement: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Print()
    {
        if (_currentShift == null) return;

        var payload = new
        {
            Date = SelectedDate.ToString("yyyy-MM-dd"),
            ShiftLabel = SelectedShift,
            CardTotal = CardTotal,
            PhonePeTotal = PhonePeTotal,
            PetroCardTotal = PetroCardTotal,
            Cards = CardPayments.Select(c => new { c.RomanIndex, Name = c.DsmName, c.Tid, c.Batch, c.Amount }).ToList(),
            PhonePes = PhonePePayments.Select(p => new { p.RomanIndex, Name = p.DsmName, p.Tid, p.Batch, p.Amount }).ToList(),
            PetroCards = PetroCardPayments.Select(pc => new { pc.RomanIndex, Name = pc.DsmName, pc.Tid, pc.Batch, pc.Amount }).ToList()
        };

        _printService.PrintCardSettlement(payload);
    }

    private static string ToRoman(int number)
    {
        if (number <= 0) return number.ToString();
        if (number >= 1000) return "M" + ToRoman(number - 1000);
        if (number >= 900) return "CM" + ToRoman(number - 900);
        if (number >= 500) return "D" + ToRoman(number - 500);
        if (number >= 400) return "CD" + ToRoman(number - 400);
        if (number >= 100) return "C" + ToRoman(number - 100);
        if (number >= 90) return "XC" + ToRoman(number - 90);
        if (number >= 50) return "L" + ToRoman(number - 50);
        if (number >= 40) return "XL" + ToRoman(number - 40);
        if (number >= 10) return "X" + ToRoman(number - 10);
        if (number >= 9) return "IX" + ToRoman(number - 9);
        if (number >= 5) return "V" + ToRoman(number - 5);
        if (number >= 4) return "IV" + ToRoman(number - 4);
        if (number >= 1) return "I" + ToRoman(number - 1);
        return string.Empty;
    }
}
