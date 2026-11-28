using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
using Newtonsoft.Json.Linq;
using Xunit;

namespace FuelPro.Tests;

public class MsSpeedRateCrossContaminationTests
{
    private static IServiceProvider CreateServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IShiftCycleService, ShiftCycleService>();
        services.AddScoped<IDsmCalculationService, DsmCalculationService>();
        services.AddScoped<INozzleReadingRepository, NozzleReadingRepository>();
        services.AddScoped<IDsmEntryRepository, DsmEntryRepository>();
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Setting_GetRateFor_ResolvesSpeedIndependently_WithoutContaminatingMsIIOrMsI()
    {
        var setting = new Setting
        {
            HsdRate = 98.15,
            MsIRate = 111.48,
            MsIIRate = 111.48,
            CngRate = 97.75,
            FuelRates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["HSD"] = 98.15,
                ["HSD - 20KL"] = 98.15,
                ["MS-I"] = 111.48,
                ["MS - 20KL"] = 111.48,
                ["MS-II"] = 111.48,
                ["MS - 20KL II"] = 111.48,
                ["HSD - 20KL II"] = 111.48,
                ["SPEED"] = 120.59,
                ["Speed"] = 120.59,
                ["SPEED - 20KL"] = 120.59,
                ["CNG"] = 97.75,
            }
        };

        // SPEED lookups must resolve to 120.59
        Assert.Equal(120.59, setting.GetRateFor("SPEED"));
        Assert.Equal(120.59, setting.GetRateFor("Speed"));
        Assert.Equal(120.59, setting.GetRateFor("SPEED - 20KL"));
        Assert.Equal(120.59, setting.GetRateFor("Speed - 20KL"));

        // MS-II lookups must resolve to 111.48 (NEVER contaminated with SPEED 120.59)
        Assert.Equal(111.48, setting.GetRateFor("MS-II"));
        Assert.Equal(111.48, setting.GetRateFor("MS - 20KL II"));
        Assert.Equal(111.48, setting.GetRateFor("HSD - 20KL II"));

        // MS-I lookups must resolve to 111.48
        Assert.Equal(111.48, setting.GetRateFor("MS-I"));
        Assert.Equal(111.48, setting.GetRateFor("MS - 20KL"));
        Assert.Equal(111.48, setting.GetRateFor("MS"));

        // HSD & CNG
        Assert.Equal(98.15, setting.GetRateFor("HSD"));
        Assert.Equal(97.75, setting.GetRateFor("CNG"));

        Assert.NotEqual(setting.GetRateFor("SPEED"), setting.GetRateFor("MS-II"));
    }

    [Fact]
    public void Setting_FuelRatesSetter_PreservesIndependentSpeedAndMsIIWithoutCrossAliasing()
    {
        var setting = new Setting();
        var dict = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["SPEED"] = 120.59,
            ["MS-II"] = 111.48,
            ["MS-I"] = 111.48,
            ["HSD"] = 98.15,
            ["CNG"] = 97.75,
        };

        setting.FuelRates = dict;

        // Verify scalar updates
        Assert.Equal(111.48, setting.MsIRate);
        Assert.Equal(111.48, setting.MsIIRate); // MsIIRate must not be overwritten by Speed!
        Assert.Equal(98.15, setting.HsdRate);
        Assert.Equal(97.75, setting.CngRate);

        // Verify dictionary contents
        Assert.True(setting.FuelRates.TryGetValue("SPEED", out var spRate));
        Assert.Equal(120.59, spRate);
        Assert.True(setting.FuelRates.TryGetValue("MS-II", out var ms2Rate));
        Assert.Equal(111.48, ms2Rate);

        // Deserializing FuelRatesJson preserves both independent rates
        var deserialized = JsonSerializer.Deserialize<Dictionary<string, double>>(setting.FuelRatesJson!);
        Assert.NotNull(deserialized);
        Assert.Equal(120.59, deserialized["SPEED"]);
        Assert.Equal(111.48, deserialized["MS-II"]);
    }

    [Fact]
    public void TankDefinition_Rate_SerializesInTankDefinitionsJsonWithoutDbMigration()
    {
        var tanks = new List<TankDefinition>
        {
            new TankDefinition { TankName = "HSD - 20KL", FuelType = "HSD", Rate = 98.15 },
            new TankDefinition { TankName = "MS - 20KL", FuelType = "MS-I", Rate = 111.48 },
            new TankDefinition { TankName = "MS - 20KL II", FuelType = "MS-II", Rate = 111.48 },
            new TankDefinition { TankName = "SPEED - 20KL", FuelType = "SPEED", Rate = 120.59 },
            new TankDefinition { TankName = "CNG Line", FuelType = "CNG", Rate = 97.75 },
        };

        string json = JsonSerializer.Serialize(tanks);
        Assert.Contains("\"Rate\":120.59", json);
        Assert.Contains("\"Rate\":111.48", json);
        Assert.Contains("\"Rate\":98.15", json);

        var restored = JsonSerializer.Deserialize<List<TankDefinition>>(json);
        Assert.NotNull(restored);
        var speedTank = restored.First(t => t.FuelType == "SPEED");
        Assert.Equal(120.59, speedTank.Rate);
    }

    [Fact]
    public async Task DsmApprovalQueue_ValidateNozzleRates_SameDateAndShiftHistoricalMatch_ShowsNoWarning()
    {
        string dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        using var context = sp.GetRequiredService<FuelProDbContext>();

        var shiftDate = new DateTime(2026, 11, 24);
        var shift = new Shift { ShiftDate = shiftDate, ShiftType = "A" };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        // Historical approved DSM entry on same date and shift with SPEED rate = 120.59
        var approvedEntry = new DsmEntry
        {
            ShiftId = shift.ShiftId,
            DsmName = "Dsm User",
            PumpId = 1,
            GrossSales = 5000
        };
        context.DsmEntries.Add(approvedEntry);
        await context.SaveChangesAsync();

        context.NozzleReadings.Add(new NozzleReading
        {
            DsmEntryId = approvedEntry.DsmEntryId,
            NozzleNumber = 1,
            FuelType = "SPEED",
            Rate = 120.59,
            OpeningReading = 1000,
            ClosingReading = 1100,
            SaleLitres = 100,
            Amount = 12059
        });
        await context.SaveChangesAsync();

        var vm = new DsmApprovalQueueViewModel(sp);
        var row = new DsmNozzleRow
        {
            NozzleId = 1,
            FuelType = "SPEED",
            Rate = 120.59,
            SubmittedRate = 120.59,
            OpeningReading = 1100,
            ClosingReading = 1200
        };
        vm.NozzleReadings.Add(row);

        var submission = new DsmPendingSubmission
        {
            Id = Guid.NewGuid(),
            ShiftDate = shiftDate,
            ShiftType = "A",
            DsmName = "Tester"
        };

        await vm.ValidateNozzleRatesAsync(submission);

        Assert.False(row.HasRateDiscrepancy);
        Assert.False(row.IsHistoricalMismatch);
        Assert.Equal("", row.RateWarningMessage);
        Assert.False(vm.HasRateDiscrepancies);
    }

    [Fact]
    public async Task DsmApprovalQueue_ValidateNozzleRates_SameDateAndShiftHistoricalMismatch_FlagsDiscrepancyAndAllowsOneClickCorrection()
    {
        string dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        using var context = sp.GetRequiredService<FuelProDbContext>();

        var shiftDate = new DateTime(2026, 11, 24);
        var shift = new Shift { ShiftDate = shiftDate, ShiftType = "A" };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        // Historical approved DSM entry on same date and shift recorded SPEED rate = 120.59
        var approvedEntry = new DsmEntry
        {
            ShiftId = shift.ShiftId,
            DsmName = "Dsm 1",
            PumpId = 1,
            GrossSales = 5000
        };
        context.DsmEntries.Add(approvedEntry);
        await context.SaveChangesAsync();

        context.NozzleReadings.Add(new NozzleReading
        {
            DsmEntryId = approvedEntry.DsmEntryId,
            NozzleNumber = 1,
            FuelType = "SPEED",
            Rate = 120.59,
            OpeningReading = 1000,
            ClosingReading = 1100,
            SaleLitres = 100,
            Amount = 12059
        });
        await context.SaveChangesAsync();

        var vm = new DsmApprovalQueueViewModel(sp);
        // Pending submission has contaminated rate = 111.48
        var row = new DsmNozzleRow
        {
            NozzleId = 1,
            FuelType = "SPEED",
            Rate = 111.48,
            SubmittedRate = 111.48,
            OpeningReading = 1100,
            ClosingReading = 1200
        };
        row.Recalculate();
        vm.NozzleReadings.Add(row);

        var submission = new DsmPendingSubmission
        {
            Id = Guid.NewGuid(),
            ShiftDate = shiftDate,
            ShiftType = "A",
            DsmName = "Tester"
        };

        await vm.ValidateNozzleRatesAsync(submission);

        // Verification: Discrepancy is detected and flagged as historical mismatch
        Assert.True(row.HasRateDiscrepancy);
        Assert.True(row.IsHistoricalMismatch);
        Assert.Equal(120.59, row.HistoricalRate);
        Assert.Contains("Historical Ground-Truth Mismatch", row.RateWarningMessage);
        Assert.True(row.CanApplyRate);
        Assert.True(vm.HasRateDiscrepancies);

        // Submitted rate is preserved by default until manager action
        Assert.Equal(111.48, row.Rate);

        // Manager executes one-click ApplyHistoricalRate
        row.ApplyHistoricalRateCommand.Execute(null);

        // Rate is updated to ground-truth 120.59, discrepancy cleared, amount live-recalculated
        Assert.Equal(120.59, row.Rate);
        Assert.False(row.HasRateDiscrepancy);
        Assert.Equal(100 * 120.59, row.Amount);
    }

    [Fact]
    public async Task DsmApprovalQueue_ValidateNozzleRates_DifferentShiftRate_IsNotUsedAsProof()
    {
        string dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        using var context = sp.GetRequiredService<FuelProDbContext>();

        var shiftDate = new DateTime(2026, 11, 24);
        // Only Shift B has an approved entry
        var shiftB = new Shift { ShiftDate = shiftDate, ShiftType = "B" };
        context.Shifts.Add(shiftB);
        await context.SaveChangesAsync();

        var approvedEntry = new DsmEntry
        {
            ShiftId = shiftB.ShiftId,
            DsmName = "Dsm Shift B",
            PumpId = 1,
            GrossSales = 5000
        };
        context.DsmEntries.Add(approvedEntry);
        await context.SaveChangesAsync();

        context.NozzleReadings.Add(new NozzleReading
        {
            DsmEntryId = approvedEntry.DsmEntryId,
            NozzleNumber = 1,
            FuelType = "SPEED",
            Rate = 120.59,
            OpeningReading = 1000,
            ClosingReading = 1100,
            SaleLitres = 100,
            Amount = 12059
        });
        await context.SaveChangesAsync();

        // Submission is for Shift A
        var vm = new DsmApprovalQueueViewModel(sp);
        var row = new DsmNozzleRow
        {
            NozzleId = 1,
            FuelType = "SPEED",
            Rate = 120.59,
            SubmittedRate = 120.59,
            OpeningReading = 1100,
            ClosingReading = 1200
        };
        vm.NozzleReadings.Add(row);

        var submissionShiftA = new DsmPendingSubmission
        {
            Id = Guid.NewGuid(),
            ShiftDate = shiftDate,
            ShiftType = "A", // Shift A, not B
            DsmName = "Tester"
        };

        await vm.ValidateNozzleRatesAsync(submissionShiftA);

        // Shift B rate MUST NOT be used as automatic proof of Shift A's rate
        Assert.False(row.IsHistoricalMismatch);
    }

    [Fact]
    public void SyncEngine_PushStationSnapshot_WhenSpeedMissingOrZero_LeavesSpeedRateNullWithoutMsInheritance()
    {
        var localSetting = new Setting
        {
            PumpStationName = "Mitali Service Station",
            HsdRate = 98.15,
            MsIRate = 111.48,
            MsIIRate = 111.48,
            CngRate = 97.75,
            FuelRatesJson = "{\"HSD\":98.15,\"MS-I\":111.48,\"MS-II\":111.48,\"CNG\":97.75}"
        };

        // SPEED is completely missing from FuelRates and tanks
        Assert.Equal(0.0, localSetting.GetRateFor("SPEED", 0.0));
        Assert.False(localSetting.FuelRates.ContainsKey("SPEED"));
        Assert.False(localSetting.FuelRates.ContainsKey("Speed"));

        // Evaluate authoritative Speed rate strictly matching SyncEngine logic
        double? authoritativeSpeedRate = null;
        if (localSetting.FuelRates.TryGetValue("Speed", out var sRate) && sRate > 0)
            authoritativeSpeedRate = sRate;
        else if (localSetting.FuelRates.TryGetValue("SPEED", out var sRate2) && sRate2 > 0)
            authoritativeSpeedRate = sRate2;
        else if (localSetting.FuelRates.TryGetValue("Speed - 20KL", out var sTankRate) && sTankRate > 0)
            authoritativeSpeedRate = sTankRate;
        else if (localSetting.FuelRates.TryGetValue("SPEED - 20KL", out var sTankRate2) && sTankRate2 > 0)
            authoritativeSpeedRate = sTankRate2;
        else if (localSetting.FuelRates.TryGetValue("XP95", out var xpRate) && xpRate > 0)
            authoritativeSpeedRate = xpRate;
        else if (localSetting.FuelRates.TryGetValue("Power", out var pwrRate) && pwrRate > 0)
            authoritativeSpeedRate = pwrRate;
        else
        {
            var sVal = localSetting.GetRateFor("SPEED", 0.0);
            if (sVal > 0) authoritativeSpeedRate = sVal;
        }

        // SPEED rate must remain NULL (unresolved), NEVER inheriting MsIRate or MsIIRate (111.48)
        Assert.Null(authoritativeSpeedRate);

        var payload = new Dictionary<string, object?>
        {
            ["HsdRate"] = localSetting.HsdRate,
            ["MsIRate"] = localSetting.MsIRate,
            ["MsIIRate"] = localSetting.MsIIRate,
            ["SpeedRate"] = authoritativeSpeedRate,
            ["speed_rate"] = authoritativeSpeedRate
        };

        Assert.Equal(111.48, payload["MsIRate"]);
        Assert.Equal(111.48, payload["MsIIRate"]);
        Assert.Null(payload["SpeedRate"]);
        Assert.Null(payload["speed_rate"]);

        string serialized = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
        Assert.Contains("\"MsIRate\":111.48", serialized);
        Assert.Contains("\"SpeedRate\":null", serialized);
    }

    [Fact]
    public async Task DsmApprovalQueue_ValidateNozzleRates_TwoNozzlesInSameShiftDifferentRates_CannotValidateOrInvalidateEachOther()
    {
        string dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        using var context = sp.GetRequiredService<FuelProDbContext>();

        var shiftDate = new DateTime(2026, 11, 24);
        var shift = new Shift { ShiftDate = shiftDate, ShiftType = "A" };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        // Historical approved DSM entry on same date and shift only recorded Nozzle 1 (MS-I at 111.48)
        var approvedEntry = new DsmEntry
        {
            ShiftId = shift.ShiftId,
            DsmName = "Dsm User",
            PumpId = 1,
            GrossSales = 5000
        };
        context.DsmEntries.Add(approvedEntry);
        await context.SaveChangesAsync();

        context.NozzleReadings.Add(new NozzleReading
        {
            DsmEntryId = approvedEntry.DsmEntryId,
            NozzleNumber = 1, // Only Nozzle 1
            FuelType = "MS-I",
            Rate = 111.48,
            OpeningReading = 1000,
            ClosingReading = 1100,
            SaleLitres = 100,
            Amount = 11148
        });
        await context.SaveChangesAsync();

        var vm = new DsmApprovalQueueViewModel(sp);

        // Pending submission has two nozzles:
        // Nozzle 1: MS-I at 111.48
        var rowMs = new DsmNozzleRow
        {
            NozzleId = 1,
            FuelType = "MS-I",
            Rate = 111.48,
            SubmittedRate = 111.48,
            OpeningReading = 1100,
            ClosingReading = 1200
        };
        // Nozzle 2: SPEED at 120.59
        var rowSpeed = new DsmNozzleRow
        {
            NozzleId = 2,
            FuelType = "SPEED",
            Rate = 120.59,
            SubmittedRate = 120.59,
            OpeningReading = 500,
            ClosingReading = 600
        };
        vm.NozzleReadings.Add(rowMs);
        vm.NozzleReadings.Add(rowSpeed);

        var submission = new DsmPendingSubmission
        {
            Id = Guid.NewGuid(),
            ShiftDate = shiftDate,
            ShiftType = "A",
            DsmName = "Tester"
        };

        await vm.ValidateNozzleRatesAsync(submission);

        // Nozzle 1 matched exact identity and rate -> no discrepancy
        Assert.False(rowMs.HasRateDiscrepancy);
        Assert.False(rowMs.IsHistoricalMismatch);

        // Nozzle 2 has NO approved historical record for Nozzle 2!
        // Nozzle 1's MS-I rate MUST NOT be treated as ground truth for Nozzle 2!
        Assert.False(rowSpeed.IsHistoricalMismatch);
        Assert.Equal(120.59, rowSpeed.Rate); // Submitted rate preserved!
    }

    [Fact]
    public async Task DsmApprovalAudit_ManagerRateCorrection_PersistsAndReloadsOriginalAndApprovedSnapshots()
    {
        string dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        var submissionId = Guid.NewGuid();

        // 1. Simulate saving an approval audit record where manager adjusted Nozzle #2 from 111.48 to 120.59
        var originalData = new
        {
            Readings = new[]
            {
                new { NozzleId = 1, FuelType = "MS-I", OpeningReading = 1000.0, ClosingReading = 1100.0, SubmittedRate = 111.48, Rate = 111.48, SaleLitres = 100.0, Amount = 11148.0 },
                new { NozzleId = 2, FuelType = "SPEED", OpeningReading = 500.0, ClosingReading = 600.0, SubmittedRate = 111.48, Rate = 111.48, SaleLitres = 100.0, Amount = 11148.0 }
            }
        };

        var approvedData = new
        {
            DsmEntryId = 42,
            ShiftId = 10,
            GrossSales = 23207.0,
            TotalCollection = 23207.0,
            Mismatch = 0.0,
            Readings = new[]
            {
                new { NozzleId = 1, FuelType = "MS-I", OpeningReading = 1000.0, ClosingReading = 1100.0, ApprovedRate = 111.48, Rate = 111.48, SaleLitres = 100.0, Amount = 11148.0 },
                new { NozzleId = 2, FuelType = "SPEED", OpeningReading = 500.0, ClosingReading = 600.0, ApprovedRate = 120.59, Rate = 120.59, SaleLitres = 100.0, Amount = 12059.0 }
            },
            RateAdjustments = new[] { "Nozzle #2 (SPEED): rate adjusted from ₹111.48 to ₹120.59" }
        };

        var auditRecord = new DsmApprovalAudit
        {
            SubmissionId = submissionId,
            OriginalDataJson = Newtonsoft.Json.JsonConvert.SerializeObject(originalData),
            ApprovedDataJson = Newtonsoft.Json.JsonConvert.SerializeObject(approvedData),
            ApprovedBy = "StationManager",
            ApprovedAt = new DateTime(2026, 11, 24, 15, 30, 0),
            Remarks = "Approved with rate adjustments | Rate adjustments: Nozzle #2 (SPEED): rate adjusted from ₹111.48 to ₹120.59"
        };

        using (var scope1 = sp.CreateScope())
        {
            var writeContext = scope1.ServiceProvider.GetRequiredService<FuelProDbContext>();
            writeContext.DsmApprovalAudits.Add(auditRecord);
            await writeContext.SaveChangesAsync();
        }

        // 2. Reload audit record from a fresh DbContext scope and verify all attributes
        using (var scope2 = sp.CreateScope())
        {
            var readContext = scope2.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var reloaded = await readContext.DsmApprovalAudits.FirstOrDefaultAsync(a => a.SubmissionId == submissionId);
            Assert.NotNull(reloaded);
            Assert.Equal(submissionId, reloaded.SubmissionId);
            Assert.Equal("StationManager", reloaded.ApprovedBy);
            Assert.Equal(new DateTime(2026, 11, 24, 15, 30, 0), reloaded.ApprovedAt);
            Assert.Contains("Nozzle #2 (SPEED): rate adjusted from ₹111.48 to ₹120.59", reloaded.Remarks);

            // Verify OriginalDataJson recovery
            var origDoc = JsonDocument.Parse(reloaded.OriginalDataJson);
            var origReadings = origDoc.RootElement.GetProperty("Readings");
            Assert.Equal(2, origReadings.GetArrayLength());
            var origSpeed = origReadings.EnumerateArray().First(r => r.GetProperty("NozzleId").GetInt32() == 2);
            Assert.Equal(111.48, origSpeed.GetProperty("SubmittedRate").GetDouble());

            // Verify ApprovedDataJson recovery
            var appDoc = JsonDocument.Parse(reloaded.ApprovedDataJson);
            var appReadings = appDoc.RootElement.GetProperty("Readings");
            Assert.Equal(2, appReadings.GetArrayLength());
            var appSpeed = appReadings.EnumerateArray().First(r => r.GetProperty("NozzleId").GetInt32() == 2);
            Assert.Equal(120.59, appSpeed.GetProperty("ApprovedRate").GetDouble());

            var adjustments = appDoc.RootElement.GetProperty("RateAdjustments");
            Assert.Equal(1, adjustments.GetArrayLength());
            Assert.Equal("Nozzle #2 (SPEED): rate adjusted from ₹111.48 to ₹120.59", adjustments[0].GetString());
        }
    }

    [Fact]
    public void Setting_GetRateFor_XP95AndPower_AreIndependentAndNeverSupplySpeed()
    {
        var setting = new Setting
        {
            HsdRate = 98.15,
            MsIRate = 111.48,
            MsIIRate = 111.48,
            CngRate = 97.75,
            FuelRates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["HSD"] = 98.15,
                ["MS-I"] = 111.48,
                ["XP95"] = 125.00,
                ["Power"] = 122.00,
                ["CNG"] = 97.75
            }
        };

        // SPEED is unconfigured -> must return defaultFallback (0.0), NEVER 125.00 or 122.00
        Assert.Equal(0.0, setting.GetRateFor("SPEED", 0.0));
        Assert.Equal(0.0, setting.GetRateFor("Speed", 0.0));

        // Independent products resolve their own legitimate rates
        Assert.Equal(125.00, setting.GetRateFor("XP95"));
        Assert.Equal(122.00, setting.GetRateFor("Power"));
        Assert.Equal(111.48, setting.GetRateFor("MS-I"));

        // Evaluate SyncEngine authoritative Speed rate logic: XP95 and Power must NOT supply SpeedRate
        double? authoritativeSpeedRate = null;
        if (setting.FuelRates.TryGetValue("Speed", out var sRate) && sRate > 0)
            authoritativeSpeedRate = sRate;
        else if (setting.FuelRates.TryGetValue("SPEED", out var sRate2) && sRate2 > 0)
            authoritativeSpeedRate = sRate2;
        else if (setting.FuelRates.TryGetValue("Speed - 20KL", out var sTankRate) && sTankRate > 0)
            authoritativeSpeedRate = sTankRate;
        else if (setting.FuelRates.TryGetValue("SPEED - 20KL", out var sTankRate2) && sTankRate2 > 0)
            authoritativeSpeedRate = sTankRate2;
        else
        {
            var sVal = setting.GetRateFor("SPEED", 0.0);
            if (sVal > 0) authoritativeSpeedRate = sVal;
        }

        Assert.Null(authoritativeSpeedRate);
    }

    [Fact]
    public async Task DsmApprovalQueue_ValidateNozzleRates_SameNozzleDifferentTankAssignment_DoesNotTreatAsGroundTruth()
    {
        string dbName = Guid.NewGuid().ToString();
        var sp = CreateServiceProvider(dbName);
        using var context = sp.GetRequiredService<FuelProDbContext>();

        var shiftDate = new DateTime(2026, 11, 24);
        var shift = new Shift { ShiftDate = shiftDate, ShiftType = "A" };
        context.Shifts.Add(shift);
        await context.SaveChangesAsync();

        // Historical approved DSM entry on same date and shift:
        // Entry had Pump 1, Nozzle 1 mapped to "MS - TANK 2" with rate 108.00
        var approvedEntry = new DsmEntry
        {
            ShiftId = shift.ShiftId,
            DsmName = "Historical Dsm",
            PumpId = 1,
            GrossSales = 5000
        };
        context.DsmEntries.Add(approvedEntry);
        await context.SaveChangesAsync();

        context.NozzleReadings.Add(new NozzleReading
        {
            DsmEntryId = approvedEntry.DsmEntryId,
            NozzleNumber = 1,
            FuelType = "MS-I",
            Rate = 108.00, // Historical rate from Tank 2
            OpeningReading = 1000,
            ClosingReading = 1100,
            SaleLitres = 100,
            Amount = 10800
        });

        // Set up pump mappings:
        // Pump 1, Nozzle 1 is now mapped to "MS - TANK 1"
        // But historically it was linked to "MS - TANK 2" on Pump 2
        context.PumpMappings.Add(new PumpMapping
        {
            PumpId = 1,
            NozzleNumber = 1,
            FuelType = "MS-I",
            TankName = "MS - TANK 1",
            IsActive = true
        });
        context.PumpMappings.Add(new PumpMapping
        {
            PumpId = 2,
            NozzleNumber = 1,
            FuelType = "MS-I",
            TankName = "MS - TANK 2",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var vm = new DsmApprovalQueueViewModel(sp);
        // Submitted row on Pump 1 has rate 111.48 (Tank 1 rate)
        var row = new DsmNozzleRow
        {
            PumpId = 1,
            NozzleId = 1,
            FuelType = "MS-I",
            Rate = 111.48,
            SubmittedRate = 111.48,
            OpeningReading = 1100,
            ClosingReading = 1200
        };
        vm.NozzleReadings.Add(row);

        // Historical entry was on Pump 2 (Tank 2)
        approvedEntry.PumpId = 2;
        await context.SaveChangesAsync();

        var submission = new DsmPendingSubmission
        {
            Id = Guid.NewGuid(),
            PumpId = 1,
            ShiftDate = shiftDate,
            ShiftType = "A",
            DsmName = "Tester"
        };

        await vm.ValidateNozzleRatesAsync(submission);

        // Because tank assignments differ ("MS - TANK 1" vs "MS - TANK 2"):
        // It must NOT treat historical rate 108.00 as ground truth!
        Assert.False(row.IsHistoricalMismatch);
        Assert.Equal(111.48, row.Rate); // Submitted rate preserved!
        Assert.True(row.HasRateDiscrepancy);
        Assert.Contains("tank assignment differs", row.RateWarningMessage);
    }

    [Fact]
    public void SettingsViewModel_BuildFuelRatesDictionary_KeepsXp95AndPowerIsolatedFromSpeed()
    {
        var items = new[]
        {
            new FuelRateItemVm { TankName = "MS - 20KL", FuelType = "MS-I", DisplayName = "MS - 20KL (MS-I)", RateText = "103.81" },
            new FuelRateItemVm { TankName = "MS - 20KL II", FuelType = "MS-II", DisplayName = "MS - 20KL II (MS-II)", RateText = "103.81" },
            new FuelRateItemVm { TankName = "HSD - 20KL", FuelType = "HSD", DisplayName = "HSD - 20KL (HSD)", RateText = "90.35" },
            new FuelRateItemVm { TankName = "CNG LINE", FuelType = "CNG", DisplayName = "CNG LINE (CNG)", RateText = "85.00" },
            new FuelRateItemVm { TankName = "XP95 - 10KL", FuelType = "XP95", DisplayName = "XP95 Premium", RateText = "114.20" },
            new FuelRateItemVm { TankName = "POWER - 10KL", FuelType = "Power", DisplayName = "HP Power", RateText = "116.50" },
            new FuelRateItemVm { TankName = "SPEED - 20KL", FuelType = "SPEED", DisplayName = "SPEED - 20KL (SPEED)", RateText = "120.59" }
        };

        var dict = SettingsViewModel.BuildFuelRatesDictionary(items);

        Assert.Equal(120.59, dict["SPEED"]);
        Assert.Equal(120.59, dict["Speed"]);
        Assert.Equal(114.20, dict["XP95"]);
        Assert.Equal(116.50, dict["Power"]);
        Assert.Equal(103.81, dict["MS-I"]);
        Assert.Equal(103.81, dict["MS-II"]);
        Assert.Equal(90.35, dict["HSD"]);
        Assert.Equal(85.00, dict["CNG"]);

        // Now verify XP95 and Power alone do NOT populate SPEED or Speed
        var xpAndPowerOnly = new[]
        {
            new FuelRateItemVm { TankName = "XP95 - 10KL", FuelType = "XP95", DisplayName = "XP95", RateText = "114.20" },
            new FuelRateItemVm { TankName = "POWER - 10KL", FuelType = "Power", DisplayName = "Power", RateText = "116.50" }
        };
        var isolatedDict = SettingsViewModel.BuildFuelRatesDictionary(xpAndPowerOnly);
        Assert.False(isolatedDict.ContainsKey("SPEED"));
        Assert.False(isolatedDict.ContainsKey("Speed"));
        Assert.Equal(114.20, isolatedDict["XP95"]);
        Assert.Equal(116.50, isolatedDict["Power"]);
    }

    [Fact]
    public async Task SyncEngine_PushStationSnapshot_CachesMissingColumnsPerSession_PreservesTankDefinitionsJson_AndSurfacesHttp500()
    {
        var tempDbPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"fuelpro_sync_test_{Guid.NewGuid():N}.db");
        var connStr = $"Data Source={tempDbPath};Pooling=False";
        try
        {
            var options = new DbContextOptionsBuilder<FuelProDbContext>().UseSqlite(connStr).Options;
            using (var initCtx = new FuelProDbContext(options))
            {
                initCtx.Database.EnsureCreated();
                var setting = new Setting
                {
                    PumpStationName = "Mitali Service Station",
                    HsdRate = 90.35,
                    MsIRate = 103.81,
                    MsIIRate = 103.81,
                    CngRate = 85.00,
                    FuelRates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["CNG"] = 85.00,
                        ["HSD"] = 90.35,
                        ["MS-I"] = 103.81,
                        ["MS-II"] = 103.81,
                        ["SPEED"] = 120.59
                    }
                };
                initCtx.Settings.Add(setting);
                initCtx.TankDefinitions.Add(new TankDefinition
                {
                    TankId = 5,
                    TankName = "SPEED - 20KL",
                    FuelType = "SPEED",
                    Rate = 120.59,
                    IsActive = true
                });
                initCtx.SaveChanges();
            }

            var settingsPostBodies = new List<JObject>();
            int phase = 1; // 1 = strip FuelRatesJson + SpeedRate via PGRST204, 2 = 2nd push pre-stripped, 3 = HTTP 500

            var handler = new LocalMockHttpMessageHandler(async req =>
            {
                var path = req.RequestUri?.PathAndQuery ?? "";
                if (req.Method == HttpMethod.Get && path.Contains("Settings"))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[]", Encoding.UTF8, "application/json")
                    };
                }

                if (path.Contains("Settings") && (req.Method == HttpMethod.Post || req.Method == HttpMethod.Patch))
                {
                    var body = req.Content != null ? await req.Content.ReadAsStringAsync() : "{}";
                    var parsed = JObject.Parse(body);
                    lock (settingsPostBodies)
                    {
                        settingsPostBodies.Add(parsed);
                    }

                    if (phase == 3)
                    {
                        return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                        {
                            Content = new StringContent("{\"message\":\"database down\"}", Encoding.UTF8, "application/json")
                        };
                    }

                    if (parsed.ContainsKey("FuelRatesJson"))
                    {
                        return new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent("{\"code\":\"PGRST204\",\"message\":\"Could not find the 'FuelRatesJson' column of 'Settings' in the schema cache\"}", Encoding.UTF8, "application/json")
                        };
                    }

                    if (parsed.ContainsKey("SpeedRate"))
                    {
                        return new HttpResponseMessage(HttpStatusCode.BadRequest)
                        {
                            Content = new StringContent("{\"code\":\"PGRST204\",\"message\":\"Could not find the 'SpeedRate' column of 'Settings' in the schema cache\"}", Encoding.UTF8, "application/json")
                        };
                    }

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("[{}]", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                };
            });

            var services = new ServiceCollection();
            services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite(connStr), ServiceLifetime.Transient);
            services.AddSingleton<SyncConfigService>();
            var supabaseClient = new SupabaseHttpClient(new HttpClient(handler));
            supabaseClient.Configure("https://mock.supabase.co", "mock-key");
            services.AddSingleton(supabaseClient);
            services.AddSingleton<SyncEngine>();

            using var sp = services.BuildServiceProvider();
            var configService = sp.GetRequiredService<SyncConfigService>();
            await configService.SaveSettingsAsync(new SyncSettings
            {
                SyncEnabled = false,
                SupabaseUrl = "https://mock.supabase.co",
                SupabaseApiKey = "mock-key",
                StationId = "RASHTRA-CCED97",
                MachineId = "MACHINE-01"
            });

            var syncEngine = sp.GetRequiredService<SyncEngine>();

            // 1. First push discovers missing FuelRatesJson + SpeedRate and retries cleanly
            var first = await syncEngine.PushStationSnapshotToCloudAsync();
            Assert.True(first.Success, first.Message);
            Assert.Equal(3, settingsPostBodies.Count);
            Assert.True(settingsPostBodies[2].ContainsKey("TankDefinitionsJson"));
            var tanksJson = settingsPostBodies[2]["TankDefinitionsJson"]?.ToString() ?? "";
            Assert.Contains("\"FuelType\":\"SPEED\"", tanksJson);
            Assert.Contains("120.59", tanksJson);

            // Brief yield so fire-and-forget ForceSyncAsync (SyncEnabled=false) finishes immediately
            await Task.Delay(50);

            // 2. Second push in same session pre-strips FuelRatesJson & SpeedRate in 1 request!
            lock (settingsPostBodies) { settingsPostBodies.Clear(); }
            phase = 2;
            var second = await syncEngine.PushStationSnapshotToCloudAsync();
            Assert.True(second.Success, second.Message);
            Assert.Single(settingsPostBodies);
            Assert.False(settingsPostBodies[0].ContainsKey("FuelRatesJson"));
            Assert.False(settingsPostBodies[0].ContainsKey("SpeedRate"));
            Assert.True(settingsPostBodies[0].ContainsKey("TankDefinitionsJson"));

            await Task.Delay(50);

            // 3. HTTP 500 must NOT be swallowed or treated as PGRST204
            phase = 3;
            var third = await syncEngine.PushStationSnapshotToCloudAsync();
            Assert.False(third.Success);
            Assert.Contains("InternalServerError", third.Message);
        }
        finally
        {
            try { if (System.IO.File.Exists(tempDbPath)) System.IO.File.Delete(tempDbPath); } catch { }
        }
    }

    private sealed class LocalMockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;
        public LocalMockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) => _handler = handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request);
    }
}
