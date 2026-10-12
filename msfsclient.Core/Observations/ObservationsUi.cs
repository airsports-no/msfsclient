using System.Collections.Immutable;
using System.Globalization;
using msfsclient.Core.Api;
using msfsclient.Core.Ui;

namespace msfsclient.Core.Observations;

/// <summary>The photo observation page's state.</summary>
public sealed record ObservationsUi
{
    /// <summary>The flight the observations are for (the pilot's active contestant in a task that allows it).</summary>
    public int? ContestantId { get; init; }
    public bool Loading { get; init; }
    public bool Enabled { get; init; } = true;
    /// <summary>The server accepts marks now (the flight is being tracked or just ended). False: the page explains why not.</summary>
    public bool MayRegister { get; init; } = true;
    /// <summary>When the crew can no longer mark, change or cancel anything (ISO time from the server).</summary>
    public string? RegistrationClosesAt { get; init; }
    public int GraceMinutes { get; init; } = 5;
    public IReadOnlyList<ObservationPhoto> Photos { get; init; } = [];
    /// <summary>Photo id -> what the server has stored.</summary>
    public ImmutableDictionary<int, PhotoObservation> Registered { get; init; } = ImmutableDictionary<int, PhotoObservation>.Empty;
    /// <summary>Photo ids marked on this PC and not yet acknowledged by the server (waiting for a connection).</summary>
    public ImmutableHashSet<int> Pending { get; init; } = [];
    public string? Error { get; init; }
    /// <summary>Thumbnails (a grid of images) or a plain list of names; remembered while the app runs.</summary>
    public bool Thumbnails { get; init; } = true;

    /// <summary>"2/5": photos marked (stored or waiting) of all; null until the photos are loaded.</summary>
    public string? Counter => Photos.Count == 0 ? null : $"{Registered.Keys.Union(Pending).Count()}/{Photos.Count}";
}

/// <summary>Where and when the simulator last reported a position: a mark is stamped with it.</summary>
public sealed record ObservationFix(double Latitude, double Longitude, long TimeMillis);

public static class ObservationRules
{
    /// <summary>A fix older than this is too old to say where the crew is now.</summary>
    public const long FixMaxAgeMillis = 10_000;

    /// <summary>The deadline for marking, in plain words (shown on the Photos page).</summary>
    public static string RuleText(ObservationsUi ui, TimeZoneInfo? zone = null)
    {
        var until = ui.RegistrationClosesAt != null ? $" ({TimeFormat.FormatTimeLocal(ui.RegistrationClosesAt, zone)})" : "";
        return $"You can mark, change or cancel photos until {ui.GraceMinutes} minutes after your tracking window closes{until}. " +
            "After that nothing can be marked or changed.";
    }

    /// <summary>Why marking is not possible right now, or null when it is.</summary>
    public static string? Blocker(ObservationsUi ui, bool trackingRunning, bool hasFreshFix, long? nowMillis = null)
    {
        if (!ui.Enabled) return "Photo observations are not switched on for this task.";
        if (nowMillis != null && IsoTime.EpochMillisOrNull(ui.RegistrationClosesAt) is { } closes && nowMillis > closes)
            return $"Marking has closed: it ended {ui.GraceMinutes} minutes after your tracking window. Nothing can be marked or changed now.";
        if (!ui.MayRegister) return "Marking opens when your tracking window starts.";
        if (!trackingRunning) return "Start tracking before marking photos: a mark is stamped with your position.";
        if (!hasFreshFix) return "Waiting for a position from the simulator. Marking needs your current position.";
        return null;
    }

    /// <summary>"Marked - waiting to send", "Marked 10:03" or "Not marked", and whether the photo counts as marked.</summary>
    public static (string Text, bool Done) MarkState(ObservationPhoto photo, ObservationsUi ui)
    {
        if (ui.Pending.Contains(photo.Id)) return ("Marked - waiting to send", false);
        if (ui.Registered.TryGetValue(photo.Id, out var stored)) return ($"Marked {TimeFormat.FormatTimeLocal(stored.Time)}", true);
        return ("Not marked", false);
    }

    /// <summary>
    /// A time of day the crew typed ("14:03", "14:03:27" or "14.03.27") on the day of <paramref name="referenceMillis"/>, in
    /// <paramref name="zone"/>, as epoch milliseconds; null when it is not a valid time of day.
    /// </summary>
    public static long? ParseTimeOfDay(string text, long referenceMillis, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var parts = text.Trim().Split(':', '.').Select(p => p.Trim()).ToArray();
        if (parts.Length is < 2 or > 3 || parts.Any(p => p.Length == 0 || p.Length > 2 || !p.All(char.IsAsciiDigit))) return null;
        var hour = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var minute = int.Parse(parts[1], CultureInfo.InvariantCulture);
        var second = parts.Length == 3 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 0;
        if (hour > 23 || minute > 59 || second > 59) return null;
        var day = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(referenceMillis), zone).Date;
        var local = new DateTime(day.Year, day.Month, day.Day, hour, minute, second, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUnixTimeMilliseconds();
    }

    /// <summary>hh:mm:ss in <paramref name="zone"/> for showing and editing a time.</summary>
    public static string TimeOfDayText(long millis, TimeZoneInfo? zone = null) => TimeFormat.FormatClock(millis, zone ?? TimeZoneInfo.Local);
}
