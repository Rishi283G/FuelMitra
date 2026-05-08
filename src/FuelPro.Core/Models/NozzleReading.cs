using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class NozzleReading
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int NozzleReadingId { get; set; }

    [Required]
    public int DsmEntryId { get; set; }

    [Required]
    public int NozzleNumber { get; set; }

    [Required]
    [MaxLength(10)]
    public string FuelType { get; set; } = "HSD"; // HSD, MS-I, MS-II

    public double OpeningReading { get; set; }
    public double ClosingReading { get; set; }

    /// <summary>
    /// Computed: ClosingReading - OpeningReading. Stored for query performance.
    /// </summary>
    public double SaleLitres { get; set; }

    /// <summary>
    /// Rate per litre at time of entry, fetched from Settings.
    /// </summary>
    public double Rate { get; set; }

    /// <summary>
    /// Computed: SaleLitres * Rate. Stored for query performance.
    /// </summary>
    public double Amount { get; set; }
    public bool IsManualOpeningOverride { get; set; }

    // Navigation
    [ForeignKey(nameof(DsmEntryId))]
    public DsmEntry? DsmEntry { get; set; }
}
