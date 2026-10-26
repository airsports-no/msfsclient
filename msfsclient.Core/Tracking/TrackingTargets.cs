using System.Globalization;
using msfsclient.Core.Api;

namespace msfsclient.Core.Tracking;

/// <summary>One label/value cell of the flight strip ("Tracking window", "10:00 - 11:30").</summary>
public sealed record Fact(string Label, string Value);

/// <summary>One flight the client is (or will be) tracking, with the details shown on the Home screen.</summary>
public sealed record TrackingTarget
{
    public string? ContestName { get; init; }
    public string TaskName { get; init; } = "";
    /// <summary>E.g. "2.A1 Curve navigation with time estimation".</summary>
    public string? TaskType { get; init; }
    /// <summary>Label/value pairs in display order: tracking window, take-off, airspeed, wind, contestant number, score.</summary>
    public IReadOnlyList<Fact> Facts { get; init; } = [];
    public bool AdaptiveStart { get; init; }
    public string? BannerUrl { get; init; }
    public string? LogoUrl { get; init; }
    public string? Country { get; init; }
    /// <summary>Path or URL of the live map for this task.</summary>
    public string? MapLink { get; init; }
    /// <summary>The live map of the whole task with every contestant, for a past flight whose <see cref="MapLink"/> shows only the pilot's own track.</summary>
    public string? AllMapLink { get; init; }
    public string? WebsiteUrl { get; init; }
    /// <summary>The contest's page on the Air Sports website (<c>/mission-dashboard/&lt;id&gt;</c>), opened in the browser; null when the contest is not known.</summary>
    public string? ContestPagePath { get; init; }
    public string? DirectionsUrl { get; init; }
    /// <summary>The live map is private to the organizer and this pilot may not open it (the details above are still shown).</summary>
    public bool MapPrivate { get; init; }
    /// <summary>Path of the page that downloads the generated flight order; null when none has been made.</summary>
    public string? FlightOrderPath { get; init; }
    /// <summary>The flight (contestant) this strip is about; Home looks it up among the pilot's flights to offer "Cancel flight".</summary>
    public int? FlightId { get; init; }
    /// <summary>
    /// For the next flight, whose tracking window opens later: says so plainly, with when it opens. Until then the server
    /// has no current flight for the pilot, so positions are not scored. Null when the window is already open (the server just
    /// has no current flight for another reason, e.g. the simulator's positions are ignored) or its start is unknown.
    /// </summary>
    public string? NotActiveNote { get; init; }
    /// <summary>When the tracking window of the next flight opens, while that is still ahead (with <see cref="NotActiveNote"/>); Home fetches the flights again then.</summary>
    public long? TrackingOpensAtMillis { get; init; }
}

public sealed record TrackingTargets(IReadOnlyList<TrackingTarget> Now, TrackingTarget? Next)
{
    public const string AdaptiveStartText = "Adaptive (nearest minute you cross the line)";

    /// <summary>"17" for a whole score, otherwise one decimal ("17.5").</summary>
    public static string ScoreText(double score) =>
        score % 1.0 == 0.0
            ? ((long)score).ToString(CultureInfo.InvariantCulture)
            : (Math.Round(score * 10, MidpointRounding.ToEven) / 10).ToString(CultureInfo.InvariantCulture);

    private static string Knots(double value) => $"{(int)value} knots";

    private static string? WindText(double? direction, double? speed) =>
        direction == null || speed == null ? null : $"{((int)direction.Value).ToString(CultureInfo.InvariantCulture).PadLeft(3, '0')}@{(int)speed.Value} knots";

    private static string? TypeOf(TaskSummary? task) => Urls.NotBlank(task?.TaskType?.DisplayName);

    private static string? Directions(string? location) =>
        Urls.VenueCoordinates(location) is { } v ? Urls.DirectionsUrl(v.Latitude, v.Longitude) : null;

    /// <summary>
    /// Which task(s) the pilot is tracking: the open windows from the "Now" endpoint, or else the next scheduled flight.
    /// <paramref name="contests"/> (by id) supplies names, images, task types and the venue; <paramref name="formatTime"/> turns
    /// an ISO instant into a short local time and <paramref name="formatDateTime"/> into date and time.
    /// </summary>
    public static TrackingTargets Of(
        IReadOnlyList<CurrentTask> tasks, IReadOnlyList<MyFlight> upcoming, IReadOnlyDictionary<int, ContestSummary> contests,
        Func<string?, string> formatTime, Func<string?, string> formatDateTime, DateTimeOffset? nowUtc = null)
    {
        var now = tasks.SelectMany(task => task.ActiveContestants.Select(c =>
        {
            var fromMetadata = c.ContestId is { } cid
                ? Urls.ContestFromMetadata(cid, task.NavigationTask.Id, task.NavigationTask.Name, c.ContestName, c.TaskType, c.ContestLogo,
                    c.ContestHeaderImage, c.ContestLocation, c.ContestCountry, c.ContestWebsite)
                : null;
            var contest = (c.ContestId is { } id ? contests.GetValueOrDefault(id) : null) ?? fromMetadata;
            var taskSummary = contest?.NavigationTasks.FirstOrDefault(t => t.Pk == task.NavigationTask.Id) ?? fromMetadata?.NavigationTasks.FirstOrDefault();
            var facts = new List<Fact> { new("Tracking window", $"{formatTime(c.TrackerStartTime)} - {formatTime(c.FinishedByTime)}") };
            if (!c.AdaptiveStart && c.TakeoffTime != null) facts.Add(new("Take-off", formatTime(c.TakeoffTime)));
            if (c.AdaptiveStart) facts.Add(new("Start", AdaptiveStartText));
            if (c.AirSpeed is { } air) facts.Add(new("Airspeed", Knots(air)));
            if (WindText(c.WindDirection, c.WindSpeed) is { } wind) facts.Add(new("Wind", wind));
            if (c.ContestantNumber is { } n) facts.Add(new("Contestant", $"#{n}"));
            if (c.Score is { } score) facts.Add(new("Score so far", ScoreText(score)));
            return new TrackingTarget
            {
                ContestName = Urls.NotBlank(contest?.Name),
                TaskName = task.NavigationTask.Name ?? $"Task {task.NavigationTask.Id}",
                TaskType = TypeOf(taskSummary),
                Facts = facts,
                FlightOrderPath = c.FlightOrderPath,
                FlightId = c.Id,
                AdaptiveStart = c.AdaptiveStart,
                BannerUrl = Urls.AbsoluteUrl(contest?.HeaderImage),
                LogoUrl = Urls.AbsoluteUrl(contest?.Logo),
                Country = Urls.NotBlank(contest?.Country),
                MapPrivate = c.MapViewable == false,
                MapLink = c.MapViewable == false ? null : c.MapPath ?? task.NavigationTask.TrackingLink,
                WebsiteUrl = Urls.NotBlank(contest?.Website),
                ContestPagePath = c.ContestId is { } pageId ? Urls.ContestPagePath(pageId) : null,
                DirectionsUrl = Directions(contest?.Location),
            };
        })).ToList();

        var next = now.Count == 0 && upcoming.Count > 0 ? FlightTarget(upcoming[0], formatTime, formatDateTime, nowUtc) : null;
        return new TrackingTargets(now, next);
    }

    /// <summary>
    /// One of the pilot's own flights as a card: the Home screen's "next flight" and every row of My flights. The "not active
    /// yet" note is only given for a flight whose tracking window opens after <paramref name="nowUtc"/>.
    /// </summary>
    public static TrackingTarget FlightTarget(MyFlight f, Func<string?, string> formatTime, Func<string?, string> formatDateTime, DateTimeOffset? nowUtc = null)
    {
        var fl = f.Flight;
        var facts = new List<Fact>();
        var opens = fl?.TrackerStartTime;
        var opensAtMillis = IsoTime.EpochMillisOrNull(opens);
        var opensLater = opensAtMillis > (nowUtc ?? DateTimeOffset.UtcNow).ToUnixTimeMilliseconds();
        if (opens != null && fl?.FinishedByTime is { } closes)
        {
            // The end shows its date too when the window runs past midnight. The date part is the date-and-time text without
            // the time, so this does not depend on how the formatters lay the text out.
            var opensText = formatDateTime(opens);
            var closesText = formatDateTime(closes);
            var sameDay = opensText.Replace(formatTime(opens), "") == closesText.Replace(formatTime(closes), "");
            facts.Add(new("Tracking window", $"{opensText} - {(sameDay ? formatTime(closes) : closesText)}"));
        }
        if (fl?.AdaptiveStart == true) facts.Add(new("Start", AdaptiveStartText));
        else facts.Add(new("Take-off", formatDateTime(f.TakeoffTime)));
        if (fl?.AirSpeed is { } air) facts.Add(new("Airspeed", Knots(air)));
        if (WindText(fl?.WindDirection, fl?.WindSpeed) is { } wind) facts.Add(new("Wind", wind));
        if (fl?.ContestantNumber is { } n) facts.Add(new("Contestant", $"#{n}"));
        return new TrackingTarget
        {
            ContestName = f.ContestName,
            TaskName = f.TaskName,
            TaskType = TypeOf(f.Task),
            Facts = facts,
            FlightOrderPath = fl?.FlightOrderPath,
            FlightId = f.Id,
            AdaptiveStart = fl?.AdaptiveStart == true,
            BannerUrl = Urls.AbsoluteUrl(f.Contest?.HeaderImage),
            LogoUrl = Urls.AbsoluteUrl(f.Contest?.Logo),
            Country = Urls.NotBlank(f.Contest?.Country),
            MapPrivate = fl?.MapViewable == false,
            MapLink = fl?.MapViewable == false ? null : f.MapLink,
            AllMapLink = fl is null || fl.MapViewable == false ? null : Urls.CompetitionTaskMapPath(fl.ContestId, fl.NavigationTask),
            WebsiteUrl = Urls.NotBlank(f.Contest?.Website),
            ContestPagePath = fl?.ContestId is { } nextId ? Urls.ContestPagePath(nextId) : null,
            DirectionsUrl = Directions(f.Contest?.Location),
            NotActiveNote = opensLater ? $"Not active yet: tracking for this flight opens at {formatDateTime(opens)}. Until then your positions are not scored." : null,
            TrackingOpensAtMillis = opensLater ? opensAtMillis : null,
        };
    }
}
