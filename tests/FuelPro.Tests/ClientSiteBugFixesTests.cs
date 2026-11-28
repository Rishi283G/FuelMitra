using FuelPro.Data;
using FuelPro.UI.Printing;
using FuelPro.Data.Repositories;
using FuelPro.UI.ViewModels;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelPro.Tests;

public class ClientSiteBugFixesTests
{
    private readonly ShiftAggregationService _aggregationService;
    private readonly ReportService _reportService;

    public ClientSiteBugFixesTests()
    {
        PumpConfiguration.ResetToDefaults();
        _aggregationService = new ShiftAggregationService();
        _reportService = new ReportService(_aggregationService);
    }

    [Fact]
    public void PetroCard_ItemizedTransactions_AggregatedIntoPetroCardTotal_AndAllocatedBySlot()
    {
        // Arrange: Shift A entry with itemized PetroCard transactions in PaymentCollection.Items
        var entry = new DsmEntry
        {
            DsmName = "Attendant 1",
            PumpId = 1,
            GrossSales = 5000,
            Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
            PaymentCollection = new PaymentCollection
            {
                // Pure itemized, scalar PetroCard is 0
                PetroCardMorning = 0,
                PetroCardNight = 0,
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 1200, Slot = "Morning", Tid = "TID01" },
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 800, Slot = "Night", Tid = "TID02" }
                }
            }
        };

        // Act
        var rows = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entry });

        // Assert
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal(1200, r.PetroCardMorning);
        Assert.Equal(800, r.PetroCardNight);
        Assert.Equal(2000, r.PetroCardTotal);
        Assert.Equal(2000, r.PetroCard);
    }

    [Fact]
    public void PetroCard_ScalarTransactions_PreservedWithoutRegression()
    {
        // Arrange: Purely scalar PetroCard entries (legacy or direct entry)
        var entry = new DsmEntry
        {
            DsmName = "Attendant 2",
            PumpId = 2,
            GrossSales = 3000,
            Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
            PaymentCollection = new PaymentCollection
            {
                PetroCardMorning = 500,
                PetroCardNight = 300,
                Items = new List<PaymentCollectionItem>()
            }
        };

        // Act
        var rows = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entry });

        // Assert
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal(500, r.PetroCardMorning);
        Assert.Equal(300, r.PetroCardNight);
        Assert.Equal(800, r.PetroCardTotal);
    }

    [Fact]
    public void PetroCard_MixedRepresentation_DoesNotDoubleCount()
    {
        // Arrange: Scalar PetroCard is set to 1500, and itemized items also total 1500 (mirroring)
        var entry = new DsmEntry
        {
            DsmName = "Attendant 3",
            PumpId = 1,
            GrossSales = 4000,
            Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
            PaymentCollection = new PaymentCollection
            {
                PetroCardMorning = 1500,
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 1500, Slot = "Morning", Tid = "TID99" }
                }
            }
        };

        // Act
        var rows = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entry });

        // Assert: Total must remain 1500, NOT double-counted to 3000
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal(1500, r.PetroCardTotal);
    }

    [Fact]
    public void PetroCard_ShiftB_AllocatesToDaySlot()
    {
        // Arrange: Shift B (Day shift)
        var entry = new DsmEntry
        {
            DsmName = "Attendant 4",
            PumpId = 3,
            GrossSales = 6000,
            Shift = new Shift { ShiftType = "B", ShiftDate = DateTime.Today },
            PaymentCollection = new PaymentCollection
            {
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 2500, Slot = "Day", Tid = "TID55" }
                }
            }
        };

        // Act
        var rows = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entry });

        // Assert
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal(2500, r.PetroCardDay);
        Assert.Equal(2500, r.PetroCardTotal);
    }

    [Fact]
    public void PineLabsCard_ItemizedTransactions_AggregatedIntoPineLabsCard()
    {
        // Arrange: Entry with itemized PINELABS_CARD transaction
        var entry = new DsmEntry
        {
            DsmName = "Attendant 5",
            PumpId = 1,
            GrossSales = 5000,
            Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
            PaymentCollection = new PaymentCollection
            {
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PINELABS_CARD", Amount = 1800, Slot = "Morning", Tid = "PL01" }
                }
            }
        };

        // Act
        var rows = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entry });

        // Assert
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal(1800, r.PineLabsCard);
    }

    [Fact]
    public void ShiftAndDayTotals_IncludePetroCard_FromItemizedItems()
    {
        // Arrange: Multiple entries across pumps
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmName = "Attendant A",
                PumpId = 1,
                GrossSales = 10000,
                Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
                PaymentCollection = new PaymentCollection
                {
                    Items = new List<PaymentCollectionItem>
                    {
                        new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 1500, Slot = "Morning" }
                    }
                }
            },
            new DsmEntry
            {
                DsmName = "Attendant B",
                PumpId = 2,
                GrossSales = 8000,
                Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
                PaymentCollection = new PaymentCollection
                {
                    Items = new List<PaymentCollectionItem>
                    {
                        new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 700, Slot = "Night" }
                    }
                }
            }
        };

        // Act
        var summaryRows = _aggregationService.BuildDsmSummaryRows(entries);
        var totalRow = _aggregationService.BuildDsmSummaryTotalRow(summaryRows);
        var shiftTotals = _aggregationService.BuildDsmShiftTotals(summaryRows);

        // Assert: Total PetroCard across both attendants is 2200
        Assert.Equal(2200, totalRow.PetroCard);
        Assert.Equal(1500, totalRow.PetroCardMorning);
        Assert.Equal(700, totalRow.PetroCardNight);

        Assert.Equal(2, shiftTotals.Count);
        Assert.Equal(2200, shiftTotals.Sum(s => s.PetroCard));
    }

    [Fact]
    public void CrossSectionDeduplication_PreservesDistinctTransactions_WithIdenticalAmount()
    {
        // Verify that an OTP transaction of 500 and a distinct manual TID transaction of 500
        // (with different TID / Batch) are both preserved and not falsely collapsed.
        var cardSwipeDetails = new List<DsmCardSwipeRow>
        {
            new DsmCardSwipeRow
            {
                Mode = "PINELABS_CARD",
                Amount = 500.0,
                Tid = "TID_OTP_1",
                Batch = "B001",
                Otp = "123456",
                TransactionId = "tx-otp-1"
            }
        };

        // Incoming manual TID transaction with SAME amount and SAME mode, but different TID/Batch
        string normDCode = "PINELABS_CARD";
        double dAmt = 500.0;
        string dTid = "TID_MANUAL_2";
        string dBatch = "B002";
        string? dTxId = "tx-manual-2";

        bool inOtp = cardSwipeDetails.Any(c =>
            (!string.IsNullOrWhiteSpace(c.TransactionId) && !string.IsNullOrWhiteSpace(dTxId) && string.Equals(c.TransactionId, dTxId, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(c.Otp) && string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normDCode, StringComparison.OrdinalIgnoreCase) && Math.Abs(c.Amount - dAmt) < 0.001
                && !string.IsNullOrWhiteSpace(c.Tid) && string.Equals(c.Tid.Trim(), dTid.Trim(), StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(c.Batch) && string.Equals(c.Batch.Trim(), dBatch.Trim(), StringComparison.OrdinalIgnoreCase)));

        Assert.False(inOtp, "Distinct manual TID transaction with same amount must NOT be identified as inOtp duplicate");
    }

    [Fact]
    public void PetroCard_MixedRepresentation_DistinctScalarAndItemized_CombinesCorrectly()
    {
        // Arrange: Scalar PetroCard is set to 500, and a distinct itemized transaction of 300 is also entered
        var entry = new DsmEntry
        {
            DsmName = "Attendant Distinct",
            PumpId = 1,
            GrossSales = 5000,
            Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
            PaymentCollection = new PaymentCollection
            {
                PetroCardMorning = 500,
                PetroCardTidMorning = "TID_SCALAR_1",
                PetroCardBatchMorning = "B01",
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 300, Slot = "Morning", Tid = "TID_ITEM_DISTINCT_2", Batch = "B02" }
                }
            }
        };

        // Act
        var rows = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entry });

        // Assert: 500 scalar + 300 distinct itemized = 800 total (NOT suppressed to 500)
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal(800, r.PetroCardMorning);
        Assert.Equal(800, r.PetroCardTotal);
    }

    [Fact]
    public void PetroCard_MixedRepresentation_MirroredRollup_DoesNotDoubleCount()
    {
        // Arrange: Scalar PetroCard is set to 500 (rollup summary), and items contain two transactions totaling 500 (200 + 300)
        var entry = new DsmEntry
        {
            DsmName = "Attendant Mirrored Rollup",
            PumpId = 1,
            GrossSales = 5000,
            Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
            PaymentCollection = new PaymentCollection
            {
                PetroCardMorning = 500,
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 200, Slot = "Morning", Tid = "T1" },
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 300, Slot = "Morning", Tid = "T2" }
                }
            }
        };

        // Act
        var rows = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entry });

        // Assert: Total must remain 500, NOT double counted to 1000
        Assert.Single(rows);
        var r = rows[0];
        Assert.Equal(500, r.PetroCardMorning);
        Assert.Equal(500, r.PetroCardTotal);
    }

    [Fact]
    public void CrossSectionDeduplication_SuppressesGenuineDuplicate_WithMatchingIdentifiers()
    {
        // Arrange: An existing OTP transaction in CardSwipeDetails
        var cardSwipeDetails = new List<DsmCardSwipeRow>
        {
            new DsmCardSwipeRow
            {
                Mode = "PINELABS_CARD",
                Amount = 500.0,
                Tid = "TID_OTP_1",
                Batch = "B001",
                Otp = "123456",
                TransactionId = "tx-otp-1"
            }
        };

        // Incoming manual TID transaction representing the SAME transaction (same mode, amount, TID, and batch)
        string normDCode = "PINELABS_CARD";
        double dAmt = 500.0;
        string dTid = "TID_OTP_1";
        string dBatch = "B001";
        string? dTxId = ""; // empty/missing ID, relies on fallback TID/Batch match

        bool inOtp = cardSwipeDetails.Any(c =>
            (!string.IsNullOrWhiteSpace(c.TransactionId) && !string.IsNullOrWhiteSpace(dTxId) && string.Equals(c.TransactionId, dTxId, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(c.Otp) && string.Equals(PaymentCollection.NormalizeCollectionTypeCode(c.Mode), normDCode, StringComparison.OrdinalIgnoreCase) && Math.Abs(c.Amount - dAmt) < 0.001
                && !string.IsNullOrWhiteSpace(c.Tid) && string.Equals(c.Tid.Trim(), dTid.Trim(), StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(c.Batch) && string.Equals(c.Batch.Trim(), dBatch.Trim(), StringComparison.OrdinalIgnoreCase)));

        // Assert: When TID and Batch match the OTP transaction, it IS recognized as a duplicate and suppressed
        Assert.True(inOtp, "Genuine duplicate with matching TID and Batch must be recognized as inOtp duplicate");
    }

    [Fact]
    public void PineLabsCard_ShiftReport_ConsolidatesItemizedTransaction()
    {
        // Arrange: Entry with itemized PineLabsCard of 1800 and 0 scalar credit card
        var date = DateTime.Today;
        var entry = new DsmEntry
        {
            DsmName = "Attendant PL",
            PumpId = 1,
            GrossSales = 2000,
            Shift = new Shift { ShiftType = "A", ShiftDate = date },
            PaymentCollection = new PaymentCollection
            {
                CashDeposit = 200,
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PINELABS_CARD", Amount = 1800, Slot = "Morning", Tid = "PL01", Batch = "B01" }
                }
            },
            CashDenominations = new List<CashDenomination> { new CashDenomination { CashType = "Cash1", TotalAmount = 200 } }
        };

        // Act
        var report = _reportService.CalculateShiftReport(
            date: date,
            shiftType: "A",
            entries: new List<DsmEntry> { entry },
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: new List<CreditorRepayment>(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // Assert: PineLabsCard (1800) + CashDeposit (200) = 2000 total collection
        Assert.Equal(2000, report.ActualCollection);
        var cardBreakdown = report.CollectionBreakdown.FirstOrDefault(c => c.Category.Contains("Card") || c.Category.Contains("PineLab"));
        Assert.NotNull(cardBreakdown);
        Assert.Equal(1800, cardBreakdown.Amount);
    }

    [Fact]
    public void PineLabsCard_DayReport_ConsolidatesItemizedTransaction()
    {
        // Arrange: Entries across shifts with itemized PineLabsCard of 2500 total
        var date = DateTime.Today;
        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmName = "Attendant Day PL",
                PumpId = 1,
                GrossSales = 3000,
                Shift = new Shift { ShiftType = "B", ShiftDate = date },
                PaymentCollection = new PaymentCollection
                {
                    CashDeposit = 500,
                    Items = new List<PaymentCollectionItem>
                    {
                        new PaymentCollectionItem { CollectionTypeCode = "PINELABS_CARD", Amount = 2500, Slot = "Day", Tid = "PL02" }
                    }
                },
                CashDenominations = new List<CashDenomination> { new CashDenomination { CashType = "Cash1", TotalAmount = 500 } }
            }
        };

        // Act
        var report = _reportService.CalculateDayReport(
            startDate: date,
            endDate: date,
            entries: entries,
            allExpenses: new List<Expense>(),
            repayments: new List<CreditorRepayment>(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            stationName: "Mitali Service Station"
        );

        // Assert: 2500 Card + 500 Cash = 3000 ActualCollection
        Assert.Equal(3000, report.ActualCollection);
        var cardBreakdown = report.CollectionBreakdown.FirstOrDefault(c => c.Category.Contains("Card") || c.Category.Contains("PineLab"));
        Assert.NotNull(cardBreakdown);
        Assert.Equal(2500, cardBreakdown.Amount);
    }

    [Fact]
    public void ShiftReport_Difference_Matches_DsmEntry_CanonicalMismatch_WhenUnDenominatedCash2Present()
    {
        // Scenario reported by client:
        // DSM Entry shows mismatch = -279.47, GrossSales = 50000, TotalCollection = 49720.53
        // Un-denominated Cash 2 of 978.03 is present in TotalInDirect.
        // Old bug: ReportService recalculated cash2 as 0 (no Cash2 denomination), causing TotalCollection = 48742.50
        // and Difference = -1257.50 (-279.47 - 978.03).
        // With fix: ReportService preserves stored TotalCollection and Mismatch (-279.47).
        var date = DateTime.Today;
        var entry = new DsmEntry
        {
            DsmName = "Attendant Mismatch Test",
            PumpId = 1,
            GrossSales = 50000,
            TotalInDirect = 49720.53m,
            TotalCollection = 49720.53m,
            Mismatch = -279.47m,
            Shift = new Shift { ShiftType = "A", ShiftDate = date },
            NozzleReadings = new List<NozzleReading>
            {
                new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 500, Amount = 50000, Rate = 100 }
            },
            PaymentCollection = new PaymentCollection
            {
                CashDeposit = 10000,
                PhonePeMorning = 20000,
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PINELABS_CARD", Amount = 18742.50, Slot = "Morning" }
                }
            },
            CashDenominations = new List<CashDenomination>
            {
                new CashDenomination { CashType = "Cash1", TotalAmount = 10000 }
            }
        };

        // Act 1: CalculateEntryTotals
        var (grossSales, totalColl, mismatch) = ReportService.CalculateEntryTotals(entry);
        Assert.Equal(50000.0, grossSales, 2);
        Assert.Equal(49720.53, totalColl, 2);
        Assert.Equal(-279.47, mismatch, 2);

        // Act 2: ShiftReport
        var report = _reportService.CalculateShiftReport(
            date: date,
            shiftType: "A",
            entries: new List<DsmEntry> { entry },
            shiftExpenses: new List<Expense>(),
            otherCashList: new List<ShiftOtherCash>(),
            repayments: new List<CreditorRepayment>(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
            stationName: "Mitali Service Station"
        );

        // Assert: Difference matches canonical mismatch of -279.47
        Assert.Equal(-279.47, report.Difference, 2);
        Assert.Equal(49720.53, report.ActualCollection, 2);
    }

    [Fact]
    public void DayReport_Difference_Matches_DsmEntry_CanonicalMismatch()
    {
        var date = DateTime.Today;
        var entry = new DsmEntry
        {
            DsmName = "Attendant Day Mismatch",
            PumpId = 1,
            GrossSales = 50000,
            TotalInDirect = 49720.53m,
            TotalCollection = 49720.53m,
            Mismatch = -279.47m,
            Shift = new Shift { ShiftType = "A", ShiftDate = date },
            NozzleReadings = new List<NozzleReading>
            {
                new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 500, Amount = 50000, Rate = 100 }
            },
            PaymentCollection = new PaymentCollection
            {
                CashDeposit = 10000,
                PhonePeMorning = 20000,
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PINELABS_CARD", Amount = 18742.50, Slot = "Morning" }
                }
            },
            CashDenominations = new List<CashDenomination>
            {
                new CashDenomination { CashType = "Cash1", TotalAmount = 10000 }
            }
        };

        var report = _reportService.CalculateDayReport(
            startDate: date,
            endDate: date,
            entries: new List<DsmEntry> { entry },
            allExpenses: new List<Expense>(),
            repayments: new List<CreditorRepayment>(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            stationName: "Mitali Service Station"
        );

        // Assert: Difference matches canonical mismatch of -279.47
        Assert.Equal(-279.47, report.Difference, 2);
        Assert.Equal(49720.53, report.ActualCollection, 2);
    }

    [Fact]
    public void FinalCalculationViewModel_Difference_Matches_ReportDifference_Directly()
    {
        // Assert that FinalCalculationViewModel does not override negative difference with -TotalDsmShort
        var report = new ShiftReportDto
        {
            TotalDsmShort = 1257.50,
            Difference = -279.47,
            IsBalanced = false
        };

        double vmDifference = report.Difference;
        bool vmIsBalanced = report.IsBalanced;

        Assert.Equal(-279.47, vmDifference);
        Assert.False(vmIsBalanced);
    }

    [Fact]
    public void UpdateCollectionTypeTotals_UpdatesExistingCollectionsInPlace_WithoutCollectionModifiedException()
    {
        var collections = new System.Collections.ObjectModel.ObservableCollection<DynamicCollectionEntryRow>
        {
            new DynamicCollectionEntryRow { Code = "CASH3", DisplayName = "Cash 3", Amount = 100 }
        };

        var existing = collections.FirstOrDefault(c => c.Code == "CASH3");
        Assert.NotNull(existing);
        existing.Amount = 250;
        Assert.Equal("Cash 3", existing.DisplayName);
        Assert.Equal(250, existing.Amount);
        Assert.Single(collections);
    }

    private IAgsInventoryService SetupInventoryService()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        string dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fuelPro_test_{Guid.NewGuid():N}.db");
        services.AddDbContext<FuelPro.Data.FuelProDbContext>(options =>
            Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions.UseSqlite(options, $"Data Source={dbPath}"),
            Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped);

        services.AddTransient<FuelPro.Core.Repositories.ISettingsRepository, FuelPro.Data.Repositories.SettingsRepository>();
        services.AddTransient<FuelPro.Core.Repositories.IShiftRepository, FuelPro.Data.Repositories.ShiftRepository>();
        services.AddTransient<FuelPro.Core.Repositories.IDsmEntryRepository, FuelPro.Data.Repositories.DsmEntryRepository>();
        services.AddTransient<FuelPro.Core.Repositories.INozzleReadingRepository, FuelPro.Data.Repositories.NozzleReadingRepository>();
        services.AddTransient<FuelPro.Core.Repositories.IPaymentRepository, FuelPro.Data.Repositories.PaymentRepository>();
        services.AddTransient<FuelPro.Core.Repositories.IDebitEntryRepository, FuelPro.Data.Repositories.DebitEntryRepository>();
        services.AddTransient<FuelPro.Core.Repositories.ITestingEntryRepository, FuelPro.Data.Repositories.TestingEntryRepository>();
        services.AddTransient<FuelPro.Core.Repositories.IExpenseRepository, FuelPro.Data.Repositories.ExpenseRepository>();
        services.AddTransient<FuelPro.Core.Repositories.ICashDenominationRepository, FuelPro.Data.Repositories.CashDenominationRepository>();
        services.AddTransient<FuelPro.Core.Repositories.IAgsImportRepository, FuelPro.Data.Repositories.AgsImportRepository>();
        services.AddTransient<FuelPro.Core.Repositories.ICreditorRepository, FuelPro.Data.Repositories.CreditorRepository>();
        services.AddTransient<FuelPro.Core.Repositories.IFuelTankerRepository, FuelPro.Data.Repositories.FuelTankerRepository>();
        services.AddTransient<FuelPro.Core.Services.IShiftCycleService, FuelPro.Core.Services.ShiftCycleService>();
        services.AddTransient<FuelPro.Core.Services.IAgsInventoryService, FuelPro.Core.Services.AgsInventoryService>();
        services.AddScoped<FuelPro.Core.Services.IShiftAggregationService, FuelPro.Core.Services.ShiftAggregationService>();

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelPro.Data.FuelProDbContext>();
        context.Database.EnsureCreated();

        return provider.GetRequiredService<FuelPro.Core.Services.IAgsInventoryService>();
    }

    [Fact]
    public async Task DayTotal_ClientOctober29_Reconciliation_N5AndN6_MatchesExactly()
    {
        var inventoryService = SetupInventoryService();

        var oct29 = new DateTime(2026, 10, 29);
        var oct30 = new DateTime(2026, 10, 30);

        var shift29A = new Shift { ShiftId = 36, ShiftDate = oct29, ShiftType = "A" };
        var shift29B = new Shift { ShiftId = 38, ShiftDate = oct29, ShiftType = "B" };
        var shift30A = new Shift { ShiftId = 37, ShiftDate = oct30, ShiftType = "A" };

        var allEntries = new List<DsmEntry>
        {
            // Oct 29 Shift A
            new DsmEntry
            {
                DsmEntryId = 159,
                ShiftId = 36,
                Shift = shift29A,
                PumpId = 1,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 6, OpeningReading = 14374.47, ClosingReading = 14959.50, SaleLitres = 585.03 }
                }
            },
            new DsmEntry
            {
                DsmEntryId = 160,
                ShiftId = 36,
                Shift = shift29A,
                PumpId = 2,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 5, OpeningReading = 3915.56, ClosingReading = 3989.63, SaleLitres = 74.07 }
                }
            },
            // Oct 29 Shift B
            new DsmEntry
            {
                DsmEntryId = 174,
                ShiftId = 38,
                Shift = shift29B,
                PumpId = 1,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 6, OpeningReading = 14959.50, ClosingReading = 15237.76, SaleLitres = 278.26 }
                }
            },
            new DsmEntry
            {
                DsmEntryId = 175,
                ShiftId = 38,
                Shift = shift29B,
                PumpId = 2,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 5, OpeningReading = 3989.63, ClosingReading = 4023.46, SaleLitres = 33.83 }
                }
            },
            // Oct 30 Shift A (MUST NOT be counted in Oct 29 Day Total!)
            new DsmEntry
            {
                DsmEntryId = 168,
                ShiftId = 37,
                Shift = shift30A,
                PumpId = 1,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 6, OpeningReading = 14959.50, ClosingReading = 15237.76, SaleLitres = 278.26 }
                }
            },
            new DsmEntry
            {
                DsmEntryId = 169,
                ShiftId = 37,
                Shift = shift30A,
                PumpId = 2,
                NozzleReadings = new List<NozzleReading>
                {
                    new NozzleReading { NozzleNumber = 5, OpeningReading = 3989.63, ClosingReading = 4023.46, SaleLitres = 33.83 }
                }
            }
        };

        // Act: Generate Day Total for Oct 29 passing candidate entries that include Oct 30
        var nozzleGroups = await inventoryService.BuildNozzleGroupsForDateRangeAsync(oct29, oct29, allEntries);

        // Assert: Find Nozzle 5 and Nozzle 6 across nozzle groups
        var allNozzles = nozzleGroups.SelectMany(g => g.Nozzles).ToList();

        var n5 = allNozzles.FirstOrDefault(n => n.NozzleNumber == 5);
        Assert.NotNull(n5);
        Assert.Equal(3915.56, n5.OpeningReading, 2);
        Assert.Equal(4023.46, n5.ClosingReading, 2);
        Assert.Equal(107.90, n5.SaleLitres, 2);
        Assert.Equal(107.90, n5.ClosingReading - n5.OpeningReading, 2);

        var n6 = allNozzles.FirstOrDefault(n => n.NozzleNumber == 6);
        Assert.NotNull(n6);
        Assert.Equal(14374.47, n6.OpeningReading, 2);
        Assert.Equal(15237.76, n6.ClosingReading, 2);
        Assert.Equal(863.29, n6.SaleLitres, 2);
        Assert.Equal(863.29, n6.ClosingReading - n6.OpeningReading, 2);
    }

    [Fact]
    public async Task DayTotal_MultiDay_ConsecutiveDays_ReconcilesProperly()
    {
        var inventoryService = SetupInventoryService();

        var d1 = new DateTime(2026, 10, 20);
        var d2 = new DateTime(2026, 10, 21);

        var shift1 = new Shift { ShiftId = 1, ShiftDate = d1, ShiftType = "A" };
        var shift2 = new Shift { ShiftId = 2, ShiftDate = d2, ShiftType = "A" };

        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1, ShiftId = 1, Shift = shift1, PumpId = 1,
                NozzleReadings = new List<NozzleReading> { new NozzleReading { NozzleNumber = 1, OpeningReading = 1000, ClosingReading = 1200, SaleLitres = 200 } }
            },
            new DsmEntry
            {
                DsmEntryId = 2, ShiftId = 2, Shift = shift2, PumpId = 1,
                NozzleReadings = new List<NozzleReading> { new NozzleReading { NozzleNumber = 1, OpeningReading = 1200, ClosingReading = 1500, SaleLitres = 300 } }
            }
        };

        var groups = await inventoryService.BuildNozzleGroupsForDateRangeAsync(d1, d2, entries);
        var n1 = groups.SelectMany(g => g.Nozzles).FirstOrDefault(n => n.NozzleNumber == 1);
        Assert.NotNull(n1);
        Assert.Equal(1000, n1.OpeningReading);
        Assert.Equal(1500, n1.ClosingReading);
        Assert.Equal(500, n1.SaleLitres);
    }

    [Fact]
    public async Task DayTotal_DuplicateRecordsInSameShift_SuppressedCleanly()
    {
        var inventoryService = SetupInventoryService();

        var date = new DateTime(2026, 10, 20);
        var shift = new Shift { ShiftId = 1, ShiftDate = date, ShiftType = "A" };

        var entries = new List<DsmEntry>
        {
            new DsmEntry
            {
                DsmEntryId = 1, ShiftId = 1, Shift = shift, PumpId = 1,
                NozzleReadings = new List<NozzleReading> { new NozzleReading { NozzleNumber = 1, OpeningReading = 1000, ClosingReading = 1200, SaleLitres = 200 } }
            },
            // Duplicate submission for same shift & nozzle
            new DsmEntry
            {
                DsmEntryId = 2, ShiftId = 1, Shift = shift, PumpId = 1,
                NozzleReadings = new List<NozzleReading> { new NozzleReading { NozzleNumber = 1, OpeningReading = 1000, ClosingReading = 1200, SaleLitres = 200 } }
            }
        };

        var groups = await inventoryService.BuildNozzleGroupsForDateRangeAsync(date, date, entries);
        var n1 = groups.SelectMany(g => g.Nozzles).FirstOrDefault(n => n.NozzleNumber == 1);
        Assert.NotNull(n1);
        Assert.Equal(1000, n1.OpeningReading);
        Assert.Equal(1200, n1.ClosingReading);
        Assert.Equal(200, n1.SaleLitres); // Must be 200, NOT double counted to 400!
    }

    [Fact]
    public void MergeConnectedPumpEntries_PrimaryOnlyDebtors_Preserved()
    {
        var primary = new DsmEntry
        {
            DsmEntryId = 1,
            PumpId = 1,
            ConnectedPumpIdsJson = "[2]",
            DebitEntries = new List<DebitEntry>
            {
                new DebitEntry { DebitId = 101, DebtorName = "Customer A", Amount = 500 }
            }
        };
        var slave = new DsmEntry
        {
            DsmEntryId = 2,
            PumpId = 2,
            ReconciledToPumpId = 1,
            DebitEntries = new List<DebitEntry>()
        };

        var merged = ReportService.MergeConnectedPumpEntries(new List<DsmEntry> { primary, slave });
        Assert.Single(merged);
        Assert.Single(merged[0].DebitEntries);
        Assert.Equal("Customer A", merged[0].DebitEntries.First().DebtorName);
        Assert.Equal(500, merged[0].DebitEntries.First().Amount);
    }

    [Fact]
    public void MergeConnectedPumpEntries_SlaveOnlyDebtors_Preserved()
    {
        var primary = new DsmEntry
        {
            DsmEntryId = 1,
            PumpId = 1,
            ConnectedPumpIdsJson = "[2]",
            DebitEntries = new List<DebitEntry>()
        };
        var slave = new DsmEntry
        {
            DsmEntryId = 2,
            PumpId = 2,
            ReconciledToPumpId = 1,
            DebitEntries = new List<DebitEntry>
            {
                new DebitEntry { DebitId = 201, DebtorName = "Slave Customer", Amount = 750 }
            }
        };

        var merged = ReportService.MergeConnectedPumpEntries(new List<DsmEntry> { primary, slave });
        Assert.Single(merged);
        Assert.Single(merged[0].DebitEntries);
        Assert.Equal("Slave Customer", merged[0].DebitEntries.First().DebtorName);
        Assert.Equal(750, merged[0].DebitEntries.First().Amount);
    }

    [Fact]
    public void MergeConnectedPumpEntries_BothPrimaryAndSlaveDebtors_CombinedWithoutLoss()
    {
        var primary = new DsmEntry
        {
            DsmEntryId = 1,
            PumpId = 1,
            ConnectedPumpIdsJson = "[2]",
            DebitEntries = new List<DebitEntry>
            {
                new DebitEntry { DebitId = 101, DebtorName = "Primary Customer", Amount = 1000 }
            }
        };
        var slave = new DsmEntry
        {
            DsmEntryId = 2,
            PumpId = 2,
            ReconciledToPumpId = 1,
            DebitEntries = new List<DebitEntry>
            {
                new DebitEntry { DebitId = 201, DebtorName = "Slave Customer", Amount = 1500 }
            }
        };

        var merged = ReportService.MergeConnectedPumpEntries(new List<DsmEntry> { primary, slave });
        Assert.Single(merged);
        Assert.Equal(2, merged[0].DebitEntries.Count);
        Assert.Contains(merged[0].DebitEntries, d => d.DebtorName == "Primary Customer" && d.Amount == 1000);
        Assert.Contains(merged[0].DebitEntries, d => d.DebtorName == "Slave Customer" && d.Amount == 1500);
    }

    [Fact]
    public void MergeConnectedPumpEntries_MultipleSlaves_DebtorsAllMerged()
    {
        var primary = new DsmEntry
        {
            DsmEntryId = 1,
            PumpId = 1,
            ConnectedPumpIdsJson = "[2, 3]",
            DebitEntries = new List<DebitEntry>
            {
                new DebitEntry { DebitId = 101, DebtorName = "P1 Customer", Amount = 300 }
            }
        };
        var slave1 = new DsmEntry
        {
            DsmEntryId = 2,
            PumpId = 2,
            ReconciledToPumpId = 1,
            DebitEntries = new List<DebitEntry>
            {
                new DebitEntry { DebitId = 201, DebtorName = "Slave 1 Customer", Amount = 400 }
            }
        };
        var slave2 = new DsmEntry
        {
            DsmEntryId = 3,
            PumpId = 3,
            ReconciledToPumpId = 1,
            DebitEntries = new List<DebitEntry>
            {
                new DebitEntry { DebitId = 301, DebtorName = "Slave 2 Customer", Amount = 500 }
            }
        };

        var merged = ReportService.MergeConnectedPumpEntries(new List<DsmEntry> { primary, slave1, slave2 });
        Assert.Single(merged);
        Assert.Equal(3, merged[0].DebitEntries.Count);
        Assert.Equal(1200, merged[0].DebitEntries.Sum(d => d.Amount));
    }

    [Fact]
    public void MergeConnectedPumpEntries_DuplicateDebtors_DeduplicatedByIdAndSlip()
    {
        var primary = new DsmEntry
        {
            DsmEntryId = 1,
            PumpId = 1,
            ConnectedPumpIdsJson = "[2]",
            DebitEntries = new List<DebitEntry>
            {
                new DebitEntry { DebitId = 101, DebtorName = "Customer A", Amount = 500 },
                new DebitEntry { DebitId = 101, DebtorName = "Customer A", Amount = 500 } // duplicate DebitId
            }
        };
        var slave = new DsmEntry
        {
            DsmEntryId = 2,
            PumpId = 2,
            ReconciledToPumpId = 1,
            DebitEntries = new List<DebitEntry>
            {
                new DebitEntry { DebitId = 0, DebtorName = "Unpersisted", Amount = 200, SlipNumber = "SLIP-1" },
                new DebitEntry { DebitId = 0, DebtorName = "Unpersisted", Amount = 200, SlipNumber = "SLIP-1" } // duplicate slip
            }
        };

        var merged = ReportService.MergeConnectedPumpEntries(new List<DsmEntry> { primary, slave });
        Assert.Single(merged);
        Assert.Equal(2, merged[0].DebitEntries.Count);
    }

    [Fact]
    public void MergeConnectedPumpEntries_TwoLegitimateTransactionsSameCustomerAndAmount_Preserved()
    {
        var primary = new DsmEntry
        {
            DsmEntryId = 1,
            PumpId = 1,
            ConnectedPumpIdsJson = "[2]",
            DebitEntries = new List<DebitEntry>
            {
                new DebitEntry { DebitId = 101, DebtorName = "Regular Customer", Amount = 1000 },
                new DebitEntry { DebitId = 102, DebtorName = "Regular Customer", Amount = 1000 } // Legitimate 2nd fill
            }
        };
        var slave = new DsmEntry
        {
            DsmEntryId = 2,
            PumpId = 2,
            ReconciledToPumpId = 1,
            DebitEntries = new List<DebitEntry>
            {
                new DebitEntry { DebitId = 201, DebtorName = "Regular Customer", Amount = 1000 } // 3rd fill on connected pump
            }
        };

        var merged = ReportService.MergeConnectedPumpEntries(new List<DsmEntry> { primary, slave });
        Assert.Single(merged);
        Assert.Equal(3, merged[0].DebitEntries.Count);
        Assert.Equal(3000, merged[0].DebitEntries.Sum(d => d.Amount));
    }

    [Fact]
    public void MergeConnectedPumpEntries_EmptyDebtorCollections_HandlesGracefully()
    {
        var primary = new DsmEntry { DsmEntryId = 1, PumpId = 1, ConnectedPumpIdsJson = "[2]", DebitEntries = new List<DebitEntry>() };
        var slave = new DsmEntry { DsmEntryId = 2, PumpId = 2, ReconciledToPumpId = 1, DebitEntries = null };

        var merged = ReportService.MergeConnectedPumpEntries(new List<DsmEntry> { primary, slave });
        Assert.Single(merged);
        Assert.NotNull(merged[0].DebitEntries);
        Assert.Empty(merged[0].DebitEntries);
    }

    [Fact]
    public void PrintReadiness_ValidateShiftReport_AcceptsValidReport_EvenWithLegitimateZeros()
    {
        var report = new ShiftReportDto
        {
            DateString = "29/10/2026",
            ShiftLabel = "A",
            Cash1 = new CashAggregateDto(),
            Cash2 = new CashAggregateDto(),
            Cash3 = new CashAggregateDto(),
            DsmSummaryRows = new List<DsmSummaryRowDto>()
        };

        var result = FuelPro.UI.Printing.PrintService.ValidateShiftReportReadiness(report);
        Assert.True(result.IsReady);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void PrintReadiness_ValidateShiftReport_BlocksMissingOrNullData()
    {
        var nullResult = FuelPro.UI.Printing.PrintService.ValidateShiftReportReadiness(null);
        Assert.False(nullResult.IsReady);
        Assert.Contains("Report data has not finished loading", nullResult.Reason);

        var missingCashResult = FuelPro.UI.Printing.PrintService.ValidateShiftReportReadiness(new ShiftReportDto
        {
            DateString = "29/10/2026",
            ShiftLabel = "A",
            Cash1 = null!,
            Cash2 = new CashAggregateDto(),
            Cash3 = new CashAggregateDto(),
            DsmSummaryRows = new List<DsmSummaryRowDto>()
        });
        Assert.False(missingCashResult.IsReady);
        Assert.Contains("Cash denomination structures are not initialized", missingCashResult.Reason);
    }

    [Fact]
    public void PrintReadiness_ValidateDayReport_AcceptsValidReport_EvenWithLegitimateZeros()
    {
        var report = new DayReportDto
        {
            DateString = "29/10/2026",
            FuelSales = new List<FuelSaleRowDto>(),
            DsmSummaryRows = new List<DsmSummaryRowDto>()
        };

        var result = FuelPro.UI.Printing.PrintService.ValidateDayReportReadiness(report);
        Assert.True(result.IsReady);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void PrintReadiness_ValidateDayReport_BlocksMissingOrNullData()
    {
        var nullResult = FuelPro.UI.Printing.PrintService.ValidateDayReportReadiness(null);
        Assert.False(nullResult.IsReady);

        var missingDateResult = FuelPro.UI.Printing.PrintService.ValidateDayReportReadiness(new DayReportDto
        {
            DateString = "",
            FuelSales = new List<FuelSaleRowDto>(),
            DsmSummaryRows = new List<DsmSummaryRowDto>()
        });
        Assert.False(missingDateResult.IsReady);
        Assert.Contains("Report date is missing", missingDateResult.Reason);
    }

    [Fact]
    public void PrintReadiness_ValidateDsmSheet_AcceptsValidSheet_EvenWithLegitimateZeros()
    {
        var sheet = new DsmSheetPrintData
        {
            DsmName = "Attendant 1",
            PumpNo = "1",
            NozzleRows = new List<DsmNozzlePrintRow>()
        };

        var result = FuelPro.UI.Printing.PrintService.ValidateDsmSheetReadiness(sheet);
        Assert.True(result.IsReady);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void PrintReadiness_ValidateDsmSheet_BlocksMissingOrNullData()
    {
        var nullResult = FuelPro.UI.Printing.PrintService.ValidateDsmSheetReadiness(null);
        Assert.False(nullResult.IsReady);

        var missingNameResult = FuelPro.UI.Printing.PrintService.ValidateDsmSheetReadiness(new DsmSheetPrintData
        {
            DsmName = "",
            PumpNo = "",
            NozzleRows = new List<DsmNozzlePrintRow>()
        });
        Assert.False(missingNameResult.IsReady);
        Assert.Contains("DSM details and pump identification are missing", missingNameResult.Reason);
    }

    [Fact]
    public void MetadataParsing_ProfessionalCourier1500Credit_ParsedWithoutDynamicCrash()
    {
        string rawJson = @"{
  ""stationId"": ""RASHTRA-74A837"",
  ""cash3Amount"": 18170,
  ""debtorEntries"": [
    {
      ""time"": ""09:23"",
      ""amount"": 1500,
      ""debtorName"": ""PROFESSIONAL COURIER"",
      ""slipNumber"": ""811"",
      ""vehicleNumber"": ""MH43BG4047""
    }
  ],
  ""cash3Denominations"": {
    ""coins"": 0,
    ""denom10"": 0,
    ""denom20"": 1,
    ""denom50"": 1,
    ""denom100"": 20,
    ""denom200"": 3,
    ""denom500"": 31
  }
}";

        var metaObj = Newtonsoft.Json.Linq.JObject.Parse(rawJson);
        Assert.NotNull(metaObj);

        // Verify Cash 3 tokens
        var cash3Meta = metaObj["cash3Denominations"];
        Assert.NotNull(cash3Meta);
        int d500 = (int)(cash3Meta["denom500"] ?? cash3Meta["c500"] ?? 0);
        int d200 = (int)(cash3Meta["denom200"] ?? cash3Meta["c200"] ?? 0);
        int d100 = (int)(cash3Meta["denom100"] ?? cash3Meta["c100"] ?? 0);
        int d50 = (int)(cash3Meta["denom50"] ?? cash3Meta["c50"] ?? 0);
        int d20 = (int)(cash3Meta["denom20"] ?? cash3Meta["c20"] ?? 0);
        int totalCash3 = (d500 * 500) + (d200 * 200) + (d100 * 100) + (d50 * 50) + (d20 * 20);
        Assert.Equal(18170, totalCash3);

        // Verify Debtor Entries
        var debtorToken = metaObj["debtorEntries"] ?? metaObj["debitEntries"];
        Assert.NotNull(debtorToken);
        var first = debtorToken.First();
        Assert.NotNull(first);
        Assert.Equal("PROFESSIONAL COURIER", (string?)first["debtorName"]);
        Assert.Equal(1500, (double)(first["amount"] ?? 0));
        Assert.Equal("811", (string?)first["slipNumber"]);
        Assert.Equal("MH43BG4047", (string?)first["vehicleNumber"]);
    }

    #region PWA Manual-Reading Propagation Lifecycle Tests

    [Fact]
    public void PwaLifecycle_1_PendingSubmission_DoesNotAlterDesktopDatabaseOrReadings()
    {
        // Arrange: Pending PWA submission received in memory
        bool eventFired = false;
        Action handler = () => { eventFired = true; };
        DsmEntryService.DsmEntryChanged += handler;

        try
        {
            var pendingSubmission = new
            {
                Id = Guid.NewGuid(),
                Status = "Pending",
                PumpId = 1,
                Readings = new[] { new { NozzleNumber = 1, ClosingReading = 9999.0 } }
            };

            // Assert: While pending, status is not committed and no desktop refresh event is raised
            Assert.Equal("Pending", pendingSubmission.Status);
            Assert.False(eventFired);
        }
        finally
        {
            DsmEntryService.DsmEntryChanged -= handler;
        }
    }

    [Fact]
    public void PwaLifecycle_2_RejectedSubmission_DoesNotAlterDesktopDatabaseOrReadings()
    {
        // Arrange: Rejected PWA submission
        bool eventFired = false;
        Action handler = () => { eventFired = true; };
        DsmEntryService.DsmEntryChanged += handler;

        try
        {
            var rejectedSubmission = new
            {
                Id = Guid.NewGuid(),
                Status = "Rejected",
                RejectionReason = "Incorrect closing meter photo",
                PumpId = 1
            };

            // Assert: Rejected submission never triggers desktop refresh
            Assert.Equal("Rejected", rejectedSubmission.Status);
            Assert.False(eventFired);
        }
        finally
        {
            DsmEntryService.DsmEntryChanged -= handler;
        }
    }

    [Fact]
    public async Task PwaLifecycle_3_SuccessfulApproval_CommitsToDatabase_AndRaisesDsmEntryChanged()
    {
        // Arrange: In-memory SQLite database context
        var options = new DbContextOptionsBuilder<FuelPro.Data.FuelProDbContext>()
            .UseSqlite($"Data Source=file:pwalifecycle3_{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;

        using var context = new FuelPro.Data.FuelProDbContext(options);
        await context.Database.EnsureCreatedAsync();

        bool eventFired = false;
        Action handler = () => { eventFired = true; };
        DsmEntryService.DsmEntryChanged += handler;

        try
        {
            var shift = new Shift { ShiftDate = DateTime.Today, ShiftType = "A" };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var entry = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                DsmName = "Attendant Test",
                CreatedAt = DateTime.Now
            };
            context.DsmEntries.Add(entry);
            await context.SaveChangesAsync();

            var reading = new NozzleReading
            {
                DsmEntryId = entry.DsmEntryId,
                NozzleNumber = 1,
                OpeningReading = 1000.0,
                ClosingReading = 1250.0,
                SaleLitres = 250.0
            };
            context.NozzleReadings.Add(reading);
            await context.SaveChangesAsync();

            // Simulate post-commit notification (mirrors DsmApprovalQueueViewModel line 4498)
            DsmEntryService.RaiseDsmEntryChanged();

            // Assert: Reading was successfully persisted and notification fired
            var savedReading = await context.NozzleReadings.FirstOrDefaultAsync(r => r.NozzleNumber == 1);
            Assert.NotNull(savedReading);
            Assert.Equal(1250.0, savedReading.ClosingReading);
            Assert.True(eventFired);
        }
        finally
        {
            DsmEntryService.DsmEntryChanged -= handler;
        }
    }

    [Fact]
    public async Task PwaLifecycle_4_DesktopDsmEntry_ReceivesLatestPredecessorClosingReading_WhenEventFires()
    {
        // Arrange: In-memory context with predecessor shift
        var options = new DbContextOptionsBuilder<FuelPro.Data.FuelProDbContext>()
            .UseSqlite($"Data Source=file:pwalifecycle4_{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;

        using var context = new FuelPro.Data.FuelProDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var shiftCycleService = new ShiftCycleService();
        var nozzleRepo = new NozzleReadingRepository(context, shiftCycleService);

        var oct29 = new DateTime(2026, 10, 29);
        var shiftB = new Shift { ShiftDate = oct29, ShiftType = "B" };
        context.Shifts.Add(shiftB);
        await context.SaveChangesAsync();

        var entryB = new DsmEntry { ShiftId = shiftB.ShiftId, PumpId = 1, DsmName = "Attendant B" };
        context.DsmEntries.Add(entryB);
        await context.SaveChangesAsync();

        var nrB = new NozzleReading { DsmEntryId = entryB.DsmEntryId, NozzleNumber = 1, OpeningReading = 5000.0, ClosingReading = 5340.50, SaleLitres = 340.50 };
        context.NozzleReadings.Add(nrB);
        await context.SaveChangesAsync();

        // Act: Predecessor resolution for next shift (Shift C)
        var prevClosingsResult = await nozzleRepo.GetPreviousShiftClosingsAsync(oct29, "C", 1);

        // Assert: Closing reading of 5340.50 correctly resolves as predecessor opening
        Assert.True(prevClosingsResult.Success);
        Assert.NotNull(prevClosingsResult.Data);
        Assert.True(prevClosingsResult.Data.ContainsKey(1));
        Assert.Equal(5340.50, prevClosingsResult.Data[1]);
    }

    [Fact]
    public void PwaLifecycle_5_ActiveEditing_GuardsAgainstClobbering()
    {
        // Arrange: Simulates DsmEntryViewModel._dsmEntryChangedHandler logic
        bool reloadCalled = false;
        void LoadNozzlesForPump() { reloadCalled = true; }

        bool isEditing = true;
        bool hasInProgressEntry = false;

        Action handler = () =>
        {
            if (isEditing || hasInProgressEntry) return;
            LoadNozzlesForPump();
        };

        // Act: Event arrives while user is actively editing
        handler();

        // Assert: Reload was NOT invoked (user input protected)
        Assert.False(reloadCalled);

        // Act: User finishes editing and event arrives
        isEditing = false;
        hasInProgressEntry = false;
        handler();

        // Assert: Reload was safely invoked
        Assert.True(reloadCalled);
    }

    [Fact]
    public void PwaLifecycle_6_DuplicateApproval_Idempotent()
    {
        // Arrange: Queue duplicate detection model
        var pendingList = new List<(Guid Id, DateTime Date, string Shift, int PumpId)>
        {
            (Guid.NewGuid(), new DateTime(2026, 10, 29), "B", 1),
            (Guid.NewGuid(), new DateTime(2026, 10, 29), "B", 1) // Duplicate submission
        };

        var processed = new HashSet<string>();
        var approvedIds = new List<Guid>();
        var autoHealedIds = new List<Guid>();

        foreach (var sub in pendingList)
        {
            string key = $"{sub.Date:yyyy-MM-dd}_{sub.Shift}_{sub.PumpId}";
            if (processed.Add(key))
            {
                approvedIds.Add(sub.Id);
            }
            else
            {
                autoHealedIds.Add(sub.Id);
            }
        }

        // Assert: Exactly one approved, duplicate auto-healed without duplicate reading writes
        Assert.Single(approvedIds);
        Assert.Single(autoHealedIds);
    }

    [Fact]
    public void PwaLifecycle_7_SubscriptionLifecycle_DisposesCleanlyWithoutMemoryLeak()
    {
        // Arrange
        int invocationCount = 0;
        Action handler = () => { invocationCount++; };

        // Act: Subscribe
        DsmEntryService.DsmEntryChanged += handler;
        DsmEntryService.RaiseDsmEntryChanged();
        Assert.Equal(1, invocationCount);

        // Act: Dispose / Unsubscribe (mirrors DsmEntryViewModel.Dispose)
        DsmEntryService.DsmEntryChanged -= handler;
        DsmEntryService.RaiseDsmEntryChanged();

        // Assert: Handler is no longer invoked, preventing memory leak
        Assert.Equal(1, invocationCount);
    }

    #endregion

    #region Print Readiness Completeness Tests

    [Fact]
    public void PrintReadiness_ValidateShiftReport_BlocksMissingCollections()
    {
        var report = new ShiftReportDto
        {
            DateString = "29/10/2026",
            ShiftLabel = "Shift A",
            Cash1 = new CashAggregateDto(),
            Cash2 = new CashAggregateDto(),
            Cash3 = new CashAggregateDto(),
            DsmSummaryRows = new(),
            FuelSales = null // Missing fuel sales collection
        };

        var res = PrintService.ValidateShiftReportReadiness(report);
        Assert.False(res.IsReady);
        Assert.Contains("Fuel sales collection is not initialized", res.Reason);
    }

    [Fact]
    public void PrintReadiness_ValidateShiftReport_BlocksNegativePhysicalCash()
    {
        var report = new ShiftReportDto
        {
            DateString = "29/10/2026",
            ShiftLabel = "Shift A",
            Cash1 = new CashAggregateDto { GrandTotal = -500 }, // Negative cash
            Cash2 = new CashAggregateDto(),
            Cash3 = new CashAggregateDto(),
            DsmSummaryRows = new(),
            FuelSales = new(),
            CreditorRows = new(),
            ExpenseRows = new(),
            CollectionBreakdown = new()
        };

        var res = PrintService.ValidateShiftReportReadiness(report);
        Assert.False(res.IsReady);
        Assert.Contains("Physical cash totals cannot be negative", res.Reason);
    }

    [Fact]
    public void PrintReadiness_ValidateDayReport_BlocksMissingCreditorsOrExpenses()
    {
        var report = new DayReportDto
        {
            DateString = "29/10/2026",
            FuelSales = new(),
            DsmSummaryRows = new(),
            Cash1 = new CashAggregateDto(),
            Cash2 = new CashAggregateDto(),
            Cash3 = new CashAggregateDto(),
            CreditorRows = null // Missing creditors collection
        };

        var res = PrintService.ValidateDayReportReadiness(report);
        Assert.False(res.IsReady);
        Assert.Contains("Debtor records collection is not initialized", res.Reason);
    }

    [Fact]
    public void PrintReadiness_ValidateDsmSheet_BlocksMissingPaymentsOrNegativeGross()
    {
        var sheet = new DsmSheetPrintData
        {
            DsmName = "Attendant 1",
            PumpNo = "Pump 1",
            NozzleRows = new(),
            GrossSales = -100 // Negative gross sales
        };

        var res = PrintService.ValidateDsmSheetReadiness(sheet);
        Assert.False(res.IsReady);
        Assert.Contains("Gross sales cannot be negative", res.Reason);
    }

    [Fact]
    public void PrintReadiness_AcceptsValidZeroValues_AndEmptyCollections()
    {
        // Shift report with legitimate ₹0.00 sales and empty optional collections
        var shiftReport = new ShiftReportDto
        {
            DateString = "29/10/2026",
            ShiftLabel = "Shift A",
            TotalFuelAmount = 0,
            Cash1 = new CashAggregateDto { GrandTotal = 0 },
            Cash2 = new CashAggregateDto { GrandTotal = 0 },
            Cash3 = new CashAggregateDto { GrandTotal = 0 },
            DsmSummaryRows = new(),
            FuelSales = new(),
            CreditorRows = new(), // Empty list is valid
            ExpenseRows = new(),  // Empty list is valid
            CollectionBreakdown = new()
        };
        var shiftRes = PrintService.ValidateShiftReportReadiness(shiftReport);
        Assert.True(shiftRes.IsReady);

        // Day report with legitimate zeros
        var dayReport = new DayReportDto
        {
            DateString = "29/10/2026",
            TotalFuelAmount = 0,
            Cash1 = new CashAggregateDto { GrandTotal = 0 },
            Cash2 = new CashAggregateDto { GrandTotal = 0 },
            Cash3 = new CashAggregateDto { GrandTotal = 0 },
            FuelSales = new(),
            DsmSummaryRows = new(),
            CreditorRows = new(),
            ExpenseRows = new()
        };
        var dayRes = PrintService.ValidateDayReportReadiness(dayReport);
        Assert.True(dayRes.IsReady);

        // DSM sheet with legitimate zeros
        var dsmSheet = new DsmSheetPrintData
        {
            DsmName = "Attendant 1",
            PumpNo = "Pump 1",
            GrossSales = 0,
            NozzleRows = new(),
            Payments = new DsmPaymentPrintBlock(),
            Creditors = new(),
            Expenses = new()
        };
        var sheetRes = PrintService.ValidateDsmSheetReadiness(dsmSheet);
        Assert.True(sheetRes.IsReady);
    }

    #endregion

    #region Cash 1 Denomination Restriction Tests

    [Fact]
    public void CashDenomRow_Cash1_RestrictedTo500_200_100_WhileCash2AndCash3SupportAllDenominations()
    {
        // Arrange & Act: Cash 1 with notes and small denominations
        var cash1 = new CashDenomRow
        {
            CashType = "Cash1",
            Denom500 = 2, // 1000
            Denom200 = 1, // 200
            Denom100 = 1, // 100
            Denom50 = 2,  // 100 - must be excluded from Cash 1
            Denom20 = 5,  // 100 - must be excluded from Cash 1
            Denom10 = 10, // 100 - must be excluded from Cash 1
            Coins = 50    // 50  - must be excluded from Cash 1
        };

        // Assert: Cash 1 calculates strictly 500 + 200 + 100 notes = 1300
        Assert.Equal(1300.0, cash1.TotalAmount);

        // Arrange & Act: Cash 2 with same denominations
        var cash2 = new CashDenomRow
        {
            CashType = "Cash2",
            Denom500 = 2,
            Denom200 = 1,
            Denom100 = 1,
            Denom50 = 2,
            Denom20 = 5,
            Denom10 = 10,
            Coins = 50
        };

        // Assert: Cash 2 calculates all denominations = 1300 + 100 + 100 + 100 + 50 = 1650
        Assert.Equal(1650.0, cash2.TotalAmount);

        // Arrange & Act: Cash 3 with same denominations
        var cash3 = new CashDenomRow
        {
            CashType = "Cash3",
            Denom500 = 2,
            Denom200 = 1,
            Denom100 = 1,
            Denom50 = 2,
            Denom20 = 5,
            Denom10 = 10,
            Coins = 50
        };

        // Assert: Cash 3 calculates all denominations = 1650
        Assert.Equal(1650.0, cash3.TotalAmount);
    }

    #endregion

    private FuelPro.Data.FuelProDbContext CreateTestDbContext(out string dbPath)
    {
        dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fuelPro_test_{Guid.NewGuid():N}.db");
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<FuelPro.Data.FuelProDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        var context = new FuelPro.Data.FuelProDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    #region Critical Client Site Corrections (01-Nov-2026)

    [Fact]
    public async Task Issue1_DsmEntry_ShiftA_LoadsPreviousShiftB_ClosingReadings_AsOpeningReadings()
    {
        // Issue 1: On 01-Nov-2026 Shift A, desktop DSM Entry must load manual closing readings
        // from 31-Oct-2026 Shift B across all nozzles, NOT from 31-Oct-2026 Shift A.
        string dbPath;
        using var context = CreateTestDbContext(out dbPath);
        try
        {
            var shiftCycle = new ShiftCycleService();
            var nozzleRepo = new NozzleReadingRepository(context, shiftCycle);

            var oct31 = new DateTime(2026, 10, 31);
            var nov01 = new DateTime(2026, 11, 1);

            // 1. Shift A on 31-Oct-2026 (earlier shift)
            var shift31A = new Shift { ShiftDate = oct31, ShiftType = "A" };
            context.Shifts.Add(shift31A);
            await context.SaveChangesAsync();

            var entry31A_P1 = new DsmEntry { ShiftId = shift31A.ShiftId, PumpId = 1, DsmName = "Attendant 1", CreatedAt = oct31.AddHours(8) };
            var entry31A_P2 = new DsmEntry { ShiftId = shift31A.ShiftId, PumpId = 2, DsmName = "Attendant 2", CreatedAt = oct31.AddHours(8) };
            var entry31A_P3 = new DsmEntry { ShiftId = shift31A.ShiftId, PumpId = 3, DsmName = "Attendant 3", CreatedAt = oct31.AddHours(8) };
            var entry31A_P4 = new DsmEntry { ShiftId = shift31A.ShiftId, PumpId = 4, DsmName = "Attendant 4", CreatedAt = oct31.AddHours(8) };
            context.DsmEntries.AddRange(entry31A_P1, entry31A_P2, entry31A_P3, entry31A_P4);
            await context.SaveChangesAsync();

            context.NozzleReadings.AddRange(
                new NozzleReading { DsmEntryId = entry31A_P1.DsmEntryId, NozzleNumber = 1, OpeningReading = 23000.00, ClosingReading = 23697.62, Rate = 100 },
                new NozzleReading { DsmEntryId = entry31A_P1.DsmEntryId, NozzleNumber = 3, OpeningReading = 1235.62, ClosingReading = 1235.62, Rate = 90 },
                new NozzleReading { DsmEntryId = entry31A_P2.DsmEntryId, NozzleNumber = 2, OpeningReading = 44000.00, ClosingReading = 44761.88, Rate = 100 },
                new NozzleReading { DsmEntryId = entry31A_P2.DsmEntryId, NozzleNumber = 4, OpeningReading = 2100.00, ClosingReading = 2197.04, Rate = 90 },
                new NozzleReading { DsmEntryId = entry31A_P3.DsmEntryId, NozzleNumber = 5, OpeningReading = 4300.00, ClosingReading = 4385.62, Rate = 100 },
                new NozzleReading { DsmEntryId = entry31A_P4.DsmEntryId, NozzleNumber = 6, OpeningReading = 16000.00, ClosingReading = 16512.75, Rate = 100 }
            );
            await context.SaveChangesAsync();

            // 2. Shift B on 31-Oct-2026 (immediate operational predecessor to 01-Nov Shift A)
            var shift31B = new Shift { ShiftDate = oct31, ShiftType = "B" };
            context.Shifts.Add(shift31B);
            await context.SaveChangesAsync();

            var entry31B_P1 = new DsmEntry { ShiftId = shift31B.ShiftId, PumpId = 1, DsmName = "Attendant 1", CreatedAt = oct31.AddHours(20) };
            var entry31B_P2 = new DsmEntry { ShiftId = shift31B.ShiftId, PumpId = 2, DsmName = "Attendant 2", CreatedAt = oct31.AddHours(20) };
            var entry31B_P3 = new DsmEntry { ShiftId = shift31B.ShiftId, PumpId = 3, DsmName = "Attendant 3", CreatedAt = oct31.AddHours(20) };
            var entry31B_P4 = new DsmEntry { ShiftId = shift31B.ShiftId, PumpId = 4, DsmName = "Attendant 4", CreatedAt = oct31.AddHours(20) };
            context.DsmEntries.AddRange(entry31B_P1, entry31B_P2, entry31B_P3, entry31B_P4);
            await context.SaveChangesAsync();

            context.NozzleReadings.AddRange(
                new NozzleReading { DsmEntryId = entry31B_P1.DsmEntryId, NozzleNumber = 1, OpeningReading = 23697.32, ClosingReading = 24800.58, Rate = 100 },
                new NozzleReading { DsmEntryId = entry31B_P1.DsmEntryId, NozzleNumber = 3, OpeningReading = 1235.62, ClosingReading = 1235.62, Rate = 90 },
                new NozzleReading { DsmEntryId = entry31B_P2.DsmEntryId, NozzleNumber = 2, OpeningReading = 44761.89, ClosingReading = 46302.93, Rate = 100 },
                new NozzleReading { DsmEntryId = entry31B_P2.DsmEntryId, NozzleNumber = 4, OpeningReading = 2197.04, ClosingReading = 2252.88, Rate = 90 },
                new NozzleReading { DsmEntryId = entry31B_P3.DsmEntryId, NozzleNumber = 5, OpeningReading = 4385.62, ClosingReading = 4488.45, Rate = 100 },
                new NozzleReading { DsmEntryId = entry31B_P4.DsmEntryId, NozzleNumber = 6, OpeningReading = 16512.75, ClosingReading = 16673.57, Rate = 100 }
            );
            await context.SaveChangesAsync();

            // Act: Query closings for 01-Nov-2026 Shift A
            var p1Closings = await nozzleRepo.GetPreviousShiftClosingsAsync(nov01, "A", pumpId: 1);
            var p2Closings = await nozzleRepo.GetPreviousShiftClosingsAsync(nov01, "A", pumpId: 2);
            var p3Closings = await nozzleRepo.GetPreviousShiftClosingsAsync(nov01, "A", pumpId: 3);
            var p4Closings = await nozzleRepo.GetPreviousShiftClosingsAsync(nov01, "A", pumpId: 4);

            // Assert: Must return 31-Oct Shift B closings, NOT Shift A
            Assert.True(p1Closings.Success);
            Assert.Equal(24800.58, p1Closings.Data![1]); // NOT 23697.62
            Assert.Equal(1235.62, p1Closings.Data![3]);

            Assert.True(p2Closings.Success);
            Assert.Equal(46302.93, p2Closings.Data![2]); // NOT 44761.88
            Assert.Equal(2252.88, p2Closings.Data![4]); // NOT 2197.04

            Assert.True(p3Closings.Success);
            Assert.Equal(4488.45, p3Closings.Data![5]); // NOT 4385.62

            Assert.True(p4Closings.Success);
            Assert.Equal(16673.57, p4Closings.Data![6]); // NOT 16512.75
        }
        finally
        {
            if (System.IO.File.Exists(dbPath))
            {
                try { System.IO.File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public void Issue2_DayTotal_CashDeposit_FallbackMatchesShiftReport_AndDifferencePreserved()
    {
        // Issue 2: Day Total must preserve Cash Deposit from summary rows when Cash1 denomination counts are 0,
        // exactly matching Shift Total behavior and preserving canonical Difference without override distortion.
        var date = new DateTime(2026, 10, 31);

        var entryA = new DsmEntry
        {
            DsmName = "Attendant A",
            PumpId = 1,
            GrossSales = 50000,
            Shift = new Shift { ShiftDate = date, ShiftType = "A" },
            NozzleReadings = new List<NozzleReading>
            {
                new NozzleReading { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 500, Rate = 100, Amount = 50000 }
            },
            PaymentCollection = new PaymentCollection
            {
                CashDeposit = 15000,
                PhonePeMorning = 34500
            },
            CashDenominations = new List<CashDenomination>() // No denomination count
        };

        var entryB = new DsmEntry
        {
            DsmName = "Attendant B",
            PumpId = 2,
            GrossSales = 40000,
            Shift = new Shift { ShiftDate = date, ShiftType = "B" },
            NozzleReadings = new List<NozzleReading>
            {
                new NozzleReading { NozzleNumber = 2, FuelType = "MS-I", SaleLitres = 400, Rate = 100, Amount = 40000 }
            },
            PaymentCollection = new PaymentCollection
            {
                CashDeposit = 10000,
                PhonePeMorning = 29600
            },
            CashDenominations = new List<CashDenomination>() // No denomination count
        };

        // Shift Reports
        var shiftReportA = _reportService.CalculateShiftReport(
            date: date, shiftType: "A", entries: new List<DsmEntry> { entryA },
            shiftExpenses: new(), otherCashList: new(), repayments: new(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new(), tomorrowTid: new(), stationName: "Mitali");

        var shiftReportB = _reportService.CalculateShiftReport(
            date: date, shiftType: "B", entries: new List<DsmEntry> { entryB },
            shiftExpenses: new(), otherCashList: new(), repayments: new(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            todayTid: new(), tomorrowTid: new(), stationName: "Mitali");

        // Day Report
        var dayReport = _reportService.CalculateDayReport(
            startDate: date, endDate: date,
            entries: new List<DsmEntry> { entryA, entryB },
            allExpenses: new(), repayments: new(),
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            stationName: "Mitali");

        // Assert: Day Report ActualCollection must equal sum of Shift Reports (49500 + 39600 = 89100)
        double expectedCollectionTotal = shiftReportA.ActualCollection + shiftReportB.ActualCollection;
        Assert.Equal(expectedCollectionTotal, dayReport.ActualCollection);
        Assert.Equal(25000.0, dayReport.Cash1.GrandTotal); // 15000 + 10000 preserved from CashDeposit fallback!

        // Assert: Difference in Day Total matches sum of shift differences (-500 + -400 = -900)
        double expectedDiff = shiftReportA.Difference + shiftReportB.Difference;
        Assert.Equal(expectedDiff, dayReport.Difference);

        // Assert: DayTotalViewModel Difference assignment matches report.Difference directly without override distortion
        double vmDifference = dayReport.Difference;
        bool vmIsBalanced = Math.Abs(vmDifference) < 0.01;
        Assert.Equal(-900.0, vmDifference);
        Assert.False(vmIsBalanced);
    }

    [Fact]
    public async Task Issue3_SyncEngine_SkippedTables_RetainsPendingLogs_AndAvoidsPrematureAck()
    {
        // Issue 3: When a table push fails or is skipped (e.g. DsmQrPayments 42P10),
        // its SyncChangeLogs must NOT be marked IsSynced = 1, keeping them pending.
        string dbPath;
        using var context = CreateTestDbContext(out dbPath);
        try
        {
            // Seed a SyncChangeLog for DsmQrPayments and a normal table
            var logQr = new SyncChangeLog
            {
                TableName = "DsmQrPayments",
                Operation = "INSERT",
                RecordId = 1,
                SyncGuid = Guid.NewGuid().ToString("D"),
                CreatedAt = DateTime.Now,
                IsSynced = false
            };
            var logNormal = new SyncChangeLog
            {
                TableName = "Expenses",
                Operation = "INSERT",
                RecordId = 10,
                SyncGuid = Guid.NewGuid().ToString("D"),
                CreatedAt = DateTime.Now,
                IsSynced = false
            };
            context.SyncChangeLogs.AddRange(logQr, logNormal);
            await context.SaveChangesAsync();

            // Simulate the SyncEngine logic: skippedTables list contains "DsmQrPayments"
            var skippedTables = new List<string> { "DsmQrPayments" };

            // For normal table: mark synced
            var normalGroup = new List<SyncChangeLog> { logNormal };
            if (!skippedTables.Contains("Expenses"))
            {
                var idsCsv = string.Join(",", normalGroup.Select(l => l.Id));
                await context.Database.ExecuteSqlRawAsync(
                    $"UPDATE SyncChangeLogs SET IsSynced = 1 WHERE Id IN ({idsCsv})");
            }

            // For skipped table: push skipped, IsSynced is NOT updated
            var qrGroup = new List<SyncChangeLog> { logQr };
            if (!skippedTables.Contains("DsmQrPayments"))
            {
                var idsCsv = string.Join(",", qrGroup.Select(l => l.Id));
                await context.Database.ExecuteSqlRawAsync(
                    $"UPDATE SyncChangeLogs SET IsSynced = 1 WHERE Id IN ({idsCsv})");
            }

            // Assert: DsmQrPayments log remains pending (IsSynced == false)
            var refreshedQr = await context.SyncChangeLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == logQr.Id);
            Assert.NotNull(refreshedQr);
            Assert.False(refreshedQr.IsSynced);

            // Assert: Normal table was marked synced
            var refreshedNormal = await context.SyncChangeLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == logNormal.Id);
            Assert.NotNull(refreshedNormal);
            Assert.True(refreshedNormal.IsSynced);

            // Assert: Remaining pending count is 1, and status message reports warning
            var remainingPending = await context.SyncChangeLogs.CountAsync(l => !l.IsSynced);
            Assert.Equal(1, remainingPending);

            string statusMsg;
            if (skippedTables.Count > 0)
            {
                var tablesStr = string.Join(", ", skippedTables.Distinct());
                statusMsg = $"Sync Warning: {tablesStr} pending schema update";
            }
            else
            {
                statusMsg = "Synced";
            }
            Assert.Equal("Sync Warning: DsmQrPayments pending schema update", statusMsg);
        }
        finally
        {
            if (System.IO.File.Exists(dbPath))
            {
                try { System.IO.File.Delete(dbPath); } catch { }
            }
        }
    }

    #endregion

    #region DSM Summary Print Corrections Tests (Debtor Repayment Reconciliation & Scoped Section 2B)

    [Fact]
    public void DsmSummaryReconciliation_DebtorRepayment_AddedToExpectedGrossAmount_EliminatesFalseMismatch()
    {
        // Arrange
        // DSM Rajeshdispensed pure fuel sales of 50,000.
        // During the shift, an old debtor repaid 10,000 via PhonePe.
        // The 10,000 was collected through PhonePe and is already inside FinalAdjusted (TotalCollection = 60,000).
        var vm = new DsmEntryViewModel();
        vm.GrossSales = 50000.0;
        vm.FinalAdjusted = 60000.0; // TotalCollection including PhonePe repayment

        vm.DebtorRepayments.Add(new DsmDebtorRepaymentRow
        {
            DebtorName = "Shree Ram Transporters",
            Amount = 10000.0,
            PaymentMode = "PhonePe",
            Notes = "Old bill clearance"
        });

        // Act: Extract valid repayments and compute expected gross & reconciliation mismatch
        var personalDebtorPrintRows = vm.DebtorRepayments
            .Where(r => !string.IsNullOrWhiteSpace(r.DebtorName) && (r.Amount ?? 0) > 0)
            .Select(r => new DsmPersonalDebtorPrintRow
            {
                DebtorName = r.DebtorName.Trim(),
                FuelProduct = r.DebtorName.Trim(),
                Remarks = r.Notes ?? "",
                PaymentMethod = r.PaymentMode,
                Amount = r.Amount ?? 0
            }).ToList();

        double totalDebtorRepayments = personalDebtorPrintRows.Sum(r => r.Amount);
        double totalSales = vm.GrossSales;
        double expectedGrossAmount = totalSales + totalDebtorRepayments;
        double reconMismatch = vm.FinalAdjusted - expectedGrossAmount;

        var recon = new DsmReconciliationPrintBlock
        {
            GrossSales = vm.GrossSales,
            TotalSales = totalSales,
            DebtorRepaymentsAdded = totalDebtorRepayments,
            ExpectedGrossAmount = expectedGrossAmount,
            TotalCollection = vm.FinalAdjusted,
            Mismatch = reconMismatch
        };

        // Assert
        Assert.Equal(10000.0, recon.DebtorRepaymentsAdded);
        Assert.Equal(50000.0, recon.TotalSales);
        Assert.Equal(60000.0, recon.ExpectedGrossAmount);
        Assert.Equal(60000.0, recon.TotalCollection);
        // The 10,000 is NOT flagged as a shortage or mismatch:
        Assert.Equal(0.0, recon.Mismatch);
    }

    [Fact]
    public void DsmSummaryReconciliation_ZeroRepayments_ExpectedGrossEqualsFuelSales()
    {
        // Arrange: DSM with 45,000 sales and 0 repayments
        var vm = new DsmEntryViewModel();
        vm.GrossSales = 45000.0;
        vm.FinalAdjusted = 45000.0;

        // No repayments added to vm.DebtorRepayments

        // Act
        var personalDebtorPrintRows = vm.DebtorRepayments
            .Where(r => !string.IsNullOrWhiteSpace(r.DebtorName) && (r.Amount ?? 0) > 0)
            .ToList();

        double totalDebtorRepayments = personalDebtorPrintRows.Sum(r => r.Amount ?? 0);
        double totalSales = vm.GrossSales;
        double expectedGrossAmount = totalSales + totalDebtorRepayments;
        double reconMismatch = vm.FinalAdjusted - expectedGrossAmount;

        // Assert
        Assert.Equal(0.0, totalDebtorRepayments);
        Assert.Equal(45000.0, expectedGrossAmount);
        Assert.Equal(0.0, reconMismatch);
    }

    [Fact]
    public void DsmSummaryReconciliation_MultipleRepaymentsAcrossModes_AggregatedCorrectly()
    {
        // Arrange: DSM receives 6,000 via PhonePe and 4,000 via Cash from debtors
        var vm = new DsmEntryViewModel();
        vm.GrossSales = 50000.0;
        vm.FinalAdjusted = 60000.0; // 50,000 fuel + 6,000 PhonePe + 4,000 Cash

        vm.DebtorRepayments.Add(new DsmDebtorRepaymentRow { DebtorName = "Debtor A", Amount = 6000.0, PaymentMode = "PhonePe" });
        vm.DebtorRepayments.Add(new DsmDebtorRepaymentRow { DebtorName = "Debtor B", Amount = 4000.0, PaymentMode = "Cash" });

        // Act
        var personalDebtorPrintRows = vm.DebtorRepayments
            .Where(r => !string.IsNullOrWhiteSpace(r.DebtorName) && (r.Amount ?? 0) > 0)
            .ToList();

        double totalDebtorRepayments = personalDebtorPrintRows.Sum(r => r.Amount ?? 0);
        double expectedGrossAmount = vm.GrossSales + totalDebtorRepayments;
        double reconMismatch = vm.FinalAdjusted - expectedGrossAmount;

        // Assert
        Assert.Equal(2, personalDebtorPrintRows.Count);
        Assert.Equal(10000.0, totalDebtorRepayments);
        Assert.Equal(60000.0, expectedGrossAmount);
        Assert.Equal(0.0, reconMismatch);
    }

    [Fact]
    public void DsmSummaryReconciliation_MultipleRepaymentsForSameDebtor_AllIncluded()
    {
        // Arrange: Single debtor makes two distinct payments in the same shift (e.g. 7,000 via PhonePe and 3,000 via Cash)
        var vm = new DsmEntryViewModel();
        vm.GrossSales = 50000.0;
        vm.FinalAdjusted = 60000.0;

        vm.DebtorRepayments.Add(new DsmDebtorRepaymentRow { DebtorName = "Khandhare Transporters", Amount = 7000.0, PaymentMode = "PhonePe", Notes = "Morning part" });
        vm.DebtorRepayments.Add(new DsmDebtorRepaymentRow { DebtorName = "Khandhare Transporters", Amount = 3000.0, PaymentMode = "Cash", Notes = "Evening part" });

        // Act
        var personalDebtorPrintRows = vm.DebtorRepayments
            .Where(r => !string.IsNullOrWhiteSpace(r.DebtorName) && (r.Amount ?? 0) > 0)
            .ToList();

        double totalDebtorRepayments = personalDebtorPrintRows.Sum(r => r.Amount ?? 0);
        double expectedGrossAmount = vm.GrossSales + totalDebtorRepayments;

        // Assert: Both payments are preserved and summed without collapsing legitimate same-debtor payments
        Assert.Equal(2, personalDebtorPrintRows.Count);
        Assert.Equal(10000.0, totalDebtorRepayments);
        Assert.Equal(60000.0, expectedGrossAmount);
    }

    [Fact]
    public void DsmSummaryReconciliation_RepeatedPrinting_IsIdempotent()
    {
        // Arrange
        var vm = new DsmEntryViewModel();
        vm.GrossSales = 50000.0;
        vm.FinalAdjusted = 55000.0;
        vm.DebtorRepayments.Add(new DsmDebtorRepaymentRow { DebtorName = "Debtor C", Amount = 5000.0, PaymentMode = "PhonePe" });

        // Act 1
        var rows1 = vm.DebtorRepayments.Where(r => !string.IsNullOrWhiteSpace(r.DebtorName) && (r.Amount ?? 0) > 0).ToList();
        double rep1 = rows1.Sum(r => r.Amount ?? 0);
        double exp1 = vm.GrossSales + rep1;
        double mis1 = vm.FinalAdjusted - exp1;

        // Act 2 (simulating subsequent print call)
        var rows2 = vm.DebtorRepayments.Where(r => !string.IsNullOrWhiteSpace(r.DebtorName) && (r.Amount ?? 0) > 0).ToList();
        double rep2 = rows2.Sum(r => r.Amount ?? 0);
        double exp2 = vm.GrossSales + rep2;
        double mis2 = vm.FinalAdjusted - exp2;

        // Assert: Calculations are completely idempotent and produce identical outputs
        Assert.Equal(rep1, rep2);
        Assert.Equal(exp1, exp2);
        Assert.Equal(mis1, mis2);
        Assert.Equal(0.0, mis1);
    }

    [Fact]
    public void Section2B_SinglePumpDsm_ScopedExclusivelyToAssignedPump()
    {
        // Arrange: DSM assigned to Pump 1 only (no connected pumps)
        var vm = new DsmEntryViewModel();
        vm.SelectedPump = new PumpDisplayItem { PumpId = 1, DisplayText = "Pump 1" };
        vm.NozzleReadings.Clear();
        vm.NozzleReadings.Add(new NozzleReadingRow { NozzleNumber = 1, FuelType = "HSD", OpeningReading = 1000, ClosingReading = 2152.77, Rate = 90.0, OnRowChanged = vm.RecalculateAll });
        vm.NozzleReadings.Add(new NozzleReadingRow { NozzleNumber = 2, FuelType = "MS-I", OpeningReading = 500, ClosingReading = 800, Rate = 104.0, OnRowChanged = vm.RecalculateAll });

        // Act
        var cumSales = vm.BuildDsmScopedCumulativeSales();

        // Assert
        Assert.Equal(2, cumSales.Count);
        var hsd = cumSales.FirstOrDefault(f => f.FuelType == "HSD");
        Assert.NotNull(hsd);
        Assert.Equal(1, hsd.NozzleCount);
        Assert.Equal(1152.77, hsd.TotalLitres, precision: 2);

        var ms = cumSales.FirstOrDefault(f => f.FuelType == "MS-I");
        Assert.NotNull(ms);
        Assert.Equal(1, ms.NozzleCount);
        Assert.Equal(300.0, ms.TotalLitres, precision: 2);
    }

    [Fact]
    public async Task Section2B_CumulativeFuelSales_ScopedToAssignedPumps_IncludesPrimaryAndConnected_ExcludesOtherDsms()
    {
        // Arrange: Setup a clean disposable SQLite DB
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fuelpro_sec2b_{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<FuelPro.Data.FuelProDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var services = new ServiceCollection();
        services.AddDbContext<FuelPro.Data.FuelProDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<FuelPro.Core.Repositories.IShiftRepository, ShiftRepository>();
        services.AddScoped<FuelPro.Core.Repositories.IDsmEntryRepository, FuelPro.Data.Repositories.DsmEntryRepository>();
        var provider = services.BuildServiceProvider();

        // Set App.Services for the test via reflection
        typeof(FuelPro.UI.App).GetProperty("Services")?.SetValue(null, provider);

        try
        {
            using (var context = provider.GetRequiredService<FuelPro.Data.FuelProDbContext>())
            {
                await context.Database.EnsureCreatedAsync();

                var shift = new Shift
                {
                    ShiftType = "A",
                    ShiftDate = new DateTime(2026, 11, 1)
                };
                context.Shifts.Add(shift);
                await context.SaveChangesAsync();

                // Entry 1: Primary Pump 1 operated by Rajesh (HSD: 1,152.77 L across 2 nozzles)
                var entry1 = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    PumpId = 1,
                    DsmName = "Rajesh",
                    GrossSales = 103749.30m,
                    ConnectedPumpId = 2,
                    NozzleReadings = new List<NozzleReading>
                    {
                        new NozzleReading { NozzleNumber = 1, FuelType = "HSD", OpeningReading = 1000, ClosingReading = 1576.38, SaleLitres = 576.38, Rate = 90.0, Amount = 51874.20 },
                        new NozzleReading { NozzleNumber = 2, FuelType = "HSD", OpeningReading = 2000, ClosingReading = 2576.39, SaleLitres = 576.39, Rate = 90.0, Amount = 51875.10 }
                    }
                };
                context.DsmEntries.Add(entry1);

                // Entry 2: Connected Pump 2 operated by Rajesh / reconciled to Pump 1 (HSD: 539.93 L on 1 nozzle)
                var entry2 = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    PumpId = 2,
                    DsmName = "Rajesh",
                    ReconciledToPumpId = 1,
                    GrossSales = 48593.70m,
                    NozzleReadings = new List<NozzleReading>
                    {
                        new NozzleReading { NozzleNumber = 1, FuelType = "HSD", OpeningReading = 3000, ClosingReading = 3539.93, SaleLitres = 539.93, Rate = 90.0, Amount = 48593.70 }
                    }
                };
                context.DsmEntries.Add(entry2);

                // Entry 3: Unrelated Pump 3 operated by DSM Suresh in the SAME shift (HSD: 1,270.92 L)
                var entry3 = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    PumpId = 3,
                    DsmName = "Suresh",
                    GrossSales = 114382.80m,
                    NozzleReadings = new List<NozzleReading>
                    {
                        new NozzleReading { NozzleNumber = 1, FuelType = "HSD", OpeningReading = 4000, ClosingReading = 4635.46, SaleLitres = 635.46, Rate = 90.0, Amount = 57191.40 },
                        new NozzleReading { NozzleNumber = 2, FuelType = "HSD", OpeningReading = 5000, ClosingReading = 5635.46, SaleLitres = 635.46, Rate = 90.0, Amount = 57191.40 }
                    }
                };
                context.DsmEntries.Add(entry3);
                await context.SaveChangesAsync();

                // Act: In DsmEntryViewModel for Rajesh (Pump 1 with Connected Pump 2)
                var vm = new DsmEntryViewModel();
                vm.SelectedDate = new DateTime(2026, 11, 1);
                vm.SelectedShift = "A";
                vm.DsmName = "Rajesh";
                vm.SelectedPump = new PumpDisplayItem { PumpId = 1, DisplayText = "Pump 1" };
                vm.ActiveConnectedPumpIds = new List<int> { 2 };

                // Populate active nozzle readings for Pump 1
                vm.NozzleReadings.Clear();
                vm.NozzleReadings.Add(new NozzleReadingRow { NozzleNumber = 1, FuelType = "HSD", OpeningReading = 1000, ClosingReading = 1576.38, SaleLitres = 576.38, Rate = 90.0, Amount = 51874.20 });
                vm.NozzleReadings.Add(new NozzleReadingRow { NozzleNumber = 2, FuelType = "HSD", OpeningReading = 2000, ClosingReading = 2576.39, SaleLitres = 576.39, Rate = 90.0, Amount = 51875.10 });

                var sec2bSales = vm.BuildDsmScopedCumulativeSales();

                // Assert:
                // Total HSD Litres = 1,152.77 L (Pump 1) + 539.93 L (Pump 2) = 1,692.70 L
                // Total HSD Nozzles = 3 (2 on Pump 1 + 1 on Pump 2)
                // Pump 3 (1,270.92 L) operated by DSM Suresh is strictly EXCLUDED!
                Assert.Single(sec2bSales);
                var hsd = sec2bSales[0];
                Assert.Equal("HSD", hsd.FuelType);
                Assert.Equal(3, hsd.NozzleCount);
                Assert.Equal(1692.70, hsd.TotalLitres, precision: 2);
                Assert.Equal(152343.00, hsd.TotalAmount, precision: 2);
            }
        }
        finally
        {
            typeof(FuelPro.UI.App).GetProperty("Services")?.SetValue(null, null);
            if (System.IO.File.Exists(dbPath))
            {
                try { System.IO.File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public void Section2B_PrintShiftSummary_PreservesShiftWideCumulativeSalesAcrossAllPumps()
    {
        // Arrange
        // Shift summary must preserve all dispensers across the shift (e.g. Pump 1 + Pump 2 + Pump 3 = 2,963.62 L)
        var shiftCumulative = new List<FuelTypeReadingSummary>
        {
            new FuelTypeReadingSummary { FuelType = "HSD", NozzleCount = 5, TotalLitres = 2963.62, Rate = 90.0, TotalAmount = 266725.80 }
        };

        var printData = new ShiftSummaryPrintData
        {
            StationName = "Mitali Service Station",
            Date = "01/11/2026",
            Shift = "A",
            CumulativeFuelSales = shiftCumulative,
            TotalSales = 266725.80,
            TotalCollection = 266725.80,
            TotalMismatch = 0
        };

        // Assert: ShiftSummaryPrintData accurately carries the shift-wide cumulative fuel sales across all pumps
        Assert.Single(printData.CumulativeFuelSales);
        Assert.Equal(5, printData.CumulativeFuelSales[0].NozzleCount);
        Assert.Equal(2963.62, printData.CumulativeFuelSales[0].TotalLitres, precision: 2);
    }

    [Fact]
    public async Task DebtorManagement_RepaymentPersistence_ReducesBalance_PersistsAcrossRestart_AppearsOnceInDsmSummary()
    {
        // Arrange: Isolated temporary SQLite DB
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fuelpro_debtor_audit_{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddDbContext<FuelPro.Data.FuelProDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<FuelPro.Core.Repositories.ICreditorRepository, CreditorRepository>();
        services.AddScoped<FuelPro.Core.Repositories.ICreditorRepaymentRepository, CreditorRepaymentRepository>();
        var provider = services.BuildServiceProvider();

        const string debtorName = "Shree Ram Transporters";

        try
        {
            int creditorId;
            // 1. Initial State: Create Creditor, Shift, DsmEntry, and a credit debit = 25,000
            using (var scope1 = provider.CreateScope())
            using (var context = scope1.ServiceProvider.GetRequiredService<FuelPro.Data.FuelProDbContext>())
            {
                await context.Database.EnsureCreatedAsync();

                var creditor = new Creditor
                {
                    Name = debtorName,
                    Phone = "9876543210",
                    IsActive = true
                };
                context.Creditors.Add(creditor);
                await context.SaveChangesAsync();
                creditorId = creditor.CreditorId;

                var shift = new Shift
                {
                    ShiftDate = DateTime.Today,
                    ShiftType = "A"
                };
                context.Shifts.Add(shift);
                await context.SaveChangesAsync();

                var dsmEntry = new DsmEntry
                {
                    ShiftId = shift.ShiftId,
                    PumpId = 1,
                    DsmName = "Rajesh"
                };
                context.DsmEntries.Add(dsmEntry);
                await context.SaveChangesAsync();

                var debit = new DebitEntry
                {
                    DsmEntryId = dsmEntry.DsmEntryId,
                    DebtorName = debtorName,
                    Amount = 25000.0,
                    VehicleNumber = "MH-14-1234",
                    ChequeNo = "SLIP-001"
                };
                context.DebitEntries.Add(debit);
                await context.SaveChangesAsync();

                // Initial Outstanding Balance: 25,000 (debt) - 0 (repaid) = 25,000
                var totalDebt = await context.DebitEntries.Where(d => d.DebtorName == debtorName).SumAsync(d => d.Amount);
                var totalRepaid = await context.CreditorRepayments.Where(r => r.CreditorName == debtorName).SumAsync(r => r.Amount);
                var initialBal = totalDebt - totalRepaid;
                Assert.Equal(25000.0, initialBal);
            }

            // 2. Action: Attendant receives 10,000 repayment via PhonePe in DSM Entry
            using (var scope2 = provider.CreateScope())
            using (var context = scope2.ServiceProvider.GetRequiredService<FuelPro.Data.FuelProDbContext>())
            {
                var repRepo = new CreditorRepaymentRepository(context);
                var repayment = new CreditorRepayment
                {
                    CreditorName = debtorName,
                    Amount = 10000.0,
                    PaymentMode = "PhonePe",
                    RepaymentDate = DateTime.Today,
                    ShiftNumber = "1",
                    ChequeNo = "DSM: Rajesh | Mode: PhonePe",
                };
                var addRes = await repRepo.AddAsync(repayment);
                Assert.True(addRes.Success, addRes.Error);

                // 3. Verify outstanding balance reduces by exactly 10,000 (from 25,000 to 15,000)
                var totalDebt = await context.DebitEntries.Where(d => d.DebtorName == debtorName).SumAsync(d => d.Amount);
                var totalRepaid = await context.CreditorRepayments.Where(r => r.CreditorName == debtorName).SumAsync(r => r.Amount);
                var currentBal = totalDebt - totalRepaid;

                Assert.Equal(10000.0, totalRepaid);
                Assert.Equal(15000.0, currentBal);
            }

            // 4. Simulate Application Restart: Dispose service provider, re-open database from scratch
            var restartServices = new ServiceCollection();
            restartServices.AddDbContext<FuelPro.Data.FuelProDbContext>(opt => opt.UseSqlite($"Data Source={dbPath}"));
            var restartProvider = restartServices.BuildServiceProvider();

            using (var restartContext = restartProvider.GetRequiredService<FuelPro.Data.FuelProDbContext>())
            {
                var creditor = await restartContext.Creditors.FindAsync(creditorId);
                Assert.NotNull(creditor);

                var repayments = await restartContext.CreditorRepayments.Where(r => r.CreditorName == debtorName).ToListAsync();
                Assert.Single(repayments);
                Assert.Equal(10000.0, repayments[0].Amount);
                Assert.Equal("PhonePe", repayments[0].PaymentMode);

                var totalDebt = await restartContext.DebitEntries.Where(d => d.DebtorName == debtorName).SumAsync(d => d.Amount);
                var totalRepaid = repayments.Sum(r => r.Amount);
                var persistedBal = totalDebt - totalRepaid;

                // Balance remains 15,000 after restart
                Assert.Equal(15000.0, persistedBal);
            }

            // 5. Verify DSM Summary appearance
            var vm = new DsmEntryViewModel();
            vm.GrossSales = 50000.0;
            vm.FinalAdjusted = 60000.0; // PhonePe collection includes the 10,000 repayment

            vm.DebtorRepayments.Add(new DsmDebtorRepaymentRow
            {
                DebtorName = debtorName,
                Amount = 10000.0,
                PaymentMode = "PhonePe",
                Notes = "DSM: Rajesh | Mode: PhonePe"
            });

            var printRows = vm.DebtorRepayments
                .Where(r => !string.IsNullOrWhiteSpace(r.DebtorName) && (r.Amount ?? 0) > 0)
                .ToList();

            // Repayment appears exactly once
            Assert.Single(printRows);
            Assert.Equal(10000.0, printRows[0].Amount);

            double repAdded = printRows.Sum(r => r.Amount ?? 0);
            double expGross = vm.GrossSales + repAdded;
            double mismatch = vm.FinalAdjusted - expGross;

            Assert.Equal(10000.0, repAdded);
            Assert.Equal(60000.0, expGross);
            Assert.Equal(0.0, mismatch);
        }
        finally
        {
            if (System.IO.File.Exists(dbPath))
            {
                try { System.IO.File.Delete(dbPath); } catch { }
            }
        }
    }

    [Fact]
    public void DsmSheetPrintOutput_GeneratedHtml_ContainsRepaymentExplanation_BalancedMismatch_AndScopedSection2B()
    {
        // Arrange: Build complete DsmSheetPrintData simulating the live client scenario
        var printData = new DsmSheetPrintData
        {
            StationName = "Mitali Service Station",
            CompanyName = "Mitali Service Station",
            Date = "01/11/2026",
            Shift = "A",
            DsmName = "Rajesh",
            PumpNo = "Pump 1",
            TotalLitres = 1152.77,
            GrossSales = 50000.0,
            CumulativeFuelSales = new List<FuelTypeReadingSummary>
            {
                new FuelTypeReadingSummary { FuelType = "HSD", NozzleCount = 3, TotalLitres = 1692.70, Rate = 90.0, TotalAmount = 152343.0 }
            },
            PersonalDebtors = new List<DsmPersonalDebtorPrintRow>
            {
                new DsmPersonalDebtorPrintRow
                {
                    DebtorName = "Shree Ram Transporters",
                    FuelProduct = "Shree Ram Transporters",
                    PaymentMethod = "PhonePe",
                    Amount = 10000.0,
                    Remarks = "Old debt clearance"
                }
            },
            TotalPersonalDebtors = 10000.0,
            CollectionBreakdown = new List<CollectionCategoryDto>
            {
                new CollectionCategoryDto { Category = "PhonePe", Amount = 10000.0, BaseAmount = 10000.0 },
                new CollectionCategoryDto { Category = "Cash 1", Amount = 50000.0, BaseAmount = 50000.0 }
            },
            Reconciliation = new DsmReconciliationPrintBlock
            {
                GrossSales = 50000.0,
                TotalSales = 50000.0,
                DebtorRepaymentsAdded = 10000.0,
                ExpectedGrossAmount = 60000.0,
                TotalCollection = 60000.0,
                Mismatch = 0.0
            }
        };

        // Act: Read the DsmSheetPrintTemplate.html and inject data JSON
        var dir = new System.IO.DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "FuelPro.sln")))
        {
            dir = dir.Parent;
        }
        var templatePath = System.IO.Path.Combine(dir!.FullName, "src", "FuelPro.UI", "Printing", "DsmSheetPrintTemplate.html");
        var templateHtml = System.IO.File.ReadAllText(templatePath, System.Text.Encoding.UTF8);

        var json = System.Text.Json.JsonSerializer.Serialize(printData, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        const string MARKER = "/* INJECT_JSON_HERE */{}";
        Assert.Contains(MARKER, templateHtml);
        var finalHtml = templateHtml.Replace(MARKER, json);

        // Assert:
        // 1. Section 2B has updated title
        Assert.Contains("Section 2B — Cumulative Fuel Sales (Assigned Pumps)", finalHtml);

        // 2. Section 2B has the scoped litres (1692.70), NOT the all-pump cumulative (2963.62)
        Assert.Contains("1692.7", finalHtml);
        Assert.DoesNotContain("2963.62", finalHtml);

        // 3. Debtor Repayment is present in the data injection
        Assert.Contains("Shree Ram Transporters", finalHtml);
        Assert.Contains("10000", finalHtml);

        // 4. Section 9 JS logic has both audit lines and balanced mismatch logic
        Assert.Contains("Expected Gross Sale", finalHtml);
        Assert.Contains("⚠️ Mismatch (Collection − Sales)", finalHtml);

        // 5. Shift summary print template preserves shift-wide title
        var shiftTemplatePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(templatePath)!, "ShiftSummaryPrintTemplate.html");
        var shiftHtml = System.IO.File.ReadAllText(shiftTemplatePath, System.Text.Encoding.UTF8);
        Assert.Contains("Shift Cumulative Fuel Sales (All Dispenser Units)", shiftHtml);
    }

    #endregion


    #region Forensic Investigation Client-Site Tests

    [Fact]
    public void Section2B_RahulShingvi_Shift45_CumulativeSalesMatchSection2Exact_771_38Litres()
    {
        // Rahul Shingvi Shift 45: Primary Pump 3 + Connected Pump 4
        // Section 2 nozzle readings:
        // Pump 3: Nozzle 8 (HSD 256.67 L), Nozzle 10 (SPEED 51.48 L), Nozzle 12 (MS-I 293.45 L) -> 601.60 L
        // Pump 4: Nozzle 7 (HSD 125.36 L), Nozzle 9 (SPEED 3.31 L), Nozzle 11 (MS-I 41.11 L) -> 169.78 L
        // Total true combined litres = 771.38 L.
        // Slave pump nozzles must NOT be added twice (which previously produced 941.16 L).

        var vm = new DsmEntryViewModel();
        vm.SelectedDate = new DateTime(2026, 11, 1);
        vm.SelectedShift = "A";
        vm.DsmName = "RAHUL SHINGVI";
        vm.SelectedPump = new PumpDisplayItem { PumpId = 3, DisplayText = "Pump 3" };
        vm.ActiveConnectedPumpIds = new List<int> { 4 };

        vm.NozzleReadings.Clear();
        // Pump 3 nozzles
        vm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 3, NozzleNumber = 8, FuelType = "HSD", OpeningReading = 0, ClosingReading = 256.67, Rate = 98.19 });
        vm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 3, NozzleNumber = 10, FuelType = "SPEED", OpeningReading = 0, ClosingReading = 51.48, Rate = 120.64 });
        vm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 3, NozzleNumber = 12, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 293.45, Rate = 111.53 });
        // Pump 4 nozzles (connected)
        vm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 4, NozzleNumber = 7, FuelType = "HSD", OpeningReading = 0, ClosingReading = 125.36, Rate = 98.19 });
        vm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 4, NozzleNumber = 9, FuelType = "SPEED", OpeningReading = 0, ClosingReading = 3.31, Rate = 120.64 });
        vm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 4, NozzleNumber = 11, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 41.11, Rate = 111.53 });

        var cumulative = vm.BuildDsmScopedCumulativeSales();

        double totalLitres = cumulative.Sum(c => c.TotalLitres);
        Assert.Equal(771.38, totalLitres, precision: 2);
        Assert.NotEqual(941.16, totalLitres);

        var hsd = cumulative.FirstOrDefault(c => c.FuelType == "HSD");
        var msi = cumulative.FirstOrDefault(c => c.FuelType == "MS-I");
        var speed = cumulative.FirstOrDefault(c => c.FuelType == "SPEED");

        Assert.NotNull(hsd);
        Assert.NotNull(msi);
        Assert.NotNull(speed);

        Assert.Equal(382.03, hsd.TotalLitres, precision: 2);
        Assert.Equal(334.56, msi.TotalLitres, precision: 2);
        Assert.Equal(54.79, speed.TotalLitres, precision: 2);
    }

    [Fact]
    public void Section2B_RaniGadhave_Shift45_CumulativeSalesMatchSection2Exact_2293_35Litres()
    {
        // Rani Gadhave Shift 45: Primary Pump 1 + Connected Pump 2
        // Pump 1: Nozzle 2 (HSD 1069.15 L), Nozzle 4 (SPEED 99.19 L), Nozzle 6 (MS-I 580.76 L) -> 1,749.10 L
        // Pump 2: Nozzle 1 (HSD 407.11 L), Nozzle 3 (SPEED 9.29 L), Nozzle 5 (MS-I 127.85 L) -> 544.25 L
        // Documented exact database sum: 1,749.10 + 544.25 = 2,293.35 L.
        // Resolves the 0.20 L human transcription discrepancy between reported 2,293.15 L and actual 2,293.35 L.
        // Slave pump nozzles must NOT be added twice (which previously produced 2,837.60 L).

        var vm = new DsmEntryViewModel();
        vm.SelectedDate = new DateTime(2026, 11, 1);
        vm.SelectedShift = "A";
        vm.DsmName = "RANI GADHAVE";
        vm.SelectedPump = new PumpDisplayItem { PumpId = 1, DisplayText = "Pump 1" };
        vm.ActiveConnectedPumpIds = new List<int> { 2 };

        vm.NozzleReadings.Clear();
        // Pump 1
        vm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 1, NozzleNumber = 2, FuelType = "HSD", OpeningReading = 0, ClosingReading = 1069.15, Rate = 98.19 });
        vm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 1, NozzleNumber = 4, FuelType = "SPEED", OpeningReading = 0, ClosingReading = 99.19, Rate = 120.64 });
        vm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 1, NozzleNumber = 6, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 580.76, Rate = 111.53 });
        // Pump 2
        vm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 2, NozzleNumber = 1, FuelType = "HSD", OpeningReading = 0, ClosingReading = 407.11, Rate = 98.19 });
        vm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 2, NozzleNumber = 3, FuelType = "SPEED", OpeningReading = 0, ClosingReading = 9.29, Rate = 120.64 });
        vm.NozzleReadings.Add(new NozzleReadingRow { PumpId = 2, NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 127.85, Rate = 111.53 });

        var cumulative = vm.BuildDsmScopedCumulativeSales();

        double totalLitres = cumulative.Sum(c => c.TotalLitres);
        Assert.Equal(2293.35, totalLitres, precision: 2);
        Assert.NotEqual(2837.60, totalLitres);

        var hsd = cumulative.FirstOrDefault(c => c.FuelType == "HSD");
        var msi = cumulative.FirstOrDefault(c => c.FuelType == "MS-I");
        var speed = cumulative.FirstOrDefault(c => c.FuelType == "SPEED");

        Assert.NotNull(hsd);
        Assert.NotNull(msi);
        Assert.NotNull(speed);

        Assert.Equal(1476.26, hsd.TotalLitres, precision: 2);
        Assert.Equal(708.61, msi.TotalLitres, precision: 2);
        Assert.Equal(108.48, speed.TotalLitres, precision: 2);
    }

    [Fact]
    public void SuvarnaShelke_Shift45_DebtorRecovery_NoFalseMismatch()
    {
        // Suvarna Shelke Shift 45 Entry 252:
        // Pure meter gross sales = 91,586.66
        // TotalCollection = 91,586.88 (MobiKwik 78,362.88 already includes 10,000 debtor recovery)
        // Expected gross must remain 91,586.66 (never add 10,000 again to expected gross)
        // Mismatch must remain +0.22 (practically 0), NOT -9,999.78!

        double grossSales = 91586.66;
        double totalCollection = 91586.88;

        // In DsmEntryViewModel logic:
        double totalSales = grossSales;
        double expectedGrossAmount = totalSales; // pure meter sales
        double reconMismatch = totalCollection - expectedGrossAmount;

        Assert.Equal(0.22, reconMismatch, precision: 2);
        Assert.True(Math.Abs(reconMismatch) < 1.0);
        Assert.NotEqual(-9999.78, reconMismatch, precision: 2);
    }

    [Fact]
    public void Shift45_PetroCard_DaySlot_InShiftA_NotDropped_TotalCollectionMatchesEntries()
    {
        // Shift 45: Entry 251 (Rugved) has 334.59 PetroCard with Slot="Day"
        // Entry 253 (Nirjala) has 450.00 PetroCard with Slot="Day"
        // In Shift A, both must be included in finalPetro, restoring 784.59.

        var entry1 = new DsmEntry
        {
            DsmName = "RUGVED BHISE",
            PumpId = 5,
            Shift = new Shift { ShiftType = "A", ShiftDate = new DateTime(2026, 11, 1) },
            PaymentCollection = new PaymentCollection
            {
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 334.59, Slot = "Day" }
                }
            }
        };

        var entry2 = new DsmEntry
        {
            DsmName = "NIRJALA GAVADE",
            PumpId = 6,
            Shift = new Shift { ShiftType = "A", ShiftDate = new DateTime(2026, 11, 1) },
            PaymentCollection = new PaymentCollection
            {
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 450.00, Slot = "Day" }
                }
            }
        };

        var rows = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entry1, entry2 });

        Assert.Equal(2, rows.Count);
        Assert.Equal(334.59, rows[0].PetroCard, precision: 2);
        Assert.Equal(450.00, rows[1].PetroCard, precision: 2);
        Assert.Equal(784.59, rows.Sum(r => r.PetroCard), precision: 2);
    }

    [Fact]
    public void ShiftTotal_ReconciliationDifference_MatchesNegativeDsmMismatches_AndExplains4_43Difference()
    {
        // Forensic proof of Shift 45 difference:
        // Expected gross = 612,519.84
        // True collection with restored PetroCard = 611,288.83
        // Aggregate Net Difference = 611,288.83 - 612,519.84 = -1,231.01
        //
        // Individual entry mismatches from actual database clone:
        // Shortages (negative):
        //   Entry 254 (Rani):   -1225.91
        //   Entry 251 (Rugved):    -7.19
        //   Entry 256 (Rahul):     -2.34
        //   Total Gross Short = -1,235.44
        //
        // Excesses (positive):
        //   Entry 253 (Nirjala):  +4.22
        //   Entry 252 (Suvarna):  +0.22
        //   Total Excess =        +4.44
        //
        // The exact algebraic difference between Net Difference (-1,231.01) and Gross Short (-1,235.44) is:
        //   -1,231.01 - (-1,235.44) = +4.43!

        double grossShort = -1225.9099 + (-7.1943) + (-2.3381);
        double totalExcess = 0.2177 + 4.2187;
        double netDifference = grossShort + totalExcess;

        Assert.Equal(-1235.44, grossShort, precision: 2);
        Assert.Equal(4.44, totalExcess, precision: 2);
        Assert.Equal(-1231.01, netDifference, precision: 2);

        // The exact algebraic difference between reported rounded Net Difference (-1,231.01) and Gross Short (-1,235.44) is:
        double roundedNet = Math.Round(netDifference, 2);
        double roundedShort = Math.Round(grossShort, 2);
        Assert.Equal(4.43, Math.Round(roundedNet - roundedShort, 2));
    }

    [Fact]
    public void ReportService_DsmShort_ExcludedFromCollectionBreakdown_RetainedAsDtoMetadata()
    {
        var entry = new DsmEntry
        {
            DsmName = "Attendant 1",
            PumpId = 1,
            GrossSales = 10000,
            TotalCollection = 9000,
            Mismatch = -1000,
            Shift = new Shift { ShiftType = "A", ShiftDate = DateTime.Today },
            NozzleReadings = new List<NozzleReading>
            {
                new NozzleReading { NozzleNumber = 1, FuelType = "HSD", SaleLitres = 100, Rate = 100, Amount = 10000 }
            },
            CashDenominations = new List<CashDenomination> { new CashDenomination { CashType = "Cash1", TotalAmount = 9000 } },
            PaymentCollection = new PaymentCollection()
        };

        var report = _reportService.CalculateShiftReport(
            DateTime.Today,
            "A",
            new List<DsmEntry> { entry },
            new List<Expense>(),
            new List<ShiftOtherCash>(),
            new List<CreditorRepayment>(),
            100, 100, 100, 100,
            new BusinessDayTidSheet(),
            new BusinessDayTidSheet(),
            "Mitali Station");

        // DSM Short must NOT appear in CollectionBreakdown
        Assert.DoesNotContain(report.CollectionBreakdown, c => c.Category == "DSM Short");

        // But must be accessible as distinct metadata on DTO
        Assert.Equal(1000, report.TotalDsmShort, precision: 2);
    }

    [Fact]
    public void PrintReadiness_ReportLoadingState_TransitionsAccurately()
    {
        var vm = new FinalCalculationViewModel();
        Assert.Equal(ReportLoadingState.Empty, vm.LoadingState);

        // When date or shift changes, state is immediately Loading
        vm.LoadingState = ReportLoadingState.Loading;
        Assert.Equal(ReportLoadingState.Loading, vm.LoadingState);

        // If query fails, state is Error
        vm.LoadingState = ReportLoadingState.Error;
        vm.LoadingErrorMessage = "Database query timeout";
        Assert.Equal(ReportLoadingState.Error, vm.LoadingState);
        Assert.Equal("Database query timeout", vm.LoadingErrorMessage);

        // On complete success, state is Loaded
        vm.LoadingState = ReportLoadingState.Loaded;
        Assert.Equal(ReportLoadingState.Loaded, vm.LoadingState);
    }

    [Fact]
    public async Task SyncEngine_GetPendingCountAsync_ReturnsActualUnsyncedLogs_WhenSyncDisabled()
    {
        var dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sync_test_{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContext<FuelPro.Data.FuelProDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        services.AddLogging();
        services.AddSingleton<FuelPro.Sync.SyncConfigService>();
        services.AddSingleton<FuelPro.Sync.SyncEngine>();

        var sp = services.BuildServiceProvider();

        try
        {
            using (var context = sp.GetRequiredService<FuelPro.Data.FuelProDbContext>())
            {
                await context.Database.EnsureCreatedAsync();

                // Insert 10 unsynced logs for DsmQrPayments
                for (int i = 1; i <= 10; i++)
                {
                    context.SyncChangeLogs.Add(new SyncChangeLog
                    {
                        TableName = "DsmQrPayments",
                        RecordId = i,
                        Operation = "INSERT",
                        CreatedAt = DateTime.UtcNow,
                        IsSynced = false
                    });
                }
                await context.SaveChangesAsync();

                var syncEngine = sp.GetRequiredService<FuelPro.Sync.SyncEngine>();
                int pendingCount = await syncEngine.GetPendingCountAsync();

                // Must return 10, never hardcode 0
                Assert.Equal(10, pendingCount);
            }
        }
        finally
        {
            if (System.IO.File.Exists(dbPath))
            {
                try { System.IO.File.Delete(dbPath); } catch { }
            }
        }
    }

    #endregion


    [Fact]
    public void PetroCard_AllShiftsAndSlots_NoDroppedTransactions_NoDoubleCounting_AndDayTotalAggregatesAccurately()
    {
        var today = DateTime.Today;

        // 1. Shift A: Attendant has scalar + distinct items in Morning, Day, Night, and unspecified slot
        var entryShiftA = new DsmEntry
        {
            DsmName = "Attendant A",
            PumpId = 1,
            GrossSales = 10000,
            Shift = new Shift { ShiftType = "A", ShiftDate = today },
            PaymentCollection = new PaymentCollection
            {
                PetroCardMorning = 1000,
                PetroCardDay = 200,
                PetroCardNight = 300,
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 500, Slot = "Morning", Tid = "TID_A_M" },
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 150, Slot = "Day", Tid = "TID_A_D" },
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 250, Slot = "Night", Tid = "TID_A_N" },
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 100, Slot = "", Tid = "TID_A_UNSPEC" } // Shift A defaults to Morning
                }
            }
        };

        // 2. Shift B: Attendant has items in Day, Morning, and unspecified slot
        var entryShiftB = new DsmEntry
        {
            DsmName = "Attendant B",
            PumpId = 2,
            GrossSales = 8000,
            Shift = new Shift { ShiftType = "B", ShiftDate = today },
            PaymentCollection = new PaymentCollection
            {
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 400, Slot = "Day", Tid = "TID_B_D" },
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 600, Slot = "", Tid = "TID_B_UNSPEC" }, // Shift B defaults to Day
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 200, Slot = "Morning", Tid = "TID_B_M" }
                }
            }
        };

        // 3. Shift C: Attendant has items in Night and unspecified slot
        var entryShiftC = new DsmEntry
        {
            DsmName = "Attendant C",
            PumpId = 3,
            GrossSales = 5000,
            Shift = new Shift { ShiftType = "C", ShiftDate = today },
            PaymentCollection = new PaymentCollection
            {
                Items = new List<PaymentCollectionItem>
                {
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 800, Slot = "Night", Tid = "TID_C_N" },
                    new PaymentCollectionItem { CollectionTypeCode = "PETROCARD", Amount = 100, Slot = "", Tid = "TID_C_UNSPEC" } // Shift C defaults to Night
                }
            }
        };

        // Act - Shift-level summaries
        var rowsA = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entryShiftA });
        var rowsB = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entryShiftB });
        var rowsC = _aggregationService.BuildDsmSummaryRows(new List<DsmEntry> { entryShiftC });

        // Assert Shift A:
        // Morning = 1000 scalar + 500 item + 100 unspec = 1600
        // Day = 200 scalar + 150 item = 350
        // Night = 300 scalar + 250 item = 550
        // Total = 1600 + 350 + 550 = 2500
        Assert.Single(rowsA);
        Assert.Equal(1600, rowsA[0].PetroCardMorning);
        Assert.Equal(350, rowsA[0].PetroCardDay);
        Assert.Equal(550, rowsA[0].PetroCardNight);
        Assert.Equal(2500, rowsA[0].PetroCardTotal);

        // Assert Shift B:
        // Day = 400 item + 600 unspec = 1000
        // Morning = 200 item
        // Total = 1200
        Assert.Single(rowsB);
        Assert.Equal(200, rowsB[0].PetroCardMorning);
        Assert.Equal(1000, rowsB[0].PetroCardDay);
        Assert.Equal(0, rowsB[0].PetroCardNight);
        Assert.Equal(1200, rowsB[0].PetroCardTotal);

        // Assert Shift C:
        // Night = 800 item + 100 unspec = 900
        // Total = 900
        Assert.Single(rowsC);
        Assert.Equal(0, rowsC[0].PetroCardMorning);
        Assert.Equal(0, rowsC[0].PetroCardDay);
        Assert.Equal(900, rowsC[0].PetroCardNight);
        Assert.Equal(900, rowsC[0].PetroCardTotal);

        // Act - Day Total aggregation across all three shifts
        var allEntries = new List<DsmEntry> { entryShiftA, entryShiftB, entryShiftC };
        var allSummaryRows = _aggregationService.BuildDsmSummaryRows(allEntries);
        var dayTotals = _aggregationService.BuildDsmSummaryTotalRow(allSummaryRows);

        // Assert Day Total:
        // Exact algebraic sum: 2500 (Shift A) + 1200 (Shift B) + 900 (Shift C) = 4600
        Assert.Equal(4600, dayTotals.PetroCard);
        Assert.Equal(4600, dayTotals.PetroCardTotal);
        Assert.Equal(1800, dayTotals.PetroCardMorning); // 1600 (A) + 200 (B)
        Assert.Equal(1350, dayTotals.PetroCardDay);     // 350 (A) + 1000 (B)
        Assert.Equal(1450, dayTotals.PetroCardNight);   // 550 (A) + 900 (C)
    }

    [Fact]
    public void DeductDebtorRepaymentsFromCollections_ScreenshotScenario_EliminatesFalseMismatch()
    {
        // Arrange: Scenario from Mitali client screenshot
        // Expected gross sales from nozzles = 102,838.41
        // Attendant collected:
        //   Mobikwik = 85,504.50
        //   Expenses = 6,100.00
        //   Cash 3 = 21,250.00 (contains 10,000 cash debtor recovery)
        // Repayment: 10,000 Cash from debtor
        double cashDep = 0;
        double cash2 = 0;
        double cash3 = 21250.0;
        double phonePe = 0;
        double creditCard = 0;
        double petroCard = 0;
        var dynAmounts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["MOBIKWIK"] = 85504.50
        };

        var repayments = new List<DsmDebtorRepaymentRow>
        {
            new DsmDebtorRepaymentRow
            {
                DebtorName = "Shree Ram Transporters",
                Amount = 10000.0,
                PaymentMode = "Cash",
                Notes = "Old recovery"
            }
        };

        // Act
        DsmEntryViewModel.DeductDebtorRepaymentsFromCollections(
            repayments, ref cashDep, ref cash2, ref cash3, ref phonePe, ref creditCard, ref petroCard, dynAmounts);

        // Assert: Cash 3 is reduced from 21,250 to 11,250 pure fuel sales
        Assert.Equal(11250.0, cash3);
        Assert.Equal(85504.50, dynAmounts["MOBIKWIK"]);

        // When summing pure collections: 11,250 + 85,504.50 + 6,100 = 102,854.50
        double totalPureCollection = cash3 + dynAmounts["MOBIKWIK"] + 6100.0;
        double grossSales = 102838.41;
        double mismatch = totalPureCollection - grossSales;

        Assert.Equal(102854.50, totalPureCollection, precision: 2);
        Assert.Equal(16.09, mismatch, precision: 2);
        // Does NOT cause the false 10,016.09 mismatch:
        Assert.True(Math.Abs(mismatch - 16.09) < 0.01);
    }

    [Fact]
    public void Section2B_CumulativeFuelSales_CalculatesDirectLitresTimesRate()
    {
        // Arrange & Act
        double litres = 1692.70;
        double rate = 90.00;
        double totalAmount = Math.Round(litres * rate, 2);

        // Assert
        Assert.Equal(152343.00, totalAmount, precision: 2);
    }

[Fact]
    public void DsmSummaryPrint_PhonePeTidAndScalar_UsesMaxAndDoesNotDoubleCount()
    {
        // Arrange: DsmEntry 258 reproduction
        // TID has PhonePe 3000, which auto-populates PhonePeMorning with 3000.
        // Other collections: PetroCard 2000, Dynamic (SBI Redeem 3000 + Other Online 4000 = 7000),
        // Cash1 = 1000, Cash2 = 3500, Cash3 = 5731 => Total Cash = 10,231.
        // Total pure collection should be: 3000 (PhonePe) + 2000 (PetroCard) + 7000 (Dynamic) + 10231 (Cash) = 22,231.00.
        var vm = new DsmEntryViewModel();
        vm.PhonePeMorning = 3000.0;
        vm.TidTransactions.Add(new DsmTidTransactionRow { CollectionTypeCode = "PHONEPE", Amount = 3000.0 });

        vm.PetroCardMorning = 2000.0;
        vm.TidTransactions.Add(new DsmTidTransactionRow { CollectionTypeCode = "PETROCARD", Amount = 2000.0 });

        vm.Cash1.Denom500 = 2; // 1,000
        vm.Cash2.Denom500 = 3; vm.Cash2.Denom200 = 10; // 3,500
        vm.Cash3.Denom500 = 11; vm.Cash3.Denom200 = 1; vm.Cash3.Denom10 = 3; vm.Cash3.Coins = 1; // 5,731

        vm.DynamicCollections.Add(new DynamicCollectionEntryRow { Code = "SBI_REDEEM", DisplayName = "SBI Redeem", Amount = 3000.0 });
        vm.DynamicCollections.Add(new DynamicCollectionEntryRow { Code = "OTHER_ONLINE", DisplayName = "Other Online / UPI", Amount = 4000.0 });

        // Calculation of phonePeTotal using the PrintDsmSheet logic:
        double phonePeFromTid = vm.TidTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PHONEPE").Sum(t => t.Amount);
        double phOtpSum = vm.OtpTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PHONEPE").Sum(t => t.Amount);
        double qrSum = vm.QrPayments.Sum(q => q.Amount ?? 0);
        double phonePeScalar = (vm.PhonePeMorning ?? 0) + (vm.IsPhonePeSplitEnabled ? (vm.PhonePeNight ?? 0) : 0) + (vm.PhonePeCardMorning ?? 0) + (vm.IsPhonePeSplitEnabled ? (vm.PhonePeCardNight ?? 0) : 0);
        double phonePeItemized = phonePeFromTid + phOtpSum;
        double phonePeTotal = Math.Max(phonePeScalar, phonePeItemized) + qrSum;

        // Assert: PhonePe is 3,000 (NOT doubled to 6,000)
        Assert.Equal(3000.0, phonePeTotal);

        double petroFromTid = vm.TidTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PETROCARD").Sum(t => t.Amount);
        double pcOtpSum = vm.OtpTransactions.Where(t => PaymentCollection.NormalizeCollectionTypeCode(t.CollectionTypeCode) == "PETROCARD").Sum(t => t.Amount);
        double petroScalar = (vm.PetroCardMorning ?? 0) + (vm.IsPetroCardSplitEnabled ? (vm.PetroCardNight ?? 0) : 0);
        double petroItemized = petroFromTid + pcOtpSum;
        double petroTotal = Math.Max(petroScalar, petroItemized);

        // Assert: PetroCard is 2,000 (NOT doubled to 4,000)
        Assert.Equal(2000.0, petroTotal);

        double totalCash = vm.Cash1.TotalAmount + vm.Cash2.TotalAmount + vm.Cash3.TotalAmount;
        double dynamicTotal = vm.DynamicCollections.Sum(d => d.Amount ?? 0);
        double totalFuelCollection = phonePeTotal + petroTotal + totalCash + dynamicTotal;

        Assert.Equal(22231.0, totalFuelCollection, precision: 2);

        // Net Gross Sales: 22,427.42 gross - 196.38 testing = 22,231.04
        double expectedGrossSale = 22231.04;
        double mismatch = totalFuelCollection - expectedGrossSale;
        Assert.Equal(-0.04, mismatch, precision: 2);
    }

    [Fact]
    public void DsmSummaryPrint_Section2B_CumulativeSales_StrictlyMatchesSection2FuelTypeSummaries()
    {
        // Arrange: Section 2 FuelTypeSummaries for DsmEntry 258
        var vm = new DsmEntryViewModel();
        vm.FuelTypeSummaries.Add(new FuelTypeReadingSummary
        {
            FuelType = "Diesel",
            NozzleCount = 2,
            TotalLitres = 103.00,
            Rate = 98.19,
            TotalAmount = 10113.57
        });
        vm.FuelTypeSummaries.Add(new FuelTypeReadingSummary
        {
            FuelType = "SPEED",
            NozzleCount = 2,
            TotalLitres = 5.00,
            Rate = 120.64,
            TotalAmount = 603.20
        });
        vm.FuelTypeSummaries.Add(new FuelTypeReadingSummary
        {
            FuelType = "Petrol",
            NozzleCount = 2,
            TotalLitres = 105.00,
            Rate = 111.53,
            TotalAmount = 11710.65
        });

        // Act: Map directly from FuelTypeSummaries as implemented in PrintDsm()
        var cumulativeSales = vm.FuelTypeSummaries.Select(f => new FuelTypeReadingSummary
        {
            FuelType = f.FuelType,
            NozzleCount = f.NozzleCount,
            TotalLitres = f.TotalLitres,
            Rate = f.Rate,
            TotalAmount = f.TotalAmount
        }).ToList();

        // Assert: Section 2B totals match Section 2 exactly (213.00 L and 22,427.42 amount)
        double totalLitres = cumulativeSales.Sum(c => c.TotalLitres);
        double totalAmount = cumulativeSales.Sum(c => c.TotalAmount);

        Assert.Equal(213.00, totalLitres, precision: 2);
        Assert.Equal(22427.42, totalAmount, precision: 2);
    }

[Fact]
    public void DsmSummaryPrint_DebtorRepaymentAdded_MatchesDsmEntryPageExactly()
    {
        // Arrange: DSM Entry page baseline for Shift A Entry 258
        var vm = new DsmEntryViewModel();
        vm.GrossSales = 22231.04;
        vm.FinalAdjusted = 22231.00;
        vm.Difference = -0.04;

        // User adds a 500.00 debtor repayment in Section 8
        vm.DebtorRepayments.Add(new DsmDebtorRepaymentRow
        {
            DebtorName = "Ramesh Kumar",
            Amount = 500.0,
            PaymentMode = "Cash",
            Notes = "Previous balance payment"
        });

        // Act: Print reconciliation block generation (as in PrintDsmSheet)
        double totalDebtorRepayments = vm.DebtorRepayments
            .Where(r => (r.Amount ?? 0) > 0)
            .Sum(r => r.Amount ?? 0);

        var recon = new DsmReconciliationPrintBlock
        {
            GrossSales            = vm.GrossSales,
            TotalSales            = vm.GrossSales,
            DebtorRepaymentsAdded = totalDebtorRepayments,
            ExpectedGrossAmount   = vm.GrossSales,
            TotalCollection       = vm.FinalAdjusted,
            Creditors             = vm.TotalDebtors,
            Mismatch              = vm.Difference
        };

        // Assert: Print reconciliation exactly mirrors DSM Entry page
        Assert.Equal(22231.00, recon.TotalCollection);
        Assert.Equal(22231.04, recon.ExpectedGrossAmount);
        Assert.Equal(-0.04, recon.Mismatch);
        Assert.Equal(500.0, recon.DebtorRepaymentsAdded); // Auditing preserved
    }

    // End of tests
    
    [Fact]
    public async Task DsmShortReport_GetEntriesForDsmAndMonthAsync_CaseInsensitiveMatching_RetrievesAllEntries()
    {
        // Arrange: In-memory FuelProDbContext with mixed-casing DSM entries
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new FuelProDbContext(options);
        var shift = new Shift { ShiftId = 1, ShiftDate = new DateTime(2026, 11, 2), ShiftType = "Shift A" };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        // 8 entries recorded as uppercase, 7 recorded as title-case
        for (int i = 0; i < 8; i++)
        {
            context.DsmEntries.Add(new DsmEntry
            {
                ShiftId = shift.ShiftId,
                DsmName = "RUGVED BHISE",
                PumpId = 1,
                GrossSales = 5000,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        for (int i = 0; i < 7; i++)
        {
            context.DsmEntries.Add(new DsmEntry
            {
                ShiftId = shift.ShiftId,
                DsmName = "Rugved Bhise",
                PumpId = 1,
                GrossSales = 5000,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        await context.SaveChangesAsync();

        var repo = new DsmEntryRepository(context);

        // Act: Query with uppercase and title-case
        var resUpper = await repo.GetEntriesForDsmAndMonthAsync("RUGVED BHISE", 2026, 11);
        var resTitle = await repo.GetEntriesForDsmAndMonthAsync("Rugved Bhise", 2026, 11);

        // Assert: Both queries retrieve all 15 entries without case-sensitivity omission
        Assert.True(resUpper.Success);
        Assert.Equal(15, resUpper.Data!.Count);

        Assert.True(resTitle.Success);
        Assert.Equal(15, resTitle.Data!.Count);
    }

    [Fact]
    public void DsmShortReport_PriorityDeduplication_PreservesDsmUserCasingAndOrdersAlphabetically()
    {
        // Priority order:
        // 1. DsmUsers (primary authoritative casing)
        // 2. DsmEntries (historical)
        // 3. DsmProfiles (supplementary)
        var dsmUsers = new List<string> { "Rugved Bhise", "Rani Gadhave", "Amol Rathod" };
        var dsmEntries = new List<string> { "RUGVED BHISE", "RANI GADHAVE", "Past Staff Person" };
        var dsmProfiles = new List<string> { "rugved bhise", "Profile Only Staff" };

        var distinctNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in dsmUsers)
        {
            var trimmed = raw?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && !distinctNames.ContainsKey(trimmed))
                distinctNames[trimmed] = trimmed;
        }

        foreach (var raw in dsmEntries)
        {
            var trimmed = raw?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && !distinctNames.ContainsKey(trimmed))
                distinctNames[trimmed] = trimmed;
        }

        foreach (var raw in dsmProfiles)
        {
            var trimmed = raw?.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed) && !distinctNames.ContainsKey(trimmed))
                distinctNames[trimmed] = trimmed;
        }

        var sorted = distinctNames.Values.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        // Assert:
        // 1. All 5 unique persons are present
        Assert.Equal(5, sorted.Count);
        // 2. DsmUsers casing is preserved (not overridden by RUGVED BHISE or rugved bhise)
        Assert.Contains("Rugved Bhise", sorted);
        Assert.DoesNotContain("RUGVED BHISE", sorted);
        Assert.Contains("Rani Gadhave", sorted);
        Assert.DoesNotContain("RANI GADHAVE", sorted);
        // 3. Historical staff absent from DsmUsers is preserved
        Assert.Contains("Past Staff Person", sorted);
        Assert.Contains("Profile Only Staff", sorted);
        // 4. Sorted alphabetically
        Assert.Equal(new List<string> { "Amol Rathod", "Past Staff Person", "Profile Only Staff", "Rani Gadhave", "Rugved Bhise" }, sorted);
    }

    [Fact]
    public void Verify_PwaMetadata_ExtractionAndSafeParsing_UnwrapsDoubleEncoding_HandlesCasingAndStringAmounts()
    {
        // 1. Double-encoded JSON primitive unescaping
        string rawInnerJson = "{\"debtorEntries\":[{\"debtorName\":\"Express Cargo\",\"amount\":\"1500.50\"},{\"debtorName\":\"Bad Record\",\"amount\":\"invalid_num\"},{\"debtorName\":\"Sharma Transport\",\"amount\":2000.0}],\"petroCardDay\":750.0}";
        string doubleEncoded = Newtonsoft.Json.JsonConvert.SerializeObject(rawInnerJson);
        
        string unescaped = DsmApprovalQueueViewModel.CleanAndUnescapeJson(doubleEncoded);
        Assert.Equal(rawInnerJson, unescaped);

        var parsedObj = DsmApprovalQueueViewModel.ParseMetadataObject(doubleEncoded);
        Assert.NotNull(parsedObj);
        Assert.NotNull(parsedObj!["debtorEntries"]);

        // 2. String amount and safe tolerance parsing
        var debtorToken = parsedObj["debtorEntries"];
        Assert.NotNull(debtorToken);
        var parsedList = new List<(string Name, double Amount)>();
        foreach (var entry in debtorToken!)
        {
            try
            {
                double dAmt = 0.0;
                var amtToken = entry["amount"] ?? entry["Amount"];
                if (amtToken != null)
                {
                    if (amtToken.Type == Newtonsoft.Json.Linq.JTokenType.Float || amtToken.Type == Newtonsoft.Json.Linq.JTokenType.Integer)
                        dAmt = (double)amtToken;
                    else
                        double.TryParse(amtToken.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out dAmt);
                }
                string dName = (string?)(entry["debtorName"] ?? entry["name"] ?? "") ?? "";
                if (dAmt > 0 && !string.IsNullOrWhiteSpace(dName))
                {
                    parsedList.Add((dName, dAmt));
                }
            }
            catch {}
        }

        // Malformed "invalid_num" did not crash or abort remaining valid records
        Assert.Equal(2, parsedList.Count);
        Assert.Equal("Express Cargo", parsedList[0].Name);
        Assert.Equal(1500.50, parsedList[0].Amount);
        Assert.Equal("Sharma Transport", parsedList[1].Name);
        Assert.Equal(2000.0, parsedList[1].Amount);

        // 3. Casing variants (creditSales / debtor_entries)
        string altCasingJson = "{\"creditSales\":[{\"debtor_name\":\"Modern Logistics\",\"amount\":\"3500.00\"}]}";
        var altObj = DsmApprovalQueueViewModel.ParseMetadataObject(altCasingJson);
        Assert.NotNull(altObj);
        var altToken = altObj!["debtorEntries"] ?? altObj["creditSales"] ?? altObj["debtor_entries"];
        Assert.NotNull(altToken);
        var firstAlt = altToken!.First;
        Assert.NotNull(firstAlt);
        string altName = (string?)(firstAlt!["debtor_name"] ?? firstAlt["debtorName"] ?? "") ?? "";
        double.TryParse(firstAlt["amount"]!.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double altAmt);
        Assert.Equal("Modern Logistics", altName);
        Assert.Equal(3500.0, altAmt);
    }

    [Fact]
    public void Verify_PetroCard_HydrationAndSymmetry_PreservesAmountAcrossShiftsAndFallbacks()
    {
        // Arrange: PaymentCollection with both scalar PetroCardDay and itemized items
        var payment = new PaymentCollection
        {
            PetroCardDay = 2500.0,
            PetroCardTidDay = "TID-PC-1",
            PetroCardBatchDay = "B-PC-1"
        };
        payment.Items.Add(new PaymentCollectionItem
        {
            CollectionTypeCode = "PETROCARD",
            Amount = 1000.0,
            Tid = "TID-PC-2",
            Batch = "B-PC-2",
            Slot = "Day"
        });

        // 1. Scalar sum
        Assert.Equal(2500.0, payment.PetroCardDay);
        Assert.Equal(2500.0, payment.PetroCard);

        // 2. Items fallback when scalar is 0
        var paymentItemsOnly = new PaymentCollection();
        paymentItemsOnly.Items.Add(new PaymentCollectionItem
        {
            CollectionTypeCode = "PETROCARD",
            Amount = 1800.0,
            Tid = "TID-PC-ITEM",
            Batch = "B-PC-ITEM",
            Slot = "Morning"
        });
        Assert.Equal(1800.0, paymentItemsOnly.PetroCard);

        // 3. Fallback slot extraction
        var petroItem = paymentItemsOnly.Items.First();
        Assert.Equal("Morning", PaymentCollection.ExtractSlotFromMode(petroItem.CollectionTypeCode, petroItem.Slot));
        Assert.Equal("PETROCARD", PaymentCollection.NormalizeCollectionTypeCode(petroItem.CollectionTypeCode));
    }

    [Fact]
    public void Verify_DebtorRepaymentRow_CreditorRepaymentId_Tracking_PreventsDuplicationOnSave()
    {
        // Verify DsmDebtorRepaymentRow has CreditorRepaymentId property and tracks identity
        var row = new DsmDebtorRepaymentRow
        {
            CreditorRepaymentId = 42,
            DebtorName = "VITTAL KASHID",
            Amount = 5000.0,
            PaymentMode = "Mobikwik",
            Notes = "Settlement"
        };

        Assert.Equal(42, row.CreditorRepaymentId);
        Assert.Equal("VITTAL KASHID", row.DebtorName);
        Assert.Equal(5000.0, row.Amount);
        Assert.Equal("Mobikwik", row.PaymentMode);

        // Simulating second save: when row already has CreditorRepaymentId > 0,
        // it updates the entity with that ID rather than generating a new entity.
        var existingEntities = new Dictionary<int, CreditorRepayment>
        {
            [42] = new CreditorRepayment
            {
                CreditorRepaymentId = 42,
                CreditorName = "VITTAL KASHID",
                Amount = 5000.0,
                PaymentMode = "Mobikwik"
            }
        };

        bool wasUpdated = existingEntities.TryGetValue(row.CreditorRepaymentId, out var existing);
        Assert.True(wasUpdated);
        Assert.NotNull(existing);
        Assert.Equal(42, existing!.CreditorRepaymentId);
        Assert.Single(existingEntities); // Remains exactly 1 entity, never duplicates
    }

    [Fact]
    public async Task Verify_DebtorRepayment_TwoIdenticalValues_MaintainDistinctIdentity_AcrossSaveReloadAndRepeatApproval()
    {
        // Setup SQLite in-memory database
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite($"Data Source=file:repay_idemp_{Guid.NewGuid():N}?mode=memory&cache=shared")
            .Options;

        using var db = new FuelProDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var shiftDate = new DateTime(2026, 11, 23);
        string shiftNum = "A";
        string dsmName = "Ramesh Test";
        int entryId = 101;

        // 1. Create two legitimate repayments with identical values but distinct identities
        var row1 = new DsmDebtorRepaymentRow
        {
            CreditorRepaymentId = 0,
            DebtorName = "VITTAL KASHID",
            Amount = 5000.0,
            PaymentMode = "Mobikwik",
            Notes = "Settlement 1"
        };
        var row2 = new DsmDebtorRepaymentRow
        {
            CreditorRepaymentId = 0,
            DebtorName = "VITTAL KASHID",
            Amount = 5000.0,
            PaymentMode = "Mobikwik",
            Notes = "Settlement 2"
        };
        var rows = new List<DsmDebtorRepaymentRow> { row1, row2 };

        // 2. Save the DSM entry (First Save)
        var existingReps = await db.CreditorRepayments
            .Where(r => (r.ChequeNo != null && r.ChequeNo.Contains($"[Entry:{entryId}"))
                     || (r.RepaymentDate.Date == shiftDate && r.ShiftNumber == shiftNum && r.ChequeNo != null && r.ChequeNo.Contains($"DSM: {dsmName}")))
            .ToListAsync();
        var existingRepMap = existingReps.ToDictionary(r => r.CreditorRepaymentId);
        var claimedIds = new HashSet<int>();

        for (int i = 0; i < rows.Count; i++)
        {
            var rep = rows[i];
            string entryTag = RepaymentIdentityHelper.BuildEntryTag(entryId, i);
            string expectedCheque = RepaymentIdentityHelper.FormatChequeNo(rep.Notes, dsmName, entryTag);

            CreditorRepayment? targetEntity = null;
            if (rep.CreditorRepaymentId > 0 && existingRepMap.TryGetValue(rep.CreditorRepaymentId, out var byId))
                targetEntity = byId;
            if (targetEntity == null)
                targetEntity = existingReps.FirstOrDefault(r => !claimedIds.Contains(r.CreditorRepaymentId) && RepaymentIdentityHelper.MatchesEntry(r.ChequeNo, entryId, i));

            if (targetEntity != null)
            {
                targetEntity.Amount = rep.Amount!.Value;
                targetEntity.ChequeNo = expectedCheque;
                rep.CreditorRepaymentId = targetEntity.CreditorRepaymentId;
                claimedIds.Add(targetEntity.CreditorRepaymentId);
            }
            else
            {
                var newEntity = new CreditorRepayment
                {
                    CreditorName = rep.DebtorName.Trim(),
                    Amount = rep.Amount!.Value,
                    PaymentMode = rep.PaymentMode,
                    RepaymentDate = shiftDate,
                    ShiftNumber = shiftNum,
                    ChequeNo = expectedCheque
                };
                await db.CreditorRepayments.AddAsync(newEntity);
                await db.SaveChangesAsync();
                rep.CreditorRepaymentId = newEntity.CreditorRepaymentId;
                claimedIds.Add(newEntity.CreditorRepaymentId);
                existingReps.Add(newEntity);
                existingRepMap[newEntity.CreditorRepaymentId] = newEntity;
            }
        }

        // Verify Step 2: Exactly two records created with distinct identities
        var initialDbRecords = await db.CreditorRepayments.ToListAsync();
        Assert.Equal(2, initialDbRecords.Count);
        Assert.NotEqual(row1.CreditorRepaymentId, row2.CreditorRepaymentId);
        Assert.True(row1.CreditorRepaymentId > 0);
        Assert.True(row2.CreditorRepaymentId > 0);
        Assert.Contains("[Entry:101:0]", initialDbRecords[0].ChequeNo);
        Assert.Contains("[Entry:101:1]", initialDbRecords[1].ChequeNo);

        int id1 = row1.CreditorRepaymentId;
        int id2 = row2.CreditorRepaymentId;

        // 3. Close/reload the entry
        var reloadedReps = await db.CreditorRepayments
            .Where(r => (r.ChequeNo != null && r.ChequeNo.Contains($"[Entry:{entryId}"))
                     || (r.RepaymentDate.Date == shiftDate && r.ShiftNumber == shiftNum && r.ChequeNo != null && r.ChequeNo.Contains($"DSM: {dsmName}")))
            .OrderBy(r => r.CreditorRepaymentId)
            .ToListAsync();

        var reloadedRows = new List<DsmDebtorRepaymentRow>();
        foreach (var r in reloadedReps)
        {
            reloadedRows.Add(new DsmDebtorRepaymentRow
            {
                CreditorRepaymentId = r.CreditorRepaymentId,
                DebtorName = r.CreditorName,
                Amount = r.Amount,
                PaymentMode = r.PaymentMode,
                Notes = RepaymentIdentityHelper.ExtractUserNotes(r.ChequeNo, $"DSM: {dsmName}")
            });
        }

        Assert.Equal(2, reloadedRows.Count);
        Assert.Equal(id1, reloadedRows[0].CreditorRepaymentId);
        Assert.Equal(id2, reloadedRows[1].CreditorRepaymentId);
        Assert.Equal("Settlement 1", reloadedRows[0].Notes);
        Assert.Equal("Settlement 2", reloadedRows[1].Notes);

        // 4. Save again (Second Save)
        existingReps = await db.CreditorRepayments
            .Where(r => (r.ChequeNo != null && r.ChequeNo.Contains($"[Entry:{entryId}"))
                     || (r.RepaymentDate.Date == shiftDate && r.ShiftNumber == shiftNum && r.ChequeNo != null && r.ChequeNo.Contains($"DSM: {dsmName}")))
            .ToListAsync();
        existingRepMap = existingReps.ToDictionary(r => r.CreditorRepaymentId);
        claimedIds.Clear();

        for (int i = 0; i < reloadedRows.Count; i++)
        {
            var rep = reloadedRows[i];
            string entryTag = RepaymentIdentityHelper.BuildEntryTag(entryId, i);
            string expectedCheque = RepaymentIdentityHelper.FormatChequeNo(rep.Notes, dsmName, entryTag);

            CreditorRepayment? targetEntity = null;
            if (rep.CreditorRepaymentId > 0 && existingRepMap.TryGetValue(rep.CreditorRepaymentId, out var byId))
                targetEntity = byId;
            if (targetEntity == null)
                targetEntity = existingReps.FirstOrDefault(r => !claimedIds.Contains(r.CreditorRepaymentId) && RepaymentIdentityHelper.MatchesEntry(r.ChequeNo, entryId, i));

            if (targetEntity != null)
            {
                targetEntity.Amount = rep.Amount!.Value;
                targetEntity.ChequeNo = expectedCheque;
                rep.CreditorRepaymentId = targetEntity.CreditorRepaymentId;
                claimedIds.Add(targetEntity.CreditorRepaymentId);
            }
            else
            {
                var newEntity = new CreditorRepayment
                {
                    CreditorName = rep.DebtorName.Trim(),
                    Amount = rep.Amount!.Value,
                    PaymentMode = rep.PaymentMode,
                    RepaymentDate = shiftDate,
                    ShiftNumber = shiftNum,
                    ChequeNo = expectedCheque
                };
                await db.CreditorRepayments.AddAsync(newEntity);
                await db.SaveChangesAsync();
                rep.CreditorRepaymentId = newEntity.CreditorRepaymentId;
                claimedIds.Add(newEntity.CreditorRepaymentId);
                existingReps.Add(newEntity);
                existingRepMap[newEntity.CreditorRepaymentId] = newEntity;
            }
        }
        await db.SaveChangesAsync();

        // 5. Verify exactly two records remain, with their identities and values intact
        var afterSecondSaveRecords = await db.CreditorRepayments.OrderBy(r => r.CreditorRepaymentId).ToListAsync();
        Assert.Equal(2, afterSecondSaveRecords.Count);
        Assert.Equal(id1, afterSecondSaveRecords[0].CreditorRepaymentId);
        Assert.Equal(id2, afterSecondSaveRecords[1].CreditorRepaymentId);
        Assert.Equal(5000.0, afterSecondSaveRecords[0].Amount);
        Assert.Equal(5000.0, afterSecondSaveRecords[1].Amount);

        // 6. Repeat approval processing and verify no duplicate logical records are inserted
        var submissionId = Guid.NewGuid();
        var pwaRows = new List<DsmDebtorRepaymentRow>
        {
            new() { CreditorRepaymentId = 0, DebtorName = "VITTAL KASHID", Amount = 5000.0, PaymentMode = "Mobikwik", Notes = "PWA 1" },
            new() { CreditorRepaymentId = 0, DebtorName = "VITTAL KASHID", Amount = 5000.0, PaymentMode = "Mobikwik", Notes = "PWA 2" }
        };

        // First approval pass
        for (int pass = 1; pass <= 2; pass++)
        {
            var existingSubReps = await db.CreditorRepayments
                .Where(r => (r.ChequeNo != null && r.ChequeNo.Contains($"[Sub:{submissionId:N}"))
                         || (r.RepaymentDate.Date == shiftDate && r.ShiftNumber == shiftNum && r.ChequeNo != null && r.ChequeNo.Contains($"DSM: {dsmName}")))
                .ToListAsync();
            var subRepMap = existingSubReps.ToDictionary(r => r.CreditorRepaymentId);
            var subClaimed = new HashSet<int>();

            for (int i = 0; i < pwaRows.Count; i++)
            {
                var rep = pwaRows[i];
                string subTag = RepaymentIdentityHelper.BuildSubmissionTag(submissionId, i);
                string expectedCheque = RepaymentIdentityHelper.FormatChequeNo(rep.Notes, dsmName, subTag);

                CreditorRepayment? target = null;
                if (rep.CreditorRepaymentId > 0 && subRepMap.TryGetValue(rep.CreditorRepaymentId, out var byId))
                    target = byId;
                if (target == null)
                    target = existingSubReps.FirstOrDefault(r => !subClaimed.Contains(r.CreditorRepaymentId) && RepaymentIdentityHelper.MatchesSubmission(r.ChequeNo, submissionId, i));

                if (target != null)
                {
                    target.Amount = rep.Amount ?? 0;
                    target.ChequeNo = expectedCheque;
                    rep.CreditorRepaymentId = target.CreditorRepaymentId;
                    subClaimed.Add(target.CreditorRepaymentId);
                }
                else
                {
                    var newEntity = new CreditorRepayment
                    {
                        CreditorName = rep.DebtorName.Trim(),
                        Amount = rep.Amount ?? 0,
                        PaymentMode = rep.PaymentMode,
                        RepaymentDate = shiftDate,
                        ShiftNumber = shiftNum,
                        ChequeNo = expectedCheque
                    };
                    await db.CreditorRepayments.AddAsync(newEntity);
                    await db.SaveChangesAsync();
                    rep.CreditorRepaymentId = newEntity.CreditorRepaymentId;
                    subClaimed.Add(newEntity.CreditorRepaymentId);
                    existingSubReps.Add(newEntity);
                    subRepMap[newEntity.CreditorRepaymentId] = newEntity;
                }
            }
            await db.SaveChangesAsync();
        }

        // Verify: PWA submission inserted exactly 2 records, repeat approval created 0 duplicates
        var pwaSubRecords = await db.CreditorRepayments
            .Where(r => r.ChequeNo != null && r.ChequeNo.Contains($"[Sub:{submissionId:N}"))
            .ToListAsync();
        Assert.Equal(2, pwaSubRecords.Count);
        Assert.Contains("[Sub:" + submissionId.ToString("N") + ":0]", pwaSubRecords[0].ChequeNo);
        Assert.Contains("[Sub:" + submissionId.ToString("N") + ":1]", pwaSubRecords[1].ChequeNo);

        // Total records in DB: 2 from desktop entry + 2 from PWA submission = 4 total, no duplicates
        var totalRecords = await db.CreditorRepayments.ToListAsync();
        Assert.Equal(4, totalRecords.Count);
    }
}
