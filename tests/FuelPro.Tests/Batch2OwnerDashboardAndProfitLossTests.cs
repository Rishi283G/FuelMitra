using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.Sync;
using FuelPro.UI;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;

namespace FuelPro.Tests;

public class Batch2OwnerDashboardAndProfitLossTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;

    public Batch2OwnerDashboardAndProfitLossTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_Batch2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "fuelPro_batch2.db");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    private IServiceProvider BuildTestServices(HttpMessageHandler? handler = null, bool useRealFinancialService = true)
    {
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(
            opt => opt.UseSqlite($"Data Source={_dbPath}"),
            ServiceLifetime.Transient);

        services.AddSingleton<ICredentialFileService>(
            new LocalCredentialFileService(Path.Combine(_tempDir, "credentials")));

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
        services.AddTransient<IOpeningBalanceRepository, OpeningBalanceRepository>();
        services.AddTransient<IFuelTankerRepository, FuelTankerRepository>();
        services.AddTransient<ITankDailyStockRepository, TankDailyStockRepository>();
        services.AddScoped<IShiftOtherCashRepository, ShiftOtherCashRepository>();
        services.AddScoped<IShiftFuelRateRepository, ShiftFuelRateRepository>();

        services.AddSingleton<AuthService>();
        services.AddTransient<DsmEntryService>();
        services.AddTransient<ShiftCalculationService>();
        services.AddSingleton<IDsmCalculationService, DsmCalculationService>();
        services.AddSingleton<IOwnerCalculationService, OwnerCalculationService>();
        services.AddSingleton<ITidCalculationService, TidCalculationService>();
        services.AddScoped<IShiftAggregationService, ShiftAggregationService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddTransient<IAgsInventoryService, MockAgsInventoryService>();
        if (useRealFinancialService)
            services.AddTransient<IFinancialCalculationService, FinancialCalculationService>();
        else
            services.AddTransient<IFinancialCalculationService, MockFinancialCalculationService>();
        services.AddTransient<PrintService>();
        services.AddTransient<ExcelExportService>();
        services.AddTransient<IAuditLogService, AuditLogService>();
        services.AddTransient<IDayLockService, DayLockService>();
        services.AddTransient<IFeatureToggleService, FeatureToggleService>();
        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddTransient<IStationConfigurationService, StationConfigurationService>();
        services.AddSingleton<IShiftCycleService, ShiftCycleService>();

        services.AddSingleton<SyncConfigService>();
        var httpClient = handler != null ? new HttpClient(handler) : new HttpClient();
        var supabaseHttp = new SupabaseHttpClient(httpClient);
        var defaultSettings = new SyncSettings
        {
            SyncEnabled = handler != null,
            SupabaseUrl = "https://mock.supabase.co",
            SupabaseApiKey = "mock-key",
            StationId = "MITALI-TEST-01",
            MachineId = "PC-01"
        };
        supabaseHttp.Configure(defaultSettings.SupabaseUrl, defaultSettings.SupabaseApiKey);
        services.AddSingleton(supabaseHttp);
        services.AddSingleton<StationPairingService>();
        services.AddSingleton<SyncEngine>();
        services.AddSingleton<DsmSubmissionPollingService>();
        services.AddSingleton<DraftService>();

        services.AddTransient<OwnerDashboardViewModel>();
        services.AddTransient<ProfitLossViewModel>();
        services.AddTransient<DebtorManagementViewModel>();

        var sp = services.BuildServiceProvider();
        typeof(App).GetProperty("Services")?.SetValue(null, sp);
        typeof(App).GetProperty("DbPath")?.SetValue(null, _dbPath);

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            db.Database.EnsureCreated();
            var now = DateTime.Now;
            var mappings = new List<PumpMapping>
            {
                new() { PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true, CreatedAt = now },
                new() { PumpId = 1, NozzleNumber = 5, FuelType = "Speed", TankName = "Speed - 20KL", IsActive = true, CreatedAt = now },
                new() { PumpId = 4, NozzleNumber = 9, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true, CreatedAt = now }
            };
            PumpConfiguration.InitializeFromDb(mappings);
            db.PumpMappings.AddRange(mappings);
            db.Settings.Add(new Setting
            {
                PumpStationName = "Mitali Service Station",
                StationDisplayName = "Mitali Service Station",
                HsdRate = 90.0,
                MsIRate = 100.0,
                MsIIRate = 105.0,
                CngRate = 85.0
            });
            db.SaveChanges();
        }
        sp.GetRequiredService<ICollectionTypeService>().GetAllCollectionTypesAsync().GetAwaiter().GetResult();
        _ = sp.GetRequiredService<IFeatureToggleService>().GetAllFeaturesAsync().GetAwaiter().GetResult();
        sp.GetRequiredService<SyncConfigService>().SaveSettingsAsync(defaultSettings).GetAwaiter().GetResult();

        return sp;
    }

    // ════════════════════════════════════════════════════════════════════════
    // ISSUE 5 — Owner Dashboard Date Range, Debtor Parity, PhonePeCard & Cache
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Issue5_OwnerDashboard_ExcludesNextDayEntriesFromCount_AndSeparatesPhonePeCard()
    {
        var sp = BuildTestServices();
        var day1 = new DateTime(2026, 11, 15);
        var day2 = day1.AddDays(1);

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var ppDirect = await db.CollectionTypes.FirstOrDefaultAsync(c => c.Code == "PHONEPE");
            if (ppDirect == null)
            {
                db.CollectionTypes.Add(new CollectionTypeMaster
                {
                    Code = "PHONEPE",
                    DisplayName = "PhonePe",
                    Category = "Online",
                    IsActive = true,
                    IsSystem = true,
                    DisplayOrder = 1
                });
            }
            else
            {
                ppDirect.IsActive = true;
            }

            var ppCard = await db.CollectionTypes.FirstOrDefaultAsync(c => c.Code == "PHONEPE_CARD");
            if (ppCard == null)
            {
                db.CollectionTypes.Add(new CollectionTypeMaster
                {
                    Code = "PHONEPE_CARD",
                    DisplayName = "PhonePe Card",
                    Category = "Online",
                    IsActive = true,
                    DisplayOrder = 2
                });
            }
            else
            {
                ppCard.IsActive = true;
            }

            var s1 = new Shift { ShiftDate = day1, ShiftType = "A" };
            var s2 = new Shift { ShiftDate = day1, ShiftType = "B" };
            var sNext = new Shift { ShiftDate = day2, ShiftType = "A" };
            db.Shifts.AddRange(s1, s2, sNext);
            await db.SaveChangesAsync();

            var e1 = new DsmEntry
            {
                ShiftId = s1.ShiftId,
                PumpId = 1,
                DsmName = "DSM One",
                GrossSales = 1000m,
                TotalCollection = 1000m,
                NozzleReadings = new List<NozzleReading>
                {
                    new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 10, SaleLitres = 10, Rate = 100, Amount = 1000 }
                },
                PaymentCollection = new PaymentCollection
                {
                    PhonePeMorning = 500,
                    PhonePeCardMorning = 250,
                    CashDeposit = 250
                },
                CashDenominations = new List<CashDenomination>
                {
                    new() { CashType = "Cash1", TotalAmount = 250 }
                }
            };
            var e2 = new DsmEntry
            {
                ShiftId = s2.ShiftId,
                PumpId = 1,
                DsmName = "DSM Two",
                GrossSales = 900m,
                TotalCollection = 900m,
                NozzleReadings = new List<NozzleReading>
                {
                    new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 10, ClosingReading = 19, SaleLitres = 9, Rate = 100, Amount = 900 }
                },
                PaymentCollection = new PaymentCollection
                {
                    PhonePeDay = 900
                }
            };
            var eTomorrow = new DsmEntry
            {
                ShiftId = sNext.ShiftId,
                PumpId = 1,
                DsmName = "Tomorrow DSM",
                GrossSales = 5000m,
                TotalCollection = 5000m,
                NozzleReadings = new List<NozzleReading>
                {
                    new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 19, ClosingReading = 69, SaleLitres = 50, Rate = 100, Amount = 5000 }
                }
            };
            db.DsmEntries.AddRange(e1, e2, eTomorrow);
            await db.SaveChangesAsync();
        }

        await sp.GetRequiredService<ICollectionTypeService>().GetAllCollectionTypesAsync();

        var ownerVm = sp.GetRequiredService<OwnerDashboardViewModel>();
        ownerVm.StartDate = day1;
        ownerVm.EndDate = day1;
        await ownerVm.LoadDataAsync(isBackgroundRefresh: false);

        // Must strictly count Day 1 entries (2), NEVER tomorrow's entry (would be 3 with AddDays(1))
        Assert.Equal(2, ownerVm.TotalDsmEntries);
        Assert.Equal(1900.0, ownerVm.TodayTotalSale, 2);
        Assert.Equal(1400.0, ownerVm.TodayTotalPhonePe, 2);

        // PhonePe Direct and PhonePe Card must remain separate lines in PaymentBreakdown (not conflated)
        var phonePeCardRow = ownerVm.PaymentBreakdown.FirstOrDefault(r =>
            r.CollectionMode.Contains("PhonePe", StringComparison.OrdinalIgnoreCase) &&
            r.CollectionMode.Contains("Card", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(phonePeCardRow);
        Assert.Equal(250.0, phonePeCardRow!.Amount, 2);
    }

    [Fact]
    public async Task Issue5_OwnerDashboard_OutstandingDebtors_MatchesDebtorManagement_AndClearsStaleCache()
    {
        var sp = BuildTestServices();
        var shiftDate = new DateTime(2026, 11, 10);

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var activeCred = new Creditor { Name = "Sharma Logistics", IsActive = true };
            var inactiveCred = new Creditor { Name = "Closed Account", IsActive = false };
            db.Creditors.AddRange(activeCred, inactiveCred);
            await db.SaveChangesAsync();

            db.OpeningBalances.Add(new OpeningBalance
            {
                EntityType = "Debtor",
                EntityIdentifier = activeCred.CreditorId.ToString(),
                CreditorId = activeCred.CreditorId,
                Amount = 10000.0,
                OpeningDate = new DateTime(2026, 11, 1),
                IsActive = true
            });

            var shift = new Shift { ShiftDate = shiftDate, ShiftType = "A" };
            db.Shifts.Add(shift);
            await db.SaveChangesAsync();

            var entry = new DsmEntry { ShiftId = shift.ShiftId, PumpId = 1, DsmName = "DSM Alpha" };
            entry.DebitEntries.Add(new DebitEntry { DebtorName = "Sharma Logistics", Amount = 5000.0 });
            entry.DebitEntries.Add(new DebitEntry { DebtorName = "Closed Account", Amount = 999.0 });
            db.DsmEntries.Add(entry);

            // Trade repayment (-2000) + DSM Loss staff repayment (-500 must NOT reduce trade debtors!)
            db.CreditorRepayments.AddRange(
                new CreditorRepayment { CreditorName = "Sharma Logistics", Amount = 2000.0, PaymentMode = "Cash", RepaymentDate = shiftDate },
                new CreditorRepayment { CreditorName = "DSM Loss (Ravi)", Amount = 500.0, PaymentMode = "Cash", RepaymentDate = shiftDate }
            );
            await db.SaveChangesAsync();
        }

        var ownerVm = sp.GetRequiredService<OwnerDashboardViewModel>();
        ownerVm.StartDate = shiftDate;
        ownerVm.EndDate = shiftDate;
        await ownerVm.LoadDataAsync(isBackgroundRefresh: false);

        var debtorVm = sp.GetRequiredService<DebtorManagementViewModel>();
        await debtorVm.LoadDataAsync();
        await debtorVm.LoadFinancialsCommand.ExecuteAsync(null);

        // Expected: Opening (10000) + Debit (5000) - Trade Repayment (2000) = 13000
        Assert.Equal(13000.0, ownerVm.OutstandingDebtors, 2);
        Assert.Equal(13000.0, debtorVm.OverallOutstandingBalance, 2);

        // Simulate new DebitEntry inserted by background sync in a separate DbContext scope
        using (var db2 = sp.GetRequiredService<FuelProDbContext>())
        {
            var entry = await db2.DsmEntries.FirstAsync();
            db2.DebitEntries.Add(new DebitEntry { DsmEntryId = entry.DsmEntryId, DebtorName = "Sharma Logistics", Amount = 1500.0 });
            await db2.SaveChangesAsync();
        }

        // Reloading the SAME OwnerDashboardViewModel must NOT read stale tracked entities
        await ownerVm.LoadDataAsync(isBackgroundRefresh: false);
        Assert.Equal(14500.0, ownerVm.OutstandingDebtors, 2);
    }

    [Fact]
    public async Task Issue5_OwnerDashboard_OutstandingDebtors_ExcludesZeroAndOverpaidBalances_MatchingDebtorManagement()
    {
        var sp = BuildTestServices();
        var shiftDate = new DateTime(2026, 11, 11);

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            var positiveCred = new Creditor { Name = "Positive Fleet", IsActive = true };
            var zeroCred = new Creditor { Name = "Settled Fleet", IsActive = true };
            var epsilonCred = new Creditor { Name = "Rounding Edge", IsActive = true };
            var overpaidCred = new Creditor { Name = "Overpaid Client", IsActive = true };
            db.Creditors.AddRange(positiveCred, zeroCred, epsilonCred, overpaidCred);
            await db.SaveChangesAsync();

            db.OpeningBalances.Add(new OpeningBalance
            {
                EntityType = "Debtor",
                EntityIdentifier = positiveCred.CreditorId.ToString(),
                CreditorId = positiveCred.CreditorId,
                Amount = 5000.0,
                OpeningDate = new DateTime(2026, 11, 1),
                IsActive = true
            });

            var shift = new Shift { ShiftDate = shiftDate, ShiftType = "A" };
            db.Shifts.Add(shift);
            await db.SaveChangesAsync();

            var entry = new DsmEntry { ShiftId = shift.ShiftId, PumpId = 1, DsmName = "DSM Alpha" };
            entry.DebitEntries.Add(new DebitEntry { DebtorName = "Positive Fleet", Amount = 3000.0 });
            entry.DebitEntries.Add(new DebitEntry { DebtorName = "Settled Fleet", Amount = 1200.0 });
            entry.DebitEntries.Add(new DebitEntry { DebtorName = "Rounding Edge", Amount = 100.0 });
            entry.DebitEntries.Add(new DebitEntry { DebtorName = "Overpaid Client", Amount = 1000.0 });
            entry.DebitEntries.Add(new DebitEntry { DebtorName = "Unregistered Positive", Amount = 600.0 });
            db.DsmEntries.Add(entry);

            db.CreditorRepayments.AddRange(
                new CreditorRepayment { CreditorName = "Positive Fleet", Amount = 1000.0, PaymentMode = "Cash", RepaymentDate = shiftDate }, // net +7000
                new CreditorRepayment { CreditorName = "Settled Fleet", Amount = 1200.0, PaymentMode = "Cash", RepaymentDate = shiftDate },   // net 0 (excluded)
                new CreditorRepayment { CreditorName = "Rounding Edge", Amount = 99.995, PaymentMode = "Cash", RepaymentDate = shiftDate }, // net 0.005 <= 0.01 (excluded)
                new CreditorRepayment { CreditorName = "Overpaid Client", Amount = 2500.0, PaymentMode = "Cash", RepaymentDate = shiftDate }, // net -1500 (excluded, never netted)
                new CreditorRepayment { CreditorName = "Overpaid Unregistered", Amount = 800.0, PaymentMode = "Cash", RepaymentDate = shiftDate } // net -800 (excluded, never netted)
            );
            await db.SaveChangesAsync();
        }

        // Before DebtorManagementViewModel auto-registers unregistered names
        var ownerVm = sp.GetRequiredService<OwnerDashboardViewModel>();
        ownerVm.StartDate = shiftDate;
        ownerVm.EndDate = shiftDate;
        await ownerVm.LoadDataAsync(isBackgroundRefresh: false);

        // Expected: Positive Fleet (7000) + Unregistered Positive (600) = 7600 (zero/<=0.01/negative balances excluded)
        Assert.Equal(7600.0, ownerVm.OutstandingDebtors, 2);

        // After DebtorManagement loads and evaluates financial summary
        var debtorVm = sp.GetRequiredService<DebtorManagementViewModel>();
        await debtorVm.LoadDataAsync();
        await debtorVm.LoadFinancialsCommand.ExecuteAsync(null);
        await ownerVm.LoadDataAsync(isBackgroundRefresh: false);

        Assert.Equal(7600.0, debtorVm.OverallOutstandingBalance, 2);
        Assert.Equal(debtorVm.OverallOutstandingBalance, ownerVm.OutstandingDebtors, 2);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ISSUE 6 — Profit & Loss Calculation Inputs, Screen/Print/Excel Parity & Sync
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Issue6_FinancialCalculationService_IncludesDsmExpenses_EndDateTimestamps_AndRealOilDefCost()
    {
        var sp = BuildTestServices(useRealFinancialService: true);
        var startDate = new DateTime(2026, 11, 1);
        var endDate = new DateTime(2026, 11, 20);

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            // Margin recorded WITH non-midnight time on endDate must still apply to endDate!
            db.FuelProfitMargins.Add(new FuelProfitMargin
            {
                FuelType = "MS-I",
                EffectiveDate = endDate.AddHours(14),
                MarginPerLitre = 5.0
            });

            var shift = new Shift { ShiftDate = endDate.AddHours(6), ShiftType = "A" };
            db.Shifts.Add(shift);
            await db.SaveChangesAsync();

            var entry = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                DsmName = "Nirjala",
                Mismatch = -100m,
                NozzleReadings = new List<NozzleReading>
                {
                    new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 100, SaleLitres = 100, Rate = 100, Amount = 10000 }
                },
                Expenses = new List<Expense>
                {
                    new() { Description = "DSM Tea", Amount = 300.0 }
                },
                KhandharePetroleumEntries = new List<KhandharePetroleumEntry>
                {
                    new() { Date = endDate.AddHours(18).AddMinutes(45), DsmName = "Nirjala", Name = "KP Owner", SlipNumber = "101", Amount = 500.0 }
                }
            };
            db.DsmEntries.Add(entry);
            db.Expenses.Add(new Expense { ShiftId = shift.ShiftId, Description = "Shift Station Repair", Amount = 200.0 });

            // PumpExpense & OuterExpense on endDate with non-midnight time-of-day
            db.PumpExpenses.Add(new PumpExpense { ExpenseDate = endDate.AddHours(21).AddMinutes(15), Rent = 1000.0 });
            db.OuterExpenses.Add(new OuterExpense { ExpenseDate = endDate.AddHours(22).AddMinutes(5), Description = "Outer Legal Fee", Amount = 750.0 });

            // Oil & DEF with prior historical purchase (UnitPrice = 350) & opening log = 18 -> real closing = 13
            var oilProd = new ProductMaster
            {
                ProductName = "Lube 1L",
                Category = "Oil",
                Unit = "Units",
                DefaultSaleRate = 500.0,
                IsActive = true
            };
            db.ProductMasters.Add(oilProd);
            await db.SaveChangesAsync();

            db.OilDefPurchases.Add(new OilDefPurchase
            {
                ProductId = oilProd.Id,
                PurchaseDate = startDate.AddDays(-5),
                Quantity = 20,
                UnitPrice = 350.0,
                TotalCost = 7000.0
            });
            db.OilDefDailyLogs.Add(new OilDefDailyLog
            {
                ProductId = oilProd.Id,
                ProductType = "Oil",
                LogDate = startDate.AddDays(-1),
                RemainingStock = 18
            });
            db.OilDefDailyLogs.Add(new OilDefDailyLog
            {
                ProductId = oilProd.Id,
                ProductType = "Oil",
                ShiftId = shift.ShiftId,
                LogDate = endDate.AddHours(12),
                SoldQuantity = 5,
                RemainingStock = 13
            });

            await db.SaveChangesAsync();
        }

        // Resolve fresh FinancialCalculationService where Shifts/Expenses are NOT locally tracked
        var finSvc = sp.GetRequiredService<IFinancialCalculationService>();
        var res = await finSvc.CalculateFinancialsAsync(startDate, endDate);

        // Fuel Profit: 100 L * 5.0 = 500
        Assert.Equal(5.0, res.FuelProfit.MsIMargin, 2);
        Assert.Equal(500.0, res.FuelProfit.MsIProfit, 2);

        // DSM expense (300) + Shift expense (200) + Khandhare on endDate 18:45 (500) = 1000
        Assert.Equal(1000.0, res.TotalExpenses, 2);
        Assert.Equal(1000.0, res.TotalPumpExpenses, 2);
        Assert.Equal(750.0, res.OwnerOuterExpenses, 2);

        // Real Oil Opening (18), Closing (13), Avg Purchase Price (350, not DefaultSaleRate*0.8 = 400), Profit (5 * (500-350) = 750)
        Assert.Equal(18.0, res.OilProfit.OpeningStock, 2);
        Assert.Equal(13.0, res.OilProfit.ClosingStock, 2);
        Assert.Equal(350.0, res.OilProfit.AveragePurchasePrice, 2);
        Assert.Equal(750.0, res.OilProfit.TotalProfit, 2);
    }

    [Fact]
    public async Task Issue6_CalculateDsmSalariesAsync_DeduplicatesCaseAndWhitespaceNames()
    {
        var sp = BuildTestServices(useRealFinancialService: true);
        var shiftDate = new DateTime(2026, 11, 5);

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            db.DsmProfiles.Add(new DsmProfile
            {
                DsmName = "Nirjala",
                SalaryType = "FixedMonthly",
                BaseSalary = 12000.0
            });
            var shift = new Shift { ShiftDate = shiftDate, ShiftType = "A" };
            db.Shifts.Add(shift);
            await db.SaveChangesAsync();

            db.DsmEntries.Add(new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                DsmName = "  nirjala ",
                Mismatch = -150m
            });
            await db.SaveChangesAsync();
        }

        var finSvc = sp.GetRequiredService<IFinancialCalculationService>();
        var rows = await finSvc.CalculateDsmSalariesAsync(2026, 11);

        Assert.Single(rows);
        Assert.Equal(12000.0, rows[0].EarnedBase, 2);
        Assert.Equal(150.0, rows[0].ShortRecovery, 2);
    }

    [Fact]
    public async Task Issue6_ProfitLossViewModel_And_ExcelExport_KeepMismatchAndPurchaseModeConsistent()
    {
        var sp = BuildTestServices(useRealFinancialService: true);
        var shiftDate = new DateTime(2026, 11, 12);

        using (var db = sp.GetRequiredService<FuelProDbContext>())
        {
            db.FuelProfitMargins.Add(new FuelProfitMargin { FuelType = "HSD", EffectiveDate = shiftDate, MarginPerLitre = 3.0 });
            db.FuelTankers.Add(new FuelTanker { FuelType = "HSD", TankerDate = shiftDate, PurchaseRate = 84.0, Quantity = 5000 });

            var shift = new Shift { ShiftDate = shiftDate, ShiftType = "A" };
            db.Shifts.Add(shift);
            await db.SaveChangesAsync();

            db.DsmEntries.Add(new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 4,
                DsmName = "Ramesh",
                GrossSales = 9000m,
                TotalCollection = 8850m,
                Mismatch = -150m,
                NozzleReadings = new List<NozzleReading>
                {
                    new() { NozzleNumber = 9, FuelType = "HSD", OpeningReading = 0, ClosingReading = 100, SaleLitres = 100, Rate = 90.0, Amount = 9000 }
                },
                PaymentCollection = new PaymentCollection { CashDeposit = 8850.0 },
                CashDenominations = new List<CashDenomination> { new() { CashType = "Cash1", TotalAmount = 8850.0 } }
            });
            db.OuterExpenses.Add(new OuterExpense { ExpenseDate = shiftDate, Description = "Audit Fee", Amount = 250.0 });
            await db.SaveChangesAsync();
        }

        var plVm = sp.GetRequiredService<ProfitLossViewModel>();
        plVm.SelectedPreset = "Custom";
        plVm.StartDate = shiftDate;
        plVm.EndDate = shiftDate;
        await plVm.LoadCommand.ExecuteAsync(null);

        // Screen TotalMismatch must stay aligned with FinancialCalculationResult.TotalMismatch (-150), NOT overwritten
        Assert.Equal(-150.0, plVm.TotalMismatch, 2);
        Assert.Equal(300.0, plVm.TotalFuelProfit, 2);
        Assert.Equal(600.0, plVm.TotalFuelPurchaseProfit, 2);

        // Toggle Purchase-Based mode on and verify Excel matches NetProfit & includes Owner Outer Expenses
        plVm.UsePurchaseBasedProfit = true;
        Assert.Equal(600.0, plVm.TotalFuelProfit, 2);

        var excelSvc = sp.GetRequiredService<ExcelExportService>();
        var fin = await sp.GetRequiredService<IFinancialCalculationService>().CalculateFinancialsAsync(shiftDate, shiftDate);
        var expList = plVm.ExpenseBreakdown.Select(e => (e.Category, e.Amount)).ToList();
        var path = await excelSvc.ExportMonthlyPLAsync("Mitali", shiftDate, shiftDate, fin, expList, usePurchaseBasedProfit: true);
        try
        {
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheet("P&L Statement");
            var texts = ws.CellsUsed().Select(c => c.GetString()).ToList();
            Assert.Contains("Total Owner Outer Expenses", texts);

            // Find Net Profit row value & confirm it matches plVm.NetProfit
            var netProfitRow = ws.RowsUsed().First(r => r.Cell(1).GetString() == "Net Profit");
            Assert.Equal(plVm.NetProfit, netProfitRow.Cell(2).GetDouble(), 2);
        }
        finally
        {
            try { File.Delete(path); } catch { }
            plVm.UsePurchaseBasedProfit = false;
        }
    }

    [Fact]
    public async Task Issue5_6_SyncEnginePull_RaisesFinancialAndInventoryNotifications()
    {
        var fixedUpdatedAt = SyncTimestampHelper.FormatUtcIso(DateTime.UtcNow.AddMinutes(-1));
        var pumpExpenseGuid = Guid.NewGuid().ToString();

        var handler = new Batch2StubHandler(req =>
        {
            var url = req.RequestUri!.PathAndQuery;
            if (req.Method == HttpMethod.Get && url.Contains("/rest/v1/PumpExpenses") && !url.Contains("SyncGuid.gt"))
            {
                var rows = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["SyncGuid"] = pumpExpenseGuid,
                        ["station_id"] = "MITALI-TEST-01",
                        ["ExpenseDate"] = SyncTimestampHelper.FormatForPush(typeof(PumpExpense), nameof(PumpExpense.ExpenseDate), DateTime.Today.AddHours(10)),
                        ["Rent"] = 1200.0,
                        ["updated_at"] = fixedUpdatedAt
                    }
                };
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(rows), Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            };
        });

        var sp = BuildTestServices(handler);
        var syncEngine = sp.GetRequiredService<SyncEngine>();

        int pettyCashSignals = 0;
        Action onPettyCash = () => Interlocked.Increment(ref pettyCashSignals);
        DsmEntryService.PettyCashChanged += onPettyCash;
        try
        {
            await syncEngine.ForceSyncAsync();
            Assert.True(pettyCashSignals >= 1, "Pulling PumpExpenses must raise PettyCashChanged so Owner Dashboard & P&L reload.");
        }
        finally
        {
            DsmEntryService.PettyCashChanged -= onPettyCash;
        }
    }

    private sealed class Batch2StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public Batch2StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }
}
