using msfsclient.Core.App;
using msfsclient.Core.Tracking;
using msfsclient.Core.Training;

namespace msfsclient.Core.Hud;

/// <summary>How a message in the simulator looks: good news green, warnings red or yellow, the rest white.</summary>
public enum SimMessageKind { Info, Good, Warning, Alert }

/// <summary>A short line printed on the simulator's screen (SimConnect text) for <see cref="Seconds"/>.</summary>
public sealed record SimMessage(string Text, SimMessageKind Kind, float Seconds = 6);

/// <summary>
/// Decides which events are worth a line on the simulator's screen. Fed the flight status every second, it reports each event
/// once, when it happens (never a repeated status): tracking on and off, the server receiving or losing positions, the window
/// closing, training gate results, the start minute, new announcements. The first call only notes the starting point.
/// </summary>
public sealed class SimMessageTracker
{
    private bool _started;
    private bool _running;
    private bool _autoStopped;
    private bool _receiving;
    private bool _stale;
    private bool _scoring;
    private bool _windowClosed;
    private bool _longTracking;
    private string? _gateKey;
    private long? _timerOrigin;
    private long _lastTimerElapsed = long.MinValue;
    private readonly HashSet<int> _announcements = [];

    public IReadOnlyList<SimMessage> Next(FlightStatus status)
    {
        var messages = new List<SimMessage>();
        var s = status.State;
        var t = status.Tracking;
        var receiving = status.Receiving is ReceivingStatus.Receiving or ReceivingStatus.ReceivingTimeNotYetKnown;
        var stale = status.Receiving is ReceivingStatus.Stale;
        var scoring = status.Scoring == ScoringStatus.Running;
        var windowClosed = status.WindowClosedMinutes != null;
        var longTracking = status.LongTrackingMinutes != null;
        var gate = status.TrainingOn && t.Running ? status.Gate : null;
        var gateKey = gate == null ? null : $"{gate.Gate}|{gate.OffsetSeconds}";
        var newAnnouncements = s.Announcements.Where(a => _announcements.Add(a.Id)).ToList();

        if (_started)
        {
            if (t.Running && !_running) messages.Add(new SimMessage(status.FlightLabel is { } f ? $"Airsports: tracking started for {f}" : "Airsports: tracking started", SimMessageKind.Good));
            if (!t.Running && _running)
            {
                messages.Add(t.AutoStopped && !_autoStopped
                    ? new SimMessage("Airsports: tracking was stopped automatically after 12 hours", SimMessageKind.Alert, 10)
                    : new SimMessage("Airsports: tracking stopped", SimMessageKind.Info));
            }
            if (t.Running)
            {
                if (receiving && !_receiving && !_stale) messages.Add(new SimMessage("Airsports: the server is receiving your positions", SimMessageKind.Good));
                if (stale && !_stale) messages.Add(new SimMessage($"Airsports: no position received for {((ReceivingStatus.Stale)status.Receiving).SecondsAgo} s", SimMessageKind.Alert, 10));
                if (receiving && _stale) messages.Add(new SimMessage("Airsports: positions are getting through again", SimMessageKind.Good));
                if (scoring && !_scoring) messages.Add(new SimMessage("Airsports: scoring is running", SimMessageKind.Good));
                if (windowClosed && !_windowClosed)
                    messages.Add(new SimMessage("Airsports: the tracking window has closed. Stop tracking if you have landed.", SimMessageKind.Warning, 10));
                if (longTracking && !_longTracking)
                    messages.Add(new SimMessage($"Airsports: tracking has been on for 4 hours. It stops by itself after {TrackingSession.MaxMillis / 3_600_000} hours.", SimMessageKind.Warning, 10));
            }
            if (gate != null && gateKey != _gateKey)
                messages.Add(new SimMessage($"{gate.Gate}: {gate.Text}", gate.Timing == GateTiming.OnTime ? SimMessageKind.Good : SimMessageKind.Warning, 8));
            foreach (var a in newAnnouncements)
                messages.Add(new SimMessage($"{(string.IsNullOrWhiteSpace(a.ContestName) ? "Organizers" : a.ContestName)}: {a.Message}", SimMessageKind.Info, 12));
            messages.AddRange(TimerMessages(s, status.NowMillis));
        }

        _started = true;
        _running = t.Running;
        _autoStopped = t.AutoStopped;
        _receiving = receiving;
        _stale = stale;
        _scoring = scoring;
        _windowClosed = windowClosed;
        _longTracking = longTracking;
        _gateKey = gateKey;
        if (s.TimerOriginMillis is { } origin) _lastTimerElapsed = status.NowMillis - origin;
        _timerOrigin = s.TimerOriginMillis;
        return messages;
    }

    /// <summary>One minute before the start minute, and the start minute itself (the pilot is looking at the sim, not the timer).</summary>
    private IEnumerable<SimMessage> TimerMessages(UiState s, long now)
    {
        if (s.TimerOriginMillis is not { } origin) yield break;
        if (origin != _timerOrigin)
        {
            yield return new SimMessage($"Airsports: timer started, {AdaptiveStartTimer.Format(now - origin)}", SimMessageKind.Info, 4);
            yield break;
        }
        var elapsed = now - origin;
        if (_lastTimerElapsed < -60_000 && elapsed >= -60_000 && elapsed < 0) yield return new SimMessage("Airsports: one minute to your start minute", SimMessageKind.Warning, 5);
        if (_lastTimerElapsed < 0 && elapsed >= 0) yield return new SimMessage("Airsports: start minute now", SimMessageKind.Good, 5);
    }
}
