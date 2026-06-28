using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

/// <summary>
/// Centralized audit log for tracking all financial modifications.
/// Records create/update/delete operations on financial entities.
/// </summary>
public class AuditLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int AuditLogId { get; set; }

    /// <summary>
    /// Name of the table being modified (e.g. "DebitEntries", "CreditorRepayments").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Primary key of the modified record in the target table.
    /// </summary>
    public int RecordId { get; set; }

    /// <summary>
    /// Type of modification: "Create", "Update", "Delete", "Lock", "Unlock".
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Action { get; set; } = "Update";

    /// <summary>
    /// Which field was changed (null for Create/Delete actions).
    /// </summary>
    [MaxLength(100)]
    public string? FieldName { get; set; }

    /// <summary>
    /// Previous value before modification (JSON for complex types).
    /// </summary>
    [MaxLength(2000)]
    public string? OldValue { get; set; }

    /// <summary>
    /// New value after modification (JSON for complex types).
    /// </summary>
    [MaxLength(2000)]
    public string? NewValue { get; set; }

    /// <summary>
    /// Username of the person who made the modification.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ModifiedBy { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp of the modification.
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// Required for Owner financial edits; optional for other operations.
    /// Stores the reason/justification for the modification.
    /// </summary>
    [MaxLength(500)]
    public string? Reason { get; set; }
}
