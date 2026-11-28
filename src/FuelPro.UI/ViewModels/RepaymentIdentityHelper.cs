using System;
using System.Text.RegularExpressions;

namespace FuelPro.UI.ViewModels;

/// <summary>
/// Helper to construct, parse, and match stable parent entry and submission identity tags
/// within CreditorRepayment.ChequeNo while preserving existing user notes and respecting the MaxLength(100) constraint.
/// </summary>
public static class RepaymentIdentityHelper
{
    private const int MaxChequeLength = 100;
    private static readonly Regex SubmissionTagRegex = new(@"\[Sub:([a-fA-F0-9]{32})(?::(\d+))?\]", RegexOptions.Compiled);
    private static readonly Regex EntryTagRegex = new(@"\[Entry:(\d+)(?::(\d+))?\]", RegexOptions.Compiled);
    private static readonly Regex DsmTagRegex = new(@"\(DSM:\s*[^)]+\)|DSM:\s*\S+", RegexOptions.Compiled);

    public static string BuildSubmissionTag(Guid submissionId, int index)
    {
        return $"[Sub:{submissionId:N}:{index}]";
    }

    public static string BuildEntryTag(int entryId, int index)
    {
        return $"[Entry:{entryId}:{index}]";
    }

    public static bool MatchesSubmission(string? chequeNo, Guid submissionId, int index)
    {
        if (string.IsNullOrWhiteSpace(chequeNo)) return false;
        string exact = $"[Sub:{submissionId:N}:{index}]";
        if (chequeNo.IndexOf(exact, StringComparison.OrdinalIgnoreCase) >= 0) return true;

        // Fallback for submissions saved with base guid without index
        string baseTag = $"[Sub:{submissionId:N}]";
        return index == 0 && chequeNo.IndexOf(baseTag, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool MatchesEntry(string? chequeNo, int entryId, int index)
    {
        if (string.IsNullOrWhiteSpace(chequeNo)) return false;
        string exact = $"[Entry:{entryId}:{index}]";
        if (chequeNo.IndexOf(exact, StringComparison.OrdinalIgnoreCase) >= 0) return true;

        // Fallback for entries saved with base entryId without index
        string baseTag = $"[Entry:{entryId}]";
        return index == 0 && chequeNo.IndexOf(baseTag, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// Formats the ChequeNo field by combining user notes, DSM attribution, and identity tag.
    /// Strictly guarantees output does not exceed MaxLength(100).
    /// </summary>
    public static string FormatChequeNo(string? userNotes, string dsmName, string? identityTag)
    {
        string cleanDsm = !string.IsNullOrWhiteSpace(dsmName) ? dsmName.Trim() : "General";
        string tagPart = !string.IsNullOrWhiteSpace(identityTag) ? $" {identityTag.Trim()}" : "";
        string dsmPart = $"DSM: {cleanDsm}{tagPart}";

        string cleanUser = ExtractUserNotes(userNotes, cleanDsm);

        if (string.IsNullOrWhiteSpace(cleanUser))
        {
            return dsmPart.Length > MaxChequeLength ? dsmPart.Substring(0, MaxChequeLength) : dsmPart;
        }

        string full = $"{cleanUser} ({dsmPart})";
        if (full.Length <= MaxChequeLength)
        {
            return full;
        }

        // Truncate user notes so total length with suffix fits within MaxLength
        string suffix = $" ({dsmPart})";
        int availableForNotes = MaxChequeLength - suffix.Length;
        if (availableForNotes > 3)
        {
            return $"{cleanUser.Substring(0, availableForNotes - 3)}...{suffix}";
        }

        // If suffix itself is too long, return truncated suffix
        return dsmPart.Length > MaxChequeLength ? dsmPart.Substring(0, MaxChequeLength) : dsmPart;
    }

    /// <summary>
    /// Extracts the original user-entered notes by stripping out internal identity and DSM tags.
    /// </summary>
    public static string ExtractUserNotes(string? chequeNo, string? dsmTagOrName = null)
    {
        if (string.IsNullOrWhiteSpace(chequeNo)) return string.Empty;

        string cur = chequeNo.Trim();

        // Strip [Sub:...] and [Entry:...] tags
        cur = SubmissionTagRegex.Replace(cur, "").Trim();
        cur = EntryTagRegex.Replace(cur, "").Trim();

        // Strip (DSM: ...) wrapper
        cur = Regex.Replace(cur, @"\(\s*DSM:[^)]*\)", "", RegexOptions.IgnoreCase).Trim();

        // Strip raw DSM: ... if it was at the beginning or end
        if (cur.StartsWith("DSM:", StringComparison.OrdinalIgnoreCase))
        {
            int nextSpace = cur.IndexOf(' ');
            cur = nextSpace >= 0 ? cur.Substring(nextSpace).Trim() : string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(dsmTagOrName))
        {
            cur = cur.Replace($"DSM: {dsmTagOrName}", "", StringComparison.OrdinalIgnoreCase).Trim();
            cur = cur.Replace(dsmTagOrName, "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        // Clean any leftover double spaces or parenthesis
        cur = Regex.Replace(cur, @"\s+", " ").Trim();
        cur = cur.Trim('(', ')', '[', ']', '-').Trim();

        return cur;
    }
}
