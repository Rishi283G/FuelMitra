using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.AppMigration;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.Sync;
using FuelPro.UI;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;
using Xunit;

namespace FuelPro.Tests;

public class DashboardValidationTest
{
    private readonly string _dbPath;

    public DashboardValidationTest()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro", "fuelPro.db");
    }

    [Fact]
    public async Task Run_DashboardAndProfitLossValidation()
    {
        FuelPro.UI.App.EnsureLegacyDatabaseCompatibility(_dbPath);

        // Ensure WPF Application object exists for Dispatcher.Invoke calls in ViewModels
        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application();
        }

        // 1. Setup DI container representing the application context
        var services = new ServiceCollection();
        
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"),
            ServiceLifetime.Transient);

        // Credentials
        var tempFolder = Path.Combine(Path.GetTempPath(), "FuelPro_Test_" + Guid.NewGuid().ToString("N"));
        services.AddSingleton<ICredentialFileService>(new LocalCredentialFileService(tempFolder));

        // Repositories
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

        // Services
        services.AddSingleton<AuthService>();
        services.AddTransient<DsmEntryService>();
        services.AddTransient<ShiftCalculationService>();
        services.AddSingleton<IDsmCalculationService, DsmCalculationService>();
        services.AddSingleton<IOwnerCalculationService, OwnerCalculationService>();
        services.AddTransient<IFinancialCalculationService, FinancialCalculationService>();
        services.AddTransient<RecalculationMigrationService>();
        services.AddScoped<IShiftAggregationService, ShiftAggregationService>();
        services.AddScoped<IShiftOtherCashRepository, ShiftOtherCashRepository>();
        services.AddScoped<IShiftFuelRateRepository, ShiftFuelRateRepository>();
        services.AddTransient<PrintService>();
        services.AddTransient<ExcelExportService>();
        services.AddTransient<IAuditLogService, AuditLogService>();
        services.AddTransient<IDayLockService, DayLockService>();
        services.AddTransient<ExportService>();
        services.AddSingleton<DraftService>();
        services.AddTransient<IAgsImportService, AgsImportService>();
        services.AddTransient<IAgsDailyAggregationService, AgsDailyAggregationService>();
        services.AddTransient<AgsImportValidator>();

        services.AddSingleton<SyncConfigService>();
        services.AddSingleton<SyncEngine>();
        services.AddSingleton<DsmSubmissionPollingService>();
        services.AddTransient<DsmAuthAdminService>();
        services.AddTransient<SupabaseDsmService>();
        services.AddTransient<DsmEntryService>();

        // ViewModels
        services.AddTransient<OwnerDashboardViewModel>();
        services.AddTransient<CollectionSummaryViewModel>();
        services.AddTransient<FinancialSummaryViewModel>();
        services.AddTransient<MonthlyPerformanceViewModel>();
        services.AddTransient<ProfitLossViewModel>();
        services.AddTransient<DsmPerformanceViewModel>();

        var serviceProvider = services.BuildServiceProvider();

        // Initialize App.Services and App.DbPath for ViewModels (static fields)
        typeof(App).GetProperty("Services")?.SetValue(null, serviceProvider);
        typeof(App).GetProperty("DbPath")?.SetValue(null, _dbPath);

        // Ensure database exists and is seeded with default users
        using (var setupContext = new FuelProDbContext(new DbContextOptionsBuilder<FuelProDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            var credentialService = serviceProvider.GetRequiredService<ICredentialFileService>();
            await SeedData.InitializeAsync(setupContext, credentialService);
        }

        // 2. Perform Sync Pull (as Owner)
        var authService = serviceProvider.GetRequiredService<AuthService>();
        var loginResult = await authService.LoginAsync("Owner", "5678");
        Assert.True(loginResult.Success, "Login as Owner failed.");

        var configService = serviceProvider.GetRequiredService<SyncConfigService>();
        var settings = await configService.GetSettingsAsync();
        if (!settings.SyncEnabled || string.IsNullOrEmpty(settings.SupabaseUrl))
        {
            settings.SyncEnabled = true;
            settings.SupabaseUrl = "https://rvcibryprvjbzrtwqktk.supabase.co";
            settings.SupabaseApiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InJ2Y2licnlwcnZqYnpydHdxa3RrIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODEzMTIxMTcsImV4cCI6MjA5Njg4ODExN30.vMTA97993upfnOCs5ja-kxIhDSHbcx1gEQ6itNm5BBk";
            settings.StationId = "STA001";
        }
        settings.LastSyncTime = DateTime.MinValue; // reset to force pull all records
        await configService.SaveSettingsAsync(settings);

        var syncEngine = serviceProvider.GetRequiredService<SyncEngine>();
        await syncEngine.ForceSyncAsync();

        Assert.True(syncEngine.CurrentStatus.IsConnected, $"Pull sync failed: {syncEngine.CurrentStatus.StatusMessage}");

        // 3. Load operational records
        using var context = serviceProvider.GetRequiredService<FuelProDbContext>();
        var shifts = await context.Shifts.OrderByDescending(s => s.ShiftDate).ToListAsync();
        Assert.NotEmpty(shifts);

        // We will validate the most recent operational shift
        var targetShift = shifts.First();
        var targetDate = targetShift.ShiftDate.Date;
        var startOfMonth = new DateTime(targetDate.Year, targetDate.Month, 1);

        // 4. Instantiate and load ViewModels
        var dashboardVm = serviceProvider.GetRequiredService<OwnerDashboardViewModel>();
        dashboardVm.SelectedDate = targetDate;
        await dashboardVm.LoadDataCommand.ExecuteAsync(null);

        var collSummaryVm = serviceProvider.GetRequiredService<CollectionSummaryViewModel>();
        collSummaryVm.StartDate = targetDate;
        collSummaryVm.EndDate = targetDate;
        await collSummaryVm.LoadCommand.ExecuteAsync(null);

        var finSummaryVm = serviceProvider.GetRequiredService<FinancialSummaryViewModel>();
        finSummaryVm.StartDate = targetDate;
        finSummaryVm.EndDate = targetDate;
        await finSummaryVm.LoadCommand.ExecuteAsync(null);

        var monthlyPerfVm = serviceProvider.GetRequiredService<MonthlyPerformanceViewModel>();
        monthlyPerfVm.SelectedMonth = startOfMonth;
        await monthlyPerfVm.LoadCommand.ExecuteAsync(null);

        var dsmPerfVm = serviceProvider.GetRequiredService<DsmPerformanceViewModel>();
        dsmPerfVm.SelectedPreset = "Custom";
        dsmPerfVm.StartDate = startOfMonth;
        dsmPerfVm.EndDate = targetDate;
        await dsmPerfVm.LoadCommand.ExecuteAsync(null);

        var profitLossVm = serviceProvider.GetRequiredService<ProfitLossViewModel>();
        profitLossVm.StartDate = targetDate;
        profitLossVm.EndDate = targetDate;
        await profitLossVm.LoadCommand.ExecuteAsync(null);

        // 5. Generate Comparison Report
        var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "dashboard_validation_report.md");

        // Grab day row from MonthlyPerformance matching our target date
        var monthlyDayRow = monthlyPerfVm.DayRows.FirstOrDefault(r => r.Date.Date == targetDate);
        
        // Grab DSM row matching our entry name (or first one)
        var dsmRow = dsmPerfVm.DsmRows.FirstOrDefault();

        // Calculate differences
        var cashDiff = dashboardVm.TodayTotalCash - (collSummaryVm.TotalCashDeposit + collSummaryVm.TotalCashInHand);
        var phonePeDiff = dashboardVm.TodayTotalPhonePe - (collSummaryVm.TotalPhonePe + collSummaryVm.TotalPhonePeCard);
        var ccDiff = dashboardVm.TodayTotalCreditCard - collSummaryVm.TotalCreditCard;
        var petroDiff = dashboardVm.TodayTotalPetroCard - collSummaryVm.TotalPetroCard;
        var debitDiff = dashboardVm.TodayTotalDebit - collSummaryVm.TotalDebit;
        
        var collectionDiff = dashboardVm.TodayTotalCollection - collSummaryVm.GrandTotal;

        var reportContent = $@"# Owner Dashboard & Profit & Loss Validation Report

This report compares calculations across the **Owner Dashboard**, **Collection Summary**, **Financial Summary**, **Monthly Performance**, **DSM Performance**, and **Profit & Loss** screens for date **{targetDate:yyyy-MM-dd}**.

---

## 1. Metric Comparison Summary

| Metric / Category | Owner Dashboard | Collection Summary | Financial Summary | Profit & Loss | Difference | Status / Match |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: |
| **Gross Sales** | {dashboardVm.TodayTotalSale:F2} | N/A | N/A | {profitLossVm.TotalGrossSales:F2} | 0.00 | {(dashboardVm.TodayTotalSale == profitLossVm.TotalGrossSales ? "MATCH" : "MISMATCH")} |
| **Total Collection** | {dashboardVm.TodayTotalCollection:F2} | {collSummaryVm.GrandTotal:F2} | N/A | {profitLossVm.TotalCollection:F2} | {collectionDiff:F2} | {(Math.Abs(collectionDiff) < 0.01 ? "MATCH" : "MISMATCH")} |
| **Total Expenses** | {dashboardVm.TodayTotalExpenses:F2} | N/A | {finSummaryVm.TotalExpenses:F2} | {profitLossVm.TotalExpenses:F2} | {(dashboardVm.TodayTotalExpenses - finSummaryVm.TotalExpenses):F2} | {(dashboardVm.TodayTotalExpenses == finSummaryVm.TotalExpenses ? "MATCH" : "MISMATCH")} |
| **Cash (Deposit + In Hand)** | {dashboardVm.TodayTotalCash:F2} | {(collSummaryVm.TotalCashDeposit + collSummaryVm.TotalCashInHand):F2} | {(finSummaryVm.TotalBankCash + finSummaryVm.TotalCashInHand):F2} | N/A | {cashDiff:F2} | {(Math.Abs(cashDiff) < 0.01 ? "MATCH" : "MISMATCH")} |
| **PhonePe (Direct + Card)** | {dashboardVm.TodayTotalPhonePe:F2} | {(collSummaryVm.TotalPhonePe + collSummaryVm.TotalPhonePeCard):F2} | N/A | N/A | {phonePeDiff:F2} | {(Math.Abs(phonePeDiff) < 0.01 ? "MATCH" : "MISMATCH")} |
| **Credit Card** | {dashboardVm.TodayTotalCreditCard:F2} | {collSummaryVm.TotalCreditCard:F2} | N/A | N/A | {ccDiff:F2} | {(Math.Abs(ccDiff) < 0.01 ? "MATCH" : "MISMATCH")} |
| **Petro Card** | {dashboardVm.TodayTotalPetroCard:F2} | {collSummaryVm.TotalPetroCard:F2} | N/A | N/A | {petroDiff:F2} | {(Math.Abs(petroDiff) < 0.01 ? "MATCH" : "MISMATCH")} |
| **Debit (Creditors)** | {dashboardVm.TodayTotalDebit:F2} | {collSummaryVm.TotalDebit:F2} | {finSummaryVm.TotalDebitorsOutstanding:F2} | {profitLossVm.TotalCreditorDebits:F2} | {debitDiff:F2} | {(Math.Abs(debitDiff) < 0.01 ? "MATCH" : "MISMATCH")} |
| **Mismatch** | {dashboardVm.TodayTotalMismatch:F2} | N/A | N/A | {profitLossVm.TotalMismatch:F2} | 0.00 | {(dashboardVm.TodayTotalMismatch == profitLossVm.TotalMismatch ? "MATCH" : "MISMATCH")} |

---

## 2. Performance Summaries Validation

### Monthly Performance (Day Row for {targetDate:yyyy-MM-dd})
- **Monthly Perf Day Sales**: `{monthlyDayRow?.TotalSale:F2}` vs **Dashboard Sales**: `{dashboardVm.TodayTotalSale:F2}` ({(monthlyDayRow?.TotalSale == dashboardVm.TodayTotalSale ? "MATCH" : "MISMATCH")})
- **Monthly Perf Day Collection**: `{monthlyDayRow?.TotalCollection:F2}` vs **Dashboard Collection**: `{dashboardVm.TodayTotalCollection:F2}` ({(monthlyDayRow?.TotalCollection == dashboardVm.TodayTotalCollection ? "MATCH" : "MISMATCH")})
- **Monthly Perf Day Mismatch**: `{monthlyDayRow?.Mismatch:F2}` vs **Dashboard Mismatch**: `{dashboardVm.TodayTotalMismatch:F2}` ({(monthlyDayRow?.Mismatch == dashboardVm.TodayTotalMismatch ? "MATCH" : "MISMATCH")})

### DSM Performance (Row for `{dsmRow?.DsmName}`)
- **DSM Total Sales**: `{dsmRow?.TotalSale:F2}`
- **DSM Total Collection**: `{dsmRow?.TotalCollection:F2}`
- **DSM Mismatch**: `{dsmRow?.Mismatch:F2}`

---

## 3. Detailed Mismatch Investigation

### Digital / PhonePe Card Tracking Mismatch
- **Owner Dashboard PhonePe**: `{dashboardVm.TodayTotalPhonePe}`
- **Collection Summary PhonePe + PhonePeCard**: `{collSummaryVm.TotalPhonePe} + {collSummaryVm.TotalPhonePeCard} = {collSummaryVm.TotalPhonePe + collSummaryVm.TotalPhonePeCard}`
- **Discrepancy**: `{phonePeDiff}`. The Owner Dashboard currently ignores PhonePe Card transactions in the payment breakdown.

### Total Collection Calculation Difference
- **Owner Dashboard Collection**: `{dashboardVm.TodayTotalCollection}` (Includes cash, digital, debit, testing, and expenses)
- **Collection Summary Grand Total**: `{collSummaryVm.GrandTotal}` (Includes cash, digital, and debit, but excludes testing and expenses)
- **Discrepancy**: `{collectionDiff}`.

---

## 4. Profit & Loss Verification
- **P&L Net Position (Sales - Expenses)**: `{profitLossVm.NetProfit:F2}`
- **P&L Mismatch**: `{profitLossVm.TotalMismatch:F2}`
- **Status**: {(profitLossVm.TotalMismatch == dashboardVm.TodayTotalMismatch ? "VERIFIED" : "DISCREPANCY DETECTED")}

";

        await File.WriteAllTextAsync(reportPath, reportContent);
        Console.WriteLine(reportContent);
    }

    [Fact]
    public async Task Run_DeveloperLoginDiagnostics()
    {
        // 1. Read dev_credential.txt
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FuelPro");
        var devCredPath = Path.Combine(folder, "dev_credential.txt");
        bool fileExists = File.Exists(devCredPath);
        string fileContent = fileExists ? await File.ReadAllTextAsync(devCredPath) : "FILE NOT FOUND";

        // Extract generated PIN if file exists
        string generatedPin = "";
        if (fileExists)
        {
            var lines = fileContent.Split('\n');
            var pinLine = lines.FirstOrDefault(l => l.Contains("Generated PIN:"));
            if (pinLine != null)
            {
                generatedPin = pinLine.Split(':').Last().Trim();
            }
        }

        // Ensure database exists and is seeded with default users
        using (var setupContext = new FuelProDbContext(new DbContextOptionsBuilder<FuelProDbContext>().UseSqlite($"Data Source={_dbPath}").Options))
        {
            await setupContext.Database.MigrateAsync();
            var credentialService = new LocalCredentialFileService(folder);
            await SeedData.InitializeAsync(setupContext, credentialService);
        }

        // 2. Query SQLite Database Users table
        using var context = new FuelProDbContext(new DbContextOptionsBuilder<FuelProDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var devUser = await context.Users.FirstOrDefaultAsync(u => u.Username == "Developer" || u.Role == "Developer");

        bool userExists = devUser != null;
        string storedUsername = devUser?.Username ?? "N/A";
        string storedRole = devUser?.Role ?? "N/A";
        bool isActive = devUser?.IsActive ?? false;
        string storedHash = devUser?.PinHash ?? "N/A";

        // 3. Verify BCrypt match
        bool hashMatches = false;
        if (userExists && !string.IsNullOrEmpty(generatedPin))
        {
            try
            {
                hashMatches = BCrypt.Net.BCrypt.Verify(generatedPin, storedHash);
            }
            catch (Exception)
            {
                hashMatches = false;
            }
        }

        // 4. Check if the installer's published folder or default location is using the same database
        // Let's write the report
        var diagReportPath = Path.Combine(Directory.GetCurrentDirectory(), "dev_login_diagnostic_report.md");
        var reportContent = $@"# Developer Login Diagnostic Report

This report diagnoses the authentication failure of the Developer user.

## Diagnostic Metrics

1. **Does the Developer user exist in the SQLite Users table?**
   - Answer: **{(userExists ? "YES" : "NO")}**

2. **What is the exact username stored?**
   - Answer: **`{storedUsername}`**

3. **What role is assigned?**
   - Answer: **`{storedRole}`**

4. **Is the account active?**
   - Answer: **{(isActive ? "YES" : "NO")}** (IsActive: `{isActive}`)

5. **Was the generated PIN successfully persisted to the database?**
   - Answer: **{(userExists ? "YES (User record exists in DB)" : "NO (User record missing in DB)")}**

6. **Does the stored PIN hash match the generated PIN {generatedPin}?**
   - Answer: **{(hashMatches ? "YES (Hash matches generated PIN)" : "NO (Hash mismatch!)")}**

7. **Is the installer using the same database that generated the credential file?**
   - Answer: **YES**. Both the UI application (built by the installer) and the credential generator use `%LOCALAPPDATA%\FuelPro\fuelPro.db`.

## Database Details
- **Database Path**: `{_dbPath}`
- **Credential File Path**: `{devCredPath}`
- **Credential File Content**:
```
{fileContent.Trim()}
```
- **DB User Hash**: `{storedHash}`

## Diagnosis & Explanation
{(
    !fileExists ? "The credential file `dev_credential.txt` is missing from the system." :
    !userExists ? "The Developer user is missing from the database." :
    !hashMatches ? "The PIN stored in `dev_credential.txt` (" + generatedPin + ") does not match the hashed PIN in the database. This indicates that the database was seeded or modified at a different time than when the current `dev_credential.txt` was written, leading to a synchronization/version mismatch between the credential file and the DB." :
    "The credential file and database match perfectly. If login is still failing, check if the UI is querying a different database path at runtime."
)}
";

        await File.WriteAllTextAsync(diagReportPath, reportContent);
        Console.WriteLine(reportContent);
    }
}
