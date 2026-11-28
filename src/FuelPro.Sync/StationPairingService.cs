using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;
using FuelPro.Core.Common;
using FuelPro.Core.Models;
using FuelPro.Sync.Security;

namespace FuelPro.Sync;

public class StationPairingService
{
    private readonly SyncConfigService _configService;
    private readonly SupabaseHttpClient _httpClient;
    private readonly ILogger _logger = Log.ForContext<StationPairingService>();

    // Character set for human-friendly, unambiguous pairing codes (no 0/O, 1/I)
    private const string PairingCodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public StationPairingService(SyncConfigService configService, SupabaseHttpClient httpClient)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Generates a secure, time-limited pairing code (format: PYRO-XXXX-XXXX) on the Primary PC
    /// and publishes the encrypted station credentials to Supabase table "StationPairingCodes".
    /// </summary>
    public async Task<Result<string>> GeneratePairingCodeAsync(TimeSpan? validity = null)
    {
        try
        {
            var settings = await _configService.GetSettingsAsync();
            if (string.IsNullOrWhiteSpace(settings.StationId) ||
                string.IsNullOrWhiteSpace(settings.SupabaseUrl) ||
                string.IsNullOrWhiteSpace(settings.SupabaseApiKey))
            {
                return Result<string>.Fail("Sync is not configured on this machine. Configure cloud connection before pairing secondary computers.");
            }

            if (string.IsNullOrWhiteSpace(settings.MachineId))
            {
                return Result<string>.Fail("Machine ID is missing on this workstation. Save cloud settings and try again.");
            }

            if (!IsValidHttpUrl(settings.SupabaseUrl))
            {
                return Result<string>.Fail("Supabase Project URL is invalid. Expected a valid http:// or https:// URL.");
            }

            // Security guard: Ensure privileged administrative keys (such as service_role) are never packaged into client pairing tokens
            if (settings.SupabaseApiKey.Contains("service_role", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error("Security violation: Station credentials contain a service_role key. Pairing only allows client-safe anon keys.");
                return Result<string>.Fail("Security violation: Station credentials contain an administrative service_role key. Pairing only permits standard client API keys.");
            }

            _httpClient.Configure(settings.SupabaseUrl, settings.SupabaseApiKey);

            var validityDuration = validity ?? TimeSpan.FromMinutes(15);
            var rawCodeChars = new char[8];
            var randomBytes = new byte[8];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }

            for (int i = 0; i < 8; i++)
            {
                rawCodeChars[i] = PairingCodeAlphabet[randomBytes[i] % PairingCodeAlphabet.Length];
            }

            var cleanCode = new string(rawCodeChars);
            var formattedCode = $"PYRO-{cleanCode.Substring(0, 4)}-{cleanCode.Substring(4, 4)}";

            // Encrypt station connection credentials using an AES key derived from cleanCode
            var payload = new
            {
                station_id = settings.StationId.Trim(),
                supabase_url = settings.SupabaseUrl.Trim(),
                supabase_api_key = settings.SupabaseApiKey.Trim()
            };
            var payloadJson = JsonConvert.SerializeObject(payload);
            var encryptedCredentials = EncryptWithPairingKey(payloadJson, cleanCode);

            var pairingRecord = new Dictionary<string, object?>
            {
                { "PairingCode", cleanCode },
                { "FormattedCode", formattedCode },
                { "station_id", settings.StationId.Trim() },
                { "EncryptedCredentials", encryptedCredentials },
                { "CreatedAt", DateTime.UtcNow.ToString("o") },
                { "ExpiresAt", DateTime.UtcNow.Add(validityDuration).ToString("o") },
                { "IsUsed", false },
                { "CreatedByMachineId", settings.MachineId.Trim() }
            };

            var postJson = JsonConvert.SerializeObject(new[] { pairingRecord });
            var response = await _httpClient.SendRequestAsync(HttpMethod.Post, "StationPairingCodes", postJson, isUpsert: false);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.Warning("Supabase POST failed for StationPairingCodes (StationId={StationId}, Status={StatusCode}): {Error}",
                    settings.StationId, (int)response.StatusCode, error);
                return Result<string>.Fail(FormatCloudSchemaOrRequestError("StationPairingCodes", response.StatusCode, error, "publish pairing code to cloud"));
            }

            // Ensure the primary PC is registered as Primary in StationDevices; never report active code if registration fails
            var machineName = string.IsNullOrWhiteSpace(Environment.MachineName) ? settings.MachineId.Trim() : Environment.MachineName.Trim();
            var (registered, regError) = await RegisterDeviceWithResultAsync(
                settings.StationId.Trim(),
                settings.MachineId.Trim(),
                machineName,
                "Primary",
                true);

            if (!registered)
            {
                _logger.Warning("Pairing code published for station {StationId}, but Primary PC device registration in StationDevices failed: {Error}",
                    settings.StationId, regError);
                return Result<string>.Fail($"Pairing code was created, but failed to register this Primary PC in cloud: {regError}");
            }

            _logger.Information("Generated station pairing code {Code} for station {StationId} (valid for {Minutes}m)", formattedCode, settings.StationId, validityDuration.TotalMinutes);
            return Result<string>.Ok(formattedCode);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to generate station pairing code");
            return Result<string>.Fail($"Error generating pairing code: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates and redeems a pairing code using pre-configured cloud endpoint settings.
    /// </summary>
    public async Task<Result<SyncSettings>> RedeemPairingCodeAsync(string pairingCodeInput, string machineName)
    {
        var settings = await _configService.GetSettingsAsync();
        return await RedeemPairingCodeAsync(pairingCodeInput, machineName, settings.SupabaseUrl, settings.SupabaseApiKey);
    }

    /// <summary>
    /// Validates and redeems a pairing code on a Secondary PC, decrypts credentials,
    /// marks the code as used in cloud, registers this machine, and saves settings locally.
    /// </summary>
    public async Task<Result<SyncSettings>> RedeemPairingCodeAsync(string pairingCodeInput, string machineName, string supabaseUrl, string supabaseApiKey)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseApiKey))
                return Result<SyncSettings>.Fail("Cloud URL and API key must be configured before redeeming a pairing code.");

            if (!IsValidHttpUrl(supabaseUrl))
                return Result<SyncSettings>.Fail("Supabase Project URL is invalid. Expected a valid http:// or https:// URL.");

            if (supabaseApiKey.Contains("service_role", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Error("Security violation: Attempted pairing redemption using a service_role API key.");
                return Result<SyncSettings>.Fail("Security violation: Pairing only permits standard client API keys, not administrative service_role keys.");
            }

            if (string.IsNullOrWhiteSpace(pairingCodeInput))
                return Result<SyncSettings>.Fail("Please enter a valid pairing code.");

            // Normalize code: remove "PYRO-", dashes, and spaces
            var cleanCode = pairingCodeInput.Trim().ToUpperInvariant()
                .Replace("PYRO-", "")
                .Replace("-", "")
                .Replace(" ", "");

            if (cleanCode.Length != 8)
                return Result<SyncSettings>.Fail("Invalid pairing code format. Expected PYRO-XXXX-XXXX or 8 characters.");

            _httpClient.Configure(supabaseUrl.Trim(), supabaseApiKey.Trim());
            var currentSettings = await _configService.GetSettingsAsync();
            if (string.IsNullOrWhiteSpace(currentSettings.MachineId))
                return Result<SyncSettings>.Fail("Machine ID is missing on this workstation. Restart or save cloud settings first.");
            var effectiveMachineName = string.IsNullOrWhiteSpace(machineName) ? Environment.MachineName : machineName.Trim();

            // Atomic server RPC redemption (rate-limited, row-locked, secure against credential leakage)
            try
            {
                var rpcPayload = JsonConvert.SerializeObject(new
                {
                    p_pairing_code = cleanCode,
                    p_machine_id = currentSettings.MachineId,
                    p_machine_name = effectiveMachineName
                });

                var rpcResponse = await _httpClient.SendRequestAsync(HttpMethod.Post, "rpc/redeem_station_pairing_code", rpcPayload);
                if (rpcResponse.IsSuccessStatusCode)
                {
                    var rpcResultJson = await rpcResponse.Content.ReadAsStringAsync();
                    var rpcResult = JsonConvert.DeserializeObject<Dictionary<string, object?>>(rpcResultJson);
                    var success = Convert.ToBoolean(rpcResult?.GetValueOrDefault("success") ?? false);
                    if (!success)
                    {
                        var errMsg = rpcResult?.GetValueOrDefault("error")?.ToString() ?? "Pairing code redemption failed.";
                        return Result<SyncSettings>.Fail(errMsg);
                    }

                    var encryptedCredsRpc = rpcResult?.GetValueOrDefault("encrypted_credentials")?.ToString();
                    if (string.IsNullOrEmpty(encryptedCredsRpc))
                    {
                        return Result<SyncSettings>.Fail("Pairing code redemption returned empty credentials.");
                    }

                    return await ApplyDecryptedCredentialsAsync(encryptedCredsRpc, cleanCode, currentSettings, effectiveMachineName);
                }
                else
                {
                    var rpcError = await rpcResponse.Content.ReadAsStringAsync();
                    _logger.Warning("RPC redeem_station_pairing_code returned non-success ({StatusCode}): {Error}", rpcResponse.StatusCode, rpcError);
                    
                    if (rpcResponse.StatusCode == HttpStatusCode.NotFound ||
                        IsMissingCloudSchemaError(rpcResponse.StatusCode, rpcError))
                    {
                        return Result<SyncSettings>.Fail("Pairing failed: The secure server RPC 'redeem_station_pairing_code' is not installed in the cloud database. Direct table access is disabled for security. Please ensure migration supabase/migrations/20261121_multi_computer_tables.sql has been deployed.");
                    }

                    if (rpcError.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                        rpcError.Contains("already used", StringComparison.OrdinalIgnoreCase) ||
                        rpcError.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                        rpcError.Contains("invalid", StringComparison.OrdinalIgnoreCase))
                    {
                        return Result<SyncSettings>.Fail($"Pairing error: {rpcError}");
                    }

                    return Result<SyncSettings>.Fail($"Pairing error ({rpcResponse.StatusCode}): {rpcError}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception during secure RPC pairing code redemption");
                return Result<SyncSettings>.Fail($"Pairing communication error: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error redeeming pairing code");
            return Result<SyncSettings>.Fail($"Pairing error: {ex.Message}");
        }
    }

    private async Task<Result<SyncSettings>> ApplyDecryptedCredentialsAsync(string encryptedCreds, string cleanCode, SyncSettings currentSettings, string effectiveMachineName)
    {
        string payloadJson;
        try
        {
            payloadJson = DecryptWithPairingKey(encryptedCreds, cleanCode);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to decrypt pairing credentials payload");
            return Result<SyncSettings>.Fail("Failed to decrypt credentials. Pairing code may be incorrect.");
        }

        var payload = JsonConvert.DeserializeAnonymousType(payloadJson, new
        {
            station_id = "",
            supabase_url = "",
            supabase_api_key = ""
        });

        if (payload == null || string.IsNullOrWhiteSpace(payload.station_id) ||
            string.IsNullOrWhiteSpace(payload.supabase_url) || string.IsNullOrWhiteSpace(payload.supabase_api_key))
        {
            return Result<SyncSettings>.Fail("Decrypted pairing data is invalid or incomplete.");
        }

        if (payload.supabase_api_key.Contains("service_role", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Error("Security violation: Decrypted pairing payload contained a service_role key.");
            return Result<SyncSettings>.Fail("Pairing payload rejected: administrative service_role keys are not permitted.");
        }

        _httpClient.Configure(payload.supabase_url.Trim(), payload.supabase_api_key.Trim());

        // Register this machine as an authorized Secondary PC in cloud
        var (registered, regError) = await RegisterDeviceWithResultAsync(payload.station_id.Trim(), currentSettings.MachineId.Trim(), effectiveMachineName, "Secondary", true);
        if (!registered)
        {
            _logger.Warning("Secondary workstation registration in StationDevices failed during pairing redemption for station {StationId}: {Error}",
                payload.station_id, regError);
            return Result<SyncSettings>.Fail($"Pairing code verified, but failed to register this computer in cloud 'StationDevices': {regError}");
        }

        // Update local settings and persist via DPAPI
        currentSettings.StationId = payload.station_id.Trim();
        currentSettings.SupabaseUrl = payload.supabase_url.Trim();
        currentSettings.SupabaseApiKey = payload.supabase_api_key.Trim();
        currentSettings.SyncEnabled = true;

        await _configService.SaveSettingsAsync(currentSettings);
        _logger.Information("Machine {MachineId} successfully paired to station {StationId} as Secondary PC", currentSettings.MachineId, payload.station_id);

        return Result<SyncSettings>.Ok(currentSettings);
    }

    /// <summary>
    /// Verifies whether the local machine is currently authorized to access and synchronize data for the station.
    /// If the device has been revoked by an administrator, returns false with the revocation reason.
    /// Persists revocation state locally so disconnecting from the network cannot bypass revocation.
    /// </summary>
    public async Task<(bool IsAuthorized, string? Reason)> CheckDeviceAuthorizationAsync(string stationId, string machineId)
    {
        try
        {
            // 1. Check local settings first: if locally marked as revoked, deny immediately (even if offline)
            var localSettings = await _configService.GetSettingsAsync();
            if (localSettings.IsDeviceRevoked)
            {
                _logger.Warning("Device {MachineId} is marked as revoked in local settings: {Reason}", machineId, localSettings.DeviceRevocationReason);
                return (false, localSettings.DeviceRevocationReason ?? "Device access revoked by station administrator.");
            }

            if (!string.IsNullOrWhiteSpace(localSettings.SupabaseUrl) && !string.IsNullOrWhiteSpace(localSettings.SupabaseApiKey))
            {
                _httpClient.Configure(localSettings.SupabaseUrl, localSettings.SupabaseApiKey);
            }

            var encodedStationId = Uri.EscapeDataString(stationId.Trim());
            var encodedMachineId = Uri.EscapeDataString(machineId.Trim());
            var response = await _httpClient.SendRequestAsync(HttpMethod.Get,
                $"StationDevices?station_id=eq.{encodedStationId}&MachineId=eq.{encodedMachineId}");

            if (!response.IsSuccessStatusCode)
            {
                // If offline or network issue, fail open conservatively only if not previously marked revoked
                return (true, null);
            }

            var json = await response.Content.ReadAsStringAsync();
            var devices = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(json);
            if (devices == null || devices.Count == 0)
            {
                // Device not registered yet; allow if primary or single-PC mode
                return (true, null);
            }

            var device = devices[0];
            var isAuth = Convert.ToBoolean(device.GetValueOrDefault("IsAuthorized") ?? true);
            var reason = device.GetValueOrDefault("RevocationReason")?.ToString();

            if (!isAuth)
            {
                _logger.Warning("Device {MachineId} authorization was revoked for station {StationId}: {Reason}", machineId, stationId, reason);
                // Persist the revocation state locally so disconnecting from the network cannot bypass revocation
                await _configService.SetDeviceRevocationAsync(true, reason ?? "Device access revoked by station administrator.");
                return (false, reason ?? "Device access revoked by station administrator.");
            }
            else
            {
                // If device was revoked before but now re-authorized in cloud, clear the local revocation flag
                if (localSettings.IsDeviceRevoked)
                {
                    await _configService.SetDeviceRevocationAsync(false, null);
                }
            }

            // Update LastSeen timestamp in background
            _ = Task.Run(async () =>
            {
                try
                {
                    var updatePayload = JsonConvert.SerializeObject(new[]
                    {
                        new { station_id = stationId, MachineId = machineId, LastSeen = DateTime.UtcNow.ToString("o") }
                    });
                    await _httpClient.SendRequestAsync(HttpMethod.Post, "StationDevices", updatePayload, isUpsert: true, onConflict: "station_id,MachineId");
                }
                catch { }
            });

            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to verify device authorization with cloud");
            // Check if device is locally marked revoked
            try
            {
                var localSettings = await _configService.GetSettingsAsync();
                if (localSettings.IsDeviceRevoked)
                {
                    return (false, localSettings.DeviceRevocationReason ?? "Device access revoked by station administrator.");
                }
            }
            catch { }
            return (true, null); // Offline tolerance
        }
    }

    /// <summary>
    /// Registers or updates a device record in Supabase table "StationDevices".
    /// </summary>
    public async Task<bool> RegisterDeviceAsync(string stationId, string machineId, string machineName, string role, bool isAuthorized)
    {
        var (success, _) = await RegisterDeviceWithResultAsync(stationId, machineId, machineName, role, isAuthorized);
        return success;
    }

    private async Task<(bool Success, string? Error)> RegisterDeviceWithResultAsync(string stationId, string machineId, string machineName, string role, bool isAuthorized)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(stationId) || string.IsNullOrWhiteSpace(machineId))
            {
                return (false, "Station ID and Machine ID are required to register workstation.");
            }

            var localSettings = await _configService.GetSettingsAsync();
            if (!string.IsNullOrWhiteSpace(localSettings.SupabaseUrl) && !string.IsNullOrWhiteSpace(localSettings.SupabaseApiKey))
            {
                _httpClient.Configure(localSettings.SupabaseUrl, localSettings.SupabaseApiKey);
            }

            var deviceDict = new Dictionary<string, object?>
            {
                { "station_id", stationId },
                { "MachineId", machineId },
                { "MachineName", machineName },
                { "Role", role },
                { "IsAuthorized", isAuthorized },
                { "PairedAt", DateTime.UtcNow.ToString("o") },
                { "LastSeen", DateTime.UtcNow.ToString("o") }
            };

            var json = JsonConvert.SerializeObject(new[] { deviceDict });
            var response = await _httpClient.SendRequestAsync(HttpMethod.Post, "StationDevices", json, isUpsert: true, onConflict: "station_id,MachineId");
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.Warning("Supabase POST failed for StationDevices (StationId={StationId}, MachineId={MachineId}, Status={StatusCode}): {Error}",
                    stationId, machineId, (int)response.StatusCode, error);
                return (false, FormatCloudSchemaOrRequestError("StationDevices", response.StatusCode, error, "register workstation"));
            }
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to register station device in cloud");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Lists all registered desktop computers for the station from Supabase.
    /// </summary>
    public async Task<List<StationDevice>> ListStationDevicesAsync(string stationId)
    {
        var list = new List<StationDevice>();
        if (string.IsNullOrWhiteSpace(stationId)) return list;

        try
        {
            var localSettings = await _configService.GetSettingsAsync();
            if (!string.IsNullOrWhiteSpace(localSettings.SupabaseUrl) && !string.IsNullOrWhiteSpace(localSettings.SupabaseApiKey))
            {
                _httpClient.Configure(localSettings.SupabaseUrl, localSettings.SupabaseApiKey);
            }

            var encodedStationId = Uri.EscapeDataString(stationId.Trim());
            var response = await _httpClient.SendRequestAsync(HttpMethod.Get,
                $"StationDevices?station_id=eq.{encodedStationId}&order=PairedAt.asc");

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var rawList = JsonConvert.DeserializeObject<List<Dictionary<string, object?>>>(json);
                if (rawList != null)
                {
                    foreach (var dict in rawList)
                    {
                        var dev = new StationDevice
                        {
                            StationId = dict.GetValueOrDefault("station_id")?.ToString() ?? "",
                            MachineId = dict.GetValueOrDefault("MachineId")?.ToString() ?? "",
                            MachineName = dict.GetValueOrDefault("MachineName")?.ToString() ?? "",
                            Role = dict.GetValueOrDefault("Role")?.ToString() ?? "Secondary",
                            IsAuthorized = Convert.ToBoolean(dict.GetValueOrDefault("IsAuthorized") ?? true),
                            RevocationReason = dict.GetValueOrDefault("RevocationReason")?.ToString()
                        };
                        if (DateTime.TryParse(dict.GetValueOrDefault("PairedAt")?.ToString(), out var pAt)) dev.PairedAt = pAt;
                        if (DateTime.TryParse(dict.GetValueOrDefault("LastSeen")?.ToString(), out var lAt)) dev.LastSeen = lAt;
                        list.Add(dev);
                    }
                }
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.Warning("Supabase GET failed for StationDevices (StationId={StationId}, Status={StatusCode}): {Error}",
                    stationId, (int)response.StatusCode, error);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to list station devices from cloud");
        }
        return list;
    }

    /// <summary>
    /// Revokes authorization for a secondary computer.
    /// </summary>
    public async Task<bool> RevokeDeviceAsync(string stationId, string machineId, string reason)
    {
        if (string.IsNullOrWhiteSpace(stationId) || string.IsNullOrWhiteSpace(machineId))
            return false;

        try
        {
            var localSettings = await _configService.GetSettingsAsync();
            if (!string.IsNullOrWhiteSpace(localSettings.SupabaseUrl) && !string.IsNullOrWhiteSpace(localSettings.SupabaseApiKey))
            {
                _httpClient.Configure(localSettings.SupabaseUrl, localSettings.SupabaseApiKey);
            }

            var updatePayload = JsonConvert.SerializeObject(new[]
            {
                new
                {
                    station_id = stationId.Trim(),
                    MachineId = machineId.Trim(),
                    IsAuthorized = false,
                    RevocationReason = reason,
                    RevokedAt = DateTime.UtcNow.ToString("o")
                }
            });

            var response = await _httpClient.SendRequestAsync(HttpMethod.Post, "StationDevices", updatePayload, isUpsert: true, onConflict: "station_id,MachineId");
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.Warning("Failed to revoke device {MachineId} for station {StationId} (Status={StatusCode}): {Error}",
                    machineId, stationId, (int)response.StatusCode, error);
                return false;
            }

            _logger.Information("Device {MachineId} revoked for station {StationId}: {Reason}", machineId, stationId, reason);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to revoke device {MachineId}", machineId);
            return false;
        }
    }

    private static bool IsValidHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool IsMissingCloudSchemaError(HttpStatusCode statusCode, string? responseBody)
    {
        if (statusCode == HttpStatusCode.NotFound)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return false;
        }

        return responseBody.Contains("PGRST205", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("PGRST202", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("Could not find the table", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("Could not find the function", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("42P01", StringComparison.OrdinalIgnoreCase) ||
               responseBody.Contains("does not exist", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatCloudSchemaOrRequestError(string objectName, HttpStatusCode statusCode, string? rawError, string operationDescription)
    {
        if (IsMissingCloudSchemaError(statusCode, rawError))
        {
            return $"Failed to {operationDescription}: Supabase table '{objectName}' was not found in the schema cache (PGRST205). " +
                   $"Multi-computer pairing requires applying 'supabase/migrations/20261121_multi_computer_tables.sql' in the Supabase SQL Editor and reloading the API schema cache.";
        }

        if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden ||
            (!string.IsNullOrWhiteSpace(rawError) && rawError.Contains("42501", StringComparison.OrdinalIgnoreCase)))
        {
            return $"Failed to {operationDescription}: Supabase rejected authorization or RLS permissions (HTTP {(int)statusCode}). Verify your Supabase API key and table policies.";
        }

        var summary = string.IsNullOrWhiteSpace(rawError) ? statusCode.ToString() : rawError.Trim();
        if (summary.Length > 240)
        {
            summary = summary.Substring(0, 240) + "...";
        }
        return $"Failed to {operationDescription} (HTTP {(int)statusCode}): {summary}";
    }

    private static string EncryptWithPairingKey(string plaintext, string key)
    {
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length); // Prepend IV
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs))
        {
            sw.Write(plaintext);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    private static string DecryptWithPairingKey(string ciphertextBase64, string key)
    {
        var cipherBytes = Convert.FromBase64String(ciphertextBase64);
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));

        using var aes = Aes.Create();
        aes.Key = keyBytes;

        var iv = new byte[aes.BlockSize / 8];
        Buffer.BlockCopy(cipherBytes, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(cipherBytes, iv.Length, cipherBytes.Length - iv.Length);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs);

        return sr.ReadToEnd();
    }
}
