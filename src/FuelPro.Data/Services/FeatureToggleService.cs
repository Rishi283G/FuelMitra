using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FuelPro.Data.Services;

public class FeatureToggleService : IFeatureToggleService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger = Log.ForContext<FeatureToggleService>();
    private readonly ConcurrentDictionary<string, bool> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _nameCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    public event Action? FeatureConfigurationChanged;

    public FeatureToggleService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public bool IsFeatureEnabled(string featureKey, bool defaultIfMissing = true)
    {
        if (string.IsNullOrWhiteSpace(featureKey)) return defaultIfMissing;

        if (_cache.TryGetValue(featureKey, out var enabled))
        {
            return enabled;
        }

        // If not initialized yet, kick off background load and return default
        if (!_initialized)
        {
            _ = RefreshCacheAsync();
        }

        return defaultIfMissing;
    }

    public string GetFeatureDisplayName(string featureKey, string defaultName = "")
    {
        if (string.IsNullOrWhiteSpace(featureKey)) return defaultName;

        if (_nameCache.TryGetValue(featureKey, out var customName) && !string.IsNullOrWhiteSpace(customName))
        {
            return customName;
        }

        if (!_initialized)
        {
            _ = RefreshCacheAsync();
        }

        return defaultName;
    }

    public async Task<List<AppFeatureSetting>> GetAllFeaturesAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            return await db.AppFeatureSettings
                .OrderBy(f => f.TargetRole)
                .ThenBy(f => f.Category)
                .ThenBy(f => f.DisplayOrder)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load AppFeatureSettings from database");
            return new List<AppFeatureSetting>();
        }
    }

    public async Task<bool> SaveFeaturesAsync(IEnumerable<AppFeatureSetting> features)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

            foreach (var item in features)
            {
                var existing = await db.AppFeatureSettings.FirstOrDefaultAsync(f => f.FeatureKey == item.FeatureKey);
                if (existing != null)
                {
                    existing.IsEnabled = item.IsEnabled;
                    existing.DisplayName = item.DisplayName;
                    existing.Description = item.Description;
                    existing.Category = item.Category;
                    existing.DisplayOrder = item.DisplayOrder;
                    existing.TargetRole = item.TargetRole;
                    existing.ConfigurationJson = item.ConfigurationJson;
                    existing.UpdatedAt = DateTime.Now;
                    db.Entry(existing).State = EntityState.Modified;
                }
                else
                {
                    item.UpdatedAt = DateTime.Now;
                    db.AppFeatureSettings.Add(item);
                }
            }

            await db.SaveChangesAsync();
            await RefreshCacheAsync();
            FeatureConfigurationChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save feature settings");
            return false;
        }
    }

    public async Task RefreshCacheAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var all = await db.AppFeatureSettings.ToListAsync();

            foreach (var item in all)
            {
                _cache[item.FeatureKey] = item.IsEnabled;
                if (!string.IsNullOrWhiteSpace(item.DisplayName))
                {
                    _nameCache[item.FeatureKey] = item.DisplayName;
                }
            }

            _initialized = true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to refresh feature toggle cache");
        }
    }
}
