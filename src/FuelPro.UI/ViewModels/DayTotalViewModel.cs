using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using Serilog;

using FuelPro.Core.Models.AGS;

namespace FuelPro.UI.ViewModels;

public partial class DayTotalViewModel : ObservableObject
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
    [ObservableProperty] private ObservableCollection<CreditorRepayment> _debtorRepayments = new();
    private readonly ICreditorRepaymentRepository _repaymentRepo;
    [ObservableProperty] private double _msTesting;
    [ObservableProperty] private double _hsdTesting;
    [ObservableProperty] private double _hsdTesting2;
    [ObservableProperty] private double _cngTesting;
    [ObservableProperty] private double _totalTesting;

    public DayTotalViewModel()
    {
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _dsmRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();
        _fuelRateRepo = App.Services.GetRequiredService<IShiftFuelRateRepository>();
        _aggregation = App.Services.GetRequiredService<IShiftAggregationService>();
        _repaymentRepo = App.Services.GetRequiredService<ICreditorRepaymentRepository>();
        _tidService = App.Services.GetRequiredService<ITidCalculationService>();
        _inventoryService = App.Services.GetRequiredService<IAgsInventoryService>();
        _reportService = App.Services.GetRequiredService<IReportService>();
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

    [RelayCommand]
    private async Task LoadDayDataAsync()
    {
        IsLoading = true;
        StatusMessage = "";
        HasData = false;
        try
        {
            DsmPrintRows = new List<(string Shift, DsmSummaryRowDto Row)>();

            var entriesResult = await _dsmRepo.GetEntriesForDateRangeAsync(StartDate, EndDate);
            var allEntries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();
            _allEntries = allEntries;

            var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(StartDate, EndDate);
            var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();

            var expResult = await _expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
            var allExpenses = expResult.Success && expResult.Data != null ? expResult.Data : new List<Expense>();

            // Build per-shift DSM rows with the shift label/date
            var entriesByShift = allEntries.GroupBy(e => e.ShiftId);
            foreach (var g in entriesByShift)
            {
                var shift = shifts.FirstOrDefault(s => s.ShiftId == g.Key);
                var shiftLabel = shift != null ? $"{shift.ShiftDate:dd/MM} {shift.ShiftType}" : "Unknown";
                
                var shiftDsmRows = _aggregation.BuildDsmSummaryRows(g.ToList());
                foreach (var r in shiftDsmRows)
                    DsmPrintRows.Add((shiftLabel, r));
            }

            if (allEntries.Count == 0)
            {
                StatusMessage = "No entries found for this date range.";
                ClearAll();
                return;
            }

            // Load repayments for the day range
            DebtorRepayments.Clear();
            var repaymentsRes = await _repaymentRepo.GetByDateRangeAsync(StartDate.Date, EndDate.Date);
            var repayments = repaymentsRes.Success && repaymentsRes.Data != null ? repaymentsRes.Data : new List<CreditorRepayment>();
            foreach (var r in repayments)
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
                DebtorRepayments.ToList(),
                defaultHsd, defaultMsI, defaultMsII, defaultCng,
                stationName);

            CurrentReport = report;

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

            // Mapping individual collections for UI bindings
            var breakdown = report.CollectionBreakdown;
            double getAmt(string cat) => breakdown.FirstOrDefault(x => x.Category == cat)?.Amount ?? 0;

            PhonePeMorningTotal = getAmt("PhonePe Morning");
            PhonePeNightTotal = getAmt("PhonePe Night");
            PhonePeCardMorningTotal = getAmt("PhonePe Card Morning");
            PhonePeCardNightTotal = getAmt("PhonePe Card Night");
            CreditCardMorningTotal = getAmt("PineLabs Morning");
            CreditCardNightTotal = getAmt("PineLabs Night");
            PetroCardTotal = getAmt("Petro Card");

            PhonePeTotal = PhonePeMorningTotal + PhonePeNightTotal;
            PhonePeCardTotal = PhonePeCardMorningTotal + PhonePeCardNightTotal;
            CreditCardTotal = CreditCardMorningTotal + CreditCardNightTotal;
            BankCashTotal = getAmt("Cash Deposit");
            CashInHandTotal = getAmt("Cash In Hand");

            TotalDigital = PhonePeTotal + PhonePeCardTotal + CreditCardTotal + PetroCardTotal;
            TotalCash = BankCashTotal + CashInHandTotal;
            TotalDigitalAndCash = TotalDigital + TotalCash;

            // Splits
            SplitCash = BankCashTotal + CashInHandTotal;
            SplitPhonePe = PhonePeTotal;
            SplitUpi = PhonePeCardTotal;
            SplitPineLabsCard = CreditCardTotal;
            SplitCredit = CreditorsTotal;
            SplitOther = PetroCardTotal + report.OtherCashTotal;
            OthersTotal = report.OtherCashTotal;

            // Testing
            double msTesting = 0;
            double hsdTesting = 0;
            double hsdTesting2 = 0;
            double cngTesting = 0;

            foreach (var entry in allEntries)
            {
                foreach (var t in entry.TestingEntries)
                {
                    var cat = FuelPro.Core.Common.PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, SelectedDate.Date);
                    if (cat == "MS") msTesting += (double)t.Amount;
                    else if (cat == "HSD") hsdTesting += (double)t.Amount;
                    else if (cat == "HSD-II") hsdTesting2 += (double)t.Amount;
                    else if (cat == "CNG") cngTesting += (double)t.Amount;
                }
            }
            MsTesting = msTesting;
            HsdTesting = hsdTesting;
            HsdTesting2 = hsdTesting2;
            CngTesting = cngTesting;
            TotalTesting = msTesting + hsdTesting + hsdTesting2 + cngTesting;

            ReconciliationTotalAmount = report.ActualCollection;
            GrossDaySaleTotal = report.ExpectedCollection;
            Difference = report.Difference;
            IsBalanced = report.IsBalanced;
            TotalDsmShort = report.TotalDsmShort;

            // Load AGS Nozzle readings for the day range
            try
            {
                var agsRepo = App.Services.GetRequiredService<IAgsImportRepository>();
                var dayShifts = new List<AgsShiftImport>();
                for (var dt = StartDate.Date; dt <= EndDate.Date; dt = dt.AddDays(1))
                {
                    var shiftsRes = await agsRepo.GetShiftsForDateAsync(dt);
                    if (shiftsRes.Success && shiftsRes.Data != null)
                    {
                        dayShifts.AddRange(shiftsRes.Data.Where(s => s.IsActive));
                    }
                }
                var nozzleGroupsList = await BuildNozzleGroupsForDayAsync(dayShifts);
                NozzleGroups = new ObservableCollection<NozzleGroupDto>(nozzleGroupsList);
                if (report != null)
                {
                    report.TankSummary = nozzleGroupsList;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to load AGS nozzle groups for day total");
                var fallbackGroups = await _inventoryService.BuildNozzleGroupsAsync(StartDate, "B", null);
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
        IsBalanced = false;
        DebtorRepayments.Clear();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDayDataAsync();

    private async Task<List<NozzleGroupDto>> BuildNozzleGroupsForDayAsync(List<AgsShiftImport> dayShifts)
    {
        var groups = new List<NozzleGroupDto>();
        
        var sortedShifts = dayShifts?.OrderBy(s => s.ImportDate).ThenBy(s => s.ShiftType == "A").ToList() ?? new List<AgsShiftImport>();
        var lastShift = sortedShifts.LastOrDefault();

        var hsdTank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD");
        var msITank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I");
        var msIITank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II");

        // Fetch previous shift closings/dips before StartDate
        var agsRepo = App.Services.GetRequiredService<IAgsImportRepository>();
        var prevDate = StartDate.Date.AddDays(-1);
        var prevShift = "A";
        var prevImportRes = await agsRepo.GetActiveShiftImportAsync(prevDate, prevShift);
        var prevImport = (prevImportRes.Success && prevImportRes.Data != null) ? prevImportRes.Data : null;

        var prevHsdClosingStock = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD")?.ClosingStockLitres ?? 0.0;
        var prevMsIClosingStock = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I")?.ClosingStockLitres ?? 0.0;
        var prevMsIIClosingStock = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II")?.ClosingStockLitres ?? 0.0;

        var hsdOpeningStock = prevHsdClosingStock > 0 ? prevHsdClosingStock : (sortedShifts.FirstOrDefault()?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD")?.OpeningStockLitres ?? 0.0);
        var msIOpeningStock = prevMsIClosingStock > 0 ? prevMsIClosingStock : (sortedShifts.FirstOrDefault()?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I")?.OpeningStockLitres ?? 0.0);
        var msIIOpeningStock = prevMsIIClosingStock > 0 ? prevMsIIClosingStock : (sortedShifts.FirstOrDefault()?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II")?.OpeningStockLitres ?? 0.0);

        // Aggregate manual readings from _allEntries
        var manualReadings = new Dictionary<int, (double Opening, double Closing, double Sale)>();
        if (_allEntries != null)
        {
            foreach (var group in _allEntries.SelectMany(e => e.NozzleReadings).GroupBy(r => r.NozzleNumber))
            {
                var sorted = group.OrderBy(r => r.OpeningReading).ToList();
                var opening = sorted.FirstOrDefault()?.OpeningReading ?? 0.0;
                var closing = group.OrderByDescending(r => r.ClosingReading).FirstOrDefault()?.ClosingReading ?? 0.0;
                var sale = group.Sum(r => r.SaleLitres);
                manualReadings[group.Key] = (opening, closing, sale);
            }
        }

        var allAgsReadings = sortedShifts.SelectMany(s => s.NozzleReadings).ToList();

        // Calculate testing litres per tank category
        double msITesting = 0;
        double hsdTesting = 0;
        double msIITesting = 0;

        if (_allEntries != null)
        {
            foreach (var entry in _allEntries)
            {
                foreach (var t in entry.TestingEntries)
                {
                    var cat = FuelPro.Core.Common.PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, entry.Shift?.ShiftDate ?? StartDate.Date);
                    if (cat == "MS") msITesting += t.Litres;
                    else if (cat == "HSD") hsdTesting += t.Litres;
                    else if (cat == "HSD-II") msIITesting += t.Litres;
                }
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
            var nozzleReadings = allAgsReadings.Where(r => r.NozzleNumber == num).ToList();
            if (nozzleReadings.Count > 0)
            {
                var op = nozzleReadings.Min(r => r.OpeningReading);
                var cl = nozzleReadings.Max(r => r.ClosingReading);
                var sale = nozzleReadings.Sum(r => r.NetSaleLitres);
                return new NozzleDisplayItem
                {
                    NozzleNumber = num,
                    FuelType = fuelType,
                    OpeningReading = op,
                    ClosingReading = cl,
                    SaleLitres = sale,
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

        var group1Nozzles = new List<NozzleDisplayItem> { CreateItem(1, "Petrol"), CreateItem(2, "Petrol"), CreateItem(5, "Petrol"), CreateItem(6, "Petrol"), CreateItem(9, "Petrol"), CreateItem(10, "Petrol") };
        var group2Nozzles = new List<NozzleDisplayItem> { CreateItem(3, "Diesel"), CreateItem(4, "Diesel"), CreateItem(11, "Diesel"), CreateItem(12, "Diesel") };
        var group3Nozzles = new List<NozzleDisplayItem> { CreateItem(7, "Diesel"), CreateItem(8, "Diesel") };

        var msIDispensed = group1Nozzles.Sum(x => x.SaleLitres);
        var hsdDispensed = group2Nozzles.Sum(x => x.SaleLitres);
        var msIIDispensed = group3Nozzles.Sum(x => x.SaleLitres);

        double msIReceipts = 0, hsdReceipts = 0, msIIReceipts = 0;
        foreach (var sImport in sortedShifts)
        {
            msIReceipts += sImport.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I")?.ReceiptLitres ?? 0.0;
            hsdReceipts += sImport.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD")?.ReceiptLitres ?? 0.0;
            msIIReceipts += sImport.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II")?.ReceiptLitres ?? 0.0;
        }

        // Petrol (Tank 1)
        var msGroup1 = new NozzleGroupDto
        {
            GroupName = "Petrol (Tank 1)",
            FuelType = "Petrol",
            OpeningStock = msIOpeningStock,
            FuelDispensed = msIDispensed,
            TestingLitres = msITesting,
            Receipts = msIReceipts,
            Dip = msITank?.ClosingDipMM ?? 0,
            Stock = msITank?.ClosingStockLitres ?? (msIOpeningStock - msIDispensed + msITesting + msIReceipts)
        };
        msGroup1.Rows.Add(new List<NozzleDisplayItem> { group1Nozzles[0], group1Nozzles[1], group1Nozzles[2] });
        msGroup1.Rows.Add(new List<NozzleDisplayItem> { group1Nozzles[3], group1Nozzles[4], group1Nozzles[5] });
        groups.Add(msGroup1);

        // Diesel (Tank 2)
        var hsdGroup2 = new NozzleGroupDto
        {
            GroupName = "Diesel (Tank 2)",
            FuelType = "Diesel",
            OpeningStock = hsdOpeningStock,
            FuelDispensed = hsdDispensed,
            TestingLitres = hsdTesting,
            Receipts = hsdReceipts,
            Dip = hsdTank?.ClosingDipMM ?? 0,
            Stock = hsdTank?.ClosingStockLitres ?? (hsdOpeningStock - hsdDispensed + hsdTesting + hsdReceipts)
        };
        hsdGroup2.Rows.Add(new List<NozzleDisplayItem> { group2Nozzles[0], group2Nozzles[1] });
        hsdGroup2.Rows.Add(new List<NozzleDisplayItem> { group2Nozzles[2], group2Nozzles[3] });
        groups.Add(hsdGroup2);

        // Diesel (Tank 3)
        var hsdGroup3 = new NozzleGroupDto
        {
            GroupName = "Diesel (Tank 3)",
            FuelType = "Diesel",
            OpeningStock = msIIOpeningStock,
            FuelDispensed = msIIDispensed,
            TestingLitres = msIITesting,
            Receipts = msIIReceipts,
            Dip = msIITank?.ClosingDipMM ?? 0,
            Stock = msIITank?.ClosingStockLitres ?? (msIIOpeningStock - msIIDispensed + msIITesting + msIIReceipts)
        };
        hsdGroup3.Rows.Add(new List<NozzleDisplayItem> { group3Nozzles[0], group3Nozzles[1] });
        groups.Add(hsdGroup3);

        return groups;
    }
}
