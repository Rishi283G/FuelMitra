using FuelPro.Core.DTOs;

namespace FuelPro.Core.Services;

public interface IDsmCalculationService
{
    DsmCalculationResult Calculate(DsmEntryDto dsm);
}
