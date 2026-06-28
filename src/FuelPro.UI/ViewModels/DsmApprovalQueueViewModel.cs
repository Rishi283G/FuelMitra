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
    
    [ObservableProperty] private DsmPendingSubmission? _selectedSubmission;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isLoadingSubmissions;
    [ObservableProperty] private bool _isLoadingDetails;

    // Detailed Editing Fields
    [ObservableProperty] private double _cashAmount;
    [ObservableProperty] private double _upiAmount;
    [ObservableProperty] private double _cardAmount;
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

                    var row = new DsmNozzleRow
                    {
                        NozzleId = nozzleId,
                        FuelType = GetFuelTypeName((int)submission.PumpId, nozzleId, submission.ShiftDate),
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
                CreditAmount = coll.Credit;
                ExpenseAmount = coll.Expense;
                ExpenseNotes = coll.ExpenseNotes ?? "";
                ShortAmount = coll.Short;
                ExcessAmount = coll.Excess;
            }
            else
            {
                CashAmount = UpiAmount = CardAmount = CreditAmount = ExpenseAmount = ShortAmount = ExcessAmount = 0;
                ExpenseNotes = "";
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
        TotalCollections = UpiAmount + CardAmount + CashAmount + CreditAmount;
        
        // Mismatch is computed: Collections + Expense - (GrossSales + Excess/Short)
        // Let's use the DsmCalculationService logic to keep it consistent!
        var cash1Total = CashAmount; // directly maps to Cash
        var dto = new FuelPro.Core.DTOs.DsmEntryDto
        {
            NozzleReadings = NozzleReadings.Select(n => new FuelPro.Core.DTOs.NozzleReadingDto { Amount = (decimal)n.Amount }).ToList(),
            PaymentCollection = new FuelPro.Core.DTOs.PaymentCollectionDto
            {
                PhonePe = (decimal)UpiAmount,
                CreditCard = (decimal)CardAmount,
                CashDeposit = (decimal)CashAmount,
                PhysicalCash = 0
            },
            DebitEntries = new List<FuelPro.Core.DTOs.DebitEntryDto> { new() { Amount = (decimal)CreditAmount } },
            Expenses = new List<FuelPro.Core.DTOs.ExpenseDto> { new() { Amount = (decimal)ExpenseAmount } },
            TestingEntries = new List<FuelPro.Core.DTOs.TestingEntryDto>()
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

            // Initialize payment fields from metadata
            double cashDeposit = CashAmount;
            double upiMorning = 0;
            double upiNight = 0;
            double upiCardMorning = 0;
            double upiCardNight = 0;
            double creditCardMorning = 0;
            double creditCardNight = 0;
            double petroCard = 0;
            double others = 0;

            string? cardTid = null;
            string? cardBatch = null;
            string? phonePeTid = null;
            string? phonePeBatch = null;
            string? petroCardTid = null;
            string? petroCardBatch = null;

            bool isNight = string.Equals(SelectedSubmission.ShiftType, "B", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(SelectedSubmission.MetadataJson))
            {
                try
                {
                    var metadata = JsonConvert.DeserializeObject<dynamic>(SelectedSubmission.MetadataJson);
                    if (metadata != null && metadata.cardSwipeDetails != null)
                    {
                        foreach (var swipe in metadata.cardSwipeDetails)
                        {
                            string mode = swipe.mode ?? "";
                            double amount = (double)(swipe.amount ?? 0.0);
                            string tid = swipe.tid ?? "";
                            string batch = swipe.batch ?? "";

                            if (mode.Contains("PhonePe Card", StringComparison.OrdinalIgnoreCase))
                            {
                                if (isNight) upiCardNight += amount;
                                else upiCardMorning += amount;
                                phonePeTid = tid;
                                phonePeBatch = batch;
                            }
                            else if (mode.Contains("PhonePe", StringComparison.OrdinalIgnoreCase))
                            {
                                if (isNight) upiNight += amount;
                                else upiMorning += amount;
                                phonePeTid = tid;
                                phonePeBatch = batch;
                            }
                            else if (mode.Contains("Petro", StringComparison.OrdinalIgnoreCase))
                            {
                                petroCard += amount;
                                petroCardTid = tid;
                                petroCardBatch = batch;
                            }
                            else
                            {
                                // Credit Card / Pinelabs Card / etc.
                                if (isNight) creditCardNight += amount;
                                else creditCardMorning += amount;
                                cardTid = tid;
                                cardBatch = batch;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to parse cardSwipeDetails from metadata JSON");
                }
            }

            // Fallbacks for backward compatibility / plain amount inputs
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

            var payment = new PaymentCollection
            {
                CashDeposit = cashDeposit,
                PhonePeMorning = upiMorning,
                PhonePeNight = upiNight,
                PhonePeCardMorning = upiCardMorning,
                PhonePeCardNight = upiCardNight,
                CreditCardMorning = creditCardMorning,
                CreditCardNight = creditCardNight,
                PetroCard = petroCard,
                Others = others,
                CardTid = cardTid,
                CardBatch = cardBatch,
                PhonePeTid = phonePeTid,
                PhonePeBatch = phonePeBatch,
                PetroCardTid = petroCardTid,
                PetroCardBatch = petroCardBatch
            };

            // Parse Debtor Entries
            var debitModels = new List<DebitEntry>();
            if (!string.IsNullOrEmpty(SelectedSubmission.MetadataJson))
            {
                try
                {
                    var metadata = JsonConvert.DeserializeObject<dynamic>(SelectedSubmission.MetadataJson);
                    if (metadata != null && metadata.debtorEntries != null)
                    {
                        foreach (var dbEntry in metadata.debtorEntries)
                        {
                            string debtorName = dbEntry.debtorName ?? "";
                            double amount = (double)(dbEntry.amount ?? 0.0);
                            string vehNo = dbEntry.vehicleNumber ?? "";
                            string time = dbEntry.time ?? "";

                            debitModels.Add(new DebitEntry
                            {
                                DebtorName = debtorName,
                                Amount = amount,
                                VehicleNumber = string.IsNullOrEmpty(vehNo) ? null : vehNo,
                                EntryTime = string.IsNullOrEmpty(time) ? null : time,
                                PaymentMethod = "Credit"
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to parse debtorEntries from metadata JSON");
                }
            }

            // Fallback for CreditAmount if no debtor entries were parsed
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

            var expenseModels = ExpenseAmount > 0
                ? new List<Expense> { new() { Description = string.IsNullOrEmpty(ExpenseNotes) ? "DSM PWA Expense" : ExpenseNotes, Amount = ExpenseAmount } }
                : new List<Expense>();

            var testingModels = new List<TestingEntry>();

            var cashModels = new List<CashDenomination>
            {
                new() { CashType = "Cash1", TotalAmount = CashAmount },
                new() { CashType = "Cash2", TotalAmount = 0 }
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
                personalDebtors: personalDebtors
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

