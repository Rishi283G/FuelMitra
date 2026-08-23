using System.Collections.Generic;
using System.Threading.Tasks;
using FuelPro.Core.Models;

namespace FuelPro.Core.Services;

/// <summary>
/// Service interface for managing configurable collection types.
/// </summary>
public interface ICollectionTypeService
{
    event Action? CollectionTypesChanged;

    /// <summary>
    /// Gets all active collection types in configured display order.
    /// </summary>
    Task<List<CollectionTypeMaster>> GetActiveCollectionTypesAsync();

    /// <summary>
    /// Gets all collection types including disabled ones (for Developer management).
    /// </summary>
    Task<List<CollectionTypeMaster>> GetAllCollectionTypesAsync();

    /// <summary>
    /// Adds or updates a collection type.
    /// </summary>
    Task<bool> SaveCollectionTypeAsync(CollectionTypeMaster item);

    /// <summary>
    /// Deletes a custom collection type (system types cannot be deleted).
    /// </summary>
    Task<bool> DeleteCollectionTypeAsync(int collectionTypeId);

    /// <summary>
    /// Updates the display order for multiple collection types.
    /// </summary>
    Task<bool> UpdateDisplayOrdersAsync(IEnumerable<(int Id, int Order)> orders);
}
