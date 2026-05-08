using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models.AGS;

public class AgsNozzleReading
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int AgsNozzleReadingId { get; set; }

    [Required]
    public int AgsShiftImportId { get; set; }

    /// <summary>Physical nozzle number 1–28 (mapped from AGS internal numbering).</summary>
    [Required]
    public int NozzleNumber { get; set; }

    /// <summary>"HSD", "MS-I", or "MS-II" — stamped from hardcoded NozzleFuelMap at import time.</summary>
    [Required]
    [MaxLength(10)]
    public string FuelType { get; set; } = "HSD";

    /// <summary>Physical pump number 1–12 — stamped from hardcoded NozzlePumpMap at import time.</summary>
    public int PumpNumber { get; set; }

    public double OpeningReading { get; set; }
    public double ClosingReading { get; set; }

    /// <summary>Stored computed value: ClosingReading - OpeningReading.</summary>
    public double SaleLitres { get; set; }

    /// <summary>Testing/calibration deduction in litres (from AGS calibration data).</summary>
    public double TestingDeduction { get; set; }

    /// <summary>Stored computed value: SaleLitres - TestingDeduction.</summary>
    public double NetSaleLitres { get; set; }

    // Navigation
    [ForeignKey(nameof(AgsShiftImportId))]
    public AgsShiftImport? AgsShiftImport { get; set; }
}
