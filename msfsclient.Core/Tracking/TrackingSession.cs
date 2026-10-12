using msfsclient.Core.History;

namespace msfsclient.Core.Tracking;

public sealed record TrackingState
{
    public bool Running { get; init; }
    public long Backlog { get; init; }
    public long? LastFixMillis { get; init; }
    public long? LastUploadMillis { get; init; }
    public string? LastError { get; init; }
    /// <summary>Reference time minus PC clock reported with the latest fix; null until a fix reports one.</summary>
    public long? ClockOffsetMillis { get; init; }
    /// <summary>PC-clock time tracking was started, to tell "still waiting for the first position" from "positions stopped".</summary>
    public long? StartedAtMillis { get; init; }
    public double? LastAccuracyMeters { get; init; }
    /// <summary>Position of the latest fix, for stamping photo observations.</summary>
    public double? LastLatitude { get; init; }
    public double? LastLongitude { get; init; }
    public double? LastSpeedMetersPerSecond { get; init; }
    /// <summary>Positions received since tracking started (the debug view shows the rate).</summary>
    public long FixCount { get; init; }
    /// <summary>Tracking was stopped by the app itself after <see cref="TrackingSession.MaxMillis"/>; reset when tracking starts again.</summary>
    public bool AutoStopped { get; init; }
}

/// <summary>
/// Glue between the simulator, the durable queue and Traccar (port of tracking/TrackingSession.kt). Every fix is persisted
/// first; a separate uploader loop drains the queue and backs off (5 s doubling to 5 min) while the network is down, so
/// tracking never blocks on connectivity and nothing is lost if the process dies.
/// </summary>
public sealed class TrackingSession
{
    /// <summary>After this long the pilot is warned that tracking has been on for a long time.</summary>
    public const long WarnAfterMillis = 4L * 60 * 60 * 1000;

    /// <summary>Tracking stops by itself after this long (tracking left on by mistake).</summary>
    public const long MaxMillis = 12L * 60 * 60 * 1000;

    private readonly IPositionSource _source;
    private readonly PositionQueue _queue;
    private readonly TraccarUploader _uploader;
    private readonly Func<long> _now;
    private readonly long _initialBackoffMs;
    private readonly long _maxBackoffMs;
    private readonly long _maxDurationMillis;
    private readonly TrackLog? _trackLog;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _wake = new(0, 1);

    private TrackingState _state;
    private CancellationTokenSource? _cts;
    private long? _logSessionId;

    public TrackingSession(
        IPositionSource source, PositionQueue queue, TraccarUploader uploader, string deviceId, Func<long>? now = null,
        long initialBackoffMs = 5_000, long maxBackoffMs = 300_000, long maxDurationMillis = MaxMillis, TrackLog? trackLog = null)
    {
        _source = source;
        _queue = queue;
        _uploader = uploader;
        DeviceId = deviceId;
        _now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _initialBackoffMs = initialBackoffMs;
        _maxBackoffMs = maxBackoffMs;
        _maxDurationMillis = maxDurationMillis;
        _trackLog = trackLog;
        _state = new TrackingState { Backlog = queue.Size() };
    }

    public string DeviceId { get; }

    public TrackingState State { get { lock (_lock) return _state; } }

    /// <summary>Raised on any thread whenever <see cref="State"/> changes.</summary>
    public event Action<TrackingState>? StateChanged;

    public void Start()
    {
        CancellationToken ct;
        lock (_lock)
        {
            if (_state.Running) return;
            _cts = new CancellationTokenSource();
            ct = _cts.Token;
            _state = _state with
            {
                Running = true, Backlog = _queue.Size(), StartedAtMillis = _now(), LastFixMillis = null, LastAccuracyMeters = null,
                AutoStopped = false, FixCount = 0,
            };
            try { _logSessionId = _trackLog?.StartSession(DeviceId, _now()); } catch (Exception) { _logSessionId = null; }
        }
        Publish();
        _source.PositionReceived += OnPosition;
        _ = Task.Run(() => DrainLoopAsync(ct), CancellationToken.None);
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromMilliseconds(_maxDurationMillis), ct); }
            catch (OperationCanceledException) { return; }
            Finish(auto: true);
        }, CancellationToken.None);
        Wake(); // leftovers from a previous session
    }

    public void Stop() => Finish(auto: false);

    private void Finish(bool auto)
    {
        lock (_lock)
        {
            if (!_state.Running) return;
            _source.PositionReceived -= OnPosition;
            _cts?.Cancel();
            _cts = null;
            if (_logSessionId is { } id)
            {
                try { _trackLog?.EndSession(id, _now()); } catch (Exception) { /* the local copy only */ }
            }
            _logSessionId = null;
            _state = _state with { Running = false, AutoStopped = auto };
        }
        Publish();
    }

    private void OnPosition(TrackPoint point)
    {
        lock (_lock)
        {
            if (!_state.Running) return;
            // Recording must never get in the way of tracking: a full disk only loses the local copy.
            if (_logSessionId is { } id)
            {
                try { _trackLog?.Append(id, point); } catch (Exception) { }
            }
            _queue.Enqueue(DeviceId, point);
            _state = _state with
            {
                LastFixMillis = point.TimestampMillis,
                LastAccuracyMeters = point.Accuracy,
                LastLatitude = point.Latitude,
                LastLongitude = point.Longitude,
                LastSpeedMetersPerSecond = point.SpeedMetersPerSecond,
                Backlog = _queue.Size(),
                ClockOffsetMillis = point.ClockOffsetMillis ?? _state.ClockOffsetMillis,
                FixCount = _state.FixCount + 1,
            };
        }
        Publish();
        Wake();
    }

    private void Wake()
    {
        // Conflated: one pending wake-up is enough however many fixes arrive meanwhile.
        try { if (_wake.CurrentCount == 0) _wake.Release(); }
        catch (SemaphoreFullException) { }
    }

    private async Task DrainLoopAsync(CancellationToken ct)
    {
        var backoff = _initialBackoffMs;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _wake.WaitAsync(ct);
                while (true)
                {
                    var result = await _uploader.FlushAsync(ct);
                    var sentAny = result is FlushResult.Done { Sent: > 0 } or FlushResult.Failed { Sent: > 0 };
                    if (result is FlushResult.Done)
                    {
                        backoff = _initialBackoffMs;
                        Update(s => s with { Backlog = _queue.Size(), LastError = null, LastUploadMillis = sentAny ? _now() : s.LastUploadMillis });
                        break;
                    }
                    var failed = (FlushResult.Failed)result;
                    Update(s => s with
                    {
                        Backlog = _queue.Size(), LastError = failed.Cause ?? "Server rejected position",
                        LastUploadMillis = sentAny ? _now() : s.LastUploadMillis,
                    });
                    await Task.Delay(TimeSpan.FromMilliseconds(backoff), ct);
                    backoff = Math.Min(backoff * 2, _maxBackoffMs);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Update(Func<TrackingState, TrackingState> change)
    {
        lock (_lock) _state = change(_state);
        Publish();
    }

    private void Publish() => StateChanged?.Invoke(State);
}
