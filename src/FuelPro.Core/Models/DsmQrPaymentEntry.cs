using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DsmQrPaymentEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int? DsmEntryId { get; set; }

    [ForeignKey("DsmEntryId")]
    public DsmEntry? DsmEntry { get; set; }

    [Required]
    [MaxLength(200)]
    public string DsmName { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string TargetDsmName { get; set; } = string.Empty;

    public double Amount { get; set; }

    [MaxLength(100)]
    public string? Tid { get; set; }

    [MaxLength(100)]
    public string? Batch { get; set; }

    [MaxLength(50)]
    public string? Slot { get; set; } // "Morning", "Day", "Night"

    public DateTime Date { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [MaxLength(100)]
    public string SyncGuid { get; set; } = Guid.NewGuid().ToString();
}
