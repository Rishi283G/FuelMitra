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
            var incomingIds = incomingList.Where(m => m.PumpMappingId > 0).Select(m => m.PumpMappingId).ToHashSet();
            var existingAll = await db.PumpMappings.ToListAsync();

            // Remove any mappings that were deleted from the layout
            foreach (var ext in existingAll)
            {
                if (!incomingIds.Contains(ext.PumpMappingId))
                {
                    db.PumpMappings.Remove(ext);
                }
            }

            foreach (var item in incomingList)
            {
                if (item.PumpMappingId > 0)
                {
                    var existing = await db.PumpMappings.FindAsync(item.PumpMappingId);
                    if (existing != null)
                    {
                        existing.PumpId = item.PumpId;
                        existing.NozzleNumber = item.NozzleNumber;
                        existing.FuelType = item.FuelType;
                        existing.TankName = item.TankName;
                        existing.IsActive = item.IsActive;
                        db.Entry(existing).State = EntityState.Modified;
                    }
                }
                else
                {
                    item.CreatedAt = DateTime.Now;
                    db.PumpMappings.Add(item);
                }
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
                    db.Entry(existing).State = EntityState.Modified;
                }
            }
            else
            {
                tank.CreatedAt = DateTime.Now;
                db.TankDefinitions.Add(tank);
            }

            await db.SaveChangesAsync();
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
}
