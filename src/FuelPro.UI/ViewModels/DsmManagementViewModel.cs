using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FuelPro.UI.ViewModels;

/// <summary>Editable row used in the Nozzle Config tab UI.</summary>
public partial class EditableNozzleRow : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private int _nozzleId;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string _fuelType = "MS-I";
    public int SortOrder { get; set; }
}

public partial class DsmManagementViewModel : ObservableObject
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DsmAuthAdminService _authAdminService;
    private readonly ILogger _logger = Log.ForContext<DsmManagementViewModel>();

    // DSM Accounts
    public ObservableCollection<DsmUser> DsmUsers { get; } = new();
    [ObservableProperty] private string _newEmployeeCode = "";
    [ObservableProperty] private string _newFullName = "";
    [ObservableProperty] private string _newEmail = "";
    [ObservableProperty] private string _newMobileNumber = "";
    [ObservableProperty] private string _newPassword = "";
    [ObservableProperty] private string _accountStatusMessage = "";
    [ObservableProperty] private bool _isCreatingAccount;

    // Pump Assignments
    public ObservableCollection<DsmPumpAssignment> PumpAssignments { get; } = new();
    public ObservableCollection<DsmUser> ActiveDsmOptions { get; } = new();
    public ObservableCollection<int> PumpOptions { get; } = new();
    public string[] ShiftOptions { get; } = { "A", "B" };

    [ObservableProperty] private DsmUser? _selectedDsmUser;
    [ObservableProperty] private int? _selectedPumpId;
    [ObservableProperty] private int? _selectedConnectedPumpId;
    [ObservableProperty] private string _selectedShift = "A";
    [ObservableProperty] private string _assignmentStatusMessage = "";

    // Device Registrations
    public ObservableCollection<DsmDevice> DsmDevices { get; } = new();
    [ObservableProperty] private string _deviceStatusMessage = "";

    // Nozzle Config (PWA)
    private readonly SupabaseDsmService _supabaseDsmService;
    public ObservableCollection<PumpNozzleConfigDto> PumpNozzleConfigs { get; } = new();
    public ObservableCollection<EditableNozzleRow> EditableNozzles { get; } = new();
    public string[] FuelTypeOptions { get; } = { "MS-I", "MS-II", "HSD" };
    [ObservableProperty] private int? _nozzleConfigPumpId;
    [ObservableProperty] private string _nozzleConfigStatusMessage = "";

    public DsmManagementViewModel()
    {
        _serviceProvider = App.Services;
        _authAdminService = _serviceProvider.GetRequiredService<DsmAuthAdminService>();
        _supabaseDsmService = _serviceProvider.GetRequiredService<SupabaseDsmService>();

        // Pump options (e.g. Pump 1 to 6)
        for (int i = 1; i <= 6; i++) PumpOptions.Add(i);

        _ = LoadDataAsync();
        _ = LoadAllNozzleConfigsAsync();
    }

    [ObservableProperty] private bool _showCompletedAssignments;

    partial void OnShowCompletedAssignmentsChanged(bool value)
    {
        _ = LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        try
        {
            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();

            // Load DsmUsers
            var users = await context.DsmUsers.OrderBy(u => u.FullName).ToListAsync();
            DsmUsers.Clear();
            ActiveDsmOptions.Clear();
            foreach (var u in users)
            {
                DsmUsers.Add(u);
                if (u.IsActive) ActiveDsmOptions.Add(u);
            }

            // Load Assignments
            var query = context.DsmPumpAssignments
                .Include(a => a.DsmUser)
                .AsQueryable();

            if (!ShowCompletedAssignments)
            {
                query = query.Where(a => a.IsActive);
            }

            var assignments = await query
                .OrderByDescending(a => a.AssignedDate)
                .ToListAsync();

            PumpAssignments.Clear();
            foreach (var a in assignments)
            {
                PumpAssignments.Add(a);
            }

            // Load Device Logs
            var devices = await context.DsmDevices
                .Include(d => d.DsmUser)
                .OrderByDescending(d => d.LastSeen)
                .ToListAsync();
            DsmDevices.Clear();
            foreach (var d in devices)
            {
                DsmDevices.Add(d);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load DSM management data");
            AccountStatusMessage = "❌ Error loading database data.";
        }
    }

    [RelayCommand]
    private async Task CreateAccountAsync()
    {
        if (string.IsNullOrWhiteSpace(NewEmployeeCode) || 
            string.IsNullOrWhiteSpace(NewFullName) || 
            string.IsNullOrWhiteSpace(NewEmail) || 
            string.IsNullOrWhiteSpace(NewMobileNumber) || 
            string.IsNullOrWhiteSpace(NewPassword))
        {
            AccountStatusMessage = "❌ All fields are required.";
            return;
        }

        if (!NewEmail.Contains("@") || NewEmail.Trim().Length < 5)
        {
            AccountStatusMessage = "❌ Invalid email address.";
            return;
        }

        if (NewMobileNumber.Trim().Length < 10)
        {
            AccountStatusMessage = "❌ Invalid mobile number (min 10 digits).";
            return;
        }

        if (NewPassword.Trim().Length < 6)
        {
            AccountStatusMessage = "❌ Password must be at least 6 characters.";
            return;
        }

        IsCreatingAccount = true;
        AccountStatusMessage = "⏳ Provisioning Supabase Auth account...";

        try
        {
            // 1. Provision user via Admin API
            var authResult = await _authAdminService.CreateDsmAuthUserAsync(
                NewEmail.Trim(),
                NewMobileNumber.Trim(),
                NewPassword.Trim(),
                NewFullName.Trim(),
                NewEmployeeCode.Trim()
            );

            if (!authResult.Success)
            {
                AccountStatusMessage = $"❌ {authResult.Error}";
                return;
            }

            string authUserId = authResult.Data!;

            // 2. Save DsmUser in local SQLite
            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
            
            // Check for local duplicate mobile number
            var dupMobile = await context.DsmUsers.FirstOrDefaultAsync(u => u.MobileNumber == NewMobileNumber.Trim());
            if (dupMobile != null)
            {
                AccountStatusMessage = "❌ A user with this mobile number already exists locally.";
                return;
            }

            // Check for local duplicate email
            var dupEmail = await context.DsmUsers.FirstOrDefaultAsync(u => u.Email == NewEmail.Trim());
            if (dupEmail != null)
            {
                AccountStatusMessage = "❌ A user with this email address already exists locally.";
                return;
            }

            var dsmUser = new DsmUser
            {
                EmployeeCode = NewEmployeeCode.Trim(),
                FullName = NewFullName.Trim(),
                Email = NewEmail.Trim(),
                MobileNumber = NewMobileNumber.Trim(),
                AuthUserId = authUserId,
                IsActive = true,
                CreatedAt = DateTime.Now
            };

            context.DsmUsers.Add(dsmUser);
            await context.SaveChangesAsync();

            // Clear inputs
            NewEmployeeCode = "";
            NewFullName = "";
            NewEmail = "";
            NewMobileNumber = "";
            NewPassword = "";

            AccountStatusMessage = $"✅ Account created! Share password with {dsmUser.FullName}.";
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create DSM user profile locally");
            AccountStatusMessage = $"❌ Local save failed: {ex.Message}";
        }
        finally
        {
            IsCreatingAccount = false;
        }
    }

    [RelayCommand]
    private async Task ToggleUserActiveAsync(DsmUser? user)
    {
        if (user == null) return;

        try
        {
            AccountStatusMessage = "⏳ Updating account status...";
            var nextActiveState = !user.IsActive;

            // 1. Update in Supabase Auth
            var authResult = await _authAdminService.ToggleDsmAuthUserActiveAsync(user.AuthUserId, nextActiveState);
            if (!authResult.Success)
            {
                AccountStatusMessage = $"❌ Auth update failed: {authResult.Error}";
                return;
            }

            // 2. Update local SQLite DB
            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
            var dbUser = await context.DsmUsers.FirstOrDefaultAsync(u => u.DsmUserId == user.DsmUserId);
            if (dbUser != null)
            {
                dbUser.IsActive = nextActiveState;
                context.Entry(dbUser).State = EntityState.Modified;
                await context.SaveChangesAsync();
            }

            AccountStatusMessage = $"✅ Account active state updated to: {nextActiveState}";
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to toggle DSM user active status");
            AccountStatusMessage = $"❌ Update failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreateAssignmentAsync()
    {
        if (SelectedDsmUser == null)
        {
            AssignmentStatusMessage = "❌ Please select a DSM user.";
            return;
        }

        if (SelectedPumpId == null)
        {
            AssignmentStatusMessage = "❌ Please select a Pump.";
            return;
        }

        if (string.IsNullOrEmpty(SelectedShift))
        {
            AssignmentStatusMessage = "❌ Please select a Shift.";
            return;
        }

        try
        {
            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();

            // Validation Rule: One pump can only have one active DSM per Shift
            var activeAssignment = await context.DsmPumpAssignments
                .Include(a => a.DsmUser)
                .FirstOrDefaultAsync(a => a.PumpId == SelectedPumpId.Value && 
                                          a.ShiftType == SelectedShift && 
                                          a.IsActive);

            if (activeAssignment != null)
            {
                // Deactivate the old active assignment to ensure only one active DSM exists at a time
                activeAssignment.IsActive = false;
                context.Entry(activeAssignment).State = EntityState.Modified;
                _logger.Information("Deactivating previous active assignment of {DsmName} on Pump {PumpId} Shift {Shift}",
                    activeAssignment.DsmUser?.FullName, activeAssignment.PumpId, activeAssignment.ShiftType);
            }

            var assignment = new DsmPumpAssignment
            {
                DsmUserId = SelectedDsmUser.DsmUserId,
                PumpId = SelectedPumpId.Value,
                ConnectedPumpId = SelectedConnectedPumpId,
                ShiftType = SelectedShift,
                IsActive = true,
                AssignedDate = DateTime.Now
            };

            context.DsmPumpAssignments.Add(assignment);
            await context.SaveChangesAsync();

            // Reset selection fields
            SelectedConnectedPumpId = null;

            AssignmentStatusMessage = "✅ Assignment saved successfully.";
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create pump assignment");
            AssignmentStatusMessage = $"❌ Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeactivateAssignmentAsync(DsmPumpAssignment? assignment)
    {
        if (assignment == null) return;

        try
        {
            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
            var dbAssignment = await context.DsmPumpAssignments.FirstOrDefaultAsync(a => a.DsmPumpAssignmentId == assignment.DsmPumpAssignmentId);
            if (dbAssignment != null)
            {
                dbAssignment.IsActive = false;
                context.Entry(dbAssignment).State = EntityState.Modified;
                await context.SaveChangesAsync();
            }

            AssignmentStatusMessage = "✅ Assignment deactivated.";
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to deactivate assignment");
            AssignmentStatusMessage = $"❌ Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteUserAsync(DsmUser? user)
    {
        if (user == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to permanently delete DSM: {user.FullName}?\nThis will delete their local user record, active assignments, registered devices, and attendance, and delete their account from Supabase Auth.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            AccountStatusMessage = "⏳ Deleting Supabase Auth account...";
            
            // 1. Delete from Supabase Auth
            var authResult = await _authAdminService.DeleteDsmAuthUserAsync(user.AuthUserId);
            if (!authResult.Success)
            {
                var localResult = MessageBox.Show(
                    $"Supabase deletion failed: {authResult.Error}\nDo you still want to delete the user profile locally?",
                    "Supabase Deletion Failed",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (localResult != MessageBoxResult.Yes)
                {
                    AccountStatusMessage = $"❌ Deletion cancelled: {authResult.Error}";
                    return;
                }
            }

            // 2. Delete local records
            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();

            var assignments = await context.DsmPumpAssignments.Where(a => a.DsmUserId == user.DsmUserId).ToListAsync();
            context.DsmPumpAssignments.RemoveRange(assignments);

            var devices = await context.DsmDevices.Where(d => d.DsmUserId == user.DsmUserId).ToListAsync();
            context.DsmDevices.RemoveRange(devices);

            var attendance = await context.DsmAttendance.Where(a => a.DsmUserId == user.DsmUserId).ToListAsync();
            context.DsmAttendance.RemoveRange(attendance);

            var dbUser = await context.DsmUsers.FirstOrDefaultAsync(u => u.DsmUserId == user.DsmUserId);
            if (dbUser != null)
            {
                context.DsmUsers.Remove(dbUser);
            }
            await context.SaveChangesAsync();

            AccountStatusMessage = $"✅ Successfully deleted user {user.FullName}.";
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete DSM user");
            AccountStatusMessage = $"❌ Delete failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeactivateDeviceAsync(DsmDevice? device)
    {
        if (device == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to deactivate and log out device: {device.DeviceName} for DSM {device.DsmUser?.FullName}?",
            "Deactivate Device",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
            var dbDevice = await context.DsmDevices.FirstOrDefaultAsync(d => d.DsmDeviceId == device.DsmDeviceId);
            if (dbDevice != null)
            {
                dbDevice.IsActive = false;
                context.Entry(dbDevice).State = EntityState.Modified;
                await context.SaveChangesAsync();
            }

            DeviceStatusMessage = "✅ Device deactivated successfully.";
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to deactivate device");
            DeviceStatusMessage = $"❌ Error: {ex.Message}";
        }
    }

    // ── Nozzle Config Commands ──────────────────────────────────────────────

    private async Task LoadAllNozzleConfigsAsync()
    {
        try
        {
            var settings = await _serviceProvider.GetRequiredService<SyncConfigService>().GetSettingsAsync();
            if (string.IsNullOrEmpty(settings.StationId)) return;

            var result = await _supabaseDsmService.FetchPumpNozzleConfigAsync(settings.StationId);
            if (result.Success && result.Data != null)
            {
                PumpNozzleConfigs.Clear();
                foreach (var cfg in result.Data) PumpNozzleConfigs.Add(cfg);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load all nozzle configs from Supabase");
        }
    }

    [RelayCommand]
    private async Task LoadNozzleConfigAsync()
    {
        if (NozzleConfigPumpId == null)
        {
            NozzleConfigStatusMessage = "❌ Please select a pump first.";
            return;
        }

        NozzleConfigStatusMessage = "⏳ Loading from Supabase...";
        try
        {
            var settings = await _serviceProvider.GetRequiredService<SyncConfigService>().GetSettingsAsync();
            var result = await _supabaseDsmService.FetchPumpNozzleConfigAsync(settings.StationId);

            EditableNozzles.Clear();
            if (result.Success && result.Data != null)
            {
                var pumpRows = result.Data
                    .Where(c => c.PumpId == NozzleConfigPumpId.Value)
                    .OrderBy(c => c.SortOrder);

                foreach (var row in pumpRows)
                {
                    EditableNozzles.Add(new EditableNozzleRow
                    {
                        NozzleId = row.NozzleId,
                        FuelType = row.FuelType,
                        SortOrder = row.SortOrder
                    });
                }

                NozzleConfigStatusMessage = EditableNozzles.Count > 0
                    ? $"✅ Loaded {EditableNozzles.Count} nozzle(s) for Pump {NozzleConfigPumpId}."
                    : $"ℹ️ No nozzle config found for Pump {NozzleConfigPumpId}. Add rows below.";
            }
            else
            {
                NozzleConfigStatusMessage = $"⚠️ Could not load: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load nozzle config for Pump {PumpId}", NozzleConfigPumpId);
            NozzleConfigStatusMessage = $"❌ Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddNozzleRow()
    {
        var nextId = EditableNozzles.Count > 0 ? EditableNozzles.Max(r => r.NozzleId) + 1 : 1;
        EditableNozzles.Add(new EditableNozzleRow
        {
            NozzleId = nextId,
            FuelType = "MS-I",
            SortOrder = EditableNozzles.Count + 1
        });
    }

    [RelayCommand]
    private void RemoveNozzleRow(EditableNozzleRow? row)
    {
        if (row != null) EditableNozzles.Remove(row);
    }

    [RelayCommand]
    private async Task SaveNozzleConfigAsync()
    {
        if (NozzleConfigPumpId == null)
        {
            NozzleConfigStatusMessage = "❌ Please select a pump first.";
            return;
        }

        // Validate: all nozzle IDs must be positive integers
        if (EditableNozzles.Any(r => r.NozzleId <= 0))
        {
            NozzleConfigStatusMessage = "❌ All Nozzle IDs must be positive numbers.";
            return;
        }

        if (EditableNozzles.Any(r => string.IsNullOrWhiteSpace(r.FuelType)))
        {
            NozzleConfigStatusMessage = "❌ All nozzles must have a Fuel Type selected.";
            return;
        }

        NozzleConfigStatusMessage = "⏳ Saving to Supabase...";
        try
        {
            var settings = await _serviceProvider.GetRequiredService<SyncConfigService>().GetSettingsAsync();

            var nozzleList = EditableNozzles
                .Select((r, i) => (r.NozzleId, r.FuelType, SortOrder: i + 1))
                .ToList();

            var result = await _supabaseDsmService.SavePumpNozzleConfigAsync(
                settings.StationId, NozzleConfigPumpId.Value, nozzleList);

            if (result.Success)
            {
                NozzleConfigStatusMessage = $"✅ Pump {NozzleConfigPumpId} nozzle config saved! DSM app will use this next time.";
                await LoadAllNozzleConfigsAsync();
            }
            else
            {
                NozzleConfigStatusMessage = $"❌ {result.Error}";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save nozzle config for Pump {PumpId}", NozzleConfigPumpId);
            NozzleConfigStatusMessage = $"❌ Error: {ex.Message}";
        }
    }
}
