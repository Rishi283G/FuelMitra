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

namespace FuelPro.UI.ViewModels;

public partial class DeveloperMainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AuthService _authService;
    private readonly IUserRepository _userRepo;
    private readonly SyncConfigService _syncConfigService;
    private readonly SyncEngine _syncEngine;
    private readonly Rashtra.Licensing.LicenseManager _licenseManager;

    [ObservableProperty] private object? _currentView;
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

    public DeveloperMainWindowViewModel()
    {
        _serviceProvider = App.Services;
        _authService = _serviceProvider.GetRequiredService<AuthService>();
        _userRepo = _serviceProvider.GetRequiredService<IUserRepository>();
        _syncConfigService = _serviceProvider.GetRequiredService<SyncConfigService>();
        _syncEngine = _serviceProvider.GetRequiredService<SyncEngine>();
        _licenseManager = _serviceProvider.GetRequiredService<Rashtra.Licensing.LicenseManager>();

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
            IsConfigLocked = true;
            ConfigStatusMessage = "✅ Cloud settings saved and locked!";
            
            await _syncEngine.ForceSyncAsync();
        }
        catch (Exception ex)
        {
            ConfigStatusMessage = $"❌ Save error: {ex.Message}";
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
                    var lines = File.ReadLines(latestFile).TakeLast(200);
                    SyncLogsText = string.Join(Environment.NewLine, lines);
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
}

public class TableCountDto
{
    public string TableName { get; set; } = "";
    public int Count { get; set; }
}
