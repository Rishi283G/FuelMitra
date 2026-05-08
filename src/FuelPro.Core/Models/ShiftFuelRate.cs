using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

/// <summary>
/// Per-shift fuel rate override. Allows the operator to change the rate displayed
/// on the Final Calculation page without modifying the global Settings rates.
/// </summary>
public class ShiftFuelRate
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ShiftFuelRateId { get; set; }

    /// <summary>
    /// Optional FK to Shifts table.
    /// </summary>
    public int? ShiftId { get; set; }

    [Required]
    public DateTime ShiftDate { get; set; }

    [Required]
    [MaxLength(1)]
    public string ShiftNumber { get; set; } = "A"; // A/B/C

    [Required]
    [MaxLength(10)]
    public string FuelType { get; set; } = "HSD"; // HSD, MS-I, MS-II

    public double OverrideRate { get; set; }

    // Navigation
    [ForeignKey(nameof(ShiftId))]
    public Shift? Shift { get; set; }
}
