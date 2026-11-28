using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;

namespace FuelPro.Tests;

public class P0dUtcTimestampIntegrityTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FuelProDbContext> _options;

    private static readonly TimeZoneInfo IstTimeZone = CreateIstTimeZone();

    public P0dUtcTimestampIntegrityTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = new FuelProDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private static readonly JsonSerializerSettings RawJsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None
    };

    private static TimeZoneInfo CreateIstTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
            catch { return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "IST", "IST"); }
        }
    }

    [Fact]
    public void PushFormat_IstLocalAndUnspecifiedAuditTimestamps_ConvertToUtcOnce()
    {
        // Local IST 14:00:00 -> 08:30:00Z
        var offset = IstTimeZone.GetUtcOffset(new DateTime(2026, 11, 25, 14, 0, 0));
        var dtoLocal = new DateTimeOffset(2026, 11, 25, 14, 0, 0, offset);
        var unspecifiedSqlite = new DateTime(2026, 11, 25, 14, 0, 0, DateTimeKind.Unspecified);

        var fromUnspecified = SyncTimestampHelper.FormatForPush(
            typeof(DsmEntry), nameof(DsmEntry.UpdatedAt), unspecifiedSqlite, IstTimeZone);
        var expectedUtc = dtoLocal.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);

        Assert.Equal(expectedUtc, fromUnspecified);
        Assert.EndsWith("Z", fromUnspecified);

        // DateTimeKind.Local uses OS local zone; verify ToUtcDateTime converts Local once and leaves Utc unchanged
        var localNow = DateTime.SpecifyKind(new DateTime(2026, 11, 25, 14, 0, 0), DateTimeKind.Local);
        var utcFromLocal = SyncTimestampHelper.ToUtcDateTime(localNow, SyncTimestampSemantics.LocalInstant, IstTimeZone);
        Assert.Equal(DateTimeKind.Utc, utcFromLocal.Kind);
        Assert.Equal(localNow.ToUniversalTime(), utcFromLocal);

        var alreadyUtc = new DateTime(2026, 11, 25, 8, 30, 0, DateTimeKind.Utc);
        var fromUtc = SyncTimestampHelper.FormatForPush(
            typeof(DsmEntry), nameof(DsmEntry.UpdatedAt), alreadyUtc, IstTimeZone);
        Assert.Equal("2026-11-25T08:30:00.000000Z", fromUtc);

        // Verify Push emitted UTC 'Z' even for DsmPumpAssignment.AssignedDate (event instant) vs AttendanceDate (business date)
        Assert.Equal(SyncTimestampSemantics.LocalInstant, SyncTimestampHelper.Classify(typeof(DsmPumpAssignment), nameof(DsmPumpAssignment.AssignedDate)));
        Assert.Equal(SyncTimestampSemantics.BusinessDate, SyncTimestampHelper.Classify(typeof(DsmAttendance), nameof(DsmAttendance.AttendanceDate)));
        Assert.Equal(SyncTimestampSemantics.LocalInstant, SyncTimestampHelper.Classify(typeof(DsmAttendance), nameof(DsmAttendance.ClockInTime)));
    }

    [Fact]
    public void PushFormat_UtcInstant_NeverDoubleShifted()
    {
        var utcFromSqlite = new DateTime(2026, 11, 25, 8, 30, 15, DateTimeKind.Unspecified);
        var utcTyped = new DateTime(2026, 11, 25, 8, 30, 15, DateTimeKind.Utc);

        var res1 = SyncTimestampHelper.FormatForPush(typeof(StationPairingToken), nameof(StationPairingToken.CreatedAt), utcFromSqlite, IstTimeZone);
        var res2 = SyncTimestampHelper.FormatForPush(typeof(StationPairingToken), nameof(StationPairingToken.CreatedAt), utcTyped, IstTimeZone);

        Assert.Equal("2026-11-25T08:30:15.000000Z", res1);
        Assert.Equal("2026-11-25T08:30:15.000000Z", res2);
    }

    [Fact]
    public void BusinessDates_PreservedExactlyWithoutTimezoneShift()
    {
        var shiftDate = new DateTime(2026, 11, 25, 0, 0, 0, DateTimeKind.Unspecified);
        var formatted = SyncTimestampHelper.FormatForPush(typeof(Shift), nameof(Shift.ShiftDate), shiftDate, IstTimeZone);
        Assert.Equal("2026-11-25T00:00:00", formatted);

        // Pulling date-only, offsetless timestamp, UTC midnight, or legacy IST-midnight-as-UTC (18:30Z prev day)
        Assert.Equal(new DateTime(2026, 11, 25), SyncTimestampHelper.ParseForPull(typeof(Shift), nameof(Shift.ShiftDate), "2026-11-25", IstTimeZone));
        Assert.Equal(new DateTime(2026, 11, 25), SyncTimestampHelper.ParseForPull(typeof(Shift), nameof(Shift.ShiftDate), "2026-11-25T00:00:00", IstTimeZone));
        Assert.Equal(new DateTime(2026, 11, 25), SyncTimestampHelper.ParseForPull(typeof(Shift), nameof(Shift.ShiftDate), "2026-11-25T00:00:00+00:00", IstTimeZone));
        Assert.Equal(new DateTime(2026, 11, 25), SyncTimestampHelper.ParseForPull(typeof(Shift), nameof(Shift.ShiftDate), "2026-11-24T18:30:00+00:00", IstTimeZone));
        Assert.Equal(new DateTime(2026, 11, 25), SyncTimestampHelper.ParseForPull(typeof(DsmQrPaymentEntry), nameof(DsmQrPaymentEntry.Date), "2026-11-25", IstTimeZone));
        Assert.Equal(new DateTime(2026, 11, 25), SyncTimestampHelper.ParseForPull(typeof(OpeningBalance), nameof(OpeningBalance.OpeningDate), "2026-11-24T18:30:00Z", IstTimeZone));
    }

    [Fact]
    public void PullParse_CultureIndependentAcrossNonUsCultures()
    {
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");

            var parsedAudit = SyncTimestampHelper.ParseForPull(
                typeof(DsmApprovalAudit), nameof(DsmApprovalAudit.ApprovedAt), "2026-11-25T08:30:45.123456+00:00", IstTimeZone);
            Assert.Equal(new DateTime(2026, 11, 25, 14, 0, 45, 123).AddTicks(4560), parsedAudit);

            var ok = SyncTimestampHelper.TryParseRemoteUtcCursor("2026-11-25T14:00:00.500000+05:30", out var formattedCursor, out var cursorUtc);
            Assert.True(ok);
            Assert.Equal("2026-11-25T08:30:00.500000Z", formattedCursor);
            Assert.Equal(DateTimeKind.Utc, cursorUtc.Kind);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
            Thread.CurrentThread.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public async Task PushThenNextCycleIncrementalPull_DoesNotSkipDueToUtcOffsetOrWatermark()
    {
        // PC-A creates & pushes a Creditor with SQLite DateTimeKind.Unspecified CreatedAt.
        // Mock Supabase server sets updated_at = NOW() (true UTC) and rejects updated_at > upperWatermark.
        // PC-B pulls on the very next cycle and MUST receive the record immediately.
        var baseUtc = new DateTime(2026, 11, 25, 8, 30, 0, DateTimeKind.Utc);
        var serverClockUtc = baseUtc.AddSeconds(2);
        var cloudCreditors = new List<Dictionary<string, object?>>();

        var handlerA = new MockHttpHandler(async req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.Contains("/Creditors"))
            {
                var body = await req.Content!.ReadAsStringAsync();
                var pushed = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(body, RawJsonSettings)!;
                foreach (var row in pushed)
                {
                    // Assert push emitted UTC timestamp ending in Z (never unsuffixed Unspecified IST)
                    var createdAtWire = row["CreatedAt"]?.ToString();
                    Assert.NotNull(createdAtWire);
                    Assert.EndsWith("Z", createdAtWire);
                    Assert.True(DateTimeOffset.TryParse(createdAtWire, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto));
                    // Verify it wasn't pushed +5.5h into the future relative to local TZ
                    Assert.True(dto.UtcDateTime <= DateTime.UtcNow.AddMinutes(1), $"CreatedAt '{createdAtWire}' appears ahead of UTC now.");

                    row["updated_at"] = serverClockUtc.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);
                    cloudCreditors.Add(row);
                }
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
        });

        var settingsA = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "TEST-STATION-P0D",
            MachineId = "PC-A",
            LastPullTime = baseUtc,
            LastSyncTime = baseUtc
        };

        var spA = BuildServices(handlerA, _connection, settingsA);
        using (var dbA = spA.GetRequiredService<FuelProDbContext>())
        {
            // Simulate entity saved with DateTime.Now then reloaded from SQLite as Unspecified
            dbA.Creditors.Add(new Creditor { Name = "P0D UTC Creditor", CreatedAt = DateTime.Now });
            await dbA.SaveChangesAsync();
        }

        var engineA = spA.GetRequiredService<SyncEngine>();
        await engineA.ForceSyncAsync();
        Assert.Single(cloudCreditors);

        // Now PC-B runs incremental pull with an independent SQLite DB and LastPullTime = baseUtc.
        using var connB = new SqliteConnection("DataSource=:memory:");
        await connB.OpenAsync();
        using (var initB = new FuelProDbContext(new DbContextOptionsBuilder<FuelProDbContext>().UseSqlite(connB).Options))
        {
            await initB.Database.EnsureCreatedAsync();
        }

        var handlerB = new MockHttpHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            var msg = new HttpResponseMessage(HttpStatusCode.OK);
            msg.Headers.Date = new DateTimeOffset(serverClockUtc.AddSeconds(5), TimeSpan.Zero);

            if (req.Method == HttpMethod.Get && url.Contains("/Creditors?"))
            {
                // Enforce server-side upperWatermark filter just like PostgREST: updated_at <= upperWatermark
                var lteMatch = System.Text.RegularExpressions.Regex.Match(url, @"updated_at=lte\.([^&]+)");
                Assert.True(lteMatch.Success);
                var watermarkStr = Uri.UnescapeDataString(lteMatch.Groups[1].Value);
                var watermarkUtc = DateTimeOffset.Parse(watermarkStr, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime;

                var matching = cloudCreditors.Where(r =>
                {
                    var upd = DateTimeOffset.Parse(r["updated_at"]!.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime;
                    return upd <= watermarkUtc;
                }).ToList();

                msg.Content = new StringContent(JsonConvert.SerializeObject(matching), Encoding.UTF8, "application/json");
                return Task.FromResult(msg);
            }

            msg.Content = new StringContent("[]", Encoding.UTF8, "application/json");
            return Task.FromResult(msg);
        });

        var settingsB = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "TEST-STATION-P0D",
            MachineId = "PC-B",
            LastPullTime = baseUtc,
            LastSyncTime = baseUtc
        };

        var spB = BuildServices(handlerB, connB, settingsB);
        var engineB = spB.GetRequiredService<SyncEngine>();
        var pullMethod = typeof(SyncEngine).GetMethod("PerformPullAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)pullMethod.Invoke(engineB, new object[] { settingsB, false })!;

        using var verifyB = spB.GetRequiredService<FuelProDbContext>();
        var pulled = await verifyB.Creditors.FirstOrDefaultAsync(c => c.Name == "P0D UTC Creditor");
        Assert.NotNull(pulled);
    }

    [Fact]
    public async Task ShiftAndDsmEntryRoundTrip_PreservesShiftDateAndHistoricalAuditTimestamp()
    {
        var baseUtc = new DateTime(2026, 11, 25, 6, 0, 0, DateTimeKind.Utc);
        var historicalCreated = new DateTime(2026, 11, 24, 22, 15, 30, DateTimeKind.Local);
        var shiftDay = new DateTime(2026, 11, 24);
        var pushedShifts = new List<Dictionary<string, object?>>();
        var pushedEntries = new List<Dictionary<string, object?>>();

        var handlerA = new MockHttpHandler(async req =>
        {
            if (req.Method == HttpMethod.Post)
            {
                var body = await req.Content!.ReadAsStringAsync();
                var rows = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(body, RawJsonSettings)!;
                foreach (var r in rows)
                {
                    r["updated_at"] = baseUtc.AddSeconds(10).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);
                    if (req.RequestUri!.AbsolutePath.Contains("/Shifts")) pushedShifts.Add(r);
                    if (req.RequestUri!.AbsolutePath.Contains("/DsmEntries")) pushedEntries.Add(r);
                }
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };
        });

        var settingsA = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "TEST-STATION-P0D",
            MachineId = "PC-A",
            LastPullTime = baseUtc,
            LastSyncTime = baseUtc
        };

        var spA = BuildServices(handlerA, _connection, settingsA);
        using (var dbA = spA.GetRequiredService<FuelProDbContext>())
        {
            var shift = new Shift { ShiftDate = shiftDay, ShiftType = "C", CreatedAt = historicalCreated };
            dbA.Shifts.Add(shift);
            await dbA.SaveChangesAsync();

            dbA.DsmEntries.Add(new DsmEntry
            {
                ShiftId = shift.ShiftId,
                DsmName = "DSM Test",
                PumpId = 1,
                GrossSales = 1500m,
                TotalCollection = 1500m,
                CreatedAt = historicalCreated,
                UpdatedAt = historicalCreated
            });
            await dbA.SaveChangesAsync();
        }

        await spA.GetRequiredService<SyncEngine>().ForceSyncAsync();
        Assert.Single(pushedShifts);
        Assert.Single(pushedEntries);
        Assert.Equal("2026-11-24T00:00:00", pushedShifts[0]["ShiftDate"]?.ToString());
        Assert.EndsWith("Z", pushedEntries[0]["UpdatedAt"]?.ToString());

        using var connB = new SqliteConnection("DataSource=:memory:");
        await connB.OpenAsync();
        using (var initB = new FuelProDbContext(new DbContextOptionsBuilder<FuelProDbContext>().UseSqlite(connB).Options))
        {
            await initB.Database.EnsureCreatedAsync();
        }

        var handlerB = new MockHttpHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/Shifts?"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonConvert.SerializeObject(pushedShifts), Encoding.UTF8, "application/json") });
            if (url.Contains("/DsmEntries?"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonConvert.SerializeObject(pushedEntries), Encoding.UTF8, "application/json") });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") });
        });

        var settingsB = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "TEST-STATION-P0D",
            MachineId = "PC-B",
            LastPullTime = baseUtc,
            LastSyncTime = baseUtc
        };

        var spB = BuildServices(handlerB, connB, settingsB);
        var pullMethod = typeof(SyncEngine).GetMethod("PerformPullAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)pullMethod.Invoke(spB.GetRequiredService<SyncEngine>(), new object[] { settingsB, false })!;

        using var verifyB = spB.GetRequiredService<FuelProDbContext>();
        var pulledShift = await verifyB.Shifts.SingleAsync();
        var pulledEntry = await verifyB.DsmEntries.SingleAsync();
        Assert.Equal(shiftDay, pulledShift.ShiftDate.Date);
        Assert.Equal("C", pulledShift.ShiftType);
        Assert.True(Math.Abs((pulledEntry.UpdatedAt - historicalCreated).TotalSeconds) <= 1.0);
    }

    [Fact]
    public void SyncTimestampHelper_AllSyncedEntitiesDateTimeProperties_ExplicitlyClassified()
    {
        using var ctx = new FuelProDbContext(_options);
        var syncedSet = new HashSet<string>(SyncEngine.SyncedTables, StringComparer.OrdinalIgnoreCase);

        foreach (var entityType in ctx.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrEmpty(tableName) || !syncedSet.Contains(tableName))
                continue;

            foreach (var prop in entityType.GetProperties())
            {
                var clrType = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
                if (clrType == typeof(DateTime))
                {
                    Assert.True(
                        SyncTimestampHelper.IsExplicitlyClassified(entityType.ClrType, prop.Name),
                        $"DateTime property {entityType.ClrType.Name}.{prop.Name} (table '{tableName}') is missing from SyncTimestampHelper.ExplicitColumnMap.");
                }
            }
        }
    }

    [Fact]
    public void MigrationScript_UsesTypeAwareTriggerExpressions_AndCoversAllSyncedTables()
    {
        var repoRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sqlPath = System.IO.Path.Combine(repoRoot, "supabase", "migrations", "20261125_p0d_updated_at_triggers.sql");
        Assert.True(System.IO.File.Exists(sqlPath), $"Migration script not found at {sqlPath}");

        var sql = System.IO.File.ReadAllText(sqlPath);

        // Must inspect column type and separate timestamptz vs timestamp without time zone using clock_timestamp()
        Assert.Contains("fn_set_sync_updated_at_timestamptz()", sql);
        Assert.Contains("fn_set_sync_updated_at_timestamp_ntz()", sql);
        Assert.Contains("NEW.\"updated_at\" := clock_timestamp();", sql);
        Assert.Contains("NEW.\"updated_at\" := (clock_timestamp() AT TIME ZONE 'UTC');", sql);
        Assert.DoesNotContain("NEW.\"updated_at\" := timezone(", sql);
        Assert.DoesNotContain("NEW.\"updated_at\" := CURRENT_TIMESTAMP", sql);
        Assert.DoesNotContain("NEW.\"updated_at\" := (CURRENT_TIMESTAMP", sql);
        Assert.Contains("c.data_type IN ('timestamp with time zone', 'timestamp without time zone')", sql);
        Assert.Contains("IF v_col_type = 'timestamp with time zone' THEN", sql);
        Assert.Contains("AND t.tgname >= v_trigger_name", sql);
        Assert.Contains("DROP TRIGGER IF EXISTS", sql);
        Assert.Contains("CREATE INDEX IF NOT EXISTS", sql);
        Assert.Contains("DROP FUNCTION IF EXISTS public.fn_set_sync_updated_at();", sql);

        // Verify all synced tables except SyncChangeLogs (uses "Timestamp") are covered in v_target_tables
        foreach (var table in SyncEngine.SyncedTables)
        {
            if (string.Equals(table, "SyncChangeLogs", StringComparison.OrdinalIgnoreCase))
                continue;
            Assert.Contains($"'{table}'", sql);
        }

        // Verify all PullTableOrder data tables (except SyncChangeLogs) + Users are covered in v_target_tables
        var pullOrderField = typeof(SyncEngine).GetField("PullTableOrder", BindingFlags.NonPublic | BindingFlags.Static)!;
        var pullOrder = (Array)pullOrderField.GetValue(null)!;
        foreach (var item in pullOrder)
        {
            var tName = item.GetType().GetProperty("TableName")!.GetValue(item)!.ToString()!;
            if (string.Equals(tName, "SyncChangeLogs", StringComparison.OrdinalIgnoreCase))
                continue;
            Assert.Contains($"'{tName}'", sql);
        }

        // Verify trigger ordering handles z* naming conflicts cleanly and cleans up both trg_z* and z* patterns
        Assert.Contains("v_prefix := 'zzz';", sql);
        Assert.Contains("z%\\_p0d\\_set\\_updated\\_at", sql);

        // Verify supplemental config/QR tables synced via Settings or triggers are also listed
        foreach (var extra in new[] { "TankDefinitions", "CollectionTypes", "AppFeatureSettings", "StationLayoutPresets", "DsmQrPayments" })
        {
            Assert.Contains($"'{extra}'", sql);
        }
    }

    [Fact]
    public async Task KeysetPagination_EqualTimestampTiesAcrossPages_PreservedWithoutOmission()
    {
        var sharedTsUtc = new DateTime(2026, 11, 25, 8, 30, 0, DateTimeKind.Utc).AddTicks(1234560);
        var sharedTsStr = sharedTsUtc.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);
        var serverNow = sharedTsUtc.AddSeconds(2);

        // Build 502 creditors ALL sharing the identical microsecond updated_at timestamp
        var cloudRows = Enumerable.Range(1, 502)
            .Select(i => new Dictionary<string, object?>
            {
                ["SyncGuid"] = $"guid-{i:D4}",
                ["Name"] = $"Tie Creditor {i:D4}",
                ["updated_at"] = sharedTsStr,
                ["machine_id"] = "REMOTE-PC"
            })
            .OrderBy(r => r["updated_at"]!.ToString(), StringComparer.Ordinal)
            .ThenBy(r => r["SyncGuid"]!.ToString(), StringComparer.Ordinal)
            .ToList();

        var seenUrls = new List<string>();
        var handler = new MockHttpHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            seenUrls.Add(url);
            var msg = new HttpResponseMessage(HttpStatusCode.OK);
            msg.Headers.Date = new DateTimeOffset(serverNow, TimeSpan.Zero);

            if (url.Contains("/Creditors?"))
            {
                var orMatch = System.Text.RegularExpressions.Regex.Match(url, @"or=\(updated_at\.gt\.([^,]+),SyncGuid\.gt\.([^)]+)\)");
                List<Dictionary<string, object?>> page;
                if (!orMatch.Success)
                {
                    page = cloudRows.Take(500).ToList();
                }
                else
                {
                    var curTs = Uri.UnescapeDataString(orMatch.Groups[1].Value);
                    var curGuid = Uri.UnescapeDataString(orMatch.Groups[2].Value);
                    Assert.False(string.IsNullOrEmpty(curGuid));
                    page = cloudRows.Where(r =>
                        string.CompareOrdinal(r["updated_at"]!.ToString(), curTs) > 0 ||
                        (string.CompareOrdinal(r["updated_at"]!.ToString(), curTs) == 0 &&
                         string.CompareOrdinal(r["SyncGuid"]!.ToString(), curGuid) > 0))
                        .Take(500)
                        .ToList();
                }

                msg.Content = new StringContent(JsonConvert.SerializeObject(page), Encoding.UTF8, "application/json");
                return Task.FromResult(msg);
            }

            msg.Content = new StringContent("[]", Encoding.UTF8, "application/json");
            return Task.FromResult(msg);
        });

        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "TEST-TIE-502",
            MachineId = "LOCAL-PC",
            LastPullTime = sharedTsUtc.AddMinutes(-1),
            LastSyncTime = sharedTsUtc.AddMinutes(-1)
        };

        var sp = BuildServices(handler, _connection, settings);
        var pullMethod = typeof(SyncEngine).GetMethod("PerformPullAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)pullMethod.Invoke(sp.GetRequiredService<SyncEngine>(), new object[] { settings, false })!;

        using var verifyDb = sp.GetRequiredService<FuelProDbContext>();
        var pulledCount = await verifyDb.Creditors.CountAsync();
        Assert.Equal(502, pulledCount);
        Assert.DoesNotContain(seenUrls, u => u.Contains("SyncGuid.gt.&") || u.EndsWith("SyncGuid.gt.)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KeysetPagination_FullPageMissingSyncGuid_FailsSafelyWithoutMalformedFilterOrWatermarkAdvance()
    {
        var initialPullUtc = new DateTime(2026, 11, 25, 8, 20, 0, DateTimeKind.Utc);
        var serverNow = new DateTime(2026, 11, 25, 8, 30, 0, DateTimeKind.Utc);
        var tsStr = serverNow.AddSeconds(-5).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);

        // Simulate 500 rows where row #500 is missing SyncGuid -> cannot safely paginate page 2
        var fullPageMissingBoundaryGuid = Enumerable.Range(1, 500)
            .Select(i => new Dictionary<string, object?>
            {
                ["SyncGuid"] = i == 500 ? "" : $"guid-{i:D4}",
                ["Name"] = $"Creditor {i:D4}",
                ["updated_at"] = tsStr,
                ["machine_id"] = "REMOTE-PC"
            })
            .ToList();

        var creditorUrls = new List<string>();
        var handler = new MockHttpHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            var msg = new HttpResponseMessage(HttpStatusCode.OK);
            msg.Headers.Date = new DateTimeOffset(serverNow, TimeSpan.Zero);

            if (url.Contains("/Creditors?"))
            {
                creditorUrls.Add(url);
                msg.Content = new StringContent(JsonConvert.SerializeObject(fullPageMissingBoundaryGuid), Encoding.UTF8, "application/json");
                return Task.FromResult(msg);
            }

            msg.Content = new StringContent("[]", Encoding.UTF8, "application/json");
            return Task.FromResult(msg);
        });

        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "TEST-MISSING-GUID-500",
            MachineId = "LOCAL-PC",
            LastPullTime = initialPullUtc,
            LastSyncTime = initialPullUtc
        };

        var sp = BuildServices(handler, _connection, settings);
        var configService = sp.GetRequiredService<SyncConfigService>();
        var pullMethod = typeof(SyncEngine).GetMethod("PerformPullAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)pullMethod.Invoke(sp.GetRequiredService<SyncEngine>(), new object[] { settings, false })!;

        // Must send only page 1 (never malformed SyncGuid.gt.), must abort table ingestion, and must retain LastPullTime
        Assert.Single(creditorUrls);
        Assert.DoesNotContain("SyncGuid.gt.", creditorUrls[0], StringComparison.OrdinalIgnoreCase);
        var after = await configService.GetSettingsAsync();
        Assert.Equal(initialPullUtc, after.LastPullTime);

        using var verifyDb = sp.GetRequiredService<FuelProDbContext>();
        Assert.Equal(0, await verifyDb.Creditors.CountAsync());
    }

    [Fact]
    public async Task PullWatermark_CapturesSubSecondWritesFromUtcAndAsiaKolkataSessions()
    {
        // Simulate server HTTP Date header truncated to whole seconds (08:30:00 GMT),
        // while rows committed at 08:30:00.850 UTC and 14:00:00.900+05:30 (Asia/Kolkata session = 08:30:00.900Z)
        var serverHeaderUtc = new DateTime(2026, 11, 25, 8, 30, 0, DateTimeKind.Utc);
        var lastPullUtc = new DateTime(2026, 11, 25, 8, 25, 0, DateTimeKind.Utc);

        var rows = new[]
        {
            new Dictionary<string, object?>
            {
                ["SyncGuid"] = "tz-utc-subsec",
                ["Name"] = "Creditor UTC Session",
                ["updated_at"] = "2026-11-25T08:30:00.850000Z",
                ["machine_id"] = "REMOTE-PC"
            },
            new Dictionary<string, object?>
            {
                ["SyncGuid"] = "tz-ist-subsec",
                ["Name"] = "Creditor Kolkata Session",
                ["updated_at"] = "2026-11-25T14:00:00.900000+05:30",
                ["machine_id"] = "REMOTE-PC"
            }
        };

        // Cursor normalization must yield UTC timestamps for both session representations
        Assert.True(SyncTimestampHelper.TryParseRemoteUtcCursor(rows[0]["updated_at"], out var cursor0, out var dt0));
        Assert.True(SyncTimestampHelper.TryParseRemoteUtcCursor(rows[1]["updated_at"], out var cursor1, out var dt1));
        Assert.Equal("2026-11-25T08:30:00.850000Z", cursor0);
        Assert.Equal("2026-11-25T08:30:00.900000Z", cursor1);
        Assert.True(dt1 > dt0);

        var handler = new MockHttpHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            var msg = new HttpResponseMessage(HttpStatusCode.OK);
            msg.Headers.Date = new DateTimeOffset(serverHeaderUtc, TimeSpan.Zero);

            if (url.Contains("/Creditors?"))
            {
                var lteMatch = System.Text.RegularExpressions.Regex.Match(url, @"updated_at=lte\.([^&]+)");
                Assert.True(lteMatch.Success);
                var watermarkUtc = DateTimeOffset.Parse(Uri.UnescapeDataString(lteMatch.Groups[1].Value), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime;

                var filtered = rows.Where(r =>
                    DateTimeOffset.Parse(r["updated_at"]!.ToString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime <= watermarkUtc
                ).ToList();

                msg.Content = new StringContent(JsonConvert.SerializeObject(filtered), Encoding.UTF8, "application/json");
                return Task.FromResult(msg);
            }

            msg.Content = new StringContent("[]", Encoding.UTF8, "application/json");
            return Task.FromResult(msg);
        });

        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "TEST-STATION-TZ",
            MachineId = "LOCAL-PC",
            LastPullTime = lastPullUtc,
            LastSyncTime = lastPullUtc
        };

        var sp = BuildServices(handler, _connection, settings);
        var pullMethod = typeof(SyncEngine).GetMethod("PerformPullAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        await (Task)pullMethod.Invoke(sp.GetRequiredService<SyncEngine>(), new object[] { settings, false })!;

        using var verifyDb = sp.GetRequiredService<FuelProDbContext>();
        var pulledNames = await verifyDb.Creditors.Select(c => c.Name).ToListAsync();
        Assert.Contains("Creditor UTC Session", pulledNames);
        Assert.Contains("Creditor Kolkata Session", pulledNames);
    }

    [Fact]
    public async Task LongRunningTransactionCommitAfterPullStart_WithClockTimestamp_RetrievedOnNextCycle()
    {
        // Simulates a long-running transaction that began at 08:18:00Z, while Cycle 1 runs at 08:25:00Z.
        // The transaction executes its row UPDATE late at 08:25:03.123456Z (clock_timestamp) and commits at 08:25:03.200Z.
        // Because clock_timestamp() > tx start (08:18:00Z), Cycle 2 (lower bound 08:20:00Z) captures it with full microsecond precision.
        var cycle1ServerUtc = new DateTime(2026, 11, 25, 8, 25, 0, DateTimeKind.Utc);
        var cycle2ServerUtc = new DateTime(2026, 11, 25, 8, 25, 5, DateTimeKind.Utc);
        var writeTimestampUtc = new DateTime(2026, 11, 25, 8, 25, 3, DateTimeKind.Utc).AddTicks(1234560); // .123456Z

        // Seed 1 local mapping so Creditors uses incremental updated_at query rather than full history DateTime.MinValue pull
        using (var seedCtx = new FuelProDbContext(_options))
        {
            var existing = new Creditor { Name = "Existing Seed Creditor", CreatedAt = DateTime.Now };
            seedCtx.Creditors.Add(existing);
            await seedCtx.SaveChangesAsync();
            seedCtx.SyncIdMappings.Add(new SyncIdMapping
            {
                TableName = "Creditors",
                LocalId = existing.CreditorId,
                RemoteGuid = "cred-existing-seed"
            });
            await seedCtx.SaveChangesAsync();
        }

        int cycle = 1;
        var rowFromLongTx = new Dictionary<string, object?>
        {
            ["SyncGuid"] = "cred-long-tx-clock",
            ["Name"] = "Creditor Updated Late In Tx",
            ["updated_at"] = writeTimestampUtc.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture),
            ["machine_id"] = "REMOTE-PC"
        };

        var handler = new MockHttpHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            var serverNow = cycle == 1 ? cycle1ServerUtc : cycle2ServerUtc;
            var msg = new HttpResponseMessage(HttpStatusCode.OK);
            msg.Headers.Date = new DateTimeOffset(serverNow, TimeSpan.Zero);

            if (url.Contains("/Creditors?"))
            {
                if (cycle == 1)
                {
                    // Transaction not committed yet during Cycle 1 snapshot
                    msg.Content = new StringContent("[]", Encoding.UTF8, "application/json");
                    return Task.FromResult(msg);
                }

                // Cycle 2 Page 1 uses updated_at=gt.{lower}; Page 2 uses updated_at=gte.{cursor}
                var gtMatch = System.Text.RegularExpressions.Regex.Match(url, @"updated_at=gt\.([^&]+)");
                var gteMatch = System.Text.RegularExpressions.Regex.Match(url, @"updated_at=gte\.([^&]+)");
                var lteMatch = System.Text.RegularExpressions.Regex.Match(url, @"updated_at=lte\.([^&]+)");
                Assert.True(lteMatch.Success);

                if (!gtMatch.Success && gteMatch.Success)
                {
                    // Page 2 after cred-long-tx-clock already returned on Page 1
                    msg.Content = new StringContent("[]", Encoding.UTF8, "application/json");
                    return Task.FromResult(msg);
                }

                Assert.True(gtMatch.Success);
                var lowerUtc = DateTimeOffset.Parse(Uri.UnescapeDataString(gtMatch.Groups[1].Value), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime;
                var upperUtc = DateTimeOffset.Parse(Uri.UnescapeDataString(lteMatch.Groups[1].Value), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).UtcDateTime;

                var matches = (writeTimestampUtc > lowerUtc && writeTimestampUtc <= upperUtc)
                    ? new[] { rowFromLongTx }
                    : Array.Empty<Dictionary<string, object?>>();

                msg.Content = new StringContent(JsonConvert.SerializeObject(matches), Encoding.UTF8, "application/json");
                return Task.FromResult(msg);
            }

            msg.Content = new StringContent("[]", Encoding.UTF8, "application/json");
            return Task.FromResult(msg);
        });

        var settings = new SyncSettings
        {
            SyncEnabled = true,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "TEST-STATION-MVCC",
            MachineId = "LOCAL-PC",
            LastPullTime = cycle1ServerUtc.AddMinutes(-1),
            LastSyncTime = cycle1ServerUtc.AddMinutes(-1)
        };

        var sp = BuildServices(handler, _connection, settings);
        var syncEngine = sp.GetRequiredService<SyncEngine>();
        var configService = sp.GetRequiredService<SyncConfigService>();
        var pullMethod = typeof(SyncEngine).GetMethod("PerformPullAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // Cycle 1 advances LastPullTime to cycle1ServerUtc (08:25:00Z)
        await (Task)pullMethod.Invoke(syncEngine, new object[] { settings, false })!;
        var afterCycle1 = await configService.GetSettingsAsync();
        Assert.Equal(cycle1ServerUtc, afterCycle1.LastPullTime);

        // Cycle 2 pulls committed long-running tx row with microsecond cursor preserved
        cycle = 2;
        await (Task)pullMethod.Invoke(syncEngine, new object[] { afterCycle1, false })!;

        using var verifyDb = sp.GetRequiredService<FuelProDbContext>();
        Assert.True(await verifyDb.Creditors.AnyAsync(c => c.Name == "Creditor Updated Late In Tx"));
    }

    private static IServiceProvider BuildServices(HttpMessageHandler handler, SqliteConnection conn, SyncSettings settings)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(conn), ServiceLifetime.Transient);
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<AuthService>();
        services.AddSingleton<SyncConfigService>();
        var subClient = new SupabaseHttpClient(new HttpClient(handler));
        subClient.Configure(settings.SupabaseUrl, settings.SupabaseApiKey);
        services.AddSingleton(subClient);
        services.AddSingleton<SyncEngine>();

        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<SyncConfigService>().SaveSettingsAsync(settings).GetAwaiter().GetResult();
        return sp;
    }

    private sealed class MockHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _fn;
        public MockHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> fn) => _fn = fn;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _fn(request);
    }
}
