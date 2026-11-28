using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using Serilog;

[assembly: InternalsVisibleTo("FuelPro.Tests")]

namespace FuelPro.Sync;

public class SyncEngine : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SyncConfigService _configService;
    private readonly SupabaseHttpClient _httpClient;
    private readonly StationPairingService _pairingService;
    private readonly ILogger _logger = Log.ForContext<SyncEngine>();
    
    private Timer? _syncTimer;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private bool _isSyncing;
    private string? _lastPushErrorMessage;
    public string? LastPushErrorMessage => _lastPushErrorMessage;
    private bool _isDisposed;
    private bool _isStopped = true;
    private readonly TimeSpan _syncInterval = TimeSpan.FromSeconds(3);
    private DateTime _lastPullTime = DateTime.MinValue;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>> _knownMissingColumnsByTable = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerSettings PullJsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None
    };

    public event Action<SyncStatusInfo>? SyncStatusChanged;
    public event Action? PullCompleted;
    public event Action? OperationalDataPulled;
    internal void RaisePullCompleted() => PullCompleted?.Invoke();

    private static readonly HashSet<string> OperationalDsmNotificationTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shifts",
        "DsmEntries",
        "NozzleReadings",
        "PaymentCollections",
        "DebitEntries",
        "TestingEntries",
        "Expenses",
        "CashDenominations",
        "ShiftOtherCash",
        "ShiftFuelRates",
        "Creditors",
        "CreditorRepayments",
        "DsmPersonalDebtors",
        "DsmPersonalDebtorRepayments",
        "KhandharePetroleumEntries",
        "DsmQrPayments",
        "AgsShiftImports",
        "AgsNozzleReadings",
        "AgsDailySummaries"
    };

    private static readonly HashSet<string> PettyCashNotificationTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "PumpExpenses",
        "ExpenseCategories",
        "PumpExpenseCategoryItems",
        "PettyCashTransactions"
    };

    private static readonly HashSet<string> InventoryNotificationTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProductMasters",
        "OilDefInventories",
        "OilDefPurchases",
        "OilDefDailyLogs",
        "FuelTankers",
        "TankDailyStocks"
    };

    private static readonly HashSet<string> PayrollNotificationTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "DsmProfiles",
        "DsmSalaryAdjustments",
        "DsmSalaryHistories",
        "DsmSalaryPayments"
    };

    private static readonly HashSet<string> DebtorNotificationTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "OpeningBalances",
        "Creditors",
        "CreditorRepayments",
        "DebitEntries",
        "DsmPersonalDebtors",
        "DsmPersonalDebtorRepayments"
    };

    private void StripKnownMissingColumns(string tableName, IDictionary<string, object?> row)
    {
        if (_knownMissingColumnsByTable.TryGetValue(tableName, out var cols))
        {
            lock (cols)
            {
                foreach (var col in cols)
                {
                    row.Remove(col);
                }
            }
        }
    }

    private void RecordKnownMissingColumn(string tableName, string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName)) return;
        var cols = _knownMissingColumnsByTable.GetOrAdd(tableName, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        lock (cols)
        {
            cols.Add(columnName);
        }
    }
    
    public SyncStatusInfo CurrentStatus { get; private set; } = new();

    public static readonly string[] SyncedTables = new[]
    {
        "Users",
        "Shifts",
        "DsmEntries",
        "NozzleReadings",
        "PaymentCollections",
        "DebitEntries",
        "TestingEntries",
        "Expenses",
        "CashDenominations",
        "Settings",
        "ShiftOtherCash",
        "ShiftFuelRates",
        "DsmProfiles",
        "Creditors",
        "CreditorRepayments",
        "AgsShiftImports",
        "AgsNozzleReadings",
        "AgsTankStocks",
        "AgsDailySummaries",
        "ProductMasters",
        "OilDefInventories",
        "OilDefPurchases",
        "OilDefDailyLogs",
        "DsmUsers",
        "DsmPumpAssignments",
        "DsmDevices",
        "DsmApprovalAudits",
        "DsmAttendance",
        "SyncChangeLogs",
        // New tables from P3_shin_nvl
        "DebtorVehicles",
        "OpeningBalances",
        "PumpMappings",
        "AuditLogs",
        "DayLocks",
        "PumpExpenses",
        "ExpenseCategories",
        "PumpExpenseCategoryItems",
        "DsmSalaryAdjustments",
        "DsmPersonalDebtors",
        "DsmPersonalDebtorRepayments",
        "PettyCashTransactions",
        "KhandharePetroleumEntries",
        // Tanker Management
        "FuelTankers",
        "TankDailyStocks",
        // Salary & Payroll
        "DsmSalaryHistories",
        "DsmSalaryPayments"
        // Dynamic Configurations (TankDefinitions, CollectionTypes, AppFeatureSettings, PaymentCollectionItems synced via Settings & PaymentCollections)
    };

    // ── FK Configuration ──────────────────────────────────────────────
    // Maps child table FK properties to the parent table name.
    // Used during both push (local int → parent GUID) and pull (parent GUID → local int).
    private record FkMapping(string FkProperty, string ReferencedTable);
    private record TableSyncConfig(string TableName, FkMapping[] ForeignKeys);

    /// <summary>
    /// Tables ordered by dependency (parents before children).
    /// Users are excluded — each machine manages its own user accounts via seed data.
    /// </summary>
    private static readonly TableSyncConfig[] PullTableOrder = new[]
    {
        new TableSyncConfig("SyncChangeLogs", Array.Empty<FkMapping>()),
        new TableSyncConfig("Settings", Array.Empty<FkMapping>()),
        new TableSyncConfig("PumpMappings", Array.Empty<FkMapping>()),
        new TableSyncConfig("DsmProfiles", Array.Empty<FkMapping>()),
        new TableSyncConfig("DsmUsers", Array.Empty<FkMapping>()),
        new TableSyncConfig("DsmDevices", new[] { new FkMapping("DsmUserId", "DsmUsers") }),
        new TableSyncConfig("DsmPumpAssignments", new[] { new FkMapping("DsmUserId", "DsmUsers") }),
        new TableSyncConfig("DsmApprovalAudits", Array.Empty<FkMapping>()),
        new TableSyncConfig("DsmAttendance", new[] { new FkMapping("DsmUserId", "DsmUsers") }),
        new TableSyncConfig("Creditors", Array.Empty<FkMapping>()),
        new TableSyncConfig("DebtorVehicles", new[] { new FkMapping("CreditorId", "Creditors") }),
        new TableSyncConfig("OpeningBalances", new[] { new FkMapping("CreditorId", "Creditors") }),
        new TableSyncConfig("ProductMasters", Array.Empty<FkMapping>()),
        new TableSyncConfig("Shifts", Array.Empty<FkMapping>()),
        new TableSyncConfig("AgsShiftImports", Array.Empty<FkMapping>()),
        new TableSyncConfig("AgsDailySummaries", Array.Empty<FkMapping>()),
        new TableSyncConfig("CreditorRepayments", Array.Empty<FkMapping>()),
        new TableSyncConfig("OilDefInventories", new[] { new FkMapping("ProductId", "ProductMasters") }),
        new TableSyncConfig("OilDefPurchases", new[] { new FkMapping("ProductId", "ProductMasters") }),
        new TableSyncConfig("OilDefDailyLogs", new[] {
            new FkMapping("ProductId", "ProductMasters"),
            new FkMapping("ShiftId", "Shifts")
        }),
        new TableSyncConfig("ShiftOtherCash", new[] { new FkMapping("ShiftId", "Shifts") }),
        new TableSyncConfig("ShiftFuelRates", new[] { new FkMapping("ShiftId", "Shifts") }),
        new TableSyncConfig("DsmEntries", new[] { new FkMapping("ShiftId", "Shifts") }),
        new TableSyncConfig("NozzleReadings", new[] { new FkMapping("DsmEntryId", "DsmEntries") }),
        new TableSyncConfig("PaymentCollections", new[] { new FkMapping("DsmEntryId", "DsmEntries") }),
        new TableSyncConfig("DebitEntries", new[] { new FkMapping("DsmEntryId", "DsmEntries") }),
        new TableSyncConfig("TestingEntries", new[] { new FkMapping("DsmEntryId", "DsmEntries") }),
        new TableSyncConfig("Expenses", new[] { new FkMapping("DsmEntryId", "DsmEntries"), new FkMapping("ShiftId", "Shifts") }),
        new TableSyncConfig("CashDenominations", new[] { new FkMapping("DsmEntryId", "DsmEntries") }),
        new TableSyncConfig("AgsNozzleReadings", new[] { new FkMapping("AgsShiftImportId", "AgsShiftImports") }),
        new TableSyncConfig("AgsTankStocks", new[] { new FkMapping("AgsShiftImportId", "AgsShiftImports") }),
        // New tables from P3_shin_nvl
        new TableSyncConfig("AuditLogs", Array.Empty<FkMapping>()),
        new TableSyncConfig("DayLocks", Array.Empty<FkMapping>()),
        new TableSyncConfig("PumpExpenses", Array.Empty<FkMapping>()),
        new TableSyncConfig("ExpenseCategories", Array.Empty<FkMapping>()),
        new TableSyncConfig("PumpExpenseCategoryItems", new[] {
            new FkMapping("PumpExpenseId", "PumpExpenses"),
            new FkMapping("CategoryId", "ExpenseCategories")
        }),
        new TableSyncConfig("DsmSalaryAdjustments", Array.Empty<FkMapping>()),
        new TableSyncConfig("DsmPersonalDebtors", new[] { new FkMapping("DsmEntryId", "DsmEntries") }),
        new TableSyncConfig("DsmPersonalDebtorRepayments", new[] { 
            new FkMapping("DsmPersonalDebtorId", "DsmPersonalDebtors"), 
            new FkMapping("ShiftId", "Shifts") 
        }),
        new TableSyncConfig("PettyCashTransactions", new[] {
            new FkMapping("ShiftExpenseId", "Expenses")
        }),
        new TableSyncConfig("KhandharePetroleumEntries", new[] {
            new FkMapping("DsmEntryId", "DsmEntries")
        }),
        new TableSyncConfig("FuelTankers", Array.Empty<FkMapping>()),
        new TableSyncConfig("TankDailyStocks", new[] { new FkMapping("ShiftId", "Shifts") }),
        new TableSyncConfig("DsmSalaryHistories", new[] { new FkMapping("DsmProfileId", "DsmProfiles") }),
        new TableSyncConfig("DsmSalaryPayments", new[] { new FkMapping("DsmProfileId", "DsmProfiles") })
    };

    /// <summary>
    /// Lookup from table name → FK config, used during push to translate FKs.
    /// </summary>
    private static readonly Dictionary<string, FkMapping[]> FkConfigByTable =
        PullTableOrder.ToDictionary(t => t.TableName, t => t.ForeignKeys);

    public SyncEngine(IServiceProvider serviceProvider)
        : this(serviceProvider, new SupabaseHttpClient())
    {
    }

    public SyncEngine(IServiceProvider serviceProvider, SupabaseHttpClient httpClient)
    {
        _serviceProvider = serviceProvider;
        _configService = serviceProvider.GetRequiredService<SyncConfigService>();
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _pairingService = serviceProvider.GetService<StationPairingService>()
            ?? new StationPairingService(_configService, _httpClient);
    }

    public void Start()
    {
        _logger.Information("Starting background sync engine...");
        _isStopped = false;
        _syncTimer?.Dispose();
        _syncTimer = new Timer(async _ => await OnTimerTickAsync(), null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    public void Stop()
    {
        _logger.Information("Stopping background sync engine...");
        _isStopped = true;
        _syncTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _syncTimer?.Dispose();
        _syncTimer = null;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
        _syncLock.Dispose();
    }

    public async Task<int> GetPendingCountAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            return await context.SyncChangeLogs.CountAsync(l => !l.IsSynced);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get pending sync count");
            return 0;
        }
    }

    public async Task ForceSyncAsync()
    {
        var settings = await _configService.GetSettingsAsync();
        if (!settings.SyncEnabled || string.IsNullOrEmpty(settings.SupabaseUrl) || string.IsNullOrEmpty(settings.SupabaseApiKey))
        {
            return;
        }

        _logger.Information("Force sync requested.");
        try
        {
            await QueueAllUnsyncedHistoricalRecordsAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to queue unsynced historical records during force sync.");
        }
        await RunSyncCycleAsync(forcePull: true, waitForLock: true);
    }

    /// <summary>
    /// Executes a serialized immediate sync cycle and verifies whether the specified local record
    /// has no remaining un-synced pending change logs or push errors.
    /// </summary>
    public async Task<(bool Success, string? ErrorMessage)> ForceSyncAndVerifyRecordAsync(string tableName, int localRecordId)
    {
        var settings = await _configService.GetSettingsAsync();
        if (!settings.SyncEnabled || string.IsNullOrEmpty(settings.SupabaseUrl) || string.IsNullOrEmpty(settings.SupabaseApiKey))
        {
            return (false, "Cloud sync is disabled or unconfigured.");
        }

        try
        {
            await RunSyncCycleAsync(forcePull: true, waitForLock: true);

            using var scope = _serviceProvider.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            bool hasPending = await context.SyncChangeLogs
                .AnyAsync(l => l.TableName == tableName && l.RecordId == localRecordId && !l.IsSynced);

            if (hasPending || !string.IsNullOrWhiteSpace(_lastPushErrorMessage))
            {
                return (false, !string.IsNullOrWhiteSpace(_lastPushErrorMessage)
                    ? _lastPushErrorMessage
                    : CurrentStatus.StatusMessage);
            }

            if (!CurrentStatus.IsConnected)
            {
                return (false, CurrentStatus.StatusMessage);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "ForceSyncAndVerifyRecordAsync failed for {Table} LocalId={Id}", tableName, localRecordId);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Attaches this workstation to <paramref name="newStationId"/> safely:
    /// when a fresh secondary install (no local Shifts or DsmEntries) switches away from its
    /// auto-generated temporary StationId, pulls the station's authoritative cloud state first
    /// so seeded local defaults never overwrite another computer's configured station data.
    /// </summary>
    public async Task AttachToStationAndSyncAsync(string oldStationId, string newStationId)
    {
        var trimmedNew = (newStationId ?? string.Empty).Trim();
        var trimmedOld = (oldStationId ?? string.Empty).Trim();
        var settings = await _configService.GetSettingsAsync();

        if (!string.IsNullOrWhiteSpace(trimmedOld)
            && !string.Equals(trimmedOld, trimmedNew, StringComparison.OrdinalIgnoreCase)
            && settings.SyncEnabled
            && !string.IsNullOrEmpty(settings.SupabaseUrl)
            && !string.IsNullOrEmpty(settings.SupabaseApiKey))
        {
            using var scope = _serviceProvider.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            bool hasOperationalData = await context.DsmEntries.AnyAsync() || await context.Shifts.AnyAsync();
            if (!hasOperationalData)
            {
                await context.Database.ExecuteSqlRawAsync(
                    "UPDATE SyncChangeLogs SET IsSynced = 1, SyncedAt = {0} WHERE IsSynced = 0 AND StationId = {1}",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), trimmedOld);

                _httpClient.Configure(settings.SupabaseUrl, settings.SupabaseApiKey);
                await PerformPullAsync(settings, forcePull: true);
            }
        }

        await QueueAllHistoricalRecordsForStationAsync(trimmedNew);
        await ForceSyncAsync();
    }

    public async Task QueueAllUnsyncedHistoricalRecordsAsync()
    {
        _logger.Information("Scanning for unsynced historical records to queue...");
        using var scope = _serviceProvider.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var settings = await _configService.GetSettingsAsync();

        int totalQueued = 0;

        foreach (var tableName in SyncedTables)
        {
            if (tableName == "SyncChangeLogs" || tableName == "Users") continue;

            var entityType = context.Model.GetEntityTypes().FirstOrDefault(t => t.GetTableName() == tableName);
            if (entityType == null) continue;

            var pkProp = entityType.FindPrimaryKey()?.Properties.FirstOrDefault();
            if (pkProp == null) continue;

            List<int> localIds;
            try
            {
                localIds = await context.Database.SqlQueryRaw<int>(
                    $"SELECT \"{pkProp.Name}\" AS \"Value\" FROM \"{tableName}\"")
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not query primary keys for historical sync scan on table {Table}", tableName);
                continue;
            }

            if (localIds.Count == 0) continue;

            var mappedIds = new HashSet<int>(await context.SyncIdMappings
                .Where(m => m.TableName == tableName)
                .Select(m => m.LocalId)
                .ToListAsync());

            var pendingLoggedIds = new HashSet<int>(await context.SyncChangeLogs
                .Where(l => l.TableName == tableName && !l.IsSynced)
                .Select(l => l.RecordId)
                .ToListAsync());

            int queuedForTable = 0;
            foreach (var localId in localIds)
            {
                if (!mappedIds.Contains(localId) && !pendingLoggedIds.Contains(localId))
                {
                    context.SyncChangeLogs.Add(new SyncChangeLog
                    {
                        TableName = tableName,
                        RecordId = localId,
                        Operation = "INSERT",
                        CreatedAt = DateTime.Now,
                        IsSynced = false,
                        StationId = settings.StationId,
                        MachineId = settings.MachineId,
                        SyncGuid = Guid.NewGuid().ToString()
                    });
                    queuedForTable++;
                    totalQueued++;
                }
            }

            if (queuedForTable > 0)
            {
                _logger.Information("Queued {Count} unsynced historical records for table {Table}", queuedForTable, tableName);
            }
        }

        if (totalQueued > 0)
        {
            await context.SaveChangesAsync();
            _logger.Information("Successfully queued {Count} total historical records for synchronization.", totalQueued);
        }
        else
        {
            _logger.Information("No unsynced historical records found.");
        }
    }

    public async Task QueueAllHistoricalRecordsForStationAsync(string stationId)
    {
        _logger.Information("Queueing all historical records for station {StationId}...", stationId);
        using var scope = _serviceProvider.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var settings = await _configService.GetSettingsAsync();

        int totalQueued = 0;

        try
        {
            // Backfill stationId for unassigned legacy logs without resetting IsSynced status
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE SyncChangeLogs SET StationId = {0} WHERE StationId IS NULL OR StationId = ''",
                stationId);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to backfill missing stationId on SyncChangeLogs");
        }

        foreach (var tableName in SyncedTables)
        {
            if (tableName == "SyncChangeLogs") continue;

            var entityType = context.Model.GetEntityTypes().FirstOrDefault(t => t.GetTableName() == tableName);
            if (entityType == null) continue;

            var pkProperty = entityType.FindPrimaryKey()?.Properties.FirstOrDefault();
            if (pkProperty == null) continue;

            try
            {
                // Dynamically construct a query to fetch all primary key IDs for this table
                var localIds = await context.Database
                    .SqlQueryRaw<int>($"SELECT [{pkProperty.Name}] FROM [{tableName}]")
                    .ToListAsync();

                if (localIds.Count == 0) continue;

                var idMappings = (await context.SyncIdMappings
                    .Where(m => m.TableName == tableName)
                    .ToListAsync())
                    .GroupBy(m => m.LocalId)
                    .ToDictionary(g => g.Key, g => g.First().RemoteGuid);

                var alreadyLoggedIds = new HashSet<int>(await context.SyncChangeLogs
                    .Where(l => l.TableName == tableName && l.StationId == stationId)
                    .Select(l => l.RecordId)
                    .ToListAsync());

                int queuedForTable = 0;
                foreach (var localId in localIds)
                {
                    if (!alreadyLoggedIds.Contains(localId))
                    {
                        var recordGuid = idMappings.TryGetValue(localId, out var g) ? g : Guid.NewGuid().ToString();
                        context.SyncChangeLogs.Add(new SyncChangeLog
                        {
                            TableName = tableName,
                            RecordId = localId,
                            Operation = "INSERT",
                            CreatedAt = DateTime.Now,
                            IsSynced = false,
                            StationId = stationId,
                            MachineId = settings.MachineId,
                            SyncGuid = Guid.NewGuid().ToString(),
                            RecordGuid = recordGuid
                        });
                        queuedForTable++;
                        totalQueued++;
                    }
                }

                if (queuedForTable > 0)
                {
                    _logger.Information("Queued {Count} records for table {Table} under station {StationId}", queuedForTable, tableName, stationId);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to dynamically queue historical records for table {Table}", tableName);
            }
        }

        if (totalQueued > 0)
        {
            await context.SaveChangesAsync();
            _logger.Information("Successfully queued {Count} total historical records for station {StationId}", totalQueued, stationId);
        }
    }

    private async Task OnTimerTickAsync()
    {
        if (_isStopped || _isDisposed) return;

        try
        {
            await RunSyncCycleAsync(forcePull: false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Background timer tick sync failed.");
        }
        finally
        {
            if (!_isStopped && !_isDisposed && _syncTimer != null)
            {
                try
                {
                    _syncTimer.Change(_syncInterval, Timeout.InfiniteTimeSpan);
                }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private async Task RunSyncCycleAsync(bool forcePull = false, bool waitForLock = false)
    {
        if (waitForLock)
        {
            if (!await _syncLock.WaitAsync(TimeSpan.FromSeconds(20)))
            {
                _logger.Warning("Timed out waiting for sync lock during queued sync request.");
                return;
            }
        }
        else
        {
            if (_isSyncing) return;
            if (!await _syncLock.WaitAsync(0)) return;
        }

        _isSyncing = true;
        try
        {
            var settings = await _configService.GetSettingsAsync();
            if (!settings.SyncEnabled || string.IsNullOrEmpty(settings.SupabaseUrl) || string.IsNullOrEmpty(settings.SupabaseApiKey))
            {
                UpdateStatus(settings.LastSyncTime, 0, false, "Sync disabled or unconfigured");
                return;
            }

            if (settings.SyncEnabled && string.IsNullOrWhiteSpace(settings.StationId))
            {
                UpdateStatus(settings.LastSyncTime, 0, false, "Sync disabled: Station ID required");
                _logger.Warning("Sync enabled but Station ID is blank. Aborting sync cycle.");
                return;
            }

            _httpClient.Configure(settings.SupabaseUrl, settings.SupabaseApiKey);

            // In Multi-Computer mode, verify device authorization before proceeding
            if (settings.MultiComputerEnabled)
            {
                var (isAuthorized, reason) = await _pairingService.CheckDeviceAuthorizationAsync(settings.StationId, settings.MachineId);
                if (!isAuthorized)
                {
                    UpdateStatus(settings.LastSyncTime, 0, false, $"Access Revoked: {reason}");
                    _logger.Warning("Sync cycle aborted: Machine {MachineId} is revoked for station {StationId}: {Reason}", settings.MachineId, settings.StationId, reason);
                    return;
                }
            }

            var authService = _serviceProvider.GetRequiredService<AuthService>();
            
            bool isOwner = authService.IsOwner;
            bool shouldPull = forcePull || isOwner || settings.MultiComputerEnabled || (DateTime.Now - _lastPullTime).TotalMinutes >= 1;

            // Check roles and route accordingly
            if (authService.IsOwner)
            {
                // Owner is read-only for transaction data at this terminal and pulls updates
                if (shouldPull)
                {
                    await PerformPullAsync(settings, forcePull);
                    _lastPullTime = DateTime.Now;
                }
            }
            else
            {
                try
                {
                    await PerformPushAsync(settings);

                    if (shouldPull)
                    {
                        await PerformPullAsync(settings, forcePull);
                        _lastPullTime = DateTime.Now;
                    }
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    _logger.Warning(ex, "Concurrency conflict during sync. The changes will be retried in the next sync cycle.");
                    UpdateStatus(settings.LastSyncTime, 0, true, "Sync paused (retrying...)");
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException ||
                                  ex is System.Net.Sockets.SocketException ||
                                  ex is TaskCanceledException ||
                                  ex is System.IO.IOException ||
                                  ex.InnerException is HttpRequestException ||
                                  ex.InnerException is System.Net.Sockets.SocketException ||
                                  ex.InnerException is TaskCanceledException ||
                                  ex.InnerException is System.IO.IOException)
        {
            _logger.Warning(ex, "Sync cycle network failure (offline mode)");
            
            int pendingCount = await GetPendingCountAsync();
            var settings = await _configService.GetSettingsAsync();
            
            CurrentStatus.IsConnected = false;
            CurrentStatus.PendingRecords = pendingCount;
            var lastSyncDisplay = settings.LastSyncTime == DateTime.MinValue
                ? "Never"
                : (settings.LastSyncTime.Kind == DateTimeKind.Utc
                    ? settings.LastSyncTime.ToLocalTime()
                    : settings.LastSyncTime).ToString("dd MMM yyyy hh:mm tt");
            CurrentStatus.StatusMessage = $"Offline\nPending Changes: {pendingCount}\nLast Successful Sync:\n{lastSyncDisplay}\nRetrying Automatically...";
            SyncStatusChanged?.Invoke(CurrentStatus);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Sync cycle failed with fatal exception (Database/Data issue)");
            CurrentStatus.IsConnected = false;
            CurrentStatus.StatusMessage = $"Database Error: {ex.Message}";
            SyncStatusChanged?.Invoke(CurrentStatus);
            throw; // Let database/mapping errors fail normally
        }
        finally
        {
            _isSyncing = false;
            _syncLock.Release();
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // PUSH SYNC — Local changes → Supabase (GUID-based)
    // ═══════════════════════════════════════════════════════════════════

    private async Task<bool> RecordSyncChangeLogTombstoneAsync(SyncChangeLog del, string targetRecordGuid, SyncSettings settings)
    {
        var logDict = new Dictionary<string, object?>
        {
            { "SyncGuid", del.SyncGuid },
            { "TableName", del.TableName },
            { "RecordGuid", targetRecordGuid },
            { "Operation", "DELETE" },
            { "station_id", settings.StationId },
            { "machine_id", settings.MachineId },
            { "Timestamp", SyncTimestampHelper.FormatUtcIso(del.CreatedAt) }
        };
        var logJson = JsonConvert.SerializeObject(new[] { logDict });
        var logResponse = await _httpClient.SendRequestAsync(HttpMethod.Post, "SyncChangeLogs", logJson, isUpsert: false);

        if (logResponse.IsSuccessStatusCode)
        {
            return true;
        }

        // Handle HTTP 409 Conflict (SyncGuid unique constraint violation)
        if (logResponse.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            try
            {
                var verifyResponse = await _httpClient.SendRequestAsync(
                    HttpMethod.Get, $"SyncChangeLogs?SyncGuid=eq.{del.SyncGuid}&limit=1");

                if (!verifyResponse.IsSuccessStatusCode)
                {
                    _logger.Warning("SyncChangeLogs tombstone push returned 409 for SyncGuid={Guid}, but retrieval failed ({StatusCode}). Retaining pending.",
                        del.SyncGuid, verifyResponse.StatusCode);
                    return false;
                }

                var verifyJson = await verifyResponse.Content.ReadAsStringAsync();
                var existingItems = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(verifyJson);
                var existing = existingItems?.FirstOrDefault();

                if (existing == null)
                {
                    _logger.Warning("SyncChangeLogs tombstone push returned 409 for SyncGuid={Guid}, but no existing record was returned. Retaining pending.",
                        del.SyncGuid);
                    return false;
                }

                bool tableMatches = string.Equals(
                    GetDictString(existing, "TableName"), del.TableName, StringComparison.OrdinalIgnoreCase);

                bool recordGuidMatches = string.Equals(
                    GetDictString(existing, "RecordGuid"), targetRecordGuid, StringComparison.OrdinalIgnoreCase);

                bool operationMatches = string.Equals(
                    GetDictString(existing, "Operation"), del.Operation, StringComparison.OrdinalIgnoreCase);

                string remoteStation = GetDictString(existing, "station_id") ?? GetDictString(existing, "StationId") ?? "";
                bool stationMatches = string.Equals(remoteStation, settings.StationId, StringComparison.OrdinalIgnoreCase);

                string remoteMachine = GetDictString(existing, "machine_id") ?? GetDictString(existing, "MachineId") ?? "";
                bool machineMatches = string.Equals(remoteMachine, settings.MachineId, StringComparison.OrdinalIgnoreCase);

                object? remoteTimestamp = existing.TryGetValue("Timestamp", out var tsVal) ? tsVal : (existing.TryGetValue("CreatedAt", out var caVal) ? caVal : null);
                bool timestampMatches = AreTimestampsEquivalent(remoteTimestamp, del.CreatedAt);

                if (tableMatches && recordGuidMatches && operationMatches && stationMatches && machineMatches && timestampMatches)
                {
                    _logger.Information("SyncChangeLog tombstone {SyncGuid} already exists with identical payload in Supabase. Treating as acknowledged.", del.SyncGuid);
                    return true;
                }
                else
                {
                    _logger.Error("SyncChangeLogs tombstone conflict for SyncGuid={Guid}! Existing remote payload differs from local: " +
                        "Table({RemoteTable} vs {LocalTable}), RecordGuid({RemoteRg} vs {LocalRg}), Op({RemoteOp} vs {LocalOp}), Station({RemoteSt} vs {LocalSt}), Machine({RemoteMach} vs {LocalMach}), TimestampMatch={TimeMatch}. Conflict preserved.",
                        del.SyncGuid,
                        GetDictString(existing, "TableName"), del.TableName,
                        GetDictString(existing, "RecordGuid"), targetRecordGuid,
                        GetDictString(existing, "Operation"), del.Operation,
                        remoteStation, settings.StationId,
                        remoteMachine, settings.MachineId,
                        timestampMatches);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error verifying 409 conflict for SyncChangeLogs tombstone {Guid}", del.SyncGuid);
                return false;
            }
        }

        var error = await logResponse.Content.ReadAsStringAsync();
        _logger.Error("Failed to push DELETE log entry to Supabase SyncChangeLogs (HTTP {StatusCode}): {Error}",
            logResponse.StatusCode, error);
        return false;
    }

    private static bool AreTimestampsEquivalent(object? remoteVal, DateTime localDt)
    {
        if (remoteVal == null) return false;

        DateTime remoteUtc;
        if (remoteVal is DateTime dt)
        {
            remoteUtc = dt.ToUniversalTime();
        }
        else if (remoteVal is DateTimeOffset dto)
        {
            remoteUtc = dto.UtcDateTime;
        }
        else
        {
            var str = remoteVal.ToString();
            if (string.IsNullOrWhiteSpace(str)) return false;

            if (DateTimeOffset.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDto))
            {
                remoteUtc = parsedDto.UtcDateTime;
            }
            else if (DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDt))
            {
                remoteUtc = parsedDt.ToUniversalTime();
            }
            else
            {
                return false;
            }
        }

        var localUtc = SyncTimestampHelper.ToUtcDateTime(localDt, SyncTimestampSemantics.LocalInstant);
        return Math.Abs((remoteUtc - localUtc).TotalSeconds) <= 1.0;
    }

    private static bool AreSyncPropertyValuesEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        if (a is DateTime da && b is DateTime db)
        {
            return Math.Abs((da - db).TotalMilliseconds) < 1.0;
        }
        if (a is double d1 && b is double d2)
        {
            return Math.Abs(d1 - d2) < 1e-9;
        }
        return Equals(a, b);
    }

    private static bool IsIncompleteZeroRemotePaymentCollection(Dictionary<string, object?> dict)
    {
        if (TryGetDictValue(dict, "DynamicItemsJson", out var dynObj) && dynObj != null && !string.IsNullOrWhiteSpace(dynObj.ToString()))
        {
            return false;
        }

        string[] numericKeys =
        {
            "PhonePeMorning", "PhonePeDay", "PhonePeNight", "PhonePe",
            "PhonePeCardMorning", "PhonePeCardDay", "PhonePeCardNight", "PhonePeCard",
            "CreditCardMorning", "CreditCardDay", "CreditCardNight", "CreditCard",
            "PetroCardMorning", "PetroCardDay", "PetroCardNight", "PetroCard",
            "CashDeposit", "Others"
        };

        foreach (var k in numericKeys)
        {
            if (TryGetDictValue(dict, k, out var v) && v != null && Convert.ToDouble(v, CultureInfo.InvariantCulture) > 0)
            {
                return false;
            }
        }
        return true;
    }

    private async Task<bool> ApplyPaymentCollectionLegacyFallbackAsync(
        FuelProDbContext context,
        PaymentCollection pc,
        Dictionary<string, object?> dict)
    {
        bool changed = false;

        string shiftType = "";
        if (pc.DsmEntryId > 0)
        {
            var shiftRaw = await context.DsmEntries
                .Where(e => e.DsmEntryId == pc.DsmEntryId)
                .Select(e => e.Shift != null ? e.Shift.ShiftType : null)
                .FirstOrDefaultAsync();
            shiftType = (shiftRaw ?? "").Trim().ToUpperInvariant();
        }
        bool isDay = shiftType is "B" or "II";
        bool isNight = shiftType is "C" or "III";

        double? TryGetPositiveLegacy(string key)
        {
            if (TryGetDictValue(dict, key, out var raw) && raw != null)
            {
                double val = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                if (val > 0) return val;
            }
            return null;
        }

        if (pc.PhonePeMorning == 0 && pc.PhonePeDay == 0 && pc.PhonePeNight == 0)
        {
            var legPh = TryGetPositiveLegacy("PhonePe");
            if (legPh.HasValue)
            {
                if (isDay) pc.PhonePeDay = legPh.Value;
                else if (isNight) pc.PhonePeNight = legPh.Value;
                else pc.PhonePeMorning = legPh.Value;
                changed = true;
            }
        }

        if (pc.PhonePeCardMorning == 0 && pc.PhonePeCardDay == 0 && pc.PhonePeCardNight == 0)
        {
            var legPpc = TryGetPositiveLegacy("PhonePeCard");
            if (legPpc.HasValue)
            {
                if (isDay) pc.PhonePeCardDay = legPpc.Value;
                else if (isNight) pc.PhonePeCardNight = legPpc.Value;
                else pc.PhonePeCardMorning = legPpc.Value;
                changed = true;
            }
        }

        if (pc.CreditCardMorning == 0 && pc.CreditCardDay == 0 && pc.CreditCardNight == 0)
        {
            var legCc = TryGetPositiveLegacy("CreditCard");
            if (legCc.HasValue)
            {
                if (isDay) pc.CreditCardDay = legCc.Value;
                else if (isNight) pc.CreditCardNight = legCc.Value;
                else pc.CreditCardMorning = legCc.Value;
                changed = true;
            }
        }

        if (pc.PetroCardMorning == 0 && pc.PetroCardDay == 0 && pc.PetroCardNight == 0)
        {
            var legPetro = TryGetPositiveLegacy("PetroCard");
            if (legPetro.HasValue)
            {
                if (isDay) pc.PetroCardDay = legPetro.Value;
                else pc.PetroCardNight = legPetro.Value;
                changed = true;
            }
        }

        return changed;
    }

    private static async Task SyncPaymentCollectionItemsFromJsonAsync(FuelProDbContext context, PaymentCollection pc)
    {
        if (string.IsNullOrWhiteSpace(pc.DynamicItemsJson)) return;
        var items = JsonConvert.DeserializeObject<List<PaymentCollectionItem>>(pc.DynamicItemsJson);
        if (items == null || items.Count == 0) return;

        var existing = await context.PaymentCollectionItems.Where(i => i.PaymentId == pc.PaymentId).ToListAsync();
        context.PaymentCollectionItems.RemoveRange(existing);
        foreach (var itm in items)
        {
            context.PaymentCollectionItems.Add(new PaymentCollectionItem
            {
                PaymentId = pc.PaymentId,
                TransactionId = itm.TransactionId,
                CollectionTypeCode = itm.CollectionTypeCode ?? "OTHERS",
                Amount = itm.Amount,
                Tid = itm.Tid,
                Batch = itm.Batch,
                Slot = itm.Slot ?? "General",
                Otp = itm.Otp,
                IsSyntheticSummary = itm.IsSyntheticSummary
            });
        }
    }

    private async Task<bool> BackfillZeroHistoricalPaymentCollectionsAsync(
        FuelProDbContext context,
        SyncSettings settings,
        Dictionary<string, Dictionary<int, string>> localToGuid)
    {
        if (string.IsNullOrWhiteSpace(settings.StationId)) return false;

        var candidates = await context.PaymentCollections
            .Where(p =>
                p.PhonePeMorning == 0 && p.PhonePeDay == 0 && p.PhonePeNight == 0 &&
                p.PhonePeCardMorning == 0 && p.PhonePeCardDay == 0 && p.PhonePeCardNight == 0 &&
                p.CreditCardMorning == 0 && p.CreditCardDay == 0 && p.CreditCardNight == 0 &&
                p.PetroCardMorning == 0 && p.PetroCardDay == 0 && p.PetroCardNight == 0 &&
                p.CashDeposit == 0 && p.Others == 0 &&
                string.IsNullOrEmpty(p.DynamicItemsJson) &&
                p.DsmEntry != null && (p.DsmEntry.TotalInDirect > 0 || p.DsmEntry.TotalCollection > 0) &&
                !context.PaymentCollectionItems.Any(i => i.PaymentId == p.PaymentId))
            .ToListAsync();

        if (candidates.Count == 0) return false;
        if (!localToGuid.TryGetValue("PaymentCollections", out var pcLocalToGuid) || pcLocalToGuid.Count == 0)
            return false;

        var guidToCandidate = new Dictionary<string, PaymentCollection>(StringComparer.OrdinalIgnoreCase);
        foreach (var pc in candidates)
        {
            if (pcLocalToGuid.TryGetValue(pc.PaymentId, out var g) && !string.IsNullOrWhiteSpace(g))
            {
                guidToCandidate[g] = pc;
            }
        }
        if (guidToCandidate.Count == 0) return false;

        int updatedCount = 0;
        var guidList = guidToCandidate.Keys.ToList();
        var stationEscaped = Uri.EscapeDataString(settings.StationId.Trim());

        const int chunkSize = 50;
        for (int offset = 0; offset < guidList.Count; offset += chunkSize)
        {
            var chunk = guidList.Skip(offset).Take(chunkSize).Select(Uri.EscapeDataString);
            var endpoint = $"PaymentCollections?station_id=eq.{stationEscaped}&SyncGuid=in.({string.Join(",", chunk)})";
            var resp = await _httpClient.SendRequestAsync(HttpMethod.Get, endpoint);
            if (!resp.IsSuccessStatusCode) continue;

            var json = await resp.Content.ReadAsStringAsync();
            var remoteRows = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(json, PullJsonSettings);
            if (remoteRows == null || remoteRows.Count == 0) continue;

            FuelProDbContext.BypassTracking = true;
            try
            {
                var paymentType = context.Model.FindEntityType(typeof(PaymentCollection));
                foreach (var dict in remoteRows)
                {
                    var rGuid = GetDictString(dict, "SyncGuid");
                    if (string.IsNullOrWhiteSpace(rGuid) || !guidToCandidate.TryGetValue(rGuid, out var localPc))
                        continue;

                    bool rowChanged = false;
                    var entry = context.Entry(localPc);
                    if (paymentType != null)
                    {
                        foreach (var prop in paymentType.GetProperties())
                        {
                            if (prop.IsPrimaryKey() || prop.Name == nameof(PaymentCollection.DsmEntryId)) continue;
                            if (!TryGetDictValue(dict, prop.Name, out var val) || val == null) continue;

                            var currentVal = entry.Property(prop.Name).CurrentValue;
                            var targetType = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
                            if (targetType == typeof(double))
                            {
                                double dVal = Convert.ToDouble(val, CultureInfo.InvariantCulture);
                                if (currentVal is double curD && curD == 0 && dVal > 0)
                                {
                                    entry.Property(prop.Name).CurrentValue = dVal;
                                    rowChanged = true;
                                }
                            }
                            else if (targetType == typeof(string))
                            {
                                var sVal = val.ToString();
                                if (string.IsNullOrWhiteSpace(currentVal as string) && !string.IsNullOrWhiteSpace(sVal))
                                {
                                    entry.Property(prop.Name).CurrentValue = sVal;
                                    rowChanged = true;
                                }
                            }
                        }
                    }

                    if (await ApplyPaymentCollectionLegacyFallbackAsync(context, localPc, dict))
                    {
                        rowChanged = true;
                    }

                    if (rowChanged)
                    {
                        updatedCount++;
                    }
                    if (!string.IsNullOrWhiteSpace(localPc.DynamicItemsJson))
                    {
                        await SyncPaymentCollectionItemsFromJsonAsync(context, localPc);
                    }
                }
                await context.SaveChangesAsync();
            }
            finally
            {
                FuelProDbContext.BypassTracking = false;
            }
        }

        if (updatedCount > 0)
        {
            _logger.Information("Targeted historical PaymentCollections backfill restored {Count} zero-collection records.", updatedCount);
        }
        return updatedCount > 0;
    }

    private static async Task<bool> SyncCollectionTypesFromJsonAsync(
        FuelProDbContext context,
        string? collectionTypesJson,
        bool removeMissingNonSystem)
    {
        if (string.IsNullOrWhiteSpace(collectionTypesJson)) return false;
        var types = JsonConvert.DeserializeObject<List<CollectionTypeMaster>>(collectionTypesJson);
        if (types == null || types.Count == 0) return false;

        bool configChanged = false;
        var existingCts = await context.CollectionTypes.ToListAsync();
        var processedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ct in types)
        {
            var existingCt = existingCts.FirstOrDefault(x => string.Equals(x.Code, ct.Code, StringComparison.OrdinalIgnoreCase));
            if (existingCt != null)
            {
                if (existingCt.DisplayName != ct.DisplayName ||
                    existingCt.Category != ct.Category ||
                    existingCt.HasTidBatch != ct.HasTidBatch ||
                    existingCt.HasOtp != ct.HasOtp ||
                    existingCt.DisplayOrder != ct.DisplayOrder ||
                    existingCt.IsActive != ct.IsActive ||
                    existingCt.EnableMorning != ct.EnableMorning ||
                    existingCt.EnableDay != ct.EnableDay ||
                    existingCt.EnableNight != ct.EnableNight)
                {
                    existingCt.DisplayName = ct.DisplayName;
                    existingCt.Category = ct.Category;
                    existingCt.HasTidBatch = ct.HasTidBatch;
                    existingCt.HasOtp = ct.HasOtp;
                    existingCt.DisplayOrder = ct.DisplayOrder;
                    existingCt.IsActive = ct.IsActive;
                    existingCt.EnableMorning = ct.EnableMorning;
                    existingCt.EnableDay = ct.EnableDay;
                    existingCt.EnableNight = ct.EnableNight;
                    configChanged = true;
                }
                processedCodes.Add(existingCt.Code);
            }
            else
            {
                context.CollectionTypes.Add(new CollectionTypeMaster
                {
                    Code = ct.Code,
                    DisplayName = ct.DisplayName,
                    Category = ct.Category,
                    HasTidBatch = ct.HasTidBatch,
                    HasOtp = ct.HasOtp,
                    DisplayOrder = ct.DisplayOrder,
                    IsActive = ct.IsActive,
                    EnableMorning = ct.EnableMorning,
                    EnableDay = ct.EnableDay,
                    EnableNight = ct.EnableNight,
                    IsSystem = ct.IsSystem,
                    CreatedAt = DateTime.Now
                });
                processedCodes.Add(ct.Code);
                configChanged = true;
            }
        }

        foreach (var existing in existingCts)
        {
            bool shouldRemove = removeMissingNonSystem
                ? (!existing.IsSystem && !processedCodes.Contains(existing.Code))
                : !processedCodes.Contains(existing.Code);
            if (shouldRemove && !types.Any(t => string.Equals(t.Code, existing.Code, StringComparison.OrdinalIgnoreCase)))
            {
                context.CollectionTypes.Remove(existing);
                configChanged = true;
            }
        }

        return configChanged;
    }

    private static string? GetDictString(Dictionary<string, object?> dict, string key)
    {
        if (dict.TryGetValue(key, out var val) && val != null)
        {
            return val.ToString()?.Trim();
        }
        return null;
    }

    private async Task PerformPushAsync(SyncSettings settings)
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        
        // Fetch logs that are pending
        var allPendingLogs = await context.SyncChangeLogs
            .Where(l => !l.IsSynced)
            .OrderBy(l => l.Id)
            .ToListAsync();

        // Option 4: Mark Mitali DsmQrPayments logs as locally retained (not pushed to cloud)
        var qrLogs = allPendingLogs.Where(l => l.TableName == "DsmQrPayments").ToList();
        if (qrLogs.Count > 0)
        {
            var qrLogIds = qrLogs.Select(l => l.Id).ToList();
            var paramPlaceholders = string.Join(",", qrLogIds.Select((_, i) => $"@p{i}"));
            var sql = "UPDATE SyncChangeLogs SET IsSynced = 1, SyncedAt = NULL, RecordGuid = CASE WHEN RecordGuid IS NULL OR RecordGuid = '' THEN 'LOCAL_RETAINED' WHEN RecordGuid LIKE '%LOCAL_RETAINED%' THEN RecordGuid ELSE RecordGuid || ':LOCAL_RETAINED' END WHERE Id IN (" + paramPlaceholders + ")";
            var parameters = qrLogIds.Select((id, i) => new Microsoft.Data.Sqlite.SqliteParameter($"@p{i}", id)).ToArray();
            await context.Database.ExecuteSqlRawAsync(sql, parameters);
        }

        // Mark changes for other non-synced internal/configuration tables as handled to prevent accumulation (preserves original behavior)
        var otherIgnoredLogs = allPendingLogs.Where(l => !SyncedTables.Contains(l.TableName) && l.TableName != "DsmQrPayments").ToList();
        if (otherIgnoredLogs.Count > 0)
        {
            var ignoredLogIds = otherIgnoredLogs.Select(l => l.Id).ToList();
            var idsCsv = string.Join(",", ignoredLogIds);
            await context.Database.ExecuteSqlRawAsync(
                $"UPDATE SyncChangeLogs SET IsSynced = 1, SyncedAt = '{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}' WHERE Id IN ({idsCsv})");
        }

        var pendingLogs = allPendingLogs.Where(l => SyncedTables.Contains(l.TableName)).ToList();
        var pendingCount = pendingLogs.Count;
        
        if (pendingCount == 0)
        {
            UpdateStatus(settings.LastSyncTime, 0, true, "Synced");
            return;
        }

        // In Multi-Computer mode, verify device authorization immediately before pushing data to cloud
        if (settings.MultiComputerEnabled && _pairingService != null && !string.IsNullOrEmpty(settings.StationId) && !string.IsNullOrEmpty(settings.MachineId))
        {
            var (isAuthorized, reason) = await _pairingService.CheckDeviceAuthorizationAsync(settings.StationId, settings.MachineId);
            if (!isAuthorized)
            {
                _logger.Warning("PerformPushAsync aborted: Workstation {MachineId} authorization is revoked: {Reason}. Local pending changes ({Count}) retained safely.",
                    settings.MachineId, reason, pendingCount);
                UpdateStatus(settings.LastSyncTime, pendingCount, false, $"Access Revoked: {reason}");
                return;
            }
        }

        UpdateStatus(settings.LastSyncTime, pendingCount, true, "Syncing...");

        // Load all existing SyncIdMappings into memory for FK translation
        var allMappings = await context.SyncIdMappings.AsNoTracking().ToListAsync();
        // Map: (TableName, LocalId) → RemoteGuid
        var localToGuid = new Dictionary<string, Dictionary<int, string>>();
        foreach (var m in allMappings)
        {
            if (!localToGuid.ContainsKey(m.TableName))
                localToGuid[m.TableName] = new Dictionary<int, string>();
            localToGuid[m.TableName][m.LocalId] = m.RemoteGuid;
        }

        // Track which SyncGuids have been confirmed pushed to Supabase in this cycle
        var pushedGuids = new HashSet<string>();

        // Group by TableName and order by dependency so parent tables are pushed before child tables
        var groups = pendingLogs
            .GroupBy(l => l.TableName)
            .OrderBy(g => GetPushOrderIndex(g.Key));

        foreach (var group in groups)
        {
            var tableName = group.Key;
            
            // 1. Process Deletes first — target by SyncGuid with verified tombstone and immediate acknowledgment
            var deletes = group.Where(l => l.Operation == "DELETE").ToList();
            foreach (var del in deletes)
            {
                var targetRecordGuid = GetExistingSyncGuid(localToGuid, tableName, del.RecordId);
                if (string.IsNullOrEmpty(targetRecordGuid))
                {
                    targetRecordGuid = del.RecordGuid;
                }

                if (string.IsNullOrEmpty(targetRecordGuid))
                {
                    _logger.Warning("Push DELETE: No SyncGuid or RecordGuid mapping found for {Table} LocalId={Id}. Acknowledging local log to avoid infinite loop.", tableName, del.RecordId);
                    await context.Database.ExecuteSqlRawAsync(
                        "UPDATE SyncChangeLogs SET IsSynced = 1, SyncedAt = {0} WHERE Id = {1}",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), del.Id);
                    continue;
                }

                // Step 1: Ensure the tombstone is confirmed in SyncChangeLogs
                bool tombstoneOk = await RecordSyncChangeLogTombstoneAsync(del, targetRecordGuid, settings);
                if (!tombstoneOk)
                {
                    _logger.Warning("Push DELETE: Tombstone unconfirmed for {Table} SyncGuid={Guid}. Retaining pending.", tableName, targetRecordGuid);
                    continue; // Retain pending
                }

                // Step 2: Execute remote DELETE for intended record
                var response = await _httpClient.SendRequestAsync(HttpMethod.Delete, $"{tableName}?SyncGuid=eq.{targetRecordGuid}");
                bool deleteConfirmed = false;

                if (response.IsSuccessStatusCode)
                {
                    deleteConfirmed = true;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // HTTP 404 safeguard: Verify target row genuinely absent before treating as success
                    try
                    {
                        var verifyGet = await _httpClient.SendRequestAsync(HttpMethod.Get, $"{tableName}?SyncGuid=eq.{targetRecordGuid}&select=SyncGuid&limit=1");
                        if (verifyGet.IsSuccessStatusCode)
                        {
                            var getBody = await verifyGet.Content.ReadAsStringAsync();
                            if (string.IsNullOrWhiteSpace(getBody) || getBody.Trim() == "[]")
                            {
                                _logger.Information("Push DELETE: Target row SyncGuid={Guid} verified absent from Supabase {Table}. Idempotent deletion confirmed.", targetRecordGuid, tableName);
                                deleteConfirmed = true;
                            }
                            else
                            {
                                _logger.Warning("Push DELETE: Row exists in {Table} despite 404 on DELETE. Retaining pending.", tableName);
                            }
                        }
                        else
                        {
                            _logger.Warning("Push DELETE: Verification GET returned {StatusCode} for table {Table}. Preserving error.", verifyGet.StatusCode, tableName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Push DELETE: Exception during 404 verification for {Table} SyncGuid={Guid}", tableName, targetRecordGuid);
                    }
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.Warning("Failed to delete record SyncGuid={Guid} in Supabase table {Table} (HTTP {StatusCode}): {Error}",
                        targetRecordGuid, tableName, response.StatusCode, error);
                }

                if (!deleteConfirmed)
                {
                    continue; // Retain pending
                }

                // Step 3: Atomically update local synchronization state in SQLite transaction
                try
                {
                    using var tx = await context.Database.BeginTransactionAsync();

                    var mappingToRemove = await context.SyncIdMappings
                        .FirstOrDefaultAsync(m => m.TableName == tableName && (m.LocalId == del.RecordId || m.RemoteGuid == targetRecordGuid));
                    if (mappingToRemove != null)
                    {
                        context.SyncIdMappings.Remove(mappingToRemove);
                        await context.SaveChangesAsync();
                    }

                    await context.Database.ExecuteSqlRawAsync(
                        "UPDATE SyncChangeLogs SET IsSynced = 1, SyncedAt = {0} WHERE Id = {1}",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"), del.Id);

                    await tx.CommitAsync();
                    _logger.Debug("Push DELETE: Successfully acknowledged local DELETE for {Table} LocalId={Id} RecordGuid={Guid}", tableName, del.RecordId, targetRecordGuid);
                }
                catch (Exception txEx)
                {
                    _logger.Error(txEx, "Failed to atomically acknowledge local DELETE for {Table} LocalId={Id}. Retryability preserved.", tableName, del.RecordId);
                }
            }

            // 2. Process Upserts (inserts/updates) in a batch
            var upsertLogs = group.Where(l => l.Operation == "INSERT" || l.Operation == "UPDATE").ToList();
            if (upsertLogs.Count > 0)
            {
                // Filter to get only the latest operation per record to avoid redundant requests
                var latestOps = upsertLogs
                    .GroupBy(l => l.RecordId)
                    .Select(g => g.Last())
                    .ToList();

                var recordsToUpsert = new List<Dictionary<string, object?>>();

                var entityType = context.Model.GetEntityTypes().FirstOrDefault(t => t.GetTableName() == tableName);
                if (entityType != null)
                {
                    // ── Collect parent records that need to be force-pushed ──
                    // Key: parentTableName → Dict of (localId → record dict ready for upsert)
                    var missingParents = new Dictionary<string, Dictionary<int, Dictionary<string, object?>>>();

                    foreach (var op in latestOps)
                    {
                        var record = await context.FindAsync(entityType.ClrType, op.RecordId);
                        if (record != null)
                        {
                            var dict = GetDatabaseValues(context, record);

                            // ── Assign or retrieve SyncGuid ──
                            var syncGuid = await GetOrCreateSyncGuidAsync(context, localToGuid, tableName, op.RecordId, settings);
                            dict["SyncGuid"] = syncGuid;

                            // ── Translate FK integer IDs → parent SyncGuids ──
                            if (FkConfigByTable.TryGetValue(tableName, out var fkMappings))
                            {
                                foreach (var fk in fkMappings)
                                {
                                    if (dict.TryGetValue(fk.FkProperty, out var fkVal) && fkVal != null)
                                    {
                                        var localFkId = Convert.ToInt32(fkVal);
                                        // For nullable FKs, 0 means null
                                        if (localFkId == 0) continue;

                                        var parentGuid = await GetOrCreateSyncGuidAsync(context, localToGuid, fk.ReferencedTable, localFkId, settings);
                                        // Replace the integer FK with the parent's SyncGuid
                                        dict[fk.FkProperty] = parentGuid;

                                        // Check if this parent was already pushed in this cycle or is in the current pending batch
                                        if (!pushedGuids.Contains(parentGuid))
                                        {
                                            // Check if the parent is in the current pending logs (will be pushed as part of its own group)
                                            var parentIsPending = pendingLogs.Any(l => l.TableName == fk.ReferencedTable && l.RecordId == localFkId && !l.IsSynced);
                                            if (!parentIsPending)
                                            {
                                                // Parent has no pending sync log — it was already synced before (or never queued).
                                                // We need to force-push it to ensure it exists in Supabase.
                                                if (!missingParents.ContainsKey(fk.ReferencedTable))
                                                    missingParents[fk.ReferencedTable] = new Dictionary<int, Dictionary<string, object?>>();

                                                if (!missingParents[fk.ReferencedTable].ContainsKey(localFkId))
                                                {
                                                    var parentEntityType = context.Model.GetEntityTypes().FirstOrDefault(t => t.GetTableName() == fk.ReferencedTable);
                                                    if (parentEntityType != null)
                                                    {
                                                        var parentRecord = await context.FindAsync(parentEntityType.ClrType, localFkId);
                                                        if (parentRecord != null)
                                                        {
                                                            var parentDict = GetDatabaseValues(context, parentRecord);
                                                            parentDict["SyncGuid"] = parentGuid;
                                                            parentDict["station_id"] = settings.StationId;
                                                            parentDict["local_id"] = localFkId;
                                                            parentDict["machine_id"] = settings.MachineId;
                                                            parentDict["updated_at"] = SyncTimestampHelper.FormatUtcIso(DateTime.UtcNow);

                                                            // Also translate any grandparent FKs on the parent record
                                                            if (FkConfigByTable.TryGetValue(fk.ReferencedTable, out var parentFkMappings))
                                                            {
                                                                foreach (var parentFk in parentFkMappings)
                                                                {
                                                                    if (parentDict.TryGetValue(parentFk.FkProperty, out var parentFkVal) && parentFkVal != null)
                                                                    {
                                                                        var grandparentLocalId = Convert.ToInt32(parentFkVal);
                                                                        if (grandparentLocalId == 0) continue;
                                                                        var grandparentGuid = await GetOrCreateSyncGuidAsync(context, localToGuid, parentFk.ReferencedTable, grandparentLocalId, settings);
                                                                        parentDict[parentFk.FkProperty] = grandparentGuid;
                                                                    }
                                                                }
                                                            }

                                                            missingParents[fk.ReferencedTable][localFkId] = parentDict;
                                                            _logger.Information("Will force-push missing parent {Table} LocalId={Id} SyncGuid={Guid}", fk.ReferencedTable, localFkId, parentGuid);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            // ── Add station metadata & dynamic JSON serialization ──
                            dict["station_id"] = settings.StationId;
                            dict["local_id"] = op.RecordId > 0
                                ? op.RecordId
                                : (record is Setting settingForId && settingForId.SettingId > 0 ? settingForId.SettingId : op.RecordId);
                            dict["machine_id"] = settings.MachineId;
                            dict["updated_at"] = SyncTimestampHelper.FormatUtcIso(DateTime.UtcNow);

                            if (record is PaymentCollection pcPush)
                            {
                                var dynItems = await context.PaymentCollectionItems.Where(i => i.PaymentId == pcPush.PaymentId).ToListAsync();
                                if (dynItems.Count > 0)
                                {
                                    dict["DynamicItemsJson"] = JsonConvert.SerializeObject(dynItems.Select(i => new
                                    {
                                        i.TransactionId,
                                        i.CollectionTypeCode,
                                        i.Amount,
                                        i.Tid,
                                        i.Batch,
                                        i.Slot,
                                        i.Otp,
                                        i.IsSyntheticSummary
                                    }));
                                }
                            }
                            else if (record is Setting settingRecord)
                            {
                                var activeTanks = await context.TankDefinitions.ToListAsync();
                                foreach (var t in activeTanks)
                                {
                                    t.Rate = settingRecord.GetRateFor(t.TankName, settingRecord.GetRateFor(t.FuelType, 0.0));
                                }
                                var activeCollTypes = await context.CollectionTypes.ToListAsync();
                                var activeFeatures = await context.AppFeatureSettings.ToListAsync();
                                var activePumpMappings = await context.PumpMappings.ToListAsync();

                                dict["TankDefinitionsJson"] = JsonConvert.SerializeObject(activeTanks);
                                dict["CollectionTypesJson"] = JsonConvert.SerializeObject(activeCollTypes);
                                dict["AppFeatureSettingsJson"] = JsonConvert.SerializeObject(activeFeatures);
                                dict["PumpMappingsJson"] = JsonConvert.SerializeObject(activePumpMappings);
                            }

                            recordsToUpsert.Add(dict);
                        }
                    }

                    // ── Force-push any missing parent records before the child batch ──
                    bool hasParentError = false;
                    foreach (var (parentTable, parentRecords) in missingParents)
                    {
                        if (parentRecords.Count > 0)
                        {
                            var parentList = parentRecords.Values.ToList();
                            var parentJson = JsonConvert.SerializeObject(parentList);
                            var parentResponse = await _httpClient.SendRequestAsync(HttpMethod.Post, parentTable, parentJson, isUpsert: true, onConflict: "SyncGuid");
                            if (!parentResponse.IsSuccessStatusCode)
                            {
                                var parentError = await parentResponse.Content.ReadAsStringAsync();
                                _logger.Error("Failed to force-push missing parent records for table {Table}: {Error}", parentTable, parentError);
                                if (parentError.Contains("PGRST205") || parentError.Contains("Could not find the table"))
                                {
                                    _logger.Warning("Parent table {ParentTable} does not exist in Supabase schema cache (PGRST205). Skipping push for dependent table {ChildTable} until Supabase schema migration is applied.", parentTable, tableName);
                                    hasParentError = true;
                                    break;
                                }
                                throw new HttpRequestException($"Supabase UPSERT failed for parent table {parentTable}: {parentError}");
                            }
                            // Mark these parent GUIDs as pushed
                            foreach (var parentDict in parentList)
                            {
                                if (parentDict.TryGetValue("SyncGuid", out var pg) && pg != null)
                                    pushedGuids.Add(pg.ToString()!);
                            }
                            _logger.Information("Force-pushed {Count} missing parent records to {Table}", parentList.Count, parentTable);
                        }
                    }

                    if (hasParentError) continue;
                }

                if (recordsToUpsert.Count > 0)
                {
                    // Split records to process deactivations (IsActive = false) first, avoiding unique constraint violations on active records.
                    var deactivations = recordsToUpsert.Where(r => r.TryGetValue("IsActive", out var val) && val is bool b && !b).ToList();
                    var activations = recordsToUpsert.Where(r => !(r.TryGetValue("IsActive", out var val) && val is bool b && !b)).ToList();

                    var batches = new List<List<Dictionary<string, object?>>>();
                    if (deactivations.Count > 0) batches.Add(deactivations);
                    if (activations.Count > 0) batches.Add(activations);

                    bool hasPushError = false;
                    foreach (var batch in batches)
                    {
                        foreach (var r in batch)
                        {
                            StripKnownMissingColumns(tableName, r);
                        }
                        var json = JsonConvert.SerializeObject(batch);
                        var response = await _httpClient.SendRequestAsync(HttpMethod.Post, tableName, json, isUpsert: true, onConflict: "SyncGuid");

                        // Auto-strip missing column retry loop (e.g. PGRST204: Could not find the 'AppFeatureSettingsJson' column)
                        while (!response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                        {
                            var errorStr = await response.Content.ReadAsStringAsync();
                            var colMatch = System.Text.RegularExpressions.Regex.Match(errorStr, @"Could not find the '([^']+)' column");
                            if (colMatch.Success)
                            {
                                var missingCol = colMatch.Groups[1].Value;
                                RecordKnownMissingColumn(tableName, missingCol);
                                _logger.Warning("Supabase table {Table} is missing column '{Col}'. Stripping and retrying push...", tableName, missingCol);
                                foreach (var r in batch)
                                {
                                    r.Remove(missingCol);
                                }
                                json = JsonConvert.SerializeObject(batch);
                                response = await _httpClient.SendRequestAsync(HttpMethod.Post, tableName, json, isUpsert: true, onConflict: "SyncGuid");
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (!response.IsSuccessStatusCode)
                        {
                            var error = await response.Content.ReadAsStringAsync();
                            _logger.Warning("Supabase UPSERT returned HTTP {StatusCode} for table {Table}: {Error}", response.StatusCode, tableName, error);

                            if (response.StatusCode == System.Net.HttpStatusCode.Conflict || error.Contains("duplicate key") || error.Contains("23505"))
                            {
                                var (conflictResolved, conflictErr) = await HandleUpsertConflictAsync(
                                    context, tableName, batch, localToGuid, settings, error);
                                if (!conflictResolved)
                                {
                                    _lastPushErrorMessage = conflictErr ?? $"Conflict pushing {tableName}: {error}";
                                    hasPushError = true;
                                    break;
                                }
                            }
                            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound || error.Contains("PGRST205") || error.Contains("Could not find the table"))
                            {
                                _logger.Information("Table {Table} is not present in Supabase schema (PGRST205). State is synchronized via JSON fields in parent tables.", tableName);
                                // Table not present in Supabase - allow change logs to be marked synced
                            }
                            else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest ||
                                response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity ||
                                error.Contains("PGRST") || error.Contains("column") || error.Contains("constraint"))
                            {
                                _logger.Warning("Skipping push for table {Table} due to Supabase schema/data issue until migration is applied: {Error}", tableName, error);
                                _lastPushErrorMessage = $"Push skipped for {tableName}: {error}";
                                hasPushError = true;
                                break;
                            }
                            else
                            {
                                _lastPushErrorMessage = $"Supabase UPSERT failed for {tableName}: {error}";
                                throw new HttpRequestException($"Supabase UPSERT failed for table {tableName}: {error}");
                            }
                        }
                    }

                    if (hasPushError) continue;

                    // Mark all upserted GUIDs as pushed
                    foreach (var rec in recordsToUpsert)
                    {
                        if (rec.TryGetValue("SyncGuid", out var sg) && sg != null)
                            pushedGuids.Add(sg.ToString()!);
                    }
                }
            }

            // Mark non-DELETE log entries as synced
            var upsertLogIds = group.Where(l => l.Operation != "DELETE").Select(l => l.Id).ToList();
            if (upsertLogIds.Count > 0)
            {
                var idsCsv = string.Join(",", upsertLogIds);
                await context.Database.ExecuteSqlRawAsync(
                    $"UPDATE SyncChangeLogs SET IsSynced = 1, SyncedAt = '{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}' WHERE Id IN ({idsCsv})");
            }
        }

        // Save any new SyncIdMappings that were created during push
        await context.SaveChangesAsync();

        var remainingPending = await context.SyncChangeLogs.CountAsync(l => !l.IsSynced);
        if (remainingPending > 0 && !string.IsNullOrWhiteSpace(_lastPushErrorMessage))
        {
            UpdateStatus(settings.LastSyncTime, remainingPending, false, $"Sync conflict/error ({remainingPending} pending): {_lastPushErrorMessage}");
            return;
        }

        _lastPushErrorMessage = null;
        // Update last sync time in UTC (consistent with PerformPullAsync)
        settings.LastSyncTime = DateTime.UtcNow;
        await _configService.SaveSettingsAsync(settings);

        UpdateStatus(settings.LastSyncTime, remainingPending, true, "Synced");
    }

    /// <summary>
    /// Safely verifies and reconciles HTTP 409 / PostgreSQL 23505 conflicts during batch push.
    /// Never blindly marks conflicting writes synced and never blindly deactivates valid remote assignments.
    /// </summary>
    private async Task<(bool Resolved, string? ErrorMessage)> HandleUpsertConflictAsync(
        FuelProDbContext context,
        string tableName,
        List<Dictionary<string, object?>> batch,
        Dictionary<string, Dictionary<int, string>> localToGuid,
        SyncSettings settings,
        string rawError)
    {
        if (string.Equals(tableName, "DsmPumpAssignments", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleDsmPumpAssignmentConflictBatchAsync(context, batch, localToGuid, settings, rawError);
        }

        // For non-assignment tables, verify that every SyncGuid in this batch actually exists remotely for this station.
        foreach (var rec in batch)
        {
            var sg = rec.TryGetValue("SyncGuid", out var sgObj) ? sgObj?.ToString() : null;
            if (string.IsNullOrWhiteSpace(sg))
            {
                return (false, $"409 Conflict on {tableName} with missing SyncGuid: {rawError}");
            }

            var endpoint = $"{tableName}?SyncGuid=eq.{Uri.EscapeDataString(sg)}&station_id=eq.{Uri.EscapeDataString(settings.StationId)}&select=SyncGuid&limit=1";
            var verifyResp = await _httpClient.SendRequestAsync(HttpMethod.Get, endpoint);
            if (!verifyResp.IsSuccessStatusCode)
            {
                return (false, $"409 Conflict verification failed for {tableName} ({verifyResp.StatusCode}): {rawError}");
            }

            var verifyJson = await verifyResp.Content.ReadAsStringAsync();
            var rows = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(verifyJson, PullJsonSettings);
            if (rows == null || rows.Count == 0)
            {
                return (false, $"409 Conflict on {tableName} SyncGuid={sg}: another unique key blocked insert and record was not found by SyncGuid.");
            }
        }

        _logger.Information("Verified all {Count} conflicting records exist in Supabase for table {Table}; proceeding.", batch.Count, tableName);
        return (true, null);
    }

    private async Task<(bool Resolved, string? ErrorMessage)> HandleDsmPumpAssignmentConflictBatchAsync(
        FuelProDbContext context,
        List<Dictionary<string, object?>> batch,
        Dictionary<string, Dictionary<int, string>> localToGuid,
        SyncSettings settings,
        string rawError)
    {
        foreach (var rec in batch)
        {
            var localSyncGuid = rec.TryGetValue("SyncGuid", out var sgObj) ? sgObj?.ToString() : null;
            int localId = rec.TryGetValue("local_id", out var lidObj) && lidObj != null ? Convert.ToInt32(lidObj) : 0;
            int pumpId = rec.TryGetValue("PumpId", out var pObj) && pObj != null ? Convert.ToInt32(pObj) : 0;
            string shiftType = rec.TryGetValue("ShiftType", out var shObj) ? shObj?.ToString()?.Trim() ?? "" : "";
            string? remoteDsmGuid = rec.TryGetValue("DsmUserId", out var dObj) ? dObj?.ToString() : null;
            bool isActive = rec.TryGetValue("IsActive", out var aObj) && aObj != null && Convert.ToBoolean(aObj);

            if (pumpId <= 0 || string.IsNullOrWhiteSpace(shiftType))
            {
                return (false, $"DsmPumpAssignments conflict on invalid Pump/Shift ({pumpId}/{shiftType}): {rawError}");
            }

            // 1. Check if this exact assignment SyncGuid already exists remotely
            if (!string.IsNullOrWhiteSpace(localSyncGuid))
            {
                var selfResp = await _httpClient.SendRequestAsync(
                    HttpMethod.Get,
                    $"DsmPumpAssignments?SyncGuid=eq.{Uri.EscapeDataString(localSyncGuid)}&station_id=eq.{Uri.EscapeDataString(settings.StationId)}&limit=1");
                if (selfResp.IsSuccessStatusCode)
                {
                    var selfRows = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(
                        await selfResp.Content.ReadAsStringAsync(), PullJsonSettings);
                    if (selfRows != null && selfRows.Count > 0)
                    {
                        continue;
                    }
                }
            }

            if (!isActive)
            {
                return (false, $"DsmPumpAssignments deactivation conflict on Pump {pumpId} Shift {shiftType}: {rawError}");
            }

            // 2. Inspect conflicting active remote row(s) on (station_id, PumpId, ShiftType, IsActive=true)
            var activeEndpoint = $"DsmPumpAssignments?station_id=eq.{Uri.EscapeDataString(settings.StationId)}&PumpId=eq.{pumpId}&ShiftType=eq.{Uri.EscapeDataString(shiftType)}&IsActive=eq.true";
            var activeResp = await _httpClient.SendRequestAsync(HttpMethod.Get, activeEndpoint);
            if (!activeResp.IsSuccessStatusCode)
            {
                return (false, $"Unable to inspect conflicting remote assignment for Pump {pumpId} Shift {shiftType} ({activeResp.StatusCode}).");
            }

            var remoteActives = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(
                await activeResp.Content.ReadAsStringAsync(), PullJsonSettings) ?? new List<Dictionary<string, object?>>();

            if (remoteActives.Count == 0)
            {
                // Transient race resolved: retry once
                var retryResp = await _httpClient.SendRequestAsync(
                    HttpMethod.Post, "DsmPumpAssignments", JsonConvert.SerializeObject(new[] { rec }), isUpsert: true, onConflict: "SyncGuid");
                if (!retryResp.IsSuccessStatusCode)
                {
                    return (false, $"DsmPumpAssignments retry failed for Pump {pumpId} Shift {shiftType}: {await retryResp.Content.ReadAsStringAsync()}");
                }
                continue;
            }

            // 3. Check if remote active row is an idempotent identical match
            if (remoteActives.Count == 1)
            {
                var r0 = remoteActives[0];
                var rGuid = GetDictString(r0, "SyncGuid");
                var rDsmGuid = GetDictString(r0, "DsmUserId");
                var rConnJson = GetDictString(r0, "ConnectedPumpIdsJson");
                var lConnJson = rec.TryGetValue("ConnectedPumpIdsJson", out var lcObj) ? lcObj?.ToString() : null;

                if (!string.IsNullOrWhiteSpace(rGuid) &&
                    string.Equals(rDsmGuid, remoteDsmGuid, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(rConnJson ?? "", lConnJson ?? "", StringComparison.OrdinalIgnoreCase) &&
                    localId > 0)
                {
                    var existingMap = await context.SyncIdMappings.FirstOrDefaultAsync(m => m.TableName == "DsmPumpAssignments" && m.LocalId == localId);
                    if (existingMap != null)
                    {
                        existingMap.RemoteGuid = rGuid;
                    }
                    else
                    {
                        context.SyncIdMappings.Add(new SyncIdMapping { TableName = "DsmPumpAssignments", LocalId = localId, RemoteGuid = rGuid });
                    }
                    if (!localToGuid.ContainsKey("DsmPumpAssignments"))
                        localToGuid["DsmPumpAssignments"] = new Dictionary<int, string>();
                    localToGuid["DsmPumpAssignments"][localId] = rGuid;
                    await context.SaveChangesAsync();
                    continue;
                }
            }

            // 4. Strict authority verification before deactivating any conflicting remote assignment:
            // Only supersede remote row R if we verify locally that R is already deactivated/superseded
            // for this station, pump, shift, DSM, and date (and R was not updated later by another PC).
            var localNewEntity = localId > 0
                ? await context.DsmPumpAssignments.AsNoTracking().FirstOrDefaultAsync(a => a.DsmPumpAssignmentId == localId)
                : null;
            DateTime localNewUtc = localNewEntity != null
                ? SyncTimestampHelper.ToUtcDateTime(localNewEntity.AssignedDate, SyncTimestampSemantics.LocalInstant)
                : DateTime.MinValue;

            bool allConflictsSafelySuperseded = true;
            foreach (var r in remoteActives)
            {
                var rGuid = GetDictString(r, "SyncGuid");
                var rStation = GetDictString(r, "station_id") ?? "";
                var rDsmGuid = GetDictString(r, "DsmUserId");
                var rMachine = GetDictString(r, "machine_id");

                if (string.IsNullOrWhiteSpace(rGuid) ||
                    !string.Equals(rStation.Trim(), settings.StationId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    allConflictsSafelySuperseded = false;
                    break;
                }

                // Resolve local deactivated counterpart for R
                DsmPumpAssignment? localDeactivated = null;
                var rMap = await context.SyncIdMappings.AsNoTracking()
                    .FirstOrDefaultAsync(m => m.TableName == "DsmPumpAssignments" && m.RemoteGuid == rGuid);
                if (rMap != null)
                {
                    var mappedEntity = await context.DsmPumpAssignments.AsNoTracking()
                        .FirstOrDefaultAsync(a => a.DsmPumpAssignmentId == rMap.LocalId);
                    if (mappedEntity != null && !mappedEntity.IsActive && mappedEntity.PumpId == pumpId &&
                        string.Equals(mappedEntity.ShiftType, shiftType, StringComparison.OrdinalIgnoreCase))
                    {
                        localDeactivated = mappedEntity;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(rDsmGuid))
                {
                    var dsmMap = await context.SyncIdMappings.AsNoTracking()
                        .FirstOrDefaultAsync(m => m.TableName == "DsmUsers" && m.RemoteGuid == rDsmGuid);
                    if (dsmMap != null)
                    {
                        localDeactivated = await context.DsmPumpAssignments.AsNoTracking()
                            .Where(a => !a.IsActive && a.DsmUserId == dsmMap.LocalId && a.PumpId == pumpId && a.ShiftType.ToLower() == shiftType.ToLower())
                            .OrderByDescending(a => a.AssignedDate)
                            .FirstOrDefaultAsync();
                    }
                }

                DateTime remoteAssignedUtc = r.TryGetValue("AssignedDate", out var raVal) && raVal != null
                    ? SyncTimestampHelper.ToUtcDateTime(SyncTimestampHelper.ParseForPull(typeof(DsmPumpAssignment), "AssignedDate", raVal), SyncTimestampSemantics.LocalInstant)
                    : DateTime.MaxValue;
                DateTime remoteUpdatedUtc = r.TryGetValue("updated_at", out var ruVal) && ruVal != null && DateTime.TryParse(ruVal.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsedRu)
                    ? parsedRu
                    : DateTime.MinValue;

                bool sameMachine = string.Equals(rMachine, settings.MachineId, StringComparison.OrdinalIgnoreCase);
                bool remoteIsNotNewer = localNewEntity != null && remoteAssignedUtc <= localNewUtc && (sameMachine || remoteUpdatedUtc <= localNewUtc);

                if (localDeactivated == null || !remoteIsNotNewer)
                {
                    allConflictsSafelySuperseded = false;
                    _logger.Warning(
                        "Refusing to deactivate conflicting remote active DsmPumpAssignment SyncGuid={RemoteGuid} on Pump {Pump} Shift {Shift} (LocalDeactivatedFound={Found}, SameMachine={SameMachine}, RemoteAssignedUtc={RemoteUtc}, LocalNewUtc={LocalUtc}).",
                        rGuid, pumpId, shiftType, localDeactivated != null, sameMachine, remoteAssignedUtc, localNewUtc);
                    break;
                }

                var patchJson = JsonConvert.SerializeObject(new
                {
                    IsActive = false,
                    updated_at = SyncTimestampHelper.FormatUtcIso(DateTime.UtcNow)
                });
                var patchResp = await _httpClient.SendRequestAsync(
                    HttpMethod.Patch,
                    $"DsmPumpAssignments?SyncGuid=eq.{Uri.EscapeDataString(rGuid)}&station_id=eq.{Uri.EscapeDataString(settings.StationId)}",
                    patchJson);
                if (!patchResp.IsSuccessStatusCode)
                {
                    allConflictsSafelySuperseded = false;
                    break;
                }
            }

            if (!allConflictsSafelySuperseded)
            {
                return (false, $"Active remote assignment conflict on Pump {pumpId} Shift {shiftType}; remote record could not be safely verified as superseded.");
            }

            // Re-attempt upserting the new active assignment after verified stale cleanup
            var retryInsert = await _httpClient.SendRequestAsync(
                HttpMethod.Post, "DsmPumpAssignments", JsonConvert.SerializeObject(new[] { rec }), isUpsert: true, onConflict: "SyncGuid");
            if (!retryInsert.IsSuccessStatusCode)
            {
                return (false, $"DsmPumpAssignments upsert retry failed on Pump {pumpId} Shift {shiftType}: {await retryInsert.Content.ReadAsStringAsync()}");
            }
        }

        return (true, null);
    }

    // ═══════════════════════════════════════════════════════════════════
    // PULL SYNC — Supabase → Local changes (GUID-based)
    // ═══════════════════════════════════════════════════════════════════

    private async Task PerformPullAsync(SyncSettings settings, bool forcePull = false)
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var pullStartTime = DateTime.UtcNow;
        var upperWatermark = pullStartTime.ToString(SyncTimestampHelper.UtcFormat, CultureInfo.InvariantCulture);
        bool pullSucceededCompletely = true;

        UpdateStatus(settings.LastSyncTime, 0, true, "Pulling updates...");

        // Load ALL existing ID mappings into memory for fast bidirectional lookup
        // Map: (TableName, RemoteGuid) → LocalId  AND  (TableName, LocalId) → RemoteGuid
        var allMappings = await context.SyncIdMappings.AsNoTracking().ToListAsync();
        var guidToLocal = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        var localToGuid = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in allMappings)
        {
            if (!guidToLocal.ContainsKey(m.TableName))
                guidToLocal[m.TableName] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            guidToLocal[m.TableName][m.RemoteGuid] = m.LocalId;

            if (!localToGuid.ContainsKey(m.TableName))
                localToGuid[m.TableName] = new Dictionary<int, string>();
            localToGuid[m.TableName][m.LocalId] = m.RemoteGuid;
        }

        var effectivePullTime = settings.LastPullTime == default ? settings.LastSyncTime : settings.LastPullTime;
        var utcEffective = SyncTimestampHelper.ToUtcDateTime(effectivePullTime, SyncTimestampSemantics.LocalInstant);
        bool operationalDataChanged = false;
        bool configOrProfileDsmChanged = false;
        bool pettyCashChanged = false;
        bool inventoryChanged = false;
        bool payrollChanged = false;
        bool debtorChanged = false;

        foreach (var tableDef in PullTableOrder)
        {
            if (tableDef.TableName == "SyncChangeLogs")
            {
                // Special handling for pulling delete propagation logs
                var logQueryCursor = (forcePull || utcEffective <= DateTime.MinValue.AddMinutes(5))
                    ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
                    : utcEffective.AddMinutes(-5);
                var logQueryTime = logQueryCursor.ToString(SyncTimestampHelper.UtcFormat, CultureInfo.InvariantCulture);
                var logResponse = await _httpClient.SendRequestAsync(HttpMethod.Get,
                    $"SyncChangeLogs?station_id=eq.{settings.StationId}&Timestamp=gt.{logQueryTime}&Timestamp=lte.{upperWatermark}");
                if (logResponse.Headers.Date.HasValue)
                {
                    pullStartTime = logResponse.Headers.Date.Value.UtcDateTime;
                    // HTTP Date headers have 1-second resolution; extend upperWatermark by 1s so sub-second writes in the current second are not skipped
                    upperWatermark = pullStartTime.AddSeconds(1).ToString(SyncTimestampHelper.UtcFormat, CultureInfo.InvariantCulture);
                }
                if (logResponse.IsSuccessStatusCode)
                {
                    var logJson = await logResponse.Content.ReadAsStringAsync();
                    var logs = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(logJson, PullJsonSettings);
                    if (logs != null && logs.Count > 0)
                    {
                        FuelProDbContext.BypassTracking = true;
                        try
                        {
                            int deletedOperationalCount = 0;
                            foreach (var logDict in logs)
                            {
                                if (logDict.TryGetValue("machine_id", out var machineIdObj) && machineIdObj?.ToString() == settings.MachineId)
                                {
                                    // Skip logs generated by ourselves
                                    continue;
                                }

                                if (!logDict.TryGetValue("Operation", out var opObj) || opObj?.ToString() != "DELETE") continue;
                                if (!logDict.TryGetValue("TableName", out var tableObj) || tableObj == null) continue;
                                if (!logDict.TryGetValue("RecordGuid", out var guidObj) || guidObj == null) continue;

                                var deletedTableName = tableObj.ToString()!;
                                var deletedRecordGuid = guidObj.ToString()!;

                                // Find entity type
                                var targetEntityType = context.Model.GetEntityTypes().FirstOrDefault(t => t.GetTableName() == deletedTableName);
                                if (targetEntityType == null) continue;

                                // Find mapping
                                var mapping = await context.SyncIdMappings
                                    .FirstOrDefaultAsync(m => m.TableName == deletedTableName && m.RemoteGuid == deletedRecordGuid);
                                if (mapping != null)
                                {
                                    var localId = mapping.LocalId;
                                    var entity = await context.FindAsync(targetEntityType.ClrType, localId);
                                    if (entity != null)
                                    {
                                        context.Remove(entity);
                                        if (OperationalDsmNotificationTables.Contains(deletedTableName))
                                        {
                                            deletedOperationalCount++;
                                        }
                                        _logger.Information("Pull DELETE: Deleted local record from {Table} with LocalId={Id} (SyncGuid={Guid})", deletedTableName, localId, deletedRecordGuid);
                                    }
                                    context.SyncIdMappings.Remove(mapping);
                                }
                            }
                            await context.SaveChangesAsync();
                            if (deletedOperationalCount > 0)
                            {
                                operationalDataChanged = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Error processing pull deletes from SyncChangeLogs");
                            pullSucceededCompletely = false;
                        }
                        finally
                        {
                            FuelProDbContext.BypassTracking = false;
                        }
                    }
                }
                else
                {
                    var error = await logResponse.Content.ReadAsStringAsync();
                    _logger.Warning("Supabase GET failed for SyncChangeLogs: {Error}", error);
                    pullSucceededCompletely = false;
                }
                continue;
            }

            var entityType = context.Model.GetEntityTypes()
                .FirstOrDefault(t => t.GetTableName() == tableDef.TableName);
            if (entityType == null) continue;

            var pkProp = entityType.FindPrimaryKey()?.Properties.FirstOrDefault();
            if (pkProp == null) continue;

            // Determine the query time for this specific table.
            // If forcePull is requested or table has no mappings yet, perform a full sync from beginning.
            var tableHasMappings = guidToLocal.TryGetValue(tableDef.TableName, out var mappings) && mappings.Count > 0;
            var queryCursor = (forcePull || !tableHasMappings || utcEffective <= DateTime.MinValue.AddMinutes(5))
                ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
                : utcEffective.AddMinutes(-5);
            var tableQueryTime = queryCursor.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);

            // Fetch records updated since tableQueryTime for this StationId (strictly isolated per station)
            var stationFilter = !string.IsNullOrWhiteSpace(settings.StationId) ? $"station_id=eq.{settings.StationId.Trim()}" : "station_id=is.null";

            const int pageSize = 500;
            string? cursorTimestamp = null;
            string? cursorGuid = null;
            bool isFallbackKeyPagination = false;
            var records = new List<Dictionary<string, object?>>();
            bool tableFetchSucceeded = true;

            while (true)
            {
                string pagedEndpoint;
                if (!isFallbackKeyPagination)
                {
                    if (cursorTimestamp == null)
                    {
                        pagedEndpoint = $"{tableDef.TableName}?{stationFilter}&updated_at=gt.{tableQueryTime}&updated_at=lte.{upperWatermark}&order=updated_at.asc,SyncGuid.asc&limit={pageSize}";
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(cursorGuid))
                        {
                            _logger.Warning("Aborting keyset pagination for table {Table} because boundary SyncGuid is empty; retaining pull watermark.", tableDef.TableName);
                            tableFetchSucceeded = false;
                            pullSucceededCompletely = false;
                            break;
                        }
                        pagedEndpoint = $"{tableDef.TableName}?{stationFilter}&updated_at=gte.{cursorTimestamp}&updated_at=lte.{upperWatermark}&or=(updated_at.gt.{cursorTimestamp},SyncGuid.gt.{cursorGuid})&order=updated_at.asc,SyncGuid.asc&limit={pageSize}";
                    }
                }
                else
                {
                    // Fallback keyset pagination using SyncGuid alone
                    if (cursorGuid == null)
                    {
                        pagedEndpoint = $"{tableDef.TableName}?{stationFilter}&order=SyncGuid.asc&limit={pageSize}";
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(cursorGuid))
                        {
                            _logger.Warning("Aborting SyncGuid fallback pagination for table {Table} because cursor SyncGuid is empty; retaining pull watermark.", tableDef.TableName);
                            tableFetchSucceeded = false;
                            pullSucceededCompletely = false;
                            break;
                        }
                        pagedEndpoint = $"{tableDef.TableName}?{stationFilter}&SyncGuid=gt.{cursorGuid}&order=SyncGuid.asc&limit={pageSize}";
                    }
                }

                var response = await _httpClient.SendRequestAsync(HttpMethod.Get, pagedEndpoint);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    if (!isFallbackKeyPagination && error.Contains("42703"))
                    {
                        if (tableDef.TableName != "Settings")
                        {
                            _logger.Information("Table {Table} returned 42703 (missing updated_at); falling back to keyset pagination on SyncGuid.", tableDef.TableName);
                            isFallbackKeyPagination = true;
                            cursorTimestamp = null;
                            cursorGuid = null;
                            records.Clear();
                            continue;
                        }
                        else
                        {
                            _logger.Warning("Table {Table} lacks updated_at and a usable unique pagination key (SyncGuid). Ingestion aborted safely to prevent truncation.", tableDef.TableName);
                            tableFetchSucceeded = false;
                            pullSucceededCompletely = false;
                            break;
                        }
                    }

                    if (error.Contains("PGRST205") || error.Contains("Could not find the table"))
                    {
                        _logger.Information("Table {Table} not present in Supabase schema (PGRST205); skipping pull.", tableDef.TableName);
                    }
                    else
                    {
                        _logger.Warning("Supabase GET failed for table {Table} with query {Endpoint}: {Error}", tableDef.TableName, pagedEndpoint, error);
                        tableFetchSucceeded = false;
                        pullSucceededCompletely = false;
                    }
                    break;
                }

                var json = await response.Content.ReadAsStringAsync();
                var pageRawRecords = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(json, PullJsonSettings);
                if (pageRawRecords == null || pageRawRecords.Count == 0)
                {
                    break;
                }

                foreach (var r in pageRawRecords)
                {
                    records.Add(new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase));
                }

                var lastRec = pageRawRecords[pageRawRecords.Count - 1];
                string? lastTimestamp = null;
                string? lastGuid = null;

                if (TryGetDictValue(lastRec, "updated_at", out var utObj) && utObj != null)
                {
                    if (SyncTimestampHelper.TryParseRemoteUtcCursor(utObj, out var parsedTs, out _))
                    {
                        lastTimestamp = parsedTs;
                    }
                    else
                    {
                        lastTimestamp = utObj.ToString()?.Trim();
                    }
                }

                if (TryGetDictValue(lastRec, "SyncGuid", out var sgObj) && sgObj != null)
                {
                    lastGuid = sgObj.ToString()?.Trim();
                }

                if (!isFallbackKeyPagination)
                {
                    if (lastTimestamp == cursorTimestamp && lastGuid == cursorGuid)
                    {
                        _logger.Warning("Keyset pagination cursor did not advance for table {Table} (updated_at={Timestamp}, SyncGuid={Guid}). Breaking pagination to avoid infinite loop.", tableDef.TableName, lastTimestamp, lastGuid);
                        break;
                    }

                    if (string.IsNullOrEmpty(lastTimestamp) || string.IsNullOrEmpty(lastGuid))
                    {
                        _logger.Warning("Boundary record in page for table {Table} is missing updated_at or SyncGuid. Keyset pagination cannot safely advance.", tableDef.TableName);
                        if (pageRawRecords.Count >= pageSize)
                        {
                            tableFetchSucceeded = false;
                            pullSucceededCompletely = false;
                        }
                        break;
                    }

                    cursorTimestamp = lastTimestamp;
                    cursorGuid = lastGuid;
                }
                else
                {
                    if (lastGuid == cursorGuid)
                    {
                        _logger.Warning("SyncGuid pagination cursor did not advance for table {Table} (SyncGuid={Guid}). Breaking pagination to avoid infinite loop.", tableDef.TableName, lastGuid);
                        break;
                    }

                    if (string.IsNullOrEmpty(lastGuid))
                    {
                        _logger.Warning("Boundary record in page for table {Table} is missing SyncGuid. Fallback pagination cannot safely advance.", tableDef.TableName);
                        if (pageRawRecords.Count >= pageSize)
                        {
                            tableFetchSucceeded = false;
                            pullSucceededCompletely = false;
                        }
                        break;
                    }

                    cursorGuid = lastGuid;
                }
            }

            if (!tableFetchSucceeded)
            {
                continue; // Skip processing this table since fetch failed or was incomplete
            }

            if (records.Count == 0) continue;

            if (!guidToLocal.ContainsKey(tableDef.TableName))
                guidToLocal[tableDef.TableName] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var tableMap = guidToLocal[tableDef.TableName];

            var newEntities = new List<(object Entity, string RemoteGuid)>();
            int appliedRecordCount = 0;

            FuelProDbContext.BypassTracking = true;
            try
            {
                foreach (var dict in records)
                {
                    // ── Get the SyncGuid from the pulled record ──
                    if (!TryGetDictValue(dict, "SyncGuid", out var syncGuidObj) || syncGuidObj == null) continue;
                    var remoteGuid = syncGuidObj.ToString()!;

                    // Skip records that this machine pushed (avoid re-importing our own changes)
                    if (TryGetDictValue(dict, "machine_id", out var machineIdObj) && machineIdObj != null)
                    {
                        if (machineIdObj.ToString() == settings.MachineId)
                        {
                            // Check if the record actually exists locally
                            bool existsLocally = false;
                            if (tableMap.TryGetValue(remoteGuid, out var pushedLocalId))
                            {
                                var localRecord = await context.FindAsync(entityType.ClrType, pushedLocalId);
                                if (localRecord != null)
                                {
                                    existsLocally = true;
                                }
                            }
                            
                            if (existsLocally)
                            {
                                continue;
                            }

                            // Check if this record was intentionally deleted locally on this machine
                            bool isDeletedLocally = await context.SyncChangeLogs
                                .AnyAsync(l => l.TableName == tableDef.TableName && l.RecordGuid == remoteGuid && l.Operation == "DELETE");

                            if (isDeletedLocally)
                            {
                                continue; // Never resurrect records deleted on this machine
                            }

                            // For PumpMappings: if we already have local pump mappings configured, do not re-import obsolete cloud mappings from this machine
                            if (tableDef.TableName == "PumpMappings" && await context.PumpMappings.AnyAsync())
                            {
                                _logger.Information("Pull: Skipping re-import of missing PumpMapping {SyncGuid} from this machine to prevent overwriting active layout.", remoteGuid);
                                continue;
                            }
                            // If it doesn't exist locally and was not deleted, we proceed to import it (e.g. fresh database install or tests)
                        }
                    }

                    // ── Step 1: Remap FK values from parent GUIDs to local IDs ──
                    bool fkFailed = false;
                    foreach (var fk in tableDef.ForeignKeys)
                    {
                        if (!TryGetDictValue(dict, fk.FkProperty, out var fkVal) || fkVal == null) continue;

                        var parentGuid = fkVal.ToString()!;
                        var isOilLogShiftReference = tableDef.TableName == "OilDefDailyLogs"
                            && fk.FkProperty == "ShiftId"
                            && fk.ReferencedTable == "Shifts";

                        // Oil-log shift references must be verified against this station remotely,
                        // even when an old local SyncIdMapping exists.
                        if (isOilLogShiftReference)
                        {
                            if (!Guid.TryParse(parentGuid, out _))
                            {
                                _logger.Warning("Pull: OilDefDailyLogs ShiftId is not a remote GUID; skipping record {Guid}.", remoteGuid);
                                fkFailed = true;
                                break;
                            }

                            var resolvedShiftId = await FetchAndImportMissingParentAsync(
                                context, guidToLocal, fk.ReferencedTable, parentGuid, settings);
                            if (!resolvedShiftId.HasValue)
                            {
                                _logger.Warning(
                                    "Pull: OilDefDailyLogs ShiftId {ShiftGuid} is missing or not associated with station {StationId}; skipping record {Guid}.",
                                    parentGuid, settings.StationId, remoteGuid);
                                fkFailed = true;
                                break;
                            }

                            dict[fk.FkProperty] = resolvedShiftId.Value;
                            continue;
                        }

                        // Check if it looks like a GUID (not a plain integer — backward compat)
                        if (Guid.TryParse(parentGuid, out _))
                        {
                            var refMap = guidToLocal.GetValueOrDefault(fk.ReferencedTable);
                            if (refMap != null && refMap.TryGetValue(parentGuid, out var localFkId))
                            {
                                dict[fk.FkProperty] = localFkId;
                            }
                            else
                            {
                                // Step 1a: Check if SyncIdMappings in local DB already has this mapping
                                var existingMapping = await context.SyncIdMappings
                                    .FirstOrDefaultAsync(m => m.TableName == fk.ReferencedTable && m.RemoteGuid == parentGuid);
                                if (existingMapping != null)
                                {
                                    if (!guidToLocal.ContainsKey(fk.ReferencedTable))
                                        guidToLocal[fk.ReferencedTable] = new Dictionary<string, int>();
                                    guidToLocal[fk.ReferencedTable][parentGuid] = existingMapping.LocalId;
                                    dict[fk.FkProperty] = existingMapping.LocalId;
                                    continue;
                                }

                                // Step 1b: On-demand fetch of missing parent record from Supabase
                                var resolvedParentId = await FetchAndImportMissingParentAsync(
                                    context, guidToLocal, fk.ReferencedTable, parentGuid, settings);

                                if (resolvedParentId.HasValue)
                                {
                                    dict[fk.FkProperty] = resolvedParentId.Value;
                                }
                                else
                                {
                                    _logger.Warning(
                                        "Pull: FK {Fk}={Val} in {Table} has no mapping in {Ref} and could not be fetched. Skipping record {Guid}.",
                                        fk.FkProperty, parentGuid, tableDef.TableName, fk.ReferencedTable, remoteGuid);
                                    fkFailed = true;
                                    break;
                                }
                            }
                        }
                        // else: it's already a local integer (legacy data), leave as-is
                    }
                    if (fkFailed)
                    {
                        pullSucceededCompletely = false;
                        continue;
                    }

                    // ── Step 2: Find or create the local entity ──
                    object? entity;
                    bool isNew = false;

                    if (tableMap.TryGetValue(remoteGuid, out var existingLocalId))
                    {
                        // We have a mapping — find the local record
                        entity = await context.FindAsync(entityType.ClrType, existingLocalId);
                        if (entity == null)
                        {
                            if (tableDef.TableName == "PumpMappings")
                            {
                                // Do not resurrect locally deleted pump mappings
                                continue;
                            }
                            // Mapping is stale (record was deleted locally) — re-create
                            entity = Activator.CreateInstance(entityType.ClrType);
                            if (entity == null) continue;
                            context.Add(entity);
                            isNew = true;
                        }
                    }
                    else
                    {
                        // No mapping — check if a matching record exists locally by business/unique key first
                        object? matchedLocalEntity = null;

                        if (tableDef.TableName == "PumpMappings")
                        {
                            var pIdObj = dict.Keys.FirstOrDefault(k => 
                                string.Equals(k, "PumpId", StringComparison.OrdinalIgnoreCase) || 
                                string.Equals(k, "pump_id", StringComparison.OrdinalIgnoreCase)) is string pKey ? dict[pKey] : null;
                            var nNoObj = dict.Keys.FirstOrDefault(k => 
                                string.Equals(k, "NozzleNumber", StringComparison.OrdinalIgnoreCase) || 
                                string.Equals(k, "nozzle_number", StringComparison.OrdinalIgnoreCase)) is string nKey ? dict[nKey] : null;

                            if (pIdObj != null && nNoObj != null)
                            {
                                int pumpId = Convert.ToInt32(pIdObj);
                                int nozzleNumber = Convert.ToInt32(nNoObj);
                                matchedLocalEntity = context.Set<PumpMapping>().Local
                                    .FirstOrDefault(pm => pm.PumpId == pumpId && pm.NozzleNumber == nozzleNumber);
                                
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<PumpMapping>()
                                        .FirstOrDefaultAsync(pm => pm.PumpId == pumpId && pm.NozzleNumber == nozzleNumber);
                                }
                            }
                        }
                        else if (tableDef.TableName == "Settings")
                        {
                            matchedLocalEntity = context.Set<Setting>().Local.FirstOrDefault();
                            if (matchedLocalEntity == null)
                            {
                                matchedLocalEntity = await context.Set<Setting>().FirstOrDefaultAsync();
                            }
                        }
                        else if (tableDef.TableName == "Shifts")
                        {
                            if (dict.TryGetValue("ShiftDate", out var sDateObj) && sDateObj != null &&
                                dict.TryGetValue("ShiftType", out var sTypeObj) && sTypeObj != null)
                            {
                                DateTime sDate = SyncTimestampHelper.ParseBusinessDate(sDateObj);
                                string sType = sTypeObj.ToString()!;
                                var altType = sType == "A" ? "I" : (sType == "B" ? "II" : (sType == "C" ? "III" : (sType == "I" ? "A" : (sType == "II" ? "B" : (sType == "III" ? "C" : sType)))));

                                matchedLocalEntity = context.Set<Shift>().Local
                                    .FirstOrDefault(s => s.ShiftDate == sDate && (s.ShiftType == sType || s.ShiftType == altType));

                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<Shift>()
                                        .FirstOrDefaultAsync(s => s.ShiftDate == sDate && (s.ShiftType == sType || s.ShiftType == altType));
                                }
                            }
                        }
                        else if (tableDef.TableName == "DsmEntries")
                        {
                            if (dict.TryGetValue("ShiftId", out var sIdObj) && sIdObj != null &&
                                dict.TryGetValue("PumpId", out var pIdObj) && pIdObj != null &&
                                dict.TryGetValue("DsmName", out var dNameObj) && dNameObj != null)
                            {
                                int localShiftId = Convert.ToInt32(sIdObj);
                                int pumpId = Convert.ToInt32(pIdObj);
                                string dsmName = dNameObj.ToString()!.Trim();

                                matchedLocalEntity = context.Set<DsmEntry>().Local
                                    .FirstOrDefault(e => e.ShiftId == localShiftId && e.PumpId == pumpId && string.Equals(e.DsmName, dsmName, StringComparison.OrdinalIgnoreCase));

                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<DsmEntry>()
                                        .FirstOrDefaultAsync(e => e.ShiftId == localShiftId && e.PumpId == pumpId && (e.DsmName != null && e.DsmName.ToLower() == dsmName.ToLower()));
                                }
                            }
                        }
                        else if (tableDef.TableName == "PaymentCollections")
                        {
                            if (dict.TryGetValue("DsmEntryId", out var dsmIdObj) && dsmIdObj != null)
                            {
                                int localDsmEntryId = Convert.ToInt32(dsmIdObj);
                                matchedLocalEntity = context.Set<PaymentCollection>().Local
                                    .FirstOrDefault(p => p.DsmEntryId == localDsmEntryId);

                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<PaymentCollection>()
                                        .FirstOrDefaultAsync(p => p.DsmEntryId == localDsmEntryId);
                                }
                            }
                        }
                        else if (tableDef.TableName == "NozzleReadings")
                        {
                            if (dict.TryGetValue("DsmEntryId", out var dsmIdObj) && dsmIdObj != null &&
                                dict.TryGetValue("NozzleNumber", out var nNoObj) && nNoObj != null)
                            {
                                int localDsmEntryId = Convert.ToInt32(dsmIdObj);
                                int nozzleNumber = Convert.ToInt32(nNoObj);
                                matchedLocalEntity = context.Set<NozzleReading>().Local
                                    .FirstOrDefault(nr => nr.DsmEntryId == localDsmEntryId && nr.NozzleNumber == nozzleNumber);
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<NozzleReading>()
                                        .FirstOrDefaultAsync(nr => nr.DsmEntryId == localDsmEntryId && nr.NozzleNumber == nozzleNumber);
                                }
                            }
                        }
                        else if (tableDef.TableName == "TestingEntries")
                        {
                            if (dict.TryGetValue("DsmEntryId", out var dsmIdObj) && dsmIdObj != null &&
                                dict.TryGetValue("FuelType", out var ftObj) && ftObj != null)
                            {
                                int localDsmEntryId = Convert.ToInt32(dsmIdObj);
                                string fuelType = ftObj.ToString()!.Trim();
                                matchedLocalEntity = context.Set<TestingEntry>().Local
                                    .FirstOrDefault(t => t.DsmEntryId == localDsmEntryId && string.Equals(t.FuelType, fuelType, StringComparison.OrdinalIgnoreCase));
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<TestingEntry>()
                                        .FirstOrDefaultAsync(t => t.DsmEntryId == localDsmEntryId && (t.FuelType != null && t.FuelType.ToLower() == fuelType.ToLower()));
                                }
                            }
                        }
                        else if (tableDef.TableName == "DebitEntries")
                        {
                            if (dict.TryGetValue("DsmEntryId", out var dsmIdObj) && dsmIdObj != null &&
                                dict.TryGetValue("DebtorName", out var dNameObj) && dNameObj != null)
                            {
                                int localDsmEntryId = Convert.ToInt32(dsmIdObj);
                                string debtorName = dNameObj.ToString()!.Trim();
                                double amt = dict.TryGetValue("Amount", out var amtObj) && amtObj != null ? Convert.ToDouble(amtObj) : 0;
                                matchedLocalEntity = context.Set<DebitEntry>().Local
                                    .FirstOrDefault(d => d.DsmEntryId == localDsmEntryId && string.Equals(d.DebtorName, debtorName, StringComparison.OrdinalIgnoreCase) && Math.Abs(d.Amount - amt) < 0.01);
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<DebitEntry>()
                                        .FirstOrDefaultAsync(d => d.DsmEntryId == localDsmEntryId && (d.DebtorName != null && d.DebtorName.ToLower() == debtorName.ToLower()) && Math.Abs(d.Amount - amt) < 0.01);
                                }
                            }
                        }
                        else if (tableDef.TableName == "Expenses")
                        {
                            dict.TryGetValue("DsmEntryId", out var dsmIdObj);
                            dict.TryGetValue("ShiftId", out var sIdObj);
                            dict.TryGetValue("Description", out var descObj);
                            dict.TryGetValue("Amount", out var amtObj);
                            int? localDsmId = dsmIdObj != null ? Convert.ToInt32(dsmIdObj) : null;
                            int? localShiftId = sIdObj != null ? Convert.ToInt32(sIdObj) : null;
                            string desc = descObj?.ToString()?.Trim() ?? "";
                            double amt = amtObj != null ? Convert.ToDouble(amtObj) : 0;

                            if ((localDsmId.HasValue || localShiftId.HasValue) && !string.IsNullOrEmpty(desc))
                            {
                                matchedLocalEntity = context.Set<Expense>().Local
                                    .FirstOrDefault(e => (localDsmId.HasValue ? e.DsmEntryId == localDsmId : e.ShiftId == localShiftId)
                                                         && string.Equals(e.Description, desc, StringComparison.OrdinalIgnoreCase)
                                                         && Math.Abs(e.Amount - amt) < 0.01);
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<Expense>()
                                        .FirstOrDefaultAsync(e => (localDsmId.HasValue ? e.DsmEntryId == localDsmId : e.ShiftId == localShiftId)
                                                                  && (e.Description != null && e.Description.ToLower() == desc.ToLower())
                                                                  && Math.Abs(e.Amount - amt) < 0.01);
                                }
                            }
                        }
                        else if (tableDef.TableName == "KhandharePetroleumEntries")
                        {
                            dict.TryGetValue("DsmEntryId", out var dsmIdObj);
                            dict.TryGetValue("SlipNumber", out var slipObj);
                            dict.TryGetValue("Name", out var nameObj);
                            dict.TryGetValue("Amount", out var amtObj);
                            int? localDsmId = dsmIdObj != null ? Convert.ToInt32(dsmIdObj) : null;
                            string slip = slipObj?.ToString()?.Trim() ?? "";
                            string name = nameObj?.ToString()?.Trim() ?? "";
                            double amt = amtObj != null ? Convert.ToDouble(amtObj) : 0;

                            if (localDsmId.HasValue)
                            {
                                matchedLocalEntity = context.Set<KhandharePetroleumEntry>().Local
                                    .FirstOrDefault(k => k.DsmEntryId == localDsmId && 
                                                         (!string.IsNullOrEmpty(slip) ? string.Equals(k.SlipNumber, slip, StringComparison.OrdinalIgnoreCase) : (string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase) && Math.Abs(k.Amount - amt) < 0.01)));
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<KhandharePetroleumEntry>()
                                        .FirstOrDefaultAsync(k => k.DsmEntryId == localDsmId && 
                                                                  (!string.IsNullOrEmpty(slip) ? (k.SlipNumber != null && k.SlipNumber.ToLower() == slip.ToLower()) : ((k.Name != null && k.Name.ToLower() == name.ToLower()) && Math.Abs(k.Amount - amt) < 0.01)));
                                }
                            }
                        }

                        else if (tableDef.TableName == "CashDenominations")
                        {
                            // CashDenominations has a UNIQUE index on (DsmEntryId, CashType).
                            // Without this check, pulling the same record twice (or when the
                            // SyncIdMapping is stale) would INSERT a duplicate and crash with
                            // "UNIQUE constraint failed: CashDenominations.DsmEntryId, CashDenominations.CashType".
                            if (dict.TryGetValue("DsmEntryId", out var dsmIdObj) && dsmIdObj != null &&
                                dict.TryGetValue("CashType", out var cashTypeObj) && cashTypeObj != null)
                            {
                                int localDsmEntryId = Convert.ToInt32(dsmIdObj);
                                string cashType = cashTypeObj.ToString()!;

                                matchedLocalEntity = context.Set<CashDenomination>().Local
                                    .FirstOrDefault(cd => cd.DsmEntryId == localDsmEntryId && cd.CashType == cashType);

                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<CashDenomination>()
                                        .FirstOrDefaultAsync(cd => cd.DsmEntryId == localDsmEntryId && cd.CashType == cashType);
                                }
                            }
                        }
                        else if (tableDef.TableName == "PumpMappings")
                        {
                            int pId = 0;
                            if (dict.TryGetValue("PumpId", out var pObj) && pObj != null) pId = Convert.ToInt32(pObj);
                            else if (dict.TryGetValue("pump_id", out var pObj2) && pObj2 != null) pId = Convert.ToInt32(pObj2);

                            int nNo = 0;
                            if (dict.TryGetValue("NozzleNumber", out var nObj) && nObj != null) nNo = Convert.ToInt32(nObj);
                            else if (dict.TryGetValue("nozzle_number", out var nObj2) && nObj2 != null) nNo = Convert.ToInt32(nObj2);

                            if (pId > 0 && nNo > 0)
                            {
                                matchedLocalEntity = context.Set<PumpMapping>().Local
                                    .FirstOrDefault(pm => pm.PumpId == pId && pm.NozzleNumber == nNo);
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<PumpMapping>()
                                        .FirstOrDefaultAsync(pm => pm.PumpId == pId && pm.NozzleNumber == nNo);
                                }
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = context.Set<PumpMapping>().Local
                                        .FirstOrDefault(pm => pm.NozzleNumber == nNo);
                                    if (matchedLocalEntity == null)
                                    {
                                        matchedLocalEntity = await context.Set<PumpMapping>()
                                            .FirstOrDefaultAsync(pm => pm.NozzleNumber == nNo);
                                    }
                                }
                            }
                        }
                        else if (tableDef.TableName == "ProductMasters")
                        {
                            if (dict.TryGetValue("ProductName", out var pNameObj) && pNameObj != null)
                            {
                                string pName = pNameObj.ToString()!.Trim();
                                matchedLocalEntity = context.Set<ProductMaster>().Local
                                    .FirstOrDefault(p => string.Equals(p.ProductName, pName, StringComparison.OrdinalIgnoreCase));
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<ProductMaster>()
                                        .FirstOrDefaultAsync(p => p.ProductName.ToLower() == pName.ToLower());
                                }
                            }
                        }
                        else if (tableDef.TableName == "TankDefinitions")
                        {
                            if (dict.TryGetValue("TankName", out var tNameObj) && tNameObj != null)
                            {
                                string tName = tNameObj.ToString()!.Trim();
                                matchedLocalEntity = context.Set<TankDefinition>().Local
                                    .FirstOrDefault(t => string.Equals(t.TankName, tName, StringComparison.OrdinalIgnoreCase));
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<TankDefinition>()
                                        .FirstOrDefaultAsync(t => t.TankName.ToLower() == tName.ToLower());
                                }
                            }
                        }
                        else if (tableDef.TableName == "AppFeatureSettings")
                        {
                            if (dict.TryGetValue("FeatureKey", out var fKeyObj) && fKeyObj != null &&
                                dict.TryGetValue("TargetRole", out var tRoleObj) && tRoleObj != null)
                            {
                                string fKey = fKeyObj.ToString()!.Trim();
                                string tRole = tRoleObj.ToString()!.Trim();
                                matchedLocalEntity = context.Set<AppFeatureSetting>().Local
                                    .FirstOrDefault(f => string.Equals(f.FeatureKey, fKey, StringComparison.OrdinalIgnoreCase) && string.Equals(f.TargetRole, tRole, StringComparison.OrdinalIgnoreCase));
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<AppFeatureSetting>()
                                        .FirstOrDefaultAsync(f => f.FeatureKey.ToLower() == fKey.ToLower() && f.TargetRole.ToLower() == tRole.ToLower());
                                }
                            }
                        }
                        else if (tableDef.TableName == "CollectionTypes")
                        {
                            if (dict.TryGetValue("Code", out var codeObj) && codeObj != null)
                            {
                                string code = codeObj.ToString()!.Trim();
                                matchedLocalEntity = context.Set<CollectionTypeMaster>().Local
                                    .FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<CollectionTypeMaster>()
                                        .FirstOrDefaultAsync(c => c.Code.ToLower() == code.ToLower());
                                }
                            }
                        }
                        else if (tableDef.TableName == "OilDefPurchases")
                        {
                            dict.TryGetValue("ProductId", out var prodIdObj);
                            dict.TryGetValue("PurchaseDate", out var pDateObj);
                            dict.TryGetValue("InvoiceNumber", out var invoiceObj);
                            dict.TryGetValue("Quantity", out var qtyObj);
                            dict.TryGetValue("UnitPrice", out var upObj);
                            int? productId = prodIdObj != null ? Convert.ToInt32(prodIdObj) : null;
                            DateTime? purchaseDate = pDateObj != null ? SyncTimestampHelper.ParseBusinessDate(pDateObj) : null;
                            string invoice = invoiceObj?.ToString()?.Trim() ?? "";
                            double qty = qtyObj != null ? Convert.ToDouble(qtyObj) : 0;
                            double unitPrice = upObj != null ? Convert.ToDouble(upObj) : 0;

                            if (productId.HasValue && purchaseDate.HasValue)
                            {
                                var pDate = purchaseDate.Value.Date;
                                matchedLocalEntity = context.Set<OilDefPurchase>().Local
                                    .FirstOrDefault(p => p.ProductId == productId && p.PurchaseDate.Date == pDate
                                        && string.Equals(p.InvoiceNumber, invoice, StringComparison.OrdinalIgnoreCase)
                                        && Math.Abs(p.Quantity - qty) < 0.01 && Math.Abs(p.UnitPrice - unitPrice) < 0.01);
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<OilDefPurchase>()
                                        .FirstOrDefaultAsync(p => p.ProductId == productId && p.PurchaseDate.Date == pDate
                                            && (p.InvoiceNumber != null && p.InvoiceNumber.ToLower() == invoice.ToLower())
                                            && Math.Abs(p.Quantity - qty) < 0.01 && Math.Abs(p.UnitPrice - unitPrice) < 0.01);
                                }
                            }
                        }
                        else if (tableDef.TableName == "CreditorRepayments")
                        {
                            dict.TryGetValue("CreditorName", out var cNameObj);
                            dict.TryGetValue("RepaymentDate", out var rDateObj);
                            dict.TryGetValue("Amount", out var amtObj);
                            dict.TryGetValue("PaymentMode", out var pmObj);
                            string cName = cNameObj?.ToString()?.Trim() ?? "";
                            DateTime? rDate = rDateObj != null ? SyncTimestampHelper.ParseBusinessDate(rDateObj) : null;
                            double amt = amtObj != null ? Convert.ToDouble(amtObj) : 0;
                            string payMode = pmObj?.ToString()?.Trim() ?? "";

                            if (!string.IsNullOrEmpty(cName) && rDate.HasValue)
                            {
                                var rd = rDate.Value.Date;
                                matchedLocalEntity = context.Set<CreditorRepayment>().Local
                                    .FirstOrDefault(r => r.CreditorName.Equals(cName, StringComparison.OrdinalIgnoreCase)
                                        && r.RepaymentDate.Date == rd && Math.Abs(r.Amount - amt) < 0.01
                                        && string.Equals(r.PaymentMode, payMode, StringComparison.OrdinalIgnoreCase));
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<CreditorRepayment>()
                                        .FirstOrDefaultAsync(r => r.CreditorName.ToLower() == cName.ToLower()
                                            && r.RepaymentDate.Date == rd && Math.Abs(r.Amount - amt) < 0.01
                                            && r.PaymentMode.ToLower() == payMode.ToLower());
                                }
                            }
                        }
                        else if (tableDef.TableName == "DsmPersonalDebtorRepayments")
                        {
                            dict.TryGetValue("DsmPersonalDebtorId", out var debtorIdObj);
                            dict.TryGetValue("Date", out var dateObj);
                            dict.TryGetValue("Amount", out var amtObj);
                            dict.TryGetValue("PaymentMethod", out var pmObj);
                            int? debtorId = debtorIdObj != null ? Convert.ToInt32(debtorIdObj) : null;
                            DateTime? date = dateObj != null ? SyncTimestampHelper.ParseBusinessDate(dateObj) : null;
                            double amt = amtObj != null ? Convert.ToDouble(amtObj) : 0;
                            string payMethod = pmObj?.ToString()?.Trim() ?? "";

                            if (debtorId.HasValue && date.HasValue)
                            {
                                var d = date.Value.Date;
                                matchedLocalEntity = context.Set<DsmPersonalDebtorRepayment>().Local
                                    .FirstOrDefault(r => r.DsmPersonalDebtorId == debtorId && r.Date.Date == d
                                        && Math.Abs(r.Amount - amt) < 0.01
                                        && string.Equals(r.PaymentMethod, payMethod, StringComparison.OrdinalIgnoreCase));
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<DsmPersonalDebtorRepayment>()
                                        .FirstOrDefaultAsync(r => r.DsmPersonalDebtorId == debtorId && r.Date.Date == d
                                            && Math.Abs(r.Amount - amt) < 0.01
                                            && r.PaymentMethod.ToLower() == payMethod.ToLower());
                                }
                            }
                        }
                        else if (tableDef.TableName == "Creditors")
                        {
                            if (dict.TryGetValue("Name", out var nameObj) && nameObj != null)
                            {
                                string cName = nameObj.ToString()!.Trim();
                                matchedLocalEntity = context.Set<Creditor>().Local
                                    .FirstOrDefault(c => string.Equals(c.Name, cName, StringComparison.OrdinalIgnoreCase));
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<Creditor>()
                                        .FirstOrDefaultAsync(c => c.Name.ToLower() == cName.ToLower());
                                }
                            }
                        }
                        else if (tableDef.TableName == "OilDefDailyLogs")
                        {
                            dict.TryGetValue("ProductId", out var prodIdObj);
                            dict.TryGetValue("ShiftId", out var shiftIdObj);
                            dict.TryGetValue("LogDate", out var logDateObj);
                            dict.TryGetValue("AdjustmentQuantity", out var adjQtyObj);
                            dict.TryGetValue("AdjustmentType", out var adjTypeObj);
                            dict.TryGetValue("SoldQuantity", out var soldQtyObj);
                            dict.TryGetValue("AddedQuantity", out var addedQtyObj);
                            dict.TryGetValue("Remarks", out var remObj);
                            int? productId = prodIdObj != null ? Convert.ToInt32(prodIdObj) : null;
                            int? shiftId = shiftIdObj != null ? Convert.ToInt32(shiftIdObj) : null;
                            DateTime? logDate = logDateObj != null ? SyncTimestampHelper.ParseBusinessDate(logDateObj) : null;
                            double adjQty = adjQtyObj != null ? Convert.ToDouble(adjQtyObj) : 0;
                            string adjType = adjTypeObj?.ToString()?.Trim() ?? "";
                            double soldQty = soldQtyObj != null ? Convert.ToDouble(soldQtyObj) : 0;
                            double addedQty = addedQtyObj != null ? Convert.ToDouble(addedQtyObj) : 0;
                            string remarks = remObj?.ToString()?.Trim() ?? "";

                            if (productId.HasValue && logDate.HasValue)
                            {
                                var ld = logDate.Value;
                                if (Math.Abs(adjQty) > 0.001)
                                {
                                    // Stock adjustment: match by ProductId, date, AdjustmentQuantity, and AdjustmentType
                                    matchedLocalEntity = OilDefLogSyncMatcher.ForShift(context.Set<OilDefDailyLog>().Local.AsQueryable(), shiftId)
                                        .FirstOrDefault(l => l.ProductId == productId.Value && l.LogDate.Date == ld.Date
                                            && Math.Abs(l.AdjustmentQuantity - adjQty) < 0.01
                                            && string.Equals(l.AdjustmentType ?? "", adjType, StringComparison.OrdinalIgnoreCase)
                                            && (Math.Abs((l.LogDate - ld).TotalMinutes) < 2 || string.Equals(l.Remarks ?? "", remarks, StringComparison.OrdinalIgnoreCase)));

                                    if (matchedLocalEntity == null)
                                    {
                                        matchedLocalEntity = await OilDefLogSyncMatcher.ForShift(context.Set<OilDefDailyLog>(), shiftId)
                                            .FirstOrDefaultAsync(l => l.ProductId == productId.Value && l.LogDate.Date == ld.Date
                                                && Math.Abs(l.AdjustmentQuantity - adjQty) < 0.01
                                                && ((l.AdjustmentType == null && adjType == "") || (l.AdjustmentType != null && l.AdjustmentType.ToLower() == adjType.ToLower())));
                                    }
                                }
                                else if (soldQty > 0.001)
                                {
                                    // Sales log: match by ProductId, date, SoldQuantity
                                    matchedLocalEntity = OilDefLogSyncMatcher.ForShift(context.Set<OilDefDailyLog>().Local.AsQueryable(), shiftId)
                                        .FirstOrDefault(l => l.ProductId == productId.Value && l.LogDate.Date == ld.Date
                                            && Math.Abs(l.SoldQuantity - soldQty) < 0.01 && Math.Abs(l.AdjustmentQuantity) < 0.001);

                                    if (matchedLocalEntity == null)
                                    {
                                        matchedLocalEntity = await OilDefLogSyncMatcher.ForShift(context.Set<OilDefDailyLog>(), shiftId)
                                            .FirstOrDefaultAsync(l => l.ProductId == productId.Value && l.LogDate.Date == ld.Date
                                                && Math.Abs(l.SoldQuantity - soldQty) < 0.01 && Math.Abs(l.AdjustmentQuantity) < 0.001);
                                    }
                                }
                                else if (addedQty > 0.001)
                                {
                                    // Purchase log: match by ProductId, date, AddedQuantity
                                    matchedLocalEntity = OilDefLogSyncMatcher.ForShift(context.Set<OilDefDailyLog>().Local.AsQueryable(), shiftId)
                                        .FirstOrDefault(l => l.ProductId == productId.Value && l.LogDate.Date == ld.Date
                                            && Math.Abs(l.AddedQuantity - addedQty) < 0.01 && Math.Abs(l.AdjustmentQuantity) < 0.001);

                                    if (matchedLocalEntity == null)
                                    {
                                        matchedLocalEntity = await OilDefLogSyncMatcher.ForShift(context.Set<OilDefDailyLog>(), shiftId)
                                            .FirstOrDefaultAsync(l => l.ProductId == productId.Value && l.LogDate.Date == ld.Date
                                                && Math.Abs(l.AddedQuantity - addedQty) < 0.01 && Math.Abs(l.AdjustmentQuantity) < 0.001);
                                    }
                                }
                            }
                        }
                        else if (tableDef.TableName == "OilDefInventories")
                        {
                            dict.TryGetValue("ProductId", out var prodIdObj);
                            dict.TryGetValue("Year", out var yearObj);
                            dict.TryGetValue("Month", out var monthObj);
                            int? productId = prodIdObj != null ? Convert.ToInt32(prodIdObj) : null;
                            int? year = yearObj != null ? Convert.ToInt32(yearObj) : null;
                            int? month = monthObj != null ? Convert.ToInt32(monthObj) : null;

                            if (productId.HasValue && year.HasValue && month.HasValue)
                            {
                                matchedLocalEntity = context.Set<OilDefInventory>().Local
                                    .FirstOrDefault(i => i.ProductId == productId.Value && i.Year == year.Value && i.Month == month.Value);
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<OilDefInventory>()
                                        .FirstOrDefaultAsync(i => i.ProductId == productId.Value && i.Year == year.Value && i.Month == month.Value);
                                }
                            }
                        }
                        else if (tableDef.TableName == "OpeningBalances")
                        {
                            var entityTypeVal = dict.GetValueOrDefault("EntityType")?.ToString();
                            var entityIdentVal = dict.GetValueOrDefault("EntityIdentifier")?.ToString();

                            // First match by SyncGuid if existing locally
                            matchedLocalEntity = context.Set<OpeningBalance>().Local
                                .FirstOrDefault(o => o.SyncGuid == remoteGuid);
                            if (matchedLocalEntity == null)
                            {
                                matchedLocalEntity = await context.Set<OpeningBalance>()
                                    .FirstOrDefaultAsync(o => o.SyncGuid == remoteGuid);
                            }

                            // If not matched by SyncGuid, match active record by EntityType + EntityIdentifier within station
                            if (matchedLocalEntity == null && !string.IsNullOrEmpty(entityTypeVal) && !string.IsNullOrEmpty(entityIdentVal))
                            {
                                matchedLocalEntity = context.Set<OpeningBalance>().Local
                                    .FirstOrDefault(o => o.EntityType == entityTypeVal && o.EntityIdentifier == entityIdentVal && o.IsActive);
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<OpeningBalance>()
                                        .FirstOrDefaultAsync(o => o.EntityType == entityTypeVal && o.EntityIdentifier == entityIdentVal && o.IsActive);
                                }
                            }
                        }
                        else if (tableDef.TableName == "DsmUsers")
                        {
                            var remoteEmpCode = TryGetDictValue(dict, "EmployeeCode", out var ec) && ec != null ? ec.ToString()!.Trim() : null;
                            var remoteMobile = TryGetDictValue(dict, "MobileNumber", out var mn) && mn != null ? mn.ToString()!.Trim() : null;
                            var remoteAuthUser = TryGetDictValue(dict, "AuthUserId", out var au) && au != null ? au.ToString()!.Trim() : null;
                            var remoteFullName = TryGetDictValue(dict, "FullName", out var fn) && fn != null ? fn.ToString()!.Trim() : null;
                            string? remoteStation = null;
                            if (TryGetDictValue(dict, "station_id", out var st1) && st1 != null) remoteStation = st1.ToString()?.Trim();
                            else if (TryGetDictValue(dict, "StationId", out var st2) && st2 != null) remoteStation = st2.ToString()?.Trim();

                            matchedLocalEntity = await MatchLocalDsmUserAsync(
                                context, remoteEmpCode, remoteMobile, remoteAuthUser, remoteFullName, remoteStation, settings.StationId);
                        }
                        else if (tableDef.TableName == "DsmPumpAssignments")
                        {
                            int pId = 0;
                            if (TryGetDictValue(dict, "PumpId", out var pObj) && pObj != null) pId = Convert.ToInt32(pObj);
                            else if (TryGetDictValue(dict, "pump_id", out var pObj2) && pObj2 != null) pId = Convert.ToInt32(pObj2);

                            string sType = "";
                            if (TryGetDictValue(dict, "ShiftType", out var sObj) && sObj != null) sType = sObj.ToString()!.Trim();
                            else if (TryGetDictValue(dict, "shift_type", out var sObj2) && sObj2 != null) sType = sObj2.ToString()!.Trim();

                            bool isActive = false;
                            if (TryGetDictValue(dict, "IsActive", out var aObj) && aObj != null) isActive = Convert.ToBoolean(aObj);
                            else if (TryGetDictValue(dict, "is_active", out var aObj2) && aObj2 != null) isActive = Convert.ToBoolean(aObj2);

                            int localDsmUserId = 0;
                            if (TryGetDictValue(dict, "DsmUserId", out var dObj) && dObj != null) localDsmUserId = Convert.ToInt32(dObj);
                            else if (TryGetDictValue(dict, "dsm_user_id", out var dObj2) && dObj2 != null) localDsmUserId = Convert.ToInt32(dObj2);

                            string? recStationId = null;
                            if (TryGetDictValue(dict, "station_id", out var stObj1) && stObj1 != null) recStationId = stObj1.ToString()?.Trim();
                            else if (TryGetDictValue(dict, "StationId", out var stObj2) && stObj2 != null) recStationId = stObj2.ToString()?.Trim();

                            bool stationMatches = string.IsNullOrEmpty(recStationId) || string.IsNullOrEmpty(settings.StationId) ||
                                string.Equals(recStationId, settings.StationId, StringComparison.OrdinalIgnoreCase);

                            if (stationMatches && pId > 0 && !string.IsNullOrEmpty(sType) && localDsmUserId > 0)
                            {
                                // Mandatory verification: DSM identity (localDsmUserId), PumpId, and ShiftType must match.
                                // Never match solely by PumpId and ShiftType without DSM identity verification!
                                var candidateAssignments = context.Set<DsmPumpAssignment>().Local
                                    .Where(a => a.DsmUserId == localDsmUserId && a.PumpId == pId && string.Equals(a.ShiftType, sType, StringComparison.OrdinalIgnoreCase))
                                    .ToList();

                                if (candidateAssignments.Count == 0)
                                {
                                    candidateAssignments = await context.Set<DsmPumpAssignment>()
                                        .Where(a => a.DsmUserId == localDsmUserId && a.PumpId == pId && a.ShiftType.ToLower() == sType.ToLower())
                                        .ToListAsync();
                                }

                                if (isActive)
                                {
                                    var activeCandidates = candidateAssignments.Where(a => a.IsActive).ToList();
                                    if (activeCandidates.Count == 1)
                                    {
                                        matchedLocalEntity = activeCandidates[0];
                                    }
                                    else if (activeCandidates.Count > 1)
                                    {
                                        _logger.Warning("DsmPumpAssignments: Multiple active assignments found for DSM {DsmId}, Pump {Pump}, Shift {Shift}. Ambiguity detected - skipping adoption.",
                                            localDsmUserId, pId, sType);
                                    }
                                }

                                if (matchedLocalEntity == null && candidateAssignments.Count == 1)
                                {
                                    matchedLocalEntity = candidateAssignments[0];
                                }
                            }
                        }

                        if (matchedLocalEntity != null)
                        {
                            var pkValue = (int)context.Entry(matchedLocalEntity).Property(pkProp.Name).CurrentValue!;

                            // Guard: Check if pkValue is ALREADY mapped to another RemoteGuid
                            bool isAlreadyMappedToDifferentGuid = false;
                            string? mappedGuid = null;
                            if (localToGuid.TryGetValue(tableDef.TableName, out var localMap) &&
                                localMap.TryGetValue(pkValue, out mappedGuid) &&
                                !string.IsNullOrEmpty(mappedGuid) &&
                                !string.Equals(mappedGuid, remoteGuid, StringComparison.OrdinalIgnoreCase))
                            {
                                isAlreadyMappedToDifferentGuid = true;
                            }

                            if (!isAlreadyMappedToDifferentGuid)
                            {
                                var existingDbMapping = await context.SyncIdMappings
                                    .FirstOrDefaultAsync(m => m.TableName == tableDef.TableName 
                                                           && m.LocalId == pkValue 
                                                           && m.RemoteGuid != remoteGuid);
                                if (existingDbMapping != null)
                                {
                                    isAlreadyMappedToDifferentGuid = true;
                                    mappedGuid = existingDbMapping.RemoteGuid;
                                }
                            }

                            if (isAlreadyMappedToDifferentGuid)
                            {
                                if (tableDef.TableName == "DsmPumpAssignments" && matchedLocalEntity is DsmPumpAssignment pa)
                                {
                                    // Verify that this is the verified rejected assignment for this exact DSM and pump/shift
                                    _logger.Information("DsmPumpAssignments LocalId={Id} for DSM {DsmId} (Pump {Pump}, Shift {Shift}) was mapped to unconfirmed GUID {OldGuid}. Canonical remote GUID {RemoteGuid} supersedes it.",
                                        pkValue, pa.DsmUserId, pa.PumpId, pa.ShiftType, mappedGuid, remoteGuid);

                                    // 1. Remove stale mapping
                                    var staleMappings = await context.SyncIdMappings
                                        .Where(m => m.TableName == tableDef.TableName && m.LocalId == pkValue)
                                        .ToListAsync();
                                    if (staleMappings.Count > 0)
                                    {
                                        context.SyncIdMappings.RemoveRange(staleMappings);
                                        await context.SaveChangesAsync();
                                    }
                                    if (localToGuid.TryGetValue(tableDef.TableName, out var lMap))
                                    {
                                        lMap.Remove(pkValue);
                                    }

                                    // 2. Narrowly acknowledge/clear only pending SyncChangeLogs for this specific local assignment
                                    var pendingConflictLogs = await context.SyncChangeLogs
                                        .Where(l => l.TableName == "DsmPumpAssignments" && (l.RecordId == pkValue || (mappedGuid != null && l.RecordGuid == mappedGuid)) && !l.IsSynced)
                                        .ToListAsync();
                                    if (pendingConflictLogs.Count > 0)
                                    {
                                        foreach (var log in pendingConflictLogs)
                                        {
                                            log.IsSynced = true;
                                            log.SyncedAt = DateTime.Now;
                                            log.RecordGuid = remoteGuid;
                                        }
                                        await context.SaveChangesAsync();
                                        _logger.Information("Acknowledged {Count} pending rejected SyncChangeLogs for DsmPumpAssignment LocalId={Id}",
                                            pendingConflictLogs.Count, pkValue);
                                    }

                                    isAlreadyMappedToDifferentGuid = false;
                                }
                                else
                                {
                                    _logger.Information("Natural-key match for {Table} LocalId={Id} rejected because it is already mapped to remote GUID {MappedGuid}. Treating incoming {RemoteGuid} as a new distinct record.",
                                        tableDef.TableName, pkValue, mappedGuid ?? "(db)", remoteGuid);
                                    matchedLocalEntity = null; // Do not adopt an already-mapped record!
                                }
                            }
                        }

                        if (matchedLocalEntity != null)
                        {
                            entity = matchedLocalEntity;
                            var pkValue = (int)context.Entry(matchedLocalEntity).Property(pkProp.Name).CurrentValue!;
                            
                            // Map the remote SyncGuid to this existing local ID
                            tableMap[remoteGuid] = pkValue;
                            if (!localToGuid.ContainsKey(tableDef.TableName))
                                localToGuid[tableDef.TableName] = new Dictionary<int, string>();
                            localToGuid[tableDef.TableName][pkValue] = remoteGuid;

                            context.SyncIdMappings.Add(new SyncIdMapping
                            {
                                TableName = tableDef.TableName,
                                RemoteGuid = remoteGuid,
                                LocalId = pkValue
                            });
                            isNew = false;
                        }
                        else
                        {
                            // No mapping and no business key match — this is a new record for this machine
                            entity = Activator.CreateInstance(entityType.ClrType);
                            if (entity == null) continue;
                            context.Add(entity);
                            isNew = true;
                        }
                    }

                    // ── Step 3: Set all non-PK properties from Supabase data ──
                    var entry = context.Entry(entity);
                    bool entityHadValueChange = isNew;
                    bool preserveExistingNonZeroPayment = !isNew && entity is PaymentCollection && IsIncompleteZeroRemotePaymentCollection(dict);
                    foreach (var prop in entityType.GetProperties())
                    {
                        if (prop.IsPrimaryKey()) continue;

                        if (TryGetDictValue(dict, prop.Name, out var val))
                        {
                            if (val == null)
                            {
                                var isNullable = Nullable.GetUnderlyingType(prop.ClrType) != null || !prop.ClrType.IsValueType;
                                if (isNullable)
                                {
                                    var existingPropVal = entry.Property(prop.Name).CurrentValue;
                                    if (existingPropVal != null)
                                    {
                                        entry.Property(prop.Name).CurrentValue = null;
                                        entityHadValueChange = true;
                                    }
                                }
                            }
                            else
                            {
                                var targetType = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
                                object? converted;
                                if (targetType == typeof(DateTime))
                                    converted = SyncTimestampHelper.ParseForPull(entityType.ClrType, prop.Name, val);
                                else if (targetType == typeof(Guid))
                                    converted = Guid.Parse(val.ToString()!);
                                else if (targetType == typeof(bool))
                                    converted = Convert.ToBoolean(val);
                                else if (targetType == typeof(int))
                                    converted = Convert.ToInt32(val);
                                else if (targetType == typeof(double))
                                    converted = Convert.ToDouble(val);
                                else if (targetType == typeof(decimal))
                                    converted = Convert.ToDecimal(val);
                                else if (targetType.IsEnum)
                                    converted = Enum.Parse(targetType, val.ToString()!);
                                else
                                    converted = Convert.ChangeType(val, targetType);

                                var existingPropVal = entry.Property(prop.Name).CurrentValue;
                                if (preserveExistingNonZeroPayment && existingPropVal is double curAmt && curAmt > 0 && converted is double newAmt && newAmt == 0)
                                {
                                    continue;
                                }
                                if (!AreSyncPropertyValuesEqual(existingPropVal, converted))
                                {
                                    entry.Property(prop.Name).CurrentValue = converted;
                                    entityHadValueChange = true;
                                }
                            }
                        }
                    }

                    if (entity is PaymentCollection pulledPc)
                    {
                        bool pcBackfilled = await ApplyPaymentCollectionLegacyFallbackAsync(context, pulledPc, dict);
                        if (pcBackfilled)
                        {
                            entityHadValueChange = true;
                        }
                    }

                    if (entityHadValueChange)
                        appliedRecordCount++;
                    if (isNew)
                        newEntities.Add((entity, remoteGuid));
                }

                // Save all changes for this table in one batch
                await context.SaveChangesAsync();

                // Create ID mappings for newly inserted entities
                if (newEntities.Count > 0)
                {
                    // Remove any stale mappings for these remote GUIDs (handles re-created records)
                    var remoteGuids = newEntities.Select(e => e.RemoteGuid).ToList();
                    var staleMappings = await context.SyncIdMappings
                        .Where(m => m.TableName == tableDef.TableName && remoteGuids.Contains(m.RemoteGuid))
                        .ToListAsync();
                    if (staleMappings.Count > 0)
                        context.SyncIdMappings.RemoveRange(staleMappings);

                    foreach (var (entity, remoteGuid) in newEntities)
                    {
                        var newLocalId = (int)context.Entry(entity).Property(pkProp.Name).CurrentValue!;
                        tableMap[remoteGuid] = newLocalId;
                        if (!localToGuid.ContainsKey(tableDef.TableName))
                            localToGuid[tableDef.TableName] = new Dictionary<int, string>();
                        localToGuid[tableDef.TableName][newLocalId] = remoteGuid;

                        context.SyncIdMappings.Add(new SyncIdMapping
                        {
                            TableName = tableDef.TableName,
                            RemoteGuid = remoteGuid,
                            LocalId = newLocalId
                        });
                    }
                    await context.SaveChangesAsync();
                }

                if (appliedRecordCount > 0)
                {
                    if (OperationalDsmNotificationTables.Contains(tableDef.TableName))
                        operationalDataChanged = true;
                    if (PettyCashNotificationTables.Contains(tableDef.TableName))
                        pettyCashChanged = true;
                    if (InventoryNotificationTables.Contains(tableDef.TableName))
                        inventoryChanged = true;
                    if (PayrollNotificationTables.Contains(tableDef.TableName))
                        payrollChanged = true;
                    if (DebtorNotificationTables.Contains(tableDef.TableName))
                        debtorChanged = true;
                }

                _logger.Information("Pulled {Count} records for table {Table}", records.Count, tableDef.TableName);

                // Post-pull recalculations & caches
                if (records.Count > 0)
                {
                    if (tableDef.TableName == "OilDefDailyLogs" || tableDef.TableName == "OilDefPurchases" || tableDef.TableName == "ProductMasters")
                    {
                        try
                        {
                            var productIds = await context.ProductMasters.Select(p => p.Id).ToListAsync();
                            foreach (var pid in productIds)
                            {
                                var earliestLog = await context.OilDefDailyLogs
                                    .Where(l => l.ProductId == pid)
                                    .OrderBy(l => l.LogDate)
                                    .FirstOrDefaultAsync();
                                if (earliestLog != null)
                                {
                                    var from = earliestLog.LogDate.Date;
                                    var prevLog = await context.OilDefDailyLogs
                                        .Where(l => l.ProductId == pid && l.LogDate.Date < from)
                                        .OrderByDescending(l => l.LogDate)
                                        .ThenByDescending(l => l.Id)
                                        .FirstOrDefaultAsync();

                                    double prevRemaining = prevLog?.RemainingStock ?? 0.0;
                                    var subsequentLogs = await context.OilDefDailyLogs
                                        .Where(l => l.ProductId == pid && l.LogDate.Date >= from)
                                        .OrderBy(l => l.LogDate)
                                        .ThenBy(l => l.Id)
                                        .ToListAsync();

                                    double running = prevRemaining;
                                    foreach (var log in subsequentLogs)
                                    {
                                        running = running + log.AddedQuantity - log.SoldQuantity + log.AdjustmentQuantity;
                                        log.RemainingStock = running;
                                        context.Entry(log).State = EntityState.Modified;
                                    }
                                }
                            }
                            await context.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to recalculate running balances after pull");
                        }
                    }
                    else if (tableDef.TableName == "PaymentCollections")
                    {
                        try
                        {
                            var pcIdsNeedingItems = await context.PaymentCollections
                                .Where(p => !string.IsNullOrEmpty(p.DynamicItemsJson)
                                    && !context.PaymentCollectionItems.Any(i => i.PaymentId == p.PaymentId))
                                .ToListAsync();

                            // Also refresh items for records updated in this pull batch that carry DynamicItemsJson
                            var pulledGuids = records
                                .Select(r => GetDictString(r, "SyncGuid"))
                                .Where(g => !string.IsNullOrWhiteSpace(g))
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

                            var pulledLocalIds = new HashSet<int>();
                            if (guidToLocal.TryGetValue("PaymentCollections", out var pcMap))
                            {
                                foreach (var g in pulledGuids)
                                {
                                    if (pcMap.TryGetValue(g!, out var lid)) pulledLocalIds.Add(lid);
                                }
                            }

                            var pulledWithJson = pulledLocalIds.Count > 0
                                ? await context.PaymentCollections
                                    .Where(p => pulledLocalIds.Contains(p.PaymentId) && !string.IsNullOrEmpty(p.DynamicItemsJson))
                                    .ToListAsync()
                                : new List<PaymentCollection>();

                            var toSyncItems = pulledWithJson
                                .Concat(pcIdsNeedingItems)
                                .GroupBy(p => p.PaymentId)
                                .Select(g => g.First())
                                .ToList();

                            foreach (var pc in toSyncItems)
                            {
                                await SyncPaymentCollectionItemsFromJsonAsync(context, pc);
                            }
                            if (toSyncItems.Count > 0)
                            {
                                await context.SaveChangesAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to synchronize PaymentCollectionItems from DynamicItemsJson");
                        }
                    }
                    else if (tableDef.TableName == "Settings")
                    {
                        try
                        {
                            var currentSetting = await context.Settings.FirstOrDefaultAsync();
                            if (currentSetting != null)
                            {
                                bool configChanged = false;

                                // 1. Tanks
                                if (!string.IsNullOrWhiteSpace(currentSetting.TankDefinitionsJson))
                                {
                                    var tanks = JsonConvert.DeserializeObject<List<TankDefinition>>(currentSetting.TankDefinitionsJson);
                                    if (tanks != null && tanks.Count > 0)
                                    {
                                        var existingTanks = await context.TankDefinitions.ToListAsync();
                                        var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                                        foreach (var t in tanks)
                                        {
                                            var existingTank = existingTanks.FirstOrDefault(x => string.Equals(x.TankName, t.TankName, StringComparison.OrdinalIgnoreCase));
                                            if (existingTank != null)
                                            {
                                                if (Math.Abs(existingTank.CapacityKL - t.CapacityKL) > 0.001 ||
                                                    existingTank.FuelType != t.FuelType ||
                                                    existingTank.IsActive != t.IsActive ||
                                                    existingTank.HasTesting != t.HasTesting)
                                                {
                                                    existingTank.CapacityKL = t.CapacityKL;
                                                    existingTank.FuelType = t.FuelType;
                                                    existingTank.IsActive = t.IsActive;
                                                    existingTank.HasTesting = t.HasTesting;
                                                    configChanged = true;
                                                }
                                                processedNames.Add(existingTank.TankName);
                                            }
                                            else
                                            {
                                                context.TankDefinitions.Add(new TankDefinition
                                                {
                                                    TankName = t.TankName,
                                                    CapacityKL = t.CapacityKL,
                                                    FuelType = t.FuelType,
                                                    IsActive = t.IsActive,
                                                    HasTesting = t.HasTesting,
                                                    CreatedAt = DateTime.Now
                                                });
                                                processedNames.Add(t.TankName);
                                                configChanged = true;
                                            }
                                        }

                                        // Remove obsolete / phantom seed tanks not present in station's tank definitions
                                        foreach (var existing in existingTanks)
                                        {
                                            if (!processedNames.Contains(existing.TankName) &&
                                                !tanks.Any(t => string.Equals(t.TankName, existing.TankName, StringComparison.OrdinalIgnoreCase)))
                                            {
                                                context.TankDefinitions.Remove(existing);
                                                configChanged = true;
                                            }
                                        }
                                    }
                                }

                                // 2. Collection Types (preserve shift enable flags & OTP configuration)
                                if (await SyncCollectionTypesFromJsonAsync(context, currentSetting.CollectionTypesJson, removeMissingNonSystem: true))
                                {
                                    configChanged = true;
                                }

                                // 3. Features
                                if (!string.IsNullOrWhiteSpace(currentSetting.AppFeatureSettingsJson))
                                {
                                    var features = JsonConvert.DeserializeObject<List<AppFeatureSetting>>(currentSetting.AppFeatureSettingsJson);
                                    if (features != null && features.Count > 0)
                                    {
                                        foreach (var feat in features)
                                        {
                                            var existingFeat = await context.AppFeatureSettings.FirstOrDefaultAsync(x => x.FeatureKey == feat.FeatureKey && x.TargetRole == feat.TargetRole);
                                            if (existingFeat != null)
                                            {
                                                if (existingFeat.IsEnabled != feat.IsEnabled ||
                                                    existingFeat.DisplayName != feat.DisplayName ||
                                                    existingFeat.Category != feat.Category ||
                                                    existingFeat.DisplayOrder != feat.DisplayOrder)
                                                {
                                                    existingFeat.IsEnabled = feat.IsEnabled;
                                                    existingFeat.DisplayName = feat.DisplayName;
                                                    existingFeat.Category = feat.Category;
                                                    existingFeat.DisplayOrder = feat.DisplayOrder;
                                                    existingFeat.UpdatedAt = DateTime.Now;
                                                    configChanged = true;
                                                }
                                            }
                                            else
                                            {
                                                context.AppFeatureSettings.Add(new AppFeatureSetting
                                                {
                                                    FeatureKey = feat.FeatureKey,
                                                    TargetRole = feat.TargetRole,
                                                    DisplayName = feat.DisplayName,
                                                    Category = feat.Category,
                                                    DisplayOrder = feat.DisplayOrder,
                                                    IsEnabled = feat.IsEnabled,
                                                    UpdatedAt = DateTime.Now
                                                });
                                                configChanged = true;
                                            }
                                        }
                                    }
                                }

                                // 4. Pump Mappings
                                if (!string.IsNullOrWhiteSpace(currentSetting.PumpMappingsJson))
                                {
                                    var dynamicPumpMappings = JsonConvert.DeserializeObject<List<PumpMapping>>(currentSetting.PumpMappingsJson);
                                    if (dynamicPumpMappings != null && dynamicPumpMappings.Count > 0)
                                    {
                                        var existingMappings = await context.PumpMappings.ToListAsync();
                                        var processedIds = new HashSet<int>();

                                        foreach (var m in dynamicPumpMappings)
                                        {
                                            var existing = existingMappings.FirstOrDefault(x => x.PumpId == m.PumpId && x.NozzleNumber == m.NozzleNumber && !processedIds.Contains(x.PumpMappingId));
                                            if (existing == null)
                                            {
                                                existing = existingMappings.FirstOrDefault(x => x.NozzleNumber == m.NozzleNumber && !processedIds.Contains(x.PumpMappingId));
                                            }

                                            if (existing != null)
                                            {
                                                if (existing.PumpId != m.PumpId ||
                                                    existing.NozzleNumber != m.NozzleNumber ||
                                                    existing.FuelType != m.FuelType ||
                                                    existing.TankName != m.TankName ||
                                                    existing.IsActive != m.IsActive)
                                                {
                                                    existing.PumpId = m.PumpId;
                                                    existing.NozzleNumber = m.NozzleNumber;
                                                    existing.FuelType = m.FuelType;
                                                    existing.TankName = m.TankName;
                                                    existing.IsActive = m.IsActive;
                                                    configChanged = true;
                                                }
                                                processedIds.Add(existing.PumpMappingId);
                                            }
                                            else
                                            {
                                                var newEntity = new PumpMapping
                                                {
                                                    PumpId = m.PumpId,
                                                    NozzleNumber = m.NozzleNumber,
                                                    FuelType = m.FuelType,
                                                    TankName = m.TankName,
                                                    IsActive = m.IsActive,
                                                    CreatedAt = DateTime.Now
                                                };
                                                context.PumpMappings.Add(newEntity);
                                                await context.SaveChangesAsync();
                                                processedIds.Add(newEntity.PumpMappingId);
                                                configChanged = true;
                                            }
                                        }

                                        // Crucial: remove obsolete seeded mappings not present in station's configured pump mappings
                                        foreach (var existing in existingMappings)
                                        {
                                            if (!processedIds.Contains(existing.PumpMappingId))
                                            {
                                                context.PumpMappings.Remove(existing);
                                                configChanged = true;
                                            }
                                        }
                                    }
                                }

                                await context.SaveChangesAsync();

                                if (configChanged)
                                {
                                    var updatedTanks = await context.TankDefinitions.ToListAsync();
                                    var updatedMappings = await context.PumpMappings.ToListAsync();
                                    FuelPro.Core.Common.PumpConfiguration.InitializeFromDb(updatedMappings);
                                    FuelPro.Core.Common.PumpConfiguration.InitializeTanksFromDb(updatedTanks);
                                    FuelPro.Core.Services.DsmEntryService.RaiseStationConfigurationChanged();
                                    configOrProfileDsmChanged = true;
                                    _serviceProvider.GetService<FuelPro.Core.Services.ICollectionTypeService>()?.NotifyCollectionTypesChanged();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to deserialize dynamic configurations from Settings");
                        }
                    }
                    else if (tableDef.TableName == "PumpMappings")
                    {
                        try
                        {
                            if (records.Count > 0)
                            {
                                var pulledNozzleKeys = new HashSet<(int PumpId, int NozzleNumber)>();
                                foreach (var r in records)
                                {
                                    int pId = 0;
                                    if (r.TryGetValue("PumpId", out var p) && p != null) pId = Convert.ToInt32(p);
                                    else if (r.TryGetValue("pump_id", out var p2) && p2 != null) pId = Convert.ToInt32(p2);

                                    int nNo = 0;
                                    if (r.TryGetValue("NozzleNumber", out var n) && n != null) nNo = Convert.ToInt32(n);
                                    else if (r.TryGetValue("nozzle_number", out var n2) && n2 != null) nNo = Convert.ToInt32(n2);

                                    if (pId > 0 && nNo > 0) pulledNozzleKeys.Add((pId, nNo));
                                }

                                await context.SaveChangesAsync();
                            }

                            var currentMappings = await context.PumpMappings.ToListAsync();
                            FuelPro.Core.Common.PumpConfiguration.InitializeFromDb(currentMappings);

                            // Dynamically update / synchronize TankDefinitions from PumpMappings if TankDefinitions table is not standalone in cloud
                            var distinctTanksFromMappings = currentMappings
                                .Where(m => !string.IsNullOrWhiteSpace(m.TankName))
                                .GroupBy(m => m.TankName.Trim(), StringComparer.OrdinalIgnoreCase)
                                .ToList();

                            if (distinctTanksFromMappings.Count > 0)
                            {
                                var existingTanks = await context.TankDefinitions.ToListAsync();
                                var validNames = new HashSet<string>(distinctTanksFromMappings.Select(g => g.Key), StringComparer.OrdinalIgnoreCase);

                                // Load tanks from Settings.TankDefinitionsJson to ensure custom tanks are not deleted
                                var currentSettings = await context.Settings.FirstOrDefaultAsync();
                                if (!string.IsNullOrWhiteSpace(currentSettings?.TankDefinitionsJson))
                                {
                                    try
                                    {
                                        var jsonTanks = JsonConvert.DeserializeObject<List<TankDefinition>>(currentSettings.TankDefinitionsJson);
                                        if (jsonTanks != null)
                                        {
                                            foreach (var jt in jsonTanks)
                                            {
                                                if (!string.IsNullOrWhiteSpace(jt.TankName))
                                                    validNames.Add(jt.TankName.Trim());
                                            }
                                        }
                                    }
                                    catch { }
                                }

                                foreach (var et in existingTanks)
                                {
                                    if (!validNames.Contains(et.TankName))
                                    {
                                        context.TankDefinitions.Remove(et);
                                    }
                                }

                                foreach (var g in distinctTanksFromMappings)
                                {
                                    var tankName = g.Key;
                                    var fuelType = g.First().FuelType;
                                    var existing = existingTanks.FirstOrDefault(t => string.Equals(t.TankName, tankName, StringComparison.OrdinalIgnoreCase));
                                    if (existing != null)
                                    {
                                        existing.FuelType = fuelType;
                                        existing.IsActive = true;
                                    }
                                    else
                                    {
                                        context.TankDefinitions.Add(new TankDefinition
                                        {
                                            TankName = tankName,
                                            FuelType = fuelType,
                                            CapacityKL = tankName.Contains("CNG", StringComparison.OrdinalIgnoreCase) ? 5 : 20,
                                            HasTesting = !tankName.Contains("CNG", StringComparison.OrdinalIgnoreCase),
                                            IsActive = true,
                                            CreatedAt = DateTime.Now
                                        });
                                    }
                                }
                                await context.SaveChangesAsync();
                                var currentTanks = await context.TankDefinitions.ToListAsync();
                                FuelPro.Core.Common.PumpConfiguration.InitializeTanksFromDb(currentTanks);
                            }

                            FuelPro.Core.Services.DsmEntryService.RaiseStationConfigurationChanged();
                            configOrProfileDsmChanged = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to reinitialize pump mappings after pull");
                        }
                    }
                    else if (tableDef.TableName == "TankDefinitions")
                    {
                        try
                        {
                            if (records.Count > 0)
                            {
                                var pulledTankNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                foreach (var r in records)
                                {
                                    string? tName = null;
                                    if (r.TryGetValue("TankName", out var t) && t != null) tName = t.ToString();
                                    else if (r.TryGetValue("tank_name", out var t2) && t2 != null) tName = t2.ToString();

                                    if (!string.IsNullOrWhiteSpace(tName)) pulledTankNames.Add(tName.Trim());
                                }

                                var allLocalTanks = await context.TankDefinitions.ToListAsync();
                                foreach (var localTank in allLocalTanks)
                                {
                                    if (!pulledTankNames.Contains(localTank.TankName))
                                    {
                                        context.TankDefinitions.Remove(localTank);
                                    }
                                }
                                await context.SaveChangesAsync();
                            }

                            var currentTanks = await context.TankDefinitions.ToListAsync();
                            FuelPro.Core.Common.PumpConfiguration.InitializeTanksFromDb(currentTanks);
                            FuelPro.Core.Services.DsmEntryService.RaiseStationConfigurationChanged();
                            configOrProfileDsmChanged = true;
                            _serviceProvider.GetService<IStationConfigurationService>()?.GetAllTanksAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to reinitialize tank definitions after pull");
                        }
                    }
                    else if (tableDef.TableName == "CollectionTypes")
                    {
                        try
                        {
                            _serviceProvider.GetService<FuelPro.Core.Services.ICollectionTypeService>()?.NotifyCollectionTypesChanged();
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Failed to notify collection types after pull");
                        }
                    }
                    else if (tableDef.TableName == "DsmPumpAssignments" || tableDef.TableName == "DsmUsers")
                    {
                        try
                        {
                            FuelPro.Core.Services.DsmEntryService.RaiseDsmProfileChanged();
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning(ex, "Failed to raise DsmProfileChanged after pulling {Table}", tableDef.TableName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing pulled records for table {Table}", tableDef.TableName);
                pullSucceededCompletely = false;
            }
            finally
            {
                FuelProDbContext.BypassTracking = false;
            }
        }

        // Targeted, idempotent backfill ONLY when evidence shows a local PaymentCollection has all zeros
        // while its parent DsmEntry has saved TotalCollection/TotalInDirect > 0. Never overwrites non-zero values.
        try
        {
            if (await BackfillZeroHistoricalPaymentCollectionsAsync(context, settings, localToGuid))
            {
                operationalDataChanged = true;
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Targeted historical PaymentCollections backfill failed; will retry on next cycle.");
        }

        // Raise UI refresh notifications ONLY after all operational tables in the pull cycle are persisted.
        if (operationalDataChanged || configOrProfileDsmChanged)
        {
            try
            {
                FuelPro.Core.Services.DsmEntryService.RaiseDsmEntryChanged();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error in DsmEntryChanged subscriber after pull");
            }
        }

        if (pettyCashChanged)
        {
            try { FuelPro.Core.Services.DsmEntryService.RaisePettyCashChanged(); }
            catch (Exception ex) { _logger.Warning(ex, "Error in PettyCashChanged subscriber after pull"); }
        }
        if (inventoryChanged)
        {
            try { FuelPro.Core.Services.DsmEntryService.RaiseInventoryChanged(); }
            catch (Exception ex) { _logger.Warning(ex, "Error in InventoryChanged subscriber after pull"); }
        }
        if (payrollChanged)
        {
            try { FuelPro.Core.Services.DsmEntryService.RaisePayrollChanged(); }
            catch (Exception ex) { _logger.Warning(ex, "Error in PayrollChanged subscriber after pull"); }
        }
        if (debtorChanged)
        {
            try { FuelPro.Core.Services.DsmEntryService.RaiseDebtorChanged(); }
            catch (Exception ex) { _logger.Warning(ex, "Error in DebtorChanged subscriber after pull"); }
        }

        if (operationalDataChanged)
        {
            try
            {
                OperationalDataPulled?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error in OperationalDataPulled subscriber");
            }
        }

        // Update checkpoints: only advance LastPullTime if all tables were pulled and saved completely
        if (pullSucceededCompletely)
        {
            settings.LastPullTime = pullStartTime;
            settings.LastSyncTime = pullStartTime;
            await _configService.SaveSettingsAsync(settings);

            UpdateStatus(settings.LastSyncTime, 0, true, "Synced");
            try
            {
                PullCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error in PullCompleted subscriber");
            }
        }
        else
        {
            _logger.Warning("PerformPullAsync completed with partial table failures. Checkpoint LastPullTime ({LastPullTime}) was retained to ensure retry on next cycle.", settings.LastPullTime);
            UpdateStatus(settings.LastSyncTime, 0, true, "Sync completed with warnings (retrying pending tables...)");
        }
    }

    /// <summary>
    /// Explicitly pushes the complete station configuration snapshot (pumps, nozzles, tanks, features, collections)
    /// directly to Supabase cloud under the active StationId.
    /// <summary>
    /// Explicitly pushes the complete station configuration snapshot (pumps, nozzles, tanks, features, collections)
    /// directly to Supabase cloud under the active StationId.
    /// </summary>
    public async Task<(bool Success, string Message)> PushStationSnapshotToCloudAsync(string? specificStationId = null)
    {
        var settings = await _configService.GetSettingsAsync();
        if (string.IsNullOrEmpty(settings.SupabaseUrl) || string.IsNullOrEmpty(settings.SupabaseApiKey))
        {
            _logger.Warning("Cannot push station snapshot: Supabase URL or API Key is missing.");
            return (false, "Supabase URL or API Key is not configured. Please enter credentials in Cloud Config.");
        }

        var stationId = !string.IsNullOrWhiteSpace(specificStationId) ? specificStationId.Trim() : settings.StationId.Trim();
        if (string.IsNullOrWhiteSpace(stationId))
        {
            _logger.Warning("Cannot push station snapshot: StationId is empty.");
            return (false, "Station ID is empty. Please set a Station ID.");
        }

        // Multi-Computer Security Guard: Check device authorization before pushing station snapshot
        if (settings.MultiComputerEnabled && _pairingService != null && !string.IsNullOrEmpty(settings.MachineId))
        {
            var (isAuthorized, reason) = await _pairingService.CheckDeviceAuthorizationAsync(stationId, settings.MachineId);
            if (!isAuthorized)
            {
                _logger.Warning("PushStationSnapshotToCloudAsync aborted: Workstation {MachineId} authorization is revoked: {Reason}", settings.MachineId, reason);
                return (false, $"Workstation authorization revoked: {reason}");
            }
        }

        try
        {
            _httpClient.Configure(settings.SupabaseUrl, settings.SupabaseApiKey);

            using var scope = _serviceProvider.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

            var localSetting = await context.Settings
                .OrderBy(s => s.SettingId)
                .FirstOrDefaultAsync();
            if (localSetting == null)
            {
                localSetting = new Setting();
                context.Settings.Add(localSetting);
            }

            // 1. Storage Tanks
            var allTanks = await context.TankDefinitions.ToListAsync();
            foreach (var t in allTanks)
            {
                t.Rate = localSetting.GetRateFor(t.TankName, localSetting.GetRateFor(t.FuelType, 0.0));
            }
            localSetting.TankDefinitionsJson = JsonConvert.SerializeObject(allTanks);

            // 2. Pump Mappings
            var allMappings = await context.PumpMappings.ToListAsync();
            localSetting.PumpMappingsJson = JsonConvert.SerializeObject(allMappings);

            // 3. Collection Types
            var allCollections = await context.CollectionTypes.ToListAsync();
            localSetting.CollectionTypesJson = JsonConvert.SerializeObject(allCollections);

            // 4. Feature Settings
            var allFeatures = await context.AppFeatureSettings.ToListAsync();
            localSetting.AppFeatureSettingsJson = JsonConvert.SerializeObject(allFeatures);

            // 5. Pump Connection Rules
            var metaRules = await context.AppMeta.AsNoTracking().FirstOrDefaultAsync(m => m.Key == "Station.PumpConnectionRules");
            if (metaRules != null && !string.IsNullOrWhiteSpace(metaRules.Value))
            {
                localSetting.PumpConnectionRulesJson = metaRules.Value;
            }

            await context.SaveChangesAsync();

            if (localSetting.SettingId <= 0)
            {
                _logger.Error("Cannot push station snapshot: persisted Settings record has invalid SettingId={SettingId} for station {StationId}.",
                    localSetting.SettingId, stationId);
                return (false, "Local Settings record could not be persisted cleanly before cloud sync.");
            }

            var encodedStationId = Uri.EscapeDataString(stationId);

            // Load existing local SyncIdMappings for Settings up-front so remote selection and
            // reconciliation never violate UNIQUE(TableName, RemoteGuid) or steal ownership.
            var localSettingsMappings = await context.SyncIdMappings
                .Where(m => m.TableName == "Settings")
                .OrderBy(m => m.Id)
                .ToListAsync();
            var existingMappingForLocal = localSettingsMappings
                .FirstOrDefault(m => m.LocalId == localSetting.SettingId);
            var mappingByRemoteGuid = new Dictionary<string, SyncIdMapping>(StringComparer.OrdinalIgnoreCase);
            foreach (var mapping in localSettingsMappings)
            {
                if (!string.IsNullOrWhiteSpace(mapping.RemoteGuid) && !mappingByRemoteGuid.ContainsKey(mapping.RemoteGuid.Trim()))
                {
                    mappingByRemoteGuid[mapping.RemoteGuid.Trim()] = mapping;
                }
            }

            // Deterministically inspect remote Settings rows for this station (ordered newest-first)
            string? remoteSyncGuid = null;
            int? remoteLocalId = null;
            int? remoteSettingId = null;
            try
            {
                var checkResp = await _httpClient.SendRequestAsync(HttpMethod.Get, $"Settings?station_id=eq.{encodedStationId}&order=updated_at.desc&limit=10");
                if (!checkResp.IsSuccessStatusCode)
                {
                    checkResp = await _httpClient.SendRequestAsync(HttpMethod.Get, $"Settings?StationId=eq.{encodedStationId}&order=updated_at.desc&limit=10");
                }
                if (checkResp.IsSuccessStatusCode)
                {
                    var checkJson = await checkResp.Content.ReadAsStringAsync();
                    var checkList = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(checkJson);
                    var selectedRemoteRow = SelectDeterministicRemoteSettingsRow(
                        checkList,
                        existingMappingForLocal?.RemoteGuid,
                        mappingByRemoteGuid,
                        out int remoteCandidateCount);

                    if (remoteCandidateCount > 1)
                    {
                        _logger.Warning(
                            "Multiple remote Settings records ({Count}) found in Supabase for station {StationId}. Selecting deterministic target without merging or deleting records.",
                            remoteCandidateCount, stationId);
                    }

                    if (selectedRemoteRow != null)
                    {
                        if (TryGetDictValue(selectedRemoteRow, "SyncGuid", out var sgVal) && sgVal != null && !string.IsNullOrWhiteSpace(sgVal.ToString()))
                        {
                            remoteSyncGuid = sgVal.ToString()!.Trim();
                        }
                        if (TryGetDictValue(selectedRemoteRow, "local_id", out var lidVal) && lidVal != null && int.TryParse(lidVal.ToString(), out var parsedLocalId) && parsedLocalId > 0)
                        {
                            remoteLocalId = parsedLocalId;
                        }
                        if (TryGetDictValue(selectedRemoteRow, "SettingId", out var sidVal) && sidVal != null && int.TryParse(sidVal.ToString(), out var parsedSettingId) && parsedSettingId > 0)
                        {
                            remoteSettingId = parsedSettingId;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not pre-fetch existing Settings row from Supabase for station {StationId}", stationId);
            }

            string targetSyncGuid;
            SyncIdMapping? remoteOwnerMapping = null;
            if (!string.IsNullOrEmpty(remoteSyncGuid))
            {
                targetSyncGuid = remoteSyncGuid;
                if (mappingByRemoteGuid.TryGetValue(targetSyncGuid, out remoteOwnerMapping))
                {
                    // Preserve existing (TableName, RemoteGuid) ownership — never overwrite or steal another LocalId's mapping
                    if (remoteOwnerMapping.LocalId != localSetting.SettingId)
                    {
                        _logger.Warning(
                            "Remote Settings SyncGuid {SyncGuid} for station {StationId} is already mapped to LocalId={OwnerLocalId} (pushing from LocalId={CurrentLocalId}). Preserving existing SyncIdMapping ownership.",
                            targetSyncGuid, stationId, remoteOwnerMapping.LocalId, localSetting.SettingId);
                    }
                }
                else if (existingMappingForLocal == null)
                {
                    context.SyncIdMappings.Add(new SyncIdMapping
                    {
                        TableName = "Settings",
                        LocalId = localSetting.SettingId,
                        RemoteGuid = targetSyncGuid
                    });
                    await context.SaveChangesAsync();
                }
                else if (!string.Equals(existingMappingForLocal.RemoteGuid, targetSyncGuid, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Information("Reconciling local Settings SyncIdMapping from {OldGuid} to established remote SyncGuid {NewGuid} for station {StationId}",
                        existingMappingForLocal.RemoteGuid, targetSyncGuid, stationId);
                    existingMappingForLocal.RemoteGuid = targetSyncGuid;
                    await context.SaveChangesAsync();
                }
            }
            else if (existingMappingForLocal != null && !string.IsNullOrEmpty(existingMappingForLocal.RemoteGuid))
            {
                targetSyncGuid = existingMappingForLocal.RemoteGuid;
            }
            else
            {
                targetSyncGuid = Guid.NewGuid().ToString();
                while (mappingByRemoteGuid.ContainsKey(targetSyncGuid))
                {
                    targetSyncGuid = Guid.NewGuid().ToString();
                }
                context.SyncIdMappings.Add(new SyncIdMapping
                {
                    TableName = "Settings",
                    LocalId = localSetting.SettingId,
                    RemoteGuid = targetSyncGuid
                });
                await context.SaveChangesAsync();
            }

            // Preserve established remote local_id / SettingId when found, otherwise the owning mapping's LocalId or persisted local SettingId
            int fallbackLocalId = remoteOwnerMapping != null && remoteOwnerMapping.LocalId > 0
                ? remoteOwnerMapping.LocalId
                : localSetting.SettingId;
            int targetLocalId = remoteLocalId ?? fallbackLocalId;
            int targetSettingId = remoteSettingId ?? fallbackLocalId;

            // Determine authoritative Speed rate strictly from Speed configuration (never fallback to MsIRate/MsIIRate, and never alias from XP95 or Power)
            double? authoritativeSpeedRate = null;
            if (localSetting.FuelRates.TryGetValue("Speed", out var sRate) && sRate > 0)
                authoritativeSpeedRate = sRate;
            else if (localSetting.FuelRates.TryGetValue("SPEED", out var sRate2) && sRate2 > 0)
                authoritativeSpeedRate = sRate2;
            else if (localSetting.FuelRates.TryGetValue("Speed - 20KL", out var sTankRate) && sTankRate > 0)
                authoritativeSpeedRate = sTankRate;
            else if (localSetting.FuelRates.TryGetValue("SPEED - 20KL", out var sTankRate2) && sTankRate2 > 0)
                authoritativeSpeedRate = sTankRate2;
            else
            {
                var sVal = localSetting.GetRateFor("SPEED", 0.0);
                if (sVal > 0) authoritativeSpeedRate = sVal;
            }

            // Prepare Payload for Supabase Settings table (always includes required NOT NULL identity columns)
            var lastUpdatedValue = localSetting.LastUpdated == default ? DateTime.Now : localSetting.LastUpdated;
            var stationDisplayName = string.IsNullOrWhiteSpace(localSetting.PumpStationName) ? "Fuel Station" : localSetting.PumpStationName;
            var payload = new Dictionary<string, object?>
            {
                ["SyncGuid"] = targetSyncGuid,
                ["station_id"] = stationId,
                ["local_id"] = targetLocalId,
                ["machine_id"] = settings.MachineId,
                ["SettingId"] = targetSettingId,
                ["PumpStationName"] = stationDisplayName,
                ["StationName"] = stationDisplayName,
                ["LastUpdated"] = SyncTimestampHelper.FormatUtcIso(lastUpdatedValue),
                ["PumpMappingsJson"] = localSetting.PumpMappingsJson,
                ["PumpConnectionRulesJson"] = localSetting.PumpConnectionRulesJson,
                ["TankDefinitionsJson"] = localSetting.TankDefinitionsJson,
                ["CollectionTypesJson"] = localSetting.CollectionTypesJson,
                ["AppFeatureSettingsJson"] = localSetting.AppFeatureSettingsJson,
                ["FuelRatesJson"] = localSetting.FuelRatesJson,
                ["fuel_rates_json"] = localSetting.FuelRatesJson,
                ["HsdRate"] = localSetting.HsdRate,
                ["hsd_rate"] = localSetting.HsdRate,
                ["MsIRate"] = localSetting.MsIRate,
                ["ms_i_rate"] = localSetting.MsIRate,
                ["MsIIRate"] = localSetting.MsIIRate,
                ["ms_ii_rate"] = localSetting.MsIIRate,
                ["SpeedRate"] = authoritativeSpeedRate,
                ["speed_rate"] = authoritativeSpeedRate,
                ["CngRate"] = localSetting.CngRate,
                ["cng_rate"] = localSetting.CngRate,
                ["updated_at"] = DateTime.UtcNow.ToString("o")
            };

            StripKnownMissingColumns("Settings", payload);

            var json = JsonConvert.SerializeObject(payload);
            var response = await _httpClient.SendRequestAsync(HttpMethod.Post, "Settings", json, isUpsert: true, onConflict: "SyncGuid");

            // Auto-strip missing column retry loop (e.g. PGRST204: Could not find the 'xyz' column)
            while (!response.IsSuccessStatusCode && response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                var errorStr = await response.Content.ReadAsStringAsync();
                var colMatch = System.Text.RegularExpressions.Regex.Match(errorStr, @"Could not find the '([^']+)' column");
                if (colMatch.Success)
                {
                    var missingCol = colMatch.Groups[1].Value;
                    RecordKnownMissingColumn("Settings", missingCol);
                    _logger.Warning("Supabase Settings table is missing column '{Col}'. Stripping and retrying push...", missingCol);
                    payload.Remove(missingCol);
                    json = JsonConvert.SerializeObject(payload);
                    response = await _httpClient.SendRequestAsync(HttpMethod.Post, "Settings", json, isUpsert: true, onConflict: "SyncGuid");
                }
                else
                {
                    break;
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.Warning("PushStationSnapshotToCloudAsync failed with on_conflict=SyncGuid: {Error}. Retrying with station_id onConflict...", error);
                response = await _httpClient.SendRequestAsync(HttpMethod.Post, "Settings", json, isUpsert: true, onConflict: "station_id");
            }

            bool patchUpdatedAnyRow = false;
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("PushStationSnapshotToCloudAsync retrying via direct PATCH Settings?station_id=eq.{StationId}...", stationId);
                var patchResponse = await _httpClient.SendRequestAsync(HttpMethod.Patch, $"Settings?station_id=eq.{encodedStationId}", json, returnRepresentation: true);
                if (!patchResponse.IsSuccessStatusCode)
                {
                    patchResponse = await _httpClient.SendRequestAsync(HttpMethod.Patch, $"Settings?StationId=eq.{encodedStationId}", json, returnRepresentation: true);
                }

                if (patchResponse.IsSuccessStatusCode)
                {
                    var patchBody = await patchResponse.Content.ReadAsStringAsync();
                    patchUpdatedAnyRow = DidPatchUpdateRows(patchBody);
                    if (patchUpdatedAnyRow)
                    {
                        response = patchResponse;
                    }
                    else
                    {
                        _logger.Warning("PushStationSnapshotToCloudAsync PATCH returned success with 0 updated rows for station {StationId}. Falling through to insert.", stationId);
                    }
                }
                else
                {
                    response = patchResponse;
                }
            }

            if (!response.IsSuccessStatusCode && !patchUpdatedAnyRow)
            {
                _logger.Warning("PushStationSnapshotToCloudAsync retrying via plain POST Settings...");
                response = await _httpClient.SendRequestAsync(HttpMethod.Post, "Settings", json, isUpsert: false);
            }

            if (response.IsSuccessStatusCode)
            {
                _logger.Information("Successfully pushed complete Station Snapshot to Supabase for station {StationId}", stationId);
                // Also trigger full force sync to push any individual changed rows
                _ = Task.Run(async () =>
                {
                    try { await ForceSyncAsync(); } catch { }
                });
                return (true, $"Station layout and configuration successfully saved to Cloud for Station '{stationId}'.");
            }
            else
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.Error("Failed to push station snapshot to Supabase: {Error}", err);
                return (false, $"Supabase push failed (HTTP {response.StatusCode}): {err}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception in PushStationSnapshotToCloudAsync");
            return (false, $"Exception during cloud push: {ex.Message}");
        }
    }

    /// <summary>
    /// Explicitly queries Supabase for the station snapshot for the active StationId,
    /// merges the pump mappings, tanks, collection types, and feature toggles into local SQLite,
    /// cleans up obsolete seed defaults, re-initializes memory caches, and notifies UI.
    /// </summary>
    public async Task<(bool Success, string Message)> PullStationSnapshotFromCloudAsync(string? specificStationId = null)
    {
        var settings = await _configService.GetSettingsAsync();
        if (string.IsNullOrEmpty(settings.SupabaseUrl) || string.IsNullOrEmpty(settings.SupabaseApiKey))
        {
            _logger.Warning("Cannot pull station snapshot: Supabase URL or API Key is missing.");
            return (false, "Supabase URL or API Key is not configured. Please enter credentials in Cloud Config.");
        }

        var stationId = !string.IsNullOrWhiteSpace(specificStationId) ? specificStationId.Trim() : settings.StationId.Trim();
        if (string.IsNullOrWhiteSpace(stationId))
        {
            _logger.Warning("Cannot pull station snapshot: StationId is empty.");
            return (false, "Station ID is empty. Please set a Station ID.");
        }

        try
        {
            _httpClient.Configure(settings.SupabaseUrl, settings.SupabaseApiKey);

            _logger.Information("Pulling station snapshot from Supabase for Station {StationId}...", stationId);
            var encodedStationId = Uri.EscapeDataString(stationId);
            var response = await _httpClient.SendRequestAsync(HttpMethod.Get, $"Settings?station_id=eq.{encodedStationId}&order=updated_at.desc&limit=1");
            if (!response.IsSuccessStatusCode)
            {
                response = await _httpClient.SendRequestAsync(HttpMethod.Get, $"Settings?StationId=eq.{encodedStationId}&order=updated_at.desc&limit=1");
            }

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                _logger.Warning("Failed to pull Settings for station {StationId}: {Error}", stationId, err);
                return (false, $"Failed to query Supabase (HTTP {response.StatusCode}): {err}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var rawList = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(json);
            if (rawList == null || rawList.Count == 0)
            {
                _logger.Warning("No Settings snapshot found in Supabase for station {StationId}", stationId);
                return (false, $"No cloud snapshot found in Supabase for Station ID '{stationId}'. Please click 'Save Preset to Cloud' or 'Push to Cloud' on the configuration machine first.");
            }

            var dict = new Dictionary<string, object?>(rawList[0], StringComparer.OrdinalIgnoreCase);

            using var scope = _serviceProvider.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

            var localSetting = await context.Settings
                .OrderBy(s => s.SettingId)
                .FirstOrDefaultAsync();
            if (localSetting == null)
            {
                localSetting = new Setting();
                context.Settings.Add(localSetting);
            }

            if (TryGetDictValue(dict, "StationName", out var snVal) && snVal != null)
                localSetting.PumpStationName = snVal.ToString()!;
            else if (TryGetDictValue(dict, "PumpStationName", out var psnVal) && psnVal != null)
                localSetting.PumpStationName = psnVal.ToString()!;

            // Persist localSetting early if new so its SettingId can be mapped cleanly to remote SyncGuid
            if (localSetting.SettingId == 0)
            {
                await context.SaveChangesAsync();
            }

            if (TryGetDictValue(dict, "SyncGuid", out var pulledSgVal) && pulledSgVal != null && !string.IsNullOrWhiteSpace(pulledSgVal.ToString()))
            {
                var pulledGuid = pulledSgVal.ToString()!.Trim();
                var allSettingsMaps = await context.SyncIdMappings
                    .Where(m => m.TableName == "Settings")
                    .OrderBy(m => m.Id)
                    .ToListAsync();
                var existingGuidOwner = allSettingsMaps
                    .FirstOrDefault(m => string.Equals(m.RemoteGuid, pulledGuid, StringComparison.OrdinalIgnoreCase));

                if (existingGuidOwner == null)
                {
                    var existingSettingMap = allSettingsMaps.FirstOrDefault(m => m.LocalId == localSetting.SettingId);
                    if (existingSettingMap == null)
                    {
                        context.SyncIdMappings.Add(new SyncIdMapping
                        {
                            TableName = "Settings",
                            LocalId = localSetting.SettingId,
                            RemoteGuid = pulledGuid
                        });
                    }
                    else if (!string.Equals(existingSettingMap.RemoteGuid, pulledGuid, StringComparison.OrdinalIgnoreCase))
                    {
                        existingSettingMap.RemoteGuid = pulledGuid;
                    }
                }
                else if (existingGuidOwner.LocalId != localSetting.SettingId)
                {
                    _logger.Warning(
                        "PullStationSnapshotFromCloudAsync preserved existing SyncIdMapping ownership (LocalId={OwnerLocalId}) for remote Settings SyncGuid {SyncGuid}.",
                        existingGuidOwner.LocalId, pulledGuid);
                }
            }

            // 1. Tanks
            if (TryGetDictValue(dict, "TankDefinitionsJson", out var tanksJsonObj) && tanksJsonObj != null)
            {
                var tanksJson = tanksJsonObj.ToString();
                if (!string.IsNullOrWhiteSpace(tanksJson))
                {
                    localSetting.TankDefinitionsJson = tanksJson;
                    var tanks = JsonConvert.DeserializeObject<List<TankDefinition>>(tanksJson);
                    if (tanks != null && tanks.Count > 0)
                    {
                        var existingTanks = await context.TankDefinitions.ToListAsync();
                        var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var t in tanks)
                        {
                            var existingTank = existingTanks.FirstOrDefault(x => string.Equals(x.TankName, t.TankName, StringComparison.OrdinalIgnoreCase));
                            if (existingTank != null)
                            {
                                existingTank.CapacityKL = t.CapacityKL;
                                existingTank.FuelType = t.FuelType;
                                existingTank.IsActive = t.IsActive;
                                existingTank.HasTesting = t.HasTesting;
                                processedNames.Add(existingTank.TankName);
                            }
                            else
                            {
                                context.TankDefinitions.Add(new TankDefinition
                                {
                                    TankName = t.TankName,
                                    CapacityKL = t.CapacityKL,
                                    FuelType = t.FuelType,
                                    IsActive = t.IsActive,
                                    HasTesting = t.HasTesting,
                                    CreatedAt = DateTime.Now
                                });
                                processedNames.Add(t.TankName);
                            }
                        }

                        foreach (var existing in existingTanks)
                        {
                            if (!processedNames.Contains(existing.TankName))
                            {
                                context.TankDefinitions.Remove(existing);
                            }
                        }
                    }
                }
            }

            // 2. Collection Types (preserve shift enable flags & OTP configuration)
            if (TryGetDictValue(dict, "CollectionTypesJson", out var ctJsonObj) && ctJsonObj != null)
            {
                var ctJson = ctJsonObj.ToString();
                if (!string.IsNullOrWhiteSpace(ctJson))
                {
                    localSetting.CollectionTypesJson = ctJson;
                    await SyncCollectionTypesFromJsonAsync(context, ctJson, removeMissingNonSystem: false);
                }
            }

            // 3. Feature Settings
            if (TryGetDictValue(dict, "AppFeatureSettingsJson", out var featJsonObj) && featJsonObj != null)
            {
                var featJson = featJsonObj.ToString();
                if (!string.IsNullOrWhiteSpace(featJson))
                {
                    localSetting.AppFeatureSettingsJson = featJson;
                    var features = JsonConvert.DeserializeObject<List<AppFeatureSetting>>(featJson);
                    if (features != null && features.Count > 0)
                    {
                        foreach (var feat in features)
                        {
                            var existingFeat = await context.AppFeatureSettings.FirstOrDefaultAsync(x => x.FeatureKey == feat.FeatureKey && x.TargetRole == feat.TargetRole);
                            if (existingFeat != null)
                            {
                                existingFeat.IsEnabled = feat.IsEnabled;
                                existingFeat.DisplayName = feat.DisplayName;
                                existingFeat.Category = feat.Category;
                                existingFeat.DisplayOrder = feat.DisplayOrder;
                                existingFeat.UpdatedAt = DateTime.Now;
                            }
                            else
                            {
                                context.AppFeatureSettings.Add(new AppFeatureSetting
                                {
                                    FeatureKey = feat.FeatureKey,
                                    TargetRole = feat.TargetRole,
                                    DisplayName = feat.DisplayName,
                                    Category = feat.Category,
                                    DisplayOrder = feat.DisplayOrder,
                                    IsEnabled = feat.IsEnabled,
                                    UpdatedAt = DateTime.Now
                                });
                            }
                        }
                    }
                }
            }

            // 4. Pump Mappings
            if (TryGetDictValue(dict, "PumpMappingsJson", out var pmJsonObj) && pmJsonObj != null)
            {
                var pmJson = pmJsonObj.ToString();
                if (!string.IsNullOrWhiteSpace(pmJson))
                {
                    localSetting.PumpMappingsJson = pmJson;
                    var dynamicPumpMappings = JsonConvert.DeserializeObject<List<PumpMapping>>(pmJson);
                    if (dynamicPumpMappings != null && dynamicPumpMappings.Count > 0)
                    {
                        var existingMappings = await context.PumpMappings.ToListAsync();
                        var processedIds = new HashSet<int>();

                        foreach (var m in dynamicPumpMappings)
                        {
                            var existing = existingMappings.FirstOrDefault(x => x.PumpId == m.PumpId && x.NozzleNumber == m.NozzleNumber && !processedIds.Contains(x.PumpMappingId));
                            if (existing == null)
                            {
                                existing = existingMappings.FirstOrDefault(x => x.NozzleNumber == m.NozzleNumber && !processedIds.Contains(x.PumpMappingId));
                            }

                            if (existing != null)
                            {
                                existing.PumpId = m.PumpId;
                                existing.NozzleNumber = m.NozzleNumber;
                                existing.FuelType = m.FuelType;
                                existing.TankName = m.TankName;
                                existing.IsActive = m.IsActive;
                                processedIds.Add(existing.PumpMappingId);
                            }
                            else
                            {
                                var newEntity = new PumpMapping
                                {
                                    PumpId = m.PumpId,
                                    NozzleNumber = m.NozzleNumber,
                                    FuelType = m.FuelType,
                                    TankName = m.TankName,
                                    IsActive = m.IsActive,
                                    CreatedAt = DateTime.Now
                                };
                                context.PumpMappings.Add(newEntity);
                                await context.SaveChangesAsync();
                                processedIds.Add(newEntity.PumpMappingId);
                            }
                        }

                        foreach (var existing in existingMappings)
                        {
                            if (!processedIds.Contains(existing.PumpMappingId))
                            {
                                context.PumpMappings.Remove(existing);
                            }
                        }
                    }
                }
            }

            // 5. Pump Connection Rules
            if (TryGetDictValue(dict, "PumpConnectionRulesJson", out var pcrJsonObj) && pcrJsonObj != null)
            {
                var pcrJson = pcrJsonObj.ToString();
                if (!string.IsNullOrWhiteSpace(pcrJson))
                {
                    localSetting.PumpConnectionRulesJson = pcrJson;
                    var meta = await context.AppMeta.FirstOrDefaultAsync(m => m.Key == "Station.PumpConnectionRules");
                    if (meta != null)
                    {
                        meta.Value = pcrJson;
                        context.Entry(meta).State = EntityState.Modified;
                    }
                    else
                    {
                        context.AppMeta.Add(new AppMeta { Key = "Station.PumpConnectionRules", Value = pcrJson });
                    }
                }
            }

            // Also check for StationLayoutPresets if present
            try
            {
                if (localSetting.PumpMappingsJson != null && localSetting.TankDefinitionsJson != null)
                {
                    var existingPreset = await context.StationLayoutPresets.FirstOrDefaultAsync(p => p.PresetCode == $"{stationId}-PRESET");
                    var presetPayload = new StationPresetData
                    {
                        PresetName = $"{stationId} Cloud Layout",
                        PresetCode = $"{stationId}-PRESET",
                        Description = $"Preset fetched from Supabase Cloud on {DateTime.Now:dd-MMM-yyyy HH:mm}",
                        Tanks = await context.TankDefinitions.Select(t => new TankPresetItem
                        {
                            TankName = t.TankName,
                            CapacityKL = t.CapacityKL,
                            FuelType = t.FuelType,
                            IsActive = t.IsActive,
                            HasTesting = t.HasTesting
                        }).ToListAsync(),
                        Pumps = (await context.PumpMappings.ToListAsync())
                            .GroupBy(p => p.PumpId)
                            .OrderBy(g => g.Key)
                            .Select(g => new PumpPresetItem
                            {
                                PumpId = g.Key,
                                Nozzles = g.OrderBy(n => n.NozzleNumber).Select(n => new NozzlePresetItem
                                {
                                    NozzleNumber = n.NozzleNumber,
                                    FuelType = n.FuelType,
                                    TankName = n.TankName
                                }).ToList()
                            }).ToList()
                    };

                    var presetJson = JsonConvert.SerializeObject(presetPayload, Formatting.Indented);
                    if (existingPreset != null)
                    {
                        existingPreset.LayoutJson = presetJson;
                        existingPreset.PumpCount = presetPayload.Pumps.Count;
                        existingPreset.NozzleCount = presetPayload.Pumps.SelectMany(p => p.Nozzles).Count();
                        existingPreset.TankCount = presetPayload.Tanks.Count;
                        existingPreset.UpdatedAt = DateTime.Now;
                    }
                    else
                    {
                        context.StationLayoutPresets.Add(new StationLayoutPreset
                        {
                            PresetCode = $"{stationId}-PRESET",
                            PresetName = $"{stationId} Cloud Layout",
                            Description = $"Preset fetched from Supabase Cloud on {DateTime.Now:dd-MMM-yyyy HH:mm}",
                            LayoutJson = presetJson,
                            PumpCount = presetPayload.Pumps.Count,
                            NozzleCount = presetPayload.Pumps.SelectMany(p => p.Nozzles).Count(),
                            TankCount = presetPayload.Tanks.Count,
                            IsActive = true,
                            CreatedAt = DateTime.Now
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to auto-generate local preset from pulled station snapshot");
            }

            await context.SaveChangesAsync();

            // Refresh runtime state & caches
            var updatedTanks = await context.TankDefinitions.ToListAsync();
            var updatedMappings = await context.PumpMappings.ToListAsync();
            FuelPro.Core.Common.PumpConfiguration.InitializeFromDb(updatedMappings);
            FuelPro.Core.Common.PumpConfiguration.InitializeTanksFromDb(updatedTanks);
            FuelPro.Core.Services.DsmEntryService.RaiseStationConfigurationChanged();
            FuelPro.Core.Services.DsmEntryService.RaiseDsmEntryChanged();
            _serviceProvider.GetService<FuelPro.Core.Services.ICollectionTypeService>()?.NotifyCollectionTypesChanged();
            _serviceProvider.GetService<FuelPro.Core.Services.IFeatureToggleService>()?.RefreshCacheAsync();

            _logger.Information("Successfully applied complete Station Snapshot from Supabase for station {StationId}", stationId);
            return (true, $"Successfully applied station configuration from cloud for Station '{stationId}'.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception in PullStationSnapshotFromCloudAsync");
            return (false, $"Exception during cloud fetch: {ex.Message}");
        }
    }

    /// <summary>
    /// Matches an incoming remote DsmUser against local DsmUsers using station-aware natural-key verification.
    /// Prefers EmployeeCode with verification, checks MobileNumber/AuthUserId for consistency, and protects against ambiguity.
    /// </summary>
    private async Task<DsmUser?> MatchLocalDsmUserAsync(
        FuelProDbContext context,
        string? remoteEmpCode,
        string? remoteMobile,
        string? remoteAuthUser,
        string? remoteFullName,
        string? remoteStationId,
        string activeStationId)
    {
        // 1. Station context verification
        if (!string.IsNullOrEmpty(remoteStationId) && !string.IsNullOrEmpty(activeStationId) &&
            !string.Equals(remoteStationId, activeStationId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning("Refusing DsmUser natural-key match: remote station {RemoteStation} does not match active station {ActiveStation}.",
                remoteStationId, activeStationId);
            return null;
        }

        var localUsers = await context.DsmUsers.ToListAsync();
        if (localUsers.Count == 0) return null;

        DsmUser? matchedUser = null;

        // 2. Primary: Match by EmployeeCode with verification
        if (!string.IsNullOrEmpty(remoteEmpCode))
        {
            var empMatches = localUsers
                .Where(u => string.Equals(u.EmployeeCode?.Trim(), remoteEmpCode, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (empMatches.Count == 1)
            {
                var candidate = empMatches[0];
                bool mobileConflict = !string.IsNullOrEmpty(remoteMobile) && !string.IsNullOrEmpty(candidate.MobileNumber) &&
                    !string.Equals(candidate.MobileNumber.Trim(), remoteMobile, StringComparison.OrdinalIgnoreCase);
                bool authConflict = !string.IsNullOrEmpty(remoteAuthUser) && !string.IsNullOrEmpty(candidate.AuthUserId) &&
                    !string.Equals(candidate.AuthUserId.Trim(), remoteAuthUser, StringComparison.OrdinalIgnoreCase);

                if (!mobileConflict && !authConflict)
                {
                    matchedUser = candidate;
                }
                else
                {
                    _logger.Warning("DsmUsers match by EmployeeCode={EmpCode} rejected due to conflicting MobileNumber or AuthUserId. Candidate: {CandName}, Remote: {RemoteName}",
                        remoteEmpCode, candidate.FullName, remoteFullName);
                }
            }
            else if (empMatches.Count > 1)
            {
                // Disambiguate using MobileNumber, AuthUserId, or FullName
                var disambiguated = empMatches.Where(u =>
                    (!string.IsNullOrEmpty(remoteMobile) && string.Equals(u.MobileNumber?.Trim(), remoteMobile, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(remoteAuthUser) && string.Equals(u.AuthUserId?.Trim(), remoteAuthUser, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(remoteFullName) && string.Equals(u.FullName?.Trim(), remoteFullName, StringComparison.OrdinalIgnoreCase))
                ).ToList();

                if (disambiguated.Count == 1)
                {
                    matchedUser = disambiguated[0];
                }
                else
                {
                    _logger.Warning("DsmUsers match for EmployeeCode={EmpCode} is ambiguous ({Count} candidates). Skipping adoption.", remoteEmpCode, empMatches.Count);
                }
            }
        }

        // 3. Secondary: Match by MobileNumber (if not matched by EmployeeCode)
        if (matchedUser == null && !string.IsNullOrEmpty(remoteMobile))
        {
            var mobileMatches = localUsers
                .Where(u => string.Equals(u.MobileNumber?.Trim(), remoteMobile, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (mobileMatches.Count == 1)
            {
                var candidate = mobileMatches[0];
                bool authConflict = !string.IsNullOrEmpty(remoteAuthUser) && !string.IsNullOrEmpty(candidate.AuthUserId) &&
                    !string.Equals(candidate.AuthUserId.Trim(), remoteAuthUser, StringComparison.OrdinalIgnoreCase);

                if (!authConflict)
                {
                    matchedUser = candidate;
                }
            }
        }

        // 4. Tertiary: Match by AuthUserId (if not matched yet)
        if (matchedUser == null && !string.IsNullOrEmpty(remoteAuthUser))
        {
            var authMatches = localUsers
                .Where(u => string.Equals(u.AuthUserId?.Trim(), remoteAuthUser, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (authMatches.Count == 1)
            {
                matchedUser = authMatches[0];
            }
        }

        return matchedUser;
    }

    /// <summary>
    /// On-demand pulls a referenced parent record from Supabase by its SyncGuid,
    /// inserting it into the local database and registering its local ID.
    /// </summary>
    private async Task<int?> FetchAndImportMissingParentAsync(
        FuelProDbContext context,
        Dictionary<string, Dictionary<string, int>> guidToLocal,
        string parentTableName,
        string parentGuid,
        SyncSettings settings)
    {
        try
        {
            var parentEntityType = context.Model.GetEntityTypes().FirstOrDefault(t => t.GetTableName() == parentTableName);
            if (parentEntityType == null) return null;
            var pkProp = parentEntityType.FindPrimaryKey()?.Properties.FirstOrDefault();
            if (pkProp == null) return null;

            _logger.Information("On-demand pulling missing parent {ParentTable} with SyncGuid={Guid}...", parentTableName, parentGuid);
            Dictionary<string, object?>? pDict;
            if (parentTableName == "Shifts")
            {
                pDict = await ShiftParentStationScope.FetchAsync(
                    parentGuid,
                    settings.StationId,
                    async endpoint => await _httpClient.SendRequestAsync(HttpMethod.Get, endpoint));
                if (pDict == null)
                {
                    _logger.Warning("Refusing to import Shift {Guid}: no unique remote row is confirmed for station {StationId}.", parentGuid, settings.StationId);
                    return null;
                }

                var existingShiftMapping = await context.SyncIdMappings
                    .FirstOrDefaultAsync(m => m.TableName == "Shifts" && m.RemoteGuid == parentGuid);
                if (existingShiftMapping != null)
                {
                    var mappedShift = await context.Shifts.FindAsync(existingShiftMapping.LocalId);
                    if (mappedShift == null ||
                        !TryGetDictValue(pDict, "ShiftDate", out var mappedDateValue) || mappedDateValue == null ||
                        !TryGetDictValue(pDict, "ShiftType", out var mappedTypeValue) || mappedTypeValue == null)
                    {
                        return null;
                    }

                    var remoteDate = SyncTimestampHelper.ParseBusinessDate(mappedDateValue);
                    var cycleService = new ShiftCycleService();
                    var remoteType = cycleService.NormalizeShiftType(mappedTypeValue.ToString()!);
                    var localType = cycleService.NormalizeShiftType(mappedShift.ShiftType);
                    if (mappedShift.ShiftDate.Date != remoteDate ||
                        !string.Equals(localType, remoteType, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Warning("Refusing Shift mapping {Guid}: local shift does not match the station-scoped remote shift.", parentGuid);
                        return null;
                    }

                    if (!guidToLocal.ContainsKey(parentTableName))
                        guidToLocal[parentTableName] = new Dictionary<string, int>();
                    guidToLocal[parentTableName][parentGuid] = existingShiftMapping.LocalId;
                    return existingShiftMapping.LocalId;
                }

                if (guidToLocal.TryGetValue(parentTableName, out var knownShiftMap) &&
                    knownShiftMap.TryGetValue(parentGuid, out var knownLocalShiftId))
                {
                    var mappedShift = await context.Shifts.FindAsync(knownLocalShiftId);
                    if (mappedShift != null &&
                        TryGetDictValue(pDict, "ShiftDate", out var knownDateValue) && knownDateValue != null &&
                        TryGetDictValue(pDict, "ShiftType", out var knownTypeValue) && knownTypeValue != null)
                    {
                        var cycleService = new ShiftCycleService();
                        if (mappedShift.ShiftDate.Date == SyncTimestampHelper.ParseBusinessDate(knownDateValue) &&
                            string.Equals(
                                cycleService.NormalizeShiftType(mappedShift.ShiftType),
                                cycleService.NormalizeShiftType(knownTypeValue.ToString()!),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            return knownLocalShiftId;
                        }
                    }

                    _logger.Warning("Refusing Shift mapping {Guid}: station-scoped remote row does not match the local shift.", parentGuid);
                    return null;
                }
            }
            else
            {
                var response = await _httpClient.SendRequestAsync(HttpMethod.Get, $"{parentTableName}?SyncGuid=eq.{parentGuid}");
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var records = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(json, PullJsonSettings);
                if (records == null || records.Count == 0) return null;
                pDict = records[0];
            }

            // DsmUsers natural-key reconciliation: check for existing local record before blind insert
            if (parentTableName == "DsmUsers")
            {
                var remoteEmpCode = TryGetDictValue(pDict, "EmployeeCode", out var ec) && ec != null ? ec.ToString()!.Trim() : null;
                var remoteMobile = TryGetDictValue(pDict, "MobileNumber", out var mn) && mn != null ? mn.ToString()!.Trim() : null;
                var remoteAuthUser = TryGetDictValue(pDict, "AuthUserId", out var au) && au != null ? au.ToString()!.Trim() : null;
                var remoteFullName = TryGetDictValue(pDict, "FullName", out var fn) && fn != null ? fn.ToString()!.Trim() : null;
                string? remoteStation = null;
                if (TryGetDictValue(pDict, "station_id", out var st1) && st1 != null) remoteStation = st1.ToString()?.Trim();
                else if (TryGetDictValue(pDict, "StationId", out var st2) && st2 != null) remoteStation = st2.ToString()?.Trim();

                var matchedLocalUser = await MatchLocalDsmUserAsync(
                    context, remoteEmpCode, remoteMobile, remoteAuthUser, remoteFullName, remoteStation, settings.StationId);

                if (matchedLocalUser != null)
                {
                    int matchedId = matchedLocalUser.DsmUserId;
                    if (!guidToLocal.ContainsKey(parentTableName))
                        guidToLocal[parentTableName] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    guidToLocal[parentTableName][parentGuid] = matchedId;

                    var existingMap = await context.SyncIdMappings
                        .FirstOrDefaultAsync(m => m.TableName == parentTableName && m.RemoteGuid == parentGuid);
                    if (existingMap == null)
                    {
                        context.SyncIdMappings.Add(new SyncIdMapping
                        {
                            TableName = parentTableName,
                            RemoteGuid = parentGuid,
                            LocalId = matchedId
                        });
                        await context.SaveChangesAsync();
                    }
                    else if (existingMap.LocalId != matchedId)
                    {
                        existingMap.LocalId = matchedId;
                        await context.SaveChangesAsync();
                    }

                    _logger.Information("Resolved existing local DsmUser '{FullName}' (LocalId={Id}, EmployeeCode={EmpCode}) for remote SyncGuid={Guid}",
                        matchedLocalUser.FullName, matchedId, matchedLocalUser.EmployeeCode, parentGuid);
                    return matchedId;
                }

                // If mobile number already exists locally but was ambiguous or unverified, do not blind-insert
                if (!string.IsNullOrEmpty(remoteMobile) && await context.DsmUsers.AnyAsync(u => u.MobileNumber == remoteMobile))
                {
                    _logger.Warning("Refusing to blind-insert DsmUser for SyncGuid={Guid}: mobile number {Mobile} already exists locally but could not be unambiguously matched.", parentGuid, remoteMobile);
                    return null;
                }
            }

            // Resolve grandparent FKs on the parent if needed
            if (FkConfigByTable.TryGetValue(parentTableName, out var grandParentFks))
            {
                foreach (var gpFk in grandParentFks)
                {
                    if (pDict.TryGetValue(gpFk.FkProperty, out var gpVal) && gpVal != null)
                    {
                        var gpGuid = gpVal.ToString()!;
                        if (Guid.TryParse(gpGuid, out _))
                        {
                            var gpMap = guidToLocal.GetValueOrDefault(gpFk.ReferencedTable);
                            if (gpMap != null && gpMap.TryGetValue(gpGuid, out var gpLocalId))
                            {
                                pDict[gpFk.FkProperty] = gpLocalId;
                            }
                            else
                            {
                                var gpResolved = await FetchAndImportMissingParentAsync(context, guidToLocal, gpFk.ReferencedTable, gpGuid, settings);
                                if (gpResolved.HasValue)
                                    pDict[gpFk.FkProperty] = gpResolved.Value;
                            }
                        }
                    }
                }
            }

            // Create entity
            var parentEntity = Activator.CreateInstance(parentEntityType.ClrType);
            if (parentEntity == null) return null;

            var entry = context.Entry(parentEntity);
            foreach (var prop in parentEntityType.GetProperties())
            {
                if (prop.IsPrimaryKey()) continue;

                if (TryGetDictValue(pDict, prop.Name, out var val))
                {
                    if (val == null)
                    {
                        var isNullable = Nullable.GetUnderlyingType(prop.ClrType) != null || !prop.ClrType.IsValueType;
                        if (isNullable) entry.Property(prop.Name).CurrentValue = null;
                    }
                    else
                    {
                        var targetType = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
                        object? converted;
                        if (targetType == typeof(DateTime))
                            converted = SyncTimestampHelper.ParseForPull(parentEntityType.ClrType, prop.Name, val);
                        else if (targetType == typeof(Guid))
                            converted = Guid.Parse(val.ToString()!);
                        else if (targetType.IsEnum)
                            converted = Enum.Parse(targetType, val.ToString()!);
                        else
                            converted = Convert.ChangeType(val, targetType);
                        entry.Property(prop.Name).CurrentValue = converted;
                    }
                }
            }

            context.Add(parentEntity);
            await context.SaveChangesAsync();

            var newLocalId = (int)entry.Property(pkProp.Name).CurrentValue!;
            if (!guidToLocal.ContainsKey(parentTableName))
                guidToLocal[parentTableName] = new Dictionary<string, int>();
            guidToLocal[parentTableName][parentGuid] = newLocalId;

            context.SyncIdMappings.Add(new SyncIdMapping
            {
                TableName = parentTableName,
                RemoteGuid = parentGuid,
                LocalId = newLocalId
            });
            await context.SaveChangesAsync();

            _logger.Information("Successfully imported missing parent {ParentTable} (SyncGuid={Guid}) as LocalId={Id}", parentTableName, parentGuid, newLocalId);
            return newLocalId;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to on-demand import parent {ParentTable} SyncGuid={Guid}", parentTableName, parentGuid);
            return null;
        }
    }


    // ═══════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gets an existing SyncGuid for a record, or creates and persists a new one.
    /// Also updates the in-memory localToGuid dictionary.
    /// </summary>
    internal async Task<string> GetOrCreateSyncGuidAsync(
        FuelProDbContext context,
        Dictionary<string, Dictionary<int, string>> localToGuid,
        string tableName,
        int localId,
        SyncSettings settings)
    {
        if (!localToGuid.ContainsKey(tableName))
            localToGuid[tableName] = new Dictionary<int, string>();

        if (localToGuid[tableName].TryGetValue(localId, out var existingGuid))
            return existingGuid;

        // Check DB in case the in-memory cache is stale
        var existing = await context.SyncIdMappings
            .FirstOrDefaultAsync(m => m.TableName == tableName && m.LocalId == localId);
        if (existing != null && !string.IsNullOrEmpty(existing.RemoteGuid))
        {
            localToGuid[tableName][localId] = existing.RemoteGuid;
            return existing.RemoteGuid;
        }

        // Generate new GUID (deterministic canonical GUID for Shifts to guarantee cross-computer consistency)
        var newGuid = Guid.NewGuid().ToString();
        if (tableName == "Shifts")
        {
            var shift = await context.Shifts.FindAsync(localId);
            if (shift != null)
            {
                var normType = DeterministicGuid.NormalizeShiftType(shift.ShiftType);

                // 1. Check if ANY shift for the exact same (ShiftDate, normalized ShiftType)
                // already has a RemoteGuid in SyncIdMappings (e.g. historical random GUID).
                var existingSameShiftMapping = await (
                    from s in context.Shifts
                    join m in context.SyncIdMappings on s.ShiftId equals m.LocalId
                    where m.TableName == "Shifts" && s.ShiftDate.Date == shift.ShiftDate.Date
                    select new { s.ShiftId, s.ShiftType, m.RemoteGuid }
                ).ToListAsync();

                var matchingHistorical = existingSameShiftMapping
                    .FirstOrDefault(x => DeterministicGuid.NormalizeShiftType(x.ShiftType) == normType);

                if (matchingHistorical != null && !string.IsNullOrEmpty(matchingHistorical.RemoteGuid))
                {
                    _logger.Information("Reusing existing RemoteGuid {Guid} for Shift {Date:yyyy-MM-dd} {Type} (LocalId={Id})",
                        matchingHistorical.RemoteGuid, shift.ShiftDate, normType, localId);
                    localToGuid[tableName][localId] = matchingHistorical.RemoteGuid;
                    return matchingHistorical.RemoteGuid;
                }

                // 2. Generate canonical deterministic GUID based on stationId, date, and normalized shift type
                var canonicalGuid = DeterministicGuid.CreateCanonicalShiftGuid(settings.StationId, shift.ShiftDate, normType);
                var existingCanonicalMapping = await context.SyncIdMappings
                    .FirstOrDefaultAsync(m => m.TableName == "Shifts" && m.RemoteGuid == canonicalGuid);
                if (existingCanonicalMapping != null)
                {
                    localToGuid[tableName][localId] = canonicalGuid;
                    return canonicalGuid;
                }
                newGuid = canonicalGuid;
            }
        }

        context.SyncIdMappings.Add(new SyncIdMapping
        {
            TableName = tableName,
            RemoteGuid = newGuid,
            LocalId = localId
        });

        // Queue a sync log so the record is pushed to Supabase if it isn't already queued
        var hasPendingLog = await context.SyncChangeLogs
            .AnyAsync(l => l.TableName == tableName && l.RecordId == localId && !l.IsSynced);
        if (!hasPendingLog)
        {
            context.SyncChangeLogs.Add(new SyncChangeLog
            {
                TableName = tableName,
                RecordId = localId,
                Operation = "INSERT",
                CreatedAt = DateTime.Now,
                IsSynced = false,
                StationId = settings.StationId,
                MachineId = settings.MachineId,
                SyncGuid = Guid.NewGuid().ToString()
            });
            _logger.Information("Queued missing parent record for push: {Table} LocalId={Id}", tableName, localId);
        }

        await context.SaveChangesAsync();

        localToGuid[tableName][localId] = newGuid;
        _logger.Debug("Created SyncGuid {Guid} for {Table} LocalId={Id}", newGuid, tableName, localId);
        return newGuid;
    }

    /// <summary>
    /// Gets an existing SyncGuid from the in-memory cache. Returns null if not found.
    /// </summary>
    private static string? GetExistingSyncGuid(
        Dictionary<string, Dictionary<int, string>> localToGuid,
        string tableName,
        int localId)
    {
        if (localToGuid.TryGetValue(tableName, out var tableMap) && tableMap.TryGetValue(localId, out var guid))
            return guid;
        return null;
    }

    private Dictionary<string, object?> GetDatabaseValues(DbContext context, object entity)
    {
        var values = new Dictionary<string, object?>();
        var entry = context.Entry(entity);
        var tableName = entry.Metadata.GetTableName() ?? string.Empty;
        var pkProperties = entry.Metadata.FindPrimaryKey()?.Properties;
        
        var excludePkTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "DsmUsers",
            "DsmPumpAssignments",
            "DsmDevices",
            "DsmApprovalAudits",
            "DsmAttendance"
        };
        
        bool excludePk = excludePkTables.Contains(tableName);

        bool isTestEnv = Environment.GetEnvironmentVariable("FUELPRO_ENV") == "TEST";
        var testExcludeCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PhonePeDay",
            "PhonePeCardDay",
            "CreditCardDay",
            "PetroCardDay",
            "PhonePeTidDay",
            "PhonePeBatchDay",
            "CreditCardTidDay",
            "CreditCardBatchDay",
            "PetroCardTidDay",
            "PetroCardBatchDay"
        };

        foreach (var property in entry.Metadata.GetProperties())
        {
            if (property.Name == "Id" 
                && tableName != "DsmPersonalDebtors" 
                && tableName != "DsmPersonalDebtorRepayments"
                && tableName != "TankDailyStocks"
                && tableName != "DebtorVehicles"
                && tableName != "PumpExpenseCategoryItems"
                && tableName != "DsmSalaryAdjustments"
                && tableName != "AuditLogs") continue;
            if (excludePk && pkProperties != null && pkProperties.Contains(property)) continue;
            if (isTestEnv && tableName == "PaymentCollections" && testExcludeCols.Contains(property.Name)) continue;
            if (tableName == "DsmPumpAssignments" && property.Name == "CompletedDate") continue;
            if (tableName == "DsmPersonalDebtorRepayments" && (property.Name == "CardBatch" || property.Name == "CardTid")) continue;

            var propVal = entry.Property(property.Name).CurrentValue;
            if (propVal is DateTime dtVal)
            {
                values[property.Name] = SyncTimestampHelper.FormatForPush(entity.GetType(), property.Name, dtVal);
            }
            else
            {
                values[property.Name] = propVal;
            }
        }
        return values;
    }

    private void UpdateStatus(DateTime lastSync, int pending, bool connected, string message)
    {
        CurrentStatus.LastSyncTime = lastSync;
        CurrentStatus.PendingRecords = pending;
        CurrentStatus.IsConnected = connected;
        CurrentStatus.StatusMessage = message;

        _logger.Information("Sync Status: {StatusMessage} | Pending: {PendingRecords} | Last Sync: {LastSyncTime}", message, pending, lastSync);
        SyncStatusChanged?.Invoke(CurrentStatus);
    }

    private static int GetPushOrderIndex(string tableName)
    {
        if (tableName == "Users") return 0;
        
        for (int i = 0; i < PullTableOrder.Length; i++)
        {
            if (PullTableOrder[i].TableName == tableName)
                return i + 1;
        }
        return int.MaxValue;
    }

    private static bool TryGetDictValue(Dictionary<string, object?> dict, string propName, out object? value)
    {
        if (dict.TryGetValue(propName, out value) && value != null) return true;

        // Try underscore / snake_case version e.g. PhonePeMorning -> phone_pe_morning, DsmEntryId -> dsm_entry_id
        var snakeCase = string.Concat(propName.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + x.ToString() : x.ToString())).ToLower();
        if (dict.TryGetValue(snakeCase, out value) && value != null) return true;

        // Try without underscores e.g. dynamic_items_json -> dynamicitemsjson
        var noUnderscore = propName.Replace("_", "");
        if (dict.TryGetValue(noUnderscore, out value) && value != null) return true;

        if (dict.ContainsKey(propName)) { value = dict[propName]; return true; }
        if (dict.ContainsKey(snakeCase)) { value = dict[snakeCase]; return true; }
        if (dict.ContainsKey(noUnderscore)) { value = dict[noUnderscore]; return true; }

        value = null;
        return false;
    }

    private static Dictionary<string, object?>? SelectDeterministicRemoteSettingsRow(
        List<Dictionary<string, object?>>? rows,
        string? preferredLocalSyncGuid,
        IReadOnlyDictionary<string, SyncIdMapping> mappingByRemoteGuid,
        out int candidateCount)
    {
        candidateCount = 0;
        if (rows == null || rows.Count == 0)
        {
            return null;
        }

        var validRows = new List<(Dictionary<string, object?> Row, string SyncGuid, DateTime UpdatedAt)>();
        foreach (var rawRow in rows)
        {
            if (rawRow == null) continue;
            var dict = new Dictionary<string, object?>(rawRow, StringComparer.OrdinalIgnoreCase);
            if (!TryGetDictValue(dict, "SyncGuid", out var sgVal) || sgVal == null) continue;
            var sg = sgVal.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(sg)) continue;

            var updatedAt = DateTime.MinValue;
            if (TryGetDictValue(dict, "updated_at", out var utVal) && utVal != null &&
                DateTime.TryParse(utVal.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsedUt))
            {
                updatedAt = parsedUt.ToUniversalTime();
            }
            validRows.Add((dict, sg, updatedAt));
        }

        candidateCount = validRows.Count;
        if (validRows.Count == 0)
        {
            // Fallback to first row if no SyncGuid column present
            return new Dictionary<string, object?>(rows[0], StringComparer.OrdinalIgnoreCase);
        }

        // Order deterministically: newest updated_at first, then lexical SyncGuid tie-breaker
        var ordered = validRows
            .OrderByDescending(r => r.UpdatedAt)
            .ThenBy(r => r.SyncGuid, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 1. Prefer remote row matching the current local Settings record's existing SyncIdMapping
        if (!string.IsNullOrWhiteSpace(preferredLocalSyncGuid))
        {
            var preferredMatch = ordered.FirstOrDefault(r =>
                string.Equals(r.SyncGuid, preferredLocalSyncGuid.Trim(), StringComparison.OrdinalIgnoreCase));
            if (preferredMatch.Row != null)
            {
                return preferredMatch.Row;
            }
        }

        // 2. Otherwise prefer the newest remote row that already owns a SyncIdMapping in SQLite
        var mappedMatch = ordered.FirstOrDefault(r => mappingByRemoteGuid.ContainsKey(r.SyncGuid));
        if (mappedMatch.Row != null)
        {
            return mappedMatch.Row;
        }

        // 3. Otherwise return the newest remote row deterministically
        return ordered[0].Row;
    }

    private static bool DidPatchUpdateRows(string? patchBody)
    {
        if (string.IsNullOrWhiteSpace(patchBody))
        {
            return false;
        }

        var trimmed = patchBody.Trim();
        if (trimmed == "[]")
        {
            return false;
        }

        try
        {
            if (trimmed.StartsWith("["))
            {
                var arr = Newtonsoft.Json.Linq.JArray.Parse(trimmed);
                return arr.Count > 0;
            }
            if (trimmed.StartsWith("{"))
            {
                var obj = Newtonsoft.Json.Linq.JObject.Parse(trimmed);
                return obj.Count > 0;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}
