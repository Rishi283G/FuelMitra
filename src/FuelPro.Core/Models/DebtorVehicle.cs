using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DebtorVehicle
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DebtorVehicleId { get; set; }

    [Required]
    public int CreditorId { get; set; }

    [Required]
    [MaxLength(50)]
    public string VehicleNumber { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation
    [ForeignKey(nameof(CreditorId))]
    public Creditor? Creditor { get; set; }
}
