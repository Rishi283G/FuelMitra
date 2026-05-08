using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Globalization;

namespace FuelPro.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly AuthService _authService;
    private readonly IUserRepository _userRepo;
    private readonly IDsmProfileRepository _dsmProfileRepo;
    private readonly ICreditorRepository _creditorRepo;

    [ObservableProperty] private string _pumpStationName = "";
    [ObservableProperty] private double _hsdRate;
    [ObservableProperty] private double _msIRate;
    [ObservableProperty] private double _msIIRate;
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

    // DSM Profile Management
    public ObservableCollection<DsmProfile> DsmProfiles { get; } = new();
    [ObservableProperty] private string _newDsmName = "";
    [ObservableProperty] private string _dsmStatusMessage = "";

    // Creditor Management
    public ObservableCollection<Creditor> Creditors { get; } = new();
    [ObservableProperty] private string _newCreditorName = "";
    [ObservableProperty] private string _newCreditorPhone = "";
    [ObservableProperty] private string _creditorStatusMessage = "";

    public string[] RoleOptions { get; } = { "Admin", "Operator" };

    private Setting? _settings;

    public SettingsViewModel()
    {
        _settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();
        _authService = App.Services.GetRequiredService<AuthService>();
        _userRepo = App.Services.GetRequiredService<IUserRepository>();
        _dsmProfileRepo = App.Services.GetRequiredService<IDsmProfileRepository>();
        _creditorRepo = App.Services.GetRequiredService<ICreditorRepository>();
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
    }

    private async Task LoadAsync()
    {
        var result = await _settingsRepo.GetSettingsAsync();
        if (result.Success && result.Data != null)
        {
            _settings = result.Data;
            PumpStationName = _settings.PumpStationName;
            HsdRate = _settings.HsdRate;
            MsIRate = _settings.MsIRate;
            MsIIRate = _settings.MsIIRate;
            LastUpdated = _settings.LastUpdated.ToString("dd MMM yyyy hh:mm tt");
        }

        var usersResult = await _userRepo.GetAllUsersAsync();
        Users.Clear();
        if (usersResult.Success)
            foreach (var u in usersResult.Data!) Users.Add(u);

        var dsmResult = await _dsmProfileRepo.GetAllAsync();
        DsmProfiles.Clear();
        if (dsmResult.Success)
            foreach (var d in dsmResult.Data!) DsmProfiles.Add(d);

        var creditorsResult = await _creditorRepo.GetAllActiveAsync();
        Creditors.Clear();
        if (creditorsResult.Success)
            foreach (var c in creditorsResult.Data!) Creditors.Add(c);
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (_settings == null) return;
        _settings.PumpStationName = PumpStationName;
        _settings.HsdRate = HsdRate;
        _settings.MsIRate = MsIRate;
        _settings.MsIIRate = MsIIRate;

        var result = await _settingsRepo.SaveSettingsAsync(_settings);
        StatusMessage = result.Success ? "✅ Settings saved!" : $"❌ {result.Error}";
        if (result.Success) LastUpdated = DateTime.Now.ToString("dd MMM yyyy hh:mm tt");
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
    private async Task AddDsmProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(NewDsmName))
        {
            DsmStatusMessage = "❌ DSM Name is required";
            return;
        }
        var profile = new DsmProfile { DsmName = NewDsmName.Trim() };
        var result = await _dsmProfileRepo.AddAsync(profile);
        if (result.Success)
        {
            NewDsmName = "";
            DsmStatusMessage = "✅ DSM Profile added!";
            await LoadAsync();
        }
        else DsmStatusMessage = $"❌ {result.Error}";
    }

    [RelayCommand]
    private async Task DeleteDsmProfileAsync(DsmProfile? profile)
    {
        if (profile == null) return;
        var result = await _dsmProfileRepo.DeleteAsync(profile.DsmProfileId);
        if (result.Success) await LoadAsync();
    }

    [RelayCommand]
    private async Task AddCreditorAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCreditorName))
        {
            CreditorStatusMessage = "❌ Creditor Name is required";
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
            NewCreditorName = "";
            NewCreditorPhone = "";
            CreditorStatusMessage = "✅ Creditor added!";
            await LoadAsync();
        }
        else CreditorStatusMessage = $"❌ {result.Error}";
    }

    [RelayCommand]
    private async Task ToggleCreditorActiveAsync(Creditor? creditor)
    {
        if (creditor == null) return;
        var result = await _creditorRepo.UpdateAsync(creditor);
        if (result.Success) await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteCreditorAsync(Creditor? creditor)
    {
        if (creditor == null) return;
        var result = await _creditorRepo.SoftDeleteAsync(creditor.CreditorId);
        if (result.Success) await LoadAsync();
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
}
