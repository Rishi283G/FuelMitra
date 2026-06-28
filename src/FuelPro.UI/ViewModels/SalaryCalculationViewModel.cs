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

public partial class SalaryCalculationViewModel : ObservableObject
{
    private readonly IFinancialCalculationService _financialCalcService;
    private readonly IDsmProfileRepository _profileRepo;
    private readonly PrintService _printService;
    private readonly ExcelExportService _excelExportService;

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
        _printService = App.Services.GetRequiredService<PrintService>();
        _excelExportService = App.Services.GetRequiredService<ExcelExportService>();

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
    private async Task DeleteAdjustmentAsync(DsmSalaryRowDto row)
    {
        if (row == null) return;

        var confirm = MessageBox.Show($"Are you sure you want to delete and reset saved payroll adjustments (Advance Paid, Other Adjustments and Remarks) for '{row.DsmName}' in {SelectedMonthName} {SelectedYear}?", "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            int monthNum = GetMonthNumber(SelectedMonthName);
            await _financialCalcService.DeleteDsmSalaryAdjustmentAsync(SelectedYear, monthNum, row.DsmName);
            MessageBox.Show("Salary adjustments deleted/reset successfully!", "PyroSync — Success", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadSalariesAsync();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to delete salary adjustment");
            MessageBox.Show($"Failed to delete adjustment: {ex.Message}", "PyroSync — Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

    [RelayCommand]
    private void Print()
    {
        try
        {
            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Payroll Cost", Value = "₹" + SalaryRows.Sum(r => r.NetSalary).ToString("N2"), Highlight = true },
                new() { Label = "Total Employees", Value = SalaryRows.Count.ToString(), Highlight = false },
                new() { Label = "Total Shifts worked", Value = SalaryRows.Sum(r => r.ShiftsWorked).ToString(), Highlight = false }
            };

            var headers = new List<string> { "Employee Name", "Salary Type", "Rate (Base)", "Shifts", "Earned Base", "Short Recovery", "Advance Paid", "Personal Debtors", "Other Adj", "Net Salary" };
            var rows = new List<List<string>>();

            foreach (var row in SalaryRows)
            {
                rows.Add(new List<string>
                {
                    row.DsmName,
                    row.SalaryType,
                    "₹" + row.BaseSalary.ToString("N2"),
                    row.ShiftsWorked.ToString(),
                    "₹" + row.EarnedBase.ToString("N2"),
                    "₹" + row.ShortRecovery.ToString("N2"),
                    "₹" + row.AdvancePaid.ToString("N2"),
                    "₹" + row.PersonalDebtorDeduction.ToString("N2"),
                    "₹" + row.OtherAdjustments.ToString("N2"),
                    "₹" + row.NetSalary.ToString("N2")
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "DSM Monthly Payroll Sheet",
                Subtitle = $"Period: {SelectedMonthName} {SelectedYear}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            _printService.PrintGenericGrid(printData);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print DSM Payroll Sheet report");
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
                new() { Label = "Total Payroll Cost", Value = "₹" + SalaryRows.Sum(r => r.NetSalary).ToString("N2"), Highlight = true },
                new() { Label = "Total Employees", Value = SalaryRows.Count.ToString(), Highlight = false },
                new() { Label = "Total Shifts worked", Value = SalaryRows.Sum(r => r.ShiftsWorked).ToString(), Highlight = false }
            };

            var headers = new List<string> { "Employee Name", "Salary Type", "Rate (Base)", "Shifts", "Earned Base", "Short Recovery", "Advance Paid", "Personal Debtors", "Other Adj", "Net Salary" };
            var rows = new List<List<string>>();

            foreach (var row in SalaryRows)
            {
                rows.Add(new List<string>
                {
                    row.DsmName,
                    row.SalaryType,
                    "₹" + row.BaseSalary.ToString("N2"),
                    row.ShiftsWorked.ToString(),
                    "₹" + row.EarnedBase.ToString("N2"),
                    "₹" + row.ShortRecovery.ToString("N2"),
                    "₹" + row.AdvancePaid.ToString("N2"),
                    "₹" + row.PersonalDebtorDeduction.ToString("N2"),
                    "₹" + row.OtherAdjustments.ToString("N2"),
                    "₹" + row.NetSalary.ToString("N2")
                });
            }

            var printData = new GenericGridPrintData
            {
                Title = "DSM Monthly Payroll Sheet",
                Subtitle = $"Period: {SelectedMonthName} {SelectedYear}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            var path = await _excelExportService.ExportGenericGridAsync(printData, "DsmPayroll");
            MessageBox.Show($"Report exported successfully to:\n{path}", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to export DSM Payroll to Excel");
            MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
