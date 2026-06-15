using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

/// <summary>
/// Persists the mapping between a remote record's local_id (from Supabase)
/// and the auto-generated local primary key on this machine.
/// Used during pull sync to correctly remap foreign keys.
/// </summary>
public class SyncIdMapping
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// The unique primary key GUID in the cloud (stored as SyncGuid in Supabase).
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string RemoteGuid { get; set; } = string.Empty;

    /// <summary>
    /// The auto-generated primary key on this machine's local SQLite database.
    /// </summary>
    public int LocalId { get; set; }
}
