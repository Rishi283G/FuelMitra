using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FuelPro.Data;
using FuelPro.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FuelPro.Sync;

public class SyncConfigService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger = Log.ForContext<SyncConfigService>();

    public SyncConfigService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    private FuelProDbContext CreateDbContext() => _serviceProvider.GetRequiredService<FuelProDbContext>();

    public async Task<SyncSettings> GetSettingsAsync()
    {
        using var context = CreateDbContext();
        
        var url = await GetMetaValueAsync(context, "Sync.SupabaseUrl", "");
        var apiKey = await GetMetaValueAsync(context, "Sync.SupabaseApiKey", "");
        var stationId = await GetMetaValueAsync(context, "Sync.StationId", "");
        var machineId = await GetMetaValueAsync(context, "Sync.MachineId", "");
        var isEnabledStr = await GetMetaValueAsync(context, "Sync.IsEnabled", "false");
        var lastSyncStr = await GetMetaValueAsync(context, "Sync.LastSyncTime", "");

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
        DateTime.TryParse(lastSyncStr, out var lastSync);

        return new SyncSettings
        {
            SupabaseUrl = url,
            SupabaseApiKey = apiKey,
            StationId = stationId,
            MachineId = machineId,
            SyncEnabled = isEnabled,
            LastSyncTime = lastSync == default ? new DateTime(2026, 1, 1) : lastSync
        };
    }

    public async Task SaveSettingsAsync(SyncSettings settings)
    {
        using var context = CreateDbContext();

        await SetMetaValueAsync(context, "Sync.SupabaseUrl", settings.SupabaseUrl);
        await SetMetaValueAsync(context, "Sync.SupabaseApiKey", settings.SupabaseApiKey);
        await SetMetaValueAsync(context, "Sync.StationId", settings.StationId);
        await SetMetaValueAsync(context, "Sync.MachineId", settings.MachineId);
        await SetMetaValueAsync(context, "Sync.IsEnabled", settings.SyncEnabled.ToString());
        await SetMetaValueAsync(context, "Sync.LastSyncTime", settings.LastSyncTime.ToString("o"));
        
        await context.SaveChangesAsync();
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
    public string StationId { get; set; } = string.Empty;
    public string MachineId { get; set; } = string.Empty;
    public bool SyncEnabled { get; set; }
    public DateTime LastSyncTime { get; set; }
}
