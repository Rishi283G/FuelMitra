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

public class DsmSaveButtonStateRegressionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly IServiceProvider _serviceProvider;

    public DsmSaveButtonStateRegressionTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _tempDir = Path.Combine(Path.GetTempPath(), "FuelPro_DsmSaveTests_" + Guid.NewGuid().ToString("N"));
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
        services.AddTransient<IShiftCycleService, ShiftCycleService>();
        services.AddTransient<DsmEntryViewModel>();

        _serviceProvider = services.BuildServiceProvider();

        typeof(App).GetProperty("Services")?.SetValue(null, _serviceProvider);
        typeof(App).GetProperty("DbPath")?.SetValue(null, _dbPath);

        var testMappings = new List<PumpMapping>
        {
            new() { PumpMappingId = 1, PumpId = 1, NozzleNumber = 2, FuelType = "HSD", IsActive = true },
            new() { PumpMappingId = 2, PumpId = 1, NozzleNumber = 4, FuelType = "SPEED", IsActive = true },
            new() { PumpMappingId = 3, PumpId = 1, NozzleNumber = 6, FuelType = "MS-I", IsActive = true }
        };
        PumpConfiguration.InitializeFromDb(testMappings);

        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();
        context.PumpMappings.AddRange(testMappings);
        context.SaveChanges();
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

    private void SeedShift(DateTime date, string shiftType, out Shift shift)
    {
        using var context = _serviceProvider.GetRequiredService<FuelProDbContext>();
        shift = new Shift
        {
            ShiftDate = date.Date,
            ShiftType = shiftType,
            IsLocked = false,
            CreatedAt = DateTime.Now
        };
        context.Shifts.Add(shift);
        context.SaveChanges();
    }

    [Fact]
    public async Task Test1_NormalNewDsmEntrySave_SucceedsAndLeavesSaveDisabled()
    {
        var testDate = new DateTime(2026, 11, 15);
        SeedShift(testDate, "A", out var shift);

        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        vm.SelectedDate = testDate;
        vm.SelectedShift = "A";
        vm.DsmName = "RAMESH";

        await vm.LoadSuggestionsAsync();
        vm.SelectedPump = vm.PumpOptions.First(p => p.PumpId == 1);

        foreach (var nr in vm.NozzleReadings)
        {
            nr.OpeningReading = 1000.0;
            nr.ClosingReading = 1050.0;
            nr.Rate = 95.0;
        }

        vm.Cash1.Denom500 = 10;
        vm.Cash1.TotalAmount = 5000;

        // Fresh unsaved entry should have Save enabled
        Assert.False(vm.IsSaved);
        Assert.False(vm.IsSaving);
        Assert.False(vm.IsSaveOutcomeUncertain);
        Assert.True(vm.CanSaveCurrentEntry);

        // Execute Save
        await vm.SaveEntryCommand.ExecuteAsync(null);

        // After successful save, Save must be disabled
        Assert.True(vm.IsSaved);
        Assert.False(vm.IsDirty);
        Assert.False(vm.IsSaving);
        Assert.False(vm.IsSaveOutcomeUncertain);
        Assert.False(vm.CanSaveCurrentEntry);
    }

    [Fact]
    public async Task Test2_RecalculateAll_DoesNotReEnableSave_WhenSnapshotUnchanged()
    {
        var testDate = new DateTime(2026, 11, 15);
        SeedShift(testDate, "A", out var shift);

        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        vm.SelectedDate = testDate;
        vm.SelectedShift = "A";
        vm.DsmName = "RAMESH";

        await vm.LoadSuggestionsAsync();
        vm.SelectedPump = vm.PumpOptions.First(p => p.PumpId == 1);

        foreach (var nr in vm.NozzleReadings)
        {
            nr.OpeningReading = 1000.0;
            nr.ClosingReading = 1050.0;
            nr.Rate = 95.0;
        }
        vm.Cash1.Denom500 = 10;

        await vm.SaveEntryCommand.ExecuteAsync(null);

        Assert.True(vm.IsSaved);
        Assert.False(vm.CanSaveCurrentEntry);

        // Call RecalculateAll multiple times
        vm.RecalculateAll();
        vm.RecalculateAll();

        // Must remain disabled and not marked dirty
        Assert.True(vm.IsSaved);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanSaveCurrentEntry);
    }

    [Fact]
    public async Task Test3_MeaningfulEdit_EnablesSave_AndRevertingDisablesSave()
    {
        var testDate = new DateTime(2026, 11, 15);
        SeedShift(testDate, "A", out var shift);

        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        vm.SelectedDate = testDate;
        vm.SelectedShift = "A";
        vm.DsmName = "RAMESH";

        await vm.LoadSuggestionsAsync();
        vm.SelectedPump = vm.PumpOptions.First(p => p.PumpId == 1);

        foreach (var nr in vm.NozzleReadings)
        {
            nr.OpeningReading = 1000.0;
            nr.ClosingReading = 1050.0;
            nr.Rate = 95.0;
        }

        await vm.SaveEntryCommand.ExecuteAsync(null);
        Assert.False(vm.CanSaveCurrentEntry);

        // Edit cash denomination
        var originalDenom500 = vm.Cash1.Denom500 ?? 0;
        vm.Cash1.Denom500 = originalDenom500 + 5;

        // Form is now dirty, Save must be enabled
        Assert.True(vm.IsDirty);
        Assert.True(vm.CanSaveCurrentEntry);

        // Revert back to original value
        vm.Cash1.Denom500 = originalDenom500 == 0 ? (int?)null : originalDenom500;

        // Form snapshot matches saved snapshot, Save must be disabled
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanSaveCurrentEntry);
    }

    [Fact]
    public async Task Test4_ExistingDsmEntryHydration_StartsDisabled_AndEditsEnableSave()
    {
        var testDate = new DateTime(2026, 11, 15);
        SeedShift(testDate, "A", out var shift);

        using (var context = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var entry = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                DsmName = "SURESH",
                PumpId = 1,
                GrossSales = 5000,
                TotalCollection = 5000,
                CreatedAt = DateTime.Now
            };
            context.DsmEntries.Add(entry);
            context.SaveChanges();

            var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
            await vm.HydrateFromEntryAsync(entry);

            // Directly after hydration, Save must be disabled
            Assert.True(vm.IsSaved);
            Assert.False(vm.IsDirty);
            Assert.False(vm.CanSaveCurrentEntry);

            // Adding an expense row should enable Save
            vm.AddExpenseCommand.Execute(null);
            Assert.True(vm.IsDirty);
            Assert.True(vm.CanSaveCurrentEntry);

            // Removing the expense row should restore clean state
            vm.RemoveExpenseCommand.Execute(vm.Expenses.Last());
            Assert.False(vm.IsDirty);
            Assert.False(vm.CanSaveCurrentEntry);
        }
    }

    [Fact]
    public async Task Test5_CollectionEdits_CorrectlyDetected()
    {
        var testDate = new DateTime(2026, 11, 15);
        SeedShift(testDate, "A", out var shift);

        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        vm.SelectedDate = testDate;
        vm.SelectedShift = "A";
        vm.DsmName = "KARTIK";

        await vm.LoadSuggestionsAsync();
        vm.SelectedPump = vm.PumpOptions.First(p => p.PumpId == 1);

        foreach (var nr in vm.NozzleReadings)
        {
            nr.OpeningReading = 500.0;
            nr.ClosingReading = 550.0;
            nr.Rate = 95.0;
        }

        await vm.SaveEntryCommand.ExecuteAsync(null);
        Assert.False(vm.CanSaveCurrentEntry);

        // Add Debit
        vm.AddDebitCommand.Execute(null);
        Assert.True(vm.IsDirty);
        Assert.True(vm.CanSaveCurrentEntry);

        // Remove Debit
        vm.RemoveDebitCommand.Execute(vm.Debits.Last());
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanSaveCurrentEntry);

        // Add QR Payment
        vm.AddQrPaymentCommand.Execute(null);
        Assert.True(vm.IsDirty);
        Assert.True(vm.CanSaveCurrentEntry);

        // Remove QR Payment
        vm.RemoveQrPaymentCommand.Execute(vm.QrPayments.Last());
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanSaveCurrentEntry);
    }

    [Fact]
    public void Test6_ClearForm_ResetsDirtyAndSnapshot()
    {
        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        vm.DsmName = "TEST USER";
        vm.IsSaved = true;
        vm.IsDirty = true;
        vm.IsSaveOutcomeUncertain = true;

        vm.ClearForm();

        Assert.False(vm.IsSaved);
        Assert.False(vm.IsDirty);
        Assert.False(vm.IsSaveOutcomeUncertain);
        Assert.Null(vm.EditingEntryId);
        Assert.Equal(string.Empty, vm.DsmName);
    }

    [Fact]
    public void Test7_CashDenomRow_RevertAllDenomsToNull_ResetsTotalToZero()
    {
        var cash1 = new CashDenomRow { CashType = "Cash1" };
        cash1.Denom500 = 4;
        Assert.Equal(2000.0, cash1.TotalAmount);

        cash1.Denom500 = null;
        Assert.Equal(0.0, cash1.TotalAmount);

        var cash2 = new CashDenomRow { CashType = "Cash2" };
        cash2.Denom200 = 3;
        cash2.Denom100 = 1;
        Assert.Equal(700.0, cash2.TotalAmount);

        cash2.Denom200 = null;
        cash2.Denom100 = null;
        Assert.Equal(0.0, cash2.TotalAmount);
    }

    [Fact]
    public async Task Test8_CashDenomRow_EditRevertDirtyState_AndDenominationRestrictionsPreserved()
    {
        var testDate = new DateTime(2026, 11, 15);
        SeedShift(testDate, "A", out _);

        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();
        vm.SelectedDate = testDate;
        vm.SelectedShift = "A";
        vm.DsmName = "DENOM TEST";

        await vm.LoadSuggestionsAsync();
        vm.SelectedPump = vm.PumpOptions.First(p => p.PumpId == 1);
        foreach (var nr in vm.NozzleReadings)
        {
            nr.OpeningReading = 1000.0;
            nr.ClosingReading = 1010.0;
            nr.Rate = 100.0;
        }

        await vm.SaveEntryCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanSaveCurrentEntry);

        // Cash1/Cash2 restrict to 500/200/100 only; small denominations do not change Cash1/Cash2 total
        vm.Cash1.Denom500 = 2; // 1000
        vm.Cash2.Denom200 = 3; // 600
        vm.Cash3.Denom50 = 4;  // 200 (Cash3 supports all denominations)
        Assert.Equal(1000.0, vm.Cash1.TotalAmount);
        Assert.Equal(600.0, vm.Cash2.TotalAmount);
        Assert.Equal(200.0, vm.Cash3.TotalAmount);
        Assert.True(vm.IsDirty);
        Assert.True(vm.CanSaveCurrentEntry);

        // Cash1/Cash2 ignore 50/20/10/Coins per established restrictions
        vm.Cash1.Denom50 = 5;
        vm.Cash2.Coins = 99;
        Assert.Equal(1000.0, vm.Cash1.TotalAmount);
        Assert.Equal(600.0, vm.Cash2.TotalAmount);

        // Revert all fields back to null -> totals must reset to 0 and dirty state must return clean
        vm.Cash1.Denom500 = null;
        vm.Cash1.Denom50 = null;
        vm.Cash2.Denom200 = null;
        vm.Cash2.Coins = null;
        vm.Cash3.Denom50 = null;

        Assert.Equal(0.0, vm.Cash1.TotalAmount);
        Assert.Equal(0.0, vm.Cash2.TotalAmount);
        Assert.Equal(0.0, vm.Cash3.TotalAmount);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanSaveCurrentEntry);
    }
}
