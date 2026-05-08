namespace FuelPro.Core.Common;

/// <summary>
/// Hardcoded pump-to-nozzle mapping and fuel type rules.
/// Loaded once, immutable configuration.
/// </summary>
public static class PumpConfiguration
{
    /// <summary>
    /// Pump ID → list of nozzle numbers assigned to that pump.
    /// </summary>
    public static readonly Dictionary<int, int[]> PumpNozzleMapping = new()
    {
        { 1,  new[] { 2, 4, 6 } },
        { 2,  new[] { 1, 3, 5 } },
        { 3,  new[] { 8, 10, 12 } },
        { 4,  new[] { 7, 9, 11 } },
        { 5,  new[] { 14, 16 } },
        { 6,  new[] { 13, 15 } },
        { 7,  new[] { 18, 20 } },
        { 8,  new[] { 17, 19 } },
        { 9,  new[] { 21, 23 } },
        { 10, new[] { 22, 24 } },
        { 11, new[] { 25, 27 } },
        { 12, new[] { 26, 28 } },
    };

    /// <summary>
    /// Exact nozzle-to-fuel-type mapping per the pump station configuration.
    /// Nozzles 1,2,7,8 → HSD
    /// Nozzles 3,4,9,10,13,14,17,18,21,22,25,26 → MS-II
    /// Nozzles 5,6,11,12,15,16,19,20,23,24,27,28 → MS-I
    /// </summary>
    private static readonly Dictionary<int, FuelType> NozzleFuelMap = new()
    {
        { 1, FuelType.HSD },    { 2, FuelType.HSD },
        { 3, FuelType.MS_II },  { 4, FuelType.MS_II },
        { 5, FuelType.MS_I },   { 6, FuelType.MS_I },
        { 7, FuelType.HSD },    { 8, FuelType.HSD },
        { 9, FuelType.MS_II },  { 10, FuelType.MS_II },
        { 11, FuelType.MS_I },  { 12, FuelType.MS_I },
        { 13, FuelType.MS_II }, { 14, FuelType.MS_II },
        { 15, FuelType.MS_I },  { 16, FuelType.MS_I },
        { 17, FuelType.MS_II }, { 18, FuelType.MS_II },
        { 19, FuelType.MS_I },  { 20, FuelType.MS_I },
        { 21, FuelType.MS_II }, { 22, FuelType.MS_II },
        { 23, FuelType.MS_I },  { 24, FuelType.MS_I },
        { 25, FuelType.MS_II }, { 26, FuelType.MS_II },
        { 27, FuelType.MS_I },  { 28, FuelType.MS_I },
    };

    /// <summary>
    /// Determines the fuel type for a given nozzle number.
    /// </summary>
    public static FuelType GetFuelType(int nozzleNumber)
    {
        if (NozzleFuelMap.TryGetValue(nozzleNumber, out var fuelType))
            return fuelType;
        throw new ArgumentOutOfRangeException(nameof(nozzleNumber), $"Invalid nozzle number: {nozzleNumber}");
    }

    /// <summary>
    /// Get all nozzle numbers for a given pump ID.
    /// </summary>
    public static int[] GetNozzlesForPump(int pumpId)
    {
        return PumpNozzleMapping.TryGetValue(pumpId, out var nozzles)
            ? nozzles
            : Array.Empty<int>();
    }

    /// <summary>
    /// Get display items for pump dropdown: "Pump 1 : 2, 4, 6"
    /// </summary>
    public static List<PumpDisplayItem> GetPumpDisplayItems()
    {
        return PumpNozzleMapping
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
    public const int TotalPumps = 12;

    /// <summary>
    /// Total number of nozzles.
    /// </summary>
    public const int TotalNozzles = 28;

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
