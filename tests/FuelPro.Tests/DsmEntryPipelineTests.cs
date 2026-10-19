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
        PumpConfiguration.ResetToDefaults();
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

    [Fact]
    public async Task Verify_ClearForm_Preserves_DynamicCollectionTypes_And_Resets_Values()
    {
        var colTypeService = _serviceProvider.GetRequiredService<ICollectionTypeService>();
        await colTypeService.SaveCollectionTypeAsync(new CollectionTypeMaster
        {
            Code = "PAYTM",
            DisplayName = "Paytm QR",
            Category = "Online",
            HasTidBatch = true,
            IsActive = true,
            DisplayOrder = 1
        });
        await colTypeService.SaveCollectionTypeAsync(new CollectionTypeMaster
        {
            Code = "SBI_REDEEM",
            DisplayName = "SBI Points",
            Category = "Card",
            HasTidBatch = true,
            IsActive = true,
            DisplayOrder = 2
        });

        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        await vm.LoadDynamicCollectionTypesAsync();

        Assert.NotEmpty(vm.DynamicCollections);
        var paytmRow = vm.DynamicCollections.FirstOrDefault(d => d.Code == "PAYTM");
        Assert.NotNull(paytmRow);
        paytmRow.Amount = 1250;
        paytmRow.Tid = "TID999";
        paytmRow.Batch = "B123";

        var countBeforeClear = vm.DynamicCollections.Count;

        // Execute ClearForm
        vm.ClearForm();

        // Verify collection types are NOT removed
        Assert.Equal(countBeforeClear, vm.DynamicCollections.Count);
        var paytmAfterClear = vm.DynamicCollections.FirstOrDefault(d => d.Code == "PAYTM");
        Assert.NotNull(paytmAfterClear);
        Assert.Equal("Paytm QR", paytmAfterClear.DisplayName);
        Assert.Null(paytmAfterClear.Amount);
        Assert.Null(paytmAfterClear.Tid);
        Assert.Null(paytmAfterClear.Batch);
    }

    [Fact]
    public async Task Verify_Desktop_OtpTransactions_SaveAndReopen_Losslessly()
    {
        var date = new DateTime(2026, 7, 15);
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var repo = scope.ServiceProvider.GetRequiredService<IDsmEntryRepository>();

        var nozzleReadings = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 1000, ClosingReading = 1050, Rate = 100 }
        };

        var otpItems = new List<PaymentCollectionItem>
        {
            new() { CollectionTypeCode = "CREDIT_CARD", Amount = 1200, Tid = "TID_CC", Batch = "B_CC", Otp = "123456", Slot = "Morning" },
            new() { CollectionTypeCode = "CREDIT_CARD", Amount = 1800, Tid = "TID_CC", Batch = "B_CC", Otp = "654321", Slot = "Morning" },
            new() { CollectionTypeCode = "PETROCARD", Amount = 1000, Tid = "TID_PC", Batch = "B_PC", Otp = "998877", Slot = "Morning" }
        };

        var payment = new PaymentCollection
        {
            CreditCardMorning = 3000,
            CreditCardTidMorning = "TID_CC",
            CreditCardBatchMorning = "B_CC",
            PetroCardMorning = 1000,
            PetroCardTidMorning = "TID_PC",
            PetroCardBatchMorning = "B_PC",
            CashDeposit = 1000,
            Items = otpItems,
            DynamicItemsJson = JsonConvert.SerializeObject(otpItems)
        };

        var cashDenoms = new List<CashDenomination>
        {
            new() { CashType = "Cash1", TotalAmount = 1000 },
            new() { CashType = "Cash2", TotalAmount = 0 }
        };

        var res = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Operator Steve", 1,
            nozzleReadings, payment, new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), cashDenoms);

        Assert.True(res.Success, $"Save failed: {res.Error}");
        var savedEntryId = res.Data!.DsmEntryId;

        // Now reopen the entry using DsmEntryViewModel.HydrateFromEntryAsync
        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        var fullEntry = await repo.GetFullEntryAsync(savedEntryId);
        Assert.NotNull(fullEntry.Data);

        await vm.HydrateFromEntryAsync(fullEntry.Data!);

        // Verify OTP transactions were restored into vm.OtpTransactions
        Assert.Equal(3, vm.OtpTransactions.Count);
        var ccRow1 = vm.OtpTransactions.FirstOrDefault(r => r.Otp == "123456");
        Assert.NotNull(ccRow1);
        Assert.Equal("CREDIT_CARD", ccRow1!.CollectionTypeCode);
        Assert.Equal(1200.0, ccRow1.Amount);
        Assert.Equal("TID_CC", ccRow1.Tid);
        Assert.Equal("B_CC", ccRow1.Batch);

        var ccRow2 = vm.OtpTransactions.FirstOrDefault(r => r.Otp == "654321");
        Assert.NotNull(ccRow2);
        Assert.Equal("CREDIT_CARD", ccRow2!.CollectionTypeCode);
        Assert.Equal(1800.0, ccRow2.Amount);

        var pcRow = vm.OtpTransactions.FirstOrDefault(r => r.Otp == "998877");
        Assert.NotNull(pcRow);
        Assert.Equal("PETROCARD", pcRow!.CollectionTypeCode);
        Assert.Equal(1000.0, pcRow.Amount);

        // Verify parent totals
        Assert.Equal(3000.0, vm.CreditCardMorning);
        Assert.Equal(1000.0, vm.PetroCardMorning);
    }

    [Fact]
    public async Task FullPipeline_PwaSubmission_Through_ApprovalQueue_To_Persistence_And_Reopen()
    {
        var date = new DateTime(2026, 10, 14);
        var subGuid = Guid.NewGuid();

        // 1. PWA Submission Payload with structured settlements + itemized OTP transactions
        var submission = new DsmPendingSubmission
        {
            Id = subGuid,
            ShiftDate = date,
            ShiftType = "A",
            DsmName = "Pipeline Operator",
            PumpId = 1,
            SubmittedAt = DateTime.UtcNow,
            MetadataJson = JsonConvert.SerializeObject(new
            {
                settlements = new[]
                {
                    new { paymentType = "PhonePe", period = "Morning", amount = 5000.0, tid = "PP01", batch = "B01" },
                    new { paymentType = "PineLabs", period = "Morning", amount = 2574.0, tid = "PL01", batch = "B02" }
                },
                cardSwipeDetails = new[]
                {
                    new { mode = "PhonePe (Morning)", amount = 5000.0, tid = "PP01", batch = "B01", otp = "1111" },
                    new { mode = "PineLabs Card Morning", amount = 2574.0, tid = "PL01", batch = "B02", otp = "2222" },
                    new { mode = "PETROCARD (Morning)", amount = 235.0, tid = "PC01", batch = "B03", otp = "4421" }
                },
                testingEntries = new[]
                {
                    new { nozzleId = 1, amount = 32.141, fuelType = "MS-I", pumpId = 1 }
                },
                cashAmount = 10000.0
            })
        };

        // 2. Approval Queue loads the submission
        var queueVm = _serviceProvider.GetRequiredService<DsmApprovalQueueViewModel>();
        queueVm.SelectedSubmission = submission;
        await Task.Delay(100);
        while (queueVm.IsLoadingDetails)
        {
            await Task.Delay(50);
        }
        Assert.Equal("Submission details loaded.", queueVm.StatusMessage);
        Assert.Equal(3, queueVm.CardSwipeDetails.Count);

        // Configure nozzle reading and testing deduction to match test case: Gross = 21023.45, Testing = 3214.10
        queueVm.NozzleReadings.Clear();
        queueVm.NozzleReadings.Add(new DsmNozzleRow
        {
            NozzleId = 1,
            FuelType = "MS-I",
            OpeningReading = 1000.0,
            ClosingReading = 1210.2345,
            Rate = 100.0
        });

        queueVm.SubmissionTestingEntries.Clear();
        queueVm.SubmissionTestingEntries.Add(new DsmTestingRow
        {
            NozzleId = 1,
            FuelType = "MS-I",
            Amount = 32.141,
            RupeeAmount = 3214.10,
            PumpId = 1
        });

        // Explicitly set scalar collections on ViewModel (simulating Supabase collection fetch)
        queueVm.CashAmount = 10000.0;
        queueVm.UpiAmount = 5000.0;
        queueVm.CardAmount = 0.0;
        queueVm.PetroCardAmount = 235.0;
        queueVm.CashDepositAmount = 0.0;
        queueVm.ExpenseAmount = 0.0;
        queueVm.CreditAmount = 0.0;
        queueVm.OthersAmount = 0.0;

        // Trigger RecalculateTotals on approval queue
        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(queueVm, null);

        // Verify Approval Queue collections and mismatch before approval
        Assert.Equal(2574.0, queueVm.PineLabsAmount);
        Assert.Equal(17809.00, queueVm.TotalCollections);
        Assert.Equal(-0.35, queueVm.MismatchAmount, precision: 2);

        // 3. Approval -> SQLite persistence
        await queueVm.ApproveCommand.ExecuteAsync(null);

        // 4. Verify SQLite persistence in isolated test database
        using var db = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var savedEntry = await db.DsmEntries
            .Include(e => e.PaymentCollection)
            .ThenInclude(p => p.Items)
            .FirstOrDefaultAsync(e => e.DsmName == "Pipeline Operator");
        Assert.NotNull(savedEntry);

        var repo = _serviceProvider.GetRequiredService<IDsmEntryRepository>();
        var fullEntryResult = await repo.GetFullEntryAsync(savedEntry!.DsmEntryId);
        Assert.True(fullEntryResult.Success);
        var fullEntry = fullEntryResult.Data!;

        Assert.NotNull(fullEntry.PaymentCollection);
        Assert.Equal(5000.0, fullEntry.PaymentCollection!.PhonePeMorning);
        Assert.Equal(0.0, fullEntry.PaymentCollection.CreditCardMorning);
        Assert.Equal(235.0, fullEntry.PaymentCollection.PetroCardMorning);
        Assert.Equal(2574.0, fullEntry.PaymentCollection.DynamicItemsTotal);

        var allItems = fullEntry.PaymentCollection.GetAllItems().ToList();
        Assert.Equal(3, allItems.Count);
        Assert.Contains(allItems, i => i.CollectionTypeCode == "PHONEPE" && i.Amount == 5000.0 && i.Otp == "1111");
        Assert.Contains(allItems, i => i.CollectionTypeCode == "PINELABS_CARD" && i.Amount == 2574.0 && i.Otp == "2222");
        Assert.Contains(allItems, i => i.CollectionTypeCode == "PETROCARD" && i.Amount == 235.0 && i.Otp == "4421");

        // 5. Reopened Desktop DSM Entry
        var dsmVm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        await dsmVm.HydrateFromEntryAsync(fullEntry);

        Assert.Equal(17809.00, dsmVm.FinalAdjusted);
        Assert.Equal(17809.35, dsmVm.NetGrossSales, precision: 2);
        Assert.Equal(-0.35, dsmVm.Difference, precision: 2);

        Assert.Equal(3, dsmVm.OtpTransactions.Count);
        var pcOtp = dsmVm.OtpTransactions.FirstOrDefault(t => t.CollectionTypeCode == "PETROCARD");
        Assert.NotNull(pcOtp);
        Assert.Equal(235.0, pcOtp!.Amount);
        Assert.Equal("4421", pcOtp.Otp);
        Assert.Equal("PC01", pcOtp.Tid);
        Assert.Equal("B03", pcOtp.Batch);
        Assert.Equal("Morning", pcOtp.Slot);
    }

    [Fact]
    public async Task StagingValidation_Case1_PhonePe_Structured5000_WithItemized5000And700_ApprovedAndReopened()
    {
        var date = new DateTime(2026, 10, 15);
        var subGuid = Guid.NewGuid();

        // 1. Submit one structured PhonePe settlement of ₹5,000 alongside two itemized PhonePe transactions:
        //    - ₹5,000, OTP 1111
        //    - ₹700, OTP 2222
        var submission = new DsmPendingSubmission
        {
            Id = subGuid,
            ShiftDate = date,
            ShiftType = "A",
            DsmName = "Staging Operator Case1",
            PumpId = 1,
            SubmittedAt = DateTime.UtcNow,
            MetadataJson = JsonConvert.SerializeObject(new
            {
                settlements = new[]
                {
                    new { paymentType = "PhonePe", period = "Morning", amount = 5000.0, tid = "PP01", batch = "B01" }
                },
                cardSwipeDetails = new[]
                {
                    new { mode = "PhonePe (Morning)", amount = 5000.0, tid = "PP01", batch = "B01", otp = "1111" },
                    new { mode = "PhonePe (Morning)", amount = 700.0, tid = "PP01", batch = "B01", otp = "2222" }
                },
                testingEntries = new[]
                {
                    new { nozzleId = 1, amount = 13.0, fuelType = "MS-I", pumpId = 1 }
                },
                cashAmount = 0.0
            })
        };

        // 2. Desktop Approval Queue loads submission
        var queueVm = _serviceProvider.GetRequiredService<DsmApprovalQueueViewModel>();
        queueVm.SelectedSubmission = submission;
        await Task.Delay(100);
        while (queueVm.IsLoadingDetails)
        {
            await Task.Delay(50);
        }

        Assert.Equal("Submission details loaded.", queueVm.StatusMessage);
        Assert.Equal(2, queueVm.CardSwipeDetails.Count);

        // Configure nozzle reading: Opening = 1000, Closing = 1070 @ Rate 100 => Gross = 7000.0
        // Testing = 13.0 L @ Rate 100 => 1300.0. NetGrossSales = 7000.0 - 1300.0 = 5700.0
        queueVm.NozzleReadings.Clear();
        queueVm.NozzleReadings.Add(new DsmNozzleRow
        {
            NozzleId = 1,
            FuelType = "MS-I",
            OpeningReading = 1000.0,
            ClosingReading = 1070.0,
            Rate = 100.0
        });

        // Simulating the PWA submission collection fetch where PhonePe total = ₹5,700
        queueVm.CashAmount = 0.0;
        queueVm.UpiAmount = 5700.0;
        queueVm.CardAmount = 0.0;
        queueVm.PetroCardAmount = 0.0;
        queueVm.CashDepositAmount = 0.0;
        queueVm.ExpenseAmount = 0.0;
        queueVm.CreditAmount = 0.0;
        queueVm.OthersAmount = 0.0;

        // Trigger RecalculateTotals on approval queue
        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(queueVm, null);

        // 3. Verify Desktop Approval Queue shows PhonePe total = ₹5,700, with no duplicate ₹5,000
        Assert.Equal(5700.00, queueVm.TotalCollections);
        Assert.Equal(0.00, queueVm.MismatchAmount, precision: 2);

        // 4. Approve the submission and verify local SQLite persistence
        await queueVm.ApproveCommand.ExecuteAsync(null);

        using var db = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var savedEntry = await db.DsmEntries
            .Include(e => e.PaymentCollection)
            .ThenInclude(p => p.Items)
            .FirstOrDefaultAsync(e => e.DsmName == "Staging Operator Case1");
        Assert.NotNull(savedEntry);

        var repo = _serviceProvider.GetRequiredService<IDsmEntryRepository>();
        var fullEntryResult = await repo.GetFullEntryAsync(savedEntry!.DsmEntryId);
        Assert.True(fullEntryResult.Success);
        var fullEntry = fullEntryResult.Data!;

        Assert.NotNull(fullEntry.PaymentCollection);
        // PhonePeMorning is ₹5,000 (from settlement) + ₹700 (from unconsumed swipe) = ₹5,700
        Assert.Equal(5700.0, fullEntry.PaymentCollection!.PhonePeMorning);
        Assert.Equal(0.0, fullEntry.PaymentCollection.CreditCardMorning);

        var allItems = fullEntry.PaymentCollection.GetAllItems().ToList();
        Assert.Equal(2, allItems.Count);
        Assert.Contains(allItems, i => i.CollectionTypeCode == "PHONEPE" && i.Amount == 5000.0 && i.Otp == "1111");
        Assert.Contains(allItems, i => i.CollectionTypeCode == "PHONEPE" && i.Amount == 700.0 && i.Otp == "2222");

        // 5. Reopen Desktop DSM Entry and verify:
        //    - Total remains ₹5,700
        //    - Both OTP transactions and their details are restored
        //    - The saved values remain unchanged after reopening
        //    - The reconciliation mismatch follows the verified accounting formula
        var dsmVm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        await dsmVm.HydrateFromEntryAsync(fullEntry);

        Assert.Equal(5700.00, dsmVm.FinalAdjusted);
        Assert.Equal(5700.00, dsmVm.NetGrossSales, precision: 2);
        Assert.Equal(0.00, dsmVm.Difference, precision: 2);
        Assert.Equal(5700.00, dsmVm.PhonePeMorning);

        Assert.Equal(2, dsmVm.OtpTransactions.Count);
        var tx1 = dsmVm.OtpTransactions.FirstOrDefault(t => t.Otp == "1111");
        Assert.NotNull(tx1);
        Assert.Equal(5000.0, tx1!.Amount);
        Assert.Equal("PP01", tx1.Tid);
        Assert.Equal("B01", tx1.Batch);
        Assert.Equal("Morning", tx1.Slot);

        var tx2 = dsmVm.OtpTransactions.FirstOrDefault(t => t.Otp == "2222");
        Assert.NotNull(tx2);
        Assert.Equal(700.0, tx2!.Amount);
        Assert.Equal("PP01", tx2.Tid);
        Assert.Equal("B01", tx2.Batch);
        Assert.Equal("Morning", tx2.Slot);
    }

    [Fact]
    public async Task StagingValidation_Case2_TwoLegitimateSameTypeSameAmount_WithDifferentTids_ApprovedAndReopened()
    {
        var date = new DateTime(2026, 10, 15);
        var subGuid = Guid.NewGuid();

        // Two legitimate same-type, same-amount transactions with different TIDs:
        // Item 1: PineLabs Card ₹1,500, TID POS01, Batch B01, OTP 8811
        // Item 2: PineLabs Card ₹1,500, TID POS02, Batch B02, OTP 8822
        var submission = new DsmPendingSubmission
        {
            Id = subGuid,
            ShiftDate = date,
            ShiftType = "A",
            DsmName = "Staging Operator Case2",
            PumpId = 1,
            SubmittedAt = DateTime.UtcNow,
            MetadataJson = JsonConvert.SerializeObject(new
            {
                cardSwipeDetails = new[]
                {
                    new { mode = "PineLabs Card Morning", amount = 1500.0, tid = "POS01", batch = "B01", otp = "8811" },
                    new { mode = "PineLabs Card Morning", amount = 1500.0, tid = "POS02", batch = "B02", otp = "8822" }
                },
                cashAmount = 0.0
            })
        };

        // Load into Approval Queue
        var queueVm = _serviceProvider.GetRequiredService<DsmApprovalQueueViewModel>();
        queueVm.SelectedSubmission = submission;
        await Task.Delay(100);
        while (queueVm.IsLoadingDetails)
        {
            await Task.Delay(50);
        }

        Assert.Equal("Submission details loaded.", queueVm.StatusMessage);
        Assert.Equal(2, queueVm.CardSwipeDetails.Count);

        // Configure nozzle reading: Opening = 1000, Closing = 1030 @ Rate 100 => Gross = 3000.0. Testing = 0
        queueVm.NozzleReadings.Clear();
        queueVm.NozzleReadings.Add(new DsmNozzleRow
        {
            NozzleId = 1,
            FuelType = "MS-I",
            OpeningReading = 1000.0,
            ClosingReading = 1030.0,
            Rate = 100.0
        });

        // Set collections
        queueVm.CashAmount = 0.0;
        queueVm.UpiAmount = 0.0;
        queueVm.CardAmount = 0.0;
        queueVm.PetroCardAmount = 0.0;
        queueVm.CashDepositAmount = 0.0;
        queueVm.ExpenseAmount = 0.0;
        queueVm.CreditAmount = 0.0;
        queueVm.OthersAmount = 0.0;

        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(queueVm, null);

        Assert.Equal(3000.00, queueVm.PineLabsAmount);
        Assert.Equal(3000.00, queueVm.TotalCollections);
        Assert.Equal(0.00, queueVm.MismatchAmount, precision: 2);

        // Approve and persist
        await queueVm.ApproveCommand.ExecuteAsync(null);

        using var db = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var savedEntry = await db.DsmEntries
            .Include(e => e.PaymentCollection)
            .ThenInclude(p => p.Items)
            .FirstOrDefaultAsync(e => e.DsmName == "Staging Operator Case2");
        Assert.NotNull(savedEntry);

        var repo = _serviceProvider.GetRequiredService<IDsmEntryRepository>();
        var fullEntryResult = await repo.GetFullEntryAsync(savedEntry!.DsmEntryId);
        Assert.True(fullEntryResult.Success);
        var fullEntry = fullEntryResult.Data!;

        Assert.NotNull(fullEntry.PaymentCollection);
        Assert.Equal(0.0, fullEntry.PaymentCollection!.CreditCardMorning);
        Assert.Equal(3000.0, fullEntry.PaymentCollection!.DynamicItemsTotal);

        var allItems = fullEntry.PaymentCollection.GetAllItems().ToList();
        Assert.Equal(2, allItems.Count);
        Assert.Contains(allItems, i => i.CollectionTypeCode == "PINELABS_CARD" && i.Amount == 1500.0 && i.Tid == "POS01" && i.Otp == "8811");
        Assert.Contains(allItems, i => i.CollectionTypeCode == "PINELABS_CARD" && i.Amount == 1500.0 && i.Tid == "POS02" && i.Otp == "8822");

        // Reopen Desktop DSM Entry
        var dsmVm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        await dsmVm.HydrateFromEntryAsync(fullEntry);

        Assert.Equal(3000.00, dsmVm.FinalAdjusted);
        Assert.Equal(3000.00, dsmVm.NetGrossSales, precision: 2);
        Assert.Equal(0.00, dsmVm.Difference, precision: 2);
        Assert.Null(dsmVm.CreditCardMorning);
        Assert.Contains(dsmVm.DynamicCollections, d => d.Code == "PINELABS_CARD" && d.Amount == 3000.00);

        // Both survive approval and reopening
        Assert.Equal(2, dsmVm.OtpTransactions.Count);
        var item1 = dsmVm.OtpTransactions.FirstOrDefault(t => t.Tid == "POS01");
        Assert.NotNull(item1);
        Assert.Equal(1500.0, item1!.Amount);
        Assert.Equal("8811", item1.Otp);

        var item2 = dsmVm.OtpTransactions.FirstOrDefault(t => t.Tid == "POS02");
        Assert.NotNull(item2);
        Assert.Equal(1500.0, item2!.Amount);
        Assert.Equal("8822", item2.Otp);
    }

    [Fact]
    public async Task Verify_ApprovalQueue_SixLineReconciliation_CalculationAndPresentation()
    {
        var queueVm = _serviceProvider.GetRequiredService<DsmApprovalQueueViewModel>();

        // Set nozzle readings: Gross Sales = 8,304.80
        queueVm.NozzleReadings.Clear();
        queueVm.NozzleReadings.Add(new DsmNozzleRow
        {
            NozzleId = 1,
            FuelType = "MS-I",
            OpeningReading = 1000.0,
            ClosingReading = 1083.048,
            Rate = 100.0,
            Amount = 8304.80
        });

        // Set testing entry: Testing = 1,141.91
        queueVm.SubmissionTestingEntries.Clear();
        queueVm.SubmissionTestingEntries.Add(new DsmTestingRow
        {
            NozzleId = 1,
            FuelType = "MS-I",
            Amount = 11.4191,
            RupeeAmount = 1141.91
        });

        // Set Collections: 7,162.00 (e.g. PhonePe)
        queueVm.UpiAmount = 7162.00;
        queueVm.CashAmount = 0.0;
        queueVm.CardAmount = 0.0;
        queueVm.PetroCardAmount = 0.0;
        queueVm.CashDepositAmount = 0.0;
        queueVm.ExpenseAmount = 0.0;
        queueVm.CreditAmount = 0.0;
        queueVm.OthersAmount = 0.0;

        // Recalculate totals
        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(queueVm, null);

        // 1. Gross Fuel Sales (Meter)
        Assert.Equal(8304.80, queueVm.GrossSales, precision: 2);

        // 2. Less: Testing (Non-Sale)
        Assert.Equal(1141.91, queueVm.TotalTestingAmount, precision: 2);

        // 3. Net Fuel Sales
        Assert.Equal(7162.89, queueVm.NetFuelSales, precision: 2);

        // 4. Oil / DEF Sales
        Assert.Equal(0.00, queueVm.TotalOilDefSales, precision: 2);

        // 5. Total Collections
        Assert.Equal(7162.00, queueVm.TotalCollections, precision: 2);

        // 6. Mismatch: Collections - (NetFuelSales + TotalOilDefSales) = 7162.00 - 7162.89 = -0.89
        Assert.Equal(-0.89, queueVm.MismatchAmount, precision: 2);
    }

    [Fact]
    public async Task Verify_DsmEntry_EditEntry_Reopen_And_Save_Preserves_Reconciliation_And_Otp_Transactions()
    {
        var date = new DateTime(2026, 10, 16);
        var payment = new PaymentCollection
        {
            PhonePeMorning = 5000.0,
            CashDeposit = 2000.0,
            Items = new List<PaymentCollectionItem>
            {
                new() { CollectionTypeCode = "PHONEPE", Amount = 5000.0, Tid = "PP01", Batch = "B01", Otp = "9988", Slot = "Morning" }
            }
        };

        var dsmService = _serviceProvider.GetRequiredService<DsmEntryService>();
        var dbContext = _serviceProvider.GetRequiredService<FuelProDbContext>();

        var nozzleReadings = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 1000.0, ClosingReading = 1083.048, Rate = 100.0, Amount = 8304.80, SaleLitres = 83.048 }
        };
        var testingEntries = new List<TestingEntry>
        {
            new() { FuelType = "MS-I", Litres = 11.4191, Rate = 100.0, Amount = 1141.91 }
        };
        var cashDenominations = new List<CashDenomination>
        {
            new() { CashType = "Cash1", Denom500 = 4, TotalAmount = 2000 },
            new() { CashType = "Cash2", Denom100 = 1, Denom50 = 1, Denom10 = 1, Coins = 2, TotalAmount = 162 }
        };

        var saveResult = await dsmService.SaveCompleteEntryWithContextAsync(
            dbContext,
            date, "A", "Operator Reopen Parity", 1,
            nozzleReadings,
            payment,
            new List<DebitEntry>(),
            testingEntries,
            new List<Expense>(),
            cashDenominations);

        Assert.True(saveResult.Success, saveResult.Error);
        var entryId = saveResult.Data!.DsmEntryId;

        // Reopen in DsmEntryViewModel
        var repo = _serviceProvider.GetRequiredService<IDsmEntryRepository>();
        var fullEntry = (await repo.GetFullEntryAsync(entryId)).Data!;

        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        await vm.HydrateFromEntryAsync(fullEntry);

        // Verify loaded state
        Assert.Equal(8304.80, vm.MeterGrossSales, precision: 2);
        Assert.Equal(1141.91, vm.TotalTestingAmount, precision: 2);
        Assert.Equal(7162.89, vm.NetGrossSales, precision: 2);
        Assert.Equal(7162.00, vm.FinalAdjusted, precision: 2); // 5000 + 2000 + 162
        Assert.Equal(-0.89, vm.Difference, precision: 2);      // 7162.00 - 7162.89 = -0.89

        // Verify OTP transactions preserved
        Assert.Single(vm.OtpTransactions);
        Assert.Equal("9988", vm.OtpTransactions.First().Otp);
        Assert.Equal(5000.0, vm.OtpTransactions.First().Amount);

        // Edit and Re-save entry (simulate user adjusting Cash2 coins by +₹1.00)
        vm.Cash2.Coins = 3; // +1 INR
        vm.RecalculateAll();
        Assert.Equal(7163.00, vm.FinalAdjusted, precision: 2);
        Assert.Equal(0.11, vm.Difference, precision: 2); // 7163.00 - 7162.89 = +0.11
    }

    [Fact]
    public async Task Verify_ApprovalQueue_BlocksApproval_WhenCreditUnitemized_WithZeroSideEffects()
    {
        using var db = _serviceProvider.GetRequiredService<FuelProDbContext>();
        int dsmCountBefore = db.DsmEntries.Count();
        int debitCountBefore = db.DebitEntries.Count();
        int pdCountBefore = db.DsmPersonalDebtors.Count();

        var submission = new DsmPendingSubmission
        {
            Id = Guid.NewGuid(),
            DsmName = "Test Unitemized DSM",
            DsmUserId = "usr-unit-01",
            PumpId = 1,
            ShiftDate = DateTime.Today,
            ShiftType = "Morning",
            Notes = "Unitemized Credit Test",
            MetadataJson = JsonConvert.SerializeObject(new
            {
                readings = new[]
                {
                    new { nozzleId = 1, openingReading = 1000.0, closingReading = 1050.0, rate = 100.0, fuelType = "MS-I" }
                },
                collections = new
                {
                    cash = 4500.0,
                    credit = 500.0
                }
            })
        };

        var queueVm = _serviceProvider.GetRequiredService<DsmApprovalQueueViewModel>();
        queueVm.SelectedSubmission = submission;
        await Task.Delay(50);
        while (queueVm.IsLoadingDetails)
        {
            await Task.Delay(50);
        }

        queueVm.NozzleReadings.Clear();
        queueVm.NozzleReadings.Add(new DsmNozzleRow
        {
            NozzleId = 1,
            FuelType = "MS-I",
            OpeningReading = 1000.0,
            ClosingReading = 1050.0,
            Rate = 100.0,
            Amount = 5000.0
        });

        queueVm.CashAmount = 4500.0;
        queueVm.UpiAmount = 0.0;
        queueVm.CardAmount = 0.0;
        queueVm.PetroCardAmount = 0.0;
        queueVm.CashDepositAmount = 0.0;
        queueVm.ExpenseAmount = 0.0;
        queueVm.OthersAmount = 0.0;
        queueVm.CreditAmount = 500.0;
        queueVm.DebtorEntries.Clear(); // No itemized debtor entries!

        // Recalculate totals
        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(queueVm, null);

        // 1. Verify approval is strictly blocked at property level
        Assert.False(queueVm.HasValidItemizedDebtors());
        Assert.False(queueVm.IsSubmissionComplete);
        Assert.False(queueVm.CanApprove);

        // 2. Verify exact rejection message
        Assert.Equal("Unitemized credit detected (₹500.00). All credit sales must be itemized with customer names in the Debtor Entries Log before approval.", queueVm.SubmissionIncompleteReason);

        // 3. Attempt execution of ApproveCommand directly
        await queueVm.ApproveCommand.ExecuteAsync(null);

        // 4. Verify ZERO side effects: no DsmEntry, no DebitEntry, no DsmPersonalDebtor created
        using var verifyDb = _serviceProvider.GetRequiredService<FuelProDbContext>();
        Assert.Equal(dsmCountBefore, verifyDb.DsmEntries.Count());
        Assert.Equal(debitCountBefore, verifyDb.DebitEntries.Count());
        Assert.Equal(pdCountBefore, verifyDb.DsmPersonalDebtors.Count());
    }

    [Fact]
    public void Verify_ApprovalQueue_BlocksApproval_WhenDebtorEntriesHaveBlankOrInvalidNames()
    {
        var queueVm = _serviceProvider.GetRequiredService<DsmApprovalQueueViewModel>();
        queueVm.CreditAmount = 500.0;
        queueVm.DebtorEntries.Clear();

        // 1. Empty entries -> false
        Assert.False(queueVm.HasValidItemizedDebtors());

        // 2. Entry with empty name -> false
        var blankEntry = new DsmDebitRow { DebtorName = "", Amount = 500.0 };
        queueVm.DebtorEntries.Add(blankEntry);
        Assert.False(queueVm.HasValidItemizedDebtors());

        // 3. Entry with whitespace name -> false
        blankEntry.DebtorName = "   ";
        Assert.False(queueVm.HasValidItemizedDebtors());

        // 4. Entry with valid name but 0 amount -> false
        blankEntry.DebtorName = "Valid Name";
        blankEntry.Amount = 0.0;
        Assert.False(queueVm.HasValidItemizedDebtors());

        // 5. One valid entry + one blank entry -> false (must not allow invalid/blank entry to slip through)
        blankEntry.Amount = 250.0;
        var secondBlank = new DsmDebitRow { DebtorName = "", Amount = 250.0 };
        queueVm.DebtorEntries.Add(secondBlank);
        Assert.False(queueVm.HasValidItemizedDebtors());

        // 6. When second entry gets valid name -> true
        secondBlank.DebtorName = "Second Customer";
        Assert.True(queueVm.HasValidItemizedDebtors());

        // 7. Negative debtor amount -> false
        secondBlank.Amount = -250.0;
        Assert.False(queueVm.HasValidItemizedDebtors());

        // 8. Named debtor with 0 amount alongside valid debtor -> false
        secondBlank.Amount = 0.0;
        blankEntry.Amount = 500.0;
        Assert.False(queueVm.HasValidItemizedDebtors());

        // 9. Negative CreditAmount without itemized debtors -> false
        queueVm.DebtorEntries.Clear();
        queueVm.CreditAmount = -500.0;
        Assert.False(queueVm.HasValidItemizedDebtors());
    }

    [Fact]
    public async Task Verify_ApprovalQueue_AllowsApproval_WhenCreditIsFullyAndValidlyItemized()
    {
        var submission = new DsmPendingSubmission
        {
            Id = Guid.NewGuid(),
            DsmName = "Test Itemized DSM",
            DsmUserId = "usr-item-01",
            PumpId = 1,
            ShiftDate = DateTime.Today,
            ShiftType = "Morning",
            Notes = "Itemized Credit Test",
            MetadataJson = JsonConvert.SerializeObject(new
            {
                readings = new[]
                {
                    new { nozzleId = 1, openingReading = 1000.0, closingReading = 1050.0, rate = 100.0, fuelType = "MS-I" }
                }
            })
        };

        var queueVm = _serviceProvider.GetRequiredService<DsmApprovalQueueViewModel>();
        queueVm.SelectedSubmission = submission;
        await Task.Delay(50);
        while (queueVm.IsLoadingDetails)
        {
            await Task.Delay(50);
        }

        queueVm.NozzleReadings.Clear();
        queueVm.NozzleReadings.Add(new DsmNozzleRow
        {
            NozzleId = 1,
            FuelType = "MS-I",
            OpeningReading = 1000.0,
            ClosingReading = 1050.0,
            Rate = 100.0,
            Amount = 5000.0
        });

        queueVm.CashAmount = 4500.0;
        queueVm.UpiAmount = 0.0;
        queueVm.CardAmount = 0.0;
        queueVm.PetroCardAmount = 0.0;
        queueVm.CashDepositAmount = 0.0;
        queueVm.ExpenseAmount = 0.0;
        queueVm.OthersAmount = 0.0;
        queueVm.CreditAmount = 500.0;
        queueVm.DebtorEntries.Clear();
        queueVm.DebtorEntries.Add(new DsmDebitRow
        {
            DebtorName = "Shree Travels",
            Amount = 500.0,
            VehicleNumber = "MH12AB1234",
            SlipNumber = "SL-501"
        });

        // Recalculate totals
        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(queueVm, null);

        // 1. Verify approval is allowed
        Assert.True(queueVm.HasValidItemizedDebtors());
        Assert.True(queueVm.IsSubmissionComplete);
        Assert.True(queueVm.CanApprove);
        Assert.Empty(queueVm.SubmissionIncompleteReason);

        // 2. Approve submission into SQLite
        await queueVm.ApproveCommand.ExecuteAsync(null);

        // 3. Verify successful save in SQLite
        using var db = _serviceProvider.GetRequiredService<FuelProDbContext>();
        var savedEntry = db.DsmEntries.OrderByDescending(e => e.DsmEntryId).First();
        Assert.Equal("Test Itemized DSM", savedEntry.DsmName);
        Assert.Equal(500.0, (double)savedEntry.TotalCreditors, precision: 2);
        Assert.Equal(5000.0, (double)savedEntry.TotalCollection, precision: 2);
        Assert.Equal(0.0, (double)savedEntry.Mismatch, precision: 2);

        var savedDebit = db.DebitEntries.FirstOrDefault(d => d.DsmEntryId == savedEntry.DsmEntryId);
        Assert.NotNull(savedDebit);
        Assert.Equal("Shree Travels", savedDebit!.DebtorName);
        Assert.Equal(500.0, savedDebit.Amount);

        // 4. Verify no shortage created
        var shortages = db.DsmPersonalDebtors.Where(pd => pd.DsmEntryId == savedEntry.DsmEntryId).ToList();
        Assert.Empty(shortages);
    }

    [Fact]
    public void Verify_ApprovalQueue_EditingOrRemovingDebtorEntries_CannotLeaveStaleCreditThatPassesApproval()
    {
        var queueVm = _serviceProvider.GetRequiredService<DsmApprovalQueueViewModel>();
        queueVm.CreditAmount = 500.0;
        queueVm.DebtorEntries.Clear();

        var entry1 = new DsmDebitRow { DebtorName = "Shree Travels", Amount = 300.0 };
        var entry2 = new DsmDebitRow { DebtorName = "Sai Motors", Amount = 200.0 };
        queueVm.DebtorEntries.Add(entry1);
        queueVm.DebtorEntries.Add(entry2);

        // Recalculate totals
        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(queueVm, null);

        // State 1: Fully valid itemized entries
        Assert.True(queueVm.HasValidItemizedDebtors());
        Assert.Equal(500.0, queueVm.CreditAmount);

        // State 2: Edit entry 2 amount from 200 to 0 -> invalid zero-amount named entry blocks approval
        entry2.Amount = 0.0;
        Assert.False(queueVm.HasValidItemizedDebtors());

        // State 3: Edit entry 2 amount to negative -> invalid negative entry blocks approval
        entry2.Amount = -100.0;
        Assert.False(queueVm.HasValidItemizedDebtors());

        // State 4: Edit entry 2 name to empty with positive amount -> missing name blocks approval
        entry2.Amount = 200.0;
        entry2.DebtorName = "";
        Assert.False(queueVm.HasValidItemizedDebtors());

        // State 5: Remove entry 2 entirely -> CreditAmount auto-syncs to 300
        entry2.DebtorName = "Sai Motors";
        queueVm.DebtorEntries.Remove(entry2);
        typeof(DsmApprovalQueueViewModel).GetMethod("RecalculateTotals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(queueVm, null);
        Assert.Equal(300.0, queueVm.CreditAmount);
        Assert.True(queueVm.HasValidItemizedDebtors());

        // State 6: Remove entry 1 entirely -> DebtorEntries is empty, CreditAmount remains 300 -> MUST strictly block approval!
        queueVm.DebtorEntries.Remove(entry1);
        Assert.Empty(queueVm.DebtorEntries);
        Assert.False(queueVm.HasValidItemizedDebtors());
    }
}

