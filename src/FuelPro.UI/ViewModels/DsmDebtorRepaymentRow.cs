using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace FuelPro.UI.ViewModels;

public partial class DsmDebtorRepaymentRow : ObservableObject
{
    public Action? OnRowChanged { get; set; }

    [ObservableProperty] private string _debtorName = string.Empty;
    [ObservableProperty] private string _paymentMode = "Cash";
    [ObservableProperty] private double? _amount;
    [ObservableProperty] private string? _notes;

    partial void OnDebtorNameChanged(string value) => OnRowChanged?.Invoke();
    partial void OnPaymentModeChanged(string value) => OnRowChanged?.Invoke();
    partial void OnAmountChanged(double? value) => OnRowChanged?.Invoke();
    partial void OnNotesChanged(string? value) => OnRowChanged?.Invoke();
}
