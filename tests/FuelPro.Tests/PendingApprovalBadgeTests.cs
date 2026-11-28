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
using FuelPro.Sync;
using FuelPro.UI.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FuelPro.Tests;

public class PendingApprovalBadgeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connStr;
    private readonly DbContextOptions<FuelProDbContext> _options;

    public PendingApprovalBadgeTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _dbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fuelpro_badge_test_{Guid.NewGuid():N}.db");
        _connStr = $"Data Source={_dbPath};Pooling=False";
        _options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite(_connStr)
            .Options;

        using var db = new FuelProDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        try
        {
            if (System.IO.File.Exists(_dbPath)) System.IO.File.Delete(_dbPath);
        }
        catch { }
    }

    private ServiceProvider CreateServices(
        HttpClient httpClient,
        SyncSettings? settings = null,
        IFeatureToggleService? featureToggleService = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(_connStr), ServiceLifetime.Transient);
        services.AddScoped<IShiftCycleService, ShiftCycleService>();
        services.AddScoped<IDsmCalculationService, DsmCalculationService>();
        services.AddScoped<INozzleReadingRepository, NozzleReadingRepository>();
        services.AddScoped<IDsmEntryRepository, DsmEntryRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<SyncConfigService>();

        var syncSettings = settings ?? new SyncSettings
        {
            SyncEnabled = true,
            MultiComputerEnabled = false,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "RASHTRA-CCED97",
            MachineId = "MITALI-PC"
        };

        var supabaseHttp = new SupabaseHttpClient(httpClient);
        supabaseHttp.Configure(syncSettings.SupabaseUrl, syncSettings.SupabaseApiKey);
        services.AddSingleton(supabaseHttp);
        services.AddSingleton<SyncEngine>();
        services.AddSingleton(sp => new SupabaseDsmService(sp.GetRequiredService<SyncConfigService>(), httpClient));
        if (featureToggleService != null)
        {
            services.AddSingleton(featureToggleService);
        }
        services.AddSingleton(sp => new DsmSubmissionPollingService(
            sp.GetRequiredService<SyncConfigService>(),
            sp.GetService<IFeatureToggleService>(),
            httpClient,
            sp.GetService<SyncEngine>()));

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<SyncConfigService>().SaveSettingsAsync(syncSettings).GetAwaiter().GetResult();
        return sp;
    }

    private static object MakeSubmission(
        Guid id,
        int pumpId = 1,
        string shiftDate = "2026-11-24",
        string shiftType = "A",
        string dsmUserId = "dsm-1",
        string dsmName = "Ramesh Sawant",
        string status = "Pending",
        string processingStatus = "Pending",
        string stationId = "RASHTRA-CCED97",
        DateTime? submittedAt = null,
        bool isDeleted = false,
        string? deletedAt = null)
    {
        return new
        {
            Id = id,
            StationId = stationId,
            PumpId = pumpId,
            ShiftDate = shiftDate,
            ShiftType = shiftType,
            DsmUserId = dsmUserId,
            DsmUsers = new { FullName = dsmName },
            Status = status,
            ProcessingStatus = processingStatus,
            SubmittedAt = submittedAt ?? new DateTime(2026, 11, 24, 14, 0, 0, DateTimeKind.Utc),
            IsDeleted = isDeleted,
            DeletedAt = deletedAt,
            Notes = ""
        };
    }

    private static async Task<DsmApprovalAudit> SeedValidAuditAsync(
        FuelProDbContext db,
        Guid submissionId,
        string approvedBy = "Manager",
        int pumpId = 1,
        string shiftType = "A",
        DateTime? approvedAt = null)
    {
        var shift = new Shift { ShiftDate = new DateTime(2026, 11, 24), ShiftType = shiftType };
        db.Shifts.Add(shift);
        await db.SaveChangesAsync();

        var entry = new DsmEntry
        {
            ShiftId = shift.ShiftId,
            DsmName = "DSM",
            PumpId = pumpId,
            GrossSales = 1000
        };
        db.DsmEntries.Add(entry);
        await db.SaveChangesAsync();

        var audit = new DsmApprovalAudit
        {
            SubmissionId = submissionId,
            ApprovedBy = approvedBy,
            ApprovedAt = approvedAt ?? DateTime.Now,
            OriginalDataJson = "{}",
            ApprovedDataJson = JsonConvert.SerializeObject(new
            {
                DsmEntryId = entry.DsmEntryId,
                ShiftId = shift.ShiftId,
                GrossSales = 1000.0,
                TotalCollection = 1000.0,
                Mismatch = 0.0
            })
        };
        db.DsmApprovalAudits.Add(audit);
        await db.SaveChangesAsync();
        return audit;
    }

    private static string Serialize(params object[] items) => JsonConvert.SerializeObject(items);

    [Fact]
    public async Task T01_LocallyApprovedAuditStillPendingInSupabase_ExcludedFromBadgeAndQueue()
    {
        var approvedId = Guid.NewGuid();
        var genuinePendingId = Guid.NewGuid();

        using (var db = new FuelProDbContext(_options))
        {
            await SeedValidAuditAsync(db, approvedId, "Manager", pumpId: 1);
        }

        var handler = new TestHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("DsmSubmissions"))
            {
                return JsonResponse(HttpStatusCode.OK, Serialize(
                    MakeSubmission(approvedId, pumpId: 1, dsmUserId: "dsm-1"),
                    MakeSubmission(genuinePendingId, pumpId: 2, dsmUserId: "dsm-2")));
            }
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var poller = sp.GetRequiredService<DsmSubmissionPollingService>();
        await poller.ForceRefreshAsync(bypassThrottle: true);

        var vm = new DsmApprovalQueueViewModel(sp);
        await vm.RefreshQueueAsync();

        Assert.Equal(1, poller.CurrentPendingCount);
        Assert.Single(vm.PendingSubmissions);
        Assert.Equal(genuinePendingId, vm.PendingSubmissions[0].Id);
    }

    [Fact]
    public async Task T02_MitaliIncident_SevenLocallyApprovedPendingRows_YieldZeroBadgeAndZeroQueue()
    {
        var mitaliIds = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToArray();
        using (var db = new FuelProDbContext(_options))
        {
            for (int i = 0; i < mitaliIds.Length; i++)
            {
                await SeedValidAuditAsync(db, mitaliIds[i], "Admin", pumpId: i + 1);
            }
        }

        var rows = mitaliIds.Select((id, i) => MakeSubmission(id, pumpId: i + 1, dsmUserId: $"dsm-{i}")).ToArray();
        var handler = new TestHandler(req =>
        {
            if (req.Method == HttpMethod.Get && req.RequestUri!.PathAndQuery.Contains("DsmSubmissions") && !req.RequestUri.PathAndQuery.Contains("Status=eq.Approved"))
                return JsonResponse(HttpStatusCode.OK, Serialize(rows));
            if (req.RequestUri!.PathAndQuery.Contains("rpc/"))
                return JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\"}");
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var poller = sp.GetRequiredService<DsmSubmissionPollingService>();
        await poller.ForceRefreshAsync(bypassThrottle: true);

        var vm = new DsmApprovalQueueViewModel(sp);
        await vm.RefreshQueueAsync();

        Assert.Equal(0, poller.CurrentPendingCount);
        Assert.True(poller.HasAuthoritativeCount);
        Assert.Empty(vm.PendingSubmissions);
    }

    [Fact]
    public void T03_OperationalKeyDeduplication_SameDsmSamePumpShiftDate_DeduplicatesToNewest()
    {
        var olderId = Guid.NewGuid();
        var newerId = Guid.NewGuid();
        var json = Serialize(
            MakeSubmission(olderId, pumpId: 6, shiftDate: "2026-11-22", shiftType: "B", dsmUserId: "dsm-ramesh", submittedAt: new DateTime(2026, 11, 22, 18, 0, 0, DateTimeKind.Utc)),
            MakeSubmission(newerId, pumpId: 6, shiftDate: "2026-11-22", shiftType: "B", dsmUserId: "dsm-ramesh", submittedAt: new DateTime(2026, 11, 22, 18, 15, 0, DateTimeKind.Utc)));

        var items = JsonConvert.DeserializeObject<List<dynamic>>(json);
        var res = DsmPendingSubmissionEvaluator.Evaluate(items, new HashSet<Guid>(), "RASHTRA-CCED97");

        Assert.Equal(1, res.Count);
        Assert.Equal(newerId, res.ActionableSubmissions[0].Id);
        Assert.Single(res.DuplicatePendingIds);
        Assert.Equal(olderId, res.DuplicatePendingIds[0]);
    }

    [Fact]
    public void T04_DistinctDsmsOrMissingDsmUserId_SamePumpShiftDate_NotCollapsed()
    {
        var rameshId = Guid.NewGuid();
        var roshanId = Guid.NewGuid();
        var blankDsm1 = Guid.NewGuid();
        var blankDsm2 = Guid.NewGuid();

        var json = Serialize(
            MakeSubmission(rameshId, pumpId: 6, shiftDate: "2026-11-22", shiftType: "B", dsmUserId: "dsm-ramesh"),
            MakeSubmission(roshanId, pumpId: 6, shiftDate: "2026-11-22", shiftType: "B", dsmUserId: "dsm-roshan"),
            MakeSubmission(blankDsm1, pumpId: 3, shiftDate: "2026-11-22", shiftType: "B", dsmUserId: ""),
            MakeSubmission(blankDsm2, pumpId: 3, shiftDate: "2026-11-22", shiftType: "B", dsmUserId: ""));

        var items = JsonConvert.DeserializeObject<List<dynamic>>(json);
        var res = DsmPendingSubmissionEvaluator.Evaluate(items, new HashSet<Guid>(), "RASHTRA-CCED97");

        Assert.Equal(4, res.Count);
        Assert.Empty(res.DuplicatePendingIds);
    }

    [Fact]
    public void T05_NormalizedStatusAndSoftDeleteFiltering_ExcludesNonPendingStatuses()
    {
        var pendingId = Guid.NewGuid();
        var json = Serialize(
            MakeSubmission(Guid.NewGuid(), status: "approved"),
            MakeSubmission(Guid.NewGuid(), status: "REJECTED"),
            MakeSubmission(Guid.NewGuid(), status: "Cancelled"),
            MakeSubmission(Guid.NewGuid(), status: "canceled"),
            MakeSubmission(Guid.NewGuid(), status: "Deleted"),
            MakeSubmission(Guid.NewGuid(), status: "Archived"),
            MakeSubmission(Guid.NewGuid(), status: "Completed"),
            MakeSubmission(Guid.NewGuid(), status: "Pending", isDeleted: true),
            MakeSubmission(Guid.NewGuid(), status: "Pending", deletedAt: "2026-11-24T10:00:00Z"),
            MakeSubmission(pendingId, status: " pending "));

        var items = JsonConvert.DeserializeObject<List<dynamic>>(json);
        var res = DsmPendingSubmissionEvaluator.Evaluate(items, new HashSet<Guid>(), "RASHTRA-CCED97");

        Assert.Equal(1, res.Count);
        Assert.Equal(pendingId, res.ActionableSubmissions[0].Id);
    }

    [Fact]
    public async Task T06_RecoverableProcessingStatuses_CountedConsistentlyByBadgeAndQueue()
    {
        var idClaimed = Guid.NewGuid();
        var idLocalProc = Guid.NewGuid();
        var idRecovery = Guid.NewGuid();

        var handler = new TestHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("DsmSubmissions") && !req.RequestUri.PathAndQuery.Contains("Status=eq.Approved"))
            {
                return JsonResponse(HttpStatusCode.OK, Serialize(
                    MakeSubmission(idClaimed, pumpId: 1, dsmUserId: "d1", status: "InProgress", processingStatus: "Claimed"),
                    MakeSubmission(idLocalProc, pumpId: 2, dsmUserId: "d2", status: "Pending", processingStatus: "LocalProcessing"),
                    MakeSubmission(idRecovery, pumpId: 3, dsmUserId: "d3", status: "Pending", processingStatus: "RecoveryRequired")));
            }
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var poller = sp.GetRequiredService<DsmSubmissionPollingService>();
        await poller.ForceRefreshAsync(bypassThrottle: true);

        var vm = new DsmApprovalQueueViewModel(sp);
        await vm.RefreshQueueAsync();

        Assert.Equal(3, poller.CurrentPendingCount);
        Assert.Equal(3, vm.PendingSubmissions.Count);
    }

    [Fact]
    public async Task T07_CrossStationSubmissions_ExcludedUnderStrictStationIsolation()
    {
        var ownId = Guid.NewGuid();
        var foreignId = Guid.NewGuid();

        var handler = new TestHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("DsmSubmissions") && !req.RequestUri.PathAndQuery.Contains("Status=eq.Approved"))
            {
                return JsonResponse(HttpStatusCode.OK, Serialize(
                    MakeSubmission(ownId, stationId: "RASHTRA-CCED97"),
                    MakeSubmission(foreignId, stationId: "OTHER-STATION-99")));
            }
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var poller = sp.GetRequiredService<DsmSubmissionPollingService>();
        await poller.ForceRefreshAsync(bypassThrottle: true);

        var vm = new DsmApprovalQueueViewModel(sp);
        await vm.RefreshQueueAsync();

        Assert.Equal(1, poller.CurrentPendingCount);
        Assert.Single(vm.PendingSubmissions);
        Assert.Equal(ownId, vm.PendingSubmissions[0].Id);
    }

    [Fact]
    public async Task T08_SinglePcMode_MissingRpcPgrst202_FallsBackToConditionalRestPatchAndSucceedsWhenRowUpdated()
    {
        var subId = Guid.NewGuid();
        bool conditionalPatchCalled = false;
        string? capturedPatchBody = null;
        string? capturedPrefer = null;
        string? capturedUrl = null;

        var handler = new TestHandler(async req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (url.Contains("rpc/claim_dsm_submission") || url.Contains("rpc/complete_dsm_approval"))
            {
                return JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\",\"message\":\"Could not find the function\"}");
            }
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissions"))
            {
                return JsonResponse(HttpStatusCode.OK, Serialize(MakeSubmission(subId, stationId: "RASHTRA-CCED97", status: "Pending")));
            }
            if (req.Method == HttpMethod.Patch && url.Contains("DsmSubmissions"))
            {
                conditionalPatchCalled = true;
                capturedUrl = url;
                capturedPrefer = req.Headers.TryGetValues("Prefer", out var vals) ? string.Join(",", vals) : null;
                capturedPatchBody = await req.Content!.ReadAsStringAsync();
                return JsonResponse(HttpStatusCode.OK, $"[{{\"Id\":\"{subId}\",\"Status\":\"Approved\"}}]");
            }
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler), new SyncSettings
        {
            SyncEnabled = true,
            MultiComputerEnabled = false,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "RASHTRA-CCED97",
            MachineId = "MITALI-PC"
        });

        var svc = sp.GetRequiredService<SupabaseDsmService>();
        var res = await svc.ApproveSubmissionAsync(subId, "Mitali Manager", "lock-123");

        Assert.True(res.Success, res.Error);
        Assert.True(conditionalPatchCalled);
        Assert.Contains($"Id=eq.{subId}", capturedUrl);
        Assert.Contains("Status=eq.Pending", capturedUrl);
        Assert.Contains("StationId=eq.RASHTRA-CCED97", capturedUrl);
        Assert.Equal("return=representation", capturedPrefer);
        var bodyObj = JObject.Parse(capturedPatchBody!);
        Assert.Equal("Approved", bodyObj["Status"]?.ToString());
        Assert.Equal("Mitali Manager", bodyObj["ApprovedBy"]?.ToString());
    }

    [Fact]
    public async Task T09_SinglePcConditionalPatch_ZeroRowsUpdated_FailsInsteadOfSilentSuccess()
    {
        var subId = Guid.NewGuid();
        var handler = new TestHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (url.Contains("rpc/"))
                return JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\"}");
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissions"))
                return JsonResponse(HttpStatusCode.OK, Serialize(MakeSubmission(subId, status: "Pending")));
            if (req.Method == HttpMethod.Patch)
                return JsonResponse(HttpStatusCode.OK, "[]"); // Concurrent transition caused 0 rows to match Status=eq.Pending
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var svc = sp.GetRequiredService<SupabaseDsmService>();

        var res = await svc.ApproveSubmissionAsync(subId, "Manager", "lock-abc");

        Assert.False(res.Success);
        Assert.Contains("updated 0 rows", res.Error);
    }

    [Fact]
    public async Task T10_MultiComputerMode_MissingRpc_DoesNotFallbackToRestPatch()
    {
        var subId = Guid.NewGuid();
        bool patchAttempted = false;
        var handler = new TestHandler(req =>
        {
            if (req.Method == HttpMethod.Patch) patchAttempted = true;
            if (req.RequestUri!.PathAndQuery.Contains("rpc/"))
                return JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\"}");
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler), new SyncSettings
        {
            SyncEnabled = true,
            MultiComputerEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "RASHTRA-CCED97",
            MachineId = "PC-MULTI-1"
        });

        var svc = sp.GetRequiredService<SupabaseDsmService>();
        var res = await svc.ApproveSubmissionAsync(subId, "Manager", "lock-1");

        Assert.False(res.Success);
        Assert.False(patchAttempted);
        Assert.Contains("NotFound", res.Error);
    }

    [Fact]
    public async Task T11_AutoHeal_RecoversOriginalAuditApprovedByAndApprovedAt()
    {
        var subId = Guid.NewGuid();
        var originalApprovedAt = new DateTime(2026, 11, 24, 9, 30, 0, DateTimeKind.Local);
        JObject? sentPatch = null;
        var patchSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using (var db = new FuelProDbContext(_options))
        {
            var shift = new Shift { ShiftDate = new DateTime(2026, 11, 24), ShiftType = "A" };
            db.Shifts.Add(shift);
            await db.SaveChangesAsync();

            var entry = new DsmEntry { ShiftId = shift.ShiftId, DsmName = "Ramesh", PumpId = 1, GrossSales = 1000 };
            db.DsmEntries.Add(entry);
            await db.SaveChangesAsync();

            db.DsmApprovalAudits.Add(new DsmApprovalAudit
            {
                SubmissionId = subId,
                ApprovedBy = "Original Shift Manager",
                ApprovedAt = originalApprovedAt,
                OriginalDataJson = "{}",
                ApprovedDataJson = JsonConvert.SerializeObject(new { DsmEntryId = entry.DsmEntryId })
            });
            await db.SaveChangesAsync();
        }

        var handler = new TestHandler(async req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissions") && !url.Contains("Status=eq.Approved"))
                return JsonResponse(HttpStatusCode.OK, Serialize(MakeSubmission(subId)));
            if (url.Contains("rpc/"))
                return JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\"}");
            if (req.Method == HttpMethod.Patch && url.Contains("DsmSubmissions"))
            {
                sentPatch = JObject.Parse(await req.Content!.ReadAsStringAsync());
                patchSignal.TrySetResult(true);
                return JsonResponse(HttpStatusCode.OK, $"[{{\"Id\":\"{subId}\",\"Status\":\"Approved\"}}]");
            }
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var vm = new DsmApprovalQueueViewModel(sp);
        await vm.RefreshQueueAsync();

        var completed = await Task.WhenAny(patchSignal.Task, Task.Delay(3000));
        Assert.Same(patchSignal.Task, completed);
        Assert.NotNull(sentPatch);
        Assert.Equal("Original Shift Manager", sentPatch!["ApprovedBy"]?.ToString());
        Assert.Equal(originalApprovedAt.ToUniversalTime(), sentPatch["ApprovedAt"]!.ToObject<DateTime>().ToUniversalTime());
    }

    [Fact]
    public async Task T12_StationSwitch_ClearsStaleBadgeAndRefreshesNewStation()
    {
        var stationASub = Guid.NewGuid();
        var stationBSub1 = Guid.NewGuid();
        var stationBSub2 = Guid.NewGuid();

        var handler = new TestHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (url.Contains("StationId=eq.STATION-A"))
                return JsonResponse(HttpStatusCode.OK, Serialize(MakeSubmission(stationASub, stationId: "STATION-A")));
            if (url.Contains("StationId=eq.STATION-B"))
                return JsonResponse(HttpStatusCode.OK, Serialize(
                    MakeSubmission(stationBSub1, pumpId: 1, dsmUserId: "d1", stationId: "STATION-B"),
                    MakeSubmission(stationBSub2, pumpId: 2, dsmUserId: "d2", stationId: "STATION-B")));
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler), new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "STATION-A"
        });

        var config = sp.GetRequiredService<SyncConfigService>();
        var poller = sp.GetRequiredService<DsmSubmissionPollingService>();

        await poller.ForceRefreshAsync(bypassThrottle: true);
        Assert.Equal(1, poller.CurrentPendingCount);

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        poller.PendingCountChanged += c => { if (c == 2) tcs.TrySetResult(c); };

        await config.SaveSettingsAsync(new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "STATION-B"
        });

        var done = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        Assert.Same(tcs.Task, done);
        Assert.Equal(2, poller.CurrentPendingCount);
    }

    [Fact]
    public async Task T13_OutOfOrderPollResponse_BlockedByMonotonicVersionGuard()
    {
        var slowFirstEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlowFirst = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var staleItems = Serialize(
            MakeSubmission(Guid.NewGuid(), pumpId: 1, dsmUserId: "d1"),
            MakeSubmission(Guid.NewGuid(), pumpId: 2, dsmUserId: "d2"),
            MakeSubmission(Guid.NewGuid(), pumpId: 3, dsmUserId: "d3"));

        var handler = new TestHandler(async req =>
        {
            slowFirstEntered.TrySetResult(true);
            await releaseSlowFirst.Task;
            return JsonResponse(HttpStatusCode.OK, staleItems);
        });

        using var sp = CreateServices(new HttpClient(handler));
        var poller = sp.GetRequiredService<DsmSubmissionPollingService>();

        // Start a slow network poll expecting 3 items
        var slowPollTask = poller.ForceRefreshAsync(bypassThrottle: true);
        await slowFirstEntered.Task;

        // While slow poll is in-flight, queue completes a newer evaluation showing 0
        poller.SyncCountFromQueue(0, "RASHTRA-CCED97");
        Assert.Equal(0, poller.CurrentPendingCount);

        // Now release slow poll response; stale count 3 must NOT overwrite 0!
        releaseSlowFirst.TrySetResult(true);
        await slowPollTask;

        Assert.Equal(0, poller.CurrentPendingCount);
    }

    [Fact]
    public async Task T14_SyncEnginePullCompleted_RefreshesPollingCountAgainstLocalAudits()
    {
        var id1 = Guid.NewGuid();
        var zeroReached = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handler = new TestHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("DsmSubmissions"))
            {
                return JsonResponse(HttpStatusCode.OK, Serialize(MakeSubmission(id1)));
            }
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var poller = sp.GetRequiredService<DsmSubmissionPollingService>();
        var syncEngine = sp.GetRequiredService<SyncEngine>();
        poller.Start();

        try
        {
            // Initial poll sees 1 pending submission
            await poller.ForceRefreshAsync(bypassThrottle: true);
            Assert.Equal(1, poller.CurrentPendingCount);

            poller.PendingCountChanged += count =>
            {
                if (count == 0) zeroReached.TrySetResult(count);
            };

            // Simulate SyncEngine pulling a valid DsmEntry + DsmApprovalAudit from another PC and completing pull
            using (var db = new FuelProDbContext(_options))
            {
                await SeedValidAuditAsync(db, id1, "OtherPC", pumpId: 1);
            }

            // Wait past the 2s ForceRefresh throttle window, then raise PullCompleted
            await Task.Delay(2100);
            syncEngine.RaisePullCompleted();

            var done = await Task.WhenAny(zeroReached.Task, Task.Delay(3000));
            Assert.Same(zeroReached.Task, done);
            Assert.Equal(0, poller.CurrentPendingCount);
        }
        finally
        {
            poller.Stop();
        }
    }

    [Fact]
    public async Task T15_PollOrQueueFailure_MarksAuthoritativeFalseAndClearsStaleBadgeCount()
    {
        bool shouldFail = false;
        var id1 = Guid.NewGuid();

        var handler = new TestHandler(req =>
        {
            if (shouldFail)
                return JsonResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":\"offline\"}");
            return JsonResponse(HttpStatusCode.OK, Serialize(MakeSubmission(id1)));
        });

        using var sp = CreateServices(new HttpClient(handler));
        var poller = sp.GetRequiredService<DsmSubmissionPollingService>();

        await poller.ForceRefreshAsync(bypassThrottle: true);
        Assert.Equal(1, poller.CurrentPendingCount);
        Assert.True(poller.HasAuthoritativeCount);
        Assert.Null(poller.LastError);

        shouldFail = true;
        await poller.ForceRefreshAsync(bypassThrottle: true);

        Assert.Equal(0, poller.CurrentPendingCount);
        Assert.False(poller.HasAuthoritativeCount);
        Assert.Contains("503", poller.LastError);

        // Recovery clears failure state
        shouldFail = false;
        await poller.ForceRefreshAsync(bypassThrottle: true);
        Assert.Equal(1, poller.CurrentPendingCount);
        Assert.True(poller.HasAuthoritativeCount);
        Assert.Null(poller.LastError);
    }

    [Fact]
    public async Task T16_ImmediateQueueReconciliation_SyncCountFromQueueUpdatesBadgeImmediately()
    {
        var handler = new TestHandler(_ => JsonResponse(HttpStatusCode.OK, "[]"));
        using var sp = CreateServices(new HttpClient(handler));
        var poller = sp.GetRequiredService<DsmSubmissionPollingService>();

        var events = new List<int>();
        poller.PendingCountChanged += c => events.Add(c);

        poller.SyncCountFromQueue(4, "RASHTRA-CCED97");
        Assert.Equal(4, poller.CurrentPendingCount);

        poller.SyncCountFromQueue(3, "RASHTRA-CCED97");
        Assert.Equal(3, poller.CurrentPendingCount);
        Assert.Equal(new[] { 4, 3 }, events);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task T17_ConcurrentQueueRefreshes_SerializeCleanlyWithoutDuplicatingItems()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        int activeRequests = 0;
        int maxConcurrent = 0;

        var handler = new TestHandler(async req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("DsmSubmissions") && !req.RequestUri.PathAndQuery.Contains("Status=eq.Approved"))
            {
                int current = Interlocked.Increment(ref activeRequests);
                if (current > maxConcurrent) maxConcurrent = current;
                await Task.Delay(50);
                Interlocked.Decrement(ref activeRequests);
                return JsonResponse(HttpStatusCode.OK, Serialize(
                    MakeSubmission(id1, pumpId: 1, dsmUserId: "d1"),
                    MakeSubmission(id2, pumpId: 2, dsmUserId: "d2")));
            }
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var vm = new DsmApprovalQueueViewModel(sp);

        await Task.WhenAll(vm.RefreshQueueAsync(), vm.RefreshQueueAsync(), vm.RefreshQueueAsync());

        Assert.Equal(1, maxConcurrent);
        Assert.Equal(2, vm.PendingSubmissions.Count);
        Assert.Equal(2, sp.GetRequiredService<DsmSubmissionPollingService>().CurrentPendingCount);
    }

    [Fact]
    public void T18_HeadlessUiDispatchHelper_DoesNotThrowSafely()
    {
        int observed = -1;

        var ex = Record.Exception(() =>
            MainWindowViewModel.DispatchOnUiThread(() => observed = 5));

        Assert.Null(ex);
        Assert.Equal(5, observed);
    }

    [Fact]
    public async Task T19_SyncDisabledOrFeatureOff_SetsAuthoritativeZero()
    {
        int httpCalls = 0;
        var handler = new TestHandler(_ => { Interlocked.Increment(ref httpCalls); return JsonResponse(HttpStatusCode.OK, "[]"); });

        using var sp = CreateServices(new HttpClient(handler), new SyncSettings
        {
            SyncEnabled = false,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "RASHTRA-CCED97"
        });

        var poller = sp.GetRequiredService<DsmSubmissionPollingService>();
        await poller.ForceRefreshAsync(bypassThrottle: true);

        Assert.Equal(0, httpCalls);
        Assert.Equal(0, poller.CurrentPendingCount);
        Assert.True(poller.HasAuthoritativeCount);
    }

    [Fact]
    public async Task T20_CombinedParityInvariant_PollingBadgeMatchesApprovalQueueAcrossAllFilters()
    {
        var approvedLocally = Guid.NewGuid();
        var duplicateOlder = Guid.NewGuid();
        var duplicateNewer = Guid.NewGuid();
        var reliefOtherDsm = Guid.NewGuid();
        var claimedInFlight = Guid.NewGuid();

        using (var db = new FuelProDbContext(_options))
        {
            await SeedValidAuditAsync(db, approvedLocally, "Manager", pumpId: 1);
        }

        var rawJson = Serialize(
            MakeSubmission(approvedLocally, pumpId: 1, dsmUserId: "dsm-a"),
            MakeSubmission(duplicateOlder, pumpId: 2, shiftDate: "2026-11-24", shiftType: "B", dsmUserId: "dsm-b", submittedAt: new DateTime(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc)),
            MakeSubmission(duplicateNewer, pumpId: 2, shiftDate: "2026-11-24", shiftType: "B", dsmUserId: "dsm-b", submittedAt: new DateTime(2026, 11, 24, 15, 10, 0, DateTimeKind.Utc)),
            MakeSubmission(reliefOtherDsm, pumpId: 2, shiftDate: "2026-11-24", shiftType: "B", dsmUserId: "dsm-c"),
            MakeSubmission(claimedInFlight, pumpId: 3, shiftDate: "2026-11-24", shiftType: "C", dsmUserId: "dsm-d", status: "InProgress", processingStatus: "Claimed"),
            MakeSubmission(Guid.NewGuid(), pumpId: 4, dsmUserId: "dsm-e", status: "Rejected"),
            MakeSubmission(Guid.NewGuid(), pumpId: 5, dsmUserId: "dsm-f", isDeleted: true),
            MakeSubmission(Guid.NewGuid(), pumpId: 6, dsmUserId: "dsm-g", stationId: "FOREIGN-99"),
            new { Id = "not-a-guid", PumpId = 7, ShiftDate = "2026-11-24", Status = "Pending" }
        );

        var handler = new TestHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("DsmSubmissions") && !req.RequestUri.PathAndQuery.Contains("Status=eq.Approved"))
                return JsonResponse(HttpStatusCode.OK, rawJson);
            if (req.RequestUri.PathAndQuery.Contains("rpc/"))
                return JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\"}");
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var poller = sp.GetRequiredService<DsmSubmissionPollingService>();
        await poller.ForceRefreshAsync(bypassThrottle: true);

        var vm = new DsmApprovalQueueViewModel(sp);
        await vm.RefreshQueueAsync();

        // Expected actionable: duplicateNewer + reliefOtherDsm + claimedInFlight = 3
        Assert.Equal(3, poller.CurrentPendingCount);
        Assert.Equal(poller.CurrentPendingCount, vm.PendingSubmissions.Count);
        Assert.Contains(vm.PendingSubmissions, x => x.Id == duplicateNewer);
        Assert.Contains(vm.PendingSubmissions, x => x.Id == reliefOtherDsm);
        Assert.Contains(vm.PendingSubmissions, x => x.Id == claimedInFlight);
    }

    // ────────────────────────────────────────────────────────────────────────
    // W2-1 Regression Tests: Validated Local Approval Evidence
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task W2_1_OrphanedAuditMissingDsmEntry_DoesNotHidePendingSubmissionFromBadgeOrQueue()
    {
        var orphanedId = Guid.NewGuid();
        var validApprovedId = Guid.NewGuid();
        var genuinePendingId = Guid.NewGuid();
        bool autoHealCalledForOrphaned = false;

        using (var db = new FuelProDbContext(_options))
        {
            await SeedValidAuditAsync(db, validApprovedId, "Manager", pumpId: 1);

            // Seed orphaned audit referencing nonexistent DsmEntryId = 99999
            db.DsmApprovalAudits.Add(new DsmApprovalAudit
            {
                SubmissionId = orphanedId,
                ApprovedBy = "Admin",
                ApprovedAt = DateTime.Now,
                OriginalDataJson = "{}",
                ApprovedDataJson = "{\"DsmEntryId\":99999,\"ShiftId\":1}"
            });
            await db.SaveChangesAsync();
        }

        var handler = new TestHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (url.Contains($"Id=eq.{orphanedId}") && req.Method == HttpMethod.Patch)
                autoHealCalledForOrphaned = true;
            if (url.Contains("rpc/"))
                return JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\"}");
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissions") && !url.Contains("Status=eq.Approved"))
                return JsonResponse(HttpStatusCode.OK, Serialize(
                    MakeSubmission(validApprovedId, pumpId: 1, dsmUserId: "d1"),
                    MakeSubmission(orphanedId, pumpId: 2, dsmUserId: "d2"),
                    MakeSubmission(genuinePendingId, pumpId: 3, dsmUserId: "d3")));
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var configSvc = sp.GetRequiredService<SyncConfigService>();
        var validatedIds = await configSvc.GetApprovedSubmissionIdsAsync();

        Assert.Contains(validApprovedId, validatedIds);
        Assert.DoesNotContain(orphanedId, validatedIds);

        var poller = sp.GetRequiredService<DsmSubmissionPollingService>();
        await poller.ForceRefreshAsync(bypassThrottle: true);

        var vm = new DsmApprovalQueueViewModel(sp);
        await vm.RefreshQueueAsync();

        // Both orphanedId and genuinePendingId stay visible; validApprovedId is excluded
        Assert.Equal(2, poller.CurrentPendingCount);
        Assert.Equal(2, vm.PendingSubmissions.Count);
        Assert.Contains(vm.PendingSubmissions, x => x.Id == orphanedId);
        Assert.Contains(vm.PendingSubmissions, x => x.Id == genuinePendingId);
        Assert.DoesNotContain(vm.PendingSubmissions, x => x.Id == validApprovedId);
        Assert.False(autoHealCalledForOrphaned);
    }

    [Fact]
    public async Task W2_1_InvalidAuditsMissingApprovedByOrDsmEntryId_DoNotExcludeFromBadgeOrQueue()
    {
        var blankById = Guid.NewGuid();
        var emptyJsonId = Guid.NewGuid();
        var malformedJsonId = Guid.NewGuid();

        using (var db = new FuelProDbContext(_options))
        {
            db.DsmApprovalAudits.AddRange(
                new DsmApprovalAudit { SubmissionId = blankById, ApprovedBy = "   ", ApprovedAt = DateTime.Now, ApprovedDataJson = "{\"DsmEntryId\":1}" },
                new DsmApprovalAudit { SubmissionId = emptyJsonId, ApprovedBy = "Admin", ApprovedAt = DateTime.Now, ApprovedDataJson = "{}" },
                new DsmApprovalAudit { SubmissionId = malformedJsonId, ApprovedBy = "Admin", ApprovedAt = DateTime.Now, ApprovedDataJson = "{not-json" }
            );
            await db.SaveChangesAsync();
        }

        var handler = new TestHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissions") && !url.Contains("Status=eq.Approved"))
                return JsonResponse(HttpStatusCode.OK, Serialize(
                    MakeSubmission(blankById, pumpId: 1, dsmUserId: "d1"),
                    MakeSubmission(emptyJsonId, pumpId: 2, dsmUserId: "d2"),
                    MakeSubmission(malformedJsonId, pumpId: 3, dsmUserId: "d3")));
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var poller = sp.GetRequiredService<DsmSubmissionPollingService>();
        await poller.ForceRefreshAsync(bypassThrottle: true);

        var vm = new DsmApprovalQueueViewModel(sp);
        await vm.RefreshQueueAsync();

        Assert.Equal(3, poller.CurrentPendingCount);
        Assert.Equal(3, vm.PendingSubmissions.Count);
    }

    // ────────────────────────────────────────────────────────────────────────
    // W2-2 Regression Tests: Safe Single-PC Missing-RPC Claim & Offline Semantics
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task W2_2_SinglePcMissingRpc_ClaimFallbackNeverWritesApprovedWithoutCompletion()
    {
        var subId = Guid.NewGuid();
        bool anyPatchOrPostTableWrite = false;

        var handler = new TestHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (url.Contains("rpc/claim_dsm_submission"))
                return JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\"}");
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissions"))
                return JsonResponse(HttpStatusCode.OK, Serialize(MakeSubmission(subId, stationId: "RASHTRA-CCED97", status: "Pending")));
            if (req.Method == HttpMethod.Patch || (!url.Contains("rpc/") && req.Method == HttpMethod.Post))
                anyPatchOrPostTableWrite = true;
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var dsmService = sp.GetRequiredService<SupabaseDsmService>();

        var claimRes = await dsmService.ClaimSubmissionWithFencingAsync(subId, "MITALI-PC", "Admin", "lock-1");

        Assert.True(claimRes.Success, claimRes.Error);
        Assert.Equal("SINGLE_PC_FALLBACK_NO_CLAIM_RPC", claimRes.Data?.Code);
        Assert.Null(claimRes.Data?.ClaimToken);
        Assert.False(anyPatchOrPostTableWrite);
    }

    [Theory]
    [InlineData("Approved", "REMOTE_ALREADY_PROCESSED")]
    [InlineData("Rejected", "REMOTE_ALREADY_PROCESSED")]
    [InlineData("Expired", "REMOTE_ALREADY_PROCESSED")]
    public async Task W2_2_SinglePcMissingRpc_RemoteAlreadyTransitioned_BlocksClaimAndPreventsApproval(string remoteStatus, string expectedCode)
    {
        var subId = Guid.NewGuid();
        bool patchCalled = false;

        var handler = new TestHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (url.Contains("rpc/"))
                return JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\"}");
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissions"))
                return JsonResponse(HttpStatusCode.OK, Serialize(MakeSubmission(subId, stationId: "RASHTRA-CCED97", status: remoteStatus)));
            if (req.Method == HttpMethod.Patch)
                patchCalled = true;
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var dsmService = sp.GetRequiredService<SupabaseDsmService>();

        var claimRes = await dsmService.ClaimSubmissionWithFencingAsync(subId, "MITALI-PC", "Admin", "lock-1");
        Assert.False(claimRes.Success);
        Assert.Equal(expectedCode, claimRes.Data?.Code);
        Assert.False(patchCalled);

        var approveRes = await dsmService.ApproveSubmissionAsync(subId, "Admin", "lock-1");
        Assert.False(approveRes.Success);
        Assert.False(patchCalled);
    }

    [Fact]
    public async Task W2_2_SinglePcMissingRpc_RemoteMissing_ReturnsRemoteMissingConflict()
    {
        var subId = Guid.NewGuid();
        var handler = new TestHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (url.Contains("rpc/"))
                return JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\"}");
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissions"))
                return JsonResponse(HttpStatusCode.OK, "[]");
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var dsmService = sp.GetRequiredService<SupabaseDsmService>();

        var claimRes = await dsmService.ClaimSubmissionWithFencingAsync(subId, "MITALI-PC", "Admin", "lock-1");
        Assert.False(claimRes.Success);
        Assert.Equal("REMOTE_MISSING", claimRes.Data?.Code);
    }

    [Fact]
    public async Task W2_2_SinglePcMissingRpc_RemoteStatusCheckServerError_DoesNotAuthorizePatch()
    {
        var subId = Guid.NewGuid();
        bool patchCalled = false;

        var handler = new TestHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (url.Contains("rpc/"))
                return JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\"}");
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissions"))
                return JsonResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":\"db down\"}");
            if (req.Method == HttpMethod.Patch)
                patchCalled = true;
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateServices(new HttpClient(handler));
        var dsmService = sp.GetRequiredService<SupabaseDsmService>();

        var claimRes = await dsmService.ClaimSubmissionWithFencingAsync(subId, "MITALI-PC", "Admin", "lock-1");
        Assert.False(claimRes.Success);
        Assert.Equal("CLOUD_VERIFICATION_UNAVAILABLE", claimRes.Data?.Code);
        Assert.False(patchCalled);

        var approveRes = await dsmService.ApproveSubmissionAsync(subId, "Admin", "lock-1");
        Assert.False(approveRes.Success);
        Assert.False(patchCalled);
    }

    [Fact]
    public async Task W2_2_ApproveCommand_RemoteRejectedOrMissing_BlocksLocalSqliteCommitAndRefreshesQueue()
    {
        var subId = Guid.NewGuid();
        int getListCalls = 0;

        var handler = new TestHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (url.Contains("rpc/claim_dsm_submission"))
                return JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\"}");
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissionReadings"))
                return JsonResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(new[]
                {
                    new { NozzleId = 1, PumpId = 1, FuelType = "MS-I", OpeningReading = 100.0, ClosingReading = 150.0, Rate = 103.81 }
                }));
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissionCollections"))
                return JsonResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(new[]
                {
                    new { Cash = 5190.50, UPI = 0.0, Card = 0.0, PetroCard = 0.0, CashDeposit = 0.0, Others = 0.0, Credit = 0.0, Expense = 0.0, ExpenseNotes = "", Short = 0.0, Excess = 0.0 }
                }));
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissions?") && url.Contains($"Id=eq.{subId}"))
                return JsonResponse(HttpStatusCode.OK, Serialize(MakeSubmission(subId, status: "Rejected")));
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissions"))
            {
                Interlocked.Increment(ref getListCalls);
                return JsonResponse(HttpStatusCode.OK, "[]");
            }
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateFullApprovalTestServices(new HttpClient(handler));
        var vm = new DsmApprovalQueueViewModel(sp);
        await vm.RefreshQueueAsync();
        getListCalls = 0;

        vm.SelectedSubmission = new DsmPendingSubmission
        {
            Id = subId,
            DsmName = "DSM Test",
            PumpId = 1,
            ShiftDate = new DateTime(2026, 11, 24),
            ShiftType = "A",
            SubmittedAt = DateTime.UtcNow
        };
        for (int i = 0; i < 40 && vm.IsLoadingDetails; i++) await Task.Delay(25);

        vm.NozzleReadings.Clear();
        vm.NozzleReadings.Add(new DsmNozzleRow
        {
            PumpId = 1,
            NozzleId = 1,
            FuelType = "MS-I",
            OpeningReading = 100,
            ClosingReading = 150,
            Rate = 103.81,
            SubmittedRate = 103.81
        });
        vm.CashAmount = 5190.50;
        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(vm, null);
        Assert.True(vm.IsSubmissionComplete, vm.SubmissionIncompleteReason);

        await vm.ApproveCommand.ExecuteAsync(null);

        using var db = new FuelProDbContext(_options);
        Assert.False(await db.DsmApprovalAudits.AnyAsync(a => a.SubmissionId == subId));
        Assert.False(await db.DsmEntries.AnyAsync(e => e.DsmName == "DSM Test"));
        Assert.True(getListCalls >= 1);
    }

    [Fact]
    public async Task W2_2_ApproveCommand_OfflineSinglePc_CommitsLocallyWithAwaitingReconciliationMarker()
    {
        var subId = Guid.NewGuid();

        var handler = new TestHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (url.Contains("rpc/claim_dsm_submission"))
                return JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"PGRST202\"}");
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissionReadings"))
                return JsonResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(new[]
                {
                    new { NozzleId = 1, PumpId = 1, FuelType = "MS-I", OpeningReading = 100.0, ClosingReading = 120.0, Rate = 103.81 }
                }));
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissionCollections"))
                return JsonResponse(HttpStatusCode.OK, JsonConvert.SerializeObject(new[]
                {
                    new { Cash = 2076.20, UPI = 0.0, Card = 0.0, PetroCard = 0.0, CashDeposit = 0.0, Others = 0.0, Credit = 0.0, Expense = 0.0, ExpenseNotes = "", Short = 0.0, Excess = 0.0 }
                }));
            if (req.Method == HttpMethod.Get && url.Contains("DsmSubmissions?") && url.Contains($"Id=eq.{subId}"))
                return JsonResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":\"offline\"}");
            if (req.Method == HttpMethod.Patch || url.Contains("rpc/complete_dsm_approval"))
                return JsonResponse(HttpStatusCode.ServiceUnavailable, "{\"error\":\"offline\"}");
            return JsonResponse(HttpStatusCode.OK, "[]");
        });

        using var sp = CreateFullApprovalTestServices(new HttpClient(handler));
        var vm = new DsmApprovalQueueViewModel(sp);
        await vm.RefreshQueueAsync();

        var pendingItem = new DsmPendingSubmission
        {
            Id = subId,
            DsmName = "Offline DSM",
            PumpId = 1,
            ShiftDate = new DateTime(2026, 11, 24),
            ShiftType = "A",
            SubmittedAt = DateTime.UtcNow
        };
        vm.PendingSubmissions.Add(pendingItem);
        vm.SelectedSubmission = pendingItem;
        for (int i = 0; i < 40 && vm.IsLoadingDetails; i++) await Task.Delay(25);

        vm.NozzleReadings.Clear();
        vm.NozzleReadings.Add(new DsmNozzleRow
        {
            PumpId = 1,
            NozzleId = 1,
            FuelType = "MS-I",
            OpeningReading = 100,
            ClosingReading = 120,
            Rate = 103.81,
            SubmittedRate = 103.81
        });
        vm.CashAmount = 2076.20;
        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(vm, null);
        Assert.True(vm.IsSubmissionComplete, vm.SubmissionIncompleteReason);

        await vm.ApproveCommand.ExecuteAsync(null);

        using var db = new FuelProDbContext(_options);
        var savedAudit = await db.DsmApprovalAudits.FirstOrDefaultAsync(a => a.SubmissionId == subId);
        Assert.NotNull(savedAudit);
        Assert.Contains("Awaiting Cloud Reconciliation", savedAudit!.Remarks);
        Assert.True(await db.DsmEntries.AnyAsync(e => e.DsmName == "Offline DSM"));
    }

    private ServiceProvider CreateFullApprovalTestServices(HttpClient httpClient)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(_connStr), ServiceLifetime.Transient);
        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<IDsmEntryRepository, DsmEntryRepository>();
        services.AddScoped<INozzleReadingRepository, NozzleReadingRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IDebitEntryRepository, DebitEntryRepository>();
        services.AddScoped<ITestingEntryRepository, TestingEntryRepository>();
        services.AddScoped<IExpenseRepository, ExpenseRepository>();
        services.AddScoped<ICashDenominationRepository, CashDenominationRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<ICreditorRepository, CreditorRepository>();
        services.AddScoped<ICreditorRepaymentRepository, CreditorRepaymentRepository>();
        services.AddScoped<IDsmPersonalDebtorRepository, DsmPersonalDebtorRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IShiftOtherCashRepository, ShiftOtherCashRepository>();
        services.AddScoped<IShiftFuelRateRepository, ShiftFuelRateRepository>();
        services.AddSingleton<IShiftCycleService, ShiftCycleService>();
        services.AddSingleton<IDsmCalculationService, DsmCalculationService>();
        services.AddScoped<IShiftAggregationService, ShiftAggregationService>();
        services.AddTransient<DsmEntryService>();
        services.AddSingleton<AuthService>();
        services.AddSingleton<SyncConfigService>();

        var syncSettings = new SyncSettings
        {
            SyncEnabled = true,
            MultiComputerEnabled = false,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "RASHTRA-CCED97",
            MachineId = "MITALI-PC"
        };

        var supabaseHttp = new SupabaseHttpClient(httpClient);
        supabaseHttp.Configure(syncSettings.SupabaseUrl, syncSettings.SupabaseApiKey);
        services.AddSingleton(supabaseHttp);
        services.AddSingleton<SyncEngine>();
        services.AddSingleton(sp => new SupabaseDsmService(sp.GetRequiredService<SyncConfigService>(), httpClient));
        services.AddSingleton(sp => new DsmSubmissionPollingService(
            sp.GetRequiredService<SyncConfigService>(),
            null,
            httpClient,
            sp.GetService<SyncEngine>()));

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<SyncConfigService>().SaveSettingsAsync(syncSettings).GetAwaiter().GetResult();
        return provider;
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode code, string json)
    {
        return new HttpResponseMessage(code)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _fn;
        public TestHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) => _fn = req => Task.FromResult(fn(req));
        public TestHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _fn(request);
    }
}
