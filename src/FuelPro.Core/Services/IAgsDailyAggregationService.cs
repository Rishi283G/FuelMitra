using FuelPro.Core.DTOs;
using FuelPro.Core.Models.AGS;

namespace FuelPro.Core.Services;

public interface IAgsDailyAggregationService
{
    AgsDailySummary Aggregate(DateTime date, List<AgsShiftImport> shiftsForDay);
    AgsDailySummaryDto ToDto(AgsDailySummary summary, List<AgsShiftImport> shiftsForDay);
}
