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
            db.PumpMappings.RemoveRange(existingAll);
            await db.SaveChangesAsync();

            foreach (var item in incomingList)
            {
                db.PumpMappings.Add(new PumpMapping
                {
                    PumpId = item.PumpId,
                    NozzleNumber = item.NozzleNumber,
                    FuelType = item.FuelType,
                    TankName = item.TankName,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                });
            }

            await db.SaveChangesAsync();

            // Re-initialize PumpConfiguration in-memory mapping
            var allActive = await db.PumpMappings.ToListAsync();
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

            if (tank.TankId > 0)
            {
                var existing = await db.TankDefinitions.FindAsync(tank.TankId);
                if (existing != null)
                {
                    existing.TankName = tank.TankName;
                    existing.CapacityKL = tank.CapacityKL;
                    existing.FuelType = tank.FuelType;
                    existing.IsActive = tank.IsActive;
                    existing.HasTesting = tank.HasTesting;
                    db.Entry(existing).State = EntityState.Modified;
                }
            }
            else
            {
                tank.CreatedAt = DateTime.Now;
                db.TankDefinitions.Add(tank);
            }

            await db.SaveChangesAsync();

            var allActiveTanks = await db.TankDefinitions.ToListAsync();
            PumpConfiguration.InitializeTanksFromDb(allActiveTanks);

            StationConfigurationChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save tank: {Name}", tank.TankName);
            return false;
        }
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
}
