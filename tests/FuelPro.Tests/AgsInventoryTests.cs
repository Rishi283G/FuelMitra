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

            // 1. Arrange: Pre-populate database with Shift B, Shift C, and Shift A imports containing nozzle readings
            await CreateMockImportAsync(agsRepo, date1, "B", 1000.0);
            await CreateMockImportAsync(agsRepo, date1, "C", 500.0);
            await CreateMockImportAsync(agsRepo, date1, "A", 500.0);

            var tankerRepo = scope.ServiceProvider.GetRequiredService<IFuelTankerRepository>();
            await tankerRepo.AddAsync(new FuelTanker
            {
                TankerDate = date1.Date,
                FuelType = "HSD",
                Quantity = 2000.0,
                InvoiceNumber = "INV-TEST-PROP",
                CreatedAt = DateTime.Now
            });

            // Load nozzle groups for Shift B (Operational Day Opening Shift)
            var groupsB = await inventoryService.BuildNozzleGroupsAsync(date1, "B");
            Assert.NotNull(groupsB);

            // Set initial opening stock of HSD (Tank 2) to 5000L.
            // Expected closing stock for Shift B: 5000 + 2000 (receipts) - 1000 (sales) = 6000L.
            var hsdGroupB = groupsB.First(g => g.GroupName.Contains("Tank 2"));
            hsdGroupB.OpeningStock = 5000.0;
            hsdGroupB.Dip = 0.0; // Dynamic book calculation

            var saveBResult = await inventoryService.SaveAndPersistInventoryAsync(date1, "B", groupsB);
            Assert.True(saveBResult.Success);

            // Re-fetch Shift B and assert stock values
            var activeBRes = await agsRepo.GetActiveShiftImportAsync(date1, "B");
            Assert.True(activeBRes.Success);
            Assert.Equal(5000.0, activeBRes.Data.HsdOpeningStock);
            Assert.Equal(6000.0, activeBRes.Data.HsdClosingStock);

            // Load nozzle groups for Shift C (Afternoon Shift receives Shift B's closing stock)
            var groupsC = await inventoryService.BuildNozzleGroupsAsync(date1, "C");
            Assert.NotNull(groupsC);
            var hsdGroupC = groupsC.First(g => g.GroupName.Contains("Tank 2"));
            Assert.Equal(6000.0, hsdGroupC.OpeningStock); // Pulled from Shift B closing stock
            hsdGroupC.Dip = 0.0;
            var saveCResult = await inventoryService.SaveAndPersistInventoryAsync(date1, "C", groupsC);
            Assert.True(saveCResult.Success);

            // Re-fetch Shift C and assert stock values (opening 6000, -500 sales = 5500 closing)
            var activeCRes = await agsRepo.GetActiveShiftImportAsync(date1, "C");
            Assert.True(activeCRes.Success);
            Assert.Equal(6000.0, activeCRes.Data.HsdOpeningStock);
            Assert.Equal(5500.0, activeCRes.Data.HsdClosingStock);

            // Load nozzle groups for Shift A (Night Closing Shift)
            var groupsA = await inventoryService.BuildNozzleGroupsAsync(date1, "A");
            Assert.NotNull(groupsA);

            // Assert that Shift A opening stock automatically pulled from Shift C closing stock (5500L)
            var hsdGroupA = groupsA.First(g => g.GroupName.Contains("Tank 2"));
            Assert.Equal(5500.0, hsdGroupA.OpeningStock);
            hsdGroupA.Dip = 0.0; // Dynamic book calculation

            var saveAResult = await inventoryService.SaveAndPersistInventoryAsync(date1, "A", groupsA);
            Assert.True(saveAResult.Success);

            // Re-fetch Shift A and assert stock values (opening 5500, closing 5000)
            var activeARes = await agsRepo.GetActiveShiftImportAsync(date1, "A");
            Assert.True(activeARes.Success);
            Assert.Equal(5500.0, activeARes.Data.HsdOpeningStock);
            Assert.Equal(5000.0, activeARes.Data.HsdClosingStock);

            // 2. Act: Edit Shift B's sales from 1000L to 1500L in the database, then run propagation
            var bImport = (await agsRepo.GetActiveShiftImportAsync(date1, "B")).Data;
            Assert.NotNull(bImport);
            bImport.NozzleReadings.First().NetSaleLitres = 1500.0;
            await agsRepo.SaveShiftImportAsync(bImport);

            // Trigger propagation pipeline starting from Shift B
            var propResult = await inventoryService.PropagateInventoryCalculationsAsync(date1, "B");
            Assert.True(propResult.Success);

            // 3. Assert: Verify Shift C's and Shift A's opening and closing stock evolved chronologically
            var activeCResAfterProp = await agsRepo.GetActiveShiftImportAsync(date1, "C");
            Assert.True(activeCResAfterProp.Success);
            Assert.Equal(5500.0, activeCResAfterProp.Data.HsdOpeningStock); // Shift B closing: 5000 + 2000 - 1500 = 5500
            Assert.Equal(5000.0, activeCResAfterProp.Data.HsdClosingStock); // 5500 - 500 = 5000

            var activeAResAfterProp = await agsRepo.GetActiveShiftImportAsync(date1, "A");
            Assert.True(activeAResAfterProp.Success);
            Assert.Equal(5000.0, activeAResAfterProp.Data.HsdOpeningStock); // Shift C closing: 5000
            Assert.Equal(4500.0, activeAResAfterProp.Data.HsdClosingStock); // 5000 - 500 = 4500
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

            // Setup Shift B (Morning Opening Shift)
            await CreateMockImportAsync(agsRepo, date, "B", 2000.0);
            var groupsB = await inventoryService.BuildNozzleGroupsAsync(date, "B");
            var hsdGroupB = groupsB.First(g => g.GroupName.Contains("Tank 2"));
            hsdGroupB.OpeningStock = 8000.0;
            hsdGroupB.Dip = 120.0;
            await inventoryService.SaveAndPersistInventoryAsync(date, "B", groupsB);

            // Setup Shift C (Afternoon Shift)
            await CreateMockImportAsync(agsRepo, date, "C", 1000.0);
            var groupsC = await inventoryService.BuildNozzleGroupsAsync(date, "C");
            var hsdGroupC = groupsC.First(g => g.GroupName.Contains("Tank 2"));
            hsdGroupC.Dip = 115.0;
            await inventoryService.SaveAndPersistInventoryAsync(date, "C", groupsC);

            // Setup Shift A (Night Closing Shift)
            await CreateMockImportAsync(agsRepo, date, "A", 1000.0);
            var groupsA = await inventoryService.BuildNozzleGroupsAsync(date, "A");
            var hsdGroupA = groupsA.First(g => g.GroupName.Contains("Tank 2"));
            hsdGroupA.Dip = 110.0;
            await inventoryService.SaveAndPersistInventoryAsync(date, "A", groupsA);

            // Fetch daily summary
            var summaryRes = await agsRepo.GetDailySummaryAsync(date);
            Assert.True(summaryRes.Success);
            Assert.NotNull(summaryRes.Data);

            // Day Opening Stock must come from Shift B (8000L)
            Assert.Equal(8000.0, summaryRes.Data.HsdDayOpeningStock);
            
            // Day Closing Stock must come from Shift A (6000 - 1000 - 1000 = 4000L)
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
