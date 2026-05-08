using FuelPro.Core.DTOs;
using FuelPro.Core.Models.AGS;
using Newtonsoft.Json;
using Serilog;

namespace FuelPro.Core.Services;

public class AgsDailyAggregationService : IAgsDailyAggregationService
{
    private static readonly ILogger _logger = Log.ForContext<AgsDailyAggregationService>();

    // Hardcoded nozzle → fuel type map (same as parser)
    private static readonly Dictionary<int, string> NozzleFuelMap = new()
    {
        { 1,"HSD"  },{ 2,"HSD"  },{ 3,"MS-II"},{ 4,"MS-II"},
        { 5,"MS-I" },{ 6,"MS-I" },{ 7,"HSD"  },{ 8,"HSD"  },
        { 9,"MS-II"},{10,"MS-II"},{11,"MS-I" },{12,"MS-I" },
        {13,"MS-II"},{14,"MS-II"},{15,"MS-I" },{16,"MS-I" },
        {17,"MS-II"},{18,"MS-II"},{19,"MS-I" },{20,"MS-I" },
        {21,"MS-II"},{22,"MS-II"},{23,"MS-I" },{24,"MS-I" },
        {25,"MS-II"},{26,"MS-II"},{27,"MS-I" },{28,"MS-I" },
    };

    public AgsDailySummary Aggregate(DateTime date, List<AgsShiftImport> shiftsForDay)
    {
        var summary = new AgsDailySummary
        {
            SummaryDate   = date.Date,
            LastUpdatedAt = DateTime.Now,
        };

        // Only aggregate active imports
        var active = shiftsForDay.Where(s => s.IsActive).ToList();

        summary.ShiftAImported = active.Any(s => s.ShiftType == "A");
        summary.ShiftBImported = active.Any(s => s.ShiftType == "B");
        summary.ShiftCImported = active.Any(s => s.ShiftType == "C");

        // Per-nozzle day sales
        var nozzleSales = new Dictionary<int, double>();
        for (int n = 1; n <= 28; n++)
        {
            nozzleSales[n] = active
                .SelectMany(s => s.NozzleReadings)
                .Where(r => r.NozzleNumber == n)
                .Sum(r => r.NetSaleLitres);
        }
        summary.NozzleDaySalesJson = JsonConvert.SerializeObject(nozzleSales);

        // Fuel-type day totals (aggregated from nozzle sales)
        summary.DayTotalHsdLitres  = nozzleSales.Where(kv => NozzleFuelMap.TryGetValue(kv.Key, out var ft) && ft == "HSD"  ).Sum(kv => kv.Value);
        summary.DayTotalMsILitres  = nozzleSales.Where(kv => NozzleFuelMap.TryGetValue(kv.Key, out var ft) && ft == "MS-I" ).Sum(kv => kv.Value);
        summary.DayTotalMsIILitres = nozzleSales.Where(kv => NozzleFuelMap.TryGetValue(kv.Key, out var ft) && ft == "MS-II").Sum(kv => kv.Value);

        // Tank stock: Shift A opening → Shift C closing
        var shiftA = active.FirstOrDefault(s => s.ShiftType == "A");
        var shiftC = active.FirstOrDefault(s => s.ShiftType == "C");

        if (shiftA != null)
        {
            summary.HsdDayOpeningStock  = shiftA.HsdOpeningStock;
            summary.MsIDayOpeningStock  = shiftA.MsIOpeningStock;
            summary.MsIIDayOpeningStock = shiftA.MsIIOpeningStock;
        }
        if (shiftC != null)
        {
            summary.HsdDayClosingStock  = shiftC.HsdClosingStock;
            summary.MsIDayClosingStock  = shiftC.MsIClosingStock;
            summary.MsIIDayClosingStock = shiftC.MsIIClosingStock;
        }
        else if (active.Any())
        {
            // Use the last available shift's closing stock as best approximation
            var lastShift = active.OrderByDescending(s => s.ShiftType).First();
            summary.HsdDayClosingStock  = lastShift.HsdClosingStock;
            summary.MsIDayClosingStock  = lastShift.MsIClosingStock;
            summary.MsIIDayClosingStock = lastShift.MsIIClosingStock;
        }

        // Per-shift breakdown JSON
        var breakdown = new Dictionary<string, object>();
        foreach (var shift in active)
        {
            breakdown[shift.ShiftType] = new
            {
                hsd   = shift.TotalHsdLitres,
                msI   = shift.TotalMsILitres,
                msII  = shift.TotalMsIILitres,
                total = shift.TotalHsdLitres + shift.TotalMsILitres + shift.TotalMsIILitres,
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
            return s == null ? null : new AgsShiftBreakdownDto
            {
                ShiftType  = shiftType,
                HsdLitres  = s.TotalHsdLitres,
                MsILitres  = s.TotalMsILitres,
                MsIILitres = s.TotalMsIILitres,
                ImportedAt = s.ImportedAt,
                ImportedBy = s.ImportedBy,
            };
        }

        dto.ShiftAData = MakeBreakdown("A");
        dto.ShiftBData = MakeBreakdown("B");
        dto.ShiftCData = MakeBreakdown("C");

        return dto;
    }
}
