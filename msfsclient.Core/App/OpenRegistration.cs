using msfsclient.Core.Api;

namespace msfsclient.Core.App;

/// <summary>The tasks open for registration today at one contest (one venue).</summary>
public sealed record OpenEvent(
    int ContestId, string ContestName, string Country, string? TimeZone, double? Latitude, double? Longitude,
    IReadOnlyList<OpenRegistrationTask> Tasks,
    /// <summary>From the aircraft to the venue; null when the aircraft's position or the venue's is not known.</summary>
    double? DistanceKm)
{
    public string Place => string.Join("  ·  ", new[] { Country, DistanceKm is { } km ? $"{OpenRegistration.DistanceText(km)} away" : "" }.Where(x => !string.IsNullOrWhiteSpace(x)));
    public string? DirectionsUrl => Latitude is { } lat && Longitude is { } lon ? Urls.DirectionsUrl(lat, lon) : null;
}

public static class OpenRegistration
{
    /// <summary>Straight-line distance in kilometres between two points (haversine).</summary>
    public static double DistanceKm(Coordinates from, double latitude, double longitude)
    {
        static double Rad(double deg) => deg * Math.PI / 180;
        var dLat = Rad(latitude - from.Latitude);
        var dLon = Rad(longitude - from.Longitude);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(from.Latitude)) * Math.Cos(Rad(latitude)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * 6371.0 * Math.Asin(Math.Sqrt(a));
    }

    /// <summary>"850 m", "3.4 km", "120 km": a distance for a person who is looking for a place to go.</summary>
    public static string DistanceText(double km) => km switch
    {
        < 1 => $"{(int)(Math.Round(km * 100, MidpointRounding.ToEven) * 10)} m",
        < 10 => (Math.Round(km * 10, MidpointRounding.ToEven) / 10).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km",
        _ => $"{(int)Math.Round(km, MidpointRounding.ToEven)} km",
    };

    /// <summary>
    /// One entry per contest, in the order the server sent them (contest name, then start time). With the aircraft's position the
    /// nearest venue comes first and venues without a known position last; the sort is stable, so ties keep the server's order.
    /// The position is only used here on the PC: it is never sent to the server.
    /// </summary>
    public static IReadOnlyList<OpenEvent> Group(IReadOnlyList<OpenRegistrationTask> tasks, Coordinates? here)
    {
        var events = tasks.GroupBy(t => t.ContestId).Select(group =>
        {
            var first = group.First();
            double? km = here != null && first.Latitude is { } lat && first.Longitude is { } lon ? DistanceKm(here, lat, lon) : null;
            return new OpenEvent(group.Key, first.ContestName, first.Country, first.TimeZone, first.Latitude, first.Longitude, group.ToList(), km);
        }).ToList();
        return here == null ? events : events.OrderBy(e => e.DistanceKm ?? double.MaxValue).ToList();
    }

    /// <summary>The web page where the pilot registers a flight for this task (the form stays on the website).</summary>
    public static string RegisterPath(OpenRegistrationTask task) => $"/schedule-flight?contestId={task.ContestId}&navigationTaskId={task.NavigationTaskId}";
}
