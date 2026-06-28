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
        "PumpMappings",
        "AuditLogs",
        "DayLocks",
        "PumpExpenses",
        "ExpenseCategories",
        "PumpExpenseCategoryItems",
        "DsmSalaryAdjustments",
        "DsmPersonalDebtors",
        "DsmPersonalDebtorRepayments"
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
        new TableSyncConfig("DsmProfiles", Array.Empty<FkMapping>()),
        new TableSyncConfig("DsmUsers", Array.Empty<FkMapping>()),
        new TableSyncConfig("DsmDevices", new[] { new FkMapping("DsmUserId", "DsmUsers") }),
        new TableSyncConfig("DsmPumpAssignments", new[] { new FkMapping("DsmUserId", "DsmUsers") }),
        new TableSyncConfig("DsmApprovalAudits", Array.Empty<FkMapping>()),
        new TableSyncConfig("DsmAttendance", new[] { new FkMapping("DsmUserId", "DsmUsers") }),
        new TableSyncConfig("Creditors", Array.Empty<FkMapping>()),
        new TableSyncConfig("DebtorVehicles", new[] { new FkMapping("CreditorId", "Creditors") }),
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
        new TableSyncConfig("PumpMappings", Array.Empty<FkMapping>()),
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
        })
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
        // Run every 30 seconds, starting after 10 seconds
        _syncTimer = new Timer(async _ => await OnTimerTickAsync(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
    }

    public void Stop()
    {
        _logger.Information("Stopping background sync engine...");
        _syncTimer?.Dispose();
    }

    public async Task ForceSyncAsync()
    {
        _logger.Information("Force sync requested.");
        await RunSyncCycleAsync(forcePull: true);
    }

    private async Task OnTimerTickAsync()
    {
        await RunSyncCycleAsync(forcePull: false);
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
            
            bool shouldPull = forcePull || (DateTime.Now - _lastPullTime).TotalMinutes >= 10;

            // Check roles and route accordingly
            if (authService.IsOwner)
            {
                // Owner is read-only for transaction data at this terminal and only pulls updates
                if (shouldPull)
                {
                    await PerformPullAsync(settings);
                    _lastPullTime = DateTime.Now;
                }
            }
            else
            {
                // Manager/Operators push local modifications first, then pull updates
                await PerformPushAsync(settings);
                
                if (shouldPull)
                {
                    await PerformPullAsync(settings);
                    _lastPullTime = DateTime.Now;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Sync cycle failed with exception");
            CurrentStatus.IsConnected = false;
            CurrentStatus.StatusMessage = $"Error: {ex.Message}";
            SyncStatusChanged?.Invoke(CurrentStatus);
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
            FuelProDbContext.BypassTracking = true;
            try
            {
                foreach (var log in ignoredLogs)
                {
                    log.IsSynced = true;
                    log.SyncedAt = DateTime.Now;
                    context.Entry(log).State = EntityState.Modified;
                }
                await context.SaveChangesAsync();
            }
            finally
            {
                FuelProDbContext.BypassTracking = false;
            }
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
                        { "CreatedAt", del.CreatedAt.ToUniversalTime().ToString("o") }
                    };
                    var logJson = JsonConvert.SerializeObject(new[] { logDict });
                    var logResponse = await _httpClient.SendRequestAsync(HttpMethod.Post, "SyncChangeLogs", logJson, isUpsert: true, onConflict: "SyncGuid");
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

                            // ── Add station metadata ──
                            dict["station_id"] = settings.StationId;
                            dict["local_id"] = op.RecordId;
                            dict["machine_id"] = settings.MachineId;

                            recordsToUpsert.Add(dict);
                        }
                    }

                    // ── Force-push any missing parent records before the child batch ──
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
                }

                if (recordsToUpsert.Count > 0)
                {
                    // Split records to process deactivations (IsActive = false) first, avoiding unique constraint violations on active records.
                    var deactivations = recordsToUpsert.Where(r => r.TryGetValue("IsActive", out var val) && val is bool b && !b).ToList();
                    var activations = recordsToUpsert.Where(r => !(r.TryGetValue("IsActive", out var val) && val is bool b && !b)).ToList();

                    var batches = new List<List<Dictionary<string, object?>>>();
                    if (deactivations.Count > 0) batches.Add(deactivations);
                    if (activations.Count > 0) batches.Add(activations);

                    foreach (var batch in batches)
                    {
                        var json = JsonConvert.SerializeObject(batch);
                        // Upsert with SyncGuid as the conflict resolution column
                        var response = await _httpClient.SendRequestAsync(HttpMethod.Post, tableName, json, isUpsert: true, onConflict: "SyncGuid");
                        if (!response.IsSuccessStatusCode)
                        {
                            var error = await response.Content.ReadAsStringAsync();
                            throw new HttpRequestException($"Supabase UPSERT failed for table {tableName}: {error}");
                        }
                    }

                    // Mark all upserted GUIDs as pushed
                    foreach (var rec in recordsToUpsert)
                    {
                        if (rec.TryGetValue("SyncGuid", out var sg) && sg != null)
                            pushedGuids.Add(sg.ToString()!);
                    }
                }
            }

            // Mark these log entries as synced
            FuelProDbContext.BypassTracking = true;
            try
            {
                foreach (var log in group)
                {
                    log.IsSynced = true;
                    log.SyncedAt = DateTime.Now;
                    context.Entry(log).State = EntityState.Modified;
                }
                await context.SaveChangesAsync();
            }
            finally
            {
                FuelProDbContext.BypassTracking = false;
            }
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

    private async Task PerformPullAsync(SyncSettings settings)
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
                var logQueryTime = settings.LastSyncTime.ToUniversalTime().ToString("o");
                var logResponse = await _httpClient.SendRequestAsync(HttpMethod.Get,
                    $"SyncChangeLogs?station_id=eq.{settings.StationId}&updated_at=gt.{logQueryTime}");
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
            // If this table has no mappings yet, perform a full sync from the beginning.
            var tableHasMappings = guidToLocal.TryGetValue(tableDef.TableName, out var mappings) && mappings.Count > 0;
            var tableQueryTime = tableHasMappings 
                ? settings.LastSyncTime.ToUniversalTime().ToString("o") 
                : DateTime.MinValue.ToUniversalTime().ToString("o");

            // Fetch records updated since tableQueryTime for this StationId
            var response = await _httpClient.SendRequestAsync(HttpMethod.Get,
                $"{tableDef.TableName}?station_id=eq.{settings.StationId}&updated_at=gt.{tableQueryTime}");
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.Warning("Supabase GET failed for table {Table}: {Error}", tableDef.TableName, error);
                continue;
            }

            var json = await response.Content.ReadAsStringAsync();
            var records = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(json);
            if (records == null || records.Count == 0) continue;

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
                    if (!dict.TryGetValue("SyncGuid", out var syncGuidObj) || syncGuidObj == null) continue;
                    var remoteGuid = syncGuidObj.ToString()!;

                    // Skip records that this machine pushed (avoid re-importing our own changes)
                    if (dict.TryGetValue("machine_id", out var machineIdObj) && machineIdObj != null)
                    {
                        if (machineIdObj.ToString() == settings.MachineId)
                        {
                            // This record was pushed by this machine — update the mapping if needed but skip data import
                            if (!tableMap.ContainsKey(remoteGuid) && dict.TryGetValue("local_id", out var localIdObj2) && localIdObj2 != null)
                            {
                                var localId2 = Convert.ToInt32(localIdObj2);
                                tableMap[remoteGuid] = localId2;
                                context.SyncIdMappings.Add(new SyncIdMapping
                                {
                                    TableName = tableDef.TableName,
                                    RemoteGuid = remoteGuid,
                                    LocalId = localId2
                                });
                            }
                            continue;
                        }
                    }

                    // ── Step 1: Remap FK values from parent GUIDs to local IDs ──
                    bool fkFailed = false;
                    foreach (var fk in tableDef.ForeignKeys)
                    {
                        if (!dict.TryGetValue(fk.FkProperty, out var fkVal) || fkVal == null) continue;

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
                                _logger.Warning(
                                    "Pull: FK {Fk}={Val} in {Table} has no mapping in {Ref}. Skipping record {Guid}.",
                                    fk.FkProperty, parentGuid, tableDef.TableName, fk.ReferencedTable, remoteGuid);
                                fkFailed = true;
                                break;
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
                            // Mapping is stale (record was deleted locally) — re-create
                            entity = Activator.CreateInstance(entityType.ClrType);
                            if (entity == null) continue;
                            context.Add(entity);
                            isNew = true;
                        }
                    }
                    else
                    {
                        // No mapping — this is a new record for this machine
                        entity = Activator.CreateInstance(entityType.ClrType);
                        if (entity == null) continue;
                        context.Add(entity);
                        isNew = true;
                    }

                    // ── Step 3: Set all non-PK properties from Supabase data ──
                    var entry = context.Entry(entity);
                    foreach (var prop in entityType.GetProperties())
                    {
                        if (prop.IsPrimaryKey()) continue;

                        if (dict.TryGetValue(prop.Name, out var val))
                        {
                            if (val == null)
                            {
                                entry.Property(prop.Name).CurrentValue = null;
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

        foreach (var property in entry.Metadata.GetProperties())
        {
            if (property.Name == "Id") continue;
            if (excludePk && pkProperties != null && pkProperties.Contains(property)) continue;
            
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
}
