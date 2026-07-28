using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

public partial class ExpenseAnalysisViewModel : ObservableObject, IDisposable
{
    private readonly IShiftRepository _shiftRepo;
    private readonly IDsmEntryRepository _dsmEntryRepo;
    private readonly IExpenseRepository _expenseRepo;
    private readonly PrintService _printService;
    private readonly ExcelExportService _excelExportService;

    [ObservableProperty] private DateTime _startDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    [ObservableProperty] private DateTime _endDate = DateTime.Today;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private double _totalExpensesAmount;

    public ObservableCollection<ExpenseItemRow> ItemizedExpenses { get; } = new();
    public ObservableCollection<ExpenseCategoryGroupRow> GroupedExpenses { get; } = new();

    private List<ExpenseItemRow> _allLoadedExpenses = new();

    public ExpenseAnalysisViewModel()
    {
        _shiftRepo = App.Services.GetRequiredService<IShiftRepository>();
        _dsmEntryRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        _expenseRepo = App.Services.GetRequiredService<IExpenseRepository>();
        _printService = App.Services.GetRequiredService<PrintService>();
        _excelExportService = App.Services.GetRequiredService<ExcelExportService>();

        DsmEntryService.DsmEntryChanged += OnDataChanged;
        DsmEntryService.PettyCashChanged += OnDataChanged;

        _ = LoadAsync();
    }

    private void OnDataChanged()
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () => await LoadAsync());
    }

    public void Dispose()
    {
        DsmEntryService.DsmEntryChanged -= OnDataChanged;
        DsmEntryService.PettyCashChanged -= OnDataChanged;
        GC.SuppressFinalize(this);
    }

    partial void OnStartDateChanged(DateTime value) => _ = LoadAsync();
    partial void OnEndDateChanged(DateTime value) => _ = LoadAsync();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var shiftsResult = await _shiftRepo.GetShiftsByDateRangeAsync(StartDate, EndDate);
            var shifts = shiftsResult.Success && shiftsResult.Data != null ? shiftsResult.Data : new List<Shift>();
            var shiftIds = shifts.Select(s => s.ShiftId).ToList();

            var loaded = new List<ExpenseItemRow>();

            // 1. Get shift-level expenses
            var shiftExpensesResult = await _expenseRepo.GetExpensesByShiftIdsAsync(shiftIds);
            if (shiftExpensesResult.Success && shiftExpensesResult.Data != null)
            {
                foreach (var exp in shiftExpensesResult.Data)
                {
                    var shift = shifts.FirstOrDefault(s => s.ShiftId == exp.ShiftId);
                    loaded.Add(new ExpenseItemRow
                    {
                        ExpenseId = exp.ExpenseId,
                        Date = shift?.ShiftDate ?? DateTime.Today,
                        ShiftType = shift?.ShiftType ?? "—",
                        Source = "Shift Level",
                        Description = exp.Description,
                        Amount = exp.Amount
                    });
                }
            }

            // 2. Get DSM-entry-level expenses
            var entriesResult = await _dsmEntryRepo.GetEntriesForDateRangeAsync(StartDate, EndDate);
            if (entriesResult.Success && entriesResult.Data != null)
            {
                foreach (var entry in entriesResult.Data)
                {
                    foreach (var exp in entry.Expenses)
                    {
                        loaded.Add(new ExpenseItemRow
                        {
                            ExpenseId = exp.ExpenseId,
                            Date = entry.Shift?.ShiftDate ?? DateTime.Today,
                            ShiftType = entry.Shift?.ShiftType ?? "—",
                            Source = $"DSM: {entry.DsmName}",
                            Description = exp.Description,
                            Amount = exp.Amount
                        });
                    }
                    if (entry.KhandharePetroleumEntries != null)
                    {
                        foreach (var kp in entry.KhandharePetroleumEntries)
                        {
                            loaded.Add(new ExpenseItemRow
                            {
                                ExpenseId = kp.Id,
                                Date = kp.Date,
                                ShiftType = entry.Shift?.ShiftType ?? "—",
                                Source = $"DSM: {entry.DsmName} (Khandhare Draw)",
                                Description = $"Khandhare Petroleum Drawing: {kp.Name} (Slip: {kp.SlipNumber})",
                                Amount = kp.Amount
                            });
                        }
                    }
                }
            }

            _allLoadedExpenses = loaded.OrderByDescending(e => e.Date).ThenBy(e => e.ShiftType).ToList();
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        ItemizedExpenses.Clear();
        GroupedExpenses.Clear();

        var query = _allLoadedExpenses.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(e => e.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase) || 
                                     e.Source.Contains(SearchText, StringComparison.OrdinalIgnoreCase));
        }

        var filteredList = query.ToList();
        foreach (var item in filteredList)
        {
            ItemizedExpenses.Add(item);
        }

        TotalExpensesAmount = filteredList.Sum(e => e.Amount);

        // Group by Description (acts as category)
        var grouped = filteredList
            .GroupBy(e => e.Description.Trim())
            .Select(g => new ExpenseCategoryGroupRow
            {
                Category = g.Key,
                Amount = g.Sum(e => e.Amount),
                Count = g.Count()
            })
            .OrderByDescending(g => g.Amount)
            .ToList();

        foreach (var group in grouped)
        {
            GroupedExpenses.Add(group);
        }
    }

    [RelayCommand]
    private void Print()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Expenses", Value = "₹" + TotalExpensesAmount.ToString("N2"), Highlight = true }
            };

            var headers = new List<string> { "Date", "Shift", "Source", "Description / Purpose", "Amount" };
            var rows = new List<List<string>>();

            foreach (var row in ItemizedExpenses)
            {
                rows.Add(new List<string>
                {
                    row.Date.ToString("dd-MMM-yyyy"),
                    row.ShiftType,
                    row.Source,
                    row.Description,
                    "₹" + row.Amount.ToString("N2")
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Expense Analysis Report",
                Subtitle = $"Date Range: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}" + 
                           (string.IsNullOrWhiteSpace(SearchText) ? "" : $" (Filtered by: '{SearchText}')"),
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            _printService.PrintGenericGrid(printData);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print Expense Analysis report");
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
                new() { Label = "Total Expenses", Value = "₹" + TotalExpensesAmount.ToString("N2"), Highlight = true }
            };

            var headers = new List<string> { "Date", "Shift", "Source", "Description / Purpose", "Amount" };
            var rows = new List<List<string>>();

            foreach (var row in ItemizedExpenses)
            {
                rows.Add(new List<string>
                {
                    row.Date.ToString("dd-MMM-yyyy"),
                    row.ShiftType,
                    row.Source,
                    row.Description,
                    "₹" + row.Amount.ToString("N2")
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "Expense Analysis Report",
                Subtitle = $"Date Range: {StartDate:dd-MMM-yyyy} to {EndDate:dd-MMM-yyyy}" + 
                           (string.IsNullOrWhiteSpace(SearchText) ? "" : $" (Filtered by: '{SearchText}')"),
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var path = await _excelExportService.ExportGenericGridAsync(printData, "ExpenseAnalysis");
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to export Expense Analysis to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

public class ExpenseItemRow
{
    public int ExpenseId { get; set; }
    public DateTime Date { get; set; }
    public string ShiftType { get; set; } = "";
    public string Source { get; set; } = "";
    public string Description { get; set; } = "";
    public double Amount { get; set; }
}

public class ExpenseCategoryGroupRow
{
    public string Category { get; set; } = "";
    public double Amount { get; set; }
    public int Count { get; set; }
}
