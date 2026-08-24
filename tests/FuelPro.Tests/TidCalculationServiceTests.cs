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
        var tomorrow = testDate.AddDays(1);

        // We create:
        // 1. Today (July 3) Shift A Morning fields:
        //    - PhonePeMorning = 1000
        //    - PhonePeCardMorning = 200
        //    - CreditCardMorning = 300
        //    - PetroCardMorning = 400
        var todayShiftA = new Shift { ShiftId = 1, ShiftDate = testDate, ShiftType = "A" };
        var entry1 = new DsmEntry
        {
            Shift = todayShiftA,
            ShiftId = 1,
            PaymentCollection = new PaymentCollection
            {
                PhonePeMorning = 1000,
                PhonePeCardMorning = 200,
                CreditCardMorning = 300,
                PetroCardMorning = 400
            }
        };

        // 2. Today (July 3) Shift B Day fields:
        //    - PhonePeDay = 5000
        //    - PhonePeCardDay = 1200
        //    - CreditCardDay = 1500
        //    - PetroCardDay = 800
        var todayShiftB = new Shift { ShiftId = 2, ShiftDate = testDate, ShiftType = "B" };
        var entry2 = new DsmEntry
        {
            Shift = todayShiftB,
            ShiftId = 2,
            PaymentCollection = new PaymentCollection
            {
                PhonePeDay = 5000,
                PhonePeCardDay = 1200,
                CreditCardDay = 1500,
                PetroCardDay = 800
            }
        };

        // 3. Tomorrow (July 4) Shift A Night fields:
        //    - PhonePeNight = 3000
        //    - PhonePeCardNight = 500
        //    - CreditCardNight = 700
        //    - PetroCardNight = 900
        var tomorrowShiftA = new Shift { ShiftId = 3, ShiftDate = tomorrow, ShiftType = "A" };
        var entry3 = new DsmEntry
        {
            Shift = tomorrowShiftA,
            ShiftId = 3,
            PaymentCollection = new PaymentCollection
            {
                PhonePeNight = 3000,
                PhonePeCardNight = 500,
                CreditCardNight = 700,
                PetroCardNight = 900
            }
        };

        var shifts = new List<Shift> { todayShiftA, todayShiftB, tomorrowShiftA };
        var entries = new List<DsmEntry> { entry1, entry2, entry3 };

        var mockShiftRepo = new MockShiftRepository(shifts);
        var mockDsmRepo = new MockDsmEntryRepository(entries);
        var mockRepaymentRepo = new MockCreditorRepaymentRepository();
        var mockPersonalDebtorRepo = new MockDsmPersonalDebtorRepository();
        var mockFeatureToggle = new MockFeatureToggleService(new Dictionary<string, bool>
        {
            { "Collection_UseMorningNight", true }
        });
        var sut = new TidCalculationService(mockShiftRepo, mockDsmRepo, mockRepaymentRepo, mockPersonalDebtorRepo, null, mockFeatureToggle);

        // Act
        var sheet = await sut.GetTidSheetAsync(testDate);

        // Assert
        // Morning Slot (today Shift A)
        Assert.Equal(1000, sheet.PhonePeDirectMorning);
        Assert.Equal(200, sheet.PhonePeCardMorning);
        Assert.Equal(300, sheet.PineLabsCardMorning);
        Assert.Equal(400, sheet.PetroCardMorning);

        // Day Slot (today Shift B)
        Assert.Equal(5000, sheet.PhonePeDirectDay);
        Assert.Equal(1200, sheet.PhonePeCardDay);
        Assert.Equal(1500, sheet.PineLabsCardDay);
        Assert.Equal(800, sheet.PetroCardDay);

        // Night Slot (tomorrow Shift A)
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

    [Fact]
    public async Task GetTidSheetAsync_TwoShiftCycle_AggregatesShiftAAndBDirectly()
    {
        // Arrange
        var testDate = new DateTime(2026, 7, 3);

        // Standard 2-Shift cycle: Shift A and Shift B on the same calendar day
        var todayShiftA = new Shift { ShiftId = 1, ShiftDate = testDate, ShiftType = "A" };
        var entry1 = new DsmEntry
        {
            Shift = todayShiftA,
            ShiftId = 1,
            PaymentCollection = new PaymentCollection
            {
                PhonePeMorning = 1500,
                CreditCardMorning = 800,
                PetroCardMorning = 400
            }
        };

        var todayShiftB = new Shift { ShiftId = 2, ShiftDate = testDate, ShiftType = "B" };
        var entry2 = new DsmEntry
        {
            Shift = todayShiftB,
            ShiftId = 2,
            PaymentCollection = new PaymentCollection
            {
                PhonePeDay = 2500,
                CreditCardDay = 1200,
                PetroCardDay = 600
            }
        };

        var shifts = new List<Shift> { todayShiftA, todayShiftB };
        var entries = new List<DsmEntry> { entry1, entry2 };

        var mockShiftRepo = new MockShiftRepository(shifts);
        var mockDsmRepo = new MockDsmEntryRepository(entries);
        var mockRepaymentRepo = new MockCreditorRepaymentRepository();
        var mockPersonalDebtorRepo = new MockDsmPersonalDebtorRepository();
        var mockFeatureToggle = new MockFeatureToggleService(new Dictionary<string, bool>
        {
            { "Collection_UseMorningNight", false }
        });
        var sut = new TidCalculationService(mockShiftRepo, mockDsmRepo, mockRepaymentRepo, mockPersonalDebtorRepo, null, mockFeatureToggle);

        // Act
        var sheet = await sut.GetTidSheetAsync(testDate);

        // Assert
        // Slot 1 (Shift A)
        Assert.Equal(1500, sheet.PhonePeDirectMorning);
        Assert.Equal(800, sheet.PineLabsCardMorning);
        Assert.Equal(400, sheet.PetroCardMorning);

        // Slot 2 (Shift B)
        Assert.Equal(2500, sheet.PhonePeDirectDay);
        Assert.Equal(1200, sheet.PineLabsCardDay);
        Assert.Equal(600, sheet.PetroCardDay);

        // Slot 3 (Night) should remain 0 in 2-shift cycle
        Assert.Equal(0, sheet.PhonePeDirectNight);
        Assert.Equal(0, sheet.PineLabsCardNight);
        Assert.Equal(0, sheet.PetroCardNight);

        // Totals
        Assert.Equal(4000, sheet.PhonePeDirectTotal);
        Assert.Equal(2000, sheet.PineLabsCardTotal);
        Assert.Equal(1000, sheet.PetroCardTotal);
    }

    private class MockFeatureToggleService : IFeatureToggleService
    {
        private readonly Dictionary<string, bool> _flags;

        public event Action? FeatureConfigurationChanged;

        public MockFeatureToggleService(Dictionary<string, bool>? flags = null)
        {
            _flags = flags ?? new Dictionary<string, bool>();
        }

        public bool IsFeatureEnabled(string featureKey, bool defaultIfMissing = true)
        {
            return _flags.TryGetValue(featureKey, out var val) ? val : defaultIfMissing;
        }

        public string GetFeatureDisplayName(string featureKey, string defaultName = "") => defaultName;
        public Task<List<AppFeatureSetting>> GetAllFeaturesAsync() => Task.FromResult(new List<AppFeatureSetting>());
        public Task<bool> SaveFeaturesAsync(IEnumerable<AppFeatureSetting> features) => Task.FromResult(true);
        public Task RefreshCacheAsync() => Task.CompletedTask;
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

    private class MockCreditorRepaymentRepository : ICreditorRepaymentRepository
    {
        public Task<Result<List<CreditorRepayment>>> GetByDateAsync(DateTime date) => 
            Task.FromResult(Result<List<CreditorRepayment>>.Ok(new List<CreditorRepayment>()));

        public Task<Result<List<CreditorRepayment>>> GetByMonthAsync(int year, int month) => throw new NotImplementedException();
        public Task<Result<CreditorRepayment>> AddAsync(CreditorRepayment repayment) => throw new NotImplementedException();
        public Task<Result> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<Result<List<CreditorRepayment>>> GetByDateRangeAsync(DateTime startDate, DateTime endDate) => 
            Task.FromResult(Result<List<CreditorRepayment>>.Ok(new List<CreditorRepayment>()));
    }

    private class MockDsmPersonalDebtorRepository : IDsmPersonalDebtorRepository
    {
        public Task<Result<List<DsmPersonalDebtor>>> GetByDateAsync(DateTime date) =>
            Task.FromResult(Result<List<DsmPersonalDebtor>>.Ok(new List<DsmPersonalDebtor>()));
        public Task<Result<List<DsmPersonalDebtor>>> GetByDateRangeAsync(DateTime startDate, DateTime endDate) =>
            Task.FromResult(Result<List<DsmPersonalDebtor>>.Ok(new List<DsmPersonalDebtor>()));
        public Task<Result<List<DsmPersonalDebtorRepayment>>> GetRepaymentsByDateAsync(DateTime date) =>
            Task.FromResult(Result<List<DsmPersonalDebtorRepayment>>.Ok(new List<DsmPersonalDebtorRepayment>()));
        public Task<Result<List<DsmPersonalDebtorRepayment>>> GetRepaymentsByDateRangeAsync(DateTime startDate, DateTime endDate) =>
            Task.FromResult(Result<List<DsmPersonalDebtorRepayment>>.Ok(new List<DsmPersonalDebtorRepayment>()));
        public Task<Result<DsmPersonalDebtor>> AddAsync(DsmPersonalDebtor debtor) => throw new NotImplementedException();
        public Task<Result<DsmPersonalDebtorRepayment>> AddRepaymentAsync(DsmPersonalDebtorRepayment repayment) => throw new NotImplementedException();
        public Task<Result> DeleteAsync(int id) => throw new NotImplementedException();
        public Task<Result> DeleteRepaymentAsync(int id) => throw new NotImplementedException();
        public Task<Result<List<DsmPersonalDebtor>>> GetByDsmEntryIdAsync(int dsmEntryId) =>
            Task.FromResult(Result<List<DsmPersonalDebtor>>.Ok(new List<DsmPersonalDebtor>()));
        public Task<Result> SavePersonalDebtorsAsync(int dsmEntryId, List<DsmPersonalDebtor> debtors) => throw new NotImplementedException();
    }
}
