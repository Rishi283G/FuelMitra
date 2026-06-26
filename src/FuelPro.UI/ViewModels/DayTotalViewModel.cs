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
    private readonly ILogger _logger = Log.ForContext<DayTotalViewModel>();

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private DateTime _startDate = DateTime.Today;
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private string _selectedPreset = "Today";

    public string[] Presets { get; } = { "Today", "Yesterday", "This Week", "This Month", "Last 7 Days", "Last 30 Days", "Custom" };
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _statusMessage = "";

    // TABLE 1: Nozzle-wise Sale
    [ObservableProperty] private ObservableCollection<NozzleSummaryRowDto> _nozzleSaleRows = new();
    [ObservableProperty] private double _totalDayFuelSaleAmount;
    [ObservableProperty] private double _totalDayLitres;
    [ObservableProperty] private double _totalHsdLitres;
    [ObservableProperty] private double _totalMsILitres;
    [ObservableProperty] private double _totalMsIILitres;
    [ObservableProperty] private double _totalMsLitres;

    // TABLE 2: Collections
    [ObservableProperty] private double _phonePeTotal;
    [ObservableProperty] private double _phonePeCardMorningTotal;
    [ObservableProperty] private double _phonePeCardNightTotal;
    [ObservableProperty] private double _creditCardMorningTotal;
    [ObservableProperty] private double _creditCardNightTotal;
    [ObservableProperty] private double _creditCardTotal;
    [ObservableProperty] private double _petroCardTotal;
    [ObservableProperty] private double _bankCashTotal;
    [ObservableProperty] private double _cashInHandTotal;
    [ObservableProperty] private double _totalDigital;
    [ObservableProperty] private double _totalCash;
    [ObservableProperty] private double _totalDigitalAndCash;

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

    public DayTotalViewModel()
    {
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _dsmRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();
        _fuelRateRepo = App.Services.GetRequiredService<IShiftFuelRateRepository>();
        _aggregation = App.Services.GetRequiredService<IShiftAggregationService>();
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

            // 1. Calculate Nozzle-wise Sale
            CalculateNozzleWiseSale(allEntries);

            // 2. Collections (using DsmSummaryRows)
            var summaryRows = _aggregation.BuildDsmSummaryRows(allEntries);
            var totalsRow = _aggregation.BuildDsmSummaryTotalRow(summaryRows);
            PhonePeTotal = totalsRow.PhonePe;
            PhonePeCardMorningTotal = totalsRow.PhonePeCardMorning;
            PhonePeCardNightTotal = totalsRow.PhonePeCardNight;
            CreditCardMorningTotal = totalsRow.CreditCardMorning;
            CreditCardNightTotal = totalsRow.CreditCardNight;
            CreditCardTotal = CreditCardMorningTotal + CreditCardNightTotal;
            PetroCardTotal = totalsRow.PetroCard;
            
            var cash1Agg = _aggregation.AggregateCash(allEntries, "Cash1");
            BankCashTotal = cash1Agg.GrandTotal; // includes CashDeposit
            
            var cash2Agg = _aggregation.AggregateCash(allEntries, "Cash2");
            CashInHandTotal = cash2Agg.GrandTotal;

            TotalDigital = PhonePeTotal + PhonePeCardMorningTotal + PhonePeCardNightTotal + CreditCardTotal + PetroCardTotal;
            TotalCash = BankCashTotal + CashInHandTotal;
            TotalDigitalAndCash = TotalDigital + TotalCash;

            // 3. Creditors
            var creditors = _aggregation.BuildCreditorRows(allEntries);
            CreditorRows = new ObservableCollection<DebitRegisterRowDto>(creditors);
            CreditorsTotal = creditors.Sum(r => r.Amount);

            // 4. Expenses
            var expenses = _aggregation.BuildExpenseRows(allEntries, allExpenses);
            ExpenseRows = new ObservableCollection<ExpenseRegisterRowDto>(expenses);
            ExpensesTotal = expenses.Sum(r => r.Amount);

            // 5. Final Day Reconciliation
            var msTesting = allEntries.SelectMany(e => e.TestingEntries).Where(t => t.FuelType == "MS").Sum(t => t.Amount);
            var hsdTesting = allEntries.SelectMany(e => e.TestingEntries).Where(t => t.FuelType == "HSD").Sum(t => t.Amount);
            var totalTesting = msTesting + hsdTesting;

            double totalDsmShort = 0;
            var mismatchGroups = allEntries.GroupBy(e => new { e.ShiftId, e.DsmName, GroupPumpId = e.ReconciledToPumpId ?? e.PumpId });
            foreach (var g in mismatchGroups)
            {
                var sumMismatch = g.Sum(e => (double)e.Mismatch);
                if (sumMismatch < 0)
                {
                    totalDsmShort += Math.Abs(sumMismatch);
                }
            }
            TotalDsmShort = totalDsmShort;
            ReconciliationTotalAmount = TotalDigitalAndCash + CreditorsTotal + ExpensesTotal + totalTesting + TotalDsmShort;
            GrossDaySaleTotal = TotalDayFuelSaleAmount;

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
                var nozzleGroupsList = BuildNozzleGroupsForDay(dayShifts);
                NozzleGroups = new ObservableCollection<NozzleGroupDto>(nozzleGroupsList);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to load AGS nozzle groups for day total");
                NozzleGroups = new ObservableCollection<NozzleGroupDto>(BuildNozzleGroups(null));
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
        TotalMsLitres = TotalMsILitres + TotalMsIILitres;
        TotalDayFuelSaleAmount = nozzleRows.Sum(r => r.Amount);
    }

    private void ClearAll()
    {
        NozzleSaleRows.Clear();
        TotalDayLitres = 0;
        TotalHsdLitres = TotalMsILitres = TotalMsIILitres = TotalMsLitres = 0;
        TotalDayFuelSaleAmount = 0;
        
        PhonePeTotal = PhonePeCardMorningTotal = PhonePeCardNightTotal = CreditCardMorningTotal = CreditCardNightTotal = CreditCardTotal = PetroCardTotal = BankCashTotal = CashInHandTotal = TotalDigital = TotalCash = TotalDigitalAndCash = 0;
        
        CreditorRows.Clear(); CreditorsTotal = 0;
        ExpenseRows.Clear(); ExpensesTotal = 0;
        
        ReconciliationTotalAmount = GrossDaySaleTotal = Difference = TotalDsmShort = 0;
        IsBalanced = false;
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadDayDataAsync();

    private List<NozzleGroupDto> BuildNozzleGroupsForDay(List<AgsShiftImport> dayShifts)
    {
        var groups = new List<NozzleGroupDto>();
        if (dayShifts == null || dayShifts.Count == 0)
        {
            return BuildNozzleGroups(null);
        }

        var allReadings = dayShifts.SelectMany(s => s.NozzleReadings).ToList();
        
        var sortedShifts = dayShifts.OrderBy(s => s.ImportDate).ThenBy(s => s.ShiftType).ToList();
        var firstShift = sortedShifts.First();
        var lastShift = sortedShifts.Last();

        var firstHsdTank = firstShift.TankStocks.FirstOrDefault(t => t.FuelType == "HSD");
        var lastHsdTank = lastShift.TankStocks.FirstOrDefault(t => t.FuelType == "HSD");
        
        var firstMsITank = firstShift.TankStocks.FirstOrDefault(t => t.FuelType == "MS-I");
        var lastMsITank = lastShift.TankStocks.FirstOrDefault(t => t.FuelType == "MS-I");
        
        var firstMsIITank = firstShift.TankStocks.FirstOrDefault(t => t.FuelType == "MS-II");
        var lastMsIITank = lastShift.TankStocks.FirstOrDefault(t => t.FuelType == "MS-II");

        NozzleDisplayItem CreateItem(int num, string fuelType)
        {
            var nozzleReadings = allReadings.Where(r => r.NozzleNumber == num).ToList();
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

        // HSD Group
        var hsdGroup = new NozzleGroupDto
        {
            GroupName = "HSD (Tank 3)",
            FuelType = "HSD",
            Dip = lastHsdTank?.ClosingDipMM ?? 0,
            Stock = lastHsdTank?.ClosingStockLitres ?? 0
        };
        hsdGroup.Rows.Add(new List<NozzleDisplayItem> { CreateItem(1, "HSD"), CreateItem(2, "HSD"), CreateItem(7, "HSD"), CreateItem(8, "HSD") });
        hsdGroup.Rows.Add(new List<NozzleDisplayItem> { CreateItem(3, "HSD"), CreateItem(4, "HSD"), CreateItem(9, "HSD"), CreateItem(10, "HSD") });
        groups.Add(hsdGroup);

        // MS-II Group
        var msIIGroup = new NozzleGroupDto
        {
            GroupName = "MS-II (Tank 2)",
            FuelType = "MS-II",
            Dip = lastMsIITank?.ClosingDipMM ?? 0,
            Stock = lastMsIITank?.ClosingStockLitres ?? 0
        };
        msIIGroup.Rows.Add(new List<NozzleDisplayItem> { CreateItem(13, "MS-II"), CreateItem(14, "MS-II"), CreateItem(17, "MS-II"), CreateItem(18, "MS-II") });
        msIIGroup.Rows.Add(new List<NozzleDisplayItem> { CreateItem(21, "MS-II"), CreateItem(22, "MS-II"), CreateItem(25, "MS-II"), CreateItem(26, "MS-II") });
        groups.Add(msIIGroup);

        // MS-I Group
        var msIGroup = new NozzleGroupDto
        {
            GroupName = "MS-I (Tank 1)",
            FuelType = "MS-I",
            Dip = lastMsITank?.ClosingDipMM ?? 0,
            Stock = lastMsITank?.ClosingStockLitres ?? 0
        };
        msIGroup.Rows.Add(new List<NozzleDisplayItem> { CreateItem(5, "MS-I"), CreateItem(6, "MS-I"), CreateItem(11, "MS-I"), CreateItem(12, "MS-I") });
        msIGroup.Rows.Add(new List<NozzleDisplayItem> { CreateItem(15, "MS-I"), CreateItem(16, "MS-I"), CreateItem(19, "MS-I"), CreateItem(20, "MS-I") });
        msIGroup.Rows.Add(new List<NozzleDisplayItem> { CreateItem(23, "MS-I"), CreateItem(24, "MS-I"), CreateItem(27, "MS-I"), CreateItem(28, "MS-I") });
        groups.Add(msIGroup);

        return groups;
    }

    private List<NozzleGroupDto> BuildNozzleGroups(AgsShiftImport? import)
    {
        var groups = new List<NozzleGroupDto>();

        var readingsDict = import?.NozzleReadings?.ToDictionary(r => r.NozzleNumber) 
                           ?? new Dictionary<int, AgsNozzleReading>();

        var hsdTank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD");
        var msITank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I");
        var msIITank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II");

        NozzleDisplayItem CreateItem(int num, string fuelType)
        {
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

        // HSD Group
        var hsdGroup = new NozzleGroupDto
        {
            GroupName = "HSD (Tank 3)",
            FuelType = "HSD",
            Dip = hsdTank?.ClosingDipMM ?? 0,
            Stock = hsdTank?.ClosingStockLitres ?? 0
        };
        hsdGroup.Rows.Add(new List<NozzleDisplayItem> { CreateItem(1, "HSD"), CreateItem(2, "HSD"), CreateItem(7, "HSD"), CreateItem(8, "HSD") });
        hsdGroup.Rows.Add(new List<NozzleDisplayItem> { CreateItem(3, "HSD"), CreateItem(4, "HSD"), CreateItem(9, "HSD"), CreateItem(10, "HSD") });
        groups.Add(hsdGroup);

        // MS-II Group
        var msIIGroup = new NozzleGroupDto
        {
            GroupName = "MS-II (Tank 2)",
            FuelType = "MS-II",
            Dip = msIITank?.ClosingDipMM ?? 0,
            Stock = msIITank?.ClosingStockLitres ?? 0
        };
        msIIGroup.Rows.Add(new List<NozzleDisplayItem> { CreateItem(13, "MS-II"), CreateItem(14, "MS-II"), CreateItem(17, "MS-II"), CreateItem(18, "MS-II") });
        msIIGroup.Rows.Add(new List<NozzleDisplayItem> { CreateItem(21, "MS-II"), CreateItem(22, "MS-II"), CreateItem(25, "MS-II"), CreateItem(26, "MS-II") });
        groups.Add(msIIGroup);

        // MS-I Group
        var msIGroup = new NozzleGroupDto
        {
            GroupName = "MS-I (Tank 1)",
            FuelType = "MS-I",
            Dip = msITank?.ClosingDipMM ?? 0,
            Stock = msITank?.ClosingStockLitres ?? 0
        };
        msIGroup.Rows.Add(new List<NozzleDisplayItem> { CreateItem(5, "MS-I"), CreateItem(6, "MS-I"), CreateItem(11, "MS-I"), CreateItem(12, "MS-I") });
        msIGroup.Rows.Add(new List<NozzleDisplayItem> { CreateItem(15, "MS-I"), CreateItem(16, "MS-I"), CreateItem(19, "MS-I"), CreateItem(20, "MS-I") });
        msIGroup.Rows.Add(new List<NozzleDisplayItem> { CreateItem(23, "MS-I"), CreateItem(24, "MS-I"), CreateItem(27, "MS-I"), CreateItem(28, "MS-I") });
        groups.Add(msIGroup);

        return groups;
    }
}
