using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Core.Repositories;
using FuelPro.Data;
using FuelPro.Data.Services;
using FuelPro.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog;

namespace FuelPro.UI.ViewModels;

public class DsmPendingSubmission : ObservableObject
{
    public Guid Id { get; set; }
    public string DsmName { get; set; } = string.Empty;
    public string DsmUserId { get; set; } = string.Empty; // Supabase SyncGuid
    public int PumpId { get; set; }
    public DateTime ShiftDate { get; set; }
    public string ShiftType { get; set; } = "A";
    public DateTime SubmittedAt { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string? AttachmentUrl { get; set; }
    public string MetadataJson { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public string ProcessingStatus { get; set; } = "Pending";
    public bool IsRecovery { get; set; }
    public string? ClaimedByMachineId { get; set; }
    public DateTime? ClaimedAt { get; set; }

    public List<int> ConnectedPumpIds
    {
        get
        {
            if (string.IsNullOrEmpty(MetadataJson)) return new List<int>();
            try
            {
                var metaObj = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(MetadataJson);
                if (metaObj != null)
                {
                    var list = new List<int>();
                    if (metaObj["connectedPumpIds"] != null)
                    {
                        var arr = metaObj["connectedPumpIds"]!.ToObject<List<int>>();
                        if (arr != null) list.AddRange(arr);
                    }
                    else if (metaObj["connectedPumpId"] != null)
                    {
                        var scalar = (int?)metaObj["connectedPumpId"];
                        if (scalar.HasValue && scalar.Value > 0) list.Add(scalar.Value);
                    }
                    return list.Where(id => id != PumpId && id > 0).Distinct().ToList();
                }
            }
            catch {}
            return new List<int>();
        }
    }

    public int? ConnectedPumpId => ConnectedPumpIds.Count > 0 ? ConnectedPumpIds[0] : null;

    public string TitleDisplay
    {
        get
        {
            var connected = ConnectedPumpIds;
            string baseTitle;
            if (connected.Count > 0)
            {
                var connectedStr = string.Join(" + ", connected.Select(id => $"Pump {id}"));
                baseTitle = $"{DsmName} - Pump {PumpId} + {connectedStr} (Connected) - Shift {ShiftType}";
            }
            else
            {
                baseTitle = $"{DsmName} - Pump {PumpId} - Shift {ShiftType}";
            }
            return IsRecovery ? $"{baseTitle} [RECOVERY REQUIRED]" : baseTitle;
        }
    }
    public string ShiftDateDisplay => ShiftDate.ToString("dd MMM yyyy");
    public string SubmittedTimeDisplay => SubmittedAt.ToLocalTime().ToString("hh:mm tt");

    public string StationName
    {
        get
        {
            if (string.IsNullOrEmpty(MetadataJson)) return string.Empty;
            try
            {
                var metaObj = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(MetadataJson);
                if (metaObj != null)
                {
                    var name = (string?)metaObj["stationName"] ?? (string?)metaObj["StationName"] ?? (string?)metaObj["station_name"];
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
            }
            catch {}
            return string.Empty;
        }
    }

    public string PersonalLedgerName
    {
        get
        {
            if (string.IsNullOrEmpty(MetadataJson)) return string.Empty;
            try
            {
                var metaObj = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(MetadataJson);
                if (metaObj != null)
                {
                    var name = (string?)metaObj["personalLedgerName"] ?? (string?)metaObj["PersonalLedgerName"] ?? (string?)metaObj["personal_ledger_name"];
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
            }
            catch {}
            return string.Empty;
        }
    }
}


public partial class DsmNozzleRow : ObservableObject
{
    public Action? OnRowChanged { get; set; }

    [ObservableProperty] private int _nozzleId;
    [ObservableProperty] private string _fuelType = "";
    [ObservableProperty] private double _openingReading;
    [ObservableProperty] private double _closingReading;
    [ObservableProperty] private double _rate;
    [ObservableProperty] private double _saleLitres;
    [ObservableProperty] private double _amount;
    
    // Continuity validation
    [ObservableProperty] private double? _expectedOpening;
    [ObservableProperty] private DateTime? _predecessorDate;
    [ObservableProperty] private string? _predecessorShift;
    [ObservableProperty] private int _hopsBack;
    [ObservableProperty] private int _pumpId;
    [ObservableProperty] private string _continuityWarning = "";
    [ObservableProperty] private bool _hasContinuityError;

    // Rate validation & ground truth
    [ObservableProperty] private double _submittedRate;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyRate))]
    private double? _historicalRate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApplyRate))]
    private bool _hasRateDiscrepancy;

    [ObservableProperty] private bool _isHistoricalMismatch;
    [ObservableProperty] private string _rateWarningMessage = "";

    public bool CanApplyRate => HasRateDiscrepancy && HistoricalRate.HasValue && HistoricalRate.Value > 0;

    [RelayCommand]
    public void ApplyHistoricalRate()
    {
        if (HistoricalRate.HasValue && HistoricalRate.Value > 0)
        {
            Rate = HistoricalRate.Value;
            HasRateDiscrepancy = false;
            RateWarningMessage = "";
            OnPropertyChanged(nameof(CanApplyRate));
            OnRowChanged?.Invoke();
        }
    }

    public string PumpLabel => $"Pump {PumpId}";
    public string NozzleNumber => NozzleId.ToString();

    partial void OnOpeningReadingChanged(double value) => Recalculate();
    partial void OnClosingReadingChanged(double value) => Recalculate();
    partial void OnRateChanged(double value)
    {
        Recalculate();
        if (HistoricalRate.HasValue && Math.Abs(value - HistoricalRate.Value) < 0.001)
        {
            HasRateDiscrepancy = false;
            RateWarningMessage = "";
            OnPropertyChanged(nameof(CanApplyRate));
        }
    }

    public void Recalculate()
    {
        SaleLitres = Math.Max(0, ClosingReading - OpeningReading);
        Amount = SaleLitres * Rate;
        
        if (ExpectedOpening.HasValue)
        {
            HasContinuityError = Math.Abs(OpeningReading - ExpectedOpening.Value) > 0.0001;
            if (HasContinuityError)
            {
                string predContext = PredecessorDate.HasValue && !string.IsNullOrWhiteSpace(PredecessorShift)
                    ? $" from Shift {PredecessorShift} ({PredecessorDate.Value:dd-MMM-yyyy}, Pump {PumpId}, Nozzle {NozzleId})"
                    : "";
                string gapContext = HopsBack > 1
                    ? " [intermediate shifts missing]"
                    : "";
                ContinuityWarning = $"⚠️ Continuity Gap: Opening ({OpeningReading}) does not match previous closing ({ExpectedOpening.Value}){predContext}{gapContext}";
            }
            else
            {
                ContinuityWarning = "";
            }
        }
        else
        {
            HasContinuityError = false;
            ContinuityWarning = "";
        }

        OnRowChanged?.Invoke();
    }
}

public partial class DsmDebitRow : ObservableObject
{
    public Action? OnRowChanged { get; set; }
    [ObservableProperty] private string _debtorName = "";
    [ObservableProperty] private double _amount;
    [ObservableProperty] private string? _vehicleNumber;
    [ObservableProperty] private string? _slipNumber;
    [ObservableProperty] private string? _entryTime;
    [ObservableProperty] private string? _remarks;

    partial void OnAmountChanged(double value) => OnRowChanged?.Invoke();
    partial void OnDebtorNameChanged(string value) => OnRowChanged?.Invoke();
}

public partial class DsmCardSwipeRow : ObservableObject
{
    public Action? OnRowChanged { get; set; }
    [ObservableProperty] private string _mode = "";
    [ObservableProperty] private double _amount;
    [ObservableProperty] private string _tid = "";
    [ObservableProperty] private string _batch = "";
    [ObservableProperty] private string _otp = "";
    [ObservableProperty] private string? _transactionId;
    [ObservableProperty] private string? _slot;
    public bool IsSyntheticSummary { get; set; }

    public string TidDisplay
    {
        get => string.IsNullOrWhiteSpace(Tid) ? "—" : Tid;
        set
        {
            Tid = value == "—" ? string.Empty : value;
            OnPropertyChanged(nameof(TidDisplay));
        }
    }

    partial void OnAmountChanged(double value) => OnRowChanged?.Invoke();
    partial void OnModeChanged(string value) => OnRowChanged?.Invoke();
    partial void OnOtpChanged(string value) => OnRowChanged?.Invoke();
    partial void OnTidChanged(string value)
    {
        OnPropertyChanged(nameof(TidDisplay));
        OnRowChanged?.Invoke();
    }
}

public partial class DynamicCollectionSummaryDto : ObservableObject
{
    [ObservableProperty] private string _code = "";
    [ObservableProperty] private string _displayName = "";
    [ObservableProperty] private double _amount;
}

public partial class DsmTestingRow : ObservableObject
{
    [ObservableProperty] private int _nozzleId;
    [ObservableProperty] private string _fuelType = "";
    [ObservableProperty] private double _amount;
    [ObservableProperty] private int _pumpId;
    [ObservableProperty] private double? _rupeeAmount;
    public bool HasExplicitRupeeAmount { get; set; }
}

public partial class DsmOilDefSaleRow : ObservableObject
{
    [ObservableProperty] private int _productId;
    [ObservableProperty] private string _productName = string.Empty;
    [ObservableProperty] private string _category = "Oil";
    [ObservableProperty] private double _quantity;
    [ObservableProperty] private double _price;
    [ObservableProperty] private string _unit = "Litre";

    public double Total => Quantity * Price;

    partial void OnQuantityChanged(double value) => OnPropertyChanged(nameof(Total));
    partial void OnPriceChanged(double value) => OnPropertyChanged(nameof(Total));
}

public partial class DsmApprovalQueueViewModel : ObservableObject, IDisposable
{
    public static string CleanAndUnescapeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        string cur = json.Trim();
        for (int i = 0; i < 3; i++)
        {
            if (cur.StartsWith("\"") && cur.EndsWith("\"") && cur.Length >= 2)
            {
                try
                {
                    var unescaped = JsonConvert.DeserializeObject<string>(cur);
                    if (!string.IsNullOrWhiteSpace(unescaped))
                    {
                        cur = unescaped.Trim();
                        continue;
                    }
                }
                catch { }
            }
            break;
        }
        return cur;
    }

    public static string ExtractMetadataJson(dynamic? item)
    {
        if (item == null) return string.Empty;
        object? raw = null;
        try { raw = item.Metadata; } catch { }
        if (raw == null) { try { raw = item.metadata; } catch { } }
        if (raw == null && item is Newtonsoft.Json.Linq.JObject jobj)
        {
            raw = jobj["Metadata"] ?? jobj["metadata"];
        }
        if (raw == null) return string.Empty;
        if (raw is string str) return CleanAndUnescapeJson(str);
        if (raw is Newtonsoft.Json.Linq.JValue jval && jval.Value is string sVal) return CleanAndUnescapeJson(sVal);
        return CleanAndUnescapeJson(JsonConvert.SerializeObject(raw));
    }

    public static Newtonsoft.Json.Linq.JObject? ParseMetadataObject(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return null;
        string clean = CleanAndUnescapeJson(metadataJson);
        try
        {
            return Newtonsoft.Json.Linq.JObject.Parse(clean);
        }
        catch
        {
            return null;
        }
    }

    private readonly IServiceProvider _serviceProvider;
    private readonly SupabaseDsmService _supabaseService;
    private readonly DsmEntryService _dsmEntryService;
    private readonly INozzleReadingRepository _nozzleRepo;
    private readonly IDsmCalculationService _dsmCalculationService;
    private readonly AuthService _authService;
    private readonly DsmSubmissionPollingService _pollingService;
    private readonly IFeatureToggleService? _featureToggleService;
    private readonly ICollectionTypeService? _collectionTypeService;
    private List<CollectionTypeMaster>? _availableCollectionTypes;

    private List<CollectionTypeMaster> GetCollectionTypesSnapshot()
    {
        if (_availableCollectionTypes != null && _availableCollectionTypes.Count > 0)
            return _availableCollectionTypes;

        var now = DateTime.Now;
        _availableCollectionTypes = new List<CollectionTypeMaster>
        {
            new() { Code = "PHONEPE", DisplayName = "PhonePe", Category = "Online", HasTidBatch = true, DisplayOrder = 1, IsActive = true, IsSystem = true, CreatedAt = now },
            new() { Code = "CREDIT_CARD", DisplayName = "Credit / Debit Card", Category = "Card", HasTidBatch = true, DisplayOrder = 2, IsActive = false, IsSystem = true, CreatedAt = now },
            new() { Code = "PETROCARD", DisplayName = "PetroCard", Category = "Card", HasTidBatch = true, DisplayOrder = 3, IsActive = true, IsSystem = true, CreatedAt = now },
            new() { Code = "PINELABS_CARD", DisplayName = "Pine Labs Card", Category = "Card", HasTidBatch = true, DisplayOrder = 4, IsActive = true, IsSystem = true, CreatedAt = now },
            new() { Code = "SBI_REDEEM", DisplayName = "SBI Redeem", Category = "Card", HasTidBatch = false, DisplayOrder = 5, IsActive = true, IsSystem = false, CreatedAt = now },
            new() { Code = "PAYTM", DisplayName = "Paytm", Category = "Online", HasTidBatch = false, DisplayOrder = 6, IsActive = true, IsSystem = false, CreatedAt = now },
            new() { Code = "QR", DisplayName = "QR / Online", Category = "Online", HasTidBatch = false, DisplayOrder = 7, IsActive = true, IsSystem = false, CreatedAt = now },
            new() { Code = "MOBIKWIK", DisplayName = "Mobikwik", Category = "Online", HasTidBatch = false, DisplayOrder = 8, IsActive = true, IsSystem = false, CreatedAt = now },
            new() { Code = "CASH_DEPOSIT", DisplayName = "Cash Deposit (Bank)", Category = "Cash", HasTidBatch = false, DisplayOrder = 9, IsActive = true, IsSystem = true, CreatedAt = now },
            new() { Code = "OTHERS", DisplayName = "Other Online / UPI", Category = "Other", HasTidBatch = false, DisplayOrder = 10, IsActive = true, IsSystem = true, CreatedAt = now }
        };
        return _availableCollectionTypes;
    }

    private async Task EnsureAvailableCollectionTypesAsync()
    {
        if (_availableCollectionTypes != null && _availableCollectionTypes.Count > 0)
            return;

        try
        {
            if (_collectionTypeService != null)
            {
                var types = await _collectionTypeService.GetAllCollectionTypesAsync();
                if (types != null && types.Count > 0)
                {
                    _availableCollectionTypes = types;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load collection types from collection type service, using fallback snapshot");
        }

        GetCollectionTypesSnapshot();
    }
    private readonly IShiftCycleService _shiftCycleService;
    private readonly Action? _featureChangedHandler;
    private readonly ILogger _logger = Log.ForContext<DsmApprovalQueueViewModel>();

    public ObservableCollection<DsmPendingSubmission> PendingSubmissions { get; } = new();
    public ObservableCollection<DsmTestingRow> SubmissionTestingEntries { get; } = new();
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveSelection))]
    [NotifyPropertyChangedFor(nameof(HeaderTitleDisplay))]
    [NotifyPropertyChangedFor(nameof(HeaderShiftDateDisplay))]
    [NotifyPropertyChangedFor(nameof(HeaderSubmittedTimeDisplay))]
    [NotifyPropertyChangedFor(nameof(StationNameDisplay))]
    [NotifyPropertyChangedFor(nameof(HeaderStationNameDisplay))]
    [NotifyPropertyChangedFor(nameof(HasStationName))]
    [NotifyPropertyChangedFor(nameof(PersonalLedgerTitle))]
    [NotifyPropertyChangedFor(nameof(IsSubmissionComplete))]
    [NotifyPropertyChangedFor(nameof(SubmissionIncompleteReason))]
    [NotifyPropertyChangedFor(nameof(CanApprove))]
    private DsmPendingSubmission? _selectedSubmission;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isLoadingSubmissions;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSubmissionComplete))]
    [NotifyPropertyChangedFor(nameof(SubmissionIncompleteReason))]
    [NotifyPropertyChangedFor(nameof(CanApprove))]
    private bool _isLoadingDetails;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSubmissionComplete))]
    [NotifyPropertyChangedFor(nameof(SubmissionIncompleteReason))]
    [NotifyPropertyChangedFor(nameof(CanApprove))]
    private string? _metadataParseError;

    public bool HasValidItemizedDebtors()
    {
        if (CreditAmount < 0) return false;
        if (CreditAmount <= 0.009) return true;
        if (DebtorEntries == null || DebtorEntries.Count == 0) return false;
        if (DebtorEntries.Any(d => d.Amount < 0)) return false;
        if (DebtorEntries.Any(d => d.Amount > 0.009 && string.IsNullOrWhiteSpace(d.DebtorName))) return false;
        if (DebtorEntries.Any(d => !string.IsNullOrWhiteSpace(d.DebtorName) && d.Amount <= 0.009)) return false;
        double validTotal = DebtorEntries.Where(d => !string.IsNullOrWhiteSpace(d.DebtorName) && d.Amount > 0.009).Sum(d => d.Amount);
        if (validTotal <= 0.009) return false;
        if (Math.Abs(CreditAmount - validTotal) > 0.01) return false;
        return true;
    }

    public bool IsSubmissionComplete
    {
        get
        {
            if (SelectedSubmission == null) return false;
            if (IsLoadingDetails) return false;
            if (!string.IsNullOrEmpty(MetadataParseError)) return false;
            if (NozzleReadings == null || NozzleReadings.Count == 0) return false;

            foreach (var n in NozzleReadings)
            {
                if (n.Rate <= 0) return false;
                if (n.ClosingReading < n.OpeningReading) return false;
            }

            if (HasContinuityWarnings && (!OverrideContinuity || string.IsNullOrWhiteSpace(OverrideRemark)))
            {
                return false;
            }

            if (CardSwipeDetails.Any(c => c.Amount > 0 && string.IsNullOrWhiteSpace(c.Mode)))
            {
                return false;
            }

            if (!HasValidItemizedDebtors())
            {
                return false;
            }

            return true;
        }
    }

    public string SubmissionIncompleteReason
    {
        get
        {
            if (SelectedSubmission == null) return "No submission selected.";
            if (IsLoadingDetails) return "⏳ Fetching submission details from PWA App...";
            if (!string.IsNullOrEmpty(MetadataParseError)) return $"⚠️ Failed to parse submission payload: {MetadataParseError}";
            if (NozzleReadings == null || NozzleReadings.Count == 0) return "⚠️ Incomplete submission: No nozzle readings fetched from PWA App.";

            foreach (var n in NozzleReadings)
            {
                if (n.Rate <= 0) return $"⚠️ Incomplete submission: Fuel rate is missing or 0 for Nozzle #{n.NozzleId}.";
                if (n.ClosingReading < n.OpeningReading) return $"⚠️ Invalid readings: Closing reading ({n.ClosingReading:F2}) is less than opening reading ({n.OpeningReading:F2}) on Nozzle #{n.NozzleId}.";
            }

            if (HasContinuityWarnings && (!OverrideContinuity || string.IsNullOrWhiteSpace(OverrideRemark)))
            {
                return "⚠️ Continuity warnings detected. Please verify readings or provide a manager's override remark.";
            }

            if (CardSwipeDetails.Any(c => c.Amount > 0 && string.IsNullOrWhiteSpace(c.Mode)))
            {
                return "⚠️ Unclassified card/payment swipe detected with missing collection type mode.";
            }

            if (!HasValidItemizedDebtors())
            {
                return $"Unitemized credit detected (₹{CreditAmount:N2}). All credit sales must be itemized with customer names in the Debtor Entries Log before approval.";
            }

            return string.Empty;
        }
    }

    public bool CanApprove => SelectedSubmission != null && IsEditable && IsSubmissionComplete;

    public void NotifySubmissionStateChanged()
    {
        OnPropertyChanged(nameof(IsSubmissionComplete));
        OnPropertyChanged(nameof(SubmissionIncompleteReason));
        OnPropertyChanged(nameof(CanApprove));
        ApproveCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty] private double _cashAmount;
    private readonly System.Threading.SemaphoreSlim _refreshLock = new(1, 1);
    private long _refreshGeneration;
    [ObservableProperty] private double _upiAmount;
    [ObservableProperty] private double _cardAmount;
    [ObservableProperty] private double _pineLabsAmount;
    [ObservableProperty] private double _petroCardAmount;

    // Split Shift Amounts (Morning / Day / Night)
    [ObservableProperty] private double _phonePeMorning;
    [ObservableProperty] private double _phonePeDay;
    [ObservableProperty] private double _phonePeNight;
    [ObservableProperty] private double _creditCardMorning;
    [ObservableProperty] private double _creditCardDay;
    [ObservableProperty] private double _creditCardNight;
    [ObservableProperty] private double _petroCardMorning;
    [ObservableProperty] private double _petroCardDay;
    [ObservableProperty] private double _petroCardNight;
    [ObservableProperty] private bool _hasSplitValues;
    public bool HasPhonePeDay => PhonePeDay > 0;
    public bool HasCreditCardDay => CreditCardDay > 0;
    public bool HasPetroCardDay => PetroCardDay > 0;
    private bool _isSyncingSplits;

    [ObservableProperty] private double _cashDepositAmount;
    [ObservableProperty] private double _collectionReductionAmount;
    [ObservableProperty] private string? _collectionReductionRemark;
    [ObservableProperty] private CashDenomRow _cash1;
    [ObservableProperty] private CashDenomRow _cash2;
    [ObservableProperty] private CashDenomRow _cash3;
    [ObservableProperty] private bool _isCash3Enabled;
    public bool IsCash1DenominationsEnabled => (_featureToggleService?.IsFeatureEnabled("Operations_Cash1", true) ?? true) && (_featureToggleService?.IsFeatureEnabled("Operations_Cash1Denominations", false) ?? false);
    public bool ShowCash1DenominationFields => IsCash1DenominationsEnabled || (Cash1?.HasDenominationCounts ?? false);
    public bool ShowCash1ScalarField => !ShowCash1DenominationFields;
    public bool IsCash2DenominationsEnabled => (_featureToggleService?.IsFeatureEnabled("Operations_Cash2", true) ?? true) && (_featureToggleService?.IsFeatureEnabled("Operations_Cash2Denominations", true) ?? true);
    public ObservableCollection<DsmDebtorRepaymentRow> DebtorRepayments { get; } = new();
    public bool HasDebtorRepayments => DebtorRepayments.Count > 0;
    private bool _isLoadingSubmission;
    [ObservableProperty] private double _othersAmount;
    [ObservableProperty] private double _creditAmount;
    [ObservableProperty] private double _expenseAmount;
    [ObservableProperty] private string _expenseNotes = "";
    [ObservableProperty] private double _shortAmount;
    [ObservableProperty] private double _excessAmount;
    [ObservableProperty] private string _submissionNotes = "";
    [ObservableProperty] private string? _attachmentUrl;
    
    // Computed Values
    [ObservableProperty] private double _grossSales;
    [ObservableProperty] private double _totalLitres;
    [ObservableProperty] private double _totalTestingAmount;
    [ObservableProperty] private double _netFuelSales;
    [ObservableProperty] private double _totalOilDefSales;
    [ObservableProperty] private double _totalCollections;
    [ObservableProperty] private double _totalPaymentIn;
    [ObservableProperty] private double _mismatchAmount; // Collections - (NetFuelSales + OilDefSales)

    public ObservableCollection<DsmNozzleRow> NozzleReadings { get; } = new();
    public ObservableCollection<FuelTypeReadingSummary> FuelTypeSummaries { get; } = new();
    public ObservableCollection<DsmDebitRow> DebtorEntries { get; } = new();
    public ObservableCollection<DsmCardSwipeRow> CardSwipeDetails { get; } = new();
    public ObservableCollection<DsmCardSwipeRow> ManualTidTransactions { get; } = new();
    public bool HasManualTidTransactions => ManualTidTransactions.Count > 0;
    public bool HasOtpTransactions => CardSwipeDetails.Count > 0;
    public bool HasTestingEntries => SubmissionTestingEntries.Count > 0;
    public bool HasQrPayments => QrPayments.Count > 0;
    public bool HasExpenses => SubmissionExpenses.Count > 0 || ExpenseAmount > 0;
    public bool HasKhandhareEntries => KhandhareEntries.Count > 0;
    public bool HasPersonalDebtors => PersonalDebtors.Count > 0;

    public string ResolveCollectionTypeDisplayName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        string trimmed = code.Trim();

        if (_availableCollectionTypes != null && _availableCollectionTypes.Count > 0)
        {
            var match = _availableCollectionTypes.FirstOrDefault(c =>
                string.Equals(c.Code, trimmed, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase) ||
                c.CollectionTypeId.ToString() == trimmed);

            if (match != null && !string.IsNullOrWhiteSpace(match.DisplayName))
            {
                return match.DisplayName;
            }
        }

        var norm = PaymentCollection.NormalizeCollectionTypeCode(trimmed);
        if (norm == "CREDIT_CARD") return "Credit Card";
        if (norm == "PETROCARD") return "PetroCard";
        if (norm == "PHONEPE") return "PhonePe";
        if (norm == "PINELABS_CARD") return "PineLab Card";
        if (norm == "SBI_REDEEM") return "SBI Redeem";
        if (norm == "PAYTM") return "Paytm";
        if (norm == "MOBIKWIK") return "Mobikwik";
        if (norm == "QR") return "QR / Online";

        return trimmed;
    }

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

    public bool IsPhonePeScalarVisible => PhonePeIsActive || PhonePeMorning > 0 || PhonePeNight > 0 || UpiAmount > 0;
    public bool IsCreditCardScalarVisible => CreditCardIsActive || CreditCardMorning > 0 || CreditCardNight > 0 || CardAmount > 0;
    public bool IsPetroCardScalarVisible => PetroCardIsActive || PetroCardMorning > 0 || PetroCardNight > 0 || PetroCardAmount > 0;

    public bool IsNightShift => IsThreeShiftMode 
        ? string.Equals(_shiftCycleService?.NormalizeShiftType(SelectedSubmission?.ShiftType), "C", StringComparison.OrdinalIgnoreCase)
        : string.Equals(_shiftCycleService?.NormalizeShiftType(SelectedSubmission?.ShiftType), "A", StringComparison.OrdinalIgnoreCase);
    public bool IsDayShift => string.Equals(_shiftCycleService?.NormalizeShiftType(SelectedSubmission?.ShiftType), "B", StringComparison.OrdinalIgnoreCase);
    public bool IsShiftC => string.Equals(_shiftCycleService?.NormalizeShiftType(SelectedSubmission?.ShiftType), "C", StringComparison.OrdinalIgnoreCase);

    public bool IsThreeShiftMode => SelectedSubmission == null || (_shiftCycleService?.GetShiftsForDate(SelectedSubmission.ShiftDate).Count ?? 0) >= 3;
    public bool IsMorningNightSplitEnabled => !IsThreeShiftMode && (_featureToggleService?.IsFeatureEnabled("Collection_UseMorningNight", false) ?? false) && IsNightShift;

    public bool IsPhonePeSplitEnabled => IsMorningNightSplitEnabled && PhonePeEnableMorning && PhonePeEnableNight;
    public bool IsCreditCardSplitEnabled => IsMorningNightSplitEnabled && CreditCardEnableMorning && CreditCardEnableNight;
    public bool IsPetroCardSplitEnabled => IsMorningNightSplitEnabled && PetroCardEnableMorning && PetroCardEnableNight;

    public string PhonePeMorningLabel => IsPhonePeSplitEnabled ? MorningLabel : "Amount (₹)";
    public string CreditCardMorningLabel => IsCreditCardSplitEnabled ? MorningLabel : "Amount (₹)";
    public string PetroCardMorningLabel => IsPetroCardSplitEnabled ? MorningLabel : "Amount (₹)";

    public string MorningLabel => (!(_featureToggleService?.IsFeatureEnabled("Collection_UseMorningNight", false) ?? false) || IsThreeShiftMode) ? "Amount (₹)" : (IsDayShift ? "Day (8am - 8pm) (₹)" : "Morning (12am - 8am) (₹)");

    public ObservableCollection<DynamicCollectionEntryRow> DynamicCollections { get; } = new();
    [ObservableProperty] private bool _hasAmbiguousLegacyDuplicates;
    [ObservableProperty] private string _legacyDuplicateAdvisoryMessage = "";
    public void UpdateCollectionTypeTotals()
    {
        var totals = new List<CollectionTypeTotalDto>();
        var candidateCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in ManualTidTransactions)
        {
            if (!string.IsNullOrWhiteSpace(tx.Mode))
            {
                candidateCodes.Add(PaymentCollection.NormalizeCollectionTypeCode(tx.Mode));
            }
        }
        foreach (var tx in CardSwipeDetails.Where(c => !string.IsNullOrWhiteSpace(c.Otp)))
        {
            if (!string.IsNullOrWhiteSpace(tx.Mode))
            {
                candidateCodes.Add(PaymentCollection.NormalizeCollectionTypeCode(tx.Mode));
            }
        }

        if (_availableCollectionTypes != null)
        {
            foreach (var c in _availableCollectionTypes.Where(m => (m.HasTidBatch || m.IsOtpEligible) && m.IsActive))
            {
                candidateCodes.Add(PaymentCollection.NormalizeCollectionTypeCode(c.Code));
            }
        }

        foreach (var code in candidateCodes)
        {
            double tidSum = ManualTidTransactions
                .Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Mode), code, StringComparison.OrdinalIgnoreCase))
                .Sum(t => t.Amount);

            double otpSum = CardSwipeDetails
                .Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Mode), code, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(t.Otp))
                .Sum(t => t.Amount);

            if (tidSum > 0 || otpSum > 0)
            {
                var master = _availableCollectionTypes?.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), code, StringComparison.OrdinalIgnoreCase));
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

    public ObservableCollection<DsmPersonalDebtor> PersonalDebtors { get; } = new();
    public ObservableCollection<KhandharePetroleumEntry> KhandhareEntries { get; } = new();
    public ObservableCollection<DsmQrPaymentEntry> QrPayments { get; } = new();
    public ObservableCollection<DsmOilDefSaleRow> OilDefSales { get; } = new();
    public ObservableCollection<Expense> SubmissionExpenses { get; } = new();
    public ObservableCollection<DynamicCollectionSummaryDto> DynamicCollectionSummaries { get; } = new();
    public bool HasDynamicSummaries => DynamicCollectionSummaries.Count > 0;
    public ObservableCollection<ManualCollectionEntryRow> ManualCollections { get; } = new();
    public bool HasManualCollections => ManualCollections.Count > 0;
    public ObservableCollection<CollectionTypeTotalDto> CollectionTypeTotals { get; } = new();
    public bool HasCollectionTypeTotals => CollectionTypeTotals.Count > 0;

    private readonly Dictionary<string, double> _dynamicManualAmounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _dynamicManualTids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _dynamicManualBatches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _dynamicManualSlots = new(StringComparer.OrdinalIgnoreCase);

    // Master Product Catalog & Editing Options
    public ObservableCollection<ProductMaster> AvailableProducts { get; } = new();
    [ObservableProperty] private bool _showProductCatalogPanel;
    [ObservableProperty] private string _newProductName = "";
    [ObservableProperty] private string _newProductCategory = "Oil";
    [ObservableProperty] private string _newProductUnit = "Litre";
    [ObservableProperty] private double _newProductDefaultRate;
    public List<string> ProductCategories { get; } = new() { "Oil", "DEF" };
    public List<string> ProductUnits { get; } = new() { "Litre", "Bottle", "Can", "Piece", "Bucket" };

    // Validations & Overrides
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSubmissionComplete))]
    [NotifyPropertyChangedFor(nameof(SubmissionIncompleteReason))]
    [NotifyPropertyChangedFor(nameof(CanApprove))]
    private bool _hasContinuityWarnings;

    [ObservableProperty]
    private bool _hasRateDiscrepancies;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSubmissionComplete))]
    [NotifyPropertyChangedFor(nameof(SubmissionIncompleteReason))]
    [NotifyPropertyChangedFor(nameof(CanApprove))]
    private bool _overrideContinuity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSubmissionComplete))]
    [NotifyPropertyChangedFor(nameof(SubmissionIncompleteReason))]
    [NotifyPropertyChangedFor(nameof(CanApprove))]
    private string _overrideRemark = "";

    [ObservableProperty] private string _validationSummary = "";

    // Edit UI Visibility
    [ObservableProperty] private bool _isEditing;

    // Recently Approved Submissions & Read-Only Details
    [ObservableProperty] private ObservableCollection<DsmApprovedSubmissionDto> _filteredApprovedSubmissions = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveSelection))]
    [NotifyPropertyChangedFor(nameof(HeaderTitleDisplay))]
    [NotifyPropertyChangedFor(nameof(HeaderShiftDateDisplay))]
    [NotifyPropertyChangedFor(nameof(HeaderSubmittedTimeDisplay))]
    [NotifyPropertyChangedFor(nameof(StationNameDisplay))]
    [NotifyPropertyChangedFor(nameof(HeaderStationNameDisplay))]
    [NotifyPropertyChangedFor(nameof(HasStationName))]
    [NotifyPropertyChangedFor(nameof(PersonalLedgerTitle))]
    private DsmApprovedSubmissionDto? _selectedApprovedSubmission;

    private string _localStationName = string.Empty;

    public string StationNameDisplay
    {
        get
        {
            if (SelectedSubmission != null && !string.IsNullOrWhiteSpace(SelectedSubmission.StationName))
                return SelectedSubmission.StationName;
            if (SelectedApprovedSubmission != null && !string.IsNullOrWhiteSpace(SelectedApprovedSubmission.StationName))
                return SelectedApprovedSubmission.StationName;
            return _localStationName;
        }
    }

    public string HeaderStationNameDisplay => !string.IsNullOrWhiteSpace(StationNameDisplay) ? $"🏢 {StationNameDisplay}" : string.Empty;
    public bool HasStationName => !string.IsNullOrWhiteSpace(StationNameDisplay);

    public string PersonalLedgerTitle
    {
        get
        {
            if (SelectedSubmission != null && !string.IsNullOrWhiteSpace(SelectedSubmission.PersonalLedgerName))
                return $"🏢 {SelectedSubmission.PersonalLedgerName}";
            if (SelectedApprovedSubmission != null && !string.IsNullOrWhiteSpace(SelectedApprovedSubmission.PersonalLedgerName))
                return $"🏢 {SelectedApprovedSubmission.PersonalLedgerName}";
            
            string configured = _featureToggleService?.GetFeatureDisplayName("Operations_PersonalLedger", "Personal Ledger") ?? "Personal Ledger";
            return $"🏢 {configured}";
        }
    }

    public string PersonalLedgerPwaHeader
    {
        get
        {
            string configured = _featureToggleService?.GetFeatureDisplayName("Operations_PersonalLedger", "DSM Loss") ?? "DSM Loss";
            return $"👥 {configured} (PWA)";
        }
    }

    public bool HasActiveSelection => SelectedSubmission != null || SelectedApprovedSubmission != null;
    public string HeaderTitleDisplay => SelectedSubmission != null ? SelectedSubmission.TitleDisplay : (SelectedApprovedSubmission != null ? SelectedApprovedSubmission.TitleDisplay : string.Empty);
    public string HeaderShiftDateDisplay => SelectedSubmission != null ? SelectedSubmission.ShiftDateDisplay : (SelectedApprovedSubmission != null ? SelectedApprovedSubmission.ShiftDateDisplay : string.Empty);
    public string HeaderSubmittedTimeDisplay => SelectedSubmission != null ? $"Submitted: {SelectedSubmission.SubmittedTimeDisplay}" : (SelectedApprovedSubmission != null ? $"Submitted: {SelectedApprovedSubmission.SubmittedTimeDisplay}" : string.Empty);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditable))]
    private bool _isReadOnlyMode;

    public bool IsEditable => !_isReadOnlyMode;
    [ObservableProperty] private ObservableCollection<DsmTimelineLog> _timelineLogs = new();

    // Filters
    [ObservableProperty] private bool _isFilterToday;
    [ObservableProperty] private bool _isFilterYesterday;
    [ObservableProperty] private bool _isFilterThisWeek;
    [ObservableProperty] private string _selectedDsmFilter = "All";
    [ObservableProperty] private string _selectedPumpFilter = "All";
    [ObservableProperty] private string _selectedShiftFilter = "All";
    [ObservableProperty] private string _searchTextFilter = "";

    // Filter Options
    [ObservableProperty] private ObservableCollection<string> _dsmFilterOptions = new() { "All" };
    [ObservableProperty] private ObservableCollection<string> _pumpFilterOptions = new() { "All" };
    [ObservableProperty] private ObservableCollection<string> _shiftFilterOptions = new() { "All" };

    // Summaries
    [ObservableProperty] private int _summaryApprovedCount;
    [ObservableProperty] private double _summaryGrossSales;
    [ObservableProperty] private double _summaryTotalCollection;
    [ObservableProperty] private double _summaryTotalDifference;
    [ObservableProperty] private double _summaryTotalExpenses;
    [ObservableProperty] private double _summaryTotalDebtors;
    [ObservableProperty] private double _summaryTotalTesting;
    [ObservableProperty] private double _summaryTotalOilDefSales;

    // UI Expand state
    [ObservableProperty] private bool _isRecentlyApprovedExpanded;

    public DsmApprovalQueueViewModel(IServiceProvider serviceProvider) : this()
    {
        _serviceProvider = serviceProvider;
        if (_serviceProvider != null)
        {
            _supabaseService = _serviceProvider.GetService<SupabaseDsmService>()!;
            _dsmEntryService = _serviceProvider.GetService<DsmEntryService>()!;
            _nozzleRepo = _serviceProvider.GetService<INozzleReadingRepository>()!;
            _dsmCalculationService = _serviceProvider.GetService<IDsmCalculationService>()!;
            _authService = _serviceProvider.GetService<AuthService>()!;
            _pollingService = _serviceProvider.GetService<DsmSubmissionPollingService>()!;
            _featureToggleService = _serviceProvider.GetService<IFeatureToggleService>();
            _collectionTypeService = _serviceProvider.GetService<ICollectionTypeService>();
            _shiftCycleService = _serviceProvider.GetService<IShiftCycleService>() ?? new ShiftCycleService();
        }
    }

    public DsmApprovalQueueViewModel()
    {
        Cash1 = new CashDenomRow
        {
            CashType = "Cash1",
            OnTotalChanged = () =>
            {
                if (!_isLoadingSubmission)
                {
                    CashDepositAmount = Cash1.TotalAmount;
                    OnPropertyChanged(nameof(ShowCash1DenominationFields));
                    OnPropertyChanged(nameof(ShowCash1ScalarField));
                    RecalculateTotals();
                }
            }
        };
        Cash2 = new CashDenomRow
        {
            CashType = "Cash2",
            OnTotalChanged = () =>
            {
                if (!_isLoadingSubmission)
                {
                    CashAmount = Cash2.TotalAmount;
                    RecalculateTotals();
                }
            }
        };
        Cash3 = new CashDenomRow
        {
            CashType = "Cash3",
            OnTotalChanged = () =>
            {
                if (!_isLoadingSubmission)
                {
                    RecalculateTotals();
                }
            }
        };
        _serviceProvider = App.Services;
        if (_serviceProvider != null)
        {
            _supabaseService = _serviceProvider.GetService<SupabaseDsmService>()!;
            _dsmEntryService = _serviceProvider.GetService<DsmEntryService>()!;
            _nozzleRepo = _serviceProvider.GetService<INozzleReadingRepository>()!;
            _dsmCalculationService = _serviceProvider.GetService<IDsmCalculationService>()!;
            _authService = _serviceProvider.GetService<AuthService>()!;
            _pollingService = _serviceProvider.GetService<DsmSubmissionPollingService>()!;
            _featureToggleService = _serviceProvider.GetService<IFeatureToggleService>();
            _collectionTypeService = _serviceProvider.GetService<ICollectionTypeService>();
            _shiftCycleService = _serviceProvider.GetService<IShiftCycleService>() ?? new ShiftCycleService();
        }
        else
        {
            _shiftCycleService = new ShiftCycleService();
        }

        if (_featureToggleService != null)
        {
            IsCash3Enabled = _featureToggleService.IsFeatureEnabled("Operations_Cash3", true) || Cash3.TotalAmount > 0;
            _featureChangedHandler = () =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(() =>
                    {
                        IsCash3Enabled = _featureToggleService.IsFeatureEnabled("Operations_Cash3", true) || Cash3.TotalAmount > 0;
                        OnPropertyChanged(nameof(PersonalLedgerTitle));
                        OnPropertyChanged(nameof(PersonalLedgerPwaHeader));
                        OnPropertyChanged(nameof(IsCash1DenominationsEnabled));
                        OnPropertyChanged(nameof(ShowCash1DenominationFields));
                        OnPropertyChanged(nameof(ShowCash1ScalarField));
                        OnPropertyChanged(nameof(IsCash2DenominationsEnabled));
                    });
                }
                else
                {
                    IsCash3Enabled = _featureToggleService.IsFeatureEnabled("Operations_Cash3", true) || Cash3.TotalAmount > 0;
                    OnPropertyChanged(nameof(PersonalLedgerTitle));
                    OnPropertyChanged(nameof(PersonalLedgerPwaHeader));
                    OnPropertyChanged(nameof(IsCash1DenominationsEnabled));
                    OnPropertyChanged(nameof(ShowCash1DenominationFields));
                    OnPropertyChanged(nameof(ShowCash1ScalarField));
                    OnPropertyChanged(nameof(IsCash2DenominationsEnabled));
                }
            };
            _featureToggleService.FeatureConfigurationChanged += _featureChangedHandler;
        }
        else
        {
            IsCash3Enabled = true;
        }

        DebtorEntries.CollectionChanged += (s, e) =>
        {
            if (e.NewItems != null)
            {
                foreach (DsmDebitRow item in e.NewItems)
                {
                    item.OnRowChanged ??= RecalculateTotals;
                }
            }
            RecalculateTotals();
            NotifySubmissionStateChanged();
        };

        CardSwipeDetails.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasOtpTransactions));
            NotifySubmissionStateChanged();
        };
        ManualTidTransactions.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasManualTidTransactions));
            NotifySubmissionStateChanged();
        };
        SubmissionTestingEntries.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasTestingEntries));
            NotifySubmissionStateChanged();
        };
        QrPayments.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasQrPayments));
            NotifySubmissionStateChanged();
        };
        SubmissionExpenses.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasExpenses));
            NotifySubmissionStateChanged();
        };
        KhandhareEntries.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasKhandhareEntries));
            NotifySubmissionStateChanged();
        };
        PersonalDebtors.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasPersonalDebtors));
            NotifySubmissionStateChanged();
        };

        _ = LoadLocalStationNameAsync();
        _ = RefreshQueueAsync();
        _ = LoadApprovedHistoryAsync();
        _ = LoadProductsAsync();
    }

    public void Dispose()
    {
        if (_featureToggleService != null && _featureChangedHandler != null)
        {
            _featureToggleService.FeatureConfigurationChanged -= _featureChangedHandler;
        }
        GC.SuppressFinalize(this);
    }

    private async Task LoadLocalStationNameAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var s = await context.Settings.FirstOrDefaultAsync();
            if (s != null)
            {
                _localStationName = !string.IsNullOrWhiteSpace(s.StationDisplayName)
                    ? s.StationDisplayName
                    : (!string.IsNullOrWhiteSpace(s.PumpStationName) ? s.PumpStationName : "");
                OnPropertyChanged(nameof(StationNameDisplay));
                OnPropertyChanged(nameof(HeaderStationNameDisplay));
                OnPropertyChanged(nameof(HasStationName));
                OnPropertyChanged(nameof(PersonalLedgerTitle));
            }
        }
        catch {}
    }

    [RelayCommand]
    public async Task RefreshQueueAsync()
    {
        long myGeneration = System.Threading.Interlocked.Increment(ref _refreshGeneration);
        await _refreshLock.WaitAsync();

        try
        {
            // Coalesce superseded queued refreshes while still ensuring at least the latest generation executes
            IsLoadingSubmissions = true;
            StatusMessage = "⏳ Querying Supabase for pending shift entries...";
            SelectedSubmission = null;

            var result = await _supabaseService.FetchPendingSubmissionsAsync();
            var syncConfig = _serviceProvider.GetService<SyncConfigService>();
            var syncSettings = syncConfig != null ? await syncConfig.GetSettingsAsync() : null;
            var activeStationId = syncSettings?.StationId;

            if (result.Success && result.Data != null)
            {
                using var scope = _serviceProvider.CreateScope();
                using var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

                var validatedAudits = syncConfig != null
                    ? await syncConfig.GetValidatedApprovedAuditsAsync()
                    : await SyncConfigService.ValidateApprovalAuditsAsync(context, _logger);

                var approvedSubIdSet = new HashSet<Guid>(validatedAudits.Keys);
                var evaluated = DsmPendingSubmissionEvaluator.Evaluate(result.Data, approvedSubIdSet, activeStationId);

                var dedupedList = new List<DsmPendingSubmission>();

                // Auto-heal only submissions backed by validated local DsmApprovalAudit + DsmEntry evidence
                foreach (var subId in evaluated.LocallyApprovedIds)
                {
                    if (!validatedAudits.TryGetValue(subId, out var audit)) continue;

                    var healBy = audit.ApprovedBy;
                    var healAt = audit.ApprovedAt;
                    _logger.Information("Submission {SubId} was already approved locally by {ApprovedBy} (DsmEntryId={EntryId}). Auto-healing Supabase status asynchronously.", subId, healBy, audit.DsmEntryId);
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var healRes = await _supabaseService.ApproveSubmissionAsync(subId, healBy, Guid.NewGuid().ToString("N"), healAt);
                            if (!healRes.Success)
                            {
                                _logger.Warning("Auto-heal failed for locally approved submission {SubId}: {Error}", subId, healRes.Error);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning(ex, "Exception while auto-healing approved submission {SubId} in Supabase", subId);
                        }
                    });
                }

                // Auto-resolve genuine duplicate pending submissions sharing the same operational key & DsmUserId
                if (evaluated.DuplicatePendingIds.Count > 0)
                {
                    var dupIds = evaluated.DuplicatePendingIds.ToList();
                    _ = Task.Run(async () =>
                    {
                        foreach (var dupId in dupIds)
                        {
                            try
                            {
                                var dupRes = await _supabaseService.ApproveSubmissionAsync(dupId, "System (Duplicate Auto-Heal)", Guid.NewGuid().ToString("N"));
                                if (!dupRes.Success)
                                {
                                    _logger.Warning("Duplicate auto-heal failed for {DupId}: {Error}", dupId, dupRes.Error);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning(ex, "Failed to auto-resolve duplicate pending submission {DupId}", dupId);
                            }
                        }
                    });
                }

                foreach (var evalItem in evaluated.ActionableSubmissions)
                {
                    dedupedList.Add(new DsmPendingSubmission
                    {
                        Id = evalItem.Id,
                        DsmName = evalItem.DsmName,
                        DsmUserId = evalItem.DsmUserId,
                        PumpId = evalItem.PumpId,
                        ShiftDate = evalItem.ShiftDate,
                        ShiftType = evalItem.ShiftType,
                        SubmittedAt = evalItem.SubmittedAt,
                        Notes = evalItem.Notes,
                        AttachmentUrl = evalItem.AttachmentUrl,
                        MetadataJson = evalItem.MetadataJson,
                        Status = evalItem.Status,
                        ProcessingStatus = evalItem.ProcessingStatus,
                        ClaimedByMachineId = evalItem.ClaimedByMachineId,
                        ClaimedAt = evalItem.ClaimedAt
                    });
                }

                // Durable Crash Recovery: Inspect recent cloud-approved submissions that have NO local audit record
                try
                {
                    var approvedResult = await _supabaseService.FetchApprovedSubmissionsAsync();
                    if (approvedResult.Success && approvedResult.Data != null)
                    {
                        var recentApproved = ((IEnumerable<dynamic>)approvedResult.Data).Take(50);
                        foreach (var appItem in recentApproved)
                        {
                            if (!DsmPendingSubmissionEvaluator.TryParseSubmission((object?)appItem, out var parsedApp) || parsedApp == null)
                                continue;

                            if (!string.IsNullOrWhiteSpace(activeStationId) &&
                                !string.IsNullOrWhiteSpace(parsedApp.StationId) &&
                                !string.Equals(parsedApp.StationId, activeStationId.Trim(), StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            if (!approvedSubIdSet.Contains(parsedApp.Id))
                            {
                                string? appKey = parsedApp.OperationalKey;
                                if (string.IsNullOrEmpty(appKey) || !evaluated.SeenOperationalKeys.Contains(appKey))
                                {
                                    var entryExists = await context.DsmEntries.AnyAsync(e =>
                                        e.Shift.ShiftDate.Date == parsedApp.ShiftDate.Date &&
                                        e.Shift.ShiftType == parsedApp.ShiftType &&
                                        e.PumpId == parsedApp.PumpId);
                                    if (!entryExists)
                                    {
                                        if (!string.IsNullOrEmpty(appKey))
                                            evaluated.SeenOperationalKeys.Add(appKey);

                                        dedupedList.Add(new DsmPendingSubmission
                                        {
                                            Id = parsedApp.Id,
                                            DsmName = parsedApp.DsmName,
                                            DsmUserId = parsedApp.DsmUserId,
                                            PumpId = parsedApp.PumpId,
                                            ShiftDate = parsedApp.ShiftDate,
                                            ShiftType = parsedApp.ShiftType,
                                            SubmittedAt = parsedApp.SubmittedAt,
                                            Notes = parsedApp.Notes + " [Recovery Required: Incomplete Approval]",
                                            AttachmentUrl = parsedApp.AttachmentUrl,
                                            MetadataJson = parsedApp.MetadataJson,
                                            ProcessingStatus = "RecoveryRequired",
                                            IsRecovery = true
                                        });
                                        _logger.Warning("Crash recovery detected uncommitted cloud-approved submission {SubId}. Added to pending queue.", parsedApp.Id);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to inspect recent approved submissions for crash recovery check");
                }

                PendingSubmissions.Clear();
                foreach (var sub in dedupedList)
                {
                    PendingSubmissions.Add(sub);
                }

                StatusMessage = PendingSubmissions.Count > 0
                    ? $"Found {PendingSubmissions.Count} pending submissions."
                    : "No pending submissions found.";

                _pollingService?.SyncCountFromQueue(PendingSubmissions.Count, activeStationId);
            }
            else
            {
                PendingSubmissions.Clear();
                StatusMessage = $"❌ Failed to load: {result.Error}";
                _pollingService?.MarkRefreshFailed(result.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh DSM approval queue");
            PendingSubmissions.Clear();
            StatusMessage = $"❌ Error: {ex.Message}";
            _pollingService?.MarkRefreshFailed(ex.Message);
        }
        finally
        {
            IsLoadingSubmissions = false;
            _refreshLock.Release();
        }
    }

    partial void OnSelectedSubmissionChanged(DsmPendingSubmission? value)
    {
        if (value != null)
        {
            SelectedApprovedSubmission = null;
            IsReadOnlyMode = false;
            _ = LoadSubmissionDetailsAsync(value);
        }
        else
        {
            if (SelectedApprovedSubmission == null)
            {
                NozzleReadings.Clear();
                OilDefSales.Clear();
                ManualTidTransactions.Clear();
                IsEditing = false;
                IsReadOnlyMode = false;
                TimelineLogs.Clear();
            }
        }
        NotifySubmissionStateChanged();
    }

    private async Task<List<int>> GetConnectedPumpsForPrimaryAsync(int pumpId)
    {
        try
        {
            var stationConfig = _serviceProvider?.GetService<FuelPro.Core.Services.IStationConfigurationService>();
            if (stationConfig != null)
            {
                var config = await stationConfig.GetPumpConnectionConfigurationAsync();
                if (config != null && config.IsEnabled)
                {
                    var group = config.GetGroupForPump(pumpId);
                    if (group != null)
                    {
                        return group.GetConnectedPumps(pumpId);
                    }
                }
            }
        }
        catch {}
        return new List<int>();
    }

    private async Task LoadSubmissionDetailsAsync(DsmPendingSubmission submission)
    {
        IsLoadingDetails = true;
        _isLoadingSubmission = true;
        StatusMessage = $"⏳ Loading details for {submission.DsmName}...";
        IsEditing = true;
        MetadataParseError = null;

        try
        {
            await EnsureAvailableCollectionTypesAsync();
            var allTypes = _availableCollectionTypes ?? GetCollectionTypesSnapshot();

            // 1. Fetch readings from Supabase
            var readingsResult = await _supabaseService.FetchSubmissionReadingsAsync(submission.Id);
            if (!readingsResult.Success)
            {
                MetadataParseError = $"Failed to fetch nozzle readings from server: {readingsResult.Error}";
            }
            NozzleReadings.Clear();
            SubmissionTestingEntries.Clear();
            OilDefSales.Clear();

            // 2. Query continuity closings from local SQLite
            var prevDetailsResult = await _nozzleRepo.GetPreviousShiftClosingDetailsAsync(submission.ShiftDate, submission.ShiftType, submission.PumpId);
            var prevDetails = prevDetailsResult.Success ? prevDetailsResult.Data! : new Dictionary<int, PredecessorClosingDetail>();

            var effectiveConnectedPumps = new HashSet<int>();
            if (submission.ConnectedPumpIds != null && submission.ConnectedPumpIds.Count > 0)
            {
                foreach (var id in submission.ConnectedPumpIds) if (id > 0 && id != submission.PumpId) effectiveConnectedPumps.Add(id);
            }
            if (submission.ConnectedPumpId.HasValue && submission.ConnectedPumpId.Value > 0 && submission.ConnectedPumpId.Value != submission.PumpId)
            {
                effectiveConnectedPumps.Add(submission.ConnectedPumpId.Value);
            }

            // Fallback to configured connected pumps if not present in submission
            if (effectiveConnectedPumps.Count == 0)
            {
                var cfgConnected = await GetConnectedPumpsForPrimaryAsync(submission.PumpId);
                foreach (var cp in cfgConnected) if (cp > 0 && cp != submission.PumpId) effectiveConnectedPumps.Add(cp);
            }

            foreach (var connPumpId in effectiveConnectedPumps)
            {
                var connPrevResult = await _nozzleRepo.GetPreviousShiftClosingDetailsAsync(submission.ShiftDate, submission.ShiftType, connPumpId);
                if (connPrevResult.Success && connPrevResult.Data != null)
                {
                    foreach (var kvp in connPrevResult.Data)
                    {
                        prevDetails[kvp.Key] = kvp.Value;
                    }
                }
            }

            if (readingsResult.Success && readingsResult.Data != null)
            {
                var sortedReadings = readingsResult.Data
                    .OrderBy(r => GetDynamicNozzleId(r))
                    .ToList();

                foreach (var r in sortedReadings)
                {
                    int nozzleId = GetDynamicNozzleId(r);
                    double opening = Convert.ToDouble((object?)r.OpeningReading);
                    double closing = Convert.ToDouble((object?)r.ClosingReading);
                    double rate = Convert.ToDouble((object?)r.Rate);
                    
                    PredecessorClosingDetail? predDetail = prevDetails.TryGetValue(nozzleId, out var pd) ? pd : null;
                    if (predDetail == null)
                    {
                        try
                        {
                            if (r.PumpId != null && (int)r.PumpId > 0 && (int)r.PumpId != submission.PumpId)
                            {
                                int rPumpId = (int)r.PumpId;
                                var directPrevResult = await _nozzleRepo.GetPreviousShiftClosingDetailsAsync(submission.ShiftDate, submission.ShiftType, rPumpId);
                                if (directPrevResult.Success && directPrevResult.Data != null && directPrevResult.Data.TryGetValue(nozzleId, out var dpd))
                                {
                                    predDetail = dpd;
                                    prevDetails[nozzleId] = dpd;
                                }
                            }
                        }
                        catch {}
                    }
                    double? expectedOpening = predDetail?.ClosingReading;

                    int nozzlePumpId = predDetail?.PumpId ?? (int)submission.PumpId;
                    try
                    {
                        if (r.PumpId != null) nozzlePumpId = (int)r.PumpId;
                    }
                    catch {}

                    string submittedFuelType = GetDynamicFuelType(r) ?? "";
                    string resolvedFuelType = !string.IsNullOrWhiteSpace(submittedFuelType)
                        ? submittedFuelType
                        : GetFuelTypeName(nozzlePumpId, nozzleId, submission.ShiftDate);

                    var row = new DsmNozzleRow
                    {
                        NozzleId = nozzleId,
                        FuelType = resolvedFuelType,
                        OpeningReading = opening,
                        ClosingReading = closing,
                        Rate = rate,
                        SubmittedRate = rate,
                        ExpectedOpening = expectedOpening,
                        PredecessorDate = predDetail?.ShiftDate,
                        PredecessorShift = predDetail?.ShiftType,
                        HopsBack = predDetail?.HopsBack ?? 0,
                        PumpId = nozzlePumpId,
                        OnRowChanged = RecalculateTotals
                    };
                    
                    row.Recalculate();
                    NozzleReadings.Add(row);
                }
            }

            // 2.5 Validate nozzle rates against historical ground truth
            await ValidateNozzleRatesAsync(submission);

            // 3. Fetch collections from Supabase
            var collResult = await _supabaseService.FetchSubmissionCollectionAsync(submission.Id);
            dynamic? coll = null;
            if (collResult.Success && collResult.Data != null)
            {
                coll = collResult.Data;
                CashAmount = coll.Cash;
                UpiAmount = coll.UPI;
                CardAmount = coll.Card;
                PetroCardAmount = coll.PetroCard ?? 0.0;
                CashDepositAmount = coll.CashDeposit ?? 0.0;
                OthersAmount = coll.Others ?? 0.0;
                CreditAmount = coll.Credit;
                ExpenseAmount = coll.Expense;
                ExpenseNotes = coll.ExpenseNotes ?? "";
                ShortAmount = coll.Short;
                ExcessAmount = coll.Excess;
            }
            else
            {
                CashAmount = UpiAmount = CardAmount = PetroCardAmount = CashDepositAmount = OthersAmount = CreditAmount = ExpenseAmount = ShortAmount = ExcessAmount = 0;
                ExpenseNotes = "";
            }

            // Reset Cash1, Cash2, Cash3 denominations, DebtorRepayments and Collection Reduction
            Cash1.Denom500 = Cash1.Denom200 = Cash1.Denom100 = Cash1.Denom50 = Cash1.Denom20 = Cash1.Denom10 = Cash1.Coins = null;
            Cash2.Denom500 = Cash2.Denom200 = Cash2.Denom100 = Cash2.Denom50 = Cash2.Denom20 = Cash2.Denom10 = Cash2.Coins = null;
            Cash3.Denom500 = Cash3.Denom200 = Cash3.Denom100 = Cash3.Denom50 = Cash3.Denom20 = Cash3.Denom10 = Cash3.Coins = null;
            DebtorRepayments.Clear();
            CollectionReductionAmount = 0.0;
            CollectionReductionRemark = null;
            PhonePeMorning = PhonePeDay = PhonePeNight = 0;
            CreditCardMorning = CreditCardDay = CreditCardNight = 0;
            PetroCardMorning = PetroCardDay = PetroCardNight = 0;
            HasSplitValues = false;

            // 4. Parse editable collections (Debtor Entries and Card Swipe Details) from MetadataJson
            DebtorEntries.Clear();
            CardSwipeDetails.Clear();
            ManualTidTransactions.Clear();
            _dynamicManualAmounts.Clear();
            _dynamicManualTids.Clear();
            _dynamicManualBatches.Clear();
            _dynamicManualSlots.Clear();

            if (!string.IsNullOrEmpty(submission.MetadataJson))
            {
                Newtonsoft.Json.Linq.JObject? metaObj = ParseMetadataObject(submission.MetadataJson);
                if (metaObj == null)
                {
                    try
                    {
                        metaObj = Newtonsoft.Json.Linq.JObject.Parse(submission.MetadataJson);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Failed to parse MetadataJson as JObject for submission {SubId}", submission.Id);
                    }
                }

                try
                {
                    string cleanMetaJson = metaObj != null ? metaObj.ToString(Newtonsoft.Json.Formatting.None) : CleanAndUnescapeJson(submission.MetadataJson);
                    dynamic? metadata = metaObj != null ? (dynamic)metaObj : JsonConvert.DeserializeObject<dynamic>(cleanMetaJson);
                    if (metaObj != null || metadata != null)
                    {
                        // 1. Cash 1 Denominations (only populate non-zero breakdowns; preserve null when scalar-only)
                        try
                        {
                            var cash1Meta = metaObj?["cash1Denominations"];
                            if (cash1Meta != null)
                            {
                                int d500 = (int?)(cash1Meta["denom500"] ?? cash1Meta["c500"]) ?? 0;
                                int d200 = (int?)(cash1Meta["denom200"] ?? cash1Meta["c200"]) ?? 0;
                                int d100 = (int?)(cash1Meta["denom100"] ?? cash1Meta["c100"]) ?? 0;
                                int d50 = (int?)(cash1Meta["denom50"] ?? cash1Meta["c50"]) ?? 0;
                                int d20 = (int?)(cash1Meta["denom20"] ?? cash1Meta["c20"]) ?? 0;
                                int d10 = (int?)(cash1Meta["denom10"] ?? cash1Meta["c10"]) ?? 0;
                                int coinsVal = (int?)(cash1Meta["coins"]) ?? 0;

                                if (d500 > 0 || d200 > 0 || d100 > 0 || d50 > 0 || d20 > 0 || d10 > 0 || coinsVal > 0)
                                {
                                    Cash1.Denom500 = d500;
                                    Cash1.Denom200 = d200;
                                    Cash1.Denom100 = d100;
                                    Cash1.Denom50 = d50 > 0 ? d50 : null;
                                    Cash1.Denom20 = d20 > 0 ? d20 : null;
                                    Cash1.Denom10 = d10 > 0 ? d10 : null;
                                    Cash1.Coins = coinsVal > 0 ? coinsVal : null;
                                }
                            }
                        }
                        catch (Exception ex) { _logger.Warning(ex, "Error parsing Cash 1 denominations"); }

                        // 2. Cash 2 Denominations
                        try
                        {
                            var cash2Meta = metaObj?["cashDenominations"] ?? metaObj?["cash2Denominations"];
                            if (cash2Meta != null)
                            {
                                Cash2.Denom500 = (int?)(cash2Meta["denom500"] ?? cash2Meta["c500"]);
                                Cash2.Denom200 = (int?)(cash2Meta["denom200"] ?? cash2Meta["c200"]);
                                Cash2.Denom100 = (int?)(cash2Meta["denom100"] ?? cash2Meta["c100"]);
                                Cash2.Denom50 = (int?)(cash2Meta["denom50"] ?? cash2Meta["c50"]);
                                Cash2.Denom20 = (int?)(cash2Meta["denom20"] ?? cash2Meta["c20"]);
                                Cash2.Denom10 = (int?)(cash2Meta["denom10"] ?? cash2Meta["c10"]);
                                Cash2.Coins = (int?)(cash2Meta["coins"]);
                            }
                        }
                        catch (Exception ex) { _logger.Warning(ex, "Error parsing Cash 2 denominations"); }

                        // 3. Cash 3 Denominations
                        try
                        {
                            var cash3Meta = metaObj?["cash3Denominations"] ?? metaObj?["cash3"];
                            if (cash3Meta != null)
                            {
                                Cash3.Denom500 = (int?)(cash3Meta["denom500"] ?? cash3Meta["c500"]);
                                Cash3.Denom200 = (int?)(cash3Meta["denom200"] ?? cash3Meta["c200"]);
                                Cash3.Denom100 = (int?)(cash3Meta["denom100"] ?? cash3Meta["c100"]);
                                Cash3.Denom50 = (int?)(cash3Meta["denom50"] ?? cash3Meta["c50"]);
                                Cash3.Denom20 = (int?)(cash3Meta["denom20"] ?? cash3Meta["c20"]);
                                Cash3.Denom10 = (int?)(cash3Meta["denom10"] ?? cash3Meta["c10"]);
                                Cash3.Coins = (int?)(cash3Meta["coins"]);
                            }
                            else if (metaObj?["cash3Amount"] != null)
                            {
                                Cash3.Coins = (int?)metaObj["cash3Amount"];
                            }
                        }
                        catch (Exception ex) { _logger.Warning(ex, "Error parsing Cash 3 denominations"); }

                        // 4. Collection Reduction
                        try
                        {
                            var redVal = metaObj?["collectionReduction"] ?? metaObj?["collection_reduction"] ?? metaObj?["Collections"]?["collectionReduction"];
                            if (redVal != null) CollectionReductionAmount = (double)redVal;
                            var redRem = metaObj?["collectionReductionRemark"] ?? metaObj?["collection_reduction_remark"] ?? metaObj?["Collections"]?["collectionReductionRemark"];
                            if (redRem != null) CollectionReductionRemark = (string?)redRem;
                        }
                        catch (Exception ex) { _logger.Warning(ex, "Error parsing collection reduction"); }

                        if (CollectionReductionAmount == 0 && coll != null)
                        {
                            try
                            {
                                if (coll is IDictionary<string, object> cDict)
                                {
                                    if (cDict.TryGetValue("CollectionReduction", out var cr) || cDict.TryGetValue("collection_reduction", out cr) || cDict.TryGetValue("collectionReduction", out cr))
                                    {
                                        if (cr != null) CollectionReductionAmount = Convert.ToDouble(cr);
                                    }
                                    if (cDict.TryGetValue("CollectionReductionRemark", out var crr) || cDict.TryGetValue("collection_reduction_remark", out crr) || cDict.TryGetValue("collectionReductionRemark", out crr))
                                    {
                                        if (crr != null) CollectionReductionRemark = Convert.ToString(crr);
                                    }
                                }
                                else if (coll is Newtonsoft.Json.Linq.JObject cJob)
                                {
                                    var cr = cJob["CollectionReduction"] ?? cJob["collection_reduction"] ?? cJob["collectionReduction"];
                                    if (cr != null) CollectionReductionAmount = (double)cr;
                                    var crr = cJob["CollectionReductionRemark"] ?? cJob["collection_reduction_remark"] ?? cJob["collectionReductionRemark"];
                                    if (crr != null) CollectionReductionRemark = (string?)crr;
                                }
                            }
                            catch {}
                        }

                        IsCash3Enabled = (_featureToggleService?.IsFeatureEnabled("Operations_Cash3", true) ?? true) || Cash3.TotalAmount > 0;

                        // Safely extract scalar amounts from coll without dynamic binder crashes
                        double? collCashDeposit = null;
                        double? collCash = null;
                        if (coll != null)
                        {
                            try
                            {
                                if (coll is Newtonsoft.Json.Linq.JObject collJob)
                                {
                                    var cd = collJob["CashDeposit"] ?? collJob["cash_deposit"];
                                    if (cd != null) collCashDeposit = (double)cd;
                                    var c = collJob["Cash"] ?? collJob["cash"];
                                    if (c != null) collCash = (double)c;
                                }
                                else if (coll is IDictionary<string, object> cDict)
                                {
                                    if (cDict.TryGetValue("CashDeposit", out var cd) || cDict.TryGetValue("cash_deposit", out cd))
                                        if (cd != null) collCashDeposit = Convert.ToDouble(cd);
                                    if (cDict.TryGetValue("Cash", out var c) || cDict.TryGetValue("cash", out c))
                                        if (c != null) collCash = Convert.ToDouble(c);
                                }
                            }
                            catch {}
                        }

                        if (Cash1.TotalAmount > 0)
                        {
                            CashDepositAmount = Cash1.TotalAmount;
                        }
                        else if (collCashDeposit.HasValue)
                        {
                            CashDepositAmount = collCashDeposit.Value;
                        }

                        if (Cash1.TotalAmount == 0 && CashDepositAmount > 0)
                        {
                            Cash1.TotalAmount = CashDepositAmount;
                        }
                        OnPropertyChanged(nameof(ShowCash1DenominationFields));
                        OnPropertyChanged(nameof(ShowCash1ScalarField));

                        if (Cash2.TotalAmount > 0)
                        {
                            CashAmount = Cash2.TotalAmount;
                        }
                        else if (collCash.HasValue)
                        {
                            CashAmount = collCash.Value;
                        }

                        if (Cash2.TotalAmount == 0 && CashAmount > 0)
                        {
                            Cash2.TotalAmount = CashAmount;
                        }

                        // 5. Debtor Entries - safe independent parsing supporting all schema variants
                        try
                        {
                            Newtonsoft.Json.Linq.JToken? debtorToken = metaObj?["debtorEntries"] 
                                ?? metaObj?["DebtorEntries"] 
                                ?? metaObj?["debitEntries"] 
                                ?? metaObj?["DebitEntries"] 
                                ?? metaObj?["debtors"] 
                                ?? metaObj?["Debtors"] 
                                ?? metaObj?["creditEntries"] 
                                ?? metaObj?["CreditEntries"] 
                                ?? metaObj?["creditSales"] 
                                ?? metaObj?["CreditSales"] 
                                ?? metaObj?["debtor_entries"];

                            if (debtorToken != null)
                            {
                                foreach (var dbEntry in debtorToken)
                                {
                                    try
                                    {
                                        double dAmt = 0.0;
                                        var amtToken = dbEntry["amount"] ?? dbEntry["Amount"];
                                        if (amtToken != null)
                                        {
                                            if (amtToken.Type == Newtonsoft.Json.Linq.JTokenType.Float || amtToken.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                                            {
                                                dAmt = (double)amtToken;
                                            }
                                            else
                                            {
                                                double.TryParse(amtToken.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dAmt);
                                            }
                                        }

                                        string dName = (string?)(dbEntry["debtorName"] ?? dbEntry["debtor_name"] ?? dbEntry["DebtorName"] ?? dbEntry["name"] ?? dbEntry["Name"] ?? "") ?? "";
                                        if (dAmt > 0 || !string.IsNullOrWhiteSpace(dName))
                                        {
                                            DebtorEntries.Add(new DsmDebitRow
                                            {
                                                DebtorName = dName,
                                                Amount = dAmt,
                                                VehicleNumber = (string?)(dbEntry["vehicleNumber"] ?? dbEntry["vehicle_number"] ?? dbEntry["VehicleNumber"] ?? dbEntry["vehicleNo"] ?? dbEntry["vehicle"] ?? ""),
                                                SlipNumber = (string?)(dbEntry["slipNumber"] ?? dbEntry["slip_number"] ?? dbEntry["SlipNumber"] ?? dbEntry["slipNo"] ?? dbEntry["slip"] ?? ""),
                                                EntryTime = (string?)(dbEntry["time"] ?? dbEntry["Time"] ?? dbEntry["entryTime"] ?? dbEntry["EntryTime"] ?? ""),
                                                Remarks = (string?)(dbEntry["remarks"] ?? dbEntry["Remarks"] ?? dbEntry["notes"] ?? dbEntry["Notes"] ?? ""),
                                                OnRowChanged = RecalculateTotals
                                            });
                                        }
                                    }
                                    catch (Exception entryEx)
                                    {
                                        _logger.Warning(entryEx, "Skipping malformed individual debtor entry in submission {SubId}", submission.Id);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning(ex, "Failed to parse DebtorEntries from submission {SubId}", submission.Id);
                        }

                        // Debtor Repayments - safe parsing supporting all schema variants
                        try
                        {
                            Newtonsoft.Json.Linq.JToken? repToken = metaObj?["debtorRepayments"] 
                                ?? metaObj?["DebtorRepayments"] 
                                ?? metaObj?["repayments"] 
                                ?? metaObj?["Repayments"] 
                                ?? metaObj?["settlements"] 
                                ?? metaObj?["Settlements"];

                            if (repToken != null)
                            {
                                List<CreditorRepayment>? existingSubReps = null;
                                try
                                {
                                    using var db = _serviceProvider.GetRequiredService<FuelPro.Data.FuelProDbContext>();
                                    existingSubReps = await db.CreditorRepayments
                                        .Where(r => r.ChequeNo != null && r.ChequeNo.Contains($"[Sub:{submission.Id:N}"))
                                        .ToListAsync();
                                }
                                catch { }

                                var usedDbIds = new HashSet<int>();
                                int repIndex = 0;
                                foreach (var dr in repToken)
                                {
                                    try
                                    {
                                        string dName = (string?)(dr["debtorName"] ?? dr["DebtorName"] ?? dr["creditorName"] ?? dr["CreditorName"] ?? dr["customerName"] ?? dr["name"] ?? "") ?? "";
                                        double dAmt = 0.0;
                                        var rAmtToken = dr["amount"] ?? dr["Amount"];
                                        if (rAmtToken != null)
                                        {
                                            if (rAmtToken.Type == Newtonsoft.Json.Linq.JTokenType.Float || rAmtToken.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                                            {
                                                dAmt = (double)rAmtToken;
                                            }
                                            else
                                            {
                                                double.TryParse(rAmtToken.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dAmt);
                                            }
                                        }
                                        string dMode = (string?)(dr["paymentMode"] ?? dr["PaymentMode"] ?? dr["collectionType"] ?? dr["mode"] ?? "Cash") ?? "Cash";
                                        string dNotes = (string?)(dr["notes"] ?? dr["Notes"] ?? dr["remarks"] ?? dr["Remarks"] ?? "") ?? "";
                                        if (dAmt > 0 && !string.IsNullOrWhiteSpace(dName))
                                        {
                                            int repId = 0;
                                            if (existingSubReps != null && existingSubReps.Count > 0)
                                            {
                                                var match = existingSubReps.FirstOrDefault(r => 
                                                    !usedDbIds.Contains(r.CreditorRepaymentId)
                                                    && RepaymentIdentityHelper.MatchesSubmission(r.ChequeNo, submission.Id, repIndex))
                                                    ?? existingSubReps.FirstOrDefault(r => 
                                                        !usedDbIds.Contains(r.CreditorRepaymentId)
                                                        && string.Equals(r.CreditorName, dName.Trim(), StringComparison.OrdinalIgnoreCase)
                                                        && Math.Abs(r.Amount - dAmt) < 0.01);
                                                if (match != null)
                                                {
                                                    repId = match.CreditorRepaymentId;
                                                    usedDbIds.Add(repId);
                                                }
                                            }

                                            DebtorRepayments.Add(new DsmDebtorRepaymentRow
                                            {
                                                CreditorRepaymentId = repId,
                                                DebtorName = dName,
                                                Amount = dAmt,
                                                PaymentMode = dMode,
                                                Notes = dNotes,
                                                OnRowChanged = RecalculateTotals
                                            });
                                            repIndex++;
                                        }
                                    }
                                    catch (Exception repItemEx)
                                    {
                                        _logger.Warning(repItemEx, "Skipping malformed debtor repayment entry in submission {SubId}", submission.Id);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning(ex, "Failed to parse DebtorRepayments from submission {SubId}", submission.Id);
                        }
                        OnPropertyChanged(nameof(HasDebtorRepayments));

                        // Parse dynamicCollectionValues for manual dynamic amounts
                        var dcvCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (metadata.dynamicCollectionValues != null)
                        {
                            try
                            {
                                var dcvJson = JsonConvert.SerializeObject(metadata.dynamicCollectionValues);
                                var dcvDict = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(dcvJson);
                                if (dcvDict != null)
                                {
                                    foreach (var kvp in dcvDict)
                                    {
                                        string normCode = PaymentCollection.NormalizeCollectionTypeCode(kvp.Key);
                                        double amt = Convert.ToDouble((object?)(kvp.Value?.amount ?? 0.0));
                                        string tid = Convert.ToString((object?)(kvp.Value?.tid ?? "")) ?? "";
                                        string batch = Convert.ToString((object?)(kvp.Value?.batch ?? "")) ?? "";
                                        string slot = Convert.ToString((object?)(kvp.Value?.slot ?? "")) ?? "";
                                        if (amt > 0)
                                        {
                                            dcvCodes.Add(normCode);
                                            _dynamicManualAmounts[normCode] = amt;
                                            _dynamicManualTids[normCode] = tid;
                                            _dynamicManualBatches[normCode] = batch;
                                            _dynamicManualSlots[normCode] = slot;
                                        }
                                    }
                                }
                            }
                            catch {}
                        }

                        if (metadata?.manualDynamicAmounts != null)
                        {
                            try
                            {
                                var mdaJson = JsonConvert.SerializeObject(metadata.manualDynamicAmounts);
                                var mdaDict = JsonConvert.DeserializeObject<Dictionary<string, double>>(mdaJson);
                                if (mdaDict != null)
                                {
                                    foreach (var kvp in mdaDict)
                                    {
                                        string normCode = PaymentCollection.NormalizeCollectionTypeCode(kvp.Key);
                                        if (kvp.Value > 0)
                                        {
                                            _dynamicManualAmounts[normCode] = kvp.Value;
                                        }
                                    }
                                }
                            }
                            catch {}
                        }

                        int formatVersion = 0;
                        if (metadata.formatVersion != null)
                        {
                            int.TryParse(Convert.ToString((object?)metadata.formatVersion), out formatVersion);
                        }

                        if (metadata.machineTransactions != null)
                        {
                            foreach (var mt in metadata.machineTransactions)
                            {
                                string mtCode = Convert.ToString((object?)(mt.collectionTypeCode ?? mt.code ?? mt.mode ?? mt.displayName ?? "")) ?? "";
                                if (string.IsNullOrWhiteSpace(mtCode)) continue;
                                double mtAmt = Convert.ToDouble((object?)(mt.amount ?? 0.0));
                                string? mtOtp = Convert.ToString((object?)(mt.otp ?? mt.Otp ?? "")) ?? "";
                                string? mtTid = Convert.ToString((object?)(mt.tid ?? mt.Tid ?? "")) ?? "";
                                string? mtBatch = Convert.ToString((object?)(mt.batch ?? mt.Batch ?? "")) ?? "";
                                string? mtSlot = Convert.ToString((object?)(mt.slot ?? mt.Slot ?? "")) ?? "";
                                string? mtTxId = Convert.ToString((object?)(mt.transactionId ?? mt.TransactionId ?? mt.id ?? mt.Id ?? "")) ?? "";
                                if (mtAmt <= 0 || string.IsNullOrWhiteSpace(mtOtp)) continue;

                                string normMtCode = PaymentCollection.NormalizeCollectionTypeCode(mtCode);

                                if (!CardSwipeDetails.Any(c => 
                                    string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normMtCode, StringComparison.OrdinalIgnoreCase)
                                    && Math.Abs(c.Amount - mtAmt) < 0.01 
                                    && string.Equals((c.Otp ?? "").Trim(), (mtOtp ?? "").Trim(), StringComparison.OrdinalIgnoreCase)))
                                {
                                    CardSwipeDetails.Add(new DsmCardSwipeRow
                                    {
                                        Mode = !string.IsNullOrWhiteSpace(normMtCode) ? normMtCode : mtCode,
                                        Amount = mtAmt,
                                        Tid = mtTid ?? "",
                                        Batch = mtBatch ?? "",
                                        Otp = mtOtp ?? "",
                                        TransactionId = mtTxId,
                                        Slot = mtSlot,
                                        OnRowChanged = RecalculateTotals
                                    });
                                }
                            }
                        }

                        if (metadata.manualTidTransactions != null)
                        {
                            foreach (var mt in metadata.manualTidTransactions)
                            {
                                string mtCode = Convert.ToString((object?)(mt.collectionTypeCode ?? mt.code ?? mt.mode ?? mt.displayName ?? "")) ?? "";
                                if (string.IsNullOrWhiteSpace(mtCode)) continue;
                                double mtAmt = Convert.ToDouble((object?)(mt.amount ?? 0.0));
                                string? mtTid = Convert.ToString((object?)(mt.tid ?? mt.Tid ?? "")) ?? "";
                                string? mtBatch = Convert.ToString((object?)(mt.batch ?? mt.Batch ?? "")) ?? "";
                                string? mtSlot = Convert.ToString((object?)(mt.slot ?? mt.Slot ?? "General")) ?? "General";
                                string? mtTxId = Convert.ToString((object?)(mt.transactionId ?? mt.TransactionId ?? mt.id ?? mt.Id ?? "")) ?? "";
                                if (mtAmt <= 0) continue;

                                string normMtCode = PaymentCollection.NormalizeCollectionTypeCode(mtCode);
                                var master = allTypes.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), normMtCode, StringComparison.OrdinalIgnoreCase));
                                bool isManualOnly = master != null && master.IsManualOnly;
                                bool isManualScalar = (!string.IsNullOrWhiteSpace(mtTxId) && mtTxId.StartsWith("manual-scalar-", StringComparison.OrdinalIgnoreCase))
                                    || (string.IsNullOrWhiteSpace(mtTid) && string.IsNullOrWhiteSpace(mtBatch));

                                if (isManualScalar)
                                {
                                    if (!_dynamicManualAmounts.ContainsKey(normMtCode))
                                    {
                                        _dynamicManualAmounts[normMtCode] = mtAmt;
                                        _dynamicManualTids[normMtCode] = mtTid;
                                        _dynamicManualBatches[normMtCode] = mtBatch;
                                        _dynamicManualSlots[normMtCode] = mtSlot;
                                    }
                                }
                                else
                                {
                                    string dispMode = ResolveCollectionTypeDisplayName(mtCode);
                                    // Cross-section check: If already represented in OTP CardSwipeDetails, do not duplicate in ManualTidTransactions
                                    bool inOtp = CardSwipeDetails.Any(c =>
                                        (!string.IsNullOrWhiteSpace(c.TransactionId) && !string.IsNullOrWhiteSpace(mtTxId) && string.Equals(c.TransactionId, mtTxId, StringComparison.OrdinalIgnoreCase))
                                        || (!string.IsNullOrWhiteSpace(c.Otp) && string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normMtCode, StringComparison.OrdinalIgnoreCase) && Math.Abs(c.Amount - mtAmt) < 0.001
                                            && !string.IsNullOrWhiteSpace(c.Tid) && string.Equals(c.Tid.Trim(), (mtTid ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                                            && !string.IsNullOrWhiteSpace(c.Batch) && string.Equals(c.Batch.Trim(), (mtBatch ?? "").Trim(), StringComparison.OrdinalIgnoreCase)));
                                    if (inOtp) continue;

                                    if (!ManualTidTransactions.Any(m =>
                                        (!string.IsNullOrWhiteSpace(mtTxId) && string.Equals(m.TransactionId, mtTxId, StringComparison.OrdinalIgnoreCase))
                                        || (string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Mode), normMtCode, StringComparison.OrdinalIgnoreCase)
                                            && Math.Abs(m.Amount - mtAmt) < 0.001
                                            && string.Equals(m.Tid, mtTid, StringComparison.OrdinalIgnoreCase)
                                            && string.Equals(m.Batch, mtBatch, StringComparison.OrdinalIgnoreCase))))
                                    {
                                        ManualTidTransactions.Add(new DsmCardSwipeRow
                                        {
                                            Mode = dispMode,
                                            Amount = mtAmt,
                                            Tid = mtTid,
                                            Batch = mtBatch,
                                            Slot = mtSlot,
                                            TransactionId = !string.IsNullOrWhiteSpace(mtTxId) ? mtTxId : Guid.NewGuid().ToString("D"),
                                            OnRowChanged = RecalculateTotals
                                        });
                                    }
                                }
                            }
                        }

                        if (formatVersion >= 3)
                        {
                            // In formatVersion >= 3:
                            // 1. metadata.cardSwipeDetails strictly owns fixed standard card splits (CREDIT_CARD, PETROCARD, PHONEPE)
                            if (metadata.cardSwipeDetails != null)
                            {
                                foreach (var swipe in metadata.cardSwipeDetails)
                                {
                                    string mode = Convert.ToString((object?)(swipe.mode ?? swipe.collectionTypeCode ?? swipe.code ?? "")) ?? "";
                                    double amt = Convert.ToDouble((object?)(swipe.amount ?? 0.0));
                                    string tid = Convert.ToString((object?)(swipe.tid ?? "")) ?? "";
                                    string batch = Convert.ToString((object?)(swipe.batch ?? "")) ?? "";
                                    string otp = Convert.ToString((object?)(swipe.otp ?? swipe.Otp ?? "")) ?? "";
                                    string? txId = Convert.ToString((object?)(swipe.transactionId ?? swipe.TransactionId ?? swipe.id ?? swipe.Id ?? "")) ?? "";
                                    string? slot = Convert.ToString((object?)(swipe.slot ?? swipe.Slot ?? "")) ?? "";
                                    if (amt <= 0) continue;

                                    string normMode = PaymentCollection.NormalizeCollectionTypeCode(mode);
                                    if (!string.IsNullOrWhiteSpace(otp))
                                    {
                                        if (!CardSwipeDetails.Any(c => 
                                            string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normMode, StringComparison.OrdinalIgnoreCase)
                                            && Math.Abs(c.Amount - amt) < 0.01
                                            && string.Equals((c.Otp ?? "").Trim(), (otp ?? "").Trim(), StringComparison.OrdinalIgnoreCase)))
                                        {
                                            CardSwipeDetails.Add(new DsmCardSwipeRow
                                            {
                                                Mode = !string.IsNullOrWhiteSpace(normMode) ? normMode : mode,
                                                Amount = amt,
                                                Tid = tid,
                                                Batch = batch,
                                                Otp = otp,
                                                TransactionId = txId,
                                                Slot = slot,
                                                OnRowChanged = RecalculateTotals
                                            });
                                        }
                                    }
                                    else if (PaymentCollection.IsStandardCollectionCode(mode))
                                    {
                                        if (normMode == "CREDIT_CARD" && CardAmount <= 0) CardAmount += amt;
                                        else if (normMode == "PETROCARD" && PetroCardAmount <= 0) PetroCardAmount += amt;
                                        else if (normMode == "PHONEPE" && UpiAmount <= 0) UpiAmount += amt;
                                    }
                                }
                            }

                            // 2. metadata.dynamicItems strictly owns all dynamic manual items (including itemized TID rows)
                            if (metadata.dynamicItems != null)
                            {
                                foreach (var d in metadata.dynamicItems)
                                {
                                    string dCode = (string)(d.collectionTypeCode ?? d.code ?? d.mode ?? "");
                                    if (string.IsNullOrWhiteSpace(dCode)) continue;
                                    double dAmt = Convert.ToDouble((object?)(d.amount ?? 0.0));
                                    string? dOtp = (string?)(d.otp ?? d.Otp ?? "");
                                    string? dTid = (string?)(d.tid ?? d.Tid ?? "");
                                    string? dBatch = (string?)(d.batch ?? d.Batch ?? "");
                                    string? dSlot = (string?)(d.slot ?? d.Slot ?? "General");
                                    string? dTxId = (string?)(d.transactionId ?? d.TransactionId ?? "");
                                    if (dAmt <= 0) continue;

                                    string normDCode = PaymentCollection.NormalizeCollectionTypeCode(dCode);
                                    if (!string.IsNullOrWhiteSpace(dOtp))
                                    {
                                        if (!CardSwipeDetails.Any(c =>
                                            string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normDCode, StringComparison.OrdinalIgnoreCase)
                                            && Math.Abs(c.Amount - dAmt) < 0.01
                                            && string.Equals((c.Otp ?? "").Trim(), (dOtp ?? "").Trim(), StringComparison.OrdinalIgnoreCase)))
                                        {
                                            CardSwipeDetails.Add(new DsmCardSwipeRow
                                            {
                                                Mode = !string.IsNullOrWhiteSpace(normDCode) ? normDCode : dCode,
                                                Amount = dAmt,
                                                Tid = dTid ?? "",
                                                Batch = dBatch ?? "",
                                                Otp = dOtp ?? "",
                                                TransactionId = dTxId,
                                                Slot = dSlot,
                                                OnRowChanged = RecalculateTotals
                                            });
                                        }
                                    }
                                    else
                                    {
                                        var master = allTypes.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), normDCode, StringComparison.OrdinalIgnoreCase));
                                        bool isManualOnly = master != null && master.IsManualOnly;
                                        bool isTidConfigured = master != null && master.HasTidBatch;
                                        bool isManualScalar = (!string.IsNullOrWhiteSpace(dTxId) && dTxId.StartsWith("manual-scalar-", StringComparison.OrdinalIgnoreCase))
                                            || (!isTidConfigured && string.IsNullOrWhiteSpace(dTid) && string.IsNullOrWhiteSpace(dBatch));
                                        bool isTidItem = !isManualScalar;
                                        if (isTidItem)
                                        {
                                            string dispMode = ResolveCollectionTypeDisplayName(dCode);
                                            string cleanTid = dTid ?? "";
                                            string cleanBatch = dBatch ?? "";
                                            string cleanSlot = dSlot ?? "General";
                                            // Cross-section check: If already represented in OTP CardSwipeDetails, do not duplicate in ManualTidTransactions
                                            bool inOtp = CardSwipeDetails.Any(c =>
                                                (!string.IsNullOrWhiteSpace(c.TransactionId) && !string.IsNullOrWhiteSpace(dTxId) && string.Equals(c.TransactionId, dTxId, StringComparison.OrdinalIgnoreCase))
                                                || (!string.IsNullOrWhiteSpace(c.Otp) && string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normDCode, StringComparison.OrdinalIgnoreCase) && Math.Abs(c.Amount - dAmt) < 0.001
                                                    && !string.IsNullOrWhiteSpace(c.Tid) && string.Equals(c.Tid.Trim(), cleanTid.Trim(), StringComparison.OrdinalIgnoreCase)
                                                    && !string.IsNullOrWhiteSpace(c.Batch) && string.Equals(c.Batch.Trim(), cleanBatch.Trim(), StringComparison.OrdinalIgnoreCase)));
                                            if (inOtp) continue;

                                            bool exists = ManualTidTransactions.Any(m =>
                                                (!string.IsNullOrWhiteSpace(dTxId) && string.Equals(m.TransactionId, dTxId, StringComparison.OrdinalIgnoreCase))
                                                || (string.Equals(m.Mode, dispMode, StringComparison.OrdinalIgnoreCase) &&
                                                    Math.Abs(m.Amount - dAmt) < 0.001 &&
                                                    string.Equals(m.Tid, cleanTid, StringComparison.OrdinalIgnoreCase) &&
                                                    string.Equals(m.Batch, cleanBatch, StringComparison.OrdinalIgnoreCase) &&
                                                    string.Equals(m.Slot, cleanSlot, StringComparison.OrdinalIgnoreCase)));

                                            if (!exists)
                                            {
                                                ManualTidTransactions.Add(new DsmCardSwipeRow
                                                {
                                                    Mode = dispMode,
                                                    Amount = dAmt,
                                                    Tid = cleanTid,
                                                    Batch = cleanBatch,
                                                    Slot = cleanSlot,
                                                    TransactionId = !string.IsNullOrWhiteSpace(dTxId) ? dTxId : Guid.NewGuid().ToString("D"),
                                                    OnRowChanged = RecalculateTotals
                                                });
                                            }
                                        }
                                        else
                                        {
                                            if (!PaymentCollection.IsStandardCollectionCode(dCode))
                                            {
                                                if (!_dynamicManualAmounts.ContainsKey(normDCode))
                                                {
                                                    _dynamicManualAmounts[normDCode] = dAmt;
                                                    _dynamicManualTids[normDCode] = dTid ?? "";
                                                    _dynamicManualBatches[normDCode] = dBatch ?? "";
                                                    _dynamicManualSlots[normDCode] = dSlot ?? "General";
                                                    if (normDCode == "OTHERS")
                                                    {
                                                        OthersAmount = 0;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            // In formatVersion < 3 (legacy ingestion):
                            // Backward-compatible ingestion combining cardSwipeDetails and dynamicItems.
                            // Collect candidate legacy dynamic rows and resolve potential duplicates according to the approved rules.
                            var legacyCandidates = new List<(string Mode, double Amount, string Tid, string Batch, string Slot, string? TxId, string? Otp)>();

                            if (metadata.cardSwipeDetails != null)
                            {
                                foreach (var swipe in metadata.cardSwipeDetails)
                                {
                                    string mode = swipe.mode ?? "";
                                    double amt = (double)(swipe.amount ?? 0.0);
                                    string tid = swipe.tid ?? "";
                                    string batch = swipe.batch ?? "";
                                    string otp = (string?)(swipe.otp ?? swipe.Otp ?? "");
                                    string? txId = (string?)(swipe.transactionId ?? swipe.TransactionId ?? "");
                                    string slot = (string?)(swipe.slot ?? swipe.Slot ?? PaymentCollection.ExtractSlotFromMode(mode, "General"));
                                    if (amt <= 0) continue;

                                    string normMode = PaymentCollection.NormalizeCollectionTypeCode(mode);
                                    if (PaymentCollection.IsStandardCollectionCode(mode))
                                    {
                                        if (!string.IsNullOrWhiteSpace(otp))
                                        {
                                            if (!CardSwipeDetails.Any(c => 
                                                string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normMode, StringComparison.OrdinalIgnoreCase)
                                                && Math.Abs(c.Amount - amt) < 0.01
                                                && string.Equals((c.Otp ?? "").Trim(), (otp ?? "").Trim(), StringComparison.OrdinalIgnoreCase)))
                                            {
                                                CardSwipeDetails.Add(new DsmCardSwipeRow
                                                {
                                                    Mode = mode,
                                                    Amount = amt,
                                                    Tid = tid,
                                                    Batch = batch,
                                                    Otp = otp,
                                                    TransactionId = txId,
                                                    Slot = slot,
                                                    OnRowChanged = RecalculateTotals
                                                });
                                            }
                                        }
                                        else
                                        {
                                            if (normMode == "CREDIT_CARD" && CardAmount <= 0) CardAmount += amt;
                                            else if (normMode == "PETROCARD" && PetroCardAmount <= 0) PetroCardAmount += amt;
                                            else if (normMode == "PHONEPE" && UpiAmount <= 0) UpiAmount += amt;
                                        }
                                    }
                                    else
                                    {
                                        legacyCandidates.Add((mode, amt, tid, batch, slot, txId, otp));
                                    }
                                }
                            }

                            if (metadata.dynamicItems != null)
                            {
                                foreach (var d in metadata.dynamicItems)
                                {
                                    string dCode = (string)(d.collectionTypeCode ?? d.code ?? "");
                                    if (string.IsNullOrWhiteSpace(dCode)) continue;
                                    double dAmt = Convert.ToDouble((object?)(d.amount ?? 0.0));
                                    string? dOtp = (string?)(d.otp ?? d.Otp ?? "");
                                    string? dTid = (string?)(d.tid ?? d.Tid ?? "");
                                    string? dBatch = (string?)(d.batch ?? d.Batch ?? "");
                                    string? dSlot = (string?)(d.slot ?? d.Slot ?? "General");
                                    string? dTxId = (string?)(d.transactionId ?? d.TransactionId ?? "");
                                    if (dAmt <= 0) continue;

                                    legacyCandidates.Add((dCode, dAmt, dTid ?? "", dBatch ?? "", dSlot ?? "General", dTxId, dOtp));
                                }
                            }

                            if (metadata.dynamicCollectionValues != null)
                            {
                                try
                                {
                                    var dcvJson = JsonConvert.SerializeObject(metadata.dynamicCollectionValues);
                                    var dcvDict = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(dcvJson);
                                    if (dcvDict != null)
                                    {
                                        foreach (var kvp in dcvDict)
                                        {
                                            string dCode = kvp.Key;
                                            if (string.IsNullOrWhiteSpace(dCode)) continue;
                                            string normDCode = PaymentCollection.NormalizeCollectionTypeCode(dCode);
                                            if (legacyCandidates.Any(c => string.IsNullOrWhiteSpace(c.Otp) && string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normDCode, StringComparison.OrdinalIgnoreCase)))
                                                continue;

                                            double dAmt = Convert.ToDouble((object?)(kvp.Value?.amount ?? 0.0));
                                            string? dTid = Convert.ToString((object?)(kvp.Value?.tid ?? "")) ?? "";
                                            string? dBatch = Convert.ToString((object?)(kvp.Value?.batch ?? "")) ?? "";
                                            string? dSlot = Convert.ToString((object?)(kvp.Value?.slot ?? "General")) ?? "General";
                                            if (dAmt <= 0) continue;

                                            legacyCandidates.Add((dCode, dAmt, dTid, dBatch, dSlot, null, null));
                                        }
                                    }
                                }
                                catch { }
                            }

                            foreach (var cand in legacyCandidates)
                            {
                                string normCode = PaymentCollection.NormalizeCollectionTypeCode(cand.Mode);
                                if (!string.IsNullOrWhiteSpace(cand.Otp))
                                {
                                    if (!CardSwipeDetails.Any(c =>
                                        string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normCode, StringComparison.OrdinalIgnoreCase)
                                        && Math.Abs(c.Amount - cand.Amount) < 0.01
                                        && string.Equals((c.Otp ?? "").Trim(), (cand.Otp ?? "").Trim(), StringComparison.OrdinalIgnoreCase)))
                                    {
                                        CardSwipeDetails.Add(new DsmCardSwipeRow
                                        {
                                            Mode = !string.IsNullOrWhiteSpace(normCode) ? normCode : cand.Mode,
                                            Amount = cand.Amount,
                                            Tid = cand.Tid,
                                            Batch = cand.Batch,
                                            Otp = cand.Otp,
                                            TransactionId = cand.TxId,
                                            Slot = cand.Slot,
                                            OnRowChanged = RecalculateTotals
                                        });
                                    }
                                }
                                else
                                {
                                    var master = allTypes.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), normCode, StringComparison.OrdinalIgnoreCase));
                                    bool isTidConfigured = master != null && master.HasTidBatch;
                                    bool isManualScalar = !string.IsNullOrWhiteSpace(cand.TxId) && cand.TxId.StartsWith("manual-scalar-", StringComparison.OrdinalIgnoreCase);
                                    bool isTidItem = !isManualScalar && (!string.IsNullOrWhiteSpace(cand.Tid) || !string.IsNullOrWhiteSpace(cand.Batch) || normCode == "PINELABS_CARD" || isTidConfigured);
                                    bool hasReliableId = !string.IsNullOrWhiteSpace(cand.TxId) && !isManualScalar;
                                    double effectiveAmount = cand.Amount;

                                    bool wasDeductedFromOtp = false;
                                    // In legacy ingestion without unique transactionId:
                                    // If OTP items exist for this mode, deduct existing OTP from total to prevent double-counting
                                    if (!hasReliableId)
                                    {
                                        double existingOtp = CardSwipeDetails
                                            .Where(c => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normCode, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Otp))
                                            .Sum(c => c.Amount);

                                        if (existingOtp > 0)
                                        {
                                            if (Math.Abs(effectiveAmount - existingOtp) < 0.01)
                                            {
                                                // Single aggregate header representing the OTP transactions: skip to prevent double counting
                                                continue;
                                            }
                                            else if (effectiveAmount > existingOtp)
                                            {
                                                // Attendant entered total (OTP + manual excess): excess portion is effective amount
                                                effectiveAmount = effectiveAmount - existingOtp;
                                                wasDeductedFromOtp = true;
                                            }
                                        }
                                    }

                                    if (isTidItem)
                                    {
                                        // Cross-section check: If already represented in OTP CardSwipeDetails, do not duplicate in ManualTidTransactions
                                        bool inOtp = !wasDeductedFromOtp && CardSwipeDetails.Any(c =>
                                            (!string.IsNullOrWhiteSpace(c.TransactionId) && !string.IsNullOrWhiteSpace(cand.TxId) && string.Equals(c.TransactionId, cand.TxId, StringComparison.OrdinalIgnoreCase))
                                            || (!string.IsNullOrWhiteSpace(c.Otp) && string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normCode, StringComparison.OrdinalIgnoreCase) && Math.Abs(c.Amount - effectiveAmount) < 0.01
                                                && !string.IsNullOrWhiteSpace(c.Tid) && string.Equals(c.Tid.Trim(), cand.Tid.Trim(), StringComparison.OrdinalIgnoreCase)
                                                && !string.IsNullOrWhiteSpace(c.Batch) && string.Equals(c.Batch.Trim(), cand.Batch.Trim(), StringComparison.OrdinalIgnoreCase)));
                                        if (inOtp) continue;

                                        // Identity-aware legacy duplicate resolution rules:
                                        // 1. Matching, reliable TransactionIds: Count one logical transaction once.
                                        // 2. Different, reliable TransactionIds: Preserve both transactions.
                                        // 3. No reliable TransactionId, with matching TID/Batch/amount/slot: Treat as a potential duplicate, NOT proven duplicate: preserve both and raise HasAmbiguousLegacyDuplicates advisory.
                                        // 4. Conflicting non-empty TID, Batch, or slot values or different amounts: Preserve both transactions.
                                        if (hasReliableId)
                                        {
                                            if (ManualTidTransactions.Any(t => string.Equals(t.TransactionId, cand.TxId, StringComparison.OrdinalIgnoreCase)))
                                            {
                                                // Same logical transaction already counted once
                                                continue;
                                            }
                                        }
                                        else
                                        {
                                            var identicalMatch = ManualTidTransactions.FirstOrDefault(t =>
                                                string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Mode), normCode, StringComparison.OrdinalIgnoreCase)
                                                && Math.Abs(t.Amount - effectiveAmount) < 0.01
                                                && string.Equals(t.Tid?.Trim(), cand.Tid.Trim(), StringComparison.OrdinalIgnoreCase)
                                                && string.Equals(t.Batch?.Trim(), cand.Batch.Trim(), StringComparison.OrdinalIgnoreCase)
                                                && string.Equals(t.Slot?.Trim(), cand.Slot.Trim(), StringComparison.OrdinalIgnoreCase));

                                            if (identicalMatch != null)
                                            {
                                                // Identical fields without reliable ID => Treat as potential duplicate, preserve BOTH and raise advisory!
                                                HasAmbiguousLegacyDuplicates = true;
                                                LegacyDuplicateAdvisoryMessage = "⚠️ Potential duplicate legacy transaction detected: multiple entries match collection type, amount, TID, and batch without unique transaction IDs. Both have been preserved for review.";
                                            }
                                        }

                                        ManualTidTransactions.Add(new DsmCardSwipeRow
                                        {
                                            Mode = ResolveCollectionTypeDisplayName(cand.Mode),
                                            Amount = effectiveAmount,
                                            Tid = cand.Tid,
                                            Batch = cand.Batch,
                                            Slot = cand.Slot,
                                            TransactionId = hasReliableId ? cand.TxId : Guid.NewGuid().ToString("D"),
                                            OnRowChanged = RecalculateTotals
                                        });
                                    }
                                    else
                                    {
                                        // Legacy scalar dynamic amount without TID/Batch:
                                        // Hydrate as a preserved transaction row in ManualTidTransactions so it is visible and editable,
                                        // avoiding hidden resurrecting scalar fields.
                                        string legacyTxId = $"legacy-scalar-{normCode}";
                                        if (!ManualTidTransactions.Any(t => string.Equals(t.TransactionId, legacyTxId, StringComparison.OrdinalIgnoreCase)))
                                        {
                                            ManualTidTransactions.Add(new DsmCardSwipeRow
                                            {
                                                Mode = ResolveCollectionTypeDisplayName(cand.Mode),
                                                Amount = effectiveAmount,
                                                Tid = cand.Tid ?? "",
                                                Batch = cand.Batch ?? "",
                                                Slot = cand.Slot,
                                                TransactionId = legacyTxId,
                                                OnRowChanged = RecalculateTotals
                                            });
                                        }
                                    }
                                }
                            }
                        }

                        if (metadata.settlements != null)
                        {
                            foreach (var s in metadata.settlements)
                            {
                                string pType = (string)(s.paymentType ?? "");
                                double amt = (double)(s.amount ?? 0.0);
                                if (amt <= 0) continue;

                                string normType = PaymentCollection.NormalizeCollectionTypeCode(pType);
                                if (normType == "CREDIT_CARD" && CardAmount <= 0) CardAmount += amt;
                                else if (normType == "PETROCARD" && PetroCardAmount <= 0) PetroCardAmount += amt;
                                else if (normType == "PHONEPE" && UpiAmount <= 0) UpiAmount += amt;
                            }
                        }

                        // Ensure CardAmount, PetroCardAmount, and UpiAmount represent strictly the manual portion
                        // so that amounts added in OTP transactions appear strictly under OTP transactions and not doubled in manual boxes.
                        double ccOtpSumLoaded = CardSwipeDetails
                            .Where(c => PaymentCollection.NormalizeCollectionTypeCode(c.Mode) == "CREDIT_CARD" && !string.IsNullOrWhiteSpace(c.Otp))
                            .Sum(c => c.Amount);
                        if (CardAmount >= ccOtpSumLoaded && ccOtpSumLoaded > 0)
                        {
                            CardAmount = Math.Max(0, CardAmount - ccOtpSumLoaded);
                        }

                        double pcOtpSumLoaded = CardSwipeDetails
                            .Where(c => PaymentCollection.NormalizeCollectionTypeCode(c.Mode) == "PETROCARD" && !string.IsNullOrWhiteSpace(c.Otp))
                            .Sum(c => c.Amount);
                        if (PetroCardAmount >= pcOtpSumLoaded && pcOtpSumLoaded > 0)
                        {
                            PetroCardAmount = Math.Max(0, PetroCardAmount - pcOtpSumLoaded);
                        }

                        double phOtpSumLoaded = CardSwipeDetails
                            .Where(c => PaymentCollection.NormalizeCollectionTypeCode(c.Mode) == "PHONEPE" && !string.IsNullOrWhiteSpace(c.Otp))
                            .Sum(c => c.Amount);
                        if (UpiAmount >= phOtpSumLoaded && phOtpSumLoaded > 0)
                        {
                            UpiAmount = Math.Max(0, UpiAmount - phOtpSumLoaded);
                        }

                        if (metadata.phonePeMorning != null) PhonePeMorning = Convert.ToDouble((object?)metadata.phonePeMorning);
                        if (metadata.phonePeDay != null) PhonePeDay = Convert.ToDouble((object?)metadata.phonePeDay);
                        if (metadata.phonePeNight != null) PhonePeNight = Convert.ToDouble((object?)metadata.phonePeNight);

                        if (metadata.creditCardMorning != null) CreditCardMorning = Convert.ToDouble((object?)metadata.creditCardMorning);
                        if (metadata.creditCardDay != null) CreditCardDay = Convert.ToDouble((object?)metadata.creditCardDay);
                        if (metadata.creditCardNight != null) CreditCardNight = Convert.ToDouble((object?)metadata.creditCardNight);

                        if (metadata.petroCardMorning != null) PetroCardMorning = Convert.ToDouble((object?)metadata.petroCardMorning);
                        if (metadata.petroCardDay != null) PetroCardDay = Convert.ToDouble((object?)metadata.petroCardDay);
                        if (metadata.petroCardNight != null) PetroCardNight = Convert.ToDouble((object?)metadata.petroCardNight);

                        if (CreditCardMorning >= ccOtpSumLoaded && ccOtpSumLoaded > 0) CreditCardMorning = Math.Max(0, CreditCardMorning - ccOtpSumLoaded);
                        if (PetroCardMorning >= pcOtpSumLoaded && pcOtpSumLoaded > 0) PetroCardMorning = Math.Max(0, PetroCardMorning - pcOtpSumLoaded);
                        if (PhonePeMorning >= phOtpSumLoaded && phOtpSumLoaded > 0) PhonePeMorning = Math.Max(0, PhonePeMorning - phOtpSumLoaded);

                        if (PhonePeMorning == 0 && PhonePeDay == 0 && PhonePeNight == 0 && metadata.settlements != null)
                        {
                            foreach (var s in metadata.settlements)
                            {
                                string pType = Convert.ToString((object?)(s.paymentType ?? "")) ?? "";
                                string period = Convert.ToString((object?)(s.period ?? "")) ?? "";
                                double amt = Convert.ToDouble((object?)(s.amount ?? 0.0));
                                if (amt <= 0) continue;
                                string norm = PaymentCollection.NormalizeCollectionTypeCode(pType);
                                if (norm == "PHONEPE")
                                {
                                    if (period.Equals("Night", StringComparison.OrdinalIgnoreCase)) PhonePeNight += amt;
                                    else if (period.Equals("Day", StringComparison.OrdinalIgnoreCase)) PhonePeDay += amt;
                                    else PhonePeMorning += amt;
                                }
                                else if (norm == "CREDIT_CARD")
                                {
                                    if (period.Equals("Night", StringComparison.OrdinalIgnoreCase)) CreditCardNight += amt;
                                    else if (period.Equals("Day", StringComparison.OrdinalIgnoreCase)) CreditCardDay += amt;
                                    else CreditCardMorning += amt;
                                }
                                else if (norm == "PETROCARD")
                                {
                                    if (period.Equals("Night", StringComparison.OrdinalIgnoreCase)) PetroCardNight += amt;
                                    else if (period.Equals("Day", StringComparison.OrdinalIgnoreCase)) PetroCardDay += amt;
                                    else PetroCardMorning += amt;
                                }
                            }
                        }

                        if (PhonePeMorning == 0 && PhonePeDay == 0 && PhonePeNight == 0 && UpiAmount > 0)
                        {
                            if (submission.ShiftType == "C") PhonePeNight = UpiAmount;
                            else if (submission.ShiftType == "B") PhonePeDay = UpiAmount;
                            else PhonePeMorning = UpiAmount;
                        }
                        if (CreditCardMorning == 0 && CreditCardDay == 0 && CreditCardNight == 0 && CardAmount > 0)
                        {
                            if (submission.ShiftType == "C") CreditCardNight = CardAmount;
                            else if (submission.ShiftType == "B") CreditCardDay = CardAmount;
                            else CreditCardMorning = CardAmount;
                        }
                        if (PetroCardMorning == 0 && PetroCardDay == 0 && PetroCardNight == 0 && PetroCardAmount > 0)
                        {
                            if (submission.ShiftType == "C") PetroCardNight = PetroCardAmount;
                            else if (submission.ShiftType == "B") PetroCardDay = PetroCardAmount;
                            else PetroCardMorning = PetroCardAmount;
                        }

                        if (UpiAmount <= 0 && (PhonePeMorning + PhonePeDay + PhonePeNight) > 0)
                            UpiAmount = PhonePeMorning + PhonePeDay + PhonePeNight;
                        if (CardAmount <= 0 && (CreditCardMorning + CreditCardDay + CreditCardNight) > 0)
                            CardAmount = CreditCardMorning + CreditCardDay + CreditCardNight;
                        if (PetroCardAmount <= 0 && (PetroCardMorning + PetroCardDay + PetroCardNight) > 0)
                            PetroCardAmount = PetroCardMorning + PetroCardDay + PetroCardNight;

                        HasSplitValues = (PhonePeNight > 0 || CreditCardNight > 0 || PetroCardNight > 0 ||
                                          PhonePeDay > 0 || CreditCardDay > 0 || PetroCardDay > 0 ||
                                          submission.ShiftType == "A" || submission.ShiftType == "C");

                        // Clean up any redundant total row when itemized OTP transactions exist
                        CleanupRedundantTotals(metadata, collResult.Success ? collResult.Data : null);

                        // In legacy formatVersion < 3, ensure _dynamicManualAmounts reflects strictly any un-itemized excess manual portion
                        // (after subtracting both itemized OTP transactions and ingested ManualTidTransactions).
                        if (formatVersion < 3)
                        {
                            var allDynamicKeys = _dynamicManualAmounts.Keys.ToList();
                            foreach (var normCode in allDynamicKeys)
                            {
                                if (_dynamicManualAmounts.TryGetValue(normCode, out var totalAmt))
                                {
                                    double existingOtp = CardSwipeDetails
                                        .Where(c => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normCode, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Otp))
                                        .Sum(c => c.Amount);
                                    double existingTid = ManualTidTransactions
                                        .Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Mode), normCode, StringComparison.OrdinalIgnoreCase))
                                        .Sum(t => t.Amount);

                                    double residualManual = Math.Max(0, totalAmt - existingOtp - existingTid);
                                    if (residualManual > 0.009)
                                        _dynamicManualAmounts[normCode] = residualManual;
                                    else
                                        _dynamicManualAmounts.Remove(normCode);
                                }
                            }
                        }

                        PersonalDebtors.Clear();

                        KhandhareEntries.Clear();
                        var kpRawList = metadata.khandhareEntries ?? metadata.khandharePetroleumEntries;
                        if (kpRawList != null)
                        {
                            foreach (var kp in kpRawList)
                            {
                                string name = (kp.name ?? kp.Name ?? string.Empty).ToString();
                                string slipNumber = (kp.slipNumber ?? kp.SlipNumber ?? string.Empty).ToString();
                                string vehicleNumber = (kp.vehicleNumber ?? kp.VehicleNumber ?? kp.vehicle_number ?? kp.vehicleNo ?? string.Empty).ToString();
                                double amount = Convert.ToDouble((object?)(kp.amount ?? kp.Amount ?? 0.0));

                                KhandhareEntries.Add(new KhandharePetroleumEntry
                                {
                                    DsmName = submission.DsmName,
                                    Date = submission.ShiftDate,
                                    Name = name,
                                    SlipNumber = slipNumber,
                                    VehicleNumber = vehicleNumber,
                                    Amount = amount
                                });
                            }
                        }

                        QrPayments.Clear();
                        var qrRawList = metadata.qrPayments ?? metadata.qrPaymentEntries ?? metadata.Collections?.qrPayments;
                        if (qrRawList != null)
                        {
                            foreach (var qr in qrRawList)
                            {
                                string targetDsm = (qr.targetDsmName ?? qr.TargetDsmName ?? qr.target_dsm_name ?? qr.targetDsm ?? string.Empty).ToString();
                                double amount = Convert.ToDouble((object?)(qr.amount ?? qr.Amount ?? 0.0));
                                string tid = (qr.tid ?? qr.Tid ?? string.Empty).ToString();
                                string batch = (qr.batch ?? qr.Batch ?? string.Empty).ToString();
                                string slot = (qr.slot ?? qr.Slot ?? string.Empty).ToString();

                                if (amount > 0)
                                {
                                    string effectiveTarget = string.IsNullOrWhiteSpace(targetDsm) ? "General QR" : targetDsm.Trim();
                                    QrPayments.Add(new DsmQrPaymentEntry
                                    {
                                        DsmName = submission.DsmName,
                                        TargetDsmName = effectiveTarget,
                                        Date = submission.ShiftDate,
                                        Amount = amount,
                                        Tid = string.IsNullOrWhiteSpace(tid) ? null : tid,
                                        Batch = string.IsNullOrWhiteSpace(batch) ? null : batch,
                                        Slot = string.IsNullOrWhiteSpace(slot) ? null : slot
                                    });
                                }
                            }
                        }

                        if (metadata.testingEntries != null)
                        {
                            foreach (var test in metadata.testingEntries)
                            {
                                int nId = (int)(test.nozzleId ?? 0);
                                double litres = (double)(test.amount ?? test.litres ?? 0.0);
                                double rate = Convert.ToDouble((object?)(test.rate ?? 0.0));
                                if (rate <= 0 && nId != 0)
                                {
                                    var matchN = NozzleReadings.FirstOrDefault(n => n.NozzleId == nId);
                                    if (matchN != null && matchN.Rate > 0) rate = matchN.Rate;
                                }
                                bool hasExplicit = (test.rupeeAmount != null || test.RupeeAmount != null);
                                double? explicitRupeeAmt = hasExplicit ? Convert.ToDouble((object?)(test.rupeeAmount ?? test.RupeeAmount)) : null;

                                SubmissionTestingEntries.Add(new DsmTestingRow
                                {
                                    NozzleId = nId,
                                    FuelType = test.fuelType ?? "",
                                    Amount = litres,
                                    RupeeAmount = explicitRupeeAmt,
                                    HasExplicitRupeeAmount = hasExplicit,
                                    PumpId = (int)(test.pumpId ?? submission.PumpId)
                                });
                            }
                        }
                        if (metadata.oilDefSales != null)
                        {
                            foreach (var sale in metadata.oilDefSales)
                            {
                                var row = new DsmOilDefSaleRow
                                {
                                    ProductId = (int)(sale.productId ?? 0),
                                    ProductName = sale.productName ?? "",
                                    Category = sale.category ?? "Oil",
                                    Quantity = (double)(sale.quantity ?? 0.0),
                                    Price = (double)(sale.price ?? 0.0),
                                    Unit = sale.unit ?? ""
                                };
                                row.PropertyChanged += (s, e) => RecalculateTotals();
                                OilDefSales.Add(row);
                            }
                        }

                        SubmissionExpenses.Clear();
                        var expRawList = metadata.expenseEntries ?? metadata.expensesList;
                        if (expRawList != null)
                        {
                            foreach (var exp in expRawList)
                            {
                                string desc = (exp.description ?? exp.Description ?? exp.notes ?? exp.Notes ?? "DSM PWA Expense").ToString();
                                double amt = Convert.ToDouble((object?)(exp.amount ?? exp.Amount ?? 0.0));
                                if (amt > 0)
                                {
                                    SubmissionExpenses.Add(new Expense { Description = desc, Amount = amt });
                                }
                            }
                        }
                        if (SubmissionExpenses.Count > 0)
                        {
                            ExpenseAmount = SubmissionExpenses.Sum(e => e.Amount);
                            ExpenseNotes = string.Join(", ", SubmissionExpenses.Select(e => e.Description));
                        }
                        else if (ExpenseAmount > 0)
                        {
                            SubmissionExpenses.Add(new Expense
                            {
                                Description = !string.IsNullOrWhiteSpace(ExpenseNotes) ? ExpenseNotes : "Expense",
                                Amount = ExpenseAmount
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to parse metadata JSON for details view");
                    MetadataParseError = $"Metadata parsing failed: {ex.Message}";
                }
            }

            if (SubmissionExpenses.Count == 0 && ExpenseAmount > 0)
            {
                SubmissionExpenses.Add(new Expense
                {
                    Description = !string.IsNullOrWhiteSpace(ExpenseNotes) ? ExpenseNotes : "Expense",
                    Amount = ExpenseAmount
                });
            }

            SubmissionNotes = submission.Notes;
            AttachmentUrl = submission.AttachmentUrl;

            await PopulateCollectionTypesAndDynamicCollectionsAsync(submission);

            RecalculateTotals();
            StatusMessage = "Submission details loaded.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load details for submission {SubId}", submission.Id);
            StatusMessage = $"❌ Details load error: {ex.Message}";
            MetadataParseError = ex.Message;
        }
        finally
        {
            _isLoadingSubmission = false;
            IsLoadingDetails = false;
            NotifySubmissionStateChanged();
        }
    }

    private string GetFuelTypeName(int pumpId, int nozzleNumber, DateTime date)
    {
        try
        {
            return PumpConfiguration.GetFuelTypeDisplayName(pumpId, nozzleNumber, date);
        }
        catch
        {
            return "MS";
        }
    }

    private static int GetDynamicNozzleId(dynamic r)
    {
        if (r == null) return 0;
        try
        {
            if (r is System.Dynamic.ExpandoObject exp)
            {
                var dict = (IDictionary<string, object?>)exp;
                if (dict.TryGetValue("NozzleId", out var nid) || dict.TryGetValue("nozzleId", out nid))
                    return Convert.ToInt32(nid);
            }
            if (r is Newtonsoft.Json.Linq.JObject jobj)
            {
                return (int?)(jobj["NozzleId"] ?? jobj["nozzleId"]) ?? 0;
            }
            return (int)(r.NozzleId ?? r.nozzleId ?? 0);
        }
        catch
        {
            return 0;
        }
    }

    private static string? GetDynamicFuelType(dynamic r)
    {
        if (r == null) return null;
        try
        {
            if (r is System.Dynamic.ExpandoObject expando)
            {
                var dict = (IDictionary<string, object?>)expando;
                if (dict.TryGetValue("FuelType", out var val) || dict.TryGetValue("fuelType", out val))
                    return val?.ToString();
                return null;
            }
            if (r is Newtonsoft.Json.Linq.JObject jobj)
            {
                return (string?)(jobj["FuelType"] ?? jobj["fuelType"]);
            }
            return (string?)r.FuelType;
        }
        catch
        {
            return null;
        }
    }

    private void CleanupRedundantTotals(dynamic? metadata, dynamic? coll)
    {
        // 1. Remove rows explicitly flagged as synthetic summaries or lacking OTP
        for (int i = CardSwipeDetails.Count - 1; i >= 0; i--)
        {
            if (CardSwipeDetails[i].IsSyntheticSummary || string.IsNullOrWhiteSpace(CardSwipeDetails[i].Otp))
            {
                CardSwipeDetails.RemoveAt(i);
            }
        }

        // 2. Deduplicate exact duplicate OTP entries
        var seenOtps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = CardSwipeDetails.Count - 1; i >= 0; i--)
        {
            var swipe = CardSwipeDetails[i];
            if (!string.IsNullOrWhiteSpace(swipe.Otp))
            {
                var norm = PaymentCollection.NormalizeCollectionTypeCode(swipe.Mode);
                var key = $"{norm}|{swipe.Otp.Trim()}|{Math.Round(swipe.Amount, 2)}";
                if (!seenOtps.Add(key))
                {
                    CardSwipeDetails.RemoveAt(i);
                }
            }
        }
    }

    private void RecalculateTotals()
    {
        GrossSales = NozzleReadings.Sum(r => r.Amount);
        TotalLitres = NozzleReadings.Sum(r => r.SaleLitres);
        UpdateFuelTypeSummaries();

        foreach (var mRow in ManualCollections)
        {
            var norm = PaymentCollection.NormalizeCollectionTypeCode(mRow.Code);
            if (mRow.Amount.HasValue && mRow.Amount.Value > 0)
            {
                _dynamicManualAmounts[norm] = mRow.Amount.Value;
            }
            else
            {
                _dynamicManualAmounts.Remove(norm);
            }
        }

        // Keep CreditAmount in sync when itemized DebtorEntries exist with non-zero amounts
        if (DebtorEntries.Count > 0)
        {
            double sumDebtors = DebtorEntries.Sum(d => d.Amount);
            if (sumDebtors > 0.009 && Math.Abs(_creditAmount - sumDebtors) > 0.009)
            {
                _isSyncingCredit = true;
                try
                {
                    CreditAmount = sumDebtors;
                }
                finally
                {
                    _isSyncingCredit = false;
                }
            }
        }

        double itemizedExpSum = SubmissionExpenses.Sum(e => e.Amount);
        double effectiveExp = (SubmissionExpenses.Count > 0 && Math.Abs(itemizedExpSum - ExpenseAmount) <= 0.01)
            ? itemizedExpSum
            : ExpenseAmount;

        var expenseDtos = (effectiveExp > 0
            ? (SubmissionExpenses.Count > 0 && Math.Abs(itemizedExpSum - ExpenseAmount) <= 0.01
                ? SubmissionExpenses.Select(e => new FuelPro.Core.DTOs.ExpenseDto { Amount = (decimal)e.Amount })
                : new List<FuelPro.Core.DTOs.ExpenseDto> { new() { Amount = (decimal)effectiveExp } })
            : new List<FuelPro.Core.DTOs.ExpenseDto>())
            .Concat(KhandhareEntries.Select(k => new FuelPro.Core.DTOs.ExpenseDto { Amount = (decimal)k.Amount }))
            .ToList();

        // Calculate dynamic collections and pine labs cleanly
        var allDynamicCodes = new HashSet<string>(_dynamicManualAmounts.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var c in CardSwipeDetails)
        {
            var norm = PaymentCollection.NormalizeCollectionTypeCode(c.Mode);
            if ((!PaymentCollection.IsStandardCollectionCode(c.Mode) || norm == "PINELABS_CARD") && !string.IsNullOrWhiteSpace(c.Mode))
            {
                allDynamicCodes.Add(norm);
            }
        }
        foreach (var t in ManualTidTransactions)
        {
            var norm = PaymentCollection.NormalizeCollectionTypeCode(t.Mode);
            if (!string.IsNullOrWhiteSpace(t.Mode) && (!PaymentCollection.IsStandardCollectionCode(t.Mode) || norm == "PINELABS_CARD"))
            {
                allDynamicCodes.Add(norm);
            }
        }

        double dynamicTotal = 0;
        double calculatedPineLabsTotal = 0;
        var summaries = new List<DynamicCollectionSummaryDto>();

        foreach (var normCode in allDynamicCodes)
        {
            double itemizedTidAmt = ManualTidTransactions
                .Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Mode), normCode, StringComparison.OrdinalIgnoreCase))
                .Sum(t => t.Amount);

            double nonOtpAmt = CardSwipeDetails
                .Where(c => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normCode, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(c.Otp))
                .Sum(c => c.Amount);

            double manualScalarAmt = _dynamicManualAmounts.TryGetValue(normCode, out var m) && m > 0 ? m : nonOtpAmt;

            double otpAmt = CardSwipeDetails
                .Where(c => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normCode, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Otp))
                .Sum(c => c.Amount);

            double effectiveCodeTotal = (itemizedTidAmt + otpAmt > 0) ? (itemizedTidAmt + otpAmt) : manualScalarAmt;
            if (effectiveCodeTotal <= 0) continue;

            dynamicTotal += effectiveCodeTotal;

            if (string.Equals(normCode, "PINELABS_CARD", StringComparison.OrdinalIgnoreCase))
            {
                calculatedPineLabsTotal = effectiveCodeTotal;
            }

            var dynMatch = DynamicCollections.FirstOrDefault(d => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(d.Code), normCode, StringComparison.OrdinalIgnoreCase));
            bool isManualOnly = (itemizedTidAmt == 0 && otpAmt == 0) && ManualCollections.Any(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), normCode, StringComparison.OrdinalIgnoreCase));
            if (isManualOnly)
            {
                // Manual-only collections (e.g. SBI Redeem, QR) are displayed in ManualCollections section; do not duplicate in summary cards
                continue;
            }

            string displayName = !string.IsNullOrWhiteSpace(dynMatch?.DisplayName)
                ? dynMatch.DisplayName
                : ResolveCollectionTypeDisplayName(normCode);

            summaries.Add(new DynamicCollectionSummaryDto
            {
                Code = normCode,
                DisplayName = displayName,
                Amount = effectiveCodeTotal
            });
        }

        DynamicCollectionSummaries.Clear();
        foreach (var s in summaries)
        {
            DynamicCollectionSummaries.Add(s);
        }
        OnPropertyChanged(nameof(HasDynamicSummaries));

        foreach (var dynRow in DynamicCollections)
        {
            var norm = PaymentCollection.NormalizeCollectionTypeCode(dynRow.Code);
            double itemizedTidAmt = ManualTidTransactions
                .Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Mode), norm, StringComparison.OrdinalIgnoreCase))
                .Sum(t => t.Amount);
            double nonOtpAmt = CardSwipeDetails
                .Where(c => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), norm, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(c.Otp))
                .Sum(c => c.Amount);
            double manualScalarAmt = _dynamicManualAmounts.TryGetValue(norm, out var m) && m > 0 ? m : nonOtpAmt;
            double otpAmt = CardSwipeDetails
                .Where(c => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), norm, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Otp))
                .Sum(c => c.Amount);
            double total = (itemizedTidAmt > 0 || otpAmt > 0) ? (itemizedTidAmt + otpAmt) : manualScalarAmt;

            dynRow.ManualAmount = manualScalarAmt > 0 ? manualScalarAmt : null;
            dynRow.OtpAmount = otpAmt;
            dynRow.TidAmount = itemizedTidAmt;
            dynRow.Amount = total > 0 ? total : null;
        }

        _isRecalculatingPineLabs = true;
        try
        {
            PineLabsAmount = calculatedPineLabsTotal;
        }
        finally
        {
            _isRecalculatingPineLabs = false;
        }

        foreach (var t in SubmissionTestingEntries)
        {
            if (!t.RupeeAmount.HasValue || t.RupeeAmount.Value <= 0)
            {
                var nozzleRow = NozzleReadings.FirstOrDefault(n => n.NozzleId == t.NozzleId);
                double rate = nozzleRow != null && nozzleRow.Rate > 0 ? nozzleRow.Rate : 0.0;
                if (rate > 0 && t.Amount > 0)
                {
                    t.RupeeAmount = Math.Round(t.Amount * rate, 2);
                }
            }
        }

        double ccTidSum = ManualTidTransactions
            .Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.Mode) == "CREDIT_CARD")
            .Sum(t => t.Amount);
        double pcTidSum = ManualTidTransactions
            .Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.Mode) == "PETROCARD")
            .Sum(t => t.Amount);
        double phTidSum = ManualTidTransactions
            .Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.Mode) == "PHONEPE")
            .Sum(t => t.Amount);

        double ccOtpSum = CardSwipeDetails
            .Where(c => PaymentCollection.NormalizeCollectionTypeCode(c.Mode) == "CREDIT_CARD" && !string.IsNullOrWhiteSpace(c.Otp))
            .Sum(c => c.Amount);
        double pcOtpSum = CardSwipeDetails
            .Where(c => PaymentCollection.NormalizeCollectionTypeCode(c.Mode) == "PETROCARD" && !string.IsNullOrWhiteSpace(c.Otp))
            .Sum(c => c.Amount);
        double phOtpSum = CardSwipeDetails
            .Where(c => PaymentCollection.NormalizeCollectionTypeCode(c.Mode) == "PHONEPE" && !string.IsNullOrWhiteSpace(c.Otp))
            .Sum(c => c.Amount);

        double itemizedPhonePe = phTidSum + phOtpSum;
        double itemizedCreditCard = ccTidSum + ccOtpSum;
        double itemizedPetroCard = pcTidSum + pcOtpSum;

        double effectivePhonePe = (itemizedPhonePe > 0 ? itemizedPhonePe : UpiAmount) + QrPayments.Sum(q => q.Amount);
        double effectiveCreditCard = itemizedCreditCard > 0 ? itemizedCreditCard : CardAmount;
        double effectivePetroCard = itemizedPetroCard > 0 ? itemizedPetroCard : PetroCardAmount;
        double cashDep = CashDepositAmount;
        double cashPhys = CashAmount;
        double cash3Val = Cash3.TotalAmount;
        double finalDynamicPayments = dynamicTotal;

        // Debtor repayments are separate customer recovery transactions persisted to CreditorRepayments.
        // They must neither inflate nor be deducted from pure fuel-sale collections or DSM reconciliation.

        foreach (var t in SubmissionTestingEntries)
        {
            if (!t.HasExplicitRupeeAmount && t.NozzleId > 0)
            {
                var matchN = NozzleReadings.FirstOrDefault(n => n.NozzleId == t.NozzleId);
                if (matchN != null && matchN.Rate > 0)
                {
                    t.RupeeAmount = Math.Round(t.Amount * matchN.Rate, 2);
                }
            }
        }

        var dto = new FuelPro.Core.DTOs.DsmEntryDto
        {
            NozzleReadings = NozzleReadings.Select(n => new FuelPro.Core.DTOs.NozzleReadingDto { Amount = (decimal)n.Amount }).ToList(),
            PaymentCollection = new FuelPro.Core.DTOs.PaymentCollectionDto
            {
                PhonePe = (decimal)effectivePhonePe,
                CreditCard = (decimal)effectiveCreditCard,
                CashDeposit = (decimal)cashDep,
                PhysicalCash = (decimal)cashPhys,
                Cash3 = (decimal)cash3Val,
                PetroCard = (decimal)effectivePetroCard,
                Others = (decimal)OthersAmount,
                DynamicPayments = (decimal)finalDynamicPayments,
                CollectionReduction = (decimal)CollectionReductionAmount,
                CollectionReductionRemark = CollectionReductionRemark
            },
            DebitEntries = DebtorEntries.Select(d => new FuelPro.Core.DTOs.DebitEntryDto { Amount = (decimal)d.Amount }).ToList(),
            Expenses = expenseDtos,
            TestingEntries = SubmissionTestingEntries.Select(t => 
            {
                if (t.RupeeAmount.HasValue)
                {
                    return new FuelPro.Core.DTOs.TestingEntryDto
                    {
                        FuelType = t.FuelType,
                        Litres = (decimal)t.Amount,
                        Rate = t.Amount > 0 ? (decimal)(t.RupeeAmount.Value / t.Amount) : 0m,
                        Amount = (decimal)t.RupeeAmount.Value
                    };
                }
                var nozzleRow = NozzleReadings.FirstOrDefault(n => n.NozzleId == t.NozzleId);
                decimal rate = nozzleRow != null ? (decimal)nozzleRow.Rate : 0m;
                return new FuelPro.Core.DTOs.TestingEntryDto
                {
                    FuelType = t.FuelType,
                    Litres = (decimal)t.Amount,
                    Rate = rate,
                    Amount = (decimal)t.Amount * rate
                };
            }).ToList()
        };

        TotalOilDefSales = OilDefSales.Sum(s => s.Total);
        dto.ProductSales = (decimal)TotalOilDefSales;
        var calc = _dsmCalculationService.Calculate(dto);
        TotalTestingAmount = (double)calc.TotalTesting;
        NetFuelSales = (double)calc.NetGrossSales;
        TotalCollections = (double)calc.TotalCollection;
        TotalPaymentIn = (double)calc.TotalInDirect;
        double totalSalesLiability = (double)calc.SalesLiability;
        MismatchAmount = (double)calc.Mismatch;
        UpdateCollectionTypeTotals();

        double rawShortage = MismatchAmount < 0 ? Math.Abs(MismatchAmount) : 0;
        double dsmLossAmount = rawShortage; // No tolerance: full shortage is DSM loss
        ShortAmount = rawShortage;          // Shift Short matches full shortage
        ExcessAmount = MismatchAmount > 0 ? MismatchAmount : 0;



        // Check continuity warnings
        HasContinuityWarnings = NozzleReadings.Any(r => r.HasContinuityError);
        HasRateDiscrepancies = NozzleReadings.Any(r => r.HasRateDiscrepancy);
        
        // Build validation summary
        var errors = new List<string>();
        if (HasContinuityWarnings)
            errors.Add("• Continuity warnings detected: DSM opening readings don't match SQLite history.");
        if (Math.Abs(MismatchAmount) > 500)
            errors.Add($"• High Mismatch: Mismatch is ₹{MismatchAmount:F2} (threshold ₹500).");
        if (NozzleReadings.Any(r => r.ClosingReading < r.OpeningReading))
            errors.Add("• Closing reading is less than opening reading.");
        if (CardSwipeDetails.Any(c => c.Amount > 0 && string.IsNullOrWhiteSpace(c.Mode)))
            errors.Add("• Unclassified transaction: A card swipe entry has an amount but no collection type selected.");
        if (!HasValidItemizedDebtors())
            errors.Add($"• Unitemized credit detected (₹{CreditAmount:N2}). All credit sales must be itemized with customer names in the Debtor Entries Log before approval.");

        ValidationSummary = errors.Count > 0 
            ? string.Join(Environment.NewLine, errors) 
            : "✓ Calculations and continuity are correct.";

        OnPropertyChanged(nameof(HasManualTidTransactions));
        OnPropertyChanged(nameof(HasOtpTransactions));
        OnPropertyChanged(nameof(HasTestingEntries));
        OnPropertyChanged(nameof(HasQrPayments));
        OnPropertyChanged(nameof(HasExpenses));
        OnPropertyChanged(nameof(HasKhandhareEntries));
        OnPropertyChanged(nameof(HasPersonalDebtors));
        NotifySubmissionStateChanged();
    }

    public async Task ValidateNozzleRatesAsync(DsmPendingSubmission? submission)
    {
        if (submission == null) return;
        try
        {
            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
            var settings = await context.Settings.FirstOrDefaultAsync();
            var pumpMappings = await context.PumpMappings.ToListAsync();

            var shiftDateOnly = submission.ShiftDate.Date;
            var normShift = _shiftCycleService?.NormalizeShiftType(submission.ShiftType) ?? submission.ShiftType;

            // 1. Fetch approved historical entries for the SAME operational date and SAME shift
            var historicalEntries = await context.DsmEntries
                .Include(e => e.Shift)
                .Include(e => e.NozzleReadings)
                .Where(e => e.Shift != null && e.Shift.ShiftDate.Date == shiftDateOnly)
                .ToListAsync();

            var matchingShiftEntries = historicalEntries
                .Where(e => e.Shift != null && string.Equals(_shiftCycleService?.NormalizeShiftType(e.Shift.ShiftType) ?? e.Shift.ShiftType, normShift, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var row in NozzleReadings)
            {
                double submittedRate = row.SubmittedRate > 0 ? row.SubmittedRate : row.Rate;
                string fuelType = row.FuelType;

                // Establish submitted nozzle's tank assignment wherever available
                string? submittedTank = pumpMappings.FirstOrDefault(m => m.PumpId == row.PumpId && m.NozzleNumber == row.NozzleId)?.TankName;
                if (string.IsNullOrWhiteSpace(submittedTank))
                {
                    submittedTank = PumpConfiguration.GetTankName(row.PumpId, row.NozzleId, submission.ShiftDate);
                }

                // Match exact nozzle identity and matching fuel product on the SAME operational date and SAME shift.
                // An unrelated nozzle's approved rate must NEVER be treated as ground truth for this nozzle.
                var matchingReadingWithEntry = (from e in matchingShiftEntries
                                                from nr in e.NozzleReadings
                                                where nr.NozzleNumber == row.NozzleId &&
                                                      string.Equals(nr.FuelType, fuelType, StringComparison.OrdinalIgnoreCase) &&
                                                      nr.Rate > 0
                                                select new { Entry = e, Reading = nr }).FirstOrDefault();

                if (matchingReadingWithEntry != null)
                {
                    var nozzleHist = matchingReadingWithEntry.Reading;
                    var parentEntry = matchingReadingWithEntry.Entry;
                    double histRate = nozzleHist.Rate;

                    // Establish historical nozzle's tank assignment wherever available
                    string? histTank = pumpMappings.FirstOrDefault(m => m.PumpId == parentEntry.PumpId && m.NozzleNumber == nozzleHist.NozzleNumber)?.TankName;
                    if (string.IsNullOrWhiteSpace(histTank))
                    {
                        histTank = PumpConfiguration.GetTankName(parentEntry.PumpId, nozzleHist.NozzleNumber, parentEntry.Shift?.ShiftDate);
                    }

                    // Check tank assignment compatibility:
                    // If tank assignment is known for either and differs, or cannot be established reliably, do not treat as automatic ground truth.
                    bool hasTankMismatch = !string.IsNullOrWhiteSpace(submittedTank) &&
                                           !string.IsNullOrWhiteSpace(histTank) &&
                                           !string.Equals(submittedTank.Trim(), histTank.Trim(), StringComparison.OrdinalIgnoreCase);

                    if (hasTankMismatch)
                    {
                        // Tank assignment differs -> Preserve submitted rate, display informational guidance only
                        row.HasRateDiscrepancy = true;
                        row.IsHistoricalMismatch = false;
                        row.HistoricalRate = histRate;
                        row.RateWarningMessage = $"ℹ️ Note: Historical record found for Nozzle {row.NozzleId} ({fuelType}), but tank assignment differs (Submitted Tank: '{submittedTank}', Historical Tank: '{histTank}'). Preserving submitted rate ₹{submittedRate:F2}.";
                    }
                    else if (Math.Abs(row.Rate - histRate) > 0.001)
                    {
                        // Ground truth matches operational date, shift, nozzle identity, fuel product, and tank assignment!
                        row.HasRateDiscrepancy = true;
                        row.IsHistoricalMismatch = true;
                        row.HistoricalRate = histRate;
                        string tankDesc = !string.IsNullOrWhiteSpace(histTank) ? $", Tank: {histTank}" : "";
                        row.RateWarningMessage = $"⚠️ Historical Ground-Truth Mismatch: Current rate is ₹{row.Rate:F2} (submitted: ₹{submittedRate:F2}), but approved Shift {normShift} on {submission.ShiftDate:dd-MMM-yyyy} recorded ₹{histRate:F2} for Nozzle {row.NozzleId} ({fuelType}{tankDesc}).";
                    }
                    else
                    {
                        row.HasRateDiscrepancy = false;
                        row.IsHistoricalMismatch = false;
                        row.RateWarningMessage = "";
                    }
                }
                else
                {
                    // Historical evidence is unavailable for this exact nozzle on this shift date & shift.
                    // Fall back to current configured Settings rate as an INFORMATIONAL note only.
                    // Preserve the submitted rate. Do not automatically replace it.
                    double configuredRate = settings?.GetRateFor(fuelType, 0.0) ?? 0.0;
                    if (configuredRate > 0 && Math.Abs(row.Rate - configuredRate) > 0.001)
                    {
                        row.HasRateDiscrepancy = true;
                        row.IsHistoricalMismatch = false;
                        row.HistoricalRate = configuredRate;
                        row.RateWarningMessage = $"ℹ️ Note: Current rate is ₹{row.Rate:F2} (submitted: ₹{submittedRate:F2}), while desktop Settings currently configures ₹{configuredRate:F2} (no approved historical record found for Nozzle {row.NozzleId} on {submission.ShiftDate:dd-MMM-yyyy} Shift {normShift}).";
                    }
                    else
                    {
                        row.HasRateDiscrepancy = false;
                        row.IsHistoricalMismatch = false;
                        row.RateWarningMessage = "";
                    }
                }
            }

            HasRateDiscrepancies = NozzleReadings.Any(n => n.HasRateDiscrepancy);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to validate nozzle rates against historical ground truth");
        }
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
            var rate = g.FirstOrDefault(n => n.Rate > 0)?.Rate ?? 0;
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
    public void AddExpense()
    {
        SubmissionExpenses.Add(new Expense { Description = "", Amount = 0 });
        OnPropertyChanged(nameof(HasExpenses));
        RecalculateTotals();
    }

    [RelayCommand]
    public void RemoveExpense(Expense? exp)
    {
        if (exp != null && SubmissionExpenses.Contains(exp))
        {
            SubmissionExpenses.Remove(exp);
            ExpenseAmount = SubmissionExpenses.Sum(e => e.Amount);
            ExpenseNotes = string.Join(", ", SubmissionExpenses.Select(e => e.Description));
            OnPropertyChanged(nameof(HasExpenses));
            RecalculateTotals();
        }
    }

    private async Task PopulateCollectionTypesAndDynamicCollectionsAsync(DsmPendingSubmission? submission = null)
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
                    new() { Code = "CREDIT_CARD", DisplayName = "Credit / Debit Card", Category = "Card", HasTidBatch = true, DisplayOrder = 2, IsActive = false, IsSystem = true, CreatedAt = now },
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
            _availableCollectionTypes = allTypes;

            bool IsEnabledForCurrentShift(CollectionTypeMaster type)
            {
                if (IsShiftC) return type.EnableNight;
                if (IsDayShift) return type.EnableDay;
                if (IsNightShift && !IsThreeShiftMode) return type.EnableNight;
                return type.EnableMorning;
            }

            bool hasHistoricalPhonePe = UpiAmount > 0 || PhonePeMorning > 0 || PhonePeNight > 0;
            bool hasHistoricalCreditCard = CardAmount > 0 || CreditCardMorning > 0 || CreditCardNight > 0;
            bool hasHistoricalPetroCard = PetroCardAmount > 0 || PetroCardMorning > 0 || PetroCardNight > 0;

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
                PhonePeHasOtp = ph.IsOtpEligible;
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

            // 2. Resolve built-in Credit Card (active-first)
            var cc = allTypes.FirstOrDefault(c => c.IsActive && IsEnabledForCurrentShift(c) && IsCreditCardType(c))
                  ?? allTypes.FirstOrDefault(c => c.IsActive && IsCreditCardType(c))
                  ?? allTypes.FirstOrDefault(c => IsCreditCardType(c));
            if (cc != null)
            {
                CreditCardDisplayName = cc.DisplayName;
                CreditCardHasTidBatch = cc.HasTidBatch;
                CreditCardHasOtp = cc.IsOtpEligible;
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
                PetroCardHasOtp = pc.IsOtpEligible;
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

            OnPropertyChanged(nameof(PhonePeDisplayName));
            OnPropertyChanged(nameof(PhonePeHasTidBatch));
            OnPropertyChanged(nameof(PhonePeHasOtp));
            OnPropertyChanged(nameof(PhonePeIsActive));
            OnPropertyChanged(nameof(IsPhonePeTidOrOtp));
            OnPropertyChanged(nameof(IsPhonePeScalarVisible));
            OnPropertyChanged(nameof(IsPhonePeSplitEnabled));
            OnPropertyChanged(nameof(PhonePeMorningLabel));

            OnPropertyChanged(nameof(CreditCardDisplayName));
            OnPropertyChanged(nameof(CreditCardHasTidBatch));
            OnPropertyChanged(nameof(CreditCardHasOtp));
            OnPropertyChanged(nameof(CreditCardIsActive));
            OnPropertyChanged(nameof(IsCreditCardTidOrOtp));
            OnPropertyChanged(nameof(IsCreditCardScalarVisible));
            OnPropertyChanged(nameof(IsCreditCardSplitEnabled));
            OnPropertyChanged(nameof(CreditCardMorningLabel));

            OnPropertyChanged(nameof(PetroCardDisplayName));
            OnPropertyChanged(nameof(PetroCardHasTidBatch));
            OnPropertyChanged(nameof(PetroCardHasOtp));
            OnPropertyChanged(nameof(PetroCardIsActive));
            OnPropertyChanged(nameof(IsPetroCardTidOrOtp));
            OnPropertyChanged(nameof(IsPetroCardScalarVisible));
            OnPropertyChanged(nameof(IsPetroCardSplitEnabled));
            OnPropertyChanged(nameof(PetroCardMorningLabel));

            // Dynamic types
            var matchedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ph != null)
            {
                matchedCodes.Add(ph.Code);
                matchedCodes.Add("PHONEPE");
                matchedCodes.Add("PHONE_PE");
            }
            if (cc != null)
            {
                matchedCodes.Add(cc.Code);
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

            var allDynamicCodes = new HashSet<string>(_dynamicManualAmounts.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var c in CardSwipeDetails)
            {
                if (!PaymentCollection.IsStandardCollectionCode(c.Mode) && !string.IsNullOrWhiteSpace(c.Mode))
                    allDynamicCodes.Add(PaymentCollection.NormalizeCollectionTypeCode(c.Mode));
            }
            foreach (var t in ManualTidTransactions)
            {
                if (!string.IsNullOrWhiteSpace(t.Mode))
                    allDynamicCodes.Add(PaymentCollection.NormalizeCollectionTypeCode(t.Mode));
            }

            var targetTypes = allTypes
                .Where(x => !matchedCodes.Contains(x.Code))
                .Where(x => !x.IsManualOnly)
                .Where(x => (x.IsActive && IsEnabledForCurrentShift(x)) || allDynamicCodes.Contains(PaymentCollection.NormalizeCollectionTypeCode(x.Code)))
                .OrderBy(x => x.DisplayOrder)
                .ToList();

            foreach (var dCode in allDynamicCodes)
            {
                if (!targetTypes.Any(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Code), dCode, StringComparison.OrdinalIgnoreCase)))
                {
                    targetTypes.Add(new CollectionTypeMaster
                    {
                        Code = dCode,
                        DisplayName = dCode.Replace("_", " "),
                        Category = "Card",
                        HasTidBatch = true,
                        IsActive = false
                    });
                }
            }

            // Deduplicate targetTypes by normalized code
            var distinctTargetTypes = new List<CollectionTypeMaster>();
            var seenTargetCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in targetTypes)
            {
                var norm = PaymentCollection.NormalizeCollectionTypeCode(t.Code);
                if (seenTargetCodes.Add(norm))
                {
                    distinctTargetTypes.Add(t);
                }
            }
            targetTypes = distinctTargetTypes;

            DynamicCollections.Clear();
            foreach (var t in targetTypes)
            {
                var norm = PaymentCollection.NormalizeCollectionTypeCode(t.Code);
                double itemizedTidAmt = ManualTidTransactions
                    .Where(x => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(x.Mode), norm, StringComparison.OrdinalIgnoreCase))
                    .Sum(x => x.Amount);
                double nonOtpAmt = CardSwipeDetails
                    .Where(x => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(x.Mode), norm, StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(x.Otp))
                    .Sum(x => x.Amount);
                double manualScalarAmt = _dynamicManualAmounts.TryGetValue(norm, out var m) && m > 0 ? m : nonOtpAmt;
                double otpAmt = CardSwipeDetails
                    .Where(x => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(x.Mode), norm, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(x.Otp))
                    .Sum(x => x.Amount);
                double total = (itemizedTidAmt > 0 || otpAmt > 0) ? (itemizedTidAmt + otpAmt) : manualScalarAmt;

                DynamicCollections.Add(new DynamicCollectionEntryRow
                {
                    Code = t.Code,
                    DisplayName = t.DisplayName,
                    Category = t.Category,
                    HasTidBatch = t.HasTidBatch,
                    ManualAmount = manualScalarAmt > 0 ? manualScalarAmt : null,
                    OtpAmount = otpAmt,
                    TidAmount = itemizedTidAmt,
                    Amount = total > 0 ? total : null,
                    OnValueChangedAction = () =>
                    {
                        var row = DynamicCollections.FirstOrDefault(d => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(d.Code), norm, StringComparison.OrdinalIgnoreCase));
                        if (row != null)
                        {
                            _dynamicManualAmounts[norm] = row.ManualAmount ?? 0;
                        }
                        RecalculateTotals();
                    }
                });
            }

            // Synchronize Manual-only Collections (Active types where both TID and OTP are disabled)
            var manualTypes = allTypes
                .Where(x => !matchedCodes.Contains(x.Code))
                .Where(x => x.IsManualOnly)
                .Where(x => (x.IsActive && IsEnabledForCurrentShift(x)) || _dynamicManualAmounts.ContainsKey(PaymentCollection.NormalizeCollectionTypeCode(x.Code)))
                .OrderBy(x => x.DisplayOrder)
                .ToList();

            foreach (var kvp in _dynamicManualAmounts)
            {
                var normCode = PaymentCollection.NormalizeCollectionTypeCode(kvp.Key);
                if (kvp.Value > 0 && !PaymentCollection.IsStandardCollectionCode(normCode))
                {
                    var master = allTypes.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), normCode, StringComparison.OrdinalIgnoreCase));
                    bool isManual = master != null ? master.IsManualOnly : true;
                    if (isManual && !manualTypes.Any(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Code), normCode, StringComparison.OrdinalIgnoreCase)))
                    {
                        manualTypes.Add(new CollectionTypeMaster
                        {
                            Code = normCode,
                            DisplayName = master?.DisplayName ?? normCode.Replace("_", " "),
                            Category = master?.Category ?? "Other",
                            HasTidBatch = false,
                            HasOtp = false,
                            IsActive = false
                        });
                    }
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
                if (!isTarget)
                {
                    ManualCollections.RemoveAt(i);
                }
            }

            for (int i = 0; i < manualTypes.Count; i++)
            {
                var t = manualTypes[i];
                var norm = PaymentCollection.NormalizeCollectionTypeCode(t.Code);
                var existingRow = ManualCollections.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), norm, StringComparison.OrdinalIgnoreCase));
                double manualAmt = _dynamicManualAmounts.TryGetValue(norm, out var mVal) ? mVal : 0;

                if (existingRow != null)
                {
                    existingRow.DisplayName = t.DisplayName;
                    existingRow.Category = t.Category;
                    existingRow.Amount = manualAmt > 0 ? manualAmt : (double?)null;
                }
                else
                {
                    var newRow = new ManualCollectionEntryRow
                    {
                        Code = t.Code,
                        DisplayName = t.DisplayName,
                        Category = t.Category,
                        Amount = manualAmt > 0 ? manualAmt : (double?)null
                    };
                    newRow.OnValueChangedAction = () =>
                    {
                        if (newRow.Amount.HasValue && newRow.Amount.Value > 0)
                            _dynamicManualAmounts[norm] = newRow.Amount.Value;
                        else
                            _dynamicManualAmounts.Remove(norm);
                        RecalculateTotals();
                    };
                    ManualCollections.Insert(Math.Min(i, ManualCollections.Count), newRow);
                }
            }
            OnPropertyChanged(nameof(HasManualCollections));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to populate collection types for approval queue");
        }
    }

    private bool _isRecalculatingPineLabs;

    partial void OnPineLabsAmountChanged(double value)
    {
        if (_isRecalculatingPineLabs) return;

        double otpSum = CardSwipeDetails
            .Where(c => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), "PINELABS_CARD", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Otp))
            .Sum(c => c.Amount);
        double tidSum = ManualTidTransactions
            .Where(t => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Mode), "PINELABS_CARD", StringComparison.OrdinalIgnoreCase))
            .Sum(t => t.Amount);
        double manualNeeded = Math.Max(0, value - otpSum - tidSum);

        _dynamicManualAmounts["PINELABS_CARD"] = manualNeeded;
        RecalculateTotals();
    }

    partial void OnPhonePeMorningChanged(double value) => SyncUpiTotalFromSplits();
    partial void OnPhonePeDayChanged(double value) => SyncUpiTotalFromSplits();
    partial void OnPhonePeNightChanged(double value) => SyncUpiTotalFromSplits();

    private void SyncUpiTotalFromSplits()
    {
        OnPropertyChanged(nameof(HasPhonePeDay));
        if (_isSyncingSplits || _isLoadingSubmission) return;
        _isSyncingSplits = true;
        try
        {
            UpiAmount = PhonePeMorning + PhonePeDay + PhonePeNight;
            RecalculateTotals();
        }
        finally
        {
            _isSyncingSplits = false;
        }
    }

    partial void OnCreditCardMorningChanged(double value) => SyncCardTotalFromSplits();
    partial void OnCreditCardDayChanged(double value) => SyncCardTotalFromSplits();
    partial void OnCreditCardNightChanged(double value) => SyncCardTotalFromSplits();

    private void SyncCardTotalFromSplits()
    {
        OnPropertyChanged(nameof(HasCreditCardDay));
        if (_isSyncingSplits || _isLoadingSubmission) return;
        _isSyncingSplits = true;
        try
        {
            CardAmount = CreditCardMorning + CreditCardDay + CreditCardNight;
            RecalculateTotals();
        }
        finally
        {
            _isSyncingSplits = false;
        }
    }

    partial void OnPetroCardMorningChanged(double value) => SyncPetroCardTotalFromSplits();
    partial void OnPetroCardDayChanged(double value) => SyncPetroCardTotalFromSplits();
    partial void OnPetroCardNightChanged(double value) => SyncPetroCardTotalFromSplits();

    private void SyncPetroCardTotalFromSplits()
    {
        OnPropertyChanged(nameof(HasPetroCardDay));
        if (_isSyncingSplits || _isLoadingSubmission) return;
        _isSyncingSplits = true;
        try
        {
            PetroCardAmount = PetroCardMorning + PetroCardDay + PetroCardNight;
            RecalculateTotals();
        }
        finally
        {
            _isSyncingSplits = false;
        }
    }

    partial void OnCashAmountChanged(double value) => RecalculateTotals();
    partial void OnUpiAmountChanged(double value)
    {
        if (!_isSyncingSplits && !_isLoadingSubmission)
        {
            double currentSplitSum = PhonePeMorning + PhonePeDay + PhonePeNight;
            if (Math.Abs(currentSplitSum - value) > 0.01)
            {
                _isSyncingSplits = true;
                try
                {
                    double diff = value - currentSplitSum;
                    if (SelectedSubmission != null && SelectedSubmission.ShiftType == "B")
                        PhonePeDay = Math.Max(0, PhonePeDay + diff);
                    else if (SelectedSubmission != null && SelectedSubmission.ShiftType == "C")
                        PhonePeNight = Math.Max(0, PhonePeNight + diff);
                    else if (PhonePeNight > 0)
                        PhonePeMorning = Math.Max(0, PhonePeMorning + diff);
                    else
                        PhonePeMorning = value;
                }
                finally
                {
                    _isSyncingSplits = false;
                }
            }
        }
        RecalculateTotals();
    }

    partial void OnCardAmountChanged(double value)
    {
        if (!_isSyncingSplits && !_isLoadingSubmission)
        {
            double currentSplitSum = CreditCardMorning + CreditCardDay + CreditCardNight;
            if (Math.Abs(currentSplitSum - value) > 0.01)
            {
                _isSyncingSplits = true;
                try
                {
                    double diff = value - currentSplitSum;
                    if (SelectedSubmission != null && SelectedSubmission.ShiftType == "B")
                        CreditCardDay = Math.Max(0, CreditCardDay + diff);
                    else if (SelectedSubmission != null && SelectedSubmission.ShiftType == "C")
                        CreditCardNight = Math.Max(0, CreditCardNight + diff);
                    else if (CreditCardNight > 0)
                        CreditCardMorning = Math.Max(0, CreditCardMorning + diff);
                    else
                        CreditCardMorning = value;
                }
                finally
                {
                    _isSyncingSplits = false;
                }
            }
        }
        RecalculateTotals();
    }

    partial void OnPetroCardAmountChanged(double value)
    {
        if (!_isSyncingSplits && !_isLoadingSubmission)
        {
            double currentSplitSum = PetroCardMorning + PetroCardDay + PetroCardNight;
            if (Math.Abs(currentSplitSum - value) > 0.01)
            {
                _isSyncingSplits = true;
                try
                {
                    double diff = value - currentSplitSum;
                    if (SelectedSubmission != null && SelectedSubmission.ShiftType == "B")
                        PetroCardDay = Math.Max(0, PetroCardDay + diff);
                    else if (SelectedSubmission != null && SelectedSubmission.ShiftType == "C")
                        PetroCardNight = Math.Max(0, PetroCardNight + diff);
                    else if (PetroCardNight > 0)
                        PetroCardMorning = Math.Max(0, PetroCardMorning + diff);
                    else
                        PetroCardMorning = value;
                }
                finally
                {
                    _isSyncingSplits = false;
                }
            }
        }
        RecalculateTotals();
    }
    partial void OnCashDepositAmountChanged(double value)
    {
        if (!_isLoadingSubmission && !ShowCash1DenominationFields && Cash1 != null && Math.Abs(Cash1.TotalAmount - value) > 0.001)
        {
            Cash1.TotalAmount = value;
        }
        RecalculateTotals();
    }
    partial void OnCollectionReductionAmountChanged(double value) => RecalculateTotals();
    partial void OnCollectionReductionRemarkChanged(string? value) => RecalculateTotals();
    partial void OnOthersAmountChanged(double value) => RecalculateTotals();
    private bool _isSyncingCredit;
    partial void OnCreditAmountChanged(double value)
    {
        if (_isSyncingCredit) return;
        RecalculateTotals();
    }
    private bool _isSyncingExpenses;
    partial void OnExpenseAmountChanged(double value)
    {
        if (_isSyncingExpenses) return;
        _isSyncingExpenses = true;
        try
        {
            if (SubmissionExpenses.Count <= 1)
            {
                if (value > 0)
                {
                    if (SubmissionExpenses.Count == 1)
                    {
                        SubmissionExpenses[0].Amount = value;
                    }
                    else
                    {
                        SubmissionExpenses.Add(new Expense
                        {
                            Description = !string.IsNullOrWhiteSpace(ExpenseNotes) ? ExpenseNotes : "Expense",
                            Amount = value
                        });
                    }
                }
                else
                {
                    SubmissionExpenses.Clear();
                }
            }
            else if (Math.Abs(SubmissionExpenses.Sum(e => e.Amount) - value) > 0.01)
            {
                SubmissionExpenses.Clear();
                if (value > 0)
                {
                    SubmissionExpenses.Add(new Expense
                    {
                        Description = !string.IsNullOrWhiteSpace(ExpenseNotes) ? ExpenseNotes : "Expense",
                        Amount = value
                    });
                }
            }
        }
        finally
        {
            _isSyncingExpenses = false;
        }

        OnPropertyChanged(nameof(HasExpenses));
        RecalculateTotals();
    }
    partial void OnOverrideContinuityChanged(bool value) => NotifySubmissionStateChanged();
    partial void OnOverrideRemarkChanged(string value) => NotifySubmissionStateChanged();
    partial void OnHasContinuityWarningsChanged(bool value) => NotifySubmissionStateChanged();

    public async Task LoadProductsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var list = await context.ProductMasters.Where(p => p.IsActive).ToListAsync();
            AvailableProducts.Clear();
            foreach (var p in list)
            {
                AvailableProducts.Add(p);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load product masters");
        }
    }

    [RelayCommand]
    private void AddOilDefSale()
    {
        var firstProd = AvailableProducts.FirstOrDefault();
        var newRow = new DsmOilDefSaleRow
        {
            ProductId = firstProd?.Id ?? 0,
            ProductName = firstProd?.ProductName ?? "Castrol CRB 20W40",
            Category = firstProd?.Category ?? "Oil",
            Unit = firstProd?.Unit ?? "Litre",
            Quantity = 1.0,
            Price = firstProd?.DefaultSaleRate ?? 350.0
        };
        newRow.PropertyChanged += (s, e) => RecalculateTotals();
        OilDefSales.Add(newRow);
        RecalculateTotals();
    }

    [RelayCommand]
    private void DeleteOilDefSale(DsmOilDefSaleRow? row)
    {
        if (row != null && OilDefSales.Contains(row))
        {
            OilDefSales.Remove(row);
            RecalculateTotals();
        }
    }

    [RelayCommand]
    private void ToggleProductCatalogPanel()
    {
        ShowProductCatalogPanel = !ShowProductCatalogPanel;
    }

    [RelayCommand]
    private async Task AddProductMasterAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProductName))
        {
            MessageBox.Show("Please enter a valid Product Name.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            
            var prod = new ProductMaster
            {
                ProductName = NewProductName.Trim(),
                Category = string.IsNullOrWhiteSpace(NewProductCategory) ? "Oil" : NewProductCategory,
                Unit = string.IsNullOrWhiteSpace(NewProductUnit) ? "Litre" : NewProductUnit,
                DefaultSaleRate = NewProductDefaultRate,
                IsActive = true
            };

            context.ProductMasters.Add(prod);
            await context.SaveChangesAsync();

            NewProductName = "";
            NewProductDefaultRate = 0;
            await LoadProductsAsync();

            MessageBox.Show($"Product '{prod.ProductName}' successfully added to catalog!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add product master");
            MessageBox.Show($"Error adding product: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteProductMasterAsync(ProductMaster? product)
    {
        if (product == null) return;
        var res = MessageBox.Show($"Are you sure you want to delete product '{product.ProductName}' from catalog?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var tracked = await context.ProductMasters.FindAsync(product.Id);
            if (tracked != null)
            {
                tracked.IsActive = false; // soft delete
                await context.SaveChangesAsync();
            }
            await LoadProductsAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete product master");
            MessageBox.Show($"Error deleting product: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private List<DsmPersonalDebtor> ParsePersonalDebtorsFromMetadata(string? metadataJson)
    {
        var personalDebtors = new List<DsmPersonalDebtor>();
        if (string.IsNullOrEmpty(metadataJson)) return personalDebtors;

        try
        {
            var metadata = JsonConvert.DeserializeObject<dynamic>(metadataJson);
            if (metadata == null || metadata.personalDebtors == null) return personalDebtors;

            int sequence = 1;
            foreach (var pd in metadata.personalDebtors)
            {
                var personalDebtor = new DsmPersonalDebtor
                {
                    DsmName = SelectedSubmission?.DsmName ?? string.Empty,
                    Date = SelectedSubmission?.ShiftDate ?? DateTime.Today,
                    Time = DateTime.Now.ToString("hh:mm tt"),
                    Amount = Convert.ToDouble((object?)(pd.amount ?? 0.0)),
                    FuelProduct = (pd.fuelProduct ?? string.Empty).ToString(),
                    Remarks = (pd.remarks ?? string.Empty).ToString(),
                    PaymentMethod = (pd.paymentMethod ?? "Cash").ToString(),
                    CardTid = (pd.tid ?? string.Empty).ToString(),
                    CardBatch = (pd.batch ?? string.Empty).ToString(),
                    Denom500 = Convert.ToInt32((object?)(pd.denom500 ?? 0)),
                    Denom200 = Convert.ToInt32((object?)(pd.denom200 ?? 0)),
                    Denom100 = Convert.ToInt32((object?)(pd.denom100 ?? 0)),
                    Denom50 = Convert.ToInt32((object?)(pd.denom50 ?? 0)),
                    Denom20 = Convert.ToInt32((object?)(pd.denom20 ?? 0)),
                    Denom10 = Convert.ToInt32((object?)(pd.denom10 ?? 0)),
                    Coins = Convert.ToInt32((object?)(pd.coins ?? 0)),
                    SequenceNumber = sequence++
                };
                personalDebtors.Add(personalDebtor);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to parse personalDebtors from metadata JSON");
        }

        return personalDebtors;
    }

    private static void ShowUiMessage(string message, string caption, MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information)
    {
        if (Environment.GetEnvironmentVariable("FUELPRO_ENV") != "TEST" && System.Windows.Application.Current != null)
        {
            MessageBox.Show(message, caption, button, image);
        }
    }

    private static void FlushFocusedUiTextBox()
    {
        try
        {
            var focused = System.Windows.Input.Keyboard.FocusedElement as System.Windows.Controls.TextBox;
            focused?.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
        }
        catch { }
    }

    [RelayCommand]
    private async Task ApproveAsync()
    {
        FlushFocusedUiTextBox();
        if (SelectedSubmission == null) return;

        if (string.Equals(SelectedSubmission.Status, "Approved", StringComparison.OrdinalIgnoreCase))
        {
            ShowUiMessage("This submission has already been approved.", "Already Approved", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Completeness Guard
        if (!IsSubmissionComplete)
        {
            ShowUiMessage($"Cannot approve submission: {SubmissionIncompleteReason}", 
                "Incomplete PWA Submission", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Validation Rules
        if (HasContinuityWarnings && !OverrideContinuity)
        {
            ShowUiMessage("Cannot approve: Continuity warnings exist. Check 'Override continuity warnings' checkbox.", 
                "Continuity Mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (HasContinuityWarnings && string.IsNullOrWhiteSpace(OverrideRemark))
        {
            ShowUiMessage("Cannot approve: Overridden continuity mismatch requires a manager's override remark/note.", 
                "Remark Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (NozzleReadings.Any(r => r.ClosingReading < r.OpeningReading))
        {
            ShowUiMessage("Cannot approve: Closing reading cannot be less than opening reading.", 
                "Reading Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var unclassifiedSwipe = CardSwipeDetails.FirstOrDefault(s => s.Amount > 0 && string.IsNullOrWhiteSpace(s.Mode));
        if (unclassifiedSwipe != null)
        {
            ShowUiMessage($"Cannot approve submission: A card swipe transaction of ₹{unclassifiedSwipe.Amount:N2} has no collection type specified. Please specify the collection type before approving.", 
                "Missing Collection Type", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Strict Blocking for Unitemized Credit Execution Guard
        if (!HasValidItemizedDebtors())
        {
            ShowUiMessage(
                $"Unitemized credit detected (₹{CreditAmount:N2}). All credit sales must be itemized with customer names in the Debtor Entries Log before approval.", 
                "Unitemized Credit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusMessage = "⏳ Saving submission to local SQLite...";
        
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        try
        {
            // 1. Generate lock token for background update
            var lockId = Guid.NewGuid().ToString();
            var approvedBy = _authService.CurrentUser?.Username ?? "Manager";

            // 2. Prepare C# models for local SaveCompleteEntryAsync
            var nozzleModels = NozzleReadings.Select(n => new NozzleReading
            {
                NozzleNumber = n.NozzleId,
                FuelType = n.FuelType,
                OpeningReading = n.OpeningReading,
                ClosingReading = n.ClosingReading,
                Rate = n.Rate,
                IsManualOpeningOverride = n.HasContinuityError
            }).ToList();

            // Initialize payment fields from metadata and collections
            double cashDeposit = CashDepositAmount;
            double upiMorning = 0;
            double upiDay = 0;
            double upiNight = 0;
            double upiCardMorning = 0;
            double upiCardDay = 0;
            double upiCardNight = 0;
            double creditCardMorning = 0;
            double creditCardDay = 0;
            double creditCardNight = 0;
            double petroCardMorning = 0;
            double petroCardDay = 0;
            double petroCardNight = 0;
            double others = OthersAmount;

            string? cardTid = null;
            string? cardBatch = null;
            string? phonePeTid = null;
            string? phonePeBatch = null;
            string? petroCardTid = null;
            string? petroCardBatch = null;
            string? phonePeTidMorning = null;
            string? phonePeBatchMorning = null;
            string? phonePeTidDay = null;
            string? phonePeBatchDay = null;
            string? phonePeTidNight = null;
            string? phonePeBatchNight = null;

            string? creditCardTidMorning = null;
            string? creditCardBatchMorning = null;
            string? creditCardTidDay = null;
            string? creditCardBatchDay = null;
            string? creditCardTidNight = null;
            string? creditCardBatchNight = null;
            string? petroCardTidMorning = null;
            string? petroCardBatchMorning = null;
            string? petroCardTidDay = null;
            string? petroCardBatchDay = null;
            string? petroCardTidNight = null;
            string? petroCardBatchNight = null;

            var cycleService = _serviceProvider.GetService<IShiftCycleService>() ?? new ShiftCycleService();
            string shiftNorm = cycleService.NormalizeShiftType(SelectedSubmission.ShiftType ?? "B");
            bool isShiftA = shiftNorm == "A";
            bool isShiftB = shiftNorm == "B";
            bool isShiftC = shiftNorm == "C";
            string defaultSlot = isShiftC ? "Night" : (isShiftB ? "Day" : "Morning");

            // Transaction-level structured settlements matching list
            var settlementRecords = new List<SettlementMatchRecord>();

            if (!string.IsNullOrEmpty(SelectedSubmission.MetadataJson))
            {
                try
                {
                    var metadata = JsonConvert.DeserializeObject<dynamic>(SelectedSubmission.MetadataJson);
                    if (metadata?.settlements != null)
                    {
                        foreach (var s in metadata.settlements)
                        {
                            string pType = (string)(s.paymentType ?? "");
                            string period = (string)(s.period ?? "");
                            double amt = (double)(s.amount ?? 0.0);
                            string tid = (string)(s.tid ?? "");
                            string batch = (string)(s.batch ?? "");

                            if (amt <= 0) continue;

                            string normType = PaymentCollection.NormalizeCollectionTypeCode(pType);
                            string normSlot = PaymentCollection.ExtractSlotFromMode(period, defaultSlot);

                            settlementRecords.Add(new SettlementMatchRecord
                            {
                                PaymentType = normType,
                                Period = normSlot,
                                Amount = amt,
                                Tid = tid,
                                Batch = batch
                            });

                            if (normType == "PHONEPE")
                            {
                                switch (normSlot)
                                {
                                    case "Day":
                                        upiDay += amt; phonePeTidDay = tid; phonePeBatchDay = batch;
                                        break;
                                    case "Night":
                                        upiNight += amt; phonePeTidNight = tid; phonePeBatchNight = batch;
                                        break;
                                    default:
                                        upiMorning += amt; phonePeTidMorning = tid; phonePeBatchMorning = batch;
                                        break;
                                }
                                phonePeTid = tid;
                                phonePeBatch = batch;
                            }
                            else if (normType == "PETROCARD")
                            {
                                switch (normSlot)
                                {
                                    case "Day":
                                        petroCardDay += amt; petroCardTidDay = tid; petroCardBatchDay = batch;
                                        break;
                                    case "Night":
                                        petroCardNight += amt; petroCardTidNight = tid; petroCardBatchNight = batch;
                                        break;
                                    default:
                                        petroCardMorning += amt; petroCardTidMorning = tid; petroCardBatchMorning = batch;
                                        break;
                                }
                                petroCardTid = tid;
                                petroCardBatch = batch;
                            }
                            else if (normType == "CREDIT_CARD")
                            {
                                switch (normSlot)
                                {
                                    case "Day":
                                        creditCardDay += amt; creditCardTidDay = tid; creditCardBatchDay = batch;
                                        break;
                                    case "Night":
                                        creditCardNight += amt; creditCardTidNight = tid; creditCardBatchNight = batch;
                                        break;
                                    default:
                                        creditCardMorning += amt; creditCardTidMorning = tid; creditCardBatchMorning = batch;
                                        break;
                                }
                                cardTid = tid;
                                cardBatch = batch;
                            }
                            else
                            {
                                // Dynamic collection types (e.g. PINELABS_CARD, SBI_REDEEM) are tracked in payment.Items
                                // and must never be dumped into scalar CreditCard fields to prevent double counting.
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to parse structured settlements from metadata");
                }
            }

            // Transaction-level matching from CardSwipeDetails:
            // Match each swipe against unconsumed settlements 1-to-1.
            // If matched, the swipe is already represented by the settlement in the scalar total (prevent double-counting).
            // If NOT matched, it is a separate legitimate transaction (prevent dropping).
            foreach (var swipe in CardSwipeDetails)
            {
                string mode = swipe.Mode ?? "";
                double amount = swipe.Amount;
                if (amount <= 0 && string.IsNullOrWhiteSpace(swipe.Otp)) continue;
                string tid = swipe.Tid ?? "";
                string batch = swipe.Batch ?? "";

                string normType = PaymentCollection.NormalizeCollectionTypeCode(mode);
                string swipeSlot = PaymentCollection.ExtractSlotFromMode(mode, defaultSlot);

                var matchedSettlement = settlementRecords.FirstOrDefault(s =>
                    !s.IsConsumed &&
                    s.PaymentType.Equals(normType, StringComparison.OrdinalIgnoreCase) &&
                    s.Period.Equals(swipeSlot, StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(s.Amount - amount) < 0.01 &&
                    (string.IsNullOrWhiteSpace(s.Tid) || string.IsNullOrWhiteSpace(tid) || s.Tid.Equals(tid, StringComparison.OrdinalIgnoreCase))
                );

                if (matchedSettlement != null)
                {
                    // 1-to-1 match found: already counted via structured settlement.
                    matchedSettlement.IsConsumed = true;
                    if (!string.IsNullOrWhiteSpace(tid))
                    {
                        if (normType == "PHONEPE") { phonePeTid = tid; phonePeBatch = batch; }
                        else if (normType == "PETROCARD") { petroCardTid = tid; petroCardBatch = batch; }
                        else if (normType == "CREDIT_CARD") { cardTid = tid; cardBatch = batch; }
                    }
                }
                else
                {
                    // Not matched by any settlement: losslessly aggregate into scalar totals
                    if (normType == "PHONEPE_CARD")
                    {
                        if (swipeSlot == "Night")
                        {
                            upiCardNight += amount;
                            phonePeTidNight = tid;
                            phonePeBatchNight = batch;
                        }
                        else if (swipeSlot == "Day")
                        {
                            upiCardDay += amount;
                            phonePeTidDay = tid;
                            phonePeBatchDay = batch;
                        }
                        else
                        {
                            upiCardMorning += amount;
                            phonePeTidMorning = tid;
                            phonePeBatchMorning = batch;
                        }
                        phonePeTid = tid;
                        phonePeBatch = batch;
                    }
                    else if (normType == "PHONEPE")
                    {
                        if (swipeSlot == "Night")
                        {
                            upiNight += amount;
                            phonePeTidNight = tid;
                            phonePeBatchNight = batch;
                        }
                        else if (swipeSlot == "Day")
                        {
                            upiDay += amount;
                            phonePeTidDay = tid;
                            phonePeBatchDay = batch;
                        }
                        else
                        {
                            upiMorning += amount;
                            phonePeTidMorning = tid;
                            phonePeBatchMorning = batch;
                        }
                        phonePeTid = tid;
                        phonePeBatch = batch;
                    }
                    else if (normType == "PETROCARD")
                    {
                        if (swipeSlot == "Night")
                        {
                            petroCardNight += amount;
                            petroCardTidNight = tid;
                            petroCardBatchNight = batch;
                        }
                        else if (swipeSlot == "Day")
                        {
                            petroCardDay += amount;
                            petroCardTidDay = tid;
                            petroCardBatchDay = batch;
                        }
                        else
                        {
                            petroCardMorning += amount;
                            petroCardTidMorning = tid;
                            petroCardBatchMorning = batch;
                        }
                        petroCardTid = tid;
                        petroCardBatch = batch;
                    }
                    else if (normType == "CREDIT_CARD")
                    {
                        if (swipeSlot == "Night")
                        {
                            creditCardNight += amount;
                            creditCardTidNight = tid;
                            creditCardBatchNight = batch;
                        }
                        else if (swipeSlot == "Day")
                        {
                            creditCardDay += amount;
                            creditCardTidDay = tid;
                            creditCardBatchDay = batch;
                        }
                        else
                        {
                            creditCardMorning += amount;
                            creditCardTidMorning = tid;
                            creditCardBatchMorning = batch;
                        }
                        cardTid = tid;
                        cardBatch = batch;
                    }
                    else
                    {
                        // Any dynamic types handled purely via payment.Items to prevent misclassification.
                    }
                }
            }

            // Honor manager UI edits and explicit split values
            if (PhonePeMorning > 0 || PhonePeDay > 0 || PhonePeNight > 0)
            {
                upiMorning = PhonePeMorning;
                upiDay = PhonePeDay;
                upiNight = PhonePeNight;
            }
            else
            {
                double totalUpi = upiMorning + upiDay + upiNight;
                if (Math.Abs(totalUpi - UpiAmount) > 0.01)
                {
                    double diff = UpiAmount - totalUpi;
                    if (isShiftC) upiNight += diff;
                    else if (isShiftB) upiDay += diff;
                    else upiMorning += diff;
                }
            }

            if (CreditCardMorning > 0 || CreditCardDay > 0 || CreditCardNight > 0)
            {
                creditCardMorning = CreditCardMorning;
                creditCardDay = CreditCardDay;
                creditCardNight = CreditCardNight;
            }
            else
            {
                double totalCC = creditCardMorning + creditCardDay + creditCardNight;
                if (Math.Abs(totalCC - CardAmount) > 0.01)
                {
                    double diff = CardAmount - totalCC;
                    if (isShiftC) creditCardNight += diff;
                    else if (isShiftB) creditCardDay += diff;
                    else creditCardMorning += diff;
                }
            }

            if (PetroCardMorning > 0 || PetroCardDay > 0 || PetroCardNight > 0)
            {
                petroCardMorning = PetroCardMorning;
                petroCardDay = PetroCardDay;
                petroCardNight = PetroCardNight;
            }
            else
            {
                double totalPetro = petroCardMorning + petroCardDay + petroCardNight;
                if (Math.Abs(totalPetro - PetroCardAmount) > 0.01)
                {
                    double diff = PetroCardAmount - totalPetro;
                    if (isShiftC) petroCardNight += diff;
                    else if (isShiftB) petroCardDay += diff;
                    else petroCardMorning += diff;
                }
            }

            var payment = new PaymentCollection
            {
                CashDeposit = cashDeposit,
                PhonePeMorning = upiMorning,
                PhonePeDay = upiDay,
                PhonePeNight = upiNight,
                PhonePeCardMorning = upiCardMorning,
                PhonePeCardDay = upiCardDay,
                PhonePeCardNight = upiCardNight,
                CreditCardMorning = creditCardMorning,
                CreditCardDay = creditCardDay,
                CreditCardNight = creditCardNight,
                PetroCardMorning = petroCardMorning,
                PetroCardDay = petroCardDay,
                PetroCardNight = petroCardNight,
                Others = others,
                CollectionReduction = CollectionReductionAmount,
                CollectionReductionRemark = CollectionReductionRemark,
                CardTid = cardTid ?? (isShiftC ? creditCardTidNight : (isShiftB ? creditCardTidDay : creditCardTidMorning)),
                CardBatch = cardBatch ?? (isShiftC ? creditCardBatchNight : (isShiftB ? creditCardBatchDay : creditCardBatchMorning)),
                PhonePeTid = phonePeTid ?? (isShiftC ? phonePeTidNight : (isShiftB ? phonePeTidDay : phonePeTidMorning)),
                PhonePeBatch = phonePeBatch ?? (isShiftC ? phonePeBatchNight : (isShiftB ? phonePeBatchDay : phonePeBatchMorning)),
                PetroCardTid = petroCardTid ?? (isShiftC ? petroCardTidNight : (isShiftB ? petroCardTidDay : petroCardTidMorning)),
                PetroCardBatch = petroCardBatch ?? (isShiftC ? petroCardBatchNight : (isShiftB ? petroCardBatchDay : petroCardBatchMorning)),
                PhonePeTidMorning = phonePeTidMorning,
                PhonePeBatchMorning = phonePeBatchMorning,
                PhonePeTidDay = phonePeTidDay,
                PhonePeBatchDay = phonePeBatchDay,
                PhonePeTidNight = phonePeTidNight,
                PhonePeBatchNight = phonePeBatchNight,
                CreditCardTidMorning = creditCardTidMorning,
                CreditCardBatchMorning = creditCardBatchMorning,
                CreditCardTidDay = creditCardTidDay,
                CreditCardBatchDay = creditCardBatchDay,
                CreditCardTidNight = creditCardTidNight,
                CreditCardBatchNight = creditCardBatchNight,
                PetroCardTidMorning = petroCardTidMorning,
                PetroCardBatchMorning = petroCardBatchMorning,
                PetroCardTidDay = petroCardTidDay,
                PetroCardBatchDay = petroCardBatchDay,
                PetroCardTidNight = petroCardTidNight,
                PetroCardBatchNight = petroCardBatchNight
            };

            foreach (var swipe in CardSwipeDetails)
            {
                if (swipe.Amount <= 0 && string.IsNullOrWhiteSpace(swipe.Otp)) continue;
                string mode = swipe.Mode ?? "";
                string itemSlot = PaymentCollection.ExtractSlotFromMode(mode, defaultSlot);

                string typeCode = PaymentCollection.NormalizeCollectionTypeCode(mode);
                if (string.IsNullOrWhiteSpace(typeCode))
                {
                    typeCode = mode;
                }
                var swipeMaster = _availableCollectionTypes?.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), typeCode, StringComparison.OrdinalIgnoreCase));
                if (swipeMaster != null && swipeMaster.IsManualOnly) continue;

                payment.Items.Add(new PaymentCollectionItem
                {
                    CollectionTypeCode = typeCode,
                    Amount = swipe.Amount,
                    Tid = swipe.Tid,
                    Batch = swipe.Batch,
                    Otp = swipe.Otp,
                    Slot = itemSlot
                });
            }

            // Ingest itemized TID transactions (excluding manual-only types)
            if (ManualTidTransactions.Count > 0)
            {
                foreach (var tx in ManualTidTransactions)
                {
                    if (tx.Amount <= 0) continue;
                    var normCode = PaymentCollection.NormalizeCollectionTypeCode(tx.Mode);
                    var master = _availableCollectionTypes?.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), normCode, StringComparison.OrdinalIgnoreCase));
                    if (master != null && master.IsManualOnly) continue;

                    payment.Items.Add(new PaymentCollectionItem
                    {
                        TransactionId = string.IsNullOrWhiteSpace(tx.TransactionId) ? Guid.NewGuid().ToString("D") : tx.TransactionId,
                        CollectionTypeCode = normCode,
                        Amount = tx.Amount,
                        Tid = tx.Tid,
                        Batch = tx.Batch,
                        Slot = string.IsNullOrWhiteSpace(tx.Slot) ? defaultSlot : tx.Slot,
                        Otp = ""
                    });
                }
            }

            // Ingest ManualCollections
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
                        Slot = defaultSlot,
                        Otp = null
                    });
                }
            }

            // Ingest any unconsumed dynamic settlements that might not have been in CardSwipeDetails
            foreach (var unconsumed in settlementRecords.Where(s => !s.IsConsumed && !PaymentCollection.IsStandardCollectionCode(s.PaymentType)))
            {
                var existing = payment.Items.FirstOrDefault(i =>
                    string.Equals(PaymentCollection.NormalizeCollectionTypeCode(i.CollectionTypeCode), unconsumed.PaymentType, StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(i.Amount - unconsumed.Amount) < 0.01);
                if (existing == null)
                {
                    payment.Items.Add(new PaymentCollectionItem
                    {
                        CollectionTypeCode = unconsumed.PaymentType,
                        Amount = unconsumed.Amount,
                        Tid = unconsumed.Tid,
                        Batch = unconsumed.Batch,
                        Slot = unconsumed.Period,
                        Otp = ""
                    });
                }
            }

            // Ingest manual dynamic amounts from _dynamicManualAmounts ONLY for legacy submissions where NO TID/OTP exist
            foreach (var kvp in _dynamicManualAmounts)
            {
                string normCode = PaymentCollection.NormalizeCollectionTypeCode(kvp.Key);
                double manualAmt = kvp.Value;
                if (manualAmt <= 0.009) continue;

                bool hasTidOrOtp = payment.Items.Any(i =>
                    string.Equals(PaymentCollection.NormalizeCollectionTypeCode(i.CollectionTypeCode), normCode, StringComparison.OrdinalIgnoreCase)
                    && (!string.IsNullOrWhiteSpace(i.Otp) || !string.IsNullOrWhiteSpace(i.Tid) || !string.IsNullOrWhiteSpace(i.Batch)
                        || !string.Equals(i.TransactionId, $"manual-scalar-{normCode}", StringComparison.OrdinalIgnoreCase)));

                if (hasTidOrOtp) continue;

                var existingManual = payment.Items.FirstOrDefault(i =>
                    string.Equals(PaymentCollection.NormalizeCollectionTypeCode(i.CollectionTypeCode), normCode, StringComparison.OrdinalIgnoreCase));

                if (existingManual != null)
                {
                    existingManual.Amount = manualAmt;
                }
                else
                {
                    _dynamicManualTids.TryGetValue(normCode, out var tid);
                    _dynamicManualBatches.TryGetValue(normCode, out var batch);
                    _dynamicManualSlots.TryGetValue(normCode, out var slot);

                    payment.Items.Add(new PaymentCollectionItem
                    {
                        TransactionId = $"manual-scalar-{normCode}",
                        CollectionTypeCode = normCode,
                        Amount = manualAmt,
                        Tid = tid,
                        Batch = batch,
                        Slot = string.IsNullOrWhiteSpace(slot) ? defaultSlot : slot,
                        Otp = ""
                    });
                }
            }

            // Remove any manual-scalar items if TID or OTP items are present for that type
            var toRemove = payment.Items.Where(i =>
                !string.IsNullOrWhiteSpace(i.TransactionId) &&
                i.TransactionId.StartsWith("manual-scalar-", StringComparison.OrdinalIgnoreCase) &&
                payment.Items.Any(other =>
                    other != i &&
                    string.Equals(PaymentCollection.NormalizeCollectionTypeCode(other.CollectionTypeCode),
                                  PaymentCollection.NormalizeCollectionTypeCode(i.CollectionTypeCode),
                                  StringComparison.OrdinalIgnoreCase))).ToList();

            foreach (var rem in toRemove)
            {
                payment.Items.Remove(rem);
            }


            if (payment.Items.Count > 0)
            {
                payment.DynamicItemsJson = JsonConvert.SerializeObject(payment.Items.Select(i => new
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

            // Build Debtor Entries
            var debitModels = new List<DebitEntry>();
            foreach (var dbEntry in DebtorEntries)
            {
                debitModels.Add(new DebitEntry
                {
                    DebtorName = dbEntry.DebtorName,
                    Amount = dbEntry.Amount,
                    VehicleNumber = string.IsNullOrWhiteSpace(dbEntry.VehicleNumber) ? null : dbEntry.VehicleNumber.Trim(),
                    SlipNumber = string.IsNullOrWhiteSpace(dbEntry.SlipNumber) ? null : dbEntry.SlipNumber.Trim(),
                    ChequeNo = string.IsNullOrWhiteSpace(dbEntry.SlipNumber) ? null : dbEntry.SlipNumber.Trim(),
                    Remarks = string.IsNullOrWhiteSpace(dbEntry.Remarks) ? null : dbEntry.Remarks.Trim(),
                    EntryTime = string.IsNullOrWhiteSpace(dbEntry.EntryTime) ? null : dbEntry.EntryTime.Trim(),
                    PaymentMethod = "Credit"
                });
            }

            // Personal debtors are managed under the dedicated Personal Ledger / drawings register
            var personalDebtors = new List<DsmPersonalDebtor>();

            // Parse cash denominations, connected pump, and testing entries
            int denom500 = 0, denom200 = 0, denom100 = 0, denom50 = 0, denom20 = 0, denom10 = 0, coins = 0;
            int cash1denom500 = 0, cash1denom200 = 0, cash1denom100 = 0, cash1denom50 = 0, cash1denom20 = 0, cash1denom10 = 0, cash1coins = 0;
            var connectedPumpIds = new List<int>();
            if (SelectedSubmission?.ConnectedPumpIds != null)
            {
                connectedPumpIds.AddRange(SelectedSubmission.ConnectedPumpIds);
            }
            int? connectedPumpId = connectedPumpIds.Count > 0 ? connectedPumpIds[0] : null;
            var testingModels = new List<TestingEntry>();

            if (!string.IsNullOrEmpty(SelectedSubmission.MetadataJson))
            {
                try
                {
                    var metadata = JsonConvert.DeserializeObject<dynamic>(SelectedSubmission.MetadataJson);
                    if (metadata != null)
                    {
                        if (metadata.cashDenominations != null)
                        {
                            denom500 = (int)(metadata.cashDenominations.denom500 ?? 0);
                            denom200 = (int)(metadata.cashDenominations.denom200 ?? 0);
                            denom100 = (int)(metadata.cashDenominations.denom100 ?? 0);
                            denom50 = (int)(metadata.cashDenominations.denom50 ?? 0);
                            denom20 = (int)(metadata.cashDenominations.denom20 ?? 0);
                            denom10 = (int)(metadata.cashDenominations.denom10 ?? 0);
                            coins = (int)(metadata.cashDenominations.coins ?? 0);
                        }
                        if (metadata.cash1Denominations != null)
                        {
                            cash1denom500 = (int)(metadata.cash1Denominations.denom500 ?? 0);
                            cash1denom200 = (int)(metadata.cash1Denominations.denom200 ?? 0);
                            cash1denom100 = (int)(metadata.cash1Denominations.denom100 ?? 0);
                            cash1denom50 = (int)(metadata.cash1Denominations.denom50 ?? 0);
                            cash1denom20 = (int)(metadata.cash1Denominations.denom20 ?? 0);
                            cash1denom10 = (int)(metadata.cash1Denominations.denom10 ?? 0);
                            cash1coins = (int)(metadata.cash1Denominations.coins ?? 0);
                        }
                        if (metadata.connectedPumpIds != null)
                        {
                            try
                            {
                                var ids = ((Newtonsoft.Json.Linq.JArray)metadata.connectedPumpIds).ToObject<List<int>>();
                                if (ids != null)
                                {
                                    foreach (var id in ids)
                                    {
                                        if (id > 0 && id != SelectedSubmission.PumpId && !connectedPumpIds.Contains(id))
                                            connectedPumpIds.Add(id);
                                    }
                                }
                            }
                            catch {}
                        }
                        else if (metadata.connectedPumpId != null)
                        {
                            int scalarId = (int)metadata.connectedPumpId;
                            if (scalarId > 0 && scalarId != SelectedSubmission.PumpId && !connectedPumpIds.Contains(scalarId))
                                connectedPumpIds.Add(scalarId);
                        }

                        if (metadata.testingEntries != null)
                        {
                            foreach (var test in metadata.testingEntries)
                            {
                                int nozzleId = (int)(test.nozzleId ?? 0);
                                var nozzleRow = nozzleModels.FirstOrDefault(n => n.NozzleNumber == nozzleId);
                                double rate = nozzleRow != null ? nozzleRow.Rate : 0.0;
                                double litres = (double)(test.amount ?? 0.0);
                                testingModels.Add(new TestingEntry
                                {
                                    FuelType = nozzleId.ToString(),
                                    Litres = litres,
                                    Rate = rate,
                                    Amount = litres * rate
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to parse cashDenominations/cash1Denominations/connectedPumpId/testingEntries from metadata JSON");
                }
            }

            if (connectedPumpIds.Count == 0)
            {
                var cfgConn = await GetConnectedPumpsForPrimaryAsync(SelectedSubmission.PumpId);
                foreach (var cp in cfgConn)
                {
                    if (cp > 0 && cp != SelectedSubmission.PumpId && !connectedPumpIds.Contains(cp))
                        connectedPumpIds.Add(cp);
                }
            }
            connectedPumpId = connectedPumpIds.Count > 0 ? connectedPumpIds[0] : null;

            var expenseModels = SubmissionExpenses.Count > 0
                ? SubmissionExpenses.ToList()
                : new List<Expense>();

            if (expenseModels.Count == 0 && !string.IsNullOrEmpty(SelectedSubmission.MetadataJson))
            {
                try
                {
                    var metadata = JsonConvert.DeserializeObject<dynamic>(SelectedSubmission.MetadataJson);
                    var expRawList = metadata?.expenseEntries ?? metadata?.expensesList;
                    if (expRawList != null)
                    {
                        foreach (var exp in expRawList)
                        {
                            string desc = (exp.description ?? exp.Description ?? exp.notes ?? exp.Notes ?? "DSM PWA Expense").ToString();
                            double amt = Convert.ToDouble((object?)(exp.amount ?? exp.Amount ?? 0.0));
                            if (amt > 0)
                            {
                                expenseModels.Add(new Expense { Description = desc, Amount = amt });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to parse expenseEntries from metadata JSON");
                }
            }
            if (expenseModels.Count == 0 && ExpenseAmount > 0)
            {
                expenseModels.Add(new Expense { Description = string.IsNullOrEmpty(ExpenseNotes) ? "DSM PWA Expense" : ExpenseNotes, Amount = ExpenseAmount });
            }

            bool cash1VmHasAnyValue = Cash1.Denom500.HasValue || Cash1.Denom200.HasValue || Cash1.Denom100.HasValue
                || Cash1.Denom50.HasValue || Cash1.Denom20.HasValue || Cash1.Denom10.HasValue || Cash1.Coins.HasValue;
            if (!cash1VmHasAnyValue && (cash1denom500 > 0 || cash1denom200 > 0 || cash1denom100 > 0 || cash1denom50 > 0 || cash1denom20 > 0 || cash1denom10 > 0 || cash1coins > 0))
            {
                Cash1.Denom500 = cash1denom500;
                Cash1.Denom200 = cash1denom200;
                Cash1.Denom100 = cash1denom100;
                if (cash1denom50 > 0) Cash1.Denom50 = cash1denom50;
                if (cash1denom20 > 0) Cash1.Denom20 = cash1denom20;
                if (cash1denom10 > 0) Cash1.Denom10 = cash1denom10;
                if (cash1coins > 0) Cash1.Coins = cash1coins;
            }

            double finalCash1Total = Cash1.HasDenominationCounts
                ? Cash1.TotalAmount
                : (CashDepositAmount > 0 ? CashDepositAmount : Cash1.TotalAmount);

            // Cash1 is Cash 1, Cash2 is Cash in Hand
            var cashModels = new List<CashDenomination>
            {
                new()
                {
                    CashType = "Cash1",
                    TotalAmount = finalCash1Total,
                    Denom500 = Cash1.Denom500 ?? 0,
                    Denom200 = Cash1.Denom200 ?? 0,
                    Denom100 = Cash1.Denom100 ?? 0,
                    Denom50 = Cash1.Denom50 ?? 0,
                    Denom20 = Cash1.Denom20 ?? 0,
                    Denom10 = Cash1.Denom10 ?? 0,
                    Coins = Cash1.Coins ?? 0
                },
                new() 
                { 
                    CashType = "Cash2", 
                    TotalAmount = CashAmount,
                    Denom500 = Cash2.Denom500 ?? 0,
                    Denom200 = Cash2.Denom200 ?? 0,
                    Denom100 = Cash2.Denom100 ?? 0,
                    Denom50 = Cash2.Denom50 ?? 0,
                    Denom20 = Cash2.Denom20 ?? 0,
                    Denom10 = Cash2.Denom10 ?? 0,
                    Coins = Cash2.Coins ?? 0
                }
            };

            if (IsCash3Enabled || Cash3.TotalAmount > 0)
            {
                cashModels.Add(new CashDenomination
                {
                    CashType = "Cash3",
                    TotalAmount = Cash3.TotalAmount,
                    Denom500 = Cash3.Denom500 ?? 0,
                    Denom200 = Cash3.Denom200 ?? 0,
                    Denom100 = Cash3.Denom100 ?? 0,
                    Denom50 = Cash3.Denom50 ?? 0,
                    Denom20 = Cash3.Denom20 ?? 0,
                    Denom10 = Cash3.Denom10 ?? 0,
                    Coins = Cash3.Coins ?? 0
                });
            }

            // 3. Save locally via single database transaction (authoritative source of truth)
            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
            var alreadyApproved = await context.DsmApprovalAudits.AnyAsync(a => a.SubmissionId == SelectedSubmission.Id);
            if (alreadyApproved)
            {
                ShowUiMessage("This shift submission has already been approved and committed by another workstation.", "Already Approved", MessageBoxButton.OK, MessageBoxImage.Information);
                await RefreshQueueAsync();
                return;
            }

            var submissionId = SelectedSubmission.Id;
            var syncConfig = _serviceProvider.GetService<FuelPro.Sync.SyncConfigService>();
            var syncSettings = syncConfig != null ? await syncConfig.GetSettingsAsync() : null;
            var machineId = !string.IsNullOrEmpty(syncSettings?.MachineId) ? syncSettings.MachineId : Environment.MachineName;

            // Multi-Computer Concurrency Guard & Single-PC Remote Verification:
            var lockResult = await _supabaseService.ClaimSubmissionWithFencingAsync(submissionId, machineId, approvedBy, lockId);
            string? claimToken = lockResult.Data?.ClaimToken;
            int claimVersion = lockResult.Data?.ClaimVersion ?? 0;
            bool awaitingCloudReconciliation = false;

            if (!lockResult.Success)
            {
                var claimCode = lockResult.Data?.Code;
                bool isRemoteConflictOrIneligible =
                    claimCode is "CONCURRENCY_CONFLICT" or "REMOTE_ALREADY_PROCESSED" or "REMOTE_DELETED" or "REMOTE_MISSING" ||
                    (lockResult.Error != null && (
                        lockResult.Error.Contains("Concurrency conflict", StringComparison.OrdinalIgnoreCase) ||
                        lockResult.Error.Contains("Remote status conflict", StringComparison.OrdinalIgnoreCase) ||
                        lockResult.Error.Contains("already", StringComparison.OrdinalIgnoreCase)));

                if (isRemoteConflictOrIneligible)
                {
                    _logger.Warning("Cloud approval check rejected submission {SubId} (Code={Code}): {Error}", submissionId, claimCode, lockResult.Error);
                    ShowUiMessage($"Cannot approve submission:\n{lockResult.Error}\n\nQueue will be refreshed so local data stays consistent with cloud.", "Approval Conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
                    await RefreshQueueAsync();
                    return;
                }

                if (syncSettings != null && syncSettings.MultiComputerEnabled)
                {
                    _logger.Warning("Cloud approval unreachable in multi-computer mode for submission {SubId}: {Error}", submissionId, lockResult.Error);
                    ShowUiMessage($"Cannot approve in Multi-Computer mode while offline:\n{lockResult.Error}\n\nPlease check network connectivity so submission status can be coordinated with other workstations.", "Network Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                awaitingCloudReconciliation = true;
                _logger.Warning("Cloud verification unavailable in single-PC mode for {SubId}: {Error}. Proceeding with offline local commit marked awaiting cloud reconciliation.", submissionId, lockResult.Error);
            }
            else if (!string.IsNullOrEmpty(claimToken))
            {
                // Pre-processing ownership check immediately before opening local SQLite transaction
                var preOwnership = await _supabaseService.RenewLeaseAsync(submissionId, machineId, claimToken, claimVersion);
                if (!preOwnership.Success)
                {
                    _logger.Warning("Pre-processing ownership check failed for {SubId}: {Error}", submissionId, preOwnership.Error);
                    ShowUiMessage($"Cannot proceed with approval:\n{preOwnership.Error}\n\nThe approval lease could not be validated with the cloud coordinator.", "Ownership Check Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    await RefreshQueueAsync();
                    return;
                }
            }

            transaction = await context.Database.BeginTransactionAsync();
                var localSaveResult = await _dsmEntryService.SaveCompleteEntryWithContextAsync(
                    context,
                    SelectedSubmission.ShiftDate,
                    SelectedSubmission.ShiftType,
                    SelectedSubmission.DsmName,
                    SelectedSubmission.PumpId,
                    nozzleModels,
                    payment,
                    debitModels,
                    testingModels,
                    expenseModels,
                    cashModels,
                    connectedPumpId: connectedPumpId,
                    personalDebtors: personalDebtors,
                    khandharePetroleumEntries: KhandhareEntries.ToList(),
                    qrPayments: QrPayments.ToList(),
                    connectedPumpIds: connectedPumpIds
                );

                if (!localSaveResult.Success)
                {
                    await transaction.RollbackAsync();
                    ShowUiMessage($"WPF local save failed: {localSaveResult.Error}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    await RefreshQueueAsync();
                    return;
                }

                var savedEntry = localSaveResult.Data!;

                // Process Oil & DEF Sales from edited Owner collection
                if (OilDefSales.Count > 0)
                {
                    try
                    {
                        foreach (var sale in OilDefSales)
                        {
                            if (sale.Quantity <= 0) continue;
                            int productId = sale.ProductId;
                            if (productId <= 0)
                            {
                                var matchedProd = await context.ProductMasters.FirstOrDefaultAsync(p => p.ProductName == sale.ProductName);
                                if (matchedProd != null) productId = matchedProd.Id;
                            }
                            if (productId <= 0) continue;

                            var productMaster = await context.ProductMasters.FindAsync(productId);
                            if (productMaster == null) continue;

                            double price = sale.Price;
                            double quantity = sale.Quantity;

                            await DsmOilDefSalePersistence.AddOrAccumulateAsync(
                                context, savedEntry.ShiftId, SelectedSubmission.ShiftDate, productMaster, quantity, price);
                            await RecalculateRunningBalancesAsync(context, productId, SelectedSubmission.ShiftDate.Date);
                        }

                        // Sync edited OilDefSales back to MetadataJson
                        try
                        {
                            var metaObj = string.IsNullOrEmpty(SelectedSubmission.MetadataJson)
                                ? new Newtonsoft.Json.Linq.JObject()
                                : Newtonsoft.Json.Linq.JObject.Parse(SelectedSubmission.MetadataJson);

                            var salesArray = Newtonsoft.Json.Linq.JArray.FromObject(OilDefSales.Select(s => new
                            {
                                productId = s.ProductId,
                                productName = s.ProductName,
                                category = s.Category,
                                unit = s.Unit,
                                quantity = s.Quantity,
                                price = s.Price,
                                total = s.Total
                            }));
                            metaObj["oilDefSales"] = salesArray;
                            SelectedSubmission.MetadataJson = metaObj.ToString(Newtonsoft.Json.Formatting.None);
                        }
                        catch {}
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Failed to save Oil & DEF sales during approval");
                        throw; // Escalate to rollback transaction
                    }
                }

                // Process Debtor Repayments received by DSM into CreditorRepayments table
                if (DebtorRepayments.Count > 0)
                {
                    var shiftDate = SelectedSubmission.ShiftDate.Date;
                    var shiftNum = SelectedSubmission.ShiftType;
                    var dsmName = SelectedSubmission.DsmName;
                    var dsmTag = $"DSM: {dsmName}";
                    var subId = SelectedSubmission.Id;

                    var existingReps = await context.CreditorRepayments
                        .Where(r => (r.ChequeNo != null && r.ChequeNo.Contains($"[Sub:{subId:N}"))
                                 || (r.RepaymentDate.Date == shiftDate && r.ShiftNumber == shiftNum && r.ChequeNo != null && r.ChequeNo.Contains(dsmTag)))
                        .ToListAsync();

                    var existingRepMap = existingReps.ToDictionary(r => r.CreditorRepaymentId);
                    var claimedIds = new HashSet<int>();

                    for (int i = 0; i < DebtorRepayments.Count; i++)
                    {
                        var rep = DebtorRepayments[i];
                        if ((rep.Amount ?? 0) <= 0 || string.IsNullOrWhiteSpace(rep.DebtorName)) continue;

                        string subTag = RepaymentIdentityHelper.BuildSubmissionTag(subId, i);
                        string expectedCheque = RepaymentIdentityHelper.FormatChequeNo(rep.Notes, dsmName, subTag);

                        CreditorRepayment? targetEntity = null;

                        // 1. Primary: match by CreditorRepaymentId
                        if (rep.CreditorRepaymentId > 0 && existingRepMap.TryGetValue(rep.CreditorRepaymentId, out var byId))
                        {
                            targetEntity = byId;
                        }

                        // 2. Secondary: match by submission identity tag
                        if (targetEntity == null)
                        {
                            targetEntity = existingReps.FirstOrDefault(r => 
                                !claimedIds.Contains(r.CreditorRepaymentId)
                                && RepaymentIdentityHelper.MatchesSubmission(r.ChequeNo, subId, i));
                        }

                        // 3. Fallback: match by unlinked row without stealing another row's record
                        if (targetEntity == null)
                        {
                            targetEntity = existingReps.FirstOrDefault(r =>
                                !claimedIds.Contains(r.CreditorRepaymentId)
                                && !DebtorRepayments.Any(d => d.CreditorRepaymentId == r.CreditorRepaymentId)
                                && string.Equals(r.CreditorName, rep.DebtorName.Trim(), StringComparison.OrdinalIgnoreCase)
                                && Math.Abs(r.Amount - (rep.Amount ?? 0)) < 0.01
                                && string.Equals(r.PaymentMode ?? "Cash", string.IsNullOrWhiteSpace(rep.PaymentMode) ? "Cash" : rep.PaymentMode, StringComparison.OrdinalIgnoreCase));
                        }

                        if (targetEntity != null)
                        {
                            targetEntity.CreditorName = rep.DebtorName.Trim();
                            targetEntity.Amount = rep.Amount ?? 0;
                            targetEntity.PaymentMode = string.IsNullOrWhiteSpace(rep.PaymentMode) ? "Cash" : rep.PaymentMode;
                            targetEntity.RepaymentDate = shiftDate;
                            targetEntity.ShiftNumber = shiftNum;
                            targetEntity.ChequeNo = expectedCheque;

                            rep.CreditorRepaymentId = targetEntity.CreditorRepaymentId;
                            claimedIds.Add(targetEntity.CreditorRepaymentId);
                        }
                        else
                        {
                            var repaymentEntity = new CreditorRepayment
                            {
                                CreditorName = rep.DebtorName.Trim(),
                                Amount = rep.Amount ?? 0,
                                PaymentMode = string.IsNullOrWhiteSpace(rep.PaymentMode) ? "Cash" : rep.PaymentMode,
                                RepaymentDate = shiftDate,
                                ShiftNumber = shiftNum,
                                ChequeNo = expectedCheque
                            };
                            await context.CreditorRepayments.AddAsync(repaymentEntity);
                            await context.SaveChangesAsync();
                            rep.CreditorRepaymentId = repaymentEntity.CreditorRepaymentId;
                            claimedIds.Add(repaymentEntity.CreditorRepaymentId);
                            existingReps.Add(repaymentEntity);
                            existingRepMap[repaymentEntity.CreditorRepaymentId] = repaymentEntity;
                        }
                    }
                }
                
                var originalData = new
                {
                    Readings = NozzleReadings.Select(r => new
                    {
                        r.NozzleId,
                        r.FuelType,
                        r.OpeningReading,
                        r.ClosingReading,
                        SubmittedRate = (r.SubmittedRate > 0 ? r.SubmittedRate : r.Rate),
                        Rate = (r.SubmittedRate > 0 ? r.SubmittedRate : r.Rate),
                        r.SaleLitres,
                        Amount = r.SaleLitres * (r.SubmittedRate > 0 ? r.SubmittedRate : r.Rate)
                    }),
                    Collections = new
                    {
                        Cash = CashAmount,
                        UPI = UpiAmount,
                        Card = CardAmount,
                        Credit = CreditAmount,
                        Expense = ExpenseAmount,
                        ExpenseNotes,
                        CollectionReduction = CollectionReductionAmount,
                        CollectionReductionRemark = CollectionReductionRemark
                    },
                    debtorEntries = DebtorEntries.Select(d => new
                    {
                        d.DebtorName,
                        d.Amount,
                        d.VehicleNumber,
                        d.SlipNumber,
                        d.Remarks,
                        d.EntryTime
                    }),
                    khandhareEntries = KhandhareEntries.Select(k => new { k.Name, k.VehicleNumber, k.SlipNumber, k.Amount }),
                    debtorRepayments = DebtorRepayments.Select(r => new { r.DebtorName, r.Amount, r.PaymentMode, r.Notes })
                };

                var rateAdjustments = NozzleReadings
                    .Where(n => n.SubmittedRate > 0 && Math.Abs(n.SubmittedRate - n.Rate) > 0.001)
                    .Select(n => $"Nozzle #{n.NozzleId} ({n.FuelType}): rate adjusted from ₹{n.SubmittedRate:F2} to ₹{n.Rate:F2}")
                    .ToList();

                var approvedData = new
                {
                    DsmEntryId = savedEntry.DsmEntryId,
                    ShiftId = savedEntry.ShiftId,
                    savedEntry.GrossSales,
                    savedEntry.TotalCollection,
                    savedEntry.Mismatch,
                    Readings = NozzleReadings.Select(r => new
                    {
                        r.NozzleId,
                        r.FuelType,
                        r.OpeningReading,
                        r.ClosingReading,
                        ApprovedRate = r.Rate,
                        Rate = r.Rate,
                        r.SaleLitres,
                        r.Amount
                    }),
                    RateAdjustments = rateAdjustments
                };

                string auditRemarks = HasContinuityWarnings ? $"Override Continuity: {OverrideRemark}" : "Approved as-is.";
                if (rateAdjustments.Count > 0)
                {
                    auditRemarks += $" | Rate adjustments: {string.Join("; ", rateAdjustments)}";
                }
                if (awaitingCloudReconciliation)
                {
                    auditRemarks += " | [Awaiting Cloud Reconciliation]";
                }

                var audit = new DsmApprovalAudit
                {
                    SubmissionId = SelectedSubmission.Id,
                    OriginalDataJson = JsonConvert.SerializeObject(originalData),
                    ApprovedDataJson = JsonConvert.SerializeObject(approvedData),
                    ApprovedBy = approvedBy,
                    ApprovedAt = DateTime.Now,
                    Remarks = auditRemarks
                };

                context.DsmApprovalAudits.Add(audit);

                // 4. Complete the active DSM assignment automatically after successful approval
                try
                {
                    DsmUser? dsmUser = null;
                    if (!string.IsNullOrWhiteSpace(SelectedSubmission.DsmUserId))
                    {
                        var userMap = await context.SyncIdMappings
                            .FirstOrDefaultAsync(m => m.TableName == "DsmUsers" && m.RemoteGuid == SelectedSubmission.DsmUserId);
                        if (userMap != null)
                        {
                            dsmUser = await context.DsmUsers.FirstOrDefaultAsync(u => u.DsmUserId == userMap.LocalId);
                        }
                        dsmUser ??= await context.DsmUsers.FirstOrDefaultAsync(u => u.AuthUserId == SelectedSubmission.DsmUserId);
                    }
                    if (dsmUser == null && !string.IsNullOrWhiteSpace(SelectedSubmission.DsmName))
                    {
                        var dsmNameLower = SelectedSubmission.DsmName.Trim().ToLower();
                        dsmUser = await context.DsmUsers.FirstOrDefaultAsync(u =>
                            u.FullName != null && u.FullName.Trim().ToLower() == dsmNameLower);
                    }

                    if (dsmUser != null)
                    {
                        var normalizedSubShift = (_shiftCycleService ?? new ShiftCycleService()).NormalizeShiftType(SelectedSubmission.ShiftType);
                        var activeUserAssignments = await context.DsmPumpAssignments
                            .Where(a => a.IsActive
                                && a.DsmUserId == dsmUser.DsmUserId
                                && a.PumpId == SelectedSubmission.PumpId)
                            .ToListAsync();

                        var matchingAssignments = activeUserAssignments
                            .Where(a => string.Equals((_shiftCycleService ?? new ShiftCycleService()).NormalizeShiftType(a.ShiftType), normalizedSubShift, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        if (matchingAssignments.Count > 0)
                        {
                            foreach (var assignment in matchingAssignments)
                            {
                                assignment.IsActive = false;
                                assignment.CompletedDate = DateTime.Now;
                                context.Entry(assignment).State = EntityState.Modified;
                                _logger.Information("Automatically marked active assignment {AssignmentId} as Completed for DSM {DsmName} on Pump {PumpId}",
                                    assignment.DsmPumpAssignmentId, dsmUser.FullName, SelectedSubmission.PumpId);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to complete active DSM assignment during approval");
                    throw; // Escalate to rollback transaction
                }

                await context.SaveChangesAsync();

                // Register deterministic SyncIdMappings for cross-computer financial idempotency (Stage 2.3)
                await RegisterDeterministicApprovalMappingsAsync(
                    context,
                    submissionId,
                    savedEntry,
                    audit,
                    connectedPumpId,
                    connectedPumpIds,
                    debitModels,
                    DebtorRepayments.ToList(),
                    testingModels,
                    expenseModels,
                    cashModels,
                    OilDefSales.ToList(),
                    QrPayments.ToList());

                await context.SaveChangesAsync();

                // Pre-commit lease check: verify claim ownership has not been lost during local processing
                if (!string.IsNullOrEmpty(claimToken))
                {
                    var preCommitCheck = await _supabaseService.RenewLeaseAsync(submissionId, machineId, claimToken, claimVersion);
                    if (!preCommitCheck.Success)
                    {
                        _logger.Warning("Pre-commit lease check failed for {SubId}: {Error}. Rolling back local SQLite transaction.", submissionId, preCommitCheck.Error);
                        await transaction.RollbackAsync();
                        ShowUiMessage($"Approval aborted before commit: {preCommitCheck.Error}\n\nAnother workstation has reclaimed this submission.", "Lease Expired", MessageBoxButton.OK, MessageBoxImage.Warning);
                        await RefreshQueueAsync();
                        return;
                    }
                }

                await transaction.CommitAsync();

                // Notify active subscribers (e.g. DSM Entry screen) that a DSM entry has been committed
                FuelPro.Core.Services.DsmEntryService.RaiseDsmEntryChanged();

                // Raise DebtorChanged strictly AFTER successful commit if any debtor records were persisted
                if ((debitModels != null && debitModels.Count > 0) ||
                    (DebtorRepayments != null && DebtorRepayments.Count > 0) ||
                    (personalDebtors != null && personalDebtors.Count > 0))
                {
                    FuelPro.Core.Services.DsmEntryService.RaiseDebtorChanged();
                    _logger.Information("Raised DebtorChanged notification after successful commit for submission {SubId}", SelectedSubmission.Id);
                }
                
                _logger.Information("Local transaction successfully committed for DSM entry. SelectedSubmissionId: {SubId}", SelectedSubmission.Id);

                // 5. Send approval notification to DSM asynchronously (non-blocking)
                var submissionDsmUserId = SelectedSubmission.DsmUserId;
                var submissionPumpId = SelectedSubmission.PumpId;
                var submissionShiftDateDisplay = SelectedSubmission.ShiftDateDisplay;
                var submissionShiftType = SelectedSubmission.ShiftType;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var settingsSvc = _serviceProvider.GetRequiredService<SyncConfigService>();
                        var settings = await settingsSvc.GetSettingsAsync();
                        var approvalMessage = $"Your shift submission for {submissionShiftDateDisplay} Shift {submissionShiftType} on Pump {submissionPumpId} has been approved.";
                        await _supabaseService.SendNotificationAsync(settings.StationId, submissionDsmUserId, "DSM", approvalMessage);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Failed to send approval notification to DSM");
                    }
                });

                // 6. Complete status in Supabase (transitions via RPC or conditional REST PATCH fallback in single-PC mode)
                var approvedAtTimestamp = audit.ApprovedAt;
                bool cloudConfirmed = false;
                string? cloudPendingReason = null;

                if (!awaitingCloudReconciliation)
                {
                    var immediateComplete = await _supabaseService.CompleteSubmissionApprovalAsync(
                        submissionId, machineId, lockId, claimToken, claimVersion, approvedBy, approvedAtTimestamp);
                    if (immediateComplete.Success)
                    {
                        cloudConfirmed = true;
                        _logger.Information("Successfully completed approval for {SubId} in Supabase.", submissionId);
                    }
                    else
                    {
                        cloudPendingReason = immediateComplete.Error;
                        _logger.Warning("Initial cloud completion failed for {SubId}: {Error}. Queuing background retry.", submissionId, immediateComplete.Error);
                    }
                }
                else
                {
                    cloudPendingReason = lockResult.Error;
                }

                if (!cloudConfirmed)
                {
                    _ = Task.Run(async () =>
                    {
                        int maxRetries = 10;
                        int delayMs = 2000;
                        for (int attempt = 1; attempt <= maxRetries; attempt++)
                        {
                            var completeResult = await _supabaseService.CompleteSubmissionApprovalAsync(
                                submissionId, machineId, lockId, claimToken, claimVersion, approvedBy, approvedAtTimestamp);
                            if (completeResult.Success)
                            {
                                _logger.Information("Successfully completed deferred cloud approval for {SubId} in Supabase on retry attempt {Attempt}", submissionId, attempt);
                                break;
                            }

                            _logger.Warning("Retry attempt {Attempt} to complete approval for {SubId} in Supabase failed: {Error}. Retrying in {Delay}ms...",
                                attempt, submissionId, completeResult.Error, delayMs);

                            await Task.Delay(delayMs);
                            delayMs *= 2;
                        }
                    });
                }

                // 7. Push local change to Supabase immediately (for DsmApprovalAudits, etc.)
                var syncEngine = _serviceProvider.GetService<SyncEngine>();
                if (syncEngine != null)
                    _ = syncEngine.ForceSyncAsync();

                // Reconcile queue and navigation badge immediately after local commit
                PendingSubmissions.Remove(SelectedSubmission);
                _pollingService?.SyncCountFromQueue(PendingSubmissions.Count);

                if (cloudConfirmed)
                {
                    ShowUiMessage("DSM Submission approved successfully, saved locally, and confirmed in cloud!", "Approved", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    ShowUiMessage(
                        $"DSM Submission saved locally (awaiting cloud reconciliation).\nCloud approval has NOT been confirmed yet: {cloudPendingReason}",
                        "Saved Locally (Awaiting Cloud Sync)",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                
                SelectedSubmission = null;
                OverrideRemark = "";
                OverrideContinuity = false;
                
                await RefreshQueueAsync();
                await LoadApprovedHistoryAsync();
            }
            catch (DbUpdateException dbEx)
            {
                await transaction.RollbackAsync();
                LogDbUpdateExceptionDetails(dbEx);
                ShowUiMessage($"Approval failed: A database update constraint or validation error occurred. Check log files for technical details.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.Error(ex, "Approval process failed for submission {SubId}", SelectedSubmission.Id);
                ShowUiMessage($"Approval failed: {ex.Message}", "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                transaction?.Dispose();
                StatusMessage = "";
            }
        }


    [RelayCommand]
    private async Task RejectAsync()
    {
        if (SelectedSubmission == null) return;

        // Prompt for reason
        var reasonWindow = new Views.DsmRejectionReasonWindow();
        if (reasonWindow.ShowDialog() != true) return;

        string reason = reasonWindow.RejectionReason;
        if (string.IsNullOrWhiteSpace(reason))
        {
            MessageBox.Show("Rejection requires a valid reason.", "Reason Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusMessage = "⏳ Rejecting submission in Supabase...";

        try
        {
            var result = await _supabaseService.RejectSubmissionAsync(SelectedSubmission.Id, reason);
            if (result.Success)
            {
                var rejected = SelectedSubmission;
                PendingSubmissions.Remove(rejected);
                _pollingService?.SyncCountFromQueue(PendingSubmissions.Count);

                // Send notification to DSM
                var settingsSvc = _serviceProvider.GetService<SyncConfigService>();
                if (settingsSvc != null)
                {
                    var settings = await settingsSvc.GetSettingsAsync();
                    var rejectMessage = $"Your shift submission for {rejected.ShiftDateDisplay} Shift {rejected.ShiftType} on Pump {rejected.PumpId} has been rejected. Reason: {reason}";
                    await _supabaseService.SendNotificationAsync(settings.StationId, rejected.DsmUserId, "DSM", rejectMessage);
                }

                ShowUiMessage("DSM Submission rejected.", "Rejected", MessageBoxButton.OK, MessageBoxImage.Information);
                SelectedSubmission = null;
                await RefreshQueueAsync();
            }
            else
            {
                MessageBox.Show($"Rejection failed: {result.Error}", "Concurrency Conflict", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to reject submission {SubId}", SelectedSubmission.Id);
            MessageBox.Show($"Rejection failed: {ex.Message}", "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            StatusMessage = "";
        }
    }

    [RelayCommand]
    private void OpenAttachment()
    {
        if (string.IsNullOrWhiteSpace(AttachmentUrl)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AttachmentUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open attachment URL {Url}", AttachmentUrl);
            MessageBox.Show($"Failed to open attachment: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RecalculateRunningBalancesAsync(FuelProDbContext context, int productId, DateTime fromDate)
    {
        try
        {
            var prevLog = await context.OilDefDailyLogs
                .Where(l => l.ProductId == productId && l.LogDate < fromDate)
                .OrderByDescending(l => l.LogDate)
                .ThenByDescending(l => l.Id)
                .FirstOrDefaultAsync();

            double prevRemaining = 0.0;
            if (prevLog != null)
            {
                prevRemaining = prevLog.RemainingStock;
            }
            else
            {
                var monthInv = await context.OilDefInventories
                    .Where(i => i.ProductId == productId && i.Year == fromDate.Year && i.Month == fromDate.Month)
                    .FirstOrDefaultAsync();
                prevRemaining = monthInv?.OpeningStock ?? 0.0;
            }

            var subsequentLogs = await context.OilDefDailyLogs
                .Where(l => l.ProductId == productId && l.LogDate >= fromDate)
                .OrderBy(l => l.LogDate)
                .ThenBy(l => l.Id)
                .ToListAsync();

            double running = prevRemaining;
            foreach (var log in subsequentLogs)
            {
                running = running + log.AddedQuantity - log.SoldQuantity + log.AdjustmentQuantity;
                log.RemainingStock = running;
                context.Entry(log).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
            }

            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to recalculate running balances for product {ProductId}", productId);
        }
    }

    private void LogDbUpdateExceptionDetails(DbUpdateException dbEx)
    {
        _logger.Error(dbEx, "=== EF Core SaveChanges Database Update Exception ===");
        
        if (dbEx.InnerException != null)
        {
            _logger.Error(dbEx.InnerException, "Inner Exception Details: {Message}", dbEx.InnerException.Message);
        }

        if (dbEx.Entries != null)
        {
            foreach (var entry in dbEx.Entries)
            {
                var entityType = entry.Entity.GetType().FullName;
                var state = entry.State.ToString();
                
                // Get primary key values
                var keyValues = new List<string>();
                var keyProperties = entry.Metadata.FindPrimaryKey()?.Properties;
                if (keyProperties != null)
                {
                    foreach (var prop in keyProperties)
                    {
                        var val = entry.Property(prop.Name).CurrentValue;
                        keyValues.Add($"{prop.Name} = {val}");
                    }
                }
                var keysStr = string.Join(", ", keyValues);

                _logger.Error("Failed Entity Type: {EntityType} | State: {State} | Key Values: [{KeysStr}]",
                    entityType, state, keysStr);

                // Print all property values
                try
                {
                    var propValues = entry.CurrentValues.Properties
                        .Select(p => $"{p.Name} = {entry.CurrentValues[p]}")
                        .ToList();
                    _logger.Error("Entity Values: {Values}", string.Join(" | ", propValues));
                }
                catch (Exception valEx)
                {
                    _logger.Error(valEx, "Error while printing entity property values");
                }
            }
        }

        try
        {
            var validationContexts = dbEx.Entries
                .Select(e => e.Entity)
                .Select(entity => 
                {
                    var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
                    var valCtx = new System.ComponentModel.DataAnnotations.ValidationContext(entity);
                    System.ComponentModel.DataAnnotations.Validator.TryValidateObject(entity, valCtx, results, true);
                    return new { Entity = entity, Results = results };
                })
                .Where(x => x.Results.Any())
                .ToList();

            foreach (var vc in validationContexts)
            {
                _logger.Error("Validation failed for entity {EntityType}:", vc.Entity.GetType().FullName);
                foreach (var err in vc.Results)
                {
                    _logger.Error("  - MemberNames: {Members} | Error: {Error}", 
                        string.Join(", ", err.MemberNames), err.ErrorMessage);
                }
            }
        }
        catch (Exception valEx)
        {
            _logger.Error(valEx, "Error while performing entity validation checks");
        }
    }

    private List<DsmApprovedSubmissionDto> _allApprovedSubmissions = new();

    [RelayCommand]
    public async Task LoadApprovedHistoryAsync()
    {
        try
        {
            var supabaseResult = await FetchApprovedSubmissionsFromSupabaseAsync();
            
            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
            var localAudits = await context.DsmApprovalAudits
                .OrderByDescending(a => a.ApprovedAt)
                .Take(500)
                .ToListAsync();

            await HealCorruptedApprovedEntriesAsync(context, localAudits);

            var auditMap = localAudits.ToDictionary(a => a.SubmissionId);

            var entryIds = new List<int>();
            foreach (var a in localAudits)
            {
                try
                {
                    var approvedObj = JsonConvert.DeserializeObject<dynamic>(a.ApprovedDataJson);
                    int entryId = (int)(approvedObj?.DsmEntryId ?? 0);
                    if (entryId > 0) entryIds.Add(entryId);
                }
                catch {}
            }

            var localEntries = await context.DsmEntries
                .Include(e => e.Shift)
                .Where(e => entryIds.Contains(e.DsmEntryId))
                .ToDictionaryAsync(e => e.DsmEntryId);

            var allApproved = new List<DsmApprovedSubmissionDto>();

            if (supabaseResult.Success && supabaseResult.Data != null)
            {
                foreach (var item in supabaseResult.Data)
                {
                    Guid subId = item.Id;
                    int entryId = 0;
                    if (auditMap.TryGetValue(subId, out var audit))
                    {
                        try
                        {
                            var approvedObj = JsonConvert.DeserializeObject<dynamic>(audit.ApprovedDataJson);
                            entryId = (int)(approvedObj?.DsmEntryId ?? 0);
                        }
                        catch {}
                    }

                    double gross = 0;
                    double coll = 0;
                    double mismatch = 0;

                    if (entryId > 0 && localEntries.TryGetValue(entryId, out var entry))
                    {
                        if (entry.ReconciledToPumpId.HasValue && localEntries.TryGetValue(entry.ReconciledToPumpId.Value, out var realPrimary))
                        {
                            entryId = realPrimary.DsmEntryId;
                            entry = realPrimary;
                        }
                        gross = (double)entry.GrossSales;
                        coll = (double)entry.TotalCollection;
                        mismatch = (double)entry.Mismatch;
                    }

                    allApproved.Add(new DsmApprovedSubmissionDto
                    {
                        SubmissionId = subId,
                        DsmEntryId = entryId,
                        DsmName = item.DsmUsers?.FullName ?? "Unknown DSM",
                        DsmUserId = item.DsmUserId,
                        PumpId = (int)item.PumpId,
                        ShiftDate = item.ShiftDate,
                        ShiftType = item.ShiftType,
                        SubmittedAt = item.SubmittedAt,
                        ApprovedAt = item.ApprovedAt ?? DateTime.Now,
                        ApprovedBy = item.ApprovedBy ?? "System",
                        Notes = item.Notes ?? "",
                        AttachmentUrl = item.AttachmentUrl,
                        MetadataJson = ExtractMetadataJson(item),
                        GrossSales = gross,
                        TotalCollection = coll,
                        Mismatch = mismatch
                    });
                }
            }
            else
            {
                foreach (var audit in localAudits)
                {
                    int entryId = 0;
                    try
                    {
                        var approvedObj = JsonConvert.DeserializeObject<dynamic>(audit.ApprovedDataJson);
                        entryId = (int)(approvedObj?.DsmEntryId ?? 0);
                    }
                    catch {}

                    if (entryId > 0 && localEntries.TryGetValue(entryId, out var entry))
                    {
                        if (entry.ReconciledToPumpId.HasValue && localEntries.TryGetValue(entry.ReconciledToPumpId.Value, out var realPrimary))
                        {
                            entryId = realPrimary.DsmEntryId;
                            entry = realPrimary;
                        }

                        if (!allApproved.Any(a => a.DsmEntryId == entryId))
                        {
                            allApproved.Add(new DsmApprovedSubmissionDto
                            {
                                SubmissionId = audit.SubmissionId,
                                DsmEntryId = entryId,
                                DsmName = entry.DsmName,
                                PumpId = entry.PumpId,
                                ShiftDate = entry.Shift?.ShiftDate ?? DateTime.Today,
                                ShiftType = entry.Shift?.ShiftType ?? "A",
                                SubmittedAt = audit.ApprovedAt.AddHours(-2),
                                ApprovedAt = audit.ApprovedAt,
                                ApprovedBy = audit.ApprovedBy,
                                Notes = audit.Remarks ?? "",
                                GrossSales = (double)entry.GrossSales,
                                TotalCollection = (double)entry.TotalCollection,
                                Mismatch = (double)entry.Mismatch
                            });
                        }
                    }
                }
            }

            // Also include local DsmEntries in the approval history to display previous month entries
            var existingEntryIds = new HashSet<int>(allApproved.Select(a => a.DsmEntryId).Where(id => id > 0));
            var allLocalEntries = await context.DsmEntries
                .Include(e => e.Shift)
                .Where(e => !e.ReconciledToPumpId.HasValue)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();

            foreach (var entry in allLocalEntries)
            {
                if (!existingEntryIds.Contains(entry.DsmEntryId))
                {
                    allApproved.Add(new DsmApprovedSubmissionDto
                    {
                        SubmissionId = Guid.NewGuid(),
                        DsmEntryId = entry.DsmEntryId,
                        DsmName = entry.DsmName,
                        PumpId = entry.PumpId,
                        ShiftDate = entry.Shift?.ShiftDate ?? DateTime.Today,
                        ShiftType = entry.Shift?.ShiftType ?? "A",
                        SubmittedAt = entry.CreatedAt,
                        ApprovedAt = entry.CreatedAt,
                        ApprovedBy = "Local System",
                        Notes = "Saved Entry",
                        GrossSales = (double)entry.GrossSales,
                        TotalCollection = (double)entry.TotalCollection,
                        Mismatch = (double)entry.Mismatch
                    });
                }
            }

            _allApprovedSubmissions = allApproved;

            DsmFilterOptions.Clear();
            DsmFilterOptions.Add("All");
            foreach (var name in allApproved.Select(s => s.DsmName).Distinct().OrderBy(x => x))
                DsmFilterOptions.Add(name);

            PumpFilterOptions.Clear();
            PumpFilterOptions.Add("All");
            foreach (var pump in allApproved.Select(s => s.PumpId).Distinct().OrderBy(x => x))
                PumpFilterOptions.Add(pump.ToString());

            ShiftFilterOptions.Clear();
            ShiftFilterOptions.Add("All");
            foreach (var shift in allApproved.Select(s => s.ShiftType).Distinct().OrderBy(x => x))
                ShiftFilterOptions.Add(shift);

            ApplyFilters();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load approved submissions history");
        }
    }

    private void ApplyFilters()
    {
        var filtered = _allApprovedSubmissions.AsEnumerable();

        if (IsFilterToday)
        {
            filtered = filtered.Where(s => s.ApprovedAt.Date == DateTime.Today);
        }
        else if (IsFilterYesterday)
        {
            filtered = filtered.Where(s => s.ApprovedAt.Date == DateTime.Today.AddDays(-1));
        }
        else if (IsFilterThisWeek)
        {
            var startOfWeek = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
            filtered = filtered.Where(s => s.ApprovedAt.Date >= startOfWeek);
        }

        if (SelectedDsmFilter != "All")
        {
            filtered = filtered.Where(s => string.Equals(s.DsmName, SelectedDsmFilter, StringComparison.OrdinalIgnoreCase));
        }
        if (SelectedPumpFilter != "All" && int.TryParse(SelectedPumpFilter, out var pumpId))
        {
            filtered = filtered.Where(s => s.PumpId == pumpId);
        }
        if (SelectedShiftFilter != "All")
        {
            filtered = filtered.Where(s => string.Equals(s.ShiftType, SelectedShiftFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchTextFilter))
        {
            filtered = filtered.Where(s => s.DsmName.Contains(SearchTextFilter, StringComparison.OrdinalIgnoreCase));
        }

        var resultList = filtered.ToList();
        FilteredApprovedSubmissions = new ObservableCollection<DsmApprovedSubmissionDto>(resultList);

        SummaryApprovedCount = resultList.Count;
        SummaryGrossSales = resultList.Sum(s => s.GrossSales);
        SummaryTotalCollection = resultList.Sum(s => s.TotalCollection);
        SummaryTotalDifference = resultList.Sum(s => s.Mismatch);

        double totalExpenses = 0;
        double totalDebtors = 0;
        double totalTesting = 0;
        double totalOilDef = 0;

        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var entryIds = resultList.Select(s => s.DsmEntryId).Where(id => id > 0).ToList();
        if (entryIds.Any())
        {
            var entries = context.DsmEntries
                .Include(e => e.Expenses)
                .Include(e => e.DebitEntries)
                .Include(e => e.TestingEntries)
                .Where(e => entryIds.Contains(e.DsmEntryId))
                .ToList();

            totalExpenses = entries.Sum(e => e.Expenses.Sum(x => x.Amount));
            totalDebtors = entries.Sum(e => e.DebitEntries.Sum(x => x.Amount));
            totalTesting = entries.Sum(e => e.TestingEntries.Sum(x => x.Amount));
        }

        foreach (var s in resultList)
        {
            if (!string.IsNullOrEmpty(s.MetadataJson))
            {
                try
                {
                    var metadata = JsonConvert.DeserializeObject<dynamic>(s.MetadataJson);
                    if (metadata != null && metadata.oilDefSales != null)
                    {
                        foreach (var sale in metadata.oilDefSales)
                        {
                            double qty = (double)(sale.quantity ?? 0.0);
                            double price = (double)(sale.price ?? 0.0);
                            totalOilDef += qty * price;
                        }
                    }
                }
                catch {}
            }
        }

        SummaryTotalExpenses = totalExpenses;
        SummaryTotalDebtors = totalDebtors;
        SummaryTotalTesting = totalTesting;
        SummaryTotalOilDefSales = totalOilDef;
    }

    partial void OnIsFilterTodayChanged(bool value) { if (value) { IsFilterYesterday = false; IsFilterThisWeek = false; } ApplyFilters(); }
    partial void OnIsFilterYesterdayChanged(bool value) { if (value) { IsFilterToday = false; IsFilterThisWeek = false; } ApplyFilters(); }
    partial void OnIsFilterThisWeekChanged(bool value) { if (value) { IsFilterToday = false; IsFilterYesterday = false; } ApplyFilters(); }
    partial void OnSelectedDsmFilterChanged(string value) => ApplyFilters();
    partial void OnSelectedPumpFilterChanged(string value) => ApplyFilters();
    partial void OnSelectedShiftFilterChanged(string value) => ApplyFilters();
    partial void OnSearchTextFilterChanged(string value) => ApplyFilters();

    partial void OnSelectedApprovedSubmissionChanged(DsmApprovedSubmissionDto? value)
    {
        if (value != null)
        {
            SelectedSubmission = null;
            _ = LoadApprovedSubmissionDetailsAsync(value);
        }
        else
        {
            if (SelectedSubmission == null)
            {
                NozzleReadings.Clear();
                OilDefSales.Clear();
                IsEditing = false;
                IsReadOnlyMode = false;
                TimelineLogs.Clear();
            }
        }
    }

    private async Task LoadApprovedSubmissionDetailsAsync(DsmApprovedSubmissionDto approvedSub)
    {
        _isLoadingSubmission = true;
        IsLoadingDetails = true;
        StatusMessage = $"⏳ Loading approved entry details for {approvedSub.TitleDisplay}...";
        IsReadOnlyMode = true;
        IsEditing = true;

        try
        {
            SubmissionNotes = approvedSub.Notes;
            AttachmentUrl = approvedSub.AttachmentUrl;

            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
            var entry = await context.DsmEntries
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                    .ThenInclude(p => p.Items)
                .Include(e => e.DebitEntries)
                .Include(e => e.TestingEntries)
                .Include(e => e.Expenses)
                .Include(e => e.CashDenominations)
                .Include(e => e.PersonalDebtors)
                .Include(e => e.KhandharePetroleumEntries)
                .Include(e => e.QrPayments)
                .FirstOrDefaultAsync(e => e.DsmEntryId == approvedSub.DsmEntryId);

            var connectedEntries = new List<DsmEntry>();
            if (entry != null)
            {
                var effectiveConnectedPumps = entry.GetEffectiveConnectedPumpIds();
                if (effectiveConnectedPumps.Count > 0)
                {
                    var connCandidates = await context.DsmEntries
                        .Include(e => e.NozzleReadings)
                        .Where(e => e.ShiftId == entry.ShiftId
                            && (e.ReconciledToPumpId == entry.DsmEntryId 
                                || (effectiveConnectedPumps.Contains(e.PumpId) && (e.ReconciledToPumpId == entry.PumpId || (e.DsmName != null && entry.DsmName != null && e.DsmName.ToLower() == entry.DsmName.ToLower())))))
                        .ToListAsync();

                    foreach (var connPumpId in effectiveConnectedPumps)
                    {
                        var matchedSlave = connCandidates
                            .Where(e => e.PumpId == connPumpId)
                            .OrderBy(e => e.ReconciledToPumpId == entry.DsmEntryId ? 0 : 1)
                            .ThenBy(e => e.DsmEntryId >= entry.DsmEntryId ? (e.DsmEntryId - entry.DsmEntryId) : (100000 + Math.Abs(e.DsmEntryId - entry.DsmEntryId)))
                            .FirstOrDefault();

                        if (matchedSlave != null)
                        {
                            connectedEntries.Add(matchedSlave);
                        }
                    }
                }
            }

            NozzleReadings.Clear();
            DebtorEntries.Clear();
            if (entry?.DebitEntries != null && entry.DebitEntries.Count > 0)
            {
                foreach (var d in entry.DebitEntries)
                {
                    DebtorEntries.Add(new DsmDebitRow
                    {
                        DebtorName = d.DebtorName ?? string.Empty,
                        Amount = d.Amount,
                        VehicleNumber = d.VehicleNumber,
                        SlipNumber = d.SlipNumber,
                        Remarks = d.Remarks,
                        EntryTime = d.EntryTime
                    });
                }
            }
            else if (!string.IsNullOrEmpty(approvedSub.MetadataJson))
            {
                try
                {
                    var metadata = JsonConvert.DeserializeObject<dynamic>(approvedSub.MetadataJson);
                    var rawDebtors = metadata?.debtorEntries ?? metadata?.debitEntries ?? metadata?.debtors;
                    if (rawDebtors != null)
                    {
                        foreach (var d in rawDebtors)
                        {
                            string name = (d.debtorName ?? d.DebtorName ?? d.name ?? d.Name ?? string.Empty).ToString();
                            double amount = Convert.ToDouble((object?)(d.amount ?? d.Amount ?? 0.0));
                            string vehicle = (d.vehicleNumber ?? d.VehicleNumber ?? string.Empty).ToString();
                            string slip = (d.slipNumber ?? d.SlipNumber ?? string.Empty).ToString();
                            string remarks = (d.remarks ?? d.Remarks ?? string.Empty).ToString();
                            string time = (d.entryTime ?? d.EntryTime ?? string.Empty).ToString();

                            if (!string.IsNullOrWhiteSpace(name) || amount > 0)
                            {
                                DebtorEntries.Add(new DsmDebitRow
                                {
                                    DebtorName = name,
                                    Amount = amount,
                                    VehicleNumber = string.IsNullOrWhiteSpace(vehicle) ? null : vehicle.Trim(),
                                    SlipNumber = string.IsNullOrWhiteSpace(slip) ? null : slip.Trim(),
                                    Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim(),
                                    EntryTime = string.IsNullOrWhiteSpace(time) ? null : time.Trim()
                                });
                            }
                        }
                    }
                }
                catch {}
            }
            CardSwipeDetails.Clear();
            SubmissionTestingEntries.Clear();
            OilDefSales.Clear();
            TimelineLogs.Clear();

            if (entry != null && entry.NozzleReadings != null)
            {
                var uniqueReadings = entry.NozzleReadings
                    .GroupBy(r => r.NozzleNumber)
                    .Select(g => g.OrderByDescending(r => r.NozzleReadingId).First())
                    .OrderBy(r => r.NozzleNumber)
                    .ToList();

                foreach (var r in uniqueReadings)
                {
                    string fuelType = !string.IsNullOrWhiteSpace(r.FuelType)
                        ? r.FuelType
                        : GetFuelTypeName(entry.PumpId, r.NozzleNumber, approvedSub.ShiftDate);

                    var row = new DsmNozzleRow
                    {
                        NozzleId = r.NozzleNumber,
                        FuelType = fuelType,
                        OpeningReading = r.OpeningReading,
                        ClosingReading = r.ClosingReading,
                        Rate = r.Rate,
                        SaleLitres = r.SaleLitres,
                        Amount = r.Amount
                    };
                    NozzleReadings.Add(row);
                }
            }

            foreach (var connectedEntry in connectedEntries)
            {
                if (connectedEntry.NozzleReadings != null)
                {
                    var uniqueConnectedReadings = connectedEntry.NozzleReadings
                        .GroupBy(r => r.NozzleNumber)
                        .Select(g => g.OrderByDescending(r => r.NozzleReadingId).First())
                        .OrderBy(r => r.NozzleNumber)
                        .ToList();

                    foreach (var r in uniqueConnectedReadings)
                    {
                        string fuelType = !string.IsNullOrWhiteSpace(r.FuelType)
                            ? r.FuelType
                            : GetFuelTypeName(connectedEntry.PumpId, r.NozzleNumber, approvedSub.ShiftDate);

                        var row = new DsmNozzleRow
                        {
                            NozzleId = r.NozzleNumber,
                            FuelType = fuelType,
                            OpeningReading = r.OpeningReading,
                            ClosingReading = r.ClosingReading,
                            Rate = r.Rate,
                            SaleLitres = r.SaleLitres,
                            Amount = r.Amount
                        };
                        NozzleReadings.Add(row);
                    }
                }
            }

            if (entry != null)
            {
                PersonalDebtors.Clear();
                if (entry.PersonalDebtors != null)
                {
                    foreach (var d in entry.PersonalDebtors.OrderBy(d => d.SequenceNumber).ThenBy(d => d.Id))
                    {
                        PersonalDebtors.Add(d);
                    }
                }

                SubmissionExpenses.Clear();
                if (entry.Expenses != null)
                {
                    foreach (var exp in entry.Expenses)
                    {
                        SubmissionExpenses.Add(new Expense
                        {
                            ExpenseId = exp.ExpenseId,
                            DsmEntryId = exp.DsmEntryId,
                            Description = exp.Description,
                            Amount = exp.Amount
                        });
                    }
                }

                KhandhareEntries.Clear();
                if (entry.KhandharePetroleumEntries != null)
                {
                    foreach (var kp in entry.KhandharePetroleumEntries)
                    {
                        KhandhareEntries.Add(new KhandharePetroleumEntry
                        {
                            Id = kp.Id,
                            DsmEntryId = kp.DsmEntryId,
                            DsmName = kp.DsmName,
                            Date = kp.Date,
                            Name = kp.Name,
                            SlipNumber = kp.SlipNumber,
                            VehicleNumber = kp.VehicleNumber,
                            Amount = kp.Amount
                        });
                    }
                }

                QrPayments.Clear();
                if (entry.QrPayments != null && entry.QrPayments.Count > 0)
                {
                    foreach (var qr in entry.QrPayments)
                    {
                        if (qr.Amount > 0)
                        {
                            string effectiveTarget = string.IsNullOrWhiteSpace(qr.TargetDsmName) ? "General QR" : qr.TargetDsmName.Trim();
                            QrPayments.Add(new DsmQrPaymentEntry
                            {
                                DsmName = !string.IsNullOrWhiteSpace(qr.DsmName) ? qr.DsmName : entry.DsmName,
                                TargetDsmName = effectiveTarget,
                                Date = qr.Date != default ? qr.Date : approvedSub.ShiftDate,
                                Amount = qr.Amount,
                                Tid = string.IsNullOrWhiteSpace(qr.Tid) ? null : qr.Tid,
                                Batch = string.IsNullOrWhiteSpace(qr.Batch) ? null : qr.Batch,
                                Slot = string.IsNullOrWhiteSpace(qr.Slot) ? null : qr.Slot
                            });
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(approvedSub.MetadataJson))
                {
                    try
                    {
                        var metadata = JsonConvert.DeserializeObject<dynamic>(approvedSub.MetadataJson);
                        var qrRawList = metadata?.qrPayments ?? metadata?.qrPaymentEntries ?? metadata?.Collections?.qrPayments;
                        if (qrRawList != null)
                        {
                            foreach (var qr in qrRawList)
                            {
                                string targetDsm = (qr.targetDsmName ?? qr.TargetDsmName ?? qr.target_dsm_name ?? qr.targetDsm ?? string.Empty).ToString();
                                double amount = Convert.ToDouble((object?)(qr.amount ?? qr.Amount ?? 0.0));
                                string tid = (qr.tid ?? qr.Tid ?? string.Empty).ToString();
                                string batch = (qr.batch ?? qr.Batch ?? string.Empty).ToString();
                                string slot = (qr.slot ?? qr.Slot ?? string.Empty).ToString();

                                if (amount > 0)
                                {
                                    string effectiveTarget = string.IsNullOrWhiteSpace(targetDsm) ? "General QR" : targetDsm.Trim();
                                    QrPayments.Add(new DsmQrPaymentEntry
                                    {
                                        DsmName = entry.DsmName,
                                        TargetDsmName = effectiveTarget,
                                        Date = approvedSub.ShiftDate,
                                        Amount = amount,
                                        Tid = string.IsNullOrWhiteSpace(tid) ? null : tid,
                                        Batch = string.IsNullOrWhiteSpace(batch) ? null : batch,
                                        Slot = string.IsNullOrWhiteSpace(slot) ? null : slot
                                    });
                                }
                            }
                        }
                    }
                    catch {}
                }

                _dynamicManualAmounts.Clear();
                _dynamicManualTids.Clear();
                _dynamicManualBatches.Clear();
                _dynamicManualSlots.Clear();

                var pc = entry.PaymentCollection;
                if (pc != null)
                {
                    var cash1Total = entry.CashDenominations.Where(c => c.CashType == "Cash1").Sum(c => c.TotalAmount);
                    var cash2Total = entry.CashDenominations.Where(c => c.CashType == "Cash2").Sum(c => c.TotalAmount);

                    var cash1Denom = entry.CashDenominations.FirstOrDefault(c => c.CashType == "Cash1");
                    bool cash1HasNonZeroDenoms = cash1Denom != null && (cash1Denom.Denom500 > 0 || cash1Denom.Denom200 > 0 || cash1Denom.Denom100 > 0
                        || cash1Denom.Denom50 > 0 || cash1Denom.Denom20 > 0 || cash1Denom.Denom10 > 0 || cash1Denom.Coins > 0);
                    if (cash1HasNonZeroDenoms)
                    {
                        Cash1.Denom500 = cash1Denom!.Denom500;
                        Cash1.Denom200 = cash1Denom.Denom200;
                        Cash1.Denom100 = cash1Denom.Denom100;
                        Cash1.Denom50 = cash1Denom.Denom50 > 0 ? cash1Denom.Denom50 : null;
                        Cash1.Denom20 = cash1Denom.Denom20 > 0 ? cash1Denom.Denom20 : null;
                        Cash1.Denom10 = cash1Denom.Denom10 > 0 ? cash1Denom.Denom10 : null;
                        Cash1.Coins = cash1Denom.Coins > 0 ? cash1Denom.Coins : null;
                    }
                    else
                    {
                        Cash1.Denom500 = Cash1.Denom200 = Cash1.Denom100 = Cash1.Denom50 = Cash1.Denom20 = Cash1.Denom10 = Cash1.Coins = null;
                    }

                    var cash2Denom = entry.CashDenominations.FirstOrDefault(c => c.CashType == "Cash2");
                    if (cash2Denom != null)
                    {
                        Cash2.Denom500 = cash2Denom.Denom500;
                        Cash2.Denom200 = cash2Denom.Denom200;
                        Cash2.Denom100 = cash2Denom.Denom100;
                        Cash2.Denom50 = cash2Denom.Denom50;
                        Cash2.Denom20 = cash2Denom.Denom20;
                        Cash2.Denom10 = cash2Denom.Denom10;
                        Cash2.Coins = cash2Denom.Coins;
                    }
                    else
                    {
                        Cash2.Denom500 = Cash2.Denom200 = Cash2.Denom100 = Cash2.Denom50 = Cash2.Denom20 = Cash2.Denom10 = Cash2.Coins = null;
                    }

                    CashAmount = cash2Total > 0 ? cash2Total : CashAmount;
                    UpiAmount = pc.PhonePeMorning + pc.PhonePeDay + pc.PhonePeNight + pc.PhonePeCardMorning + pc.PhonePeCardDay + pc.PhonePeCardNight;
                    CardAmount = pc.CreditCardMorning + pc.CreditCardDay + pc.CreditCardNight;
                    PetroCardAmount = pc.PetroCardMorning + pc.PetroCardDay + pc.PetroCardNight;
                    CashDepositAmount = cash1Total > 0 ? cash1Total : pc.CashDeposit;
                    if (Cash1.TotalAmount == 0 && CashDepositAmount > 0)
                    {
                        Cash1.TotalAmount = CashDepositAmount;
                    }
                    if (Cash2.TotalAmount == 0 && CashAmount > 0)
                    {
                        Cash2.TotalAmount = CashAmount;
                    }
                    CollectionReductionAmount = pc.CollectionReduction;
                    CollectionReductionRemark = pc.CollectionReductionRemark;
                    if ((CollectionReductionAmount == null || CollectionReductionAmount == 0) && !string.IsNullOrEmpty(approvedSub.MetadataJson))
                    {
                        try
                        {
                            var meta = JsonConvert.DeserializeObject<dynamic>(approvedSub.MetadataJson);
                            var redVal = meta?.collectionReduction ?? meta?.collection_reduction ?? meta?.Collections?.collectionReduction;
                            if (redVal != null)
                            {
                                double rAmt = Convert.ToDouble((object)redVal);
                                if (rAmt > 0)
                                {
                                    CollectionReductionAmount = rAmt;
                                }
                            }
                            var redRem = meta?.collectionReductionRemark ?? meta?.collection_reduction_remark ?? meta?.Collections?.collectionReductionRemark;
                            if (redRem != null && string.IsNullOrWhiteSpace(CollectionReductionRemark))
                            {
                                CollectionReductionRemark = redRem.ToString();
                            }
                        }
                        catch {}
                    }
                    OthersAmount = pc.Others;
                    CreditAmount = (double)entry.TotalCreditors;
                    ExpenseAmount = entry.Expenses.Sum(e => e.Amount);
                    ExpenseNotes = entry.Expenses.FirstOrDefault()?.Description ?? "";
                    ShortAmount = (double)(entry.Mismatch < 0 ? Math.Abs(entry.Mismatch) : 0);
                    ExcessAmount = (double)(entry.Mismatch > 0 ? entry.Mismatch : 0);

                    ManualTidTransactions.Clear();
                    var allItems = pc.GetAllItems()?.ToList() ?? new List<PaymentCollectionItem>();
                    var distinctCodes = allItems
                        .Where(i => !PaymentCollection.IsStandardCollectionCode(i.CollectionTypeCode) && !string.IsNullOrWhiteSpace(i.CollectionTypeCode))
                        .Select(i => PaymentCollection.NormalizeCollectionTypeCode(i.CollectionTypeCode))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    foreach (var normCode in distinctCodes)
                    {
                        var matchingItems = allItems.Where(i =>
                            string.Equals(PaymentCollection.NormalizeCollectionTypeCode(i.CollectionTypeCode), normCode, StringComparison.OrdinalIgnoreCase)).ToList();

                        var otpItems = matchingItems.Where(i => !string.IsNullOrWhiteSpace(i.Otp)).ToList();
                        var nonOtpItems = matchingItems.Where(i => string.IsNullOrWhiteSpace(i.Otp)).ToList();

                        double otpPortion = otpItems.Sum(i => i.Amount);
                        bool isSingleAggregateHeader = nonOtpItems.Count == 1 && (
                            (otpItems.Count > 1 && Math.Abs(nonOtpItems[0].Amount - otpPortion) < 0.01) ||
                            (otpItems.Count == 1 && Math.Abs(nonOtpItems[0].Amount - otpPortion) < 0.01
                                && string.IsNullOrWhiteSpace(nonOtpItems[0].Tid) && string.IsNullOrWhiteSpace(nonOtpItems[0].Batch)
                                && string.Equals(nonOtpItems[0].Slot?.Trim(), otpItems[0].Slot?.Trim(), StringComparison.OrdinalIgnoreCase))
                        );

                        double manualPortion = isSingleAggregateHeader ? 0 : nonOtpItems.Where(nonOtp => !nonOtp.IsSyntheticSummary).Sum(nonOtp => nonOtp.Amount);
                        var firstManual = nonOtpItems.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Tid) || !string.IsNullOrWhiteSpace(i.Batch))
                            ?? matchingItems.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Tid) || !string.IsNullOrWhiteSpace(i.Batch))
                            ?? nonOtpItems.FirstOrDefault()
                            ?? matchingItems.FirstOrDefault();

                        var allTypes = GetCollectionTypesSnapshot();
                        var master = allTypes.FirstOrDefault(m => string.Equals(PaymentCollection.NormalizeCollectionTypeCode(m.Code), normCode, StringComparison.OrdinalIgnoreCase));
                        bool isManualOnly = master != null && master.IsManualOnly;

                        var genuineTidItems = nonOtpItems.Where(i => !i.IsSyntheticSummary && (
                            !string.IsNullOrWhiteSpace(i.Tid) ||
                            !string.IsNullOrWhiteSpace(i.Batch) ||
                            string.Equals(normCode, "PINELABS_CARD", StringComparison.OrdinalIgnoreCase)
                        )).ToList();

                        var scalarManualItems = nonOtpItems.Where(i => !i.IsSyntheticSummary && !genuineTidItems.Contains(i)).ToList();

                        if (genuineTidItems.Count > 0)
                        {
                            foreach (var item in genuineTidItems)
                            {
                                string mode = item.CollectionTypeCode;
                                ManualTidTransactions.Add(new DsmCardSwipeRow
                                {
                                    Mode = ResolveCollectionTypeDisplayName(mode),
                                    Amount = item.Amount,
                                    Tid = item.Tid ?? "",
                                    Batch = item.Batch ?? "",
                                    Slot = item.Slot ?? "General",
                                    TransactionId = item.TransactionId,
                                    OnRowChanged = RecalculateTotals
                                });
                            }
                        }

                        double remainingManualPortion = scalarManualItems.Sum(i => i.Amount);
                        if (!isSingleAggregateHeader && remainingManualPortion > 0)
                        {
                            var firstScalar = scalarManualItems.FirstOrDefault();
                            _dynamicManualAmounts[normCode] = remainingManualPortion;
                            _dynamicManualTids[normCode] = firstScalar?.Tid ?? "";
                            _dynamicManualBatches[normCode] = firstScalar?.Batch ?? "";
                            _dynamicManualSlots[normCode] = firstScalar?.Slot ?? "General";
                        }

                        // Add OTP items to CardSwipeDetails
                        foreach (var item in otpItems)
                        {
                            string mode = item.CollectionTypeCode;
                            if (!string.IsNullOrWhiteSpace(item.Slot)) mode = $"{item.CollectionTypeCode} ({item.Slot})";
                            string norm = PaymentCollection.NormalizeCollectionTypeCode(item.CollectionTypeCode);
                            if (!CardSwipeDetails.Any(c =>
                                string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), norm, StringComparison.OrdinalIgnoreCase)
                                && Math.Abs(c.Amount - item.Amount) < 0.01
                                && string.Equals((c.Otp ?? "").Trim(), (item.Otp ?? "").Trim(), StringComparison.OrdinalIgnoreCase)))
                            {
                                CardSwipeDetails.Add(new DsmCardSwipeRow
                                {
                                    Mode = mode,
                                    Amount = item.Amount,
                                    Tid = item.Tid ?? "",
                                    Batch = item.Batch ?? "",
                                    Otp = item.Otp ?? "",
                                    Slot = item.Slot ?? "",
                                    TransactionId = item.TransactionId,
                                    OnRowChanged = RecalculateTotals
                                });
                            }
                        }
                    }

                    // Add all OTP items for standard collection types (PHONEPE, CREDIT_CARD, PETROCARD)
                    var standardOtpItems = allItems.Where(i => PaymentCollection.IsStandardCollectionCode(i.CollectionTypeCode) && !string.IsNullOrWhiteSpace(i.Otp)).ToList();
                    foreach (var item in standardOtpItems)
                    {
                        string mode = item.CollectionTypeCode;
                        if (!string.IsNullOrWhiteSpace(item.Slot)) mode = $"{item.CollectionTypeCode} ({item.Slot})";
                        string norm = PaymentCollection.NormalizeCollectionTypeCode(item.CollectionTypeCode);
                        if (!CardSwipeDetails.Any(c =>
                            string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), norm, StringComparison.OrdinalIgnoreCase)
                            && Math.Abs(c.Amount - item.Amount) < 0.01
                            && string.Equals((c.Otp ?? "").Trim(), (item.Otp ?? "").Trim(), StringComparison.OrdinalIgnoreCase)))
                        {
                            CardSwipeDetails.Add(new DsmCardSwipeRow
                            {
                                Mode = mode,
                                Amount = item.Amount,
                                Tid = item.Tid ?? "",
                                Batch = item.Batch ?? "",
                                Otp = item.Otp ?? "",
                                Slot = item.Slot ?? "",
                                TransactionId = item.TransactionId,
                                OnRowChanged = RecalculateTotals
                            });
                        }
                    }

                    PhonePeMorning = pc.PhonePeMorning;
                    PhonePeDay = pc.PhonePeDay;
                    PhonePeNight = pc.PhonePeNight;
                    CreditCardMorning = pc.CreditCardMorning;
                    CreditCardDay = pc.CreditCardDay;
                    CreditCardNight = pc.CreditCardNight;
                    PetroCardMorning = pc.PetroCardMorning;
                    PetroCardDay = pc.PetroCardDay;
                    PetroCardNight = pc.PetroCardNight;

                    if (PhonePeMorning == 0 && PhonePeDay == 0 && PhonePeNight == 0 && UpiAmount > 0)
                    {
                        if (approvedSub.ShiftType == "C") PhonePeNight = UpiAmount;
                        else if (approvedSub.ShiftType == "B") PhonePeDay = UpiAmount;
                        else PhonePeMorning = UpiAmount;
                    }
                    if (CreditCardMorning == 0 && CreditCardDay == 0 && CreditCardNight == 0 && CardAmount > 0)
                    {
                        if (approvedSub.ShiftType == "C") CreditCardNight = CardAmount;
                        else if (approvedSub.ShiftType == "B") CreditCardDay = CardAmount;
                        else CreditCardMorning = CardAmount;
                    }
                    if (PetroCardMorning == 0 && PetroCardDay == 0 && PetroCardNight == 0 && PetroCardAmount > 0)
                    {
                        if (approvedSub.ShiftType == "C") PetroCardNight = PetroCardAmount;
                        else if (approvedSub.ShiftType == "B") PetroCardDay = PetroCardAmount;
                        else PetroCardMorning = PetroCardAmount;
                    }

                    HasSplitValues = (PhonePeNight > 0 || CreditCardNight > 0 || PetroCardNight > 0 ||
                                      PhonePeDay > 0 || CreditCardDay > 0 || PetroCardDay > 0 ||
                                      approvedSub.ShiftType == "A" || approvedSub.ShiftType == "C");

                    // Clean up redundant totals safely using explicit row provenance and metadata
                    dynamic? appMeta = null;
                    if (!string.IsNullOrEmpty(approvedSub.MetadataJson))
                    {
                        try { appMeta = JsonConvert.DeserializeObject<dynamic>(approvedSub.MetadataJson); } catch {}
                    }

                    if (CardSwipeDetails.Count == 0 && appMeta?.machineTransactions != null)
                    {
                        foreach (var mt in appMeta.machineTransactions)
                        {
                            string mtCode = Convert.ToString((object?)(mt.collectionTypeCode ?? mt.code ?? "")) ?? "";
                            if (string.IsNullOrWhiteSpace(mtCode)) continue;
                            double mtAmt = Convert.ToDouble((object?)(mt.amount ?? 0.0));
                            string? mtOtp = Convert.ToString((object?)(mt.otp ?? mt.Otp ?? "")) ?? "";
                            string? mtTid = Convert.ToString((object?)(mt.tid ?? mt.Tid ?? "")) ?? "";
                            string? mtBatch = Convert.ToString((object?)(mt.batch ?? mt.Batch ?? "")) ?? "";
                            string? mtSlot = Convert.ToString((object?)(mt.slot ?? mt.Slot ?? "")) ?? "";
                            string? mtTxId = Convert.ToString((object?)(mt.transactionId ?? mt.TransactionId ?? mt.id ?? mt.Id ?? "")) ?? "";
                            if (mtAmt <= 0 || string.IsNullOrWhiteSpace(mtOtp)) continue;

                            string normMtCode = PaymentCollection.NormalizeCollectionTypeCode(mtCode);
                            if (!CardSwipeDetails.Any(c =>
                                string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normMtCode, StringComparison.OrdinalIgnoreCase)
                                && Math.Abs(c.Amount - mtAmt) < 0.01
                                && string.Equals((c.Otp ?? "").Trim(), (mtOtp ?? "").Trim(), StringComparison.OrdinalIgnoreCase)))
                            {
                                CardSwipeDetails.Add(new DsmCardSwipeRow
                                {
                                    Mode = string.IsNullOrWhiteSpace(mtSlot) ? mtCode : $"{mtCode} ({mtSlot})",
                                    Amount = mtAmt,
                                    Tid = mtTid ?? "",
                                    Batch = mtBatch ?? "",
                                    Otp = mtOtp ?? "",
                                    Slot = mtSlot,
                                    TransactionId = mtTxId,
                                    OnRowChanged = RecalculateTotals
                                });
                            }
                        }
                    }

                    CleanupRedundantTotals(appMeta, null);
                }
                else if (!string.IsNullOrEmpty(approvedSub.MetadataJson))
                {
                    try
                    {
                        var meta = JsonConvert.DeserializeObject<dynamic>(approvedSub.MetadataJson);
                        var redVal = meta?.collectionReduction ?? meta?.collection_reduction ?? meta?.Collections?.collectionReduction;
                        if (redVal != null)
                        {
                            CollectionReductionAmount = Convert.ToDouble((object)redVal);
                        }
                        var redRem = meta?.collectionReductionRemark ?? meta?.collection_reduction_remark ?? meta?.Collections?.collectionReductionRemark;
                        if (redRem != null)
                        {
                            CollectionReductionRemark = redRem.ToString();
                        }
                    }
                    catch {}
                }

                foreach (var t in entry.TestingEntries)
                {
                    SubmissionTestingEntries.Add(new DsmTestingRow
                    {
                        NozzleId = 0,
                        FuelType = t.FuelType,
                        Amount = t.Litres,
                        RupeeAmount = t.Amount,
                        PumpId = entry.PumpId
                    });
                }
            }

            if (!string.IsNullOrEmpty(approvedSub.MetadataJson))
            {
                try
                {
                    var metadata = JsonConvert.DeserializeObject<dynamic>(approvedSub.MetadataJson);
                    if (metadata != null && metadata.oilDefSales != null)
                    {
                        foreach (var sale in metadata.oilDefSales)
                        {
                            OilDefSales.Add(new DsmOilDefSaleRow
                            {
                                ProductName = sale.productName ?? "",
                                Quantity = (double)(sale.quantity ?? 0.0),
                                Price = (double)(sale.price ?? 0.0),
                                Unit = sale.unit ?? ""
                            });
                        }
                    }
                }
                catch {}
            }

            await PopulateCollectionTypesAndDynamicCollectionsAsync();
            RecalculateTotals();

            TimelineLogs.Add(new DsmTimelineLog { Title = "Submitted on PWA", Timestamp = approvedSub.SubmittedTimeDisplay, Description = $"DSM '{approvedSub.DsmName}' completed shift entry on mobile app.", IsCompleted = true });
            TimelineLogs.Add(new DsmTimelineLog { Title = "Synced to Desktop", Timestamp = approvedSub.SubmittedAt.AddMinutes(5).ToLocalTime().ToString("hh:mm tt"), Description = "Submission payload successfully fetched and cached locally.", IsCompleted = true });
            TimelineLogs.Add(new DsmTimelineLog { Title = "Approved by Manager", Timestamp = approvedSub.ApprovedTimeDisplay, Description = $"Manager '{approvedSub.ApprovedBy}' approved the shift totals.", IsCompleted = true });
            TimelineLogs.Add(new DsmTimelineLog { Title = "Shift Totals Updated", Timestamp = approvedSub.ApprovedTimeDisplay, Description = "Local SQLite ledgers and nozzle readings updated.", IsCompleted = true });
            TimelineLogs.Add(new DsmTimelineLog { Title = "Day Totals Updated", Timestamp = approvedSub.ApprovedTimeDisplay, Description = "Unified Day Total and Owner Dashboard reports updated.", IsCompleted = true });
            TimelineLogs.Add(new DsmTimelineLog { Title = "TID Sheet Updated", Timestamp = approvedSub.ApprovedTimeDisplay, Description = "TID terminal transactions registered for reconciliation.", IsCompleted = true });

            StatusMessage = "🔒 Approved submission loaded in read-only mode.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load approved submission details");
            StatusMessage = $"❌ Error loading: {ex.Message}";
        }
        finally
        {
            _isLoadingSubmission = false;
            IsLoadingDetails = false;
        }
    }

    private async Task<Result<List<dynamic>>> FetchApprovedSubmissionsFromSupabaseAsync()
    {
        try
        {
            var supabaseService = _serviceProvider.GetRequiredService<SupabaseDsmService>();
            return await supabaseService.FetchApprovedSubmissionsAsync();
        }
        catch (Exception ex)
        {
            return Result<List<dynamic>>.Fail(ex.Message);
        }
    }

    private async Task HealCorruptedApprovedEntriesAsync(FuelProDbContext context, List<DsmApprovalAudit> localAudits)
    {
        // No-op: Do not force overwrite nozzle readings with audit trail
        await Task.CompletedTask;
    }

    private async Task RegisterDeterministicApprovalMappingsAsync(
        FuelProDbContext context,
        Guid submissionId,
        DsmEntry savedEntry,
        DsmApprovalAudit audit,
        int? connectedPumpId,
        IEnumerable<int>? connectedPumpIds,
        List<DebitEntry>? debitModels,
        List<DsmDebtorRepaymentRow>? debtorRepayments,
        List<TestingEntry>? testingModels,
        List<Expense>? expenseModels,
        List<CashDenomination>? cashModels,
        List<DsmOilDefSaleRow>? oilDefSales,
        List<DsmQrPaymentEntry>? qrPayments)
    {
        try
        {
            // 1. DsmApprovalAudit
            if (audit.DsmApprovalAuditId > 0)
            {
                var auditGuid = DeterministicGuid.CreateDsmApprovalAuditGuid(submissionId);
                await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                    context, "DsmApprovalAudits", audit.DsmApprovalAuditId, auditGuid);
            }

            // 2. DsmEntry
            if (savedEntry.DsmEntryId > 0)
            {
                var dsmEntryGuid = DeterministicGuid.CreateDsmEntryGuid(submissionId, savedEntry.PumpId);
                await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                    context, "DsmEntries", savedEntry.DsmEntryId, dsmEntryGuid);
            }

            // 3. NozzleReadings for this entry
            var nozzleReadings = await context.NozzleReadings
                .Where(n => n.DsmEntryId == savedEntry.DsmEntryId)
                .ToListAsync();

            foreach (var nr in nozzleReadings)
            {
                var nrGuid = DeterministicGuid.CreateNozzleReadingGuid(submissionId, savedEntry.PumpId, nr.NozzleNumber);
                await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                    context, "NozzleReadings", nr.NozzleReadingId, nrGuid);
            }

            // 4. PaymentCollections
            var paymentColl = await context.PaymentCollections
                .FirstOrDefaultAsync(p => p.DsmEntryId == savedEntry.DsmEntryId);
            if (paymentColl != null && paymentColl.PaymentId > 0)
            {
                var payGuid = DeterministicGuid.CreatePaymentCollectionGuid(submissionId, savedEntry.PumpId);
                await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                    context, "PaymentCollections", paymentColl.PaymentId, payGuid);
            }

            // 5. CashDenominations
            var cashDenoms = await context.CashDenominations
                .Where(c => c.DsmEntryId == savedEntry.DsmEntryId)
                .ToListAsync();
            foreach (var cd in cashDenoms)
            {
                var cdGuid = DeterministicGuid.CreateCashDenominationGuid(submissionId, cd.CashType);
                await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                    context, "CashDenominations", cd.CashDenomId, cdGuid);
            }

            // 6. DebitEntries
            var debits = await context.DebitEntries
                .Where(d => d.DsmEntryId == savedEntry.DsmEntryId)
                .OrderBy(d => d.DebitId)
                .ToListAsync();
            for (int i = 0; i < debits.Count; i++)
            {
                var d = debits[i];
                var debitGuid = DeterministicGuid.CreateDebitEntryGuid(submissionId, i, d.SlipNumber);
                await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                    context, "DebitEntries", d.DebitId, debitGuid);
            }

            // 7. CreditorRepayments
            if (debtorRepayments != null && debtorRepayments.Count > 0)
            {
                var shift = savedEntry.Shift;
                if (shift != null)
                {
                    var repayments = await context.CreditorRepayments
                        .Where(r => r.RepaymentDate == shift.ShiftDate && r.ShiftNumber == shift.ShiftType)
                        .OrderBy(r => r.CreditorRepaymentId)
                        .ToListAsync();
                    for (int i = 0; i < repayments.Count; i++)
                    {
                        var rep = repayments[i];
                        var repGuid = DeterministicGuid.CreateCreditorRepaymentGuid(submissionId, i, rep.CreditorName);
                        await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                            context, "CreditorRepayments", rep.CreditorRepaymentId, repGuid);
                    }
                }
            }

            // 8. TestingEntries
            var testings = await context.TestingEntries
                .Where(t => t.DsmEntryId == savedEntry.DsmEntryId)
                .OrderBy(t => t.TestingId)
                .ToListAsync();
            for (int i = 0; i < testings.Count; i++)
            {
                var t = testings[i];
                var testGuid = DeterministicGuid.CreateTestingEntryGuid(submissionId, t.FuelType, i);
                await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                    context, "TestingEntries", t.TestingId, testGuid);
            }

            // 9. Expenses
            var expenses = await context.Expenses
                .Where(e => e.DsmEntryId == savedEntry.DsmEntryId)
                .OrderBy(e => e.ExpenseId)
                .ToListAsync();
            for (int i = 0; i < expenses.Count; i++)
            {
                var e = expenses[i];
                var expGuid = DeterministicGuid.CreateExpenseGuid(submissionId, i);
                await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                    context, "Expenses", e.ExpenseId, expGuid);
            }

            // 10. OilDefDailyLogs
            if (oilDefSales != null && oilDefSales.Count > 0)
            {
                var oilSales = await context.OilDefDailyLogs
                    .Where(o => o.ShiftId == savedEntry.ShiftId)
                    .OrderBy(o => o.Id)
                    .ToListAsync();
                for (int i = 0; i < oilSales.Count; i++)
                {
                    var o = oilSales[i];
                    var oilGuid = DeterministicGuid.CreateOilDefSaleGuid(submissionId, o.ProductId, i);
                    await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                        context, "OilDefDailyLogs", o.Id, oilGuid);
                }
            }

            // 11. DsmQrPayments
            if (qrPayments != null && qrPayments.Count > 0)
            {
                var qrs = await context.DsmQrPayments
                    .Where(q => q.DsmEntryId == savedEntry.DsmEntryId)
                    .OrderBy(q => q.Id)
                    .ToListAsync();
                for (int i = 0; i < qrs.Count; i++)
                {
                    var q = qrs[i];
                    var qrGuid = DeterministicGuid.CreateQrPaymentGuid(submissionId, i, q.Tid);
                    await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                        context, "DsmQrPayments", q.Id, qrGuid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to register deterministic sync mappings for submission {SubId}", submissionId);
        }
    }
}

public class DsmApprovedSubmissionDto : ObservableObject
{
    public Guid SubmissionId { get; set; }
    public int DsmEntryId { get; set; }
    public string DsmName { get; set; } = string.Empty;
    public string DsmUserId { get; set; } = string.Empty;
    public int PumpId { get; set; }
    public string ShiftType { get; set; } = "A";
    public DateTime ShiftDate { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime ApprovedAt { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
    public double GrossSales { get; set; }
    public double TotalCollection { get; set; }
    public double Mismatch { get; set; }
    public string Status { get; set; } = "Approved";
    public string MetadataJson { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string? AttachmentUrl { get; set; }

    public List<int> ConnectedPumpIds
    {
        get
        {
            if (string.IsNullOrEmpty(MetadataJson)) return new List<int>();
            try
            {
                var metaObj = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(MetadataJson);
                if (metaObj != null)
                {
                    var list = new List<int>();
                    if (metaObj["connectedPumpIds"] != null)
                    {
                        var arr = metaObj["connectedPumpIds"]!.ToObject<List<int>>();
                        if (arr != null) list.AddRange(arr);
                    }
                    else if (metaObj["connectedPumpId"] != null)
                    {
                        var scalar = (int?)metaObj["connectedPumpId"];
                        if (scalar.HasValue && scalar.Value > 0) list.Add(scalar.Value);
                    }
                    return list.Where(id => id != PumpId && id > 0).Distinct().ToList();
                }
            }
            catch {}
            return new List<int>();
        }
    }

    public int? ConnectedPumpId => ConnectedPumpIds.Count > 0 ? ConnectedPumpIds[0] : null;

    public string TitleDisplay
    {
        get
        {
            var connected = ConnectedPumpIds;
            string baseTitle;
            if (connected.Count > 0)
            {
                var connectedStr = string.Join(" + ", connected.Select(id => $"Pump {id}"));
                baseTitle = $"{DsmName} - Pump {PumpId} + {connectedStr} (Connected) - Shift {ShiftType}";
            }
            else
            {
                baseTitle = $"{DsmName} - Pump {PumpId} - Shift {ShiftType}";
            }
            return baseTitle;
        }
    }
    public string ShiftDateDisplay => ShiftDate.ToString("dd MMM yyyy");
    public string SubmittedTimeDisplay => SubmittedAt.ToLocalTime().ToString("hh:mm tt");
    public string ApprovedTimeDisplay => ApprovedAt.ToLocalTime().ToString("hh:mm tt");

    public string StationName
    {
        get
        {
            if (string.IsNullOrEmpty(MetadataJson)) return string.Empty;
            try
            {
                var metaObj = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(MetadataJson);
                if (metaObj != null)
                {
                    var name = (string?)metaObj["stationName"] ?? (string?)metaObj["StationName"] ?? (string?)metaObj["station_name"];
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
            }
            catch {}
            return string.Empty;
        }
    }

    public string PersonalLedgerName
    {
        get
        {
            if (string.IsNullOrEmpty(MetadataJson)) return string.Empty;
            try
            {
                var metaObj = JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(MetadataJson);
                if (metaObj != null)
                {
                    var name = (string?)metaObj["personalLedgerName"] ?? (string?)metaObj["PersonalLedgerName"] ?? (string?)metaObj["personal_ledger_name"];
                    if (!string.IsNullOrWhiteSpace(name)) return name;
                }
            }
            catch {}
            return string.Empty;
        }
    }
}


public class DsmTimelineLog
{
    public string Title { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}

internal sealed class SettlementMatchRecord
{
    public string PaymentType { get; set; } = string.Empty;
    public string Period { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string Tid { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public bool IsConsumed { get; set; }
}


