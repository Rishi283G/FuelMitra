using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FuelPro.UI.ViewModels;

public partial class OuterExpensesViewModel : ObservableObject
{
    private readonly FuelProDbContext _dbContext;

    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private double _amount;
    [ObservableProperty] private DateTime _expenseDate = DateTime.Today;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private OuterExpense? _editingExpense;

    public bool IsEditing => EditingExpense != null;

    public ObservableCollection<OuterExpense> HistoryExpenses { get; } = new();

    public OuterExpensesViewModel(FuelProDbContext dbContext)
    {
        _dbContext = dbContext;
        _ = LoadExpensesAsync();
    }

    [RelayCommand]
    public async Task LoadExpensesAsync()
    {
        try
        {
            HistoryExpenses.Clear();
            var list = await _dbContext.OuterExpenses
                .OrderByDescending(e => e.ExpenseDate)
                .ThenByDescending(e => e.CreatedAt)
                .ToListAsync();
            foreach (var item in list)
            {
                HistoryExpenses.Add(item);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load outer expenses");
        }
    }

    [RelayCommand]
    private async Task SaveExpenseAsync()
    {
        if (string.IsNullOrWhiteSpace(Description))
        {
            MessageBox.Show("Description is required.", "PyroSync — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (Amount <= 0)
        {
            MessageBox.Show("Amount must be greater than zero.", "PyroSync — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (EditingExpense != null)
            {
                var tracked = await _dbContext.OuterExpenses.FindAsync(EditingExpense.OuterExpenseId);
                if (tracked != null)
                {
                    tracked.Description = Description;
                    tracked.Amount = Amount;
                    tracked.ExpenseDate = ExpenseDate;
                    _dbContext.Entry(tracked).State = EntityState.Modified;
                    await _dbContext.SaveChangesAsync();
                }
            }
            else
            {
                var item = new OuterExpense
                {
                    Description = Description,
                    Amount = Amount,
                    ExpenseDate = ExpenseDate,
                    CreatedAt = DateTime.Now
                };
                _dbContext.OuterExpenses.Add(item);
                await _dbContext.SaveChangesAsync();
            }

            CancelEdit();
            await LoadExpensesAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save outer expense");
            MessageBox.Show($"Error saving outer expense: {ex.Message}", "PyroSync — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void EditExpense(OuterExpense item)
    {
        EditingExpense = item;
        Description = item.Description;
        Amount = item.Amount;
        ExpenseDate = item.ExpenseDate;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingExpense = null;
        Description = string.Empty;
        Amount = 0;
        ExpenseDate = DateTime.Today;
    }

    [RelayCommand]
    private async Task DeleteExpenseAsync(OuterExpense item)
    {
        var confirm = MessageBox.Show($"Are you sure you want to delete outer expense '{item.Description}'?", 
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var tracked = await _dbContext.OuterExpenses.FindAsync(item.OuterExpenseId);
            if (tracked != null)
            {
                _dbContext.OuterExpenses.Remove(tracked);
                await _dbContext.SaveChangesAsync();
            }
            await LoadExpensesAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to delete outer expense");
            MessageBox.Show($"Error deleting outer expense: {ex.Message}", "PyroSync — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
