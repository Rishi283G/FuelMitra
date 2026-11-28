using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;
using FuelPro.Core.Models;
using FuelPro.Data;
using FuelPro.Sync;

namespace FuelPro.Tests;

public class DesktopSyncReliabilityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FuelProDbContext> _options;

    public DesktopSyncReliabilityTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new FuelProDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private FuelProDbContext CreateContext() => new FuelProDbContext(_options);

    private IServiceProvider CreateServiceProvider(HttpClient httpClient, SyncSettings? settings = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(_connection), ServiceLifetime.Transient);
        services.AddSingleton<SyncConfigService>();

        var defaultSettings = settings ?? new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "RASHTRA-74A837",
            MachineId = "MACHINE-01"
        };
        var supabaseClient = new SupabaseHttpClient(httpClient);
        supabaseClient.Configure(defaultSettings.SupabaseUrl, defaultSettings.SupabaseApiKey);
        services.AddSingleton(supabaseClient);
        services.AddSingleton<SyncEngine>();

        var sp = services.BuildServiceProvider();

        var configService = sp.GetRequiredService<SyncConfigService>();
        configService.SaveSettingsAsync(defaultSettings).GetAwaiter().GetResult();

        return sp;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 1: Startup Queue Does NOT Reset Synced Logs & Preserves Pending State
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test1_StartupQueue_DoesNotResetSyncedLogs_AndPreservesPending()
    {
        using var context = CreateContext();

        // Seed 1: Already synced log
        context.SyncChangeLogs.Add(new SyncChangeLog
        {
            Id = 101,
            TableName = "Shifts",
            RecordId = 1,
            Operation = "INSERT",
            CreatedAt = DateTime.Now.AddDays(-1),
            IsSynced = true,
            SyncedAt = DateTime.Now.AddHours(-12),
            StationId = "RASHTRA-74A837",
            MachineId = "MACHINE-01",
            SyncGuid = Guid.NewGuid().ToString(),
            RecordGuid = Guid.NewGuid().ToString()
        });

        // Seed 2: Genuinely pending log
        context.SyncChangeLogs.Add(new SyncChangeLog
        {
            Id = 102,
            TableName = "Shifts",
            RecordId = 2,
            Operation = "INSERT",
            CreatedAt = DateTime.Now.AddHours(-1),
            IsSynced = false,
            StationId = "RASHTRA-74A837",
            MachineId = "MACHINE-01",
            SyncGuid = Guid.NewGuid().ToString(),
            RecordGuid = Guid.NewGuid().ToString()
        });

        // Seed 3: Legacy log missing StationId (IsSynced = true)
        context.SyncChangeLogs.Add(new SyncChangeLog
        {
            Id = 103,
            TableName = "Shifts",
            RecordId = 3,
            Operation = "INSERT",
            CreatedAt = DateTime.Now.AddDays(-2),
            IsSynced = true,
            SyncedAt = DateTime.Now.AddDays(-2),
            StationId = "",
            MachineId = "MACHINE-01",
            SyncGuid = Guid.NewGuid().ToString(),
            RecordGuid = Guid.NewGuid().ToString()
        });

        await context.SaveChangesAsync();

        var mockHandler = new TestHttpMessageHandler((req) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var client = new HttpClient(mockHandler);
        var sp = CreateServiceProvider(client);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        // Execute self-healing startup historical queue scan
        await syncEngine.QueueAllHistoricalRecordsForStationAsync("RASHTRA-74A837");

        using var verifyContext = CreateContext();
        var log1 = await verifyContext.SyncChangeLogs.FindAsync(101);
        var log2 = await verifyContext.SyncChangeLogs.FindAsync(102);
        var log3 = await verifyContext.SyncChangeLogs.FindAsync(103);

        Assert.NotNull(log1);
        Assert.True(log1.IsSynced, "Pre-existing synced log must NOT be reset to IsSynced = false");
        Assert.NotNull(log1.SyncedAt);

        Assert.NotNull(log2);
        Assert.False(log2.IsSynced, "Genuinely pending log must remain pending (IsSynced = false)");

        Assert.NotNull(log3);
        Assert.True(log3.IsSynced, "Legacy log missing station_id must retain IsSynced = true after station_id backfill");
        Assert.Equal("RASHTRA-74A837", log3.StationId);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 2: Startup Queue Queues Unlogged Entities Without Duplicating Existing
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test2_StartupQueue_QueuesUnloggedEntities_WithoutDuplicatingExisting()
    {
        using var context = CreateContext();

        // Add a Shift record directly in the DB
        var shift = new Shift
        {
            ShiftId = 55,
            ShiftDate = DateTime.Today,
            ShiftType = "A",
            CreatedAt = DateTime.Now
        };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        var mockHandler = new TestHttpMessageHandler((req) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var client = new HttpClient(mockHandler);
        var sp = CreateServiceProvider(client);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        // Run queue scan 1st time
        await syncEngine.QueueAllHistoricalRecordsForStationAsync("RASHTRA-74A837");

        using (var verify1 = CreateContext())
        {
            var logs = await verify1.SyncChangeLogs.Where(l => l.TableName == "Shifts" && l.RecordId == 55).ToListAsync();
            Assert.Single(logs);
            Assert.False(logs[0].IsSynced);
            Assert.Equal("RASHTRA-74A837", logs[0].StationId);
        }

        // Run queue scan 2nd time - must be idempotent (no duplicates added)
        await syncEngine.QueueAllHistoricalRecordsForStationAsync("RASHTRA-74A837");

        using (var verify2 = CreateContext())
        {
            var logs = await verify2.SyncChangeLogs.Where(l => l.TableName == "Shifts" && l.RecordId == 55).ToListAsync();
            Assert.Single(logs);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 3: RecordSyncChangeLogTombstoneAsync Handles 201 Created
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test3_RecordSyncChangeLogTombstoneAsync_Handles201Created()
    {
        var testLog = new SyncChangeLog
        {
            Id = 1,
            TableName = "NozzleReadings",
            RecordId = 10,
            Operation = "DELETE",
            CreatedAt = DateTime.UtcNow,
            SyncGuid = Guid.NewGuid().ToString(),
            RecordGuid = Guid.NewGuid().ToString(),
            StationId = "RASHTRA-74A837",
            MachineId = "M1"
        };

        var mockHandler = new TestHttpMessageHandler((req) =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.Contains("SyncChangeLogs"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var client = new HttpClient(mockHandler);
        var sp = CreateServiceProvider(client);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        // Invoke private RecordSyncChangeLogTombstoneAsync via reflection
        var method = typeof(SyncEngine).GetMethod("RecordSyncChangeLogTombstoneAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var settings = await sp.GetRequiredService<SyncConfigService>().GetSettingsAsync();
        var task = (Task<bool>)method.Invoke(syncEngine, new object[] { testLog, testLog.RecordGuid!, settings })!;
        bool result = await task;

        Assert.True(result, "201 Created must be accepted as successfully acknowledged tombstone");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 4: RecordSyncChangeLogTombstoneAsync Handles 409 Conflict With Matching Payload
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test4_RecordSyncChangeLogTombstoneAsync_Handles409Conflict_WithMatchingPayload()
    {
        var syncGuid = Guid.NewGuid().ToString();
        var recordGuid = Guid.NewGuid().ToString();
        var createdAt = DateTime.UtcNow;

        var testLog = new SyncChangeLog
        {
            Id = 2,
            TableName = "NozzleReadings",
            RecordId = 20,
            Operation = "DELETE",
            CreatedAt = createdAt,
            SyncGuid = syncGuid,
            RecordGuid = recordGuid,
            StationId = "RASHTRA-74A837",
            MachineId = "MACHINE-01"
        };

        var mockHandler = new TestHttpMessageHandler((req) =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.Contains("SyncChangeLogs"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("{\"code\":\"23505\",\"message\":\"duplicate key value violates unique constraint\"}", Encoding.UTF8, "application/json")
                });
            }

            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Contains("SyncChangeLogs") && req.RequestUri.Query.Contains(syncGuid))
            {
                var remoteRow = new[]
                {
                    new Dictionary<string, object>
                    {
                        { "SyncGuid", syncGuid },
                        { "TableName", "NozzleReadings" },
                        { "RecordGuid", recordGuid },
                        { "Operation", "DELETE" },
                        { "StationId", "RASHTRA-74A837" },
                        { "MachineId", "MACHINE-01" },
                        { "Timestamp", createdAt.ToString("o") }
                    }
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(remoteRow), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var client = new HttpClient(mockHandler);
        var sp = CreateServiceProvider(client);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        var method = typeof(SyncEngine).GetMethod("RecordSyncChangeLogTombstoneAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var settings = await sp.GetRequiredService<SyncConfigService>().GetSettingsAsync();
        var task = (Task<bool>)method.Invoke(syncEngine, new object[] { testLog, testLog.RecordGuid!, settings })!;
        bool result = await task;

        Assert.True(result, "409 Conflict with verified matching remote payload must be accepted as acknowledged");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 5: RecordSyncChangeLogTombstoneAsync Rejects 409 Conflict With Mismatched Payload
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test5_RecordSyncChangeLogTombstoneAsync_Rejects409Conflict_WithMismatchedPayload()
    {
        var syncGuid = Guid.NewGuid().ToString();

        var testLog = new SyncChangeLog
        {
            Id = 3,
            TableName = "NozzleReadings",
            RecordId = 30,
            Operation = "DELETE",
            CreatedAt = DateTime.UtcNow,
            SyncGuid = syncGuid,
            RecordGuid = Guid.NewGuid().ToString(),
            StationId = "RASHTRA-74A837",
            MachineId = "M1"
        };

        var mockHandler = new TestHttpMessageHandler((req) =>
        {
            if (req.Method == HttpMethod.Post)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
            }

            if (req.Method == HttpMethod.Get)
            {
                // Mismatched remote row: Operation is "UPDATE" instead of "DELETE", and different StationId
                var remoteRow = new[]
                {
                    new Dictionary<string, object>
                    {
                        { "SyncGuid", syncGuid },
                        { "TableName", "NozzleReadings" },
                        { "RecordGuid", Guid.NewGuid().ToString() },
                        { "Operation", "UPDATE" },
                        { "StationId", "OTHER-STATION" },
                        { "MachineId", "M2" },
                        { "Timestamp", DateTime.UtcNow.ToString("o") }
                    }
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(remoteRow), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var client = new HttpClient(mockHandler);
        var sp = CreateServiceProvider(client);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        var method = typeof(SyncEngine).GetMethod("RecordSyncChangeLogTombstoneAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);

        var settings = await sp.GetRequiredService<SyncConfigService>().GetSettingsAsync();
        var task = (Task<bool>)method.Invoke(syncEngine, new object[] { testLog, testLog.RecordGuid!, settings })!;
        bool result = await task;

        Assert.False(result, "409 Conflict with mismatched payload must be rejected and remain pending");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 6: Push DELETE Immediate Per-Operation Acknowledgment & Mapping Removal
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test6_PushDelete_ImmediatePerOperationAcknowledgment_AndMappingRemoval()
    {
        using var context = CreateContext();

        var remoteGuid = "rem-guid-del-01";
        context.SyncIdMappings.Add(new SyncIdMapping
        {
            TableName = "NozzleReadings",
            LocalId = 105,
            RemoteGuid = remoteGuid
        });

        context.SyncChangeLogs.Add(new SyncChangeLog
        {
            Id = 501,
            TableName = "NozzleReadings",
            RecordId = 105,
            Operation = "DELETE",
            CreatedAt = DateTime.UtcNow,
            IsSynced = false,
            StationId = "RASHTRA-74A837",
            MachineId = "MACHINE-01",
            SyncGuid = Guid.NewGuid().ToString(),
            RecordGuid = remoteGuid
        });

        await context.SaveChangesAsync();

        bool deleteCalled = false;
        var mockHandler = new TestHttpMessageHandler((req) =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.Contains("SyncChangeLogs"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
            }

            if (req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath.Contains("NozzleReadings"))
            {
                deleteCalled = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        });

        var client = new HttpClient(mockHandler);
        var sp = CreateServiceProvider(client);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        var performPushMethod = typeof(SyncEngine).GetMethod("PerformPushAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(performPushMethod);

        var settings = await sp.GetRequiredService<SyncConfigService>().GetSettingsAsync();
        var pushTask = (Task)performPushMethod.Invoke(syncEngine, new object[] { settings })!;
        await pushTask;

        Assert.True(deleteCalled, "Remote DELETE must be called for the record");

        using var verify = CreateContext();
        var log = await verify.SyncChangeLogs.FindAsync(501);
        Assert.NotNull(log);
        Assert.True(log.IsSynced, "DELETE operation must be marked IsSynced = 1 immediately upon successful deletion");

        var mapping = await verify.SyncIdMappings.FirstOrDefaultAsync(m => m.TableName == "NozzleReadings" && m.LocalId == 105);
        Assert.Null(mapping);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 7: Push DELETE 404 With Genuine Absence Marks Synced
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test7_PushDelete_404_WithGenuineAbsence_MarksSynced()
    {
        using var context = CreateContext();

        var remoteGuid = "rem-missing-02";
        context.SyncChangeLogs.Add(new SyncChangeLog
        {
            Id = 502,
            TableName = "NozzleReadings",
            RecordId = 106,
            Operation = "DELETE",
            CreatedAt = DateTime.UtcNow,
            IsSynced = false,
            StationId = "RASHTRA-74A837",
            MachineId = "MACHINE-01",
            SyncGuid = Guid.NewGuid().ToString(),
            RecordGuid = remoteGuid
        });

        await context.SaveChangesAsync();

        var mockHandler = new TestHttpMessageHandler((req) =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.Contains("SyncChangeLogs"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
            }

            if (req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath.Contains("NozzleReadings"))
            {
                // Returns 404 Not Found on remote delete
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.Contains("NozzleReadings"))
            {
                // Verification GET confirms row is genuinely absent in Supabase
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        });

        var client = new HttpClient(mockHandler);
        var sp = CreateServiceProvider(client);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        var performPushMethod = typeof(SyncEngine).GetMethod("PerformPushAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(performPushMethod);

        var settings = await sp.GetRequiredService<SyncConfigService>().GetSettingsAsync();
        var pushTask = (Task)performPushMethod.Invoke(syncEngine, new object[] { settings })!;
        await pushTask;

        using var verify = CreateContext();
        var log = await verify.SyncChangeLogs.FindAsync(502);
        Assert.NotNull(log);
        Assert.True(log.IsSynced, "404 with verified absence in Supabase must acknowledge DELETE and mark IsSynced = 1");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 8: Push Batch Upsert Failure Does NOT Invalidate Prior DELETE Acknowledgment
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test8_PushBatch_UpsertFailureDoesNotInvalidatePriorDeleteAcknowledgment()
    {
        using var context = CreateContext();

        // 1. DELETE log for Shifts (record 10)
        context.SyncChangeLogs.Add(new SyncChangeLog
        {
            Id = 601,
            TableName = "Shifts",
            RecordId = 10,
            Operation = "DELETE",
            CreatedAt = DateTime.UtcNow,
            IsSynced = false,
            StationId = "RASHTRA-74A837",
            MachineId = "MACHINE-01",
            SyncGuid = Guid.NewGuid().ToString(),
            RecordGuid = "del-shift-10"
        });

        // 2. INSERT log for Shifts (record 11)
        var shift11 = new Shift
        {
            ShiftId = 11,
            ShiftDate = DateTime.Today,
            ShiftType = "A",
            CreatedAt = DateTime.Now
        };
        context.Shifts.Add(shift11);

        context.SyncChangeLogs.Add(new SyncChangeLog
        {
            Id = 602,
            TableName = "Shifts",
            RecordId = 11,
            Operation = "INSERT",
            CreatedAt = DateTime.UtcNow,
            IsSynced = false,
            StationId = "RASHTRA-74A837",
            MachineId = "MACHINE-01",
            SyncGuid = Guid.NewGuid().ToString(),
            RecordGuid = "ins-shift-11"
        });

        await context.SaveChangesAsync();

        var mockHandler = new TestHttpMessageHandler((req) =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.Contains("SyncChangeLogs"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
            }

            if (req.Method == HttpMethod.Delete && req.RequestUri!.AbsolutePath.Contains("Shifts"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.Contains("Shifts"))
            {
                // Simulate Supabase schema error on upsert batch
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"message\":\"column 'UnknownCol' does not exist\"}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        });

        var client = new HttpClient(mockHandler);
        var sp = CreateServiceProvider(client);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        var performPushMethod = typeof(SyncEngine).GetMethod("PerformPushAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(performPushMethod);

        var settings = await sp.GetRequiredService<SyncConfigService>().GetSettingsAsync();
        var pushTask = (Task)performPushMethod.Invoke(syncEngine, new object[] { settings })!;
        await pushTask;

        using var verify = CreateContext();
        var deleteLog = await verify.SyncChangeLogs.FindAsync(601);
        var insertLog = await verify.SyncChangeLogs.FindAsync(602);

        Assert.NotNull(deleteLog);
        Assert.True(deleteLog.IsSynced, "DELETE log must remain acknowledged (IsSynced = 1) even when subsequent upsert in batch fails");

        Assert.NotNull(insertLog);
        Assert.False(insertLog.IsSynced, "INSERT log must remain un-synced due to upsert failure");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 9: DsmSubmissionPollingService Normal Count and Count Zero Handling
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test9_DsmSubmissionPollingService_NormalCountAndCountZero()
    {
        int requestCount = 0;
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var mockHandler = new TestHttpMessageHandler((req) =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                // First call: 2 valid pending submissions with GUID Ids and required PumpId/ShiftDate
                var json = JsonConvert.SerializeObject(new[]
                {
                    new { Id = id1, StationId = "RASHTRA-74A837", PumpId = 1, ShiftDate = "2026-11-24", ShiftType = "A", DsmUserId = "dsm-1", Status = "Pending", SubmittedAt = DateTime.UtcNow.AddMinutes(-10) },
                    new { Id = id2, StationId = "RASHTRA-74A837", PumpId = 2, ShiftDate = "2026-11-24", ShiftType = "A", DsmUserId = "dsm-2", Status = "Pending", SubmittedAt = DateTime.UtcNow.AddMinutes(-5) }
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }
            else
            {
                // Second call: 0 pending submissions
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
            }
        });

        var httpClient = new HttpClient(mockHandler);
        var sp = CreateServiceProvider(httpClient);
        var configService = sp.GetRequiredService<SyncConfigService>();
        using var pollingService = new DsmSubmissionPollingService(configService, null, httpClient);

        var receivedCounts = new List<int>();
        pollingService.PendingCountChanged += count => receivedCounts.Add(count);

        // First poll: expect count 2
        await pollingService.ForceRefreshAsync();
        Assert.Equal(2, pollingService.CurrentPendingCount);
        Assert.True(pollingService.HasAuthoritativeCount);

        // Sleep > 2s to bypass ForceRefresh throttle window
        await Task.Delay(2100);

        // Second poll: expect count 0
        await pollingService.ForceRefreshAsync();
        Assert.Equal(0, pollingService.CurrentPendingCount);

        Assert.Equal(2, receivedCounts.Count);
        Assert.Equal(2, receivedCounts[0]);
        Assert.Equal(0, receivedCounts[1]);
    }

    [Fact]
    public async Task Test9b_DsmSubmissionPollingService_ExcludesInvalidAndMalformedPayloads()
    {
        var validId = Guid.NewGuid();
        var mockHandler = new TestHttpMessageHandler((req) =>
        {
            var payload = new object[]
            {
                new { Id = "1", StationId = "RASHTRA-74A837", PumpId = 1, ShiftDate = "2026-11-24", Status = "Pending" }, // non-GUID Id
                new { Id = Guid.NewGuid().ToString(), StationId = "RASHTRA-74A837", PumpId = 0, ShiftDate = "2026-11-24", Status = "Pending" }, // invalid PumpId
                new { Id = Guid.NewGuid().ToString(), StationId = "RASHTRA-74A837", PumpId = 1, Status = "Pending" }, // missing ShiftDate
                new { Id = validId.ToString(), StationId = "RASHTRA-74A837", PumpId = 1, ShiftDate = "2026-11-24", ShiftType = "A", Status = "Pending" }
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
            });
        });

        var httpClient = new HttpClient(mockHandler);
        var sp = CreateServiceProvider(httpClient);
        var configService = sp.GetRequiredService<SyncConfigService>();
        using var pollingService = new DsmSubmissionPollingService(configService, null, httpClient);

        await pollingService.ForceRefreshAsync();
        Assert.Equal(1, pollingService.CurrentPendingCount);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 10: DsmSubmissionPollingService ForceRefresh Throttling (2s window)
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test10_DsmSubmissionPollingService_ForceRefreshThrottle()
    {
        int httpRequestsMade = 0;
        var mockHandler = new TestHttpMessageHandler((req) =>
        {
            httpRequestsMade++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[{\"Id\":1}]", Encoding.UTF8, "application/json")
            });
        });

        var httpClient = new HttpClient(mockHandler);
        var sp = CreateServiceProvider(httpClient);
        var configService = sp.GetRequiredService<SyncConfigService>();
        using var pollingService = new DsmSubmissionPollingService(configService, null, httpClient);

        // 1st ForceRefresh: should execute HTTP request
        await pollingService.ForceRefreshAsync();
        Assert.Equal(1, httpRequestsMade);

        // Immediate subsequent ForceRefresh calls (< 2 seconds): should be throttled
        await pollingService.ForceRefreshAsync();
        await pollingService.ForceRefreshAsync();
        Assert.Equal(1, httpRequestsMade);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 11: DsmSubmissionPollingService 500 Error & 429 Handling & Disposal
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test11_DsmSubmissionPollingService_ErrorAndBackoffAndDispose()
    {
        var mockHandler = new TestHttpMessageHandler((req) =>
        {
            var resp = new HttpResponseMessage((HttpStatusCode)429)
            {
                Content = new StringContent("{\"error\":\"Too Many Requests\"}", Encoding.UTF8, "application/json")
            };
            resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(resp);
        });

        var httpClient = new HttpClient(mockHandler);
        var sp = CreateServiceProvider(httpClient);
        var configService = sp.GetRequiredService<SyncConfigService>();
        var pollingService = new DsmSubmissionPollingService(configService, null, httpClient);

        // Should not throw on 429
        await pollingService.ForceRefreshAsync();

        // Service should dispose cleanly
        pollingService.Dispose();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test 12: Rugved Bhise DsmUsers Natural-Key Matching & Assignment Reconciliation
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test12_RugvedBhise_DsmUsersAndPumpAssignmentsReconciliation()
    {
        using var context = CreateContext();

        // 1. Seed existing local DsmUser for Rugved Bhise without SyncIdMapping
        var localRugved = new DsmUser
        {
            EmployeeCode = "006",
            FullName = "Rugved Bhise",
            MobileNumber = "9876543210",
            Email = "rugved@petrol.com",
            AuthUserId = "auth-rugved-123",
            IsActive = true
        };
        context.DsmUsers.Add(localRugved);
        await context.SaveChangesAsync();
        int rugvedLocalId = localRugved.DsmUserId;

        // 2. Seed an existing rejected local assignment for Rugved on Pump 1, Shift A
        // mapped to a local unconfirmed GUID
        var localAssignment = new DsmPumpAssignment
        {
            DsmUserId = rugvedLocalId,
            PumpId = 1,
            ShiftType = "A",
            IsActive = true,
            AssignedDate = DateTime.Now
        };
        context.DsmPumpAssignments.Add(localAssignment);
        await context.SaveChangesAsync();
        int assignmentLocalId = localAssignment.DsmPumpAssignmentId;

        string unconfirmedLocalGuid = Guid.NewGuid().ToString();
        context.SyncIdMappings.Add(new SyncIdMapping
        {
            TableName = "DsmPumpAssignments",
            LocalId = assignmentLocalId,
            RemoteGuid = unconfirmedLocalGuid
        });

        // Seed pending SyncChangeLog for this local assignment
        context.SyncChangeLogs.Add(new SyncChangeLog
        {
            TableName = "DsmPumpAssignments",
            RecordId = assignmentLocalId,
            RecordGuid = unconfirmedLocalGuid,
            Operation = "INSERT",
            IsSynced = false,
            StationId = "RASHTRA-74A837",
            MachineId = "MACHINE-01",
            CreatedAt = DateTime.Now
        });
        await context.SaveChangesAsync();

        string remoteDsmUserGuid = "99d1f99e-fde6-48f0-87f0-ad24eda58599";
        string canonicalAssignmentGuid = "ff1edd1a-4f1f-4148-80c1-2b4de1b7b1b2";

        // 3. Mock Supabase responses
        var mockHandler = new TestHttpMessageHandler((req) =>
        {
            var url = req.RequestUri!.ToString();

            // GET DsmUsers by SyncGuid (on-demand parent fetch)
            if (url.Contains("DsmUsers?SyncGuid=eq." + remoteDsmUserGuid))
            {
                var payload = new List<Dictionary<string, object?>>
                {
                    new Dictionary<string, object?>
                    {
                        { "SyncGuid", remoteDsmUserGuid },
                        { "EmployeeCode", "006" },
                        { "FullName", "RUGVED BHISE" },
                        { "MobileNumber", "9876543210" },
                        { "AuthUserId", "auth-rugved-123" },
                        { "station_id", "RASHTRA-74A837" },
                        { "IsActive", true }
                    }
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
                });
            }

            // GET DsmUsers table pull
            if (url.Contains("DsmUsers?station_id=eq.RASHTRA-74A837"))
            {
                var payload = new List<Dictionary<string, object?>>
                {
                    new Dictionary<string, object?>
                    {
                        { "SyncGuid", remoteDsmUserGuid },
                        { "EmployeeCode", "006" },
                        { "FullName", "RUGVED BHISE" },
                        { "MobileNumber", "9876543210" },
                        { "AuthUserId", "auth-rugved-123" },
                        { "station_id", "RASHTRA-74A837" },
                        { "IsActive", true }
                    }
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
                });
            }

            // GET DsmPumpAssignments table pull
            if (url.Contains("DsmPumpAssignments?station_id=eq.RASHTRA-74A837"))
            {
                var payload = new List<Dictionary<string, object?>>
                {
                    new Dictionary<string, object?>
                    {
                        { "SyncGuid", canonicalAssignmentGuid },
                        { "local_id", 298 },
                        { "DsmUserId", remoteDsmUserGuid },
                        { "PumpId", 1 },
                        { "ShiftType", "A" },
                        { "IsActive", true },
                        { "ConnectedPumpId", 2 },
                        { "ConnectedPumpIdsJson", "[2]" },
                        { "station_id", "RASHTRA-74A837" }
                    }
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
                });
            }

            // Fallback for all other tables: return empty array
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        });

        var httpClient = new HttpClient(mockHandler);
        var sp = CreateServiceProvider(httpClient);
        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var settings = await sp.GetRequiredService<SyncConfigService>().GetSettingsAsync();

        // 4. Test FetchAndImportMissingParentAsync via reflection
        var fetchMethod = typeof(SyncEngine).GetMethod("FetchAndImportMissingParentAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(fetchMethod);

        var guidToLocal = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        using (var testContext = CreateContext())
        {
            var fetchTask = (Task<int?>)fetchMethod.Invoke(syncEngine, new object[] { testContext, guidToLocal, "DsmUsers", remoteDsmUserGuid, settings })!;
            var resolvedUserId = await fetchTask;

            Assert.NotNull(resolvedUserId);
            Assert.Equal(rugvedLocalId, resolvedUserId.Value);

            // Verify no duplicate DsmUser was created
            int userCount = await testContext.DsmUsers.CountAsync();
            Assert.Equal(1, userCount);

            // Verify remote UUID was mapped to existing local ID
            var mapping = await testContext.SyncIdMappings.FirstOrDefaultAsync(m => m.TableName == "DsmUsers" && m.RemoteGuid == remoteDsmUserGuid);
            Assert.NotNull(mapping);
            Assert.Equal(rugvedLocalId, mapping.LocalId);
        }

        // 5. Test PerformPullAsync
        var pullMethod = typeof(SyncEngine).GetMethod("PerformPullAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(pullMethod);

        var pullTask = (Task)pullMethod.Invoke(syncEngine, new object[] { settings, true })!;
        await pullTask;

        // 6. Assertions on the reconciled assignment in the local database
        using (var verifyContext = CreateContext())
        {
            var updatedAssignment = await verifyContext.DsmPumpAssignments.FindAsync(assignmentLocalId);
            Assert.NotNull(updatedAssignment);
            Assert.Equal(rugvedLocalId, updatedAssignment.DsmUserId);
            Assert.Equal(1, updatedAssignment.PumpId);
            Assert.Equal("A", updatedAssignment.ShiftType);
            Assert.True(updatedAssignment.IsActive);
            Assert.Equal(2, updatedAssignment.ConnectedPumpId);
            Assert.Equal("[2]", updatedAssignment.ConnectedPumpIdsJson);

            // Verify canonical remote GUID supersedes the unconfirmed local GUID
            var canonicalMapping = await verifyContext.SyncIdMappings
                .FirstOrDefaultAsync(m => m.TableName == "DsmPumpAssignments" && m.RemoteGuid == canonicalAssignmentGuid);
            Assert.NotNull(canonicalMapping);
            Assert.Equal(assignmentLocalId, canonicalMapping.LocalId);

            var oldMapping = await verifyContext.SyncIdMappings
                .FirstOrDefaultAsync(m => m.TableName == "DsmPumpAssignments" && m.RemoteGuid == unconfirmedLocalGuid);
            Assert.Null(oldMapping);

            // Verify pending SyncChangeLog was marked as synced
            var changeLog = await verifyContext.SyncChangeLogs
                .FirstOrDefaultAsync(l => l.TableName == "DsmPumpAssignments" && l.RecordId == assignmentLocalId);
            Assert.NotNull(changeLog);
            Assert.True(changeLog.IsSynced);
            Assert.Equal(canonicalAssignmentGuid, changeLog.RecordGuid);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Test: Two-Computer Bidirectional Sync (Computer 1 <-> Computer 2, MultiComputerEnabled = false)
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task TwoComputerBidirectionalSync_Computer1AndComputer2_SyncsEntriesWithoutDuplicatesOrCrossStationLeak()
    {
        using var connPc1 = new SqliteConnection("DataSource=:memory:");
        using var connPc2 = new SqliteConnection("DataSource=:memory:");
        using var connOtherStation = new SqliteConnection("DataSource=:memory:");
        connPc1.Open();
        connPc2.Open();
        connOtherStation.Open();

        var opt1 = new DbContextOptionsBuilder<FuelProDbContext>().UseSqlite(connPc1).Options;
        var opt2 = new DbContextOptionsBuilder<FuelProDbContext>().UseSqlite(connPc2).Options;
        var opt3 = new DbContextOptionsBuilder<FuelProDbContext>().UseSqlite(connOtherStation).Options;

        using (var d1 = new FuelProDbContext(opt1)) d1.Database.EnsureCreated();
        using (var d2 = new FuelProDbContext(opt2)) d2.Database.EnsureCreated();
        using (var d3 = new FuelProDbContext(opt3)) d3.Database.EnsureCreated();

        var cloudStore = new System.Collections.Concurrent.ConcurrentDictionary<string, List<Dictionary<string, object?>>>(StringComparer.OrdinalIgnoreCase);

        static string GetField(Dictionary<string, object?> d, string key) =>
            d.TryGetValue(key, out var v) && v != null ? v.ToString() ?? "" : "";

        var sharedCloudHandler = new TestHttpMessageHandler(async req =>
        {
            var pathAndQuery = req.RequestUri!.PathAndQuery.TrimStart('/');
            var parts = pathAndQuery.Split('?', 2);
            var tableName = parts[0];
            var query = parts.Length > 1 ? parts[1] : "";
            var rows = cloudStore.GetOrAdd(tableName, _ => new List<Dictionary<string, object?>>());

            if (req.Method == HttpMethod.Post)
            {
                var body = await req.Content!.ReadAsStringAsync();
                var incoming = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(body)
                    ?? new List<Dictionary<string, object?>>();

                lock (rows)
                {
                    foreach (var raw in incoming)
                    {
                        var rec = new Dictionary<string, object?>(raw, StringComparer.OrdinalIgnoreCase);
                        rec["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ");
                        var sg = GetField(rec, "SyncGuid");
                        var idx = rows.FindIndex(r => string.Equals(GetField(r, "SyncGuid"), sg, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0) rows[idx] = rec;
                        else rows.Add(rec);
                    }
                }
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            }

            if (req.Method == HttpMethod.Get)
            {
                string? stationFilter = null;
                string? syncGuidFilter = null;
                foreach (var param in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (param.StartsWith("station_id=eq.", StringComparison.OrdinalIgnoreCase))
                        stationFilter = Uri.UnescapeDataString(param["station_id=eq.".Length..]);
                    else if (param.StartsWith("SyncGuid=eq.", StringComparison.OrdinalIgnoreCase))
                        syncGuidFilter = Uri.UnescapeDataString(param["SyncGuid=eq.".Length..]);
                }

                List<Dictionary<string, object?>> snapshot;
                lock (rows)
                {
                    snapshot = rows
                        .Where(r => stationFilter == null || string.Equals(GetField(r, "station_id"), stationFilter, StringComparison.Ordinal))
                        .Where(r => syncGuidFilter == null || string.Equals(GetField(r, "SyncGuid"), syncGuidFilter, StringComparison.OrdinalIgnoreCase))
                        .Where(r => !query.Contains("SyncGuid.gt.") && !query.Contains("updated_at.gt.")) // stop keyset page loop after page 1 unless explicitly initial query
                        .Select(r => new Dictionary<string, object?>(r, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }

                // Wait: initial pull has updated_at=gt....&updated_at=lte.... (NOT updated_at.gt.), pagination page 2 has SyncGuid.gt.
                if (query.Contains("SyncGuid.gt.", StringComparison.OrdinalIgnoreCase))
                    snapshot = new List<Dictionary<string, object?>>();

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(snapshot), Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        });

        IServiceProvider BuildWorkstation(SqliteConnection conn, string stationId, string machineId)
        {
            var sc = new ServiceCollection();
            sc.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(conn), ServiceLifetime.Transient);
            sc.AddSingleton<SyncConfigService>();
            var httpClient = new HttpClient(sharedCloudHandler);
            var sbClient = new SupabaseHttpClient(httpClient);
            sbClient.Configure("https://mock.supabase.co", "mock-key");
            sc.AddSingleton(sbClient);
            sc.AddSingleton<SyncEngine>();
            var provider = sc.BuildServiceProvider();
            provider.GetRequiredService<SyncConfigService>().SaveSettingsAsync(new SyncSettings
            {
                SyncEnabled = true,
                MultiComputerEnabled = false,
                SupabaseUrl = "https://mock.supabase.co",
                SupabaseApiKey = "mock-key",
                StationId = stationId,
                MachineId = machineId
            }).GetAwaiter().GetResult();
            return provider;
        }

        var sp1 = BuildWorkstation(connPc1, "MITALI-TWO-PC", "COMPUTER-01");
        var sp2 = BuildWorkstation(connPc2, "MITALI-TWO-PC", "COMPUTER-02");
        var sp3 = BuildWorkstation(connOtherStation, "OTHER-STATION-99", "COMPUTER-OTHER");

        var engine1 = sp1.GetRequiredService<SyncEngine>();
        var engine2 = sp2.GetRequiredService<SyncEngine>();
        var engine3 = sp3.GetRequiredService<SyncEngine>();

        var pushMethod = typeof(SyncEngine).GetMethod("PerformPushAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var pullMethod = typeof(SyncEngine).GetMethod("PerformPullAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // Scenario 1: Computer 1 creates a Shift + DsmEntry (with PINELABS_CARD Manual 500 + 2 identical TIDs 850+850 = 2200) and pushes
        var shiftDate = new DateTime(2026, 11, 26);
        using (var db1 = new FuelProDbContext(opt1))
        {
            var shift = new Shift { ShiftDate = shiftDate, ShiftType = "B", CreatedAt = DateTime.Now };
            db1.Shifts.Add(shift);
            await db1.SaveChangesAsync();

            var entry1 = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                DsmName = "Operator One",
                PumpId = 1,
                GrossSales = 2500m,
                TotalCollection = 2500m,
                Mismatch = 0m,
                CreatedAt = DateTime.Now
            };
            db1.DsmEntries.Add(entry1);
            await db1.SaveChangesAsync();

            db1.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = entry1.DsmEntryId,
                NozzleNumber = 1,
                FuelType = "MS-I",
                OpeningReading = 100,
                ClosingReading = 125,
                SaleLitres = 25,
                Rate = 100,
                Amount = 2500
            });
            var payment1 = new PaymentCollection
            {
                DsmEntryId = entry1.DsmEntryId,
                Items = new List<PaymentCollectionItem>
                {
                    new() { TransactionId = "manual-scalar-pinelabs_card", CollectionTypeCode = "PINELABS_CARD", Amount = 500, Slot = "Day", Otp = "" },
                    new() { TransactionId = "tid-pc1-a", CollectionTypeCode = "PINELABS_CARD", Amount = 850, Tid = "T1", Batch = "B1", Slot = "Day", Otp = "" },
                    new() { TransactionId = "tid-pc1-b", CollectionTypeCode = "PINELABS_CARD", Amount = 850, Tid = "T2", Batch = "B1", Slot = "Day", Otp = "" }
                }
            };
            db1.PaymentCollections.Add(payment1);
            db1.CashDenominations.Add(new CashDenomination { DsmEntryId = entry1.DsmEntryId, CashType = "Cash1", Denom100 = 3, TotalAmount = 300 });
            await db1.SaveChangesAsync();
        }

        var settings1 = await sp1.GetRequiredService<SyncConfigService>().GetSettingsAsync();
        var settings2 = await sp2.GetRequiredService<SyncConfigService>().GetSettingsAsync();
        var settings3 = await sp3.GetRequiredService<SyncConfigService>().GetSettingsAsync();

        await (Task)pushMethod.Invoke(engine1, new object[] { settings1 })!;
        await (Task)pullMethod.Invoke(engine2, new object[] { settings2, true })!;

        using (var db2Verify = new FuelProDbContext(opt2))
        {
            Assert.Single(await db2Verify.Shifts.ToListAsync());
            var e2List = await db2Verify.DsmEntries.Include(e => e.PaymentCollection).ThenInclude(p => p!.Items).ToListAsync();
            Assert.Single(e2List);
            Assert.Equal("Operator One", e2List[0].DsmName);
            Assert.Equal(2500m, e2List[0].GrossSales);
            Assert.Equal(2500m, e2List[0].TotalCollection);
            Assert.Equal(2200.0, e2List[0].PaymentCollection!.DynamicItemsTotal, precision: 2);
            Assert.Contains(e2List[0].PaymentCollection.Items, i => i.TransactionId == "manual-scalar-pinelabs_card" && Math.Abs(i.Amount - 500) < 0.01);
        }

        // Scenario 2: Computer 2 creates a second DsmEntry in the SAME pulled Shift (Pump 2) and pushes -> Computer 1 pulls
        using (var db2Write = new FuelProDbContext(opt2))
        {
            var sharedShift = await db2Write.Shifts.FirstAsync();
            var entry2 = new DsmEntry
            {
                ShiftId = sharedShift.ShiftId,
                DsmName = "Operator Two",
                PumpId = 2,
                GrossSales = 4000m,
                TotalCollection = 4000m,
                Mismatch = 0m,
                CreatedAt = DateTime.Now
            };
            db2Write.DsmEntries.Add(entry2);
            await db2Write.SaveChangesAsync();

            db2Write.NozzleReadings.Add(new NozzleReading
            {
                DsmEntryId = entry2.DsmEntryId,
                NozzleNumber = 2,
                FuelType = "MS-I",
                OpeningReading = 200,
                ClosingReading = 240,
                SaleLitres = 40,
                Rate = 100,
                Amount = 4000
            });
            await db2Write.SaveChangesAsync();
        }

        await (Task)pushMethod.Invoke(engine2, new object[] { settings2 })!;
        await (Task)pullMethod.Invoke(engine1, new object[] { settings1, true })!;

        // Scenario 3: Repeated cycles produce zero duplicates; both computers have identical 2 DsmEntries under 1 Shift
        await (Task)pushMethod.Invoke(engine1, new object[] { settings1 })!;
        await (Task)pullMethod.Invoke(engine2, new object[] { settings2, true })!;

        using (var db1Verify = new FuelProDbContext(opt1))
        using (var db2Verify = new FuelProDbContext(opt2))
        {
            Assert.Single(await db1Verify.Shifts.ToListAsync());
            Assert.Single(await db2Verify.Shifts.ToListAsync());

            var e1 = await db1Verify.DsmEntries.OrderBy(e => e.PumpId).ToListAsync();
            var e2 = await db2Verify.DsmEntries.OrderBy(e => e.PumpId).ToListAsync();
            Assert.Equal(2, e1.Count);
            Assert.Equal(2, e2.Count);
            Assert.Equal(6500m, e1.Sum(x => x.GrossSales));
            Assert.Equal(6500m, e2.Sum(x => x.GrossSales));
        }

        // Scenario 3b: Update existing entry on Computer 1 -> propagates to Computer 2 without duplicating records
        using (var db1Update = new FuelProDbContext(opt1))
        {
            var target = await db1Update.DsmEntries.FirstAsync(e => e.PumpId == 1);
            target.TotalCollection = 2600m;
            target.Mismatch = 100m;
            await db1Update.SaveChangesAsync();
        }
        await (Task)pushMethod.Invoke(engine1, new object[] { settings1 })!;
        await (Task)pullMethod.Invoke(engine2, new object[] { settings2, true })!;
        using (var db2AfterUpdate = new FuelProDbContext(opt2))
        {
            var e2List = await db2AfterUpdate.DsmEntries.ToListAsync();
            Assert.Equal(2, e2List.Count);
            var updated = e2List.First(e => e.PumpId == 1);
            Assert.Equal(2600m, updated.TotalCollection);
            Assert.Equal(100m, updated.Mismatch);
        }

        // Scenario 4: Other station isolation - OTHER-STATION-99 sees zero records from MITALI-TWO-PC
        await (Task)pullMethod.Invoke(engine3, new object[] { settings3, true })!;
        using (var db3Verify = new FuelProDbContext(opt3))
        {
            Assert.Empty(await db3Verify.Shifts.ToListAsync());
            Assert.Empty(await db3Verify.DsmEntries.ToListAsync());
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helper: TestHttpMessageHandler
    // ────────────────────────────────────────────────────────────────────────
    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public TestHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}
