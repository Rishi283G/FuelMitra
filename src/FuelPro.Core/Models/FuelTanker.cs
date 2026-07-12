using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class FuelTanker
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int FuelTankerId { get; set; }

    [Required]
    public DateTime TankerDate { get; set; }

    [MaxLength(50)]
    public string? TankerNumber { get; set; }

    [Required]
    [MaxLength(100)]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string FuelType { get; set; } = "HSD"; // "HSD", "MS-I", "MS-II", "CNG"

    public double Quantity { get; set; }
    public double PurchaseRate { get; set; }
    public double TotalAmount { get; set; }
    public double Density { get; set; }

    [MaxLength(500)]
    public string? Remarks { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
