using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.UI.Printing;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;
using Serilog;

namespace FuelPro.UI.ViewModels;

public enum CardSettlementSlot
{
    Morning,
    Day,
    Night
}

public partial class CardSettlementItem : ObservableObject
{
    public string RomanIndex { get; set; } = string.Empty;
    public string DsmName { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string ShiftLabel { get; set; } = string.Empty; // "Morning", "Day", "Night"
    public string SlotDisplaySubtitle { get; set; } = string.Empty;
    
    [ObservableProperty] private string? _tid;
    [ObservableProperty] private string? _batch;
    
    public PaymentCollection PaymentCollection { get; set; } = null!;
    public bool IsNightShift { get; set; }
    public CardSettlementSlot Slot { get; set; }
}

public partial class TidTabViewModel : ObservableObject
{
    public string CollectionTypeCode { get; set; } = string.Empty;
    public string TabHeader { get; set; } = string.Empty;
    public string SectionTitle { get; set; } = string.Empty;
    [ObservableProperty] private double _totalAmount;
    public ObservableCollection<CardSettlementItem> Items { get; } = new();
}

public partial class CardSettlementViewModel : ObservableObject, IDisposable
{
    private readonly IShiftRepository _shiftRepo;
    private readonly IDsmEntryRepository _dsmRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly PrintService _printService;
    private readonly ITidCalculationService _tidService;
    private readonly IFeatureToggleService? _featureToggleService;
    private readonly ILogger _logger = Log.ForContext<CardSettlementViewModel>();

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private string _selectedShift = "A";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isShiftLocked;
    [ObservableProperty] private BusinessDayTidSheet? _currentTidSheet;
    
    [ObservableProperty] private double _cardTotal;
    [ObservableProperty] private double _phonePeTotal;
    [ObservableProperty] private double _petroCardTotal;

    [ObservableProperty] private TidTabViewModel? _selectedTab;
    public ObservableCollection<TidTabViewModel> Tabs { get; } = new();

    public string[] ShiftOptions { get; } = { "A", "B" };

    public ObservableCollection<CardSettlementItem> CardPayments { get; } = new();
    public ObservableCollection<CardSettlementItem> PhonePePayments { get; } = new();
    public ObservableCollection<CardSettlementItem> PetroCardPayments { get; } = new();
    public ObservableCollection<BusinessDayTidSheet> PreviousTidSheets { get; } = new();

    private Shift? _currentShift;

    public CardSettlementViewModel()
    {
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _dsmRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _paymentRepo = App.Services.GetRequiredService<IPaymentRepository>();
        _printService = App.Services.GetRequiredService<PrintService>();
        _tidService = App.Services.GetRequiredService<ITidCalculationService>();
        _featureToggleService = App.Services.GetService<IFeatureToggleService>();

        DsmEntryService.DsmEntryChanged += OnDsmEntryChanged;

        _ = LoadShiftDataAsync();
    }

    private void OnDsmEntryChanged()
    {
        App.Current?.Dispatcher?.InvokeAsync(async () => await LoadShiftDataAsync());
    }

    public void Dispose()
    {
        DsmEntryService.DsmEntryChanged -= OnDsmEntryChanged;
        GC.SuppressFinalize(this);
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
        Tabs.Clear();
        CardTotal = 0;
        PhonePeTotal = 0;
        PetroCardTotal = 0;

        try
        {
            bool useMorningNight = _featureToggleService?.IsFeatureEnabled("Collection_UseMorningNight", false) ?? false;
            var slotsToLoad = new List<(Shift shift, CardSettlementSlot slot, string label)>();

            var tidSheetTask = _tidService.GetTidSheetAsync(SelectedDate);

            if (useMorningNight)
            {
                var prevDate = SelectedDate.Date.AddDays(-1);
                var morningShiftTask = _shiftRepo.GetShiftAsync(prevDate, "A");
                var dayShiftTask = _shiftRepo.GetShiftAsync(SelectedDate.Date, "B");
                var nightShiftTask = _shiftRepo.GetShiftAsync(SelectedDate.Date, "A");

                await Task.WhenAll(morningShiftTask, dayShiftTask, nightShiftTask, tidSheetTask);

                var morningShiftRes = await morningShiftTask;
                var dayShiftRes = await dayShiftTask;
                var nightShiftRes = await nightShiftTask;

                if (morningShiftRes.Success && morningShiftRes.Data != null)
                    slotsToLoad.Add((morningShiftRes.Data, CardSettlementSlot.Morning, "Morning (12am - 8am)"));
                if (dayShiftRes.Success && dayShiftRes.Data != null)
                    slotsToLoad.Add((dayShiftRes.Data, CardSettlementSlot.Day, "Day (8am - 8pm)"));
                if (nightShiftRes.Success && nightShiftRes.Data != null)
                    slotsToLoad.Add((nightShiftRes.Data, CardSettlementSlot.Night, "Night (8pm - 12am)"));

                _currentShift = dayShiftRes.Data ?? nightShiftRes.Data ?? morningShiftRes.Data;
            }
            else
            {
                var shiftATask = _shiftRepo.GetShiftAsync(SelectedDate.Date, "A");
                var shiftBTask = _shiftRepo.GetShiftAsync(SelectedDate.Date, "B");

                await Task.WhenAll(shiftATask, shiftBTask, tidSheetTask);

                var shiftARes = await shiftATask;
                var shiftBRes = await shiftBTask;

                if (shiftARes.Success && shiftARes.Data != null)
                    slotsToLoad.Add((shiftARes.Data, CardSettlementSlot.Morning, "Shift A"));
                if (shiftBRes.Success && shiftBRes.Data != null)
                    slotsToLoad.Add((shiftBRes.Data, CardSettlementSlot.Day, "Shift B"));

                _currentShift = shiftARes.Data ?? shiftBRes.Data;
            }

            if (slotsToLoad.Count == 0)
            {
                StatusMessage = "No shifts found for selected date.";
                return;
            }

            IsShiftLocked = slotsToLoad.Any(s => s.shift.IsLocked);

            var tidSheet = await tidSheetTask;
            CurrentTidSheet = tidSheet;

            // 1. Credit Cards (legacy list)
            foreach (var item in tidSheet.CardPayments)
            {
                CardPayments.Add(new CardSettlementItem
                {
                    RomanIndex = item.RomanIndex,
                    DsmName = $"{item.DsmName} ({item.ShiftLabel})",
                    Amount = item.Amount,
                    Tid = item.Tid,
                    Batch = item.Batch,
                    ShiftLabel = item.ShiftLabel,
                    PaymentCollection = item.PaymentCollection,
                    Slot = ParseSlot(item.Slot),
                    SlotDisplaySubtitle = item.SlotDisplaySubtitle
                });
            }

            // 2. PhonePe (legacy list)
            foreach (var item in tidSheet.PhonePePayments)
            {
                PhonePePayments.Add(new CardSettlementItem
                {
                    RomanIndex = item.RomanIndex,
                    DsmName = $"{item.DsmName} ({item.ShiftLabel})",
                    Amount = item.Amount,
                    Tid = item.Tid,
                    Batch = item.Batch,
                    ShiftLabel = item.ShiftLabel,
                    PaymentCollection = item.PaymentCollection,
                    Slot = ParseSlot(item.Slot),
                    SlotDisplaySubtitle = item.SlotDisplaySubtitle
                });
            }

            // 3. PetroCard (legacy list)
            foreach (var item in tidSheet.PetroCardPayments)
            {
                PetroCardPayments.Add(new CardSettlementItem
                {
                    RomanIndex = item.RomanIndex,
                    DsmName = $"{item.DsmName} ({item.ShiftLabel})",
                    Amount = item.Amount,
                    Tid = item.Tid,
                    Batch = item.Batch,
                    ShiftLabel = item.ShiftLabel,
                    PaymentCollection = item.PaymentCollection,
                    Slot = ParseSlot(item.Slot),
                    SlotDisplaySubtitle = item.SlotDisplaySubtitle
                });
            }

            // 4. Debtor repayments — PhonePe
            foreach (var item in tidSheet.DebtorPhonePeRepayments)
            {
                PhonePePayments.Add(new CardSettlementItem
                {
                    RomanIndex = "—",
                    DsmName = $"[Debtor] {item.DsmName} ({item.ShiftLabel})",
                    Amount = item.Amount,
                    Tid = item.Tid,
                    Batch = item.Batch,
                    ShiftLabel = item.ShiftLabel,
                    Slot = ParseSlot(item.Slot),
                    SlotDisplaySubtitle = item.SlotDisplaySubtitle
                });
            }

            // 5. Debtor repayments — PineLabs Card
            foreach (var item in tidSheet.DebtorCardRepayments)
            {
                CardPayments.Add(new CardSettlementItem
                {
                    RomanIndex = "—",
                    DsmName = $"[Debtor] {item.DsmName} ({item.ShiftLabel})",
                    Amount = item.Amount,
                    Tid = item.Tid,
                    Batch = item.Batch,
                    ShiftLabel = item.ShiftLabel,
                    Slot = ParseSlot(item.Slot),
                    SlotDisplaySubtitle = item.SlotDisplaySubtitle
                });
            }

            // 6. Debtor repayments — Petro Card
            foreach (var item in tidSheet.DebtorPetroCardRepayments)
            {
                PetroCardPayments.Add(new CardSettlementItem
                {
                    RomanIndex = "—",
                    DsmName = $"[Debtor] {item.DsmName} ({item.ShiftLabel})",
                    Amount = item.Amount,
                    Tid = item.Tid,
                    Batch = item.Batch,
                    ShiftLabel = item.ShiftLabel,
                    Slot = ParseSlot(item.Slot),
                    SlotDisplaySubtitle = item.SlotDisplaySubtitle
                });
            }

            // 7. Dynamic Tabs for ALL active collection types
            int tabIndex = 1;
            foreach (var group in tidSheet.CollectionGroups)
            {
                var tabVm = new TidTabViewModel
                {
                    CollectionTypeCode = group.CollectionTypeCode,
                    TabHeader = group.DisplayName,
                    SectionTitle = $"{ToRoman(tabIndex)}. {group.DisplayName.ToUpper()} SETTLEMENT — Morning / Day / Night",
                    TotalAmount = group.TotalAmount
                };

                foreach (var item in group.Items)
                {
                    tabVm.Items.Add(new CardSettlementItem
                    {
                        RomanIndex = item.RomanIndex,
                        DsmName = (item.RomanIndex == "Debtor" || item.RomanIndex == "DSM Loss")
                            ? $"[{item.RomanIndex}] {item.DsmName} ({item.ShiftLabel})"
                            : $"{item.DsmName} ({item.ShiftLabel})",
                        Amount = item.Amount,
                        Tid = item.Tid,
                        Batch = item.Batch,
                        ShiftLabel = item.ShiftLabel,
                        PaymentCollection = item.PaymentCollection,
                        Slot = ParseSlot(item.Slot),
                        SlotDisplaySubtitle = item.SlotDisplaySubtitle
                    });
                }

                Tabs.Add(tabVm);
                tabIndex++;
            }

            SelectedTab = Tabs.FirstOrDefault();

            HasData = Tabs.Any(t => t.Items.Count > 0) || CardPayments.Count > 0 || PhonePePayments.Count > 0 || PetroCardPayments.Count > 0;
            if (!HasData)
            {
                StatusMessage = "No card or digital payment collections found for these shifts.";
            }

            RecalculateTotals();
            _ = LoadHistoryLogsAsync();
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

    private static CardSettlementSlot ParseSlot(string slot)
    {
        return slot switch
        {
            "Morning" => CardSettlementSlot.Morning,
            "Day" => CardSettlementSlot.Day,
            "Night" => CardSettlementSlot.Night,
            _ => CardSettlementSlot.Morning
        };
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
    private async Task ExportExcelAsync()
    {
        try
        {
            var exportService = App.Services.GetRequiredService<FuelPro.Core.Services.ExcelExportService>();
            var settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();
            var sResult = await settingsRepo.GetSettingsAsync();
            var stationName = sResult.Success && sResult.Data != null && !string.IsNullOrWhiteSpace(sResult.Data.PumpStationName) ? sResult.Data.PumpStationName : "Mitali Service Station";

            if (CurrentTidSheet == null) return;
            var path = await exportService.ExportTidSheetAsync(CurrentTidSheet, stationName);
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export card settlement to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Print()
    {
        if (CurrentTidSheet == null) return;
        _printService.PrintCardSettlement(CurrentTidSheet);
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

    [RelayCommand]
    public async Task LoadHistoryLogsAsync()
    {
        try
        {
            var end = DateTime.Today;
            var start = end.AddDays(-15);
            var sheets = await _tidService.GetTidSheetsForRangeAsync(start, end);
            
            PreviousTidSheets.Clear();
            foreach (var date in sheets.Keys.OrderByDescending(d => d))
            {
                var sheet = sheets[date];
                if (sheet.GrandTotal > 0 || sheet.CollectionGroups.Any(g => g.Items.Count > 0) || sheet.PhonePePayments.Count > 0 || sheet.CardPayments.Count > 0 || sheet.PetroCardPayments.Count > 0)
                {
                    PreviousTidSheets.Add(sheet);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load TID history logs");
        }
    }

    [RelayCommand]
    private async Task SelectHistoryDateAsync(DateTime date)
    {
        SelectedDate = date;
        await LoadShiftDataAsync();
    }
}
