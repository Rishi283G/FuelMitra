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

    private bool _hasNonZeroDenomination;

    public string CashType { get; set; } = "Cash1";
    [ObservableProperty] private int? _denom500;
    [ObservableProperty] private int? _denom200;
    [ObservableProperty] private int? _denom100;
    [ObservableProperty] private int? _denom50;
    [ObservableProperty] private int? _denom20;
    [ObservableProperty] private int? _denom10;
    [ObservableProperty] private int? _coins;
    [ObservableProperty] private double _totalAmount;

    public bool HasDenominationCounts =>
        CashType == "Cash1" || CashType == "Cash2"
            ? (Denom500 ?? 0) > 0 || (Denom200 ?? 0) > 0 || (Denom100 ?? 0) > 0
            : (Denom500 ?? 0) > 0 || (Denom200 ?? 0) > 0 || (Denom100 ?? 0) > 0
              || (Denom50 ?? 0) > 0 || (Denom20 ?? 0) > 0 || (Denom10 ?? 0) > 0 || (Coins ?? 0) > 0;

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
        if (CashType == "Cash1" || CashType == "Cash2")
        {
            sum = (Denom500 ?? 0) * 500.0 + (Denom200 ?? 0) * 200.0 + (Denom100 ?? 0) * 100.0;
        }
        else
        {
            sum = (Denom500 ?? 0) * 500.0 + (Denom200 ?? 0) * 200.0 + (Denom100 ?? 0) * 100.0
                + (Denom50 ?? 0) * 50.0 + (Denom20 ?? 0) * 20.0 + (Denom10 ?? 0) * 10.0 + (Coins ?? 0);
        }

        OnPropertyChanged(nameof(HasDenominationCounts));

        if (sum > 0)
        {
            _hasNonZeroDenomination = true;
            TotalAmount = sum;
        }
        else if (_hasNonZeroDenomination)
        {
            // Only reset TotalAmount to 0 when clearing previously entered non-zero denominations;
            // preserve scalar-only TotalAmount when denomination fields were already empty/zero.
            _hasNonZeroDenomination = false;
            TotalAmount = 0;
        }
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
    [ObservableProperty] private double? _manualAmount;
    [ObservableProperty] private double _otpAmount;
    [ObservableProperty] private double _tidAmount;
    [ObservableProperty] private double? _amount;
    [ObservableProperty] private string? _tid;
    [ObservableProperty] private string? _batch;
    [ObservableProperty] private string? _otp;
    public Action? OnValueChangedAction { get; set; }

    public bool HasTidOrOtp => HasTidBatch || OtpAmount > 0 || TidAmount > 0;
    public string SummaryDisplay => HasTidBatch
        ? (OtpAmount > 0 ? $"TID: ₹{TidAmount:F2} | OTP: ₹{OtpAmount:F2} | Total: ₹{(Amount ?? 0):F2}" : $"TID: ₹{TidAmount:F2} | Total: ₹{(Amount ?? 0):F2}")
        : (OtpAmount > 0 ? $"OTP: ₹{OtpAmount:F2} | Total: ₹{(Amount ?? 0):F2}" : "");

    private bool _isRecalculating;

    public void RecalculateTotal()
    {
        _isRecalculating = true;
        try
        {
            double itemized = TidAmount + OtpAmount;
            double total = (itemized > 0 || HasTidBatch)
                ? itemized
                : (ManualAmount ?? 0);
            Amount = total > 0 ? total : (double?)null;
        }
        finally
        {
            _isRecalculating = false;
        }
        OnPropertyChanged(nameof(SummaryDisplay));
        OnPropertyChanged(nameof(HasTidOrOtp));
        OnValueChangedAction?.Invoke();
    }

    partial void OnAmountChanged(double? value)
    {
        if (!_isRecalculating)
        {
            if (HasTidBatch || TidAmount > 0 || OtpAmount > 0)
            {
                double manual = Math.Max(0, (value ?? 0) - TidAmount - OtpAmount);
                ManualAmount = manual > 0 ? manual : (double?)null;
            }
            else
            {
                ManualAmount = value;
            }
        }
        OnPropertyChanged(nameof(SummaryDisplay));
        OnPropertyChanged(nameof(HasTidOrOtp));
        OnValueChangedAction?.Invoke();
    }

    partial void OnManualAmountChanged(double? value)
    {
        if (!_isRecalculating)
        {
            RecalculateTotal();
        }
    }

    partial void OnTidAmountChanged(double value)
    {
        if (!_isRecalculating)
        {
            RecalculateTotal();
        }
    }

    partial void OnTidChanged(string? value) => OnValueChangedAction?.Invoke();
    partial void OnBatchChanged(string? value) => OnValueChangedAction?.Invoke();
    partial void OnOtpChanged(string? value) => OnValueChangedAction?.Invoke();
}

public partial class DsmOtpTransactionRow : ObservableObject
{
    [ObservableProperty] private string _collectionTypeCode = "CREDIT_CARD";
    [ObservableProperty] private string _slot = "General";
    [ObservableProperty] private double _amount;
    [ObservableProperty] private string? _otp;
    [ObservableProperty] private string? _tid;
    [ObservableProperty] private string? _batch;
    public Action? OnRowChanged { get; set; }

    partial void OnAmountChanged(double value) => OnRowChanged?.Invoke();
    partial void OnOtpChanged(string? value) => OnRowChanged?.Invoke();
    partial void OnCollectionTypeCodeChanged(string value) => OnRowChanged?.Invoke();
    partial void OnSlotChanged(string value) => OnRowChanged?.Invoke();
    partial void OnTidChanged(string? value) => OnRowChanged?.Invoke();
    partial void OnBatchChanged(string? value) => OnRowChanged?.Invoke();
}

public partial class ManualCollectionEntryRow : ObservableObject
{
    public string Code { get; set; } = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _category = "Online";
    [ObservableProperty] private double? _amount;
    public Action? OnValueChangedAction { get; set; }

    partial void OnAmountChanged(double? value) => OnValueChangedAction?.Invoke();
}

public partial class DsmTidTransactionRow : ObservableObject
{
    [ObservableProperty] private string _transactionId = Guid.NewGuid().ToString("D");
    [ObservableProperty] private string _collectionTypeCode = "";
    [ObservableProperty] private string _slot = "General";
    [ObservableProperty] private double _amount;
    [ObservableProperty] private string? _tid;
    [ObservableProperty] private string? _batch;
    public Action? OnRowChanged { get; set; }

    partial void OnAmountChanged(double value) => OnRowChanged?.Invoke();
    partial void OnTidChanged(string? value) => OnRowChanged?.Invoke();
    partial void OnBatchChanged(string? value) => OnRowChanged?.Invoke();
    partial void OnCollectionTypeCodeChanged(string value) => OnRowChanged?.Invoke();
    partial void OnSlotChanged(string value) => OnRowChanged?.Invoke();
}

public partial class DsmEntryViewModel : ObservableObject, IDisposable
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly DsmEntryService _dsmService;
    private readonly DraftService _draftService;
    private readonly IDsmCalculationService _dsmCalculationService;
    private readonly Core.Repositories.INozzleReadingRepository _nozzleRepository;
    private readonly IAgsImportRepository _agsImportRepository;
    private readonly ICollectionTypeService _collectionTypeService;
    private readonly IFeatureToggleService _featureToggleService;
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly PrintService _printService;
    private readonly ICreditorRepaymentRepository? _repaymentRepo;

    private readonly Action _featureChangedHandler;
    private readonly Action _stationConfigChangedHandler;
    private readonly Action _collectionTypesChangedHandler;
    private readonly Action _profileChangedHandler;
    private readonly Action<string, string> _profileRenamedHandler;
    private readonly Action<string> _profileDeletedHandler;
    private readonly Action _debtorChangedHandler;
    private readonly Action _fuelRatesChangedHandler;
    private readonly Action _dsmEntryChangedHandler;
    private readonly Action _operationalDataPulledHandler;
    private readonly FuelPro.Sync.SyncEngine? _syncEngine;
    private readonly Dictionary<int, double?> _autoFilledAgsClosingReadings = new();
    private bool _disposed;


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
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isSaveOutcomeUncertain;
    public bool IsNotSaving => !IsSaving;
    public bool CanSaveCurrentEntry => !IsSaving && !IsSaveOutcomeUncertain && (!IsSaved || IsDirty);

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

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSaveCurrentEntry));
        SaveEntryCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSaveOutcomeUncertainChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSaveCurrentEntry));
        SaveEntryCommand.NotifyCanExecuteChanged();
    }
    [ObservableProperty] private int? _editingEntryId;
    [ObservableProperty] private string _startTime = "08:00 AM";
    [ObservableProperty] private string _endTime = "08:00 PM";
    private double? _savedEntryGrossSales;
    private bool _isHydratingEntry;
    private string? _savedFormSnapshot;

    public bool HasInProgressEntry
    {
        get
        {
            if (_isEditing) return true;
            if (!string.IsNullOrWhiteSpace(DsmName)) return true;
            if (NozzleReadings.Any(r => r.ClosingReading.HasValue || r.IsManualOpeningOverride)) return true;
            if (OtpTransactions.Count > 0) return true;
            if (TidTransactions.Count > 0) return true;
            if (Debits.Count > 0) return true;
            if (Expenses.Count > 0) return true;
            if (TestingRows.Any(t => t.Litres.HasValue && t.Litres.Value > 0)) return true;
            if ((Cash1?.TotalAmount ?? 0) > 0 || (Cash2?.TotalAmount ?? 0) > 0 || (Cash3?.TotalAmount ?? 0) > 0) return true;
            if (DebtorRepayments.Count > 0) return true;
            if (DynamicCollections.Any(d => (d.Amount.HasValue && d.Amount.Value > 0) || !string.IsNullOrWhiteSpace(d.Tid) || !string.IsNullOrWhiteSpace(d.Batch) || !string.IsNullOrWhiteSpace(d.Otp))) return true;
            if (PhonePeMorning.HasValue || PhonePeNight.HasValue || PhonePeCardMorning.HasValue || PhonePeCardNight.HasValue ||
                CreditCardMorning.HasValue || CreditCardNight.HasValue ||
                PetroCardMorning.HasValue || PetroCardNight.HasValue ||
                Others.HasValue || CashDeposit.HasValue || CollectionReduction.HasValue) return true;
            if (!string.IsNullOrWhiteSpace(CardTid) || !string.IsNullOrWhiteSpace(CardBatch) ||
                !string.IsNullOrWhiteSpace(PhonePeTid) || !string.IsNullOrWhiteSpace(PhonePeBatch) ||
                !string.IsNullOrWhiteSpace(PetroCardTid) || !string.IsNullOrWhiteSpace(PetroCardBatch)) return true;
            if (QrPayments.Count > 0 || KhandharePetroleumEntries.Count > 0) return true;
            return false;
        }
    }

    private bool HasActiveUnsavedEdits()
    {
        if (_isEditing) return true;
        if (IsSaved) return IsDirty;
        if (!string.IsNullOrWhiteSpace(DsmName)) return true;
        if (NozzleReadings.Any(r => r.IsManualOpeningOverride || (r.ClosingReading.HasValue && !IsUnmodifiedAutoFilledClosing(r.NozzleNumber, r.ClosingReading.Value)))) return true;
        if (OtpTransactions.Count > 0) return true;
        if (TidTransactions.Count > 0) return true;
        if (Debits.Count > 0) return true;
        if (Expenses.Count > 0) return true;
        if (TestingRows.Any(t => t.Litres.HasValue && t.Litres.Value > 0)) return true;
        if ((Cash1?.TotalAmount ?? 0) > 0 || (Cash2?.TotalAmount ?? 0) > 0 || (Cash3?.TotalAmount ?? 0) > 0) return true;
        if (DebtorRepayments.Count > 0) return true;
        if (DynamicCollections.Any(d => (d.Amount.HasValue && d.Amount.Value > 0) || !string.IsNullOrWhiteSpace(d.Tid) || !string.IsNullOrWhiteSpace(d.Batch) || !string.IsNullOrWhiteSpace(d.Otp))) return true;
        if (PhonePeMorning.HasValue || PhonePeNight.HasValue || PhonePeCardMorning.HasValue || PhonePeCardNight.HasValue ||
            CreditCardMorning.HasValue || CreditCardNight.HasValue ||
            PetroCardMorning.HasValue || PetroCardNight.HasValue ||
            Others.HasValue || CashDeposit.HasValue || CollectionReduction.HasValue) return true;
        if (!string.IsNullOrWhiteSpace(CardTid) || !string.IsNullOrWhiteSpace(CardBatch) ||
            !string.IsNullOrWhiteSpace(PhonePeTid) || !string.IsNullOrWhiteSpace(PhonePeBatch) ||
            !string.IsNullOrWhiteSpace(PetroCardTid) || !string.IsNullOrWhiteSpace(PetroCardBatch)) return true;
        if (QrPayments.Count > 0 || KhandharePetroleumEntries.Count > 0) return true;
        return false;
    }

    private bool IsUnmodifiedAutoFilledClosing(int nozzleNumber, double closingReading)
    {
        return _autoFilledAgsClosingReadings.TryGetValue(nozzleNumber, out var autoClosing)
            && autoClosing.HasValue
            && Math.Abs(autoClosing.Value - closingReading) <= 0.0001;
    }

    private static bool CanDispatchTo(Dispatcher? dispatcher)
    {
        return dispatcher != null
            && !dispatcher.CheckAccess()
            && !dispatcher.HasShutdownStarted
            && !dispatcher.HasShutdownFinished
            && dispatcher.Thread.IsAlive;
    }

    public async Task RefreshOnNavigateAsync()
    {
        await LoadShiftEntriesAsync();
        if (!HasActiveUnsavedEdits() && !IsSaved)
        {
            await LoadNozzlesForPumpAsync();
        }
    }

    // Nozzle readings
    public ObservableCollection<NozzleReadingRow> NozzleReadings { get; } = new();

    [ObservableProperty] private string _phonePeDisplayName = "PhonePe";
    [ObservableProperty] private bool _phonePeHasTidBatch = true;
    [ObservableProperty] private bool _phonePeHasOtp;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPhonePeScalarVisible))]
    private bool _phonePeIsActive;
    [ObservableProperty] private bool _phonePeEnableMorning = true;
    [ObservableProperty] private bool _phonePeEnableNight = true;

    [ObservableProperty] private string _creditCardDisplayName = "Pine Labs Card";
    [ObservableProperty] private bool _creditCardHasTidBatch = true;
    [ObservableProperty] private bool _creditCardHasOtp;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreditCardScalarVisible))]
    private bool _creditCardIsActive;
    [ObservableProperty] private bool _creditCardEnableMorning = true;
    [ObservableProperty] private bool _creditCardEnableNight = true;

    [ObservableProperty] private string _petroCardDisplayName = "PetroCard";
    [ObservableProperty] private bool _petroCardHasTidBatch = true;
    [ObservableProperty] private bool _petroCardHasOtp;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPetroCardScalarVisible))]
    private bool _petroCardIsActive;
    [ObservableProperty] private bool _petroCardEnableMorning = true;
    [ObservableProperty] private bool _petroCardEnableNight = true;

    public bool IsPhonePeTidOrOtp => PhonePeHasTidBatch || PhonePeHasOtp;
    public bool IsCreditCardTidOrOtp => CreditCardHasTidBatch || CreditCardHasOtp;
    public bool IsPetroCardTidOrOtp => PetroCardHasTidBatch || PetroCardHasOtp;

    public bool IsPhonePeScalarVisible => PhonePeIsActive 
        || (PhonePeMorning ?? 0) > 0 
        || (PhonePeNight ?? 0) > 0 
        || !string.IsNullOrWhiteSpace(PhonePeTid) 
        || !string.IsNullOrWhiteSpace(PhonePeTidMorning) 
        || !string.IsNullOrWhiteSpace(PhonePeBatchMorning)
        || !string.IsNullOrWhiteSpace(PhonePeTidNight)
        || !string.IsNullOrWhiteSpace(PhonePeBatchNight);

    public bool IsCreditCardScalarVisible => CreditCardIsActive 
        || (CreditCardMorning ?? 0) > 0 
        || (CreditCardNight ?? 0) > 0 
        || !string.IsNullOrWhiteSpace(CardTid) 
        || !string.IsNullOrWhiteSpace(CreditCardTidMorning) 
        || !string.IsNullOrWhiteSpace(CreditCardBatchMorning)
        || !string.IsNullOrWhiteSpace(CreditCardTidNight)
        || !string.IsNullOrWhiteSpace(CreditCardBatchNight);

    public bool IsPetroCardScalarVisible => PetroCardIsActive 
        || (PetroCardMorning ?? 0) > 0 
        || (PetroCardNight ?? 0) > 0 
        || !string.IsNullOrWhiteSpace(PetroCardTid) 
        || !string.IsNullOrWhiteSpace(PetroCardTidMorning) 
        || !string.IsNullOrWhiteSpace(PetroCardBatchMorning)
        || !string.IsNullOrWhiteSpace(PetroCardTidNight)
        || !string.IsNullOrWhiteSpace(PetroCardBatchNight);

    public bool IsPhonePeSplitEnabled => IsMorningNightSplitEnabled && PhonePeEnableMorning && PhonePeEnableNight;
    public bool IsCreditCardSplitEnabled => IsMorningNightSplitEnabled && CreditCardEnableMorning && CreditCardEnableNight;
    public bool IsPetroCardSplitEnabled => IsMorningNightSplitEnabled && PetroCardEnableMorning && PetroCardEnableNight;

    public bool IsPhonePeNightTidVisible => IsPhonePeSplitEnabled && PhonePeHasTidBatch;
    public bool IsCreditCardNightTidVisible => IsCreditCardSplitEnabled && CreditCardHasTidBatch;
    public bool IsPetroCardNightTidVisible => IsPetroCardSplitEnabled && PetroCardHasTidBatch;

    public string PhonePeMorningLabel => IsPhonePeSplitEnabled ? MorningLabel : "Amount (₹)";
    public string CreditCardMorningLabel => IsCreditCardSplitEnabled ? MorningLabel : "Amount (₹)";
    public string PetroCardMorningLabel => IsPetroCardSplitEnabled ? MorningLabel : "Amount (₹)";

    public string PhonePeMorningTidLabel => IsPhonePeSplitEnabled ? MorningTidLabel : "TID";
    public string CreditCardMorningTidLabel => IsCreditCardSplitEnabled ? MorningTidLabel : "TID";
    public string PetroCardMorningTidLabel => IsPetroCardSplitEnabled ? MorningTidLabel : "TID";

    public string PhonePeMorningBatchLabel => IsPhonePeSplitEnabled ? MorningBatchLabel : "Batch";
    public string CreditCardMorningBatchLabel => IsCreditCardSplitEnabled ? MorningBatchLabel : "Batch";
    public string PetroCardMorningBatchLabel => IsPetroCardSplitEnabled ? MorningBatchLabel : "Batch";

    [ObservableProperty] private double? _phonePeCardMorning;
    [ObservableProperty] private double? _phonePeCardNight;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPhonePeScalarVisible))]
    private double? _phonePeMorning;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPhonePeScalarVisible))]
    private double? _phonePeNight;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreditCardScalarVisible))]
    private double? _creditCardMorning;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreditCardScalarVisible))]
    private double? _creditCardNight;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPetroCardScalarVisible))]
    private double? _petroCardMorning;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPetroCardScalarVisible))]
    private double? _petroCardNight;
    [ObservableProperty] private double? _others;
    [ObservableProperty] private double? _cashDeposit;
    [ObservableProperty] private double? _collectionReduction;
    [ObservableProperty] private string? _collectionReductionRemark;
    public bool IsCollectionReductionEnabled => _featureToggleService?.IsFeatureEnabled("Operations_CollectionReduction", true) ?? true;

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
    partial void OnCollectionReductionChanged(double? value) => RecalculateAll();
    partial void OnCollectionReductionRemarkChanged(string? value) => UpdateDirtyState();
    partial void OnStartTimeChanged(string value) => UpdateDirtyState();
    partial void OnEndTimeChanged(string value) => UpdateDirtyState();
    partial void OnCardTidChanged(string? value) { OnPropertyChanged(nameof(IsCreditCardScalarVisible)); UpdateDirtyState(); }
    partial void OnCardBatchChanged(string? value) { OnPropertyChanged(nameof(IsCreditCardScalarVisible)); UpdateDirtyState(); }
    partial void OnPhonePeTidChanged(string? value) { OnPropertyChanged(nameof(IsPhonePeScalarVisible)); UpdateDirtyState(); }
    partial void OnPhonePeBatchChanged(string? value) { OnPropertyChanged(nameof(IsPhonePeScalarVisible)); UpdateDirtyState(); }
    partial void OnPetroCardTidChanged(string? value) { OnPropertyChanged(nameof(IsPetroCardScalarVisible)); UpdateDirtyState(); }
    partial void OnPetroCardBatchChanged(string? value) { OnPropertyChanged(nameof(IsPetroCardScalarVisible)); UpdateDirtyState(); }
    partial void OnPhonePeTidMorningChanged(string? value) { OnPropertyChanged(nameof(IsPhonePeScalarVisible)); UpdateDirtyState(); }
    partial void OnPhonePeBatchMorningChanged(string? value) { OnPropertyChanged(nameof(IsPhonePeScalarVisible)); UpdateDirtyState(); }
    partial void OnPhonePeTidNightChanged(string? value) { OnPropertyChanged(nameof(IsPhonePeScalarVisible)); UpdateDirtyState(); }
    partial void OnPhonePeBatchNightChanged(string? value) { OnPropertyChanged(nameof(IsPhonePeScalarVisible)); UpdateDirtyState(); }
    partial void OnCreditCardTidMorningChanged(string? value) { OnPropertyChanged(nameof(IsCreditCardScalarVisible)); UpdateDirtyState(); }
    partial void OnCreditCardBatchMorningChanged(string? value) { OnPropertyChanged(nameof(IsCreditCardScalarVisible)); UpdateDirtyState(); }
    partial void OnCreditCardTidNightChanged(string? value) { OnPropertyChanged(nameof(IsCreditCardScalarVisible)); UpdateDirtyState(); }
    partial void OnCreditCardBatchNightChanged(string? value) { OnPropertyChanged(nameof(IsCreditCardScalarVisible)); UpdateDirtyState(); }
    partial void OnPetroCardTidMorningChanged(string? value) { OnPropertyChanged(nameof(IsPetroCardScalarVisible)); UpdateDirtyState(); }
    partial void OnPetroCardBatchMorningChanged(string? value) { OnPropertyChanged(nameof(IsPetroCardScalarVisible)); UpdateDirtyState(); }
    partial void OnPetroCardTidNightChanged(string? value) { OnPropertyChanged(nameof(IsPetroCardScalarVisible)); UpdateDirtyState(); }
    partial void OnPetroCardBatchNightChanged(string? value) { OnPropertyChanged(nameof(IsPetroCardScalarVisible)); UpdateDirtyState(); }

    // Dynamic sections
    public ObservableCollection<DynamicCollectionEntryRow> DynamicCollections { get; } = new();
    public ObservableCollection<ManualCollectionEntryRow> ManualCollections { get; } = new();
    public bool HasManualCollections => ManualCollections.Count > 0;
    public ObservableCollection<CollectionTypeTotalDto> CollectionTypeTotals { get; } = new();
    public bool HasCollectionTypeTotals => CollectionTypeTotals.Count > 0;
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
    [ObservableProperty] private CashDenomRow _cash3;
    public ObservableCollection<DsmDebtorRepaymentRow> DebtorRepayments { get; } = new();
    public bool HasDebtorRepayments => DebtorRepayments.Count > 0;
    public bool IsDebtorRepaymentEnabled => _featureToggleService?.IsFeatureEnabled("Operations_DebtorRepayment", true) ?? true;
    public ObservableCollection<string> AvailableRepaymentModes { get; } = new() { "Cash", "PhonePe", "Google Pay", "Paytm", "Card", "Cheque", "Online" };

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
    public string[] ShiftOptions { get; } = { "A", "B", "C" };
    public ObservableCollection<PumpDisplayItem> PumpOptions { get; } = new();
    public ObservableCollection<PumpDisplayItem> ConnectablePumpOptions { get; } = new();

    // DSM list for current shift
    public ObservableCollection<DsmEntrySummaryDto> ShiftEntries { get; } = new();
    public ObservableCollection<FuelTypeReadingSummary> FuelTypeSummaries { get; } = new();
    public ObservableCollection<FuelTypeReadingSummary> ShiftFuelTypeSummaries { get; } = new();
    [ObservableProperty] private bool _hasShiftFuelTypeSummaries;

    private readonly IDsmProfileRepository _dsmProfileRepo;
    private readonly ICreditorRepository _creditorRepo;
    private readonly IStationConfigurationService _stationConfigService;
    private readonly IShiftCycleService _shiftCycleService;
    private readonly SemaphoreSlim _dynamicTypesSemaphore = new(1, 1);

    public bool IsCrossDsmQrEnabled => _featureToggleService?.IsFeatureEnabled("Operations_CrossDsmQr", true) ?? true;
    public bool IsPersonalLedgerEnabled => _featureToggleService?.IsFeatureEnabled("Operations_PersonalLedger", true) ?? true;
    public string PersonalLedgerTitle => _featureToggleService?.GetFeatureDisplayName("Operations_PersonalLedger", "Personal Ledger") ?? "Personal Ledger";
    public bool IsOtpMachineTrackingEnabled => _featureToggleService?.IsFeatureEnabled("Operations_OtpMachineTracking", true) ?? true;
    private void OnCash1Changed()
    {
        OnPropertyChanged(nameof(ShowCash1DenominationFields));
        OnPropertyChanged(nameof(ShowCash1ScalarField));
        RecalculateAll();
    }

    public bool IsCash3Enabled => (_featureToggleService?.IsFeatureEnabled("Operations_Cash3", true) ?? true) || (Cash3?.TotalAmount ?? 0) > 0;
    public bool IsCash1DenominationsEnabled => (_featureToggleService?.IsFeatureEnabled("Operations_Cash1", true) ?? true) && (_featureToggleService?.IsFeatureEnabled("Operations_Cash1Denominations", false) ?? false);
    public bool ShowCash1DenominationFields => IsCash1DenominationsEnabled || (Cash1?.HasDenominationCounts ?? false);
    public bool ShowCash1ScalarField => !ShowCash1DenominationFields;
    public bool IsCash2DenominationsEnabled => (_featureToggleService?.IsFeatureEnabled("Operations_Cash2", true) ?? true) && (_featureToggleService?.IsFeatureEnabled("Operations_Cash2Denominations", true) ?? true);

    public ObservableCollection<DsmOtpTransactionRow> OtpTransactions { get; } = new();
    public ObservableCollection<string> AvailableOtpCollectionTypes { get; } = new();
    public string[] AvailableOtpSlots { get; } = { "Morning", "Day", "Night", "General" };

    public ObservableCollection<DsmTidTransactionRow> TidTransactions { get; } = new();
    public ObservableCollection<string> AvailableTidCollectionTypes { get; } = new();
    public string[] AvailableTidSlots { get; } = { "Morning", "Day", "Night", "General" };
    public bool HasTidCollectionTypes => AvailableTidCollectionTypes.Count > 0;

    private List<CollectionTypeMaster>? _cachedAllCollectionTypes;
    public void SetCachedCollectionTypesForTesting(List<CollectionTypeMaster>? types) => _cachedAllCollectionTypes = types;

    public void RefreshAvailableOtpCollectionTypes()
    {
        var types = new List<string>();
        if (IsOtpMachineTrackingEnabled)
        {
            var seenNormalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Local helper to add a candidate collection type, strictly deduplicating by normalized code
            void TryAddOtpCandidate(string? rawCode, bool isActive, bool isOtpEligible)
            {
                if (string.IsNullOrWhiteSpace(rawCode)) return;
                if (!isActive || !isOtpEligible) return;

                var norm = PaymentCollection.NormalizeCollectionTypeCode(rawCode);
                var key = string.IsNullOrEmpty(norm) ? rawCode.Trim().ToUpperInvariant() : norm;

                if (seenNormalized.Add(key))
                {
                    // Prefer canonical standard code (e.g. PINELABS_CARD, CREDIT_CARD, PETROCARD, PHONEPE),
                    // otherwise use the trimmed configured code
                    var codeToAdd = (!string.IsNullOrEmpty(norm) && PaymentCollection.IsStandardCollectionCode(norm))
                        ? norm
                        : rawCode.Trim();

                    if (!types.Contains(codeToAdd, StringComparer.OrdinalIgnoreCase))
                    {
                        types.Add(codeToAdd);
                    }
                }
            }

            // 1. Built-in standard payment methods (Credit Card, PetroCard, PhonePe)
            // Determine active/eligible status respecting cached masters if present, otherwise top-level flags
            var ccMaster = _cachedAllCollectionTypes?.FirstOrDefault(c =>
                string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Code), "CREDIT_CARD", StringComparison.OrdinalIgnoreCase));
            bool ccEligible = ccMaster != null && ccMaster.IsOtpEligible;
            bool ccActive = _cachedAllCollectionTypes != null
                ? (ccMaster != null && ccMaster.IsActive && CreditCardIsActive)
                : CreditCardIsActive;
            TryAddOtpCandidate("CREDIT_CARD", ccActive, ccEligible);

            var pcMaster = _cachedAllCollectionTypes?.FirstOrDefault(c =>
                string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Code), "PETROCARD", StringComparison.OrdinalIgnoreCase));
            bool pcEligible = pcMaster != null && pcMaster.IsOtpEligible;
            bool pcActive = _cachedAllCollectionTypes != null
                ? (pcMaster != null && pcMaster.IsActive && PetroCardIsActive)
                : PetroCardIsActive;
            TryAddOtpCandidate("PETROCARD", pcActive, pcEligible);

            var phMaster = _cachedAllCollectionTypes?.FirstOrDefault(c =>
                string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Code), "PHONEPE", StringComparison.OrdinalIgnoreCase));
            bool phEligible = phMaster != null && phMaster.IsOtpEligible;
            bool phActive = _cachedAllCollectionTypes != null
                ? (phMaster != null && phMaster.IsActive && PhonePeIsActive)
                : PhonePeIsActive;
            TryAddOtpCandidate("PHONEPE", phActive, phEligible);

            // 2. Include all active, OTP-eligible developer-configured collection types from _cachedAllCollectionTypes
            if (_cachedAllCollectionTypes != null)
            {
                foreach (var master in _cachedAllCollectionTypes)
                {
                    if (master == null || string.IsNullOrWhiteSpace(master.Code)) continue;
                    var norm = PaymentCollection.NormalizeCollectionTypeCode(master.Code);
                    bool isBuiltIn = norm is "CREDIT_CARD" or "PETROCARD" or "PHONEPE";
                    if (isBuiltIn) continue; // Handled above with shift-specific active checks

                    TryAddOtpCandidate(master.Code, master.IsActive, master.IsOtpEligible);
                }
            }

            // 3. Fallback: Check DynamicCollections if _cachedAllCollectionTypes was not yet populated
            foreach (var dyn in DynamicCollections)
            {
                if (dyn == null || string.IsNullOrWhiteSpace(dyn.Code)) continue;
                var master = _cachedAllCollectionTypes?.FirstOrDefault(c =>
                    string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Code), PaymentCollection.NormalizeCollectionTypeCode(dyn.Code), StringComparison.OrdinalIgnoreCase));
                bool isEligible = master != null && master.IsOtpEligible;
                bool isActive = master?.IsActive ?? true;
                TryAddOtpCandidate(dyn.Code, isActive, isEligible);
            }

            // 4. Preserve existing row selections from OtpTransactions (ensuring no duplicate entries)
            foreach (var tx in OtpTransactions)
            {
                if (tx == null || string.IsNullOrWhiteSpace(tx.CollectionTypeCode)) continue;
                var norm = PaymentCollection.NormalizeCollectionTypeCode(tx.CollectionTypeCode);
                var master = _cachedAllCollectionTypes?.FirstOrDefault(c => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Code), norm, StringComparison.OrdinalIgnoreCase));
                if (master != null && master.IsManualOnly) continue;
                var key = string.IsNullOrEmpty(norm) ? tx.CollectionTypeCode.Trim().ToUpperInvariant() : norm;
                if (seenNormalized.Add(key))
                {
                    var codeToAdd = (!string.IsNullOrEmpty(norm) && PaymentCollection.IsStandardCollectionCode(norm))
                        ? norm
                        : tx.CollectionTypeCode.Trim();
                    if (!types.Contains(codeToAdd, StringComparer.OrdinalIgnoreCase))
                    {
                        types.Add(codeToAdd);
                    }
                }
            }
        }

        AvailableOtpCollectionTypes.Clear();
        foreach (var t in types) AvailableOtpCollectionTypes.Add(t);
    }

    [RelayCommand]
    public void AddOtpTransaction()
    {
        var defaultSlot = IsDayShift ? "Day" : (IsNightShift ? "Night" : "Morning");
        var defaultType = AvailableOtpCollectionTypes.FirstOrDefault() ?? "CREDIT_CARD";
        var row = new DsmOtpTransactionRow
        {
            CollectionTypeCode = defaultType,
            Slot = defaultSlot,
            Amount = 0,
            OnRowChanged = OnOtpTransactionsChanged
        };
        var (tid, batch) = GetSharedTidBatch(row.CollectionTypeCode, row.Slot);
        row.Tid = tid;
        row.Batch = batch;
        OtpTransactions.Add(row);
        OnOtpTransactionsChanged();
    }

    [RelayCommand]
    public void RemoveOtpTransaction(DsmOtpTransactionRow? row)
    {
        if (row != null && OtpTransactions.Contains(row))
        {
            OtpTransactions.Remove(row);
            OnOtpTransactionsChanged();
        }
    }

    public void RefreshAvailableTidCollectionTypes()
    {
        var types = new List<string>();
        var seenNormalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAddTidCandidate(string? rawCode, bool isActive, bool hasTidBatch)
        {
            if (string.IsNullOrWhiteSpace(rawCode)) return;
            if (!isActive || !hasTidBatch) return;

            var norm = PaymentCollection.NormalizeCollectionTypeCode(rawCode);
            var key = string.IsNullOrEmpty(norm) ? rawCode.Trim().ToUpperInvariant() : norm;

            if (seenNormalized.Add(key))
            {
                var codeToAdd = (!string.IsNullOrEmpty(norm) && PaymentCollection.IsStandardCollectionCode(norm))
                    ? norm
                    : rawCode.Trim();

                if (!types.Contains(codeToAdd, StringComparer.OrdinalIgnoreCase))
                {
                    types.Add(codeToAdd);
                }
            }
        }

        // 1. Built-in scalar methods only if active and configured for TID/Batch
        var phMaster = _cachedAllCollectionTypes?.FirstOrDefault(c =>
            string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Code), "PHONEPE", StringComparison.OrdinalIgnoreCase));
        bool phActive = _cachedAllCollectionTypes != null
            ? (phMaster != null && phMaster.IsActive && PhonePeIsActive)
            : PhonePeIsActive;
        bool phEligible = phMaster != null ? phMaster.HasTidBatch : PhonePeHasTidBatch;
        if (phActive && phEligible)
        {
            TryAddTidCandidate("PHONEPE", true, true);
        }

        var pcMaster = _cachedAllCollectionTypes?.FirstOrDefault(c =>
            string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Code), "PETROCARD", StringComparison.OrdinalIgnoreCase));
        bool pcActive = _cachedAllCollectionTypes != null
            ? (pcMaster != null && pcMaster.IsActive && PetroCardIsActive)
            : PetroCardIsActive;
        bool pcEligible = pcMaster != null ? pcMaster.HasTidBatch : PetroCardHasTidBatch;
        if (pcActive && pcEligible)
        {
            TryAddTidCandidate("PETROCARD", true, true);
        }

        var ccMaster = _cachedAllCollectionTypes?.FirstOrDefault(c =>
            string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Code), "CREDIT_CARD", StringComparison.OrdinalIgnoreCase));
        bool ccActive = _cachedAllCollectionTypes != null
            ? (ccMaster != null && ccMaster.IsActive && CreditCardIsActive)
            : CreditCardIsActive;
        bool ccEligible = ccMaster != null ? ccMaster.HasTidBatch : CreditCardHasTidBatch;
        if (ccActive && ccEligible)
        {
            TryAddTidCandidate("CREDIT_CARD", true, true);
        }

        // 2. Active configured dynamic collection types
        foreach (var dyn in DynamicCollections)
        {
            var master = _cachedAllCollectionTypes?.FirstOrDefault(m => string.Equals(m.Code, dyn.Code, StringComparison.OrdinalIgnoreCase));
            bool isEligible = master != null ? master.HasTidBatch : dyn.HasTidBatch;
            bool isActive = master?.IsActive ?? true;
            TryAddTidCandidate(dyn.Code, isActive, isEligible);
        }

        if (_cachedAllCollectionTypes != null)
        {
            foreach (var master in _cachedAllCollectionTypes.Where(m => m.HasTidBatch && m.IsActive))
            {
                TryAddTidCandidate(master.Code, true, true);
            }
        }

        // 3. Preserve any already entered rows
        foreach (var tx in TidTransactions)
        {
            if (tx == null || string.IsNullOrWhiteSpace(tx.CollectionTypeCode)) continue;
            var norm = PaymentCollection.NormalizeCollectionTypeCode(tx.CollectionTypeCode);
            var master = _cachedAllCollectionTypes?.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), norm, StringComparison.OrdinalIgnoreCase));
            if (master != null && master.IsManualOnly) continue;
            var key = string.IsNullOrEmpty(norm) ? tx.CollectionTypeCode.Trim().ToUpperInvariant() : norm;
            if (seenNormalized.Add(key))
            {
                var codeToAdd = (!string.IsNullOrEmpty(norm) && PaymentCollection.IsStandardCollectionCode(norm))
                    ? norm
                    : tx.CollectionTypeCode.Trim();
                if (!types.Contains(codeToAdd, StringComparer.OrdinalIgnoreCase))
                {
                    types.Add(codeToAdd);
                }
            }
        }

        AvailableTidCollectionTypes.Clear();
        foreach (var t in types) AvailableTidCollectionTypes.Add(t);
        OnPropertyChanged(nameof(HasTidCollectionTypes));
    }

    [RelayCommand]
    public void AddTidTransaction()
    {
        var defaultSlot = IsDayShift ? "Day" : (IsNightShift ? "Night" : "Morning");
        var defaultType = AvailableTidCollectionTypes.FirstOrDefault() ?? "PINELABS_CARD";
        var row = new DsmTidTransactionRow
        {
            TransactionId = Guid.NewGuid().ToString("D"),
            CollectionTypeCode = defaultType,
            Slot = defaultSlot,
            Amount = 0,
            OnRowChanged = OnTidTransactionsChanged
        };
        TidTransactions.Add(row);
        OnTidTransactionsChanged();
    }

    [RelayCommand]
    public void RemoveTidTransaction(DsmTidTransactionRow? row)
    {
        if (row != null && TidTransactions.Contains(row))
        {
            TidTransactions.Remove(row);
            var norm = PaymentCollection.NormalizeCollectionTypeCode(row.CollectionTypeCode);
            if (!TidTransactions.Any(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == norm))
            {
                if (norm == "PHONEPE") { PhonePeMorning = null; PhonePeNight = null; }
                else if (norm == "PETROCARD") { PetroCardMorning = null; PetroCardNight = null; }
            }
            OnTidTransactionsChanged();
        }
    }

    public void OnTidTransactionsChanged()
    {
        var phTxs = TidTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PHONEPE").ToList();
        var pcTxs = TidTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PETROCARD").ToList();

        if (!_isHydratingEntry)
        {
            if (phTxs.Count > 0)
            {
                if (IsDayShift) PhonePeMorning = phTxs.Sum(t => t.Amount);
                else
                {
                    var morningAmt = phTxs.Where(t => t.Slot != "Night").Sum(t => t.Amount);
                    var nightAmt = phTxs.Where(t => t.Slot == "Night").Sum(t => t.Amount);
                    PhonePeMorning = morningAmt > 0 ? morningAmt : (double?)null;
                    if (IsNightShift) PhonePeNight = nightAmt > 0 ? nightAmt : (double?)null;
                }
            }

            if (pcTxs.Count > 0)
            {
                if (IsDayShift) PetroCardMorning = pcTxs.Sum(t => t.Amount);
                else
                {
                    var morningAmt = pcTxs.Where(t => t.Slot != "Night").Sum(t => t.Amount);
                    var nightAmt = pcTxs.Where(t => t.Slot == "Night").Sum(t => t.Amount);
                    PetroCardMorning = morningAmt > 0 ? morningAmt : (double?)null;
                    if (IsNightShift) PetroCardNight = nightAmt > 0 ? nightAmt : (double?)null;
                }
            }
        }

        foreach (var dynRow in DynamicCollections)
        {
            if (dynRow.HasTidBatch)
            {
                var matching = TidTransactions
                    .Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode), 
                                              PaymentCollection.NormalizeCollectionTypeCode(dynRow.Code), 
                                              StringComparison.OrdinalIgnoreCase))
                    .ToList();
                dynRow.TidAmount = matching.Sum(t => t.Amount);
                dynRow.RecalculateTotal();
            }
        }
        RecalculateAll();
    }

    public (string? tid, string? batch) GetSharedTidBatch(string? typeCode, string? slot)
    {
        var code = (typeCode ?? "").ToUpperInvariant().Replace("_", "").Replace(" ", "");
        if (code == "CREDITCARD")
        {
            if (IsDayShift || slot == "Day") return (CreditCardTidMorning, CreditCardBatchMorning);
            if (slot == "Night") return (CreditCardTidNight, CreditCardBatchNight);
            return (CreditCardTidMorning, CreditCardBatchMorning);
        }
        if (code == "PETROCARD")
        {
            if (IsDayShift || slot == "Day") return (PetroCardTidMorning, PetroCardBatchMorning);
            if (slot == "Night") return (PetroCardTidNight, PetroCardBatchNight);
            return (PetroCardTidMorning, PetroCardBatchMorning);
        }
        if (code == "PHONEPE" || code == "PHONEPECARD")
        {
            if (IsDayShift || slot == "Day") return (PhonePeTidMorning, PhonePeBatchMorning);
            if (slot == "Night") return (PhonePeTidNight, PhonePeBatchNight);
            return (PhonePeTidMorning, PhonePeBatchMorning);
        }
        var dyn = DynamicCollections.FirstOrDefault(d => string.Equals(d.Code, typeCode, StringComparison.OrdinalIgnoreCase));
        if (dyn != null)
        {
            return (dyn.Tid, dyn.Batch);
        }
        return (null, null);
    }

    public void OnOtpTransactionsChanged()
    {
        if (!IsOtpMachineTrackingEnabled)
        {
            RecalculateAll();
            return;
        }

        foreach (var dynRow in DynamicCollections)
        {
            var dynRows = OtpTransactions.Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode), PaymentCollection.NormalizeCollectionTypeCode(dynRow.Code), StringComparison.OrdinalIgnoreCase)).ToList();
            dynRow.OtpAmount = dynRows.Sum(t => t.Amount);
            dynRow.RecalculateTotal();
        }

        RecalculateAll();
    }

    public void UpdateCollectionTypeTotals()
    {
        var totals = new List<CollectionTypeTotalDto>();
        var candidateCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in TidTransactions)
        {
            if (!string.IsNullOrWhiteSpace(tx.CollectionTypeCode))
            {
                candidateCodes.Add(PaymentCollection.NormalizeCollectionTypeCode(tx.CollectionTypeCode));
            }
        }
        foreach (var tx in OtpTransactions)
        {
            if (!string.IsNullOrWhiteSpace(tx.CollectionTypeCode))
            {
                candidateCodes.Add(PaymentCollection.NormalizeCollectionTypeCode(tx.CollectionTypeCode));
            }
        }

        if (_cachedAllCollectionTypes != null)
        {
            foreach (var c in _cachedAllCollectionTypes.Where(m => (m.HasTidBatch || m.IsOtpEligible) && m.IsActive))
            {
                candidateCodes.Add(PaymentCollection.NormalizeCollectionTypeCode(c.Code));
            }
        }

        foreach (var code in candidateCodes)
        {
            double tidSum = TidTransactions
                .Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode), code, StringComparison.OrdinalIgnoreCase))
                .Sum(t => t.Amount);

            double otpSum = OtpTransactions
                .Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode), code, StringComparison.OrdinalIgnoreCase))
                .Sum(t => t.Amount);

            if (tidSum > 0 || otpSum > 0)
            {
                var master = _cachedAllCollectionTypes?.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), code, StringComparison.OrdinalIgnoreCase));
                string displayName = master?.DisplayName ?? (code switch
                {
                    "PHONEPE" => "PhonePe",
                    "CREDIT_CARD" => "Credit / Debit Card",
                    "PETROCARD" => "PetroCard",
                    "PINELABS_CARD" => "Pine Labs Card",
                    _ => code.Replace("_", " ")
                });

                totals.Add(new CollectionTypeTotalDto
                {
                    Code = code,
                    DisplayName = displayName,
                    TidAmount = tidSum,
                    OtpAmount = otpSum,
                    ManualAmount = 0
                });
            }
        }

        CollectionTypeTotals.Clear();
        foreach (var item in totals)
        {
            CollectionTypeTotals.Add(item);
        }
        OnPropertyChanged(nameof(HasCollectionTypeTotals));
    }

    public DsmEntryViewModel(IServiceProvider? serviceProvider = null)
    {
        var services = serviceProvider ?? App.Services;
        _serviceProvider = services;
        if (services != null)
        {
            try
            {
                _dsmService = services.GetService<DsmEntryService>();
                _draftService = services.GetService<DraftService>();
                _dsmCalculationService = services.GetService<IDsmCalculationService>() ?? new DsmCalculationService();
                _nozzleRepository = services.GetService<Core.Repositories.INozzleReadingRepository>();
                _agsImportRepository = services.GetService<IAgsImportRepository>();
                _dsmProfileRepo = services.GetService<IDsmProfileRepository>();
                _creditorRepo = services.GetService<ICreditorRepository>();
                _collectionTypeService = services.GetService<ICollectionTypeService>()!;
                _featureToggleService = services.GetService<IFeatureToggleService>()!;
                _stationConfigService = services.GetService<IStationConfigurationService>()!;
                _shiftCycleService = services.GetService<IShiftCycleService>() ?? new ShiftCycleService();
                _printService = services.GetService<PrintService>();
                _repaymentRepo = services.GetService<ICreditorRepaymentRepository>();
                _syncEngine = services.GetService<FuelPro.Sync.SyncEngine>();
            }
            catch (ObjectDisposedException)
            {
                _dsmCalculationService = new DsmCalculationService();
                _shiftCycleService = new ShiftCycleService();
            }
        }
        else
        {
            _dsmCalculationService = new DsmCalculationService();
            _shiftCycleService = new ShiftCycleService();
        }

        Cash1 = new CashDenomRow { CashType = "Cash1", OnTotalChanged = OnCash1Changed };
        Cash2 = new CashDenomRow { CashType = "Cash2", OnTotalChanged = RecalculateAll };
        Cash3 = new CashDenomRow { CashType = "Cash3", OnTotalChanged = RecalculateAll };

        _featureChangedHandler = () =>
        {
            void ApplyFeatureChange()
            {
                OnPropertyChanged(nameof(IsMorningNightSplitEnabled));
                OnPropertyChanged(nameof(MorningLabel));
                OnPropertyChanged(nameof(MorningTidLabel));
                OnPropertyChanged(nameof(MorningBatchLabel));
                OnPropertyChanged(nameof(IsCrossDsmQrEnabled));
                OnPropertyChanged(nameof(IsPersonalLedgerEnabled));
                OnPropertyChanged(nameof(PersonalLedgerTitle));
                OnPropertyChanged(nameof(IsOtpMachineTrackingEnabled));
                OnPropertyChanged(nameof(IsCash3Enabled));
                OnPropertyChanged(nameof(IsCash1DenominationsEnabled));
                OnPropertyChanged(nameof(ShowCash1DenominationFields));
                OnPropertyChanged(nameof(ShowCash1ScalarField));
                OnPropertyChanged(nameof(IsCash2DenominationsEnabled));
                OnPropertyChanged(nameof(IsCollectionReductionEnabled));
                OnPropertyChanged(nameof(IsDebtorRepaymentEnabled));
                _ = LoadDynamicCollectionTypesAsync();
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(ApplyFeatureChange);
            }
            else
            {
                ApplyFeatureChange();
            }
        };
        if (_featureToggleService != null)
        {
            _featureToggleService.FeatureConfigurationChanged += _featureChangedHandler;
        }

        _stationConfigChangedHandler = () =>
        {
            if (_isEditing || HasInProgressEntry) return;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(() =>
                {
                    if (!_isEditing && !HasInProgressEntry)
                    {
                        ReloadPumpOptions();
                        LoadNozzlesForPump();
                    }
                });
            }
            else
            {
                if (!_isEditing && !HasInProgressEntry)
                {
                    ReloadPumpOptions();
                    LoadNozzlesForPump();
                }
            }
        };
        if (_stationConfigService != null)
        {
            _stationConfigService.StationConfigurationChanged += _stationConfigChangedHandler;
        }

        _collectionTypesChangedHandler = () =>
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
        if (_collectionTypeService != null)
        {
            _collectionTypeService.CollectionTypesChanged += _collectionTypesChangedHandler;
        }

        _profileChangedHandler = () =>
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
        FuelPro.Core.Services.DsmEntryService.DsmProfileChanged += _profileChangedHandler;

        _profileRenamedHandler = (oldName, newName) =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(async () =>
                {
                    ApplyDsmProfileRenamed(oldName, newName);
                    await LoadSuggestionsAsync();
                });
            }
            else
            {
                ApplyDsmProfileRenamed(oldName, newName);
                _ = LoadSuggestionsAsync();
            }
        };
        FuelPro.Core.Services.DsmEntryService.DsmProfileRenamed += _profileRenamedHandler;

        _profileDeletedHandler = (deletedName) =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(async () =>
                {
                    ApplyDsmProfileDeleted(deletedName);
                    await LoadSuggestionsAsync();
                });
            }
            else
            {
                ApplyDsmProfileDeleted(deletedName);
                _ = LoadSuggestionsAsync();
            }
        };
        FuelPro.Core.Services.DsmEntryService.DsmProfileDeleted += _profileDeletedHandler;

        _debtorChangedHandler = () =>
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
        FuelPro.Core.Services.DsmEntryService.DebtorChanged += _debtorChangedHandler;

        _fuelRatesChangedHandler = () =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (CanDispatchTo(dispatcher))
            {
                dispatcher!.BeginInvoke(async () => await RefreshFuelRatesAsync());
            }
            else
            {
                _ = RefreshFuelRatesAsync();
            }
        };
        FuelPro.Core.Services.DsmEntryService.FuelRatesChanged += _fuelRatesChangedHandler;

        _dsmEntryChangedHandler = () =>
        {
            void ApplyDsmEntryChanged()
            {
                if (!_isHydratingEntry)
                {
                    _ = LoadShiftEntriesAsync();
                }
                if (!HasActiveUnsavedEdits() && !IsSaved)
                {
                    LoadNozzlesForPump();
                }
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (CanDispatchTo(dispatcher))
            {
                dispatcher!.BeginInvoke(ApplyDsmEntryChanged);
            }
            else
            {
                ApplyDsmEntryChanged();
            }
        };
        FuelPro.Core.Services.DsmEntryService.DsmEntryChanged += _dsmEntryChangedHandler;

        _operationalDataPulledHandler = () =>
        {
            void ApplyOperationalDataPulled()
            {
                if (!_isHydratingEntry)
                {
                    _ = LoadShiftEntriesAsync();
                }

                if (HasActiveUnsavedEdits())
                {
                    return;
                }

                if (IsSaved && !IsDirty && !_isEditing)
                {
                    // Previously saved entry was already committed; reset completed state so incoming
                    // remote readings immediately populate the fresh opening meters.
                    ClearForm();
                    return;
                }

                LoadNozzlesForPump();
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (CanDispatchTo(dispatcher))
            {
                dispatcher!.BeginInvoke(ApplyOperationalDataPulled);
            }
            else
            {
                ApplyOperationalDataPulled();
            }
        };
        if (_syncEngine != null)
        {
            _syncEngine.OperationalDataPulled += _operationalDataPulledHandler;
        }

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
        await _dynamicTypesSemaphore.WaitAsync();
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
                    new() { Code = "PINELABS_CARD", DisplayName = "Pine Labs Card", Category = "Card", HasTidBatch = true, DisplayOrder = 4, IsActive = true, IsSystem = true, CreatedAt = now },
                    new() { Code = "SBI_REDEEM", DisplayName = "SBI Redeem", Category = "Card", HasTidBatch = false, DisplayOrder = 5, IsActive = true, IsSystem = false, CreatedAt = now },
                    new() { Code = "PAYTM", DisplayName = "Paytm", Category = "Online", HasTidBatch = false, DisplayOrder = 6, IsActive = true, IsSystem = false, CreatedAt = now },
                    new() { Code = "QR", DisplayName = "QR / Online", Category = "Online", HasTidBatch = false, DisplayOrder = 7, IsActive = true, IsSystem = false, CreatedAt = now },
                    new() { Code = "MOBIKWIK", DisplayName = "Mobikwik", Category = "Online", HasTidBatch = false, DisplayOrder = 8, IsActive = true, IsSystem = false, CreatedAt = now },
                    new() { Code = "CASH_DEPOSIT", DisplayName = "Cash Deposit (Bank)", Category = "Cash", HasTidBatch = false, DisplayOrder = 9, IsActive = true, IsSystem = true, CreatedAt = now },
                    new() { Code = "OTHERS", DisplayName = "Other Online / UPI", Category = "Other", HasTidBatch = false, DisplayOrder = 10, IsActive = true, IsSystem = true, CreatedAt = now }
                };
            }

            _cachedAllCollectionTypes = allTypes;

            var repModes = new List<string> { "Cash" };
            foreach (var t in allTypes.Where(t => t.IsActive && !string.IsNullOrWhiteSpace(t.DisplayName)))
            {
                if (!repModes.Contains(t.DisplayName, StringComparer.OrdinalIgnoreCase) &&
                    !t.DisplayName.Contains("Cash Deposit", StringComparison.OrdinalIgnoreCase) &&
                    !t.DisplayName.Contains("Bank Cash", StringComparison.OrdinalIgnoreCase))
                {
                    repModes.Add(t.DisplayName);
                }
            }
            if (!repModes.Contains("Cheque", StringComparer.OrdinalIgnoreCase)) repModes.Add("Cheque");
            if (!repModes.Contains("Bank Transfer", StringComparer.OrdinalIgnoreCase)) repModes.Add("Bank Transfer");

            AvailableRepaymentModes.Clear();
            foreach (var m in repModes) AvailableRepaymentModes.Add(m);

            bool IsEnabledForCurrentShift(CollectionTypeMaster type)
            {
                if (IsShiftC) return type.EnableNight;
                if (IsDayShift) return type.EnableDay;
                if (IsNightShift && !IsThreeShiftMode) return type.EnableNight;
                return type.EnableMorning;
            }

            bool hasHistoricalPhonePe = (existingPayment != null && (existingPayment.PhonePe > 0 || existingPayment.PhonePeMorning > 0 || existingPayment.PhonePeNight > 0))
                || (PhonePeMorning.GetValueOrDefault() > 0 || PhonePeNight.GetValueOrDefault() > 0)
                || !string.IsNullOrWhiteSpace(PhonePeTid) || !string.IsNullOrWhiteSpace(PhonePeTidMorning) || !string.IsNullOrWhiteSpace(PhonePeBatchMorning)
                || !string.IsNullOrWhiteSpace(PhonePeTidNight) || !string.IsNullOrWhiteSpace(PhonePeBatchNight);

            bool hasHistoricalCreditCard = (existingPayment != null && (existingPayment.CreditCard > 0 || existingPayment.CreditCardMorning > 0 || existingPayment.CreditCardNight > 0))
                || (CreditCardMorning.GetValueOrDefault() > 0 || CreditCardNight.GetValueOrDefault() > 0)
                || !string.IsNullOrWhiteSpace(CardTid) || !string.IsNullOrWhiteSpace(CreditCardTidMorning) || !string.IsNullOrWhiteSpace(CreditCardBatchMorning)
                || !string.IsNullOrWhiteSpace(CreditCardTidNight) || !string.IsNullOrWhiteSpace(CreditCardBatchNight);

            bool hasHistoricalPetroCard = (existingPayment != null && (existingPayment.PetroCard > 0 || existingPayment.PetroCardMorning > 0 || existingPayment.PetroCardNight > 0))
                || (PetroCardMorning.GetValueOrDefault() > 0 || PetroCardNight.GetValueOrDefault() > 0)
                || !string.IsNullOrWhiteSpace(PetroCardTid) || !string.IsNullOrWhiteSpace(PetroCardTidMorning) || !string.IsNullOrWhiteSpace(PetroCardBatchMorning)
                || !string.IsNullOrWhiteSpace(PetroCardTidNight) || !string.IsNullOrWhiteSpace(PetroCardBatchNight);

            static bool IsPineLabsCardType(CollectionTypeMaster c) =>
                string.Equals(c.Code, "PINELABS_CARD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Code, "PINELAB_CARD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Code, "PINELABS", StringComparison.OrdinalIgnoreCase);

            static bool IsCreditCardType(CollectionTypeMaster c) =>
                string.Equals(c.Code, "CREDIT_CARD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Code, "CREDITCARD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Code, "CREDIT CARD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Code, "CREDIT_DEBIT_CARD", StringComparison.OrdinalIgnoreCase);

            static bool IsPetroCardType(CollectionTypeMaster c) =>
                string.Equals(c.Code, "PETROCARD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Code, "PETRO_CARD", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Code, "PETRO CARD", StringComparison.OrdinalIgnoreCase);

            static bool IsPhonePeType(CollectionTypeMaster c) =>
                string.Equals(c.Code, "PHONEPE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Code, "PHONE_PE", StringComparison.OrdinalIgnoreCase);

            // 1. Resolve built-in PhonePe (active-first)
            var ph = allTypes.FirstOrDefault(c => c.IsActive && IsEnabledForCurrentShift(c) && IsPhonePeType(c))
                  ?? allTypes.FirstOrDefault(c => c.IsActive && IsPhonePeType(c))
                  ?? allTypes.FirstOrDefault(c => IsPhonePeType(c));
            if (ph != null)
            {
                PhonePeDisplayName = ph.DisplayName;
                PhonePeHasTidBatch = ph.HasTidBatch;
                PhonePeIsActive = (ph.IsActive && IsEnabledForCurrentShift(ph)) || hasHistoricalPhonePe;
                PhonePeEnableMorning = ph.EnableMorning;
                PhonePeEnableNight = ph.EnableNight;
            }
            else
            {
                PhonePeDisplayName = "PhonePe";
                PhonePeHasTidBatch = true;
                PhonePeIsActive = hasHistoricalPhonePe;
                PhonePeEnableMorning = true;
                PhonePeEnableNight = true;
            }

            // 2. Resolve built-in Credit Card (prefer active generic CREDIT_CARD; fall back to active PineLabs for display when CREDIT_CARD is inactive)
            var cc = allTypes.FirstOrDefault(c => c.IsActive && IsEnabledForCurrentShift(c) && IsCreditCardType(c))
                  ?? allTypes.FirstOrDefault(c => c.IsActive && IsEnabledForCurrentShift(c) && IsPineLabsCardType(c))
                  ?? allTypes.FirstOrDefault(c => c.IsActive && IsCreditCardType(c))
                  ?? allTypes.FirstOrDefault(c => c.IsActive && IsPineLabsCardType(c))
                  ?? allTypes.FirstOrDefault(c => IsCreditCardType(c));
            if (cc != null)
            {
                CreditCardDisplayName = cc.DisplayName;
                CreditCardHasTidBatch = cc.HasTidBatch;
                CreditCardIsActive = (cc.IsActive && IsEnabledForCurrentShift(cc)) || hasHistoricalCreditCard;
                CreditCardEnableMorning = cc.EnableMorning;
                CreditCardEnableNight = cc.EnableNight;
            }
            else
            {
                CreditCardDisplayName = "Credit / Debit Card";
                CreditCardHasTidBatch = true;
                CreditCardIsActive = hasHistoricalCreditCard;
                CreditCardEnableMorning = true;
                CreditCardEnableNight = true;
            }

            // 3. Resolve built-in PetroCard (active-first)
            var pc = allTypes.FirstOrDefault(c => c.IsActive && IsEnabledForCurrentShift(c) && IsPetroCardType(c))
                  ?? allTypes.FirstOrDefault(c => c.IsActive && IsPetroCardType(c))
                  ?? allTypes.FirstOrDefault(c => IsPetroCardType(c));
            if (pc != null)
            {
                PetroCardDisplayName = pc.DisplayName;
                PetroCardHasTidBatch = pc.HasTidBatch;
                PetroCardIsActive = (pc.IsActive && IsEnabledForCurrentShift(pc)) || hasHistoricalPetroCard;
                PetroCardEnableMorning = pc.EnableMorning;
                PetroCardEnableNight = pc.EnableNight;
            }
            else
            {
                PetroCardDisplayName = "PetroCard";
                PetroCardHasTidBatch = true;
                PetroCardIsActive = hasHistoricalPetroCard;
                PetroCardEnableMorning = true;
                PetroCardEnableNight = true;
            }

            PhonePeHasOtp = ph?.IsOtpEligible ?? false;
            CreditCardHasOtp = cc?.IsOtpEligible ?? false;
            PetroCardHasOtp = pc?.IsOtpEligible ?? false;

            OnPropertyChanged(nameof(PhonePeDisplayName));
            OnPropertyChanged(nameof(PhonePeHasTidBatch));
            OnPropertyChanged(nameof(PhonePeHasOtp));
            OnPropertyChanged(nameof(PhonePeIsActive));
            OnPropertyChanged(nameof(IsPhonePeTidOrOtp));
            OnPropertyChanged(nameof(IsPhonePeScalarVisible));
            OnPropertyChanged(nameof(IsPhonePeSplitEnabled));
            OnPropertyChanged(nameof(PhonePeMorningLabel));
            OnPropertyChanged(nameof(PhonePeMorningTidLabel));
            OnPropertyChanged(nameof(PhonePeMorningBatchLabel));
            OnPropertyChanged(nameof(IsPhonePeNightTidVisible));

            OnPropertyChanged(nameof(CreditCardDisplayName));
            OnPropertyChanged(nameof(CreditCardHasTidBatch));
            OnPropertyChanged(nameof(CreditCardHasOtp));
            OnPropertyChanged(nameof(CreditCardIsActive));
            OnPropertyChanged(nameof(IsCreditCardTidOrOtp));
            OnPropertyChanged(nameof(IsCreditCardScalarVisible));
            OnPropertyChanged(nameof(IsCreditCardSplitEnabled));
            OnPropertyChanged(nameof(CreditCardMorningLabel));
            OnPropertyChanged(nameof(CreditCardMorningTidLabel));
            OnPropertyChanged(nameof(CreditCardMorningBatchLabel));
            OnPropertyChanged(nameof(IsCreditCardNightTidVisible));

            OnPropertyChanged(nameof(PetroCardDisplayName));
            OnPropertyChanged(nameof(PetroCardHasTidBatch));
            OnPropertyChanged(nameof(PetroCardHasOtp));
            OnPropertyChanged(nameof(PetroCardIsActive));
            OnPropertyChanged(nameof(IsPetroCardTidOrOtp));
            OnPropertyChanged(nameof(IsPetroCardScalarVisible));
            OnPropertyChanged(nameof(IsPetroCardSplitEnabled));
            OnPropertyChanged(nameof(PetroCardMorningLabel));
            OnPropertyChanged(nameof(PetroCardMorningTidLabel));
            OnPropertyChanged(nameof(PetroCardMorningBatchLabel));
            OnPropertyChanged(nameof(IsPetroCardNightTidVisible));

            var existingItems = existingPayment?.GetAllItems()?.ToList() ?? new List<PaymentCollectionItem>();
            var matchedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ph != null)
            {
                matchedCodes.Add(ph.Code);
                matchedCodes.Add("PHONEPE");
                matchedCodes.Add("PHONE_PE");
            }
            if (cc != null)
            {
                if (!IsPineLabsCardType(cc))
                {
                    matchedCodes.Add(cc.Code);
                }
                matchedCodes.Add("CREDIT_CARD");
                matchedCodes.Add("CREDITCARD");
                matchedCodes.Add("CREDIT CARD");
                matchedCodes.Add("CREDIT_DEBIT_CARD");
            }
            if (pc != null)
            {
                matchedCodes.Add(pc.Code);
                matchedCodes.Add("PETROCARD");
                matchedCodes.Add("PETRO_CARD");
            }
            matchedCodes.Add("CASH_DEPOSIT");

            var targetTypes = allTypes
                .Where(x => !matchedCodes.Contains(x.Code))
                .Where(x => !x.IsManualOnly)
                .Where(x => (x.IsActive && IsEnabledForCurrentShift(x)) || existingItems.Any(item => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(item.CollectionTypeCode), PaymentCollection.NormalizeCollectionTypeCode(x.Code), StringComparison.OrdinalIgnoreCase) && item.Amount > 0))
                .OrderBy(x => x.DisplayOrder)
                .ToList();

            foreach (var item in existingItems.Where(i => i.Amount > 0 && !PaymentCollection.IsStandardCollectionCode(i.CollectionTypeCode)))
            {
                var normCode = PaymentCollection.NormalizeCollectionTypeCode(item.CollectionTypeCode);
                if (!targetTypes.Any(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Code), normCode, StringComparison.OrdinalIgnoreCase)))
                {
                    targetTypes.Add(new CollectionTypeMaster
                    {
                        Code = normCode,
                        DisplayName = item.CollectionTypeCode,
                        Category = "Card",
                        HasTidBatch = !string.IsNullOrWhiteSpace(item.Tid) || !string.IsNullOrWhiteSpace(item.Batch),
                        IsActive = false
                    });
                }
            }

            // Deduplicate targetTypes by normalized code
            var distinctTargetTypes = new List<CollectionTypeMaster>();
            var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in targetTypes)
            {
                var norm = PaymentCollection.NormalizeCollectionTypeCode(t.Code);
                if (seenCodes.Add(norm))
                {
                    distinctTargetTypes.Add(t);
                }
            }
            targetTypes = distinctTargetTypes;

            // Purge duplicate rows within DynamicCollections by normalized code
            var seenDynCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = DynamicCollections.Count - 1; i >= 0; i--)
            {
                var norm = PaymentCollection.NormalizeCollectionTypeCode(DynamicCollections[i].Code);
                if (!seenDynCodes.Add(norm))
                {
                    DynamicCollections.RemoveAt(i);
                }
            }

            // Synchronize DynamicCollections in place without clearing collection types
            for (int i = DynamicCollections.Count - 1; i >= 0; i--)
            {
                var row = DynamicCollections[i];
                bool isTarget = targetTypes.Any(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Code), PaymentCollection.NormalizeCollectionTypeCode(row.Code), StringComparison.OrdinalIgnoreCase));
                bool hasHistoricalData = existingItems.Any(item => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(item.CollectionTypeCode), PaymentCollection.NormalizeCollectionTypeCode(row.Code), StringComparison.OrdinalIgnoreCase) && item.Amount > 0);
                if (!isTarget && !hasHistoricalData)
                {
                    DynamicCollections.RemoveAt(i);
                }
            }

            for (int i = 0; i < targetTypes.Count; i++)
            {
                var t = targetTypes[i];
                var existingRow = DynamicCollections.FirstOrDefault(d => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(d.Code), PaymentCollection.NormalizeCollectionTypeCode(t.Code), StringComparison.OrdinalIgnoreCase));
                var matchingItems = existingItems.Where(item =>
                    string.Equals(PaymentCollection.NormalizeCollectionTypeCode(item.CollectionTypeCode), PaymentCollection.NormalizeCollectionTypeCode(t.Code), StringComparison.OrdinalIgnoreCase)).ToList();

                var otpItems = matchingItems.Where(item => !string.IsNullOrWhiteSpace(item.Otp)).ToList();
                var nonOtpItems = matchingItems.Where(item => string.IsNullOrWhiteSpace(item.Otp)).ToList();

                double otpPortion = otpItems.Sum(item => item.Amount);
                bool isSingleAggregateHeader = nonOtpItems.Count == 1 && (
                    (otpItems.Count > 1 && Math.Abs(nonOtpItems[0].Amount - otpPortion) < 0.01) ||
                    (otpItems.Count == 1 && Math.Abs(nonOtpItems[0].Amount - otpPortion) < 0.01
                        && string.IsNullOrWhiteSpace(nonOtpItems[0].Tid) && string.IsNullOrWhiteSpace(nonOtpItems[0].Batch)
                        && string.Equals(nonOtpItems[0].Slot?.Trim(), otpItems[0].Slot?.Trim(), StringComparison.OrdinalIgnoreCase))
                );

                bool isTidConfigured = t.HasTidBatch || string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Code), "PINELABS_CARD", StringComparison.OrdinalIgnoreCase);

                var manualItems = nonOtpItems.Where(item =>
                    !item.IsSyntheticSummary && (
                        (item.TransactionId != null && item.TransactionId.StartsWith("manual-scalar-", StringComparison.OrdinalIgnoreCase))
                        || (!isTidConfigured && string.IsNullOrWhiteSpace(item.Tid) && string.IsNullOrWhiteSpace(item.Batch))
                    )).ToList();

                var tidItems = nonOtpItems.Where(item =>
                    !item.IsSyntheticSummary &&
                    !(item.TransactionId != null && item.TransactionId.StartsWith("manual-scalar-", StringComparison.OrdinalIgnoreCase)) &&
                    (!string.IsNullOrWhiteSpace(item.Tid) || !string.IsNullOrWhiteSpace(item.Batch) || isTidConfigured)
                ).ToList();

                double manualPortion = isSingleAggregateHeader ? 0 : manualItems.Sum(item => item.Amount);
                double tidPortion = tidItems.Sum(item => item.Amount);
                var firstManual = nonOtpItems.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Tid) || !string.IsNullOrWhiteSpace(item.Batch))
                    ?? matchingItems.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Tid) || !string.IsNullOrWhiteSpace(item.Batch))
                    ?? nonOtpItems.FirstOrDefault()
                    ?? matchingItems.FirstOrDefault();

                if (existingRow != null)
                {
                    existingRow.DisplayName = t.DisplayName;
                    existingRow.Category = t.Category;
                    existingRow.HasTidBatch = t.HasTidBatch;
                    if (existingPayment != null)
                    {
                        existingRow.ManualAmount = manualPortion > 0 ? manualPortion : (double?)null;
                        existingRow.TidAmount = tidPortion;
                        existingRow.OtpAmount = otpPortion;
                        existingRow.RecalculateTotal();
                        existingRow.Tid = firstManual?.Tid;
                        existingRow.Batch = firstManual?.Batch;
                        existingRow.Otp = firstManual?.Otp;
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
                        ManualAmount = (existingPayment != null && manualPortion > 0) ? manualPortion : (double?)null,
                        TidAmount = existingPayment != null ? tidPortion : 0,
                        OtpAmount = existingPayment != null ? otpPortion : 0,
                        Tid = firstManual?.Tid,
                        Batch = firstManual?.Batch,
                        Otp = firstManual?.Otp,
                        OnValueChangedAction = RecalculateAll
                    };
                    newRow.RecalculateTotal();
                    DynamicCollections.Insert(Math.Min(i, DynamicCollections.Count), newRow);
                }
            }

            // Synchronize Manual-only Collections (Active types where both TID and OTP are disabled)
            var manualTypes = allTypes
                .Where(x => !matchedCodes.Contains(x.Code))
                .Where(x => x.IsManualOnly)
                .Where(x => (x.IsActive && IsEnabledForCurrentShift(x)) || existingItems.Any(item => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(item.CollectionTypeCode), PaymentCollection.NormalizeCollectionTypeCode(x.Code), StringComparison.OrdinalIgnoreCase) && item.Amount > 0))
                .OrderBy(x => x.DisplayOrder)
                .ToList();

            foreach (var item in existingItems.Where(i => i.Amount > 0 && !PaymentCollection.IsStandardCollectionCode(i.CollectionTypeCode)))
            {
                var normCode = PaymentCollection.NormalizeCollectionTypeCode(item.CollectionTypeCode);
                var master = allTypes.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), normCode, StringComparison.OrdinalIgnoreCase));
                bool isManual = master != null 
                    ? master.IsManualOnly
                    : ((item.TransactionId != null && item.TransactionId.StartsWith("manual-scalar-", StringComparison.OrdinalIgnoreCase)) || (string.IsNullOrWhiteSpace(item.Tid) && string.IsNullOrWhiteSpace(item.Batch)));
                if (isManual && !manualTypes.Any(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Code), normCode, StringComparison.OrdinalIgnoreCase)))
                {
                    manualTypes.Add(new CollectionTypeMaster
                    {
                        Code = normCode,
                        DisplayName = item.CollectionTypeCode,
                        Category = "Other",
                        HasTidBatch = false,
                        HasOtp = false,
                        IsActive = false
                    });
                }
            }

            var distinctManualTypes = new List<CollectionTypeMaster>();
            var seenManualCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in manualTypes)
            {
                var norm = PaymentCollection.NormalizeCollectionTypeCode(t.Code);
                if (seenManualCodes.Add(norm))
                {
                    distinctManualTypes.Add(t);
                }
            }
            manualTypes = distinctManualTypes;

            for (int i = ManualCollections.Count - 1; i >= 0; i--)
            {
                var row = ManualCollections[i];
                bool isTarget = manualTypes.Any(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Code), PaymentCollection.NormalizeCollectionTypeCode(row.Code), StringComparison.OrdinalIgnoreCase));
                bool hasHistoricalData = existingItems.Any(item => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(item.CollectionTypeCode), PaymentCollection.NormalizeCollectionTypeCode(row.Code), StringComparison.OrdinalIgnoreCase) && item.Amount > 0);
                if (!isTarget && !hasHistoricalData)
                {
                    ManualCollections.RemoveAt(i);
                }
            }

            for (int i = 0; i < manualTypes.Count; i++)
            {
                var t = manualTypes[i];
                var norm = PaymentCollection.NormalizeCollectionTypeCode(t.Code);
                var existingRow = ManualCollections.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), norm, StringComparison.OrdinalIgnoreCase));
                var matchingItems = existingItems.Where(item => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(item.CollectionTypeCode), norm, StringComparison.OrdinalIgnoreCase)).ToList();
                double manualAmt = matchingItems.Where(item => !item.IsSyntheticSummary && string.IsNullOrWhiteSpace(item.Otp) && string.IsNullOrWhiteSpace(item.Tid) && string.IsNullOrWhiteSpace(item.Batch)).Sum(item => item.Amount);

                if (existingRow != null)
                {
                    existingRow.DisplayName = t.DisplayName;
                    existingRow.Category = t.Category;
                    if (existingPayment != null)
                    {
                        existingRow.Amount = manualAmt > 0 ? manualAmt : (double?)null;
                    }
                }
                else
                {
                    var newRow = new ManualCollectionEntryRow
                    {
                        Code = t.Code,
                        DisplayName = t.DisplayName,
                        Category = t.Category,
                        Amount = (existingPayment != null && manualAmt > 0) ? manualAmt : (double?)null,
                        OnValueChangedAction = RecalculateAll
                    };
                    ManualCollections.Insert(Math.Min(i, ManualCollections.Count), newRow);
                }
            }
            OnPropertyChanged(nameof(HasManualCollections));

            if (existingPayment != null)
            {
                _isHydratingEntry = true;
                try
                {
                    OtpTransactions.Clear();
                    var allItems = existingPayment.GetAllItems()?.ToList() ?? new List<PaymentCollectionItem>();
                    foreach (var item in allItems)
                    {
                        if (!string.IsNullOrWhiteSpace(item.Otp))
                        {
                            var normType = PaymentCollection.NormalizeCollectionTypeCode(item.CollectionTypeCode);
                            var slot = !string.IsNullOrWhiteSpace(item.Slot) ? item.Slot : PaymentCollection.ExtractSlotFromMode(item.CollectionTypeCode, "General");
                            var (sharedTid, sharedBatch) = GetSharedTidBatch(normType, slot);
                            OtpTransactions.Add(new DsmOtpTransactionRow
                            {
                                CollectionTypeCode = normType,
                                Amount = item.Amount,
                                Otp = item.Otp,
                                Tid = string.IsNullOrWhiteSpace(item.Tid) ? sharedTid : item.Tid,
                                Batch = string.IsNullOrWhiteSpace(item.Batch) ? sharedBatch : item.Batch,
                                Slot = slot,
                                OnRowChanged = OnOtpTransactionsChanged
                            });
                        }
                    }

                    TidTransactions.Clear();
                    foreach (var item in allItems)
                    {
                        if (string.IsNullOrWhiteSpace(item.Otp) && item.Amount > 0 && !item.IsSyntheticSummary)
                        {
                            var normType = PaymentCollection.NormalizeCollectionTypeCode(item.CollectionTypeCode);
                            var master = allTypes.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), normType, StringComparison.OrdinalIgnoreCase));
                            bool isManualOnly = master != null && master.IsManualOnly;
                            bool isExplicitManualScalar = !string.IsNullOrWhiteSpace(item.TransactionId) && item.TransactionId.StartsWith("manual-scalar-", StringComparison.OrdinalIgnoreCase);
                            bool hasTidOrBatch = !string.IsNullOrWhiteSpace(item.Tid) || !string.IsNullOrWhiteSpace(item.Batch);
                            bool isTidConfigured = master != null && master.HasTidBatch;
                            bool isTidItem = !isExplicitManualScalar &&
                                (hasTidOrBatch || string.Equals(normType, "PINELABS_CARD", StringComparison.OrdinalIgnoreCase) || isTidConfigured);

                            if (isTidItem && (!PaymentCollection.IsStandardCollectionCode(normType) || normType is "PHONEPE" or "PETROCARD" or "CREDIT_CARD"))
                            {
                                var slot = !string.IsNullOrWhiteSpace(item.Slot) ? item.Slot : "General";
                                TidTransactions.Add(new DsmTidTransactionRow
                                {
                                    TransactionId = !string.IsNullOrWhiteSpace(item.TransactionId) ? item.TransactionId : Guid.NewGuid().ToString("D"),
                                    CollectionTypeCode = normType,
                                    Amount = item.Amount,
                                    Tid = item.Tid,
                                    Batch = item.Batch,
                                    Slot = slot,
                                    OnRowChanged = OnTidTransactionsChanged
                                });
                            }
                        }
                    }

                    if (TidTransactions.Count > 0)
                    {
                        OnTidTransactionsChanged();
                    }

                    if (OtpTransactions.Count > 0)
                    {
                        OnOtpTransactionsChanged();
                    }
                }
                finally
                {
                    _isHydratingEntry = false;
                }
            }
            else
            {
                if (OtpTransactions.Count > 0)
                {
                    // In-progress entry: update OTP amounts on dynamic collections from existing OtpTransactions
                    foreach (var dynRow in DynamicCollections)
                    {
                        var dynRows = OtpTransactions.Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode), PaymentCollection.NormalizeCollectionTypeCode(dynRow.Code), StringComparison.OrdinalIgnoreCase)).ToList();
                        dynRow.OtpAmount = dynRows.Sum(t => t.Amount);
                        dynRow.RecalculateTotal();
                    }
                }
                if (TidTransactions.Count > 0)
                {
                    OnTidTransactionsChanged();
                }
            }

            RefreshAvailableOtpCollectionTypes();

            // Reconcile row CollectionTypeCode against AvailableOtpCollectionTypes so ComboBox selects accurately
            foreach (var row in OtpTransactions)
            {
                var norm = PaymentCollection.NormalizeCollectionTypeCode(row.CollectionTypeCode);
                var matched = AvailableOtpCollectionTypes.FirstOrDefault(t =>
                    string.Equals(t, row.CollectionTypeCode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t), norm, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    row.CollectionTypeCode = matched;
                }
                else if (!string.IsNullOrWhiteSpace(row.CollectionTypeCode))
                {
                    AvailableOtpCollectionTypes.Add(row.CollectionTypeCode);
                }
            }

            RefreshAvailableTidCollectionTypes();

            foreach (var row in TidTransactions)
            {
                var norm = PaymentCollection.NormalizeCollectionTypeCode(row.CollectionTypeCode);
                var matched = AvailableTidCollectionTypes.FirstOrDefault(t =>
                    string.Equals(t, row.CollectionTypeCode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t), norm, StringComparison.OrdinalIgnoreCase));
                if (matched != null)
                {
                    row.CollectionTypeCode = matched;
                }
                else if (!string.IsNullOrWhiteSpace(row.CollectionTypeCode))
                {
                    AvailableTidCollectionTypes.Add(row.CollectionTypeCode);
                }
            }

            UpdateCollectionTypeTotals();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load dynamic collection types in DsmEntryViewModel");
        }
        finally
        {
            _dynamicTypesSemaphore.Release();
        }
    }



    partial void OnSelectedPumpChanged(PumpDisplayItem value)
    {
        if (value != null)
        {
            if (!_isEditing)
            {
                SelectedConnectedPump = null;
                ActiveConnectedPumpIds = new List<int>();
            }
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
        OnPropertyChanged(nameof(IsThreeShiftMode));
        OnPropertyChanged(nameof(IsMorningNightSplitEnabled));
        OnPropertyChanged(nameof(MorningLabel));
        OnPropertyChanged(nameof(MorningTidLabel));
        OnPropertyChanged(nameof(MorningBatchLabel));
        ReloadPumpOptions();
        if (!_isEditing) LoadNozzlesForPump();
    }

    public bool IsNightShift => IsThreeShiftMode 
        ? string.Equals(_shiftCycleService?.NormalizeShiftType(SelectedShift), "C", StringComparison.OrdinalIgnoreCase)
        : string.Equals(_shiftCycleService?.NormalizeShiftType(SelectedShift), "A", StringComparison.OrdinalIgnoreCase);
    public bool IsDayShift => string.Equals(_shiftCycleService?.NormalizeShiftType(SelectedShift), "B", StringComparison.OrdinalIgnoreCase);
    public bool IsShiftC => string.Equals(_shiftCycleService?.NormalizeShiftType(SelectedShift), "C", StringComparison.OrdinalIgnoreCase);

    public bool IsThreeShiftMode => (_shiftCycleService?.GetShiftsForDate(SelectedDate).Count ?? 0) >= 3;
    public bool IsMorningNightSplitEnabled => !IsThreeShiftMode && (_featureToggleService?.IsFeatureEnabled("Collection_UseMorningNight", false) ?? false) && IsNightShift;

    public string MorningLabel => (!(_featureToggleService?.IsFeatureEnabled("Collection_UseMorningNight", false) ?? false) || IsThreeShiftMode) ? "Amount (₹)" : (IsDayShift ? "Day (8am - 8pm) (₹)" : "Morning (12am - 8am) (₹)");
    public string MorningTidLabel => (!(_featureToggleService?.IsFeatureEnabled("Collection_UseMorningNight", false) ?? false) || IsThreeShiftMode) ? "TID" : (IsDayShift ? "Day TID" : "Morning TID");
    public string MorningBatchLabel => (!(_featureToggleService?.IsFeatureEnabled("Collection_UseMorningNight", false) ?? false) || IsThreeShiftMode) ? "Batch" : (IsDayShift ? "Day Batch" : "Morning Batch");

    partial void OnSelectedShiftChanged(string value)
    {
        OnPropertyChanged(nameof(IsNightShift));
        OnPropertyChanged(nameof(IsDayShift));
        OnPropertyChanged(nameof(IsShiftC));
        OnPropertyChanged(nameof(IsThreeShiftMode));
        OnPropertyChanged(nameof(IsMorningNightSplitEnabled));
        OnPropertyChanged(nameof(MorningLabel));
        OnPropertyChanged(nameof(MorningTidLabel));
        OnPropertyChanged(nameof(MorningBatchLabel));
        OnPropertyChanged(nameof(IsPhonePeSplitEnabled));
        OnPropertyChanged(nameof(PhonePeMorningLabel));
        OnPropertyChanged(nameof(PhonePeMorningTidLabel));
        OnPropertyChanged(nameof(PhonePeMorningBatchLabel));
        OnPropertyChanged(nameof(IsPhonePeNightTidVisible));
        OnPropertyChanged(nameof(IsCreditCardSplitEnabled));
        OnPropertyChanged(nameof(CreditCardMorningLabel));
        OnPropertyChanged(nameof(CreditCardMorningTidLabel));
        OnPropertyChanged(nameof(CreditCardMorningBatchLabel));
        OnPropertyChanged(nameof(IsCreditCardNightTidVisible));
        OnPropertyChanged(nameof(IsPetroCardSplitEnabled));
        OnPropertyChanged(nameof(PetroCardMorningLabel));
        OnPropertyChanged(nameof(PetroCardMorningTidLabel));
        OnPropertyChanged(nameof(PetroCardMorningBatchLabel));
        OnPropertyChanged(nameof(IsPetroCardNightTidVisible));
        _ = LoadDynamicCollectionTypesAsync();
        if (!_isEditing) LoadNozzlesForPump();
    }
    partial void OnDsmNameChanged(string value)
    {
        _ = RefreshConnectedPumpGrossSalesAsync();
        UpdateDsmCumulativeSummary();
        UpdateDirtyState();
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

        // Capture entered values to restore later only when active unsaved user edits exist
        var tempReadings = new Dictionary<int, (double? Opening, double? Closing, double? Rate, bool Override)>();
        bool preserveCurrentEdits = !_isEditing && HasActiveUnsavedEdits();
        if (preserveCurrentEdits)
        {
            foreach (var r in NozzleReadings)
            {
                tempReadings[r.NozzleNumber] = (r.OpeningReading, r.ClosingReading, r.Rate, r.IsManualOpeningOverride);
            }
        }

        _autoFilledAgsClosingReadings.Clear();
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

        // Also inspect the current operational shift for earlier completed/approved entries
        var activeServices = _serviceProvider ?? App.Services;
        if (activeServices != null)
        {
            try
            {
                var shiftRepo = activeServices.GetService<Core.Repositories.IShiftRepository>();
                var entryRepo = activeServices.GetService<IDsmEntryRepository>();
                if (shiftRepo != null && entryRepo != null)
                {
                    var currentShiftResult = await shiftRepo.GetShiftAsync(SelectedDate, SelectedShift);
                    if (currentShiftResult.Success && currentShiftResult.Data != null)
                    {
                        var currentEntriesResult = await entryRepo.GetEntriesForShiftAsync(currentShiftResult.Data.ShiftId);
                        if (currentEntriesResult.Success && currentEntriesResult.Data != null)
                        {
                            var relevantEntries = currentEntriesResult.Data
                                .Where(e => !EditingEntryId.HasValue || (e.ReconciledToPumpId ?? e.DsmEntryId) < EditingEntryId.Value)
                                .OrderBy(e => e.CreatedAt)
                                .ThenBy(e => e.ReconciledToPumpId ?? e.DsmEntryId);

                            foreach (var entry in relevantEntries)
                            {
                                if (entry.NozzleReadings == null) continue;
                                foreach (var nr in entry.NozzleReadings)
                                {
                                    if (nr.ClosingReading > 0)
                                    {
                                        allPreviousClosings[nr.NozzleNumber] = nr.ClosingReading;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to load current shift closings for continuity");
            }
        }

        var currentShiftImport = await GetShiftImportAsync(SelectedDate, SelectedShift);
        var currentAgsReadings = currentShiftImport?.NozzleReadings
            .ToDictionary(r => r.NozzleNumber, r => r);

        var (previousShiftDate, previousShiftType) = _nozzleRepository != null
            ? await _nozzleRepository.ResolvePreviousShiftAsync(SelectedDate, SelectedShift)
            : _shiftCycleService.GetPreviousShift(SelectedDate, SelectedShift);
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
            if (hasPreviousDsm && previousDsmClosing > 0)
            {
                // Prefer latest approved DSM closing reading as authoritative predecessor
                openingReading = previousDsmClosing;
                if (hasCurrentAgs && currentAgsRow?.OpeningReading > openingReading)
                {
                    openingReading = currentAgsRow.OpeningReading;
                }
                else if (openingFromAgsPreviousShift.HasValue && openingFromAgsPreviousShift.Value > openingReading)
                {
                    openingReading = openingFromAgsPreviousShift.Value;
                }
            }
            else if (hasCurrentAgs && currentAgsRow?.OpeningReading > 0)
            {
                openingReading = currentAgsRow.OpeningReading;
            }
            else if (openingFromAgsPreviousShift.HasValue && openingFromAgsPreviousShift.Value > 0)
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
            else if (hasCurrentAgs && currentAgsRow != null)
            {
                _autoFilledAgsClosingReadings[n] = currentAgsRow.ClosingReading;
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

        if (_isEditing && previousSelection.HasValue)
        {
            SelectedConnectedPump = ConnectablePumpOptions.FirstOrDefault(p => p.PumpId == previousSelection.Value);
        }
        else if (!_isEditing && SelectedConnectedPump != null && SelectedPump != null && SelectedConnectedPump.PumpId == SelectedPump.PumpId)
        {
            SelectedConnectedPump = null;
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

    private string? GetPreviousShiftType(string shiftType)
    {
        return _shiftCycleService.GetPreviousShift(SelectedDate, shiftType).ShiftType;
    }

    private void UpdateTestingVisibility()
    {
        ShowTesting = true;
        ShowMsTesting = true;
        ShowHsdTesting = true;
    }

    private void RebuildTestingRows(double hsdRate, double msIRate, double msIIRate, double cngRate)
    {
        var existingTesting = TestingRows.ToDictionary(t => t.NozzleNumber, t => t.Litres);
        TestingRows.Clear();
        foreach (var nozzle in NozzleReadings)
        {
            var tank = FuelPro.Core.Common.PumpConfiguration.GetTankName(SelectedPump?.PumpId ?? 1, nozzle.NozzleNumber, SelectedDate);
            if (!FuelPro.Core.Common.PumpConfiguration.IsTestingEnabledForTank(tank))
            {
                continue;
            }

            double? prevLitres = existingTesting.TryGetValue(nozzle.NozzleNumber, out var l) ? l : null;

            TestingRows.Add(new TestingRow
            {
                NozzleNumber = nozzle.NozzleNumber,
                FuelType = $"Nozzle {nozzle.NozzleNumber} ({nozzle.FuelType})",
                TankName = tank,
                Rate = nozzle.Rate,
                Litres = prevLitres,
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

            if (combined.Contains("SPEED"))
            {
                var speedMatch = ratesMap.FirstOrDefault(kv => kv.Value > 0 && kv.Key.Contains("SPEED", StringComparison.OrdinalIgnoreCase));
                if (speedMatch.Value > 0) return speedMatch.Value;
                return 0.0;
            }

            if (combined.Contains("XP95") || combined.Contains("XP-95"))
            {
                var xpMatch = ratesMap.FirstOrDefault(kv => kv.Value > 0 && (kv.Key.Contains("XP95", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("XP-95", StringComparison.OrdinalIgnoreCase)));
                if (xpMatch.Value > 0) return xpMatch.Value;
                return 0.0;
            }

            if (combined.Contains("POWER"))
            {
                var pwrMatch = ratesMap.FirstOrDefault(kv => kv.Value > 0 && kv.Key.Contains("POWER", StringComparison.OrdinalIgnoreCase));
                if (pwrMatch.Value > 0) return pwrMatch.Value;
                return 0.0;
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
                if (msIIRate > 0) return msIIRate;
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

        // Fallback to standard 4-rate switch (specialized fuels must never fall back to regular petrol)
        string c = $"{(fuelTypeName ?? "")} {(tankName ?? "")}".ToUpperInvariant();
        if (c.Contains("SPEED") || c.Contains("XP95") || c.Contains("POWER") || c.Contains("TURBO") || c.Contains("EXTRA"))
            return 0.0;

        return fuelType switch
        {
            FuelType.HSD => hsdRate,
            FuelType.MS_I => msIRate,
            FuelType.MS_II => msIIRate,
            FuelType.CNG => cngRate,
            _ => msIRate
        };
    }

    /// <summary>
    /// Refreshes fuel rates for currently loaded nozzles and recalculates amounts immediately without resetting readings.
    /// </summary>
    public async Task RefreshFuelRatesAsync()
    {
        try
        {
            var (hsdRate, msIRate, msIIRate, cngRate) = await _dsmService.GetCurrentRatesAsync();
            var ratesMap = await _dsmService.GetFuelRatesMapAsync();

            foreach (var nozzle in NozzleReadings)
            {
                var nozzlePumpId = nozzle.PumpId > 0 ? nozzle.PumpId : (SelectedPump?.PumpId ?? 1);
                var fuelType = PumpConfiguration.GetFuelType(nozzlePumpId, nozzle.NozzleNumber, SelectedDate);
                var tankName = PumpConfiguration.GetTankName(nozzlePumpId, nozzle.NozzleNumber, SelectedDate);
                var fuelTypeName = PumpConfiguration.GetFuelTypeDisplayName(nozzlePumpId, nozzle.NozzleNumber, SelectedDate);

                double newRate = ResolveFuelRate(tankName, fuelTypeName, fuelType, ratesMap, hsdRate, msIRate, msIIRate, cngRate);
                if (newRate > 0)
                {
                    nozzle.Rate = newRate; // Triggers OnRateChanged -> Recalculate() -> OnRowChanged
                }
            }

            foreach (var testRow in TestingRows)
            {
                var matchingNozzle = NozzleReadings.FirstOrDefault(n => n.NozzleNumber == testRow.NozzleNumber);
                if (matchingNozzle != null && matchingNozzle.Rate.HasValue)
                {
                    testRow.Rate = matchingNozzle.Rate.Value;
                }
            }

            RecalculateAll();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to refresh fuel rates in DSM Entry");
        }
    }

    public void RecalculateAll()
    {
        TotalLitres = NozzleReadings.Sum(n => n.SaleLitres);
        UpdateFuelTypeSummaries();
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
        UpdateCollectionTypeTotals();
        UpdateDirtyState();
    }

    private void UpdateFuelTypeSummaries()
    {
        FuelTypeSummaries.Clear();
        var groups = NozzleReadings
            .Where(n => !string.IsNullOrWhiteSpace(n.FuelType))
            .GroupBy(n => n.FuelType.Trim().ToUpperInvariant())
            .OrderBy(g => g.Key);

        foreach (var g in groups)
        {
            var first = g.First();
            var totalLitres = g.Sum(n => n.SaleLitres);
            var rate = g.FirstOrDefault(n => (n.Rate ?? 0) > 0)?.Rate ?? 0;
            var totalAmount = Math.Round(totalLitres * rate, 2);

            FuelTypeSummaries.Add(new FuelTypeReadingSummary
            {
                FuelType = first.FuelType,
                NozzleCount = g.Count(),
                TotalLitres = totalLitres,
                Rate = rate,
                TotalAmount = totalAmount
            });
        }
    }

    [RelayCommand]
    private void AddDebit() { Debits.Add(new DebitRow(Creditors, RecalculateAll)); RecalculateAll(); }

    [RelayCommand]
    private void RemoveDebit(DebitRow? row) { if (row != null) Debits.Remove(row); RecalculateAll(); }

    [RelayCommand]
    private void AddDebtorRepayment()
    {
        DebtorRepayments.Add(new DsmDebtorRepaymentRow
        {
            PaymentMode = AvailableRepaymentModes.FirstOrDefault() ?? "Cash",
            OnRowChanged = () =>
            {
                OnPropertyChanged(nameof(HasDebtorRepayments));
                UpdateDirtyState();
            }
        });
        OnPropertyChanged(nameof(HasDebtorRepayments));
        UpdateDirtyState();
    }

    [RelayCommand]
    private void RemoveDebtorRepayment(DsmDebtorRepaymentRow? row)
    {
        if (row != null)
        {
            DebtorRepayments.Remove(row);
            OnPropertyChanged(nameof(HasDebtorRepayments));
            UpdateDirtyState();
        }
    }

    [RelayCommand]
    private void AddExpense() { Expenses.Add(new ExpenseRow { OnRowChanged = RecalculateAll }); RecalculateAll(); }

    [RelayCommand]
    private void RemoveExpense(ExpenseRow? row) { if (row != null) Expenses.Remove(row); RecalculateAll(); }

    [RelayCommand]
    private void AddKhandharePetroleumEntry() { KhandharePetroleumEntries.Add(new KhandharePetroleumRow { OnRowChanged = RecalculateAll }); RecalculateAll(); }

    [RelayCommand]
    private void RemoveKhandharePetroleumEntry(KhandharePetroleumRow? row) { if (row != null) KhandharePetroleumEntries.Remove(row); RecalculateAll(); }

    [RelayCommand]
    private void AddQrPayment() { QrPayments.Add(new QrPaymentRow { OnRowChanged = RecalculateAll }); RecalculateAll(); }

    [RelayCommand]
    private void RemoveQrPayment(QrPaymentRow? row) { if (row != null) QrPayments.Remove(row); RecalculateAll(); }

    public void UpdateDirtyState()
    {
        if (_isHydratingEntry || _isEditing) return;

        var current = CaptureFormSnapshot();
        bool isDirty = _savedFormSnapshot != null && !string.Equals(_savedFormSnapshot, current, StringComparison.Ordinal);
        if (IsDirty != isDirty)
        {
            IsDirty = isDirty;
        }
    }

    public string CaptureFormSnapshot()
    {
        var sb = new System.Text.StringBuilder(1024);

        // Header
        sb.Append(SelectedDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).Append('|');
        sb.Append((SelectedShift ?? string.Empty).Trim()).Append('|');
        sb.Append((DsmName ?? string.Empty).Trim()).Append('|');
        sb.Append(SelectedPump?.PumpId ?? 0).Append('|');
        sb.Append(SelectedConnectedPump?.PumpId ?? 0).Append('|');
        if (ActiveConnectedPumpIds != null && ActiveConnectedPumpIds.Count > 0)
        {
            sb.Append(string.Join(",", ActiveConnectedPumpIds.OrderBy(x => x)));
        }
        sb.Append('|');
        sb.Append((StartTime ?? string.Empty).Trim()).Append('|');
        sb.Append((EndTime ?? string.Empty).Trim()).Append('|');

        // Nozzles
        foreach (var n in NozzleReadings.OrderBy(x => x.NozzleNumber))
        {
            sb.Append(n.NozzleNumber).Append(':')
              .Append((n.OpeningReading ?? 0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
              .Append((n.ClosingReading ?? 0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
              .Append((n.Rate ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
              .Append(n.IsManualOpeningOverride ? "1" : "0").Append(';');
        }
        sb.Append('|');

        // Payments (Scalars)
        sb.Append((PhonePeCardMorning ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
          .Append((PhonePeCardNight ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
          .Append((PhonePeMorning ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
          .Append((PhonePeNight ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
          .Append((CreditCardMorning ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
          .Append((CreditCardNight ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
          .Append((PetroCardMorning ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
          .Append((PetroCardNight ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
          .Append((Others ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
          .Append((CashDeposit ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
          .Append((CollectionReduction ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';')
          .Append((CollectionReductionRemark ?? string.Empty).Trim()).Append(';');

        // Payment TIDs / Batches
        sb.Append((CardTid ?? string.Empty).Trim()).Append(';')
          .Append((CardBatch ?? string.Empty).Trim()).Append(';')
          .Append((PhonePeTid ?? string.Empty).Trim()).Append(';')
          .Append((PhonePeBatch ?? string.Empty).Trim()).Append(';')
          .Append((PetroCardTid ?? string.Empty).Trim()).Append(';')
          .Append((PetroCardBatch ?? string.Empty).Trim()).Append(';')
          .Append((PhonePeTidMorning ?? string.Empty).Trim()).Append(';')
          .Append((PhonePeBatchMorning ?? string.Empty).Trim()).Append(';')
          .Append((PhonePeTidNight ?? string.Empty).Trim()).Append(';')
          .Append((PhonePeBatchNight ?? string.Empty).Trim()).Append(';')
          .Append((CreditCardTidMorning ?? string.Empty).Trim()).Append(';')
          .Append((CreditCardBatchMorning ?? string.Empty).Trim()).Append(';')
          .Append((CreditCardTidNight ?? string.Empty).Trim()).Append(';')
          .Append((CreditCardBatchNight ?? string.Empty).Trim()).Append(';')
          .Append((PetroCardTidMorning ?? string.Empty).Trim()).Append(';')
          .Append((PetroCardBatchMorning ?? string.Empty).Trim()).Append(';')
          .Append((PetroCardTidNight ?? string.Empty).Trim()).Append(';')
          .Append((PetroCardBatchNight ?? string.Empty).Trim()).Append('|');

        // OtpTransactions
        foreach (var tx in OtpTransactions.OrderBy(t => t.CollectionTypeCode).ThenBy(t => t.Slot).ThenBy(t => t.Otp))
        {
            sb.Append((tx.CollectionTypeCode ?? string.Empty).Trim()).Append(':')
              .Append((tx.Slot ?? string.Empty).Trim()).Append(':')
              .Append(tx.Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
              .Append((tx.Otp ?? string.Empty).Trim()).Append(':')
              .Append((tx.Tid ?? string.Empty).Trim()).Append(':')
              .Append((tx.Batch ?? string.Empty).Trim()).Append(';');
        }
        sb.Append('|');

        // TidTransactions
        foreach (var tx in TidTransactions.OrderBy(t => t.CollectionTypeCode).ThenBy(t => t.Slot))
        {
            sb.Append((tx.CollectionTypeCode ?? string.Empty).Trim()).Append(':')
              .Append((tx.Slot ?? string.Empty).Trim()).Append(':')
              .Append(tx.Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
              .Append((tx.Tid ?? string.Empty).Trim()).Append(':')
              .Append((tx.Batch ?? string.Empty).Trim()).Append(';');
        }
        sb.Append('|');

        // DynamicCollections
        foreach (var d in DynamicCollections.OrderBy(x => x.Code))
        {
            sb.Append((d.Code ?? string.Empty).Trim()).Append(':')
              .Append((d.Amount ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
              .Append((d.ManualAmount ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
              .Append((d.Otp ?? string.Empty).Trim()).Append(':')
              .Append((d.Tid ?? string.Empty).Trim()).Append(':')
              .Append((d.Batch ?? string.Empty).Trim()).Append(';');
        }
        sb.Append('|');

        // Debits
        foreach (var d in Debits.OrderBy(x => x.DebtorName).ThenBy(x => x.Amount))
        {
            sb.Append((d.DebtorName ?? string.Empty).Trim()).Append(':')
              .Append((d.VehicleNumber ?? string.Empty).Trim()).Append(':')
              .Append((d.Amount ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
              .Append((d.PaymentMethod ?? string.Empty).Trim()).Append(':')
              .Append((d.ChequeNo ?? string.Empty).Trim()).Append(':')
              .Append((d.CardTid ?? string.Empty).Trim()).Append(':')
              .Append((d.CardBatch ?? string.Empty).Trim()).Append(';');
        }
        sb.Append('|');

        // TestingRows
        foreach (var t in TestingRows.OrderBy(x => x.NozzleNumber))
        {
            sb.Append(t.NozzleNumber).Append(':')
              .Append((t.Litres ?? 0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
              .Append((t.Rate ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
        }
        sb.Append('|');

        // Expenses
        foreach (var e in Expenses.OrderBy(x => x.Description).ThenBy(x => x.Amount))
        {
            sb.Append((e.Description ?? string.Empty).Trim()).Append(':')
              .Append((e.Amount ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
        }
        sb.Append('|');

        // Cash1, Cash2, Cash3
        AppendCashDenom(sb, Cash1);
        AppendCashDenom(sb, Cash2);
        AppendCashDenom(sb, Cash3);
        sb.Append('|');

        // KhandharePetroleumEntries
        foreach (var k in KhandharePetroleumEntries.OrderBy(x => x.Name).ThenBy(x => x.Amount))
        {
            sb.Append((k.Name ?? string.Empty).Trim()).Append(':')
              .Append((k.VehicleNumber ?? string.Empty).Trim()).Append(':')
              .Append((k.SlipNumber ?? string.Empty).Trim()).Append(':')
              .Append((k.Amount ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
        }
        sb.Append('|');

        // QrPayments
        foreach (var q in QrPayments.OrderBy(x => x.TargetDsmName).ThenBy(x => x.Amount))
        {
            sb.Append((q.TargetDsmName ?? string.Empty).Trim()).Append(':')
              .Append((q.Amount ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
              .Append((q.Tid ?? string.Empty).Trim()).Append(':')
              .Append((q.Batch ?? string.Empty).Trim()).Append(':')
              .Append((q.Slot ?? string.Empty).Trim()).Append(';');
        }
        sb.Append('|');

        // DebtorRepayments
        foreach (var r in DebtorRepayments.OrderBy(x => x.DebtorName).ThenBy(x => x.Amount))
        {
            sb.Append((r.DebtorName ?? string.Empty).Trim()).Append(':')
              .Append((r.Amount ?? 0).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)).Append(':')
              .Append((r.PaymentMode ?? string.Empty).Trim()).Append(':')
              .Append((r.Notes ?? string.Empty).Trim()).Append(';');
        }

        return sb.ToString();
    }

    private static void AppendCashDenom(System.Text.StringBuilder sb, CashDenomRow? c)
    {
        if (c == null)
        {
            sb.Append("null;");
            return;
        }
        sb.Append(c.Denom500 ?? 0).Append(':')
          .Append(c.Denom200 ?? 0).Append(':')
          .Append(c.Denom100 ?? 0).Append(':')
          .Append(c.Denom50 ?? 0).Append(':')
          .Append(c.Denom20 ?? 0).Append(':')
          .Append(c.Denom10 ?? 0).Append(':')
          .Append(c.Coins ?? 0).Append(';');
    }

    private bool CanSaveEntry() => !IsSaving && !IsSaveOutcomeUncertain && (!IsSaved || IsDirty);

    [RelayCommand(CanExecute = nameof(CanSaveEntry), AllowConcurrentExecutions = false)]
    private async Task SaveEntryAsync()
    {
        if (IsSaving || IsSaveOutcomeUncertain) return;

        IsSaving = true;
        StatusMessage = "Saving...";
        try
        {
            var validationErrors = ValidateBeforeSave();
            if (validationErrors.Count > 0)
            {
                ValidationSummary = string.Join(Environment.NewLine, validationErrors);
                StatusMessage = $"❌ Validation failed.{Environment.NewLine}{ValidationSummary}";
                MessageBox.Show(ValidationSummary, "Validation Summary", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var nozzleModels = NozzleReadings.Select(n => new NozzleReading
            {
                NozzleNumber = n.NozzleNumber,
                FuelType = n.FuelType,
                OpeningReading = n.OpeningReading ?? 0,
                ClosingReading = n.ClosingReading ?? 0,
                Rate = n.Rate ?? 0,
                IsManualOpeningOverride = n.IsManualOpeningOverride
            }).ToList();

            var shiftNorm = _shiftCycleService.NormalizeShiftType(SelectedShift);
            var isDay = shiftNorm == "B";
            var isC = shiftNorm == "C";
            var phTxs = TidTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PHONEPE").ToList();
            var pcTxs = TidTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PETROCARD").ToList();

            double? tidPhonePeMorning = phTxs.Count > 0 ? (isDay ? 0 : (isC ? 0 : phTxs.Where(t => t.Slot != "Night").Sum(t => t.Amount))) : null;
            double? tidPhonePeDay = phTxs.Count > 0 ? (isDay ? phTxs.Sum(t => t.Amount) : 0) : null;
            double? tidPhonePeNight = phTxs.Count > 0 ? (isC ? phTxs.Sum(t => t.Amount) : (isDay ? 0 : phTxs.Where(t => t.Slot == "Night").Sum(t => t.Amount))) : null;
            string? tidPhonePeTid = phTxs.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Tid))?.Tid;
            string? tidPhonePeBatch = phTxs.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Batch))?.Batch;

            double? tidPetroMorning = pcTxs.Count > 0 ? (isDay ? 0 : (isC ? 0 : pcTxs.Where(t => t.Slot != "Night").Sum(t => t.Amount))) : null;
            double? tidPetroDay = pcTxs.Count > 0 ? (isDay ? pcTxs.Sum(t => t.Amount) : 0) : null;
            double? tidPetroNight = pcTxs.Count > 0 ? (isC ? pcTxs.Sum(t => t.Amount) : (isDay ? 0 : pcTxs.Where(t => t.Slot == "Night").Sum(t => t.Amount))) : null;
            string? tidPetroTid = pcTxs.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Tid))?.Tid;
            string? tidPetroBatch = pcTxs.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.Batch))?.Batch;

            var payment = new PaymentCollection
            {
                PhonePeCardMorning = (!isDay && !isC) ? (PhonePeCardMorning ?? 0) : 0,
                PhonePeCardDay = isDay ? (PhonePeCardMorning ?? 0) : 0,
                PhonePeCardNight = isC ? (PhonePeCardMorning ?? 0) : (PhonePeCardNight ?? 0),
                PhonePeMorning = tidPhonePeMorning ?? ((!isDay && !isC) ? (PhonePeMorning ?? 0) : 0),
                PhonePeDay = tidPhonePeDay ?? (isDay ? (PhonePeMorning ?? 0) : 0),
                PhonePeNight = tidPhonePeNight ?? (isC ? (PhonePeMorning ?? 0) : (PhonePeNight ?? 0)),
                CreditCardMorning = (!isDay && !isC) ? (CreditCardMorning ?? 0) : 0,
                CreditCardDay = isDay ? (CreditCardMorning ?? 0) : 0,
                CreditCardNight = isC ? (CreditCardMorning ?? 0) : (CreditCardNight ?? 0),
                PetroCardMorning = tidPetroMorning ?? ((!isDay && !isC) ? (PetroCardMorning ?? 0) : 0),
                PetroCardDay = tidPetroDay ?? (isDay ? (PetroCardMorning ?? 0) : 0),
                PetroCardNight = tidPetroNight ?? (isC ? (PetroCardMorning ?? 0) : (PetroCardNight ?? 0)),
                Others = Others ?? 0,
                CashDeposit = Cash1.TotalAmount,
                CollectionReduction = CollectionReduction ?? 0,
                CollectionReductionRemark = CollectionReductionRemark,
                CardTid = isC ? (CreditCardTidNight ?? CreditCardTidMorning) : (isDay ? CreditCardTidMorning : (CreditCardTidMorning ?? CreditCardTidNight)),
                CardBatch = isC ? (CreditCardBatchNight ?? CreditCardBatchMorning) : (isDay ? CreditCardBatchMorning : (CreditCardBatchMorning ?? CreditCardBatchNight)),
                PhonePeTid = tidPhonePeTid ?? (isC ? (PhonePeTidNight ?? PhonePeTidMorning) : (isDay ? PhonePeTidMorning : (PhonePeTidMorning ?? PhonePeTidNight))),
                PhonePeBatch = tidPhonePeBatch ?? (isC ? (PhonePeBatchNight ?? PhonePeBatchMorning) : (isDay ? PhonePeBatchMorning : (PhonePeBatchMorning ?? PhonePeBatchNight))),
                PetroCardTid = tidPetroTid ?? (isC ? (PetroCardTidNight ?? PetroCardTidMorning) : (isDay ? PetroCardTidMorning : (PetroCardTidMorning ?? PetroCardTidNight))),
                PetroCardBatch = tidPetroBatch ?? (isC ? (PetroCardBatchNight ?? PetroCardBatchMorning) : (isDay ? PetroCardBatchMorning : (PetroCardBatchMorning ?? PetroCardBatchNight))),
                PhonePeTidMorning = tidPhonePeTid ?? ((!isDay && !isC) ? PhonePeTidMorning : null),
                PhonePeBatchMorning = tidPhonePeBatch ?? ((!isDay && !isC) ? PhonePeBatchMorning : null),
                PhonePeTidDay = isDay ? (tidPhonePeTid ?? PhonePeTidMorning) : null,
                PhonePeBatchDay = isDay ? (tidPhonePeBatch ?? PhonePeBatchMorning) : null,
                PhonePeTidNight = isC ? (tidPhonePeTid ?? PhonePeTidMorning) : (tidPhonePeTid ?? PhonePeTidNight),
                PhonePeBatchNight = isC ? (tidPhonePeBatch ?? PhonePeBatchMorning) : (tidPhonePeBatch ?? PhonePeBatchNight),
                CreditCardTidMorning = (!isDay && !isC) ? CreditCardTidMorning : null,
                CreditCardBatchMorning = (!isDay && !isC) ? CreditCardBatchMorning : null,
                CreditCardTidDay = isDay ? CreditCardTidMorning : null,
                CreditCardBatchDay = isDay ? CreditCardBatchMorning : null,
                CreditCardTidNight = isC ? CreditCardTidMorning : CreditCardTidNight,
                CreditCardBatchNight = isC ? CreditCardBatchMorning : CreditCardBatchNight,
                PetroCardTidMorning = tidPetroTid ?? ((!isDay && !isC) ? PetroCardTidMorning : null),
                PetroCardBatchMorning = tidPetroBatch ?? ((!isDay && !isC) ? PetroCardBatchMorning : null),
                PetroCardTidDay = isDay ? (tidPetroTid ?? PetroCardTidMorning) : null,
                PetroCardBatchDay = isDay ? (tidPetroBatch ?? PetroCardBatchMorning) : null,
                PetroCardTidNight = isC ? (tidPetroTid ?? PetroCardTidMorning) : (tidPetroTid ?? PetroCardTidNight),
                PetroCardBatchNight = isC ? (tidPetroBatch ?? PetroCardBatchMorning) : (tidPetroBatch ?? PetroCardBatchNight)
            };

            if (IsOtpMachineTrackingEnabled && OtpTransactions.Count > 0)
            {
                foreach (var tx in OtpTransactions)
                {
                    if (tx.Amount <= 0 && string.IsNullOrWhiteSpace(tx.Otp)) continue;
                    var (sharedTid, sharedBatch) = GetSharedTidBatch(tx.CollectionTypeCode, tx.Slot);
                    payment.Items.Add(new PaymentCollectionItem
                    {
                        TransactionId = Guid.NewGuid().ToString("D"),
                        CollectionTypeCode = tx.CollectionTypeCode,
                        Amount = tx.Amount,
                        Tid = string.IsNullOrWhiteSpace(tx.Tid) ? sharedTid : tx.Tid,
                        Batch = string.IsNullOrWhiteSpace(tx.Batch) ? sharedBatch : tx.Batch,
                        Slot = tx.Slot,
                        Otp = tx.Otp
                    });
                }
            }

            if (TidTransactions.Count > 0)
            {
                foreach (var tx in TidTransactions)
                {
                    if (tx.Amount <= 0) continue;
                    payment.Items.Add(new PaymentCollectionItem
                    {
                        TransactionId = string.IsNullOrWhiteSpace(tx.TransactionId) ? Guid.NewGuid().ToString("D") : tx.TransactionId,
                        CollectionTypeCode = PaymentCollection.NormalizeCollectionTypeCode(tx.CollectionTypeCode),
                        Amount = tx.Amount,
                        Tid = tx.Tid,
                        Batch = tx.Batch,
                        Slot = tx.Slot,
                        Otp = null
                    });
                }
            }

            foreach (var manual in ManualCollections)
            {
                if ((manual.Amount ?? 0) > 0)
                {
                    var normCode = PaymentCollection.NormalizeCollectionTypeCode(manual.Code);
                    payment.Items.Add(new PaymentCollectionItem
                    {
                        TransactionId = $"manual-scalar-{normCode.ToLowerInvariant()}",
                        CollectionTypeCode = normCode,
                        Amount = manual.Amount.Value,
                        Tid = null,
                        Batch = null,
                        Slot = "General",
                        Otp = null
                    });
                }
            }

            foreach (var dyn in DynamicCollections)
            {
                var normCode = PaymentCollection.NormalizeCollectionTypeCode(dyn.Code);
                if (PaymentCollection.IsStandardCollectionCode(normCode)) continue;

                double manualAmt = dyn.ManualAmount ?? ((dyn.Amount ?? 0) - dyn.TidAmount - dyn.OtpAmount);
                if (manualAmt > 0 && !payment.Items.Any(i => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(i.CollectionTypeCode), normCode, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(i.Tid) && string.IsNullOrWhiteSpace(i.Batch) && string.IsNullOrWhiteSpace(i.Otp)))
                {
                    payment.Items.Add(new PaymentCollectionItem
                    {
                        TransactionId = $"manual-scalar-{normCode.ToLowerInvariant()}",
                        CollectionTypeCode = normCode,
                        Amount = manualAmt,
                        Tid = null,
                        Batch = null,
                        Slot = "General",
                        Otp = null
                    });
                }
            }

            if (payment.Items.Count > 0)
            {
                payment.DynamicItemsJson = Newtonsoft.Json.JsonConvert.SerializeObject(payment.Items.Select(i => new
                {
                    i.TransactionId,
                    i.CollectionTypeCode,
                    i.Amount,
                    i.Tid,
                    i.Batch,
                    i.Slot,
                    i.Otp
                }));
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
                    Name = !string.IsNullOrWhiteSpace(kp.Name) ? kp.Name : (!string.IsNullOrWhiteSpace(kp.SlipNumber) ? $"Slip #{kp.SlipNumber}" : "Station Drawings"),
                    VehicleNumber = kp.VehicleNumber ?? "",
                    SlipNumber = kp.SlipNumber ?? "",
                    Amount = kp.Amount ?? 0
                }).ToList();

            var qrModels = QrPayments
                .Where(q => (q.Amount ?? 0) > 0)
                .Select(q => new DsmQrPaymentEntry
                {
                    TargetDsmName = !string.IsNullOrWhiteSpace(q.TargetDsmName) ? q.TargetDsmName.Trim() : "General QR",
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

            if (IsCash3Enabled || (Cash3?.TotalAmount ?? 0) > 0)
            {
                cashModels.Add(new()
                {
                    CashType = "Cash3",
                    Denom500 = Cash3.Denom500 ?? 0,
                    Denom200 = Cash3.Denom200 ?? 0,
                    Denom100 = Cash3.Denom100 ?? 0,
                    Denom50 = Cash3.Denom50 ?? 0,
                    Denom20 = Cash3.Denom20 ?? 0,
                    Denom10 = Cash3.Denom10 ?? 0,
                    Coins = Cash3.Coins ?? 0,
                    TotalAmount = Cash3.TotalAmount
                });
            }

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

                if (result.Data != null)
                {
                    double savedGross = (double)result.Data.GrossSales;
                    double savedNetGross = (double)result.Data.NetGrossSales;
                    double testingAmt = (double)(result.Data.TestingEntries?.Sum(t => (decimal)t.Amount) ?? 0m);

                    MeterGrossSales = savedGross;
                    GrossSales = savedGross;
                    TotalTestingAmount = testingAmt;
                    HasTesting = testingAmt > 0;
                    MeterGrossSalesSubtitle = HasTesting
                        ? $"Meter: ₹{MeterGrossSales:N2} - Test: ₹{TotalTestingAmount:N2}"
                        : string.Empty;
                    NetGrossSales = savedNetGross;

                    TotalPaymentIn = (double)result.Data.TotalInDirect;
                    TotalDebtors = (double)result.Data.TotalCreditors;
                    FinalAdjusted = (double)result.Data.TotalCollection;
                    Difference = (double)result.Data.Mismatch;
                    UpdateCollectionTypeTotals();
                }

                // Process Debtor Repayments received directly by DSM into CreditorRepayments table
                if (DebtorRepayments.Count > 0)
                {
                    using var db = App.Services.GetRequiredService<FuelPro.Data.FuelProDbContext>();
                    var shiftDate = SelectedDate.Date;
                    var shiftNum = SelectedShift;
                    var dsmName = DsmName;
                    var dsmTag = $"DSM: {dsmName}";
                    int entryId = EditingEntryId ?? result.Data?.DsmEntryId ?? 0;

                    var existingReps = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                        db.CreditorRepayments
                            .Where(r => (entryId > 0 && r.ChequeNo != null && r.ChequeNo.Contains($"[Entry:{entryId}"))
                                     || (r.RepaymentDate.Date == shiftDate && r.ShiftNumber == shiftNum && (r.ChequeNo != null && r.ChequeNo.Contains(dsmTag)))));

                    var existingRepMap = existingReps.ToDictionary(r => r.CreditorRepaymentId);
                    var claimedIds = new HashSet<int>();
                    bool dbChanged = false;

                    for (int i = 0; i < DebtorRepayments.Count; i++)
                    {
                        var rep = DebtorRepayments[i];
                        if (rep.Amount.HasValue && rep.Amount.Value > 0 && !string.IsNullOrWhiteSpace(rep.DebtorName))
                        {
                            string? entryTag = entryId > 0 ? RepaymentIdentityHelper.BuildEntryTag(entryId, i) : null;
                            string expectedCheque = RepaymentIdentityHelper.FormatChequeNo(rep.Notes, dsmName, entryTag);

                            CreditorRepayment? targetEntity = null;

                            // 1. Primary: match by CreditorRepaymentId
                            if (rep.CreditorRepaymentId > 0 && existingRepMap.TryGetValue(rep.CreditorRepaymentId, out var byId))
                            {
                                targetEntity = byId;
                            }

                            // 2. Secondary: match by entry identity tag
                            if (targetEntity == null && entryId > 0)
                            {
                                targetEntity = existingReps.FirstOrDefault(r => 
                                    !claimedIds.Contains(r.CreditorRepaymentId)
                                    && RepaymentIdentityHelper.MatchesEntry(r.ChequeNo, entryId, i));
                            }

                            // 3. Fallback: match by unlinked row without stealing another row's record
                            if (targetEntity == null)
                            {
                                targetEntity = existingReps.FirstOrDefault(r =>
                                    !claimedIds.Contains(r.CreditorRepaymentId)
                                    && !DebtorRepayments.Any(d => d.CreditorRepaymentId == r.CreditorRepaymentId)
                                    && string.Equals(r.CreditorName, rep.DebtorName.Trim(), StringComparison.OrdinalIgnoreCase)
                                    && Math.Abs(r.Amount - rep.Amount.Value) < 0.01
                                    && string.Equals(r.PaymentMode ?? "Cash", string.IsNullOrWhiteSpace(rep.PaymentMode) ? "Cash" : rep.PaymentMode, StringComparison.OrdinalIgnoreCase));
                            }

                            if (targetEntity != null)
                            {
                                // Update existing record idempotently
                                targetEntity.CreditorName = rep.DebtorName.Trim();
                                targetEntity.Amount = rep.Amount.Value;
                                targetEntity.PaymentMode = string.IsNullOrWhiteSpace(rep.PaymentMode) ? "Cash" : rep.PaymentMode;
                                targetEntity.RepaymentDate = shiftDate;
                                targetEntity.ShiftNumber = shiftNum;
                                targetEntity.ChequeNo = expectedCheque;

                                rep.CreditorRepaymentId = targetEntity.CreditorRepaymentId;
                                claimedIds.Add(targetEntity.CreditorRepaymentId);
                                dbChanged = true;
                            }
                            else
                            {
                                // Insert new record
                                var newEntity = new CreditorRepayment
                                {
                                    CreditorName = rep.DebtorName.Trim(),
                                    Amount = rep.Amount.Value,
                                    PaymentMode = string.IsNullOrWhiteSpace(rep.PaymentMode) ? "Cash" : rep.PaymentMode,
                                    RepaymentDate = shiftDate,
                                    ShiftNumber = shiftNum,
                                    ChequeNo = expectedCheque
                                };
                                await db.CreditorRepayments.AddAsync(newEntity);
                                await db.SaveChangesAsync();

                                rep.CreditorRepaymentId = newEntity.CreditorRepaymentId;
                                claimedIds.Add(newEntity.CreditorRepaymentId);
                                existingReps.Add(newEntity);
                                existingRepMap[newEntity.CreditorRepaymentId] = newEntity;
                                dbChanged = true;
                            }
                        }
                    }
                    if (dbChanged)
                    {
                        await db.SaveChangesAsync();
                    }
                    FuelPro.Core.Services.DsmEntryService.RaiseDebtorChanged();
                }

                StatusMessage = "✅ DSM Entry saved successfully!";
                _draftService.ClearDraft();
                await LoadShiftEntriesAsync();

                IsSaveOutcomeUncertain = false;
                _savedFormSnapshot = CaptureFormSnapshot();
                IsSaved = true;
                IsDirty = false;
                OnPropertyChanged(nameof(CanSaveCurrentEntry));
                SaveEntryCommand.NotifyCanExecuteChanged();

                // Trigger background sync now that transaction is fully committed
                var syncEngine = App.Services.GetService<FuelPro.Sync.SyncEngine>();
                _ = syncEngine?.ForceSyncAsync();
            }
            else
            {
                IsSaveOutcomeUncertain = false;
                StatusMessage = $"❌ {result.Error}";
                IsSaved = false;
            }
        }
        catch (Exception ex)
        {
            IsSaveOutcomeUncertain = true;
            StatusMessage = $"❌ Save outcome is unconfirmed ({ex.Message}). Save is disabled to prevent duplicate entries. Please verify Shift Entries or reload this shift before retrying.";
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
        IsDirty = false;
        IsSaveOutcomeUncertain = false;
        _savedFormSnapshot = null;
        StartTime = "08:00 AM";
        EndTime = "08:00 PM";
        PhonePeCardMorning = PhonePeCardNight = null;
        PhonePeMorning = PhonePeNight = null;
        CreditCardMorning = CreditCardNight = null;
        PetroCardMorning = PetroCardNight = null;
        Others = CashDeposit = CollectionReduction = null;
        CollectionReductionRemark = null;
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
        DebtorRepayments.Clear();
        OnPropertyChanged(nameof(HasDebtorRepayments));
        OtpTransactions.Clear();
        OnOtpTransactionsChanged();
        TidTransactions.Clear();
        OnTidTransactionsChanged();
        foreach (var row in DynamicCollections)
        {
            row.ManualAmount = null;
            row.OtpAmount = 0;
            row.TidAmount = 0;
            row.Amount = null;
            row.Tid = null;
            row.Batch = null;
            row.Otp = null;
            row.RecalculateTotal();
        }
        foreach (var m in ManualCollections)
        {
            m.Amount = null;
        }
        if (DynamicCollections.Count == 0)
        {
            _ = LoadDynamicCollectionTypesAsync();
        }
        RefreshAvailableTidCollectionTypes();
        RefreshAvailableOtpCollectionTypes();
        _autoFilledAgsClosingReadings.Clear();
        NozzleReadings.Clear();
        FuelTypeSummaries.Clear();
        ValidationSummary = "";
        _savedEntryGrossSales = null;
        Cash1 = new CashDenomRow { CashType = "Cash1", OnTotalChanged = OnCash1Changed };
        Cash2 = new CashDenomRow { CashType = "Cash2", OnTotalChanged = RecalculateAll };
        Cash3 = new CashDenomRow { CashType = "Cash3", OnTotalChanged = RecalculateAll };
        LoadNozzlesForPump();
        _ = LoadShiftEntriesAsync();
        RecalculateAll();
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
            var shiftNormEdit = _shiftCycleService.NormalizeShiftType(SelectedShift ?? entry.Shift?.ShiftType ?? "B");
            bool isShiftB = shiftNormEdit == "B";
            bool isShiftC = shiftNormEdit == "C";

            // Extract items-based card fallbacks strictly for CREDIT_CARD when scalar properties are 0 / null
            var cardItems = paymentSource?.GetAllItems()
                ?.Where(i => PaymentCollection.NormalizeCollectionTypeCode(i.CollectionTypeCode) == "CREDIT_CARD")
                .ToList() ?? new List<PaymentCollectionItem>();

            double? cardItemDay = cardItems.Where(i => PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot) == "Day").Sum(i => i.Amount);
            if (cardItemDay <= 0) cardItemDay = null;
            double? cardItemMorning = cardItems.Where(i => PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot) == "Morning").Sum(i => i.Amount);
            if (cardItemMorning <= 0) cardItemMorning = null;
            double? cardItemNight = cardItems.Where(i => PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot) == "Night").Sum(i => i.Amount);
            if (cardItemNight <= 0) cardItemNight = null;

            double unslottedCardAmt = cardItems.Where(i => {
                var slot = PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot);
                return slot != "Day" && slot != "Morning" && slot != "Night";
            }).Sum(i => i.Amount);

            if (unslottedCardAmt > 0)
            {
                if (isShiftB) cardItemDay = (cardItemDay ?? 0) + unslottedCardAmt;
                else if (isShiftC) cardItemNight = (cardItemNight ?? 0) + unslottedCardAmt;
                else cardItemMorning = (cardItemMorning ?? 0) + unslottedCardAmt;
            }

            string? cardItemTid = cardItems.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Tid))?.Tid;
            string? cardItemBatch = cardItems.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Batch))?.Batch;

            // Extract items-based card fallbacks for PetroCard when scalar properties are 0 / null
            var petroItems = paymentSource?.GetAllItems()
                ?.Where(i => {
                    var norm = PaymentCollection.NormalizeCollectionTypeCode(i.CollectionTypeCode);
                    return norm == "PETROCARD";
                }).ToList() ?? new List<PaymentCollectionItem>();

            double? petroItemDay = petroItems.Where(i => PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot) == "Day").Sum(i => i.Amount);
            if (petroItemDay <= 0) petroItemDay = null;
            double? petroItemMorning = petroItems.Where(i => PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot) == "Morning").Sum(i => i.Amount);
            if (petroItemMorning <= 0) petroItemMorning = null;
            double? petroItemNight = petroItems.Where(i => PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot) == "Night").Sum(i => i.Amount);
            if (petroItemNight <= 0) petroItemNight = null;

            double unslottedPetroAmt = petroItems.Where(i => {
                var slot = PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot);
                return slot != "Day" && slot != "Morning" && slot != "Night";
            }).Sum(i => i.Amount);

            if (unslottedPetroAmt > 0)
            {
                if (isShiftB) petroItemDay = (petroItemDay ?? 0) + unslottedPetroAmt;
                else if (isShiftC) petroItemNight = (petroItemNight ?? 0) + unslottedPetroAmt;
                else petroItemMorning = (petroItemMorning ?? 0) + unslottedPetroAmt;
            }

            string? petroItemTid = petroItems.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Tid))?.Tid;
            string? petroItemBatch = petroItems.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Batch))?.Batch;

            if (isShiftB)
            {
                PhonePeCardMorning = paymentSource?.PhonePeCardDay > 0 ? paymentSource.PhonePeCardDay : (paymentSource?.PhonePeCardMorning > 0 ? paymentSource.PhonePeCardMorning : (double?)null);
                PhonePeCardNight = null;
                PhonePeMorning = paymentSource?.PhonePeDay > 0 ? paymentSource.PhonePeDay : (paymentSource?.PhonePeMorning > 0 ? paymentSource.PhonePeMorning : (double?)null);
                PhonePeNight = null;
                CreditCardMorning = paymentSource?.CreditCardDay > 0 ? paymentSource.CreditCardDay : (paymentSource?.CreditCardMorning > 0 ? paymentSource.CreditCardMorning : (cardItemDay > 0 ? cardItemDay : (cardItemMorning > 0 ? cardItemMorning : (double?)null)));
                CreditCardNight = null;
                PetroCardMorning = paymentSource?.PetroCardDay > 0 ? paymentSource.PetroCardDay : (paymentSource?.PetroCardMorning > 0 ? paymentSource.PetroCardMorning : (petroItemDay > 0 ? petroItemDay : (petroItemMorning > 0 ? petroItemMorning : (paymentSource?.PetroCard > 0 ? paymentSource.PetroCard : (double?)null))));
                PetroCardNight = null;

                PhonePeTidMorning = paymentSource?.PhonePeTidDay ?? paymentSource?.PhonePeTidMorning ?? paymentSource?.PhonePeTid;
                PhonePeBatchMorning = paymentSource?.PhonePeBatchDay ?? paymentSource?.PhonePeBatchMorning ?? paymentSource?.PhonePeBatch;
                CreditCardTidMorning = paymentSource?.CreditCardTidDay ?? paymentSource?.CreditCardTidMorning ?? paymentSource?.CardTid ?? cardItemTid;
                CreditCardBatchMorning = paymentSource?.CreditCardBatchDay ?? paymentSource?.CreditCardBatchMorning ?? paymentSource?.CardBatch ?? cardItemBatch;
                PetroCardTidMorning = paymentSource?.PetroCardTidDay ?? paymentSource?.PetroCardTidMorning ?? paymentSource?.PetroCardTid ?? petroItemTid;
                PetroCardBatchMorning = paymentSource?.PetroCardBatchDay ?? paymentSource?.PetroCardBatchMorning ?? paymentSource?.PetroCardBatch ?? petroItemBatch;

                PhonePeTidNight = null;
                PhonePeBatchNight = null;
                CreditCardTidNight = null;
                CreditCardBatchNight = null;
                PetroCardTidNight = null;
                PetroCardBatchNight = null;
            }
            else if (isShiftC)
            {
                PhonePeCardMorning = paymentSource?.PhonePeCardNight > 0 ? paymentSource.PhonePeCardNight : (paymentSource?.PhonePeCardMorning > 0 ? paymentSource.PhonePeCardMorning : (double?)null);
                PhonePeCardNight = null;
                PhonePeMorning = paymentSource?.PhonePeNight > 0 ? paymentSource.PhonePeNight : (paymentSource?.PhonePeMorning > 0 ? paymentSource.PhonePeMorning : (double?)null);
                PhonePeNight = null;
                CreditCardMorning = paymentSource?.CreditCardNight > 0 ? paymentSource.CreditCardNight : (paymentSource?.CreditCardMorning > 0 ? paymentSource.CreditCardMorning : (cardItemNight > 0 ? cardItemNight : (cardItemMorning > 0 ? cardItemMorning : (double?)null)));
                CreditCardNight = null;
                PetroCardMorning = paymentSource?.PetroCardNight > 0 ? paymentSource.PetroCardNight : (paymentSource?.PetroCardMorning > 0 ? paymentSource.PetroCardMorning : (petroItemNight > 0 ? petroItemNight : (petroItemMorning > 0 ? petroItemMorning : (paymentSource?.PetroCard > 0 ? paymentSource.PetroCard : (double?)null))));
                PetroCardNight = null;

                PhonePeTidMorning = paymentSource?.PhonePeTidNight ?? paymentSource?.PhonePeTidMorning ?? paymentSource?.PhonePeTid;
                PhonePeBatchMorning = paymentSource?.PhonePeBatchNight ?? paymentSource?.PhonePeBatchMorning ?? paymentSource?.PhonePeBatch;
                CreditCardTidMorning = paymentSource?.CreditCardTidNight ?? paymentSource?.CreditCardTidMorning ?? paymentSource?.CardTid ?? cardItemTid;
                CreditCardBatchMorning = paymentSource?.CreditCardBatchNight ?? paymentSource?.CreditCardBatchMorning ?? paymentSource?.CardBatch ?? cardItemBatch;
                PetroCardTidMorning = paymentSource?.PetroCardTidNight ?? paymentSource?.PetroCardTidMorning ?? paymentSource?.PetroCardTid ?? petroItemTid;
                PetroCardBatchMorning = paymentSource?.PetroCardBatchNight ?? paymentSource?.PetroCardBatchMorning ?? paymentSource?.PetroCardBatch ?? petroItemBatch;

                PhonePeTidNight = null;
                PhonePeBatchNight = null;
                CreditCardTidNight = null;
                CreditCardBatchNight = null;
                PetroCardTidNight = null;
                PetroCardBatchNight = null;
            }
            else
            {
                PhonePeCardMorning = paymentSource?.PhonePeCardMorning > 0 ? paymentSource.PhonePeCardMorning : (double?)null;
                PhonePeCardNight = paymentSource?.PhonePeCardNight;
                PhonePeMorning = paymentSource?.PhonePeMorning > 0 ? paymentSource.PhonePeMorning : (double?)null;
                PhonePeNight = paymentSource?.PhonePeNight;
                CreditCardMorning = paymentSource?.CreditCardMorning > 0 ? paymentSource.CreditCardMorning : (cardItemMorning > 0 ? cardItemMorning : (double?)null);
                CreditCardNight = paymentSource?.CreditCardNight > 0 ? paymentSource.CreditCardNight : (cardItemNight > 0 ? cardItemNight : (double?)null);
                PetroCardMorning = paymentSource?.PetroCardMorning > 0 ? paymentSource.PetroCardMorning : (paymentSource?.PetroCardDay > 0 ? paymentSource.PetroCardDay : (petroItemMorning > 0 ? petroItemMorning : (petroItemDay > 0 ? petroItemDay : (paymentSource?.PetroCard > 0 ? paymentSource.PetroCard : (double?)null))));
                PetroCardNight = paymentSource?.PetroCardNight > 0 ? paymentSource.PetroCardNight : (petroItemNight > 0 ? petroItemNight : (double?)null);

                PhonePeTidMorning = paymentSource?.PhonePeTidMorning ?? paymentSource?.PhonePeTid;
                PhonePeBatchMorning = paymentSource?.PhonePeBatchMorning ?? paymentSource?.PhonePeBatch;
                PhonePeTidNight = paymentSource?.PhonePeTidNight;
                PhonePeBatchNight = paymentSource?.PhonePeBatchNight;

                CreditCardTidMorning = paymentSource?.CreditCardTidMorning ?? paymentSource?.CardTid ?? cardItems.FirstOrDefault(i => PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot) == "Morning" && !string.IsNullOrWhiteSpace(i.Tid))?.Tid ?? cardItemTid;
                CreditCardBatchMorning = paymentSource?.CreditCardBatchMorning ?? paymentSource?.CardBatch ?? cardItems.FirstOrDefault(i => PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot) == "Morning" && !string.IsNullOrWhiteSpace(i.Batch))?.Batch ?? cardItemBatch;
                CreditCardTidNight = paymentSource?.CreditCardTidNight ?? cardItems.FirstOrDefault(i => PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot) == "Night" && !string.IsNullOrWhiteSpace(i.Tid))?.Tid;
                CreditCardBatchNight = paymentSource?.CreditCardBatchNight ?? cardItems.FirstOrDefault(i => PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot) == "Night" && !string.IsNullOrWhiteSpace(i.Batch))?.Batch;

                PetroCardTidMorning = paymentSource?.PetroCardTidMorning ?? paymentSource?.PetroCardTid ?? petroItems.FirstOrDefault(i => PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot) == "Morning" && !string.IsNullOrWhiteSpace(i.Tid))?.Tid ?? petroItemTid;
                PetroCardBatchMorning = paymentSource?.PetroCardBatchMorning ?? paymentSource?.PetroCardBatch ?? petroItems.FirstOrDefault(i => PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot) == "Morning" && !string.IsNullOrWhiteSpace(i.Batch))?.Batch ?? petroItemBatch;
                PetroCardTidNight = paymentSource?.PetroCardTidNight ?? petroItems.FirstOrDefault(i => PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot) == "Night" && !string.IsNullOrWhiteSpace(i.Tid))?.Tid;
                PetroCardBatchNight = paymentSource?.PetroCardBatchNight ?? petroItems.FirstOrDefault(i => PaymentCollection.ExtractSlotFromMode(i.CollectionTypeCode, i.Slot) == "Night" && !string.IsNullOrWhiteSpace(i.Batch))?.Batch;
            }

            Others = paymentSource?.Others;
            CashDeposit = paymentSource?.CashDeposit;
            CollectionReduction = paymentSource?.CollectionReduction > 0.009 ? paymentSource.CollectionReduction : null;
            CollectionReductionRemark = paymentSource?.CollectionReductionRemark;

            PhonePeTid = isShiftC ? (paymentSource?.PhonePeTidNight ?? paymentSource?.PhonePeTid) : (isShiftB ? (paymentSource?.PhonePeTidDay ?? paymentSource?.PhonePeTid) : (paymentSource?.PhonePeTidMorning ?? paymentSource?.PhonePeTid ?? paymentSource?.PhonePeTidNight));
            PhonePeBatch = isShiftC ? (paymentSource?.PhonePeBatchNight ?? paymentSource?.PhonePeBatch) : (isShiftB ? (paymentSource?.PhonePeBatchDay ?? paymentSource?.PhonePeBatch) : (paymentSource?.PhonePeBatchMorning ?? paymentSource?.PhonePeBatch ?? paymentSource?.PhonePeBatchNight));
            CardTid = isShiftC ? (paymentSource?.CreditCardTidNight ?? paymentSource?.CardTid) : (isShiftB ? (paymentSource?.CreditCardTidDay ?? paymentSource?.CardTid) : (paymentSource?.CreditCardTidMorning ?? paymentSource?.CardTid ?? paymentSource?.CreditCardTidNight));
            CardBatch = isShiftC ? (paymentSource?.CreditCardBatchNight ?? paymentSource?.CardBatch) : (isShiftB ? (paymentSource?.CreditCardBatchDay ?? paymentSource?.CardBatch) : (paymentSource?.CreditCardBatchMorning ?? paymentSource?.CardBatch ?? paymentSource?.CreditCardBatchNight));
            PetroCardTid = isShiftC ? (paymentSource?.PetroCardTidNight ?? paymentSource?.PetroCardTid) : (isShiftB ? (paymentSource?.PetroCardTidDay ?? paymentSource?.PetroCardTid) : (paymentSource?.PetroCardTidMorning ?? paymentSource?.PetroCardTid ?? paymentSource?.PetroCardTidNight));
            PetroCardBatch = isShiftC ? (paymentSource?.PetroCardBatchNight ?? paymentSource?.PetroCardBatch) : (isShiftB ? (paymentSource?.PetroCardBatchDay ?? paymentSource?.PetroCardBatch) : (paymentSource?.PetroCardBatchMorning ?? paymentSource?.PetroCardBatch ?? paymentSource?.PetroCardBatchNight));

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
                                    Name = !string.IsNullOrWhiteSpace(name) ? name : (!string.IsNullOrWhiteSpace(slipNumber) ? $"Slip #{slipNumber}" : "Station Drawings"),
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
                    string canonicalFuel = testEntry.FuelType?.Trim().ToUpperInvariant() ?? "";
                    if (canonicalFuel.Contains("MS-I") || canonicalFuel == "MS" || canonicalFuel.Contains("PETROL"))
                    {
                        match = TestingRows.FirstOrDefault(t => t.FuelType.Contains("MS-I", StringComparison.OrdinalIgnoreCase) || t.FuelType.Contains("MS", StringComparison.OrdinalIgnoreCase));
                    }
                    else if (canonicalFuel.Contains("SPEED") || canonicalFuel.Contains("XP95") || canonicalFuel.Contains("POWER"))
                    {
                        match = TestingRows.FirstOrDefault(t => t.FuelType.Contains("SPEED", StringComparison.OrdinalIgnoreCase) || t.FuelType.Contains("XP95", StringComparison.OrdinalIgnoreCase) || t.FuelType.Contains("POWER", StringComparison.OrdinalIgnoreCase));
                    }
                    else if (canonicalFuel.Contains("HSD") && !canonicalFuel.Contains("HSD-II") && !canonicalFuel.Contains("II"))
                    {
                        match = TestingRows.FirstOrDefault(t => t.FuelType.Contains("HSD", StringComparison.OrdinalIgnoreCase) && !t.FuelType.Contains("HSD-II", StringComparison.OrdinalIgnoreCase));
                    }
                    else if (canonicalFuel.Contains("MS-II") || canonicalFuel.Contains("HSD-II") || canonicalFuel.Contains("II"))
                    {
                        match = TestingRows.FirstOrDefault(t => t.FuelType.Contains("MS-II", StringComparison.OrdinalIgnoreCase) || t.FuelType.Contains("HSD-II", StringComparison.OrdinalIgnoreCase));
                    }
                    else if (canonicalFuel.Contains("CNG"))
                    {
                        match = TestingRows.FirstOrDefault(t => t.FuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase));
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
            bool cash1HasDenoms = cash1Data != null && (cash1Data.Denom500 > 0 || cash1Data.Denom200 > 0 || cash1Data.Denom100 > 0);
            double cash1ScalarTotal = (cash1Data != null && cash1Data.TotalAmount > 0) ? cash1Data.TotalAmount : (CashDeposit ?? 0);
            Cash1 = new CashDenomRow
            {
                CashType = "Cash1",
                Denom500 = cash1HasDenoms ? cash1Data!.Denom500 : null,
                Denom200 = cash1HasDenoms ? cash1Data!.Denom200 : null,
                Denom100 = cash1HasDenoms ? cash1Data!.Denom100 : null,
                Denom50 = cash1HasDenoms && cash1Data!.Denom50 > 0 ? cash1Data.Denom50 : null,
                Denom20 = cash1HasDenoms && cash1Data!.Denom20 > 0 ? cash1Data.Denom20 : null,
                Denom10 = cash1HasDenoms && cash1Data!.Denom10 > 0 ? cash1Data.Denom10 : null,
                Coins = cash1HasDenoms && cash1Data!.Coins > 0 ? cash1Data.Coins : null,
                TotalAmount = cash1HasDenoms && cash1Data!.TotalAmount <= 0
                    ? cash1Data.Denom500 * 500.0 + cash1Data.Denom200 * 200.0 + cash1Data.Denom100 * 100.0
                    : cash1ScalarTotal,
                OnTotalChanged = () =>
                {
                    OnPropertyChanged(nameof(ShowCash1DenominationFields));
                    OnPropertyChanged(nameof(ShowCash1ScalarField));
                    RecalculateAll();
                }
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

            var cash3Data = entry.CashDenominations.FirstOrDefault(c => c.CashType == "Cash3")
                ?? allSlaveEntries.Select(s => s.CashDenominations?.FirstOrDefault(c => c.CashType == "Cash3")).FirstOrDefault(c => c != null);
            Cash3 = new CashDenomRow
            {
                CashType = "Cash3",
                Denom500 = cash3Data?.Denom500,
                Denom200 = cash3Data?.Denom200,
                Denom100 = cash3Data?.Denom100,
                Denom50 = cash3Data?.Denom50,
                Denom20 = cash3Data?.Denom20,
                Denom10 = cash3Data?.Denom10,
                Coins = cash3Data?.Coins,
                TotalAmount = cash3Data?.TotalAmount ?? 0,
                OnTotalChanged = RecalculateAll
            };

            // Populate Debtor Repayments
            DebtorRepayments.Clear();
            try
            {
                using var db = App.Services.GetRequiredService<FuelPro.Data.FuelProDbContext>();
                var audit = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                    db.DsmApprovalAudits
                        .Where(a => a.ApprovedDataJson.Contains($"\"DsmEntryId\":{entry.DsmEntryId}") 
                                 || a.ApprovedDataJson.Contains($"\"DsmEntryId\": {entry.DsmEntryId}")));

                var shiftDate = entry.Shift?.ShiftDate.Date ?? SelectedDate.Date;
                var shiftNum = entry.Shift?.ShiftType ?? SelectedShift;
                var dsmTag = $"DSM: {entry.DsmName}";
                var existingDbReps = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                    db.CreditorRepayments
                        .Where(r => (entry.DsmEntryId > 0 && r.ChequeNo != null && r.ChequeNo.Contains($"[Entry:{entry.DsmEntryId}"))
                                 || (r.RepaymentDate.Date == shiftDate && r.ShiftNumber == shiftNum && (r.ChequeNo != null && r.ChequeNo.Contains(dsmTag)))));

                if (audit != null && !string.IsNullOrEmpty(audit.OriginalDataJson))
                {
                    var parsed = JsonConvert.DeserializeObject<dynamic>(audit.OriginalDataJson);
                    var repRawList = parsed?.debtorRepayments ?? parsed?.DebtorRepayments ?? parsed?.settlements;
                    if (repRawList != null)
                    {
                        var matchedIds = new HashSet<int>();
                        int auditRepIndex = 0;
                        foreach (var rep in repRawList)
                        {
                            string debtorName = (rep.debtorName ?? rep.DebtorName ?? rep.customerName ?? rep.customer ?? string.Empty).ToString();
                            double amount = System.Convert.ToDouble((object?)(rep.amount ?? rep.Amount ?? 0.0));
                            string paymentMode = (rep.paymentMode ?? rep.PaymentMode ?? rep.mode ?? "Cash").ToString();
                            string notes = (rep.notes ?? rep.Notes ?? string.Empty).ToString();

                            if (amount > 0 && !string.IsNullOrWhiteSpace(debtorName))
                            {
                                var dbMatch = existingDbReps.FirstOrDefault(r => 
                                    !matchedIds.Contains(r.CreditorRepaymentId)
                                    && RepaymentIdentityHelper.MatchesEntry(r.ChequeNo, entry.DsmEntryId, auditRepIndex))
                                    ?? existingDbReps.FirstOrDefault(r => 
                                        !matchedIds.Contains(r.CreditorRepaymentId)
                                        && string.Equals(r.CreditorName, debtorName.Trim(), StringComparison.OrdinalIgnoreCase)
                                        && Math.Abs(r.Amount - amount) < 0.01);

                                int repId = 0;
                                if (dbMatch != null)
                                {
                                    repId = dbMatch.CreditorRepaymentId;
                                    matchedIds.Add(repId);
                                }

                                DebtorRepayments.Add(new DsmDebtorRepaymentRow
                                {
                                    CreditorRepaymentId = repId,
                                    DebtorName = debtorName,
                                    Amount = amount,
                                    PaymentMode = paymentMode,
                                    Notes = !string.IsNullOrWhiteSpace(notes) ? notes : RepaymentIdentityHelper.ExtractUserNotes(dbMatch?.ChequeNo, dsmTag),
                                    OnRowChanged = () =>
                                    {
                                        OnPropertyChanged(nameof(HasDebtorRepayments));
                                        UpdateDirtyState();
                                    }
                                });
                                auditRepIndex++;
                            }
                        }
                    }
                }

                // If none from audit, check CreditorRepayments for this shift/DSM
                if (DebtorRepayments.Count == 0 && existingDbReps.Count > 0)
                {
                    foreach (var r in existingDbReps)
                    {
                        DebtorRepayments.Add(new DsmDebtorRepaymentRow
                        {
                            CreditorRepaymentId = r.CreditorRepaymentId,
                            DebtorName = r.CreditorName,
                            Amount = r.Amount,
                            PaymentMode = r.PaymentMode ?? "Cash",
                            Notes = RepaymentIdentityHelper.ExtractUserNotes(r.ChequeNo, dsmTag),
                            OnRowChanged = () =>
                            {
                                OnPropertyChanged(nameof(HasDebtorRepayments));
                                UpdateDirtyState();
                            }
                        });
                    }
                }
            }
            catch (System.Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to hydrate DebtorRepayments for DsmEntryId {Id}", entry.DsmEntryId);
            }
            OnPropertyChanged(nameof(HasDebtorRepayments));
            OnPropertyChanged(nameof(IsCash3Enabled));
            OnPropertyChanged(nameof(IsCash1DenominationsEnabled));
            OnPropertyChanged(nameof(ShowCash1DenominationFields));
            OnPropertyChanged(nameof(ShowCash1ScalarField));

            RecalculateAll();
            _savedFormSnapshot = CaptureFormSnapshot();
            IsSaved = true;
            IsDirty = false;
            IsSaveOutcomeUncertain = false;
            OnPropertyChanged(nameof(CanSaveCurrentEntry));
            SaveEntryCommand.NotifyCanExecuteChanged();
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
        IsSaveOutcomeUncertain = false;
        var services = _serviceProvider ?? App.Services;
        var shiftRepo = services?.GetService<Core.Repositories.IShiftRepository>();
        if (shiftRepo == null || _dsmService == null)
        {
            ShiftEntries.Clear();
            ShiftFuelTypeSummaries.Clear();
            HasShiftFuelTypeSummaries = false;
            return;
        }
        var shiftResult = await shiftRepo.GetShiftAsync(SelectedDate, SelectedShift);
        if (!shiftResult.Success)
        {
            ShiftEntries.Clear();
            ShiftFuelTypeSummaries.Clear();
            HasShiftFuelTypeSummaries = false;
            return;
        }

        var summaries = await _dsmService.GetShiftEntrySummariesAsync(shiftResult.Data!.ShiftId);
        ShiftEntries.Clear();
        if (summaries.Success && summaries.Data != null)
            foreach (var s in summaries.Data!) ShiftEntries.Add(s);

        var fuelResult = await _dsmService.GetShiftFuelTypeSummariesAsync(shiftResult.Data!.ShiftId);
        ShiftFuelTypeSummaries.Clear();
        if (fuelResult.Success && fuelResult.Data != null)
        {
            foreach (var f in fuelResult.Data) ShiftFuelTypeSummaries.Add(f);
        }
        HasShiftFuelTypeSummaries = ShiftFuelTypeSummaries.Count > 0;

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

    private void ApplyDsmProfileRenamed(string oldName, string newName)
    {
        if (!string.IsNullOrWhiteSpace(oldName) && string.Equals(DsmName, oldName, StringComparison.OrdinalIgnoreCase))
        {
            DsmName = newName ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(oldName) && QrPayments != null)
        {
            foreach (var qr in QrPayments)
            {
                if (string.Equals(qr.TargetDsmName, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    qr.TargetDsmName = newName ?? string.Empty;
                }
            }
        }
    }

    private void ApplyDsmProfileDeleted(string deletedName)
    {
        if (!string.IsNullOrWhiteSpace(deletedName) && string.Equals(DsmName, deletedName, StringComparison.OrdinalIgnoreCase))
        {
            DsmName = string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(deletedName) && QrPayments != null)
        {
            foreach (var qr in QrPayments)
            {
                if (string.Equals(qr.TargetDsmName, deletedName, StringComparison.OrdinalIgnoreCase))
                {
                    qr.TargetDsmName = string.Empty;
                }
            }
        }
    }

    public async Task LoadSuggestionsAsync()
    {
        var currentDsm = DsmName;
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
            var users = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(dbContext.DsmUsers));
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

        if (!string.IsNullOrEmpty(currentDsm))
        {
            DsmName = currentDsm;
        }

        var creditorsResult = await _creditorRepo.GetAllActiveWithVehiclesAsync();
        Creditors.Clear();
        if (creditorsResult.Success)
        {
            foreach (var c in creditorsResult.Data!)
                Creditors.Add(c);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_featureToggleService != null && _featureChangedHandler != null)
            _featureToggleService.FeatureConfigurationChanged -= _featureChangedHandler;

        if (_stationConfigService != null && _stationConfigChangedHandler != null)
            _stationConfigService.StationConfigurationChanged -= _stationConfigChangedHandler;

        if (_collectionTypeService != null && _collectionTypesChangedHandler != null)
            _collectionTypeService.CollectionTypesChanged -= _collectionTypesChangedHandler;

        if (_profileChangedHandler != null)
            FuelPro.Core.Services.DsmEntryService.DsmProfileChanged -= _profileChangedHandler;

        if (_profileRenamedHandler != null)
            FuelPro.Core.Services.DsmEntryService.DsmProfileRenamed -= _profileRenamedHandler;

        if (_profileDeletedHandler != null)
            FuelPro.Core.Services.DsmEntryService.DsmProfileDeleted -= _profileDeletedHandler;

        if (_debtorChangedHandler != null)
            FuelPro.Core.Services.DsmEntryService.DebtorChanged -= _debtorChangedHandler;

        if (_fuelRatesChangedHandler != null)
            FuelPro.Core.Services.DsmEntryService.FuelRatesChanged -= _fuelRatesChangedHandler;

        if (_dsmEntryChangedHandler != null)
            FuelPro.Core.Services.DsmEntryService.DsmEntryChanged -= _dsmEntryChangedHandler;

        if (_syncEngine != null && _operationalDataPulledHandler != null)
            _syncEngine.OperationalDataPulled -= _operationalDataPulledHandler;

        if (_autoSaveTimer != null)
        {
            _autoSaveTimer.Stop();
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

    public static void DeductDebtorRepaymentsFromCollections(
        IEnumerable<DsmDebtorRepaymentRow>? repayments,
        ref double cashDeposit,
        ref double cash2,
        ref double cash3,
        ref double phonePe,
        ref double creditCard,
        ref double petroCard,
        IDictionary<string, double>? dynamicAmounts = null)
    {
        if (repayments == null) return;

        foreach (var rep in repayments)
        {
            if (!rep.Amount.HasValue || rep.Amount.Value <= 0) continue;
            double rem = rep.Amount.Value;
            string rawMode = (rep.PaymentMode ?? "").Trim();
            string normMode = PaymentCollection.NormalizeCollectionTypeCode(rawMode);

            // 1. Check if matches custom dynamic collection code or name
            if (dynamicAmounts != null && dynamicAmounts.Count > 0)
            {
                var dynKey = dynamicAmounts.Keys.FirstOrDefault(k =>
                    string.Equals(k, normMode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(k, rawMode, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(k.Replace("_", " "), rawMode, StringComparison.OrdinalIgnoreCase));

                if (dynKey != null)
                {
                    double cur = dynamicAmounts[dynKey];
                    double deduct = Math.Min(cur, rem);
                    dynamicAmounts[dynKey] = cur - deduct;
                    rem -= deduct;
                    if (rem <= 0.001) continue;
                }
            }

            // 2. Standard modes
            if (normMode == "PHONEPE" || string.Equals(rawMode, "PhonePe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawMode, "UPI", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawMode, "Google Pay", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rawMode, "Paytm", StringComparison.OrdinalIgnoreCase))
            {
                double deduct = Math.Min(phonePe, rem);
                phonePe -= deduct;
                rem -= deduct;
            }
            else if (normMode == "CREDIT_CARD" || string.Equals(rawMode, "Credit Card", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rawMode, "Card", StringComparison.OrdinalIgnoreCase))
            {
                double deduct = Math.Min(creditCard, rem);
                creditCard -= deduct;
                rem -= deduct;
            }
            else if (normMode == "PETROCARD" || string.Equals(rawMode, "PetroCard", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(rawMode, "Petro Card", StringComparison.OrdinalIgnoreCase))
            {
                double deduct = Math.Min(petroCard, rem);
                petroCard -= deduct;
                rem -= deduct;
            }
            else if (string.Equals(rawMode, "Cash", StringComparison.OrdinalIgnoreCase) ||
                     string.IsNullOrWhiteSpace(rawMode) ||
                     rawMode.StartsWith("Cash", StringComparison.OrdinalIgnoreCase))
            {
                if (cash3 > 0)
                {
                    double deduct = Math.Min(cash3, rem);
                    cash3 -= deduct;
                    rem -= deduct;
                }
                if (rem > 0.001 && cash2 > 0)
                {
                    double deduct = Math.Min(cash2, rem);
                    cash2 -= deduct;
                    rem -= deduct;
                }
                if (rem > 0.001 && cashDeposit > 0)
                {
                    double deduct = Math.Min(cashDeposit, rem);
                    cashDeposit -= deduct;
                    rem -= deduct;
                }
            }
        }
    }

    private DsmEntryDto BuildCalculationDto()
    {
        var ccTxs = TidTransactions.Where(t =>
            PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "CREDIT_CARD").ToList();
        var phTxs = TidTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PHONEPE").ToList();
        var pcTxs = TidTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PETROCARD").ToList();

        decimal phonePeFromTid = (decimal)phTxs.Sum(t => t.Amount);
        decimal petroCardFromTid = (decimal)pcTxs.Sum(t => t.Amount);
        decimal ccFromTid = (decimal)ccTxs.Sum(t => t.Amount);

        decimal ccOtpSum = (decimal)OtpTransactions.Where(t =>
            PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "CREDIT_CARD").Sum(t => t.Amount);
        decimal pcOtpSum = (decimal)OtpTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PETROCARD").Sum(t => t.Amount);
        decimal phOtpSum = (decimal)OtpTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PHONEPE").Sum(t => t.Amount);

        decimal qrSum = (decimal)QrPayments.Sum(q => q.Amount ?? 0);
        decimal phonePeScalar = (decimal)((PhonePeMorning ?? 0) + (IsPhonePeSplitEnabled ? (PhonePeNight ?? 0) : 0) + (PhonePeCardMorning ?? 0) + (IsPhonePeSplitEnabled ? (PhonePeCardNight ?? 0) : 0));
        decimal creditCardScalar = (decimal)((CreditCardMorning ?? 0) + (IsCreditCardSplitEnabled ? (CreditCardNight ?? 0) : 0));
        decimal petroCardScalar = (decimal)((PetroCardMorning ?? 0) + (IsPetroCardSplitEnabled ? (PetroCardNight ?? 0) : 0));

        decimal phonePeItemized = phonePeFromTid + phOtpSum;
        decimal ccItemized = ccFromTid + ccOtpSum;
        decimal pcItemized = petroCardFromTid + pcOtpSum;

        decimal effectivePhonePe = ((IsPhonePeTidOrOtp || phonePeItemized > 0) ? Math.Max(phonePeItemized, phonePeScalar) : phonePeScalar) + qrSum;
        decimal effectiveCreditCard = (IsCreditCardTidOrOtp || ccItemized > 0) ? (ccItemized > 0 ? ccItemized : creditCardScalar) : creditCardScalar;
        decimal effectivePetroCard = (IsPetroCardTidOrOtp || pcItemized > 0) ? Math.Max(pcItemized, petroCardScalar) : petroCardScalar;

        var allDynamicTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in TidTransactions.Where(t => !PaymentCollection.IsStandardCollectionCode(t.CollectionTypeCode)))
            allDynamicTypes.Add(PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode));
        foreach (var t in OtpTransactions.Where(t => !PaymentCollection.IsStandardCollectionCode(t.CollectionTypeCode)))
            allDynamicTypes.Add(PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode));
        foreach (var d in DynamicCollections.Where(d => !PaymentCollection.IsStandardCollectionCode(d.Code)))
            allDynamicTypes.Add(PaymentCollection.NormalizeCollectionTypeCode(d.Code));
        foreach (var m in ManualCollections.Where(m => !PaymentCollection.IsStandardCollectionCode(m.Code)))
            allDynamicTypes.Add(PaymentCollection.NormalizeCollectionTypeCode(m.Code));

        decimal dynamicPayments = 0;
        foreach (var normCode in allDynamicTypes)
        {
            decimal tidSum = (decimal)TidTransactions.Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode), normCode, StringComparison.OrdinalIgnoreCase)).Sum(t => t.Amount);
            decimal otpSum = (decimal)OtpTransactions.Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode), normCode, StringComparison.OrdinalIgnoreCase)).Sum(t => t.Amount);
            decimal manualSum = (decimal)ManualCollections.Where(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), normCode, StringComparison.OrdinalIgnoreCase)).Sum(m => m.Amount ?? 0);
            var dynRow = DynamicCollections.FirstOrDefault(d => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(d.Code), normCode, StringComparison.OrdinalIgnoreCase));
            decimal dynRowAmt = (decimal)(dynRow?.Amount ?? 0);

            decimal typeTotal = tidSum + otpSum + manualSum;
            if (typeTotal == 0 && dynRowAmt > 0)
            {
                typeTotal = dynRowAmt;
            }
            dynamicPayments += typeTotal;
        }

        return new DsmEntryDto
        {
            NozzleReadings = NozzleReadings.Select(x => new NozzleReadingDto { Amount = (decimal)x.Amount }).ToList(),
            PaymentCollection = new PaymentCollectionDto
            {
                // Others is NOT included in TotalInDirect — it is informational only
                PhonePe = effectivePhonePe,
                CreditCard = effectiveCreditCard,
                CashDeposit = Cash1.TotalAmount > 0 ? (decimal)Cash1.TotalAmount : (decimal)(CashDeposit ?? 0),
                PhysicalCash = (decimal)Cash2.TotalAmount,
                Cash3 = (decimal)Cash3.TotalAmount,
                PetroCard = effectivePetroCard,
                Others = (decimal)(Others ?? 0),
                DynamicPayments = dynamicPayments,
                CollectionReduction = (decimal)(CollectionReduction ?? 0),
                CollectionReductionRemark = CollectionReductionRemark
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

        if (!ValidateReduction(out var redErr))
        {
            errors.Add(redErr!);
        }

        if (IsOtpMachineTrackingEnabled && OtpTransactions.Count > 0)
        {
            for (int i = 0; i < OtpTransactions.Count; i++)
            {
                var tx = OtpTransactions[i];
                if (tx.Amount > 0 && string.IsNullOrWhiteSpace(tx.CollectionTypeCode))
                {
                    errors.Add($"Slip Number transaction #{i + 1} has an amount of ₹{tx.Amount:N2} but no collection type is selected. Please select a collection type or remove the row.");
                }
                if (tx.Amount < 0)
                {
                    errors.Add($"Slip Number transaction #{i + 1} amount cannot be negative.");
                }
            }
        }

        if (TidTransactions.Count > 0)
        {
            for (int i = 0; i < TidTransactions.Count; i++)
            {
                var tx = TidTransactions[i];
                if (tx.Amount > 0 && string.IsNullOrWhiteSpace(tx.CollectionTypeCode))
                {
                    errors.Add($"Itemized TID transaction #{i + 1} has an amount of ₹{tx.Amount:N2} but no collection type is selected. Please select a collection type or remove the row.");
                }
                if (tx.Amount < 0)
                {
                    errors.Add($"Itemized TID transaction #{i + 1} amount cannot be negative.");
                }
            }
        }

        return errors;
    }

    public bool ValidateReduction(out string? error)
    {
        error = null;
        if (CollectionReduction.HasValue)
        {
            if (CollectionReduction.Value < 0)
            {
                error = "Collection reduction cannot be negative.";
                return false;
            }
            if (CollectionReduction.Value > 0 && string.IsNullOrWhiteSpace(CollectionReductionRemark))
            {
                error = "A meaningful remark is required when a collection reduction is entered.";
                return false;
            }
        }
        return true;
    }

    public List<FuelTypeReadingSummary> BuildDsmScopedCumulativeSales()
    {
        var distinctNozzleDict = new Dictionary<(int PumpId, int NozzleNumber), (string FuelType, double SaleLitres, double Rate, double Amount)>();

        // 1. Primary pump nozzles from active viewmodel
        int primaryPumpId = SelectedPump?.PumpId ?? 0;
        if (primaryPumpId > 0)
        {
            foreach (var n in NozzleReadings)
            {
                if (string.IsNullOrWhiteSpace(n.FuelType)) continue;
                distinctNozzleDict[(primaryPumpId, n.NozzleNumber)] = (
                    FuelType: n.FuelType.Trim(),
                    SaleLitres: n.SaleLitres,
                    Rate: n.Rate ?? 0.0,
                    Amount: n.Amount
                );
            }
        }

        // 2. Connected pump nozzles (if configured and active)
        var effectiveConnectedPumps = ActiveConnectedPumpIds != null
            ? ActiveConnectedPumpIds.Where(p => SelectedPump == null || p != SelectedPump.PumpId).Distinct().ToList()
            : new List<int>();
        if (effectiveConnectedPumps.Count == 0 && SelectedConnectedPump != null && SelectedPump != null && SelectedConnectedPump.PumpId != SelectedPump.PumpId)
        {
            effectiveConnectedPumps.Add(SelectedConnectedPump.PumpId);
        }

        if (effectiveConnectedPumps.Count > 0)
        {
            try
            {
                var shiftRepo = App.Services.GetRequiredService<Core.Repositories.IShiftRepository>();
                var shiftResult = shiftRepo.GetShiftAsync(SelectedDate, SelectedShift).GetAwaiter().GetResult();
                if (shiftResult.Success && shiftResult.Data != null)
                {
                    var dsmRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
                    var entriesResult = dsmRepo.GetEntriesForShiftAsync(shiftResult.Data.ShiftId).GetAwaiter().GetResult();
                    if (entriesResult.Success && entriesResult.Data != null)
                    {
                        foreach (var connPumpId in effectiveConnectedPumps)
                        {
                            DsmEntry? connEntry = null;
                            if (EditingEntryId.HasValue)
                            {
                                connEntry = entriesResult.Data.FirstOrDefault(e =>
                                    e.PumpId == connPumpId
                                    && (e.ReconciledToPumpId == EditingEntryId.Value || e.ReconciledToPumpId == primaryPumpId));
                            }
                            if (connEntry == null)
                            {
                                connEntry = entriesResult.Data
                                    .Where(e => e.PumpId == connPumpId
                                        && (e.ReconciledToPumpId == primaryPumpId
                                            || (string.Equals(e.DsmName, DsmName, StringComparison.OrdinalIgnoreCase) && (!e.ReconciledToPumpId.HasValue || e.ReconciledToPumpId == primaryPumpId))))
                                    .OrderByDescending(e => e.DsmEntryId)
                                    .FirstOrDefault();
                            }

                            if (connEntry != null)
                            {
                                var readings = connEntry.NozzleReadings;
                                if (readings == null || readings.Count == 0)
                                {
                                    var fullEntry = dsmRepo.GetFullEntryAsync(connEntry.DsmEntryId).GetAwaiter().GetResult();
                                    if (fullEntry.Success && fullEntry.Data != null)
                                    {
                                        readings = fullEntry.Data.NozzleReadings;
                                    }
                                }

                                if (readings != null)
                                {
                                    foreach (var rn in readings)
                                    {
                                        if (string.IsNullOrWhiteSpace(rn.FuelType)) continue;
                                        var key = (connPumpId, rn.NozzleNumber);
                                        if (!distinctNozzleDict.ContainsKey(key))
                                        {
                                            distinctNozzleDict[key] = (
                                                FuelType: rn.FuelType.Trim(),
                                                SaleLitres: (double)rn.SaleLitres,
                                                Rate: (double)rn.Rate,
                                                Amount: (double)rn.Amount
                                            );
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Could not fetch connected pump readings for DSM-scoped cumulative sales in PrintDsm");
            }
        }

        // Fallback: If no nozzles resolved, use FuelTypeSummaries
        if (distinctNozzleDict.Count == 0)
        {
            return FuelTypeSummaries.Select(f => new FuelTypeReadingSummary
            {
                FuelType = f.FuelType,
                NozzleCount = f.NozzleCount,
                TotalLitres = f.TotalLitres,
                Rate = f.Rate,
                TotalAmount = Math.Round(f.TotalLitres * f.Rate, 2)
            }).ToList();
        }

        return distinctNozzleDict.Values
            .GroupBy(n => n.FuelType.Trim().ToUpperInvariant())
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var litres = g.Sum(n => n.SaleLitres);
                var rate = g.FirstOrDefault(n => n.Rate > 0).Rate;
                return new FuelTypeReadingSummary
                {
                    FuelType = g.First().FuelType,
                    NozzleCount = g.Count(),
                    TotalLitres = litres,
                    Rate = rate,
                    TotalAmount = Math.Round(litres * rate, 2)
                };
            })
            .ToList();
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

        RecalculateAll();

        var settings = App.Services.GetRequiredService<ISettingsRepository>();
        var settingsResult = settings.GetSettingsAsync().GetAwaiter().GetResult();
        var stationName = settingsResult.Success && !string.IsNullOrWhiteSpace(settingsResult.Data?.PumpStationName) ? settingsResult.Data.PumpStationName : "Mitali Service Station";

        UpdateFuelTypeSummaries();
        var cumulativeSales = FuelTypeSummaries.Select(f => new FuelTypeReadingSummary
        {
            FuelType = f.FuelType,
            NozzleCount = f.NozzleCount,
            TotalLitres = f.TotalLitres,
            Rate = f.Rate,
            TotalAmount = f.TotalAmount
        }).ToList();

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
            CashDeposit        = Cash1.TotalAmount,
            Others             = Others             ?? 0,
            BankDeposit        = Cash1.TotalAmount,
            Cash3              = Cash3.TotalAmount,
            TotalDigital       = (PhonePeCardMorning ?? 0) + (PhonePeCardNight ?? 0)
                               + (PhonePeMorning ?? 0)     + (PhonePeNight ?? 0)
                               + (CreditCardMorning ?? 0)  + (CreditCardNight ?? 0)
                               + (PetroCardMorning ?? 0)   + (PetroCardNight ?? 0)
                               + DynamicCollections.Where(d => (d.Amount ?? 0) > 0).Sum(d => d.Amount ?? 0),
            CardTid            = CardTid ?? "",
            CardBatch          = CardBatch ?? "",
            PhonePeTid         = PhonePeTid ?? "",
            PhonePeBatch       = PhonePeBatch ?? "",
            PetroCardTid       = PetroCardTid ?? "",
            PetroCardBatch     = PetroCardBatch ?? "",
            Items              = OtpTransactions
                .Where(t => t.Amount > 0 || !string.IsNullOrWhiteSpace(t.Otp))
                .Select(t => new DsmPaymentItemPrintRow
                {
                    CollectionTypeCode = t.CollectionTypeCode,
                    DisplayName = _cachedAllCollectionTypes?.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode), StringComparison.OrdinalIgnoreCase))?.DisplayName ?? t.CollectionTypeCode,
                    Amount = t.Amount,
                    Tid = t.Tid ?? "",
                    Batch = t.Batch ?? "",
                    Slot = t.Slot,
                    Otp = t.Otp ?? ""
                })
                .Concat(TidTransactions
                    .Where(t => t.Amount > 0)
                    .Select(t => new DsmPaymentItemPrintRow
                    {
                        CollectionTypeCode = t.CollectionTypeCode,
                        DisplayName = _cachedAllCollectionTypes?.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode), StringComparison.OrdinalIgnoreCase))?.DisplayName ?? t.CollectionTypeCode,
                        Amount = t.Amount,
                        Tid = t.Tid ?? "",
                        Batch = t.Batch ?? "",
                        Slot = t.Slot,
                        Otp = ""
                    }))
                .Concat(ManualCollections
                    .Where(m => (m.Amount ?? 0) > 0)
                    .Select(m => new DsmPaymentItemPrintRow
                    {
                        CollectionTypeCode = m.Code,
                        DisplayName = m.DisplayName,
                        Amount = m.Amount ?? 0,
                        Tid = "",
                        Batch = "",
                        Slot = "General",
                        Otp = ""
                    }))
                .ToList(),
            CollectionTypeTotals = CollectionTypeTotals.ToList()
        };

        var cash1Denom = new DsmCashDenomPrintBlock
        {
            Qty500 = Cash1.Denom500 ?? 0,
            Amt500 = (Cash1.Denom500 ?? 0) * 500.0,
            Qty200 = Cash1.Denom200 ?? 0,
            Amt200 = (Cash1.Denom200 ?? 0) * 200.0,
            Qty100 = Cash1.Denom100 ?? 0,
            Amt100 = (Cash1.Denom100 ?? 0) * 100.0,
            Total  = Cash1.TotalAmount
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

        var cash3Denom = new DsmCashDenomPrintBlock
        {
            Qty500 = Cash3.Denom500 ?? 0,
            Amt500 = (Cash3.Denom500 ?? 0) * 500.0,
            Qty200 = Cash3.Denom200 ?? 0,
            Amt200 = (Cash3.Denom200 ?? 0) * 200.0,
            Qty100 = Cash3.Denom100 ?? 0,
            Amt100 = (Cash3.Denom100 ?? 0) * 100.0,
            Qty50  = Cash3.Denom50  ?? 0,
            Amt50  = (Cash3.Denom50  ?? 0) * 50.0,
            Qty20  = Cash3.Denom20  ?? 0,
            Amt20  = (Cash3.Denom20  ?? 0) * 20.0,
            Qty10  = Cash3.Denom10  ?? 0,
            Amt10  = (Cash3.Denom10  ?? 0) * 10.0,
            Coins  = Cash3.Coins    ?? 0,
            Total  = Cash3.TotalAmount
        };

        var creditors = Debits.Where(d => !string.IsNullOrWhiteSpace(d.DebtorName))
            .Select(d => new DsmCreditorPrintRow { DebtorName = d.DebtorName, ChequeNo = d.ChequeNo ?? "", Amount = d.Amount ?? 0 }).ToList();

        var expenses = Expenses.Where(e => !string.IsNullOrWhiteSpace(e.Description))
            .Select(e => new DsmExpensePrintRow { Description = e.Description, Amount = e.Amount ?? 0 }).ToList();

        var testing = TestingRows.Where(t => t.Amount > 0)
            .Select(t => new DsmTestingPrintRow { FuelType = t.FuelType, Litres = t.Litres ?? 0, Rate = t.Rate ?? 0, Amount = t.Amount }).ToList();

        double c1 = Cash1.TotalAmount;
        double c2 = Cash2.TotalAmount;
        double c3 = Cash3.TotalAmount;

        double phonePeFromTid = TidTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PHONEPE").Sum(t => t.Amount);
        double phOtpSum = OtpTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PHONEPE").Sum(t => t.Amount);
        double qrSum = QrPayments.Sum(q => q.Amount ?? 0);
        double phonePeScalar = (PhonePeMorning ?? 0) + (IsPhonePeSplitEnabled ? (PhonePeNight ?? 0) : 0) + (PhonePeCardMorning ?? 0) + (IsPhonePeSplitEnabled ? (PhonePeCardNight ?? 0) : 0);
        double phonePeItemized = phonePeFromTid + phOtpSum;
        double phonePeTotal = Math.Max(phonePeScalar, phonePeItemized) + qrSum;

        double ccFromTid = TidTransactions.Where(t =>
            PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "CREDIT_CARD").Sum(t => t.Amount);
        double ccOtpSum = OtpTransactions.Where(t =>
            PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "CREDIT_CARD").Sum(t => t.Amount);
        double creditCardScalar = (CreditCardMorning ?? 0) + (IsCreditCardSplitEnabled ? (CreditCardNight ?? 0) : 0);
        double ccItemized = ccFromTid + ccOtpSum;
        double cardTotal = ccItemized > 0 ? ccItemized : creditCardScalar;

        double pcFromTid = TidTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PETROCARD").Sum(t => t.Amount);
        double pcOtpSum = OtpTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PETROCARD").Sum(t => t.Amount);
        double petroCardScalar = (PetroCardMorning ?? 0) + (IsPetroCardSplitEnabled ? (PetroCardNight ?? 0) : 0);
        double pcItemized = pcFromTid + pcOtpSum;
        double petroTotal = Math.Max(petroCardScalar, pcItemized);

        var allDynamicTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in TidTransactions.Where(t => !PaymentCollection.IsStandardCollectionCode(t.CollectionTypeCode)))
            allDynamicTypes.Add(PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode));
        foreach (var t in OtpTransactions.Where(t => !PaymentCollection.IsStandardCollectionCode(t.CollectionTypeCode)))
            allDynamicTypes.Add(PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode));
        foreach (var d in DynamicCollections.Where(d => !PaymentCollection.IsStandardCollectionCode(d.Code)))
            allDynamicTypes.Add(PaymentCollection.NormalizeCollectionTypeCode(d.Code));
        foreach (var m in ManualCollections.Where(m => !PaymentCollection.IsStandardCollectionCode(m.Code)))
            allDynamicTypes.Add(PaymentCollection.NormalizeCollectionTypeCode(m.Code));

        var dynamicAmounts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var dynamicDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var normCode in allDynamicTypes)
        {
            var matchingDyn = DynamicCollections.FirstOrDefault(d => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(d.Code), normCode, StringComparison.OrdinalIgnoreCase));
            var matchingManual = ManualCollections.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), normCode, StringComparison.OrdinalIgnoreCase));
            string displayName = matchingDyn?.DisplayName ?? matchingManual?.DisplayName ?? normCode.Replace("_", " ");
            double tidSum = TidTransactions.Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode), normCode, StringComparison.OrdinalIgnoreCase)).Sum(t => t.Amount);
            double otpSum = OtpTransactions.Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode), normCode, StringComparison.OrdinalIgnoreCase)).Sum(t => t.Amount);
            double manualSum = ManualCollections.Where(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), normCode, StringComparison.OrdinalIgnoreCase)).Sum(m => m.Amount ?? 0);
            double total = tidSum + otpSum + manualSum;
            if (total == 0 && matchingDyn != null && (matchingDyn.Amount ?? 0) > 0)
            {
                total = matchingDyn.Amount ?? 0;
            }
            dynamicAmounts[normCode] = total;
            dynamicDisplayNames[normCode] = displayName;
        }

        var breakdownList = new List<CollectionCategoryDto>();
        if (c1 > 0)
        {
            breakdownList.Add(new CollectionCategoryDto { Category = "Cash 1", Amount = c1, BaseAmount = c1 });
        }
        if (c2 > 0)
        {
            breakdownList.Add(new CollectionCategoryDto { Category = "Cash 2", Amount = c2, BaseAmount = c2 });
        }
        if (c3 > 0)
        {
            breakdownList.Add(new CollectionCategoryDto { Category = "Cash 3", Amount = c3, BaseAmount = c3 });
        }
        if (phonePeTotal > 0)
        {
            breakdownList.Add(new CollectionCategoryDto { Category = "PhonePe", Amount = phonePeTotal, BaseAmount = phonePeTotal });
        }
        if (cardTotal > 0)
        {
            breakdownList.Add(new CollectionCategoryDto { Category = "Credit Card", Amount = cardTotal, BaseAmount = cardTotal });
        }
        if (petroTotal > 0)
        {
            breakdownList.Add(new CollectionCategoryDto { Category = "PetroCard", Amount = petroTotal, BaseAmount = petroTotal });
        }
        foreach (var kvp in dynamicAmounts)
        {
            if (kvp.Value > 0)
            {
                string disp = dynamicDisplayNames.TryGetValue(kvp.Key, out var dname) ? dname : kvp.Key;
                breakdownList.Add(new CollectionCategoryDto { Category = disp, Amount = kvp.Value, BaseAmount = kvp.Value });
            }
        }
        if (TotalDebtors > 0)
        {
            breakdownList.Add(new CollectionCategoryDto { Category = "Debtors", Amount = TotalDebtors, BaseAmount = TotalDebtors });
        }
        double testAmt = TestingRows.Sum(t => t.Amount);
        if (testAmt > 0)
        {
            breakdownList.Add(new CollectionCategoryDto { Category = "Testing", Amount = testAmt, BaseAmount = testAmt, IsInformational = true });
        }
        double expAmt = Expenses.Sum(e => e.Amount ?? 0);
        if (expAmt > 0)
        {
            breakdownList.Add(new CollectionCategoryDto { Category = "Expenses", Amount = expAmt, BaseAmount = expAmt });
        }
        if ((CollectionReduction ?? 0) > 0)
        {
            breakdownList.Add(new CollectionCategoryDto { Category = "Collection Reduction", Amount = -(CollectionReduction ?? 0), BaseAmount = -(CollectionReduction ?? 0) });
        }

        double totalFuelCollection = breakdownList.Where(b => !b.IsInformational).Sum(b => b.Amount);
        double authoritativeCollection = totalFuelCollection > 0 ? totalFuelCollection : FinalAdjusted;
        double authoritativeGross = GrossSales;
        double authoritativeMismatch = authoritativeCollection - authoritativeGross;
        double totalDebtorRepayments = DebtorRepayments
            .Where(r => (r.Amount ?? 0) > 0)
            .Sum(r => r.Amount ?? 0);

        var recon = new DsmReconciliationPrintBlock
        {
            GrossSales            = authoritativeGross,
            TotalSales            = authoritativeGross,
            DebtorRepaymentsAdded = totalDebtorRepayments,
            ExpectedGrossAmount   = authoritativeGross,
            TotalCollection       = authoritativeCollection,
            Creditors             = TotalDebtors,
            Testing               = testAmt,
            Expenses              = expAmt,
            Cash                  = c2,
            Mismatch              = authoritativeMismatch
        };

        var personalDebtorPrintRows = DebtorRepayments
            .Where(r => !string.IsNullOrWhiteSpace(r.DebtorName) && (r.Amount ?? 0) > 0)
            .Select(r => new DsmPersonalDebtorPrintRow
            {
                DebtorName    = r.DebtorName,
                FuelProduct   = r.DebtorName,
                Remarks       = r.Notes ?? "",
                PaymentMethod = r.PaymentMode,
                Amount        = r.Amount ?? 0
            }).ToList();

        var printData = new DsmSheetPrintData
        {
            StationName  = stationName,
            CompanyName  = stationName,
            Date         = SelectedDate.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture),
            Shift        = SelectedShift,
            DsmName      = DsmName,
            PumpNo       = SelectedPump?.DisplayText ?? "",
            PrintedAt    = DateTime.Now.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            StartTime    = StartTime,
            EndTime      = EndTime,
            NozzleRows   = nozzleRows,
            TotalLitres  = TotalLitres,
            GrossSales   = GrossSales,
            Payments     = payments,
            Cash1Denom   = cash1Denom,
            CashDenom    = cashDenom,
            Cash3Denom   = cash3Denom,
            ConnectedPumpsDisplay = ActiveConnectedPumpsDisplay,
            CumulativeFuelSales = cumulativeSales,
            Creditors    = creditors,
            TotalCreditors = TotalDebtors,
            Expenses     = expenses,
            TotalExpenses = Expenses.Sum(e => e.Amount ?? 0),
            Testing      = testing,
            TotalTesting = TestingRows.Sum(t => t.Amount),
            Reconciliation = recon,
            CollectionBreakdown = breakdownList,
            PersonalDebtors = personalDebtorPrintRows,
            TotalPersonalDebtors = personalDebtorPrintRows.Sum(r => r.Amount)
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

        if (ShiftFuelTypeSummaries.Count == 0)
        {
            try
            {
                var shiftRepo = App.Services.GetRequiredService<Core.Repositories.IShiftRepository>();
                var shiftResult = shiftRepo.GetShiftAsync(SelectedDate, SelectedShift).GetAwaiter().GetResult();
                if (shiftResult.Success && shiftResult.Data != null)
                {
                    var fuelResult = _dsmService.GetShiftFuelTypeSummariesAsync(shiftResult.Data.ShiftId).GetAwaiter().GetResult();
                    if (fuelResult.Success && fuelResult.Data != null && fuelResult.Data.Count > 0)
                    {
                        foreach (var f in fuelResult.Data) ShiftFuelTypeSummaries.Add(f);
                        HasShiftFuelTypeSummaries = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Could not fetch ShiftFuelTypeSummaries in PrintShiftSummary");
            }
        }

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
            Date           = SelectedDate.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture),
            Shift          = SelectedShift,
            PrintedAt      = DateTime.Now.ToString("dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            Rows           = rows,
            CumulativeFuelSales = ShiftFuelTypeSummaries.ToList(),
            TotalSales     = rows.Sum(r => r.GrossSales),
            TotalCollection = rows.Sum(r => r.TotalCollection),
            TotalMismatch  = rows.Sum(r => r.Mismatch)
        };

        _printService.PrintShiftSummary(printData);
    }
}
