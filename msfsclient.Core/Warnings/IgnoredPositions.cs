namespace msfsclient.Core.Warnings;

using msfsclient.Core.Api;

/// <summary>
/// What to tell the pilot when the server ignores the simulator's positions for a current flight (the task does not accept
/// simulator flights, or the flight was booked as a real aircraft flight): one line per such flight, without repeats. Empty when
/// everything is used, and for an older backend that does not say.
/// </summary>
public static class IgnoredPositions
{
    public static IReadOnlyList<string> Notices(IEnumerable<CurrentTask> tasks) =>
        tasks.SelectMany(task => task.ActiveContestants)
            .Where(flight => flight.PositionsAccepted == false)
            .Select(flight => string.IsNullOrWhiteSpace(flight.PositionsIgnoredReason)
                ? "The positions from the simulator are not used for your flight."
                : flight.PositionsIgnoredReason!)
            .Distinct()
            .ToList();
}
