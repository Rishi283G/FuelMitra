using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class KhandharePetroleumEntry
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
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string SlipNumber { get; set; } = string.Empty;

    public double Amount { get; set; }

    public DateTime Date { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [MaxLength(100)]
    public string SyncGuid { get; set; } = Guid.NewGuid().ToString();
}
