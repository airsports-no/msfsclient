using System.Globalization;
using System.Text.RegularExpressions;

namespace msfsclient.Core.Api;

/// <summary>
/// ISO-8601 instants as the backend sends them ("2026-10-10T09:05:00Z", "...00.123456+00:00"). Parsing is strict: a value
/// without a date, a time and an offset is not an instant (the lenient .NET parser would read "10:00" as today).
/// </summary>
public static partial class IsoTime
{
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|z|[+-]\d{2}:?\d{2})$")]
    private static partial Regex InstantPattern();

    public static DateTimeOffset? Parse(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso) || !InstantPattern().IsMatch(iso.Trim())) return null;
        return DateTimeOffset.TryParse(iso.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value : null;
    }

    public static long? EpochMillisOrNull(string? iso) => Parse(iso)?.ToUnixTimeMilliseconds();

    /// <summary>UTC instant as <c>yyyy-MM-ddTHH:mm:ss[.fff]Z</c>: fractional seconds only when there are any.</summary>
    public static string Format(long epochMillis)
    {
        var t = DateTimeOffset.FromUnixTimeMilliseconds(epochMillis).UtcDateTime;
        return t.ToString(epochMillis % 1000 == 0 ? "yyyy-MM-dd'T'HH:mm:ss'Z'" : "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }
}
