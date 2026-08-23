using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

/// <summary>
/// Dynamic collection line item attached to a DSM PaymentCollection record.
/// Allows recording arbitrary configured collection types with optional TID/Batch.
/// </summary>
public class PaymentCollectionItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    public int PaymentId { get; set; }

    /// <summary>
    /// Code matching CollectionTypeMaster.Code (e.g., "SBI_REDEEM", "PAYTM", "QR", "MOBIKWIK").
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string CollectionTypeCode { get; set; } = string.Empty;

    public double Amount { get; set; }

    [MaxLength(50)]
    public string? Tid { get; set; }

    [MaxLength(50)]
    public string? Batch { get; set; }

    /// <summary>
    /// Slot identifier (e.g. "General", or legacy "Morning", "Day", "Night").
    /// </summary>
    [MaxLength(20)]
    public string? Slot { get; set; } = "General";

    [ForeignKey(nameof(PaymentId))]
    public PaymentCollection? PaymentCollection { get; set; }
}
