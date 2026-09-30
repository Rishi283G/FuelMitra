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
    private readonly IStationConfigurationService? _stationConfigService;

    [ObservableProperty] private string _pumpStationName = "";
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

        for (var month = 1; month <= 12; month++)
        {
            MonthOptions.Add(new KeyValuePair<int, string>(month, CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month)));
        }
        var thisYear = DateTime.Today.Year;
        for (var year = thisYear - 2; year <= thisYear + 2; year++)
        {
            YearOptions.Add(year);
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
        LastSyncTimeDisplay = syncSettings.LastSyncTime.ToString("dd MMM yyyy hh:mm tt");

        var usersResult = await _userRepo.GetAllUsersAsync();
        Users.Clear();
        if (usersResult.Success)
            foreach (var u in usersResult.Data!) Users.Add(u);

        var creditorsResult = await _creditorRepo.GetAllActiveAsync();
        Creditors.Clear();
        if (creditorsResult.Success)
            foreach (var c in creditorsResult.Data!) Creditors.Add(c);
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (_settings == null) return;

        var ratesDict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in FuelRateItems)
        {
            if (item.Rate <= 0)
            {
                StatusMessage = $"❌ Invalid rate for {item.DisplayName}. Please enter a positive number.";
                return;
            }
            if (!string.IsNullOrWhiteSpace(item.TankName))
                ratesDict[item.TankName] = item.Rate;
            if (!string.IsNullOrWhiteSpace(item.FuelType))
                ratesDict[item.FuelType] = item.Rate;
            if (!string.IsNullOrWhiteSpace(item.DisplayName))
                ratesDict[item.DisplayName] = item.Rate;
        }

        _settings.FuelRates = ratesDict;
        _settings.PumpStationName = PumpStationName;

        // Legacy synchronizations
        foreach (var kvp in ratesDict)
        {
            var k = kvp.Key;
            var v = kvp.Value;
            if (k.Contains("HSD", StringComparison.OrdinalIgnoreCase) && !k.Contains("II", StringComparison.OrdinalIgnoreCase))
                _settings.HsdRate = v;
            else if (k.Contains("MS-I", StringComparison.OrdinalIgnoreCase) || k.Equals("MS", StringComparison.OrdinalIgnoreCase) || k.Contains("MS -", StringComparison.OrdinalIgnoreCase))
                _settings.MsIRate = v;
            else if (k.Contains("MS-II", StringComparison.OrdinalIgnoreCase) || k.Contains("HSD - 20KL II", StringComparison.OrdinalIgnoreCase))
                _settings.MsIIRate = v;
            else if (k.Contains("CNG", StringComparison.OrdinalIgnoreCase))
                _settings.CngRate = v;
        }

        _settings.Shift1Manager = Shift1Manager?.Trim();
        _settings.Shift2Manager = Shift2Manager?.Trim();
        _settings.Shift3Manager = Shift3Manager?.Trim();

        var result = await _settingsRepo.SaveSettingsAsync(_settings);
        if (result.Success)
        {
            // Notify main windows to reload their titles and logos
            if (System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w is Views.MainWindow) is System.Windows.Window mw && mw.DataContext is MainWindowViewModel mwVm)
            {
                mwVm.RefreshBranding();
            }
            if (System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w is Views.OwnerMainWindow) is System.Windows.Window omw && omw.DataContext is OwnerMainWindowViewModel omwVm)
            {
                omwVm.RefreshBranding();
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

    // DSM Short Report
    [ObservableProperty] private string? _selectedReportDsm;
    [ObservableProperty] private int _selectedReportMonthNumber = DateTime.Today.Month;
    [ObservableProperty] private int _selectedReportYear = DateTime.Today.Year;
    public ObservableCollection<FuelPro.Core.DTOs.DsmShortReportRowDto> DsmShortReportRows { get; } = new();
    [ObservableProperty] private double _totalShortAmount;
    [ObservableProperty] private string _reportStatusMessage = "";
    public ObservableCollection<KeyValuePair<int, string>> MonthOptions { get; } = new();
    public ObservableCollection<int> YearOptions { get; } = new();

    [RelayCommand]
    private async Task GenerateShortReportAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedReportDsm))
        {
            ReportStatusMessage = "❌ Please select a DSM";
            return;
        }

        ReportStatusMessage = "⏳ Generating report...";
        var dsmRepo = App.Services.GetRequiredService<IDsmEntryRepository>();
        var calcService = App.Services.GetRequiredService<IDsmCalculationService>();

        var result = await dsmRepo.GetEntriesForDsmAndMonthAsync(SelectedReportDsm, SelectedReportYear, SelectedReportMonthNumber);
        DsmShortReportRows.Clear();
        TotalShortAmount = 0;

        if (!result.Success)
        {
            ReportStatusMessage = $"❌ {result.Error}";
            return;
        }

        foreach (var entry in result.Data!)
        {
            if (entry.ReconciledToPumpId.HasValue)
            {
                continue;
            }

            var cash1 = entry.CashDenominations.Where(c => c.CashType == "Cash1").Sum(c => c.TotalAmount);
            var cash2 = entry.CashDenominations.Where(c => c.CashType == "Cash2").Sum(c => c.TotalAmount);
            
            var dto = new FuelPro.Core.DTOs.DsmEntryDto
            {
                NozzleReadings = entry.NozzleReadings.Select(n => new FuelPro.Core.DTOs.NozzleReadingDto { Amount = (decimal)n.Amount }).ToList(),
                PaymentCollection = new FuelPro.Core.DTOs.PaymentCollectionDto
                {
                    PhonePe = (decimal)((entry.PaymentCollection?.PhonePe ?? 0) + (entry.PaymentCollection?.PhonePeCard ?? 0)),
                    CreditCard = (decimal)((entry.PaymentCollection?.CreditCard ?? 0) + (entry.PaymentCollection?.PetroCard ?? 0)),
                    CashDeposit = (decimal)(cash1 + cash2 + (entry.PaymentCollection?.CashDeposit ?? 0)),
                    PhysicalCash = 0
                },
                DebitEntries = entry.DebitEntries.Select(d => new FuelPro.Core.DTOs.DebitEntryDto { Amount = (decimal)d.Amount }).ToList(),
                TestingEntries = entry.TestingEntries.Select(t => new FuelPro.Core.DTOs.TestingEntryDto { Amount = (decimal)t.Amount }).ToList(),
                Expenses = entry.Expenses.Select(e => new FuelPro.Core.DTOs.ExpenseDto { Amount = (decimal)e.Amount }).ToList()
            };

            var calcResult = calcService.Calculate(dto);
            // Mismatch < 0 means short (Collection < GrossSales)
            if (calcResult.Mismatch < 0)
            {
                var shortAmount = Math.Abs((double)calcResult.Mismatch);
                DsmShortReportRows.Add(new FuelPro.Core.DTOs.DsmShortReportRowDto
                {
                    Date = entry.Shift?.ShiftDate ?? DateTime.Today,
                    ShiftType = entry.Shift?.ShiftType ?? "",
                    ShortAmount = shortAmount
                });
                TotalShortAmount += shortAmount;
            }
        }

        if (DsmShortReportRows.Count == 0)
        {
            ReportStatusMessage = "✅ No shorts found for this month.";
        }
        else
        {
            ReportStatusMessage = $"✅ Found {DsmShortReportRows.Count} short entries.";
        }
    }

    [RelayCommand]
    private void PrintShortReport()
    {
        if (DsmShortReportRows.Count == 0)
        {
            System.Windows.MessageBox.Show("No report data to print. Please generate the report first.", "Short Report", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        try
        {
            var printService = App.Services.GetRequiredService<PrintService>();
            var monthName = MonthOptions.FirstOrDefault(m => m.Key == SelectedReportMonthNumber).Value ?? SelectedReportMonthNumber.ToString();

            var summaryCards = new List<GenericGridPrintCard>
            {
                new() { Label = "Total Short Amount", Value = "₹" + TotalShortAmount.ToString("N2"), Highlight = true }
            };

            var headers = new List<string> { "Date", "Shift", "Short Amount" };

            var rows = DsmShortReportRows.Select(r => new List<string>
            {
                r.Date.ToString("dd MMM yyyy"),
                r.ShiftType,
                "₹" + r.ShortAmount.ToString("N2")
            }).ToList();

            var printData = new GenericGridPrintData
            {
                Title = "DSM Short Report",
                Subtitle = $"DSM: {SelectedReportDsm}  |  Period: {monthName} {SelectedReportYear}",
                SummaryCards = summaryCards,
                Headers = headers,
                Rows = rows,
                ShowSignatures = true
            };

            printService.PrintGenericGrid(printData);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to print DSM Short Report");
            System.Windows.MessageBox.Show($"Print failed: {ex.Message}", "Print Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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
            var settings = new FuelPro.Sync.SyncSettings
            {
                SupabaseUrl = SupabaseUrl.Trim(),
                SupabaseApiKey = SupabaseApiKey.Trim(),
                StationId = SyncStationId.Trim(),
                MachineId = SyncMachineId,
                SyncEnabled = SyncEnabled,
                LastSyncTime = _syncEngine.CurrentStatus.LastSyncTime
            };
            var newStationId = SyncStationId.Trim();
            await _syncConfigService.SaveSettingsAsync(settings);

            // Dynamically queue all historical data under the new Station ID so they sync immediately
            await _syncEngine.QueueAllHistoricalRecordsForStationAsync(newStationId);

            SyncStatusMessage = "✅ Sync settings saved!";
            
            // Trigger sync update
            await _syncEngine.ForceSyncAsync();
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

    // ───────────────────────────────────────────────
    // DSM PROFILE MANAGEMENT (STAFF DIRECTORY)
    // ───────────────────────────────────────────────
    public ObservableCollection<DsmProfile> DsmProfiles { get; } = new();
    [ObservableProperty] private string _newDsmName = "";
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
            var res = await _profileRepo.DeleteAsync(profile.DsmProfileId);
            if (res.Success)
            {
                DsmProfiles.Remove(profile);
                DsmStatusMessage = $"✅ Deleted {profile.DsmName}.";
                FuelPro.Core.Services.DsmEntryService.RaiseDsmProfileChanged();
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

