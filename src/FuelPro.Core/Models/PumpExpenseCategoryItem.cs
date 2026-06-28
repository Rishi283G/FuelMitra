using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class PumpExpenseCategoryItem
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int PumpExpenseId { get; set; }

    public int CategoryId { get; set; }

    public double Amount { get; set; }

    [MaxLength(500)]
    public string Remarks { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation properties
    [ForeignKey(nameof(PumpExpenseId))]
    public PumpExpense? PumpExpense { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public ExpenseCategory? Category { get; set; }
}
