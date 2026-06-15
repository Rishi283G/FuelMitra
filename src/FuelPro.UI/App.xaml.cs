using System.IO;
using System.Windows;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using FuelPro.Data;
using FuelPro.Data.AppMigration;
using FuelPro.Core.Repositories;
using FuelPro.Data.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data.Services;
using FuelPro.UI.ViewModels;
using FuelPro.UI.Printing;
using Serilog;

namespace FuelPro.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static string DbPath { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Setup Serilog
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FuelPro", "logs", "app-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
            .CreateLogger();

        // Global exception handlers
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            Log.Fatal(args.ExceptionObject as Exception, "FATAL UNHANDLED EXCEPTION");
            Log.CloseAndFlush();
            MessageBox.Show("A critical error occurred. The application will restart.\n\nPlease check the logs for details.",
                "FuelPro — Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            Log.Error(args.Exception, "Unhandled UI Exception");
            MessageBox.Show($"An error occurred: {args.Exception.Message}\n\nThe error has been logged.",
                "FuelPro — Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        // Setup database path
        DbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FuelPro", "fuelPro.db");
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);

        // Extract and copy default logos to LocalApplicationData if missing
        CopyDefaultLogosToDisk();

        // Backup database
        var backupService = new BackupService(DbPath);
        backupService.PerformBackup();

        // Setup DI
        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);
        Services = serviceCollection.BuildServiceProvider();

        // Run migrations and seed data
        try
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var credentialService = scope.ServiceProvider.GetRequiredService<ICredentialFileService>();
            await SeedData.InitializeAsync(context, credentialService);
            
            EnsureLegacyDatabaseCompatibility();
            
            var recalcMigration = scope.ServiceProvider.GetRequiredService<RecalculationMigrationService>();
            await recalcMigration.RunIfNeededAsync();
            Log.Information("Database initialized at {DbPath}", DbPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize database");
            MessageBox.Show($"Database initialization failed: {ex.Message}",
                "FuelPro — Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // Start Cloud Sync Engine
        try
        {
            var syncEngine = Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
            syncEngine.Start();
            Log.Information("Sync Engine started successfully on startup");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start Sync Engine on startup");
        }

        // Validate License
        var licenseManager = Services.GetRequiredService<Rashtra.Licensing.LicenseManager>();
        var validationResult = licenseManager.ValidateLicense();
        if (validationResult.IsValid)
        {
            Log.Information("License is valid for client {CustomerName}", validationResult.License?.CustomerName);
            var loginView = new Views.LoginView();
            loginView.Show();
        }
        else
        {
            Log.Warning("License is invalid or missing: {ErrorMessage}", validationResult.ErrorMessage);
            var activationWindow = new Views.ActivationWindow();
            activationWindow.Show();
        }

        base.OnStartup(e);
    }

    private static void EnsureLegacyDatabaseCompatibility()
    {
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();

        // Guard: skip column additions for tables that don't exist yet (e.g. fresh install)
        if (!TableExists(connection, "PaymentCollections") || !TableExists(connection, "DsmEntries"))
            return;

        EnsureColumnExists(connection, "PaymentCollections", "CashDeposit", "ALTER TABLE PaymentCollections ADD COLUMN CashDeposit REAL NOT NULL DEFAULT 0.0;");
        EnsureColumnExists(connection, "DsmEntries", "ConnectedPumpId", "ALTER TABLE DsmEntries ADD COLUMN ConnectedPumpId INTEGER NULL;");
        EnsureColumnExists(connection, "DsmEntries", "ReconciledToPumpId", "ALTER TABLE DsmEntries ADD COLUMN ReconciledToPumpId INTEGER NULL;");

        using var cmd = connection.CreateCommand();

        // Check if ShiftOtherCash exists
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ShiftOtherCash'";
        var otherCashExists = cmd.ExecuteScalar() != null;
        if (!otherCashExists)
        {
            cmd.CommandText = @"
                CREATE TABLE ""ShiftOtherCash"" (
                    ""ShiftOtherCashId"" INTEGER NOT NULL CONSTRAINT ""PK_ShiftOtherCash"" PRIMARY KEY AUTOINCREMENT,
                    ""ShiftId"" INTEGER NULL,
                    ""ShiftDate"" TEXT NOT NULL,
                    ""ShiftNumber"" TEXT NOT NULL,
                    ""Description"" TEXT NOT NULL,
                    ""Amount"" REAL NOT NULL,
                    ""IsEditable"" INTEGER NOT NULL DEFAULT 1,
                    ""CreatedAt"" TEXT NOT NULL,
                    CONSTRAINT ""FK_ShiftOtherCash_Shifts_ShiftId"" FOREIGN KEY (""ShiftId"") REFERENCES ""Shifts"" (""ShiftId"") ON DELETE SET NULL
                );";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE INDEX ""IX_ShiftOtherCash_ShiftDate_ShiftNumber"" ON ""ShiftOtherCash"" (""ShiftDate"", ""ShiftNumber"");
                CREATE INDEX ""IX_ShiftOtherCash_ShiftId"" ON ""ShiftOtherCash"" (""ShiftId"");
            ";
            cmd.ExecuteNonQuery();
            Log.Information("Created ShiftOtherCash table");
        }

        // Check if ShiftFuelRates exists
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ShiftFuelRates'";
        var fuelRatesExists = cmd.ExecuteScalar() != null;
        if (!fuelRatesExists)
        {
            cmd.CommandText = @"
                CREATE TABLE ""ShiftFuelRates"" (
                    ""ShiftFuelRateId"" INTEGER NOT NULL CONSTRAINT ""PK_ShiftFuelRates"" PRIMARY KEY AUTOINCREMENT,
                    ""ShiftId"" INTEGER NULL,
                    ""ShiftDate"" TEXT NOT NULL,
                    ""ShiftNumber"" TEXT NOT NULL,
                    ""FuelType"" TEXT NOT NULL,
                    ""OverrideRate"" REAL NOT NULL,
                    CONSTRAINT ""FK_ShiftFuelRates_Shifts_ShiftId"" FOREIGN KEY (""ShiftId"") REFERENCES ""Shifts"" (""ShiftId"") ON DELETE SET NULL
                );";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE UNIQUE INDEX ""IX_ShiftFuelRates_ShiftDate_ShiftNumber_FuelType"" ON ""ShiftFuelRates"" (""ShiftDate"", ""ShiftNumber"", ""FuelType"");
                CREATE INDEX ""IX_ShiftFuelRates_ShiftId"" ON ""ShiftFuelRates"" (""ShiftId"");
            ";
            cmd.ExecuteNonQuery();
            Log.Information("Created ShiftFuelRates table");
        }

        // Feature 1: Creditors Table
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Creditors'";
        var creditorsExists = cmd.ExecuteScalar() != null;
        if (!creditorsExists)
        {
            cmd.CommandText = @"
                CREATE TABLE ""Creditors"" (
                    ""CreditorId"" INTEGER NOT NULL CONSTRAINT ""PK_Creditors"" PRIMARY KEY AUTOINCREMENT,
                    ""Name"" TEXT NOT NULL,
                    ""Phone"" TEXT,
                    ""IsActive"" INTEGER NOT NULL DEFAULT 1,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT (datetime('now'))
                );";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE UNIQUE INDEX ""IX_Creditors_Name"" ON ""Creditors"" (""Name"");
            ";
            cmd.ExecuteNonQuery();
            Log.Information("Created Creditors table");
        }

        // Feature 4: PhonePe Split
        EnsureColumnExists(connection, "PaymentCollections", "PhonePeMorning", "ALTER TABLE PaymentCollections ADD COLUMN PhonePeMorning REAL NOT NULL DEFAULT 0.0");
        EnsureColumnExists(connection, "PaymentCollections", "PhonePeNight", "ALTER TABLE PaymentCollections ADD COLUMN PhonePeNight REAL NOT NULL DEFAULT 0.0");
        
        // Note: PhonePe was always a [NotMapped] computed property, never a DB column.
        // No data migration needed for PhonePe split.

        // Feature 5: PhonePe Card Split
        EnsureColumnExists(connection, "PaymentCollections", "PhonePeCardMorning", "ALTER TABLE PaymentCollections ADD COLUMN PhonePeCardMorning REAL NOT NULL DEFAULT 0.0");
        EnsureColumnExists(connection, "PaymentCollections", "PhonePeCardNight", "ALTER TABLE PaymentCollections ADD COLUMN PhonePeCardNight REAL NOT NULL DEFAULT 0.0");
        
        // Note: PhonePeCard was always a [NotMapped] computed property, never a DB column.
        // No data migration needed for PhonePeCard split.

        // Feature 3: IsReconciled for Creditor Return Tracker
        EnsureColumnExists(connection, "DsmEntries", "IsReconciled", "ALTER TABLE DsmEntries ADD COLUMN IsReconciled INTEGER NOT NULL DEFAULT 0");

        // Cloud Sync: SyncChangeLogs Table
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='SyncChangeLogs'";
        var syncChangeLogsExists = cmd.ExecuteScalar() != null;
        if (!syncChangeLogsExists)
        {
            cmd.CommandText = @"
                CREATE TABLE ""SyncChangeLogs"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SyncChangeLogs"" PRIMARY KEY AUTOINCREMENT,
                    ""TableName"" TEXT NOT NULL,
                    ""RecordId"" INTEGER NOT NULL,
                    ""Operation"" TEXT NOT NULL,
                    ""CreatedAt"" TEXT NOT NULL,
                    ""IsSynced"" INTEGER NOT NULL DEFAULT 0,
                    ""SyncedAt"" TEXT NULL,
                    ""StationId"" TEXT NULL,
                    ""MachineId"" TEXT NULL
                );";
            cmd.ExecuteNonQuery();
            Log.Information("Created SyncChangeLogs table with StationId and MachineId columns");
        }
        else
        {
            EnsureColumnExists(connection, "SyncChangeLogs", "StationId", "ALTER TABLE SyncChangeLogs ADD COLUMN StationId TEXT NULL;");
            EnsureColumnExists(connection, "SyncChangeLogs", "MachineId", "ALTER TABLE SyncChangeLogs ADD COLUMN MachineId TEXT NULL;");
        }

        // Owner Dashboard Expansion columns and tables
        EnsureColumnExists(connection, "DsmProfiles", "SalaryType", "ALTER TABLE DsmProfiles ADD COLUMN SalaryType TEXT NOT NULL DEFAULT 'FixedMonthly';");
        EnsureColumnExists(connection, "DsmProfiles", "BaseSalary", "ALTER TABLE DsmProfiles ADD COLUMN BaseSalary REAL NOT NULL DEFAULT 12000.0;");
        EnsureColumnExists(connection, "DsmProfiles", "JoiningDate", "ALTER TABLE DsmProfiles ADD COLUMN JoiningDate TEXT NULL;");

        // FuelProfitMargins
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='FuelProfitMargins'";
        var fuelMarginsExists = cmd.ExecuteScalar() != null;
        if (!fuelMarginsExists)
        {
            cmd.CommandText = @"
                CREATE TABLE ""FuelProfitMargins"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_FuelProfitMargins"" PRIMARY KEY AUTOINCREMENT,
                    ""EffectiveDate"" TEXT NOT NULL,
                    ""FuelType"" TEXT NOT NULL,
                    ""MarginPerLitre"" REAL NOT NULL
                );";
            cmd.ExecuteNonQuery();
            Log.Information("Created FuelProfitMargins table");
        }

        // Seed default margins if table is empty
        cmd.CommandText = "SELECT COUNT(*) FROM FuelProfitMargins";
        var marginCount = Convert.ToInt32(cmd.ExecuteScalar());
        if (marginCount == 0)
        {
            cmd.CommandText = @"
                INSERT INTO FuelProfitMargins (EffectiveDate, FuelType, MarginPerLitre) VALUES ('2026-01-01 00:00:00', 'HSD', 3.0);
                INSERT INTO FuelProfitMargins (EffectiveDate, FuelType, MarginPerLitre) VALUES ('2026-01-01 00:00:00', 'MS-I', 4.0);
                INSERT INTO FuelProfitMargins (EffectiveDate, FuelType, MarginPerLitre) VALUES ('2026-01-01 00:00:00', 'MS-II', 4.0);
            ";
            cmd.ExecuteNonQuery();
            Log.Information("Seeded default fuel profit margins");
        }

        // OilDefInventories
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='OilDefInventories'";
        var oilDefInventoriesExists = cmd.ExecuteScalar() != null;
        if (!oilDefInventoriesExists)
        {
            cmd.CommandText = @"
                CREATE TABLE ""OilDefInventories"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_OilDefInventories"" PRIMARY KEY AUTOINCREMENT,
                    ""Year"" INTEGER NOT NULL,
                    ""Month"" INTEGER NOT NULL,
                    ""ProductType"" TEXT NOT NULL,
                    ""OpeningStock"" REAL NOT NULL,
                    ""ClosingStock"" REAL NOT NULL,
                    ""SalePrice"" REAL NOT NULL
                );";
            cmd.ExecuteNonQuery();
            Log.Information("Created OilDefInventories table");
        }

        // OilDefPurchases
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='OilDefPurchases'";
        var oilDefPurchasesExists = cmd.ExecuteScalar() != null;
        if (!oilDefPurchasesExists)
        {
            cmd.CommandText = @"
                CREATE TABLE ""OilDefPurchases"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_OilDefPurchases"" PRIMARY KEY AUTOINCREMENT,
                    ""ProductType"" TEXT NOT NULL,
                    ""SupplierName"" TEXT NOT NULL,
                    ""InvoiceNumber"" TEXT NOT NULL,
                    ""PurchaseDate"" TEXT NOT NULL,
                    ""Quantity"" REAL NOT NULL,
                    ""UnitPrice"" REAL NOT NULL,
                    ""TotalCost"" REAL NOT NULL
                );";
            cmd.ExecuteNonQuery();
            Log.Information("Created OilDefPurchases table");
        }

        // DsmSalaryAdjustments
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='DsmSalaryAdjustments'";
        var dsmSalaryAdjustmentsExists = cmd.ExecuteScalar() != null;
        if (!dsmSalaryAdjustmentsExists)
        {
            cmd.CommandText = @"
                CREATE TABLE ""DsmSalaryAdjustments"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_DsmSalaryAdjustments"" PRIMARY KEY AUTOINCREMENT,
                    ""DsmName"" TEXT NOT NULL,
                    ""Year"" INTEGER NOT NULL,
                    ""Month"" INTEGER NOT NULL,
                    ""AdvancePaid"" REAL NOT NULL,
                    ""OtherAdjustments"" REAL NOT NULL,
                    ""Remarks"" TEXT NOT NULL
                );";
            cmd.ExecuteNonQuery();
            Log.Information("Created DsmSalaryAdjustments table");
        }

        // OilDefDailyLogs
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='OilDefDailyLogs'";
        var oilDefDailyLogsExists = cmd.ExecuteScalar() != null;
        if (!oilDefDailyLogsExists)
        {
            cmd.CommandText = @"
                CREATE TABLE ""OilDefDailyLogs"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_OilDefDailyLogs"" PRIMARY KEY AUTOINCREMENT,
                    ""LogDate"" TEXT NOT NULL,
                    ""ProductType"" TEXT NOT NULL,
                    ""AddedQuantity"" REAL NOT NULL,
                    ""SoldQuantity"" REAL NOT NULL,
                    ""RemainingStock"" REAL NOT NULL,
                    ""Remarks"" TEXT NULL
                );";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE INDEX ""IX_OilDefDailyLogs_LogDate"" ON ""OilDefDailyLogs"" (""LogDate"");
            ";
            cmd.ExecuteNonQuery();
            Log.Information("Created OilDefDailyLogs table");
        }

        // AGS tables are now handled via EF Core Migrations.
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@name";
        cmd.Parameters.AddWithValue("@name", tableName);
        return cmd.ExecuteScalar() != null;
    }

    private static void EnsureColumnExists(SqliteConnection connection, string tableName, string columnName, string alterSql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName});";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = alterSql;
        alterCommand.ExecuteNonQuery();
        Log.Information("Added missing legacy column {ColumnName} to {TableName}", columnName, tableName);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Database
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={DbPath}"),
            ServiceLifetime.Transient);

        // Credentials
        services.AddSingleton<ICredentialFileService, LocalCredentialFileService>();

        // Repositories
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

        // Services
        services.AddSingleton<AuthService>();
        services.AddTransient<DsmEntryService>();
        services.AddTransient<ShiftCalculationService>();
        services.AddSingleton<IDsmCalculationService, DsmCalculationService>();
        services.AddSingleton<IOwnerCalculationService, OwnerCalculationService>();
        services.AddTransient<IFinancialCalculationService, FinancialCalculationService>();
        services.AddTransient<RecalculationMigrationService>();
        services.AddSingleton<DraftService>();
        services.AddTransient<ExportService>();
        services.AddScoped<IShiftAggregationService, ShiftAggregationService>();
        services.AddScoped<IShiftOtherCashRepository, ShiftOtherCashRepository>();
        services.AddScoped<IShiftFuelRateRepository, ShiftFuelRateRepository>();
        services.AddTransient<IAgsImportService, AgsImportService>();
        services.AddTransient<IAgsDailyAggregationService, AgsDailyAggregationService>();
        services.AddTransient<AgsImportValidator>();
        services.AddTransient<PrintService>();
        
        // Sync Services
        services.AddSingleton<FuelPro.Sync.SyncConfigService>();
        services.AddSingleton<FuelPro.Sync.SyncEngine>();
        
        // ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<DsmEntryViewModel>();
        services.AddTransient<FinalCalculationViewModel>();
        services.AddTransient<DayTotalViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AgsImportViewModel>();
        services.AddTransient<OilDefDailyLogViewModel>();

        // Owner ViewModels
        services.AddTransient<OwnerMainWindowViewModel>();
        services.AddTransient<OwnerDashboardViewModel>();
        services.AddTransient<DailyPerformanceViewModel>();
        services.AddTransient<MonthlyPerformanceViewModel>();
        services.AddTransient<ProfitLossViewModel>();
        services.AddTransient<DsmPerformanceViewModel>();
        services.AddTransient<SalaryCalculationViewModel>();
        services.AddTransient<OilDefInventoryViewModel>();
        services.AddTransient<OilDefSummaryViewModel>();
        services.AddTransient<ShortRecoveryViewModel>();
        services.AddTransient<CollectionSummaryViewModel>();
        services.AddTransient<FinancialSummaryViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<ExpenseAnalysisViewModel>();
        services.AddTransient<MismatchLedgerViewModel>();

        // Developer ViewModels
        services.AddTransient<DeveloperMainWindowViewModel>();

        // Licensing
        services.AddSingleton(new Rashtra.Licensing.LicenseManager("FPL", "PyroSync"));
        services.AddTransient<ActivationViewModel>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            var syncEngine = Services.GetService<FuelPro.Sync.SyncEngine>();
            syncEngine?.Stop();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error stopping Sync Engine during exit");
        }

        Log.Information("Application shutting down");
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void CopyDefaultLogosToDisk()
    {
        try
        {
            var appDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FuelPro");
            Directory.CreateDirectory(appDataFolder);

            var defaultLightLogoPath = Path.Combine(appDataFolder, "logo_light.png");
            var defaultDarkLogoPath = Path.Combine(appDataFolder, "logo_dark.png");

            void CopyLogo(string resourceName, string destPath)
            {
                if (!File.Exists(destPath))
                {
                    var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                    var resourcePath = assembly.GetManifestResourceNames()
                        .FirstOrDefault(n => n.EndsWith(resourceName));
                    if (resourcePath != null)
                    {
                        using var stream = assembly.GetManifestResourceStream(resourcePath);
                        if (stream != null)
                        {
                            using var fs = File.Create(destPath);
                            stream.CopyTo(fs);
                        }
                    }
                    else
                    {
                        // Fallback to WPF Resource Stream if it is compiled as WPF Resource
                        try
                        {
                            var uri = new Uri($"/Resources/{resourceName}", UriKind.Relative);
                            var streamInfo = System.Windows.Application.GetResourceStream(uri);
                            if (streamInfo != null)
                            {
                                using var fs = File.Create(destPath);
                                streamInfo.Stream.CopyTo(fs);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to copy logo via WPF resource stream: {ResourceName}", resourceName);
                        }
                    }
                }
            }

            CopyLogo("logo_light.png", defaultLightLogoPath);
            CopyLogo("logo_dark.png", defaultDarkLogoPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to copy default logos to disk");
        }
    }

    public static string GetLogoPath(bool isDark = false)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppMeta WHERE Key = @key";
            cmd.Parameters.AddWithValue("@key", isDark ? "ApplicationLogoDarkPath" : "ApplicationLogoPath");
            var path = cmd.ExecuteScalar()?.ToString();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                return path;
            }
        }
        catch { }

        // Fallback to local app data folder
        var appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro");
        var defaultPath = Path.Combine(appDataFolder, isDark ? "logo_dark.png" : "logo_light.png");
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        // Final fallback to pack URI
        return isDark ? "pack://application:,,,/Resources/logo_dark.png" : "pack://application:,,,/Resources/logo_light.png";
    }
}
