using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DsmPersonalDebtor
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string SyncGuid { get; set; } = Guid.NewGuid().ToString();

    public int? DsmEntryId { get; set; }

    [Required]
    [MaxLength(200)]
    public string DsmName { get; set; } = string.Empty;

    [Required]
    public DateTime Date { get; set; }

    [Required]
    [MaxLength(50)]
    public string Time { get; set; } = string.Empty;

    public double Amount { get; set; }

    [MaxLength(100)]
    public string? FuelProduct { get; set; }

    [MaxLength(500)]
    public string? Remarks { get; set; }

    [Required]
    [MaxLength(50)]
    public string PaymentMethod { get; set; } = "Cash"; // Cash, Card

    public int Denom500 { get; set; }
    public int Denom200 { get; set; }
    public int Denom100 { get; set; }
    public int Denom50 { get; set; }
    public int Denom20 { get; set; }
    public int Denom10 { get; set; }
    public int Coins { get; set; }

    [MaxLength(100)]
    public string? CardTid { get; set; }

    [MaxLength(100)]
    public string? CardBatch { get; set; }

    public int SequenceNumber { get; set; }

    public double RepaidAmount { get; set; }

    public bool DeductFromSalary { get; set; } = true;

    /// <summary>
    /// Conceptual values: "Operational", "OpeningBalance", "DeactivatedOpening".
    /// Defaults to "Operational" so all existing records remain valid.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string EntryType { get; set; } = "Operational";

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [ForeignKey(nameof(DsmEntryId))]
    public DsmEntry? DsmEntry { get; set; }
}
