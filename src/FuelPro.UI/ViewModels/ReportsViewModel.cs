using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;
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
    private readonly ITidCalculationService _tidService;
    private readonly IReportService _reportService;

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
        _tidService = App.Services.GetRequiredService<ITidCalculationService>();
        _reportService = App.Services.GetRequiredService<IReportService>();

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
                var entriesResult = await _dsmRepo.GetEntriesForDateRangeAsync(SelectedDate, SelectedDate);
                var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

                if (entries.Count == 0)
                {
                    MessageBox.Show("No entry data found for the selected date.", "PyroSync — Report", MessageBoxButton.OK, MessageBoxImage.Information);
                    StatusMessage = "No data found.";
                    return;
                }

                var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(SelectedDate, SelectedDate);
                var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
                var shiftIds = shifts.Select(s => s.ShiftId).ToList();

                var expResult = await _expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
                var allExpenses = expResult.Success && expResult.Data != null ? expResult.Data : new List<Expense>();

                var repaymentsRes = await App.Services.GetRequiredService<ICreditorRepaymentRepository>().GetByDateRangeAsync(SelectedDate.Date, SelectedDate.Date);
                var repayments = repaymentsRes.Success && repaymentsRes.Data != null ? repaymentsRes.Data : new List<CreditorRepayment>();

                var settings = await _settingsRepo.GetSettingsAsync();
                double defaultHsd = settings.Success ? settings.Data!.HsdRate : 90.35;
                double defaultMsI = settings.Success ? settings.Data!.MsIRate : 103.81;
                double defaultMsII = settings.Success ? settings.Data!.MsIIRate : 103.81;
                double defaultCng = settings.Success ? settings.Data!.CngRate : 85.0;
                string stationName = settings.Success ? settings.Data!.PumpStationName : "PyroSync";

                var report = _reportService.CalculateDayReport(
                    SelectedDate,
                    SelectedDate,
                    entries,
                    allExpenses,
                    repayments,
                    defaultHsd, defaultMsI, defaultMsII, defaultCng,
                    stationName);

                // Load AGS Nozzle readings for the day range
                try
                {
                    var agsRepo = App.Services.GetRequiredService<IAgsImportRepository>();
                    var dayShifts = new List<AgsShiftImport>();
                    var shiftsRes = await agsRepo.GetShiftsForDateAsync(SelectedDate.Date);
                    if (shiftsRes.Success && shiftsRes.Data != null)
                    {
                        dayShifts.AddRange(shiftsRes.Data.Where(s => s.IsActive));
                    }
                    var nozzleGroupsList = await BuildNozzleGroupsForDayAsync(SelectedDate.Date, dayShifts, entries);
                    report.TankSummary = nozzleGroupsList;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Failed to load AGS nozzle groups for ReportsViewModel print");
                    var inventoryService = App.Services.GetRequiredService<IAgsInventoryService>();
                    var fallbackGroups = await inventoryService.BuildNozzleGroupsAsync(SelectedDate.Date, "B", null);
                    report.TankSummary = fallbackGroups;
                }

                new PrintService().PrintDayTotal(report);
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
                    totalMismatch = financials.TotalMismatch,
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

    private async Task<List<NozzleGroupDto>> BuildNozzleGroupsForDayAsync(DateTime selectedDate, List<AgsShiftImport> dayShifts, List<DsmEntry> entries)
    {
        var groups = new List<NozzleGroupDto>();
        
        var sortedShifts = dayShifts?.OrderBy(s => s.ImportDate).ThenBy(s => s.ShiftType == "A").ToList() ?? new List<AgsShiftImport>();
        var lastShift = sortedShifts.LastOrDefault();

        var hsdTank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD");
        var msITank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I");
        var msIITank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II");

        // Fetch previous shift closings/dips before SelectedDate
        var agsRepo = App.Services.GetRequiredService<IAgsImportRepository>();
        var prevDate = selectedDate.Date.AddDays(-1);
        var prevShift = "A";
        var prevImportRes = await agsRepo.GetActiveShiftImportAsync(prevDate, prevShift);
        var prevImport = (prevImportRes.Success && prevImportRes.Data != null) ? prevImportRes.Data : null;

        var prevHsdClosingStock = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD")?.ClosingStockLitres ?? 0.0;
        var prevMsIClosingStock = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I")?.ClosingStockLitres ?? 0.0;
        var prevMsIIClosingStock = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II")?.ClosingStockLitres ?? 0.0;

        var hsdOpeningStock = prevHsdClosingStock > 0 ? prevHsdClosingStock : (sortedShifts.FirstOrDefault()?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD")?.OpeningStockLitres ?? 0.0);
        var msIOpeningStock = prevMsIClosingStock > 0 ? prevMsIClosingStock : (sortedShifts.FirstOrDefault()?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I")?.OpeningStockLitres ?? 0.0);
        var msIIOpeningStock = prevMsIIClosingStock > 0 ? prevMsIIClosingStock : (sortedShifts.FirstOrDefault()?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II")?.OpeningStockLitres ?? 0.0);

        // Aggregate manual readings from entries
        var manualReadings = new Dictionary<int, (double Opening, double Closing, double Sale)>();
        if (entries != null)
        {
            foreach (var group in entries.SelectMany(e => e.NozzleReadings).GroupBy(r => r.NozzleNumber))
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

        if (entries != null)
        {
            foreach (var entry in entries)
            {
                foreach (var t in entry.TestingEntries)
                {
                    var cat = FuelPro.Core.Common.PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, entry.Shift?.ShiftDate ?? selectedDate.Date);
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
