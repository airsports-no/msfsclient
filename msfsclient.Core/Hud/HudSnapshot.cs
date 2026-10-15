using msfsclient.Core.App;
using msfsclient.Core.Observations;
using msfsclient.Core.Ui;

namespace msfsclient.Core.Hud;

/// <summary>
/// Everything an in-sim display shows, as plain data (sent as JSON by <see cref="HudServer"/>): what the client's dock shows,
/// the start timer, training feedback, the photos and the announcements. Only what the client already has from the server:
/// no route, no gates, nothing the organizer has not allowed.
/// </summary>
public sealed record HudSnapshot
{
    public bool SignedIn { get; init; }
    public string Version { get; init; } = "";
    public string? Pilot { get; init; }
    public string? Flight { get; init; }
    public HudTracking Tracking { get; init; } = new();
    public HudTimer? Timer { get; init; }
    public HudTraining? Training { get; init; }
    public HudPhotos? Photos { get; init; }
    public IReadOnlyList<HudAnnouncement> Announcements { get; init; } = [];

    public static HudSnapshot Of(FlightStatus status, string versionLabel)
    {
        var s = status.State;
        if (s.Person == null || s.IsOutside)
        {
            return new HudSnapshot { Version = versionLabel, Tracking = new HudTracking { Lines = [new StatusLine("Sign in to the Airsports client to track your flight.", StatusTone.Muted)] } };
        }
        var adaptive = s.ActiveContestants.FirstOrDefault(c => c.AdaptiveStart);
        var zone = TimeFormat.ZoneOrDevice(adaptive?.ContestId is { } cid && s.Contests.TryGetValue(cid, out var contest) ? contest.TimeZone : null);
        var obs = s.Observations;
        var showPhotos = s.ActiveContestants.Any(c => c.ObservationRegistration == true);
        var fresh = FreshFix(status) != null;
        var blocker = ObservationRules.Blocker(obs, status.Tracking.Running, fresh, status.NowMillis);
        return new HudSnapshot
        {
            SignedIn = true,
            Version = versionLabel,
            Pilot = new[] { s.Person.FirstName, s.Person.Email }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)),
            Flight = status.FlightLabel,
            Tracking = new HudTracking
            {
                Running = status.Tracking.Running,
                CanStart = status.CanStartTracking,
                StartedAtMillis = status.Tracking.StartedAtMillis,
                Lines = status.Lines(compact: true),
            },
            Timer = adaptive == null ? null : new HudTimer
            {
                OriginMillis = s.TimerOriginMillis,
                BufferMinutes = s.TimerBufferMinutes,
                ClockMillis = status.NowMillis,
                Zone = TimeFormat.ZoneLabel(zone),
                UtcOffsetMinutes = (int)zone.GetUtcOffset(DateTimeOffset.FromUnixTimeMilliseconds(status.NowMillis)).TotalMinutes,
                StartMinute = s.TimerOriginMillis is { } origin ? TimeFormat.FormatMinute(origin, zone) : null,
            },
            Training = status.TrainingOn
                ? new HudTraining { Gate = status.Gate?.Gate, Text = status.Gate?.Text, Timing = status.Gate?.Timing.ToString() }
                : null,
            Photos = !showPhotos ? null : new HudPhotos
            {
                Rule = ObservationRules.RuleText(obs, zone),
                Blocker = blocker,
                Counter = obs.Counter,
                Items = obs.Photos.Select(p =>
                {
                    var (text, done) = ObservationRules.MarkState(p, obs);
                    return new HudPhoto { Id = p.Id, Name = p.Name, Image = p.File, State = text, Marked = done || obs.Pending.Contains(p.Id), CanMark = blocker == null };
                }).ToList(),
            },
            Announcements = s.Announcements.Select(a => new HudAnnouncement
            {
                Id = a.Id,
                Source = string.Join(" - ", new[] { a.ContestName, a.TaskName }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } src ? src : "From the organizers",
                Message = a.Message,
            }).ToList(),
        };
    }

    /// <summary>The latest simulator position, when it is fresh enough to stamp a photo mark with.</summary>
    public static ObservationFix? FreshFix(FlightStatus status)
    {
        var t = status.Tracking;
        if (!t.Running || t.LastFixMillis is not { } time || t.LastLatitude is not { } lat || t.LastLongitude is not { } lon) return null;
        return status.NowMillis - time <= ObservationRules.FixMaxAgeMillis ? new ObservationFix(lat, lon, time) : null;
    }
}

public sealed record HudTracking
{
    public bool Running { get; init; }
    public bool CanStart { get; init; }
    public long? StartedAtMillis { get; init; }
    public IReadOnlyList<StatusLine> Lines { get; init; } = [];
}

/// <summary>
/// The adaptive-start timer. The display counts itself from <see cref="OriginMillis"/>: <see cref="ClockMillis"/> is the PC
/// clock (what positions are stamped with) when the snapshot was made, so a display can correct for its own clock.
/// </summary>
public sealed record HudTimer
{
    public long? OriginMillis { get; init; }
    public int BufferMinutes { get; init; }
    public long ClockMillis { get; init; }
    public string Zone { get; init; } = "";
    public int UtcOffsetMinutes { get; init; }
    public string? StartMinute { get; init; }
}

public sealed record HudTraining
{
    /// <summary>Null until the first gate has been passed.</summary>
    public string? Gate { get; init; }
    public string? Text { get; init; }
    /// <summary>"OnTime", "Early" or "Late".</summary>
    public string? Timing { get; init; }
}

public sealed record HudPhotos
{
    public string Rule { get; init; } = "";
    /// <summary>Why marking is not possible right now; null when it is.</summary>
    public string? Blocker { get; init; }
    public string? Counter { get; init; }
    public IReadOnlyList<HudPhoto> Items { get; init; } = [];
}

public sealed record HudPhoto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? Image { get; init; }
    public string State { get; init; } = "";
    public bool Marked { get; init; }
    public bool CanMark { get; init; }
}

public sealed record HudAnnouncement
{
    public int Id { get; init; }
    public string Source { get; init; } = "";
    public string Message { get; init; } = "";
}
