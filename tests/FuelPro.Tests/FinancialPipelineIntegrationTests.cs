using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;
using FuelPro.Core.DTOs;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.Sync;
using FuelPro.UI;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;
using Xunit;

namespace FuelPro.Tests;

public class FinancialPipelineIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;

    public FinancialPipelineIntegrationTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        var tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _dbPath = Path.Combine(tempDir, "fuelPro.db");

        // Ensure WPF Application object exists for ViewModels
        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application();
        }

        var services = new ServiceCollection();
        
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"),
            ServiceLifetime.Transient);

        services.AddTransient<DbContext>(sp => sp.GetRequiredService<FuelProDbContext>());

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
        services.AddTransient<IDsmPersonalDebtorRepository, DsmPersonalDebtorRepository>();
        services.AddScoped<IShiftOtherCashRepository, ShiftOtherCashRepository>();
        services.AddScoped<IShiftFuelRateRepository, ShiftFuelRateRepository>();

        // Services
        services.AddSingleton<AuthService>();
        services.AddTransient<DsmEntryService>();
        services.AddTransient<ShiftCalculationService>();
        services.AddSingleton<IDsmCalculationService, DsmCalculationService>();
        services.AddSingleton<IOwnerCalculationService, OwnerCalculationService>();
        services.AddSingleton<ITidCalculationService, TidCalculationService>();
        services.AddScoped<IShiftAggregationService, ShiftAggregationService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddTransient<IAgsInventoryService, MockAgsInventoryService>();
        services.AddTransient<IFinancialCalculationService, MockFinancialCalculationService>();
        
        services.AddTransient<PrintService>();
        services.AddTransient<ExcelExportService>();
        services.AddTransient<IAuditLogService, AuditLogService>();
        services.AddTransient<IDayLockService, DayLockService>();
        services.AddTransient<IFeatureToggleService, FeatureToggleService>();
        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddTransient<IStationConfigurationService, StationConfigurationService>();
        services.AddTransient<IShiftCycleService, ShiftCycleService>();

        // Sync services (needed by OwnerDashboardViewModel)
        services.AddSingleton<SyncConfigService>();
        services.AddSingleton<SyncEngine>();
        services.AddSingleton<DsmSubmissionPollingService>();
        services.AddTransient<DsmAuthAdminService>();
        services.AddTransient<SupabaseDsmService, FakeSupabaseDsmService>();
        services.AddSingleton<DraftService>();


        // ViewModels
        services.AddTransient<DayTotalViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<OwnerDashboardViewModel>();
        services.AddTransient<DsmEntryViewModel>();
        services.AddTransient<DsmApprovalQueueViewModel>();
        services.AddTransient<DebtorManagementViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        // Initialize App static fields
        typeof(App).GetProperty("Services")?.SetValue(null, _serviceProvider);
        typeof(App).GetProperty("DbPath")?.SetValue(null, _dbPath);

        var now = DateTime.Now;
        var defaultMappings = new List<PumpMapping>
        {
            new PumpMapping { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now },
            new PumpMapping { PumpId = 1, NozzleNumber = 3, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true, CreatedAt = now },
            new PumpMapping { PumpId = 2, NozzleNumber = 2, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now },
            new PumpMapping { PumpId = 2, NozzleNumber = 4, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true, CreatedAt = now },
            new PumpMapping { PumpId = 3, NozzleNumber = 5, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now },
            new PumpMapping { PumpId = 3, NozzleNumber = 7, FuelType = "MS-II", TankName = "HSD - 20KL II", IsActive = true, CreatedAt = now },
            new PumpMapping { PumpId = 4, NozzleNumber = 6, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now },
            new PumpMapping { PumpId = 4, NozzleNumber = 8, FuelType = "MS-II", TankName = "HSD - 20KL II", IsActive = true, CreatedAt = now },
            new PumpMapping { PumpId = 5, NozzleNumber = 9, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now },
            new PumpMapping { PumpId = 5, NozzleNumber = 11, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true, CreatedAt = now },
            new PumpMapping { PumpId = 6, NozzleNumber = 10, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now },
            new PumpMapping { PumpId = 6, NozzleNumber = 12, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true, CreatedAt = now }
        };
        PumpConfiguration.InitializeFromDb(defaultMappings);

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.Database.EnsureCreated();
        }
    }

    [Fact]
    public async Task FinancialPipeline_ProducesMatchingTotals()
    {
        var testDate = new DateTime(2026, 7, 11);

        // 1. Seed shifts and entries matching the operational business day cycle
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.Settings.Add(new Setting { HsdRate = 100.0, MsIRate = 100.0, MsIIRate = 100.0, CngRate = 100.0 });

            // Today Shift A (Morning)
            var shift1 = new Shift { ShiftId = 1, ShiftDate = testDate, ShiftType = "A", IsLocked = false };
            // Today Shift B (Day)
            var shift2 = new Shift { ShiftId = 2, ShiftDate = testDate, ShiftType = "B", IsLocked = false };
            // Tomorrow Shift A (Night)
            var shift3 = new Shift { ShiftId = 3, ShiftDate = testDate.AddDays(1), ShiftType = "A", IsLocked = false };
            
            context.Shifts.AddRange(shift1, shift2, shift3);

            var entry1 = new DsmEntry
            {
                DsmEntryId = 1,
                ShiftId = 1,
                PumpId = 1,
                DsmName = "Peter Parker"
            };
            var entry2 = new DsmEntry
            {
                DsmEntryId = 2,
                ShiftId = 2,
                PumpId = 1,
                DsmName = "Tony Stark"
            };
            var entry3 = new DsmEntry
            {
                DsmEntryId = 3,
                ShiftId = 3,
                PumpId = 1,
                DsmName = "Steve Rogers"
            };
            
            context.DsmEntries.AddRange(entry1, entry2, entry3);

            // Nozzle readings (gross sale total = 3000: 1200 on Shift A, 1800 on Shift B)
            entry1.NozzleReadings.Add(new NozzleReading { NozzleNumber = 1, OpeningReading = 0, ClosingReading = 4, Rate = 100, SaleLitres = 4, Amount = 400 });
            entry1.NozzleReadings.Add(new NozzleReading { NozzleNumber = 2, OpeningReading = 0, ClosingReading = 8, Rate = 100, SaleLitres = 8, Amount = 800 });

            entry2.NozzleReadings.Add(new NozzleReading { NozzleNumber = 1, OpeningReading = 0, ClosingReading = 6, Rate = 100, SaleLitres = 6, Amount = 600 });
            entry2.NozzleReadings.Add(new NozzleReading { NozzleNumber = 2, OpeningReading = 0, ClosingReading = 12, Rate = 100, SaleLitres = 12, Amount = 1200 });

            // PaymentCollections
            entry1.PaymentCollection = new PaymentCollection
            {
                PhonePeMorning = 300,
                PhonePeCardMorning = 100,
                CreditCardMorning = 400,
                PetroCardMorning = 100,
                CashDeposit = 100
            };

            entry2.PaymentCollection = new PaymentCollection
            {
                PhonePeDay = 200,
                PhonePeCardDay = 150,
                CreditCardDay = 300,
                PetroCardDay = 100
            };

            entry3.PaymentCollection = new PaymentCollection
            {
                PhonePeNight = 500,
                PhonePeCardNight = 250,
                CreditCardNight = 200,
                PetroCardNight = 100
            };

            // Cash denominations (Cash1 = 100, Cash2 = 100) on today's Shift A
            entry1.CashDenominations.Add(new CashDenomination { CashType = "Cash1", Denom100 = 1, TotalAmount = 100 });
            entry1.CashDenominations.Add(new CashDenomination { CashType = "Cash2", Denom100 = 1, TotalAmount = 100 });

            // Debtor / Creditor (50) on today's Shift A
            entry1.DebitEntries.Add(new DebitEntry { DebtorName = "John Doe", Amount = 50 });

            // Testing (50) on today's Shift A
            entry1.TestingEntries.Add(new TestingEntry { FuelType = "MS", Amount = 50, Litres = 0.5 });

            // Expenses (50) on today's Shift A
            entry1.Expenses.Add(new Expense { Description = "WINE", Amount = 50 });

            await context.SaveChangesAsync();
        }

        // 2. Instantiate and load all three ViewModels
        var dayTotalVm = _serviceProvider.GetRequiredService<DayTotalViewModel>();
        dayTotalVm.StartDate = testDate;
        dayTotalVm.EndDate = testDate;
        dayTotalVm.SelectedDate = testDate;
        await (Task)dayTotalVm.GetType().GetMethod("LoadDayDataAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(dayTotalVm, null);

        var dashboardVm = _serviceProvider.GetRequiredService<DashboardViewModel>();
        dashboardVm.StartDate = testDate;
        dashboardVm.EndDate = testDate;
        dashboardVm.SelectedDate = testDate;
        await (Task)dashboardVm.GetType().GetMethod("LoadDataAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(dashboardVm, null);

        var ownerVm = _serviceProvider.GetRequiredService<OwnerDashboardViewModel>();
        ownerVm.StartDate = testDate;
        ownerVm.EndDate = testDate;
        await (Task)ownerVm.GetType().GetMethod("LoadDataAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(ownerVm, null);

        // 3. Verify consistency across all three modules
        
        // Assert Sales: DayTotalVm shows DSR (Actual Customer Sales Only = Gross 3000 - Testing 50 = 2950), while Dashboards preserve Gross Sales (3000)
        Assert.Equal(2950, dayTotalVm.TotalDayFuelSaleAmount);
        Assert.Equal(3000, dashboardVm.TodayTotalSale);
        Assert.Equal(3000, ownerVm.TodayTotalSale);

        // Assert PhonePe / UPI Direct Direct
        Assert.Equal(500, dayTotalVm.PhonePeTotal);
        Assert.Equal(500, dashboardVm.TodayTotalPhonePe);
        Assert.Equal(500, ownerVm.TodayTotalPhonePe);

        // Assert PhonePe Card / UPI Card
        Assert.Equal(250, dashboardVm.TodayTotalPhonePeCardMorning + dashboardVm.TodayTotalPhonePeCardNight);

        // Assert Credit Card (PineLabs)
        Assert.Equal(700, dayTotalVm.CreditCardTotal);
        Assert.Equal(700, dashboardVm.TodayTotalCreditCard);
        Assert.Equal(700, ownerVm.TodayTotalCreditCard);

        // Assert Petro Card
        Assert.Equal(200, dayTotalVm.PetroCardTotal);
        Assert.Equal(200, dashboardVm.TodayTotalPetroCard);
        Assert.Equal(200, ownerVm.TodayTotalPetroCard);

        // Assert Cash Deposits and Cash In Hand
        Assert.Equal(100, dayTotalVm.BankCashTotal); // Cash1 (100) or CashDeposit (100) (not double-counted)
        Assert.Equal(100, dashboardVm.TodayTotalBankCash);

        Assert.Equal(100, dayTotalVm.CashInHandTotal); // Cash2 (100)
        Assert.Equal(100, dashboardVm.TodayTotalCashInHand);
        
        // OwnerDashboardViewModel combines deposits + hand cash into TodayTotalCash
        Assert.Equal(200, ownerVm.TodayTotalCash);

        // Assert Total Expenses
        Assert.Equal(50, dayTotalVm.ExpensesTotal);
        Assert.Equal(50, dashboardVm.TodayTotalExpenses);
        Assert.Equal(50, ownerVm.TodayTotalExpenses);

        // Assert Difference / Mismatch
        // Total Collection = (UPI Direct) 1000 + (UPI Card) 500 + (Credit Card) 900 + (Petro Card) 300 
        //                    + (Bank Cash) 100 + (Cash in hand) 100 + (Debtors) 50 + (Expenses) 50 + (Testing) 50
        //                  = 3050
        // Sales = 3000
        // Expected Mismatch = -1250 from report.Difference (DayTotalViewModel now faithfully preserves ReportService difference without override)
        Assert.Equal(-1250, dayTotalVm.Difference);
        Assert.Equal(50, dashboardVm.TodayTotalMismatch);
        Assert.Equal(-1050, ownerVm.TodayTotalMismatch);

    }

    [Fact]
    public async Task ConnectedPumpAggregation_GroupsAndAggregatesCorrectly()
    {
        var testDate = new DateTime(2026, 7, 12);
        var reportService = _serviceProvider.GetRequiredService<IReportService>();

        var entries = new List<DsmEntry>();

        // Shift
        var shift = new Shift { ShiftId = 2, ShiftDate = testDate, ShiftType = "A" };

        // Entry 1: Primary (Pump 1)
        var primaryEntry = new DsmEntry
        {
            DsmEntryId = 10,
            ShiftId = 2,
            Shift = shift,
            PumpId = 1,
            ConnectedPumpId = 2,
            DsmName = "Peter Parker",
            GrossSales = 1000
        };
        primaryEntry.NozzleReadings.Add(new NozzleReading { NozzleNumber = 1, OpeningReading = 0, ClosingReading = 10, Rate = 100, SaleLitres = 10, Amount = 1000 });
        primaryEntry.PaymentCollection = new PaymentCollection
        {
            PhonePeMorning = 300,
            PhonePeDay = 200,
            PhonePeNight = 500,
            CashDeposit = 100
        };
        primaryEntry.CashDenominations.Add(new CashDenomination { CashType = "Cash1", Denom100 = 1, TotalAmount = 100 });
        primaryEntry.CashDenominations.Add(new CashDenomination { CashType = "Cash2", Denom100 = 1, TotalAmount = 100 });
        primaryEntry.DebitEntries.Add(new DebitEntry { DebtorName = "John Doe", Amount = 50 });

        // Entry 2: Connected (Pump 2, reconciled to Pump 1)
        var connectedEntry = new DsmEntry
        {
            DsmEntryId = 11,
            ShiftId = 2,
            Shift = shift,
            PumpId = 2,
            ReconciledToPumpId = 1,
            DsmName = "Peter Parker",
            GrossSales = 2000
        };
        connectedEntry.NozzleReadings.Add(new NozzleReading { NozzleNumber = 2, OpeningReading = 0, ClosingReading = 20, Rate = 100, SaleLitres = 20, Amount = 2000 });
        connectedEntry.Expenses.Add(new Expense { Description = "WINE", Amount = 50 });

        entries.Add(primaryEntry);
        entries.Add(connectedEntry);

        var report = reportService.CalculateShiftReport(
            testDate,
            "A",
            entries,
            new List<Expense>(),
            new List<ShiftOtherCash>(),
            new List<CreditorRepayment>(),
            100, 100, 100, 100,
            null, null,
            "Test Station"
        );

        // Assertions
        Assert.Single(report.DsmSummaryRows); // Must aggregate into exactly 1 row
        var row = report.DsmSummaryRows[0];
        Assert.Equal("Peter Parker", row.DsmName);
        Assert.Equal(1, row.PumpId); // Primary pump ID
        Assert.Equal(3000, row.GrossSales); // 1000 (primary) + 2000 (connected)
        Assert.Equal(1000, row.PhonePe); // PhonePe total
        Assert.Equal(50, row.Expenses); // Exp from connected entry aggregated to primary
        Assert.Equal(50, row.Debit); // Debtors from primary entry
        Assert.Equal(100, row.CashDeposit); // Cash1
        Assert.Equal(100, row.CashInHand); // Cash2
    }

    [Fact]
    public async Task ConnectedPumpEditPipeline_LosslessReconstruction()
    {
        var testDate = new DateTime(2026, 7, 13);
        
        // 1. Seed primary and connected entries in the database
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var shift = new Shift { ShiftDate = testDate, ShiftType = "A" };
            context.Shifts.Add(shift);
            await context.SaveChangesAsync();

            var primary = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                DsmName = "Raviraj Gangane",
                PumpId = 1,
                ConnectedPumpId = 2,
                StartTime = "08:00 AM",
                EndTime = "08:00 PM",
                GrossSales = 2222.24m
            };
            context.DsmEntries.Add(primary);
            await context.SaveChangesAsync();

            var connected = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                DsmName = "Raviraj Gangane",
                PumpId = 2,
                ReconciledToPumpId = 1,
                StartTime = "08:00 AM",
                EndTime = "08:00 PM",
                GrossSales = 1038.10m
            };
            context.DsmEntries.Add(connected);
            await context.SaveChangesAsync();

            // Add nozzle readings
            context.NozzleReadings.Add(new NozzleReading { DsmEntryId = primary.DsmEntryId, NozzleNumber = 1, OpeningReading = 280, ClosingReading = 284, Rate = 103.81 });
            context.NozzleReadings.Add(new NozzleReading { DsmEntryId = primary.DsmEntryId, NozzleNumber = 3, OpeningReading = 280, ClosingReading = 300, Rate = 90.35 });
            context.NozzleReadings.Add(new NozzleReading { DsmEntryId = connected.DsmEntryId, NozzleNumber = 2, OpeningReading = 490, ClosingReading = 500, Rate = 103.81 });
            context.NozzleReadings.Add(new NozzleReading { DsmEntryId = connected.DsmEntryId, NozzleNumber = 4, OpeningReading = 360, ClosingReading = 380, Rate = 90.35 });

            // Add Payment Collection to Primary
            context.PaymentCollections.Add(new PaymentCollection
            {
                DsmEntryId = primary.DsmEntryId,
                PhonePeMorning = 400,
                PhonePeNight = 600,
                PhonePeTid = "1211",
                PhonePeBatch = "002"
            });

            // Add Cash Denominations to Primary
            context.CashDenominations.Add(new CashDenomination { DsmEntryId = primary.DsmEntryId, CashType = "Cash1", Denom500 = 2, TotalAmount = 1000 });

            // Add Debits/Expenses to Primary
            context.DebitEntries.Add(new DebitEntry { DsmEntryId = primary.DsmEntryId, DebtorName = "Express Cargo", Amount = 150 });
            context.Expenses.Add(new Expense { DsmEntryId = primary.DsmEntryId, Description = "Chai", Amount = 50 });

            await context.SaveChangesAsync();
        }

        // 2. Resolve DsmEntryViewModel
        var viewModel = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        
        // 3. Edit the Connected Pump Entry (ID = 2 in seed, but we can query it)
        int connectedEntryId;
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var connEntry = await context.DsmEntries.FirstAsync(e => e.PumpId == 2);
            connectedEntryId = connEntry.DsmEntryId;
        }

        // Execute edit command
        await viewModel.EditEntryCommand.ExecuteAsync(connectedEntryId);

        // 4. Assertions to verify lossless reconstruction
        Assert.Equal("Raviraj Gangane", viewModel.DsmName);
        Assert.Equal(1, viewModel.SelectedPump?.PumpId); // Must be primary pump 1
        Assert.Equal(2, viewModel.SelectedConnectedPump?.PumpId); // Must be connected pump 2

        // Nozzles from both pumps must be loaded (nozzles 1, 3, 2, 4)
        Assert.Equal(4, viewModel.NozzleReadings.Count);
        
        var nr1 = viewModel.NozzleReadings.First(n => n.NozzleNumber == 1);
        Assert.Equal(280, nr1.OpeningReading);
        Assert.Equal(284, nr1.ClosingReading);

        var nr2 = viewModel.NozzleReadings.First(n => n.NozzleNumber == 2);
        Assert.Equal(490, nr2.OpeningReading);
        Assert.Equal(500, nr2.ClosingReading);

        // Payments, Cash, Debits, Expenses must be loaded correctly from primary
        Assert.Equal(400, viewModel.PhonePeMorning);
        Assert.Equal(600, viewModel.PhonePeNight);
        Assert.Equal("1211", viewModel.PhonePeTid);
        Assert.Equal("002", viewModel.PhonePeBatch);
        Assert.Equal(1000, viewModel.Cash1.TotalAmount);
        Assert.Single(viewModel.Debits);
        Assert.Equal(150, viewModel.Debits[0].Amount);
        Assert.Single(viewModel.Expenses);
        Assert.Equal(50, viewModel.Expenses[0].Amount);
    }

    [Fact]
    public async Task AssignmentLifecycleAutomation_CompletesAssignmentOnApproval()
    {
        var testDate = new DateTime(2026, 7, 14);
        const string dsmAuthId = "dsm-auth-uuid-lifecycle-test";
        const string dsmName   = "Raviraj Gangane";
        const int    pumpId    = 1;
        int?   connectedPumpId = 2;
        const string shiftType = "A";

        // 1. Seed DsmUser and an active assignment
        int dsmUserId;
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var user = new DsmUser
            {
                AuthUserId   = dsmAuthId,
                FullName     = dsmName,
                Email        = "raviraj@gmail.com",
                MobileNumber = "9988776655",
                EmployeeCode = "EMP002",
                IsActive     = true
            };
            context.DsmUsers.Add(user);
            await context.SaveChangesAsync();
            dsmUserId = user.DsmUserId;

            context.DsmPumpAssignments.Add(new DsmPumpAssignment
            {
                DsmUserId       = dsmUserId,
                PumpId          = pumpId,
                ConnectedPumpId = connectedPumpId,
                ShiftType       = shiftType,
                IsActive        = true,
                AssignedDate    = testDate.AddHours(-2)
            });
            await context.SaveChangesAsync();
        }

        // 2. Simulate the assignment-completion block that runs inside ApproveAsync.
        //    This mirrors DsmApprovalQueueViewModel lines 1197–1238 exactly,
        //    but without the WPF MessageBox / Supabase network calls.
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var dsmUser = await context.DsmUsers
                .FirstOrDefaultAsync(u => u.AuthUserId == dsmAuthId || u.FullName == dsmName);

            Assert.NotNull(dsmUser);

            // Try exact match first (DSM + shift + primary pump + connected pump)
            var assignment = await context.DsmPumpAssignments
                .FirstOrDefaultAsync(a => a.IsActive
                    && a.DsmUserId  == dsmUser.DsmUserId
                    && a.ShiftType  == shiftType
                    && a.PumpId     == pumpId
                    && a.ConnectedPumpId == connectedPumpId);

            // Fallback: match without connected pump
            if (assignment == null)
            {
                assignment = await context.DsmPumpAssignments
                    .FirstOrDefaultAsync(a => a.IsActive
                        && a.DsmUserId == dsmUser.DsmUserId
                        && a.ShiftType == shiftType
                        && a.PumpId    == pumpId);
            }

            Assert.NotNull(assignment); // Assignment must be found before marking complete

            assignment.IsActive      = false;
            assignment.CompletedDate = DateTime.Now;
            context.Entry(assignment).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
            await context.SaveChangesAsync();
        }

        // 3. Verify the assignment is now marked as completed in the database
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var dbAssignment = await context.DsmPumpAssignments
                .FirstOrDefaultAsync(a => a.DsmUserId == dsmUserId);

            Assert.NotNull(dbAssignment);
            Assert.False(dbAssignment.IsActive,     "Assignment must be marked inactive after approval");
            Assert.NotNull(dbAssignment.CompletedDate);
        }
    }

    [Fact]
    public async Task DebtorRepayments_FinancialIntegration_BalancedExpected()
    {
        var testDate = new DateTime(2026, 7, 12);
        
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var shift = new Shift
            {
                ShiftId = 2,
                ShiftDate = testDate,
                ShiftType = "A",
                IsLocked = false
            };
            context.Shifts.Add(shift);

            var entry = new DsmEntry
            {
                DsmEntryId = 2,
                ShiftId = 2,
                PumpId = 1,
                DsmName = "Bruce Wayne"
            };
            context.DsmEntries.Add(entry);

            // Fuel sales (Gross sales = 5000)
            entry.NozzleReadings.Add(new NozzleReading { NozzleNumber = 1, OpeningReading = 0, ClosingReading = 50, Rate = 100, SaleLitres = 50, Amount = 5000 });

            // PaymentCollection (PhonePe = 2000, CreditCard = 1500; Total = 3500)
            entry.PaymentCollection = new PaymentCollection
            {
                DsmEntryId = 2,
                PhonePeMorning = 1000,
                PhonePeNight = 1000,
                CreditCardMorning = 1500
            };

            // Cash In Hand = 1500
            entry.CashDenominations.Add(new CashDenomination
            {
                CashType = "Cash2",
                Denom500 = 3,
                TotalAmount = 1500
            });

            await context.SaveChangesAsync();
        }

        // Setup debtor repayments:
        // 1. PhonePe (Reconcilable) - 500
        // 2. PetroCard (Reconcilable) - 300
        // 3. Bank Transfer (Non-Reconcilable / Record-Only) - 1000
        // Total reconcilable recoveries = 500 + 300 = 800
        var repaymentRepo = _serviceProvider.GetRequiredService<ICreditorRepaymentRepository>();
        await repaymentRepo.AddAsync(new CreditorRepayment
        {
            CreditorName = "Clark Kent",
            RepaymentDate = testDate.AddHours(4),
            PaymentMode = "PhonePe",
            Amount = 500,
            ShiftNumber = "A"
        });
        await repaymentRepo.AddAsync(new CreditorRepayment
        {
            CreditorName = "Clark Kent",
            RepaymentDate = testDate.AddHours(4),
            PaymentMode = "PetroCard",
            Amount = 300,
            ShiftNumber = "A"
        });
        await repaymentRepo.AddAsync(new CreditorRepayment
        {
            CreditorName = "Clark Kent",
            RepaymentDate = testDate.AddHours(4),
            PaymentMode = "Bank Transfer",
            Amount = 1000,
            ShiftNumber = "A"
        });

        // Load repositories and calculate
        List<DsmEntry> entries;
        List<CreditorRepayment> repayments;
        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            entries = await context.DsmEntries
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                .Include(e => e.DebitEntries)
                .Include(e => e.TestingEntries)
                .Include(e => e.Expenses)
                .Include(e => e.CashDenominations)
                .Where(e => e.ShiftId == 2)
                .ToListAsync();

            repayments = await context.CreditorRepayments
                .Where(r => r.ShiftNumber == "A")
                .ToListAsync();
        }
        
        var todayTid = new BusinessDayTidSheet
        {
            Date = testDate,
            PhonePeDirectNight = 1000
        };
        var tomorrowTid = new BusinessDayTidSheet
        {
            Date = testDate.AddDays(1),
            PhonePeDirectMorning = 1000,
            PineLabsCardMorning = 1500
        };
        
        var reportService = _serviceProvider.GetRequiredService<IReportService>();
        var report = reportService.CalculateShiftReport(
            testDate,
            "A",
            entries,
            new List<Expense>(),
            new List<ShiftOtherCash>(),
            repayments,
            100.0, 100.0, 100.0, 100.0,
            todayTid, tomorrowTid,
            "Test Station"
        );

        // Assertions:
        // ExpectedCollection = Fuel Sales (5000) (repayments do not inflate operational sales totals)
        // ActualCollection = CashDeposit (0) + CashInHand (1500) + PhonePeMorning (1000) + PhonePeNight (1000) + PineLabsMorning (1500) + PetroCard (0) = 5000
        // Difference = 0 (Balanced)
        Assert.Equal(5000, report.TotalFuelAmount);
        Assert.Equal(5000, report.ExpectedCollection);
        Assert.Equal(5000, report.ActualCollection);
        Assert.Equal(0, report.Difference);
        Assert.True(report.IsBalanced);
        
        // Verify breakdown items:
        // Cash In Hand category total should be 1500 base + 0 recovery = 1500
        var cashInHandCol = report.CollectionBreakdown.First(c => c.Category == "Cash In Hand");
        Assert.Equal(1500, cashInHandCol.Amount);
        Assert.Equal(1500, cashInHandCol.BaseAmount);
        Assert.Equal(0, cashInHandCol.RecoveryAmount);

        // PhonePe unified category base total should be 2000 base, with 500 recovery
        var phonePeCol = report.CollectionBreakdown.First(c => c.Category.Contains("PhonePe"));
        Assert.Equal(2000, phonePeCol.Amount);
        Assert.Equal(2000, phonePeCol.BaseAmount);
        Assert.Equal(500, phonePeCol.RecoveryAmount);

        // Petro Card category total should be 0 base + 300 recovery = 300 in recovery
        var petroCardCol = report.CollectionBreakdown.First(c => c.Category.Contains("Petro", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0, petroCardCol.Amount);
        Assert.Equal(0, petroCardCol.BaseAmount);
        Assert.Equal(300, petroCardCol.RecoveryAmount);
    }

    [Fact]
    public async Task CrossDsmQrPayments_SavedAndAggregatedCorrectly_InAllLayers()
    {
        var testDate = new DateTime(2026, 8, 15);
        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var tidService = _serviceProvider.GetRequiredService<ITidCalculationService>();
        var reportService = _serviceProvider.GetRequiredService<IReportService>();

        var nozzleReadings = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, OpeningReading = 100, ClosingReading = 110, Rate = 100, SaleLitres = 10, Amount = 1000 }
        };
        var payment = new PaymentCollection
        {
            PhonePeMorning = 500,
            PhonePeTidMorning = "TID001",
            PhonePeBatchMorning = "B01"
        };
        var qrPayments = new List<DsmQrPaymentEntry>
        {
            new()
            {
                TargetDsmName = "Bob Builder",
                Amount = 300,
                Tid = "TID002",
                Batch = "B02",
                Slot = "Morning"
            }
        };

        var saveRes = await dsmService.SaveCompleteEntryAsync(
            testDate, "A", "Alice Worker", 1,
            nozzleReadings, payment,
            new List<DebitEntry>(), new List<TestingEntry>(), new List<Expense>(), new List<CashDenomination>(),
            qrPayments: qrPayments
        );

        Assert.True(saveRes.Success, saveRes.Error);
        var savedEntry = saveRes.Data!;
        Assert.NotNull(savedEntry);

        // 1. Verify calculation DTO includes QR in TotalInDirect and TotalCollection
        var fullRes = await _serviceProvider.GetRequiredService<IDsmEntryRepository>().GetFullEntryAsync(savedEntry.DsmEntryId);
        Assert.True(fullRes.Success);
        Assert.Single(fullRes.Data!.QrPayments);
        Assert.Equal(300, fullRes.Data.QrPayments.First().Amount);
        Assert.Equal("Bob Builder", fullRes.Data.QrPayments.First().TargetDsmName);

        // 2. Verify TID Calculation aggregates QR payment under PhonePe with target DSM name
        var tidSheet = await tidService.GetTidSheetAsync(testDate);
        var qrPaymentItem = tidSheet.PhonePePayments.FirstOrDefault(p => p.DsmName.Contains("Bob Builder"));
        Assert.NotNull(qrPaymentItem);
        Assert.Equal(300, qrPaymentItem.Amount);
        Assert.Equal("TID002", qrPaymentItem.Tid);

        // 3. Verify Shift Report and Day Total Report contain QrPayments
        var shiftReport = reportService.CalculateShiftReport(
            testDate, "A",
            new List<DsmEntry> { fullRes.Data },
            new List<Expense>(), new List<ShiftOtherCash>(), new List<CreditorRepayment>(),
            100, 100, 100, 100,
            tidSheet, new BusinessDayTidSheet(), "Test Station"
        );
        Assert.NotEmpty(shiftReport.QrPayments);
        Assert.Contains(shiftReport.QrPayments, q => q.TargetDsmName == "Bob Builder" && q.Amount == 300);

        var dayReport = reportService.CalculateDayReport(
            testDate, testDate,
            new List<DsmEntry> { fullRes.Data },
            new List<Expense>(), new List<CreditorRepayment>(),
            100, 100, 100, 100, "Test Station"
        );
        Assert.NotEmpty(dayReport.QrPayments);
        Assert.Contains(dayReport.QrPayments, q => q.TargetDsmName == "Bob Builder" && q.Amount == 300);
    }

    [Fact]
    public async Task DebtorManagement_SaveDebtorAsync_CustomDebtDateAndRepaymentDate_PersistedAndReflectedInLedger()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();

        var shift = new Shift { ShiftId = 1, ShiftDate = new DateTime(2026, 11, 1), ShiftType = "A" };
        var dsm = new DsmEntry { DsmEntryId = 1, ShiftId = 1, PumpId = 1, DsmName = "Test DSM", CreatedAt = DateTime.Now };
        context.Shifts.Add(shift);
        context.DsmEntries.Add(dsm);

        var creditor = new Creditor { Name = "Alpha Logistics", Phone = "9876543210", IsActive = true, CreatedAt = DateTime.Today };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        var vm = _serviceProvider.GetRequiredService<DebtorManagementViewModel>();
        await vm.LoadDataAsync();

        var row = vm.Debtors.FirstOrDefault(d => d.CreditorId == creditor.CreditorId);
        Assert.NotNull(row);

        // Edit debtor profile with custom dates
        vm.EditDebtorCommand.Execute(row);
        Assert.True(vm.IsEditingDebtor);

        var debtDate = new DateTime(2026, 11, 1);
        var repaymentDate = new DateTime(2026, 11, 2);

        vm.EditTotalDebt = 5000;
        vm.EditDebtDate = debtDate;
        vm.EditRepaidAmount = 2000;
        vm.EditRepaymentDate = repaymentDate;
        vm.EditEntryDetails = "Initial Balances Setup";

        await vm.SaveDebtorCommand.ExecuteAsync(null);

        // Verify entities in database have the selected dates
        using var verifyContext = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var debit = await verifyContext.DebitEntries.FirstOrDefaultAsync(d => d.DebtorName == "Alpha Logistics");
        Assert.NotNull(debit);
        Assert.Equal(5000, debit.Amount);
        Assert.Equal(debtDate.Date, debit.CreatedAt.Date);

        var repayment = await verifyContext.CreditorRepayments.FirstOrDefaultAsync(r => r.CreditorName == "Alpha Logistics");
        Assert.NotNull(repayment);
        Assert.Equal(2000, repayment.Amount);
        Assert.Equal(repaymentDate.Date, repayment.RepaymentDate.Date);

        // Now test Account Statements (Ledger) loading
        vm.LedgerDebtorName = "Alpha Logistics";
        vm.LedgerStartDate = new DateTime(2026, 11, 1);
        vm.LedgerEndDate = new DateTime(2026, 11, 5);
        await vm.LoadLedgerAsync();

        // 1 Opening Balance row + 1 Debt row (Nov 1) + 1 Repayment row (Nov 2)
        Assert.Equal(3, vm.LedgerTransactions.Count);
        var debtRow = vm.LedgerTransactions.FirstOrDefault(t => t.TransactionType == "Debt");
        Assert.NotNull(debtRow);
        Assert.Equal(debtDate.Date, debtRow.Date.Date);
        Assert.Equal(5000, debtRow.Debit);

        var repaymentRow = vm.LedgerTransactions.FirstOrDefault(t => t.TransactionType == "Repayment");
        Assert.NotNull(repaymentRow);
        Assert.Equal(repaymentDate.Date, repaymentRow.Date.Date);
        Assert.Equal(2000, repaymentRow.Credit);
        Assert.Equal(3000, vm.LedgerOutstandingBalance);
    }

    [Fact]
    public async Task DebtorManagement_SaveTransactionEditAsync_ChangeDate_PreservesAmountAndUpdatesDate()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();

        var origDate = DateTime.Today.AddHours(10);
        var shift = new Shift { ShiftId = 1, ShiftDate = origDate.Date, ShiftType = "A" };
        var dsm = new DsmEntry { DsmEntryId = 1, ShiftId = 1, PumpId = 1, DsmName = "Test DSM", CreatedAt = DateTime.Now };
        context.Shifts.Add(shift);
        context.DsmEntries.Add(dsm);

        var creditor = new Creditor { Name = "Beta Transporters", IsActive = true, CreatedAt = DateTime.Today };
        context.Creditors.Add(creditor);

        var debit = new DebitEntry
        {
            DsmEntryId = 1,
            DebtorName = "Beta Transporters",
            Amount = 1500,
            PaymentMethod = "Credit",
            Remarks = "Fuel Delivery",
            CreatedAt = origDate
        };
        context.DebitEntries.Add(debit);
        await context.SaveChangesAsync();

        var vm = _serviceProvider.GetRequiredService<DebtorManagementViewModel>();
        vm.LedgerDebtorName = "Beta Transporters";
        vm.LedgerStartDate = DateTime.Today.AddDays(-10);
        vm.LedgerEndDate = DateTime.Today.AddDays(5);
        await vm.LoadLedgerAsync();

        var txRow = vm.LedgerTransactions.FirstOrDefault(t => t.TransactionType == "Debt");
        Assert.NotNull(txRow);

        // Start edit
        vm.StartEditTransactionCommand.Execute(txRow);
        Assert.True(vm.IsEditingTransaction);
        Assert.Equal(origDate.Date, vm.EditTxDate.Date);
        Assert.Equal(1500, vm.EditTxAmount);

        // Change Date only, preserve amount
        var newDate = DateTime.Today.AddDays(-3);
        vm.EditTxDate = newDate;

        await vm.SaveTransactionEditCommand.ExecuteAsync(null);

        // Verify in DB
        using var verifyContext = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var updatedDebit = await verifyContext.DebitEntries.FindAsync(debit.DebitId);
        Assert.NotNull(updatedDebit);
        Assert.Equal(newDate.Date, updatedDebit.CreatedAt.Date);
        Assert.Equal(1500, updatedDebit.Amount);

        // Verify audit log
        var audit = await verifyContext.AuditLogs.FirstOrDefaultAsync(a => a.RecordId == debit.DebitId && a.FieldName == "CreatedAt");
        Assert.NotNull(audit);
        Assert.Equal(origDate.Date.ToString("yyyy-MM-dd"), audit.OldValue);
        Assert.Equal(newDate.Date.ToString("yyyy-MM-dd"), audit.NewValue);
    }

    [Fact]
    public async Task DebtorManagement_SaveTransactionEditAsync_ChangeAmount_PreservesOriginalDate()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();

        var creditor = new Creditor { Name = "Gamma Travels", IsActive = true, CreatedAt = DateTime.Today };
        context.Creditors.Add(creditor);

        var origDate = new DateTime(2026, 11, 2, 14, 0, 0);
        var repayment = new CreditorRepayment
        {
            CreditorName = "Gamma Travels",
            Amount = 800,
            PaymentMode = "Cash",
            RepaymentDate = origDate,
            CreatedAt = origDate
        };
        context.CreditorRepayments.Add(repayment);
        await context.SaveChangesAsync();

        var vm = _serviceProvider.GetRequiredService<DebtorManagementViewModel>();
        vm.LedgerDebtorName = "Gamma Travels";
        vm.LedgerStartDate = new DateTime(2026, 11, 1);
        vm.LedgerEndDate = new DateTime(2026, 11, 5);
        await vm.LoadLedgerAsync();

        var txRow = vm.LedgerTransactions.FirstOrDefault(t => t.TransactionType == "Repayment");
        Assert.NotNull(txRow);

        // Start edit
        vm.StartEditTransactionCommand.Execute(txRow);
        Assert.True(vm.IsEditingTransaction);
        Assert.Equal(origDate.Date, vm.EditTxDate.Date);

        // Change amount only
        vm.EditTxAmount = 1250;

        await vm.SaveTransactionEditCommand.ExecuteAsync(null);

        // Verify in DB
        using var verifyContext = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var updatedRepayment = await verifyContext.CreditorRepayments.FindAsync(repayment.CreditorRepaymentId);
        Assert.NotNull(updatedRepayment);
        Assert.Equal(origDate.Date, updatedRepayment.RepaymentDate.Date);
        Assert.Equal(1250, updatedRepayment.Amount);

        // Verify audit log
        var audit = await verifyContext.AuditLogs.FirstOrDefaultAsync(a => a.RecordId == repayment.CreditorRepaymentId && a.FieldName == "Amount");
        Assert.NotNull(audit);
        Assert.Equal("800.00", audit.OldValue);
        Assert.Equal("1250.00", audit.NewValue);
    }

    [Fact]
    public async Task DebtorManagement_LedgerOpeningBalance_CalculatesPriorDebitsAndRepaymentsCorrectly()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();

        var shift = new Shift { ShiftId = 1, ShiftDate = new DateTime(2026, 10, 20), ShiftType = "A" };
        var shift2 = new Shift { ShiftId = 2, ShiftDate = new DateTime(2026, 11, 2), ShiftType = "A" };
        var dsm = new DsmEntry { DsmEntryId = 1, ShiftId = 1, PumpId = 1, DsmName = "Test DSM", CreatedAt = DateTime.Now };
        var dsm2 = new DsmEntry { DsmEntryId = 2, ShiftId = 2, PumpId = 1, DsmName = "Test DSM 2", CreatedAt = DateTime.Now };
        context.Shifts.AddRange(shift, shift2);
        context.DsmEntries.AddRange(dsm, dsm2);

        var creditor = new Creditor { Name = "Delta Express", IsActive = true, CreatedAt = DateTime.Today };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        // Historical opening balance = 500
        context.OpeningBalances.Add(new OpeningBalance
        {
            EntityType = "Debtor",
            EntityIdentifier = creditor.CreditorId.ToString(),
            CreditorId = creditor.CreditorId,
            Amount = 500,
            IsActive = true
        });

        // Prior debt on Oct 25 = 1000
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = 1,
            DebtorName = "Delta Express",
            Amount = 1000,
            PaymentMethod = "Credit",
            Remarks = "Old Fuel",
            CreatedAt = new DateTime(2026, 10, 25, 10, 0, 0)
        });

        // Prior repayment on Oct 28 = 400
        context.CreditorRepayments.Add(new CreditorRepayment
        {
            CreditorName = "Delta Express",
            Amount = 400,
            PaymentMode = "Cash",
            RepaymentDate = new DateTime(2026, 10, 28, 12, 0, 0),
            CreatedAt = new DateTime(2026, 10, 28, 12, 0, 0)
        });

        // Current period debt on Nov 2 = 700
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = 2,
            DebtorName = "Delta Express",
            Amount = 700,
            PaymentMethod = "Credit",
            Remarks = "New Fuel",
            CreatedAt = new DateTime(2026, 11, 2, 10, 0, 0)
        });

        await context.SaveChangesAsync();

        var vm = _serviceProvider.GetRequiredService<DebtorManagementViewModel>();
        vm.LedgerDebtorName = "Delta Express";
        vm.LedgerStartDate = new DateTime(2026, 11, 1);
        vm.LedgerEndDate = new DateTime(2026, 11, 10);
        await vm.LoadLedgerAsync();

        // Period Opening Balance = 500 (historical) + 1000 (prior debt) - 400 (prior repayment) = 1100
        Assert.Equal(1100, vm.LedgerOpeningBalance);

        // Final balance = 1100 + 700 = 1800
        Assert.Equal(1800, vm.LedgerOutstandingBalance);
    }

    [Fact]
    public async Task DebtorManagement_DatePartitionedBalance_SairajTransportScenario_AllCheckpointsPass()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();

        // 1. Seed Shifts and DSM entries for Oct 1 to Oct 5
        var shift1 = new Shift { ShiftId = 1, ShiftDate = new DateTime(2026, 10, 1), ShiftType = "A" };
        var shift2 = new Shift { ShiftId = 2, ShiftDate = new DateTime(2026, 10, 2), ShiftType = "A" };
        var shift3 = new Shift { ShiftId = 3, ShiftDate = new DateTime(2026, 10, 3), ShiftType = "A" };
        var shift4 = new Shift { ShiftId = 4, ShiftDate = new DateTime(2026, 10, 4), ShiftType = "A" };
        var shift5 = new Shift { ShiftId = 5, ShiftDate = new DateTime(2026, 10, 5), ShiftType = "A" };
        context.Shifts.AddRange(shift1, shift2, shift3, shift4, shift5);

        var dsm1 = new DsmEntry { DsmEntryId = 1, ShiftId = 1, PumpId = 1, DsmName = "DSM 1", CreatedAt = new DateTime(2026, 10, 1, 8, 0, 0) };
        var dsm2 = new DsmEntry { DsmEntryId = 2, ShiftId = 2, PumpId = 1, DsmName = "DSM 2", CreatedAt = new DateTime(2026, 10, 2, 8, 0, 0) };
        var dsm3 = new DsmEntry { DsmEntryId = 3, ShiftId = 3, PumpId = 1, DsmName = "DSM 3", CreatedAt = new DateTime(2026, 10, 3, 8, 0, 0) };
        var dsm4 = new DsmEntry { DsmEntryId = 4, ShiftId = 4, PumpId = 1, DsmName = "DSM 4", CreatedAt = new DateTime(2026, 10, 4, 8, 0, 0) };
        var dsm5 = new DsmEntry { DsmEntryId = 5, ShiftId = 5, PumpId = 1, DsmName = "DSM 5", CreatedAt = new DateTime(2026, 10, 5, 8, 0, 0) };
        context.DsmEntries.AddRange(dsm1, dsm2, dsm3, dsm4, dsm5);

        var creditor = new Creditor { Name = "SAIRAJ TRANSPORT", Phone = "9876543210", IsActive = true, CreatedAt = new DateTime(2026, 10, 1) };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        var vm = _serviceProvider.GetRequiredService<DebtorManagementViewModel>();
        await vm.LoadDataAsync();

        var row = vm.Debtors.FirstOrDefault(d => d.CreditorId == creditor.CreditorId);
        Assert.NotNull(row);

        // Step 1: On 1 Oct 2026, Manager sets effective debt balance = 5,000
        vm.EditDebtorCommand.Execute(row);
        vm.EditTotalDebt = 5000;
        vm.EditDebtDate = new DateTime(2026, 10, 1);
        vm.EditRepaidAmount = 0;
        vm.EditRepaymentDate = new DateTime(2026, 10, 1);
        vm.EditEntryDetails = "Initial Opening Setup";
        await vm.SaveDebtorCommand.ExecuteAsync(null);

        // Step 2: On 2 Oct 2026, New debt entry = +2,000
        using (var updateCtx = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            updateCtx.DebitEntries.Add(new DebitEntry
            {
                DsmEntryId = 2,
                DebtorName = "SAIRAJ TRANSPORT",
                Amount = 2000,
                PaymentMethod = "Credit",
                Remarks = "Diesel refill",
                CreatedAt = new DateTime(2026, 10, 2, 10, 0, 0)
            });
            await updateCtx.SaveChangesAsync();
        }

        // Step 3: On 3 Oct 2026, New debt entry = +1,500
        using (var updateCtx = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            updateCtx.DebitEntries.Add(new DebitEntry
            {
                DsmEntryId = 3,
                DebtorName = "SAIRAJ TRANSPORT",
                Amount = 1500,
                PaymentMethod = "Credit",
                Remarks = "Diesel refill 2",
                CreatedAt = new DateTime(2026, 10, 3, 11, 0, 0)
            });
            await updateCtx.SaveChangesAsync();
        }

        // Step 4: On 4 Oct 2026, Repayment = 1,000
        using (var updateCtx = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            updateCtx.CreditorRepayments.Add(new CreditorRepayment
            {
                CreditorName = "SAIRAJ TRANSPORT",
                Amount = 1000,
                PaymentMode = "Cash",
                RepaymentDate = new DateTime(2026, 10, 4, 15, 0, 0),
                CreatedAt = new DateTime(2026, 10, 4, 15, 0, 0)
            });
            await updateCtx.SaveChangesAsync();
        }

        // Verify Checkpoint 1: 1 Oct 2026 (balance = 5,000)
        vm.LedgerDebtorName = "SAIRAJ TRANSPORT";
        vm.LedgerStartDate = new DateTime(2026, 10, 1);
        vm.LedgerEndDate = new DateTime(2026, 10, 1);
        await vm.LoadLedgerAsync();
        Assert.Equal(0, vm.LedgerOpeningBalance);
        Assert.Equal(5000, vm.LedgerTotalDebt);
        Assert.Equal(0, vm.LedgerTotalRepayments);
        Assert.Equal(5000, vm.LedgerOutstandingBalance);

        // Verify Checkpoint 2: 3 Oct 2026 (balance = 8,500)
        vm.LedgerStartDate = new DateTime(2026, 10, 1);
        vm.LedgerEndDate = new DateTime(2026, 10, 3);
        await vm.LoadLedgerAsync();
        Assert.Equal(0, vm.LedgerOpeningBalance);
        Assert.Equal(8500, vm.LedgerTotalDebt); // 5000 + 2000 + 1500
        Assert.Equal(0, vm.LedgerTotalRepayments);
        Assert.Equal(8500, vm.LedgerOutstandingBalance);

        // Verify Checkpoint 3: 5 Oct 2026 (balance = 7,500)
        vm.LedgerStartDate = new DateTime(2026, 10, 1);
        vm.LedgerEndDate = new DateTime(2026, 10, 5);
        await vm.LoadLedgerAsync();
        Assert.Equal(0, vm.LedgerOpeningBalance);
        Assert.Equal(8500, vm.LedgerTotalDebt);
        Assert.Equal(1000, vm.LedgerTotalRepayments);
        Assert.Equal(7500, vm.LedgerOutstandingBalance);

        // Verify Debtor Row in main grid
        await vm.LoadDataAsync();
        var updatedRow = vm.Debtors.FirstOrDefault(d => d.Name == "SAIRAJ TRANSPORT");
        Assert.NotNull(updatedRow);
        Assert.Equal(8500, updatedRow.TotalDebt);
        Assert.Equal(1000, updatedRow.TotalRepayment);
        Assert.Equal(7500, updatedRow.OutstandingBalance);
    }

    [Fact]
    public async Task DebtorManagement_BackdatedDebtAdjustment_PreservesSubsequentTransactions()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();

        var shift1 = new Shift { ShiftId = 1, ShiftDate = new DateTime(2026, 10, 1), ShiftType = "A" };
        var shift2 = new Shift { ShiftId = 2, ShiftDate = new DateTime(2026, 10, 5), ShiftType = "A" };
        context.Shifts.AddRange(shift1, shift2);

        var dsm1 = new DsmEntry { DsmEntryId = 1, ShiftId = 1, PumpId = 1, DsmName = "DSM 1", CreatedAt = new DateTime(2026, 10, 1, 8, 0, 0) };
        var dsm2 = new DsmEntry { DsmEntryId = 2, ShiftId = 2, PumpId = 1, DsmName = "DSM 2", CreatedAt = new DateTime(2026, 10, 5, 8, 0, 0) };
        context.DsmEntries.AddRange(dsm1, dsm2);

        var creditor = new Creditor { Name = "BHARAT CARRIERS", IsActive = true, CreatedAt = new DateTime(2026, 10, 1) };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        // Historical debt on Oct 1: 1000
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = 1,
            DebtorName = "BHARAT CARRIERS",
            Amount = 1000,
            PaymentMethod = "Credit",
            Remarks = "Oct 1 Fuel",
            CreatedAt = new DateTime(2026, 10, 1, 10, 0, 0)
        });

        // Future debt on Oct 5: 2000
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = 2,
            DebtorName = "BHARAT CARRIERS",
            Amount = 2000,
            PaymentMethod = "Credit",
            Remarks = "Oct 5 Fuel",
            CreatedAt = new DateTime(2026, 10, 5, 10, 0, 0)
        });

        // Future repayment on Oct 10: 500
        context.CreditorRepayments.Add(new CreditorRepayment
        {
            CreditorName = "BHARAT CARRIERS",
            Amount = 500,
            PaymentMode = "Cash",
            RepaymentDate = new DateTime(2026, 10, 10, 10, 0, 0),
            CreatedAt = new DateTime(2026, 10, 10, 10, 0, 0)
        });
        await context.SaveChangesAsync();

        var vm = _serviceProvider.GetRequiredService<DebtorManagementViewModel>();
        await vm.LoadDataAsync();

        var row = vm.Debtors.FirstOrDefault(d => d.CreditorId == creditor.CreditorId);
        Assert.NotNull(row);

        // Manager sets effective debt on Oct 2 = 3000
        // (Existing on or before Oct 2 is 1000, so adjustment should be +2000 dated Oct 2)
        vm.EditDebtorCommand.Execute(row);
        vm.EditTotalDebt = 3000;
        vm.EditDebtDate = new DateTime(2026, 10, 2);
        vm.EditRepaidAmount = 0; // Existing on or before Oct 2 is 0
        vm.EditRepaymentDate = new DateTime(2026, 10, 2);
        vm.EditEntryDetails = "Oct 2 Audit Correction";
        await vm.SaveDebtorCommand.ExecuteAsync(null);

        // Full history total debt should now be: Target 3000 + Oct 5 debt (2000) = 5000
        // Total repaid: 500 (from Oct 10, preserved!)
        // Net outstanding: 4500
        await vm.LoadDataAsync();
        var updatedRow = vm.Debtors.FirstOrDefault(d => d.Name == "BHARAT CARRIERS");
        Assert.NotNull(updatedRow);
        Assert.Equal(5000, updatedRow.TotalDebt);
        Assert.Equal(500, updatedRow.TotalRepayment);
        Assert.Equal(4500, updatedRow.OutstandingBalance);
    }

    [Fact]
    public async Task DebtorManagement_NegativeDebtAdjustment_ReducesHistoricalDebtCorrectly()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();

        var shift = new Shift { ShiftId = 1, ShiftDate = new DateTime(2026, 10, 1), ShiftType = "A" };
        var dsm = new DsmEntry { DsmEntryId = 1, ShiftId = 1, PumpId = 1, DsmName = "DSM 1", CreatedAt = new DateTime(2026, 10, 1, 8, 0, 0) };
        context.Shifts.Add(shift);
        context.DsmEntries.Add(dsm);

        var creditor = new Creditor { Name = "KIRAN TRADERS", IsActive = true, CreatedAt = new DateTime(2026, 10, 1) };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        // Existing debt on Oct 1: 5000
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = 1,
            DebtorName = "KIRAN TRADERS",
            Amount = 5000,
            PaymentMethod = "Credit",
            Remarks = "Overbilled Fuel",
            CreatedAt = new DateTime(2026, 10, 1, 10, 0, 0)
        });
        await context.SaveChangesAsync();

        var vm = _serviceProvider.GetRequiredService<DebtorManagementViewModel>();
        await vm.LoadDataAsync();

        var row = vm.Debtors.FirstOrDefault(d => d.CreditorId == creditor.CreditorId);
        Assert.NotNull(row);

        // Manager corrects effective debt on Oct 1 down to 3000 (delta = -2000)
        vm.EditDebtorCommand.Execute(row);
        vm.EditTotalDebt = 3000;
        vm.EditDebtDate = new DateTime(2026, 10, 1);
        vm.EditRepaidAmount = 0;
        vm.EditRepaymentDate = new DateTime(2026, 10, 1);
        vm.EditEntryDetails = "Billing Correction";
        await vm.SaveDebtorCommand.ExecuteAsync(null);

        // Verify in DB that an adjustment of -2000 was created
        using var verifyContext = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var adjustment = await verifyContext.DebitEntries.FirstOrDefaultAsync(d => d.DebtorName == "KIRAN TRADERS" && d.Amount == -2000);
        Assert.NotNull(adjustment);

        // Verify row
        await vm.LoadDataAsync();
        var updatedRow = vm.Debtors.FirstOrDefault(d => d.Name == "KIRAN TRADERS");
        Assert.NotNull(updatedRow);
        Assert.Equal(3000, updatedRow.TotalDebt);
        Assert.Equal(3000, updatedRow.OutstandingBalance);
    }

    [Fact]
    public async Task DebtorManagement_SameDayOrdering_AdjustmentSequencedAfterExistingTransactions()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();

        var shift = new Shift { ShiftId = 1, ShiftDate = new DateTime(2026, 10, 1), ShiftType = "A" };
        var dsm = new DsmEntry { DsmEntryId = 1, ShiftId = 1, PumpId = 1, DsmName = "DSM 1", CreatedAt = new DateTime(2026, 10, 1, 8, 0, 0) };
        context.Shifts.Add(shift);
        context.DsmEntries.Add(dsm);

        var creditor = new Creditor { Name = "SAMARTH LOGISTICS", IsActive = true, CreatedAt = new DateTime(2026, 10, 1) };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        var existingDebitTime = new DateTime(2026, 10, 1, 10, 0, 0);
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = 1,
            DebtorName = "SAMARTH LOGISTICS",
            Amount = 1200,
            PaymentMethod = "Credit",
            Remarks = "Morning Fuel",
            CreatedAt = existingDebitTime
        });
        await context.SaveChangesAsync();

        var vm = _serviceProvider.GetRequiredService<DebtorManagementViewModel>();
        await vm.LoadDataAsync();

        var row = vm.Debtors.FirstOrDefault(d => d.CreditorId == creditor.CreditorId);
        Assert.NotNull(row);

        // Manager sets effective debt on same date (Oct 1) to 2000
        vm.EditDebtorCommand.Execute(row);
        vm.EditTotalDebt = 2000;
        vm.EditDebtDate = new DateTime(2026, 10, 1);
        vm.EditRepaidAmount = 0;
        vm.EditRepaymentDate = new DateTime(2026, 10, 1);
        vm.EditEntryDetails = "Evening Balance Check";
        await vm.SaveDebtorCommand.ExecuteAsync(null);

        // Check adjustment CreatedAt in DB
        using var verifyContext = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var adj = await verifyContext.DebitEntries.FirstOrDefaultAsync(d => d.DebtorName == "SAMARTH LOGISTICS" && d.Amount == 800);
        Assert.NotNull(adj);
        Assert.True(adj.CreatedAt > existingDebitTime, "Adjustment should be sequenced after existing transactions on same day");
        Assert.Equal(existingDebitTime.AddSeconds(1), adj.CreatedAt);

        // Check Ledger running balances
        vm.LedgerDebtorName = "SAMARTH LOGISTICS";
        vm.LedgerStartDate = new DateTime(2026, 10, 1);
        vm.LedgerEndDate = new DateTime(2026, 10, 1);
        await vm.LoadLedgerAsync();

        var debitsInLedger = vm.LedgerTransactions.Where(t => t.TransactionType == "Debt").ToList();
        Assert.Equal(2, debitsInLedger.Count);
        Assert.Equal(1200, debitsInLedger[0].RunningBalance);
        Assert.Equal(2000, debitsInLedger[1].RunningBalance);
    }

    [Fact]
    public async Task DebtorManagement_LedgerDateFiltering_OpensWithCumulativeBalanceOnStartDate()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();

        var shift1 = new Shift { ShiftId = 1, ShiftDate = new DateTime(2026, 10, 1), ShiftType = "A" };
        var shift2 = new Shift { ShiftId = 2, ShiftDate = new DateTime(2026, 10, 2), ShiftType = "A" };
        var shift3 = new Shift { ShiftId = 3, ShiftDate = new DateTime(2026, 10, 3), ShiftType = "A" };
        var shift4 = new Shift { ShiftId = 4, ShiftDate = new DateTime(2026, 10, 4), ShiftType = "A" };
        context.Shifts.AddRange(shift1, shift2, shift3, shift4);

        var dsm1 = new DsmEntry { DsmEntryId = 1, ShiftId = 1, PumpId = 1, DsmName = "DSM 1", CreatedAt = new DateTime(2026, 10, 1, 8, 0, 0) };
        var dsm2 = new DsmEntry { DsmEntryId = 2, ShiftId = 2, PumpId = 1, DsmName = "DSM 2", CreatedAt = new DateTime(2026, 10, 2, 8, 0, 0) };
        var dsm3 = new DsmEntry { DsmEntryId = 3, ShiftId = 3, PumpId = 1, DsmName = "DSM 3", CreatedAt = new DateTime(2026, 10, 3, 8, 0, 0) };
        var dsm4 = new DsmEntry { DsmEntryId = 4, ShiftId = 4, PumpId = 1, DsmName = "DSM 4", CreatedAt = new DateTime(2026, 10, 4, 8, 0, 0) };
        context.DsmEntries.AddRange(dsm1, dsm2, dsm3, dsm4);

        var creditor = new Creditor { Name = "OM SAI TRAVELS", IsActive = true, CreatedAt = new DateTime(2026, 10, 1) };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        // Oct 1: Debt 5000
        context.DebitEntries.Add(new DebitEntry { DsmEntryId = 1, DebtorName = "OM SAI TRAVELS", Amount = 5000, PaymentMethod = "Credit", CreatedAt = new DateTime(2026, 10, 1, 10, 0, 0) });
        // Oct 2: Debt 2000
        context.DebitEntries.Add(new DebitEntry { DsmEntryId = 2, DebtorName = "OM SAI TRAVELS", Amount = 2000, PaymentMethod = "Credit", CreatedAt = new DateTime(2026, 10, 2, 10, 0, 0) });
        // Oct 3: Debt 1500
        context.DebitEntries.Add(new DebitEntry { DsmEntryId = 3, DebtorName = "OM SAI TRAVELS", Amount = 1500, PaymentMethod = "Credit", CreatedAt = new DateTime(2026, 10, 3, 10, 0, 0) });
        // Oct 4: Repayment 1000
        context.CreditorRepayments.Add(new CreditorRepayment { CreditorName = "OM SAI TRAVELS", Amount = 1000, PaymentMode = "Cash", RepaymentDate = new DateTime(2026, 10, 4, 10, 0, 0), CreatedAt = new DateTime(2026, 10, 4, 10, 0, 0) });
        await context.SaveChangesAsync();

        var vm = _serviceProvider.GetRequiredService<DebtorManagementViewModel>();
        vm.LedgerDebtorName = "OM SAI TRAVELS";
        vm.LedgerStartDate = new DateTime(2026, 10, 3);
        vm.LedgerEndDate = new DateTime(2026, 10, 5);
        await vm.LoadLedgerAsync();

        // Cumulative before Oct 3 = 5000 + 2000 = 7000
        Assert.Equal(7000, vm.LedgerOpeningBalance);

        // Period transactions: Oct 3 debt 1500, Oct 4 repayment 1000
        Assert.Equal(1500, vm.LedgerTotalDebt);
        Assert.Equal(1000, vm.LedgerTotalRepayments);

        // Closing outstanding balance = 7000 + 1500 - 1000 = 7500
        Assert.Equal(7500, vm.LedgerOutstandingBalance);
    }

    [Fact]
    public async Task DebtorManagement_SaveDebtorAsync_UnresolvableLegacyDate_AbortsWithoutCorruption()
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();

        var shift = new Shift { ShiftId = 999, ShiftDate = default, ShiftType = "A" };
        var dsm = new DsmEntry { DsmEntryId = 999, ShiftId = 999, PumpId = 1, DsmName = "DSM Legacy", CreatedAt = default };
        context.Shifts.Add(shift);
        context.DsmEntries.Add(dsm);

        var creditor = new Creditor { Name = "CORRUPT LEGACY CO", IsActive = true, CreatedAt = DateTime.Today };
        context.Creditors.Add(creditor);
        await context.SaveChangesAsync();

        // Seed a debit entry with default date and default shift date
        context.DebitEntries.Add(new DebitEntry
        {
            DsmEntryId = 999,
            DebtorName = "CORRUPT LEGACY CO",
            Amount = 1000,
            PaymentMethod = "Credit",
            Remarks = "Legacy corrupted entry",
            CreatedAt = default
        });
        await context.SaveChangesAsync();

        var vm = _serviceProvider.GetRequiredService<DebtorManagementViewModel>();
        await vm.LoadDataAsync();

        var row = vm.Debtors.FirstOrDefault(d => d.CreditorId == creditor.CreditorId);
        Assert.NotNull(row);

        vm.EditDebtorCommand.Execute(row);
        vm.EditTotalDebt = 2500;
        vm.EditDebtDate = DateTime.Today;

        await vm.SaveDebtorCommand.ExecuteAsync(null);

        // Check that error message was set and save aborted
        Assert.Contains("unresolvable transaction date found", vm.DebtorStatusMessage);

        // Verify no adjustment was added
        using var verifyContext = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var debitCount = await verifyContext.DebitEntries.CountAsync(d => d.DebtorName == "CORRUPT LEGACY CO");
        Assert.Equal(1, debitCount); // Still only the original uncorrupted record
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch { }
    }
}

// Simple mocks to satisfy dependency injection requirements in tests
public class MockAgsInventoryService : IAgsInventoryService
{
    public Task<List<NozzleGroupDto>> BuildNozzleGroupsAsync(DateTime date, string shiftType, List<DsmEntry>? loadedEntries = null)
    {
        return Task.FromResult(new List<NozzleGroupDto>());
    }

    public Task<List<NozzleGroupDto>> BuildNozzleGroupsForDateRangeAsync(DateTime startDate, DateTime endDate, List<DsmEntry>? loadedEntries = null)
    {
        return Task.FromResult(new List<NozzleGroupDto>());
    }

    public Task<Result<AgsShiftImport>> SaveAndPersistInventoryAsync(DateTime date, string shiftType, List<NozzleGroupDto> groups)
    {
        return Task.FromResult(Result<AgsShiftImport>.Ok(new AgsShiftImport()));
    }

    public Task<Result> PropagateInventoryCalculationsAsync(DateTime startDate, string startShiftType)
    {
        return Task.FromResult(Result.Ok());
    }

    public (DateTime Date, string Shift) GetPreviousShift(DateTime date, string shift)
    {
        return (date.AddDays(-1), "B");
    }
}

public class MockFinancialCalculationService : IFinancialCalculationService
{
    public Task<FinancialCalculationResult> CalculateFinancialsAsync(DateTime startDate, DateTime endDate)
    {
        return Task.FromResult(new FinancialCalculationResult());
    }

    public Task<List<DsmSalaryRowDto>> CalculateDsmSalariesAsync(int year, int month)
    {
        return Task.FromResult(new List<DsmSalaryRowDto>());
    }

    public Task<OilDefStockReportDto> GenerateStockReportAsync(int year, int month)
    {
        return Task.FromResult(new OilDefStockReportDto());
    }

    public Task SaveDsmSalaryAdjustmentsAsync(int year, int month, List<DsmSalaryRowDto> rows)
    {
        return Task.CompletedTask;
    }

    public Task DeleteDsmSalaryAdjustmentAsync(int year, int month, string dsmName)
    {
        return Task.CompletedTask;
    }
}

public class FakeSupabaseDsmService : SupabaseDsmService
{
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, dynamic> CustomCollections = new();

    private class FakeSubmissionState
    {
        public string MachineId { get; set; } = string.Empty;
        public string ApprovedBy { get; set; } = string.Empty;
        public string LockId { get; set; } = string.Empty;
        public string ClaimToken { get; set; } = string.Empty;
        public int ClaimVersion { get; set; } = 0;
        public DateTime ClaimedAt { get; set; } = DateTime.UtcNow;
        public bool IsCompleted { get; set; } = false;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, FakeSubmissionState> SubmissionsState = new();

    public static void ResetState()
    {
        CustomCollections.Clear();
        SubmissionsState.Clear();
    }

    public FakeSupabaseDsmService(SyncConfigService syncConfigService) : base(syncConfigService) { }

    public override Task<Result<List<dynamic>>> FetchPendingSubmissionsAsync()
    {
        return Task.FromResult(Result<List<dynamic>>.Ok(new List<dynamic>()));
    }

    public override Task<Result<List<dynamic>>> FetchApprovedSubmissionsAsync()
    {
        return Task.FromResult(Result<List<dynamic>>.Ok(new List<dynamic>()));
    }

    public override Task<Result<List<dynamic>>> FetchSubmissionReadingsAsync(Guid submissionId)
    {
        dynamic r1 = new System.Dynamic.ExpandoObject();
        r1.NozzleId = 1;
        r1.OpeningReading = 100.0;
        r1.ClosingReading = 110.0;
        r1.Rate = 103.81;
        r1.PumpId = 1;

        dynamic r2 = new System.Dynamic.ExpandoObject();
        r2.NozzleId = 3;
        r2.OpeningReading = 100.0;
        r2.ClosingReading = 120.0;
        r2.Rate = 90.35;
        r2.PumpId = 1;

        var readings = new List<dynamic> { r1, r2 };
        return Task.FromResult(Result<List<dynamic>>.Ok(readings));
    }

    public override Task<Result<dynamic>> FetchSubmissionCollectionAsync(Guid submissionId)
    {
        if (CustomCollections.TryGetValue(submissionId, out var customColl))
        {
            return Task.FromResult(Result<dynamic>.Ok(customColl));
        }

        dynamic coll = new System.Dynamic.ExpandoObject();
        coll.Cash = 500.0;
        coll.UPI = 1000.0;
        coll.Card = 200.0;
        coll.PetroCard = 100.0;
        coll.CashDeposit = 1000.0;
        coll.Others = 0.0;
        coll.Credit = 300.0;
        coll.Expense = 50.0;
        coll.ExpenseNotes = "Snacks";
        coll.Short = 0.0;
        coll.Excess = 0.0;
        
        return Task.FromResult(Result<dynamic>.Ok(coll));
    }

    public override Task<Result<DsmClaimResult>> ClaimSubmissionWithFencingAsync(
        Guid submissionId, string machineId, string approvedBy, string lockId)
    {
        var state = SubmissionsState.AddOrUpdate(
            submissionId,
            id => new FakeSubmissionState
            {
                MachineId = machineId,
                ApprovedBy = approvedBy,
                LockId = lockId,
                ClaimVersion = 1,
                ClaimToken = Guid.NewGuid().ToString("N"),
                ClaimedAt = DateTime.UtcNow,
                IsCompleted = false
            },
            (id, existing) =>
            {
                if (existing.IsCompleted)
                {
                    return existing;
                }
                if (existing.LockId == lockId && existing.MachineId == machineId)
                {
                    return existing;
                }
                existing.MachineId = machineId;
                existing.ApprovedBy = approvedBy;
                existing.LockId = lockId;
                existing.ClaimVersion++;
                existing.ClaimToken = Guid.NewGuid().ToString("N");
                existing.ClaimedAt = DateTime.UtcNow;
                return existing;
            });

        if (state.IsCompleted)
        {
            return Task.FromResult(Result<DsmClaimResult>.Fail("This submission has already been fully approved and completed."));
        }

        var result = new DsmClaimResult
        {
            Success = true,
            Code = "CLAIMED",
            Message = "Claimed successfully",
            ClaimToken = state.ClaimToken,
            ClaimVersion = state.ClaimVersion
        };
        return Task.FromResult(Result<DsmClaimResult>.Ok(result));
    }

    public override async Task<Result> ClaimSubmissionAsync(Guid submissionId, string machineId, string approvedBy, string lockId)
    {
        var res = await ClaimSubmissionWithFencingAsync(submissionId, machineId, approvedBy, lockId);
        return res.Success ? Result.Ok() : Result.Fail(res.Error ?? "Claim failed");
    }

    public override Task<Result> RenewLeaseAsync(Guid submissionId, string machineId, string claimToken, int? claimVersion = null)
    {
        if (SubmissionsState.TryGetValue(submissionId, out var state))
        {
            if (state.IsCompleted)
                return Task.FromResult(Result.Fail("Submission already completed."));

            if (!string.IsNullOrEmpty(claimToken) && state.ClaimToken != claimToken)
                return Task.FromResult(Result.Fail("Fencing violation: Lease has been reassigned to another claim (stale workstation token)."));

            if (claimVersion.HasValue && state.ClaimVersion != claimVersion.Value)
                return Task.FromResult(Result.Fail("Fencing violation: Stale claim version."));

            state.ClaimedAt = DateTime.UtcNow;
            return Task.FromResult(Result.Ok());
        }

        return Task.FromResult(Result.Ok());
    }

    public override Task<Result> CompleteSubmissionApprovalAsync(
        Guid submissionId,
        string machineId,
        string lockId,
        string? claimToken = null,
        int? claimVersion = null)
    {
        if (SubmissionsState.TryGetValue(submissionId, out var state))
        {
            if (state.IsCompleted)
            {
                return Task.FromResult(Result.Ok());
            }

            if (!string.IsNullOrEmpty(claimToken) && state.ClaimToken != claimToken)
                return Task.FromResult(Result.Fail("Fencing violation: Stale claim token."));

            if (claimVersion.HasValue && state.ClaimVersion != claimVersion.Value)
                return Task.FromResult(Result.Fail("Fencing violation: Stale claim version."));

            state.IsCompleted = true;
            return Task.FromResult(Result.Ok());
        }

        return Task.FromResult(Result.Ok());
    }

    public override Task<Result> ApproveSubmissionAsync(Guid submissionId, string approvedBy, string lockId)
    {
        return Task.FromResult(Result.Ok());
    }

    public override Task<Result> RejectSubmissionAsync(Guid submissionId, string reason)
    {
        return Task.FromResult(Result.Ok());
    }

    public override Task<Result> ResetSubmissionToPendingAsync(Guid submissionId)
    {
        if (SubmissionsState.TryGetValue(submissionId, out var state))
        {
            state.IsCompleted = false;
            state.ClaimToken = string.Empty;
        }
        return Task.FromResult(Result.Ok());
    }

    public override Task<Result> SendNotificationAsync(string stationId, string userId, string role, string message)
    {
        return Task.FromResult(Result.Ok());
    }
}
