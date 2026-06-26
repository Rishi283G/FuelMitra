using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class Shift
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ShiftId { get; set; }

    [Required]
    public DateTime ShiftDate { get; set; }

    [Required]
    [MaxLength(1)]
    public string ShiftType { get; set; } = "A"; // A=Morning, B=Afternoon, C=Night

    public bool IsLocked { get; set; } = false;
    public double CardSettlementPosTotal { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation
    public ICollection<DsmEntry> DsmEntries { get; set; } = new List<DsmEntry>();
    public ICollection<Expense> Expenses { get; set; } = new List<Expense>();
}
