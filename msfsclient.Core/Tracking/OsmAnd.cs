using System.Globalization;

namespace msfsclient.Core.Tracking;

/// <summary>
/// Traccar OsmAnd protocol parameters. Timestamp is in <b>seconds</b> and speed in <b>knots</b>, which is what Traccar's
/// OsmAnd decoder expects. Numbers always use a dot (invariant culture), whatever the PC's regional settings.
/// </summary>
public static class OsmAnd
{
    public static IReadOnlyList<KeyValuePair<string, string>> Parameters(
        string deviceId, long timestampMillis, double latitude, double longitude, double speedKnots,
        double? bearing, double? altitude, double? accuracy, double? batteryPercent)
    {
        var list = new List<KeyValuePair<string, string>>
        {
            new("id", deviceId),
            new("timestamp", (timestampMillis / 1000).ToString(CultureInfo.InvariantCulture)),
            new("lat", Number(latitude)),
            new("lon", Number(longitude)),
            new("speed", Number(speedKnots)),
        };
        if (bearing is >= 0) list.Add(new("bearing", Number(bearing.Value)));
        if (altitude != null) list.Add(new("altitude", Number(altitude.Value)));
        if (accuracy != null) list.Add(new("accuracy", Number(accuracy.Value)));
        if (batteryPercent != null) list.Add(new("batt", Number(batteryPercent.Value)));
        return list;
    }

    public static string Query(IEnumerable<KeyValuePair<string, string>> parameters) =>
        string.Join("&", parameters.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
