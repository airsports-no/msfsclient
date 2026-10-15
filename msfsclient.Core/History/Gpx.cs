using System.Globalization;
using System.Text;
using msfsclient.Core.Api;

namespace msfsclient.Core.History;

/// <summary>
/// A GPX 1.1 track of one session. Times are UTC in ISO 8601 with the explicit <c>Z</c> designator (xsd:dateTime), e.g.
/// <c>2026-10-11T15:55:22Z</c>; fractional seconds appear only when the fix has them.
/// </summary>
public static class Gpx
{
    public static string Build(string name, IReadOnlyList<LoggedPoint> points)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<gpx version=\"1.1\" creator=\"Airsports\" xmlns=\"http://www.topografix.com/GPX/1/1\" ");
        sb.Append("xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" ");
        sb.Append("xsi:schemaLocation=\"http://www.topografix.com/GPX/1/1 http://www.topografix.com/GPX/1/1/gpx.xsd\">\n");
        sb.Append("  <metadata>\n    <name>").Append(XmlEscape(name)).Append("</name>\n");
        if (points.Count > 0) sb.Append("    <time>").Append(Time(points[0].TimestampMillis)).Append("</time>\n");
        sb.Append("  </metadata>\n");
        sb.Append("  <trk>\n    <name>").Append(XmlEscape(name)).Append("</name>\n    <trkseg>\n");
        foreach (var p in points)
        {
            // A broken value from the simulator (not a number, or out of range) would make a corrupt file.
            if (!double.IsFinite(p.Latitude) || !double.IsFinite(p.Longitude) || Math.Abs(p.Latitude) > 90 || Math.Abs(p.Longitude) > 180) continue;
            sb.Append("      <trkpt lat=\"").Append(Fixed(p.Latitude, 7)).Append("\" lon=\"").Append(Fixed(p.Longitude, 7)).Append("\">\n");
            if (p.Altitude is { } alt && double.IsFinite(alt) && Math.Abs(alt) < 1e9) sb.Append("        <ele>").Append(Fixed(alt, 1)).Append("</ele>\n");
            sb.Append("        <time>").Append(Time(p.TimestampMillis)).Append("</time>\n");
            sb.Append("      </trkpt>\n");
        }
        sb.Append("    </trkseg>\n  </trk>\n</gpx>\n");
        return sb.ToString();
    }

    /// <summary>UTC instant as <c>yyyy-MM-ddTHH:mm:ss[.fff]Z</c>.</summary>
    public static string Time(long epochMillis) => IsoTime.Format(epochMillis);

    /// <summary>A file name that is safe everywhere, e.g. <c>airsports-2026-10-11-1555Z.gpx</c> (the start time in UTC).</summary>
    public static string FileName(long startedAtMillis)
    {
        var t = Time(startedAtMillis); // 2026-10-11T15:55:22Z
        return $"airsports-{t[..10]}-{t.Substring(11, 2)}{t.Substring(14, 2)}Z.gpx";
    }

    /// <summary>Plain decimal text with exactly <paramref name="decimals"/> places: no exponent notation, whatever the magnitude.</summary>
    public static string Fixed(double value, int decimals)
    {
        long scale = 1;
        for (var i = 0; i < decimals; i++) scale *= 10;
        var scaled = (long)Math.Round(Math.Abs(value) * scale, MidpointRounding.ToEven);
        var whole = scaled / scale;
        var fraction = (scaled % scale).ToString(CultureInfo.InvariantCulture).PadLeft(decimals, '0');
        var sign = value < 0 && scaled != 0 ? "-" : "";
        return $"{sign}{whole.ToString(CultureInfo.InvariantCulture)}.{fraction}";
    }

    private static string XmlEscape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
