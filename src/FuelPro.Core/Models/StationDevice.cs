using System;

namespace FuelPro.Core.Models;

/// <summary>
/// Represents a registered desktop computer authorized to access station operational data in a multi-computer deployment.
/// Synchronized across machines via Supabase table "StationDevices".
/// </summary>
public class StationDevice
{
    public string MachineId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;

    /// <summary>
    /// Role: "Primary" (configuration owner/station admin PC) or "Secondary" (additional admin/manager PC).
    /// </summary>
    public string Role { get; set; } = "Secondary";

    public bool IsAuthorized { get; set; } = true;
    public DateTime PairedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public string? RevocationReason { get; set; }
}

/// <summary>
/// Represents a time-limited one-time pairing token for authorizing a new PC to join a station.
/// Synchronized via Supabase table "StationPairingCodes".
/// </summary>
public class StationPairingToken
{
    public string PairingCode { get; set; } = string.Empty;
    public string StationId { get; set; } = string.Empty;
    public string EncryptedCredentials { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(15);
    public bool IsUsed { get; set; }
    public string CreatedByMachineId { get; set; } = string.Empty;
}
