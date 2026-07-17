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

        // Tank stock: Shift B opening (starts 8:00 AM) → Shift A closing (ends 8:00 AM next day)
        var shiftA = active.FirstOrDefault(s => s.ShiftType == "A" || s.ShiftType == "I");
        var shiftB = active.FirstOrDefault(s => s.ShiftType == "B" || s.ShiftType == "II");

        summary.HsdDayOpeningStock  = shiftB?.HsdOpeningStock ?? shiftA?.HsdOpeningStock ?? 0;
        summary.MsIDayOpeningStock  = shiftB?.MsIOpeningStock ?? shiftA?.MsIOpeningStock ?? 0;
        summary.MsIIDayOpeningStock = shiftB?.MsIIOpeningStock ?? shiftA?.MsIIOpeningStock ?? 0;

        summary.HsdDayClosingStock  = shiftA?.HsdClosingStock ?? shiftB?.HsdClosingStock ?? 0;
        summary.MsIDayClosingStock  = shiftA?.MsIClosingStock ?? shiftB?.MsIClosingStock ?? 0;
        summary.MsIIDayClosingStock = shiftA?.MsIIClosingStock ?? shiftB?.MsIIClosingStock ?? 0;

        // Per-shift breakdown JSON
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

            breakdown[shift.ShiftType] = new
            {
                hsd   = hsd,
                msI   = msI,
                msII  = msII,
                total = hsd + msI + msII,
                importedAt = shift.ImportedAt.ToString("hh:mm tt"),
                importedBy = shift.ImportedBy,
            };
        }
        summary.ShiftBreakdownJson = JsonConvert.SerializeObject(breakdown);

        _logger.Information("Daily summary aggregated for {Date}: HSD={HSD:F0}L MS-I={MSI:F0}L MS-II={MSII:F0}L Shifts={Shifts}",
            date.ToString("dd-MMM-yyyy"), summary.DayTotalHsdLitres, summary.DayTotalMsILitres, summary.DayTotalMsIILitres,
            active.Count);

        return summary;
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
