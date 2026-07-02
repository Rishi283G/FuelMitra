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
    public string ShiftLabel { get; set; } = string.Empty; // "Morning", "Day", "Night"
    
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

    public string[] ShiftOptions { get; } = { "A", "B" };

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
            // 1. Fetch previous day's last shift (Morning)
            var prevDate = SelectedDate.AddDays(-1);
            var prevShiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(prevDate, prevDate);
            var prevShifts = prevShiftsResult.Success && prevShiftsResult.Data != null 
                ? prevShiftsResult.Data.OrderBy(s => s.ShiftType).ToList() 
                : new List<Shift>();
            var prevLastShift = prevShifts.LastOrDefault(s => s.ShiftType == "B") 
                ?? prevShifts.LastOrDefault(s => s.ShiftType != "A") 
                ?? prevShifts.LastOrDefault();

            // 2. Fetch today's shifts (Day & Night)
            var todayShiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(SelectedDate, SelectedDate);
            var todayShifts = todayShiftsResult.Success && todayShiftsResult.Data != null 
                ? todayShiftsResult.Data.OrderBy(s => s.ShiftType).ToList() 
                : new List<Shift>();

            var todayFirstShift = todayShifts.FirstOrDefault(s => s.ShiftType == "A");
            var todayLastShift = todayShifts.FirstOrDefault(s => s.ShiftType == "B") 
                ?? todayShifts.FirstOrDefault(s => s.ShiftType != "A");

            var allShiftsToLoad = new List<(Shift Shift, string Label)>();
            if (prevLastShift != null)
            {
                allShiftsToLoad.Add((prevLastShift, "Morning"));
            }
            if (todayFirstShift != null)
            {
                allShiftsToLoad.Add((todayFirstShift, "Day"));
            }
            if (todayLastShift != null)
            {
                allShiftsToLoad.Add((todayLastShift, "Night"));
            }

            if (allShiftsToLoad.Count == 0)
            {
                StatusMessage = "No shifts found for today or yesterday.";
                return;
            }

            // Set current shift context for locking check (use today's primary shift A or first available)
            _currentShift = todayFirstShift ?? todayLastShift ?? prevLastShift;
            IsShiftLocked = todayShifts.Any(s => s.IsLocked);

            foreach (var item in allShiftsToLoad)
            {
                var entriesResult = await _dsmRepo.GetEntriesForShiftAsync(item.Shift.ShiftId);
                if (entriesResult.Success && entriesResult.Data != null)
                {
                    var entries = entriesResult.Data.OrderBy(e => e.PumpId).ToList();
                    foreach (var entry in entries)
                    {
                        var pc = entry.PaymentCollection;
                        if (pc == null) continue;

                        pc.DsmEntry = entry; // backlink

                        double cardAmount = 0;
                        double phonePeAmount = 0;
                        double petroAmount = 0;

                        if (item.Label == "Morning")
                        {
                            cardAmount = pc.CreditCardMorning;
                            phonePeAmount = pc.PhonePeMorning + pc.PhonePeCardMorning;
                            petroAmount = 0; // PetroCard is not loaded from previous day's shift B
                        }
                        else if (item.Label == "Day")
                        {
                            cardAmount = pc.CreditCardMorning;
                            phonePeAmount = pc.PhonePeMorning + pc.PhonePeCardMorning;
                            petroAmount = pc.PetroCard;
                        }
                        else if (item.Label == "Night")
                        {
                            cardAmount = pc.CreditCardNight;
                            phonePeAmount = pc.PhonePeNight + pc.PhonePeCardNight;
                            petroAmount = pc.PetroCard;
                        }

                        // 1. Credit Cards
                        if (cardAmount > 0)
                        {
                            CardPayments.Add(new CardSettlementItem
                            {
                                RomanIndex = ToRoman(entry.PumpId),
                                DsmName = $"{entry.DsmName} ({item.Label})",
                                Amount = cardAmount,
                                Tid = pc.CardTid ?? string.Empty,
                                Batch = pc.CardBatch ?? string.Empty,
                                ShiftLabel = item.Label,
                                PaymentCollection = pc
                            });
                        }

                        // 2. PhonePe / Digital
                        if (phonePeAmount > 0)
                        {
                            PhonePePayments.Add(new CardSettlementItem
                            {
                                RomanIndex = ToRoman(entry.PumpId),
                                DsmName = $"{entry.DsmName} ({item.Label})",
                                Amount = phonePeAmount,
                                Tid = pc.PhonePeTid ?? string.Empty,
                                Batch = pc.PhonePeBatch ?? string.Empty,
                                ShiftLabel = item.Label,
                                PaymentCollection = pc
                            });
                        }

                        // 3. PetroCard
                        if (petroAmount > 0)
                        {
                            PetroCardPayments.Add(new CardSettlementItem
                            {
                                RomanIndex = ToRoman(entry.PumpId),
                                DsmName = $"{entry.DsmName} ({item.Label})",
                                Amount = petroAmount,
                                Tid = pc.PetroCardTid ?? string.Empty,
                                Batch = pc.PetroCardBatch ?? string.Empty,
                                ShiftLabel = item.Label,
                                PaymentCollection = pc
                            });
                        }
                    }
                }
            }

            HasData = CardPayments.Count > 0 || PhonePePayments.Count > 0 || PetroCardPayments.Count > 0;
            if (!HasData)
            {
                StatusMessage = "No card, PhonePe, or Petro Card payments found for these shifts.";
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
        if (IsShiftLocked)
        {
            MessageBox.Show("Shifts are locked. Changes cannot be saved.", "Shift Locked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // Gather all payment collections to save
            var uniqueCollections = CardPayments.Select(x => x.PaymentCollection)
                .Concat(PhonePePayments.Select(x => x.PaymentCollection))
                .Concat(PetroCardPayments.Select(x => x.PaymentCollection))
                .GroupBy(x => x.PaymentId)
                .Select(g => g.First())
                .ToList();

            foreach (var pc in uniqueCollections)
            {
                // Sync TID/Batch from edited lists back to model
                var cardItem = CardPayments.FirstOrDefault(x => x.PaymentCollection.PaymentId == pc.PaymentId);
                if (cardItem != null)
                {
                    pc.CardTid = cardItem.Tid;
                    pc.CardBatch = cardItem.Batch;
                }

                var phonePeItem = PhonePePayments.FirstOrDefault(x => x.PaymentCollection.PaymentId == pc.PaymentId);
                if (phonePeItem != null)
                {
                    pc.PhonePeTid = phonePeItem.Tid;
                    pc.PhonePeBatch = phonePeItem.Batch;
                }

                var petroItem = PetroCardPayments.FirstOrDefault(x => x.PaymentCollection.PaymentId == pc.PaymentId);
                if (petroItem != null)
                {
                    pc.PetroCardTid = petroItem.Tid;
                    pc.PetroCardBatch = petroItem.Batch;
                }

                var saveResult = await _paymentRepo.SavePaymentAsync(pc);
                if (!saveResult.Success)
                {
                    MessageBox.Show($"Failed to save payments: {saveResult.Error}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            MessageBox.Show("Card & digital settlement saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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

        double phonePeMorning = PhonePePayments.Where(p => p.ShiftLabel == "Morning").Sum(p => p.Amount);
        double phonePeDay = PhonePePayments.Where(p => p.ShiftLabel == "Day").Sum(p => p.Amount);
        double phonePeNight = PhonePePayments.Where(p => p.ShiftLabel == "Night").Sum(p => p.Amount);

        double cardMorning = CardPayments.Where(c => c.ShiftLabel == "Morning").Sum(c => c.Amount);
        double cardDay = CardPayments.Where(c => c.ShiftLabel == "Day").Sum(c => c.Amount);
        double cardNight = CardPayments.Where(c => c.ShiftLabel == "Night").Sum(c => c.Amount);

        double petroNight = PetroCardPayments.Where(p => p.ShiftLabel == "Night").Sum(p => p.Amount);
        double petroDay = PetroCardPayments.Where(p => p.ShiftLabel == "Day").Sum(p => p.Amount);

        var payload = new
        {
            Date = SelectedDate.ToString("dd/MM/yyyy"),
            CardTotal = CardTotal,
            PhonePeTotal = PhonePeTotal,
            PetroCardTotal = PetroCardTotal,
            GrandTotal = CardTotal + PhonePeTotal + PetroCardTotal,
            Cards = CardPayments.Select(c => new { c.Tid, c.Batch, c.Amount }).ToList(),
            PhonePeSummary = new { Morning = phonePeMorning, Day = phonePeDay, Night = phonePeNight, Total = PhonePeTotal },
            CardSummary = new { Morning = cardMorning, Day = cardDay, Night = cardNight, Total = CardTotal },
            PetroSummary = new { Night = petroNight, Day = petroDay, Total = PetroCardTotal }
        };

        _printService.PrintCardSettlement(payload);
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        try
        {
            var exportService = App.Services.GetRequiredService<FuelPro.Core.Services.ExcelExportService>();
            var summaryCards = new List<FuelPro.Core.DTOs.GenericGridPrintCard>
            {
                new() { Label = "Total PineLabs Card", Value = "₹" + CardTotal.ToString("N2"), Highlight = false },
                new() { Label = "Total PhonePe", Value = "₹" + PhonePeTotal.ToString("N2"), Highlight = false },
                new() { Label = "Total Petro Card", Value = "₹" + PetroCardTotal.ToString("N2"), Highlight = false },
                new() { Label = "Grand Total Digital", Value = "₹" + (CardTotal + PhonePeTotal + PetroCardTotal).ToString("N2"), Highlight = true }
            };

            var headers = new List<string> { "Payment Type", "Index", "DSM Name", "TID", "Batch No.", "Amount (₹)" };
            var rows = new List<List<string>>();

            foreach (var c in CardPayments)
            {
                rows.Add(new List<string> { "PineLabs Card", c.RomanIndex, c.DsmName, c.Tid ?? "—", c.Batch ?? "—", "₹" + c.Amount.ToString("N2") });
            }
            foreach (var p in PhonePePayments)
            {
                rows.Add(new List<string> { "PhonePe", p.RomanIndex, p.DsmName, p.Tid ?? "—", p.Batch ?? "—", "₹" + p.Amount.ToString("N2") });
            }
            foreach (var pc in PetroCardPayments)
            {
                rows.Add(new List<string> { "Petro Card", pc.RomanIndex, pc.DsmName, pc.Tid ?? "—", pc.Batch ?? "—", "₹" + pc.Amount.ToString("N2") });
            }

            var printData = new FuelPro.Core.DTOs.GenericGridPrintData
            {
                Title = "Card & Digital Settlement Report",
                Subtitle = $"Date: {SelectedDate:dd-MMM-yyyy}  |  Shift: {SelectedShift}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var path = await exportService.ExportGenericGridAsync(printData, "CardSettlement");
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export card settlement to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
