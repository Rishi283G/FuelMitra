using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Core.DTOs;
using FuelPro.Core.Repositories;
using FuelPro.Core.Models.AGS;
using FuelPro.UI.Printing;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;
using Newtonsoft.Json;
using System.Windows.Threading;

namespace FuelPro.UI.ViewModels;

/// <summary>
/// Observable nozzle reading row for DataGrid binding.
/// </summary>
public partial class NozzleReadingRow : ObservableObject
{
    public Action? OnRowChanged { get; set; }

    [ObservableProperty] private int _nozzleNumber;
    [ObservableProperty] private string _fuelType = "";
    public string FuelTypeLabel => FuelPro.Core.Common.FuelTypeExtensions.ToFriendlyLabel(FuelType);
    public string UnitLabel => FuelPro.Core.Common.FuelTypeExtensions.GetUnitLabel(FuelType);
    [ObservableProperty] private double? _autoOpeningReading;
    [ObservableProperty] private double? _openingReading;
    [ObservableProperty] private double? _closingReading;
    [ObservableProperty] private double _saleLitres;
    [ObservableProperty] private double? _rate;
    [ObservableProperty] private double _amount;
    [ObservableProperty] private bool _isManualOpeningOverride;
    [ObservableProperty] private string _openingWarning = "";
    [ObservableProperty] private bool _hasValidationError;
    partial void OnOpeningReadingChanged(double? value)
    {
        if (AutoOpeningReading.HasValue)
        {
            IsManualOpeningOverride = !value.HasValue || Math.Abs(value.Value - AutoOpeningReading.Value) > 0.0001;
        }
        else
        {
            IsManualOpeningOverride = value.HasValue;
        }
        Recalculate();
    }
    partial void OnClosingReadingChanged(double? value) => Recalculate();
    partial void OnRateChanged(double? value) => Recalculate();

    private void Recalculate()
    {
        HasValidationError = ClosingReading.HasValue && OpeningReading.HasValue && ClosingReading.Value < OpeningReading.Value;
        SaleLitres = Math.Max(0, (ClosingReading ?? 0) - (OpeningReading ?? 0));
        Amount = SaleLitres * (Rate ?? 0);
        OnRowChanged?.Invoke();
    }
}

public partial class DebitRow : ObservableObject
{
    public Action? OnRowChanged { get; set; }
    private readonly IEnumerable<Creditor> _creditors;

    [ObservableProperty] private string _debtorName = "";
    [ObservableProperty] private string? _chequeNo;
    [ObservableProperty] private double? _amount;
    [ObservableProperty] private string? _vehicleNumber;
    [ObservableProperty] private string _paymentMethod = "Credit";

    // Card fields
    [ObservableProperty] private string? _cardTid;
    [ObservableProperty] private string? _cardBatch;

    // Cash denomination fields
    [ObservableProperty] private int? _denom500;
    [ObservableProperty] private int? _denom200;
    [ObservableProperty] private int? _denom100;
    [ObservableProperty] private int? _denom50;
    [ObservableProperty] private int? _denom20;
    [ObservableProperty] private int? _denom10;
    [ObservableProperty] private int? _coins;

    public ObservableCollection<string> AvailableVehicles { get; } = new();

    public DebitRow()
    {
        _creditors = new List<Creditor>();
    }

    public DebitRow(IEnumerable<Creditor> creditors, Action? onRowChanged = null)
    {
        _creditors = creditors;
        OnRowChanged = onRowChanged;
    }

    partial void OnDebtorNameChanged(string value)
    {
        AvailableVehicles.Clear();
        VehicleNumber = null;
        if (string.IsNullOrWhiteSpace(value)) return;

        var creditor = _creditors.FirstOrDefault(c => string.Equals(c.Name, value, StringComparison.OrdinalIgnoreCase));
        if (creditor != null && creditor.Vehicles != null)
        {
            foreach (var v in creditor.Vehicles.Where(x => x.IsActive))
            {
                AvailableVehicles.Add(v.VehicleNumber);
            }
        }
    }

    partial void OnAmountChanged(double? value) => OnRowChanged?.Invoke();
}

public partial class ExpenseRow : ObservableObject
{
    public Action? OnRowChanged { get; set; }

    [ObservableProperty] private string _description = "";
    [ObservableProperty] private double? _amount;

    partial void OnAmountChanged(double? value) => OnRowChanged?.Invoke();
}

public partial class TestingRow : ObservableObject
{
    public Action? OnRowChanged { get; set; }

    [ObservableProperty] private int _nozzleNumber;
    [ObservableProperty] private string _fuelType = "";
    [ObservableProperty] private string _tankName = "";
    [ObservableProperty] private double? _litres;
    [ObservableProperty] private double? _rate;
    [ObservableProperty] private double _amount;

    partial void OnLitresChanged(double? value) => Recalculate();
    partial void OnRateChanged(double? value) => Recalculate();

    private void Recalculate()
    {
        Amount = (Litres ?? 0) * (Rate ?? 0);
        OnRowChanged?.Invoke();
    }
}

public partial class CashDenomRow : ObservableObject
{
    public Action? OnTotalChanged { get; set; }

    public string CashType { get; set; } = "Cash1";
    [ObservableProperty] private int? _denom500;
    [ObservableProperty] private int? _denom200;
    [ObservableProperty] private int? _denom100;
    [ObservableProperty] private int? _denom50;
    [ObservableProperty] private int? _denom20;
    [ObservableProperty] private int? _denom10;
    [ObservableProperty] private int? _coins;
    [ObservableProperty] private double _totalAmount;

    partial void OnDenom500Changed(int? value) => RecalcTotal();
    partial void OnDenom200Changed(int? value) => RecalcTotal();
    partial void OnDenom100Changed(int? value) => RecalcTotal();
    partial void OnDenom50Changed(int? value) => RecalcTotal();
    partial void OnDenom20Changed(int? value) => RecalcTotal();
    partial void OnDenom10Changed(int? value) => RecalcTotal();
    partial void OnCoinsChanged(int? value) => RecalcTotal();

    partial void OnTotalAmountChanged(double value) => OnTotalChanged?.Invoke();

    private void RecalcTotal()
    {
        double sum;
        if (CashType == "Cash1")
        {
            sum = (Denom500 ?? 0) * 500.0 + (Denom200 ?? 0) * 200.0 + (Denom100 ?? 0) * 100.0;
        }
        else
        {
            sum = (Denom500 ?? 0) * 500.0 + (Denom200 ?? 0) * 200.0 + (Denom100 ?? 0) * 100.0
                + (Denom50 ?? 0) * 50.0 + (Denom20 ?? 0) * 20.0 + (Denom10 ?? 0) * 10.0 + (Coins ?? 0);
        }
        TotalAmount = sum;
    }
}

public partial class PersonalDebtorRow : ObservableObject
{
    public Action? OnRowChanged { get; set; }

    [ObservableProperty] private int _dsmPersonalDebtorId;
    [ObservableProperty] private string _time = string.Empty;
    [ObservableProperty] private double? _amount;
    [ObservableProperty] private string _fuelProduct = "MS-II";
    [ObservableProperty] private string _remarks = string.Empty;
    [ObservableProperty] private string _paymentMethod = "Cash";
    [ObservableProperty] private string _cardTid = string.Empty;
    [ObservableProperty] private string _cardBatch = string.Empty;
    [ObservableProperty] private int? _denom500;
    [ObservableProperty] private int? _denom200;
    [ObservableProperty] private int? _denom100;
    [ObservableProperty] private int? _denom50;
    [ObservableProperty] private int? _denom20;
    [ObservableProperty] private int? _denom10;
    [ObservableProperty] private int? _coins;

    public string[] FuelProducts { get; } = { "MS-I", "MS-II", "HSD" };
    public string[] PaymentMethods { get; } = { "Cash", "PhonePe", "PetroCard", "Others" };

    partial void OnAmountChanged(double? value) => OnRowChanged?.Invoke();
    partial void OnPaymentMethodChanged(string value) => OnRowChanged?.Invoke();
}

public partial class DsmEntryViewModel : ObservableObject
{
    private readonly DsmEntryService _dsmService;
    private readonly DraftService _draftService;
    private readonly IDsmCalculationService _dsmCalculationService;
    private readonly Core.Repositories.INozzleReadingRepository _nozzleRepository;
    private readonly IAgsImportRepository _agsImportRepository;
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly PrintService _printService;

    // Header fields
    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private string _selectedShift = "A";
    [ObservableProperty] private string _dsmName = "";
    [ObservableProperty] private PumpDisplayItem _selectedPump = null!;
    [ObservableProperty] private PumpDisplayItem? _selectedConnectedPump;
    [ObservableProperty] private double _connectedPumpGrossSales;
    [ObservableProperty] private string _connectedPumpStatus = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private int? _editingEntryId;
    [ObservableProperty] private string _startTime = "08:00 AM";
    [ObservableProperty] private string _endTime = "08:00 PM";

    // Nozzle readings
    public ObservableCollection<NozzleReadingRow> NozzleReadings { get; } = new();

    [ObservableProperty] private double? _phonePeCardMorning;
    [ObservableProperty] private double? _phonePeCardNight;
    [ObservableProperty] private double? _phonePeMorning;
    [ObservableProperty] private double? _phonePeNight;
    [ObservableProperty] private double? _creditCardMorning;
    [ObservableProperty] private double? _creditCardNight;
    [ObservableProperty] private double? _petroCardMorning;
    [ObservableProperty] private double? _petroCardNight;
    [ObservableProperty] private double? _others;
    [ObservableProperty] private double? _cashDeposit;

    [ObservableProperty] private string? _cardTid;
    [ObservableProperty] private string? _cardBatch;
    [ObservableProperty] private string? _phonePeTid;
    [ObservableProperty] private string? _phonePeBatch;
    [ObservableProperty] private string? _petroCardTid;
    [ObservableProperty] private string? _petroCardBatch;

    [ObservableProperty] private string? _phonePeTidMorning;
    [ObservableProperty] private string? _phonePeBatchMorning;
    [ObservableProperty] private string? _phonePeTidNight;
    [ObservableProperty] private string? _phonePeBatchNight;

    [ObservableProperty] private string? _creditCardTidMorning;
    [ObservableProperty] private string? _creditCardBatchMorning;
    [ObservableProperty] private string? _creditCardTidNight;
    [ObservableProperty] private string? _creditCardBatchNight;

    [ObservableProperty] private string? _petroCardTidMorning;
    [ObservableProperty] private string? _petroCardBatchMorning;
    [ObservableProperty] private string? _petroCardTidNight;
    [ObservableProperty] private string? _petroCardBatchNight;

    partial void OnPhonePeCardMorningChanged(double? value) => RecalculateAll();
    partial void OnPhonePeCardNightChanged(double? value) => RecalculateAll();
    partial void OnPhonePeMorningChanged(double? value) => RecalculateAll();
    partial void OnPhonePeNightChanged(double? value) => RecalculateAll();
    partial void OnCreditCardMorningChanged(double? value) => RecalculateAll();
    partial void OnCreditCardNightChanged(double? value) => RecalculateAll();
    partial void OnPetroCardMorningChanged(double? value) => RecalculateAll();
    partial void OnPetroCardNightChanged(double? value) => RecalculateAll();
    partial void OnOthersChanged(double? value) => RecalculateAll();
    partial void OnCashDepositChanged(double? value) => RecalculateAll();

    // Dynamic sections
    public ObservableCollection<DebitRow> Debits { get; } = new();
    public ObservableCollection<ExpenseRow> Expenses { get; } = new();

    // Testing
    public ObservableCollection<TestingRow> TestingRows { get; } = new();
    [ObservableProperty] private bool _showTesting;
    [ObservableProperty] private bool _showMsTesting;
    [ObservableProperty] private bool _showHsdTesting;

    // Cash
    [ObservableProperty] private CashDenomRow _cash1;
    [ObservableProperty] private CashDenomRow _cash2;

    // Reconciliation (computed)
    [ObservableProperty] private double _totalLitres;
    [ObservableProperty] private double _grossSales;
    [ObservableProperty] private double _totalPaymentIn;
    [ObservableProperty] private double _totalDebtors;
    [ObservableProperty] private double _finalAdjusted;
    [ObservableProperty] private double _difference;
    [ObservableProperty] private string _validationSummary = "";
    public ObservableCollection<string> OpeningWarnings { get; } = new();
    [ObservableProperty] private string _agsImportStatusText = "";

    // Autocomplete
    public ObservableCollection<string> DsmOptions { get; } = new();
    public ObservableCollection<Creditor> Creditors { get; } = new();

    private bool _isEditing;
    public string[] ShiftOptions { get; } = { "A", "B" };
    public ObservableCollection<PumpDisplayItem> PumpOptions { get; } = new();
    public ObservableCollection<PumpDisplayItem> ConnectablePumpOptions { get; } = new();

    // DSM list for current shift
    public ObservableCollection<DsmEntrySummaryDto> ShiftEntries { get; } = new();

    private readonly IDsmProfileRepository _dsmProfileRepo;
    private readonly ICreditorRepository _creditorRepo;

    public DsmEntryViewModel()
    {
        _dsmService = App.Services.GetRequiredService<DsmEntryService>();
        _draftService = App.Services.GetRequiredService<DraftService>();
        _dsmCalculationService = App.Services.GetRequiredService<IDsmCalculationService>();
        _nozzleRepository = App.Services.GetRequiredService<Core.Repositories.INozzleReadingRepository>();
        _agsImportRepository = App.Services.GetRequiredService<IAgsImportRepository>();
        _dsmProfileRepo = App.Services.GetRequiredService<IDsmProfileRepository>();
        _creditorRepo = App.Services.GetRequiredService<ICreditorRepository>();
        _printService = App.Services.GetRequiredService<PrintService>();

        Cash1 = new CashDenomRow { CashType = "Cash1", OnTotalChanged = RecalculateAll };
        Cash2 = new CashDenomRow { CashType = "Cash2", OnTotalChanged = RecalculateAll };

        // Populate pump options for default selected date
        ReloadPumpOptions();

        // Auto-save timer
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _autoSaveTimer.Tick += (_, _) => SaveDraft();
        _autoSaveTimer.Start();

        LoadNozzlesForPump();
        _ = LoadSuggestionsAsync();
    }


    partial void OnSelectedPumpChanged(PumpDisplayItem value)
    {
        if (value != null)
        {
            RefreshConnectablePumpOptions();
            if (!_isEditing) LoadNozzlesForPump();
        }
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        ReloadPumpOptions();
        if (!_isEditing) LoadNozzlesForPump();
    }

    public bool IsNightShift => SelectedShift == "A";
    public bool IsDayShift => SelectedShift == "B";

    public string MorningLabel => IsDayShift ? "Day (8am - 8pm) (₹)" : "Morning (12am - 8am) (₹)";
    public string MorningTidLabel => IsDayShift ? "Day TID" : "Morning TID";
    public string MorningBatchLabel => IsDayShift ? "Day Batch" : "Morning Batch";

    partial void OnSelectedShiftChanged(string value)
    {
        OnPropertyChanged(nameof(IsNightShift));
        OnPropertyChanged(nameof(IsDayShift));
        OnPropertyChanged(nameof(MorningLabel));
        OnPropertyChanged(nameof(MorningTidLabel));
        OnPropertyChanged(nameof(MorningBatchLabel));
        if (!_isEditing) LoadNozzlesForPump();
    }
    partial void OnDsmNameChanged(string value) => _ = RefreshConnectedPumpGrossSalesAsync();
    partial void OnSelectedConnectedPumpChanged(PumpDisplayItem? value)
    {
        _ = RefreshConnectedPumpGrossSalesAsync();
        if (!_isEditing) LoadNozzlesForPump();
    }

    private void LoadNozzlesForPump()
    {
        _ = LoadNozzlesForPumpAsync();
    }

    private async Task LoadNozzlesForPumpAsync()
    {
        // Capture entered values to restore later
        var tempReadings = new Dictionary<int, (double? Opening, double? Closing, double? Rate, bool Override)>();
        if (!_isEditing)
        {
            foreach (var r in NozzleReadings)
            {
                tempReadings[r.NozzleNumber] = (r.OpeningReading, r.ClosingReading, r.Rate, r.IsManualOpeningOverride);
            }
        }

        NozzleReadings.Clear();
        OpeningWarnings.Clear();
        if (SelectedPump == null) return;

        var primaryNozzles = PumpConfiguration.GetNozzlesForPump(SelectedPump.PumpId, SelectedDate);
        var connectedNozzles = SelectedConnectedPump != null
            ? PumpConfiguration.GetNozzlesForPump(SelectedConnectedPump.PumpId, SelectedDate)
            : Array.Empty<int>();
        var nozzles = primaryNozzles.Concat(connectedNozzles).ToList();

        var (hsdRate, msIRate, msIIRate, cngRate) = await _dsmService.GetCurrentRatesAsync();
        
        // Fetch previous shift closings for primary pump
        var previousResult = await _nozzleRepository.GetPreviousShiftClosingsAsync(SelectedDate, SelectedShift, SelectedPump.PumpId);
        var previousClosings = previousResult.Success ? previousResult.Data! : new Dictionary<int, double>();

        // Fetch previous shift closings for connected pump if active
        var connectedPreviousClosings = new Dictionary<int, double>();
        if (SelectedConnectedPump != null)
        {
            var connPrevResult = await _nozzleRepository.GetPreviousShiftClosingsAsync(SelectedDate, SelectedShift, SelectedConnectedPump.PumpId);
            if (connPrevResult.Success && connPrevResult.Data != null)
            {
                connectedPreviousClosings = connPrevResult.Data;
            }
        }

        var currentShiftImport = await GetShiftImportAsync(SelectedDate, SelectedShift);
        var currentAgsReadings = currentShiftImport?.NozzleReadings
            .ToDictionary(r => r.NozzleNumber, r => r);

        var previousShiftType = GetPreviousShiftType(SelectedShift);
        // Operationally: Shift B (Day) predecessor is yesterday's Shift A (Night)
        //                Shift A (Night) predecessor is same day's Shift B (Day)
        DateTime previousShiftDate = SelectedShift == "B" ? SelectedDate.AddDays(-1) : SelectedDate;
        var previousShiftImport = previousShiftType != null
            ? await GetShiftImportAsync(previousShiftDate, previousShiftType)
            : null;
        var previousAgsClosings = previousShiftImport?.NozzleReadings
            .ToDictionary(r => r.NozzleNumber, r => r.ClosingReading)
            ?? new Dictionary<int, double>();

        var currentShiftAutoFilledCount = 0;
        foreach (var n in nozzles)
        {
            var nozzlePumpId = PumpConfiguration.GetPumpIdForNozzle(n, SelectedDate);
            if (nozzlePumpId == 0) nozzlePumpId = SelectedPump.PumpId;

            var fuelType = PumpConfiguration.GetFuelType(nozzlePumpId, n, SelectedDate);
            var rate = fuelType switch
            {
                FuelType.HSD => hsdRate,
                FuelType.MS_I => msIRate,
                FuelType.MS_II => msIIRate,
                FuelType.CNG => cngRate,
                _ => msIRate
            };

            double? openingFromAgsPreviousShift = previousAgsClosings.TryGetValue(n, out var prevAgsClosing)
                ? prevAgsClosing
                : null;
            AgsNozzleReading? currentAgsRow = null;
            var hasCurrentAgs = currentAgsReadings != null && currentAgsReadings.TryGetValue(n, out currentAgsRow);
            
            var hasPreviousDsm = previousClosings.TryGetValue(n, out var previousDsmClosing)
                || connectedPreviousClosings.TryGetValue(n, out previousDsmClosing);

            double? openingReading = (hasCurrentAgs ? currentAgsRow?.OpeningReading : null)
                                     ?? openingFromAgsPreviousShift
                                     ?? (hasPreviousDsm ? previousDsmClosing : null);
            double? closingReading = hasCurrentAgs ? currentAgsRow?.ClosingReading : null;

            if (hasCurrentAgs && openingReading.HasValue && closingReading.HasValue)
            {
                currentShiftAutoFilledCount++;
            }

            var hasAutoOpening = openingReading.HasValue;
            string openingWarning = hasAutoOpening
                ? ""
                : $"No AGS/continuity opening found for Nozzle {n}. Please enter opening manually.";

            // Restore temp reading if available
            if (tempReadings.TryGetValue(n, out var temp))
            {
                if (temp.Opening.HasValue) openingReading = temp.Opening;
                if (temp.Closing.HasValue) closingReading = temp.Closing;
                if (temp.Rate.HasValue) rate = temp.Rate.Value;
            }

            NozzleReadings.Add(new NozzleReadingRow
            {
                NozzleNumber = n,
                FuelType = fuelType.ToDisplayName(),
                AutoOpeningReading = openingReading,
                OpeningReading = openingReading,
                ClosingReading = closingReading,
                OpeningWarning = openingWarning,
                Rate = rate,
                OnRowChanged = RecalculateAll
            });
            if (!hasAutoOpening)
            {
                OpeningWarnings.Add(openingWarning);
            }
        }

        if (currentShiftImport == null)
        {
            OpeningWarnings.Add($"No AGS import found for {SelectedDate:dd-MMM-yyyy} Shift {SelectedShift}. You can still enter readings manually.");
            AgsImportStatusText = $"No AGS import found for {SelectedDate:dd-MMM-yyyy} Shift {SelectedShift}. Manual entry mode is enabled.";
        }
        else if (currentAgsReadings != null)
        {
            OpeningWarnings.Add($"Loaded AGS readings for {SelectedDate:dd-MMM-yyyy} Shift {SelectedShift}; auto-filled {currentShiftAutoFilledCount}/{nozzles.Count} nozzle rows.");
            AgsImportStatusText = $"AGS import loaded for {SelectedDate:dd-MMM-yyyy} Shift {SelectedShift}: {currentShiftAutoFilledCount}/{nozzles.Count} nozzle rows auto-filled.";
            if (previousShiftType != null && previousShiftImport == null)
            {
                OpeningWarnings.Add($"No AGS import found for previous Shift {previousShiftType} on {SelectedDate:dd-MMM-yyyy}; continuity fallback applied.");
            }
        }

        UpdateTestingVisibility();
        RebuildTestingRows(hsdRate, msIRate, msIIRate, cngRate);
        await RefreshConnectedPumpGrossSalesAsync();
        RecalculateAll();
    }


    private void ReloadPumpOptions()
    {
        var currentPumpId = SelectedPump?.PumpId;
        PumpOptions.Clear();
        foreach (var item in PumpConfiguration.GetPumpDisplayItems(SelectedDate))
        {
            PumpOptions.Add(item);
        }
        
        // Try to keep the selected pump, or default to the first one
        if (currentPumpId.HasValue)
        {
            var match = PumpOptions.FirstOrDefault(p => p.PumpId == currentPumpId.Value);
            if (match != null)
            {
                SelectedPump = match;
            }
            else
            {
                SelectedPump = PumpOptions.FirstOrDefault()!;
            }
        }
        else
        {
            SelectedPump = PumpOptions.FirstOrDefault()!;
        }
        RefreshConnectablePumpOptions();
    }

    private void RefreshConnectablePumpOptions()
    {
        ConnectablePumpOptions.Clear();
        foreach (var pump in PumpOptions.Where(p => SelectedPump == null || p.PumpId != SelectedPump.PumpId))
        {
            ConnectablePumpOptions.Add(pump);
        }

        if (SelectedConnectedPump != null && ConnectablePumpOptions.All(p => p.PumpId != SelectedConnectedPump.PumpId))
        {
            SelectedConnectedPump = null;
        }
    }

    private async Task RefreshConnectedPumpGrossSalesAsync()
    {
        ConnectedPumpGrossSales = 0;
        ConnectedPumpStatus = "";
        if (SelectedConnectedPump == null || string.IsNullOrWhiteSpace(DsmName))
        {
            RecalculateAll();
            return;
        }

        var shiftResult = await App.Services.GetRequiredService<Core.Repositories.IShiftRepository>()
            .GetShiftAsync(SelectedDate, SelectedShift);
        if (!shiftResult.Success || shiftResult.Data == null)
        {
            ConnectedPumpStatus = $"Connected pump {SelectedConnectedPump.PumpId} will be included after that entry is saved.";
            RecalculateAll();
            return;
        }

        var entriesResult = await App.Services.GetRequiredService<IDsmEntryRepository>()
            .GetEntriesForShiftAsync(shiftResult.Data.ShiftId);
        if (!entriesResult.Success || entriesResult.Data == null)
        {
            RecalculateAll();
            return;
        }

        var connectedEntry = entriesResult.Data.FirstOrDefault(e =>
            e.PumpId == SelectedConnectedPump.PumpId
            && string.Equals(e.DsmName, DsmName, StringComparison.OrdinalIgnoreCase));

        if (connectedEntry == null)
        {
            ConnectedPumpStatus = $"Connected pump {SelectedConnectedPump.PumpId} entry not found yet for this shift.";
            RecalculateAll();
            return;
        }

        if (connectedEntry.NozzleReadings == null || connectedEntry.NozzleReadings.Count == 0)
        {
            var fullConnected = await App.Services.GetRequiredService<IDsmEntryRepository>()
                .GetFullEntryAsync(connectedEntry.DsmEntryId);
            if (fullConnected.Success && fullConnected.Data != null)
            {
                connectedEntry = fullConnected.Data;
            }
        }

        ConnectedPumpGrossSales = (connectedEntry.NozzleReadings ?? new List<NozzleReading>()).Sum(n => n.Amount);
        ConnectedPumpStatus = $"Including Pump {SelectedConnectedPump.PumpId} nozzle gross sale: ₹{ConnectedPumpGrossSales:N2}";
        RecalculateAll();
    }

    private async Task<AgsShiftImport?> GetShiftImportAsync(DateTime date, string shiftType)
    {
        var result = await _agsImportRepository.GetActiveShiftImportAsync(date, shiftType);
        if (result.Success)
        {
            return result.Data;
        }

        return null;
    }

    private static string? GetPreviousShiftType(string shiftType)
    {
        var normalized = (shiftType ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "A" => "B",   // Night/Morning predecessor is Day shift (same calendar date)
            "B" => "A",   // Day predecessor is Night/Morning shift (previous calendar date, handled by caller)
            "C" => "B",
            _ => null
        };
    }

    private void UpdateTestingVisibility()
    {
        ShowTesting = true;
        ShowMsTesting = true;
        ShowHsdTesting = true;
    }

    private void RebuildTestingRows(double hsdRate, double msIRate, double msIIRate, double cngRate)
    {
        TestingRows.Clear();
        foreach (var nozzle in NozzleReadings)
        {
            var tank = FuelPro.Core.Common.PumpConfiguration.GetTankName(SelectedPump?.PumpId ?? 1, nozzle.NozzleNumber, SelectedDate);
            TestingRows.Add(new TestingRow
            {
                NozzleNumber = nozzle.NozzleNumber,
                FuelType = $"Nozzle {nozzle.NozzleNumber} ({nozzle.FuelTypeLabel})",
                TankName = tank,
                Rate = nozzle.Rate,
                OnRowChanged = RecalculateAll
            });
        }
    }


    private List<TestingEntry> BuildTestingModels()
    {
        return TestingRows
            .Where(t => (t.Litres ?? 0) > 0)
            .Select(t => new TestingEntry
            {
                FuelType = t.NozzleNumber.ToString(),
                Litres = t.Litres ?? 0,
                Rate = t.Rate ?? 0,
                Amount = t.Amount
            })
            .ToList();
    }

    public void RecalculateAll()
    {
        TotalLitres = NozzleReadings.Sum(n => n.SaleLitres);
        var calc = _dsmCalculationService.Calculate(BuildCalculationDto());
        GrossSales = (double)calc.GrossSales;
        TotalPaymentIn = (double)calc.TotalInDirect;
        TotalDebtors = (double)calc.TotalCreditors;
        FinalAdjusted = (double)calc.TotalCollection;
        Difference = FinalAdjusted - GrossSales;
    }

    [RelayCommand]
    private void AddDebit() => Debits.Add(new DebitRow(Creditors, RecalculateAll));

    [RelayCommand]
    private void RemoveDebit(DebitRow? row) { if (row != null) Debits.Remove(row); RecalculateAll(); }

    [RelayCommand]
    private void AddExpense() => Expenses.Add(new ExpenseRow { OnRowChanged = RecalculateAll });

    [RelayCommand]
    private void RemoveExpense(ExpenseRow? row) { if (row != null) Expenses.Remove(row); RecalculateAll(); }

    [RelayCommand]
    private async Task SaveEntryAsync()
    {
        var validationErrors = ValidateBeforeSave();
        if (validationErrors.Count > 0)
        {
            ValidationSummary = string.Join(Environment.NewLine, validationErrors);
            StatusMessage = $"❌ Validation failed.{Environment.NewLine}{ValidationSummary}";
            MessageBox.Show(ValidationSummary, "Validation Summary", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsSaving = true;
        StatusMessage = "Saving...";
        try
        {
            var nozzleModels = NozzleReadings.Select(n => new NozzleReading
            {
                NozzleNumber = n.NozzleNumber,
                FuelType = n.FuelType,
                OpeningReading = n.OpeningReading ?? 0,
                ClosingReading = n.ClosingReading ?? 0,
                Rate = n.Rate ?? 0,
                IsManualOpeningOverride = n.IsManualOpeningOverride
            }).ToList();

            var payment = new PaymentCollection
            {
                PhonePeCardMorning = PhonePeCardMorning ?? 0,
                PhonePeCardNight = SelectedShift == "B" ? 0 : (PhonePeCardNight ?? 0),
                PhonePeMorning = PhonePeMorning ?? 0,
                PhonePeNight = SelectedShift == "B" ? 0 : (PhonePeNight ?? 0),
                CreditCardMorning = CreditCardMorning ?? 0,
                CreditCardNight = SelectedShift == "B" ? 0 : (CreditCardNight ?? 0),
                PetroCardMorning = PetroCardMorning ?? 0,
                PetroCardNight = SelectedShift == "B" ? 0 : (PetroCardNight ?? 0),
                Others = Others ?? 0,
                CashDeposit = CashDeposit ?? 0,
                CardTid = SelectedShift == "B" ? CreditCardTidMorning : CreditCardTidNight,
                CardBatch = SelectedShift == "B" ? CreditCardBatchMorning : CreditCardBatchNight,
                PhonePeTid = SelectedShift == "B" ? PhonePeTidMorning : PhonePeTidNight,
                PhonePeBatch = SelectedShift == "B" ? PhonePeBatchMorning : PhonePeBatchNight,
                PetroCardTid = SelectedShift == "B" ? PetroCardTidMorning : PetroCardTidNight,
                PetroCardBatch = SelectedShift == "B" ? PetroCardBatchMorning : PetroCardBatchNight,
                PhonePeTidMorning = PhonePeTidMorning,
                PhonePeBatchMorning = PhonePeBatchMorning,
                PhonePeTidNight = SelectedShift == "B" ? null : PhonePeTidNight,
                PhonePeBatchNight = SelectedShift == "B" ? null : PhonePeBatchNight,
                CreditCardTidMorning = CreditCardTidMorning,
                CreditCardBatchMorning = CreditCardBatchMorning,
                CreditCardTidNight = SelectedShift == "B" ? null : CreditCardTidNight,
                CreditCardBatchNight = SelectedShift == "B" ? null : CreditCardBatchNight,
                PetroCardTidMorning = PetroCardTidMorning,
                PetroCardBatchMorning = PetroCardBatchMorning,
                PetroCardTidNight = SelectedShift == "B" ? null : PetroCardTidNight,
                PetroCardBatchNight = SelectedShift == "B" ? null : PetroCardBatchNight
            };

            var debitModels = Debits.Where(d => !string.IsNullOrWhiteSpace(d.DebtorName))
                .Select(d => new DebitEntry 
                { 
                    DebtorName = d.DebtorName, 
                    Amount = d.Amount ?? 0, 
                    ChequeNo = d.ChequeNo,
                    VehicleNumber = d.VehicleNumber,
                    PaymentMethod = "Credit"
                }).ToList();

            var testingModels = BuildTestingModels();

            var expenseModels = Expenses.Where(e => !string.IsNullOrWhiteSpace(e.Description))
                .Select(e => new Expense { Description = e.Description, Amount = e.Amount ?? 0 }).ToList();

            var cashModels = new List<CashDenomination>
            {
                new() { CashType = "Cash1", Denom500 = Cash1.Denom500 ?? 0, Denom200 = Cash1.Denom200 ?? 0,
                    Denom100 = Cash1.Denom100 ?? 0, Denom50 = Cash1.Denom50 ?? 0, Denom20 = Cash1.Denom20 ?? 0,
                    Denom10 = Cash1.Denom10 ?? 0, Coins = Cash1.Coins ?? 0, TotalAmount = Cash1.TotalAmount },
                new() { CashType = "Cash2", Denom500 = Cash2.Denom500 ?? 0, Denom200 = Cash2.Denom200 ?? 0,
                    Denom100 = Cash2.Denom100 ?? 0, Denom50 = Cash2.Denom50 ?? 0, Denom20 = Cash2.Denom20 ?? 0,
                    Denom10 = Cash2.Denom10 ?? 0, Coins = Cash2.Coins ?? 0, TotalAmount = Cash2.TotalAmount }
            };

            var result = await _dsmService.SaveCompleteEntryAsync(
                SelectedDate, SelectedShift, DsmName, SelectedPump.PumpId,
                nozzleModels, payment, debitModels, testingModels, expenseModels, cashModels,
                SelectedConnectedPump?.PumpId,
                EditingEntryId,
                StartTime,
                EndTime,
                new List<DsmPersonalDebtor>());

            if (result.Success)
            {
                StatusMessage = "✅ DSM Entry saved successfully!";
                _draftService.ClearDraft();
                await LoadShiftEntriesAsync();

                // Trigger background sync now that transaction is fully committed
                var syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
                _ = syncEngine.ForceSyncAsync();
            }
            else
            {
                StatusMessage = $"❌ {result.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Save failed: {ex.Message}";
        }
        finally { IsSaving = false; }
    }


    [RelayCommand]
    private void ClearForm()
    {
        DsmName = "";
        EditingEntryId = null;
        StartTime = "08:00 AM";
        EndTime = "08:00 PM";
        PhonePeCardMorning = PhonePeCardNight = PhonePeMorning = PhonePeNight = CreditCardMorning = CreditCardNight = PetroCardMorning = PetroCardNight = Others = CashDeposit = null;
        CardTid = CardBatch = PhonePeTid = PhonePeBatch = PetroCardTid = PetroCardBatch = null;
        PhonePeTidMorning = PhonePeBatchMorning = PhonePeTidNight = PhonePeBatchNight = null;
        CreditCardTidMorning = CreditCardBatchMorning = CreditCardTidNight = CreditCardBatchNight = null;
        PetroCardTidMorning = PetroCardBatchMorning = PetroCardTidNight = PetroCardBatchNight = null;
        SelectedConnectedPump = null;
        ConnectedPumpGrossSales = 0;
        ConnectedPumpStatus = "";
        TestingRows.Clear();
        Debits.Clear();
        Expenses.Clear();
        NozzleReadings.Clear();
        Cash1 = new CashDenomRow { CashType = "Cash1", OnTotalChanged = RecalculateAll };
        Cash2 = new CashDenomRow { CashType = "Cash2", OnTotalChanged = RecalculateAll };
        LoadNozzlesForPump();
        StatusMessage = "Form cleared — ready for new entry";
    }

    [RelayCommand]
    private async Task EditEntryAsync(int dsmEntryId)
    {
        _isEditing = true;
        try
        {
            var repo = App.Services.GetRequiredService<IDsmEntryRepository>();
            var fullResult = await repo.GetFullEntryAsync(dsmEntryId);
            if (!fullResult.Success || fullResult.Data == null)
            {
                StatusMessage = $"❌ Failed to load entry: {fullResult.Error}";
                return;
            }

            var entry = fullResult.Data;

            // If this is a connected pump entry, redirect to the primary pump entry
            if (entry.ReconciledToPumpId.HasValue)
            {
                var shiftId = entry.ShiftId;
                var entriesResult = await repo.GetEntriesForShiftAsync(shiftId);
                if (entriesResult.Success && entriesResult.Data != null)
                {
                    var primaryRaw = entriesResult.Data.FirstOrDefault(e =>
                        e.PumpId == entry.ReconciledToPumpId.Value
                        && string.Equals(e.DsmName, entry.DsmName, StringComparison.OrdinalIgnoreCase));
                    if (primaryRaw != null)
                    {
                        var fullPrimary = await repo.GetFullEntryAsync(primaryRaw.DsmEntryId);
                        if (fullPrimary.Success && fullPrimary.Data != null)
                        {
                            entry = fullPrimary.Data;
                        }
                    }
                }
            }

            // Self-healing: if the primary entry's ConnectedPumpId is not set, check if any entry reconciles to it
            if (!entry.ConnectedPumpId.HasValue && !entry.ReconciledToPumpId.HasValue)
            {
                var shiftId = entry.ShiftId;
                var entriesResult = await repo.GetEntriesForShiftAsync(shiftId);
                if (entriesResult.Success && entriesResult.Data != null)
                {
                    var connectedRaw = entriesResult.Data.FirstOrDefault(e =>
                        e.ReconciledToPumpId == entry.PumpId
                        && string.Equals(e.DsmName, entry.DsmName, StringComparison.OrdinalIgnoreCase));
                    if (connectedRaw != null)
                    {
                        entry.ConnectedPumpId = connectedRaw.PumpId;
                    }
                }
            }

            // Set shift details first
            if (entry.Shift != null)
            {
                SelectedDate = entry.Shift.ShiftDate;
                SelectedShift = entry.Shift.ShiftType;
            }

            // Populate header
            EditingEntryId = entry.DsmEntryId;
            DsmName = entry.DsmName;
            StartTime = string.IsNullOrEmpty(entry.StartTime) ? "08:00 AM" : entry.StartTime;
            EndTime = string.IsNullOrEmpty(entry.EndTime) ? "08:00 PM" : entry.EndTime;

            // Find matching pump
            var pumpMatch = PumpOptions.FirstOrDefault(p => p.PumpId == entry.PumpId);
            if (pumpMatch != null) SelectedPump = pumpMatch;

            // Connected pump
            DsmEntry? connectedEntry = null;
            if (entry.ConnectedPumpId.HasValue)
            {
                var connMatch = ConnectablePumpOptions.FirstOrDefault(p => p.PumpId == entry.ConnectedPumpId.Value);
                SelectedConnectedPump = connMatch;

                var shiftId = entry.ShiftId;
                var entriesResult = await App.Services.GetRequiredService<IDsmEntryRepository>()
                    .GetEntriesForShiftAsync(shiftId);
                if (entriesResult.Success && entriesResult.Data != null)
                {
                    var rawConn = entriesResult.Data.FirstOrDefault(e =>
                        e.PumpId == entry.ConnectedPumpId.Value
                        && (e.ReconciledToPumpId == entry.PumpId || string.Equals(e.DsmName, entry.DsmName, StringComparison.OrdinalIgnoreCase)));
                    if (rawConn != null)
                    {
                        var fullConnected = await repo.GetFullEntryAsync(rawConn.DsmEntryId);
                        if (fullConnected.Success && fullConnected.Data != null)
                        {
                            connectedEntry = fullConnected.Data;
                        }
                    }
                }
            }
            else
            {
                SelectedConnectedPump = null;
            }

            // Explicitly load and await nozzles loading
            await LoadNozzlesForPumpAsync();

            // Populate payment
            var paymentSource = entry.PaymentCollection ?? connectedEntry?.PaymentCollection;
            PhonePeCardMorning = paymentSource?.PhonePeCardMorning;
            PhonePeCardNight = paymentSource?.PhonePeCardNight;
            PhonePeMorning = paymentSource?.PhonePeMorning;
            PhonePeNight = paymentSource?.PhonePeNight;
            CreditCardMorning = paymentSource?.CreditCardMorning;
            CreditCardNight = paymentSource?.CreditCardNight;
            PetroCardMorning = paymentSource?.PetroCardMorning;
            PetroCardNight = paymentSource?.PetroCardNight;
            Others = paymentSource?.Others;
            CashDeposit = paymentSource?.CashDeposit;

            CardTid = paymentSource?.CardTid;
            CardBatch = paymentSource?.CardBatch;
            PhonePeTid = paymentSource?.PhonePeTid;
            PhonePeBatch = paymentSource?.PhonePeBatch;
            PetroCardTid = paymentSource?.PetroCardTid;
            PetroCardBatch = paymentSource?.PetroCardBatch;
            PhonePeTidMorning = paymentSource?.PhonePeTidMorning ?? paymentSource?.PhonePeTid;
            PhonePeBatchMorning = paymentSource?.PhonePeBatchMorning ?? paymentSource?.PhonePeBatch;
            PhonePeTidNight = paymentSource?.PhonePeTidNight ?? paymentSource?.PhonePeTid;
            PhonePeBatchNight = paymentSource?.PhonePeBatchNight ?? paymentSource?.PhonePeBatch;

            CreditCardTidMorning = paymentSource?.CreditCardTidMorning ?? paymentSource?.CardTid;
            CreditCardBatchMorning = paymentSource?.CreditCardBatchMorning ?? paymentSource?.CardBatch;
            CreditCardTidNight = paymentSource?.CreditCardTidNight ?? paymentSource?.CardTid;
            CreditCardBatchNight = paymentSource?.CreditCardBatchNight ?? paymentSource?.CardBatch;

            PetroCardTidMorning = paymentSource?.PetroCardTidMorning ?? paymentSource?.PetroCardTid;
            PetroCardBatchMorning = paymentSource?.PetroCardBatchMorning ?? paymentSource?.PetroCardBatch;
            PetroCardTidNight = paymentSource?.PetroCardTidNight ?? paymentSource?.PetroCardTid;
            PetroCardBatchNight = paymentSource?.PetroCardBatchNight ?? paymentSource?.PetroCardBatch;

            // Populate nozzle readings (override auto-loaded ones)
            foreach (var nozzleRow in NozzleReadings)
            {
                var savedReading = entry.NozzleReadings.FirstOrDefault(n => n.NozzleNumber == nozzleRow.NozzleNumber);
                if (savedReading == null && connectedEntry != null)
                {
                    savedReading = connectedEntry.NozzleReadings.FirstOrDefault(n => n.NozzleNumber == nozzleRow.NozzleNumber);
                }

                if (savedReading != null)
                {
                    nozzleRow.OpeningReading = savedReading.OpeningReading;
                    nozzleRow.ClosingReading = savedReading.ClosingReading;
                    nozzleRow.Rate = savedReading.Rate;
                }
            }

            // Populate debits
            Debits.Clear();
            var allDebits = new List<DebitEntry>();
            if (entry.DebitEntries != null) allDebits.AddRange(entry.DebitEntries);
            if (connectedEntry?.DebitEntries != null) allDebits.AddRange(connectedEntry.DebitEntries);

            foreach (var debit in allDebits)
            {
                var row = new DebitRow(Creditors, RecalculateAll)
                {
                    DebtorName = debit.DebtorName,
                    ChequeNo = debit.ChequeNo,
                    Amount = debit.Amount,
                    VehicleNumber = debit.VehicleNumber,
                    PaymentMethod = string.IsNullOrEmpty(debit.PaymentMethod) ? "Credit" : debit.PaymentMethod,
                    CardTid = debit.CardTid,
                    CardBatch = debit.CardBatch,
                    Denom500 = debit.Denom500,
                    Denom200 = debit.Denom200,
                    Denom100 = debit.Denom100,
                    Denom50 = debit.Denom50,
                    Denom20 = debit.Denom20,
                    Denom10 = debit.Denom10,
                    Coins = debit.Coins
                };
                Debits.Add(row);
            }

            // Populate expenses
            Expenses.Clear();
            var allExpenses = new List<Expense>();
            if (entry.Expenses != null) allExpenses.AddRange(entry.Expenses);
            if (connectedEntry?.Expenses != null) allExpenses.AddRange(connectedEntry.Expenses);

            foreach (var expense in allExpenses)
            {
                Expenses.Add(new ExpenseRow
                {
                    Description = expense.Description,
                    Amount = expense.Amount,
                    OnRowChanged = RecalculateAll
                });
            }

            // Populate testing rows
            TestingRows.Clear();
            var (hsdRate, msIRate, msIIRate, cngRate) = await _dsmService.GetCurrentRatesAsync();
            RebuildTestingRows(hsdRate, msIRate, msIIRate, cngRate);

            var allTesting = new List<TestingEntry>();
            if (entry.TestingEntries != null) allTesting.AddRange(entry.TestingEntries);
            if (connectedEntry?.TestingEntries != null) allTesting.AddRange(connectedEntry.TestingEntries);

            foreach (var testEntry in allTesting)
            {
                int? nozzleNum = int.TryParse(testEntry.FuelType, out var num) ? num : (int?)null;
                TestingRow? match = null;
                if (nozzleNum.HasValue)
                {
                    match = TestingRows.FirstOrDefault(t => t.NozzleNumber == nozzleNum.Value);
                }
                else
                {
                    // Legacy fallback mapping
                    string canonicalFuel = testEntry.FuelType;
                    if (canonicalFuel == "MS")
                    {
                        match = TestingRows.FirstOrDefault(t => t.FuelType.Contains("MS-I") || t.FuelType.Contains("MS"));
                    }
                    else if (canonicalFuel == "HSD")
                    {
                        match = TestingRows.FirstOrDefault(t => t.FuelType.Contains("HSD") && !t.FuelType.Contains("HSD-II"));
                    }
                    else if (canonicalFuel == "HSD-II")
                    {
                        match = TestingRows.FirstOrDefault(t => t.FuelType.Contains("MS-II") || t.FuelType.Contains("HSD-II"));
                    }
                    else if (canonicalFuel == "CNG")
                    {
                        match = TestingRows.FirstOrDefault(t => t.FuelType.Contains("CNG"));
                    }
                }

                if (match != null)
                {
                    match.Litres = testEntry.Litres;
                    match.Rate = testEntry.Rate;
                }
            }

            // Populate cash denominations
            var cash1Data = entry.CashDenominations.FirstOrDefault(c => c.CashType == "Cash1")
                ?? connectedEntry?.CashDenominations.FirstOrDefault(c => c.CashType == "Cash1");
            Cash1 = new CashDenomRow
            {
                CashType = "Cash1",
                Denom500 = cash1Data?.Denom500,
                Denom200 = cash1Data?.Denom200,
                Denom100 = cash1Data?.Denom100,
                Denom50 = cash1Data?.Denom50,
                Denom20 = cash1Data?.Denom20,
                Denom10 = cash1Data?.Denom10,
                Coins = cash1Data?.Coins,
                TotalAmount = cash1Data?.TotalAmount ?? 0,
                OnTotalChanged = RecalculateAll
            };

            var cash2Data = entry.CashDenominations.FirstOrDefault(c => c.CashType == "Cash2")
                ?? connectedEntry?.CashDenominations.FirstOrDefault(c => c.CashType == "Cash2");
            Cash2 = new CashDenomRow
            {
                CashType = "Cash2",
                Denom500 = cash2Data?.Denom500,
                Denom200 = cash2Data?.Denom200,
                Denom100 = cash2Data?.Denom100,
                Denom50 = cash2Data?.Denom50,
                Denom20 = cash2Data?.Denom20,
                Denom10 = cash2Data?.Denom10,
                Coins = cash2Data?.Coins,
                TotalAmount = cash2Data?.TotalAmount ?? 0,
                OnTotalChanged = RecalculateAll
            };

            RecalculateAll();
            StatusMessage = $"📝 Editing entry for {DsmName} on Pump {entry.PumpId}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Failed to load entry for editing: {ex.Message}";
        }
        finally
        {
            _isEditing = false;
        }
    }


    [RelayCommand]
    private async Task LoadShiftEntriesAsync()
    {
        var shiftResult = await App.Services.GetRequiredService<Core.Repositories.IShiftRepository>()
            .GetShiftAsync(SelectedDate, SelectedShift);
        if (!shiftResult.Success) return;

        var summaries = await _dsmService.GetShiftEntrySummariesAsync(shiftResult.Data!.ShiftId);
        ShiftEntries.Clear();
        if (summaries.Success)
            foreach (var s in summaries.Data!) ShiftEntries.Add(s);
    }

    private async Task LoadSuggestionsAsync()
    {
        var result = await _dsmProfileRepo.GetAllAsync();
        DsmOptions.Clear();
        if (result.Success)
        {
            foreach (var profile in result.Data!) DsmOptions.Add(profile.DsmName);
        }

        var creditorsResult = await _creditorRepo.GetAllActiveWithVehiclesAsync();
        Creditors.Clear();
        if (creditorsResult.Success)
        {
            foreach (var c in creditorsResult.Data!)
                Creditors.Add(c);
        }
    }

    private void SaveDraft()
    {
        try
        {
            var draft = new
            {
                SelectedDate, SelectedShift, DsmName, PumpId = SelectedPump?.PumpId ?? 1,
                PhonePeCardMorning, PhonePeCardNight, PhonePeMorning, PhonePeNight, CreditCardMorning, CreditCardNight, PetroCardMorning, PetroCardNight,
                TestingRows = TestingRows.Select(t => new { t.FuelType, t.Litres, t.Rate, t.Amount }).ToList()
            };
            _draftService.SaveDraft(JsonConvert.SerializeObject(draft));
        }
        catch { /* draft save is best-effort */ }
    }

    private DsmEntryDto BuildCalculationDto()
    {
        return new DsmEntryDto
        {
            NozzleReadings = NozzleReadings.Select(x => new NozzleReadingDto { Amount = (decimal)x.Amount }).ToList(),
            PaymentCollection = new PaymentCollectionDto
            {
                // Others is NOT included in TotalInDirect — it is informational only
                PhonePe = (decimal)((PhonePeMorning ?? 0) + (PhonePeNight ?? 0) + (PhonePeCardMorning ?? 0) + (PhonePeCardNight ?? 0)),
                CreditCard = (decimal)((CreditCardMorning ?? 0) + (CreditCardNight ?? 0) + (PetroCardMorning ?? 0) + (PetroCardNight ?? 0)),
                CashDeposit = (decimal)(CashDeposit ?? 0),
                PhysicalCash = (decimal)(Cash1.TotalAmount + Cash2.TotalAmount)
            },
            DebitEntries = Debits.Select(x => new DebitEntryDto { Amount = (decimal)(x.Amount ?? 0), ChequeNo = x.ChequeNo }).ToList(),
            Expenses = Expenses.Select(x => new ExpenseDto { Amount = (decimal)(x.Amount ?? 0) }).ToList(),
            TestingEntries = TestingRows
                .Where(x => x.Amount > 0)
                .Select(x => new TestingEntryDto
                {
                    FuelType = x.FuelType,
                    Litres = (decimal)(x.Litres ?? 0),
                    Rate = (decimal)(x.Rate ?? 0),
                    Amount = (decimal)x.Amount
                })
                .ToList()
        };
    }

    private List<string> ValidateBeforeSave()
    {
        var errors = new List<string>();
        foreach (var row in NozzleReadings)
        {
            row.HasValidationError = false;
            if (!row.OpeningReading.HasValue || !row.ClosingReading.HasValue || !row.Rate.HasValue)
            {
                errors.Add($"Invalid numeric value in Nozzle {row.NozzleNumber} row.");
                row.HasValidationError = true;
                continue;
            }
            if (double.IsNaN(row.OpeningReading.Value) || double.IsNaN(row.ClosingReading.Value) || double.IsNaN(row.Rate.Value))
            {
                errors.Add($"Invalid numeric value in Nozzle {row.NozzleNumber} row.");
                row.HasValidationError = true;
            }
            if (row.ClosingReading.Value < row.OpeningReading.Value)
            {
                errors.Add($"Closing reading cannot be less than opening reading for Nozzle {row.NozzleNumber}.");
                row.HasValidationError = true;
            }
            if (row.SaleLitres < 0)
            {
                errors.Add($"Sale litres cannot be negative for Nozzle {row.NozzleNumber}.");
                row.HasValidationError = true;
            }
            if (row.Rate.Value <= 0)
            {
                errors.Add($"Rate must be greater than zero for Nozzle {row.NozzleNumber}.");
                row.HasValidationError = true;
            }
        }

        foreach (var debit in Debits)
        {
            if (string.IsNullOrWhiteSpace(debit.DebtorName)) errors.Add("Debtor name is required.");
            if (!debit.Amount.HasValue || debit.Amount.Value <= 0) errors.Add($"Debit amount must be greater than zero for {debit.DebtorName}.");
        }
        foreach (var expense in Expenses)
        {
            if (string.IsNullOrWhiteSpace(expense.Description)) errors.Add("Expense description is required.");
            if (!expense.Amount.HasValue || expense.Amount.Value <= 0) errors.Add($"Expense amount must be greater than zero for {expense.Description}.");
        }
        if (string.IsNullOrWhiteSpace(DsmName)) errors.Add("DSM Name is required.");
        return errors;
    }

    // ── Print Commands ────────────────────────────────────────────────────────

    [RelayCommand]
    private void PrintDsm()
    {
        if (string.IsNullOrWhiteSpace(DsmName))
        {
            MessageBox.Show("Please enter a DSM Name before printing.", "Print", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var settings = App.Services.GetRequiredService<ISettingsRepository>();
        var settingsResult = settings.GetSettingsAsync().GetAwaiter().GetResult();
        var stationName = settingsResult.Success ? settingsResult.Data?.PumpStationName ?? "PyroSync" : "PyroSync";

        var nozzleRows = NozzleReadings.Select(n => new DsmNozzlePrintRow
        {
            NozzleNumber  = n.NozzleNumber,
            FuelType      = n.FuelType,
            OpeningReading = n.OpeningReading ?? 0,
            ClosingReading = n.ClosingReading ?? 0,
            SaleLitres    = n.SaleLitres,
            Rate          = n.Rate ?? 0,
            Amount        = n.Amount
        }).ToList();

        var payments = new DsmPaymentPrintBlock
        {
            PhonePeCardMorning = PhonePeCardMorning ?? 0,
            PhonePeCardNight   = PhonePeCardNight   ?? 0,
            PhonePeMorning     = PhonePeMorning     ?? 0,
            PhonePeNight       = PhonePeNight       ?? 0,
            CreditCardMorning  = CreditCardMorning  ?? 0,
            CreditCardNight    = CreditCardNight    ?? 0,
            PetroCard          = (PetroCardMorning ?? 0) + (PetroCardNight ?? 0),
            CashDeposit        = CashDeposit        ?? 0,
            Others             = Others             ?? 0,
            BankDeposit        = Cash1.TotalAmount,
            TotalDigital       = (PhonePeCardMorning ?? 0) + (PhonePeCardNight ?? 0)
                               + (PhonePeMorning ?? 0)     + (PhonePeNight ?? 0)
                               + (CreditCardMorning ?? 0)  + (CreditCardNight ?? 0)
                               + (PetroCardMorning ?? 0)   + (PetroCardNight ?? 0)
                               + (CashDeposit ?? 0),
            CardTid            = CardTid ?? "",
            CardBatch          = CardBatch ?? "",
            PhonePeTid         = PhonePeTid ?? "",
            PhonePeBatch       = PhonePeBatch ?? "",
            PetroCardTid       = PetroCardTid ?? "",
            PetroCardBatch     = PetroCardBatch ?? ""
        };

        var cashDenom = new DsmCashDenomPrintBlock
        {
            Qty500 = Cash2.Denom500 ?? 0,
            Amt500 = (Cash2.Denom500 ?? 0) * 500.0,
            Qty200 = Cash2.Denom200 ?? 0,
            Amt200 = (Cash2.Denom200 ?? 0) * 200.0,
            Qty100 = Cash2.Denom100 ?? 0,
            Amt100 = (Cash2.Denom100 ?? 0) * 100.0,
            Qty50  = Cash2.Denom50  ?? 0,
            Amt50  = (Cash2.Denom50  ?? 0) * 50.0,
            Qty20  = Cash2.Denom20  ?? 0,
            Amt20  = (Cash2.Denom20  ?? 0) * 20.0,
            Qty10  = Cash2.Denom10  ?? 0,
            Amt10  = (Cash2.Denom10  ?? 0) * 10.0,
            Coins  = Cash2.Coins    ?? 0,
            Total  = Cash2.TotalAmount
        };

        var creditors = Debits.Where(d => !string.IsNullOrWhiteSpace(d.DebtorName))
            .Select(d => new DsmCreditorPrintRow { DebtorName = d.DebtorName, ChequeNo = d.ChequeNo ?? "", Amount = d.Amount ?? 0 }).ToList();

        var expenses = Expenses.Where(e => !string.IsNullOrWhiteSpace(e.Description))
            .Select(e => new DsmExpensePrintRow { Description = e.Description, Amount = e.Amount ?? 0 }).ToList();

        var testing = TestingRows.Where(t => t.Amount > 0)
            .Select(t => new DsmTestingPrintRow { FuelType = t.FuelType, Litres = t.Litres ?? 0, Rate = t.Rate ?? 0, Amount = t.Amount }).ToList();

        var recon = new DsmReconciliationPrintBlock
        {
            GrossSales      = GrossSales,
            TotalCollection = FinalAdjusted,
            Creditors       = TotalDebtors,
            Testing         = TestingRows.Sum(t => t.Amount),
            Expenses        = Expenses.Sum(e => e.Amount ?? 0),
            Cash            = Cash2.TotalAmount,
            Mismatch        = Difference
        };

        var personalDebtorPrintRows = new List<DsmPersonalDebtorPrintRow>();

        var printData = new DsmSheetPrintData
        {
            StationName  = stationName,
            CompanyName  = "PyroSync",
            Date         = SelectedDate.ToString("dd/MM/yyyy"),
            Shift        = SelectedShift,
            DsmName      = DsmName,
            PumpNo       = SelectedPump?.DisplayText ?? "",
            PrintedAt    = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
            StartTime    = StartTime,
            EndTime      = EndTime,
            NozzleRows   = nozzleRows,
            TotalLitres  = TotalLitres,
            GrossSales   = GrossSales,
            Payments     = payments,
            CashDenom    = cashDenom,
            Creditors    = creditors,
            TotalCreditors = TotalDebtors,
            Expenses     = expenses,
            TotalExpenses = Expenses.Sum(e => e.Amount ?? 0),
            Testing      = testing,
            TotalTesting = TestingRows.Sum(t => t.Amount),
            Reconciliation = recon,
            PersonalDebtors = personalDebtorPrintRows,
            TotalPersonalDebtors = 0
        };

        _printService.PrintDsmSheet(printData);
    }

    [RelayCommand]
    private void PrintShiftSummary()
    {
        if (ShiftEntries.Count == 0)
        {
            MessageBox.Show("No shift entries loaded. Click 'View Shift Entries' first.", "Print",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var settings = App.Services.GetRequiredService<ISettingsRepository>();
        var settingsResult = settings.GetSettingsAsync().GetAwaiter().GetResult();
        var stationName = settingsResult.Success ? settingsResult.Data?.PumpStationName ?? "PyroSync" : "PyroSync";

        var rows = ShiftEntries.Select(e => new ShiftSummaryDsmRow
        {
            DsmName        = e.DsmName,
            PumpId         = e.PumpId,
            GrossSales     = e.GrossSales,
            TotalCollection = e.TotalPaymentIn,
            Mismatch       = e.Difference
        }).ToList();

        var printData = new ShiftSummaryPrintData
        {
            StationName    = stationName,
            Date           = SelectedDate.ToString("dd/MM/yyyy"),
            Shift          = SelectedShift,
            PrintedAt      = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
            Rows           = rows,
            TotalSales     = rows.Sum(r => r.GrossSales),
            TotalCollection = rows.Sum(r => r.TotalCollection),
            TotalMismatch  = rows.Sum(r => r.Mismatch)
        };

        _printService.PrintShiftSummary(printData);
    }
}
