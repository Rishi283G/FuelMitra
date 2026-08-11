using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.UI.Printing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using FuelPro.Data;
using System;
using System.Windows;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace FuelPro.UI.ViewModels;

public partial class ProfitLossViewModel : ObservableObject, IDisposable
{
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IShiftRepository _shiftRepo;
    private readonly IExpenseRepository _expenseRepo;
    private readonly ICreditorRepaymentRepository _repaymentRepo;
    private readonly IOwnerCalculationService _ownerCalcService;
    private readonly IFinancialCalculationService _financialCalcService;
    private readonly ITidCalculationService _tidService;
    private readonly IReportService _reportService;
    private readonly FuelPro.Sync.SyncEngine _syncEngine;

    [ObservableProperty] private DateTime _startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _selectedPreset = "Monthly";

    // Legacy support fields for secondary collections info
    [ObservableProperty] private double _totalGrossSales;

    private static bool _savedUsePurchaseBasedProfit;
    public bool UsePurchaseBasedProfit
    {
        get => _savedUsePurchaseBasedProfit;
        set
        {
            if (_savedUsePurchaseBasedProfit != value)
            {
                _savedUsePurchaseBasedProfit = value;
                OnPropertyChanged(nameof(UsePurchaseBasedProfit));
                UpdateCalculatedTotals();
                SelectedProfitTabIndex = value ? 1 : 0;
            }
        }
    }

    private int _selectedProfitTabIndex;
    public int SelectedProfitTabIndex
    {
        get => _selectedProfitTabIndex;
        set
        {
            if (SetProperty(ref _selectedProfitTabIndex, value))
            {
                UsePurchaseBasedProfit = value == 1;
            }
        }
    }

    // Purchase-based rates and profit details
    [ObservableProperty] private double _hsdAvgSaleRate;
    [ObservableProperty] private double _hsdAvgPurchaseRate;
    [ObservableProperty] private double _hsdPurchaseProfit;

    [ObservableProperty] private double _msIAvgSaleRate;
    [ObservableProperty] private double _msIAvgPurchaseRate;
    [ObservableProperty] private double _msIPurchaseProfit;

    [ObservableProperty] private double _msIIAvgSaleRate;
    [ObservableProperty] private double _msIIAvgPurchaseRate;
    [ObservableProperty] private double _msIIPurchaseProfit;

    [ObservableProperty] private double _cngAvgSaleRate;
    [ObservableProperty] private double _cngAvgPurchaseRate;
    [ObservableProperty] private double _cngPurchaseProfit;

    [ObservableProperty] private double _totalFuelPurchaseProfit;

    private double _marginFuelProfit;
    private double _purchaseFuelProfit;
    private double _marginGrossProfit;
    private double _purchaseGrossProfit;
    private double _marginNetProfit;
    private double _purchaseNetProfit;

    // Margin Edit Properties
    [ObservableProperty] private double _editHsdMargin;
    [ObservableProperty] private double _editMsIMargin;
    [ObservableProperty] private double _editMsIIMargin;
    [ObservableProperty] private double _editCngMargin;

    public bool IsCustomRange => SelectedPreset == "Custom";

    partial void OnSelectedPresetChanged(string value) => OnPropertyChanged(nameof(IsCustomRange));

    partial void OnStartDateChanged(DateTime value)
    {
        if (SelectedPreset == "Custom") _ = LoadAsync();
    }

    partial void OnEndDateChanged(DateTime value)
    {
        if (SelectedPreset == "Custom") _ = LoadAsync();
    }
    [ObservableProperty] private double _totalCollection;
    [ObservableProperty] private double _totalCreditorDebits;
    [ObservableProperty] private double _totalCreditorRepayments;
    [ObservableProperty] private double _totalMismatch;

    // Fuel Profit Breakdown
    [ObservableProperty] private double _hsdLitres;
    [ObservableProperty] private double _hsdMargin;
    [ObservableProperty] private double _hsdProfit;

    [ObservableProperty] private double _msILitres;
    [ObservableProperty] private double _msIMargin;
    [ObservableProperty] private double _msIProfit;

    [ObservableProperty] private double _msIILitres;
    [ObservableProperty] private double _msIIMargin;
    [ObservableProperty] private double _msIIProfit;

    [ObservableProperty] private double _cngLitres;
    [ObservableProperty] private double _cngMargin;
    [ObservableProperty] private double _cngProfit;

    [ObservableProperty] private double _totalFuelLitres;
    [ObservableProperty] private double _totalFuelProfit;

    // Oil Inventory Details
    [ObservableProperty] private double _oilSalesQty;
    [ObservableProperty] private double _oilAvgPurchasePrice;
    [ObservableProperty] private double _oilSalePrice;
    [ObservableProperty] private double _oilRevenue;
    [ObservableProperty] private double _oilCost;
    [ObservableProperty] private double _oilProfit;

    // DEF Inventory Details
    [ObservableProperty] private double _defSalesQty;
    [ObservableProperty] private double _defAvgPurchasePrice;
    [ObservableProperty] private double _defSalePrice;
    [ObservableProperty] private double _defRevenue;
    [ObservableProperty] private double _defCost;
    [ObservableProperty] private double _defProfit;

    // Totals for Profit & Loss Statement
    [ObservableProperty] private double _grossProfit;
    [ObservableProperty] private double _totalExpenses;
    [ObservableProperty] private double _totalDsmSalaries;
    [ObservableProperty] private double _dsmBaseSalaries;
    [ObservableProperty] private double _salaryAdjustments;
    [ObservableProperty] private double _shortRecoveries;
    [ObservableProperty] private double _ownerOuterExpenses;
    [ObservableProperty] private double _netProfit;

    // Pump Expenses
    [ObservableProperty] private double _pumpRent;
    [ObservableProperty] private double _pumpSalary;
    [ObservableProperty] private double _pumpTripSheetLoss;
    [ObservableProperty] private double _pumpDsmShort;
    [ObservableProperty] private double _pumpBankingExpenses;
    [ObservableProperty] private double _pumpBpclPortalExpenses;
    [ObservableProperty] private double _pumpFuelAndTravel;
    [ObservableProperty] private double _pumpOilPurchase;
    [ObservableProperty] private double _pumpRepairsAndMaintenance;
    [ObservableProperty] private double _pumpElectricity;
    [ObservableProperty] private double _pumpOfficeExpenses;
    [ObservableProperty] private double _pumpPrintingExpense;
    [ObservableProperty] private double _pumpOtherAmount;
    [ObservableProperty] private double _totalPumpExpenses;

    public ObservableCollection<ExpenseBreakdownRow> ExpenseBreakdown { get; } = new();

    public ProfitLossViewModel()
    {
        _dsmEntryRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _repaymentRepo = App.Services.GetRequiredService<ICreditorRepaymentRepository>();
        _ownerCalcService = App.Services.GetRequiredService<IOwnerCalculationService>();
        _financialCalcService = App.Services.GetRequiredService<IFinancialCalculationService>();
        _tidService = App.Services.GetRequiredService<ITidCalculationService>();
        _reportService = App.Services.GetRequiredService<IReportService>();
        _syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
        
        // Wire Sync Status to trigger auto-reload
        _syncEngine.SyncStatusChanged += OnSyncStatusChanged;

        DsmEntryService.DsmEntryChanged += OnDataChanged;
        DsmEntryService.PettyCashChanged += OnDataChanged;
        DsmEntryService.DebtorChanged += OnDataChanged;
        DsmEntryService.PayrollChanged += OnDataChanged;
        DsmEntryService.InventoryChanged += OnDataChanged;
        DsmEntryService.SettingsChanged += OnDataChanged;

        _ = LoadCurrentMarginsAsync();
        _ = LoadAsync();
    }

    private void OnSyncStatusChanged(FuelPro.Sync.SyncStatusInfo status)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(async () =>
        {
            if (status.StatusMessage == "Synced")
            {
                await LoadCurrentMarginsAsync();
                await LoadAsync();
            }
        });
    }

    private void OnDataChanged()
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await LoadCurrentMarginsAsync();
            await LoadAsync();
        });
    }

    public void Dispose()
    {
        _syncEngine.SyncStatusChanged -= OnSyncStatusChanged;
        DsmEntryService.DsmEntryChanged -= OnDataChanged;
        DsmEntryService.PettyCashChanged -= OnDataChanged;
        DsmEntryService.DebtorChanged -= OnDataChanged;
        DsmEntryService.PayrollChanged -= OnDataChanged;
        DsmEntryService.InventoryChanged -= OnDataChanged;
        DsmEntryService.SettingsChanged -= OnDataChanged;
        GC.SuppressFinalize(this);
    }

    [RelayCommand]
    private void SetPreset(string preset)
    {
        SelectedPreset = preset;
        switch (preset)
        {
            case "Daily":
                StartDate = DateTime.Today;
                EndDate = DateTime.Today;
                break;
            case "Weekly":
                StartDate = DateTime.Today.AddDays(-6);
                EndDate = DateTime.Today;
                break;
            case "Monthly":
                StartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                EndDate = DateTime.Today;
                break;
            case "Custom":
                // Don't change dates, let the user change
                break;
        }
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var result = await _financialCalcService.CalculateFinancialsAsync(StartDate, EndDate);

            HsdLitres = result.FuelProfit.HsdLitres;
            HsdMargin = result.FuelProfit.HsdMargin;
            HsdProfit = result.FuelProfit.HsdProfit;

            MsILitres = result.FuelProfit.MsILitres;
            MsIMargin = result.FuelProfit.MsIMargin;
            MsIProfit = result.FuelProfit.MsIProfit;

            MsIILitres = result.FuelProfit.MsIILitres;
            MsIIMargin = result.FuelProfit.MsIIMargin;
            MsIIProfit = result.FuelProfit.MsIIProfit;

            CngLitres = result.FuelProfit.CngLitres;
            CngMargin = result.FuelProfit.CngMargin;
            CngProfit = result.FuelProfit.CngProfit;

            HsdAvgSaleRate = result.FuelProfit.HsdAvgSaleRate;
            HsdAvgPurchaseRate = result.FuelProfit.HsdAvgPurchaseRate;
            HsdPurchaseProfit = result.FuelProfit.HsdPurchaseProfit;

            MsIAvgSaleRate = result.FuelProfit.MsIAvgSaleRate;
            MsIAvgPurchaseRate = result.FuelProfit.MsIAvgPurchaseRate;
            MsIPurchaseProfit = result.FuelProfit.MsIPurchaseProfit;

            MsIIAvgSaleRate = result.FuelProfit.MsIIAvgSaleRate;
            MsIIAvgPurchaseRate = result.FuelProfit.MsIIAvgPurchaseRate;
            MsIIPurchaseProfit = result.FuelProfit.MsIIPurchaseProfit;

            CngAvgSaleRate = result.FuelProfit.CngAvgSaleRate;
            CngAvgPurchaseRate = result.FuelProfit.CngAvgPurchaseRate;
            CngPurchaseProfit = result.FuelProfit.CngPurchaseProfit;

            TotalFuelPurchaseProfit = result.FuelProfit.TotalPurchaseFuelProfit;

            TotalFuelLitres = result.FuelProfit.TotalLitres;

            OilSalesQty = result.OilProfit.SalesQuantity;
            OilAvgPurchasePrice = result.OilProfit.AveragePurchasePrice;
            OilSalePrice = result.OilProfit.SalePrice;
            OilRevenue = result.OilProfit.SalesRevenue;
            OilCost = result.OilProfit.CostOfGoodsSold;
            OilProfit = result.OilProfit.TotalProfit;

            DefSalesQty = result.DefProfit.SalesQuantity;
            DefAvgPurchasePrice = result.DefProfit.AveragePurchasePrice;
            DefSalePrice = result.DefProfit.SalePrice;
            DefRevenue = result.DefProfit.SalesRevenue;
            DefCost = result.DefProfit.CostOfGoodsSold;
            DefProfit = result.DefProfit.TotalProfit;

            TotalExpenses = result.TotalExpenses;
            TotalDsmSalaries = result.TotalDsmSalaries;
            DsmBaseSalaries = result.DsmBaseSalaries;
            SalaryAdjustments = result.SalaryAdjustments;
            ShortRecoveries = result.ShortRecoveries;
            OwnerOuterExpenses = result.OwnerOuterExpenses;
            
            PumpRent = result.PumpRent;
            PumpSalary = result.PumpSalary;
            PumpTripSheetLoss = result.PumpTripSheetLoss;
            PumpDsmShort = result.PumpDsmShort;
            PumpBankingExpenses = result.PumpBankingExpenses;
            PumpBpclPortalExpenses = result.PumpBpclPortalExpenses;
            PumpFuelAndTravel = result.PumpFuelAndTravel;
            PumpOilPurchase = result.PumpOilPurchase;
            PumpRepairsAndMaintenance = result.PumpRepairsAndMaintenance;
            PumpElectricity = result.PumpElectricity;
            PumpOfficeExpenses = result.PumpOfficeExpenses;
            PumpPrintingExpense = result.PumpPrintingExpense;
            PumpOtherAmount = result.PumpOtherAmount;
            TotalPumpExpenses = result.TotalPumpExpenses;
            TotalMismatch = result.TotalMismatch;

            // Save both versions for runtime toggle
            _marginFuelProfit = result.FuelProfit.TotalFuelProfit;
            _purchaseFuelProfit = result.FuelProfit.TotalPurchaseFuelProfit;

            _marginGrossProfit = result.GrossProfit;
            _purchaseGrossProfit = result.FuelProfit.TotalPurchaseFuelProfit + result.OilProfit.TotalProfit + result.DefProfit.TotalProfit;

            _marginNetProfit = result.NetProfit;
            _purchaseNetProfit = _purchaseGrossProfit - result.TotalExpenses - result.DsmBaseSalaries - result.SalaryAdjustments + result.ShortRecoveries - result.OwnerOuterExpenses - result.TotalPumpExpenses + result.TotalMismatch;
            
            UpdateCalculatedTotals();
            SelectedProfitTabIndex = UsePurchaseBasedProfit ? 1 : 0;

            // Load expense breakdown
            ExpenseBreakdown.Clear();
            var entriesResult = await _dsmEntryRepo.GetEntriesForDateRangeAsync(StartDate, EndDate);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(StartDate, EndDate);
            var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();

            var expensesByCategory = new Dictionary<string, double>();

            double totalKpDrawings = 0;
            foreach (var entry in entries)
            {
                foreach (var exp in entry.Expenses)
                {
                    var cat = exp.Description ?? "Other";
                    if (!expensesByCategory.ContainsKey(cat)) expensesByCategory[cat] = 0;
                    expensesByCategory[cat] += exp.Amount;
                }
                if (entry.KhandharePetroleumEntries != null)
                {
                    totalKpDrawings += entry.KhandharePetroleumEntries.Sum(k => k.Amount);
                }
            }

            if (totalKpDrawings > 0)
            {
                expensesByCategory["Khandhare Petroleum"] = totalKpDrawings;
            }

            var shiftExpResult = await _expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
            var shiftExpensesList = shiftExpResult.Success && shiftExpResult.Data != null ? shiftExpResult.Data : new List<Expense>();
            foreach (var exp in shiftExpensesList)
            {
                var cat = exp.Description ?? "Shift Expense";
                if (!expensesByCategory.ContainsKey(cat)) expensesByCategory[cat] = 0;
                expensesByCategory[cat] += exp.Amount;
            }

            foreach (var kvp in expensesByCategory.OrderByDescending(x => x.Value))
            {
                ExpenseBreakdown.Add(new ExpenseBreakdownRow { Category = kvp.Key, Amount = kvp.Value });
            }

            // Fetch creditor repayments and settings
            var repResult = await _repaymentRepo.GetByDateRangeAsync(StartDate, EndDate);
            var repaymentsList = repResult.Success && repResult.Data != null ? repResult.Data : new List<CreditorRepayment>();

            var settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();
            var settingsResult = await settingsRepo.GetSettingsAsync();
            var settings = settingsResult.Success && settingsResult.Data != null ? settingsResult.Data : new Setting();
            double defaultHsd = settings.HsdRate;
            double defaultMsI = settings.MsIRate;
            double defaultMsII = settings.MsIIRate;
            double defaultCng = settings.CngRate;
            string stationName = settings.StationDisplayName;

            var report = _reportService.CalculateDayReport(
                StartDate.Date, EndDate.Date,
                entries,
                shiftExpensesList,
                repaymentsList,
                defaultHsd, defaultMsI, defaultMsII, defaultCng,
                stationName);

            TotalCreditorRepayments = report.DebtorRepaymentsTotal;
            TotalGrossSales = report.TotalFuelAmount;
            TotalCollection = report.ActualCollection;
            TotalCreditorDebits = report.CreditorsTotal;
            TotalMismatch = report.Difference;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load Profit & Loss statistics");
        }
        finally { IsLoading = false; }
    }

    private readonly PrintService _printService = App.Services.GetRequiredService<PrintService>();

    [RelayCommand]
    private void Print()
    {
        try
        {
            var printData = new
            {
                StartDate = StartDate.ToString("dd-MMM-yyyy"),
                EndDate = EndDate.ToString("dd-MMM-yyyy"),
                FuelProfit = new
                {
                    hsdLitres = HsdLitres,
                    hsdMargin = HsdMargin,
                    hsdProfit = HsdProfit,
                    msILitres = MsILitres,
                    msIMargin = MsIMargin,
                    msIProfit = MsIProfit,
                    msIILitres = MsIILitres,
                    msIIMargin = MsIIMargin,
                    msIIProfit = MsIIProfit,
                    cngLitres = CngLitres,
                    cngMargin = CngMargin,
                    cngProfit = CngProfit,
                    totalLitres = TotalFuelLitres,
                    totalFuelProfit = TotalFuelProfit
                },
                oilProfit = new
                {
                    openingStock = OilSalesQty * 1.2,
                    closingStock = OilSalesQty * 0.2,
                    salesQuantity = OilSalesQty,
                    averagePurchasePrice = OilAvgPurchasePrice,
                    salePrice = OilSalePrice,
                    totalProfit = OilProfit
                },
                defProfit = new
                {
                    openingStock = DefSalesQty * 1.2,
                    closingStock = DefSalesQty * 0.2,
                    salesQuantity = DefSalesQty,
                    averagePurchasePrice = DefAvgPurchasePrice,
                    salePrice = DefSalePrice,
                    totalProfit = DefProfit
                },
                expenses = ExpenseBreakdown.Select(e => new { category = e.Category, amount = e.Amount }).ToList(),
                totalExpenses = TotalExpenses,
                totalDsmSalaries = TotalDsmSalaries,
                grossProfit = GrossProfit,
                ownerOuterExpenses = OwnerOuterExpenses,
                totalMismatch = TotalMismatch,
                netProfit = NetProfit,
                
                // Pump Expenses properties
                pumpRent = PumpRent,
                pumpSalary = PumpSalary,
                pumpTripSheetLoss = PumpTripSheetLoss,
                pumpDsmShort = PumpDsmShort,
                pumpBankingExpenses = PumpBankingExpenses,
                pumpBpclPortalExpenses = PumpBpclPortalExpenses,
                pumpUsedFuel = PumpFuelAndTravel,
                pumpOilPurchase = PumpOilPurchase,
                pumpRepairsAndMaintenance = PumpRepairsAndMaintenance,
                pumpElectricity = PumpElectricity,
                pumpOfficeExpenses = PumpOfficeExpenses,
                pumpPrintingExpense = PumpPrintingExpense,
                pumpOtherAmount = PumpOtherAmount,
                totalPumpExpenses = TotalPumpExpenses
            };

            _printService.PrintMonthlyPL(printData);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print P&L statement");
            MessageBox.Show($"Print failed: {ex.Message}", "Print Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        IsLoading = true;
        try
        {
            var settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();
            var stationName = "PyroSync";
            var s = await settingsRepo.GetSettingsAsync();
            if (s.Success && s.Data != null) stationName = s.Data.PumpStationName;

            var financials = await _financialCalcService.CalculateFinancialsAsync(StartDate, EndDate);
            var expenses = ExpenseBreakdown.Select(e => (e.Category, e.Amount)).ToList();

            var exportService = App.Services.GetRequiredService<ExcelExportService>();
            var filePath = await exportService.ExportMonthlyPLAsync(stationName, StartDate, EndDate, financials, expenses);

            MessageBox.Show($"Report exported successfully to:\n{filePath}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to export P&L to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateCalculatedTotals()
    {
        TotalFuelProfit = UsePurchaseBasedProfit ? _purchaseFuelProfit : _marginFuelProfit;
        GrossProfit = UsePurchaseBasedProfit ? _purchaseGrossProfit : _marginGrossProfit;
        NetProfit = UsePurchaseBasedProfit ? _purchaseNetProfit : _marginNetProfit;
    }

    [RelayCommand]
    public async Task LoadCurrentMarginsAsync()
    {
        try
        {
            var db = App.Services.GetRequiredService<FuelProDbContext>();
            var margins = await db.FuelProfitMargins
                .OrderByDescending(m => m.EffectiveDate)
                .ToListAsync();

            EditHsdMargin = margins.FirstOrDefault(m => string.Equals(m.FuelType, "HSD", StringComparison.OrdinalIgnoreCase))?.MarginPerLitre ?? 3.0;
            EditMsIMargin = margins.FirstOrDefault(m => string.Equals(m.FuelType, "MS-I", StringComparison.OrdinalIgnoreCase))?.MarginPerLitre ?? 4.0;
            EditMsIIMargin = margins.FirstOrDefault(m => string.Equals(m.FuelType, "MS-II", StringComparison.OrdinalIgnoreCase))?.MarginPerLitre ?? 4.0;
            EditCngMargin = margins.FirstOrDefault(m => string.Equals(m.FuelType, "CNG", StringComparison.OrdinalIgnoreCase))?.MarginPerLitre ?? 2.5;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load current fuel margins");
        }
    }

    [RelayCommand]
    private async Task SaveMarginsAsync()
    {
        try
        {
            var db = App.Services.GetRequiredService<FuelProDbContext>();
            var today = DateTime.Today;

            var existingMargins = await db.FuelProfitMargins
                .Where(m => m.EffectiveDate.Date == today)
                .ToListAsync();

            void UpdateOrInsert(string fuelType, double newMargin)
            {
                var existing = existingMargins.FirstOrDefault(m => string.Equals(m.FuelType, fuelType, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.MarginPerLitre = newMargin;
                }
                else
                {
                    db.FuelProfitMargins.Add(new FuelProfitMargin
                    {
                        EffectiveDate = today,
                        FuelType = fuelType,
                        MarginPerLitre = newMargin
                    });
                }
            }

            UpdateOrInsert("HSD", EditHsdMargin);
            UpdateOrInsert("MS-I", EditMsIMargin);
            UpdateOrInsert("MS-II", EditMsIIMargin);
            UpdateOrInsert("CNG", EditCngMargin);

            await db.SaveChangesAsync();
            MessageBox.Show("Fuel profit margins updated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);

            await LoadAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save fuel profit margins");
            MessageBox.Show($"Failed to save margins: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}


public class ExpenseBreakdownRow
{
    public string Category { get; set; } = "";
    public double Amount { get; set; }
}
