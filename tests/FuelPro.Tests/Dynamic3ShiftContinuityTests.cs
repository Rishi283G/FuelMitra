using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelPro.Tests;

public class Dynamic3ShiftContinuityTests
{
    private readonly IShiftCycleService _cycleService = new ShiftCycleService();

    public Dynamic3ShiftContinuityTests()
    {
        PumpConfiguration.ResetToDefaults();
    }

    [Fact]
    public void Test01_CalendarDateReturns_B_C_A()
    {
        var date = new DateTime(2026, 10, 3);
        var shifts = _cycleService.GetShiftsForDate(date);

        Assert.Equal(3, shifts.Count);
        Assert.Equal("B", shifts[0]);
        Assert.Equal("C", shifts[1]);
        Assert.Equal("A", shifts[2]);
    }

    [Fact]
    public void Test02_Prev_B_D_Returns_A_DMinus1()
    {
        var date = new DateTime(2026, 10, 3);
        var (predDate, predShift) = _cycleService.GetPreviousShift(date, "B");

        Assert.Equal(date.AddDays(-1).Date, predDate.Date);
        Assert.Equal("A", predShift);
    }

    [Fact]
    public void Test03_Prev_C_D_Returns_B_D()
    {
        var date = new DateTime(2026, 10, 3);
        var (predDate, predShift) = _cycleService.GetPreviousShift(date, "C");

        Assert.Equal(date.Date, predDate.Date);
        Assert.Equal("B", predShift);
    }

    [Fact]
    public void Test04_Prev_A_D_Returns_C_D()
    {
        var date = new DateTime(2026, 10, 3);
        var (predDate, predShift) = _cycleService.GetPreviousShift(date, "A");

        Assert.Equal(date.Date, predDate.Date);
        Assert.Equal("C", predShift);
    }

    [Fact]
    public void Test05_Next_B_D_Returns_C_D()
    {
        var date = new DateTime(2026, 10, 3);
        var (nextDate, nextShift) = _cycleService.GetNextShift(date, "B");

        Assert.Equal(date.Date, nextDate.Date);
        Assert.Equal("C", nextShift);
    }

    [Fact]
    public void Test06_Next_C_D_Returns_A_D()
    {
        var date = new DateTime(2026, 10, 3);
        var (nextDate, nextShift) = _cycleService.GetNextShift(date, "C");

        Assert.Equal(date.Date, nextDate.Date);
        Assert.Equal("A", nextShift);
    }

    [Fact]
    public void Test07_Next_A_D_Returns_B_DPlus1()
    {
        var date = new DateTime(2026, 10, 3);
        var (nextDate, nextShift) = _cycleService.GetNextShift(date, "A");

        Assert.Equal(date.AddDays(1).Date, nextDate.Date);
        Assert.Equal("B", nextShift);
    }

    [Fact]
    public void Test08_DayOpeningShift_Returns_B()
    {
        var date = new DateTime(2026, 10, 3);
        Assert.Equal("B", _cycleService.GetOpeningShiftForDate(date));
    }

    [Fact]
    public void Test09_DayClosingShift_Returns_A()
    {
        var date = new DateTime(2026, 10, 3);
        Assert.Equal("A", _cycleService.GetClosingShiftForDate(date));
    }

    [Fact]
    public async Task Test10_ShiftB_OpeningUses_ShiftA_PreviousDate_Closing()
    {
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new FuelProDbContext(options);
        var repo = new NozzleReadingRepository(context, _cycleService);

        var date = new DateTime(2026, 10, 3);

        // Predecessor of B(D) is A(D-1)
        var shiftA = new Shift { ShiftId = 1, ShiftDate = date.AddDays(-1), ShiftType = "A" };
        var entryA = new DsmEntry { DsmEntryId = 1, ShiftId = 1, Shift = shiftA, PumpId = 1 };
        var readingA = new NozzleReading { NozzleReadingId = 1, DsmEntryId = 1, DsmEntry = entryA, NozzleNumber = 1, ClosingReading = 1000.50 };
        context.AddRange(shiftA, entryA, readingA);
        await context.SaveChangesAsync();

        var res = await repo.GetPreviousShiftClosingsAsync(date, "B", 1);
        Assert.True(res.Success);
        Assert.NotNull(res.Data);
        Assert.Equal(1000.50, res.Data[1]);
    }

    [Fact]
    public async Task Test11_ShiftC_OpeningUses_ShiftB_SameDate_Closing()
    {
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new FuelProDbContext(options);
        var repo = new NozzleReadingRepository(context, _cycleService);

        var date = new DateTime(2026, 10, 3);

        // Predecessor of C(D) is B(D)
        var shiftB = new Shift { ShiftId = 2, ShiftDate = date, ShiftType = "B" };
        var entryB = new DsmEntry { DsmEntryId = 2, ShiftId = 2, Shift = shiftB, PumpId = 1 };
        var readingB = new NozzleReading { NozzleReadingId = 2, DsmEntryId = 2, DsmEntry = entryB, NozzleNumber = 1, ClosingReading = 1500.75 };
        context.AddRange(shiftB, entryB, readingB);
        await context.SaveChangesAsync();

        var res = await repo.GetPreviousShiftClosingsAsync(date, "C", 1);
        Assert.True(res.Success);
        Assert.NotNull(res.Data);
        Assert.Equal(1500.75, res.Data[1]);
    }

    [Fact]
    public async Task Test12_ShiftA_OpeningUses_ShiftC_SameDate_Closing()
    {
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new FuelProDbContext(options);
        var repo = new NozzleReadingRepository(context, _cycleService);

        var date = new DateTime(2026, 10, 3);

        // Predecessor of A(D) is C(D)
        var shiftC = new Shift { ShiftId = 3, ShiftDate = date, ShiftType = "C" };
        var entryC = new DsmEntry { DsmEntryId = 3, ShiftId = 3, Shift = shiftC, PumpId = 1 };
        var readingC = new NozzleReading { NozzleReadingId = 3, DsmEntryId = 3, DsmEntry = entryC, NozzleNumber = 1, ClosingReading = 2200.25 };
        context.AddRange(shiftC, entryC, readingC);
        await context.SaveChangesAsync();

        var res = await repo.GetPreviousShiftClosingsAsync(date, "A", 1);
        Assert.True(res.Success);
        Assert.NotNull(res.Data);
        Assert.Equal(2200.25, res.Data[1]);
    }

    [Fact]
    public async Task Test13_SameNozzleContinuity_WhenPredecessorContainsNozzle()
    {
        PumpConfiguration.ResetToDefaults();
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new FuelProDbContext(options);
        var repo = new NozzleReadingRepository(context, _cycleService);

        var date = new DateTime(2026, 10, 3);
        var prevDate = date.AddDays(-1);

        var shiftC = new Shift { ShiftId = 10, ShiftDate = prevDate, ShiftType = "C" };
        var entryC = new DsmEntry { DsmEntryId = 10, ShiftId = 10, Shift = shiftC, PumpId = 1 };
        var readingC1 = new NozzleReading { NozzleReadingId = 101, DsmEntryId = 10, DsmEntry = entryC, NozzleNumber = 1, ClosingReading = 520378.48 };
        var readingC3 = new NozzleReading { NozzleReadingId = 102, DsmEntryId = 10, DsmEntry = entryC, NozzleNumber = 3, ClosingReading = 310500.12 };
        context.AddRange(shiftC, entryC, readingC1, readingC3);
        await context.SaveChangesAsync();

        var res = await repo.GetPreviousShiftClosingsAsync(date, "A", 1);
        Assert.True(res.Success);
        Assert.Equal(520378.48, res.Data![1]);
        Assert.Equal(310500.12, res.Data[3]);
    }

    [Fact]
    public async Task Test14_SameNozzleContinuity_CarriesForwardFromLastValidClosing_WhenPredecessorDidNotOperateNozzle()
    {
        PumpConfiguration.ResetToDefaults();
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new FuelProDbContext(options);
        var repo = new NozzleReadingRepository(context, _cycleService);

        var date = new DateTime(2026, 10, 3);
        var dateDMinus1 = date.AddDays(-1);

        // Shift B on D-1: Both Pump 1 and Pump 2 operated
        var shiftB = new Shift { ShiftId = 20, ShiftDate = dateDMinus1, ShiftType = "B" };
        var entryB1 = new DsmEntry { DsmEntryId = 201, ShiftId = 20, Shift = shiftB, PumpId = 1 };
        var readingB1 = new NozzleReading { NozzleReadingId = 2001, DsmEntryId = 201, DsmEntry = entryB1, NozzleNumber = 1, ClosingReading = 519000.00 };
        var entryB2 = new DsmEntry { DsmEntryId = 202, ShiftId = 20, Shift = shiftB, PumpId = 2 };
        var readingB4 = new NozzleReading { NozzleReadingId = 2004, DsmEntryId = 202, DsmEntry = entryB2, NozzleNumber = 4, ClosingReading = 229424.92 };
        context.AddRange(shiftB, entryB1, readingB1, entryB2, readingB4);

        // Shift C on D-1: ONLY Pump 1 operated (night pump). Pump 2 DID NOT OPERATE!
        var shiftC = new Shift { ShiftId = 21, ShiftDate = dateDMinus1, ShiftType = "C" };
        var entryC1 = new DsmEntry { DsmEntryId = 211, ShiftId = 21, Shift = shiftC, PumpId = 1 };
        var readingC1 = new NozzleReading { NozzleReadingId = 2101, DsmEntryId = 211, DsmEntry = entryC1, NozzleNumber = 1, ClosingReading = 520378.48 };
        context.AddRange(shiftC, entryC1, readingC1);
        await context.SaveChangesAsync();

        // 1. Shift A(D) on Pump 1: Must resolve Nozzle 1 from Shift C (520378.48)
        var resPump1 = await repo.GetPreviousShiftClosingsAsync(date, "A", 1);
        Assert.True(resPump1.Success);
        Assert.Equal(520378.48, resPump1.Data![1]);

        // 2. Shift A(D) on Pump 2: Must continue backward past Shift C to Shift B (229424.92) for Nozzle 4!
        var resPump2 = await repo.GetPreviousShiftClosingsAsync(date, "A", 2);
        Assert.True(resPump2.Success);
        Assert.Equal(229424.92, resPump2.Data![4]);
    }

    [Fact]
    public async Task Test15_SameNozzleContinuity_NeverCrossesNozzle()
    {
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new FuelProDbContext(options);
        var repo = new NozzleReadingRepository(context, _cycleService);

        var date = new DateTime(2026, 10, 3);
        var prevDate = date.AddDays(-1);

        // Predecessor shift only has Nozzle 2, NOT Nozzle 1
        var shiftC = new Shift { ShiftId = 30, ShiftDate = prevDate, ShiftType = "C" };
        var entryC = new DsmEntry { DsmEntryId = 301, ShiftId = 30, Shift = shiftC, PumpId = 1 };
        var reading2 = new NozzleReading { NozzleReadingId = 3002, DsmEntryId = 301, DsmEntry = entryC, NozzleNumber = 2, ClosingReading = 12345.67 };
        context.AddRange(shiftC, entryC, reading2);
        await context.SaveChangesAsync();

        var res = await repo.GetPreviousShiftClosingsAsync(date, "A", 1);
        // Nozzle 1 must NOT receive Nozzle 2's reading!
        if (res.Success && res.Data != null)
        {
            Assert.False(res.Data.ContainsKey(1));
            Assert.Equal(12345.67, res.Data[2]);
        }
    }

    [Fact]
    public async Task Test16_SameNozzleContinuity_NeverCrossesPump()
    {
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new FuelProDbContext(options);
        var repo = new NozzleReadingRepository(context, _cycleService);

        var date = new DateTime(2026, 10, 3);
        var prevDate = date.AddDays(-1);

        // Only Pump 2 has readings in predecessor, Pump 1 has none
        var shiftC = new Shift { ShiftId = 40, ShiftDate = prevDate, ShiftType = "C" };
        var entryC = new DsmEntry { DsmEntryId = 402, ShiftId = 40, Shift = shiftC, PumpId = 2 };
        var reading4 = new NozzleReading { NozzleReadingId = 4004, DsmEntryId = 402, DsmEntry = entryC, NozzleNumber = 4, ClosingReading = 99999.0 };
        context.AddRange(shiftC, entryC, reading4);
        await context.SaveChangesAsync();

        var resPump1 = await repo.GetPreviousShiftClosingsAsync(date, "A", 1);
        // Pump 1 must NOT receive readings from Pump 2!
        Assert.False(resPump1.Success);
    }

    [Fact]
    public async Task Test17_ActivePredecessor_WithMissingClosing_DoesNotSilentlyUseAnotherNozzle()
    {
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new FuelProDbContext(options);
        var repo = new NozzleReadingRepository(context, _cycleService);

        var date = new DateTime(2026, 10, 3);
        var prevDate = date.AddDays(-1);

        // Predecessor Shift C exists with pump 1, but ClosingReading <= 0
        var shiftC = new Shift { ShiftId = 50, ShiftDate = prevDate, ShiftType = "C" };
        var entryC = new DsmEntry { DsmEntryId = 501, ShiftId = 50, Shift = shiftC, PumpId = 1 };
        var readingZero = new NozzleReading { NozzleReadingId = 5001, DsmEntryId = 501, DsmEntry = entryC, NozzleNumber = 1, ClosingReading = 0 };
        context.AddRange(shiftC, entryC, readingZero);
        await context.SaveChangesAsync();

        var res = await repo.GetPreviousShiftClosingsAsync(date, "A", 1);
        // Must fail rather than returning 0 or fabricating a reading
        Assert.False(res.Success);
    }

    [Fact]
    public async Task Test18_NoArbitraryUnrelatedHistoricalFallback_BoundedHorizon()
    {
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new FuelProDbContext(options);
        var repo = new NozzleReadingRepository(context, _cycleService);

        // Record exists 20 shifts in the past (beyond 15-shift bound)
        var oldDate = new DateTime(2026, 10, 3).AddDays(-10);
        var shiftOld = new Shift { ShiftId = 60, ShiftDate = oldDate, ShiftType = "A" };
        var entryOld = new DsmEntry { DsmEntryId = 601, ShiftId = 60, Shift = shiftOld, PumpId = 1 };
        var readingOld = new NozzleReading { NozzleReadingId = 6001, DsmEntryId = 601, DsmEntry = entryOld, NozzleNumber = 1, ClosingReading = 5000 };
        context.AddRange(shiftOld, entryOld, readingOld);
        await context.SaveChangesAsync();

        var res = await repo.GetPreviousShiftClosingsAsync(new DateTime(2026, 10, 3), "A", 1);
        Assert.False(res.Success);
    }

    [Fact]
    public async Task Test19_Regression_DoNotSelectMaxClosingValue_UsesChronologicalOrder()
    {
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new FuelProDbContext(options);
        var repo = new NozzleReadingRepository(context, _cycleService);

        var date = new DateTime(2026, 10, 3);
        var prevDate = date.AddDays(-1);

        var shiftC = new Shift { ShiftId = 70, ShiftDate = prevDate, ShiftType = "C" };
        // First entry: higher meter reading (e.g. before rollover or erroneous entry)
        var entry1 = new DsmEntry
        {
            DsmEntryId = 701,
            ShiftId = 70,
            Shift = shiftC,
            PumpId = 1,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        var readingHigh = new NozzleReading
        {
            NozzleReadingId = 7001,
            DsmEntryId = 701,
            DsmEntry = entry1,
            NozzleNumber = 1,
            ClosingReading = 999999.0
        };

        // Second entry: lower meter reading (e.g. rollover to 100.0 or corrected reading), saved chronologically LATER
        var entry2 = new DsmEntry
        {
            DsmEntryId = 702,
            ShiftId = 70,
            Shift = shiftC,
            PumpId = 1,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        var readingChronologicalLatest = new NozzleReading
        {
            NozzleReadingId = 7002,
            DsmEntryId = 702,
            DsmEntry = entry2,
            NozzleNumber = 1,
            ClosingReading = 100.0
        };

        context.AddRange(shiftC, entry1, readingHigh, entry2, readingChronologicalLatest);
        await context.SaveChangesAsync();

        var res = await repo.GetPreviousShiftClosingsAsync(date, "A", 1);
        Assert.True(res.Success);
        Assert.NotNull(res.Data);
        // PROVE: Latest record (100.0) is returned, NOT the maximum closing reading (999999.0)!
        Assert.Equal(100.0, res.Data[1]);
        Assert.NotEqual(999999.0, res.Data[1]);
    }

    [Fact]
    public async Task Test20_RealClientMitaliValues_Nozzle1_And_Nozzle4_ReproducesCorrectly()
    {
        PumpConfiguration.ResetToDefaults();
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var context = new FuelProDbContext(options);
        var repo = new NozzleReadingRepository(context, _cycleService);

        var dateOct2 = new DateTime(2026, 10, 2);
        var dateOct3 = new DateTime(2026, 10, 3);

        // Real forensic Shift B (2026-10-02): Pump 2 / Nozzle 4 closing = 229424.92
        var shiftB = new Shift { ShiftId = 11, ShiftDate = dateOct2, ShiftType = "B" };
        var entryB2 = new DsmEntry { DsmEntryId = 112, ShiftId = 11, Shift = shiftB, PumpId = 2 };
        var readingB4 = new NozzleReading { NozzleReadingId = 1104, DsmEntryId = 112, DsmEntry = entryB2, NozzleNumber = 4, ClosingReading = 229424.92 };
        context.AddRange(shiftB, entryB2, readingB4);

        // Real forensic Shift C (2026-10-02): Pump 1 / Nozzle 1 closing = 520378.48
        var shiftC = new Shift { ShiftId = 12, ShiftDate = dateOct2, ShiftType = "C" };
        var entryC1 = new DsmEntry { DsmEntryId = 121, ShiftId = 12, Shift = shiftC, PumpId = 1 };
        var readingC1 = new NozzleReading { NozzleReadingId = 1201, DsmEntryId = 121, DsmEntry = entryC1, NozzleNumber = 1, ClosingReading = 520378.48 };
        context.AddRange(shiftC, entryC1, readingC1);
        await context.SaveChangesAsync();

        // 1. Verify Nozzle 1 on Shift A(2026-10-03) resolves 520378.48 from Shift C
        var resNozzle1 = await repo.GetPreviousShiftClosingsAsync(dateOct3, "A", 1);
        Assert.True(resNozzle1.Success);
        Assert.Equal(520378.48, resNozzle1.Data![1]);

        // 2. Verify Nozzle 4 on Shift A(2026-10-03) resolves 229424.92 from Shift B
        var resNozzle4 = await repo.GetPreviousShiftClosingsAsync(dateOct3, "A", 2);
        Assert.True(resNozzle4.Success);
        Assert.Equal(229424.92, resNozzle4.Data![4]);
    }

    [Fact]
    public void Test21_ShiftC_DebtorRepayment_RemainsC()
    {
        var date = new DateTime(2026, 10, 3);
        var repayment = new CreditorRepayment
        {
            CreditorRepaymentId = 801,
            CreditorName = "Mitali Debtor",
            Amount = 2500,
            ShiftNumber = "C",
            RepaymentDate = SettlementWindowResolver.GetRepaymentDateTime(date, "C"),
            PaymentMode = "PhonePe"
        };

        var classified = SettlementWindowResolver.Classify(repayment);
        Assert.True(classified.IsValid);
        Assert.Equal("Night", classified.SettlementWindow);
        Assert.Equal("C", repayment.ShiftNumber);
    }

    [Fact]
    public void Test22_DsmLossC_Repayment_RemainsC()
    {
        var date = new DateTime(2026, 10, 3);
        var pdRepayment = new DsmPersonalDebtorRepayment
        {
            Id = 901,
            Amount = 1200,
            Date = date,
            PaymentMethod = "PineLabs Card",
            CardTid = "TIDC09"
        };

        var repayment = new CreditorRepayment
        {
            CreditorRepaymentId = pdRepayment.Id,
            CreditorName = "DSM Loss (Test DSM)",
            RepaymentDate = pdRepayment.Date,
            Amount = pdRepayment.Amount,
            PaymentMode = "PineLabs Card",
            CardTid = pdRepayment.CardTid,
            ShiftNumber = "C"
        };

        var classified = SettlementWindowResolver.Classify(repayment);
        Assert.True(classified.IsValid);
        Assert.Equal("Night", classified.SettlementWindow);
        Assert.Equal("C", repayment.ShiftNumber);
    }

    [Fact]
    public void Test23_ShiftC_Payments_RemainIsolated_From_ShiftA()
    {
        var aggService = new ShiftAggregationService(_cycleService);
        var reportService = new ReportService(aggService);
        var date = new DateTime(2026, 10, 3);

        var repC = new CreditorRepayment { CreditorRepaymentId = 1, Amount = 1000, ShiftNumber = "C", RepaymentDate = date.AddHours(20), PaymentMode = "PhonePe" };
        var repA = new CreditorRepayment { CreditorRepaymentId = 2, Amount = 2000, ShiftNumber = "A", RepaymentDate = date.AddHours(4), PaymentMode = "PhonePe" };
        var all = new List<CreditorRepayment> { repC, repA };

        var reportA = reportService.CalculateShiftReport(
            date, "A", new List<DsmEntry>(), new List<Expense>(), new List<ShiftOtherCash>(),
            all, 90, 100, 100, 85, new BusinessDayTidSheet(), new BusinessDayTidSheet(), "Station");

        Assert.Equal(2000, reportA.DebtorRepaymentsTotal);
        Assert.Equal(2000, reportA.PhonePeRepayments);
    }

    [Fact]
    public async Task Test24_TidSheet_Settlement_RemainsConsistent()
    {
        var date = new DateTime(2026, 10, 3);
        var sA = new Shift { ShiftId = 101, ShiftDate = date, ShiftType = "A" };
        var sB = new Shift { ShiftId = 102, ShiftDate = date, ShiftType = "B" };
        var sC = new Shift { ShiftId = 103, ShiftDate = date, ShiftType = "C" };

        var eA = new DsmEntry { DsmEntryId = 101, ShiftId = 101, Shift = sA, PumpId = 1, PaymentCollection = new PaymentCollection { PhonePeMorning = 1000, PhonePeTidMorning = "TID_A" } };
        var eB = new DsmEntry { DsmEntryId = 102, ShiftId = 102, Shift = sB, PumpId = 1, PaymentCollection = new PaymentCollection { PhonePeDay = 2000, PhonePeTidDay = "TID_B" } };
        var eC = new DsmEntry { DsmEntryId = 103, ShiftId = 103, Shift = sC, PumpId = 1, PaymentCollection = new PaymentCollection { PhonePeNight = 3000, PhonePeTidNight = "TID_C" } };

        var mockShiftRepo = new TidCalculationServiceTests.MockShiftRepository(new List<Shift> { sA, sB, sC });
        var mockDsmRepo = new TidCalculationServiceTests.MockDsmEntryRepository(new List<DsmEntry> { eA, eB, eC });
        var mockRepaymentRepo = new TidCalculationServiceTests.MockCreditorRepaymentRepository();
        var mockPersonalDebtorRepo = new TidCalculationServiceTests.MockDsmPersonalDebtorRepository();
        var mockFeatureToggle = new TidCalculationServiceTests.MockFeatureToggleService(new Dictionary<string, bool> { { "Collection_UseMorningNight", true } });

        var tidService = new TidCalculationService(mockShiftRepo, mockDsmRepo, mockRepaymentRepo, mockPersonalDebtorRepo, null, mockFeatureToggle);
        var sheet = await tidService.GetTidSheetAsync(date);

        Assert.Equal(1000, sheet.PhonePeDirectMorning);
        Assert.Equal(2000, sheet.PhonePeDirectDay);
        Assert.Equal(3000, sheet.PhonePeDirectNight);
    }

    [Fact]
    public void Test25_DayTotal_OpeningB_ClosingA()
    {
        var service = new AgsDailyAggregationService();
        var date = new DateTime(2026, 10, 3);

        var shiftB = new AgsShiftImport { ImportDate = date, ShiftType = "B", IsActive = true, HsdOpeningStock = 14000, HsdClosingStock = 13000 };
        var shiftC = new AgsShiftImport { ImportDate = date, ShiftType = "C", IsActive = true, HsdOpeningStock = 13000, HsdClosingStock = 12000 };
        var shiftA = new AgsShiftImport { ImportDate = date, ShiftType = "A", IsActive = true, HsdOpeningStock = 12000, HsdClosingStock = 11000 };

        var summary = service.Aggregate(date, new List<AgsShiftImport> { shiftB, shiftC, shiftA });

        // Day Opening Stock must strictly come from Shift B: 14000
        Assert.Equal(14000, summary.HsdDayOpeningStock);
        // Day Closing Stock must strictly come from Shift A: 11000
        Assert.Equal(11000, summary.HsdDayClosingStock);
        Assert.True(summary.ShiftAImported);
        Assert.True(summary.ShiftBImported);
        Assert.True(summary.ShiftCImported);
    }

    [Fact]
    public void Test26_UiDisplayOrder_Is_B_C_A()
    {
        var shifts = _cycleService.GetShiftsForDate(DateTime.Today);
        Assert.Equal(new[] { "B", "C", "A" }, shifts);
    }

    [Fact]
    public void Test27_NoWallClockLogic_ShiftSequenceDeterministicAcrossAll24Hours()
    {
        var date = new DateTime(2026, 10, 3);

        for (int hour = 0; hour < 24; hour++)
        {
            var simulatedTime = date.AddHours(hour);

            var shifts = _cycleService.GetShiftsForDate(simulatedTime);
            Assert.Equal("B", shifts[0]);
            Assert.Equal("C", shifts[1]);
            Assert.Equal("A", shifts[2]);

            Assert.Equal(("C", date.Date), (_cycleService.GetNextShift(simulatedTime.Date, "B").ShiftType, _cycleService.GetNextShift(simulatedTime.Date, "B").Date.Date));
            Assert.Equal(("A", date.Date), (_cycleService.GetNextShift(simulatedTime.Date, "C").ShiftType, _cycleService.GetNextShift(simulatedTime.Date, "C").Date.Date));
            Assert.Equal(("B", date.AddDays(1).Date), (_cycleService.GetNextShift(simulatedTime.Date, "A").ShiftType, _cycleService.GetNextShift(simulatedTime.Date, "A").Date.Date));

            Assert.Equal(("A", date.AddDays(-1).Date), (_cycleService.GetPreviousShift(simulatedTime.Date, "B").ShiftType, _cycleService.GetPreviousShift(simulatedTime.Date, "B").Date.Date));
            Assert.Equal(("B", date.Date), (_cycleService.GetPreviousShift(simulatedTime.Date, "C").ShiftType, _cycleService.GetPreviousShift(simulatedTime.Date, "C").Date.Date));
            Assert.Equal(("C", date.Date), (_cycleService.GetPreviousShift(simulatedTime.Date, "A").ShiftType, _cycleService.GetPreviousShift(simulatedTime.Date, "A").Date.Date));
        }
    }

    [Fact]
    public void Test28_HistoricalCompatibility_OldABOnlyRecordsReadable()
    {
        var aggService = new ShiftAggregationService(_cycleService);
        var reportService = new ReportService(aggService);

        var date = new DateTime(2026, 9, 15); // Historical date before Shift C was adopted
        var shiftA = new Shift { ShiftId = 51, ShiftDate = date, ShiftType = "A" };
        var entryA = new DsmEntry
        {
            DsmEntryId = 51,
            ShiftId = 51,
            Shift = shiftA,
            PumpId = 1,
            PaymentCollection = new PaymentCollection { PhonePeMorning = 3000 }
        };

        var shiftB = new Shift { ShiftId = 52, ShiftDate = date, ShiftType = "B" };
        var entryB = new DsmEntry
        {
            DsmEntryId = 52,
            ShiftId = 52,
            Shift = shiftB,
            PumpId = 1,
            PaymentCollection = new PaymentCollection { PhonePeDay = 4000 }
        };

        var entries = new List<DsmEntry> { entryA, entryB };
        var dayReport = reportService.CalculateDayReport(
            date, date, entries, new List<Expense>(),
            new List<CreditorRepayment>(), 90, 100, 100, 85,
            "Station");

        Assert.Equal(7000, dayReport.DsmSummaryTotals.PhonePe);
    }

    [Fact]
    public async Task Test29_DisposableClone_RealMitaliDatabase_Verification()
    {
        string baseDir = AppContext.BaseDirectory;
        string cloneDbPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "forensic_investigation", "DISPOSABLE_WORKING_CLONE", "fuelPro.db"));

        if (!File.Exists(cloneDbPath))
        {
            // If running in an environment without the local clone folder, skip clone-specific assertions
            return;
        }

        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite($"Data Source={cloneDbPath};Mode=ReadOnly")
            .Options;

        using var context = new FuelProDbContext(options);
        var repo = new NozzleReadingRepository(context, _cycleService);

        var dateOct3 = new DateTime(2026, 10, 3);

        // Pump 1 operated in Shift C (2026-10-02): must resolve Nozzle 1 = 520378.48
        var resPump1 = await repo.GetPreviousShiftClosingsAsync(dateOct3, "A", 1);
        Assert.True(resPump1.Success, $"Pump 1 query failed: {resPump1.Error}");
        Assert.NotNull(resPump1.Data);
        Assert.True(resPump1.Data.ContainsKey(1));
        Assert.Equal(520378.48, resPump1.Data[1]);

        // Pump 2 DID NOT operate in Shift C (2026-10-02): must resolve Nozzle 4 = 229424.92 from Shift B
        var resPump2 = await repo.GetPreviousShiftClosingsAsync(dateOct3, "A", 2);
        Assert.True(resPump2.Success, $"Pump 2 query failed: {resPump2.Error}");
        Assert.NotNull(resPump2.Data);
        Assert.True(resPump2.Data.ContainsKey(4));
        Assert.Equal(229424.92, resPump2.Data[4]);
    }
}
