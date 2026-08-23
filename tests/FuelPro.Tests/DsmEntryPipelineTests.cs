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
using Newtonsoft.Json;

namespace FuelPro.Tests;

public class DsmEntryPipelineTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly string _tempDir;

    public DsmEntryPipelineTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_EntryPipelineTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "fuelPro.db");

        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application();
        }

        var services = new ServiceCollection();
        
        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"),
            ServiceLifetime.Transient);

        services.AddSingleton<ICredentialFileService>(new LocalCredentialFileService(Path.Combine(_tempDir, "credentials")));

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

        // Sync services
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

        // Set up default pump config mappings
        var testMappings = new List<PumpMapping>
        {
            new() { PumpMappingId = 1, PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 2, PumpId = 1, NozzleNumber = 2, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 3, PumpId = 2, NozzleNumber = 3, FuelType = "MS-II", IsActive = true }
        };
        PumpConfiguration.InitializeFromDb(testMappings);

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            context.Database.EnsureCreated();
            // Seed the test mappings into DB
            context.PumpMappings.AddRange(testMappings);
            context.SaveChanges();
        }
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

    private async Task<DsmEntry> SaveTestEntryAsync(
        DateTime date, string shiftType, string name, int pumpId,
        double phonePe, double phonePeCard, double creditCard, double petroCard, double cashDeposit, double cashInHand,
        double openingNozzle1, double closingNozzle1,
        string creditCardTid = "T1", string creditCardBatch = "B1")
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();

        var nozzleReadings = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = openingNozzle1, ClosingReading = closingNozzle1, Rate = 100 }
        };

        var payment = new PaymentCollection
        {
            PhonePeMorning = shiftType == "A" ? phonePe : 0,
            PhonePeDay = shiftType == "B" ? phonePe : 0,
            PhonePeCardMorning = shiftType == "A" ? phonePeCard : 0,
            PhonePeCardDay = shiftType == "B" ? phonePeCard : 0,
            CreditCardMorning = shiftType == "A" ? creditCard : 0,
            CreditCardDay = shiftType == "B" ? creditCard : 0,
            PetroCardMorning = shiftType == "A" ? petroCard : 0,
            PetroCardDay = shiftType == "B" ? petroCard : 0,
            CashDeposit = cashDeposit,
            
            CreditCardTidMorning = shiftType == "A" ? creditCardTid : null,
            CreditCardBatchMorning = shiftType == "A" ? creditCardBatch : null,
            CreditCardTidDay = shiftType == "B" ? creditCardTid : null,
            CreditCardBatchDay = shiftType == "B" ? creditCardBatch : null
        };

        var cashDenominations = new List<CashDenomination>
        {
            new() { CashType = "Cash1", TotalAmount = cashDeposit },
            new() { CashType = "Cash2", TotalAmount = cashInHand }
        };

        var res = await dsmService.SaveCompleteEntryAsync(
            date, shiftType, name, pumpId,
            nozzleReadings, payment, new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), cashDenominations);

        Assert.True(res.Success, $"Save failed: {res.Error}");
        return res.Data!;
    }

    [Fact]
    public async Task Test1_EditShiftB_ShouldLeaveShiftAReportsByteForByteIdentical()
    {
        // 1. Create Shift A (Night)
        var date = new DateTime(2026, 7, 12);
        var entryA = await SaveTestEntryAsync(date, "A", "Tony Stark", 1, 1000, 500, 800, 200, 5000, 4500, 1000, 1100);

        // 2. Create Shift B (Day)
        var entryB = await SaveTestEntryAsync(date, "B", "Steve Rogers", 1, 2000, 1000, 1500, 400, 10000, 9500, 1100, 1300);

        // 3. Print Shift A (get canonical report state)
        var reportService = _serviceProvider.GetRequiredService<IReportService>();
        var dsmRepo = _serviceProvider.GetRequiredService<IDsmEntryRepository>();
        
        var shiftARecord = await dsmRepo.GetFullEntryAsync(entryA.DsmEntryId);
        var reportBefore = reportService.CalculateShiftReport(
            date, "A", new List<DsmEntry> { shiftARecord.Data! }, new List<Expense>(), new List<ShiftOtherCash>(),
            new List<CreditorRepayment>(), 90, 100, 100, 85, new BusinessDayTidSheet(), new BusinessDayTidSheet(), "Mitali Station");
        
        string beforeJson = JsonConvert.SerializeObject(reportBefore);

        // 4. Edit Shift B
        using (var scope = _serviceProvider.CreateScope())
        {
            var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
            
            // Re-save/update Shift B with modified cash and payments
            var nozzleReadings = new List<NozzleReading>
            {
                new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 1100, ClosingReading = 1350, Rate = 100 } // Sale Litres changed from 200 to 250
            };
            var payment = new PaymentCollection
            {
                PhonePeDay = 3000, // changed from 2000
                PhonePeCardDay = 1000,
                CreditCardDay = 1500,
                PetroCardDay = 400,
                CashDeposit = 12000 // changed from 10000
            };
            var cashDenominations = new List<CashDenomination>
            {
                new() { CashType = "Cash1", TotalAmount = 12000 },
                new() { CashType = "Cash2", TotalAmount = 11500 }
            };

            var res = await dsmService.SaveCompleteEntryAsync(
                date, "B", "Steve Rogers", 1, nozzleReadings, payment, new List<DebitEntry>(), new List<TestingEntry>(),
                new List<Expense>(), cashDenominations, existingEntryId: entryB.DsmEntryId);
            
            Assert.True(res.Success);
        }

        // 5. Print Shift A again
        var shiftARecordAfter = await dsmRepo.GetFullEntryAsync(entryA.DsmEntryId);
        var reportAfter = reportService.CalculateShiftReport(
            date, "A", new List<DsmEntry> { shiftARecordAfter.Data! }, new List<Expense>(), new List<ShiftOtherCash>(),
            new List<CreditorRepayment>(), 90, 100, 100, 85, new BusinessDayTidSheet(), new BusinessDayTidSheet(), "Mitali Station");

        string afterJson = JsonConvert.SerializeObject(reportAfter);

        // ASSERT: Shift A remains completely untouched and identical
        Assert.Equal(beforeJson, afterJson);
    }

    [Fact]
    public async Task Test2_OpeningShiftBAndThenShiftA_ShouldPreserveShiftADataExactly()
    {
        // 1. Create Shift A & Shift B
        var date = new DateTime(2026, 7, 12);
        var entryA = await SaveTestEntryAsync(date, "A", "Tony Stark", 1, 1000, 500, 800, 200, 5000, 4500, 1000, 1100);
        var entryB = await SaveTestEntryAsync(date, "B", "Steve Rogers", 1, 2000, 1000, 1500, 400, 10000, 9500, 1100, 1300);

        var repo = _serviceProvider.GetRequiredService<IDsmEntryRepository>();

        // 2. Open Shift B using HydrateFromEntryAsync (simulates Edit → pure read)
        var vmB = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        var fullB = await repo.GetFullEntryAsync(entryB.DsmEntryId);
        await vmB.HydrateFromEntryAsync(fullB.Data!);

        // 3. Open Shift A on a brand-new VM
        var vmA = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        var fullA = await repo.GetFullEntryAsync(entryA.DsmEntryId);
        await vmA.HydrateFromEntryAsync(fullA.Data!);

        // ASSERT: Shift A is pristine — no state leaked from Shift B
        Assert.Equal("Tony Stark", vmA.DsmName);
        Assert.Equal(5000, vmA.CashDeposit); // Cash1 deposit
        Assert.Equal(1000, vmA.PhonePeMorning);
        Assert.Equal(800, vmA.CreditCardMorning);
        var nozzle1A = vmA.NozzleReadings.FirstOrDefault(n => n.NozzleNumber == 1);
        Assert.NotNull(nozzle1A);
        // Verify opening matches the persisted DB value (may have been auto-cascaded)
        var dbReadingA = fullA.Data!.NozzleReadings.First(n => n.NozzleNumber == 1);
        Assert.Equal(dbReadingA.OpeningReading, nozzle1A.OpeningReading);
        Assert.Equal(1100, nozzle1A.ClosingReading);
    }

    [Fact]
    public async Task Test3_EditShiftB_ShouldOnlyChangeShiftBValuesAndNotAffectShiftAInDayTotal()
    {
        // 1. Create Shift A & B
        var date = new DateTime(2026, 7, 12);
        var entryA = await SaveTestEntryAsync(date, "A", "Tony Stark", 1, 1000, 500, 800, 200, 5000, 4500, 1000, 1100);
        var entryB = await SaveTestEntryAsync(date, "B", "Steve Rogers", 1, 2000, 1000, 1500, 400, 10000, 9500, 1100, 1300);

        // 2. Load initial Day Total ViewModel
        var dayTotalVm = _serviceProvider.GetRequiredService<DayTotalViewModel>();
        // Setting SelectedDate auto-triggers LoadDayDataAsync; call explicitly for determinism
        dayTotalVm.StartDate = date;
        dayTotalVm.EndDate = date;
        await dayTotalVm.LoadDayDataCommand.ExecuteAsync(null);

        double originalSplitPhonePe = dayTotalVm.SplitPhonePe;
        double originalSplitPineLabs = dayTotalVm.SplitPineLabsCard;

        // 3. Edit Shift B
        using (var scope = _serviceProvider.CreateScope())
        {
            var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
            
            var nozzleReadings = new List<NozzleReading>
            {
                new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 1100, ClosingReading = 1300, Rate = 100 }
            };
            var payment = new PaymentCollection
            {
                PhonePeDay = 5000, // changed from 2000 (+3000 difference)
                PhonePeCardDay = 1000,
                CreditCardDay = 3500, // changed from 1500 (+2000 difference)
                PetroCardDay = 400,
                CashDeposit = 10000
            };
            var cashDenominations = new List<CashDenomination>
            {
                new() { CashType = "Cash1", TotalAmount = 10000 },
                new() { CashType = "Cash2", TotalAmount = 9500 }
            };

            await dsmService.SaveCompleteEntryAsync(
                date, "B", "Steve Rogers", 1, nozzleReadings, payment, new List<DebitEntry>(), new List<TestingEntry>(),
                new List<Expense>(), cashDenominations, existingEntryId: entryB.DsmEntryId);
        }

        // 4. Reload Day Total
        var dayTotalVm2 = _serviceProvider.GetRequiredService<DayTotalViewModel>();
        dayTotalVm2.StartDate = date;
        dayTotalVm2.EndDate = date;
        await dayTotalVm2.LoadDayDataCommand.ExecuteAsync(null);

        // ASSERT: Shift A values remain untouched, but Day Total shows B shift updates
        // Original PhonePe: 1000 (Shift A) + 2000 (Shift B) = 3000
        // New PhonePe: 1000 (Shift A) + 5000 (Shift B) = 6000
        Assert.Equal(3000, originalSplitPhonePe);
        Assert.Equal(6000, dayTotalVm2.SplitPhonePe);

        // Original PineLabs (CreditCard): 800 (Shift A) + 1500 (Shift B) = 2300
        // New PineLabs: 800 (Shift A) + 3500 (Shift B) = 4300
        Assert.Equal(2300, originalSplitPineLabs);
        Assert.Equal(4300, dayTotalVm2.SplitPineLabsCard);
    }

    [Fact]
    public async Task Test4_ReopeningEntry_EveryPersistedFieldMatchesLosslessly()
    {
        var date = new DateTime(2026, 7, 12);
        
        // 1. Save entry for Shift B (Day shift to verify day mapping)
        var entry = await SaveTestEntryAsync(
            date, "B", "Bruce Banner", 1, 1500, 750, 950, 350, 7000, 6500, 1000, 1250, "CC-TID-99", "CC-BATCH-88");

        // 2. Reopen using a fresh VM + HydrateFromEntryAsync
        var repo = _serviceProvider.GetRequiredService<IDsmEntryRepository>();
        var fullEntry = await repo.GetFullEntryAsync(entry.DsmEntryId);
        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        await vm.HydrateFromEntryAsync(fullEntry.Data!);

        // ASSERT: Verification of every single field mapping symmetrically
        Assert.Equal("Bruce Banner", vm.DsmName);
        Assert.Equal(1, vm.SelectedPump.PumpId);
        Assert.Equal(date, vm.SelectedDate);
        Assert.Equal("B", vm.SelectedShift);

        // Nozzles (pump has 2 nozzles configured; only nozzle 1 was saved with readings)
        var nozzle1 = vm.NozzleReadings.FirstOrDefault(n => n.NozzleNumber == 1);
        Assert.NotNull(nozzle1);
        Assert.Equal(1000, nozzle1.OpeningReading);
        Assert.Equal(1250, nozzle1.ClosingReading);
        Assert.Equal(100, nozzle1.Rate);
        Assert.Equal(250, nozzle1.SaleLitres);
        Assert.Equal(25000, nozzle1.Amount);

        // Collections (Day shift values loaded back into Morning properties)
        Assert.Equal(1500, vm.PhonePeMorning);
        Assert.Equal(750, vm.PhonePeCardMorning);
        Assert.Equal(950, vm.CreditCardMorning);
        Assert.Equal(350, vm.PetroCardMorning);
        Assert.Equal(7000, vm.CashDeposit); // Cash1 deposit

        // TIDs and Batch Numbers
        Assert.Equal("CC-TID-99", vm.CreditCardTidMorning);
        Assert.Equal("CC-BATCH-88", vm.CreditCardBatchMorning);
    }

    [Fact]
    public async Task Test5_ApprovedSubmissionWithKhandhareEntries_PersistsAndHydratesCorrectly()
    {
        var date = new DateTime(2026, 8, 9);
        var kpEntries = new List<KhandharePetroleumEntry>
        {
            new() { Name = "Abhishek", SlipNumber = "01", Amount = 2500.0 }
        };

        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var dbContext = _serviceProvider.GetRequiredService<FuelProDbContext>();

        var result = await dsmService.SaveCompleteEntryWithContextAsync(
            dbContext,
            date, "A", "Naruto Uzumaki", 3,
            new List<NozzleReading>(),
            new PaymentCollection(),
            new List<DebitEntry>(),
            new List<TestingEntry>(),
            new List<Expense>(),
            new List<CashDenomination>(),
            khandharePetroleumEntries: kpEntries);

        Assert.True(result.Success, result.Error);
        var entryId = result.Data!.DsmEntryId;

        // Reopen entry
        var repo = _serviceProvider.GetRequiredService<IDsmEntryRepository>();
        var fullEntry = await repo.GetFullEntryAsync(entryId);

        Assert.True(fullEntry.Success);
        Assert.NotNull(fullEntry.Data!.KhandharePetroleumEntries);
        Assert.Single(fullEntry.Data.KhandharePetroleumEntries);
        Assert.Equal("Abhishek", fullEntry.Data.KhandharePetroleumEntries.First().Name);
        Assert.Equal("01", fullEntry.Data.KhandharePetroleumEntries.First().SlipNumber);
        Assert.Equal(2500.0, fullEntry.Data.KhandharePetroleumEntries.First().Amount);

        // Hydrate ViewModel
        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        await vm.HydrateFromEntryAsync(fullEntry.Data);

        Assert.Single(vm.KhandharePetroleumEntries);
        Assert.Equal("Abhishek", vm.KhandharePetroleumEntries.First().Name);
        Assert.Equal("01", vm.KhandharePetroleumEntries.First().SlipNumber);
        Assert.Equal(2500.0, vm.KhandharePetroleumEntries.First().Amount);
    }

    [Fact]
    public async Task Verify_DynamicCollectionTypes_Saved_Loaded_And_Calculated_InShiftAggregation()
    {
        var date = new DateTime(2026, 8, 23);
        var payment = new PaymentCollection
        {
            CashDeposit = 1000,
            PhonePeMorning = 200,
            Items = new List<PaymentCollectionItem>
            {
                new() { CollectionTypeCode = "PAYTM", Amount = 1500 },
                new() { CollectionTypeCode = "QR", Amount = 2000 },
                new() { CollectionTypeCode = "SBI_REDEEM", Amount = 500 },
                new() { CollectionTypeCode = "MOBIKWIK", Amount = 750 }
            }
        };

        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var dbContext = _serviceProvider.GetRequiredService<FuelProDbContext>();

        var result = await dsmService.SaveCompleteEntryWithContextAsync(
            dbContext,
            date, "A", "Goku Son", 1,
            new List<NozzleReading>(),
            payment,
            new List<DebitEntry>(),
            new List<TestingEntry>(),
            new List<Expense>(),
            new List<CashDenomination>());

        Assert.True(result.Success, result.Error);
        var entryId = result.Data!.DsmEntryId;

        // 1. Verify Repository includes Items
        var repo = _serviceProvider.GetRequiredService<IDsmEntryRepository>();
        var fullEntry = await repo.GetFullEntryAsync(entryId);

        Assert.True(fullEntry.Success);
        Assert.NotNull(fullEntry.Data!.PaymentCollection);
        Assert.NotNull(fullEntry.Data.PaymentCollection.Items);
        Assert.Equal(4, fullEntry.Data.PaymentCollection.Items.Count);
        Assert.Equal(4750.0, fullEntry.Data.PaymentCollection.Items.Sum(i => i.Amount));

        // 2. Verify ShiftAggregationService BuildDsmSummaryRows & BuildDsmShiftTotals
        var aggService = _serviceProvider.GetRequiredService<IShiftAggregationService>();
        var rows = aggService.BuildDsmSummaryRows(new List<DsmEntry> { fullEntry.Data });

        Assert.Single(rows);
        var row = rows.First();
        Assert.Equal(1500.0, row.Paytm);
        Assert.Equal(2000.0, row.QrPayment);
        Assert.Equal(500.0, row.SbiRedeem);
        Assert.Equal(750.0, row.Mobikwik);
        Assert.Equal(4750.0, row.DynamicCollectionsTotal);

        var shiftTotals = aggService.BuildDsmShiftTotals(rows);
        Assert.Single(shiftTotals);
        // TotalCollection = CashDeposit (1000) + PhonePe (200) + DynamicTotal (4750) = 5950
        Assert.Equal(5950.0, shiftTotals.First().TotalCollection);
    }
}
