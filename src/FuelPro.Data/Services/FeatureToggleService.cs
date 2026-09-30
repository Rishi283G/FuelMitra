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
            var existingRows = await db.AppFeatureSettings
                .OrderBy(f => f.TargetRole)
                .ThenBy(f => f.Category)
                .ThenBy(f => f.DisplayOrder)
                .ToListAsync();

            if (existingRows.Count > 0)
            {
                return existingRows;
            }

            // Local table is empty. Attempt self-healing.
            _logger.Information("AppFeatureSettings table is empty. Initiating self-healing feature restoration.");

            var canonicalDefaults = AppFeatureSetting.GetCanonicalDefaults(DateTime.Now);
            var featuresToPersist = new List<AppFeatureSetting>();

            Setting? setting = null;
            try
            {
                setting = await db.Settings.FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not query Settings table during feature self-healing.");
            }

            bool restoredFromJson = false;
            if (setting != null && !string.IsNullOrWhiteSpace(setting.AppFeatureSettingsJson))
            {
                var candidateList = TryDeserializeAndValidateFeatures(setting.AppFeatureSettingsJson);
                if (candidateList != null && candidateList.Count > 0)
                {
                    var restoredKeySet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var feat in candidateList)
                    {
                        feat.Id = 0; // Reset PK for identity insert
                        featuresToPersist.Add(feat);
                        restoredKeySet.Add(feat.FeatureKey);
                    }

                    // Backfill any missing canonical feature keys using canonical defaults
                    var missingDefaults = canonicalDefaults
                        .Where(cd => !restoredKeySet.Contains(cd.FeatureKey))
                        .ToList();

                    foreach (var missing in missingDefaults)
                    {
                        missing.Id = 0;
                        featuresToPersist.Add(missing);
                    }

                    restoredFromJson = true;
                    _logger.Information("Successfully restored {RestoredCount} feature settings from Settings.AppFeatureSettingsJson (backfilled {BackfillCount} missing canonical features).",
                        candidateList.Count, missingDefaults.Count);
                }
                else
                {
                    _logger.Warning("Settings.AppFeatureSettingsJson was present but invalid/unusable. Falling back to canonical 34 defaults.");
                }
            }

            if (!restoredFromJson)
            {
                // Fall back to canonical 34 defaults
                foreach (var cd in canonicalDefaults)
                {
                    cd.Id = 0;
                    featuresToPersist.Add(cd);
                }
                _logger.Information("Seeded canonical {Count} default feature settings.", featuresToPersist.Count);
            }

            // Persist to database
            db.AppFeatureSettings.AddRange(featuresToPersist);
            await db.SaveChangesAsync();

            // Populate/update Settings.AppFeatureSettingsJson consistently
            if (setting != null)
            {
                try
                {
                    setting.AppFeatureSettingsJson = System.Text.Json.JsonSerializer.Serialize(featuresToPersist);
                    setting.LastUpdated = DateTime.Now;
                    db.Entry(setting).State = EntityState.Modified;
                    await db.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to update Settings.AppFeatureSettingsJson after self-healing feature seeding.");
                }
            }

            await RefreshCacheAsync();
            FeatureConfigurationChanged?.Invoke();

            return await db.AppFeatureSettings
                .OrderBy(f => f.TargetRole)
                .ThenBy(f => f.Category)
                .ThenBy(f => f.DisplayOrder)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load or self-heal AppFeatureSettings from database");
            return new List<AppFeatureSetting>();
        }
    }

    private List<AppFeatureSetting>? TryDeserializeAndValidateFeatures(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        List<AppFeatureSetting>? candidateList = null;

        try
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            candidateList = System.Text.Json.JsonSerializer.Deserialize<List<AppFeatureSetting>>(json, options);
        }
        catch
        {
            try
            {
                candidateList = Newtonsoft.Json.JsonConvert.DeserializeObject<List<AppFeatureSetting>>(json);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to deserialize AppFeatureSettingsJson using both System.Text.Json and Newtonsoft.Json.");
                return null;
            }
        }

        if (candidateList == null || candidateList.Count == 0) return null;

        var validRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Manager", "Owner", "Global", "Collection", "Station"
        };

        var validatedFeatures = new List<AppFeatureSetting>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in candidateList)
        {
            if (item == null) return null; // structurally invalid record

            if (string.IsNullOrWhiteSpace(item.FeatureKey))
            {
                _logger.Warning("AppFeatureSettingsJson contains record with missing or empty FeatureKey.");
                return null;
            }

            if (seenKeys.Contains(item.FeatureKey))
            {
                _logger.Warning("AppFeatureSettingsJson contains duplicate FeatureKey '{Key}'. Rejecting payload.", item.FeatureKey);
                return null; // Reject duplicate FeatureKey per requirement
            }

            var role = string.IsNullOrWhiteSpace(item.TargetRole) ? "Global" : item.TargetRole.Trim();
            if (!validRoles.Contains(role))
            {
                _logger.Warning("AppFeatureSettingsJson contains unrecognized TargetRole '{Role}' for FeatureKey '{Key}'. Rejecting payload.", item.TargetRole, item.FeatureKey);
                return null;
            }

            item.TargetRole = role;
            item.DisplayName = item.DisplayName ?? string.Empty;
            item.Description = item.Description ?? string.Empty;
            item.Category = item.Category ?? "General";
            item.UpdatedAt = item.UpdatedAt == default ? DateTime.Now : item.UpdatedAt;

            seenKeys.Add(item.FeatureKey);
            validatedFeatures.Add(item);
        }

        return validatedFeatures;
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

            // Sync updated features to Settings.AppFeatureSettingsJson for cloud synchronization
            try
            {
                var allFeatures = await db.AppFeatureSettings.ToListAsync();
                var setting = await db.Settings.FirstOrDefaultAsync();
                if (setting != null)
                {
                    setting.AppFeatureSettingsJson = System.Text.Json.JsonSerializer.Serialize(allFeatures);
                    setting.LastUpdated = DateTime.Now;
                    db.Entry(setting).State = EntityState.Modified;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to update Settings.AppFeatureSettingsJson in SaveFeaturesAsync");
            }

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
