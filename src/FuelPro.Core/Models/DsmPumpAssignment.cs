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

    [Required]
    [MaxLength(10)]
    public string ShiftType { get; set; } = "A"; // 'A', 'B', 'C'

    public bool IsActive { get; set; } = true;

    public DateTime AssignedDate { get; set; } = DateTime.Now;
}
