using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class CreditorRepayment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int CreditorRepaymentId { get; set; }

    [Required]
    public DateTime RepaymentDate { get; set; }

    [Required]
    [MaxLength(200)]
    public string CreditorName { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string PaymentMode { get; set; } = "Cash"; // PhonePe, Credit Card, Cheque, Cash

    [MaxLength(100)]
    public string? ChequeNo { get; set; }

    public double Amount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
