using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.UI.Printing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FuelPro.UI.ViewModels;

public partial class PumpExpensesViewModel : ObservableObject
{
    private readonly IPumpExpenseRepository _pumpExpenseRepo;
    private readonly FuelProDbContext _dbContext;
    private readonly PrintService _printService;
    private readonly ExcelExportService _excelExportService;

    [ObservableProperty] private DateTime _expenseDate = DateTime.Today;
    
    // Inputs
    [ObservableProperty] private double _rent;
    [ObservableProperty] private double _salary;
    [ObservableProperty] private double _tripSheetLoss;
    [ObservableProperty] private double _dsmShort;
    [ObservableProperty] private double _bankingExpenses;
    [ObservableProperty] private double _bpclPortalExpenses;
    [ObservableProperty] private double _fuelAndTravel;
    [ObservableProperty] private double _oilPurchase;
    [ObservableProperty] private double _repairsAndMaintenance;
    [ObservableProperty] private double _electricityExpenses;
    [ObservableProperty] private double _officeExpenses;
    [ObservableProperty] private double _printingExpense;
    [ObservableProperty] private string _remarks = "";

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private string _newCategoryName = "";

    // Range selector for history
    [ObservableProperty] private DateTime _historyStartDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _historyEndDate = DateTime.Today;

    [ObservableProperty] private double _totalKpAmount;
    [ObservableProperty] private double _totalPumpExpensesAmount;
    [ObservableProperty] private double _grandTotalExpensesAmount;

    public ObservableCollection<PumpExpense> HistoryExpenses { get; } = new();
    public ObservableCollection<CategoryExpenseItemViewModel> CategoryExpenses { get; } = new();
    public ObservableCollection<KhandharePetroleumEntry> HistoryKpEntries { get; } = new();

    public PumpExpensesViewModel()
    {
        _pumpExpenseRepo = App.Services.GetRequiredService<IPumpExpenseRepository>();
        _dbContext = App.Services.GetRequiredService<FuelProDbContext>();
        _printService = App.Services.GetRequiredService<PrintService>();
        _excelExportService = App.Services.GetRequiredService<ExcelExportService>();
        
        _ = LoadAsync();
    }

    partial void OnExpenseDateChanged(DateTime value) => _ = LoadExpenseForDateAsync();
    partial void OnHistoryStartDateChanged(DateTime value) => _ = LoadHistoryAsync();
    partial void OnHistoryEndDateChanged(DateTime value) => _ = LoadHistoryAsync();

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            await LoadExpenseForDateAsync();
            await LoadHistoryAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadExpenseForDateAsync()
    {
        StatusMessage = "";
        
        // Load active categories
        var activeCategories = await _dbContext.ExpenseCategories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync();

        CategoryExpenses.Clear();

        var result = await _pumpExpenseRepo.GetByDateAsync(ExpenseDate);
        if (result.Success && result.Data != null)
        {
            var exp = result.Data;
            Rent = exp.Rent;
            Salary = exp.Salary;
            TripSheetLoss = exp.TripSheetLoss;
            DsmShort = exp.DsmShort;
            BankingExpenses = exp.BankingExpenses;
            BpclPortalExpenses = exp.BpclPortalExpenses;
            FuelAndTravel = exp.FuelAndTravel;
            OilPurchase = exp.OilPurchase;
            RepairsAndMaintenance = exp.RepairsAndMaintenance;
            ElectricityExpenses = exp.ElectricityExpenses;
            OfficeExpenses = exp.OfficeExpenses;
            PrintingExpense = exp.PrintingExpense;
            Remarks = exp.Remarks;

            // Load existing items for this PumpExpense
            var items = await _dbContext.PumpExpenseCategoryItems
                .Where(i => i.PumpExpenseId == exp.Id)
                .ToListAsync();

            foreach (var cat in activeCategories)
            {
                var existingItem = items.FirstOrDefault(i => i.CategoryId == cat.Id);
                CategoryExpenses.Add(new CategoryExpenseItemViewModel
                {
                    CategoryId = cat.Id,
                    CategoryName = cat.Name,
                    Amount = existingItem?.Amount ?? 0,
                    Remarks = existingItem?.Remarks ?? ""
                });
            }
        }
        else
        {
            Rent = 0;
            Salary = 0;
            TripSheetLoss = 0;
            DsmShort = 0;
            BankingExpenses = 0;
            BpclPortalExpenses = 0;
            FuelAndTravel = 0;
            OilPurchase = 0;
            RepairsAndMaintenance = 0;
            ElectricityExpenses = 0;
            OfficeExpenses = 0;
            PrintingExpense = 0;
            Remarks = "";

            foreach (var cat in activeCategories)
            {
                CategoryExpenses.Add(new CategoryExpenseItemViewModel
                {
                    CategoryId = cat.Id,
                    CategoryName = cat.Name,
                    Amount = 0,
                    Remarks = ""
                });
            }
        }
    }

    private async Task LoadHistoryAsync()
    {
        var result = await _pumpExpenseRepo.GetByDateRangeAsync(HistoryStartDate, HistoryEndDate);
        HistoryExpenses.Clear();
        double pumpExpSum = 0;
        if (result.Success && result.Data != null)
        {
            foreach (var item in result.Data.OrderByDescending(e => e.ExpenseDate))
            {
                HistoryExpenses.Add(item);
                pumpExpSum += (item.Rent + item.Salary + item.TripSheetLoss + item.DsmShort + 
                               item.BankingExpenses + item.BpclPortalExpenses + item.FuelAndTravel + 
                               item.OilPurchase + item.RepairsAndMaintenance + item.ElectricityExpenses + 
                               item.OfficeExpenses + item.PrintingExpense + item.OtherAmount);
            }
        }
        TotalPumpExpensesAmount = pumpExpSum;

        // Load Kandhare Petroleum Ledger Entries for selected date range
        try
        {
            var startDate = HistoryStartDate.Date;
            var endDate = HistoryEndDate.Date.AddDays(1).AddTicks(-1);
            var kpEntries = await _dbContext.KhandharePetroleumEntries
                .AsNoTracking()
                .Where(kp => kp.Date >= startDate && kp.Date <= endDate)
                .OrderByDescending(kp => kp.Date)
                .ThenByDescending(kp => kp.Id)
                .ToListAsync();

            HistoryKpEntries.Clear();
            double sum = 0;
            foreach (var kp in kpEntries)
            {
                HistoryKpEntries.Add(kp);
                sum += kp.Amount;
            }
            TotalKpAmount = sum;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load KP history: {ex.Message}");
        }

        GrandTotalExpensesAmount = TotalPumpExpensesAmount + TotalKpAmount;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        StatusMessage = "Saving...";
        try
        {
            // Verify DayLock first
            var dayLockService = App.Services.GetRequiredService<IDayLockService>();
            var isLocked = await dayLockService.IsDateLockedAsync(ExpenseDate);
            var authService = App.Services.GetRequiredService<AuthService>();
            var currentUser = authService.CurrentUser;
            var isOwner = currentUser?.Role == "Owner";

            if (isLocked && !isOwner)
            {
                MessageBox.Show("This date has been locked by the Owner. Modifications are not allowed.", 
                    "Day Locked", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusMessage = "❌ Save failed: Date is locked.";
                return;
            }

            // Calculate aggregate other fields
            var nonZeroItems = CategoryExpenses.Where(c => c.Amount > 0).ToList();
            double totalOtherAmount = nonZeroItems.Sum(c => c.Amount);
            string otherDesc = string.Join(", ", nonZeroItems.Select(c => $"{c.CategoryName}: {c.Amount}"));

            var exp = new PumpExpense
            {
                ExpenseDate = ExpenseDate.Date,
                Rent = Rent,
                Salary = Salary,
                TripSheetLoss = TripSheetLoss,
                DsmShort = DsmShort,
                BankingExpenses = BankingExpenses,
                BpclPortalExpenses = BpclPortalExpenses,
                FuelAndTravel = FuelAndTravel,
                OilPurchase = OilPurchase,
                RepairsAndMaintenance = RepairsAndMaintenance,
                ElectricityExpenses = ElectricityExpenses,
                OfficeExpenses = OfficeExpenses,
                PrintingExpense = PrintingExpense,
                OtherDescription = otherDesc,
                OtherAmount = totalOtherAmount,
                Remarks = Remarks ?? ""
            };

            var result = await _pumpExpenseRepo.AddOrUpdateAsync(exp);
            if (result.Success)
            {
                var savedExp = result.Data;

                // Update category items in database
                var existingItems = await _dbContext.PumpExpenseCategoryItems
                    .Where(i => i.PumpExpenseId == savedExp.Id)
                    .ToListAsync();

                foreach (var itemVm in CategoryExpenses)
                {
                    var existingItem = existingItems.FirstOrDefault(i => i.CategoryId == itemVm.CategoryId);

                    if (itemVm.Amount > 0)
                    {
                        if (existingItem != null)
                        {
                            existingItem.Amount = itemVm.Amount;
                            existingItem.Remarks = itemVm.Remarks ?? "";
                        }
                        else
                        {
                            _dbContext.PumpExpenseCategoryItems.Add(new PumpExpenseCategoryItem
                            {
                                PumpExpenseId = savedExp.Id,
                                CategoryId = itemVm.CategoryId,
                                Amount = itemVm.Amount,
                                Remarks = itemVm.Remarks ?? "",
                                CreatedAt = DateTime.Now
                            });
                        }
                    }
                    else if (existingItem != null)
                    {
                        _dbContext.PumpExpenseCategoryItems.Remove(existingItem);
                    }
                }

                await _dbContext.SaveChangesAsync();

                // Log changes in Audit Log
                var auditLogService = App.Services.GetRequiredService<IAuditLogService>();
                var username = currentUser?.Username ?? "Unknown";
                await auditLogService.LogAsync("PumpExpenses", savedExp.Id, "Save", null, null, $"Total: {savedExp.Rent + savedExp.Salary + savedExp.OtherAmount}", username);

                StatusMessage = "✅ Saved successfully!";
                await LoadHistoryAsync();
            }
            else
            {
                StatusMessage = $"❌ Save failed: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(PumpExpense? exp)
    {
        if (exp == null) return;

        // Verify DayLock
        var dayLockService = App.Services.GetRequiredService<IDayLockService>();
        var isLocked = await dayLockService.IsDateLockedAsync(exp.ExpenseDate);
        var authService = App.Services.GetRequiredService<AuthService>();
        var currentUser = authService.CurrentUser;
        var isOwner = currentUser?.Role == "Owner";

        if (isLocked && !isOwner)
        {
            MessageBox.Show("This date has been locked by the Owner. Modifications are not allowed.", 
                "Day Locked", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Are you sure you want to delete the pump expenses logged for {exp.ExpenseDate:dd-MMM-yyyy}?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        
        if (confirm != MessageBoxResult.Yes) return;

        var result = await _pumpExpenseRepo.DeleteAsync(exp.Id);
        if (result.Success)
        {
            // Log in Audit Log
            var auditLogService = App.Services.GetRequiredService<IAuditLogService>();
            var username = currentUser?.Username ?? "Unknown";
            await auditLogService.LogAsync("PumpExpenses", exp.Id, "Delete", null, $"Total: {exp.Rent + exp.Salary + exp.OtherAmount}", null, username);

            StatusMessage = "✅ Log deleted successfully.";
            await LoadAsync();
        }
        else
        {
            StatusMessage = $"❌ Delete failed: {result.Error}";
        }
    }

    [RelayCommand]
    private async Task AddCategoryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName)) return;

        var name = NewCategoryName.Trim();
        var exists = await _dbContext.ExpenseCategories.AnyAsync(c => c.Name.ToLower() == name.ToLower());
        if (exists)
        {
            MessageBox.Show($"Category '{name}' already exists.", "Add Category", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var category = new ExpenseCategory
        {
            Name = name,
            IsActive = true,
            CreatedAt = DateTime.Now
        };

        _dbContext.ExpenseCategories.Add(category);
        await _dbContext.SaveChangesAsync();

        NewCategoryName = "";
        
        // Reload list for the current selected date
        await LoadExpenseForDateAsync();
    }

    [RelayCommand]
    private void Edit(PumpExpense? exp)
    {
        if (exp == null) return;
        ExpenseDate = exp.ExpenseDate; // This triggers OnExpenseDateChanged and loads data automatically
    }

    [RelayCommand]
    private void Print()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Rent", Value = "₹" + HistoryExpenses.Sum(e => e.Rent).ToString("N2") },
                new() { Label = "Total Staff Salary", Value = "₹" + HistoryExpenses.Sum(e => e.Salary).ToString("N2") },
                new() { Label = "Total Trip Sheet Loss", Value = "₹" + HistoryExpenses.Sum(e => e.TripSheetLoss).ToString("N2") },
                new() { Label = "Total DSM Short", Value = "₹" + HistoryExpenses.Sum(e => e.DsmShort).ToString("N2") },
                new() { Label = "Total Pump Expenses", Value = "₹" + HistoryExpenses.Sum(e => e.Rent + e.Salary + e.TripSheetLoss + e.DsmShort + e.BankingExpenses + e.BpclPortalExpenses + e.FuelAndTravel + e.OilPurchase + e.RepairsAndMaintenance + e.ElectricityExpenses + e.OfficeExpenses + e.PrintingExpense + e.OtherAmount).ToString("N2"), Highlight = true }
            };

            var headers = new List<string> { "Date", "Rent", "Staff Salary", "Trip Sheet Loss", "DSM Short", "Banking", "BPCL Portal", "Fuel & Travel", "Oil Purchase", "Repairs & Maint.", "Electricity", "Office Exp.", "Printing", "Other Desc", "Other Amt", "Remarks" };
            var rows = new List<List<string>>();

            foreach (var e in HistoryExpenses.OrderBy(x => x.ExpenseDate))
            {
                rows.Add(new List<string>
                {
                    e.ExpenseDate.ToString("dd-MMM-yyyy"),
                    "₹" + e.Rent.ToString("N2"),
                    "₹" + e.Salary.ToString("N2"),
                    "₹" + e.TripSheetLoss.ToString("N2"),
                    "₹" + e.DsmShort.ToString("N2"),
                    "₹" + e.BankingExpenses.ToString("N2"),
                    "₹" + e.BpclPortalExpenses.ToString("N2"),
                    "₹" + e.FuelAndTravel.ToString("N2"),
                    "₹" + e.OilPurchase.ToString("N2"),
                    "₹" + e.RepairsAndMaintenance.ToString("N2"),
                    "₹" + e.ElectricityExpenses.ToString("N2"),
                    "₹" + e.OfficeExpenses.ToString("N2"),
                    "₹" + e.PrintingExpense.ToString("N2"),
                    e.OtherDescription,
                    "₹" + e.OtherAmount.ToString("N2"),
                    e.Remarks
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Pump Expenses Log Report",
                Subtitle = $"Period: {HistoryStartDate:dd-MMM-yyyy} to {HistoryEndDate:dd-MMM-yyyy}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            _printService.PrintGenericGrid(printData);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print pump expenses history");
            MessageBox.Show($"Print failed: {ex.Message}", "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Expenses Invoiced", Value = "₹" + HistoryExpenses.Sum(e => e.Rent + e.Salary + e.TripSheetLoss + e.DsmShort + e.BankingExpenses + e.BpclPortalExpenses + e.FuelAndTravel + e.OilPurchase + e.RepairsAndMaintenance + e.ElectricityExpenses + e.OfficeExpenses + e.PrintingExpense + e.OtherAmount).ToString("N2"), Highlight = true }
            };

            var headers = new List<string> { "Date", "Rent", "Staff Salary", "Trip Sheet Loss", "DSM Short", "Banking", "BPCL Portal", "Fuel & Travel", "Oil Purchase", "Repairs & Maint.", "Electricity", "Office Exp.", "Printing", "Other Desc", "Other Amt", "Remarks" };
            var rows = new List<List<string>>();

            foreach (var e in HistoryExpenses.OrderBy(x => x.ExpenseDate))
            {
                rows.Add(new List<string>
                {
                    e.ExpenseDate.ToString("dd-MMM-yyyy"),
                    "₹" + e.Rent.ToString("N2"),
                    "₹" + e.Salary.ToString("N2"),
                    "₹" + e.TripSheetLoss.ToString("N2"),
                    "₹" + e.DsmShort.ToString("N2"),
                    "₹" + e.BankingExpenses.ToString("N2"),
                    "₹" + e.BpclPortalExpenses.ToString("N2"),
                    "₹" + e.FuelAndTravel.ToString("N2"),
                    "₹" + e.OilPurchase.ToString("N2"),
                    "₹" + e.RepairsAndMaintenance.ToString("N2"),
                    "₹" + e.ElectricityExpenses.ToString("N2"),
                    "₹" + e.OfficeExpenses.ToString("N2"),
                    "₹" + e.PrintingExpense.ToString("N2"),
                    e.OtherDescription,
                    "₹" + e.OtherAmount.ToString("N2"),
                    e.Remarks
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Pump Expenses Log Report",
                Subtitle = $"Period: {HistoryStartDate:dd-MMM-yyyy} to {HistoryEndDate:dd-MMM-yyyy}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var path = await _excelExportService.ExportGenericGridAsync(printData, "PumpExpenses");
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to export pump expenses to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public class CategoryExpenseItemViewModel : ObservableObject
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    
    private double _amount;
    public double Amount
    {
        get => _amount;
        set => SetProperty(ref _amount, value);
    }

    private string _remarks = string.Empty;
    public string Remarks
    {
        get => _remarks;
        set => SetProperty(ref _remarks, value);
    }
}
