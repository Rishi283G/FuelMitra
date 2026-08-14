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



public class DsmPersonalDebtorLedgerRow
{
    public DateTime Date { get; set; }
    public string Description { get; set; } = string.Empty;
    public double Debit { get; set; }
    public double Credit { get; set; }
    public double RunningBalance { get; set; }

    public string TransactionType { get; set; } = string.Empty; // "Borrow" or "Repayment"
    public int TransactionId { get; set; }
    public string? Remarks { get; set; }
    public string? FuelProduct { get; set; }
    public double Amount { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool CanEdit { get; set; }
    
    // Payment details
    public string? PaymentMethod { get; set; }
    public string? CardTid { get; set; }
    public string? CardBatch { get; set; }
}

public partial class DsmPersonalDebtorViewModel : ObservableObject
{
    private readonly FuelProDbContext _dbContext;
    private readonly PrintService _printService;
    private readonly ILogger _logger = Log.ForContext<DsmPersonalDebtorViewModel>();

    [ObservableProperty] private bool _isLoading;

    // Summaries directory
    [ObservableProperty] private ObservableCollection<DsmPersonalDebtorSummaryRow> _dsmSummaries = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDsmSelected))]
    private DsmPersonalDebtorSummaryRow? _selectedSummary;

    public bool IsDsmSelected => SelectedSummary != null;

    // Ledger statement
    [ObservableProperty] private ObservableCollection<DsmPersonalDebtorLedgerRow> _dsmLedger = new();
    [ObservableProperty] private DsmPersonalDebtorLedgerRow? _selectedLedgerRow;

    // Date range filters
    [ObservableProperty] private DateTime _ledgerStartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _ledgerEndDate = DateTime.Today;
    [ObservableProperty] private string _ledgerStatus = "";

    // Running totals
    [ObservableProperty] private double _ledgerTotalDebt;
    [ObservableProperty] private double _ledgerTotalRepayments;
    [ObservableProperty] private double _ledgerOutstandingBalance;

    // Repayment / Advance Form fields
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRepaymentMode))]
    [NotifyPropertyChangedFor(nameof(IsAdvanceMode))]
    private string _selectedActionType = "Repayment";
    
    public string[] ActionTypes { get; } = { "Repayment", "Advance" };
    public bool IsRepaymentMode => SelectedActionType == "Repayment";
    public bool IsAdvanceMode => SelectedActionType == "Advance";

    [ObservableProperty] private double _repaymentAmount;
    [ObservableProperty] private DateTime _repaymentDate = DateTime.Today;
    [ObservableProperty] private string _selectedPaymentMode = "Cash";
    [ObservableProperty] private string _repaymentRemarks = "";
    [ObservableProperty] private string _advanceFuelProduct = "Cash Advance";
    [ObservableProperty] private string? _cardTid = "";
    [ObservableProperty] private string? _cardBatch = "";
    [ObservableProperty] private int? _denom500;
    [ObservableProperty] private int? _denom200;
    [ObservableProperty] private int? _denom100;
    [ObservableProperty] private int? _denom50;
    [ObservableProperty] private int? _denom20;
    [ObservableProperty] private int? _denom10;
    [ObservableProperty] private int? _coins;
    
    public string[] PaymentModes { get; } = { "Cash", "PhonePe", "PineLabs Card", "PetroCard", "Bank Transfer", "Cheque" };
    public bool IsCashPaymentMode => SelectedPaymentMode == "Cash";
    public bool IsCardPaymentMode => SelectedPaymentMode.Contains("Card") || SelectedPaymentMode.Equals("PhonePe", StringComparison.OrdinalIgnoreCase);
    public bool IsChequePaymentMode => SelectedPaymentMode.Equals("Cheque", StringComparison.OrdinalIgnoreCase);

    // Edit Form fields
    [ObservableProperty] private bool _isEditingTransaction;
    [ObservableProperty] private double _editTxAmount;
    [ObservableProperty] private string _editTxRemarks = "";
    [ObservableProperty] private string _editTxFuelProduct = "";
    [ObservableProperty] private string _editTxPaymentMethod = "Cash";
    [ObservableProperty] private string _editReason = "";
    [ObservableProperty] private string _editTxStatusMessage = "";

    public bool IsOwner { get; }

    public DsmPersonalDebtorViewModel()
    {
        _dbContext = App.Services.GetRequiredService<FuelProDbContext>();
        _printService = App.Services.GetRequiredService<PrintService>();
        _excelExportService = App.Services.GetRequiredService<ExcelExportService>();

        var auth = App.Services.GetRequiredService<AuthService>();
        IsOwner = auth.CurrentUser?.IsOwner ?? false;

        _ = LoadDataAsync();
        _ = LoadKpEntriesAsync();
    }

    partial void OnSelectedSummaryChanged(DsmPersonalDebtorSummaryRow? value)
    {
        _ = LoadLedgerAsync();
    }

    partial void OnSelectedPaymentModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsCashPaymentMode));
        OnPropertyChanged(nameof(IsCardPaymentMode));
        OnPropertyChanged(nameof(IsChequePaymentMode));
        if (value == "Cash")
        {
            CardTid = "";
            CardBatch = "";
        }
        else
        {
            Denom500 = null;
            Denom200 = null;
            Denom100 = null;
            Denom50 = null;
            Denom20 = null;
            Denom10 = null;
            Coins = null;
        }
    }

    private void RecalculateCashAmount()
    {
        if (SelectedPaymentMode != "Cash") return;
        double total = 0;
        total += (Denom500 ?? 0) * 500;
        total += (Denom200 ?? 0) * 200;
        total += (Denom100 ?? 0) * 100;
        total += (Denom50 ?? 0) * 50;
        total += (Denom20 ?? 0) * 20;
        total += (Denom10 ?? 0) * 10;
        total += Coins ?? 0;
        RepaymentAmount = total;
    }

    partial void OnDenom500Changed(int? value) => RecalculateCashAmount();
    partial void OnDenom200Changed(int? value) => RecalculateCashAmount();
    partial void OnDenom100Changed(int? value) => RecalculateCashAmount();
    partial void OnDenom50Changed(int? value) => RecalculateCashAmount();
    partial void OnDenom20Changed(int? value) => RecalculateCashAmount();
    partial void OnDenom10Changed(int? value) => RecalculateCashAmount();
    partial void OnCoinsChanged(int? value) => RecalculateCashAmount();

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            var allDsms = await _dbContext.DsmUsers.Select(u => u.FullName).Distinct().ToListAsync();
            var debtorsList = await _dbContext.DsmPersonalDebtors.ToListAsync();
            var repaymentsList = await _dbContext.DsmPersonalDebtorRepayments.ToListAsync();

            var debtorsGrouped = debtorsList.GroupBy(d => d.DsmName, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var repaymentsGrouped = repaymentsList.GroupBy(r => r.DsmPersonalDebtorId).ToDictionary(g => g.Key, g => g.ToList());

            var summaryList = new List<DsmPersonalDebtorSummaryRow>();
            foreach (var dsm in allDsms)
            {
                double totalBorrowed = 0;
                double totalRepaid = 0;

                if (debtorsGrouped.TryGetValue(dsm, out var debits))
                {
                    totalBorrowed = debits.Sum(d => d.Amount);
                    var personalDebtorIds = debits.Select(d => d.Id).ToList();
                    totalRepaid = repaymentsList
                        .Where(r => personalDebtorIds.Contains(r.DsmPersonalDebtorId))
                        .Sum(r => r.Amount);
                }

                summaryList.Add(new DsmPersonalDebtorSummaryRow
                {
                    DsmName = dsm,
                    TotalBorrowed = totalBorrowed,
                    TotalRepaid = totalRepaid
                });
            }

            // Fallback for any DSM names in personal debtors not present in DsmUsers
            foreach (var dsm in debtorsGrouped.Keys)
            {
                if (!summaryList.Any(s => string.Equals(s.DsmName, dsm, StringComparison.OrdinalIgnoreCase)))
                {
                    var debits = debtorsGrouped[dsm];
                    var totalBorrowed = debits.Sum(d => d.Amount);
                    var personalDebtorIds = debits.Select(d => d.Id).ToList();
                    var totalRepaid = repaymentsList
                        .Where(r => personalDebtorIds.Contains(r.DsmPersonalDebtorId))
                        .Sum(r => r.Amount);

                    summaryList.Add(new DsmPersonalDebtorSummaryRow
                    {
                        DsmName = dsm,
                        TotalBorrowed = totalBorrowed,
                        TotalRepaid = totalRepaid
                    });
                }
            }

            var summary = summaryList.OrderBy(s => s.DsmName).ToList();

            DsmSummaries.Clear();
            foreach (var s in summary)
            {
                DsmSummaries.Add(s);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load DSM personal debtors summary");
            MessageBox.Show($"Error loading summaries: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task LoadLedgerAsync()
    {
        DsmLedger.Clear();
        SelectedLedgerRow = null;
        IsEditingTransaction = false;
        
        if (SelectedSummary == null) return;

        try
        {
            var dsmName = SelectedSummary.DsmName;

            var debits = await _dbContext.DsmPersonalDebtors
                .Where(d => d.DsmName == dsmName && d.Date.Date >= LedgerStartDate.Date && d.Date.Date <= LedgerEndDate.Date)
                .ToListAsync();

            var repayments = await _dbContext.DsmPersonalDebtorRepayments
                .Include(r => r.DsmPersonalDebtor)
                .Where(r => r.DsmPersonalDebtor != null && r.DsmPersonalDebtor.DsmName == dsmName && r.Date.Date >= LedgerStartDate.Date && r.Date.Date <= LedgerEndDate.Date)
                .ToListAsync();

            var list = new List<DsmPersonalDebtorLedgerRow>();

            foreach (var d in debits)
            {
                list.Add(new DsmPersonalDebtorLedgerRow
                {
                    TransactionId = d.Id,
                    TransactionType = "Borrow",
                    Date = d.Date,
                    Description = $"Borrowed: {d.FuelProduct} ({(string.IsNullOrEmpty(d.Remarks) ? "No remarks" : d.Remarks)})",
                    Debit = d.Amount,
                    Credit = 0,
                    FuelProduct = d.FuelProduct,
                    Remarks = d.Remarks,
                    PaymentMethod = d.PaymentMethod,
                    CardTid = d.CardTid,
                    CardBatch = d.CardBatch,
                    Amount = d.Amount,
                    CreatedAt = d.CreatedAt
                });
            }

            foreach (var c in repayments)
            {
                string tidBatchInfo = "";
                if (c.PaymentMethod == "Cheque")
                {
                    if (!string.IsNullOrWhiteSpace(c.CardTid))
                    {
                        tidBatchInfo = $" (Cheque No: {c.CardTid})";
                    }
                }
                else if (!string.IsNullOrWhiteSpace(c.CardTid) || !string.IsNullOrWhiteSpace(c.CardBatch))
                {
                    var details = new List<string>();
                    if (!string.IsNullOrWhiteSpace(c.CardTid)) details.Add($"TID: {c.CardTid}");
                    if (!string.IsNullOrWhiteSpace(c.CardBatch)) details.Add($"Batch: {c.CardBatch}");
                    tidBatchInfo = $" ({string.Join(", ", details)})";
                }

                list.Add(new DsmPersonalDebtorLedgerRow
                {
                    TransactionId = c.Id,
                    TransactionType = "Repayment",
                    Date = c.Date,
                    Description = $"Repayment via {c.PaymentMethod}{tidBatchInfo}",
                    Debit = 0,
                    Credit = c.Amount,
                    PaymentMethod = c.PaymentMethod,
                    CardTid = c.CardTid,
                    CardBatch = c.CardBatch,
                    Amount = c.Amount,
                    Remarks = c.DsmPersonalDebtor?.Remarks,
                    CreatedAt = c.CreatedAt
                });
            }

            var sorted = list.OrderBy(x => x.Date).ThenBy(x => x.TransactionType == "Borrow" ? 0 : 1).ToList();

            double running = 0;
            foreach (var item in sorted)
            {
                running += (item.Debit - item.Credit);
                item.RunningBalance = running;
                item.CanEdit = true;
            }

            foreach (var item in sorted)
            {
                DsmLedger.Add(item);
            }

            LedgerTotalDebt = sorted.Sum(x => x.Debit);
            LedgerTotalRepayments = sorted.Sum(x => x.Credit);
            LedgerOutstandingBalance = running;

            LedgerStatus = sorted.Count > 0 ? $"Loaded {sorted.Count} transactions." : "No transactions found in date range.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load DSM personal debtor ledger");
            LedgerStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void StartEditTransaction(DsmPersonalDebtorLedgerRow? row)
    {
        if (row == null) return;
        SelectedLedgerRow = row;
        EditTxAmount = row.Amount;
        EditTxRemarks = row.Remarks ?? "";
        EditTxFuelProduct = row.FuelProduct ?? "";
        EditTxPaymentMethod = row.PaymentMethod ?? "Cash";
        EditReason = "";
        EditTxStatusMessage = "";
        IsEditingTransaction = true;
    }

    [RelayCommand]
    private void CancelEditTransaction()
    {
        IsEditingTransaction = false;
        EditReason = "";
        EditTxStatusMessage = "";
    }

    [RelayCommand]
    private async Task SaveTransactionEditAsync()
    {
        if (SelectedLedgerRow == null) return;

        if (EditTxAmount <= 0)
        {
            EditTxStatusMessage = "❌ Amount must be greater than zero.";
            return;
        }

        if (IsOwner && string.IsNullOrWhiteSpace(EditReason))
        {
            EditTxStatusMessage = "❌ Owner edits require a justification reason.";
            return;
        }

        try
        {
            var auth = App.Services.GetRequiredService<AuthService>();
            var currentUser = auth.CurrentUser?.Username ?? "Unknown";
            var auditLogService = App.Services.GetRequiredService<IAuditLogService>();
            var reasonStr = IsOwner ? EditReason : "Manager edit";

            if (SelectedLedgerRow.TransactionType == "Borrow")
            {
                var entry = await _dbContext.DsmPersonalDebtors.FindAsync(SelectedLedgerRow.TransactionId);
                if (entry != null)
                {
                    double oldAmount = entry.Amount;
                    entry.Amount = EditTxAmount;
                    entry.Remarks = EditTxRemarks;
                    entry.FuelProduct = EditTxFuelProduct;
                    entry.PaymentMethod = EditTxPaymentMethod;

                    _dbContext.DsmPersonalDebtors.Update(entry);
                    await _dbContext.SaveChangesAsync();

                    await auditLogService.LogAsync(
                        tableName: "DsmPersonalDebtors",
                        recordId: entry.Id,
                        action: "Update",
                        fieldName: "Amount/Remarks/FuelProduct",
                        oldValue: oldAmount.ToString("F2"),
                        newValue: EditTxAmount.ToString("F2"),
                        modifiedBy: currentUser,
                        reason: reasonStr
                    );
                }
            }
            else if (SelectedLedgerRow.TransactionType == "Repayment")
            {
                var repayment = await _dbContext.DsmPersonalDebtorRepayments.FindAsync(SelectedLedgerRow.TransactionId);
                if (repayment != null)
                {
                    double oldAmount = repayment.Amount;
                    double diff = EditTxAmount - oldAmount;

                    var parentBorrow = await _dbContext.DsmPersonalDebtors.FindAsync(repayment.DsmPersonalDebtorId);
                    if (parentBorrow != null)
                    {
                        parentBorrow.RepaidAmount += diff;
                        _dbContext.DsmPersonalDebtors.Update(parentBorrow);
                    }

                    repayment.Amount = EditTxAmount;
                    repayment.PaymentMethod = EditTxPaymentMethod;

                    _dbContext.DsmPersonalDebtorRepayments.Update(repayment);
                    await _dbContext.SaveChangesAsync();

                    await auditLogService.LogAsync(
                        tableName: "DsmPersonalDebtorRepayments",
                        recordId: repayment.Id,
                        action: "Update",
                        fieldName: "Amount/PaymentMethod",
                        oldValue: oldAmount.ToString("F2"),
                        newValue: EditTxAmount.ToString("F2"),
                        modifiedBy: currentUser,
                        reason: reasonStr
                    );
                }
            }

            IsEditingTransaction = false;
            if (SelectedSummary != null)
            {
                await RecalculateRepaidAmountsAsync(SelectedSummary.DsmName);
            }
            await LoadLedgerAsync();
            await LoadDataAsync();

            var syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
            _ = syncEngine.ForceSyncAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save transaction edit");
            EditTxStatusMessage = $"❌ Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteTransactionAsync(DsmPersonalDebtorLedgerRow? row)
    {
        if (row == null) return;

        var confirm = MessageBox.Show(
            $"Are you sure you want to delete this {row.TransactionType} transaction of amount ₹{row.Amount:F2}?\nThis action cannot be undone.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var auth = App.Services.GetRequiredService<AuthService>();
            var currentUser = auth.CurrentUser?.Username ?? "Unknown";
            var auditLogService = App.Services.GetRequiredService<IAuditLogService>();
            var reasonStr = IsOwner ? "Deleted by Owner" : "Deleted by Manager";

            if (row.TransactionType == "Borrow")
            {
                var entry = await _dbContext.DsmPersonalDebtors.FindAsync(row.TransactionId);
                if (entry != null)
                {
                    _dbContext.DsmPersonalDebtors.Remove(entry);
                    await _dbContext.SaveChangesAsync();

                    await auditLogService.LogAsync(
                        tableName: "DsmPersonalDebtors",
                        recordId: entry.Id,
                        action: "Delete",
                        fieldName: "All",
                        oldValue: entry.Amount.ToString("F2"),
                        newValue: null,
                        modifiedBy: currentUser,
                        reason: reasonStr
                    );
                }
            }
            else if (row.TransactionType == "Repayment")
            {
                var repayment = await _dbContext.DsmPersonalDebtorRepayments.FindAsync(row.TransactionId);
                if (repayment != null)
                {
                    var parentBorrow = await _dbContext.DsmPersonalDebtors.FindAsync(repayment.DsmPersonalDebtorId);
                    if (parentBorrow != null)
                    {
                        parentBorrow.RepaidAmount = Math.Max(0, parentBorrow.RepaidAmount - repayment.Amount);
                        _dbContext.DsmPersonalDebtors.Update(parentBorrow);
                    }

                    _dbContext.DsmPersonalDebtorRepayments.Remove(repayment);
                    await _dbContext.SaveChangesAsync();

                    await auditLogService.LogAsync(
                        tableName: "DsmPersonalDebtorRepayments",
                        recordId: repayment.Id,
                        action: "Delete",
                        fieldName: "All",
                        oldValue: repayment.Amount.ToString("F2"),
                        newValue: null,
                        modifiedBy: currentUser,
                        reason: reasonStr
                    );
                }
            }

            if (SelectedSummary != null)
            {
                await RecalculateRepaidAmountsAsync(SelectedSummary.DsmName);
            }
            await LoadLedgerAsync();
            await LoadDataAsync();

            var syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
            _ = syncEngine.ForceSyncAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete transaction");
            MessageBox.Show($"❌ Delete failed: {ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RecalculateRepaidAmountsAsync(string dsmName)
    {
        var borrowings = await _dbContext.DsmPersonalDebtors
            .Where(b => b.DsmName == dsmName)
            .OrderBy(b => b.Date)
            .ThenBy(b => b.Id)
            .ToListAsync();

        if (borrowings.Count == 0) return;

        var personalDebtorIds = borrowings.Select(b => b.Id).ToList();
        var totalRepaidForDsm = await _dbContext.DsmPersonalDebtorRepayments
            .Where(r => personalDebtorIds.Contains(r.DsmPersonalDebtorId))
            .SumAsync(r => r.Amount);

        double remainingToDistribute = totalRepaidForDsm;

        foreach (var b in borrowings)
        {
            double allocated = Math.Min(b.Amount, remainingToDistribute);
            b.RepaidAmount = allocated;
            remainingToDistribute -= allocated;
        }

        if (remainingToDistribute > 0 && borrowings.Count > 0)
        {
            borrowings.Last().RepaidAmount += remainingToDistribute;
        }

        _dbContext.DsmPersonalDebtors.UpdateRange(borrowings);
        await _dbContext.SaveChangesAsync();
    }

    [RelayCommand]
    private async Task SavePersonalRepaymentAsync()
    {
        if (SelectedSummary == null)
        {
            MessageBox.Show("Please select a DSM account to record a repayment.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (RepaymentAmount <= 0)
        {
            MessageBox.Show("Please enter a valid repayment amount.", "Invalid Amount", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var dsmName = SelectedSummary.DsmName;

            var primaryBorrow = await _dbContext.DsmPersonalDebtors
                .Where(b => b.DsmName == dsmName && b.Amount > b.RepaidAmount)
                .OrderBy(b => b.Date)
                .ThenBy(b => b.Id)
                .FirstOrDefaultAsync();

            if (primaryBorrow == null)
            {
                primaryBorrow = await _dbContext.DsmPersonalDebtors
                    .Where(b => b.DsmName == dsmName)
                    .OrderByDescending(b => b.Date)
                    .ThenByDescending(b => b.Id)
                    .FirstOrDefaultAsync();
            }

            if (primaryBorrow == null)
            {
                primaryBorrow = new DsmPersonalDebtor
                {
                    DsmName = dsmName,
                    Date = RepaymentDate,
                    Time = DateTime.Now.ToString("HH:mm"),
                    Amount = 0,
                    RepaidAmount = 0,
                    Remarks = string.IsNullOrWhiteSpace(RepaymentRemarks) ? "Advance Payment" : RepaymentRemarks.Trim(),
                    PaymentMethod = SelectedPaymentMode,
                    CreatedAt = DateTime.Now
                };
                _dbContext.DsmPersonalDebtors.Add(primaryBorrow);
                await _dbContext.SaveChangesAsync();
            }

            var repayment = new DsmPersonalDebtorRepayment
            {
                DsmPersonalDebtorId = primaryBorrow.Id,
                Amount = RepaymentAmount,
                Date = RepaymentDate,
                PaymentMethod = SelectedPaymentMode,
                Source = "OwnerPayroll",
                Denom500 = Denom500 ?? 0,
                Denom200 = Denom200 ?? 0,
                Denom100 = Denom100 ?? 0,
                Denom50 = Denom50 ?? 0,
                Denom20 = Denom20 ?? 0,
                Denom10 = Denom10 ?? 0,
                Coins = Coins ?? 0,
                CardTid = CardTid ?? "",
                CardBatch = CardBatch ?? ""
            };

            _dbContext.DsmPersonalDebtorRepayments.Add(repayment);
            await _dbContext.SaveChangesAsync();

            await RecalculateRepaidAmountsAsync(dsmName);

            MessageBox.Show("Repayment recorded successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            // Reset inputs
            RepaymentAmount = 0;
            RepaymentRemarks = "";
            CardTid = "";
            CardBatch = "";
            Denom500 = null;
            Denom200 = null;
            Denom100 = null;
            Denom50 = null;
            Denom20 = null;
            Denom10 = null;
            Coins = null;

            await LoadLedgerAsync();
            await LoadDataAsync();

            var syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
            _ = syncEngine.ForceSyncAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save personal debtor repayment");
            MessageBox.Show($"Error saving repayment: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task SavePersonalAdvanceAsync()
    {
        if (SelectedSummary == null)
        {
            MessageBox.Show("Please select a DSM account to record an advance.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (RepaymentAmount <= 0)
        {
            MessageBox.Show("Please enter a valid advance amount.", "Invalid Amount", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var dsmName = SelectedSummary.DsmName;
            var entry = new DsmPersonalDebtor
            {
                DsmName = dsmName,
                Date = RepaymentDate,
                Time = DateTime.Now.ToString("HH:mm"),
                Amount = RepaymentAmount,
                FuelProduct = string.IsNullOrWhiteSpace(AdvanceFuelProduct) ? "Cash Advance" : AdvanceFuelProduct,
                Remarks = string.IsNullOrWhiteSpace(RepaymentRemarks) ? "Admin Advance Entry" : RepaymentRemarks,
                PaymentMethod = SelectedPaymentMode,
                Denom500 = Denom500 ?? 0,
                Denom200 = Denom200 ?? 0,
                Denom100 = Denom100 ?? 0,
                Denom50 = Denom50 ?? 0,
                Denom20 = Denom20 ?? 0,
                Denom10 = Denom10 ?? 0,
                Coins = Coins ?? 0,
                CardTid = CardTid,
                CardBatch = CardBatch,
                CreatedAt = DateTime.Now
            };

            _dbContext.DsmPersonalDebtors.Add(entry);
            await _dbContext.SaveChangesAsync();

            MessageBox.Show($"Advance of ₹{RepaymentAmount:N2} recorded successfully for {dsmName}.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            // Reset inputs
            RepaymentAmount = 0;
            RepaymentRemarks = "";
            AdvanceFuelProduct = "Cash Advance";
            CardTid = "";
            CardBatch = "";
            Denom500 = null;
            Denom200 = null;
            Denom100 = null;
            Denom50 = null;
            Denom20 = null;
            Denom10 = null;
            Coins = null;

            await LoadLedgerAsync();
            await LoadDataAsync();

            var syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
            _ = syncEngine.ForceSyncAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save personal debtor advance");
            MessageBox.Show($"Error saving advance: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void PrintLedger()
    {
        if (SelectedSummary == null)
        {
            MessageBox.Show("Please select a DSM account first.", "Print Ledger", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DsmLedger == null || DsmLedger.Count == 0)
        {
            MessageBox.Show("No transactions to print. Please generate the ledger first.", "Print Ledger", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var printRows = DsmLedger.Select(t => new DebtorLedgerPrintRow
            {
                Date = t.Date.ToString("dd-MMM-yyyy"),
                Description = t.Description,
                Debit = t.Debit,
                Credit = t.Credit,
                RunningBalance = t.RunningBalance
            }).ToList();

            var printData = new DebtorLedgerPrintData
            {
                DebtorName = $"{SelectedSummary.DsmName} (DSM Personal Debtors)",
                DebtorPhone = "N/A",
                StartDate = LedgerStartDate.ToString("dd-MMM-yyyy"),
                EndDate = LedgerEndDate.ToString("dd-MMM-yyyy"),
                OpeningBalance = DsmLedger.FirstOrDefault()?.RunningBalance ?? 0,
                TotalDebt = LedgerTotalDebt,
                TotalRepayments = LedgerTotalRepayments,
                ClosingBalance = LedgerOutstandingBalance,
                Transactions = printRows
            };

            _printService.PrintDebtorLedger(printData);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to print DSM personal debtor ledger");
            MessageBox.Show($"Failed to print ledger: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Khandhare Petroleum Drawings Tab
    private readonly ExcelExportService _excelExportService;

    [ObservableProperty] private DateTime _kpStartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _kpEndDate = DateTime.Today;
    [ObservableProperty] private string _kpSearchText = "";
    [ObservableProperty] private double _kpTotalAmount;
    public ObservableCollection<KhandharePetroleumEntry> KpEntries { get; } = new();

    private List<KhandharePetroleumEntry> _allLoadedKpEntries = new();

    partial void OnKpStartDateChanged(DateTime value) => _ = LoadKpEntriesAsync();
    partial void OnKpEndDateChanged(DateTime value) => _ = LoadKpEntriesAsync();
    partial void OnKpSearchTextChanged(string value) => ApplyKpFilter();

    [RelayCommand]
    public async Task LoadKpEntriesAsync()
    {
        try
        {
            var entries = await _dbContext.KhandharePetroleumEntries
                .Where(kp => kp.Date.Date >= KpStartDate.Date && kp.Date.Date <= KpEndDate.Date)
                .OrderByDescending(kp => kp.Date)
                .ToListAsync();

            _allLoadedKpEntries = entries;
            ApplyKpFilter();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load Khandhare Petroleum entries");
        }
    }

    private void ApplyKpFilter()
    {
        KpEntries.Clear();
        var query = _allLoadedKpEntries.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(KpSearchText))
        {
            query = query.Where(kp => kp.Name.Contains(KpSearchText, StringComparison.OrdinalIgnoreCase) ||
                                     kp.DsmName.Contains(KpSearchText, StringComparison.OrdinalIgnoreCase) ||
                                     kp.SlipNumber.Contains(KpSearchText, StringComparison.OrdinalIgnoreCase) ||
                                     (kp.VehicleNumber != null && kp.VehicleNumber.Contains(KpSearchText, StringComparison.OrdinalIgnoreCase)));
        }

        var filtered = query.ToList();
        foreach (var kp in filtered)
        {
            KpEntries.Add(kp);
        }

        KpTotalAmount = filtered.Sum(kp => kp.Amount);
    }

    [RelayCommand]
    private void PrintKpLedger()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Kandhare Petroleum", Value = "₹" + KpTotalAmount.ToString("N2"), Highlight = true }
            };

            var headers = new List<string> { "Date", "Logged By", "Person Name", "Slip No", "Amount" };
            var rows = new List<List<string>>();

            foreach (var row in KpEntries)
            {
                rows.Add(new List<string>
                {
                    row.Date.ToString("dd-MMM-yyyy"),
                    row.DsmName,
                    row.Name,
                    row.SlipNumber,
                    "₹" + row.Amount.ToString("N2")
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Kandhare Petroleum Report",
                Subtitle = $"Date Range: {KpStartDate:dd-MMM-yyyy} to {KpEndDate:dd-MMM-yyyy}" + 
                           (string.IsNullOrWhiteSpace(KpSearchText) ? "" : $" (Filtered by: '{KpSearchText}')"),
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            _printService.PrintGenericGrid(printData);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to print Kandhare Petroleum report");
            MessageBox.Show($"Print failed: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ExportKpExcelAsync()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Kandhare Petroleum", Value = "₹" + KpTotalAmount.ToString("N2"), Highlight = true }
            };

            var headers = new List<string> { "Date", "Logged By", "Person Name", "Slip No", "Amount" };
            var rows = new List<List<string>>();

            foreach (var row in KpEntries)
            {
                rows.Add(new List<string>
                {
                    row.Date.ToString("dd-MMM-yyyy"),
                    row.DsmName,
                    row.Name,
                    row.SlipNumber,
                    "₹" + row.Amount.ToString("N2")
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Kandhare Petroleum Report",
                Subtitle = $"Date Range: {KpStartDate:dd-MMM-yyyy} to {KpEndDate:dd-MMM-yyyy}" + 
                           (string.IsNullOrWhiteSpace(KpSearchText) ? "" : $" (Filtered by: '{KpSearchText}')"),
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var path = await _excelExportService.ExportGenericGridAsync(printData, "KandharePetroleum");
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export Kandhare Petroleum to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
