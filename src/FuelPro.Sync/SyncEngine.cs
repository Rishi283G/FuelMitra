using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using Serilog;

namespace FuelPro.Sync;

public class SyncEngine
{
    private readonly IServiceProvider _serviceProvider;
    private readonly SyncConfigService _configService;
    private readonly SupabaseHttpClient _httpClient;
    private readonly ILogger _logger = Log.ForContext<SyncEngine>();
    
    private Timer? _syncTimer;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
    private bool _isSyncing;
    private DateTime _lastPullTime = DateTime.MinValue;

    public event Action<SyncStatusInfo>? SyncStatusChanged;
    
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
        "DsmSalaryPayments",
        // Dynamic Configurations (TankDefinitions, CollectionTypes, AppFeatureSettings, PaymentCollectionItems synced via Settings & PaymentCollections)
        "DsmQrPayments"
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
        new TableSyncConfig("OilDefDailyLogs", new[] { new FkMapping("ProductId", "ProductMasters") }),
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
        new TableSyncConfig("DsmQrPayments", new[] {
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
    {
        _serviceProvider = serviceProvider;
        _configService = serviceProvider.GetRequiredService<SyncConfigService>();
        _httpClient = new SupabaseHttpClient();
    }

    public void Start()
    {
        _logger.Information("Starting background sync engine...");
        _syncTimer = new Timer(async _ => await OnTimerTickAsync(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
    }

    public void Stop()
    {
        _logger.Information("Stopping background sync engine...");
        _syncTimer?.Dispose();
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
        await RunSyncCycleAsync(forcePull: true);
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
            // Reset IsSynced = 0 for any existing logs with this stationId or unassigned so they are pushed immediately
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE SyncChangeLogs SET IsSynced = 0, StationId = {0} WHERE StationId = {0} OR StationId IS NULL OR StationId = ''",
                stationId);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to reset IsSynced flags on SyncChangeLogs");
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
        try
        {
            await RunSyncCycleAsync(forcePull: false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Background timer tick sync failed.");
        }
    }

    private async Task RunSyncCycleAsync(bool forcePull = false)
    {
        if (_isSyncing) return;
        
        if (!await _syncLock.WaitAsync(0)) return;

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

            var authService = _serviceProvider.GetRequiredService<AuthService>();
            
            bool isOwner = authService.IsOwner;
            bool shouldPull = forcePull || isOwner || (DateTime.Now - _lastPullTime).TotalMinutes >= 1;

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
            var lastSyncStr = settings.LastSyncTime == DateTime.MinValue ? "Never" : settings.LastSyncTime.ToString("dd MMM yyyy hh:mm tt");
            CurrentStatus.StatusMessage = $"Offline\nPending Changes: {pendingCount}\nLast Successful Sync:\n{lastSyncStr}\nRetrying Automatically...";
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

    private async Task PerformPushAsync(SyncSettings settings)
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        
        // Fetch logs that are pending
        var allPendingLogs = await context.SyncChangeLogs
            .Where(l => !l.IsSynced)
            .OrderBy(l => l.Id)
            .ToListAsync();

        // Mark changes for non-synced tables as synced immediately to prevent errors and accumulation
        var ignoredLogs = allPendingLogs.Where(l => !SyncedTables.Contains(l.TableName)).ToList();
        if (ignoredLogs.Count > 0)
        {
            var ignoredLogIds = ignoredLogs.Select(l => l.Id).ToList();
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
            
            // 1. Process Deletes first — target by SyncGuid
            var deletes = group.Where(l => l.Operation == "DELETE").ToList();
            foreach (var del in deletes)
            {
                var syncGuid = GetExistingSyncGuid(localToGuid, tableName, del.RecordId);
                if (string.IsNullOrEmpty(syncGuid))
                {
                    syncGuid = del.RecordGuid;
                }

                if (!string.IsNullOrEmpty(syncGuid))
                {
                    var response = await _httpClient.SendRequestAsync(HttpMethod.Delete, $"{tableName}?SyncGuid=eq.{syncGuid}");
                    if (!response.IsSuccessStatusCode)
                    {
                        var error = await response.Content.ReadAsStringAsync();
                        _logger.Warning("Failed to delete record SyncGuid={Guid} in Supabase table {Table}: {Error}", syncGuid, tableName, error);
                    }
                    // Remove the local mapping
                    var mappingToRemove = await context.SyncIdMappings
                        .FirstOrDefaultAsync(m => m.TableName == tableName && (m.LocalId == del.RecordId || m.RemoteGuid == syncGuid));
                    if (mappingToRemove != null)
                    {
                        context.SyncIdMappings.Remove(mappingToRemove);
                    }

                    // ALSO push this DELETE log entry to Supabase SyncChangeLogs table!
                    var logDict = new Dictionary<string, object?>
                    {
                        { "SyncGuid", del.SyncGuid },
                        { "TableName", del.TableName },
                        { "RecordGuid", syncGuid },
                        { "Operation", "DELETE" },
                        { "station_id", settings.StationId },
                        { "machine_id", settings.MachineId },
                        { "Timestamp", del.CreatedAt.ToUniversalTime().ToString("o") }
                    };
                    var logJson = JsonConvert.SerializeObject(new[] { logDict });
                    var logResponse = await _httpClient.SendRequestAsync(HttpMethod.Post, "SyncChangeLogs", logJson, isUpsert: false);
                    if (!logResponse.IsSuccessStatusCode)
                    {
                        var logError = await logResponse.Content.ReadAsStringAsync();
                        _logger.Error("Failed to push DELETE log entry to Supabase SyncChangeLogs: {Error}", logError);
                    }
                }
                else
                {
                    _logger.Warning("Push DELETE: No SyncGuid or RecordGuid mapping found for {Table} LocalId={Id}. Skipping cloud delete.", tableName, del.RecordId);
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
                            dict["local_id"] = op.RecordId;
                            dict["machine_id"] = settings.MachineId;

                            if (record is PaymentCollection pcPush)
                            {
                                var dynItems = await context.PaymentCollectionItems.Where(i => i.PaymentId == pcPush.PaymentId).ToListAsync();
                                if (dynItems.Count > 0)
                                {
                                    dict["DynamicItemsJson"] = JsonConvert.SerializeObject(dynItems.Select(i => new
                                    {
                                        i.CollectionTypeCode,
                                        i.Amount,
                                        i.Tid,
                                        i.Batch,
                                        i.Slot
                                    }));
                                }
                            }
                            else if (record is Setting)
                            {
                                var activeTanks = await context.TankDefinitions.ToListAsync();
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
                                _logger.Information("Record already exists in Supabase for table {Table}; proceeding.", tableName);
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
                                hasPushError = true;
                                break;
                            }
                            else
                            {
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

            // Mark these log entries as synced
            var logIds = group.Select(l => l.Id).ToList();
            var idsCsv = string.Join(",", logIds);
            await context.Database.ExecuteSqlRawAsync(
                $"UPDATE SyncChangeLogs SET IsSynced = 1, SyncedAt = '{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}' WHERE Id IN ({idsCsv})");
        }

        // Save any new SyncIdMappings that were created during push
        await context.SaveChangesAsync();

        // Update last sync time
        settings.LastSyncTime = DateTime.Now;
        await _configService.SaveSettingsAsync(settings);

        UpdateStatus(settings.LastSyncTime, 0, true, "Synced");
    }

    // ═══════════════════════════════════════════════════════════════════
    // PULL SYNC — Supabase → Local changes (GUID-based)
    // ═══════════════════════════════════════════════════════════════════

    private async Task PerformPullAsync(SyncSettings settings, bool forcePull = false)
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();

        UpdateStatus(settings.LastSyncTime, 0, true, "Pulling updates...");

        // Load ALL existing ID mappings into memory for fast lookup
        // Map: (TableName, RemoteGuid) → LocalId
        var allMappings = await context.SyncIdMappings.AsNoTracking().ToListAsync();
        var guidToLocal = new Dictionary<string, Dictionary<string, int>>();
        foreach (var m in allMappings)
        {
            if (!guidToLocal.ContainsKey(m.TableName))
                guidToLocal[m.TableName] = new Dictionary<string, int>();
            guidToLocal[m.TableName][m.RemoteGuid] = m.LocalId;
        }

        foreach (var tableDef in PullTableOrder)
        {
            if (tableDef.TableName == "SyncChangeLogs")
            {
                // Special handling for pulling delete propagation logs
                var logQueryTime = (forcePull ? DateTime.MinValue : settings.LastSyncTime.AddMinutes(-5)).ToUniversalTime().ToString("o");
                var logResponse = await _httpClient.SendRequestAsync(HttpMethod.Get,
                    $"SyncChangeLogs?station_id=eq.{settings.StationId}&Timestamp=gt.{logQueryTime}");
                if (logResponse.IsSuccessStatusCode)
                {
                    var logJson = await logResponse.Content.ReadAsStringAsync();
                    var logs = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(logJson);
                    if (logs != null && logs.Count > 0)
                    {
                        FuelProDbContext.BypassTracking = true;
                        try
                        {
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
                                        _logger.Information("Pull DELETE: Deleted local record from {Table} with LocalId={Id} (SyncGuid={Guid})", deletedTableName, localId, deletedRecordGuid);
                                    }
                                    context.SyncIdMappings.Remove(mapping);
                                }
                            }
                            await context.SaveChangesAsync();
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Error processing pull deletes from SyncChangeLogs");
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
            var tableQueryTime = (forcePull || !tableHasMappings)
                ? DateTime.MinValue.ToUniversalTime().ToString("o") 
                : settings.LastSyncTime.AddMinutes(-5).ToUniversalTime().ToString("o");

            // Fetch records updated since tableQueryTime for this StationId (strictly isolated per station)
            var stationFilter = tableDef.TableName == "DsmQrPayments"
                ? (!string.IsNullOrWhiteSpace(settings.StationId) ? $"StationId=eq.{settings.StationId.Trim()}" : "StationId=is.null")
                : (!string.IsNullOrWhiteSpace(settings.StationId) ? $"station_id=eq.{settings.StationId.Trim()}" : "station_id=is.null");

            var response = await _httpClient.SendRequestAsync(HttpMethod.Get,
                $"{tableDef.TableName}?{stationFilter}&updated_at=gt.{tableQueryTime}");

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                if (error.Contains("42703"))
                {
                    // If updated_at or StationId column is missing, fallback without updated_at filter
                    response = await _httpClient.SendRequestAsync(HttpMethod.Get, $"{tableDef.TableName}?{stationFilter}");
                    if (!response.IsSuccessStatusCode)
                    {
                        var altStationFilter = stationFilter.StartsWith("StationId")
                            ? (!string.IsNullOrWhiteSpace(settings.StationId) ? $"station_id=eq.{settings.StationId.Trim()}" : "station_id=is.null")
                            : (!string.IsNullOrWhiteSpace(settings.StationId) ? $"StationId=eq.{settings.StationId.Trim()}" : "StationId=is.null");
                        response = await _httpClient.SendRequestAsync(HttpMethod.Get, $"{tableDef.TableName}?{altStationFilter}");
                    }
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                if (error.Contains("PGRST205") || error.Contains("Could not find the table"))
                {
                    _logger.Information("Table {Table} not present in Supabase schema (PGRST205); skipping pull.", tableDef.TableName);
                }
                else
                {
                    _logger.Warning("Supabase GET failed for table {Table} with filter {Filter}: {Error}", tableDef.TableName, stationFilter, error);
                }
                continue;
            }

            var json = await response.Content.ReadAsStringAsync();
            var rawRecords = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(json);
            if (rawRecords == null || rawRecords.Count == 0) continue;

            var records = rawRecords.Select(r => new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase)).ToList();

            if (!guidToLocal.ContainsKey(tableDef.TableName))
                guidToLocal[tableDef.TableName] = new Dictionary<string, int>();
            var tableMap = guidToLocal[tableDef.TableName];

            var newEntities = new List<(object Entity, string RemoteGuid)>();

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
                            // If it doesn't exist locally, we proceed to import it (e.g. fresh database install or tests)
                        }
                    }

                    // ── Step 1: Remap FK values from parent GUIDs to local IDs ──
                    bool fkFailed = false;
                    foreach (var fk in tableDef.ForeignKeys)
                    {
                        if (!TryGetDictValue(dict, fk.FkProperty, out var fkVal) || fkVal == null) continue;

                        var parentGuid = fkVal.ToString()!;
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
                    if (fkFailed) continue;

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
                                DateTime sDate = Convert.ToDateTime(sDateObj).Date;
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
                        else if (tableDef.TableName == "DsmQrPayments")
                        {
                            dict.TryGetValue("DsmEntryId", out var dsmIdObj);
                            dict.TryGetValue("Tid", out var tidObj);
                            dict.TryGetValue("Batch", out var batchObj);
                            dict.TryGetValue("Amount", out var amtObj);
                            int? localDsmId = dsmIdObj != null ? Convert.ToInt32(dsmIdObj) : null;
                            string tid = tidObj?.ToString()?.Trim() ?? "";
                            string batch = batchObj?.ToString()?.Trim() ?? "";
                            double amt = amtObj != null ? Convert.ToDouble(amtObj) : 0;

                            if (localDsmId.HasValue)
                            {
                                matchedLocalEntity = context.Set<DsmQrPaymentEntry>().Local
                                    .FirstOrDefault(q => q.DsmEntryId == localDsmId &&
                                                         (!string.IsNullOrEmpty(tid) ? (string.Equals(q.Tid, tid, StringComparison.OrdinalIgnoreCase) && string.Equals(q.Batch, batch, StringComparison.OrdinalIgnoreCase)) : Math.Abs(q.Amount - amt) < 0.01));
                                if (matchedLocalEntity == null)
                                {
                                    matchedLocalEntity = await context.Set<DsmQrPaymentEntry>()
                                        .FirstOrDefaultAsync(q => q.DsmEntryId == localDsmId &&
                                                                  (!string.IsNullOrEmpty(tid) ? (q.Tid != null && q.Tid.ToLower() == tid.ToLower()) && (q.Batch != null && q.Batch.ToLower() == batch.ToLower()) : Math.Abs(q.Amount - amt) < 0.01));
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
                            DateTime? purchaseDate = pDateObj != null ? Convert.ToDateTime(pDateObj) : null;
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
                            DateTime? rDate = rDateObj != null ? Convert.ToDateTime(rDateObj) : null;
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
                            DateTime? date = dateObj != null ? Convert.ToDateTime(dateObj) : null;
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
                            dict.TryGetValue("LogDate", out var logDateObj);
                            dict.TryGetValue("AdjustmentQuantity", out var adjQtyObj);
                            dict.TryGetValue("AdjustmentType", out var adjTypeObj);
                            dict.TryGetValue("SoldQuantity", out var soldQtyObj);
                            dict.TryGetValue("AddedQuantity", out var addedQtyObj);
                            dict.TryGetValue("Remarks", out var remObj);
                            int? productId = prodIdObj != null ? Convert.ToInt32(prodIdObj) : null;
                            DateTime? logDate = logDateObj != null ? Convert.ToDateTime(logDateObj) : null;
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
                                    matchedLocalEntity = context.Set<OilDefDailyLog>().Local
                                        .FirstOrDefault(l => l.ProductId == productId.Value && l.LogDate.Date == ld.Date
                                            && Math.Abs(l.AdjustmentQuantity - adjQty) < 0.01
                                            && string.Equals(l.AdjustmentType ?? "", adjType, StringComparison.OrdinalIgnoreCase)
                                            && (Math.Abs((l.LogDate - ld).TotalMinutes) < 2 || string.Equals(l.Remarks ?? "", remarks, StringComparison.OrdinalIgnoreCase)));

                                    if (matchedLocalEntity == null)
                                    {
                                        matchedLocalEntity = await context.Set<OilDefDailyLog>()
                                            .FirstOrDefaultAsync(l => l.ProductId == productId.Value && l.LogDate.Date == ld.Date
                                                && Math.Abs(l.AdjustmentQuantity - adjQty) < 0.01
                                                && ((l.AdjustmentType == null && adjType == "") || (l.AdjustmentType != null && l.AdjustmentType.ToLower() == adjType.ToLower())));
                                    }
                                }
                                else if (soldQty > 0.001)
                                {
                                    // Sales log: match by ProductId, date, SoldQuantity
                                    matchedLocalEntity = context.Set<OilDefDailyLog>().Local
                                        .FirstOrDefault(l => l.ProductId == productId.Value && l.LogDate.Date == ld.Date
                                            && Math.Abs(l.SoldQuantity - soldQty) < 0.01 && Math.Abs(l.AdjustmentQuantity) < 0.001);

                                    if (matchedLocalEntity == null)
                                    {
                                        matchedLocalEntity = await context.Set<OilDefDailyLog>()
                                            .FirstOrDefaultAsync(l => l.ProductId == productId.Value && l.LogDate.Date == ld.Date
                                                && Math.Abs(l.SoldQuantity - soldQty) < 0.01 && Math.Abs(l.AdjustmentQuantity) < 0.001);
                                    }
                                }
                                else if (addedQty > 0.001)
                                {
                                    // Purchase log: match by ProductId, date, AddedQuantity
                                    matchedLocalEntity = context.Set<OilDefDailyLog>().Local
                                        .FirstOrDefault(l => l.ProductId == productId.Value && l.LogDate.Date == ld.Date
                                            && Math.Abs(l.AddedQuantity - addedQty) < 0.01 && Math.Abs(l.AdjustmentQuantity) < 0.001);

                                    if (matchedLocalEntity == null)
                                    {
                                        matchedLocalEntity = await context.Set<OilDefDailyLog>()
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

                        if (matchedLocalEntity != null)
                        {
                            entity = matchedLocalEntity;
                            var pkValue = (int)context.Entry(matchedLocalEntity).Property(pkProp.Name).CurrentValue!;
                            
                            // Map the remote SyncGuid to this existing local ID
                            tableMap[remoteGuid] = pkValue;
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
                                    entry.Property(prop.Name).CurrentValue = null;
                                }
                            }
                            else
                            {
                                var targetType = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
                                object? converted;
                                if (targetType == typeof(DateTime))
                                    converted = DateTime.Parse(val.ToString()!);
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
                                entry.Property(prop.Name).CurrentValue = converted;
                            }
                        }
                    }

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
                        context.SyncIdMappings.Add(new SyncIdMapping
                        {
                            TableName = tableDef.TableName,
                            RemoteGuid = remoteGuid,
                            LocalId = newLocalId
                        });
                    }
                    await context.SaveChangesAsync();
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
                            var pcList = await context.PaymentCollections.Where(p => !string.IsNullOrEmpty(p.DynamicItemsJson)).ToListAsync();
                            foreach (var pc in pcList)
                            {
                                if (!string.IsNullOrWhiteSpace(pc.DynamicItemsJson))
                                {
                                    var items = JsonConvert.DeserializeObject<List<PaymentCollectionItem>>(pc.DynamicItemsJson);
                                    if (items != null && items.Count > 0)
                                    {
                                        var existing = await context.PaymentCollectionItems.Where(i => i.PaymentId == pc.PaymentId).ToListAsync();
                                        context.PaymentCollectionItems.RemoveRange(existing);
                                        foreach (var itm in items)
                                        {
                                            context.PaymentCollectionItems.Add(new PaymentCollectionItem
                                            {
                                                PaymentId = pc.PaymentId,
                                                CollectionTypeCode = itm.CollectionTypeCode ?? "OTHERS",
                                                Amount = itm.Amount,
                                                Tid = itm.Tid,
                                                Batch = itm.Batch,
                                                Slot = itm.Slot ?? "General"
                                            });
                                        }
                                    }
                                }
                            }
                            await context.SaveChangesAsync();
                            FuelPro.Core.Services.DsmEntryService.RaiseDsmEntryChanged();
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

                                        // Remove obsolete / phantom seed tanks not present in station's tank definitions
                                        foreach (var existing in existingTanks)
                                        {
                                            if (!processedNames.Contains(existing.TankName) &&
                                                !tanks.Any(t => string.Equals(t.TankName, existing.TankName, StringComparison.OrdinalIgnoreCase)))
                                            {
                                                context.TankDefinitions.Remove(existing);
                                            }
                                        }

                                        configChanged = true;
                                    }
                                }

                                // 2. Collection Types
                                if (!string.IsNullOrWhiteSpace(currentSetting.CollectionTypesJson))
                                {
                                    var types = JsonConvert.DeserializeObject<List<CollectionTypeMaster>>(currentSetting.CollectionTypesJson);
                                    if (types != null && types.Count > 0)
                                    {
                                        var existingCts = await context.CollectionTypes.ToListAsync();
                                        var processedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                                        foreach (var ct in types)
                                        {
                                            var existingCt = existingCts.FirstOrDefault(x => string.Equals(x.Code, ct.Code, StringComparison.OrdinalIgnoreCase));
                                            if (existingCt != null)
                                            {
                                                existingCt.DisplayName = ct.DisplayName;
                                                existingCt.Category = ct.Category;
                                                existingCt.HasTidBatch = ct.HasTidBatch;
                                                existingCt.DisplayOrder = ct.DisplayOrder;
                                                existingCt.IsActive = ct.IsActive;
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
                                                    DisplayOrder = ct.DisplayOrder,
                                                    IsActive = ct.IsActive,
                                                    IsSystem = ct.IsSystem,
                                                    CreatedAt = DateTime.Now
                                                });
                                                processedCodes.Add(ct.Code);
                                            }
                                        }

                                        // Remove obsolete non-system collection types not present in station config
                                        foreach (var existing in existingCts)
                                        {
                                            if (!existing.IsSystem && !processedCodes.Contains(existing.Code) &&
                                                !types.Any(t => string.Equals(t.Code, existing.Code, StringComparison.OrdinalIgnoreCase)))
                                            {
                                                context.CollectionTypes.Remove(existing);
                                            }
                                        }

                                        configChanged = true;
                                    }
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
                                        configChanged = true;
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

                                        // Crucial: remove obsolete seeded mappings not present in station's configured pump mappings
                                        foreach (var existing in existingMappings)
                                        {
                                            if (!processedIds.Contains(existing.PumpMappingId))
                                            {
                                                context.PumpMappings.Remove(existing);
                                            }
                                        }

                                        configChanged = true;
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
                                    FuelPro.Core.Services.DsmEntryService.RaiseDsmEntryChanged();
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
                            FuelPro.Core.Services.DsmEntryService.RaiseDsmEntryChanged();
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
                            FuelPro.Core.Services.DsmEntryService.RaiseDsmEntryChanged();
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
                }
            }
            finally
            {
                FuelProDbContext.BypassTracking = false;
            }
        }

        // Update last sync time
        settings.LastSyncTime = DateTime.Now;
        await _configService.SaveSettingsAsync(settings);

        UpdateStatus(settings.LastSyncTime, 0, true, "Synced");
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

        try
        {
            _httpClient.Configure(settings.SupabaseUrl, settings.SupabaseApiKey);

            using var scope = _serviceProvider.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

            var localSetting = await context.Settings.FirstOrDefaultAsync();
            if (localSetting == null)
            {
                localSetting = new Setting();
                context.Settings.Add(localSetting);
            }

            // 1. Storage Tanks
            var allTanks = await context.TankDefinitions.ToListAsync();
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

            // Find existing SyncGuid for this station in remote or local
            string? targetSyncGuid = null;
            try
            {
                var checkResp = await _httpClient.SendRequestAsync(HttpMethod.Get, $"Settings?station_id=eq.{stationId}&limit=1");
                if (!checkResp.IsSuccessStatusCode)
                {
                    checkResp = await _httpClient.SendRequestAsync(HttpMethod.Get, $"Settings?StationId=eq.{stationId}&limit=1");
                }
                if (checkResp.IsSuccessStatusCode)
                {
                    var checkJson = await checkResp.Content.ReadAsStringAsync();
                    var checkList = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(checkJson);
                    if (checkList != null && checkList.Count > 0)
                    {
                        if (TryGetDictValue(checkList[0], "SyncGuid", out var sgVal) && sgVal != null)
                        {
                            targetSyncGuid = sgVal.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not pre-fetch existing Settings row from Supabase");
            }

            if (string.IsNullOrEmpty(targetSyncGuid))
            {
                var existingMapping = await context.SyncIdMappings.FirstOrDefaultAsync(m => m.TableName == "Settings" && m.LocalId == localSetting.SettingId);
                if (existingMapping != null && !string.IsNullOrEmpty(existingMapping.RemoteGuid))
                {
                    targetSyncGuid = existingMapping.RemoteGuid;
                }
                else
                {
                    targetSyncGuid = Guid.NewGuid().ToString();
                    context.SyncIdMappings.Add(new SyncIdMapping
                    {
                        TableName = "Settings",
                        LocalId = localSetting.SettingId,
                        RemoteGuid = targetSyncGuid
                    });
                    await context.SaveChangesAsync();
                }
            }

            // Prepare Payload for Supabase Settings table
            var payload = new Dictionary<string, object?>
            {
                ["SyncGuid"] = targetSyncGuid,
                ["station_id"] = stationId,
                ["machine_id"] = settings.MachineId,
                ["StationName"] = localSetting.PumpStationName ?? "Fuel Station",
                ["PumpMappingsJson"] = localSetting.PumpMappingsJson,
                ["PumpConnectionRulesJson"] = localSetting.PumpConnectionRulesJson,
                ["TankDefinitionsJson"] = localSetting.TankDefinitionsJson,
                ["CollectionTypesJson"] = localSetting.CollectionTypesJson,
                ["AppFeatureSettingsJson"] = localSetting.AppFeatureSettingsJson,
                ["FuelRatesJson"] = localSetting.FuelRatesJson,
                ["updated_at"] = DateTime.UtcNow.ToString("o")
            };

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

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("PushStationSnapshotToCloudAsync retrying via direct PATCH Settings?station_id=eq.{StationId}...", stationId);
                response = await _httpClient.SendRequestAsync(HttpMethod.Patch, $"Settings?station_id=eq.{stationId}", json);
                if (!response.IsSuccessStatusCode)
                {
                    response = await _httpClient.SendRequestAsync(HttpMethod.Patch, $"Settings?StationId=eq.{stationId}", json);
                }
            }

            if (!response.IsSuccessStatusCode)
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
            var response = await _httpClient.SendRequestAsync(HttpMethod.Get, $"Settings?station_id=eq.{stationId}&order=updated_at.desc&limit=1");
            if (!response.IsSuccessStatusCode)
            {
                response = await _httpClient.SendRequestAsync(HttpMethod.Get, $"Settings?StationId=eq.{stationId}&order=updated_at.desc&limit=1");
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

            var localSetting = await context.Settings.FirstOrDefaultAsync();
            if (localSetting == null)
            {
                localSetting = new Setting();
                context.Settings.Add(localSetting);
            }

            if (TryGetDictValue(dict, "StationName", out var snVal) && snVal != null)
                localSetting.PumpStationName = snVal.ToString()!;

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

            // 2. Collection Types
            if (TryGetDictValue(dict, "CollectionTypesJson", out var ctJsonObj) && ctJsonObj != null)
            {
                var ctJson = ctJsonObj.ToString();
                if (!string.IsNullOrWhiteSpace(ctJson))
                {
                    localSetting.CollectionTypesJson = ctJson;
                    var types = JsonConvert.DeserializeObject<List<CollectionTypeMaster>>(ctJson);
                    if (types != null && types.Count > 0)
                    {
                        var existingCts = await context.CollectionTypes.ToListAsync();
                        var processedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var ct in types)
                        {
                            var existingCt = existingCts.FirstOrDefault(x => string.Equals(x.Code, ct.Code, StringComparison.OrdinalIgnoreCase));
                            if (existingCt != null)
                            {
                                existingCt.DisplayName = ct.DisplayName;
                                existingCt.Category = ct.Category;
                                existingCt.HasTidBatch = ct.HasTidBatch;
                                existingCt.DisplayOrder = ct.DisplayOrder;
                                existingCt.IsActive = ct.IsActive;
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
                                    DisplayOrder = ct.DisplayOrder,
                                    IsActive = ct.IsActive,
                                    IsSystem = ct.IsSystem,
                                    CreatedAt = DateTime.Now
                                });
                                processedCodes.Add(ct.Code);
                            }
                        }

                        foreach (var existing in existingCts)
                        {
                            if (!existing.IsSystem && !processedCodes.Contains(existing.Code))
                            {
                                context.CollectionTypes.Remove(existing);
                            }
                        }
                    }
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
            var response = await _httpClient.SendRequestAsync(HttpMethod.Get, $"{parentTableName}?SyncGuid=eq.{parentGuid}");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var records = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
            if (records == null || records.Count == 0) return null;

            var pDict = records[0];

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

                if (pDict.TryGetValue(prop.Name, out var val))
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
                            converted = DateTime.Parse(val.ToString()!);
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
    private async Task<string> GetOrCreateSyncGuidAsync(
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

        // Generate new GUID
        var newGuid = Guid.NewGuid().ToString();
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
            
            values[property.Name] = entry.Property(property.Name).CurrentValue;
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
}
