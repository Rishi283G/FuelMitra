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

                    var dbSet = (System.Collections.IEnumerable)context.GetType()
                        .GetMethod("Set", new[] { typeof(Type) })!
                        .Invoke(context, new[] { entityType.ClrType })!;

                    var records = new List<object>();
                    foreach (var rec in dbSet)
                    {
                        records.Add(rec);
                    }
                    
                    foreach (var rec in records)
                    {
                        var propInfo = entityType.FindProperty(primaryKey.Name)!.PropertyInfo;
                        if (propInfo == null) continue;
                        
                        var id = Convert.ToInt32(propInfo.GetValue(rec));
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
