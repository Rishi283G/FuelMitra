namespace FuelPro.Core.Common;

/// <summary>
/// Hardcoded pump-to-nozzle mapping and fuel type rules.
/// Loaded once, immutable configuration.
/// </summary>
public static class PumpConfiguration
{
    public static readonly DateTime MigrationCutoffDate = new(2026, 6, 14);

    /// <summary>
    /// Operational Pump ID → list of nozzle numbers assigned to that pump.
    /// </summary>
    public static readonly Dictionary<int, int[]> PumpNozzleMapping = new()
    {
        { 1, new[] { 1, 2 } },
        { 2, new[] { 3, 4 } },
        { 3, new[] { 5, 6, 7 } },
        { 4, new[] { 8, 9, 10 } },
        { 5, new[] { 11, 12, 13 } },
        { 6, new[] { 14, 15, 16 } },
        { 7, new[] { 17, 18 } },
        { 8, new[] { 19, 20 } }
    };

    /// <summary>
    /// Historical Pump ID → list of nozzle numbers assigned to that pump.
    /// </summary>
    private static readonly Dictionary<int, int[]> HistoricalPumpNozzleMapping = new()
    {
        { 1, new[] { 1, 2, 3, 4 } },
        { 2, new[] { 5, 6, 7, 8, 9, 10 } },
        { 3, new[] { 11, 12, 13, 14, 15, 16 } },
        { 4, new[] { 17, 18, 19, 20 } }
    };

    /// <summary>
    /// Exact (PumpId, NozzleNumber) to FuelType mapping for current 8 operational pumps.
    /// </summary>
    private static readonly Dictionary<(int PumpId, int NozzleNumber), FuelType> NozzleFuelMap = new()
    {
        // Pump 1
        { (1, 1), FuelType.MS_I },
        { (1, 2), FuelType.MS_II },

        // Pump 2
        { (2, 3), FuelType.MS_I },
        { (2, 4), FuelType.MS_II },

        // Pump 3
        { (3, 5), FuelType.MS_I },
        { (3, 6), FuelType.MS_II },
        { (3, 7), FuelType.HSD },

        // Pump 4
        { (4, 8), FuelType.MS_I },
        { (4, 9), FuelType.MS_II },
        { (4, 10), FuelType.HSD },

        // Pump 5
        { (5, 11), FuelType.MS_I },
        { (5, 12), FuelType.MS_II },
        { (5, 13), FuelType.HSD },

        // Pump 6
        { (6, 14), FuelType.MS_I },
        { (6, 15), FuelType.MS_II },
        { (6, 16), FuelType.HSD },

        // Pump 7
        { (7, 17), FuelType.HSD },
        { (7, 18), FuelType.HSD },

        // Pump 8
        { (8, 19), FuelType.HSD },
        { (8, 20), FuelType.HSD }
    };

    /// <summary>
    /// Historical (PumpId, NozzleNumber) to FuelType mapping for physical machines.
    /// </summary>
    private static readonly Dictionary<(int PumpId, int NozzleNumber), FuelType> HistoricalNozzleFuelMap = new()
    {
        // Pump 1
        { (1, 1), FuelType.MS_I },
        { (1, 2), FuelType.MS_II },
        { (1, 3), FuelType.MS_I },
        { (1, 4), FuelType.MS_II },

        // Pump 2
        { (2, 5), FuelType.MS_II },
        { (2, 6), FuelType.HSD },
        { (2, 7), FuelType.HSD },
        { (2, 8), FuelType.MS_I },
        { (2, 9), FuelType.MS_II },
        { (2, 10), FuelType.MS_I },

        // Pump 3
        { (3, 11), FuelType.MS_I },
        { (3, 12), FuelType.MS_II },
        { (3, 13), FuelType.HSD },
        { (3, 14), FuelType.MS_I },
        { (3, 15), FuelType.MS_II },
        { (3, 16), FuelType.HSD },

        // Pump 4
        { (4, 17), FuelType.HSD },
        { (4, 18), FuelType.HSD },
        { (4, 19), FuelType.HSD },
        { (4, 20), FuelType.HSD }
    };

    /// <summary>
    /// Expose all defined nozzles.
    /// </summary>
    public static readonly List<(int PumpId, int NozzleNumber)> AllNozzles = NozzleFuelMap.Keys.ToList();

    public static FuelType GetFuelType(int pumpId, int nozzleNumber, DateTime? date = null)
    {
        var isHistorical = date.HasValue && date.Value.Date < MigrationCutoffDate;
        if (isHistorical)
        {
            if (HistoricalNozzleFuelMap.TryGetValue((pumpId, nozzleNumber), out var historicalFuelType))
                return historicalFuelType;

            // Historical fallback to prevent crash and preserve correct classification of old records
            if (nozzleNumber == 1 || nozzleNumber == 2 || nozzleNumber == 7 || nozzleNumber == 8)
                return FuelType.HSD;
            
            if (nozzleNumber == 3 || nozzleNumber == 4 || nozzleNumber == 9 || nozzleNumber == 10 || 
                nozzleNumber == 13 || nozzleNumber == 14 || nozzleNumber == 17 || nozzleNumber == 18 || 
                nozzleNumber == 23 || nozzleNumber == 24 || nozzleNumber == 27 || nozzleNumber == 28)
            {
                return FuelType.MS_II;
            }

            return FuelType.MS_I;
        }

        // For current/new records, avoid fallback logic and throw exception on invalid mapping
        if (NozzleFuelMap.TryGetValue((pumpId, nozzleNumber), out var fuelType))
            return fuelType;

        throw new ArgumentException($"Invalid operational pump and nozzle mapping: Pump {pumpId}, Nozzle {nozzleNumber}");
    }

    /// <summary>
    /// Returns the canonical display name ("HSD", "MS-I", "MS-II") for a pump ID and nozzle number.
    /// Use this instead of the stored FuelType string to ensure old DB records
    /// are classified correctly even if they were saved with a stale mapping.
    /// </summary>
    public static string GetFuelTypeDisplayName(int pumpId, int nozzleNumber, DateTime? date = null)
        => GetFuelType(pumpId, nozzleNumber, date).ToDisplayName();

    /// <summary>
    /// Get all nozzle numbers for a given pump ID.
    /// </summary>
    public static int[] GetNozzlesForPump(int pumpId, DateTime? date = null)
    {
        var isHistorical = date.HasValue && date.Value.Date < MigrationCutoffDate;
        var map = isHistorical ? HistoricalPumpNozzleMapping : PumpNozzleMapping;
        return map.TryGetValue(pumpId, out var nozzles)
            ? nozzles
            : Array.Empty<int>();
    }

    /// <summary>
    /// Get display items for pump dropdown: "Pump 1 : 1, 2, 3, 4"
    /// </summary>
    public static List<PumpDisplayItem> GetPumpDisplayItems(DateTime? date = null)
    {
        var isHistorical = date.HasValue && date.Value.Date < MigrationCutoffDate;
        var map = isHistorical ? HistoricalPumpNozzleMapping : PumpNozzleMapping;
        return map
            .OrderBy(p => p.Key)
            .Select(p => new PumpDisplayItem
            {
                PumpId = p.Key,
                DisplayText = $"Pump {p.Key}  :  {string.Join(", ", p.Value)}"
            })
            .ToList();
    }

    /// <summary>
    /// Total number of pumps.
    /// </summary>
    public const int TotalPumps = 8;

    /// <summary>
    /// Total number of nozzles.
    /// </summary>
    public const int TotalNozzles = 20;

    /// <summary>
    /// Denomination values for cash counting.
    /// </summary>
    public static readonly int[] Denominations = { 500, 200, 100, 50, 20, 10 };

    /// <summary>
    /// Tank configuration.
    /// </summary>
    public static readonly Dictionary<string, string> Tanks = new()
    {
        { "Tank 1", "HSD" },
        { "Tank 2", "MS (MS-I)" },
        { "Tank 3", "MS (MS-II)" }
    };
}

/// <summary>
/// Display item for pump dropdown.
/// </summary>
public class PumpDisplayItem
{
    public int PumpId { get; set; }
    public string DisplayText { get; set; } = "";
    public override string ToString() => DisplayText;
}
