using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

/// <summary>
/// Configurable station tank definition.
/// Defines the storage tanks available at the station.
/// </summary>
public class TankDefinition
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int TankId { get; set; }

    /// <summary>
    /// Tank Name/Identifier (e.g. "Tank 1", "MS - 20KL", "HSD - 20KL", "HSD - 20KL II", "CNG Line").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string TankName { get; set; } = string.Empty;

    /// <summary>
    /// Tank capacity in Kilolitres (KL) or equivalent unit.
    /// </summary>
    public double CapacityKL { get; set; } = 20.0;

    /// <summary>
    /// Fuel product code stored in this tank (e.g. "MS-I", "MS-II", "HSD", "CNG", "XP95").
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string FuelType { get; set; } = "HSD";

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Whether this tank supports testing functionality (e.g. true for Liquid fuels, false for CNG).
    /// </summary>
    public bool HasTesting { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
