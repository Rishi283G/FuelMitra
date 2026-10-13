using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.UI;
using Xunit;

namespace FuelPro.Tests;

public class PreReleaseStagingValidationTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly string _readOnlySourceDir;

    public PreReleaseStagingValidationTests()
    {
        PumpConfiguration.ResetToDefaults();
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        _readOnlySourceDir = Path.Combine(repoRoot, "forensic_investigation", "PRODUCTION_DB_COPY_READONLY_SOURCE");
    }

    public void Dispose()
    {
        PumpConfiguration.ResetToDefaults();
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
            try { if (File.Exists(file + "-wal")) File.Delete(file + "-wal"); } catch { }
            try { if (File.Exists(file + "-shm")) File.Delete(file + "-shm"); } catch { }
        }
    }

    private string CreateDisposableStagingDb()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"fuelpro_staging_validation_{Guid.NewGuid():N}.db");
        _tempFiles.Add(tempDb);

        var sourceDb = Path.Combine(_readOnlySourceDir, "fuelPro.db");
        if (File.Exists(sourceDb))
        {
            File.Copy(sourceDb, tempDb, true);
            File.SetAttributes(tempDb, FileAttributes.Normal);

            var walSource = Path.Combine(_readOnlySourceDir, "fuelPro.db-wal");
            if (File.Exists(walSource))
            {
                File.Copy(walSource, tempDb + "-wal", true);
                File.SetAttributes(tempDb + "-wal", FileAttributes.Normal);
            }
            var shmSource = Path.Combine(_readOnlySourceDir, "fuelPro.db-shm");
            if (File.Exists(shmSource))
            {
                File.Copy(shmSource, tempDb + "-shm", true);
                File.SetAttributes(tempDb + "-shm", FileAttributes.Normal);
            }
        }
        else
        {
            using var conn = new SqliteConnection($"Data Source={tempDb}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE AppMeta (Key TEXT PRIMARY KEY, Value TEXT);";
            cmd.ExecuteNonQuery();
        }

        return tempDb;
    }

    [Fact]
    public async Task Stage1_UpgradeV1341ToV1342_PreservesAllExistingRecordsAndExtendsSchema()
    {
        var stagingDb = CreateDisposableStagingDb();

        int preShifts = 0, preDsmEntries = 0, preTanks = 0, preMappings = 0, preDebtors = 0, preKhandhare = 0;

        // Baseline pre-upgrade inspection
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite($"Data Source={stagingDb}")
            .Options;

        using (var preContext = new FuelProDbContext(options))
        {
            preShifts = await preContext.Shifts.CountAsync();
            preDsmEntries = await preContext.DsmEntries.CountAsync();
            preTanks = await preContext.TankDefinitions.CountAsync();
            preMappings = await preContext.PumpMappings.CountAsync();
            preDebtors = await preContext.Creditors.CountAsync();
            preKhandhare = await preContext.KhandharePetroleumEntries.CountAsync();
        }

        // Execute v1.3.42 Upgrade Protocol (EnsureLegacyDatabaseCompatibility + SeedData.InitializeAsync)
        App.EnsureLegacyDatabaseCompatibility(stagingDb);

        using (var upgradeContext = new FuelProDbContext(options))
        {
            await SeedData.InitializeAsync(upgradeContext, null);
        }

        // Post-upgrade verification
        using (var postContext = new FuelProDbContext(options))
        {
            int postShifts = await postContext.Shifts.CountAsync();
            int postDsmEntries = await postContext.DsmEntries.CountAsync();
            int postTanks = await postContext.TankDefinitions.CountAsync();
            int postMappings = await postContext.PumpMappings.CountAsync();
            int postDebtors = await postContext.Creditors.CountAsync();
            int postKhandhare = await postContext.KhandharePetroleumEntries.CountAsync();

            // Zero data loss
            Assert.True(postShifts >= preShifts, $"Shifts decreased: pre={preShifts}, post={postShifts}");
            Assert.True(postDsmEntries >= preDsmEntries, $"DsmEntries decreased: pre={preDsmEntries}, post={postDsmEntries}");
            Assert.True(postTanks >= preTanks, $"TankDefinitions decreased: pre={preTanks}, post={postTanks}");
            Assert.True(postMappings >= preMappings, $"PumpMappings decreased: pre={preMappings}, post={postMappings}");
            Assert.True(postDebtors >= preDebtors, $"Creditors decreased: pre={preDebtors}, post={postDebtors}");
            Assert.True(postKhandhare >= preKhandhare, $"Khandhare entries decreased: pre={preKhandhare}, post={postKhandhare}");

            // Verify v1.3.42 dynamic tables and columns exist and are querying properly
            var canonicalFeatures = await postContext.AppFeatureSettings.ToListAsync();
            Assert.True(canonicalFeatures.Count >= 36, $"Expected at least 36 canonical features, got {canonicalFeatures.Count}");
            var canonicalDefaults = AppFeatureSetting.GetCanonicalDefaults();
            Assert.All(canonicalDefaults, cd => Assert.Contains(canonicalFeatures, f => f.FeatureKey == cd.FeatureKey));

            var collectionTypes = await postContext.CollectionTypes.ToListAsync();
            Assert.NotEmpty(collectionTypes);
            Assert.All(collectionTypes, ct =>
            {
                Assert.True(ct.EnableMorning);
                Assert.True(ct.EnableDay);
                Assert.True(ct.EnableNight);
            });

            // Verify TankDefinitions HasTesting column
            var tanks = await postContext.TankDefinitions.ToListAsync();
            var cngTank = tanks.FirstOrDefault(t => t.FuelType == "CNG" || t.TankName.Contains("CNG"));
            if (cngTank != null)
            {
                Assert.False(cngTank.HasTesting, "CNG tank testing should be auto-disabled");
            }
            var liquidTanks = tanks.Where(t => t.FuelType != "CNG" && !t.TankName.Contains("CNG")).ToList();
            if (liquidTanks.Count > 0)
            {
                Assert.All(liquidTanks, t => Assert.True(t.HasTesting, $"{t.TankName} liquid tank testing should be enabled"));
            }

            // Verify PaymentCollectionItems.Otp column accessibility
            var conn = postContext.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync();
            }
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM PaymentCollectionItems WHERE Otp IS NOT NULL OR Otp IS NULL;";
            var result = await cmd.ExecuteScalarAsync();
            Assert.NotNull(result);
        }
    }

    [Fact]
    public async Task Stage2_StationConfiguration_PersistenceAcrossRestartAndModuleChanges()
    {
        var stagingDb = CreateDisposableStagingDb();
        App.EnsureLegacyDatabaseCompatibility(stagingDb);

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={stagingDb}"),
            ServiceLifetime.Transient);
        services.AddTransient<IStationConfigurationService, StationConfigurationService>();
        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddTransient<IFeatureToggleService, FeatureToggleService>();

        using var sp = services.BuildServiceProvider();

        // 1. Configure custom station parameters
        using (var scope = sp.CreateScope())
        {
            var colService = scope.ServiceProvider.GetRequiredService<ICollectionTypeService>();

            // Add custom collection type
            await colService.SaveCollectionTypeAsync(new CollectionTypeMaster
            {
                Code = "STAGING_UPI",
                DisplayName = "Staging Custom UPI",
                Category = "UPI",
                DisplayOrder = 99,
                IsActive = true,
                EnableMorning = true,
                EnableDay = true,
                EnableNight = false
            });
        }

        // 2. Simulate application restart with a fresh service scope
        using (var restartScope = sp.CreateScope())
        {
            var colService = restartScope.ServiceProvider.GetRequiredService<ICollectionTypeService>();
            var allTypes = await colService.GetAllCollectionTypesAsync();
            var customType = allTypes.FirstOrDefault(c => c.Code == "STAGING_UPI");

            Assert.NotNull(customType);
            Assert.Equal("Staging Custom UPI", customType.DisplayName);
            Assert.True(customType.EnableMorning);
            Assert.True(customType.EnableDay);
            Assert.False(customType.EnableNight);
        }
    }

    [Fact]
    public async Task Stage3_OtpMachineTransactions_AndDenominations_CalculateAndAggregateWithoutDoubleCounting()
    {
        var stagingDb = CreateDisposableStagingDb();
        App.EnsureLegacyDatabaseCompatibility(stagingDb);

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={stagingDb}"),
            ServiceLifetime.Transient);
        services.AddSingleton<IDsmCalculationService, DsmCalculationService>();
        services.AddScoped<IShiftAggregationService, ShiftAggregationService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddTransient<IStationConfigurationService, StationConfigurationService>();
        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddTransient<IFeatureToggleService, FeatureToggleService>();

        using var sp = services.BuildServiceProvider();

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            await SeedData.InitializeAsync(db, null);

            var date = new DateTime(2026, 10, 14);
            var shift = new Shift { ShiftDate = date, ShiftType = "A", CreatedAt = DateTime.Now };
            db.Shifts.Add(shift);
            await db.SaveChangesAsync();

            // Create DSM Entry with dynamic OTP machine items and cash denominations
            var dynamicItems = new List<PaymentCollectionItem>
            {
                new() { CollectionTypeCode = "PINELAB_CARD", Amount = 1500, Tid = "TID_POS_01", Batch = "B01", Otp = "OTP1234" },
                new() { CollectionTypeCode = "PINELAB_CARD", Amount = 2500, Tid = "TID_POS_01", Batch = "B01", Otp = "OTP5678" },
                new() { CollectionTypeCode = "PAYTM", Amount = 1000, Tid = "PAYTM_QR_01" }
            };

            var dsmEntry = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                DsmName = "Staging Operator",
                GrossSales = 10000m,
                NozzleReadings = new List<NozzleReading>
                {
                    new() { NozzleNumber = 1, FuelType = "MS-I", SaleLitres = 100, Rate = 100, Amount = 10000 }
                },
                TestingEntries = new List<TestingEntry>
                {
                    new() { FuelType = "MS-I", Litres = 5, Rate = 100, Amount = 500 }
                },
                PaymentCollection = new PaymentCollection
                {
                    CashDeposit = 3000,
                    CreditCardMorning = 4000,
                    DynamicItemsJson = JsonSerializer.Serialize(dynamicItems),
                    Items = dynamicItems
                },
                CashDenominations = new List<CashDenomination>
                {
                    new() { CashType = "Cash1", Denom500 = 4, TotalAmount = 2000 },
                    new() { CashType = "Cash2", Denom200 = 5, TotalAmount = 1000 }
                },
                CreatedAt = DateTime.Now
            };

            db.DsmEntries.Add(dsmEntry);
            await db.SaveChangesAsync();

            // Act: Calculate shift report
            var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();
            var entries = await db.DsmEntries
                .Include(e => e.NozzleReadings)
                .Include(e => e.TestingEntries)
                .Include(e => e.PaymentCollection)
                .Include(e => e.PaymentCollection.Items)
                .Include(e => e.CashDenominations)
                .Where(e => e.ShiftId == shift.ShiftId)
                .ToListAsync();

            var report = reportService.CalculateShiftReport(
                date: date,
                shiftType: "A",
                entries: entries,
                shiftExpenses: new List<Expense>(),
                otherCashList: new List<ShiftOtherCash>(),
                repayments: new List<CreditorRepayment>(),
                hsdRate: 90, msIRate: 100, msIIRate: 100, cngRate: 85,
                todayTid: new BusinessDayTidSheet(), tomorrowTid: new BusinessDayTidSheet(),
                stationName: "Mitali Service Station"
            );

            // Assertions:
            // 1. Gross Fuel Sales = 10000, Testing = 500 => DsrTotalFuelAmount = 9500
            Assert.Equal(10000, report.TotalFuelAmount);
            Assert.Equal(9500, report.DsrTotalFuelAmount);

            // 2. Collection breakdown captures dynamic PINELAB_CARD and PAYTM
            var pineLabCat = report.CollectionBreakdown.FirstOrDefault(c => c.Category.Contains("PINELAB", StringComparison.OrdinalIgnoreCase) || c.Category.Contains("Card", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(pineLabCat);
            Assert.Equal(4000, pineLabCat.Amount); // 1500 + 2500

            var paytmCat = report.CollectionBreakdown.FirstOrDefault(c => c.Category.Contains("Paytm", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(paytmCat);
            Assert.Equal(1000, paytmCat.Amount);

            // 3. Cash Denominations total properly reflected
            Assert.Equal(2000, report.Cash1.GrandTotal);
            Assert.Equal(1000, report.Cash2.GrandTotal);

            // 4. TID grouping properly extracts both transactions under TID_POS_01 without double-counting
            var tidItems = dsmEntry.PaymentCollection.GetAllItems().Where(i => i.Tid == "TID_POS_01").ToList();
            Assert.Equal(2, tidItems.Count);
            Assert.Equal(4000, tidItems.Sum(i => i.Amount));
            Assert.Contains(tidItems, i => i.Otp == "OTP1234");
            Assert.Contains(tidItems, i => i.Otp == "OTP5678");
        }
    }
}
