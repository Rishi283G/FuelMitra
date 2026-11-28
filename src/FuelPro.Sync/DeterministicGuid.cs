using System;
using System.Security.Cryptography;
using System.Text;

namespace FuelPro.Sync;

/// <summary>
/// Implements RFC 4122 Version 5 UUID (SHA-1 based) for generating deterministic canonical GUIDs
/// across multiple computers for logical operational entities (such as Shifts).
/// </summary>
public static class DeterministicGuid
{
    // Canonical namespace UUID for PyroSync operational entities
    private static readonly Guid PyroSyncNamespace = new("a3b4c5d6-e7f8-4a1b-9c2d-3e4f5a6b7c8d");

    /// <summary>
    /// Generates a deterministic RFC 4122 v5 UUID based on a canonical namespace and name string.
    /// Two independent computers computing this for the exact same input will always generate identical GUIDs.
    /// </summary>
    public static Guid Create(string name, Guid? namespaceId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name cannot be null or whitespace for deterministic GUID generation.", nameof(name));

        var ns = namespaceId ?? PyroSyncNamespace;
        var nsBytes = ns.ToByteArray();

        // Swap byte order to Big-Endian network order as required by RFC 4122
        SwapByteOrder(nsBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var combined = new byte[nsBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(nsBytes, 0, combined, 0, nsBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, combined, nsBytes.Length, nameBytes.Length);

        byte[] hash;
        using (var sha1 = SHA1.Create())
        {
            hash = sha1.ComputeHash(combined);
        }

        var newGuidBytes = new byte[16];
        Buffer.BlockCopy(hash, 0, newGuidBytes, 0, 16);

        // Set version 5 (bits 4-7 of time_hi_and_version to 0101)
        newGuidBytes[6] = (byte)((newGuidBytes[6] & 0x0F) | (5 << 4));

        // Set variant to RFC 4122 (bits 6-7 of clock_seq_hi_and_reserved to 10)
        newGuidBytes[8] = (byte)((newGuidBytes[8] & 0x3F) | 0x80);

        // Swap byte order back to Little-Endian for .NET Guid structure
        SwapByteOrder(newGuidBytes);

        return new Guid(newGuidBytes);
    }

    /// <summary>
    /// Normalizes shift type aliases across systems (e.g. "Shift A", "I", "1", "a" -> "A").
    /// Matches FuelPro ShiftCycleService normalization rules.
    /// </summary>
    public static string NormalizeShiftType(string? shiftType)
    {
        if (string.IsNullOrWhiteSpace(shiftType))
            return "A";

        var clean = shiftType.Trim().ToUpperInvariant();
        return clean switch
        {
            "A" or "SHIFT A" or "SHIFT-A" or "I" or "SHIFT I" or "1" => "A",
            "B" or "SHIFT B" or "SHIFT-B" or "II" or "SHIFT II" or "2" => "B",
            "C" or "SHIFT C" or "SHIFT-C" or "III" or "SHIFT III" or "3" => "C",
            _ when clean.EndsWith("A") || clean.StartsWith("A") => "A",
            _ when clean.EndsWith("B") || clean.StartsWith("B") => "B",
            _ when clean.EndsWith("C") || clean.StartsWith("C") => "C",
            _ => clean
        };
    }

    /// <summary>
    /// Generates the canonical deterministic Shift GUID for cross-computer operational consistency.
    /// Format: "shift:{cleanStation}:{yyyy-MM-dd}:{normalizedShiftType}"
    /// </summary>
    public static string CreateCanonicalShiftGuid(string stationId, DateTime shiftDate, string shiftType)
    {
        var cleanStation = (stationId ?? "default").Trim().ToLowerInvariant();
        var dateStr = shiftDate.Date.ToString("yyyy-MM-dd");
        var normalizedType = NormalizeShiftType(shiftType);

        var canonicalKey = $"shift:{cleanStation}:{dateStr}:{normalizedType}";
        return Create(canonicalKey).ToString();
    }

    // ── DSM Approval Deterministic GUID Factories (Stage 2.3) ──

    public static string CreateDsmEntryGuid(Guid submissionId, int pumpId)
        => Create($"dsm_entry:{submissionId}:{pumpId}").ToString();

    public static string CreateDsmApprovalAuditGuid(Guid submissionId)
        => Create($"dsm_audit:{submissionId}").ToString();

    public static string CreateNozzleReadingGuid(Guid submissionId, int pumpId, int nozzleNumber)
        => Create($"nozzle_reading:{submissionId}:{pumpId}:{nozzleNumber}").ToString();

    public static string CreatePaymentCollectionGuid(Guid submissionId, int pumpId)
        => Create($"payment:{submissionId}:{pumpId}").ToString();

    public static string CreateCashDenominationGuid(Guid submissionId, string cashType)
        => Create($"cash:{submissionId}:{(cashType ?? "Cash").Trim().ToUpperInvariant()}").ToString();

    public static string CreateDebitEntryGuid(Guid submissionId, int itemIndex, string? slipNumber = null)
        => Create($"debit:{submissionId}:{itemIndex}:{(slipNumber ?? "").Trim()}").ToString();

    public static string CreateCreditorRepaymentGuid(Guid submissionId, int itemIndex, string? debtorName = null)
        => Create($"creditor_repayment:{submissionId}:{itemIndex}:{(debtorName ?? "").Trim()}").ToString();

    public static string CreateTestingEntryGuid(Guid submissionId, string fuelType, int itemIndex)
        => Create($"testing:{submissionId}:{(fuelType ?? "").Trim().ToUpperInvariant()}:{itemIndex}").ToString();

    public static string CreateExpenseGuid(Guid submissionId, int itemIndex)
        => Create($"expense:{submissionId}:{itemIndex}").ToString();

    public static string CreateOilDefSaleGuid(Guid submissionId, int productId, int itemIndex)
        => Create($"oil_def:{submissionId}:{productId}:{itemIndex}").ToString();

    public static string CreateQrPaymentGuid(Guid submissionId, int itemIndex, string? transactionId = null)
        => Create($"qr:{submissionId}:{itemIndex}:{(transactionId ?? "").Trim()}").ToString();

    private static void SwapByteOrder(byte[] guid)
    {
        // Byte 0-3 (Data1 - UInt32)
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);

        // Byte 4-5 (Data2 - UInt16)
        (guid[4], guid[5]) = (guid[5], guid[4]);

        // Byte 6-7 (Data3 - UInt16)
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}
