using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;
using FuelPro.Core.Common;

namespace FuelPro.Sync;

/// <summary>
/// Calls the official Supabase Auth Admin API using the secure service_role key to manage DSM accounts.
/// </summary>
public class DsmAuthAdminService
{
    private readonly SyncConfigService _syncConfigService;
    private readonly HttpClient _client;
    private readonly ILogger _logger = Log.ForContext<DsmAuthAdminService>();

    public DsmAuthAdminService(SyncConfigService syncConfigService)
    {
        _syncConfigService = syncConfigService;
        _client = SupabaseHttpClient.CreateHttpClient(TimeSpan.FromSeconds(20));
    }

    /// <summary>
    /// Creates a user in Supabase Auth via the Admin API.
    /// </summary>
    /// <returns>The Supabase Auth User ID (UUID) if successful; otherwise, null.</returns>
    public async Task<Result<string>> CreateDsmAuthUserAsync(string email, string mobileNumber, string password, string fullName, string employeeCode)
    {
        try
        {
            var settings = await _syncConfigService.GetSettingsAsync();
            if (string.IsNullOrEmpty(settings.SupabaseUrl) || string.IsNullOrEmpty(settings.SupabaseServiceRoleKey))
            {
                return Result<string>.Fail("Supabase URL or Service Role Key is not configured. Please set them in Developer Tools.");
            }

            var requestUrl = $"{settings.SupabaseUrl.TrimEnd('/')}/auth/v1/admin/users";
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl);

            request.Headers.Add("apikey", settings.SupabaseServiceRoleKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SupabaseServiceRoleKey);

            // Construct the payload for the Admin API
            var payload = new
            {
                email = email.Trim(),
                phone = mobileNumber.Trim(),
                password = password,
                email_confirm = true,
                phone_confirm = true,
                user_metadata = new
                {
                    full_name = fullName.Trim(),
                    station_id = settings.StationId,
                    employee_code = employeeCode.Trim(),
                    role = "dsm"
                }
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            _logger.Information("Sending request to provision Supabase Auth user for {Email} / {MobileNumber}", email, mobileNumber);
            var response = await _client.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var userResponse = JsonConvert.DeserializeObject<dynamic>(responseContent);
                string authUserId = userResponse?.id ?? string.Empty;
                if (string.IsNullOrEmpty(authUserId))
                {
                    return Result<string>.Fail("Supabase Auth API did not return a valid user ID.");
                }

                _logger.Information("Successfully provisioned Supabase Auth user with ID: {AuthUserId}", authUserId);
                return Result<string>.Ok(authUserId);
            }
            else
            {
                _logger.Error("Supabase Auth Admin user provisioning failed (Status: {StatusCode}): {Error}", response.StatusCode, responseContent);
                var errorObj = JsonConvert.DeserializeObject<dynamic>(responseContent);
                string errMsg = errorObj?.msg ?? errorObj?.message ?? "Unknown error provisioning user.";
                return Result<string>.Fail($"Supabase Auth provisioning failed: {errMsg}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception provisioning Supabase Auth user for {MobileNumber}", mobileNumber);
            return Result<string>.Fail($"Error provisioning user: {ex.Message}");
        }
    }

    /// <summary>
    /// Bans or unbans a user in Supabase Auth via the Admin API.
    /// </summary>
    public async Task<Result> ToggleDsmAuthUserActiveAsync(string authUserId, bool isActive)
    {
        try
        {
            var settings = await _syncConfigService.GetSettingsAsync();
            if (string.IsNullOrEmpty(settings.SupabaseUrl) || string.IsNullOrEmpty(settings.SupabaseServiceRoleKey))
            {
                return Result.Fail("Supabase URL or Service Role Key is not configured.");
            }

            var requestUrl = $"{settings.SupabaseUrl.TrimEnd('/')}/auth/v1/admin/users/{authUserId}";
            var request = new HttpRequestMessage(HttpMethod.Put, requestUrl);

            request.Headers.Add("apikey", settings.SupabaseServiceRoleKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SupabaseServiceRoleKey);

            // Banning the user blocks all current and future active sessions.
            // ban_duration can be set to "none" (unban) or a long duration (e.g. "876000h" - 100 years to ban).
            var payload = new
            {
                ban_duration = isActive ? "none" : "876000h"
            };

            var jsonBody = JsonConvert.SerializeObject(payload);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            _logger.Information("Updating Supabase Auth active state for {AuthUserId} to {IsActive}", authUserId, isActive);
            var response = await _client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                _logger.Information("Successfully updated Supabase Auth active state for {AuthUserId}", authUserId);
                return Result.Ok();
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.Error("Failed to update active state for {AuthUserId} in Supabase Auth (Status: {StatusCode}): {Error}", authUserId, response.StatusCode, error);
                return Result.Fail($"Failed to update Supabase Auth status: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception updating active state for {AuthUserId}", authUserId);
            return Result.Fail($"Error updating Auth active state: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes a user from Supabase Auth via the Admin API.
    /// </summary>
    public async Task<Result> DeleteDsmAuthUserAsync(string authUserId)
    {
        try
        {
            var settings = await _syncConfigService.GetSettingsAsync();
            if (string.IsNullOrEmpty(settings.SupabaseUrl) || string.IsNullOrEmpty(settings.SupabaseServiceRoleKey))
            {
                return Result.Fail("Supabase URL or Service Role Key is not configured.");
            }

            var requestUrl = $"{settings.SupabaseUrl.TrimEnd('/')}/auth/v1/admin/users/{authUserId}";
            var request = new HttpRequestMessage(HttpMethod.Delete, requestUrl);

            request.Headers.Add("apikey", settings.SupabaseServiceRoleKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SupabaseServiceRoleKey);

            _logger.Information("Deleting Supabase Auth user {AuthUserId}", authUserId);
            var response = await _client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                _logger.Information("Successfully deleted Supabase Auth user {AuthUserId}", authUserId);
                return Result.Ok();
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.Error("Failed to delete Supabase Auth user {AuthUserId} (Status: {StatusCode}): {Error}", authUserId, response.StatusCode, error);
                return Result.Fail($"Failed to delete Supabase Auth user: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Exception deleting Supabase Auth user {AuthUserId}", authUserId);
            return Result.Fail($"Error deleting Auth user: {ex.Message}");
        }
    }
}
