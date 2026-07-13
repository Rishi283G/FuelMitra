using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models;
using FuelPro.Core.Models.AGS;
using FuelPro.Core.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace FuelPro.Core.Services;

public class AgsInventoryService : IAgsInventoryService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IAgsImportRepository _agsRepo;
    private readonly IDsmEntryRepository _dsmRepo;
    private readonly ILogger _logger = Log.ForContext<AgsInventoryService>();

    public AgsInventoryService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _agsRepo = serviceProvider.GetRequiredService<IAgsImportRepository>();
        _dsmRepo = serviceProvider.GetRequiredService<IDsmEntryRepository>();
    }

    public (DateTime Date, string Shift) GetPreviousShift(DateTime date, string shift)
    {
        return shift == "A" ? (date.Date, "B") : (date.Date.AddDays(-1), "A");
    }

    public async Task<List<NozzleGroupDto>> BuildNozzleGroupsAsync(DateTime date, string shiftType, List<DsmEntry>? loadedEntries = null)
    {
        var groups = new List<NozzleGroupDto>();

        // Load active shift import for current shift
        var activeImportRes = await _agsRepo.GetActiveShiftImportAsync(date, shiftType);
        var import = (activeImportRes.Success && activeImportRes.Data != null) ? activeImportRes.Data : null;

        var readingsDict = import?.NozzleReadings?.ToDictionary(r => r.NozzleNumber) 
                           ?? new Dictionary<int, AgsNozzleReading>();

        var hsdTank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD");
        var msITank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I");
        var msIITank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II");

        // Fetch previous shift closings/dips
        var (prevDate, prevShift) = GetPreviousShift(date, shiftType);
        var prevImportRes = await _agsRepo.GetActiveShiftImportAsync(prevDate, prevShift);
        var prevImport = (prevImportRes.Success && prevImportRes.Data != null) ? prevImportRes.Data : null;

        var prevHsdClosingStock = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD")?.ClosingStockLitres ?? 0.0;
        var prevHsdClosingDip = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD")?.ClosingDipMM ?? 0.0;

        var prevMsIClosingStock = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I")?.ClosingStockLitres ?? 0.0;
        var prevMsIClosingDip = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I")?.ClosingDipMM ?? 0.0;

        var prevMsIIClosingStock = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II")?.ClosingStockLitres ?? 0.0;
        var prevMsIIClosingDip = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II")?.ClosingDipMM ?? 0.0;

        var hsdOpeningStock = prevHsdClosingStock > 0 ? prevHsdClosingStock : (hsdTank?.OpeningStockLitres ?? 0.0);
        var hsdOpeningDip = prevHsdClosingDip > 0 ? prevHsdClosingDip : (hsdTank?.OpeningDipMM ?? 0.0);

        var msIOpeningStock = prevMsIClosingStock > 0 ? prevMsIClosingStock : (msITank?.OpeningStockLitres ?? 0.0);
        var msIOpeningDip = prevMsIClosingDip > 0 ? prevMsIClosingDip : (msITank?.OpeningDipMM ?? 0.0);

        var msIIOpeningStock = prevMsIIClosingStock > 0 ? prevMsIIClosingStock : (msIITank?.OpeningStockLitres ?? 0.0);
        var msIIOpeningDip = prevMsIIClosingDip > 0 ? prevMsIIClosingDip : (msIITank?.OpeningDipMM ?? 0.0);

        // Load DSM entries if not passed
        if (loadedEntries == null)
        {
            var shiftRepo = _serviceProvider.GetRequiredService<IShiftRepository>();
            var shiftRes = await shiftRepo.GetShiftAsync(date, shiftType);
            if (shiftRes.Success && shiftRes.Data != null)
            {
                var entriesRes = await _dsmRepo.GetEntriesForShiftAsync(shiftRes.Data.ShiftId);
                loadedEntries = entriesRes.Success ? entriesRes.Data : null;
            }
        }

        // Aggregate manual readings from loaded DSM entries
        var manualReadings = new Dictionary<int, (double Opening, double Closing, double Sale)>();
        if (loadedEntries != null)
        {
            foreach (var group in loadedEntries.SelectMany(e => e.NozzleReadings).GroupBy(r => r.NozzleNumber))
            {
                var sorted = group.OrderBy(r => r.OpeningReading).ToList();
                var opening = sorted.FirstOrDefault()?.OpeningReading ?? 0.0;
                var closing = group.OrderByDescending(r => r.ClosingReading).FirstOrDefault()?.ClosingReading ?? 0.0;
                var sale = group.Sum(r => r.SaleLitres);
                manualReadings[group.Key] = (opening, closing, sale);
            }
        }

        // Calculate testing litres per tank category
        double msITesting = 0;
        double hsdTesting = 0;
        double msIITesting = 0;

        if (loadedEntries != null)
        {
            foreach (var entry in loadedEntries)
            {
                foreach (var t in entry.TestingEntries)
                {
                    var cat = PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, date.Date);
                    if (cat == "MS") msITesting += t.Litres;
                    else if (cat == "HSD") hsdTesting += t.Litres;
                    else if (cat == "HSD-II") msIITesting += t.Litres;
                }
            }
        }

        NozzleDisplayItem CreateItem(int num, string fuelType)
        {
            if (manualReadings.TryGetValue(num, out var mr))
            {
                return new NozzleDisplayItem
                {
                    NozzleNumber = num,
                    FuelType = fuelType,
                    OpeningReading = mr.Opening,
                    ClosingReading = mr.Closing,
                    SaleLitres = mr.Sale,
                    HasReading = true
                };
            }
            if (readingsDict.TryGetValue(num, out var r))
            {
                return new NozzleDisplayItem
                {
                    NozzleNumber = num,
                    FuelType = fuelType,
                    OpeningReading = r.OpeningReading,
                    ClosingReading = r.ClosingReading,
                    SaleLitres = r.NetSaleLitres,
                    HasReading = true
                };
            }
            return new NozzleDisplayItem
            {
                NozzleNumber = num,
                FuelType = fuelType,
                OpeningReading = 0,
                ClosingReading = 0,
                SaleLitres = 0,
                HasReading = false
            };
        }

        var group1Nozzles = new List<NozzleDisplayItem> { CreateItem(1, "Petrol"), CreateItem(2, "Petrol"), CreateItem(5, "Petrol"), CreateItem(6, "Petrol"), CreateItem(9, "Petrol"), CreateItem(10, "Petrol") };
        var group2Nozzles = new List<NozzleDisplayItem> { CreateItem(3, "Diesel"), CreateItem(4, "Diesel"), CreateItem(11, "Diesel"), CreateItem(12, "Diesel") };
        var group3Nozzles = new List<NozzleDisplayItem> { CreateItem(7, "Diesel"), CreateItem(8, "Diesel") };

        var msIDispensed = group1Nozzles.Sum(x => x.SaleLitres);
        var hsdDispensed = group2Nozzles.Sum(x => x.SaleLitres);
        var msIIDispensed = group3Nozzles.Sum(x => x.SaleLitres);

        double msIReceipts = 0.0;
        double hsdReceipts = 0.0;
        double msIIReceipts = 0.0;

        var tankerRepo = _serviceProvider.GetService<IFuelTankerRepository>();
        if (tankerRepo != null)
        {
            var tankersRes = await tankerRepo.GetByDateRangeAsync(date.Date, date.Date);
            var tankers = (tankersRes.Success && tankersRes.Data != null) ? tankersRes.Data : new List<FuelTanker>();

            // Put all approved tanker receipts into Shift B (Day shift/first shift of the day)
            if (shiftType == "B")
            {
                msIReceipts = tankers.Where(t => t.FuelType == "MS-I").Sum(t => t.Quantity);
                hsdReceipts = tankers.Where(t => t.FuelType == "HSD").Sum(t => t.Quantity);
                msIIReceipts = tankers.Where(t => t.FuelType == "MS-II").Sum(t => t.Quantity);
            }
        }
        else
        {
            // Fallback for tests where repository is not registered
            msIReceipts = msITank?.ReceiptLitres ?? 0.0;
            hsdReceipts = hsdTank?.ReceiptLitres ?? 0.0;
            msIIReceipts = msIITank?.ReceiptLitres ?? 0.0;
        }

        // Petrol (Tank 1)
        var msGroup1 = new NozzleGroupDto
        {
            GroupName = "Petrol (Tank 1)",
            FuelType = "Petrol",
            OpeningStock = msIOpeningStock,
            FuelDispensed = msIDispensed,
            TestingLitres = msITesting,
            Receipts = msIReceipts,
            Dip = msITank?.ClosingDipMM ?? 0,
            Stock = (msITank != null && msITank.ClosingDipMM > 0) ? msITank.ClosingStockLitres : (msIOpeningStock - msIDispensed + msITesting + msIReceipts)
        };
        msGroup1.Rows.Add(new List<NozzleDisplayItem> { group1Nozzles[0], group1Nozzles[1], group1Nozzles[2] });
        msGroup1.Rows.Add(new List<NozzleDisplayItem> { group1Nozzles[3], group1Nozzles[4], group1Nozzles[5] });
        groups.Add(msGroup1);

        // Diesel (Tank 2)
        var hsdGroup2 = new NozzleGroupDto
        {
            GroupName = "Diesel (Tank 2)",
            FuelType = "Diesel",
            OpeningStock = hsdOpeningStock,
            FuelDispensed = hsdDispensed,
            TestingLitres = hsdTesting,
            Receipts = hsdReceipts,
            Dip = hsdTank?.ClosingDipMM ?? 0,
            Stock = (hsdTank != null && hsdTank.ClosingDipMM > 0) ? hsdTank.ClosingStockLitres : (hsdOpeningStock - hsdDispensed + hsdTesting + hsdReceipts)
        };
        hsdGroup2.Rows.Add(new List<NozzleDisplayItem> { group2Nozzles[0], group2Nozzles[1] });
        hsdGroup2.Rows.Add(new List<NozzleDisplayItem> { group2Nozzles[2], group2Nozzles[3] });
        groups.Add(hsdGroup2);

        // Diesel (Tank 3)
        var hsdGroup3 = new NozzleGroupDto
        {
            GroupName = "Diesel (Tank 3)",
            FuelType = "Diesel",
            OpeningStock = msIIOpeningStock,
            FuelDispensed = msIIDispensed,
            TestingLitres = msIITesting,
            Receipts = msIIReceipts,
            Dip = msIITank?.ClosingDipMM ?? 0,
            Stock = (msIITank != null && msIITank.ClosingDipMM > 0) ? msIITank.ClosingStockLitres : (msIIOpeningStock - msIIDispensed + msIITesting + msIIReceipts)
        };
        hsdGroup3.Rows.Add(new List<NozzleDisplayItem> { group3Nozzles[0], group3Nozzles[1] });
        groups.Add(hsdGroup3);

        return groups;
    }

    public async Task<Result<AgsShiftImport>> SaveAndPersistInventoryAsync(DateTime date, string shiftType, List<NozzleGroupDto> groups)
    {
        try
        {
            var activeImportRes = await _agsRepo.GetActiveShiftImportAsync(date, shiftType);
            var import = (activeImportRes.Success && activeImportRes.Data != null) ? activeImportRes.Data : null;

            if (import == null)
            {
                // Create a manual import record
                import = new AgsShiftImport
                {
                    ImportDate = date.Date,
                    ShiftType = shiftType,
                    PdfFileName = "Manual Entry",
                    ImportedBy = "User",
                    IsActive = true,
                    ImportedAt = DateTime.Now
                };
            }

            // Ensure TankStocks is loaded
            if (import.TankStocks == null)
            {
                import.TankStocks = new List<AgsTankStock>();
            }

            var (prevDate, prevShift) = GetPreviousShift(date, shiftType);
            var prevImportRes = await _agsRepo.GetActiveShiftImportAsync(prevDate, prevShift);
            var prevImport = (prevImportRes.Success && prevImportRes.Data != null) ? prevImportRes.Data : null;

            foreach (var g in groups)
            {
                string fuelTypeInDb = g.GroupName switch
                {
                    string s when s.Contains("Tank 1") => "MS-I",
                    string s when s.Contains("Tank 2") => "HSD",
                    string s when s.Contains("Tank 3") => "MS-II",
                    _ => g.FuelType
                };

                var tank = import.TankStocks.FirstOrDefault(t => t.FuelType == fuelTypeInDb);
                if (tank == null)
                {
                    tank = new AgsTankStock
                    {
                        FuelType = fuelTypeInDb,
                        TankNumber = fuelTypeInDb switch { "HSD" => 1, "MS-I" => 2, "MS-II" => 3, _ => 1 }
                    };
                    import.TankStocks.Add(tank);
                }

                var prevClosingDip = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == fuelTypeInDb)?.ClosingDipMM ?? 0.0;

                tank.OpeningStockLitres = g.OpeningStock;
                tank.OpeningDipMM = prevClosingDip;
                tank.FuelDispensedLitres = g.FuelDispensed;
                tank.ReceiptLitres = g.Receipts;
                tank.ClosingDipMM = g.Dip;
                double closingStock = (g.Dip > 0 && g.Stock > 0) ? g.Stock : g.CalculatedStock;
                tank.ClosingStockLitres = closingStock;

                // Sync to parent denormalized columns for dashboard/reporting consistency!
                if (fuelTypeInDb == "HSD")
                {
                    import.HsdOpeningStock = g.OpeningStock;
                    import.HsdClosingStock = closingStock;
                }
                else if (fuelTypeInDb == "MS-I")
                {
                    import.MsIOpeningStock = g.OpeningStock;
                    import.MsIClosingStock = closingStock;
                }
                else if (fuelTypeInDb == "MS-II")
                {
                    import.MsIIOpeningStock = g.OpeningStock;
                    import.MsIIClosingStock = closingStock;
                }
            }

            var saveResult = await _agsRepo.SaveShiftImportAsync(import);
            if (!saveResult.Success) return saveResult;

            // Re-aggregate daily summary
            var shiftsResult = await _agsRepo.GetShiftsForDateAsync(date);
            if (shiftsResult.Success && shiftsResult.Data != null)
            {
                var aggregationService = _serviceProvider.GetRequiredService<IAgsDailyAggregationService>();
                var summary = aggregationService.Aggregate(date, shiftsResult.Data);
                await _agsRepo.SaveDailySummaryAsync(summary);
            }

            return Result<AgsShiftImport>.Ok(import);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed in SaveAndPersistInventoryAsync");
            return Result<AgsShiftImport>.Fail($"Failed to save: {ex.Message}");
        }
    }

    public async Task<Result> PropagateInventoryCalculationsAsync(DateTime startDate, string startShiftType)
    {
        try
        {
            var activeImportsRes = await _agsRepo.GetActiveImportsFromDateAsync(startDate);
            if (!activeImportsRes.Success || activeImportsRes.Data == null)
            {
                return Result.Fail(activeImportsRes.Error ?? "Failed to get active imports");
            }
            var allImports = activeImportsRes.Data;

            // Sort chronologically: OrderBy Date, then Shift B (Day) before Shift A (Night/Morning)
            var sortedImports = allImports
                .OrderBy(x => x.ImportDate)
                .ThenBy(x => x.ShiftType == "A")
                .ToList();

            var startIdx = sortedImports.FindIndex(x => x.ImportDate.Date == startDate.Date && x.ShiftType == startShiftType);
            if (startIdx < 0) return Result.Ok(); // Nothing downstream found

            for (int i = startIdx; i < sortedImports.Count; i++)
            {
                var import = sortedImports[i];
                var date = import.ImportDate;
                var shiftType = import.ShiftType;

                // Build nozzle groups (which fetches previous closing stock chronologically)
                var groups = await BuildNozzleGroupsAsync(date, shiftType, null);

                // Update the import entity values
                foreach (var g in groups)
                {
                    string fuelTypeInDb = g.GroupName switch
                    {
                        string s when s.Contains("Tank 1") => "MS-I",
                        string s when s.Contains("Tank 2") => "HSD",
                        string s when s.Contains("Tank 3") => "MS-II",
                        _ => g.FuelType
                    };

                    var tank = import.TankStocks.FirstOrDefault(t => t.FuelType == fuelTypeInDb);
                    if (tank == null)
                    {
                        tank = new AgsTankStock
                        {
                            FuelType = fuelTypeInDb,
                            TankNumber = fuelTypeInDb switch { "HSD" => 1, "MS-I" => 2, "MS-II" => 3, _ => 1 }
                        };
                        import.TankStocks.Add(tank);
                    }

                    tank.OpeningStockLitres = g.OpeningStock;
                    tank.FuelDispensedLitres = g.FuelDispensed;
                    tank.ReceiptLitres = g.Receipts;
                    double closingStock = (g.Dip > 0 && g.Stock > 0) ? g.Stock : g.CalculatedStock;
                    tank.ClosingStockLitres = closingStock; // Recompute book stock!

                    if (fuelTypeInDb == "HSD")
                    {
                        import.HsdOpeningStock = g.OpeningStock;
                        import.HsdClosingStock = closingStock;
                    }
                    else if (fuelTypeInDb == "MS-I")
                    {
                        import.MsIOpeningStock = g.OpeningStock;
                        import.MsIClosingStock = closingStock;
                    }
                    else if (fuelTypeInDb == "MS-II")
                    {
                        import.MsIIOpeningStock = g.OpeningStock;
                        import.MsIIClosingStock = closingStock;
                    }
                }

                // Save to database
                await _agsRepo.SaveShiftImportAsync(import);

                // Re-aggregate daily summary for this date
                var dayImports = sortedImports.Where(x => x.ImportDate.Date == date.Date).ToList();
                var aggregationService = _serviceProvider.GetRequiredService<IAgsDailyAggregationService>();
                var summary = aggregationService.Aggregate(date, dayImports);
                
                await _agsRepo.SaveDailySummaryAsync(summary);
            }

            return Result.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed in PropagateInventoryCalculationsAsync");
            return Result.Fail($"Propagation error: {ex.Message}");
        }
    }

    public async Task<List<NozzleGroupDto>> BuildNozzleGroupsForDateRangeAsync(DateTime startDate, DateTime endDate, List<DsmEntry>? loadedEntries = null)
    {
        var groups = new List<NozzleGroupDto>();

        // Load active imports for the date range
        var activeImports = new List<AgsShiftImport>();
        for (var dt = startDate.Date; dt <= endDate.Date; dt = dt.AddDays(1))
        {
            var shiftsRes = await _agsRepo.GetShiftsForDateAsync(dt);
            if (shiftsRes.Success && shiftsRes.Data != null)
            {
                activeImports.AddRange(shiftsRes.Data.Where(s => s.IsActive));
            }
        }

        var sortedShifts = activeImports.OrderBy(s => s.ImportDate).ThenBy(s => s.ShiftType == "A").ToList();
        var lastShift = sortedShifts.LastOrDefault();

        var hsdTank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD");
        var msITank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I");
        var msIITank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II");

        // Fetch previous shift closings/dips before startDate
        var prevDate = startDate.Date.AddDays(-1);
        var prevShift = "A";
        var prevImportRes = await _agsRepo.GetActiveShiftImportAsync(prevDate, prevShift);
        var prevImport = (prevImportRes.Success && prevImportRes.Data != null) ? prevImportRes.Data : null;

        var prevHsdClosingStock = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD")?.ClosingStockLitres ?? 0.0;
        var prevMsIClosingStock = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I")?.ClosingStockLitres ?? 0.0;
        var prevMsIIClosingStock = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II")?.ClosingStockLitres ?? 0.0;

        var hsdOpeningStock = prevHsdClosingStock > 0 ? prevHsdClosingStock : (sortedShifts.FirstOrDefault()?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD")?.OpeningStockLitres ?? 0.0);
        var msIOpeningStock = prevMsIClosingStock > 0 ? prevMsIClosingStock : (sortedShifts.FirstOrDefault()?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I")?.OpeningStockLitres ?? 0.0);
        var msIIOpeningStock = prevMsIIClosingStock > 0 ? prevMsIIClosingStock : (sortedShifts.FirstOrDefault()?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II")?.OpeningStockLitres ?? 0.0);

        // Load DSM entries if not passed
        if (loadedEntries == null)
        {
            loadedEntries = new List<DsmEntry>();
            for (var dt = startDate.Date; dt <= endDate.Date; dt = dt.AddDays(1))
            {
                var shiftsResult = await _serviceProvider.GetRequiredService<IShiftRepository>().GetShiftsForDateAsync(dt);
                if (shiftsResult.Success && shiftsResult.Data != null)
                {
                    foreach (var shift in shiftsResult.Data)
                    {
                        var entriesRes = await _dsmRepo.GetEntriesForShiftAsync(shift.ShiftId);
                        if (entriesRes.Success && entriesRes.Data != null)
                        {
                            loadedEntries.AddRange(entriesRes.Data);
                        }
                    }
                }
            }
        }

        // Aggregate manual readings
        var manualReadings = new Dictionary<int, (double Opening, double Closing, double Sale)>();
        foreach (var group in loadedEntries.SelectMany(e => e.NozzleReadings).GroupBy(r => r.NozzleNumber))
        {
            var sorted = group.OrderBy(r => r.OpeningReading).ToList();
            var opening = sorted.FirstOrDefault()?.OpeningReading ?? 0.0;
            var closing = group.OrderByDescending(r => r.ClosingReading).FirstOrDefault()?.ClosingReading ?? 0.0;
            var sale = group.Sum(r => r.SaleLitres);
            manualReadings[group.Key] = (opening, closing, sale);
        }

        var allAgsReadings = sortedShifts.SelectMany(s => s.NozzleReadings).ToList();

        // Calculate testing litres
        double msITesting = 0;
        double hsdTesting = 0;
        double msIITesting = 0;

        foreach (var entry in loadedEntries)
        {
            foreach (var t in entry.TestingEntries)
            {
                var cat = PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, entry.Shift?.ShiftDate ?? startDate.Date);
                if (cat == "MS") msITesting += t.Litres;
                else if (cat == "HSD") hsdTesting += t.Litres;
                else if (cat == "HSD-II") msIITesting += t.Litres;
            }
        }

        NozzleDisplayItem CreateItem(int num, string fuelType)
        {
            if (manualReadings.TryGetValue(num, out var mr))
            {
                return new NozzleDisplayItem
                {
                    NozzleNumber = num,
                    FuelType = fuelType,
                    OpeningReading = mr.Opening,
                    ClosingReading = mr.Closing,
                    SaleLitres = mr.Sale,
                    HasReading = true
                };
            }
            var nozzleReadings = allAgsReadings.Where(r => r.NozzleNumber == num).ToList();
            if (nozzleReadings.Count > 0)
            {
                var op = nozzleReadings.Min(r => r.OpeningReading);
                var cl = nozzleReadings.Max(r => r.ClosingReading);
                var sale = nozzleReadings.Sum(r => r.NetSaleLitres);
                return new NozzleDisplayItem
                {
                    NozzleNumber = num,
                    FuelType = fuelType,
                    OpeningReading = op,
                    ClosingReading = cl,
                    SaleLitres = sale,
                    HasReading = true
                };
            }
            return new NozzleDisplayItem
            {
                NozzleNumber = num,
                FuelType = fuelType,
                OpeningReading = 0,
                ClosingReading = 0,
                SaleLitres = 0,
                HasReading = false
            };
        }

        var group1Nozzles = new List<NozzleDisplayItem> { CreateItem(1, "Petrol"), CreateItem(2, "Petrol"), CreateItem(5, "Petrol"), CreateItem(6, "Petrol"), CreateItem(9, "Petrol"), CreateItem(10, "Petrol") };
        var group2Nozzles = new List<NozzleDisplayItem> { CreateItem(3, "Diesel"), CreateItem(4, "Diesel"), CreateItem(11, "Diesel"), CreateItem(12, "Diesel") };
        var group3Nozzles = new List<NozzleDisplayItem> { CreateItem(7, "Diesel"), CreateItem(8, "Diesel") };

        var msIDispensed = group1Nozzles.Sum(x => x.SaleLitres);
        var hsdDispensed = group2Nozzles.Sum(x => x.SaleLitres);
        var msIIDispensed = group3Nozzles.Sum(x => x.SaleLitres);

        // Receipts come directly from approved Fuel Tanker Purchases!
        double msIReceipts = 0.0;
        double hsdReceipts = 0.0;
        double msIIReceipts = 0.0;

        var tankerRepo = _serviceProvider.GetService<IFuelTankerRepository>();
        if (tankerRepo != null)
        {
            var tankersRes = await tankerRepo.GetByDateRangeAsync(startDate.Date, endDate.Date);
            var tankers = tankersRes.Success && tankersRes.Data != null ? tankersRes.Data : new List<FuelTanker>();

            msIReceipts = tankers.Where(t => t.FuelType == "MS-I").Sum(t => t.Quantity);
            hsdReceipts = tankers.Where(t => t.FuelType == "HSD").Sum(t => t.Quantity);
            msIIReceipts = tankers.Where(t => t.FuelType == "MS-II").Sum(t => t.Quantity);
        }
        else
        {
            // Fallback for tests
            foreach (var sImport in sortedShifts)
            {
                msIReceipts += sImport.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I")?.ReceiptLitres ?? 0.0;
                hsdReceipts += sImport.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD")?.ReceiptLitres ?? 0.0;
                msIIReceipts += sImport.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II")?.ReceiptLitres ?? 0.0;
            }
        }

        // Petrol (Tank 1)
        var msGroup1 = new NozzleGroupDto
        {
            GroupName = "Petrol (Tank 1)",
            FuelType = "Petrol",
            OpeningStock = msIOpeningStock,
            FuelDispensed = msIDispensed,
            TestingLitres = msITesting,
            Receipts = msIReceipts,
            Dip = msITank?.ClosingDipMM ?? 0,
            Stock = msITank?.ClosingStockLitres ?? (msIOpeningStock - msIDispensed + msITesting + msIReceipts)
        };
        msGroup1.Rows.Add(new List<NozzleDisplayItem> { group1Nozzles[0], group1Nozzles[1], group1Nozzles[2] });
        msGroup1.Rows.Add(new List<NozzleDisplayItem> { group1Nozzles[3], group1Nozzles[4], group1Nozzles[5] });
        groups.Add(msGroup1);

        // Diesel (Tank 2)
        var hsdGroup2 = new NozzleGroupDto
        {
            GroupName = "Diesel (Tank 2)",
            FuelType = "Diesel",
            OpeningStock = hsdOpeningStock,
            FuelDispensed = hsdDispensed,
            TestingLitres = hsdTesting,
            Receipts = hsdReceipts,
            Dip = hsdTank?.ClosingDipMM ?? 0,
            Stock = hsdTank?.ClosingStockLitres ?? (hsdOpeningStock - hsdDispensed + hsdTesting + hsdReceipts)
        };
        hsdGroup2.Rows.Add(new List<NozzleDisplayItem> { group2Nozzles[0], group2Nozzles[1] });
        hsdGroup2.Rows.Add(new List<NozzleDisplayItem> { group2Nozzles[2], group2Nozzles[3] });
        groups.Add(hsdGroup2);

        // Diesel (Tank 3)
        var hsdGroup3 = new NozzleGroupDto
        {
            GroupName = "Diesel (Tank 3)",
            FuelType = "Diesel",
            OpeningStock = msIIOpeningStock,
            FuelDispensed = msIIDispensed,
            TestingLitres = msIITesting,
            Receipts = msIIReceipts,
            Dip = msIITank?.ClosingDipMM ?? 0,
            Stock = msIITank?.ClosingStockLitres ?? (msIIOpeningStock - msIIDispensed + msIITesting + msIIReceipts)
        };
        hsdGroup3.Rows.Add(new List<NozzleDisplayItem> { group3Nozzles[0], group3Nozzles[1] });
        groups.Add(hsdGroup3);

        return groups;
    }
}
