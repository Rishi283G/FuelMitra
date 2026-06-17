using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class SyncChangeLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string TableName { get; set; } = string.Empty;

    [Required]
    public int RecordId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Operation { get; set; } = string.Empty; // INSERT, UPDATE, DELETE

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool IsSynced { get; set; } = false;

    public DateTime? SyncedAt { get; set; }

    [MaxLength(100)]
    public string? StationId { get; set; }

    [MaxLength(100)]
    public string? MachineId { get; set; }

    [MaxLength(100)]
    public string? SyncGuid { get; set; }

    [MaxLength(100)]
    public string? RecordGuid { get; set; }
}
