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

    public int? ConnectedPumpId
    {
        get
        {
            if (string.IsNullOrEmpty(MetadataJson)) return null;
            try
            {
                var metaObj = JsonConvert.DeserializeObject<dynamic>(MetadataJson);
                if (metaObj != null && metaObj.connectedPumpId != null)
                {
                    return (int?)metaObj.connectedPumpId;
                }
            }
            catch {}
            return null;
        }
    }

    public string TitleDisplay => ConnectedPumpId.HasValue 
        ? $"{DsmName} - Pump {PumpId} + Pump {ConnectedPumpId.Value} (Connected) - Shift {ShiftType}"
        : $"{DsmName} - Pump {PumpId} - Shift {ShiftType}";
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
    public double? RupeeAmount { get; set; }
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
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveSelection))]
    [NotifyPropertyChangedFor(nameof(HeaderTitleDisplay))]
    [NotifyPropertyChangedFor(nameof(HeaderShiftDateDisplay))]
    [NotifyPropertyChangedFor(nameof(HeaderSubmittedTimeDisplay))]
    private DsmPendingSubmission? _selectedSubmission;
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
    public ObservableCollection<DsmPersonalDebtor> PersonalDebtors { get; } = new();
    public ObservableCollection<KhandharePetroleumEntry> KhandhareEntries { get; } = new();
    public ObservableCollection<DsmOilDefSaleRow> OilDefSales { get; } = new();
    public ObservableCollection<Expense> SubmissionExpenses { get; } = new();

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
    [ObservableProperty] private bool _hasContinuityWarnings;
    [ObservableProperty] private bool _overrideContinuity;
    [ObservableProperty] private string _overrideRemark = "";
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
    private DsmApprovedSubmissionDto? _selectedApprovedSubmission;

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
        _ = LoadApprovedHistoryAsync();
        _ = LoadProductsAsync();
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
                using var scope = _serviceProvider.CreateScope();
                using var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
                
                var approvedSubIds = await context.DsmApprovalAudits
                    .Select(a => a.SubmissionId)
                    .ToListAsync();
                
                var approvedSubIdSet = new HashSet<Guid>(approvedSubIds);

                foreach (var item in result.Data)
                {
                    Guid subId = item.Id;

                    // If already approved locally, skip showing it and auto-heal status on Supabase
                    if (approvedSubIdSet.Contains(subId))
                    {
                        _logger.Information("Submission {SubId} was already approved locally. Auto-healing Supabase status asynchronously.", subId);
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var settingsSvc = _serviceProvider.GetRequiredService<SyncConfigService>();
                                var settings = await settingsSvc.GetSettingsAsync();
                                await _supabaseService.ApproveSubmissionAsync(subId, "System (Sync Auto-Heal)", Guid.NewGuid().ToString("N"));
                            }
                            catch (Exception ex)
                            {
                                _logger.Warning(ex, "Failed to auto-heal approved submission {SubId} in Supabase", subId);
                            }
                        });
                        continue;
                    }

                    // Keep all pending submissions regardless of age

                    PendingSubmissions.Add(new DsmPendingSubmission
                    {
                        Id = subId,
                        DsmName = item.DsmUsers?.FullName ?? "Unknown DSM",
                        DsmUserId = item.DsmUserId,
                        PumpId = (int)item.PumpId,
                        ShiftDate = item.ShiftDate,
                        ShiftType = item.ShiftType,
                        SubmittedAt = item.SubmittedAt,
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
                IsEditing = false;
                IsReadOnlyMode = false;
                TimelineLogs.Clear();
            }
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
            OilDefSales.Clear();

            // 2. Query continuity closings from local SQLite
            var prevResult = await _nozzleRepo.GetPreviousShiftClosingsAsync(submission.ShiftDate, submission.ShiftType, submission.PumpId);
            var prevClosings = prevResult.Success ? prevResult.Data! : new Dictionary<int, double>();

            if (submission.ConnectedPumpId.HasValue)
            {
                var connPrevResult = await _nozzleRepo.GetPreviousShiftClosingsAsync(submission.ShiftDate, submission.ShiftType, submission.ConnectedPumpId.Value);
                if (connPrevResult.Success && connPrevResult.Data != null)
                {
                    foreach (var kvp in connPrevResult.Data)
                    {
                        prevClosings[kvp.Key] = kvp.Value;
                    }
                }
            }

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

                        PersonalDebtors.Clear();
                        if (metadata.personalDebtors != null)
                        {
                            foreach (var pd in metadata.personalDebtors)
                            {
                                PersonalDebtors.Add(new DsmPersonalDebtor
                                {
                                    DsmName = submission.DsmName,
                                    Date = submission.ShiftDate,
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
                                    Coins = Convert.ToInt32((object?)(pd.coins ?? 0))
                                });
                            }
                        }

                        KhandhareEntries.Clear();
                        var kpRawList = metadata.khandhareEntries ?? metadata.khandharePetroleumEntries;
                        if (kpRawList != null)
                        {
                            foreach (var kp in kpRawList)
                            {
                                string name = (kp.name ?? kp.Name ?? string.Empty).ToString();
                                string slipNumber = (kp.slipNumber ?? kp.SlipNumber ?? string.Empty).ToString();
                                double amount = Convert.ToDouble((object?)(kp.amount ?? kp.Amount ?? 0.0));

                                KhandhareEntries.Add(new KhandharePetroleumEntry
                                {
                                    DsmName = submission.DsmName,
                                    Date = submission.ShiftDate,
                                    Name = name,
                                    SlipNumber = slipNumber,
                                    Amount = amount
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

        var expenseDtos = (SubmissionExpenses.Count > 0
            ? SubmissionExpenses.Select(e => new FuelPro.Core.DTOs.ExpenseDto { Amount = (decimal)e.Amount })
            : new List<FuelPro.Core.DTOs.ExpenseDto> { new() { Amount = (decimal)ExpenseAmount } })
            .Concat(KhandhareEntries.Select(k => new FuelPro.Core.DTOs.ExpenseDto { Amount = (decimal)k.Amount }))
            .ToList();

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

        var calc = _dsmCalculationService.Calculate(dto);
        double oilDefSalesTotal = OilDefSales.Sum(s => s.Total);
        TotalCollections = (double)calc.TotalCollection + oilDefSalesTotal;
        MismatchAmount = TotalCollections - GrossSales;

        double rawShortage = MismatchAmount < 0 ? Math.Abs(MismatchAmount) : 0;
        if (rawShortage > 10)
        {
            ShortAmount = 0; // Transferred to DSM Loss Ledger (> 10)
        }
        else
        {
            ShortAmount = rawShortage; // Shift Short (<= 10)
        }
        ExcessAmount = MismatchAmount > 0 ? MismatchAmount : 0;

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

            bool isNight = string.Equals(SelectedSubmission.ShiftType, "A", StringComparison.OrdinalIgnoreCase);
            bool isDay = string.Equals(SelectedSubmission.ShiftType, "B", StringComparison.OrdinalIgnoreCase);

            // Try structured settlements array first (new PWA format)
            bool hasStructuredSettlements = false;
            if (!string.IsNullOrEmpty(SelectedSubmission.MetadataJson))
            {
                try
                {
                    var metadata = JsonConvert.DeserializeObject<dynamic>(SelectedSubmission.MetadataJson);
                    if (metadata?.settlements != null)
                    {
                        hasStructuredSettlements = true;
                        foreach (var s in metadata.settlements)
                        {
                            string paymentType = (string)(s.paymentType ?? "");
                            string period = (string)(s.period ?? "");
                            double amt = (double)(s.amount ?? 0.0);
                            string tid = (string)(s.tid ?? "");
                            string batch = (string)(s.batch ?? "");

                            if (amt <= 0) continue;

                            if (paymentType.Contains("PhonePe", StringComparison.OrdinalIgnoreCase))
                            {
                                switch (period)
                                {
                                    case "Morning":
                                        upiMorning += amt; phonePeTidMorning = tid; phonePeBatchMorning = batch;
                                        break;
                                    case "Day":
                                        upiDay += amt; phonePeTidDay = tid; phonePeBatchDay = batch;
                                        break;
                                    case "Night":
                                        upiNight += amt; phonePeTidNight = tid; phonePeBatchNight = batch;
                                        break;
                                }
                                phonePeTid = tid;
                                phonePeBatch = batch;
                            }
                            else if (paymentType.Contains("Petro", StringComparison.OrdinalIgnoreCase))
                            {
                                switch (period)
                                {
                                    case "Morning":
                                        petroCardMorning += amt; petroCardTidMorning = tid; petroCardBatchMorning = batch;
                                        break;
                                    case "Day":
                                        petroCardDay += amt; petroCardTidDay = tid; petroCardBatchDay = batch;
                                        break;
                                    case "Night":
                                        petroCardNight += amt; petroCardTidNight = tid; petroCardBatchNight = batch;
                                        break;
                                }
                                petroCardTid = tid;
                                petroCardBatch = batch;
                            }
                            else // PineLabs / Credit Card
                            {
                                switch (period)
                                {
                                    case "Morning":
                                        creditCardMorning += amt; creditCardTidMorning = tid; creditCardBatchMorning = batch;
                                        break;
                                    case "Day":
                                        creditCardDay += amt; creditCardTidDay = tid; creditCardBatchDay = batch;
                                        break;
                                    case "Night":
                                        creditCardNight += amt; creditCardTidNight = tid; creditCardBatchNight = batch;
                                        break;
                                }
                                cardTid = tid;
                                cardBatch = batch;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to parse structured settlements from metadata");
                }
            }

            // Fall back to legacy cardSwipeDetails parsing if no structured settlements
            if (!hasStructuredSettlements)

            foreach (var swipe in CardSwipeDetails)
            {
                string mode = swipe.Mode ?? "";
                double amount = swipe.Amount;
                string tid = swipe.Tid ?? "";
                string batch = swipe.Batch ?? "";

                bool isSwipeNight = mode.Contains("Night", StringComparison.OrdinalIgnoreCase) || 
                                    (!mode.Contains("Morning", StringComparison.OrdinalIgnoreCase) && 
                                     !mode.Contains("Day", StringComparison.OrdinalIgnoreCase) && 
                                     isNight);

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
                    else if (isDay || mode.Contains("Day", StringComparison.OrdinalIgnoreCase))
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

            // Fallbacks for backward compatibility / plain amount inputs (only when NO card swipe details AND no structured settlements exist)
            if (CardSwipeDetails.Count == 0 && !hasStructuredSettlements)
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
                if (petroCardMorning == 0 && petroCardDay == 0 && petroCardNight == 0 && PetroCardAmount > 0)
                {
                    if (isNight) petroCardNight = PetroCardAmount;
                    else if (isDay) petroCardDay = PetroCardAmount;
                    else petroCardMorning = PetroCardAmount;
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
                CardTid = cardTid ?? (isNight ? creditCardTidNight : creditCardTidMorning),
                CardBatch = cardBatch ?? (isNight ? creditCardBatchNight : creditCardBatchMorning),
                PhonePeTid = phonePeTid ?? (isNight ? phonePeTidNight : phonePeTidMorning),
                PhonePeBatch = phonePeBatch ?? (isNight ? phonePeBatchNight : phonePeBatchMorning),
                PetroCardTid = petroCardTid ?? (isNight ? petroCardTidNight : petroCardTidMorning),
                PetroCardBatch = petroCardBatch ?? (isNight ? petroCardBatchNight : petroCardBatchMorning),
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

            // Automatic DSM Loss calculation based on mismatch threshold (> 10)
            double shiftShortage = MismatchAmount < 0 ? Math.Abs(MismatchAmount) : 0;
            var personalDebtors = new List<DsmPersonalDebtor>();
            if (shiftShortage > 10)
            {
                personalDebtors.Add(new DsmPersonalDebtor
                {
                    DsmName = SelectedSubmission.DsmName,
                    Date = SelectedSubmission.ShiftDate,
                    Time = DateTime.Now.ToString("hh:mm tt"),
                    Amount = shiftShortage,
                    Remarks = $"Auto Shift Shortage (Pump {SelectedSubmission.PumpId}, Shift {SelectedSubmission.ShiftType})",
                    PaymentMethod = "Cash"
                });
            }

            // Parse cash denominations, connected pump, and testing entries
            int denom500 = 0, denom200 = 0, denom100 = 0, denom50 = 0, denom20 = 0, denom10 = 0, coins = 0;
            int cash1denom500 = 0, cash1denom200 = 0, cash1denom100 = 0, cash1denom50 = 0, cash1denom20 = 0, cash1denom10 = 0, cash1coins = 0;
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
                        if (metadata.connectedPumpId != null)
                        {
                            connectedPumpId = (int?)metadata.connectedPumpId;
                        }

                        if (connectedPumpId == null && nozzleModels != null)
                        {
                            foreach (var n in nozzleModels)
                            {
                                var nozzlePumpId = PumpConfiguration.GetPumpIdForNozzle(n.NozzleNumber, SelectedSubmission.ShiftDate);
                                if (nozzlePumpId != 0 && nozzlePumpId != SelectedSubmission.PumpId)
                                {
                                    connectedPumpId = nozzlePumpId;
                                    break;
                                }
                            }
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

            // Cash1 is Cash 1, Cash2 is Cash in Hand
            var cashModels = new List<CashDenomination>
            {
                new()
                {
                    CashType = "Cash1",
                    TotalAmount = CashDepositAmount,
                    Denom500 = cash1denom500,
                    Denom200 = cash1denom200,
                    Denom100 = cash1denom100,
                    Denom50 = 0,
                    Denom20 = 0,
                    Denom10 = 0,
                    Coins = 0
                },
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

            // 3. Save locally via single database transaction (authoritative source of truth)
            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
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
                    khandharePetroleumEntries: KhandhareEntries.ToList()
                );

                if (!localSaveResult.Success)
                {
                    await transaction.RollbackAsync();
                    MessageBox.Show($"WPF local save failed: {localSaveResult.Error}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

                            double defaultSaleRate = productMaster.DefaultSaleRate;
                            double price = sale.Price;
                            double quantity = sale.Quantity;

                            var log = await context.OilDefDailyLogs
                                .FirstOrDefaultAsync(l => l.ProductId == productId && l.LogDate == SelectedSubmission.ShiftDate.Date);

                            if (log != null)
                            {
                                log.SoldQuantity += quantity;
                                if (Math.Abs(price - defaultSaleRate) > 0.01)
                                {
                                    log.OverrideSaleRate = price;
                                }
                                context.Entry(log).State = EntityState.Modified;
                            }
                            else
                            {
                                log = new OilDefDailyLog
                                {
                                    LogDate = SelectedSubmission.ShiftDate.Date,
                                    ProductId = productId,
                                    ProductType = productMaster.Category,
                                    SoldQuantity = quantity,
                                    OverrideSaleRate = Math.Abs(price - defaultSaleRate) > 0.01 ? price : null
                                };
                                context.OilDefDailyLogs.Add(log);
                            }

                            await context.SaveChangesAsync();
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
                
                var originalData = new
                {
                    Readings = NozzleReadings.Select(r => new { r.NozzleId, r.OpeningReading, r.ClosingReading, r.Rate }),
                    Collections = new { Cash = CashAmount, UPI = UpiAmount, Card = CardAmount, Credit = CreditAmount, Expense = ExpenseAmount, ExpenseNotes },
                    khandhareEntries = KhandhareEntries.Select(k => new { k.Name, k.SlipNumber, k.Amount })
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

                // 4. Complete the active DSM assignment automatically after successful approval
                try
                {
                    var dsmUser = await context.DsmUsers.FirstOrDefaultAsync(u =>
                        u.AuthUserId == SelectedSubmission.DsmUserId ||
                        u.FullName == SelectedSubmission.DsmName);

                    if (dsmUser != null)
                    {
                        var assignment = await context.DsmPumpAssignments
                            .FirstOrDefaultAsync(a => a.IsActive
                                && a.DsmUserId == dsmUser.DsmUserId
                                && a.ShiftType == SelectedSubmission.ShiftType
                                && a.PumpId == SelectedSubmission.PumpId
                                && a.ConnectedPumpId == connectedPumpId);

                        if (assignment == null)
                        {
                            // Fallback to match without connected pump if exact match not found
                            assignment = await context.DsmPumpAssignments
                                .FirstOrDefaultAsync(a => a.IsActive
                                    && a.DsmUserId == dsmUser.DsmUserId
                                    && a.ShiftType == SelectedSubmission.ShiftType
                                    && a.PumpId == SelectedSubmission.PumpId);
                        }

                        if (assignment != null)
                        {
                            assignment.IsActive = false;
                            assignment.CompletedDate = DateTime.Now;
                            context.Entry(assignment).State = EntityState.Modified;
                            _logger.Information("Automatically marked active assignment {AssignmentId} as Completed for DSM {DsmName} on Pump {PumpId}",
                                assignment.DsmPumpAssignmentId, dsmUser.FullName, SelectedSubmission.PumpId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to complete active DSM assignment during approval");
                    throw; // Escalate to rollback transaction
                }

                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                
                _logger.Information("Local transaction successfully committed for DSM entry. SelectedSubmissionId: {SubId}", SelectedSubmission.Id);

                // 5. Send approval notification to DSM asynchronously (non-blocking)
                var submissionId = SelectedSubmission.Id;
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

                // 6. Update status in Supabase asynchronously with retry and exponential backoff
                _ = Task.Run(async () =>
                {
                    int maxRetries = 10;
                    int delayMs = 1000;
                    for (int attempt = 1; attempt <= maxRetries; attempt++)
                    {
                        var lockResult = await _supabaseService.ApproveSubmissionAsync(submissionId, approvedBy, lockId);
                        if (lockResult.Success)
                        {
                            _logger.Information("Successfully approved submission {SubId} in Supabase on attempt {Attempt}", submissionId, attempt);
                            break;
                        }
                        
                        _logger.Warning("Attempt {Attempt} to approve submission {SubId} in Supabase failed: {Error}. Retrying in {Delay}ms...",
                            attempt, submissionId, lockResult.Error, delayMs);
                        
                        await Task.Delay(delayMs);
                        delayMs *= 2;
                    }
                });

                // 7. Push local change to Supabase immediately (for DsmApprovalAudits, etc.)
                var syncEngine = _serviceProvider.GetRequiredService<SyncEngine>();
                _ = syncEngine.ForceSyncAsync();

                MessageBox.Show("DSM Submission approved successfully and saved locally!", "Approved", MessageBoxButton.OK, MessageBoxImage.Information);
                
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
                MessageBox.Show($"Approval failed: A database update constraint or validation error occurred. Check log files for technical details.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.Error(ex, "Approval process failed for submission {SubId}", SelectedSubmission.Id);
                MessageBox.Show($"Approval failed: {ex.Message}", "System Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private async Task RecalculateRunningBalancesAsync(FuelProDbContext context, int productId, DateTime fromDate)
    {
        try
        {
            var prevRemaining = await context.OilDefDailyLogs
                .Where(l => l.ProductId == productId && l.LogDate < fromDate)
                .OrderByDescending(l => l.LogDate)
                .Select(l => l.RemainingStock)
                .FirstOrDefaultAsync();

            var subsequentLogs = await context.OilDefDailyLogs
                .Where(l => l.ProductId == productId && l.LogDate >= fromDate)
                .OrderBy(l => l.LogDate)
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
                        MetadataJson = item.Metadata != null ? JsonConvert.SerializeObject(item.Metadata) : "",
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
                .Include(e => e.DebitEntries)
                .Include(e => e.TestingEntries)
                .Include(e => e.Expenses)
                .Include(e => e.CashDenominations)
                .Include(e => e.PersonalDebtors)
                .Include(e => e.KhandharePetroleumEntries)
                .FirstOrDefaultAsync(e => e.DsmEntryId == approvedSub.DsmEntryId);

            DsmEntry? connectedEntry = null;
            if (entry != null && entry.ConnectedPumpId.HasValue)
            {
                var connCandidates = await context.DsmEntries
                    .Include(e => e.NozzleReadings)
                    .Where(e => e.ShiftId == entry.ShiftId
                        && e.PumpId == entry.ConnectedPumpId.Value
                        && (e.ReconciledToPumpId == entry.DsmEntryId || e.ReconciledToPumpId == entry.PumpId || (e.DsmName != null && entry.DsmName != null && e.DsmName.ToLower() == entry.DsmName.ToLower())))
                    .ToListAsync();
                connectedEntry = connCandidates
                    .OrderBy(e => e.ReconciledToPumpId == entry.DsmEntryId ? 0 : 1)
                    .ThenBy(e => e.DsmEntryId >= entry.DsmEntryId ? (e.DsmEntryId - entry.DsmEntryId) : (100000 + Math.Abs(e.DsmEntryId - entry.DsmEntryId)))
                    .FirstOrDefault();
            }

            NozzleReadings.Clear();
            DebtorEntries.Clear();
            CardSwipeDetails.Clear();
            SubmissionTestingEntries.Clear();
            OilDefSales.Clear();
            TimelineLogs.Clear();

            if (entry != null && entry.NozzleReadings != null)
            {
                var uniqueReadings = entry.NozzleReadings
                    .GroupBy(r => r.NozzleNumber)
                    .Select(g => g.OrderByDescending(r => r.NozzleReadingId).First())
                    .ToList();

                foreach (var r in uniqueReadings)
                {
                    var row = new DsmNozzleRow
                    {
                        NozzleId = r.NozzleNumber,
                        FuelType = GetFuelTypeName(entry.PumpId, r.NozzleNumber, approvedSub.ShiftDate),
                        OpeningReading = r.OpeningReading,
                        ClosingReading = r.ClosingReading,
                        Rate = r.Rate,
                        SaleLitres = r.SaleLitres,
                        Amount = r.Amount
                    };
                    NozzleReadings.Add(row);
                }
            }

            if (connectedEntry != null && connectedEntry.NozzleReadings != null)
            {
                var uniqueConnectedReadings = connectedEntry.NozzleReadings
                    .GroupBy(r => r.NozzleNumber)
                    .Select(g => g.OrderByDescending(r => r.NozzleReadingId).First())
                    .ToList();

                foreach (var r in uniqueConnectedReadings)
                {
                    var row = new DsmNozzleRow
                    {
                        NozzleId = r.NozzleNumber,
                        FuelType = GetFuelTypeName(connectedEntry.PumpId, r.NozzleNumber, approvedSub.ShiftDate),
                        OpeningReading = r.OpeningReading,
                        ClosingReading = r.ClosingReading,
                        Rate = r.Rate,
                        SaleLitres = r.SaleLitres,
                        Amount = r.Amount
                    };
                    NozzleReadings.Add(row);
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

                var pc = entry.PaymentCollection;
                if (pc != null)
                {
                    var cash1Total = entry.CashDenominations.Where(c => c.CashType == "Cash1").Sum(c => c.TotalAmount);
                    var cash2Total = entry.CashDenominations.Where(c => c.CashType == "Cash2").Sum(c => c.TotalAmount);

                    CashAmount = cash2Total;
                    UpiAmount = pc.PhonePeMorning + pc.PhonePeDay + pc.PhonePeNight;
                    CardAmount = pc.CreditCardMorning + pc.CreditCardDay + pc.CreditCardNight + pc.PhonePeCardMorning + pc.PhonePeCardDay + pc.PhonePeCardNight;
                    PetroCardAmount = pc.PetroCardMorning + pc.PetroCardDay + pc.PetroCardNight;
                    CashDepositAmount = cash1Total > 0 ? cash1Total : pc.CashDeposit;
                    OthersAmount = pc.Others;
                    CreditAmount = (double)entry.TotalCreditors;
                    ExpenseAmount = entry.Expenses.Sum(e => e.Amount);
                    ExpenseNotes = entry.Expenses.FirstOrDefault()?.Description ?? "";
                    ShortAmount = (double)(entry.Mismatch < 0 ? Math.Abs(entry.Mismatch) : 0);
                    ExcessAmount = (double)(entry.Mismatch > 0 ? entry.Mismatch : 0);
                }

                foreach (var d in entry.DebitEntries)
                {
                    DebtorEntries.Add(new DsmDebitRow
                    {
                        DebtorName = d.DebtorName,
                        Amount = d.Amount,
                        VehicleNumber = d.VehicleNumber,
                        SlipNumber = d.SlipNumber,
                        EntryTime = d.CreatedAt.ToString("hh:mm tt")
                    });
                }

                if (pc != null)
                {
                    if (pc.CreditCardMorning > 0)
                        CardSwipeDetails.Add(new DsmCardSwipeRow { Mode = "Credit Card (Morning)", Amount = pc.CreditCardMorning, Tid = pc.CreditCardTidMorning ?? "", Batch = pc.CreditCardBatchMorning ?? "" });
                    if (pc.CreditCardDay > 0)
                        CardSwipeDetails.Add(new DsmCardSwipeRow { Mode = "Credit Card (Day)", Amount = pc.CreditCardDay, Tid = pc.CreditCardTidDay ?? "", Batch = pc.CreditCardBatchDay ?? "" });
                    if (pc.CreditCardNight > 0)
                        CardSwipeDetails.Add(new DsmCardSwipeRow { Mode = "Credit Card (Night)", Amount = pc.CreditCardNight, Tid = pc.CreditCardTidNight ?? "", Batch = pc.CreditCardBatchNight ?? "" });
                    if (pc.PhonePeCardMorning > 0)
                        CardSwipeDetails.Add(new DsmCardSwipeRow { Mode = "PhonePe Card (Morning)", Amount = pc.PhonePeCardMorning, Tid = pc.PhonePeTidMorning ?? "", Batch = pc.PhonePeBatchMorning ?? "" });
                    if (pc.PhonePeCardDay > 0)
                        CardSwipeDetails.Add(new DsmCardSwipeRow { Mode = "PhonePe Card (Day)", Amount = pc.PhonePeCardDay, Tid = pc.PhonePeTidDay ?? "", Batch = pc.PhonePeBatchDay ?? "" });
                    if (pc.PhonePeCardNight > 0)
                        CardSwipeDetails.Add(new DsmCardSwipeRow { Mode = "PhonePe Card (Night)", Amount = pc.PhonePeCardNight, Tid = pc.PhonePeTidNight ?? "", Batch = pc.PhonePeBatchNight ?? "" });
                    if (pc.PetroCardMorning > 0)
                        CardSwipeDetails.Add(new DsmCardSwipeRow { Mode = "Petro Card (Morning)", Amount = pc.PetroCardMorning, Tid = pc.PetroCardTidMorning ?? "", Batch = pc.PetroCardBatchMorning ?? "" });
                    if (pc.PetroCardDay > 0)
                        CardSwipeDetails.Add(new DsmCardSwipeRow { Mode = "Petro Card (Day)", Amount = pc.PetroCardDay, Tid = pc.PetroCardTidDay ?? "", Batch = pc.PetroCardBatchDay ?? "" });
                    if (pc.PetroCardNight > 0)
                        CardSwipeDetails.Add(new DsmCardSwipeRow { Mode = "Petro Card (Night)", Amount = pc.PetroCardNight, Tid = pc.PetroCardTidNight ?? "", Batch = pc.PetroCardBatchNight ?? "" });
                }

                foreach (var d in entry.PersonalDebtors)
                {
                    PersonalDebtors.Add(d);
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
        try
        {
            bool anyRepaired = false;
            foreach (var audit in localAudits)
            {
                if (string.IsNullOrWhiteSpace(audit.OriginalDataJson) || string.IsNullOrWhiteSpace(audit.ApprovedDataJson))
                    continue;

                int primaryEntryId = 0;
                try
                {
                    var appObj = JsonConvert.DeserializeObject<dynamic>(audit.ApprovedDataJson);
                    primaryEntryId = (int)(appObj?.DsmEntryId ?? 0);
                }
                catch {}

                if (primaryEntryId <= 0) continue;

                var primaryEntry = await context.DsmEntries
                    .Include(e => e.NozzleReadings)
                    .FirstOrDefaultAsync(e => e.DsmEntryId == primaryEntryId);

                if (primaryEntry == null) continue;

                // Load slave entry linked to this primary entry
                DsmEntry? slaveEntry = null;
                if (primaryEntry.ConnectedPumpId.HasValue)
                {
                    slaveEntry = await context.DsmEntries
                        .Include(e => e.NozzleReadings)
                        .FirstOrDefaultAsync(e => e.ShiftId == primaryEntry.ShiftId
                            && e.PumpId == primaryEntry.ConnectedPumpId.Value
                            && (e.ReconciledToPumpId == primaryEntry.DsmEntryId || (e.ReconciledToPumpId == primaryEntry.PumpId && e.DsmName == primaryEntry.DsmName)));
                }

                // Parse original readings
                var origObj = JsonConvert.DeserializeObject<dynamic>(audit.OriginalDataJson);
                var readingsToken = origObj?.Readings;
                if (readingsToken == null) continue;

                var expectedReadings = new List<(int NozzleId, double Opening, double Closing, double Rate)>();
                foreach (var r in readingsToken)
                {
                    int nId = (int)(r.NozzleId ?? r.nozzleId ?? 0);
                    double op = (double)(r.OpeningReading ?? r.openingReading ?? 0);
                    double cl = (double)(r.ClosingReading ?? r.closingReading ?? 0);
                    double rt = (double)(r.Rate ?? r.rate ?? 0);
                    if (nId > 0) expectedReadings.Add((nId, op, cl, rt));
                }

                if (expectedReadings.Count == 0) continue;

                bool entryHealed = false;

                // Check and fix slave entry readings
                if (slaveEntry != null && slaveEntry.NozzleReadings != null)
                {
                    foreach (var sReading in slaveEntry.NozzleReadings)
                    {
                        var exp = expectedReadings.FirstOrDefault(x => x.NozzleId == sReading.NozzleNumber);
                        if (exp.NozzleId > 0)
                        {
                            if (Math.Abs(sReading.OpeningReading - exp.Opening) > 0.001 || Math.Abs(sReading.ClosingReading - exp.Closing) > 0.001)
                            {
                                sReading.OpeningReading = exp.Opening;
                                sReading.ClosingReading = exp.Closing;
                                sReading.SaleLitres = sReading.ClosingReading - sReading.OpeningReading;
                                sReading.Amount = sReading.SaleLitres * sReading.Rate;
                                entryHealed = true;
                            }
                        }
                    }

                    if (entryHealed)
                    {
                        slaveEntry.GrossSales = (decimal)slaveEntry.NozzleReadings.Sum(r => r.Amount);
                        slaveEntry.Mismatch = 0m;
                        context.Entry(slaveEntry).State = EntityState.Modified;
                    }
                }

                // Check and fix primary entry readings
                if (primaryEntry.NozzleReadings != null)
                {
                    foreach (var pReading in primaryEntry.NozzleReadings)
                    {
                        var exp = expectedReadings.FirstOrDefault(x => x.NozzleId == pReading.NozzleNumber);
                        if (exp.NozzleId > 0)
                        {
                            if (Math.Abs(pReading.OpeningReading - exp.Opening) > 0.001 || Math.Abs(pReading.ClosingReading - exp.Closing) > 0.001)
                            {
                                pReading.OpeningReading = exp.Opening;
                                pReading.ClosingReading = exp.Closing;
                                pReading.SaleLitres = pReading.ClosingReading - pReading.OpeningReading;
                                pReading.Amount = pReading.SaleLitres * pReading.Rate;
                                entryHealed = true;
                            }
                        }
                    }
                }

                if (entryHealed)
                {
                    double primaryGross = primaryEntry.NozzleReadings?.Sum(r => r.Amount) ?? (double)primaryEntry.GrossSales;
                    double slaveGross = slaveEntry?.NozzleReadings?.Sum(r => r.Amount) ?? (double)(slaveEntry?.GrossSales ?? 0m);
                    primaryEntry.GrossSales = (decimal)(primaryGross + slaveGross);
                    primaryEntry.Mismatch = primaryEntry.TotalCollection - primaryEntry.GrossSales;
                    context.Entry(primaryEntry).State = EntityState.Modified;
                    anyRepaired = true;
                }
            }

            if (anyRepaired)
            {
                await context.SaveChangesAsync();
                _logger.Information("Successfully auto-healed corrupted nozzle readings for approved submissions.");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to auto-heal corrupted approved entries.");
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

    public int? ConnectedPumpId
    {
        get
        {
            if (string.IsNullOrEmpty(MetadataJson)) return null;
            try
            {
                var metaObj = JsonConvert.DeserializeObject<dynamic>(MetadataJson);
                if (metaObj != null && metaObj.connectedPumpId != null)
                {
                    return (int?)metaObj.connectedPumpId;
                }
            }
            catch {}
            return null;
        }
    }

    public string TitleDisplay => ConnectedPumpId.HasValue 
        ? $"{DsmName} - Pump {PumpId} + Pump {ConnectedPumpId.Value} (Connected) - Shift {ShiftType}"
        : $"{DsmName} - Pump {PumpId} - Shift {ShiftType}";
    public string ShiftDateDisplay => ShiftDate.ToString("dd MMM yyyy");
    public string SubmittedTimeDisplay => SubmittedAt.ToLocalTime().ToString("hh:mm tt");
    public string ApprovedTimeDisplay => ApprovedAt.ToLocalTime().ToString("hh:mm tt");
}


public class DsmTimelineLog
{
    public string Title { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
}

