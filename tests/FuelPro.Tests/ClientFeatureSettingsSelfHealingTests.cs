using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Services;
using FuelPro.UI.ViewModels;
using Xunit;

namespace FuelPro.Tests;

public class ClientFeatureSettingsSelfHealingTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public ClientFeatureSettingsSelfHealingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_FeatureTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "fuelPro_test.db");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"),
            ServiceLifetime.Transient);

        services.AddSingleton<IFeatureToggleService, FeatureToggleService>();
        return services.BuildServiceProvider();
    }

    private async Task EnsureBaseSchemaAsync(FuelProDbContext db)
    {
        var prodDbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro", "fuelPro.db");
        if (File.Exists(prodDbPath))
        {
            await db.Database.CloseConnectionAsync();
            File.Copy(prodDbPath, _dbPath, true);
            await db.Database.OpenConnectionAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }

        // Reset AppFeatureSettings and AppFeatureSettingsJson for clean test isolation
        await db.Database.ExecuteSqlRawAsync("DELETE FROM AppFeatureSettings;");
        await db.Database.ExecuteSqlRawAsync("UPDATE Settings SET AppFeatureSettingsJson = NULL WHERE SettingId = 1;");
    }

    [Fact]
    public async Task A_EmptyLocalTable_AutoSeedsCanonical34Features()
    {
        using var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        await EnsureBaseSchemaAsync(db);

        var service = sp.GetRequiredService<IFeatureToggleService>();

        // Act
        var features = await service.GetAllFeaturesAsync();

        // Assert
        Assert.NotNull(features);
        Assert.Equal(34, features.Count);

        var managerCount = features.Count(f => f.TargetRole == "Manager");
        var ownerCount = features.Count(f => f.TargetRole == "Owner");
        var globalCount = features.Count(f => f.TargetRole == "Global");

        Assert.Equal(16, managerCount);
        Assert.Equal(15, ownerCount);
        Assert.Equal(3, globalCount);

        // Verify DB persisted
        var dbCount = await db.AppFeatureSettings.CountAsync();
        Assert.Equal(34, dbCount);

        // Verify Settings.AppFeatureSettingsJson was populated
        var setting = await db.Settings.FirstOrDefaultAsync();
        Assert.NotNull(setting?.AppFeatureSettingsJson);
        Assert.Contains("Admin_DsmEntry", setting.AppFeatureSettingsJson);
    }

    [Fact]
    public async Task B_ExistingLocalRecords_ReturnsUnchangedWithoutReseeding()
    {
        using var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        await EnsureBaseSchemaAsync(db);

        // Seed 3 specific custom features
        db.AppFeatureSettings.AddRange(
            new AppFeatureSetting { FeatureKey = "Custom_1", DisplayName = "Custom 1", TargetRole = "Manager", IsEnabled = true, DisplayOrder = 1 },
            new AppFeatureSetting { FeatureKey = "Custom_2", DisplayName = "Custom 2", TargetRole = "Owner", IsEnabled = false, DisplayOrder = 2 },
            new AppFeatureSetting { FeatureKey = "Custom_3", DisplayName = "Custom 3", TargetRole = "Global", IsEnabled = true, DisplayOrder = 3 }
        );
        await db.SaveChangesAsync();

        var service = sp.GetRequiredService<IFeatureToggleService>();

        // Act
        var features = await service.GetAllFeaturesAsync();

        // Assert: must return the 3 existing features unchanged
        Assert.Equal(3, features.Count);
        Assert.Contains(features, f => f.FeatureKey == "Custom_1");
        Assert.Contains(features, f => f.FeatureKey == "Custom_2" && !f.IsEnabled);
        Assert.Contains(features, f => f.FeatureKey == "Custom_3");

        var dbCount = await db.AppFeatureSettings.CountAsync();
        Assert.Equal(3, dbCount);
    }

    [Fact]
    public async Task C_ExistingCustomizedIsEnabledValues_Preserved()
    {
        using var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        await EnsureBaseSchemaAsync(db);

        // Seed canonical defaults, but disable Admin_CardSettlement and Owner_Reports
        var canonical = AppFeatureSetting.GetCanonicalDefaults(DateTime.Now);
        var card = canonical.First(f => f.FeatureKey == "Admin_CardSettlement");
        card.IsEnabled = false;
        var reports = canonical.First(f => f.FeatureKey == "Owner_Reports");
        reports.IsEnabled = false;

        db.AppFeatureSettings.AddRange(canonical);
        await db.SaveChangesAsync();

        var service = sp.GetRequiredService<IFeatureToggleService>();

        // Act
        var features = await service.GetAllFeaturesAsync();

        // Assert
        Assert.Equal(34, features.Count);
        var dbCard = features.First(f => f.FeatureKey == "Admin_CardSettlement");
        Assert.False(dbCard.IsEnabled, "Customized disabled state for Admin_CardSettlement must be preserved");

        var dbReports = features.First(f => f.FeatureKey == "Owner_Reports");
        Assert.False(dbReports.IsEnabled, "Customized disabled state for Owner_Reports must be preserved");
    }

    [Fact]
    public async Task D_ValidAppFeatureSettingsJson_RestoredInPreferenceToDefaults()
    {
        using var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        await EnsureBaseSchemaAsync(db);

        // Create cloud snapshot with 34 features where Admin_PettyCash is disabled and Owner_SalaryCalculation has custom name
        var canonical = AppFeatureSetting.GetCanonicalDefaults(DateTime.Now);
        canonical.First(f => f.FeatureKey == "Admin_PettyCash").IsEnabled = false;
        canonical.First(f => f.FeatureKey == "Owner_SalaryCalculation").DisplayName = "Custom Payroll Name";

        var setting = await db.Settings.FirstAsync();
        setting.AppFeatureSettingsJson = JsonSerializer.Serialize(canonical);
        await db.SaveChangesAsync();

        var service = sp.GetRequiredService<IFeatureToggleService>();

        // Act: Table is empty, but valid JSON exists
        var features = await service.GetAllFeaturesAsync();

        // Assert
        Assert.Equal(34, features.Count);
        var petty = features.First(f => f.FeatureKey == "Admin_PettyCash");
        Assert.False(petty.IsEnabled, "State from valid JSON snapshot must be restored");

        var salary = features.First(f => f.FeatureKey == "Owner_SalaryCalculation");
        Assert.Equal("Custom Payroll Name", salary.DisplayName);
    }

    [Fact]
    public async Task E_Historical32FeatureJson_RestoresAndBackfillsTo34()
    {
        using var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        await EnsureBaseSchemaAsync(db);

        // Historical 32 features (without Operations_CrossDsmQr and Operations_PersonalLedger)
        var historical32 = AppFeatureSetting.GetCanonicalDefaults(DateTime.Now)
            .Where(f => f.FeatureKey != "Operations_CrossDsmQr" && f.FeatureKey != "Operations_PersonalLedger")
            .ToList();

        Assert.Equal(32, historical32.Count);

        // Customise one feature in historical JSON
        historical32.First(f => f.FeatureKey == "Admin_DayTotal").IsEnabled = false;

        var setting = await db.Settings.FirstAsync();
        setting.AppFeatureSettingsJson = JsonSerializer.Serialize(historical32);
        await db.SaveChangesAsync();

        var service = sp.GetRequiredService<IFeatureToggleService>();

        // Act
        var features = await service.GetAllFeaturesAsync();

        // Assert
        Assert.Equal(34, features.Count);

        // Preserved customized state
        Assert.False(features.First(f => f.FeatureKey == "Admin_DayTotal").IsEnabled);

        // Backfilled missing keys
        Assert.Contains(features, f => f.FeatureKey == "Operations_CrossDsmQr" && f.IsEnabled);
        Assert.Contains(features, f => f.FeatureKey == "Operations_PersonalLedger" && f.IsEnabled);
    }

    [Fact]
    public async Task F_InvalidJson_FallsBackToCanonical34Defaults()
    {
        using var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        await EnsureBaseSchemaAsync(db);

        var setting = await db.Settings.FirstAsync();
        setting.AppFeatureSettingsJson = "NOT_A_VALID_JSON_STRING{{{";
        await db.SaveChangesAsync();

        var service = sp.GetRequiredService<IFeatureToggleService>();

        // Act
        var features = await service.GetAllFeaturesAsync();

        // Assert: falls back to canonical 34 defaults
        Assert.Equal(34, features.Count);
        Assert.Contains(features, f => f.FeatureKey == "Admin_DsmEntry");
    }

    [Fact]
    public async Task G_DuplicateFeatureKeyJson_RejectedAndHandledSafely()
    {
        using var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        await EnsureBaseSchemaAsync(db);

        var duplicateJson = @"[
            { ""FeatureKey"": ""Admin_DsmEntry"", ""TargetRole"": ""Manager"", ""DisplayName"": ""Entry 1"" },
            { ""FeatureKey"": ""Admin_DsmEntry"", ""TargetRole"": ""Manager"", ""DisplayName"": ""Entry 2"" }
        ]";

        var setting = await db.Settings.FirstAsync();
        setting.AppFeatureSettingsJson = duplicateJson;
        await db.SaveChangesAsync();

        var service = sp.GetRequiredService<IFeatureToggleService>();

        // Act
        var features = await service.GetAllFeaturesAsync();

        // Assert: duplicate payload rejected, falls back to 34 canonical defaults
        Assert.Equal(34, features.Count);
        Assert.Equal(1, features.Count(f => f.FeatureKey == "Admin_DsmEntry"));
    }

    [Fact]
    public async Task H_LegacyMigrationBaseline_SuccessfullyBaselinesAndAllowsSubsequentMigrations()
    {
        using var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        // Copy exact legacy database state to test path
        var prodDbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro", "fuelPro.db");
        if (File.Exists(prodDbPath))
        {
            await db.Database.CloseConnectionAsync();
            File.Copy(prodDbPath, _dbPath, true);
            await db.Database.OpenConnectionAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }

        // Ensure __EFMigrationsHistory is empty to reproduce legacy condition
        await db.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE IF NOT EXISTS ""__EFMigrationsHistory"" (
                ""MigrationId"" TEXT NOT NULL PRIMARY KEY,
                ""ProductVersion"" TEXT NOT NULL
            );
            DELETE FROM ""__EFMigrationsHistory"";
        ");

        // Verify history is empty before
        var preApplied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Empty(preApplied);

        // Act: Execute SeedData baseline
        await SeedData.BaselineLegacyMigrationsIfRequiredAsync(db);

        // Assert: migrations are baselined
        var postApplied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.NotEmpty(postApplied);
        Assert.Contains("20260421082012_InitialCreate", postApplied);
        Assert.Contains("20260721090022_AddKhandharePetroleumEntries", postApplied);

        // Verify MigrateAsync() now completes with 0 errors without table collision
        await db.Database.MigrateAsync();

        // Verify existing settings and user records are preserved
        var setting = await db.Settings.FirstOrDefaultAsync();
        Assert.NotNull(setting);
        Assert.False(string.IsNullOrWhiteSpace(setting.PumpStationName));
    }

    [Fact]
    public async Task I_DeveloperViewModel_LoadFeaturesAsync_PopulatesFilteredCollectionsAndReportsCount()
    {
        using var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        await EnsureBaseSchemaAsync(db);

        var service = sp.GetRequiredService<IFeatureToggleService>();
        var vm = new DeveloperMainWindowViewModel(service);

        // Act
        await vm.LoadFeaturesAsync();

        // Assert
        Assert.Equal(34, vm.FeatureSettings.Count);
        Assert.Contains("Loaded 34 feature settings", vm.FeatureStatusMessage);

        // Filter: All
        vm.SelectedFeatureRoleFilter = "All";
        Assert.Equal(34, vm.FilteredFeatureSettings.Count);

        // Filter: Manager
        vm.SelectedFeatureRoleFilter = "Manager";
        Assert.Equal(16, vm.FilteredFeatureSettings.Count);

        // Filter: Owner
        vm.SelectedFeatureRoleFilter = "Owner";
        Assert.Equal(15, vm.FilteredFeatureSettings.Count);

        // Filter: Global
        vm.SelectedFeatureRoleFilter = "Global";
        Assert.Equal(3, vm.FilteredFeatureSettings.Count);
    }

    [Fact]
    public async Task J_ApplyClientPreset_ModifiesStatesOnlyAndPreservesCanonicalCount()
    {
        using var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        await EnsureBaseSchemaAsync(db);

        var service = sp.GetRequiredService<IFeatureToggleService>();
        var vm = new DeveloperMainWindowViewModel(service);

        // Before loading: ApplyClientPreset should guard against empty collection
        vm.ApplyClientPreset("CurrentClient");
        Assert.Contains("No feature settings loaded", vm.FeatureStatusMessage);
        Assert.Empty(vm.FeatureSettings);

        // Load features
        await vm.LoadFeaturesAsync();
        Assert.Equal(34, vm.FeatureSettings.Count);

        // Apply CurrentClient preset
        vm.ApplyClientPreset("CurrentClient");

        // Assert: Count remains exactly 34
        Assert.Equal(34, vm.FeatureSettings.Count);

        // Verify specific preset flags
        var ags = vm.FeatureSettings.First(f => f.FeatureKey == "Admin_AgsImport");
        Assert.False(ags.IsEnabled);

        var pl = vm.FeatureSettings.First(f => f.FeatureKey == "Owner_ProfitLoss");
        Assert.False(pl.IsEnabled);

        var mismatch = vm.FeatureSettings.First(f => f.FeatureKey == "Owner_MismatchLedger");
        Assert.False(mismatch.IsEnabled);

        var morningNight = vm.FeatureSettings.First(f => f.FeatureKey == "Collection_UseMorningNight");
        Assert.False(morningNight.IsEnabled);

        var dsmEntry = vm.FeatureSettings.First(f => f.FeatureKey == "Admin_DsmEntry");
        Assert.True(dsmEntry.IsEnabled);

        var finalCalc = vm.FeatureSettings.First(f => f.FeatureKey == "Admin_FinalCalculation");
        Assert.True(finalCalc.IsEnabled);
    }

    [Fact]
    public async Task K_RealDatabaseCopy_EndToEndInitializationAndViewModelValidation()
    {
        var realDbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro", "fuelPro.db");
        if (!File.Exists(realDbPath))
        {
            return;
        }

        // Copy exact real database to isolated test path
        File.Copy(realDbPath, _dbPath, true);

        using var sp = CreateServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        // Reset test database copy to reproduce the legacy state
        await db.Database.ExecuteSqlRawAsync(@"
            DELETE FROM ""__EFMigrationsHistory"";
            DELETE FROM ""AppFeatureSettings"";
            UPDATE ""Settings"" SET ""AppFeatureSettingsJson"" = NULL;
        ");

        // 1. Verify before state:
        var beforeFeaturesCount = await db.AppFeatureSettings.CountAsync();
        var beforeMigrationsCount = (await db.Database.GetAppliedMigrationsAsync()).Count();
        var beforeSetting = await db.Settings.FirstOrDefaultAsync();

        Assert.Equal(0, beforeFeaturesCount);
        Assert.Equal(0, beforeMigrationsCount);
        Assert.True(string.IsNullOrWhiteSpace(beforeSetting?.AppFeatureSettingsJson));

        // 2. Launch initialization
        await SeedData.InitializeAsync(db, null);

        // 3. Verify after state:
        var afterSetting = await db.Settings.FirstOrDefaultAsync();
        Assert.NotNull(afterSetting);
        Assert.Equal(beforeSetting?.PumpStationName, afterSetting.PumpStationName);

        // AppFeatureSettings = 34
        var afterFeaturesCount = await db.AppFeatureSettings.CountAsync();
        Assert.Equal(34, afterFeaturesCount);

        // Settings.AppFeatureSettingsJson is populated
        Assert.False(string.IsNullOrWhiteSpace(afterSetting.AppFeatureSettingsJson));
        Assert.Contains("Admin_DsmEntry", afterSetting.AppFeatureSettingsJson);

        // __EFMigrationsHistory contains the expected applied migration baseline
        var afterMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Equal(15, afterMigrations.Count);
        Assert.Contains("20260421082012_InitialCreate", afterMigrations);
        Assert.Contains("20260721090022_AddKhandharePetroleumEntries", afterMigrations);

        // Subsequent MigrateAsync() completes without the Settings-already-exists exception
        await db.Database.MigrateAsync();

        // 4. Verify Developer Tools ViewModel:
        var service = sp.GetRequiredService<IFeatureToggleService>();
        var vm = new DeveloperMainWindowViewModel(service);

        await vm.LoadFeaturesAsync();

        // 34 rows visible
        Assert.Equal(34, vm.FeatureSettings.Count);
        Assert.Contains("Loaded 34 feature settings", vm.FeatureStatusMessage);

        // All filter shows 34
        vm.SelectedFeatureRoleFilter = "All";
        Assert.Equal(34, vm.FilteredFeatureSettings.Count);

        // Manager filter shows 16
        vm.SelectedFeatureRoleFilter = "Manager";
        Assert.Equal(16, vm.FilteredFeatureSettings.Count);

        // Owner filter shows 15
        vm.SelectedFeatureRoleFilter = "Owner";
        Assert.Equal(15, vm.FilteredFeatureSettings.Count);

        // Global filter shows 3
        vm.SelectedFeatureRoleFilter = "Global";
        Assert.Equal(3, vm.FilteredFeatureSettings.Count);

        // Current Client Preset operates normally
        vm.ApplyClientPreset("CurrentClient");
        Assert.Equal(34, vm.FeatureSettings.Count);
        Assert.False(vm.FeatureSettings.First(f => f.FeatureKey == "Admin_AgsImport").IsEnabled);
        Assert.False(vm.FeatureSettings.First(f => f.FeatureKey == "Owner_ProfitLoss").IsEnabled);
        Assert.False(vm.FeatureSettings.First(f => f.FeatureKey == "Owner_MismatchLedger").IsEnabled);
        Assert.True(vm.FeatureSettings.First(f => f.FeatureKey == "Admin_DsmEntry").IsEnabled);
    }

    [Fact]
    public void L_DeveloperMainWindowViewModel_PrimaryConstructor_HasActivatorUtilitiesConstructorAttribute()
    {
        var ctor = typeof(FuelPro.UI.ViewModels.DeveloperMainWindowViewModel).GetConstructor(Type.EmptyTypes);
        Assert.NotNull(ctor);
        var attr = ctor.GetCustomAttributes(typeof(Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructorAttribute), false);
        Assert.NotEmpty(attr);
    }
}
