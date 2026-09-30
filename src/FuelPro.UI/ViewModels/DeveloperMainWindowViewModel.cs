        using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rashtra.Licensing;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FuelPro.UI.ViewModels;

public partial class DeveloperMainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AuthService _authService;
    private readonly IUserRepository _userRepo;
    private readonly SyncConfigService _syncConfigService;
    private readonly SyncEngine _syncEngine;
    private readonly Rashtra.Licensing.LicenseManager _licenseManager;
    private readonly IDuplicateDataInspectionService _inspectionService;
    private readonly DuplicateResolutionService _resolutionService;
    private readonly IFeatureToggleService _featureToggleService;
    private readonly ICollectionTypeService _collectionTypeService;
    private readonly IStationConfigurationService _stationConfigService;
    private bool _isSavingLayout;

    [ObservableProperty] private object? _currentView;

    partial void OnCurrentViewChanged(object? oldValue, object? newValue)
    {
        if (oldValue is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    [ObservableProperty] private int _selectedNavIndex;
    [ObservableProperty] private string _currentUser = "";
    [ObservableProperty] private string _currentDateTime = DateTime.Now.ToString("dd MMM yyyy  hh:mm tt");

    // Tab 5: Client Features
    public ObservableCollection<AppFeatureSetting> FeatureSettings { get; } = new();
    public ObservableCollection<AppFeatureSetting> FilteredFeatureSettings { get; } = new();
    [ObservableProperty] private string _selectedFeatureRoleFilter = "All";
    public string[] FeatureRoleFilterOptions { get; } = { "All", "Manager", "Owner", "Global" };
    [ObservableProperty] private string _featureStatusMessage = "";

    // Tab 6: Station & Collections
    public ObservableCollection<PumpCardVm> ConfiguredPumps { get; } = new();
    public ObservableCollection<TankDefinition> ConfiguredTanks { get; } = new();
    public ObservableCollection<ProductMaster> ConfiguredProducts { get; } = new();
    public ObservableCollection<CollectionTypeMaster> ConfiguredCollectionTypes { get; } = new();
    public ObservableCollection<string> AvailableTankNames { get; } = new();
    public ObservableCollection<string> AvailableFuelTypes { get; } = new();
    public ObservableCollection<StationLayoutPreset> SavedPresets { get; } = new();
    [ObservableProperty] private string _stationStatusMessage = "";
    [ObservableProperty] private string _collectionStatusMessage = "";
    [ObservableProperty] private string _shiftCycleStatusMessage = "";
    [ObservableProperty] private string _selectedShiftCycleOption = "2_SHIFT (Standard Date-to-Date: Shift A & Shift B)";
    public string[] ShiftCycleOptions { get; } = { "2_SHIFT (Standard Date-to-Date: Shift A & Shift B)", "3_SLOT_SPLIT (Overnight Midnight Split: Morning, Day, Night)" };
    [ObservableProperty] private string _presetCode = "";
    [ObservableProperty] private string _presetName = "";
    [ObservableProperty] private string _presetDescription = "";
    [ObservableProperty] private StationLayoutPreset? _selectedPreset;
    [ObservableProperty] private string _searchPresetCode = "";
    [ObservableProperty] private string _presetStatusMessage = "";
    public string[] AvailableCollectionCategories { get; } = { "Online", "Card", "Cash", "Other" };

    private bool _isLoadingPresetInternal;

    partial void OnSelectedPresetChanged(StationLayoutPreset? value)
    {
        if (value != null && !_isLoadingPresetInternal)
        {
            SearchPresetCode = value.PresetCode;
            PresetCode = value.PresetCode;
            PresetName = value.PresetName;
            PresetDescription = value.Description;
            PresetStatusMessage = $"Selected preset '{value.PresetName}' ({value.PresetCode}). Click '⚡ Load Code' to load onto canvas.";
        }
    }



    // Tab 1: User Management
    public ObservableCollection<User> Users { get; } = new();
    [ObservableProperty] private string _newUsername = "";
    [ObservableProperty] private string _newPin = "";
    [ObservableProperty] private UserRole _newRole = UserRole.Manager;
    [ObservableProperty] private string _userStatusMessage = "";
    public UserRole[] RoleOptions { get; } = { UserRole.Manager, UserRole.Owner, UserRole.Developer };

    // Tab 2: Cloud Config
    [ObservableProperty] private string _supabaseUrl = "";
    [ObservableProperty] private string _supabaseApiKey = "";
    [ObservableProperty] private string _supabaseServiceRoleKey = "";
    [ObservableProperty] private string _stationId = "";
    [ObservableProperty] private string _machineId = "";
    [ObservableProperty] private bool _syncEnabled;
    [ObservableProperty] private bool _isConfigLocked = true;
    [ObservableProperty] private string _configStatusMessage = "";
    [ObservableProperty] private bool _isCloudConnected;

    // Application Branding
    [ObservableProperty] private string _applicationLogoPath = "";
    [ObservableProperty] private string _applicationLogoDarkPath = "";
    [ObservableProperty] private string _logoStatusMessage = "";


    // Tab 3: Diagnostics
    public ObservableCollection<TableCountDto> TableCounts { get; } = new();
    [ObservableProperty] private string _diagnosticsMessage = "";
    [ObservableProperty] private string _syncLogsText = "";
    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private string _lastSyncTime = "—";
    [ObservableProperty] private int _pendingRecords;

    // Tab 4: License Management
    [ObservableProperty] private string _licenseStatus = "Unknown";
    [ObservableProperty] private string _licenseProductName = "FuelPro Lite";
    [ObservableProperty] private string _licenseCustomerName = "";
    [ObservableProperty] private string _licenseBusinessName = "";
    [ObservableProperty] private string _licenseMobileNumber = "";
    [ObservableProperty] private string _licenseType = "";
    [ObservableProperty] private string _licenseActivationDate = "";
    [ObservableProperty] private string _licenseExpiryDate = "";
    [ObservableProperty] private string _licenseDeviceId = "";

    // Tab 5: Data Integrity
    [ObservableProperty] private bool _isRunningIntegrityScan;
    [ObservableProperty] private string _integrityScanMessage = "Run a scan to check data integrity.";
    [ObservableProperty] private string _integrityScanTimestamp = "Never scanned";
    [ObservableProperty] private bool _integrityHasIssues;
    [ObservableProperty] private int _integrityDsmDuplicates;
    [ObservableProperty] private int _integrityDebitDuplicates;
    [ObservableProperty] private int _integrityRepaymentDuplicates;
    [ObservableProperty] private int _integrityOrphans;
    [ObservableProperty] private int _integrityBrokenFks;
    public ObservableCollection<DsmEntryDuplicateGroupVm> DuplicateDsmGroups { get; } = new();
    public ObservableCollection<DebitEntryDuplicateGroupVm> DuplicateDebitGroups { get; } = new();
    public ObservableCollection<RepaymentDuplicateGroupVm> DuplicateRepaymentGroups { get; } = new();
    public ObservableCollection<OrphanRecord> OrphanRecords { get; } = new();

    // Data Integrity Selection States
    [ObservableProperty] private bool _isAllDsmSelected;
    [ObservableProperty] private int _selectedDsmCount;

    [ObservableProperty] private bool _isAllDebitSelected;
    [ObservableProperty] private int _selectedDebitCount;

    [ObservableProperty] private bool _isAllRepaymentSelected;
    [ObservableProperty] private int _selectedRepaymentCount;

    // DSM Entry Inspector & Repair Tools
    [ObservableProperty] private string _dsmSearchText = "";
    [ObservableProperty] private bool _isLoadingRecentDsmEntries;
    public ObservableCollection<RecentDsmEntryInspectorDto> RecentDsmEntries { get; } = new();
    public ObservableCollection<RecentDsmEntryInspectorDto> FilteredRecentDsmEntries { get; } = new();

    partial void OnDsmSearchTextChanged(string value)
    {
        FilterRecentDsmEntries();
    }

    partial void OnSelectedNavIndexChanged(int value)
    {
        if (value == 4)
        {
            _ = LoadRecentDsmEntriesAsync();
        }
    }

    private void FilterRecentDsmEntries()
    {
        FilteredRecentDsmEntries.Clear();
        var text = (DsmSearchText ?? "").Trim();

        var query = RecentDsmEntries.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(text))
        {
            query = query.Where(e =>
                e.IdDisplay.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                e.DsmName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                e.PumpLabel.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                e.ShiftDate.ToString("dd-MMM-yyyy").Contains(text, StringComparison.OrdinalIgnoreCase) ||
                e.StatusMessage.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                e.Origin.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var item in query)
        {
            FilteredRecentDsmEntries.Add(item);
        }
    }

    private bool _isUpdatingSelection;

    partial void OnIsAllDsmSelectedChanged(bool value)
    {
        if (_isUpdatingSelection) return;
        _isUpdatingSelection = true;
        foreach (var group in DuplicateDsmGroups)
            group.IsSelected = value;
        _isUpdatingSelection = false;
        SelectedDsmCount = value ? DuplicateDsmGroups.Count : 0;
    }

    partial void OnIsAllDebitSelectedChanged(bool value)
    {
        if (_isUpdatingSelection) return;
        _isUpdatingSelection = true;
        foreach (var group in DuplicateDebitGroups)
            group.IsSelected = value;
        _isUpdatingSelection = false;
        SelectedDebitCount = value ? DuplicateDebitGroups.Count : 0;
    }

    partial void OnIsAllRepaymentSelectedChanged(bool value)
    {
        if (_isUpdatingSelection) return;
        _isUpdatingSelection = true;
        foreach (var group in DuplicateRepaymentGroups)
            group.IsSelected = value;
        _isUpdatingSelection = false;
        SelectedRepaymentCount = value ? DuplicateRepaymentGroups.Count : 0;
    }

    private void UpdateDsmSelectionState()
    {
        if (_isUpdatingSelection) return;
        _isUpdatingSelection = true;
        SelectedDsmCount = DuplicateDsmGroups.Count(g => g.IsSelected);
        IsAllDsmSelected = DuplicateDsmGroups.Count > 0 && SelectedDsmCount == DuplicateDsmGroups.Count;
        _isUpdatingSelection = false;
    }

    private void UpdateDebitSelectionState()
    {
        if (_isUpdatingSelection) return;
        _isUpdatingSelection = true;
        SelectedDebitCount = DuplicateDebitGroups.Count(g => g.IsSelected);
        IsAllDebitSelected = DuplicateDebitGroups.Count > 0 && SelectedDebitCount == DuplicateDebitGroups.Count;
        _isUpdatingSelection = false;
    }

    private void UpdateRepaymentSelectionState()
    {
        if (_isUpdatingSelection) return;
        _isUpdatingSelection = true;
        SelectedRepaymentCount = DuplicateRepaymentGroups.Count(g => g.IsSelected);
        IsAllRepaymentSelected = DuplicateRepaymentGroups.Count > 0 && SelectedRepaymentCount == DuplicateRepaymentGroups.Count;
        _isUpdatingSelection = false;
    }

    public DeveloperMainWindowViewModel(IFeatureToggleService featureToggleService)
    {
        _featureToggleService = featureToggleService;
        _serviceProvider = null!;
        _authService = null!;
        _userRepo = null!;
        _syncConfigService = null!;
        _syncEngine = null!;
        _licenseManager = null!;
        _inspectionService = null!;
        _resolutionService = null!;
        _collectionTypeService = null!;
        _stationConfigService = null!;
    }

    [ActivatorUtilitiesConstructor]
    public DeveloperMainWindowViewModel()
    {
        _serviceProvider = App.Services;
        _authService = _serviceProvider.GetRequiredService<AuthService>();
        _userRepo = _serviceProvider.GetRequiredService<IUserRepository>();
        _syncConfigService = _serviceProvider.GetRequiredService<SyncConfigService>();
        _syncEngine = _serviceProvider.GetRequiredService<SyncEngine>();
        _licenseManager = _serviceProvider.GetRequiredService<Rashtra.Licensing.LicenseManager>();
        _inspectionService = _serviceProvider.GetRequiredService<IDuplicateDataInspectionService>();
        _resolutionService = _serviceProvider.GetRequiredService<DuplicateResolutionService>();

        _featureToggleService = _serviceProvider.GetRequiredService<IFeatureToggleService>();
        _collectionTypeService = _serviceProvider.GetRequiredService<ICollectionTypeService>();
        _stationConfigService = _serviceProvider.GetRequiredService<IStationConfigurationService>();

        _stationConfigService.StationConfigurationChanged += () =>
        {
            if (_isSavingLayout) return;
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_isSavingLayout) return;
                _ = LoadStationAndCollectionsAsync();
            });
        };

        CurrentUser = _authService.CurrentUser?.Username ?? "Developer";

        // Listen to sync engine
        _syncEngine.SyncStatusChanged += (status) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                ConnectionStatus = status.IsConnected ? "Connected" : "Offline";
                LastSyncTime = status.LastSyncTime.ToString("dd MMM yyyy hh:mm tt");
                PendingRecords = status.PendingRecords;
                IsCloudConnected = status.IsConnected;
            });
        };

        var currentStatus = _syncEngine.CurrentStatus;
        ConnectionStatus = currentStatus.IsConnected ? "Connected" : "Offline";
        LastSyncTime = currentStatus.LastSyncTime.ToString("dd MMM yyyy hh:mm tt");
        PendingRecords = currentStatus.PendingRecords;
        IsCloudConnected = currentStatus.IsConnected;

        _ = RefreshUsersAsync();
        _ = LoadCloudConfigAsync();
        _ = LoadDiagnosticsAsync();
        _ = LoadFeaturesAsync();
        _ = LoadStationAndCollectionsAsync();
        LoadLicenseInfo();
    }

    // Nav commands
    [RelayCommand]
    private void SelectTab(string indexStr)
    {
        if (int.TryParse(indexStr, out var idx))
        {
            SelectedNavIndex = idx;
            if (idx == 2) // Diagnostics
            {
                _ = LoadDiagnosticsAsync();
            }
            else if (idx == 4) // Data Integrity
            {
                _ = LoadRecentDsmEntriesAsync();
            }
            else if (idx == 5) // Client Features
            {
                _ = LoadFeaturesAsync();
            }
        }
    }



    // User Management actions
    [RelayCommand]
    private async Task RefreshUsersAsync()
    {
        var result = await _userRepo.GetAllUsersAsync();
        Users.Clear();
        if (result.Success && result.Data != null)
        {
            foreach (var u in result.Data)
            {
                Users.Add(u);
            }
        }
    }

    [RelayCommand]
    private async Task CreateUserAsync()
    {
        UserStatusMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(NewUsername) || string.IsNullOrWhiteSpace(NewPin))
        {
            UserStatusMessage = "❌ Username and PIN are required";
            return;
        }

        var result = await _authService.CreateUserAsync(NewUsername, NewPin, NewRole.ToString());
        if (result.Success)
        {
            NewUsername = ""; NewPin = "";
            UserStatusMessage = "✅ User created successfully!";
            await RefreshUsersAsync();
        }
        else
        {
            UserStatusMessage = $"❌ {result.Error}";
        }
    }

    [RelayCommand]
    private async Task ToggleUserActiveAsync(User? user)
    {
        if (user == null) return;
        user.IsActive = !user.IsActive;
        var result = await _userRepo.UpdateUserAsync(user);
        if (!result.Success)
        {
            UserStatusMessage = $"❌ Failed to update user active state: {result.Error}";
            user.IsActive = !user.IsActive; // revert
        }
        else
        {
            await RefreshUsersAsync();
        }
    }

    [RelayCommand]
    private async Task ResetUserPinAsync(User? user)
    {
        if (user == null) return;

        // Prevent accidental reset of Developer credentials
        if (string.Equals(user.Role, "Developer", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(user.Username, "Developer", StringComparison.OrdinalIgnoreCase))
        {
            System.Windows.MessageBox.Show(
                "Developer PIN cannot be reset from the UI.\nUse the FuelPro.Maintenance tool if a reset is absolutely necessary.",
                "Operation Blocked", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }
        
        var randomPin = Random.Shared.Next(1000, 9999).ToString("D4");
        user.PinHash = BCrypt.Net.BCrypt.HashPassword(randomPin);
        user.MustChangePin = true;
        
        var result = await _userRepo.UpdateUserAsync(user);
        if (result.Success)
        {
            System.Windows.MessageBox.Show($"PIN for {user.Username} has been reset to: {randomPin}\n\nThey will be prompted to change it at next login.", 
                "PIN Reset Successful", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            await RefreshUsersAsync();
        }
        else
        {
            UserStatusMessage = $"❌ Failed to reset PIN: {result.Error}";
        }
    }

    // Cloud Config actions
    private async Task LoadCloudConfigAsync()
    {
        var settings = await _syncConfigService.GetSettingsAsync();
        SupabaseUrl = settings.SupabaseUrl;
        SupabaseApiKey = settings.SupabaseApiKey;
        SupabaseServiceRoleKey = settings.SupabaseServiceRoleKey;
        StationId = settings.StationId;
        MachineId = settings.MachineId;
        SyncEnabled = settings.SyncEnabled;
        IsCloudConnected = _syncEngine.CurrentStatus.IsConnected;

        // Load Application Logo Path from AppMeta
        using (var db = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var logoMeta = await db.AppMeta.FirstOrDefaultAsync(m => m.Key == "ApplicationLogoPath");
            if (logoMeta == null)
            {
                var appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro");
                ApplicationLogoPath = Path.Combine(appDataFolder, "logo_light.png");
            }
            else
            {
                ApplicationLogoPath = logoMeta.Value;
            }

            var logoDarkMeta = await db.AppMeta.FirstOrDefaultAsync(m => m.Key == "ApplicationLogoDarkPath");
            if (logoDarkMeta == null)
            {
                var appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro");
                ApplicationLogoDarkPath = Path.Combine(appDataFolder, "logo_dark.png");
            }
            else
            {
                ApplicationLogoDarkPath = logoDarkMeta.Value;
            }
        }
    }

    [RelayCommand]
    private void UnlockConfiguration()
    {
        IsConfigLocked = false;
        ConfigStatusMessage = "🔓 Configuration Unlocked. Be careful making edits to production values.";
    }

    [RelayCommand]
    private void GenerateStationId()
    {
        if (IsConfigLocked) return;
        var suffix = Guid.NewGuid().ToString("n").Substring(0, 6).ToUpperInvariant();
        StationId = $"RASHTRA-{suffix}";
        ConfigStatusMessage = "Generated new Station ID.";
    }

    [RelayCommand]
    private void CopyStationId()
    {
        if (!string.IsNullOrEmpty(StationId))
        {
            System.Windows.Clipboard.SetText(StationId);
            System.Windows.MessageBox.Show("Station ID copied to clipboard!", "Developer Tools", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(SupabaseUrl) || string.IsNullOrWhiteSpace(SupabaseApiKey))
        {
            ConfigStatusMessage = "❌ Supabase URL and API Key are required to test connection.";
            return;
        }

        ConfigStatusMessage = "⏳ Testing connection to Supabase...";
        try
        {
            var testClient = new SupabaseHttpClient();
            testClient.Configure(SupabaseUrl.Trim(), SupabaseApiKey.Trim());
            
            var response = await testClient.SendRequestAsync(System.Net.Http.HttpMethod.Get, "Users?limit=1");
            if (response.IsSuccessStatusCode)
            {
                ConfigStatusMessage = "✅ Connection successful! Supabase is reachable.";
                IsCloudConnected = true;
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                ConfigStatusMessage = $"❌ Connection failed (Status: {response.StatusCode}): {error}";
                IsCloudConnected = false;
            }
        }
        catch (Exception ex)
        {
            ConfigStatusMessage = $"❌ Connection exception: {ex.Message}";
            IsCloudConnected = false;
        }
    }

    [RelayCommand]
    private async Task SaveCloudConfigAsync()
    {
        if (IsConfigLocked) return;

        try
        {
            if (SyncEnabled && string.IsNullOrWhiteSpace(StationId))
            {
                ConfigStatusMessage = "❌ Station ID is required when Cloud Sync is enabled.";
                return;
            }

            var currentSettings = await _syncConfigService.GetSettingsAsync();
            var normalizedStationId = System.Text.RegularExpressions.Regex.Replace(StationId.Trim(), @"\s+", "_").ToUpperInvariant();
            StationId = normalizedStationId;

            if (!string.IsNullOrEmpty(currentSettings.StationId) && 
                !string.Equals(currentSettings.StationId, normalizedStationId, StringComparison.OrdinalIgnoreCase))
            {
                var confirmResult = System.Windows.MessageBox.Show(
                    "Warning: Changing the Station ID will move this computer to a different data partition.\n\nDo you want to continue?",
                    "Confirm Station ID Change",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                
                if (confirmResult != System.Windows.MessageBoxResult.Yes)
                {
                    ConfigStatusMessage = "Change cancelled.";
                    return;
                }
            }

            var settings = new SyncSettings
            {
                SupabaseUrl = SupabaseUrl.Trim(),
                SupabaseApiKey = SupabaseApiKey.Trim(),
                SupabaseServiceRoleKey = SupabaseServiceRoleKey.Trim(),
                StationId = normalizedStationId,
                MachineId = MachineId.Trim(),
                SyncEnabled = SyncEnabled,
                LastSyncTime = _syncEngine.CurrentStatus.LastSyncTime
            };

            await _syncConfigService.SaveSettingsAsync(settings);

            // Dynamically queue all historical data under the new Station ID so they sync immediately
            await _syncEngine.QueueAllHistoricalRecordsForStationAsync(normalizedStationId);

            IsConfigLocked = true;
            ConfigStatusMessage = "✅ Cloud settings saved and locked!";
            
            await _syncEngine.ForceSyncAsync();
        }
        catch (Exception ex)
        {
            ConfigStatusMessage = $"❌ Save error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveLogoPathAsync()
    {
        try
        {
            using var db = _serviceProvider.GetRequiredService<FuelProDbContext>();
            
            // Save light logo
            var logoMeta = await db.AppMeta.FirstOrDefaultAsync(m => m.Key == "ApplicationLogoPath");
            if (logoMeta == null)
            {
                db.AppMeta.Add(new AppMeta { Key = "ApplicationLogoPath", Value = ApplicationLogoPath });
            }
            else
            {
                logoMeta.Value = ApplicationLogoPath;
                db.Entry(logoMeta).State = EntityState.Modified;
            }

            // Save dark logo
            var logoDarkMeta = await db.AppMeta.FirstOrDefaultAsync(m => m.Key == "ApplicationLogoDarkPath");
            if (logoDarkMeta == null)
            {
                db.AppMeta.Add(new AppMeta { Key = "ApplicationLogoDarkPath", Value = ApplicationLogoDarkPath });
            }
            else
            {
                logoDarkMeta.Value = ApplicationLogoDarkPath;
                db.Entry(logoDarkMeta).State = EntityState.Modified;
            }

            await db.SaveChangesAsync();

            // Notify main windows to reload branding
            if (System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w is Views.MainWindow) is System.Windows.Window mw && mw.DataContext is MainWindowViewModel mwVm)
            {
                mwVm.RefreshBranding();
            }
            if (System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w => w is Views.OwnerMainWindow) is System.Windows.Window omw && omw.DataContext is OwnerMainWindowViewModel omwVm)
            {
                omwVm.RefreshBranding();
            }

            LogoStatusMessage = "✅ Logo paths saved!";
        }
        catch (Exception ex)
        {
            LogoStatusMessage = $"❌ Save error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseLogo()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Application Light Logo",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.ico|All Files|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            ApplicationLogoPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseDarkLogo()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Application Dark Logo",
            Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.ico|All Files|*.*"
        };
        if (dialog.ShowDialog() == true)
        {
            ApplicationLogoDarkPath = dialog.FileName;
        }
    }


    // Diagnostics actions
    [RelayCommand]
    private async Task LoadDiagnosticsAsync()
    {
        DiagnosticsMessage = "⏳ Querying database table records...";
        try
        {
            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
            TableCounts.Clear();

            TableCounts.Add(new TableCountDto { TableName = "Users", Count = await context.Users.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "Shifts", Count = await context.Shifts.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "DsmEntries", Count = await context.DsmEntries.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "NozzleReadings", Count = await context.NozzleReadings.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "PaymentCollections", Count = await context.PaymentCollections.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "DebitEntries", Count = await context.DebitEntries.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "TestingEntries", Count = await context.TestingEntries.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "Expenses", Count = await context.Expenses.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "CashDenominations", Count = await context.CashDenominations.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "Settings", Count = await context.Settings.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "ShiftOtherCash", Count = await context.ShiftOtherCash.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "ShiftFuelRates", Count = await context.ShiftFuelRates.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "DsmProfiles", Count = await context.DsmProfiles.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "Creditors", Count = await context.Creditors.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "CreditorRepayments", Count = await context.CreditorRepayments.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "AgsShiftImports", Count = await context.AgsShiftImports.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "AgsNozzleReadings", Count = await context.AgsNozzleReadings.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "AgsTankStocks", Count = await context.AgsTankStocks.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "AgsDailySummaries", Count = await context.AgsDailySummaries.CountAsync() });
            TableCounts.Add(new TableCountDto { TableName = "SyncChangeLogs (Pending)", Count = await context.SyncChangeLogs.CountAsync(l => !l.IsSynced) });
            TableCounts.Add(new TableCountDto { TableName = "SyncChangeLogs (Total)", Count = await context.SyncChangeLogs.CountAsync() });

            DiagnosticsMessage = "✅ Database counts updated successfully.";
            LoadSyncLogs();
        }
        catch (Exception ex)
        {
            DiagnosticsMessage = $"❌ Diagnostics query failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void LoadSyncLogs()
    {
        try
        {
            var logFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro", "logs");
            if (Directory.Exists(logFolder))
            {
                var files = Directory.GetFiles(logFolder, "app-*.log")
                    .OrderByDescending(f => f)
                    .ToList();
                if (files.Count > 0)
                {
                    var latestFile = files.First();
                    using (var fs = new FileStream(latestFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(fs))
                    {
                        var allLines = new List<string>();
                        while (!reader.EndOfStream)
                        {
                            var line = reader.ReadLine();
                            if (line != null) allLines.Add(line);
                        }
                        var lines = allLines.TakeLast(200);
                        SyncLogsText = string.Join(Environment.NewLine, lines);
                    }
                }
                else
                {
                    SyncLogsText = "No log files found in logs directory.";
                }
            }
            else
            {
                SyncLogsText = $"Log directory does not exist at: {logFolder}";
            }
        }
        catch (Exception ex)
        {
            SyncLogsText = $"Failed to read logs: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ForceSyncNowAsync()
    {
        DiagnosticsMessage = "⏳ Syncing...";
        try
        {
            await _syncEngine.ForceSyncAsync();
            DiagnosticsMessage = "✅ Sync cycle triggered. Check status below.";
            await LoadDiagnosticsAsync();
        }
        catch (Exception ex)
        {
            DiagnosticsMessage = $"❌ Sync trigger error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ForceFullResyncAsync()
    {
        var result = System.Windows.MessageBox.Show(
            "Warning: This will force a full resynchronization of ALL local database records to Supabase. This may take several minutes depending on data size. Do you wish to continue?",
            "Confirm Full Resync",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        DiagnosticsMessage = "⏳ Generating resync logs for all database tables. Please wait...";
        try
        {
            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
            
            // Delete all existing change logs first to avoid duplicates
            await context.Database.ExecuteSqlRawAsync("DELETE FROM SyncChangeLogs");

            FuelProDbContext.BypassTracking = true;
            try
            {
                foreach (var entityType in context.Model.GetEntityTypes())
                {
                    var tableName = entityType.GetTableName();
                    if (tableName == null || tableName == "SyncChangeLogs" || tableName == "AppMeta") continue;

                    var primaryKey = entityType.FindPrimaryKey()?.Properties.FirstOrDefault();
                    if (primaryKey == null) continue;

                    var clrType = entityType.ClrType;
                    var dbSetMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(clrType);
                    var dbSet = (IQueryable)dbSetMethod.Invoke(context, null)!;

                    foreach (var rec in dbSet.Cast<object>())
                    {
                        var propInfo = entityType.FindProperty(primaryKey.Name)?.PropertyInfo;
                        if (propInfo == null) continue;
                        
                        var id = Convert.ToInt32(propInfo.GetValue(rec));
                        if (id <= 0) continue;

                        context.SyncChangeLogs.Add(new SyncChangeLog
                        {
                            TableName = tableName,
                            RecordId = id,
                            Operation = "INSERT",
                            CreatedAt = DateTime.Now,
                            IsSynced = false
                        });
                    }
                }
                await context.SaveChangesAsync();
            }
            finally
            {
                FuelProDbContext.BypassTracking = false;
            }

            DiagnosticsMessage = "✅ Resync logs generated! Starting background upload...";
            await _syncEngine.ForceSyncAsync();
            await LoadDiagnosticsAsync();
        }
        catch (Exception ex)
        {
            DiagnosticsMessage = $"❌ Full resync failure: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RetryFailedSyncAsync()
    {
        // Simple retry trigger
        await ForceSyncNowAsync();
    }

    // License Management actions
    private void LoadLicenseInfo()
    {
        LicenseDeviceId = DeviceIdentifier.GetDeviceId();
        var validation = _licenseManager.ValidateLicense();
        if (validation.IsValid && validation.License != null)
        {
            var lic = validation.License;
            LicenseProductName = lic.ProductName switch
            {
                "FPL" => "FuelPro Lite",
                "VKD" => "PyroSync",
                "ZPA" => "ZP Automation",
                "PSC" => "PyroSync",
                _ => lic.ProductName
            };
            LicenseCustomerName = lic.CustomerName;
            LicenseBusinessName = lic.BusinessName;
            LicenseMobileNumber = lic.MobileNumber;
            LicenseType = lic.LicenseType;
            LicenseActivationDate = lic.ActivationDate.ToString("dd MMM yyyy");
            LicenseExpiryDate = lic.ExpiryDate.HasValue ? lic.ExpiryDate.Value.ToString("dd MMM yyyy") : "N/A (Lifetime)";
            
            if (lic.LicenseType == "Trial" || lic.LicenseType == "Annual" || lic.LicenseType == "Demo")
            {
                var daysLeft = (lic.ExpiryDate!.Value - DateTime.Now).Days;
                LicenseStatus = $"{lic.LicenseType} Active ({daysLeft} days left)";
            }
            else
            {
                LicenseStatus = "Active (Lifetime)";
            }
        }
        else
        {
            LicenseStatus = $"Unlicensed/Invalid ({validation.ErrorMessage})";
        }
    }

    [RelayCommand]
    private void CopyLicenseInfo()
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
    private void Exit()
    {
        _authService.Logout();
        
        // Open login view
        var loginView = new Views.LoginView();
        loginView.Show();
        
        // Close current developer window
        foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
        {
            if (window is Views.DeveloperMainWindow)
            {
                window.Close();
                break;
            }
        }
    }

    // ══════════════════════════════════════════════════════════
    // Tab 5 — Data Integrity commands
    // ══════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task RunIntegrityScanAsync()
    {
        IsRunningIntegrityScan = true;
        IntegrityScanMessage = "⏳ Scanning database for integrity issues...";
        DuplicateDsmGroups.Clear();
        DuplicateDebitGroups.Clear();
        DuplicateRepaymentGroups.Clear();
        OrphanRecords.Clear();

        try
        {
            await LoadRecentDsmEntriesAsync();
            var report = await _inspectionService.RunFullScanAsync();

            IntegrityDsmDuplicates = report.DuplicateDsmEntries;
            IntegrityDebitDuplicates = report.DuplicateDebitEntries;
            IntegrityRepaymentDuplicates = report.DuplicateRepayments;
            IntegrityOrphans = report.OrphanRecords;
            IntegrityBrokenFks = report.BrokenForeignKeys;
            IntegrityHasIssues = report.HasIssues;
            IntegrityScanTimestamp = $"Last scanned: {report.ScannedAt:dd MMM yyyy  hh:mm tt}";

            foreach (var g in report.DsmEntryGroups)
            {
                var vm = DsmEntryDuplicateGroupVm.FromModel(g);
                vm.SelectionChangedAction = UpdateDsmSelectionState;
                DuplicateDsmGroups.Add(vm);
            }
            UpdateDsmSelectionState();

            foreach (var g in report.DebitEntryGroups)
            {
                var vm = DebitEntryDuplicateGroupVm.FromModel(g);
                vm.SelectionChangedAction = UpdateDebitSelectionState;
                DuplicateDebitGroups.Add(vm);
            }
            UpdateDebitSelectionState();

            foreach (var g in report.RepaymentGroups)
            {
                var vm = RepaymentDuplicateGroupVm.FromModel(g);
                vm.SelectionChangedAction = UpdateRepaymentSelectionState;
                DuplicateRepaymentGroups.Add(vm);
            }
            UpdateRepaymentSelectionState();

            foreach (var o in report.Orphans)
                OrphanRecords.Add(o);

            IntegrityScanMessage = report.HasIssues
                ? $"⚠️  Issues found — {report.DuplicateDsmEntries} duplicate DSM entries, " +
                  $"{report.DuplicateDebitEntries} duplicate debits, {report.OrphanRecords} orphans."
                : "✅ No integrity issues found. Database is clean.";
        }
        catch (Exception ex)
        {
            Log.ForContext<DeveloperMainWindowViewModel>().Error(ex, "Integrity scan failed");
            IntegrityScanMessage = $"❌ Scan failed: {ex.Message}";
        }
        finally
        {
            IsRunningIntegrityScan = false;
        }
    }

    [RelayCommand]
    public async Task LoadRecentDsmEntriesAsync()
    {
        IsLoadingRecentDsmEntries = true;
        try
        {
            var items = await _resolutionService.GetRecentDsmEntriesAsync(100);
            RecentDsmEntries.Clear();
            foreach (var item in items)
            {
                RecentDsmEntries.Add(item);
            }
            FilterRecentDsmEntries();
        }
        catch (Exception ex)
        {
            Log.ForContext<DeveloperMainWindowViewModel>().Error(ex, "Failed to load recent DSM entries");
        }
        finally
        {
            IsLoadingRecentDsmEntries = false;
        }
    }

    [RelayCommand]
    private async Task FixCorruptedDsmEntryAsync(RecentDsmEntryInspectorDto entry)
    {
        if (entry == null) return;

        var result = await _resolutionService.FixCorruptedDsmEntryAsync(entry.EntryIds);
        if (result.Success)
        {
            MessageBox.Show(result.Data, "Entry Repaired", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadRecentDsmEntriesAsync();
        }
        else
        {
            MessageBox.Show(result.Error, "Repair Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteAnyDsmEntryAsync(RecentDsmEntryInspectorDto entry)
    {
        if (entry == null) return;

        var confirm = MessageBox.Show(
            $"Are you sure you want to PERMANENTLY DELETE DsmEntry record(s) #{entry.IdDisplay}?\n\n" +
            $"DSM Name: {entry.DsmName}\n" +
            $"Shift Date: {entry.ShiftDate:dd MMM yyyy} (Shift {entry.ShiftType})\n" +
            $"Pump(s): {entry.PumpLabel}\n" +
            $"Origin: {entry.Origin}\n" +
            $"Gross Sales: ₹{entry.GrossSales:N2}\n\n" +
            "⚠️ WARNING: This will delete all connected entries and ALL associated nozzle readings, payments, debit entries, and expenses. This action CANNOT be undone.",
            "Confirm Delete DSM Entry",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var result = await _resolutionService.DeleteAnyDsmEntryAsync(entry.EntryIds);
        if (result.Success)
        {
            MessageBox.Show(result.Data, "Entry Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadRecentDsmEntriesAsync();
            await RunIntegrityScanAsync();
        }
        else
        {
            MessageBox.Show(result.Error, "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteAllRecentDsmEntriesAsync()
    {
        if (RecentDsmEntries.Count == 0)
        {
            MessageBox.Show("No DSM entries found to delete.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            $"Are you sure you want to PURGE ALL {RecentDsmEntries.Count} DSM entry records and all associated readings, collections, debits, and expenses from the database?\n\n" +
            "⚠️ WARNING: This will permanently wipe all current DSM entries in the local database. Use this after testing or when changing station setups.",
            "Confirm Purge All DSM Entries",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var result = await _resolutionService.DeleteAllDsmEntriesAsync();
        if (result.Success)
        {
            MessageBox.Show(result.Data, "All Entries Purged", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadRecentDsmEntriesAsync();
            await RunIntegrityScanAsync();
        }
        else
        {
            MessageBox.Show(result.Error, "Purge Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteDuplicateDsmEntryAsync(DsmEntryDuplicateGroupVm group)
    {
        if (group == null) return;

        var confirm = MessageBox.Show(
            $"Delete duplicate DsmEntry {group.DuplicateEntryId}?\n\n" +
            $"DSM: {group.DsmName}  |  Pump: {group.PumpId}\n" +
            $"Surviving entry: {group.OriginalEntryId} (created {group.OriginalCreatedAt:dd MMM yyyy})\n\n" +
            "This will permanently delete the duplicate and all its child rows.\n" +
            "The surviving record will not be affected.",
            "Confirm Delete Duplicate",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var result = await _resolutionService.DeleteDuplicateDsmEntryAsync(
            group.DuplicateEntryId, group.OriginalEntryId);

        if (result.Success)
        {
            DuplicateDsmGroups.Remove(group);
            IntegrityDsmDuplicates = DuplicateDsmGroups.Count;
            IntegrityHasIssues = IntegrityDsmDuplicates > 0 || IntegrityDebitDuplicates > 0
                || IntegrityRepaymentDuplicates > 0 || IntegrityOrphans > 0;
            IntegrityScanMessage = $"✅ {result.Data}";
        }
        else
        {
            MessageBox.Show(result.Error, "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteDuplicateDebitGroupAsync(DebitEntryDuplicateGroupVm group)
    {
        if (group == null) return;

        var ids = group.DuplicateIds;
        var confirm = MessageBox.Show(
            $"Delete {ids.Count} duplicate DebitEntry row(s)?\n" +
            $"IDs: [{string.Join(", ", ids)}]\n\n" +
            $"The original entry (ID {group.OriginalEntryId}) will be kept.",
            "Confirm Delete Duplicates",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var result = await _resolutionService.DeleteDuplicateDebitEntriesAsync(ids);

        if (result.Success)
        {
            DuplicateDebitGroups.Remove(group);
            IntegrityDebitDuplicates = DuplicateDebitGroups.Sum(g => g.DuplicateCount);
            IntegrityScanMessage = $"✅ {result.Data}";
        }
        else
        {
            MessageBox.Show(result.Error, "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteDuplicateRepaymentGroupAsync(RepaymentDuplicateGroupVm group)
    {
        if (group == null) return;

        var ids = group.DuplicateIds;
        var confirm = MessageBox.Show(
            $"Delete {ids.Count} duplicate CreditorRepayment row(s)?\n" +
            $"IDs: [{string.Join(", ", ids)}]\n\n" +
            $"The original repayment (ID {group.OriginalRepaymentId}) will be kept.",
            "Confirm Delete Duplicates",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var result = await _resolutionService.DeleteDuplicateRepaymentsAsync(ids);

        if (result.Success)
        {
            DuplicateRepaymentGroups.Remove(group);
            IntegrityRepaymentDuplicates = DuplicateRepaymentGroups.Sum(g => g.DuplicateCount);
            UpdateRepaymentSelectionState();
            IntegrityScanMessage = $"✅ {result.Data}";
        }
        else
        {
            MessageBox.Show(result.Error, "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedDsmEntriesAsync()
    {
        var selected = DuplicateDsmGroups.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirm = MessageBox.Show(
            $"Delete {selected.Count} selected duplicate DsmEntry record(s)?\n\n" +
            "This will permanently delete all selected duplicates and their child rows.\n" +
            "The original surviving records will not be affected.",
            "Confirm Delete Selected Duplicates",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var pairs = selected.Select(g => (g.DuplicateEntryId, g.OriginalEntryId));
        var result = await _resolutionService.DeleteMultipleDuplicateDsmEntriesAsync(pairs);

        if (result.Success)
        {
            foreach (var item in selected)
                DuplicateDsmGroups.Remove(item);

            IntegrityDsmDuplicates = DuplicateDsmGroups.Count;
            UpdateDsmSelectionState();
            IntegrityHasIssues = IntegrityDsmDuplicates > 0 || IntegrityDebitDuplicates > 0
                || IntegrityRepaymentDuplicates > 0 || IntegrityOrphans > 0;
            IntegrityScanMessage = $"✅ {result.Data}";
        }
        else
        {
            MessageBox.Show(result.Error, "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteAllDsmEntriesAsync()
    {
        if (DuplicateDsmGroups.Count == 0) return;

        var confirm = MessageBox.Show(
            $"Delete ALL {DuplicateDsmGroups.Count} duplicate DsmEntry record(s)?\n\n" +
            "This will permanently delete ALL duplicate DsmEntries and their child rows.\n" +
            "The original surviving records will not be affected.",
            "Confirm Delete ALL Duplicate DsmEntries",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var pairs = DuplicateDsmGroups.Select(g => (g.DuplicateEntryId, g.OriginalEntryId)).ToList();
        var result = await _resolutionService.DeleteMultipleDuplicateDsmEntriesAsync(pairs);

        if (result.Success)
        {
            DuplicateDsmGroups.Clear();
            IntegrityDsmDuplicates = 0;
            UpdateDsmSelectionState();
            IntegrityHasIssues = IntegrityDsmDuplicates > 0 || IntegrityDebitDuplicates > 0
                || IntegrityRepaymentDuplicates > 0 || IntegrityOrphans > 0;
            IntegrityScanMessage = $"✅ {result.Data}";
        }
        else
        {
            MessageBox.Show(result.Error, "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedDebitGroupsAsync()
    {
        var selected = DuplicateDebitGroups.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0) return;

        var allSelectedIds = selected.SelectMany(g => g.DuplicateIds).ToList();
        var confirm = MessageBox.Show(
            $"Delete {allSelectedIds.Count} duplicate DebitEntry row(s) across {selected.Count} selected group(s)?\n\n" +
            "The original debit entry records will be preserved.",
            "Confirm Delete Selected Duplicate Debits",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var result = await _resolutionService.DeleteDuplicateDebitEntriesAsync(allSelectedIds);

        if (result.Success)
        {
            foreach (var item in selected)
                DuplicateDebitGroups.Remove(item);

            IntegrityDebitDuplicates = DuplicateDebitGroups.Sum(g => g.DuplicateCount);
            UpdateDebitSelectionState();
            IntegrityHasIssues = IntegrityDsmDuplicates > 0 || IntegrityDebitDuplicates > 0
                || IntegrityRepaymentDuplicates > 0 || IntegrityOrphans > 0;
            IntegrityScanMessage = $"✅ {result.Data}";
        }
        else
        {
            MessageBox.Show(result.Error, "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteAllDebitGroupsAsync()
    {
        if (DuplicateDebitGroups.Count == 0) return;

        var allIds = DuplicateDebitGroups.SelectMany(g => g.DuplicateIds).ToList();
        var confirm = MessageBox.Show(
            $"Delete ALL {allIds.Count} duplicate DebitEntry row(s) across all {DuplicateDebitGroups.Count} group(s)?\n\n" +
            "The original debit entry records will be preserved.",
            "Confirm Delete ALL Duplicate Debits",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var result = await _resolutionService.DeleteDuplicateDebitEntriesAsync(allIds);

        if (result.Success)
        {
            DuplicateDebitGroups.Clear();
            IntegrityDebitDuplicates = 0;
            UpdateDebitSelectionState();
            IntegrityHasIssues = IntegrityDsmDuplicates > 0 || IntegrityDebitDuplicates > 0
                || IntegrityRepaymentDuplicates > 0 || IntegrityOrphans > 0;
            IntegrityScanMessage = $"✅ {result.Data}";
        }
        else
        {
            MessageBox.Show(result.Error, "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedRepaymentGroupsAsync()
    {
        var selected = DuplicateRepaymentGroups.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0) return;

        var allSelectedIds = selected.SelectMany(g => g.DuplicateIds).ToList();
        var confirm = MessageBox.Show(
            $"Delete {allSelectedIds.Count} duplicate CreditorRepayment row(s) across {selected.Count} selected group(s)?\n\n" +
            "The original repayment records will be preserved.",
            "Confirm Delete Selected Duplicate Repayments",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var result = await _resolutionService.DeleteDuplicateRepaymentsAsync(allSelectedIds);

        if (result.Success)
        {
            foreach (var item in selected)
                DuplicateRepaymentGroups.Remove(item);

            IntegrityRepaymentDuplicates = DuplicateRepaymentGroups.Sum(g => g.DuplicateCount);
            UpdateRepaymentSelectionState();
            IntegrityHasIssues = IntegrityDsmDuplicates > 0 || IntegrityDebitDuplicates > 0
                || IntegrityRepaymentDuplicates > 0 || IntegrityOrphans > 0;
            IntegrityScanMessage = $"✅ {result.Data}";
        }
        else
        {
            MessageBox.Show(result.Error, "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteAllRepaymentGroupsAsync()
    {
        if (DuplicateRepaymentGroups.Count == 0) return;

        var allIds = DuplicateRepaymentGroups.SelectMany(g => g.DuplicateIds).ToList();
        var confirm = MessageBox.Show(
            $"Delete ALL {allIds.Count} duplicate CreditorRepayment row(s) across all {DuplicateRepaymentGroups.Count} group(s)?\n\n" +
            "The original repayment records will be preserved.",
            "Confirm Delete ALL Duplicate Repayments",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var result = await _resolutionService.DeleteDuplicateRepaymentsAsync(allIds);

        if (result.Success)
        {
            DuplicateRepaymentGroups.Clear();
            IntegrityRepaymentDuplicates = 0;
            UpdateRepaymentSelectionState();
            IntegrityHasIssues = IntegrityDsmDuplicates > 0 || IntegrityDebitDuplicates > 0
                || IntegrityRepaymentDuplicates > 0 || IntegrityOrphans > 0;
            IntegrityScanMessage = $"✅ {result.Data}";
        }
        else
        {
            MessageBox.Show(result.Error, "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteAllOrphansAsync()
    {
        if (OrphanRecords.Count == 0) return;

        var confirm = MessageBox.Show(
            $"Delete all {OrphanRecords.Count} orphan child records?\n\n" +
            "These are rows whose parent DsmEntry no longer exists.\n" +
            "They are not visible in any report and are safe to remove.",
            "Confirm Delete Orphans",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        var result = await _resolutionService.DeleteOrphanRecordsAsync(OrphanRecords.ToList());

        if (result.Success)
        {
            OrphanRecords.Clear();
            IntegrityOrphans = 0;
            IntegrityScanMessage = $"✅ {result.Data}";
        }
        else
        {
            MessageBox.Show(result.Error, "Delete Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

// ─── View-Model wrappers for the Data Integrity DataGrids ─────────────────────

public partial class DsmEntryDuplicateGroupVm : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    public Action? SelectionChangedAction { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChangedAction?.Invoke();
    }

    public int OriginalEntryId { get; set; }
    public int DuplicateEntryId { get; set; }
    public string DsmName { get; set; } = string.Empty;
    public int PumpId { get; set; }
    public int ShiftId { get; set; }
    public DateTime OriginalCreatedAt { get; set; }
    public DateTime DuplicateCreatedAt { get; set; }
    public int DuplicateNozzleCount { get; set; }
    public int DuplicateDebitCount { get; set; }
    public bool DuplicateHasPayment { get; set; }
    public string BusinessKey => $"Shift {ShiftId} | DSM: {DsmName} | Pump: {PumpId}";

    public static DsmEntryDuplicateGroupVm FromModel(DsmEntryDuplicateGroup g)
    {
        var dup = g.DuplicateRecords.First();
        return new DsmEntryDuplicateGroupVm
        {
            OriginalEntryId = g.OriginalRecord.DsmEntryId,
            DuplicateEntryId = dup.DsmEntryId,
            DsmName = g.OriginalRecord.DsmName ?? string.Empty,
            PumpId = g.OriginalRecord.PumpId,
            ShiftId = g.OriginalRecord.ShiftId,
            OriginalCreatedAt = g.OriginalRecord.CreatedAt,
            DuplicateCreatedAt = dup.CreatedAt,
            DuplicateNozzleCount = dup.NozzleReadings?.Count ?? 0,
            DuplicateDebitCount = dup.DebitEntries?.Count ?? 0,
            DuplicateHasPayment = dup.PaymentCollection != null
        };
    }
}

public partial class DebitEntryDuplicateGroupVm : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    public Action? SelectionChangedAction { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChangedAction?.Invoke();
    }

    public int OriginalEntryId { get; set; }
    public List<int> DuplicateIds { get; set; } = new();
    public int DuplicateCount => DuplicateIds.Count;
    public string DebtorName { get; set; } = string.Empty;
    public double Amount { get; set; }
    public int DsmEntryId { get; set; }

    public static DebitEntryDuplicateGroupVm FromModel(DebitEntryDuplicateGroup g)
    {
        return new DebitEntryDuplicateGroupVm
        {
            OriginalEntryId = g.OriginalRecord.DebitId,
            DuplicateIds = g.DuplicateRecords.Select(d => d.DebitId).ToList(),
            DebtorName = g.OriginalRecord.DebtorName ?? string.Empty,
            Amount = g.OriginalRecord.Amount,
            DsmEntryId = g.OriginalRecord.DsmEntryId
        };
    }
}

public partial class RepaymentDuplicateGroupVm : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    public Action? SelectionChangedAction { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChangedAction?.Invoke();
    }

    public int OriginalRepaymentId { get; set; }
    public List<int> DuplicateIds { get; set; } = new();
    public int DuplicateCount => DuplicateIds.Count;
    public string CreditorName { get; set; } = string.Empty;
    public double Amount { get; set; }
    public DateTime RepaymentDate { get; set; }
    public string PaymentMode { get; set; } = string.Empty;

    public static RepaymentDuplicateGroupVm FromModel(RepaymentDuplicateGroup g)
    {
        return new RepaymentDuplicateGroupVm
        {
            OriginalRepaymentId = g.OriginalRecord.CreditorRepaymentId,
            DuplicateIds = g.DuplicateRecords.Select(r => r.CreditorRepaymentId).ToList(),
            CreditorName = g.OriginalRecord.CreditorName ?? string.Empty,
            Amount = g.OriginalRecord.Amount,
            RepaymentDate = g.OriginalRecord.RepaymentDate,
            PaymentMode = g.OriginalRecord.PaymentMode ?? string.Empty
        };
    }
}

public class TableCountDto
{
    public string TableName { get; set; } = "";
    public int Count { get; set; }
}

public partial class NozzleItemVm : ObservableObject
{
    public int PumpMappingId { get; set; }
    [ObservableProperty] private int _nozzleNumber;

    private string _fuelType = "MS-I";
    public string FuelType
    {
        get => _fuelType;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (SetProperty(ref _fuelType, value))
            {
                OnFuelTypeChangedCallback?.Invoke(this, value);
            }
        }
    }

    private string _tankName = "";
    public string TankName
    {
        get => _tankName;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (SetProperty(ref _tankName, value))
            {
                OnTankNameChangedCallback?.Invoke(this, value);
            }
        }
    }

    public Action<NozzleItemVm, string>? OnTankNameChangedCallback { get; set; }
    public Action<NozzleItemVm, string>? OnFuelTypeChangedCallback { get; set; }
}

public partial class PumpCardVm : ObservableObject
{
    [ObservableProperty] private int _pumpId;
    public ObservableCollection<NozzleItemVm> Nozzles { get; } = new();
}

public partial class PumpCandidateVm : ObservableObject
{
    [ObservableProperty] private int _pumpId;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private string _disabledReason = string.Empty;

    public Action? OnSelectionChangedCallback { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        OnSelectionChangedCallback?.Invoke();
    }
}

public partial class PumpConnectionGroupVm : ObservableObject
{
    [ObservableProperty] private int _groupId = 1;
    [ObservableProperty] private string _groupName = "Group 1";
    [ObservableProperty] private int _primaryPumpId = 1;
    [ObservableProperty] private string _validationMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    public ObservableCollection<int> AvailablePrimaryPumps { get; } = new();
    public ObservableCollection<PumpCandidateVm> CandidateSlaves { get; } = new();

    public Action? OnGroupChangedCallback { get; set; }

    partial void OnPrimaryPumpIdChanged(int value)
    {
        OnGroupChangedCallback?.Invoke();
    }

    public List<int> GetSelectedConnectedPumpIds()
    {
        return CandidateSlaves
            .Where(c => c.IsSelected && c.PumpId != PrimaryPumpId)
            .Select(c => c.PumpId)
            .ToList();
    }
}

public partial class DeveloperMainWindowViewModel
{
    // ───────────────────────────────────────────────
    // TAB 5: CLIENT FEATURES & PAGE ACCESS
    // ───────────────────────────────────────────────

    partial void OnSelectedFeatureRoleFilterChanged(string value)
    {
        FilterFeatures();
    }

    [RelayCommand]
    public async Task LoadFeaturesAsync()
    {
        FeatureStatusMessage = "⏳ Loading feature settings...";
        try
        {
            var features = await _featureToggleService.GetAllFeaturesAsync();
            void UpdateUi()
            {
                FeatureSettings.Clear();
                foreach (var f in features)
                {
                    FeatureSettings.Add(f);
                }
                FilterFeatures();
                if (features.Count > 0)
                {
                    FeatureStatusMessage = $"✅ Loaded {features.Count} feature settings.";
                }
                else
                {
                    FeatureStatusMessage = "⚠️ No feature settings found in database.";
                }
            }

            if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                System.Windows.Application.Current.Dispatcher.Invoke(UpdateUi);
            }
            else
            {
                UpdateUi();
            }
        }
        catch (Exception ex)
        {
            FeatureStatusMessage = $"❌ Failed to load features: {ex.Message}";
        }
    }

    private void FilterFeatures()
    {
        void UpdateFilter()
        {
            FilteredFeatureSettings.Clear();
            var filter = SelectedFeatureRoleFilter;
            var query = FeatureSettings.AsEnumerable();

            if (!string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(f => string.Equals(f.TargetRole, filter, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var item in query)
            {
                FilteredFeatureSettings.Add(item);
            }
        }

        if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            System.Windows.Application.Current.Dispatcher.Invoke(UpdateFilter);
        }
        else
        {
            UpdateFilter();
        }
    }

    [RelayCommand]
    public async Task SaveFeaturesAsync()
    {
        FeatureStatusMessage = "⏳ Saving feature settings...";
        try
        {
            // Core Dependency Guardrail: Ensure essential operations features remain enabled to prevent logical breakage
            var dsmEntryFeature = FeatureSettings.FirstOrDefault(f => f.FeatureKey == "Admin_DsmEntry");
            if (dsmEntryFeature != null && !dsmEntryFeature.IsEnabled)
            {
                dsmEntryFeature.IsEnabled = true;
                MessageBox.Show("DSM Entry is a core operational module and must remain enabled to capture pump sales.", "Core Protection", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            var finalCalcFeature = FeatureSettings.FirstOrDefault(f => f.FeatureKey == "Admin_FinalCalculation");
            if (finalCalcFeature != null && !finalCalcFeature.IsEnabled)
            {
                finalCalcFeature.IsEnabled = true;
                MessageBox.Show("Final Calculation is a core module required to reconcile shifts and calculate day sales.", "Core Protection", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            var success = await _featureToggleService.SaveFeaturesAsync(FeatureSettings);
            if (success)
            {
                FeatureStatusMessage = "✅ Feature matrix saved successfully!";
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _syncEngine.ForceSyncAsync();
                    }
                    catch { }
                });
                System.Windows.MessageBox.Show("Client feature configuration saved successfully!\nAdmin and Owner interfaces will now reflect these settings.", 
                    "Features Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                FeatureStatusMessage = "❌ Failed to save feature settings.";
            }
        }
        catch (Exception ex)
        {
            FeatureStatusMessage = $"❌ Save error: {ex.Message}";
        }
    }

    [RelayCommand]
    public void ApplyClientPreset(string presetName)
    {
        if (FeatureSettings.Count == 0)
        {
            FeatureStatusMessage = "⚠️ No feature settings loaded to apply preset.";
            return;
        }

        if (presetName == "CurrentClient")
        {
            // Current client requirements:
            // Daily sales, fuel/oil sales, debtor management, DSM salary, collections.
            // Disable AGS Import, Profit & Loss, Mismatch Ledger if not needed.
            foreach (var f in FeatureSettings)
            {
                if (f.FeatureKey == "Admin_AgsImport" || f.FeatureKey == "Owner_ProfitLoss" || f.FeatureKey == "Owner_MismatchLedger")
                {
                    f.IsEnabled = false;
                }
                else if (f.FeatureKey == "Collection_UseMorningNight")
                {
                    f.IsEnabled = false; // No morning/night split
                }
                else
                {
                    f.IsEnabled = true;
                }
            }
            FeatureStatusMessage = "Applied 'Current Client' configuration preset. Click 'Save All Features' to persist.";
        }
        else if (presetName == "Full")
        {
            foreach (var f in FeatureSettings)
            {
                f.IsEnabled = true;
            }
            FeatureStatusMessage = "Applied 'All Features Enabled' preset. Click 'Save All Features' to persist.";
        }
        FilterFeatures();
    }

    // ───────────────────────────────────────────────
    // TAB 6: STATION LAYOUT & COLLECTIONS MASTER
    // ───────────────────────────────────────────────

    [RelayCommand]
    public async Task LoadStationAndCollectionsAsync()
    {
        StationStatusMessage = "⏳ Loading station layout...";
        CollectionStatusMessage = "⏳ Loading collection types...";
        try
        {
            // Load Tanks
            var tanks = await _stationConfigService.GetAllTanksAsync();
            ConfiguredTanks.Clear();
            foreach (var t in tanks)
            {
                ConfiguredTanks.Add(t);
            }
            RefreshAvailableTankNames();

            // Load Products
            var products = await _stationConfigService.GetAllProductsAsync();
            ConfiguredProducts.Clear();
            foreach (var p in products)
            {
                ConfiguredProducts.Add(p);
            }

            // Load Pump Mappings
            var mappings = await _stationConfigService.GetAllPumpMappingsAsync();
            ConfiguredPumps.Clear();

            var grouped = mappings
                .GroupBy(m => m.PumpId)
                .OrderBy(g => g.Key);

            foreach (var g in grouped)
            {
                var pumpVm = new PumpCardVm { PumpId = g.Key };
                foreach (var nozzle in g.OrderBy(n => n.NozzleNumber))
                {
                    var nozzleVm = new NozzleItemVm
                    {
                        PumpMappingId = nozzle.PumpMappingId,
                        NozzleNumber = nozzle.NozzleNumber,
                        FuelType = nozzle.FuelType,
                        TankName = string.IsNullOrWhiteSpace(nozzle.TankName) ? (AvailableTankNames.FirstOrDefault() ?? "Tank 1") : nozzle.TankName,
                        OnTankNameChangedCallback = HandleNozzleTankChanged,
                        OnFuelTypeChangedCallback = HandleNozzleFuelTypeChanged
                    };
                    pumpVm.Nozzles.Add(nozzleVm);
                }
                ConfiguredPumps.Add(pumpVm);
            }

            // Load Collection Types
            var collectionTypes = await _collectionTypeService.GetAllCollectionTypesAsync();
            ConfiguredCollectionTypes.Clear();
            foreach (var c in collectionTypes)
            {
                ConfiguredCollectionTypes.Add(c);
            }

            // Load Saved Presets
            _isLoadingPresetInternal = true;
            try
            {
                await LoadAllPresetsAsync();
            }
            finally
            {
                _isLoadingPresetInternal = false;
            }

            // Load Shift Cycle Setting
            bool useMorningNight = _featureToggleService.IsFeatureEnabled("Collection_UseMorningNight", false);
            SelectedShiftCycleOption = useMorningNight 
                ? "3_SLOT_SPLIT (Overnight Midnight Split: Morning, Day, Night)" 
                : "2_SHIFT (Standard Date-to-Date: Shift A & Shift B)";

            RefreshAvailableFuelTypes();

            _isLoadingPresetInternal = true;
            SelectedPreset = null;
            _isLoadingPresetInternal = false;

            StationStatusMessage = $"✅ Loaded {ConfiguredPumps.Count} pumps, {ConfiguredTanks.Count} tanks.";
            CollectionStatusMessage = $"✅ Loaded {ConfiguredCollectionTypes.Count} collection types.";
            ShiftCycleStatusMessage = $"Current cycle: {(useMorningNight ? "3-Slot Split" : "Standard 2-Shift")}";

            // Load Dynamic Pump Connection Rules
            await LoadPumpConnectionConfigurationAsync();
        }
        catch (Exception ex)
        {
            StationStatusMessage = $"❌ Load error: {ex.Message}";
            CollectionStatusMessage = $"❌ Load error: {ex.Message}";
        }
    }

    public void RefreshAvailableFuelTypes()
    {
        // STRICTLY from Configured Storage Tanks only (excluding all lubricants, DEF, oils, etc.)
        var targetTypes = ConfiguredTanks
            .Select(t => t.FuelType?.Trim())
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f)
            .ToList();

        if (targetTypes.Count == 0)
        {
            targetTypes = new List<string> { "MS-I", "HSD", "MS-II", "CNG" };
        }

        // In-place synchronization to prevent WPF ComboBoxes from clearing selections
        for (int i = AvailableFuelTypes.Count - 1; i >= 0; i--)
        {
            if (!targetTypes.Contains(AvailableFuelTypes[i], StringComparer.OrdinalIgnoreCase))
            {
                AvailableFuelTypes.RemoveAt(i);
            }
        }

        foreach (var t in targetTypes)
        {
            if (!AvailableFuelTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
            {
                AvailableFuelTypes.Add(t);
            }
        }
    }

    private void RefreshAvailableTankNames()
    {
        var targetTanks = ConfiguredTanks
            .Select(t => t.TankName?.Trim())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetTanks.Count == 0)
        {
            targetTanks = new List<string> { "MS - 20KL", "HSD - 20KL", "HSD - 20KL II", "CNG Line" };
        }

        for (int i = AvailableTankNames.Count - 1; i >= 0; i--)
        {
            if (!targetTanks.Contains(AvailableTankNames[i], StringComparer.OrdinalIgnoreCase))
            {
                AvailableTankNames.RemoveAt(i);
            }
        }

        foreach (var t in targetTanks)
        {
            if (!AvailableTankNames.Contains(t, StringComparer.OrdinalIgnoreCase))
            {
                AvailableTankNames.Add(t);
            }
        }

        RefreshAvailableFuelTypes();
    }

    private void HandleNozzleTankChanged(NozzleItemVm nozzle, string tankName)
    {
        if (nozzle == null || string.IsNullOrWhiteSpace(tankName)) return;
        var tankMatch = ConfiguredTanks.FirstOrDefault(t => string.Equals(t.TankName, tankName, StringComparison.OrdinalIgnoreCase));
        if (tankMatch != null && !string.IsNullOrWhiteSpace(tankMatch.FuelType))
        {
            if (!string.Equals(nozzle.FuelType, tankMatch.FuelType, StringComparison.OrdinalIgnoreCase))
            {
                nozzle.FuelType = tankMatch.FuelType;
            }
        }
    }

    private void HandleNozzleFuelTypeChanged(NozzleItemVm nozzle, string fuelType)
    {
        if (nozzle == null || string.IsNullOrWhiteSpace(fuelType)) return;
        var currentTank = ConfiguredTanks.FirstOrDefault(t => string.Equals(t.TankName, nozzle.TankName, StringComparison.OrdinalIgnoreCase));
        if (currentTank != null && string.Equals(currentTank.FuelType, fuelType, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var matchingTank = ConfiguredTanks.FirstOrDefault(t => string.Equals(t.FuelType, fuelType, StringComparison.OrdinalIgnoreCase));
        if (matchingTank != null)
        {
            nozzle.TankName = matchingTank.TankName;
        }
    }

    [RelayCommand]
    public void AddPump()
    {
        var nextPumpId = ConfiguredPumps.Select(p => p.PumpId).DefaultIfEmpty(0).Max() + 1;
        var newPump = new PumpCardVm { PumpId = nextPumpId };
        
        var nextNozzle = ConfiguredPumps.SelectMany(p => p.Nozzles).Select(n => n.NozzleNumber).DefaultIfEmpty(0).Max() + 1;
        var defaultTank = AvailableTankNames.FirstOrDefault() ?? (ConfiguredTanks.FirstOrDefault()?.TankName ?? "Tank 1");
        var defaultFuel = ConfiguredTanks.FirstOrDefault(t => t.TankName == defaultTank)?.FuelType ?? (AvailableFuelTypes.FirstOrDefault() ?? "HSD");

        newPump.Nozzles.Add(new NozzleItemVm
        {
            NozzleNumber = nextNozzle,
            FuelType = defaultFuel,
            TankName = defaultTank,
            OnTankNameChangedCallback = HandleNozzleTankChanged,
            OnFuelTypeChangedCallback = HandleNozzleFuelTypeChanged
        });

        ConfiguredPumps.Add(newPump);
        StationStatusMessage = $"Added Pump {nextPumpId}. Remember to click 'Save Station Layout'.";
    }

    [RelayCommand]
    public void RemovePump(PumpCardVm? pump)
    {
        if (pump == null) return;
        ConfiguredPumps.Remove(pump);
        StationStatusMessage = $"Removed Pump {pump.PumpId}. Remember to click 'Save Station Layout'.";
    }

    [RelayCommand]
    public void AddNozzle(PumpCardVm? pump)
    {
        if (pump == null) return;
        var nextNozzle = ConfiguredPumps.SelectMany(p => p.Nozzles).Select(n => n.NozzleNumber).DefaultIfEmpty(0).Max() + 1;
        var defaultTank = AvailableTankNames.FirstOrDefault() ?? (ConfiguredTanks.FirstOrDefault()?.TankName ?? "Tank 1");
        var defaultFuel = ConfiguredTanks.FirstOrDefault(t => t.TankName == defaultTank)?.FuelType ?? (AvailableFuelTypes.FirstOrDefault() ?? "MS-I");

        pump.Nozzles.Add(new NozzleItemVm
        {
            NozzleNumber = nextNozzle,
            FuelType = defaultFuel,
            TankName = defaultTank,
            OnTankNameChangedCallback = HandleNozzleTankChanged,
            OnFuelTypeChangedCallback = HandleNozzleFuelTypeChanged
        });
        StationStatusMessage = $"Added Nozzle {nextNozzle} to Pump {pump.PumpId}.";
    }

    [RelayCommand]
    public void RemoveNozzle(NozzleItemVm? nozzle)
    {
        if (nozzle == null) return;
        foreach (var p in ConfiguredPumps)
        {
            if (p.Nozzles.Contains(nozzle))
            {
                p.Nozzles.Remove(nozzle);
                StationStatusMessage = $"Removed Nozzle {nozzle.NozzleNumber} from Pump {p.PumpId}.";
                break;
            }
        }
    }

    [RelayCommand]
    public void SetNozzleTank((NozzleItemVm Nozzle, string TankName) param)
    {
        if (param.Nozzle != null && !string.IsNullOrWhiteSpace(param.TankName))
        {
            param.Nozzle.TankName = param.TankName;
            HandleNozzleTankChanged(param.Nozzle, param.TankName);
            StationStatusMessage = $"Attached Nozzle {param.Nozzle.NozzleNumber} to {param.TankName}.";
        }
    }

    [RelayCommand]
    public void AutoSequenceNozzles()
    {
        int num = 1;
        foreach (var pump in ConfiguredPumps.OrderBy(p => p.PumpId))
        {
            foreach (var nozzle in pump.Nozzles)
            {
                nozzle.NozzleNumber = num++;
            }
        }
        StationStatusMessage = $"✅ Auto-sequenced all nozzles from 1 to {num - 1}. Click 'Save Station Layout' to persist.";
    }

    [RelayCommand]
    public async Task SaveStationLayoutAsync()
    {
        StationStatusMessage = "⏳ Saving station layout...";
        try
        {
            _isSavingLayout = true;

            // Check for duplicate nozzle numbers
            var duplicateNumbers = ConfiguredPumps
                .SelectMany(p => p.Nozzles)
                .GroupBy(n => n.NozzleNumber)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateNumbers.Count > 0)
            {
                var dupStr = string.Join(", ", duplicateNumbers);
                var result = MessageBox.Show(
                    $"Duplicate Nozzle Number(s) detected: [{dupStr}].\n\nEvery nozzle must have a unique number across all pumps.\n\nWould you like to automatically re-sequence all nozzles (1, 2, 3...) and proceed with saving?",
                    "Duplicate Nozzle Numbers Detected",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    AutoSequenceNozzles();
                }
                else
                {
                    StationStatusMessage = $"❌ Save cancelled due to duplicate nozzle numbers: [{dupStr}].";
                    return;
                }
            }

            var flatMappings = new List<PumpMapping>();
            var allNozzleNumbers = new HashSet<int>();

            foreach (var pump in ConfiguredPumps)
            {
                foreach (var nozzle in pump.Nozzles)
                {
                    if (allNozzleNumbers.Contains(nozzle.NozzleNumber))
                    {
                        StationStatusMessage = $"❌ Duplicate Nozzle Number {nozzle.NozzleNumber} detected! Each nozzle must be unique.";
                        MessageBox.Show($"Duplicate Nozzle Number {nozzle.NozzleNumber} detected across pumps.\nEach nozzle number must be unique.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    allNozzleNumbers.Add(nozzle.NozzleNumber);

                    flatMappings.Add(new PumpMapping
                    {
                        PumpMappingId = nozzle.PumpMappingId,
                        PumpId = pump.PumpId,
                        NozzleNumber = nozzle.NozzleNumber,
                        FuelType = string.IsNullOrWhiteSpace(nozzle.FuelType) ? "MS-I" : nozzle.FuelType.Trim(),
                        TankName = nozzle.TankName ?? "",
                        IsActive = true,
                        CreatedAt = DateTime.Now
                    });
                }
            }

            // 1. Batch Save Tanks to DB
            await _stationConfigService.SaveTanksAsync(ConfiguredTanks);

            // 2. Save Pump Mappings to DB
            var success = await _stationConfigService.SavePumpMappingsAsync(flatMappings);
            if (success)
            {
                // 3. Automatically persist this configuration as an active layout preset
                if (string.IsNullOrWhiteSpace(PresetCode))
                {
                    GeneratePresetCode();
                }
                if (string.IsNullOrWhiteSpace(PresetName))
                {
                    PresetName = $"{ConfiguredPumps.Count}-Pump Layout";
                }

                var presetData = new StationPresetData
                {
                    PresetCode = PresetCode.Trim().ToUpperInvariant(),
                    PresetName = PresetName.Trim(),
                    Description = PresetDescription?.Trim() ?? "Active Station Layout",
                    Tanks = ConfiguredTanks.Select(t => new TankPresetItem
                    {
                        TankName = t.TankName,
                        CapacityKL = t.CapacityKL,
                        FuelType = t.FuelType,
                        IsActive = t.IsActive,
                        HasTesting = t.HasTesting
                    }).ToList(),
                    Pumps = ConfiguredPumps.Select(p => new PumpPresetItem
                    {
                        PumpId = p.PumpId,
                        Nozzles = p.Nozzles.Select(n => new NozzlePresetItem
                        {
                            NozzleNumber = n.NozzleNumber,
                            FuelType = n.FuelType,
                            TankName = n.TankName
                        }).ToList()
                    }).ToList()
                };

                var json = System.Text.Json.JsonSerializer.Serialize(presetData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                var entity = new StationLayoutPreset
                {
                    PresetCode = PresetCode.Trim().ToUpperInvariant(),
                    PresetName = PresetName.Trim(),
                    Description = PresetDescription?.Trim() ?? "Active Station Layout",
                    LayoutJson = json,
                    PumpCount = ConfiguredPumps.Count,
                    NozzleCount = ConfiguredPumps.SelectMany(p => p.Nozzles).Count(),
                    TankCount = ConfiguredTanks.Count,
                    IsActive = true
                };
                await _stationConfigService.SavePresetAsync(entity);
                await LoadAllPresetsAsync();

                RefreshAvailableTankNames();
                RefreshAvailableFuelTypes();

                StationStatusMessage = $"✅ Station layout and tanks successfully saved! ({flatMappings.Count} nozzles, {ConfiguredTanks.Count} tanks configured)";
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _syncEngine.ForceSyncAsync();
                    }
                    catch
                    {
                    }
                });
                MessageBox.Show("Station layout and storage tanks successfully saved to active database and runtime cache.", "Station Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StationStatusMessage = "❌ Failed to save station layout.";
            }
        }
        catch (Exception ex)
        {
            StationStatusMessage = $"❌ Save error: {ex.Message}";
        }
        finally
        {
            _isSavingLayout = false;
        }
    }

    // ── Tanks ──
    [RelayCommand]
    public async Task AddTankAsync()
    {
        var nextNum = ConfiguredTanks.Count + 1;
        var defaultFuel = AvailableFuelTypes.FirstOrDefault() ?? "HSD";
        var newTank = new TankDefinition
        {
            TankName = $"Tank {nextNum} - 20KL",
            CapacityKL = 20.0,
            FuelType = defaultFuel,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
        ConfiguredTanks.Add(newTank);
        RefreshAvailableTankNames();

        try
        {
            _isSavingLayout = true;
            await _stationConfigService.SaveTankAsync(newTank);
            StationStatusMessage = $"✅ Added and saved {newTank.TankName}.";
        }
        catch (Exception ex)
        {
            StationStatusMessage = $"Added tank, save note: {ex.Message}";
        }
        finally
        {
            _isSavingLayout = false;
        }
    }

    [RelayCommand]
    public async Task DeleteTankAsync(TankDefinition? tank)
    {
        if (tank == null) return;
        try
        {
            _isSavingLayout = true;
            if (tank.TankId > 0)
            {
                await _stationConfigService.DeleteTankAsync(tank.TankId);
            }
            ConfiguredTanks.Remove(tank);
            RefreshAvailableTankNames();
            StationStatusMessage = $"Deleted tank: {tank.TankName}";
        }
        catch (Exception ex)
        {
            StationStatusMessage = $"Error deleting tank: {ex.Message}";
        }
        finally
        {
            _isSavingLayout = false;
        }
    }

    [RelayCommand]
    public async Task SaveTanksAsync()
    {
        StationStatusMessage = "⏳ Saving tanks...";
        try
        {
            _isSavingLayout = true;
            var success = await _stationConfigService.SaveTanksAsync(ConfiguredTanks);
            if (success)
            {
                RefreshAvailableTankNames();
                StationStatusMessage = "✅ Tanks saved successfully! Nozzles can now be attached to these tanks.";
                _ = Task.Run(async () =>
                {
                    try { await _syncEngine.ForceSyncAsync(); } catch { }
                });
                MessageBox.Show("Storage tanks successfully saved to database and runtime cache.", "Tanks Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StationStatusMessage = "❌ Failed to save storage tanks.";
            }
        }
        catch (Exception ex)
        {
            StationStatusMessage = $"❌ Save error: {ex.Message}";
        }
        finally
        {
            _isSavingLayout = false;
        }
    }

    // ── PRESET MANAGEMENT ──

    [RelayCommand]
    public void GeneratePresetCode()
    {
        var rand = new Random();
        var letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        var code = $"{letters[rand.Next(letters.Length)]}{letters[rand.Next(letters.Length)]}{letters[rand.Next(letters.Length)]}-{rand.Next(100, 999)}";
        PresetCode = code;
        PresetName = $"{ConfiguredPumps.Count}-Pump Layout";
    }

    [RelayCommand]
    public async Task LoadAllPresetsAsync()
    {
        try
        {
            _isLoadingPresetInternal = true;
            var presets = await _stationConfigService.GetAllPresetsAsync();
            SavedPresets.Clear();
            foreach (var p in presets)
            {
                SavedPresets.Add(p);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load presets");
        }
        finally
        {
            _isLoadingPresetInternal = false;
        }
    }

    [RelayCommand]
    public async Task SavePresetAsync()
    {
        if (string.IsNullOrWhiteSpace(PresetCode))
        {
            GeneratePresetCode();
        }

        if (string.IsNullOrWhiteSpace(PresetName))
        {
            PresetName = $"{ConfiguredPumps.Count}-Pump Layout";
        }

        PresetStatusMessage = "⏳ Saving preset...";
        try
        {
            var presetData = new StationPresetData
            {
                PresetCode = PresetCode.Trim().ToUpperInvariant(),
                PresetName = PresetName.Trim(),
                Description = PresetDescription?.Trim() ?? "",
                Tanks = ConfiguredTanks.Select(t => new TankPresetItem
                {
                    TankName = t.TankName,
                    CapacityKL = t.CapacityKL,
                    FuelType = t.FuelType,
                    IsActive = t.IsActive,
                    HasTesting = t.HasTesting
                }).ToList(),
                Pumps = ConfiguredPumps.Select(p => new PumpPresetItem
                {
                    PumpId = p.PumpId,
                    Nozzles = p.Nozzles.Select(n => new NozzlePresetItem
                    {
                        NozzleNumber = n.NozzleNumber,
                        FuelType = n.FuelType,
                        TankName = n.TankName
                    }).ToList()
                }).ToList()
            };

            var json = System.Text.Json.JsonSerializer.Serialize(presetData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            var entity = new StationLayoutPreset
            {
                PresetCode = PresetCode.Trim().ToUpperInvariant(),
                PresetName = PresetName.Trim(),
                Description = PresetDescription?.Trim() ?? "",
                LayoutJson = json,
                PumpCount = ConfiguredPumps.Count,
                NozzleCount = ConfiguredPumps.SelectMany(p => p.Nozzles).Count(),
                TankCount = ConfiguredTanks.Count,
                IsActive = true
            };

            var success = await _stationConfigService.SavePresetAsync(entity);
            if (success)
            {
                await LoadAllPresetsAsync();
                SelectedPreset = SavedPresets.FirstOrDefault(p => p.PresetCode.Equals(entity.PresetCode, StringComparison.OrdinalIgnoreCase));
                PresetStatusMessage = $"✅ Preset '{PresetName}' ({entity.PresetCode}) successfully saved!";
                MessageBox.Show($"Station layout preset successfully saved with code:\n\n{entity.PresetCode}\n\nYou can use this code anytime to restore this mapping.",
                    "Preset Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                PresetStatusMessage = "❌ Failed to save preset.";
            }
        }
        catch (Exception ex)
        {
            PresetStatusMessage = $"❌ Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task LoadPresetByCodeAsync(string? codeParam)
    {
        var targetCode = (codeParam ?? SearchPresetCode ?? SelectedPreset?.PresetCode ?? PresetCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(targetCode))
        {
            PresetStatusMessage = "❌ Please enter a Preset Code to load.";
            MessageBox.Show("Please enter or select a Preset Code.", "Preset Code Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PresetStatusMessage = $"⏳ Loading preset '{targetCode}'...";
        try
        {
            var preset = await _stationConfigService.GetPresetByCodeAsync(targetCode);
            if (preset == null)
            {
                PresetStatusMessage = $"❌ Preset code '{targetCode}' not found in database.";
                MessageBox.Show($"Preset with code '{targetCode}' was not found in saved presets.", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await LoadPresetDirectAsync(preset);
            MessageBox.Show($"Preset '{preset.PresetName}' ({preset.PresetCode}) loaded onto canvas.\n\nClick 'Save Station Layout' when ready to apply to active station database.",
                "Preset Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            PresetStatusMessage = $"❌ Error loading preset: {ex.Message}";
        }
    }

    private async Task LoadPresetDirectAsync(StationLayoutPreset preset)
    {
        if (preset == null) return;
        try
        {
            var presetData = System.Text.Json.JsonSerializer.Deserialize<StationPresetData>(preset.LayoutJson);
            if (presetData == null)
            {
                PresetStatusMessage = "❌ Corrupt preset layout data.";
                return;
            }

            // Populate Tanks
            ConfiguredTanks.Clear();
            foreach (var t in presetData.Tanks)
            {
                ConfiguredTanks.Add(new TankDefinition
                {
                    TankName = t.TankName,
                    CapacityKL = t.CapacityKL,
                    FuelType = t.FuelType,
                    IsActive = t.IsActive,
                    HasTesting = t.HasTesting,
                    CreatedAt = DateTime.Now
                });
            }
            RefreshAvailableTankNames();

            // Populate Pumps & Nozzles
            ConfiguredPumps.Clear();
            foreach (var p in presetData.Pumps)
            {
                var pumpVm = new PumpCardVm { PumpId = p.PumpId };
                foreach (var n in p.Nozzles)
                {
                    var nozzleVm = new NozzleItemVm
                    {
                        NozzleNumber = n.NozzleNumber,
                        FuelType = n.FuelType,
                        TankName = n.TankName,
                        OnTankNameChangedCallback = HandleNozzleTankChanged,
                        OnFuelTypeChangedCallback = HandleNozzleFuelTypeChanged
                    };
                    pumpVm.Nozzles.Add(nozzleVm);
                }
                ConfiguredPumps.Add(pumpVm);
            }

            RefreshAvailableFuelTypes();

            _isLoadingPresetInternal = true;
            PresetCode = preset.PresetCode;
            PresetName = preset.PresetName;
            PresetDescription = preset.Description;
            SearchPresetCode = preset.PresetCode;
            SelectedPreset = SavedPresets.FirstOrDefault(p => p.PresetCode.Equals(preset.PresetCode, StringComparison.OrdinalIgnoreCase));
            _isLoadingPresetInternal = false;

            StationStatusMessage = $"✅ Loaded preset '{preset.PresetName}' ({preset.PresetCode}). Click 'Save Station Layout' to commit.";
            PresetStatusMessage = $"✅ Preset '{preset.PresetCode}' loaded ({ConfiguredPumps.Count} pumps, {ConfiguredTanks.Count} tanks).";
        }
        catch (Exception ex)
        {
            PresetStatusMessage = $"❌ Error loading preset: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task DeletePresetAsync(StationLayoutPreset? preset)
    {
        var target = preset ?? SelectedPreset;
        if (target == null) return;

        var result = MessageBox.Show($"Are you sure you want to delete preset '{target.PresetName}' ({target.PresetCode})?",
            "Confirm Delete Preset", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _stationConfigService.DeletePresetAsync(target.PresetId);
            SavedPresets.Remove(target);
            PresetStatusMessage = $"Deleted preset: {target.PresetCode}";
        }
        catch (Exception ex)
        {
            PresetStatusMessage = $"❌ Error deleting preset: {ex.Message}";
        }
    }

    private async Task EnsureCloudSettingsPersistedAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(SupabaseUrl) && !string.IsNullOrWhiteSpace(SupabaseApiKey))
            {
                var normalizedStationId = string.IsNullOrWhiteSpace(StationId) ? "" : System.Text.RegularExpressions.Regex.Replace(StationId.Trim(), @"\s+", "_").ToUpperInvariant();
                var current = await _syncConfigService.GetSettingsAsync();
                current.SupabaseUrl = SupabaseUrl.Trim();
                current.SupabaseApiKey = SupabaseApiKey.Trim();
                if (!string.IsNullOrWhiteSpace(SupabaseServiceRoleKey))
                    current.SupabaseServiceRoleKey = SupabaseServiceRoleKey.Trim();
                if (!string.IsNullOrWhiteSpace(normalizedStationId))
                    current.StationId = normalizedStationId;
                if (!string.IsNullOrWhiteSpace(MachineId))
                    current.MachineId = MachineId.Trim();
                await _syncConfigService.SaveSettingsAsync(current);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to auto-persist cloud settings before cloud operation");
        }
    }

    private async Task SaveAllTabsLocallyAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(PresetCode))
            {
                await SavePresetAsync();
            }

            await SaveStationLayoutAsync();

            for (int i = 0; i < ConfiguredCollectionTypes.Count; i++)
            {
                ConfiguredCollectionTypes[i].DisplayOrder = i + 1;
                await _collectionTypeService.SaveCollectionTypeAsync(ConfiguredCollectionTypes[i]);
            }

            await SaveFeaturesAsync();
            await SaveShiftCycleConfigurationAsync();
            await SavePumpConnectionInternalAsync(showDialogs: false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to auto-save all tabs locally before cloud snapshot");
        }
    }

    [RelayCommand]
    public async Task SavePresetToCloudAsync()
    {
        PresetStatusMessage = "☁️ Saving preset & layout to Supabase Cloud...";
        try
        {
            await EnsureCloudSettingsPersistedAsync();
            await SaveAllTabsLocallyAsync();

            var (success, message) = await _syncEngine.PushStationSnapshotToCloudAsync(StationId);
            if (success)
            {
                PresetStatusMessage = $"☁️ Preset & station layout saved to Cloud for station '{StationId}'!";
                MessageBox.Show($"Station layout and preset have been successfully pushed to Supabase Cloud for Station ID:\n\n{StationId}\n\nAny remote machine can now click 'Load from Cloud' to restore this exact configuration.",
                    "Cloud Preset Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                PresetStatusMessage = $"❌ {message}";
                MessageBox.Show($"Failed to push layout to cloud:\n\n{message}",
                    "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            PresetStatusMessage = $"❌ Cloud save error: {ex.Message}";
            MessageBox.Show($"Exception saving preset to cloud:\n\n{ex.Message}", "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task LoadPresetFromCloudAsync()
    {
        PresetStatusMessage = "☁️ Fetching preset & layout from Supabase Cloud...";
        try
        {
            await EnsureCloudSettingsPersistedAsync();

            var (success, message) = await _syncEngine.PullStationSnapshotFromCloudAsync(StationId);
            if (success)
            {
                await LoadStationAndCollectionsAsync();
                PresetStatusMessage = $"☁️ Successfully loaded preset & layout from Cloud for station '{StationId}'!";
                MessageBox.Show($"Station layout (pumps, nozzles, tanks, and fuel mapping) successfully loaded and applied from Supabase Cloud for Station ID:\n\n{StationId}",
                    "Cloud Preset Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                PresetStatusMessage = $"❌ {message}";
                MessageBox.Show($"Could not load preset from cloud:\n\n{message}",
                    "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            PresetStatusMessage = $"❌ Cloud fetch error: {ex.Message}";
            MessageBox.Show($"Exception loading preset from cloud:\n\n{ex.Message}", "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task SaveStationLayoutToCloudAsync()
    {
        StationStatusMessage = "☁️ Pushing station layout to Supabase Cloud...";
        try
        {
            await EnsureCloudSettingsPersistedAsync();
            await SaveAllTabsLocallyAsync();

            var (success, message) = await _syncEngine.PushStationSnapshotToCloudAsync(StationId);
            if (success)
            {
                StationStatusMessage = $"☁️ Station layout saved to Cloud for station '{StationId}'!";
                MessageBox.Show($"Station layout (pumps, nozzles, tanks) successfully pushed to Supabase Cloud for Station ID:\n\n{StationId}",
                    "Layout Pushed to Cloud", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StationStatusMessage = $"❌ {message}";
                MessageBox.Show($"Failed to push layout to cloud:\n\n{message}",
                    "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StationStatusMessage = $"❌ Cloud push error: {ex.Message}";
            MessageBox.Show($"Exception pushing layout to cloud:\n\n{ex.Message}", "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task LoadStationLayoutFromCloudAsync()
    {
        StationStatusMessage = "☁️ Pulling station layout from Supabase Cloud...";
        try
        {
            await EnsureCloudSettingsPersistedAsync();

            var (success, message) = await _syncEngine.PullStationSnapshotFromCloudAsync(StationId);
            if (success)
            {
                await LoadStationAndCollectionsAsync();
                StationStatusMessage = $"☁️ Station layout loaded from Cloud for station '{StationId}'!";
                MessageBox.Show($"Station layout successfully loaded from Supabase Cloud for Station ID:\n\n{StationId}",
                    "Layout Pulled from Cloud", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                StationStatusMessage = $"❌ {message}";
                MessageBox.Show($"Failed to pull layout from cloud:\n\n{message}",
                    "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StationStatusMessage = $"❌ Cloud pull error: {ex.Message}";
            MessageBox.Show($"Exception pulling layout from cloud:\n\n{ex.Message}", "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task SaveFeaturesToCloudAsync()
    {
        FeatureStatusMessage = "☁️ Saving client features to Supabase Cloud...";
        try
        {
            await EnsureCloudSettingsPersistedAsync();
            await SaveAllTabsLocallyAsync();

            var (success, message) = await _syncEngine.PushStationSnapshotToCloudAsync(StationId);
            if (success)
            {
                FeatureStatusMessage = $"☁️ Client feature toggles saved to Cloud for station '{StationId}'!";
                MessageBox.Show($"Client feature toggles and page access settings successfully pushed to Supabase Cloud for Station ID:\n\n{StationId}",
                    "Features Pushed to Cloud", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                FeatureStatusMessage = $"❌ {message}";
                MessageBox.Show($"Failed to push features to cloud:\n\n{message}",
                    "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            FeatureStatusMessage = $"❌ Cloud push error: {ex.Message}";
            MessageBox.Show($"Exception saving features to cloud:\n\n{ex.Message}", "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task LoadFeaturesFromCloudAsync()
    {
        FeatureStatusMessage = "☁️ Fetching client features from Supabase Cloud...";
        try
        {
            await EnsureCloudSettingsPersistedAsync();

            var (success, message) = await _syncEngine.PullStationSnapshotFromCloudAsync(StationId);
            if (success)
            {
                await LoadFeaturesAsync();
                FeatureStatusMessage = $"☁️ Client feature toggles loaded from Cloud for station '{StationId}'!";
                MessageBox.Show($"Client feature toggles successfully loaded from Supabase Cloud for Station ID:\n\n{StationId}",
                    "Features Loaded from Cloud", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                FeatureStatusMessage = $"❌ {message}";
                MessageBox.Show($"Failed to fetch feature toggles from cloud:\n\n{message}",
                    "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            FeatureStatusMessage = $"❌ Cloud fetch error: {ex.Message}";
            MessageBox.Show($"Exception loading features from cloud:\n\n{ex.Message}", "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task SaveCollectionsToCloudAsync()
    {
        CollectionStatusMessage = "☁️ Saving collection types to Supabase Cloud...";
        try
        {
            await EnsureCloudSettingsPersistedAsync();
            await SaveAllTabsLocallyAsync();

            var (success, message) = await _syncEngine.PushStationSnapshotToCloudAsync(StationId);
            if (success)
            {
                CollectionStatusMessage = $"☁️ Dynamic collection types saved to Cloud for station '{StationId}'!";
                MessageBox.Show($"Payment collection types successfully pushed to Supabase Cloud for Station ID:\n\n{StationId}",
                    "Collections Pushed to Cloud", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                CollectionStatusMessage = $"❌ {message}";
                MessageBox.Show($"Failed to push collections to cloud:\n\n{message}",
                    "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            CollectionStatusMessage = $"❌ Cloud push error: {ex.Message}";
            MessageBox.Show($"Exception saving collections to cloud:\n\n{ex.Message}", "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task LoadCollectionsFromCloudAsync()
    {
        CollectionStatusMessage = "☁️ Fetching collection types from Supabase Cloud...";
        try
        {
            await EnsureCloudSettingsPersistedAsync();

            var (success, message) = await _syncEngine.PullStationSnapshotFromCloudAsync(StationId);
            if (success)
            {
                await LoadStationAndCollectionsAsync();
                CollectionStatusMessage = $"☁️ Dynamic collection types loaded from Cloud for station '{StationId}'!";
                MessageBox.Show($"Payment collection types successfully loaded from Supabase Cloud for Station ID:\n\n{StationId}",
                    "Collections Loaded from Cloud", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                CollectionStatusMessage = $"❌ {message}";
                MessageBox.Show($"Failed to fetch collection types from cloud:\n\n{message}",
                    "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            CollectionStatusMessage = $"❌ Cloud fetch error: {ex.Message}";
            MessageBox.Show($"Exception loading collections from cloud:\n\n{ex.Message}", "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task PullCompleteStationSnapshotAsync()
    {
        DiagnosticsMessage = "☁️ Downloading complete Station Image from Cloud...";
        try
        {
            await EnsureCloudSettingsPersistedAsync();

            var (success, message) = await _syncEngine.PullStationSnapshotFromCloudAsync(StationId);
            if (success)
            {
                await LoadStationAndCollectionsAsync();
                await LoadFeaturesAsync();
                DiagnosticsMessage = $"☁️ Station Image loaded from Cloud for '{StationId}'!";
                MessageBox.Show($"Complete Station Snapshot (Pumps, Nozzles, Tanks, Presets, Client Features, and Payment Collections) has been successfully downloaded and applied for Station ID:\n\n{StationId}",
                    "Station Image Synchronized", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                DiagnosticsMessage = $"❌ {message}";
                MessageBox.Show($"Failed to download station snapshot from cloud:\n\n{message}",
                    "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            DiagnosticsMessage = $"❌ Error: {ex.Message}";
            MessageBox.Show($"Exception downloading complete station image:\n\n{ex.Message}", "Cloud Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Collections ──
    [RelayCommand]
    public void AddCollectionType()
    {
        var nextOrder = ConfiguredCollectionTypes.Select(c => c.DisplayOrder).DefaultIfEmpty(0).Max() + 1;
        ConfiguredCollectionTypes.Add(new CollectionTypeMaster
        {
            Code = $"NEW_COLLECTION_{nextOrder}",
            DisplayName = "New Collection Method",
            Category = "Online",
            HasTidBatch = true,
            DisplayOrder = nextOrder,
            IsActive = true,
            IsSystem = false,
            CreatedAt = DateTime.Now
        });
        CollectionStatusMessage = "Added new Collection Type. Fill details and click 'Save Collection Types'.";
    }

    [RelayCommand]
    public async Task DeleteCollectionTypeAsync(CollectionTypeMaster? col)
    {
        if (col == null) return;
        try
        {
            if (col.CollectionTypeId > 0)
            {
                await _collectionTypeService.DeleteCollectionTypeAsync(col.CollectionTypeId);
            }
            ConfiguredCollectionTypes.Remove(col);
            CollectionStatusMessage = $"Removed collection type '{col.DisplayName}'.";
            await LoadStationAndCollectionsAsync();
        }
        catch (Exception ex)
        {
            CollectionStatusMessage = $"❌ Error removing collection type: {ex.Message}";
        }
    }

    [RelayCommand]
    public void MoveCollectionUp(CollectionTypeMaster? col)
    {
        if (col == null) return;
        var idx = ConfiguredCollectionTypes.IndexOf(col);
        if (idx > 0)
        {
            ConfiguredCollectionTypes.Move(idx, idx - 1);
            for (int i = 0; i < ConfiguredCollectionTypes.Count; i++)
            {
                ConfiguredCollectionTypes[i].DisplayOrder = i + 1;
            }
        }
    }

    [RelayCommand]
    public void MoveCollectionDown(CollectionTypeMaster? col)
    {
        if (col == null) return;
        var idx = ConfiguredCollectionTypes.IndexOf(col);
        if (idx < ConfiguredCollectionTypes.Count - 1)
        {
            ConfiguredCollectionTypes.Move(idx, idx + 1);
            for (int i = 0; i < ConfiguredCollectionTypes.Count; i++)
            {
                ConfiguredCollectionTypes[i].DisplayOrder = i + 1;
            }
        }
    }

    [RelayCommand]
    public async Task SaveCollectionsAsync()
    {
        CollectionStatusMessage = "⏳ Saving collection types...";
        try
        {
            for (int i = 0; i < ConfiguredCollectionTypes.Count; i++)
            {
                ConfiguredCollectionTypes[i].DisplayOrder = i + 1;
                await _collectionTypeService.SaveCollectionTypeAsync(ConfiguredCollectionTypes[i]);
            }
            CollectionStatusMessage = "✅ Collection types saved successfully!";
            _ = Task.Run(async () =>
            {
                try { await _syncEngine.ForceSyncAsync(); } catch { }
            });
            MessageBox.Show("Collection types successfully saved! DSM Entry and TID sheets will now use these types.", "Collections Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadStationAndCollectionsAsync();
        }
        catch (Exception ex)
        {
            CollectionStatusMessage = $"❌ Save error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task SaveShiftCycleConfigurationAsync()
    {
        ShiftCycleStatusMessage = "⏳ Saving shift cycle configuration...";
        try
        {
            bool useMorningNight = SelectedShiftCycleOption.StartsWith("3_SLOT");
            var allFeatures = await _featureToggleService.GetAllFeaturesAsync();
            
            var morningNightFeature = allFeatures.FirstOrDefault(f => f.FeatureKey == "Collection_UseMorningNight");
            if (morningNightFeature != null)
            {
                morningNightFeature.IsEnabled = useMorningNight;
            }
            else
            {
                allFeatures.Add(new AppFeatureSetting
                {
                    FeatureKey = "Collection_UseMorningNight",
                    DisplayName = "Use Morning / Night Settlement Slots",
                    Description = "Enable 3-slot midnight split for collections (Morning, Day, Night)",
                    Category = "Collections",
                    TargetRole = "Global",
                    IsEnabled = useMorningNight,
                    DisplayOrder = 1
                });
            }

            var shiftCycleFeature = allFeatures.FirstOrDefault(f => f.FeatureKey == "Station_ShiftCycleMode");
            if (shiftCycleFeature != null)
            {
                shiftCycleFeature.IsEnabled = useMorningNight;
                shiftCycleFeature.DisplayName = useMorningNight ? "3_SLOT_SPLIT" : "2_SHIFT";
            }
            else
            {
                allFeatures.Add(new AppFeatureSetting
                {
                    FeatureKey = "Station_ShiftCycleMode",
                    DisplayName = useMorningNight ? "3_SLOT_SPLIT" : "2_SHIFT",
                    Description = "Station Shift Cycle Mode (2_SHIFT vs 3_SLOT_SPLIT)",
                    Category = "Station",
                    TargetRole = "Global",
                    IsEnabled = useMorningNight,
                    DisplayOrder = 2
                });
            }

            await _featureToggleService.SaveFeaturesAsync(allFeatures);
            await _featureToggleService.RefreshCacheAsync();

            ShiftCycleStatusMessage = $"✅ Shift cycle set to {(useMorningNight ? "3-Slot Split (Morning/Day/Night)" : "Standard 2-Shift (Shift A & Shift B)")}!";
            _ = Task.Run(async () =>
            {
                try { await _syncEngine.ForceSyncAsync(); } catch { }
            });
            MessageBox.Show($"Shift cycle schedule successfully configured as: {(useMorningNight ? "3-Slot Split (Morning/Day/Night)" : "Standard 2-Shift (Shift A & Shift B)")}.\n\nTID sheets, reports, and Debtor Repayments will now align accordingly.", "Shift Cycle Updated", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShiftCycleStatusMessage = $"❌ Save error: {ex.Message}";
            MessageBox.Show($"Failed to save shift cycle configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ───────────────────────────────────────────────
    // TAB 6: PUMP CONNECTION CONFIGURATION (MULTI-PUMP GROUPS)
    // ───────────────────────────────────────────────

    [ObservableProperty] private bool _isPumpConnectionEnabled;
    [ObservableProperty] private string _selectedPumpConnectionMode = PumpConnectionModes.Off;
    public ObservableCollection<PumpConnectionGroupVm> PumpConnectionGroups { get; } = new();
    [ObservableProperty] private string _pumpConnectionStatusMessage = string.Empty;
    [ObservableProperty] private string _pumpConnectionValidationSummary = string.Empty;
    [ObservableProperty] private bool _hasPumpConnectionValidationError;

    public bool IsModeOff
    {
        get => SelectedPumpConnectionMode == PumpConnectionModes.Off;
        set { if (value) SetPumpConnectionMode(PumpConnectionModes.Off); }
    }

    public bool IsMode2Pumps
    {
        get => SelectedPumpConnectionMode == PumpConnectionModes.TwoPumps;
        set { if (value) SetPumpConnectionMode(PumpConnectionModes.TwoPumps); }
    }

    public bool IsMode3Pumps
    {
        get => SelectedPumpConnectionMode == PumpConnectionModes.ThreePumps;
        set { if (value) SetPumpConnectionMode(PumpConnectionModes.ThreePumps); }
    }

    public bool IsMode4Pumps
    {
        get => SelectedPumpConnectionMode == PumpConnectionModes.FourPumps;
        set { if (value) SetPumpConnectionMode(PumpConnectionModes.FourPumps); }
    }

    partial void OnIsPumpConnectionEnabledChanged(bool value)
    {
        if (!value)
        {
            SelectedPumpConnectionMode = PumpConnectionModes.Off;
        }
        else if (SelectedPumpConnectionMode == PumpConnectionModes.Off)
        {
            SelectedPumpConnectionMode = PumpConnectionModes.TwoPumps;
        }

        NotifyModeProperties();
        ReevaluatePumpConnectionConstraints();
    }

    [RelayCommand]
    public void SetPumpConnectionMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) mode = PumpConnectionModes.Off;
        SelectedPumpConnectionMode = mode;
        IsPumpConnectionEnabled = mode != PumpConnectionModes.Off;
        NotifyModeProperties();
        ReevaluatePumpConnectionConstraints();
    }

    private void NotifyModeProperties()
    {
        OnPropertyChanged(nameof(IsModeOff));
        OnPropertyChanged(nameof(IsMode2Pumps));
        OnPropertyChanged(nameof(IsMode3Pumps));
        OnPropertyChanged(nameof(IsMode4Pumps));
    }

    public void ReevaluatePumpConnectionConstraints()
    {
        var physicalPumps = ConfiguredPumps.Select(p => p.PumpId).Distinct().OrderBy(id => id).ToList();
        if (physicalPumps.Count == 0) physicalPumps = new List<int> { 1, 2, 3, 4 };

        // Ensure available pumps and candidates are present in all groups
        foreach (var group in PumpConnectionGroups)
        {
            // Sync available primary pumps
            for (int i = group.AvailablePrimaryPumps.Count - 1; i >= 0; i--)
            {
                if (!physicalPumps.Contains(group.AvailablePrimaryPumps[i]))
                    group.AvailablePrimaryPumps.RemoveAt(i);
            }
            foreach (var pid in physicalPumps)
            {
                if (!group.AvailablePrimaryPumps.Contains(pid))
                    group.AvailablePrimaryPumps.Add(pid);
            }

            // Sync candidate slaves
            for (int i = group.CandidateSlaves.Count - 1; i >= 0; i--)
            {
                if (!physicalPumps.Contains(group.CandidateSlaves[i].PumpId))
                    group.CandidateSlaves.RemoveAt(i);
            }
            foreach (var pid in physicalPumps)
            {
                if (!group.CandidateSlaves.Any(c => c.PumpId == pid))
                {
                    group.CandidateSlaves.Add(new PumpCandidateVm
                    {
                        PumpId = pid,
                        IsSelected = false,
                        OnSelectionChangedCallback = ReevaluatePumpConnectionConstraints
                    });
                }
            }
        }

        if (!IsPumpConnectionEnabled || SelectedPumpConnectionMode == PumpConnectionModes.Off)
        {
            foreach (var g in PumpConnectionGroups)
            {
                g.HasError = false;
                g.ValidationMessage = string.Empty;
                foreach (var c in g.CandidateSlaves)
                {
                    c.IsEnabled = false;
                    c.DisabledReason = "Pump connections are disabled (OFF).";
                }
            }
            HasPumpConnectionValidationError = false;
            PumpConnectionValidationSummary = "Connection mode is OFF. Standalone single-pump operation is active.";
            return;
        }

        int maxConnected = PumpConnectionModes.GetMaxConnectedPumps(SelectedPumpConnectionMode);
        var summaryErrors = new List<string>();

        // Map each pump ID to group names it is assigned to (either as Primary or as selected Slave)
        var pumpUsage = new Dictionary<int, List<string>>();

        foreach (var group in PumpConnectionGroups)
        {
            var groupLabel = string.IsNullOrWhiteSpace(group.GroupName) ? $"Group {group.GroupId}" : group.GroupName;

            if (group.PrimaryPumpId > 0)
            {
                if (!pumpUsage.TryGetValue(group.PrimaryPumpId, out var list))
                {
                    list = new List<string>();
                    pumpUsage[group.PrimaryPumpId] = list;
                }
                list.Add($"{groupLabel} (Primary)");
            }

            var selectedSlaves = group.CandidateSlaves
                .Where(c => c.IsSelected && c.PumpId != group.PrimaryPumpId)
                .Select(c => c.PumpId)
                .ToList();

            foreach (var slaveId in selectedSlaves)
            {
                if (!pumpUsage.TryGetValue(slaveId, out var list))
                {
                    list = new List<string>();
                    pumpUsage[slaveId] = list;
                }
                list.Add($"{groupLabel} (Connected)");
            }
        }

        // Evaluate each group and configure candidate controls
        foreach (var group in PumpConnectionGroups)
        {
            var groupLabel = string.IsNullOrWhiteSpace(group.GroupName) ? $"Group {group.GroupId}" : group.GroupName;
            var groupErrors = new List<string>();

            if (group.PrimaryPumpId <= 0)
            {
                groupErrors.Add("Please select a Primary Pump.");
            }

            int expectedTotal = PumpConnectionModes.GetMaxGroupSize(SelectedPumpConnectionMode);
            int expectedConnected = expectedTotal - 1;
            int selectedSlavesCount = group.CandidateSlaves.Count(c => c.IsSelected && c.PumpId != group.PrimaryPumpId);
            if (selectedSlavesCount != expectedConnected)
            {
                groupErrors.Add($"Mode {SelectedPumpConnectionMode} requires {expectedTotal} pumps (1 Primary + {expectedConnected} Connected). Currently selected: {selectedSlavesCount}.");
            }

            // Check if primary pump is used elsewhere
            if (group.PrimaryPumpId > 0 && pumpUsage.TryGetValue(group.PrimaryPumpId, out var pUsages) && pUsages.Count > 1)
            {
                groupErrors.Add($"Primary Pump #{group.PrimaryPumpId} is assigned in multiple locations: {string.Join(", ", pUsages)}.");
            }

            // Check if any selected slave is used elsewhere
            var selectedSlaves = group.CandidateSlaves.Where(c => c.IsSelected && c.PumpId != group.PrimaryPumpId).ToList();
            foreach (var slave in selectedSlaves)
            {
                if (pumpUsage.TryGetValue(slave.PumpId, out var sUsages) && sUsages.Count > 1)
                {
                    groupErrors.Add($"Connected Pump #{slave.PumpId} is assigned in multiple locations: {string.Join(", ", sUsages)}.");
                }
            }

            group.HasError = groupErrors.Count > 0;
            group.ValidationMessage = string.Join(" ", groupErrors);
            if (group.HasError)
            {
                summaryErrors.Add($"{groupLabel}: {group.ValidationMessage}");
            }

            // Set enablements on candidates
            foreach (var candidate in group.CandidateSlaves)
            {
                if (candidate.PumpId == group.PrimaryPumpId)
                {
                    candidate.IsEnabled = false;
                    candidate.IsSelected = false;
                    candidate.DisabledReason = "Configured as Primary for this group.";
                }
                else
                {
                    // Check if candidate pump is used in another group
                    bool usedInOtherGroup = false;
                    string usedInLabel = "";
                    foreach (var otherGroup in PumpConnectionGroups)
                    {
                        if (otherGroup == group) continue;
                        if (otherGroup.PrimaryPumpId == candidate.PumpId)
                        {
                            usedInOtherGroup = true;
                            usedInLabel = $"{otherGroup.GroupName} (Primary)";
                            break;
                        }
                        if (otherGroup.CandidateSlaves.Any(c => c.IsSelected && c.PumpId == candidate.PumpId))
                        {
                            usedInOtherGroup = true;
                            usedInLabel = $"{otherGroup.GroupName} (Connected)";
                            break;
                        }
                    }

                    if (usedInOtherGroup)
                    {
                        candidate.IsEnabled = false;
                        candidate.DisabledReason = $"Pump #{candidate.PumpId} is assigned in {usedInLabel}.";
                    }
                    else if (!candidate.IsSelected && selectedSlavesCount >= maxConnected)
                    {
                        candidate.IsEnabled = false;
                        candidate.DisabledReason = $"Mode {SelectedPumpConnectionMode} permits maximum {maxConnected} connected pump(s).";
                    }
                    else
                    {
                        candidate.IsEnabled = true;
                        candidate.DisabledReason = string.Empty;
                    }
                }
            }
        }

        HasPumpConnectionValidationError = summaryErrors.Count > 0;
        PumpConnectionValidationSummary = HasPumpConnectionValidationError
            ? string.Join(" | ", summaryErrors)
            : $"✅ Topology valid: Mode '{SelectedPumpConnectionMode}' active. Each group allows 1 Primary + up to {maxConnected} connected pump(s).";
    }

    [RelayCommand]
    public void AddPumpConnectionGroup()
    {
        var physicalPumps = ConfiguredPumps.Select(p => p.PumpId).Distinct().OrderBy(id => id).ToList();
        if (physicalPumps.Count == 0) physicalPumps = new List<int> { 1, 2, 3, 4 };

        var usedPumps = new HashSet<int>();
        foreach (var g in PumpConnectionGroups)
        {
            if (g.PrimaryPumpId > 0) usedPumps.Add(g.PrimaryPumpId);
            foreach (var s in g.CandidateSlaves.Where(c => c.IsSelected))
                usedPumps.Add(s.PumpId);
        }

        var defaultPrimary = physicalPumps.FirstOrDefault(id => !usedPumps.Contains(id));
        if (defaultPrimary == 0) defaultPrimary = physicalPumps.FirstOrDefault();

        int newGroupId = PumpConnectionGroups.Select(g => g.GroupId).DefaultIfEmpty(0).Max() + 1;
        var newGroup = new PumpConnectionGroupVm
        {
            GroupId = newGroupId,
            GroupName = $"Group {newGroupId}",
            PrimaryPumpId = defaultPrimary,
            OnGroupChangedCallback = ReevaluatePumpConnectionConstraints
        };

        foreach (var pumpId in physicalPumps)
        {
            newGroup.AvailablePrimaryPumps.Add(pumpId);
            newGroup.CandidateSlaves.Add(new PumpCandidateVm
            {
                PumpId = pumpId,
                IsSelected = false,
                OnSelectionChangedCallback = ReevaluatePumpConnectionConstraints
            });
        }

        PumpConnectionGroups.Add(newGroup);
        ReevaluatePumpConnectionConstraints();
        PumpConnectionStatusMessage = $"Added {newGroup.GroupName}. Select Primary and connected pumps, then save.";
    }

    [RelayCommand]
    public void RemovePumpConnectionGroup(PumpConnectionGroupVm? group)
    {
        if (group == null) return;
        PumpConnectionGroups.Remove(group);
        ReevaluatePumpConnectionConstraints();
        PumpConnectionStatusMessage = $"Removed {group.GroupName}.";
    }

    [RelayCommand]
    public async Task SavePumpConnectionConfigurationAsync()
    {
        await SavePumpConnectionInternalAsync(showDialogs: true);
    }

    public async Task<bool> SavePumpConnectionInternalAsync(bool showDialogs)
    {
        PumpConnectionStatusMessage = "⏳ Saving pump connection rules...";
        try
        {
            ReevaluatePumpConnectionConstraints();
            if (HasPumpConnectionValidationError)
            {
                if (showDialogs)
                {
                    MessageBox.Show(
                        $"Cannot save pump connection configuration due to validation errors:\n\n{PumpConnectionValidationSummary}",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                PumpConnectionStatusMessage = "❌ Save aborted: Fix topology errors.";
                return false;
            }

            var connectionSize = PumpConnectionModes.ModeToConnectionSize(SelectedPumpConnectionMode);
            var config = new PumpConnectionConfiguration
            {
                IsEnabled = IsPumpConnectionEnabled && SelectedPumpConnectionMode != PumpConnectionModes.Off,
                ConnectionSize = connectionSize,
                Mode = SelectedPumpConnectionMode,
                Groups = PumpConnectionGroups.Select(g => new PumpConnectionGroup
                {
                    GroupId = g.GroupId,
                    GroupName = g.GroupName,
                    PrimaryPumpId = g.PrimaryPumpId,
                    ConnectedPumpIds = g.GetSelectedConnectedPumpIds()
                }).ToList()
            };

            if (!config.Validate(out var validationErrors))
            {
                var errorText = string.Join("\n", validationErrors);
                if (showDialogs)
                {
                    MessageBox.Show($"Topology Validation Failed:\n\n{errorText}", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                PumpConnectionStatusMessage = "❌ Validation failed.";
                return false;
            }

            bool success = await _stationConfigService.SavePumpConnectionConfigurationAsync(config);
            if (success)
            {
                PumpConnectionStatusMessage = $"✅ Connection configuration saved! Mode: {config.Mode}, {config.Groups.Count} group(s).";
                if (showDialogs)
                {
                    MessageBox.Show(
                        $"Pump Connection Configuration successfully saved!\n\nMode: {config.Mode}\nActive Groups: {config.Groups.Count}",
                        "Configuration Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return true;
            }
            else
            {
                PumpConnectionStatusMessage = "❌ Failed to persist configuration.";
                return false;
            }
        }
        catch (Exception ex)
        {
            PumpConnectionStatusMessage = $"❌ Error: {ex.Message}";
            Log.Error(ex, "Failed to save pump connection configuration");
            return false;
        }
    }

    public async Task LoadPumpConnectionConfigurationAsync()
    {
        try
        {
            var config = await _stationConfigService.GetPumpConnectionConfigurationAsync();
            IsPumpConnectionEnabled = config.IsEnabled;
            SelectedPumpConnectionMode = string.IsNullOrWhiteSpace(config.Mode) ? PumpConnectionModes.Off : config.Mode;

            NotifyModeProperties();

            var physicalPumps = ConfiguredPumps.Select(p => p.PumpId).Distinct().OrderBy(id => id).ToList();
            if (physicalPumps.Count == 0) physicalPumps = new List<int> { 1, 2, 3, 4 };

            PumpConnectionGroups.Clear();

            foreach (var groupConfig in config.Groups)
            {
                var groupVm = new PumpConnectionGroupVm
                {
                    GroupId = groupConfig.GroupId,
                    GroupName = string.IsNullOrWhiteSpace(groupConfig.GroupName) ? $"Group {groupConfig.GroupId}" : groupConfig.GroupName,
                    PrimaryPumpId = groupConfig.PrimaryPumpId,
                    OnGroupChangedCallback = ReevaluatePumpConnectionConstraints
                };

                foreach (var pid in physicalPumps)
                {
                    groupVm.AvailablePrimaryPumps.Add(pid);
                    var isSelected = groupConfig.ConnectedPumpIds.Contains(pid) && pid != groupConfig.PrimaryPumpId;
                    groupVm.CandidateSlaves.Add(new PumpCandidateVm
                    {
                        PumpId = pid,
                        IsSelected = isSelected,
                        OnSelectionChangedCallback = ReevaluatePumpConnectionConstraints
                    });
                }

                PumpConnectionGroups.Add(groupVm);
            }

            ReevaluatePumpConnectionConstraints();
            PumpConnectionStatusMessage = config.IsEnabled 
                ? $"✅ Loaded configuration: Mode {config.Mode}, {PumpConnectionGroups.Count} group(s)."
                : "Pump connections are currently OFF.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load pump connection configuration");
            PumpConnectionStatusMessage = $"❌ Error loading connections: {ex.Message}";
        }
    }
}



