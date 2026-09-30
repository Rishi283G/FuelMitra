using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.UI;
using FuelPro.UI.ViewModels;
using Xunit;

namespace FuelPro.Tests;

public class OpeningBalanceMigrationTests : IDisposable
{
    private readonly string _testDir;

    public OpeningBalanceMigrationTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _testDir = Path.Combine(Path.GetTempPath(), "FuelPro_MigrationTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }
        catch { }
    }

    private string CreateDbPath()
    {
        return Path.Combine(_testDir, $"fuelPro_{Guid.NewGuid():N}.db");
    }

    /// <summary>
    /// Creates a simulated pre-Opening-Balance legacy SQLite database.
    /// Excludes DsmPersonalDebtors.EntryType and the OpeningBalances table.
    /// Populates operational shifts, DSM entries, debtors, repayments, and settings.
    /// </summary>
    private async Task<(string DbPath, LegacyDataFingerprint Fingerprint)> CreateLegacyStationDatabaseAsync()
    {
        var dbPath = CreateDbPath();

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                PRAGMA foreign_keys = OFF;

                CREATE TABLE ""Settings"" (
                    ""SettingId"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""StationName"" TEXT NOT NULL DEFAULT 'Mitali Service Station',
                    ""StationCode"" TEXT NOT NULL DEFAULT 'MSS-001',
                    ""DealerName"" TEXT NOT NULL DEFAULT 'Mitali Enterprises',
                    ""ContactNumber"" TEXT NOT NULL DEFAULT '9876543210',
                    ""Address"" TEXT NOT NULL DEFAULT 'Highway Junction',
                    ""MsRate"" REAL NOT NULL DEFAULT 104.5,
                    ""HsdRate"" REAL NOT NULL DEFAULT 92.5,
                    ""CngRate"" REAL NOT NULL DEFAULT 85.0,
                    ""HasTwoMsTanks"" INTEGER NOT NULL DEFAULT 0,
                    ""HasCngPumps"" INTEGER NOT NULL DEFAULT 0,
                    ""DensityTolerance"" REAL NOT NULL DEFAULT 0.003,
                    ""UpdatedAt"" TEXT NOT NULL DEFAULT (datetime('now'))
                );
                INSERT INTO Settings (SettingId, StationName, StationCode) VALUES (1, 'Mitali Service Station', 'MSS-001');

                CREATE TABLE ""Shifts"" (
                    ""ShiftId"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""ShiftDate"" TEXT NOT NULL,
                    ""ShiftType"" TEXT NOT NULL,
                    ""ManagerName"" TEXT NULL,
                    ""Remarks"" TEXT NULL,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT (datetime('now'))
                );
                INSERT INTO Shifts (ShiftId, ShiftDate, ShiftType, ManagerName) VALUES (1, '2026-09-01', 'A', 'Manager 1');
                INSERT INTO Shifts (ShiftId, ShiftDate, ShiftType, ManagerName) VALUES (2, '2026-09-02', 'B', 'Manager 2');

                CREATE TABLE ""DsmEntries"" (
                    ""DsmEntryId"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""ShiftId"" INTEGER NOT NULL,
                    ""DsmName"" TEXT NOT NULL,
                    ""GrossSales"" REAL NOT NULL DEFAULT 0.0,
                    ""TotalCollection"" REAL NOT NULL DEFAULT 0.0,
                    ""TotalShortage"" REAL NOT NULL DEFAULT 0.0,
                    ""TotalExcess"" REAL NOT NULL DEFAULT 0.0,
                    ""ShiftPeriod"" TEXT NOT NULL DEFAULT 'Day',
                    ""CreatedAt"" TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY (""ShiftId"") REFERENCES ""Shifts"" (""ShiftId"")
                );
                INSERT INTO DsmEntries (DsmEntryId, ShiftId, DsmName, TotalShortage, TotalExcess) 
                VALUES (1, 1, 'Ramesh', 2500.0, 0.0);
                INSERT INTO DsmEntries (DsmEntryId, ShiftId, DsmName, TotalShortage, TotalExcess) 
                VALUES (2, 2, 'Suresh', 1200.0, 0.0);

                CREATE TABLE ""PaymentCollections"" (
                    ""PaymentId"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""DsmEntryId"" INTEGER NOT NULL,
                    ""Cash"" REAL NOT NULL DEFAULT 0.0,
                    ""CreditCard"" REAL NOT NULL DEFAULT 0.0,
                    ""PhonePe"" REAL NOT NULL DEFAULT 0.0,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY (""DsmEntryId"") REFERENCES ""DsmEntries"" (""DsmEntryId"")
                );

                CREATE TABLE ""Creditors"" (
                    ""CreditorId"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""Name"" TEXT NOT NULL,
                    ""Phone"" TEXT NULL,
                    ""IsActive"" INTEGER NOT NULL DEFAULT 1,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT (datetime('now'))
                );
                INSERT INTO Creditors (CreditorId, Name, Phone, IsActive) 
                VALUES (1, 'M/s Sharma Transports', '9822011223', 1);

                CREATE TABLE ""DebitEntries"" (
                    ""DebitId"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""DsmEntryId"" INTEGER NOT NULL,
                    ""DebtorName"" TEXT NOT NULL,
                    ""Amount"" REAL NOT NULL,
                    ""VehicleNumber"" TEXT NULL,
                    ""PaymentMethod"" TEXT NOT NULL DEFAULT 'Credit',
                    ""CreatedAt"" TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY (""DsmEntryId"") REFERENCES ""DsmEntries"" (""DsmEntryId"")
                );
                INSERT INTO DebitEntries (DebitId, DsmEntryId, DebtorName, Amount, VehicleNumber) 
                VALUES (1, 1, 'M/s Sharma Transports', 15000.0, 'MH-14-AZ-9999');

                CREATE TABLE ""CreditorRepayments"" (
                    ""CreditorRepaymentId"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""CreditorName"" TEXT NOT NULL,
                    ""RepaymentDate"" TEXT NOT NULL,
                    ""Amount"" REAL NOT NULL,
                    ""PaymentMode"" TEXT NOT NULL DEFAULT 'Cash',
                    ""CreatedAt"" TEXT NOT NULL DEFAULT (datetime('now'))
                );
                INSERT INTO CreditorRepayments (CreditorRepaymentId, CreditorName, RepaymentDate, Amount, PaymentMode) 
                VALUES (1, 'M/s Sharma Transports', '2026-09-05', 5000.0, 'Cash');

                -- Legacy DsmPersonalDebtors: EXPLICITLY NO EntryType column!
                CREATE TABLE ""DsmPersonalDebtors"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""SyncGuid"" TEXT NOT NULL,
                    ""DsmEntryId"" INTEGER NULL,
                    ""DsmName"" TEXT NOT NULL,
                    ""Date"" TEXT NOT NULL,
                    ""Time"" TEXT NOT NULL,
                    ""Amount"" REAL NOT NULL,
                    ""FuelProduct"" TEXT NULL,
                    ""Remarks"" TEXT NULL,
                    ""PaymentMethod"" TEXT NOT NULL,
                    ""Denom500"" INTEGER NOT NULL DEFAULT 0,
                    ""Denom200"" INTEGER NOT NULL DEFAULT 0,
                    ""Denom100"" INTEGER NOT NULL DEFAULT 0,
                    ""Denom50"" INTEGER NOT NULL DEFAULT 0,
                    ""Denom20"" INTEGER NOT NULL DEFAULT 0,
                    ""Denom10"" INTEGER NOT NULL DEFAULT 0,
                    ""Coins"" INTEGER NOT NULL DEFAULT 0,
                    ""CardTid"" TEXT NULL,
                    ""CardBatch"" TEXT NULL,
                    ""SequenceNumber"" INTEGER NOT NULL DEFAULT 1,
                    ""RepaidAmount"" REAL NOT NULL DEFAULT 0.0,
                    ""DeductFromSalary"" INTEGER NOT NULL DEFAULT 1,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY (""DsmEntryId"") REFERENCES ""DsmEntries"" (""DsmEntryId"")
                );
                INSERT INTO DsmPersonalDebtors (Id, SyncGuid, DsmEntryId, DsmName, Date, Time, Amount, PaymentMethod, SequenceNumber, RepaidAmount)
                VALUES (1, 'guid-dsm-001', 1, 'Ramesh', '2026-09-01', '14:30:00', 2500.0, 'Shortage', 1, 1000.0);
                INSERT INTO DsmPersonalDebtors (Id, SyncGuid, DsmEntryId, DsmName, Date, Time, Amount, PaymentMethod, SequenceNumber, RepaidAmount)
                VALUES (2, 'guid-dsm-002', 2, 'Suresh', '2026-09-02', '22:15:00', 1200.0, 'Shortage', 1, 0.0);

                CREATE TABLE ""DsmPersonalDebtorRepayments"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""SyncGuid"" TEXT NOT NULL,
                    ""DsmPersonalDebtorId"" INTEGER NOT NULL,
                    ""ShiftId"" INTEGER NULL,
                    ""Date"" TEXT NOT NULL,
                    ""Amount"" REAL NOT NULL,
                    ""PaymentMethod"" TEXT NOT NULL DEFAULT 'Cash',
                    ""Denom500"" INTEGER NOT NULL DEFAULT 0,
                    ""Denom200"" INTEGER NOT NULL DEFAULT 0,
                    ""Denom100"" INTEGER NOT NULL DEFAULT 0,
                    ""Denom50"" INTEGER NOT NULL DEFAULT 0,
                    ""Denom20"" INTEGER NOT NULL DEFAULT 0,
                    ""Denom10"" INTEGER NOT NULL DEFAULT 0,
                    ""Coins"" INTEGER NOT NULL DEFAULT 0,
                    ""CardTid"" TEXT NULL,
                    ""CardBatch"" TEXT NULL,
                    ""Source"" TEXT NOT NULL DEFAULT 'Direct',
                    ""CreatedAt"" TEXT NOT NULL DEFAULT (datetime('now')),
                    FOREIGN KEY (""DsmPersonalDebtorId"") REFERENCES ""DsmPersonalDebtors"" (""Id"")
                );
                INSERT INTO DsmPersonalDebtorRepayments (Id, SyncGuid, DsmPersonalDebtorId, Date, Amount, PaymentMethod, Source)
                VALUES (1, 'guid-dsm-rep-001', 1, '2026-09-03', 1000.0, 'Cash', 'Direct');

                CREATE TABLE ""SyncChangeLogs"" (
                    ""Id"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""TableName"" TEXT NOT NULL,
                    ""RecordId"" INTEGER NOT NULL,
                    ""Operation"" TEXT NOT NULL,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT (datetime('now')),
                    ""IsSynced"" INTEGER NOT NULL DEFAULT 0,
                    ""SyncedAt"" TEXT NULL,
                    ""StationId"" TEXT NULL,
                    ""MachineId"" TEXT NULL,
                    ""SyncGuid"" TEXT NULL,
                    ""RecordGuid"" TEXT NULL
                );
            ";
            await cmd.ExecuteNonQueryAsync();
        }

        var fingerprint = await CaptureFingerprintAsync(dbPath);
        return (dbPath, fingerprint);
    }

    private record LegacyDataFingerprint(
        int DsmDebtorsCount,
        double DsmDebtorsSum,
        double DsmDebtorsRepaidSum,
        List<int> DsmDebtorIds,
        int DsmRepaymentsCount,
        double DsmRepaymentsSum,
        List<int> DsmRepaymentIds,
        int CreditorsCount,
        List<int> CreditorIds,
        int DebitEntriesCount,
        double DebitEntriesSum,
        int CreditorRepaymentsCount,
        double CreditorRepaymentsSum,
        int ShiftsCount,
        int DsmEntriesCount
    );

    private static async Task<LegacyDataFingerprint> CaptureFingerprintAsync(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        var dsmDebtorIds = new List<int>();
        var dsmRepaymentIds = new List<int>();
        var creditorIds = new List<int>();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Id FROM DsmPersonalDebtors ORDER BY Id;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) dsmDebtorIds.Add(r.GetInt32(0));
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Id FROM DsmPersonalDebtorRepayments ORDER BY Id;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) dsmRepaymentIds.Add(r.GetInt32(0));
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT CreditorId FROM Creditors ORDER BY CreditorId;";
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) creditorIds.Add(r.GetInt32(0));
        }

        async Task<double> QueryDoubleAsync(string sql)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            var res = await cmd.ExecuteScalarAsync();
            return res == null || res == DBNull.Value ? 0.0 : Convert.ToDouble(res);
        }

        async Task<int> QueryIntAsync(string sql)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            var res = await cmd.ExecuteScalarAsync();
            return res == null || res == DBNull.Value ? 0 : Convert.ToInt32(res);
        }

        return new LegacyDataFingerprint(
            DsmDebtorsCount: await QueryIntAsync("SELECT COUNT(*) FROM DsmPersonalDebtors;"),
            DsmDebtorsSum: await QueryDoubleAsync("SELECT SUM(Amount) FROM DsmPersonalDebtors;"),
            DsmDebtorsRepaidSum: await QueryDoubleAsync("SELECT SUM(RepaidAmount) FROM DsmPersonalDebtors;"),
            DsmDebtorIds: dsmDebtorIds,
            DsmRepaymentsCount: await QueryIntAsync("SELECT COUNT(*) FROM DsmPersonalDebtorRepayments;"),
            DsmRepaymentsSum: await QueryDoubleAsync("SELECT SUM(Amount) FROM DsmPersonalDebtorRepayments;"),
            DsmRepaymentIds: dsmRepaymentIds,
            CreditorsCount: await QueryIntAsync("SELECT COUNT(*) FROM Creditors;"),
            CreditorIds: creditorIds,
            DebitEntriesCount: await QueryIntAsync("SELECT COUNT(*) FROM DebitEntries;"),
            DebitEntriesSum: await QueryDoubleAsync("SELECT SUM(Amount) FROM DebitEntries;"),
            CreditorRepaymentsCount: await QueryIntAsync("SELECT COUNT(*) FROM CreditorRepayments;"),
            CreditorRepaymentsSum: await QueryDoubleAsync("SELECT SUM(Amount) FROM CreditorRepayments;"),
            ShiftsCount: await QueryIntAsync("SELECT COUNT(*) FROM Shifts;"),
            DsmEntriesCount: await QueryIntAsync("SELECT COUNT(*) FROM DsmEntries;")
        );
    }

    private static bool ColumnExistsInTable(SqliteConnection connection, string table, string column)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r["name"]?.ToString(), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool TableExistsInDb(SqliteConnection connection, string table)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@name";
        cmd.Parameters.AddWithValue("@name", table);
        return cmd.ExecuteScalar() != null;
    }

    private ServiceProvider BuildServiceProvider(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"),
            ServiceLifetime.Transient);

        services.AddTransient<IOpeningBalanceRepository, OpeningBalanceRepository>();
        services.AddTransient<IDsmPersonalDebtorRepository, DsmPersonalDebtorRepository>();
        services.AddTransient<ICreditorRepository, CreditorRepository>();
        services.AddTransient<IDebitEntryRepository, DebitEntryRepository>();
        services.AddTransient<ICreditorRepaymentRepository, CreditorRepaymentRepository>();
        services.AddSingleton<IDsmCalculationService, DsmCalculationService>();
        services.AddTransient<IFinancialCalculationService, FinancialCalculationService>();
        return services.BuildServiceProvider();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // TEST 1: Legacy database without EntryType column upgrades successfully
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test01_LegacyDatabaseWithoutEntryType_UpgradesSuccessfully()
    {
        var (dbPath, _) = await CreateLegacyStationDatabaseAsync();

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            Assert.False(ColumnExistsInTable(connection, "DsmPersonalDebtors", "EntryType"));
        }

        // Run application startup legacy database compatibility
        App.EnsureLegacyDatabaseCompatibility(dbPath);

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            Assert.True(ColumnExistsInTable(connection, "DsmPersonalDebtors", "EntryType"));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // TEST 2: Legacy database without OpeningBalances table upgrades successfully
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test02_LegacyDatabaseWithoutOpeningBalances_UpgradesSuccessfully()
    {
        var (dbPath, _) = await CreateLegacyStationDatabaseAsync();

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            Assert.False(TableExistsInDb(connection, "OpeningBalances"));
        }

        // Run startup migration
        App.EnsureLegacyDatabaseCompatibility(dbPath);

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            Assert.True(TableExistsInDb(connection, "OpeningBalances"));
            Assert.True(ColumnExistsInTable(connection, "OpeningBalances", "SyncGuid"));
            Assert.True(ColumnExistsInTable(connection, "OpeningBalances", "EntityType"));
            Assert.True(ColumnExistsInTable(connection, "OpeningBalances", "Amount"));
            Assert.True(ColumnExistsInTable(connection, "OpeningBalances", "IsActive"));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // TEST 3: All pre-existing DSM records receive EntryType = 'Operational'
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test03_PreExistingDsmRecords_BecomeOperational()
    {
        var (dbPath, _) = await CreateLegacyStationDatabaseAsync();

        App.EnsureLegacyDatabaseCompatibility(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, DsmName, EntryType FROM DsmPersonalDebtors ORDER BY Id;";
        using var r = await cmd.ExecuteReaderAsync();

        var rows = new List<(int Id, string Name, string EntryType)>();
        while (await r.ReadAsync())
        {
            rows.Add((r.GetInt32(0), r.GetString(1), r.GetString(2)));
        }

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("Operational", row.EntryType));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // TEST 4: Existing DSM repayments remain intact with parent FK references
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test04_ExistingDsmRepayments_RemainIntact()
    {
        var (dbPath, preFingerprint) = await CreateLegacyStationDatabaseAsync();

        App.EnsureLegacyDatabaseCompatibility(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, DsmPersonalDebtorId, Amount, PaymentMethod FROM DsmPersonalDebtorRepayments;";
        using var r = await cmd.ExecuteReaderAsync();

        Assert.True(await r.ReadAsync());
        Assert.Equal(1, r.GetInt32(0));
        Assert.Equal(1, r.GetInt32(1)); // Linked to Ramesh (Id=1)
        Assert.Equal(1000.0, r.GetDouble(2));
        Assert.Equal("Cash", r.GetString(3));
        Assert.False(await r.ReadAsync());
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // TEST 5: Existing debtor debit entries and repayments remain intact
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test05_ExistingDebtorTransactions_RemainIntact()
    {
        var (dbPath, _) = await CreateLegacyStationDatabaseAsync();

        App.EnsureLegacyDatabaseCompatibility(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        // Verify Creditor
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT CreditorId, Name FROM Creditors WHERE CreditorId = 1;";
            using var r = await cmd.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal("M/s Sharma Transports", r.GetString(1));
        }

        // Verify DebitEntry
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT DebitId, DebtorName, Amount, VehicleNumber FROM DebitEntries WHERE DebitId = 1;";
            using var r = await cmd.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal("M/s Sharma Transports", r.GetString(1));
            Assert.Equal(15000.0, r.GetDouble(2));
            Assert.Equal("MH-14-AZ-9999", r.GetString(3));
        }

        // Verify Repayment
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT CreditorRepaymentId, CreditorName, Amount FROM CreditorRepayments WHERE CreditorRepaymentId = 1;";
            using var r = await cmd.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal("M/s Sharma Transports", r.GetString(1));
            Assert.Equal(5000.0, r.GetDouble(2));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // TEST 6: Existing shift and DSR records remain intact
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test06_ExistingShiftAndDsrData_RemainIntact()
    {
        var (dbPath, _) = await CreateLegacyStationDatabaseAsync();

        App.EnsureLegacyDatabaseCompatibility(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT ShiftId, ShiftDate, ShiftType FROM Shifts ORDER BY ShiftId;";
            using var r = await cmd.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal("2026-09-01", r.GetString(1));
            Assert.Equal("A", r.GetString(2));
            Assert.True(await r.ReadAsync());
            Assert.Equal("2026-09-02", r.GetString(1));
            Assert.Equal("B", r.GetString(2));
        }

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT DsmEntryId, DsmName, TotalShortage FROM DsmEntries ORDER BY DsmEntryId;";
            using var r = await cmd.ExecuteReaderAsync();
            Assert.True(await r.ReadAsync());
            Assert.Equal("Ramesh", r.GetString(1));
            Assert.Equal(2500.0, r.GetDouble(2));
            Assert.True(await r.ReadAsync());
            Assert.Equal("Suresh", r.GetString(1));
            Assert.Equal(1200.0, r.GetDouble(2));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // TEST 7: Fresh database creation succeeds with all constraints and indexes
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test07_FreshDatabaseCreation_Succeeds()
    {
        var dbPath = CreateDbPath();

        // Simulate production startup order: EF creates full schema first
        var sp = BuildServiceProvider(dbPath);
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        await context.Database.EnsureCreatedAsync();

        // Then EnsureLegacyDatabaseCompatibility ensures OpeningBalances + EntryType are present
        // This must be idempotent when EF already created the schema
        App.EnsureLegacyDatabaseCompatibility(dbPath);

        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();

            // OpeningBalances table (created by EnsureLegacyDatabaseCompatibility or EF)
            Assert.True(TableExistsInDb(connection, "OpeningBalances"));

            // DsmPersonalDebtors table (created by EF Core)
            Assert.True(TableExistsInDb(connection, "DsmPersonalDebtors"));
            Assert.True(ColumnExistsInTable(connection, "DsmPersonalDebtors", "EntryType"));

            // Verify unique index on OpeningBalances.SyncGuid
            using var cmdIdx = connection.CreateCommand();
            cmdIdx.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='OpeningBalances' AND name='IX_OpeningBalances_SyncGuid';";
            Assert.NotNull(await cmdIdx.ExecuteScalarAsync());
        }

        // Running compatibility a second time (idempotency check)
        App.EnsureLegacyDatabaseCompatibility(dbPath);

        var repo = scope.ServiceProvider.GetRequiredService<IOpeningBalanceRepository>();
        var result = await repo.GetAllActiveAsync();
        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // TEST 8: Upgrade → Create Debtor Opening Balance succeeds
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test08_Upgrade_CreateDebtorOpeningBalance_Succeeds()
    {
        var (dbPath, _) = await CreateLegacyStationDatabaseAsync();
        App.EnsureLegacyDatabaseCompatibility(dbPath);

        var sp = BuildServiceProvider(dbPath);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpeningBalanceRepository>();

        var ob = new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = "1",
            CreditorId = 1,
            OpeningDate = new DateTime(2026, 8, 31),
            Amount = 10000.0,
            Notes = "Historical balance prior to PyroSync migration",
            IsActive = true,
            CreatedBy = "Owner"
        };

        var saveResult = await repo.SaveOpeningBalanceAsync(ob);
        Assert.True(saveResult.Success);
        Assert.NotNull(saveResult.Data);
        Assert.True(saveResult.Data.OpeningBalanceId > 0);
        Assert.False(string.IsNullOrEmpty(saveResult.Data.SyncGuid));

        var fetchResult = await repo.GetActiveByEntityAsync("Debtor", "1");
        Assert.True(fetchResult.Success);
        Assert.NotNull(fetchResult.Data);
        Assert.Equal(10000.0, fetchResult.Data.Amount);
        Assert.Equal(1, fetchResult.Data.CreditorId);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // TEST 9: Upgrade → Create DSM Opening Balance succeeds and creates anchor
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test09_Upgrade_CreateDsmOpeningBalance_CreatesAnchorRecord()
    {
        var (dbPath, _) = await CreateLegacyStationDatabaseAsync();
        App.EnsureLegacyDatabaseCompatibility(dbPath);

        var sp = BuildServiceProvider(dbPath);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpeningBalanceRepository>();

        var ob = new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Ramesh",
            OpeningDate = new DateTime(2026, 8, 31),
            Amount = 8500.0,
            Notes = "Previous system shortage carryover",
            IsActive = true,
            CreatedBy = "Owner"
        };

        var saveResult = await repo.SaveOpeningBalanceAsync(ob);
        Assert.True(saveResult.Success);

        // Verify the automatic anchor record in DsmPersonalDebtors
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var anchors = await context.DsmPersonalDebtors.Where(d => d.DsmName == "Ramesh").ToListAsync();

        var anchor = anchors.FirstOrDefault(d => d.EntryType == "OpeningBalance");
        Assert.NotNull(anchor);
        Assert.Equal(8500.0, anchor.Amount);
        Assert.Equal("Ramesh", anchor.DsmName);

        // Ensure pre-existing operational records are still present
        var operational = anchors.Where(d => d.EntryType == "Operational").ToList();
        Assert.Single(operational);
        Assert.Equal(2500.0, operational[0].Amount);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // TEST 10: Upgrade → Sync metadata remains valid for opening balances
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test10_Upgrade_SyncMetadataRemainsValid()
    {
        var (dbPath, _) = await CreateLegacyStationDatabaseAsync();
        App.EnsureLegacyDatabaseCompatibility(dbPath);

        var sp = BuildServiceProvider(dbPath);
        using var scope = sp.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOpeningBalanceRepository>();

        var ob = new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = "1",
            CreditorId = 1,
            OpeningDate = new DateTime(2026, 8, 31),
            Amount = 10000.0,
            IsActive = true
        };
        var saveResult = await repo.SaveOpeningBalanceAsync(ob);
        Assert.True(saveResult.Success);

        // Verify SyncChangeLog recorded the insert
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT TableName, Operation, SyncGuid FROM SyncChangeLogs WHERE TableName = 'OpeningBalances';";
        using var r = await cmd.ExecuteReaderAsync();

        Assert.True(await r.ReadAsync());
        Assert.Equal("OpeningBalances", r.GetString(0));
        Assert.Equal("INSERT", r.GetString(1));
        Assert.False(string.IsNullOrEmpty(r.GetString(2)));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // TEST 11: Data fingerprint verification proves row counts & sums unchanged
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test11_DataFingerprintComparison_ProvesNoDataLoss()
    {
        var (dbPath, preFingerprint) = await CreateLegacyStationDatabaseAsync();

        // Perform upgrade
        App.EnsureLegacyDatabaseCompatibility(dbPath);

        var postFingerprint = await CaptureFingerprintAsync(dbPath);

        Assert.Equal(preFingerprint.DsmDebtorsCount, postFingerprint.DsmDebtorsCount);
        Assert.Equal(preFingerprint.DsmDebtorsSum, postFingerprint.DsmDebtorsSum);
        Assert.Equal(preFingerprint.DsmDebtorsRepaidSum, postFingerprint.DsmDebtorsRepaidSum);
        Assert.Equal(preFingerprint.DsmDebtorIds, postFingerprint.DsmDebtorIds);

        Assert.Equal(preFingerprint.DsmRepaymentsCount, postFingerprint.DsmRepaymentsCount);
        Assert.Equal(preFingerprint.DsmRepaymentsSum, postFingerprint.DsmRepaymentsSum);
        Assert.Equal(preFingerprint.DsmRepaymentIds, postFingerprint.DsmRepaymentIds);

        Assert.Equal(preFingerprint.CreditorsCount, postFingerprint.CreditorsCount);
        Assert.Equal(preFingerprint.CreditorIds, postFingerprint.CreditorIds);

        Assert.Equal(preFingerprint.DebitEntriesCount, postFingerprint.DebitEntriesCount);
        Assert.Equal(preFingerprint.DebitEntriesSum, postFingerprint.DebitEntriesSum);

        Assert.Equal(preFingerprint.CreditorRepaymentsCount, postFingerprint.CreditorRepaymentsCount);
        Assert.Equal(preFingerprint.CreditorRepaymentsSum, postFingerprint.CreditorRepaymentsSum);

        Assert.Equal(preFingerprint.ShiftsCount, postFingerprint.ShiftsCount);
        Assert.Equal(preFingerprint.DsmEntriesCount, postFingerprint.DsmEntriesCount);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // TEST 12: No destructive migration occurs
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test12_NoDestructiveMigrationOccurs()
    {
        var (dbPath, _) = await CreateLegacyStationDatabaseAsync();

        // Running compatibility multiple times (idempotency check)
        App.EnsureLegacyDatabaseCompatibility(dbPath);
        App.EnsureLegacyDatabaseCompatibility(dbPath);
        App.EnsureLegacyDatabaseCompatibility(dbPath);

        using var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync();

        // Verify all required tables exist
        var requiredTables = new[]
        {
            "Settings", "Shifts", "DsmEntries", "Creditors", "DebitEntries",
            "CreditorRepayments", "DsmPersonalDebtors", "DsmPersonalDebtorRepayments",
            "OpeningBalances", "SyncChangeLogs"
        };

        foreach (var tbl in requiredTables)
        {
            Assert.True(TableExistsInDb(connection, tbl), $"Table {tbl} must exist after upgrade");
        }

        // Verify no orphaned records
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM DsmPersonalDebtorRepayments r
            WHERE NOT EXISTS (SELECT 1 FROM DsmPersonalDebtors d WHERE d.Id = r.DsmPersonalDebtorId);
        ";
        var orphans = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(0, orphans);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // TEST 13: Critical Mitali Simulation
    // ─────────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test13_CriticalMitaliSimulation_MaintainsIntegrityAndAppliesHistoricalBalances()
    {
        // 1. Create simulated Mitali station legacy DB
        var (dbPath, preFingerprint) = await CreateLegacyStationDatabaseAsync();

        // 2. Execute startup migration
        App.EnsureLegacyDatabaseCompatibility(dbPath);

        // 3. Verify operational numbers remain identical
        var postFingerprint = await CaptureFingerprintAsync(dbPath);
        Assert.Equal(preFingerprint.DsmDebtorsSum, postFingerprint.DsmDebtorsSum);
        Assert.Equal(preFingerprint.DebitEntriesSum, postFingerprint.DebitEntriesSum);

        var sp = BuildServiceProvider(dbPath);
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var obRepo = scope.ServiceProvider.GetRequiredService<IOpeningBalanceRepository>();

        // Pre-opening balance operational state:
        // Ramesh operational shortage = 2500, repayments = 1000, net = 1500
        var rameshOperationalDebits = await context.DsmPersonalDebtors
            .Where(d => d.DsmName == "Ramesh" && d.EntryType == "Operational")
            .SumAsync(d => d.Amount);
        var rameshRepayments = await context.DsmPersonalDebtorRepayments
            .Where(r => r.DsmPersonalDebtor.DsmName == "Ramesh")
            .SumAsync(r => r.Amount);
        Assert.Equal(2500.0, rameshOperationalDebits);
        Assert.Equal(1000.0, rameshRepayments);
        Assert.Equal(1500.0, rameshOperationalDebits - rameshRepayments);

        // Sharma Transports operational balance: Debits = 15000, repayments = 5000, net = 10000
        var sharmaDebits = await context.DebitEntries
            .Where(d => d.DebtorName == "M/s Sharma Transports")
            .SumAsync(d => d.Amount);
        var sharmaRepayments = await context.CreditorRepayments
            .Where(r => r.CreditorName == "M/s Sharma Transports")
            .SumAsync(r => r.Amount);
        Assert.Equal(15000.0, sharmaDebits);
        Assert.Equal(5000.0, sharmaRepayments);
        Assert.Equal(10000.0, sharmaDebits - sharmaRepayments);

        // 4. Create Historical Opening Balances:
        // DSM Historical Opening for Ramesh = ₹8,500
        var dsmObResult = await obRepo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "DsmLoss",
            EntityIdentifier = "Ramesh",
            OpeningDate = new DateTime(2026, 8, 31),
            Amount = 8500.0,
            Notes = "Mitali Station historical DSM loss carryover",
            IsActive = true,
            CreatedBy = "Owner"
        });
        Assert.True(dsmObResult.Success);

        // Debtor Historical Opening for Sharma Transports = ₹10,000
        var debtorObResult = await obRepo.SaveOpeningBalanceAsync(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = "1",
            CreditorId = 1,
            OpeningDate = new DateTime(2026, 8, 31),
            Amount = 10000.0,
            Notes = "Mitali Station historical debtor credit carryover",
            IsActive = true,
            CreatedBy = "Owner"
        });
        Assert.True(debtorObResult.Success);

        // 5. Verify Calculations with Historical Balances:
        // Ramesh DsmPersonalDebtorSummaryRow:
        // OpeningBalance = 8500, TotalBorrowed = 2500, TotalRepaid = 1000, Balance = 10000
        var rameshRows = await context.DsmPersonalDebtors.Where(d => d.DsmName == "Ramesh").ToListAsync();
        var rameshOpening = rameshRows.Where(d => d.EntryType == "OpeningBalance").Sum(d => d.Amount);
        var rameshBorrowed = rameshRows.Where(d => d.EntryType == "Operational").Sum(d => d.Amount);
        var dsmSummary = new DsmPersonalDebtorSummaryRow
        {
            DsmName = "Ramesh",
            OpeningBalance = rameshOpening,
            TotalBorrowed = rameshBorrowed,
            TotalRepaid = rameshRepayments
        };
        Assert.Equal(8500.0, dsmSummary.OpeningBalance);
        Assert.Equal(2500.0, dsmSummary.TotalBorrowed);
        Assert.Equal(1000.0, dsmSummary.TotalRepaid);
        Assert.Equal(10000.0, dsmSummary.Balance); // 8500 + 2500 - 1000 = 10000

        // Sharma Transports DebtorDisplayRow:
        // OpeningBalance = 10000, TotalDebt = 15000, TotalRepayment = 5000, OutstandingBalance = 20000
        var debtorSummary = new DebtorDisplayRow
        {
            CreditorId = 1,
            Name = "M/s Sharma Transports",
            OpeningBalance = 10000.0,
            TotalDebt = sharmaDebits,
            TotalRepayment = sharmaRepayments
        };
        Assert.Equal(10000.0, debtorSummary.OpeningBalance);
        Assert.Equal(15000.0, debtorSummary.TotalDebt);
        Assert.Equal(5000.0, debtorSummary.TotalRepayment);
        Assert.Equal(20000.0, debtorSummary.OutstandingBalance); // 10000 + 15000 - 5000 = 20000

        // 6. Test Deactivation / Rollback Flow:
        // Deactivate Ramesh opening balance
        var rameshActiveOb = (await obRepo.GetActiveByEntityAsync("DsmLoss", "Ramesh")).Data;
        Assert.NotNull(rameshActiveOb);
        var deactDsm = await obRepo.DeactivateOpeningBalanceAsync(rameshActiveOb.OpeningBalanceId);
        Assert.True(deactDsm.Success);

        // Deactivate Sharma Transports opening balance
        var sharmaActiveOb = (await obRepo.GetActiveByEntityAsync("Debtor", "1")).Data;
        Assert.NotNull(sharmaActiveOb);
        var deactDebtor = await obRepo.DeactivateOpeningBalanceAsync(sharmaActiveOb.OpeningBalanceId);
        Assert.True(deactDebtor.Success);

        // Verify balances return to purely operational figures
        // Use a fresh scope to avoid EF Core change-tracker returning stale entities
        using var freshScope = sp.CreateScope();
        var freshContext = freshScope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        var revertedRameshRows = await freshContext.DsmPersonalDebtors
            .AsNoTracking()
            .Where(d => d.DsmName == "Ramesh")
            .ToListAsync();
        var revertedRameshOpening = revertedRameshRows.Where(d => d.EntryType == "OpeningBalance").Sum(d => d.Amount);
        Assert.Equal(0.0, revertedRameshOpening); // Anchor changed to DeactivatedOpening

        var revertedDsmSummary = new DsmPersonalDebtorSummaryRow
        {
            DsmName = "Ramesh",
            OpeningBalance = revertedRameshOpening,
            TotalBorrowed = revertedRameshRows.Where(d => d.EntryType == "Operational").Sum(d => d.Amount),
            TotalRepaid = rameshRepayments
        };
        Assert.Equal(1500.0, revertedDsmSummary.Balance);

        var freshObRepo = freshScope.ServiceProvider.GetRequiredService<IOpeningBalanceRepository>();
        var activeDebtorOb = (await freshObRepo.GetActiveByEntityAsync("Debtor", "1")).Data;
        var revertedDebtorSummary = new DebtorDisplayRow
        {
            CreditorId = 1,
            Name = "M/s Sharma Transports",
            OpeningBalance = activeDebtorOb?.Amount ?? 0.0,
            TotalDebt = sharmaDebits,
            TotalRepayment = sharmaRepayments
        };
        Assert.Equal(0.0, revertedDebtorSummary.OpeningBalance);
        Assert.Equal(10000.0, revertedDebtorSummary.OutstandingBalance);
    }
}
