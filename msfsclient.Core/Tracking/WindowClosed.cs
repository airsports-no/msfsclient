using msfsclient.Core.Api;

namespace msfsclient.Core.Tracking;

public static class WindowClosed
{
    /// <summary>Tracking is left on this long after the flight's window closed before the pilot is warned (time to land and taxi in).</summary>
    public const long GraceMinutes = 10;

    /// <summary>The latest finish time among the flights that are open now, in epoch milliseconds; null when there are none.</summary>
    public static long? LatestWindowEnd(IEnumerable<CurrentTask> tasks)
    {
        var ends = tasks.SelectMany(t => t.ActiveContestants).Select(c => IsoTime.EpochMillisOrNull(c.FinishedByTime)).Where(e => e != null).ToList();
        return ends.Count == 0 ? null : ends.Max();
    }

    /// <summary>
    /// How many minutes ago the flight's tracking window closed while tracking is still on, or null when no warning is due.
    /// The flights open now come from the server only while their window is open, so <paramref name="lastWindowEnd"/>
    /// remembers the window that has just closed. No warning when another flight is open or opens within the hour, or when
    /// the window closed less than <see cref="GraceMinutes"/> ago.
    /// </summary>
    public static long? MinutesAgo(bool running, IReadOnlyCollection<long> activeEnds, long? lastWindowEnd, IReadOnlyCollection<long> nextWindowOpens, long nowMillis)
    {
        if (!running) return null;
        if (activeEnds.Any(e => e > nowMillis)) return null;
        var candidates = activeEnds.Concat(lastWindowEnd is { } l ? [l] : Array.Empty<long>()).ToList();
        if (candidates.Count == 0) return null;
        var end = candidates.Max();
        if (nextWindowOpens.Any(o => o <= nowMillis + 60 * 60_000L)) return null;
        var minutes = (nowMillis - end) / 60_000L;
        return minutes >= GraceMinutes ? minutes : null;
    }
}
