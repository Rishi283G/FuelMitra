using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rashtra.Licensing;
using System;
using System.Windows;

namespace FuelPro.UI.ViewModels
{
    public partial class ActivationViewModel : ObservableObject
    {
        private readonly LicenseManager _licenseManager;

        [ObservableProperty] private string _customerName = string.Empty;
        [ObservableProperty] private string _businessName = string.Empty;
        [ObservableProperty] private string _mobileNumber = string.Empty;
        [ObservableProperty] private string _deviceId = string.Empty;
        [ObservableProperty] private string _licenseKey = string.Empty;
        [ObservableProperty] private string _errorMessage = string.Empty;
        [ObservableProperty] private bool _isActivated;

        public event Action? ActivationSucceeded;

        public ActivationViewModel(LicenseManager licenseManager)
        {
            _licenseManager = licenseManager;
            DeviceId = DeviceIdentifier.GetDeviceId();

            // Check if there's an existing but invalid/expired license to show the detailed error on load
            var validation = _licenseManager.ValidateLicense();
            if (!validation.IsValid && validation.ErrorMessage != "License file not found. Activation Required.")
            {
                ErrorMessage = "❌ " + validation.ErrorMessage;
            }
        }

        [RelayCommand]
        private void CopyDeviceId()
        {
            if (!string.IsNullOrEmpty(DeviceId))
            {
                Clipboard.SetText(DeviceId);
                ErrorMessage = "✅ Device ID copied to clipboard!";
            }
        }

        [RelayCommand]
        private void GenerateRequest()
        {
            if (string.IsNullOrWhiteSpace(CustomerName) || string.IsNullOrWhiteSpace(BusinessName))
            {
                ErrorMessage = "❌ Customer Name and Business Name are required to generate request.";
                return;
            }

            var request = $"Product: FuelPro Lite\n" +
                          $"Customer Name: {CustomerName.Trim()}\n" +
                          $"Business Name: {BusinessName.Trim()}\n" +
                          $"Mobile Number: {MobileNumber.Trim()}\n" +
                          $"Device ID: {DeviceId}";

            Clipboard.SetText(request);
            ErrorMessage = "✅ Activation Request details copied to clipboard! Share this with Rashtra Technologies.";
        }

        [RelayCommand]
        private void Activate()
        {
            ErrorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(CustomerName) || string.IsNullOrWhiteSpace(BusinessName) || string.IsNullOrWhiteSpace(LicenseKey))
            {
                ErrorMessage = "❌ All fields are required for activation.";
                return;
            }

            var success = _licenseManager.ActivateLicense(CustomerName, BusinessName, MobileNumber, LicenseKey);
            if (success)
            {
                IsActivated = true;
                ActivationSucceeded?.Invoke();
            }
            else
            {
                ErrorMessage = "❌ Invalid License Key for the provided Customer Name and Device ID. Please verify and try again.";
            }
        }
    }
}
