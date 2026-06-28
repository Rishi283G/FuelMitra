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
    [ObservableProperty] private string _windowTitle = "PyroSync";
    [ObservableProperty] private string _currentDateTime = DateTime.Now.ToString("dd MMM yyyy  hh:mm tt");
    [ObservableProperty] private string _dbPath = App.DbPath;
    [ObservableProperty] private string _lastSaveTime = "—";
    [ObservableProperty] private string _stationName = "Shree Mahakaleshwar Petroleum";
    
    public string LogoSource => App.GetLogoPath(false);
    public string SidebarLogoSource => App.GetLogoPath(true);
    public string HeaderLogoSource => App.GetLogoPath(false);
    [ObservableProperty] private string _currentUser = "";
    [ObservableProperty] private int _selectedNavIndex;

    // Sync status
    [ObservableProperty] private string _lastSyncTime = "—";
    [ObservableProperty] private int _pendingSyncCount;
    [ObservableProperty] private string _syncStatusText = "Not Connected";
    [ObservableProperty] private bool _isSyncConnected;

    // DSM status
    [ObservableProperty] private int _pendingDsmCount;

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

        // Wire Cloud Sync Status
        var syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
        syncEngine.SyncStatusChanged += (status) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                UpdateSyncStatus(status.LastSyncTime, status.PendingRecords, status.IsConnected, status.StatusMessage);
            });
        };
        // Initial sync state update
        UpdateSyncStatus(syncEngine.CurrentStatus.LastSyncTime, syncEngine.CurrentStatus.PendingRecords, syncEngine.CurrentStatus.IsConnected, syncEngine.CurrentStatus.StatusMessage);

        // Wire DSM Poller Count
        var pollingService = App.Services.GetRequiredService<FuelPro.Sync.DsmSubmissionPollingService>();
        pollingService.PendingCountChanged += (count) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                PendingDsmCount = count;
            });
        };
        PendingDsmCount = pollingService.CurrentPendingCount;
    }


    private async Task LoadStationNameAsync()
    {
        var result = await _settingsRepo.GetSettingsAsync();
        if (result.Success && result.Data != null)
        {
            StationName = result.Data.StationDisplayName;
            WindowTitle = $"PyroSync — {StationName} — {DateTime.Now:dd MMM yyyy}";
        }
    }

    public void RefreshBranding()
    {
        OnPropertyChanged(nameof(LogoSource));
        OnPropertyChanged(nameof(SidebarLogoSource));
        OnPropertyChanged(nameof(HeaderLogoSource));
        _ = LoadStationNameAsync();
    }

    [RelayCommand]
    private async Task ForceSyncAsync()
    {
        var syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
        await syncEngine.ForceSyncAsync();
    }

    [RelayCommand]
    private void NavigateToDsmEntry()
    {
        SelectedNavIndex = 0;
        CurrentView = App.Services.GetRequiredService<DsmEntryViewModel>();
    }

    [RelayCommand]
    private void NavigateToOilDefDailyLog()
    {
        SelectedNavIndex = 1;
        CurrentView = App.Services.GetRequiredService<OilDefDailyLogViewModel>();
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

    [RelayCommand]
    private void NavigateToDsmApprovalQueue()
    {
        SelectedNavIndex = 6;
        CurrentView = App.Services.GetRequiredService<DsmApprovalQueueViewModel>();
    }

    [RelayCommand]
    private void NavigateToDsmManagement()
    {
        SelectedNavIndex = 7;
        CurrentView = App.Services.GetRequiredService<DsmManagementViewModel>();
    }

    [RelayCommand]
    private void NavigateToCardSettlement()
    {
        SelectedNavIndex = 8;
        CurrentView = App.Services.GetRequiredService<CardSettlementViewModel>();
    }

    [RelayCommand]
    private void NavigateToDebtorManagement()
    {
        SelectedNavIndex = 9;
        var debtorVm = App.Services.GetRequiredService<DebtorManagementViewModel>();
        debtorVm.SelectedTabIndex = 0; // Debtors Directory tab
        CurrentView = debtorVm;
    }

    [RelayCommand]
    private void NavigateToPumpExpenses()
    {
        SelectedNavIndex = 10;
        CurrentView = App.Services.GetRequiredService<PumpExpensesViewModel>();
    }


    public void UpdateLastSaveTime() =>
        LastSaveTime = DateTime.Now.ToString("hh:mm:ss tt");

    public void RefreshTitle() => _ = LoadStationNameAsync();

    public void UpdateSyncStatus(DateTime lastSync, int pendingCount, bool isConnected, string statusMessage)
    {
        LastSyncTime = lastSync.ToString("dd MMM yyyy hh:mm tt");
        PendingSyncCount = pendingCount;
        IsSyncConnected = isConnected;
        SyncStatusText = statusMessage;
    }
}
