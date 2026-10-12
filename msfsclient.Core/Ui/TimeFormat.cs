using System.Globalization;
using msfsclient.Core.Api;

namespace msfsclient.Core.Ui;

/// <summary>Times as pilots read them (port of ui/TimeFormat.kt and the duration format of the history screen).</summary>
public static class TimeFormat
{
    /// <summary>"2026-10-10 14:05" in the PC's time zone (or <paramref name="zone"/>); the raw text when it cannot be parsed.</summary>
    public static string FormatLocal(string? iso, TimeZoneInfo? zone = null)
    {
        if (iso == null) return "Time not set";
        var t = IsoTime.Parse(iso);
        if (t == null) return iso;
        return TimeZoneInfo.ConvertTime(t.Value, zone ?? TimeZoneInfo.Local).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>"10:30" in the PC's time zone; the raw text when it cannot be parsed.</summary>
    public static string FormatTimeLocal(string? iso, TimeZoneInfo? zone = null)
    {
        if (iso == null) return "-";
        var local = FormatLocal(iso, zone);
        return local == iso ? iso : local[^5..];
    }

    /// <summary>The zone to show times in: the contest's own zone when known and valid, else the PC's.</summary>
    public static TimeZoneInfo ZoneOrDevice(string? zoneId)
    {
        if (zoneId == null) return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(zoneId); }
        catch (Exception) { return TimeZoneInfo.Local; }
    }

    /// <summary>A zone's name as the flight orders print it ("Europe/Oslo"), also for the Windows zone of the PC.</summary>
    public static string ZoneLabel(TimeZoneInfo zone) =>
        zone.HasIanaId ? zone.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;

    /// <summary>"10:03:27" in <paramref name="zone"/>: a seconds-resolution clock, for comparing with planned start-line crossing times.</summary>
    public static string FormatClock(long epochMillis, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(epochMillis), zone).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>"10:04" in <paramref name="zone"/>: a whole-minute time such as the start minute.</summary>
    public static string FormatMinute(long epochMillis, TimeZoneInfo zone) => FormatClock(epochMillis, zone)[..5];

    /// <summary>"1 h 12 min", "7 min 30 s" or "45 s".</summary>
    public static string FormatDuration(long millis)
    {
        var s = Math.Max(0, millis / 1000);
        return s >= 3600 ? $"{s / 3600} h {(s % 3600) / 60} min" : s >= 60 ? $"{s / 60} min {s % 60} s" : $"{s} s";
    }

    /// <summary>How long tracking has run, for the stop confirmation: "less than a minute", "12 min" or "1 h 5 min".</summary>
    public static string TrackedFor(long millis)
    {
        var minutes = Math.Max(0, millis / 60_000);
        return minutes < 1 ? "less than a minute" : minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60} min";
    }
}
