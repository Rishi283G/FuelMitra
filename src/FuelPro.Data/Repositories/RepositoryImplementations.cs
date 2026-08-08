using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;
using FuelPro.Core.Repositories;
using Serilog;

namespace FuelPro.Data.Repositories;

public class SettingsRepository : ISettingsRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<SettingsRepository>();

    public SettingsRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<Setting>> GetSettingsAsync()
    {
        try
        {
            var settings = await _context.Settings.AsNoTracking().FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new Setting
                {
                    HsdRate = 90.35,
                    MsIRate = 103.81,
                    MsIIRate = 103.81
                };
                _context.Settings.Add(settings);
                await _context.SaveChangesAsync();
            }
            return Result<Setting>.Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get settings");
            return Result<Setting>.Fail($"Failed to load settings: {ex.Message}");
        }
    }

    public async Task<Result> SaveSettingsAsync(Setting settings)
    {
        try
        {
            settings.LastUpdated = DateTime.Now;
            _context.Settings.Update(settings);
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save settings");
            return Result.Fail($"Failed to save settings: {ex.Message}");
        }
    }
}

public class UserRepository : IUserRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<UserRepository>();

    public UserRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<User>> GetByUsernameAsync(string username)
    {
        try
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower() && u.IsActive);
            return user != null
                ? Result<User>.Ok(user)
                : Result<User>.Fail("User not found");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get user {Username}", username);
            return Result<User>.Fail($"Failed to find user: {ex.Message}");
        }
    }

    public async Task<Result<List<User>>> GetAllUsersAsync()
    {
        try
        {
            var users = await _context.Users.OrderBy(u => u.Username).ToListAsync();
            return Result<List<User>>.Ok(users);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get all users");
            return Result<List<User>>.Fail($"Failed to load users: {ex.Message}");
        }
    }

    public async Task<Result<User>> CreateUserAsync(User user)
    {
        try
        {
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return Result<User>.Ok(user);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create user");
            return Result<User>.Fail($"Failed to create user: {ex.Message}");
        }
    }

    public async Task<Result> UpdateUserAsync(User user)
    {
        try
        {
            _context.Users.Update(user);
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update user");
            return Result.Fail($"Failed to update user: {ex.Message}");
        }
    }

    public async Task<Result> DeleteUserAsync(int userId)
    {
        try
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null) return Result.Fail("User not found");
            user.IsActive = false; // soft delete
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete user {UserId}", userId);
            return Result.Fail($"Failed to delete user: {ex.Message}");
        }
    }
}

public class ShiftRepository : IShiftRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<ShiftRepository>();

    public ShiftRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<Shift>> GetOrCreateShiftAsync(DateTime date, string shiftType)
    {
        try
        {
            var dateOnly = date.Date;
            var altShift = shiftType == "A" ? "I" : (shiftType == "B" ? "II" : (shiftType == "C" ? "III" : (shiftType == "I" ? "A" : (shiftType == "II" ? "B" : (shiftType == "III" ? "C" : shiftType)))));
            var shift = await _context.Shifts
                .FirstOrDefaultAsync(s => s.ShiftDate == dateOnly && (s.ShiftType == shiftType || s.ShiftType == altShift));

            if (shift == null)
            {
                shift = new Shift
                {
                    ShiftDate = dateOnly,
                    ShiftType = shiftType,
                    IsLocked = false,
                    CreatedAt = DateTime.Now
                };
                _context.Shifts.Add(shift);
                await _context.SaveChangesAsync();
            }

            return Result<Shift>.Ok(shift);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get/create shift for {Date} {ShiftType}", date, shiftType);
            return Result<Shift>.Fail($"Failed to access shift: {ex.Message}");
        }
    }

    public async Task<Result<Shift>> GetShiftAsync(DateTime date, string shiftType)
    {
        try
        {
            var dateOnly = date.Date;
            var altShift = shiftType == "A" ? "I" : (shiftType == "B" ? "II" : (shiftType == "C" ? "III" : (shiftType == "I" ? "A" : (shiftType == "II" ? "B" : (shiftType == "III" ? "C" : shiftType)))));
            var shift = await _context.Shifts
                .Include(s => s.DsmEntries)
                .FirstOrDefaultAsync(s => s.ShiftDate == dateOnly && (s.ShiftType == shiftType || s.ShiftType == altShift));

            return shift != null
                ? Result<Shift>.Ok(shift)
                : Result<Shift>.Fail("Shift not found");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get shift");
            return Result<Shift>.Fail($"Failed to load shift: {ex.Message}");
        }
    }

    public async Task<Result<Shift>> GetShiftByIdAsync(int shiftId)
    {
        try
        {
            var shift = await _context.Shifts
                .Include(s => s.DsmEntries)
                .FirstOrDefaultAsync(s => s.ShiftId == shiftId);

            return shift != null
                ? Result<Shift>.Ok(shift)
                : Result<Shift>.Fail("Shift not found");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get shift {ShiftId}", shiftId);
            return Result<Shift>.Fail($"Failed to load shift: {ex.Message}");
        }
    }

    public async Task<Result<List<Shift>>> GetShiftsForDateAsync(DateTime date)
    {
        try
        {
            var dateOnly = date.Date;
            var shifts = await _context.Shifts
                .Where(s => s.ShiftDate == dateOnly)
                .OrderBy(s => s.ShiftType)
                .ToListAsync();
            return Result<List<Shift>>.Ok(shifts);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get shifts for date {Date}", date);
            return Result<List<Shift>>.Fail($"Failed to load shifts: {ex.Message}");
        }
    }

    public async Task<Result> LockShiftAsync(int shiftId)
    {
        try
        {
            var shift = await _context.Shifts.FindAsync(shiftId);
            if (shift == null) return Result.Fail("Shift not found");
            shift.IsLocked = true;
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to lock shift {ShiftId}", shiftId);
            return Result.Fail($"Failed to lock shift: {ex.Message}");
        }
    }

    public async Task<Result<bool>> IsShiftLockedAsync(int shiftId)
    {
        try
        {
            var shift = await _context.Shifts.FindAsync(shiftId);
            return Result<bool>.Ok(shift?.IsLocked ?? false);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check shift lock status");
            return Result<bool>.Fail($"Failed to check shift lock: {ex.Message}");
        }
    }

    public async Task<Result<List<Shift>>> GetShiftsByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            var start = startDate.Date;
            var end = endDate.Date;
            var shifts = await _context.Shifts
                .Include(s => s.DsmEntries)
                .Where(s => s.ShiftDate >= start && s.ShiftDate < end.AddDays(1))
                .OrderBy(s => s.ShiftDate)
                .ThenBy(s => s.ShiftType)
                .ToListAsync();
            return Result<List<Shift>>.Ok(shifts);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get shifts by date range {Start} to {End}", startDate, endDate);
            return Result<List<Shift>>.Fail($"Failed to load shifts: {ex.Message}");
        }
    }

    public async Task<Result> UpdateShiftAsync(Shift shift)
    {
        try
        {
            var existing = await _context.Shifts.FindAsync(shift.ShiftId);
            if (existing == null) return Result.Fail("Shift not found");
            
            existing.CardSettlementPosTotal = shift.CardSettlementPosTotal;
            existing.IsLocked = shift.IsLocked;
            
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update shift {ShiftId}", shift.ShiftId);
            return Result.Fail($"Failed to update shift: {ex.Message}");
        }
    }
}

public class DsmEntryRepository : IDsmEntryRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<DsmEntryRepository>();

    public DsmEntryRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<DsmEntry>> GetByIdAsync(int dsmEntryId)
    {
        try
        {
            var entry = await _context.DsmEntries.FindAsync(dsmEntryId);
            return entry != null
                ? Result<DsmEntry>.Ok(entry)
                : Result<DsmEntry>.Fail("DSM Entry not found");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get DSM entry {Id}", dsmEntryId);
            return Result<DsmEntry>.Fail($"Failed to load DSM entry: {ex.Message}");
        }
    }

    public async Task<Result<DsmEntry>> GetFullEntryAsync(int dsmEntryId)
    {
        try
        {
            var entry = await _context.DsmEntries
                .AsNoTracking()
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                .Include(e => e.DebitEntries)
                .Include(e => e.TestingEntries)
                .Include(e => e.Expenses)
                .Include(e => e.CashDenominations)
                .Include(e => e.PersonalDebtors)
                .Include(e => e.KhandharePetroleumEntries)
                .Include(e => e.Shift)
                .FirstOrDefaultAsync(e => e.DsmEntryId == dsmEntryId);

            return entry != null
                ? Result<DsmEntry>.Ok(entry)
                : Result<DsmEntry>.Fail("DSM Entry not found");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get full DSM entry {Id}", dsmEntryId);
            return Result<DsmEntry>.Fail($"Failed to load DSM entry: {ex.Message}");
        }
    }

    public async Task<Result<List<DsmEntry>>> GetEntriesForShiftAsync(int shiftId)
    {
        try
        {
            var entries = await _context.DsmEntries
                .AsNoTracking()
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                .Include(e => e.DebitEntries)
                .Include(e => e.TestingEntries)
                .Include(e => e.Expenses)
                .Include(e => e.CashDenominations)
                .Include(e => e.PersonalDebtors)
                .Include(e => e.KhandharePetroleumEntries)
                .Where(e => e.ShiftId == shiftId)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();

            return Result<List<DsmEntry>>.Ok(entries);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get entries for shift {ShiftId}", shiftId);
            return Result<List<DsmEntry>>.Fail($"Failed to load entries: {ex.Message}");
        }
    }

    public async Task<Result<DsmEntry>> SaveEntryAsync(DsmEntry entry)
    {
        try
        {
            entry.UpdatedAt = DateTime.Now;
            if (entry.DsmEntryId == 0)
            {
                entry.CreatedAt = DateTime.Now;
                _context.DsmEntries.Add(entry);
            }
            else
            {
                var existing = await _context.DsmEntries.FindAsync(entry.DsmEntryId);
                if (existing != null)
                {
                    _context.Entry(existing).CurrentValues.SetValues(entry);
                    entry = existing;
                }
                else
                {
                    _context.DsmEntries.Update(entry);
                }
            }
            await _context.SaveChangesAsync();
            return Result<DsmEntry>.Ok(entry);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save DSM entry");
            return Result<DsmEntry>.Fail($"Failed to save DSM entry: {ex.Message}");
        }
    }

    public async Task<Result> DeleteEntryAsync(int dsmEntryId)
    {
        try
        {
            var entry = await _context.DsmEntries.FindAsync(dsmEntryId);
            if (entry == null) return Result.Fail("DSM Entry not found");
            _context.DsmEntries.Remove(entry);
            await _context.SaveChangesAsync();
            FuelPro.Core.Services.DsmEntryService.RaiseDsmEntryChanged();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete DSM entry {Id}", dsmEntryId);
            return Result.Fail($"Failed to delete DSM entry: {ex.Message}");
        }
    }

    public async Task<Result<bool>> IsDuplicateAsync(int shiftId, int pumpId, string dsmName, int? excludeId = null)
    {
        try
        {
            var query = _context.DsmEntries
                .Where(e => e.ShiftId == shiftId && e.PumpId == pumpId && e.DsmName == dsmName);

            if (excludeId.HasValue)
                query = query.Where(e => e.DsmEntryId != excludeId.Value);

            var exists = await query.AnyAsync();
            return Result<bool>.Ok(exists);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to check duplicate");
            return Result<bool>.Fail($"Failed to check duplicate: {ex.Message}");
        }
    }

    public async Task<Result<List<string>>> GetDistinctDsmNamesAsync()
    {
        try
        {
            var names = await _context.DsmEntries
                .Select(e => e.DsmName)
                .Distinct()
                .OrderBy(n => n)
                .ToListAsync();
            return Result<List<string>>.Ok(names);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get DSM names");
            return Result<List<string>>.Fail($"Failed to load names: {ex.Message}");
        }
    }

    public async Task<Result<List<DsmEntry>>> GetEntriesForDsmAndMonthAsync(string dsmName, int year, int month)
    {
        try
        {
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var entries = await _context.DsmEntries
                .AsNoTracking()
                .Include(e => e.Shift)
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                .Include(e => e.DebitEntries)
                .Include(e => e.TestingEntries)
                .Include(e => e.Expenses)
                .Include(e => e.CashDenominations)
                .Where(e => e.DsmName == dsmName && 
                            e.Shift != null && e.Shift.ShiftDate >= startDate && 
                            e.Shift.ShiftDate <= endDate)
                .OrderBy(e => e.Shift!.ShiftDate)
                .ThenBy(e => e.Shift!.ShiftType)
                .ToListAsync();

            return Result<List<DsmEntry>>.Ok(entries);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get DSM entries for {DsmName} in {Year}-{Month}", dsmName, year, month);
            return Result<List<DsmEntry>>.Fail($"Failed to load entries: {ex.Message}");
        }
    }

    public async Task<Result<List<DsmEntry>>> GetEntriesForMonthAsync(int year, int month)
    {
        try
        {
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var entries = await _context.DsmEntries
                .AsNoTracking()
                .Include(e => e.Shift)
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                .Include(e => e.DebitEntries)
                .Include(e => e.TestingEntries)
                .Include(e => e.Expenses)
                .Include(e => e.CashDenominations)
                .Where(e => e.Shift != null && e.Shift.ShiftDate >= startDate && 
                            e.Shift.ShiftDate <= endDate)
                .OrderBy(e => e.Shift!.ShiftDate)
                .ThenBy(e => e.Shift!.ShiftType)
                .ToListAsync();

            return Result<List<DsmEntry>>.Ok(entries);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get DSM entries for month {Year}-{Month}", year, month);
            return Result<List<DsmEntry>>.Fail($"Failed to load entries: {ex.Message}");
        }
    }

    public async Task<Result<List<DsmEntry>>> GetEntriesForDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            var start = startDate.Date;
            var end = endDate.Date;
            var entries = await _context.DsmEntries
                .AsNoTracking()
                .Include(e => e.Shift)
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                .Include(e => e.DebitEntries)
                .Include(e => e.TestingEntries)
                .Include(e => e.Expenses)
                .Include(e => e.CashDenominations)
                .Include(e => e.KhandharePetroleumEntries)
                .Where(e => e.Shift != null && e.Shift.ShiftDate >= start && e.Shift.ShiftDate < end.AddDays(1))
                .OrderBy(e => e.Shift!.ShiftDate)
                .ThenBy(e => e.Shift!.ShiftType)
                .ToListAsync();
            return Result<List<DsmEntry>>.Ok(entries);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get DSM entries for date range {Start} to {End}", startDate, endDate);
            return Result<List<DsmEntry>>.Fail($"Failed to load entries: {ex.Message}");
        }
    }
}

public class NozzleReadingRepository : INozzleReadingRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<NozzleReadingRepository>();

    public NozzleReadingRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<List<NozzleReading>>> GetByDsmEntryIdAsync(int dsmEntryId)
    {
        try
        {
            var readings = await _context.NozzleReadings
                .Where(r => r.DsmEntryId == dsmEntryId)
                .OrderBy(r => r.NozzleNumber)
                .ToListAsync();
            return Result<List<NozzleReading>>.Ok(readings);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get nozzle readings for {DsmEntryId}", dsmEntryId);
            return Result<List<NozzleReading>>.Fail($"Failed to load readings: {ex.Message}");
        }
    }

    public async Task<Result<Dictionary<int, double>>> GetPreviousShiftClosingsAsync(DateTime date, string shiftType, int pumpId, int? currentDsmEntryId = null)
    {
        try
        {
            var targetDate = date.Date;
            var altShift = shiftType == "A" ? "I" : (shiftType == "B" ? "II" : (shiftType == "C" ? "III" : (shiftType == "I" ? "A" : (shiftType == "II" ? "B" : (shiftType == "III" ? "C" : shiftType)))));
            var isDayShift = shiftType == "B" || shiftType == "II";
            var closings = await _context.NozzleReadings
                .Include(r => r.DsmEntry)
                .ThenInclude(e => e!.Shift)
                .Where(r => r.DsmEntry != null
                    && r.DsmEntry.Shift != null
                    && r.DsmEntry.PumpId == pumpId
                    && (currentDsmEntryId == null || r.DsmEntryId != currentDsmEntryId.Value)
                    && (r.DsmEntry.Shift.ShiftDate < targetDate || 
                       (r.DsmEntry.Shift.ShiftDate == targetDate && isDayShift && (r.DsmEntry.Shift.ShiftType == "A" || r.DsmEntry.Shift.ShiftType == "I")) ||
                       (r.DsmEntry.Shift.ShiftDate == targetDate && (r.DsmEntry.Shift.ShiftType == shiftType || r.DsmEntry.Shift.ShiftType == altShift) && (currentDsmEntryId == null || r.DsmEntryId < currentDsmEntryId.Value))))
                .GroupBy(r => r.NozzleNumber)
                .Select(g => new { 
                    NozzleNumber = g.Key, 
                    Closing = g.OrderByDescending(x => x.DsmEntry!.Shift!.ShiftDate)
                               .ThenByDescending(x => x.DsmEntry!.Shift!.ShiftType)
                               .ThenByDescending(x => x.DsmEntryId)
                               .ThenByDescending(x => x.NozzleReadingId)
                               .Select(x => x.ClosingReading)
                               .FirstOrDefault() 
                })
                .ToDictionaryAsync(x => x.NozzleNumber, x => x.Closing);

            return Result<Dictionary<int, double>>.Ok(closings);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get previous-shift closings for date {Date} shift {ShiftType} pump {PumpId}", date, shiftType, pumpId);
            return Result<Dictionary<int, double>>.Fail($"Failed to load previous-shift closings: {ex.Message}");
        }
    }

    public async Task<Result> SaveReadingsAsync(int dsmEntryId, List<NozzleReading> readings)
    {
        try
        {
            var existing = await _context.NozzleReadings
                .Where(r => r.DsmEntryId == dsmEntryId)
                .ToListAsync();
            _context.NozzleReadings.RemoveRange(existing);

            foreach (var r in readings)
            {
                r.DsmEntryId = dsmEntryId;
                r.DsmEntry = null;
                if (r.ClosingReading < r.OpeningReading)
                {
                    throw new ArgumentException("Closing cannot be less than opening");
                }
                r.SaleLitres = r.ClosingReading - r.OpeningReading;
                r.Amount = r.SaleLitres * r.Rate;
            }
            _context.NozzleReadings.AddRange(readings);
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save nozzle readings");
            return Result.Fail($"Failed to save readings: {ex.Message}");
        }
    }
}

public class PaymentRepository : IPaymentRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<PaymentRepository>();

    public PaymentRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<PaymentCollection>> GetByDsmEntryIdAsync(int dsmEntryId)
    {
        try
        {
            var payment = await _context.PaymentCollections
                .FirstOrDefaultAsync(p => p.DsmEntryId == dsmEntryId);
            return payment != null
                ? Result<PaymentCollection>.Ok(payment)
                : Result<PaymentCollection>.Ok(new PaymentCollection { DsmEntryId = dsmEntryId });
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get payment for {DsmEntryId}", dsmEntryId);
            return Result<PaymentCollection>.Fail($"Failed to load payment: {ex.Message}");
        }
    }

    public async Task<Result> SavePaymentAsync(PaymentCollection payment)
    {
        try
        {
            payment.DsmEntry = null;
            var existing = await _context.PaymentCollections
                .FirstOrDefaultAsync(p => p.DsmEntryId == payment.DsmEntryId);

            if (existing != null)
            {
                existing.PhonePeCardMorning = payment.PhonePeCardMorning;
                existing.PhonePeCardDay = payment.PhonePeCardDay;
                existing.PhonePeCardNight = payment.PhonePeCardNight;
                existing.PhonePeMorning = payment.PhonePeMorning;
                existing.PhonePeDay = payment.PhonePeDay;
                existing.PhonePeNight = payment.PhonePeNight;
                existing.CreditCardMorning = payment.CreditCardMorning;
                existing.CreditCardDay = payment.CreditCardDay;
                existing.CreditCardNight = payment.CreditCardNight;
                existing.PetroCardMorning = payment.PetroCardMorning;
                existing.PetroCardDay = payment.PetroCardDay;
                existing.PetroCardNight   = payment.PetroCardNight;
                existing.CashDeposit = payment.CashDeposit;
                existing.Others      = payment.Others;
                existing.CardTid     = payment.CardTid;
                existing.CardBatch   = payment.CardBatch;
                existing.PhonePeTid  = payment.PhonePeTid;
                existing.PhonePeBatch = payment.PhonePeBatch;
                existing.PetroCardTid = payment.PetroCardTid;
                existing.PetroCardBatch = payment.PetroCardBatch;
                existing.CreditCardTidMorning = payment.CreditCardTidMorning;
                existing.CreditCardBatchMorning = payment.CreditCardBatchMorning;
                existing.CreditCardTidDay = payment.CreditCardTidDay;
                existing.CreditCardBatchDay = payment.CreditCardBatchDay;
                existing.CreditCardTidNight = payment.CreditCardTidNight;
                existing.CreditCardBatchNight = payment.CreditCardBatchNight;
                existing.PetroCardTidMorning = payment.PetroCardTidMorning;
                existing.PetroCardBatchMorning = payment.PetroCardBatchMorning;
                existing.PetroCardTidDay = payment.PetroCardTidDay;
                existing.PetroCardBatchDay = payment.PetroCardBatchDay;
                existing.PetroCardTidNight = payment.PetroCardTidNight;
                existing.PetroCardBatchNight = payment.PetroCardBatchNight;
                existing.PhonePeTidMorning = payment.PhonePeTidMorning;
                existing.PhonePeBatchMorning = payment.PhonePeBatchMorning;
                existing.PhonePeTidDay = payment.PhonePeTidDay;
                existing.PhonePeBatchDay = payment.PhonePeBatchDay;
                existing.PhonePeTidNight = payment.PhonePeTidNight;
                existing.PhonePeBatchNight = payment.PhonePeBatchNight;
            }
            else
            {
                _context.PaymentCollections.Add(payment);
            }
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save payment");
            return Result.Fail($"Failed to save payment: {ex.Message}");
        }
    }
}

public class DebitEntryRepository : IDebitEntryRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<DebitEntryRepository>();

    public DebitEntryRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<List<DebitEntry>>> GetByDsmEntryIdAsync(int dsmEntryId)
    {
        try
        {
            var debits = await _context.DebitEntries
                .Where(d => d.DsmEntryId == dsmEntryId)
                .ToListAsync();
            return Result<List<DebitEntry>>.Ok(debits);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get debits for {DsmEntryId}", dsmEntryId);
            return Result<List<DebitEntry>>.Fail($"Failed to load debits: {ex.Message}");
        }
    }

    public async Task<Result> SaveDebitsAsync(int dsmEntryId, List<DebitEntry> debits)
    {
        try
        {
            var existing = await _context.DebitEntries
                .Where(d => d.DsmEntryId == dsmEntryId).ToListAsync();
            _context.DebitEntries.RemoveRange(existing);

            foreach (var d in debits)
            {
                d.DsmEntryId = dsmEntryId;
                d.DsmEntry = null;
            }
            _context.DebitEntries.AddRange(debits);
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save debits");
            return Result.Fail($"Failed to save debits: {ex.Message}");
        }
    }
}

public class TestingEntryRepository : ITestingEntryRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<TestingEntryRepository>();

    public TestingEntryRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<List<TestingEntry>>> GetByDsmEntryIdAsync(int dsmEntryId)
    {
        try
        {
            var entries = await _context.TestingEntries
                .Where(t => t.DsmEntryId == dsmEntryId).ToListAsync();
            return Result<List<TestingEntry>>.Ok(entries);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get testing entries");
            return Result<List<TestingEntry>>.Fail($"Failed to load testing entries: {ex.Message}");
        }
    }

    public async Task<Result> SaveTestingEntriesAsync(int dsmEntryId, List<TestingEntry> entries)
    {
        try
        {
            var existing = await _context.TestingEntries
                .Where(t => t.DsmEntryId == dsmEntryId).ToListAsync();
            _context.TestingEntries.RemoveRange(existing);

            foreach (var e in entries)
            {
                e.DsmEntryId = dsmEntryId;
                e.DsmEntry = null;
            }
            _context.TestingEntries.AddRange(entries);
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save testing entries");
            return Result.Fail($"Failed to save testing entries: {ex.Message}");
        }
    }
}

public class ExpenseRepository : IExpenseRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<ExpenseRepository>();

    public ExpenseRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<List<Expense>>> GetByDsmEntryIdAsync(int dsmEntryId)
    {
        try
        {
            var expenses = await _context.Expenses
                .Where(e => e.DsmEntryId == dsmEntryId).ToListAsync();
            return Result<List<Expense>>.Ok(expenses);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get expenses for DSM {Id}", dsmEntryId);
            return Result<List<Expense>>.Fail($"Failed to load expenses: {ex.Message}");
        }
    }

    public async Task<Result<List<Expense>>> GetByShiftIdAsync(int shiftId)
    {
        try
        {
            var expenses = await _context.Expenses
                .Where(e => e.ShiftId == shiftId && e.DsmEntryId == null).ToListAsync();
            return Result<List<Expense>>.Ok(expenses);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get shift expenses");
            return Result<List<Expense>>.Fail($"Failed to load expenses: {ex.Message}");
        }
    }

    public async Task<Result> SaveExpensesAsync(int dsmEntryId, List<Expense> expenses)
    {
        try
        {
            var existing = await _context.Expenses
                .Where(e => e.DsmEntryId == dsmEntryId).ToListAsync();
            _context.Expenses.RemoveRange(existing);

            foreach (var e in expenses)
            {
                e.DsmEntryId = dsmEntryId;
                e.DsmEntry = null;
            }
            _context.Expenses.AddRange(expenses);
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save expenses");
            return Result.Fail($"Failed to save expenses: {ex.Message}");
        }
    }

    public async Task<Result<Expense>> AddShiftExpenseAsync(int shiftId, string description, double amount)
    {
        try
        {
            var expense = new Expense
            {
                ShiftId = shiftId,
                DsmEntryId = null,
                Description = description,
                Amount = amount
            };
            _context.Expenses.Add(expense);
            await _context.SaveChangesAsync();
            return Result<Expense>.Ok(expense);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add shift expense");
            return Result<Expense>.Fail($"Failed to add expense: {ex.Message}");
        }
    }

    public async Task<Result> DeleteExpenseAsync(int expenseId)
    {
        try
        {
            var expense = await _context.Expenses.FindAsync(expenseId);
            if (expense == null) return Result.Fail("Expense not found");
            _context.Expenses.Remove(expense);
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete expense {Id}", expenseId);
            return Result.Fail($"Failed to delete expense: {ex.Message}");
        }
    }

    public async Task<Result<List<Expense>>> GetExpensesByShiftIdsAsync(List<int> shiftIds)
    {
        try
        {
            var expenses = await _context.Expenses
                .Where(e => e.ShiftId != null && shiftIds.Contains(e.ShiftId.Value) && e.DsmEntryId == null)
                .ToListAsync();
            return Result<List<Expense>>.Ok(expenses);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get expenses for shift IDs");
            return Result<List<Expense>>.Fail($"Failed to load expenses: {ex.Message}");
        }
    }
}

public class CashDenominationRepository : ICashDenominationRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<CashDenominationRepository>();

    public CashDenominationRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<List<CashDenomination>>> GetByDsmEntryIdAsync(int dsmEntryId)
    {
        try
        {
            var denoms = await _context.CashDenominations
                .Where(c => c.DsmEntryId == dsmEntryId).ToListAsync();
            return Result<List<CashDenomination>>.Ok(denoms);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get cash denominations");
            return Result<List<CashDenomination>>.Fail($"Failed to load cash: {ex.Message}");
        }
    }

    public async Task<Result> SaveCashDenominationsAsync(int dsmEntryId, List<CashDenomination> denominations)
    {
        try
        {
            var existing = await _context.CashDenominations
                .Where(c => c.DsmEntryId == dsmEntryId).ToListAsync();

            foreach (var d in denominations)
            {
                d.DsmEntryId = dsmEntryId;
                d.DsmEntry = null;
                d.RecalculateTotal();

                var match = existing.FirstOrDefault(e => e.CashType == d.CashType);
                if (match != null)
                {
                    match.Coins = d.Coins;
                    match.Denom10 = d.Denom10;
                    match.Denom20 = d.Denom20;
                    match.Denom50 = d.Denom50;
                    match.Denom100 = d.Denom100;
                    match.Denom200 = d.Denom200;
                    match.Denom500 = d.Denom500;
                    match.TotalAmount = d.TotalAmount;
                    _context.Entry(match).State = EntityState.Modified;
                }
                else
                {
                    _context.CashDenominations.Add(d);
                }
            }

            var passedTypes = denominations.Select(d => d.CashType).ToList();
            var toDelete = existing.Where(e => !passedTypes.Contains(e.CashType)).ToList();
            if (toDelete.Any())
            {
                _context.CashDenominations.RemoveRange(toDelete);
            }

            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save cash denominations");
            return Result.Fail($"Failed to save cash: {ex.Message}");
        }
    }
}

public class ShiftOtherCashRepository : IShiftOtherCashRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<ShiftOtherCashRepository>();

    public ShiftOtherCashRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<List<ShiftOtherCash>>> GetByShiftAsync(DateTime date, string shiftNumber)
    {
        try
        {
            var dateOnly = date.Date;
            var altShift = shiftNumber == "A" ? "I" : (shiftNumber == "B" ? "II" : (shiftNumber == "C" ? "III" : (shiftNumber == "I" ? "A" : (shiftNumber == "II" ? "B" : (shiftNumber == "III" ? "C" : shiftNumber)))));
            var entries = await _context.ShiftOtherCash
                .Where(e => e.ShiftDate == dateOnly && (e.ShiftNumber == shiftNumber || e.ShiftNumber == altShift))
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();
            return Result<List<ShiftOtherCash>>.Ok(entries);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get shift other cash");
            return Result<List<ShiftOtherCash>>.Fail($"Failed to load other cash: {ex.Message}");
        }
    }

    public async Task<Result<ShiftOtherCash>> AddAsync(ShiftOtherCash entry)
    {
        try
        {
            entry.CreatedAt = DateTime.Now;
            if (!string.IsNullOrEmpty(entry.ShiftNumber))
            {
                entry.ShiftNumber = entry.ShiftNumber == "A" ? "I" : entry.ShiftNumber == "B" ? "II" : entry.ShiftNumber == "C" ? "III" : entry.ShiftNumber;
            }
            _context.ShiftOtherCash.Add(entry);
            await _context.SaveChangesAsync();
            return Result<ShiftOtherCash>.Ok(entry);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add shift other cash");
            return Result<ShiftOtherCash>.Fail($"Failed to add: {ex.Message}");
        }
    }

    public async Task<Result> DeleteAsync(int shiftOtherCashId)
    {
        try
        {
            var entry = await _context.ShiftOtherCash.FindAsync(shiftOtherCashId);
            if (entry == null) return Result.Fail("Entry not found");
            _context.ShiftOtherCash.Remove(entry);
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete shift other cash {Id}", shiftOtherCashId);
            return Result.Fail($"Failed to delete: {ex.Message}");
        }
    }

    public async Task<Result<List<ShiftOtherCash>>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            var start = startDate.Date;
            var end = endDate.Date;
            var entries = await _context.ShiftOtherCash
                .Where(e => e.ShiftDate >= start && e.ShiftDate <= end)
                .OrderBy(e => e.ShiftDate)
                .ToListAsync();
            return Result<List<ShiftOtherCash>>.Ok(entries);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get shift other cash for range");
            return Result<List<ShiftOtherCash>>.Fail($"Failed to load other cash: {ex.Message}");
        }
    }
}

public class ShiftFuelRateRepository : IShiftFuelRateRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<ShiftFuelRateRepository>();

    public ShiftFuelRateRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<List<ShiftFuelRate>>> GetByShiftAsync(DateTime date, string shiftNumber)
    {
        try
        {
            var dateOnly = date.Date;
            var altShift = shiftNumber == "A" ? "I" : (shiftNumber == "B" ? "II" : (shiftNumber == "C" ? "III" : (shiftNumber == "I" ? "A" : (shiftNumber == "II" ? "B" : (shiftNumber == "III" ? "C" : shiftNumber)))));
            var rates = await _context.ShiftFuelRates
                .Where(r => r.ShiftDate == dateOnly && (r.ShiftNumber == shiftNumber || r.ShiftNumber == altShift))
                .ToListAsync();
            return Result<List<ShiftFuelRate>>.Ok(rates);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get shift fuel rates");
            return Result<List<ShiftFuelRate>>.Fail($"Failed to load rates: {ex.Message}");
        }
    }

    public async Task<Result> SaveRateAsync(ShiftFuelRate rate)
    {
        try
        {
            var dateOnly = rate.ShiftDate.Date;
            var altShift = rate.ShiftNumber == "A" ? "I" : (rate.ShiftNumber == "B" ? "II" : (rate.ShiftNumber == "C" ? "III" : (rate.ShiftNumber == "I" ? "A" : (rate.ShiftNumber == "II" ? "B" : (rate.ShiftNumber == "III" ? "C" : rate.ShiftNumber)))));
            var existing = await _context.ShiftFuelRates
                .FirstOrDefaultAsync(r => r.ShiftDate == dateOnly
                    && (r.ShiftNumber == rate.ShiftNumber || r.ShiftNumber == altShift)
                    && r.FuelType == rate.FuelType);

            if (existing != null)
            {
                existing.OverrideRate = rate.OverrideRate;
                existing.ShiftId = rate.ShiftId;
            }
            else
            {
                _context.ShiftFuelRates.Add(rate);
            }
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save shift fuel rate");
            return Result.Fail($"Failed to save rate: {ex.Message}");
        }
    }
}

public class DsmProfileRepository : IDsmProfileRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<DsmProfileRepository>();

    public DsmProfileRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<List<DsmProfile>>> GetAllAsync()
    {
        try
        {
            var profiles = await _context.DsmProfiles.OrderBy(p => p.DsmName).ToListAsync();
            return Result<List<DsmProfile>>.Ok(profiles);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get DSM profiles");
            return Result<List<DsmProfile>>.Fail($"Failed to load DSM profiles: {ex.Message}");
        }
    }

    public async Task<Result<DsmProfile>> AddAsync(DsmProfile profile)
    {
        try
        {
            var existing = await _context.DsmProfiles
                .FirstOrDefaultAsync(p => p.DsmName.ToLower() == profile.DsmName.ToLower());
            if (existing != null) return Result<DsmProfile>.Fail("DSM Name already exists");

            _context.DsmProfiles.Add(profile);
            await _context.SaveChangesAsync();
            return Result<DsmProfile>.Ok(profile);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add DSM profile");
            return Result<DsmProfile>.Fail($"Failed to add profile: {ex.Message}");
        }
    }

    public async Task<Result> UpdateAsync(DsmProfile profile)
    {
        try
        {
            var nameConflict = await _context.DsmProfiles
                .AnyAsync(p => p.DsmProfileId != profile.DsmProfileId && p.DsmName.ToLower() == profile.DsmName.ToLower());
            if (nameConflict)
            {
                return Result.Fail("A DSM profile with this name already exists.");
            }

            var tracked = _context.DsmProfiles.Local.FirstOrDefault(p => p.DsmProfileId == profile.DsmProfileId);
            if (tracked != null)
            {
                _context.Entry(tracked).CurrentValues.SetValues(profile);
            }
            else
            {
                _context.DsmProfiles.Update(profile);
            }
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update DSM profile");
            return Result.Fail($"Failed to update profile: {ex.Message}");
        }
    }

    public async Task<Result> DeleteAsync(int id)
    {
        try
        {
            var profile = await _context.DsmProfiles.FindAsync(id);
            if (profile == null) return Result.Fail("Profile not found");
            _context.DsmProfiles.Remove(profile);
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete DSM profile {Id}", id);
            return Result.Fail($"Failed to delete profile: {ex.Message}");
        }
    }
}

public class CreditorRepaymentRepository : ICreditorRepaymentRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<CreditorRepaymentRepository>();

    public CreditorRepaymentRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<List<CreditorRepayment>>> GetByDateAsync(DateTime date)
    {
        try
        {
            var dateOnly = date.Date;
            var repayments = await _context.CreditorRepayments
                .Where(r => r.RepaymentDate >= dateOnly && r.RepaymentDate < dateOnly.AddDays(1))
                .OrderBy(r => r.CreatedAt)
                .ToListAsync();
            return Result<List<CreditorRepayment>>.Ok(repayments);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get creditor repayments for {Date}", date);
            return Result<List<CreditorRepayment>>.Fail($"Failed to load repayments: {ex.Message}");
        }
    }

    public async Task<Result<List<CreditorRepayment>>> GetByMonthAsync(int year, int month)
    {
        try
        {
            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1);

            var repayments = await _context.CreditorRepayments
                .Where(r => r.RepaymentDate >= startDate && r.RepaymentDate < endDate)
                .OrderBy(r => r.RepaymentDate)
                .ToListAsync();
            return Result<List<CreditorRepayment>>.Ok(repayments);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get creditor repayments for month {Year}-{Month}", year, month);
            return Result<List<CreditorRepayment>>.Fail($"Failed to load repayments: {ex.Message}");
        }
    }

    public async Task<Result<CreditorRepayment>> AddAsync(CreditorRepayment repayment)
    {
        try
        {
            repayment.CreatedAt = DateTime.Now;
            _context.CreditorRepayments.Add(repayment);
            await _context.SaveChangesAsync();
            return Result<CreditorRepayment>.Ok(repayment);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add creditor repayment");
            return Result<CreditorRepayment>.Fail($"Failed to add repayment: {ex.Message}");
        }
    }

    public async Task<Result> DeleteAsync(int id)
    {
        try
        {
            var repayment = await _context.CreditorRepayments.FindAsync(id);
            if (repayment == null) return Result.Fail("Repayment not found");
            _context.CreditorRepayments.Remove(repayment);
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete creditor repayment {Id}", id);
            return Result.Fail($"Failed to delete repayment: {ex.Message}");
        }
    }

    public async Task<Result<List<CreditorRepayment>>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            var start = startDate.Date;
            var end = endDate.Date;
            var repayments = await _context.CreditorRepayments
                .Where(r => r.RepaymentDate >= start && r.RepaymentDate < end.AddDays(1))
                .OrderBy(r => r.RepaymentDate)
                .ToListAsync();
            return Result<List<CreditorRepayment>>.Ok(repayments);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get creditor repayments for range");
            return Result<List<CreditorRepayment>>.Fail($"Failed to load repayments: {ex.Message}");
        }
    }
}

public class AgsImportRepository : IAgsImportRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<AgsImportRepository>();

    public AgsImportRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<AgsShiftImport>> SaveShiftImportAsync(AgsShiftImport import)
    {
        try
        {
            import.ImportedAt = DateTime.Now;
            if (!string.IsNullOrEmpty(import.ShiftType))
            {
                import.ShiftType = import.ShiftType == "A" ? "I" : import.ShiftType == "B" ? "II" : import.ShiftType == "C" ? "III" : import.ShiftType;
            }
            if (import.AgsShiftImportId == 0)
                _context.AgsShiftImports.Add(import);
            else
                _context.AgsShiftImports.Update(import);
            await _context.SaveChangesAsync();
            return Result<AgsShiftImport>.Ok(import);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save AGS shift import");
            return Result<AgsShiftImport>.Fail($"Failed to save import: {ex.Message}");
        }
    }

    public async Task<Result<AgsShiftImport?>> GetActiveShiftImportAsync(DateTime date, string shiftType)
    {
        try
        {
            var dateOnly = date.Date;
            var altShift = shiftType == "A" ? "I" : (shiftType == "B" ? "II" : (shiftType == "C" ? "III" : (shiftType == "I" ? "A" : (shiftType == "II" ? "B" : (shiftType == "III" ? "C" : shiftType)))));
            var record = await _context.AgsShiftImports
                .Include(x => x.NozzleReadings)
                .Include(x => x.TankStocks)
                .FirstOrDefaultAsync(x => x.ImportDate == dateOnly && (x.ShiftType == shiftType || x.ShiftType == altShift) && x.IsActive);
            return Result<AgsShiftImport?>.Ok(record);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get AGS shift import for {Date} {Shift}", date, shiftType);
            return Result<AgsShiftImport?>.Fail($"Failed to load import: {ex.Message}");
        }
    }

    public async Task<Result<List<AgsShiftImport>>> GetShiftsForDateAsync(DateTime date)
    {
        try
        {
            var dateOnly = date.Date;
            var records = await _context.AgsShiftImports
                .Include(x => x.NozzleReadings)
                .Include(x => x.TankStocks)
                .Where(x => x.ImportDate == dateOnly && x.IsActive)
                .OrderBy(x => x.ShiftType)
                .ToListAsync();
            return Result<List<AgsShiftImport>>.Ok(records);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get AGS shifts for {Date}", date);
            return Result<List<AgsShiftImport>>.Fail($"Failed to load shifts: {ex.Message}");
        }
    }

    public async Task<Result> SoftDeleteShiftImportAsync(int agsShiftImportId)
    {
        try
        {
            var record = await _context.AgsShiftImports.FindAsync(agsShiftImportId);
            if (record == null) return Result.Fail("AGS import not found");
            record.IsActive = false;
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to soft-delete AGS import {Id}", agsShiftImportId);
            return Result.Fail($"Failed to delete import: {ex.Message}");
        }
    }

    public async Task<Result<AgsDailySummary>> SaveDailySummaryAsync(AgsDailySummary summary)
    {
        try
        {
            summary.LastUpdatedAt = DateTime.Now;
            var existing = await _context.AgsDailySummaries
                .FirstOrDefaultAsync(x => x.SummaryDate == summary.SummaryDate.Date);

            if (existing != null)
            {
                // Update all fields in-place
                existing.DayTotalHsdLitres   = summary.DayTotalHsdLitres;
                existing.DayTotalMsILitres   = summary.DayTotalMsILitres;
                existing.DayTotalMsIILitres  = summary.DayTotalMsIILitres;
                existing.HsdDayOpeningStock  = summary.HsdDayOpeningStock;
                existing.HsdDayClosingStock  = summary.HsdDayClosingStock;
                existing.MsIDayOpeningStock  = summary.MsIDayOpeningStock;
                existing.MsIDayClosingStock  = summary.MsIDayClosingStock;
                existing.MsIIDayOpeningStock = summary.MsIIDayOpeningStock;
                existing.MsIIDayClosingStock = summary.MsIIDayClosingStock;
                existing.ShiftAImported      = summary.ShiftAImported;
                existing.ShiftBImported      = summary.ShiftBImported;
                existing.ShiftCImported      = summary.ShiftCImported;
                existing.NozzleDaySalesJson  = summary.NozzleDaySalesJson;
                existing.ShiftBreakdownJson  = summary.ShiftBreakdownJson;
                existing.LastUpdatedAt       = summary.LastUpdatedAt;
                await _context.SaveChangesAsync();
                try
                {
                    await TankDailyStockRepository.UpdateTankDailyStocksStaticAsync(_context, summary.SummaryDate.Date, existing);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to update tank stocks after updating summary");
                }
                return Result<AgsDailySummary>.Ok(existing);
            }
            else
            {
                _context.AgsDailySummaries.Add(summary);
                await _context.SaveChangesAsync();
                try
                {
                    await TankDailyStockRepository.UpdateTankDailyStocksStaticAsync(_context, summary.SummaryDate.Date, summary);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to update tank stocks after adding summary");
                }
                return Result<AgsDailySummary>.Ok(summary);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save AGS daily summary for {Date}", summary.SummaryDate);
            return Result<AgsDailySummary>.Fail($"Failed to save summary: {ex.Message}");
        }
    }

    public async Task<Result<AgsDailySummary?>> GetDailySummaryAsync(DateTime date)
    {
        try
        {
            var dateOnly = date.Date;
            var record = await _context.AgsDailySummaries
                .FirstOrDefaultAsync(x => x.SummaryDate == dateOnly);
            return Result<AgsDailySummary?>.Ok(record);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get AGS daily summary for {Date}", date);
            return Result<AgsDailySummary?>.Fail($"Failed to load summary: {ex.Message}");
        }
    }

    public async Task<Result<List<AgsShiftImport>>> GetImportHistoryAsync(int count = 30)
    {
        try
        {
            var records = await _context.AgsShiftImports
                .Where(x => x.IsActive)
                .OrderByDescending(x => x.ImportDate)
                .ThenBy(x => x.ShiftType)
                .Take(count)
                .ToListAsync();
            return Result<List<AgsShiftImport>>.Ok(records);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get AGS import history");
            return Result<List<AgsShiftImport>>.Fail($"Failed to load history: {ex.Message}");
        }
    }

    public async Task<Result<List<AgsShiftImport>>> GetActiveImportsFromDateAsync(DateTime startDate)
    {
        try
        {
            var dateOnly = startDate.Date;
            var records = await _context.AgsShiftImports
                .Include(x => x.NozzleReadings)
                .Include(x => x.TankStocks)
                .Where(x => x.IsActive && x.ImportDate >= dateOnly)
                .ToListAsync();
            return Result<List<AgsShiftImport>>.Ok(records);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get active imports from date {Date}", startDate);
            return Result<List<AgsShiftImport>>.Fail(ex.Message);
        }
    }
}

public class CreditorRepository : ICreditorRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<CreditorRepository>();

    public CreditorRepository(FuelProDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<Creditor>>> GetAllAsync()
    {
        try
        {
            var items = await _context.Creditors.OrderBy(c => c.Name).ToListAsync();
            return Result<List<Creditor>>.Ok(items);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get all creditors");
            return Result<List<Creditor>>.Fail($"Failed to load creditors: {ex.Message}");
        }
    }

    public async Task<Result<List<Creditor>>> GetAllActiveAsync()
    {
        try
        {
            var items = await _context.Creditors.Where(c => c.IsActive).OrderBy(c => c.Name).ToListAsync();
            return Result<List<Creditor>>.Ok(items);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get active creditors");
            return Result<List<Creditor>>.Fail($"Failed to load active creditors: {ex.Message}");
        }
    }

    public async Task<Result<List<Creditor>>> GetAllActiveWithVehiclesAsync()
    {
        try
        {
            var items = await _context.Creditors
                .Include(c => c.Vehicles.Where(v => v.IsActive))
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync();
            return Result<List<Creditor>>.Ok(items);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get active creditors with vehicles");
            return Result<List<Creditor>>.Fail($"Failed to load creditors: {ex.Message}");
        }
    }

    public async Task<Result<Creditor>> AddAsync(Creditor creditor)
    {
        try
        {
            var existing = await _context.Creditors
                .FirstOrDefaultAsync(c => c.Name.ToLower() == creditor.Name.ToLower());
                
            if (existing != null)
            {
                if (!existing.IsActive)
                {
                    existing.IsActive = true;
                    existing.Phone = creditor.Phone;
                    await _context.SaveChangesAsync();
                    return Result<Creditor>.Ok(existing);
                }
                return Result<Creditor>.Fail("A creditor with this name already exists.");
            }

            _context.Creditors.Add(creditor);
            await _context.SaveChangesAsync();
            return Result<Creditor>.Ok(creditor);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add creditor: {Name}", creditor.Name);
            return Result<Creditor>.Fail($"Failed to add creditor: {ex.Message}");
        }
    }

    public async Task<Result> UpdateAsync(Creditor creditor)
    {
        try
        {
            var nameConflict = await _context.Creditors
                .AnyAsync(c => c.CreditorId != creditor.CreditorId && c.Name.ToLower() == creditor.Name.ToLower() && c.IsActive);
            if (nameConflict)
            {
                return Result.Fail("A creditor with this name already exists.");
            }

            var tracked = _context.Creditors.Local.FirstOrDefault(c => c.CreditorId == creditor.CreditorId);
            if (tracked != null)
            {
                _context.Entry(tracked).CurrentValues.SetValues(creditor);
            }
            else
            {
                _context.Creditors.Update(creditor);
            }
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update creditor: {Id}", creditor.CreditorId);
            return Result.Fail($"Failed to update creditor: {ex.Message}");
        }
    }

    public async Task<Result> SoftDeleteAsync(int creditorId)
    {
        try
        {
            var c = await _context.Creditors.FindAsync(creditorId);
            if (c == null) return Result.Fail("Creditor not found.");
            
            c.IsActive = false;
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to soft delete creditor: {Id}", creditorId);
            return Result.Fail($"Failed to delete creditor: {ex.Message}");
        }
    }
}

public class PumpExpenseRepository : IPumpExpenseRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<PumpExpenseRepository>();

    public PumpExpenseRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<List<PumpExpense>>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            var list = await _context.PumpExpenses
                .Where(e => e.ExpenseDate >= startDate.Date && e.ExpenseDate <= endDate.Date)
                .OrderBy(e => e.ExpenseDate)
                .ToListAsync();
            return Result<List<PumpExpense>>.Ok(list);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get pump expenses by date range");
            return Result<List<PumpExpense>>.Fail($"Failed to load pump expenses: {ex.Message}");
        }
    }

    public async Task<Result<PumpExpense>> GetByDateAsync(DateTime date)
    {
        try
        {
            var item = await _context.PumpExpenses
                .FirstOrDefaultAsync(e => e.ExpenseDate == date.Date);
            return item != null 
                ? Result<PumpExpense>.Ok(item) 
                : Result<PumpExpense>.Fail("No expense entry for this date.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get pump expense by date");
            return Result<PumpExpense>.Fail($"Failed to load pump expense: {ex.Message}");
        }
    }

    public async Task<Result<PumpExpense>> AddOrUpdateAsync(PumpExpense expense)
    {
        try
        {
            var existing = await _context.PumpExpenses
                .FirstOrDefaultAsync(e => e.ExpenseDate == expense.ExpenseDate.Date);

            if (existing != null)
            {
                existing.Rent = expense.Rent;
                existing.Salary = expense.Salary;
                existing.TripSheetLoss = expense.TripSheetLoss;
                existing.DsmShort = expense.DsmShort;
                existing.BankingExpenses = expense.BankingExpenses;
                existing.BpclPortalExpenses = expense.BpclPortalExpenses;
                existing.FuelAndTravel = expense.FuelAndTravel;
                existing.OilPurchase = expense.OilPurchase;
                existing.RepairsAndMaintenance = expense.RepairsAndMaintenance;
                existing.ElectricityExpenses = expense.ElectricityExpenses;
                existing.OfficeExpenses = expense.OfficeExpenses;
                existing.PrintingExpense = expense.PrintingExpense;
                existing.OtherDescription = expense.OtherDescription;
                existing.OtherAmount = expense.OtherAmount;
                existing.Remarks = expense.Remarks;
                
                _context.PumpExpenses.Update(existing);
                await _context.SaveChangesAsync();
                return Result<PumpExpense>.Ok(existing);
            }
            else
            {
                expense.ExpenseDate = expense.ExpenseDate.Date;
                expense.CreatedAt = DateTime.Now;
                _context.PumpExpenses.Add(expense);
                await _context.SaveChangesAsync();
                return Result<PumpExpense>.Ok(expense);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save pump expense");
            return Result<PumpExpense>.Fail($"Failed to save pump expense: {ex.Message}");
        }
    }

    public async Task<Result> DeleteAsync(int id)
    {
        try
        {
            var existing = await _context.PumpExpenses.FindAsync(id);
            if (existing != null)
            {
                _context.PumpExpenses.Remove(existing);
                await _context.SaveChangesAsync();
            }
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete pump expense");
            return Result.Fail($"Failed to delete: {ex.Message}");
        }
    }
}

public class DebtorVehicleRepository : IDebtorVehicleRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<DebtorVehicleRepository>();

    public DebtorVehicleRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<List<DebtorVehicle>>> GetByCreditorIdAsync(int creditorId)
    {
        try
        {
            var vehicles = await _context.DebtorVehicles
                .Where(v => v.CreditorId == creditorId && v.IsActive)
                .OrderBy(v => v.VehicleNumber)
                .ToListAsync();
            return Result<List<DebtorVehicle>>.Ok(vehicles);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get vehicles for creditor {Id}", creditorId);
            return Result<List<DebtorVehicle>>.Fail($"Failed to load vehicles: {ex.Message}");
        }
    }

    public async Task<Result<DebtorVehicle>> AddAsync(DebtorVehicle vehicle)
    {
        try
        {
            vehicle.CreatedAt = DateTime.Now;
            _context.DebtorVehicles.Add(vehicle);
            await _context.SaveChangesAsync();
            return Result<DebtorVehicle>.Ok(vehicle);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add vehicle for creditor {Id}", vehicle.CreditorId);
            return Result<DebtorVehicle>.Fail($"Failed to add vehicle: {ex.Message}");
        }
    }

    public async Task<Result> DeleteAsync(int debtorVehicleId)
    {
        try
        {
            var vehicle = await _context.DebtorVehicles.FindAsync(debtorVehicleId);
            if (vehicle == null) return Result.Fail("Vehicle not found");
            vehicle.IsActive = false;
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete vehicle {Id}", debtorVehicleId);
            return Result.Fail($"Failed to delete vehicle: {ex.Message}");
        }
    }

    public async Task<Result> UpdateVehicleNumberAsync(int vehicleId, string vehicleNumber)
    {
        try
        {
            var vehicle = await _context.DebtorVehicles.FindAsync(vehicleId);
            if (vehicle == null) return Result.Fail("Vehicle not found");
            vehicle.VehicleNumber = vehicleNumber;
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update vehicle {Id}", vehicleId);
            return Result.Fail($"Failed to update vehicle: {ex.Message}");
        }
    }
}

public class DsmPersonalDebtorRepository : IDsmPersonalDebtorRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<DsmPersonalDebtorRepository>();

    public DsmPersonalDebtorRepository(FuelProDbContext context)
    {
        _context = context;
    }

    public async Task<Result<List<DsmPersonalDebtor>>> GetByDsmEntryIdAsync(int dsmEntryId)
    {
        try
        {
            var list = await _context.DsmPersonalDebtors
                .Where(d => d.DsmEntryId == dsmEntryId)
                .ToListAsync();
            return Result<List<DsmPersonalDebtor>>.Ok(list);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get personal debtors for entry {EntryId}", dsmEntryId);
            return Result<List<DsmPersonalDebtor>>.Fail(ex.Message);
        }
    }

    public async Task<Result> SavePersonalDebtorsAsync(int dsmEntryId, List<DsmPersonalDebtor> personalDebtors)
    {
        try
        {
            // 1. Remove existing ones
            var existing = await _context.DsmPersonalDebtors
                .Where(d => d.DsmEntryId == dsmEntryId)
                .ToListAsync();
            _context.DsmPersonalDebtors.RemoveRange(existing);

            // 2. Set Entry ID and DsmName / Date / Time
            var entry = await _context.DsmEntries.FindAsync(dsmEntryId);
            if (entry != null)
            {
                foreach (var pd in personalDebtors)
                {
                    pd.DsmEntryId = dsmEntryId;
                    pd.DsmName = entry.DsmName;
                    
                    // Fetch shift date if needed
                    var shift = await _context.Shifts.FindAsync(entry.ShiftId);
                    if (shift != null)
                    {
                        pd.Date = shift.ShiftDate;
                    }
                }
            }

            // 3. Add new ones
            _context.DsmPersonalDebtors.AddRange(personalDebtors);
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save personal debtors for entry {EntryId}", dsmEntryId);
            return Result.Fail($"Failed to save personal debtors: {ex.Message}");
        }
    }
}


public class FuelTankerRepository : IFuelTankerRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<FuelTankerRepository>();

    public FuelTankerRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<List<FuelTanker>>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            var list = await _context.FuelTankers
                .Where(t => t.TankerDate.Date >= startDate.Date && t.TankerDate.Date <= endDate.Date)
                .OrderByDescending(t => t.TankerDate)
                .ToListAsync();
            return Result<List<FuelTanker>>.Ok(list);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get fuel tankers");
            return Result<List<FuelTanker>>.Fail($"Failed to load tankers: {ex.Message}");
        }
    }

    public async Task<Result<FuelTanker>> AddAsync(FuelTanker tanker)
    {
        try
        {
            _context.FuelTankers.Add(tanker);
            await _context.SaveChangesAsync();
            return Result<FuelTanker>.Ok(tanker);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add fuel tanker");
            return Result<FuelTanker>.Fail($"Failed to add tanker: {ex.Message}");
        }
    }

    public async Task<Result<FuelTanker>> UpdateAsync(FuelTanker tanker)
    {
        try
        {
            _context.FuelTankers.Update(tanker);
            await _context.SaveChangesAsync();
            return Result<FuelTanker>.Ok(tanker);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to update fuel tanker");
            return Result<FuelTanker>.Fail($"Failed to update tanker: {ex.Message}");
        }
    }

    public async Task<Result> DeleteAsync(int fuelTankerId)
    {
        try
        {
            var tanker = await _context.FuelTankers.FindAsync(fuelTankerId);
            if (tanker == null)
                return Result.Fail("Tanker not found.");
            _context.FuelTankers.Remove(tanker);
            await _context.SaveChangesAsync();
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to delete fuel tanker {Id}", fuelTankerId);
            return Result.Fail($"Failed to delete tanker: {ex.Message}");
        }
    }
}

public class TankDailyStockRepository : ITankDailyStockRepository
{
    private readonly FuelProDbContext _context;
    private readonly ILogger _logger = Log.ForContext<TankDailyStockRepository>();

    public TankDailyStockRepository(FuelProDbContext context) => _context = context;

    public async Task<Result<List<TankDailyStock>>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
    {
        try
        {
            // Rebuild/backfill if TankDailyStocks table is empty but daily summaries exist
            var hasSummaries = await _context.AgsDailySummaries.AnyAsync();
            var hasStocks = await _context.TankDailyStocks.AnyAsync();
            if (hasSummaries && !hasStocks)
            {
                _logger.Information("Rebuilding TankDailyStocks from AgsDailySummaries...");
                var summaries = await _context.AgsDailySummaries.ToListAsync();
                foreach (var sum in summaries)
                {
                    await UpdateTankDailyStocksStaticAsync(_context, sum.SummaryDate, sum);
                }
            }

            var list = await _context.TankDailyStocks
                .Where(s => s.Date.Date >= startDate.Date && s.Date.Date <= endDate.Date)
                .OrderByDescending(s => s.Date)
                .ToListAsync();
            return Result<List<TankDailyStock>>.Ok(list);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get tank daily stocks");
            return Result<List<TankDailyStock>>.Fail($"Failed to load stocks: {ex.Message}");
        }
    }

    public static async Task UpdateTankDailyStocksStaticAsync(FuelProDbContext context, DateTime date, AgsDailySummary summary)
    {
        try
        {
            var activeImports = await context.AgsShiftImports
                .Include(i => i.TankStocks)
                .Where(i => i.ImportDate.Date == date.Date && i.IsActive)
                .ToListAsync();

            var dsmEntries = await context.DsmEntries
                .Include(e => e.TestingEntries)
                .Where(e => e.Shift != null && e.Shift.ShiftDate.Date == date.Date)
                .ToListAsync();

            double msITesting = 0;
            double hsdTesting = 0;
            double msIITesting = 0;

            foreach (var entry in dsmEntries)
            {
                foreach (var t in entry.TestingEntries)
                {
                    var cat = PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, date.Date);
                    if (cat == "MS") msITesting += t.Litres;
                    else if (cat == "HSD") hsdTesting += t.Litres;
                    else if (cat == "HSD-II") msIITesting += t.Litres;
                }
            }

            var fuelTypes = new[] { "HSD", "MS-I", "MS-II" };
            var shiftB = activeImports.FirstOrDefault(i => i.ShiftType == "B");
            var shiftA = activeImports.FirstOrDefault(i => i.ShiftType == "A");
            var lastImport = shiftA ?? shiftB;

            int? shiftId = null;
            if (lastImport != null)
            {
                var dbShift = await context.Shifts
                    .FirstOrDefaultAsync(s => s.ShiftDate.Date == date.Date && s.ShiftType == lastImport.ShiftType);
                shiftId = dbShift?.ShiftId;
            }

            foreach (var ft in fuelTypes)
            {
                var stocks = activeImports.SelectMany(i => i.TankStocks).Where(t => t.FuelType == ft).ToList();

                double opening = 0;
                double closing = 0;
                double sale = 0;
                double purchased = 0;
                double dip = 0;
                double manual = 0;
                double testing = ft switch
                {
                    "HSD" => hsdTesting,
                    "MS-I" => msITesting,
                    "MS-II" => msIITesting,
                    _ => 0
                };

                if (stocks.Any())
                {
                    opening = shiftB?.TankStocks?.FirstOrDefault(t => t.FuelType == ft)?.OpeningStockLitres 
                           ?? shiftA?.TankStocks?.FirstOrDefault(t => t.FuelType == ft)?.OpeningStockLitres 
                           ?? 0;
                    closing = shiftA?.TankStocks?.FirstOrDefault(t => t.FuelType == ft)?.ClosingStockLitres 
                           ?? shiftB?.TankStocks?.FirstOrDefault(t => t.FuelType == ft)?.ClosingStockLitres 
                           ?? 0;
                    sale = stocks.Sum(t => t.FuelDispensedLitres);
                    purchased = stocks.Sum(t => t.ReceiptLitres);
                    dip = shiftA?.TankStocks?.FirstOrDefault(t => t.FuelType == ft)?.ClosingDipMM 
                       ?? shiftB?.TankStocks?.FirstOrDefault(t => t.FuelType == ft)?.ClosingDipMM 
                       ?? 0;
                    manual = closing;
                }
                else
                {
                    // Fallback to daily summary values if TankStocks are empty
                    opening = ft switch
                    {
                        "HSD" => summary.HsdDayOpeningStock,
                        "MS-I" => summary.MsIDayOpeningStock,
                        "MS-II" => summary.MsIIDayOpeningStock,
                        _ => 0
                    };
                    closing = ft switch
                    {
                        "HSD" => summary.HsdDayClosingStock,
                        "MS-I" => summary.MsIDayClosingStock,
                        "MS-II" => summary.MsIIDayClosingStock,
                        _ => 0
                    };
                    sale = ft switch
                    {
                        "HSD" => summary.DayTotalHsdLitres,
                        "MS-I" => summary.DayTotalMsILitres,
                        "MS-II" => summary.DayTotalMsIILitres,
                        _ => 0
                    };
                    manual = closing;
                }

                var existingStock = await context.TankDailyStocks
                    .FirstOrDefaultAsync(s => s.Date.Date == date.Date && s.FuelType == ft);

                if (existingStock != null)
                {
                    existingStock.OpeningStock = opening;
                    existingStock.DaySaleLitres = sale;
                    existingStock.TestingLitres = testing;
                    existingStock.PurchasedLitres = purchased;
                    existingStock.ClosingStock = closing;
                    existingStock.DipMm = dip;
                    existingStock.ManualStock = manual;
                    existingStock.ShiftId = shiftId;
                    existingStock.LastUpdated = DateTime.Now;
                    context.TankDailyStocks.Update(existingStock);
                }
                else
                {
                    var newStock = new TankDailyStock
                    {
                        Date = date.Date,
                        FuelType = ft,
                        OpeningStock = opening,
                        DaySaleLitres = sale,
                        TestingLitres = testing,
                        PurchasedLitres = purchased,
                        ClosingStock = closing,
                        DipMm = dip,
                        ManualStock = manual,
                        ShiftId = shiftId,
                        LastUpdated = DateTime.Now
                    };
                    context.TankDailyStocks.Add(newStock);
                }
            }

            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update TankDailyStocks for date {Date}", date);
        }
    }

    public async Task<Result<List<TankDailyStock>>> GetByDateAsync(DateTime date)
    {
        try
        {
            var list = await _context.TankDailyStocks
                .Where(s => s.Date.Date == date.Date)
                .ToListAsync();
            return Result<List<TankDailyStock>>.Ok(list);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get tank daily stock for date {Date}", date);
            return Result<List<TankDailyStock>>.Fail($"Failed to load stock: {ex.Message}");
        }
    }

    public async Task<Result<TankDailyStock>> UpsertAsync(TankDailyStock stock)
    {
        try
        {
            var existing = await _context.TankDailyStocks
                .FirstOrDefaultAsync(s => s.Date.Date == stock.Date.Date && s.FuelType == stock.FuelType);

            if (existing != null)
            {
                existing.OpeningStock = stock.OpeningStock;
                existing.DaySaleLitres = stock.DaySaleLitres;
                existing.TestingLitres = stock.TestingLitres;
                existing.PurchasedLitres = stock.PurchasedLitres;
                existing.ClosingStock = stock.ClosingStock;
                existing.DipMm = stock.DipMm;
                existing.ManualStock = stock.ManualStock;
                existing.LastUpdated = DateTime.Now;
                _context.TankDailyStocks.Update(existing);
                await _context.SaveChangesAsync();
                return Result<TankDailyStock>.Ok(existing);
            }
            else
            {
                _context.TankDailyStocks.Add(stock);
                await _context.SaveChangesAsync();
                return Result<TankDailyStock>.Ok(stock);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to upsert tank daily stock");
            return Result<TankDailyStock>.Fail($"Failed to save stock: {ex.Message}");
        }
    }
}
