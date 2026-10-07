using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.Sync;
using FuelPro.UI;
using FuelPro.UI.Printing;
using FuelPro.UI.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FuelPro.Tests;

public class Phase217UiConsistencyAndShiftCTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly IShiftCycleService _shiftCycleService;

    public Phase217UiConsistencyAndShiftCTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        if (System.Windows.Application.Current == null)
        {
            new System.Windows.Application();
        }

        _dbPath = Path.Combine(Path.GetTempPath(), $"fuelpro_test_p217_{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();

        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"), ServiceLifetime.Transient);

        services.AddLogging();
        services.AddSingleton<IShiftCycleService, ShiftCycleService>();
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
        services.AddTransient<IOpeningBalanceRepository, OpeningBalanceRepository>();

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
        services.AddTransient<IAgsImportService, AgsImportService>();
        services.AddTransient<IAgsDailyAggregationService, AgsDailyAggregationService>();
        services.AddTransient<AgsImportValidator>();

        services.AddTransient<PrintService>();
        services.AddTransient<ExcelExportService>();
        services.AddTransient<IAuditLogService, AuditLogService>();
        services.AddTransient<IDayLockService, DayLockService>();
        services.AddTransient<IFeatureToggleService, FeatureToggleService>();
        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddTransient<IStationConfigurationService, StationConfigurationService>();

        services.AddSingleton<SyncConfigService>();
        services.AddTransient<DraftService>();
        services.AddTransient<DsmAuthAdminService>();
        services.AddTransient<SupabaseDsmService, FakeSupabaseDsmService>();

        services.AddTransient<DsmEntryViewModel>();
        services.AddTransient<DebtorManagementViewModel>();
        services.AddTransient<DsmPersonalDebtorViewModel>();
        services.AddTransient<CardSettlementViewModel>();
        services.AddTransient<FinalCalculationViewModel>();
        services.AddTransient<DsmManagementViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<AgsImportViewModel>();

        _serviceProvider = services.BuildServiceProvider();
        _shiftCycleService = _serviceProvider.GetRequiredService<IShiftCycleService>();

        typeof(App).GetProperty("Services")?.SetValue(null, _serviceProvider);
        typeof(App).GetProperty("DbPath")?.SetValue(null, _dbPath);

        using var initContext = _serviceProvider.GetRequiredService<FuelProDbContext>();
        initContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    [Fact]
    public void Test01_ShiftCycleService_GetDisplayShifts_Returns_A_B_C()
    {
        var displayShifts = _shiftCycleService.GetDisplayShifts();

        Assert.Equal(3, displayShifts.Count);
        Assert.Equal("A", displayShifts[0]);
        Assert.Equal("B", displayShifts[1]);
        Assert.Equal("C", displayShifts[2]);
    }

    [Fact]
    public void Test02_ShiftCycleService_OperationalContinuity_Preserved_A_B_C()
    {
        var date = new DateTime(2026, 10, 3);
        var operationalShifts = _shiftCycleService.GetShiftsForDate(date);

        // Traversal order is A, B, C
        Assert.Equal(3, operationalShifts.Count);
        Assert.Equal("A", operationalShifts[0]);
        Assert.Equal("B", operationalShifts[1]);
        Assert.Equal("C", operationalShifts[2]);

        // Exact Predecessors
        var (prevADate, prevAShift) = _shiftCycleService.GetPreviousShift(date, "A");
        Assert.Equal(date.AddDays(-1).Date, prevADate.Date);
        Assert.Equal("C", prevAShift);

        var (prevBDate, prevBShift) = _shiftCycleService.GetPreviousShift(date, "B");
        Assert.Equal(date.Date, prevBDate.Date);
        Assert.Equal("A", prevBShift);

        var (prevCDate, prevCShift) = _shiftCycleService.GetPreviousShift(date, "C");
        Assert.Equal(date.Date, prevCDate.Date);
        Assert.Equal("B", prevCShift);

        // Exact Successors
        var (nextADate, nextAShift) = _shiftCycleService.GetNextShift(date, "A");
        Assert.Equal(date.Date, nextADate.Date);
        Assert.Equal("B", nextAShift);

        var (nextBDate, nextBShift) = _shiftCycleService.GetNextShift(date, "B");
        Assert.Equal(date.Date, nextBDate.Date);
        Assert.Equal("C", nextBShift);

        var (nextCDate, nextCShift) = _shiftCycleService.GetNextShift(date, "C");
        Assert.Equal(date.AddDays(1).Date, nextCDate.Date);
        Assert.Equal("A", nextCShift);
    }

    [Theory]
    [InlineData("A", "A")]
    [InlineData("Shift A", "A")]
    [InlineData("SHIFT A", "A")]
    [InlineData("I", "A")]
    [InlineData("1", "A")]
    [InlineData("B", "B")]
    [InlineData("Shift B", "B")]
    [InlineData("SHIFT B", "B")]
    [InlineData("II", "B")]
    [InlineData("2", "B")]
    [InlineData("C", "C")]
    [InlineData("Shift C", "C")]
    [InlineData("SHIFT C", "C")]
    [InlineData("III", "C")]
    [InlineData("3", "C")]
    public void Test03_ShiftCycleService_NormalizeShiftType_AllCases(string input, string expected)
    {
        Assert.Equal(expected, _shiftCycleService.NormalizeShiftType(input));
    }

    [Fact]
    public void Test04_DsmEntryViewModel_ShiftOptions_DisplayOrder_Is_A_B_C_And_Default_Is_A()
    {
        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();

        Assert.Equal(new[] { "A", "B", "C" }, vm.ShiftOptions);
        Assert.Equal("A", vm.SelectedShift);
    }

    [Fact]
    public void Test05_DsmEntryViewModel_PaymentLabels_Clean_And_No_Night_Split_In_3Shift()
    {
        var vm = _serviceProvider.GetRequiredService<DsmEntryViewModel>();

        foreach (var shift in new[] { "A", "B", "C" })
        {
            vm.SelectedShift = shift;

            // In 3-shift mode, labels must be clean generic without scattering "Shift C"
            Assert.Equal("Amount (₹)", vm.MorningLabel);
            Assert.Equal("TID", vm.MorningTidLabel);
            Assert.Equal("Batch", vm.MorningBatchLabel);

            // In 3-shift mode, intra-shift split is never enabled
            Assert.False(vm.IsMorningNightSplitEnabled);
        }
    }

    [Fact]
    public void Test06_DebtorManagementViewModel_RepaymentShifts_Order_Is_A_B_C_And_Default_Is_A()
    {
        var vm = _serviceProvider.GetRequiredService<DebtorManagementViewModel>();

        Assert.Equal(3, vm.RepaymentShifts.Count);
        Assert.Equal("Shift A", vm.RepaymentShifts[0]);
        Assert.Equal("Shift B", vm.RepaymentShifts[1]);
        Assert.Equal("Shift C", vm.RepaymentShifts[2]);
        Assert.Equal("Shift A", vm.SelectedRepaymentShift);
    }

    [Fact]
    public async Task Test07_DebtorManagementViewModel_ShiftC_Persists_As_C()
    {
        using (var setupContext = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            setupContext.Creditors.Add(new Creditor { Name = "Test Creditor 1", IsActive = true });
            await setupContext.SaveChangesAsync();
        }

        var vm = _serviceProvider.GetRequiredService<DebtorManagementViewModel>();
        await vm.LoadDataCommand.ExecuteAsync(null);

        vm.SelectedDebtorName = "Test Creditor 1";
        vm.SelectedPaymentMode = "Cash";
        vm.RepaymentAmount = 1500;
        vm.SelectedRepaymentShift = "Shift C";

        await vm.AddRepaymentCommand.ExecuteAsync(null);

        using (var verifyContext = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var savedRepayment = await verifyContext.CreditorRepayments.FirstOrDefaultAsync(r => r.CreditorName == "Test Creditor 1");
            Assert.NotNull(savedRepayment);
            Assert.Equal("C", savedRepayment.ShiftNumber);
            Assert.Equal(1500, savedRepayment.Amount);
        }
    }

    [Fact]
    public void Test08_DsmPersonalDebtorViewModel_AvailableShifts_Includes_ShiftC_And_Order_Is_A_B_C()
    {
        var vm = _serviceProvider.GetRequiredService<DsmPersonalDebtorViewModel>();

        Assert.Equal(3, vm.AvailableShifts.Count);
        Assert.Equal("Shift A", vm.AvailableShifts[0]);
        Assert.Equal("Shift B", vm.AvailableShifts[1]);
        Assert.Equal("Shift C", vm.AvailableShifts[2]);
        Assert.Equal("Shift A", vm.SelectedShift);
    }

    [Fact]
    public async Task Test09_DsmPersonalDebtorViewModel_ShiftC_Repayment_Maps_To_ShiftC_Not_Falling_Back_To_A()
    {
        using (var setupContext = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var dsmUser = new DsmUser { FullName = "Ramesh Kumar", EmployeeCode = "DSM001", MobileNumber = "9876543210", IsActive = true };
            setupContext.DsmUsers.Add(dsmUser);
            await setupContext.SaveChangesAsync();
        }

        var vm = _serviceProvider.GetRequiredService<DsmPersonalDebtorViewModel>();
        await vm.LoadDataCommand.ExecuteAsync(null);

        vm.SelectedSummary = vm.DsmSummaries.FirstOrDefault(s => s.DsmName == "Ramesh Kumar");
        Assert.NotNull(vm.SelectedSummary);

        vm.RepaymentAmount = 500;
        vm.SelectedPaymentMode = "Cash";
        vm.SelectedShift = "Shift C";
        vm.RepaymentDate = new DateTime(2026, 10, 3);

        await vm.SavePersonalRepaymentCommand.ExecuteAsync(null);

        using (var verifyContext = _serviceProvider.GetRequiredService<FuelProDbContext>())
        {
            var repayment = await verifyContext.DsmPersonalDebtorRepayments.FirstOrDefaultAsync();
            Assert.NotNull(repayment);
            Assert.NotNull(repayment.ShiftId);

            var linkedShift = await verifyContext.Shifts.FindAsync(repayment.ShiftId);
            Assert.NotNull(linkedShift);
            // Must be ShiftType "C", NOT falling back to "A"!
            Assert.Equal("C", linkedShift.ShiftType);
        }
    }

    [Fact]
    public void Test10_CardSettlementViewModel_ShiftOptions_Is_A_B_C_And_Default_Is_A()
    {
        var vm = _serviceProvider.GetRequiredService<CardSettlementViewModel>();

        Assert.Equal(new[] { "A", "B", "C" }, vm.ShiftOptions);
        Assert.Equal("A", vm.SelectedShift);
    }

    [Fact]
    public void Test11_DashboardViewModel_RepaymentShiftOptions_Is_A_B_C_And_Default_Is_A()
    {
        var vm = _serviceProvider.GetRequiredService<DashboardViewModel>();

        Assert.Equal(new[] { "Shift A", "Shift B", "Shift C" }, vm.RepaymentShiftOptions);
        Assert.Equal("Shift A", vm.NewRepaymentShift);
    }

    [Fact]
    public void Test12_FinalCalculationViewModel_ShiftOptions_Is_A_B_C_And_Default_Is_A()
    {
        var vm = _serviceProvider.GetRequiredService<FinalCalculationViewModel>();

        Assert.Equal(new[] { "A", "B", "C" }, vm.ShiftOptions);
        Assert.Equal("A", vm.SelectedShift);
    }

    [Fact]
    public void Test13_DsmManagementViewModel_ShiftOptions_Is_A_B_C_And_Default_Is_A()
    {
        var vm = _serviceProvider.GetRequiredService<DsmManagementViewModel>();

        Assert.Equal(new[] { "A", "B", "C" }, vm.ShiftOptions);
        Assert.Equal("A", vm.SelectedShift);
    }

    [Fact]
    public void Test14_AgsImportViewModel_Default_Shift_Is_A()
    {
        var vm = _serviceProvider.GetRequiredService<AgsImportViewModel>();

        Assert.Equal("A", vm.SelectedShift);
    }
}
