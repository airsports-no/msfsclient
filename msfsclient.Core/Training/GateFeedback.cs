using msfsclient.Core.Api;

namespace msfsclient.Core.Training;

/// <summary>How a gate crossing compares with its required time.</summary>
public enum GateTiming { OnTime, Early, Late }

/// <summary>One gate crossing as the pilot sees it: <see cref="OffsetSeconds"/> is crossing minus required time (positive: late).</summary>
public sealed record GateFeedback(string Gate, double OffsetSeconds)
{
    /// <summary>Within this many seconds of the required time counts as on time.</summary>
    public const double OnTimeSeconds = 2.0;

    public GateTiming Timing => Math.Abs(OffsetSeconds) <= OnTimeSeconds ? GateTiming.OnTime : OffsetSeconds < 0 ? GateTiming.Early : GateTiming.Late;

    /// <summary>"3 s late", "5 s early" or "on time".</summary>
    public string Text => Timing switch
    {
        GateTiming.OnTime => "on time",
        GateTiming.Early => $"{(long)Math.Round(Math.Abs(OffsetSeconds), MidpointRounding.AwayFromZero)} s early",
        _ => $"{(long)Math.Round(OffsetSeconds, MidpointRounding.AwayFromZero)} s late",
    };

    /// <summary>The latest crossing among the active flights, or null when the server sent none (no training feedback for this task).</summary>
    public static GateFeedback? Latest(IEnumerable<ActiveContestant> contestants) =>
        contestants
            .Select(c => c.TrainingFeedback is { LastGate: { } gate, LastGateTimeOffset: { } offset } ? new GateFeedback(gate, offset) : null)
            .FirstOrDefault(f => f != null);

    /// <summary>Training mode is offered only for flights the server says support it (live feedback allowed by the organizer, no calculation delay).</summary>
    public static bool TrainingAvailable(IEnumerable<CurrentTask> tasks) =>
        tasks.Any(t => t.ActiveContestants.Any(c => c.TrainingAvailable == true));
}
