using msfsclient.Core.Api;

namespace msfsclient.Core.Training;

/// <summary>
/// The training-mode countdown to the next gate (port of training/GateCountdown.kt): the next gate with a time check that the
/// pilot is shown (the server never sends a secret gate, unknown leg or curve point) and its required time. It is updated
/// whenever the server moves on to the next gate (the client polls every 5 s in training mode); with an adaptive start the
/// server only sends it once the starting point is crossed, with the gate times that crossing set.
/// </summary>
public sealed record GateCountdown(string Gate, long RequiredMillis)
{
    /// <summary>Milliseconds left until the required time; negative once it has passed.</summary>
    public long RemainingMillis(long nowMillis) => RequiredMillis - nowMillis;

    /// <summary>
    /// "1:05" while counting down (rounded up, so it shows "0:00" exactly at the required time) and "+0:04" once the required
    /// time has passed. Beyond an hour "1:02:05".
    /// </summary>
    public static string Format(long remainingMillis)
    {
        var late = remainingMillis < 0;
        var totalSeconds = late ? -remainingMillis / 1000 : (remainingMillis + 999) / 1000;
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        var sign = late ? "+" : "";
        return hours > 0 ? $"{sign}{hours}:{minutes:00}:{seconds:00}" : $"{sign}{minutes}:{seconds:00}";
    }

    /// <summary>The next gate to count down to among the active flights, or null when the server sent none (or an older server).</summary>
    public static GateCountdown? Next(IEnumerable<ActiveContestant> contestants) =>
        contestants
            .Select(c => c.TrainingFeedback is { NextGate: { } gate } f && IsoTime.EpochMillisOrNull(f.NextGateTime) is { } required
                ? new GateCountdown(gate, required)
                : null)
            .FirstOrDefault(g => g != null);

    /// <summary>
    /// The Timer page exists for an adaptive start (the start timer) and in training mode (the gate countdown). Training mode
    /// needs a flight the server supports it for.
    /// </summary>
    public static bool TimerAvailable(IEnumerable<CurrentTask> tasks, bool trainingMode)
    {
        var list = tasks.ToList();
        return list.Any(t => t.ActiveContestants.Any(c => c.AdaptiveStart)) || (trainingMode && GateFeedback.TrainingAvailable(list));
    }
}

/// <summary>Which of the Timer page's two timers is shown large; the other stays small as a reference.</summary>
public enum TimerFocus { Start, Gate }
