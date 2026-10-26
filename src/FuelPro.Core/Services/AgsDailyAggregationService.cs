using FuelPro.Core.Common;
using FuelPro.Core.DTOs;
using FuelPro.Core.Models.AGS;
using Newtonsoft.Json;
using Serilog;

namespace FuelPro.Core.Services;

public class AgsDailyAggregationService : IAgsDailyAggregationService
{
    private static readonly ILogger _logger = Log.ForContext<AgsDailyAggregationService>();

    public AgsDailySummary Aggregate(DateTime date, List<AgsShiftImport> shiftsForDay)
    {
        var summary = new AgsDailySummary
        {
            SummaryDate   = date.Date,
            LastUpdatedAt = DateTime.Now,
        };

        // Only aggregate active imports
        var active = shiftsForDay.Where(s => s.IsActive).ToList();

        summary.ShiftAImported = active.Any(s => s.ShiftType == "A" || s.ShiftType == "I");
        summary.ShiftBImported = active.Any(s => s.ShiftType == "B" || s.ShiftType == "II");
        summary.ShiftCImported = active.Any(s => s.ShiftType == "C" || s.ShiftType == "III");



        // Per-nozzle day sales
        var nozzleSales = new Dictionary<int, double>();
        foreach (var nozzleInfo in PumpConfiguration.AllNozzles)
        {
            var p = nozzleInfo.PumpId;
            var n = nozzleInfo.NozzleNumber;
            int key = p * 100 + n;
            nozzleSales[key] = active
                .SelectMany(s => s.NozzleReadings)
                .Where(r => r.NozzleNumber == n && r.PumpNumber == p)
                .Sum(r => r.NetSaleLitres);
        }
        summary.NozzleDaySalesJson = JsonConvert.SerializeObject(nozzleSales);

        // Fuel-type day totals (aggregated from nozzle sales using central PumpConfiguration)
        summary.DayTotalHsdLitres  = active.SelectMany(s => s.NozzleReadings)
            .Where(r => GetFuelTypeSafe(r.PumpNumber, r.NozzleNumber, date) == "HSD")
            .Sum(r => r.NetSaleLitres);
        summary.DayTotalMsILitres  = active.SelectMany(s => s.NozzleReadings)
            .Where(r => GetFuelTypeSafe(r.PumpNumber, r.NozzleNumber, date) == "MS-I")
            .Sum(r => r.NetSaleLitres);
        summary.DayTotalMsIILitres = active.SelectMany(s => s.NozzleReadings)
            .Where(r => GetFuelTypeSafe(r.PumpNumber, r.NozzleNumber, date) == "MS-II")
            .Sum(r => r.NetSaleLitres);

        // Tank stock: Shift B opening (day opening) → Shift A closing (day closing)
        // With provisional fallbacks for in-progress operational days
        var shiftB = active.FirstOrDefault(s => s.ShiftType == "B" || s.ShiftType == "II");
        var shiftC = active.FirstOrDefault(s => s.ShiftType == "C" || s.ShiftType == "III");
        var shiftA = active.FirstOrDefault(s => s.ShiftType == "A" || s.ShiftType == "I");

        var openingShift = shiftB ?? shiftC ?? shiftA;
        var closingShift = shiftA ?? shiftC ?? shiftB;

        summary.HsdDayOpeningStock  = openingShift?.HsdOpeningStock ?? 0;
        summary.MsIDayOpeningStock  = openingShift?.MsIOpeningStock ?? 0;
        summary.MsIIDayOpeningStock = openingShift?.MsIIOpeningStock ?? 0;

        summary.HsdDayClosingStock  = closingShift?.HsdClosingStock ?? 0;
        summary.MsIDayClosingStock  = closingShift?.MsIClosingStock ?? 0;
        summary.MsIIDayClosingStock = closingShift?.MsIIClosingStock ?? 0;

        // Per-shift breakdown JSON (carries tank measurement provenance across synchronized clients)
        var breakdown = new Dictionary<string, object>();
        foreach (var shift in active)
        {
            double hsd = 0, msI = 0, msII = 0;
            if (shift.NozzleReadings != null && shift.NozzleReadings.Any())
            {
                hsd  = shift.NozzleReadings.Where(r => GetFuelTypeSafe(r.PumpNumber, r.NozzleNumber, shift.ImportDate) == "HSD"  ).Sum(r => r.NetSaleLitres);
                msI  = shift.NozzleReadings.Where(r => GetFuelTypeSafe(r.PumpNumber, r.NozzleNumber, shift.ImportDate) == "MS-I" ).Sum(r => r.NetSaleLitres);
                msII = shift.NozzleReadings.Where(r => GetFuelTypeSafe(r.PumpNumber, r.NozzleNumber, shift.ImportDate) == "MS-II").Sum(r => r.NetSaleLitres);
            }
            else
            {
                hsd  = shift.TotalHsdLitres;
                msI  = shift.TotalMsILitres;
                msII = shift.TotalMsIILitres;
            }

            var tankMeasurements = new Dictionary<string, object>();
            if (shift.TankStocks != null && shift.TankStocks.Any())
            {
                foreach (var ts in shift.TankStocks)
                {
                    bool isAgsPdf = !string.IsNullOrEmpty(shift.PdfFileName) &&
                                    shift.PdfFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
                    bool hasPhysical = ts.HasPhysicalDip || isAgsPdf || ts.ClosingDipMM > 0.001;
                    string src = ts.DipSource ?? (isAgsPdf ? "AgsGauge" : (ts.ClosingDipMM > 0.001 ? "Manual" : "None"));

                    tankMeasurements[ts.FuelType] = new
                    {
                        hasDip = hasPhysical,
                        dipMm = ts.ClosingDipMM,
                        measuredStock = hasPhysical ? (double?)ts.ClosingStockLitres : null,
                        source = src
                    };
                }
            }

            breakdown[shift.ShiftType] = new
            {
                hsd   = hsd,
                msI   = msI,
                msII  = msII,
                total = hsd + msI + msII,
                importedAt = shift.ImportedAt.ToString("hh:mm tt"),
                importedBy = shift.ImportedBy,
                tankMeasurements = tankMeasurements
            };
        }
        summary.ShiftBreakdownJson = JsonConvert.SerializeObject(breakdown);

        _logger.Information("Daily summary aggregated for {Date}: HSD={HSD:F0}L MS-I={MSI:F0}L MS-II={MSII:F0}L Shifts={Shifts}",
            date.ToString("dd-MMM-yyyy"), summary.DayTotalHsdLitres, summary.DayTotalMsILitres, summary.DayTotalMsIILitres,
            active.Count);

        return summary;
    }

    public static TankMeasurementSyncDto? ParseTankMeasurement(string? breakdownJson, string? shiftType, string fuelType)
    {
        if (string.IsNullOrWhiteSpace(breakdownJson) || string.IsNullOrWhiteSpace(shiftType))
            return null;

        try
        {
            var dict = JsonConvert.DeserializeObject<Dictionary<string, Newtonsoft.Json.Linq.JObject>>(breakdownJson);
            if (dict == null) return null;

            var normShift = shiftType.Trim().ToUpperInvariant() switch
            {
                "I" or "SHIFT I" => "A",
                "II" or "SHIFT II" => "B",
                "III" or "SHIFT III" => "C",
                var s => s
            };

            if (!dict.TryGetValue(normShift, out var shiftObj) || shiftObj == null)
                return null;

            var tmObj = shiftObj["tankMeasurements"] as Newtonsoft.Json.Linq.JObject;
            if (tmObj == null) return null;

            if (!tmObj.TryGetValue(fuelType, StringComparison.OrdinalIgnoreCase, out var tankToken) || tankToken is not Newtonsoft.Json.Linq.JObject tankData)
                return null;

            return new TankMeasurementSyncDto
            {
                HasDip = tankData.Value<bool>("hasDip"),
                DipMm = tankData.Value<double>("dipMm"),
                MeasuredStock = tankData["measuredStock"]?.Type != Newtonsoft.Json.Linq.JTokenType.Null ? tankData.Value<double?>("measuredStock") : null,
                Source = tankData.Value<string>("source") ?? "None"
            };
        }
        catch
        {
            return null;
        }
    }

    public AgsDailySummaryDto ToDto(AgsDailySummary summary, List<AgsShiftImport> shiftsForDay)
    {
        var dto = new AgsDailySummaryDto
        {
            Date                = summary.SummaryDate,
            DayTotalHsdLitres   = summary.DayTotalHsdLitres,
            DayTotalMsILitres   = summary.DayTotalMsILitres,
            DayTotalMsIILitres  = summary.DayTotalMsIILitres,
            HsdDayOpeningStock  = summary.HsdDayOpeningStock,
            HsdDayClosingStock  = summary.HsdDayClosingStock,
            MsIDayOpeningStock  = summary.MsIDayOpeningStock,
            MsIDayClosingStock  = summary.MsIDayClosingStock,
            MsIIDayOpeningStock = summary.MsIIDayOpeningStock,
            MsIIDayClosingStock = summary.MsIIDayClosingStock,
            ShiftAImported      = summary.ShiftAImported,
            ShiftBImported      = summary.ShiftBImported,
            ShiftCImported      = summary.ShiftCImported,
        };

        // Deserialize nozzle sales
        try
        {
            dto.NozzleDaySales = JsonConvert.DeserializeObject<Dictionary<int, double>>(summary.NozzleDaySalesJson) ?? new();
        }
        catch { dto.NozzleDaySales = new(); }

        // Per-shift breakdown DTOs
        var active = shiftsForDay.Where(s => s.IsActive).ToList();
        AgsShiftBreakdownDto? MakeBreakdown(string shiftType)
        {
            var s = active.FirstOrDefault(x => x.ShiftType == shiftType);
            if (s == null) return null;

            double hsd = 0, msI = 0, msII = 0;
            if (s.NozzleReadings != null && s.NozzleReadings.Any())
            {
                hsd  = s.NozzleReadings.Where(r => GetFuelTypeSafe(r.PumpNumber, r.NozzleNumber, s.ImportDate) == "HSD"  ).Sum(r => r.NetSaleLitres);
                msI  = s.NozzleReadings.Where(r => GetFuelTypeSafe(r.PumpNumber, r.NozzleNumber, s.ImportDate) == "MS-I" ).Sum(r => r.NetSaleLitres);
                msII = s.NozzleReadings.Where(r => GetFuelTypeSafe(r.PumpNumber, r.NozzleNumber, s.ImportDate) == "MS-II").Sum(r => r.NetSaleLitres);
            }
            else
            {
                hsd  = s.TotalHsdLitres;
                msI  = s.TotalMsILitres;
                msII = s.TotalMsIILitres;
            }

            return new AgsShiftBreakdownDto
            {
                ShiftType  = shiftType,
                HsdLitres  = hsd,
                MsILitres  = msI,
                MsIILitres = msII,
                ImportedAt = s.ImportedAt,
                ImportedBy = s.ImportedBy,
            };
        }

        dto.ShiftAData = MakeBreakdown("A");
        dto.ShiftBData = MakeBreakdown("B");
        dto.ShiftCData = MakeBreakdown("C");

        return dto;
    }

    private string GetFuelTypeSafe(int pumpNum, int nozzleNum, DateTime d)
    {
        try
        {
            return PumpConfiguration.GetFuelTypeDisplayName(pumpNum, nozzleNum, d);
        }
        catch
        {
            return "";
        }
    }
}
