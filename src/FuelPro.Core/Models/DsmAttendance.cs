using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DsmAttendance
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DsmAttendanceId { get; set; }

    [Required]
    public int DsmUserId { get; set; }

    [ForeignKey(nameof(DsmUserId))]
    public DsmUser? DsmUser { get; set; }

    [Required]
    public DateTime AttendanceDate { get; set; }

    [Required]
    [MaxLength(10)]
    public string ShiftType { get; set; } = "A";

    public DateTime ClockInTime { get; set; }

    public DateTime? ClockOutTime { get; set; }

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "Present"; // Present, Absent, HalfDay, Leave
}
