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

    [ObservableProperty] private int _pumpId;
    public string PumpLabel => PumpId > 0 ? $"Pump {PumpId}" : "";
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
        if (!string.IsNullOrWhiteSpace(value))
        {
            var creditor = _creditors.FirstOrDefault(c => string.Equals(c.Name, value, StringComparison.OrdinalIgnoreCase));
            if (creditor != null && creditor.Vehicles != null)
            {
                foreach (var v in creditor.Vehicles.Where(x => x.IsActive))
                {
                    AvailableVehicles.Add(v.VehicleNumber);
                }
            }
        }
        OnRowChanged?.Invoke();
    }

    partial void OnAmountChanged(double? value) => OnRowChanged?.Invoke();
    partial void OnChequeNoChanged(string? value) => OnRowChanged?.Invoke();
    partial void OnVehicleNumberChanged(string? value) => OnRowChanged?.Invoke();
    partial void OnPaymentMethodChanged(string value) => OnRowChanged?.Invoke();
}

public partial class ExpenseRow : ObservableObject
{
    public Action? OnRowChanged { get; set; }

    [ObservableProperty] private string _description = "";
    [ObservableProperty] private double? _amount;

    partial void OnDescriptionChanged(string value) => OnRowChanged?.Invoke();
    partial void OnAmountChanged(double? value) => OnRowChanged?.Invoke();
}

public partial class KhandharePetroleumRow : ObservableObject
{
    public Action? OnRowChanged { get; set; }

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _vehicleNumber = "";
    [ObservableProperty] private string _slipNumber = "";
    [ObservableProperty] private double? _amount;

    partial void OnNameChanged(string value) => OnRowChanged?.Invoke();
    partial void OnVehicleNumberChanged(string value) => OnRowChanged?.Invoke();
    partial void OnAmountChanged(double? value) => OnRowChanged?.Invoke();
    partial void OnSlipNumberChanged(string value) => OnRowChanged?.Invoke();
}

public partial class QrPaymentRow : ObservableObject
{
    public Action? OnRowChanged { get; set; }

    [ObservableProperty] private string _targetDsmName = "";
    [ObservableProperty] private double? _amount;
    [ObservableProperty] private string? _tid;
    [ObservableProperty] private string? _batch;
    [ObservableProperty] private string? _slot;

    partial void OnTargetDsmNameChanged(string value) => OnRowChanged?.Invoke();
    partial void OnAmountChanged(double? value) => OnRowChanged?.Invoke();
    partial void OnTidChanged(string? value) => OnRowChanged?.Invoke();
    partial void OnBatchChanged(string? value) => OnRowChanged?.Invoke();
    partial void OnSlotChanged(string? value) => OnRowChanged?.Invoke();
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

public partial class DynamicCollectionEntryRow : ObservableObject
{
    public string Code { get; set; } = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _category = "Online";
    [ObservableProperty] private bool _hasTidBatch;
    [ObservableProperty] private double? _amount;
    [ObservableProperty] private string? _tid;
    [ObservableProperty] private string? _batch;
    public Action? OnValueChangedAction { get; set; }

    partial void OnAmountChanged(double? value) => OnValueChangedAction?.Invoke();
    partial void OnTidChanged(string? value) => OnValueChangedAction?.Invoke();
    partial void OnBatchChanged(string? value) => OnValueChangedAction?.Invoke();
}

public partial class DsmEntryViewModel : ObservableObject
{
    private readonly DsmEntryService _dsmService;
    private readonly DraftService _draftService;
    private readonly IDsmCalculationService _dsmCalculationService;
    private readonly Core.Repositories.INozzleReadingRepository _nozzleRepository;
    private readonly IAgsImportRepository _agsImportRepository;
    private readonly ICollectionTypeService _collectionTypeService;
    private readonly IFeatureToggleService _featureToggleService;
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly PrintService _printService;


    // Header fields
    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private string _selectedShift = "A";
    [ObservableProperty] private string _dsmName = "";
    [ObservableProperty] private PumpDisplayItem _selectedPump = null!;
    [ObservableProperty] private PumpDisplayItem? _selectedConnectedPump;
    [ObservableProperty] private List<int> _activeConnectedPumpIds = new();
    public string ActiveConnectedPumpsDisplay
    {
        get
        {
            if (ActiveConnectedPumpIds != null && ActiveConnectedPumpIds.Count > 0)
            {
                return $"Pump {string.Join(" + Pump ", ActiveConnectedPumpIds)}";
            }
            return SelectedConnectedPump != null ? SelectedConnectedPump.DisplayText : "None";
        }
    }
    [ObservableProperty] private double _connectedPumpGrossSales;
    [ObservableProperty] private string _connectedPumpStatus = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _dsmCumulativeShiftSummaryMessage = "";
    [ObservableProperty] private bool _isSaving;
    [ObservableProperty] private bool _isSaved;
    public bool IsNotSaving => !IsSaving;
    public bool CanSaveCurrentEntry => !IsSaving && !IsSaved;

    partial void OnIsSavedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSaveCurrentEntry));
        SaveEntryCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSavingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotSaving));
        OnPropertyChanged(nameof(CanSaveCurrentEntry));
        SaveEntryCommand.NotifyCanExecuteChanged();
    }
    [ObservableProperty] private int? _editingEntryId;
    [ObservableProperty] private string _startTime = "08:00 AM";
    [ObservableProperty] private string _endTime = "08:00 PM";
    private double? _savedEntryGrossSales;

    // Nozzle readings
    public ObservableCollection<NozzleReadingRow> NozzleReadings { get; } = new();

    [ObservableProperty] private string _phonePeDisplayName = "PhonePe";
    [ObservableProperty] private bool _phonePeHasTidBatch = true;
    [ObservableProperty] private bool _phonePeIsActive = true;

    [ObservableProperty] private string _creditCardDisplayName = "Credit / Debit Card";
    [ObservableProperty] private bool _creditCardHasTidBatch = true;
    [ObservableProperty] private bool _creditCardIsActive = true;

    [ObservableProperty] private string _petroCardDisplayName = "PetroCard";
    [ObservableProperty] private bool _petroCardHasTidBatch = true;
    [ObservableProperty] private bool _petroCardIsActive = true;

    public bool IsPhonePeNightTidVisible => IsMorningNightSplitEnabled && PhonePeHasTidBatch;
    public bool IsCreditCardNightTidVisible => IsMorningNightSplitEnabled && CreditCardHasTidBatch;
    public bool IsPetroCardNightTidVisible => IsMorningNightSplitEnabled && PetroCardHasTidBatch;

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
    public ObservableCollection<DynamicCollectionEntryRow> DynamicCollections { get; } = new();
    public ObservableCollection<DebitRow> Debits { get; } = new();
    public ObservableCollection<ExpenseRow> Expenses { get; } = new();
    public ObservableCollection<KhandharePetroleumRow> KhandharePetroleumEntries { get; } = new();
    public ObservableCollection<QrPaymentRow> QrPayments { get; } = new();

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
    [ObservableProperty] private double _meterGrossSales;
    [ObservableProperty] private double _totalTestingAmount;
    [ObservableProperty] private double _netGrossSales;
    [ObservableProperty] private string _meterGrossSalesSubtitle = string.Empty;
    [ObservableProperty] private bool _hasTesting;
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
    private readonly IStationConfigurationService _stationConfigService;

    public bool IsCrossDsmQrEnabled => _featureToggleService?.IsFeatureEnabled("Operations_CrossDsmQr", true) ?? true;
    public bool IsPersonalLedgerEnabled => _featureToggleService?.IsFeatureEnabled("Operations_PersonalLedger", true) ?? true;
    public string PersonalLedgerTitle => _featureToggleService?.GetFeatureDisplayName("Operations_PersonalLedger", "Personal Ledger") ?? "Personal Ledger";

    public DsmEntryViewModel()
    {
        _dsmService = App.Services.GetRequiredService<DsmEntryService>();
        _draftService = App.Services.GetRequiredService<DraftService>();
        _dsmCalculationService = App.Services.GetRequiredService<IDsmCalculationService>();
        _nozzleRepository = App.Services.GetRequiredService<Core.Repositories.INozzleReadingRepository>();
        _agsImportRepository = App.Services.GetRequiredService<IAgsImportRepository>();
        _dsmProfileRepo = App.Services.GetRequiredService<IDsmProfileRepository>();
        _creditorRepo = App.Services.GetRequiredService<ICreditorRepository>();
        _collectionTypeService = App.Services.GetService<ICollectionTypeService>()!;
        _featureToggleService = App.Services.GetService<IFeatureToggleService>()!;
        _stationConfigService = App.Services.GetService<IStationConfigurationService>()!;
        _printService = App.Services.GetRequiredService<PrintService>();

        Cash1 = new CashDenomRow { CashType = "Cash1", OnTotalChanged = RecalculateAll };
        Cash2 = new CashDenomRow { CashType = "Cash2", OnTotalChanged = RecalculateAll };

        if (_featureToggleService != null)
        {
            _featureToggleService.FeatureConfigurationChanged += () =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    OnPropertyChanged(nameof(IsMorningNightSplitEnabled));
                    OnPropertyChanged(nameof(MorningLabel));
                    OnPropertyChanged(nameof(MorningTidLabel));
                    OnPropertyChanged(nameof(MorningBatchLabel));
                    OnPropertyChanged(nameof(IsCrossDsmQrEnabled));
                    OnPropertyChanged(nameof(IsPersonalLedgerEnabled));
                    OnPropertyChanged(nameof(PersonalLedgerTitle));
                    _ = LoadDynamicCollectionTypesAsync();
                });
            };
        }

        if (_stationConfigService != null)
        {
            _stationConfigService.StationConfigurationChanged += () =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(() =>
                    {
                        ReloadPumpOptions();
                        if (!_isEditing) LoadNozzlesForPump();
                    });
                }
                else
                {
                    ReloadPumpOptions();
                    if (!_isEditing) LoadNozzlesForPump();
                }
            };
        }

        if (_collectionTypeService != null)
        {
            _collectionTypeService.CollectionTypesChanged += () =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(async () =>
                    {
                        await LoadDynamicCollectionTypesAsync();
                        RecalculateAll();
                    });
                }
                else
                {
                    _ = LoadDynamicCollectionTypesAsync();
                    RecalculateAll();
                }
            };
        }

        FuelPro.Core.Services.DsmEntryService.DsmProfileChanged += () =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(async () => await LoadSuggestionsAsync());
            }
            else
            {
                _ = LoadSuggestionsAsync();
            }
        };

        FuelPro.Core.Services.DsmEntryService.DebtorChanged += () =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(async () => await LoadSuggestionsAsync());
            }
            else
            {
                _ = LoadSuggestionsAsync();
            }
        };

        // Populate pump options for default selected date
        ReloadPumpOptions();

        // Auto-save timer
        if (System.Windows.Application.Current != null)
        {
            _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _autoSaveTimer.Tick += (_, _) => SaveDraft();
            _autoSaveTimer.Start();
        }

        LoadNozzlesForPump();
        _ = LoadSuggestionsAsync();
        _ = LoadDynamicCollectionTypesAsync();
    }

    public async Task LoadDynamicCollectionTypesAsync(PaymentCollection? existingPayment = null)
    {
        try
        {
            var allTypes = _collectionTypeService != null
                ? await _collectionTypeService.GetAllCollectionTypesAsync()
                : new List<CollectionTypeMaster>();

            if (allTypes == null || allTypes.Count == 0)
            {
                var now = DateTime.Now;
                allTypes = new List<CollectionTypeMaster>
                {
                    new() { Code = "PHONEPE", DisplayName = "PhonePe", Category = "Online", HasTidBatch = true, DisplayOrder = 1, IsActive = true, IsSystem = true, CreatedAt = now },
                    new() { Code = "CREDIT_CARD", DisplayName = "Credit / Debit Card", Category = "Card", HasTidBatch = true, DisplayOrder = 2, IsActive = true, IsSystem = true, CreatedAt = now },
                    new() { Code = "PETROCARD", DisplayName = "PetroCard", Category = "Card", HasTidBatch = true, DisplayOrder = 3, IsActive = true, IsSystem = true, CreatedAt = now },
                    new() { Code = "SBI_REDEEM", DisplayName = "SBI Redeem", Category = "Card", HasTidBatch = true, DisplayOrder = 4, IsActive = true, IsSystem = false, CreatedAt = now },
                    new() { Code = "PAYTM", DisplayName = "Paytm", Category = "Online", HasTidBatch = true, DisplayOrder = 5, IsActive = true, IsSystem = false, CreatedAt = now },
                    new() { Code = "QR", DisplayName = "QR / Online", Category = "Online", HasTidBatch = true, DisplayOrder = 6, IsActive = true, IsSystem = false, CreatedAt = now },
                    new() { Code = "MOBIKWIK", DisplayName = "Mobikwik", Category = "Online", HasTidBatch = true, DisplayOrder = 7, IsActive = true, IsSystem = false, CreatedAt = now },
                    new() { Code = "CASH_DEPOSIT", DisplayName = "Cash Deposit (Bank)", Category = "Cash", HasTidBatch = false, DisplayOrder = 8, IsActive = true, IsSystem = true, CreatedAt = now },
                    new() { Code = "OTHERS", DisplayName = "Other Online / UPI", Category = "Other", HasTidBatch = false, DisplayOrder = 9, IsActive = true, IsSystem = true, CreatedAt = now }
                };
            }

            // 1. Resolve built-in PhonePe
            var ph = allTypes.FirstOrDefault(c => string.Equals(c.Code, "PHONEPE", StringComparison.OrdinalIgnoreCase));
            if (ph != null)
            {
                PhonePeDisplayName = ph.DisplayName;
                PhonePeHasTidBatch = ph.HasTidBatch;
                PhonePeIsActive = ph.IsActive;
            }
            else
            {
                PhonePeDisplayName = "PhonePe";
                PhonePeHasTidBatch = true;
                PhonePeIsActive = true;
            }

            // 2. Resolve built-in Credit Card / PineLabs
            var cc = allTypes.FirstOrDefault(c => string.Equals(c.Code, "CREDIT_CARD", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(c.Code, "CREDITCARD", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(c.Code, "CREDIT CARD", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(c.Code, "PINELAB", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(c.Code, "PINELABS", StringComparison.OrdinalIgnoreCase)
                                            || c.Code.IndexOf("PINELAB", StringComparison.OrdinalIgnoreCase) >= 0
                                            || c.Code.IndexOf("CARD", StringComparison.OrdinalIgnoreCase) >= 0);
            if (cc != null)
            {
                CreditCardDisplayName = cc.DisplayName;
                CreditCardHasTidBatch = cc.HasTidBatch;
                CreditCardIsActive = cc.IsActive;
            }
            else
            {
                CreditCardDisplayName = "Credit / Debit Card";
                CreditCardHasTidBatch = true;
                CreditCardIsActive = true;
            }

            // 3. Resolve built-in PetroCard
            var pc = allTypes.FirstOrDefault(c => string.Equals(c.Code, "PETROCARD", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(c.Code, "PETRO_CARD", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(c.Code, "PETRO CARD", StringComparison.OrdinalIgnoreCase));
            if (pc != null)
            {
                PetroCardDisplayName = pc.DisplayName;
                PetroCardHasTidBatch = pc.HasTidBatch;
                PetroCardIsActive = pc.IsActive;
            }
            else
            {
                PetroCardDisplayName = "PetroCard";
                PetroCardHasTidBatch = true;
                PetroCardIsActive = true;
            }

            OnPropertyChanged(nameof(PhonePeDisplayName));
            OnPropertyChanged(nameof(PhonePeHasTidBatch));
            OnPropertyChanged(nameof(PhonePeIsActive));
            OnPropertyChanged(nameof(CreditCardDisplayName));
            OnPropertyChanged(nameof(CreditCardHasTidBatch));
            OnPropertyChanged(nameof(CreditCardIsActive));
            OnPropertyChanged(nameof(PetroCardDisplayName));
            OnPropertyChanged(nameof(PetroCardHasTidBatch));
            OnPropertyChanged(nameof(PetroCardIsActive));
            OnPropertyChanged(nameof(IsPhonePeNightTidVisible));
            OnPropertyChanged(nameof(IsCreditCardNightTidVisible));
            OnPropertyChanged(nameof(IsPetroCardNightTidVisible));

            var existingItems = existingPayment?.Items?.ToList() ?? new List<PaymentCollectionItem>();
            var matchedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ph != null) matchedCodes.Add(ph.Code);
            if (cc != null) matchedCodes.Add(cc.Code);
            if (pc != null) matchedCodes.Add(pc.Code);
            matchedCodes.Add("CASH_DEPOSIT");
            matchedCodes.Add("OTHERS");

            var targetTypes = allTypes
                .Where(x => x.IsActive && !matchedCodes.Contains(x.Code))
                .OrderBy(x => x.DisplayOrder)
                .ToList();

            // Synchronize DynamicCollections in place without clearing collection types
            for (int i = DynamicCollections.Count - 1; i >= 0; i--)
            {
                var row = DynamicCollections[i];
                if (!targetTypes.Any(t => string.Equals(t.Code, row.Code, StringComparison.OrdinalIgnoreCase)))
                {
                    DynamicCollections.RemoveAt(i);
                }
            }

            for (int i = 0; i < targetTypes.Count; i++)
            {
                var t = targetTypes[i];
                var existingRow = DynamicCollections.FirstOrDefault(d => string.Equals(d.Code, t.Code, StringComparison.OrdinalIgnoreCase));
                var existingItem = existingItems.FirstOrDefault(item => string.Equals(item.CollectionTypeCode, t.Code, StringComparison.OrdinalIgnoreCase));

                if (existingRow != null)
                {
                    existingRow.DisplayName = t.DisplayName;
                    existingRow.Category = t.Category;
                    existingRow.HasTidBatch = t.HasTidBatch;
                    if (existingPayment != null)
                    {
                        existingRow.Amount = (existingItem != null && existingItem.Amount > 0) ? existingItem.Amount : (double?)null;
                        existingRow.Tid = existingItem?.Tid;
                        existingRow.Batch = existingItem?.Batch;
                    }
                }
                else
                {
                    var newRow = new DynamicCollectionEntryRow
                    {
                        Code = t.Code,
                        DisplayName = t.DisplayName,
                        Category = t.Category,
                        HasTidBatch = t.HasTidBatch,
                        Amount = (existingItem != null && existingItem.Amount > 0) ? existingItem.Amount : (double?)null,
                        Tid = existingItem?.Tid,
                        Batch = existingItem?.Batch,
                        OnValueChangedAction = RecalculateAll
                    };
                    DynamicCollections.Insert(Math.Min(i, DynamicCollections.Count), newRow);
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load dynamic collection types in DsmEntryViewModel");
        }
    }



    partial void OnSelectedPumpChanged(PumpDisplayItem value)
    {
        if (value != null)
        {
            RefreshConnectablePumpOptions();
            if (!_isEditing)
            {
                _ = HandlePumpSelectionAsync(value);
            }
        }
    }

    private async Task HandlePumpSelectionAsync(PumpDisplayItem value)
    {
        await ResolveActiveConnectionGroupAsync();
        await LoadNozzlesForPumpAsync();
    }

    private async Task ResolveActiveConnectionGroupAsync()
    {
        if (SelectedPump == null)
        {
            ActiveConnectedPumpIds = new List<int>();
            SelectedConnectedPump = null;
            return;
        }

        try
        {
            if (_stationConfigService != null)
            {
                var config = await _stationConfigService.GetPumpConnectionConfigurationAsync();
                if (config != null && config.IsEnabled)
                {
                    var group = config.GetGroupForPump(SelectedPump.PumpId);
                    if (group != null)
                    {
                        var connectedIds = group.GetConnectedPumps(SelectedPump.PumpId);
                        ActiveConnectedPumpIds = connectedIds;
                        var firstConnected = connectedIds.FirstOrDefault();
                        SelectedConnectedPump = firstConnected > 0
                            ? ConnectablePumpOptions.FirstOrDefault(p => p.PumpId == firstConnected)
                            : null;
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to resolve active pump connection group");
        }

        // Fallback: If legacy SelectedConnectedPump is chosen
        if (SelectedConnectedPump != null && SelectedConnectedPump.PumpId != SelectedPump.PumpId)
        {
            ActiveConnectedPumpIds = new List<int> { SelectedConnectedPump.PumpId };
        }
        else
        {
            ActiveConnectedPumpIds = new List<int>();
        }
    }

    partial void OnSelectedDateChanged(DateTime value)
    {
        ReloadPumpOptions();
        if (!_isEditing) LoadNozzlesForPump();
    }

    public bool IsNightShift => SelectedShift == "A";
    public bool IsDayShift => SelectedShift == "B";

    public bool IsMorningNightSplitEnabled => _featureToggleService?.IsFeatureEnabled("Collection_UseMorningNight", false) ?? false;

    public string MorningLabel => !IsMorningNightSplitEnabled ? "Amount (₹)" : (IsDayShift ? "Day (8am - 8pm) (₹)" : "Morning (12am - 8am) (₹)");
    public string MorningTidLabel => !IsMorningNightSplitEnabled ? "TID" : (IsDayShift ? "Day TID" : "Morning TID");
    public string MorningBatchLabel => !IsMorningNightSplitEnabled ? "Batch" : (IsDayShift ? "Day Batch" : "Morning Batch");

    partial void OnSelectedShiftChanged(string value)
    {
        OnPropertyChanged(nameof(IsNightShift));
        OnPropertyChanged(nameof(IsDayShift));
        OnPropertyChanged(nameof(IsMorningNightSplitEnabled));
        OnPropertyChanged(nameof(MorningLabel));
        OnPropertyChanged(nameof(MorningTidLabel));
        OnPropertyChanged(nameof(MorningBatchLabel));
        if (!_isEditing) LoadNozzlesForPump();
    }
    partial void OnDsmNameChanged(string value)
    {
        _ = RefreshConnectedPumpGrossSalesAsync();
        UpdateDsmCumulativeSummary();
    }
    partial void OnSelectedConnectedPumpChanged(PumpDisplayItem? value)
    {
        if (value != null && SelectedPump != null && value.PumpId != SelectedPump.PumpId)
        {
            if (!ActiveConnectedPumpIds.Contains(value.PumpId))
            {
                ActiveConnectedPumpIds = new List<int> { value.PumpId };
            }
        }
        else if (value == null && (ActiveConnectedPumpIds == null || ActiveConnectedPumpIds.Count <= 1))
        {
            ActiveConnectedPumpIds = new List<int>();
        }
        _ = RefreshConnectedPumpGrossSalesAsync();
        if (!_isEditing) LoadNozzlesForPump();
    }

    private void LoadNozzlesForPump()
    {
        _ = LoadNozzlesForPumpAsync();
    }

    private async Task LoadNozzlesForPumpAsync()
    {
        if (!_isEditing && SelectedPump != null && (ActiveConnectedPumpIds == null || ActiveConnectedPumpIds.Count == 0))
        {
            await ResolveActiveConnectionGroupAsync();
        }

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

        var effectivePumps = new List<int> { SelectedPump.PumpId };
        if (ActiveConnectedPumpIds != null && ActiveConnectedPumpIds.Count > 0)
        {
            effectivePumps.AddRange(ActiveConnectedPumpIds);
        }
        else if (SelectedConnectedPump != null && SelectedConnectedPump.PumpId != SelectedPump.PumpId)
        {
            effectivePumps.Add(SelectedConnectedPump.PumpId);
        }
        var allEffectivePumps = effectivePumps.Distinct().ToList();

        var nozzles = new List<int>();
        foreach (var pId in allEffectivePumps)
        {
            nozzles.AddRange(PumpConfiguration.GetNozzlesForPump(pId, SelectedDate));
        }
        nozzles = nozzles.Distinct().OrderBy(n => n).ToList();

        var (hsdRate, msIRate, msIIRate, cngRate) = await _dsmService.GetCurrentRatesAsync();
        var ratesMap = await _dsmService.GetFuelRatesMapAsync();
        
        // Fetch previous shift closings for all effective pumps in group
        var allPreviousClosings = new Dictionary<int, double>();
        foreach (var pId in allEffectivePumps)
        {
            var prevResult = await _nozzleRepository.GetPreviousShiftClosingsAsync(SelectedDate, SelectedShift, pId, EditingEntryId);
            if (prevResult.Success && prevResult.Data != null)
            {
                foreach (var kvp in prevResult.Data)
                {
                    allPreviousClosings[kvp.Key] = kvp.Value;
                }
            }
        }

        var currentShiftImport = await GetShiftImportAsync(SelectedDate, SelectedShift);
        var currentAgsReadings = currentShiftImport?.NozzleReadings
            .ToDictionary(r => r.NozzleNumber, r => r);

        var previousShiftType = GetPreviousShiftType(SelectedShift);
        // Operationally: Shift A (Night/Morning) predecessor is yesterday's Shift B (Day)
        //                Shift B (Day) predecessor is same day's Shift A (Night/Morning)
        DateTime previousShiftDate = SelectedShift == "A" ? SelectedDate.AddDays(-1) : SelectedDate;
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
            var tankName = PumpConfiguration.GetTankName(nozzlePumpId, n, SelectedDate);
            var fuelTypeName = PumpConfiguration.GetFuelTypeDisplayName(nozzlePumpId, n, SelectedDate);

            double rate = ResolveFuelRate(tankName, fuelTypeName, fuelType, ratesMap, hsdRate, msIRate, msIIRate, cngRate);

            double? openingFromAgsPreviousShift = previousAgsClosings.TryGetValue(n, out var prevAgsClosing)
                ? prevAgsClosing
                : null;
            AgsNozzleReading? currentAgsRow = null;
            var hasCurrentAgs = currentAgsReadings != null && currentAgsReadings.TryGetValue(n, out currentAgsRow);
            
            var hasPreviousDsm = allPreviousClosings.TryGetValue(n, out var previousDsmClosing);

            double? openingReading = null;
            if (hasCurrentAgs && currentAgsRow?.OpeningReading > 0)
            {
                openingReading = currentAgsRow.OpeningReading;
            }
            else if (hasPreviousDsm && previousDsmClosing > 0)
            {
                // Prefer previous DSM closing (which captures latest shift/session readings)
                // If previous AGS is higher (e.g. from a subsequent AGS import), use the higher meter reading.
                if (openingFromAgsPreviousShift.HasValue && openingFromAgsPreviousShift.Value > previousDsmClosing)
                {
                    openingReading = openingFromAgsPreviousShift.Value;
                }
                else
                {
                    openingReading = previousDsmClosing;
                }
            }
            else if (openingFromAgsPreviousShift.HasValue)
            {
                openingReading = openingFromAgsPreviousShift.Value;
            }

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
                if (temp.Override && temp.Opening.HasValue) openingReading = temp.Opening;
                if (temp.Closing.HasValue) closingReading = temp.Closing;
                if (temp.Rate.HasValue) rate = temp.Rate.Value;
            }

            NozzleReadings.Add(new NozzleReadingRow
            {
                NozzleNumber = n,
                PumpId = nozzlePumpId,
                FuelType = PumpConfiguration.GetFuelTypeDisplayName(nozzlePumpId, n, SelectedDate),
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
        var previousSelection = SelectedConnectedPump?.PumpId;
        ConnectablePumpOptions.Clear();

        if (SelectedPump != null)
        {
            foreach (var item in PumpConfiguration.GetPumpDisplayItems(SelectedDate))
            {
                if (item.PumpId != SelectedPump.PumpId)
                {
                    ConnectablePumpOptions.Add(item);
                }
            }
        }

        if (previousSelection.HasValue)
        {
            SelectedConnectedPump = ConnectablePumpOptions.FirstOrDefault(p => p.PumpId == previousSelection.Value);
        }
    }

    public async Task RefreshConnectedPumpGrossSalesAsync()
    {
        ConnectedPumpGrossSales = 0;
        ConnectedPumpStatus = "";

        var effectiveConnectedPumps = ActiveConnectedPumpIds.Where(p => SelectedPump == null || p != SelectedPump.PumpId).Distinct().ToList();
        if (effectiveConnectedPumps.Count == 0 && SelectedConnectedPump != null && SelectedPump != null && SelectedConnectedPump.PumpId != SelectedPump.PumpId)
        {
            effectiveConnectedPumps.Add(SelectedConnectedPump.PumpId);
        }

        if (effectiveConnectedPumps.Count == 0 || string.IsNullOrWhiteSpace(DsmName))
        {
            RecalculateAll();
            return;
        }

        var shiftResult = await App.Services.GetRequiredService<Core.Repositories.IShiftRepository>()
            .GetShiftAsync(SelectedDate, SelectedShift);
        if (!shiftResult.Success || shiftResult.Data == null)
        {
            var pList = string.Join(", ", effectiveConnectedPumps);
            ConnectedPumpStatus = $"Connected pump(s) {pList} will be included after entries are saved.";
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

        double totalConnectedGross = 0;
        var foundPumps = new List<int>();
        var repo = App.Services.GetRequiredService<IDsmEntryRepository>();

        foreach (var connPumpId in effectiveConnectedPumps)
        {
            DsmEntry? connectedEntry = null;
            if (EditingEntryId.HasValue)
            {
                connectedEntry = entriesResult.Data.FirstOrDefault(e =>
                    e.PumpId == connPumpId
                    && (e.ReconciledToPumpId == EditingEntryId.Value || e.ReconciledToPumpId == SelectedPump?.PumpId));
            }

            if (connectedEntry == null)
            {
                connectedEntry = entriesResult.Data
                    .Where(e => e.PumpId == connPumpId
                        && string.Equals(e.DsmName, DsmName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.DsmEntryId)
                    .FirstOrDefault();
            }

            if (connectedEntry != null)
            {
                if (connectedEntry.NozzleReadings == null || connectedEntry.NozzleReadings.Count == 0)
                {
                    var fullConnected = await repo.GetFullEntryAsync(connectedEntry.DsmEntryId);
                    if (fullConnected.Success && fullConnected.Data != null)
                    {
                        connectedEntry = fullConnected.Data;
                    }
                }

                double gross = (connectedEntry.NozzleReadings ?? new List<NozzleReading>()).Sum(n => n.Amount);
                totalConnectedGross += gross;
                foundPumps.Add(connPumpId);
            }
        }

        ConnectedPumpGrossSales = totalConnectedGross;
        if (effectiveConnectedPumps.Count > 0)
        {
            var pList = string.Join(", ", effectiveConnectedPumps.Select(p => $"Pump {p}"));
            if (foundPumps.Count > 0)
            {
                ConnectedPumpStatus = $"Primary: Pump {SelectedPump?.PumpId} | Connected: {pList} | Included sales: ₹{ConnectedPumpGrossSales:N2}";
            }
            else
            {
                ConnectedPumpStatus = $"Primary: Pump {SelectedPump?.PumpId} | Connected: {pList} (Partitioned across {1 + effectiveConnectedPumps.Count} pumps)";
            }
        }

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
            if (!FuelPro.Core.Common.PumpConfiguration.IsTestingEnabledForTank(tank))
            {
                continue;
            }

            TestingRows.Add(new TestingRow
            {
                NozzleNumber = nozzle.NozzleNumber,
                FuelType = $"Nozzle {nozzle.NozzleNumber} ({nozzle.FuelType})",
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

    private static double ResolveFuelRate(string? tankName, string? fuelTypeName, FuelType fuelType, Dictionary<string, double> ratesMap, double hsdRate, double msIRate, double msIIRate, double cngRate)
    {
        if (ratesMap != null && ratesMap.Count > 0)
        {
            var normTank = (tankName ?? "").Trim();
            var normFuel = (fuelTypeName ?? "").Trim();

            // 1. Direct exact key match (case-insensitive)
            if (!string.IsNullOrEmpty(normTank) && ratesMap.TryGetValue(normTank, out var tr) && tr > 0)
                return tr;

            if (!string.IsNullOrEmpty(normFuel) && ratesMap.TryGetValue(normFuel, out var fr) && fr > 0)
                return fr;

            // 2. Exact match in keys by normalized comparison
            foreach (var kv in ratesMap)
            {
                if (kv.Value <= 0) continue;
                var key = kv.Key.Trim();

                if (!string.IsNullOrEmpty(normTank) && (string.Equals(key, normTank, StringComparison.OrdinalIgnoreCase) || key.Contains(normTank, StringComparison.OrdinalIgnoreCase) || normTank.Contains(key, StringComparison.OrdinalIgnoreCase)))
                    return kv.Value;

                if (!string.IsNullOrEmpty(normFuel) && (string.Equals(key, normFuel, StringComparison.OrdinalIgnoreCase) || key.Contains(normFuel, StringComparison.OrdinalIgnoreCase) || normFuel.Contains(key, StringComparison.OrdinalIgnoreCase)))
                    return kv.Value;
            }

            // 3. Keyword-based matching for specialized and standard fuel types
            string combined = $"{normFuel} {normTank}".ToUpperInvariant();

            if (combined.Contains("SPEED") || combined.Contains("XP95") || combined.Contains("POWER") || combined.Contains("TURBO") || combined.Contains("EXTRA"))
            {
                var speedMatch = ratesMap.FirstOrDefault(kv => kv.Value > 0 && (kv.Key.Contains("SPEED", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("XP95", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("POWER", StringComparison.OrdinalIgnoreCase)));
                if (speedMatch.Value > 0) return speedMatch.Value;
            }

            if (combined.Contains("HSD") || combined.Contains("DIESEL"))
            {
                var hsdMatch = ratesMap.FirstOrDefault(kv => kv.Value > 0 && (kv.Key.Contains("HSD", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("DIESEL", StringComparison.OrdinalIgnoreCase)));
                if (hsdMatch.Value > 0) return hsdMatch.Value;
            }

            if (combined.Contains("MS-II") || combined.Contains("MS2") || combined.Contains("MS_II") || combined.Contains("20KL II"))
            {
                var ms2Match = ratesMap.FirstOrDefault(kv => kv.Value > 0 && (kv.Key.Contains("MS-II", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("MS2", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("20KL II", StringComparison.OrdinalIgnoreCase)));
                if (ms2Match.Value > 0) return ms2Match.Value;
            }

            if (combined.Contains("MS-I") || combined.Contains("MS") || combined.Contains("PETROL"))
            {
                var ms1Match = ratesMap.FirstOrDefault(kv => kv.Value > 0 && (kv.Key.Contains("MS-I", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("MS", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("PETROL", StringComparison.OrdinalIgnoreCase)));
                if (ms1Match.Value > 0) return ms1Match.Value;
            }

            if (combined.Contains("CNG"))
            {
                var cngMatch = ratesMap.FirstOrDefault(kv => kv.Value > 0 && kv.Key.Contains("CNG", StringComparison.OrdinalIgnoreCase));
                if (cngMatch.Value > 0) return cngMatch.Value;
            }
        }

        // Fallback to standard 4-rate switch
        return fuelType switch
        {
            FuelType.HSD => hsdRate,
            FuelType.MS_I => msIRate,
            FuelType.MS_II => msIIRate,
            FuelType.CNG => cngRate,
            _ => msIRate
        };
    }

    public void RecalculateAll()
    {
        if (!_isEditing && IsSaved)
        {
            IsSaved = false;
        }
        TotalLitres = NozzleReadings.Sum(n => n.SaleLitres);
        var calc = _dsmCalculationService.Calculate(BuildCalculationDto());
        
        double rawGross;
        // If nozzle readings have no closing values (e.g. historical entry with missing nozzle child records)
        // but the entry has a saved GrossSales from DB, preserve the saved GrossSales rather than zeroing it out.
        if (calc.GrossSales == 0 && _savedEntryGrossSales.HasValue && _savedEntryGrossSales.Value > 0 && NozzleReadings.All(n => !n.ClosingReading.HasValue || n.ClosingReading == 0))
        {
            rawGross = (double)_savedEntryGrossSales.Value;
        }
        else
        {
            rawGross = (double)calc.GrossSales;
        }

        MeterGrossSales = rawGross;
        TotalTestingAmount = (double)calc.TotalTesting;
        NetGrossSales = (double)calc.NetGrossSales;
        HasTesting = TotalTestingAmount > 0;
        MeterGrossSalesSubtitle = HasTesting
            ? $"Meter: ₹{MeterGrossSales:N2} - Test: ₹{TotalTestingAmount:N2}"
            : string.Empty;

        GrossSales = NetGrossSales;
        TotalPaymentIn = (double)calc.TotalInDirect;
        TotalDebtors = (double)calc.TotalCreditors;
        FinalAdjusted = (double)calc.TotalCollection;
        Difference = (double)calc.Mismatch;
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
    private void AddKhandharePetroleumEntry() => KhandharePetroleumEntries.Add(new KhandharePetroleumRow { OnRowChanged = RecalculateAll });

    [RelayCommand]
    private void RemoveKhandharePetroleumEntry(KhandharePetroleumRow? row) { if (row != null) KhandharePetroleumEntries.Remove(row); RecalculateAll(); }

    [RelayCommand]
    private void AddQrPayment() => QrPayments.Add(new QrPaymentRow { OnRowChanged = RecalculateAll });

    [RelayCommand]
    private void RemoveQrPayment(QrPaymentRow? row) { if (row != null) QrPayments.Remove(row); RecalculateAll(); }

    private bool CanSaveEntry() => !IsSaving && !IsSaved;

    [RelayCommand(CanExecute = nameof(CanSaveEntry))]
    private async Task SaveEntryAsync()
    {
        if (IsSaving) return;

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

            var isDay = SelectedShift == "B";
            var payment = new PaymentCollection
            {
                PhonePeCardMorning = isDay ? 0 : (PhonePeCardMorning ?? 0),
                PhonePeCardDay = isDay ? (PhonePeCardMorning ?? 0) : 0,
                PhonePeCardNight = PhonePeCardNight ?? 0,
                PhonePeMorning = isDay ? 0 : (PhonePeMorning ?? 0),
                PhonePeDay = isDay ? (PhonePeMorning ?? 0) : 0,
                PhonePeNight = PhonePeNight ?? 0,
                CreditCardMorning = isDay ? 0 : (CreditCardMorning ?? 0),
                CreditCardDay = isDay ? (CreditCardMorning ?? 0) : 0,
                CreditCardNight = CreditCardNight ?? 0,
                PetroCardMorning = isDay ? 0 : (PetroCardMorning ?? 0),
                PetroCardDay = isDay ? (PetroCardMorning ?? 0) : 0,
                PetroCardNight = PetroCardNight ?? 0,
                Others = Others ?? 0,
                CashDeposit = Cash1.TotalAmount,
                CardTid = (CreditCardTidNight ?? CreditCardTidMorning),
                CardBatch = (CreditCardBatchNight ?? CreditCardBatchMorning),
                PhonePeTid = (PhonePeTidNight ?? PhonePeTidMorning),
                PhonePeBatch = (PhonePeBatchNight ?? PhonePeBatchMorning),
                PetroCardTid = (PetroCardTidNight ?? PetroCardTidMorning),
                PetroCardBatch = (PetroCardBatchNight ?? PetroCardBatchMorning),
                PhonePeTidMorning = PhonePeTidMorning,
                PhonePeBatchMorning = PhonePeBatchMorning,
                PhonePeTidDay = isDay ? PhonePeTidMorning : null,
                PhonePeBatchDay = isDay ? PhonePeBatchMorning : null,
                PhonePeTidNight = PhonePeTidNight,
                PhonePeBatchNight = PhonePeBatchNight,
                CreditCardTidMorning = CreditCardTidMorning,
                CreditCardBatchMorning = CreditCardBatchMorning,
                CreditCardTidDay = isDay ? CreditCardTidMorning : null,
                CreditCardBatchDay = isDay ? CreditCardBatchMorning : null,
                CreditCardTidNight = CreditCardTidNight,
                CreditCardBatchNight = CreditCardBatchNight,
                PetroCardTidMorning = PetroCardTidMorning,
                PetroCardBatchMorning = PetroCardBatchMorning,
                PetroCardTidDay = isDay ? PetroCardTidMorning : null,
                PetroCardBatchDay = isDay ? PetroCardBatchMorning : null,
                PetroCardTidNight = PetroCardTidNight,
                PetroCardBatchNight = PetroCardBatchNight
            };

            foreach (var dyn in DynamicCollections)
            {
                if (dyn.Amount.HasValue && dyn.Amount.Value > 0)
                {
                    payment.Items.Add(new PaymentCollectionItem
                    {
                        CollectionTypeCode = dyn.Code,
                        Amount = dyn.Amount.Value,
                        Tid = dyn.Tid,
                        Batch = dyn.Batch,
                        Slot = "General"
                    });
                }
            }


            var debitModels = Debits.Where(d => !string.IsNullOrWhiteSpace(d.DebtorName))
                .Select(d => new DebitEntry 
                { 
                    DebtorName = d.DebtorName, 
                    Amount = d.Amount ?? 0, 
                    ChequeNo = d.ChequeNo,
                    VehicleNumber = d.VehicleNumber,
                    PaymentMethod = string.IsNullOrEmpty(d.PaymentMethod) ? "Credit" : d.PaymentMethod
                }).ToList();

            var testingModels = BuildTestingModels();

            var expenseModels = Expenses.Where(e => !string.IsNullOrWhiteSpace(e.Description))
                .Select(e => new Expense { Description = e.Description, Amount = e.Amount ?? 0 }).ToList();

            var kpModels = KhandharePetroleumEntries
                .Where(kp => !string.IsNullOrWhiteSpace(kp.Name) || !string.IsNullOrWhiteSpace(kp.SlipNumber) || !string.IsNullOrWhiteSpace(kp.VehicleNumber) || (kp.Amount ?? 0) > 0)
                .Select(kp => new KhandharePetroleumEntry
                {
                    Name = !string.IsNullOrWhiteSpace(kp.Name) ? kp.Name : (!string.IsNullOrWhiteSpace(kp.SlipNumber) ? $"Slip #{kp.SlipNumber}" : "Kandhare Petroleum"),
                    VehicleNumber = kp.VehicleNumber ?? "",
                    SlipNumber = kp.SlipNumber ?? "",
                    Amount = kp.Amount ?? 0
                }).ToList();

            var qrModels = QrPayments
                .Where(q => !string.IsNullOrWhiteSpace(q.TargetDsmName) && (q.Amount ?? 0) > 0)
                .Select(q => new DsmQrPaymentEntry
                {
                    TargetDsmName = q.TargetDsmName,
                    Amount = q.Amount ?? 0,
                    Tid = q.Tid,
                    Batch = q.Batch,
                    Slot = q.Slot
                }).ToList();

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
                null,
                kpModels,
                qrModels,
                connectedPumpIds: ActiveConnectedPumpIds);

            if (result.Success)
            {
                EditingEntryId = result.Data?.DsmEntryId ?? EditingEntryId;
                StatusMessage = "✅ DSM Entry saved successfully!";
                _draftService.ClearDraft();
                await LoadShiftEntriesAsync();
                IsSaved = true;
                OnPropertyChanged(nameof(CanSaveCurrentEntry));
                SaveEntryCommand.NotifyCanExecuteChanged();

                // Trigger background sync now that transaction is fully committed
                var syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
                _ = syncEngine.ForceSyncAsync();
            }
            else
            {
                StatusMessage = $"❌ {result.Error}";
                IsSaved = false;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Save failed: {ex.Message}";
            IsSaved = false;
        }
        finally
        {
            IsSaving = false;
            OnPropertyChanged(nameof(CanSaveCurrentEntry));
            SaveEntryCommand.NotifyCanExecuteChanged();
        }
    }


    [RelayCommand]
    public void ClearForm()
    {
        DsmName = "";
        EditingEntryId = null;
        _isEditing = false;
        IsSaved = false;
        StartTime = "08:00 AM";
        EndTime = "08:00 PM";
        PhonePeCardMorning = PhonePeCardNight = null;
        PhonePeMorning = PhonePeNight = null;
        CreditCardMorning = CreditCardNight = null;
        PetroCardMorning = PetroCardNight = null;
        Others = CashDeposit = null;
        CardTid = CardBatch = null;
        PhonePeTid = PhonePeBatch = PhonePeTidMorning = PhonePeBatchMorning = PhonePeTidNight = PhonePeBatchNight = null;
        CreditCardTidMorning = CreditCardBatchMorning = CreditCardTidNight = CreditCardBatchNight = null;
        PetroCardTid = PetroCardBatch = PetroCardTidMorning = PetroCardBatchMorning = PetroCardTidNight = PetroCardBatchNight = null;
        SelectedConnectedPump = null;
        ActiveConnectedPumpIds = new();
        ConnectedPumpGrossSales = 0;
        ConnectedPumpStatus = "";
        TestingRows.Clear();
        Debits.Clear();
        Expenses.Clear();
        KhandharePetroleumEntries.Clear();
        QrPayments.Clear();
        foreach (var row in DynamicCollections)
        {
            row.Amount = null;
            row.Tid = null;
            row.Batch = null;
        }
        if (DynamicCollections.Count == 0)
        {
            _ = LoadDynamicCollectionTypesAsync();
        }
        NozzleReadings.Clear();
        ValidationSummary = "";
        _savedEntryGrossSales = null;
        Cash1 = new CashDenomRow { CashType = "Cash1", OnTotalChanged = RecalculateAll };
        Cash2 = new CashDenomRow { CashType = "Cash2", OnTotalChanged = RecalculateAll };
        LoadNozzlesForPump();
        StatusMessage = "Form cleared — ready for new entry";
        OnPropertyChanged(nameof(CanSaveCurrentEntry));
        SaveEntryCommand.NotifyCanExecuteChanged();
    }

    public async Task HydrateFromEntryAsync(DsmEntry entry)
    {
        _isEditing = true;
        IsSaved = true;
        try
        {
            EditingEntryId = entry.DsmEntryId;
            DsmName = entry.DsmName;
            StartTime = string.IsNullOrEmpty(entry.StartTime) ? "08:00 AM" : entry.StartTime;
            EndTime = string.IsNullOrEmpty(entry.EndTime) ? "08:00 PM" : entry.EndTime;

            if (entry.Shift != null)
            {
                SelectedDate = entry.Shift.ShiftDate;
                var st = entry.Shift.ShiftType;
                SelectedShift = st == "I" ? "A" : (st == "II" ? "B" : (st == "III" ? "C" : st));
            }

            var pumpMatch = PumpOptions.FirstOrDefault(p => p.PumpId == entry.PumpId);
            if (pumpMatch != null) SelectedPump = pumpMatch;

            // Connected pumps reconstruction
            var effectiveConnectedIds = entry.GetEffectiveConnectedPumpIds();
            if (effectiveConnectedIds.Count == 0 && entry.NozzleReadings != null)
            {
                foreach (var nr in entry.NozzleReadings)
                {
                    var nozzlePumpId = PumpConfiguration.GetPumpIdForNozzle(nr.NozzleNumber, entry.Shift?.ShiftDate ?? SelectedDate);
                    if (nozzlePumpId != 0 && nozzlePumpId != entry.PumpId && !effectiveConnectedIds.Contains(nozzlePumpId))
                    {
                        effectiveConnectedIds.Add(nozzlePumpId);
                    }
                }
            }

            ActiveConnectedPumpIds = effectiveConnectedIds.Distinct().Where(id => id != entry.PumpId).ToList();
            if (ActiveConnectedPumpIds.Count > 0)
            {
                var firstConnId = ActiveConnectedPumpIds[0];
                var connMatch = ConnectablePumpOptions.FirstOrDefault(p => p.PumpId == firstConnId);
                SelectedConnectedPump = connMatch;
            }
            else
            {
                SelectedConnectedPump = null;
            }

            var repo = App.Services.GetRequiredService<IDsmEntryRepository>();
            var allSlaveEntries = new List<DsmEntry>();

            if (ActiveConnectedPumpIds.Count > 0)
            {
                var shiftId = entry.ShiftId;
                var entriesResult = await repo.GetEntriesForShiftAsync(shiftId);
                if (entriesResult.Success && entriesResult.Data != null)
                {
                    foreach (var connPumpId in ActiveConnectedPumpIds)
                    {
                        var rawConn = entriesResult.Data
                            .Where(e => e.DsmEntryId != entry.DsmEntryId
                                && e.PumpId == connPumpId
                                && (e.ReconciledToPumpId == entry.DsmEntryId || e.ReconciledToPumpId == entry.PumpId || string.Equals(e.DsmName, entry.DsmName, StringComparison.OrdinalIgnoreCase)))
                            .OrderBy(e => e.ReconciledToPumpId == entry.DsmEntryId ? 0 : 1)
                            .ThenBy(e => e.DsmEntryId >= entry.DsmEntryId ? (e.DsmEntryId - entry.DsmEntryId) : (100000 + Math.Abs(e.DsmEntryId - entry.DsmEntryId)))
                            .FirstOrDefault();

                        if (rawConn != null)
                        {
                            var fullConnected = await repo.GetFullEntryAsync(rawConn.DsmEntryId);
                            if (fullConnected.Success && fullConnected.Data != null && fullConnected.Data.DsmEntryId != entry.DsmEntryId)
                            {
                                allSlaveEntries.Add(fullConnected.Data);
                            }
                        }
                    }
                }
            }
            var connectedEntry = allSlaveEntries.FirstOrDefault();

            // Capture existing saved GrossSales from DB (to prevent 0 GrossSales on historical entries missing child NozzleReadings)
            // If primary GrossSales already represents the group total, DO NOT add slave GrossSales.
            if (entry.GrossSales > 0)
            {
                _savedEntryGrossSales = (double)entry.GrossSales;
            }
            else
            {
                double slaveSum = 0;
                foreach (var slave in allSlaveEntries)
                {
                    if (slave.GrossSales > 0)
                    {
                        slaveSum += (double)slave.GrossSales;
                    }
                }
                _savedEntryGrossSales = slaveSum > 0 ? slaveSum : (double?)null;
            }

            // Explicitly load nozzles loading
            await LoadNozzlesForPumpAsync();

            // Populate payment losslessly (preserve both Morning and Night slots for all shifts)
            var paymentSource = entry.PaymentCollection ?? (connectedEntry != null && connectedEntry.DsmEntryId != entry.DsmEntryId ? connectedEntry.PaymentCollection : null);
            bool isShiftB = string.Equals(SelectedShift, "B", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(entry.Shift?.ShiftType, "B", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(entry.Shift?.ShiftType, "II", StringComparison.OrdinalIgnoreCase);

            if (isShiftB)
            {
                PhonePeCardMorning = paymentSource?.PhonePeCardDay > 0 ? paymentSource.PhonePeCardDay : (paymentSource?.PhonePeCardMorning > 0 ? paymentSource.PhonePeCardMorning : (double?)null);
                PhonePeCardNight = paymentSource?.PhonePeCardNight;
                PhonePeMorning = paymentSource?.PhonePeDay > 0 ? paymentSource.PhonePeDay : (paymentSource?.PhonePeMorning > 0 ? paymentSource.PhonePeMorning : (double?)null);
                PhonePeNight = paymentSource?.PhonePeNight;
                CreditCardMorning = paymentSource?.CreditCardDay > 0 ? paymentSource.CreditCardDay : (paymentSource?.CreditCardMorning > 0 ? paymentSource.CreditCardMorning : (double?)null);
                CreditCardNight = paymentSource?.CreditCardNight;
                PetroCardMorning = paymentSource?.PetroCardDay > 0 ? paymentSource.PetroCardDay : (paymentSource?.PetroCardMorning > 0 ? paymentSource.PetroCardMorning : (double?)null);
                PetroCardNight = paymentSource?.PetroCardNight;
            }
            else
            {
                PhonePeCardMorning = paymentSource?.PhonePeCardMorning > 0 ? paymentSource.PhonePeCardMorning : (paymentSource?.PhonePeCardDay > 0 ? paymentSource.PhonePeCardDay : (double?)null);
                PhonePeCardNight = paymentSource?.PhonePeCardNight;
                PhonePeMorning = paymentSource?.PhonePeMorning > 0 ? paymentSource.PhonePeMorning : (paymentSource?.PhonePeDay > 0 ? paymentSource.PhonePeDay : (double?)null);
                PhonePeNight = paymentSource?.PhonePeNight;
                CreditCardMorning = paymentSource?.CreditCardMorning > 0 ? paymentSource.CreditCardMorning : (paymentSource?.CreditCardDay > 0 ? paymentSource.CreditCardDay : (double?)null);
                CreditCardNight = paymentSource?.CreditCardNight;
                PetroCardMorning = paymentSource?.PetroCardMorning > 0 ? paymentSource.PetroCardMorning : (paymentSource?.PetroCardDay > 0 ? paymentSource.PetroCardDay : (double?)null);
                PetroCardNight = paymentSource?.PetroCardNight;
            }

            Others = paymentSource?.Others;
            CashDeposit = paymentSource?.CashDeposit;

            PhonePeTidMorning = paymentSource?.PhonePeTidMorning ?? paymentSource?.PhonePeTidDay ?? paymentSource?.PhonePeTid;
            PhonePeBatchMorning = paymentSource?.PhonePeBatchMorning ?? paymentSource?.PhonePeBatchDay ?? paymentSource?.PhonePeBatch;
            PhonePeTidNight = paymentSource?.PhonePeTidNight;
            PhonePeBatchNight = paymentSource?.PhonePeBatchNight;

            CreditCardTidMorning = paymentSource?.CreditCardTidMorning ?? paymentSource?.CreditCardTidDay ?? paymentSource?.CardTid;
            CreditCardBatchMorning = paymentSource?.CreditCardBatchMorning ?? paymentSource?.CreditCardBatchDay ?? paymentSource?.CardBatch;
            CreditCardTidNight = paymentSource?.CreditCardTidNight;
            CreditCardBatchNight = paymentSource?.CreditCardBatchNight;

            PetroCardTidMorning = paymentSource?.PetroCardTidMorning ?? paymentSource?.PetroCardTidDay ?? paymentSource?.PetroCardTid;
            PetroCardBatchMorning = paymentSource?.PetroCardBatchMorning ?? paymentSource?.PetroCardBatchDay ?? paymentSource?.PetroCardBatch;
            PetroCardTidNight = paymentSource?.PetroCardTidNight;
            PetroCardBatchNight = paymentSource?.PetroCardBatchNight;

            PhonePeTid = paymentSource?.PhonePeTidNight ?? paymentSource?.PhonePeTidMorning ?? paymentSource?.PhonePeTid;
            PhonePeBatch = paymentSource?.PhonePeBatchNight ?? paymentSource?.PhonePeBatchMorning ?? paymentSource?.PhonePeBatch;
            CardTid = paymentSource?.CreditCardTidNight ?? paymentSource?.CreditCardTidMorning ?? paymentSource?.CardTid;
            CardBatch = paymentSource?.CreditCardBatchNight ?? paymentSource?.CreditCardBatchMorning ?? paymentSource?.CardBatch;
            PetroCardTid = paymentSource?.PetroCardTidNight ?? paymentSource?.PetroCardTidMorning ?? paymentSource?.PetroCardTid;
            PetroCardBatch = paymentSource?.PetroCardBatchNight ?? paymentSource?.PetroCardBatchMorning ?? paymentSource?.PetroCardBatch;

            await LoadDynamicCollectionTypesAsync(paymentSource);


            // Populate nozzle readings (override auto-loaded ones)
            foreach (var nozzleRow in NozzleReadings)
            {
                var savedReading = entry.NozzleReadings?.FirstOrDefault(n => n.NozzleNumber == nozzleRow.NozzleNumber);
                if (savedReading == null)
                {
                    foreach (var slave in allSlaveEntries)
                    {
                        savedReading = slave.NozzleReadings?.FirstOrDefault(n => n.NozzleNumber == nozzleRow.NozzleNumber);
                        if (savedReading != null) break;
                    }
                }

                if (savedReading != null)
                {
                    nozzleRow.OpeningReading = savedReading.OpeningReading;
                    nozzleRow.ClosingReading = savedReading.ClosingReading;
                    if (savedReading.Rate > 0) nozzleRow.Rate = savedReading.Rate;
                }
            }

            // Populate debits with strict deduplication
            Debits.Clear();
            var allDebits = new List<DebitEntry>();
            if (entry.DebitEntries != null) allDebits.AddRange(entry.DebitEntries);
            foreach (var slave in allSlaveEntries)
            {
                if (slave.DebitEntries != null)
                {
                    foreach (var cd in slave.DebitEntries)
                    {
                        if (!allDebits.Any(d => (d.DebitId != 0 && d.DebitId == cd.DebitId)
                            || (string.Equals(d.DebtorName, cd.DebtorName, StringComparison.OrdinalIgnoreCase) && Math.Abs(d.Amount - cd.Amount) < 0.001)))
                        {
                            allDebits.Add(cd);
                        }
                    }
                }
            }

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

            // Populate expenses with strict deduplication
            Expenses.Clear();
            var allExpenses = new List<Expense>();
            if (entry.Expenses != null) allExpenses.AddRange(entry.Expenses);
            foreach (var slave in allSlaveEntries)
            {
                if (slave.Expenses != null)
                {
                    foreach (var ce in slave.Expenses)
                    {
                        if (!allExpenses.Any(e => (e.ExpenseId != 0 && e.ExpenseId == ce.ExpenseId)
                            || (string.Equals(e.Description, ce.Description, StringComparison.OrdinalIgnoreCase) && Math.Abs(e.Amount - ce.Amount) < 0.001)))
                        {
                            allExpenses.Add(ce);
                        }
                    }
                }
            }

            foreach (var expense in allExpenses)
            {
                Expenses.Add(new ExpenseRow
                {
                    Description = expense.Description,
                    Amount = expense.Amount,
                    OnRowChanged = RecalculateAll
                });
            }

            // Populate Khandhare Petroleum entries with strict deduplication
            KhandharePetroleumEntries.Clear();
            var allKp = new List<KhandharePetroleumEntry>();
            if (entry.KhandharePetroleumEntries != null && entry.KhandharePetroleumEntries.Count > 0)
            {
                allKp.AddRange(entry.KhandharePetroleumEntries);
            }
            if (connectedEntry != null && connectedEntry.DsmEntryId != entry.DsmEntryId && connectedEntry.KhandharePetroleumEntries != null && connectedEntry.KhandharePetroleumEntries.Count > 0)
            {
                foreach (var ckp in connectedEntry.KhandharePetroleumEntries)
                {
                    if (!allKp.Any(k => (k.Id != 0 && k.Id == ckp.Id)
                        || (string.Equals(k.SlipNumber, ckp.SlipNumber, StringComparison.OrdinalIgnoreCase) && Math.Abs(k.Amount - ckp.Amount) < 0.001)))
                    {
                        allKp.Add(ckp);
                    }
                }
            }

            // Fallback 1: Query local DB for KhandharePetroleumEntries by (Date, DsmName) or DsmEntryId
            if (allKp.Count == 0 && entry.Shift != null)
            {
                try
                {
                    using var db = App.Services.GetRequiredService<FuelPro.Data.FuelProDbContext>();
                    var shiftDate = entry.Shift.ShiftDate.Date;
                    var unlinkedKp = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                        db.KhandharePetroleumEntries
                            .Where(kp => (kp.DsmEntryId == entry.DsmEntryId) || (kp.Date.Date == shiftDate && kp.DsmName == entry.DsmName)));

                    if (unlinkedKp.Count > 0)
                    {
                        bool needsSave = false;
                        foreach (var kp in unlinkedKp)
                        {
                            if (!kp.DsmEntryId.HasValue || kp.DsmEntryId.Value <= 0)
                            {
                                kp.DsmEntryId = entry.DsmEntryId;
                                db.Entry(kp).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
                                needsSave = true;
                            }
                        }
                        if (needsSave)
                        {
                            await db.SaveChangesAsync();
                        }
                        allKp.AddRange(unlinkedKp);
                    }
                }
                catch (System.Exception ex)
                {
                    Serilog.Log.Error(ex, "Failed to load fallback KhandharePetroleumEntries by date/dsmName for DsmEntryId {Id}", entry.DsmEntryId);
                }
            }

            // Fallback 2: Check DsmApprovalAudits for metadata JSON containing khandhareEntries
            if (allKp.Count == 0)
            {
                try
                {
                    using var db = App.Services.GetRequiredService<FuelPro.Data.FuelProDbContext>();
                    var audit = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                        db.DsmApprovalAudits
                            .Where(a => a.ApprovedDataJson.Contains($"\"DsmEntryId\":{entry.DsmEntryId}") 
                                     || a.ApprovedDataJson.Contains($"\"DsmEntryId\": {entry.DsmEntryId}")));

                    if (audit != null && !string.IsNullOrEmpty(audit.OriginalDataJson))
                    {
                        var parsed = JsonConvert.DeserializeObject<dynamic>(audit.OriginalDataJson);
                        var kpRawList = parsed?.khandhareEntries ?? parsed?.khandharePetroleumEntries ?? parsed?.Collections?.khandhareEntries;
                        if (kpRawList != null)
                        {
                            var newKpModels = new List<KhandharePetroleumEntry>();
                            foreach (var kp in kpRawList)
                            {
                                string name = (kp.name ?? kp.Name ?? string.Empty).ToString();
                                string slipNumber = (kp.slipNumber ?? kp.SlipNumber ?? string.Empty).ToString();
                                string vehicleNumber = (kp.vehicleNumber ?? kp.VehicleNumber ?? kp.vehicle_number ?? kp.vehicleNo ?? string.Empty).ToString();
                                double amount = System.Convert.ToDouble((object?)(kp.amount ?? kp.Amount ?? 0.0));

                                var model = new KhandharePetroleumEntry
                                {
                                    DsmEntryId = entry.DsmEntryId,
                                    DsmName = entry.DsmName,
                                    Date = entry.Shift?.ShiftDate ?? SelectedDate,
                                    Name = !string.IsNullOrWhiteSpace(name) ? name : (!string.IsNullOrWhiteSpace(slipNumber) ? $"Slip #{slipNumber}" : "Kandhare Petroleum"),
                                    SlipNumber = slipNumber,
                                    VehicleNumber = string.IsNullOrWhiteSpace(vehicleNumber) ? null : vehicleNumber,
                                    Amount = amount
                                };
                                newKpModels.Add(model);
                            }

                            if (newKpModels.Count > 0)
                            {
                                db.KhandharePetroleumEntries.AddRange(newKpModels);
                                await db.SaveChangesAsync();
                                allKp.AddRange(newKpModels);
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Serilog.Log.Error(ex, "Failed to restore KhandharePetroleumEntries from audit metadata for DsmEntryId {Id}", entry.DsmEntryId);
                }
            }

            foreach (var kp in allKp)
            {
                KhandharePetroleumEntries.Add(new KhandharePetroleumRow
                {
                    Name = kp.Name ?? "",
                    VehicleNumber = kp.VehicleNumber ?? "",
                    SlipNumber = kp.SlipNumber ?? "",
                    Amount = kp.Amount,
                    OnRowChanged = RecalculateAll
                });
            }

            // Populate Cross-DSM QR Payments with strict deduplication
            QrPayments.Clear();
            var allQr = new List<DsmQrPaymentEntry>();
            if (entry.QrPayments != null && entry.QrPayments.Count > 0)
            {
                allQr.AddRange(entry.QrPayments);
            }
            if (connectedEntry != null && connectedEntry.DsmEntryId != entry.DsmEntryId && connectedEntry.QrPayments != null && connectedEntry.QrPayments.Count > 0)
            {
                foreach (var cqr in connectedEntry.QrPayments)
                {
                    if (!allQr.Any(q => (q.Id != 0 && q.Id == cqr.Id)
                        || (string.Equals(q.TargetDsmName, cqr.TargetDsmName, StringComparison.OrdinalIgnoreCase) && Math.Abs(q.Amount - cqr.Amount) < 0.001)))
                    {
                        allQr.Add(cqr);
                    }
                }
            }

            foreach (var qr in allQr)
            {
                QrPayments.Add(new QrPaymentRow
                {
                    TargetDsmName = qr.TargetDsmName ?? "",
                    Amount = qr.Amount,
                    Tid = qr.Tid,
                    Batch = qr.Batch,
                    Slot = qr.Slot,
                    OnRowChanged = RecalculateAll
                });
            }

            // Populate testing rows
            TestingRows.Clear();
            var (hsdRate, msIRate, msIIRate, cngRate) = await _dsmService.GetCurrentRatesAsync();
            RebuildTestingRows(hsdRate, msIRate, msIIRate, cngRate);

            var allTesting = new List<TestingEntry>();
            if (entry.TestingEntries != null) allTesting.AddRange(entry.TestingEntries);
            foreach (var slave in allSlaveEntries)
            {
                if (slave.TestingEntries != null) allTesting.AddRange(slave.TestingEntries);
            }

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
                ?? allSlaveEntries.Select(s => s.CashDenominations?.FirstOrDefault(c => c.CashType == "Cash1")).FirstOrDefault(c => c != null);
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
                ?? allSlaveEntries.Select(s => s.CashDenominations?.FirstOrDefault(c => c.CashType == "Cash2")).FirstOrDefault(c => c != null);
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
        }
        finally
        {
            _isEditing = false;
        }
    }

    private bool UpdateHostViewModel(DsmEntryViewModel newVm)
    {
        if (Application.Current == null || Application.Current.Dispatcher == null) return false;

        bool updated = false;
        try
        {
            if (Application.Current.Dispatcher.CheckAccess())
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window.DataContext is MainWindowViewModel mwvm)
                    {
                        mwvm.SetCachedView<DsmEntryViewModel>(newVm);
                        mwvm.CurrentView = newVm;
                        updated = true;
                        break;
                    }
                    else if (window.DataContext is OwnerMainWindowViewModel omwvm)
                    {
                        omwvm.CurrentView = newVm;
                        updated = true;
                        break;
                    }
                    else if (window.DataContext is DeveloperMainWindowViewModel dmwvm)
                    {
                        dmwvm.CurrentView = newVm;
                        updated = true;
                        break;
                    }
                }
            }
            else
            {
                // In background/test threads, post asynchronously to dispatcher without blocking
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window.DataContext is MainWindowViewModel mwvm)
                        {
                            mwvm.SetCachedView<DsmEntryViewModel>(newVm);
                            mwvm.CurrentView = newVm;
                            break;
                        }
                        else if (window.DataContext is OwnerMainWindowViewModel omwvm)
                        {
                            omwvm.CurrentView = newVm;
                            break;
                        }
                        else if (window.DataContext is DeveloperMainWindowViewModel dmwvm)
                        {
                            dmwvm.CurrentView = newVm;
                            break;
                        }
                    }
                }));
            }
        }
        catch
        {
            // Ignore UI dispatch failures in headless/test environments
        }
        return updated;
    }

    [RelayCommand]
    private async Task EditEntryAsync(int dsmEntryId)
    {
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
                        (e.DsmEntryId == entry.ReconciledToPumpId.Value || e.PumpId == entry.ReconciledToPumpId.Value)
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

            // Self-healing: if the primary entry's ConnectedPumpId is not set, check if any entries reconcile to it
            if (!entry.ConnectedPumpId.HasValue && !entry.ReconciledToPumpId.HasValue)
            {
                var shiftId = entry.ShiftId;
                var entriesResult = await repo.GetEntriesForShiftAsync(shiftId);
                if (entriesResult.Success && entriesResult.Data != null)
                {
                    var reconcilingSlaves = entriesResult.Data
                        .Where(e => e.DsmEntryId != entry.DsmEntryId
                            && e.PumpId != entry.PumpId
                            && (e.ReconciledToPumpId == entry.DsmEntryId || e.ReconciledToPumpId == entry.PumpId)
                            && string.Equals(e.DsmName, entry.DsmName, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (reconcilingSlaves.Count > 0)
                    {
                        var slavePumpIds = reconcilingSlaves.Select(s => s.PumpId).Distinct().ToList();
                        entry.ConnectedPumpId = slavePumpIds.FirstOrDefault();
                        entry.ConnectedPumpIdsJson = System.Text.Json.JsonSerializer.Serialize(slavePumpIds);
                    }
                }
            }

            // Hydrate a brand new ViewModel instance
            var newVm = App.Services.GetRequiredService<DsmEntryViewModel>();
            await newVm.HydrateFromEntryAsync(entry);

            // Swap view model on host window
            bool hostUpdated = UpdateHostViewModel(newVm);
            if (!hostUpdated)
            {
                // Fallback for tests where no active host window is found/updated
                await HydrateFromEntryAsync(entry);
                StatusMessage = $"📝 Editing entry for {DsmName} on Pump {entry.PumpId}";
            }
            else
            {
                newVm.StatusMessage = $"📝 Editing entry for {newVm.DsmName} on Pump {entry.PumpId}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Failed to load entry for editing: {ex.Message}";
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

        UpdateDsmCumulativeSummary();
    }

    private void UpdateDsmCumulativeSummary()
    {
        if (string.IsNullOrWhiteSpace(DsmName) || ShiftEntries.Count == 0)
        {
            DsmCumulativeShiftSummaryMessage = string.Empty;
            return;
        }

        var dsmEntries = ShiftEntries.Where(e => string.Equals(e.DsmName, DsmName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (dsmEntries.Count > 1)
        {
            double totalSales = dsmEntries.Sum(e => (double)e.GrossSales);
            double totalNetSales = dsmEntries.Sum(e => (double)e.NetSales);
            double totalCollection = dsmEntries.Sum(e => (double)e.TotalCollection);
            string salesText = totalNetSales != totalSales && totalNetSales > 0
                ? $"Gross: ₹{totalSales:N2}, Net: ₹{totalNetSales:N2}"
                : $"Gross Sales: ₹{totalSales:N2}";
            DsmCumulativeShiftSummaryMessage = $"ℹ️ {DsmName} has {dsmEntries.Count} sessions in Shift {SelectedShift}. Combined {salesText}, Combined Collection: ₹{totalCollection:N2}";
        }
        else
        {
            DsmCumulativeShiftSummaryMessage = string.Empty;
        }
    }

    public async Task LoadSuggestionsAsync()
    {
        var result = await _dsmProfileRepo.GetAllAsync();
        DsmOptions.Clear();

        var uniqueNames = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        if (result.Success)
        {
            foreach (var profile in result.Data!)
            {
                if (!string.IsNullOrWhiteSpace(profile.DsmName))
                {
                    uniqueNames.Add(profile.DsmName);
                }
            }
        }

        try
        {
            using var dbContext = App.Services.GetRequiredService<FuelPro.Data.FuelProDbContext>();
            var users = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(dbContext.DsmUsers);
            foreach (var user in users)
            {
                if (!string.IsNullOrWhiteSpace(user.FullName))
                {
                    uniqueNames.Add(user.FullName);
                }
            }
        }
        catch (System.Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load DsmUsers for manual entry suggestions");
        }

        foreach (var name in uniqueNames.OrderBy(n => n))
        {
            DsmOptions.Add(name);
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
                PhonePe = (decimal)((PhonePeMorning ?? 0) + (PhonePeNight ?? 0) + (PhonePeCardMorning ?? 0) + (PhonePeCardNight ?? 0) + QrPayments.Sum(q => q.Amount ?? 0)),
                CreditCard = (decimal)((CreditCardMorning ?? 0) + (CreditCardNight ?? 0)),
                PetroCard = (decimal)((PetroCardMorning ?? 0) + (PetroCardNight ?? 0)),
                DynamicPayments = (decimal)DynamicCollections.Sum(d => d.Amount ?? 0),
                CashDeposit = (decimal)Cash1.TotalAmount,
                PhysicalCash = (decimal)Cash2.TotalAmount

            },
            DebitEntries = Debits.Select(x => new DebitEntryDto { Amount = (decimal)(x.Amount ?? 0), ChequeNo = x.ChequeNo }).ToList(),
            Expenses = Expenses.Select(x => new ExpenseDto { Amount = (decimal)(x.Amount ?? 0) })
                .Concat(KhandharePetroleumEntries.Select(x => new ExpenseDto { Amount = (decimal)(x.Amount ?? 0) }))
                .ToList(),
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
        foreach (var kp in KhandharePetroleumEntries)
        {
            if (string.IsNullOrWhiteSpace(kp.SlipNumber)) errors.Add("Khandhare Petroleum slip number is required.");
            if (!kp.Amount.HasValue || kp.Amount.Value <= 0) errors.Add($"Khandhare Petroleum amount must be greater than zero for Slip #{kp.SlipNumber}.");
        }
        foreach (var qr in QrPayments)
        {
            if (string.IsNullOrWhiteSpace(qr.TargetDsmName)) errors.Add("Paid on DSM QR is required for Cross-DSM QR payment.");
            if (!qr.Amount.HasValue || qr.Amount.Value <= 0) errors.Add($"Cross-DSM QR amount must be greater than zero for {qr.TargetDsmName}.");
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
        var stationName = settingsResult.Success && !string.IsNullOrWhiteSpace(settingsResult.Data?.PumpStationName) ? settingsResult.Data.PumpStationName : "Mitali Service Station";

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
            CompanyName  = stationName,
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
        var stationName = settingsResult.Success && !string.IsNullOrWhiteSpace(settingsResult.Data?.PumpStationName) ? settingsResult.Data.PumpStationName : "Mitali Service Station";

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
