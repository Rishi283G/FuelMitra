using System.IO;
using System;
using System.Threading.Tasks;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FuelPro.Tests;

public sealed class StartupOilDataSafetyTests
{
    [Fact]
    public async Task Compatibility_keeps_identical_sales_from_different_shifts_and_preserves_values()
    {
        using var database = await DisposableOilDatabase.CreateAsync();
        var productId = await database.AddProductAsync();
        var shiftA = await database.AddShiftAsync("A");
        var shiftB = await database.AddShiftAsync("B");
        var firstId = await database.AddSaleAsync(productId, shiftA, 10, 90.5, 123.45);
        var secondId = await database.AddSaleAsync(productId, shiftB, 10, 90.5, 113.45);

        database.RunCompatibility();

        using var context = database.CreateContext();
        var logs = await context.OilDefDailyLogs.OrderBy(l => l.Id).ToListAsync();
        Assert.Equal(new[] { firstId, secondId }, logs.Select(l => l.Id));
        Assert.Equal(new int?[] { shiftA, shiftB }, logs.Select(l => l.ShiftId));
        Assert.Equal(new[] { 123.45, 113.45 }, logs.Select(l => l.RemainingStock));
        Assert.All(logs, log =>
        {
            Assert.Equal(10, log.SoldQuantity);
            Assert.Equal(90.5, log.OverrideSaleRate);
        });
    }

    [Fact]
    public async Task Compatibility_still_deduplicates_identical_sales_in_the_same_shift()
    {
        using var database = await DisposableOilDatabase.CreateAsync();
        var productId = await database.AddProductAsync();
        var shift = await database.AddShiftAsync("A");
        var canonicalId = await database.AddSaleAsync(productId, shift, 10, 90.5, 123.45);
        await database.AddSaleAsync(productId, shift, 10, 90.5, 123.45);

        database.RunCompatibility();

        using var context = database.CreateContext();
        var log = Assert.Single(await context.OilDefDailyLogs.ToListAsync());
        Assert.Equal(canonicalId, log.Id);
        Assert.Equal(123.45, log.RemainingStock);
    }

    [Fact]
    public async Task Compatibility_preserves_legacy_null_shift_duplicate_contract()
    {
        using var database = await DisposableOilDatabase.CreateAsync();
        var productId = await database.AddProductAsync();
        var canonicalId = await database.AddSaleAsync(productId, null, 10, 90.5, 123.45);
        await database.AddSaleAsync(productId, null, 10, 90.5, 123.45);

        database.RunCompatibility();

        using var context = database.CreateContext();
        var log = Assert.Single(await context.OilDefDailyLogs.ToListAsync());
        Assert.Equal(canonicalId, log.Id);
        Assert.Null(log.ShiftId);
    }

    [Fact]
    public async Task Compatibility_does_not_merge_legacy_null_and_populated_shift_sales()
    {
        using var database = await DisposableOilDatabase.CreateAsync();
        var productId = await database.AddProductAsync();
        var shift = await database.AddShiftAsync("A");
        var legacyId = await database.AddSaleAsync(productId, null, 10, 90.5, 123.45);
        var assignedId = await database.AddSaleAsync(productId, shift, 10, 90.5, 123.45);

        database.RunCompatibility();

        using var context = database.CreateContext();
        var logs = await context.OilDefDailyLogs.OrderBy(l => l.Id).ToListAsync();
        Assert.Equal(new[] { legacyId, assignedId }, logs.Select(l => l.Id));
        Assert.Null(logs[0].ShiftId);
        Assert.Equal(shift, logs[1].ShiftId);
    }

    [Fact]
    public async Task Compatibility_does_not_recalculate_existing_remaining_stock()
    {
        using var database = await DisposableOilDatabase.CreateAsync();
        var productId = await database.AddProductAsync();
        var id = await database.AddSaleAsync(productId, null, 5, 90.5, 95);

        database.RunCompatibility();

        using var context = database.CreateContext();
        var log = await context.OilDefDailyLogs.SingleAsync(l => l.Id == id);
        Assert.Equal(95, log.RemainingStock);
    }

    [Fact]
    public async Task Compatibility_repoints_oil_logs_before_deleting_duplicate_shift_parent()
    {
        using var database = await DisposableOilDatabase.CreateAsync();
        var productId = await database.AddProductAsync();
        var canonicalShiftId = await database.AddShiftAsync("A");
        var duplicateShiftId = await database.AddShiftAsync("A");
        var logId = await database.AddSaleAsync(productId, duplicateShiftId, 4, 90.5, 96);
        var repaymentId = await database.AddDsmRepaymentAsync(duplicateShiftId);

        database.RunCompatibility();

        using var context = database.CreateContext();
        var log = await context.OilDefDailyLogs.SingleAsync(l => l.Id == logId);
        var repayment = await context.DsmPersonalDebtorRepayments.SingleAsync(r => r.Id == repaymentId);
        Assert.Equal(canonicalShiftId, log.ShiftId);
        Assert.Equal(canonicalShiftId, repayment.ShiftId);
        Assert.DoesNotContain(await context.Shifts.Select(s => s.ShiftId).ToListAsync(), id => id == duplicateShiftId);
    }

    [Fact]
    public async Task Compatibility_adds_missing_petty_cash_shift_column_without_dropping_rows()
    {
        using var database = await DisposableOilDatabase.CreateAsync();
        using (var connection = new SqliteConnection($"Data Source={database.DbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                DROP TABLE PettyCashTransactions;
                CREATE TABLE PettyCashTransactions (
                    TransactionId INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    Date TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    Amount REAL NOT NULL,
                    Type TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
                INSERT INTO PettyCashTransactions (Date, Description, Amount, Type, CreatedAt)
                VALUES ('2026-10-01', 'Existing row', 25, 'Expense', '2026-10-01');";
            command.ExecuteNonQuery();
        }

        database.RunCompatibility();

        using var context = database.CreateContext();
        var transaction = await context.PettyCashTransactions.SingleAsync();
        Assert.Equal("Existing row", transaction.Description);
        Assert.Equal(25, transaction.Amount);
        Assert.Null(transaction.ShiftExpenseId);
    }

    [Fact]
    public async Task Compatibility_keeps_identical_dsm_repayments_from_different_shifts()
    {
        using var database = await DisposableOilDatabase.CreateAsync();
        var shiftA = await database.AddShiftAsync("A");
        var shiftB = await database.AddShiftAsync("B");
        var debtorId = await database.AddDsmDebtorAsync();
        var repaymentA = await database.AddDsmRepaymentAsync(debtorId, shiftA);
        var repaymentB = await database.AddDsmRepaymentAsync(debtorId, shiftB);

        database.RunCompatibility();

        using var context = database.CreateContext();
        var repayments = await context.DsmPersonalDebtorRepayments.OrderBy(r => r.Id).ToListAsync();
        Assert.Equal(new[] { repaymentA, repaymentB }, repayments.Select(r => r.Id));
        Assert.Equal(new int?[] { shiftA, shiftB }, repayments.Select(r => r.ShiftId));
    }

    [Fact]
    public void BackupService_refreshes_same_day_backup_with_current_pre_migration_database()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FuelPro_BackupSafety_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var databasePath = Path.Combine(directory, "source.db");
            var backupDirectory = Path.Combine(directory, "backups");
            CreateBackupTestDatabase(databasePath, "before migration");
            var backupService = new BackupService(databasePath, backupDirectory);

            Assert.True(backupService.PerformBackup());
            using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "UPDATE BackupPayload SET Value = 'latest before migration';";
                command.ExecuteNonQuery();
            }
            Assert.True(backupService.PerformBackup());

            var backupPath = Path.Combine(backupDirectory, $"fuelPro_{DateTime.Now:yyyyMMdd}.db");
            Assert.Equal("latest before migration", ReadBackupTestDatabase(backupPath));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void BackupService_includes_committed_rows_still_in_source_wal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FuelPro_BackupWalSafety_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var databasePath = Path.Combine(directory, "source.db");
            var backupDirectory = Path.Combine(directory, "backups");
            using var source = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            source.Open();
            using (var setup = source.CreateCommand())
            {
                setup.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE BackupPayload (Value TEXT NOT NULL);";
                setup.ExecuteNonQuery();
            }
            using (var insert = source.CreateCommand())
            {
                insert.CommandText = "INSERT INTO BackupPayload (Value) VALUES ('committed in WAL');";
                insert.ExecuteNonQuery();
            }

            Assert.True(File.Exists(databasePath + "-wal"));
            Assert.True(new BackupService(databasePath, backupDirectory).PerformBackup());

            var backupPath = Path.Combine(backupDirectory, $"fuelPro_{DateTime.Now:yyyyMMdd}.db");
            Assert.Equal("committed in WAL", ReadBackupTestDatabase(backupPath));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void BackupService_reports_failure_when_backup_cannot_be_created()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FuelPro_BackupFailure_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var databasePath = Path.Combine(directory, "source.db");
            var backupDirectory = Path.Combine(directory, "backup-blocker");
            File.WriteAllText(databasePath, "database");
            File.WriteAllText(backupDirectory, "not a directory");

            var backupService = new BackupService(databasePath, backupDirectory);

            Assert.False(backupService.PerformBackup());
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    private sealed class DisposableOilDatabase : IDisposable
    {
        private readonly string _directory;
        private readonly string _path;
        private readonly DbContextOptions<FuelProDbContext> _options;
        public string DbPath => _path;

        private DisposableOilDatabase(string directory, string path)
        {
            _directory = directory;
            _path = path;
            _options = new DbContextOptionsBuilder<FuelProDbContext>()
                .UseSqlite($"Data Source={path}")
                .Options;
        }

        public static async Task<DisposableOilDatabase> CreateAsync()
        {
            var directory = Path.Combine(Path.GetTempPath(), "FuelPro_StartupOilSafety_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var database = new DisposableOilDatabase(directory, Path.Combine(directory, "test.db"));
            using var context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public FuelProDbContext CreateContext() => new(_options);

        public async Task<int> AddProductAsync()
        {
            using var context = CreateContext();
            var product = new ProductMaster { ProductName = "Test Oil", Category = "Oil", Unit = "Litre", DefaultSaleRate = 90.5 };
            context.ProductMasters.Add(product);
            await context.SaveChangesAsync();
            return product.Id;
        }

        public async Task<int> AddShiftAsync(string type)
        {
            using var context = CreateContext();
            var shift = new Shift { ShiftDate = new DateTime(2026, 10, 1), ShiftType = type };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();
            return shift.ShiftId;
        }

        public async Task<int> AddSaleAsync(int productId, int? shiftId, double quantity, double rate, double remainingStock)
        {
            using var context = CreateContext();
            var log = new OilDefDailyLog
            {
                LogDate = new DateTime(2026, 10, 1),
                ProductType = "Oil",
                ProductId = productId,
                ShiftId = shiftId,
                SoldQuantity = quantity,
                OverrideSaleRate = rate,
                RemainingStock = remainingStock
            };
            context.OilDefDailyLogs.Add(log);
            await context.SaveChangesAsync();
            return log.Id;
        }

        public async Task<int> AddDsmRepaymentAsync(int shiftId)
        {
            var debtorId = await AddDsmDebtorAsync();
            return await AddDsmRepaymentAsync(debtorId, shiftId);
        }

        public async Task<int> AddDsmDebtorAsync()
        {
            using var context = CreateContext();
            var debtor = new DsmPersonalDebtor
            {
                DsmName = "Test DSM",
                Date = new DateTime(2026, 10, 1),
                Time = "10:00",
                PaymentMethod = "Cash"
            };
            context.DsmPersonalDebtors.Add(debtor);
            await context.SaveChangesAsync();
            return debtor.Id;
        }

        public async Task<int> AddDsmRepaymentAsync(int debtorId, int shiftId)
        {
            using var context = CreateContext();
            var repayment = new DsmPersonalDebtorRepayment
            {
                DsmPersonalDebtorId = debtorId,
                ShiftId = shiftId,
                Date = new DateTime(2026, 10, 1),
                Amount = 10,
                PaymentMethod = "Cash",
                Source = "ManagerShiftTotal"
            };
            context.DsmPersonalDebtorRepayments.Add(repayment);
            await context.SaveChangesAsync();
            return repayment.Id;
        }

        public void RunCompatibility() => FuelPro.UI.App.EnsureLegacyDatabaseCompatibility(_path);

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); } catch { }
        }
    }

    private static void CreateBackupTestDatabase(string path, string value)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE BackupPayload (Value TEXT NOT NULL); INSERT INTO BackupPayload (Value) VALUES ($value);";
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static string ReadBackupTestDatabase(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM BackupPayload;";
        return (string)command.ExecuteScalar()!;
    }
}
