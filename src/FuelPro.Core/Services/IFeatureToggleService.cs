using FuelPro.Core.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FuelPro.Core.Services;

/// <summary>
/// Service interface for centralized Developer-controlled feature and page configuration.
/// </summary>
public interface IFeatureToggleService
{
    event Action? FeatureConfigurationChanged;

    /// <summary>
    /// Checks whether a specific feature or page is enabled.
    /// Synchronous for fast UI binding.
    /// </summary>
    bool IsFeatureEnabled(string featureKey, bool defaultIfMissing = true);

    /// <summary>
    /// Gets the custom display name for a feature (e.g. custom Personal Ledger name).
    /// </summary>
    string GetFeatureDisplayName(string featureKey, string defaultName = "");

    /// <summary>
    /// Checks whether DSM PWA integration is enabled.
    /// </summary>
    bool IsDsmPwaEnabled => IsFeatureEnabled("Integration_DsmPwa", true);

    /// <summary>
    /// Checks whether collections should be split into Morning/Night slots.
    /// </summary>
    bool UseMorningNightCollections => IsFeatureEnabled("Collection_UseMorningNight", false);

    /// <summary>
    /// Gets all feature settings from database/cache.
    /// </summary>
    Task<List<AppFeatureSetting>> GetAllFeaturesAsync();

    /// <summary>
    /// Updates multiple feature settings.
    /// </summary>
    Task<bool> SaveFeaturesAsync(IEnumerable<AppFeatureSetting> features);

    /// <summary>
    /// Refreshes in-memory cache from the database.
    /// </summary>
    Task RefreshCacheAsync();
}
