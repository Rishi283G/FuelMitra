using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DsmDevice
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DsmDeviceId { get; set; }

    [Required]
    public int DsmUserId { get; set; }

    [ForeignKey(nameof(DsmUserId))]
    public DsmUser? DsmUser { get; set; }

    [Required]
    [MaxLength(200)]
    public string DeviceId { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string DeviceName { get; set; } = string.Empty;

    public DateTime LastLogin { get; set; } = DateTime.Now;

    public DateTime LastSeen { get; set; } = DateTime.Now;

    public bool IsActive { get; set; } = true;
}
