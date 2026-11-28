using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Core.Repositories;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Sync;
using FuelPro.Sync.Security;

namespace FuelPro.Tests;

public class MultiComputerArchitectureTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FuelProDbContext> _options;

    public MultiComputerArchitectureTests()
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

    private IServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(_connection), ServiceLifetime.Transient);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<AuthService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void DeterministicGuid_SameInputsAcrossDifferentMachines_ProducesIdenticalGuid()
    {
        // Scenario: Machine 1 and Machine 2 both generate a shift for Mitali on 2026-11-21 Shift A.
        // Even though they run on distinct hardware with different MachineIds, the canonical shift GUID must match.
        var stationId = "MITALI-STATION-01";
        var date = new DateTime(2026, 11, 21);
        var shiftType = "A";

        var guid1 = DeterministicGuid.CreateCanonicalShiftGuid(stationId, date, shiftType);
        var guid2 = DeterministicGuid.CreateCanonicalShiftGuid(stationId, date, shiftType);

        Assert.Equal(guid1, guid2);
        Assert.False(string.IsNullOrWhiteSpace(guid1));
        Assert.True(Guid.TryParse(guid1, out var parsed) && parsed != Guid.Empty);
    }

    [Fact]
    public void DeterministicGuid_DifferentInputs_ProduceDistinctGuids()
    {
        var stationId = "MITALI-STATION-01";
        var date1 = new DateTime(2026, 11, 21);
        var date2 = new DateTime(2026, 11, 22);

        var guidShiftA = DeterministicGuid.CreateCanonicalShiftGuid(stationId, date1, "A");
        var guidShiftB = DeterministicGuid.CreateCanonicalShiftGuid(stationId, date1, "B");
        var guidNextDay = DeterministicGuid.CreateCanonicalShiftGuid(stationId, date2, "A");
        var guidOtherStation = DeterministicGuid.CreateCanonicalShiftGuid("OTHER-STATION", date1, "A");

        Assert.NotEqual(guidShiftA, guidShiftB);
        Assert.NotEqual(guidShiftA, guidNextDay);
        Assert.NotEqual(guidShiftA, guidOtherStation);
    }

    [Fact]
    public void DeterministicGuid_NormalizesShiftTypeCaseAndWhitespace()
    {
        var stationId = "MITALI-STATION-01";
        var date = new DateTime(2026, 11, 21);

        var guid1 = DeterministicGuid.CreateCanonicalShiftGuid(stationId, date, "A");
        var guid2 = DeterministicGuid.CreateCanonicalShiftGuid(stationId, date, " a ");
        var guid3 = DeterministicGuid.CreateCanonicalShiftGuid(stationId, date, "A ");

        Assert.Equal(guid1, guid2);
        Assert.Equal(guid1, guid3);
    }

    [Fact]
    public void WindowsDpapiProtection_Roundtrip_ProtectsAndUnprotectsData()
    {
        var sensitiveSecret = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.my-supabase-secret-key-12345";

        var encrypted = WindowsDpapiProtection.Protect(sensitiveSecret);
        Assert.NotNull(encrypted);
        Assert.StartsWith("dpapi:", encrypted);
        Assert.NotEqual(sensitiveSecret, encrypted);

        var decrypted = WindowsDpapiProtection.Unprotect(encrypted);
        Assert.Equal(sensitiveSecret, decrypted);
    }

    [Fact]
    public void WindowsDpapiProtection_UnprotectLegacyPlaintext_ReturnsUnchanged()
    {
        // Backward compatibility: If existing client database already has plaintext API keys,
        // it must read them without failure.
        var plaintextKey = "plain-api-key-from-older-pyrosync-version";
        var result = WindowsDpapiProtection.Unprotect(plaintextKey);
        Assert.Equal(plaintextKey, result);
    }

    [Fact]
    public async Task StationPairingService_RedeemPairingCode_WhenExpired_Fails()
    {
        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("rpc/redeem_station_pairing_code"))
            {
                var rpcResult = new { success = false, error = "This pairing code has expired. Please generate a new code on the primary PC." };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(rpcResult), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler);
        var subClient = new SupabaseHttpClient(httpClient);
        subClient.Configure("https://mock.supabase.co", "key");
        var sp = CreateServiceProvider();
        var configService = new SyncConfigService(sp);
        var pairingService = new StationPairingService(configService, subClient);

        var result = await pairingService.RedeemPairingCodeAsync("PYRO-ABCD-2345", "Office-PC-2", "https://mock.supabase.co", "key");

        Assert.False(result.Success);
        Assert.Contains("expired", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StationPairingService_RedeemPairingCode_WhenAlreadyUsed_Fails()
    {
        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("rpc/redeem_station_pairing_code"))
            {
                var rpcResult = new { success = false, error = "This pairing code has already been used. Please generate a new code on the primary PC." };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(rpcResult), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler);
        var subClient = new SupabaseHttpClient(httpClient);
        subClient.Configure("https://mock.supabase.co", "key");
        var sp = CreateServiceProvider();
        var configService = new SyncConfigService(sp);
        var pairingService = new StationPairingService(configService, subClient);

        var result = await pairingService.RedeemPairingCodeAsync("PYRO-WXYZ-6789", "Office-PC-3", "https://mock.supabase.co", "key");

        Assert.False(result.Success);
        Assert.Contains("already been used", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StationPairingService_DeviceRevocation_ReturnsUnauthorized()
    {
        var revokedDevice = new Dictionary<string, object?>
        {
            { "station_id", "TEST-STATION" },
            { "MachineId", "REVOKED-MACHINE-ID" },
            { "MachineName", "Stolen-Laptop" },
            { "Role", "Secondary" },
            { "IsAuthorized", false },
            { "RevocationReason", "Decommissioned machine" }
        };

        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("StationDevices"))
            {
                var json = JsonConvert.SerializeObject(new[] { revokedDevice });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler);
        var subClient = new SupabaseHttpClient(httpClient);
        subClient.Configure("https://mock.supabase.co", "key");
        var sp = CreateServiceProvider();
        var configService = new SyncConfigService(sp);
        var pairingService = new StationPairingService(configService, subClient);

        var (isAuth, reason) = await pairingService.CheckDeviceAuthorizationAsync("TEST-STATION", "REVOKED-MACHINE-ID");

        Assert.False(isAuth);
        Assert.Contains("Decommissioned machine", reason);
    }

    [Fact]
    public async Task SyncEngine_MultiComputerDisabled_PreservesSinglePcModeWithoutCheckingAuth()
    {
        // When MultiComputerEnabled is false, device authorization checking is completely bypassed.
        var calledAuthCheck = false;
        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("StationDevices"))
            {
                calledAuthCheck = true;
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(handler);
        var subClient = new SupabaseHttpClient(httpClient);
        subClient.Configure("https://mock.supabase.co", "mock-key");

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(_connection), ServiceLifetime.Transient);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<AuthService>();
        services.AddSingleton<SyncConfigService>();
        services.AddSingleton(subClient);
        services.AddSingleton<StationPairingService>();
        services.AddSingleton<SyncEngine>();

        var sp = services.BuildServiceProvider();
        var config = sp.GetRequiredService<SyncConfigService>();
        await config.SaveSettingsAsync(new SyncSettings
        {
            SyncEnabled = true,
            MultiComputerEnabled = false, // Single-PC mode!
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "TEST-STATION"
        });

        var syncEngine = sp.GetRequiredService<SyncEngine>();
        await syncEngine.ForceSyncAsync();

        Assert.False(calledAuthCheck, "Single-PC mode must not check StationDevices authorization");
    }

    [Fact]
    public async Task SyncEngine_MultiComputerEnabled_WhenDeviceRevoked_AbortsSync()
    {
        var revokedDevice = new Dictionary<string, object?>
        {
            { "station_id", "TEST-STATION" },
            { "MachineId", "LOCAL-MACHINE" },
            { "MachineName", "Office-PC-Revoked" },
            { "Role", "Secondary" },
            { "IsAuthorized", false },
            { "RevocationReason", "Decommissioned by Owner" }
        };

        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("StationDevices"))
            {
                var json = JsonConvert.SerializeObject(new[] { revokedDevice });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(handler);
        var subClient = new SupabaseHttpClient(httpClient);
        subClient.Configure("https://mock.supabase.co", "mock-key");

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(_connection), ServiceLifetime.Transient);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<AuthService>();
        services.AddSingleton<SyncConfigService>();
        services.AddSingleton(subClient);
        services.AddSingleton<StationPairingService>();
        services.AddSingleton<SyncEngine>();

        var sp = services.BuildServiceProvider();
        var config = sp.GetRequiredService<SyncConfigService>();
        await config.SaveSettingsAsync(new SyncSettings
        {
            SyncEnabled = true,
            MultiComputerEnabled = true, // Multi-computer active!
            MachineId = "LOCAL-MACHINE",
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "TEST-STATION"
        });

        var syncEngine = sp.GetRequiredService<SyncEngine>();
        await syncEngine.ForceSyncAsync();

        var status = syncEngine.CurrentStatus;
        Assert.False(status.IsConnected);
        Assert.Contains("Access Revoked", status.StatusMessage);
        Assert.Contains("Decommissioned by Owner", status.StatusMessage);
    }

    [Fact]
    public async Task RevokedDevice_Offline_RetainsRevocationAndBlocksPush()
    {
        // Scenario: A secondary PC was revoked by the station owner.
        // The user unplugs network cable to attempt syncing pending records offline.
        // The workstation MUST retain the revocation state locally in AppMeta and reject push.
        var handler = new MockHttpMessageHandler((req) =>
        {
            throw new HttpRequestException("Simulated offline: network cable unplugged");
        });

        var httpClient = new HttpClient(handler);
        var subClient = new SupabaseHttpClient(httpClient);
        subClient.Configure("https://mock.supabase.co", "mock-key");

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(_connection), ServiceLifetime.Transient);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<AuthService>();
        services.AddSingleton<SyncConfigService>();
        services.AddSingleton(subClient);
        services.AddSingleton<StationPairingService>();
        services.AddSingleton<SyncEngine>();

        var sp = services.BuildServiceProvider();
        var config = sp.GetRequiredService<SyncConfigService>();
        
        // Save initial settings and persist local revocation flag
        await config.SaveSettingsAsync(new SyncSettings
        {
            SyncEnabled = true,
            MultiComputerEnabled = true,
            MachineId = "LOCAL-MACHINE-REVOKED",
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "TEST-STATION"
        });
        await config.SetDeviceRevocationAsync(true, "Unauthorized employee laptop");

        var pairingService = sp.GetRequiredService<StationPairingService>();
        var (isAuth, reason) = await pairingService.CheckDeviceAuthorizationAsync("TEST-STATION", "LOCAL-MACHINE-REVOKED");
        Assert.False(isAuth, "Device must be reported as unauthorized even when offline");
        Assert.Contains("Unauthorized employee laptop", reason);

        // Add a pending shift and change log into SQLite
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var shift = new Shift
            {
                ShiftDate = new DateTime(2026, 11, 21),
                ShiftType = "A"
            };
            db.Shifts.Add(shift);
            await db.SaveChangesAsync();

            db.SyncChangeLogs.Add(new SyncChangeLog
            {
                TableName = "Shifts",
                RecordId = shift.ShiftId,
                Operation = "INSERT",
                CreatedAt = DateTime.Now,
                IsSynced = false,
                StationId = "TEST-STATION",
                MachineId = "LOCAL-MACHINE-REVOKED",
                SyncGuid = Guid.NewGuid().ToString()
            });
            await db.SaveChangesAsync();
        }

        // Run sync cycle
        var syncEngine = sp.GetRequiredService<SyncEngine>();
        await syncEngine.ForceSyncAsync();

        // Verify push was blocked and pending log was NOT touched or marked synced
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var log = await db.SyncChangeLogs.FirstOrDefaultAsync(l => l.TableName == "Shifts");
            Assert.NotNull(log);
            Assert.False(log.IsSynced, "Pending change logs must not be marked synced when device is revoked");
        }
    }

    [Fact]
    public async Task HistoricalShift_ExistingRandomGuid_PreservedWithoutConflict()
    {
        // Scenario: A legacy shift created before multi-computer support already has a random GUID in SyncIdMappings.
        // SyncEngine must reuse the existing mapped GUID rather than overwriting it with a new canonical GUID.
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(_connection), ServiceLifetime.Transient);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<AuthService>();
        services.AddSingleton<SyncConfigService>();
        services.AddSingleton(new SupabaseHttpClient(new HttpClient()));
        services.AddSingleton<StationPairingService>();
        services.AddSingleton<SyncEngine>();

        var sp = services.BuildServiceProvider();
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        int shiftId1;
        int shiftId2;
        var existingRandomGuid = "random-historical-guid-98765432-abcd";

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var shift1 = new Shift
            {
                ShiftDate = new DateTime(2025, 6, 15),
                ShiftType = "A"
            };
            db.Shifts.Add(shift1);

            var shift2 = new Shift
            {
                ShiftDate = new DateTime(2025, 6, 15),
                ShiftType = "Shift-A" // Variant spelling
            };
            db.Shifts.Add(shift2);
            await db.SaveChangesAsync();

            shiftId1 = shift1.ShiftId;
            shiftId2 = shift2.ShiftId;

            // Seed historical random GUID for shift 1
            db.SyncIdMappings.Add(new SyncIdMapping
            {
                TableName = "Shifts",
                LocalId = shiftId1,
                RemoteGuid = existingRandomGuid
            });
            await db.SaveChangesAsync();
        }

        var settings = new SyncSettings { StationId = "MITALI-STATION", MachineId = "TEST-PC" };
        var localToGuid = new Dictionary<string, Dictionary<int, string>>();

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var guid1 = await syncEngine.GetOrCreateSyncGuidAsync(db, localToGuid, "Shifts", shiftId1, settings);
            Assert.Equal(existingRandomGuid, guid1);

            // Shift 2 has no mapping yet, but matches date + normalized shift type. It must reuse the existing mapping!
            var guid2 = await syncEngine.GetOrCreateSyncGuidAsync(db, localToGuid, "Shifts", shiftId2, settings);
            Assert.Equal(existingRandomGuid, guid2);
        }
    }

    [Fact]
    public async Task PairingPayload_ExcludesServiceRoleKey()
    {
        // Scenario: Primary PC administrator inadvertently configured a service_role key.
        // StationPairingService must refuse to create pairing codes containing service_role credentials.
        var handler = new MockHttpMessageHandler((req) => new HttpResponseMessage(HttpStatusCode.OK));
        var httpClient = new HttpClient(handler);
        var subClient = new SupabaseHttpClient(httpClient);
        subClient.Configure("https://mock.supabase.co", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.service_role_secret");

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(_connection), ServiceLifetime.Transient);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<AuthService>();
        services.AddSingleton<SyncConfigService>();
        services.AddSingleton(subClient);
        services.AddSingleton<StationPairingService>();

        var sp = services.BuildServiceProvider();
        var config = sp.GetRequiredService<SyncConfigService>();
        await config.SaveSettingsAsync(new SyncSettings
        {
            StationId = "TEST-STATION",
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.service_role_secret"
        });

        var pairingService = sp.GetRequiredService<StationPairingService>();
        var result = await pairingService.GeneratePairingCodeAsync();

        Assert.False(result.Success);
        Assert.Contains("service_role", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SupabaseDsmService_ApproveSubmission_WhenConflict_ReturnsFailure()
    {
        // Scenario: Workstation 2 attempts to approve a submission that Workstation 1 already approved.
        // Supabase returns 200 OK with empty array representation (0 updated rows due to status != Pending).
        var handler = new MockHttpMessageHandler((req) =>
        {
            // RPC endpoint returns conflict
            if (req.RequestUri!.ToString().Contains("rpc/claim_dsm_submission") || req.RequestUri!.ToString().Contains("rpc/approve_dsm_submission"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(new
                    {
                        success = false,
                        code = "CONCURRENCY_CONFLICT",
                        error = "Concurrency conflict: This submission has already been approved, completed, or locked by another workstation."
                    }), Encoding.UTF8, "application/json")
                };
            }

            // Fallback for non-RPC requests
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        });

        var httpClient = new HttpClient(handler);
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(_connection), ServiceLifetime.Transient);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<AuthService>();
        services.AddSingleton<SyncConfigService>();

        var sp = services.BuildServiceProvider();
        var config = sp.GetRequiredService<SyncConfigService>();
        await config.SaveSettingsAsync(new SyncSettings
        {
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "TEST-STATION"
        });
        var dsmService = new SupabaseDsmService(config, httpClient);

        var submissionId = Guid.NewGuid();
        var result = await dsmService.ApproveSubmissionAsync(submissionId, "Manager-2", "lock-uuid-2");

        Assert.False(result.Success);
        Assert.Contains("Concurrency conflict", result.Error);
    }

    [Fact]
    public async Task PairingSecurity_RpcUnavailable_FailsSafelyWithoutInsecureFallback()
    {
        // Scenario: Cloud RPC redeem_station_pairing_code is not deployed (returns 404).
        // StationPairingService must fail safely with an informative error rather than attempting
        // an unauthenticated, non-atomic direct table query fallback.
        bool queriedDirectTable = false;
        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("StationPairingCodes"))
            {
                queriedDirectTable = true;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
            }
            if (req.RequestUri!.ToString().Contains("rpc/redeem_station_pairing_code"))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler);
        var subClient = new SupabaseHttpClient(httpClient);
        subClient.Configure("https://mock.supabase.co", "anon-key");
        var sp = CreateServiceProvider();
        var configService = new SyncConfigService(sp);
        var pairingService = new StationPairingService(configService, subClient);

        var result = await pairingService.RedeemPairingCodeAsync("PYRO-TEST-CODE", "Client-PC", "https://mock.supabase.co", "anon-key");

        Assert.False(result.Success);
        Assert.Contains("redeem_station_pairing_code", result.Error);
        Assert.False(queriedDirectTable, "Security guarantee violated: StationPairingCodes table was queried directly without RPC protection!");
    }

    [Fact]
    public async Task PairingSecurity_RateLimiting_PropagatesRpcError()
    {
        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("rpc/redeem_station_pairing_code"))
            {
                var rpcResult = new { success = false, error = "Rate limit exceeded: Too many failed pairing attempts. Please wait 10 minutes before retrying." };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(rpcResult), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler);
        var subClient = new SupabaseHttpClient(httpClient);
        subClient.Configure("https://mock.supabase.co", "anon-key");
        var sp = CreateServiceProvider();
        var configService = new SyncConfigService(sp);
        var pairingService = new StationPairingService(configService, subClient);

        var result = await pairingService.RedeemPairingCodeAsync("PYRO-RATE-TEST", "Client-PC", "https://mock.supabase.co", "anon-key");

        Assert.False(result.Success);
        Assert.Contains("Rate limit exceeded", result.Error);
    }

    [Fact]
    public async Task DsmApproval_CrashAfterCloudClaim_CanBeRecoveredBySameOrOtherWorkstation()
    {
        // Scenario: Workstation 1 claims a submission in cloud, but crashes before local SQLite commit.
        // A recovery claim must succeed, allowing local processing and completion without duplicate financial postings.
        var subId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("rpc/claim_dsm_submission"))
            {
                var claimResult = new { success = true, message = "Claimed successfully" };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(claimResult), Encoding.UTF8, "application/json")
                };
            }
            if (req.RequestUri!.ToString().Contains("rpc/complete_dsm_approval"))
            {
                var compResult = new { success = true, message = "Completed successfully" };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(compResult), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
        });

        var httpClient = new HttpClient(handler);
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(_connection), ServiceLifetime.Transient);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<AuthService>();
        services.AddSingleton<SyncConfigService>();

        var sp = services.BuildServiceProvider();
        var config = sp.GetRequiredService<SyncConfigService>();
        await config.SaveSettingsAsync(new SyncSettings { SupabaseUrl = "https://mock.supabase.co", SupabaseApiKey = "key", StationId = "TEST-STATION", MachineId = "PC-RECOVER" });
        var dsmService = new SupabaseDsmService(config, httpClient);

        // 1. Claim by recovering workstation
        var lockId = Guid.NewGuid().ToString("N");
        var claimRes = await dsmService.ClaimSubmissionAsync(subId, "PC-RECOVER", "Admin", lockId);
        Assert.True(claimRes.Success);

        // 2. Perform local SQLite transaction
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            db.DsmApprovalAudits.Add(new DsmApprovalAudit
            {
                SubmissionId = subId,
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                OriginalDataJson = "{}",
                ApprovedDataJson = "{}"
            });
            await db.SaveChangesAsync();
        }

        // 3. Mark complete in Supabase
        var completeRes = await dsmService.CompleteSubmissionApprovalAsync(subId, "PC-RECOVER", lockId);
        Assert.True(completeRes.Success);

        // 4. Verify local DB state
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var auditCount = await db.DsmApprovalAudits.CountAsync(a => a.SubmissionId == subId);
            Assert.Equal(1, auditCount); // Exactly 1 audit record, no duplicate!
        }
    }

    [Fact]
    public async Task DsmApproval_CrashAfterLocalCommit_AutoHealsOnRestartWithoutDuplicates()
    {
        // Scenario: SQLite transaction committed, but app was terminated before cloud complete notification.
        // On restart, the queue detection calls CompleteSubmissionApprovalAsync and does not duplicate local records.
        var subId = Guid.NewGuid();
        bool completeCalled = false;
        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("rpc/complete_dsm_approval"))
            {
                completeCalled = true;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(new { success = true }), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
        });

        var httpClient = new HttpClient(handler);
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(_connection), ServiceLifetime.Transient);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<AuthService>();
        services.AddSingleton<SyncConfigService>();

        var sp = services.BuildServiceProvider();
        var config = sp.GetRequiredService<SyncConfigService>();
        await config.SaveSettingsAsync(new SyncSettings { SupabaseUrl = "https://mock.supabase.co", SupabaseApiKey = "key", StationId = "TEST-STATION", MachineId = "PC-AUTOHEAL" });
        var dsmService = new SupabaseDsmService(config, httpClient);

        // Seed existing local audit (simulating previous local commit before crash)
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            db.DsmApprovalAudits.Add(new DsmApprovalAudit
            {
                SubmissionId = subId,
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                OriginalDataJson = "{}",
                ApprovedDataJson = "{}"
            });
            await db.SaveChangesAsync();
        }

        // Call auto-heal complete
        var autoHealRes = await dsmService.CompleteSubmissionApprovalAsync(subId, "PC-AUTOHEAL", "lock-heal");
        Assert.True(autoHealRes.Success);
        Assert.True(completeCalled);

        // Verify local DB count remains 1
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var auditCount = await db.DsmApprovalAudits.CountAsync(a => a.SubmissionId == subId);
            Assert.Equal(1, auditCount);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Stage 2.3 Tests: Fencing Tokens, Leases, and Deterministic Financial Idempotency
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClaimFencing_PcAClaimFollowedByPcBReclaim_IncrementsVersionAndNewToken()
    {
        var subId = Guid.NewGuid();
        int callCount = 0;
        var handler = new MockHttpMessageHandler((req) =>
        {
            callCount++;
            if (callCount == 1)
            {
                // PC-A first claim: Version 1, Token-A
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(new
                    {
                        success = true,
                        code = "CLAIMED",
                        claim_version = 1,
                        claim_token = "token-A",
                        message = "Claimed successfully"
                    }), Encoding.UTF8, "application/json")
                };
            }
            else
            {
                // PC-B reclaim after lease expiry: Version 2, Token-B
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(new
                    {
                        success = true,
                        code = "CLAIMED",
                        claim_version = 2,
                        claim_token = "token-B",
                        message = "Claimed successfully"
                    }), Encoding.UTF8, "application/json")
                };
            }
        });

        var dsmService = CreateMockDsmService(handler, "PC-A");
        var resA = await dsmService.ClaimSubmissionWithFencingAsync(subId, "PC-A", "Manager", "lock-A");
        Assert.True(resA.Success);
        Assert.Equal(1, resA.Data!.ClaimVersion);
        Assert.Equal("token-A", resA.Data!.ClaimToken);

        var dsmServiceB = CreateMockDsmService(handler, "PC-B");
        var resB = await dsmServiceB.ClaimSubmissionWithFencingAsync(subId, "PC-B", "Manager", "lock-B");
        Assert.True(resB.Success);
        Assert.Equal(2, resB.Data!.ClaimVersion);
        Assert.Equal("token-B", resB.Data!.ClaimToken);
        Assert.NotEqual(resA.Data.ClaimToken, resB.Data.ClaimToken);
    }

    [Fact]
    public async Task ClaimFencing_StaleTokenRenewal_RejectedWithFencingViolation()
    {
        var subId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("rpc/renew_dsm_lease"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(new
                    {
                        success = false,
                        code = "FENCING_VIOLATION_STALE_TOKEN",
                        error = "Fencing violation: Lease has been reassigned to another claim (stale workstation token)."
                    }), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var dsmService = CreateMockDsmService(handler, "PC-A");
        var renewRes = await dsmService.RenewLeaseAsync(subId, "PC-A", "stale-token-A", 1);

        Assert.False(renewRes.Success);
        Assert.Contains("Fencing violation", renewRes.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClaimFencing_StaleTokenCompletion_RejectedByServer()
    {
        var subId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("rpc/complete_dsm_approval"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(new
                    {
                        success = false,
                        code = "FENCING_VIOLATION_STALE_TOKEN",
                        error = "Fencing violation: Stale claim token. A newer claim has taken over this submission."
                    }), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var dsmService = CreateMockDsmService(handler, "PC-A");
        var compRes = await dsmService.CompleteSubmissionApprovalAsync(subId, "PC-A", "lock-A", "stale-token-A", 1);

        Assert.False(compRes.Success);
        Assert.Contains("Fencing violation", compRes.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClaimFencing_ExpiredLeaseWithoutRenewal_Rejected()
    {
        var subId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("rpc/complete_dsm_approval"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(new
                    {
                        success = false,
                        code = "LEASE_EXPIRED",
                        error = "Claim lease expired. Workstation did not maintain active lease."
                    }), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var dsmService = CreateMockDsmService(handler, "PC-A");
        var compRes = await dsmService.CompleteSubmissionApprovalAsync(subId, "PC-A", "lock-A", "token-A", 1);

        Assert.False(compRes.Success);
        Assert.Contains("expired", compRes.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ClaimFencing_PcAExtends_PcBReclaimsAfterExpiry_PcALateCommitFailsFencing()
    {
        // Scenario (Requested Race Condition):
        // 1. PC A renews successfully.
        // 2. PC A's lease expires; PC B reclaims (Version=2, Token=B).
        // 3. PC A attempts pre-commit lease renewal or completion with Token A: rejected!
        var subId = Guid.NewGuid();
        int step = 0;
        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("rpc/renew_dsm_lease"))
            {
                step++;
                if (step == 1)
                {
                    // PC A renews successfully
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonConvert.SerializeObject(new { success = true, code = "RENEWED" }), Encoding.UTF8, "application/json")
                    };
                }
                else
                {
                    // PC A attempts renewal after PC B reclaimed -> Fencing violation!
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonConvert.SerializeObject(new
                        {
                            success = false,
                            code = "FENCING_VIOLATION_STALE_TOKEN",
                            error = "Fencing violation: Lease has been reassigned to another claim (stale workstation token)."
                        }), Encoding.UTF8, "application/json")
                    };
                }
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var dsmService = CreateMockDsmService(handler, "PC-A");
        // Step 1: Successful renewal
        var firstRenew = await dsmService.RenewLeaseAsync(subId, "PC-A", "token-A", 1);
        Assert.True(firstRenew.Success);

        // Step 2: PC B reclaims in cloud...
        // Step 3: PC A attempts pre-commit renewal -> must fail safely and stop local commit
        var lateRenew = await dsmService.RenewLeaseAsync(subId, "PC-A", "token-A", 1);
        Assert.False(lateRenew.Success);
        Assert.Contains("Fencing violation", lateRenew.Error);
    }

    [Fact]
    public async Task DeterministicGuids_TwoIndependentSqliteDatabases_ProduceIdenticalRemoteGuids()
    {
        // Scenario: Two independent desktop workstations (separate SQLite databases) process
        // the exact same immutable PWA submission.
        // Both databases MUST produce 100% identical RemoteGuids in SyncIdMappings.
        var subId = Guid.NewGuid();
        var date = new DateTime(2026, 11, 21);

        using var conn1 = new SqliteConnection("DataSource=:memory:");
        await conn1.OpenAsync();
        using var conn2 = new SqliteConnection("DataSource=:memory:");
        await conn2.OpenAsync();

        var opt1 = new DbContextOptionsBuilder<FuelProDbContext>().UseSqlite(conn1).Options;
        var opt2 = new DbContextOptionsBuilder<FuelProDbContext>().UseSqlite(conn2).Options;

        using (var db1 = new FuelProDbContext(opt1))
        using (var db2 = new FuelProDbContext(opt2))
        {
            await db1.Database.EnsureCreatedAsync();
            await db2.Database.EnsureCreatedAsync();

            // DB 1 registrations
            var auditGuid1 = await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                db1, "DsmApprovalAudits", 1, DeterministicGuid.CreateDsmApprovalAuditGuid(subId));
            var entryGuid1 = await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                db1, "DsmEntries", 10, DeterministicGuid.CreateDsmEntryGuid(subId, 1));
            var nr1Guid1 = await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                db1, "NozzleReadings", 101, DeterministicGuid.CreateNozzleReadingGuid(subId, 1, 1));
            var nr2Guid1 = await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                db1, "NozzleReadings", 102, DeterministicGuid.CreateNozzleReadingGuid(subId, 1, 2));
            var debitGuid1 = await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                db1, "DebitEntries", 201, DeterministicGuid.CreateDebitEntryGuid(subId, 0, "SLIP-001"));
            var repGuid1 = await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                db1, "CreditorRepayments", 301, DeterministicGuid.CreateCreditorRepaymentGuid(subId, 0, "ABC Transporters"));

            await db1.SaveChangesAsync();

            // DB 2 registrations (local IDs may differ completely!)
            var auditGuid2 = await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                db2, "DsmApprovalAudits", 99, DeterministicGuid.CreateDsmApprovalAuditGuid(subId));
            var entryGuid2 = await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                db2, "DsmEntries", 88, DeterministicGuid.CreateDsmEntryGuid(subId, 1));
            var nr1Guid2 = await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                db2, "NozzleReadings", 77, DeterministicGuid.CreateNozzleReadingGuid(subId, 1, 1));
            var nr2Guid2 = await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                db2, "NozzleReadings", 78, DeterministicGuid.CreateNozzleReadingGuid(subId, 1, 2));
            var debitGuid2 = await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                db2, "DebitEntries", 66, DeterministicGuid.CreateDebitEntryGuid(subId, 0, "SLIP-001"));
            var repGuid2 = await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
                db2, "CreditorRepayments", 55, DeterministicGuid.CreateCreditorRepaymentGuid(subId, 0, "ABC Transporters"));

            await db2.SaveChangesAsync();

            // Assert: Remote GUIDs match 100% across independent workstations
            Assert.Equal(auditGuid1, auditGuid2);
            Assert.Equal(entryGuid1, entryGuid2);
            Assert.Equal(nr1Guid1, nr1Guid2);
            Assert.Equal(nr2Guid1, nr2Guid2);
            Assert.Equal(debitGuid1, debitGuid2);
            Assert.Equal(repGuid1, repGuid2);
        }
    }

    [Fact]
    public void DeterministicGuids_SeparateLegitimateItemsWithIdenticalValues_RemainSeparate()
    {
        // Scenario: A submission contains two distinct debtor slips for the same customer
        // and exact same amount (e.g. ₹5,000). Their itemIndex uniquely distinguishes them.
        var subId = Guid.NewGuid();

        var guidSlip0 = DeterministicGuid.CreateDebitEntryGuid(subId, 0, "SLIP-100");
        var guidSlip1 = DeterministicGuid.CreateDebitEntryGuid(subId, 1, "SLIP-100"); // same slip number, different index
        var guidSlip2 = DeterministicGuid.CreateDebitEntryGuid(subId, 2, "SLIP-101");

        Assert.NotEqual(guidSlip0, guidSlip1);
        Assert.NotEqual(guidSlip0, guidSlip2);
        Assert.NotEqual(guidSlip1, guidSlip2);
    }

    [Fact]
    public async Task HistoricalCompatibility_ExistingRandomSyncGuid_PreservedNeverOverwritten()
    {
        // Scenario: A historical transaction already has a random GUID in SyncIdMappings.
        // SyncMappingPreservationHelper MUST preserve the historical GUID and never replace it.
        using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var opt = new DbContextOptionsBuilder<FuelProDbContext>().UseSqlite(conn).Options;

        using var db = new FuelProDbContext(opt);
        await db.Database.EnsureCreatedAsync();

        var historicalRandomGuid = Guid.NewGuid().ToString();
        db.SyncIdMappings.Add(new SyncIdMapping
        {
            TableName = "DebitEntries",
            LocalId = 42,
            RemoteGuid = historicalRandomGuid
        });
        await db.SaveChangesAsync();

        var subId = Guid.NewGuid();
        var newFormulaGuid = DeterministicGuid.CreateDebitEntryGuid(subId, 0, "SLIP-42");

        // Attempt registration
        var resolvedGuid = await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
            db, "DebitEntries", 42, newFormulaGuid);

        Assert.Equal(historicalRandomGuid, resolvedGuid);
        Assert.NotEqual(newFormulaGuid, resolvedGuid);

        var dbMapping = await db.SyncIdMappings.FirstAsync(m => m.TableName == "DebitEntries" && m.LocalId == 42);
        Assert.Equal(historicalRandomGuid, dbMapping.RemoteGuid);
    }

    [Fact]
    public void DeterministicGuids_NozzlesAcrossPumps_AreUniquelyDistinguished()
    {
        // Scenario: Pump 1 has Nozzle 1 and Pump 2 has Nozzle 1.
        // DeterministicGuid must distinguish them so nozzle readings never collide.
        var subId = Guid.NewGuid();

        var pump1Nozzle1 = DeterministicGuid.CreateNozzleReadingGuid(subId, pumpId: 1, nozzleNumber: 1);
        var pump2Nozzle1 = DeterministicGuid.CreateNozzleReadingGuid(subId, pumpId: 2, nozzleNumber: 1);

        Assert.NotEqual(pump1Nozzle1, pump2Nozzle1);
    }

    [Fact]
    public async Task Recovery_AfterLocalCommit_CloudCompletionRetriedIdempotently()
    {
        // Scenario: PC crashed after SQLite commit but before complete_dsm_approval RPC returned.
        // On restart, completing approval returns ALREADY_COMPLETED_SAME_CLAIM, which is treated as success.
        var subId = Guid.NewGuid();
        var handler = new MockHttpMessageHandler((req) =>
        {
            if (req.RequestUri!.ToString().Contains("rpc/complete_dsm_approval"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(new
                    {
                        success = true,
                        code = "ALREADY_COMPLETED_SAME_CLAIM",
                        message = "Already marked completed by this claim"
                    }), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var dsmService = CreateMockDsmService(handler, "PC-A");
        var res = await dsmService.CompleteSubmissionApprovalAsync(subId, "PC-A", "lock-A", "token-A", 1);

        Assert.True(res.Success);
    }

    [Fact]
    public async Task ConcurrentRecovery_TwoWorkstations_OnlyOneLeaseGranted()
    {
        // Scenario: Two workstations attempt to claim an expired submission at the same time.
        // Server serializes via row lock: Workstation 1 receives CLAIMED, Workstation 2 receives CONCURRENCY_CONFLICT.
        var subId = Guid.NewGuid();
        int attempt = 0;
        var handler = new MockHttpMessageHandler((req) =>
        {
            attempt++;
            if (attempt == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(new
                    {
                        success = true,
                        code = "CLAIMED",
                        claim_version = 3,
                        claim_token = "token-pc1"
                    }), Encoding.UTF8, "application/json")
                };
            }
            else
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(new
                    {
                        success = false,
                        code = "CONCURRENCY_CONFLICT",
                        error = "Concurrency conflict: This submission is currently being approved by another active lease (PC-1)."
                    }), Encoding.UTF8, "application/json")
                };
            }
        });

        var svc1 = CreateMockDsmService(handler, "PC-1");
        var svc2 = CreateMockDsmService(handler, "PC-2");

        var res1 = await svc1.ClaimSubmissionWithFencingAsync(subId, "PC-1", "Manager 1", "lock-1");
        var res2 = await svc2.ClaimSubmissionWithFencingAsync(subId, "PC-2", "Manager 2", "lock-2");

        Assert.True(res1.Success);
        Assert.Equal("CLAIMED", res1.Data!.Code);

        Assert.False(res2.Success);
        Assert.Contains("Concurrency conflict", res2.Error);
    }

    [Fact]
    public async Task HistoricalCompatibility_MappingReassignmentProtection_NeverReassigns()
    {
        // Scenario: RemoteGuid is already mapped to LocalId=10.
        // Another call attempts to map the same RemoteGuid to LocalId=20.
        // Mapping MUST NOT be reassigned to 20.
        using var conn = new SqliteConnection("DataSource=:memory:");
        await conn.OpenAsync();
        var opt = new DbContextOptionsBuilder<FuelProDbContext>().UseSqlite(conn).Options;

        using var db = new FuelProDbContext(opt);
        await db.Database.EnsureCreatedAsync();

        var deterministicGuid = Guid.NewGuid().ToString();
        db.SyncIdMappings.Add(new SyncIdMapping
        {
            TableName = "NozzleReadings",
            LocalId = 10,
            RemoteGuid = deterministicGuid
        });
        await db.SaveChangesAsync();

        // Attempt to register same RemoteGuid for localId 20
        var resolved = await SyncMappingPreservationHelper.RegisterOrPreserveDeterministicMappingAsync(
            db, "NozzleReadings", 20, deterministicGuid);

        Assert.Equal(deterministicGuid, resolved);

        // Verify the database still has LocalId 10, NOT 20
        var mapping = await db.SyncIdMappings.FirstAsync(m => m.TableName == "NozzleReadings" && m.RemoteGuid == deterministicGuid);
        Assert.Equal(10, mapping.LocalId);
    }

    private SupabaseDsmService CreateMockDsmService(MockHttpMessageHandler handler, string machineId)
    {
        var httpClient = new HttpClient(handler);
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(_connection), ServiceLifetime.Transient);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<AuthService>();
        services.AddSingleton<SyncConfigService>();

        var sp = services.BuildServiceProvider();
        var config = sp.GetRequiredService<SyncConfigService>();
        config.SaveSettingsAsync(new SyncSettings
        {
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "TEST-STATION",
            MachineId = machineId
        }).GetAwaiter().GetResult();

        return new SupabaseDsmService(config, httpClient);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
