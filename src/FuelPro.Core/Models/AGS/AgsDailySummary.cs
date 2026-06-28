using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models.AGS;

/// <summary>
/// One row per date. Aggregated from all active shift imports for that day.
/// Updated after every shift import. NozzleDaySalesJson stores per-nozzle totals as JSON.
/// </summary>
public class AgsDailySummary
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int AgsDailySummaryId { get; set; }

    [Required]
    public DateTime SummaryDate { get; set; }

    // Day totals (sum across all imported shifts)
    public double DayTotalHsdLitres { get; set; }
    public double DayTotalMsILitres { get; set; }
    public double DayTotalMsIILitres { get; set; }

    // Tank stock: Shift A opening → Shift B closing
    public double HsdDayOpeningStock { get; set; }
    public double HsdDayClosingStock { get; set; }
    public double MsIDayOpeningStock { get; set; }
    public double MsIDayClosingStock { get; set; }
    public double MsIIDayOpeningStock { get; set; }
    public double MsIIDayClosingStock { get; set; }

    // Shift import flags
    public bool ShiftAImported { get; set; }
    public bool ShiftBImported { get; set; }
    public bool ShiftCImported { get; set; }

    public DateTime LastUpdatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// JSON-serialized Dictionary&lt;int, double&gt; — nozzle number → day total litres.
    /// Stored as TEXT in SQLite to avoid a separate 28-row join table.
    /// </summary>
    public string NozzleDaySalesJson { get; set; } = "{}";

    /// <summary>Per-shift HSD/MS-I/MS-II totals stored as JSON for the breakdown table.</summary>
    public string ShiftBreakdownJson { get; set; } = "{}";
}
