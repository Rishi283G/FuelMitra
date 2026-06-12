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
            await SeedData.InitializeAsync(context);
            
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

        // AGS tables are now handled via EF Core Migrations.
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
        
        // ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<DsmEntryViewModel>();
        services.AddTransient<FinalCalculationViewModel>();
        services.AddTransient<DayTotalViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AgsImportViewModel>();

        // Licensing
        services.AddSingleton(new Rashtra.Licensing.LicenseManager("FPL", "FuelProLite"));
        services.AddTransient<ActivationViewModel>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down");
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
