using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.Sync;
using FuelPro.UI.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelPro.Tests;

[Collection("PumpConfiguration")]
public class ClientSiteShreeMahakaleshwarRemediationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public ClientSiteShreeMahakaleshwarRemediationTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _tempDir = Path.Combine(Path.GetTempPath(), "PyroSync_ClientSiteTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        PumpConfiguration.ResetToDefaults();
    }

    public void Dispose()
    {
        PumpConfiguration.ResetToDefaults();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore transient file locks on cleanup
        }
    }

    private ServiceProvider BuildServiceProvider(bool useSqlite = true)
    {
        var services = new ServiceCollection();
        if (useSqlite)
        {
            services.AddDbContext<FuelProDbContext>(
                options => options.UseSqlite($"Data Source={_dbPath}"),
                ServiceLifetime.Transient);
        }
        else
        {
            var dbName = "InMem_" + Guid.NewGuid().ToString("N");
            services.AddDbContext<FuelProDbContext>(
                options => options.UseInMemoryDatabase(dbName),
                ServiceLifetime.Transient);
        }

        services.AddTransient<ISettingsRepository, SettingsRepository>();
        services.AddTransient<IUserRepository, UserRepository>();
        services.AddTransient<IShiftRepository, ShiftRepository>();
        services.AddTransient<IDsmEntryRepository, DsmEntryRepository>();
        services.AddTransient<INozzleReadingRepository, NozzleReadingRepository>();
        services.AddTransient<IPaymentRepository, PaymentRepository>();
        services.AddTransient<IDebitEntryRepository, DebitEntryRepository>();
        services.AddTransient<ITestingEntryRepository, TestingEntryRepository>();
        services.AddTransient<IExpenseRepository, ExpenseRepository>();
        services.AddTransient<ICashDenominationRepository, CashDenominationRepository>();
        services.AddTransient<IDsmProfileRepository, DsmProfileRepository>();
        services.AddTransient<ICreditorRepaymentRepository, CreditorRepaymentRepository>();
        services.AddTransient<IAgsImportRepository, AgsImportRepository>();
        services.AddTransient<ICreditorRepository, CreditorRepository>();
        services.AddTransient<IPumpExpenseRepository, PumpExpenseRepository>();
        services.AddTransient<IDebtorVehicleRepository, DebtorVehicleRepository>();
        services.AddTransient<IDsmPersonalDebtorRepository, DsmPersonalDebtorRepository>();
        services.AddScoped<IShiftOtherCashRepository, ShiftOtherCashRepository>();
        services.AddScoped<IShiftFuelRateRepository, ShiftFuelRateRepository>();

        services.AddSingleton<IShiftCycleService, ShiftCycleService>();
        services.AddSingleton<IDsmCalculationService, DsmCalculationService>();
        services.AddSingleton<IOwnerCalculationService, OwnerCalculationService>();
        services.AddSingleton<ITidCalculationService, TidCalculationService>();
        services.AddScoped<IShiftAggregationService, ShiftAggregationService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddTransient<DsmEntryService>();
        services.AddSingleton<DraftService>();
        services.AddSingleton<IFeatureToggleService, FeatureToggleService>();
        services.AddSingleton<ICollectionTypeService, CollectionTypeService>();
        services.AddSingleton<IStationConfigurationService, StationConfigurationService>();
        services.AddSingleton<SyncConfigService>();

        return services.BuildServiceProvider();
    }

    private static void ConfigureShreeMahakaleshwarPump3Nozzles()
    {
        var now = new DateTime(2026, 11, 20);
        PumpConfiguration.InitializeFromDb(new List<PumpMapping>
        {
            new() { PumpId = 3, NozzleNumber = 8, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now },
            new() { PumpId = 3, NozzleNumber = 10, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true, CreatedAt = now },
            new() { PumpId = 3, NozzleNumber = 12, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now }
        });
    }

    private static async Task SeedShreeMahakaleshwar24NovPump3Async(FuelProDbContext context, bool includeEarlierShiftC = false)
    {
        context.Settings.Add(new Setting
        {
            PumpStationName = "Shree Mahakaleshwar Petroleum",
            HsdRate = 90.35,
            MsIRate = 103.81,
            MsIIRate = 103.81,
            CngRate = 85.0
        });

        var date24 = new DateTime(2026, 11, 24);
        if (includeEarlierShiftC)
        {
            var date22 = new DateTime(2026, 11, 22);
            var shiftC22 = new Shift { ShiftDate = date22, ShiftType = "C" };
            context.Shifts.Add(shiftC22);
        }

        var shiftA24 = new Shift { ShiftDate = date24, ShiftType = "A", CreatedAt = new DateTime(2026, 11, 24, 14, 0, 0) };
        var shiftB24 = new Shift { ShiftDate = date24, ShiftType = "B", CreatedAt = new DateTime(2026, 11, 24, 22, 0, 0) };
        context.Shifts.AddRange(shiftA24, shiftB24);
        await context.SaveChangesAsync();

        var entryA = new DsmEntry
        {
            ShiftId = shiftA24.ShiftId,
            PumpId = 3,
            DsmName = "DSM-A",
            CreatedAt = new DateTime(2026, 11, 24, 14, 5, 0),
            UpdatedAt = new DateTime(2026, 11, 24, 14, 5, 0)
        };
        var entryB = new DsmEntry
        {
            ShiftId = shiftB24.ShiftId,
            PumpId = 3,
            DsmName = "DSM-B",
            CreatedAt = new DateTime(2026, 11, 24, 22, 5, 0),
            UpdatedAt = new DateTime(2026, 11, 24, 22, 5, 0)
        };
        context.DsmEntries.AddRange(entryA, entryB);
        await context.SaveChangesAsync();

        context.NozzleReadings.AddRange(
            new NozzleReading { DsmEntryId = entryA.DsmEntryId, NozzleNumber = 8, OpeningReading = 235000.00, ClosingReading = 235296.53, Rate = 103.81 },
            new NozzleReading { DsmEntryId = entryA.DsmEntryId, NozzleNumber = 10, OpeningReading = 13600.00, ClosingReading = 13745.78, Rate = 90.35 },
            new NozzleReading { DsmEntryId = entryA.DsmEntryId, NozzleNumber = 12, OpeningReading = 132500.00, ClosingReading = 133072.75, Rate = 103.81 },

            new NozzleReading { DsmEntryId = entryB.DsmEntryId, NozzleNumber = 8, OpeningReading = 235296.53, ClosingReading = 235504.63, Rate = 103.81 },
            new NozzleReading { DsmEntryId = entryB.DsmEntryId, NozzleNumber = 10, OpeningReading = 13745.78, ClosingReading = 13805.97, Rate = 90.35 },
            new NozzleReading { DsmEntryId = entryB.DsmEntryId, NozzleNumber = 12, OpeningReading = 133072.75, ClosingReading = 133584.88, Rate = 103.81 }
        );
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task ShreeMahakaleshwar_Pump3_25Nov_ShiftA_Resolves24NovShiftB_Closings()
    {
        ConfigureShreeMahakaleshwarPump3Nozzles();
        using var sp = BuildServiceProvider(useSqlite: false);
        using var context = sp.GetRequiredService<FuelProDbContext>();
        await SeedShreeMahakaleshwar24NovPump3Async(context);

        var repo = sp.GetRequiredService<INozzleReadingRepository>();
        var result = await repo.GetPreviousShiftClosingDetailsAsync(new DateTime(2026, 11, 25), "A", 3);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Data);
        Assert.Equal(235504.63, result.Data![8].ClosingReading, 2);
        Assert.Equal(13805.97, result.Data[10].ClosingReading, 2);
        Assert.Equal(133584.88, result.Data[12].ClosingReading, 2);
        Assert.Equal("B", result.Data[8].ShiftType);
        Assert.Equal(new DateTime(2026, 11, 24), result.Data[8].ShiftDate.Date);
    }

    [Fact]
    public async Task ShreeMahakaleshwar_Pump3_25Nov_ShiftB_BeforeShiftA_Resolves24NovShiftB_Closings()
    {
        ConfigureShreeMahakaleshwarPump3Nozzles();
        using var sp = BuildServiceProvider(useSqlite: false);
        using var context = sp.GetRequiredService<FuelProDbContext>();
        await SeedShreeMahakaleshwar24NovPump3Async(context);

        var repo = sp.GetRequiredService<INozzleReadingRepository>();
        var result = await repo.GetPreviousShiftClosingsAsync(new DateTime(2026, 11, 25), "B", 3);

        Assert.True(result.Success, result.Error);
        Assert.Equal(235504.63, result.Data![8], 2);
        Assert.Equal(13805.97, result.Data[10], 2);
        Assert.Equal(133584.88, result.Data[12], 2);
    }

    [Fact]
    public async Task ShreeMahakaleshwar_Pump3_25Nov_ShiftB_AfterShiftA_Resolves25NovShiftA_Closings()
    {
        ConfigureShreeMahakaleshwarPump3Nozzles();
        using var sp = BuildServiceProvider(useSqlite: false);
        using var context = sp.GetRequiredService<FuelProDbContext>();
        await SeedShreeMahakaleshwar24NovPump3Async(context);

        var shiftA25 = new Shift { ShiftDate = new DateTime(2026, 11, 25), ShiftType = "A" };
        context.Shifts.Add(shiftA25);
        await context.SaveChangesAsync();

        var entryA25 = new DsmEntry { ShiftId = shiftA25.ShiftId, PumpId = 3, DsmName = "DSM-A25" };
        context.DsmEntries.Add(entryA25);
        await context.SaveChangesAsync();

        context.NozzleReadings.AddRange(
            new NozzleReading { DsmEntryId = entryA25.DsmEntryId, NozzleNumber = 8, OpeningReading = 235504.63, ClosingReading = 235700.00, Rate = 103.81 },
            new NozzleReading { DsmEntryId = entryA25.DsmEntryId, NozzleNumber = 10, OpeningReading = 13805.97, ClosingReading = 13850.00, Rate = 90.35 },
            new NozzleReading { DsmEntryId = entryA25.DsmEntryId, NozzleNumber = 12, OpeningReading = 133584.88, ClosingReading = 133700.00, Rate = 103.81 }
        );
        await context.SaveChangesAsync();

        var repo = sp.GetRequiredService<INozzleReadingRepository>();
        var result = await repo.GetPreviousShiftClosingDetailsAsync(new DateTime(2026, 11, 25), "B", 3);

        Assert.True(result.Success, result.Error);
        Assert.Equal(235700.00, result.Data![8].ClosingReading, 2);
        Assert.Equal(13850.00, result.Data[10].ClosingReading, 2);
        Assert.Equal(133700.00, result.Data[12].ClosingReading, 2);
        Assert.Equal("A", result.Data[8].ShiftType);
        Assert.Equal(new DateTime(2026, 11, 25), result.Data[8].ShiftDate.Date);
    }

    [Fact]
    public async Task ShreeMahakaleshwar_Pump3_ThreeShiftHistory_When24NovOnlyHasAB_Resolves24NovShiftB()
    {
        ConfigureShreeMahakaleshwarPump3Nozzles();
        using var sp = BuildServiceProvider(useSqlite: false);
        using var context = sp.GetRequiredService<FuelProDbContext>();
        await SeedShreeMahakaleshwar24NovPump3Async(context, includeEarlierShiftC: true);

        var repo = sp.GetRequiredService<INozzleReadingRepository>();
        var result = await repo.GetPreviousShiftClosingsAsync(new DateTime(2026, 11, 25), "A", 3);

        Assert.True(result.Success, result.Error);
        Assert.Equal(235504.63, result.Data![8], 2);
        Assert.Equal(13805.97, result.Data[10], 2);
        Assert.Equal(133584.88, result.Data[12], 2);
    }

    [Fact]
    public async Task DsmEntryViewModel_ShreeMahakaleshwar_Pump3_25Nov_Loads24NovShiftB_OpeningReadings()
    {
        ConfigureShreeMahakaleshwarPump3Nozzles();
        using var sp = BuildServiceProvider(useSqlite: true);
        using (var context = sp.GetRequiredService<FuelProDbContext>())
        {
            await context.Database.EnsureCreatedAsync();
            await SeedShreeMahakaleshwar24NovPump3Async(context);
        }

        var vm = new DsmEntryViewModel(sp);
        vm.SelectedDate = new DateTime(2026, 11, 25);
        vm.SelectedShift = "A";
        vm.SelectedPump = new PumpDisplayItem { PumpId = 3, DisplayText = "Pump 3" };

        var loadMethod = typeof(DsmEntryViewModel).GetMethod("LoadNozzlesForPumpAsync", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(loadMethod);
        await (Task)loadMethod!.Invoke(vm, null)!;

        var rows = vm.NozzleReadings.ToDictionary(r => r.NozzleNumber, r => r.OpeningReading);
        Assert.Equal(235504.63, rows[8] ?? 0, 2);
        Assert.Equal(13805.97, rows[10] ?? 0, 2);
        Assert.Equal(133584.88, rows[12] ?? 0, 2);
    }

    [Fact]
    public async Task MitaliStation_PreservesBcaCycleContinuity_WhenAIsClosingShiftOfDay()
    {
        ConfigureShreeMahakaleshwarPump3Nozzles();
        using var sp = BuildServiceProvider(useSqlite: false);
        using var context = sp.GetRequiredService<FuelProDbContext>();

        context.Settings.Add(new Setting { PumpStationName = "Mitali Service Station" });
        var date24 = new DateTime(2026, 11, 24);
        var shiftB = new Shift { ShiftDate = date24, ShiftType = "B" };
        var shiftA = new Shift { ShiftDate = date24, ShiftType = "A" };
        context.Shifts.AddRange(shiftB, shiftA);
        await context.SaveChangesAsync();

        var entryB = new DsmEntry { ShiftId = shiftB.ShiftId, PumpId = 3 };
        var entryA = new DsmEntry { ShiftId = shiftA.ShiftId, PumpId = 3 };
        context.DsmEntries.AddRange(entryB, entryA);
        await context.SaveChangesAsync();

        context.NozzleReadings.AddRange(
            new NozzleReading { DsmEntryId = entryB.DsmEntryId, NozzleNumber = 8, ClosingReading = 1000.0 },
            new NozzleReading { DsmEntryId = entryA.DsmEntryId, NozzleNumber = 8, ClosingReading = 2000.0 }
        );
        await context.SaveChangesAsync();

        var repo = sp.GetRequiredService<INozzleReadingRepository>();
        var resFor25B = await repo.GetPreviousShiftClosingDetailsAsync(new DateTime(2026, 11, 25), "B", 3);

        Assert.True(resFor25B.Success, resFor25B.Error);
        Assert.Equal(2000.0, resFor25B.Data![8].ClosingReading, 2);
        Assert.Equal("A", resFor25B.Data[8].ShiftType);
    }

    [Fact]
    public async Task SyncConfigService_ApplyAdminSyncSettings_PreservesUnrelatedNonPrivilegedState()
    {
        using var sp = BuildServiceProvider(useSqlite: true);
        using (var context = sp.GetRequiredService<FuelProDbContext>())
        {
            await context.Database.EnsureCreatedAsync();
        }

        var configService = sp.GetRequiredService<SyncConfigService>();
        var initialLastSync = new DateTime(2026, 11, 24, 18, 30, 0, DateTimeKind.Utc);
        var initialLastPull = new DateTime(2026, 11, 24, 18, 35, 0, DateTimeKind.Utc);

        await configService.SaveSettingsAsync(new SyncSettings
        {
            SupabaseUrl = "https://example.supabase.co",
            SupabaseApiKey = "anon-key-1",
            StationId = "SHREE_01",
            MachineId = "PC-MAIN",
            SyncEnabled = true,
            MultiComputerEnabled = false,
            IsDeviceRevoked = true,
            DeviceRevocationReason = "Admin test revocation",
            LastSyncTime = initialLastSync,
            LastPullTime = initialLastPull
        });

        var loaded = await configService.GetSettingsAsync();
        SyncConfigService.ApplyAdminSyncSettings(
            loaded,
            "https://example.supabase.co",
            "anon-key-2",
            "SHREE_01",
            "PC-MAIN",
            true,
            true,
            default);
        await configService.SaveSettingsAsync(loaded);

        var reloaded = await configService.GetSettingsAsync();
        Assert.Equal("anon-key-2", reloaded.SupabaseApiKey);
        Assert.True(reloaded.MultiComputerEnabled);
        Assert.True(reloaded.IsDeviceRevoked);
        Assert.Equal("Admin test revocation", reloaded.DeviceRevocationReason);
        Assert.Equal(initialLastSync, reloaded.LastSyncTime);
        Assert.Equal(initialLastPull, reloaded.LastPullTime);
    }

    [Fact]
    public async Task SyncConfigService_ApplyDeveloperCloudSettings_PreservesMultiComputerAndPullWatermark()
    {
        using var sp = BuildServiceProvider(useSqlite: true);
        using (var context = sp.GetRequiredService<FuelProDbContext>())
        {
            await context.Database.EnsureCreatedAsync();
        }

        var configService = sp.GetRequiredService<SyncConfigService>();
        var initialLastSync = new DateTime(2026, 11, 24, 18, 30, 0, DateTimeKind.Utc);
        var initialLastPull = new DateTime(2026, 11, 24, 18, 35, 0, DateTimeKind.Utc);

        await configService.SaveSettingsAsync(new SyncSettings
        {
            SupabaseUrl = "https://example.supabase.co",
            SupabaseApiKey = "anon-key-1",
            StationId = "SHREE_01",
            MachineId = "PC-MAIN",
            SyncEnabled = true,
            MultiComputerEnabled = true,
            IsDeviceRevoked = true,
            DeviceRevocationReason = "Preserved reason",
            LastSyncTime = initialLastSync,
            LastPullTime = initialLastPull
        });

        var loaded = await configService.GetSettingsAsync();
        SyncConfigService.ApplyDeveloperCloudSettings(
            loaded,
            "https://example.supabase.co",
            "anon-key-updated",
            string.Empty,
            "SHREE_01",
            "PC-MAIN",
            true,
            default);
        await configService.SaveSettingsAsync(loaded);

        var reloaded = await configService.GetSettingsAsync();
        Assert.Equal("anon-key-updated", reloaded.SupabaseApiKey);
        Assert.True(reloaded.MultiComputerEnabled);
        Assert.True(reloaded.IsDeviceRevoked);
        Assert.Equal("Preserved reason", reloaded.DeviceRevocationReason);
        Assert.Equal(initialLastSync, reloaded.LastSyncTime);
        Assert.Equal(initialLastPull, reloaded.LastPullTime);
    }
}
