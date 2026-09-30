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
using FuelPro.Sync;
using FuelPro.Core.Models;
using Serilog;


namespace FuelPro.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static string DbPath { get; private set; } = null!;
    public static FuelPro.Core.Services.DataIntegrityReport? StartupIntegrityReport { get; set; }

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
            
            // Create legacy tables FIRST so SeedData can safely ALTER/query them
            EnsureLegacyDatabaseCompatibility(DbPath);
            
            try
            {
                await SeedData.InitializeAsync(context, credentialService);
            }
            catch (Exception seedEx)
            {
                Log.Warning(seedEx, "Non-fatal database initialization/seeding concurrency issue encountered. The database might have already been initialized by another concurrent instance.");
            }

            // Re-run compatibility check to ensure columns are added after EF Core creates the tables on fresh install
            EnsureLegacyDatabaseCompatibility(DbPath);

            // Ensure all 12 PumpMappings are queued for sync to Supabase under the current station ID.
            // IMPORTANT: Check ALL logs (both synced and unsynced) to avoid re-queuing already-synced
            // mappings with a new RecordGuid, which would create duplicate rows in Supabase.
            try
            {
                var settings = await scope.ServiceProvider.GetRequiredService<SyncConfigService>().GetSettingsAsync();
                var allMappings = await context.PumpMappings.ToListAsync();

                // Get IDs of mappings that have ANY log entry (synced OR pending) for this station
                var alreadyLoggedRecordIds = await context.SyncChangeLogs
                    .Where(l => l.TableName == "PumpMappings" && l.StationId == settings.StationId)
                    .Select(l => l.RecordId)
                    .Distinct()
                    .ToListAsync();

                // Load the stable SyncIdMappings GUIDs so upserts hit the conflict key correctly
                var idMappings = (await context.SyncIdMappings
                    .Where(m => m.TableName == "PumpMappings")
                    .ToListAsync())
                    .GroupBy(m => m.LocalId)
                    .ToDictionary(g => g.Key, g => g.First().RemoteGuid);

                var mappingsToQueue = allMappings.Where(m => !alreadyLoggedRecordIds.Contains(m.PumpMappingId)).ToList();
                if (mappingsToQueue.Any())
                {
                    foreach (var mapping in mappingsToQueue)
                    {
                        // Use the stable GUID from SyncIdMappings so the Supabase upsert on SyncGuid
                        // updates the existing row rather than inserting a second one.
                        var recordGuid = idMappings.TryGetValue(mapping.PumpMappingId, out var g) ? g : Guid.NewGuid().ToString();
                        var log = new SyncChangeLog
                        {
                            TableName = "PumpMappings",
                            RecordId = mapping.PumpMappingId,
                            Operation = "INSERT",
                            CreatedAt = DateTime.Now,
                            IsSynced = false,
                            SyncGuid = Guid.NewGuid().ToString(),
                            RecordGuid = recordGuid,
                            StationId = settings.StationId,
                            MachineId = settings.MachineId
                        };
                        context.SyncChangeLogs.Add(log);
                    }
                    await context.SaveChangesAsync();
                    Log.Information("Queued {Count} pump mappings for sync to Supabase under station {StationId}", mappingsToQueue.Count, settings.StationId);
                }

                // Also ensure Settings is queued for sync under the current StationId
                var settingEntity = await context.Settings.FirstOrDefaultAsync();
                if (settingEntity != null)
                {
                    var settingLogged = await context.SyncChangeLogs
                        .AnyAsync(l => l.TableName == "Settings" && l.StationId == settings.StationId);
                    if (!settingLogged)
                    {
                        var settingIdMappings = (await context.SyncIdMappings
                            .Where(m => m.TableName == "Settings")
                            .ToListAsync())
                            .GroupBy(m => m.LocalId)
                            .ToDictionary(g => g.Key, g => g.First().RemoteGuid);

                        var recordGuid = settingIdMappings.TryGetValue(settingEntity.SettingId, out var sg) ? sg : Guid.NewGuid().ToString();
                        context.SyncChangeLogs.Add(new SyncChangeLog
                        {
                            TableName = "Settings",
                            RecordId = settingEntity.SettingId,
                            Operation = "INSERT",
                            CreatedAt = DateTime.Now,
                            IsSynced = false,
                            SyncGuid = Guid.NewGuid().ToString(),
                            RecordGuid = recordGuid,
                            StationId = settings.StationId,
                            MachineId = settings.MachineId
                        });
                        await context.SaveChangesAsync();
                        Log.Information("Queued Settings for sync to Supabase under station {StationId}", settings.StationId);
                    }
                }
            }
            catch (Exception syncEx)
            {
                Log.Error(syncEx, "Failed to check and queue missing pump mappings for sync");
            }

            // Self-healing: Ensure all historical data is queued for sync under the current StationId
            try
            {
                var settings = await scope.ServiceProvider.GetRequiredService<SyncConfigService>().GetSettingsAsync();
                if (!string.IsNullOrEmpty(settings.StationId))
                {
                    var syncEngine = scope.ServiceProvider.GetRequiredService<FuelPro.Sync.SyncEngine>();
                    await syncEngine.QueueAllHistoricalRecordsForStationAsync(settings.StationId);
                }
            }
            catch (Exception histSyncEx)
            {
                Log.Error(histSyncEx, "Failed to run self-healing sync recovery for historical data");
            }

            // Initialize PumpConfiguration from Database
            var pumpMappings = await context.PumpMappings.ToListAsync();
            var allTanks = await context.TankDefinitions.ToListAsync();
            FuelPro.Core.Common.PumpConfiguration.InitializeFromDb(pumpMappings);
            FuelPro.Core.Common.PumpConfiguration.InitializeTanksFromDb(allTanks);
            Log.Information("Initialized PumpConfiguration with {Count} pump mappings and {TankCount} tanks from database", pumpMappings.Count, allTanks.Count);
            
            var recalcMigration = scope.ServiceProvider.GetRequiredService<RecalculationMigrationService>();
            await recalcMigration.RunIfNeededAsync();
            Log.Information("Database initialized at {DbPath}", DbPath);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to initialize/migrate database. Application will exit.");
            MessageBox.Show($"Database initialization or migration failed:\n\n{ex.Message}\n\nThe application will now close.",
                "FuelPro — Database Migration Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // Start Cloud Sync Engine and Polling Service
        try
        {
            var syncEngine = Services.GetRequiredService<FuelPro.Sync.SyncEngine>();
            syncEngine.Start();
            Log.Information("Sync Engine started successfully on startup");

            var pollingService = Services.GetRequiredService<FuelPro.Sync.DsmSubmissionPollingService>();
            pollingService.Start();
            Log.Information("DSM Submission Polling Service started successfully on startup");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start Sync Engine or Polling Service on startup");
        }

        // ── LICENSE VALIDATION ──
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

        // Run database integrity check (non-blocking, silent)
        try
        {
            using (var scope = Services.CreateScope())
            {
                var integrityService = scope.ServiceProvider.GetRequiredService<IDuplicateDataInspectionService>();
                var report = await integrityService.RunFullScanAsync();
                if (report.HasIssues)
                {
                    Log.Warning("Startup integrity scan found issues: DsmDuplicates={DsmDupes}, DebitDuplicates={DebitDupes}, Orphans={Orphans}",
                        report.DuplicateDsmEntries, report.DuplicateDebitEntries, report.OrphanRecords);
                    StartupIntegrityReport = report;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Non-fatal: startup integrity scan failed");
        }

        base.OnStartup(e);
    }

    public static void EnsureLegacyDatabaseCompatibility(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Default Timeout=5");
        connection.Open();

        using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA journal_mode=WAL;";
            pragmaCmd.ExecuteNonQuery();
        }

        // Self-heal Shifts table: drop UNIQUE constraint index and merge any duplicate shift records
        if (TableExists(connection, "Shifts"))
        {
            try
            {
                using var fixCmd = connection.CreateCommand();
                fixCmd.CommandText = @"
                    UPDATE DsmEntries
                    SET ShiftId = (
                        SELECT MIN(s_min.ShiftId)
                        FROM Shifts s_curr
                        JOIN Shifts s_min ON s_curr.ShiftDate = s_min.ShiftDate AND s_curr.ShiftType = s_min.ShiftType
                        WHERE s_curr.ShiftId = DsmEntries.ShiftId
                    )
                    WHERE ShiftId IS NOT NULL AND EXISTS (
                        SELECT 1 FROM Shifts s1
                        JOIN Shifts s2 ON s1.ShiftDate = s2.ShiftDate AND s1.ShiftType = s2.ShiftType AND s2.ShiftId < s1.ShiftId
                        WHERE s1.ShiftId = DsmEntries.ShiftId
                    );

                    UPDATE ShiftOtherCash
                    SET ShiftId = (
                        SELECT MIN(s_min.ShiftId)
                        FROM Shifts s_curr
                        JOIN Shifts s_min ON s_curr.ShiftDate = s_min.ShiftDate AND s_curr.ShiftType = s_min.ShiftType
                        WHERE s_curr.ShiftId = ShiftOtherCash.ShiftId
                    )
                    WHERE ShiftId IS NOT NULL AND EXISTS (
                        SELECT 1 FROM Shifts s1
                        JOIN Shifts s2 ON s1.ShiftDate = s2.ShiftDate AND s1.ShiftType = s2.ShiftType AND s2.ShiftId < s1.ShiftId
                        WHERE s1.ShiftId = ShiftOtherCash.ShiftId
                    );

                    UPDATE ShiftFuelRates
                    SET ShiftId = (
                        SELECT MIN(s_min.ShiftId)
                        FROM Shifts s_curr
                        JOIN Shifts s_min ON s_curr.ShiftDate = s_min.ShiftDate AND s_curr.ShiftType = s_min.ShiftType
                        WHERE s_curr.ShiftId = ShiftFuelRates.ShiftId
                    )
                    WHERE ShiftId IS NOT NULL AND EXISTS (
                        SELECT 1 FROM Shifts s1
                        JOIN Shifts s2 ON s1.ShiftDate = s2.ShiftDate AND s1.ShiftType = s2.ShiftType AND s2.ShiftId < s1.ShiftId
                        WHERE s1.ShiftId = ShiftFuelRates.ShiftId
                    );

                    UPDATE Expenses
                    SET ShiftId = (
                        SELECT MIN(s_min.ShiftId)
                        FROM Shifts s_curr
                        JOIN Shifts s_min ON s_curr.ShiftDate = s_min.ShiftDate AND s_curr.ShiftType = s_min.ShiftType
                        WHERE s_curr.ShiftId = Expenses.ShiftId
                    )
                    WHERE ShiftId IS NOT NULL AND EXISTS (
                        SELECT 1 FROM Shifts s1
                        JOIN Shifts s2 ON s1.ShiftDate = s2.ShiftDate AND s1.ShiftType = s2.ShiftType AND s2.ShiftId < s1.ShiftId
                        WHERE s1.ShiftId = Expenses.ShiftId
                    );

                    DELETE FROM Shifts
                    WHERE ShiftId IN (
                        SELECT s1.ShiftId
                        FROM Shifts s1
                        JOIN Shifts s2 ON s1.ShiftDate = s2.ShiftDate AND s1.ShiftType = s2.ShiftType AND s2.ShiftId < s1.ShiftId
                    );

                    DROP INDEX IF EXISTS IX_Shifts_ShiftDate_ShiftType;
                    CREATE INDEX IF NOT EXISTS IX_Shifts_ShiftDate_ShiftType ON Shifts (ShiftDate, ShiftType);
                ";
                fixCmd.ExecuteNonQuery();
                Log.Information("Successfully validated/healed Shifts table indices and merged duplicate shift records.");
            }
            catch (Exception shiftFixEx)
            {
                Log.Warning(shiftFixEx, "Non-fatal: Failed to self-heal duplicate Shifts records or drop unique index.");
            }
        }

        // Always ensure Creditors table exists since EF migrations assume it's there
        using (var cmdCred = connection.CreateCommand())
        {
            cmdCred.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""Creditors"" (
                    ""CreditorId"" INTEGER NOT NULL CONSTRAINT ""PK_Creditors"" PRIMARY KEY AUTOINCREMENT,
                    ""Name"" TEXT NOT NULL,
                    ""Phone"" TEXT NULL,
                    ""IsActive"" INTEGER NOT NULL DEFAULT 1,
                    ""CreatedAt"" TEXT NOT NULL
                );";
            cmdCred.ExecuteNonQuery();
        }

        // Always ensure ShiftOtherCash table exists since EF migrations assume it's there
        using (var cmdOther = connection.CreateCommand())
        {
            cmdOther.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""ShiftOtherCash"" (
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
            cmdOther.ExecuteNonQuery();
        }

        // Dynamic Configuration tables
        using (var cmdDyn = connection.CreateCommand())
        {
            cmdDyn.CommandText = @"
                CREATE TABLE IF NOT EXISTS ""AppFeatureSettings"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_AppFeatureSettings"" PRIMARY KEY AUTOINCREMENT,
                    ""FeatureKey"" TEXT NOT NULL,
                    ""TargetRole"" TEXT NOT NULL DEFAULT 'Global',
                    ""DisplayName"" TEXT NOT NULL DEFAULT '',
                    ""Description"" TEXT NOT NULL DEFAULT '',
                    ""Category"" TEXT NOT NULL DEFAULT 'General',
                    ""IsEnabled"" INTEGER NOT NULL DEFAULT 1,
                    ""DisplayOrder"" INTEGER NOT NULL DEFAULT 0,
                    ""ConfigurationJson"" TEXT NULL,
                    ""UpdatedAt"" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AppFeatureSettings_FeatureKey"" ON ""AppFeatureSettings"" (""FeatureKey"");

                CREATE TABLE IF NOT EXISTS ""CollectionTypes"" (
                    ""CollectionTypeId"" INTEGER NOT NULL CONSTRAINT ""PK_CollectionTypes"" PRIMARY KEY AUTOINCREMENT,
                    ""Code"" TEXT NOT NULL,
                    ""DisplayName"" TEXT NOT NULL DEFAULT '',
                    ""Category"" TEXT NOT NULL DEFAULT 'Online',
                    ""HasTidBatch"" INTEGER NOT NULL DEFAULT 0,
                    ""DisplayOrder"" INTEGER NOT NULL DEFAULT 0,
                    ""IsActive"" INTEGER NOT NULL DEFAULT 1,
                    ""IsSystem"" INTEGER NOT NULL DEFAULT 0,
                    ""CreatedAt"" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CollectionTypes_Code"" ON ""CollectionTypes"" (""Code"");

                CREATE TABLE IF NOT EXISTS ""PaymentCollectionItems"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PaymentCollectionItems"" PRIMARY KEY AUTOINCREMENT,
                    ""PaymentId"" INTEGER NOT NULL,
                    ""CollectionTypeCode"" TEXT NOT NULL,
                    ""Amount"" REAL NOT NULL DEFAULT 0.0,
                    ""Tid"" TEXT NULL,
                    ""Batch"" TEXT NULL,
                    ""Slot"" TEXT NULL DEFAULT 'General',
                    CONSTRAINT ""FK_PaymentCollectionItems_PaymentCollections_PaymentId"" FOREIGN KEY (""PaymentId"") REFERENCES ""PaymentCollections"" (""PaymentId"") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS ""IX_PaymentCollectionItems_PaymentId"" ON ""PaymentCollectionItems"" (""PaymentId"");

                CREATE TABLE IF NOT EXISTS ""TankDefinitions"" (
                    ""TankId"" INTEGER NOT NULL CONSTRAINT ""PK_TankDefinitions"" PRIMARY KEY AUTOINCREMENT,
                    ""TankName"" TEXT NOT NULL,
                    ""CapacityKL"" REAL NOT NULL DEFAULT 20.0,
                    ""FuelType"" TEXT NOT NULL DEFAULT 'HSD',
                    ""IsActive"" INTEGER NOT NULL DEFAULT 1,
                    ""HasTesting"" INTEGER NOT NULL DEFAULT 1,
                    ""CreatedAt"" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_TankDefinitions_TankName"" ON ""TankDefinitions"" (""TankName"");
            ";
            cmdDyn.ExecuteNonQuery();
        }

        if (TableExists(connection, "TankDefinitions"))
        {
            EnsureColumnExists(connection, "TankDefinitions", "HasTesting", "ALTER TABLE TankDefinitions ADD COLUMN HasTesting INTEGER NOT NULL DEFAULT 1;");
            try
            {
                using var cmdCng = connection.CreateCommand();
                cmdCng.CommandText = "UPDATE TankDefinitions SET HasTesting = 0 WHERE (FuelType = 'CNG' OR TankName LIKE '%CNG%') AND HasTesting = 1;";
                cmdCng.ExecuteNonQuery();
            }
            catch { }
        }

        // DsmPumpAssignments columns — must run before the guard since SyncValidationTest calls this
        // before EF migrations create PaymentCollections/DsmEntries tables.
        if (TableExists(connection, "DsmPumpAssignments"))
        {
            EnsureColumnExists(connection, "DsmPumpAssignments", "ConnectedPumpId", "ALTER TABLE DsmPumpAssignments ADD COLUMN ConnectedPumpId INTEGER NULL;");
            EnsureColumnExists(connection, "DsmPumpAssignments", "CompletedDate", "ALTER TABLE DsmPumpAssignments ADD COLUMN CompletedDate TEXT NULL;");
        }

        // Settings Manager & Dynamic Config JSON columns
        if (TableExists(connection, "Settings"))
        {
            EnsureColumnExists(connection, "Settings", "Shift1Manager", "ALTER TABLE Settings ADD COLUMN Shift1Manager TEXT NULL;");
            EnsureColumnExists(connection, "Settings", "Shift2Manager", "ALTER TABLE Settings ADD COLUMN Shift2Manager TEXT NULL;");
            EnsureColumnExists(connection, "Settings", "Shift3Manager", "ALTER TABLE Settings ADD COLUMN Shift3Manager TEXT NULL;");
            EnsureColumnExists(connection, "Settings", "FuelRatesJson", "ALTER TABLE Settings ADD COLUMN FuelRatesJson TEXT NULL;");
            EnsureColumnExists(connection, "Settings", "TankDefinitionsJson", "ALTER TABLE Settings ADD COLUMN TankDefinitionsJson TEXT NULL;");
            EnsureColumnExists(connection, "Settings", "CollectionTypesJson", "ALTER TABLE Settings ADD COLUMN CollectionTypesJson TEXT NULL;");
            EnsureColumnExists(connection, "Settings", "AppFeatureSettingsJson", "ALTER TABLE Settings ADD COLUMN AppFeatureSettingsJson TEXT NULL;");
            EnsureColumnExists(connection, "Settings", "PumpMappingsJson", "ALTER TABLE Settings ADD COLUMN PumpMappingsJson TEXT NULL;");
            EnsureColumnExists(connection, "Settings", "PumpConnectionRulesJson", "ALTER TABLE Settings ADD COLUMN PumpConnectionRulesJson TEXT NULL;");
        }

        // ─── OpeningBalances Table & DsmPersonalDebtors EntryType (Independent of PaymentCollections) ─────
        if (!TableExists(connection, "OpeningBalances"))
        {
            using var cmdOb = connection.CreateCommand();
            cmdOb.CommandText = @"
                CREATE TABLE ""OpeningBalances"" (
                    ""OpeningBalanceId"" INTEGER NOT NULL CONSTRAINT ""PK_OpeningBalances"" PRIMARY KEY AUTOINCREMENT,
                    ""SyncGuid"" TEXT NOT NULL,
                    ""EntityType"" TEXT NOT NULL,
                    ""EntityIdentifier"" TEXT NOT NULL,
                    ""CreditorId"" INTEGER NULL,
                    ""OpeningDate"" TEXT NOT NULL,
                    ""Amount"" REAL NOT NULL,
                    ""Notes"" TEXT NULL,
                    ""IsActive"" INTEGER NOT NULL DEFAULT 1,
                    ""CreatedAt"" TEXT NOT NULL,
                    ""UpdatedAt"" TEXT NOT NULL,
                    ""CreatedBy"" TEXT NULL,
                    CONSTRAINT ""FK_OpeningBalances_Creditors_CreditorId"" FOREIGN KEY (""CreditorId"") REFERENCES ""Creditors"" (""CreditorId"") ON DELETE SET NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS ""IX_OpeningBalances_SyncGuid"" ON ""OpeningBalances"" (""SyncGuid"");
                CREATE INDEX IF NOT EXISTS ""IX_OpeningBalances_CreditorId"" ON ""OpeningBalances"" (""CreditorId"");
            ";
            cmdOb.ExecuteNonQuery();
            Log.Information("Created OpeningBalances table");
        }

        if (TableExists(connection, "DsmPersonalDebtors"))
        {
            EnsureColumnExists(connection, "DsmPersonalDebtors", "SyncGuid", "ALTER TABLE DsmPersonalDebtors ADD COLUMN SyncGuid TEXT NOT NULL DEFAULT '';");
            EnsureColumnExists(connection, "DsmPersonalDebtors", "DeductFromSalary", "ALTER TABLE DsmPersonalDebtors ADD COLUMN DeductFromSalary INTEGER NOT NULL DEFAULT 1;");
            EnsureColumnExists(connection, "DsmPersonalDebtors", "EntryType", "ALTER TABLE DsmPersonalDebtors ADD COLUMN EntryType TEXT NOT NULL DEFAULT 'Operational';");
        }

        // Guard: skip column additions for tables that don't exist yet (e.g. fresh install)
        if (!TableExists(connection, "PaymentCollections") || !TableExists(connection, "DsmEntries"))
            return;

        EnsureColumnExists(connection, "PaymentCollections", "CashDeposit", "ALTER TABLE PaymentCollections ADD COLUMN CashDeposit REAL NOT NULL DEFAULT 0.0;");
        EnsureColumnExists(connection, "PaymentCollections", "DynamicItemsJson", "ALTER TABLE PaymentCollections ADD COLUMN DynamicItemsJson TEXT NULL;");
        EnsureColumnExists(connection, "DsmEntries", "ConnectedPumpId", "ALTER TABLE DsmEntries ADD COLUMN ConnectedPumpId INTEGER NULL;");
        EnsureColumnExists(connection, "DsmEntries", "ConnectedPumpIdsJson", "ALTER TABLE DsmEntries ADD COLUMN ConnectedPumpIdsJson TEXT NULL;");
        EnsureColumnExists(connection, "DsmEntries", "ReconciledToPumpId", "ALTER TABLE DsmEntries ADD COLUMN ReconciledToPumpId INTEGER NULL;");
        EnsureColumnExists(connection, "DsmPumpAssignments", "ConnectedPumpIdsJson", "ALTER TABLE DsmPumpAssignments ADD COLUMN ConnectedPumpIdsJson TEXT NULL;");


        // Phase 3 additions: DsmEntry Start/End times
        EnsureColumnExists(connection, "DsmEntries", "StartTime", "ALTER TABLE DsmEntries ADD COLUMN StartTime TEXT NULL;");
        EnsureColumnExists(connection, "DsmEntries", "EndTime", "ALTER TABLE DsmEntries ADD COLUMN EndTime TEXT NULL;");

        // DsmProfiles Pending Advance tracking columns
        EnsureColumnExists(connection, "DsmProfiles", "PendingAdvance", "ALTER TABLE DsmProfiles ADD COLUMN PendingAdvance REAL NOT NULL DEFAULT 0.0;");
        EnsureColumnExists(connection, "DsmProfiles", "MonthlyAdvanceDeduction", "ALTER TABLE DsmProfiles ADD COLUMN MonthlyAdvanceDeduction REAL NOT NULL DEFAULT 0.0;");
        EnsureColumnExists(connection, "DsmSalaryAdjustments", "PendingAdvanceDeduction", "ALTER TABLE DsmSalaryAdjustments ADD COLUMN PendingAdvanceDeduction REAL NOT NULL DEFAULT 0.0;");

        // Phase 3 additions: DebitEntry additional tracking columns
        EnsureColumnExists(connection, "DebitEntries", "Fuel", "ALTER TABLE DebitEntries ADD COLUMN Fuel TEXT NULL;");
        EnsureColumnExists(connection, "DebitEntries", "EntryTime", "ALTER TABLE DebitEntries ADD COLUMN EntryTime TEXT NULL;");
        EnsureColumnExists(connection, "DebitEntries", "PaymentMethod", "ALTER TABLE DebitEntries ADD COLUMN PaymentMethod TEXT NOT NULL DEFAULT 'Credit';");
        EnsureColumnExists(connection, "DebitEntries", "CardTid", "ALTER TABLE DebitEntries ADD COLUMN CardTid TEXT NULL;");
        EnsureColumnExists(connection, "DebitEntries", "CardBatch", "ALTER TABLE DebitEntries ADD COLUMN CardBatch TEXT NULL;");
        EnsureColumnExists(connection, "DebitEntries", "Denom500", "ALTER TABLE DebitEntries ADD COLUMN Denom500 INTEGER NOT NULL DEFAULT 0;");
        EnsureColumnExists(connection, "DebitEntries", "Denom200", "ALTER TABLE DebitEntries ADD COLUMN Denom200 INTEGER NOT NULL DEFAULT 0;");
        EnsureColumnExists(connection, "DebitEntries", "Denom100", "ALTER TABLE DebitEntries ADD COLUMN Denom100 INTEGER NOT NULL DEFAULT 0;");
        EnsureColumnExists(connection, "DebitEntries", "Denom50", "ALTER TABLE DebitEntries ADD COLUMN Denom50 INTEGER NOT NULL DEFAULT 0;");
        EnsureColumnExists(connection, "DebitEntries", "Denom20", "ALTER TABLE DebitEntries ADD COLUMN Denom20 INTEGER NOT NULL DEFAULT 0;");
        EnsureColumnExists(connection, "DebitEntries", "Denom10", "ALTER TABLE DebitEntries ADD COLUMN Denom10 INTEGER NOT NULL DEFAULT 0;");
        EnsureColumnExists(connection, "DebitEntries", "Coins", "ALTER TABLE DebitEntries ADD COLUMN Coins INTEGER NOT NULL DEFAULT 0;");

        // Phase 3 additions: CreditorRepayments details
        EnsureColumnExists(connection, "CreditorRepayments", "CardTid", "ALTER TABLE CreditorRepayments ADD COLUMN CardTid TEXT NULL;");
        EnsureColumnExists(connection, "CreditorRepayments", "CardBatch", "ALTER TABLE CreditorRepayments ADD COLUMN CardBatch TEXT NULL;");
        EnsureColumnExists(connection, "CreditorRepayments", "Denom500", "ALTER TABLE CreditorRepayments ADD COLUMN Denom500 INTEGER NOT NULL DEFAULT 0;");
        EnsureColumnExists(connection, "CreditorRepayments", "Denom200", "ALTER TABLE CreditorRepayments ADD COLUMN Denom200 INTEGER NOT NULL DEFAULT 0;");
        EnsureColumnExists(connection, "CreditorRepayments", "Denom100", "ALTER TABLE CreditorRepayments ADD COLUMN Denom100 INTEGER NOT NULL DEFAULT 0;");
        EnsureColumnExists(connection, "CreditorRepayments", "Denom50", "ALTER TABLE CreditorRepayments ADD COLUMN Denom50 INTEGER NOT NULL DEFAULT 0;");
        EnsureColumnExists(connection, "CreditorRepayments", "Denom20", "ALTER TABLE CreditorRepayments ADD COLUMN Denom20 INTEGER NOT NULL DEFAULT 0;");
        EnsureColumnExists(connection, "CreditorRepayments", "Denom10", "ALTER TABLE CreditorRepayments ADD COLUMN Denom10 INTEGER NOT NULL DEFAULT 0;");
        EnsureColumnExists(connection, "CreditorRepayments", "Coins", "ALTER TABLE CreditorRepayments ADD COLUMN Coins INTEGER NOT NULL DEFAULT 0;");
        EnsureColumnExists(connection, "CreditorRepayments", "ShiftNumber", "ALTER TABLE CreditorRepayments ADD COLUMN ShiftNumber TEXT NULL;");

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
        
        // Feature 5: PhonePe Card Split
        EnsureColumnExists(connection, "PaymentCollections", "PhonePeCardMorning", "ALTER TABLE PaymentCollections ADD COLUMN PhonePeCardMorning REAL NOT NULL DEFAULT 0.0");
        EnsureColumnExists(connection, "PaymentCollections", "PhonePeCardNight", "ALTER TABLE PaymentCollections ADD COLUMN PhonePeCardNight REAL NOT NULL DEFAULT 0.0");
        
        // Feature 3: IsReconciled for Creditor Return Tracker
        EnsureColumnExists(connection, "DsmEntries", "IsReconciled", "ALTER TABLE DsmEntries ADD COLUMN IsReconciled INTEGER NOT NULL DEFAULT 0");

        // Feature: Card Settlement details
        EnsureColumnExists(connection, "PaymentCollections", "CardTid", "ALTER TABLE PaymentCollections ADD COLUMN CardTid TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "CardBatch", "ALTER TABLE PaymentCollections ADD COLUMN CardBatch TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "PhonePeTid", "ALTER TABLE PaymentCollections ADD COLUMN PhonePeTid TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "PhonePeBatch", "ALTER TABLE PaymentCollections ADD COLUMN PhonePeBatch TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "PetroCardTid", "ALTER TABLE PaymentCollections ADD COLUMN PetroCardTid TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "PetroCardBatch", "ALTER TABLE PaymentCollections ADD COLUMN PetroCardBatch TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "PhonePeTidMorning", "ALTER TABLE PaymentCollections ADD COLUMN PhonePeTidMorning TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "PhonePeBatchMorning", "ALTER TABLE PaymentCollections ADD COLUMN PhonePeBatchMorning TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "PhonePeTidNight", "ALTER TABLE PaymentCollections ADD COLUMN PhonePeTidNight TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "PhonePeBatchNight", "ALTER TABLE PaymentCollections ADD COLUMN PhonePeBatchNight TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "CreditCardTidMorning", "ALTER TABLE PaymentCollections ADD COLUMN CreditCardTidMorning TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "CreditCardBatchMorning", "ALTER TABLE PaymentCollections ADD COLUMN CreditCardBatchMorning TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "CreditCardTidNight", "ALTER TABLE PaymentCollections ADD COLUMN CreditCardTidNight TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "CreditCardBatchNight", "ALTER TABLE PaymentCollections ADD COLUMN CreditCardBatchNight TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "PetroCardMorning", "ALTER TABLE PaymentCollections ADD COLUMN PetroCardMorning REAL NOT NULL DEFAULT 0.0");
        EnsureColumnExists(connection, "PaymentCollections", "PetroCardNight", "ALTER TABLE PaymentCollections ADD COLUMN PetroCardNight REAL NOT NULL DEFAULT 0.0");
        EnsureColumnExists(connection, "PaymentCollections", "PetroCardTidMorning", "ALTER TABLE PaymentCollections ADD COLUMN PetroCardTidMorning TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "PetroCardBatchMorning", "ALTER TABLE PaymentCollections ADD COLUMN PetroCardBatchMorning TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "PetroCardTidNight", "ALTER TABLE PaymentCollections ADD COLUMN PetroCardTidNight TEXT NULL");
        EnsureColumnExists(connection, "PaymentCollections", "PetroCardBatchNight", "ALTER TABLE PaymentCollections ADD COLUMN PetroCardBatchNight TEXT NULL");
        EnsureColumnExists(connection, "Shifts", "CardSettlementPosTotal", "ALTER TABLE Shifts ADD COLUMN CardSettlementPosTotal REAL NOT NULL DEFAULT 0.0");

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
                    ""MachineId"" TEXT NULL,
                    ""SyncGuid"" TEXT NULL,
                    ""RecordGuid"" TEXT NULL
                );";
            cmd.ExecuteNonQuery();
            Log.Information("Created SyncChangeLogs table with StationId, MachineId, SyncGuid, and RecordGuid columns");
        }
        else
        {
            EnsureColumnExists(connection, "SyncChangeLogs", "StationId", "ALTER TABLE SyncChangeLogs ADD COLUMN StationId TEXT NULL;");
            EnsureColumnExists(connection, "SyncChangeLogs", "MachineId", "ALTER TABLE SyncChangeLogs ADD COLUMN MachineId TEXT NULL;");
            EnsureColumnExists(connection, "SyncChangeLogs", "SyncGuid", "ALTER TABLE SyncChangeLogs ADD COLUMN SyncGuid TEXT NULL;");
            EnsureColumnExists(connection, "SyncChangeLogs", "RecordGuid", "ALTER TABLE SyncChangeLogs ADD COLUMN RecordGuid TEXT NULL;");
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

        // Phase 3 additions: DsmPersonalDebtors table
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='DsmPersonalDebtors'";
        var dsmPersonalDebtorsExists = cmd.ExecuteScalar() != null;
        if (!dsmPersonalDebtorsExists)
        {
            cmd.CommandText = @"
                CREATE TABLE ""DsmPersonalDebtors"" (
                    ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""SyncGuid"" TEXT NOT NULL,
                    ""DsmEntryId"" INTEGER NULL,
                    ""DsmName"" TEXT NOT NULL,
                    ""Date"" TEXT NOT NULL,
                    ""Time"" TEXT NOT NULL,
                    ""Amount"" REAL NOT NULL,
                    ""FuelProduct"" TEXT NULL,
                    ""Remarks"" TEXT NULL,
                    ""PaymentMethod"" TEXT NOT NULL,
                    ""Denom500"" INTEGER NOT NULL,
                    ""Denom200"" INTEGER NOT NULL,
                    ""Denom100"" INTEGER NOT NULL,
                    ""Denom50"" INTEGER NOT NULL,
                    ""Denom20"" INTEGER NOT NULL,
                    ""Denom10"" INTEGER NOT NULL,
                    ""Coins"" INTEGER NOT NULL,
                    ""CardTid"" TEXT NULL,
                    ""CardBatch"" TEXT NULL,
                    ""SequenceNumber"" INTEGER NOT NULL,
                    ""RepaidAmount"" REAL NOT NULL,
                    ""EntryType"" TEXT NOT NULL DEFAULT 'Operational',
                    ""CreatedAt"" TEXT NOT NULL,
                    CONSTRAINT ""FK_DsmPersonalDebtors_DsmEntries_DsmEntryId"" FOREIGN KEY (""DsmEntryId"") REFERENCES ""DsmEntries"" (""DsmEntryId"") ON DELETE SET NULL
                );";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE UNIQUE INDEX \"IX_DsmPersonalDebtors_SyncGuid\" ON \"DsmPersonalDebtors\" (\"SyncGuid\");";
            cmd.ExecuteNonQuery();
            Log.Information("Created DsmPersonalDebtors table");
        }

        EnsureColumnExists(connection, "DsmPersonalDebtors", "SyncGuid", "ALTER TABLE DsmPersonalDebtors ADD COLUMN SyncGuid TEXT NOT NULL DEFAULT '';");
        EnsureColumnExists(connection, "DsmPersonalDebtors", "DeductFromSalary", "ALTER TABLE DsmPersonalDebtors ADD COLUMN DeductFromSalary INTEGER NOT NULL DEFAULT 1;");
        EnsureColumnExists(connection, "DsmPersonalDebtors", "EntryType", "ALTER TABLE DsmPersonalDebtors ADD COLUMN EntryType TEXT NOT NULL DEFAULT 'Operational';");

        // Check if DsmPersonalDebtorRepayments exists
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='DsmPersonalDebtorRepayments'";
        var dsmPersonalDebtorRepaymentsExists = cmd.ExecuteScalar() != null;
        if (!dsmPersonalDebtorRepaymentsExists)
        {
            cmd.CommandText = @"
                CREATE TABLE ""DsmPersonalDebtorRepayments"" (
                    ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    ""SyncGuid"" TEXT NOT NULL,
                    ""DsmPersonalDebtorId"" INTEGER NOT NULL,
                    ""ShiftId"" INTEGER NULL,
                    ""Date"" TEXT NOT NULL,
                    ""Amount"" REAL NOT NULL,
                    ""PaymentMethod"" TEXT NOT NULL,
                    ""Denom500"" INTEGER NOT NULL,
                    ""Denom200"" INTEGER NOT NULL,
                    ""Denom100"" INTEGER NOT NULL,
                    ""Denom50"" INTEGER NOT NULL,
                    ""Denom20"" INTEGER NOT NULL,
                    ""Denom10"" INTEGER NOT NULL,
                    ""Coins"" INTEGER NOT NULL,
                    ""CardTid"" TEXT NULL,
                    ""CardBatch"" TEXT NULL,
                    ""Source"" TEXT NOT NULL,
                    ""CreatedAt"" TEXT NOT NULL,
                    CONSTRAINT ""FK_DsmPersonalDebtorRepayments_DsmPersonalDebtors_DsmPersonalDebtorId"" FOREIGN KEY (""DsmPersonalDebtorId"") REFERENCES ""DsmPersonalDebtors"" (""Id"") ON DELETE CASCADE,
                    CONSTRAINT ""FK_DsmPersonalDebtorRepayments_Shifts_ShiftId"" FOREIGN KEY (""ShiftId"") REFERENCES ""Shifts"" (""ShiftId"") ON DELETE SET NULL
                );";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "CREATE UNIQUE INDEX \"IX_DsmPersonalDebtorRepayments_SyncGuid\" ON \"DsmPersonalDebtorRepayments\" (\"SyncGuid\");";
            cmd.ExecuteNonQuery();
            Log.Information("Created DsmPersonalDebtorRepayments table");
        }
        else
        {
            EnsureColumnExists(connection, "DsmPersonalDebtorRepayments", "SyncGuid", "ALTER TABLE DsmPersonalDebtorRepayments ADD COLUMN SyncGuid TEXT NOT NULL DEFAULT '';");
        }

        // DsmQrPayments
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='DsmQrPayments'";
        var qrPaymentsExists = cmd.ExecuteScalar() != null;
        if (!qrPaymentsExists)
        {
            cmd.CommandText = @"
                CREATE TABLE ""DsmQrPayments"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_DsmQrPayments"" PRIMARY KEY AUTOINCREMENT,
                    ""DsmEntryId"" INTEGER NULL,
                    ""DsmName"" TEXT NOT NULL,
                    ""TargetDsmName"" TEXT NOT NULL,
                    ""Amount"" REAL NOT NULL,
                    ""Tid"" TEXT NULL,
                    ""Batch"" TEXT NULL,
                    ""Slot"" TEXT NULL,
                    ""Date"" TEXT NOT NULL,
                    ""CreatedAt"" TEXT NOT NULL,
                    ""SyncGuid"" TEXT NOT NULL,
                    CONSTRAINT ""FK_DsmQrPayments_DsmEntries_DsmEntryId"" FOREIGN KEY (""DsmEntryId"") REFERENCES ""DsmEntries"" (""DsmEntryId"") ON DELETE SET NULL
                );";
            cmd.ExecuteNonQuery();

            cmd.CommandText = @"
                CREATE INDEX ""IX_DsmQrPayments_DsmEntryId"" ON ""DsmQrPayments"" (""DsmEntryId"");
                CREATE UNIQUE INDEX ""IX_DsmQrPayments_SyncGuid"" ON ""DsmQrPayments"" (""SyncGuid"");
                CREATE INDEX ""IX_DsmQrPayments_DsmName_Date"" ON ""DsmQrPayments"" (""DsmName"", ""Date"");
                CREATE INDEX ""IX_DsmQrPayments_TargetDsmName"" ON ""DsmQrPayments"" (""TargetDsmName"");
            ";
            cmd.ExecuteNonQuery();
            Log.Information("Created DsmQrPayments table");
        }
        else
        {
            EnsureColumnExists(connection, "DsmQrPayments", "SyncGuid", "ALTER TABLE DsmQrPayments ADD COLUMN SyncGuid TEXT NOT NULL DEFAULT '';");
        }

        // PettyCashTransactions
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='PettyCashTransactions'";
        var pettyCashExists = cmd.ExecuteScalar() != null;
        var needsRecreate = false;
        if (pettyCashExists)
        {
            cmd.CommandText = "PRAGMA table_info(PettyCashTransactions);";
            var hasShiftExpenseId = false;
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    if (reader["name"].ToString() == "ShiftExpenseId")
                    {
                        hasShiftExpenseId = true;
                        break;
                    }
                }
            }
            if (!hasShiftExpenseId)
            {
                needsRecreate = true;
            }
        }

        if (!pettyCashExists || needsRecreate)
        {
            if (needsRecreate)
            {
                cmd.CommandText = "DROP TABLE IF EXISTS \"PettyCashTransactions\";";
                cmd.ExecuteNonQuery();
                Log.Warning("Dropped legacy PettyCashTransactions table due to schema mismatch");
            }

            cmd.CommandText = @"
                CREATE TABLE ""PettyCashTransactions"" (
                    ""TransactionId"" INTEGER NOT NULL CONSTRAINT ""PK_PettyCashTransactions"" PRIMARY KEY AUTOINCREMENT,
                    ""Date"" TEXT NOT NULL,
                    ""Description"" TEXT NOT NULL,
                    ""Amount"" REAL NOT NULL,
                    ""Type"" TEXT NOT NULL,
                    ""ShiftExpenseId"" INTEGER NULL,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT (datetime('now'))
                );";
            cmd.ExecuteNonQuery();
            Log.Information("Created PettyCashTransactions table");
        }

        // ─── FuelTankers ──────────────────────────────────────────────────────────
        if (!TableExists(connection, "FuelTankers"))
        {
            cmd.CommandText = @"
                CREATE TABLE ""FuelTankers"" (
                    ""FuelTankerId"" INTEGER NOT NULL CONSTRAINT ""PK_FuelTankers"" PRIMARY KEY AUTOINCREMENT,
                    ""TankerDate"" TEXT NOT NULL,
                    ""TankerNumber"" TEXT NULL,
                    ""InvoiceNumber"" TEXT NOT NULL,
                    ""FuelType"" TEXT NOT NULL,
                    ""Quantity"" REAL NOT NULL DEFAULT 0,
                    ""PurchaseRate"" REAL NOT NULL DEFAULT 0,
                    ""TotalAmount"" REAL NOT NULL DEFAULT 0,
                    ""Density"" REAL NOT NULL DEFAULT 0,
                    ""Remarks"" TEXT NULL,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT (datetime('now')),
                    ""UpdatedAt"" TEXT NOT NULL DEFAULT (datetime('now'))
                );";
            cmd.ExecuteNonQuery();
            Log.Information("Created FuelTankers table");
        }

        // ─── TankDailyStocks ──────────────────────────────────────────────────────
        if (!TableExists(connection, "TankDailyStocks"))
        {
            cmd.CommandText = @"
                CREATE TABLE ""TankDailyStocks"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_TankDailyStocks"" PRIMARY KEY AUTOINCREMENT,
                    ""Date"" TEXT NOT NULL,
                    ""FuelType"" TEXT NOT NULL,
                    ""OpeningStock"" REAL NOT NULL DEFAULT 0,
                    ""DaySaleLitres"" REAL NOT NULL DEFAULT 0,
                    ""TestingLitres"" REAL NOT NULL DEFAULT 0,
                    ""PurchasedLitres"" REAL NOT NULL DEFAULT 0,
                    ""ClosingStock"" REAL NOT NULL DEFAULT 0,
                    ""DipMm"" REAL NOT NULL DEFAULT 0,
                    ""ManualStock"" REAL NOT NULL DEFAULT 0,
                    ""LastUpdated"" TEXT NOT NULL DEFAULT (datetime('now')),
                    ""ShiftId"" INTEGER NULL
                );";
            cmd.ExecuteNonQuery();
            Log.Information("Created TankDailyStocks table");
        }

        // ─── DsmSalaryHistories ───────────────────────────────────────────────────
        if (!TableExists(connection, "DsmSalaryHistories"))
        {
            cmd.CommandText = @"
                CREATE TABLE ""DsmSalaryHistories"" (
                    ""DsmSalaryHistoryId"" INTEGER NOT NULL CONSTRAINT ""PK_DsmSalaryHistories"" PRIMARY KEY AUTOINCREMENT,
                    ""DsmProfileId"" INTEGER NOT NULL,
                    ""OldBaseSalary"" REAL NOT NULL DEFAULT 0,
                    ""NewBaseSalary"" REAL NOT NULL DEFAULT 0,
                    ""OldSalaryType"" TEXT NOT NULL,
                    ""NewSalaryType"" TEXT NOT NULL,
                    ""ChangeDate"" TEXT NOT NULL DEFAULT (datetime('now'))
                );";
            cmd.ExecuteNonQuery();
            Log.Information("Created DsmSalaryHistories table");
        }

        // ─── DsmSalaryPayments ────────────────────────────────────────────────────
        if (!TableExists(connection, "DsmSalaryPayments"))
        {
            cmd.CommandText = @"
                CREATE TABLE ""DsmSalaryPayments"" (
                    ""DsmSalaryPaymentId"" INTEGER NOT NULL CONSTRAINT ""PK_DsmSalaryPayments"" PRIMARY KEY AUTOINCREMENT,
                    ""DsmProfileId"" INTEGER NOT NULL,
                    ""Year"" INTEGER NOT NULL DEFAULT 0,
                    ""Month"" INTEGER NOT NULL DEFAULT 0,
                    ""NetSalary"" REAL NOT NULL DEFAULT 0,
                    ""PaidAmount"" REAL NOT NULL DEFAULT 0,
                    ""PaymentDate"" TEXT NOT NULL DEFAULT (datetime('now')),
                    ""PaymentMode"" TEXT NOT NULL DEFAULT 'Cash',
                    ""Remarks"" TEXT NULL,
                    ""CreatedAt"" TEXT NOT NULL DEFAULT (datetime('now')),
                    ""UpdatedAt"" TEXT NOT NULL DEFAULT (datetime('now'))
                );";
            cmd.ExecuteNonQuery();
            Log.Information("Created DsmSalaryPayments table");
        }

        // ─── KhandharePetroleumEntries ───────────────────────────────────────────
        if (!TableExists(connection, "KhandharePetroleumEntries"))
        {
            cmd.CommandText = @"
                CREATE TABLE ""KhandharePetroleumEntries"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_KhandharePetroleumEntries"" PRIMARY KEY AUTOINCREMENT,
                    ""DsmEntryId"" INTEGER NULL,
                    ""DsmName"" TEXT NOT NULL DEFAULT '',
                    ""Name"" TEXT NOT NULL DEFAULT '',
                    ""SlipNumber"" TEXT NOT NULL DEFAULT '',
                    ""VehicleNumber"" TEXT NULL,
                    ""Amount"" REAL NOT NULL DEFAULT 0.0,
                    ""Date"" TEXT NOT NULL DEFAULT '',
                    ""CreatedAt"" TEXT NOT NULL DEFAULT '',
                    ""SyncGuid"" TEXT NOT NULL DEFAULT '',
                    CONSTRAINT ""FK_KhandharePetroleumEntries_DsmEntries_DsmEntryId"" FOREIGN KEY (""DsmEntryId"") REFERENCES ""DsmEntries"" (""DsmEntryId"") ON DELETE CASCADE
                );";
            cmd.ExecuteNonQuery();
            Log.Information("Created KhandharePetroleumEntries table");
        }
        else
        {
            EnsureColumnExists(connection, "KhandharePetroleumEntries", "VehicleNumber", "ALTER TABLE KhandharePetroleumEntries ADD COLUMN VehicleNumber TEXT NULL;");
            EnsureColumnExists(connection, "KhandharePetroleumEntries", "SyncGuid", "ALTER TABLE KhandharePetroleumEntries ADD COLUMN SyncGuid TEXT NOT NULL DEFAULT '';");
        }

        // Restore canonical shift types A/B/C and backfill any empty SyncGuids
        using (var cmdMigrate = connection.CreateCommand())
        {
            cmdMigrate.CommandText = "UPDATE Shifts SET ShiftType = 'A' WHERE ShiftType = 'I';";
            cmdMigrate.ExecuteNonQuery();
            cmdMigrate.CommandText = "UPDATE Shifts SET ShiftType = 'B' WHERE ShiftType = 'II';";
            cmdMigrate.ExecuteNonQuery();
            cmdMigrate.CommandText = "UPDATE Shifts SET ShiftType = 'C' WHERE ShiftType = 'III';";
            cmdMigrate.ExecuteNonQuery();

            try
            {
                cmdMigrate.CommandText = "UPDATE DsmPersonalDebtors SET SyncGuid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-a' || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))) WHERE SyncGuid IS NULL OR SyncGuid = '';";
                cmdMigrate.ExecuteNonQuery();
                cmdMigrate.CommandText = "UPDATE DsmPersonalDebtorRepayments SET SyncGuid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-a' || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))) WHERE SyncGuid IS NULL OR SyncGuid = '';";
                cmdMigrate.ExecuteNonQuery();
                cmdMigrate.CommandText = "UPDATE DsmQrPayments SET SyncGuid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-a' || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))) WHERE SyncGuid IS NULL OR SyncGuid = '';";
                cmdMigrate.ExecuteNonQuery();
                cmdMigrate.CommandText = "UPDATE KhandharePetroleumEntries SET SyncGuid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-a' || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))) WHERE SyncGuid IS NULL OR SyncGuid = '';";
                cmdMigrate.ExecuteNonQuery();
            }
            catch { }
        }

        // ── One-time cleanup: remove duplicate records created by missing sync dedup ──
        try
        {
            using var cleanupCmd = connection.CreateCommand();

            // 1. OilDefPurchases — dedup by (ProductId, PurchaseDate, InvoiceNumber, Quantity, UnitPrice)
            if (TableExists(connection, "OilDefPurchases"))
            {
                cleanupCmd.CommandText = @"
                    DELETE FROM OilDefPurchases WHERE Id NOT IN (
                        SELECT MIN(Id) FROM OilDefPurchases
                        GROUP BY ProductId, date(PurchaseDate), LOWER(COALESCE(InvoiceNumber,'')), 
                               CAST(ROUND(Quantity * 100) AS INTEGER), CAST(ROUND(UnitPrice * 100) AS INTEGER)
                    );";
                int delPurchases = cleanupCmd.ExecuteNonQuery();
                if (delPurchases > 0)
                    Log.Information("Duplicate cleanup: removed {Count} duplicate OilDefPurchases rows", delPurchases);

                // Also remove stale SyncIdMappings pointing to now-deleted OilDefPurchases
                cleanupCmd.CommandText = @"
                    DELETE FROM SyncIdMappings WHERE TableName = 'OilDefPurchases' 
                    AND LocalId NOT IN (SELECT Id FROM OilDefPurchases);";
                cleanupCmd.ExecuteNonQuery();
            }

            // 2. CreditorRepayments — dedup by (CreditorName, RepaymentDate, Amount, PaymentMode)
            if (TableExists(connection, "CreditorRepayments"))
            {
                cleanupCmd.CommandText = @"
                    DELETE FROM CreditorRepayments WHERE CreditorRepaymentId NOT IN (
                        SELECT MIN(CreditorRepaymentId) FROM CreditorRepayments
                        GROUP BY LOWER(COALESCE(CreditorName,'')), date(RepaymentDate), 
                               CAST(ROUND(Amount * 100) AS INTEGER), LOWER(COALESCE(PaymentMode,''))
                    );";
                int delRepayments = cleanupCmd.ExecuteNonQuery();
                if (delRepayments > 0)
                    Log.Information("Duplicate cleanup: removed {Count} duplicate CreditorRepayments rows", delRepayments);

                cleanupCmd.CommandText = @"
                    DELETE FROM SyncIdMappings WHERE TableName = 'CreditorRepayments' 
                    AND LocalId NOT IN (SELECT CreditorRepaymentId FROM CreditorRepayments);";
                cleanupCmd.ExecuteNonQuery();
            }

            // 3. DsmPersonalDebtorRepayments — dedup by (DsmPersonalDebtorId, Date, Amount, PaymentMethod)
            if (TableExists(connection, "DsmPersonalDebtorRepayments"))
            {
                cleanupCmd.CommandText = @"
                    DELETE FROM DsmPersonalDebtorRepayments WHERE Id NOT IN (
                        SELECT MIN(Id) FROM DsmPersonalDebtorRepayments
                        GROUP BY DsmPersonalDebtorId, date(Date), 
                               CAST(ROUND(Amount * 100) AS INTEGER), LOWER(COALESCE(PaymentMethod,''))
                    );";
                int delDsmRepayments = cleanupCmd.ExecuteNonQuery();
                if (delDsmRepayments > 0)
                    Log.Information("Duplicate cleanup: removed {Count} duplicate DsmPersonalDebtorRepayments rows", delDsmRepayments);

                cleanupCmd.CommandText = @"
                    DELETE FROM SyncIdMappings WHERE TableName = 'DsmPersonalDebtorRepayments' 
                    AND LocalId NOT IN (SELECT Id FROM DsmPersonalDebtorRepayments);";
                cleanupCmd.ExecuteNonQuery();
            }

            // 4. Creditors — dedup by Name (case-insensitive), re-point FKs before deleting duplicates
            if (TableExists(connection, "Creditors"))
            {
                // Re-point DebtorVehicles FKs to the canonical (lowest-Id) creditor before deleting duplicates
                if (TableExists(connection, "DebtorVehicles"))
                {
                    cleanupCmd.CommandText = @"
                        UPDATE DebtorVehicles SET CreditorId = (
                            SELECT MIN(c2.CreditorId) FROM Creditors c2 
                            WHERE LOWER(TRIM(c2.Name)) = (
                                SELECT LOWER(TRIM(c3.Name)) FROM Creditors c3 WHERE c3.CreditorId = DebtorVehicles.CreditorId
                            )
                        )
                        WHERE CreditorId NOT IN (
                            SELECT MIN(CreditorId) FROM Creditors GROUP BY LOWER(TRIM(Name))
                        );";
                    cleanupCmd.ExecuteNonQuery();
                }

                cleanupCmd.CommandText = @"
                    DELETE FROM Creditors WHERE CreditorId NOT IN (
                        SELECT MIN(CreditorId) FROM Creditors GROUP BY LOWER(TRIM(Name))
                    );";
                int delCreditors = cleanupCmd.ExecuteNonQuery();
                if (delCreditors > 0)
                    Log.Information("Duplicate cleanup: removed {Count} duplicate Creditors rows", delCreditors);

                cleanupCmd.CommandText = @"
                    DELETE FROM SyncIdMappings WHERE TableName = 'Creditors' 
                    AND LocalId NOT IN (SELECT CreditorId FROM Creditors);";
                cleanupCmd.ExecuteNonQuery();
            }

            // 5. OilDefDailyLogs — dedup stock adjustments, sales logs, purchase logs
            if (TableExists(connection, "OilDefDailyLogs"))
            {
                // Stock adjustments: dedup by (ProductId, date(LogDate), strftime('%H:%M', LogDate), AdjustmentQuantity, AdjustmentType)
                cleanupCmd.CommandText = @"
                    DELETE FROM OilDefDailyLogs WHERE AdjustmentQuantity != 0 AND Id NOT IN (
                        SELECT MIN(Id) FROM OilDefDailyLogs
                        WHERE AdjustmentQuantity != 0
                        GROUP BY ProductId, date(LogDate), strftime('%H:%M', LogDate), 
                               CAST(ROUND(AdjustmentQuantity * 100) AS INTEGER), LOWER(COALESCE(AdjustmentType,''))
                    );";
                int delAdj = cleanupCmd.ExecuteNonQuery();
                if (delAdj > 0)
                    Log.Information("Duplicate cleanup: removed {Count} duplicate OilDefDailyLogs adjustment rows", delAdj);

                // Sales logs: dedup by (ProductId, date(LogDate), SoldQuantity, OverrideSaleRate)
                cleanupCmd.CommandText = @"
                    DELETE FROM OilDefDailyLogs WHERE SoldQuantity > 0 AND AdjustmentQuantity = 0 AND AddedQuantity = 0 AND Id NOT IN (
                        SELECT MIN(Id) FROM OilDefDailyLogs
                        WHERE SoldQuantity > 0 AND AdjustmentQuantity = 0 AND AddedQuantity = 0
                        GROUP BY ProductId, date(LogDate), 
                               CAST(ROUND(SoldQuantity * 100) AS INTEGER), CAST(ROUND(COALESCE(OverrideSaleRate, 0) * 100) AS INTEGER)
                    );";
                int delSales = cleanupCmd.ExecuteNonQuery();
                if (delSales > 0)
                    Log.Information("Duplicate cleanup: removed {Count} duplicate OilDefDailyLogs sales rows", delSales);

                // Purchase daily logs: dedup by (ProductId, date(LogDate), AddedQuantity)
                cleanupCmd.CommandText = @"
                    DELETE FROM OilDefDailyLogs WHERE AddedQuantity > 0 AND AdjustmentQuantity = 0 AND SoldQuantity = 0 AND Id NOT IN (
                        SELECT MIN(Id) FROM OilDefDailyLogs
                        WHERE AddedQuantity > 0 AND AdjustmentQuantity = 0 AND SoldQuantity = 0
                        GROUP BY ProductId, date(LogDate), 
                               CAST(ROUND(AddedQuantity * 100) AS INTEGER)
                    );";
                int delPurch = cleanupCmd.ExecuteNonQuery();
                if (delPurch > 0)
                    Log.Information("Duplicate cleanup: removed {Count} duplicate OilDefDailyLogs purchase rows", delPurch);

                cleanupCmd.CommandText = @"
                    DELETE FROM SyncIdMappings WHERE TableName = 'OilDefDailyLogs' 
                    AND LocalId NOT IN (SELECT Id FROM OilDefDailyLogs);";
                cleanupCmd.ExecuteNonQuery();
            }

            // 6. OilDefInventories — dedup by (ProductId, Year, Month)
            if (TableExists(connection, "OilDefInventories"))
            {
                cleanupCmd.CommandText = @"
                    DELETE FROM OilDefInventories WHERE Id NOT IN (
                        SELECT MIN(Id) FROM OilDefInventories
                        GROUP BY ProductId, Year, Month
                    );";
                int delInv = cleanupCmd.ExecuteNonQuery();
                if (delInv > 0)
                    Log.Information("Duplicate cleanup: removed {Count} duplicate OilDefInventories rows", delInv);

                cleanupCmd.CommandText = @"
                    DELETE FROM SyncIdMappings WHERE TableName = 'OilDefInventories' 
                    AND LocalId NOT IN (SELECT Id FROM OilDefInventories);";
                cleanupCmd.ExecuteNonQuery();
            }

            // 7. ProductMasters — dedup by ProductName (case-insensitive)
            if (TableExists(connection, "ProductMasters"))
            {
                if (TableExists(connection, "OilDefDailyLogs"))
                {
                    cleanupCmd.CommandText = @"
                        UPDATE OilDefDailyLogs SET ProductId = (
                            SELECT MIN(p2.Id) FROM ProductMasters p2
                            WHERE LOWER(TRIM(p2.ProductName)) = (
                                SELECT LOWER(TRIM(p3.ProductName)) FROM ProductMasters p3 WHERE p3.Id = OilDefDailyLogs.ProductId
                            )
                        )
                        WHERE ProductId NOT IN (
                            SELECT MIN(Id) FROM ProductMasters GROUP BY LOWER(TRIM(ProductName))
                        );";
                    cleanupCmd.ExecuteNonQuery();
                }

                if (TableExists(connection, "OilDefPurchases"))
                {
                    cleanupCmd.CommandText = @"
                        UPDATE OilDefPurchases SET ProductId = (
                            SELECT MIN(p2.Id) FROM ProductMasters p2
                            WHERE LOWER(TRIM(p2.ProductName)) = (
                                SELECT LOWER(TRIM(p3.ProductName)) FROM ProductMasters p3 WHERE p3.Id = OilDefPurchases.ProductId
                            )
                        )
                        WHERE ProductId NOT IN (
                            SELECT MIN(Id) FROM ProductMasters GROUP BY LOWER(TRIM(ProductName))
                        );";
                    cleanupCmd.ExecuteNonQuery();
                }

                if (TableExists(connection, "OilDefInventories"))
                {
                    cleanupCmd.CommandText = @"
                        UPDATE OilDefInventories SET ProductId = (
                            SELECT MIN(p2.Id) FROM ProductMasters p2
                            WHERE LOWER(TRIM(p2.ProductName)) = (
                                SELECT LOWER(TRIM(p3.ProductName)) FROM ProductMasters p3 WHERE p3.Id = OilDefInventories.ProductId
                            )
                        )
                        WHERE ProductId NOT IN (
                            SELECT MIN(Id) FROM ProductMasters GROUP BY LOWER(TRIM(ProductName))
                        );";
                    cleanupCmd.ExecuteNonQuery();
                }

                cleanupCmd.CommandText = @"
                    DELETE FROM ProductMasters WHERE Id NOT IN (
                        SELECT MIN(Id) FROM ProductMasters GROUP BY LOWER(TRIM(ProductName))
                    );";
                int delProd = cleanupCmd.ExecuteNonQuery();
                if (delProd > 0)
                    Log.Information("Duplicate cleanup: removed {Count} duplicate ProductMasters rows", delProd);

                cleanupCmd.CommandText = @"
                    DELETE FROM SyncIdMappings WHERE TableName = 'ProductMasters' 
                    AND LocalId NOT IN (SELECT Id FROM ProductMasters);";
                cleanupCmd.ExecuteNonQuery();
            }

            // 8. Recalculate RemainingStock running balances in OilDefDailyLogs
            if (TableExists(connection, "OilDefDailyLogs"))
            {
                using var recalcCmd = connection.CreateCommand();
                recalcCmd.CommandText = @"
                    SELECT Id, ProductId, AddedQuantity, SoldQuantity, AdjustmentQuantity 
                    FROM OilDefDailyLogs 
                    ORDER BY ProductId, LogDate, Id;";
                using var reader = recalcCmd.ExecuteReader();
                var updates = new List<(int Id, double Remaining)>();
                int currentProdId = -1;
                double currentRunning = 0.0;

                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    int prodId = reader.GetInt32(1);
                    double added = reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2);
                    double sold = reader.IsDBNull(3) ? 0.0 : reader.GetDouble(3);
                    double adj = reader.IsDBNull(4) ? 0.0 : reader.GetDouble(4);

                    if (prodId != currentProdId)
                    {
                        currentProdId = prodId;
                        currentRunning = 0.0;
                    }

                    currentRunning = currentRunning + added - sold + adj;
                    updates.Add((id, currentRunning));
                }
                reader.Close();

                if (updates.Count > 0)
                {
                    using var updateCmd = connection.CreateCommand();
                    var sb = new System.Text.StringBuilder();
                    foreach (var u in updates)
                    {
                        sb.AppendLine($"UPDATE OilDefDailyLogs SET RemainingStock = {u.Remaining:F4} WHERE Id = {u.Id};");
                    }
                    updateCmd.CommandText = sb.ToString();
                    updateCmd.ExecuteNonQuery();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Non-fatal: duplicate cleanup migration failed");
        }
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
        if (!TableExists(connection, tableName))
            return;

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
            options.UseSqlite($"Data Source={DbPath};Default Timeout=5"),
            ServiceLifetime.Transient);

        services.AddTransient<DbContext>(sp => sp.GetRequiredService<FuelProDbContext>());

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
        services.AddTransient<IPumpExpenseRepository, PumpExpenseRepository>();
        services.AddTransient<IDebtorVehicleRepository, DebtorVehicleRepository>();
        services.AddTransient<IDsmPersonalDebtorRepository, DsmPersonalDebtorRepository>();
        services.AddTransient<IFuelTankerRepository, FuelTankerRepository>();
        services.AddTransient<ITankDailyStockRepository, TankDailyStockRepository>();
        services.AddTransient<IOpeningBalanceRepository, OpeningBalanceRepository>();

        // Services
        services.AddSingleton<AuthService>();
        services.AddTransient<DsmAuthAdminService>();
        services.AddTransient<SupabaseDsmService>();
        services.AddTransient<DsmEntryService>();
        services.AddTransient<ShiftCalculationService>();
        services.AddSingleton<IDsmCalculationService, DsmCalculationService>();
        services.AddSingleton<IOwnerCalculationService, OwnerCalculationService>();
        services.AddSingleton<ITidCalculationService, TidCalculationService>();
        services.AddTransient<IFinancialCalculationService, FinancialCalculationService>();
        services.AddTransient<RecalculationMigrationService>();
        services.AddSingleton<DraftService>();
        services.AddTransient<ExportService>();
        services.AddScoped<IShiftAggregationService, ShiftAggregationService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IShiftOtherCashRepository, ShiftOtherCashRepository>();
        services.AddScoped<IShiftFuelRateRepository, ShiftFuelRateRepository>();
        services.AddTransient<IAgsImportService, AgsImportService>();
        services.AddTransient<IAgsDailyAggregationService, AgsDailyAggregationService>();
        services.AddTransient<IAgsInventoryService, AgsInventoryService>();
        services.AddTransient<AgsImportValidator>();
        services.AddTransient<PrintService>();
        services.AddTransient<ExcelExportService>();
        services.AddTransient<IAuditLogService, AuditLogService>();
        services.AddTransient<IDayLockService, DayLockService>();
        // Dynamic Configuration Services
        services.AddSingleton<IFeatureToggleService, FeatureToggleService>();
        services.AddSingleton<ICollectionTypeService, CollectionTypeService>();
        services.AddSingleton<IStationConfigurationService, StationConfigurationService>();

        // Sync Services
        services.AddSingleton<FuelPro.Sync.SyncConfigService>();
        services.AddSingleton<FuelPro.Sync.SyncEngine>();
        services.AddSingleton<FuelPro.Sync.DsmSubmissionPollingService>();

        // Views
        services.AddTransient<Views.LoginView>();
        services.AddTransient<Views.MainWindow>();
        services.AddTransient<Views.OwnerMainWindow>();
        services.AddTransient<Views.OpeningBalanceManagementView>();

        // ViewModels
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<DsmEntryViewModel>();
        services.AddTransient<FinalCalculationViewModel>();
        services.AddTransient<DayTotalViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<OpeningBalanceManagementViewModel>();
        services.AddTransient<CardSettlementViewModel>();
        services.AddTransient<AgsImportViewModel>();
        services.AddTransient<OilDefDailyLogViewModel>();
        services.AddTransient<DebtorManagementViewModel>();
        services.AddTransient<DsmPersonalDebtorViewModel>();
        services.AddTransient<PumpExpensesViewModel>();
        services.AddTransient<PettyCashViewModel>();
        services.AddTransient<FuelTankerEntryViewModel>();
        services.AddTransient<TankStockHistoryViewModel>();

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
        services.AddTransient<OuterExpensesViewModel>();
        services.AddTransient<MismatchLedgerViewModel>();

        // Developer ViewModels
        services.AddTransient<DeveloperMainWindowViewModel>(sp => new DeveloperMainWindowViewModel());
        services.AddTransient<DsmManagementViewModel>();
        services.AddTransient<DsmApprovalQueueViewModel>();

        // Duplicate prevention / resolution services
        services.AddTransient<IDuplicateDataInspectionService, DuplicateDataInspectionService>();
        services.AddTransient<DuplicateResolutionService>();

        // Licensing
        services.AddSingleton(new Rashtra.Licensing.LicenseManager("PSC", "PyroSyncMax"));
        services.AddTransient<ActivationViewModel>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            var syncEngine = Services.GetService<FuelPro.Sync.SyncEngine>();
            syncEngine?.Stop();

            var pollingService = Services.GetService<FuelPro.Sync.DsmSubmissionPollingService>();
            pollingService?.Stop();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error stopping Sync Engine or Polling Service during exit");
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
                            Log.Debug(ex, "Optional logo WPF resource stream not found: {ResourceName}", resourceName);
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
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var logoName = isDark ? "pyrosync logo dark.jpg" : "pyrosync logo light.jpg";
            
            // 1. Try local execution directory Assets/
            var path = Path.Combine(exeDir, "Assets", logoName);
            if (File.Exists(path)) return path;

            // 2. Try development path fallback
            var devPath = Path.Combine(exeDir, "..", "..", "..", "..", "Assets", logoName);
            if (File.Exists(devPath)) return Path.GetFullPath(devPath);
            
            // 3. Fallback to exe dir itself
            var fallbackPath = Path.Combine(exeDir, logoName);
            if (File.Exists(fallbackPath)) return fallbackPath;
            
            return path;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get logo path");
            return string.Empty;
        }
    }
}
