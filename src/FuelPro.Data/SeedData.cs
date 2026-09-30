using System;
using System.IO;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using BCrypt.Net;
using Serilog;

namespace FuelPro.Data;

/// <summary>
/// Seeds default data on first run: manager user, owner user, and default settings.
/// Also migrates legacy "Admin" role to "Manager".
/// Creates any tables missing from EF migrations for fresh installs.
/// </summary>
public static class SeedData
{
    public static async Task InitializeAsync(FuelProDbContext context, ICredentialFileService? credentialFileService = null)
    {
        // Drop unique index on Shifts if present so migration and initial seeding won't fail on duplicates
        if (context.Database.IsRelational())
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_Shifts_ShiftDate_ShiftType;");
                await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS IX_Shifts_ShiftDate_ShiftType ON Shifts (ShiftDate, ShiftType);");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Non-fatal: Failed to ensure non-unique index on Shifts before MigrateAsync");
            }

            // Safely baseline legacy database if existing schema is already materialized
            await BaselineLegacyMigrationsIfRequiredAsync(context);

            await context.Database.MigrateAsync();
        }
        else
        {
            await context.Database.EnsureCreatedAsync();
        }

        // Ensure Settings manager and dynamic JSON columns exist for SyncEngine & dynamic features
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Settings ADD COLUMN Shift1Manager TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Settings ADD COLUMN Shift2Manager TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Settings ADD COLUMN Shift3Manager TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Settings ADD COLUMN FuelRatesJson TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Settings ADD COLUMN TankDefinitionsJson TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Settings ADD COLUMN CollectionTypesJson TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Settings ADD COLUMN AppFeatureSettingsJson TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Settings ADD COLUMN PumpMappingsJson TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Settings ADD COLUMN PumpConnectionRulesJson TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE PaymentCollections ADD COLUMN DynamicItemsJson TEXT NULL;"); } catch { }


        // Legacy dynamic columns added for database compatibility
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE PaymentCollections ADD COLUMN CashDeposit REAL NOT NULL DEFAULT 0.0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmEntries ADD COLUMN ConnectedPumpId INTEGER NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmEntries ADD COLUMN ReconciledToPumpId INTEGER NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE PaymentCollections ADD COLUMN PhonePeMorning REAL NOT NULL DEFAULT 0.0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE PaymentCollections ADD COLUMN PhonePeNight REAL NOT NULL DEFAULT 0.0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE PaymentCollections ADD COLUMN PhonePeCardMorning REAL NOT NULL DEFAULT 0.0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE PaymentCollections ADD COLUMN PhonePeCardNight REAL NOT NULL DEFAULT 0.0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmEntries ADD COLUMN IsReconciled INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmProfiles ADD COLUMN PendingAdvance REAL NOT NULL DEFAULT 0.0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmProfiles ADD COLUMN MonthlyAdvanceDeduction REAL NOT NULL DEFAULT 0.0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmSalaryAdjustments ADD COLUMN PendingAdvanceDeduction REAL NOT NULL DEFAULT 0.0;"); } catch { }
        // DsmPumpAssignment lifecycle columns (added via try-catch so existing databases are safe)
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmPumpAssignments ADD COLUMN ConnectedPumpId INTEGER NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmPumpAssignments ADD COLUMN CompletedDate TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmEntries ADD COLUMN ConnectedPumpIdsJson TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmPumpAssignments ADD COLUMN ConnectedPumpIdsJson TEXT NULL;"); } catch { }

        // Dynamically execute SQLite schema updates for ProductMaster
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ProductMasters (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProductName TEXT NOT NULL,
                    Category TEXT NOT NULL,
                    Unit TEXT NOT NULL,
                    DefaultSaleRate REAL NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1
                );
            ");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create ProductMasters table");
        }

        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS KhandharePetroleumEntries (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    DsmEntryId INTEGER NULL,
                    DsmName TEXT NOT NULL DEFAULT '',
                    Name TEXT NOT NULL DEFAULT '',
                    SlipNumber TEXT NOT NULL DEFAULT '',
                    Amount REAL NOT NULL DEFAULT 0.0,
                    Date TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL DEFAULT '',
                    SyncGuid TEXT NOT NULL DEFAULT '',
                    FOREIGN KEY (DsmEntryId) REFERENCES DsmEntries (DsmEntryId) ON DELETE CASCADE
                );
            ");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create KhandharePetroleumEntries table");
        }

        // Add ProductId, OverrideSaleRate, AdjustmentQuantity, AdjustmentType to OilDefDailyLogs
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE OilDefDailyLogs ADD COLUMN ProductId INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE OilDefDailyLogs ADD COLUMN OverrideSaleRate REAL NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE OilDefDailyLogs ADD COLUMN AdjustmentQuantity REAL NOT NULL DEFAULT 0.0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE OilDefDailyLogs ADD COLUMN AdjustmentType TEXT NULL;"); } catch { }

        // Add ProductId to OilDefPurchases and OilDefInventories
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE OilDefPurchases ADD COLUMN ProductId INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE OilDefInventories ADD COLUMN ProductId INTEGER NOT NULL DEFAULT 0;"); } catch { }

        // ── SyncIdMappings table migration (RemoteId → RemoteGuid) ──
        // Ensure the table exists with the new schema (fresh installs)
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS SyncIdMappings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    TableName TEXT NOT NULL,
                    RemoteGuid TEXT NOT NULL DEFAULT '',
                    LocalId INTEGER NOT NULL
                );
            ");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create SyncIdMappings table");
        }

        // For existing databases: add the new RemoteGuid column
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SyncIdMappings ADD COLUMN RemoteGuid TEXT NOT NULL DEFAULT '';"); } catch { }

        // Migrate existing RemoteId integer values to RemoteGuid string (one-time data migration)
        try
        {
            // Check if the old RemoteId column exists by querying PRAGMA
            var tableInfo = await context.Database.SqlQueryRaw<string>(
                "SELECT name FROM pragma_table_info('SyncIdMappings') WHERE name = 'RemoteId'").ToListAsync();
            if (tableInfo.Count > 0)
            {
                // Copy RemoteId values (as strings) into RemoteGuid where RemoteGuid is empty
                await context.Database.ExecuteSqlRawAsync(
                    "UPDATE SyncIdMappings SET RemoteGuid = CAST(RemoteId AS TEXT) WHERE RemoteGuid = '' AND RemoteId IS NOT NULL AND RemoteId != 0;");
                Log.Information("Migrated SyncIdMappings.RemoteId values to RemoteGuid");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SyncIdMappings RemoteId→RemoteGuid data migration skipped (may already be done)");
        }

        // Recreate the unique index on (TableName, RemoteGuid) — drop old one if it exists
        try { await context.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS IX_SyncIdMappings_TableName_RemoteId;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("CREATE UNIQUE INDEX IF NOT EXISTS IX_SyncIdMappings_TableName_RemoteGuid ON SyncIdMappings (TableName, RemoteGuid);"); } catch { }

        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SyncChangeLogs ADD COLUMN StationId TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SyncChangeLogs ADD COLUMN MachineId TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SyncChangeLogs ADD COLUMN SyncGuid TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SyncChangeLogs ADD COLUMN RecordGuid TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Settings ADD COLUMN CngRate REAL NOT NULL DEFAULT 85.0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Settings ADD COLUMN FuelRatesJson TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Settings ADD COLUMN Shift1Manager TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Settings ADD COLUMN Shift2Manager TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Settings ADD COLUMN Shift3Manager TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmProfiles ADD COLUMN MobileNumber TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DebitEntries ADD COLUMN SlipNumber TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DebitEntries ADD COLUMN Remarks TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DebitEntries ADD COLUMN CreatedAt TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DebitEntries ADD COLUMN VehicleNumber TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DebitEntries ADD COLUMN PaymentMethod TEXT NOT NULL DEFAULT 'Credit';"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DebitEntries ADD COLUMN CardTid TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DebitEntries ADD COLUMN CardBatch TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DebitEntries ADD COLUMN Denom500 INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DebitEntries ADD COLUMN Denom200 INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DebitEntries ADD COLUMN Denom100 INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DebitEntries ADD COLUMN Denom50 INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DebitEntries ADD COLUMN Denom20 INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DebitEntries ADD COLUMN Denom10 INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DebitEntries ADD COLUMN Coins INTEGER NOT NULL DEFAULT 0;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmPumpAssignments ADD COLUMN CompletedDate TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE KhandharePetroleumEntries ADD COLUMN VehicleNumber TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE KhandharePetroleumEntries ADD COLUMN SyncGuid TEXT NOT NULL DEFAULT '';"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmPersonalDebtors ADD COLUMN SyncGuid TEXT NOT NULL DEFAULT '';"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmPersonalDebtors ADD COLUMN DeductFromSalary INTEGER NOT NULL DEFAULT 1;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmPersonalDebtors ADD COLUMN EntryType TEXT NOT NULL DEFAULT 'Operational';"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmPersonalDebtorRepayments ADD COLUMN SyncGuid TEXT NOT NULL DEFAULT '';"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE DsmQrPayments ADD COLUMN SyncGuid TEXT NOT NULL DEFAULT '';"); } catch { }

        // Backfill any empty SyncGuids with valid UUIDs
        try
        {
            await context.Database.ExecuteSqlRawAsync("UPDATE DsmPersonalDebtors SET SyncGuid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-a' || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))) WHERE SyncGuid IS NULL OR SyncGuid = '';");
            await context.Database.ExecuteSqlRawAsync("UPDATE DsmPersonalDebtorRepayments SET SyncGuid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-a' || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))) WHERE SyncGuid IS NULL OR SyncGuid = '';");
            await context.Database.ExecuteSqlRawAsync("UPDATE DsmQrPayments SET SyncGuid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-a' || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))) WHERE SyncGuid IS NULL OR SyncGuid = '';");
            await context.Database.ExecuteSqlRawAsync("UPDATE KhandharePetroleumEntries SET SyncGuid = lower(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-a' || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))) WHERE SyncGuid IS NULL OR SyncGuid = '';");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to backfill empty SyncGuids in personal debtors / QR tables");
        }

        // Create OuterExpenses table if it does not exist
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS OuterExpenses (
                    OuterExpenseId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Description TEXT NOT NULL,
                    Amount REAL NOT NULL,
                    ExpenseDate TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL
                );
            ");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create OuterExpenses table");
        }

        // Creditors
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS Creditors (
                    CreditorId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Phone TEXT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_Creditors_Name ON Creditors (Name);
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create Creditors table"); }

        // DebtorVehicles
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS DebtorVehicles (
                    DebtorVehicleId INTEGER PRIMARY KEY AUTOINCREMENT,
                    CreditorId INTEGER NOT NULL,
                    VehicleNumber TEXT NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (CreditorId) REFERENCES Creditors (CreditorId) ON DELETE CASCADE
                );
            ");
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE UNIQUE INDEX IF NOT EXISTS IX_DebtorVehicles_CreditorId_VehicleNumber ON DebtorVehicles (CreditorId, VehicleNumber);
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create DebtorVehicles table"); }

        // AuditLogs
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS AuditLogs (
                    AuditLogId INTEGER PRIMARY KEY AUTOINCREMENT,
                    TableName TEXT NOT NULL,
                    RecordId INTEGER NOT NULL,
                    Action TEXT NOT NULL,
                    FieldName TEXT,
                    OldValue TEXT,
                    NewValue TEXT,
                    ModifiedBy TEXT NOT NULL,
                    ModifiedAt TEXT NOT NULL,
                    Reason TEXT
                );
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create AuditLogs table"); }

        // DayLocks
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS DayLocks (
                    DayLockId INTEGER PRIMARY KEY AUTOINCREMENT,
                    LockDate TEXT NOT NULL,
                    LockedAt TEXT NOT NULL,
                    LockedBy TEXT NOT NULL,
                    IsLocked INTEGER NOT NULL DEFAULT 1,
                    UnlockedAt TEXT,
                    UnlockedBy TEXT,
                    UnlockReason TEXT
                );
            ");
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE UNIQUE INDEX IF NOT EXISTS IX_DayLocks_LockDate ON DayLocks (LockDate);
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create DayLocks table"); }

        // SoftwareVersionHistories
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS SoftwareVersionHistories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Version TEXT NOT NULL,
                    BuildConfiguration TEXT,
                    MachineName TEXT,
                    DeployedAt TEXT NOT NULL,
                    Notes TEXT
                );
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create SoftwareVersionHistories table"); }

        // DsmSalaryHistories
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS DsmSalaryHistories (
                    DsmSalaryHistoryId INTEGER PRIMARY KEY AUTOINCREMENT,
                    DsmProfileId INTEGER NOT NULL,
                    OldBaseSalary REAL NOT NULL,
                    NewBaseSalary REAL NOT NULL,
                    OldSalaryType TEXT NOT NULL,
                    NewSalaryType TEXT NOT NULL,
                    ChangeDate TEXT NOT NULL,
                    FOREIGN KEY (DsmProfileId) REFERENCES DsmProfiles (DsmProfileId) ON DELETE CASCADE
                );
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create DsmSalaryHistories table"); }

        // DsmSalaryPayments
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS DsmSalaryPayments (
                    DsmSalaryPaymentId INTEGER PRIMARY KEY AUTOINCREMENT,
                    DsmProfileId INTEGER NOT NULL,
                    Year INTEGER NOT NULL,
                    Month INTEGER NOT NULL,
                    NetSalary REAL NOT NULL,
                    PaidAmount REAL NOT NULL,
                    PaymentDate TEXT NOT NULL,
                    PaymentMode TEXT NOT NULL,
                    Remarks TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (DsmProfileId) REFERENCES DsmProfiles (DsmProfileId) ON DELETE CASCADE
                );
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create DsmSalaryPayments table"); }

        // FuelTankers
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS FuelTankers (
                    FuelTankerId INTEGER PRIMARY KEY AUTOINCREMENT,
                    TankerDate TEXT NOT NULL,
                    TankerNumber TEXT,
                    InvoiceNumber TEXT NOT NULL,
                    FuelType TEXT NOT NULL,
                    Quantity REAL NOT NULL,
                    PurchaseRate REAL NOT NULL,
                    TotalAmount REAL NOT NULL,
                    Density REAL NOT NULL,
                    Remarks TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL
                );
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create FuelTankers table"); }

        // TankDailyStocks
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS TankDailyStocks (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Date TEXT NOT NULL,
                    FuelType TEXT NOT NULL,
                    OpeningStock REAL NOT NULL,
                    DaySaleLitres REAL NOT NULL,
                    TestingLitres REAL NOT NULL,
                    PurchasedLitres REAL NOT NULL,
                    ClosingStock REAL NOT NULL,
                    DipMm REAL NOT NULL,
                    ManualStock REAL NOT NULL,
                    LastUpdated TEXT NOT NULL,
                    ShiftId INTEGER,
                    FOREIGN KEY (ShiftId) REFERENCES Shifts (ShiftId) ON DELETE SET NULL
                );
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create TankDailyStocks table"); }



        // PumpMappings
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS PumpMappings (
                    PumpMappingId INTEGER PRIMARY KEY AUTOINCREMENT,
                    PumpId INTEGER NOT NULL,
                    NozzleNumber INTEGER NOT NULL,
                    FuelType TEXT NOT NULL,
                    TankName TEXT NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL
                );
            ");
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE UNIQUE INDEX IF NOT EXISTS IX_PumpMappings_PumpId_NozzleNumber ON PumpMappings (PumpId, NozzleNumber);
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create PumpMappings table"); }

        // StationLayoutPresets
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS StationLayoutPresets (
                    PresetId INTEGER PRIMARY KEY AUTOINCREMENT,
                    PresetCode TEXT NOT NULL,
                    PresetName TEXT NOT NULL,
                    Description TEXT NOT NULL DEFAULT '',
                    LayoutJson TEXT NOT NULL,
                    PumpCount INTEGER NOT NULL DEFAULT 0,
                    NozzleCount INTEGER NOT NULL DEFAULT 0,
                    TankCount INTEGER NOT NULL DEFAULT 0,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT
                );
            ");
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE UNIQUE INDEX IF NOT EXISTS IX_StationLayoutPresets_PresetCode ON StationLayoutPresets (PresetCode);
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create StationLayoutPresets table"); }

        // ExpenseCategories
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ExpenseCategories (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL
                );
            ");
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ExpenseCategories_Name ON ExpenseCategories (Name);
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create ExpenseCategories table"); }

        // PumpExpenses
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS PumpExpenses (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ExpenseDate TEXT NOT NULL,
                    Rent REAL NOT NULL DEFAULT 0.0,
                    Salary REAL NOT NULL DEFAULT 0.0,
                    TripSheetLoss REAL NOT NULL DEFAULT 0.0,
                    DsmShort REAL NOT NULL DEFAULT 0.0,
                    BankingExpenses REAL NOT NULL DEFAULT 0.0,
                    BpclPortalExpenses REAL NOT NULL DEFAULT 0.0,
                    FuelAndTravel REAL NOT NULL DEFAULT 0.0,
                    OilPurchase REAL NOT NULL DEFAULT 0.0,
                    RepairsAndMaintenance REAL NOT NULL DEFAULT 0.0,
                    ElectricityExpenses REAL NOT NULL DEFAULT 0.0,
                    OfficeExpenses REAL NOT NULL DEFAULT 0.0,
                    PrintingExpense REAL NOT NULL DEFAULT 0.0,
                    OtherDescription TEXT NOT NULL DEFAULT '',
                    OtherAmount REAL NOT NULL DEFAULT 0.0,
                    Remarks TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL
                );
            ");
            await context.Database.ExecuteSqlRawAsync(@"
                DROP INDEX IF EXISTS IX_PumpExpenses_ExpenseDate;
                CREATE INDEX IF NOT EXISTS IX_PumpExpenses_ExpenseDate ON PumpExpenses (ExpenseDate);
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create PumpExpenses table"); }

        // PumpExpenseCategoryItems
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS PumpExpenseCategoryItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PumpExpenseId INTEGER NOT NULL,
                    CategoryId INTEGER NOT NULL,
                    Amount REAL NOT NULL,
                    Remarks TEXT NOT NULL DEFAULT '',
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (PumpExpenseId) REFERENCES PumpExpenses (Id) ON DELETE CASCADE,
                    FOREIGN KEY (CategoryId) REFERENCES ExpenseCategories (Id) ON DELETE RESTRICT
                );
            ");
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE INDEX IF NOT EXISTS IX_PumpExpenseCategoryItems_PumpExpenseId ON PumpExpenseCategoryItems (PumpExpenseId);
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create PumpExpenseCategoryItems table"); }

        // Dynamic Configuration Tables: AppFeatureSettings, CollectionTypes, PaymentCollectionItems, TankDefinitions
        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS AppFeatureSettings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    FeatureKey TEXT NOT NULL,
                    TargetRole TEXT NOT NULL DEFAULT 'Global',
                    DisplayName TEXT NOT NULL DEFAULT '',
                    Description TEXT NOT NULL DEFAULT '',
                    Category TEXT NOT NULL DEFAULT 'General',
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    DisplayOrder INTEGER NOT NULL DEFAULT 0,
                    ConfigurationJson TEXT NULL,
                    UpdatedAt TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_AppFeatureSettings_FeatureKey ON AppFeatureSettings (FeatureKey);
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create AppFeatureSettings table"); }

        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS CollectionTypes (
                    CollectionTypeId INTEGER PRIMARY KEY AUTOINCREMENT,
                    Code TEXT NOT NULL,
                    DisplayName TEXT NOT NULL DEFAULT '',
                    Category TEXT NOT NULL DEFAULT 'Online',
                    HasTidBatch INTEGER NOT NULL DEFAULT 0,
                    DisplayOrder INTEGER NOT NULL DEFAULT 0,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    IsSystem INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_CollectionTypes_Code ON CollectionTypes (Code);
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create CollectionTypes table"); }

        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS PaymentCollectionItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PaymentId INTEGER NOT NULL,
                    CollectionTypeCode TEXT NOT NULL,
                    Amount REAL NOT NULL DEFAULT 0.0,
                    Tid TEXT NULL,
                    Batch TEXT NULL,
                    Slot TEXT NULL DEFAULT 'General',
                    FOREIGN KEY (PaymentId) REFERENCES PaymentCollections (PaymentId) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS IX_PaymentCollectionItems_PaymentId ON PaymentCollectionItems (PaymentId);
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create PaymentCollectionItems table"); }

        try
        {
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS TankDefinitions (
                    TankId INTEGER PRIMARY KEY AUTOINCREMENT,
                    TankName TEXT NOT NULL,
                    CapacityKL REAL NOT NULL DEFAULT 20.0,
                    FuelType TEXT NOT NULL DEFAULT 'HSD',
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    HasTesting INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS IX_TankDefinitions_TankName ON TankDefinitions (TankName);
            ");
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create TankDefinitions table"); }

        // Seed default products
        if (!await context.ProductMasters.AnyAsync())
        {
            context.ProductMasters.AddRange(
                new ProductMaster { ProductName = "Castrol CRB 20W40", Category = "Oil", Unit = "Bottle", DefaultSaleRate = 350.0, IsActive = true },
                new ProductMaster { ProductName = "Servo Pride 15W40", Category = "Oil", Unit = "Litre", DefaultSaleRate = 280.0, IsActive = true },
                new ProductMaster { ProductName = "DEF Bulk", Category = "DEF", Unit = "Litre", DefaultSaleRate = 75.0, IsActive = true },
                new ProductMaster { ProductName = "DEF 10L Can", Category = "DEF", Unit = "Bottle", DefaultSaleRate = 850.0, IsActive = true }
            );
            await context.SaveChangesAsync();
            Log.Information("Seeded default products in ProductMaster");
        }

        // Seed default TankDefinitions
        if (!await context.TankDefinitions.AnyAsync())
        {
            var now = DateTime.Now;
            context.TankDefinitions.AddRange(
                new TankDefinition { TankName = "MS - 20KL", CapacityKL = 20.0, FuelType = "MS-I", IsActive = true, HasTesting = true, CreatedAt = now },
                new TankDefinition { TankName = "HSD - 20KL", CapacityKL = 20.0, FuelType = "HSD", IsActive = true, HasTesting = true, CreatedAt = now },
                new TankDefinition { TankName = "HSD - 20KL II", CapacityKL = 20.0, FuelType = "MS-II", IsActive = true, HasTesting = true, CreatedAt = now },
                new TankDefinition { TankName = "CNG Line", CapacityKL = 5.0, FuelType = "CNG", IsActive = true, HasTesting = false, CreatedAt = now }
            );
            await context.SaveChangesAsync();
            Log.Information("Seeded default TankDefinitions");
        }

        // Seed default CollectionTypes
        if (!await context.CollectionTypes.AnyAsync())
        {
            var now = DateTime.Now;
            context.CollectionTypes.AddRange(
                new CollectionTypeMaster { Code = "PHONEPE", DisplayName = "PhonePe", Category = "Online", HasTidBatch = true, DisplayOrder = 1, IsActive = true, IsSystem = true, CreatedAt = now },
                new CollectionTypeMaster { Code = "CREDIT_CARD", DisplayName = "Credit / Debit Card", Category = "Card", HasTidBatch = true, DisplayOrder = 2, IsActive = true, IsSystem = true, CreatedAt = now },
                new CollectionTypeMaster { Code = "PETROCARD", DisplayName = "PetroCard", Category = "Card", HasTidBatch = true, DisplayOrder = 3, IsActive = true, IsSystem = true, CreatedAt = now },
                new CollectionTypeMaster { Code = "SBI_REDEEM", DisplayName = "SBI Redeem", Category = "Card", HasTidBatch = true, DisplayOrder = 4, IsActive = true, IsSystem = false, CreatedAt = now },
                new CollectionTypeMaster { Code = "PAYTM", DisplayName = "Paytm", Category = "Online", HasTidBatch = true, DisplayOrder = 5, IsActive = true, IsSystem = false, CreatedAt = now },
                new CollectionTypeMaster { Code = "QR", DisplayName = "QR / Online", Category = "Online", HasTidBatch = true, DisplayOrder = 6, IsActive = true, IsSystem = false, CreatedAt = now },
                new CollectionTypeMaster { Code = "MOBIKWIK", DisplayName = "Mobikwik", Category = "Online", HasTidBatch = true, DisplayOrder = 7, IsActive = true, IsSystem = false, CreatedAt = now },
                new CollectionTypeMaster { Code = "CASH_DEPOSIT", DisplayName = "Cash Deposit (Bank)", Category = "Cash", HasTidBatch = false, DisplayOrder = 8, IsActive = true, IsSystem = true, CreatedAt = now },
                new CollectionTypeMaster { Code = "OTHERS", DisplayName = "Other Online / UPI", Category = "Other", HasTidBatch = false, DisplayOrder = 9, IsActive = true, IsSystem = true, CreatedAt = now }
            );
            await context.SaveChangesAsync();
            Log.Information("Seeded default CollectionTypes including SBI Redeem, Paytm, QR, Mobikwik");
        }

        // Seed default AppFeatureSettings
        var featureSeedTimestamp = DateTime.Now;
        if (!await context.AppFeatureSettings.AnyAsync())
        {
            var defaultFeatures = AppFeatureSetting.GetCanonicalDefaults(featureSeedTimestamp);
            context.AppFeatureSettings.AddRange(defaultFeatures);
            await context.SaveChangesAsync();
            Log.Information("Seeded default AppFeatureSettings configuration matrix ({Count} features)", defaultFeatures.Count);

            // Populate Settings.AppFeatureSettingsJson so cloud sync has a coherent snapshot
            try
            {
                var setting = await context.Settings.FirstOrDefaultAsync();
                if (setting != null)
                {
                    setting.AppFeatureSettingsJson = JsonSerializer.Serialize(defaultFeatures);
                    setting.LastUpdated = featureSeedTimestamp;
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to update Settings.AppFeatureSettingsJson during initial feature seeding.");
            }
        }
        else
        {
            // Backfill any missing canonical features into existing DB without overwriting customized settings
            var canonical = AppFeatureSetting.GetCanonicalDefaults(featureSeedTimestamp);
            var existingKeys = (await context.AppFeatureSettings.Select(f => f.FeatureKey).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = canonical.Where(c => !existingKeys.Contains(c.FeatureKey)).ToList();
            if (missing.Count > 0)
            {
                context.AppFeatureSettings.AddRange(missing);
                await context.SaveChangesAsync();
                Log.Information("Added {Count} missing canonical features to existing database", missing.Count);
            }

            // Ensure Settings.AppFeatureSettingsJson is populated if currently null
            try
            {
                var setting = await context.Settings.FirstOrDefaultAsync();
                if (setting != null && string.IsNullOrWhiteSpace(setting.AppFeatureSettingsJson))
                {
                    var allFeatures = await context.AppFeatureSettings.ToListAsync();
                    if (allFeatures.Count > 0)
                    {
                        setting.AppFeatureSettingsJson = JsonSerializer.Serialize(allFeatures);
                        setting.LastUpdated = featureSeedTimestamp;
                        await context.SaveChangesAsync();
                    }
                }
            }
            catch { }
        }


        // Migrate existing logs, purchases, and inventories
        var defaultOil = await context.ProductMasters.FirstOrDefaultAsync(p => p.ProductName == "Castrol CRB 20W40");
        var defaultDef = await context.ProductMasters.FirstOrDefaultAsync(p => p.ProductName == "DEF Bulk");

        if (defaultOil != null && defaultDef != null)
        {
            var unmigratedLogs = await context.OilDefDailyLogs.Where(l => l.ProductId == 0).ToListAsync();
            if (unmigratedLogs.Any())
            {
                foreach (var log in unmigratedLogs)
                {
                    log.ProductId = string.Equals(log.ProductType, "DEF", StringComparison.OrdinalIgnoreCase) ? defaultDef.Id : defaultOil.Id;
                }
                await context.SaveChangesAsync();
                Log.Information("Migrated {Count} OilDefDailyLogs to new ProductMaster schema", unmigratedLogs.Count);
            }

            var unmigratedPurchases = await context.OilDefPurchases.Where(p => p.ProductId == 0).ToListAsync();
            if (unmigratedPurchases.Any())
            {
                foreach (var purchase in unmigratedPurchases)
                {
                    purchase.ProductId = string.Equals(purchase.ProductType, "DEF", StringComparison.OrdinalIgnoreCase) ? defaultDef.Id : defaultOil.Id;
                }
                await context.SaveChangesAsync();
                Log.Information("Migrated {Count} OilDefPurchases to new ProductMaster schema", unmigratedPurchases.Count);
            }

            var unmigratedInventories = await context.OilDefInventories.Where(i => i.ProductId == 0).ToListAsync();
            if (unmigratedInventories.Any())
            {
                foreach (var inv in unmigratedInventories)
                {
                    inv.ProductId = string.Equals(inv.ProductType, "DEF", StringComparison.OrdinalIgnoreCase) ? defaultDef.Id : defaultOil.Id;
                }
                await context.SaveChangesAsync();
                Log.Information("Migrated {Count} OilDefInventories to new ProductMaster schema", unmigratedInventories.Count);
            }
        }

        // Deduplicate duplicate ProductMasters in database
        try
        {
            var allProducts = await context.ProductMasters.OrderBy(p => p.Id).ToListAsync();
            var duplicateGroups = allProducts
                .GroupBy(p => p.ProductName.Trim().ToLowerInvariant())
                .Where(g => g.Count() > 1)
                .ToList();

            if (duplicateGroups.Any())
            {
                foreach (var group in duplicateGroups)
                {
                    var primary = group.First();
                    var duplicates = group.Skip(1).ToList();
                    var duplicateIds = duplicates.Select(d => d.Id).ToList();

                    Log.Information("Deduplicating product '{ProductName}': Primary ID {PrimaryId}, removing duplicate IDs {DuplicateIds}", 
                        primary.ProductName, primary.Id, string.Join(", ", duplicateIds));

                    // 1. Reassign OilDefDailyLogs
                    var logs = await context.OilDefDailyLogs
                        .Where(l => duplicateIds.Contains(l.ProductId))
                        .ToListAsync();
                    foreach (var l in logs)
                    {
                        l.ProductId = primary.Id;
                    }

                    // 2. Reassign OilDefPurchases
                    var purchases = await context.OilDefPurchases
                        .Where(p => duplicateIds.Contains(p.ProductId))
                        .ToListAsync();
                    foreach (var p in purchases)
                    {
                        p.ProductId = primary.Id;
                    }

                    // 3. Reassign OilDefInventories
                    var inventories = await context.OilDefInventories
                        .Where(i => duplicateIds.Contains(i.ProductId))
                        .ToListAsync();
                    foreach (var i in inventories)
                    {
                        i.ProductId = primary.Id;
                    }

                    // 4. Update SyncIdMappings
                    var mappings = await context.SyncIdMappings
                        .Where(m => m.TableName == "ProductMasters" && duplicateIds.Contains(m.LocalId))
                        .ToListAsync();
                    foreach (var m in mappings)
                    {
                        m.LocalId = primary.Id;
                    }

                    // 5. Remove duplicates
                    context.ProductMasters.RemoveRange(duplicates);
                }
                await context.SaveChangesAsync();
                Log.Information("Successfully deduplicated {Count} product groups in ProductMaster", duplicateGroups.Count);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to deduplicate ProductMasters");
        }

        // Migrate legacy "Admin" role → "Manager"
        var adminUsers = await context.Users
            .Where(u => u.Role == "Admin")
            .ToListAsync();
        if (adminUsers.Any())
        {
            foreach (var user in adminUsers)
            {
                user.Role = "Manager";
            }
            Log.Information("Migrated {Count} Admin user(s) to Manager role", adminUsers.Count);
        }

        // Seed default Manager user if no users exist at all
        if (!await context.Users.AnyAsync())
        {
            var managerUser = new User
            {
                Username = "Admin",
                PinHash = BCrypt.Net.BCrypt.HashPassword("1234"),
                Role = "Manager",
                IsActive = true,
                MustChangePin = true,
                CreatedAt = DateTime.Now
            };

            context.Users.Add(managerUser);
            Log.Information("Seeded default Manager user");
        }

        // Seed default Owner user if no Owner exists
        if (!await context.Users.AnyAsync(u => u.Role == "Owner"))
        {
            var ownerUser = new User
            {
                Username = "Owner",
                PinHash = BCrypt.Net.BCrypt.HashPassword("5678"),
                Role = "Owner",
                IsActive = true,
                MustChangePin = true,
                CreatedAt = DateTime.Now
            };

            context.Users.Add(ownerUser);
            Log.Information("Seeded default Owner user");
        }

        // Seed default Developer user if no Developer exists
        if (!await context.Users.AnyAsync(u => u.Role == "Developer" || u.Username == "Developer"))
        {
            const string fixedPin = "825837";
            var devUser = new User
            {
                Username = "Developer",
                PinHash = BCrypt.Net.BCrypt.HashPassword(fixedPin),
                Role = "Developer",
                IsActive = true,
                MustChangePin = false,
                CreatedAt = DateTime.Now
            };

            context.Users.Add(devUser);
            
            try
            {
                var fileService = credentialFileService ?? new LocalCredentialFileService();
                await fileService.WriteCredentialAsync("Developer", fixedPin);
                Log.Information("Developer account seeded with fixed credentials.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to write Developer credentials using file service");
            }
        }

        // Seed default settings if none exist
        var settings = await context.Settings.FirstOrDefaultAsync();
        if (settings == null)
        {
            var defaultSettings = new Setting
            {
                HsdRate = 90.35,
                MsIRate = 103.81,
                MsIIRate = 103.81,
                CngRate = 85.0,
                PumpStationName = "Mitali Service Station",
                LastUpdated = DateTime.Now
            };
            context.Settings.Add(defaultSettings);
        }
        else if (string.IsNullOrWhiteSpace(settings.PumpStationName))
        {
            settings.PumpStationName = "Mitali Service Station";
            context.Entry(settings).State = EntityState.Modified;
        }

        // Seed default Supabase Sync configuration if not configured yet
        async Task SetMetaDefaultAsync(string key, string val)
        {
            var existing = await context.AppMeta.FirstOrDefaultAsync(m => m.Key == key);
            if (existing == null)
            {
                context.AppMeta.Add(new AppMeta { Key = key, Value = val });
            }
        }
        await SetMetaDefaultAsync("Sync.SupabaseUrl", "https://rvcibryprvjbzrtwqktk.supabase.co");
        await SetMetaDefaultAsync("Sync.SupabaseApiKey", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ2Y2licnlwcnZqYnpydHdxa3RrIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODEzMTIxMTcsImV4cCI6MjA5Njg4ODExN30.vMTA97993upfnOCs5ja-kxIhDSHbcx1gEQ6itNm5BBk");
        await SetMetaDefaultAsync("Sync.IsEnabled", "true");

        // Seed PumpMappings if empty
        if (!await context.PumpMappings.AnyAsync())
        {
            var mappings = new List<PumpMapping>();
            var now = DateTime.Now;

            // Pump 1: Nozzle 1 (MS-I/Petrol, MS - 20KL), Nozzle 3 (HSD/Diesel, HSD - 20KL)
            mappings.Add(new PumpMapping { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", TankName = "MS - 20KL", CreatedAt = now });
            mappings.Add(new PumpMapping { PumpId = 1, NozzleNumber = 3, FuelType = "HSD", TankName = "HSD - 20KL", CreatedAt = now });

            // Pump 2: Nozzle 2 (MS-I/Petrol, MS - 20KL), Nozzle 4 (HSD/Diesel, HSD - 20KL)
            mappings.Add(new PumpMapping { PumpId = 2, NozzleNumber = 2, FuelType = "MS-I", TankName = "MS - 20KL", CreatedAt = now });
            mappings.Add(new PumpMapping { PumpId = 2, NozzleNumber = 4, FuelType = "HSD", TankName = "HSD - 20KL", CreatedAt = now });

            // Pump 3: Nozzle 5 (MS-I/Petrol, MS - 20KL), Nozzle 7 (HSD/Diesel, HSD - 20KL II)
            mappings.Add(new PumpMapping { PumpId = 3, NozzleNumber = 5, FuelType = "MS-I", TankName = "MS - 20KL", CreatedAt = now });
            mappings.Add(new PumpMapping { PumpId = 3, NozzleNumber = 7, FuelType = "HSD", TankName = "HSD - 20KL II", CreatedAt = now });

            // Pump 4: Nozzle 6 (MS-I/Petrol, MS - 20KL), Nozzle 8 (HSD/Diesel, HSD - 20KL II)
            mappings.Add(new PumpMapping { PumpId = 4, NozzleNumber = 6, FuelType = "MS-I", TankName = "MS - 20KL", CreatedAt = now });
            mappings.Add(new PumpMapping { PumpId = 4, NozzleNumber = 8, FuelType = "HSD", TankName = "HSD - 20KL II", CreatedAt = now });

            // Pump 5: Nozzle 9 (MS-I/Petrol, MS - 20KL), Nozzle 11 (HSD/Diesel, HSD - 20KL)
            mappings.Add(new PumpMapping { PumpId = 5, NozzleNumber = 9, FuelType = "MS-I", TankName = "MS - 20KL", CreatedAt = now });
            mappings.Add(new PumpMapping { PumpId = 5, NozzleNumber = 11, FuelType = "HSD", TankName = "HSD - 20KL", CreatedAt = now });

            // Pump 6: Nozzle 10 (MS-I/Petrol, MS - 20KL), Nozzle 12 (HSD/Diesel, HSD - 20KL)
            mappings.Add(new PumpMapping { PumpId = 6, NozzleNumber = 10, FuelType = "MS-I", TankName = "MS - 20KL", CreatedAt = now });
            mappings.Add(new PumpMapping { PumpId = 6, NozzleNumber = 12, FuelType = "HSD", TankName = "HSD - 20KL", CreatedAt = now });

            context.PumpMappings.AddRange(mappings);
            await context.SaveChangesAsync();
            Log.Information("Seeded default 6-pump mapping layout.");
        }

        // Seed SoftwareVersionHistory entry
        try
        {
            var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
            var buildConfig = "Release";
#if DEBUG
            buildConfig = "Debug";
#endif
            var lastVersion = await context.SoftwareVersionHistories
                .OrderByDescending(v => v.DeployedAt)
                .FirstOrDefaultAsync();

            if (lastVersion == null || lastVersion.Version != currentVersion)
            {
                var newVersion = new SoftwareVersionHistory
                {
                    Version = currentVersion,
                    BuildConfiguration = buildConfig,
                    MachineName = Environment.MachineName,
                    DeployedAt = DateTime.Now,
                    Notes = lastVersion == null
                        ? "Initial PyroSync_Max deployment"
                        : $"Upgrade from {lastVersion.Version} to {currentVersion}"
                };
                context.SoftwareVersionHistories.Add(newVersion);
                Log.Information("Logged new version history: {Version}", currentVersion);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to record software version history");
        }

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Detects legacy databases where schemas were materialized outside of EF Core migrations,
    /// and safely baselines existing migrations in __EFMigrationsHistory so future migrations execute cleanly.
    /// </summary>
    public static async Task BaselineLegacyMigrationsIfRequiredAsync(FuelProDbContext context)
    {
        if (!context.Database.IsRelational()) return;

        var connection = context.Database.GetDbConnection();
        bool closeConnection = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
            closeConnection = true;
        }

        try
        {
            // 1. Detect if legacy core tables exist
            var isLegacyDb = TableExists(connection, "Settings") && 
                             TableExists(connection, "Shifts") && 
                             TableExists(connection, "DsmEntries");

            if (!isLegacyDb)
            {
                // Brand new database or fresh install — let standard EF migrations handle everything
                return;
            }

            // 2. Ensure __EFMigrationsHistory exists so we can inspect/baseline safely
            await context.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
                    ""MigrationId"" TEXT NOT NULL CONSTRAINT ""PK___EFMigrationsHistory"" PRIMARY KEY,
                    ""ProductVersion"" TEXT NOT NULL
                );");

            // 3. Inspect migration state
            var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var allMigrations = context.Database.GetMigrations().ToList();

            var unappliedMigrations = allMigrations.Where(m => !appliedMigrations.Contains(m)).ToList();
            if (unappliedMigrations.Count == 0)
            {
                // All migrations already recorded
                return;
            }

            Log.Information("Legacy database detected with {AppliedCount} applied and {UnappliedCount} unrecorded migrations. Evaluating schema compatibility for baselining.",
                appliedMigrations.Count, unappliedMigrations.Count);

            var productVersion = typeof(DbContext).Assembly.GetName().Version?.ToString() ?? "8.0.11";
            int baselinedCount = 0;

            foreach (var migrationId in unappliedMigrations)
            {
                if (IsMigrationSchemaPresent(connection, migrationId))
                {
                    using var insertCmd = connection.CreateCommand();
                    insertCmd.CommandText = @"
                        INSERT OR IGNORE INTO ""__EFMigrationsHistory"" (""MigrationId"", ""ProductVersion"")
                        VALUES (@mId, @pVer);";

                    var p1 = insertCmd.CreateParameter();
                    p1.ParameterName = "@mId";
                    p1.Value = migrationId;
                    insertCmd.Parameters.Add(p1);

                    var p2 = insertCmd.CreateParameter();
                    p2.ParameterName = "@pVer";
                    p2.Value = productVersion;
                    insertCmd.Parameters.Add(p2);

                    await insertCmd.ExecuteNonQueryAsync();
                    baselinedCount++;
                }
                else
                {
                    Log.Information("Migration {MigrationId} schema is not yet materialized in legacy DB. Leaving as pending for MigrateAsync.", migrationId);
                }
            }

            if (baselinedCount > 0)
            {
                Log.Information("Legacy database baseline completed: safely baselined {BaselinedCount} pre-existing migrations into __EFMigrationsHistory. Remaining pending migrations: {PendingCount}.",
                    baselinedCount, unappliedMigrations.Count - baselinedCount);
            }
        }
        finally
        {
            if (closeConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static bool IsMigrationSchemaPresent(DbConnection connection, string migrationId)
    {
        return migrationId switch
        {
            "20260421082012_InitialCreate" => TableExists(connection, "Settings") && TableExists(connection, "Shifts") && TableExists(connection, "DsmEntries"),
            "20260421104914_AddOthersToPayment" => ColumnExists(connection, "PaymentCollections", "Others"),
            "20260421125454_ReconciliationV2" => ColumnExists(connection, "DsmEntries", "GrossSales") && ColumnExists(connection, "DsmEntries", "TotalCollection"),
            "20260421131956_AddCashDepositToPayment" => ColumnExists(connection, "PaymentCollections", "CashDeposit"),
            "20260422140102_AddLitresAndRateToTesting" => ColumnExists(connection, "TestingEntries", "Litres"),
            "20260422161230_AddDsmProfilesAndCreditorRepayments" => TableExists(connection, "DsmProfiles") && TableExists(connection, "CreditorRepayments"),
            "20260424052601_AddAgsImport" => TableExists(connection, "AgsDailySummaries"),
            "20260424053749_AddChequeNoToDebitEntry" => ColumnExists(connection, "DebitEntries", "ChequeNo"),
            "20260509193233_SplitPineLabCard" => ColumnExists(connection, "PaymentCollections", "PhonePe") && ColumnExists(connection, "PaymentCollections", "PhonePeCard"),
            "20260619105954_AddDsmPhase1Tables" => TableExists(connection, "DsmUsers") && TableExists(connection, "DsmPumpAssignments"),
            "20260619130135_AddEmailToDsmUser" => ColumnExists(connection, "DsmUsers", "Email"),
            "20260626182136_AddP3ShinNvlAndCardSettlement" => ColumnExists(connection, "PaymentCollections", "CardTid") || ColumnExists(connection, "DebitEntries", "VehicleNumber"),
            "20260710200724_AddDayPeriodPaymentFields" => ColumnExists(connection, "PaymentCollections", "CreditCardDay") || ColumnExists(connection, "PaymentCollections", "PhonePeDay"),
            "20260711123112_AddDsmPumpAssignmentCompletedDate" => ColumnExists(connection, "DsmPumpAssignments", "CompletedDate"),
            "20260721090022_AddKhandharePetroleumEntries" => TableExists(connection, "KhandharePetroleumEntries"),
            var m when m.EndsWith("_AddOpeningBalancesTable") => TableExists(connection, "OpeningBalances") && ColumnExists(connection, "DsmPersonalDebtors", "EntryType"),
            _ => false // For any new/unrecognized future migration, return false so MigrateAsync() executes it!
        };
    }

    private static bool TableExists(DbConnection connection, string tableName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name = @name;";
        var p = cmd.CreateParameter();
        p.ParameterName = "@name";
        p.Value = tableName;
        cmd.Parameters.Add(p);
        return cmd.ExecuteScalar() != null;
    }

    private static bool ColumnExists(DbConnection connection, string tableName, string columnName)
    {
        if (!TableExists(connection, tableName)) return false;
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{tableName}\");";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
