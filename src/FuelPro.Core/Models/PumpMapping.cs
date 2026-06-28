using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

/// <summary>
/// Database-driven pump-to-nozzle-to-fuel mapping.
/// Replaces the hardcoded PumpConfiguration dictionaries.
/// Seeded during station initialization with the specific station's pump layout.
/// </summary>
public class PumpMapping
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int PumpMappingId { get; set; }

    /// <summary>
    /// Operational pump number (1-22 for Shree Mahakaleshwar Petroleum).
    /// </summary>
    [Required]
    public int PumpId { get; set; }

    /// <summary>
    /// Nozzle number within the pump.
    /// </summary>
    [Required]
    public int NozzleNumber { get; set; }

    /// <summary>
    /// Fuel type dispensed by this nozzle: "HSD", "MS-I", "MS-II", "CNG".
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string FuelType { get; set; } = "HSD";

    /// <summary>
    /// Tank this pump is connected to: "Tank 2", "Tank 3", "CNG Line".
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string TankName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this mapping is currently active.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When this mapping was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
