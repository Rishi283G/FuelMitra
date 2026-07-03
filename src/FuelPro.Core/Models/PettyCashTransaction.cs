using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class PettyCashTransaction
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int TransactionId { get; set; }

    [Required]
    public DateTime Date { get; set; }

    [Required]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    public double Amount { get; set; } // Positive for addition, negative for deduction

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = "Addition"; // "Addition" or "Deduction"

    public int? ShiftExpenseId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
