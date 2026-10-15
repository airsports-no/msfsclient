using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.Tracking;
using msfsclient.Core.Training;

namespace msfsclient.Core.Hud;

/// <summary>How a status line is shown (the dock colours it, the HUD styles it, simulator messages pick a colour).</summary>
public enum StatusTone { Title, Live, Muted, Small, Warning, Error }

public sealed record StatusLine(string Text, StatusTone Tone);

/// <summary>
/// The state of the flight in progress, worked out once from the app state, the tracking session and the simulator, for
/// everything that shows it: the dock, the in-sim HUD and the simulator messages (port of FlightDeck's status logic).
/// </summary>
public sealed class FlightStatus(UiState state, TrackingState tracking, bool simConnected, long? lastSimPositionMillis, long nowMillis)
{
    public UiState State { get; } = state;
    public TrackingState Tracking { get; } = tracking;
    public long NowMillis { get; } = nowMillis;

    public bool CanStartTracking => !string.IsNullOrWhiteSpace(State.Person?.SimulatorTrackingId);

    public SimStatus Sim { get; } = SimStatus.Of(simConnected, lastSimPositionMillis, nowMillis);

    public ScoringStatus Scoring => ServerStatus.Scoring(State.ActiveContestants);

    public ReceivingStatus Receiving
    {
        get
        {
            var active = State.ActiveContestants;
            return ReceivingStatus.Of(State.Tasks.Count > 0, ServerStatus.LastHeardIso(active), NowMillis,
                Scoring is ScoringStatus.Running or ScoringStatus.Finished, ServerStatus.MapDelayMinutes(active));
        }
    }

    public long? WindowClosedMinutes => WindowClosed.MinutesAgo(
        Tracking.Running,
        State.ActiveContestants.Select(c => IsoTime.EpochMillisOrNull(c.FinishedByTime)).Where(e => e != null).Select(e => e!.Value).ToList(),
        State.LastWindowEndMillis,
        State.Upcoming.Select(f => IsoTime.EpochMillisOrNull(f.Flight?.TrackerStartTime)).Where(e => e != null).Select(e => e!.Value).ToList(),
        NowMillis);

    /// <summary>Minutes tracking has been on when that is long enough to warn about (4 h); null otherwise.</summary>
    public long? LongTrackingMinutes => Tracking.Running && Tracking.StartedAtMillis is { } started && NowMillis - started >= TrackingSession.WarnAfterMillis
        ? (NowMillis - started) / 60_000
        : null;

    public bool TrainingOn => State.TrainingMode && GateFeedback.TrainingAvailable(State.Tasks);

    public GateFeedback? Gate => GateFeedback.Latest(State.ActiveContestants);

    /// <summary>The flight being tracked ("Contest - Task"), as named in the stop confirmation.</summary>
    public string? FlightLabel
    {
        get
        {
            var task = State.Tasks.FirstOrDefault();
            if (task == null) return null;
            var c = task.ActiveContestants.FirstOrDefault();
            var contest = (c?.ContestId is { } id ? State.Contests.GetValueOrDefault(id)?.Name : null) ?? c?.ContestName;
            var label = string.Join(" - ", new[] { contest, task.NavigationTask.Name }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return label.Length == 0 ? null : label;
        }
    }

    /// <summary>
    /// The status lines under the play/stop button, in order. <paramref name="compact"/> (the Timer and Photos pages, the HUD)
    /// shows only trouble with the server, so a pilot there still sees that positions are not getting through.
    /// </summary>
    public IReadOnlyList<StatusLine> Lines(bool compact)
    {
        var lines = new List<StatusLine>();
        if (!Tracking.Running)
        {
            lines.Add(new StatusLine("Tracking is off", StatusTone.Title));
            lines.Add(new StatusLine(CanStartTracking ? "Press play to share your position." : "Your profile has no simulator tracking id yet. Open the profile on the website.", StatusTone.Muted));
            if (Sim.IsProblem) lines.Add(new StatusLine(Sim.Text, StatusTone.Warning));
            return lines;
        }
        lines.Add(new StatusLine("Tracking is on", StatusTone.Live));
        if (Tracking.LastFixMillis == null && Sim is SimStatus.Receiving) lines.Add(new StatusLine("Waiting for the first position…", StatusTone.Muted));
        else lines.Add(new StatusLine(Sim.Text, Sim.IsProblem ? StatusTone.Error : StatusTone.Muted));
        if (WindowClosedMinutes is { } closed)
            lines.Add(new StatusLine($"The tracking window closed {closed} min ago and tracking is still on. Stop tracking if you have landed.", StatusTone.Warning));
        if (LongTrackingMinutes is { } minutes)
            lines.Add(new StatusLine($"Tracking has been on for {minutes / 60} h {minutes % 60} min. It stops by itself after {TrackingSession.MaxMillis / 3_600_000} h.", StatusTone.Warning));
        if (Tracking.LastError is { } error) lines.Add(new StatusLine($"Upload problem: {error} (retrying)", StatusTone.Error));
        var receiving = Receiving;
        if (compact)
        {
            if (receiving is ReceivingStatus.WaitingForFirstPosition) lines.Add(new StatusLine("Server has not received a position yet", StatusTone.Error));
            else if (receiving is ReceivingStatus.Stale stale) lines.Add(new StatusLine($"No position received for {stale.SecondsAgo} s", StatusTone.Error));
            return lines;
        }
        switch (receiving)
        {
            case ReceivingStatus.WaitingForFirstPosition:
                lines.Add(new StatusLine("Server has not received a position yet", StatusTone.Error));
                break;
            case ReceivingStatus.Receiving r:
                lines.Add(new StatusLine($"Server receiving: last position {r.SecondsAgo} s ago", StatusTone.Muted));
                if (r.MapDelayMinutes > 0) lines.Add(new StatusLine($"The live map is delayed by {r.MapDelayMinutes} min in this task.", StatusTone.Small));
                break;
            case ReceivingStatus.ReceivingTimeNotYetKnown:
                lines.Add(new StatusLine("Server receiving positions", StatusTone.Muted));
                break;
            case ReceivingStatus.Stale s:
                lines.Add(new StatusLine($"No position received for {s.SecondsAgo} s", StatusTone.Error));
                break;
        }
        var scoring = Scoring;
        if (scoring == ScoringStatus.Running) lines.Add(new StatusLine("Scoring is running", StatusTone.Live));
        else if (scoring == ScoringStatus.Finished) lines.Add(new StatusLine("Scoring has finished", StatusTone.Muted));
        // Only worth mentioning once positions arrive: the first one starts the calculator.
        else if (scoring == ScoringStatus.NotStarted && receiving is ReceivingStatus.Receiving) lines.Add(new StatusLine("Scoring is starting…", StatusTone.Muted));
        lines.Add(new StatusLine($"Queued positions: {Tracking.Backlog}", StatusTone.Small));
        return lines;
    }
}
