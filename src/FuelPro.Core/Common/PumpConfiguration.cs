using System;
using System.Collections.Generic;
using System.Linq;
using FuelPro.Core.Models;

namespace FuelPro.Core.Common;

/// <summary>
/// Database-driven pump-to-nozzle mapping and fuel type rules.
/// Replaces the hardcoded PumpConfiguration dictionaries.
/// </summary>
public static class PumpConfiguration
{
    public static readonly DateTime Legacy4PumpCutoffDate = new(2026, 6, 14);
    public static readonly DateTime Legacy22PumpCutoffDate = new(2026, 6, 21);

    /// <summary>
    /// Operational Pump ID → list of nozzle numbers assigned to that pump.
    /// Dynamically populated from Database.
    /// </summary>
    public static readonly Dictionary<int, int[]> PumpNozzleMapping = new();

    /// <summary>
    /// Historical Pump ID → list of nozzle numbers assigned to that pump (legacy 4-pump).
    /// </summary>
    private static readonly Dictionary<int, int[]> HistoricalPumpNozzleMapping = new()
    {
        { 1, new[] { 1, 2, 3, 4 } },
        { 2, new[] { 5, 6, 7, 8, 9, 10 } },
        { 3, new[] { 11, 12, 13, 14, 15, 16 } },
        { 4, new[] { 17, 18, 19, 20 } }
    };

    private static readonly Dictionary<(int PumpId, int NozzleNumber), FuelType> NozzleFuelMap = new();
    private static readonly Dictionary<(int PumpId, int NozzleNumber), string> NozzleTankMap = new();

    /// <summary>
    /// Historical (PumpId, NozzleNumber) to FuelType mapping for legacy 4-pump layout.
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
    /// Legacy 22-pump layout nozzle mappings.
    /// </summary>
    private static readonly Dictionary<int, int[]> Legacy22PumpNozzleMapping = new();
    private static readonly Dictionary<(int PumpId, int NozzleNumber), FuelType> Legacy22NozzleFuelMap = new();

    /// <summary>
    /// Expose all defined nozzles.
    /// </summary>
    public static List<(int PumpId, int NozzleNumber)> AllNozzles => NozzleFuelMap.Keys.ToList();

    static PumpConfiguration()
    {
        // 1. Initialize Legacy 22-pump mappings for historical retrieval
        int nozzle22 = 1;
        for (int pump = 1; pump <= 8; pump++)
        {
            Legacy22PumpNozzleMapping[pump] = new[] { nozzle22, nozzle22 + 1, nozzle22 + 2 };
            Legacy22NozzleFuelMap[(pump, nozzle22++)] = FuelType.MS_I;
            Legacy22NozzleFuelMap[(pump, nozzle22++)] = FuelType.MS_II;
            Legacy22NozzleFuelMap[(pump, nozzle22++)] = FuelType.HSD;
        }
        for (int pump = 9; pump <= 10; pump++)
        {
            Legacy22PumpNozzleMapping[pump] = new[] { nozzle22, nozzle22 + 1 };
            Legacy22NozzleFuelMap[(pump, nozzle22++)] = FuelType.MS_I;
            Legacy22NozzleFuelMap[(pump, nozzle22++)] = FuelType.MS_II;
        }
        for (int pump = 11; pump <= 12; pump++)
        {
            Legacy22PumpNozzleMapping[pump] = new[] { nozzle22, nozzle22 + 1 };
            Legacy22NozzleFuelMap[(pump, nozzle22++)] = FuelType.MS_I;
            Legacy22NozzleFuelMap[(pump, nozzle22++)] = FuelType.MS_II;
        }
        for (int pump = 13; pump <= 16; pump++)
        {
            Legacy22PumpNozzleMapping[pump] = new[] { nozzle22, nozzle22 + 1 };
            Legacy22NozzleFuelMap[(pump, nozzle22++)] = FuelType.MS_I;
            Legacy22NozzleFuelMap[(pump, nozzle22++)] = FuelType.MS_II;
        }
        for (int pump = 17; pump <= 18; pump++)
        {
            Legacy22PumpNozzleMapping[pump] = new[] { nozzle22, nozzle22 + 1 };
            Legacy22NozzleFuelMap[(pump, nozzle22++)] = FuelType.MS_I;
            Legacy22NozzleFuelMap[(pump, nozzle22++)] = FuelType.HSD;
        }
        for (int pump = 19; pump <= 22; pump++)
        {
            Legacy22PumpNozzleMapping[pump] = new[] { nozzle22 };
            Legacy22NozzleFuelMap[(pump, nozzle22++)] = FuelType.CNG;
        }

        // 2. Set up default 6-pump mappings as active fallback
        var defaultMappings = new List<PumpMapping>();
        var now = DateTime.Now;

        // Pump 1: Nozzle 1 (MS-I/Petrol, MS - 20KL), Nozzle 3 (HSD/Diesel, HSD - 20KL)
        defaultMappings.Add(new PumpMapping { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", TankName = "MS - 20KL", CreatedAt = now });
        defaultMappings.Add(new PumpMapping { PumpId = 1, NozzleNumber = 3, FuelType = "HSD", TankName = "HSD - 20KL", CreatedAt = now });

        // Pump 2: Nozzle 2 (MS-I/Petrol, MS - 20KL), Nozzle 4 (HSD/Diesel, HSD - 20KL)
        defaultMappings.Add(new PumpMapping { PumpId = 2, NozzleNumber = 2, FuelType = "MS-I", TankName = "MS - 20KL", CreatedAt = now });
        defaultMappings.Add(new PumpMapping { PumpId = 2, NozzleNumber = 4, FuelType = "HSD", TankName = "HSD - 20KL", CreatedAt = now });

        // Pump 3: Nozzle 5 (MS-I/Petrol, MS - 20KL), Nozzle 7 (MS-II/Diesel, HSD - 20KL II)
        defaultMappings.Add(new PumpMapping { PumpId = 3, NozzleNumber = 5, FuelType = "MS-I", TankName = "MS - 20KL", CreatedAt = now });
        defaultMappings.Add(new PumpMapping { PumpId = 3, NozzleNumber = 7, FuelType = "MS-II", TankName = "HSD - 20KL II", CreatedAt = now });

        // Pump 4: Nozzle 6 (MS-I/Petrol, MS - 20KL), Nozzle 8 (MS-II/Diesel, HSD - 20KL II)
        defaultMappings.Add(new PumpMapping { PumpId = 4, NozzleNumber = 6, FuelType = "MS-I", TankName = "MS - 20KL", CreatedAt = now });
        defaultMappings.Add(new PumpMapping { PumpId = 4, NozzleNumber = 8, FuelType = "MS-II", TankName = "HSD - 20KL II", CreatedAt = now });

        // Pump 5: Nozzle 9 (MS-I/Petrol, MS - 20KL), Nozzle 11 (HSD/Diesel, HSD - 20KL)
        defaultMappings.Add(new PumpMapping { PumpId = 5, NozzleNumber = 9, FuelType = "MS-I", TankName = "MS - 20KL", CreatedAt = now });
        defaultMappings.Add(new PumpMapping { PumpId = 5, NozzleNumber = 11, FuelType = "HSD", TankName = "HSD - 20KL", CreatedAt = now });

        // Pump 6: Nozzle 10 (MS-I/Petrol, MS - 20KL), Nozzle 12 (HSD/Diesel, HSD - 20KL)
        defaultMappings.Add(new PumpMapping { PumpId = 6, NozzleNumber = 10, FuelType = "MS-I", TankName = "MS - 20KL", CreatedAt = now });
        defaultMappings.Add(new PumpMapping { PumpId = 6, NozzleNumber = 12, FuelType = "HSD", TankName = "HSD - 20KL", CreatedAt = now });

        InitializeFromDb(defaultMappings);
    }

    public static void InitializeFromDb(List<PumpMapping> mappings)
    {
        var active = mappings.Where(m => m.IsActive).ToList();
        
        PumpNozzleMapping.Clear();
        NozzleFuelMap.Clear();
        NozzleTankMap.Clear();

        foreach (var m in active)
        {
            var cleanFuelType = m.FuelType.Replace("-", "_");
            if (Enum.TryParse<FuelType>(cleanFuelType, out var fuelType))
            {
                NozzleFuelMap[(m.PumpId, m.NozzleNumber)] = fuelType;
            }
            NozzleTankMap[(m.PumpId, m.NozzleNumber)] = m.TankName;

            if (!PumpNozzleMapping.ContainsKey(m.PumpId))
            {
                PumpNozzleMapping[m.PumpId] = new[] { m.NozzleNumber };
            }
            else
            {
                var list = PumpNozzleMapping[m.PumpId].ToList();
                list.Add(m.NozzleNumber);
                PumpNozzleMapping[m.PumpId] = list.OrderBy(n => n).ToArray();
            }
        }

        TotalPumps = PumpNozzleMapping.Keys.DefaultIfEmpty(0).Max();
        TotalNozzles = active.Count;
    }

    public static string GetTankName(int pumpId, int nozzleNumber, DateTime? date = null)
    {
        var actualPumpId = GetPumpIdForNozzle(nozzleNumber, date);
        if (actualPumpId == 0)
        {
            actualPumpId = pumpId;
        }

        if (NozzleTankMap.TryGetValue((actualPumpId, nozzleNumber), out var tankName))
            return tankName;

        var fuelType = GetFuelType(actualPumpId, nozzleNumber, date);
        return fuelType.ToTankName();
    }

    public static string GetTestingTankCategory(string fuelTypeField, int pumpId, DateTime date)
    {
        if (string.IsNullOrWhiteSpace(fuelTypeField)) return "MS";

        if (int.TryParse(fuelTypeField, out var nozzleNumber))
        {
            var ft = GetFuelType(pumpId, nozzleNumber, date);
            if (ft == FuelType.MS_I) return "MS";
            if (ft == FuelType.MS_II) return "HSD-II";
            if (ft == FuelType.HSD) return "HSD";
            if (ft == FuelType.CNG) return "CNG";

            var tankName = (GetTankName(pumpId, nozzleNumber, date) ?? "").ToUpperInvariant();
            if (tankName.Contains("MS-II") || tankName.Contains("20KL II") || tankName.Contains("HSD-II") || tankName.Contains("TANK 3")) return "HSD-II";
            if (tankName.Contains("MS") || tankName.Contains("PETROL") || tankName.Contains("TANK 1")) return "MS";
            if (tankName.Contains("HSD") || tankName.Contains("DIESEL") || tankName.Contains("TANK 2")) return "HSD";
            if (tankName.Contains("CNG")) return "CNG";
        }

        var str = fuelTypeField.Trim().ToUpperInvariant();
        if (str == "MS-II" || str.Contains("HSD-II") || str.Contains("HSD II") || str.Contains("20KL II") || str.Contains("MS-II")) return "HSD-II";
        if (str == "MS-I" || str == "MS" || str.StartsWith("MS") || str.Contains("PETROL")) return "MS";
        if (str == "HSD" || str.StartsWith("HSD") || str.Contains("DIESEL")) return "HSD";
        if (str == "CNG" || str.StartsWith("CNG")) return "CNG";

        return fuelTypeField;
    }

    public static int GetPumpIdForNozzle(int nozzleNumber, DateTime? date = null)
    {
        if (date.HasValue)
        {
            if (date.Value.Date < Legacy4PumpCutoffDate)
            {
                var match = HistoricalNozzleFuelMap.Keys.FirstOrDefault(k => k.NozzleNumber == nozzleNumber);
                return match != default ? match.PumpId : 0;
            }
            else if (date.Value.Date < Legacy22PumpCutoffDate)
            {
                var match = Legacy22NozzleFuelMap.Keys.FirstOrDefault(k => k.NozzleNumber == nozzleNumber);
                return match != default ? match.PumpId : 0;
            }
        }

        var activeMatch = NozzleFuelMap.Keys.FirstOrDefault(k => k.NozzleNumber == nozzleNumber);
        if (activeMatch != default)
            return activeMatch.PumpId;

        // Try falling back to legacy 22-pump then 4-pump
        var legacy22Match = Legacy22NozzleFuelMap.Keys.FirstOrDefault(k => k.NozzleNumber == nozzleNumber);
        if (legacy22Match != default)
            return legacy22Match.PumpId;

        var histMatch = HistoricalNozzleFuelMap.Keys.FirstOrDefault(k => k.NozzleNumber == nozzleNumber);
        return histMatch != default ? histMatch.PumpId : 0;
    }

    public static FuelType GetFuelType(int pumpId, int nozzleNumber, DateTime? date = null)
    {
        var actualPumpId = GetPumpIdForNozzle(nozzleNumber, date);
        if (actualPumpId == 0)
        {
            actualPumpId = pumpId;
        }

        if (date.HasValue)
        {
            if (date.Value.Date < Legacy4PumpCutoffDate)
            {
                if (HistoricalNozzleFuelMap.TryGetValue((actualPumpId, nozzleNumber), out var historicalFuelType))
                    return historicalFuelType;
            }
            else if (date.Value.Date < Legacy22PumpCutoffDate)
            {
                if (Legacy22NozzleFuelMap.TryGetValue((actualPumpId, nozzleNumber), out var legacy22FuelType))
                    return legacy22FuelType;
            }
        }

        if (NozzleFuelMap.TryGetValue((actualPumpId, nozzleNumber), out var fuelType))
            return fuelType;

        if (Legacy22NozzleFuelMap.TryGetValue((actualPumpId, nozzleNumber), out var fallback22Type))
            return fallback22Type;

        if (HistoricalNozzleFuelMap.TryGetValue((actualPumpId, nozzleNumber), out var fallback4Type))
            return fallback4Type;

        return FuelType.MS_I;
    }

    /// <summary>
    /// Returns the canonical display name ("HSD", "MS-I", "MS-II", "CNG") for a pump ID and nozzle number.
    /// </summary>
    public static string GetFuelTypeDisplayName(int pumpId, int nozzleNumber, DateTime? date = null)
        => GetFuelType(pumpId, nozzleNumber, date).ToDisplayName();

    /// <summary>
    /// Get all nozzle numbers for a given pump ID.
    /// </summary>
    public static int[] GetNozzlesForPump(int pumpId, DateTime? date = null)
    {
        if (date.HasValue)
        {
            if (date.Value.Date < Legacy4PumpCutoffDate)
            {
                return HistoricalPumpNozzleMapping.TryGetValue(pumpId, out var nozzles) ? nozzles : Array.Empty<int>();
            }
            else if (date.Value.Date < Legacy22PumpCutoffDate)
            {
                return Legacy22PumpNozzleMapping.TryGetValue(pumpId, out var nozzles) ? nozzles : Array.Empty<int>();
            }
        }

        return PumpNozzleMapping.TryGetValue(pumpId, out var nozzlesActive) ? nozzlesActive : Array.Empty<int>();
    }

    /// <summary>
    /// Get display items for pump dropdown: "Pump 1 : 1, 2, 3"
    /// </summary>
    public static List<PumpDisplayItem> GetPumpDisplayItems(DateTime? date = null)
    {
        var map = PumpNozzleMapping;
        if (date.HasValue)
        {
            if (date.Value.Date < Legacy4PumpCutoffDate)
                map = HistoricalPumpNozzleMapping;
            else if (date.Value.Date < Legacy22PumpCutoffDate)
                map = Legacy22PumpNozzleMapping;
        }
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
    public static int TotalPumps { get; private set; } = 6;

    /// <summary>
    /// Total number of nozzles.
    /// </summary>
    public static int TotalNozzles { get; private set; } = 12;

    /// <summary>
    /// Denomination values for cash counting.
    /// </summary>
    public static readonly int[] Denominations = { 500, 200, 100, 50, 20, 10 };

    /// <summary>
    /// Tank configuration.
    /// </summary>
    public static readonly Dictionary<string, string> Tanks = new()
    {
        { "MS - 20KL", "MS" },
        { "HSD - 20KL", "HSD" },
        { "HSD - 20KL II", "HSD" }
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
