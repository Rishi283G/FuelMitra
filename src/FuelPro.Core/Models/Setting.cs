using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class Setting
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int SettingId { get; set; }

    public double HsdRate { get; set; } = 90.35;

    public double MsIRate { get; set; } = 103.81;
    public double MsIIRate { get; set; } = 103.81;
    public double CngRate { get; set; } = 85.0;

    [MaxLength(300)]
    public string PumpStationName { get; set; } = "Mitali Service Station";

    [MaxLength(100)]
    public string? Shift1Manager { get; set; }

    [MaxLength(100)]
    public string? Shift2Manager { get; set; }

    [MaxLength(100)]
    public string? Shift3Manager { get; set; }

    [NotMapped]
    public string StationDisplayName
    {
        get => PumpStationName;
        set => PumpStationName = value;
    }

    public DateTime LastUpdated { get; set; } = DateTime.Now;

    /// <summary>
    /// JSON dictionary storing dynamic rates for all tanks and fuel types:
    /// e.g. {"HSD - 20KL": 90.35, "MS - 20KL": 103.81, "XP95 - 20KL": 112.50, "CNG": 85.0}
    /// </summary>
    public string? FuelRatesJson { get; set; }

    /// <summary>
    /// Serialized JSON for dynamic TankDefinitions, ensuring station tanks sync to cloud.
    /// </summary>
    public string? TankDefinitionsJson { get; set; }

    /// <summary>
    /// Serialized JSON for dynamic CollectionTypes, ensuring payment types sync to cloud.
    /// </summary>
    public string? CollectionTypesJson { get; set; }

    /// <summary>
    /// Serialized JSON for dynamic AppFeatureSettings, ensuring feature access syncs to cloud.
    /// </summary>
    public string? AppFeatureSettingsJson { get; set; }

    /// <summary>
    /// Serialized JSON for dynamic PumpMappings, ensuring station pump-to-nozzle layout syncs reliably to cloud and across app sessions.
    /// </summary>
    public string? PumpMappingsJson { get; set; }

    /// <summary>
    /// Serialized JSON for dynamic PumpConnectionRules, defining configurable 2, 3, or 4-pump connection groups.
    /// Ensures connection topology syncs reliably to cloud and across app sessions.
    /// </summary>
    public string? PumpConnectionRulesJson { get; set; }

    [NotMapped]
    private Dictionary<string, double>? _fuelRatesCache;

    [NotMapped]
    public Dictionary<string, double> FuelRates
    {
        get
        {
            if (_fuelRatesCache == null)
            {
                var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(FuelRatesJson))
                {
                    try
                    {
                        var deserialized = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(FuelRatesJson);
                        if (deserialized != null)
                        {
                            foreach (var kvp in deserialized)
                            {
                                dict[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    catch { }
                }

                // Fallbacks for standard tanks / fuel types if not in JSON
                if (!dict.ContainsKey("HSD - 20KL") && !dict.ContainsKey("HSD")) dict["HSD - 20KL"] = HsdRate;
                if (!dict.ContainsKey("MS - 20KL") && !dict.ContainsKey("MS-I") && !dict.ContainsKey("MS")) dict["MS - 20KL"] = MsIRate;
                if (!dict.ContainsKey("HSD - 20KL II") && !dict.ContainsKey("HSD II")) dict["HSD - 20KL II"] = MsIIRate;
                if (!dict.ContainsKey("CNG")) dict["CNG"] = CngRate;

                _fuelRatesCache = dict;
            }
            return _fuelRatesCache;
        }
        set
        {
            _fuelRatesCache = new Dictionary<string, double>(value ?? new(), StringComparer.OrdinalIgnoreCase);
            try
            {
                FuelRatesJson = System.Text.Json.JsonSerializer.Serialize(_fuelRatesCache);
            }
            catch
            {
                FuelRatesJson = "{}";
            }

            // Sync legacy fields
            if (_fuelRatesCache.TryGetValue("HSD - 20KL", out var hsd) || _fuelRatesCache.TryGetValue("HSD", out hsd))
                HsdRate = hsd;
            if (_fuelRatesCache.TryGetValue("MS - 20KL", out var ms1) || _fuelRatesCache.TryGetValue("MS", out ms1) || _fuelRatesCache.TryGetValue("MS-I", out ms1))
                MsIRate = ms1;
            if (_fuelRatesCache.TryGetValue("HSD - 20KL II", out var hsd2) || _fuelRatesCache.TryGetValue("MS-II", out hsd2))
                MsIIRate = hsd2;
            if (_fuelRatesCache.TryGetValue("CNG", out var cng))
                CngRate = cng;
        }
    }

    /// <summary>
    /// Gets the configured rate for a tank name, fuel product, or fallback.
    /// </summary>
    public double GetRateFor(string? key, double defaultFallback = 0.0)
    {
        if (string.IsNullOrWhiteSpace(key)) return defaultFallback;
        var rates = FuelRates;
        if (rates.TryGetValue(key, out var rate) && rate > 0) return rate;

        // Try normalized key variants
        string normalized = key.Trim();
        if (rates.TryGetValue(normalized, out rate) && rate > 0) return rate;

        // Try standard mappings
        if (normalized.Contains("HSD", StringComparison.OrdinalIgnoreCase))
        {
            if (rates.TryGetValue("HSD - 20KL", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("HSD", out rate) && rate > 0) return rate;
            return HsdRate > 0 ? HsdRate : defaultFallback;
        }
        if (normalized.Contains("MS", StringComparison.OrdinalIgnoreCase) || normalized.Contains("PETROL", StringComparison.OrdinalIgnoreCase))
        {
            if (rates.TryGetValue("MS - 20KL", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("MS", out rate) && rate > 0) return rate;
            return MsIRate > 0 ? MsIRate : defaultFallback;
        }
        if (normalized.Contains("CNG", StringComparison.OrdinalIgnoreCase))
        {
            if (rates.TryGetValue("CNG", out rate) && rate > 0) return rate;
            return CngRate > 0 ? CngRate : defaultFallback;
        }

        return defaultFallback;
    }
}
