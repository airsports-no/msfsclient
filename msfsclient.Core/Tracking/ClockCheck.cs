namespace msfsclient.Core.Tracking;

/// <summary>What the PC clock was compared against.</summary>
public enum ClockReference
{
    /// <summary>A reference time reported with the positions (not available from the simulator; kept for parity).</summary>
    Gps,
    /// <summary>Network time (NTP). The simulator client stamps positions with the PC clock, so this is the one that matters.</summary>
    Network,
}

/// <summary>
/// The PC clock against a reference. Positions are scored by the timestamps they carry, never by the server clock, so a
/// wrong PC clock moves every gate time.
/// </summary>
public abstract record ClockStatus
{
    /// <summary>Nothing to compare with yet.</summary>
    public sealed record Unknown : ClockStatus;
    public sealed record InSync(long OffsetMillis, ClockReference Reference = ClockReference.Gps) : ClockStatus;
    /// <summary><see cref="OffsetMillis"/> &gt; 0: PC clock is behind the reference; &lt; 0: ahead.</summary>
    public sealed record Drift(long OffsetMillis, ClockReference Reference = ClockReference.Gps) : ClockStatus
    {
        public long Seconds => Math.Abs(OffsetMillis) / 1000;
        public bool ClockIsAhead => OffsetMillis < 0;
    }
}

public static class ClockCheck
{
    /// <summary>Whole-second displays make smaller differences invisible, and measurement noise is well below this.</summary>
    public const long WarnAtMillis = 2_000;

    /// <summary>
    /// <paramref name="uncertaintyMillis"/> is the measurement's own error bar (for NTP, half the round trip): a difference
    /// only counts when it clearly exceeds it, so a slow network can never cause a false warning.
    /// </summary>
    public static ClockStatus Status(long? offsetMillis, ClockReference reference = ClockReference.Gps, long uncertaintyMillis = 0)
    {
        if (offsetMillis == null) return new ClockStatus.Unknown();
        var size = Math.Abs(offsetMillis.Value);
        return size >= WarnAtMillis && size > 2 * uncertaintyMillis
            ? new ClockStatus.Drift(offsetMillis.Value, reference)
            : new ClockStatus.InSync(offsetMillis.Value, reference);
    }

    /// <summary>The PC clock corrected to the reference; falls back to the PC clock while <paramref name="offsetMillis"/> is unknown.</summary>
    public static long ReferenceNow(long clockNowMillis, long? offsetMillis) => clockNowMillis + (offsetMillis ?? 0L);
}
