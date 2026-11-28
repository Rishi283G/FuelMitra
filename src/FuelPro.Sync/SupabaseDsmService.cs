using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;
using FuelPro.Core.Common;

namespace FuelPro.Sync;

public class SupabaseDsmService
{
    private readonly SyncConfigService _syncConfigService;
    private readonly HttpClient _client;
    private readonly ILogger _logger = Log.ForContext<SupabaseDsmService>();

    public SupabaseDsmService(SyncConfigService syncConfigService)
        : this(syncConfigService, SupabaseHttpClient.CreateHttpClient(TimeSpan.FromSeconds(20)))
    {
    }

    public SupabaseDsmService(SyncConfigService syncConfigService, HttpClient httpClient)
    {
        _syncConfigService = syncConfigService ?? throw new ArgumentNullException(nameof(syncConfigService));
        _client = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string endpoint, string? jsonBody = null)
    {
        var settings = await _syncConfigService.GetSettingsAsync();
        if (string.IsNullOrEmpty(settings.SupabaseUrl) || string.IsNullOrEmpty(settings.SupabaseApiKey))
        {
            throw new InvalidOperationException("Supabase credentials are not configured.");
        }

        var requestUrl = $"{settings.SupabaseUrl.TrimEnd('/')}/rest/v1/{endpoint}";
        var request = new HttpRequestMessage(method, requestUrl);

        request.Headers.Add("apikey", settings.SupabaseApiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SupabaseApiKey);

        if (jsonBody != null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return request;
    }

    public virtual async Task<Result<List<dynamic>>> FetchPendingSubmissionsAsync()
    {
        try
        {
            var settings = await _syncConfigService.GetSettingsAsync();
            if (string.IsNullOrEmpty(settings.StationId)) return Result<List<dynamic>>.Fail("Station ID is blank.");

            // 1. Try to fetch pending AND recoverable in-flight submissions
            try
            {
                var recoverableEndpoint = $"DsmSubmissions?select=*,DsmUsers(FullName)&StationId=eq.{settings.StationId}&or=(Status.eq.Pending,ProcessingStatus.in.(Claimed,LocalProcessing,RecoveryRequired))&order=SubmittedAt.asc";
                var req = await CreateRequestAsync(HttpMethod.Get, recoverableEndpoint);
                var res = await _client.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    var content = await res.Content.ReadAsStringAsync();
                    var list = JsonConvert.DeserializeObject<List<dynamic>>(content) ?? new List<dynamic>();
                    return Result<List<dynamic>>.Ok(list);
                }

                // Fallback ONLY on schema mismatch (400 / 404); surface 429/5xx/401/403 directly
                if (res.StatusCode != HttpStatusCode.BadRequest && res.StatusCode != HttpStatusCode.NotFound)
                {
                    var errContent = await res.Content.ReadAsStringAsync();
                    return Result<List<dynamic>>.Fail($"Supabase request failed: {res.StatusCode} - {errContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to fetch with ProcessingStatus filter, falling back to Status=eq.Pending");
            }

            // Fallback: Join DsmUsers to get FullName where Status is Pending
            var endpoint = $"DsmSubmissions?select=*,DsmUsers(FullName)&StationId=eq.{settings.StationId}&Status=eq.Pending";
            var request = await CreateRequestAsync(HttpMethod.Get, endpoint);

            var response = await _client.SendAsync(request);
            var standardContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var list = JsonConvert.DeserializeObject<List<dynamic>>(standardContent) ?? new List<dynamic>();
                return Result<List<dynamic>>.Ok(list);
            }

            return Result<List<dynamic>>.Fail($"Supabase request failed: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch pending submissions");
            return Result<List<dynamic>>.Fail(ex.Message);
        }
    }

    public virtual async Task<Result<List<dynamic>>> FetchApprovedSubmissionsAsync()
    {
        try
        {
            var settings = await _syncConfigService.GetSettingsAsync();
            if (string.IsNullOrEmpty(settings.StationId)) return Result<List<dynamic>>.Fail("Station ID is blank.");

            var endpoint = $"DsmSubmissions?select=*,DsmUsers(FullName)&StationId=eq.{settings.StationId}&Status=eq.Approved&order=ApprovedAt.desc&limit=500";
            var request = await CreateRequestAsync(HttpMethod.Get, endpoint);

            var response = await _client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var list = JsonConvert.DeserializeObject<List<dynamic>>(content) ?? new List<dynamic>();
                return Result<List<dynamic>>.Ok(list);
            }

            return Result<List<dynamic>>.Fail($"Supabase request failed: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch approved submissions");
            return Result<List<dynamic>>.Fail(ex.Message);
        }
    }

    public virtual async Task<Result<List<dynamic>>> FetchSubmissionReadingsAsync(Guid submissionId)
    {
        try
        {
            var endpoint = $"DsmSubmissionReadings?SubmissionId=eq.{submissionId}&order=NozzleId.asc";
            var request = await CreateRequestAsync(HttpMethod.Get, endpoint);

            var response = await _client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var list = JsonConvert.DeserializeObject<List<dynamic>>(content) ?? new List<dynamic>();
                return Result<List<dynamic>>.Ok(list);
            }

            return Result<List<dynamic>>.Fail($"Failed to fetch readings: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch readings for submission {SubId}", submissionId);
            return Result<List<dynamic>>.Fail(ex.Message);
        }
    }

    public virtual async Task<Result<dynamic>> FetchSubmissionCollectionAsync(Guid submissionId)
    {
        try
        {
            var endpoint = $"DsmSubmissionCollections?SubmissionId=eq.{submissionId}&limit=1";
            var request = await CreateRequestAsync(HttpMethod.Get, endpoint);

            var response = await _client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var list = JsonConvert.DeserializeObject<List<dynamic>>(content);
                if (list != null && list.Count > 0)
                {
                    return Result<dynamic>.Ok(list[0]);
                }
                return Result<dynamic>.Fail("No collection details found for this submission.");
            }

            return Result<dynamic>.Fail($"Failed to fetch collection details: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch collection for submission {SubId}", submissionId);
            return Result<dynamic>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Result structure for DSM submission claim including fencing tokens (Stage 2.3).
    /// </summary>
    public class DsmClaimResult
    {
        public bool Success { get; set; }
        public string? Code { get; set; }
        public string? Error { get; set; }
        public string? Message { get; set; }
        public string? ClaimToken { get; set; }
        public int ClaimVersion { get; set; }
    }

    /// <summary>
    /// Initiates a durable claim on a DSM submission for approval.
    /// Employs PostgreSQL row-locking, monotonic ClaimVersion, and unique ClaimToken fencing.
    /// </summary>
    public virtual async Task<Result<DsmClaimResult>> ClaimSubmissionWithFencingAsync(Guid submissionId, string machineId, string approvedBy, string lockId)
    {
        try
        {
            var rpcPayload = JsonConvert.SerializeObject(new
            {
                p_submission_id = submissionId,
                p_machine_id = machineId,
                p_approved_by = approvedBy,
                p_lock_id = lockId
            });

            var rpcRequest = await CreateRequestAsync(HttpMethod.Post, "rpc/claim_dsm_submission", rpcPayload);
            var rpcResponse = await _client.SendAsync(rpcRequest);
            if (rpcResponse.IsSuccessStatusCode)
            {
                var rpcContent = await rpcResponse.Content.ReadAsStringAsync();
                Dictionary<string, object?>? rpcResult = null;
                try
                {
                    rpcResult = JsonConvert.DeserializeObject<Dictionary<string, object?>>(rpcContent);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to deserialize RPC claim response as dictionary: {Content}", rpcContent);
                }

                var success = Convert.ToBoolean(rpcResult?.GetValueOrDefault("success") ?? false);
                var code = rpcResult?.GetValueOrDefault("code")?.ToString();
                var claimToken = rpcResult?.GetValueOrDefault("claim_token")?.ToString();
                var claimVerObj = rpcResult?.GetValueOrDefault("claim_version");
                int claimVersion = claimVerObj != null ? Convert.ToInt32(claimVerObj) : 0;
                var errorMsg = rpcResult?.GetValueOrDefault("error")?.ToString();
                var msg = rpcResult?.GetValueOrDefault("message")?.ToString();

                var res = new DsmClaimResult
                {
                    Success = success,
                    Code = code,
                    Error = errorMsg,
                    Message = msg,
                    ClaimToken = claimToken,
                    ClaimVersion = claimVersion
                };

                if (success)
                {
                    _logger.Information("Successfully claimed submission {SubId} via RPC (Version={Ver}, Token={Token})", submissionId, claimVersion, claimToken);
                    return Result<DsmClaimResult>.Ok(res);
                }
                return Result<DsmClaimResult>.Fail(errorMsg ?? "Concurrency conflict: Submission already claimed or completed.");
            }
            else
            {
                var rpcError = await rpcResponse.Content.ReadAsStringAsync();
                var settings = await _syncConfigService.GetSettingsAsync();
                if (!settings.MultiComputerEnabled && IsMissingRpcResponse(rpcResponse.StatusCode, rpcError))
                {
                    _logger.Warning("Supabase RPC claim_dsm_submission is unavailable (HTTP {Status}). Single-PC mode verifying remote submission status before allowing local approval.", rpcResponse.StatusCode);
                    return await VerifySinglePcRemotePendingStatusAsync(submissionId, machineId, settings);
                }
                return Result<DsmClaimResult>.Fail($"Supabase claim error: {rpcResponse.StatusCode} - {rpcError}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception during claim in Supabase for {SubId}", submissionId);
            return Result<DsmClaimResult>.Fail(ex.Message);
        }
    }

    public virtual async Task<Result> ClaimSubmissionAsync(Guid submissionId, string machineId, string approvedBy, string lockId)
    {
        var res = await ClaimSubmissionWithFencingAsync(submissionId, machineId, approvedBy, lockId);
        return res.Success ? Result.Ok() : Result.Fail(res.Error ?? "Claim failed");
    }

    /// <summary>
    /// Renews an active DSM approval lease using fencing token and version verification.
    /// </summary>
    public virtual async Task<Result> RenewLeaseAsync(Guid submissionId, string machineId, string claimToken, int? claimVersion = null)
    {
        try
        {
            var rpcPayload = JsonConvert.SerializeObject(new
            {
                p_submission_id = submissionId,
                p_machine_id = machineId,
                p_claim_token = claimToken,
                p_claim_version = claimVersion
            });

            var rpcRequest = await CreateRequestAsync(HttpMethod.Post, "rpc/renew_dsm_lease", rpcPayload);
            var rpcResponse = await _client.SendAsync(rpcRequest);
            if (rpcResponse.IsSuccessStatusCode)
            {
                var rpcContent = await rpcResponse.Content.ReadAsStringAsync();
                var rpcResult = JsonConvert.DeserializeObject<Dictionary<string, object?>>(rpcContent);
                var success = Convert.ToBoolean(rpcResult?.GetValueOrDefault("success") ?? false);
                if (success)
                    return Result.Ok();

                var error = rpcResult?.GetValueOrDefault("error")?.ToString() ?? "Lease renewal failed";
                return Result.Fail(error);
            }

            var errContent = await rpcResponse.Content.ReadAsStringAsync();
            return Result.Fail($"Supabase lease renewal error: {rpcResponse.StatusCode} - {errContent}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception during lease renewal for {SubId}", submissionId);
            return Result.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Confirms that local SQLite commit has succeeded and marks the submission as Completed in Supabase.
    /// Passes fencing token and version to ensure stale claims cannot complete newer claims.
    /// </summary>
    private async Task<Result<DsmClaimResult>> VerifySinglePcRemotePendingStatusAsync(
        Guid submissionId,
        string machineId,
        SyncSettings settings)
    {
        var stationId = settings.StationId?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(stationId))
        {
            return FailClaimResult("CLOUD_VERIFICATION_UNAVAILABLE", "Cloud verification unavailable: StationId is empty.");
        }

        try
        {
            var encodedStation = Uri.EscapeDataString(stationId);
            var primaryEndpoint = $"DsmSubmissions?select=Id,StationId,Status,ProcessingStatus,IsDeleted,DeletedAt,ClaimedByMachineId&Id=eq.{submissionId}&StationId=eq.{encodedStation}&limit=1";
            var req = await CreateRequestAsync(HttpMethod.Get, primaryEndpoint);
            var res = await _client.SendAsync(req);

            // Fallback ONLY on schema mismatch (400 / 404 for optional columns such as ProcessingStatus/IsDeleted)
            if (!res.IsSuccessStatusCode &&
                (res.StatusCode == HttpStatusCode.BadRequest || res.StatusCode == HttpStatusCode.NotFound))
            {
                res.Dispose();
                var fallbackEndpoint = $"DsmSubmissions?select=Id,StationId,Status&Id=eq.{submissionId}&StationId=eq.{encodedStation}&limit=1";
                var fallbackReq = await CreateRequestAsync(HttpMethod.Get, fallbackEndpoint);
                res = await _client.SendAsync(fallbackReq);
            }

            using (res)
            {
                var content = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode)
                {
                    return FailClaimResult(
                        "CLOUD_VERIFICATION_UNAVAILABLE",
                        $"Cloud verification unavailable (HTTP {(int)res.StatusCode} {res.StatusCode}): {content}");
                }

                var rows = JsonConvert.DeserializeObject<List<Newtonsoft.Json.Linq.JObject>>(content);
                if (rows == null || rows.Count == 0)
                {
                    return FailClaimResult(
                        "REMOTE_MISSING",
                        $"Remote status conflict: Submission {submissionId} was not found for station '{stationId}' in cloud (missing or deleted).");
                }

                var row = rows[0];
                var rowIdStr = row["Id"]?.ToString() ?? row["id"]?.ToString();
                if (!Guid.TryParse(rowIdStr, out var rowGuid) || rowGuid != submissionId)
                {
                    return FailClaimResult(
                        "REMOTE_MISSING",
                        $"Remote status conflict: Cloud record ID mismatch for submission {submissionId}.");
                }

                var rowStation = (row["StationId"]?.ToString() ?? row["station_id"]?.ToString() ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(rowStation) &&
                    !string.Equals(rowStation, stationId, StringComparison.OrdinalIgnoreCase))
                {
                    return FailClaimResult(
                        "REMOTE_MISSING",
                        $"Remote status conflict: Submission {submissionId} belongs to station '{rowStation}', not '{stationId}'.");
                }

                var isDeletedTok = row["IsDeleted"] ?? row["is_deleted"];
                bool isDeleted = isDeletedTok != null &&
                                 isDeletedTok.Type != Newtonsoft.Json.Linq.JTokenType.Null &&
                                 bool.TryParse(isDeletedTok.ToString(), out var delBool) &&
                                 delBool;
                var deletedAtStr = row["DeletedAt"]?.ToString() ?? row["deleted_at"]?.ToString();
                if (isDeleted || !string.IsNullOrWhiteSpace(deletedAtStr))
                {
                    return FailClaimResult(
                        "REMOTE_DELETED",
                        $"Remote status conflict: Submission {submissionId} has already been deleted in cloud.");
                }

                var status = (row["Status"]?.ToString() ?? row["status"]?.ToString() ?? string.Empty).Trim();
                if (!string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
                {
                    return FailClaimResult(
                        "REMOTE_ALREADY_PROCESSED",
                        $"Remote status conflict: Submission {submissionId} is already '{(string.IsNullOrEmpty(status) ? "Unknown" : status)}' in cloud (no longer Pending).");
                }

                var procStatus = (row["ProcessingStatus"]?.ToString() ?? row["processing_status"]?.ToString() ?? string.Empty).Trim();
                var claimedBy = (row["ClaimedByMachineId"]?.ToString() ?? row["claimed_by_machine_id"]?.ToString() ?? string.Empty).Trim();
                if ((string.Equals(procStatus, "Claimed", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(procStatus, "LocalProcessing", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrEmpty(claimedBy) &&
                    !string.Equals(claimedBy, machineId, StringComparison.OrdinalIgnoreCase))
                {
                    return FailClaimResult(
                        "CONCURRENCY_CONFLICT",
                        $"Concurrency conflict: Submission {submissionId} is currently claimed by '{claimedBy}'.");
                }

                if (string.Equals(procStatus, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    return FailClaimResult(
                        "REMOTE_ALREADY_PROCESSED",
                        $"Remote status conflict: Submission {submissionId} is already Completed in cloud.");
                }

                return Result<DsmClaimResult>.Ok(new DsmClaimResult
                {
                    Success = true,
                    Code = "SINGLE_PC_FALLBACK_NO_CLAIM_RPC",
                    Message = "Single-PC mode: remote submission verified Pending; proceeding with local commit + conditional REST completion."
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Remote Pending status check failed in single-PC fallback for {SubId}", submissionId);
            return FailClaimResult(
                "CLOUD_VERIFICATION_UNAVAILABLE",
                $"Cloud verification unavailable ({ex.GetType().Name}): {ex.Message}");
        }
    }

    private static Result<DsmClaimResult> FailClaimResult(string code, string error)
    {
        return new Result<DsmClaimResult>
        {
            Success = false,
            Error = error,
            Data = new DsmClaimResult
            {
                Success = false,
                Code = code,
                Error = error
            }
        };
    }

    private static bool IsMissingRpcResponse(HttpStatusCode statusCode, string responseContent)
    {
        if (statusCode == HttpStatusCode.NotFound) return true;
        if (!string.IsNullOrEmpty(responseContent) &&
            (responseContent.Contains("PGRST202", StringComparison.OrdinalIgnoreCase) ||
             responseContent.Contains("Could not find the function", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        return false;
    }

    public virtual async Task<Result> CompleteSubmissionApprovalAsync(
        Guid submissionId,
        string machineId,
        string lockId,
        string? claimToken = null,
        int? claimVersion = null)
        => await CompleteSubmissionApprovalAsync(submissionId, machineId, lockId, claimToken, claimVersion, null, null);

    public virtual async Task<Result> CompleteSubmissionApprovalAsync(
        Guid submissionId,
        string machineId,
        string lockId,
        string? claimToken,
        int? claimVersion,
        string? approvedBy,
        DateTime? approvedAt)
    {
        try
        {
            var rpcPayload = JsonConvert.SerializeObject(new
            {
                p_submission_id = submissionId,
                p_machine_id = machineId,
                p_lock_id = lockId,
                p_claim_token = claimToken,
                p_claim_version = claimVersion
            });

            var rpcRequest = await CreateRequestAsync(HttpMethod.Post, "rpc/complete_dsm_approval", rpcPayload);
            var rpcResponse = await _client.SendAsync(rpcRequest);
            if (rpcResponse.IsSuccessStatusCode)
            {
                var rpcContent = await rpcResponse.Content.ReadAsStringAsync();
                var rpcResult = JsonConvert.DeserializeObject<Dictionary<string, object?>>(rpcContent);
                var success = Convert.ToBoolean(rpcResult?.GetValueOrDefault("success") ?? false);
                var code = rpcResult?.GetValueOrDefault("code")?.ToString();
                if (success || code == "ALREADY_COMPLETED_SAME_CLAIM")
                {
                    _logger.Information("Successfully completed approval for submission {SubId} via RPC (Code={Code}).", submissionId, code);
                    return Result.Ok();
                }

                var errorMsg = rpcResult?.GetValueOrDefault("error")?.ToString() ?? "Completion rejected by server";
                return Result.Fail(errorMsg);
            }

            var errContent = await rpcResponse.Content.ReadAsStringAsync();
            var settings = await _syncConfigService.GetSettingsAsync();

            // Single-PC mode only: when complete_dsm_approval RPC is absent (HTTP 404 / PGRST202),
            // execute conditional atomic REST PATCH requiring Status=eq.Pending and verifying updated row count > 0.
            // Multi-computer mode retains strict RPC enforcement.
            if (!settings.MultiComputerEnabled && IsMissingRpcResponse(rpcResponse.StatusCode, errContent))
            {
                var stationFilter = !string.IsNullOrWhiteSpace(settings.StationId)
                    ? $"&StationId=eq.{Uri.EscapeDataString(settings.StationId.Trim())}"
                    : string.Empty;
                var endpoint = $"DsmSubmissions?Id=eq.{submissionId}&Status=eq.Pending{stationFilter}";
                var patchPayload = new
                {
                    Status = "Approved",
                    ApprovedAt = (approvedAt ?? DateTime.UtcNow).ToUniversalTime().ToString("o"),
                    ApprovedBy = string.IsNullOrWhiteSpace(approvedBy) ? machineId : approvedBy,
                    ApprovalLockId = lockId
                };

                var patchJson = JsonConvert.SerializeObject(patchPayload);
                var patchReq = await CreateRequestAsync(HttpMethod.Patch, endpoint, patchJson);
                patchReq.Headers.Add("Prefer", "return=representation");

                var patchRes = await _client.SendAsync(patchReq);
                var patchContent = await patchRes.Content.ReadAsStringAsync();

                if (patchRes.IsSuccessStatusCode)
                {
                    var updatedRows = JsonConvert.DeserializeObject<List<dynamic>>(patchContent);
                    if (updatedRows != null && updatedRows.Count > 0)
                    {
                        _logger.Information("Completed approval for {SubId} via single-PC conditional REST PATCH fallback ({Count} row updated).", submissionId, updatedRows.Count);
                        return Result.Ok();
                    }

                    return Result.Fail("Concurrency conflict or missing pending row: conditional REST PATCH updated 0 rows.");
                }

                return Result.Fail($"Supabase conditional REST PATCH fallback failed: {patchRes.StatusCode} - {patchContent}");
            }

            return Result.Fail($"Supabase complete failed: {rpcResponse.StatusCode} - {errContent}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception completing approval for {SubId}", submissionId);
            return Result.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Updates status of a submission using the two-phase claim and complete workflow.
    /// </summary>
    public virtual Task<Result> ApproveSubmissionAsync(Guid submissionId, string approvedBy, string lockId)
        => ApproveSubmissionAsync(submissionId, approvedBy, lockId, null);

    public virtual async Task<Result> ApproveSubmissionAsync(Guid submissionId, string approvedBy, string lockId, DateTime? approvedAt)
    {
        var settings = await _syncConfigService.GetSettingsAsync();
        var machineId = !string.IsNullOrEmpty(settings.MachineId) ? settings.MachineId : Environment.MachineName;

        var claimResult = await ClaimSubmissionWithFencingAsync(submissionId, machineId, approvedBy, lockId);
        if (!claimResult.Success || claimResult.Data == null)
            return Result.Fail(claimResult.Error ?? "Claim failed");

        return await CompleteSubmissionApprovalAsync(
            submissionId,
            machineId,
            lockId,
            claimResult.Data.ClaimToken,
            claimResult.Data.ClaimVersion,
            approvedBy,
            approvedAt);
    }

    public virtual async Task<Result> RejectSubmissionAsync(Guid submissionId, string reason)
    {
        try
        {
            var endpoint = $"DsmSubmissions?Id=eq.{submissionId}&Status=eq.Pending";
            
            var payload = new
            {
                Status = "Rejected",
                RejectionReason = reason
            };

            var json = JsonConvert.SerializeObject(payload);
            var request = await CreateRequestAsync(HttpMethod.Patch, endpoint, json);
            request.Headers.Add("Prefer", "return=representation");

            var response = await _client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var updatedRows = JsonConvert.DeserializeObject<List<dynamic>>(content);
                if (updatedRows != null && updatedRows.Count > 0)
                {
                    _logger.Information("Successfully rejected submission {SubId} in Supabase.", submissionId);
                    return Result.Ok();
                }
                return Result.Fail("Concurrency conflict: The submission has already been processed.");
            }

            return Result.Fail($"Supabase API error: {response.StatusCode} - {content}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception during rejection in Supabase for {SubId}", submissionId);
            return Result.Fail(ex.Message);
        }
    }

    public virtual async Task<Result> ResetSubmissionToPendingAsync(Guid submissionId)
    {
        try
        {
            var endpoint = $"DsmSubmissions?Id=eq.{submissionId}";
            
            var payload = new
            {
                Status = "Pending",
                ApprovedAt = (string?)null,
                ApprovedBy = (string?)null,
                ApprovalLockId = (string?)null
            };

            var json = JsonConvert.SerializeObject(payload);
            var request = await CreateRequestAsync(HttpMethod.Patch, endpoint, json);
            request.Headers.Add("Prefer", "return=representation");

            var response = await _client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var updatedRows = JsonConvert.DeserializeObject<List<dynamic>>(content);
                if (updatedRows != null && updatedRows.Count > 0)
                {
                    _logger.Information("Successfully reset submission {SubId} to Pending in Supabase.", submissionId);
                    return Result.Ok();
                }
                return Result.Fail("Failed to reset submission status.");
            }

            return Result.Fail($"Supabase API error: {response.StatusCode} - {content}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception during status reset in Supabase for {SubId}", submissionId);
            return Result.Fail(ex.Message);
        }
    }


    public virtual async Task<Result> SendNotificationAsync(string stationId, string userId, string role, string message)
    {
        try
        {
            var payload = new
            {
                StationId = stationId,
                UserId = userId,
                Role = role,
                Message = message,
                IsRead = false
            };

            var json = JsonConvert.SerializeObject(payload);
            var request = await CreateRequestAsync(HttpMethod.Post, "DsmNotifications", json);

            var response = await _client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                return Result.Ok();
            }

            var err = await response.Content.ReadAsStringAsync();
            return Result.Fail($"Failed to send notification: {err}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to send notification to {Role} {User}", role, userId);
            return Result.Fail(ex.Message);
        }
    }

    // ── Pump Nozzle Configuration ────────────────────────────────────────────

    /// <summary>
    /// Fetches all PumpNozzleConfig entries for a station from Supabase.
    /// </summary>
    public async Task<Result<List<PumpNozzleConfigDto>>> FetchPumpNozzleConfigAsync(string stationId)
    {
        try
        {
            var endpoint = $"PumpNozzleConfig?StationId=eq.{stationId}&order=PumpId.asc,SortOrder.asc";
            var request = await CreateRequestAsync(HttpMethod.Get, endpoint);

            var response = await _client.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var list = JsonConvert.DeserializeObject<List<PumpNozzleConfigDto>>(content) ?? new List<PumpNozzleConfigDto>();
                return Result<List<PumpNozzleConfigDto>>.Ok(list);
            }

            return Result<List<PumpNozzleConfigDto>>.Fail($"Supabase request failed ({response.StatusCode}): {content}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to fetch PumpNozzleConfig for station {StationId}", stationId);
            return Result<List<PumpNozzleConfigDto>>.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Upserts a list of nozzle configs for a pump (replaces entire pump config).
    /// </summary>
    public async Task<Result> SavePumpNozzleConfigAsync(string stationId, int pumpId, List<(int NozzleId, string FuelType, int SortOrder)> nozzles)
    {
        try
        {
            var settings = await _syncConfigService.GetSettingsAsync();

            // 1. Delete existing config for this pump
            var delEndpoint = $"PumpNozzleConfig?StationId=eq.{stationId}&PumpId=eq.{pumpId}";
            var delRequest = await CreateRequestAsync(HttpMethod.Delete, delEndpoint);
            if (!string.IsNullOrWhiteSpace(settings.SupabaseServiceRoleKey))
            {
                delRequest.Headers.Remove("apikey");
                delRequest.Headers.Remove("Authorization");
                delRequest.Headers.Add("apikey", settings.SupabaseServiceRoleKey);
                delRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.SupabaseServiceRoleKey);
            }
            await _client.SendAsync(delRequest);

            if (nozzles.Count == 0) return Result.Ok();

            // 2. Insert new config
            var payload = nozzles.Select(n => new
            {
                StationId = stationId,
                PumpId = pumpId,
                NozzleId = n.NozzleId,
                FuelType = n.FuelType,
                IsActive = true,
                SortOrder = n.SortOrder
            }).ToList();

            var json = JsonConvert.SerializeObject(payload);
            var insRequest = await CreateRequestAsync(HttpMethod.Post, "PumpNozzleConfig", json);
            insRequest.Headers.Add("Prefer", "resolution=merge-duplicates");

            var response = await _client.SendAsync(insRequest);
            if (response.IsSuccessStatusCode)
            {
                _logger.Information("Saved PumpNozzleConfig for Pump {PumpId} with {Count} nozzles.", pumpId, nozzles.Count);
                return Result.Ok();
            }

            var error = await response.Content.ReadAsStringAsync();
            return Result.Fail($"Failed to save nozzle config: {response.StatusCode} — {error}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception saving PumpNozzleConfig for Pump {PumpId}", pumpId);
            return Result.Fail(ex.Message);
        }
    }
}

public class PumpNozzleConfigDto
{
    public long Id { get; set; }
    public string StationId { get; set; } = string.Empty;
    public int PumpId { get; set; }
    public int NozzleId { get; set; }
    public string FuelType { get; set; } = "MS-I";
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
