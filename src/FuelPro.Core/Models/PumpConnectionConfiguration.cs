using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FuelPro.Core.Models;

/// <summary>
/// Connection group size representing the maximum number of pumps in one connection group,
/// including the primary pump.
/// </summary>
public enum ConnectionSize
{
    Two = 2,
    Three = 3,
    Four = 4
}

/// <summary>
/// Standard pump connection modes supported by the dynamic architecture.
/// </summary>
public static class PumpConnectionModes
{
    public const string Off = "OFF";
    public const string TwoPumps = "2_PUMP";
    public const string ThreePumps = "3_PUMP";
    public const string FourPumps = "4_PUMP";

    public static readonly IReadOnlyList<string> AllModes = new[]
    {
        Off,
        TwoPumps,
        ThreePumps,
        FourPumps
    };

    public static int GetMaxConnectedPumps(string mode) => mode switch
    {
        TwoPumps => 1,
        ThreePumps => 2,
        FourPumps => 3,
        _ => 0
    };

    public static int GetMaxGroupSize(string mode) => mode switch
    {
        TwoPumps => 2,
        ThreePumps => 3,
        FourPumps => 4,
        _ => 1
    };

    public static ConnectionSize ModeToConnectionSize(string mode) => mode switch
    {
        ThreePumps => ConnectionSize.Three,
        FourPumps => ConnectionSize.Four,
        _ => ConnectionSize.Two
    };

    public static string ConnectionSizeToMode(ConnectionSize size) => size switch
    {
        ConnectionSize.Three => ThreePumps,
        ConnectionSize.Four => FourPumps,
        _ => TwoPumps
    };
}

/// <summary>
/// Configuration for a single connection group (1 Primary + 1..N Connected Slaves).
/// Concept:
/// Connection Group
///   Primary Pump
///   ├── Connected Pump 1
///   ├── Connected Pump 2
///   └── Connected Pump 3
/// </summary>
public class PumpConnectionGroup
{
    public int GroupId { get; set; } = 1;
    public string GroupName { get; set; } = "Group 1";
    public int PrimaryPumpId { get; set; }
    public List<int> ConnectedPumpIds { get; set; } = new();

    /// <summary>
    /// Returns all pump IDs involved in this group (Primary + Slaves).
    /// </summary>
    [JsonIgnore]
    public IEnumerable<int> AllPumpIds
    {
        get
        {
            if (PrimaryPumpId > 0)
                yield return PrimaryPumpId;

            foreach (var slaveId in ConnectedPumpIds.Where(id => id > 0 && id != PrimaryPumpId))
                yield return slaveId;
        }
    }

    /// <summary>
    /// Returns the connected (slave) pump IDs relative to the given pump ID.
    /// </summary>
    public List<int> GetConnectedPumps(int currentPumpId)
    {
        return AllPumpIds.Where(id => id != currentPumpId).ToList();
    }
}

/// <summary>
/// Backward-compatible alias for PumpConnectionGroup.
/// </summary>
public class PumpConnectionGroupConfig : PumpConnectionGroup
{
}

/// <summary>
/// Station-level configuration for dynamic pump connections.
/// Stored in AppMeta (Key = "Station.PumpConnectionRules") and Setting.PumpConnectionRulesJson.
/// </summary>
public class PumpConnectionConfiguration
{
    /// <summary>
    /// Master switch: OFF / ON. Default: OFF.
    /// When OFF, the entire application behaves in legacy single-pump mode.
    /// </summary>
    public bool IsEnabled { get; set; } = false;

    /// <summary>
    /// Maximum number of pumps in one connection group, including primary pump (2, 3, or 4).
    /// </summary>
    public ConnectionSize ConnectionSize { get; set; } = ConnectionSize.Two;

    /// <summary>
    /// Backward-compatible mode string (OFF, 2_PUMP, 3_PUMP, 4_PUMP).
    /// </summary>
    [JsonIgnore]
    public string Mode
    {
        get => !IsEnabled ? PumpConnectionModes.Off : PumpConnectionModes.ConnectionSizeToMode(ConnectionSize);
        set
        {
            if (string.Equals(value, PumpConnectionModes.Off, StringComparison.OrdinalIgnoreCase))
            {
                IsEnabled = false;
            }
            else
            {
                IsEnabled = true;
                ConnectionSize = PumpConnectionModes.ModeToConnectionSize(value);
            }
        }
    }

    /// <summary>
    /// Configured connection groups.
    /// </summary>
    public List<PumpConnectionGroup> Groups { get; set; } = new();

    /// <summary>
    /// Validates the configuration against business topology rules.
    /// </summary>
    public bool Validate(out List<string> errors)
    {
        errors = new List<string>();

        // Rule 6: If IsEnabled == false, topology should be treated as inactive.
        if (!IsEnabled || Mode == PumpConnectionModes.Off)
        {
            return true;
        }

        int expectedTotalPumps = (int)ConnectionSize;
        int expectedConnectedPumps = expectedTotalPumps - 1;
        var usedPumps = new HashSet<int>();

        for (int i = 0; i < Groups.Count; i++)
        {
            var group = Groups[i];
            var groupLabel = string.IsNullOrWhiteSpace(group.GroupName) ? $"Group {i + 1}" : group.GroupName;

            if (group.PrimaryPumpId <= 0)
            {
                errors.Add($"{groupLabel}: A valid Primary Pump must be selected.");
                continue;
            }

            // Rule 4: A pump should not belong to multiple active connection groups.
            if (usedPumps.Contains(group.PrimaryPumpId))
            {
                errors.Add($"{groupLabel}: Pump {group.PrimaryPumpId} is already assigned to another connection group.");
            }
            else
            {
                usedPumps.Add(group.PrimaryPumpId);
            }

            // Rule 1 & Rule 3: A pump cannot connect to itself / PrimaryPumpId must not appear in ConnectedPumpIds.
            if (group.ConnectedPumpIds.Contains(group.PrimaryPumpId))
            {
                errors.Add($"{groupLabel}: Primary Pump {group.PrimaryPumpId} cannot also be selected as a connected pump.");
            }

            // Rule 2: A pump cannot appear twice inside the same group.
            var duplicateSlaves = group.ConnectedPumpIds
                .GroupBy(id => id)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicateSlaves.Count > 0)
            {
                errors.Add($"{groupLabel}: Duplicate connected pump(s) found: {string.Join(", ", duplicateSlaves)}.");
            }

            // Rule 5: The number of pumps in each group must equal ConnectionSize.
            var distinctSlaves = group.ConnectedPumpIds.Where(id => id > 0 && id != group.PrimaryPumpId).Distinct().ToList();
            int actualGroupPumps = 1 + distinctSlaves.Count;
            if (actualGroupPumps != expectedTotalPumps)
            {
                errors.Add($"{groupLabel}: Has {actualGroupPumps} pump(s) (1 Primary + {distinctSlaves.Count} Connected), but selected ConnectionSize is {expectedTotalPumps} Pumps (must have exactly {expectedConnectedPumps} connected pump(s)).");
            }

            // Rule 4 for slave pumps: A pump should not belong to multiple active connection groups.
            foreach (var slaveId in distinctSlaves)
            {
                if (usedPumps.Contains(slaveId))
                {
                    errors.Add($"{groupLabel}: Pump {slaveId} is already assigned to another connection group.");
                }
                else
                {
                    usedPumps.Add(slaveId);
                }
            }
        }

        return errors.Count == 0;
    }

    /// <summary>
    /// Resolves effective connected pump IDs with strict backward compatibility:
    /// 1. If explicit connectedPumpIds collection is provided, sanitizes, deduplicates, and returns it.
    /// 2. Else if connectedPumpIdsJson exists, parses integer array, deduplicates, and ignores non-positive IDs.
    /// 3. Else if legacy connectedPumpId exists (> 0), treats it as a one-element list.
    /// 4. Else returns an empty list.
    /// </summary>
    public static List<int> ResolveEffectiveConnectedPumpIds(int? legacyConnectedPumpId, string? connectedPumpIdsJson, IEnumerable<int>? connectedPumpIds = null)
    {
        if (connectedPumpIds != null)
        {
            return connectedPumpIds.Where(id => id > 0).Distinct().ToList();
        }

        if (!string.IsNullOrWhiteSpace(connectedPumpIdsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<int>>(connectedPumpIdsJson);
                if (parsed != null)
                {
                    return parsed.Where(id => id > 0).Distinct().ToList();
                }
            }
            catch
            {
                // In case of malformed JSON, fallback to legacy scalar
            }
        }

        if (legacyConnectedPumpId.HasValue && legacyConnectedPumpId.Value > 0)
        {
            return new List<int> { legacyConnectedPumpId.Value };
        }

        return new List<int>();
    }

    /// <summary>
    /// Resolves effective connected pump IDs from an enumerable list.
    /// </summary>
    public static List<int> ResolveEffectiveConnectedPumpIds(IEnumerable<int>? connectedPumpIds)
    {
        return ResolveEffectiveConnectedPumpIds(null, null, connectedPumpIds);
    }

    /// <summary>
    /// Gets the connection group containing the specified pump ID, if any.
    /// </summary>
    public PumpConnectionGroup? GetGroupForPump(int pumpId)
    {
        if (pumpId <= 0 || Groups == null) return null;
        return Groups.FirstOrDefault(g => g.AllPumpIds.Contains(pumpId));
    }
}
