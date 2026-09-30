using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Core.Repositories;
using FuelPro.Core.Services;
using FuelPro.Data;
using FuelPro.Data.Repositories;
using FuelPro.Data.Services;

namespace FuelPro.Tests;

public class RemoteSyncDataParityTests
{
    private (IServiceProvider ServiceProvider, DbContextOptions<FuelProDbContext> Options) CreateTestServices()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<FuelProDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddTransient<FuelProDbContext>(sp => new FuelProDbContext(options));
        services.AddTransient<IPaymentRepository, PaymentRepository>();
        services.AddTransient<IDsmEntryRepository, DsmEntryRepository>();
        services.AddTransient<INozzleReadingRepository, NozzleReadingRepository>();
        services.AddTransient<IDebitEntryRepository, DebitEntryRepository>();
        services.AddTransient<ITestingEntryRepository, TestingEntryRepository>();
        services.AddTransient<IExpenseRepository, ExpenseRepository>();
        services.AddTransient<ICashDenominationRepository, CashDenominationRepository>();
        services.AddTransient<IShiftRepository, ShiftRepository>();
        services.AddTransient<ISettingsRepository, SettingsRepository>();
        services.AddTransient<IDsmPersonalDebtorRepository, DsmPersonalDebtorRepository>();
        services.AddTransient<IDsmCalculationService, DsmCalculationService>();
        services.AddTransient<IOwnerCalculationService, OwnerCalculationService>();
        services.AddTransient<IFinancialCalculationService, FinancialCalculationService>();
        services.AddTransient<ITidCalculationService, TidCalculationService>();
        services.AddTransient<IShiftAggregationService, ShiftAggregationService>();
        services.AddTransient<IReportService, ReportService>();
        services.AddTransient<IStationConfigurationService, StationConfigurationService>();
        services.AddTransient<ICollectionTypeService, CollectionTypeService>();
        services.AddTransient<DsmEntryService>();

        var sp = services.BuildServiceProvider();
        return (sp, options);
    }

    [Fact]
    public async Task SavePaymentAsync_PopulatesDynamicItemsJson_AndReconstructsOnRemotePull()
    {
        var (sp, options) = CreateTestServices();
        var paymentRepo = sp.GetRequiredService<IPaymentRepository>();

        // Create initial entry and payment with dynamic items
        using (var db = new FuelProDbContext(options))
        {
            var shift = new Shift { ShiftDate = DateTime.Today, ShiftType = "I" };
            db.Shifts.Add(shift);
            await db.SaveChangesAsync();

            var entry = new DsmEntry { ShiftId = shift.ShiftId, PumpId = 1, DsmName = "Kira Yagami" };
            db.DsmEntries.Add(entry);
            await db.SaveChangesAsync();

            var payment = new PaymentCollection
            {
                DsmEntryId = entry.DsmEntryId,
                CashDeposit = 18000,
                Items = new List<PaymentCollectionItem>
                {
                    new() { CollectionTypeCode = "PHONEPE", Amount = 12875, Slot = "Morning" },
                    new() { CollectionTypeCode = "PINELAB_CARD", Amount = 14150, Slot = "Morning" },
                    new() { CollectionTypeCode = "PETROCARD", Amount = 16925, Slot = "Morning" },
                    new() { CollectionTypeCode = "SBI_REDEEM", Amount = 14900, Slot = "General" }
                }
            };

            var result = await paymentRepo.SavePaymentAsync(payment);
            Assert.True(result.Success);

            // Verify DynamicItemsJson was populated in DB
            var savedPayment = await db.PaymentCollections.FirstOrDefaultAsync(p => p.DsmEntryId == entry.DsmEntryId);
            Assert.NotNull(savedPayment);
            Assert.False(string.IsNullOrWhiteSpace(savedPayment!.DynamicItemsJson));
            Assert.Contains("PHONEPE", savedPayment.DynamicItemsJson);
            Assert.Contains("12875", savedPayment.DynamicItemsJson);
            Assert.Contains("SBI_REDEEM", savedPayment.DynamicItemsJson);

            // Simulate Remote Machine Sync Reconstruction from DynamicItemsJson
            var remoteItems = JsonConvert.DeserializeObject<List<PaymentCollectionItem>>(savedPayment.DynamicItemsJson);
            Assert.NotNull(remoteItems);
            Assert.Equal(4, remoteItems!.Count);
            Assert.Equal(12875, remoteItems.First(i => i.CollectionTypeCode == "PHONEPE").Amount);
            Assert.Equal(14150, remoteItems.First(i => i.CollectionTypeCode == "PINELAB_CARD").Amount);
            Assert.Equal(16925, remoteItems.First(i => i.CollectionTypeCode == "PETROCARD").Amount);
            Assert.Equal(14900, remoteItems.First(i => i.CollectionTypeCode == "SBI_REDEEM").Amount);
        }
    }

    [Fact]
    public async Task DynamicStationLayoutSync_EnsuresRemoteMachineCalculatesExactFuelTotals()
    {
        var (sp, options) = CreateTestServices();
        var reportService = sp.GetRequiredService<IReportService>();

        // Custom 4-pump layout (mapping Nozzle 7 and Nozzle 8 to MS - 20KL)
        var customLayout = new List<PumpMapping>
        {
            new() { PumpId = 1, NozzleNumber = 1, FuelType = "CNG", TankName = "CNG Line", IsActive = true },
            new() { PumpId = 1, NozzleNumber = 2, FuelType = "CNG", TankName = "CNG Line", IsActive = true },
            new() { PumpId = 2, NozzleNumber = 3, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true },
            new() { PumpId = 2, NozzleNumber = 4, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true },
            new() { PumpId = 3, NozzleNumber = 5, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true },
            new() { PumpId = 3, NozzleNumber = 6, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true },
            new() { PumpId = 4, NozzleNumber = 7, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true },
            new() { PumpId = 4, NozzleNumber = 8, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true }
        };

        var customTanks = new List<TankDefinition>
        {
            new() { TankName = "CNG Line", FuelType = "CNG", CapacityKL = 10, IsActive = true },
            new() { TankName = "HSD - 20KL", FuelType = "HSD", CapacityKL = 20, IsActive = true },
            new() { TankName = "MS - 20KL", FuelType = "MS-I", CapacityKL = 20, IsActive = true }
        };

        // Initialize PumpConfiguration from dynamic layout
        PumpConfiguration.InitializeFromDb(customLayout);
        PumpConfiguration.InitializeTanksFromDb(customTanks);

        using (var db = new FuelProDbContext(options))
        {
            db.TankDefinitions.AddRange(customTanks);
            db.PumpMappings.AddRange(customLayout);
            var date = new DateTime(2026, 8, 28);
            var shift = new Shift { ShiftDate = date, ShiftType = "I" };
            db.Shifts.Add(shift);
            await db.SaveChangesAsync();

            // Entry 1: Nozzle 1 (CNG: 1400L @ 85) + Nozzle 3 (HSD: 350L @ 99.15)
            var entry1 = new DsmEntry { ShiftId = shift.ShiftId, PumpId = 1, DsmName = "Kira Yagami", Shift = shift };
            db.DsmEntries.Add(entry1);
            await db.SaveChangesAsync();

            db.NozzleReadings.AddRange(
                new NozzleReading { DsmEntryId = entry1.DsmEntryId, NozzleNumber = 1, OpeningReading = 0, ClosingReading = 1400, SaleLitres = 1400, Rate = 85, Amount = 119000 },
                new NozzleReading { DsmEntryId = entry1.DsmEntryId, NozzleNumber = 3, OpeningReading = 0, ClosingReading = 350, SaleLitres = 350, Rate = 99.15, Amount = 34702.50 }
            );

            // Entry 2: Nozzle 5 (MS: 300L @ 108.52) + Nozzle 7 (MS: 175L @ 108.52)
            var entry2 = new DsmEntry { ShiftId = shift.ShiftId, PumpId = 4, DsmName = "Light Yagami", Shift = shift };
            db.DsmEntries.Add(entry2);
            await db.SaveChangesAsync();

            db.NozzleReadings.AddRange(
                new NozzleReading { DsmEntryId = entry2.DsmEntryId, NozzleNumber = 5, OpeningReading = 0, ClosingReading = 300, SaleLitres = 300, Rate = 108.52, Amount = 32556 },
                new NozzleReading { DsmEntryId = entry2.DsmEntryId, NozzleNumber = 7, OpeningReading = 0, ClosingReading = 175, SaleLitres = 175, Rate = 108.52, Amount = 18991 }
            );

            await db.SaveChangesAsync();

            var entries = await db.DsmEntries
                .Include(e => e.Shift)
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                    .ThenInclude(p => p.Items)
                .ToListAsync();

            // Run ReportService Day Report Calculation
            var report = reportService.CalculateDayReport(date, date, entries, new List<Expense>(), new List<CreditorRepayment>(), 99.15, 108.52, 108.52, 85, "Kandhare Petroleum");
            Assert.NotNull(report);

            // Total litres = 1400 + 350 + 300 + 175 = 2225 L
            var totalLitres = report.FuelSales.Sum(f => f.Litres);
            Assert.Equal(2225, totalLitres);

            // Fuel Sales breakdown
            var msSale = report.FuelSales.FirstOrDefault(f => f.Description.Contains("MS - 20KL"));
            Assert.NotNull(msSale);
            // 300 + 175 = 475 L (Should NOT be split into HSD - 20KL II!)
            Assert.Equal(475, msSale!.Litres);

            var hsdSale = report.FuelSales.FirstOrDefault(f => f.Description.Contains("HSD - 20KL"));
            Assert.NotNull(hsdSale);
            Assert.Equal(350, hsdSale!.Litres);

            var cngSale = report.FuelSales.FirstOrDefault(f => f.Description.Contains("CNG Line"));
            Assert.NotNull(cngSale);
            Assert.Equal(1400, cngSale!.Litres);

            // Verify no phantom HSD - 20KL II tank exists
            Assert.Null(report.FuelSales.FirstOrDefault(f => f.Description.Contains("HSD - 20KL II")));
        }
    }

    [Fact]
    public async Task ConnectedPumpDsmMismatchParity_MatchesLocalDashboardExactTotals()
    {
        PumpConfiguration.ResetToDefaults();
        var (sp, options) = CreateTestServices();
        var reportService = sp.GetRequiredService<IReportService>();
        var aggregationService = sp.GetRequiredService<IShiftAggregationService>();

        var kandhareTanks = new List<TankDefinition>
        {
            new() { TankName = "CNG Line", FuelType = "CNG", CapacityKL = 10, IsActive = true },
            new() { TankName = "HSD - 20KL", FuelType = "HSD", CapacityKL = 20, IsActive = true },
            new() { TankName = "MS - 20KL", FuelType = "MS-I", CapacityKL = 20, IsActive = true },
            new() { TankName = "SPEED - 20KL", FuelType = "SPEED", CapacityKL = 20, IsActive = true }
        };

        var kandhareMappings = new List<PumpMapping>
        {
            new() { PumpId = 1, NozzleNumber = 1, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true },
            new() { PumpId = 1, NozzleNumber = 2, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true },
            new() { PumpId = 1, NozzleNumber = 3, FuelType = "SPEED", TankName = "SPEED - 20KL", IsActive = true },
            new() { PumpId = 2, NozzleNumber = 4, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true },
            new() { PumpId = 2, NozzleNumber = 5, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true },
            new() { PumpId = 2, NozzleNumber = 6, FuelType = "SPEED", TankName = "SPEED - 20KL", IsActive = true },
            new() { PumpId = 3, NozzleNumber = 7, FuelType = "CNG", TankName = "CNG Line", IsActive = true },
            new() { PumpId = 4, NozzleNumber = 8, FuelType = "CNG", TankName = "CNG Line", IsActive = true }
        };

        PumpConfiguration.InitializeFromDb(kandhareMappings);
        PumpConfiguration.InitializeTanksFromDb(kandhareTanks);

        using (var db = new FuelProDbContext(options))
        {
            db.TankDefinitions.AddRange(kandhareTanks);
            db.PumpMappings.AddRange(kandhareMappings);

            var date = new DateTime(2026, 8, 28);
            var shift1 = new Shift { ShiftId = 1, ShiftDate = date, ShiftType = "A" };
            var shift2 = new Shift { ShiftId = 2, ShiftDate = date, ShiftType = "B" };
            db.Shifts.AddRange(shift1, shift2);
            await db.SaveChangesAsync();

            // Shift 1 entries
            var e1 = new DsmEntry { DsmEntryId = 1, ShiftId = 1, DsmName = "LIGHT YAGAMI", PumpId = 1, ConnectedPumpId = 2, ReconciledToPumpId = null, GrossSales = 22134.20m, TotalCollection = 22125.60m, Mismatch = -8.60m, Shift = shift1 };
            var e5 = new DsmEntry { DsmEntryId = 5, ShiftId = 1, DsmName = "KIRA YAGAMI", PumpId = 1, ConnectedPumpId = 2, ReconciledToPumpId = null, GrossSales = 24239.80m, TotalCollection = 24235.00m, Mismatch = -4.80m, Shift = shift1 };
            var e7 = new DsmEntry { DsmEntryId = 7, ShiftId = 1, DsmName = "KIRA YAGAMI", PumpId = 2, ConnectedPumpId = null, ReconciledToPumpId = 3, GrossSales = 7482.50m, TotalCollection = 0m, Mismatch = 0m, Shift = shift1 };
            var e8 = new DsmEntry { DsmEntryId = 8, ShiftId = 1, DsmName = "LIGHT YAGAMI", PumpId = 2, ConnectedPumpId = null, ReconciledToPumpId = 1, GrossSales = 15704.50m, TotalCollection = 0m, Mismatch = 0m, Shift = shift1 };
            var e9 = new DsmEntry { DsmEntryId = 9, ShiftId = 1, DsmName = "LIGHT YAGAMI", PumpId = 1, ConnectedPumpId = 2, ReconciledToPumpId = null, GrossSales = 39877.50m, TotalCollection = 39880.00m, Mismatch = 2.50m, Shift = shift1 };
            var e10 = new DsmEntry { DsmEntryId = 10, ShiftId = 1, DsmName = "LIGHT YAGAMI", PumpId = 2, ConnectedPumpId = null, ReconciledToPumpId = 5, GrossSales = 31532.25m, TotalCollection = 0m, Mismatch = 0m, Shift = shift1 };

            // Shift 2 entries
            var e2 = new DsmEntry { DsmEntryId = 2, ShiftId = 2, DsmName = "LIGHT YAGAMI", PumpId = 3, ConnectedPumpId = 4, ReconciledToPumpId = null, GrossSales = 34000.00m, TotalCollection = 34000.00m, Mismatch = 0m, Shift = shift2 };
            var e3 = new DsmEntry { DsmEntryId = 3, ShiftId = 2, DsmName = "LIGHT YAGAMI", PumpId = 4, ConnectedPumpId = null, ReconciledToPumpId = 9, GrossSales = 12750.00m, TotalCollection = 0m, Mismatch = 0m, Shift = shift2 };
            var e4 = new DsmEntry { DsmEntryId = 4, ShiftId = 2, DsmName = "KIRA YAGAMI", PumpId = 3, ConnectedPumpId = 4, ReconciledToPumpId = null, GrossSales = 59500.00m, TotalCollection = 59503.00m, Mismatch = 3.00m, Shift = shift2 };
            var e6 = new DsmEntry { DsmEntryId = 6, ShiftId = 2, DsmName = "KIRA YAGAMI", PumpId = 4, ConnectedPumpId = null, ReconciledToPumpId = 11, GrossSales = 29750.00m, TotalCollection = 0m, Mismatch = 0m, Shift = shift2 };
            var e11 = new DsmEntry { DsmEntryId = 11, ShiftId = 2, DsmName = "KIRA YAGAMI", PumpId = 3, ConnectedPumpId = 4, ReconciledToPumpId = null, GrossSales = 25500.00m, TotalCollection = 25480.00m, Mismatch = -20.00m, Shift = shift2 };
            var e12 = new DsmEntry { DsmEntryId = 12, ShiftId = 2, DsmName = "KIRA YAGAMI", PumpId = 4, ConnectedPumpId = null, ReconciledToPumpId = 7, GrossSales = 17000.00m, TotalCollection = 0m, Mismatch = 0m, Shift = shift2 };

            db.DsmEntries.AddRange(e1, e2, e3, e4, e5, e6, e7, e8, e9, e10, e11, e12);
            await db.SaveChangesAsync();

            // Nozzle Readings
            db.NozzleReadings.AddRange(
                new NozzleReading { DsmEntryId = 1, NozzleNumber = 1, OpeningReading = 0, ClosingReading = 10, SaleLitres = 10, Rate = 90.35, Amount = 903.50, FuelType = "HSD" },
                new NozzleReading { DsmEntryId = 1, NozzleNumber = 2, OpeningReading = 0, ClosingReading = 20, SaleLitres = 20, Rate = 103.81, Amount = 2076.20, FuelType = "MS-I" },
                new NozzleReading { DsmEntryId = 1, NozzleNumber = 3, OpeningReading = 0, ClosingReading = 30, SaleLitres = 30, Rate = 115, Amount = 3450, FuelType = "SPEED" },

                new NozzleReading { DsmEntryId = 5, NozzleNumber = 1, OpeningReading = 10, ClosingReading = 75, SaleLitres = 65, Rate = 90.35, Amount = 5872.75, FuelType = "HSD" },
                new NozzleReading { DsmEntryId = 5, NozzleNumber = 2, OpeningReading = 20, ClosingReading = 75, SaleLitres = 55, Rate = 103.81, Amount = 5709.55, FuelType = "MS-I" },
                new NozzleReading { DsmEntryId = 5, NozzleNumber = 3, OpeningReading = 30, ClosingReading = 75, SaleLitres = 45, Rate = 115, Amount = 5175, FuelType = "SPEED" },

                new NozzleReading { DsmEntryId = 7, NozzleNumber = 4, OpeningReading = 40, ClosingReading = 75, SaleLitres = 35, Rate = 90.35, Amount = 3162.25, FuelType = "HSD" },
                new NozzleReading { DsmEntryId = 7, NozzleNumber = 5, OpeningReading = 50, ClosingReading = 75, SaleLitres = 25, Rate = 103.81, Amount = 2595.25, FuelType = "MS-II" },
                new NozzleReading { DsmEntryId = 7, NozzleNumber = 6, OpeningReading = 60, ClosingReading = 75, SaleLitres = 15, Rate = 115, Amount = 1725, FuelType = "SPEED" },

                new NozzleReading { DsmEntryId = 8, NozzleNumber = 4, OpeningReading = 0, ClosingReading = 40, SaleLitres = 40, Rate = 90.35, Amount = 3614, FuelType = "HSD" },
                new NozzleReading { DsmEntryId = 8, NozzleNumber = 5, OpeningReading = 0, ClosingReading = 50, SaleLitres = 50, Rate = 103.81, Amount = 5190.50, FuelType = "MS-II" },
                new NozzleReading { DsmEntryId = 8, NozzleNumber = 6, OpeningReading = 0, ClosingReading = 60, SaleLitres = 60, Rate = 115, Amount = 6900, FuelType = "SPEED" },

                new NozzleReading { DsmEntryId = 9, NozzleNumber = 1, OpeningReading = 75, ClosingReading = 75, SaleLitres = 0, Rate = 90.35, Amount = 0, FuelType = "HSD" },
                new NozzleReading { DsmEntryId = 9, NozzleNumber = 2, OpeningReading = 75, ClosingReading = 100, SaleLitres = 25, Rate = 103.81, Amount = 2595.25, FuelType = "MS-I" },
                new NozzleReading { DsmEntryId = 9, NozzleNumber = 3, OpeningReading = 75, ClosingReading = 125, SaleLitres = 50, Rate = 115, Amount = 5750, FuelType = "SPEED" },

                new NozzleReading { DsmEntryId = 10, NozzleNumber = 4, OpeningReading = 75, ClosingReading = 150, SaleLitres = 75, Rate = 90.35, Amount = 6776.25, FuelType = "HSD" },
                new NozzleReading { DsmEntryId = 10, NozzleNumber = 5, OpeningReading = 75, ClosingReading = 175, SaleLitres = 100, Rate = 103.81, Amount = 10381, FuelType = "MS-II" },
                new NozzleReading { DsmEntryId = 10, NozzleNumber = 6, OpeningReading = 75, ClosingReading = 200, SaleLitres = 125, Rate = 115, Amount = 14375, FuelType = "SPEED" },

                new NozzleReading { DsmEntryId = 2, NozzleNumber = 7, OpeningReading = 100, ClosingReading = 350, SaleLitres = 250, Rate = 85, Amount = 21250, FuelType = "CNG" },
                new NozzleReading { DsmEntryId = 3, NozzleNumber = 8, OpeningReading = 200, ClosingReading = 350, SaleLitres = 150, Rate = 85, Amount = 12750, FuelType = "CNG" },

                new NozzleReading { DsmEntryId = 4, NozzleNumber = 7, OpeningReading = 350, ClosingReading = 700, SaleLitres = 350, Rate = 85, Amount = 29750, FuelType = "CNG" },
                new NozzleReading { DsmEntryId = 6, NozzleNumber = 8, OpeningReading = 350, ClosingReading = 700, SaleLitres = 350, Rate = 85, Amount = 29750, FuelType = "CNG" },

                new NozzleReading { DsmEntryId = 11, NozzleNumber = 7, OpeningReading = 0, ClosingReading = 100, SaleLitres = 100, Rate = 85, Amount = 8500, FuelType = "CNG" },
                new NozzleReading { DsmEntryId = 12, NozzleNumber = 8, OpeningReading = 0, ClosingReading = 200, SaleLitres = 200, Rate = 85, Amount = 17000, FuelType = "CNG" }
            );

            // Payments
            var p1 = new PaymentCollection { DsmEntryId = 1, PhonePeMorning = 250, CreditCardMorning = 500, PetroCardMorning = 750 };
            var p5 = new PaymentCollection { DsmEntryId = 5, PhonePeMorning = 1000, CreditCardMorning = 1500, PetroCardMorning = 2000 };
            var p9 = new PaymentCollection { DsmEntryId = 9, PhonePeMorning = 1000, CreditCardMorning = 2000, PetroCardMorning = 3000 };
            var p2 = new PaymentCollection { DsmEntryId = 2, PhonePeDay = 500, CreditCardDay = 1000, PetroCardDay = 3000 };
            var p4 = new PaymentCollection { DsmEntryId = 4, PhonePeDay = 10000, CreditCardDay = 9000, PetroCardDay = 8000 };
            var p11 = new PaymentCollection { DsmEntryId = 11, PhonePeDay = 125, CreditCardDay = 150, PetroCardDay = 175 };

            db.PaymentCollections.AddRange(p1, p5, p9, p2, p4, p11);
            await db.SaveChangesAsync();

            db.PaymentCollectionItems.AddRange(
                new PaymentCollectionItem { PaymentId = p1.PaymentId, CollectionTypeCode = "SBI_REDEEM", Amount = 1000 },
                new PaymentCollectionItem { PaymentId = p1.PaymentId, CollectionTypeCode = "PAYTM", Amount = 1250 },
                new PaymentCollectionItem { PaymentId = p1.PaymentId, CollectionTypeCode = "QR", Amount = 1500 },
                new PaymentCollectionItem { PaymentId = p1.PaymentId, CollectionTypeCode = "MOBIKWIK", Amount = 1750 },

                new PaymentCollectionItem { PaymentId = p5.PaymentId, CollectionTypeCode = "SBI_REDEEM", Amount = 2500 },
                new PaymentCollectionItem { PaymentId = p5.PaymentId, CollectionTypeCode = "PAYTM", Amount = 3000 },
                new PaymentCollectionItem { PaymentId = p5.PaymentId, CollectionTypeCode = "QR", Amount = 3500 },
                new PaymentCollectionItem { PaymentId = p5.PaymentId, CollectionTypeCode = "MOBIKWIK", Amount = 4000 },

                new PaymentCollectionItem { PaymentId = p9.PaymentId, CollectionTypeCode = "SBI_REDEEM", Amount = 4000 },
                new PaymentCollectionItem { PaymentId = p9.PaymentId, CollectionTypeCode = "PAYTM", Amount = 5000 },
                new PaymentCollectionItem { PaymentId = p9.PaymentId, CollectionTypeCode = "QR", Amount = 6000 },
                new PaymentCollectionItem { PaymentId = p9.PaymentId, CollectionTypeCode = "MOBIKWIK", Amount = 7000 },

                new PaymentCollectionItem { PaymentId = p2.PaymentId, CollectionTypeCode = "SBI_REDEEM", Amount = 200 },
                new PaymentCollectionItem { PaymentId = p2.PaymentId, CollectionTypeCode = "PAYTM", Amount = 250 },
                new PaymentCollectionItem { PaymentId = p2.PaymentId, CollectionTypeCode = "QR", Amount = 700 },
                new PaymentCollectionItem { PaymentId = p2.PaymentId, CollectionTypeCode = "MOBIKWIK", Amount = 1000 },

                new PaymentCollectionItem { PaymentId = p4.PaymentId, CollectionTypeCode = "SBI_REDEEM", Amount = 7000 },
                new PaymentCollectionItem { PaymentId = p4.PaymentId, CollectionTypeCode = "PAYTM", Amount = 6000 },
                new PaymentCollectionItem { PaymentId = p4.PaymentId, CollectionTypeCode = "QR", Amount = 5000 },
                new PaymentCollectionItem { PaymentId = p4.PaymentId, CollectionTypeCode = "MOBIKWIK", Amount = 4000 },

                new PaymentCollectionItem { PaymentId = p11.PaymentId, CollectionTypeCode = "SBI_REDEEM", Amount = 200 },
                new PaymentCollectionItem { PaymentId = p11.PaymentId, CollectionTypeCode = "PAYTM", Amount = 225 },
                new PaymentCollectionItem { PaymentId = p11.PaymentId, CollectionTypeCode = "QR", Amount = 250 },
                new PaymentCollectionItem { PaymentId = p11.PaymentId, CollectionTypeCode = "MOBIKWIK", Amount = 275 }
            );

            // Cash Denominations
            db.CashDenominations.AddRange(
                new CashDenomination { DsmEntryId = 1, CashType = "Cash1", TotalAmount = 800 },
                new CashDenomination { DsmEntryId = 1, CashType = "Cash2", TotalAmount = 420 },
                new CashDenomination { DsmEntryId = 5, CashType = "Cash1", TotalAmount = 2000 },
                new CashDenomination { DsmEntryId = 5, CashType = "Cash2", TotalAmount = 4185 },
                new CashDenomination { DsmEntryId = 9, CashType = "Cash1", TotalAmount = 4000 },
                new CashDenomination { DsmEntryId = 9, CashType = "Cash2", TotalAmount = 7380 },
                new CashDenomination { DsmEntryId = 2, CashType = "Cash1", TotalAmount = 16000 },
                new CashDenomination { DsmEntryId = 2, CashType = "Cash2", TotalAmount = 11350 },
                new CashDenomination { DsmEntryId = 4, CashType = "Cash1", TotalAmount = 4000 },
                new CashDenomination { DsmEntryId = 4, CashType = "Cash2", TotalAmount = 6353 },
                new CashDenomination { DsmEntryId = 11, CashType = "Cash1", TotalAmount = 12000 },
                new CashDenomination { DsmEntryId = 11, CashType = "Cash2", TotalAmount = 12080 }
            );

            // Debits, Expenses, Testing
            db.DebitEntries.AddRange(
                new DebitEntry { DsmEntryId = 1, DebtorName = "RASHTRA TECH", Amount = 500 },
                new DebitEntry { DsmEntryId = 1, DebtorName = "RUSHII", Amount = 10005 },
                new DebitEntry { DsmEntryId = 5, DebtorName = "RASHTRA TECH", Amount = 500 },
                new DebitEntry { DsmEntryId = 9, DebtorName = "RUSHII", Amount = 500 }
            );

            db.Expenses.AddRange(
                new Expense { DsmEntryId = 1, Description = "PIZZA", Amount = 309 },
                new Expense { DsmEntryId = 5, Description = "TEA", Amount = 50 },
                new Expense { DsmEntryId = 4, Description = "LUNCH", Amount = 150 }
            );

            db.TestingEntries.AddRange(
                new TestingEntry { DsmEntryId = 1, Amount = 451.75 },
                new TestingEntry { DsmEntryId = 1, Amount = 519.05 },
                new TestingEntry { DsmEntryId = 1, Amount = 575 },
                new TestingEntry { DsmEntryId = 1, Amount = 451.75 },
                new TestingEntry { DsmEntryId = 1, Amount = 519.05 },
                new TestingEntry { DsmEntryId = 1, Amount = 575 }
            );

            await db.SaveChangesAsync();

            var allEntries = await db.DsmEntries
                .Include(e => e.Shift)
                .Include(e => e.NozzleReadings)
                .Include(e => e.PaymentCollection)
                    .ThenInclude(p => p.Items)
                .Include(e => e.CashDenominations)
                .Include(e => e.DebitEntries)
                .Include(e => e.Expenses)
                .Include(e => e.TestingEntries)
                .ToListAsync();

            var summaryRows = aggregationService.BuildDsmSummaryRows(allEntries);
            var shiftTotals = aggregationService.BuildDsmShiftTotals(summaryRows);

            var kira = shiftTotals.FirstOrDefault(r => r.DsmName.Equals("KIRA YAGAMI", StringComparison.OrdinalIgnoreCase));
            var light = shiftTotals.FirstOrDefault(r => r.DsmName.Equals("LIGHT YAGAMI", StringComparison.OrdinalIgnoreCase));

            Assert.NotNull(kira);
            Assert.NotNull(light);

            Assert.Equal(3, kira!.SessionsCount);
            Assert.Equal(3, light!.SessionsCount);

            // Kira Expected: Gross 1,09,239.80, Collection 1,09,218.00, Mismatch -21.80
            Assert.Equal(109239.80, Math.Round(kira.GrossSales, 2));
            Assert.Equal(109218.00, Math.Round(kira.TotalCollection, 2));
            Assert.Equal(-21.80, Math.Round(kira.Mismatch, 2));

            // Light Expected: Gross 96,011.70, Testing 3,091.60, Net Gross 92,920.10, Collection 92,914.00, Mismatch -6.10
            Assert.Equal(96011.70, Math.Round(light.GrossSales, 2));
            Assert.Equal(92920.10, Math.Round(light.NetGrossSales, 2));
            Assert.Equal(92914.00, Math.Round(light.TotalCollection, 2));
            Assert.Equal(-6.10, Math.Round(light.Mismatch, 2));

            // Verify Day Report Fuel breakdown
            var dayReport = reportService.CalculateDayReport(
                new DateTime(2026, 8, 28), new DateTime(2026, 8, 28),
                allEntries,
                await db.Expenses.ToListAsync(),
                new List<CreditorRepayment>(),
                90.35, 108.52, 103.81, 85.00,
                "Kandhare Petroleum"
            );

            // Monthly Performance fuel classification
            double dayDiesel = dayReport.FuelSales
                .Where(f => string.Equals(f.FuelType, "HSD", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(f.FuelType, "Diesel", StringComparison.OrdinalIgnoreCase) ||
                            (f.Description != null && f.Description.Contains("HSD", StringComparison.OrdinalIgnoreCase)))
                .Sum(f => f.Litres);

            double daySpeed = dayReport.FuelSales
                .Where(f => string.Equals(f.FuelType, "SPEED", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(f.FuelType, "MS-II", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(f.FuelType, "Power", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(f.FuelType, "XP", StringComparison.OrdinalIgnoreCase) ||
                            (f.Description != null && (f.Description.Contains("SPEED", StringComparison.OrdinalIgnoreCase) ||
                                                       f.Description.Contains("XP", StringComparison.OrdinalIgnoreCase) ||
                                                       f.Description.Contains("Power", StringComparison.OrdinalIgnoreCase) ||
                                                       f.Description.Contains("20KL II", StringComparison.OrdinalIgnoreCase))))
                .Sum(f => f.Litres);

            double dayPetrol = dayReport.FuelSales
                .Where(f => (string.Equals(f.FuelType, "MS-I", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(f.FuelType, "MS", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(f.FuelType, "Petrol", StringComparison.OrdinalIgnoreCase) ||
                             (f.Description != null && (f.Description.Contains("MS", StringComparison.OrdinalIgnoreCase) || f.Description.Contains("Petrol", StringComparison.OrdinalIgnoreCase))))
                            && !string.Equals(f.FuelType, "SPEED", StringComparison.OrdinalIgnoreCase)
                            && !(f.Description != null && (f.Description.Contains("SPEED", StringComparison.OrdinalIgnoreCase) || f.Description.Contains("20KL II", StringComparison.OrdinalIgnoreCase) || f.Description.Contains("XP", StringComparison.OrdinalIgnoreCase) || f.Description.Contains("Power", StringComparison.OrdinalIgnoreCase))))
                .Sum(f => f.Litres);

            double dayCng = dayReport.FuelSales
                .Where(f => string.Equals(f.FuelType, "CNG", StringComparison.OrdinalIgnoreCase) ||
                            (f.Description != null && f.Description.Contains("CNG", StringComparison.OrdinalIgnoreCase)))
                .Sum(f => f.Litres);

            Assert.Equal(225.00, dayDiesel);
            Assert.Equal(275.00, dayPetrol);
            Assert.Equal(325.00, daySpeed);
            Assert.Equal(1400.00, dayCng);
            Assert.Equal(2225.00, dayReport.TotalFuelLitres);

            // Collection Summary Total Digital verification (including "Credit / Debit Card" / "PineLab Card")
            var modeTotals = new List<(string Mode, double Amount)>();
            foreach (var cat in dayReport.CollectionBreakdown)
            {
                if (cat.Category.Contains("Testing") || cat.Category.Equals("Expenses") || cat.Category.Contains("DSM Short") || cat.Category.Contains("Kandhare"))
                    continue;
                modeTotals.Add((cat.Category, cat.Amount));
            }

            double totalDigital = modeTotals
                .Where(m => !m.Mode.Contains("Cash", StringComparison.OrdinalIgnoreCase) &&
                            !m.Mode.Contains("Debtor", StringComparison.OrdinalIgnoreCase) &&
                            (!m.Mode.Contains("Debit", StringComparison.OrdinalIgnoreCase) || m.Mode.Contains("Card", StringComparison.OrdinalIgnoreCase) || m.Mode.Contains("PineLab", StringComparison.OrdinalIgnoreCase)) &&
                            !m.Mode.Contains("(Record)", StringComparison.OrdinalIgnoreCase) &&
                            !m.Mode.Contains("Others", StringComparison.OrdinalIgnoreCase) &&
                            !m.Mode.Contains("Other", StringComparison.OrdinalIgnoreCase))
                .Sum(m => m.Amount);

            Assert.Equal(43950.00, totalDigital);
            Assert.True(dayReport.ActualCollection > 0);
        }
    }

    [Fact]
    public async Task KandharePetroleum_RemoteSync_FullParity_MatchesLocalAdminMachine()
    {
        var (sp, options) = CreateTestServices();
        var reportService = sp.GetRequiredService<IReportService>();
        var shiftAggService = sp.GetRequiredService<IShiftAggregationService>();

        using (var db = new FuelProDbContext(options))
        {
            // 1. Simulate Fresh Install Seed Data on Remote Machine (Default seed layout)
            db.TankDefinitions.AddRange(new[]
            {
                new TankDefinition { TankName = "MS - 20KL", FuelType = "MS-I", CapacityKL = 20, IsActive = true, HasTesting = true },
                new TankDefinition { TankName = "HSD - 20KL", FuelType = "HSD", CapacityKL = 20, IsActive = true, HasTesting = true },
                new TankDefinition { TankName = "HSD - 20KL II", FuelType = "MS-II", CapacityKL = 20, IsActive = true, HasTesting = true }, // Phantom seed tank
                new TankDefinition { TankName = "CNG Line", FuelType = "CNG", CapacityKL = 0, IsActive = true, HasTesting = false }
            });

            db.CollectionTypes.AddRange(new[]
            {
                new CollectionTypeMaster { Code = "CREDIT_CARD", DisplayName = "Credit / Debit Card", Category = "Card", IsActive = true, IsSystem = true },
                new CollectionTypeMaster { Code = "PHONEPE", DisplayName = "PhonePe", Category = "UPI", IsActive = true, IsSystem = true },
                new CollectionTypeMaster { Code = "PETROCARD", DisplayName = "PetroCard", Category = "Fleet", IsActive = true, IsSystem = true }
            });

            await db.SaveChangesAsync();

            // 2. Simulate Remote Cloud Pull of Station Settings & Layout from Admin Machine
            var adminTanks = new List<TankDefinition>
            {
                new TankDefinition { TankName = "CNG Line", FuelType = "CNG", CapacityKL = 0, IsActive = true, HasTesting = false },
                new TankDefinition { TankName = "HSD - 20KL", FuelType = "HSD", CapacityKL = 20, IsActive = true, HasTesting = true },
                new TankDefinition { TankName = "MS - 20KL", FuelType = "MS-I", CapacityKL = 20, IsActive = true, HasTesting = true },
                new TankDefinition { TankName = "MS - 20KL II", FuelType = "MS-II", CapacityKL = 20, IsActive = true, HasTesting = true },
                new TankDefinition { TankName = "SPEED - 20KL", FuelType = "SPEED", CapacityKL = 20, IsActive = true, HasTesting = true }
            };

            var adminMappings = new List<PumpMapping>
            {
                new PumpMapping { PumpId = 1, NozzleNumber = 1, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true },
                new PumpMapping { PumpId = 1, NozzleNumber = 2, FuelType = "MS-I", TankName = "MS - 20KL", IsActive = true },
                new PumpMapping { PumpId = 1, NozzleNumber = 3, FuelType = "SPEED", TankName = "SPEED - 20KL", IsActive = true },
                new PumpMapping { PumpId = 2, NozzleNumber = 4, FuelType = "HSD", TankName = "HSD - 20KL", IsActive = true },
                new PumpMapping { PumpId = 2, NozzleNumber = 5, FuelType = "MS-II", TankName = "MS - 20KL II", IsActive = true },
                new PumpMapping { PumpId = 2, NozzleNumber = 6, FuelType = "SPEED", TankName = "SPEED - 20KL", IsActive = true }
            };

            var adminCollectionTypes = new List<CollectionTypeMaster>
            {
                new CollectionTypeMaster { Code = "PINELAB_CARD", DisplayName = "PineLab Card", Category = "Card", IsActive = true, IsSystem = true },
                new CollectionTypeMaster { Code = "PHONEPE", DisplayName = "PhonePe", Category = "UPI", IsActive = true, IsSystem = true },
                new CollectionTypeMaster { Code = "PETROCARD", DisplayName = "PetroCard", Category = "Fleet", IsActive = true, IsSystem = true },
                new CollectionTypeMaster { Code = "SBI_REDEEM", DisplayName = "SBI Redeem", Category = "Fleet", IsActive = true },
                new CollectionTypeMaster { Code = "PAYTM", DisplayName = "Paytm", Category = "UPI", IsActive = true },
                new CollectionTypeMaster { Code = "QR", DisplayName = "QR / Online", Category = "UPI", IsActive = true },
                new CollectionTypeMaster { Code = "MOBIKWIK", DisplayName = "Mobikwik", Category = "Wallet", IsActive = true },
                new CollectionTypeMaster { Code = "OTHERS", DisplayName = "Other Online / UPI", Category = "Other", IsActive = true }
            };

            var setting = new Setting
            {
                PumpStationName = "Kandhare Petroleum",
                HsdRate = 90.35,
                MsIRate = 103.81,
                MsIIRate = 103.81,
                CngRate = 85.00,
                FuelRatesJson = JsonConvert.SerializeObject(new Dictionary<string, double>
                {
                    { "CNG Line", 85.00 },
                    { "HSD - 20KL", 90.35 },
                    { "MS - 20KL", 103.81 },
                    { "MS - 20KL II", 103.81 },
                    { "SPEED - 20KL", 115.00 }
                }),
                TankDefinitionsJson = JsonConvert.SerializeObject(adminTanks),
                PumpMappingsJson = JsonConvert.SerializeObject(adminMappings),
                CollectionTypesJson = JsonConvert.SerializeObject(adminCollectionTypes)
            };
            db.Settings.Add(setting);
            await db.SaveChangesAsync();

            // Simulate settings deserialization logic from SyncEngine
            var existingTanks = await db.TankDefinitions.ToListAsync();
            var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in adminTanks)
            {
                var existingTank = existingTanks.FirstOrDefault(x => string.Equals(x.TankName, t.TankName, StringComparison.OrdinalIgnoreCase));
                if (existingTank != null)
                {
                    existingTank.CapacityKL = t.CapacityKL;
                    existingTank.FuelType = t.FuelType;
                    existingTank.IsActive = t.IsActive;
                    existingTank.HasTesting = t.HasTesting;
                    processedNames.Add(existingTank.TankName);
                }
                else
                {
                    db.TankDefinitions.Add(new TankDefinition
                    {
                        TankName = t.TankName,
                        CapacityKL = t.CapacityKL,
                        FuelType = t.FuelType,
                        IsActive = t.IsActive,
                        HasTesting = t.HasTesting,
                        CreatedAt = DateTime.Now
                    });
                    processedNames.Add(t.TankName);
                }
            }
            foreach (var existing in existingTanks)
            {
                if (!processedNames.Contains(existing.TankName) &&
                    !adminTanks.Any(t => string.Equals(t.TankName, existing.TankName, StringComparison.OrdinalIgnoreCase)))
                {
                    db.TankDefinitions.Remove(existing);
                }
            }

            // Sync collection types
            var existingCts = await db.CollectionTypes.ToListAsync();
            foreach (var ct in adminCollectionTypes)
            {
                var existingCt = existingCts.FirstOrDefault(x => string.Equals(x.Code, ct.Code, StringComparison.OrdinalIgnoreCase));
                if (existingCt != null)
                {
                    existingCt.DisplayName = ct.DisplayName;
                    existingCt.Category = ct.Category;
                    existingCt.IsActive = ct.IsActive;
                }
                else
                {
                    db.CollectionTypes.Add(ct);
                }
            }

            // Sync pump mappings
            db.PumpMappings.AddRange(adminMappings);
            await db.SaveChangesAsync();

            // Initialize in-memory configuration
            PumpConfiguration.InitializeFromDb(await db.PumpMappings.ToListAsync());
            PumpConfiguration.InitializeTanksFromDb(await db.TankDefinitions.ToListAsync());

            // 3. Create Shift & DSM Entry
            var shift = new Shift { ShiftDate = new DateTime(2026, 8, 29), ShiftType = "I" };
            db.Shifts.Add(shift);
            await db.SaveChangesAsync();

            var dsmEntry = new DsmEntry
            {
                ShiftId = shift.ShiftId,
                PumpId = 1,
                DsmName = "Kira Yagami",
                GrossSales = 52870.50M,
                TotalCollection = 52871.60M,
                Mismatch = 1.10M
            };
            db.DsmEntries.Add(dsmEntry);
            await db.SaveChangesAsync();

            // Nozzle readings:
            // N1 HSD: 225 L @ 90.35 = 20,328.75
            // N2 MS: 125 L @ 103.81 = 12,976.25
            // N3 SPEED: 100 L @ 115.00 = 11,500.00
            // N5 MS-II: 50 L @ 103.81 = 5,190.50
            // N6 SPEED: 25 L @ 115.00 = 2,875.00
            db.NozzleReadings.AddRange(new[]
            {
                new NozzleReading { DsmEntryId = dsmEntry.DsmEntryId, NozzleNumber = 1, FuelType = "HSD", SaleLitres = 225.00, Rate = 90.35, Amount = 20328.75 },
                new NozzleReading { DsmEntryId = dsmEntry.DsmEntryId, NozzleNumber = 2, FuelType = "MS-I", SaleLitres = 125.00, Rate = 103.81, Amount = 12976.25 },
                new NozzleReading { DsmEntryId = dsmEntry.DsmEntryId, NozzleNumber = 3, FuelType = "SPEED", SaleLitres = 100.00, Rate = 115.00, Amount = 11500.00 },
                new NozzleReading { DsmEntryId = dsmEntry.DsmEntryId, NozzleNumber = 5, FuelType = "MS-II", SaleLitres = 50.00, Rate = 103.81, Amount = 5190.50 },
                new NozzleReading { DsmEntryId = dsmEntry.DsmEntryId, NozzleNumber = 6, FuelType = "SPEED", SaleLitres = 25.00, Rate = 115.00, Amount = 2875.00 }
            });

            // Testing entries (Total = 3,091.60)
            db.TestingEntries.AddRange(new[]
            {
                new TestingEntry { DsmEntryId = dsmEntry.DsmEntryId, FuelType = "1", Litres = 5, Rate = 90.35, Amount = 451.75 },
                new TestingEntry { DsmEntryId = dsmEntry.DsmEntryId, FuelType = "2", Litres = 5, Rate = 103.81, Amount = 519.05 },
                new TestingEntry { DsmEntryId = dsmEntry.DsmEntryId, FuelType = "3", Litres = 5, Rate = 115.00, Amount = 575.00 },
                new TestingEntry { DsmEntryId = dsmEntry.DsmEntryId, FuelType = "4", Litres = 5, Rate = 90.35, Amount = 451.75 },
                new TestingEntry { DsmEntryId = dsmEntry.DsmEntryId, FuelType = "5", Litres = 5, Rate = 103.81, Amount = 519.05 },
                new TestingEntry { DsmEntryId = dsmEntry.DsmEntryId, FuelType = "6", Litres = 5, Rate = 115.00, Amount = 575.00 }
            });

            // Expenses: ₹150
            db.Expenses.Add(new Expense { DsmEntryId = dsmEntry.DsmEntryId, Description = "Tea & Refreshment", Amount = 150.00 });

            // Debtors: ₹2,000
            db.DebitEntries.Add(new DebitEntry { DsmEntryId = dsmEntry.DsmEntryId, DebtorName = "Pawar Transport", Amount = 2000.00 });

            // Cash Denominations:
            db.CashDenominations.AddRange(new[]
            {
                new CashDenomination { DsmEntryId = dsmEntry.DsmEntryId, CashType = "Cash1", TotalAmount = 7000 },
                new CashDenomination { DsmEntryId = dsmEntry.DsmEntryId, CashType = "Cash2", TotalAmount = 12630 }
            });

            // Payments:
            // Bank Cash: 7000, Hand Cash: 12630, PhonePe: 5000, PineLab Card: 4000, PetroCard: 3000, SBI Redeem: 2000, Paytm: 1000, QR: 6000, Mobikwik: 7000, Others: 10000
            db.PaymentCollections.Add(new PaymentCollection
            {
                DsmEntryId = dsmEntry.DsmEntryId,
                CashDeposit = 7000,
                PhonePeMorning = 5000,
                CreditCardMorning = 4000,
                PetroCardMorning = 3000,
                Others = 10000,
                Items = new List<PaymentCollectionItem>
                {
                    new() { CollectionTypeCode = "SBI_REDEEM", Amount = 2000 },
                    new() { CollectionTypeCode = "PAYTM", Amount = 1000 },
                    new() { CollectionTypeCode = "QR", Amount = 6000 },
                    new() { CollectionTypeCode = "MOBIKWIK", Amount = 7000 }
                }
            });

            await db.SaveChangesAsync();

            // 4. Run Calculations
            var allEntries = await db.DsmEntries
                .Include(e => e.NozzleReadings)
                .Include(e => e.TestingEntries)
                .Include(e => e.Expenses)
                .Include(e => e.DebitEntries)
                .Include(e => e.CashDenominations)
                .Include(e => e.PaymentCollection)
                    .ThenInclude(p => p!.Items)
                .ToListAsync();

            var dayReport = reportService.CalculateDayReport(
                new DateTime(2026, 8, 29), new DateTime(2026, 8, 29),
                allEntries,
                await db.Expenses.ToListAsync(),
                new List<CreditorRepayment>(),
                90.35, 103.81, 103.81, 85.00,
                "Kandhare Petroleum"
            );

            // 5. Assert 100% Parity with Screenshot 1
            // Total Fuel Litres: 525.00 L
            Assert.Equal(525.00, dayReport.TotalFuelLitres);
            // Total Fuel Sales Gross Amount: ₹52,870.50
            double totalFuelSalesAmt = dayReport.FuelSales.Sum(f => f.Amount);
            Assert.Equal(52870.50, Math.Round(totalFuelSalesAmt, 2));

            // Verify Tank Breakdown:
            // CNG Line: 0.00 L @ 85.00
            var cngTank = dayReport.FuelSales.FirstOrDefault(f => f.Description.Contains("CNG Line"));
            Assert.NotNull(cngTank);
            Assert.Equal(0.00, cngTank!.Litres);

            // HSD - 20KL: 225.00 L @ 90.35 = ₹20,328.75
            var hsdTank = dayReport.FuelSales.FirstOrDefault(f => f.Description.Contains("HSD - 20KL") && !f.Description.Contains("II"));
            Assert.NotNull(hsdTank);
            Assert.Equal(225.00, hsdTank!.Litres);
            Assert.Equal(90.35, hsdTank.Rate);
            Assert.Equal(20328.75, Math.Round(hsdTank.Amount, 2));

            // MS - 20KL: 125.00 L @ 103.81 = ₹12,976.25
            var msTank = dayReport.FuelSales.FirstOrDefault(f => f.Description.Contains("MS - 20KL") && !f.Description.Contains("II"));
            Assert.NotNull(msTank);
            Assert.Equal(125.00, msTank!.Litres);
            Assert.Equal(103.81, msTank.Rate);
            Assert.Equal(12976.25, Math.Round(msTank.Amount, 2));

            // MS - 20KL II: 50.00 L @ 103.81 = ₹5,190.50
            var msIITank = dayReport.FuelSales.FirstOrDefault(f => f.Description.Contains("MS - 20KL II"));
            Assert.NotNull(msIITank);
            Assert.Equal(50.00, msIITank!.Litres);
            Assert.Equal(103.81, msIITank.Rate);
            Assert.Equal(5190.50, Math.Round(msIITank.Amount, 2));

            // SPEED - 20KL: 125.00 L @ 115.00 = ₹14,375.00
            var speedTank = dayReport.FuelSales.FirstOrDefault(f => f.Description.Contains("SPEED - 20KL"));
            Assert.NotNull(speedTank);
            Assert.Equal(125.00, speedTank!.Litres);
            Assert.Equal(115.00, speedTank.Rate);
            Assert.Equal(14375.00, Math.Round(speedTank.Amount, 2));

            // Phantom tank HSD - 20KL II must NOT exist
            var phantomTank = dayReport.FuelSales.FirstOrDefault(f => f.Description.Contains("HSD - 20KL II"));
            Assert.Null(phantomTank);

            // Verify DSM Shift Totals:
            var dsmRows = shiftAggService.BuildDsmSummaryRows(allEntries);
            Assert.Single(dsmRows);
            var kiraRow = dsmRows[0];

            Assert.Equal("Kira Yagami", kiraRow.DsmName);
            Assert.Equal(52870.50, Math.Round(kiraRow.GrossSales, 2));
            Assert.Equal(3091.60, Math.Round(kiraRow.Testing, 2));
            Assert.Equal(150.00, Math.Round(kiraRow.Expenses, 2));
            Assert.Equal(2000.00, Math.Round(kiraRow.Debit, 2));
            Assert.Equal(7000.00, Math.Round(kiraRow.CashDeposit, 2));
            Assert.Equal(12630.00, Math.Round(kiraRow.CashInHand, 2));
            double digitalSum = kiraRow.PhonePeTotal + kiraRow.CreditCardTotal + kiraRow.PetroCardTotal + kiraRow.DynamicCollectionsTotal;
            Assert.Equal(28000.00, Math.Round(digitalSum, 2));
            Assert.Equal(49778.90, Math.Round(kiraRow.NetGrossSales, 2));
            Assert.Equal(49780.00, Math.Round(kiraRow.TotalCollection, 2));
            Assert.Equal(1.10, Math.Round(kiraRow.Mismatch, 2));
        }
    }
}
