using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FuelPro.UI.ViewModels;

public partial class SalaryCalculationViewModel : ObservableObject
{
    private readonly IFinancialCalculationService _financialCalcService;
    private readonly IDsmProfileRepository _profileRepo;

    [ObservableProperty] private int _selectedYear = DateTime.Today.Year;
    [ObservableProperty] private string _selectedMonthName = DateTime.Today.ToString("MMMM");
    [ObservableProperty] private bool _isLoading;

    [ObservableProperty] private DsmProfile _newProfile = new() { SalaryType = "FixedMonthly", BaseSalary = 12000.0 };
    [ObservableProperty] private DsmProfile? _selectedProfile;

    public ObservableCollection<DsmSalaryRowDto> SalaryRows { get; } = new();
    public ObservableCollection<DsmProfile> Profiles { get; } = new();

    public ObservableCollection<int> Years { get; } = new();
    public ObservableCollection<string> Months { get; } = new();

    private readonly string[] _monthNames = {
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"
    };

    public SalaryCalculationViewModel()
    {
        _financialCalcService = App.Services.GetRequiredService<IFinancialCalculationService>();
        _profileRepo = App.Services.GetRequiredService<IDsmProfileRepository>();

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

        _ = LoadAllAsync();
    }

    private int GetMonthNumber(string name)
    {
        int idx = Array.IndexOf(_monthNames, name);
        return idx >= 0 ? idx + 1 : DateTime.Today.Month;
    }

    partial void OnSelectedYearChanged(int value) => _ = LoadSalariesAsync();
    partial void OnSelectedMonthNameChanged(string value) => _ = LoadSalariesAsync();

    [RelayCommand]
    public async Task LoadAllAsync()
    {
        await Task.WhenAll(LoadSalariesAsync(), LoadProfilesAsync());
    }

    [RelayCommand]
    public async Task LoadSalariesAsync()
    {
        IsLoading = true;
        try
        {
            SalaryRows.Clear();
            int monthNum = GetMonthNumber(SelectedMonthName);
            var rows = await _financialCalcService.CalculateDsmSalariesAsync(SelectedYear, monthNum);
            foreach (var r in rows)
            {
                SalaryRows.Add(r);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load DSM salaries");
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    public async Task LoadProfilesAsync()
    {
        try
        {
            Profiles.Clear();
            var res = await _profileRepo.GetAllAsync();
            if (res.Success && res.Data != null)
            {
                foreach (var p in res.Data)
                {
                    Profiles.Add(p);
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load DSM profiles");
        }
    }

    [RelayCommand]
    private async Task SaveAdjustmentsAsync()
    {
        IsLoading = true;
        try
        {
            int monthNum = GetMonthNumber(SelectedMonthName);
            await _financialCalcService.SaveDsmSalaryAdjustmentsAsync(SelectedYear, monthNum, SalaryRows.ToList());
            MessageBox.Show("Salary adjustments saved successfully!", "PyroSync — Success", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadSalariesAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save salary adjustments");
            MessageBox.Show($"Failed to save adjustments: {ex.Message}", "PyroSync — Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private async Task AddProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewProfile.DsmName))
        {
            MessageBox.Show("Please enter a DSM Name.", "PyroSync — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var res = await _profileRepo.AddAsync(NewProfile);
            if (res.Success)
            {
                MessageBox.Show("DSM Profile added successfully!", "PyroSync — Success", MessageBoxButton.OK, MessageBoxImage.Information);
                NewProfile = new DsmProfile { SalaryType = "FixedMonthly", BaseSalary = 12000.0 };
                await LoadAllAsync();
            }
            else
            {
                MessageBox.Show(res.Error, "PyroSync — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to add DSM profile");
        }
    }

    [RelayCommand]
    private async Task UpdateProfileAsync()
    {
        if (SelectedProfile == null)
        {
            MessageBox.Show("Please select a profile to update.", "PyroSync — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var res = await _profileRepo.UpdateAsync(SelectedProfile);
            if (res.Success)
            {
                MessageBox.Show("DSM Profile updated successfully!", "PyroSync — Success", MessageBoxButton.OK, MessageBoxImage.Information);
                SelectedProfile = null;
                await LoadAllAsync();
            }
            else
            {
                MessageBox.Show(res.Error, "PyroSync — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to update DSM profile");
        }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile == null)
        {
            MessageBox.Show("Please select a profile to delete.", "PyroSync — Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show($"Are you sure you want to delete profile for '{SelectedProfile.DsmName}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var res = await _profileRepo.DeleteAsync(SelectedProfile.DsmProfileId);
            if (res.Success)
            {
                MessageBox.Show("DSM Profile deleted successfully!", "PyroSync — Success", MessageBoxButton.OK, MessageBoxImage.Information);
                SelectedProfile = null;
                await LoadAllAsync();
            }
            else
            {
                MessageBox.Show(res.Error, "PyroSync — Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to delete DSM profile");
        }
    }
}
