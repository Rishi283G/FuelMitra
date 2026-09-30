using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FuelPro.UI.ViewModels;

public class OpeningBalanceDisplayItem
{
    public int OpeningBalanceId { get; set; }
    public string EntityType { get; set; } = string.Empty; // "Debtor" or "DsmLoss"
    public string EntityIdentifier { get; set; } = string.Empty;
    public string EntityDisplayName { get; set; } = string.Empty;
    public int? CreditorId { get; set; }
    public DateTime OpeningDate { get; set; }
    public string OpeningDateFormatted => OpeningDate.ToString("dd-MMM-yyyy");
    public double Amount { get; set; }
    public string AmountFormatted => $"₹{Amount:N2}";
    public string? Notes { get; set; }
    public bool IsActive { get; set; }
    public string StatusText => IsActive ? "Active" : "Inactive";
    public DateTime UpdatedAt { get; set; }
    public string UpdatedAtFormatted => UpdatedAt.ToString("dd-MMM-yyyy HH:mm");
    public bool CanEdit => IsActive;
    public bool CanDeactivate => IsActive;
}

public partial class OpeningBalanceManagementViewModel : ObservableObject
{
    private readonly IOpeningBalanceRepository _openingBalanceRepo;
    private readonly ICreditorRepository _creditorRepo;
    private readonly IDsmProfileRepository _dsmProfileRepo;
    private readonly AuthService _authService;
    private readonly FuelProDbContext? _dbContext;
    private readonly ILogger _logger = Log.ForContext<OpeningBalanceManagementViewModel>();

    [ObservableProperty] private string _selectedTab = "Debtor"; // "Debtor" or "DsmLoss"
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusMessage = "";

    // Form account type selection
    [ObservableProperty] private bool _isDebtorAccountType = true;
    [ObservableProperty] private bool _isDsmAccountType = false;
    [ObservableProperty] private int _selectedTabIndex = 0;

    // Form state
    [ObservableProperty] private bool _isFormOpen;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private int _editingBalanceId;
    [ObservableProperty] private string _formTitle = "Add Historical Opening Balance";
    [ObservableProperty] private DateTime _formOpeningDate = DateTime.Today;
    [ObservableProperty] private string _formAmountText = "";
    [ObservableProperty] private string _formNotes = "";
    [ObservableProperty] private string _validationMessage = "";

    [ObservableProperty] private Creditor? _selectedCreditor;
    [ObservableProperty] private DsmProfile? _selectedDsmProfile;

    public ObservableCollection<OpeningBalanceDisplayItem> DebtorBalances { get; } = new();
    public ObservableCollection<OpeningBalanceDisplayItem> DsmBalances { get; } = new();
    public ObservableCollection<Creditor> DebtorsList { get; } = new();
    public ObservableCollection<DsmProfile> DsmProfilesList { get; } = new();

    public bool CanManageOpeningBalances =>
        _authService.CurrentUser != null &&
        (_authService.CurrentUser.IsOwner || _authService.CurrentUser.IsManager || _authService.CurrentUser.IsDeveloper);

    public bool CanChangeEntityType => !IsEditing;

    partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(CanChangeEntityType));

    partial void OnIsDebtorAccountTypeChanged(bool value)
    {
        if (IsEditing) return; // Strict safeguard: never clear or overwrite entity during edit mode
        if (value)
        {
            if (IsDsmAccountType) IsDsmAccountType = false;
            SelectedTab = "Debtor";
            SelectedDsmProfile = null;
            FormTitle = "Add Debtor Opening Balance";
        }
    }

    partial void OnIsDsmAccountTypeChanged(bool value)
    {
        if (IsEditing) return; // Strict safeguard: never clear or overwrite entity during edit mode
        if (value)
        {
            if (IsDebtorAccountType) IsDebtorAccountType = false;
            SelectedTab = "DsmLoss";
            SelectedCreditor = null;
            FormTitle = "Add DSM Historical Opening Balance";
        }
    }

    partial void OnSelectedTabChanged(string value)
    {
        if (value == "DsmLoss")
        {
            if (!IsDsmAccountType)
            {
                IsDsmAccountType = true;
            }
        }
        else
        {
            if (!IsDebtorAccountType)
            {
                IsDebtorAccountType = true;
            }
        }
    }

    public OpeningBalanceManagementViewModel()
        : this(
            App.Services.GetRequiredService<IOpeningBalanceRepository>(),
            App.Services.GetRequiredService<ICreditorRepository>(),
            App.Services.GetRequiredService<IDsmProfileRepository>(),
            App.Services.GetRequiredService<AuthService>(),
            App.Services.GetService<FuelProDbContext>())
    {
    }

    public OpeningBalanceManagementViewModel(
        IOpeningBalanceRepository openingBalanceRepo,
        ICreditorRepository creditorRepo,
        IDsmProfileRepository dsmProfileRepo,
        AuthService authService,
        FuelProDbContext? dbContext = null)
    {
        _openingBalanceRepo = openingBalanceRepo;
        _creditorRepo = creditorRepo;
        _dsmProfileRepo = dsmProfileRepo;
        _authService = authService;
        _dbContext = dbContext;
    }

    [RelayCommand]
    public async Task LoadDataAsync()
    {
        IsLoading = true;
        StatusMessage = "";
        ValidationMessage = "";
        try
        {
            // 1. Load active creditors for dropdown (same source as Debtor Management)
            var creditorsResult = await _creditorRepo.GetAllActiveAsync();
            DebtorsList.Clear();
            if (creditorsResult.Success && creditorsResult.Data != null)
            {
                foreach (var c in creditorsResult.Data.OrderBy(x => x.Name))
                {
                    DebtorsList.Add(c);
                }
            }

            // 2. Load active DSM accounts using the same authoritative source as DSM Loss Management (DsmPersonalDebtorViewModel)
            DsmProfilesList.Clear();
            var dsmNames = new List<string>();

            if (_dbContext != null)
            {
                // Primary authoritative source: DsmUsers
                var allDsms = await _dbContext.DsmUsers
                    .Where(u => !string.IsNullOrWhiteSpace(u.FullName))
                    .Select(u => u.FullName.Trim())
                    .Distinct()
                    .ToListAsync();

                // Operational fallback: existing accounts in personal debtors
                var existingDebtorDsms = await _dbContext.DsmPersonalDebtors
                    .Where(d => !string.IsNullOrWhiteSpace(d.DsmName))
                    .Select(d => d.DsmName.Trim())
                    .Distinct()
                    .ToListAsync();

                var distinctSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in allDsms)
                {
                    if (distinctSet.Add(name))
                        dsmNames.Add(name);
                }
                foreach (var name in existingDebtorDsms)
                {
                    if (distinctSet.Add(name))
                        dsmNames.Add(name);
                }
            }
            else
            {
                // Fallback for isolated mock repository environments
                var dsmResult = await _dsmProfileRepo.GetAllAsync();
                if (dsmResult.Success && dsmResult.Data != null)
                {
                    dsmNames = dsmResult.Data
                        .Where(d => !string.IsNullOrWhiteSpace(d.DsmName))
                        .Select(d => d.DsmName.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
            }

            foreach (var name in dsmNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                DsmProfilesList.Add(new DsmProfile { DsmName = name });
            }

            // 3. Load debtor opening balances
            var debtorObResult = await _openingBalanceRepo.GetAllByEntityTypeAsync("Debtor");
            DebtorBalances.Clear();
            if (debtorObResult.Success && debtorObResult.Data != null)
            {
                foreach (var ob in debtorObResult.Data.OrderByDescending(x => x.IsActive).ThenBy(x => x.OpeningDate))
                {
                    string displayName = ob.Creditor != null
                        ? ob.Creditor.Name
                        : (DebtorsList.FirstOrDefault(c => c.CreditorId.ToString() == ob.EntityIdentifier)?.Name ?? $"Debtor #{ob.EntityIdentifier}");

                    DebtorBalances.Add(new OpeningBalanceDisplayItem
                    {
                        OpeningBalanceId = ob.OpeningBalanceId,
                        EntityType = ob.EntityType,
                        EntityIdentifier = ob.EntityIdentifier,
                        EntityDisplayName = displayName,
                        CreditorId = ob.CreditorId,
                        OpeningDate = ob.OpeningDate,
                        Amount = ob.Amount,
                        Notes = ob.Notes,
                        IsActive = ob.IsActive,
                        UpdatedAt = ob.UpdatedAt
                    });
                }
            }

            // 4. Load DSM loss opening balances
            var dsmObResult = await _openingBalanceRepo.GetAllByEntityTypeAsync("DsmLoss");
            DsmBalances.Clear();
            if (dsmObResult.Success && dsmObResult.Data != null)
            {
                foreach (var ob in dsmObResult.Data.OrderByDescending(x => x.IsActive).ThenBy(x => x.OpeningDate))
                {
                    DsmBalances.Add(new OpeningBalanceDisplayItem
                    {
                        OpeningBalanceId = ob.OpeningBalanceId,
                        EntityType = ob.EntityType,
                        EntityIdentifier = ob.EntityIdentifier,
                        EntityDisplayName = ob.EntityIdentifier,
                        CreditorId = null,
                        OpeningDate = ob.OpeningDate,
                        Amount = ob.Amount,
                        Notes = ob.Notes,
                        IsActive = ob.IsActive,
                        UpdatedAt = ob.UpdatedAt
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load opening balances");
            StatusMessage = $"Error loading data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SwitchTab(string tab)
    {
        SelectedTab = tab;
        IsFormOpen = false;
        ValidationMessage = "";
    }

    [RelayCommand]
    public void OpenCreateForm()
    {
        if (!CanManageOpeningBalances)
        {
            MessageBox.Show("You do not have permission to manage opening balances.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsEditing = false;
        EditingBalanceId = 0;

        // Default type matching the currently selected tab
        if (SelectedTabIndex == 1 || SelectedTab == "DsmLoss")
        {
            IsDsmAccountType = true;
        }
        else
        {
            IsDebtorAccountType = true;
        }

        FormOpeningDate = DateTime.Today;
        FormAmountText = "";
        FormNotes = "";
        SelectedCreditor = null;
        SelectedDsmProfile = null;
        ValidationMessage = "";
        IsFormOpen = true;
    }

    [RelayCommand]
    public void OpenEditForm(OpeningBalanceDisplayItem? item)
    {
        if (item == null) return;

        if (!CanManageOpeningBalances)
        {
            MessageBox.Show("You do not have permission to edit opening balances.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!item.IsActive)
        {
            MessageBox.Show("Inactive opening balances cannot be edited.", "Action Not Allowed", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsEditing = true;
        EditingBalanceId = item.OpeningBalanceId;
        FormTitle = item.EntityType == "Debtor" ? $"Edit Opening Balance: {item.EntityDisplayName}" : $"Edit Historical Opening Loss: {item.EntityDisplayName}";
        FormOpeningDate = item.OpeningDate;
        FormAmountText = item.Amount.ToString("0.##", CultureInfo.InvariantCulture);
        FormNotes = item.Notes ?? "";
        ValidationMessage = "";

        if (item.EntityType == "Debtor")
        {
            IsDebtorAccountType = true;
            IsDsmAccountType = false;
            SelectedTab = "Debtor";
            SelectedCreditor = DebtorsList.FirstOrDefault(c => c.CreditorId.ToString() == item.EntityIdentifier);
            SelectedDsmProfile = null;
        }
        else
        {
            IsDsmAccountType = true;
            IsDebtorAccountType = false;
            SelectedTab = "DsmLoss";
            SelectedDsmProfile = DsmProfilesList.FirstOrDefault(d => d.DsmName.Equals(item.EntityIdentifier, StringComparison.OrdinalIgnoreCase))
                ?? new DsmProfile { DsmName = item.EntityIdentifier };
            SelectedCreditor = null;
        }

        IsFormOpen = true;
    }

    [RelayCommand]
    public async Task SaveFormAsync()
    {
        ValidationMessage = "";

        if (!CanManageOpeningBalances)
        {
            ValidationMessage = "You do not have permission to manage opening balances.";
            return;
        }

        // 1. Validate entity selection
        string entityType = SelectedTab;
        string entityIdentifier = "";
        int? creditorId = null;
        string entityDisplayName = "";

        if (entityType == "Debtor")
        {
            if (SelectedCreditor == null)
            {
                ValidationMessage = "Please select a debtor.";
                return;
            }
            entityIdentifier = SelectedCreditor.CreditorId.ToString();
            creditorId = SelectedCreditor.CreditorId;
            entityDisplayName = SelectedCreditor.Name;
        }
        else
        {
            if (SelectedDsmProfile == null || string.IsNullOrWhiteSpace(SelectedDsmProfile.DsmName))
            {
                ValidationMessage = "Please select a DSM staff member.";
                return;
            }
            entityIdentifier = SelectedDsmProfile.DsmName.Trim();
            entityDisplayName = entityIdentifier;
        }

        // 2. Validate amount
        if (!double.TryParse(FormAmountText, NumberStyles.Any, CultureInfo.InvariantCulture, out double amount) &&
            !double.TryParse(FormAmountText, NumberStyles.Any, CultureInfo.CurrentCulture, out amount))
        {
            ValidationMessage = "Please enter a valid numeric amount.";
            return;
        }

        if (amount <= 0)
        {
            ValidationMessage = "Opening amount must be greater than zero.";
            return;
        }

        // 3. Prevent duplicate active records on create
        if (!IsEditing)
        {
            var existingActive = await _openingBalanceRepo.GetActiveByEntityAsync(entityType, entityIdentifier);
            if (existingActive.Success && existingActive.Data != null)
            {
                ValidationMessage = $"An active opening balance already exists for {entityDisplayName}. Please edit or deactivate the existing record.";
                return;
            }
        }

        // 4. Confirmation dialog for edit
        if (IsEditing)
        {
            bool isTestEnv = Environment.GetEnvironmentVariable("FUELPRO_ENV") == "TEST";
            if (!isTestEnv)
            {
                var confirmResult = MessageBox.Show(
                    $"Are you sure you want to update the historical opening balance for {entityDisplayName}?\n\n" +
                    $"New Amount: ₹{amount:N2}\n" +
                    $"New Date: {FormOpeningDate:dd-MMM-yyyy}\n\n" +
                    "Note: Editing an opening balance modifies the historical starting position and does NOT alter operational transactions.",
                    "Confirm Opening Balance Update",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirmResult != MessageBoxResult.Yes)
                {
                    return;
                }
            }
        }

        try
        {
            var ob = new OpeningBalance
            {
                OpeningBalanceId = IsEditing ? EditingBalanceId : 0,
                EntityType = entityType,
                EntityIdentifier = entityIdentifier,
                CreditorId = creditorId,
                OpeningDate = FormOpeningDate.Date,
                Amount = amount,
                Notes = string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes.Trim(),
                IsActive = true,
                CreatedBy = _authService.CurrentUser?.Username ?? "User"
            };

            var saveResult = await _openingBalanceRepo.SaveOpeningBalanceAsync(ob);
            if (!saveResult.Success)
            {
                ValidationMessage = $"Failed to save: {saveResult.Error}";
                return;
            }

            IsFormOpen = false;
            await LoadDataAsync();
            StatusMessage = IsEditing
                ? $"Opening balance for {entityDisplayName} updated successfully."
                : $"Opening balance for {entityDisplayName} created successfully.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save opening balance");
            ValidationMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task DeactivateAsync(OpeningBalanceDisplayItem? item)
    {
        if (item == null) return;

        bool isTestEnv = Environment.GetEnvironmentVariable("FUELPRO_ENV") == "TEST";

        if (!CanManageOpeningBalances)
        {
            if (!isTestEnv)
            {
                MessageBox.Show("You do not have permission to deactivate opening balances.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            StatusMessage = "Access Denied: You do not have permission to deactivate opening balances.";
            return;
        }

        if (!item.IsActive)
        {
            if (!isTestEnv)
            {
                MessageBox.Show("This opening balance is already inactive.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        if (!isTestEnv)
        {
            var confirmResult = MessageBox.Show(
                $"Deactivate this historical opening balance for {item.EntityDisplayName} ({item.AmountFormatted})?\n\n" +
                "Existing repayment history will be preserved. The opening balance will contribute ₹0 to outstanding calculations.\n\n" +
                "Note: Opening balances cannot be permanently deleted, only deactivated.",
                "Confirm Deactivation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            var result = await _openingBalanceRepo.DeactivateOpeningBalanceAsync(item.OpeningBalanceId);
            if (!result.Success)
            {
                if (!isTestEnv)
                {
                    MessageBox.Show($"Deactivation failed: {result.Error}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                StatusMessage = $"Deactivation failed: {result.Error}";
                return;
            }

            await LoadDataAsync();
            StatusMessage = $"Opening balance for {item.EntityDisplayName} has been deactivated.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to deactivate opening balance");
            MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CancelForm()
    {
        IsFormOpen = false;
        ValidationMessage = "";
    }
}
