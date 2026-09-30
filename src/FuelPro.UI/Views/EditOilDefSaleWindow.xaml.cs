using System;
using System.Windows;
using System.Windows.Controls;

namespace FuelPro.UI.Views;

public partial class EditOilDefSaleWindow : Window
{
    public double Quantity { get; private set; }
    public double Rate { get; private set; }
    public DateTime SelectedDate { get; private set; } = DateTime.Today;

    public EditOilDefSaleWindow(string productName, DateTime date, double currentQty, double currentRate)
    {
        InitializeComponent();

        ProductLabel.Text = $"Product: {productName}";
        SaleDatePicker.SelectedDate = date.Date;
        QuantityTextBox.Text = currentQty.ToString("0.##");
        RateTextBox.Text = currentRate.ToString("0.##");

        UpdateTotalDisplay();
    }

    private void OnValuesChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTotalDisplay();
    }

    private void UpdateTotalDisplay()
    {
        if (TotalAmountLabel == null) return;

        double qty = double.TryParse(QuantityTextBox?.Text, out var q) ? q : 0;
        double rate = double.TryParse(RateTextBox?.Text, out var r) ? r : 0;
        TotalAmountLabel.Text = $"Total Amount: ₹{(qty * rate):N2}";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(QuantityTextBox.Text, out var qty) || qty <= 0)
        {
            MessageBox.Show("Please enter a valid sold quantity greater than 0.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(RateTextBox.Text, out var rate) || rate < 0)
        {
            MessageBox.Show("Please enter a valid rate (₹).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Quantity = qty;
        Rate = rate;
        SelectedDate = SaleDatePicker.SelectedDate ?? DateTime.Today;

        DialogResult = true;
        Close();
    }
}
