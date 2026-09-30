using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FuelPro.Data.Services;

public class CollectionTypeService : ICollectionTypeService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger = Log.ForContext<CollectionTypeService>();

    public event Action? CollectionTypesChanged;
    public void NotifyCollectionTypesChanged() => CollectionTypesChanged?.Invoke();

    public CollectionTypeService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task<List<CollectionTypeMaster>> GetActiveCollectionTypesAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            return await db.CollectionTypes
                .Where(c => c.IsActive)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.CollectionTypeId)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load active collection types");
            return new List<CollectionTypeMaster>();
        }
    }

    public async Task<List<CollectionTypeMaster>> GetAllCollectionTypesAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            return await db.CollectionTypes
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.CollectionTypeId)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to load all collection types");
            return new List<CollectionTypeMaster>();
        }
    }

    public async Task<bool> SaveCollectionTypeAsync(CollectionTypeMaster item)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

            if (item.CollectionTypeId > 0)
            {
                var existing = await db.CollectionTypes.FindAsync(item.CollectionTypeId);
                if (existing != null)
                {
                    existing.Code = item.Code;
                    existing.DisplayName = item.DisplayName;
                    existing.Category = item.Category;
                    existing.HasTidBatch = item.HasTidBatch;
                    existing.DisplayOrder = item.DisplayOrder;
                    existing.IsActive = item.IsActive;
                    db.Entry(existing).State = EntityState.Modified;
                }
            }
            else
            {
                // Ensure unique code
                if (string.IsNullOrWhiteSpace(item.Code))
                {
                    item.Code = $"COLLECTION_{item.DisplayOrder}";
                }
                item.Code = item.Code.Trim().ToUpperInvariant().Replace(" ", "_");
                var codeExists = await db.CollectionTypes.AnyAsync(c => c.Code == item.Code);
                if (codeExists)
                {
                    item.Code = $"{item.Code}_{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
                }
                item.CreatedAt = DateTime.Now;
                db.CollectionTypes.Add(item);
            }

            await db.SaveChangesAsync();

            // Sync updated collection types to Settings.CollectionTypesJson
            try
            {
                var allActive = await db.CollectionTypes.ToListAsync();
                var setting = await db.Settings.FirstOrDefaultAsync();
                if (setting != null)
                {
                    setting.CollectionTypesJson = System.Text.Json.JsonSerializer.Serialize(allActive);
                    setting.LastUpdated = DateTime.Now;
                    db.Entry(setting).State = EntityState.Modified;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to update Settings.CollectionTypesJson");
            }

            CollectionTypesChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save collection type: {Name}", item.DisplayName);
            return false;
        }
    }

    public async Task<bool> DeleteCollectionTypeAsync(int collectionTypeId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var item = await db.CollectionTypes.FindAsync(collectionTypeId);
            if (item == null) return false;

            db.CollectionTypes.Remove(item);
            await db.SaveChangesAsync();

            // Sync updated collection types to Settings.CollectionTypesJson
            try
            {
                var allActive = await db.CollectionTypes.ToListAsync();
                var setting = await db.Settings.FirstOrDefaultAsync();
                if (setting != null)
                {
                    setting.CollectionTypesJson = System.Text.Json.JsonSerializer.Serialize(allActive);
                    setting.LastUpdated = DateTime.Now;
                    db.Entry(setting).State = EntityState.Modified;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to update Settings.CollectionTypesJson after deletion");
            }

            CollectionTypesChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete collection type id {Id}", collectionTypeId);
            return false;
        }
    }

    public async Task<bool> UpdateDisplayOrdersAsync(IEnumerable<(int Id, int Order)> orders)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var all = await db.CollectionTypes.ToListAsync();

            foreach (var (id, order) in orders)
            {
                var item = all.FirstOrDefault(c => c.CollectionTypeId == id);
                if (item != null)
                {
                    item.DisplayOrder = order;
                    db.Entry(item).State = EntityState.Modified;
                }
            }

            await db.SaveChangesAsync();

            // Sync updated collection types to Settings.CollectionTypesJson
            try
            {
                var allActive = await db.CollectionTypes.ToListAsync();
                var setting = await db.Settings.FirstOrDefaultAsync();
                if (setting != null)
                {
                    setting.CollectionTypesJson = System.Text.Json.JsonSerializer.Serialize(allActive);
                    setting.LastUpdated = DateTime.Now;
                    db.Entry(setting).State = EntityState.Modified;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to update Settings.CollectionTypesJson after order change");
            }

            CollectionTypesChanged?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update collection display orders");
            return false;
        }
    }
}
