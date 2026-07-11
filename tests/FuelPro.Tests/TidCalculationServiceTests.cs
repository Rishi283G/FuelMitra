using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using Xunit;

namespace FuelPro.Tests;

public class TidCalculationServiceTests
{
    [Fact]
    public async Task GetTidSheetAsync_AggregatesCorrectlyBasedOnOperationalBusinessDay()
    {
        // Arrange
        var testDate = new DateTime(2026, 7, 3);
        var yesterday = testDate.AddDays(-1);

        // We create:
        // 1. Yesterday (July 2) Shift A Morning fields:
        //    - PhonePeMorning = 1000
        //    - PhonePeCardMorning = 200
        //    - CreditCardMorning = 300
        //    - PetroCardMorning = 400
        var yesterdayShiftA = new Shift { ShiftId = 1, ShiftDate = yesterday, ShiftType = "A" };
        var entry1 = new DsmEntry
        {
            Shift = yesterdayShiftA,
            ShiftId = 1,
            PaymentCollection = new PaymentCollection
            {
                PhonePeMorning = 1000,
                PhonePeCardMorning = 200,
                CreditCardMorning = 300,
                PetroCardMorning = 400
            }
        };

        // 2. Today (July 3) Shift B Morning fields (which represents Day shift collections):
        //    - PhonePeMorning = 5000
        //    - PhonePeCardMorning = 1200
        //    - CreditCardMorning = 1500
        //    - PetroCardMorning = 800
        var todayShiftB = new Shift { ShiftId = 2, ShiftDate = testDate, ShiftType = "B" };
        var entry2 = new DsmEntry
        {
            Shift = todayShiftB,
            ShiftId = 2,
            PaymentCollection = new PaymentCollection
            {
                PhonePeMorning = 5000,
                PhonePeCardMorning = 1200,
                CreditCardMorning = 1500,
                PetroCardMorning = 800
            }
        };

        // 3. Today (July 3) Shift A Night fields:
        //    - PhonePeNight = 3000
        //    - PhonePeCardNight = 500
        //    - CreditCardNight = 700
        //    - PetroCardNight = 900
        var todayShiftA = new Shift { ShiftId = 3, ShiftDate = testDate, ShiftType = "A" };
        var entry3 = new DsmEntry
        {
            Shift = todayShiftA,
            ShiftId = 3,
            PaymentCollection = new PaymentCollection
            {
                PhonePeNight = 3000,
                PhonePeCardNight = 500,
                CreditCardNight = 700,
                PetroCardNight = 900
            }
        };

        var shifts = new List<Shift> { yesterdayShiftA, todayShiftA, todayShiftB };
        var entries = new List<DsmEntry> { entry1, entry2, entry3 };

        var mockShiftRepo = new MockShiftRepository(shifts);
        var mockDsmRepo = new MockDsmEntryRepository(entries);
        var sut = new TidCalculationService(mockShiftRepo, mockDsmRepo);

        // Act
        var sheet = await sut.GetTidSheetAsync(testDate);

        // Assert
        // Morning Slot (yesterday Shift B)
        Assert.Equal(1000, sheet.PhonePeDirectMorning);
        Assert.Equal(200, sheet.PhonePeCardMorning);
        Assert.Equal(300, sheet.PineLabsCardMorning);
        Assert.Equal(400, sheet.PetroCardMorning);

        // Day Slot (today Shift A)
        Assert.Equal(5000, sheet.PhonePeDirectDay);
        Assert.Equal(1200, sheet.PhonePeCardDay);
        Assert.Equal(1500, sheet.PineLabsCardDay);
        Assert.Equal(800, sheet.PetroCardDay);

        // Night Slot (today Shift B)
        Assert.Equal(3000, sheet.PhonePeDirectNight);
        Assert.Equal(500, sheet.PhonePeCardNight);
        Assert.Equal(700, sheet.PineLabsCardNight);
        Assert.Equal(900, sheet.PetroCardNight);

        // Totals
        Assert.Equal(9000, sheet.PhonePeDirectTotal);
        Assert.Equal(1900, sheet.PhonePeCardTotal);
        Assert.Equal(10900, sheet.PhonePeTotal);
        Assert.Equal(2500, sheet.PineLabsCardTotal);
        Assert.Equal(2100, sheet.PetroCardTotal);
    }

    private class MockShiftRepository : IShiftRepository
    {
        private readonly List<Shift> _shifts;

        public MockShiftRepository(List<Shift> shifts)
        {
            _shifts = shifts;
        }

        public Task<Result<Shift>> GetShiftAsync(DateTime date, string shiftType)
        {
            var shift = _shifts.FirstOrDefault(s => s.ShiftDate.Date == date.Date && s.ShiftType == shiftType);
            return Task.FromResult(shift != null ? Result<Shift>.Ok(shift) : Result<Shift>.Fail("Not found"));
        }

        public Task<Result<Shift>> GetShiftByIdAsync(int shiftId)
        {
            var shift = _shifts.FirstOrDefault(s => s.ShiftId == shiftId);
            return Task.FromResult(shift != null ? Result<Shift>.Ok(shift) : Result<Shift>.Fail("Not found"));
        }

        public Task<Result<Shift>> GetOrCreateShiftAsync(DateTime date, string shiftType) => throw new NotImplementedException();
        public Task<Result<List<Shift>>> GetShiftsForDateAsync(DateTime date) => throw new NotImplementedException();
        public Task<Result> LockShiftAsync(int shiftId) => throw new NotImplementedException();
        public Task<Result<bool>> IsShiftLockedAsync(int shiftId) => throw new NotImplementedException();
        public Task<Result<List<Shift>>> GetShiftsByDateRangeAsync(DateTime startDate, DateTime endDate) => throw new NotImplementedException();
        public Task<Result> UpdateShiftAsync(Shift shift) => throw new NotImplementedException();
    }

    private class MockDsmEntryRepository : IDsmEntryRepository
    {
        private readonly List<DsmEntry> _entries;

        public MockDsmEntryRepository(List<DsmEntry> entries)
        {
            _entries = entries;
        }

        public Task<Result<List<DsmEntry>>> GetEntriesForShiftAsync(int shiftId)
        {
            var list = _entries.Where(e => e.ShiftId == shiftId).ToList();
            return Task.FromResult(Result<List<DsmEntry>>.Ok(list));
        }

        public Task<Result<List<DsmEntry>>> GetEntriesForDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            var list = _entries.Where(e => e.Shift != null && e.Shift.ShiftDate.Date >= startDate.Date && e.Shift.ShiftDate.Date <= endDate.Date).ToList();
            return Task.FromResult(Result<List<DsmEntry>>.Ok(list));
        }

        public Task<Result<DsmEntry>> GetByIdAsync(int dsmEntryId) => throw new NotImplementedException();
        public Task<Result<DsmEntry>> GetFullEntryAsync(int dsmEntryId) => throw new NotImplementedException();
        public Task<Result<DsmEntry>> SaveEntryAsync(DsmEntry entry) => throw new NotImplementedException();
        public Task<Result> DeleteEntryAsync(int dsmEntryId) => throw new NotImplementedException();
        public Task<Result<bool>> IsDuplicateAsync(int shiftId, int pumpId, string dsmName, int? excludeId = null) => throw new NotImplementedException();
        public Task<Result<List<string>>> GetDistinctDsmNamesAsync() => throw new NotImplementedException();
        public Task<Result<List<DsmEntry>>> GetEntriesForDsmAndMonthAsync(string dsmName, int year, int month) => throw new NotImplementedException();
        public Task<Result<List<DsmEntry>>> GetEntriesForMonthAsync(int year, int month) => throw new NotImplementedException();
    }
}
