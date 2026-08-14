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

public partial class FinalCalculationViewModel : ObservableObject, IDisposable
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
    private readonly IDsmPersonalDebtorRepository _personalDebtorRepo;
    private readonly ITidCalculationService _tidService;
    private readonly IAgsInventoryService _inventoryService;
    private readonly IReportService _reportService;
    private readonly ILogger _logger = Log.ForContext<FinalCalculationViewModel>();
    public DebtorManagementViewModel DebtorManagementVm { get; }

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private string _selectedShift = "A";
    [ObservableProperty] private string _selectedManager = "";
    public ObservableCollection<string> ManagerOptions { get; } = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isShiftLocked;
    [ObservableProperty] private int _shiftId;
    [ObservableProperty] private ObservableCollection<NozzleGroupDto> _nozzleGroups = new();
    [ObservableProperty] private ShiftReportDto? _currentReport;

    // TABLE A
    [ObservableProperty] private ObservableCollection<DsmSummaryRowDto> _dsmSummaryRows = new();
    [ObservableProperty] private DsmSummaryRowDto? _dsmSummaryTotals;
    [ObservableProperty] private ObservableCollection<DsmShiftTotalDto> _dsmShiftTotals = new();

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
    [ObservableProperty] private ObservableCollection<RepaymentBreakdownDto> _repaymentBreakdown = new();
    [ObservableProperty] private ObservableCollection<Creditor> _debtorsList = new();
    [ObservableProperty] private string _newDebtorName = "";
    [ObservableProperty] private string _newRepaymentMode = "Cash";
    [ObservableProperty] private double _newRepaymentAmount;
    [ObservableProperty] private string _newChequeNumber = "";
    [ObservableProperty] private string _repaymentStatusMessage = "";
    [ObservableProperty] private string _newCardTid = "";
    [ObservableProperty] private string _newCardBatch = "";

    // Denominations for debtor Cash repayment
    [ObservableProperty] private int? _newDenom500;
    [ObservableProperty] private int? _newDenom200;
    [ObservableProperty] private int? _newDenom100;
    [ObservableProperty] private int? _newDenom50;
    [ObservableProperty] private int? _newDenom20;
    [ObservableProperty] private int? _newDenom10;
    [ObservableProperty] private double? _newCoins;

    public string[] PaymentModes { get; } = { "Cash", "PhonePe", "PineLabs Card", "PetroCard", "Bank Transfer", "Cheque" };

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
        _personalDebtorRepo = App.Services.GetRequiredService<IDsmPersonalDebtorRepository>();
        _tidService = App.Services.GetRequiredService<ITidCalculationService>();
        _inventoryService = App.Services.GetRequiredService<IAgsInventoryService>();
        _reportService = App.Services.GetRequiredService<IReportService>();
        DebtorManagementVm = App.Services.GetRequiredService<DebtorManagementViewModel>();

        DsmEntryService.DsmEntryChanged += OnDataChanged;
        DsmEntryService.DebtorChanged += OnDataChanged;
    }

    private void OnDataChanged()
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await LoadShiftDataAsync());
    }

    public void Dispose()
    {
        DsmEntryService.DsmEntryChanged -= OnDataChanged;
        DsmEntryService.DebtorChanged -= OnDataChanged;
        GC.SuppressFinalize(this);
    }

    partial void OnSelectedDateChanged(DateTime value) => _ = LoadShiftDataAsync();
    partial void OnSelectedShiftChanged(string value) => _ = LoadShiftDataAsync();
    partial void OnSelectedManagerChanged(string value)
    {
        if (CurrentReport != null)
        {
            CurrentReport.ManagerName = value;
        }
    }
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

            // Load extra shift collections, rates, and repayments
            var rateOverrides = await _fuelRateRepo.GetByShiftAsync(SelectedDate, SelectedShift);
            var rateDict = rateOverrides.Success
                ? rateOverrides.Data!.ToDictionary(r => r.FuelType, r => r.OverrideRate)
                : new Dictionary<string, double>();

            var settings = await _settingsRepo.GetSettingsAsync();
            double defaultHsd = settings.Success ? settings.Data!.HsdRate : 90.35;
            double defaultMsI = settings.Success ? settings.Data!.MsIRate : 103.81;
            double defaultMsII = settings.Success ? settings.Data!.MsIIRate : 103.81;
            double defaultCng = settings.Success ? settings.Data!.CngRate : 85.0;

            double hsdRate = rateDict.TryGetValue("HSD", out var hr) ? hr : defaultHsd;
            double msIRate = rateDict.TryGetValue("MS-I", out var mr1) ? mr1 : defaultMsI;
            double msIIRate = rateDict.TryGetValue("MS-II", out var mr2) ? mr2 : defaultMsII;
            double cngRate = rateDict.TryGetValue("CNG", out var cr) ? cr : defaultCng;

            var shiftExpensesResult = await _expenseRepo.GetByShiftIdAsync(shift.ShiftId);
            var shiftExpenses = shiftExpensesResult.Success ? shiftExpensesResult.Data! : new List<Expense>();

            var otherCashResult = await _otherCashRepo.GetByShiftAsync(SelectedDate, SelectedShift);
            var otherCash = otherCashResult.Success ? otherCashResult.Data! : new List<ShiftOtherCash>();

            await LoadDebtorRepaymentsAsync();

            var tidSheetToday = await _tidService.GetTidSheetAsync(SelectedDate.Date);
            var tidSheetTomorrow = await _tidService.GetTidSheetAsync(SelectedDate.Date.AddDays(1));
            string stationName = settings.Success ? settings.Data!.PumpStationName : "PyroSync";

            var report = _reportService.CalculateShiftReport(
                SelectedDate,
                SelectedShift,
                _loadedEntries,
                shiftExpenses,
                otherCash,
                DebtorRepayments.ToList(),
                hsdRate, msIRate, msIIRate, cngRate,
                tidSheetToday, tidSheetTomorrow,
                stationName);

            CurrentReport = report;

            var settingsRes = await _settingsRepo.GetSettingsAsync();
            if (settingsRes.Success && settingsRes.Data != null)
            {
                var set = settingsRes.Data;
                ManagerOptions.Clear();
                if (!string.IsNullOrWhiteSpace(set.Shift1Manager)) ManagerOptions.Add(set.Shift1Manager.Trim());
                if (!string.IsNullOrWhiteSpace(set.Shift2Manager)) ManagerOptions.Add(set.Shift2Manager.Trim());
                if (!string.IsNullOrWhiteSpace(set.Shift3Manager)) ManagerOptions.Add(set.Shift3Manager.Trim());

                if (string.IsNullOrWhiteSpace(SelectedManager) || !ManagerOptions.Contains(SelectedManager))
                {
                    if (SelectedShift == "A" || SelectedShift == "1" || SelectedShift == "I")
                        SelectedManager = set.Shift1Manager ?? "";
                    else if (SelectedShift == "B" || SelectedShift == "2" || SelectedShift == "II")
                        SelectedManager = set.Shift2Manager ?? "";
                    else if (SelectedShift == "C" || SelectedShift == "3" || SelectedShift == "III")
                        SelectedManager = set.Shift3Manager ?? "";
                    else if (ManagerOptions.Count > 0)
                        SelectedManager = ManagerOptions[0];
                }
            }
            if (CurrentReport != null)
            {
                CurrentReport.ManagerName = SelectedManager;
            }

            // Bind values to existing properties so UI bindings don't break
            DsmSummaryRows = new ObservableCollection<DsmSummaryRowDto>(report.DsmSummaryRows);
            DsmSummaryTotals = report.DsmSummaryTotals;
            DsmShiftTotals = new ObservableCollection<DsmShiftTotalDto>(report.DsmShiftTotals ?? new List<DsmShiftTotalDto>());

            Cash1Rows = new ObservableCollection<CashDenomDisplayRow>(report.Cash1.ToDisplayRows(true));
            Cash1Total = report.Cash1.GrandTotal;

            Cash2Rows = new ObservableCollection<CashDenomDisplayRow>(report.Cash2.ToDisplayRows(false));
            Cash2Total = report.Cash2.GrandTotal;

            CreditorRows = new ObservableCollection<DebitRegisterRowDto>(report.CreditorRows);
            CreditorsTotal = report.CreditorsTotal;

            // Re-bind expense rows
            ExpenseRows = new ObservableCollection<ExpenseRegisterRowDto>(report.ExpenseRows);
            ExpensesTotal = report.ExpensesTotal;

            HsdLitres = report.FuelSales.FirstOrDefault(f => f.FuelType == "HSD")?.Litres ?? 0;
            HsdRate = report.FuelSales.FirstOrDefault(f => f.FuelType == "HSD")?.Rate ?? hsdRate;
            HsdAmount = report.FuelSales.FirstOrDefault(f => f.FuelType == "HSD")?.Amount ?? 0;

            MsILitres = report.FuelSales.FirstOrDefault(f => f.FuelType == "MS-I")?.Litres ?? 0;
            MsIRate = report.FuelSales.FirstOrDefault(f => f.FuelType == "MS-I")?.Rate ?? msIRate;
            MsIAmount = report.FuelSales.FirstOrDefault(f => f.FuelType == "MS-I")?.Amount ?? 0;

            MsIILitres = report.FuelSales.FirstOrDefault(f => f.FuelType == "MS-II")?.Litres ?? 0;
            MsIIRate = report.FuelSales.FirstOrDefault(f => f.FuelType == "MS-II")?.Rate ?? msIIRate;
            MsIIAmount = report.FuelSales.FirstOrDefault(f => f.FuelType == "MS-II")?.Amount ?? 0;

            CngLitres = report.FuelSales.FirstOrDefault(f => f.FuelType == "CNG")?.Litres ?? 0;
            CngRate = report.FuelSales.FirstOrDefault(f => f.FuelType == "CNG")?.Rate ?? cngRate;
            CngAmount = report.FuelSales.FirstOrDefault(f => f.FuelType == "CNG")?.Amount ?? 0;

            MsTotalLitres = MsILitres + MsIILitres;
            MsTotalAmount = MsIAmount + MsIIAmount;

            TotalLitres = report.TotalFuelLitres;
            TotalFuelSaleAmount = report.TotalFuelAmount;
            OtherCashTotal = report.OtherCashTotal;
            GrandTotalSaleAmount = report.GrandTotalSaleAmount;

            // Reconciliation rows — use audit description with breakdown
            var reconRows = new List<ReconciliationRowDto>();
            foreach (var category in report.CollectionBreakdown)
            {
                reconRows.Add(new ReconciliationRowDto { Description = category.DescriptionWithBreakdown, Amount = category.Amount });
            }
            ReconciliationRows = new ObservableCollection<ReconciliationRowDto>(reconRows);
            ReconciliationTotal = report.ActualCollection;
            GrossSaleTotal = report.ExpectedCollection;
            Difference = report.Difference;
            IsBalanced = report.IsBalanced;
            TotalDsmShort = report.TotalDsmShort;

            // Bind repayment breakdown for debtor recovery section
            RepaymentBreakdown = new ObservableCollection<RepaymentBreakdownDto>(report.RepaymentBreakdown);

            // Load AGS Nozzle Readings
            try
            {
                var nozzleGroupsList = await _inventoryService.BuildNozzleGroupsAsync(SelectedDate, SelectedShift, _loadedEntries);
                NozzleGroups = new ObservableCollection<NozzleGroupDto>(nozzleGroupsList);
                report.NozzleGroups = nozzleGroupsList;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to load AGS nozzle groups for calculation");
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
        if (_loadedEntries == null || _loadedEntries.Count == 0) return;

        var settings = _settingsRepo.GetSettingsAsync().GetAwaiter().GetResult();
        string stationName = settings.Success ? settings.Data!.PumpStationName : "PyroSync";

        var rateOverrides = _fuelRateRepo.GetByShiftAsync(SelectedDate, SelectedShift).GetAwaiter().GetResult();
        var rateDict = rateOverrides.Success
            ? rateOverrides.Data!.ToDictionary(r => r.FuelType, r => r.OverrideRate)
            : new Dictionary<string, double>();

        double defaultHsd = settings.Success ? settings.Data!.HsdRate : 90.35;
        double defaultMsI = settings.Success ? settings.Data!.MsIRate : 103.81;
        double defaultMsII = settings.Success ? settings.Data!.MsIIRate : 103.81;
        double defaultCng = settings.Success ? settings.Data!.CngRate : 85.0;

        double hsdRate = rateDict.TryGetValue("HSD", out var hr) ? hr : defaultHsd;
        double msIRate = rateDict.TryGetValue("MS-I", out var mr1) ? mr1 : defaultMsI;
        double msIIRate = rateDict.TryGetValue("MS-II", out var mr2) ? mr2 : defaultMsII;
        double cngRate = rateDict.TryGetValue("CNG", out var cr) ? cr : defaultCng;

        var shiftExpensesResult = _expenseRepo.GetByShiftIdAsync(ShiftId).GetAwaiter().GetResult();
        var shiftExpenses = shiftExpensesResult.Success ? shiftExpensesResult.Data! : new List<Expense>();

        var otherCashResult = _otherCashRepo.GetByShiftAsync(SelectedDate, SelectedShift).GetAwaiter().GetResult();
        var otherCash = otherCashResult.Success ? otherCashResult.Data! : new List<ShiftOtherCash>();

        var tidSheetToday = _tidService.GetTidSheetAsync(SelectedDate.Date).GetAwaiter().GetResult();
        var tidSheetTomorrow = _tidService.GetTidSheetAsync(SelectedDate.Date.AddDays(1)).GetAwaiter().GetResult();

        var report = _reportService.CalculateShiftReport(
            SelectedDate,
            SelectedShift,
            _loadedEntries,
            shiftExpenses,
            otherCash,
            DebtorRepayments.ToList(),
            hsdRate, msIRate, msIIRate, cngRate,
            tidSheetToday, tidSheetTomorrow,
            stationName);

        CurrentReport = report;

        // Re-bind reconciliation
        var reconRows = new List<ReconciliationRowDto>();
        foreach (var category in report.CollectionBreakdown)
        {
            reconRows.Add(new ReconciliationRowDto { Description = category.DescriptionWithBreakdown, Amount = category.Amount });
        }
        ReconciliationRows = new ObservableCollection<ReconciliationRowDto>(reconRows);
        ReconciliationTotal = report.ActualCollection;
        GrossSaleTotal = report.ExpectedCollection;
        Difference = report.Difference;
        IsBalanced = report.IsBalanced;
        TotalDsmShort = report.TotalDsmShort;
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

        // Auto-save tank stocks and propagate calculations before locking!
        var saveInvRes = await _inventoryService.SaveAndPersistInventoryAsync(SelectedDate, SelectedShift, NozzleGroups.ToList());
        if (!saveInvRes.Success)
        {
            StatusMessage = $"❌ Failed to save tank inventory before locking: {saveInvRes.Error}";
            return;
        }

        var propRes = await _inventoryService.PropagateInventoryCalculationsAsync(SelectedDate, SelectedShift);
        if (!propRes.Success)
        {
            _logger.Warning("Propagation failed during lock shift: {Error}", propRes.Error);
        }

        var result = await _shiftRepo.LockShiftAsync(ShiftId);
        if (result.Success) { StatusMessage = "✅ Shift locked successfully!"; await LoadShiftDataAsync(); }
        else StatusMessage = $"❌ {result.Error}";
    }

    private readonly ExcelExportService _excelExportService = App.Services.GetRequiredService<ExcelExportService>();

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        if (CurrentReport == null)
        {
            MessageBox.Show("No shift data available to export.", "Export Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"ShiftTotal_Shift{SelectedShift}_{SelectedDate:yyyyMMdd}.xlsx",
                DefaultExt = ".xlsx",
                Filter = "Excel Worksheets (*.xlsx)|*.xlsx"
            };

            if (dialog.ShowDialog() == true)
            {
                var filePath = await _excelExportService.ExportShiftTotalReportAsync(CurrentReport, dialog.FileName);
                MessageBox.Show($"Shift Total report exported successfully to:\n{filePath}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export Shift Total report to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

        var repaymentsRes = await _repaymentRepo.GetByDateRangeAsync(SelectedDate.Date.AddDays(-1), SelectedDate.Date.AddDays(1));
        if (repaymentsRes.Success && repaymentsRes.Data != null)
        {
            foreach (var r in repaymentsRes.Data)
            {
                var classified = SettlementWindowResolver.Classify(r);
                if (classified.IsValid && classified.BusinessDate == SelectedDate.Date)
                {
                    bool match = false;
                    if (SelectedShift == "B")
                    {
                        match = classified.SettlementWindow == "Day";
                    }
                    else if (SelectedShift == "A")
                    {
                        match = classified.SettlementWindow == "Morning" || classified.SettlementWindow == "Night";
                    }

                    if (match)
                    {
                        DebtorRepayments.Add(r);
                    }
                }
            }
        }

        // Personal Debtor repayments are handled under DSM Loss section
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

        var repayment = new CreditorRepayment
        {
            CreditorName = NewDebtorName.Trim(),
            RepaymentDate = SettlementWindowResolver.GetRepaymentDateTime(SelectedDate.Date, SelectedShift, DateTime.Now),
            ShiftNumber = SelectedShift,
            PaymentMode = NewRepaymentMode,
            ChequeNo = NewRepaymentMode == "Cheque" ? NewChequeNumber?.Trim() : null,
            Amount = NewRepaymentAmount,
            CreatedAt = DateTime.Now,
            CardTid = (NewRepaymentMode == "PhonePe" || NewRepaymentMode == "Credit Card" || NewRepaymentMode == "PineLabs Card" || NewRepaymentMode == "PetroCard" || NewRepaymentMode == "Petro Card" || NewRepaymentMode == "Others") ? NewCardTid?.Trim() : null,
            CardBatch = (NewRepaymentMode == "PhonePe" || NewRepaymentMode == "Credit Card" || NewRepaymentMode == "PineLabs Card" || NewRepaymentMode == "PetroCard" || NewRepaymentMode == "Petro Card" || NewRepaymentMode == "Others") ? NewCardBatch?.Trim() : null,
            Denom500 = NewRepaymentMode == "Cash" ? (NewDenom500 ?? 0) : 0,
            Denom200 = NewRepaymentMode == "Cash" ? (NewDenom200 ?? 0) : 0,
            Denom100 = NewRepaymentMode == "Cash" ? (NewDenom100 ?? 0) : 0,
            Denom50 = NewRepaymentMode == "Cash" ? (NewDenom50 ?? 0) : 0,
            Denom20 = NewRepaymentMode == "Cash" ? (NewDenom20 ?? 0) : 0,
            Denom10 = NewRepaymentMode == "Cash" ? (NewDenom10 ?? 0) : 0,
            Coins = NewRepaymentMode == "Cash" ? (int)(NewCoins ?? 0.0) : 0
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
            DsmEntryService.RaiseDsmEntryChanged();
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
            DsmEntryService.RaiseDsmEntryChanged();
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

    [RelayCommand]
    private async Task SaveTankStocksAsync()
    {
        try
        {
            var saveResult = await _inventoryService.SaveAndPersistInventoryAsync(SelectedDate, SelectedShift, NozzleGroups.ToList());
            if (saveResult.Success)
            {
                var propResult = await _inventoryService.PropagateInventoryCalculationsAsync(SelectedDate, SelectedShift);
                if (propResult.Success)
                {
                    StatusMessage = "✅ Tank stocks saved and propagated successfully!";
                }
                else
                {
                    StatusMessage = $"⚠️ Saved, but propagation failed: {propResult.Error}";
                }
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
