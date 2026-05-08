using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class Expense
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ExpenseId { get; set; }

    /// <summary>
    /// Nullable: null means shift-level expense.
    /// </summary>
    public int? DsmEntryId { get; set; }

    /// <summary>
    /// Nullable: links to shift if shift-level expense.
    /// </summary>
    public int? ShiftId { get; set; }

    [Required]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    public double Amount { get; set; }

    // Navigation
    [ForeignKey(nameof(DsmEntryId))]
    public DsmEntry? DsmEntry { get; set; }

    [ForeignKey(nameof(ShiftId))]
    public Shift? Shift { get; set; }
}
