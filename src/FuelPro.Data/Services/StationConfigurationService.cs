using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FuelPro.Data.Services;

public class StationConfigurationService : IStationConfigurationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger = Log.ForContext<StationConfigurationService>();

    public event Action? StationConfigurationChanged;

    public StationConfigurationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<List<PumpMapping>> GetAllPumpMappingsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            return await db.PumpMappings
                .OrderBy(m => m.PumpId)
                .ThenBy(m => m.NozzleNumber)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load pump mappings");
            return new List<PumpMapping>();
        }
    }

    public async Task<bool> SavePumpMappingsAsync(IEnumerable<PumpMapping> mappings)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

            var incomingList = mappings.ToList();
            var existingAll = await db.PumpMappings.ToListAsync();

            var processedIds = new HashSet<int>();

            foreach (var item in incomingList)
            {
                PumpMapping? match = null;
                if (item.PumpMappingId > 0)
                {
                    match = existingAll.FirstOrDefault(m => m.PumpMappingId == item.PumpMappingId && !processedIds.Contains(m.PumpMappingId));
                }
                if (match == null)
                {
                    match = existingAll.FirstOrDefault(m => m.PumpId == item.PumpId && m.NozzleNumber == item.NozzleNumber && !processedIds.Contains(m.PumpMappingId));
                }
                if (match == null)
                {
                    match = existingAll.FirstOrDefault(m => m.NozzleNumber == item.NozzleNumber && !processedIds.Contains(m.PumpMappingId));
                }

                if (match != null)
                {
                    match.PumpId = item.PumpId;
                    match.NozzleNumber = item.NozzleNumber;
                    match.FuelType = string.IsNullOrWhiteSpace(item.FuelType) ? "MS-I" : item.FuelType.Trim();
                    match.TankName = item.TankName ?? "";
                    match.IsActive = true;
                    db.Entry(match).State = EntityState.Modified;
                    processedIds.Add(match.PumpMappingId);
                    item.PumpMappingId = match.PumpMappingId;
                }
                else
                {
                    var newEntity = new PumpMapping
                    {
                        PumpId = item.PumpId,
                        NozzleNumber = item.NozzleNumber,
                        FuelType = string.IsNullOrWhiteSpace(item.FuelType) ? "MS-I" : item.FuelType.Trim(),
                        TankName = item.TankName ?? "",
                        IsActive = true,
                        CreatedAt = item.CreatedAt != default ? item.CreatedAt : DateTime.Now
                    };
                    db.PumpMappings.Add(newEntity);
                }
            }

            // Remove any obsolete mappings that are no longer part of the station layout
            foreach (var existing in existingAll)
            {
                if (!processedIds.Contains(existing.PumpMappingId))
                {
                    db.PumpMappings.Remove(existing);
                }
            }

            await db.SaveChangesAsync();

            // Re-initialize PumpConfiguration in-memory mapping
            var allActive = await db.PumpMappings
                .OrderBy(m => m.PumpId)
                .ThenBy(m => m.NozzleNumber)
                .ToListAsync();

            // Update Setting.PumpMappingsJson so Settings sync propagates active layout to cloud
            var setting = await db.Settings.FirstOrDefaultAsync();
            if (setting != null)
            {
                setting.PumpMappingsJson = System.Text.Json.JsonSerializer.Serialize(allActive);
                setting.LastUpdated = DateTime.Now;
                db.Entry(setting).State = EntityState.Modified;
                await db.SaveChangesAsync();
            }

            PumpConfiguration.InitializeFromDb(allActive);

            StationConfigurationChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save pump mappings");
            return false;
        }
    }

    public async Task<bool> DeletePumpMappingAsync(int pumpMappingId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var item = await db.PumpMappings.FindAsync(pumpMappingId);
            if (item != null)
            {
                db.PumpMappings.Remove(item);
                await db.SaveChangesAsync();

                var allActive = await db.PumpMappings.ToListAsync();
                PumpConfiguration.InitializeFromDb(allActive);

                StationConfigurationChanged?.Invoke();
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete pump mapping {Id}", pumpMappingId);
            return false;
        }
    }

    public async Task<List<TankDefinition>> GetAllTanksAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            return await db.TankDefinitions
                .OrderBy(t => t.TankName)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load tanks");
            return new List<TankDefinition>();
        }
    }

    public async Task<bool> SaveTankAsync(TankDefinition tank)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

            TankDefinition? existing = null;
            if (tank.TankId > 0)
            {
                existing = await db.TankDefinitions.FindAsync(tank.TankId);
            }
            if (existing == null && !string.IsNullOrWhiteSpace(tank.TankName))
            {
                existing = await db.TankDefinitions.FirstOrDefaultAsync(x => x.TankName.Trim().ToLower() == tank.TankName.Trim().ToLower());
            }

            if (existing != null)
            {
                existing.TankName = tank.TankName.Trim();
                existing.CapacityKL = tank.CapacityKL;
                existing.FuelType = tank.FuelType;
                existing.IsActive = tank.IsActive;
                existing.HasTesting = tank.HasTesting;
                db.Entry(existing).State = EntityState.Modified;
                await db.SaveChangesAsync();
                tank.TankId = existing.TankId;
            }
            else
            {
                tank.CreatedAt = DateTime.Now;
                tank.TankName = tank.TankName.Trim();
                db.TankDefinitions.Add(tank);
                await db.SaveChangesAsync();
            }

            var allActiveTanks = await db.TankDefinitions.ToListAsync();
            PumpConfiguration.InitializeTanksFromDb(allActiveTanks);

            // Sync updated tanks to Settings.TankDefinitionsJson
            try
            {
                var setting = await db.Settings.FirstOrDefaultAsync();
                if (setting != null)
                {
                    setting.TankDefinitionsJson = System.Text.Json.JsonSerializer.Serialize(allActiveTanks);
                    setting.LastUpdated = DateTime.Now;
                    db.Entry(setting).State = EntityState.Modified;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to update Settings.TankDefinitionsJson in SaveTankAsync");
            }

            StationConfigurationChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save tank: {Name}", tank.TankName);
            return false;
        }
    }

    public async Task<bool> SaveTanksAsync(IEnumerable<TankDefinition> tanks)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

            var existingDbTanks = await db.TankDefinitions.ToListAsync();
            var incomingList = tanks.ToList();

            foreach (var incoming in incomingList)
            {
                TankDefinition? match = null;
                if (incoming.TankId > 0)
                {
                    match = existingDbTanks.FirstOrDefault(x => x.TankId == incoming.TankId);
                }
                if (match == null && !string.IsNullOrWhiteSpace(incoming.TankName))
                {
                    match = existingDbTanks.FirstOrDefault(x => string.Equals(x.TankName.Trim(), incoming.TankName.Trim(), StringComparison.OrdinalIgnoreCase));
                }

                if (match != null)
                {
                    match.TankName = incoming.TankName.Trim();
                    match.CapacityKL = incoming.CapacityKL;
                    match.FuelType = incoming.FuelType;
                    match.IsActive = incoming.IsActive;
                    match.HasTesting = incoming.HasTesting;
                    db.Entry(match).State = EntityState.Modified;
                    incoming.TankId = match.TankId;
                }
                else
                {
                    var newEntity = new TankDefinition
                    {
                        TankName = incoming.TankName.Trim(),
                        CapacityKL = incoming.CapacityKL,
                        FuelType = incoming.FuelType,
                        IsActive = incoming.IsActive,
                        HasTesting = incoming.HasTesting,
                        CreatedAt = DateTime.Now
                    };
                    db.TankDefinitions.Add(newEntity);
                }
            }

            await db.SaveChangesAsync();

            var allActiveTanks = await db.TankDefinitions.ToListAsync();
            PumpConfiguration.InitializeTanksFromDb(allActiveTanks);

            // Sync updated tanks to Settings.TankDefinitionsJson
            try
            {
                var setting = await db.Settings.FirstOrDefaultAsync();
                if (setting != null)
                {
                    setting.TankDefinitionsJson = System.Text.Json.JsonSerializer.Serialize(allActiveTanks);
                    setting.LastUpdated = DateTime.Now;
                    db.Entry(setting).State = EntityState.Modified;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to update Settings.TankDefinitionsJson in SaveTanksAsync");
            }

            StationConfigurationChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to batch save tanks");
            return false;
        }
    }

    public void NotifyConfigurationChanged()
    {
        StationConfigurationChanged?.Invoke();
    }

    public async Task<bool> DeleteTankAsync(int tankId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var item = await db.TankDefinitions.FindAsync(tankId);
            if (item != null)
            {
                db.TankDefinitions.Remove(item);
                await db.SaveChangesAsync();

                var allActiveTanks = await db.TankDefinitions.ToListAsync();
                PumpConfiguration.InitializeTanksFromDb(allActiveTanks);

                // Sync updated tanks to Settings.TankDefinitionsJson
                try
                {
                    var setting = await db.Settings.FirstOrDefaultAsync();
                    if (setting != null)
                    {
                        setting.TankDefinitionsJson = System.Text.Json.JsonSerializer.Serialize(allActiveTanks);
                        setting.LastUpdated = DateTime.Now;
                        db.Entry(setting).State = EntityState.Modified;
                        await db.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to update Settings.TankDefinitionsJson in DeleteTankAsync");
                }

                StationConfigurationChanged?.Invoke();
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete tank {Id}", tankId);
            return false;
        }
    }

    public async Task<List<ProductMaster>> GetAllProductsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            return await db.ProductMasters
                .OrderBy(p => p.Category)
                .ThenBy(p => p.ProductName)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load products");
            return new List<ProductMaster>();
        }
    }

    public async Task<bool> SaveProductAsync(ProductMaster product)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

            if (product.Id > 0)
            {
                var existing = await db.ProductMasters.FindAsync(product.Id);
                if (existing != null)
                {
                    existing.ProductName = product.ProductName;
                    existing.Category = product.Category;
                    existing.Unit = product.Unit;
                    existing.DefaultSaleRate = product.DefaultSaleRate;
                    existing.IsActive = product.IsActive;
                    db.Entry(existing).State = EntityState.Modified;
                }
            }
            else
            {
                db.ProductMasters.Add(product);
            }

            await db.SaveChangesAsync();
            StationConfigurationChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save product: {Name}", product.ProductName);
            return false;
        }
    }

    public async Task<List<StationLayoutPreset>> GetAllPresetsAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            return await db.StationLayoutPresets
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load station presets");
            return new List<StationLayoutPreset>();
        }
    }

    public async Task<StationLayoutPreset?> GetPresetByCodeAsync(string presetCode)
    {
        if (string.IsNullOrWhiteSpace(presetCode)) return null;
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var code = presetCode.Trim();
            return await db.StationLayoutPresets
                .FirstOrDefaultAsync(p => p.PresetCode.ToLower() == code.ToLower());
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load station preset by code: {Code}", presetCode);
            return null;
        }
    }

    public async Task<bool> SavePresetAsync(StationLayoutPreset preset)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

            var existing = await db.StationLayoutPresets
                .FirstOrDefaultAsync(p => p.PresetId == preset.PresetId || p.PresetCode.ToLower() == preset.PresetCode.Trim().ToLower());

            if (existing != null)
            {
                existing.PresetName = preset.PresetName;
                existing.PresetCode = preset.PresetCode.Trim();
                existing.Description = preset.Description;
                existing.LayoutJson = preset.LayoutJson;
                existing.PumpCount = preset.PumpCount;
                existing.NozzleCount = preset.NozzleCount;
                existing.TankCount = preset.TankCount;
                existing.IsActive = preset.IsActive;
                existing.UpdatedAt = DateTime.Now;
                db.Entry(existing).State = EntityState.Modified;
            }
            else
            {
                preset.PresetCode = preset.PresetCode.Trim();
                preset.CreatedAt = DateTime.Now;
                db.StationLayoutPresets.Add(preset);
            }

            await db.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save preset: {Code}", preset.PresetCode);
            return false;
        }
    }

    public async Task<bool> DeletePresetAsync(int presetId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var item = await db.StationLayoutPresets.FindAsync(presetId);
            if (item != null)
            {
                db.StationLayoutPresets.Remove(item);
                await db.SaveChangesAsync();
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete preset {Id}", presetId);
            return false;
        }
    }

    private const string PumpConnectionConfigMetaKey = "Station.PumpConnectionRules";

    public async Task<PumpConnectionConfiguration> GetPumpConnectionConfigurationAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var meta = await db.AppMeta.AsNoTracking().FirstOrDefaultAsync(m => m.Key == PumpConnectionConfigMetaKey);
            if (meta != null && !string.IsNullOrWhiteSpace(meta.Value))
            {
                var config = System.Text.Json.JsonSerializer.Deserialize<PumpConnectionConfiguration>(meta.Value);
                if (config != null)
                {
                    return config;
                }
            }

            // Fallback: Check Setting.PumpConnectionRulesJson if AppMeta is not present
            var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync();
            if (setting != null && !string.IsNullOrWhiteSpace(setting.PumpConnectionRulesJson))
            {
                var config = System.Text.Json.JsonSerializer.Deserialize<PumpConnectionConfiguration>(setting.PumpConnectionRulesJson);
                if (config != null)
                {
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load pump connection configuration from AppMeta/Settings");
        }

        return new PumpConnectionConfiguration();
    }

    public async Task<bool> SavePumpConnectionConfigurationAsync(PumpConnectionConfiguration config)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var json = System.Text.Json.JsonSerializer.Serialize(config);

            // 1. Persist to AppMeta (fast local key-value store)
            var meta = await db.AppMeta.FirstOrDefaultAsync(m => m.Key == PumpConnectionConfigMetaKey);
            if (meta != null)
            {
                meta.Value = json;
                db.Entry(meta).State = EntityState.Modified;
            }
            else
            {
                db.AppMeta.Add(new AppMeta { Key = PumpConnectionConfigMetaKey, Value = json });
            }

            // 2. Persist to Settings.PumpConnectionRulesJson (for cloud snapshot & settings sync)
            try
            {
                var setting = await db.Settings.FirstOrDefaultAsync();
                if (setting != null)
                {
                    setting.PumpConnectionRulesJson = json;
                    setting.LastUpdated = DateTime.Now;
                    db.Entry(setting).State = EntityState.Modified;
                }
            }
            catch (Exception exSetting)
            {
                _logger.Warning(exSetting, "Failed to update Settings.PumpConnectionRulesJson during SavePumpConnectionConfigurationAsync");
            }

            await db.SaveChangesAsync();
            StationConfigurationChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to persist pump connection configuration to AppMeta/Settings");
            return false;
        }
    }
}

