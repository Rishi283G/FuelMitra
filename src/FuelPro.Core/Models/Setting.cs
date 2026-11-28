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

    [MaxLength(300)]
    public string? StationSubtitle { get; set; } = "(KANDHARE NANGADE SURVE GROUP)";

    [MaxLength(500)]
    public string? StationAddress { get; set; } = "Gat No. 119/B Jamgaon, Taluka Mulshi, Dist - Pune. POST-Shere, Pin - 412 108.";

    [MaxLength(300)]
    public string? StationContact { get; set; } = "Tel. : 8975549999 / 9850846783 | Email : kandharepetroleum@gmail.com";

    [MaxLength(100)]
    public string? OilCompany { get; set; } = "BPCL";

    [MaxLength(500)]
    public string? CustomLogoPath { get; set; }

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
                if (!dict.ContainsKey("HSD - 20KL II") && !dict.ContainsKey("HSD II") && !dict.ContainsKey("MS-II")) dict["HSD - 20KL II"] = MsIIRate;
                if (!dict.ContainsKey("CNG") && !dict.ContainsKey("CNG Line")) dict["CNG"] = CngRate;

                // Ensure cross-aliases exist so lookups by standard fuel name or tank name succeed
                if (!dict.ContainsKey("HSD") && dict.TryGetValue("HSD - 20KL", out var hVal)) dict["HSD"] = hVal;
                if (!dict.ContainsKey("HSD - 20KL") && dict.TryGetValue("HSD", out var hVal2)) dict["HSD - 20KL"] = hVal2;

                if (!dict.ContainsKey("MS-I") && dict.TryGetValue("MS - 20KL", out var mVal)) dict["MS-I"] = mVal;
                if (!dict.ContainsKey("MS - 20KL") && dict.TryGetValue("MS-I", out var mVal2)) dict["MS - 20KL"] = mVal2;

                if (!dict.ContainsKey("Speed") && dict.TryGetValue("Speed - 20KL", out var spVal)) dict["Speed"] = spVal;
                if (!dict.ContainsKey("Speed - 20KL") && dict.TryGetValue("Speed", out var spVal3)) dict["Speed - 20KL"] = spVal3;
                if (!dict.ContainsKey("MS-II") && dict.TryGetValue("MS - 20KL II", out var ms2Val)) dict["MS-II"] = ms2Val;
                if (!dict.ContainsKey("MS - 20KL II") && dict.TryGetValue("MS-II", out var ms2Val2)) dict["MS - 20KL II"] = ms2Val2;

                if (!dict.ContainsKey("CNG") && dict.TryGetValue("CNG Line", out var cVal)) dict["CNG"] = cVal;
                if (!dict.ContainsKey("CNG Line") && dict.TryGetValue("CNG", out var cVal2)) dict["CNG Line"] = cVal2;

                _fuelRatesCache = dict;
            }
            return _fuelRatesCache;
        }
        set
        {
            _fuelRatesCache = new Dictionary<string, double>(value ?? new(), StringComparer.OrdinalIgnoreCase);

            // Sync legacy fields
            if (_fuelRatesCache.TryGetValue("HSD - 20KL", out var hsd) || _fuelRatesCache.TryGetValue("HSD", out hsd))
                HsdRate = hsd;
            if (_fuelRatesCache.TryGetValue("MS - 20KL", out var ms1) || _fuelRatesCache.TryGetValue("MS", out ms1) || _fuelRatesCache.TryGetValue("MS-I", out ms1))
                MsIRate = ms1;
            if (_fuelRatesCache.TryGetValue("MS-II", out var ms2Rate) ||
                _fuelRatesCache.TryGetValue("MS - 20KL II", out ms2Rate) ||
                _fuelRatesCache.TryGetValue("HSD - 20KL II", out ms2Rate))
                MsIIRate = ms2Rate;
            if (_fuelRatesCache.TryGetValue("CNG", out var cng) || _fuelRatesCache.TryGetValue("CNG Line", out cng))
                CngRate = cng;

            // Ensure canonical keys are present in cache before serialization
            if (!_fuelRatesCache.ContainsKey("HSD")) _fuelRatesCache["HSD"] = HsdRate;
            if (!_fuelRatesCache.ContainsKey("MS-I")) _fuelRatesCache["MS-I"] = MsIRate;
            if (!_fuelRatesCache.ContainsKey("MS-II")) _fuelRatesCache["MS-II"] = MsIIRate;
            if (!_fuelRatesCache.ContainsKey("CNG")) _fuelRatesCache["CNG"] = CngRate;

            // Synchronize canonical SPEED keys if present
            if (_fuelRatesCache.TryGetValue("Speed", out var spR) && !_fuelRatesCache.ContainsKey("SPEED")) _fuelRatesCache["SPEED"] = spR;
            if (_fuelRatesCache.TryGetValue("SPEED", out var spR2) && !_fuelRatesCache.ContainsKey("Speed")) _fuelRatesCache["Speed"] = spR2;

            try
            {
                FuelRatesJson = System.Text.Json.JsonSerializer.Serialize(_fuelRatesCache);
            }
            catch
            {
                FuelRatesJson = "{}";
            }
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

        // 1. Check SPEED (BPCL premium product)
        if (normalized.Contains("SPEED", StringComparison.OrdinalIgnoreCase))
        {
            if (rates.TryGetValue("SPEED", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("Speed", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("SPEED - 20KL", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("Speed - 20KL", out rate) && rate > 0) return rate;
            return defaultFallback;
        }

        // 1b. Check XP95 (Independent IOCL premium product - never alias with SPEED)
        if (normalized.Contains("XP95", StringComparison.OrdinalIgnoreCase) || normalized.Contains("XP-95", StringComparison.OrdinalIgnoreCase))
        {
            if (rates.TryGetValue("XP95", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("XP-95", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("XP95 - 20KL", out rate) && rate > 0) return rate;
            return defaultFallback;
        }

        // 1c. Check Power (Independent HPCL premium product - never alias with SPEED)
        if (normalized.Contains("POWER", StringComparison.OrdinalIgnoreCase))
        {
            if (rates.TryGetValue("Power", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("POWER", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("Power - 20KL", out rate) && rate > 0) return rate;
            return defaultFallback;
        }

        // 2. Check HSD / Diesel
        if (normalized.Contains("HSD", StringComparison.OrdinalIgnoreCase) || 
            normalized.Contains("DIESEL", StringComparison.OrdinalIgnoreCase))
        {
            if (rates.TryGetValue("HSD - 20KL", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("HSD", out rate) && rate > 0) return rate;
            return HsdRate > 0 ? HsdRate : defaultFallback;
        }

        // 3. Check MS-II / Regular Petrol Tank 2
        if (normalized.Contains("MS-II", StringComparison.OrdinalIgnoreCase) || 
            normalized.Contains("MS2", StringComparison.OrdinalIgnoreCase) || 
            normalized.Contains("20KL II", StringComparison.OrdinalIgnoreCase))
        {
            if (rates.TryGetValue("MS - 20KL II", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("MS-II", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("HSD - 20KL II", out rate) && rate > 0) return rate;
            return MsIIRate > 0 ? MsIIRate : defaultFallback;
        }

        // 4. Check MS-I / Regular Petrol
        if (normalized.Contains("MS", StringComparison.OrdinalIgnoreCase) || 
            normalized.Contains("PETROL", StringComparison.OrdinalIgnoreCase))
        {
            if (rates.TryGetValue("MS - 20KL", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("MS-I", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("MS", out rate) && rate > 0) return rate;
            return MsIRate > 0 ? MsIRate : defaultFallback;
        }

        // 5. Check CNG
        if (normalized.Contains("CNG", StringComparison.OrdinalIgnoreCase))
        {
            if (rates.TryGetValue("CNG Line", out rate) && rate > 0) return rate;
            if (rates.TryGetValue("CNG", out rate) && rate > 0) return rate;
            return CngRate > 0 ? CngRate : defaultFallback;
        }

        return defaultFallback;
    }
}
