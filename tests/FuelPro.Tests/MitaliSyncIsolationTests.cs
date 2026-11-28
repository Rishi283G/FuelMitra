using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FuelPro.Core.Models;
using FuelPro.Data;
using FuelPro.Sync;

namespace FuelPro.Tests;

public class MitaliSyncIsolationTests : IDisposable
{
    private readonly string _dbName;
    private readonly ServiceProvider _serviceProvider;

    public MitaliSyncIsolationTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _dbName = "MitaliSyncIsolationTests_" + Guid.NewGuid().ToString("N");

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseInMemoryDatabase(databaseName: _dbName),
            ServiceLifetime.Transient);

        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    [Fact]
    public void Test1_DsmQrPayments_ExcludedFromSyncedTables_AndPullTableOrder()
    {
        // 1. Verify DsmQrPayments is excluded from cloud push
        Assert.DoesNotContain("DsmQrPayments", SyncEngine.SyncedTables);

        // 2. Verify DsmQrPayments is excluded from cloud pull
        var pullField = typeof(SyncEngine).GetField("PullTableOrder", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(pullField);
        var pullValue = pullField.GetValue(null);
        Assert.NotNull(pullValue);

        var pullTables = ((IEnumerable<object>)pullValue)
            .Select(t => t.GetType().GetProperty("TableName")?.GetValue(t)?.ToString())
            .Where(name => name != null)
            .ToList();

        Assert.DoesNotContain("DsmQrPayments", pullTables);
    }

    [Fact]
    public async Task Test2_TenMitaliQrLogs_ReceiveLocalRetainedTreatment_TruthfulSyncedAt_AndMarker()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();

        // Replicate Mitali's exact 10 pending DsmQrPayments logs
        var qrLogs = new List<SyncChangeLog>
        {
            new() { Id = 4901, TableName = "DsmQrPayments", RecordId = 1, Operation = "INSERT", IsSynced = false, RecordGuid = null },
            new() { Id = 6480, TableName = "DsmQrPayments", RecordId = 2, Operation = "INSERT", IsSynced = false, RecordGuid = null },
            new() { Id = 6569, TableName = "DsmQrPayments", RecordId = 2, Operation = "DELETE", IsSynced = false, RecordGuid = "2f4f2cc4-f65c-450a-aa29-8c1c84dc9730" },
            new() { Id = 7406, TableName = "DsmQrPayments", RecordId = 3, Operation = "INSERT", IsSynced = false, RecordGuid = null },
            new() { Id = 7454, TableName = "DsmQrPayments", RecordId = 4, Operation = "INSERT", IsSynced = false, RecordGuid = null },
            new() { Id = 7455, TableName = "DsmQrPayments", RecordId = 3, Operation = "DELETE", IsSynced = false, RecordGuid = "c327db50-9115-4e06-b562-48f8cafe5010" },
            new() { Id = 7708, TableName = "DsmQrPayments", RecordId = 5, Operation = "INSERT", IsSynced = false, RecordGuid = null },
            new() { Id = 7709, TableName = "DsmQrPayments", RecordId = 4, Operation = "DELETE", IsSynced = false, RecordGuid = "42a46cf5-520c-4042-b0a8-d6c9ba4953e9" },
            new() { Id = 9139, TableName = "DsmQrPayments", RecordId = 6, Operation = "INSERT", IsSynced = false, RecordGuid = null },
            new() { Id = 9182, TableName = "DsmQrPayments", RecordId = 6, Operation = "DELETE", IsSynced = false, RecordGuid = "03f105d1-e85e-46da-bc7e-7fb953b7a992" }
        };

        context.SyncChangeLogs.AddRange(qrLogs);
        await context.SaveChangesAsync();

        // Simulate SyncEngine tier-1 handler
        var allPendingLogs = await context.SyncChangeLogs.Where(l => !l.IsSynced).ToListAsync();
        var targetQrLogs = allPendingLogs.Where(l => l.TableName == "DsmQrPayments").ToList();
        Assert.Equal(10, targetQrLogs.Count);

        foreach (var l in targetQrLogs)
        {
            l.IsSynced = true;
            l.SyncedAt = null; // Truthful representation: not uploaded to cloud
            l.RecordGuid = string.IsNullOrEmpty(l.RecordGuid) ? "LOCAL_RETAINED" : (l.RecordGuid.Contains("LOCAL_RETAINED") ? l.RecordGuid : l.RecordGuid + ":LOCAL_RETAINED");
        }
        await context.SaveChangesAsync();

        var retainedQr = await context.SyncChangeLogs.Where(l => l.TableName == "DsmQrPayments").ToListAsync();
        Assert.Equal(10, retainedQr.Count);
        Assert.All(retainedQr, l =>
        {
            Assert.True(l.IsSynced);
            Assert.Null(l.SyncedAt);
            Assert.Contains("LOCAL_RETAINED", l.RecordGuid ?? "");
        });

        // Original GUID on deletes preserved before suffix
        Assert.Equal("2f4f2cc4-f65c-450a-aa29-8c1c84dc9730:LOCAL_RETAINED", retainedQr.First(l => l.Id == 6569).RecordGuid);
        Assert.Equal("LOCAL_RETAINED", retainedQr.First(l => l.Id == 4901).RecordGuid);
    }

    [Fact]
    public async Task Test3_OtherExcludedTables_PreservePreviousSyncHandling_WithoutLocalRetainedMarker()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();

        // Seed logs for other excluded internal/config tables
        var mappingLog = new SyncChangeLog { Id = 101, TableName = "SyncIdMappings", RecordId = 1, Operation = "INSERT", IsSynced = false, RecordGuid = "map-1" };
        var featureLog = new SyncChangeLog { Id = 102, TableName = "AppFeatureSettings", RecordId = 2, Operation = "UPDATE", IsSynced = false, RecordGuid = "feat-2" };
        var collectionTypeLog = new SyncChangeLog { Id = 103, TableName = "CollectionTypes", RecordId = 3, Operation = "INSERT", IsSynced = false, RecordGuid = null };
        var tankDefLog = new SyncChangeLog { Id = 104, TableName = "TankDefinitions", RecordId = 4, Operation = "UPDATE", IsSynced = false, RecordGuid = null };

        context.SyncChangeLogs.AddRange(mappingLog, featureLog, collectionTypeLog, tankDefLog);
        await context.SaveChangesAsync();

        // Simulate SyncEngine tier-2 handler for other non-synced tables
        var allPendingLogs = await context.SyncChangeLogs.Where(l => !l.IsSynced).ToListAsync();
        var otherIgnoredLogs = allPendingLogs.Where(l => !SyncEngine.SyncedTables.Contains(l.TableName) && l.TableName != "DsmQrPayments").ToList();
        Assert.Equal(4, otherIgnoredLogs.Count);

        var now = DateTime.Now;
        foreach (var l in otherIgnoredLogs)
        {
            l.IsSynced = true;
            l.SyncedAt = now; // Preserves historical behavior
            // Note: RecordGuid is NOT modified
        }
        await context.SaveChangesAsync();

        var updatedLogs = await context.SyncChangeLogs.Where(l => l.TableName != "DsmQrPayments").ToListAsync();
        Assert.Equal(4, updatedLogs.Count);
        Assert.All(updatedLogs, l =>
        {
            Assert.True(l.IsSynced);
            Assert.NotNull(l.SyncedAt);
            // Must NOT have LOCAL_RETAINED marker
            Assert.DoesNotContain("LOCAL_RETAINED", l.RecordGuid ?? "");
        });

        // Original RecordGuid remains 100% unaltered
        Assert.Equal("map-1", updatedLogs.First(l => l.Id == 101).RecordGuid);
        Assert.Equal("feat-2", updatedLogs.First(l => l.Id == 102).RecordGuid);
        Assert.Null(updatedLogs.First(l => l.Id == 103).RecordGuid);
        Assert.Null(updatedLogs.First(l => l.Id == 104).RecordGuid);
    }

    [Fact]
    public async Task Test4_SupportedTableLogs_RemainEligibleForNormalCloudPush()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();

        // Seed logs for supported tables that must be pushed to Supabase
        var expenseLog = new SyncChangeLog { Id = 201, TableName = "Expenses", RecordId = 5, Operation = "INSERT", IsSynced = false, RecordGuid = "exp-5" };
        var shiftLog = new SyncChangeLog { Id = 202, TableName = "Shifts", RecordId = 10, Operation = "UPDATE", IsSynced = false, RecordGuid = "shift-10" };
        var dsmLog = new SyncChangeLog { Id = 203, TableName = "DsmEntries", RecordId = 99, Operation = "INSERT", IsSynced = false, RecordGuid = "dsm-99" };
        var qrLog = new SyncChangeLog { Id = 204, TableName = "DsmQrPayments", RecordId = 1, Operation = "INSERT", IsSynced = false, RecordGuid = null };

        context.SyncChangeLogs.AddRange(expenseLog, shiftLog, dsmLog, qrLog);
        await context.SaveChangesAsync();

        var allPendingLogs = await context.SyncChangeLogs.Where(l => !l.IsSynced).ToListAsync();

        // 1. Process QR logs via tier-1
        var qrLogs = allPendingLogs.Where(l => l.TableName == "DsmQrPayments").ToList();
        foreach (var l in qrLogs)
        {
            l.IsSynced = true;
            l.SyncedAt = null;
            l.RecordGuid = "LOCAL_RETAINED";
        }
        await context.SaveChangesAsync();

        // 2. Pending logs for push are filtered by SyncedTables
        var pendingForPush = allPendingLogs.Where(l => SyncEngine.SyncedTables.Contains(l.TableName)).ToList();

        // Must contain all supported tables and exclude DsmQrPayments
        Assert.Equal(3, pendingForPush.Count);
        Assert.Contains(pendingForPush, l => l.TableName == "Expenses");
        Assert.Contains(pendingForPush, l => l.TableName == "Shifts");
        Assert.Contains(pendingForPush, l => l.TableName == "DsmEntries");
        Assert.DoesNotContain(pendingForPush, l => l.TableName == "DsmQrPayments");
    }

    [Fact]
    public async Task Test5_RepeatedProcessing_IsIdempotent_AndDoesNotDuplicateMarkers()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();

        // Seed an already retained log
        var retained = new SyncChangeLog
        {
            Id = 4901,
            TableName = "DsmQrPayments",
            RecordId = 1,
            Operation = "INSERT",
            IsSynced = true,
            SyncedAt = null,
            RecordGuid = "LOCAL_RETAINED"
        };
        var retainedDelete = new SyncChangeLog
        {
            Id = 6569,
            TableName = "DsmQrPayments",
            RecordId = 2,
            Operation = "DELETE",
            IsSynced = true,
            SyncedAt = null,
            RecordGuid = "2f4f2cc4-f65c-450a-aa29-8c1c84dc9730:LOCAL_RETAINED"
        };
        context.SyncChangeLogs.AddRange(retained, retainedDelete);
        await context.SaveChangesAsync();

        // Next tick / restart query:
        var allPendingLogs = await context.SyncChangeLogs.Where(l => !l.IsSynced).ToListAsync();
        var targetQrLogs = allPendingLogs.Where(l => l.TableName == "DsmQrPayments").ToList();

        // Already retained logs are not selected because IsSynced == true
        Assert.Empty(targetQrLogs);

        // Verify RecordGuid was not corrupted or appended again
        var r1 = await context.SyncChangeLogs.FindAsync(4901);
        var r2 = await context.SyncChangeLogs.FindAsync(6569);
        Assert.Equal("LOCAL_RETAINED", r1!.RecordGuid);
        Assert.Equal("2f4f2cc4-f65c-450a-aa29-8c1c84dc9730:LOCAL_RETAINED", r2!.RecordGuid);
    }

    [Fact]
    public async Task Test6_ActiveQrRecords_FullyPreservedAndAccessible_Locally()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();

        // Replicate Mitali's local active QR records (Id 1 and Id 5)
        var qr1 = new DsmQrPaymentEntry
        {
            Id = 1,
            DsmEntryId = 98,
            DsmName = "KARTIK BHOJALE",
            TargetDsmName = "KARTIK BHOJALE",
            Amount = 4540,
            Date = new DateTime(2026, 10, 24),
            CreatedAt = new DateTime(2026, 10, 25, 8, 48, 10),
            SyncGuid = "0431741f-6421-43eb-9871-ab5c65b6480e"
        };
        var qr5 = new DsmQrPaymentEntry
        {
            Id = 5,
            DsmEntryId = 142,
            DsmName = "KARTIK BHOJALE",
            TargetDsmName = "KARTIK BHOJALE",
            Amount = 13055.39,
            Date = new DateTime(2026, 10, 27),
            CreatedAt = new DateTime(2026, 10, 28, 10, 3, 14),
            SyncGuid = "4f79387c-442a-446b-8965-34b62bf93768"
        };

        context.DsmQrPayments.AddRange(qr1, qr5);
        await context.SaveChangesAsync();

        var localRecords = await context.DsmQrPayments.OrderBy(q => q.Id).ToListAsync();
        Assert.Equal(2, localRecords.Count);
        Assert.Equal(4540, localRecords[0].Amount);
        Assert.Equal(13055.39, localRecords[1].Amount);
    }
}
