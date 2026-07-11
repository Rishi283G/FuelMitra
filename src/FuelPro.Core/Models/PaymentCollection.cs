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
    public double PhonePeCard => PhonePeCardMorning + PhonePeCardDay + PhonePeCardNight;

    public double PhonePeCardMorning { get; set; }
    public double PhonePeCardDay { get; set; }
    public double PhonePeCardNight { get; set; }
    public double PhonePeMorning { get; set; }
    public double PhonePeDay { get; set; }
    public double PhonePeNight { get; set; }
    [NotMapped]
    public double CreditCard => CreditCardMorning + CreditCardDay + CreditCardNight;

    public double CreditCardMorning { get; set; }
    public double CreditCardDay { get; set; }
    public double CreditCardNight { get; set; }
    public double Others { get; set; }
    public double CashDeposit { get; set; }

    // Legacy fields mapped/fallback
    public string? CardTid { get; set; }
    public string? CardBatch { get; set; }
    public string? PhonePeTid { get; set; }
    public string? PhonePeBatch { get; set; }
    public string? PetroCardTid { get; set; }
    public string? PetroCardBatch { get; set; }

    // Shift-wise digital collections details
    public string? CreditCardTidMorning { get; set; }
    public string? CreditCardBatchMorning { get; set; }
    public string? CreditCardTidDay { get; set; }
    public string? CreditCardBatchDay { get; set; }
    public string? CreditCardTidNight { get; set; }
    public string? CreditCardBatchNight { get; set; }

    public double PetroCardMorning { get; set; }
    public double PetroCardDay { get; set; }
    public double PetroCardNight { get; set; }
    public string? PetroCardTidMorning { get; set; }
    public string? PetroCardBatchMorning { get; set; }
    public string? PetroCardTidDay { get; set; }
    public string? PetroCardBatchDay { get; set; }
    public string? PetroCardTidNight { get; set; }
    public string? PetroCardBatchNight { get; set; }

    [NotMapped]
    public double PetroCard => PetroCardMorning + PetroCardDay + PetroCardNight;

    public string? PhonePeTidMorning { get; set; }
    public string? PhonePeBatchMorning { get; set; }
    public string? PhonePeTidDay { get; set; }
    public string? PhonePeBatchDay { get; set; }
    public string? PhonePeTidNight { get; set; }
    public string? PhonePeBatchNight { get; set; }

    /// <summary>
    /// Computed total PhonePe (Morning + Night). Kept for backward compatibility.
    /// The old DB column 'PhonePe' is maintained via legacy migration; this is NotMapped.
    /// </summary>
    [NotMapped]
    public double PhonePe => PhonePeMorning + PhonePeDay + PhonePeNight;

    /// <summary>
    /// Total of all digital payment methods.
    /// </summary>
    [NotMapped]
    public double TotalDigitalPayments => PhonePeCard + PhonePe + CreditCard + PetroCard + Others;

    // Navigation
    [ForeignKey(nameof(DsmEntryId))]
    public DsmEntry? DsmEntry { get; set; }
}
