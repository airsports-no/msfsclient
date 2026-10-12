using msfsclient.Core.Api;

namespace msfsclient.Core.Tracking;

/// <summary>What the pilot should be told about the server having received their positions for the active task.</summary>
public abstract record ReceivingStatus
{
    /// <summary>Not scheduled in a task right now, so positions are only recorded as casual tracking.</summary>
    public sealed record NoActiveTask : ReceivingStatus;
    /// <summary>Scheduled, but the server has not received any position yet.</summary>
    public sealed record WaitingForFirstPosition : ReceivingStatus;
    /// <summary><see cref="MapDelayMinutes"/> &gt; 0: the organizer delays the live map, so the pilot is not surprised that it shows less.</summary>
    public sealed record Receiving(long SecondsAgo, int MapDelayMinutes = 0) : ReceivingStatus;
    /// <summary>Scoring is running, so the server certainly has positions, but their time has not been stored yet.</summary>
    public sealed record ReceivingTimeNotYetKnown : ReceivingStatus;
    /// <summary>The last position is old: the simulator, the connection or the app is not delivering.</summary>
    public sealed record Stale(long SecondsAgo) : ReceivingStatus;

    /// <summary>
    /// The server stores received positions in batches (at most every 10 s) and the app polls every 10 s, so a healthy flight
    /// routinely shows a last position up to ~20 s old; the warning must stay clear of that.
    /// </summary>
    public const long StaleAfterSeconds = 45;

    public static ReceivingStatus Of(bool hasActiveTask, string? lastPositionIso, long nowMillis, bool scoringConfirmsPositions = false, int mapDelayMinutes = 0)
    {
        if (!hasActiveTask) return new NoActiveTask();
        var last = IsoTime.EpochMillisOrNull(lastPositionIso);
        if (last == null) return scoringConfirmsPositions ? new ReceivingTimeNotYetKnown() : new WaitingForFirstPosition();
        var ago = Math.Max(0, (nowMillis - last.Value) / 1000);
        return ago > StaleAfterSeconds ? new Stale(ago) : new Receiving(ago, mapDelayMinutes);
    }
}

/// <summary>Whether the server-side live calculator that scores the flight is running.</summary>
public enum ScoringStatus
{
    /// <summary>The backend did not say (older server), so the app claims nothing.</summary>
    Unknown,
    /// <summary>Scheduled, but no calculator is running yet - it is started by the first received position.</summary>
    NotStarted,
    Running,
    Finished,
}

public static class ServerStatus
{
    public static ScoringStatus Scoring(IReadOnlyCollection<ActiveContestant> contestants)
    {
        if (contestants.Count == 0) return ScoringStatus.Unknown;
        if (contestants.Any(c => c.CalculatorRunning == true)) return ScoringStatus.Running;
        if (contestants.All(c => c.CalculatorFinished == true)) return ScoringStatus.Finished;
        if (contestants.Any(c => c.CalculatorRunning == false)) return ScoringStatus.NotStarted;
        return ScoringStatus.Unknown;
    }

    /// <summary>
    /// When the server last heard from this client. Prefers <c>last_received_time</c>, which is not delayed. The stored
    /// <c>last_position_time</c> is only trusted when the task has no calculation delay: with a delay it lags real
    /// reception by that long and would make a healthy flight look stale.
    /// </summary>
    public static string? LastHeardIso(IReadOnlyCollection<ActiveContestant> contestants) =>
        MaxIso(contestants.Select(c => c.LastReceivedTime))
        ?? MaxIso(contestants.Where(c => (c.CalculationDelayMinutes ?? 0.0) <= 0.0).Select(c => c.LastPositionTime));

    /// <summary>The largest map delay (whole minutes, rounded up) among the pilot's active contestants; 0 for none.</summary>
    public static int MapDelayMinutes(IReadOnlyCollection<ActiveContestant> contestants) =>
        (int)Math.Ceiling(contestants.Count == 0 ? 0.0 : contestants.Max(c => c.CalculationDelayMinutes ?? 0.0));

    private static string? MaxIso(IEnumerable<string?> values) => values.Where(v => v != null).Max(StringComparer.Ordinal);
}
