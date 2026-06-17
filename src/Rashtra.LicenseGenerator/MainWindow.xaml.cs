using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rashtra.Licensing;
using System;
using System.Collections.Generic;
using System.Windows;

namespace Rashtra.LicenseGenerator
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
        }
    }

    public partial class MainWindowViewModel : ObservableObject
    {
        [ObservableProperty] private string _selectedProduct = "PSC";
        [ObservableProperty] private string _selectedLicenseType = "Lifetime";
        [ObservableProperty] private string _customerName = "";
        [ObservableProperty] private string _businessName = "";
        [ObservableProperty] private string _mobileNumber = "";
        [ObservableProperty] private string _deviceId = "";
        [ObservableProperty] private string _generatedKey = "";
        [ObservableProperty] private string _statusMessage = "";
        [ObservableProperty] private string _statusColor = "#F44336"; // Red
        [ObservableProperty] private DateTime? _expiryDate;
        [ObservableProperty] private bool _isExpiryDateEnabled;

        public Dictionary<string, string> ProductOptions { get; } = new()
        {
            { "FPL", "FuelPro Lite (FPL)" },
            { "PSC", "PyroSync (PSC)" },
            { "ZPA", "ZP Automation (ZPA)" }
        };

        public Dictionary<string, string> LicenseTypeOptions { get; } = new()
        {
            { "Lifetime", "Lifetime License" },
            { "Annual", "Annual License (1 Year)" },
            { "Trial", "Trial License (15 Days)" },
            { "Demo", "Demo License (7 Days)" }
        };

        public MainWindowViewModel()
        {
            UpdateDefaultExpiryDate();
        }

        partial void OnSelectedLicenseTypeChanged(string value)
        {
            UpdateDefaultExpiryDate();
        }

        private void UpdateDefaultExpiryDate()
        {
            if (SelectedLicenseType == "Lifetime")
            {
                ExpiryDate = null;
                IsExpiryDateEnabled = false;
            }
            else
            {
                IsExpiryDateEnabled = true;
                if (SelectedLicenseType == "Annual")
                {
                    ExpiryDate = DateTime.Today.AddYears(1);
                }
                else if (SelectedLicenseType == "Trial")
                {
                    ExpiryDate = DateTime.Today.AddDays(15);
                }
                else if (SelectedLicenseType == "Demo")
                {
                    ExpiryDate = DateTime.Today.AddDays(7);
                }
            }
        }

        [RelayCommand]
        private void GenerateKey()
        {
            StatusMessage = "";
            if (string.IsNullOrWhiteSpace(CustomerName))
            {
                StatusMessage = "❌ Customer Name is required.";
                StatusColor = "#F44336";
                return;
            }

            if (string.IsNullOrWhiteSpace(DeviceId) || !DeviceId.StartsWith("RT-"))
            {
                StatusMessage = "❌ Valid Device ID starting with 'RT-' is required.";
                StatusColor = "#F44336";
                return;
            }

            try
            {
                // Format the product code argument for the key generator
                var productCode = SelectedProduct;
                if (SelectedLicenseType != "Lifetime" && ExpiryDate.HasValue)
                {
                    var suffix = SelectedLicenseType switch
                    {
                        "Trial" => "T",
                        "Annual" => "A",
                        "Demo" => "D",
                        _ => ""
                    };
                    if (!string.IsNullOrEmpty(suffix))
                    {
                        productCode = $"{SelectedProduct}-{suffix}-{ExpiryDate.Value:yyyyMMdd}";
                    }
                }

                var key = LicenseManager.GenerateLicenseKey(productCode, CustomerName, DeviceId);
                GeneratedKey = key;
                StatusMessage = "✅ License Key generated successfully!";
                StatusColor = "#4CAF50";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Error: {ex.Message}";
                StatusColor = "#F44336";
            }
        }

        [RelayCommand]
        private void CopyKey()
        {
            if (string.IsNullOrWhiteSpace(GeneratedKey))
            {
                StatusMessage = "❌ No generated key to copy.";
                StatusColor = "#F44336";
                return;
            }

            Clipboard.SetText(GeneratedKey);
            StatusMessage = "✅ Key copied to clipboard!";
            StatusColor = "#4CAF50";
        }

        [RelayCommand]
        private void CopyDetails()
        {
            if (string.IsNullOrWhiteSpace(GeneratedKey))
            {
                StatusMessage = "❌ Generate a key first before copying details.";
                StatusColor = "#F44336";
                return;
            }

            var expiryStr = ExpiryDate.HasValue ? ExpiryDate.Value.ToString("dd-MMM-yyyy") : "N/A (Lifetime)";
            var details = $"=== RASHTRA TECHNOLOGIES LICENSE SYSTEM ===\n" +
                          $"Product: {ProductOptions[SelectedProduct]}\n" +
                          $"Customer Name: {CustomerName.Trim()}\n" +
                          $"Business Name: {BusinessName.Trim()}\n" +
                          $"Mobile Number: {MobileNumber.Trim()}\n" +
                          $"Device ID: {DeviceId.Trim().ToUpper()}\n" +
                          $"License Type: {SelectedLicenseType}\n" +
                          $"Expiry Date: {expiryStr}\n" +
                          $"Generated Key: {GeneratedKey}\n" +
                          $"===========================================";

            Clipboard.SetText(details);
            StatusMessage = "✅ Full activation details copied to clipboard!";
            StatusColor = "#4CAF50";
        }
    }
}