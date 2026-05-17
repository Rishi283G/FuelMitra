using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using Serilog;

namespace FuelPro.UI.ViewModels;

public partial class DayTotalViewModel : ObservableObject
{
    private readonly IShiftRepository _shiftRepo;
    private readonly IDsmEntryRepository _dsmRepo;
    private readonly IExpenseRepository _expenseRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IShiftFuelRateRepository _fuelRateRepo;
    private readonly IShiftAggregationService _aggregation;
    private readonly ILogger _logger = Log.ForContext<DayTotalViewModel>();

    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
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

    partial void OnSelectedDateChanged(DateTime value) => _ = LoadDayDataAsync();

    [RelayCommand]
    private async Task LoadDayDataAsync()
    {
        IsLoading = true;
        StatusMessage = "";
        HasData = false;
        try
        {
            var allEntries = new List<DsmEntry>();
            var allExpenses = new List<Expense>();
            var shiftOverrides = new Dictionary<string, double>();
            DsmPrintRows = new List<(string Shift, DsmSummaryRowDto Row)>();

            // Aggregate data from all shifts (A, B, C)
            foreach (var shiftLabel in new[] { "A", "B", "C" })
            {
                var shiftResult = await _shiftRepo.GetShiftAsync(SelectedDate, shiftLabel);
                if (!shiftResult.Success || shiftResult.Data == null) continue;

                var shift = shiftResult.Data;
                
                var entriesResult = await _dsmRepo.GetEntriesForShiftAsync(shift.ShiftId);
                if (entriesResult.Success && entriesResult.Data != null)
                {
                    allEntries.AddRange(entriesResult.Data);
                    // Build per-shift DSM rows with the shift label
                    var shiftDsmRows = _aggregation.BuildDsmSummaryRows(entriesResult.Data);
                    foreach (var r in shiftDsmRows)
                        DsmPrintRows.Add((shiftLabel, r));
                }

                var expResult = await _expenseRepo.GetByShiftIdAsync(shift.ShiftId);
                if (expResult.Success && expResult.Data != null)
                {
                    allExpenses.AddRange(expResult.Data);
                }
                
                var rateOverrides = await _fuelRateRepo.GetByShiftAsync(SelectedDate, shiftLabel);
                if (rateOverrides.Success && rateOverrides.Data != null)
                {
                    foreach(var ro in rateOverrides.Data)
                    {
                        shiftOverrides.TryAdd(ro.FuelType, ro.OverrideRate);
                    }
                }
            }

            if (allEntries.Count == 0)
            {
                StatusMessage = "No entries found for this date.";
                ClearAll();
                return;
            }

            // 1. Calculate Nozzle-wise Sale
            CalculateNozzleWiseSale(allEntries, shiftOverrides);

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

            Difference = ReconciliationTotalAmount - GrossDaySaleTotal;
            IsBalanced = Math.Abs(Difference) < 0.01;

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

    private void CalculateNozzleWiseSale(List<DsmEntry> allEntries, Dictionary<string, double> shiftOverrides)
    {
        var nozzleRows = new List<NozzleSummaryRowDto>();
        var settingsResult = _settingsRepo.GetSettingsAsync().Result;
        double defaultHsd = settingsResult.Success ? settingsResult.Data!.HsdRate : 90.35;
        double defaultMsI = settingsResult.Success ? settingsResult.Data!.MsIRate : 103.81;
        double defaultMsII = settingsResult.Success ? settingsResult.Data!.MsIIRate : 103.81;

        // Group by Pump and Nozzle, then find min opening and max closing across the whole day
        var allReadings = allEntries.SelectMany(e => e.NozzleReadings.Select(r => new { e.PumpId, Reading = r })).ToList();

        var pumpGroups = allReadings.GroupBy(x => new { x.PumpId, x.Reading.FuelType });

        foreach (var group in pumpGroups)
        {
            // The earliest entry has the minimum OpeningReading (assuming readings only go up)
            var opening = group.Min(x => x.Reading.OpeningReading);
            var closing = group.Max(x => x.Reading.ClosingReading);
            
            // Total gross litres sold from this pump/fuel
            var grossLitres = closing - opening;

            // Total testing for this pump (if we can link it)
            var netLitres = group.Sum(x => x.Reading.SaleLitres);

            double rate = group.Key.FuelType switch
            {
                "HSD" => shiftOverrides.TryGetValue("HSD", out var hr) ? hr : defaultHsd,
                "MS-I" => shiftOverrides.TryGetValue("MS-I", out var m1r) ? m1r : defaultMsI,
                "MS-II" => shiftOverrides.TryGetValue("MS-II", out var m2r) ? m2r : defaultMsII,
                _ => defaultHsd
            };

            double amount = netLitres * rate;

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
}
