using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models.AGS;

/// <summary>
/// One row per AGS PDF import (one per shift per day).
/// IsActive = false means soft-deleted (superseded by a re-import).
/// </summary>
public class AgsShiftImport
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int AgsShiftImportId { get; set; }

    [Required]
    public DateTime ImportDate { get; set; }

    /// <summary>"A", "B", or "C"</summary>
    [Required]
    [MaxLength(1)]
    public string ShiftType { get; set; } = "A";

    public DateTime ImportedAt { get; set; } = DateTime.Now;

    [MaxLength(255)]
    public string PdfFileName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ImportedBy { get; set; } = string.Empty;

    /// <summary>False when superseded by a re-import (soft delete).</summary>
    public bool IsActive { get; set; } = true;

    // ---- Denormalized summary columns for fast dashboard queries ----
    public double TotalHsdLitres { get; set; }
    public double TotalMsILitres { get; set; }
    public double TotalMsIILitres { get; set; }

    public double HsdOpeningStock { get; set; }
    public double HsdClosingStock { get; set; }
    public double MsIOpeningStock { get; set; }
    public double MsIClosingStock { get; set; }
    public double MsIIOpeningStock { get; set; }
    public double MsIIClosingStock { get; set; }

    // Optional: period timestamps extracted from the PDF header
    [MaxLength(50)]
    public string? PdfPeriodFrom { get; set; }
    [MaxLength(50)]
    public string? PdfPeriodTo { get; set; }

    // Navigation
    public ICollection<AgsNozzleReading> NozzleReadings { get; set; } = new List<AgsNozzleReading>();
    public ICollection<AgsTankStock> TankStocks { get; set; } = new List<AgsTankStock>();
}
