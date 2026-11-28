using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FuelPro.Data;
using FuelPro.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Serilog;

namespace FuelPro.Sync;

public sealed record ValidatedApprovalAudit(
    Guid SubmissionId,
    int DsmEntryId,
    string ApprovedBy,
    DateTime ApprovedAt);

public class SyncConfigService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger = Log.ForContext<SyncConfigService>();

    public event Action<SyncSettings>? SettingsChanged;

    public SyncConfigService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    private FuelProDbContext CreateDbContext() => _serviceProvider.GetRequiredService<FuelProDbContext>();

    public virtual async Task<Dictionary<Guid, ValidatedApprovalAudit>> GetValidatedApprovedAuditsAsync()
    {
        using var context = CreateDbContext();
        return await ValidateApprovalAuditsAsync(context, _logger);
    }

    public static async Task<Dictionary<Guid, ValidatedApprovalAudit>> ValidateApprovalAuditsAsync(
        FuelProDbContext context,
        ILogger? logger = null)
    {
        var log = logger ?? Log.ForContext<SyncConfigService>();
        var result = new Dictionary<Guid, ValidatedApprovalAudit>();

        var audits = await context.DsmApprovalAudits
            .AsNoTracking()
            .OrderByDescending(a => a.ApprovedAt)
            .ThenByDescending(a => a.DsmApprovalAuditId)
            .ToListAsync();

        if (audits.Count == 0)
            return result;

        var existingEntryIds = new HashSet<int>(
            await context.DsmEntries
                .AsNoTracking()
                .Select(e => e.DsmEntryId)
                .ToListAsync());

        foreach (var audit in audits)
        {
            if (audit.SubmissionId == Guid.Empty)
            {
                log.Warning("Ignoring invalid DsmApprovalAudit {AuditId}: empty SubmissionId.", audit.DsmApprovalAuditId);
                continue;
            }

            if (string.IsNullOrWhiteSpace(audit.ApprovedBy) || audit.ApprovedAt == default)
            {
                log.Warning(
                    "Ignoring incomplete DsmApprovalAudit {AuditId} for SubmissionId {SubId}: missing ApprovedBy ('{ApprovedBy}') or ApprovedAt ({ApprovedAt}).",
                    audit.DsmApprovalAuditId, audit.SubmissionId, audit.ApprovedBy, audit.ApprovedAt);
                continue;
            }

            if (string.IsNullOrWhiteSpace(audit.ApprovedDataJson))
            {
                log.Warning(
                    "Ignoring incomplete DsmApprovalAudit {AuditId} for SubmissionId {SubId}: ApprovedDataJson is empty.",
                    audit.DsmApprovalAuditId, audit.SubmissionId);
                continue;
            }

            int entryId = 0;
            try
            {
                var obj = JObject.Parse(audit.ApprovedDataJson);
                var tok = obj["DsmEntryId"] ?? obj["dsmEntryId"];
                if (tok != null && tok.Type != JTokenType.Null)
                {
                    entryId = tok.Value<int>();
                }
            }
            catch (Exception ex)
            {
                log.Warning(
                    ex,
                    "Ignoring invalid DsmApprovalAudit {AuditId} for SubmissionId {SubId}: failed to parse ApprovedDataJson.",
                    audit.DsmApprovalAuditId, audit.SubmissionId);
                continue;
            }

            if (entryId <= 0)
            {
                log.Warning(
                    "Ignoring incomplete DsmApprovalAudit {AuditId} for SubmissionId {SubId}: missing positive DsmEntryId in ApprovedDataJson ({Json}).",
                    audit.DsmApprovalAuditId, audit.SubmissionId, audit.ApprovedDataJson);
                continue;
            }

            if (!existingEntryIds.Contains(entryId))
            {
                log.Warning(
                    "Ignoring orphaned DsmApprovalAudit {AuditId} for SubmissionId {SubId}: referenced DsmEntryId {EntryId} does not exist in local DsmEntries.",
                    audit.DsmApprovalAuditId, audit.SubmissionId, entryId);
                continue;
            }

            if (!result.ContainsKey(audit.SubmissionId))
            {
                result[audit.SubmissionId] = new ValidatedApprovalAudit(
                    audit.SubmissionId,
                    entryId,
                    audit.ApprovedBy.Trim(),
                    audit.ApprovedAt);
            }
        }

        return result;
    }

    public virtual async Task<HashSet<Guid>> GetApprovedSubmissionIdsAsync()
    {
        var validated = await GetValidatedApprovedAuditsAsync();
        return new HashSet<Guid>(validated.Keys);
    }

    public async Task<SyncSettings> GetSettingsAsync()
    {
        using var context = CreateDbContext();
        
        var url = await GetMetaValueAsync(context, "Sync.SupabaseUrl", "");
        var apiKeyRaw = await GetMetaValueAsync(context, "Sync.SupabaseApiKey", "");
        var serviceRoleKeyRaw = await GetMetaValueAsync(context, "Sync.SupabaseServiceRoleKey", "");
        var apiKey = Security.WindowsDpapiProtection.Unprotect(apiKeyRaw);
        var serviceRoleKey = Security.WindowsDpapiProtection.Unprotect(serviceRoleKeyRaw);
        var stationId = await GetMetaValueAsync(context, "Sync.StationId", "");
        var machineId = await GetMetaValueAsync(context, "Sync.MachineId", "");
        var isEnabledStr = await GetMetaValueAsync(context, "Sync.IsEnabled", "false");
        var multiComputerStr = await GetMetaValueAsync(context, "Sync.MultiComputerEnabled", "false");
        var lastSyncStr = await GetMetaValueAsync(context, "Sync.LastSyncTime", "");
        var lastPullStr = await GetMetaValueAsync(context, "Sync.LastPullTime", "");
        var isRevokedStr = await GetMetaValueAsync(context, "Sync.IsDeviceRevoked", "false");
        var revocationReason = await GetMetaValueAsync(context, "Sync.DeviceRevocationReason", "");

        if (string.IsNullOrEmpty(stationId))
        {
            // Auto-generate a unique StationId if not configured yet
            stationId = "pump-" + Guid.NewGuid().ToString("n").Substring(0, 12);
            await SetMetaValueAsync(context, "Sync.StationId", stationId);
            await context.SaveChangesAsync();
        }

        if (string.IsNullOrEmpty(machineId))
        {
            machineId = System.Environment.MachineName;
            if (string.IsNullOrEmpty(machineId))
            {
                machineId = "mc-" + Guid.NewGuid().ToString("n").Substring(0, 12);
            }
            await SetMetaValueAsync(context, "Sync.MachineId", machineId);
            await context.SaveChangesAsync();
        }

        bool.TryParse(isEnabledStr, out var isEnabled);
        bool.TryParse(multiComputerStr, out var multiComputerEnabled);
        bool.TryParse(isRevokedStr, out var isRevoked);
        DateTime.TryParse(lastSyncStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var lastSync);
        DateTime.TryParse(lastPullStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var lastPull);

        var effectiveLastSync = lastSync == default ? new DateTime(2026, 1, 1) : lastSync;
        var effectiveLastPull = lastPull == default ? effectiveLastSync : lastPull;

        return new SyncSettings
        {
            SupabaseUrl = url,
            SupabaseApiKey = apiKey,
            SupabaseServiceRoleKey = serviceRoleKey,
            StationId = stationId,
            MachineId = machineId,
            SyncEnabled = isEnabled,
            MultiComputerEnabled = multiComputerEnabled,
            IsDeviceRevoked = isRevoked,
            DeviceRevocationReason = string.IsNullOrWhiteSpace(revocationReason) ? null : revocationReason,
            LastSyncTime = effectiveLastSync,
            LastPullTime = effectiveLastPull
        };
    }

    public async Task SetDeviceRevocationAsync(bool isRevoked, string? reason = null)
    {
        using var context = CreateDbContext();
        await SetMetaValueAsync(context, "Sync.IsDeviceRevoked", isRevoked.ToString());
        await SetMetaValueAsync(context, "Sync.DeviceRevocationReason", reason ?? "");
        await context.SaveChangesAsync();
    }

    public static void ApplyAdminSyncSettings(
        SyncSettings existing,
        string supabaseUrl,
        string supabaseApiKey,
        string stationId,
        string machineId,
        bool syncEnabled,
        bool multiComputerEnabled,
        DateTime engineLastSyncTime)
    {
        ArgumentNullException.ThrowIfNull(existing);
        existing.SupabaseUrl = (supabaseUrl ?? string.Empty).Trim();
        existing.SupabaseApiKey = (supabaseApiKey ?? string.Empty).Trim();
        existing.StationId = (stationId ?? string.Empty).Trim();
        existing.MachineId = (machineId ?? string.Empty).Trim();
        existing.SyncEnabled = syncEnabled;
        existing.MultiComputerEnabled = multiComputerEnabled;
        if (engineLastSyncTime != default)
        {
            existing.LastSyncTime = engineLastSyncTime;
        }
    }

    public static void ApplyDeveloperCloudSettings(
        SyncSettings existing,
        string supabaseUrl,
        string supabaseApiKey,
        string supabaseServiceRoleKey,
        string stationId,
        string machineId,
        bool syncEnabled,
        DateTime engineLastSyncTime)
    {
        ArgumentNullException.ThrowIfNull(existing);
        existing.SupabaseUrl = (supabaseUrl ?? string.Empty).Trim();
        existing.SupabaseApiKey = (supabaseApiKey ?? string.Empty).Trim();
        existing.SupabaseServiceRoleKey = (supabaseServiceRoleKey ?? string.Empty).Trim();
        existing.StationId = (stationId ?? string.Empty).Trim();
        existing.MachineId = (machineId ?? string.Empty).Trim();
        existing.SyncEnabled = syncEnabled;
        if (engineLastSyncTime != default)
        {
            existing.LastSyncTime = engineLastSyncTime;
        }
    }

    public async Task SaveSettingsAsync(SyncSettings settings)
    {
        using var context = CreateDbContext();

        var protectedApiKey = Security.WindowsDpapiProtection.Protect(settings.SupabaseApiKey);
        var protectedServiceRoleKey = Security.WindowsDpapiProtection.Protect(settings.SupabaseServiceRoleKey);

        await SetMetaValueAsync(context, "Sync.SupabaseUrl", settings.SupabaseUrl);
        await SetMetaValueAsync(context, "Sync.SupabaseApiKey", protectedApiKey);
        await SetMetaValueAsync(context, "Sync.SupabaseServiceRoleKey", protectedServiceRoleKey);
        await SetMetaValueAsync(context, "Sync.StationId", settings.StationId);
        await SetMetaValueAsync(context, "Sync.MachineId", settings.MachineId);
        await SetMetaValueAsync(context, "Sync.IsEnabled", settings.SyncEnabled.ToString());
        await SetMetaValueAsync(context, "Sync.MultiComputerEnabled", settings.MultiComputerEnabled.ToString());
        await SetMetaValueAsync(context, "Sync.IsDeviceRevoked", settings.IsDeviceRevoked.ToString());
        await SetMetaValueAsync(context, "Sync.DeviceRevocationReason", settings.DeviceRevocationReason ?? "");
        await SetMetaValueAsync(context, "Sync.LastSyncTime", settings.LastSyncTime.ToString("o"));
        await SetMetaValueAsync(context, "Sync.LastPullTime", settings.LastPullTime.ToString("o"));
        
        await context.SaveChangesAsync();
        SettingsChanged?.Invoke(settings);
    }

    private async Task<string> GetMetaValueAsync(FuelProDbContext context, string key, string defaultValue)
    {
        var meta = await context.AppMeta.FirstOrDefaultAsync(m => m.Key == key);
        return meta?.Value ?? defaultValue;
    }

    private async Task SetMetaValueAsync(FuelProDbContext context, string key, string value)
    {
        var meta = await context.AppMeta.FirstOrDefaultAsync(m => m.Key == key);
        if (meta == null)
        {
            meta = new AppMeta { Key = key, Value = value };
            context.AppMeta.Add(meta);
        }
        else
        {
            meta.Value = value;
            context.Entry(meta).State = EntityState.Modified;
        }
    }
}

public class SyncSettings
{
    public string SupabaseUrl { get; set; } = string.Empty;
    public string SupabaseApiKey { get; set; } = string.Empty;
    public string SupabaseServiceRoleKey { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public bool SyncEnabled { get; set; }
    public bool MultiComputerEnabled { get; set; }
    public bool IsDeviceRevoked { get; set; }
    public string? DeviceRevocationReason { get; set; }
    public DateTime LastSyncTime { get; set; }
    public DateTime LastPullTime { get; set; }
}
