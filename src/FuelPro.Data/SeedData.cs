using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using BCrypt.Net;
using Serilog;

namespace FuelPro.Data;

/// <summary>
/// Seeds default data on first run: manager user, owner user, and default settings.
/// Also migrates legacy "Admin" role to "Manager".
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

        // Add StationId and MachineId columns to SyncChangeLogs if not present
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SyncChangeLogs ADD COLUMN StationId TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SyncChangeLogs ADD COLUMN MachineId TEXT NULL;"); } catch { }

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
            var randomPin = Random.Shared.Next(100000, 999999).ToString("D6");
            var devUser = new User
            {
                Username = "Developer",
                PinHash = BCrypt.Net.BCrypt.HashPassword(randomPin),
                Role = "Developer",
                IsActive = true,
                MustChangePin = false,
                CreatedAt = DateTime.Now
            };

            context.Users.Add(devUser);
            
            try
            {
                var fileService = credentialFileService ?? new LocalCredentialFileService();
                await fileService.WriteCredentialAsync("Developer", randomPin);
                Log.Information("Developer account seeded. Credentials saved using file service.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to write Developer credentials using file service");
            }
        }

        // Seed default settings if none exist
        if (!await context.Settings.AnyAsync())
        {
            var defaultSettings = new Setting
            {
                HsdRate = 90.35,
                MsIRate = 103.81,
                MsIIRate = 103.81,
                PumpStationName = "Shree Mahakaleshwar Petroleum",
                LastUpdated = DateTime.Now
            };

            context.Settings.Add(defaultSettings);
        }

        await context.SaveChangesAsync();
    }
}

