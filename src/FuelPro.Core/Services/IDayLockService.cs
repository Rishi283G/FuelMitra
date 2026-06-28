using System;
using System.Threading.Tasks;
using FuelPro.Core.Models;

namespace FuelPro.Core.Services;

public interface IDayLockService
{
    /// <summary>
    /// Check if a specific date is locked.
    /// </summary>
    Task<bool> IsDateLockedAsync(DateTime date);

    /// <summary>
    /// Get the DayLock record for a specific date (null if no lock exists).
    /// </summary>
    Task<DayLock?> GetLockStatusAsync(DateTime date);

    /// <summary>
    /// Lock a day. Both Manager and Owner can lock.
    /// </summary>
    Task<bool> LockDayAsync(DateTime date, string username);

    /// <summary>
    /// Unlock a locked day. Owner only. Requires reason.
    /// </summary>
    Task<bool> UnlockDayAsync(DateTime date, string username, string reason);

    /// <summary>
    /// Check if the current user can modify data for a given date.
    /// Returns true if date is not locked, or if user is Owner.
    /// </summary>
    Task<bool> CanModifyDateAsync(DateTime date, string userRole);
}
