using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FuelPro.UI.ViewModels;

public partial class PettyCashViewModel : ObservableObject
{
    private readonly FuelProDbContext _dbContext;

    [ObservableProperty] private DateTime _date = DateTime.Today;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private double _amount;
    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private double _balance;
    [ObservableProperty] private double _totalAdditions;
    [ObservableProperty] private double _totalDeductions;

    public ObservableCollection<PettyCashTransaction> History { get; } = new();

    public PettyCashViewModel(FuelProDbContext dbContext)
    {
        _dbContext = dbContext;
        _ = LoadDataAsync();
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        try
        {
            History.Clear();
            var list = await _dbContext.PettyCashTransactions
                .OrderByDescending(t => t.Date)
                .ThenByDescending(t => t.CreatedAt)
                .ToListAsync();

            foreach (var item in list)
            {
                History.Add(item);
            }

            Balance = list.Sum(t => t.Amount);
            TotalAdditions = list.Where(t => t.Type == "Addition").Sum(t => t.Amount);
            TotalDeductions = Math.Abs(list.Where(t => t.Type == "Deduction").Sum(t => t.Amount));
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load petty cash history");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAdditionAsync()
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
            var item = new PettyCashTransaction
            {
                Date = Date,
                Description = Description,
                Amount = Amount,
                Type = "Addition",
                ShiftExpenseId = null,
                CreatedAt = DateTime.Now
            };

            _dbContext.PettyCashTransactions.Add(item);
            await _dbContext.SaveChangesAsync();

            Description = string.Empty;
            Amount = 0;

            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            if (ex.InnerException != null) msg += $"\n\nInner: {ex.InnerException.Message}";
            MessageBox.Show($"Failed to save transaction: {msg}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteTransactionAsync(PettyCashTransaction tx)
    {
        if (tx == null) return;

        if (tx.Type == "Deduction" && tx.ShiftExpenseId.HasValue)
        {
            MessageBox.Show("This transaction is linked to a Shift Expense. Please delete it from the shift calculation screen to maintain records consistency.", "PyroSync — Protected Transaction", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show($"Are you sure you want to delete the addition '{tx.Description}' of ₹{tx.Amount:F2}?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            var tracked = await _dbContext.PettyCashTransactions.FindAsync(tx.TransactionId);
            if (tracked != null)
            {
                _dbContext.PettyCashTransactions.Remove(tracked);
                await _dbContext.SaveChangesAsync();
                await LoadDataAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete transaction: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Description = string.Empty;
        Amount = 0;
        Date = DateTime.Today;
    }
}
