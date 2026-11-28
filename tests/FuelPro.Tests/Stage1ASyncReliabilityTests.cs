using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
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

public class Stage1ASyncReliabilityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FuelProDbContext> _options;

    public Stage1ASyncReliabilityTests()
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
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE"
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

    private static async Task InvokePerformPullAsync(SyncEngine engine, SyncSettings settings, bool forcePull = false)
    {
        var method = typeof(SyncEngine).GetMethod("PerformPullAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(method);
        var task = (Task)method.Invoke(engine, new object[] { settings, forcePull })!;
        await task;
    }

    private static HttpResponseMessage JsonResponse(object obj, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var json = obj is string str ? str : JsonConvert.SerializeObject(obj);
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 1: Earlier-page record updated during pagination (moves to later page)
    // -> Keyset retrieves all records without skipping
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Scenario01_EarlierRecordUpdatedDuringPagination_KeysetRetrievesAllRecordsWithoutSkipping()
    {
        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE"
        };

        var pageRequests = 0;
        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("/Creditors?"))
            {
                pageRequests++;
                if (pageRequests == 1)
                {
                    // Page 1: returns record 1
                    var page1 = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            { "SyncGuid", "cred-001" },
                            { "Name", "Creditor 1" },
                            { "updated_at", "2026-11-20T10:00:00.000000Z" },
                            { "MachineId", "REMOTE-MACHINE" }
                        }
                    };
                    return Task.FromResult(JsonResponse(page1));
                }
                else if (pageRequests == 2)
                {
                    // While page 1 was processed, cred-001 was updated on server to 10:05:00Z.
                    // Keyset query requests > (10:00:00, cred-001).
                    // Even though cred-001 moved to the end, records 2 and 3 are returned cleanly without skipping.
                    Assert.True(url.Contains("updated_at=gte.2026-11-20T10:00:00.000000Z") || url.Contains("updated_at=gte.2026-11-20T10%3A00%3A00.000000Z"),
                        $"URL should contain updated_at=gte.2026-11-20T10:00:00.000000Z but was '{url}'");
                    Assert.Contains("SyncGuid.gt.cred-001", url);

                    var page2 = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            { "SyncGuid", "cred-002" },
                            { "Name", "Creditor 2" },
                            { "updated_at", "2026-11-20T10:01:00.000000Z" },
                            { "MachineId", "REMOTE-MACHINE" }
                        },
                        new Dictionary<string, object?>
                        {
                            { "SyncGuid", "cred-003" },
                            { "Name", "Creditor 3" },
                            { "updated_at", "2026-11-20T10:02:00.000000Z" },
                            { "MachineId", "REMOTE-MACHINE" }
                        }
                    };
                    return Task.FromResult(JsonResponse(page2));
                }
                else
                {
                    return Task.FromResult(JsonResponse("[]"));
                }
            }
            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        await InvokePerformPullAsync(syncEngine, settings);

        using var verifyContext = CreateContext();
        var creditors = await verifyContext.Creditors.OrderBy(c => c.Name).ToListAsync();
        Assert.Equal(3, creditors.Count);
        Assert.Equal("Creditor 1", creditors[0].Name);
        Assert.Equal("Creditor 2", creditors[1].Name);
        Assert.Equal("Creditor 3", creditors[2].Name);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 2: Record updated beyond upper watermark
    // -> Excluded from current cycle, retrieved on next cycle via lookback
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Scenario02_RecordUpdatedBeyondUpperWatermark_ExcludedFromCurrentCycle_RetrievedOnNextCycle()
    {
        var initialPullTime = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE",
            LastPullTime = initialPullTime,
            LastSyncTime = initialPullTime
        };

        int cycle = 1;
        var requestedUrls = new List<string>();

        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            requestedUrls.Add(url);

            if (url.Contains("/Creditors?"))
            {
                if (cycle == 1)
                {
                    // Verify upper watermark is present in query
                    Assert.Contains("updated_at=lte.", url);

                    // Cycle 1: Server only returns records <= upperWatermark.
                    // cred-post-watermark is beyond watermark and thus excluded by server.
                    var records = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            { "SyncGuid", "cred-within-watermark" },
                            { "Name", "Creditor Within Watermark" },
                            { "updated_at", "2026-11-20T08:30:00.000000Z" },
                            { "MachineId", "REMOTE-MACHINE" }
                        }
                    };
                    return Task.FromResult(JsonResponse(records));
                }
                else
                {
                    // Cycle 2: Retrieved via lookback (pullStartTime minus 5 minutes).
                    // Server now returns cred-post-watermark.
                    var records = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            { "SyncGuid", "cred-post-watermark" },
                            { "Name", "Creditor Post Watermark" },
                            { "updated_at", "2026-11-20T09:00:00.000000Z" },
                            { "MachineId", "REMOTE-MACHINE" }
                        }
                    };
                    return Task.FromResult(JsonResponse(records));
                }
            }
            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var configService = sp.GetRequiredService<SyncConfigService>();

        // Execute Cycle 1
        await InvokePerformPullAsync(syncEngine, settings);

        using (var verify1 = CreateContext())
        {
            var count1 = await verify1.Creditors.CountAsync();
            Assert.Equal(1, count1);
            var creditor1 = await verify1.Creditors.FirstAsync();
            Assert.Equal("Creditor Within Watermark", creditor1.Name);
        }

        var settingsAfterCycle1 = await configService.GetSettingsAsync();
        Assert.True(settingsAfterCycle1.LastPullTime > initialPullTime);

        // Execute Cycle 2
        cycle = 2;
        await InvokePerformPullAsync(syncEngine, settingsAfterCycle1);

        using (var verify2 = CreateContext())
        {
            var count2 = await verify2.Creditors.CountAsync();
            Assert.Equal(2, count2);
            var names = await verify2.Creditors.Select(c => c.Name).ToListAsync();
            Assert.Contains("Creditor Within Watermark", names);
            Assert.Contains("Creditor Post Watermark", names);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 3: New record inserted during pagination
    // -> Retrieved cleanly without cursor shift
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Scenario03_NewRecordInsertedDuringPagination_RetrievedCleanlyWithoutCursorShift()
    {
        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE"
        };

        var pageRequests = 0;
        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("/Creditors?"))
            {
                pageRequests++;
                if (pageRequests == 1)
                {
                    // Page 1 returns record 1
                    var page1 = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            { "SyncGuid", "cred-001" },
                            { "Name", "Creditor 1" },
                            { "updated_at", "2026-11-20T10:00:00.000000Z" },
                            { "MachineId", "REMOTE-MACHINE" }
                        }
                    };
                    return Task.FromResult(JsonResponse(page1));
                }
                else if (pageRequests == 2)
                {
                    // While page 1 was running, record 4 was inserted ahead of cursor
                    var page2 = new[]
                    {
                        new Dictionary<string, object?>
                        {
                            { "SyncGuid", "cred-002" },
                            { "Name", "Creditor 2" },
                            { "updated_at", "2026-11-20T10:01:00.000000Z" },
                            { "MachineId", "REMOTE-MACHINE" }
                        },
                        new Dictionary<string, object?>
                        {
                            { "SyncGuid", "cred-004-new" },
                            { "Name", "Creditor 4 Inserted" },
                            { "updated_at", "2026-11-20T10:03:00.000000Z" },
                            { "MachineId", "REMOTE-MACHINE" }
                        }
                    };
                    return Task.FromResult(JsonResponse(page2));
                }
                else
                {
                    return Task.FromResult(JsonResponse("[]"));
                }
            }
            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        await InvokePerformPullAsync(syncEngine, settings);

        using var verifyContext = CreateContext();
        var creditors = await verifyContext.Creditors.OrderBy(c => c.Name).ToListAsync();
        Assert.Equal(3, creditors.Count);
        Assert.Contains(creditors, c => c.Name == "Creditor 4 Inserted");
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 4: Multiple records sharing identical updated_at
    // -> Deterministic tie-breaking via SyncGuid.asc
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Scenario04_MultipleRecordsSharingIdenticalUpdatedAt_DeterministicTieBreakingViaSyncGuidAsc()
    {
        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE"
        };

        var allRecords = new[]
        {
            new Dictionary<string, object?> { { "SyncGuid", "guid-a" }, { "Name", "A" }, { "updated_at", "2026-11-20T10:00:00.000000Z" }, { "MachineId", "REMOTE-MACHINE" } },
            new Dictionary<string, object?> { { "SyncGuid", "guid-b" }, { "Name", "B" }, { "updated_at", "2026-11-20T10:00:00.000000Z" }, { "MachineId", "REMOTE-MACHINE" } },
            new Dictionary<string, object?> { { "SyncGuid", "guid-c" }, { "Name", "C" }, { "updated_at", "2026-11-20T10:00:00.000000Z" }, { "MachineId", "REMOTE-MACHINE" } },
            new Dictionary<string, object?> { { "SyncGuid", "guid-d" }, { "Name", "D" }, { "updated_at", "2026-11-20T10:00:00.000000Z" }, { "MachineId", "REMOTE-MACHINE" } },
            new Dictionary<string, object?> { { "SyncGuid", "guid-e" }, { "Name", "E" }, { "updated_at", "2026-11-20T10:00:00.000000Z" }, { "MachineId", "REMOTE-MACHINE" } },
        };

        var requestedUrls = new List<string>();
        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            requestedUrls.Add(url);

            if (url.Contains("/Creditors?"))
            {
                // Parse keyset cursor if present: SyncGuid.gt.{guid}
                var match = Regex.Match(url, @"SyncGuid\.gt\.([a-zA-Z0-9\-_]+)");
                string? afterGuid = match.Success ? match.Groups[1].Value : null;

                IEnumerable<Dictionary<string, object?>> query = allRecords;
                if (!string.IsNullOrEmpty(afterGuid))
                {
                    query = query.Where(r => string.Compare(r["SyncGuid"]!.ToString(), afterGuid, StringComparison.OrdinalIgnoreCase) > 0);
                }

                // Page size simulated as 2
                var page = query.Take(2).ToList();
                return Task.FromResult(JsonResponse(page));
            }
            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        await InvokePerformPullAsync(syncEngine, settings);

        // Verification: all 5 records persisted
        using var verifyContext = CreateContext();
        var creditors = await verifyContext.Creditors.OrderBy(c => c.Name).ToListAsync();
        Assert.Equal(5, creditors.Count);
        Assert.Equal(new[] { "A", "B", "C", "D", "E" }, creditors.Select(c => c.Name));

        // Verification: query included tie-breaker SyncGuid.asc in order clause
        var creditorUrls = requestedUrls.Where(u => u.Contains("/Creditors?")).ToList();
        Assert.True(creditorUrls.Count >= 3);
        foreach (var url in creditorUrls)
        {
            Assert.Contains("order=updated_at.asc,SyncGuid.asc", url);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 5: Server max_rows = 200 (limit 500)
    // -> Pulls 450 records across 3 pages without early truncation
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Scenario05_ServerMaxRows200_Pulls450RecordsAcrossPages_WithoutEarlyTruncation()
    {
        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE"
        };

        var allRecords = Enumerable.Range(0, 450).Select(i => new Dictionary<string, object?>
        {
            { "SyncGuid", $"cred-{i:D4}" },
            { "Name", $"Creditor {i:D4}" },
            { "updated_at", new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc).AddSeconds(i).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ") },
            { "MachineId", "REMOTE-MACHINE" }
        }).ToList();

        var requestsCount = 0;
        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("/Creditors?"))
            {
                requestsCount++;
                var match = Regex.Match(url, @"SyncGuid\.gt\.([a-zA-Z0-9\-_]+)");
                string? afterGuid = match.Success ? match.Groups[1].Value : null;

                IEnumerable<Dictionary<string, object?>> query = allRecords;
                if (!string.IsNullOrEmpty(afterGuid))
                {
                    query = query.Where(r => string.Compare(r["SyncGuid"]!.ToString(), afterGuid, StringComparison.OrdinalIgnoreCase) > 0);
                }

                // Server enforces max_rows = 200 (even though client requests limit=500)
                var page = query.Take(200).ToList();
                return Task.FromResult(JsonResponse(page));
            }
            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var configService = sp.GetRequiredService<SyncConfigService>();

        await InvokePerformPullAsync(syncEngine, settings);

        // Verification: all 450 records persisted without early truncation
        using var verifyContext = CreateContext();
        var count = await verifyContext.Creditors.CountAsync();
        Assert.Equal(450, count);

        // Verification: 3 pages of records + 1 empty page = 4 requests
        Assert.Equal(4, requestsCount);

        // Verification: checkpoint advanced
        var updatedSettings = await configService.GetSettingsAsync();
        Assert.True(updatedSettings.LastPullTime > DateTime.MinValue);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 6: Server max_rows = 100 (limit 500)
    // -> Pulls 250 records across 3 pages without early truncation
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Scenario06_ServerMaxRows100_Pulls250RecordsAcrossPages_WithoutEarlyTruncation()
    {
        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE"
        };

        var allRecords = Enumerable.Range(0, 250).Select(i => new Dictionary<string, object?>
        {
            { "SyncGuid", $"cred-{i:D4}" },
            { "Name", $"Creditor {i:D4}" },
            { "updated_at", new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc).AddSeconds(i).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ") },
            { "MachineId", "REMOTE-MACHINE" }
        }).ToList();

        var requestsCount = 0;
        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("/Creditors?"))
            {
                requestsCount++;
                var match = Regex.Match(url, @"SyncGuid\.gt\.([a-zA-Z0-9\-_]+)");
                string? afterGuid = match.Success ? match.Groups[1].Value : null;

                IEnumerable<Dictionary<string, object?>> query = allRecords;
                if (!string.IsNullOrEmpty(afterGuid))
                {
                    query = query.Where(r => string.Compare(r["SyncGuid"]!.ToString(), afterGuid, StringComparison.OrdinalIgnoreCase) > 0);
                }

                // Server enforces max_rows = 100
                var page = query.Take(100).ToList();
                return Task.FromResult(JsonResponse(page));
            }
            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        await InvokePerformPullAsync(syncEngine, settings);

        using var verifyContext = CreateContext();
        var count = await verifyContext.Creditors.CountAsync();
        Assert.Equal(250, count);
        Assert.Equal(4, requestsCount); // 100 + 100 + 50 + 0 = 4 requests
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 7: Server max_rows = 50 (limit 500)
    // -> Pulls 120 records across 3 pages without early truncation
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Scenario07_ServerMaxRows50_Pulls120RecordsAcrossPages_WithoutEarlyTruncation()
    {
        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE"
        };

        var allRecords = Enumerable.Range(0, 120).Select(i => new Dictionary<string, object?>
        {
            { "SyncGuid", $"cred-{i:D4}" },
            { "Name", $"Creditor {i:D4}" },
            { "updated_at", new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc).AddSeconds(i).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ") },
            { "MachineId", "REMOTE-MACHINE" }
        }).ToList();

        var requestsCount = 0;
        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("/Creditors?"))
            {
                requestsCount++;
                var match = Regex.Match(url, @"SyncGuid\.gt\.([a-zA-Z0-9\-_]+)");
                string? afterGuid = match.Success ? match.Groups[1].Value : null;

                IEnumerable<Dictionary<string, object?>> query = allRecords;
                if (!string.IsNullOrEmpty(afterGuid))
                {
                    query = query.Where(r => string.Compare(r["SyncGuid"]!.ToString(), afterGuid, StringComparison.OrdinalIgnoreCase) > 0);
                }

                // Server enforces max_rows = 50
                var page = query.Take(50).ToList();
                return Task.FromResult(JsonResponse(page));
            }
            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        await InvokePerformPullAsync(syncEngine, settings);

        using var verifyContext = CreateContext();
        var count = await verifyContext.Creditors.CountAsync();
        Assert.Equal(120, count);
        Assert.Equal(4, requestsCount); // 50 + 50 + 20 + 0 = 4 requests
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 8: Table lacking updated_at with valid SyncGuid
    // -> Paginates via SyncGuid keyset
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Scenario08_TableLackingUpdatedAt_PaginatesViaSyncGuidKeyset()
    {
        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE"
        };

        var allRecords = Enumerable.Range(0, 120).Select(i => new Dictionary<string, object?>
        {
            { "SyncGuid", $"cred-{i:D4}" },
            { "Name", $"Creditor {i:D4}" },
            { "MachineId", "REMOTE-MACHINE" }
        }).ToList();

        var requestedUrls = new List<string>();
        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            requestedUrls.Add(url);

            if (url.Contains("/Creditors?"))
            {
                // First request queries with updated_at -> simulate error 42703 (undefined column)
                if (url.Contains("updated_at="))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("{\"code\":\"42703\",\"message\":\"column updated_at does not exist\"}", Encoding.UTF8, "application/json")
                    });
                }

                // Fallback queries by SyncGuid (supports both SyncGuid=gt.{guid} and SyncGuid.gt.{guid})
                var match = Regex.Match(url, @"SyncGuid(=|\.)gt\.([a-zA-Z0-9\-_]+)");
                string? afterGuid = match.Success ? match.Groups[2].Value : null;

                IEnumerable<Dictionary<string, object?>> query = allRecords;
                if (!string.IsNullOrEmpty(afterGuid))
                {
                    query = query.Where(r => string.Compare(r["SyncGuid"]!.ToString(), afterGuid, StringComparison.OrdinalIgnoreCase) > 0);
                }

                var page = query.Take(50).ToList();
                return Task.FromResult(JsonResponse(page));
            }
            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        await InvokePerformPullAsync(syncEngine, settings);

        // Verification: all 120 records persisted via SyncGuid keyset fallback
        using var verifyContext = CreateContext();
        var count = await verifyContext.Creditors.CountAsync();
        Assert.Equal(120, count);

        // Verification: fallback queries used order=SyncGuid.asc
        var fallbackUrls = requestedUrls.Where(u => u.Contains("/Creditors?") && !u.Contains("updated_at=")).ToList();
        Assert.NotEmpty(fallbackUrls);
        Assert.All(fallbackUrls, u => Assert.Contains("order=SyncGuid.asc", u));
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 9: Table lacking unique key
    // -> Fails safely, retains checkpoint
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Scenario09_TableLackingUniqueKey_FailsSafely_RetainsCheckpoint()
    {
        var initialPullTime = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE",
            LastPullTime = initialPullTime,
            LastSyncTime = initialPullTime
        };

        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";

            // Simulate Settings table (which has no SyncGuid in EF model) returning 42703
            if (url.Contains("/Settings?"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"code\":\"42703\",\"message\":\"column updated_at does not exist\"}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var configService = sp.GetRequiredService<SyncConfigService>();

        await InvokePerformPullAsync(syncEngine, settings);

        // Verification: checkpoint was retained because table lacking unique key failed safely
        var updatedSettings = await configService.GetSettingsAsync();
        Assert.Equal(initialPullTime.ToUniversalTime(), updatedSettings.LastPullTime.ToUniversalTime());
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 10: Mid-pagination failure followed by successful retry
    // -> Checkpoint advances only after full success
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Scenario10_MidPaginationFailure_RetainsCheckpoint_AdvancesOnRetry()
    {
        var initialPullTime = new DateTime(2026, 11, 20, 7, 30, 0, DateTimeKind.Utc);
        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE",
            LastPullTime = initialPullTime,
            LastSyncTime = initialPullTime
        };

        bool shouldFailPage2 = true;

        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("/Creditors?"))
            {
                if (!url.Contains("SyncGuid.gt."))
                {
                    // Page 1: 200 records
                    var page1 = Enumerable.Range(0, 200).Select(i => new Dictionary<string, object?>
                    {
                        { "SyncGuid", $"cred-{i:D4}" },
                        { "Name", $"Creditor {i:D4}" },
                        { "updated_at", "2026-11-20T08:00:00.000000Z" },
                        { "MachineId", "REMOTE-MACHINE" }
                    }).ToList();
                    return Task.FromResult(JsonResponse(page1));
                }
                else
                {
                    if (shouldFailPage2)
                    {
                        // Mid-pagination network / server error on page 2
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
                        {
                            Content = new StringContent("{\"message\":\"Bad Gateway mid-pagination\"}", Encoding.UTF8, "application/json")
                        });
                    }
                    else
                    {
                        // Retry: Page 2 succeeds with remaining 50 records
                        var page2 = Enumerable.Range(200, 50).Select(i => new Dictionary<string, object?>
                        {
                            { "SyncGuid", $"cred-{i:D4}" },
                            { "Name", $"Creditor {i:D4}" },
                            { "updated_at", "2026-11-20T08:01:00.000000Z" },
                            { "MachineId", "REMOTE-MACHINE" }
                        }).ToList();
                        return Task.FromResult(JsonResponse(page2));
                    }
                }
            }
            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var configService = sp.GetRequiredService<SyncConfigService>();

        // 1. Initial attempt fails mid-pagination
        await InvokePerformPullAsync(syncEngine, settings);

        using (var verify1 = CreateContext())
        {
            // Table was aborted: partial records NOT committed
            Assert.Equal(0, await verify1.Creditors.CountAsync());
        }

        var settingsAfterFail = await configService.GetSettingsAsync();
        Assert.Equal(initialPullTime.ToUniversalTime(), settingsAfterFail.LastPullTime.ToUniversalTime());

        // 2. Retry succeeds completely
        shouldFailPage2 = false;
        await InvokePerformPullAsync(syncEngine, settingsAfterFail);

        using (var verify2 = CreateContext())
        {
            Assert.Equal(250, await verify2.Creditors.CountAsync());
        }

        var settingsAfterSuccess = await configService.GetSettingsAsync();
        Assert.True(settingsAfterSuccess.LastPullTime > initialPullTime);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 11: Checkpoint persistence failure
    // -> Next cycle re-reads from previous checkpoint idempotently
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Scenario11_CheckpointPersistenceFailure_NextCycleReReadsIdempotently()
    {
        var initialPullTime = new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE",
            LastPullTime = initialPullTime,
            LastSyncTime = initialPullTime
        };

        var records = new[]
        {
            new Dictionary<string, object?> { { "SyncGuid", "cred-idem-01" }, { "Name", "Creditor 1" }, { "updated_at", "2026-11-20T08:15:00.000000Z" }, { "MachineId", "REMOTE-MACHINE" } },
            new Dictionary<string, object?> { { "SyncGuid", "cred-idem-02" }, { "Name", "Creditor 2" }, { "updated_at", "2026-11-20T08:20:00.000000Z" }, { "MachineId", "REMOTE-MACHINE" } }
        };

        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("/Creditors?"))
            {
                return Task.FromResult(JsonResponse(records));
            }
            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        // Cycle 1: pulls data, but simulate checkpoint not advancing (retains initialPullTime)
        await InvokePerformPullAsync(syncEngine, settings);

        using (var verify1 = CreateContext())
        {
            Assert.Equal(2, await verify1.Creditors.CountAsync());
            Assert.Equal(2, await verify1.SyncIdMappings.CountAsync(m => m.TableName == "Creditors"));
        }

        // Cycle 2: runs with old settings (simulating checkpoint persistence failure on cycle 1)
        await InvokePerformPullAsync(syncEngine, settings);

        // Verification: perfectly idempotent, zero duplicate rows, zero duplicate mappings
        using (var verify2 = CreateContext())
        {
            Assert.Equal(2, await verify2.Creditors.CountAsync());
            Assert.Equal(2, await verify2.SyncIdMappings.CountAsync(m => m.TableName == "Creditors"));
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Scenario 12: Repeated pulls
    // -> Zero duplicate records or mappings
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Scenario12_RepeatedPulls_ZeroDuplicateRecordsOrMappings()
    {
        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE"
        };

        var creditors = new[]
        {
            new Dictionary<string, object?> { { "SyncGuid", "creditor-rep-01" }, { "Name", "Alpha Fuels" }, { "updated_at", "2026-11-20T08:00:00.000000Z" }, { "MachineId", "REMOTE-MACHINE" } },
            new Dictionary<string, object?> { { "SyncGuid", "creditor-rep-02" }, { "Name", "Beta Transports" }, { "updated_at", "2026-11-20T08:05:00.000000Z" }, { "MachineId", "REMOTE-MACHINE" } }
        };

        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("/Creditors?"))
            {
                return Task.FromResult(JsonResponse(creditors));
            }
            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var configService = sp.GetRequiredService<SyncConfigService>();

        // 1st pull
        await InvokePerformPullAsync(syncEngine, settings);

        using (var verify1 = CreateContext())
        {
            Assert.Equal(2, await verify1.Creditors.CountAsync());
            Assert.Equal(2, await verify1.SyncIdMappings.CountAsync(m => m.TableName == "Creditors"));
        }

        var updatedSettings = await configService.GetSettingsAsync();

        // 2nd pull with same records
        await InvokePerformPullAsync(syncEngine, updatedSettings);

        using (var verify2 = CreateContext())
        {
            Assert.Equal(2, await verify2.Creditors.CountAsync());
            Assert.Equal(2, await verify2.SyncIdMappings.CountAsync(m => m.TableName == "Creditors"));
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Additional Test 13: Natural-key matching protection with distinct same-value transactions
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test13_NaturalKeyMatchingProtection_DistinctSameValueTransactions_DoNotCollapse()
    {
        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE"
        };

        // 1. Seed local database with Debtor and first Repayment
        using (var setupContext = CreateContext())
        {
            var debtor = new DsmPersonalDebtor
            {
                Id = 1,
                DsmName = "Sameer Patil",
                Date = DateTime.Today,
                Time = "10:00",
                Amount = 5000,
                SyncGuid = "debtor-guid-1"
            };
            setupContext.DsmPersonalDebtors.Add(debtor);

            var repayment1 = new DsmPersonalDebtorRepayment
            {
                Id = 1,
                DsmPersonalDebtorId = 1,
                Date = new DateTime(2026, 11, 20),
                Amount = 1500.0,
                PaymentMethod = "Cash",
                Source = "ManagerShiftTotal",
                SyncGuid = "repayment-guid-alpha"
            };
            setupContext.DsmPersonalDebtorRepayments.Add(repayment1);

            setupContext.SyncIdMappings.Add(new SyncIdMapping
            {
                TableName = "DsmPersonalDebtorRepayments",
                RemoteGuid = "repayment-guid-alpha",
                LocalId = 1
            });

            await setupContext.SaveChangesAsync();
        }

        // 2. Mock Supabase returning a SECOND distinct repayment with IDENTICAL business values:
        // Same debtor ID (1), same Date (2026-11-20), same Amount (1500), same PaymentMethod ("Cash"),
        // but with a DIFFERENT RemoteGuid ("repayment-guid-beta")
        var incomingRepayments = new[]
        {
            new Dictionary<string, object?>
            {
                { "SyncGuid", "repayment-guid-beta" },
                { "DsmPersonalDebtorId", 1 },
                { "Date", "2026-11-20T00:00:00" },
                { "Amount", 1500.0 },
                { "PaymentMethod", "Cash" },
                { "Source", "ManagerShiftTotal" },
                { "updated_at", "2026-11-20T10:00:00.000000Z" },
                { "MachineId", "REMOTE-MACHINE" }
            }
        };

        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("/DsmPersonalDebtorRepayments?"))
            {
                return Task.FromResult(JsonResponse(incomingRepayments));
            }
            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        await InvokePerformPullAsync(syncEngine, settings);

        // 3. Verify that two DISTINCT repayments exist locally and did NOT collapse into one
        using var verifyContext = CreateContext();
        var repayments = await verifyContext.DsmPersonalDebtorRepayments.ToListAsync();
        Assert.Equal(2, repayments.Count);
        Assert.All(repayments, r => Assert.Equal(1500.0, r.Amount));
        Assert.All(repayments, r => Assert.Equal("Cash", r.PaymentMethod));
    }

    // ────────────────────────────────────────────────────────────────────────
    // Additional Test 14: SyncIdMappings retains both distinct mappings independently
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test14_SyncIdMappings_RetainsBothDistinctMappingsIndependently()
    {
        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-api-key",
            StationId = "TEST-STATION-01",
            MachineId = "LOCAL-MACHINE"
        };

        using (var setupContext = CreateContext())
        {
            var debtor = new DsmPersonalDebtor
            {
                Id = 2,
                DsmName = "Rajesh Sharma",
                Date = DateTime.Today,
                Time = "10:00",
                Amount = 5000,
                SyncGuid = "debtor-guid-2"
            };
            setupContext.DsmPersonalDebtors.Add(debtor);

            var repayment1 = new DsmPersonalDebtorRepayment
            {
                Id = 10,
                DsmPersonalDebtorId = 2,
                Date = new DateTime(2026, 11, 20),
                Amount = 3000.0,
                PaymentMethod = "UPI",
                Source = "ManagerShiftTotal",
                SyncGuid = "repayment-guid-first"
            };
            setupContext.DsmPersonalDebtorRepayments.Add(repayment1);

            setupContext.SyncIdMappings.Add(new SyncIdMapping
            {
                TableName = "DsmPersonalDebtorRepayments",
                RemoteGuid = "repayment-guid-first",
                LocalId = 10
            });

            await setupContext.SaveChangesAsync();
        }

        var incomingRepayments = new[]
        {
            new Dictionary<string, object?>
            {
                { "SyncGuid", "repayment-guid-second" },
                { "DsmPersonalDebtorId", 2 },
                { "Date", "2026-11-20T00:00:00" },
                { "Amount", 3000.0 },
                { "PaymentMethod", "UPI" },
                { "Source", "ManagerShiftTotal" },
                { "updated_at", "2026-11-20T10:00:00.000000Z" },
                { "MachineId", "REMOTE-MACHINE" }
            }
        };

        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            if (url.Contains("/DsmPersonalDebtorRepayments?"))
            {
                return Task.FromResult(JsonResponse(incomingRepayments));
            }
            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        await InvokePerformPullAsync(syncEngine, settings);

        using var verifyContext = CreateContext();
        var mappings = await verifyContext.SyncIdMappings
            .Where(m => m.TableName == "DsmPersonalDebtorRepayments")
            .ToListAsync();

        Assert.Equal(2, mappings.Count);

        var firstMapping = mappings.FirstOrDefault(m => m.RemoteGuid == "repayment-guid-first");
        var secondMapping = mappings.FirstOrDefault(m => m.RemoteGuid == "repayment-guid-second");

        Assert.NotNull(firstMapping);
        Assert.NotNull(secondMapping);

        Assert.Equal(10, firstMapping.LocalId);
        Assert.NotEqual(10, secondMapping.LocalId);
        Assert.True(secondMapping.LocalId > 0);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Additional Test 15: Ascending order pagination safety
    // ────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Test15_AscendingOrder_PreservesPaginationIntegrity()
    {
        var requestedUrls = new List<string>();
        var handler = new TestHttpMessageHandler(req =>
        {
            var url = req.RequestUri?.ToString() ?? "";
            requestedUrls.Add(url);
            return Task.FromResult(JsonResponse("[]"));
        });

        var client = new HttpClient(handler);
        var sp = CreateServiceProvider(client);
        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var settings = await sp.GetRequiredService<SyncConfigService>().GetSettingsAsync();

        await InvokePerformPullAsync(syncEngine, settings);

        foreach (var url in requestedUrls)
        {
            Assert.DoesNotContain("updated_at.desc", url);
        }
    }

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
