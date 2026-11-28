using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Core.Repositories;
using FuelPro.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Rashtra.Licensing;
using FuelPro.UI.Printing;
using FuelPro.Core.DTOs;

namespace FuelPro.UI.ViewModels;

public partial class FuelRateItemVm : ObservableObject
{
    public string TankName { get; set; } = "";
    public string FuelType { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Unit { get; set; } = "₹/L";
    public string HintText => $"{DisplayName} Rate ({Unit})";

    [ObservableProperty] private string _rateText = "0.00";
    public double Rate
    {
        get
        {
            if (double.TryParse(RateText, NumberStyles.Any, CultureInfo.CurrentCulture, out var v)) return v;
            if (double.TryParse(RateText, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            return 0.0;
        }
    }
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly AuthService _authService;
    private readonly IUserRepository _userRepo;
    private readonly ICreditorRepository _creditorRepo;
    private readonly LicenseManager _licenseManager;
    private readonly FuelPro.Sync.SyncConfigService _syncConfigService;
    private readonly FuelPro.Sync.SyncEngine _syncEngine;
    private readonly FuelPro.Sync.StationPairingService? _pairingService;
    private readonly IStationConfigurationService? _stationConfigService;

    [ObservableProperty] private string _pumpStationName = "";
    [ObservableProperty] private string _stationSubtitle = "";
    [ObservableProperty] private string _stationAddress = "";
    [ObservableProperty] private string _stationContact = "";
    [ObservableProperty] private string _oilCompany = "BPCL";
    [ObservableProperty] private string _customLogoPath = "";
    public ObservableCollection<string> OilCompanies { get; } = new() { "BPCL", "HPCL", "IOCL", "SHELL", "JIO_BP", "OTHER" };
    [ObservableProperty] private string _hsdRate = "";

    // Dynamic Fuel Rates
    public ObservableCollection<FuelRateItemVm> FuelRateItems { get; } = new();

    // Cloud Sync Settings
    [ObservableProperty] private string _supabaseUrl = "";
    [ObservableProperty] private string _supabaseApiKey = "";
    [ObservableProperty] private string _syncStationId = "";
    [ObservableProperty] private string _syncMachineId = "";
    [ObservableProperty] private bool _syncEnabled;
    [ObservableProperty] private string _syncStatusMessage = "";
    [ObservableProperty] private string _lastSyncTimeDisplay = "—";

    // Multi-Computer Access Settings
    [ObservableProperty] private bool _isMultiComputerEnabled;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPairingCode))]
    private string _pairingCodeDisplay = "";

    public bool HasPairingCode => !string.IsNullOrWhiteSpace(PairingCodeDisplay);
    [ObservableProperty] private string _pairingCodeInput = "";
    [ObservableProperty] private string _deviceFriendlyName = Environment.MachineName;
    [ObservableProperty] private string _multiComputerStatusMessage = "";
    [ObservableProperty] private bool _isPairingInProgress;
    [ObservableProperty] private StationDevice? _selectedDevice;
    public ObservableCollection<StationDevice> StationDevices { get; } = new();
    [ObservableProperty] private string _msIRate = "";
    [ObservableProperty] private string _msIIRate = "";
    [ObservableProperty] private string _cngRate = "";
    [ObservableProperty] private string _shift1Manager = "";
    [ObservableProperty] private string _shift2Manager = "";
    [ObservableProperty] private string _shift3Manager = "";
    [ObservableProperty] private string _lastUpdated = "";
    [ObservableProperty] private string _statusMessage = "";

    // User management
    public ObservableCollection<User> Users { get; } = new();
    [ObservableProperty] private string _newUsername = "";
    [ObservableProperty] private string _newPin = "";
    [ObservableProperty] private string _newRole = "Operator";

    // Change PIN
    [ObservableProperty] private string _currentPin = "";
    [ObservableProperty] private string _newPinChange = "";
    [ObservableProperty] private string _pinStatusMessage = "";



    // Creditor Management
    public ObservableCollection<Creditor> Creditors { get; } = new();
    [ObservableProperty] private string _newCreditorName = "";
    [ObservableProperty] private string _newCreditorPhone = "";
    [ObservableProperty] private string _newCreditorVehicleNumber = "";
    [ObservableProperty] private string _creditorStatusMessage = "";

    // Creditor Vehicle Mapping
    [ObservableProperty] private Creditor? _selectedCreditor;
    public ObservableCollection<DebtorVehicle> SelectedCreditorVehicles { get; } = new();
    [ObservableProperty] private string _newVehicleNumber = "";
    [ObservableProperty] private bool _isCreditorSelected;

    partial void OnSelectedCreditorChanged(Creditor? value)
    {
        IsCreditorSelected = value != null;
        _ = LoadSelectedCreditorVehiclesAsync();
    }

    private async Task LoadSelectedCreditorVehiclesAsync()
    {
        SelectedCreditorVehicles.Clear();
        if (SelectedCreditor == null) return;

        try
        {
            var vehicleRepo = App.Services.GetRequiredService<IDebtorVehicleRepository>();
            var result = await vehicleRepo.GetByCreditorIdAsync(SelectedCreditor.CreditorId);
            if (result.Success && result.Data != null)
            {
                foreach (var v in result.Data)
                {
                    SelectedCreditorVehicles.Add(v);
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load debtor vehicles in settings");
        }
    }

    public string[] RoleOptions { get; } = { "Admin", "Operator" };

    private Setting? _settings;

    private readonly IDsmProfileRepository _profileRepo;

    public OpeningBalanceManagementViewModel OpeningBalanceVm { get; }

    public SettingsViewModel()
    {
        _settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();
        _authService = App.Services.GetRequiredService<AuthService>();
        _userRepo = App.Services.GetRequiredService<IUserRepository>();
        _creditorRepo = App.Services.GetRequiredService<ICreditorRepository>();
        _licenseManager = App.Services.GetRequiredService<LicenseManager>();
        _syncConfigService = App.Services.GetRequiredService<FuelPro.Sync.SyncConfigService>();
        _syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
        _pairingService = App.Services.GetService<FuelPro.Sync.StationPairingService>();
        _profileRepo = App.Services.GetRequiredService<IDsmProfileRepository>();
        _stationConfigService = App.Services.GetService<IStationConfigurationService>();
        OpeningBalanceVm = App.Services.GetRequiredService<OpeningBalanceManagementViewModel>();

        if (_stationConfigService != null)
        {
            _stationConfigService.StationConfigurationChanged += () =>
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(async () => await LoadFuelRatesAsync());
                }
                else
                {
                    _ = LoadFuelRatesAsync();
                }
            };
        }

        _ = LoadAsync();
        LoadLicenseInfo();
    }

    public async Task LoadFuelRatesAsync()
    {
        try
        {
            var items = new List<FuelRateItemVm>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_stationConfigService != null)
            {
                var tanks = await _stationConfigService.GetAllTanksAsync();
                var activeTanks = tanks.Where(t => t.IsActive).ToList();

                foreach (var tank in activeTanks)
                {
                    string tankKey = tank.TankName?.Trim() ?? "";
                    string fuelType = tank.FuelType?.Trim() ?? "";
                    if (string.IsNullOrWhiteSpace(tankKey)) continue;

                    string displayKey = !string.IsNullOrWhiteSpace(fuelType) && !string.Equals(tankKey, fuelType, StringComparison.OrdinalIgnoreCase)
                        ? $"{fuelType} ({tankKey})"
                        : tankKey;

                    if (seenKeys.Contains(displayKey)) continue;
                    seenKeys.Add(displayKey);

                    string unit = (fuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase) || tankKey.Contains("CNG", StringComparison.OrdinalIgnoreCase))
                        ? "₹/Kg"
                        : "₹/L";

                    double currentRate = _settings?.GetRateFor(tankKey, 0.0) ?? 0.0;
                    if (currentRate <= 0 && !string.IsNullOrWhiteSpace(fuelType)) currentRate = _settings?.GetRateFor(fuelType, 0.0) ?? 0.0;
                    if (currentRate <= 0) currentRate = _settings?.GetRateFor(displayKey, 0.0) ?? 0.0;

                    items.Add(new FuelRateItemVm
                    {
                        TankName = tankKey,
                        FuelType = !string.IsNullOrWhiteSpace(fuelType) ? fuelType : tankKey,
                        DisplayName = displayKey,
                        Unit = unit,
                        RateText = currentRate > 0 ? currentRate.ToString("0.##", CultureInfo.InvariantCulture) : "0.00"
                    });
                }
            }

            // Fallback if no tanks configured in database
            if (items.Count == 0)
            {
                items.Add(new FuelRateItemVm { TankName = "HSD - 20KL", FuelType = "HSD", DisplayName = "HSD (HSD - 20KL)", Unit = "₹/L", RateText = (_settings?.HsdRate ?? 90.35).ToString("0.##", CultureInfo.InvariantCulture) });
                items.Add(new FuelRateItemVm { TankName = "MS - 20KL", FuelType = "MS-I", DisplayName = "MS-I (MS - 20KL)", Unit = "₹/L", RateText = (_settings?.MsIRate ?? 103.81).ToString("0.##", CultureInfo.InvariantCulture) });
                items.Add(new FuelRateItemVm { TankName = "HSD - 20KL II", FuelType = "MS-II", DisplayName = "MS-II (HSD - 20KL II)", Unit = "₹/L", RateText = (_settings?.MsIIRate ?? 103.81).ToString("0.##", CultureInfo.InvariantCulture) });
                items.Add(new FuelRateItemVm { TankName = "CNG Line", FuelType = "CNG", DisplayName = "CNG (CNG Line)", Unit = "₹/Kg", RateText = (_settings?.CngRate ?? 85.0).ToString("0.##", CultureInfo.InvariantCulture) });
            }

            FuelRateItems.Clear();
            foreach (var itm in items)
            {
                FuelRateItems.Add(itm);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load dynamic fuel rates in Settings");
        }
    }

    private async Task LoadAsync()
    {
        await LoadDsmsAsync();
        var result = await _settingsRepo.GetSettingsAsync();
        if (result.Success && result.Data != null)
        {
            _settings = result.Data;
            PumpStationName = _settings.PumpStationName;
            StationSubtitle = _settings.StationSubtitle ?? "";
            StationAddress = _settings.StationAddress ?? "";
            StationContact = _settings.StationContact ?? "";
            OilCompany = string.IsNullOrWhiteSpace(_settings.OilCompany) ? "BPCL" : _settings.OilCompany;
            CustomLogoPath = _settings.CustomLogoPath ?? "";
            HsdRate = _settings.HsdRate.ToString(CultureInfo.CurrentCulture);
            MsIRate = _settings.MsIRate.ToString(CultureInfo.CurrentCulture);
            MsIIRate = _settings.MsIIRate.ToString(CultureInfo.CurrentCulture);
            CngRate = _settings.CngRate.ToString(CultureInfo.CurrentCulture);
            Shift1Manager = _settings.Shift1Manager ?? "";
            Shift2Manager = _settings.Shift2Manager ?? "";
            Shift3Manager = _settings.Shift3Manager ?? "";
            LastUpdated = _settings.LastUpdated.ToString("dd MMM yyyy hh:mm tt");
        }

        await LoadFuelRatesAsync();

        // Load Cloud Sync Settings
        var syncSettings = await _syncConfigService.GetSettingsAsync();
        SupabaseUrl = syncSettings.SupabaseUrl;
        SupabaseApiKey = syncSettings.SupabaseApiKey;
        SyncStationId = syncSettings.StationId;
        SyncMachineId = syncSettings.MachineId;
        SyncEnabled = syncSettings.SyncEnabled;
        IsMultiComputerEnabled = syncSettings.MultiComputerEnabled;
        LastSyncTimeDisplay = syncSettings.LastSyncTime.ToString("dd MMM yyyy hh:mm tt");

        if (IsMultiComputerEnabled)
        {
            await RefreshDevicesAsync();
        }

        var usersResult = await _userRepo.GetAllUsersAsync();
        Users.Clear();
        if (usersResult.Success)
            foreach (var u in usersResult.Data!) Users.Add(u);

        var creditorsResult = await _creditorRepo.GetAllActiveAsync();
        Creditors.Clear();
        if (creditorsResult.Success)
            foreach (var c in creditorsResult.Data!) Creditors.Add(c);
    }

    public static Dictionary<string, double> BuildFuelRatesDictionary(IEnumerable<FuelRateItemVm> items)
    {
        var ratesDict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.TankName))
                ratesDict[item.TankName] = item.Rate;
            if (!string.IsNullOrWhiteSpace(item.FuelType))
                ratesDict[item.FuelType] = item.Rate;
            if (!string.IsNullOrWhiteSpace(item.DisplayName))
                ratesDict[item.DisplayName] = item.Rate;

            // Normalize independent product aliases (never cross-contaminate SPEED with XP95 or Power)
            string combined = $"{item.FuelType} {item.TankName} {item.DisplayName}".ToUpperInvariant();
            if (combined.Contains("SPEED"))
            {
                ratesDict["Speed"] = item.Rate;
                ratesDict["SPEED"] = item.Rate;
            }
            else if (combined.Contains("XP95") || combined.Contains("XP-95"))
            {
                ratesDict["XP95"] = item.Rate;
            }
            else if (combined.Contains("POWER"))
            {
                ratesDict["Power"] = item.Rate;
                ratesDict["POWER"] = item.Rate;
            }
            else if (combined.Contains("MS-II"))
            {
                ratesDict["MS-II"] = item.Rate;
            }
            else if (combined.Contains("MS-I") || combined.Contains("MS - 20KL") || combined.Contains("PETROL"))
            {
                ratesDict["MS-I"] = item.Rate;
                ratesDict["MS"] = item.Rate;
            }
            else if (combined.Contains("HSD") && !combined.Contains("II"))
            {
                ratesDict["HSD"] = item.Rate;
            }
            else if (combined.Contains("CNG"))
            {
                ratesDict["CNG"] = item.Rate;
            }
        }
        return ratesDict;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (_settings == null) return;

        foreach (var item in FuelRateItems)
        {
            if (item.Rate <= 0)
            {
                StatusMessage = $"❌ Invalid rate for {item.DisplayName}. Please enter a positive number.";
                return;
            }
        }

        var ratesDict = BuildFuelRatesDictionary(FuelRateItems);

        _settings.FuelRates = ratesDict;
        _settings.PumpStationName = PumpStationName;

        // Legacy synchronizations
        foreach (var kvp in ratesDict)
        {
            var k = kvp.Key;
            var v = kvp.Value;
            if (k.Contains("HSD", StringComparison.OrdinalIgnoreCase) && !k.Contains("II", StringComparison.OrdinalIgnoreCase))
                _settings.HsdRate = v;
            else if (k.Contains("MS-I", StringComparison.OrdinalIgnoreCase) || k.Equals("MS", StringComparison.OrdinalIgnoreCase) || (k.Contains("MS -", StringComparison.OrdinalIgnoreCase) && !k.Contains("II", StringComparison.OrdinalIgnoreCase)))
                _settings.MsIRate = v;
            else if (k.Contains("MS-II", StringComparison.OrdinalIgnoreCase) || 
                     k.Contains("HSD - 20KL II", StringComparison.OrdinalIgnoreCase))
                _settings.MsIIRate = v;
            else if (k.Contains("CNG", StringComparison.OrdinalIgnoreCase))
                _settings.CngRate = v;
        }

        // Re-assign to ensure legacy scalar updates and canonical keys are serialized in FuelRatesJson
        _settings.FuelRates = ratesDict;

        // Enrich TankDefinitionsJson with authoritative rates for canonical cloud transport contract
        try
        {
            if (_stationConfigService != null)
            {
                var allTanks = await _stationConfigService.GetAllTanksAsync();
                foreach (var t in allTanks)
                {
                    t.Rate = _settings.GetRateFor(t.TankName, _settings.GetRateFor(t.FuelType, 0.0));
                }
                _settings.TankDefinitionsJson = System.Text.Json.JsonSerializer.Serialize(allTanks);
            }
            else if (!string.IsNullOrWhiteSpace(_settings.TankDefinitionsJson))
            {
                var tanks = System.Text.Json.JsonSerializer.Deserialize<List<FuelPro.Core.Models.TankDefinition>>(_settings.TankDefinitionsJson);
                if (tanks != null)
                {
                    foreach (var t in tanks)
                    {
                        t.Rate = _settings.GetRateFor(t.TankName, _settings.GetRateFor(t.FuelType, 0.0));
                    }
                    _settings.TankDefinitionsJson = System.Text.Json.JsonSerializer.Serialize(tanks);
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to enrich TankDefinitionsJson with rates in SaveSettingsAsync");
        }

        _settings.Shift1Manager = Shift1Manager?.Trim();
        _settings.Shift2Manager = Shift2Manager?.Trim();
        _settings.Shift3Manager = Shift3Manager?.Trim();
        _settings.StationSubtitle = StationSubtitle?.Trim();
        _settings.StationAddress = StationAddress?.Trim();
        _settings.StationContact = StationContact?.Trim();
        _settings.OilCompany = string.IsNullOrWhiteSpace(OilCompany) ? "BPCL" : OilCompany.Trim();
        _settings.CustomLogoPath = CustomLogoPath?.Trim();

        var result = await _settingsRepo.SaveSettingsAsync(_settings);
        if (result.Success)
        {
            // Propagate fuel rates change immediately to any open DSM Entry screens & data views without app restart
            FuelPro.Core.Services.DsmEntryService.RaiseFuelRatesChanged();

            // Notify main windows to reload their titles and logos
            if (System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w is Views.MainWindow) is System.Windows.Window mw && mw.DataContext is MainWindowViewModel mwVm)
            {
                mwVm.RefreshBranding();
            }
            if (System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w is Views.OwnerMainWindow) is System.Windows.Window omw && omw.DataContext is OwnerMainWindowViewModel omwVm)
            {
                omwVm.RefreshBranding();
            }

            if (_syncEngine != null)
            {
                _ = Task.Run(async () =>
                {
                    try { await _syncEngine.PushStationSnapshotToCloudAsync(); }
                    catch (Exception ex) { Serilog.Log.Warning(ex, "Background cloud snapshot push after settings save failed"); }
                });
            }
        }
        StatusMessage = result.Success ? "✅ Settings saved!" : $"❌ {result.Error}";
        if (result.Success) LastUpdated = DateTime.Now.ToString("dd MMM yyyy hh:mm tt");
    }

    private bool TryParseRate(string text, out double value)
    {
        if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
            return true;
        return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }

    [RelayCommand]
    private void BrowseCustomLogo()
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.bmp;*.svg)|*.png;*.jpg;*.jpeg;*.bmp;*.svg|All Files (*.*)|*.*",
                Title = "Select Custom Station / Company Logo"
            };
            if (dlg.ShowDialog() == true)
            {
                CustomLogoPath = dlg.FileName;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to browse custom logo");
        }
    }

    [RelayCommand]
    private void ClearCustomLogo()
    {
        CustomLogoPath = "";
    }

    [RelayCommand]
    private async Task CreateUserAsync()
    {
        if (string.IsNullOrWhiteSpace(NewUsername) || string.IsNullOrWhiteSpace(NewPin))
        {
            StatusMessage = "❌ Username and PIN are required";
            return;
        }
        var result = await _authService.CreateUserAsync(NewUsername, NewPin, NewRole);
        if (result.Success)
        {
            NewUsername = ""; NewPin = "";
            StatusMessage = "✅ User created!";
            await LoadAsync();
        }
        else StatusMessage = $"❌ {result.Error}";
    }

    [RelayCommand]
    private async Task ChangePinAsync()
    {
        if (_authService.CurrentUser == null) return;
        var result = await _authService.ChangePinAsync(
            _authService.CurrentUser.UserId, CurrentPin, NewPinChange);
        PinStatusMessage = result.Success ? "✅ PIN changed!" : $"❌ {result.Error}";
        CurrentPin = ""; NewPinChange = "";
    }

    [RelayCommand]
    private async Task DeleteUserAsync(User? user)
    {
        if (user == null) return;
        var result = await _userRepo.DeleteUserAsync(user.UserId);
        if (result.Success) await LoadAsync();
    }



    [RelayCommand]
    private async Task AddCreditorAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCreditorName))
        {
            CreditorStatusMessage = "❌ Debtor Name is required";
            return;
        }
        var creditor = new Creditor
        {
            Name = NewCreditorName.Trim(),
            Phone = string.IsNullOrWhiteSpace(NewCreditorPhone) ? null : NewCreditorPhone.Trim()
        };
        var result = await _creditorRepo.AddAsync(creditor);
        if (result.Success)
        {
            if (!string.IsNullOrWhiteSpace(NewCreditorVehicleNumber))
            {
                try
                {
                    var vehicleRepo = App.Services.GetRequiredService<IDebtorVehicleRepository>();
                    await vehicleRepo.AddAsync(new DebtorVehicle
                    {
                        CreditorId = result.Data!.CreditorId,
                        VehicleNumber = NewCreditorVehicleNumber.Trim().ToUpperInvariant(),
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    });
                }
                catch (Exception) { /* Ignored */ }
            }

            NewCreditorName = "";
            NewCreditorPhone = "";
            NewCreditorVehicleNumber = "";
            CreditorStatusMessage = "✅ Debtor added!";
            FuelPro.Core.Services.DsmEntryService.RaiseDebtorChanged();
            await LoadAsync();
        }
        else CreditorStatusMessage = $"❌ {result.Error}";
    }

    [RelayCommand]
    private async Task ToggleCreditorActiveAsync(Creditor? creditor)
    {
        if (creditor == null) return;
        var result = await _creditorRepo.UpdateAsync(creditor);
        if (result.Success)
        {
            FuelPro.Core.Services.DsmEntryService.RaiseDebtorChanged();
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteCreditorAsync(Creditor? creditor)
    {
        if (creditor == null) return;
        var result = await _creditorRepo.SoftDeleteAsync(creditor.CreditorId);
        if (result.Success)
        {
            FuelPro.Core.Services.DsmEntryService.RaiseDebtorChanged();
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task AddVehicleAsync()
    {
        if (SelectedCreditor == null) return;
        if (string.IsNullOrWhiteSpace(NewVehicleNumber))
        {
            CreditorStatusMessage = "❌ Vehicle number is required";
            return;
        }

        try
        {
            var vehicleRepo = App.Services.GetRequiredService<IDebtorVehicleRepository>();
            var vehicle = new DebtorVehicle
            {
                CreditorId = SelectedCreditor.CreditorId,
                VehicleNumber = NewVehicleNumber.Trim().ToUpperInvariant(),
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            var result = await vehicleRepo.AddAsync(vehicle);
            if (result.Success)
            {
                NewVehicleNumber = "";
                CreditorStatusMessage = "✅ Vehicle number added!";
                await LoadSelectedCreditorVehiclesAsync();
            }
            else
            {
                CreditorStatusMessage = $"❌ {result.Error}";
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to add vehicle in settings");
            CreditorStatusMessage = "❌ Error adding vehicle";
        }
    }

    [RelayCommand]
    private async Task DeleteVehicleAsync(DebtorVehicle? vehicle)
    {
        if (vehicle == null) return;

        try
        {
            var vehicleRepo = App.Services.GetRequiredService<IDebtorVehicleRepository>();
            var result = await vehicleRepo.DeleteAsync(vehicle.DebtorVehicleId);
            if (result.Success)
            {
                CreditorStatusMessage = "✅ Vehicle number deleted!";
                await LoadSelectedCreditorVehiclesAsync();
            }
            else
            {
                CreditorStatusMessage = $"❌ {result.Error}";
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to delete vehicle in settings");
            CreditorStatusMessage = "❌ Error deleting vehicle";
        }
    }

    // License Management
    [ObservableProperty] private string _licenseProductName = "Unknown";
    [ObservableProperty] private string _licenseStatus = "Unknown";
    [ObservableProperty] private string _licenseCustomerName = "";
    [ObservableProperty] private string _licenseBusinessName = "";
    [ObservableProperty] private string _licenseMobileNumber = "";
    [ObservableProperty] private string _licenseDeviceId = "";
    [ObservableProperty] private string _licenseType = "";
    [ObservableProperty] private string _licenseExpiryDate = "";
    [ObservableProperty] private string _licenseActivationDate = "";
    [ObservableProperty] private string _licenseStatusColor = "#F44336";

    private void LoadLicenseInfo()
    {
        LicenseDeviceId = DeviceIdentifier.GetDeviceId();
        var validation = _licenseManager.ValidateLicense();
        if (validation.IsValid && validation.License != null)
        {
            var lic = validation.License;
            LicenseProductName = lic.ProductName switch
            {
                "FPL" => "PyroSync",
                "VKD" => "PyroSync",
                "ZPA" => "PyroSync",
                "PSC" => "PyroSync",
                _ => lic.ProductName
            };
            LicenseCustomerName = lic.CustomerName;
            LicenseBusinessName = lic.BusinessName;
            LicenseMobileNumber = lic.MobileNumber;
            LicenseType = lic.LicenseType;
            LicenseActivationDate = lic.ActivationDate.ToString("dd MMM yyyy");
            LicenseExpiryDate = lic.ExpiryDate.HasValue ? lic.ExpiryDate.Value.ToString("dd MMM yyyy") : "N/A (Lifetime)";
            
            if (lic.LicenseType == "Trial")
            {
                var daysLeft = (lic.ExpiryDate!.Value - DateTime.Now).Days;
                LicenseStatus = $"Trial Active ({daysLeft} days left)";
                LicenseStatusColor = "#FF9800";
            }
            else if (lic.LicenseType == "Annual")
            {
                var daysLeft = (lic.ExpiryDate!.Value - DateTime.Now).Days;
                LicenseStatus = $"Annual Active ({daysLeft} days left)";
                LicenseStatusColor = "#FF9800";
            }
            else if (lic.LicenseType == "Demo")
            {
                var daysLeft = (lic.ExpiryDate!.Value - DateTime.Now).Days;
                LicenseStatus = $"Demo Active ({daysLeft} days left)";
                LicenseStatusColor = "#FF9800";
            }
            else
            {
                LicenseStatus = "Active (Lifetime)";
                LicenseStatusColor = "#4CAF50";
            }
        }
        else
        {
            LicenseStatus = $"Unlicensed/Invalid ({validation.ErrorMessage})";
            LicenseStatusColor = "#F44336";
        }
    }

    [RelayCommand]
    private void ExportLicenseInfo()
    {
        var text = $"Product: {LicenseProductName}\n" +
                   $"Status: {LicenseStatus}\n" +
                   $"Device ID: {LicenseDeviceId}\n" +
                   $"Customer Name: {LicenseCustomerName}\n" +
                   $"Business Name: {LicenseBusinessName}\n" +
                   $"Mobile: {LicenseMobileNumber}\n" +
                   $"Type: {LicenseType}\n" +
                   $"Activated: {LicenseActivationDate}\n" +
                   $"Expires: {LicenseExpiryDate}";
        
        System.Windows.Clipboard.SetText(text);
        System.Windows.MessageBox.Show("License details copied to clipboard!", "PyroSync — License Management", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    [RelayCommand]
    private async Task SaveSyncSettingsAsync()
    {
        try
        {
            var newStationId = SyncStationId.Trim();
            var settings = await _syncConfigService.GetSettingsAsync();
            var oldStationId = settings.StationId;
            FuelPro.Sync.SyncConfigService.ApplyAdminSyncSettings(
                settings,
                SupabaseUrl,
                SupabaseApiKey,
                newStationId,
                SyncMachineId,
                SyncEnabled,
                IsMultiComputerEnabled,
                _syncEngine.CurrentStatus.LastSyncTime);
            await _syncConfigService.SaveSettingsAsync(settings);

            SyncStatusMessage = "✅ Sync settings saved!";

            // Safely attach to the station (pulling first if fresh workstation) and synchronize
            await _syncEngine.AttachToStationAndSyncAsync(oldStationId, newStationId);

            if (IsMultiComputerEnabled)
            {
                await RefreshDevicesAsync();
            }
        }
        catch (System.Exception ex)
        {
            SyncStatusMessage = $"❌ Save error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void CopyStationId()
    {
        if (!string.IsNullOrEmpty(SyncStationId))
        {
            System.Windows.Clipboard.SetText(SyncStationId);
        }
    }

    [RelayCommand]
    private void CopyPairingCode()
    {
        if (!string.IsNullOrEmpty(PairingCodeDisplay))
        {
            System.Windows.Clipboard.SetText(PairingCodeDisplay);
            MultiComputerStatusMessage = "✅ Pairing code copied to clipboard!";
        }
    }

    [RelayCommand]
    private async Task ForceSyncAsync()
    {
        SyncStatusMessage = "⏳ Syncing...";
        try
        {
            await _syncEngine.ForceSyncAsync();
            var status = _syncEngine.CurrentStatus;
            LastSyncTimeDisplay = status.LastSyncTime.ToString("dd MMM yyyy hh:mm tt");
            SyncStatusMessage = status.IsConnected ? $"✅ Sync successful: {status.StatusMessage}" : $"❌ Sync failed: {status.StatusMessage}";
        }
        catch (System.Exception ex)
        {
            SyncStatusMessage = $"❌ Sync error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RefreshDevicesAsync()
    {
        if (_pairingService == null || string.IsNullOrWhiteSpace(SyncStationId)) return;
        try
        {
            var devices = await _pairingService.ListStationDevicesAsync(SyncStationId.Trim());
            StationDevices.Clear();
            foreach (var d in devices)
            {
                StationDevices.Add(d);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to refresh station devices list");
        }
    }

    [RelayCommand]
    public async Task GeneratePairingCodeAsync()
    {
        if (_pairingService == null) return;
        if (!IsMultiComputerEnabled)
        {
            PairingCodeDisplay = "";
            MultiComputerStatusMessage = "❌ Enable Multi-Computer Network Mode and save Cloud Settings before generating a pairing code.";
            return;
        }
        if (string.IsNullOrWhiteSpace(SyncStationId))
        {
            PairingCodeDisplay = "";
            MultiComputerStatusMessage = "❌ Station ID is required before generating a pairing code.";
            return;
        }
        if (string.IsNullOrWhiteSpace(SupabaseUrl) || string.IsNullOrWhiteSpace(SupabaseApiKey))
        {
            PairingCodeDisplay = "";
            MultiComputerStatusMessage = "❌ Supabase URL and API key must be configured before generating a pairing code.";
            return;
        }
        if (SupabaseApiKey.Contains("service_role", StringComparison.OrdinalIgnoreCase))
        {
            PairingCodeDisplay = "";
            MultiComputerStatusMessage = "❌ Administrative service_role keys cannot be used for pairing. Enter the standard client API key.";
            return;
        }
        if (!SyncEnabled)
        {
            PairingCodeDisplay = "";
            MultiComputerStatusMessage = "❌ Enable Cloud Synchronization and save Cloud Settings before generating a pairing code.";
            return;
        }

        IsPairingInProgress = true;
        PairingCodeDisplay = "";
        MultiComputerStatusMessage = "Generating secure pairing code...";
        try
        {
            // Persist current explicit UI cloud & multi-computer settings before publishing code
            var currentSettings = await _syncConfigService.GetSettingsAsync();
            FuelPro.Sync.SyncConfigService.ApplyAdminSyncSettings(
                currentSettings,
                SupabaseUrl,
                SupabaseApiKey,
                SyncStationId,
                string.IsNullOrWhiteSpace(SyncMachineId) ? currentSettings.MachineId : SyncMachineId,
                SyncEnabled,
                IsMultiComputerEnabled,
                _syncEngine.CurrentStatus.LastSyncTime);
            await _syncConfigService.SaveSettingsAsync(currentSettings);
            SyncMachineId = currentSettings.MachineId;

            var result = await _pairingService.GeneratePairingCodeAsync();
            if (result.Success && !string.IsNullOrEmpty(result.Data))
            {
                PairingCodeDisplay = result.Data;
                System.Windows.Clipboard.SetText(result.Data);
                MultiComputerStatusMessage = "✅ Code copied to clipboard! Valid for 15 minutes.";
                await RefreshDevicesAsync();
            }
            else
            {
                PairingCodeDisplay = "";
                MultiComputerStatusMessage = $"❌ {result.Error}";
            }
        }
        catch (Exception ex)
        {
            PairingCodeDisplay = "";
            MultiComputerStatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsPairingInProgress = false;
        }
    }

    [RelayCommand]
    public async Task RedeemPairingCodeAsync()
    {
        if (_pairingService == null) return;
        if (!IsMultiComputerEnabled)
        {
            MultiComputerStatusMessage = "❌ Enable Multi-Computer Network Mode before connecting with a pairing code.";
            return;
        }
        if (string.IsNullOrWhiteSpace(PairingCodeInput))
        {
            MultiComputerStatusMessage = "❌ Please enter a pairing code.";
            return;
        }
        if (string.IsNullOrWhiteSpace(SupabaseUrl) || string.IsNullOrWhiteSpace(SupabaseApiKey))
        {
            MultiComputerStatusMessage = "❌ Cloud URL and API key must be configured to redeem code.";
            return;
        }
        if (SupabaseApiKey.Contains("service_role", StringComparison.OrdinalIgnoreCase))
        {
            MultiComputerStatusMessage = "❌ Administrative service_role keys cannot be used for pairing. Enter the standard client API key.";
            return;
        }

        IsPairingInProgress = true;
        MultiComputerStatusMessage = "Redeeming pairing code and connecting...";
        try
        {
            var result = await _pairingService.RedeemPairingCodeAsync(PairingCodeInput, DeviceFriendlyName, SupabaseUrl.Trim(), SupabaseApiKey.Trim());
            if (result.Success && result.Data != null)
            {
                SyncStationId = result.Data.StationId;
                IsMultiComputerEnabled = true;
                PairingCodeInput = "";
                MultiComputerStatusMessage = "✅ Successfully paired! Synchronization activated.";
                await RefreshDevicesAsync();
                await ForceSyncAsync();
            }
            else
            {
                MultiComputerStatusMessage = $"❌ {result.Error}";
            }
        }
        catch (Exception ex)
        {
            MultiComputerStatusMessage = $"❌ Error: {ex.Message}";
        }
        finally
        {
            IsPairingInProgress = false;
        }
    }

    [RelayCommand]
    public async Task RevokeDeviceAsync(StationDevice? device)
    {
        if (_pairingService == null) return;
        var target = device ?? SelectedDevice;
        if (target == null) return;

        if (string.Equals(target.MachineId, SyncMachineId, StringComparison.OrdinalIgnoreCase))
        {
            MultiComputerStatusMessage = "⚠️ You cannot revoke authorization for the current computer.";
            return;
        }

        var success = await _pairingService.RevokeDeviceAsync(SyncStationId, target.MachineId, "Revoked by Administrator");
        if (success)
        {
            MultiComputerStatusMessage = $"✅ Device '{target.MachineName}' revoked.";
            await RefreshDevicesAsync();
        }
        else
        {
            MultiComputerStatusMessage = "❌ Failed to revoke device.";
        }
    }

    // ───────────────────────────────────────────────
    // DSM PROFILE MANAGEMENT (STAFF DIRECTORY)
    // ───────────────────────────────────────────────
    public ObservableCollection<DsmProfile> DsmProfiles { get; } = new();
    [ObservableProperty] private string _newDsmName = "";
    [ObservableProperty] private DsmProfile? _editingDsmProfile;
    [ObservableProperty] private string _editingDsmName = "";
    [ObservableProperty] private bool _isEditingDsm;
    [ObservableProperty] private string _newDsmPhone = "";
    [ObservableProperty] private string _newDsmSalaryType = "FixedMonthly";
    [ObservableProperty] private double _newDsmBaseSalary = 12000;
    [ObservableProperty] private string _dsmStatusMessage = "";
    public string[] DsmSalaryTypeOptions { get; } = { "FixedMonthly", "PerShift", "PerDay" };

    [RelayCommand]
    public async Task LoadDsmsAsync()
    {
        try
        {
            DsmProfiles.Clear();
            if (_profileRepo != null)
            {
                var res = await _profileRepo.GetAllAsync();
                if (res.Success && res.Data != null)
                {
                    foreach (var d in res.Data.OrderBy(d => d.DsmName))
                    {
                        DsmProfiles.Add(d);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load DSM profiles in Settings");
        }
    }

    [RelayCommand]
    public async Task AddDsmAsync()
    {
        if (string.IsNullOrWhiteSpace(NewDsmName))
        {
            DsmStatusMessage = "❌ DSM name is required.";
            return;
        }

        try
        {
            var profile = new DsmProfile
            {
                DsmName = NewDsmName.Trim(),
                MobileNumber = NewDsmPhone.Trim(),
                SalaryType = NewDsmSalaryType,
                BaseSalary = NewDsmBaseSalary,
                JoiningDate = DateTime.Today
            };

            if (_profileRepo != null)
            {
                var res = await _profileRepo.AddAsync(profile);
                if (res.Success)
                {
                    NewDsmName = "";
                    NewDsmPhone = "";
                    DsmStatusMessage = "✅ DSM added successfully!";
                    FuelPro.Core.Services.DsmEntryService.RaiseDsmProfileChanged();
                    await LoadDsmsAsync();
                }
                else
                {
                    DsmStatusMessage = $"❌ {res.Error}";
                }
            }
        }
        catch (Exception ex)
        {
            DsmStatusMessage = $"❌ Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void EditDsm(DsmProfile? profile)
    {
        if (profile == null) return;

        EditingDsmProfile = profile;
        EditingDsmName = profile.DsmName;
        IsEditingDsm = true;
        DsmStatusMessage = "";
    }

    [RelayCommand]
    public void CancelDsmEdit()
    {
        EditingDsmProfile = null;
        EditingDsmName = "";
        IsEditingDsm = false;
    }

    [RelayCommand]
    public async Task SaveDsmEditAsync()
    {
        var profile = EditingDsmProfile;
        if (profile == null || _profileRepo == null) return;

        if (string.IsNullOrWhiteSpace(EditingDsmName))
        {
            DsmStatusMessage = "❌ DSM name is required.";
            return;
        }

        try
        {
            // Update only the selected master profile, retaining its stable ID and other profile fields.
            var updatedProfile = new DsmProfile
            {
                DsmProfileId = profile.DsmProfileId,
                DsmName = EditingDsmName.Trim(),
                SalaryType = profile.SalaryType,
                BaseSalary = profile.BaseSalary,
                JoiningDate = profile.JoiningDate,
                MobileNumber = profile.MobileNumber,
                PendingAdvance = profile.PendingAdvance,
                MonthlyAdvanceDeduction = profile.MonthlyAdvanceDeduction
            };

            var result = await _profileRepo.UpdateAsync(updatedProfile);
            if (!result.Success)
            {
                DsmStatusMessage = $"❌ {result.Error}";
                return;
            }

            var oldName = profile.DsmName;
            var updatedName = updatedProfile.DsmName;
            CancelDsmEdit();
            DsmStatusMessage = $"✅ DSM name updated to {updatedName}.";
            if (!string.Equals(oldName, updatedName, StringComparison.OrdinalIgnoreCase))
            {
                FuelPro.Core.Services.DsmEntryService.RaiseDsmProfileRenamed(oldName, updatedName);
            }
            else
            {
                FuelPro.Core.Services.DsmEntryService.RaiseDsmProfileChanged();
            }
            await LoadDsmsAsync();
        }
        catch (Exception ex)
        {
            DsmStatusMessage = $"❌ Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ToggleDsmActiveAsync(DsmProfile? profile)
    {
        if (profile == null || _profileRepo == null) return;
        try
        {
            await _profileRepo.UpdateAsync(profile);
            FuelPro.Core.Services.DsmEntryService.RaiseDsmProfileChanged();
            await LoadDsmsAsync();
            DsmStatusMessage = $"Updated profile for {profile.DsmName}.";
        }
        catch (Exception ex)
        {
            DsmStatusMessage = $"❌ Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task DeleteDsmAsync(DsmProfile? profile)
    {
        if (profile == null || _profileRepo == null) return;
        var confirm = System.Windows.MessageBox.Show($"Are you sure you want to delete DSM '{profile.DsmName}'?", "Confirm Delete", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            var deletedName = profile.DsmName;
            var res = await _profileRepo.DeleteAsync(profile.DsmProfileId);
            if (res.Success)
            {
                DsmProfiles.Remove(profile);
                DsmStatusMessage = $"✅ Deleted {deletedName}.";
                FuelPro.Core.Services.DsmEntryService.RaiseDsmProfileDeleted(deletedName);
            }
            else
            {
                DsmStatusMessage = $"❌ {res.Error}";
            }
        }
        catch (Exception ex)
        {
            DsmStatusMessage = $"❌ Error: {ex.Message}";
        }
    }
}

