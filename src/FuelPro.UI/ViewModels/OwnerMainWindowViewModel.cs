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
    private readonly IFeatureToggleService _featureService;

    [ObservableProperty] private object? _currentView;
    [ObservableProperty] private string _windowTitle = "PyroSync — Owner";
    [ObservableProperty] private string _currentDateTime = DateTime.Now.ToString("dd MMM yyyy  hh:mm tt");
    [ObservableProperty] private string _stationName = "Mitali Service Station";
    
    public string LogoSource => App.GetLogoPath(false);
    public string SidebarLogoSource => App.GetLogoPath(true);
    public string HeaderLogoSource => App.GetLogoPath(false);
    [ObservableProperty] private string _currentUser = "";
    [ObservableProperty] private int _selectedNavIndex;

    // Feature Visibility for Owner
    public bool IsDashboardVisible => _featureService.IsFeatureEnabled("Owner_Dashboard", true);
    public bool IsDailyPerformanceVisible => _featureService.IsFeatureEnabled("Owner_DailyPerformance", true);
    public bool IsMonthlyPerformanceVisible => _featureService.IsFeatureEnabled("Owner_MonthlyPerformance", true);
    public bool IsProfitLossVisible => _featureService.IsFeatureEnabled("Owner_ProfitLoss", true);
    public bool IsExpenseAnalysisVisible => _featureService.IsFeatureEnabled("Owner_ExpenseAnalysis", true);
    public bool IsMismatchLedgerVisible => _featureService.IsFeatureEnabled("Owner_MismatchLedger", true);
    public bool IsCollectionSummaryVisible => _featureService.IsFeatureEnabled("Owner_CollectionSummary", true);
    public bool IsSalaryCalculationVisible => _featureService.IsFeatureEnabled("Owner_SalaryCalculation", true);
    public bool IsOilDefInventoryVisible => _featureService.IsFeatureEnabled("Owner_OilDefInventory", true);
    public bool IsCardSettlementVisible => _featureService.IsFeatureEnabled("Owner_CardSettlement", true);
    public bool IsDebtorManagementVisible => _featureService.IsFeatureEnabled("Owner_DebtorManagement", true);
    public bool IsPumpExpensesVisible => _featureService.IsFeatureEnabled("Owner_PumpExpenses", true);
    public bool IsPettyCashVisible => _featureService.IsFeatureEnabled("Owner_PettyCash", true);
    public bool IsDsmPersonalDebtorVisible => _featureService.IsFeatureEnabled("Owner_DsmPersonalDebtor", true);
    public bool IsReportsVisible => _featureService.IsFeatureEnabled("Owner_Reports", true);

    public void NotifyFeaturePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsDashboardVisible));
        OnPropertyChanged(nameof(IsDailyPerformanceVisible));
        OnPropertyChanged(nameof(IsMonthlyPerformanceVisible));
        OnPropertyChanged(nameof(IsProfitLossVisible));
        OnPropertyChanged(nameof(IsExpenseAnalysisVisible));
        OnPropertyChanged(nameof(IsMismatchLedgerVisible));
        OnPropertyChanged(nameof(IsCollectionSummaryVisible));
        OnPropertyChanged(nameof(IsSalaryCalculationVisible));
        OnPropertyChanged(nameof(IsOilDefInventoryVisible));
        OnPropertyChanged(nameof(IsCardSettlementVisible));
        OnPropertyChanged(nameof(IsDebtorManagementVisible));
        OnPropertyChanged(nameof(IsPumpExpensesVisible));
        OnPropertyChanged(nameof(IsPettyCashVisible));
        OnPropertyChanged(nameof(IsDsmPersonalDebtorVisible));
        OnPropertyChanged(nameof(IsReportsVisible));
    }

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
        _featureService = App.Services.GetRequiredService<IFeatureToggleService>();

        _featureService.FeatureConfigurationChanged += () =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(NotifyFeaturePropertiesChanged);
        };

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

    private OwnerDashboardViewModel? _dashboardViewModel;

    [RelayCommand]
    private void NavigateToDashboard()
    {
        if (!IsDashboardVisible) return;
        SelectedNavIndex = 0;
        _dashboardViewModel ??= App.Services.GetRequiredService<OwnerDashboardViewModel>();
        CurrentView = _dashboardViewModel;
    }

    [RelayCommand]
    private void NavigateToDailyPerformance()
    {
        if (!IsDailyPerformanceVisible) return;
        SelectedNavIndex = 1;
        CurrentView = App.Services.GetRequiredService<DailyPerformanceViewModel>();
    }

    [RelayCommand]
    private void NavigateToMonthlyPerformance()
    {
        if (!IsMonthlyPerformanceVisible) return;
        SelectedNavIndex = 2;
        CurrentView = App.Services.GetRequiredService<MonthlyPerformanceViewModel>();
    }

    [RelayCommand]
    private void NavigateToProfitLoss()
    {
        if (!IsProfitLossVisible) return;
        SelectedNavIndex = 3;
        CurrentView = App.Services.GetRequiredService<ProfitLossViewModel>();
    }

    [RelayCommand]
    private void NavigateToExpenseAnalysis()
    {
        if (!IsExpenseAnalysisVisible) return;
        SelectedNavIndex = 4;
        CurrentView = App.Services.GetRequiredService<ExpenseAnalysisViewModel>();
    }

    [RelayCommand]
    private void NavigateToMismatchLedger()
    {
        if (!IsMismatchLedgerVisible) return;
        SelectedNavIndex = 5;
        CurrentView = App.Services.GetRequiredService<MismatchLedgerViewModel>();
    }

    [RelayCommand]
    private void NavigateToCollectionSummary()
    {
        if (!IsCollectionSummaryVisible) return;
        SelectedNavIndex = 6;
        CurrentView = App.Services.GetRequiredService<CollectionSummaryViewModel>();
    }

    [RelayCommand]
    private void NavigateToSalaryCalculation()
    {
        if (!IsSalaryCalculationVisible) return;
        SelectedNavIndex = 7;
        CurrentView = App.Services.GetRequiredService<SalaryCalculationViewModel>();
    }

    [RelayCommand]
    private void NavigateToOilDefInventory()
    {
        if (!IsOilDefInventoryVisible) return;
        SelectedNavIndex = 8;
        CurrentView = App.Services.GetRequiredService<OilDefSummaryViewModel>();
    }

    [RelayCommand]
    private void NavigateToCardSettlement()
    {
        if (!IsCardSettlementVisible) return;
        SelectedNavIndex = 9;
        CurrentView = App.Services.GetRequiredService<CardSettlementViewModel>();
    }

    [RelayCommand]
    private void NavigateToDebtorManagement()
    {
        if (!IsDebtorManagementVisible) return;
        SelectedNavIndex = 10;
        var debtorVm = App.Services.GetRequiredService<DebtorManagementViewModel>();
        debtorVm.SelectedTabIndex = 0; // Debtors Directory tab
        CurrentView = debtorVm;
    }

    [RelayCommand]
    private void NavigateToPumpExpenses()
    {
        if (!IsPumpExpensesVisible) return;
        SelectedNavIndex = 11;
        CurrentView = App.Services.GetRequiredService<PumpExpensesViewModel>();
    }

    [RelayCommand]
    private void NavigateToPettyCash()
    {
        if (!IsPettyCashVisible) return;
        SelectedNavIndex = 12;
        CurrentView = App.Services.GetRequiredService<PettyCashViewModel>();
    }

    [RelayCommand]
    private void NavigateToDsmPersonalDebtor()
    {
        if (!IsDsmPersonalDebtorVisible) return;
        SelectedNavIndex = 13;
        var vm = App.Services.GetRequiredService<DsmPersonalDebtorViewModel>();
        _ = vm.LoadDataAsync();
        CurrentView = vm;
    }

    [RelayCommand]
    private void NavigateToReports()
    {
        if (!IsReportsVisible) return;
        SelectedNavIndex = 14;
        CurrentView = App.Services.GetRequiredService<ReportsViewModel>();
    }


    [RelayCommand]
    public void Logout()
    {
        var confirm = System.Windows.MessageBox.Show("Are you sure you want to log out and switch user?", "Logout", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        _authService.Logout();
        var loginWindow = App.Services.GetRequiredService<Views.LoginView>();
        loginWindow.Show();

        foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
        {
            if (window != loginWindow)
            {
                window.Close();
            }
        }
        System.Windows.Application.Current.MainWindow = loginWindow;
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
