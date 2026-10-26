using msfsclient.Core.Api;

namespace msfsclient.Core.App;

/// <summary>The search of the past flights (app/FlightFilter.kt).</summary>
public static class FlightFilter
{
    /// <summary>
    /// The flights that match what the pilot typed: every word must appear (case ignored) in the contest name, task name, task
    /// type or country, so "elverum precision" finds the precision tasks of the Elverum contest. A blank query keeps everything.
    /// </summary>
    public static IReadOnlyList<MyFlight> Filter(IReadOnlyList<MyFlight> flights, string query)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return flights;
        return flights.Where(f =>
        {
            var haystack = string.Join("\n", new[] { f.ContestName, f.TaskName, f.Task?.TaskType?.DisplayName, f.Contest?.Country }.Where(t => t != null));
            return words.All(w => haystack.Contains(w, StringComparison.OrdinalIgnoreCase));
        }).ToList();
    }
}
