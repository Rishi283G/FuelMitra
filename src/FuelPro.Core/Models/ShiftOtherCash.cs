using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

/// <summary>
/// Shift-level "Other Cash" or "Cheque" line items displayed in Table E (Fuel Dispensed & Sale).
/// These are not tied to a specific DSM entry — they belong to the shift as a whole.
/// </summary>
public class ShiftOtherCash
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ShiftOtherCashId { get; set; }

    /// <summary>
    /// Optional FK to Shifts table. Null if created before shift record exists.
    /// </summary>
    public int? ShiftId { get; set; }

    [Required]
    public DateTime ShiftDate { get; set; }

    [Required]
    [MaxLength(1)]
    public string ShiftNumber { get; set; } = "A"; // A/B/C

    [Required]
    [MaxLength(200)]
    public string Description { get; set; } = string.Empty;

    public double Amount { get; set; }

    /// <summary>
    /// Whether this row can be edited/deleted on the Final Calculation page.
    /// </summary>
    public bool IsEditable { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation
    [ForeignKey(nameof(ShiftId))]
    public Shift? Shift { get; set; }
}
