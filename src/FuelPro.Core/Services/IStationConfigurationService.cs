using System.Collections.Generic;
using System.Threading.Tasks;
using FuelPro.Core.Models;

namespace FuelPro.Core.Services;

/// <summary>
/// Service interface for Developer configuration of station layout:
/// Pumps, Nozzles, Tanks, and Fuel Products.
/// </summary>
public interface IStationConfigurationService
{
    event Action? StationConfigurationChanged;

    Task<List<PumpMapping>> GetAllPumpMappingsAsync();
    Task<bool> SavePumpMappingsAsync(IEnumerable<PumpMapping> mappings);
    Task<bool> DeletePumpMappingAsync(int pumpMappingId);

    Task<List<TankDefinition>> GetAllTanksAsync();
    Task<bool> SaveTankAsync(TankDefinition tank);
    Task<bool> SaveTanksAsync(IEnumerable<TankDefinition> tanks);
    Task<bool> DeleteTankAsync(int tankId);
    void NotifyConfigurationChanged();

    Task<List<ProductMaster>> GetAllProductsAsync();
    Task<bool> SaveProductAsync(ProductMaster product);

    Task<List<StationLayoutPreset>> GetAllPresetsAsync();
    Task<StationLayoutPreset?> GetPresetByCodeAsync(string presetCode);
    Task<bool> SavePresetAsync(StationLayoutPreset preset);
    Task<bool> DeletePresetAsync(int presetId);

    Task<PumpConnectionConfiguration> GetPumpConnectionConfigurationAsync();
    Task<bool> SavePumpConnectionConfigurationAsync(PumpConnectionConfiguration config);
}
