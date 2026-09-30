using System;
using System.Windows;
using System.Windows.Controls;

namespace FuelPro.UI.Views;

public partial class EditOilDefPurchaseWindow : Window
{
    public string SupplierName { get; private set; } = string.Empty;
    public string InvoiceNumber { get; private set; } = string.Empty;
    public double Quantity { get; private set; }
    public double UnitPrice { get; private set; }
    public double TotalCost { get; private set; }
    public DateTime SelectedDate { get; private set; } = DateTime.Today;

    public EditOilDefPurchaseWindow(string productName, DateTime date, string supplier, string invoice, double currentQty, double currentUnitPrice)
    {
        InitializeComponent();

        ProductLabel.Text = $"Product: {productName}";
        PurchaseDatePicker.SelectedDate = date.Date;
        SupplierTextBox.Text = supplier ?? string.Empty;
        InvoiceTextBox.Text = invoice ?? string.Empty;
        QuantityTextBox.Text = currentQty.ToString("0.##");
        UnitPriceTextBox.Text = currentUnitPrice.ToString("0.##");

        UpdateTotalDisplay();
    }

    private void OnValuesChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTotalDisplay();
    }

    private void UpdateTotalDisplay()
    {
        if (TotalCostLabel == null) return;

        double qty = double.TryParse(QuantityTextBox?.Text, out var q) ? q : 0;
        double price = double.TryParse(UnitPriceTextBox?.Text, out var p) ? p : 0;
        TotalCostLabel.Text = $"Total Cost: ₹{(qty * price):N2}";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SupplierTextBox.Text))
        {
            MessageBox.Show("Please enter a supplier name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(InvoiceTextBox.Text))
        {
            MessageBox.Show("Please enter an invoice number.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(QuantityTextBox.Text, out var qty) || qty <= 0)
        {
            MessageBox.Show("Please enter a valid quantity greater than 0.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!double.TryParse(UnitPriceTextBox.Text, out var price) || price <= 0)
        {
            MessageBox.Show("Please enter a valid unit price greater than 0.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SupplierName = SupplierTextBox.Text.Trim();
        InvoiceNumber = InvoiceTextBox.Text.Trim();
        Quantity = qty;
        UnitPrice = price;
        TotalCost = Math.Round(qty * price, 2);
        SelectedDate = PurchaseDatePicker.SelectedDate ?? DateTime.Today;

        DialogResult = true;
        Close();
    }
}
