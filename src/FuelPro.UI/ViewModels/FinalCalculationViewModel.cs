using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using Serilog;

using FuelPro.Core.Models.AGS;

namespace FuelPro.UI.ViewModels;

public partial class FinalCalculationViewModel : ObservableObject
{
    private readonly IShiftRepository _shiftRepo;
    private readonly IDsmEntryRepository _dsmRepo;
    private readonly IExpenseRepository _expenseRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IShiftOtherCashRepository _otherCashRepo;
    private readonly IShiftFuelRateRepository _fuelRateRepo;
    private readonly IShiftAggregationService _aggregation;
    private readonly ICreditorRepository _creditorRepo;
    private readonly ICreditorRepaymentRepository _repaymentRepo;
    private readonly ITidCalculationService _tidService;
    private readonly ILogger _logger = Log.ForContext<FinalCalculationViewModel>();

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private string _selectedShift = "A";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isShiftLocked;
    [ObservableProperty] private int _shiftId;
    [ObservableProperty] private ObservableCollection<NozzleGroupDto> _nozzleGroups = new();

    // TABLE A
    [ObservableProperty] private ObservableCollection<DsmSummaryRowDto> _dsmSummaryRows = new();
    [ObservableProperty] private DsmSummaryRowDto? _dsmSummaryTotals;

    // TABLE B
    [ObservableProperty] private ObservableCollection<CashDenomDisplayRow> _cash1Rows = new();
    [ObservableProperty] private double _cash1Total;
    [ObservableProperty] private ObservableCollection<CashDenomDisplayRow> _cash2Rows = new();
    [ObservableProperty] private double _cash2Total;

    // TABLE C
    [ObservableProperty] private ObservableCollection<DebitRegisterRowDto> _creditorRows = new();
    [ObservableProperty] private double _creditorsTotal;

    // TABLE D
    [ObservableProperty] private ObservableCollection<ExpenseRegisterRowDto> _expenseRows = new();
    [ObservableProperty] private double _expensesTotal;
    [ObservableProperty] private string _newExpenseDescription = "";
    [ObservableProperty] private double _newExpenseAmount;

    // TABLE E
    [ObservableProperty] private double _hsdLitres;
    [ObservableProperty] private double _hsdRate;
    [ObservableProperty] private double _hsdAmount;
    [ObservableProperty] private double _msILitres;
    [ObservableProperty] private double _msIRate;
    [ObservableProperty] private double _msIAmount;
    [ObservableProperty] private double _msIILitres;
    [ObservableProperty] private double _msIIRate;
    [ObservableProperty] private double _msIIAmount;
    [ObservableProperty] private double _cngLitres;
    [ObservableProperty] private double _cngRate;
    [ObservableProperty] private double _cngAmount;

    // MS Totals
    [ObservableProperty] private double _msTotalLitres;
    [ObservableProperty] private double _msTotalAmount;

    [ObservableProperty] private double _totalLitres;
    [ObservableProperty] private double _totalFuelSaleAmount;
    [ObservableProperty] private ObservableCollection<OtherCashRowDto> _otherCashRows = new();
    [ObservableProperty] private double _otherCashTotal;
    [ObservableProperty] private double _grandTotalSaleAmount;
    [ObservableProperty] private string _newOtherCashDescription = "";
    [ObservableProperty] private double _newOtherCashAmount;

    // TABLE F
    [ObservableProperty] private ObservableCollection<ReconciliationRowDto> _reconciliationRows = new();
    [ObservableProperty] private double _reconciliationTotal;
    [ObservableProperty] private double _grossSaleTotal;
    [ObservableProperty] private double _difference;
    [ObservableProperty] private bool _isBalanced;
    [ObservableProperty] private bool _includeOtherCashInGrossSale;
    [ObservableProperty] private double _totalDsmShort;

    // DEBTOR REPAYMENTS (Part 5)
    [ObservableProperty] private ObservableCollection<CreditorRepayment> _debtorRepayments = new();
    [ObservableProperty] private ObservableCollection<Creditor> _debtorsList = new();
    [ObservableProperty] private string _newDebtorName = "";
    [ObservableProperty] private string _newRepaymentMode = "Cash";
    [ObservableProperty] private double _newRepaymentAmount;
    [ObservableProperty] private string? _newChequeNumber = "";
    [ObservableProperty] private string _repaymentStatusMessage = "";
    [ObservableProperty] private string? _newCardTid = "";
    [ObservableProperty] private string? _newCardBatch = "";
    [ObservableProperty] private int? _newDenom500;
    [ObservableProperty] private int? _newDenom200;
    [ObservableProperty] private int? _newDenom100;
    [ObservableProperty] private int? _newDenom50;
    [ObservableProperty] private int? _newDenom20;
    [ObservableProperty] private int? _newDenom10;
    [ObservableProperty] private int? _newCoins;
    public string[] PaymentModes { get; } = { "Cash", "PhonePe", "Credit Card", "Cheque" };

    public string[] ShiftOptions { get; } = { "A", "B" };

    private List<DsmEntry> _loadedEntries = new();
    public IReadOnlyList<DsmEntry> LoadedEntries => _loadedEntries;

    public FinalCalculationViewModel()
    {
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _dsmRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();
        _otherCashRepo = App.Services.GetRequiredService<IShiftOtherCashRepository>();
        _fuelRateRepo = App.Services.GetRequiredService<IShiftFuelRateRepository>();
        _aggregation = App.Services.GetRequiredService<IShiftAggregationService>();
        _creditorRepo = App.Services.GetRequiredService<ICreditorRepository>();
        _repaymentRepo = App.Services.GetRequiredService<ICreditorRepaymentRepository>();
        _tidService = App.Services.GetRequiredService<ITidCalculationService>();
    }

    partial void OnSelectedDateChanged(DateTime value) => _ = LoadShiftDataAsync();
    partial void OnSelectedShiftChanged(string value) => _ = LoadShiftDataAsync();
    partial void OnIncludeOtherCashInGrossSaleChanged(bool value) => RecalcReconciliation();

    [RelayCommand]
    private async Task LoadShiftDataAsync()
    {
        IsLoading = true;
        StatusMessage = "";
        HasData = false;
        try
        {
            var shiftResult = await _shiftRepo.GetShiftAsync(SelectedDate, SelectedShift);
            if (!shiftResult.Success || shiftResult.Data == null)
            {
                StatusMessage = "No DSM entries found for this date and shift.";
                ClearAll();
                return;
            }

            var shift = shiftResult.Data;
            ShiftId = shift.ShiftId;
            IsShiftLocked = shift.IsLocked;

            var entriesResult = await _dsmRepo.GetEntriesForShiftAsync(shift.ShiftId);
            if (!entriesResult.Success || entriesResult.Data == null || entriesResult.Data.Count == 0)
            {
                StatusMessage = "No DSM entries found for this date and shift.";
                ClearAll();
                return;
            }

            _loadedEntries = entriesResult.Data;

            // TABLE A
            var summaryRows = _aggregation.BuildDsmSummaryRows(_loadedEntries);
            DsmSummaryRows = new ObservableCollection<DsmSummaryRowDto>(summaryRows);
            DsmSummaryTotals = _aggregation.BuildDsmSummaryTotalRow(summaryRows);

            // TABLE B
            var cash1Agg = _aggregation.AggregateCash(_loadedEntries, "Cash1");
            Cash1Rows = new ObservableCollection<CashDenomDisplayRow>(cash1Agg.ToDisplayRows());
            Cash1Total = cash1Agg.GrandTotal;

            var cash2Agg = _aggregation.AggregateCash(_loadedEntries, "Cash2");
            Cash2Rows = new ObservableCollection<CashDenomDisplayRow>(cash2Agg.ToDisplayRows());
            Cash2Total = cash2Agg.GrandTotal;

            // TABLE C
            var creditors = _aggregation.BuildCreditorRows(_loadedEntries);
            CreditorRows = new ObservableCollection<DebitRegisterRowDto>(creditors);
            CreditorsTotal = creditors.Sum(r => r.Amount);

            // TABLE D
            var shiftExpensesResult = await _expenseRepo.GetByShiftIdAsync(shift.ShiftId);
            var shiftExpenses = shiftExpensesResult.Success ? shiftExpensesResult.Data! : new List<Expense>();
            var expenseRows = _aggregation.BuildExpenseRows(_loadedEntries, shiftExpenses);
            ExpenseRows = new ObservableCollection<ExpenseRegisterRowDto>(expenseRows);
            ExpensesTotal = expenseRows.Sum(r => r.Amount);

            // TABLE E — Fuel
            var rateOverrides = await _fuelRateRepo.GetByShiftAsync(SelectedDate, SelectedShift);
            var rateDict = rateOverrides.Success
                ? rateOverrides.Data!.ToDictionary(r => r.FuelType, r => r.OverrideRate)
                : new Dictionary<string, double>();

            var settings = await _settingsRepo.GetSettingsAsync();
            double defaultHsd = settings.Success ? settings.Data!.HsdRate : 90.35;
            double defaultMsI = settings.Success ? settings.Data!.MsIRate : 103.81;
            double defaultMsII = settings.Success ? settings.Data!.MsIIRate : 103.81;
            double defaultCng = settings.Success ? settings.Data!.CngRate : 85.0;

            var (hsdL, hsdA) = _aggregation.GetFuelTotals(_loadedEntries, "HSD",
                rateDict.TryGetValue("HSD", out var hr) ? hr : null);
            HsdLitres = hsdL;
            HsdRate = rateDict.TryGetValue("HSD", out var hrr) ? hrr : defaultHsd;
            HsdAmount = hsdA;

            var (msIL, msIA) = _aggregation.GetFuelTotals(_loadedEntries, "MS-I",
                rateDict.TryGetValue("MS-I", out var mr1) ? mr1 : null);
            MsILitres = msIL;
            MsIRate = rateDict.TryGetValue("MS-I", out var mr1r) ? mr1r : defaultMsI;
            MsIAmount = msIA;

            var (msIIL, msIIA) = _aggregation.GetFuelTotals(_loadedEntries, "MS-II",
                rateDict.TryGetValue("MS-II", out var mr2) ? mr2 : null);
            MsIILitres = msIIL;
            MsIIRate = rateDict.TryGetValue("MS-II", out var mr2r) ? mr2r : defaultMsII;
            MsIIAmount = msIIA;

            var (cngL, cngA) = _aggregation.GetFuelTotals(_loadedEntries, "CNG",
                rateDict.TryGetValue("CNG", out var cr) ? cr : null);
            CngLitres = cngL;
            CngRate = rateDict.TryGetValue("CNG", out var crr) ? crr : defaultCng;
            CngAmount = cngA;

            MsTotalLitres = MsILitres + MsIILitres;
            MsTotalAmount = MsIAmount + MsIIAmount;

            TotalLitres = HsdLitres + MsILitres + MsIILitres + CngLitres;
            TotalFuelSaleAmount = HsdAmount + MsIAmount + MsIIAmount + CngAmount;

            // TABLE E — Other Cash
            var otherCashResult = await _otherCashRepo.GetByShiftAsync(SelectedDate, SelectedShift);
            var otherCash = otherCashResult.Success ? otherCashResult.Data! : new List<ShiftOtherCash>();
            OtherCashRows = new ObservableCollection<OtherCashRowDto>(
                otherCash.Select(o => new OtherCashRowDto
                {
                    ShiftOtherCashId = o.ShiftOtherCashId,
                    Description = o.Description,
                    Amount = o.Amount,
                    IsEditable = o.IsEditable
                }));
            OtherCashTotal = 0;
            GrandTotalSaleAmount = TotalFuelSaleAmount;

            // TABLE F — Reconciliation
            RecalcReconciliation();

            await LoadDebtorRepaymentsAsync();

            // Load AGS Nozzle Readings
            try
            {
                var agsRepo = App.Services.GetRequiredService<IAgsImportRepository>();
                var agsImportResult = await agsRepo.GetActiveShiftImportAsync(SelectedDate, SelectedShift);
                var agsImport = (agsImportResult.Success && agsImportResult.Data != null) ? agsImportResult.Data : null;
                var nozzleGroupsList = BuildNozzleGroups(agsImport);
                NozzleGroups = new ObservableCollection<NozzleGroupDto>(nozzleGroupsList);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to load AGS nozzle groups for calculation");
                NozzleGroups = new ObservableCollection<NozzleGroupDto>(BuildNozzleGroups(null));
            }

            HasData = true;
            StatusMessage = IsShiftLocked ? "🔒 SHIFT LOCKED" : "";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load shift data");
            StatusMessage = $"Error loading data: {ex.Message}";
        }
        finally { IsLoading = false; }
    }

    private void RecalcReconciliation()
    {
        var msTesting = _loadedEntries.SelectMany(e => e.TestingEntries).Where(t => t.FuelType == "MS").Sum(t => t.Amount);
        var hsdTesting = _loadedEntries.SelectMany(e => e.TestingEntries).Where(t => t.FuelType == "HSD" || t.FuelType == "HSD-I").Sum(t => t.Amount);
        var hsdTesting2 = _loadedEntries.SelectMany(e => e.TestingEntries).Where(t => t.FuelType == "HSD-II").Sum(t => t.Amount);
        var cngTesting = _loadedEntries.SelectMany(e => e.TestingEntries).Where(t => t.FuelType == "CNG").Sum(t => t.Amount);
        
        var tidSheetToday = _tidService.GetTidSheetAsync(SelectedDate.Date).GetAwaiter().GetResult();
        var tidSheetTomorrow = _tidService.GetTidSheetAsync(SelectedDate.Date.AddDays(1)).GetAwaiter().GetResult();

        double phonePeCardMorning = 0;
        double phonePeCardNight = 0;
        double phonePeMorning = 0;
        double phonePeNight = 0;
        double petroCard = 0;
        double creditCardMorning = 0;
        double creditCardNight = 0;

        if (SelectedShift == "A")
        {
            phonePeCardMorning = tidSheetToday.PhonePeCardDay;
            phonePeCardNight = 0;
            phonePeMorning = tidSheetToday.PhonePeDirectDay;
            phonePeNight = 0;
            petroCard = tidSheetToday.PetroCardDay;
            creditCardMorning = tidSheetToday.PineLabsCardDay;
            creditCardNight = 0;
        }
        else
        {
            phonePeCardMorning = tidSheetTomorrow.PhonePeCardMorning;
            phonePeCardNight = tidSheetToday.PhonePeCardNight;
            phonePeMorning = tidSheetTomorrow.PhonePeDirectMorning;
            phonePeNight = tidSheetToday.PhonePeDirectNight;
            petroCard = tidSheetTomorrow.PetroCardMorning + tidSheetToday.PetroCardNight;
            creditCardMorning = tidSheetTomorrow.PineLabsCardMorning;
            creditCardNight = tidSheetToday.PineLabsCardNight;
        }

        var debit = CreditorsTotal;

        // Repayments for this shift (already filtered by SelectedShift in LoadDebtorRepaymentsAsync)
        double cashRepayments = 0;
        double phonePeRepayments = 0;
        double creditCardRepayments = 0;
        double petroCardRepayments = 0;
        double bankCashRepayments = 0;

        foreach (var r in DebtorRepayments)
        {
            string mode = r.PaymentMode ?? "";
            if (string.Equals(mode, "Cash", StringComparison.OrdinalIgnoreCase))
            {
                cashRepayments += r.Amount;
            }
            else if (string.Equals(mode, "PhonePe", StringComparison.OrdinalIgnoreCase) || 
                     string.Equals(mode, "PhonePe UPI", StringComparison.OrdinalIgnoreCase))
            {
                phonePeRepayments += r.Amount;
            }
            else if (string.Equals(mode, "Credit Card", StringComparison.OrdinalIgnoreCase) || 
                     string.Equals(mode, "PineLabs Card", StringComparison.OrdinalIgnoreCase) || 
                     string.Equals(mode, "PineLab Card", StringComparison.OrdinalIgnoreCase))
            {
                creditCardRepayments += r.Amount;
            }
            else if (string.Equals(mode, "PetroCard", StringComparison.OrdinalIgnoreCase) || 
                     string.Equals(mode, "Petro Card", StringComparison.OrdinalIgnoreCase))
            {
                petroCardRepayments += r.Amount;
            }
            else if (string.Equals(mode, "Bank Transfer", StringComparison.OrdinalIgnoreCase))
            {
                bankCashRepayments += r.Amount;
            }
        }

        // Silent addition to respective fields
        double displayCashInHand = Cash2Total + cashRepayments;
        double displayBankCash = Cash1Total + bankCashRepayments;
        double displayPetroCard = petroCard + petroCardRepayments;
        
        double displayPhonePeMorning = phonePeMorning;
        double displayPhonePeNight = phonePeNight;
        if (SelectedShift == "A") displayPhonePeMorning += phonePeRepayments;
        else displayPhonePeNight += phonePeRepayments;

        double displayCreditCardMorning = creditCardMorning;
        double displayCreditCardNight = creditCardNight;
        if (SelectedShift == "A") displayCreditCardMorning += creditCardRepayments;
        else displayCreditCardNight += creditCardRepayments;

        // Build reconciliation rows
        var reconRows = new List<ReconciliationRowDto>();
        reconRows.Add(new ReconciliationRowDto { Description = "MS Testing", Amount = msTesting });
        reconRows.Add(new ReconciliationRowDto { Description = "HSD Testing", Amount = hsdTesting });
        reconRows.Add(new ReconciliationRowDto { Description = "CNG Testing", Amount = cngTesting });
        
        reconRows.Add(new ReconciliationRowDto { Description = "Phone Pe Card (Morning)", Amount = phonePeCardMorning });
        reconRows.Add(new ReconciliationRowDto { Description = "Phone Pe Card (Night)", Amount = phonePeCardNight });
        
        string ppMorningDesc = SelectedShift == "A" && phonePeRepayments > 0 
            ? $"Phone Pe (Morning) (Repayment: ₹{phonePeRepayments:F0})" 
            : "Phone Pe (Morning)";
        reconRows.Add(new ReconciliationRowDto { Description = ppMorningDesc, Amount = displayPhonePeMorning });
        
        string ppNightDesc = SelectedShift == "B" && phonePeRepayments > 0 
            ? $"Phone Pe (Night) (Repayment: ₹{phonePeRepayments:F0})" 
            : "Phone Pe (Night)";
        reconRows.Add(new ReconciliationRowDto { Description = ppNightDesc, Amount = displayPhonePeNight });
        
        string petroDesc = petroCardRepayments > 0 
            ? $"P. Card (Repayment: ₹{petroCardRepayments:F0})" 
            : "P. Card";
        reconRows.Add(new ReconciliationRowDto { Description = petroDesc, Amount = displayPetroCard });
        
        reconRows.Add(new ReconciliationRowDto { Description = "Debit", Amount = debit });
        
        string ccMorningDesc = SelectedShift == "A" && creditCardRepayments > 0 
            ? $"PineLab Card (Morning) (Repayment: ₹{creditCardRepayments:F0})" 
            : "PineLab Card (Morning)";
        reconRows.Add(new ReconciliationRowDto { Description = ccMorningDesc, Amount = displayCreditCardMorning });
        
        string ccNightDesc = SelectedShift == "B" && creditCardRepayments > 0 
            ? $"PineLab Card (Night) (Repayment: ₹{creditCardRepayments:F0})" 
            : "PineLab Card (Night)";
        reconRows.Add(new ReconciliationRowDto { Description = ccNightDesc, Amount = displayCreditCardNight });
        
        string bankCashDesc = bankCashRepayments > 0 
            ? $"Bank Cash (Repayment: ₹{bankCashRepayments:F0})" 
            : "Bank Cash";
        reconRows.Add(new ReconciliationRowDto { Description = bankCashDesc, Amount = displayBankCash });
        
        string cashInHandDesc = cashRepayments > 0 
            ? $"Cash In Hand (Repayment: ₹{cashRepayments:F0})" 
            : "Cash In Hand";
        reconRows.Add(new ReconciliationRowDto { Description = cashInHandDesc, Amount = displayCashInHand });
        
        reconRows.Add(new ReconciliationRowDto { Description = "Expenses", Amount = ExpensesTotal });

        double totalDsmShort = 0;
        var mismatchGroups = _loadedEntries.GroupBy(e => new { e.ShiftId, e.DsmName, GroupPumpId = e.ReconciledToPumpId ?? e.PumpId });
        foreach (var g in mismatchGroups)
        {
            var grossSales = g.SelectMany(e => e.NozzleReadings).Sum(n => n.Amount);
            var cash1Total = g.SelectMany(e => e.CashDenominations).Where(c => c.CashType == "Cash1").Sum(c => c.TotalAmount);
            var cash2Total = g.SelectMany(e => e.CashDenominations).Where(c => c.CashType == "Cash2").Sum(c => c.TotalAmount);
            var physicalCash = cash1Total + cash2Total;
            
            var phonePe = g.Sum(e => (e.PaymentCollection?.PhonePeMorning ?? 0) 
                                   + (e.PaymentCollection?.PhonePeNight ?? 0) 
                                   + (e.PaymentCollection?.PhonePeCardMorning ?? 0) 
                                   + (e.PaymentCollection?.PhonePeCardNight ?? 0));
                                   
            var creditCard = g.Sum(e => (e.PaymentCollection?.CreditCardMorning ?? 0) 
                                       + (e.PaymentCollection?.CreditCardNight ?? 0) 
                                       + (e.PaymentCollection?.PetroCardMorning ?? 0) 
                                       + (e.PaymentCollection?.PetroCardNight ?? 0));
                                       
            var cashDeposit = g.Sum(e => e.PaymentCollection?.CashDeposit ?? 0);
            var totalInDirect = (double)(phonePe + creditCard + cashDeposit + physicalCash);
            
            var totalCreditors = g.SelectMany(e => e.DebitEntries).Sum(d => d.Amount);
            var totalTesting = g.SelectMany(e => e.TestingEntries).Sum(t => t.Amount);
            var totalExpenses = g.SelectMany(e => e.Expenses).Sum(ex => ex.Amount);
            
            var totalCollection = totalInDirect + totalCreditors + totalTesting + totalExpenses;
            var mismatch = totalCollection - grossSales;
            
            if (mismatch < -0.01)
            {
                totalDsmShort += Math.Abs(mismatch);
            }
        }
        TotalDsmShort = totalDsmShort;

        if (TotalDsmShort > 0.01)
        {
            reconRows.Add(new ReconciliationRowDto { Description = "Total DSM Short", Amount = TotalDsmShort });
        }
        
        ReconciliationRows = new ObservableCollection<ReconciliationRowDto>(reconRows);
        ReconciliationTotal = reconRows.Sum(r => r.Amount);

        // Adjust GrossSaleTotal to include repayments (excluding cheque) so they balance
        double repaymentsReconciled = cashRepayments + phonePeRepayments + creditCardRepayments + petroCardRepayments + bankCashRepayments;
        GrossSaleTotal = TotalFuelSaleAmount + repaymentsReconciled;

        Difference = ReconciliationTotal - GrossSaleTotal;
        IsBalanced = Math.Abs(Difference) < 0.01;
    }

    private void ClearAll()
    {
        _loadedEntries.Clear();
        DsmSummaryRows.Clear();
        DsmSummaryTotals = null;
        Cash1Rows.Clear(); Cash1Total = 0;
        Cash2Rows.Clear(); Cash2Total = 0;
        CreditorRows.Clear(); CreditorsTotal = 0;
        ExpenseRows.Clear(); ExpensesTotal = 0;
        OtherCashRows.Clear(); OtherCashTotal = 0;
        ReconciliationRows.Clear(); ReconciliationTotal = 0;
        HsdLitres = HsdRate = HsdAmount = 0;
        MsILitres = MsIRate = MsIAmount = 0;
        MsIILitres = MsIIRate = MsIIAmount = 0;
        CngLitres = CngRate = CngAmount = 0;
        TotalLitres = TotalFuelSaleAmount = MsTotalLitres = MsTotalAmount = 0;
        GrandTotalSaleAmount = GrossSaleTotal = Difference = TotalDsmShort = 0;
        IsBalanced = false;
        IsShiftLocked = false;
        DebtorRepayments.Clear();
        DebtorsList.Clear();
        NewDebtorName = "";
        NewRepaymentMode = "Cash";
        NewRepaymentAmount = 0;
        NewChequeNumber = "";
        RepaymentStatusMessage = "";
        NewCardTid = "";
        NewCardBatch = "";
        NewDenom500 = null;
        NewDenom200 = null;
        NewDenom100 = null;
        NewDenom50 = null;
        NewDenom20 = null;
        NewDenom10 = null;
        NewCoins = null;
    }

    [RelayCommand]
    private async Task AddShiftExpenseAsync()
    {
        if (IsShiftLocked) { StatusMessage = "❌ Shift is locked."; return; }
        if (string.IsNullOrWhiteSpace(NewExpenseDescription)) return;
        var result = await _expenseRepo.AddShiftExpenseAsync(ShiftId, NewExpenseDescription, NewExpenseAmount);
        if (result.Success && result.Data != null) 
        { 
            try
            {
                var db = App.Services.GetRequiredService<FuelPro.Data.FuelProDbContext>();
                var pTx = new PettyCashTransaction
                {
                    Date = SelectedDate,
                    Description = $"Shift Expense: {result.Data.Description}",
                    Amount = -result.Data.Amount,
                    Type = "Deduction",
                    ShiftExpenseId = result.Data.ExpenseId,
                    CreatedAt = DateTime.Now
                };
                db.PettyCashTransactions.Add(pTx);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create petty cash deduction for expense");
            }

            NewExpenseDescription = ""; 
            NewExpenseAmount = 0; 
            await LoadShiftDataAsync(); 
        }
        else StatusMessage = $"❌ {result.Error}";
    }

    [RelayCommand]
    private async Task RemoveShiftExpenseAsync(int expenseId)
    {
        if (IsShiftLocked) { StatusMessage = "❌ Shift is locked."; return; }
        
        try
        {
            var db = App.Services.GetRequiredService<FuelPro.Data.FuelProDbContext>();
            var pTx = await db.PettyCashTransactions.FirstOrDefaultAsync(t => t.ShiftExpenseId == expenseId);
            if (pTx != null)
            {
                db.PettyCashTransactions.Remove(pTx);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to remove matching petty cash transaction for expense {ExpenseId}", expenseId);
        }

        var result = await _expenseRepo.DeleteExpenseAsync(expenseId);
        if (result.Success) await LoadShiftDataAsync();
        else StatusMessage = $"❌ {result.Error}";
    }

    [RelayCommand]
    private async Task AddOtherCashRowAsync()
    {
        if (IsShiftLocked) { StatusMessage = "❌ Shift is locked."; return; }
        if (string.IsNullOrWhiteSpace(NewOtherCashDescription)) return;
        var entry = new ShiftOtherCash
        {
            ShiftId = ShiftId > 0 ? ShiftId : null,
            ShiftDate = SelectedDate.Date,
            ShiftNumber = SelectedShift,
            Description = NewOtherCashDescription,
            Amount = NewOtherCashAmount,
            IsEditable = true
        };
        var result = await _otherCashRepo.AddAsync(entry);
        if (result.Success) { NewOtherCashDescription = ""; NewOtherCashAmount = 0; await LoadShiftDataAsync(); }
        else StatusMessage = $"❌ {result.Error}";
    }

    [RelayCommand]
    private async Task RemoveOtherCashRowAsync(int id)
    {
        if (IsShiftLocked) { StatusMessage = "❌ Shift is locked."; return; }
        var result = await _otherCashRepo.DeleteAsync(id);
        if (result.Success) await LoadShiftDataAsync();
        else StatusMessage = $"❌ {result.Error}";
    }

    [RelayCommand]
    private async Task LockShiftAsync()
    {
        if (ShiftId == 0) return;
        var shiftLabel = SelectedShift switch { "A" => "I", "B" => "II", "C" => "III", _ => SelectedShift };
        var confirm = MessageBox.Show(
            $"Lock Shift {shiftLabel} for {SelectedDate:dd MMM yyyy}?\nThis cannot be undone.",
            "Confirm Lock", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        var result = await _shiftRepo.LockShiftAsync(ShiftId);
        if (result.Success) { StatusMessage = "✅ Shift locked successfully!"; await LoadShiftDataAsync(); }
        else StatusMessage = $"❌ {result.Error}";
    }

    [RelayCommand]
    private void ExportCsv()
    {
        if (!HasData) return;
        try
        {
            var shiftLabel = SelectedShift switch { "A" => "1", "B" => "2", "C" => "3", _ => SelectedShift };
            var dateStr = SelectedDate.ToString("yyyyMMdd");
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "ZIP files (*.zip)|*.zip",
                FileName = $"ShiftReport_{dateStr}_Shift{shiftLabel}.zip"
            };
            if (dialog.ShowDialog() != true) return;

            using var zipStream = new FileStream(dialog.FileName, FileMode.Create);
            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create);

            WriteCsvEntry(zip, $"DSM_Summary_{dateStr}_Shift{shiftLabel}.csv", w =>
            {
                w.WriteLine("DSM Name,Pump No.,P.Pe Card (Morning),P.Pe Card (Night),Phone Pe,PineLab Card (Morning),PineLab Card (Night),Petro Card,Bank Cash,Debit,Expenses,Testing,Cash In Hand,Gross Sale");
                foreach (var r in DsmSummaryRows)
                    w.WriteLine($"{r.DsmName},{r.PumpId},{r.PhonePeCardMorning:F2},{r.PhonePeCardNight:F2},{r.PhonePe:F2},{r.CreditCardMorning:F2},{r.CreditCardNight:F2},{r.PetroCard:F2},{r.CashDeposit:F2},{r.Debit:F2},{r.Expenses:F2},{r.Testing:F2},{r.CashInHand:F2},{r.GrossSales:F2}");
                if (DsmSummaryTotals != null)
                    w.WriteLine($"TOTAL,,{DsmSummaryTotals.PhonePeCardMorning:F2},{DsmSummaryTotals.PhonePeCardNight:F2},{DsmSummaryTotals.PhonePe:F2},{DsmSummaryTotals.CreditCardMorning:F2},{DsmSummaryTotals.CreditCardNight:F2},{DsmSummaryTotals.PetroCard:F2},{DsmSummaryTotals.CashDeposit:F2},{DsmSummaryTotals.Debit:F2},{DsmSummaryTotals.Expenses:F2},{DsmSummaryTotals.Testing:F2},{DsmSummaryTotals.CashInHand:F2},{DsmSummaryTotals.GrossSales:F2}");
            });

            WriteCsvEntry(zip, $"Cash_Details_{dateStr}_Shift{shiftLabel}.csv", w =>
            {
                w.WriteLine("Type,Amount");
                w.WriteLine($"Bank Deposit,{Cash1Total:F2}");
                w.WriteLine($"Cash In Hand,{Cash2Total:F2}");
            });

            WriteCsvEntry(zip, $"Debtors_{dateStr}_Shift{shiftLabel}.csv", w =>
            {
                w.WriteLine("DSM Name,Pump No.,Name,Cheque No.,Amount");
                foreach (var r in CreditorRows) w.WriteLine($"{r.DsmName},{r.PumpId},{r.DebtorName},{r.ChequeNo},{r.Amount:F2}");
                w.WriteLine($"TOTAL,,,,{CreditorsTotal:F2}");
            });

            WriteCsvEntry(zip, $"Expenses_{dateStr}_Shift{shiftLabel}.csv", w =>
            {
                w.WriteLine("DSM Name,Pump No.,Description,Amount");
                foreach (var r in ExpenseRows) w.WriteLine($"{r.DsmName},{r.PumpId},{r.Description},{r.Amount:F2}");
                w.WriteLine($"TOTAL,,,{ExpensesTotal:F2}");
            });

            WriteCsvEntry(zip, $"Fuel_Sale_{dateStr}_Shift{shiftLabel}.csv", w =>
            {
                w.WriteLine("Description,Litres,Rate,Amount");
                w.WriteLine($"HSD,{HsdLitres:F4},{HsdRate:F2},{HsdAmount:F2}");
                w.WriteLine($"MS-I,{MsILitres:F4},{MsIRate:F2},{MsIAmount:F2}");
                w.WriteLine($"MS-II,{MsIILitres:F4},{MsIIRate:F2},{MsIIAmount:F2}");
                foreach (var r in OtherCashRows) w.WriteLine($"{r.Description},,,{r.Amount:F2}");
                w.WriteLine($"TOTAL,{TotalLitres:F4},,{GrandTotalSaleAmount:F2}");
            });

            WriteCsvEntry(zip, $"Reconciliation_{dateStr}_Shift{shiftLabel}.csv", w =>
            {
                w.WriteLine("Description,Amount");
                foreach (var r in ReconciliationRows) w.WriteLine($"{r.Description},{r.Amount:F2}");
                w.WriteLine($"TOTAL,{ReconciliationTotal:F2}");
                w.WriteLine($"Gross Sale (Fuel),{GrossSaleTotal:F2}");
                w.WriteLine($"Difference,{Difference:F2}");
            });

            StatusMessage = $"✅ Exported to {dialog.FileName}";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "CSV export failed");
            StatusMessage = $"❌ Export failed: {ex.Message}";
        }
    }

    private static void WriteCsvEntry(ZipArchive zip, string name, Action<StreamWriter> writeAction)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writeAction(writer);
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadShiftDataAsync();

    private async Task LoadDebtorRepaymentsAsync()
    {
        DebtorRepayments.Clear();
        DebtorsList.Clear();

        var debtorsRes = await _creditorRepo.GetAllActiveAsync();
        if (debtorsRes.Success && debtorsRes.Data != null)
        {
            foreach (var d in debtorsRes.Data)
            {
                DebtorsList.Add(d);
            }
        }

        var repaymentsRes = await _repaymentRepo.GetByDateAsync(SelectedDate.Date);
        if (repaymentsRes.Success && repaymentsRes.Data != null)
        {
            var filtered = repaymentsRes.Data.Where(r => string.IsNullOrEmpty(r.ShiftNumber) || r.ShiftNumber == SelectedShift);
            foreach (var r in filtered)
            {
                DebtorRepayments.Add(r);
            }
        }
    }

    [RelayCommand]
    private async Task AddDebtorRepaymentAsync()
    {
        if (IsShiftLocked)
        {
            RepaymentStatusMessage = "❌ Shift is locked.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewDebtorName))
        {
            RepaymentStatusMessage = "❌ Debtor Name is required.";
            return;
        }

        if (NewRepaymentAmount <= 0)
        {
            RepaymentStatusMessage = "❌ Amount must be greater than zero.";
            return;
        }

        // Validate outstanding balance: Outstanding Balance = Total Debit - Total Repayments
        var allEntriesResult = await _dsmRepo.GetEntriesForDateRangeAsync(new DateTime(2000, 1, 1), DateTime.Today.AddYears(1));
        double totalDebit = 0;
        if (allEntriesResult.Success && allEntriesResult.Data != null)
        {
            totalDebit = allEntriesResult.Data
                .SelectMany(e => e.DebitEntries)
                .Where(d => d.DebtorName.Trim().Equals(NewDebtorName.Trim(), StringComparison.OrdinalIgnoreCase))
                .Sum(d => d.Amount);
        }

        double totalRepayments = 0;
        var repaymentsResult = await _repaymentRepo.GetByDateRangeAsync(new DateTime(2000, 1, 1), DateTime.Today.AddYears(1));
        if (repaymentsResult.Success && repaymentsResult.Data != null)
        {
            totalRepayments = repaymentsResult.Data
                .Where(r => r.CreditorName.Trim().Equals(NewDebtorName.Trim(), StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.Amount);
        }

        double outstandingBalance = totalDebit - totalRepayments;
        if (NewRepaymentAmount > outstandingBalance)
        {
            RepaymentStatusMessage = $"❌ Repayment exceeds outstanding balance of ₹{outstandingBalance:F2}.";
            return;
        }

        var repayment = new CreditorRepayment
        {
            CreditorName = NewDebtorName.Trim(),
            RepaymentDate = SelectedDate.Date,
            ShiftNumber = SelectedShift,
            PaymentMode = NewRepaymentMode,
            ChequeNo = NewRepaymentMode == "Cheque" ? NewChequeNumber?.Trim() : null,
            Amount = NewRepaymentAmount,
            CreatedAt = DateTime.Now,
            CardTid = (NewRepaymentMode == "PhonePe" || NewRepaymentMode == "Credit Card" || NewRepaymentMode == "PetroCard" || NewRepaymentMode == "Others") ? NewCardTid?.Trim() : null,
            CardBatch = (NewRepaymentMode == "PhonePe" || NewRepaymentMode == "Credit Card" || NewRepaymentMode == "PetroCard" || NewRepaymentMode == "Others") ? NewCardBatch?.Trim() : null,
            Denom500 = NewRepaymentMode == "Cash" ? (NewDenom500 ?? 0) : 0,
            Denom200 = NewRepaymentMode == "Cash" ? (NewDenom200 ?? 0) : 0,
            Denom100 = NewRepaymentMode == "Cash" ? (NewDenom100 ?? 0) : 0,
            Denom50 = NewRepaymentMode == "Cash" ? (NewDenom50 ?? 0) : 0,
            Denom20 = NewRepaymentMode == "Cash" ? (NewDenom20 ?? 0) : 0,
            Denom10 = NewRepaymentMode == "Cash" ? (NewDenom10 ?? 0) : 0,
            Coins = NewRepaymentMode == "Cash" ? (NewCoins ?? 0) : 0
        };

        var result = await _repaymentRepo.AddAsync(repayment);
        if (result.Success)
        {
            RepaymentStatusMessage = "✅ Repayment logged successfully!";
            NewDebtorName = "";
            NewRepaymentAmount = 0;
            NewChequeNumber = "";
            NewRepaymentMode = "Cash";
            NewCardTid = "";
            NewCardBatch = "";
            NewDenom500 = null;
            NewDenom200 = null;
            NewDenom100 = null;
            NewDenom50 = null;
            NewDenom20 = null;
            NewDenom10 = null;
            NewCoins = null;
            await LoadShiftDataAsync();
        }
        else
        {
            RepaymentStatusMessage = $"❌ {result.Error}";
        }
    }

    [RelayCommand]
    private async Task DeleteDebtorRepaymentAsync(CreditorRepayment? repayment)
    {
        if (repayment == null) return;
        if (IsShiftLocked)
        {
            RepaymentStatusMessage = "❌ Shift is locked.";
            return;
        }

        var result = await _repaymentRepo.DeleteAsync(repayment.CreditorRepaymentId);
        if (result.Success)
        {
            RepaymentStatusMessage = "✅ Repayment deleted.";
            await LoadShiftDataAsync();
        }
        else
        {
            RepaymentStatusMessage = $"❌ {result.Error}";
        }
    }

    /// <summary>
    /// Returns shift display label: A→I, B→II, C→III
    /// </summary>
    public static string GetShiftDisplayLabel(string shiftType) => shiftType switch
    {
        "A" => "Shift I",
        "B" => "Shift II",
        "C" => "Shift III",
        _ => $"Shift {shiftType}"
    };

    private List<NozzleGroupDto> BuildNozzleGroups(AgsShiftImport? import)
    {
        var groups = new List<NozzleGroupDto>();

        var readingsDict = import?.NozzleReadings?.ToDictionary(r => r.NozzleNumber) 
                           ?? new Dictionary<int, AgsNozzleReading>();

        var hsdTank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD");
        var msITank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I");
        var msIITank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II");

        // Aggregate manual readings from loaded DSM entries
        var manualReadings = new Dictionary<int, (double Opening, double Closing, double Sale)>();
        if (_loadedEntries != null)
        {
            foreach (var group in _loadedEntries.SelectMany(e => e.NozzleReadings).GroupBy(r => r.NozzleNumber))
            {
                var sorted = group.OrderBy(r => r.OpeningReading).ToList();
                var opening = sorted.FirstOrDefault()?.OpeningReading ?? 0.0;
                var closing = group.OrderByDescending(r => r.ClosingReading).FirstOrDefault()?.ClosingReading ?? 0.0;
                var sale = group.Sum(r => r.SaleLitres);
                manualReadings[group.Key] = (opening, closing, sale);
            }
        }

        NozzleDisplayItem CreateItem(int num, string fuelType)
        {
            if (manualReadings.TryGetValue(num, out var mr))
            {
                return new NozzleDisplayItem
                {
                    NozzleNumber = num,
                    FuelType = fuelType,
                    OpeningReading = mr.Opening,
                    ClosingReading = mr.Closing,
                    SaleLitres = mr.Sale,
                    HasReading = true
                };
            }
            if (readingsDict.TryGetValue(num, out var r))
            {
                return new NozzleDisplayItem
                {
                    NozzleNumber = num,
                    FuelType = fuelType,
                    OpeningReading = r.OpeningReading,
                    ClosingReading = r.ClosingReading,
                    SaleLitres = r.NetSaleLitres,
                    HasReading = true
                };
            }
            return new NozzleDisplayItem
            {
                NozzleNumber = num,
                FuelType = fuelType,
                OpeningReading = 0,
                ClosingReading = 0,
                SaleLitres = 0,
                HasReading = false
            };
        }

        // Petrol (Tank 1)
        var msGroup1 = new NozzleGroupDto
        {
            GroupName = "Petrol (Tank 1)",
            FuelType = "Petrol",
            Dip = msITank?.ClosingDipMM ?? 0,
            Stock = msITank?.ClosingStockLitres ?? 0
        };
        msGroup1.Rows.Add(new List<NozzleDisplayItem> { CreateItem(1, "Petrol"), CreateItem(2, "Petrol"), CreateItem(5, "Petrol") });
        msGroup1.Rows.Add(new List<NozzleDisplayItem> { CreateItem(6, "Petrol"), CreateItem(9, "Petrol"), CreateItem(10, "Petrol") });
        groups.Add(msGroup1);

        // Diesel (Tank 2)
        var hsdGroup2 = new NozzleGroupDto
        {
            GroupName = "Diesel (Tank 2)",
            FuelType = "Diesel",
            Dip = hsdTank?.ClosingDipMM ?? 0,
            Stock = hsdTank?.ClosingStockLitres ?? 0
        };
        hsdGroup2.Rows.Add(new List<NozzleDisplayItem> { CreateItem(3, "Diesel"), CreateItem(4, "Diesel") });
        hsdGroup2.Rows.Add(new List<NozzleDisplayItem> { CreateItem(11, "Diesel"), CreateItem(12, "Diesel") });
        groups.Add(hsdGroup2);

        // Diesel (Tank 3)
        var hsdGroup3 = new NozzleGroupDto
        {
            GroupName = "Diesel (Tank 3)",
            FuelType = "Diesel",
            Dip = msIITank?.ClosingDipMM ?? 0,
            Stock = msIITank?.ClosingStockLitres ?? 0
        };
        hsdGroup3.Rows.Add(new List<NozzleDisplayItem> { CreateItem(7, "Diesel"), CreateItem(8, "Diesel") });
        groups.Add(hsdGroup3);

        return groups;
    }

    [RelayCommand]
    private async Task SaveTankStocksAsync()
    {
        try
        {
            var agsRepo = App.Services.GetRequiredService<IAgsImportRepository>();
            var activeImportRes = await agsRepo.GetActiveShiftImportAsync(SelectedDate, SelectedShift);
            var import = (activeImportRes.Success && activeImportRes.Data != null) ? activeImportRes.Data : null;

            if (import == null)
            {
                // Create a manual import record
                import = new AgsShiftImport
                {
                    ImportDate = SelectedDate.Date,
                    ShiftType = SelectedShift,
                    PdfFileName = "Manual Entry",
                    ImportedBy = "User",
                    IsActive = true,
                    ImportedAt = DateTime.Now
                };
            }

            // Ensure TankStocks is loaded
            if (import.TankStocks == null)
            {
                import.TankStocks = new List<AgsTankStock>();
            }

            foreach (var g in NozzleGroups)
            {
                string fuelTypeInDb = g.GroupName switch
                {
                    string s when s.Contains("Tank 1") => "MS-I",
                    string s when s.Contains("Tank 2") => "HSD",
                    string s when s.Contains("Tank 3") => "MS-II",
                    _ => g.FuelType
                };

                var tank = import.TankStocks.FirstOrDefault(t => t.FuelType == fuelTypeInDb);
                if (tank == null)
                {
                    tank = new AgsTankStock
                    {
                        FuelType = fuelTypeInDb,
                        TankNumber = fuelTypeInDb switch { "HSD" => 1, "MS-I" => 2, "MS-II" => 3, _ => 1 }
                    };
                    import.TankStocks.Add(tank);
                }

                tank.ClosingDipMM = g.Dip;
                tank.ClosingStockLitres = g.Stock;
            }

            var saveResult = await agsRepo.SaveShiftImportAsync(import);
            if (saveResult.Success)
            {
                StatusMessage = "✅ Tank stocks saved successfully!";
                await LoadShiftDataAsync();
            }
            else
            {
                StatusMessage = $"❌ Save failed: {saveResult.Error}";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save tank stocks");
            StatusMessage = $"❌ Error: {ex.Message}";
        }
    }
}
