using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FuelPro.Tests;

public class CloudSyncFixesTests : IDisposable
{
    private readonly string _tempDbPath;
    private readonly DbContextOptions<FuelProDbContext> _options;

    public CloudSyncFixesTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _tempDbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fuelpro_sync_fix_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={_tempDbPath}";

        _options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite(connStr)
            .Options;

        using var context = new FuelProDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (System.IO.File.Exists(_tempDbPath)) System.IO.File.Delete(_tempDbPath); } catch { }
    }

    private ServiceProvider BuildServices(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var supabaseClient = new SupabaseHttpClient(httpClient);
        supabaseClient.Configure("https://mock.supabase.co", "anon-client-key");

        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite($"Data Source={_tempDbPath}"), ServiceLifetime.Transient);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<AuthService>();
        services.AddSingleton<IFeatureToggleService, FeatureToggleService>();
        services.AddSingleton<SyncConfigService>();
        services.AddSingleton(supabaseClient);
        services.AddSingleton<StationPairingService>();
        services.AddSingleton<SyncEngine>();
        return services.BuildServiceProvider();
    }

    private sealed class CapturingHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, Task<HttpResponseMessage>> _handler;
        public CapturingHttpHandler(Func<HttpRequestMessage, string?, Task<HttpResponseMessage>> handler)
            => _handler = handler;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
            return await _handler(request, body);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ISSUE 1 — Settings.local_id, SyncGuid Reconciliation & Feature Sync
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PushStationSnapshot_FreshInstall_IncludesValidLocalIdSettingIdAndReusesSyncGuid()
    {
        var remoteSettings = new Dictionary<string, JObject>();
        var capturedSnapshotPayloads = new List<JObject>();

        var handler = new CapturingHttpHandler((req, body) =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Get && path.Contains("/rest/v1/Settings"))
            {
                var arr = new JArray(remoteSettings.Values);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(arr.ToString(), Encoding.UTF8, "application/json")
                });
            }

            if (req.Method == HttpMethod.Post && path.Contains("/rest/v1/Settings") && !string.IsNullOrEmpty(body))
            {
                if (body.TrimStart().StartsWith("["))
                {
                    var items = JArray.Parse(body);
                    foreach (JObject item in items)
                    {
                        var guid = item["SyncGuid"]!.ToString();
                        remoteSettings[guid] = item;
                    }
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json")
                    });
                }

                var obj = JObject.Parse(body);
                capturedSnapshotPayloads.Add(obj);
                var sg = obj["SyncGuid"]!.ToString();
                remoteSettings[sg] = obj;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new JArray(obj).ToString(), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        });

        using var sp = BuildServices(handler);

        int seededSettingId;
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var s = new Setting
            {
                PumpStationName = "Fresh Pump",
                HsdRate = 91.0,
                MsIRate = 104.0,
                MsIIRate = 104.0,
                LastUpdated = DateTime.Now
            };
            db.Settings.Add(s);
            await db.SaveChangesAsync();
            seededSettingId = s.SettingId;
        }

        var featureSvc = sp.GetRequiredService<IFeatureToggleService>();
        var features = await featureSvc.GetAllFeaturesAsync();
        Assert.NotEmpty(features);

        var configSvc = sp.GetRequiredService<SyncConfigService>();
        await configSvc.SaveSettingsAsync(new SyncSettings
        {
            SyncEnabled = true,
            MultiComputerEnabled = false,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "anon-client-key",
            StationId = "STATION-FRESH-01",
            MachineId = "PC-PRIMARY"
        });

        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var firstSyncedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void FirstSyncHandler(SyncStatusInfo s)
        {
            if (s.IsConnected && s.StatusMessage == "Synced")
                firstSyncedTcs.TrySetResult(true);
        }
        syncEngine.SyncStatusChanged += FirstSyncHandler;

        // 1. Snapshot push on fresh DB
        var first = await syncEngine.PushStationSnapshotToCloudAsync("STATION-FRESH-01");
        Assert.True(first.Success, first.Message);
        Assert.Single(capturedSnapshotPayloads);

        var p1 = capturedSnapshotPayloads[0];
        Assert.NotNull(p1["local_id"]);
        Assert.Equal(seededSettingId, p1["local_id"]!.Value<int>());
        Assert.Equal(seededSettingId, p1["SettingId"]!.Value<int>());
        Assert.Equal("Fresh Pump", p1["PumpStationName"]!.ToString());
        Assert.False(string.IsNullOrWhiteSpace(p1["LastUpdated"]?.ToString()));
        Assert.False(string.IsNullOrWhiteSpace(p1["AppFeatureSettingsJson"]?.ToString()));
        var guid1 = p1["SyncGuid"]!.ToString();
        Assert.True(Guid.TryParse(guid1, out _));

        // Wait deterministically for background ForceSyncAsync triggered by PushStationSnapshotToCloudAsync
        await Task.WhenAny(firstSyncedTcs.Task, Task.Delay(5000));
        syncEngine.SyncStatusChanged -= FirstSyncHandler;

        // 2. Normal ForceSyncAsync pushes pending Settings change log using the exact same SyncGuid
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            db.SyncChangeLogs.Add(new SyncChangeLog
            {
                TableName = "Settings",
                RecordId = seededSettingId,
                Operation = "UPDATE",
                CreatedAt = DateTime.Now,
                IsSynced = false,
                StationId = "STATION-FRESH-01",
                MachineId = "PC-PRIMARY",
                SyncGuid = Guid.NewGuid().ToString()
            });
            await db.SaveChangesAsync();
        }

        await syncEngine.ForceSyncAsync();

        // Still only ONE Settings row in remote cloud (no duplicate created)
        Assert.Single(remoteSettings);
        Assert.True(remoteSettings.ContainsKey(guid1));
        Assert.Equal(seededSettingId, remoteSettings[guid1]["local_id"]!.Value<int>());

        // 3. Second snapshot push uses identical SyncGuid and local_id
        var second = await syncEngine.PushStationSnapshotToCloudAsync("STATION-FRESH-01");
        Assert.True(second.Success, second.Message);
        Assert.Equal(guid1, capturedSnapshotPayloads[1]["SyncGuid"]!.ToString());
        Assert.Equal(seededSettingId, capturedSnapshotPayloads[1]["local_id"]!.Value<int>());
        Assert.Single(remoteSettings);
    }

    [Fact]
    public async Task PushStationSnapshot_ExistingRemoteStation_PreservesRemoteSyncGuidAndRemoteLocalId()
    {
        var remoteExistingGuid = "11111111-2222-3333-4444-555555555555";
        const int remoteExistingLocalId = 77;
        JObject? postedSnapshot = null;

        var handler = new CapturingHttpHandler((req, body) =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Get && path.Contains("/rest/v1/Settings"))
            {
                var existing = new JArray(new JObject
                {
                    ["SyncGuid"] = remoteExistingGuid,
                    ["station_id"] = "STATION-EXISTING",
                    ["local_id"] = remoteExistingLocalId,
                    ["SettingId"] = remoteExistingLocalId,
                    ["PumpStationName"] = "Existing Station"
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(existing.ToString(), Encoding.UTF8, "application/json")
                });
            }

            if (req.Method == HttpMethod.Post && path.Contains("/rest/v1/Settings") && !string.IsNullOrEmpty(body))
            {
                postedSnapshot = JObject.Parse(body);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new JArray(postedSnapshot).ToString(), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        });

        using var sp = BuildServices(handler);

        int localSettingId;
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var s = new Setting { PumpStationName = "Secondary Local", LastUpdated = DateTime.Now };
            db.Settings.Add(s);
            await db.SaveChangesAsync();
            localSettingId = s.SettingId;

            // Seed a stale different local SyncIdMapping to confirm reconciliation
            db.SyncIdMappings.Add(new SyncIdMapping
            {
                TableName = "Settings",
                LocalId = localSettingId,
                RemoteGuid = "99999999-9999-9999-9999-999999999999"
            });
            await db.SaveChangesAsync();
        }

        var configSvc = sp.GetRequiredService<SyncConfigService>();
        await configSvc.SaveSettingsAsync(new SyncSettings
        {
            SyncEnabled = false,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "anon-client-key",
            StationId = "STATION-EXISTING",
            MachineId = "PC-SECONDARY"
        });

        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var result = await syncEngine.PushStationSnapshotToCloudAsync("STATION-EXISTING");

        Assert.True(result.Success, result.Message);
        Assert.NotNull(postedSnapshot);
        Assert.Equal(remoteExistingGuid, postedSnapshot!["SyncGuid"]!.ToString());
        Assert.Equal(remoteExistingLocalId, postedSnapshot["local_id"]!.Value<int>());
        Assert.Equal(remoteExistingLocalId, postedSnapshot["SettingId"]!.Value<int>());

        // Verify local SyncIdMapping was reconciled to remoteSyncGuid so PerformPushAsync won't duplicate
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var mapping = await db.SyncIdMappings.FirstOrDefaultAsync(m => m.TableName == "Settings" && m.LocalId == localSettingId);
            Assert.NotNull(mapping);
            Assert.Equal(remoteExistingGuid, mapping!.RemoteGuid);
        }
    }

    [Fact]
    public async Task PushStationSnapshot_ZeroRowPatchFallback_DoesNotReportFalseSuccess_AndEscapesStationId()
    {
        var observedGetPaths = new List<string>();
        var observedPatchPaths = new List<string>();
        bool patchSawReturnRepresentation = false;

        var handler = new CapturingHttpHandler((req, body) =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Get && path.Contains("/rest/v1/Settings"))
            {
                observedGetPaths.Add(path);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
            }

            if (req.Method == HttpMethod.Patch && path.Contains("/rest/v1/Settings"))
            {
                observedPatchPaths.Add(path);
                patchSawReturnRepresentation = req.Headers.TryGetValues("Prefer", out var vals) &&
                    vals.Any(v => v.Contains("return=representation", StringComparison.OrdinalIgnoreCase));

                // PostgREST returns 200 OK with empty array "[]" when 0 rows match PATCH filter
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
            }

            if (req.Method == HttpMethod.Post && path.Contains("/rest/v1/Settings"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"code\":\"42P10\",\"message\":\"no unique constraint\"}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        });

        using var sp = BuildServices(handler);
        var configSvc = sp.GetRequiredService<SyncConfigService>();
        await configSvc.SaveSettingsAsync(new SyncSettings
        {
            SyncEnabled = false,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "anon-client-key",
            StationId = "STN+A&B",
            MachineId = "PC-1"
        });

        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var result = await syncEngine.PushStationSnapshotToCloudAsync("STN+A&B");

        Assert.False(result.Success, "Zero-row PATCH returning [] must never report snapshot push success.");
        Assert.True(patchSawReturnRepresentation, "PATCH fallback must request Prefer: return=representation.");
        Assert.Contains(observedGetPaths, p => p.Contains("station_id=eq.STN%2BA%26B"));
        Assert.Contains(observedPatchPaths, p => p.Contains("station_id=eq.STN%2BA%26B"));
    }

    [Fact]
    public async Task PushStationSnapshot_StationIsolation_UsesExplicitStationId()
    {
        var pushedStations = new List<string>();
        var handler = new CapturingHttpHandler((req, body) =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Post && path.Contains("/rest/v1/Settings") && !string.IsNullOrEmpty(body))
            {
                var obj = JObject.Parse(body);
                pushedStations.Add(obj["station_id"]!.ToString());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new JArray(obj).ToString(), Encoding.UTF8, "application/json")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        });

        using var sp = BuildServices(handler);
        var configSvc = sp.GetRequiredService<SyncConfigService>();
        await configSvc.SaveSettingsAsync(new SyncSettings
        {
            SyncEnabled = false,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "anon-client-key",
            StationId = "STATION-DEFAULT",
            MachineId = "PC-1"
        });

        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var res = await syncEngine.PushStationSnapshotToCloudAsync("STATION-TARGET-B");

        Assert.True(res.Success, res.Message);
        Assert.Equal(new[] { "STATION-TARGET-B" }, pushedStations);
    }

    [Fact]
    public async Task FeatureSync_CloudErrorDoesNotCrashStartupOrFeatureLoading()
    {
        var handler = new CapturingHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"code\":\"23502\",\"message\":\"null value in column \\\"local_id\\\"\"}", Encoding.UTF8, "application/json")
            }));

        using var sp = BuildServices(handler);
        var configSvc = sp.GetRequiredService<SyncConfigService>();
        await configSvc.SaveSettingsAsync(new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "anon-client-key",
            StationId = "STATION-ERR",
            MachineId = "PC-1"
        });

        var featureSvc = sp.GetRequiredService<IFeatureToggleService>();
        var features = await featureSvc.GetAllFeaturesAsync();
        Assert.NotEmpty(features);

        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var pushResult = await syncEngine.PushStationSnapshotToCloudAsync();
        Assert.False(pushResult.Success);

        // App continues functioning cleanly despite cloud error
        Assert.True(await featureSvc.SaveFeaturesAsync(features));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ISSUE 2 — Multi-Computer Pairing, PGRST205 Guidance & Validation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GeneratePairingCode_MissingCloudTablePgrst205_ReturnsActionableMigrationGuidance()
    {
        var handler = new CapturingHttpHandler((req, _) =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("StationPairingCodes"))
            {
                var pgrst205 = JsonConvert.SerializeObject(new
                {
                    code = "PGRST205",
                    message = "Could not find the table 'public.StationPairingCodes' in the schema cache",
                    hint = "Perhaps you meant the table 'public.AdditionalSettings'"
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(pgrst205, Encoding.UTF8, "application/json")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        });

        using var sp = BuildServices(handler);
        var configSvc = sp.GetRequiredService<SyncConfigService>();
        await configSvc.SaveSettingsAsync(new SyncSettings
        {
            StationId = "MITALI-01",
            MachineId = "PC-1",
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "anon-client-key",
            SyncEnabled = true,
            MultiComputerEnabled = true
        });

        var pairingSvc = sp.GetRequiredService<StationPairingService>();
        var result = await pairingSvc.GeneratePairingCodeAsync();

        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Contains("PGRST205", result.Error);
        Assert.Contains("StationPairingCodes", result.Error);
        Assert.Contains("20261121_multi_computer_tables.sql", result.Error);
        Assert.DoesNotContain("AdditionalSettings", result.Error);
    }

    [Fact]
    public async Task GeneratePairingCode_PrimaryDeviceRegistrationFails_DoesNotReturnPairingCode()
    {
        var handler = new CapturingHttpHandler((req, _) =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (path.Contains("StationPairingCodes"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
            }

            if (path.Contains("StationDevices"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"code\":\"PGRST205\",\"message\":\"Could not find the table 'public.StationDevices' in the schema cache\"}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        });

        using var sp = BuildServices(handler);
        var configSvc = sp.GetRequiredService<SyncConfigService>();
        await configSvc.SaveSettingsAsync(new SyncSettings
        {
            StationId = "MITALI-01",
            MachineId = "PC-PRIMARY",
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "anon-client-key",
            SyncEnabled = true,
            MultiComputerEnabled = true
        });

        var pairingSvc = sp.GetRequiredService<StationPairingService>();
        var result = await pairingSvc.GeneratePairingCodeAsync();

        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Contains("StationDevices", result.Error);
    }

    [Fact]
    public async Task GenerateAndRedeemPairingCode_EndToEndFlow_PublishesRedeemsRegistersAndIsolatesStation()
    {
        var storedCodes = new Dictionary<string, JObject>();
        var storedDevices = new List<JObject>();

        var handler = new CapturingHttpHandler((req, body) =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Post && path.Contains("StationPairingCodes") && !string.IsNullOrEmpty(body))
            {
                var arr = JArray.Parse(body);
                foreach (JObject rec in arr)
                {
                    storedCodes[rec["PairingCode"]!.ToString()] = rec;
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }

            if (req.Method == HttpMethod.Post && path.Contains("StationDevices") && !string.IsNullOrEmpty(body))
            {
                var arr = JArray.Parse(body);
                foreach (JObject dev in arr)
                {
                    storedDevices.RemoveAll(d =>
                        string.Equals(d["station_id"]?.ToString(), dev["station_id"]?.ToString(), StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(d["MachineId"]?.ToString(), dev["MachineId"]?.ToString(), StringComparison.OrdinalIgnoreCase));
                    storedDevices.Add(dev);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }

            if (req.Method == HttpMethod.Post && path.Contains("rpc/redeem_station_pairing_code") && !string.IsNullOrEmpty(body))
            {
                var args = JObject.Parse(body);
                var code = args["p_pairing_code"]!.ToString();
                if (!storedCodes.TryGetValue(code, out var rec))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(JsonConvert.SerializeObject(new { success = false, error = "Invalid pairing code." }))
                    });
                }

                var rpcOk = JsonConvert.SerializeObject(new
                {
                    success = true,
                    station_id = rec["station_id"]!.ToString(),
                    encrypted_credentials = rec["EncryptedCredentials"]!.ToString()
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(rpcOk, Encoding.UTF8, "application/json")
                });
            }

            if (req.Method == HttpMethod.Get && path.Contains("StationDevices"))
            {
                IEnumerable<JObject> filtered = storedDevices;
                var mIdx = path.IndexOf("MachineId=eq.", StringComparison.OrdinalIgnoreCase);
                if (mIdx >= 0)
                {
                    var rest = path.Substring(mIdx + "MachineId=eq.".Length);
                    var amp = rest.IndexOf('&');
                    var targetMachine = Uri.UnescapeDataString(amp >= 0 ? rest.Substring(0, amp) : rest);
                    filtered = filtered.Where(d => string.Equals(d["MachineId"]?.ToString(), targetMachine, StringComparison.OrdinalIgnoreCase));
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new JArray(filtered).ToString(), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        // 1. Primary publishes code
        using var spPrimary = BuildServices(handler);
        var primaryConfig = spPrimary.GetRequiredService<SyncConfigService>();
        await primaryConfig.SaveSettingsAsync(new SyncSettings
        {
            StationId = "STATION-PAIR-01",
            MachineId = "PC-PRIMARY",
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "anon-client-key",
            SyncEnabled = true,
            MultiComputerEnabled = true
        });

        var primaryPairing = spPrimary.GetRequiredService<StationPairingService>();
        var genRes = await primaryPairing.GeneratePairingCodeAsync();
        Assert.True(genRes.Success, genRes.Error);
        Assert.StartsWith("PYRO-", genRes.Data);
        Assert.Single(storedCodes);

        // 2. Secondary redeems code in its own SQLite DB
        var secDbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fuelpro_sec_{Guid.NewGuid():N}.db");
        try
        {
            var secServices = new ServiceCollection();
            secServices.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite($"Data Source={secDbPath}"), ServiceLifetime.Transient);
            secServices.AddSingleton<SyncConfigService>();
            var secSubClient = new SupabaseHttpClient(new HttpClient(handler));
            secServices.AddSingleton(secSubClient);
            secServices.AddSingleton<StationPairingService>();
            using (var secDb = new FuelProDbContext(new DbContextOptionsBuilder<FuelProDbContext>().UseSqlite($"Data Source={secDbPath}").Options))
            {
                secDb.Database.EnsureCreated();
            }
            using var spSecondary = secServices.BuildServiceProvider();
            var secConfig = spSecondary.GetRequiredService<SyncConfigService>();
            await secConfig.SaveSettingsAsync(new SyncSettings
            {
                StationId = "TEMP-STATION",
                MachineId = "PC-SECONDARY"
            });

            var secPairing = spSecondary.GetRequiredService<StationPairingService>();
            var redeemRes = await secPairing.RedeemPairingCodeAsync(genRes.Data!, "Office-PC-2", "https://mock.supabase.co", "anon-client-key");

            Assert.True(redeemRes.Success, redeemRes.Error);
            Assert.Equal("STATION-PAIR-01", redeemRes.Data!.StationId);
            Assert.True(redeemRes.Data.SyncEnabled);
            Assert.Equal(2, storedDevices.Count);
            Assert.Contains(storedDevices, d => d["MachineId"]?.ToString() == "PC-PRIMARY" && d["Role"]?.ToString() == "Primary");
            Assert.Contains(storedDevices, d => d["MachineId"]?.ToString() == "PC-SECONDARY" && d["Role"]?.ToString() == "Secondary");

            // 3. List, check authorization, and revoke secondary
            var devices = await primaryPairing.ListStationDevicesAsync("STATION-PAIR-01");
            Assert.Equal(2, devices.Count);

            var revokedOk = await primaryPairing.RevokeDeviceAsync("STATION-PAIR-01", "PC-SECONDARY", "Retired");
            Assert.True(revokedOk);
            var (isAuth, reason) = await secPairing.CheckDeviceAuthorizationAsync("STATION-PAIR-01", "PC-SECONDARY");
            Assert.False(isAuth);
            Assert.Contains("Retired", reason);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (System.IO.File.Exists(secDbPath)) System.IO.File.Delete(secDbPath); } catch { }
        }
    }

    [Fact]
    public async Task StationPairing_Validation_RejectsInvalidUrlMissingConfigAndServiceRoleKeys()
    {
        var handler = new CapturingHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") }));
        using var sp = BuildServices(handler);
        var configSvc = sp.GetRequiredService<SyncConfigService>();
        var pairingSvc = sp.GetRequiredService<StationPairingService>();

        // Missing Supabase URL/Key
        await configSvc.SaveSettingsAsync(new SyncSettings
        {
            StationId = "MITALI-01",
            MachineId = "PC-1",
            SupabaseUrl = "",
            SupabaseApiKey = ""
        });
        var missingRes = await pairingSvc.GeneratePairingCodeAsync();
        Assert.False(missingRes.Success);

        // Invalid URL format
        await configSvc.SaveSettingsAsync(new SyncSettings
        {
            StationId = "MITALI-01",
            MachineId = "PC-1",
            SupabaseUrl = "not-a-valid-url",
            SupabaseApiKey = "anon-client-key"
        });
        var badUrlRes = await pairingSvc.GeneratePairingCodeAsync();
        Assert.False(badUrlRes.Success);
        Assert.Contains("URL", badUrlRes.Error, StringComparison.OrdinalIgnoreCase);

        // Redeem rejects service_role key
        var redeemServiceRole = await pairingSvc.RedeemPairingCodeAsync(
            "PYRO-ABCD-2345",
            "PC-2",
            "https://mock.supabase.co",
            "jwt.service_role.secret");
        Assert.False(redeemServiceRole.Success);
        Assert.Contains("service_role", redeemServiceRole.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MultiComputerDefault_RemainsFalse_OnFreshInstallation()
    {
        using var sp = BuildServices(new CapturingHttpHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") })));

        var configSvc = sp.GetRequiredService<SyncConfigService>();
        var settings = await configSvc.GetSettingsAsync();

        Assert.False(settings.MultiComputerEnabled);
    }

    [Fact]
    public async Task MultiComputerDisabled_OrdinaryCloudSyncAndSnapshotPush_SucceedWithoutPairingTables()
    {
        var calledPairingTables = false;
        var remoteSettings = new Dictionary<string, JObject>();
        var remoteShifts = new Dictionary<string, JObject>();

        var handler = new CapturingHttpHandler((req, body) =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (path.Contains("StationPairingCodes", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("StationDevices", StringComparison.OrdinalIgnoreCase))
            {
                calledPairingTables = true;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"code\":\"PGRST205\",\"message\":\"Table missing\"}", Encoding.UTF8, "application/json")
                });
            }

            if (req.Method == HttpMethod.Get && path.Contains("/rest/v1/Settings"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new JArray(remoteSettings.Values).ToString(), Encoding.UTF8, "application/json")
                });
            }

            if (req.Method == HttpMethod.Get && path.Contains("/rest/v1/Shifts"))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new JArray(remoteShifts.Values).ToString(), Encoding.UTF8, "application/json")
                });
            }

            if (req.Method == HttpMethod.Post && !string.IsNullOrEmpty(body))
            {
                if (path.Contains("/rest/v1/Settings"))
                {
                    if (body.TrimStart().StartsWith("["))
                    {
                        foreach (JObject obj in JArray.Parse(body))
                            remoteSettings[obj["SyncGuid"]!.ToString()] = obj;
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                        {
                            Content = new StringContent(body, Encoding.UTF8, "application/json")
                        });
                    }
                    var single = JObject.Parse(body);
                    remoteSettings[single["SyncGuid"]!.ToString()] = single;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(new JArray(single).ToString(), Encoding.UTF8, "application/json")
                    });
                }

                if (path.Contains("/rest/v1/Shifts"))
                {
                    foreach (JObject obj in JArray.Parse(body))
                        remoteShifts[obj["SyncGuid"]!.ToString()] = obj;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created)
                    {
                        Content = new StringContent(body, Encoding.UTF8, "application/json")
                    });
                }
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        });

        using var sp = BuildServices(handler);
        int settingId;
        int shiftId;
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var s = new Setting { PumpStationName = "Single-PC Station", LastUpdated = DateTime.Now };
            db.Settings.Add(s);
            var sh = new Shift { ShiftDate = new DateTime(2026, 11, 26), ShiftType = "A" };
            db.Shifts.Add(sh);
            await db.SaveChangesAsync();
            settingId = s.SettingId;
            shiftId = sh.ShiftId;

            db.SyncChangeLogs.Add(new SyncChangeLog
            {
                TableName = "Shifts",
                RecordId = shiftId,
                Operation = "INSERT",
                CreatedAt = DateTime.Now,
                IsSynced = false,
                StationId = "STATION-SINGLE-PC",
                MachineId = "PC-MAIN",
                SyncGuid = Guid.NewGuid().ToString()
            });
            await db.SaveChangesAsync();
        }

        var configSvc = sp.GetRequiredService<SyncConfigService>();
        await configSvc.SaveSettingsAsync(new SyncSettings
        {
            SyncEnabled = true,
            MultiComputerEnabled = false,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "anon-client-key",
            StationId = "STATION-SINGLE-PC",
            MachineId = "PC-MAIN"
        });

        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var bgSyncTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void BgHandler(SyncStatusInfo s)
        {
            if (s.IsConnected && s.StatusMessage == "Synced")
                bgSyncTcs.TrySetResult(true);
        }
        syncEngine.SyncStatusChanged += BgHandler;

        var snapshotRes = await syncEngine.PushStationSnapshotToCloudAsync("STATION-SINGLE-PC");
        Assert.True(snapshotRes.Success, snapshotRes.Message);
        await Task.WhenAny(bgSyncTcs.Task, Task.Delay(5000));
        syncEngine.SyncStatusChanged -= BgHandler;

        await syncEngine.ForceSyncAsync();

        Assert.False(calledPairingTables, "MultiComputerEnabled=false must never require or query pairing tables.");
        Assert.True(syncEngine.CurrentStatus.IsConnected, syncEngine.CurrentStatus.StatusMessage);
        Assert.Single(remoteSettings);
        Assert.Single(remoteShifts);
        Assert.Equal(shiftId, remoteShifts.Values.First()["local_id"]!.Value<int>());
        Assert.Equal(settingId, remoteSettings.Values.First()["local_id"]!.Value<int>());
    }

    [Fact]
    public async Task PushStationSnapshot_TwoLocalSettingsWithTwoMappings_RemoteOwnedByOtherLocalId_PreservesOwnershipWithoutConstraintFailure()
    {
        var guidForLocal1 = "a534430d-87e8-4c2e-8186-786c4e20325b";
        var guidForLocal2 = "f29e581d-fbb6-4008-826f-c58779cab59a";
        var postedPayloads = new List<JObject>();

        // Remote returns ONLY guidForLocal2 (which is already owned in local SyncIdMappings by LocalId=2)
        var handler = new CapturingHttpHandler((req, body) =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Get && path.Contains("/rest/v1/Settings"))
            {
                var rows = new JArray(new JObject
                {
                    ["SyncGuid"] = guidForLocal2,
                    ["station_id"] = "TESTING",
                    ["local_id"] = 2,
                    ["SettingId"] = 2,
                    ["PumpStationName"] = "Remote Station Row 2",
                    ["updated_at"] = "2026-11-27T02:00:00Z"
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(rows.ToString(), Encoding.UTF8, "application/json")
                });
            }

            if (req.Method == HttpMethod.Post && path.Contains("/rest/v1/Settings") && !string.IsNullOrEmpty(body))
            {
                var obj = JObject.Parse(body);
                postedPayloads.Add(obj);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new JArray(obj).ToString(), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        });

        using var sp = BuildServices(handler);
        int localId1;
        int localId2;
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var s1 = new Setting { PumpStationName = "Station Row 1", HsdRate = 91.25, LastUpdated = DateTime.Now };
            var s2 = new Setting { PumpStationName = "Unrelated Row 2", HsdRate = 89.50, LastUpdated = DateTime.Now.AddDays(-1) };
            db.Settings.AddRange(s1, s2);
            await db.SaveChangesAsync();
            localId1 = s1.SettingId;
            localId2 = s2.SettingId;

            db.SyncIdMappings.AddRange(
                new SyncIdMapping { TableName = "Settings", LocalId = localId1, RemoteGuid = guidForLocal1 },
                new SyncIdMapping { TableName = "Settings", LocalId = localId2, RemoteGuid = guidForLocal2 }
            );
            await db.SaveChangesAsync();
        }

        var configSvc = sp.GetRequiredService<SyncConfigService>();
        await configSvc.SaveSettingsAsync(new SyncSettings
        {
            SyncEnabled = false,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "anon-client-key",
            StationId = "TESTING",
            MachineId = "ORION-PRIME"
        });

        var syncEngine = sp.GetRequiredService<SyncEngine>();

        // Repeated snapshot pushes must succeed cleanly without DbUpdateException UNIQUE constraint error
        var first = await syncEngine.PushStationSnapshotToCloudAsync("TESTING");
        var second = await syncEngine.PushStationSnapshotToCloudAsync("TESTING");

        Assert.True(first.Success, first.Message);
        Assert.True(second.Success, second.Message);
        Assert.Equal(2, postedPayloads.Count);
        Assert.All(postedPayloads, p =>
        {
            Assert.Equal(guidForLocal2, p["SyncGuid"]!.ToString());
            Assert.Equal(2, p["local_id"]!.Value<int>());
            Assert.Equal(2, p["SettingId"]!.Value<int>());
        });

        // Verify SyncIdMappings ownership & unrelated Settings row were preserved untouched with zero duplicates
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var maps = await db.SyncIdMappings.Where(m => m.TableName == "Settings").OrderBy(m => m.LocalId).ToListAsync();
            Assert.Equal(2, maps.Count);
            Assert.Equal(localId1, maps[0].LocalId);
            Assert.Equal(guidForLocal1, maps[0].RemoteGuid);
            Assert.Equal(localId2, maps[1].LocalId);
            Assert.Equal(guidForLocal2, maps[1].RemoteGuid);
            Assert.Equal(2, maps.Select(m => m.RemoteGuid).Distinct(StringComparer.OrdinalIgnoreCase).Count());

            var row2 = await db.Settings.AsNoTracking().FirstAsync(s => s.SettingId == localId2);
            Assert.Equal("Unrelated Row 2", row2.PumpStationName);
            Assert.Equal(89.50, row2.HsdRate);
        }
    }

    [Fact]
    public async Task PushStationSnapshot_MultipleUnorderedRemoteSettings_DeterministicallyPrefersLocalMappedRowAndIsIdempotent()
    {
        var guidLocal1 = "a534430d-87e8-4c2e-8186-786c4e20325b";
        var guidLocal2 = "f29e581d-fbb6-4008-826f-c58779cab59a";
        var postedGuids = new List<string>();
        var postedLocalIds = new List<int>();
        string? observedGetQuery = null;

        // Remote returns multiple Settings rows in reverse/arbitrary order where guidLocal2 is newer than guidLocal1
        var handler = new CapturingHttpHandler((req, body) =>
        {
            var path = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Get && path.Contains("/rest/v1/Settings"))
            {
                observedGetQuery = path;
                var rows = new JArray(
                    new JObject
                    {
                        ["SyncGuid"] = guidLocal2,
                        ["station_id"] = "TESTING",
                        ["local_id"] = 2,
                        ["SettingId"] = 2,
                        ["updated_at"] = "2026-11-27T02:05:00Z"
                    },
                    new JObject
                    {
                        ["SyncGuid"] = guidLocal1,
                        ["station_id"] = "TESTING",
                        ["local_id"] = 1,
                        ["SettingId"] = 1,
                        ["updated_at"] = "2026-11-27T01:00:00Z"
                    }
                );
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(rows.ToString(), Encoding.UTF8, "application/json")
                });
            }

            if (req.Method == HttpMethod.Post && path.Contains("/rest/v1/Settings") && !string.IsNullOrEmpty(body))
            {
                var obj = JObject.Parse(body);
                postedGuids.Add(obj["SyncGuid"]!.ToString());
                postedLocalIds.Add(obj["local_id"]!.Value<int>());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(new JArray(obj).ToString(), Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        });

        using var sp = BuildServices(handler);
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var s1 = new Setting { PumpStationName = "Primary Setting", LastUpdated = DateTime.Now };
            var s2 = new Setting { PumpStationName = "Secondary Setting", LastUpdated = DateTime.Now };
            db.Settings.AddRange(s1, s2);
            await db.SaveChangesAsync();

            db.SyncIdMappings.AddRange(
                new SyncIdMapping { TableName = "Settings", LocalId = s1.SettingId, RemoteGuid = guidLocal1 },
                new SyncIdMapping { TableName = "Settings", LocalId = s2.SettingId, RemoteGuid = guidLocal2 }
            );
            await db.SaveChangesAsync();
        }

        var configSvc = sp.GetRequiredService<SyncConfigService>();
        await configSvc.SaveSettingsAsync(new SyncSettings
        {
            SyncEnabled = false,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "anon-client-key",
            StationId = "TESTING",
            MachineId = "ORION-PRIME"
        });

        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var res1 = await syncEngine.PushStationSnapshotToCloudAsync("TESTING");
        var res2 = await syncEngine.PushStationSnapshotToCloudAsync("TESTING");

        Assert.True(res1.Success, res1.Message);
        Assert.True(res2.Success, res2.Message);
        Assert.Contains("order=updated_at.desc", observedGetQuery);
        Assert.Equal(new[] { guidLocal1, guidLocal1 }, postedGuids);
        Assert.Equal(new[] { 1, 1 }, postedLocalIds);

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var maps = await db.SyncIdMappings.Where(m => m.TableName == "Settings").ToListAsync();
            Assert.Equal(2, maps.Count);
        }
    }

    [Fact]
    public async Task PullStationSnapshot_RemoteGuidOwnedByOtherLocalMapping_PreservesOwnershipWithoutConstraintFailure()
    {
        var guidLocal1 = "aaaaaaaa-1111-2222-3333-444444444444";
        var guidLocal2 = "bbbbbbbb-1111-2222-3333-444444444444";

        var handler = new CapturingHttpHandler((req, _) =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.Contains("/rest/v1/Settings"))
            {
                var rows = new JArray(new JObject
                {
                    ["SyncGuid"] = guidLocal2,
                    ["station_id"] = "TESTING",
                    ["PumpStationName"] = "Cloud Pulled Name"
                });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(rows.ToString(), Encoding.UTF8, "application/json")
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        });

        using var sp = BuildServices(handler);
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var s1 = new Setting { PumpStationName = "Row 1", LastUpdated = DateTime.Now };
            var s2 = new Setting { PumpStationName = "Row 2", LastUpdated = DateTime.Now };
            db.Settings.AddRange(s1, s2);
            await db.SaveChangesAsync();

            db.SyncIdMappings.AddRange(
                new SyncIdMapping { TableName = "Settings", LocalId = s1.SettingId, RemoteGuid = guidLocal1 },
                new SyncIdMapping { TableName = "Settings", LocalId = s2.SettingId, RemoteGuid = guidLocal2 }
            );
            await db.SaveChangesAsync();
        }

        var configSvc = sp.GetRequiredService<SyncConfigService>();
        await configSvc.SaveSettingsAsync(new SyncSettings
        {
            SyncEnabled = false,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "anon-client-key",
            StationId = "TESTING",
            MachineId = "ORION-PRIME"
        });

        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var pullRes = await syncEngine.PullStationSnapshotFromCloudAsync("TESTING");

        Assert.True(pullRes.Success, pullRes.Message);
        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var maps = await db.SyncIdMappings.Where(m => m.TableName == "Settings").OrderBy(m => m.LocalId).ToListAsync();
            Assert.Equal(2, maps.Count);
            Assert.Equal(guidLocal1, maps[0].RemoteGuid);
            Assert.Equal(guidLocal2, maps[1].RemoteGuid);
        }
    }
}
