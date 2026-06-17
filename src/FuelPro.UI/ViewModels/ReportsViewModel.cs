using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.UI.Printing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FuelPro.UI.ViewModels;

public partial class ReportsViewModel : ObservableObject
{
    private readonly ExportService _exportService;
    private readonly IShiftRepository _shiftRepo;
    private readonly IDsmEntryRepository _dsmRepo;
    private readonly IExpenseRepository _expenseRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IShiftAggregationService _aggregation;
    private readonly IFinancialCalculationService _financialCalcService;

    [ObservableProperty] private string _selectedReportType = "DSR"; // "DSR" or "MonthlyPL"
    [ObservableProperty] private DateTime _selectedDate = DateTime.Today;
    [ObservableProperty] private int _selectedYear = DateTime.Today.Year;
    [ObservableProperty] private string _selectedMonthName = DateTime.Today.ToString("MMMM");

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isLoading;

    public ObservableCollection<int> Years { get; } = new();
    public ObservableCollection<string> Months { get; } = new();

    private readonly string[] _monthNames = {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    };

    public ReportsViewModel()
    {
        _exportService = App.Services.GetRequiredService<ExportService>();
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _dsmRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();
        _aggregation = App.Services.GetRequiredService<IShiftAggregationService>();
        _financialCalcService = App.Services.GetRequiredService<IFinancialCalculationService>();

        // Populate Years (Current Year - 2 to Current Year + 2)
        var currYear = DateTime.Today.Year;
        for (int y = currYear - 2; y <= currYear + 2; y++)
        {
            Years.Add(y);
        }

        // Populate Months
        foreach (var m in _monthNames)
        {
            Months.Add(m);
        }
    }

    private int GetMonthNumber(string name)
    {
        int idx = Array.IndexOf(_monthNames, name);
        return idx >= 0 ? idx + 1 : DateTime.Today.Month;
    }

    [RelayCommand]
    private async Task PrintReportAsync()
    {
        IsLoading = true;
        StatusMessage = "Preparing report for printing...";
        try
        {
            if (SelectedReportType == "DSR")
            {
                // DSR: Daily Sales Register (Single Day)
                var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(SelectedDate, SelectedDate);
                var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
                
                var entriesResult = await _dsmRepo.GetEntriesForDateRangeAsync(SelectedDate, SelectedDate);
                var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

                if (entries.Count == 0)
                {
                    MessageBox.Show("No entry data found for the selected date.", "PyroSync — Report", MessageBoxButton.OK, MessageBoxImage.Information);
                    StatusMessage = "No data found.";
                    return;
                }

                var shiftIds = shifts.Select(s => s.ShiftId).ToList();
                var expResult = await _expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
                var allExpenses = expResult.Success && expResult.Data != null ? expResult.Data : new List<Expense>();

                var stationName = "PyroSync";
                var s = await _settingsRepo.GetSettingsAsync();
                if (s.Success && s.Data != null) stationName = s.Data.PumpStationName;

                // 1. Calculate Nozzle-wise Sale
                var nozzleRows = new List<NozzleSummaryRowDto>();
                var allReadings = entries.SelectMany(e => e.NozzleReadings.Select(r => new
                {
                    e.PumpId,
                    Reading = r,
                    CanonicalFuelType = PumpConfiguration.GetFuelTypeDisplayName(e.PumpId, r.NozzleNumber, SelectedDate)
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
                double totalDayFuelSaleAmount = nozzleRows.Sum(r => r.Amount);
                double totalDayLitres = nozzleRows.Sum(r => r.NetSaleLitres);
                double totalHsdLitres = nozzleRows.Where(r => r.FuelType == "HSD").Sum(r => r.NetSaleLitres);
                double totalMsILitres = nozzleRows.Where(r => r.FuelType == "MS-I").Sum(r => r.NetSaleLitres);
                double totalMsIILitres = nozzleRows.Where(r => r.FuelType == "MS-II").Sum(r => r.NetSaleLitres);
                double totalMsLitres = totalMsILitres + totalMsIILitres;

                // 2. Collections
                var summaryRows = _aggregation.BuildDsmSummaryRows(entries);
                var totalsRow = _aggregation.BuildDsmSummaryTotalRow(summaryRows);
                var phonePeTotal = totalsRow.PhonePe;
                var phonePeCardMorningTotal = totalsRow.PhonePeCardMorning;
                var phonePeCardNightTotal = totalsRow.PhonePeCardNight;
                var creditCardMorningTotal = totalsRow.CreditCardMorning;
                var creditCardNightTotal = totalsRow.CreditCardNight;
                var creditCardTotal = creditCardMorningTotal + creditCardNightTotal;
                var petroCardTotal = totalsRow.PetroCard;

                var cash1Agg = _aggregation.AggregateCash(entries, "Cash1");
                var bankCashTotal = cash1Agg.GrandTotal;

                var cash2Agg = _aggregation.AggregateCash(entries, "Cash2");
                var cashInHandTotal = cash2Agg.GrandTotal;

                var totalDigital = phonePeTotal + phonePeCardMorningTotal + phonePeCardNightTotal + creditCardTotal + petroCardTotal;
                var totalCash = bankCashTotal + cashInHandTotal;
                var totalDigitalAndCash = totalDigital + totalCash;

                // 3. Creditors
                var creditors = _aggregation.BuildCreditorRows(entries);
                var creditorsTotal = creditors.Sum(r => r.Amount);

                // 4. Expenses
                var expenses = _aggregation.BuildExpenseRows(entries, allExpenses);
                var expensesTotal = expenses.Sum(r => r.Amount);

                // 5. Testing
                var msTesting = entries.SelectMany(e => e.TestingEntries).Where(t => t.FuelType == "MS").Sum(t => t.Amount);
                var hsdTesting = entries.SelectMany(e => e.TestingEntries).Where(t => t.FuelType == "HSD").Sum(t => t.Amount);
                var totalTesting = msTesting + hsdTesting;

                // 6. DSM Shortage
                double totalDsmShort = 0;
                var mismatchGroups = entries.GroupBy(e => new { e.ShiftId, e.DsmName, GroupPumpId = e.ReconciledToPumpId ?? e.PumpId });
                foreach (var g in mismatchGroups)
                {
                    var sumMismatch = g.Sum(e => (double)e.Mismatch);
                    if (sumMismatch < 0) totalDsmShort += Math.Abs(sumMismatch);
                }

                var reconciliationTotalAmount = totalDigitalAndCash + creditorsTotal + expensesTotal + totalTesting + totalDsmShort;
                var grossDaySaleTotal = totalDayFuelSaleAmount;
                var difference = reconciliationTotalAmount - grossDaySaleTotal;

                // Build DSM print rows
                var dsmPrintRows = new List<object>();
                var entriesByShift = entries.GroupBy(e => e.ShiftId);
                foreach (var g in entriesByShift)
                {
                    var shift = shifts.FirstOrDefault(s => s.ShiftId == g.Key);
                    var shiftLabel = shift != null ? $"{shift.ShiftDate:dd/MM} {shift.ShiftType}" : "Unknown";
                    
                    var shiftDsmRows = _aggregation.BuildDsmSummaryRows(g.ToList());
                    foreach (var r in shiftDsmRows)
                    {
                        dsmPrintRows.Add(new
                        {
                            shift = shiftLabel,
                            dsmName = r.DsmName,
                            pumpNo = r.PumpId,
                            phonePe = r.PhonePe,
                            phonePeCardMorning = r.PhonePeCardMorning,
                            phonePeCardNight = r.PhonePeCardNight,
                            phonePeCard = r.PhonePeCardMorning + r.PhonePeCardNight,
                            creditCardMorning = r.CreditCardMorning,
                            creditCardNight = r.CreditCardNight,
                            creditCard = r.CreditCardMorning + r.CreditCardNight,
                            petroCard = r.PetroCard,
                            bankCash = r.CashDeposit,
                            debit = r.Debit,
                            expenses = r.Expenses,
                            testing = r.Testing,
                            cashInHand = r.CashInHand,
                            grossSale = r.GrossSales,
                            mismatch = (r.PhonePe + r.PhonePeCardMorning + r.PhonePeCardNight + r.CreditCardMorning + r.CreditCardNight + r.PetroCard + r.CashDeposit + r.Debit + r.Expenses + r.Testing + r.CashInHand) - r.GrossSales
                        });
                    }
                }

                var payload = new
                {
                    date = SelectedDate.ToString("dd-MM-yyyy"),
                    stationName,
                    totalFuelSaleAmount = totalDayFuelSaleAmount,
                    totalDayLitres,
                    totalHsdLitres,
                    totalMsILitres,
                    totalMsIILitres,
                    totalMsLitres,
                    reconciliationTotalAmount,
                    grossDaySaleTotal,
                    difference,
                    totalDsmShort,
                    creditorsTotal,
                    expensesTotal,
                    phonePeTotal,
                    phonePeCardMorningTotal,
                    phonePeCardNightTotal,
                    creditCardMorningTotal,
                    creditCardNightTotal,
                    petroCardTotal,
                    bankCashTotal,
                    cashInHandTotal,
                    nozzleRows = nozzleRows.Select(r => new
                    {
                        pumpId = r.PumpId,
                        fuelType = r.FuelType,
                        openingReading = r.OpeningReading,
                        closingReading = r.ClosingReading,
                        grossLitres = r.GrossLitres,
                        netSaleLitres = r.NetSaleLitres,
                        rate = r.Rate,
                        amount = r.Amount
                    }).ToList(),
                    dsmEntries = dsmPrintRows,
                    creditors = creditors.Select(c => new
                    {
                        dsmName = c.DsmName,
                        debtorName = c.DebtorName,
                        chequeNo = c.ChequeNo,
                        amount = c.Amount
                    }).ToList(),
                    expenses = expenses.Select(ex => new
                    {
                        dsmName = ex.DsmName,
                        description = ex.Description,
                        amount = ex.Amount
                    }).ToList()
                };

                new PrintService().PrintDayTotal(payload);
                StatusMessage = "DSR Report opened in browser.";
            }
            else if (SelectedReportType == "MonthlyPL")
            {
                // Monthly Profit & Loss Statement
                int monthNum = GetMonthNumber(SelectedMonthName);
                var startDate = new DateTime(SelectedYear, monthNum, 1);
                var endDate = new DateTime(SelectedYear, monthNum, DateTime.DaysInMonth(SelectedYear, monthNum));
                
                var financials = await _financialCalcService.CalculateFinancialsAsync(startDate, endDate);

                // Fetch operational expenses
                var entriesResult = await _dsmRepo.GetEntriesForDateRangeAsync(startDate, endDate);
                var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();
                var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(startDate, endDate);
                var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
                var shiftIds = shifts.Select(s => s.ShiftId).ToList();

                var expensesByCategory = new Dictionary<string, double>();
                foreach (var entry in entries)
                {
                    foreach (var exp in entry.Expenses)
                    {
                        var cat = exp.Description ?? "Other";
                        if (!expensesByCategory.ContainsKey(cat)) expensesByCategory[cat] = 0;
                        expensesByCategory[cat] += exp.Amount;
                    }
                }
                var shiftExpResult = await _expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
                if (shiftExpResult.Success && shiftExpResult.Data != null)
                {
                    foreach (var exp in shiftExpResult.Data)
                    {
                        var cat = exp.Description ?? "Shift Expense";
                        if (!expensesByCategory.ContainsKey(cat)) expensesByCategory[cat] = 0;
                        expensesByCategory[cat] += exp.Amount;
                    }
                }

                var expenseRows = expensesByCategory
                    .OrderByDescending(x => x.Value)
                    .Select(kvp => new { category = kvp.Key, amount = kvp.Value })
                    .ToList();

                var stationName = "PyroSync";
                var s = await _settingsRepo.GetSettingsAsync();
                if (s.Success && s.Data != null) stationName = s.Data.PumpStationName;

                var payload = new
                {
                    startDate = startDate.ToString("dd-MMM-yyyy"),
                    endDate = endDate.ToString("dd-MMM-yyyy"),
                    stationName,
                    fuelProfit = new
                    {
                        hsdLitres = financials.FuelProfit.HsdLitres,
                        hsdMargin = financials.FuelProfit.HsdMargin,
                        hsdProfit = financials.FuelProfit.HsdProfit,
                        msILitres = financials.FuelProfit.MsILitres,
                        msIMargin = financials.FuelProfit.MsIMargin,
                        msIProfit = financials.FuelProfit.MsIProfit,
                        msIILitres = financials.FuelProfit.MsIILitres,
                        msIIMargin = financials.FuelProfit.MsIIMargin,
                        msIIProfit = financials.FuelProfit.MsIIProfit,
                        totalLitres = financials.FuelProfit.TotalLitres,
                        totalFuelProfit = financials.FuelProfit.TotalFuelProfit
                    },
                    oilProfit = new
                    {
                        openingStock = financials.OilProfit.OpeningStock,
                        closingStock = financials.OilProfit.ClosingStock,
                        salesQuantity = financials.OilProfit.SalesQuantity,
                        averagePurchasePrice = financials.OilProfit.AveragePurchasePrice,
                        salePrice = financials.OilProfit.SalePrice,
                        totalProfit = financials.OilProfit.TotalProfit
                    },
                    defProfit = new
                    {
                        openingStock = financials.DefProfit.OpeningStock,
                        closingStock = financials.DefProfit.ClosingStock,
                        salesQuantity = financials.DefProfit.SalesQuantity,
                        averagePurchasePrice = financials.DefProfit.AveragePurchasePrice,
                        salePrice = financials.DefProfit.SalePrice,
                        totalProfit = financials.DefProfit.TotalProfit
                    },
                    expenses = expenseRows,
                    totalExpenses = financials.TotalExpenses,
                    totalDsmSalaries = financials.TotalDsmSalaries,
                    grossProfit = financials.GrossProfit,
                    ownerOuterExpenses = financials.OwnerOuterExpenses,
                    netProfit = financials.NetProfit
                };

                new PrintService().PrintMonthlyPL(payload);
                StatusMessage = "Monthly P&L Statement opened in browser.";
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print report");
            MessageBox.Show($"Print failed: {ex.Message}", "PyroSync — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusMessage = "Print failed.";
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task ExportMonthlyReportAsync()
    {
        IsLoading = true;
        try
        {
            DateTime startDate, endDate;
            if (SelectedReportType == "DSR")
            {
                StatusMessage = "Exporting DSR report to Excel...";
                startDate = SelectedDate.Date;
                endDate = SelectedDate.Date;
            }
            else
            {
                StatusMessage = "Exporting monthly Excel report...";
                int monthNum = GetMonthNumber(SelectedMonthName);
                startDate = new DateTime(SelectedYear, monthNum, 1);
                endDate = new DateTime(SelectedYear, monthNum, DateTime.DaysInMonth(SelectedYear, monthNum));
            }
            
            var result = await _exportService.ExportDailyDataAsync(startDate, endDate);
            StatusMessage = result.Success
                ? $"✅ Report exported: {result.Data}"
                : $"❌ Export failed: {result.Error}";
        }
        catch (System.Exception ex)
        {
            StatusMessage = $"❌ Error: {ex.Message}";
        }
        finally { IsLoading = false; }
    }
}
