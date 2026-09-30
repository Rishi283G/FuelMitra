using System.Collections.Generic;
using System.Text.Json;
using FuelPro.Core.Models;
using Xunit;

namespace FuelPro.Tests;

public class PumpConnectionConfigurationTests
{
    /// <summary>
    /// TEST 1: Connections disabled → valid.
    /// </summary>
    [Fact]
    public void Test1_ConnectionsDisabled_IsValid()
    {
        var config = new PumpConnectionConfiguration
        {
            IsEnabled = false,
            ConnectionSize = ConnectionSize.Two,
            Groups = new List<PumpConnectionGroup>
            {
                // Even with empty or unconfigured groups, disabled topology must be valid
                new() { GroupId = 1, GroupName = "Group 1", PrimaryPumpId = 0, ConnectedPumpIds = new List<int>() }
            }
        };

        bool isValid = config.Validate(out var errors);

        Assert.True(isValid);
        Assert.Empty(errors);
    }

    /// <summary>
    /// TEST 2: 2-pump group: Primary 1 + Connected [2] → valid.
    /// </summary>
    [Fact]
    public void Test2_TwoPumpGroup_Primary1_Connected2_IsValid()
    {
        var config = new PumpConnectionConfiguration
        {
            IsEnabled = true,
            ConnectionSize = ConnectionSize.Two,
            Groups = new List<PumpConnectionGroup>
            {
                new()
                {
                    GroupId = 1,
                    GroupName = "Group 1",
                    PrimaryPumpId = 1,
                    ConnectedPumpIds = new List<int> { 2 }
                }
            }
        };

        bool isValid = config.Validate(out var errors);

        Assert.True(isValid);
        Assert.Empty(errors);
        Assert.Equal(PumpConnectionModes.TwoPumps, config.Mode);
    }

    /// <summary>
    /// TEST 3: 3-pump group: Primary 1 + Connected [2,3] → valid.
    /// </summary>
    [Fact]
    public void Test3_ThreePumpGroup_Primary1_Connected2And3_IsValid()
    {
        var config = new PumpConnectionConfiguration
        {
            IsEnabled = true,
            ConnectionSize = ConnectionSize.Three,
            Groups = new List<PumpConnectionGroup>
            {
                new()
                {
                    GroupId = 1,
                    GroupName = "Group 1",
                    PrimaryPumpId = 1,
                    ConnectedPumpIds = new List<int> { 2, 3 }
                }
            }
        };

        bool isValid = config.Validate(out var errors);

        Assert.True(isValid);
        Assert.Empty(errors);
        Assert.Equal(PumpConnectionModes.ThreePumps, config.Mode);
    }

    /// <summary>
    /// TEST 4: 4-pump group: Primary 1 + Connected [2,3,4] → valid.
    /// </summary>
    [Fact]
    public void Test4_FourPumpGroup_Primary1_Connected2And3And4_IsValid()
    {
        var config = new PumpConnectionConfiguration
        {
            IsEnabled = true,
            ConnectionSize = ConnectionSize.Four,
            Groups = new List<PumpConnectionGroup>
            {
                new()
                {
                    GroupId = 1,
                    GroupName = "Group 1",
                    PrimaryPumpId = 1,
                    ConnectedPumpIds = new List<int> { 2, 3, 4 }
                }
            }
        };

        bool isValid = config.Validate(out var errors);

        Assert.True(isValid);
        Assert.Empty(errors);
        Assert.Equal(PumpConnectionModes.FourPumps, config.Mode);
    }

    /// <summary>
    /// TEST 5: Duplicate pump in same group → invalid.
    /// </summary>
    [Fact]
    public void Test5_DuplicatePumpInSameGroup_IsInvalid()
    {
        var config = new PumpConnectionConfiguration
        {
            IsEnabled = true,
            ConnectionSize = ConnectionSize.Three,
            Groups = new List<PumpConnectionGroup>
            {
                new()
                {
                    GroupId = 1,
                    GroupName = "Group 1",
                    PrimaryPumpId = 1,
                    ConnectedPumpIds = new List<int> { 2, 2 } // Duplicate connected pump
                }
            }
        };

        bool isValid = config.Validate(out var errors);

        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("Duplicate connected pump(s) found"));
    }

    /// <summary>
    /// TEST 6: Primary pump also present in connected list → invalid.
    /// </summary>
    [Fact]
    public void Test6_PrimaryPumpAlsoPresentInConnectedList_IsInvalid()
    {
        var config = new PumpConnectionConfiguration
        {
            IsEnabled = true,
            ConnectionSize = ConnectionSize.Two,
            Groups = new List<PumpConnectionGroup>
            {
                new()
                {
                    GroupId = 1,
                    GroupName = "Group 1",
                    PrimaryPumpId = 1,
                    ConnectedPumpIds = new List<int> { 1 } // Primary pump present in connected list
                }
            }
        };

        bool isValid = config.Validate(out var errors);

        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("cannot also be selected as a connected pump"));
    }

    /// <summary>
    /// TEST 7: Same pump used in two groups → invalid.
    /// </summary>
    [Fact]
    public void Test7_SamePumpUsedInTwoGroups_IsInvalid()
    {
        var config = new PumpConnectionConfiguration
        {
            IsEnabled = true,
            ConnectionSize = ConnectionSize.Two,
            Groups = new List<PumpConnectionGroup>
            {
                new()
                {
                    GroupId = 1,
                    GroupName = "Group 1",
                    PrimaryPumpId = 1,
                    ConnectedPumpIds = new List<int> { 2 }
                },
                new()
                {
                    GroupId = 2,
                    GroupName = "Group 2",
                    PrimaryPumpId = 2, // Pump 2 collision!
                    ConnectedPumpIds = new List<int> { 3 }
                }
            }
        };

        bool isValid = config.Validate(out var errors);

        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("Pump 2 is already assigned to another connection group"));
    }

    /// <summary>
    /// TEST 8: Wrong number of pumps for selected ConnectionSize → invalid.
    /// </summary>
    [Theory]
    [InlineData(ConnectionSize.Two, 0)] // Needs 1 connected, got 0 (total 1 != 2)
    [InlineData(ConnectionSize.Two, 2)] // Needs 1 connected, got 2 (total 3 != 2)
    [InlineData(ConnectionSize.Three, 1)] // Needs 2 connected, got 1 (total 2 != 3)
    [InlineData(ConnectionSize.Three, 3)] // Needs 2 connected, got 3 (total 4 != 3)
    [InlineData(ConnectionSize.Four, 2)] // Needs 3 connected, got 2 (total 3 != 4)
    [InlineData(ConnectionSize.Four, 4)] // Needs 3 connected, got 4 (total 5 != 4)
    public void Test8_WrongNumberOfPumpsForSelectedConnectionSize_IsInvalid(ConnectionSize size, int connectedCount)
    {
        var slaves = new List<int>();
        for (int i = 2; i < 2 + connectedCount; i++)
        {
            slaves.Add(i);
        }

        var config = new PumpConnectionConfiguration
        {
            IsEnabled = true,
            ConnectionSize = size,
            Groups = new List<PumpConnectionGroup>
            {
                new()
                {
                    GroupId = 1,
                    GroupName = "Group 1",
                    PrimaryPumpId = 1,
                    ConnectedPumpIds = slaves
                }
            }
        };

        bool isValid = config.Validate(out var errors);

        Assert.False(isValid);
        Assert.Contains(errors, e => e.Contains("selected ConnectionSize is"));
    }

    /// <summary>
    /// TEST 9: Legacy ConnectedPumpId configuration remains readable.
    /// </summary>
    [Fact]
    public void Test9_LegacyConnectedPumpIdConfiguration_RemainsReadable()
    {
        // When JSON is null or empty, legacy scalar ConnectedPumpId must resolve correctly
        int? legacyConnectedPumpId = 2;
        string? json = null;

        var resolved = PumpConnectionConfiguration.ResolveEffectiveConnectedPumpIds(legacyConnectedPumpId, json);

        Assert.Single(resolved);
        Assert.Equal(2, resolved[0]);

        // When JSON is present and valid, JSON takes precedence
        string multiJson = "[2, 3, 4]";
        var resolvedMulti = PumpConnectionConfiguration.ResolveEffectiveConnectedPumpIds(legacyConnectedPumpId, multiJson);

        Assert.Equal(3, resolvedMulti.Count);
        Assert.Equal(new List<int> { 2, 3, 4 }, resolvedMulti);
    }

    /// <summary>
    /// TEST 10: No configuration on an existing installation defaults to connections disabled.
    /// </summary>
    [Fact]
    public void Test10_NoConfigurationOnExistingInstallation_DefaultsToConnectionsDisabled()
    {
        var config = new PumpConnectionConfiguration();

        Assert.False(config.IsEnabled);
        Assert.Equal(PumpConnectionModes.Off, config.Mode);
        Assert.Empty(config.Groups);
        Assert.True(config.Validate(out var errors));
        Assert.Empty(errors);
    }

    /// <summary>
    /// Verifies JSON serialization format matches the exact specification for 4 pumps.
    /// </summary>
    [Fact]
    public void JsonSerialization_Matches4PumpSpec()
    {
        var config = new PumpConnectionConfiguration
        {
            IsEnabled = true,
            ConnectionSize = ConnectionSize.Four,
            Groups = new List<PumpConnectionGroup>
            {
                new()
                {
                    PrimaryPumpId = 1,
                    ConnectedPumpIds = new List<int> { 2, 3, 4 }
                }
            }
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        var deserialized = JsonSerializer.Deserialize<PumpConnectionConfiguration>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.IsEnabled);
        Assert.Equal(ConnectionSize.Four, deserialized.ConnectionSize);
        Assert.Equal(PumpConnectionModes.FourPumps, deserialized.Mode);
        Assert.Single(deserialized.Groups);
        Assert.Equal(1, deserialized.Groups[0].PrimaryPumpId);
        Assert.Equal(new List<int> { 2, 3, 4 }, deserialized.Groups[0].ConnectedPumpIds);
    }

    /// <summary>
    /// Verifies JSON deserialization parses sample payload from user prompt.
    /// </summary>
    [Fact]
    public void JsonDeserialization_ParsesSamplePayload()
    {
        string json = """
        {
          "IsEnabled": true,
          "ConnectionSize": 4,
          "Groups": [
            {
              "PrimaryPumpId": 1,
              "ConnectedPumpIds": [2, 3, 4]
            }
          ]
        }
        """;

        var config = JsonSerializer.Deserialize<PumpConnectionConfiguration>(json);

        Assert.NotNull(config);
        Assert.True(config.IsEnabled);
        Assert.Equal(ConnectionSize.Four, config.ConnectionSize);
        Assert.Equal(PumpConnectionModes.FourPumps, config.Mode);
        Assert.Single(config.Groups);
        Assert.Equal(1, config.Groups[0].PrimaryPumpId);
        Assert.Equal(new List<int> { 2, 3, 4 }, config.Groups[0].ConnectedPumpIds);
        Assert.True(config.Validate(out var errors));
        Assert.Empty(errors);
    }
}
