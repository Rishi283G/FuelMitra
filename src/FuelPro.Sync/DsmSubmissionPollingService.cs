using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;

namespace FuelPro.Sync;

/// <summary>
/// Background service that polls Supabase for pending DSM submissions.
/// Uses <see cref="DsmPendingSubmissionEvaluator"/> so badge counts match the Approval Queue,
/// protects against out-of-order overwrites via monotonic versioning, tracks explicit
/// authoritative vs failed/offline status, and reacts to station changes &amp; sync pulls.
/// </summary>
public class DsmSubmissionPollingService : IDisposable
{
    private readonly SyncConfigService _configService;
    private readonly FuelPro.Core.Services.IFeatureToggleService? _featureToggleService;
    private readonly SyncEngine? _syncEngine;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly ILogger _logger = Log.ForContext<DsmSubmissionPollingService>();

    private Timer? _pollingTimer;
    private readonly SemaphoreSlim _pollLock = new(1, 1);
    private readonly object _stateLock = new();

    private bool _isDisposed;
    private bool _isStopped = true;
    private int _consecutiveFailures;
    private DateTime _lastPollCompletedUtc = DateTime.MinValue;
    private int _lastPendingCount = -1;
    private bool _hasAuthoritativeCount;
    private string? _lastError;
    private string? _activeStationId;
    private long _stateVersion;
    private long _appliedStateVersion;

    // Polling intervals & bounds
    private static readonly TimeSpan BaseInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ForceRefreshThrottleWindow = TimeSpan.FromSeconds(2);

    public event Action<int>? PendingCountChanged;
    public event Action? PollStateChanged;

    public int CurrentPendingCount
    {
        get
        {
            lock (_stateLock)
            {
                return _lastPendingCount < 0 ? 0 : _lastPendingCount;
            }
        }
    }

    public bool HasAuthoritativeCount
    {
        get
        {
            lock (_stateLock)
            {
                return _hasAuthoritativeCount;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_stateLock)
            {
                return _lastError;
            }
        }
    }

    public DsmSubmissionPollingService(
        SyncConfigService configService,
        FuelPro.Core.Services.IFeatureToggleService? featureToggleService = null,
        HttpClient? httpClient = null,
        SyncEngine? syncEngine = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _featureToggleService = featureToggleService;
        _syncEngine = syncEngine;

        if (httpClient != null)
        {
            _client = httpClient;
            _ownsClient = false;
        }
        else
        {
            _client = SupabaseHttpClient.CreateHttpClient(TimeSpan.FromSeconds(15));
            _ownsClient = true;
        }

        _configService.SettingsChanged += OnConfigSettingsChanged;
        if (_syncEngine != null)
        {
            _syncEngine.PullCompleted += OnSyncPullCompleted;
        }
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(DsmSubmissionPollingService));
            if (!_isStopped) return;

            _isStopped = false;
            _logger.Information("Starting DSM submission background polling service (15s nominal + jitter)...");

            _pollingTimer = new Timer(async _ => await OnTimerTickAsync(), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (_isStopped) return;
            _isStopped = true;
            _logger.Information("Stopping DSM submission background polling service...");
            _pollingTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _pollingTimer?.Dispose();
            _pollingTimer = null;
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _configService.SettingsChanged -= OnConfigSettingsChanged;
            if (_syncEngine != null)
            {
                _syncEngine.PullCompleted -= OnSyncPullCompleted;
            }
            Stop();
            _pollLock.Dispose();
            if (_ownsClient)
            {
                _client.Dispose();
            }
        }
    }

    private void OnConfigSettingsChanged(SyncSettings updatedSettings)
    {
        bool stationChanged;
        lock (_stateLock)
        {
            if (_isDisposed) return;
            var newStation = updatedSettings.StationId?.Trim() ?? string.Empty;
            stationChanged = _activeStationId != null &&
                             !string.Equals(_activeStationId, newStation, StringComparison.OrdinalIgnoreCase);

            if (stationChanged || !updatedSettings.SyncEnabled)
            {
                _activeStationId = newStation;
                long v = ++_stateVersion;
                _appliedStateVersion = v;
                _lastError = null;
                _hasAuthoritativeCount = !updatedSettings.SyncEnabled;
                if (_lastPendingCount != 0)
                {
                    _lastPendingCount = 0;
                    PendingCountChanged?.Invoke(0);
                }
                PollStateChanged?.Invoke();
            }
            else if (_activeStationId == null && !string.IsNullOrEmpty(newStation))
            {
                _activeStationId = newStation;
            }
        }

        if (stationChanged && !_isDisposed && updatedSettings.SyncEnabled)
        {
            _ = Task.Run(() => ForceRefreshAsync(bypassThrottle: true));
        }
    }

    private void OnSyncPullCompleted()
    {
        if (_isDisposed || _isStopped) return;
        _ = Task.Run(() => ForceRefreshAsync());
    }

    /// <summary>
    /// Synchronizes the badge state directly from the evaluated Approval Queue result
    /// and prevents older in-flight polls from overwriting it.
    /// </summary>
    public void SyncCountFromQueue(int count, string? stationId = null)
    {
        if (count < 0) count = 0;
        bool countChanged = false;
        bool stateChanged = false;

        lock (_stateLock)
        {
            if (_isDisposed) return;
            if (!string.IsNullOrWhiteSpace(stationId))
                _activeStationId = stationId.Trim();

            long version = ++_stateVersion;
            _appliedStateVersion = version;

            if (!_hasAuthoritativeCount || _lastError != null)
            {
                _hasAuthoritativeCount = true;
                _lastError = null;
                stateChanged = true;
            }

            if (_lastPendingCount != count)
            {
                _lastPendingCount = count;
                countChanged = true;
                stateChanged = true;
                _logger.Information("Pending DSM submission count reconciled from queue: {Count}", count);
            }

            _lastPollCompletedUtc = DateTime.UtcNow;
        }

        if (countChanged) PendingCountChanged?.Invoke(count);
        if (stateChanged) PollStateChanged?.Invoke();
    }

    /// <summary>
    /// Marks the count as stale/unavailable after a queue or poll failure so the UI
    /// clears any outdated number and displays offline/refresh-failed status honestly.
    /// </summary>
    public void MarkRefreshFailed(string? errorMessage)
    {
        bool countChanged = false;
        bool stateChanged = false;
        var cleanError = string.IsNullOrWhiteSpace(errorMessage)
            ? "Unable to refresh pending DSM submissions."
            : errorMessage.Trim();

        lock (_stateLock)
        {
            if (_isDisposed) return;
            long version = ++_stateVersion;
            _appliedStateVersion = version;

            if (_hasAuthoritativeCount || _lastError != cleanError)
            {
                _hasAuthoritativeCount = false;
                _lastError = cleanError;
                stateChanged = true;
            }

            if (_lastPendingCount != 0)
            {
                _lastPendingCount = 0;
                countChanged = true;
                stateChanged = true;
            }
        }

        if (countChanged) PendingCountChanged?.Invoke(0);
        if (stateChanged) PollStateChanged?.Invoke();
    }

    private async Task OnTimerTickAsync()
    {
        lock (_stateLock)
        {
            if (_isStopped || _isDisposed) return;
        }

        TimeSpan nextDelay = BaseInterval;

        try
        {
            var outcome = await ExecutePollWithSingleFlightAsync(isForced: false, bypassThrottle: false);
            nextDelay = CalculateNextDelay(outcome);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unhandled error in DSM submission polling timer tick");
            nextDelay = CalculateNextDelay(new PollOutcome(IsSuccess: false, StatusCode: null, RetryAfter: null));
        }
        finally
        {
            lock (_stateLock)
            {
                if (!_isStopped && !_isDisposed && _pollingTimer != null)
                {
                    _pollingTimer.Change(nextDelay, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    public Task ForceRefreshAsync() => ForceRefreshAsync(bypassThrottle: false);

    public async Task ForceRefreshAsync(bool bypassThrottle)
    {
        if (_isDisposed) return;

        if (!bypassThrottle && (DateTime.UtcNow - _lastPollCompletedUtc < ForceRefreshThrottleWindow))
        {
            _logger.Debug("DSM submission ForceRefresh throttled (completed within last 2s)");
            return;
        }

        var outcome = await ExecutePollWithSingleFlightAsync(isForced: true, bypassThrottle: bypassThrottle);

        if (outcome.IsSuccess)
        {
            lock (_stateLock)
            {
                if (!_isStopped && !_isDisposed && _pollingTimer != null)
                {
                    var delay = CalculateNextDelay(outcome);
                    _pollingTimer.Change(delay, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    private async Task<PollOutcome> ExecutePollWithSingleFlightAsync(bool isForced, bool bypassThrottle)
    {
        if (!await _pollLock.WaitAsync(TimeSpan.FromSeconds(30)))
        {
            _logger.Warning("DSM polling semaphore wait timed out. Skipping cycle.");
            return new PollOutcome(IsSuccess: false, StatusCode: null, RetryAfter: null);
        }

        try
        {
            if (isForced && !bypassThrottle && (DateTime.UtcNow - _lastPollCompletedUtc < ForceRefreshThrottleWindow))
            {
                _logger.Debug("DSM submission ForceRefresh throttled after acquiring lock");
                return new PollOutcome(IsSuccess: true, StatusCode: HttpStatusCode.OK, RetryAfter: null);
            }

            return await PollCoreAsync();
        }
        finally
        {
            _lastPollCompletedUtc = DateTime.UtcNow;
            _pollLock.Release();
        }
    }

    private async Task<PollOutcome> PollCoreAsync()
    {
        long requestVersion;
        lock (_stateLock)
        {
            if (_isDisposed) return new PollOutcome(IsSuccess: false, StatusCode: null, RetryAfter: null);
            requestVersion = ++_stateVersion;
        }

        try
        {
            if (_featureToggleService != null && !_featureToggleService.IsDsmPwaEnabled)
            {
                ApplyAuthoritativeCountIfCurrent(0, null, requestVersion);
                return new PollOutcome(IsSuccess: true, StatusCode: HttpStatusCode.OK, RetryAfter: null);
            }

            var settings = await _configService.GetSettingsAsync();
            var stationId = settings.StationId?.Trim() ?? string.Empty;

            if (!settings.SyncEnabled || string.IsNullOrEmpty(settings.SupabaseUrl) || string.IsNullOrEmpty(settings.SupabaseApiKey) || string.IsNullOrEmpty(stationId))
            {
                ApplyAuthoritativeCountIfCurrent(0, stationId, requestVersion);
                return new PollOutcome(IsSuccess: true, StatusCode: HttpStatusCode.OK, RetryAfter: null);
            }

            lock (_stateLock)
            {
                if (_activeStationId != null && !string.Equals(_activeStationId, stationId, StringComparison.OrdinalIgnoreCase))
                {
                    // Station switched cleanly before poll ran
                    _activeStationId = stationId;
                }
                else
                {
                    _activeStationId ??= stationId;
                }
            }

            var baseUrl = settings.SupabaseUrl.TrimEnd('/');
            var encodedStationId = Uri.EscapeDataString(stationId);
            var primaryUrl = $"{baseUrl}/rest/v1/DsmSubmissions?select=*,DsmUsers(FullName)&StationId=eq.{encodedStationId}&or=(Status.eq.Pending,ProcessingStatus.in.(Claimed,LocalProcessing,RecoveryRequired))&order=SubmittedAt.asc";

            var response = await SendAuthenticatedGetAsync(primaryUrl, settings.SupabaseApiKey);

            // Fallback ONLY if ProcessingStatus/relational query is unsupported on remote schema (HTTP 400 / 404)
            if (!response.IsSuccessStatusCode &&
                (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.NotFound))
            {
                var errPreview = await response.Content.ReadAsStringAsync();
                response.Dispose();
                _logger.Warning("Recoverable DSM polling query returned {StatusCode} ({Error}); falling back to Status=eq.Pending", response.StatusCode, errPreview);
                var fallbackUrl = $"{baseUrl}/rest/v1/DsmSubmissions?select=*,DsmUsers(FullName)&StationId=eq.{encodedStationId}&Status=eq.Pending";
                response = await SendAuthenticatedGetAsync(fallbackUrl, settings.SupabaseApiKey);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var items = JsonConvert.DeserializeObject<List<dynamic>>(json) ?? new List<dynamic>();

                    var latestSettings = await _configService.GetSettingsAsync();
                    var latestStation = latestSettings.StationId?.Trim() ?? string.Empty;
                    if (!string.Equals(stationId, latestStation, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.Information("Discarding stale DSM poll response for station {OldStation} (active station is {NewStation}).", stationId, latestStation);
                        return new PollOutcome(IsSuccess: true, StatusCode: response.StatusCode, RetryAfter: null);
                    }

                    var locallyApprovedIds = await _configService.GetApprovedSubmissionIdsAsync();
                    var evaluated = DsmPendingSubmissionEvaluator.Evaluate(items, locallyApprovedIds, stationId);

                    ApplyAuthoritativeCountIfCurrent(evaluated.Count, stationId, requestVersion);
                    return new PollOutcome(IsSuccess: true, StatusCode: response.StatusCode, RetryAfter: null);
                }

                TimeSpan? retryAfter = null;
                if (response.Headers.RetryAfter != null)
                {
                    if (response.Headers.RetryAfter.Delta.HasValue)
                    {
                        retryAfter = response.Headers.RetryAfter.Delta.Value;
                    }
                    else if (response.Headers.RetryAfter.Date.HasValue)
                    {
                        var delta = response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow;
                        if (delta > TimeSpan.Zero) retryAfter = delta;
                    }
                }

                var error = await response.Content.ReadAsStringAsync();
                _logger.Warning("Supabase DSM polling request returned HTTP {StatusCode}: {Error}", response.StatusCode, error);
                ApplyFailureIfCurrent($"HTTP {(int)response.StatusCode}: Unable to fetch pending submissions", requestVersion);

                return new PollOutcome(IsSuccess: false, StatusCode: response.StatusCode, RetryAfter: retryAfter);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Transient exception occurred during DSM submission polling");
            ApplyFailureIfCurrent(ex.Message, requestVersion);
            return new PollOutcome(IsSuccess: false, StatusCode: null, RetryAfter: null);
        }
    }

    private async Task<HttpResponseMessage> SendAuthenticatedGetAsync(string url, string apiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("apikey", apiKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return await _client.SendAsync(request);
    }

    private TimeSpan CalculateNextDelay(PollOutcome outcome)
    {
        int jitterMs = Random.Shared.Next(0, 4001);

        if (outcome.IsSuccess)
        {
            _consecutiveFailures = 0;
            return BaseInterval + TimeSpan.FromMilliseconds(jitterMs);
        }

        if (outcome.StatusCode == (HttpStatusCode)429)
        {
            _consecutiveFailures++;
            var delay = outcome.RetryAfter ?? TimeSpan.FromSeconds(60);
            if (delay < TimeSpan.FromSeconds(10)) delay = TimeSpan.FromSeconds(10);
            if (delay > TimeSpan.FromSeconds(300)) delay = TimeSpan.FromSeconds(300);
            _logger.Warning("DSM polling rate limited (HTTP 429). Backing off for {Seconds}s + jitter.", delay.TotalSeconds);
            return delay + TimeSpan.FromMilliseconds(jitterMs);
        }

        _consecutiveFailures++;
        int factor = 1 << Math.Min(_consecutiveFailures, 3);
        var backoffSeconds = Math.Min(MaxBackoff.TotalSeconds, BaseInterval.TotalSeconds * factor / 2);
        var backoff = TimeSpan.FromSeconds(backoffSeconds) + TimeSpan.FromMilliseconds(jitterMs);
        _logger.Warning("DSM polling failed ({Failures} consecutive). Backing off for {Seconds}s.", _consecutiveFailures, backoff.TotalSeconds);
        return backoff;
    }

    private void ApplyAuthoritativeCountIfCurrent(int count, string? stationId, long requestVersion)
    {
        bool countChanged = false;
        bool stateChanged = false;

        lock (_stateLock)
        {
            if (_isDisposed) return;
            if (requestVersion < _appliedStateVersion)
            {
                _logger.Debug("Discarding out-of-order poll result (request v{Req} < applied v{Applied}).", requestVersion, _appliedStateVersion);
                return;
            }
            if (stationId != null && _activeStationId != null &&
                !string.Equals(stationId, _activeStationId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Debug("Discarding stale poll result for station {ReqStation} (active {Active}).", stationId, _activeStationId);
                return;
            }

            _appliedStateVersion = requestVersion;

            if (!_hasAuthoritativeCount || _lastError != null)
            {
                _hasAuthoritativeCount = true;
                _lastError = null;
                stateChanged = true;
            }

            if (_lastPendingCount != count)
            {
                _lastPendingCount = count;
                countChanged = true;
                stateChanged = true;
                _logger.Information("Pending DSM submission count updated: {Count}", count);
            }
        }

        if (countChanged) PendingCountChanged?.Invoke(count);
        if (stateChanged) PollStateChanged?.Invoke();
    }

    private void ApplyFailureIfCurrent(string? errorMessage, long requestVersion)
    {
        bool countChanged = false;
        bool stateChanged = false;
        var cleanError = string.IsNullOrWhiteSpace(errorMessage)
            ? "DSM polling failed."
            : errorMessage.Trim();

        lock (_stateLock)
        {
            if (_isDisposed) return;
            if (requestVersion < _appliedStateVersion)
                return;

            _appliedStateVersion = requestVersion;

            if (_hasAuthoritativeCount || _lastError != cleanError)
            {
                _hasAuthoritativeCount = false;
                _lastError = cleanError;
                stateChanged = true;
            }

            if (_lastPendingCount != 0)
            {
                _lastPendingCount = 0;
                countChanged = true;
                stateChanged = true;
            }
        }

        if (countChanged) PendingCountChanged?.Invoke(0);
        if (stateChanged) PollStateChanged?.Invoke();
    }

    private sealed record PollOutcome(bool IsSuccess, HttpStatusCode? StatusCode, TimeSpan? RetryAfter);
}
