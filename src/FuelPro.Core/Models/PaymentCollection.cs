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
    /// Total of all digital payment methods (including dynamic collection types).
    /// </summary>
    [NotMapped]
    public double TotalDigitalPayments => PhonePeCard + PhonePe + CreditCard + PetroCard + DynamicItemsTotal;

    // Navigation
    [ForeignKey(nameof(DsmEntryId))]
    public DsmEntry? DsmEntry { get; set; }

    /// <summary>
    /// Dynamic collection items (e.g. SBI Redeem, Paytm, QR, Mobikwik, etc.)
    /// </summary>
    public ICollection<PaymentCollectionItem> Items { get; set; } = new List<PaymentCollectionItem>();

    /// <summary>
    /// Serialized JSON for dynamic collection items, ensuring reliable cloud sync.
    /// </summary>
    public string? DynamicItemsJson { get; set; }

    public static bool IsStandardCollectionCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var clean = code.Trim();

        // Strip slot parentheses, e.g. "PETROCARD (Morning)" -> "PETROCARD"
        int parenIdx = clean.IndexOf('(');
        if (parenIdx >= 0)
        {
            clean = clean.Substring(0, parenIdx).Trim();
        }

        var normalized = clean.Replace("_", "").Replace(" ", "");

        // Strip trailing slot name without parentheses, e.g. "PetroCard Morning" -> "PetroCard"
        if (normalized.EndsWith("Morning", StringComparison.OrdinalIgnoreCase) && normalized.Length > 7)
            normalized = normalized[..^7];
        else if (normalized.EndsWith("Night", StringComparison.OrdinalIgnoreCase) && normalized.Length > 5)
            normalized = normalized[..^5];
        else if (normalized.EndsWith("Day", StringComparison.OrdinalIgnoreCase) && normalized.Length > 3)
            normalized = normalized[..^3];
        else if (normalized.EndsWith("General", StringComparison.OrdinalIgnoreCase) && normalized.Length > 7)
            normalized = normalized[..^7];

        return normalized.Equals("PHONEPE", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("PHONEPECARD", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("CREDITCARD", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("PINELABS", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("PINELABSCARD", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("PETROCARD", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("CASHDEPOSIT", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("CASH1", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("OTHERS", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes raw collection type code/mode into canonical standard code or trimmed dynamic code.
    /// Preserves dynamic codes without coercion.
    /// </summary>
    public static string NormalizeCollectionTypeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;
        var clean = code.Trim();
        int parenIdx = clean.IndexOf('(');
        if (parenIdx >= 0)
        {
            clean = clean.Substring(0, parenIdx).Trim();
        }

        var test = clean.Replace("_", "").Replace(" ", "");
        if (test.EndsWith("Morning", StringComparison.OrdinalIgnoreCase) && test.Length > 7)
            test = test[..^7];
        else if (test.EndsWith("Night", StringComparison.OrdinalIgnoreCase) && test.Length > 5)
            test = test[..^5];
        else if (test.EndsWith("Day", StringComparison.OrdinalIgnoreCase) && test.Length > 3)
            test = test[..^3];
        else if (test.EndsWith("General", StringComparison.OrdinalIgnoreCase) && test.Length > 7)
            test = test[..^7];

        if (test.Equals("PHONEPECARD", StringComparison.OrdinalIgnoreCase))
            return "PHONEPE_CARD";
        if (test.Equals("PHONEPE", StringComparison.OrdinalIgnoreCase) || test.Equals("UPI", StringComparison.OrdinalIgnoreCase))
            return "PHONEPE";
        if (test.Equals("CREDITCARD", StringComparison.OrdinalIgnoreCase) || test.Equals("PINELABS", StringComparison.OrdinalIgnoreCase) || test.Equals("PINELABSCARD", StringComparison.OrdinalIgnoreCase))
            return "CREDIT_CARD";
        if (test.Equals("PETROCARD", StringComparison.OrdinalIgnoreCase) || test.Equals("PETRO", StringComparison.OrdinalIgnoreCase))
            return "PETROCARD";
        if (test.Equals("CASHDEPOSIT", StringComparison.OrdinalIgnoreCase) || test.Equals("CASH1", StringComparison.OrdinalIgnoreCase))
            return "CASH_DEPOSIT";
        if (test.Equals("OTHERS", StringComparison.OrdinalIgnoreCase))
            return "OTHERS";

        return clean;
    }

    /// <summary>
    /// Extracts slot identifier if embedded in mode/string (e.g. "PETROCARD (Morning)" -> "Morning").
    /// </summary>
    public static string ExtractSlotFromMode(string? mode, string? fallbackSlot = "General")
    {
        if (string.IsNullOrWhiteSpace(mode)) return fallbackSlot ?? "General";
        if (mode.Contains("Morning", StringComparison.OrdinalIgnoreCase)) return "Morning";
        if (mode.Contains("Night", StringComparison.OrdinalIgnoreCase)) return "Night";
        if (mode.Contains("Day", StringComparison.OrdinalIgnoreCase)) return "Day";
        if (mode.Contains("General", StringComparison.OrdinalIgnoreCase)) return "General";
        return fallbackSlot ?? "General";
    }

    /// <summary>
    /// Computes total of all dynamic collection items (excluding standard payment cards to prevent double counting).
    /// </summary>
    [NotMapped]
    public double DynamicItemsTotal
    {
        get
        {
            var all = GetAllItems();
            return all.Where(i => !IsStandardCollectionCode(i.CollectionTypeCode)).Sum(i => i.Amount);
        }
    }

    /// <summary>
    /// Returns Items if loaded, otherwise attempts to deserialize from DynamicItemsJson.
    /// </summary>
    public IEnumerable<PaymentCollectionItem> GetAllItems()
    {
        if (Items != null && Items.Count > 0)
        {
            return Items;
        }

        if (!string.IsNullOrWhiteSpace(DynamicItemsJson))
        {
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var deserialized = System.Text.Json.JsonSerializer.Deserialize<List<PaymentCollectionItem>>(DynamicItemsJson, options);
                if (deserialized != null && deserialized.Count > 0)
                {
                    return deserialized;
                }
            }
            catch { }
        }

        return Enumerable.Empty<PaymentCollectionItem>();
    }
}


