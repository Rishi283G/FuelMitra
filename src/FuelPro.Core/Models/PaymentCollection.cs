using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FuelPro.Core.Models;

public class PaymentCollection
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int PaymentId { get; set; }

    [Required]
    public int DsmEntryId { get; set; }

    [NotMapped]
    public double PhonePeCard => PhonePeCardMorning + PhonePeCardNight;

    public double PhonePeCardMorning { get; set; }
    public double PhonePeCardNight { get; set; }
    public double PhonePeMorning { get; set; }
    public double PhonePeNight { get; set; }
    public double CreditCard { get; set; }
    public double PetroCard { get; set; }
    public double Others { get; set; }
    public double CashDeposit { get; set; }

    /// <summary>
    /// Computed total PhonePe (Morning + Night). Kept for backward compatibility.
    /// The old DB column 'PhonePe' is maintained via legacy migration; this is NotMapped.
    /// </summary>
    [NotMapped]
    public double PhonePe => PhonePeMorning + PhonePeNight;

    /// <summary>
    /// Total of all digital payment methods.
    /// </summary>
    [NotMapped]
    public double TotalDigitalPayments => PhonePeCard + PhonePe + CreditCard + PetroCard + Others;

    // Navigation
    [ForeignKey(nameof(DsmEntryId))]
    public DsmEntry? DsmEntry { get; set; }
}
