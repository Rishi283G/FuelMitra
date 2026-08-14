using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Services;
using FuelPro.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Threading;

namespace FuelPro.UI.ViewModels;

public partial class OwnerMainWindowViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly ISettingsRepository _settingsRepo;

    [ObservableProperty] private object? _currentView;
    [ObservableProperty] private string _windowTitle = "PyroSync — Owner";
    [ObservableProperty] private string _currentDateTime = DateTime.Now.ToString("dd MMM yyyy  hh:mm tt");
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

    private readonly DispatcherTimer _clockTimer;

    public OwnerMainWindowViewModel()
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

        // Default to Dashboard
        NavigateToDashboard();
    }

    private async Task LoadStationNameAsync()
    {
        var result = await _settingsRepo.GetSettingsAsync();
        if (result.Success && result.Data != null)
        {
            StationName = result.Data.StationDisplayName;
            WindowTitle = $"PyroSync — Owner — {StationName}";
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
    private void NavigateToDashboard()
    {
        SelectedNavIndex = 0;
        CurrentView = App.Services.GetRequiredService<OwnerDashboardViewModel>();
    }

    [RelayCommand]
    private void NavigateToDailyPerformance()
    {
        SelectedNavIndex = 1;
        CurrentView = App.Services.GetRequiredService<DailyPerformanceViewModel>();
    }

    [RelayCommand]
    private void NavigateToMonthlyPerformance()
    {
        SelectedNavIndex = 2;
        CurrentView = App.Services.GetRequiredService<MonthlyPerformanceViewModel>();
    }

    [RelayCommand]
    private void NavigateToProfitLoss()
    {
        SelectedNavIndex = 3;
        CurrentView = App.Services.GetRequiredService<ProfitLossViewModel>();
    }

    [RelayCommand]
    private void NavigateToExpenseAnalysis()
    {
        SelectedNavIndex = 4;
        CurrentView = App.Services.GetRequiredService<ExpenseAnalysisViewModel>();
    }

    [RelayCommand]
    private void NavigateToMismatchLedger()
    {
        SelectedNavIndex = 5;
        CurrentView = App.Services.GetRequiredService<MismatchLedgerViewModel>();
    }

    [RelayCommand]
    private void NavigateToCollectionSummary()
    {
        SelectedNavIndex = 6;
        CurrentView = App.Services.GetRequiredService<CollectionSummaryViewModel>();
    }

    [RelayCommand]
    private void NavigateToSalaryCalculation()
    {
        SelectedNavIndex = 7;
        CurrentView = App.Services.GetRequiredService<SalaryCalculationViewModel>();
    }

    [RelayCommand]
    private void NavigateToOilDefInventory()
    {
        SelectedNavIndex = 8;
        CurrentView = App.Services.GetRequiredService<OilDefSummaryViewModel>();
    }

    [RelayCommand]
    private void NavigateToCardSettlement()
    {
        SelectedNavIndex = 9;
        CurrentView = App.Services.GetRequiredService<CardSettlementViewModel>();
    }

    [RelayCommand]
    private void NavigateToDebtorManagement()
    {
        SelectedNavIndex = 10;
        var debtorVm = App.Services.GetRequiredService<DebtorManagementViewModel>();
        debtorVm.SelectedTabIndex = 0; // Debtors Directory tab
        CurrentView = debtorVm;
    }

    [RelayCommand]
    private void NavigateToPumpExpenses()
    {
        SelectedNavIndex = 11;
        CurrentView = App.Services.GetRequiredService<PumpExpensesViewModel>();
    }

    [RelayCommand]
    private void NavigateToPettyCash()
    {
        SelectedNavIndex = 12;
        CurrentView = App.Services.GetRequiredService<PettyCashViewModel>();
    }

    [RelayCommand]
    private void NavigateToDsmPersonalDebtor()
    {
        SelectedNavIndex = 13;
        CurrentView = App.Services.GetRequiredService<DsmPersonalDebtorViewModel>();
    }

    [RelayCommand]
    private void NavigateToReports()
    {
        SelectedNavIndex = 14;
        CurrentView = App.Services.GetRequiredService<ReportsViewModel>();
    }

    partial void OnCurrentViewChanged(object? oldValue, object? newValue)
    {
        if (oldValue is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <summary>
    /// Called by the sync engine to update status display.
    /// </summary>
    public void UpdateSyncStatus(DateTime lastSync, int pendingCount, bool isConnected, string statusMessage)
    {
        PendingSyncCount = pendingCount;
        IsSyncConnected = isConnected;
        
        if (!isConnected)
        {
            var lastSyncStr = lastSync == DateTime.MinValue ? "Never" : lastSync.ToString("dd MMM yyyy hh:mm tt");
            SyncStatusText = $"Offline\nPending Changes: {pendingCount}\nLast Successful Sync:\n{lastSyncStr}\nRetrying Automatically...";
            LastSyncTime = lastSync == DateTime.MinValue ? "Never" : lastSync.ToString("dd MMM yyyy hh:mm tt");
        }
        else
        {
            SyncStatusText = statusMessage;
            LastSyncTime = lastSync == DateTime.MinValue ? "Never" : lastSync.ToString("dd MMM yyyy hh:mm tt");
        }
    }
}
