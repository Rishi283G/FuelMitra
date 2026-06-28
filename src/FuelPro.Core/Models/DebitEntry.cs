using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DebitEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DebitId { get; set; }

    [Required]
    public int DsmEntryId { get; set; }

    [Required]
    [MaxLength(200)]
    public string DebtorName { get; set; } = string.Empty;

    public double Amount { get; set; }

    [MaxLength(100)]
    public string? ChequeNo { get; set; }

    [MaxLength(50)]
    public string? VehicleNumber { get; set; }

    [MaxLength(100)]
    public string? SlipNumber { get; set; }

    [MaxLength(500)]
    public string? Remarks { get; set; }

    [MaxLength(50)]
    public string? Fuel { get; set; }

    [MaxLength(50)]
    public string? EntryTime { get; set; }

    [Required]
    [MaxLength(50)]
    public string PaymentMethod { get; set; } = "Credit"; // Credit, Cash, Card

    [MaxLength(100)]
    public string? CardTid { get; set; }

    [MaxLength(100)]
    public string? CardBatch { get; set; }

    public int Denom500 { get; set; }
    public int Denom200 { get; set; }
    public int Denom100 { get; set; }
    public int Denom50 { get; set; }
    public int Denom20 { get; set; }
    public int Denom10 { get; set; }
    public int Coins { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation
    [ForeignKey(nameof(DsmEntryId))]
    public DsmEntry? DsmEntry { get; set; }
}
