using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.UI;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;
using Xunit;

namespace FuelPro.Tests;

public class MultiPumpDsmEntryEngineTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly string _tempDir;

    public MultiPumpDsmEntryEngineTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_MultiPumpDsmTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "fuelPro.db");

        var services = new ServiceCollection();

        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"),
            ServiceLifetime.Transient);

        services.AddTransient<DbContext>(sp => sp.GetRequiredService<FuelProDbContext>());

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
        services.AddTransient<IFeatureToggleService, FeatureToggleService>();
        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddTransient<IStationConfigurationService, StationConfigurationService>();
        services.AddTransient<IDuplicateDataInspectionService, DuplicateDataInspectionService>();
        services.AddTransient<DuplicateResolutionService>();
        services.AddTransient<DraftService>();
        services.AddTransient<PrintService>();
        services.AddTransient<DsmEntryViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        typeof(App).GetProperty("Services")?.SetValue(null, _serviceProvider);
        typeof(App).GetProperty("DbPath")?.SetValue(null, _dbPath);

        // 5 physical pumps with 2 nozzles each:
        // Pump 1: N1 (MS-I), N2 (HSD)
        // Pump 2: N3 (MS-I), N4 (HSD)
        // Pump 3: N5 (MS-I), N6 (HSD)
        // Pump 4: N7 (MS-I), N8 (HSD)
        // Pump 5: N9 (MS-I), N10 (HSD)
        var testMappings = new List<PumpMapping>
        {
            new() { PumpMappingId = 1, PumpId = 1, NozzleNumber = 1, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 2, PumpId = 1, NozzleNumber = 2, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 3, PumpId = 2, NozzleNumber = 3, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 4, PumpId = 2, NozzleNumber = 4, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 5, PumpId = 3, NozzleNumber = 5, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 6, PumpId = 3, NozzleNumber = 6, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 7, PumpId = 4, NozzleNumber = 7, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 8, PumpId = 4, NozzleNumber = 8, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 9, PumpId = 5, NozzleNumber = 9, FuelType = "MS-I", IsActive = true },
            new() { PumpMappingId = 10, PumpId = 5, NozzleNumber = 10, FuelType = "HSD", IsActive = true }
        };
        PumpConfiguration.InitializeFromDb(testMappings);

        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();
        context.PumpMappings.AddRange(testMappings);
        context.SaveChanges();
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

    [Fact]
    public async Task Test_01_SinglePump_ProducesSingleEntry_ZeroSlaves()
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        var date = new DateTime(2026, 9, 8);
        var nozzleReadings = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 1000, ClosingReading = 1050, Rate = 100 }, // 5000
            new() { NozzleNumber = 2, FuelType = "HSD", OpeningReading = 2000, ClosingReading = 2050, Rate = 90 }    // 4500
        };

        var payment = new PaymentCollection { PhonePeMorning = 5000, CashDeposit = 4500 };
        var cash = new List<CashDenomination> { new() { CashType = "Cash1", TotalAmount = 4500 } };

        var result = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Operator P1", 1,
            nozzleReadings, payment, new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), cash,
            connectedPumpId: null,
            existingEntryId: null,
            connectedPumpIds: null);

        Assert.True(result.Success, $"Save failed: {result.Error}");
        Assert.NotNull(result.Data);

        var allEntries = await db.DsmEntries.ToListAsync();
        Assert.Single(allEntries);

        var primary = allEntries[0];
        Assert.Equal(1, primary.PumpId);
        Assert.Null(primary.ConnectedPumpId);
        Assert.Null(primary.ConnectedPumpIdsJson);
        Assert.Null(primary.ReconciledToPumpId);
        Assert.Equal(9500, primary.GrossSales);

        var readings = await db.NozzleReadings.Where(n => n.DsmEntryId == primary.DsmEntryId).ToListAsync();
        Assert.Equal(2, readings.Count);
        Assert.Contains(readings, r => r.NozzleNumber == 1);
        Assert.Contains(readings, r => r.NozzleNumber == 2);
    }

    [Fact]
    public async Task Test_02_LegacyTwoPump_ProducesOnePrimaryOneSlave_BackwardCompatible()
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        var date = new DateTime(2026, 9, 8);
        var nozzleReadings = new List<NozzleReading>
        {
            // Pump 1
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 1000, ClosingReading = 1020, Rate = 100 }, // 2000
            new() { NozzleNumber = 2, FuelType = "HSD", OpeningReading = 2000, ClosingReading = 2030, Rate = 90 },   // 2700
            // Pump 2
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 3000, ClosingReading = 3010, Rate = 100 }, // 1000
            new() { NozzleNumber = 4, FuelType = "HSD", OpeningReading = 4000, ClosingReading = 4020, Rate = 90 }    // 1800
        };

        var payment = new PaymentCollection { PhonePeMorning = 4700, CashDeposit = 2800 };
        var cash = new List<CashDenomination> { new() { CashType = "Cash1", TotalAmount = 2800 } };

        // Legacy 2-pump call passing connectedPumpId: 2
        var result = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Operator P1-P2", 1,
            nozzleReadings, payment, new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), cash,
            connectedPumpId: 2);

        Assert.True(result.Success, $"Save failed: {result.Error}");
        Assert.NotNull(result.Data);

        var allEntries = await db.DsmEntries.OrderBy(e => e.PumpId).ToListAsync();
        Assert.Equal(2, allEntries.Count);

        var primary = allEntries.First(e => e.PumpId == 1);
        var slave = allEntries.First(e => e.PumpId == 2);

        // Primary checks
        Assert.Equal(2, primary.ConnectedPumpId);
        Assert.Equal("[2]", primary.ConnectedPumpIdsJson);
        Assert.Null(primary.ReconciledToPumpId);
        Assert.Equal(7500, primary.GrossSales); // 2000 + 2700 + 1000 + 1800

        // Slave checks
        Assert.Equal(primary.DsmEntryId, slave.ReconciledToPumpId);
        Assert.Null(slave.ConnectedPumpId);
        Assert.Equal(2800, slave.GrossSales); // 1000 + 1800
        Assert.Equal(0, slave.Mismatch);

        // Collections only on primary
        var primaryPayments = await db.PaymentCollections.Where(p => p.DsmEntryId == primary.DsmEntryId).ToListAsync();
        Assert.Single(primaryPayments);
        Assert.True(primaryPayments[0].PhonePeMorning > 0 || primaryPayments[0].CashDeposit > 0);

        var slavePayments = await db.PaymentCollections.Where(p => p.DsmEntryId == slave.DsmEntryId).ToListAsync();
        foreach (var sp in slavePayments)
        {
            Assert.Equal(0, sp.TotalDigitalPayments);
            Assert.Equal(0, sp.CashDeposit);
        }
        Assert.Equal(0, slave.TotalCollection);
        Assert.Equal(0, slave.Mismatch);

        // Nozzles correctly partitioned
        var p1Readings = await db.NozzleReadings.Where(n => n.DsmEntryId == primary.DsmEntryId).ToListAsync();
        var p2Readings = await db.NozzleReadings.Where(n => n.DsmEntryId == slave.DsmEntryId).ToListAsync();
        Assert.Equal(2, p1Readings.Count);
        Assert.All(p1Readings, r => Assert.True(r.NozzleNumber is 1 or 2));
        Assert.Equal(2, p2Readings.Count);
        Assert.All(p2Readings, r => Assert.True(r.NozzleNumber is 3 or 4));
    }

    [Fact]
    public async Task Test_03_ThreePumpGroup_ProducesOnePrimaryTwoSlaves()
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        var date = new DateTime(2026, 9, 8);
        var nozzleReadings = new List<NozzleReading>
        {
            // P1
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 1000, ClosingReading = 1010, Rate = 100 }, // 1000
            new() { NozzleNumber = 2, FuelType = "HSD", OpeningReading = 2000, ClosingReading = 2010, Rate = 90 },   // 900
            // P2
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 3000, ClosingReading = 3020, Rate = 100 }, // 2000
            new() { NozzleNumber = 4, FuelType = "HSD", OpeningReading = 4000, ClosingReading = 4010, Rate = 90 },   // 900
            // P3
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 5000, ClosingReading = 5030, Rate = 100 }, // 3000
            new() { NozzleNumber = 6, FuelType = "HSD", OpeningReading = 6000, ClosingReading = 6010, Rate = 90 }    // 900
        };

        var payment = new PaymentCollection { PhonePeMorning = 8700 };

        var result = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Operator 3-Pump", 1,
            nozzleReadings, payment, new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), new List<CashDenomination>(),
            connectedPumpIds: new[] { 2, 3 });

        Assert.True(result.Success, $"Save failed: {result.Error}");

        var allEntries = await db.DsmEntries.OrderBy(e => e.PumpId).ToListAsync();
        Assert.Equal(3, allEntries.Count);

        var primary = allEntries.First(e => e.PumpId == 1);
        var slave1 = allEntries.First(e => e.PumpId == 2);
        var slave2 = allEntries.First(e => e.PumpId == 3);

        // Primary
        Assert.Equal(2, primary.ConnectedPumpId);
        Assert.Equal("[2,3]", primary.ConnectedPumpIdsJson);
        Assert.Equal(8700, primary.GrossSales);

        // Slaves
        Assert.Equal(primary.DsmEntryId, slave1.ReconciledToPumpId);
        Assert.Equal(primary.DsmEntryId, slave2.ReconciledToPumpId);
        Assert.Equal(2900, slave1.GrossSales); // 2000 + 900
        Assert.Equal(3900, slave2.GrossSales); // 3000 + 900
        Assert.Equal(0, slave1.Mismatch);
        Assert.Equal(0, slave2.Mismatch);
    }

    [Fact]
    public async Task Test_04_FourPumpGroup_ProducesOnePrimaryThreeSlaves()
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        var date = new DateTime(2026, 9, 8);
        var nozzleReadings = new List<NozzleReading>
        {
            // P1
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 110, Rate = 100 }, // 1000
            new() { NozzleNumber = 2, FuelType = "HSD", OpeningReading = 200, ClosingReading = 210, Rate = 100 },   // 1000
            // P2
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 300, ClosingReading = 320, Rate = 100 }, // 2000
            new() { NozzleNumber = 4, FuelType = "HSD", OpeningReading = 400, ClosingReading = 410, Rate = 100 },   // 1000
            // P3
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 500, ClosingReading = 530, Rate = 100 }, // 3000
            new() { NozzleNumber = 6, FuelType = "HSD", OpeningReading = 600, ClosingReading = 610, Rate = 100 },   // 1000
            // P4
            new() { NozzleNumber = 7, FuelType = "MS-I", OpeningReading = 700, ClosingReading = 740, Rate = 100 }, // 4000
            new() { NozzleNumber = 8, FuelType = "HSD", OpeningReading = 800, ClosingReading = 810, Rate = 100 }    // 1000
        };

        var payment = new PaymentCollection { PhonePeMorning = 14000 };

        var result = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Operator 4-Pump", 1,
            nozzleReadings, payment, new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), new List<CashDenomination>(),
            connectedPumpIds: new[] { 2, 3, 4 });

        Assert.True(result.Success, $"Save failed: {result.Error}");

        var allEntries = await db.DsmEntries.OrderBy(e => e.PumpId).ToListAsync();
        Assert.Equal(4, allEntries.Count);

        var primary = allEntries.First(e => e.PumpId == 1);
        var slave1 = allEntries.First(e => e.PumpId == 2);
        var slave2 = allEntries.First(e => e.PumpId == 3);
        var slave3 = allEntries.First(e => e.PumpId == 4);

        // Primary
        Assert.Equal(2, primary.ConnectedPumpId);
        Assert.Equal("[2,3,4]", primary.ConnectedPumpIdsJson);
        Assert.Equal(14000, primary.GrossSales);

        // Slaves
        Assert.Equal(primary.DsmEntryId, slave1.ReconciledToPumpId);
        Assert.Equal(primary.DsmEntryId, slave2.ReconciledToPumpId);
        Assert.Equal(primary.DsmEntryId, slave3.ReconciledToPumpId);

        Assert.Equal(3000, slave1.GrossSales);
        Assert.Equal(4000, slave2.GrossSales);
        Assert.Equal(5000, slave3.GrossSales);

        Assert.Equal(0, slave1.Mismatch);
        Assert.Equal(0, slave2.Mismatch);
        Assert.Equal(0, slave3.Mismatch);
    }

    [Fact]
    public async Task Test_05_NozzleReadings_PartitionedExclusivelyToPhysicalPump_NotPositional()
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        var date = new DateTime(2026, 9, 8);
        // Completely scrambled input order: P4, P1, P3, P2, P1, P4, P2, P3
        var scrambledReadings = new List<NozzleReading>
        {
            new() { NozzleNumber = 7, FuelType = "MS-I", OpeningReading = 700, ClosingReading = 710, Rate = 100 }, // P4
            new() { NozzleNumber = 2, FuelType = "HSD", OpeningReading = 200, ClosingReading = 210, Rate = 100 },   // P1
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 500, ClosingReading = 510, Rate = 100 }, // P3
            new() { NozzleNumber = 4, FuelType = "HSD", OpeningReading = 400, ClosingReading = 410, Rate = 100 },   // P2
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 110, Rate = 100 }, // P1
            new() { NozzleNumber = 8, FuelType = "HSD", OpeningReading = 800, ClosingReading = 810, Rate = 100 },   // P4
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 300, ClosingReading = 310, Rate = 100 }, // P2
            new() { NozzleNumber = 6, FuelType = "HSD", OpeningReading = 600, ClosingReading = 610, Rate = 100 }    // P3
        };

        var result = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Scrambled Attendant", 1,
            scrambledReadings, new PaymentCollection { PhonePeMorning = 8000 },
            new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), new List<CashDenomination>(),
            connectedPumpIds: new[] { 2, 3, 4 });

        Assert.True(result.Success, $"Save failed: {result.Error}");

        var primary = await db.DsmEntries.Include(e => e.NozzleReadings).FirstAsync(e => e.PumpId == 1);
        var slaveP2 = await db.DsmEntries.Include(e => e.NozzleReadings).FirstAsync(e => e.PumpId == 2);
        var slaveP3 = await db.DsmEntries.Include(e => e.NozzleReadings).FirstAsync(e => e.PumpId == 3);
        var slaveP4 = await db.DsmEntries.Include(e => e.NozzleReadings).FirstAsync(e => e.PumpId == 4);

        // Assert physical pump mappings are respected 100%, without positional bias
        var p1Nozzles = primary.NozzleReadings.Select(n => n.NozzleNumber).OrderBy(n => n).ToList();
        var p2Nozzles = slaveP2.NozzleReadings.Select(n => n.NozzleNumber).OrderBy(n => n).ToList();
        var p3Nozzles = slaveP3.NozzleReadings.Select(n => n.NozzleNumber).OrderBy(n => n).ToList();
        var p4Nozzles = slaveP4.NozzleReadings.Select(n => n.NozzleNumber).OrderBy(n => n).ToList();

        Assert.Equal(new[] { 1, 2 }, p1Nozzles);
        Assert.Equal(new[] { 3, 4 }, p2Nozzles);
        Assert.Equal(new[] { 5, 6 }, p3Nozzles);
        Assert.Equal(new[] { 7, 8 }, p4Nozzles);
    }

    [Fact]
    public async Task Test_06_FourPreviousOpenings_LoadedAcrossAllPumpsInGroup()
    {
        // 1. Setup previous shift in database with closing readings for all 4 pumps
        var prevDate = new DateTime(2026, 9, 7);
        using (var scope = _serviceProvider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            var prevShift = new Shift { ShiftDate = prevDate, ShiftType = "B" };
            db.Shifts.Add(prevShift);
            await db.SaveChangesAsync();

            // P1
            var e1 = new DsmEntry { ShiftId = prevShift.ShiftId, PumpId = 1, DsmName = "Op1", GrossSales = 1000 };
            db.DsmEntries.Add(e1);
            await db.SaveChangesAsync();
            db.NozzleReadings.AddRange(
                new NozzleReading { DsmEntryId = e1.DsmEntryId, NozzleNumber = 1, OpeningReading = 1000, ClosingReading = 1150, Rate = 100 },
                new NozzleReading { DsmEntryId = e1.DsmEntryId, NozzleNumber = 2, OpeningReading = 2000, ClosingReading = 2250, Rate = 90 });

            // P2
            var e2 = new DsmEntry { ShiftId = prevShift.ShiftId, PumpId = 2, DsmName = "Op1", GrossSales = 1000 };
            db.DsmEntries.Add(e2);
            await db.SaveChangesAsync();
            db.NozzleReadings.AddRange(
                new NozzleReading { DsmEntryId = e2.DsmEntryId, NozzleNumber = 3, OpeningReading = 3000, ClosingReading = 3350, Rate = 100 },
                new NozzleReading { DsmEntryId = e2.DsmEntryId, NozzleNumber = 4, OpeningReading = 4000, ClosingReading = 4450, Rate = 90 });

            // P3
            var e3 = new DsmEntry { ShiftId = prevShift.ShiftId, PumpId = 3, DsmName = "Op1", GrossSales = 1000 };
            db.DsmEntries.Add(e3);
            await db.SaveChangesAsync();
            db.NozzleReadings.AddRange(
                new NozzleReading { DsmEntryId = e3.DsmEntryId, NozzleNumber = 5, OpeningReading = 5000, ClosingReading = 5550, Rate = 100 },
                new NozzleReading { DsmEntryId = e3.DsmEntryId, NozzleNumber = 6, OpeningReading = 6000, ClosingReading = 6650, Rate = 90 });

            // P4
            var e4 = new DsmEntry { ShiftId = prevShift.ShiftId, PumpId = 4, DsmName = "Op1", GrossSales = 1000 };
            db.DsmEntries.Add(e4);
            await db.SaveChangesAsync();
            db.NozzleReadings.AddRange(
                new NozzleReading { DsmEntryId = e4.DsmEntryId, NozzleNumber = 7, OpeningReading = 7000, ClosingReading = 7750, Rate = 100 },
                new NozzleReading { DsmEntryId = e4.DsmEntryId, NozzleNumber = 8, OpeningReading = 8000, ClosingReading = 8850, Rate = 90 });

            await db.SaveChangesAsync();

            // Save StationConfiguration for 4-pump group [1, 2, 3, 4]
            var stationConfigService = scope.ServiceProvider.GetRequiredService<IStationConfigurationService>();
            var groupConfig = new PumpConnectionConfiguration
            {
                IsEnabled = true,
                ConnectionSize = ConnectionSize.Four,
                Groups = new List<PumpConnectionGroup>
                {
                    new()
                    {
                        GroupId = 1,
                        GroupName = "Main 4-Pump Group",
                        PrimaryPumpId = 1,
                        ConnectedPumpIds = new List<int> { 2, 3, 4 }
                    }
                }
            };
            await stationConfigService.SavePumpConnectionConfigurationAsync(groupConfig);
        }

        // 2. Initialize ViewModel for current shift A on 2026-09-08
        using (var scope = _serviceProvider.CreateScope())
        {
            var vm = scope.ServiceProvider.GetRequiredService<DsmEntryViewModel>();
            vm.SelectedDate = new DateTime(2026, 9, 8);
            vm.SelectedShift = "A";
            vm.SelectedPump = vm.PumpOptions.First(p => p.PumpId == 1);

            // Wait for nozzle load
            await Task.Delay(100);

            Assert.Equal(8, vm.NozzleReadings.Count);

            var n1 = vm.NozzleReadings.First(n => n.NozzleNumber == 1);
            var n2 = vm.NozzleReadings.First(n => n.NozzleNumber == 2);
            var n3 = vm.NozzleReadings.First(n => n.NozzleNumber == 3);
            var n4 = vm.NozzleReadings.First(n => n.NozzleNumber == 4);
            var n5 = vm.NozzleReadings.First(n => n.NozzleNumber == 5);
            var n6 = vm.NozzleReadings.First(n => n.NozzleNumber == 6);
            var n7 = vm.NozzleReadings.First(n => n.NozzleNumber == 7);
            var n8 = vm.NozzleReadings.First(n => n.NozzleNumber == 8);

            // Assert all 4 pumps loaded their correct previous shift closings
            Assert.Equal(1150, n1.AutoOpeningReading);
            Assert.Equal(2250, n2.AutoOpeningReading);
            Assert.Equal(3350, n3.AutoOpeningReading);
            Assert.Equal(4450, n4.AutoOpeningReading);
            Assert.Equal(5550, n5.AutoOpeningReading);
            Assert.Equal(6650, n6.AutoOpeningReading);
            Assert.Equal(7750, n7.AutoOpeningReading);
            Assert.Equal(8850, n8.AutoOpeningReading);
        }
    }

    [Fact]
    public async Task Test_07_GrossSales_AggregatedCorrectly_CollectionsOnlyOnPrimary()
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        var date = new DateTime(2026, 9, 8);
        var nozzleReadings = new List<NozzleReading>
        {
            // P1: 2500
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 110, Rate = 100 }, // 1000
            new() { NozzleNumber = 2, FuelType = "HSD", OpeningReading = 200, ClosingReading = 215, Rate = 100 },   // 1500
            // P2: 4500
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 300, ClosingReading = 320, Rate = 100 }, // 2000
            new() { NozzleNumber = 4, FuelType = "HSD", OpeningReading = 400, ClosingReading = 425, Rate = 100 },   // 2500
            // P3: 6500
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 500, ClosingReading = 530, Rate = 100 }, // 3000
            new() { NozzleNumber = 6, FuelType = "HSD", OpeningReading = 600, ClosingReading = 635, Rate = 100 },   // 3500
            // P4: 8500
            new() { NozzleNumber = 7, FuelType = "MS-I", OpeningReading = 700, ClosingReading = 740, Rate = 100 }, // 4000
            new() { NozzleNumber = 8, FuelType = "HSD", OpeningReading = 800, ClosingReading = 845, Rate = 100 }    // 4500
        };
        // Total Group Gross Sales = 2500 + 4500 + 6500 + 8500 = 22,000

        var payment = new PaymentCollection
        {
            PhonePeMorning = 10000,
            CashDeposit = 7000
        };

        var debits = new List<DebitEntry>
        {
            new() { DebtorName = "Transport Co", Amount = 3000 }
        };

        var expenses = new List<Expense>
        {
            new() { Description = "Tea & Refreshments", Amount = 2000 }
        };

        var cash = new List<CashDenomination>
        {
            new() { CashType = "Cash1", TotalAmount = 7000 }
        };

        var result = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Full Group Attendant", 1,
            nozzleReadings, payment, debits, new List<TestingEntry>(),
            expenses, cash,
            connectedPumpIds: new[] { 2, 3, 4 });

        Assert.True(result.Success, $"Save failed: {result.Error}");

        var primary = await db.DsmEntries
            .Include(e => e.PaymentCollection)
            .Include(e => e.DebitEntries)
            .Include(e => e.Expenses)
            .Include(e => e.CashDenominations)
            .FirstAsync(e => e.PumpId == 1);

        var slaves = await db.DsmEntries
            .Include(e => e.PaymentCollection)
            .Include(e => e.DebitEntries)
            .Include(e => e.Expenses)
            .Include(e => e.CashDenominations)
            .Where(e => e.PumpId != 1)
            .OrderBy(e => e.PumpId)
            .ToListAsync();

        Assert.Equal(22000, primary.GrossSales);
        Assert.NotNull(primary.PaymentCollection);
        Assert.Single(primary.DebitEntries);
        Assert.Single(primary.Expenses);
        Assert.Single(primary.CashDenominations);

        Assert.Equal(3, slaves.Count);
        Assert.Equal(4500, slaves[0].GrossSales);
        Assert.Equal(6500, slaves[1].GrossSales);
        Assert.Equal(8500, slaves[2].GrossSales);

        foreach (var slave in slaves)
        {
            if (slave.PaymentCollection != null)
            {
                Assert.Equal(0, slave.PaymentCollection.TotalDigitalPayments);
                Assert.Equal(0, slave.PaymentCollection.CashDeposit);
            }
            Assert.Empty(slave.DebitEntries);
            Assert.Empty(slave.Expenses);
            Assert.Empty(slave.CashDenominations);
            Assert.Equal(0, slave.Mismatch);
            Assert.Equal(0, slave.TotalCollection);
        }
    }

    [Fact]
    public async Task Test_08_LegacyConnectedPumpId_StillWorks_BackwardCompatibility()
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        var date = new DateTime(2026, 9, 8);
        var readings = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 110, Rate = 100 },
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 300, ClosingReading = 310, Rate = 100 },
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 500, ClosingReading = 510, Rate = 100 },
            new() { NozzleNumber = 7, FuelType = "MS-I", OpeningReading = 700, ClosingReading = 710, Rate = 100 }
        };

        var result = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Op Compat", 1,
            readings, new PaymentCollection { PhonePeMorning = 4000 },
            new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), new List<CashDenomination>(),
            connectedPumpIds: new[] { 2, 3, 4 });

        Assert.True(result.Success);

        var primary = await db.DsmEntries.FirstAsync(e => e.PumpId == 1);

        // Legacy scalar column is populated with the first connected pump
        Assert.Equal(2, primary.ConnectedPumpId);

        // JSON column contains the full connected list
        Assert.Equal("[2,3,4]", primary.ConnectedPumpIdsJson);

        // Helper resolves all 3 connected pumps
        var resolved = primary.GetEffectiveConnectedPumpIds();
        Assert.Equal(new[] { 2, 3, 4 }, resolved);
    }

    [Fact]
    public async Task Test_09_AtomicSave_InvalidNozzleOwnership_RejectsWithoutPartialWrites()
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        var date = new DateTime(2026, 9, 8);

        // Initial database counts
        int initialEntryCount = await db.DsmEntries.CountAsync();
        int initialNozzleCount = await db.NozzleReadings.CountAsync();
        int initialPaymentCount = await db.PaymentCollections.CountAsync();

        // Submit:
        // N1 -> Pump 1 (valid)
        // N3 -> Pump 2 (valid)
        // N5 -> Pump 3 (valid)
        // N10 -> Pump 5 (INVALID! Pump 5 is outside configured group [1, 2, 3, 4])
        var readingsWithInvalidOwnership = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 110, Rate = 100 },
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 300, ClosingReading = 310, Rate = 100 },
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 500, ClosingReading = 510, Rate = 100 },
            new() { NozzleNumber = 10, FuelType = "HSD", OpeningReading = 900, ClosingReading = 910, Rate = 90 } // Pump 5!
        };

        var result = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Attendant Atomic Test", 1,
            readingsWithInvalidOwnership,
            new PaymentCollection { PhonePeMorning = 5000 },
            new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), new List<CashDenomination>(),
            connectedPumpIds: new[] { 2, 3, 4 });

        // Save MUST be rejected
        Assert.False(result.Success);
        Assert.Contains("Nozzle 10 belongs to Pump 5, which is not part of connection group", result.Error);

        // CRITICAL ATOMICITY CHECK: Zero new or partial database writes!
        int finalEntryCount = await db.DsmEntries.CountAsync();
        int finalNozzleCount = await db.NozzleReadings.CountAsync();
        int finalPaymentCount = await db.PaymentCollections.CountAsync();

        Assert.Equal(initialEntryCount, finalEntryCount);
        Assert.Equal(initialNozzleCount, finalNozzleCount);
        Assert.Equal(initialPaymentCount, finalPaymentCount);
    }

    [Fact]
    public async Task Test_10_AtomicSave_DuplicateNozzles_RejectsWithoutPartialWrites()
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        var date = new DateTime(2026, 9, 8);

        int initialEntryCount = await db.DsmEntries.CountAsync();
        int initialNozzleCount = await db.NozzleReadings.CountAsync();

        // Submit duplicate nozzle N1
        var duplicateReadings = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 110, Rate = 100 },
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 110, ClosingReading = 120, Rate = 100 },
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 300, ClosingReading = 310, Rate = 100 }
        };

        var result = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Duplicate Attendant", 1,
            duplicateReadings,
            new PaymentCollection { PhonePeMorning = 3000 },
            new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), new List<CashDenomination>(),
            connectedPumpIds: new[] { 2 });

        Assert.False(result.Success);
        Assert.Contains("Duplicate nozzle reading", result.Error);

        // Atomic check: zero writes
        Assert.Equal(initialEntryCount, await db.DsmEntries.CountAsync());
        Assert.Equal(initialNozzleCount, await db.NozzleReadings.CountAsync());
    }

    [Fact]
    public async Task Test_11_EditingFourPumpEntry_PreservesAllPumpReadings()
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        var date = new DateTime(2026, 9, 8);
        var initialReadings = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 110, Rate = 100 }, // 1000
            new() { NozzleNumber = 2, FuelType = "HSD", OpeningReading = 200, ClosingReading = 210, Rate = 100 },   // 1000
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 300, ClosingReading = 310, Rate = 100 }, // 1000
            new() { NozzleNumber = 4, FuelType = "HSD", OpeningReading = 400, ClosingReading = 410, Rate = 100 },   // 1000
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 500, ClosingReading = 510, Rate = 100 }, // 1000
            new() { NozzleNumber = 6, FuelType = "HSD", OpeningReading = 600, ClosingReading = 610, Rate = 100 },   // 1000
            new() { NozzleNumber = 7, FuelType = "MS-I", OpeningReading = 700, ClosingReading = 710, Rate = 100 }, // 1000
            new() { NozzleNumber = 8, FuelType = "HSD", OpeningReading = 800, ClosingReading = 810, Rate = 100 }    // 1000
        };

        var initialSave = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Attendant Edit Test", 1,
            initialReadings,
            new PaymentCollection { PhonePeMorning = 8000 },
            new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), new List<CashDenomination>(),
            connectedPumpIds: new[] { 2, 3, 4 });

        Assert.True(initialSave.Success);
        int primaryId = initialSave.Data!.DsmEntryId;

        // Verify initial 4 entries
        Assert.Equal(4, await db.DsmEntries.CountAsync());

        // Update readings: N1 on P1 from 110 to 120 (sales 2000), N8 on P4 from 810 to 825 (sales 2500)
        var updatedReadings = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 120, Rate = 100 }, // 2000 (+1000)
            new() { NozzleNumber = 2, FuelType = "HSD", OpeningReading = 200, ClosingReading = 210, Rate = 100 },   // 1000
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 300, ClosingReading = 310, Rate = 100 }, // 1000
            new() { NozzleNumber = 4, FuelType = "HSD", OpeningReading = 400, ClosingReading = 410, Rate = 100 },   // 1000
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 500, ClosingReading = 510, Rate = 100 }, // 1000
            new() { NozzleNumber = 6, FuelType = "HSD", OpeningReading = 600, ClosingReading = 610, Rate = 100 },   // 1000
            new() { NozzleNumber = 7, FuelType = "MS-I", OpeningReading = 700, ClosingReading = 710, Rate = 100 }, // 1000
            new() { NozzleNumber = 8, FuelType = "HSD", OpeningReading = 800, ClosingReading = 825, Rate = 100 }    // 2500 (+1500)
        };
        // New group gross sales = 10,500

        var editSave = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Attendant Edit Test", 1,
            updatedReadings,
            new PaymentCollection { PhonePeMorning = 10500 },
            new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), new List<CashDenomination>(),
            connectedPumpIds: new[] { 2, 3, 4 },
            existingEntryId: primaryId);

        Assert.True(editSave.Success, $"Edit save failed: {editSave.Error}");

        // DB MUST still contain exactly 4 entries (no duplicate slaves!)
        var entriesAfterEdit = await db.DsmEntries.OrderBy(e => e.PumpId).ToListAsync();
        Assert.Equal(4, entriesAfterEdit.Count);

        var primaryAfter = entriesAfterEdit.First(e => e.PumpId == 1);
        var slave4After = entriesAfterEdit.First(e => e.PumpId == 4);

        Assert.Equal(10500, primaryAfter.GrossSales);
        Assert.Equal(3500, slave4After.GrossSales); // 1000 + 2500

        // Verify updated nozzle reading on P1
        var n1InDb = await db.NozzleReadings.FirstAsync(n => n.DsmEntryId == primaryAfter.DsmEntryId && n.NozzleNumber == 1);
        Assert.Equal(120, n1InDb.ClosingReading);

        // Verify updated nozzle reading on P4
        var n8InDb = await db.NozzleReadings.FirstAsync(n => n.DsmEntryId == slave4After.DsmEntryId && n.NozzleNumber == 8);
        Assert.Equal(825, n8InDb.ClosingReading);

        // Verify P2 and P3 nozzle readings were fully preserved
        var n3InDb = await db.NozzleReadings.FirstAsync(n => n.NozzleNumber == 3);
        Assert.Equal(310, n3InDb.ClosingReading);
        var n5InDb = await db.NozzleReadings.FirstAsync(n => n.NozzleNumber == 5);
        Assert.Equal(510, n5InDb.ClosingReading);
    }

    [Fact]
    public async Task Test_12_EditMode_CleansUpOrphanedSlaves_WhenGroupShrinks()
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        var date = new DateTime(2026, 9, 8);
        var readings4Pumps = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 110, Rate = 100 },
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 300, ClosingReading = 310, Rate = 100 },
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 500, ClosingReading = 510, Rate = 100 },
            new() { NozzleNumber = 7, FuelType = "MS-I", OpeningReading = 700, ClosingReading = 710, Rate = 100 }
        };

        var initialSave = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Attendant Shrink Group", 1,
            readings4Pumps,
            new PaymentCollection { PhonePeMorning = 4000 },
            new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), new List<CashDenomination>(),
            connectedPumpIds: new[] { 2, 3, 4 });

        Assert.True(initialSave.Success);
        int primaryId = initialSave.Data!.DsmEntryId;

        Assert.Equal(4, await db.DsmEntries.CountAsync());

        // Now edit entry: group shrinks to [1, 2, 3] (P4 is removed)
        var readings3Pumps = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 110, Rate = 100 },
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 300, ClosingReading = 310, Rate = 100 },
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 500, ClosingReading = 510, Rate = 100 }
        };

        var shrinkSave = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Attendant Shrink Group", 1,
            readings3Pumps,
            new PaymentCollection { PhonePeMorning = 3000 },
            new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), new List<CashDenomination>(),
            connectedPumpIds: new[] { 2, 3 },
            existingEntryId: primaryId);

        Assert.True(shrinkSave.Success);

        // DB should now contain exactly 3 entries: P1, P2, P3 (P4 slave was cleanly removed)
        var entries = await db.DsmEntries.OrderBy(e => e.PumpId).ToListAsync();
        Assert.Equal(3, entries.Count);
        Assert.Equal(new[] { 1, 2, 3 }, entries.Select(e => e.PumpId).ToArray());

        // P4 slave entry is gone
        Assert.DoesNotContain(entries, e => e.PumpId == 4);
    }

    [Fact]
    public async Task Test_13_RequirementE_Pump3Nozzle_CannotBeSubmitted_UnderPump1And2Connection()
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();

        var date = new DateTime(2026, 9, 8);
        // Connection group is [1, 2]. Attempt to submit Nozzle 5 (belongs to Pump 3)
        var readings = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 110, Rate = 100 }, // Pump 1
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 300, ClosingReading = 310, Rate = 100 }, // Pump 2
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 500, ClosingReading = 510, Rate = 100 }  // Pump 3 (INVALID!)
        };

        var result = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Operator P1-P2", 1,
            readings,
            new PaymentCollection { PhonePeMorning = 3000 },
            new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), new List<CashDenomination>(),
            connectedPumpIds: new[] { 2 });

        Assert.False(result.Success);
        Assert.Contains("Nozzle 5 belongs to Pump 3, which is not part of connection group [1, 2]", result.Error);
    }

    [Fact]
    public async Task Test_14_RequirementF_Pump4Nozzle_CannotBeSubmitted_UnderPump123Connection()
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();

        var date = new DateTime(2026, 9, 8);
        // Connection group is [1, 2, 3]. Attempt to submit Nozzle 7 (belongs to Pump 4)
        var readings = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 110, Rate = 100 }, // Pump 1
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 300, ClosingReading = 310, Rate = 100 }, // Pump 2
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 500, ClosingReading = 510, Rate = 100 }, // Pump 3
            new() { NozzleNumber = 7, FuelType = "MS-I", OpeningReading = 700, ClosingReading = 710, Rate = 100 }  // Pump 4 (INVALID!)
        };

        var result = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Operator P1-P2-P3", 1,
            readings,
            new PaymentCollection { PhonePeMorning = 4000 },
            new List<DebitEntry>(), new List<TestingEntry>(),
            new List<Expense>(), new List<CashDenomination>(),
            connectedPumpIds: new[] { 2, 3 });

        Assert.False(result.Success);
        Assert.Contains("Nozzle 7 belongs to Pump 4, which is not part of connection group [1, 2, 3]", result.Error);
    }

    [Fact]
    public async Task Test_15_RequirementI_MultiSlaveAggregation_Total30000_NoDoubleCounting()
    {
        using var scope = _serviceProvider.CreateScope();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var shiftAggService = scope.ServiceProvider.GetRequiredService<IShiftAggregationService>();
        var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();
        var db = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();

        var date = new DateTime(2026, 9, 8);
        var shift = new Shift { ShiftDate = date, ShiftType = "A" };
        db.Shifts.Add(shift);
        await db.SaveChangesAsync();

        // 4 pumps, each producing 7,500 physical fuel sales (50L @ 75 each nozzle)
        // P1: N1 (3750) + N2 (3750) = 7500
        // P2: N3 (3750) + N4 (3750) = 7500
        // P3: N5 (3750) + N6 (3750) = 7500
        // P4: N7 (3750) + N8 (3750) = 7500
        // Combined Group Sales = 30,000
        var nozzleReadings = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 150, Rate = 75 }, // 3750
            new() { NozzleNumber = 2, FuelType = "HSD",  OpeningReading = 200, ClosingReading = 250, Rate = 75 }, // 3750
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 300, ClosingReading = 350, Rate = 75 }, // 3750
            new() { NozzleNumber = 4, FuelType = "HSD",  OpeningReading = 400, ClosingReading = 450, Rate = 75 }, // 3750
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 500, ClosingReading = 550, Rate = 75 }, // 3750
            new() { NozzleNumber = 6, FuelType = "HSD",  OpeningReading = 600, ClosingReading = 650, Rate = 75 }, // 3750
            new() { NozzleNumber = 7, FuelType = "MS-I", OpeningReading = 700, ClosingReading = 750, Rate = 75 }, // 3750
            new() { NozzleNumber = 8, FuelType = "HSD",  OpeningReading = 800, ClosingReading = 850, Rate = 75 }  // 3750
        };

        // Financial collections on PRIMARY ONLY:
        // Cash: 10,000
        // PhonePe: 10,000
        // Debits: 5,000
        // Expenses: 3,000
        // Testing: 2,000
        // Total Collections = 30,000 -> Mismatch = 0!
        var payment = new PaymentCollection { CashDeposit = 10000, PhonePeMorning = 10000 };
        var cash = new List<CashDenomination> { new() { CashType = "Cash1", TotalAmount = 10000 } };
        var debits = new List<DebitEntry> { new() { DebtorName = "National Fleet", Amount = 5000 } };
        var expenses = new List<Expense> { new() { Description = "Maintenance", Amount = 3000 } };
        var testing = new List<TestingEntry> { new() { FuelType = "MS-I", Amount = 2000 } };

        var saveResult = await dsmService.SaveCompleteEntryAsync(
            date, "A", "Supervisor Multi", 1,
            nozzleReadings, payment, debits, testing,
            expenses, cash,
            connectedPumpIds: new[] { 2, 3, 4 });

        Assert.True(saveResult.Success, $"Save failed: {saveResult.Error}");

        // Load all saved entries from DB with includes
        var entries = await db.DsmEntries
            .Include(e => e.Shift)
            .Include(e => e.NozzleReadings)
            .Include(e => e.PaymentCollection)
            .Include(e => e.CashDenominations)
            .Include(e => e.DebitEntries)
            .Include(e => e.Expenses)
            .Include(e => e.TestingEntries)
            .Include(e => e.PersonalDebtors)
            .Include(e => e.KhandharePetroleumEntries)
            .Include(e => e.QrPayments)
            .Where(e => e.ShiftId == shift.ShiftId)
            .ToListAsync();

        Assert.Equal(4, entries.Count);

        // A. Verify ShiftAggregationService
        var summaryRows = shiftAggService.BuildDsmSummaryRows(entries);
        Assert.Single(summaryRows); // 1 combined row for the 4-pump group

        var row = summaryRows[0];
        Assert.Equal("Pump 1 & 2 & 3 & 4", row.PumpLabel);
        Assert.Equal("1 & 2 & 3 & 4", row.PumpNoDisplay);
        Assert.Equal(30000, row.GrossSales);
        Assert.Equal(10000, row.CashDeposit);
        Assert.Equal(0, row.CashInHand);
        Assert.Equal(10000, row.PhonePeMorning);
        Assert.Equal(5000, row.Debit);
        Assert.Equal(3000, row.Expenses);
        Assert.Equal(2000, row.Testing);
        Assert.Equal(28000, row.NetGrossSales);
        Assert.Equal(28000, row.TotalCollection);
        Assert.Equal(0, row.Difference);

        // B. Verify ReportService
        var report = reportService.CalculateShiftReport(
            date, "A", entries,
            new List<Expense>(), new List<ShiftOtherCash>(), new List<CreditorRepayment>(),
            hsdRate: 75, msIRate: 75, msIIRate: 75, cngRate: 85,
            new BusinessDayTidSheet(), new BusinessDayTidSheet(), "Mitali Station");

        Assert.NotNull(report);
        Assert.Single(report.DsmSummaryRows);
        Assert.Equal(30000, report.TotalFuelAmount);
        Assert.Equal(30000, report.GrandTotalSaleAmount);
        Assert.Equal(10000, report.Cash1.GrandTotal);
        Assert.Equal(5000, report.CreditorsTotal);
        Assert.Equal(3000, report.ExpensesTotal);
        Assert.Equal(30000, report.DsmSummaryRows[0].GrossSales);
        Assert.Equal(28000, report.DsmSummaryRows[0].NetGrossSales);
        Assert.Equal(28000, report.DsmSummaryRows[0].TotalCollection);
        Assert.Equal(0, report.DsmSummaryRows[0].Difference);
    }

    [Fact]
    public void Test_16_RequirementJ_LegacyConnectedPumpId_WithoutJson_ResolvesToSingleSlave()
    {
        // Legacy: ConnectedPumpId = 2, JSON is null
        var resolved = PumpConnectionConfiguration.ResolveEffectiveConnectedPumpIds(legacyConnectedPumpId: 2, connectedPumpIdsJson: null);
        Assert.Equal(new[] { 2 }, resolved);

        // Both null
        var empty = PumpConnectionConfiguration.ResolveEffectiveConnectedPumpIds(legacyConnectedPumpId: null, connectedPumpIdsJson: null);
        Assert.Empty(empty);

        // ConnectedPumpId <= 0 ignored
        var zero = PumpConnectionConfiguration.ResolveEffectiveConnectedPumpIds(legacyConnectedPumpId: 0, connectedPumpIdsJson: null);
        Assert.Empty(zero);
    }

    [Fact]
    public void Test_17_RequirementK_DuplicateIds_HandledGracefully()
    {
        // Duplicate IDs in JSON string: "[2,2,3]" -> [2, 3]
        var resolved = PumpConnectionConfiguration.ResolveEffectiveConnectedPumpIds(legacyConnectedPumpId: null, connectedPumpIdsJson: "[2,2,3]");
        Assert.Equal(new[] { 2, 3 }, resolved);

        // Duplicates across legacy ID and JSON
        var resolvedMulti = PumpConnectionConfiguration.ResolveEffectiveConnectedPumpIds(legacyConnectedPumpId: 2, connectedPumpIdsJson: "[2, 2, 3, 3, 4]");
        Assert.Equal(new[] { 2, 3, 4 }, resolvedMulti);
    }

    [Fact]
    public void Test_18_RequirementL_InvalidIds_HandledGracefully()
    {
        // Invalid IDs in JSON string: "[2, 0, -1, 3]" -> [2, 3]
        var resolved = PumpConnectionConfiguration.ResolveEffectiveConnectedPumpIds(legacyConnectedPumpId: null, connectedPumpIdsJson: "[2, 0, -1, 3]");
        Assert.Equal(new[] { 2, 3 }, resolved);

        // Malformed JSON falls back gracefully to legacyConnectedPumpId
        var malformed = PumpConnectionConfiguration.ResolveEffectiveConnectedPumpIds(legacyConnectedPumpId: 2, connectedPumpIdsJson: "{ not valid json array }");
        Assert.Equal(new[] { 2 }, malformed);

        // Non-array JSON element falls back gracefully
        var nonArray = PumpConnectionConfiguration.ResolveEffectiveConnectedPumpIds(legacyConnectedPumpId: 3, connectedPumpIdsJson: "123");
        Assert.Equal(new[] { 3 }, nonArray);
    }

    [Fact]
    public void Test_19_DefensiveFiltering_ExcludesPrimaryPumpFromSlaves()
    {
        // 1. DsmEntry
        var entry = new DsmEntry
        {
            PumpId = 1,
            ConnectedPumpId = 1, // accidentally self-referencing
            ConnectedPumpIdsJson = "[1, 2, 3]"
        };
        var effective = entry.GetEffectiveConnectedPumpIds();
        Assert.DoesNotContain(1, effective);
        Assert.Equal(new[] { 2, 3 }, effective);

        // 2. DsmPumpAssignment
        var assignment = new DsmPumpAssignment
        {
            PumpId = 2,
            ConnectedPumpId = 2,
            ConnectedPumpIdsJson = "[2, 3, 4]"
        };
        var assignmentEffective = assignment.GetEffectiveConnectedPumpIds();
        Assert.DoesNotContain(2, assignmentEffective);
        Assert.Equal(new[] { 3, 4 }, assignmentEffective);

        // 3. DTO
        var dto = new FuelPro.Core.DTOs.DsmEntrySummaryDto
        {
            PumpId = 3,
            ConnectedPumpId = 3,
            ConnectedPumpIdsJson = "[3, 4]"
        };
        Assert.Equal(new[] { 4 }, dto.GetEffectiveConnectedPumpIds());
    }

    [Fact]
    public void Test_20_DsmPumpAssignment_DisplayPumps_FormatsCorrectly()
    {
        // Single pump
        var p1 = new DsmPumpAssignment { PumpId = 1 };
        Assert.Equal("Pump 1", p1.DisplayPumps);

        // 2 pumps (legacy scalar)
        var p2Legacy = new DsmPumpAssignment { PumpId = 1, ConnectedPumpId = 2 };
        Assert.Equal("Pump 1 + Pump 2", p2Legacy.DisplayPumps);

        // 3 pumps (JSON)
        var p3 = new DsmPumpAssignment { PumpId = 1, ConnectedPumpId = 2, ConnectedPumpIdsJson = "[2, 3]" };
        Assert.Equal("Pump 1 + Pump 2 + Pump 3", p3.DisplayPumps);

        // 4 pumps (JSON)
        var p4 = new DsmPumpAssignment { PumpId = 1, ConnectedPumpId = 2, ConnectedPumpIdsJson = "[2, 3, 4]" };
        Assert.Equal("Pump 1 + Pump 2 + Pump 3 + Pump 4", p4.DisplayPumps);
    }

    [Fact]
    public void Test_21_ApprovalQueue_PendingSubmission_MultiPumpMetadataFormatting()
    {
        // Legacy single connected pump
        var subLegacy = new DsmPendingSubmission
        {
            DsmName = "Ramesh",
            PumpId = 1,
            ShiftType = "A",
            MetadataJson = "{\"connectedPumpId\": 2}"
        };
        Assert.Equal(2, subLegacy.ConnectedPumpId);
        Assert.Equal(new[] { 2 }, subLegacy.ConnectedPumpIds);
        Assert.Equal("Ramesh - Pump 1 + Pump 2 (Connected) - Shift A", subLegacy.TitleDisplay);

        // Multi-pump JSON array
        var subMulti = new DsmPendingSubmission
        {
            DsmName = "Suresh",
            PumpId = 1,
            ShiftType = "B",
            MetadataJson = "{\"connectedPumpIds\": [2, 3, 4], \"connectedPumpId\": 2}"
        };
        Assert.Equal(2, subMulti.ConnectedPumpId);
        Assert.Equal(new[] { 2, 3, 4 }, subMulti.ConnectedPumpIds);
        Assert.Equal("Suresh - Pump 1 + Pump 2 + Pump 3 + Pump 4 (Connected) - Shift B", subMulti.TitleDisplay);

        // Single pump without connected pump
        var subSingle = new DsmPendingSubmission
        {
            DsmName = "Mahesh",
            PumpId = 1,
            ShiftType = "A",
            MetadataJson = "{}"
        };
        Assert.Null(subSingle.ConnectedPumpId);
        Assert.Empty(subSingle.ConnectedPumpIds);
        Assert.Equal("Mahesh - Pump 1 - Shift A", subSingle.TitleDisplay);

        // DsmApprovedSubmissionDto with 4 pumps
        var approvedDto = new DsmApprovedSubmissionDto
        {
            DsmName = "Ganesh",
            PumpId = 1,
            ShiftType = "A",
            MetadataJson = "{\"connectedPumpIds\": [2, 3, 4]}"
        };
        Assert.Equal(new[] { 2, 3, 4 }, approvedDto.ConnectedPumpIds);
        Assert.Equal("Ganesh - Pump 1 + Pump 2 + Pump 3 + Pump 4 (Connected) - Shift A", approvedDto.TitleDisplay);
    }

    [Fact]
    public async Task Test_22_EditMode_GrossSalesFallback_DoesNotDoubleCountSlaves()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var date = DateTime.Today.AddDays(-20);

        // 1. Create a 3-pump group: Primary Pump 1, Slaves Pump 2 and Pump 3
        var nozzles = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 200, Rate = 100 }, // P1: 10,000
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 200, Rate = 100 }, // P2: 10,000
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 100, ClosingReading = 200, Rate = 100 }  // P3: 10,000
        };

        var payment = new PaymentCollection { PhonePeMorning = 15000, CashDeposit = 15000 };
        var cash = new List<CashDenomination> { new() { CashType = "Cash1", TotalAmount = 15000 } };
        var saveResult = await dsmService.SaveCompleteEntryWithContextAsync(
            context, date, "A", "Operator A", 1, nozzles, payment,
            new List<DebitEntry>(), new List<TestingEntry>(), new List<Expense>(), cash,
            connectedPumpId: 2, connectedPumpIds: new[] { 2, 3 });

        Assert.True(saveResult.Success);
        var primaryEntry = saveResult.Data!;
        Assert.Equal(30000, primaryEntry.GrossSales); // Primary has aggregate gross sales

        // 2. Load into DsmEntryViewModel for Edit
        var vm = scope.ServiceProvider.GetRequiredService<DsmEntryViewModel>();
        await vm.HydrateFromEntryAsync(primaryEntry);

        // Verify that ViewModel loaded gross sales directly without re-adding slave sales
        // If it had a double-counting bug, it would be 30,000 + 10,000 + 10,000 = 50,000
        Assert.Equal(30000, vm.GrossSales);
    }

    [Fact]
    public async Task Test_23_Phase5_DeterministicFinancialScenario_FourPumps_DeliberateMismatch_NoInflation()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var shiftAggService = scope.ServiceProvider.GetRequiredService<IShiftAggregationService>();
        var reportService = scope.ServiceProvider.GetRequiredService<IReportService>();
        var date = new DateTime(2026, 9, 10);

        // 1. Deterministic Nozzle Sales:
        // Pump 1 = ₹10,000 (100L @ ₹100)
        // Pump 2 = ₹8,000 (80L @ ₹100)
        // Pump 3 = ₹7,000 (70L @ ₹100)
        // Pump 4 = ₹5,000 (50L @ ₹100)
        // Expected group GrossSales = ₹30,000
        var nozzles = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 100, Rate = 100 }, // P1: 10,000
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 80, Rate = 100 },  // P2: 8,000
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 70, Rate = 100 },  // P3: 7,000
            new() { NozzleNumber = 7, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 50, Rate = 100 }   // P4: 5,000
        };

        // 2. Deterministic Collections with deliberate mismatch:
        // CashDeposit = ₹15,000
        // PhonePeMorning = ₹10,000
        // CreditCardMorning = ₹5,500
        // Total Collections = ₹30,500
        // Expected Mismatch = +₹500 (Excess)
        var payment = new PaymentCollection
        {
            CashDeposit = 15000,
            PhonePeMorning = 10000,
            CreditCardMorning = 5500
        };
        var cash = new List<CashDenomination>
        {
            new() { CashType = "Cash1", TotalAmount = 15000 }
        };

        var saveResult = await dsmService.SaveCompleteEntryWithContextAsync(
            context, date, "A", "Operator P5", 1, nozzles, payment,
            new List<DebitEntry>(), new List<TestingEntry>(), new List<Expense>(), cash,
            connectedPumpId: 2, connectedPumpIds: new[] { 2, 3, 4 });

        Assert.True(saveResult.Success, $"Save failed: {saveResult.Error}");
        var primaryEntry = saveResult.Data!;

        // 3. Database verification
        var allEntries = await context.DsmEntries
            .Include(e => e.NozzleReadings)
            .Include(e => e.PaymentCollection)
            .Where(e => e.ShiftId == primaryEntry.ShiftId)
            .OrderBy(e => e.PumpId)
            .ToListAsync();

        Assert.Equal(4, allEntries.Count); // 1 primary + 3 slaves

        // Verify Primary DsmEntry
        var dbPrimary = allEntries.First(e => e.PumpId == 1);
        Assert.Equal(30000, dbPrimary.GrossSales);
        Assert.Equal(30500, dbPrimary.TotalCollection);
        Assert.Equal(500, dbPrimary.Mismatch); // +500 Excess

        // Verify Slaves
        var slave2 = allEntries.First(e => e.PumpId == 2);
        Assert.Equal(8000, slave2.GrossSales);
        Assert.Equal(0, slave2.TotalCollection);
        Assert.Equal(dbPrimary.DsmEntryId, slave2.ReconciledToPumpId);

        var slave3 = allEntries.First(e => e.PumpId == 3);
        Assert.Equal(7000, slave3.GrossSales);
        Assert.Equal(0, slave3.TotalCollection);
        Assert.Equal(dbPrimary.DsmEntryId, slave3.ReconciledToPumpId);

        var slave4 = allEntries.First(e => e.PumpId == 4);
        Assert.Equal(5000, slave4.GrossSales);
        Assert.Equal(0, slave4.TotalCollection);
        Assert.Equal(dbPrimary.DsmEntryId, slave4.ReconciledToPumpId);

        // 4. ShiftAggregationService Verification
        var summaryRows = shiftAggService.BuildDsmSummaryRows(allEntries);
        Assert.Single(summaryRows); // 1 unified row for group
        var row = summaryRows[0];

        // SPECIFIC VERIFICATION: GrossSales must be exactly 30,000 and NOT inflated by adding slaves (30k + 20k = 50k)
        Assert.Equal(30000, row.GrossSales);
        Assert.Equal(30500, row.TotalCollection);
        Assert.Equal(500, row.Difference); // +500 Mismatch
        Assert.Equal(15000, row.CashDeposit);
        Assert.Equal(10000, row.PhonePeMorning);
        Assert.Equal(5500, row.CreditCardMorning);
        Assert.Equal(5500, row.CreditCardTotal);

        // Verify individual nozzle sum equals group total
        var nozzleSalesSum = allEntries.SelectMany(e => e.NozzleReadings).Sum(n => n.Amount);
        Assert.Equal(30000, nozzleSalesSum);
        Assert.Equal(row.GrossSales, nozzleSalesSum);

        // 5. ReportService Verification
        var report = reportService.CalculateShiftReport(
            date, "A", allEntries,
            new List<Expense>(), new List<ShiftOtherCash>(), new List<CreditorRepayment>(),
            hsdRate: 100, msIRate: 100, msIIRate: 100, cngRate: 85,
            new BusinessDayTidSheet(), new BusinessDayTidSheet(), "Mitali Service Station");

        Assert.NotNull(report);
        Assert.Single(report.DsmSummaryRows);
        Assert.Equal(30000, report.TotalFuelAmount);
        Assert.Equal(30000, report.GrandTotalSaleAmount);
        Assert.Equal(30000, report.DsmSummaryRows[0].GrossSales);
        Assert.Equal(30500, report.DsmSummaryRows[0].TotalCollection);
        Assert.Equal(500, report.DsmSummaryRows[0].Difference);

        // 6. DsmEntryViewModel Edit Mode Verification
        var vm = scope.ServiceProvider.GetRequiredService<DsmEntryViewModel>();
        await vm.HydrateFromEntryAsync(dbPrimary);

        Assert.Equal(30000, vm.GrossSales); // Not inflated to 50,000
    }

    [Fact]
    public async Task Test_24_Phase5_EditWorkflow_ContractionAndExpansion_OrphanCleanup()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        var dsmService = scope.ServiceProvider.GetRequiredService<DsmEntryService>();
        var date = new DateTime(2026, 9, 11);

        // Stage 1: Create 4-pump group [1, 2, 3, 4]
        var nozzles4 = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 100, Rate = 100 }, // 10,000
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 80, Rate = 100 },  // 8,000
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 70, Rate = 100 },  // 7,000
            new() { NozzleNumber = 7, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 50, Rate = 100 }   // 5,000
        };
        var payment4 = new PaymentCollection { CashDeposit = 30000 };
        var cash4 = new List<CashDenomination> { new() { CashType = "Cash1", TotalAmount = 30000 } };

        var createResult = await dsmService.SaveCompleteEntryWithContextAsync(
            context, date, "A", "Operator P5-Edit", 1, nozzles4, payment4,
            new List<DebitEntry>(), new List<TestingEntry>(), new List<Expense>(), cash4,
            connectedPumpId: 2, connectedPumpIds: new[] { 2, 3, 4 });

        Assert.True(createResult.Success);
        var primaryId = createResult.Data!.DsmEntryId;
        var shiftId = createResult.Data!.ShiftId;

        var entriesAfterCreate = await context.DsmEntries.Where(e => e.ShiftId == shiftId).ToListAsync();
        Assert.Equal(4, entriesAfterCreate.Count); // 1 primary + 3 slaves

        // Stage 2: Contract from 4 pumps [1, 2, 3, 4] to 2 pumps [1, 2]
        var nozzlesContracted = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 100, Rate = 100 }, // 10,000
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 80, Rate = 100 }   // 8,000
        };
        var paymentContracted = new PaymentCollection { CashDeposit = 18000 };
        var cashContracted = new List<CashDenomination> { new() { CashType = "Cash1", TotalAmount = 18000 } };

        var contractResult = await dsmService.SaveCompleteEntryWithContextAsync(
            context, date, "A", "Operator P5-Edit", 1, nozzlesContracted, paymentContracted,
            new List<DebitEntry>(), new List<TestingEntry>(), new List<Expense>(), cashContracted,
            connectedPumpId: 2, existingEntryId: primaryId, connectedPumpIds: new[] { 2 });

        Assert.True(contractResult.Success);

        // Verify that orphaned slaves (Pump 3 and Pump 4) were deleted
        var entriesAfterContract = await context.DsmEntries.Where(e => e.ShiftId == shiftId).OrderBy(e => e.PumpId).ToListAsync();
        Assert.Equal(2, entriesAfterContract.Count); // Only Primary (Pump 1) and Slave (Pump 2)
        Assert.Equal(1, entriesAfterContract[0].PumpId);
        Assert.Equal(18000, entriesAfterContract[0].GrossSales);
        Assert.Equal(2, entriesAfterContract[1].PumpId);
        Assert.Equal(8000, entriesAfterContract[1].GrossSales);

        // Stage 3: Expand from 2 pumps [1, 2] to 3 pumps [1, 2, 3]
        var nozzlesExpanded = new List<NozzleReading>
        {
            new() { NozzleNumber = 1, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 100, Rate = 100 }, // 10,000
            new() { NozzleNumber = 3, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 80, Rate = 100 },  // 8,000
            new() { NozzleNumber = 5, FuelType = "MS-I", OpeningReading = 0, ClosingReading = 70, Rate = 100 }   // 7,000
        };
        var paymentExpanded = new PaymentCollection { CashDeposit = 25000 };
        var cashExpanded = new List<CashDenomination> { new() { CashType = "Cash1", TotalAmount = 25000 } };

        var expandResult = await dsmService.SaveCompleteEntryWithContextAsync(
            context, date, "A", "Operator P5-Edit", 1, nozzlesExpanded, paymentExpanded,
            new List<DebitEntry>(), new List<TestingEntry>(), new List<Expense>(), cashExpanded,
            connectedPumpId: 2, existingEntryId: primaryId, connectedPumpIds: new[] { 2, 3 });

        Assert.True(expandResult.Success);

        var entriesAfterExpand = await context.DsmEntries.Where(e => e.ShiftId == shiftId).OrderBy(e => e.PumpId).ToListAsync();
        Assert.Equal(3, entriesAfterExpand.Count); // Primary (Pump 1) + Slave (Pump 2) + newly created Slave (Pump 3)
        Assert.Equal(1, entriesAfterExpand[0].PumpId);
        Assert.Equal(25000, entriesAfterExpand[0].GrossSales);
        Assert.Equal(2, entriesAfterExpand[1].PumpId);
        Assert.Equal(3, entriesAfterExpand[2].PumpId);
    }
}
