using System;
using System.Collections.Generic;
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
    {
        _syncConfigService = syncConfigService;
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
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

            // Join DsmUsers to get FullName
            var endpoint = $"DsmSubmissions?select=*,DsmUsers(FullName)&StationId=eq.{settings.StationId}&Status=eq.Pending";
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

            var endpoint = $"DsmSubmissions?select=*,DsmUsers(FullName)&StationId=eq.{settings.StationId}&Status=eq.Approved&order=ApprovedAt.desc&limit=50";
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
            var endpoint = $"DsmSubmissionReadings?SubmissionId=eq.{submissionId}";
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
    /// Updates status of a submission. Employs optimistic locking by checking that status is currently 'Pending'.
    /// </summary>
    public virtual async Task<Result> ApproveSubmissionAsync(Guid submissionId, string approvedBy, string lockId)
    {
        try
        {
            var endpoint = $"DsmSubmissions?Id=eq.{submissionId}&Status=eq.Pending";
            
            var payload = new
            {
                Status = "Approved",
                ApprovedAt = DateTime.UtcNow.ToString("o"),
                ApprovedBy = approvedBy,
                ApprovalLockId = lockId
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
                    _logger.Information("Successfully approved submission {SubId} in Supabase.", submissionId);
                    return Result.Ok();
                }
                return Result.Fail("Concurrency conflict: The submission has already been approved, rejected, or expired.");
            }

            return Result.Fail($"Supabase API error: {response.StatusCode} - {content}");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception during approval in Supabase for {SubId}", submissionId);
            return Result.Fail(ex.Message);
        }
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
            delRequest.Headers.Add("apikey", settings.SupabaseServiceRoleKey);
            delRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", settings.SupabaseServiceRoleKey);
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
