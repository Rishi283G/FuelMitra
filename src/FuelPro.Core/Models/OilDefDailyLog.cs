using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class OilDefDailyLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public DateTime LogDate { get; set; }

    [Required]
    [MaxLength(10)]
    public string ProductType { get; set; } = "Oil"; // "Oil" or "DEF"

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public ProductMaster? Product { get; set; }

    public double? OverrideSaleRate { get; set; }

    public double AddedQuantity { get; set; }
    public double SoldQuantity { get; set; }
    public double RemainingStock { get; set; }
    public double AdjustmentQuantity { get; set; }
    public string? AdjustmentType { get; set; }
    public string? Remarks { get; set; }

    // ── Computed display helpers (not persisted) ──────────────────────────
    /// <summary>Rate actually applied: override if set, else product default.</summary>
    [NotMapped]
    public double EffectiveRate => OverrideSaleRate ?? Product?.DefaultSaleRate ?? 0;

    /// <summary>Total sale value for this log row: Qty × EffectiveRate.</summary>
    [NotMapped]
    public double SalesValue => SoldQuantity * EffectiveRate;
}
