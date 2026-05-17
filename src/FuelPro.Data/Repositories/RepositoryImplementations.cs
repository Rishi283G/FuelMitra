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
            var settings = await _context.Settings.FirstOrDefaultAsync();
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
            var shift = await _context.Shifts
                .FirstOrDefaultAsync(s => s.ShiftDate == dateOnly && s.ShiftType == shiftType);

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
            var shift = await _context.Shifts
                .Include(s => s.DsmEntries)
                .FirstOrDefaultAsync(s => s.ShiftDate == dateOnly && s.ShiftType == shiftType);

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

    public async Task<Result<Dictionary<int, double>>> GetPreviousShiftClosingsAsync(DateTime date, string shiftType, int pumpId)
    {
        try
        {
            var targetDate = date.Date;
            var closings = await _context.NozzleReadings
                .Include(r => r.DsmEntry)
                .ThenInclude(e => e!.Shift)
                .Where(r => r.DsmEntry != null
                    && r.DsmEntry.PumpId == pumpId
                    && r.DsmEntry.Shift != null
                    && (r.DsmEntry.Shift.ShiftDate < targetDate || 
                       (r.DsmEntry.Shift.ShiftDate == targetDate && r.DsmEntry.Shift.ShiftType.CompareTo(shiftType) < 0)))
                .GroupBy(r => r.NozzleNumber)
                .Select(g => new { 
                    NozzleNumber = g.Key, 
                    Closing = g.OrderByDescending(x => x.DsmEntry!.Shift!.ShiftDate)
                               .ThenByDescending(x => x.DsmEntry!.Shift!.ShiftType)
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
            var existing = await _context.PaymentCollections
                .FirstOrDefaultAsync(p => p.DsmEntryId == payment.DsmEntryId);

            if (existing != null)
            {
                existing.PhonePeCardMorning = payment.PhonePeCardMorning;
                existing.PhonePeCardNight = payment.PhonePeCardNight;
                existing.PhonePeMorning = payment.PhonePeMorning;
                existing.PhonePeNight = payment.PhonePeNight;
                existing.CreditCardMorning = payment.CreditCardMorning;
                existing.CreditCardNight = payment.CreditCardNight;
                existing.PetroCard   = payment.PetroCard;
                existing.CashDeposit = payment.CashDeposit;
                existing.Others      = payment.Others;
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

            foreach (var d in debits) d.DsmEntryId = dsmEntryId;
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

            foreach (var e in entries) e.DsmEntryId = dsmEntryId;
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

            foreach (var e in expenses) e.DsmEntryId = dsmEntryId;
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
            _context.CashDenominations.RemoveRange(existing);

            foreach (var d in denominations)
            {
                d.DsmEntryId = dsmEntryId;
                d.RecalculateTotal();
            }
            _context.CashDenominations.AddRange(denominations);
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
            var entries = await _context.ShiftOtherCash
                .Where(e => e.ShiftDate == dateOnly && e.ShiftNumber == shiftNumber)
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
            var rates = await _context.ShiftFuelRates
                .Where(r => r.ShiftDate == dateOnly && r.ShiftNumber == shiftNumber)
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
            var existing = await _context.ShiftFuelRates
                .FirstOrDefaultAsync(r => r.ShiftDate == dateOnly
                    && r.ShiftNumber == rate.ShiftNumber
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
                .Where(r => r.RepaymentDate == dateOnly)
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
            var endDate = startDate.AddMonths(1).AddDays(-1);

            var repayments = await _context.CreditorRepayments
                .Where(r => r.RepaymentDate >= startDate && r.RepaymentDate <= endDate)
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
            var record = await _context.AgsShiftImports
                .Include(x => x.NozzleReadings)
                .Include(x => x.TankStocks)
                .FirstOrDefaultAsync(x => x.ImportDate == dateOnly && x.ShiftType == shiftType && x.IsActive);
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
                return Result<AgsDailySummary>.Ok(existing);
            }
            else
            {
                _context.AgsDailySummaries.Add(summary);
                await _context.SaveChangesAsync();
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
            _context.Creditors.Update(creditor);
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


