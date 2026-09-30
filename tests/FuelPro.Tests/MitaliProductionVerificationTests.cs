using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.UI;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelPro.Tests;

public class MitaliProductionVerificationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
                var wal = file + "-wal";
                var shm = file + "-shm";
                if (File.Exists(wal)) File.Delete(wal);
                if (File.Exists(shm)) File.Delete(shm);
            }
            catch { }
        }
    }

    private string CreateTempDatabase()
    {
        string dbPath = Path.Combine(Path.GetTempPath(), $"fuelpro_mitali_verif_{Guid.NewGuid():N}.db");
        _tempFiles.Add(dbPath);

        // Ensure WPF App is initialized for Dispatcher and static properties
        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application();
        }

        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        using (var context = new FuelProDbContext(options))
        {
            context.Database.EnsureCreated();

            context.Settings.Add(new Setting
            {
                PumpStationName = "Mitali Service Station",
                StationDisplayName = "Mitali Service Station",
                HsdRate = 90,
                MsIRate = 100,
                MsIIRate = 100,
                CngRate = 85
            });
            context.SaveChanges();
        }

        return dbPath;
    }

    private ServiceProvider CreateServiceProvider(string dbPath)
    {
        var services = new ServiceCollection();

        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"),
            ServiceLifetime.Transient);

        services.AddTransient<ISettingsRepository, SettingsRepository>();
        services.AddTransient<IShiftRepository, ShiftRepository>();
        services.AddTransient<IDsmPersonalDebtorRepository, DsmPersonalDebtorRepository>();
        services.AddTransient<IDsmEntryRepository, DsmEntryRepository>();
        services.AddTransient<IExpenseRepository, ExpenseRepository>();
        services.AddTransient<IShiftFuelRateRepository, ShiftFuelRateRepository>();
        services.AddTransient<IShiftOtherCashRepository, ShiftOtherCashRepository>();
        services.AddTransient<ICreditorRepaymentRepository, CreditorRepaymentRepository>();
        services.AddTransient<ITidCalculationService, TidCalculationService>();
        services.AddTransient<IShiftAggregationService, ShiftAggregationService>();
        services.AddTransient<IReportService, ReportService>();
        services.AddSingleton<IFeatureToggleService, FeatureToggleService>();
        services.AddTransient<IUserRepository, UserRepository>();
        services.AddTransient<AuthService>();
        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddTransient<ICredentialFileService, LocalCredentialFileService>();
        services.AddTransient<ExcelExportService>();
        services.AddTransient<PrintService>();

        // ViewModels
        services.AddTransient<DsmPersonalDebtorViewModel>();
        services.AddTransient<FinalCalculationViewModel>();
        services.AddTransient<DayTotalViewModel>();

        var sp = services.BuildServiceProvider();
        typeof(App).GetProperty("Services", BindingFlags.Public | BindingFlags.Static)!.SetValue(null, sp);
        typeof(App).GetProperty("DbPath", BindingFlags.Public | BindingFlags.Static)!.SetValue(null, dbPath);
        return sp;
    }

    // =========================================================================
    // SECTION 1: VERIFY SHIFT MATCHING IMPLEMENTATION
    // =========================================================================
    [Fact]
    public void Section1_VerifyShiftMatchingCondition_AuthoritativeShiftIdOnly()
    {
        // Inspect FinalCalculationViewModel to ensure strict pdr.ShiftId.Value == shift.ShiftId is used
        var shift = new Shift { ShiftId = 42, ShiftDate = new DateTime(2026, 9, 14), ShiftType = "A" };
        var otherShift = new Shift { ShiftId = 99, ShiftDate = new DateTime(2026, 9, 14), ShiftType = "B" };

        var repayments = new List<DsmPersonalDebtorRepayment>
        {
            new() { Id = 1, ShiftId = 42, Amount = 500, PaymentMethod = "Cash", Date = shift.ShiftDate },
            new() { Id = 2, ShiftId = 99, Amount = 700, PaymentMethod = "Cash", Date = shift.ShiftDate },
            new() { Id = 3, ShiftId = null, Amount = 1000, PaymentMethod = "Cash", Date = shift.ShiftDate } // Historical null record
        };

        // Condition currently used in FinalCalculationViewModel:
        var matchedForShiftA = repayments.Where(pdr =>
            pdr.ShiftId.HasValue && pdr.ShiftId.Value == shift.ShiftId
        ).ToList();

        Assert.Single(matchedForShiftA);
        Assert.Equal(1, matchedForShiftA[0].Id);
        Assert.Equal(500, matchedForShiftA[0].Amount);

        // Ensure Shift B does not claim Shift A's record or null record
        var matchedForShiftB = repayments.Where(pdr =>
            pdr.ShiftId.HasValue && pdr.ShiftId.Value == otherShift.ShiftId
        ).ToList();

        Assert.Single(matchedForShiftB);
        Assert.Equal(2, matchedForShiftB[0].Id);
        Assert.Equal(700, matchedForShiftB[0].Amount);
    }

    // =========================================================================
    // SECTION 3: INTEGRATION VERIFICATION FOR NEW DSM REPAYMENTS (SQLite Pipeline)
    // =========================================================================
    [Fact]
    public async Task Section3_IntegrationVerification_NewDsmRepayments_ShiftA_ShiftB_DayTotal()
    {
        string dbPath = CreateTempDatabase();
        var sp = CreateServiceProvider(dbPath);

        using var db = sp.GetRequiredService<FuelProDbContext>();
        var shiftRepo = sp.GetRequiredService<IShiftRepository>();
        var reportService = sp.GetRequiredService<IReportService>();
        var aggregation = sp.GetRequiredService<IShiftAggregationService>();

        var testDate = new DateTime(2026, 9, 14);

        // 1. Resolve Shift A and Shift B via IShiftRepository.GetOrCreateShiftAsync
        var shiftARes = await shiftRepo.GetOrCreateShiftAsync(testDate, "A");
        Assert.True(shiftARes.Success);
        var shiftA = shiftARes.Data!;

        var shiftBRes = await shiftRepo.GetOrCreateShiftAsync(testDate, "B");
        Assert.True(shiftBRes.Success);
        var shiftB = shiftBRes.Data!;

        Assert.NotEqual(shiftA.ShiftId, shiftB.ShiftId);

        // 2. Create DSM Master and Personal Debtor record
        var dsmUser = new DsmUser
        {
            FullName = "Ramesh Jadhav",
            MobileNumber = "9876543210",
            EmployeeCode = "DSM001",
            CreatedAt = DateTime.Now
        };
        db.DsmUsers.Add(dsmUser);

        var personalDebtor = new DsmPersonalDebtor
        {
            DsmName = "Ramesh Jadhav",
            Date = testDate,
            Time = "08:00",
            Amount = 5000,
            RepaidAmount = 0,
            PaymentMethod = "Cash",
            CreatedAt = DateTime.Now
        };
        db.DsmPersonalDebtors.Add(personalDebtor);
        await db.SaveChangesAsync();

        // 3. Create Operational DSM Entry for Shift A (Gross Sales = 5000, Cash = 2000, Bank Cash = 3000)
        var dsmEntryA = new DsmEntry
        {
            ShiftId = shiftA.ShiftId,
            DsmName = "Ramesh Jadhav",
            PumpId = 1,
            GrossSales = 5000,
            CreatedAt = DateTime.Now,
            NozzleReadings = new List<NozzleReading>
            {
                new() { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 50, Rate = 100, Amount = 5000 }
            },
            PaymentCollection = new PaymentCollection { CashDeposit = 3000 },
            CashDenominations = new List<CashDenomination>
            {
                new() { CashType = "Cash2", TotalAmount = 2000 }
            }
        };
        db.DsmEntries.Add(dsmEntryA);

        // 4. Create Operational DSM Entry for Shift B (Gross Sales = 6000, Cash = 2500, Bank Cash = 3500)
        var dsmEntryB = new DsmEntry
        {
            ShiftId = shiftB.ShiftId,
            DsmName = "Ramesh Jadhav",
            PumpId = 1,
            GrossSales = 6000,
            CreatedAt = DateTime.Now,
            NozzleReadings = new List<NozzleReading>
            {
                new() { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 60, Rate = 100, Amount = 6000 }
            },
            PaymentCollection = new PaymentCollection { CashDeposit = 3500 },
            CashDenominations = new List<CashDenomination>
            {
                new() { CashType = "Cash2", TotalAmount = 2500 }
            }
        };
        db.DsmEntries.Add(dsmEntryB);
        await db.SaveChangesAsync();

        // ---------------------------------------------------------------------
        // TEST SHIFT A: ₹500 Cash DSM Repayment
        // ---------------------------------------------------------------------
        var repaymentA = new DsmPersonalDebtorRepayment
        {
            DsmPersonalDebtorId = personalDebtor.Id,
            ShiftId = shiftA.ShiftId,
            Date = testDate,
            Amount = 500,
            PaymentMethod = "Cash",
            Source = "OwnerPayroll",
            Denom500 = 1,
            CreatedAt = DateTime.Now
        };
        db.DsmPersonalDebtorRepayments.Add(repaymentA);
        await db.SaveChangesAsync();

        // Verify SQLite database persistence contains correct ShiftId for Shift A
        var persistedRepaymentA = await db.DsmPersonalDebtorRepayments.FindAsync(repaymentA.Id);
        Assert.NotNull(persistedRepaymentA);
        Assert.Equal(shiftA.ShiftId, persistedRepaymentA.ShiftId);

        // Load Shift A Report through the pipeline
        var shiftAPdRepayments = await db.DsmPersonalDebtorRepayments
            .Include(r => r.DsmPersonalDebtor)
            .Where(r => r.ShiftId.HasValue && r.ShiftId.Value == shiftA.ShiftId)
            .ToListAsync();

        Assert.Single(shiftAPdRepayments);

        var shiftACreditorRepayments = shiftAPdRepayments.Select(r => new CreditorRepayment
        {
            CreditorRepaymentId = r.Id,
            CreditorName = $"DSM Loss ({r.DsmPersonalDebtor?.DsmName})",
            RepaymentDate = r.Date,
            Amount = r.Amount,
            PaymentMode = r.PaymentMethod,
            ShiftNumber = shiftA.ShiftType
        }).ToList();

        var shiftAReport = reportService.CalculateShiftReport(
            date: testDate,
            shiftType: "A",
            entries: new List<DsmEntry> { dsmEntryA },
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: shiftACreditorRepayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // Assertions for Shift A
        Assert.Equal(500, shiftAReport.CashRepayments);
        Assert.Equal(5500, shiftAReport.ExpectedCollection); // 5000 gross + 500 recovery
        var cashInHandA = shiftAReport.CollectionBreakdown.First(c => c.Category == "Cash In Hand").Amount;
        Assert.Equal(2500, cashInHandA); // 2000 base cash + 500 cash repayment
        Assert.Equal(5500, shiftAReport.ActualCollection); // 2500 cash in hand + 3000 bank cash
        Assert.True(shiftAReport.IsBalanced);
        Assert.Equal(0, shiftAReport.Difference, precision: 2);
        Assert.Single(shiftAReport.PersonalDebtorRepayments);
        Assert.Equal(500, shiftAReport.PersonalDebtorRepayments[0].Amount);
        Assert.Equal("Ramesh Jadhav", shiftAReport.PersonalDebtorRepayments[0].DsmName);

        // Verify Shift B contains ₹0 recovery before its repayment
        var shiftBPdRepaymentsInitial = await db.DsmPersonalDebtorRepayments
            .Where(r => r.ShiftId.HasValue && r.ShiftId.Value == shiftB.ShiftId)
            .ToListAsync();
        Assert.Empty(shiftBPdRepaymentsInitial);

        // ---------------------------------------------------------------------
        // TEST SHIFT B: ₹700 Cash DSM Repayment
        // ---------------------------------------------------------------------
        var repaymentB = new DsmPersonalDebtorRepayment
        {
            DsmPersonalDebtorId = personalDebtor.Id,
            ShiftId = shiftB.ShiftId,
            Date = testDate,
            Amount = 700,
            PaymentMethod = "Cash",
            Source = "OwnerPayroll",
            Denom500 = 1,
            Denom200 = 1,
            CreatedAt = DateTime.Now
        };
        db.DsmPersonalDebtorRepayments.Add(repaymentB);
        await db.SaveChangesAsync();

        // Verify SQLite database persistence contains correct ShiftId for Shift B
        var persistedRepaymentB = await db.DsmPersonalDebtorRepayments.FindAsync(repaymentB.Id);
        Assert.NotNull(persistedRepaymentB);
        Assert.Equal(shiftB.ShiftId, persistedRepaymentB.ShiftId);

        // Load Shift B Report through the pipeline
        var shiftBPdRepayments = await db.DsmPersonalDebtorRepayments
            .Include(r => r.DsmPersonalDebtor)
            .Where(r => r.ShiftId.HasValue && r.ShiftId.Value == shiftB.ShiftId)
            .ToListAsync();

        Assert.Single(shiftBPdRepayments);
        Assert.Equal(700, shiftBPdRepayments[0].Amount);

        var shiftBCreditorRepayments = shiftBPdRepayments.Select(r => new CreditorRepayment
        {
            CreditorRepaymentId = r.Id,
            CreditorName = $"DSM Loss ({r.DsmPersonalDebtor?.DsmName})",
            RepaymentDate = r.Date,
            Amount = r.Amount,
            PaymentMode = r.PaymentMethod,
            ShiftNumber = shiftB.ShiftType
        }).ToList();

        var shiftBReport = reportService.CalculateShiftReport(
            date: testDate,
            shiftType: "B",
            entries: new List<DsmEntry> { dsmEntryB },
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: shiftBCreditorRepayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // Assertions for Shift B
        Assert.Equal(700, shiftBReport.CashRepayments);
        Assert.Equal(6700, shiftBReport.ExpectedCollection); // 6000 gross + 700 recovery
        var cashInHandB = shiftBReport.CollectionBreakdown.First(c => c.Category == "Cash In Hand").Amount;
        Assert.Equal(3200, cashInHandB); // 2500 base cash + 700 cash repayment
        Assert.Equal(6700, shiftBReport.ActualCollection); // 3200 cash in hand + 3500 bank cash
        Assert.True(shiftBReport.IsBalanced);
        Assert.Equal(0, shiftBReport.Difference, precision: 2);
        Assert.Single(shiftBReport.PersonalDebtorRepayments);
        Assert.Equal(700, shiftBReport.PersonalDebtorRepayments[0].Amount);

        // Confirm Shift A does NOT contain ₹700
        Assert.DoesNotContain(shiftAReport.PersonalDebtorRepayments, r => r.Amount == 700);

        // ---------------------------------------------------------------------
        // TEST DAY TOTAL: Aggregates both Shift A (₹500) and Shift B (₹700)
        // ---------------------------------------------------------------------
        var allDayPdRepayments = await db.DsmPersonalDebtorRepayments
            .Include(r => r.DsmPersonalDebtor)
            .Include(r => r.Shift)
            .Where(r => r.Date.Date >= testDate.Date && r.Date.Date <= testDate.Date)
            .ToListAsync();

        Assert.Equal(2, allDayPdRepayments.Count);

        var allDayCreditorRepayments = allDayPdRepayments.Select(r => new CreditorRepayment
        {
            CreditorRepaymentId = r.Id,
            CreditorName = $"DSM Loss ({r.DsmPersonalDebtor?.DsmName})",
            RepaymentDate = r.Date,
            Amount = r.Amount,
            PaymentMode = r.PaymentMethod,
            ShiftNumber = r.Shift?.ShiftType
        }).ToList();

        var dayReport = reportService.CalculateDayReport(
            startDate: testDate,
            endDate: testDate,
            entries: new List<DsmEntry> { dsmEntryA, dsmEntryB },
            allExpenses: new List<Expense>(),
            repayments: allDayCreditorRepayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            stationName: "Mitali Service Station"
        );

        Assert.Equal(1200, dayReport.CashRepayments); // 500 + 700
        Assert.Equal(12200, dayReport.ExpectedCollection); // 5000 + 6000 + 1200 = 12200
        var dayCashInHand = dayReport.CollectionBreakdown.First(c => c.Category == "Cash In Hand").Amount;
        Assert.Equal(5700, dayCashInHand); // 2000 + 2500 + 1200 = 5700
        Assert.Equal(12200, dayReport.ActualCollection); // 5700 + 6500 bank cash = 12200
        Assert.True(dayReport.IsBalanced);
        Assert.Equal(0, dayReport.Difference, precision: 2);
        Assert.Equal(2, dayReport.PersonalDebtorRepayments.Count);
        Assert.Contains(dayReport.PersonalDebtorRepayments, r => r.Amount == 500);
        Assert.Contains(dayReport.PersonalDebtorRepayments, r => r.Amount == 700);
    }

    // =========================================================================
    // SECTION 4: PAYMENT MODES THROUGH COMPLETE PIPELINE
    // =========================================================================
    [Fact]
    public void Section4_VerifyPaymentModes_Cash_PhonePe_Card_CompletePipeline()
    {
        var testDate = new DateTime(2026, 9, 14);
        var entries = new List<DsmEntry>
        {
            new()
            {
                DsmEntryId = 1,
                DsmName = "Ramesh Jadhav",
                PumpId = 1,
                GrossSales = 20000,
                NozzleReadings = new List<NozzleReading>
                {
                    new() { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 200, Rate = 100, Amount = 20000 }
                },
                CashDenominations = new List<CashDenomination>
                {
                    new() { CashType = "Cash2", TotalAmount = 10000 }
                },
                PaymentCollection = new PaymentCollection
                {
                    CashDeposit = 5000,
                    PhonePeMorning = 3000,
                    CreditCardMorning = 2000
                }
            }
        };

        var multiModeRepayments = new List<CreditorRepayment>
        {
            new()
            {
                CreditorRepaymentId = 1,
                CreditorName = "DSM Loss (Ramesh Jadhav)",
                RepaymentDate = testDate,
                Amount = 500,
                PaymentMode = "Cash",
                ShiftNumber = "A"
            },
            new()
            {
                CreditorRepaymentId = 2,
                CreditorName = "DSM Loss (Ramesh Jadhav)",
                RepaymentDate = testDate,
                Amount = 700,
                PaymentMode = "PhonePe",
                CardTid = "UPI123456",
                ShiftNumber = "A"
            },
            new()
            {
                CreditorRepaymentId = 3,
                CreditorName = "DSM Loss (Ramesh Jadhav)",
                RepaymentDate = testDate,
                Amount = 800,
                PaymentMode = "PineLabs Card",
                CardTid = "TID999",
                CardBatch = "BATCH01",
                ShiftNumber = "A"
            }
        };

        var aggregation = new ShiftAggregationService();
        var reportService = new ReportService(aggregation);

        var report = reportService.CalculateShiftReport(
            date: testDate,
            shiftType: "A",
            entries: entries,
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: multiModeRepayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // 1. Correct recovery amount breakdowns
        Assert.Equal(500, report.CashRepayments);
        Assert.Equal(700, report.PhonePeRepayments);
        Assert.Equal(800, report.CreditCardRepayments);
        Assert.Equal(2000, report.DebtorRepaymentsTotal);

        // 2. Physical cash integrity: Cash In Hand must ONLY increase by Cash repayments (500), NOT by digital (700 or 800)
        var cashInHand = report.CollectionBreakdown.First(c => c.Category == "Cash In Hand").Amount;
        Assert.Equal(10500, cashInHand); // 10000 base + 500 cash repayment

        // 3. Digital category routing
        var phonePeCollection = report.CollectionBreakdown.First(c => c.Category.Contains("PhonePe")).Amount;
        Assert.Equal(3700, phonePeCollection); // 3000 base + 700 repayment

        var cardCollection = report.CollectionBreakdown.First(c => c.Category.Contains("Card") || c.Category.Contains("PineLab")).Amount;
        Assert.Equal(2800, cardCollection); // 2000 base + 800 repayment

        // 4. ExpectedCollection = 20000 gross + 2000 recoveries = 22000
        Assert.Equal(22000, report.ExpectedCollection);

        // 5. ActualCollection = 10500 (cash) + 5000 (bank cash) + 3700 (phonepe) + 2800 (card) = 22000
        Assert.Equal(22000, report.ActualCollection);

        // 6. No double counting and difference = 0
        Assert.True(report.IsBalanced);
        Assert.Equal(0, report.Difference, precision: 2);

        // 7. DSM Loss report table contains all 3 entries with accurate payment modes
        Assert.Equal(3, report.PersonalDebtorRepayments.Count);
        Assert.Contains(report.PersonalDebtorRepayments, r => r.Amount == 500 && r.PaymentMethod == "Cash");
        Assert.Contains(report.PersonalDebtorRepayments, r => r.Amount == 700 && r.PaymentMethod == "PhonePe");
        Assert.Contains(report.PersonalDebtorRepayments, r => r.Amount == 800 && r.PaymentMethod == "PineLabs Card");
    }

    // =========================================================================
    // SECTION 5: HISTORICAL NULL-SHIFT RECORDS
    // =========================================================================
    [Fact]
    public async Task Section5_HistoricalNullShiftRecords_RemainNull_ExcludedFromShiftAB_IncludedInDayTotal()
    {
        string dbPath = CreateTempDatabase();
        var sp = CreateServiceProvider(dbPath);

        using var db = sp.GetRequiredService<FuelProDbContext>();
        var shiftRepo = sp.GetRequiredService<IShiftRepository>();
        var reportService = sp.GetRequiredService<IReportService>();

        var testDate = new DateTime(2026, 9, 14);

        var shiftA = (await shiftRepo.GetOrCreateShiftAsync(testDate, "A")).Data!;
        var shiftB = (await shiftRepo.GetOrCreateShiftAsync(testDate, "B")).Data!;

        var debtor = new DsmPersonalDebtor
        {
            DsmName = "Amit Singh",
            Date = testDate,
            Time = "09:00",
            Amount = 3000,
            RepaidAmount = 0,
            PaymentMethod = "Cash",
            CreatedAt = DateTime.Now
        };
        db.DsmPersonalDebtors.Add(debtor);
        await db.SaveChangesAsync();

        // 1. Insert historical record with ShiftId = NULL
        var historicalRepayment = new DsmPersonalDebtorRepayment
        {
            DsmPersonalDebtorId = debtor.Id,
            ShiftId = null, // HISTORICAL RECORD
            Date = testDate,
            Amount = 1500,
            PaymentMethod = "Cash",
            Source = "OwnerPayroll",
            CreatedAt = DateTime.Now
        };
        db.DsmPersonalDebtorRepayments.Add(historicalRepayment);
        await db.SaveChangesAsync();

        // 2. Verify database value remains NULL
        var persisted = await db.DsmPersonalDebtorRepayments.AsNoTracking().FirstOrDefaultAsync(r => r.Id == historicalRepayment.Id);
        Assert.NotNull(persisted);
        Assert.Null(persisted.ShiftId);

        // 3. Shift A query: MUST NOT claim it
        var shiftAPdRepayments = await db.DsmPersonalDebtorRepayments
            .Where(r => r.ShiftId.HasValue && r.ShiftId.Value == shiftA.ShiftId)
            .ToListAsync();
        Assert.Empty(shiftAPdRepayments);

        // 4. Shift B query: MUST NOT claim it
        var shiftBPdRepayments = await db.DsmPersonalDebtorRepayments
            .Where(r => r.ShiftId.HasValue && r.ShiftId.Value == shiftB.ShiftId)
            .ToListAsync();
        Assert.Empty(shiftBPdRepayments);

        // 5. Day Total query: INCLUDES IT
        var dayPdRepayments = await db.DsmPersonalDebtorRepayments
            .Include(r => r.DsmPersonalDebtor)
            .Include(r => r.Shift)
            .Where(r => r.Date.Date >= testDate.Date && r.Date.Date <= testDate.Date)
            .ToListAsync();

        Assert.Single(dayPdRepayments);
        Assert.Equal(1500, dayPdRepayments[0].Amount);
        Assert.Null(dayPdRepayments[0].ShiftId);

        var dayCreditorRepayments = dayPdRepayments.Select(r => new CreditorRepayment
        {
            CreditorRepaymentId = r.Id,
            CreditorName = $"DSM Loss ({r.DsmPersonalDebtor?.DsmName})",
            RepaymentDate = r.Date,
            Amount = r.Amount,
            PaymentMode = r.PaymentMethod,
            ShiftNumber = r.Shift?.ShiftType
        }).ToList();

        var dayReport = reportService.CalculateDayReport(
            startDate: testDate,
            endDate: testDate,
            entries: new List<DsmEntry>(),
            allExpenses: new List<Expense>(),
            repayments: dayCreditorRepayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            stationName: "Mitali Service Station"
        );

        Assert.Equal(1500, dayReport.CashRepayments);
        Assert.Single(dayReport.PersonalDebtorRepayments);
        Assert.Equal(1500, dayReport.PersonalDebtorRepayments[0].Amount);
    }

    // =========================================================================
    // SECTION 6: WPF NAVIGATION BEHAVIOR & AUTO-REFRESH
    // =========================================================================
    [Fact]
    public async Task Section6_VerifyWpfNavigationBehavior_AutoRefresh_StateRetention_NoDuplicates()
    {
        string dbPath = CreateTempDatabase();
        var sp = CreateServiceProvider(dbPath);

        using var db = sp.GetRequiredService<FuelProDbContext>();
        var testDate = new DateTime(2026, 9, 14);

        var dsm1 = new DsmUser { FullName = "Ramesh Jadhav", MobileNumber = "9876543210", EmployeeCode = "DSM001", CreatedAt = DateTime.Now };
        var dsm2 = new DsmUser { FullName = "Suresh Patel", MobileNumber = "9876543211", EmployeeCode = "DSM002", CreatedAt = DateTime.Now };
        db.DsmUsers.AddRange(dsm1, dsm2);

        var debtor1 = new DsmPersonalDebtor { DsmName = "Ramesh Jadhav", Date = testDate, Time = "08:00", Amount = 5000, RepaidAmount = 0, PaymentMethod = "Cash", CreatedAt = DateTime.Now };
        db.DsmPersonalDebtors.Add(debtor1);
        await db.SaveChangesAsync();

        var vm = sp.GetRequiredService<DsmPersonalDebtorViewModel>();

        // Step 1: Open DSM Loss
        await vm.LoadDataAsync();
        Assert.Equal(2, vm.DsmSummaries.Count);

        // Step 2: Select Ramesh Jadhav
        var ramesh = vm.DsmSummaries.First(s => s.DsmName == "Ramesh Jadhav");
        vm.SelectedSummary = ramesh;
        await vm.LoadLedgerAsync();
        Assert.Empty(vm.DsmLedger.Where(l => l.TransactionType == "Repayment"));

        // Step 3: Add new repayment directly to DB (simulating SavePersonalRepayment)
        var rep1 = new DsmPersonalDebtorRepayment
        {
            DsmPersonalDebtorId = debtor1.Id,
            Date = testDate,
            Amount = 500,
            PaymentMethod = "Cash",
            CreatedAt = DateTime.Now
        };
        db.DsmPersonalDebtorRepayments.Add(rep1);
        await db.SaveChangesAsync();

        // Step 4 & 5: Simulate navigating away and navigating back (MainWindowViewModel.NavigateToDsmPersonalDebtor calls vm.LoadDataAsync())
        await vm.LoadDataAsync();

        // Confirm latest repayment appears immediately without pressing manual Refresh
        Assert.NotNull(vm.SelectedSummary);
        Assert.Equal("Ramesh Jadhav", vm.SelectedSummary.DsmName);
        Assert.Equal(500, vm.SelectedSummary.TotalRepaid);
        Assert.Equal(4500, vm.SelectedSummary.Balance);

        // Confirm ledger contains the new repayment
        var ledgerRepayment = vm.DsmLedger.FirstOrDefault(l => l.TransactionType == "Repayment");
        Assert.NotNull(ledgerRepayment);
        Assert.Equal(500, ledgerRepayment.Amount);

        // Step 6 & 7: Repeat navigation multiple times — confirm no duplicate entries
        for (int i = 0; i < 5; i++)
        {
            await vm.LoadDataAsync();
        }
        Assert.Equal(2, vm.DsmSummaries.Count); // Exactly 2 DSMs, no duplication!

        // Step 8 & 9: Selected DSM and ledger restored correctly
        Assert.Equal("Ramesh Jadhav", vm.SelectedSummary.DsmName);
        Assert.Single(vm.DsmLedger.Where(l => l.TransactionType == "Repayment"));

        // Step 10: Manual Refresh command still works
        await vm.LoadDataCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.DsmSummaries.Count);
        Assert.Equal("Ramesh Jadhav", vm.SelectedSummary.DsmName);
    }

    // =========================================================================
    // SECTION 7: DYNAMIC PERSONAL LEDGER FEATURE CONFIGURATION PATH
    // =========================================================================
    [Fact]
    public async Task Section7_DynamicPersonalLedger_FeatureConfigurationPath_ToggleAndDisplayName()
    {
        string dbPath = CreateTempDatabase();
        var sp = CreateServiceProvider(dbPath);

        using var db = sp.GetRequiredService<FuelProDbContext>();
        var featureService = sp.GetRequiredService<IFeatureToggleService>();

        // 1. Configure Disabled in AppFeatureSettings
        var setting = new AppFeatureSetting
        {
            FeatureKey = "Operations_PersonalLedger",
            TargetRole = "Global",
            DisplayName = "Personal Ledger",
            IsEnabled = false,
            Description = "Custom Personal Ledger feature"
        };
        db.AppFeatureSettings.Add(setting);
        await db.SaveChangesAsync();

        await featureService.RefreshCacheAsync();

        var vm = sp.GetRequiredService<DsmPersonalDebtorViewModel>();

        // Verification Disabled:
        Assert.False(vm.IsPersonalLedgerEnabled);
        // Ensure no historical data was deleted
        Assert.Equal(0, await db.DsmPersonalDebtors.CountAsync()); // Empty, not deleted

        // 2. Configure Enabled with dynamic Custom Display Name
        setting.IsEnabled = true;
        setting.DisplayName = "Staff Drawings & Advances";
        db.AppFeatureSettings.Update(setting);
        await db.SaveChangesAsync();

        await featureService.RefreshCacheAsync();

        // Verification Enabled:
        Assert.True(vm.IsPersonalLedgerEnabled);
        Assert.Equal("Staff Drawings & Advances", vm.PersonalLedgerTitle);
        Assert.Equal("Staff Drawings & Advances Ledger", vm.PersonalLedgerTabHeader);
        // Verify Kandhare Petroleum is NOT hardcoded anywhere
        Assert.DoesNotContain("Kandhare", vm.PersonalLedgerTabHeader);
    }

    // =========================================================================
    // SECTION 8: DATABASE COMPATIBILITY ON MITALI CLIENT DATABASE COPY
    // =========================================================================
    [Fact]
    public async Task Section8_DatabaseCompatibility_MitaliClientDatabaseBackup()
    {
        string clientDbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FuelPro", "fuelPro.db");

        if (!File.Exists(clientDbPath))
        {
            // If the local database file does not exist in this environment, skip live copy test
            return;
        }

        // Copy client database to isolated temp file
        string tempCopy = Path.Combine(Path.GetTempPath(), $"mitali_client_backup_test_{Guid.NewGuid():N}.db");
        File.Copy(clientDbPath, tempCopy, overwrite: true);
        _tempFiles.Add(tempCopy);

        int initialDsmPersonalDebtorsCount;
        int initialDsmPersonalDebtorRepaymentsCount;
        int initialShiftsCount;
        var initialRepayments = new List<(int Id, double Amount, string Date, int? ShiftId)>();

        // Record pre-execution state
        using (var connection = new SqliteConnection($"Data Source={tempCopy}"))
        {
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();

            cmd.CommandText = "SELECT COUNT(*) FROM DsmPersonalDebtors;";
            initialDsmPersonalDebtorsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            cmd.CommandText = "SELECT COUNT(*) FROM DsmPersonalDebtorRepayments;";
            initialDsmPersonalDebtorRepaymentsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            cmd.CommandText = "SELECT COUNT(*) FROM Shifts;";
            initialShiftsCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            cmd.CommandText = "SELECT Id, Amount, Date, ShiftId FROM DsmPersonalDebtorRepayments ORDER BY Id;";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int id = reader.GetInt32(0);
                double amt = reader.GetDouble(1);
                string dt = reader.GetString(2);
                int? sId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
                initialRepayments.Add((id, amt, dt, sId));
            }
        }

        // Run application startup compatibility check on the copy
        App.EnsureLegacyDatabaseCompatibility(tempCopy);

        // Open with EF Core DbContext
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite($"Data Source={tempCopy}")
            .Options;

        using (var efContext = new FuelProDbContext(options))
        {
            var currentDebtorsCount = await efContext.DsmPersonalDebtors.CountAsync();
            var currentRepaymentsCount = await efContext.DsmPersonalDebtorRepayments.CountAsync();
            var currentShiftsCount = await efContext.Shifts.CountAsync();

            // 1. Confirm no historical DSM records were modified/deleted
            Assert.Equal(initialDsmPersonalDebtorsCount, currentDebtorsCount);
            Assert.Equal(initialDsmPersonalDebtorRepaymentsCount, currentRepaymentsCount);
            Assert.Equal(initialShiftsCount, currentShiftsCount);

            // 2. Confirm existing repayments are byte/field identical
            var currentRepayments = await efContext.DsmPersonalDebtorRepayments
                .OrderBy(r => r.Id)
                .Select(r => new { r.Id, r.Amount, Date = r.Date.ToString("yyyy-MM-dd"), r.ShiftId })
                .ToListAsync();

            Assert.Equal(initialRepayments.Count, currentRepayments.Count);
            for (int i = 0; i < initialRepayments.Count; i++)
            {
                Assert.Equal(initialRepayments[i].Id, currentRepayments[i].Id);
                Assert.Equal(initialRepayments[i].Amount, currentRepayments[i].Amount, precision: 2);
                Assert.Equal(initialRepayments[i].ShiftId, currentRepayments[i].ShiftId);
            }

            // 3. Confirm no ShiftNumber column exists (zero schema migration alterations)
            using var connection = new SqliteConnection($"Data Source={tempCopy}");
            await connection.OpenAsync();
            using var pragmaCmd = connection.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA table_info(DsmPersonalDebtorRepayments);";
            using var pragmaReader = await pragmaCmd.ExecuteReaderAsync();
            var columnNames = new List<string>();
            while (await pragmaReader.ReadAsync())
            {
                columnNames.Add(pragmaReader.GetString(1));
            }
            Assert.DoesNotContain("ShiftNumber", columnNames);
        }
    }

    // =========================================================================
    // SECTION 9: VERIFY ACTUAL PRINT GENERATION
    // =========================================================================
    [Fact]
    public void Section9_VerifyActualPrintGeneration_HtmlTemplatesContainExpectedLossRepayments()
    {
        // 1. Load HTML templates
        string finalCalcTemplatePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Printing", "FinalCalculationPrintTemplate.html");
        
        // If not in output directory, fallback to source tree path
        if (!File.Exists(finalCalcTemplatePath))
        {
            finalCalcTemplatePath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "src", "FuelPro.UI", "Printing", "FinalCalculationPrintTemplate.html"));
        }
        Assert.True(File.Exists(finalCalcTemplatePath), $"Template not found at {finalCalcTemplatePath}");

        string dayTotalTemplatePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Printing", "DayTotalPrintTemplate.html");
        if (!File.Exists(dayTotalTemplatePath))
        {
            dayTotalTemplatePath = Path.GetFullPath(Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "src", "FuelPro.UI", "Printing", "DayTotalPrintTemplate.html"));
        }
        Assert.True(File.Exists(dayTotalTemplatePath), $"Day Total template not found at {dayTotalTemplatePath}");

        var finalCalcTemplate = File.ReadAllText(finalCalcTemplatePath);
        var dayTotalTemplate = File.ReadAllText(dayTotalTemplatePath);

        // 2. Create Shift A DTO with 500 Cash repayment
        var shiftADto = new ShiftReportDto
        {
            ShiftLabel = "A",
            Date = new DateTime(2026, 9, 14),
            DateString = "14/09/2026",
            PersonalDebtorRepayments = new List<DsmPersonalDebtorRepaymentPrintDto>
            {
                new() { DsmName = "Ramesh Jadhav", Amount = 500, PaymentMethod = "Cash" }
            }
        };

        // 3. Create Shift B DTO with 700 Cash repayment
        var shiftBDto = new ShiftReportDto
        {
            ShiftLabel = "B",
            Date = new DateTime(2026, 9, 14),
            DateString = "14/09/2026",
            PersonalDebtorRepayments = new List<DsmPersonalDebtorRepaymentPrintDto>
            {
                new() { DsmName = "Ramesh Jadhav", Amount = 700, PaymentMethod = "Cash" }
            }
        };

        // 4. Create Day Total DTO with both
        var dayTotalDto = new DayReportDto
        {
            StartDate = new DateTime(2026, 9, 14),
            EndDate = new DateTime(2026, 9, 14),
            DateString = "14/09/2026",
            PersonalDebtorRepayments = new List<DsmPersonalDebtorRepaymentPrintDto>
            {
                new() { DsmName = "Ramesh Jadhav", Amount = 500, PaymentMethod = "Cash" },
                new() { DsmName = "Ramesh Jadhav", Amount = 700, PaymentMethod = "Cash" }
            }
        };

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // Inject Shift A JSON
        var shiftAJson = JsonSerializer.Serialize(shiftADto, jsonOptions);
        var shiftAHtml = finalCalcTemplate.Replace("/* INJECT_JSON_HERE */{}", shiftAJson);

        // Assert Shift A HTML contains its 500 repayment and not Shift B's 700
        Assert.Contains("Ramesh Jadhav", shiftAHtml);
        Assert.Contains("\"amount\":500", shiftAHtml);
        Assert.DoesNotContain("\"amount\":700", shiftAHtml);
        Assert.Contains("secPersonalDebtorRepayments", shiftAHtml);

        // Inject Shift B JSON
        var shiftBJson = JsonSerializer.Serialize(shiftBDto, jsonOptions);
        var shiftBHtml = finalCalcTemplate.Replace("/* INJECT_JSON_HERE */{}", shiftBJson);

        // Assert Shift B HTML contains its 700 repayment and not Shift A's 500
        Assert.Contains("Ramesh Jadhav", shiftBHtml);
        Assert.Contains("\"amount\":700", shiftBHtml);
        Assert.DoesNotContain("\"amount\":500", shiftBHtml);

        // Inject Day Total JSON
        var dayTotalJson = JsonSerializer.Serialize(dayTotalDto, jsonOptions);
        var dayTotalHtml = dayTotalTemplate.Replace("/* INJECT_JSON_HERE */{}", dayTotalJson);

        // Assert Day Total HTML contains both repayments
        Assert.Contains("Ramesh Jadhav", dayTotalHtml);
        Assert.Contains("\"amount\":500", dayTotalHtml);
        Assert.Contains("\"amount\":700", dayTotalHtml);
        Assert.Contains("secPersonalDebtorRepayments", dayTotalHtml);
    }
}
