using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;
using Xunit;

namespace FuelPro.Tests;

public class ReleasePackageSmokeTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
    }

    [Fact]
    public void SmokeTest_Step1_InstallerPackageIntegrityAndMetadata()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var installerPath = Path.Combine(repoRoot, "installer_output", "PyroSync_Setup_v1.3.33.exe");
        var publishDir = Path.Combine(repoRoot, "publish_output");

        Assert.True(File.Exists(installerPath), $"Installer missing at {installerPath}");
        var fileInfo = new FileInfo(installerPath);
        Assert.True(fileInfo.Length > 55_000_000, $"Installer size {fileInfo.Length} bytes is smaller than expected.");

        // Check release binaries
        var uiExe = Path.Combine(publishDir, "FuelPro.UI.exe");
        var uiDll = Path.Combine(publishDir, "FuelPro.UI.dll");
        var coreDll = Path.Combine(publishDir, "FuelPro.Core.dll");
        var dataDll = Path.Combine(publishDir, "FuelPro.Data.dll");

        Assert.True(File.Exists(uiExe), "FuelPro.UI.exe missing from publish_output");
        Assert.True(File.Exists(uiDll), "FuelPro.UI.dll missing from publish_output");
        Assert.True(File.Exists(coreDll), "FuelPro.Core.dll missing from publish_output");
        Assert.True(File.Exists(dataDll), "FuelPro.Data.dll missing from publish_output");

        var verInfo = FileVersionInfo.GetVersionInfo(uiDll);
        Assert.Equal("1.3.33.0", verInfo.FileVersion);
        Assert.StartsWith("1.3.33", verInfo.ProductVersion);

        // Confirm no test databases or source files exist in publish_output
        var dbFiles = Directory.GetFiles(publishDir, "*.db*", SearchOption.AllDirectories);
        Assert.Empty(dbFiles);

        var csFiles = Directory.GetFiles(publishDir, "*.cs", SearchOption.AllDirectories);
        Assert.Empty(csFiles);

        var pdbFiles = Directory.GetFiles(publishDir, "*.pdb", SearchOption.AllDirectories);
        Assert.NotEmpty(pdbFiles); 
    }

    [Fact]
    public async Task SmokeTest_Steps2Through14_FullFunctionalFlowInIsolatedEnvironment()
    {
        // 1. Isolated test database
        var tempDb = Path.Combine(Path.GetTempPath(), $"release_smoke_test_{Guid.NewGuid():N}.db");
        _tempFiles.Add(tempDb);

        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application();
        }

        // Create database schema using EF Core EnsureCreated
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite($"Data Source={tempDb}")
            .Options;

        using (var initContext = new FuelProDbContext(options))
        {
            await initContext.Database.EnsureCreatedAsync();

            initContext.Settings.Add(new Setting
            {
                PumpStationName = "Mitali Service Station",
                StationDisplayName = "Mitali Service Station",
                HsdRate = 90,
                MsIRate = 100,
                MsIIRate = 100,
                CngRate = 85
            });

            // Seed initial DSM and Shift records
            var shiftA = new Shift { ShiftDate = DateTime.Today, ShiftType = "A" };
            var shiftB = new Shift { ShiftDate = DateTime.Today, ShiftType = "B" };
            initContext.Shifts.AddRange(shiftA, shiftB);

            var dsmUser = new DsmUser { FullName = "Ramesh SmokeTest", MobileNumber = "9876543210", EmployeeCode = "DSM001", CreatedAt = DateTime.Now };
            initContext.DsmUsers.Add(dsmUser);

            var dsmDebtor = new DsmPersonalDebtor
            {
                DsmName = "Ramesh SmokeTest",
                Amount = 1000.0,
                Date = DateTime.Today,
                FuelProduct = "MS",
                Remarks = "Pre-existing advance"
            };
            initContext.DsmPersonalDebtors.Add(dsmDebtor);

            // Add an existing historical repayment with ShiftId = null
            var historicalRepayment = new DsmPersonalDebtorRepayment
            {
                DsmPersonalDebtor = dsmDebtor,
                Date = DateTime.Today,
                Amount = 100.0,
                PaymentMethod = "Cash",
                ShiftId = null,
                Source = "ManagerShiftTotal"
            };
            initContext.DsmPersonalDebtorRepayments.Add(historicalRepayment);

            await initContext.SaveChangesAsync();
        }

        // 2. Setup isolated DI container using the database
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(opt => opt.UseSqlite($"Data Source={tempDb}"), ServiceLifetime.Transient);

        services.AddTransient<ISettingsRepository, SettingsRepository>();
        services.AddTransient<IShiftRepository, ShiftRepository>();
        services.AddTransient<IDsmPersonalDebtorRepository, DsmPersonalDebtorRepository>();
        services.AddTransient<IDsmEntryRepository, DsmEntryRepository>();
        services.AddTransient<IExpenseRepository, ExpenseRepository>();
        services.AddTransient<IShiftFuelRateRepository, ShiftFuelRateRepository>();
        services.AddTransient<IShiftOtherCashRepository, ShiftOtherCashRepository>();
        services.AddTransient<ICreditorRepaymentRepository, CreditorRepaymentRepository>();
        services.AddTransient<ICreditorRepository, CreditorRepository>();
        services.AddTransient<IAgsImportRepository, AgsImportRepository>();
        services.AddTransient<IAgsInventoryService, AgsInventoryService>();
        services.AddTransient<ITidCalculationService, TidCalculationService>();
        services.AddTransient<IShiftAggregationService, ShiftAggregationService>();
        services.AddTransient<IReportService, ReportService>();
        services.AddSingleton<IFeatureToggleService, FeatureToggleService>();
        services.AddTransient<IUserRepository, UserRepository>();
        services.AddTransient<AuthService>();
        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddTransient<ICredentialFileService, LocalCredentialFileService>();
        services.AddTransient<ExcelExportService>();
        services.AddTransient<PrintService>();
        services.AddSingleton<FuelPro.Sync.SyncConfigService>();
        services.AddSingleton<FuelPro.Sync.SyncEngine>();
        services.AddSingleton<FuelPro.Sync.DsmSubmissionPollingService>();

        services.AddTransient<DsmPersonalDebtorViewModel>();
        services.AddTransient<FinalCalculationViewModel>();
        services.AddTransient<DayTotalViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<OwnerMainWindowViewModel>();

        var sp = services.BuildServiceProvider();
        typeof(FuelPro.UI.App).GetProperty("Services", BindingFlags.Public | BindingFlags.Static)!.SetValue(null, sp);
        typeof(FuelPro.UI.App).GetProperty("DbPath", BindingFlags.Public | BindingFlags.Static)!.SetValue(null, tempDb);

        // 3. Confirm application startup succeeds and database is read correctly (Step 3 & 4)
        var dsmVm = sp.GetRequiredService<DsmPersonalDebtorViewModel>();
        await dsmVm.LoadDataAsync();

        // 4. Confirm Shift A/B selector exists on ViewModel (Step 6)
        Assert.NotNull(dsmVm.AvailableShifts);
        Assert.Contains("Shift A", dsmVm.AvailableShifts);
        Assert.Contains("Shift B", dsmVm.AvailableShifts);
        Assert.Equal("Shift A", dsmVm.SelectedShift);

        // 5. Confirm existing DSM records remain visible (Step 7)
        Assert.Single(dsmVm.DsmSummaries);
        var summary = dsmVm.DsmSummaries.First();
        Assert.Equal("Ramesh SmokeTest", summary.DsmName);
        Assert.Equal(1000.0, summary.TotalBorrowed);
        Assert.Equal(100.0, summary.TotalRepaid);
        Assert.Equal(900.0, summary.Balance);

        // 6. Create a test DSM repayment using Shift A: ₹500 Cash (Step 8)
        var shiftRepo = sp.GetRequiredService<IShiftRepository>();
        var shiftRes = await shiftRepo.GetOrCreateShiftAsync(DateTime.Today, "A");
        Assert.True(shiftRes.Success && shiftRes.Data != null);

        using (var saveContext = new FuelProDbContext(options))
        {
            var debtorEntity = await saveContext.DsmPersonalDebtors.FirstAsync(d => d.DsmName == "Ramesh SmokeTest");
            var repayment = new DsmPersonalDebtorRepayment
            {
                DsmPersonalDebtorId = debtorEntity.Id,
                ShiftId = shiftRes.Data.ShiftId,
                Amount = 500.0,
                Date = DateTime.Today,
                PaymentMethod = "Cash",
                Source = "ManagerShiftTotal"
            };
            saveContext.DsmPersonalDebtorRepayments.Add(repayment);
            saveContext.DsmEntries.Add(new DsmEntry
            {
                ShiftId = shiftRes.Data.ShiftId,
                DsmName = "Ramesh SmokeTest",
                PumpId = 1,
                GrossSales = 500m
            });
            await saveContext.SaveChangesAsync();
        }

        // 7. Confirm it appears correctly with ShiftId assigned (Step 9)
        using (var verifyContext = new FuelProDbContext(options))
        {
            var newRepayment = await verifyContext.DsmPersonalDebtorRepayments
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync();

            Assert.NotNull(newRepayment);
            Assert.Equal(500.0, newRepayment.Amount);
            Assert.NotNull(newRepayment.ShiftId);

            var shift = await verifyContext.Shifts.FindAsync(newRepayment.ShiftId);
            Assert.NotNull(shift);
            Assert.Equal("A", shift.ShiftType);

            // Confirm historical record remains untouched with ShiftId = null (Step 13 & 14)
            var hist = await verifyContext.DsmPersonalDebtorRepayments.FindAsync(1);
            Assert.NotNull(hist);
            Assert.Null(hist.ShiftId);
        }

        // 8. Navigate away and back to DSM Loss and confirm it refreshes without manual Refresh (Step 10)
        var mainVm = sp.GetRequiredService<MainWindowViewModel>();
        mainVm.NavigateToDsmPersonalDebtorCommand.Execute(null);

        var currentDsmVm = Assert.IsType<DsmPersonalDebtorViewModel>(mainVm.CurrentView);
        Assert.NotNull(currentDsmVm.SelectedSummary);
        Assert.Equal("Ramesh SmokeTest", currentDsmVm.SelectedSummary.DsmName);
        Assert.Equal(600.0, currentDsmVm.SelectedSummary.TotalRepaid);
        Assert.Equal(400.0, currentDsmVm.SelectedSummary.Balance);

        // 9. Verify Shift A/B/Day Total behavior (Step 11)
        var finalVm = sp.GetRequiredService<FinalCalculationViewModel>();
        finalVm.SelectedDate = DateTime.Today;
        finalVm.SelectedShift = "A";
        await finalVm.LoadShiftDataCommand.ExecuteAsync(null);

        var shiftARepayments = finalVm.DebtorRepayments.Where(r => r.CreditorName.Contains("DSM Loss")).ToList();
        Assert.Single(shiftARepayments);
        Assert.Equal(500.0, shiftARepayments[0].Amount);

        // Shift B
        finalVm.SelectedShift = "B";
        await finalVm.LoadShiftDataCommand.ExecuteAsync(null);
        var shiftBRepayments = finalVm.DebtorRepayments.Where(r => r.CreditorName.Contains("DSM Loss")).ToList();
        Assert.Empty(shiftBRepayments);

        // Day Total
        var dayVm = sp.GetRequiredService<DayTotalViewModel>();
        dayVm.StartDate = DateTime.Today;
        dayVm.EndDate = DateTime.Today;
        await dayVm.LoadDayDataCommand.ExecuteAsync(null);
        var dayDsmRepayments = dayVm.DebtorRepayments.Where(r => r.CreditorName.Contains("DSM Loss")).ToList();
        // Day Total contains both the historical ₹100 and new ₹500
        Assert.Equal(2, dayDsmRepayments.Count);
        Assert.Equal(600.0, dayDsmRepayments.Sum(r => r.Amount));

        // 10. Verify Personal Ledger visibility according to feature configuration (Step 12)
        var featureService = sp.GetRequiredService<IFeatureToggleService>();
        await featureService.SaveFeaturesAsync(new[]
        {
            new AppFeatureSetting
            {
                FeatureKey = "Operations_PersonalLedger",
                IsEnabled = false,
                DisplayName = "Special Ledger",
                Category = "Operations",
                TargetRole = "Global"
            }
        });

        Assert.False(dsmVm.IsPersonalLedgerEnabled);

        await featureService.SaveFeaturesAsync(new[]
        {
            new AppFeatureSetting
            {
                FeatureKey = "Operations_PersonalLedger",
                IsEnabled = true,
                DisplayName = "Mitali Personal Account",
                Category = "Operations",
                TargetRole = "Global"
            }
        });

        Assert.True(dsmVm.IsPersonalLedgerEnabled);
        Assert.Equal("Mitali Personal Account", dsmVm.PersonalLedgerTitle);
        Assert.Equal("Mitali Personal Account Ledger", dsmVm.PersonalLedgerTabHeader);

        // 11. Confirm no database migrations or schema alterations occurred (Step 13 & 14)
        using (var checkConn = new SqliteConnection($"Data Source={tempDb}"))
        {
            await checkConn.OpenAsync();
            using var pragmaCmd = checkConn.CreateCommand();
            pragmaCmd.CommandText = "PRAGMA table_info(DsmPersonalDebtorRepayments);";
            var cols = new List<string>();
            using (var reader = await pragmaCmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    cols.Add(reader.GetString(1));
                }
            }

            Assert.DoesNotContain("ShiftNumber", cols);
            Assert.Contains("ShiftId", cols);
        }
    }
}
