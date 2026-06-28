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
    private void NavigateToDsmPerformance()
    {
        SelectedNavIndex = 4;
        CurrentView = App.Services.GetRequiredService<DsmPerformanceViewModel>();
    }

    [RelayCommand]
    private void NavigateToExpenseAnalysis()
    {
        SelectedNavIndex = 5;
        CurrentView = App.Services.GetRequiredService<ExpenseAnalysisViewModel>();
    }

    [RelayCommand]
    private void NavigateToMismatchLedger()
    {
        SelectedNavIndex = 6;
        CurrentView = App.Services.GetRequiredService<MismatchLedgerViewModel>();
    }

    [RelayCommand]
    private void NavigateToCollectionSummary()
    {
        SelectedNavIndex = 7;
        CurrentView = App.Services.GetRequiredService<CollectionSummaryViewModel>();
    }

    [RelayCommand]
    private void NavigateToFinancialSummary()
    {
        SelectedNavIndex = 8;
        var debtorVm = App.Services.GetRequiredService<DebtorManagementViewModel>();
        debtorVm.SelectedTabIndex = 2; // Financial Summary tab
        CurrentView = debtorVm;
    }

    [RelayCommand]
    private void NavigateToReports()
    {
        SelectedNavIndex = 9;
        CurrentView = App.Services.GetRequiredService<ReportsViewModel>();
    }

    [RelayCommand]
    private void NavigateToSalaryCalculation()
    {
        SelectedNavIndex = 10;
        CurrentView = App.Services.GetRequiredService<SalaryCalculationViewModel>();
    }

    [RelayCommand]
    private void NavigateToOilDefInventory()
    {
        SelectedNavIndex = 11;
        CurrentView = App.Services.GetRequiredService<OilDefSummaryViewModel>();
    }

    [RelayCommand]
    private void NavigateToOuterExpenses()
    {
        SelectedNavIndex = 12;
        CurrentView = App.Services.GetRequiredService<OuterExpensesViewModel>();
    }

    [RelayCommand]
    private void NavigateToCardSettlement()
    {
        SelectedNavIndex = 13;
        CurrentView = App.Services.GetRequiredService<CardSettlementViewModel>();
    }

    [RelayCommand]
    private void NavigateToDebtorManagement()
    {
        SelectedNavIndex = 14;
        var debtorVm = App.Services.GetRequiredService<DebtorManagementViewModel>();
        debtorVm.SelectedTabIndex = 0; // Debtors Directory tab
        CurrentView = debtorVm;
    }

    [RelayCommand]
    private void NavigateToPumpExpenses()
    {
        SelectedNavIndex = 15;
        CurrentView = App.Services.GetRequiredService<PumpExpensesViewModel>();
    }


    /// <summary>
    /// Called by the sync engine to update status display.
    /// </summary>
    public void UpdateSyncStatus(DateTime lastSync, int pendingCount, bool isConnected, string statusMessage)
    {
        LastSyncTime = lastSync.ToString("dd MMM yyyy\nhh:mm tt");
        PendingSyncCount = pendingCount;
        IsSyncConnected = isConnected;
        SyncStatusText = statusMessage;
    }
}
