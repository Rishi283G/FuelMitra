using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using Xunit;

namespace FuelPro.Tests;

public class AgsInventoryTests
{
    private readonly string _dbPath;

    public AgsInventoryTests()
    {
        Environment.SetEnvironmentVariable("FUELPRO_ENV", "TEST");
        _dbPath = Path.Combine(Path.GetTempPath(), $"fuelPro_test_{Guid.NewGuid():N}.db");
    }

    private ServiceProvider SetupServiceProvider()
    {
        var services = new ServiceCollection();

        services.AddDbContext<FuelProDbContext>(options =>
            options.UseSqlite($"Data Source={_dbPath}"),
            ServiceLifetime.Scoped);

        // Repositories
        services.AddTransient<ISettingsRepository, SettingsRepository>();
        services.AddTransient<IShiftRepository, ShiftRepository>();
        services.AddTransient<IDsmEntryRepository, DsmEntryRepository>();
        services.AddTransient<INozzleReadingRepository, NozzleReadingRepository>();
        services.AddTransient<IPaymentRepository, PaymentRepository>();
        services.AddTransient<IDebitEntryRepository, DebitEntryRepository>();
        services.AddTransient<ITestingEntryRepository, TestingEntryRepository>();
        services.AddTransient<IExpenseRepository, ExpenseRepository>();
        services.AddTransient<ICashDenominationRepository, CashDenominationRepository>();
        services.AddTransient<IAgsImportRepository, AgsImportRepository>();
        services.AddTransient<ICreditorRepository, CreditorRepository>();
        services.AddTransient<IFuelTankerRepository, FuelTankerRepository>();

        // Services
        services.AddTransient<IAgsImportService, AgsImportService>();
        services.AddTransient<IAgsDailyAggregationService, AgsDailyAggregationService>();
        services.AddTransient<IAgsInventoryService, AgsInventoryService>();
        services.AddScoped<IShiftAggregationService, ShiftAggregationService>();

        var provider = services.BuildServiceProvider();

        // Ensure database schema is created
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FuelProDbContext>();
        context.Database.EnsureCreated();

        return provider;
    }

    private async Task CreateMockImportAsync(IAgsImportRepository repo, DateTime date, string shiftType, double hsdSaleLitres)
    {
        var mappedShift = shiftType == "A" ? "I" : shiftType == "B" ? "II" : shiftType == "C" ? "III" : shiftType;
        var import = new AgsShiftImport
        {
            ImportDate = date.Date,
            ShiftType = mappedShift,
            PdfFileName = $"Mock_{shiftType}.pdf",
            ImportedBy = "Test",
            IsActive = true,
            ImportedAt = DateTime.Now,
            NozzleReadings = new List<AgsNozzleReading>
            {
                // Nozzle 3 is HSD (Tank 2)
                new AgsNozzleReading { NozzleNumber = 3, NetSaleLitres = hsdSaleLitres, OpeningReading = 1000, ClosingReading = 1000 + hsdSaleLitres }
            }
        };
        await repo.SaveShiftImportAsync(import);
    }

    [Fact]
    public async Task Test_ShiftContinuityAndPropagationPipeline()
    {
        var provider = SetupServiceProvider();
        var date1 = new DateTime(2026, 7, 10);

        using (var scope = provider.CreateScope())
        {
            var agsRepo = scope.ServiceProvider.GetRequiredService<IAgsImportRepository>();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IAgsInventoryService>();

            // 1. Arrange: Pre-populate database with Shift A, Shift B, and Shift C imports containing nozzle readings
            await CreateMockImportAsync(agsRepo, date1, "A", 1000.0);
            await CreateMockImportAsync(agsRepo, date1, "B", 500.0);
            await CreateMockImportAsync(agsRepo, date1, "C", 500.0);

            var tankerRepo = scope.ServiceProvider.GetRequiredService<IFuelTankerRepository>();
            await tankerRepo.AddAsync(new FuelTanker
            {
                TankerDate = date1.Date,
                FuelType = "HSD",
                Quantity = 2000.0,
                InvoiceNumber = "INV-TEST-PROP",
                CreatedAt = DateTime.Now
            });

            // Load nozzle groups for Shift A (Day Opening Shift)
            var groupsA = await inventoryService.BuildNozzleGroupsAsync(date1, "A");
            Assert.NotNull(groupsA);

            // Set initial opening stock of HSD (Tank 2) to 5000L.
            // Expected closing stock for Shift A: 5000 - 1000 = 4000L.
            var hsdGroupA = groupsA.First(g => g.GroupName.Contains("Tank 2"));
            hsdGroupA.OpeningStock = 5000.0;
            hsdGroupA.Dip = 0.0; // Dynamic book calculation

            var saveAResult = await inventoryService.SaveAndPersistInventoryAsync(date1, "A", groupsA);
            Assert.True(saveAResult.Success);

            // Re-fetch Shift A and assert stock values
            var activeARes = await agsRepo.GetActiveShiftImportAsync(date1, "A");
            Assert.True(activeARes.Success);
            Assert.Equal(5000.0, activeARes.Data.HsdOpeningStock);
            Assert.Equal(4000.0, activeARes.Data.HsdClosingStock);

            // Load nozzle groups for Shift B (Day Shift receives the 2000L tanker receipts)
            var groupsB = await inventoryService.BuildNozzleGroupsAsync(date1, "B");
            Assert.NotNull(groupsB);
            var hsdGroupB = groupsB.First(g => g.GroupName.Contains("Tank 2"));
            Assert.Equal(4000.0, hsdGroupB.OpeningStock); // Pulled from Shift A closing stock
            hsdGroupB.Dip = 0.0;
            var saveBResult = await inventoryService.SaveAndPersistInventoryAsync(date1, "B", groupsB);
            Assert.True(saveBResult.Success);

            // Re-fetch Shift B and assert stock values (opening 4000, +2000 receipts -500 sales = 5500 closing)
            var activeBRes = await agsRepo.GetActiveShiftImportAsync(date1, "B");
            Assert.True(activeBRes.Success);
            Assert.Equal(4000.0, activeBRes.Data.HsdOpeningStock);
            Assert.Equal(5500.0, activeBRes.Data.HsdClosingStock);

            // Load nozzle groups for Shift C (Closing Shift)
            var groupsC = await inventoryService.BuildNozzleGroupsAsync(date1, "C");
            Assert.NotNull(groupsC);

            // Assert that Shift C opening stock automatically pulled from Shift B closing stock (5500L)
            var hsdGroupC = groupsC.First(g => g.GroupName.Contains("Tank 2"));
            Assert.Equal(5500.0, hsdGroupC.OpeningStock);
            hsdGroupC.Dip = 0.0; // Dynamic book calculation

            var saveCResult = await inventoryService.SaveAndPersistInventoryAsync(date1, "C", groupsC);
            Assert.True(saveCResult.Success);

            // Re-fetch Shift C and assert stock values (opening 5500, closing 5000)
            var activeCRes = await agsRepo.GetActiveShiftImportAsync(date1, "C");
            Assert.True(activeCRes.Success);
            Assert.Equal(5500.0, activeCRes.Data.HsdOpeningStock);
            Assert.Equal(5000.0, activeCRes.Data.HsdClosingStock);

            // 2. Act: Edit Shift A's sales from 1000L to 1500L in the database, then run propagation
            var aImport = (await agsRepo.GetActiveShiftImportAsync(date1, "A")).Data;
            Assert.NotNull(aImport);
            aImport.NozzleReadings.First().NetSaleLitres = 1500.0;
            await agsRepo.SaveShiftImportAsync(aImport);

            // Trigger propagation pipeline starting from Shift A
            var propResult = await inventoryService.PropagateInventoryCalculationsAsync(date1, "A");
            Assert.True(propResult.Success);

            // 3. Assert: Verify Shift B's and Shift C's opening and closing stock evolved chronologically
            var activeBResAfterProp = await agsRepo.GetActiveShiftImportAsync(date1, "B");
            Assert.True(activeBResAfterProp.Success);
            Assert.Equal(3500.0, activeBResAfterProp.Data.HsdOpeningStock); // Shift A closing: 5000 - 1500 = 3500
            Assert.Equal(5000.0, activeBResAfterProp.Data.HsdClosingStock); // 3500 + 2000 - 500 = 5000

            var activeCResAfterProp = await agsRepo.GetActiveShiftImportAsync(date1, "C");
            Assert.True(activeCResAfterProp.Success);
            Assert.Equal(5000.0, activeCResAfterProp.Data.HsdOpeningStock); // Shift B closing: 5000
            Assert.Equal(4500.0, activeCResAfterProp.Data.HsdClosingStock); // 5000 - 500 = 4500
        }
    }

    [Fact]
    public async Task Test_DailySummaryChronologicalMapping()
    {
        var provider = SetupServiceProvider();
        var date = new DateTime(2026, 7, 10);

        using (var scope = provider.CreateScope())
        {
            var agsRepo = scope.ServiceProvider.GetRequiredService<IAgsImportRepository>();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IAgsInventoryService>();

            // Setup Shift A (Morning Opening Shift)
            await CreateMockImportAsync(agsRepo, date, "A", 2000.0);
            var groupsA = await inventoryService.BuildNozzleGroupsAsync(date, "A");
            var hsdGroupA = groupsA.First(g => g.GroupName.Contains("Tank 2"));
            hsdGroupA.OpeningStock = 8000.0;
            hsdGroupA.Dip = 120.0;
            await inventoryService.SaveAndPersistInventoryAsync(date, "A", groupsA);

            // Setup Shift B (Day Shift)
            await CreateMockImportAsync(agsRepo, date, "B", 1000.0);
            var groupsB = await inventoryService.BuildNozzleGroupsAsync(date, "B");
            var hsdGroupB = groupsB.First(g => g.GroupName.Contains("Tank 2"));
            hsdGroupB.Dip = 115.0;
            await inventoryService.SaveAndPersistInventoryAsync(date, "B", groupsB);

            // Setup Shift C (Night Closing Shift)
            await CreateMockImportAsync(agsRepo, date, "C", 1000.0);
            var groupsC = await inventoryService.BuildNozzleGroupsAsync(date, "C");
            var hsdGroupC = groupsC.First(g => g.GroupName.Contains("Tank 2"));
            hsdGroupC.Dip = 110.0;
            await inventoryService.SaveAndPersistInventoryAsync(date, "C", groupsC);

            // Fetch daily summary
            var summaryRes = await agsRepo.GetDailySummaryAsync(date);
            Assert.True(summaryRes.Success);
            Assert.NotNull(summaryRes.Data);

            // Day Opening Stock must come from Shift A (8000L)
            Assert.Equal(8000.0, summaryRes.Data.HsdDayOpeningStock);
            
            // Day Closing Stock must come from Shift C (6000 - 1000 - 1000 = 4000L)
            Assert.Equal(4000.0, summaryRes.Data.HsdDayClosingStock);
        }
    }

    [Fact]
    public async Task Test_FuelTankerPurchases_FlowIntoReceipts()
    {
        var provider = SetupServiceProvider();
        var date = new DateTime(2026, 7, 10);

        using (var scope = provider.CreateScope())
        {
            var agsRepo = scope.ServiceProvider.GetRequiredService<IAgsImportRepository>();
            var tankerRepo = scope.ServiceProvider.GetRequiredService<IFuelTankerRepository>();
            var inventoryService = scope.ServiceProvider.GetRequiredService<IAgsInventoryService>();

            // Setup Shift B (Day) with an import
            await CreateMockImportAsync(agsRepo, date, "B", 1000.0);

            // Add a tanker purchase of 5000L of HSD
            var tanker = new FuelTanker
            {
                TankerDate = date.Date,
                FuelType = "HSD",
                Quantity = 5000.0,
                PurchaseRate = 90.0,
                TotalAmount = 450000.0,
                CreatedAt = DateTime.Now
            };
            var saveTankerRes = await tankerRepo.AddAsync(tanker);
            Assert.True(saveTankerRes.Success);

            // Load nozzle groups for Shift B
            var groupsB = await inventoryService.BuildNozzleGroupsAsync(date, "B");
            var hsdGroupB = groupsB.First(g => g.GroupName.Contains("Tank 2"));

            // Verify that Receipts is automatically populated with the 5000L tanker quantity
            Assert.Equal(5000.0, hsdGroupB.Receipts);

            // Verify calculated stock: Opening (default 0) - Sales (1000) + Receipts (5000) = 4000L
            Assert.Equal(4000.0, hsdGroupB.CalculatedStock);

            // Load groups for Shift A (Night) and verify receipts are 0 (only Shift B gets the day's tanker receipts)
            await CreateMockImportAsync(agsRepo, date, "A", 500.0);
            var groupsA = await inventoryService.BuildNozzleGroupsAsync(date, "A");
            var hsdGroupA = groupsA.First(g => g.GroupName.Contains("Tank 2"));
            Assert.Equal(0.0, hsdGroupA.Receipts);
        }
    }
}
