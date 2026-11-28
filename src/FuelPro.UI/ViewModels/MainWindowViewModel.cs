using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Threading;

namespace FuelPro.UI.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly AuthService _authService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IFeatureToggleService _featureService;

    [ObservableProperty] private object? _currentView;
    [ObservableProperty] private string _windowTitle = "PyroSync";
    [ObservableProperty] private string _currentDateTime = DateTime.Now.ToString("dd MMM yyyy  hh:mm tt");
    [ObservableProperty] private string _dbPath = App.DbPath;
    [ObservableProperty] private string _lastSaveTime = "—";
    [ObservableProperty] private string _stationName = "Mitali Service Station";
    
    public string LogoSource => App.GetLogoPath(false);
    public string SidebarLogoSource => App.GetLogoPath(true);
    public string HeaderLogoSource => App.GetLogoPath(false);
    [ObservableProperty] private string _currentUser = "";
    [ObservableProperty] private int _selectedNavIndex;

    // Feature Visibility
    public bool IsDsmEntryVisible => _featureService.IsFeatureEnabled("Admin_DsmEntry", true);
    public bool IsOilDefVisible => _featureService.IsFeatureEnabled("Admin_OilDefDailyLog", true);
    public bool IsFinalCalculationVisible => _featureService.IsFeatureEnabled("Admin_FinalCalculation", true);
    public bool IsDayTotalVisible => _featureService.IsFeatureEnabled("Admin_DayTotal", true);
    public bool IsDebtorManagementVisible => _featureService.IsFeatureEnabled("Admin_DebtorManagement", true);
    public bool IsDebtorManagementAsNewPage => _featureService.IsFeatureEnabled("UI_DebtorManagement_AsNewPage", true);
    public bool IsDebtorManagementStandaloneVisible => IsDebtorManagementVisible && IsDebtorManagementAsNewPage;
    public bool IsDsmPersonalDebtorVisible => _featureService.IsFeatureEnabled("Admin_DsmPersonalDebtor", true);
    public bool IsDsmApprovalQueueVisible => _featureService.IsFeatureEnabled("Admin_DsmApprovalQueue", true);
    public bool IsDsmManagementVisible => _featureService.IsFeatureEnabled("Admin_DsmManagement", true);
    public bool IsCardSettlementVisible => _featureService.IsFeatureEnabled("Admin_CardSettlement", true);
    public bool IsAgsImportVisible => _featureService.IsFeatureEnabled("Admin_AgsImport", true);
    public bool IsPettyCashVisible => _featureService.IsFeatureEnabled("Admin_PettyCash", true);
    public bool IsFuelTankerVisible => _featureService.IsFeatureEnabled("Admin_FuelTankerEntry", true);
    public bool IsTankStockVisible => _featureService.IsFeatureEnabled("Admin_TankStockHistory", true);
    public bool IsSettingsVisible => _featureService.IsFeatureEnabled("Admin_Settings", true);

    public void NotifyFeaturePropertiesChanged()
    {
        OnPropertyChanged(nameof(IsDsmEntryVisible));
        OnPropertyChanged(nameof(IsOilDefVisible));
        OnPropertyChanged(nameof(IsFinalCalculationVisible));
        OnPropertyChanged(nameof(IsDayTotalVisible));
        OnPropertyChanged(nameof(IsDebtorManagementVisible));
        OnPropertyChanged(nameof(IsDebtorManagementAsNewPage));
        OnPropertyChanged(nameof(IsDebtorManagementStandaloneVisible));
        OnPropertyChanged(nameof(IsDsmPersonalDebtorVisible));
        OnPropertyChanged(nameof(IsDsmApprovalQueueVisible));
        OnPropertyChanged(nameof(IsDsmManagementVisible));
        OnPropertyChanged(nameof(IsCardSettlementVisible));
        OnPropertyChanged(nameof(IsAgsImportVisible));
        OnPropertyChanged(nameof(IsPettyCashVisible));
        OnPropertyChanged(nameof(IsFuelTankerVisible));
        OnPropertyChanged(nameof(IsTankStockVisible));
        OnPropertyChanged(nameof(IsSettingsVisible));
    }

    // Sync status
    [ObservableProperty] private string _lastSyncTime = "—";
    [ObservableProperty] private int _pendingSyncCount;
    [ObservableProperty] private string _syncStatusText = "Not Connected";
    [ObservableProperty] private string _syncStatusToolTip = "Sync Status: Not Connected";
    [ObservableProperty] private bool _isSyncConnected;

    // DSM status
    [ObservableProperty] private int _pendingDsmCount;
    [ObservableProperty] private bool _hasAuthoritativePendingCount = true;
    [ObservableProperty] private string _pendingDsmTooltip = "DSM Approval Queue";

    private readonly DispatcherTimer _clockTimer;
    private readonly FuelPro.Sync.SyncEngine _syncEngine;
    private readonly FuelPro.Sync.DsmSubmissionPollingService _pollingService;
    private readonly Action _featureChangedHandler;
    private readonly Action<FuelPro.Sync.SyncStatusInfo> _syncStatusChangedHandler;
    private readonly Action<int> _pendingCountChangedHandler;
    private readonly Action _pollStateChangedHandler;
    private bool _disposed;

    private static readonly bool _isTestHost = AppDomain.CurrentDomain.GetAssemblies()
        .Any(a => string.Equals(a.GetName().Name, "FuelPro.Tests", StringComparison.OrdinalIgnoreCase));

    public static void DispatchOnUiThread(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (!_isTestHost && dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    public MainWindowViewModel()
    {
        _authService = App.Services.GetRequiredService<AuthService>();
        _settingsRepo = App.Services.GetRequiredService<ISettingsRepository>();
        _featureService = App.Services.GetRequiredService<IFeatureToggleService>();

        _featureChangedHandler = () =>
        {
            DispatchOnUiThread(NotifyFeaturePropertiesChanged);
        };
        _featureService.FeatureConfigurationChanged += _featureChangedHandler;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clockTimer.Tick += (_, _) => CurrentDateTime = DateTime.Now.ToString("dd MMM yyyy  hh:mm tt");
        _clockTimer.Start();

        CurrentUser = _authService.CurrentUser?.Username ?? "";
        _ = LoadStationNameAsync();

        // Wire Cloud Sync Status
        _syncEngine = App.Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
        _syncStatusChangedHandler = (status) =>
        {
            DispatchOnUiThread(() =>
            {
                UpdateSyncStatus(status.LastSyncTime, status.PendingRecords, status.IsConnected, status.StatusMessage);
            });
        };
        _syncEngine.SyncStatusChanged += _syncStatusChangedHandler;
        // Initial sync state update
        UpdateSyncStatus(_syncEngine.CurrentStatus.LastSyncTime, _syncEngine.CurrentStatus.PendingRecords, _syncEngine.CurrentStatus.IsConnected, _syncEngine.CurrentStatus.StatusMessage);

        // Wire DSM Poller Count safely (guarded against null Application.Current during headless tests)
        _pollingService = App.Services.GetRequiredService<FuelPro.Sync.DsmSubmissionPollingService>();

        void ApplyPollState()
        {
            PendingDsmCount = _pollingService.CurrentPendingCount;
            HasAuthoritativePendingCount = _pollingService.HasAuthoritativeCount;
            PendingDsmTooltip = !_pollingService.HasAuthoritativeCount && !string.IsNullOrWhiteSpace(_pollingService.LastError)
                ? $"DSM Approval Queue (Offline / Refresh Failed: {_pollingService.LastError})"
                : (_pollingService.CurrentPendingCount > 0
                    ? $"DSM Approval Queue ({_pollingService.CurrentPendingCount} pending)"
                    : "DSM Approval Queue");
        }

        _pendingCountChangedHandler = (count) => DispatchOnUiThread(() => PendingDsmCount = count);
        _pollStateChangedHandler = () => DispatchOnUiThread(ApplyPollState);
        _pollingService.PendingCountChanged += _pendingCountChangedHandler;
        _pollingService.PollStateChanged += _pollStateChangedHandler;
        ApplyPollState();
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

    private readonly Dictionary<Type, object> _cachedViews = new();

    public void SetCachedView<T>(T view) where T : class
    {
        var type = typeof(T);
        _cachedViews[type] = view;
    }

    private T GetOrCreateView<T>() where T : class
    {
        var type = typeof(T);
        if (_cachedViews.TryGetValue(type, out var cached))
        {
            return (T)cached;
        }
        var created = App.Services.GetRequiredService<T>();
        _cachedViews[type] = created;
        return created;
    }

    [RelayCommand]
    private void NavigateToDsmEntry()
    {
        if (!IsDsmEntryVisible) return;
        SelectedNavIndex = 0;
        bool wasCached = _cachedViews.ContainsKey(typeof(DsmEntryViewModel));
        var dsmVm = GetOrCreateView<DsmEntryViewModel>();
        if (dsmVm.IsSaved || dsmVm.EditingEntryId.HasValue)
        {
            dsmVm.ClearForm();
        }
        else if (wasCached)
        {
            _ = dsmVm.RefreshOnNavigateAsync();
        }
        _ = dsmVm.LoadSuggestionsAsync();
        CurrentView = dsmVm;
    }

    [RelayCommand]
    private void NavigateToOilDefDailyLog()
    {
        if (!IsOilDefVisible) return;
        SelectedNavIndex = 1;
        CurrentView = GetOrCreateView<OilDefDailyLogViewModel>();
    }

    [RelayCommand]
    private void NavigateToFinalCalculation()
    {
        if (!IsFinalCalculationVisible) return;
        SelectedNavIndex = 2;
        bool wasCached = _cachedViews.ContainsKey(typeof(FinalCalculationViewModel));
        var vm = GetOrCreateView<FinalCalculationViewModel>();
        CurrentView = vm;
        if (wasCached && !vm.IsLoading)
        {
            _ = vm.LoadShiftDataCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void NavigateToDayTotal()
    {
        if (!IsDayTotalVisible) return;
        SelectedNavIndex = 3;
        bool wasCached = _cachedViews.ContainsKey(typeof(DayTotalViewModel));
        var vm = GetOrCreateView<DayTotalViewModel>();
        CurrentView = vm;
        if (wasCached && !vm.IsLoading)
        {
            _ = vm.RefreshCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void NavigateToDebtorManagement()
    {
        if (!IsDebtorManagementVisible) return;
        SelectedNavIndex = 4;
        var debtorVm = GetOrCreateView<DebtorManagementViewModel>();
        debtorVm.SelectedTabIndex = 0; // Debtors Directory tab
        _ = debtorVm.LoadDataAsync();
        CurrentView = debtorVm;
    }

    [RelayCommand]
    private async Task ToggleDebtorManagementLocationAsync()
    {
        bool current = IsDebtorManagementAsNewPage;
        await _featureService.SaveFeaturesAsync(new[]
        {
            new AppFeatureSetting
            {
                FeatureKey = "UI_DebtorManagement_AsNewPage",
                IsEnabled = !current,
                DisplayName = "Debtor Management on Dedicated Page (vs Shift Total)",
                Category = "Navigation",
                TargetRole = "Global"
            }
        });
        NotifyFeaturePropertiesChanged();
    }

    [RelayCommand]
    private void NavigateToDsmPersonalDebtor()
    {
        if (!IsDsmPersonalDebtorVisible) return;
        SelectedNavIndex = 14;
        var vm = GetOrCreateView<DsmPersonalDebtorViewModel>();
        _ = vm.LoadDataAsync();
        CurrentView = vm;
    }

    [RelayCommand]
    private void NavigateToDsmApprovalQueue()
    {
        if (!IsDsmApprovalQueueVisible) return;
        SelectedNavIndex = 5;
        var vm = GetOrCreateView<DsmApprovalQueueViewModel>();
        CurrentView = vm;
        _ = vm.RefreshQueueAsync();
    }

    [RelayCommand]
    private void NavigateToDsmManagement()
    {
        if (!IsDsmManagementVisible) return;
        SelectedNavIndex = 6;
        CurrentView = GetOrCreateView<DsmManagementViewModel>();
    }

    [RelayCommand]
    private void NavigateToCardSettlement()
    {
        if (!IsCardSettlementVisible) return;
        SelectedNavIndex = 7;
        CurrentView = GetOrCreateView<CardSettlementViewModel>();
    }

    [RelayCommand]
    private void NavigateToAgsImport()
    {
        if (!IsAgsImportVisible) return;
        SelectedNavIndex = 9;
        CurrentView = GetOrCreateView<AgsImportViewModel>();
    }

    [RelayCommand]
    private void NavigateToPettyCash()
    {
        if (!IsPettyCashVisible) return;
        SelectedNavIndex = 11;
        CurrentView = GetOrCreateView<PettyCashViewModel>();
    }

    [RelayCommand]
    private void NavigateToFuelTankerEntry()
    {
        if (!IsFuelTankerVisible) return;
        SelectedNavIndex = 12;
        CurrentView = GetOrCreateView<FuelTankerEntryViewModel>();
    }

    [RelayCommand]
    private void NavigateToTankStockHistory()
    {
        if (!IsTankStockVisible) return;
        SelectedNavIndex = 13;
        CurrentView = GetOrCreateView<TankStockHistoryViewModel>();
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        if (!IsSettingsVisible) return;
        SelectedNavIndex = 10;
        CurrentView = GetOrCreateView<SettingsViewModel>();
    }

    [ObservableProperty] private bool _isPageLoading;

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

    public void UpdateLastSaveTime() =>
        LastSaveTime = DateTime.Now.ToString("hh:mm:ss tt");

    public void RefreshTitle() => _ = LoadStationNameAsync();

    partial void OnCurrentViewChanged(object? oldValue, object? newValue)
    {
        // Retain cached ViewModels across screen navigation so their sync/data subscriptions stay active
        // without re-subscribing; only dispose uncached views here, and dispose cached views in Dispose().
        if (oldValue is IDisposable disposable && !_cachedViews.Values.Any(v => ReferenceEquals(v, oldValue)))
        {
            disposable.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _clockTimer.Stop();
        _featureService.FeatureConfigurationChanged -= _featureChangedHandler;
        _syncEngine.SyncStatusChanged -= _syncStatusChangedHandler;
        _pollingService.PendingCountChanged -= _pendingCountChangedHandler;
        _pollingService.PollStateChanged -= _pollStateChangedHandler;

        var disposed = new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (CurrentView is IDisposable currentDisposable && disposed.Add(currentDisposable))
        {
            currentDisposable.Dispose();
        }
        foreach (var cached in _cachedViews.Values)
        {
            if (cached is IDisposable cachedDisposable && disposed.Add(cachedDisposable))
            {
                cachedDisposable.Dispose();
            }
        }
        _cachedViews.Clear();
        GC.SuppressFinalize(this);
    }

    public void UpdateSyncStatus(DateTime lastSync, int pendingCount, bool isConnected, string statusMessage)
    {
        PendingSyncCount = pendingCount;
        IsSyncConnected = isConnected;
        
        var lastSyncStr = lastSync == DateTime.MinValue ? "Never" : lastSync.ToString("dd MMM yyyy hh:mm tt");
        LastSyncTime = lastSyncStr;

        if (!isConnected)
        {
            SyncStatusText = pendingCount > 0 ? $"Offline ({pendingCount} pending)" : "Offline";
            SyncStatusToolTip = $"Status: Offline\nPending Changes: {pendingCount}\nLast Successful Sync: {lastSyncStr}\nRetrying automatically in the background...";
        }
        else
        {
            SyncStatusText = string.IsNullOrWhiteSpace(statusMessage) ? "Synced" : statusMessage;
            SyncStatusToolTip = $"Status: Online / Synced\nLast Successful Sync: {lastSyncStr}";
        }
    }
}
