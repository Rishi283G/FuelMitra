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
    [ObservableProperty] private double _phonePeTotal;
    [ObservableProperty] private double _phonePeCardMorningTotal;
    [ObservableProperty] private double _phonePeCardNightTotal;
    [ObservableProperty] private double _phonePeCardTotal;
    [ObservableProperty] private double _creditCardMorningTotal;
    [ObservableProperty] private double _creditCardNightTotal;
    [ObservableProperty] private double _creditCardTotal;
    [ObservableProperty] private double _petroCardTotal;
    [ObservableProperty] private double _bankCashTotal;
    [ObservableProperty] private double _cashInHandTotal;
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

            // 1. Calculate Nozzle-wise Sale
            CalculateNozzleWiseSale(allEntries);

            // 2. Collections (using DsmSummaryRows)
            var summaryRows = _aggregation.BuildDsmSummaryRows(allEntries);
            var totalsRow = _aggregation.BuildDsmSummaryTotalRow(summaryRows);

            // Load repayments for the day range
            DebtorRepayments.Clear();
            var repaymentsRes = await _repaymentRepo.GetByDateRangeAsync(StartDate.Date, EndDate.Date);
            var repayments = repaymentsRes.Success && repaymentsRes.Data != null ? repaymentsRes.Data : new List<CreditorRepayment>();
            foreach (var r in repayments)
            {
                DebtorRepayments.Add(r);
            }

            double cashRepayments = 0;
            double phonePeRepayments = 0;
            double creditCardRepayments = 0;

            foreach (var r in repayments)
            {
                if (r.PaymentMode == "Cash") cashRepayments += r.Amount;
                else if (r.PaymentMode == "PhonePe") phonePeRepayments += r.Amount;
                else if (r.PaymentMode == "Credit Card") creditCardRepayments += r.Amount;
            }

            double phonePeDirectMorning = 0;
            double phonePeDirectDay = 0;
            double phonePeDirectNight = 0;

            double phonePeCardMorning = 0;
            double phonePeCardDay = 0;
            double phonePeCardNight = 0;

            double pineLabsCardMorning = 0;
            double pineLabsCardDay = 0;
            double pineLabsCardNight = 0;

            double petroCardMorning = 0;
            double petroCardDay = 0;
            double petroCardNight = 0;
            
            var tidSheets = await _tidService.GetTidSheetsForRangeAsync(StartDate, EndDate);
            foreach (var sheet in tidSheets.Values)
            {
                phonePeDirectMorning += sheet.PhonePeDirectMorning;
                phonePeDirectDay += sheet.PhonePeDirectDay;
                phonePeDirectNight += sheet.PhonePeDirectNight;

                phonePeCardMorning += sheet.PhonePeCardMorning;
                phonePeCardDay += sheet.PhonePeCardDay;
                phonePeCardNight += sheet.PhonePeCardNight;

                pineLabsCardMorning += sheet.PineLabsCardMorning;
                pineLabsCardDay += sheet.PineLabsCardDay;
                pineLabsCardNight += sheet.PineLabsCardNight;

                petroCardMorning += sheet.PetroCardMorning;
                petroCardDay += sheet.PetroCardDay;
                petroCardNight += sheet.PetroCardNight;
            }

            PhonePeMorningTotal = phonePeDirectMorning + phonePeDirectDay;
            PhonePeNightTotal = phonePeDirectNight;
            PhonePeTotal = PhonePeMorningTotal + PhonePeNightTotal + phonePeRepayments;

            PhonePeCardMorningTotal = phonePeCardMorning + phonePeCardDay;
            PhonePeCardNightTotal = phonePeCardNight;
            PhonePeCardTotal = PhonePeCardMorningTotal + PhonePeCardNightTotal;

            CreditCardMorningTotal = pineLabsCardMorning + pineLabsCardDay;
            CreditCardNightTotal = pineLabsCardNight;
            CreditCardTotal = CreditCardMorningTotal + CreditCardNightTotal + creditCardRepayments;

            PetroCardTotal = petroCardMorning + petroCardDay + petroCardNight;
            
            var cash1Agg = _aggregation.AggregateCash(allEntries, "Cash1");
            BankCashTotal = cash1Agg.GrandTotal; // includes CashDeposit
            
            var cash2Agg = _aggregation.AggregateCash(allEntries, "Cash2");
            CashInHandTotal = cash2Agg.GrandTotal + cashRepayments;

            TotalDigital = PhonePeTotal + PhonePeCardMorningTotal + PhonePeCardNightTotal + CreditCardTotal + PetroCardTotal;
            TotalCash = BankCashTotal + CashInHandTotal;
            TotalDigitalAndCash = TotalDigital + TotalCash;

            // 3. Creditors
            var creditors = _aggregation.BuildCreditorRows(allEntries);
            CreditorRows = new ObservableCollection<DebitRegisterRowDto>(creditors);
            CreditorsTotal = creditors.Sum(r => r.Amount);

            SplitCash = BankCashTotal + CashInHandTotal;
            SplitPhonePe = PhonePeTotal;
            SplitUpi = PhonePeCardTotal;
            SplitPineLabsCard = CreditCardTotal;
            SplitCredit = CreditorsTotal;
            SplitOther = PetroCardTotal + totalsRow.Others;
            OthersTotal = totalsRow.Others;

            // 4. Expenses
            var expenses = _aggregation.BuildExpenseRows(allEntries, allExpenses);
            ExpenseRows = new ObservableCollection<ExpenseRegisterRowDto>(expenses);
            ExpensesTotal = expenses.Sum(r => r.Amount);

            // 5. Final Day Reconciliation
            var msTesting = allEntries.SelectMany(e => e.TestingEntries).Where(t => t.FuelType == "MS").Sum(t => t.Amount);
            var hsdTesting = allEntries.SelectMany(e => e.TestingEntries).Where(t => t.FuelType == "HSD").Sum(t => t.Amount);
            var cngTesting = allEntries.SelectMany(e => e.TestingEntries).Where(t => t.FuelType == "CNG").Sum(t => t.Amount);
            var totalTesting = msTesting + hsdTesting + cngTesting;

            double totalDsmShort = 0;
            var mismatchGroups = allEntries.GroupBy(e => new { e.ShiftId, e.DsmName, GroupPumpId = e.ReconciledToPumpId ?? e.PumpId });
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
                var dsmTesting = g.SelectMany(e => e.TestingEntries).Sum(t => t.Amount);
                var totalExpenses = g.SelectMany(e => e.Expenses).Sum(ex => ex.Amount);
                
                var totalCollection = totalInDirect + totalCreditors + dsmTesting + totalExpenses;
                var mismatch = totalCollection - grossSales;
                
                if (mismatch < -0.01)
                {
                    totalDsmShort += Math.Abs(mismatch);
                }
            }
            TotalDsmShort = totalDsmShort;
            ReconciliationTotalAmount = TotalDigitalAndCash + CreditorsTotal + ExpensesTotal + totalTesting + TotalDsmShort;
            
            double totalRepaymentsReconciled = cashRepayments + phonePeRepayments + creditCardRepayments;
            GrossDaySaleTotal = TotalDayFuelSaleAmount + totalRepaymentsReconciled;
            Difference = ReconciliationTotalAmount - GrossDaySaleTotal;
            IsBalanced = Math.Abs(Difference) < 0.01;

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

    private List<NozzleGroupDto> BuildNozzleGroupsForDay(List<AgsShiftImport> dayShifts)
    {
        var groups = new List<NozzleGroupDto>();
        
        var sortedShifts = dayShifts?.OrderBy(s => s.ImportDate).ThenBy(s => s.ShiftType).ToList() ?? new List<AgsShiftImport>();
        var lastShift = sortedShifts.LastOrDefault();

        var hsdTank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD");
        var msITank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I");
        var msIITank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II");

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

    private List<NozzleGroupDto> BuildNozzleGroups(AgsShiftImport? import)
    {
        var groups = new List<NozzleGroupDto>();

        var readingsDict = import?.NozzleReadings?.ToDictionary(r => r.NozzleNumber) 
                           ?? new Dictionary<int, AgsNozzleReading>();

        var hsdTank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD");
        var msITank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I");
        var msIITank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II");

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
}
