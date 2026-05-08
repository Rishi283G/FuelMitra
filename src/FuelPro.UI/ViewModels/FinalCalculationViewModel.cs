using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using Serilog;

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
    private readonly ILogger _logger = Log.ForContext<FinalCalculationViewModel>();

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private string _selectedShift = "A";
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isShiftLocked;
    [ObservableProperty] private int _shiftId;

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

    public string[] ShiftOptions { get; } = { "A", "B", "C" };

    private List<DsmEntry> _loadedEntries = new();

    public FinalCalculationViewModel()
    {
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _dsmRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();
        _otherCashRepo = App.Services.GetRequiredService<IShiftOtherCashRepository>();
        _fuelRateRepo = App.Services.GetRequiredService<IShiftFuelRateRepository>();
        _aggregation = App.Services.GetRequiredService<IShiftAggregationService>();
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

            MsTotalLitres = MsILitres + MsIILitres;
            MsTotalAmount = MsIAmount + MsIIAmount;

            TotalLitres = HsdLitres + MsILitres + MsIILitres;
            TotalFuelSaleAmount = HsdAmount + MsIAmount + MsIIAmount;

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
            OtherCashTotal = otherCash.Sum(o => o.Amount);
            GrandTotalSaleAmount = TotalFuelSaleAmount + OtherCashTotal;

            // TABLE F — Reconciliation
            RecalcReconciliation();

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
        var hsdTesting = _loadedEntries.SelectMany(e => e.TestingEntries).Where(t => t.FuelType == "HSD").Sum(t => t.Amount);
        var phonePeCardMorning = DsmSummaryRows.Sum(r => r.PhonePeCardMorning);
        var phonePeCardNight = DsmSummaryRows.Sum(r => r.PhonePeCardNight);
        var phonePeMorning = DsmSummaryRows.Sum(r => r.PhonePeMorning);
        var phonePeNight = DsmSummaryRows.Sum(r => r.PhonePeNight);
        var petroCard = DsmSummaryRows.Sum(r => r.PetroCard);
        var debit = CreditorsTotal;
        var creditCard = DsmSummaryRows.Sum(r => r.CreditCard);

        var reconRows = _aggregation.BuildReconciliationRows(
            msTesting, hsdTesting, phonePeCardMorning, phonePeCardNight, phonePeMorning, phonePeNight, petroCard,
            debit, creditCard, Cash1Total, Cash2Total, ExpensesTotal);
        ReconciliationRows = new ObservableCollection<ReconciliationRowDto>(reconRows);
        ReconciliationTotal = reconRows.Sum(r => r.Amount);

        GrossSaleTotal = IncludeOtherCashInGrossSale
            ? TotalFuelSaleAmount + OtherCashTotal
            : TotalFuelSaleAmount;

        Difference = _aggregation.CalculateDifference(GrossSaleTotal, ReconciliationTotal);
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
        TotalLitres = TotalFuelSaleAmount = MsTotalLitres = 0;
        GrandTotalSaleAmount = GrossSaleTotal = Difference = 0;
        IsBalanced = false;
        IsShiftLocked = false;
    }

    [RelayCommand]
    private async Task AddShiftExpenseAsync()
    {
        if (IsShiftLocked) { StatusMessage = "❌ Shift is locked."; return; }
        if (string.IsNullOrWhiteSpace(NewExpenseDescription)) return;
        var result = await _expenseRepo.AddShiftExpenseAsync(ShiftId, NewExpenseDescription, NewExpenseAmount);
        if (result.Success) { NewExpenseDescription = ""; NewExpenseAmount = 0; await LoadShiftDataAsync(); }
        else StatusMessage = $"❌ {result.Error}";
    }

    [RelayCommand]
    private async Task RemoveShiftExpenseAsync(int expenseId)
    {
        if (IsShiftLocked) { StatusMessage = "❌ Shift is locked."; return; }
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
                w.WriteLine("DSM Name,Pump No.,P.Pe Card (Morning),P.Pe Card (Night),Phone Pe,PineLab Card,Petro Card,Bank Cash,Debit,Expenses,Testing,Cash In Hand,Gross Sale");
                foreach (var r in DsmSummaryRows)
                    w.WriteLine($"{r.DsmName},{r.PumpId},{r.PhonePeCardMorning:F2},{r.PhonePeCardNight:F2},{r.PhonePe:F2},{r.CreditCard:F2},{r.PetroCard:F2},{r.CashDeposit:F2},{r.Debit:F2},{r.Expenses:F2},{r.Testing:F2},{r.CashInHand:F2},{r.GrossSales:F2}");
                if (DsmSummaryTotals != null)
                    w.WriteLine($"TOTAL,,{DsmSummaryTotals.PhonePeCardMorning:F2},{DsmSummaryTotals.PhonePeCardNight:F2},{DsmSummaryTotals.PhonePe:F2},{DsmSummaryTotals.CreditCard:F2},{DsmSummaryTotals.PetroCard:F2},{DsmSummaryTotals.CashDeposit:F2},{DsmSummaryTotals.Debit:F2},{DsmSummaryTotals.Expenses:F2},{DsmSummaryTotals.Testing:F2},{DsmSummaryTotals.CashInHand:F2},{DsmSummaryTotals.GrossSales:F2}");
            });

            WriteCsvEntry(zip, $"Cash_Details_{dateStr}_Shift{shiftLabel}.csv", w =>
            {
                w.WriteLine("Type,Amount");
                w.WriteLine($"Bank Deposit,{Cash1Total:F2}");
                w.WriteLine($"Cash In Hand,{Cash2Total:F2}");
            });

            WriteCsvEntry(zip, $"Creditors_{dateStr}_Shift{shiftLabel}.csv", w =>
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
}
