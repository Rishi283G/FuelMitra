using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;
using Serilog;

using FuelPro.Core.Models.AGS;

namespace FuelPro.UI.ViewModels;

public partial class DayTotalViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private ObservableCollection<NozzleGroupDto> _nozzleGroups = new();
    private readonly IShiftRepository _shiftRepo;
    private readonly IDsmEntryRepository _dsmRepo;
    private readonly IExpenseRepository _expenseRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IShiftFuelRateRepository _fuelRateRepo;
    private readonly IShiftAggregationService _aggregation;
    private readonly ITidCalculationService _tidService;
    private readonly IAgsInventoryService _inventoryService;
    private readonly IReportService _reportService;
    private readonly ILogger _logger = Log.ForContext<DayTotalViewModel>();
    private List<DsmEntry> _allEntries = new();

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private DateTime _startDate = DateTime.Today;
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private string _selectedPreset = "Today";

    public string[] Presets { get; } = { "Today", "Yesterday", "This Week", "This Month", "Last 7 Days", "Last 30 Days", "Custom" };
    [ObservableProperty] private string _selectedShift1Manager = "";
    [ObservableProperty] private string _selectedShift2Manager = "";
    public ObservableCollection<string> ManagerOptions { get; } = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private DayReportDto? _currentReport;

    // TABLE 1: Nozzle-wise Sale
    [ObservableProperty] private ObservableCollection<NozzleSummaryRowDto> _nozzleSaleRows = new();
    [ObservableProperty] private double _totalDayFuelSaleAmount;
    [ObservableProperty] private double _totalDayLitres;
    [ObservableProperty] private double _totalHsdLitres;
    [ObservableProperty] private double _totalMsILitres;
    [ObservableProperty] private double _totalMsIILitres;
    [ObservableProperty] private double _totalMsLitres;
    [ObservableProperty] private double _totalCngLitres;

    // TABLE 2: Collections
    [ObservableProperty] private double _phonePeMorningTotal;
    [ObservableProperty] private double _phonePeNightTotal;
    [ObservableProperty] private double _phonePeCardMorningTotal;
    [ObservableProperty] private double _phonePeCardNightTotal;
    [ObservableProperty] private double _creditCardMorningTotal;
    [ObservableProperty] private double _creditCardNightTotal;
    [ObservableProperty] private double _petroCardTotal;
    [ObservableProperty] private double _phonePeTotal;
    [ObservableProperty] private double _phonePeCardTotal;
    [ObservableProperty] private double _creditCardTotal;
    [ObservableProperty] private double _bankCashTotal;
    [ObservableProperty] private double _cashInHandTotal;
    // Cash Aggregate
    [ObservableProperty] private double _cash1Total;
    [ObservableProperty] private double _cash2Total;
    [ObservableProperty] private double _totalDigital;
    [ObservableProperty] private double _totalCash;
    [ObservableProperty] private double _totalDigitalAndCash;

    // Simplified splits
    [ObservableProperty] private double _splitCash;
    [ObservableProperty] private double _splitPhonePe;
    [ObservableProperty] private double _splitUpi;
    [ObservableProperty] private double _splitPineLabsCard;
    [ObservableProperty] private double _splitCredit;
    [ObservableProperty] private double _splitOther;
    [ObservableProperty] private double _othersTotal;

    // TABLE 3: Creditors / Debtors
    [ObservableProperty] private ObservableCollection<DebitRegisterRowDto> _creditorRows = new();
    [ObservableProperty] private double _creditorsTotal;

    // TABLE 4: Expenses
    [ObservableProperty] private ObservableCollection<ExpenseRegisterRowDto> _expenseRows = new();
    [ObservableProperty] private double _expensesTotal;

    // For printing: DSM rows tagged with their shift label
    public List<(string Shift, DsmSummaryRowDto Row)> DsmPrintRows { get; private set; } = new();

    // TABLE 5: Final Reconciliation
    [ObservableProperty] private double _reconciliationTotalAmount;
    [ObservableProperty] private double _grossDaySaleTotal;
    [ObservableProperty] private double _difference;
    [ObservableProperty] private bool _isBalanced;
    [ObservableProperty] private double _totalDsmShort;
    [ObservableProperty] private ObservableCollection<DsmPersonalDebtorPrintDto> _personalDebtorRows = new();
    [ObservableProperty] private double _totalDsmLoss;
    [ObservableProperty] private ObservableCollection<CreditorRepayment> _debtorRepayments = new();
    private readonly ICreditorRepaymentRepository _repaymentRepo;
    private readonly IDsmPersonalDebtorRepository _personalDebtorRepo;
    [ObservableProperty] private double _msTesting;
    [ObservableProperty] private double _hsdTesting;
    [ObservableProperty] private double _hsdTesting2;
    [ObservableProperty] private double _cngTesting;
    [ObservableProperty] private double _msTestingLitres;
    [ObservableProperty] private double _hsdTestingLitres;
    [ObservableProperty] private double _hsdTesting2Litres;
    [ObservableProperty] private double _cngTestingLitres;
    [ObservableProperty] private string _msTestingLabel = "MS Testing";
    [ObservableProperty] private string _hsdTestingLabel = "HSD Testing I";
    [ObservableProperty] private string _hsdTesting2Label = "HSD Testing II";
    [ObservableProperty] private string _cngTestingLabel = "CNG Testing";
    [ObservableProperty] private double _totalTesting;
    public ObservableCollection<TestingSummaryItem> TestingSummaryRows { get; } = new();

    private readonly IFeatureToggleService _featureService;
    private readonly ICollectionTypeService? _collectionTypeService;
    public bool IsDsmPersonalDebtorVisible => _featureService.IsFeatureEnabled("Admin_DsmPersonalDebtor", true);

    [ObservableProperty] private string _phonePeDisplayName = "PhonePe Direct";
    [ObservableProperty] private string _creditCardDisplayName = "PineLabs Card";
    [ObservableProperty] private string _petroCardDisplayName = "Petro Card";
    [ObservableProperty] private ObservableCollection<DayCollectionSummaryRow> _collectionRows = new();

    public DayTotalViewModel()
    {
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _dsmRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();
        _fuelRateRepo = App.Services.GetRequiredService<IShiftFuelRateRepository>();
        _aggregation = App.Services.GetRequiredService<IShiftAggregationService>();
        _repaymentRepo = App.Services.GetRequiredService<ICreditorRepaymentRepository>();
        _personalDebtorRepo = App.Services.GetRequiredService<IDsmPersonalDebtorRepository>();
        _tidService = App.Services.GetRequiredService<ITidCalculationService>();
        _inventoryService = App.Services.GetRequiredService<IAgsInventoryService>();
        _reportService = App.Services.GetRequiredService<IReportService>();
        _featureService = App.Services.GetRequiredService<IFeatureToggleService>();
        _collectionTypeService = App.Services?.GetService<ICollectionTypeService>();

        _featureService.FeatureConfigurationChanged += () =>
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(IsDsmPersonalDebtorVisible)));
            }
            else
            {
                OnPropertyChanged(nameof(IsDsmPersonalDebtorVisible));
            }
        };

        if (_collectionTypeService != null)
        {
            _collectionTypeService.CollectionTypesChanged += () =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(async () => await UpdateCollectionDisplayNamesAsync());
                }
                else
                {
                    _ = UpdateCollectionDisplayNamesAsync();
                }
            };
        }
        _ = UpdateCollectionDisplayNamesAsync();

        DsmEntryService.DsmEntryChanged += OnDataChanged;
        DsmEntryService.DebtorChanged += OnDataChanged;
    }

    private async Task UpdateCollectionDisplayNamesAsync()
    {
        if (_collectionTypeService == null) return;
        try
        {
            var types = await _collectionTypeService.GetActiveCollectionTypesAsync();
            var ph = types.FirstOrDefault(c => string.Equals(c.Code, "PHONEPE", StringComparison.OrdinalIgnoreCase));
            var cc = types.FirstOrDefault(c => string.Equals(c.Code, "CREDIT_CARD", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(c.Code, "CREDITCARD", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(c.Code, "PINELAB", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(c.Code, "PINELABS", StringComparison.OrdinalIgnoreCase));
            var pc = types.FirstOrDefault(c => string.Equals(c.Code, "PETROCARD", StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(c.Code, "PETRO_CARD", StringComparison.OrdinalIgnoreCase));

            PhonePeDisplayName = ph?.DisplayName ?? "PhonePe Direct";
            CreditCardDisplayName = cc?.DisplayName ?? "PineLabs Card";
            PetroCardDisplayName = pc?.DisplayName ?? "Petro Card";
        }
        catch { }
    }

    private void OnDataChanged()
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await LoadDayDataAsync());
    }

    public void Dispose()
    {
        DsmEntryService.DsmEntryChanged -= OnDataChanged;
        DsmEntryService.DebtorChanged -= OnDataChanged;
        GC.SuppressFinalize(this);
    }

    private bool _isUpdatingPreset;

    partial void OnSelectedDateChanged(DateTime value)
    {
        if (!_isUpdatingPreset)
        {
            _isUpdatingPreset = true;
            StartDate = value;
            EndDate = value;
            _isUpdatingPreset = false;
            _ = LoadDayDataAsync();
        }
    }

    partial void OnSelectedPresetChanged(string value)
    {
        if (value == "Custom") return;

        _isUpdatingPreset = true;
        var today = DateTime.Today;
        DateTime newStart = today;
        DateTime newEnd = today;
        switch (value)
        {
            case "Today":
                newStart = today;
                newEnd = today;
                break;
            case "Yesterday":
                newStart = today.AddDays(-1);
                newEnd = today.AddDays(-1);
                break;
            case "This Week":
                int diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                newStart = today.AddDays(-1 * diff).Date;
                newEnd = today;
                break;
            case "This Month":
                newStart = new DateTime(today.Year, today.Month, 1);
                newEnd = today;
                break;
            case "Last 7 Days":
                newStart = today.AddDays(-6);
                newEnd = today;
                break;
            case "Last 30 Days":
                newStart = today.AddDays(-29);
                newEnd = today;
                break;
        }

        StartDate = newStart;
        EndDate = newEnd;
        SelectedDate = newEnd;
        _isUpdatingPreset = false;

        _ = LoadDayDataAsync();
    }

    partial void OnStartDateChanged(DateTime value)
    {
        if (!_isUpdatingPreset)
        {
            SelectedPreset = "Custom";
            _ = LoadDayDataAsync();
        }
    }

    partial void OnEndDateChanged(DateTime value)
    {
        SelectedDate = value;
        if (!_isUpdatingPreset)
        {
            SelectedPreset = "Custom";
            _ = LoadDayDataAsync();
        }
    }

    partial void OnSelectedShift1ManagerChanged(string value)
    {
        if (CurrentReport != null) CurrentReport.Shift1Manager = value;
    }

    partial void OnSelectedShift2ManagerChanged(string value)
    {
        if (CurrentReport != null) CurrentReport.Shift2Manager = value;
    }

    [RelayCommand]
    private async Task LoadDayDataAsync()
    {
        IsLoading = true;
        StatusMessage = "";
        HasData = false;
        try
        {
            DsmPrintRows = new List<(string Shift, DsmSummaryRowDto Row)>();

            var entriesResult = await _dsmRepo.GetEntriesForDateRangeAsync(StartDate.Date, EndDate.Date.AddDays(1));
            var allEntries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();
            _allEntries = allEntries;

            var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(StartDate.Date, EndDate.Date.AddDays(1));
            var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
            
            var todayShifts = shifts.Where(s => s.ShiftDate.Date >= StartDate.Date && s.ShiftDate.Date <= EndDate.Date).ToList();
            var todayShiftIds = todayShifts.Select(s => s.ShiftId).ToList();

            var expResult = await _expenseRepo.GetExpensesByShiftIdsAsync(todayShiftIds);
            var allExpenses = expResult.Success && expResult.Data != null ? expResult.Data : new List<Expense>();

            if (allEntries.Where(e => e.Shift != null && e.Shift.ShiftDate.Date >= StartDate.Date && e.Shift.ShiftDate.Date <= EndDate.Date).Count() == 0)
            {
                StatusMessage = "No entries found for this date range.";
                ClearAll();
                return;
            }

            // Load repayments for today + 1 day
            DebtorRepayments.Clear();
            var repaymentsRes = await _repaymentRepo.GetByDateRangeAsync(StartDate.Date, EndDate.Date.AddDays(1));
            var repayments = repaymentsRes.Success && repaymentsRes.Data != null ? repaymentsRes.Data : new List<CreditorRepayment>();

            // Personal Debtor repayments are handled under DSM Loss section
            
            // Only add today's repayments to the UI list of debtor repayments on-screen (tomorrow's Shift A repayments are silently aggregated into the report)
            foreach (var r in repayments.Where(x => x.RepaymentDate.Date >= StartDate.Date && x.RepaymentDate.Date <= EndDate.Date))
            {
                DebtorRepayments.Add(r);
            }

            var settings = await _settingsRepo.GetSettingsAsync();
            double defaultHsd = settings.Success ? settings.Data!.HsdRate : 90.35;
            double defaultMsI = settings.Success ? settings.Data!.MsIRate : 103.81;
            double defaultMsII = settings.Success ? settings.Data!.MsIIRate : 103.81;
            double defaultCng = settings.Success ? settings.Data!.CngRate : 85.0;
            string stationName = settings.Success ? settings.Data!.PumpStationName : "PyroSync";

            var report = _reportService.CalculateDayReport(
                StartDate,
                EndDate,
                _allEntries,
                allExpenses,
                repayments,
                defaultHsd, defaultMsI, defaultMsII, defaultCng,
                stationName);

            // Populate DsmPrintRows from report
            foreach (var r in report.DsmSummaryRows)
            {
                var entry = allEntries.FirstOrDefault(e => e.DsmName == r.DsmName && e.PumpId == r.PumpId && e.Shift?.ShiftType == r.Shift && e.Shift.ShiftDate.Date >= StartDate.Date && e.Shift.ShiftDate.Date <= EndDate.Date);
                if (entry == null)
                {
                    entry = allEntries.FirstOrDefault(e => e.DsmName == r.DsmName && e.PumpId == r.PumpId && e.Shift?.ShiftType == r.Shift && e.Shift.ShiftDate.Date == EndDate.Date.AddDays(1));
                }
                var shiftLabel = entry?.Shift != null ? $"{entry.Shift.ShiftDate:dd/MM} {r.Shift}" : $"Unknown {r.Shift}";
                DsmPrintRows.Add((shiftLabel, r));
            }

            CurrentReport = report;

            var settingsRes = await _settingsRepo.GetSettingsAsync();
            if (settingsRes.Success && settingsRes.Data != null)
            {
                var set = settingsRes.Data;
                ManagerOptions.Clear();
                if (!string.IsNullOrWhiteSpace(set.Shift1Manager)) ManagerOptions.Add(set.Shift1Manager.Trim());
                if (!string.IsNullOrWhiteSpace(set.Shift2Manager)) ManagerOptions.Add(set.Shift2Manager.Trim());
                if (!string.IsNullOrWhiteSpace(set.Shift3Manager)) ManagerOptions.Add(set.Shift3Manager.Trim());

                if (string.IsNullOrWhiteSpace(SelectedShift1Manager))
                    SelectedShift1Manager = set.Shift1Manager ?? "";
                if (string.IsNullOrWhiteSpace(SelectedShift2Manager))
                    SelectedShift2Manager = set.Shift2Manager ?? "";
            }
            if (CurrentReport != null)
            {
                CurrentReport.Shift1Manager = SelectedShift1Manager;
                CurrentReport.Shift2Manager = SelectedShift2Manager;
            }

            // 1. Calculate Nozzle-wise Sale
            CalculateNozzleWiseSale(allEntries);

            TotalDayFuelSaleAmount = report.TotalFuelAmount;
            TotalDayLitres = report.TotalFuelLitres;
            TotalHsdLitres = report.FuelSales.Where(f => f.FuelType == "HSD").Sum(f => f.Litres);
            TotalMsILitres = report.FuelSales.Where(f => f.FuelType == "MS-I").Sum(f => f.Litres);
            TotalMsIILitres = report.FuelSales.Where(f => f.FuelType == "MS-II").Sum(f => f.Litres);
            TotalMsLitres = TotalMsILitres + TotalMsIILitres;
            TotalCngLitres = report.FuelSales.Where(f => f.FuelType == "CNG").Sum(f => f.Litres);

            // Rebind Creditor and Expense rows
            CreditorRows = new ObservableCollection<DebitRegisterRowDto>(report.CreditorRows);
            CreditorsTotal = report.CreditorsTotal;

            ExpenseRows = new ObservableCollection<ExpenseRegisterRowDto>(report.ExpenseRows);
            ExpensesTotal = report.ExpensesTotal;

            // Mapping dynamic collections for UI bindings
            var breakdown = report.CollectionBreakdown ?? new List<CollectionCategoryDto>();
            CollectionRows.Clear();
            double digitalTotal = 0;
            double cashTotal = 0;

            foreach (var c in breakdown)
            {
                string cat = c.Category;
                if (cat.Contains("Testing", StringComparison.OrdinalIgnoreCase) ||
                    cat.Equals("Debtors", StringComparison.OrdinalIgnoreCase) ||
                    cat.Equals("Expenses", StringComparison.OrdinalIgnoreCase) ||
                    cat.Contains("DSM Short", StringComparison.OrdinalIgnoreCase) ||
                    cat.Contains("Kandhare", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bool isCash = cat.Contains("Cash", StringComparison.OrdinalIgnoreCase);
                if (isCash)
                {
                    cashTotal += c.Amount;
                }
                else
                {
                    digitalTotal += c.Amount;
                }

                string color = isCash ? "#2E7D32" :
                               cat.Contains("Card", StringComparison.OrdinalIgnoreCase) ? "#E65100" :
                               cat.Contains("Petro", StringComparison.OrdinalIgnoreCase) ? "#6A1B9A" : "#1565C0";

                CollectionRows.Add(new DayCollectionSummaryRow
                {
                    CollectionMode = cat,
                    Amount = c.Amount,
                    DisplayColor = color
                });
            }

            TotalCash = cashTotal;
            TotalDigital = digitalTotal;
            TotalDigitalAndCash = cashTotal + digitalTotal;

            // Legacy individual properties preserved for any specific bindings
            double getAmt(string cat) => breakdown.FirstOrDefault(x => string.Equals(x.Category, cat, StringComparison.OrdinalIgnoreCase))?.Amount ?? 0;
            BankCashTotal = getAmt("Cash Deposit") > 0 ? getAmt("Cash Deposit") : getAmt("Bank Cash");
            CashInHandTotal = getAmt("Cash In Hand");
            PhonePeTotal = breakdown.Where(x => x.Category.Contains("PhonePe", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount);
            CreditCardTotal = breakdown.Where(x => (x.Category.Contains("Card", StringComparison.OrdinalIgnoreCase) || x.Category.Contains("PineLab", StringComparison.OrdinalIgnoreCase)) && !x.Category.Contains("Petro", StringComparison.OrdinalIgnoreCase) && !x.Category.Contains("PhonePe", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount);
            PetroCardTotal = breakdown.Where(x => x.Category.Contains("Petro", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Amount);

            // Splits
            SplitCash = TotalCash;
            SplitPhonePe = PhonePeTotal;
            SplitUpi = PhonePeCardTotal;
            SplitPineLabsCard = CreditCardTotal;
            SplitCredit = CreditorsTotal;
            SplitOther = PetroCardTotal + report.OtherCashTotal;
            OthersTotal = report.OtherCashTotal;

            TestingSummaryRows.Clear();
            if (report?.TestingSummaryItems != null)
            {
                foreach (var t in report.TestingSummaryItems)
                {
                    TestingSummaryRows.Add(t);
                }
            }

            TotalTesting = TestingSummaryRows.Sum(t => t.Amount);

            ReconciliationTotalAmount = report.ActualCollection;
            GrossDaySaleTotal = report.ExpectedCollection;
            TotalDsmShort = report.TotalDsmShort;
            PersonalDebtorRows = new ObservableCollection<DsmPersonalDebtorPrintDto>(report.PersonalDebtors ?? new());
            TotalDsmLoss = PersonalDebtorRows.Sum(p => p.Amount);
            Difference = report.Difference < -0.01 ? -report.TotalDsmShort : (report.Difference > 0.01 ? report.Difference : 0);
            IsBalanced = Math.Abs(Difference) < 0.01;

            // Load AGS Nozzle readings for the day range
            try
            {
                var nozzleGroupsList = await _inventoryService.BuildNozzleGroupsForDateRangeAsync(StartDate, EndDate, _allEntries);
                NozzleGroups = new ObservableCollection<NozzleGroupDto>(nozzleGroupsList);
                if (report != null)
                {
                    report.TankSummary = nozzleGroupsList;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to load AGS nozzle groups for day total");
                var fallbackGroups = await _inventoryService.BuildNozzleGroupsForDateRangeAsync(StartDate, EndDate, _allEntries);
                NozzleGroups = new ObservableCollection<NozzleGroupDto>(fallbackGroups);
                if (report != null)
                {
                    report.TankSummary = fallbackGroups;
                }
            }

            HasData = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load day data");
            StatusMessage = $"Error loading data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void CalculateNozzleWiseSale(List<DsmEntry> allEntries)
    {
        var nozzleRows = new List<NozzleSummaryRowDto>();

        // Group by Pump and canonical fuel type (from PumpConfiguration, not stored FuelType)
        var allReadings = allEntries.SelectMany(e => e.NozzleReadings.Select(r => new
        {
            e.PumpId,
            Reading = r,
            CanonicalFuelType = FuelPro.Core.Common.PumpConfiguration.GetFuelTypeDisplayName(e.PumpId, r.NozzleNumber, e.Shift?.ShiftDate ?? SelectedDate)
        })).ToList();

        var pumpGroups = allReadings.GroupBy(x => new { x.PumpId, FuelType = x.CanonicalFuelType });

        foreach (var group in pumpGroups)
        {
            var opening = group.Min(x => x.Reading.OpeningReading);
            var closing = group.Max(x => x.Reading.ClosingReading);
            var grossLitres = closing - opening;
            var netLitres = group.Sum(x => x.Reading.SaleLitres);
            var amount = group.Sum(x => x.Reading.Amount);
            double rate = netLitres > 0 ? amount / netLitres : group.Select(x => x.Reading.Rate).FirstOrDefault();

            nozzleRows.Add(new NozzleSummaryRowDto
            {
                PumpId = group.Key.PumpId,
                FuelType = group.Key.FuelType,
                OpeningReading = opening,
                ClosingReading = closing,
                GrossLitres = grossLitres,
                NetSaleLitres = netLitres,
                Rate = rate,
                Amount = amount
            });
        }

        NozzleSaleRows = new ObservableCollection<NozzleSummaryRowDto>(nozzleRows.OrderBy(r => r.PumpId).ThenBy(r => r.FuelType));
        TotalDayLitres = nozzleRows.Sum(r => r.NetSaleLitres);
        TotalHsdLitres = nozzleRows.Where(r => r.FuelType == "HSD").Sum(r => r.NetSaleLitres);
        TotalMsILitres = nozzleRows.Where(r => r.FuelType == "MS-I").Sum(r => r.NetSaleLitres);
        TotalMsIILitres = nozzleRows.Where(r => r.FuelType == "MS-II").Sum(r => r.NetSaleLitres);
        TotalCngLitres = nozzleRows.Where(r => r.FuelType == "CNG").Sum(r => r.NetSaleLitres);
        TotalMsLitres = TotalMsILitres + TotalMsIILitres;
        TotalDayFuelSaleAmount = nozzleRows.Sum(r => r.Amount);
    }

    private void ClearAll()
    {
        NozzleSaleRows.Clear();
        TotalDayLitres = 0;
        TotalHsdLitres = TotalMsILitres = TotalMsIILitres = TotalMsLitres = TotalCngLitres = 0;
        TotalDayFuelSaleAmount = 0;

        PhonePeMorningTotal = PhonePeNightTotal = PhonePeTotal = PhonePeCardMorningTotal = PhonePeCardNightTotal = PhonePeCardTotal = CreditCardMorningTotal = CreditCardNightTotal = CreditCardTotal = PetroCardTotal = BankCashTotal = CashInHandTotal = TotalDigital = TotalCash = TotalDigitalAndCash = OthersTotal = 0;
        
        CreditorRows.Clear(); CreditorsTotal = 0;
        ExpenseRows.Clear(); ExpensesTotal = 0;
        
        ReconciliationTotalAmount = GrossDaySaleTotal = Difference = TotalDsmShort = 0;
        PersonalDebtorRows.Clear(); TotalDsmLoss = 0;
        IsBalanced = false;
        DebtorRepayments.Clear();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDayDataAsync();

    private readonly ExcelExportService _excelExportService = App.Services.GetRequiredService<ExcelExportService>();

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        if (CurrentReport == null)
        {
            MessageBox.Show("No day total data available to export.", "Export Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"DayTotal_{StartDate:yyyyMMdd}_to_{EndDate:yyyyMMdd}.xlsx",
                DefaultExt = ".xlsx",
                Filter = "Excel Worksheets (*.xlsx)|*.xlsx"
            };

            if (dialog.ShowDialog() == true)
            {
                var filePath = await _excelExportService.ExportDayTotalReportAsync(CurrentReport, dialog.FileName);
                MessageBox.Show($"Day Total report exported successfully to:\n{filePath}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to export Day Total report to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task SaveTankStocksAsync()
    {
        try
        {
            var saveResult = await _inventoryService.SaveAndPersistInventoryAsync(EndDate, "B", NozzleGroups.ToList());
            if (saveResult.Success)
            {
                var propResult = await _inventoryService.PropagateInventoryCalculationsAsync(EndDate, "B");
                if (propResult.Success)
                {
                    StatusMessage = "✅ Tank stocks saved and propagated successfully!";
                }
                else
                {
                    StatusMessage = $"⚠️ Saved, but propagation failed: {propResult.Error}";
                }
                await LoadDayDataAsync();
            }
            else
            {
                StatusMessage = $"❌ Save failed: {saveResult.Error}";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save tank stocks from day total");
            StatusMessage = $"❌ Error: {ex.Message}";
        }
    }
}

public class DayCollectionSummaryRow
{
    public string CollectionMode { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string DisplayColor { get; set; } = "#1565C0";
}
