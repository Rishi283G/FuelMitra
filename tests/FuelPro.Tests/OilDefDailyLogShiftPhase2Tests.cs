using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Services;
using FuelPro.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace FuelPro.Tests;

public class OilDefDailyLogShiftPhase2Tests
{
    [Fact]
    public async Task OilLogShift_IsNullableAndCanBeSavedAndEdited()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var product = new ProductMaster { ProductName = "Test Oil", DefaultSaleRate = 20 };
        var date = new DateTime(2026, 10, 7);
        var shiftA = new Shift { ShiftDate = date, ShiftType = "A" };
        var shiftB = new Shift { ShiftDate = date, ShiftType = "B" };
        context.AddRange(product, shiftA, shiftB);
        await context.SaveChangesAsync();

        var newSale = await OilDefAdminSalePersistence.SaveAsync(context, date, product, 2, 20, shiftA.ShiftId);
        Assert.Equal(shiftA.ShiftId, newSale.ShiftId);

        await OilDefAdminSalePersistence.SaveAsync(context, date, product, 2, 20, shiftB.ShiftId, newSale);
        Assert.Equal(shiftB.ShiftId, (await context.OilDefDailyLogs.SingleAsync()).ShiftId);

        var historical = await OilDefAdminSalePersistence.SaveAsync(
            context, date.AddDays(-100), product, 1, 20, null);
        await OilDefAdminSalePersistence.SaveAsync(
            context, historical.LogDate, product, 1, 20, null, historical);
        Assert.Null((await context.OilDefDailyLogs.SingleAsync(l => l.Id == historical.Id)).ShiftId);

        var fk = context.Model.FindEntityType(typeof(OilDefDailyLog))!.GetForeignKeys()
            .Single(f => f.Properties.Any(p => p.Name == nameof(OilDefDailyLog.ShiftId)));
        Assert.False(fk.IsRequired);
        Assert.Equal(DeleteBehavior.SetNull, fk.DeleteBehavior);
        var migration = new FuelPro.Data.Migrations.AddOilDefDailyLogShift();
        var addedColumn = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());
        Assert.Equal("OilDefDailyLogs", addedColumn.Table);
        Assert.Equal("ShiftId", addedColumn.Name);
        Assert.True(addedColumn.IsNullable);
        Assert.Contains(migration.UpOperations.OfType<AddForeignKeyOperation>(), fk =>
            fk.Table == "OilDefDailyLogs" && fk.PrincipalTable == "Shifts" && fk.OnDelete == ReferentialAction.SetNull);
    }

    [Fact]
    public async Task ActualShiftReportsFilterOilByShift_WhileDayTotalIncludesAllShiftsAndLegacyOil()
    {
        using var connection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options => options.UseSqlite(connection));
        using var provider = services.BuildServiceProvider();
        var reportService = new ReportService(new ShiftAggregationService(), provider);
        var date = new DateTime(2026, 10, 7);
        Shift[] shifts;

        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            context.Database.EnsureCreated();
            var product = new ProductMaster { ProductName = "Test Oil", DefaultSaleRate = 20 };
            shifts = new[]
            {
                new Shift { ShiftDate = date, ShiftType = "A" },
                new Shift { ShiftDate = date, ShiftType = "B" },
                new Shift { ShiftDate = date, ShiftType = "C" }
            };
            context.Add(product);
            context.AddRange(shifts);
            await context.SaveChangesAsync();
            foreach (var shift in shifts)
            {
                context.OilDefDailyLogs.Add(new OilDefDailyLog
                {
                    LogDate = date,
                    ProductId = product.Id,
                    ProductType = "Oil",
                    SoldQuantity = 4,
                    OverrideSaleRate = 20,
                    ShiftId = shift.ShiftId
                });
            }
            context.OilDefDailyLogs.Add(new OilDefDailyLog
            {
                LogDate = date,
                ProductId = product.Id,
                ProductType = "Oil",
                SoldQuantity = 2,
                OverrideSaleRate = 20,
                ShiftId = null
            });
            await context.SaveChangesAsync();
        }

        foreach (var shift in shifts)
        {
            var reportEntries = shift.ShiftType == "A"
                ? new List<DsmEntry>()
                : new List<DsmEntry> { new() { ShiftId = shift.ShiftId, Shift = shift, CreatedAt = date } };
            var shiftReport = reportService.CalculateShiftReport(
                date,
                shift.ShiftType,
                reportEntries,
                new List<Expense>(),
                new List<ShiftOtherCash>(),
                new List<CreditorRepayment>(),
                0, 0, 0, 0,
                new BusinessDayTidSheet(),
                new BusinessDayTidSheet(),
                "Test Station");

            Assert.Single(shiftReport.OilDefSales);
            Assert.Equal(4, shiftReport.OilDefSales[0].Quantity);
            Assert.Equal(80, shiftReport.OilDefSalesTotal);
        }

        var dayReport = reportService.CalculateDayReport(
            date,
            date,
            shifts.Select(s => new DsmEntry { ShiftId = s.ShiftId, Shift = s, CreatedAt = date }).ToList(),
            new List<Expense>(),
            new List<CreditorRepayment>(),
            0, 0, 0, 0,
            "Test Station");
        Assert.Equal(4, dayReport.OilDefSales.Count);
        Assert.Equal(14, dayReport.OilDefSales.Sum(r => r.Quantity));
        Assert.Equal(280, dayReport.OilDefSalesTotal);
    }

    [Fact]
    public async Task DsmApprovedOilSale_IsStoredAgainstOriginatingShift()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var date = new DateTime(2026, 10, 7);
        var product = new ProductMaster { ProductName = "Test Oil", DefaultSaleRate = 10 };
        var shifts = new[]
        {
            new Shift { ShiftDate = date, ShiftType = "A" },
            new Shift { ShiftDate = date, ShiftType = "B" },
            new Shift { ShiftDate = date, ShiftType = "C" }
        };
        context.Add(product);
        context.AddRange(shifts);
        await context.SaveChangesAsync();

        foreach (var shift in shifts)
            await DsmOilDefSalePersistence.AddOrAccumulateAsync(context, shift.ShiftId, date, product, 3, 10);

        var logs = await context.OilDefDailyLogs.OrderBy(l => l.ShiftId).ToListAsync();
        Assert.Equal(3, logs.Count);
        Assert.Equal(shifts.Select(s => s.ShiftId).Order().ToArray(), logs.Select(l => l.ShiftId!.Value).ToArray());
        Assert.All(logs, log => Assert.Equal(3, log.SoldQuantity));
    }

    [Fact]
    public async Task OilSyncShiftMatcher_DoesNotMatchOtherwiseIdenticalLogsAcrossShifts()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
        var date = new DateTime(2026, 10, 7);
        var product = new ProductMaster { ProductName = "Test Oil" };
        var shifts = new[]
        {
            new Shift { ShiftDate = date, ShiftType = "A" },
            new Shift { ShiftDate = date, ShiftType = "B" },
            new Shift { ShiftDate = date, ShiftType = "C" }
        };
        context.Add(product);
        context.AddRange(shifts);
        await context.SaveChangesAsync();
        foreach (var shift in shifts)
            context.OilDefDailyLogs.Add(new OilDefDailyLog
            {
                LogDate = date,
                ProductId = product.Id,
                ProductType = "Oil",
                SoldQuantity = 5,
                ShiftId = shift.ShiftId
            });
        context.OilDefDailyLogs.Add(new OilDefDailyLog
        {
            LogDate = date,
            ProductId = product.Id,
            ProductType = "Oil",
            SoldQuantity = 5,
            ShiftId = null
        });
        await context.SaveChangesAsync();

        foreach (var shift in shifts)
        {
            var matches = await OilDefLogSyncMatcher.ForShift(context.OilDefDailyLogs, shift.ShiftId).ToListAsync();
            Assert.Single(matches);
            Assert.Equal(shift.ShiftId, matches[0].ShiftId);
        }
        Assert.Single(await OilDefLogSyncMatcher.ForShift(context.OilDefDailyLogs, null).ToListAsync());
    }

    [Fact]
    public async Task ShiftReport_IncludesShiftCSale_WhenDuplicateShiftCRecordsExistInDb()
    {
        using var connection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options => options.UseSqlite(connection));
        using var provider = services.BuildServiceProvider();
        var reportService = new ReportService(new ShiftAggregationService(), provider);
        var date = new DateTime(2026, 10, 7);

        Shift shiftC2;
        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            context.Database.EnsureCreated();
            var product = new ProductMaster { ProductName = "Test Oil", DefaultSaleRate = 25 };
            var shiftA = new Shift { ShiftDate = date, ShiftType = "A" };
            var shiftB = new Shift { ShiftDate = date, ShiftType = "B" };
            var shiftC1 = new Shift { ShiftDate = date, ShiftType = "C" };
            shiftC2 = new Shift { ShiftDate = date, ShiftType = "C" }; // Duplicate physical Shift C
            context.Add(product);
            context.AddRange(shiftA, shiftB, shiftC1, shiftC2);
            await context.SaveChangesAsync();

            // Sale is logged against duplicate Shift C record (shiftC2)
            context.OilDefDailyLogs.Add(new OilDefDailyLog
            {
                LogDate = date,
                ProductId = product.Id,
                ProductType = "Oil",
                SoldQuantity = 4,
                OverrideSaleRate = 25,
                ShiftId = shiftC2.ShiftId
            });
            await context.SaveChangesAsync();
        }

        // Generate report for Shift C on that date
        var reportEntries = new List<DsmEntry>
        {
            new() { ShiftId = shiftC2.ShiftId, Shift = shiftC2, CreatedAt = date }
        };

        var shiftReport = reportService.CalculateShiftReport(
            date,
            "C",
            reportEntries,
            new List<Expense>(),
            new List<ShiftOtherCash>(),
            new List<CreditorRepayment>(),
            0, 0, 0, 0,
            new BusinessDayTidSheet(),
            new BusinessDayTidSheet(),
            "Test Station");

        // Oil sale must be included exactly once even though 2 Shift C records exist in DB
        Assert.Single(shiftReport.OilDefSales);
        Assert.Equal(4, shiftReport.OilDefSales[0].Quantity);
        Assert.Equal(100, shiftReport.OilDefSalesTotal);
    }

    [Fact]
    public async Task DayReport_IncludesOvernightShiftCSale_EnteredAfterMidnight()
    {
        using var connection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options => options.UseSqlite(connection));
        using var provider = services.BuildServiceProvider();
        var reportService = new ReportService(new ShiftAggregationService(), provider);

        var operationalDate = new DateTime(2026, 10, 7);
        var nextCalendarDate = operationalDate.AddDays(1); // Entered at 01:30 AM after midnight

        Shift shiftC;
        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            context.Database.EnsureCreated();
            var product = new ProductMaster { ProductName = "Engine Oil 1L", DefaultSaleRate = 350 };
            shiftC = new Shift { ShiftDate = operationalDate, ShiftType = "C" };
            context.Add(product);
            context.Add(shiftC);
            await context.SaveChangesAsync();

            // Sale entered at 01:30 AM on nextCalendarDate, but associated to Shift C of operationalDate
            context.OilDefDailyLogs.Add(new OilDefDailyLog
            {
                LogDate = nextCalendarDate.AddHours(1).AddMinutes(30),
                ProductId = product.Id,
                ProductType = "Oil",
                SoldQuantity = 2,
                OverrideSaleRate = 350,
                ShiftId = shiftC.ShiftId
            });
            await context.SaveChangesAsync();
        }

        // Shift report for operationalDate Shift C
        var shiftReport = reportService.CalculateShiftReport(
            operationalDate,
            "C",
            new List<DsmEntry> { new() { ShiftId = shiftC.ShiftId, Shift = shiftC, CreatedAt = operationalDate } },
            new List<Expense>(),
            new List<ShiftOtherCash>(),
            new List<CreditorRepayment>(),
            0, 0, 0, 0,
            new BusinessDayTidSheet(),
            new BusinessDayTidSheet(),
            "Test Station");

        Assert.Single(shiftReport.OilDefSales);
        Assert.Equal(700, shiftReport.OilDefSalesTotal);

        // Day report for operationalDate must include the overnight Shift C sale exactly once
        var dayReport = reportService.CalculateDayReport(
            operationalDate,
            operationalDate,
            new List<DsmEntry> { new() { ShiftId = shiftC.ShiftId, Shift = shiftC, CreatedAt = operationalDate } },
            new List<Expense>(),
            new List<CreditorRepayment>(),
            0, 0, 0, 0,
            "Test Station");

        Assert.Single(dayReport.OilDefSales);
        Assert.Equal(700, dayReport.OilDefSalesTotal);
    }

    [Fact]
    public async Task LegacyNullShiftOilSale_PreservedInDayReport_WithoutInferringHistoricalShift()
    {
        using var connection = new SqliteConnection($"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<FuelProDbContext>(options => options.UseSqlite(connection));
        using var provider = services.BuildServiceProvider();
        var reportService = new ReportService(new ShiftAggregationService(), provider);
        var date = new DateTime(2026, 10, 7);

        using (var scope = provider.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
            context.Database.EnsureCreated();
            var product = new ProductMaster { ProductName = "Legacy Oil", DefaultSaleRate = 100 };
            var shiftC = new Shift { ShiftDate = date, ShiftType = "C" };
            context.AddRange(product, shiftC);
            await context.SaveChangesAsync();

            // Legacy sale with ShiftId == null
            context.OilDefDailyLogs.Add(new OilDefDailyLog
            {
                LogDate = date,
                ProductId = product.Id,
                ProductType = "Oil",
                SoldQuantity = 1,
                OverrideSaleRate = 100,
                ShiftId = null
            });
            await context.SaveChangesAsync();
        }

        // Shift report for Shift C must NOT infer or include unassigned legacy sale
        var shiftReport = reportService.CalculateShiftReport(
            date,
            "C",
            new List<DsmEntry>(),
            new List<Expense>(),
            new List<ShiftOtherCash>(),
            new List<CreditorRepayment>(),
            0, 0, 0, 0,
            new BusinessDayTidSheet(),
            new BusinessDayTidSheet(),
            "Test Station");
        Assert.Empty(shiftReport.OilDefSales);

        // Day report for date must include the legacy sale based on its LogDate
        var dayReport = reportService.CalculateDayReport(
            date,
            date,
            new List<DsmEntry>(),
            new List<Expense>(),
            new List<CreditorRepayment>(),
            0, 0, 0, 0,
            "Test Station");
        Assert.Single(dayReport.OilDefSales);
        Assert.Equal(100, dayReport.OilDefSalesTotal);
    }

    private static FuelProDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new FuelProDbContext(options);
    }
}
