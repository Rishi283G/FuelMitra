using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Core.Services;

namespace FuelPro.Data.Services;

public class DayLockService : IDayLockService
{
    private readonly FuelProDbContext _context;

    public DayLockService(FuelProDbContext context)
    {
        _context = context;
    }

    public async Task<bool> IsDateLockedAsync(DateTime date)
    {
        var targetDate = date.Date;
        return await _context.DayLocks
            .AnyAsync(l => l.LockDate == targetDate && l.IsLocked);
    }

    public async Task<DayLock?> GetLockStatusAsync(DateTime date)
    {
        var targetDate = date.Date;
        return await _context.DayLocks
            .FirstOrDefaultAsync(l => l.LockDate == targetDate);
    }

    public async Task<bool> LockDayAsync(DateTime date, string username)
    {
        var targetDate = date.Date;
        var existing = await GetLockStatusAsync(targetDate);

        if (existing != null)
        {
            if (existing.IsLocked) return true; // Already locked
            existing.IsLocked = true;
            existing.LockedAt = DateTime.Now;
            existing.LockedBy = username;
            existing.UnlockedAt = null;
            existing.UnlockedBy = null;
            existing.UnlockReason = null;
        }
        else
        {
            var dayLock = new DayLock
            {
                LockDate = targetDate,
                LockedAt = DateTime.Now,
                LockedBy = username,
                IsLocked = true
            };
            _context.DayLocks.Add(dayLock);
        }

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UnlockDayAsync(DateTime date, string username, string reason)
    {
        var targetDate = date.Date;
        var existing = await GetLockStatusAsync(targetDate);

        if (existing == null || !existing.IsLocked)
        {
            return false; // Can't unlock what is not locked
        }

        existing.IsLocked = false;
        existing.UnlockedAt = DateTime.Now;
        existing.UnlockedBy = username;
        existing.UnlockReason = reason;

        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CanModifyDateAsync(DateTime date, string userRole)
    {
        if (userRole == "Owner") return true; // Owner is unrestricted
        
        var isLocked = await IsDateLockedAsync(date);
        return !isLocked; // Others can only modify if day is not locked
    }
}
