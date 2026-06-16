using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace FuelPro.UI.ViewModels;

public partial class ProfitLossViewModel : ObservableObject
{
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IShiftRepository _shiftRepo;
    private readonly IExpenseRepository _expenseRepo;
    private readonly ICreditorRepaymentRepository _repaymentRepo;
    private readonly IOwnerCalculationService _ownerCalcService;
    private readonly IFinancialCalculationService _financialCalcService;

    [ObservableProperty] private DateTime _startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _selectedPreset = "Monthly";

    // Legacy support fields for secondary collections info
    [ObservableProperty] private double _totalGrossSales;

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
    [ObservableProperty] private double _netProfit;

    public ObservableCollection<ExpenseBreakdownRow> ExpenseBreakdown { get; } = new();

    public ProfitLossViewModel()
    {
        _dsmEntryRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _repaymentRepo = App.Services.GetRequiredService<ICreditorRepaymentRepository>();
        _ownerCalcService = App.Services.GetRequiredService<IOwnerCalculationService>();
        _financialCalcService = App.Services.GetRequiredService<IFinancialCalculationService>();
        _ = LoadAsync();
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

            TotalFuelLitres = result.FuelProfit.TotalLitres;
            TotalFuelProfit = result.FuelProfit.TotalFuelProfit;

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

            GrossProfit = result.GrossProfit;
            TotalExpenses = result.TotalExpenses;
            TotalDsmSalaries = result.TotalDsmSalaries;
            DsmBaseSalaries = result.DsmBaseSalaries;
            SalaryAdjustments = result.SalaryAdjustments;
            ShortRecoveries = result.ShortRecoveries;
            NetProfit = result.NetProfit;

            // Load expense breakdown
            ExpenseBreakdown.Clear();
            var entriesResult = await _dsmEntryRepo.GetEntriesForDateRangeAsync(StartDate, EndDate);
            var entries = entriesResult.Success && entriesResult.Data != null ? entriesResult.Data : new List<DsmEntry>();

            var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(StartDate, EndDate);
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

            // Fetch creditor repayments
            var repResult = await _repaymentRepo.GetByDateRangeAsync(StartDate, EndDate);
            TotalCreditorRepayments = repResult.Success && repResult.Data != null
                ? (double)repResult.Data.Sum(r => r.Amount)
                : 0;

            var calculationData = _ownerCalcService.Calculate(entries, shiftExpensesList, Array.Empty<ShiftOtherCash>());
            TotalGrossSales = calculationData.GrossSales;
            TotalCollection = calculationData.AdjustedCollection;
            TotalCreditorDebits = calculationData.Debit;
            TotalMismatch = calculationData.Mismatch;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load Profit & Loss statistics");
        }
        finally { IsLoading = false; }
    }
}

public class ExpenseBreakdownRow
{
    public string Category { get; set; } = "";
    public double Amount { get; set; }
}
