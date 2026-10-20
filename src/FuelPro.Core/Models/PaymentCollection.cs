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
        var normalized = NormalizeCollectionTypeCode(code);
        return normalized is "PHONEPE" or "PHONEPE_CARD" or "CREDIT_CARD" or "PETROCARD" or "CASH_DEPOSIT" or "OTHERS";
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
        if (test.Equals("PHONEPE", StringComparison.OrdinalIgnoreCase)
            || test.Equals("UPI", StringComparison.OrdinalIgnoreCase)
            || test.Equals("PHONEPEUPI", StringComparison.OrdinalIgnoreCase)
            || test.Equals("PHONEPEDIRECT", StringComparison.OrdinalIgnoreCase))
            return "PHONEPE";
        if (test.Equals("CREDITCARD", StringComparison.OrdinalIgnoreCase)
            || test.Equals("CREDIT_CARD", StringComparison.OrdinalIgnoreCase)
            || test.Equals("CREDITDEBITCARD", StringComparison.OrdinalIgnoreCase)
            || test.Equals("CARD", StringComparison.OrdinalIgnoreCase))
            return "CREDIT_CARD";
        if (test.Equals("PINELABS", StringComparison.OrdinalIgnoreCase)
            || test.Equals("PINELABSCARD", StringComparison.OrdinalIgnoreCase)
            || test.Equals("PINELABSCARDS", StringComparison.OrdinalIgnoreCase)
            || test.Equals("PINELAB", StringComparison.OrdinalIgnoreCase)
            || test.Equals("PINELABCARD", StringComparison.OrdinalIgnoreCase)
            || test.Equals("PINELABSCREDITCARD", StringComparison.OrdinalIgnoreCase))
            return "PINELABS_CARD";
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
    /// Prevents double-counting when itemized OTP transactions and an aggregate/parent row exist for the same dynamic collection.
    /// </summary>
    [NotMapped]
    public double DynamicItemsTotal
    {
        get
        {
            var all = GetAllItems().Where(i => !IsStandardCollectionCode(i.CollectionTypeCode)).ToList();
            if (all.Count == 0) return 0;

            double total = 0;
            foreach (var group in all.GroupBy(i => NormalizeCollectionTypeCode(i.CollectionTypeCode), StringComparer.OrdinalIgnoreCase))
            {
                var otpItems = group.Where(i => !string.IsNullOrWhiteSpace(i.Otp)).ToList();
                var nonOtpItems = group.Where(i => string.IsNullOrWhiteSpace(i.Otp) && !i.IsSyntheticSummary).ToList();

                if (otpItems.Count == 0)
                {
                    total += nonOtpItems.Sum(i => i.Amount);
                    continue;
                }

                // Sum all legitimate distinct OTP transactions
                double otpTotal = otpItems.Sum(i => i.Amount);
                total += otpTotal;

                // Only ignore a non-OTP row if:
                // 1) It is a single aggregate header for multiple itemized OTP items (otpItems.Count > 1), OR
                // 2) It is a phantom duplicate without TID/Batch in the same slot matching the single OTP transaction
                bool isSingleAggregateHeader = nonOtpItems.Count == 1 && (
                    (otpItems.Count > 1 && Math.Abs(nonOtpItems[0].Amount - otpTotal) < 0.01) ||
                    (otpItems.Count == 1 && Math.Abs(nonOtpItems[0].Amount - otpTotal) < 0.01
                        && string.IsNullOrWhiteSpace(nonOtpItems[0].Tid) && string.IsNullOrWhiteSpace(nonOtpItems[0].Batch)
                        && string.Equals(nonOtpItems[0].Slot?.Trim(), otpItems[0].Slot?.Trim(), StringComparison.OrdinalIgnoreCase))
                );
                if (isSingleAggregateHeader)
                {
                    continue;
                }

                foreach (var nonOtp in nonOtpItems)
                {
                    // Exclude only if an exact duplicate within the SAME slot with matching TID and Batch
                    bool isDuplicateInSameSlot = otpItems.Any(otp =>
                        string.Equals(nonOtp.Slot?.Trim(), otp.Slot?.Trim(), StringComparison.OrdinalIgnoreCase)
                        && IsDuplicateManualAndOtp(nonOtp.Amount, nonOtp.Tid, nonOtp.Batch, otp.Amount, otp.Tid, otp.Batch));

                    if (!isDuplicateInSameSlot)
                    {
                        total += nonOtp.Amount;
                    }
                }
            }

            return total;
        }
    }

    /// <summary>
    /// Determines whether a non-OTP manual item and an OTP-derived item represent the same physical card/terminal transaction.
    /// Deduplication requires:
    /// 1. Matching transaction amount within 0.01 tolerance.
    /// 2. Evidence of machine transaction identity: TID and/or Batch must be present on both records and match.
    /// Empty or missing TID/Batch values on either side cannot establish that two transactions are identical.
    /// </summary>
    public static bool IsDuplicateManualAndOtp(
        double nonOtpAmount, string? nonOtpTid, string? nonOtpBatch,
        double otpAmount, string? otpTid, string? otpBatch)
    {
        if (Math.Abs(nonOtpAmount - otpAmount) >= 0.01) return false;

        bool hasTidOrBatchNonOtp = !string.IsNullOrWhiteSpace(nonOtpTid) || !string.IsNullOrWhiteSpace(nonOtpBatch);
        bool hasTidOrBatchOtp = !string.IsNullOrWhiteSpace(otpTid) || !string.IsNullOrWhiteSpace(otpBatch);

        // If either record lacks machine identity (both TID and Batch empty), they cannot be established as identical.
        if (!hasTidOrBatchNonOtp || !hasTidOrBatchOtp)
            return false;

        // If TID is present on either, they must match
        if (!string.IsNullOrWhiteSpace(nonOtpTid) || !string.IsNullOrWhiteSpace(otpTid))
        {
            if (!string.Equals(nonOtpTid?.Trim(), otpTid?.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // If Batch is present on either, they must match
        if (!string.IsNullOrWhiteSpace(nonOtpBatch) || !string.IsNullOrWhiteSpace(otpBatch))
        {
            if (!string.Equals(nonOtpBatch?.Trim(), otpBatch?.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static bool IsDuplicateManualAndOtp(PaymentCollectionItem nonOtp, PaymentCollectionItem otp)
    {
        if (nonOtp == null || otp == null) return false;
        return IsDuplicateManualAndOtp(nonOtp.Amount, nonOtp.Tid, nonOtp.Batch, otp.Amount, otp.Tid, otp.Batch);
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


