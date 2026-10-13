using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;
using FuelPro.UI.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FuelPro.Tests;

public class ProductionSafeEnhancementsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<FuelProDbContext> _options;

    public ProductionSafeEnhancementsTests()
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

    #region Requirement 1: Oil/DEF Originating Shift Tests

    [Fact]
    public void OilDefDailyLog_ShiftDisplay_ShowsShiftTypeOrSafeDash()
    {
        // Null shift -> safe dash
        var logNull = new OilDefDailyLog { Shift = null, ShiftId = null };
        Assert.Equal("—", logNull.ShiftDisplay);

        // Empty shift type -> safe dash
        var logEmpty = new OilDefDailyLog { Shift = new Shift { ShiftType = "  " } };
        Assert.Equal("—", logEmpty.ShiftDisplay);

        // Valid shifts -> exact shift type
        var logA = new OilDefDailyLog { Shift = new Shift { ShiftType = "A" } };
        Assert.Equal("A", logA.ShiftDisplay);

        var logB = new OilDefDailyLog { Shift = new Shift { ShiftType = "B" } };
        Assert.Equal("B", logB.ShiftDisplay);

        var logC = new OilDefDailyLog { Shift = new Shift { ShiftType = "C" } };
        Assert.Equal("C", logC.ShiftDisplay);
    }

    [Fact]
    public async Task OilDefHistory_LoadsShiftNavigation_PreservingHistoricalNulls()
    {
        await using var context = CreateContext();
        var date = new DateTime(2026, 10, 10);
        var product = new ProductMaster { ProductName = "Servo 4T 1L", DefaultSaleRate = 350, IsActive = true };
        var shiftB = new Shift { ShiftDate = date, ShiftType = "B" };
        context.ProductMasters.Add(product);
        context.Shifts.Add(shiftB);
        await context.SaveChangesAsync();

        // 1. Entry with originating Shift B
        var logWithShift = new OilDefDailyLog
        {
            LogDate = date,
            ProductId = product.Id,
            ProductType = "Oil",
            SoldQuantity = 2,
            OverrideSaleRate = 350,
            ShiftId = shiftB.ShiftId
        };

        // 2. Legacy entry without shift
        var logLegacy = new OilDefDailyLog
        {
            LogDate = date.AddDays(-30),
            ProductId = product.Id,
            ProductType = "Oil",
            SoldQuantity = 1,
            OverrideSaleRate = 350,
            ShiftId = null
        };

        context.OilDefDailyLogs.AddRange(logWithShift, logLegacy);
        await context.SaveChangesAsync();

        // Query with Include(l => l.Shift) as implemented in LoadHistoryAsync
        var logs = await context.OilDefDailyLogs
            .Include(l => l.Product)
            .Include(l => l.Shift)
            .OrderByDescending(l => l.LogDate)
            .ToListAsync();

        var queriedWithShift = logs.Single(l => l.Id == logWithShift.Id);
        var queriedLegacy = logs.Single(l => l.Id == logLegacy.Id);

        Assert.NotNull(queriedWithShift.Shift);
        Assert.Equal("B", queriedWithShift.ShiftDisplay);

        Assert.Null(queriedLegacy.Shift);
        Assert.Equal("—", queriedLegacy.ShiftDisplay);
    }

    [Fact]
    public void OilDefSaleDisplayRow_MapsShiftTypeCorrectly()
    {
        var row = new OilDefSaleDisplayRow
        {
            ProductName = "Servo 2T",
            Quantity = 3,
            Rate = 60,
            Total = 180,
            ShiftType = "A"
        };

        Assert.Equal("A", row.ShiftType);

        var rowLegacy = new OilDefSaleDisplayRow
        {
            ProductName = "Legacy Oil",
            Quantity = 1,
            Rate = 100,
            Total = 100,
            ShiftType = ""
        };

        Assert.Equal(string.Empty, rowLegacy.ShiftType);
    }

    #endregion

    #region Requirement 2: Debtor Repayment Shift Tests

    [Fact]
    public void CreditorRepayment_ShiftDisplay_ShowsShiftOrSafeDash()
    {
        var repNull = new CreditorRepayment { ShiftNumber = null };
        Assert.Equal("—", repNull.ShiftDisplay);

        var repEmpty = new CreditorRepayment { ShiftNumber = "   " };
        Assert.Equal("—", repEmpty.ShiftDisplay);

        var repA = new CreditorRepayment { ShiftNumber = "A" };
        Assert.Equal("A", repA.ShiftDisplay);

        var repB = new CreditorRepayment { ShiftNumber = "B" };
        Assert.Equal("B", repB.ShiftDisplay);

        var repC = new CreditorRepayment { ShiftNumber = "C" };
        Assert.Equal("C", repC.ShiftDisplay);
    }

    [Fact]
    public void DebtorLedgerPrintRow_ContainsShiftProperty()
    {
        var row = new DebtorLedgerPrintRow
        {
            Date = DateTime.Today.ToString("dd-MM-yyyy"),
            Shift = "B",
            Description = "Fuel credit",
            Debit = 1500,
            Credit = 0,
            RunningBalance = 1500
        };

        Assert.Equal("B", row.Shift);

        var repRow = new DebtorLedgerPrintRow
        {
            Date = DateTime.Today.ToString("dd-MM-yyyy"),
            Shift = "A",
            Description = "Repayment (Cash)",
            Debit = 0,
            Credit = 1500,
            RunningBalance = 0
        };

        Assert.Equal("A", repRow.Shift);
    }

    [Fact]
    public void DsmPersonalDebtorLedgerRow_DefaultsShiftToDash()
    {
        var row = new DsmPersonalDebtorLedgerRow();
        Assert.Equal("—", row.Shift);

        row.Shift = "C";
        Assert.Equal("C", row.Shift);
    }

    #endregion

    #region Requirement 3: DSM Profile Propagation & Lifecycle Tests

    [Fact]
    public void DsmProfileRenamedAndDeleted_EventsTriggerProperly()
    {
        string? renamedOld = null;
        string? renamedNew = null;
        string? deletedName = null;
        int profileChangedCount = 0;

        void OnRenamed(string oldN, string newN)
        {
            renamedOld = oldN;
            renamedNew = newN;
        }

        void OnDeleted(string name)
        {
            deletedName = name;
        }

        void OnChanged()
        {
            profileChangedCount++;
        }

        DsmEntryService.DsmProfileRenamed += OnRenamed;
        DsmEntryService.DsmProfileDeleted += OnDeleted;
        DsmEntryService.DsmProfileChanged += OnChanged;

        try
        {
            DsmEntryService.RaiseDsmProfileRenamed("OldName", "NewName");
            Assert.Equal("OldName", renamedOld);
            Assert.Equal("NewName", renamedNew);

            DsmEntryService.RaiseDsmProfileDeleted("DeletedProfile");
            Assert.Equal("DeletedProfile", deletedName);

            DsmEntryService.RaiseDsmProfileChanged();
            // RaiseDsmProfileRenamed (+1), RaiseDsmProfileDeleted (+1), and RaiseDsmProfileChanged (+1)
            // ensure full backward compatibility for listeners of DsmProfileChanged.
            Assert.Equal(3, profileChangedCount);
        }
        finally
        {
            DsmEntryService.DsmProfileRenamed -= OnRenamed;
            DsmEntryService.DsmProfileDeleted -= OnDeleted;
            DsmEntryService.DsmProfileChanged -= OnChanged;
        }

        // Verify that unsubscription stops further invocations
        DsmEntryService.RaiseDsmProfileRenamed("X", "Y");
        DsmEntryService.RaiseDsmProfileDeleted("Z");
        DsmEntryService.RaiseDsmProfileChanged();

        Assert.Equal("OldName", renamedOld);
        Assert.Equal("NewName", renamedNew);
        Assert.Equal("DeletedProfile", deletedName);
        Assert.Equal(3, profileChangedCount);
    }

    [Fact]
    public async Task DsmProfileRepository_GetAllAsync_ReturnsNoTrackingEntities()
    {
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using (var setup = new FuelProDbContext(options))
        {
            setup.DsmProfiles.Add(new DsmProfile { DsmName = "Active DSM" });
            await setup.SaveChangesAsync();
        }

        await using (var context = new FuelProDbContext(options))
        {
            var repo = new DsmProfileRepository(context);
            var result = await repo.GetAllAsync();

            Assert.True(result.Success);
            Assert.Single(result.Data!);

            // Entities queried via GetAllAsync should not be tracked by the context's change tracker
            var trackedCount = context.ChangeTracker.Entries<DsmProfile>().Count();
            Assert.Equal(0, trackedCount);
        }
    }

    #endregion
}
