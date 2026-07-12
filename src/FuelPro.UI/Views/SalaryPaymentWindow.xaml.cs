using System;
using System.Linq;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;

namespace FuelPro.UI.Views;

public partial class SalaryPaymentWindow : Window
{
    private readonly DsmSalaryRowDto _row;
    private readonly int _year;
    private readonly string _monthName;
    private readonly FuelProDbContext _dbContext;

    public SalaryPaymentWindow(DsmSalaryRowDto row, int year, string monthName)
    {
        InitializeComponent();

        _row = row;
        _year = year;
        _monthName = monthName;
        _dbContext = App.Services.GetRequiredService<FuelProDbContext>();

        // Populate fields
        TxtDsmName.Text = row.DsmName;
        TxtPeriod.Text = $"{monthName} {year}";
        TxtNetSalary.Text = row.NetSalary.ToString("F2");
        TxtPaidAmount.Text = row.NetSalary.ToString("F2"); // Default to full net salary
        DpPaymentDate.SelectedDate = DateTime.Today;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        // 1. Validation
        if (!double.TryParse(TxtPaidAmount.Text, out double paidAmount) || paidAmount <= 0)
        {
            MessageBox.Show("Please enter a valid paid amount greater than 0.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (DpPaymentDate.SelectedDate == null)
        {
            MessageBox.Show("Please select a payment date.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // 2. Lookup DSM Profile ID by Name
            var profile = await _dbContext.DsmProfiles
                .FirstOrDefaultAsync(p => p.DsmName.ToLower() == _row.DsmName.ToLower());
            if (profile == null)
            {
                MessageBox.Show($"Could not find a DSM Profile for '{_row.DsmName}'.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 3. Create payment entry
            int monthNum = GetMonthNumber(_monthName);
            var payment = new DsmSalaryPayment
            {
                DsmProfileId = profile.DsmProfileId,
                Year = _year,
                Month = monthNum,
                NetSalary = _row.NetSalary,
                PaidAmount = paidAmount,
                PaymentDate = DpPaymentDate.SelectedDate.Value,
                PaymentMode = ((System.Windows.Controls.ComboBoxItem)CbPaymentMode.SelectedItem).Content.ToString()!,
                Remarks = TxtRemarks.Text?.Trim(),
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _dbContext.DsmSalaryPayments.Add(payment);
            await _dbContext.SaveChangesAsync();

            MessageBox.Show("Salary payment recorded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to save salary payment");
            MessageBox.Show($"Failed to save payment: {ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private int GetMonthNumber(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return DateTime.Today.Month;
        name = name.Trim();

        string[] monthNames = {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        };

        int idx = Array.FindIndex(monthNames, m => string.Equals(m, name, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? idx + 1 : DateTime.Today.Month;
    }
}
