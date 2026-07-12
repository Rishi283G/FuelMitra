using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.DTOs;
using FuelPro.UI.Printing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Serilog;
using FuelPro.Core.Services;
using FuelPro.Data;
using Microsoft.EntityFrameworkCore;

namespace FuelPro.UI.ViewModels;

public class DebtorDisplayRow
{
    public int CreditorId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public double TotalDebt { get; set; }
    public double TotalRepayment { get; set; }
    public double OutstandingBalance => TotalDebt - TotalRepayment;
}

public class LedgerTransactionRow
{
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public double Debit { get; set; }
    public double Credit { get; set; }
    public double RunningBalance { get; set; }

    public string TransactionType { get; set; } = string.Empty; // "Debt" or "Repayment"
    public int TransactionId { get; set; }
    public string? SlipNumber { get; set; }
    public string? Remarks { get; set; }
    public string? VehicleNumber { get; set; }
    public double Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool CanEdit { get; set; }
}

public partial class DebtorManagementViewModel : ObservableObject
{
    private readonly ICreditorRepository _creditorRepo;
    private readonly ICreditorRepaymentRepository _repaymentRepo;
    private readonly IDsmEntryRepository _dsmRepo;
    private readonly PrintService _printService;
    private readonly ExcelExportService _excelExportService;
    private readonly FuelProDbContext _dbContext;
    private readonly ILogger _logger = Log.ForContext<DebtorManagementViewModel>();

    [ObservableProperty] private bool _isLoading;

    // Debtors directory
    [ObservableProperty] private ObservableCollection<DebtorDisplayRow> _debtors = new();
    [ObservableProperty] private ObservableCollection<Creditor> _debtorsList = new();

    // Debtor edit / create form
    [ObservableProperty] private string _newDebtorName = "";
    [ObservableProperty] private string _newDebtorPhone = "";
    [ObservableProperty] private bool _isEditingDebtor;
    [ObservableProperty] private int? _editingDebtorId;
    [ObservableProperty] private string _debtorStatusMessage = "";
    [ObservableProperty] private string _debtorFormTitle = "Add New Debtor";
    [ObservableProperty] private string _debtorButtonContent = "Save Debtor";

    // Repayment logging form
    [ObservableProperty] private string _selectedDebtorName = "";
    [ObservableProperty] private double _repaymentAmount;
    [ObservableProperty] private string? _chequeNumber = "";
    [ObservableProperty] private DateTime _repaymentDate = DateTime.Today;
    [ObservableProperty] private string _selectedPaymentMode = "Cash";
    [ObservableProperty] private string _repaymentStatus = "";
    [ObservableProperty] private string? _cardTid = "";
    [ObservableProperty] private string? _cardBatch = "";
    [ObservableProperty] private int? _denom500;
    [ObservableProperty] private int? _denom200;
    [ObservableProperty] private int? _denom100;
    [ObservableProperty] private int? _denom50;
    [ObservableProperty] private int? _denom20;
    [ObservableProperty] private int? _denom10;
    [ObservableProperty] private int? _coins;
    public string[] PaymentModes { get; } = { "Cash", "PhonePe", "PineLabs Card", "Cheque", "Bank Transfer" };

    // True when Cash is selected — used for XAML visibility of denomination grid vs manual amount
    public bool IsCashPaymentMode => SelectedPaymentMode == "Cash";

    partial void OnSelectedPaymentModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsCashPaymentMode));
        if (value == "Cash")
        {
            // Recalculate from existing denominations
            RecalculateCashAmount();
        }
        else
        {
            // Clear denomination fields when switching away from Cash
            Denom500 = null; Denom200 = null; Denom100 = null;
            Denom50 = null; Denom20 = null; Denom10 = null; Coins = null;
            RepaymentAmount = 0;
        }
    }

    partial void OnDenom500Changed(int? value) { if (SelectedPaymentMode == "Cash") RecalculateCashAmount(); }
    partial void OnDenom200Changed(int? value) { if (SelectedPaymentMode == "Cash") RecalculateCashAmount(); }
    partial void OnDenom100Changed(int? value) { if (SelectedPaymentMode == "Cash") RecalculateCashAmount(); }
    partial void OnDenom50Changed(int? value)  { if (SelectedPaymentMode == "Cash") RecalculateCashAmount(); }
    partial void OnDenom20Changed(int? value)  { if (SelectedPaymentMode == "Cash") RecalculateCashAmount(); }
    partial void OnDenom10Changed(int? value)  { if (SelectedPaymentMode == "Cash") RecalculateCashAmount(); }
    partial void OnCoinsChanged(int? value)    { if (SelectedPaymentMode == "Cash") RecalculateCashAmount(); }

    private void RecalculateCashAmount()
    {
        RepaymentAmount = (Denom500 ?? 0) * 500
                        + (Denom200 ?? 0) * 200
                        + (Denom100 ?? 0) * 100
                        + (Denom50  ?? 0) * 50
                        + (Denom20  ?? 0) * 20
                        + (Denom10  ?? 0) * 10
                        + (Coins    ?? 0) * 1;
    }

    // Ledger statements
    [ObservableProperty] private string _ledgerDebtorName = "";
    [ObservableProperty] private DateTime _ledgerStartDate = DateTime.Today.AddDays(-30);
    [ObservableProperty] private DateTime _ledgerEndDate = DateTime.Today;
    [ObservableProperty] private ObservableCollection<LedgerTransactionRow> _ledgerTransactions = new();
    [ObservableProperty] private double _ledgerOutstandingBalance;
    [ObservableProperty] private double _ledgerOpeningBalance;
    [ObservableProperty] private double _ledgerTotalDebt;
    [ObservableProperty] private double _ledgerTotalRepayments;
    [ObservableProperty] private string _ledgerStatus = "";

    // Transaction editing properties
    [ObservableProperty] private LedgerTransactionRow? _selectedTransaction;
    [ObservableProperty] private bool _isEditingTransaction;
    [ObservableProperty] private double _editTxAmount;
    [ObservableProperty] private string? _editTxSlipNumber;
    [ObservableProperty] private string? _editTxVehicleNumber;
    [ObservableProperty] private string? _editTxRemarks;
    [ObservableProperty] private string _editReason = "";
    [ObservableProperty] private string _editTxStatusMessage = "";

    public bool IsOwner => App.Services.GetRequiredService<AuthService>().CurrentUser?.IsOwner ?? false;

    // Vehicle management properties
    [ObservableProperty] private DebtorDisplayRow? _selectedDebtor;
    [ObservableProperty] private ObservableCollection<DebtorVehicle> _selectedDebtorVehicles = new();
    [ObservableProperty] private string _newVehicleNumber = "";
    [ObservableProperty] private string _vehicleStatusMessage = "";
    [ObservableProperty] private bool _isDebtorSelected;

    // Debtor totals editing fields
    [ObservableProperty] private double _editTotalDebt;
    [ObservableProperty] private double _editRepaidAmount;
    [ObservableProperty] private string _editEntryDetails = "Profile Edit Adjustment";

    public double EditOutstandingBalance => EditTotalDebt - EditRepaidAmount;

    partial void OnEditTotalDebtChanged(double value) => OnPropertyChanged(nameof(EditOutstandingBalance));
    partial void OnEditRepaidAmountChanged(double value) => OnPropertyChanged(nameof(EditOutstandingBalance));

    // Vehicle edit form state
    [ObservableProperty] private int? _editingVehicleId;
    [ObservableProperty] private string _vehicleButtonContent = "Add";
    [ObservableProperty] private bool _isEditingVehicle;

    partial void OnEditingVehicleIdChanged(int? value) => IsEditingVehicle = value.HasValue;

    [ObservableProperty] private int _selectedTabIndex;

    [ObservableProperty] private Creditor? _selectedVehicleCreditor;

    partial void OnSelectedVehicleCreditorChanged(Creditor? value)
    {
        if (value == null)
        {
            SelectedDebtor = null;
        }
        else
        {
            SelectedDebtor = Debtors.FirstOrDefault(d => d.CreditorId == value.CreditorId);
        }
    }

    [ObservableProperty] private Creditor? _selectedDebtorForEdit;

    partial void OnSelectedDebtorForEditChanged(Creditor? value)
    {
        if (value == null)
        {
            CancelEditDebtor();
        }
        else
        {
            var row = Debtors.FirstOrDefault(d => d.CreditorId == value.CreditorId);
            if (row != null)
            {
                EditDebtor(row);
            }
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedDebtorAsync()
    {
        if (!EditingDebtorId.HasValue) return;
        var debtorName = NewDebtorName;
        var confirm = MessageBox.Show(
            $"Are you sure you want to delete debtor '{debtorName}'?\nThis will hide the debtor from new transactions.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        
        if (confirm != MessageBoxResult.Yes) return;

        var result = await _creditorRepo.SoftDeleteAsync(EditingDebtorId.Value);
        if (result.Success)
        {
            DebtorStatusMessage = "✅ Debtor deleted successfully.";
            CancelEditDebtor();
            await LoadDataAsync();
        }
        else
        {
            DebtorStatusMessage = $"❌ {result.Error}";
        }
    }

    partial void OnSelectedDebtorChanged(DebtorDisplayRow? value)
    {
        IsDebtorSelected = value != null;
        _ = LoadSelectedDebtorVehiclesAsync();
    }

    public async Task LoadSelectedDebtorVehiclesAsync()
    {
        NewVehicleNumber = "";
        VehicleStatusMessage = "";
        SelectedDebtorVehicles.Clear();
        if (SelectedDebtor == null) return;

        var repo = App.Services.GetRequiredService<IDebtorVehicleRepository>();
        var result = await repo.GetByCreditorIdAsync(SelectedDebtor.CreditorId);
        if (result.Success && result.Data != null)
        {
            foreach (var v in result.Data)
            {
                SelectedDebtorVehicles.Add(v);
            }
        }
    }

    public DebtorManagementViewModel()
    {
        _creditorRepo = App.Services.GetRequiredService<ICreditorRepository>();
        _repaymentRepo = App.Services.GetRequiredService<ICreditorRepaymentRepository>();
        _dsmRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _printService = App.Services.GetRequiredService<PrintService>();
        _excelExportService = App.Services.GetRequiredService<ExcelExportService>();
        _dbContext = App.Services.GetRequiredService<FuelProDbContext>();

        _ = LoadDataAsync();
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            await LoadDsmPersonalDebtorsAsync();
            var creditorsRes = await _creditorRepo.GetAllActiveAsync();
            var creditors = creditorsRes.Success ? creditorsRes.Data ?? new List<Creditor>() : new List<Creditor>();

            // Query all DSM Shift entries to fetch debit records
            var allEntriesResult = await _dsmRepo.GetEntriesForDateRangeAsync(new DateTime(2000, 1, 1), DateTime.Today.AddYears(5));
            var allDebits = allEntriesResult.Success && allEntriesResult.Data != null
                ? allEntriesResult.Data.SelectMany(e => e.DebitEntries).ToList()
                : new List<DebitEntry>();

            var debitsGrouped = allDebits
                .GroupBy(d => d.DebtorName.Trim().ToLower())
                .ToDictionary(g => g.Key, g => g.Sum(d => d.Amount));

            // Query all repayment logs
            var repaymentsResult = await _repaymentRepo.GetByDateRangeAsync(new DateTime(2000, 1, 1), DateTime.Today.AddYears(5));
            var allRepayments = repaymentsResult.Success ? repaymentsResult.Data ?? new List<CreditorRepayment>() : new List<CreditorRepayment>();

            var repaymentsGrouped = allRepayments
                .GroupBy(r => r.CreditorName.Trim().ToLower())
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

            var rows = new List<DebtorDisplayRow>();
            foreach (var c in creditors)
            {
                var key = c.Name.Trim().ToLower();
                debitsGrouped.TryGetValue(key, out var totalDebt);
                repaymentsGrouped.TryGetValue(key, out var totalRepayment);

                rows.Add(new DebtorDisplayRow
                {
                    CreditorId = c.CreditorId,
                    Name = c.Name,
                    Phone = c.Phone ?? string.Empty,
                    TotalDebt = totalDebt,
                    TotalRepayment = totalRepayment
                });
            }

            Debtors = new ObservableCollection<DebtorDisplayRow>(rows.OrderBy(r => r.Name));
            
            DebtorsList.Clear();
            foreach (var c in creditors)
            {
                DebtorsList.Add(c);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load debtor details");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveDebtorAsync()
    {
        if (string.IsNullOrWhiteSpace(NewDebtorName))
        {
            DebtorStatusMessage = "❌ Debtor Name is required.";
            return;
        }

        try
        {
            if (IsEditingDebtor && EditingDebtorId.HasValue)
            {
                var creditor = new Creditor
                {
                    CreditorId = EditingDebtorId.Value,
                    Name = NewDebtorName.Trim(),
                    Phone = string.IsNullOrWhiteSpace(NewDebtorPhone) ? null : NewDebtorPhone.Trim(),
                    IsActive = true
                };
                var result = await _creditorRepo.UpdateAsync(creditor);
                if (result.Success)
                {
                    // Totals Adjustment Logic
                    var existingRow = Debtors.FirstOrDefault(d => d.CreditorId == EditingDebtorId.Value);
                    if (existingRow != null)
                    {
                        double diffDebt = EditTotalDebt - existingRow.TotalDebt;
                        double diffRepaid = EditRepaidAmount - existingRow.TotalRepayment;

                        if (Math.Abs(diffDebt) > 0.01)
                        {
                            using var context = App.Services.GetRequiredService<FuelProDbContext>();
                            var latestDsm = await context.DsmEntries.OrderByDescending(e => e.DsmEntryId).FirstOrDefaultAsync();
                            if (latestDsm != null)
                            {
                                var adjDebit = new DebitEntry
                                {
                                    DsmEntryId = latestDsm.DsmEntryId,
                                    DebtorName = creditor.Name,
                                    Amount = diffDebt,
                                    Remarks = string.IsNullOrWhiteSpace(EditEntryDetails) ? "Profile Edit Adjustment" : EditEntryDetails.Trim(),
                                    SlipNumber = "ADJ",
                                    VehicleNumber = "",
                                    CreatedAt = DateTime.Now
                                };
                                context.DebitEntries.Add(adjDebit);
                                await context.SaveChangesAsync();
                            }
                        }

                        if (Math.Abs(diffRepaid) > 0.01)
                        {
                            var adjRepay = new CreditorRepayment
                            {
                                CreditorName = creditor.Name,
                                RepaymentDate = DateTime.Today,
                                PaymentMode = "Adjustment",
                                ChequeNo = "ADJ",
                                Amount = diffRepaid,
                                CreatedAt = DateTime.Now
                            };
                            await _repaymentRepo.AddAsync(adjRepay);
                        }
                    }

                    DebtorStatusMessage = "✅ Debtor updated successfully!";
                    CancelEditDebtor();
                    await LoadDataAsync();
                }
                else
                {
                    DebtorStatusMessage = $"❌ {result.Error}";
                }
            }
            else
            {
                var creditor = new Creditor
                {
                    Name = NewDebtorName.Trim(),
                    Phone = string.IsNullOrWhiteSpace(NewDebtorPhone) ? null : NewDebtorPhone.Trim(),
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };
                var result = await _creditorRepo.AddAsync(creditor);
                if (result.Success)
                {
                    DebtorStatusMessage = "✅ Debtor added successfully!";
                    NewDebtorName = "";
                    NewDebtorPhone = "";
                    await LoadDataAsync();
                }
                else
                {
                    DebtorStatusMessage = $"❌ {result.Error}";
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save debtor master");
            DebtorStatusMessage = $"❌ Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void EditDebtor(DebtorDisplayRow? row)
    {
        if (row == null) return;
        EditingDebtorId = row.CreditorId;
        NewDebtorName = row.Name;
        NewDebtorPhone = row.Phone;
        EditTotalDebt = row.TotalDebt;
        EditRepaidAmount = row.TotalRepayment;
        EditEntryDetails = "Profile Edit Adjustment";
        IsEditingDebtor = true;
        DebtorFormTitle = "Edit Debtor Profile";
        DebtorButtonContent = "Update Profile";
        DebtorStatusMessage = "";
    }

    [RelayCommand]
    private void CancelEditDebtor()
    {
        EditingDebtorId = null;
        NewDebtorName = "";
        NewDebtorPhone = "";
        EditTotalDebt = 0;
        EditRepaidAmount = 0;
        EditEntryDetails = "Profile Edit Adjustment";
        IsEditingDebtor = false;
        DebtorFormTitle = "Add New Debtor";
        DebtorButtonContent = "Save Debtor";
        DebtorStatusMessage = "";
    }

    [RelayCommand]
    private async Task DeleteDebtorAsync(DebtorDisplayRow? row)
    {
        if (row == null) return;

        var confirm = MessageBox.Show(
            $"Are you sure you want to delete debtor '{row.Name}'?\nThis will hide the debtor from new transactions.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        
        if (confirm != MessageBoxResult.Yes) return;

        var result = await _creditorRepo.SoftDeleteAsync(row.CreditorId);
        if (result.Success)
        {
            DebtorStatusMessage = "✅ Debtor deleted successfully.";
            if (IsEditingDebtor && EditingDebtorId == row.CreditorId)
            {
                CancelEditDebtor();
            }
            await LoadDataAsync();
        }
        else
        {
            DebtorStatusMessage = $"❌ {result.Error}";
        }
    }

    [RelayCommand]
    private async Task AddRepaymentAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedDebtorName))
        {
            RepaymentStatus = "❌ Debtor Name is required.";
            return;
        }

        if (RepaymentAmount <= 0)
        {
            RepaymentStatus = "❌ Amount must be greater than zero.";
            return;
        }

        var debtorRow = Debtors.FirstOrDefault(d => d.Name.Equals(SelectedDebtorName, StringComparison.OrdinalIgnoreCase));
        double outstanding = debtorRow?.OutstandingBalance ?? 0;
        if (RepaymentAmount > outstanding)
        {
            RepaymentStatus = $"❌ Repayment exceeds outstanding balance of ₹{outstanding:F2}.";
            return;
        }

        // Auto-assign ShiftNumber for TID-sheet slot attribution:
        //   before 20:00 → Shift B (Day slot on the business day)
        //   from  20:00 → Shift A (Night slot on the business day)
        var shiftNumber = DateTime.Now.Hour < 20 ? "B" : "A";

        var repayment = new CreditorRepayment
        {
            CreditorName = SelectedDebtorName.Trim(),
            RepaymentDate = RepaymentDate.Date,
            PaymentMode = SelectedPaymentMode,
            ChequeNo = SelectedPaymentMode == "Cheque" ? ChequeNumber?.Trim() : null,
            Amount = RepaymentAmount,
            CreatedAt = DateTime.Now,
            ShiftNumber = shiftNumber,
            CardTid = (SelectedPaymentMode == "PhonePe" || SelectedPaymentMode == "PineLabs Card" || SelectedPaymentMode == "Others") ? CardTid?.Trim() : null,
            CardBatch = (SelectedPaymentMode == "PhonePe" || SelectedPaymentMode == "PineLabs Card" || SelectedPaymentMode == "Others") ? CardBatch?.Trim() : null,
            Denom500 = SelectedPaymentMode == "Cash" ? (Denom500 ?? 0) : 0,
            Denom200 = SelectedPaymentMode == "Cash" ? (Denom200 ?? 0) : 0,
            Denom100 = SelectedPaymentMode == "Cash" ? (Denom100 ?? 0) : 0,
            Denom50 = SelectedPaymentMode == "Cash" ? (Denom50 ?? 0) : 0,
            Denom20 = SelectedPaymentMode == "Cash" ? (Denom20 ?? 0) : 0,
            Denom10 = SelectedPaymentMode == "Cash" ? (Denom10 ?? 0) : 0,
            Coins = SelectedPaymentMode == "Cash" ? (Coins ?? 0) : 0
        };

        var result = await _repaymentRepo.AddAsync(repayment);
        if (result.Success)
        {
            RepaymentStatus = "✅ Repayment logged successfully!";
            RepaymentAmount = 0;
            ChequeNumber = "";
            CardTid = "";
            CardBatch = "";
            Denom500 = null;
            Denom200 = null;
            Denom100 = null;
            Denom50 = null;
            Denom20 = null;
            Denom10 = null;
            Coins = null;
            await LoadDataAsync();
            if (!string.IsNullOrEmpty(LedgerDebtorName) && LedgerDebtorName.Equals(SelectedDebtorName, StringComparison.OrdinalIgnoreCase))
            {
                await LoadLedgerAsync();
            }
        }
        else
        {
            RepaymentStatus = $"❌ {result.Error}";
        }
    }

    [RelayCommand]
    public async Task LoadLedgerAsync()
    {
        if (string.IsNullOrWhiteSpace(LedgerDebtorName))
        {
            LedgerStatus = "Please select a debtor.";
            return;
        }

        LedgerStatus = "Querying ledger...";
        try
        {
            var start = LedgerStartDate.Date;
            var end = LedgerEndDate.Date;
            var name = LedgerDebtorName.Trim();

            // Load all DSM entries for calculation of debts
            var entriesResult = await _dsmRepo.GetEntriesForDateRangeAsync(new DateTime(2000, 1, 1), DateTime.Today.AddYears(5));
            List<(DateTime Date, DebitEntry Debit, int PumpId)> allDebits;
            if (entriesResult.Success && entriesResult.Data != null)
            {
                allDebits = entriesResult.Data
                    .SelectMany(e => e.DebitEntries.Select(d => (Date: e.Shift?.ShiftDate ?? e.CreatedAt.Date, Debit: d, PumpId: e.PumpId)))
                    .Where(x => x.Debit.DebtorName.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                allDebits = new List<(DateTime Date, DebitEntry Debit, int PumpId)>();
            }

            // Load all repayments
            var repaymentsResult = await _repaymentRepo.GetByDateRangeAsync(new DateTime(2000, 1, 1), DateTime.Today.AddYears(5));
            var allRepayments = repaymentsResult.Success && repaymentsResult.Data != null
                ? repaymentsResult.Data
                    .Where(r => r.CreditorName.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : new List<CreditorRepayment>();

            // Calculate opening balance before start date
            double openingDebits = allDebits.Where(x => x.Date < start).Sum(x => (double)x.Debit.Amount);
            double openingCredits = allRepayments.Where(r => r.RepaymentDate < start).Sum(r => r.Amount);
            double openingBalance = openingDebits - openingCredits;

            var txList = new List<LedgerTransactionRow>();

            // Check day lock status for unique dates
            var dayLockService = App.Services.GetRequiredService<IDayLockService>();
            var auth = App.Services.GetRequiredService<AuthService>();
            var isOwner = auth.CurrentUser?.IsOwner ?? false;

            var uniqueDates = allDebits.Select(x => x.Date.Date)
                .Concat(allRepayments.Select(r => r.RepaymentDate.Date))
                .Distinct()
                .ToList();
            var dayLocks = new Dictionary<DateTime, bool>();
            foreach (var d in uniqueDates)
            {
                dayLocks[d] = await dayLockService.IsDateLockedAsync(d);
            }

            var currentDebits = allDebits
                .Where(x => x.Date >= start && x.Date <= end)
                .Select(x => {
                    bool isLocked = dayLocks.TryGetValue(x.Date.Date, out bool locked) && locked;
                    bool within48Hours = (DateTime.Now - x.Debit.CreatedAt).TotalHours <= 48;
                    bool canEdit = isOwner || (!isLocked && within48Hours);

                    return new LedgerTransactionRow
                    {
                        Date = x.Date,
                        Description = $"Debt (Pump {x.PumpId}){(string.IsNullOrEmpty(x.Debit.SlipNumber) ? "" : $" [Slip: {x.Debit.SlipNumber}]")}",
                        Debit = x.Debit.Amount,
                        Credit = 0,
                        TransactionType = "Debt",
                        TransactionId = x.Debit.DebitId,
                        SlipNumber = x.Debit.SlipNumber,
                        Remarks = x.Debit.Remarks,
                        VehicleNumber = x.Debit.VehicleNumber,
                        Amount = x.Debit.Amount,
                        CreatedAt = x.Debit.CreatedAt,
                        CanEdit = canEdit
                    };
                })
                .ToList();

            var currentCredits = allRepayments
                .Where(r => r.RepaymentDate >= start && r.RepaymentDate <= end)
                .Select(r => {
                    bool isLocked = dayLocks.TryGetValue(r.RepaymentDate.Date, out bool locked) && locked;
                    bool within48Hours = (DateTime.Now - r.CreatedAt).TotalHours <= 48;
                    bool canEdit = isOwner || (!isLocked && within48Hours);

                    return new LedgerTransactionRow
                    {
                        Date = r.RepaymentDate,
                        Description = $"Repayment ({r.PaymentMode}){(string.IsNullOrEmpty(r.ChequeNo) ? "" : $" [Ref: {r.ChequeNo}]")}",
                        Debit = 0,
                        Credit = r.Amount,
                        TransactionType = "Repayment",
                        TransactionId = r.CreditorRepaymentId,
                        SlipNumber = r.ChequeNo,
                        Remarks = "Repayment",
                        Amount = r.Amount,
                        CreatedAt = r.CreatedAt,
                        CanEdit = canEdit
                    };
                })
                .ToList();

            var merged = currentDebits
                .Concat(currentCredits)
                .OrderBy(t => t.Date)
                .ToList();

            double running = openingBalance;
            var finalRows = new List<LedgerTransactionRow>();
            
            // Add opening balance row
            finalRows.Add(new LedgerTransactionRow
            {
                Date = start,
                Description = "Opening Balance",
                Debit = 0,
                Credit = 0,
                RunningBalance = openingBalance
            });

            foreach (var t in merged)
            {
                running += t.Debit - t.Credit;
                t.RunningBalance = running;
                finalRows.Add(t);
            }

            LedgerTransactions = new ObservableCollection<LedgerTransactionRow>(finalRows);
            LedgerOutstandingBalance = running;
            LedgerOpeningBalance = openingBalance;
            LedgerTotalDebt = openingDebits + merged.Sum(t => t.Debit);
            LedgerTotalRepayments = openingCredits + merged.Sum(t => t.Credit);
            LedgerStatus = "";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load debtor ledger database records");
            LedgerStatus = $"❌ Query failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void PrintLedger()
    {
        if (string.IsNullOrWhiteSpace(LedgerDebtorName))
        {
            MessageBox.Show("Please select a debtor first.", "Print Ledger", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (LedgerTransactions == null || LedgerTransactions.Count == 0)
        {
            MessageBox.Show("No transactions to print. Please generate the ledger first.", "Print Ledger", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var debtorRow = Debtors.FirstOrDefault(d => d.Name.Equals(LedgerDebtorName, StringComparison.OrdinalIgnoreCase));
            var printRows = LedgerTransactions.Select(t => new DebtorLedgerPrintRow
            {
                Date = t.Date.ToString("dd-MMM-yyyy"),
                Description = t.Description,
                Debit = t.Debit,
                Credit = t.Credit,
                RunningBalance = t.RunningBalance
            }).ToList();

            var printData = new DebtorLedgerPrintData
            {
                DebtorName = LedgerDebtorName,
                DebtorPhone = debtorRow?.Phone ?? "N/A",
                StartDate = LedgerStartDate.ToString("dd-MMM-yyyy"),
                EndDate = LedgerEndDate.ToString("dd-MMM-yyyy"),
                OpeningBalance = LedgerTransactions.FirstOrDefault()?.RunningBalance ?? 0,
                TotalDebt = LedgerTotalDebt,
                TotalRepayments = LedgerTotalRepayments,
                ClosingBalance = LedgerOutstandingBalance,
                Transactions = printRows
            };

            _printService.PrintDebtorLedger(printData);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to print debtor statement");
            MessageBox.Show($"Print failed: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void StartEditVehicle(DebtorVehicle? vehicle)
    {
        if (vehicle == null) return;
        EditingVehicleId = vehicle.DebtorVehicleId;
        NewVehicleNumber = vehicle.VehicleNumber;
        VehicleButtonContent = "Update";
        VehicleStatusMessage = "";
    }

    [RelayCommand]
    private void CancelEditVehicle()
    {
        EditingVehicleId = null;
        NewVehicleNumber = "";
        VehicleButtonContent = "Add";
        VehicleStatusMessage = "";
    }

    [RelayCommand]
    private async Task AddVehicleAsync()
    {
        if (SelectedDebtor == null) return;
        if (string.IsNullOrWhiteSpace(NewVehicleNumber))
        {
            VehicleStatusMessage = "❌ Vehicle number is required.";
            return;
        }

        var repo = App.Services.GetRequiredService<IDebtorVehicleRepository>();
        if (EditingVehicleId.HasValue)
        {
            var result = await repo.UpdateVehicleNumberAsync(EditingVehicleId.Value, NewVehicleNumber.Trim().ToUpper());
            if (result.Success)
            {
                VehicleStatusMessage = "✅ Vehicle updated successfully!";
                CancelEditVehicle();
                await LoadSelectedDebtorVehiclesAsync();
            }
            else
            {
                VehicleStatusMessage = $"❌ {result.Error}";
            }
        }
        else
        {
            var vehicle = new DebtorVehicle
            {
                CreditorId = SelectedDebtor.CreditorId,
                VehicleNumber = NewVehicleNumber.Trim().ToUpper(),
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            var result = await repo.AddAsync(vehicle);
            if (result.Success)
            {
                VehicleStatusMessage = "✅ Vehicle added successfully!";
                NewVehicleNumber = "";
                await LoadSelectedDebtorVehiclesAsync();
            }
            else
            {
                VehicleStatusMessage = $"❌ {result.Error}";
            }
        }
    }

    [RelayCommand]
    private async Task DeleteVehicleAsync(DebtorVehicle? vehicle)
    {
        if (vehicle == null) return;
        var confirm = MessageBox.Show(
            $"Are you sure you want to delete vehicle '{vehicle.VehicleNumber}'?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        var repo = App.Services.GetRequiredService<IDebtorVehicleRepository>();
        var result = await repo.DeleteAsync(vehicle.DebtorVehicleId);
        if (result.Success)
        {
            VehicleStatusMessage = "✅ Vehicle deleted successfully.";
            await LoadSelectedDebtorVehiclesAsync();
        }
        else
        {
            VehicleStatusMessage = $"❌ {result.Error}";
        }
    }

    [RelayCommand]
    private void StartEditTransaction(LedgerTransactionRow? tx)
    {
        if (tx == null || !tx.CanEdit) return;
        SelectedTransaction = tx;
        EditTxAmount = tx.Amount;
        EditTxSlipNumber = tx.SlipNumber;
        EditTxVehicleNumber = tx.VehicleNumber;
        EditTxRemarks = tx.Remarks;
        EditReason = "";
        EditTxStatusMessage = "";
        IsEditingTransaction = true;
    }

    [RelayCommand]
    private void CancelEditTransaction()
    {
        SelectedTransaction = null;
        IsEditingTransaction = false;
        EditReason = "";
        EditTxStatusMessage = "";
    }

    [RelayCommand]
    private async Task SaveTransactionEditAsync()
    {
        if (SelectedTransaction == null) return;

        if (EditTxAmount <= 0)
        {
            EditTxStatusMessage = "❌ Amount must be greater than zero.";
            return;
        }

        var auth = App.Services.GetRequiredService<AuthService>();
        var isOwner = auth.CurrentUser?.IsOwner ?? false;

        if (isOwner && string.IsNullOrWhiteSpace(EditReason))
        {
            EditTxStatusMessage = "❌ Owner edits require a justification reason.";
            return;
        }

        try
        {
            if (SelectedTransaction.TransactionType == "Debt")
            {
                using var context = App.Services.GetRequiredService<FuelProDbContext>();
                var entry = await context.DebitEntries.FindAsync(SelectedTransaction.TransactionId);
                if (entry != null)
                {
                    var auditLogService = App.Services.GetRequiredService<IAuditLogService>();
                    var currentUser = auth.CurrentUser?.Username ?? "Unknown";

                    var changes = new Dictionary<string, (string? oldVal, string? newVal)>();
                    if (Math.Abs(entry.Amount - EditTxAmount) > 0.01)
                    {
                        changes["Amount"] = (entry.Amount.ToString("F2"), EditTxAmount.ToString("F2"));
                        entry.Amount = EditTxAmount;
                    }
                    if (entry.SlipNumber != EditTxSlipNumber)
                    {
                        changes["SlipNumber"] = (entry.SlipNumber, EditTxSlipNumber);
                        entry.SlipNumber = EditTxSlipNumber;
                    }
                    if (entry.VehicleNumber != EditTxVehicleNumber)
                    {
                        changes["VehicleNumber"] = (entry.VehicleNumber, EditTxVehicleNumber);
                        entry.VehicleNumber = EditTxVehicleNumber;
                    }
                    if (entry.Remarks != EditTxRemarks)
                    {
                        changes["Remarks"] = (entry.Remarks, EditTxRemarks);
                        entry.Remarks = EditTxRemarks;
                    }

                    if (changes.Any())
                    {
                        context.DebitEntries.Update(entry);
                        await context.SaveChangesAsync();

                        var reasonStr = isOwner ? EditReason : "Manager edit within 48h";
                        foreach (var change in changes)
                        {
                            await auditLogService.LogAsync(
                                tableName: "DebitEntries",
                                recordId: entry.DebitId,
                                action: "Update",
                                fieldName: change.Key,
                                oldValue: change.Value.oldVal,
                                newValue: change.Value.newVal,
                                modifiedBy: currentUser,
                                reason: reasonStr
                            );
                        }
                    }
                }
            }
            else if (SelectedTransaction.TransactionType == "Repayment")
            {
                using var context = App.Services.GetRequiredService<FuelProDbContext>();
                var repayment = await context.CreditorRepayments.FindAsync(SelectedTransaction.TransactionId);
                if (repayment != null)
                {
                    var auditLogService = App.Services.GetRequiredService<IAuditLogService>();
                    var currentUser = auth.CurrentUser?.Username ?? "Unknown";

                    var changes = new Dictionary<string, (string? oldVal, string? newVal)>();
                    if (Math.Abs(repayment.Amount - EditTxAmount) > 0.01)
                    {
                        changes["Amount"] = (repayment.Amount.ToString("F2"), EditTxAmount.ToString("F2"));
                        repayment.Amount = EditTxAmount;
                    }
                    if (repayment.ChequeNo != EditTxSlipNumber)
                    {
                        changes["ChequeNo"] = (repayment.ChequeNo, EditTxSlipNumber);
                        repayment.ChequeNo = EditTxSlipNumber;
                    }

                    if (changes.Any())
                    {
                        context.CreditorRepayments.Update(repayment);
                        await context.SaveChangesAsync();

                        var reasonStr = isOwner ? EditReason : "Manager edit within 48h";
                        foreach (var change in changes)
                        {
                            await auditLogService.LogAsync(
                                tableName: "CreditorRepayments",
                                recordId: repayment.CreditorRepaymentId,
                                action: "Update",
                                fieldName: change.Key,
                                oldValue: change.Value.oldVal,
                                newValue: change.Value.newVal,
                                modifiedBy: currentUser,
                                reason: reasonStr
                            );
                        }
                    }
                }
            }

            IsEditingTransaction = false;
            SelectedTransaction = null;
            EditReason = "";
            EditTxStatusMessage = "";

            await LoadDataAsync();
            await LoadLedgerAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save transaction edit");
            EditTxStatusMessage = $"❌ Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportLedgerExcelAsync()
    {
        if (string.IsNullOrWhiteSpace(LedgerDebtorName))
        {
            MessageBox.Show("Please select a debtor first.", "Export Ledger", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (LedgerTransactions == null || LedgerTransactions.Count == 0)
        {
            MessageBox.Show("No transactions to export. Please generate the ledger first.", "Export Ledger", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var debtorRow = Debtors.FirstOrDefault(d => d.Name.Equals(LedgerDebtorName, StringComparison.OrdinalIgnoreCase));
            var printRows = LedgerTransactions.Select(t => new DebtorLedgerPrintRow
            {
                Date = t.Date.ToString("dd-MMM-yyyy"),
                Description = t.Description,
                Debit = t.Debit,
                Credit = t.Credit,
                RunningBalance = t.RunningBalance
            }).ToList();

            var printData = new DebtorLedgerPrintData
            {
                DebtorName = LedgerDebtorName,
                DebtorPhone = debtorRow?.Phone ?? "N/A",
                StartDate = LedgerStartDate.ToString("dd-MMM-yyyy"),
                EndDate = LedgerEndDate.ToString("dd-MMM-yyyy"),
                OpeningBalance = LedgerTransactions.FirstOrDefault()?.RunningBalance ?? 0,
                TotalDebt = LedgerTotalDebt,
                TotalRepayments = LedgerTotalRepayments,
                ClosingBalance = LedgerOutstandingBalance,
                Transactions = printRows
            };

            var path = await _excelExportService.ExportDebtorLedgerAsync(printData);
            MessageBox.Show($"Ledger statement exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export debtor ledger to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ShareOnWhatsAppAsync()
    {
        if (string.IsNullOrWhiteSpace(LedgerDebtorName))
        {
            MessageBox.Show("Please select a debtor first.", "WhatsApp Share", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (LedgerTransactions == null || LedgerTransactions.Count == 0)
        {
            MessageBox.Show("No transactions to share. Please generate the ledger first.", "WhatsApp Share", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var debtorRow = Debtors.FirstOrDefault(d => d.Name.Equals(LedgerDebtorName, StringComparison.OrdinalIgnoreCase));
            var phone = debtorRow?.Phone ?? "";
            
            if (string.IsNullOrWhiteSpace(phone))
            {
                MessageBox.Show("Selected debtor has no mobile number stored. Please update profile.", "WhatsApp Share", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var cleanPhone = new string(phone.Where(char.IsDigit).ToArray());
            if (cleanPhone.Length == 10)
            {
                cleanPhone = "91" + cleanPhone;
            }

            // 1. Generate Statement PDF using Edge headless rendering
            var printRows = LedgerTransactions.Select(t => new DebtorLedgerPrintRow
            {
                Date = t.Date.ToString("dd-MMM-yyyy"),
                Description = t.Description,
                Debit = t.Debit,
                Credit = t.Credit,
                RunningBalance = t.RunningBalance
            }).ToList();

            var printData = new DebtorLedgerPrintData
            {
                DebtorName = LedgerDebtorName,
                DebtorPhone = debtorRow?.Phone ?? "N/A",
                StartDate = LedgerStartDate.ToString("dd-MMM-yyyy"),
                EndDate = LedgerEndDate.ToString("dd-MMM-yyyy"),
                OpeningBalance = LedgerTransactions.FirstOrDefault()?.RunningBalance ?? 0,
                TotalDebt = LedgerTotalDebt,
                TotalRepayments = LedgerTotalRepayments,
                ClosingBalance = LedgerOutstandingBalance,
                Transactions = printRows
            };

            var pdfPath = _printService.GenerateDebtorLedgerPdf(printData);

            // 2. Copy the PDF path to Windows Clipboard as FileDropList (allowing instant paste/attachment via Ctrl+V)
            var fileList = new System.Collections.Specialized.StringCollection { pdfPath };
            Clipboard.SetFileDropList(fileList);

            // 3. Retrieve Petrol Pump name from DB settings
            var settings = _dbContext.Settings.FirstOrDefault();
            var stationName = settings?.StationDisplayName ?? "Mitali Service Station";

            // 4. Prefill professional message
            var message = $"Hello {LedgerDebtorName},\n\n" +
                          $"Please find your account statement attached for the selected period.\n\n" +
                          $"Kindly verify the statement and contact us if you have any questions.\n\n" +
                          $"Regards,\n" +
                          $"{stationName}";

            var escapedMsg = Uri.EscapeDataString(message);
            bool launchedDesktop = false;

            // 5. Open WhatsApp Desktop with fallback to WhatsApp Web
            try
            {
                var desktopUrl = $"whatsapp://send?phone={cleanPhone}&text={escapedMsg}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = desktopUrl, UseShellExecute = true });
                launchedDesktop = true;
            }
            catch (Exception)
            {
                var webUrl = $"https://web.whatsapp.com/send?phone={cleanPhone}&text={escapedMsg}";
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = webUrl, UseShellExecute = true });
            }

            // 6. Automated pasting and attachment send
            _ = Task.Run(async () =>
            {
                // Wait for WhatsApp to open and load the chat window
                await Task.Delay(4000);

                try
                {
                    var processes = System.Diagnostics.Process.GetProcessesByName("WhatsApp");
                    if (processes.Length > 0)
                    {
                        var handle = processes[0].MainWindowHandle;
                        if (handle != IntPtr.Zero)
                        {
                            SetForegroundWindow(handle);
                        }
                    }
                }
                catch { }

                // Simulate Ctrl+V to paste the PDF
                SimulateCtrlV();

                // Wait for WhatsApp to render the attachment preview screen
                await Task.Delay(1500);

                // Simulate Enter key to send the attachment
                SimulateEnter();
            });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to share on WhatsApp");
            MessageBox.Show($"Failed to initiate WhatsApp share: {ex.Message}", "WhatsApp Share Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Merged Financial Summary Fields & Properties
    [ObservableProperty] private DateTime _financialStartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _financialEndDate = DateTime.Today;
    [ObservableProperty] private bool _isFinancialLoading;

    [ObservableProperty] private double _totalBankCash;
    [ObservableProperty] private double _totalCashInHand;
    [ObservableProperty] private double _totalDigitalPayments;
    [ObservableProperty] private double _totalExpenses;
    [ObservableProperty] private double _totalCreditorRepayments;
    [ObservableProperty] private double _totalDebitorsOutstanding;
    [ObservableProperty] private double _netCashPosition;
    [ObservableProperty] private double _overallOutstandingBalance;
    [ObservableProperty] private int _activeDebtorsCount;

    public ObservableCollection<CreditorRepayment> Repayments { get; } = new();
    public ObservableCollection<DebtorLogEntryDto> DebtorLogs { get; } = new();

    partial void OnFinancialStartDateChanged(DateTime value) => _ = LoadFinancialsAsync();
    partial void OnFinancialEndDateChanged(DateTime value) => _ = LoadFinancialsAsync();

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (value == 2) // Financial Summary tab
        {
            _ = LoadFinancialsAsync();
        }
    }

    [RelayCommand]
    public async Task LoadFinancialsAsync()
    {
        IsFinancialLoading = true;
        try
        {
            var entriesResult = await _dsmRepo.GetEntriesForDateRangeAsync(FinancialStartDate, FinancialEndDate);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            var shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
            var shiftsResult = await shiftRepo.GetShiftsByDateRangeAsync(FinancialStartDate, FinancialEndDate);
            var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();

            TotalBankCash = 0; TotalCashInHand = 0; TotalDigitalPayments = 0; TotalExpenses = 0;
            TotalDebitorsOutstanding = 0;
            DebtorLogs.Clear();

            foreach (var entry in entries)
            {
                TotalBankCash += entry.CashDenominations.Where(x => x.CashType == "Cash1").Sum(x => x.TotalAmount)
                                 + (entry.PaymentCollection?.CashDeposit ?? 0);
                TotalCashInHand += entry.CashDenominations.Where(x => x.CashType == "Cash2").Sum(x => x.TotalAmount);

                TotalDigitalPayments += (entry.PaymentCollection?.PhonePe ?? 0)
                                        + (entry.PaymentCollection?.PhonePeCardMorning ?? 0)
                                        + (entry.PaymentCollection?.PhonePeCardNight ?? 0)
                                        + (entry.PaymentCollection?.CreditCard ?? 0)
                                        + (entry.PaymentCollection?.PetroCard ?? 0)
                                        + (entry.PaymentCollection?.Others ?? 0);

                TotalExpenses += entry.Expenses.Sum(e => e.Amount);
                TotalDebitorsOutstanding += entry.DebitEntries.Sum(d => d.Amount);

                foreach (var d in entry.DebitEntries)
                {
                    DebtorLogs.Add(new DebtorLogEntryDto
                    {
                        Date = entry.Shift?.ShiftDate ?? entry.CreatedAt,
                        DsmName = entry.DsmName,
                        DebtorName = d.DebtorName,
                        ChequeNo = d.ChequeNo,
                        Amount = d.Amount
                    });
                }
            }

            var expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
            var shiftExpResult = await expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
            if (shiftExpResult.Success && shiftExpResult.Data != null)
                TotalExpenses += shiftExpResult.Data.Sum(e => e.Amount);

            var repResult = await _repaymentRepo.GetByDateRangeAsync(FinancialStartDate, FinancialEndDate);
            Repayments.Clear();
            if (repResult.Success && repResult.Data != null)
            {
                TotalCreditorRepayments = (double)repResult.Data.Sum(r => r.Amount);
                foreach (var r in repResult.Data)
                    Repayments.Add(r);
            }

            NetCashPosition = TotalBankCash + TotalCashInHand + TotalDigitalPayments - TotalExpenses;

            var creditorRepo = App.Services.GetRequiredService<ICreditorRepository>();
            var creditorsRes = await creditorRepo.GetAllActiveAsync();
            var creditors = creditorsRes.Success ? creditorsRes.Data ?? new List<Creditor>() : new List<Creditor>();

            var allEntriesResult = await _dsmRepo.GetEntriesForDateRangeAsync(new DateTime(2000, 1, 1), DateTime.Today.AddYears(5));
            var allDebits = allEntriesResult.Success && allEntriesResult.Data != null
                ? allEntriesResult.Data.SelectMany(e => e.DebitEntries).ToList()
                : new List<DebitEntry>();

            var debitsGrouped = allDebits
                .GroupBy(d => d.DebtorName.Trim().ToLower())
                .ToDictionary(g => g.Key, g => g.Sum(d => d.Amount));

            var repaymentsResult = await _repaymentRepo.GetByDateRangeAsync(new DateTime(2000, 1, 1), DateTime.Today.AddYears(5));
            var allRepayments = repaymentsResult.Success ? repaymentsResult.Data ?? new List<CreditorRepayment>() : new List<CreditorRepayment>();

            var repaymentsGrouped = allRepayments
                .GroupBy(r => r.CreditorName.Trim().ToLower())
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

            double overallOutstanding = 0;
            int activeCount = 0;
            foreach (var c in creditors)
            {
                var key = c.Name.Trim().ToLower();
                debitsGrouped.TryGetValue(key, out var totalDebt);
                repaymentsGrouped.TryGetValue(key, out var totalRepayment);
                var balance = totalDebt - totalRepayment;
                if (balance > 0.01)
                {
                    overallOutstanding += balance;
                    activeCount++;
                }
            }

            OverallOutstandingBalance = overallOutstanding;
            ActiveDebtorsCount = activeCount;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load financial summary in merged view");
        }
        finally { IsFinancialLoading = false; }
    }

    [RelayCommand]
    private void PrintFinancialSummary()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Net Cash Position", Value = "₹" + NetCashPosition.ToString("N2"), Highlight = true },
                new() { Label = "Bank Cash", Value = "₹" + TotalBankCash.ToString("N2"), Highlight = false },
                new() { Label = "Cash In Hand", Value = "₹" + TotalCashInHand.ToString("N2"), Highlight = false },
                new() { Label = "Digital Collections", Value = "₹" + TotalDigitalPayments.ToString("N2"), Highlight = false },
                new() { Label = "Total Expenses", Value = "₹" + TotalExpenses.ToString("N2"), Highlight = false },
                new() { Label = "Debtor Repayments", Value = "₹" + TotalCreditorRepayments.ToString("N2"), Highlight = false },
                new() { Label = "Debtor Debits", Value = "₹" + TotalDebitorsOutstanding.ToString("N2"), Highlight = false },
                new() { Label = "Overall Outstanding", Value = "₹" + OverallOutstandingBalance.ToString("N2"), Highlight = false },
                new() { Label = "Overall Active Debtors", Value = ActiveDebtorsCount.ToString(), Highlight = false }
            };

            var headers = new List<string> { "Date", "Type / Mode", "Debtor Name", "Reference / Cheque", "Debit (Sales on Credit)", "Credit (Repayments Received)" };
            var rows = new List<List<string>>();

            foreach (var d in DebtorLogs)
            {
                rows.Add(new List<string>
                {
                    d.Date.ToString("dd-MMM-yyyy"),
                    "Debtor Debit",
                    d.DebtorName,
                    d.ChequeNo ?? "—",
                    "₹" + d.Amount.ToString("N2"),
                    "—"
                });
            }

            foreach (var r in Repayments)
            {
                rows.Add(new List<string>
                {
                    r.RepaymentDate.ToString("dd-MMM-yyyy"),
                    $"Repayment ({r.PaymentMode})",
                    r.CreditorName,
                    r.ChequeNo ?? "—",
                    "—",
                    "₹" + r.Amount.ToString("N2")
                });
            }

            rows = rows.OrderBy(r => DateTime.ParseExact(r[0], "dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture)).ToList();

            var printData = new GenericGridPrintData
            {
                Title = "Financial Summary Statement",
                Subtitle = $"Date Range: {FinancialStartDate:dd-MMM-yyyy} to {FinancialEndDate:dd-MMM-yyyy}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            _printService.PrintGenericGrid(printData);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to print Financial Summary report");
            MessageBox.Show($"Print failed: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ExportFinancialSummaryExcelAsync()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Net Cash Position", Value = "₹" + NetCashPosition.ToString("N2"), Highlight = true },
                new() { Label = "Bank Cash", Value = "₹" + TotalBankCash.ToString("N2"), Highlight = false },
                new() { Label = "Cash In Hand", Value = "₹" + TotalCashInHand.ToString("N2"), Highlight = false },
                new() { Label = "Digital Collections", Value = "₹" + TotalDigitalPayments.ToString("N2"), Highlight = false },
                new() { Label = "Total Expenses", Value = "₹" + TotalExpenses.ToString("N2"), Highlight = false },
                new() { Label = "Debtor Repayments", Value = "₹" + TotalCreditorRepayments.ToString("N2"), Highlight = false },
                new() { Label = "Debtor Debits", Value = "₹" + TotalDebitorsOutstanding.ToString("N2"), Highlight = false },
                new() { Label = "Overall Outstanding", Value = "₹" + OverallOutstandingBalance.ToString("N2"), Highlight = false },
                new() { Label = "Overall Active Debtors", Value = ActiveDebtorsCount.ToString(), Highlight = false }
            };

            var headers = new List<string> { "Date", "Type / Mode", "Debtor Name", "Reference / Cheque", "Debit (Sales on Credit)", "Credit (Repayments Received)" };
            var rows = new List<List<string>>();

            foreach (var d in DebtorLogs)
            {
                rows.Add(new List<string>
                {
                    d.Date.ToString("dd-MMM-yyyy"),
                    "Debtor Debit",
                    d.DebtorName,
                    d.ChequeNo ?? "—",
                    "₹" + d.Amount.ToString("N2"),
                    "—"
                });
            }

            foreach (var r in Repayments)
            {
                rows.Add(new List<string>
                {
                    r.RepaymentDate.ToString("dd-MMM-yyyy"),
                    $"Repayment ({r.PaymentMode})",
                    r.CreditorName,
                    r.ChequeNo ?? "—",
                    "—",
                    "₹" + r.Amount.ToString("N2")
                });
            }

            rows = rows.OrderBy(r => DateTime.ParseExact(r[0], "dd-MMM-yyyy", System.Globalization.CultureInfo.InvariantCulture)).ToList();

            var printData = new GenericGridPrintData
            {
                Title = "Financial Summary Statement",
                Subtitle = $"Date Range: {FinancialStartDate:dd-MMM-yyyy} to {FinancialEndDate:dd-MMM-yyyy}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var path = await _excelExportService.ExportGenericGridAsync(printData, "FinancialSummary");
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export Financial Summary to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // --- DSM Personal Debtors Section ---
    [ObservableProperty] private ObservableCollection<DsmPersonalDebtorSummaryRow> _dsmPersonalDebtorsSummary = new();
    [ObservableProperty] private DsmPersonalDebtorSummaryRow? _selectedDsmPersonalDebtorSummary;

    [ObservableProperty] private ObservableCollection<DsmPersonalDebtor> _selectedDsmPersonalDebtorEntries = new();
    [ObservableProperty] private DsmPersonalDebtor? _selectedDsmPersonalDebtorEntry;

    [ObservableProperty] private double _personalRepaymentAmount;
    [ObservableProperty] private string _personalRepaymentPaymentMethod = "Cash";
    [ObservableProperty] private DateTime _personalRepaymentDate = DateTime.Today;
    [ObservableProperty] private string _personalRepaymentRemarks = "";

    partial void OnSelectedDsmPersonalDebtorSummaryChanged(DsmPersonalDebtorSummaryRow? value)
    {
        _ = LoadSelectedDsmPersonalDebtorEntriesAsync();
    }

    public async Task LoadDsmPersonalDebtorsAsync()
    {
        try
        {
            var debtorsList = await _dbContext.DsmPersonalDebtors.ToListAsync();
            var repaymentsList = await _dbContext.DsmPersonalDebtorRepayments.ToListAsync();

            var summary = debtorsList
                .GroupBy(d => d.DsmName, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var dsmName = g.Key;
                    var totalBorrowed = g.Sum(d => d.Amount);
                    var personalDebtorIds = g.Select(d => d.Id).ToList();
                    var totalRepaid = repaymentsList
                        .Where(r => personalDebtorIds.Contains(r.DsmPersonalDebtorId))
                        .Sum(r => r.Amount);

                    return new DsmPersonalDebtorSummaryRow
                    {
                        DsmName = dsmName,
                        TotalBorrowed = totalBorrowed,
                        TotalRepaid = totalRepaid
                    };
                })
                .Where(s => s.Balance > 0 || s.TotalBorrowed > 0)
                .OrderBy(s => s.DsmName)
                .ToList();

            DsmPersonalDebtorsSummary.Clear();
            foreach (var s in summary)
            {
                DsmPersonalDebtorsSummary.Add(s);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load DSM personal debtors summary");
        }
    }

    public async Task LoadSelectedDsmPersonalDebtorEntriesAsync()
    {
        SelectedDsmPersonalDebtorEntries.Clear();
        SelectedDsmPersonalDebtorEntry = null;
        PersonalRepaymentAmount = 0;
        PersonalRepaymentRemarks = "";

        if (SelectedDsmPersonalDebtorSummary == null) return;

        try
        {
            var name = SelectedDsmPersonalDebtorSummary.DsmName;
            var entries = await _dbContext.DsmPersonalDebtors
                .Where(d => d.DsmName == name)
                .OrderByDescending(d => d.Date)
                .ToListAsync();

            var repayments = await _dbContext.DsmPersonalDebtorRepayments.ToListAsync();

            foreach (var e in entries)
            {
                e.RepaidAmount = repayments
                    .Where(r => r.DsmPersonalDebtorId == e.Id)
                    .Sum(r => r.Amount);

                SelectedDsmPersonalDebtorEntries.Add(e);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load DSM personal debtor entries for selection");
        }
    }

    [RelayCommand]
    private async Task ToggleSalaryDeductionAsync(DsmPersonalDebtor debtor)
    {
        if (debtor == null) return;
        try
        {
            var dbEntry = await _dbContext.DsmPersonalDebtors.FindAsync(debtor.Id);
            if (dbEntry != null)
            {
                dbEntry.DeductFromSalary = debtor.DeductFromSalary;
                await _dbContext.SaveChangesAsync();
                
                // Let's force sync
                var syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
                _ = syncEngine.ForceSyncAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to toggle salary deduction");
            MessageBox.Show($"Error toggling salary deduction: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task SavePersonalRepaymentAsync()
    {
        if (SelectedDsmPersonalDebtorEntry == null)
        {
            MessageBox.Show("Please select a personal debtor entry to repay.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (PersonalRepaymentAmount <= 0)
        {
            MessageBox.Show("Please enter a valid repayment amount.", "Invalid Amount", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var remaining = SelectedDsmPersonalDebtorEntry.Amount - SelectedDsmPersonalDebtorEntry.RepaidAmount;
        if (PersonalRepaymentAmount > remaining + 0.01)
        {
            MessageBox.Show($"Repayment amount cannot exceed the remaining balance of ₹{remaining:N2}.", "Excess Repayment", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var repayment = new DsmPersonalDebtorRepayment
            {
                DsmPersonalDebtorId = SelectedDsmPersonalDebtorEntry.Id,
                Amount = PersonalRepaymentAmount,
                Date = PersonalRepaymentDate,
                PaymentMethod = PersonalRepaymentPaymentMethod,
                Source = "OwnerPayroll",
                Denom500 = 0,
                Denom200 = 0,
                Denom100 = 0,
                Denom50 = 0,
                Denom20 = 0,
                Denom10 = 0,
                Coins = 0
            };

            _dbContext.DsmPersonalDebtorRepayments.Add(repayment);
            await _dbContext.SaveChangesAsync();

            MessageBox.Show("Repayment recorded successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            // Reload data
            await LoadSelectedDsmPersonalDebtorEntriesAsync();
            await LoadDsmPersonalDebtorsAsync();

            var syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
            _ = syncEngine.ForceSyncAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save personal debtor repayment");
            MessageBox.Show($"Error saving repayment: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const byte VK_RETURN = 0x0D;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static void SimulateCtrlV()
    {
        keybd_event(VK_CONTROL, 0, 0, 0);
        keybd_event(VK_V, 0, 0, 0);
        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, 0);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
    }

    private static void SimulateEnter()
    {
        keybd_event(VK_RETURN, 0, 0, 0);
        keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, 0);
    }
}

public class DsmPersonalDebtorSummaryRow
{
    public string DsmName { get; set; } = string.Empty;
    public double TotalBorrowed { get; set; }
    public double TotalRepaid { get; set; }
    public double Balance => TotalBorrowed - TotalRepaid;
}

