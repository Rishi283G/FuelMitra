using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
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
        // Ensure database is created and migrated
        if (context.Database.IsRelational())
        {
            await context.Database.MigrateAsync();
        }
        else
        {
            await context.Database.EnsureCreatedAsync();
        }

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
                CREATE UNIQUE INDEX IF NOT EXISTS IX_PumpExpenses_ExpenseDate ON PumpExpenses (ExpenseDate);
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

        // Seed default settings or migrate legacy pump name
        var settings = await context.Settings.FirstOrDefaultAsync();
        if (settings == null)
        {
            var defaultSettings = new Setting
            {
                HsdRate = 90.35,
                MsIRate = 103.81,
                MsIIRate = 103.81,
                CngRate = 85.0,
                PumpStationName = "Kandhare Petroleum",
                LastUpdated = DateTime.Now
            };
            context.Settings.Add(defaultSettings);
        }
        else
        {
            settings.PumpStationName = "Kandhare Petroleum";
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

        // Seed PumpMappings if empty, or if count/structure doesn't match the new global 6-pump/12-nozzle layout
        // Also re-seed if the old sequential mapping is detected (nozzle 2 on Pump 1 = wrong)
        bool needsReseed = !await context.PumpMappings.AnyAsync() || 
                           await context.PumpMappings.CountAsync() != 12 ||
                           await context.PumpMappings.AnyAsync(m => m.PumpId > 6) ||
                           await context.PumpMappings.AnyAsync(m => m.PumpId == 1 && m.NozzleNumber == 2) ||
                           !await context.PumpMappings.AnyAsync(m => m.TankName == "MS - 20KL"); // Force re-seed for new tank splitting layout
        if (needsReseed)
        {
            if (await context.PumpMappings.AnyAsync())
            {
                context.PumpMappings.RemoveRange(context.PumpMappings);
                await context.SaveChangesAsync();
                Log.Information("Cleared legacy PumpMappings table to re-seed with correct interleaved nozzle layout.");
            }

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
            Log.Information("Seeded 6-pump mapping with interleaved nozzle numbers for Mitali Service Station");
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
}
