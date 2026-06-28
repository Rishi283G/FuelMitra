using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

/// <summary>
/// Tracks software deployment versions for audit and diagnostic purposes.
/// A new record is created on each application startup if the version has changed.
/// </summary>
public class SoftwareVersionHistory
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// Application version string (e.g. "3.1.0").
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Build configuration (e.g. "Release", "Debug").
    /// </summary>
    [MaxLength(20)]
    public string? BuildConfiguration { get; set; }

    /// <summary>
    /// Machine name where this version was deployed.
    /// </summary>
    [MaxLength(100)]
    public string? MachineName { get; set; }

    /// <summary>
    /// When this version was first deployed/detected.
    /// </summary>
    public DateTime DeployedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Optional notes about this deployment.
    /// </summary>
    [MaxLength(500)]
    public string? Notes { get; set; }
}
