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
        Assert.Contains("OTP", otpTracking.DisplayName);
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
}
