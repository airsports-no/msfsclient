using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace msfsclient.Core.Api;

/// <summary>Links and joins from api/Models.kt and ui/WebScreen.kt.</summary>
public static partial class Urls
{
    private const string Site = "https://airsports.no";

    /// <summary>The web app's host; pages open there so the session cookie lands where the React app runs.</summary>
    public const string AppHost = "app.airsports.no";

    /// <summary>The website's dashboard: contests, results and everything an organizer does.</summary>
    public const string DashboardUrl = "https://app.airsports.no/";

    /// <summary>The one page that holds both the terms and the privacy policy.</summary>
    public const string TermsAndPrivacyUrl = "https://airsports.no/terms_and_conditions/";

    /// <summary>Image fields come back relative ("/media/x.png") from some storage backends; make them loadable.</summary>
    public static string? AbsoluteUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ? null : url.StartsWith('/') ? Site + url : url;

    /// <summary>The contest's page on the website: its tasks, results and how to take part.</summary>
    public static string ContestPagePath(int contestId) => $"/mission-dashboard/{contestId}";

    /// <summary>The competition map page for a task with one contestant selected (the website's <c>contestantIds</c> parameter).</summary>
    public static string CompetitionMapPath(int contestId, int navigationTaskId, int contestantId) =>
        $"/competition-map/{contestId}/{navigationTaskId}?contestantIds={contestantId}";

    [GeneratedRegex("^https?://([^/?#]+)([^#]*)")]
    private static partial Regex AbsoluteLink();

    /// <summary>
    /// The in-site path of a link that should open in the embedded web view: relative paths as they are, absolute links on
    /// airsports.no (any subdomain) reduced to their path and query. Anything else is not ours to open here: null.
    /// </summary>
    public static string? WebPath(string link)
    {
        // "//host/x" and "/\host/x" are links to another site, not paths on ours.
        if (link.StartsWith('/')) return link.StartsWith("//", StringComparison.Ordinal) || link.StartsWith("/\\", StringComparison.Ordinal) ? null : link;
        var match = AbsoluteLink().Match(link);
        if (!match.Success) return null;
        var host = match.Groups[1].Value.Split(':')[0].ToLowerInvariant();
        if (host != "airsports.no" && !host.EndsWith(".airsports.no", StringComparison.Ordinal)) return null;
        var rest = match.Groups[2].Value;
        return rest.Length == 0 ? "/" : rest;
    }

    /// <summary>The venue as (latitude, longitude), or null when the organizer left it blank or it is the 0,0 placeholder.</summary>
    public static (double Latitude, double Longitude)? VenueCoordinates(string? location)
    {
        if (location == null) return null;
        var parts = location.Split(',').Select(p => p.Trim()).ToArray();
        if (parts.Length < 2) return null;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) return null;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return null;
        return lat == 0.0 && lon == 0.0 ? null : (lat, lon);
    }

    /// <summary>A path on the site made into a full address (links that leave the app: share, browser).</summary>
    public static string SiteUrl(string pathOrUrl) => pathOrUrl.StartsWith("http", StringComparison.Ordinal) ? pathOrUrl : SiteBase + pathOrUrl;

    /// <summary>The website the app's pages and links are on; a local server for testing (<c>--base-url</c>).</summary>
    public static string SiteBase { get; set; } = "https://" + AppHost;

    /// <summary>Percent-encodes everything except RFC 3986 unreserved characters, so a path like <c>/?tab=upcoming</c> survives as one query value.</summary>
    public static string EncodeQueryComponent(string value)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            var unreserved = c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '.' or '_' or '~';
            if (unreserved) sb.Append(c);
            else sb.Append('%').Append("0123456789ABCDEF"[b >> 4]).Append("0123456789ABCDEF"[b & 0xF]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Pages opened in the app ask the website to leave out its own navigation bar (the app has its own), and say that this client
    /// opens them: page loads inside a web view carry no X-App-Platform header, and the server needs to know that a flight booked
    /// there is a simulator flight (and to list only the tasks that accept simulator flights).
    /// </summary>
    public static string WithEmbedFlag(string path)
    {
        if (path.Contains("embed=app", StringComparison.Ordinal)) return path;
        // The flag belongs in the query, before any #fragment (which carries data for the page and is not sent to the server).
        var hash = path.IndexOf('#');
        var (beforeFragment, fragment) = hash >= 0 ? (path[..hash], path[hash..]) : (path, "");
        return beforeFragment + (beforeFragment.Contains('?') ? "&" : "?") + "embed=app&client=msfs" + fragment;
    }

    /// <summary>The address that signs the web view in (token in the Authorization header, never in the URL) and then opens <paramref name="path"/>.</summary>
    public static string FirebaseLoginUrl(string path) => $"{SiteBase}/firebase_login/?next={EncodeQueryComponent(WithEmbedFlag(path))}";

    /// <summary>Google Maps directions to the venue (same link the website's registration page uses).</summary>
    public static string DirectionsUrl(double latitude, double longitude) =>
        string.Create(CultureInfo.InvariantCulture, $"https://www.google.com/maps/dir/?api=1&destination={latitude},{longitude}");

    /// <summary>The contest as the server described it with the flight, for contests the pilot cannot look up in the contest list.</summary>
    public static ContestSummary? ContestFromMetadata(
        int id, int taskId, string? taskName, string? name, TaskType? taskType, string? logo, string? headerImage,
        string? location, string? country, string? website) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : new ContestSummary
            {
                Id = id, Name = name, NavigationTasks = [new TaskSummary { Pk = taskId, Name = taskName ?? "", TaskType = taskType }],
                Logo = logo, HeaderImage = headerImage, Website = website, Location = location, Country = country,
            };

    public static ContestSummary? MetadataContest(this Flight f) =>
        ContestFromMetadata(f.ContestId, f.NavigationTask, f.NavigationTaskName, f.ContestName, f.TaskType, f.ContestLogo,
            f.ContestHeaderImage, f.ContestLocation, f.ContestCountry, f.ContestWebsite);

    public static IReadOnlyList<MyFlight> JoinFlights(IEnumerable<Flight> flights, IEnumerable<ContestSummary> contests)
    {
        var byId = new Dictionary<int, ContestSummary>();
        foreach (var c in contests) byId.TryAdd(c.Id, c);
        return flights.Select(f =>
        {
            // The contest list only has contests the pilot may view; the flight itself carries the metadata of the others.
            var contest = byId.GetValueOrDefault(f.ContestId) ?? f.MetadataContest();
            var task = contest?.NavigationTasks.FirstOrDefault(t => t.Pk == f.NavigationTask)
                ?? f.MetadataContest()?.NavigationTasks.FirstOrDefault();
            return new MyFlight
            {
                Id = f.Id,
                ContestName = NotBlank(contest?.Name) ?? $"Contest {f.ContestId}",
                TaskName = NotBlank(task?.Name) ?? NotBlank(f.NavigationTaskName) ?? $"Task {f.NavigationTask}",
                TakeoffTime = f.TakeoffTime,
                // The live/results map for this pilot's own flight (default_map_url is the printable route map, not this).
                MapLink = CompetitionMapPath(f.ContestId, f.NavigationTask, f.Id),
                Contest = contest,
                Task = task,
                Flight = f,
            };
        }).ToList();
    }

    internal static string? NotBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
