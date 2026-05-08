using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Services;
using FuelPro.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Threading;

namespace FuelPro.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly ISettingsRepository _settingsRepo;

    [ObservableProperty] private object? _currentView;
    [ObservableProperty] private string _windowTitle = "Fuel Pro";
    [ObservableProperty] private string _currentDateTime = DateTime.Now.ToString("dd MMM yyyy  hh:mm tt");
    [ObservableProperty] private string _dbPath = App.DbPath;
    [ObservableProperty] private string _lastSaveTime = "—";
    [ObservableProperty] private string _stationName = "VKD Petroleum";
    [ObservableProperty] private string _currentUser = "";
    [ObservableProperty] private int _selectedNavIndex;

    private readonly DispatcherTimer _clockTimer;

    public MainWindowViewModel()
    {
        _authService = App.Services.GetRequiredService<AuthService>();
        _settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clockTimer.Tick += (_, _) => CurrentDateTime = DateTime.Now.ToString("dd MMM yyyy  hh:mm tt");
        _clockTimer.Start();

        CurrentUser = _authService.CurrentUser?.Username ?? "";
        _ = LoadStationNameAsync();
    }

    private async Task LoadStationNameAsync()
    {
        var result = await _settingsRepo.GetSettingsAsync();
        if (result.Success && result.Data != null)
        {
            StationName = result.Data.PumpStationName;
            WindowTitle = $"Fuel Pro — {StationName} — {DateTime.Now:dd MMM yyyy}";
        }
    }

    [RelayCommand]
    private void NavigateToDashboard()
    {
        SelectedNavIndex = 0;
        CurrentView = App.Services.GetRequiredService<DashboardViewModel>();
    }

    [RelayCommand]
    private void NavigateToDsmEntry()
    {
        SelectedNavIndex = 1;
        CurrentView = App.Services.GetRequiredService<DsmEntryViewModel>();
    }

    [RelayCommand]
    private void NavigateToFinalCalculation()
    {
        SelectedNavIndex = 2;
        CurrentView = App.Services.GetRequiredService<FinalCalculationViewModel>();
    }

    [RelayCommand]
    private void NavigateToDayTotal()
    {
        SelectedNavIndex = 3;
        CurrentView = App.Services.GetRequiredService<DayTotalViewModel>();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        SelectedNavIndex = 4;
        CurrentView = App.Services.GetRequiredService<SettingsViewModel>();
    }

    [RelayCommand]
    private void NavigateToAgsImport()
    {
        SelectedNavIndex = 5;
        CurrentView = App.Services.GetRequiredService<AgsImportViewModel>();
    }

    public void UpdateLastSaveTime() =>
        LastSaveTime = DateTime.Now.ToString("hh:mm:ss tt");

    public void RefreshTitle() => _ = LoadStationNameAsync();
}
