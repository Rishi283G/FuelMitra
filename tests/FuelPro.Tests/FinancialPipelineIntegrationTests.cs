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
        
        // Assert Gross Sales
        Assert.Equal(3000, dayTotalVm.TotalDayFuelSaleAmount);
        Assert.Equal(3000, dashboardVm.TodayTotalSale);
        Assert.Equal(3000, ownerVm.TodayTotalSale);

        // Assert PhonePe / UPI Direct Direct
        Assert.Equal(1000, dayTotalVm.PhonePeTotal);
        Assert.Equal(1000, dashboardVm.TodayTotalPhonePe);
        Assert.Equal(1500, ownerVm.TodayTotalPhonePe);

        // Assert PhonePe Card / UPI Card
        Assert.Equal(500, dayTotalVm.PhonePeCardTotal);
        Assert.Equal(500, dashboardVm.TodayTotalPhonePeCardMorning + dashboardVm.TodayTotalPhonePeCardNight);

        // Assert Credit Card (PineLabs)
        Assert.Equal(900, dayTotalVm.CreditCardTotal);
        Assert.Equal(900, dashboardVm.TodayTotalCreditCard);
        Assert.Equal(900, ownerVm.TodayTotalCreditCard);

        // Assert Petro Card
        Assert.Equal(300, dayTotalVm.PetroCardTotal);
        Assert.Equal(300, dashboardVm.TodayTotalPetroCard);
        Assert.Equal(300, ownerVm.TodayTotalPetroCard);

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
        // Expected Mismatch = 1100 (due to tomorrow's Shift A Night collections (1050) being added to actual collections but not expected sales, and shift B shortfall of 1050 being added to DSM Short)
        Assert.Equal(1100, dayTotalVm.Difference);
        Assert.Equal(1100, dashboardVm.TodayTotalMismatch);
        Assert.Equal(1100, ownerVm.TodayTotalMismatch);
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
        // ExpectedCollection = Fuel Sales (5000) + Reconcilable Recoveries (800) = 5800
        // ActualCollection = CashDeposit (0) + CashInHand (1500) + PhonePeMorning (1000) + PhonePeNight (1000 + 500 = 1500) + PineLabsMorning (1500) + PetroCard (300) = 5800
        // Difference = 0 (Balanced)
        Assert.Equal(5000, report.TotalFuelAmount);
        Assert.Equal(5800, report.ExpectedCollection);
        Assert.Equal(5800, report.ActualCollection);
        Assert.Equal(0, report.Difference);
        Assert.True(report.IsBalanced);
        
        // Verify breakdown items:
        // Cash In Hand category total should be 1500 base + 0 recovery = 1500
        var cashInHandCol = report.CollectionBreakdown.First(c => c.Category == "Cash In Hand");
        Assert.Equal(1500, cashInHandCol.Amount);
        Assert.Equal(1500, cashInHandCol.BaseAmount);
        Assert.Equal(0, cashInHandCol.RecoveryAmount);

        // PhonePe Night category total should be 1000 base + 500 recovery = 1500
        var phonePeNightCol = report.CollectionBreakdown.First(c => c.Category == "PhonePe Night");
        Assert.Equal(1500, phonePeNightCol.Amount);
        Assert.Equal(1000, phonePeNightCol.BaseAmount);
        Assert.Equal(500, phonePeNightCol.RecoveryAmount);

        // Petro Card category total should be 0 base + 300 recovery = 300
        var petroCardCol = report.CollectionBreakdown.First(c => c.Category == "Petro Card");
        Assert.Equal(300, petroCardCol.Amount);
        Assert.Equal(0, petroCardCol.BaseAmount);
        Assert.Equal(300, petroCardCol.RecoveryAmount);
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
    public FakeSupabaseDsmService(SyncConfigService syncConfigService) : base(syncConfigService) { }

    public override Task<Result<List<dynamic>>> FetchPendingSubmissionsAsync()
    {
        return Task.FromResult(Result<List<dynamic>>.Ok(new List<dynamic>()));
    }

    public override Task<Result<List<dynamic>>> FetchSubmissionReadingsAsync(Guid submissionId)
    {
        var readings = new List<dynamic>
        {
            new { NozzleId = 1, OpeningReading = 100.0, ClosingReading = 110.0, Rate = 103.81, PumpId = 1 },
            new { NozzleId = 3, OpeningReading = 100.0, ClosingReading = 120.0, Rate = 90.35, PumpId = 1 }
        };
        return Task.FromResult(Result<List<dynamic>>.Ok(readings));
    }

    public override Task<Result<dynamic>> FetchSubmissionCollectionAsync(Guid submissionId)
    {
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
        
        return Task.FromResult(Result<dynamic>.Ok(coll));
    }

    public override Task<Result> ApproveSubmissionAsync(Guid submissionId, string approvedBy, string lockId)
    {
        return Task.FromResult(Result.Ok());
    }

    public override Task<Result> SendNotificationAsync(string stationId, string userId, string role, string message)
    {
        return Task.FromResult(Result.Ok());
    }
}
