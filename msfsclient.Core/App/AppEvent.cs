using System.Globalization;

namespace msfsclient.Core.App;

/// <summary>
/// Something a website page shown in the app's web view tells the app. The page navigates to the private
/// <c>airsportsapp://&lt;event&gt;?&lt;params&gt;</c> address; the web view intercepts it (it is never loaded).
/// </summary>
public abstract record AppEvent
{
    /// <summary>The pilot registered a flight: return to Home and refresh the upcoming flights.</summary>
    public sealed record FlightRegistered(int? ContestId, int? NavigationTaskId) : AppEvent;

    public const string Scheme = "airsportsapp";

    public static bool IsAppEventUrl(string url) => url.StartsWith(Scheme + "://", StringComparison.Ordinal);

    /// <summary>Null for anything that is not a known app event, so unknown events from newer pages are ignored safely.</summary>
    public static AppEvent? Parse(string url)
    {
        if (!IsAppEventUrl(url)) return null;
        var rest = url[(Scheme.Length + 3)..];
        var q = rest.IndexOf('?');
        var name = (q >= 0 ? rest[..q] : rest).TrimEnd('/');
        var parameters = new Dictionary<string, string>();
        if (q >= 0)
        {
            foreach (var pair in rest[(q + 1)..].Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0) parameters[pair[..eq]] = pair[(eq + 1)..];
            }
        }
        return name switch
        {
            "registered" => new FlightRegistered(Int(parameters.GetValueOrDefault("contestId")), Int(parameters.GetValueOrDefault("navigationTaskId"))),
            _ => null,
        };
    }

    private static int? Int(string? s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
}

/// <summary>A position on the map, for the few places that sort or filter by distance.</summary>
public sealed record Coordinates(double Latitude, double Longitude);

/// <summary>Where the pilot is: on this client, the simulator's aircraft. Null when it is not known.</summary>
public interface ICurrentLocation
{
    Coordinates? Current();
}

public static class OpenTodayPage
{
    /// <summary>
    /// The page listing the tasks open for registration today. With a position it is sent along in the address FRAGMENT
    /// (<c>#lat=..&amp;lon=..</c>), which the browser never sends to the server. Rounded to about 100 m.
    /// </summary>
    public static string Path(Coordinates? here) => here == null
        ? "/open-today"
        : string.Create(CultureInfo.InvariantCulture, $"/open-today#lat={Rounded(here.Latitude)}&lon={Rounded(here.Longitude)}");

    private static double Rounded(double value) => Math.Round(value * 1000, MidpointRounding.AwayFromZero) / 1000.0;
}
