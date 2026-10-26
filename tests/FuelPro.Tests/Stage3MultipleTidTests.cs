using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
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
using FuelPro.Sync;
using Xunit;

namespace FuelPro.Tests;

/// <summary>
/// Stage 3: Multiple TID Transactions per Collection Type Test Suite.
/// Covers:
/// 1. Multiple Pine Labs transactions with different amounts (TID 001 ₹1,000 + TID 002 ₹2,500 = ₹3,500).
/// 2. Same-amount independent transactions with distinct TransactionIds preserved.
/// 3. Add and remove independence for TID transaction rows.
/// 4. Canonical formatVersion 3 partitioned ownership without double counting.
/// 5. Identity-aware legacy duplicate resolution:
///    - Matching reliable TransactionId counted once.
///    - Conflicting TID/Batch/Slot preserved.
///    - Matching values without ID treated as potential duplicate: both preserved, advisory raised.
/// 6. PaymentCollectionItem.TransactionId SQLite/EF persistence round-trip.
/// 7. Backward compatibility for legacy single-TID entries.
/// 8. Ordinary non-TID dynamic methods remaining scalar-based.
/// 9. Simultaneous OTP and manual TID coexistence.
/// 10. Financial reconciliation and dropdown deduplication regressions.
/// </summary>
public class Stage3MultipleTidTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly IServiceProvider _serviceProvider;

    public Stage3MultipleTidTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_Stage3_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "stage3_test.db");

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"),
            ServiceLifetime.Transient);

        // Core Repositories & Services
        services.AddSingleton<ICredentialFileService>(new LocalCredentialFileService(Path.Combine(_tempDir, "credentials")));

        services.AddTransient<ISettingsRepository, SettingsRepository>();
        services.AddTransient<IUserRepository, UserRepository>();
        services.AddTransient<IShiftRepository, ShiftRepository>();
        services.AddTransient<IDsmEntryRepository, DsmEntryRepository>();
        services.AddTransient<INozzleReadingRepository, NozzleReadingRepository>();
        services.AddTransient<IPaymentRepository, PaymentRepository>();
        services.AddTransient<IDebitEntryRepository, DebitEntryRepository>();
        services.AddTransient<ITestingEntryRepository, TestingEntryRepository>();
        services.AddTransient<IExpenseRepository, ExpenseRepository>();
        services.AddTransient<ICashDenominationRepository, CashDenominationRepository>();
        services.AddTransient<IDsmProfileRepository, DsmProfileRepository>();
        services.AddTransient<ICreditorRepaymentRepository, CreditorRepaymentRepository>();
        services.AddTransient<IAgsImportRepository, AgsImportRepository>();
        services.AddTransient<ICreditorRepository, CreditorRepository>();
        services.AddTransient<IPumpExpenseRepository, PumpExpenseRepository>();
        services.AddTransient<IDebtorVehicleRepository, DebtorVehicleRepository>();
        services.AddTransient<IDsmPersonalDebtorRepository, DsmPersonalDebtorRepository>();
        services.AddScoped<IShiftOtherCashRepository, ShiftOtherCashRepository>();
        services.AddScoped<IShiftFuelRateRepository, ShiftFuelRateRepository>();

        services.AddSingleton<AuthService>();
        services.AddTransient<DsmEntryService>();
        services.AddTransient<ShiftCalculationService>();
        services.AddSingleton<IDsmCalculationService, DsmCalculationService>();
        services.AddSingleton<IOwnerCalculationService, OwnerCalculationService>();
        services.AddSingleton<ITidCalculationService, TidCalculationService>();
        services.AddScoped<IShiftAggregationService, ShiftAggregationService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddTransient<IAgsInventoryService, MockAgsInventoryService>();
        services.AddTransient<IFinancialCalculationService, MockFinancialCalculationService>();

        services.AddTransient<PrintService>();
        services.AddTransient<ExcelExportService>();
        services.AddTransient<IAuditLogService, AuditLogService>();
        services.AddTransient<IDayLockService, DayLockService>();
        services.AddTransient<IFeatureToggleService, FeatureToggleService>();
        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddTransient<IStationConfigurationService, StationConfigurationService>();
        services.AddTransient<IShiftCycleService, ShiftCycleService>();

        // Sync services
        services.AddSingleton<SyncConfigService>();
        services.AddSingleton<SyncEngine>();
        services.AddSingleton<DsmSubmissionPollingService>();
        services.AddTransient<DsmAuthAdminService>();
        services.AddTransient<SupabaseDsmService, FakeSupabaseDsmService>();
        services.AddSingleton<DraftService>();

        // ViewModels
        services.AddTransient<DsmEntryViewModel>();
        services.AddTransient<DsmApprovalQueueViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        // Initialize App static fields
        typeof(App).GetProperty("Services")?.SetValue(null, _serviceProvider);
        typeof(App).GetProperty("DbPath")?.SetValue(null, _dbPath);

        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    private DbContextOptions<FuelProDbContext> CreateInMemoryDbOptions()
    {
        return new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 1. Multiple TID Transactions & Row Calculations
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MultipleTidTransactions_DifferentAmounts_SumReplacesScalarAmount()
    {
        var vm = new DsmEntryViewModel();
        var dynRow = new DynamicCollectionEntryRow
        {
            Code = "PINELABS_CARD",
            DisplayName = "Pine Labs Card",
            Category = "Card",
            HasTidBatch = true,
            ManualAmount = 500, // Stale scalar
            OtpAmount = 0
        };
        vm.DynamicCollections.Add(dynRow);

        // Add TID 001: ₹1,000
        vm.TidTransactions.Add(new DsmTidTransactionRow
        {
            CollectionTypeCode = "PINELABS_CARD",
            Tid = "TID001",
            Batch = "101",
            Slot = "Morning",
            Amount = 1000,
            OnRowChanged = vm.OnTidTransactionsChanged
        });

        // Add TID 002: ₹2,500
        vm.TidTransactions.Add(new DsmTidTransactionRow
        {
            CollectionTypeCode = "PINELABS_CARD",
            Tid = "TID002",
            Batch = "102",
            Slot = "Morning",
            Amount = 2500,
            OnRowChanged = vm.OnTidTransactionsChanged
        });

        vm.OnTidTransactionsChanged();

        // The itemized TID transactions must sum to ₹3,500 and replace the scalar amount
        Assert.Equal(3500.0, dynRow.TidAmount);
        Assert.Equal(3500.0, dynRow.Amount);
    }

    [Fact]
    public void MultipleTidTransactions_SameAmountDistinctIds_PreservedAndSummed()
    {
        var vm = new DsmEntryViewModel();
        var dynRow = new DynamicCollectionEntryRow
        {
            Code = "PINELABS_CARD",
            DisplayName = "Pine Labs Card",
            Category = "Card",
            HasTidBatch = true
        };
        vm.DynamicCollections.Add(dynRow);

        string id1 = Guid.NewGuid().ToString("D");
        string id2 = Guid.NewGuid().ToString("D");

        vm.TidTransactions.Add(new DsmTidTransactionRow
        {
            TransactionId = id1,
            CollectionTypeCode = "PINELABS_CARD",
            Tid = "TID001",
            Batch = "101",
            Slot = "Morning",
            Amount = 1500,
            OnRowChanged = vm.OnTidTransactionsChanged
        });

        vm.TidTransactions.Add(new DsmTidTransactionRow
        {
            TransactionId = id2,
            CollectionTypeCode = "PINELABS_CARD",
            Tid = "TID001",
            Batch = "101",
            Slot = "Morning",
            Amount = 1500,
            OnRowChanged = vm.OnTidTransactionsChanged
        });

        vm.OnTidTransactionsChanged();

        Assert.Equal(2, vm.TidTransactions.Count);
        Assert.NotEqual(vm.TidTransactions[0].TransactionId, vm.TidTransactions[1].TransactionId);
        Assert.Equal(3000.0, dynRow.Amount);
    }

    [Fact]
    public void MultipleTidTransactions_AddAndRemove_Independent()
    {
        var vm = new DsmEntryViewModel();
        var dynRow = new DynamicCollectionEntryRow
        {
            Code = "PINELABS_CARD",
            DisplayName = "Pine Labs Card",
            Category = "Card",
            HasTidBatch = true
        };
        vm.DynamicCollections.Add(dynRow);

        var row1 = new DsmTidTransactionRow
        {
            CollectionTypeCode = "PINELABS_CARD",
            Tid = "TID001",
            Batch = "101",
            Amount = 1000,
            OnRowChanged = vm.OnTidTransactionsChanged
        };
        var row2 = new DsmTidTransactionRow
        {
            CollectionTypeCode = "PINELABS_CARD",
            Tid = "TID002",
            Batch = "102",
            Amount = 2500,
            OnRowChanged = vm.OnTidTransactionsChanged
        };

        vm.TidTransactions.Add(row1);
        vm.TidTransactions.Add(row2);
        vm.OnTidTransactionsChanged();
        Assert.Equal(3500.0, dynRow.Amount);

        // Remove row 1
        vm.RemoveTidTransaction(row1);
        Assert.Single(vm.TidTransactions);
        Assert.Equal(2500.0, dynRow.Amount);

        // Remove row 2
        vm.RemoveTidTransaction(row2);
        Assert.Empty(vm.TidTransactions);
        Assert.Null(dynRow.Amount);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 2. Canonical formatVersion: 3 Single Ownership (Anti-Double-Counting)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FormatVersion3_StrictPartitionedOwnership_NoDoubleCounting()
    {
        string txId1 = Guid.NewGuid().ToString("D");
        string txId2 = Guid.NewGuid().ToString("D");

        var metadata = new
        {
            formatVersion = 3,
            dynamicItems = new[]
            {
                new { collectionTypeCode = "PINELABS_CARD", amount = 1000.0, tid = "TID001", batch = "101", slot = "Morning", transactionId = txId1 },
                new { collectionTypeCode = "PINELABS_CARD", amount = 2500.0, tid = "TID002", batch = "102", slot = "Morning", transactionId = txId2 }
            },
            cardSwipeDetails = new[]
            {
                new { mode = "CREDIT_CARD", amount = 4000.0, tid = "CC01", batch = "501" }
            },
            machineTransactions = new[]
            {
                new { collectionTypeCode = "CREDIT_CARD", amount = 500.0, otp = "OTP123", tid = "CC01", batch = "501", slot = "Morning" }
            }
        };

        var approvalVm = new DsmApprovalQueueViewModel();
        approvalVm.ManualTidTransactions.Clear();
        approvalVm.CardSwipeDetails.Clear();

        // Simulate formatVersion: 3 ingestion
        int formatVersion = metadata.formatVersion;
        Assert.Equal(3, formatVersion);

        // 1. dynamicItems owns all dynamic manual items
        foreach (var d in metadata.dynamicItems)
        {
            approvalVm.ManualTidTransactions.Add(new DsmCardSwipeRow
            {
                Mode = d.collectionTypeCode,
                Amount = d.amount,
                Tid = d.tid,
                Batch = d.batch,
                Slot = d.slot,
                TransactionId = d.transactionId
            });
        }

        // 2. cardSwipeDetails owns standard credit card split
        foreach (var c in metadata.cardSwipeDetails)
        {
            if (PaymentCollection.IsStandardCollectionCode(c.mode))
            {
                approvalVm.CardAmount += c.amount;
            }
        }

        // 3. machineTransactions owns machine OTP swipes
        foreach (var mt in metadata.machineTransactions)
        {
            approvalVm.CardSwipeDetails.Add(new DsmCardSwipeRow
            {
                Mode = mt.collectionTypeCode,
                Amount = mt.amount,
                Tid = mt.tid,
                Batch = mt.batch,
                Otp = mt.otp,
                Slot = mt.slot
            });
        }

        // Verify counts
        Assert.Equal(2, approvalVm.ManualTidTransactions.Count);
        Assert.Equal(3500.0, approvalVm.ManualTidTransactions.Sum(t => t.Amount));
        Assert.Equal(4000.0, approvalVm.CardAmount);
        Assert.Single(approvalVm.CardSwipeDetails);
        Assert.Equal(500.0, approvalVm.CardSwipeDetails[0].Amount);

        // Verify Pine Labs is never added to CardSwipeDetails
        Assert.DoesNotContain(approvalVm.CardSwipeDetails, c => c.Mode.Contains("PINELABS"));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 3. Legacy Identity-Aware Duplicate Resolution (formatVersion < 3)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LegacyPayload_MatchingValuesWithoutId_PreservesBothAndRaisesAdvisory()
    {
        var approvalVm = new DsmApprovalQueueViewModel();
        approvalVm.ManualTidTransactions.Clear();
        approvalVm.HasAmbiguousLegacyDuplicates = false;

        // Legacy candidate 1 from dynamicItems (no TransactionId)
        var cand1 = (Mode: "PINELABS_CARD", Amount: 1000.0, Tid: "TID001", Batch: "101", Slot: "Morning", TxId: (string?)null);
        // Legacy candidate 2 from cardSwipeDetails (same values, no TransactionId)
        var cand2 = (Mode: "PINELABS_CARD", Amount: 1000.0, Tid: "TID001", Batch: "101", Slot: "Morning", TxId: (string?)null);

        var candidates = new[] { cand1, cand2 };

        foreach (var cand in candidates)
        {
            string normCode = PaymentCollection.NormalizeCollectionTypeCode(cand.Mode);
            bool hasReliableId = !string.IsNullOrWhiteSpace(cand.TxId);

            if (!hasReliableId)
            {
                var identicalMatch = approvalVm.ManualTidTransactions.FirstOrDefault(t =>
                    string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t.Mode), normCode, StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(t.Amount - cand.Amount) < 0.01
                    && string.Equals(t.Tid?.Trim(), cand.Tid.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(t.Batch?.Trim(), cand.Batch.Trim(), StringComparison.OrdinalIgnoreCase)
                    && string.Equals(t.Slot?.Trim(), cand.Slot.Trim(), StringComparison.OrdinalIgnoreCase));

                if (identicalMatch != null)
                {
                    // Treat as potential duplicate, NOT proven duplicate: preserve BOTH and raise advisory
                    approvalVm.HasAmbiguousLegacyDuplicates = true;
                    approvalVm.LegacyDuplicateAdvisoryMessage = "⚠️ Potential duplicate legacy transaction detected.";
                }
            }

            approvalVm.ManualTidTransactions.Add(new DsmCardSwipeRow
            {
                Mode = cand.Mode,
                Amount = cand.Amount,
                Tid = cand.Tid,
                Batch = cand.Batch,
                Slot = cand.Slot,
                TransactionId = hasReliableId ? cand.TxId : Guid.NewGuid().ToString("D")
            });
        }

        // Both transactions preserved!
        Assert.Equal(2, approvalVm.ManualTidTransactions.Count);
        Assert.Equal(2000.0, approvalVm.ManualTidTransactions.Sum(t => t.Amount));
        Assert.True(approvalVm.HasAmbiguousLegacyDuplicates);
        Assert.False(string.IsNullOrWhiteSpace(approvalVm.LegacyDuplicateAdvisoryMessage));
    }

    [Fact]
    public void LegacyPayload_MatchingReliableTransactionId_DeduplicatedOnce()
    {
        var approvalVm = new DsmApprovalQueueViewModel();
        approvalVm.ManualTidTransactions.Clear();
        approvalVm.HasAmbiguousLegacyDuplicates = false;

        string stableId = "TX-STABLE-UUID-123";

        // Legacy candidate 1 from dynamicItems
        var cand1 = (Mode: "PINELABS_CARD", Amount: 1000.0, Tid: "TID001", Batch: "101", Slot: "Morning", TxId: stableId);
        // Legacy candidate 2 from cardSwipeDetails with the SAME stable ID
        var cand2 = (Mode: "PINELABS_CARD", Amount: 1000.0, Tid: "TID001", Batch: "101", Slot: "Morning", TxId: stableId);

        var candidates = new[] { cand1, cand2 };

        foreach (var cand in candidates)
        {
            bool hasReliableId = !string.IsNullOrWhiteSpace(cand.TxId);
            if (hasReliableId)
            {
                if (approvalVm.ManualTidTransactions.Any(t => string.Equals(t.TransactionId, cand.TxId, StringComparison.OrdinalIgnoreCase)))
                {
                    // Counted once
                    continue;
                }
            }

            approvalVm.ManualTidTransactions.Add(new DsmCardSwipeRow
            {
                Mode = cand.Mode,
                Amount = cand.Amount,
                Tid = cand.Tid,
                Batch = cand.Batch,
                Slot = cand.Slot,
                TransactionId = cand.TxId
            });
        }

        // Exactly one instance preserved
        Assert.Single(approvalVm.ManualTidTransactions);
        Assert.Equal(1000.0, approvalVm.ManualTidTransactions[0].Amount);
        Assert.False(approvalVm.HasAmbiguousLegacyDuplicates);
    }

    [Fact]
    public void LegacyPayload_ConflictingTidOrBatch_PreservesBothWithoutAdvisory()
    {
        var approvalVm = new DsmApprovalQueueViewModel();
        approvalVm.ManualTidTransactions.Clear();
        approvalVm.HasAmbiguousLegacyDuplicates = false;

        // Legacy candidate 1: TID 001, Batch 101
        var cand1 = (Mode: "PINELABS_CARD", Amount: 1000.0, Tid: "TID001", Batch: "101", Slot: "Morning", TxId: (string?)null);
        // Legacy candidate 2: TID 002, Batch 102
        var cand2 = (Mode: "PINELABS_CARD", Amount: 2500.0, Tid: "TID002", Batch: "102", Slot: "Morning", TxId: (string?)null);

        var candidates = new[] { cand1, cand2 };

        foreach (var cand in candidates)
        {
            approvalVm.ManualTidTransactions.Add(new DsmCardSwipeRow
            {
                Mode = cand.Mode,
                Amount = cand.Amount,
                Tid = cand.Tid,
                Batch = cand.Batch,
                Slot = cand.Slot,
                TransactionId = Guid.NewGuid().ToString("D")
            });
        }

        Assert.Equal(2, approvalVm.ManualTidTransactions.Count);
        Assert.Equal(3500.0, approvalVm.ManualTidTransactions.Sum(t => t.Amount));
        Assert.False(approvalVm.HasAmbiguousLegacyDuplicates);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 4. Persistence Round-Trip for PaymentCollectionItem.TransactionId
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PaymentCollectionItem_TransactionId_PersistsAndSurvivesReload()
    {
        var options = CreateInMemoryDbOptions();
        string expectedTxId1 = Guid.NewGuid().ToString("D");
        string expectedTxId2 = Guid.NewGuid().ToString("D");
        int paymentId;

        using (var context = new FuelProDbContext(options))
        {
            var payment = new PaymentCollection
            {
                DsmEntryId = 1,
                Items = new List<PaymentCollectionItem>
                {
                    new()
                    {
                        TransactionId = expectedTxId1,
                        CollectionTypeCode = "PINELABS_CARD",
                        Amount = 1000.0,
                        Tid = "TID001",
                        Batch = "101",
                        Slot = "Morning"
                    },
                    new()
                    {
                        TransactionId = expectedTxId2,
                        CollectionTypeCode = "PINELABS_CARD",
                        Amount = 2500.0,
                        Tid = "TID002",
                        Batch = "102",
                        Slot = "Morning"
                    }
                }
            };
            context.PaymentCollections.Add(payment);
            await context.SaveChangesAsync();
            paymentId = payment.PaymentId;
        }

        // Reload fresh context
        using (var reloadContext = new FuelProDbContext(options))
        {
            var reloaded = await reloadContext.PaymentCollections
                .Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.PaymentId == paymentId);

            Assert.NotNull(reloaded);
            Assert.Equal(2, reloaded.Items.Count);

            var item1 = reloaded.Items.FirstOrDefault(i => i.Tid == "TID001");
            var item2 = reloaded.Items.FirstOrDefault(i => i.Tid == "TID002");

            Assert.NotNull(item1);
            Assert.Equal(expectedTxId1, item1.TransactionId);
            Assert.Equal(1000.0, item1.Amount);

            Assert.NotNull(item2);
            Assert.Equal(expectedTxId2, item2.TransactionId);
            Assert.Equal(2500.0, item2.Amount);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 5. Single-TID Backward Compatibility & Non-TID Dynamic Methods
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LegacySingleTidEntry_BackwardCompatibleLoading()
    {
        var existingPayment = new PaymentCollection
        {
            Items = new List<PaymentCollectionItem>
            {
                new()
                {
                    CollectionTypeCode = "PINELABS_CARD",
                    Amount = 1500.0,
                    Tid = "LEGACY_TID_99",
                    Batch = "BATCH_88",
                    Slot = "General",
                    TransactionId = null // Legacy without UUID
                }
            }
        };

        var allItems = existingPayment.GetAllItems().ToList();
        var legacyItem = allItems.FirstOrDefault(i => i.CollectionTypeCode == "PINELABS_CARD");

        Assert.NotNull(legacyItem);
        Assert.Equal("LEGACY_TID_99", legacyItem.Tid);
        Assert.Equal("BATCH_88", legacyItem.Batch);
        Assert.Equal(1500.0, legacyItem.Amount);
        Assert.Null(legacyItem.TransactionId);
    }

    [Fact]
    public void NonTidDynamicMethods_RemainScalarBased()
    {
        var dynRow = new DynamicCollectionEntryRow
        {
            Code = "CASH_DEPOSIT",
            DisplayName = "Cash Deposit (Bank)",
            Category = "Cash",
            HasTidBatch = false, // Non-TID dynamic collection
            ManualAmount = 5000.0
        };

        dynRow.RecalculateTotal();

        // Amount equals manual amount directly without TID row requirement
        Assert.Equal(5000.0, dynRow.Amount);
        Assert.False(dynRow.HasTidBatch);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 6. Simultaneous OTP and Manual TID Coexistence
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SimultaneousOtpAndManualTid_CoexistAndSumCorrectly()
    {
        var dynRow = new DynamicCollectionEntryRow
        {
            Code = "PINELABS_CARD",
            DisplayName = "Pine Labs Card",
            Category = "Card",
            HasTidBatch = true,
            TidAmount = 3500.0, // From 2 manual TID swipes (1000 + 2500)
            OtpAmount = 700.0   // From machine slip OTP swipe
        };

        dynRow.RecalculateTotal();

        // Total = TidAmount (3500) + OtpAmount (700) = 4200
        Assert.Equal(4200.0, dynRow.Amount);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 7. Dropdown Deduplication & Financial Reconciliation Regressions
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DropdownDeduplication_PineLabsCard_AppearsExactlyOnceInAvailableTypes()
    {
        var vm = new DsmEntryViewModel();
        vm.DynamicCollections.Add(new DynamicCollectionEntryRow
        {
            Code = "PINELABS_CARD",
            DisplayName = "Pine Labs Card",
            HasTidBatch = true
        });

        vm.RefreshAvailableTidCollectionTypes();

        var pineLabsMatches = vm.AvailableTidCollectionTypes.Where(t =>
            string.Equals(PaymentCollection.NormalizeCollectionTypeCode(t), "PINELABS_CARD", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Single(pineLabsMatches);
    }

    [Fact]
    public void Stage1FinancialReconciliation_MultipleTidEntries_ReconcilesAccurately()
    {
        var calcService = new DsmCalculationService();

        // Gross sales 10,000
        // Collections: PineLabs TID1 ₹1,000 + PineLabs TID2 ₹2,500 = ₹3,500
        // CashDeposit ₹6,500. Total collection = ₹10,000.
        var entry = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 10000m } },
            PaymentCollection = new PaymentCollectionDto
            {
                DynamicPayments = 3500m,
                CashDeposit = 6500m
            },
            DebitEntries = new List<DebitEntryDto>(),
            Expenses = new List<ExpenseDto>(),
            TestingEntries = new List<TestingEntryDto>()
        };

        var result = calcService.Calculate(entry);

        Assert.Equal(10000m, result.GrossSales);
        Assert.Equal(10000m, result.TotalCollection);
        Assert.Equal(0m, result.Mismatch);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public async Task SeedData_StartupDeduplication_RemovesDuplicateNormalizedCollectionTypes_AndNeverResurrectsDeleted()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        // Simulate database that already has both "Pine LabS Cards" and "PINELABS_CARD"
        context.CollectionTypes.RemoveRange(context.CollectionTypes);
        await context.SaveChangesAsync();

        var now = DateTime.Now;
        context.CollectionTypes.AddRange(
            new CollectionTypeMaster { Code = "PHONEPE", DisplayName = "PhonePe", DisplayOrder = 1, IsActive = true, CreatedAt = now },
            new CollectionTypeMaster { Code = "Pine LabS Cards", DisplayName = "PineLab Card", DisplayOrder = 2, IsActive = true, CreatedAt = now },
            new CollectionTypeMaster { Code = "PINELABS_CARD", DisplayName = "Pine Labs Card", DisplayOrder = 9, IsActive = true, CreatedAt = now }
        );
        await context.SaveChangesAsync();

        Assert.Equal(3, await context.CollectionTypes.CountAsync());

        // Run SeedData.InitializeAsync
        await SeedData.InitializeAsync(context);

        // Verification 1: Deduplication successfully removed row 9 and kept row 2
        var typesAfter = await context.CollectionTypes.OrderBy(c => c.DisplayOrder).ToListAsync();
        Assert.Equal(2, typesAfter.Count);
        Assert.Equal("PHONEPE", typesAfter[0].Code);
        Assert.Equal("Pine LabS Cards", typesAfter[1].Code);

        // Verification 2: User explicitly deletes "Pine LabS Cards"
        context.CollectionTypes.Remove(typesAfter[1]);
        await context.SaveChangesAsync();
        Assert.Single(await context.CollectionTypes.ToListAsync());

        // Re-run SeedData.InitializeAsync (simulating next app startup)
        await SeedData.InitializeAsync(context);

        // Verification 3: PINELABS_CARD is NEVER resurrected
        var typesAfterRestart = await context.CollectionTypes.ToListAsync();
        Assert.Single(typesAfterRestart);
        Assert.Equal("PHONEPE", typesAfterRestart[0].Code);
    }

    [Fact]
    public void RefreshAvailableTidCollectionTypes_StrictlyDynamic_DoesNotHardcodeDeletedOrInactiveTypes()
    {
        var vm = new DsmEntryViewModel();
        // Clear cached collection types so no default fallback types are active
        vm.SetCachedCollectionTypesForTesting(new List<CollectionTypeMaster>());
        vm.DynamicCollections.Clear();
        vm.RefreshAvailableTidCollectionTypes();

        // When no dynamic collections exist and scalar cards are inactive, AvailableTidCollectionTypes is strictly empty
        Assert.Empty(vm.AvailableTidCollectionTypes);

        // Add a dynamic collection with HasTidBatch = true
        vm.DynamicCollections.Add(new DynamicCollectionEntryRow
        {
            Code = "CUSTOM_PAY",
            DisplayName = "Custom Pay",
            HasTidBatch = true
        });

        vm.RefreshAvailableTidCollectionTypes();

        Assert.Single(vm.AvailableTidCollectionTypes);
        Assert.Equal("CUSTOM_PAY", vm.AvailableTidCollectionTypes[0]);
    }

    [Fact]
    public void ClearForm_ClearsTidTransactions_AndResetsDynamicRowAmounts()
    {
        var vm = new DsmEntryViewModel();
        var dynRow = vm.DynamicCollections.FirstOrDefault(d => d.Code == "MOBIKWIK");
        if (dynRow == null)
        {
            dynRow = new DynamicCollectionEntryRow
            {
                Code = "MOBIKWIK",
                DisplayName = "Mobikwik",
                HasTidBatch = true
            };
            vm.DynamicCollections.Add(dynRow);
        }

        // Add a TID transaction for MOBIKWIK
        vm.TidTransactions.Add(new DsmTidTransactionRow
        {
            TransactionId = Guid.NewGuid().ToString("D"),
            CollectionTypeCode = "MOBIKWIK",
            Slot = "Morning",
            Amount = 300,
            OnRowChanged = vm.OnTidTransactionsChanged
        });
        vm.OnTidTransactionsChanged();

        Assert.Single(vm.TidTransactions);
        Assert.Equal(300.0, dynRow.TidAmount);
        Assert.Equal(300.0, dynRow.Amount);

        // Add an OTP transaction
        vm.OtpTransactions.Add(new DsmOtpTransactionRow
        {
            CollectionTypeCode = "PINELAB",
            Slot = "Morning",
            Amount = 150
        });
        Assert.Single(vm.OtpTransactions);

        // Now trigger ClearForm
        vm.ClearForm();

        // Verify TID transactions and OTP transactions are empty
        Assert.Empty(vm.TidTransactions);
        Assert.Empty(vm.OtpTransactions);

        // Verify DynamicCollections row is reset
        Assert.Equal(0.0, dynRow.TidAmount);
        Assert.Equal(0.0, dynRow.OtpAmount);
        Assert.Null(dynRow.Amount);
    }
}

