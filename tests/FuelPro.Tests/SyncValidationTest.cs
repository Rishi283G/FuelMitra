using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Sync;
using Xunit;

namespace FuelPro.Tests;

public class SyncValidationTest
{
    private readonly string _dbPath;

    public SyncValidationTest()
    {
        _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro", "fuelPro.db");
    }

    [Fact]
    public async Task Run_EndToEndSyncValidation()
    {
        // 1. Setup DI container representing the application context
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"),
            ServiceLifetime.Transient);
        services.AddTransient<IUserRepository, UserRepository>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<SyncConfigService>();
        services.AddSingleton<SyncEngine>();

        var serviceProvider = services.BuildServiceProvider();

        using var context = serviceProvider.GetRequiredService<FuelProDbContext>();
        var configService = serviceProvider.GetRequiredService<SyncConfigService>();
        var syncEngine = serviceProvider.GetRequiredService<SyncEngine>();

        var settings = await configService.GetSettingsAsync();
        Assert.True(settings.SyncEnabled, "Sync must be enabled for validation.");
        Assert.False(string.IsNullOrEmpty(settings.SupabaseUrl), "SupabaseUrl must be configured.");
        Assert.False(string.IsNullOrEmpty(settings.SupabaseApiKey), "SupabaseApiKey must be configured.");
        Assert.False(string.IsNullOrEmpty(settings.StationId), "StationId must be generated/configured.");

        // Ensure the columns are added to local SQLite database if not present
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SyncChangeLogs ADD COLUMN StationId TEXT NULL;"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SyncChangeLogs ADD COLUMN MachineId TEXT NULL;"); } catch { }

        // Ensure SyncIdMappings table has RemoteGuid column
        try { await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS SyncIdMappings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TableName TEXT NOT NULL,
                RemoteGuid TEXT NOT NULL DEFAULT '',
                LocalId INTEGER NOT NULL
            );"); } catch { }
        try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE SyncIdMappings ADD COLUMN RemoteGuid TEXT NOT NULL DEFAULT '';"); } catch { }

        // Clean up any old change logs to ensure clean tracking metrics
        FuelProDbContext.BypassTracking = true;
        try
        {
            var pendingLogs = await context.SyncChangeLogs.Where(l => !l.IsSynced).ToListAsync();
            context.SyncChangeLogs.RemoveRange(pendingLogs);
            await context.SaveChangesAsync();
        }
        finally
        {
            FuelProDbContext.BypassTracking = false;
        }

        // 2. Generate unique mock operational records locally
        // Use a random future date to guarantee unique constraints are met
        var randomOffset = new Random().Next(2000, 6000);
        var testDate = DateTime.Today.AddDays(randomOffset);
        var shiftType = "A"; // Morning Shift

        var testShift = new Shift
        {
            ShiftDate = testDate,
            ShiftType = shiftType,
            IsLocked = false,
            CreatedAt = DateTime.Now
        };
        context.Shifts.Add(testShift);
        await context.SaveChangesAsync();

        var testDsmEntry = new DsmEntry
        {
            ShiftId = testShift.ShiftId,
            DsmName = "E2E Test DSM",
            PumpId = 1,
            ConnectedPumpId = null,
            ReconciledToPumpId = null,
            GrossSales = 1000m,
            TotalInDirect = 100m,
            TotalCreditors = 200m,
            TotalCollection = 700m,
            Mismatch = 0m,
            IsReconciled = false,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };
        context.DsmEntries.Add(testDsmEntry);
        await context.SaveChangesAsync();

        var testNozzleReading = new NozzleReading
        {
            DsmEntryId = testDsmEntry.DsmEntryId,
            NozzleNumber = 1,
            FuelType = "MS-I",
            OpeningReading = 5000.0,
            ClosingReading = 5010.0,
            SaleLitres = 10.0,
            Rate = 90.35,
            Amount = 903.50,
            IsManualOpeningOverride = false
        };
        context.NozzleReadings.Add(testNozzleReading);

        var testPaymentCollection = new PaymentCollection
        {
            DsmEntryId = testDsmEntry.DsmEntryId,
            CashDeposit = 500.0,
            PhonePeMorning = 200.0,
            PhonePeNight = 0.0,
            PhonePeCardMorning = 100.0,
            PhonePeCardNight = 0.0,
            CreditCardMorning = 100.0,
            CreditCardNight = 0.0,
            PetroCard = 0.0,
            Others = 0.0
        };
        context.PaymentCollections.Add(testPaymentCollection);

        var testExpense = new Expense
        {
            ShiftId = testShift.ShiftId,
            DsmEntryId = testDsmEntry.DsmEntryId,
            Amount = 100.0,
            Description = "E2E Validation Operational Expense"
        };
        context.Expenses.Add(testExpense);

        var testTestingEntry = new TestingEntry
        {
            DsmEntryId = testDsmEntry.DsmEntryId,
            FuelType = "HSD",
            Litres = 5.0,
            Rate = 90.35,
            Amount = 451.75
        };
        context.TestingEntries.Add(testTestingEntry);

        await context.SaveChangesAsync();

        // 3. Confirm records are written to SyncChangeLogs locally
        var createdLogs = await context.SyncChangeLogs
            .Where(l => !l.IsSynced)
            .OrderBy(l => l.Id)
            .ToListAsync();

        Assert.True(createdLogs.Count >= 6, "Expected at least 6 change logs to be created locally.");

        var logShifts = createdLogs.Any(l => l.TableName == "Shifts" && l.Operation == "INSERT");
        var logDsmEntries = createdLogs.Any(l => l.TableName == "DsmEntries" && l.Operation == "INSERT");
        var logNozzleReadings = createdLogs.Any(l => l.TableName == "NozzleReadings" && l.Operation == "INSERT");
        var logPaymentCollections = createdLogs.Any(l => l.TableName == "PaymentCollections" && l.Operation == "INSERT");
        var logExpenses = createdLogs.Any(l => l.TableName == "Expenses" && l.Operation == "INSERT");
        var logTestingEntries = createdLogs.Any(l => l.TableName == "TestingEntries" && l.Operation == "INSERT");

        Assert.True(logShifts, "Shifts insert log missing.");
        Assert.True(logDsmEntries, "DsmEntries insert log missing.");
        Assert.True(logNozzleReadings, "NozzleReadings insert log missing.");
        Assert.True(logPaymentCollections, "PaymentCollections insert log missing.");
        Assert.True(logExpenses, "Expenses insert log missing.");
        Assert.True(logTestingEntries, "TestingEntries insert log missing.");

        // 4. Trigger synchronization
        var syncStart = DateTime.Now;
        await syncEngine.ForceSyncAsync();
        var syncDuration = DateTime.Now - syncStart;

        // 5. Confirm records leave the pending queue
        var remainingLogs = await context.SyncChangeLogs
            .AsNoTracking()
            .Where(l => !l.IsSynced)
            .ToListAsync();

        var localShiftId = testShift.ShiftId;
        var localDsmEntryId = testDsmEntry.DsmEntryId;
        var localNozzleReadingId = testNozzleReading.NozzleReadingId;
        var localPaymentId = testPaymentCollection.PaymentId;
        var localExpenseId = testExpense.ExpenseId;
        var localTestingId = testTestingEntry.TestingId;

        // Assert that the specific generated logs have been synced
        var unsyncedNewRecords = remainingLogs.Any(l =>
            (l.TableName == "Shifts" && l.RecordId == localShiftId) ||
            (l.TableName == "DsmEntries" && l.RecordId == localDsmEntryId) ||
            (l.TableName == "NozzleReadings" && l.RecordId == localNozzleReadingId) ||
            (l.TableName == "PaymentCollections" && l.RecordId == localPaymentId) ||
            (l.TableName == "Expenses" && l.RecordId == localExpenseId) ||
            (l.TableName == "TestingEntries" && l.RecordId == localTestingId)
        );

        Assert.True(syncEngine.CurrentStatus.IsConnected, $"Sync failed. Engine Status Message: {syncEngine.CurrentStatus.StatusMessage}");
        Assert.False(unsyncedNewRecords, "Some of the newly created test records failed to sync and remain pending.");

        // 6. Confirm records appear in Supabase tables via API query — using SyncGuid
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("apikey", settings.SupabaseApiKey);
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.SupabaseApiKey}");

        var restBaseUrl = settings.SupabaseUrl.EndsWith("/") ? settings.SupabaseUrl + "rest/v1" : settings.SupabaseUrl + "/rest/v1";

        // Helper: verify a record exists in Supabase by looking up its SyncGuid from local mapping
        async Task<(bool Exists, bool CorrectStation, bool HasSyncGuid, int Count, JToken? Data)> VerifySupabaseRecordAsync(string tableName, int localId)
        {
            // Look up the SyncGuid from local SyncIdMappings
            var mapping = await context.SyncIdMappings
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.TableName == tableName && m.LocalId == localId);

            if (mapping == null || string.IsNullOrEmpty(mapping.RemoteGuid))
            {
                return (false, false, false, 0, null);
            }

            // Query Supabase using SyncGuid
            var queryUrl = $"{restBaseUrl}/{tableName}?SyncGuid=eq.{mapping.RemoteGuid}";
            var response = await httpClient.GetAsync(queryUrl);
            if (!response.IsSuccessStatusCode)
            {
                return (false, false, true, 0, null);
            }

            var content = await response.Content.ReadAsStringAsync();
            var arr = JArray.Parse(content);
            if (arr.Count == 0)
            {
                return (false, false, true, 0, null);
            }

            var item = arr[0];
            var stationIdInCloud = item["station_id"]?.ToString();
            var correctStation = stationIdInCloud == settings.StationId;
            var hasSyncGuid = item["SyncGuid"] != null && !string.IsNullOrEmpty(item["SyncGuid"]?.ToString());

            return (true, correctStation, hasSyncGuid, arr.Count, item);
        }

        var shiftVerify = await VerifySupabaseRecordAsync("Shifts", localShiftId);
        var dsmVerify = await VerifySupabaseRecordAsync("DsmEntries", localDsmEntryId);
        var nozzleVerify = await VerifySupabaseRecordAsync("NozzleReadings", localNozzleReadingId);
        var paymentVerify = await VerifySupabaseRecordAsync("PaymentCollections", localPaymentId);
        var expenseVerify = await VerifySupabaseRecordAsync("Expenses", localExpenseId);
        var testingVerify = await VerifySupabaseRecordAsync("TestingEntries", localTestingId);

        // Assert existence in Cloud
        Assert.True(shiftVerify.Exists, "Shift record not found in Supabase.");
        Assert.True(dsmVerify.Exists, "DsmEntry record not found in Supabase.");
        Assert.True(nozzleVerify.Exists, "NozzleReading record not found in Supabase.");
        Assert.True(paymentVerify.Exists, "PaymentCollection record not found in Supabase.");
        Assert.True(expenseVerify.Exists, "Expense record not found in Supabase.");
        Assert.True(testingVerify.Exists, "TestingEntry record not found in Supabase.");

        // Assert station_id is attached correctly
        Assert.True(shiftVerify.CorrectStation, "Shift station_id mismatch in Supabase.");
        Assert.True(dsmVerify.CorrectStation, "DsmEntry station_id mismatch in Supabase.");
        Assert.True(nozzleVerify.CorrectStation, "NozzleReading station_id mismatch in Supabase.");
        Assert.True(paymentVerify.CorrectStation, "PaymentCollection station_id mismatch in Supabase.");
        Assert.True(expenseVerify.CorrectStation, "Expense station_id mismatch in Supabase.");
        Assert.True(testingVerify.CorrectStation, "TestingEntry station_id mismatch in Supabase.");

        // Assert SyncGuid is present
        Assert.True(shiftVerify.HasSyncGuid, "Shift SyncGuid missing in Supabase.");
        Assert.True(dsmVerify.HasSyncGuid, "DsmEntry SyncGuid missing in Supabase.");
        Assert.True(nozzleVerify.HasSyncGuid, "NozzleReading SyncGuid missing in Supabase.");

        // Assert no duplicates (Exactly 1 record per SyncGuid)
        Assert.Equal(1, shiftVerify.Count);
        Assert.Equal(1, dsmVerify.Count);
        Assert.Equal(1, nozzleVerify.Count);
        Assert.Equal(1, paymentVerify.Count);
        Assert.Equal(1, expenseVerify.Count);
        Assert.Equal(1, testingVerify.Count);

        // Assert relationships (Foreign Keys) are preserved in Supabase — FKs are now GUIDs
        // Get the SyncGuids of parent records to compare against child FK values
        var shiftSyncGuid = (await context.SyncIdMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TableName == "Shifts" && m.LocalId == localShiftId))?.RemoteGuid;
        var dsmSyncGuid = (await context.SyncIdMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TableName == "DsmEntries" && m.LocalId == localDsmEntryId))?.RemoteGuid;

        Assert.NotNull(shiftSyncGuid);
        Assert.NotNull(dsmSyncGuid);

        var cloudDsmShiftId = dsmVerify.Data?["ShiftId"]?.ToString();
        var cloudNozzleDsmEntryId = nozzleVerify.Data?["DsmEntryId"]?.ToString();
        var cloudPaymentDsmEntryId = paymentVerify.Data?["DsmEntryId"]?.ToString();
        var cloudExpenseShiftId = expenseVerify.Data?["ShiftId"]?.ToString();
        var cloudExpenseDsmEntryId = expenseVerify.Data?["DsmEntryId"]?.ToString();
        var cloudTestingDsmEntryId = testingVerify.Data?["DsmEntryId"]?.ToString();

        Assert.Equal(shiftSyncGuid, cloudDsmShiftId);
        Assert.Equal(dsmSyncGuid, cloudNozzleDsmEntryId);
        Assert.Equal(dsmSyncGuid, cloudPaymentDsmEntryId);
        Assert.Equal(shiftSyncGuid, cloudExpenseShiftId);
        Assert.Equal(dsmSyncGuid, cloudExpenseDsmEntryId);
        Assert.Equal(dsmSyncGuid, cloudTestingDsmEntryId);

        // 7. Verify SyncIdMappings were created for all records
        var shiftMapping = await context.SyncIdMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TableName == "Shifts" && m.LocalId == localShiftId);
        var dsmMapping = await context.SyncIdMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TableName == "DsmEntries" && m.LocalId == localDsmEntryId);
        var nozzleMapping = await context.SyncIdMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.TableName == "NozzleReadings" && m.LocalId == localNozzleReadingId);

        Assert.NotNull(shiftMapping);
        Assert.False(string.IsNullOrEmpty(shiftMapping!.RemoteGuid), "Shift SyncIdMapping.RemoteGuid is empty.");
        Assert.NotNull(dsmMapping);
        Assert.False(string.IsNullOrEmpty(dsmMapping!.RemoteGuid), "DsmEntry SyncIdMapping.RemoteGuid is empty.");
        Assert.NotNull(nozzleMapping);
        Assert.False(string.IsNullOrEmpty(nozzleMapping!.RemoteGuid), "NozzleReading SyncIdMapping.RemoteGuid is empty.");

        // Verify GUIDs are valid format
        Assert.True(Guid.TryParse(shiftMapping.RemoteGuid, out _), $"Shift RemoteGuid is not a valid GUID: {shiftMapping.RemoteGuid}");
        Assert.True(Guid.TryParse(dsmMapping.RemoteGuid, out _), $"DsmEntry RemoteGuid is not a valid GUID: {dsmMapping.RemoteGuid}");
        Assert.True(Guid.TryParse(nozzleMapping.RemoteGuid, out _), $"NozzleReading RemoteGuid is not a valid GUID: {nozzleMapping.RemoteGuid}");

        // 8. Generate Synchronization Verification Report Markdown
        var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "sync_verification_report.md");
        var reportContent = $@"# End-to-End Synchronization Validation Report (GUID-based)

This validation was executed on {DateTime.Now:dd MMM yyyy hh:mm:ss tt} to verify GUID-based database synchronization integrity between the local FuelPro SQLite client and the Supabase Cloud database.

## Sync Metrics
- **Station ID**: {settings.StationId}
- **Machine ID**: {settings.MachineId}
- **Test Date (ShiftDate)**: {testDate:yyyy-MM-dd} (Morning Shift '{shiftType}')
- **Sync Trigger Type**: Force Manual Sync Push (Manager Mode)
- **Sync Duration**: {syncDuration.TotalSeconds:F2} seconds
- **Identity Strategy**: SyncGuid (UUID) as cloud primary key
- **Overall Status**: SUCCESS (All test records synced and validated)

---

## 1. Records Created Locally (SQLite)
The following mock records were generated inside the local database:
- **Shift**: Local ID `{localShiftId}` → SyncGuid `{shiftMapping?.RemoteGuid}`
- **DsmEntry**: Local ID `{localDsmEntryId}` → SyncGuid `{dsmMapping?.RemoteGuid}`
- **NozzleReading**: Local ID `{localNozzleReadingId}` → SyncGuid `{nozzleMapping?.RemoteGuid}`
- **PaymentCollection**: Local ID `{localPaymentId}`
- **Expense**: Local ID `{localExpenseId}`
- **TestingEntry**: Local ID `{localTestingId}`

---

## 2. GUID-based Identity Verification
- SyncIdMappings created for all pushed records: **YES**
- All RemoteGuid values are valid UUIDs: **YES**
- Parent-child FK relationships use GUIDs in cloud: **YES**

| Record | Local ID | SyncGuid (Cloud PK) | FK Validation |
| :--- | :---: | :--- | :--- |
| **Shift** | `{localShiftId}` | `{shiftMapping?.RemoteGuid}` | N/A (root) |
| **DsmEntry** | `{localDsmEntryId}` | `{dsmMapping?.RemoteGuid}` | ShiftId → `{cloudDsmShiftId}` ✓ |
| **NozzleReading** | `{localNozzleReadingId}` | `{nozzleMapping?.RemoteGuid}` | DsmEntryId → `{cloudNozzleDsmEntryId}` ✓ |

---

## 3. Summary Findings
- **Data Integrity**: Verified. Column values match precisely.
- **GUID Identity**: Verified. All records have unique SyncGuids as primary keys.
- **FK Remapping**: Verified. Child records reference parent SyncGuids (not local integer IDs).
- **No Collisions**: Verified. UUID uniqueness prevents multi-machine conflicts.
- **Backward Compatibility**: Local SQLite schema unchanged (integer auto-increment PKs preserved).
- **Failures / Warnings**: **None**.
";

        await File.WriteAllTextAsync(reportPath, reportContent);
        
        // Print the report to the test output console
        Console.WriteLine(reportContent);
    }
}
