using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

/// <summary>
/// Controls the availability and behavior of individual features, pages, and integrations
/// across Manager (Admin), Owner, and Global roles.
/// </summary>
public class AppFeatureSetting
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// Unique key identifying the feature or page (e.g., "Admin_DsmApprovalQueue", "Owner_ProfitLoss", "Integration_DsmPwa").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string FeatureKey { get; set; } = string.Empty;

    /// <summary>
    /// Target role: "Manager", "Owner", "Global", "Collection", "Station".
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string TargetRole { get; set; } = "Global";

    /// <summary>
    /// User-friendly name displayed in Developer configuration.
    /// </summary>
    [Required]
    [MaxLength(150)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Explanatory description of what the feature enables or disables.
    /// </summary>
    [MaxLength(300)]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Visual grouping category in Developer Tools (e.g. "Operations", "Financials", "Reports", "Integrations").
    /// </summary>
    [MaxLength(100)]
    public string Category { get; set; } = "General";

    /// <summary>
    /// Whether this feature/page is currently active and available for the client.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Display sort order within its category.
    /// </summary>
    public int DisplayOrder { get; set; } = 0;

    /// <summary>
    /// Optional JSON configuration string for advanced settings.
    /// </summary>
    public string? ConfigurationJson { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
