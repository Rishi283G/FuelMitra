using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.UI.ViewModels;
using FuelPro.UI.Views;
using Xunit;

namespace FuelPro.Tests;

public class SettingsAndOilShiftRegressionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FuelProDbContext> _options;

    public SettingsAndOilShiftRegressionTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new FuelProDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private FuelProDbContext CreateContext() => new FuelProDbContext(_options);

    private OilDefDailyLogViewModel CreateViewModel(FuelProDbContext context)
    {
        var finCalc = new FinancialCalculationService(context);
        var shiftRepo = new ShiftRepository(context);
        var shiftCycle = new ShiftCycleService();
        return new OilDefDailyLogViewModel(context, finCalc, shiftRepo, shiftCycle);
    }

    [Fact]
    public async Task FreshDate_WithNoShiftRecords_Loads_A_B_C_InPhysicalOrder()
    {
        await using var context = CreateContext();
        var date = new DateTime(2026, 11, 1);

        // Verify no shifts exist initially
        Assert.Empty(await context.Shifts.Where(s => s.ShiftDate == date).ToListAsync());

        var vm = CreateViewModel(context);
        vm.SaleDate = date;
        await vm.LoadSaleShiftsAsync();

        // Must offer exactly 3 shifts in physical operational order: A, B, C
        Assert.Equal(3, vm.SaleShifts.Count);
        Assert.Equal("A", vm.SaleShifts[0].ShiftType);
        Assert.Equal("B", vm.SaleShifts[1].ShiftType);
        Assert.Equal("C", vm.SaleShifts[2].ShiftType);

        // Each must have a valid local primary key (ShiftId > 0)
        Assert.All(vm.SaleShifts, shift =>
        {
            Assert.True(shift.ShiftId > 0);
            Assert.Equal(date, shift.ShiftDate.Date);
        });

        // Database now has the 3 records created safely
        var dbShifts = await context.Shifts.Where(s => s.ShiftDate == date).ToListAsync();
        Assert.Equal(3, dbShifts.Count);
    }

    [Fact]
    public async Task ExistingShifts_AreReusedWithoutDuplicates()
    {
        await using var context = CreateContext();
        var date = new DateTime(2026, 11, 2);

        // Pre-insert Shift A only (e.g. created earlier by a DSM entry)
        var preExistingA = new Shift
        {
            ShiftDate = date,
            ShiftType = "A",
            CreatedAt = DateTime.Now
        };
        context.Shifts.Add(preExistingA);
        await context.SaveChangesAsync();
        var originalIdA = preExistingA.ShiftId;

        var vm = CreateViewModel(context);
        vm.SaleDate = date;
        await vm.LoadSaleShiftsAsync();

        Assert.Equal(3, vm.SaleShifts.Count);
        // Existing Shift A must be reused with the same ShiftId
        Assert.Equal(originalIdA, vm.SaleShifts[0].ShiftId);
        Assert.Equal("A", vm.SaleShifts[0].ShiftType);

        // Total shifts for that date in DB must be exactly 3 (no duplicate A)
        var dbShifts = await context.Shifts.Where(s => s.ShiftDate == date).ToListAsync();
        Assert.Equal(3, dbShifts.Count);
        Assert.Single(dbShifts, s => s.ShiftType == "A");
    }

    [Fact]
    public async Task RepeatedDropdownLoading_DoesNotCreateDuplicateRecords()
    {
        await using var context = CreateContext();
        var date = new DateTime(2026, 11, 3);

        var vm = CreateViewModel(context);
        vm.SaleDate = date;

        // Load 3 consecutive times
        await vm.LoadSaleShiftsAsync();
        var firstIds = vm.SaleShifts.Select(s => s.ShiftId).ToList();

        await vm.LoadSaleShiftsAsync();
        var secondIds = vm.SaleShifts.Select(s => s.ShiftId).ToList();

        await vm.LoadSaleShiftsAsync();
        var thirdIds = vm.SaleShifts.Select(s => s.ShiftId).ToList();

        Assert.Equal(firstIds, secondIds);
        Assert.Equal(secondIds, thirdIds);

        var dbShifts = await context.Shifts.Where(s => s.ShiftDate == date).ToListAsync();
        Assert.Equal(3, dbShifts.Count);
    }

    [Fact]
    public async Task ExistingOilLog_RetainsAssociatedShiftId_WhileBeingEdited()
    {
        await using var context = CreateContext();
        var date = new DateTime(2026, 11, 4);
        var product = new ProductMaster { ProductName = "2T Oil", DefaultSaleRate = 50, IsActive = true };
        context.ProductMasters.Add(product);

        var shiftB = new Shift { ShiftDate = date, ShiftType = "B" };
        context.Shifts.Add(shiftB);
        await context.SaveChangesAsync();

        var existingLog = new OilDefDailyLog
        {
            LogDate = date,
            ProductId = product.Id,
            ProductType = "Oil",
            SoldQuantity = 5,
            OverrideSaleRate = 50,
            ShiftId = shiftB.ShiftId
        };
        context.OilDefDailyLogs.Add(existingLog);
        await context.SaveChangesAsync();

        var vm = CreateViewModel(context);
        await vm.EditSaleLogCommand.ExecuteAsync(existingLog);

        Assert.NotNull(vm.SelectedSaleShift);
        Assert.Equal(shiftB.ShiftId, vm.SelectedSaleShift.ShiftId);
        Assert.Equal("B", vm.SelectedSaleShift.ShiftType);
    }

    [Fact]
    public async Task ShiftSelection_IsSavedToCorrectLocalShiftRecord()
    {
        await using var context = CreateContext();
        var date = new DateTime(2026, 11, 5);
        var product = new ProductMaster { ProductName = "DEF Can 20L", DefaultSaleRate = 120, IsActive = true };
        context.ProductMasters.Add(product);
        await context.SaveChangesAsync();

        var vm = CreateViewModel(context);
        vm.SaleDate = date;
        await vm.LoadSaleShiftsAsync();

        // Select Shift C
        var shiftC = vm.SaleShifts.First(s => s.ShiftType == "C");
        vm.SelectedSaleShift = shiftC;
        vm.SelectedProduct = product;
        vm.SaleQty = 2;
        vm.SaleRate = 120;

        // Save via persistence using the selected shift
        var saved = await OilDefAdminSalePersistence.SaveAsync(
            context,
            date,
            product,
            2,
            120,
            vm.SelectedSaleShift.ShiftId);

        Assert.NotNull(saved.ShiftId);
        Assert.Equal(shiftC.ShiftId, saved.ShiftId.Value);

        var persisted = await context.OilDefDailyLogs.SingleAsync(l => l.Id == saved.Id);
        Assert.Equal(shiftC.ShiftId, persisted.ShiftId);
    }

    [Fact]
    public async Task DuplicateShiftC_InDatabase_DeduplicatesDropdown_ToOneOptionPerSlot()
    {
        await using var context = CreateContext();
        var date = new DateTime(2026, 11, 10);

        // Pre-insert Shift A, Shift B, and TWO physical Shift C records
        var shiftA = new Shift { ShiftDate = date, ShiftType = "A" };
        var shiftB = new Shift { ShiftDate = date, ShiftType = "B" };
        var shiftC1 = new Shift { ShiftDate = date, ShiftType = "C" };
        var shiftC2 = new Shift { ShiftDate = date, ShiftType = "C" };
        context.Shifts.AddRange(shiftA, shiftB, shiftC1, shiftC2);
        await context.SaveChangesAsync();

        var vm = CreateViewModel(context);
        vm.SaleDate = date;
        await vm.LoadSaleShiftsAsync();

        // Must display exactly 3 slots: A, B, C (no duplicate C!)
        Assert.Equal(3, vm.SaleShifts.Count);
        Assert.Equal("A", vm.SaleShifts[0].ShiftType);
        Assert.Equal("B", vm.SaleShifts[1].ShiftType);
        Assert.Equal("C", vm.SaleShifts[2].ShiftType);

        // Both Shift C records in the DB must remain intact (no destructive deletes)
        var dbShifts = await context.Shifts.Where(s => s.ShiftDate == date).ToListAsync();
        Assert.Equal(4, dbShifts.Count);
        Assert.Equal(2, dbShifts.Count(s => s.ShiftType == "C"));
    }

    [Fact]
    public async Task EditSale_WithAlternateShiftC_PreservesSelectedShiftIdentity_WithoutDuplicates()
    {
        await using var context = CreateContext();
        var date = new DateTime(2026, 11, 11);
        var product = new ProductMaster { ProductName = "2T Oil", DefaultSaleRate = 50, IsActive = true };
        context.ProductMasters.Add(product);

        // Pre-insert Shift A, B, and two Shift C records (C1 and C2)
        var shiftA = new Shift { ShiftDate = date, ShiftType = "A" };
        var shiftB = new Shift { ShiftDate = date, ShiftType = "B" };
        var shiftC1 = new Shift { ShiftDate = date, ShiftType = "C" };
        var shiftC2 = new Shift { ShiftDate = date, ShiftType = "C" };
        context.Shifts.AddRange(shiftA, shiftB, shiftC1, shiftC2);
        await context.SaveChangesAsync();

        // An existing sale references alternate Shift C record (shiftC2)
        var existingLog = new OilDefDailyLog
        {
            LogDate = date,
            ProductId = product.Id,
            ProductType = "Oil",
            SoldQuantity = 3,
            OverrideSaleRate = 50,
            ShiftId = shiftC2.ShiftId
        };
        context.OilDefDailyLogs.Add(existingLog);
        await context.SaveChangesAsync();

        var vm = CreateViewModel(context);
        await vm.EditSaleLogCommand.ExecuteAsync(existingLog);

        // Dropdown must present exactly 3 options (A, B, C) - not A, B, C, C!
        Assert.Equal(3, vm.SaleShifts.Count);
        Assert.Equal(1, vm.SaleShifts.Count(s => s.ShiftType == "C"));

        // Selected sale shift must preserve shiftC2's stable identity
        Assert.NotNull(vm.SelectedSaleShift);
        Assert.Equal(shiftC2.ShiftId, vm.SelectedSaleShift.ShiftId);
        Assert.Equal("C", vm.SelectedSaleShift.ShiftType);
    }

    [Fact]
    public async Task DistinctShiftRecords_WithDifferentLabels_NormalizesAndDeduplicates()
    {
        await using var context = CreateContext();
        var date = new DateTime(2026, 11, 12);

        // Pre-insert Shift "A", "B", and Roman numeral "III" for Shift C
        var shiftA = new Shift { ShiftDate = date, ShiftType = "A" };
        var shiftB = new Shift { ShiftDate = date, ShiftType = "B" };
        var shiftC_Roman = new Shift { ShiftDate = date, ShiftType = "III" };
        context.Shifts.AddRange(shiftA, shiftB, shiftC_Roman);
        await context.SaveChangesAsync();

        var vm = CreateViewModel(context);
        vm.SaleDate = date;
        await vm.LoadSaleShiftsAsync();

        // Must offer exactly 3 slots (A, B, and the Roman III occupying the C slot)
        Assert.Equal(3, vm.SaleShifts.Count);
        Assert.Contains(vm.SaleShifts, s => s.ShiftId == shiftC_Roman.ShiftId);
    }

    [Fact]
    public void SettingsXaml_ConverterResource_ResolvesCorrectly()
    {
        Exception? threadException = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (System.Windows.Application.Current == null)
                {
                    _ = new System.Windows.Application();
                }
                var app = System.Windows.Application.Current;
                if (app != null && !app.Resources.MergedDictionaries.Any(d => d.Source?.OriginalString.Contains("MaterialDesign") == true))
                {
                    app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
                    {
                        BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Light,
                        PrimaryColor = MaterialDesignColors.PrimaryColor.Blue,
                        SecondaryColor = MaterialDesignColors.SecondaryColor.LightBlue
                    });
                    app.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign2.Defaults.xaml")
                    });
                }

                var view = new SettingsView();
                Assert.NotNull(view);
                Assert.True(view.Resources.Contains("BoolToVisConverter"), "SettingsView.Resources must contain 'BoolToVisConverter'");
                Assert.True(view.Resources.Contains("BooleanToVisibilityConverter"), "SettingsView.Resources must contain 'BooleanToVisibilityConverter'");
                Assert.IsType<System.Windows.Controls.BooleanToVisibilityConverter>(view.Resources["BoolToVisConverter"]);
                Assert.IsType<System.Windows.Controls.BooleanToVisibilityConverter>(view.Resources["BooleanToVisibilityConverter"]);
            }
            catch (Exception ex)
            {
                threadException = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (threadException != null)
        {
            throw new Exception($"SettingsView STA thread initialization failed: {threadException.Message}", threadException);
        }
    }
}

