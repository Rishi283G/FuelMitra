using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Serilog;

namespace FuelPro.Sync;

public class SupabaseHttpClient
{
    private readonly HttpClient _client;
    private readonly ILogger _logger = Log.ForContext<SupabaseHttpClient>();
    private string _url = string.Empty;
    private string _apiKey = string.Empty;

    public SupabaseHttpClient()
    {
        _client = new HttpClient();
        _client.Timeout = TimeSpan.FromSeconds(30);
    }

    public void Configure(string url, string apiKey)
    {
        _url = url.TrimEnd('/');
        _apiKey = apiKey;
    }

    public async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, string endpoint, string? jsonBody = null, bool isUpsert = false, string? onConflict = null)
    {
        if (string.IsNullOrEmpty(_url) || string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException("Supabase HTTP client is not configured");
        }

        var requestUrl = $"{_url}/rest/v1/{endpoint}";

        // If onConflict is specified, append it as a query parameter for PostgREST
        if (isUpsert && !string.IsNullOrEmpty(onConflict))
        {
            var separator = requestUrl.Contains('?') ? "&" : "?";
            requestUrl += $"{separator}on_conflict={onConflict}";
        }

        var request = new HttpRequestMessage(method, requestUrl);

        request.Headers.Add("apikey", _apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        if (isUpsert)
        {
            // PostgREST upsert headers
            request.Headers.Add("Prefer", "resolution=merge-duplicates");
        }

        if (jsonBody != null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        _logger.Debug("Sending {Method} request to {Url}", method, requestUrl);
        
        var response = await _client.SendAsync(request);
        return response;
    }
}
