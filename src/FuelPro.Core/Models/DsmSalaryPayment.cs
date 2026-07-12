using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DsmSalaryPayment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DsmSalaryPaymentId { get; set; }

    [Required]
    public int DsmProfileId { get; set; }

    [ForeignKey(nameof(DsmProfileId))]
    public DsmProfile? DsmProfile { get; set; }

    public int Year { get; set; }
    public int Month { get; set; }

    public double NetSalary { get; set; }
    public double PaidAmount { get; set; }
    public DateTime PaymentDate { get; set; } = DateTime.Now;

    [Required]
    [MaxLength(50)]
    public string PaymentMode { get; set; } = "Cash"; // e.g. "Cash", "Bank Transfer", "UPI", "Cheque"

    [MaxLength(500)]
    public string? Remarks { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
