using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class DsmPersonalDebtorRepayment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string SyncGuid { get; set; } = Guid.NewGuid().ToString();

    [Required]
    public int DsmPersonalDebtorId { get; set; }

    public int? ShiftId { get; set; }

    [Required]
    public DateTime Date { get; set; }

    public double Amount { get; set; }

    [Required]
    [MaxLength(50)]
    public string PaymentMethod { get; set; } = "Cash"; // Cash, Card

    public int Denom500 { get; set; }
    public int Denom200 { get; set; }
    public int Denom100 { get; set; }
    public int Denom50 { get; set; }
    public int Denom20 { get; set; }
    public int Denom10 { get; set; }
    public int Coins { get; set; }

    [MaxLength(100)]
    public string? CardTid { get; set; }

    [MaxLength(100)]
    public string? CardBatch { get; set; }

    [Required]
    [MaxLength(50)]
    public string Source { get; set; } = "ManagerShiftTotal"; // ManagerShiftTotal, OwnerPayroll

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [ForeignKey(nameof(DsmPersonalDebtorId))]
    public DsmPersonalDebtor? DsmPersonalDebtor { get; set; }

    [ForeignKey(nameof(ShiftId))]
    public Shift? Shift { get; set; }
}
