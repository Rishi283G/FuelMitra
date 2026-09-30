using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using Xunit;

namespace FuelPro.Tests;

public class VerificationEnvironmentSetup
{
    private static void EnsureLegacyColumns(FuelProDbContext context)
    {
        var rawSqls = new[]
        {
            "ALTER TABLE Settings ADD COLUMN Shift1Manager TEXT NULL;",
            "ALTER TABLE Settings ADD COLUMN Shift2Manager TEXT NULL;",
            "ALTER TABLE Settings ADD COLUMN Shift3Manager TEXT NULL;",
            "ALTER TABLE Settings ADD COLUMN FuelRatesJson TEXT NULL;",
            "ALTER TABLE Settings ADD COLUMN TankDefinitionsJson TEXT NULL;",
            "ALTER TABLE Settings ADD COLUMN CollectionTypesJson TEXT NULL;",
            "ALTER TABLE Settings ADD COLUMN AppFeatureSettingsJson TEXT NULL;",
            "ALTER TABLE Settings ADD COLUMN PumpMappingsJson TEXT NULL;",
            "ALTER TABLE Settings ADD COLUMN PumpConnectionRulesJson TEXT NULL;",
            "ALTER TABLE PaymentCollections ADD COLUMN DynamicItemsJson TEXT NULL;",
            "ALTER TABLE PaymentCollections ADD COLUMN CashDeposit REAL NOT NULL DEFAULT 0.0;",
            "ALTER TABLE DsmEntries ADD COLUMN ConnectedPumpId INTEGER NULL;",
            "ALTER TABLE DsmEntries ADD COLUMN ReconciledToPumpId INTEGER NULL;",
            "ALTER TABLE PaymentCollections ADD COLUMN PhonePeMorning REAL NOT NULL DEFAULT 0.0;",
            "ALTER TABLE PaymentCollections ADD COLUMN PhonePeNight REAL NOT NULL DEFAULT 0.0;",
            "ALTER TABLE PaymentCollections ADD COLUMN PhonePeCardMorning REAL NOT NULL DEFAULT 0.0;",
            "ALTER TABLE PaymentCollections ADD COLUMN PhonePeCardNight REAL NOT NULL DEFAULT 0.0;",
            "ALTER TABLE DsmEntries ADD COLUMN IsReconciled INTEGER NOT NULL DEFAULT 0;",
            "ALTER TABLE DsmProfiles ADD COLUMN PendingAdvance REAL NOT NULL DEFAULT 0.0;",
            "ALTER TABLE DsmProfiles ADD COLUMN MonthlyAdvanceDeduction REAL NOT NULL DEFAULT 0.0;",
            "ALTER TABLE DsmSalaryAdjustments ADD COLUMN PendingAdvanceDeduction REAL NOT NULL DEFAULT 0.0;",
            "ALTER TABLE DsmPumpAssignments ADD COLUMN ConnectedPumpId INTEGER NULL;",
            "ALTER TABLE DsmPumpAssignments ADD COLUMN CompletedDate TEXT NULL;",
            "ALTER TABLE DsmEntries ADD COLUMN ConnectedPumpIdsJson TEXT NULL;",
            "ALTER TABLE DsmPumpAssignments ADD COLUMN ConnectedPumpIdsJson TEXT NULL;",
            "ALTER TABLE SyncIdMappings ADD COLUMN RemoteGuid TEXT NOT NULL DEFAULT '';",
            "ALTER TABLE SyncChangeLogs ADD COLUMN StationId TEXT NULL;",
            "ALTER TABLE SyncChangeLogs ADD COLUMN MachineId TEXT NULL;",
            "ALTER TABLE SyncChangeLogs ADD COLUMN SyncGuid TEXT NULL;",
            "ALTER TABLE SyncChangeLogs ADD COLUMN RecordGuid TEXT NULL;",
            "ALTER TABLE Settings ADD COLUMN CngRate REAL NOT NULL DEFAULT 85.0;"
        };

        foreach (var sql in rawSqls)
        {
            try { context.Database.ExecuteSqlRaw(sql); } catch { }
        }
    }

    [Fact]
    public async Task SetupVerificationDatabase_PopulatesExactReproductionData()
    {
        var appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro");
        Directory.CreateDirectory(appDataFolder);
        var dbPath = Path.Combine(appDataFolder, "fuelPro.db");

        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        using var context = new FuelProDbContext(options);
        var credentialService = new LocalCredentialFileService(appDataFolder);

        // 1. Initialize schema and seed data
        await SeedData.InitializeAsync(context, credentialService);
        EnsureLegacyColumns(context);

        // 2. Configure Station ID and Supabase sync
        async Task SetMetaAsync(string key, string val)
        {
            var existing = await context.AppMeta.FirstOrDefaultAsync(m => m.Key == key);
            if (existing == null)
            {
                context.AppMeta.Add(new AppMeta { Key = key, Value = val });
            }
            else
            {
                existing.Value = val;
            }
        }

        await SetMetaAsync("Sync.StationId", "RASHTRA-FD51E4");
        await SetMetaAsync("Sync.IsEnabled", "true");
        await SetMetaAsync("Sync.SupabaseUrl", "https://rvcibryprvjbzrtwqktk.supabase.co");
        await SetMetaAsync("Sync.SupabaseApiKey", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ2Y2licnlwcnZqYnpydHdxa3RrIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODEzMTIxMTcsImV4cCI6MjA5Njg4ODExN30.vMTA97993upfnOCs5ja-kxIhDSHbcx1gEQ6itNm5BBk");

        // 3. Configure Settings
        var settings = await context.Settings.FirstOrDefaultAsync();
        if (settings != null)
        {
            settings.PumpStationName = "Mitali Service Station";
            context.Entry(settings).State = EntityState.Modified;
        }

        // 4. Ensure Developer User exists and credential file written
        var devUser = await context.Users.FirstOrDefaultAsync(u => u.Username == "Developer" || u.Role == "Developer");
        const string fixedPin = "825837";
        if (devUser == null)
        {
            devUser = new User
            {
                Username = "Developer",
                PinHash = BCrypt.Net.BCrypt.HashPassword(fixedPin),
                Role = "Developer",
                IsActive = true,
                MustChangePin = false,
                CreatedAt = DateTime.Now
            };
            context.Users.Add(devUser);
        }
        else
        {
            devUser.PinHash = BCrypt.Net.BCrypt.HashPassword(fixedPin);
            devUser.IsActive = true;
            devUser.MustChangePin = false;
        }
        await credentialService.WriteCredentialAsync("Developer", fixedPin);

        // Ensure Manager User exists
        var adminUser = await context.Users.FirstOrDefaultAsync(u => u.Username == "Admin");
        if (adminUser == null)
        {
            adminUser = new User
            {
                Username = "Admin",
                PinHash = BCrypt.Net.BCrypt.HashPassword("1234"),
                Role = "Manager",
                IsActive = true,
                MustChangePin = false,
                CreatedAt = DateTime.Now
            };
            context.Users.Add(adminUser);
        }

        await context.SaveChangesAsync();

        // 5. Seed Shifts for 11-Sep-2026 (Shift A & Shift B)
        var targetDate = new DateTime(2026, 9, 11);
        var shiftA = await context.Shifts.FirstOrDefaultAsync(s => s.ShiftDate.Date == targetDate.Date && s.ShiftType == "A");
        if (shiftA == null)
        {
            shiftA = new Shift
            {
                ShiftDate = targetDate,
                ShiftType = "A",
                IsLocked = false,
                CreatedAt = new DateTime(2026, 9, 11, 17, 57, 25)
            };
            context.Shifts.Add(shiftA);
            await context.SaveChangesAsync();
        }

        var shiftB = await context.Shifts.FirstOrDefaultAsync(s => s.ShiftDate.Date == targetDate.Date && s.ShiftType == "B");
        if (shiftB == null)
        {
            shiftB = new Shift
            {
                ShiftDate = targetDate,
                ShiftType = "B",
                IsLocked = false,
                CreatedAt = new DateTime(2026, 9, 11, 18, 0, 23)
            };
            context.Shifts.Add(shiftB);
            await context.SaveChangesAsync();
        }

        // 6. Seed DsmEntries for Ramesh Jadhav
        var dsmEntryA = await context.DsmEntries.FirstOrDefaultAsync(e => e.ShiftId == shiftA.ShiftId && e.DsmName == "Ramesh Jadhav");
        if (dsmEntryA == null)
        {
            dsmEntryA = new DsmEntry
            {
                ShiftId = shiftA.ShiftId,
                PumpId = 1,
                DsmName = "Ramesh Jadhav",
                GrossSales = 10824.10m,
                TotalCollection = 7630.00m,
                Mismatch = -2.50m,
                CreatedAt = shiftA.CreatedAt,
                UpdatedAt = shiftA.CreatedAt
            };
            context.DsmEntries.Add(dsmEntryA);
            await context.SaveChangesAsync();
        }
        else
        {
            dsmEntryA.GrossSales = 10824.10m;
            dsmEntryA.TotalCollection = 7630.00m;
            dsmEntryA.Mismatch = -2.50m;
            await context.SaveChangesAsync();
        }

        var dsmEntryB = await context.DsmEntries.FirstOrDefaultAsync(e => e.ShiftId == shiftB.ShiftId && e.DsmName == "Ramesh Jadhav");
        if (dsmEntryB == null)
        {
            dsmEntryB = new DsmEntry
            {
                ShiftId = shiftB.ShiftId,
                PumpId = 1,
                DsmName = "Ramesh Jadhav",
                GrossSales = 14842.60m,
                TotalCollection = 14820.00m,
                Mismatch = -22.60m,
                CreatedAt = shiftB.CreatedAt,
                UpdatedAt = shiftB.CreatedAt
            };
            context.DsmEntries.Add(dsmEntryB);
            await context.SaveChangesAsync();
        }
        else
        {
            dsmEntryB.GrossSales = 14842.60m;
            dsmEntryB.TotalCollection = 14820.00m;
            dsmEntryB.Mismatch = -22.60m;
            await context.SaveChangesAsync();
        }

        // 7. Seed DsmPersonalDebtors (Shift A = 2.50, Shift B = 22.60)
        var debtorA = await context.DsmPersonalDebtors.FirstOrDefaultAsync(d => d.DsmEntryId == dsmEntryA.DsmEntryId);
        if (debtorA == null)
        {
            debtorA = new DsmPersonalDebtor
            {
                DsmEntryId = dsmEntryA.DsmEntryId,
                DsmName = "Ramesh Jadhav",
                Date = targetDate,
                Time = "06:00 PM",
                Amount = 2.50,
                Remarks = "Auto Shift Shortage (Pump 1, Shift A)",
                PaymentMethod = "Cash",
                DeductFromSalary = true,
                CreatedAt = shiftA.CreatedAt
            };
            context.DsmPersonalDebtors.Add(debtorA);
        }
        else
        {
            debtorA.Amount = 2.50;
            debtorA.Remarks = "Auto Shift Shortage (Pump 1, Shift A)";
            debtorA.DsmName = "Ramesh Jadhav";
        }

        var debtorB = await context.DsmPersonalDebtors.FirstOrDefaultAsync(d => d.DsmEntryId == dsmEntryB.DsmEntryId);
        if (debtorB == null)
        {
            debtorB = new DsmPersonalDebtor
            {
                DsmEntryId = dsmEntryB.DsmEntryId,
                DsmName = "Ramesh Jadhav",
                Date = targetDate,
                Time = "06:00 PM",
                Amount = 22.60,
                Remarks = "Auto Shift Shortage (Pump 1, Shift B)",
                PaymentMethod = "Cash",
                DeductFromSalary = true,
                CreatedAt = shiftB.CreatedAt
            };
            context.DsmPersonalDebtors.Add(debtorB);
        }
        else
        {
            debtorB.Amount = 22.60;
            debtorB.Remarks = "Auto Shift Shortage (Pump 1, Shift B)";
            debtorB.DsmName = "Ramesh Jadhav";
        }

        await context.SaveChangesAsync();

        // 8. SyncIdMappings configuration
        async Task EnsureSyncMappingAsync(string tableName, int localId, string remoteGuid)
        {
            var mapping = await context.SyncIdMappings.FirstOrDefaultAsync(m => m.TableName == tableName && m.LocalId == localId);
            if (mapping == null)
            {
                context.SyncIdMappings.Add(new SyncIdMapping
                {
                    TableName = tableName,
                    LocalId = localId,
                    RemoteGuid = remoteGuid
                });
            }
            else
            {
                mapping.RemoteGuid = remoteGuid;
            }
        }

        await EnsureSyncMappingAsync("Shifts", shiftA.ShiftId, "1322b8a1-e560-4780-ac4a-a8f7f037e4e3");
        await EnsureSyncMappingAsync("Shifts", shiftB.ShiftId, "a7191af5-55b1-4803-b96c-2fc620d2536d");
        await EnsureSyncMappingAsync("DsmEntries", dsmEntryA.DsmEntryId, "641d1096-8b63-42b2-a464-7958efa3c68e");
        await EnsureSyncMappingAsync("DsmEntries", dsmEntryB.DsmEntryId, "06a8ab9e-eeee-42d3-a904-108c54427ec2");
        await EnsureSyncMappingAsync("DsmPersonalDebtors", debtorA.Id, "b7b00024-421d-49b4-b9ed-9f5e0a82435d");
        await EnsureSyncMappingAsync("DsmPersonalDebtors", debtorB.Id, "f914cbbf-0bd9-46d1-a500-5255cd9adf72");

        await context.SaveChangesAsync();

        // 9. Assertions to guarantee test dataset correctness
        var allDebtors = await context.DsmPersonalDebtors
            .Where(d => d.DsmName == "Ramesh Jadhav" && d.Date.Date == targetDate.Date)
            .OrderBy(d => d.Amount)
            .ToListAsync();

        Assert.Equal(2, allDebtors.Count);
        Assert.Equal(2.50, allDebtors[0].Amount, 2);
        Assert.Equal(22.60, allDebtors[1].Amount, 2);
        Assert.Equal(25.10, allDebtors.Sum(d => d.Amount), 2);
        Assert.Contains("Shift A", allDebtors[0].Remarks);
        Assert.Contains("Shift B", allDebtors[1].Remarks);
    }
}
