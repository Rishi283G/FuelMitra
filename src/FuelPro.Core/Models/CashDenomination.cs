using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class CashDenomination
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int CashDenomId { get; set; }

    [Required]
    public int DsmEntryId { get; set; }

    [Required]
    [MaxLength(10)]
    public string CashType { get; set; } = "Cash1"; // Cash1=Bank Deposit, Cash2=Cash in Hand

    public int Denom500 { get; set; }
    public int Denom200 { get; set; }
    public int Denom100 { get; set; }
    public int Denom50 { get; set; }
    public int Denom20 { get; set; }
    public int Denom10 { get; set; }
    public int Coins { get; set; }

    /// <summary>
    /// Computed total: 500*Denom500 + 200*Denom200 + ... + Coins. Stored for performance.
    /// </summary>
    public double TotalAmount { get; set; }

    /// <summary>
    /// Recalculates TotalAmount from denomination counts.
    /// </summary>
    public void RecalculateTotal()
    {
        var sum = (Denom500 * 500.0)
                    + (Denom200 * 200.0)
                    + (Denom100 * 100.0)
                    + (Denom50 * 50.0)
                    + (Denom20 * 20.0)
                    + (Denom10 * 10.0)
                    + Coins;
        
        if (sum > 0)
        {
            TotalAmount = sum;
        }
    }

    // Navigation
    [ForeignKey(nameof(DsmEntryId))]
    public DsmEntry? DsmEntry { get; set; }
}
