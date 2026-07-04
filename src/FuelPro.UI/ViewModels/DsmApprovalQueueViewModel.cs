using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Core.Repositories;
using FuelPro.Data;
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

    public string TitleDisplay => $"{DsmName} - Pump {PumpId} - Shift {ShiftType}";
    public string ShiftDateDisplay => ShiftDate.ToString("dd MMM yyyy");
    public string SubmittedTimeDisplay => SubmittedAt.ToLocalTime().ToString("hh:mm tt");
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
    [ObservableProperty] private string _continuityWarning = "";
    [ObservableProperty] private bool _hasContinuityError;

    partial void OnOpeningReadingChanged(double value) => Recalculate();
    partial void OnClosingReadingChanged(double value) => Recalculate();
    partial void OnRateChanged(double value) => Recalculate();

    public void Recalculate()
    {
        SaleLitres = Math.Max(0, ClosingReading - OpeningReading);
        Amount = SaleLitres * Rate;
        
        if (ExpectedOpening.HasValue)
        {
            HasContinuityError = Math.Abs(OpeningReading - ExpectedOpening.Value) > 0.0001;
            ContinuityWarning = HasContinuityError 
                ? $"⚠️ Continuity Gap: Opening ({OpeningReading}) does not match previous closing ({ExpectedOpening.Value})" 
                : "";
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

    partial void OnAmountChanged(double value) => OnRowChanged?.Invoke();
}

public partial class DsmCardSwipeRow : ObservableObject
{
    public Action? OnRowChanged { get; set; }
    [ObservableProperty] private string _mode = "";
    [ObservableProperty] private double _amount;
    [ObservableProperty] private string _tid = "";
    [ObservableProperty] private string _batch = "";

    partial void OnAmountChanged(double value) => OnRowChanged?.Invoke();
    partial void OnModeChanged(string value) => OnRowChanged?.Invoke();
}

public class DsmTestingRow
{
    public int NozzleId { get; set; }
    public string FuelType { get; set; } = "";
    public double Amount { get; set; }
    public int PumpId { get; set; }
}

public partial class DsmApprovalQueueViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SupabaseDsmService _supabaseService;
    private readonly DsmEntryService _dsmEntryService;
    private readonly INozzleReadingRepository _nozzleRepo;
    private readonly IDsmCalculationService _dsmCalculationService;
    private readonly AuthService _authService;
    private readonly DsmSubmissionPollingService _pollingService;
    private readonly ILogger _logger = Log.ForContext<DsmApprovalQueueViewModel>();

    public ObservableCollection<DsmPendingSubmission> PendingSubmissions { get; } = new();
    public List<DsmTestingRow> SubmissionTestingEntries { get; } = new();
    
    [ObservableProperty] private DsmPendingSubmission? _selectedSubmission;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isLoadingSubmissions;
    [ObservableProperty] private bool _isLoadingDetails;

    // Detailed Editing Fields
    [ObservableProperty] private double _cashAmount;
    [ObservableProperty] private double _upiAmount;
    [ObservableProperty] private double _cardAmount;
    [ObservableProperty] private double _petroCardAmount;
    [ObservableProperty] private double _cashDepositAmount;
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
    [ObservableProperty] private double _totalCollections;
    [ObservableProperty] private double _mismatchAmount; // Collections - GrossSales

    public ObservableCollection<DsmNozzleRow> NozzleReadings { get; } = new();
    public ObservableCollection<DsmDebitRow> DebtorEntries { get; } = new();
    public ObservableCollection<DsmCardSwipeRow> CardSwipeDetails { get; } = new();

    // Validations & Overrides
    [ObservableProperty] private bool _hasContinuityWarnings;
    [ObservableProperty] private bool _overrideContinuity;
    [ObservableProperty] private string _overrideRemark = "";
    [ObservableProperty] private string _validationSummary = "";

    // Edit UI Visibility
    [ObservableProperty] private bool _isEditing;

    public DsmApprovalQueueViewModel()
    {
        _serviceProvider = App.Services;
        _supabaseService = _serviceProvider.GetRequiredService<SupabaseDsmService>();
        _dsmEntryService = _serviceProvider.GetRequiredService<DsmEntryService>();
        _nozzleRepo = _serviceProvider.GetRequiredService<INozzleReadingRepository>();
        _dsmCalculationService = _serviceProvider.GetRequiredService<IDsmCalculationService>();
        _authService = _serviceProvider.GetRequiredService<AuthService>();
        _pollingService = _serviceProvider.GetRequiredService<DsmSubmissionPollingService>();

        _ = RefreshQueueAsync();
    }

    [RelayCommand]
    public async Task RefreshQueueAsync()
    {
        IsLoadingSubmissions = true;
        StatusMessage = "⏳ Querying Supabase for pending shift entries...";
        SelectedSubmission = null;

        try
        {
            var result = await _supabaseService.FetchPendingSubmissionsAsync();
            PendingSubmissions.Clear();

            if (result.Success && result.Data != null)
            {
                foreach (var item in result.Data)
                {
                    // Filter out expired submissions (older than 48 hours)
                    DateTime submittedAt = item.SubmittedAt;
                    if (DateTime.UtcNow - submittedAt > TimeSpan.FromHours(48))
                    {
                        // Submissions older than 48 hours are treated as expired
                        continue;
                    }

                    PendingSubmissions.Add(new DsmPendingSubmission
                    {
                        Id = item.Id,
                        DsmName = item.DsmUsers?.FullName ?? "Unknown DSM",
                        DsmUserId = item.DsmUserId,
                        PumpId = (int)item.PumpId,
                        ShiftDate = item.ShiftDate,
                        ShiftType = item.ShiftType,
                        SubmittedAt = submittedAt,
                        Notes = item.Notes ?? "",
                        AttachmentUrl = item.AttachmentUrl,
                        MetadataJson = item.Metadata != null ? JsonConvert.SerializeObject(item.Metadata) : ""
                    });
                }
                
                StatusMessage = PendingSubmissions.Count > 0 
                    ? $"Found {PendingSubmissions.Count} pending submissions." 
                    : "No pending submissions found.";
            }
            else
            {
                StatusMessage = $"❌ Failed to load: {result.Error}";
            }

            // Force poll refresh
            await _pollingService.ForceRefreshAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh DSM approval queue");
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsLoadingSubmissions = false;
        }
    }

    partial void OnSelectedSubmissionChanged(DsmPendingSubmission? value)
    {
        if (value != null)
        {
            _ = LoadSubmissionDetailsAsync(value);
        }
        else
        {
            NozzleReadings.Clear();
            IsEditing = false;
        }
    }

    private async Task LoadSubmissionDetailsAsync(DsmPendingSubmission submission)
    {
        IsLoadingDetails = true;
        StatusMessage = $"⏳ Loading details for {submission.DsmName}...";
        IsEditing = true;

        try
        {
            // 1. Fetch readings from Supabase
            var readingsResult = await _supabaseService.FetchSubmissionReadingsAsync(submission.Id);
            NozzleReadings.Clear();
            SubmissionTestingEntries.Clear();

            // 2. Query continuity closings from local SQLite
            var prevResult = await _nozzleRepo.GetPreviousShiftClosingsAsync(submission.ShiftDate, submission.ShiftType, submission.PumpId);
            var prevClosings = prevResult.Success ? prevResult.Data! : new Dictionary<int, double>();

            if (readingsResult.Success && readingsResult.Data != null)
            {
                foreach (var r in readingsResult.Data)
                {
                    int nozzleId = r.NozzleId;
                    double opening = r.OpeningReading;
                    double closing = r.ClosingReading;
                    double rate = r.Rate;
                    
                    double? expectedOpening = prevClosings.TryGetValue(nozzleId, out var prevClosing) 
                        ? prevClosing 
                        : null;

                    int nozzlePumpId = r.PumpId != null ? (int)r.PumpId : (int)submission.PumpId;

                    var row = new DsmNozzleRow
                    {
                        NozzleId = nozzleId,
                        FuelType = GetFuelTypeName(nozzlePumpId, nozzleId, submission.ShiftDate),
                        OpeningReading = opening,
                        ClosingReading = closing,
                        Rate = rate,
                        ExpectedOpening = expectedOpening,
                        OnRowChanged = RecalculateTotals
                    };
                    
                    row.Recalculate();
                    NozzleReadings.Add(row);
                }
            }

            // 3. Fetch collections from Supabase
            var collResult = await _supabaseService.FetchSubmissionCollectionAsync(submission.Id);
            if (collResult.Success && collResult.Data != null)
            {
                var coll = collResult.Data;
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

            // 4. Parse editable collections (Debtor Entries and Card Swipe Details) from MetadataJson
            DebtorEntries.Clear();
            CardSwipeDetails.Clear();
            if (!string.IsNullOrEmpty(submission.MetadataJson))
            {
                try
                {
                    var metadata = JsonConvert.DeserializeObject<dynamic>(submission.MetadataJson);
                    if (metadata != null)
                    {
                        if (metadata.debtorEntries != null)
                        {
                            foreach (var dbEntry in metadata.debtorEntries)
                            {
                                DebtorEntries.Add(new DsmDebitRow
                                {
                                    DebtorName = dbEntry.debtorName ?? "",
                                    Amount = (double)(dbEntry.amount ?? 0.0),
                                    VehicleNumber = dbEntry.vehicleNumber,
                                    SlipNumber = dbEntry.slipNumber ?? "",
                                    EntryTime = dbEntry.time,
                                    OnRowChanged = RecalculateTotals
                                });
                            }
                        }
                        if (metadata.cardSwipeDetails != null)
                        {
                            foreach (var swipe in metadata.cardSwipeDetails)
                            {
                                CardSwipeDetails.Add(new DsmCardSwipeRow
                                {
                                    Mode = swipe.mode ?? "",
                                    Amount = (double)(swipe.amount ?? 0.0),
                                    Tid = swipe.tid ?? "",
                                    Batch = swipe.batch ?? "",
                                    OnRowChanged = RecalculateTotals
                                });
                            }
                        }
                        if (metadata.testingEntries != null)
                        {
                            foreach (var test in metadata.testingEntries)
                            {
                                SubmissionTestingEntries.Add(new DsmTestingRow
                                {
                                    NozzleId = (int)(test.nozzleId ?? 0),
                                    FuelType = test.fuelType ?? "",
                                    Amount = (double)(test.amount ?? 0.0),
                                    PumpId = (int)(test.pumpId ?? submission.PumpId)
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to parse metadata JSON for details view");
                }
            }

            SubmissionNotes = submission.Notes;
            AttachmentUrl = submission.AttachmentUrl;

            RecalculateTotals();
            StatusMessage = "Submission details loaded.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load details for submission {SubId}", submission.Id);
            StatusMessage = $"❌ Details load error: {ex.Message}";
        }
        finally
        {
            IsLoadingDetails = false;
        }
    }

    private string GetFuelTypeName(int pumpId, int nozzleNumber, DateTime date)
    {
        try
        {
            var fuelType = PumpConfiguration.GetFuelType(pumpId, nozzleNumber, date);
            return fuelType.ToDisplayName();
        }
        catch
        {
            return "MS";
        }
    }

    private void RecalculateTotals()
    {
        GrossSales = NozzleReadings.Sum(r => r.Amount);
        double totalTestingAmount = SubmissionTestingEntries.Sum(t => 
        {
            var nozzleRow = NozzleReadings.FirstOrDefault(n => n.NozzleId == t.NozzleId);
            return t.Amount * (nozzleRow != null ? nozzleRow.Rate : 0.0);
        });
        TotalCollections = UpiAmount + CardAmount + CashAmount + CreditAmount + PetroCardAmount + CashDepositAmount + OthersAmount + totalTestingAmount;
        
        // Mismatch is computed: Collections + Expense - (GrossSales + Excess/Short)
        // Let's use the DsmCalculationService logic to keep it consistent!
        var dto = new FuelPro.Core.DTOs.DsmEntryDto
        {
            NozzleReadings = NozzleReadings.Select(n => new FuelPro.Core.DTOs.NozzleReadingDto { Amount = (decimal)n.Amount }).ToList(),
            PaymentCollection = new FuelPro.Core.DTOs.PaymentCollectionDto
            {
                PhonePe = (decimal)UpiAmount,
                CreditCard = (decimal)CardAmount,
                CashDeposit = (decimal)CashDepositAmount,
                PhysicalCash = (decimal)CashAmount,
                PetroCard = (decimal)PetroCardAmount,
                Others = (decimal)OthersAmount
            },
            DebitEntries = DebtorEntries.Select(d => new FuelPro.Core.DTOs.DebitEntryDto { Amount = (decimal)d.Amount }).ToList(),
            Expenses = new List<FuelPro.Core.DTOs.ExpenseDto> { new() { Amount = (decimal)ExpenseAmount } },
            TestingEntries = SubmissionTestingEntries.Select(t => 
            {
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

        var calc = _dsmCalculationService.Calculate(dto);
        MismatchAmount = (double)calc.Mismatch;

        // Check continuity warnings
        HasContinuityWarnings = NozzleReadings.Any(r => r.HasContinuityError);
        
        // Build validation summary
        var errors = new List<string>();
        if (HasContinuityWarnings)
            errors.Add("• Continuity warnings detected: DSM opening readings don't match SQLite history.");
        if (Math.Abs(MismatchAmount) > 500)
            errors.Add($"• High Mismatch: Mismatch is ₹{MismatchAmount:F2} (threshold ₹500).");
        if (NozzleReadings.Any(r => r.ClosingReading < r.OpeningReading))
            errors.Add("• Closing reading is less than opening reading.");

        ValidationSummary = errors.Count > 0 
            ? string.Join(Environment.NewLine, errors) 
            : "✓ Calculations and continuity are correct.";
    }

    partial void OnCashAmountChanged(double value) => RecalculateTotals();
    partial void OnUpiAmountChanged(double value) => RecalculateTotals();
    partial void OnCardAmountChanged(double value) => RecalculateTotals();
    partial void OnPetroCardAmountChanged(double value) => RecalculateTotals();
    partial void OnCashDepositAmountChanged(double value) => RecalculateTotals();
    partial void OnOthersAmountChanged(double value) => RecalculateTotals();
    partial void OnCreditAmountChanged(double value) => RecalculateTotals();
    partial void OnExpenseAmountChanged(double value) => RecalculateTotals();

    [RelayCommand]
    private async Task ApproveAsync()
    {
        if (SelectedSubmission == null) return;

        // Validation Rules
        if (HasContinuityWarnings && !OverrideContinuity)
        {
            MessageBox.Show("Cannot approve: Continuity warnings exist. Check 'Override continuity warnings' checkbox.", 
                "Continuity Mismatch", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (HasContinuityWarnings && string.IsNullOrWhiteSpace(OverrideRemark))
        {
            MessageBox.Show("Cannot approve: Overridden continuity mismatch requires a manager's override remark/note.", 
                "Remark Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (NozzleReadings.Any(r => r.ClosingReading < r.OpeningReading))
        {
            MessageBox.Show("Cannot approve: Closing reading cannot be less than opening reading.", 
                "Reading Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusMessage = "⏳ Locking submission and saving to local SQLite...";
        
        try
        {
            // 1. Generate lock token and acquire lock in Supabase (Optimistic Locking)
            var lockId = Guid.NewGuid().ToString();
            var approvedBy = _authService.CurrentUser?.Username ?? "Manager";

            var lockResult = await _supabaseService.ApproveSubmissionAsync(SelectedSubmission.Id, approvedBy, lockId);
            if (!lockResult.Success)
            {
                MessageBox.Show($"Approval failed: {lockResult.Error}", "Concurrency Conflict", MessageBoxButton.OK, MessageBoxImage.Error);
                await RefreshQueueAsync();
                return;
            }

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
            double upiNight = 0;
            double upiCardMorning = 0;
            double upiCardNight = 0;
            double creditCardMorning = 0;
            double creditCardNight = 0;
            double petroCardMorning = 0;
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
            string? phonePeTidNight = null;
            string? phonePeBatchNight = null;

            string? creditCardTidMorning = null;
            string? creditCardBatchMorning = null;
            string? creditCardTidNight = null;
            string? creditCardBatchNight = null;
            string? petroCardTidMorning = null;
            string? petroCardBatchMorning = null;
            string? petroCardTidNight = null;
            string? petroCardBatchNight = null;

            bool isNight = string.Equals(SelectedSubmission.ShiftType, "B", StringComparison.OrdinalIgnoreCase);

            foreach (var swipe in CardSwipeDetails)
            {
                string mode = swipe.Mode ?? "";
                double amount = swipe.Amount;
                string tid = swipe.Tid ?? "";
                string batch = swipe.Batch ?? "";

                bool isSwipeNight = mode.Contains("Night", StringComparison.OrdinalIgnoreCase) || 
                                    (!mode.Contains("Morning", StringComparison.OrdinalIgnoreCase) && isNight);

                if (mode.Contains("PhonePe Card", StringComparison.OrdinalIgnoreCase) || 
                    mode.Contains("PhonePe UPI", StringComparison.OrdinalIgnoreCase) || 
                    mode.Contains("PhonePe", StringComparison.OrdinalIgnoreCase))
                {
                    // Check if it's PhonePe Card vs PhonePe UPI
                    bool isCard = mode.Contains("Card", StringComparison.OrdinalIgnoreCase);
                    if (isCard)
                    {
                        if (isSwipeNight)
                        {
                            upiCardNight += amount;
                            phonePeTidNight = tid;
                            phonePeBatchNight = batch;
                        }
                        else
                        {
                            upiCardMorning += amount;
                            phonePeTidMorning = tid;
                            phonePeBatchMorning = batch;
                        }
                    }
                    else
                    {
                        if (isSwipeNight)
                        {
                            upiNight += amount;
                            phonePeTidNight = tid;
                            phonePeBatchNight = batch;
                        }
                        else
                        {
                            upiMorning += amount;
                            phonePeTidMorning = tid;
                            phonePeBatchMorning = batch;
                        }
                    }
                    phonePeTid = tid;
                    phonePeBatch = batch;
                }
                else if (mode.Contains("Petro", StringComparison.OrdinalIgnoreCase))
                {
                    if (isSwipeNight)
                    {
                        petroCardNight += amount;
                        petroCardTidNight = tid;
                        petroCardBatchNight = batch;
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
                else
                {
                    // Credit Card / Pinelabs Card / etc.
                    if (isSwipeNight)
                    {
                        creditCardNight += amount;
                        creditCardTidNight = tid;
                        creditCardBatchNight = batch;
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
            }

            // Fallbacks for backward compatibility / plain amount inputs (only when NO card swipe details exist)
            if (CardSwipeDetails.Count == 0)
            {
                if (upiMorning == 0 && upiNight == 0 && UpiAmount > 0)
                {
                    if (isNight) upiNight = UpiAmount;
                    else upiMorning = UpiAmount;
                }
                if (creditCardMorning == 0 && creditCardNight == 0 && CardAmount > 0)
                {
                    if (isNight) creditCardNight = CardAmount;
                    else creditCardMorning = CardAmount;
                }
                if (petroCardMorning == 0 && petroCardNight == 0 && PetroCardAmount > 0)
                {
                    if (isNight) petroCardNight = PetroCardAmount;
                    else petroCardMorning = PetroCardAmount;
                }
            }

            var payment = new PaymentCollection
            {
                CashDeposit = cashDeposit,
                PhonePeMorning = upiMorning,
                PhonePeNight = upiNight,
                PhonePeCardMorning = upiCardMorning,
                PhonePeCardNight = upiCardNight,
                CreditCardMorning = creditCardMorning,
                CreditCardNight = creditCardNight,
                PetroCardMorning = petroCardMorning,
                PetroCardNight = petroCardNight,
                Others = others,
                CardTid = isNight ? creditCardTidNight : creditCardTidMorning,
                CardBatch = isNight ? creditCardBatchNight : creditCardBatchMorning,
                PhonePeTid = isNight ? phonePeTidNight : phonePeTidMorning,
                PhonePeBatch = isNight ? phonePeBatchNight : phonePeBatchMorning,
                PetroCardTid = isNight ? petroCardTidNight : petroCardTidMorning,
                PetroCardBatch = isNight ? petroCardBatchNight : petroCardBatchMorning,
                PhonePeTidMorning = phonePeTidMorning,
                PhonePeBatchMorning = phonePeBatchMorning,
                PhonePeTidNight = phonePeTidNight,
                PhonePeBatchNight = phonePeBatchNight,
                CreditCardTidMorning = creditCardTidMorning,
                CreditCardBatchMorning = creditCardBatchMorning,
                CreditCardTidNight = creditCardTidNight,
                CreditCardBatchNight = creditCardBatchNight,
                PetroCardTidMorning = petroCardTidMorning,
                PetroCardBatchMorning = petroCardBatchMorning,
                PetroCardTidNight = petroCardTidNight,
                PetroCardBatchNight = petroCardBatchNight
            };

            // Build Debtor Entries
            var debitModels = new List<DebitEntry>();
            foreach (var dbEntry in DebtorEntries)
            {
                debitModels.Add(new DebitEntry
                {
                    DebtorName = dbEntry.DebtorName,
                    Amount = dbEntry.Amount,
                    VehicleNumber = string.IsNullOrEmpty(dbEntry.VehicleNumber) ? null : dbEntry.VehicleNumber,
                    ChequeNo = string.IsNullOrEmpty(dbEntry.SlipNumber) ? null : dbEntry.SlipNumber,
                    EntryTime = string.IsNullOrEmpty(dbEntry.EntryTime) ? null : dbEntry.EntryTime,
                    PaymentMethod = "Credit"
                });
            }

            // Fallback for CreditAmount if no debtor entries exist
            if (debitModels.Count == 0 && CreditAmount > 0)
            {
                debitModels.Add(new DebitEntry
                {
                    DebtorName = "DSM PWA Credit",
                    Amount = CreditAmount,
                    PaymentMethod = "Credit"
                });
            }

            // Parse Personal Debtors
            var personalDebtors = new List<DsmPersonalDebtor>();
            if (!string.IsNullOrEmpty(SelectedSubmission.MetadataJson))
            {
                try
                {
                    var metadata = JsonConvert.DeserializeObject<dynamic>(SelectedSubmission.MetadataJson);
                    if (metadata != null && metadata.personalDebtors != null)
                    {
                        foreach (var pd in metadata.personalDebtors)
                        {
                            double amount = (double)(pd.amount ?? 0.0);
                            string fuelProduct = pd.fuelProduct ?? "";
                            string remarks = pd.remarks ?? "";
                            string paymentMethod = pd.paymentMethod ?? "Cash";
                            string tid = pd.tid ?? "";
                            string batch = pd.batch ?? "";

                            personalDebtors.Add(new DsmPersonalDebtor
                            {
                                DsmName = SelectedSubmission.DsmName,
                                Date = SelectedSubmission.ShiftDate,
                                Time = DateTime.Now.ToString("hh:mm tt"),
                                Amount = amount,
                                FuelProduct = string.IsNullOrEmpty(fuelProduct) ? null : fuelProduct,
                                Remarks = string.IsNullOrEmpty(remarks) ? null : remarks,
                                PaymentMethod = paymentMethod,
                                CardTid = string.IsNullOrEmpty(tid) ? null : tid,
                                CardBatch = string.IsNullOrEmpty(batch) ? null : batch,
                                Denom500 = (int)(pd.denom500 ?? 0),
                                Denom200 = (int)(pd.denom200 ?? 0),
                                Denom100 = (int)(pd.denom100 ?? 0),
                                Denom50 = (int)(pd.denom50 ?? 0),
                                Denom20 = (int)(pd.denom20 ?? 0),
                                Denom10 = (int)(pd.denom10 ?? 0),
                                Coins = (int)(pd.coins ?? 0)
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to parse personalDebtors from metadata JSON");
                }
            }

            // Parse cash denominations, connected pump, and testing entries
            int denom500 = 0, denom200 = 0, denom100 = 0, denom50 = 0, denom20 = 0, denom10 = 0, coins = 0;
            int? connectedPumpId = null;
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
                        if (metadata.connectedPumpId != null)
                        {
                            connectedPumpId = (int?)metadata.connectedPumpId;
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
                                    FuelType = test.fuelType ?? "",
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
                    _logger.Error(ex, "Failed to parse cashDenominations/connectedPumpId/testingEntries from metadata JSON");
                }
            }

            var expenseModels = ExpenseAmount > 0
                ? new List<Expense> { new() { Description = string.IsNullOrEmpty(ExpenseNotes) ? "DSM PWA Expense" : ExpenseNotes, Amount = ExpenseAmount } }
                : new List<Expense>();

            // Cash1 is Bank Deposit, Cash2 is Cash in Hand
            var cashModels = new List<CashDenomination>
            {
                new() { CashType = "Cash1", TotalAmount = CashDepositAmount },
                new() 
                { 
                    CashType = "Cash2", 
                    TotalAmount = CashAmount,
                    Denom500 = denom500,
                    Denom200 = denom200,
                    Denom100 = denom100,
                    Denom50 = denom50,
                    Denom20 = denom20,
                    Denom10 = denom10,
                    Coins = coins
                }
            };

            // 3. Save locally via existing service (zero calculation redundancy!)
            var localSaveResult = await _dsmEntryService.SaveCompleteEntryAsync(
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
                personalDebtors: new List<DsmPersonalDebtor>()
            );

            if (!localSaveResult.Success)
            {
                // Rollback in Supabase: reset status to Pending
                await _supabaseService.RejectSubmissionAsync(SelectedSubmission.Id, "Rollback: Local database save failed.");
                MessageBox.Show($"WPF local save failed: {localSaveResult.Error}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                await RefreshQueueAsync();
                return;
            }

            var savedEntry = localSaveResult.Data!;

            // 4. Create local DsmApprovalAudit record (will sync back to Supabase)
            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
            
            var originalData = new
            {
                Readings = NozzleReadings.Select(r => new { r.NozzleId, r.OpeningReading, r.ClosingReading, r.Rate }),
                Collections = new { Cash = CashAmount, UPI = UpiAmount, Card = CardAmount, Credit = CreditAmount, Expense = ExpenseAmount, ExpenseNotes }
            };

            var approvedData = new
            {
                DsmEntryId = savedEntry.DsmEntryId,
                ShiftId = savedEntry.ShiftId,
                savedEntry.GrossSales,
                savedEntry.TotalCollection,
                savedEntry.Mismatch
            };

            var audit = new DsmApprovalAudit
            {
                SubmissionId = SelectedSubmission.Id,
                OriginalDataJson = JsonConvert.SerializeObject(originalData),
                ApprovedDataJson = JsonConvert.SerializeObject(approvedData),
                ApprovedBy = approvedBy,
                ApprovedAt = DateTime.Now,
                Remarks = HasContinuityWarnings ? $"Override Continuity: {OverrideRemark}" : "Approved as-is."
            };

            context.DsmApprovalAudits.Add(audit);
            await context.SaveChangesAsync();

            // 5. Send approval notification to DSM
            var settings = await _serviceProvider.GetRequiredService<SyncConfigService>().GetSettingsAsync();
            var approvalMessage = $"Your shift submission for {SelectedSubmission.ShiftDateDisplay} Shift {SelectedSubmission.ShiftType} on Pump {SelectedSubmission.PumpId} has been approved.";
            await _supabaseService.SendNotificationAsync(settings.StationId, SelectedSubmission.DsmUserId, "DSM", approvalMessage);

            // 6. Push local change to Supabase immediately
            var syncEngine = _serviceProvider.GetRequiredService<SyncEngine>();
            _ = syncEngine.ForceSyncAsync();

            MessageBox.Show("DSM Submission approved successfully!", "Approved", MessageBoxButton.OK, MessageBoxImage.Information);
            
            SelectedSubmission = null;
            OverrideRemark = "";
            OverrideContinuity = false;
            
            await RefreshQueueAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Approval process failed for submission {SubId}", SelectedSubmission.Id);
            MessageBox.Show($"Approval failed: {ex.Message}", "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
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
                // Send notification to DSM
                var settings = await _serviceProvider.GetRequiredService<SyncConfigService>().GetSettingsAsync();
                var rejectMessage = $"Your shift submission for {SelectedSubmission.ShiftDateDisplay} Shift {SelectedSubmission.ShiftType} on Pump {SelectedSubmission.PumpId} has been rejected. Reason: {reason}";
                await _supabaseService.SendNotificationAsync(settings.StationId, SelectedSubmission.DsmUserId, "DSM", rejectMessage);

                MessageBox.Show("DSM Submission rejected.", "Rejected", MessageBoxButton.OK, MessageBoxImage.Information);
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
}

