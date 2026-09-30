using System;
using System.Collections.Generic;
using System.Windows;

namespace FuelPro.UI.Views;

public partial class EditStockAdjustmentWindow : Window
{
    public double Quantity { get; private set; }
    public string AdjustmentType { get; private set; } = string.Empty;
    public string Remarks { get; private set; } = string.Empty;
    public DateTime SelectedDate { get; private set; } = DateTime.Today;

    public EditStockAdjustmentWindow(string productName, DateTime date, double currentQty, string currentType, string currentRemarks)
    {
        InitializeComponent();

        ProductLabel.Text = $"Product: {productName}";
        AdjustmentDatePicker.SelectedDate = date.Date;
        QuantityTextBox.Text = currentQty.ToString("N2");

        var types = new List<string>
        {
            "Physical Count Correction",
            "Damaged Stock",
            "Expired Item",
            "Supplier Return",
            "Opening Balance Correction"
        };
        TypeComboBox.ItemsSource = types;
        TypeComboBox.SelectedItem = types.Contains(currentType) ? currentType : types[0];

        RemarksTextBox.Text = currentRemarks ?? string.Empty;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(QuantityTextBox.Text, out var qty) || qty == 0)
        {
            MessageBox.Show("Please enter a valid non-zero adjustment quantity.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Quantity = qty;
        AdjustmentType = TypeComboBox.SelectedItem?.ToString() ?? "Physical Count Correction";
        Remarks = RemarksTextBox.Text?.Trim() ?? string.Empty;
        SelectedDate = AdjustmentDatePicker.SelectedDate ?? DateTime.Today;

        DialogResult = true;
        Close();
    }
}
