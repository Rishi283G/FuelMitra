using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DsmPumpAssignment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DsmPumpAssignmentId { get; set; }

    [Required]
    public int DsmUserId { get; set; }

    [ForeignKey(nameof(DsmUserId))]
    public DsmUser? DsmUser { get; set; }

    [Required]
    public int PumpId { get; set; }

    public int? ConnectedPumpId { get; set; }
    public string? ConnectedPumpIdsJson { get; set; }

    /// <summary>
    /// Resolves all connected pump IDs (excluding primary) with legacy fallback.
    /// </summary>
    public List<int> GetEffectiveConnectedPumpIds()
    {
        return PumpConnectionConfiguration.ResolveEffectiveConnectedPumpIds(ConnectedPumpId, ConnectedPumpIdsJson)
            .Where(id => id != PumpId)
            .ToList();
    }

    [Required]
    [MaxLength(10)]
    public string ShiftType { get; set; } = "A"; // 'A', 'B', 'C'

    public bool IsActive { get; set; } = true;

    public DateTime AssignedDate { get; set; } = DateTime.Now;

    public DateTime? CompletedDate { get; set; }

    [NotMapped]
    public string DisplayPumps
    {
        get
        {
            var effectiveConnected = GetEffectiveConnectedPumpIds();
            if (effectiveConnected.Count > 0)
            {
                return $"Pump {PumpId} + " + string.Join(" + ", effectiveConnected.Select(id => $"Pump {id}"));
            }
            return $"Pump {PumpId}";
        }
    }
}
