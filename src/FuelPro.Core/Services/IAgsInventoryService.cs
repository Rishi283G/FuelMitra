using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;

namespace FuelPro.Core.Services;

public interface IAgsInventoryService
{
    Task<List<NozzleGroupDto>> BuildNozzleGroupsAsync(DateTime date, string shiftType, List<DsmEntry>? loadedEntries = null);
    Task<List<NozzleGroupDto>> BuildNozzleGroupsForDateRangeAsync(DateTime startDate, DateTime endDate, List<DsmEntry>? loadedEntries = null);
    Task<Result<AgsShiftImport>> SaveAndPersistInventoryAsync(DateTime date, string shiftType, List<NozzleGroupDto> groups);
    Task<Result> PropagateInventoryCalculationsAsync(DateTime startDate, string startShiftType);
    (DateTime Date, string Shift) GetPreviousShift(DateTime date, string shift);
}
