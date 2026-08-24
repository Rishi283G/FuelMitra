using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

/// <summary>
/// Preset configuration for pump-to-nozzle layout and storage tanks.
/// Identified by a unique code and editable name for rapid layout restoration.
/// </summary>
public class StationLayoutPreset
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int PresetId { get; set; }

    /// <summary>
    /// Unique preset code (e.g. "FP-6P12N-A1", "MSS-6P-12N", "PRESET-8492").
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string PresetCode { get; set; } = string.Empty;

    /// <summary>
    /// User-friendly, editable descriptive name for the preset.
    /// </summary>
    [Required]
    [MaxLength(150)]
    public string PresetName { get; set; } = string.Empty;

    /// <summary>
    /// Optional description or notes about this preset.
    /// </summary>
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Serialized JSON containing tank definitions and pump nozzle mappings.
    /// </summary>
    [Required]
    public string LayoutJson { get; set; } = string.Empty;

    /// <summary>
    /// Number of pumps configured in this preset.
    /// </summary>
    public int PumpCount { get; set; }

    /// <summary>
    /// Number of nozzles configured in this preset.
    /// </summary>
    public int NozzleCount { get; set; }

    /// <summary>
    /// Number of storage tanks configured in this preset.
    /// </summary>
    public int TankCount { get; set; }

    /// <summary>
    /// Whether this preset is active / visible in dropdowns.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Timestamp when this preset was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Timestamp when this preset was last updated.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
