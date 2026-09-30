using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

/// <summary>
/// Represents a historical opening balance for an entity (Debtor or DSM Loss).
/// Used as the starting baseline before operational tracking in PyroSync Dynamic.
/// </summary>
public class OpeningBalance
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int OpeningBalanceId { get; set; }

    [Required]
    [MaxLength(50)]
    public string SyncGuid { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Supported conceptual values: "Debtor", "DsmLoss"
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the entity: Creditor/Debtor Name or DSM Name.
    /// </summary>
    [Required]
    [MaxLength(200)]
    public string EntityIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// Optional foreign key to Creditor (when EntityType is "Debtor").
    /// </summary>
    public int? CreditorId { get; set; }

    [Required]
    public DateTime OpeningDate { get; set; }

    public double Amount { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    // Navigation
    [ForeignKey(nameof(CreditorId))]
    public Creditor? Creditor { get; set; }
}
