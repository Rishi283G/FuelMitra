using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FuelPro.Sync;

/// <summary>
/// Canonical, station-isolated evaluator that determines which remote DSM submissions
/// represent actionable pending (or recoverable) queue items.
/// Shared by both <see cref="DsmSubmissionPollingService"/> (sidebar badge count) and
/// the UI queue loader so the badge count and visible queue can never diverge.
/// </summary>
public static class DsmPendingSubmissionEvaluator
{
    private static readonly HashSet<string> ExcludedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "APPROVED",
        "REJECTED",
        "CANCELLED",
        "CANCELED",
        "DELETED",
        "ARCHIVED",
        "COMPLETED"
    };

    private static readonly HashSet<string> RecoverableProcessingStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "CLAIMED",
        "LOCALPROCESSING",
        "RECOVERYREQUIRED"
    };

    public sealed class EvaluatedSubmission
    {
        public Guid Id { get; init; }
        public string StationId { get; init; } = string.Empty;
        public string DsmName { get; init; } = "Unknown DSM";
        public string DsmUserId { get; init; } = string.Empty;
        public int PumpId { get; init; }
        public DateTime ShiftDate { get; init; }
        public string ShiftType { get; init; } = "A";
        public DateTime SubmittedAt { get; init; }
        public string Notes { get; init; } = string.Empty;
        public string? AttachmentUrl { get; init; }
        public string MetadataJson { get; init; } = string.Empty;
        public string Status { get; init; } = "Pending";
        public string ProcessingStatus { get; init; } = "Pending";
        public string? ClaimedByMachineId { get; init; }
        public DateTime? ClaimedAt { get; init; }
        public string? OperationalKey { get; init; }
    }

    public sealed class EvaluationResult
    {
        public List<EvaluatedSubmission> ActionableSubmissions { get; } = new();
        public List<Guid> LocallyApprovedIds { get; } = new();
        public List<Guid> DuplicatePendingIds { get; } = new();
        public HashSet<string> SeenOperationalKeys { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Count => ActionableSubmissions.Count;
    }

    public static EvaluationResult Evaluate(
        IEnumerable<dynamic>? rawSubmissions,
        IReadOnlySet<Guid>? locallyApprovedIds,
        string? expectedStationId = null)
    {
        var result = new EvaluationResult();
        if (rawSubmissions == null) return result;

        var cleanStationId = expectedStationId?.Trim();
        var parsedItems = new List<EvaluatedSubmission>();

        foreach (object? raw in rawSubmissions)
        {
            if (!TryParseSubmission(raw, out var parsed) || parsed == null)
                continue;

            // Strict station isolation: if the row carries a StationId that disagrees with expectedStationId, exclude it.
            if (!string.IsNullOrEmpty(cleanStationId) &&
                !string.IsNullOrEmpty(parsed.StationId) &&
                !string.Equals(parsed.StationId, cleanStationId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsEligibleStatus(parsed.Status, parsed.ProcessingStatus))
                continue;

            parsedItems.Add(parsed);
        }

        // Preserve newest submission per duplicate group (matches QueueViewModel sort order)
        var sortedItems = parsedItems
            .OrderByDescending(x => x.SubmittedAt)
            .ThenByDescending(x => x.Id)
            .ToList();

        var seenIds = new HashSet<Guid>();

        foreach (var item in sortedItems)
        {
            if (!seenIds.Add(item.Id))
                continue;

            if (locallyApprovedIds != null && locallyApprovedIds.Contains(item.Id))
            {
                result.LocallyApprovedIds.Add(item.Id);
                continue;
            }

            // Operational-key deduplication ONLY when DsmUserId is known so distinct DSMs or incomplete IDs never collapse.
            if (!string.IsNullOrEmpty(item.OperationalKey))
            {
                if (!result.SeenOperationalKeys.Add(item.OperationalKey))
                {
                    result.DuplicatePendingIds.Add(item.Id);
                    continue;
                }
            }

            result.ActionableSubmissions.Add(item);
        }

        return result;
    }

    public static bool IsEligibleStatus(string? status, string? processingStatus)
    {
        var normStatus = (status ?? string.Empty).Trim();
        var normProc = (processingStatus ?? string.Empty).Trim();

        if (!string.IsNullOrEmpty(normStatus) && ExcludedStatuses.Contains(normStatus))
            return false;

        if (string.Equals(normStatus, "PENDING", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!string.IsNullOrEmpty(normProc) && RecoverableProcessingStatuses.Contains(normProc))
            return true;

        return false;
    }

    public static string? BuildOperationalKey(DateTime shiftDate, string shiftType, int pumpId, string? dsmUserId)
    {
        var cleanDsmUserId = dsmUserId?.Trim();
        if (string.IsNullOrEmpty(cleanDsmUserId))
            return null;

        var cleanShift = string.IsNullOrWhiteSpace(shiftType) ? "A" : shiftType.Trim().ToUpperInvariant();
        return $"{shiftDate:yyyy-MM-dd}_{cleanShift}_{pumpId}_{cleanDsmUserId}";
    }

    public static bool TryParseSubmission(object? raw, out EvaluatedSubmission? parsed)
    {
        parsed = null;
        if (raw == null) return false;

        if (!TryGetGuid(raw, out var subId, "Id", "id") || subId == Guid.Empty)
            return false;

        if (!TryGetInt(raw, out var pumpId, "PumpId", "pump_id") || pumpId <= 0)
            return false;

        if (!TryGetDateTime(raw, out var shiftDate, "ShiftDate", "shift_date"))
            return false;

        if (TryGetBool(raw, out var isDeleted, "IsDeleted", "is_deleted") && isDeleted)
            return false;

        var deletedAtStr = GetString(raw, "DeletedAt", "deleted_at");
        if (!string.IsNullOrWhiteSpace(deletedAtStr))
            return false;

        // If StationId key is explicitly present with a whitespace/empty value, treat as malformed
        if (HasExplicitEmptyValue(raw, "StationId", "station_id"))
            return false;

        var stationId = GetString(raw, "StationId", "station_id")?.Trim() ?? string.Empty;
        var shiftType = GetString(raw, "ShiftType", "shift_type");
        shiftType = string.IsNullOrWhiteSpace(shiftType) ? "A" : shiftType.Trim();

        var dsmUserId = GetString(raw, "DsmUserId", "dsm_user_id")?.Trim() ?? string.Empty;
        var status = GetString(raw, "Status", "status")?.Trim() ?? string.Empty;
        var processingStatus = GetString(raw, "ProcessingStatus", "processing_status")?.Trim() ?? "Pending";

        TryGetDateTime(raw, out var submittedAt, "SubmittedAt", "submitted_at");
        DateTime? claimedAt = TryGetDateTime(raw, out var cAt, "ClaimedAt", "claimed_at") ? cAt : null;

        var dsmName = ExtractDsmName(raw);
        var notes = GetString(raw, "Notes", "notes") ?? string.Empty;
        var attachmentUrl = GetString(raw, "AttachmentUrl", "attachment_url");
        var claimedByMachineId = GetString(raw, "ClaimedByMachineId", "claimed_by_machine_id");
        var metadataJson = ExtractMetadataJson(raw);

        parsed = new EvaluatedSubmission
        {
            Id = subId,
            StationId = stationId,
            DsmName = dsmName,
            DsmUserId = dsmUserId,
            PumpId = pumpId,
            ShiftDate = shiftDate,
            ShiftType = shiftType,
            SubmittedAt = submittedAt == default ? DateTime.MinValue : submittedAt,
            Notes = notes,
            AttachmentUrl = attachmentUrl,
            MetadataJson = metadataJson,
            Status = string.IsNullOrEmpty(status) ? "Pending" : status,
            ProcessingStatus = processingStatus,
            ClaimedByMachineId = claimedByMachineId,
            ClaimedAt = claimedAt,
            OperationalKey = BuildOperationalKey(shiftDate, shiftType, pumpId, dsmUserId)
        };

        return true;
    }

    public static string ExtractMetadataJson(object? item)
    {
        if (item == null) return string.Empty;
        object? raw = GetRawProperty(item, "Metadata", "metadata");
        if (raw == null) return string.Empty;
        if (raw is string str) return CleanAndUnescapeJson(str);
        if (raw is JValue jval)
        {
            if (jval.Value == null) return string.Empty;
            if (jval.Value is string sVal) return CleanAndUnescapeJson(sVal);
            return CleanAndUnescapeJson(jval.Value.ToString());
        }
        return CleanAndUnescapeJson(JsonConvert.SerializeObject(raw));
    }

    private static string CleanAndUnescapeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        string current = json.Trim();
        for (int i = 0; i < 3; i++)
        {
            if (current.StartsWith("\"") && current.EndsWith("\""))
            {
                try
                {
                    var unwrapped = JsonConvert.DeserializeObject<string>(current);
                    if (!string.IsNullOrWhiteSpace(unwrapped))
                    {
                        current = unwrapped.Trim();
                        continue;
                    }
                }
                catch { break; }
            }
            break;
        }
        return current;
    }

    private static string ExtractDsmName(object raw)
    {
        var nested = GetRawProperty(raw, "DsmUsers", "dsm_users");
        if (nested != null)
        {
            var fullName = GetString(nested, "FullName", "full_name");
            if (!string.IsNullOrWhiteSpace(fullName))
                return fullName.Trim();
        }
        var direct = GetString(raw, "DsmName", "dsm_name");
        return string.IsNullOrWhiteSpace(direct) ? "Unknown DSM" : direct.Trim();
    }

    private static bool HasExplicitEmptyValue(object raw, params string[] names)
    {
        if (raw is JObject jobj)
        {
            foreach (var name in names)
            {
                if (jobj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var tok))
                {
                    if (tok.Type == JTokenType.Null) continue;
                    if (tok.Type == JTokenType.String && string.IsNullOrWhiteSpace(tok.Value<string>()))
                        return true;
                }
            }
        }
        return false;
    }

    private static object? GetRawProperty(object? raw, params string[] names)
    {
        if (raw == null) return null;
        if (raw is JObject jobj)
        {
            foreach (var name in names)
            {
                if (jobj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var tok) && tok.Type != JTokenType.Null)
                    return tok;
            }
            return null;
        }

        if (raw is IDictionary<string, object?> dict)
        {
            foreach (var name in names)
            {
                foreach (var kv in dict)
                {
                    if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase) && kv.Value != null)
                        return kv.Value;
                }
            }
            return null;
        }

        var type = raw.GetType();
        foreach (var name in names)
        {
            var prop = type.GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
            if (prop != null)
            {
                var val = prop.GetValue(raw);
                if (val != null) return val;
            }
        }
        return null;
    }

    private static string? GetString(object raw, params string[] names)
    {
        var val = GetRawProperty(raw, names);
        if (val == null) return null;
        if (val is JValue jv) return jv.Value?.ToString();
        if (val is JToken jt) return jt.Type == JTokenType.Null ? null : jt.ToString();
        return val.ToString();
    }

    private static bool TryGetGuid(object raw, out Guid value, params string[] names)
    {
        value = Guid.Empty;
        var val = GetRawProperty(raw, names);
        if (val == null) return false;
        if (val is Guid g) { value = g; return g != Guid.Empty; }
        var s = GetString(raw, names);
        return !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s.Trim(), out value) && value != Guid.Empty;
    }

    private static bool TryGetInt(object raw, out int value, params string[] names)
    {
        value = 0;
        var val = GetRawProperty(raw, names);
        if (val == null) return false;
        if (val is JValue jv) val = jv.Value;
        if (val == null) return false;
        if (val is int i) { value = i; return true; }
        if (val is long l) { value = (int)l; return true; }
        return int.TryParse(val.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetBool(object raw, out bool value, params string[] names)
    {
        value = false;
        var val = GetRawProperty(raw, names);
        if (val == null) return false;
        if (val is JValue jv) val = jv.Value;
        if (val == null) return false;
        if (val is bool b) { value = b; return true; }
        return bool.TryParse(val.ToString(), out value);
    }

    private static bool TryGetDateTime(object raw, out DateTime value, params string[] names)
    {
        value = default;
        var val = GetRawProperty(raw, names);
        if (val == null) return false;
        if (val is JValue jv) val = jv.Value;
        if (val == null) return false;
        if (val is DateTime dt) { value = dt; return dt != default; }
        var str = val.ToString();
        return !string.IsNullOrWhiteSpace(str) &&
               DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value) &&
               value != default;
    }
}
