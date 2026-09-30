using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Services;

namespace FuelPro.Tests;

public class DeveloperPumpMappingPersistenceTests
{
    private (IServiceProvider ServiceProvider, DbContextOptions<FuelProDbContext> Options) CreateTestServices()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddTransient<FuelProDbContext>(sp => new FuelProDbContext(options));
        services.AddTransient<IStationConfigurationService, StationConfigurationService>();

        var sp = services.BuildServiceProvider();
        return (sp, options);
    }

    [Fact]
    public async Task SavePumpMappingsAsync_PersistsToDbAndUpdatesSettingJson()
    {
        var (sp, options) = CreateTestServices();
        var stationConfig = sp.GetRequiredService<IStationConfigurationService>();

        // Pre-populate default setting
        using (var db = new FuelProDbContext(options))
        {
            db.Settings.Add(new Setting
            {
                PumpStationName = "Kandhare Petroleum",
                LastUpdated = DateTime.Now
            });
            await db.SaveChangesAsync();
        }

        // Configure 4-pump layout (nozzles 1..8)
        var customLayout = new List<PumpMapping>
        {
            new() { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true },
            new() { PumpId = 1, NozzleNumber = 2, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true },
            new() { PumpId = 1, NozzleNumber = 3, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true },
            new() { PumpId = 2, NozzleNumber = 4, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true },
            new() { PumpId = 2, NozzleNumber = 5, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true },
            new() { PumpId = 2, NozzleNumber = 6, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true },
            new() { PumpId = 3, NozzleNumber = 7, FuelType = "HSD", TankName = "HSD - 20KL II", IsActive = true },
            new() { PumpId = 4, NozzleNumber = 8, FuelType = "HSD", TankName = "HSD - 20KL II", IsActive = true }
        };

        var saveSuccess = await stationConfig.SavePumpMappingsAsync(customLayout);
        Assert.True(saveSuccess);

        // Verify local DB mappings
        var loadedMappings = await stationConfig.GetAllPumpMappingsAsync();
        Assert.Equal(8, loadedMappings.Count);
        Assert.Equal(4, loadedMappings.Select(m => m.PumpId).Distinct().Count());

        // Verify PumpConfiguration in-memory mapping was updated
        Assert.True(PumpConfiguration.PumpNozzleMapping.ContainsKey(1));
        Assert.True(PumpConfiguration.PumpNozzleMapping.ContainsKey(2));
        Assert.True(PumpConfiguration.PumpNozzleMapping.ContainsKey(3));
        Assert.True(PumpConfiguration.PumpNozzleMapping.ContainsKey(4));
        Assert.False(PumpConfiguration.PumpNozzleMapping.ContainsKey(5));
        Assert.False(PumpConfiguration.PumpNozzleMapping.ContainsKey(6));

        // Verify Setting.PumpMappingsJson
        using (var db = new FuelProDbContext(options))
        {
            var setting = await db.Settings.FirstOrDefaultAsync();
            Assert.NotNull(setting);
            Assert.False(string.IsNullOrWhiteSpace(setting!.PumpMappingsJson));
            Assert.Contains("NozzleNumber", setting.PumpMappingsJson);
        }
    }

    [Fact]
    public async Task SavePumpMappingsAsync_IntelligentUpsert_RetainsExistingEntityIds()
    {
        var (sp, options) = CreateTestServices();
        var stationConfig = sp.GetRequiredService<IStationConfigurationService>();

        // Pre-populate setting and 2 initial pump mappings
        int existingId1;
        using (var db = new FuelProDbContext(options))
        {
            db.Settings.Add(new Setting { PumpStationName = "Test Station" });
            var p1 = new PumpMapping { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = DateTime.Now };
            var p2 = new PumpMapping { PumpId = 1, NozzleNumber = 2, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true, CreatedAt = DateTime.Now };
            db.PumpMappings.AddRange(p1, p2);
            await db.SaveChangesAsync();
            existingId1 = p1.PumpMappingId;
        }

        // Update Nozzle 1 tank and add Nozzle 3
        var updatedList = new List<PumpMapping>
        {
            new() { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", TankName = "Updated Tank", IsActive = true },
            new() { PumpId = 1, NozzleNumber = 2, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true },
            new() { PumpId = 2, NozzleNumber = 3, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true }
        };

        var success = await stationConfig.SavePumpMappingsAsync(updatedList);
        Assert.True(success);

        using (var db = new FuelProDbContext(options))
        {
            var all = await db.PumpMappings.OrderBy(p => p.NozzleNumber).ToListAsync();
            Assert.Equal(3, all.Count);
            // Nozzle 1 retained its original ID
            Assert.Equal(existingId1, all[0].PumpMappingId);
            Assert.Equal("Updated Tank", all[0].TankName);
        }
    }

    [Fact]
    public async Task SavePumpMappingsAsync_RemovesObsoletePumps()
    {
        var (sp, options) = CreateTestServices();
        var stationConfig = sp.GetRequiredService<IStationConfigurationService>();

        using (var db = new FuelProDbContext(options))
        {
            db.Settings.Add(new Setting { PumpStationName = "Test Station" });
            // 6 pumps seeded
            for (int i = 1; i <= 6; i++)
            {
                db.PumpMappings.Add(new PumpMapping { PumpId = i, NozzleNumber = i, FuelType = "MS-I", TankName = "Tank 1", IsActive = true, CreatedAt = DateTime.Now });
            }
            await db.SaveChangesAsync();
        }

        // Reconfigure to 3 pumps only
        var threePumpLayout = new List<PumpMapping>
        {
            new() { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", TankName = "Tank 1", IsActive = true },
            new() { PumpId = 2, NozzleNumber = 2, FuelType = "MS-I", TankName = "Tank 1", IsActive = true },
            new() { PumpId = 3, NozzleNumber = 3, FuelType = "MS-I", TankName = "Tank 1", IsActive = true }
        };

        await stationConfig.SavePumpMappingsAsync(threePumpLayout);

        var mappings = await stationConfig.GetAllPumpMappingsAsync();
        Assert.Equal(3, mappings.Count);
        Assert.DoesNotContain(mappings, m => m.PumpId == 4 || m.PumpId == 5 || m.PumpId == 6);
    }
}
