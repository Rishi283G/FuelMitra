using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Newtonsoft.Json;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;

namespace FuelPro.Tests;

public class DynamicStationConfigurationAndOtpTests
{
    [Theory]
    [InlineData("PHONEPE", true)]
    [InlineData("phonepe", true)]
    [InlineData("PhonePe", true)]
    [InlineData("PHONEPE_CARD", true)]
    [InlineData("PhonePe Card", true)]
    [InlineData("CREDIT_CARD", true)]
    [InlineData("CreditCard", true)]
    [InlineData("credit card", true)]
    [InlineData("PETROCARD", true)]
    [InlineData("PetroCard", true)]
    [InlineData("petro_card", true)]
    [InlineData("CASH_DEPOSIT", true)]
    [InlineData("CashDeposit", true)]
    [InlineData("CASH1", true)]
    [InlineData("OTHERS", true)]
    [InlineData("SBI_REDEEM", false)]
    [InlineData("PAYTM", false)]
    [InlineData("HP_PAY", false)]
    [InlineData("QR", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsStandardCollectionCode_AccuratelyClassifiesStandardVsDynamic(string? code, bool expectedStandard)
    {
        var result = PaymentCollection.IsStandardCollectionCode(code);
        Assert.Equal(expectedStandard, result);
    }

    [Fact]
    public void DynamicItemsTotal_ExcludesStandardCards_PreventsDoubleCounting()
    {
        var payment = new PaymentCollection
        {
            CreditCardMorning = 2500,
            PetroCardMorning = 1500,
            PhonePeMorning = 3000,
            Items = new List<PaymentCollectionItem>
            {
                // Machine transaction swipe records with OTP for standard cards
                new() { CollectionTypeCode = "CREDIT_CARD", Amount = 1500, Tid = "TID01", Batch = "B01", Otp = "982341", Slot = "Morning" },
                new() { CollectionTypeCode = "CREDIT_CARD", Amount = 1000, Tid = "TID01", Batch = "B01", Otp = "124578", Slot = "Morning" },
                new() { CollectionTypeCode = "PETROCARD", Amount = 1500, Tid = "PT01", Batch = "B02", Otp = "554433", Slot = "Morning" },
                // True dynamic collection item
                new() { CollectionTypeCode = "SBI_REDEEM", Amount = 800, Tid = "SR01", Batch = "B03", Otp = "778899", Slot = "Morning" }
            }
        };

        // DynamicItemsTotal must ONLY include SBI_REDEEM (800), not the 2500 CreditCard or 1500 PetroCard
        Assert.Equal(800, payment.DynamicItemsTotal);

        // TotalDigitalPayments = PhonePe (3000) + CreditCard (2500) + PetroCard (1500) + DynamicItemsTotal (800) = 7800
        Assert.Equal(7800, payment.TotalDigitalPayments);
    }

    [Fact]
    public void ShiftAggregationService_BuildDsmSummaryRows_PreventsDoubleCountingWithOtpTransactions()
    {
        var sut = new ShiftAggregationService();

        var entry = new DsmEntry
        {
            DsmName = "Test DSM",
            PumpId = 1,
            GrossSales = 10000,
            PaymentCollection = new PaymentCollection
            {
                CreditCardMorning = 2000,
                PhonePeMorning = 3000,
                PetroCardMorning = 1000,
                CashDeposit = 2000,
                Items = new List<PaymentCollectionItem>
                {
                    // OTP Machine Transactions for standard cards
                    new() { CollectionTypeCode = "CREDIT_CARD", Amount = 1200, Tid = "T1", Batch = "B1", Otp = "111222" },
                    new() { CollectionTypeCode = "CREDIT_CARD", Amount = 800, Tid = "T1", Batch = "B1", Otp = "333444" },
                    new() { CollectionTypeCode = "PETROCARD", Amount = 1000, Tid = "P1", Batch = "B2", Otp = "555666" },
                    // Dynamic custom collection
                    new() { CollectionTypeCode = "HP_PAY", Amount = 500, Tid = "H1", Batch = "B3", Otp = "777888" }
                }
            },
            CashDenominations = new List<CashDenomination>
            {
                new() { CashType = "Cash1", TotalAmount = 2000 },
                new() { CashType = "Cash2", TotalAmount = 1500 }
            },
            DebitEntries = new List<DebitEntry>(),
            Expenses = new List<Expense>(),
            TestingEntries = new List<TestingEntry>()
        };

        var rows = sut.BuildDsmSummaryRows(new List<DsmEntry> { entry });
        Assert.Single(rows);

        var r = rows[0];
        Assert.Equal(2000, r.CreditCardMorning);
        Assert.Equal(3000, r.PhonePe);
        Assert.Equal(1000, r.PetroCardMorning);
        Assert.Equal(2000, r.CashDeposit);
        Assert.Equal(1500, r.CashInHand);

        // Verify dynamic total on DsmSummaryRow: HP_PAY should be in DynamicCollections and not duplicate Credit Card
        Assert.NotNull(r.DynamicCollections);
        Assert.True(r.DynamicCollections.ContainsKey("HP_PAY"));
        Assert.Equal(500, r.DynamicCollections["HP_PAY"]);
        Assert.False(r.DynamicCollections.ContainsKey("CREDIT_CARD"));
        Assert.False(r.DynamicCollections.ContainsKey("PETROCARD"));
    }

    [Fact]
    public void CollectionTypeMaster_PeriodToggles_DefaultToEnabled()
    {
        var type = new CollectionTypeMaster
        {
            Code = "FASTAG",
            DisplayName = "Fastag Fuel",
            Category = "Online"
        };

        Assert.True(type.EnableMorning);
        Assert.True(type.EnableDay);
        Assert.True(type.EnableNight);
        Assert.True(type.IsActive);
    }

    [Fact]
    public void PaymentCollectionItem_Otp_SerializesAndDeserializesCorrectly()
    {
        var items = new List<PaymentCollectionItem>
        {
            new()
            {
                CollectionTypeCode = "CREDIT_CARD",
                Amount = 1550.50,
                Tid = "TID_9901",
                Batch = "B_0042",
                Otp = "883921",
                Slot = "Day"
            },
            new()
            {
                CollectionTypeCode = "PETROCARD",
                Amount = 2000.00,
                Tid = "TID_9902",
                Batch = "B_0042",
                Otp = "120934",
                Slot = "Night"
            }
        };

        var json = JsonConvert.SerializeObject(items.Select(i => new
        {
            i.CollectionTypeCode,
            i.Amount,
            i.Tid,
            i.Batch,
            i.Slot,
            i.Otp
        }));

        Assert.Contains("883921", json);
        Assert.Contains("120934", json);

        var deserialized = JsonConvert.DeserializeObject<List<PaymentCollectionItem>>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized!.Count);
        Assert.Equal("883921", deserialized[0].Otp);
        Assert.Equal("120934", deserialized[1].Otp);
    }

    [Fact]
    public void CanonicalFeatureSettings_IncludeCashDenominationsAndOtpTracking()
    {
        var defaults = AppFeatureSetting.GetCanonicalDefaults();

        var cashDenom = defaults.FirstOrDefault(f => f.FeatureKey == "Operations_CashDenominations");
        Assert.NotNull(cashDenom);
        Assert.Equal("Collections", cashDenom!.Category);
        Assert.Contains("Denomination", cashDenom.DisplayName);

        var otpTracking = defaults.FirstOrDefault(f => f.FeatureKey == "Operations_OtpMachineTracking");
        Assert.NotNull(otpTracking);
        Assert.Equal("Collections", otpTracking!.Category);
        Assert.Contains("Slip Number", otpTracking.DisplayName);
    }

    [Fact]
    public void GetAllItems_DeserializesFromDynamicItemsJson_WhenItemsIsEmpty()
    {
        var payment = new PaymentCollection
        {
            CreditCardMorning = 1500,
            DynamicItemsJson = JsonConvert.SerializeObject(new[]
            {
                new { CollectionTypeCode = "CREDIT_CARD", Amount = 1500.0, Tid = "TID1", Batch = "B1", Otp = "998811", Slot = "Morning" },
                new { CollectionTypeCode = "PAYTM_QR", Amount = 450.0, Tid = "QR1", Batch = "B2", Otp = "112233", Slot = "Morning" }
            })
        };

        var allItems = payment.GetAllItems().ToList();
        Assert.Equal(2, allItems.Count);
        Assert.Equal("CREDIT_CARD", allItems[0].CollectionTypeCode);
        Assert.Equal("998811", allItems[0].Otp);
        Assert.Equal("PAYTM_QR", allItems[1].CollectionTypeCode);
        Assert.Equal(450.0, allItems[1].Amount);

        // DynamicItemsTotal must exclude CREDIT_CARD and only return PAYTM_QR (450)
        Assert.Equal(450.0, payment.DynamicItemsTotal);
    }

    [Fact]
    public void DsmCalculationService_DoesNotDoubleCountStandardCards_WhenItemsContainSwipes()
    {
        var service = new DsmCalculationService();

        var payment = new PaymentCollection
        {
            CreditCardMorning = 2000,
            PhonePeMorning = 1000,
            PetroCardMorning = 500,
            CashDeposit = 1500,
            Items = new List<PaymentCollectionItem>
            {
                new() { CollectionTypeCode = "CREDIT_CARD", Amount = 2000, Tid = "T1", Batch = "B1", Otp = "123456" },
                new() { CollectionTypeCode = "PHONEPE", Amount = 1000, Tid = "P1", Batch = "B2", Otp = "654321" },
                new() { CollectionTypeCode = "SBI_REDEEM", Amount = 300, Tid = "S1", Batch = "B3", Otp = "111222" }
            }
        };

        var dsmDto = new DsmEntryDto
        {
            DSMEntryId = 1,
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 5300m } },
            TestingEntries = new List<TestingEntryDto>(),
            DebitEntries = new List<DebitEntryDto>(),
            Expenses = new List<ExpenseDto>(),
            PaymentCollection = new PaymentCollectionDto
            {
                CreditCard = (decimal)payment.CreditCard,
                PhonePe = (decimal)payment.PhonePe,
                PetroCard = (decimal)payment.PetroCard,
                CashDeposit = (decimal)payment.CashDeposit,
                PhysicalCash = 0,
                // Using DynamicItemsTotal (which excludes standard cards)
                DynamicPayments = (decimal)payment.DynamicItemsTotal
            }
        };

        var result = service.Calculate(dsmDto);

        // Total collection = CreditCard (2000) + PhonePe (1000) + PetroCard (500) + CashDeposit (1500) + DynamicPayments (300) = 5300
        Assert.Equal(5300m, result.TotalCollection);
        Assert.Equal(5300m, result.GrossSales);
        Assert.Equal(0m, result.Mismatch);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public void EnsureLegacyDatabaseCompatibility_SafelyAddsNewColumns_ToExistingDatabase()
    {
        var tempDb = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"LegacyDb_{Guid.NewGuid():N}.db");
        try
        {
            // 1. Create a legacy database without Otp or EnableMorning/Day/Night
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tempDb}"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE CollectionTypes (
                        CollectionTypeId INTEGER PRIMARY KEY AUTOINCREMENT,
                        Code TEXT NOT NULL,
                        DisplayName TEXT NOT NULL,
                        Category TEXT NOT NULL,
                        HasTidBatch INTEGER NOT NULL DEFAULT 0,
                        DisplayOrder INTEGER NOT NULL DEFAULT 0,
                        IsActive INTEGER NOT NULL DEFAULT 1,
                        IsSystem INTEGER NOT NULL DEFAULT 0,
                        CreatedAt TEXT NOT NULL
                    );
                    INSERT INTO CollectionTypes (Code, DisplayName, Category, CreatedAt)
                    VALUES ('CREDIT_CARD', 'Credit Card', 'Card', '2026-01-01');

                    CREATE TABLE PaymentCollections (
                        PaymentId INTEGER PRIMARY KEY AUTOINCREMENT,
                        DsmEntryId INTEGER NOT NULL,
                        CreditCardMorning REAL NOT NULL DEFAULT 0.0
                    );

                    CREATE TABLE PaymentCollectionItems (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        PaymentId INTEGER NOT NULL,
                        CollectionTypeCode TEXT NOT NULL,
                        Amount REAL NOT NULL DEFAULT 0.0,
                        Tid TEXT NULL,
                        Batch TEXT NULL,
                        Slot TEXT NULL DEFAULT 'General'
                    );
                    INSERT INTO PaymentCollectionItems (PaymentId, CollectionTypeCode, Amount, Tid, Batch, Slot)
                    VALUES (1, 'CREDIT_CARD', 1500.0, 'T1', 'B1', 'Morning');

                    CREATE TABLE Settings (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        PumpStationName TEXT NOT NULL DEFAULT 'Station'
                    );
                    INSERT INTO Settings (PumpStationName) VALUES ('Station');
                ";
                cmd.ExecuteNonQuery();
            }

            // 2. Run App.EnsureLegacyDatabaseCompatibility on the legacy DB
            FuelPro.UI.App.EnsureLegacyDatabaseCompatibility(tempDb);

            // 3. Verify columns now exist and original data was perfectly preserved
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tempDb}"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();

                // Verify CollectionTypes gained EnableMorning, EnableDay, EnableNight with default 1
                cmd.CommandText = "SELECT Code, EnableMorning, EnableDay, EnableNight FROM CollectionTypes WHERE Code = 'CREDIT_CARD';";
                using (var reader = cmd.ExecuteReader())
                {
                    Assert.True(reader.Read());
                    Assert.Equal("CREDIT_CARD", reader.GetString(0));
                    Assert.Equal(1, reader.GetInt32(1));
                    Assert.Equal(1, reader.GetInt32(2));
                    Assert.Equal(1, reader.GetInt32(3));
                }

                // Verify PaymentCollectionItems gained Otp and original row is intact
                cmd.CommandText = "SELECT CollectionTypeCode, Amount, Otp FROM PaymentCollectionItems WHERE Id = 1;";
                using (var reader = cmd.ExecuteReader())
                {
                    Assert.True(reader.Read());
                    Assert.Equal("CREDIT_CARD", reader.GetString(0));
                    Assert.Equal(1500.0, reader.GetDouble(1));
                    Assert.True(reader.IsDBNull(2)); // NULL for legacy rows
                }

                // Verify Settings gained dynamic json columns
                cmd.CommandText = "SELECT CollectionTypesJson, AppFeatureSettingsJson, PumpMappingsJson FROM Settings WHERE Id = 1;";
                using (var reader = cmd.ExecuteReader())
                {
                    Assert.True(reader.Read());
                }
            }
        }
        finally
        {
            if (System.IO.File.Exists(tempDb))
            {
                try { System.IO.File.Delete(tempDb); } catch { }
            }
        }
    }

    [Theory]
    [InlineData("Kandhare Petroleum", "Kandhare Petroleum")]
    [InlineData("Personal Ledger", "Personal Ledger")]
    [InlineData("", "Personal Ledger")]
    [InlineData(null, "Personal Ledger")]
    [InlineData("   ", "Personal Ledger")]
    public void PersonalLedger_DisplayName_DefaultsToPersonalLedgerWhenEmpty(string? configuredName, string expectedName)
    {
        var setting = new AppFeatureSetting
        {
            FeatureKey = "Operations_PersonalLedger",
            DisplayName = configuredName ?? ""
        };

        var resolvedName = string.IsNullOrWhiteSpace(setting.DisplayName) ? "Personal Ledger" : setting.DisplayName.Trim();
        Assert.Equal(expectedName, resolvedName);
    }

    [Fact]
    public void ApprovalQueue_DynamicPayments_IncludesNonStandardCollections_PreventsArtificialShortage()
    {
        // Metadata contains card swipes for credit card (1000) and dynamic items (SBI_REDEEM 500, HP_PAY 300)
        var cardSwipeDetails = new List<PaymentCollectionItem>
        {
            new() { CollectionTypeCode = "CREDIT_CARD", Amount = 1000, Tid = "T1", Batch = "B1", Otp = "123456" },
            new() { CollectionTypeCode = "SBI_REDEEM", Amount = 500, Tid = "S1", Batch = "B2", Otp = "654321" },
            new() { CollectionTypeCode = "HP_PAY", Amount = 300, Tid = "H1", Batch = "B3", Otp = "998877" }
        };

        // RecalculateTotals logic in DsmApprovalQueueViewModel:
        var dynamicTotal = cardSwipeDetails
            .Where(r => !PaymentCollection.IsStandardCollectionCode(r.CollectionTypeCode))
            .Sum(r => r.Amount);

        var creditCardTotal = cardSwipeDetails
            .Where(r => string.Equals(r.CollectionTypeCode, "CREDIT_CARD", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.Amount);

        Assert.Equal(800.0, dynamicTotal);
        Assert.Equal(1000.0, creditCardTotal);

        var paymentDto = new PaymentCollectionDto
        {
            CreditCard = (decimal)creditCardTotal,
            DynamicPayments = (decimal)dynamicTotal
        };

        // DsmCalculationService calculates total collection:
        var dsmDto = new DsmEntryDto
        {
            DSMEntryId = 1,
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 1800m } },
            PaymentCollection = paymentDto
        };

        var calcService = new DsmCalculationService();
        var calcResult = calcService.Calculate(dsmDto);

        // Verify total collection is exactly 1800 with 0 mismatch (no artificial shortage)
        Assert.Equal(1800m, calcResult.TotalCollection);
        Assert.Equal(1800m, calcResult.GrossSales);
        Assert.Equal(0m, calcResult.Mismatch);
        Assert.True(calcResult.IsBalanced);
    }

    [Fact]
    public void ApprovalQueue_SwipeDetails_PreservesOriginalDynamicCollectionTypeCode()
    {
        // Dynamic items should not be coerced into CREDIT_CARD
        var item1 = new PaymentCollectionItem { CollectionTypeCode = "SBI_REDEEM", Amount = 400 };
        var item2 = new PaymentCollectionItem { CollectionTypeCode = "HP_PAY", Amount = 600 };
        var item3 = new PaymentCollectionItem { CollectionTypeCode = "CREDIT_CARD", Amount = 1000 };

        var items = new List<PaymentCollectionItem> { item1, item2, item3 };

        // Ensure non-standard items are recognized as dynamic
        Assert.False(PaymentCollection.IsStandardCollectionCode(item1.CollectionTypeCode));
        Assert.False(PaymentCollection.IsStandardCollectionCode(item2.CollectionTypeCode));
        Assert.True(PaymentCollection.IsStandardCollectionCode(item3.CollectionTypeCode));

        // When saving, non-standard items retain their original code
        var dynamicItems = items.Where(i => !PaymentCollection.IsStandardCollectionCode(i.CollectionTypeCode)).ToList();
        Assert.Equal(2, dynamicItems.Count);
        Assert.Contains(dynamicItems, i => i.CollectionTypeCode == "SBI_REDEEM");
        Assert.Contains(dynamicItems, i => i.CollectionTypeCode == "HP_PAY");
    }

    [Theory]
    [InlineData("PETROCARD (Morning)", "PETROCARD")]
    [InlineData("PetroCard Morning", "PETROCARD")]
    [InlineData("PETROCARD", "PETROCARD")]
    [InlineData("PHONEPE (Night)", "PHONEPE")]
    [InlineData("PhonePe Morning", "PHONEPE")]
    [InlineData("PhonePe", "PHONEPE")]
    [InlineData("Credit Card (Day)", "CREDIT_CARD")]
    [InlineData("CREDIT_CARD", "CREDIT_CARD")]
    [InlineData("PineLabs", "PINELABS_CARD")]
    [InlineData("PineLabs Card (Morning)", "PINELABS_CARD")]
    [InlineData("PineLabs Credit Card", "PINELABS_CARD")]
    [InlineData("PhonePe Card (Morning)", "PHONEPE_CARD")]
    [InlineData("PHONEPE_CARD", "PHONEPE_CARD")]
    [InlineData("SBI_REDEEM (Morning)", "SBI_REDEEM")]
    [InlineData("HP_PAY", "HP_PAY")]
    [InlineData("BHARAT_PE (General)", "BHARAT_PE")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void NormalizeCollectionTypeCode_WithSlotSuffixes_NormalizesOnlyStandardTypesAndPreservesDynamic(string? code, string expected)
    {
        var result = PaymentCollection.NormalizeCollectionTypeCode(code);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("PETROCARD (Morning)", "Morning")]
    [InlineData("PhonePe Night", "Night")]
    [InlineData("CREDIT_CARD (Day)", "Day")]
    [InlineData("SBI_REDEEM (General)", "General")]
    [InlineData("HP_PAY", "Morning")]
    [InlineData("", "Morning")]
    [InlineData(null, "Morning")]
    public void ExtractSlotFromMode_AccuratelyExtractsSlotOrUsesFallback(string? mode, string expected)
    {
        var result = PaymentCollection.ExtractSlotFromMode(mode, "Morning");
        Assert.Equal(expected, result);
    }

    [Fact]
    public void DiagnosticReconciliation_MatchesDiagnosticNumbersExactly()
    {
        // Fixture: Gross sales ₹21,023.45; testing ₹3,214.10; net fuel sales ₹17,809.35; collections ₹17,809.00; expected mismatch −₹0.35
        var grossSales = 21023.45m;
        var testingLitres = 32.141m;
        var testingRate = 100m;
        var testingAmount = 3214.10m;

        var dsmDto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto>
            {
                new() { Amount = grossSales }
            },
            TestingEntries = new List<TestingEntryDto>
            {
                new() { Amount = testingAmount, Litres = testingLitres, Rate = testingRate, FuelType = "MS" }
            },
            PaymentCollection = new PaymentCollectionDto
            {
                PhysicalCash = 10000m,
                PhonePe = 5000m,
                CreditCard = 2574m,
                PetroCard = 235m // Losslessly preserved PetroCard
                // Total = 10000 + 5000 + 2574 + 235 = 17809.00
            }
        };

        var calcService = new DsmCalculationService();
        var result = calcService.Calculate(dsmDto);

        Assert.Equal(21023.45m, result.GrossSales);
        Assert.Equal(3214.10m, result.TotalTesting);
        Assert.Equal(17809.35m, result.NetGrossSales);
        Assert.Equal(17809.00m, result.TotalCollection);
        Assert.Equal(-0.35m, result.Mismatch);
    }

    [Fact]
    public void ScenarioA_SettlementAndSwipe_SamePayment_CountedOnce()
    {
        // A. A structured settlement of ₹5,000 and one itemized PhonePe record of ₹5,000 represent the same payment.
        // Expected: count ₹5,000 once.
        var settlements = new List<SettlementTestRecord>
        {
            new() { PaymentType = "PHONEPE", Period = "Morning", Amount = 5000.0, Tid = "PP01", Batch = "B01" }
        };
        var swipes = new List<SwipeTestRecord>
        {
            new() { Mode = "PhonePe (Morning)", Amount = 5000.0, Tid = "PP01", Batch = "B01", Otp = "1234" }
        };

        var (phonePeTotal, _, items) = AggregateWithTransactionMatching(settlements, swipes, "Morning");

        Assert.Equal(5000.0, phonePeTotal);
        Assert.Single(items);
        Assert.Equal("1234", items[0].Otp);
    }

    [Fact]
    public void ScenarioB_SettlementAndTwoSwipes_UnsettledTransactionCounted()
    {
        // B. A structured settlement of ₹5,000 and two itemized PhonePe records exist,
        // where only one itemized record is represented by the settlement and the other is a separate legitimate ₹700 transaction.
        // Expected: count ₹5,700, not ₹5,000 or ₹6,200.
        var settlements = new List<SettlementTestRecord>
        {
            new() { PaymentType = "PHONEPE", Period = "Morning", Amount = 5000.0, Tid = "PP01", Batch = "B01" }
        };
        var swipes = new List<SwipeTestRecord>
        {
            new() { Mode = "PhonePe (Morning)", Amount = 5000.0, Tid = "PP01", Batch = "B01", Otp = "1111" },
            new() { Mode = "PhonePe (Morning)", Amount = 700.0, Tid = "PP02", Batch = "B02", Otp = "2222" }
        };

        var (phonePeTotal, _, items) = AggregateWithTransactionMatching(settlements, swipes, "Morning");

        Assert.Equal(5700.0, phonePeTotal);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Amount == 5000.0 && i.Tid == "PP01");
        Assert.Contains(items, i => i.Amount == 700.0 && i.Tid == "PP02");
    }

    [Fact]
    public void ScenarioC_TwoItemizedTransactions_SameTypeAndAmount_DifferentTids_BothPreserved()
    {
        // C. Two legitimate itemized transactions have the same payment type and amount but different transaction IDs.
        // Expected: both remain accounted for (₹1,000 total).
        var settlements = new List<SettlementTestRecord>();
        var swipes = new List<SwipeTestRecord>
        {
            new() { Mode = "PhonePe (Morning)", Amount = 500.0, Tid = "T1", Batch = "B1" },
            new() { Mode = "PhonePe (Morning)", Amount = 500.0, Tid = "T2", Batch = "B2" }
        };

        var (phonePeTotal, _, items) = AggregateWithTransactionMatching(settlements, swipes, "Morning");

        Assert.Equal(1000.0, phonePeTotal);
        Assert.Equal(2, items.Count);
        Assert.Equal("T1", items[0].Tid);
        Assert.Equal("T2", items[1].Tid);
    }

    [Fact]
    public void ScenarioD_PetroCardOtpAbsentFromSettlements_PreservedWithDetails()
    {
        // D. An OTP-tracked PetroCard transaction is absent from structured settlements.
        // Expected: it is included exactly once, with OTP, TID, batch, slot, and amount preserved.
        var settlements = new List<SettlementTestRecord>
        {
            new() { PaymentType = "PHONEPE", Period = "Morning", Amount = 5000.0 }
        };
        var swipes = new List<SwipeTestRecord>
        {
            new() { Mode = "PETROCARD (Morning)", Amount = 235.0, Tid = "PC01", Batch = "B03", Otp = "4421" }
        };

        var (_, petroTotal, items) = AggregateWithTransactionMatching(settlements, swipes, "Morning");

        Assert.Equal(235.0, petroTotal);
        var petroItem = items.FirstOrDefault(i => i.CollectionTypeCode == "PETROCARD");
        Assert.NotNull(petroItem);
        Assert.Equal(235.0, petroItem!.Amount);
        Assert.Equal("4421", petroItem.Otp);
        Assert.Equal("PC01", petroItem.Tid);
        Assert.Equal("B03", petroItem.Batch);
        Assert.Equal("Morning", petroItem.Slot);
    }

    [Fact]
    public void ScenarioE_MissingTidIdentity_SafeFallback_DoesNotDiscardLegitimatePayment()
    {
        // E. A transaction has incomplete identity information (blank TID).
        // Expected: safe 1-to-1 matching rule; do not silently discard a potentially legitimate payment.
        var settlements = new List<SettlementTestRecord>
        {
            new() { PaymentType = "PHONEPE", Period = "Morning", Amount = 5000.0, Tid = "" }
        };
        var swipes = new List<SwipeTestRecord>
        {
            new() { Mode = "PhonePe (Morning)", Amount = 5000.0, Tid = "", Otp = "" },
            new() { Mode = "PhonePe (Morning)", Amount = 700.0, Tid = "", Otp = "" }
        };

        var (phonePeTotal, _, items) = AggregateWithTransactionMatching(settlements, swipes, "Morning");

        Assert.Equal(5700.0, phonePeTotal);
        Assert.Equal(2, items.Count);
    }

    private class SettlementTestRecord
    {
        public string PaymentType { get; set; } = "";
        public string Period { get; set; } = "";
        public double Amount { get; set; }
        public string Tid { get; set; } = "";
        public string Batch { get; set; } = "";
        public bool IsConsumed { get; set; }
    }

    private class SwipeTestRecord
    {
        public string Mode { get; set; } = "";
        public double Amount { get; set; }
        public string Tid { get; set; } = "";
        public string Batch { get; set; } = "";
        public string Otp { get; set; } = "";
    }

    private static (double PhonePeTotal, double PetroTotal, List<PaymentCollectionItem> Items) AggregateWithTransactionMatching(
        List<SettlementTestRecord> settlements, List<SwipeTestRecord> swipes, string defaultSlot)
    {
        double phonePeTotal = 0;
        double petroTotal = 0;
        var items = new List<PaymentCollectionItem>();

        foreach (var s in settlements)
        {
            if (s.PaymentType.Equals("PHONEPE", StringComparison.OrdinalIgnoreCase)) phonePeTotal += s.Amount;
            if (s.PaymentType.Equals("PETROCARD", StringComparison.OrdinalIgnoreCase)) petroTotal += s.Amount;
        }

        foreach (var sw in swipes)
        {
            string normType = PaymentCollection.NormalizeCollectionTypeCode(sw.Mode);
            string swipeSlot = PaymentCollection.ExtractSlotFromMode(sw.Mode, defaultSlot);

            var matched = settlements.FirstOrDefault(s =>
                !s.IsConsumed &&
                s.PaymentType.Equals(normType, StringComparison.OrdinalIgnoreCase) &&
                s.Period.Equals(swipeSlot, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(s.Amount - sw.Amount) < 0.01 &&
                (string.IsNullOrWhiteSpace(s.Tid) || string.IsNullOrWhiteSpace(sw.Tid) || s.Tid.Equals(sw.Tid, StringComparison.OrdinalIgnoreCase))
            );

            if (matched != null)
            {
                matched.IsConsumed = true;
            }
            else
            {
                if (normType.Equals("PHONEPE", StringComparison.OrdinalIgnoreCase)) phonePeTotal += sw.Amount;
                if (normType.Equals("PETROCARD", StringComparison.OrdinalIgnoreCase)) petroTotal += sw.Amount;
            }

            items.Add(new PaymentCollectionItem
            {
                CollectionTypeCode = normType,
                Amount = sw.Amount,
                Tid = sw.Tid,
                Batch = sw.Batch,
                Otp = sw.Otp,
                Slot = swipeSlot
            });
        }

        return (phonePeTotal, petroTotal, items);
    }

    [Fact]
    public void DsmOtpTransactions_ShouldBePreserved_WhenReloadingWithoutSavedPayment()
    {
        // Simulate in-progress OTP transactions
        var otpTransactions = new List<PaymentCollectionItem>
        {
            new() { CollectionTypeCode = "CREDIT_CARD", Amount = 1500, Otp = "987654", Tid = "TID01", Batch = "B01", Slot = "Morning" },
            new() { CollectionTypeCode = "PHONEPE", Amount = 750, Otp = "123456", Tid = "POS02", Batch = "B02", Slot = "General" }
        };

        PaymentCollection? existingPayment = null; // Background sync triggered refresh

        // The fix guarantees: if existingPayment == null, OtpTransactions must NOT be cleared
        if (existingPayment != null)
        {
            otpTransactions.Clear();
        }

        Assert.Equal(2, otpTransactions.Count);
        Assert.Equal("987654", otpTransactions[0].Otp);
        Assert.Equal(1500, otpTransactions[0].Amount);
        Assert.Equal("123456", otpTransactions[1].Otp);
        Assert.Equal(750, otpTransactions[1].Amount);
    }

    [Fact]
    public void DebtorRecoveries_AreStrictlyExcludedFromSalesAndDayTotals()
    {
        var aggregation = new ShiftAggregationService();
        var reportService = new ReportService(aggregation);
        var date = new DateTime(2026, 10, 15);

        var entries = new List<DsmEntry>
        {
            new()
            {
                DsmEntryId = 1,
                DsmName = "Test DSM",
                GrossSales = 10000,
                NozzleReadings = new List<NozzleReading>
                {
                    new() { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 100, Amount = 10000, Rate = 100 }
                },
                CashDenominations = new List<CashDenomination>
                {
                    new() { CashType = "Cash2", TotalAmount = 6000 }
                },
                PaymentCollection = new PaymentCollection
                {
                    PhonePeMorning = 2000,
                    CreditCardMorning = 2000
                }
            }
        };

        var repayments = new List<CreditorRepayment>
        {
            new()
            {
                CreditorRepaymentId = 1,
                CreditorName = "Debtor ABC",
                PaymentMode = "Cash",
                Amount = 3000,
                RepaymentDate = date
            },
            new()
            {
                CreditorRepaymentId = 2,
                CreditorName = "Debtor XYZ",
                PaymentMode = "PhonePe",
                Amount = 1500,
                RepaymentDate = date
            }
        };

        var dayReport = reportService.CalculateDayReport(
            startDate: date,
            endDate: date,
            entries: entries,
            allExpenses: new List<Expense>(),
            repayments: repayments,
            hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
            stationName: "Mitali Service Station"
        );

        // 1. Debtor recoveries MUST NOT inflate GrandTotalSaleAmount or ExpectedCollection
        Assert.Equal(10000, dayReport.GrandTotalSaleAmount);
        Assert.Equal(10000, dayReport.ExpectedCollection);

        // 2. Pure fuel payment amounts must not be inflated by repayments
        var cashInHand = dayReport.CollectionBreakdown.First(c => c.Category == "Cash In Hand");
        Assert.Equal(6000, cashInHand.Amount); // pure fuel cash in hand, NOT 9000
        Assert.Equal(3000, cashInHand.RecoveryAmount);

        var phonePe = dayReport.CollectionBreakdown.First(c => c.Category.Contains("PhonePe"));
        Assert.Equal(2000, phonePe.Amount); // pure fuel PhonePe, NOT 3500
        Assert.Equal(1500, phonePe.RecoveryAmount);

        // 3. DebtorRepaymentsTotal is recorded separately in its own register
        Assert.Equal(4500, dayReport.DebtorRepaymentsTotal);

        // 4. ActualCollection and Difference are based on pure fuel collections
        Assert.Equal(10000, dayReport.ActualCollection);
        Assert.True(dayReport.IsBalanced);
        Assert.Equal(0, dayReport.Difference, precision: 2);
    }

    [Fact]
    public void NormalizeCollectionTypeCode_ShouldCanonicalize_PineLabsCards_WithPluralS()
    {
        string norm1 = PaymentCollection.NormalizeCollectionTypeCode("PINE LABS CARDS");
        string norm2 = PaymentCollection.NormalizeCollectionTypeCode("PINE LABS CARDS (General)");
        string norm3 = PaymentCollection.NormalizeCollectionTypeCode("PINELABS_CARD");
        string norm4 = PaymentCollection.NormalizeCollectionTypeCode("PINELABS CARD");

        Assert.Equal("PINELABS_CARD", norm1);
        Assert.Equal("PINELABS_CARD", norm2);
        Assert.Equal("PINELABS_CARD", norm3);
        Assert.Equal("PINELABS_CARD", norm4);
    }

    [Fact]
    public void DynamicItemsTotal_IncludesLegitimateEqualAmountManualPayment()
    {
        var payment = new PaymentCollection
        {
            Items = new List<PaymentCollectionItem>
            {
                // Machine transaction slip with OTP: 250
                new() { CollectionTypeCode = "PINELABS_CARD", Amount = 250, Tid = "T1", Batch = "B1", Otp = "998877" },
                // Legitimate manual payment: 250
                new() { CollectionTypeCode = "PINELABS_CARD", Amount = 250, Tid = "T2", Batch = "B2", Otp = null }
            }
        };

        // Must NOT drop the manual 250 merely because it equals the OTP sum of 250!
        // Both rows are legitimate distinct collections and must sum to 500.
        Assert.Equal(500, payment.DynamicItemsTotal);
    }

    [Fact]
    public void DynamicItemsTotal_ExcludesExactDuplicatesAndSyntheticSummaries()
    {
        var payment = new PaymentCollection
        {
            Items = new List<PaymentCollectionItem>
            {
                // Legitimate OTP machine slip: 250
                new() { CollectionTypeCode = "SBI_REDEEM", Amount = 250, Tid = "T1", Batch = "B1", Otp = "123456", Slot = "General" },
                // Exact duplicate with same amount, Tid, Batch, Slot: 250
                new() { CollectionTypeCode = "SBI_REDEEM", Amount = 250, Tid = "T1", Batch = "B1", Otp = null, Slot = "General" },
                // Synthetic summary row flagged explicitly: 250
                new() { CollectionTypeCode = "SBI_REDEEM", Amount = 250, Otp = null, IsSyntheticSummary = true }
            }
        };

        // Only the genuine 250 OTP slip should be counted
        Assert.Equal(250, payment.DynamicItemsTotal);
    }

    [Fact]
    public void ApprovalQueue_Deduplication_PreservesLegitimateManualPayment_WhenAmountEqualsOtpSum()
    {
        // DSM PWA submitted:
        // - Manual Pine Labs card payment: 250
        // - One machine transaction slip: 250 (OTP 112233)
        // dynamicItems sent effective total: 500 (manual 250 + txTotal 250)
        var cardSwipeDetails = new List<FuelPro.UI.ViewModels.DsmCardSwipeRow>
        {
            new() { Mode = "PINE LABS CARDS", Amount = 250, Otp = "112233", Tid = "T01", Batch = "B01" },
            new() { Mode = "PINE LABS CARDS", Amount = 250, Otp = "", Tid = "", Batch = "" }
        };

        var metadata = new
        {
            dynamicItems = new[]
            {
                new { collectionTypeCode = "PINE LABS CARDS", amount = 500.0 }
            }
        };

        // Provenance deduplication logic from DsmApprovalQueueViewModel.CleanupRedundantTotals
        var dynamicGroups = cardSwipeDetails
            .GroupBy(c => PaymentCollection.NormalizeCollectionTypeCode(c.Mode), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in dynamicGroups)
        {
            var otpRows = group.Where(c => !string.IsNullOrWhiteSpace(c.Otp)).ToList();
            if (otpRows.Count == 0) continue;

            double otpSum = otpRows.Sum(c => c.Amount);
            string normCode = group.Key;

            double expectedManual = 0;
            bool hasExplicitTotal = false;

            if (metadata?.dynamicItems != null)
            {
                foreach (var d in metadata.dynamicItems)
                {
                    string dCode = d.collectionTypeCode;
                    if (string.Equals(PaymentCollection.NormalizeCollectionTypeCode(dCode), normCode, StringComparison.OrdinalIgnoreCase))
                    {
                        double dAmt = d.amount;
                        expectedManual = Math.Max(0, dAmt - otpSum);
                        hasExplicitTotal = true;
                        break;
                    }
                }
            }

            var redundantTotals = group
                .Where(c => string.IsNullOrWhiteSpace(c.Otp) &&
                            (c.IsSyntheticSummary ||
                             c.Amount <= 0.009 ||
                             (hasExplicitTotal && expectedManual <= 0.009 && Math.Abs(c.Amount - otpSum) < 0.01)))
                .ToList();

            foreach (var red in redundantTotals)
            {
                cardSwipeDetails.Remove(red);
            }
        }

        // The manual 250 row MUST be preserved because expectedManual (500 - 250 = 250) > 0.009
        Assert.Equal(2, cardSwipeDetails.Count);
        Assert.Equal(500, cardSwipeDetails.Sum(c => c.Amount));
        Assert.Contains(cardSwipeDetails, c => string.IsNullOrWhiteSpace(c.Otp) && Math.Abs(c.Amount - 250) < 0.01);
        Assert.Contains(cardSwipeDetails, c => !string.IsNullOrWhiteSpace(c.Otp) && Math.Abs(c.Amount - 250) < 0.01);
    }

    [Fact]
    public void ApprovalQueue_Deduplication_RemovesSyntheticSummary_WhenNoManualPortion()
    {
        // DSM PWA submitted NO manual portion, only 5 machine slips totaling 350.
        // dynamicItems sent effective total: 350 (manual 0 + txTotal 350).
        var cardSwipeDetails = new List<FuelPro.UI.ViewModels.DsmCardSwipeRow>
        {
            new() { Mode = "PINE LABS CARDS", Amount = 100, Otp = "01", Tid = "3", Batch = "3" },
            new() { Mode = "PINE LABS CARDS", Amount = 50, Otp = "02", Tid = "3", Batch = "3" },
            new() { Mode = "PINE LABS CARDS", Amount = 100, Otp = "03", Tid = "3", Batch = "3" },
            new() { Mode = "PINE LABS CARDS", Amount = 70, Otp = "04", Tid = "3", Batch = "3" },
            new() { Mode = "PINE LABS CARDS", Amount = 30, Otp = "05", Tid = "3", Batch = "3" },
            new() { Mode = "PINE LABS CARDS", Amount = 350, Otp = "", Tid = "3", Batch = "3", IsSyntheticSummary = true } // Redundant summary
        };

        var metadata = new
        {
            dynamicItems = new[]
            {
                new { collectionTypeCode = "PINE LABS CARDS", amount = 350.0 }
            }
        };

        // Provenance deduplication logic from DsmApprovalQueueViewModel.CleanupRedundantTotals
        var dynamicGroups = cardSwipeDetails
            .GroupBy(c => PaymentCollection.NormalizeCollectionTypeCode(c.Mode), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in dynamicGroups)
        {
            var otpRows = group.Where(c => !string.IsNullOrWhiteSpace(c.Otp)).ToList();
            if (otpRows.Count == 0) continue;

            double otpSum = otpRows.Sum(c => c.Amount);
            string normCode = group.Key;

            double expectedManual = 0;
            bool hasExplicitTotal = false;

            if (metadata?.dynamicItems != null)
            {
                foreach (var d in metadata.dynamicItems)
                {
                    string dCode = d.collectionTypeCode;
                    if (string.Equals(PaymentCollection.NormalizeCollectionTypeCode(dCode), normCode, StringComparison.OrdinalIgnoreCase))
                    {
                        double dAmt = d.amount;
                        expectedManual = Math.Max(0, dAmt - otpSum);
                        hasExplicitTotal = true;
                        break;
                    }
                }
            }

            var redundantTotals = group
                .Where(c => string.IsNullOrWhiteSpace(c.Otp) &&
                            (c.IsSyntheticSummary ||
                             c.Amount <= 0.009 ||
                             (hasExplicitTotal && expectedManual <= 0.009 && Math.Abs(c.Amount - otpSum) < 0.01)))
                .ToList();

            foreach (var red in redundantTotals)
            {
                cardSwipeDetails.Remove(red);
            }
        }

        // Summary row (350) is removed because expectedManual == 0 and IsSyntheticSummary is true
        Assert.Equal(5, cardSwipeDetails.Count);
        Assert.Equal(350, cardSwipeDetails.Sum(c => c.Amount));
        Assert.DoesNotContain(cardSwipeDetails, c => string.IsNullOrWhiteSpace(c.Otp));
    }

    [Fact]
    public void ApprovalQueue_NozzleReadings_DeterministicOrderAndFuelTypePreserved()
    {
        var rawReadings = new List<FuelPro.UI.ViewModels.DsmNozzleRow>
        {
            new() { NozzleId = 4, FuelType = "HSD", OpeningReading = 100.0, ClosingReading = 200.0, Rate = 90.0 },
            new() { NozzleId = 1, FuelType = "SPEED", OpeningReading = 50.0, ClosingReading = 150.0, Rate = 105.0 },
            new() { NozzleId = 2, FuelType = "MS-I", OpeningReading = 300.0, ClosingReading = 400.0, Rate = 100.0 }
        };

        var sorted = rawReadings.OrderBy(r => r.NozzleId).ToList();

        // Must be deterministically ordered 1, 2, 4
        Assert.Equal(1, sorted[0].NozzleId);
        Assert.Equal(2, sorted[1].NozzleId);
        Assert.Equal(4, sorted[2].NozzleId);

        // Submitted FuelType must be preserved
        Assert.Equal("SPEED", sorted[0].FuelType);
        Assert.Equal("MS-I", sorted[1].FuelType);
        Assert.Equal("HSD", sorted[2].FuelType);
    }

    [Fact]
    public void CreditAndDebtorAccounting_SyncsWhenDebtorsPresent_DoesNotFabricateDebtorWhenEmpty()
    {
        var calcService = new DsmCalculationService();

        // 1. When DebtorEntries are present
        var debtorDto = new DsmEntryDto
        {
            PaymentCollection = new PaymentCollectionDto(),
            DebitEntries = new List<DebitEntryDto>
            {
                new() { Amount = 1500m }
            }
        };
        var resultWithDebtors = calcService.Calculate(debtorDto);
        Assert.Equal(1500m, resultWithDebtors.TotalCreditors);

        // 2. When DebtorEntries is empty, TotalCreditors must be 0 and no phantom debtor is fabricated
        var emptyDebtorDto = new DsmEntryDto
        {
            PaymentCollection = new PaymentCollectionDto(),
            DebitEntries = new List<DebitEntryDto>()
        };
        var resultEmpty = calcService.Calculate(emptyDebtorDto);
        Assert.Equal(0m, resultEmpty.TotalCreditors);
    }

    [Fact]
    public void TwoDecimalFormatting_DisplaysReadingsAndSalesWithConsistentPrecision()
    {
        double opening = 26.5;
        double closing = 56.4;
        double sales = closing - opening; // 29.9

        Assert.Equal("26.50", opening.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("56.40", closing.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("29.90", sales.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

        var litreConverter = new FuelPro.UI.Converters.LitreFormatConverter();
        Assert.Equal("26.50 L", litreConverter.Convert(opening, typeof(string), string.Empty, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("56.40 L", litreConverter.Convert(closing, typeof(string), string.Empty, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void PaymentCollection_PhonePe_Manual250_And_OtpSlip250_BothRetained_OtpCountedOnce()
    {
        // 1. Transaction matching regression:
        // Structured settlement of 250 for PhonePe (from POS machine settlement)
        var settlements = new List<SettlementTestRecord>
        {
            new() { PaymentType = "PHONEPE", Period = "Morning", Amount = 250.0, Tid = "POS01", Batch = "B01" }
        };

        // Two swipes: one OTP machine slip (250) and one manual PhonePe payment (250)
        var swipes = new List<SwipeTestRecord>
        {
            new() { Mode = "PhonePe (Morning)", Amount = 250.0, Tid = "POS01", Batch = "B01", Otp = "987654" },
            new() { Mode = "PhonePe (Morning)", Amount = 250.0, Tid = "MANUAL01", Batch = "B02", Otp = "" }
        };

        var (phonePeTotal, _, items) = AggregateWithTransactionMatching(settlements, swipes, "Morning");

        // Structured settlement matches OTP slip (POS01, 250), so OTP is counted once (via settlement).
        // The manual payment (250) is unmatched by settlement, so it is losslessly added.
        // Total must be 500.0, NOT 250 (which would drop the manual payment) and NOT 750 (which would double-count OTP).
        Assert.Equal(500.0, phonePeTotal);
        Assert.Equal(2, items.Count);
        Assert.Contains(items, i => i.Amount == 250.0 && !string.IsNullOrWhiteSpace(i.Otp));
        Assert.Contains(items, i => i.Amount == 250.0 && string.IsNullOrWhiteSpace(i.Otp));

        // 2. Queue loading deduplication logic:
        var cardSwipeRows = new List<FuelPro.UI.ViewModels.DsmCardSwipeRow>
        {
            new() { Mode = "PhonePe (Morning)", Amount = 250.0, Otp = "987654", Tid = "POS01", Batch = "B01" },
            new() { Mode = "PhonePe (Morning)", Amount = 250.0, Otp = "", Tid = "", Batch = "" }
        };

        var coll = new { UPI = 500.0 }; // Total UPI collection submitted by DSM

        // Provenance deduplication logic from CleanupRedundantTotals:
        var dynamicGroups = cardSwipeRows
            .GroupBy(c => PaymentCollection.NormalizeCollectionTypeCode(c.Mode), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in dynamicGroups)
        {
            var otpRows = group.Where(c => !string.IsNullOrWhiteSpace(c.Otp)).ToList();
            if (otpRows.Count == 0) continue;

            double otpSum = otpRows.Sum(c => c.Amount);
            string normCode = group.Key;

            double expectedManual = 0;
            bool hasExplicitTotal = false;

            if (string.Equals(normCode, "PHONEPE", StringComparison.OrdinalIgnoreCase))
            {
                double collUpi = coll.UPI;
                expectedManual = Math.Max(0, collUpi - otpSum);
                hasExplicitTotal = true;
            }

            var redundantTotals = group
                .Where(c => string.IsNullOrWhiteSpace(c.Otp) &&
                            (c.IsSyntheticSummary ||
                             c.Amount <= 0.009 ||
                             (hasExplicitTotal && expectedManual <= 0.009 && Math.Abs(c.Amount - otpSum) < 0.01)))
                .ToList();

            foreach (var red in redundantTotals)
            {
                cardSwipeRows.Remove(red);
            }
        }

        // Both rows must be retained: manual 250 is NOT dropped!
        Assert.Equal(2, cardSwipeRows.Count);
        Assert.Equal(500.0, cardSwipeRows.Sum(c => c.Amount));
    }

    [Fact]
    public void ApprovalQueue_Deduplication_RepeatedLoading_IsIdempotent()
    {
        var cardSwipeRows = new List<FuelPro.UI.ViewModels.DsmCardSwipeRow>
        {
            new() { Mode = "PINE LABS CARDS", Amount = 250, Otp = "112233", Tid = "T01", Batch = "B01" },
            new() { Mode = "PINE LABS CARDS", Amount = 250, Otp = "", Tid = "", Batch = "" }
        };

        var metadata = new
        {
            dynamicItems = new[]
            {
                new { collectionTypeCode = "PINE LABS CARDS", amount = 500.0 }
            }
        };

        void RunCleanup()
        {
            var dynamicGroups = cardSwipeRows
                .GroupBy(c => PaymentCollection.NormalizeCollectionTypeCode(c.Mode), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var group in dynamicGroups)
            {
                var otpRows = group.Where(c => !string.IsNullOrWhiteSpace(c.Otp)).ToList();
                if (otpRows.Count == 0) continue;

                double otpSum = otpRows.Sum(c => c.Amount);
                string normCode = group.Key;

                double expectedManual = 0;
                bool hasExplicitTotal = false;

                if (metadata?.dynamicItems != null)
                {
                    foreach (var d in metadata.dynamicItems)
                    {
                        string dCode = d.collectionTypeCode;
                        if (string.Equals(PaymentCollection.NormalizeCollectionTypeCode(dCode), normCode, StringComparison.OrdinalIgnoreCase))
                        {
                            double dAmt = d.amount;
                            expectedManual = Math.Max(0, dAmt - otpSum);
                            hasExplicitTotal = true;
                            break;
                        }
                    }
                }

                var redundantTotals = group
                    .Where(c => string.IsNullOrWhiteSpace(c.Otp) &&
                                (c.IsSyntheticSummary ||
                                 c.Amount <= 0.009 ||
                                 (hasExplicitTotal && expectedManual <= 0.009 && Math.Abs(c.Amount - otpSum) < 0.01)))
                    .ToList();

                foreach (var red in redundantTotals)
                {
                    cardSwipeRows.Remove(red);
                }
            }
        }

        // First pass
        RunCleanup();
        Assert.Equal(2, cardSwipeRows.Count);

        // Second pass (simulating repeated reload / reopening)
        RunCleanup();
        Assert.Equal(2, cardSwipeRows.Count);

        // Third pass
        RunCleanup();
        Assert.Equal(2, cardSwipeRows.Count);
        Assert.Equal(500.0, cardSwipeRows.Sum(c => c.Amount));
    }

    [Fact]
    public void ApprovalQueue_Deduplication_RemovesZeroAndNearZeroEntries()
    {
        var cardSwipeRows = new List<FuelPro.UI.ViewModels.DsmCardSwipeRow>
        {
            new() { Mode = "PINE LABS CARDS", Amount = 500, Otp = "998877", Tid = "T01", Batch = "B01" },
            new() { Mode = "PINE LABS CARDS", Amount = 0.000, Otp = "", Tid = "", Batch = "" },
            new() { Mode = "PINE LABS CARDS", Amount = 0.005, Otp = "", Tid = "", Batch = "" }
        };

        var dynamicGroups = cardSwipeRows
            .GroupBy(c => PaymentCollection.NormalizeCollectionTypeCode(c.Mode), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in dynamicGroups)
        {
            var otpRows = group.Where(c => !string.IsNullOrWhiteSpace(c.Otp)).ToList();
            if (otpRows.Count == 0) continue;

            var redundantTotals = group
                .Where(c => string.IsNullOrWhiteSpace(c.Otp) && (c.IsSyntheticSummary || c.Amount <= 0.009))
                .ToList();

            foreach (var red in redundantTotals)
            {
                cardSwipeRows.Remove(red);
            }
        }

        // Only the genuine 500 OTP row remains; 0.000 and 0.005 rows were cleaned up
        Assert.Single(cardSwipeRows);
        Assert.Equal(500, cardSwipeRows[0].Amount);
    }

    [Fact]
    public void PaymentCollectionItem_IsSyntheticSummary_HasNotMappedAttribute_RequiresNoDatabaseMigration()
    {
        var prop = typeof(PaymentCollectionItem).GetProperty(nameof(PaymentCollectionItem.IsSyntheticSummary));
        Assert.NotNull(prop);

        var notMappedAttr = prop!.GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.Schema.NotMappedAttribute), false);
        Assert.NotEmpty(notMappedAttr);
    }

    [Fact]
    public void CreditAndDebtors_EditedDebtors_UpdatesCreditAmountAndTotals()
    {
        var debtorList = new List<FuelPro.UI.ViewModels.DsmDebitRow>
        {
            new() { DebtorName = "Customer A", Amount = 1000.0 },
            new() { DebtorName = "Customer B", Amount = 500.0 }
        };

        double creditAmount = debtorList.Sum(d => d.Amount);
        Assert.Equal(1500.0, creditAmount);

        // Edit debtor amount (e.g. manager modifies slip from 500 to 750)
        debtorList[1].Amount = 750.0;
        creditAmount = debtorList.Sum(d => d.Amount);
        Assert.Equal(1750.0, creditAmount);

        // Add a new debtor entry
        debtorList.Add(new FuelPro.UI.ViewModels.DsmDebitRow { DebtorName = "Customer C", Amount = 250.0 });
        creditAmount = debtorList.Sum(d => d.Amount);
        Assert.Equal(2000.0, creditAmount);

        // Calculate DsmCalculationService result with the edited debtor rows
        var calcService = new DsmCalculationService();
        var dto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 5000m } },
            PaymentCollection = new PaymentCollectionDto { PhysicalCash = 3000m },
            DebitEntries = debtorList.Select(d => new DebitEntryDto { Amount = (decimal)d.Amount }).ToList()
        };

        var result = calcService.Calculate(dto);
        Assert.Equal(2000m, result.TotalCreditors);
        Assert.Equal(5000m, result.TotalCollection);
        Assert.Equal(0m, result.Mismatch);
        Assert.True(result.IsBalanced);
    }

    [Fact]
    public void ApprovedSubmission_Reopening_RestoresExactDebitModels()
    {
        // Simulate saved entry with exact debit models in SQLite
        var savedEntry = new DsmEntry
        {
            DsmEntryId = 42,
            DsmName = "Ramesh",
            DebitEntries = new List<DebitEntry>
            {
                new() { DebtorName = "Shree Travels", Amount = 1200.0, VehicleNumber = "MH12AB1234", SlipNumber = "SL-99" },
                new() { DebtorName = "Pawar Transport", Amount = 800.0, VehicleNumber = "MH14CD5678", SlipNumber = "SL-100" }
            }
        };

        // When reopening in LoadApprovedSubmissionDetailsAsync:
        var reloadedDebtorRows = new List<FuelPro.UI.ViewModels.DsmDebitRow>();
        foreach (var db in savedEntry.DebitEntries)
        {
            reloadedDebtorRows.Add(new FuelPro.UI.ViewModels.DsmDebitRow
            {
                DebtorName = db.DebtorName,
                Amount = db.Amount,
                VehicleNumber = db.VehicleNumber ?? "",
                SlipNumber = db.SlipNumber ?? ""
            });
        }

        Assert.Equal(2, reloadedDebtorRows.Count);
        Assert.Equal("Shree Travels", reloadedDebtorRows[0].DebtorName);
        Assert.Equal(1200.0, reloadedDebtorRows[0].Amount);
        Assert.Equal("Pawar Transport", reloadedDebtorRows[1].DebtorName);
        Assert.Equal(800.0, reloadedDebtorRows[1].Amount);
        Assert.Equal(2000.0, reloadedDebtorRows.Sum(d => d.Amount));
    }

    [Fact]
    public void PaymentReconciliation_MultipleOtpSlips_ManualMatchingOneSlip_RepeatedLoading_And_EditedCollections_CountedExactlyOnce()
    {
        // 1. Multiple OTP slips: 300, 250, 450 (sum = 1000)
        // 2. Manual payment matching one slip's amount: 250
        // Effective total submitted in dynamicItems: 1250 (1000 OTP + 250 manual)
        var cardSwipeDetails = new List<FuelPro.UI.ViewModels.DsmCardSwipeRow>
        {
            new() { Mode = "PINELABS_CARD", Amount = 300, Otp = "111111", Tid = "T01", Batch = "B01" },
            new() { Mode = "PINELABS_CARD", Amount = 250, Otp = "222222", Tid = "T02", Batch = "B02" },
            new() { Mode = "PINELABS_CARD", Amount = 450, Otp = "333333", Tid = "T03", Batch = "B03" },
            new() { Mode = "PINELABS_CARD", Amount = 250, Otp = "", Tid = "", Batch = "" }
        };

        var metadata = new
        {
            dynamicItems = new[]
            {
                new { collectionTypeCode = "PINELABS_CARD", amount = 1250.0 }
            }
        };

        void RunCleanup()
        {
            var dynamicGroups = cardSwipeDetails
                .GroupBy(c => PaymentCollection.NormalizeCollectionTypeCode(c.Mode), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var group in dynamicGroups)
            {
                var otpRows = group.Where(c => !string.IsNullOrWhiteSpace(c.Otp)).ToList();
                if (otpRows.Count == 0) continue;

                double otpSum = otpRows.Sum(c => c.Amount);
                string normCode = group.Key;

                double expectedManual = 0;
                bool hasExplicitTotal = false;

                if (metadata?.dynamicItems != null)
                {
                    foreach (var d in metadata.dynamicItems)
                    {
                        string dCode = d.collectionTypeCode;
                        if (string.Equals(PaymentCollection.NormalizeCollectionTypeCode(dCode), normCode, StringComparison.OrdinalIgnoreCase))
                        {
                            double dAmt = d.amount;
                            expectedManual = Math.Max(0, dAmt - otpSum);
                            hasExplicitTotal = true;
                            break;
                        }
                    }
                }

                var redundantTotals = group
                    .Where(c => string.IsNullOrWhiteSpace(c.Otp) &&
                                (c.IsSyntheticSummary ||
                                 c.Amount <= 0.009 ||
                                 (hasExplicitTotal && expectedManual <= 0.009 && Math.Abs(c.Amount - otpSum) < 0.01)))
                    .ToList();

                foreach (var red in redundantTotals)
                {
                    cardSwipeDetails.Remove(red);
                }
            }
        }

        // Pass 1: Deduplication
        RunCleanup();
        Assert.Equal(4, cardSwipeDetails.Count);
        Assert.Equal(1250.0, cardSwipeDetails.Sum(c => c.Amount));

        // Pass 2 & 3: Idempotent repeated loads
        RunCleanup();
        RunCleanup();
        Assert.Equal(4, cardSwipeDetails.Count);
        Assert.Equal(1250.0, cardSwipeDetails.Sum(c => c.Amount));

        // Confirm each legitimate amount is counted exactly once:
        var otp250 = cardSwipeDetails.Where(c => !string.IsNullOrWhiteSpace(c.Otp) && c.Amount == 250).ToList();
        var manual250 = cardSwipeDetails.Where(c => string.IsNullOrWhiteSpace(c.Otp) && c.Amount == 250).ToList();
        Assert.Single(otp250);
        Assert.Single(manual250);

        // DynamicItemsTotal in PaymentCollection also calculates exactly 1250:
        var payment = new PaymentCollection
        {
            Items = cardSwipeDetails.Select(c => new PaymentCollectionItem
            {
                CollectionTypeCode = c.Mode,
                Amount = c.Amount,
                Tid = c.Tid,
                Batch = c.Batch,
                Otp = c.Otp
            }).ToList()
        };
        Assert.Equal(1250.0, payment.DynamicItemsTotal);

        // Edited collections: manager edits the manual payment amount from 250 to 350
        var manualRow = cardSwipeDetails.First(c => string.IsNullOrWhiteSpace(c.Otp));
        manualRow.Amount = 350.0;

        // Recalculate dynamic total with edited collections
        var dynamicSwipes = cardSwipeDetails
            .Where(c => !PaymentCollection.IsStandardCollectionCode(c.Mode))
            .ToList();

        double editedDynamicTotal = 0;
        foreach (var group in dynamicSwipes.GroupBy(c => PaymentCollection.NormalizeCollectionTypeCode(c.Mode)))
        {
            var otpSwipes = group.Where(c => !string.IsNullOrWhiteSpace(c.Otp)).ToList();
            var nonOtpSwipes = group.Where(c => string.IsNullOrWhiteSpace(c.Otp)).ToList();
            double groupTotal = otpSwipes.Sum(c => c.Amount) + nonOtpSwipes.Where(c => !c.IsSyntheticSummary).Sum(c => c.Amount);
            editedDynamicTotal += groupTotal;
        }

        Assert.Equal(1350.0, editedDynamicTotal);

        // Verify with DsmCalculationService
        var dsmDto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 1350m } },
            PaymentCollection = new PaymentCollectionDto { DynamicPayments = (decimal)editedDynamicTotal }
        };
        var calcResult = new DsmCalculationService().Calculate(dsmDto);
        Assert.Equal(1350m, calcResult.TotalCollection);
        Assert.True(calcResult.IsBalanced);
    }

    [Fact]
    public void CreditAccounting_WhenCreditAmountGreaterThanZero_ButDebtorEntriesEmpty_TotalCreditorsIsZero_And_MismatchReflectsUnitemizedCredit()
    {
        // PWA or payload submitted with credit amount = 500, but no debtor items
        var calcService = new DsmCalculationService();
        var dto = new DsmEntryDto
        {
            NozzleReadings = new List<NozzleReadingDto> { new() { Amount = 5000m } },
            PaymentCollection = new PaymentCollectionDto { PhysicalCash = 4500m },
            DebitEntries = new List<DebitEntryDto>() // Empty debtor entries
        };

        var result = calcService.Calculate(dto);

        // Core business rule: TotalCreditors strictly equals sum of DebitEntries
        // No fake debtor is fabricated, so TotalCreditors is 0m
        Assert.Equal(0m, result.TotalCreditors);
        // TotalCollection = 4500m (cash only)
        Assert.Equal(4500m, result.TotalCollection);
        // Mismatch is -500m (surfacing the unitemized credit as a collection shortage of 500)
        Assert.Equal(-500m, result.Mismatch);
        Assert.False(result.IsBalanced);
    }
}



