using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;

namespace FuelPro.Sync;

/// <summary>
/// Background service that polls Supabase for pending DSM submissions.
/// Runs independently of the UI thread and raises an event on changes.
/// </summary>
public class DsmSubmissionPollingService
{
    private readonly SyncConfigService _configService;
    private readonly HttpClient _client;
    private readonly ILogger _logger = Log.ForContext<DsmSubmissionPollingService>();
    private Timer? _pollingTimer;
    private readonly object _lock = new();
    private bool _isPolling;
    
    private int _lastPendingCount = -1;

    public event Action<int>? PendingCountChanged;

    public int CurrentPendingCount => _lastPendingCount < 0 ? 0 : _lastPendingCount;

    public DsmSubmissionPollingService(SyncConfigService configService)
    {
        _configService = configService;
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_pollingTimer != null) return;
            
            _logger.Information("Starting DSM submission background polling service...");
            // [TESTING] Poll every 1 second, starting immediately (was: 30s interval)
            _pollingTimer = new Timer(async _ => await PollAsync(), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _logger.Information("Stopping DSM submission background polling service...");
            _pollingTimer?.Dispose();
            _pollingTimer = null;
        }
    }

    public async Task ForceRefreshAsync()
    {
        await PollAsync();
    }

    private async Task PollAsync()
    {
        if (_isPolling) return;
        _isPolling = true;

        try
        {
            var settings = await _configService.GetSettingsAsync();
            if (!settings.SyncEnabled || string.IsNullOrEmpty(settings.SupabaseUrl) || string.IsNullOrEmpty(settings.SupabaseApiKey) || string.IsNullOrEmpty(settings.StationId))
            {
                UpdatePendingCount(0);
                return;
            }

            var requestUrl = $"{settings.SupabaseUrl.TrimEnd('/')}/rest/v1/DsmSubmissions?StationId=eq.{settings.StationId}&Status=eq.Pending";
            
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("apikey", settings.SupabaseApiKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.SupabaseApiKey);

            var response = await _client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var items = JsonConvert.DeserializeObject<dynamic[]>(json);
                var count = items?.Length ?? 0;
                UpdatePendingCount(count);
            }
            else
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.Warning("Supabase polling request failed (Status: {StatusCode}): {Error}", response.StatusCode, error);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error occurred during background pending submissions polling");
        }
        finally
        {
            _isPolling = false;
        }
    }

    private void UpdatePendingCount(int count)
    {
        if (_lastPendingCount != count)
        {
            _lastPendingCount = count;
            _logger.Information("Pending DSM submission count updated: {Count}", count);
            PendingCountChanged?.Invoke(count);
        }
    }
}
