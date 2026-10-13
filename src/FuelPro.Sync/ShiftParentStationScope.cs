using System.Net.Http;
using Newtonsoft.Json;

namespace FuelPro.Sync;

/// <summary>Fetches a Shift only when the remote row belongs to the active station.</summary>
public static class ShiftParentStationScope
{
    public static string BuildLookupEndpoint(string syncGuid, string stationId)
    {
        if (string.IsNullOrWhiteSpace(syncGuid))
            throw new ArgumentException("A shift SyncGuid is required.", nameof(syncGuid));
        if (string.IsNullOrWhiteSpace(stationId))
            throw new ArgumentException("A station id is required to resolve a shift.", nameof(stationId));

        return $"Shifts?SyncGuid=eq.{Uri.EscapeDataString(syncGuid.Trim())}" +
               $"&station_id=eq.{Uri.EscapeDataString(stationId.Trim())}";
    }

    public static async Task<Dictionary<string, object?>?> FetchAsync(
        string syncGuid,
        string stationId,
        Func<string, Task<HttpResponseMessage>> sendRequestAsync)
    {
        ArgumentNullException.ThrowIfNull(sendRequestAsync);
        if (string.IsNullOrWhiteSpace(stationId))
            return null;

        using var response = await sendRequestAsync(BuildLookupEndpoint(syncGuid, stationId));
        if (!response.IsSuccessStatusCode)
            return null;

        var json = await response.Content.ReadAsStringAsync();
        var records = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(json);
        if (records is not { Count: 1 })
            return null;

        var record = records[0];
        var stationKey = record.Keys.FirstOrDefault(k => string.Equals(k, "station_id", StringComparison.OrdinalIgnoreCase));
        if (stationKey == null || !string.Equals(record[stationKey]?.ToString()?.Trim(), stationId.Trim(), StringComparison.Ordinal))
            return null;

        var syncGuidKey = record.Keys.FirstOrDefault(k => string.Equals(k, "SyncGuid", StringComparison.OrdinalIgnoreCase));
        if (syncGuidKey == null || !string.Equals(record[syncGuidKey]?.ToString(), syncGuid, StringComparison.OrdinalIgnoreCase))
            return null;

        return record;
    }
}
