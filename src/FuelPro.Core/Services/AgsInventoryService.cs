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
    private readonly IShiftCycleService _shiftCycleService;
    private readonly ILogger _logger = Log.ForContext<AgsInventoryService>();

    public AgsInventoryService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _agsRepo = serviceProvider.GetRequiredService<IAgsImportRepository>();
        _dsmRepo = serviceProvider.GetRequiredService<IDsmEntryRepository>();
        _shiftCycleService = serviceProvider.GetService<IShiftCycleService>() ?? new ShiftCycleService();
    }

    /// <summary>Normalises Roman-numeral shift types (I/II/III) back to A/B/C so all
    /// internal comparisons use a single canonical form.</summary>
    private static string NormalizeShiftType(string shiftType) => shiftType switch
    {
        "I"   or "Shift I"   => "A",
        "II"  or "Shift II"  => "B",
        "III" or "Shift III" => "C",
        _                    => shiftType
    };

    public (DateTime Date, string Shift) GetPreviousShift(DateTime date, string shift)
    {
        return _shiftCycleService.GetPreviousShift(date, shift);
    }

    /// <summary>
    /// Matches fuel types supporting bidirectional aliases:
    /// - SPEED &lt;-&gt; MS-II
    /// - Petrol &lt;-&gt; MS-I / MS
    /// - Diesel &lt;-&gt; HSD
    /// Strict equality for all others, preventing cross-matching between unrelated fuel types.
    /// </summary>
    public static bool FuelMatches(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

        var normA = a.Trim().ToUpperInvariant();
        var normB = b.Trim().ToUpperInvariant();

        if (normA == normB) return true;

        // SPEED <-> MS-II
        if ((normA == "SPEED" || normA == "MS-II") && (normB == "SPEED" || normB == "MS-II")) return true;

        // Petrol <-> MS-I / MS
        if ((normA == "PETROL" || normA == "MS-I" || normA == "MS") && (normB == "PETROL" || normB == "MS-I" || normB == "MS")) return true;

        // Diesel <-> HSD
        if ((normA == "DIESEL" || normA == "HSD") && (normB == "DIESEL" || normB == "HSD")) return true;

        return false;
    }

    /// <summary>
    /// Resolves measurement provenance according to 4-tier specification:
    /// 1. Locally persisted explicit provenance (HasPhysicalDip = true, honors measured stock including 0.0)
    /// 2. AGS import evidence (verified AGS PDF import)
    /// 3. Synchronized provenance from ShiftBreakdownJson
    /// 4. Legacy fallback (ClosingDipMM > 0 means measured; default 0 means Dip Not Recorded)
    /// </summary>
    public static (bool HasMeasurement, double MeasuredStock, double DipValue, string? Source) ResolveMeasurementProvenance(
        AgsTankStock? tankStock, 
        AgsShiftImport? import, 
        string? shiftBreakdownJson, 
        string shiftType, 
        string fuelType)
    {
        // 1. Locally persisted explicit provenance
        if (tankStock != null && tankStock.HasPhysicalDip)
        {
            string src = tankStock.DipSource ?? "Manual";
            double stock = (src == "DipOnly") ? 0.0 : tankStock.ClosingStockLitres;
            return (true, stock, tankStock.ClosingDipMM, src);
        }

        // 2. AGS import evidence: verified AGS PDF tank measurement
        if (import != null && !string.IsNullOrEmpty(import.PdfFileName) &&
            import.PdfFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) &&
            tankStock != null &&
            (tankStock.HasPhysicalDip || tankStock.ClosingDipMM > 0.001 || tankStock.DipSource == "AgsGauge" || tankStock.DipSource == "AGS"))
        {
            return (true, tankStock.ClosingStockLitres, tankStock.ClosingDipMM, tankStock.DipSource ?? "AGS");
        }

        // 3. Synchronized provenance from ShiftBreakdownJson
        if (!string.IsNullOrEmpty(shiftBreakdownJson))
        {
            var syncMeasurement = AgsDailyAggregationService.ParseTankMeasurement(shiftBreakdownJson, shiftType, fuelType);
            if (syncMeasurement != null && syncMeasurement.HasMeasurement)
            {
                return (true, syncMeasurement.MeasuredStock ?? 0.0, syncMeasurement.DipMm, syncMeasurement.Source ?? "Sync");
            }
        }

        // 4. Legacy fallback: positive dip means measured; default 0 means Dip Not Recorded
        if (tankStock != null && tankStock.ClosingDipMM > 0.001)
        {
            return (true, tankStock.ClosingStockLitres, tankStock.ClosingDipMM, "Legacy");
        }

        return (false, 0.0, 0.0, null);
    }

    public async Task<List<NozzleGroupDto>> BuildNozzleGroupsAsync(DateTime date, string shiftType, List<DsmEntry>? loadedEntries = null)
    {
        shiftType = NormalizeShiftType(shiftType);
        // ---- end of normalisation ----
        return await BuildNozzleGroupsInternalAsync(date, shiftType, loadedEntries);
    }

    private async Task<List<NozzleGroupDto>> BuildNozzleGroupsInternalAsync(DateTime date, string shiftType, List<DsmEntry>? loadedEntries = null)
    {
        var groups = new List<NozzleGroupDto>();

        // Load active shift import for current shift
        var activeImportRes = await _agsRepo.GetActiveShiftImportAsync(date, shiftType);
        var import = (activeImportRes.Success && activeImportRes.Data != null) ? activeImportRes.Data : null;

        AgsDailySummary? dailySummary = null;
        var summaryRes = await _agsRepo.GetDailySummaryAsync(date);
        if (summaryRes.Success && summaryRes.Data != null)
        {
            dailySummary = summaryRes.Data;
        }

        var readingsDict = import?.NozzleReadings?.ToDictionary(r => r.NozzleNumber) 
                           ?? new Dictionary<int, AgsNozzleReading>();

        var hsdTank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD");
        var msITank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I");
        var msIITank = import?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II");

        // Fetch previous shift closings/dips
        var (prevDate, prevShift) = GetPreviousShift(date, shiftType);
        var prevImportRes = await _agsRepo.GetActiveShiftImportAsync(prevDate, prevShift);
        var prevImport = (prevImportRes.Success && prevImportRes.Data != null) ? prevImportRes.Data : null;

        var prevHsdTank = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD");
        var prevMsITank = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I");
        var prevMsIITank = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II");

        var hsdOpeningStock = (prevHsdTank != null) ? prevHsdTank.ClosingStockLitres : (hsdTank?.OpeningStockLitres ?? 0.0);
        var hsdOpeningDip = (prevHsdTank != null) ? prevHsdTank.ClosingDipMM : (hsdTank?.OpeningDipMM ?? 0.0);

        var msIOpeningStock = (prevMsITank != null) ? prevMsITank.ClosingStockLitres : (msITank?.OpeningStockLitres ?? 0.0);
        var msIOpeningDip = (prevMsITank != null) ? prevMsITank.ClosingDipMM : (msITank?.OpeningDipMM ?? 0.0);

        var msIIOpeningStock = (prevMsIITank != null) ? prevMsIITank.ClosingStockLitres : (msIITank?.OpeningStockLitres ?? 0.0);
        var msIIOpeningDip = (prevMsIITank != null) ? prevMsIITank.ClosingDipMM : (msIITank?.OpeningDipMM ?? 0.0);

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
                foreach (var t in entry.TestingEntries ?? Enumerable.Empty<TestingEntry>())
                {
                    var cat = PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, date.Date);
                    if (cat.Contains("MS - 20KL", StringComparison.OrdinalIgnoreCase) || cat.Equals("MS", StringComparison.OrdinalIgnoreCase) || cat.Equals("MS-I", StringComparison.OrdinalIgnoreCase))
                        msITesting += t.Litres;
                    else if (cat.Contains("20KL II", StringComparison.OrdinalIgnoreCase) || cat.Equals("MS-II", StringComparison.OrdinalIgnoreCase) || cat.Contains("SPEED", StringComparison.OrdinalIgnoreCase))
                        msIITesting += t.Litres;
                    else if (cat.Contains("HSD", StringComparison.OrdinalIgnoreCase))
                        hsdTesting += t.Litres;
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

        var stationConfig = _serviceProvider.GetService<IStationConfigurationService>();
        List<TankDefinition> dynamicTanks = new();
        List<PumpMapping> dynamicMappings = new();
        if (stationConfig != null)
        {
            try
            {
                dynamicTanks = (await stationConfig.GetAllTanksAsync()).Where(t => t.IsActive).ToList();
                dynamicMappings = (await stationConfig.GetAllPumpMappingsAsync()).Where(m => m.IsActive).ToList();
            }
            catch { }
        }

        if (dynamicTanks.Count > 0 && dynamicMappings.Count > 0)
        {
            foreach (var tank in dynamicTanks)
            {
                var tankNozzles = dynamicMappings
                    .Where(m => string.Equals(m.TankName, tank.TankName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(m => m.NozzleNumber)
                    .Select(m => CreateItem(m.NozzleNumber, m.FuelType))
                    .ToList();

                if (tankNozzles.Count == 0)
                {
                    tankNozzles = dynamicMappings
                        .Where(m => string.Equals(m.FuelType, tank.FuelType, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(m => m.NozzleNumber)
                        .Select(m => CreateItem(m.NozzleNumber, m.FuelType))
                        .ToList();
                }

                var isCng = string.Equals(tank.FuelType, "CNG", StringComparison.OrdinalIgnoreCase) || 
                            tank.TankName.Contains("CNG", StringComparison.OrdinalIgnoreCase);

                if (tankNozzles.Count == 0 && isCng)
                {
                    tankNozzles = dynamicMappings
                        .Where(m => (m.FuelType != null && m.FuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase)) || 
                                    (m.TankName != null && m.TankName.Contains("CNG", StringComparison.OrdinalIgnoreCase)))
                        .OrderBy(m => m.NozzleNumber)
                        .Select(m => CreateItem(m.NozzleNumber, m.FuelType))
                        .ToList();
                }

                var dispensed = tankNozzles.Sum(x => x.SaleLitres);
                if (dispensed == 0 && loadedEntries != null && isCng)
                {
                    var cngSales = loadedEntries
                        .SelectMany(e => e.NozzleReadings ?? new List<NozzleReading>())
                        .Where(r => string.Equals(r.FuelType, "CNG", StringComparison.OrdinalIgnoreCase) || 
                                    (r.FuelType != null && r.FuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase)))
                        .Sum(r => r.SaleLitres);
                    if (cngSales > 0) dispensed = cngSales;
                }

                double testingLitres = 0;
                if (loadedEntries != null && !isCng)
                {
                    foreach (var entry in loadedEntries)
                    {
                        foreach (var t in entry.TestingEntries ?? Enumerable.Empty<TestingEntry>())
                        {
                            if (IsTestingEntryForTank(t, tank, tankNozzles, entry.PumpId, entry.Shift?.ShiftDate ?? date.Date))
                            {
                                testingLitres += t.Litres;
                            }
                        }
                    }
                }

                double receipts = 0;
                var tankerRepo = _serviceProvider.GetService<IFuelTankerRepository>();
                if (tankerRepo != null)
                {
                    bool isRecipient = false;
                    var normShift = NormalizeShiftType(shiftType);
                    if (normShift == "B")
                    {
                        isRecipient = true;
                    }
                    else if (normShift == "C")
                    {
                        var bRes = await _agsRepo.GetActiveShiftImportAsync(date.Date, "B");
                        isRecipient = !bRes.Success || bRes.Data == null;
                    }
                    else if (normShift == "A")
                    {
                        var bRes = await _agsRepo.GetActiveShiftImportAsync(date.Date, "B");
                        var cRes = await _agsRepo.GetActiveShiftImportAsync(date.Date, "C");
                        isRecipient = (!bRes.Success || bRes.Data == null) && (!cRes.Success || cRes.Data == null);
                    }

                    if (isRecipient)
                    {
                        var tankersRes = await tankerRepo.GetByDateRangeAsync(date.Date, date.Date);
                        var tankers = (tankersRes.Success && tankersRes.Data != null) ? tankersRes.Data : new List<FuelTanker>();
                        receipts = tankers.Where(t => FuelMatches(t.FuelType, tank.FuelType)).Sum(t => t.Quantity);
                    }
                }

                var tankStock = import?.TankStocks?.FirstOrDefault(ts => string.Equals(ts.FuelType, tank.FuelType, StringComparison.OrdinalIgnoreCase));
                var prevTankStock = prevImport?.TankStocks?.FirstOrDefault(ts => string.Equals(ts.FuelType, tank.FuelType, StringComparison.OrdinalIgnoreCase));

                double openingStock = (prevTankStock != null) ? prevTankStock.ClosingStockLitres : (tankStock?.OpeningStockLitres ?? 0.0);
                var prov = ResolveMeasurementProvenance(tankStock, import, dailySummary?.ShiftBreakdownJson, shiftType, tank.FuelType);

                var groupDto = new NozzleGroupDto
                {
                    GroupName = $"{tank.FuelType} ({tank.TankName})",
                    FuelType = tank.FuelType,
                    OpeningStock = openingStock,
                    FuelDispensed = dispensed,
                    TestingLitres = testingLitres,
                    Receipts = receipts
                };

                if (prov.HasMeasurement)
                {
                    groupDto.HasPhysicalDip = true;
                    groupDto.DipSource = prov.Source;

                    if (prov.Source == "DipOnly")
                    {
                        groupDto.Dip = prov.DipValue;
                        groupDto.ManualStock = null;
                    }
                    else
                    {
                        groupDto.ManualStock = prov.MeasuredStock;
                        groupDto.Dip = (prov.DipValue > 0.001) ? prov.DipValue : (double?)null;
                    }
                }
                else
                {
                    groupDto.ManualStock = null;
                    groupDto.Dip = null;
                    groupDto.HasPhysicalDip = false;
                    groupDto.DipSource = null;
                }

                for (int i = 0; i < tankNozzles.Count; i += 3)
                {
                    groupDto.Rows.Add(tankNozzles.Skip(i).Take(3).ToList());
                }

                groups.Add(groupDto);
            }

            return groups;
        }

        // Fallback for default 3-tank configuration
        var group1Nozzles = new List<NozzleDisplayItem> { CreateItem(1, "Petrol"), CreateItem(2, "Petrol"), CreateItem(5, "Petrol"), CreateItem(6, "Petrol"), CreateItem(9, "Petrol"), CreateItem(10, "Petrol") };
        var group2Nozzles = new List<NozzleDisplayItem> { CreateItem(3, "Diesel"), CreateItem(4, "Diesel"), CreateItem(11, "Diesel"), CreateItem(12, "Diesel") };
        var group3Nozzles = new List<NozzleDisplayItem> { CreateItem(7, "Diesel"), CreateItem(8, "Diesel") };

        var msIDispensed = group1Nozzles.Sum(x => x.SaleLitres);
        var hsdDispensed = group2Nozzles.Sum(x => x.SaleLitres);
        var msIIDispensed = group3Nozzles.Sum(x => x.SaleLitres);

        double msIReceipts = 0.0;
        double hsdReceipts = 0.0;
        double msIIReceipts = 0.0;

        var tankerRepoFallback = _serviceProvider.GetService<IFuelTankerRepository>();
        if (tankerRepoFallback != null)
        {
            bool isRecipient = false;
            var normShift = NormalizeShiftType(shiftType);
            if (normShift == "B")
            {
                isRecipient = true;
            }
            else if (normShift == "C")
            {
                var bRes = await _agsRepo.GetActiveShiftImportAsync(date.Date, "B");
                isRecipient = !bRes.Success || bRes.Data == null;
            }
            else if (normShift == "A")
            {
                var bRes = await _agsRepo.GetActiveShiftImportAsync(date.Date, "B");
                var cRes = await _agsRepo.GetActiveShiftImportAsync(date.Date, "C");
                isRecipient = (!bRes.Success || bRes.Data == null) && (!cRes.Success || cRes.Data == null);
            }

            if (isRecipient)
            {
                var tankersRes = await tankerRepoFallback.GetByDateRangeAsync(date.Date, date.Date);
                var tankers = (tankersRes.Success && tankersRes.Data != null) ? tankersRes.Data : new List<FuelTanker>();
                msIReceipts = tankers.Where(t => FuelMatches(t.FuelType, "MS-I")).Sum(t => t.Quantity);
                hsdReceipts = tankers.Where(t => FuelMatches(t.FuelType, "HSD")).Sum(t => t.Quantity);
                msIIReceipts = tankers.Where(t => FuelMatches(t.FuelType, "MS-II")).Sum(t => t.Quantity);
            }
            else
            {
                msIReceipts = 0.0;
                hsdReceipts = 0.0;
                msIIReceipts = 0.0;
            }
        }
        else
        {
            msIReceipts = msITank?.ReceiptLitres ?? 0.0;
            hsdReceipts = hsdTank?.ReceiptLitres ?? 0.0;
            msIIReceipts = msIITank?.ReceiptLitres ?? 0.0;
        }

        var provMsI = ResolveMeasurementProvenance(msITank, import, dailySummary?.ShiftBreakdownJson, shiftType, "MS-I");
        var provHsd = ResolveMeasurementProvenance(hsdTank, import, dailySummary?.ShiftBreakdownJson, shiftType, "HSD");
        var provMsII = ResolveMeasurementProvenance(msIITank, import, dailySummary?.ShiftBreakdownJson, shiftType, "MS-II");

        // Petrol (Tank 1)
        var msGroup1 = new NozzleGroupDto
        {
            GroupName = "Petrol (Tank 1)",
            FuelType = "Petrol",
            OpeningStock = msIOpeningStock,
            FuelDispensed = msIDispensed,
            TestingLitres = msITesting,
            Receipts = msIReceipts
        };
        if (provMsI.HasMeasurement)
        {
            msGroup1.HasPhysicalDip = true;
            msGroup1.DipSource = provMsI.Source;
            if (provMsI.Source == "DipOnly")
            {
                msGroup1.Dip = provMsI.DipValue;
                msGroup1.ManualStock = null;
            }
            else
            {
                msGroup1.ManualStock = provMsI.MeasuredStock;
                msGroup1.Dip = (provMsI.DipValue > 0.001) ? provMsI.DipValue : (double?)null;
            }
        }
        else
        {
            msGroup1.ManualStock = null;
            msGroup1.Dip = null;
            msGroup1.HasPhysicalDip = false;
            msGroup1.DipSource = null;
        }
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
            Receipts = hsdReceipts
        };
        if (provHsd.HasMeasurement)
        {
            hsdGroup2.HasPhysicalDip = true;
            hsdGroup2.DipSource = provHsd.Source;
            if (provHsd.Source == "DipOnly")
            {
                hsdGroup2.Dip = provHsd.DipValue;
                hsdGroup2.ManualStock = null;
            }
            else
            {
                hsdGroup2.ManualStock = provHsd.MeasuredStock;
                hsdGroup2.Dip = (provHsd.DipValue > 0.001) ? provHsd.DipValue : (double?)null;
            }
        }
        else
        {
            hsdGroup2.ManualStock = null;
            hsdGroup2.Dip = null;
            hsdGroup2.HasPhysicalDip = false;
            hsdGroup2.DipSource = null;
        }
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
            Receipts = msIIReceipts
        };
        if (provMsII.HasMeasurement)
        {
            hsdGroup3.HasPhysicalDip = true;
            hsdGroup3.DipSource = provMsII.Source;
            if (provMsII.Source == "DipOnly")
            {
                hsdGroup3.Dip = provMsII.DipValue;
                hsdGroup3.ManualStock = null;
            }
            else
            {
                hsdGroup3.ManualStock = provMsII.MeasuredStock;
                hsdGroup3.Dip = (provMsII.DipValue > 0.001) ? provMsII.DipValue : (double?)null;
            }
        }
        else
        {
            hsdGroup3.ManualStock = null;
            hsdGroup3.Dip = null;
            hsdGroup3.HasPhysicalDip = false;
            hsdGroup3.DipSource = null;
        }
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
                    string s when s.Contains("Tank 1") && (s.Contains("Petrol") || s.Contains("MS")) => "MS-I",
                    string s when s.Contains("Tank 2") && (s.Contains("Diesel") || s.Contains("HSD")) => "HSD",
                    string s when s.Contains("Tank 3") && (s.Contains("Diesel") || s.Contains("Speed") || s.Contains("MS")) => "MS-II",
                    _ => g.FuelType switch
                    {
                        "Petrol" => "MS-I",
                        "Diesel" => "HSD",
                        "SPEED" => "SPEED",
                        _ => g.FuelType
                    }
                };

                var tank = import.TankStocks.FirstOrDefault(t => 
                    string.Equals(t.FuelType, fuelTypeInDb, StringComparison.OrdinalIgnoreCase) ||
                    (fuelTypeInDb == "MS-II" && string.Equals(t.FuelType, "SPEED", StringComparison.OrdinalIgnoreCase)) ||
                    (fuelTypeInDb == "SPEED" && string.Equals(t.FuelType, "MS-II", StringComparison.OrdinalIgnoreCase)));

                if (tank == null)
                {
                    tank = new AgsTankStock
                    {
                        FuelType = fuelTypeInDb,
                        TankNumber = fuelTypeInDb switch { "HSD" => 1, "MS-I" => 2, "MS-II" or "SPEED" => 3, _ => 1 }
                    };
                    import.TankStocks.Add(tank);
                }

                var prevClosingDip = prevImport?.TankStocks?.FirstOrDefault(t => 
                    string.Equals(t.FuelType, fuelTypeInDb, StringComparison.OrdinalIgnoreCase) ||
                    (fuelTypeInDb == "MS-II" && string.Equals(t.FuelType, "SPEED", StringComparison.OrdinalIgnoreCase)) ||
                    (fuelTypeInDb == "SPEED" && string.Equals(t.FuelType, "MS-II", StringComparison.OrdinalIgnoreCase)))?.ClosingDipMM ?? 0.0;

                tank.OpeningStockLitres = g.OpeningStock;
                tank.OpeningDipMM = prevClosingDip;
                tank.FuelDispensedLitres = g.FuelDispensed;
                tank.ReceiptLitres = g.Receipts;

                double closingStock;
                if (g.HasManualStockEntry && g.ManualStock.HasValue)
                {
                    closingStock = g.ManualStock.Value;
                    tank.HasPhysicalDip = true;
                    tank.ClosingDipMM = g.Dip ?? 0.0;
                    tank.DipSource = (g.HasManualDipEntry || (g.Dip.HasValue && g.Dip.Value > 0.001)) ? (g.DipSource ?? "Manual") : "ManualStockOnly";
                }
                else if (g.HasManualDipEntry && g.Dip.HasValue && (g.Dip.Value > 0.001 || g.HasPhysicalDip == true))
                {
                    // Dip-only: preserve dip, but closing stock is book stock
                    closingStock = g.CalculatedStock;
                    tank.HasPhysicalDip = true;
                    tank.ClosingDipMM = g.Dip.Value;
                    tank.DipSource = "DipOnly";
                }
                else
                {
                    // No measurement
                    closingStock = g.CalculatedStock;
                    tank.HasPhysicalDip = false;
                    tank.ClosingDipMM = 0.0;
                    tank.DipSource = null;
                }

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
                else if (fuelTypeInDb == "MS-II" || fuelTypeInDb == "SPEED")
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

            startShiftType = NormalizeShiftType(startShiftType);

            // Sort chronologically within date: Shift B (1) -> Shift C (2) -> Shift A (3)
            var sortedImports = allImports
                .OrderBy(x => x.ImportDate)
                .ThenBy(x => _shiftCycleService.NormalizeShiftType(x.ShiftType) switch { "B" => 1, "C" => 2, "A" => 3, _ => 4 })
                .ToList();

            int targetOrder = _shiftCycleService.NormalizeShiftType(startShiftType) switch { "B" => 1, "C" => 2, "A" => 3, _ => 4 };
            var startIdx = sortedImports.FindIndex(x =>
                x.ImportDate.Date > startDate.Date ||
                (x.ImportDate.Date == startDate.Date &&
                 (_shiftCycleService.NormalizeShiftType(x.ShiftType) switch { "B" => 1, "C" => 2, "A" => 3, _ => 4 }) >= targetOrder));
            if (startIdx < 0) return Result.Ok(); // Nothing downstream found

            for (int i = startIdx; i < sortedImports.Count; i++)
            {
                var import = sortedImports[i];
                var date = import.ImportDate;
                var shiftType = NormalizeShiftType(import.ShiftType);

                // Build nozzle groups (which fetches previous closing stock chronologically)
                var groups = await BuildNozzleGroupsAsync(date, shiftType, null);

                // Update the import entity values
                foreach (var g in groups)
                {
                    string fuelTypeInDb = g.GroupName switch
                    {
                        string s when s.Contains("Tank 1") && (s.Contains("Petrol") || s.Contains("MS")) => "MS-I",
                        string s when s.Contains("Tank 2") && (s.Contains("Diesel") || s.Contains("HSD")) => "HSD",
                        string s when s.Contains("Tank 3") && (s.Contains("Diesel") || s.Contains("Speed") || s.Contains("MS")) => "MS-II",
                        _ => g.FuelType switch
                        {
                            "Petrol" => "MS-I",
                            "Diesel" => "HSD",
                            "SPEED" => "SPEED",
                            _ => g.FuelType
                        }
                    };

                    var tank = import.TankStocks.FirstOrDefault(t => FuelMatches(t.FuelType, fuelTypeInDb));

                    if (tank == null)
                    {
                        tank = new AgsTankStock
                        {
                            FuelType = fuelTypeInDb,
                            TankNumber = fuelTypeInDb switch { "HSD" => 1, "MS-I" => 2, "MS-II" or "SPEED" => 3, _ => 1 }
                        };
                        import.TankStocks.Add(tank);
                    }

                    tank.OpeningStockLitres = g.OpeningStock;
                    tank.FuelDispensedLitres = g.FuelDispensed;
                    tank.ReceiptLitres = g.Receipts;

                    double closingStock;
                    if (tank.HasPhysicalDip && tank.DipSource != "DipOnly")
                    {
                        // Keep previously recorded physical measurement
                        closingStock = tank.ClosingStockLitres;
                    }
                    else
                    {
                        // Recompute book stock
                        closingStock = g.CalculatedStock;
                        tank.ClosingStockLitres = closingStock;
                    }

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
                    else if (fuelTypeInDb == "MS-II" || fuelTypeInDb == "SPEED")
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

        var sortedShifts = activeImports
            .OrderBy(s => s.ImportDate)
            .ThenBy(s => _shiftCycleService.NormalizeShiftType(s.ShiftType) switch { "B" => 1, "C" => 2, "A" => 3, _ => 4 })
            .ToList();
        var lastShift = sortedShifts.LastOrDefault();

        var hsdTank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD");
        var msITank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I");
        var msIITank = lastShift?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II" || t.FuelType == "SPEED");

        // Fetch previous shift closings/dips before startDate
        var (prevDate, prevShift) = GetPreviousShift(startDate.Date, _shiftCycleService.GetOpeningShiftForDate(startDate.Date));
        var prevImportRes = await _agsRepo.GetActiveShiftImportAsync(prevDate, prevShift);
        var prevImport = (prevImportRes.Success && prevImportRes.Data != null) ? prevImportRes.Data : null;

        var prevHsdTank = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD");
        var prevMsITank = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I");
        var prevMsIITank = prevImport?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II" || t.FuelType == "SPEED");

        var hsdOpeningStock = (prevHsdTank != null) ? prevHsdTank.ClosingStockLitres : (sortedShifts.FirstOrDefault()?.TankStocks?.FirstOrDefault(t => t.FuelType == "HSD")?.OpeningStockLitres ?? 0.0);
        var msIOpeningStock = (prevMsITank != null) ? prevMsITank.ClosingStockLitres : (sortedShifts.FirstOrDefault()?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-I")?.OpeningStockLitres ?? 0.0);
        var msIIOpeningStock = (prevMsIITank != null) ? prevMsIITank.ClosingStockLitres : (sortedShifts.FirstOrDefault()?.TankStocks?.FirstOrDefault(t => t.FuelType == "MS-II")?.OpeningStockLitres ?? 0.0);

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

        // Filter entries strictly to requested date range
        var validEntries = (loadedEntries ?? new List<DsmEntry>())
            .Where(e => e.Shift == null || (e.Shift.ShiftDate.Date >= startDate.Date && e.Shift.ShiftDate.Date <= endDate.Date))
            .ToList();

        // Deduplicate nozzle readings per (ShiftId, NozzleNumber) to eliminate duplicate entry submissions in the same shift
        var distinctNozzleReadings = validEntries
            .SelectMany(e => (e.NozzleReadings ?? Enumerable.Empty<NozzleReading>()).Select(r => new { Entry = e, Reading = r }))
            .GroupBy(x => new {
                ShiftDate = x.Entry.Shift?.ShiftDate.Date ?? startDate.Date,
                ShiftType = _shiftCycleService.NormalizeShiftType(x.Entry.Shift?.ShiftType ?? "A"),
                ShiftId = x.Entry.ShiftId,
                x.Reading.NozzleNumber
            })
            .Select(g => g.OrderByDescending(x => x.Reading.SaleLitres > 0).ThenByDescending(x => x.Reading.ClosingReading).First().Reading)
            .ToList();

        // Aggregate manual readings
        var manualReadings = new Dictionary<int, (double Opening, double Closing, double Sale)>();
        foreach (var group in distinctNozzleReadings.GroupBy(r => r.NozzleNumber))
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

        foreach (var entry in validEntries)
        {
            foreach (var t in entry.TestingEntries ?? Enumerable.Empty<TestingEntry>())
            {
                var cat = PumpConfiguration.GetTestingTankCategory(t.FuelType, entry.PumpId, entry.Shift?.ShiftDate ?? startDate.Date);
                if (cat.Contains("MS - 20KL", StringComparison.OrdinalIgnoreCase) || cat.Equals("MS", StringComparison.OrdinalIgnoreCase) || cat.Equals("MS-I", StringComparison.OrdinalIgnoreCase))
                    msITesting += t.Litres;
                else if (cat.Contains("20KL II", StringComparison.OrdinalIgnoreCase) || cat.Equals("MS-II", StringComparison.OrdinalIgnoreCase) || cat.Contains("SPEED", StringComparison.OrdinalIgnoreCase))
                    msIITesting += t.Litres;
                else if (cat.Contains("HSD", StringComparison.OrdinalIgnoreCase))
                    hsdTesting += t.Litres;
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

        var stationConfig = _serviceProvider.GetService<IStationConfigurationService>();
        List<TankDefinition> dynamicTanks = new();
        List<PumpMapping> dynamicMappings = new();
        if (stationConfig != null)
        {
            try
            {
                dynamicTanks = (await stationConfig.GetAllTanksAsync()).Where(t => t.IsActive).ToList();
                dynamicMappings = (await stationConfig.GetAllPumpMappingsAsync()).Where(m => m.IsActive).ToList();
            }
            catch { }
        }

        if (dynamicTanks.Count > 0 && dynamicMappings.Count > 0)
        {
            foreach (var tank in dynamicTanks)
            {
                var tankNozzles = dynamicMappings
                    .Where(m => string.Equals(m.TankName, tank.TankName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(m => m.NozzleNumber)
                    .Select(m => CreateItem(m.NozzleNumber, m.FuelType))
                    .ToList();

                if (tankNozzles.Count == 0)
                {
                    tankNozzles = dynamicMappings
                        .Where(m => string.Equals(m.FuelType, tank.FuelType, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(m => m.NozzleNumber)
                        .Select(m => CreateItem(m.NozzleNumber, m.FuelType))
                        .ToList();
                }

                var isCng = string.Equals(tank.FuelType, "CNG", StringComparison.OrdinalIgnoreCase) || 
                            tank.TankName.Contains("CNG", StringComparison.OrdinalIgnoreCase);

                if (tankNozzles.Count == 0 && isCng)
                {
                    tankNozzles = dynamicMappings
                        .Where(m => (m.FuelType != null && m.FuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase)) || 
                                    (m.TankName != null && m.TankName.Contains("CNG", StringComparison.OrdinalIgnoreCase)))
                        .OrderBy(m => m.NozzleNumber)
                        .Select(m => CreateItem(m.NozzleNumber, m.FuelType))
                        .ToList();
                }

                var dispensed = tankNozzles.Sum(x => x.SaleLitres);
                if (dispensed == 0 && loadedEntries != null && isCng)
                {
                    var cngSales = loadedEntries
                        .SelectMany(e => e.NozzleReadings ?? new List<NozzleReading>())
                        .Where(r => string.Equals(r.FuelType, "CNG", StringComparison.OrdinalIgnoreCase) || 
                                    (r.FuelType != null && r.FuelType.Contains("CNG", StringComparison.OrdinalIgnoreCase)))
                        .Sum(r => r.SaleLitres);
                    if (cngSales > 0) dispensed = cngSales;
                }

                double testingLitres = 0;
                if (loadedEntries != null && !isCng)
                {
                    foreach (var entry in loadedEntries)
                    {
                        foreach (var t in entry.TestingEntries ?? Enumerable.Empty<TestingEntry>())
                        {
                            if (IsTestingEntryForTank(t, tank, tankNozzles, entry.PumpId, entry.Shift?.ShiftDate ?? startDate.Date))
                            {
                                testingLitres += t.Litres;
                            }
                        }
                    }
                }

                double receipts = 0;
                var tankerRepo = _serviceProvider.GetService<IFuelTankerRepository>();
                if (tankerRepo != null)
                {
                    var tankersRes = await tankerRepo.GetByDateRangeAsync(startDate.Date, endDate.Date);
                    var tankers = tankersRes.Success && tankersRes.Data != null ? tankersRes.Data : new List<FuelTanker>();
                    receipts = tankers.Where(t => FuelMatches(t.FuelType, tank.FuelType)).Sum(t => t.Quantity);
                }

                var tankStock = lastShift?.TankStocks?.FirstOrDefault(ts => string.Equals(ts.FuelType, tank.FuelType, StringComparison.OrdinalIgnoreCase));
                var prevTankStock = prevImport?.TankStocks?.FirstOrDefault(ts => string.Equals(ts.FuelType, tank.FuelType, StringComparison.OrdinalIgnoreCase));

                double openingStock = (prevTankStock != null) ? prevTankStock.ClosingStockLitres : (tankStock?.OpeningStockLitres ?? 0.0);
                var prov = ResolveMeasurementProvenance(tankStock, lastShift, null, lastShift?.ShiftType ?? "", tank.FuelType);

                var groupDto = new NozzleGroupDto
                {
                    GroupName = $"{tank.FuelType} ({tank.TankName})",
                    FuelType = tank.FuelType,
                    OpeningStock = openingStock,
                    FuelDispensed = dispensed,
                    TestingLitres = testingLitres,
                    Receipts = receipts
                };

                if (prov.HasMeasurement)
                {
                    groupDto.HasPhysicalDip = true;
                    groupDto.DipSource = prov.Source;

                    if (prov.Source == "DipOnly")
                    {
                        groupDto.Dip = prov.DipValue;
                        groupDto.ManualStock = null;
                    }
                    else
                    {
                        groupDto.ManualStock = prov.MeasuredStock;
                        groupDto.Dip = (prov.DipValue > 0.001) ? prov.DipValue : (double?)null;
                    }
                }
                else
                {
                    groupDto.ManualStock = null;
                    groupDto.Dip = null;
                    groupDto.HasPhysicalDip = false;
                    groupDto.DipSource = null;
                }

                for (int i = 0; i < tankNozzles.Count; i += 3)
                {
                    groupDto.Rows.Add(tankNozzles.Skip(i).Take(3).ToList());
                }

                groups.Add(groupDto);
            }

            return groups;
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

        var tankerRepoRangeFallback = _serviceProvider.GetService<IFuelTankerRepository>();
        if (tankerRepoRangeFallback != null)
        {
            var tankersRes = await tankerRepoRangeFallback.GetByDateRangeAsync(startDate.Date, endDate.Date);
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

        var provMsI = ResolveMeasurementProvenance(msITank, lastShift, null, lastShift?.ShiftType ?? "", "MS-I");
        var provHsd = ResolveMeasurementProvenance(hsdTank, lastShift, null, lastShift?.ShiftType ?? "", "HSD");
        var provMsII = ResolveMeasurementProvenance(msIITank, lastShift, null, lastShift?.ShiftType ?? "", "MS-II");

        // Petrol (Tank 1)
        var msGroup1 = new NozzleGroupDto
        {
            GroupName = "Petrol (Tank 1)",
            FuelType = "Petrol",
            OpeningStock = msIOpeningStock,
            FuelDispensed = msIDispensed,
            TestingLitres = msITesting,
            Receipts = msIReceipts
        };
        if (provMsI.HasMeasurement)
        {
            msGroup1.HasPhysicalDip = true;
            msGroup1.DipSource = provMsI.Source;
            if (provMsI.Source == "DipOnly")
            {
                msGroup1.Dip = provMsI.DipValue;
                msGroup1.ManualStock = null;
            }
            else
            {
                msGroup1.ManualStock = provMsI.MeasuredStock;
                msGroup1.Dip = (provMsI.DipValue > 0.001) ? provMsI.DipValue : (double?)null;
            }
        }
        else
        {
            msGroup1.ManualStock = null;
            msGroup1.Dip = null;
            msGroup1.HasPhysicalDip = false;
            msGroup1.DipSource = null;
        }
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
            Receipts = hsdReceipts
        };
        if (provHsd.HasMeasurement)
        {
            hsdGroup2.HasPhysicalDip = true;
            hsdGroup2.DipSource = provHsd.Source;
            if (provHsd.Source == "DipOnly")
            {
                hsdGroup2.Dip = provHsd.DipValue;
                hsdGroup2.ManualStock = null;
            }
            else
            {
                hsdGroup2.ManualStock = provHsd.MeasuredStock;
                hsdGroup2.Dip = (provHsd.DipValue > 0.001) ? provHsd.DipValue : (double?)null;
            }
        }
        else
        {
            hsdGroup2.ManualStock = null;
            hsdGroup2.Dip = null;
            hsdGroup2.HasPhysicalDip = false;
            hsdGroup2.DipSource = null;
        }
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
            Receipts = msIIReceipts
        };
        if (provMsII.HasMeasurement)
        {
            hsdGroup3.HasPhysicalDip = true;
            hsdGroup3.DipSource = provMsII.Source;
            if (provMsII.Source == "DipOnly")
            {
                hsdGroup3.Dip = provMsII.DipValue;
                hsdGroup3.ManualStock = null;
            }
            else
            {
                hsdGroup3.ManualStock = provMsII.MeasuredStock;
                hsdGroup3.Dip = (provMsII.DipValue > 0.001) ? provMsII.DipValue : (double?)null;
            }
        }
        else
        {
            hsdGroup3.ManualStock = null;
            hsdGroup3.Dip = null;
            hsdGroup3.HasPhysicalDip = false;
            hsdGroup3.DipSource = null;
        }
        hsdGroup3.Rows.Add(new List<NozzleDisplayItem> { group3Nozzles[0], group3Nozzles[1] });
        groups.Add(hsdGroup3);

        return groups;
    }

    private static bool IsTestingEntryForTank(TestingEntry t, TankDefinition tank, List<NozzleDisplayItem> tankNozzles, int entryPumpId, DateTime opDate)
    {
        if (t == null) return false;
        var fuelTypeStr = t.FuelType?.Trim() ?? "";

        // 1. Direct nozzle number match in tank nozzles (e.g., "3", "N3")
        if (int.TryParse(fuelTypeStr, out int tNoz))
        {
            if (tankNozzles != null && tankNozzles.Any(n => n.NozzleNumber == tNoz))
                return true;
        }
        else if (fuelTypeStr.StartsWith("N", StringComparison.OrdinalIgnoreCase) && int.TryParse(fuelTypeStr[1..], out int tNoz2))
        {
            if (tankNozzles != null && tankNozzles.Any(n => n.NozzleNumber == tNoz2))
                return true;
        }

        // 2. Direct fuel type or tank name string match
        if (!string.IsNullOrWhiteSpace(tank.FuelType) && string.Equals(fuelTypeStr, tank.FuelType, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(tank.TankName) && string.Equals(fuelTypeStr, tank.TankName, StringComparison.OrdinalIgnoreCase))
            return true;

        // 3. Category match via PumpConfiguration
        var cat = PumpConfiguration.GetTestingTankCategory(fuelTypeStr, entryPumpId, opDate);
        if (!string.IsNullOrWhiteSpace(cat))
        {
            if (!string.IsNullOrWhiteSpace(tank.TankName) && string.Equals(cat, tank.TankName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (cat.Contains("MS - 20KL", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(tank.FuelType, "MS-I", StringComparison.OrdinalIgnoreCase) || string.Equals(tank.FuelType, "MS", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (cat.Contains("20KL II", StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(tank.FuelType, "MS-II", StringComparison.OrdinalIgnoreCase) || string.Equals(tank.FuelType, "SPEED", StringComparison.OrdinalIgnoreCase)))
                return true;

            if (cat.Contains("HSD", StringComparison.OrdinalIgnoreCase) && !cat.Contains("20KL II", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(tank.FuelType, "HSD", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
