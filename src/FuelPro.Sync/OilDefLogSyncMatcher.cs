using FuelPro.Core.Models;

namespace FuelPro.Sync;

/// <summary>Applies the shift portion of an oil-log sync identity.</summary>
public static class OilDefLogSyncMatcher
{
    public static IQueryable<OilDefDailyLog> ForShift(IQueryable<OilDefDailyLog> logs, int? shiftId)
        => logs.Where(log => log.ShiftId == shiftId);
}
