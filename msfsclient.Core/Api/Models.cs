using System.Text.Json.Serialization;

namespace msfsclient.Core.Api;

// Port of api/Models.kt. Property names map to snake_case through AirsportsJson; [JsonPropertyName] only where the
// server's name differs from the C# one.

/// <summary>Mirrors backend <c>PersonSerialiser</c> (src/display/serialisers.py).</summary>
public sealed record Person
{
    public int? Id { get; init; }
    public string? Email { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Country { get; init; }
    public string? CountryFlagUrl { get; init; }
    public string? PhoneNationalNumber { get; init; }
    public string? PhoneCountryPrefix { get; init; }
    public bool Validated { get; init; }
    public string? AppTrackingId { get; init; }
    /// <summary>The Traccar device id the simulator client tracks with (the phone app uses <see cref="AppTrackingId"/>).</summary>
    public string? SimulatorTrackingId { get; init; }
    public string? AppAircraftRegistration { get; init; }
    public string? Biography { get; init; }
    public string? Picture { get; init; }
    public bool IsPublic { get; init; }
}

/// <summary>Partial update body for <c>userprofile/partial_update_profile/</c>. Null fields are omitted.</summary>
public sealed record PersonUpdate
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Country { get; init; }
    public string? PhoneNationalNumber { get; init; }
    public string? PhoneCountryPrefix { get; init; }
    public bool? Validated { get; init; }
    public string? AppAircraftRegistration { get; init; }
    public string? Biography { get; init; }
    public bool? IsPublic { get; init; }
}

/// <summary>One entry of <c>userprofile/get_current_sim_navigation_task/</c> (404 means nothing scheduled now).</summary>
public sealed record CurrentTask
{
    public NavigationTaskSummary NavigationTask { get; init; } = new();
    public IReadOnlyList<ActiveContestant> ActiveContestants { get; init; } = [];
}

public sealed record NavigationTaskSummary
{
    [JsonPropertyName("pk")] public int Id { get; init; }
    public string? Name { get; init; }
    public string? TrackingLink { get; init; }
    /// <summary>The pilot set up this flight themselves (training).</summary>
    public bool AllowSelfManagement { get; init; }
}

public sealed record ActiveContestant
{
    public int Id { get; init; }
    public string? TrackerStartTime { get; init; }
    public string? FinishedByTime { get; init; }
    public string? FirstPositionTime { get; init; }
    /// <summary>Time of the latest STORED position. The task's calculation delay holds these back, so it lags real reception.</summary>
    public string? LastPositionTime { get; init; }
    /// <summary>When the server last received a position from this device, not held back by the calculation delay.</summary>
    public string? LastReceivedTime { get; init; }
    /// <summary>The organizer's delay before positions and scores show on the map; stored positions lag by this much.</summary>
    public double? CalculationDelayMinutes { get; init; }
    public int? ContestId { get; init; }
    public string? TakeoffTime { get; init; }
    public int? ContestantNumber { get; init; }
    public double? AirSpeed { get; init; }
    public double? WindSpeed { get; init; }
    public double? WindDirection { get; init; }
    public bool AdaptiveStart { get; init; }
    /// <summary>May the signed-in user open this task's live map? Null from an older backend (then the website decides).</summary>
    public bool? MapViewable { get; init; }
    /// <summary>The competition map page showing this contestant's flight, e.g. /competition-map/10/7?contestantIds=55.</summary>
    public string? MapPath { get; init; }
    /// <summary>The crew may register photo observations from the app in this task (older backends: null, which means no).</summary>
    public bool? ObservationRegistration { get; init; }
    public string? ContestName { get; init; }
    public TaskType? TaskType { get; init; }
    public string? ContestLogo { get; init; }
    public string? ContestHeaderImage { get; init; }
    public string? ContestLocation { get; init; }
    public string? ContestCountry { get; init; }
    public string? ContestWebsite { get; init; }
    /// <summary>Null when talking to an older backend that does not report it.</summary>
    public bool? CalculatorRunning { get; init; }
    public bool? CalculatorFinished { get; init; }
    /// <summary>Live gate timing, only sent for tasks that allow it (older backends: null).</summary>
    public TrainingFeedback? TrainingFeedback { get; init; }
    /// <summary>The server says this flight supports training mode. Null from an older backend: no.</summary>
    public bool? TrainingAvailable { get; init; }
    /// <summary>The pilot's own score so far; only where the results are visible to them and not held back.</summary>
    public double? Score { get; init; }
    /// <summary>Page that downloads the newest generated flight order, a path on the site; null when none exists.</summary>
    public string? FlightOrderPath { get; init; }
}

/// <summary>The last gate the live calculator passed and how far from its required time, in seconds (positive: late).</summary>
public sealed record TrainingFeedback
{
    public string? LastGate { get; init; }
    public double? LastGateTimeOffset { get; init; }
}

public sealed record DeleteAccountRequest(string Email);

/// <summary>A contestant entry of <c>my_future_flights</c> / <c>my_previous_flights</c> (only what the app needs).</summary>
public sealed record Flight
{
    public int Id { get; init; }
    public int ContestId { get; init; }
    public int NavigationTask { get; init; }
    public string? TakeoffTime { get; init; }
    public string? TrackerStartTime { get; init; }
    public string? FinishedByTime { get; init; }
    public string? DefaultMapUrl { get; init; }
    public int? ContestantNumber { get; init; }
    public double? AirSpeed { get; init; }
    public double? WindSpeed { get; init; }
    public double? WindDirection { get; init; }
    public bool AdaptiveStart { get; init; }
    public string? FlightOrderPath { get; init; }
    /// <summary>The server's verdict on whether this user may cancel the flight (null from older servers: fall back to the task).</summary>
    public bool? CanCancel { get; init; }
    public string? NavigationTaskName { get; init; }
    public bool? MapViewable { get; init; }
    public string? ContestName { get; init; }
    public TaskType? TaskType { get; init; }
    public string? ContestLogo { get; init; }
    public string? ContestHeaderImage { get; init; }
    public string? ContestLocation { get; init; }
    public string? ContestCountry { get; init; }
    public string? ContestWebsite { get; init; }
}

public sealed record ContestSummary
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    [JsonPropertyName("navigationtask_set")] public IReadOnlyList<TaskSummary> NavigationTasks { get; init; } = [];
    /// <summary>Square logo and wide banner; URLs may be relative to the site, see <see cref="Urls.AbsoluteUrl"/>.</summary>
    public string? Logo { get; init; }
    public string? HeaderImage { get; init; }
    [JsonPropertyName("contest_website")] public string? Website { get; init; }
    /// <summary>"latitude,longitude" as entered by the organizer; may be blank.</summary>
    public string? Location { get; init; }
    public string? Country { get; init; }
    public string? TimeZone { get; init; }
}

public sealed record TaskSummary
{
    public int Pk { get; init; }
    public string Name { get; init; } = "";
    public string? TrackingLink { get; init; }
    public string? StartTime { get; init; }
    public string? FinishTime { get; init; }
    [JsonPropertyName("task_subtype_definition")] public TaskType? TaskType { get; init; }
    /// <summary>Pilots may register and cancel their own flights in this task.</summary>
    public bool AllowSelfManagement { get; init; }
}

/// <summary>E.g. "Precision" / "2.A1 Curve navigation with time estimation".</summary>
public sealed record TaskType
{
    public string? DisplayName { get; init; }
    [JsonPropertyName("coarse_family_label")] public string? FamilyLabel { get; init; }
}

public sealed record ContestPage
{
    public IReadOnlyList<ContestSummary> Results { get; init; } = [];
    public string? Next { get; init; }
}

/// <summary>A flight joined with its contest and task names, ready to display.</summary>
public sealed record MyFlight
{
    public int Id { get; init; }
    public string ContestName { get; init; } = "";
    public string TaskName { get; init; } = "";
    public string? TakeoffTime { get; init; }
    /// <summary>What to open when the pilot taps the flight: the competition map of their own flight.</summary>
    public string? MapLink { get; init; }
    public ContestSummary? Contest { get; init; }
    public TaskSummary? Task { get; init; }
    public Flight? Flight { get; init; }

    /// <summary>May the pilot open the map / results of this flight? False for a private task they may not view; null: not known.</summary>
    public bool? MapViewable => Flight?.MapViewable;

    /// <summary>
    /// May the pilot cancel this flight from the app? Any flight in a task that allows self-management, before or after it
    /// has started; the server's own verdict wins when it sends one.
    /// </summary>
    public bool CanCancel() => Flight?.CanCancel ?? Task?.AllowSelfManagement == true;

    /// <summary>The tracking window has opened, so cancelling ends the flight now instead of just removing a booking.</summary>
    public bool IsUnderway(long nowMillis)
    {
        var opens = IsoTime.EpochMillisOrNull(Flight?.TrackerStartTime);
        return opens != null && opens <= nowMillis;
    }
}

/// <summary><c>GET /api/v1/mobile/config/</c> (public). Versions are build numbers per platform.</summary>
public sealed record MobileConfig
{
    public PlatformConfig Android { get; init; } = new();
    public PlatformConfig Ios { get; init; } = new();
    /// <summary>The simulator client's block; null until the backend adds it (then there is no version gate).</summary>
    public PlatformConfig? Msfs { get; init; }
    public bool PushNotificationsEnabled { get; init; }

    public PlatformConfig? ForPlatform(string platform) => platform switch
    {
        "ios" => Ios,
        "android" => Android,
        "msfs" => Msfs,
        _ => null,
    };
}

public sealed record PlatformConfig
{
    public int MinVersion { get; init; }
    public int LatestVersion { get; init; }
    public string? StoreUrl { get; init; }
}

/// <summary>A photo the crew can mark; <see cref="File"/> is a public image URL. The target position is deliberately not part of this.</summary>
public sealed record ObservationPhoto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? File { get; init; }
}

/// <summary>Which photo is marked, and when. Nothing about how it scored: the crew gets no feedback on that.</summary>
public sealed record PhotoObservation
{
    public int Id { get; init; }
    public int Photo { get; init; }
    public string? PhotoName { get; init; }
    public string? Time { get; init; }
}

public sealed record ObservationPhotos
{
    public bool Enabled { get; init; }
    /// <summary>The flight is being tracked right now (or within the grace period after it), so marking is accepted.</summary>
    public bool MayRegister { get; init; }
    public IReadOnlyList<ObservationPhoto> Photos { get; init; } = [];
    public IReadOnlyList<PhotoObservation> Observations { get; init; } = [];
    /// <summary>After this nothing can be registered, changed or cancelled: the tracking window plus a few minutes.</summary>
    public string? RegistrationClosesAt { get; init; }
    public int? RegistrationGraceMinutes { get; init; }
}

public sealed record PhotoObservationRequest
{
    public int Photo { get; init; }
    public string Time { get; init; } = "";
    /// <summary>Both null: the server takes the position from the flight's position reports at <see cref="Time"/>.</summary>
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    /// <summary>A correction: replaces the earlier mark of the photo even when the time is earlier. Left out when false.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Replace { get; init; }
}

/// <summary>A message from the organizers of a contest (or of one task), still showing in the app.</summary>
public sealed record Announcement
{
    public int Id { get; init; }
    public int? ContestId { get; init; }
    public string ContestName { get; init; } = "";
    public string? TaskName { get; init; }
    public string Message { get; init; } = "";
    public string? CreatedAt { get; init; }
}

/// <summary>
/// The practice flight the server offers new simulator pilots (<c>userprofile/msfs_quickstart/</c>, 404 when none): the task,
/// where to start in the simulator, how to register, and the pilot's own registration.
/// </summary>
public sealed record Quickstart
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public int ContestId { get; init; }
    public string ContestName { get; init; } = "";
    public int NavigationTaskId { get; init; }
    public string NavigationTaskName { get; init; } = "";
    /// <summary>Where to start, as the pilot finds it in the MSFS world map, e.g. "Kjeller (ENKJ), runway 30".</summary>
    public string StartLocation { get; init; } = "";
    public string StartInstructions { get; init; } = "";
    /// <summary>On the runway at the take-off gate, facing the take-off direction; null when the route has no take-off gate.</summary>
    public QuickstartPosition? GroundStart { get; init; }
    /// <summary>In the air before the starting point, on course; null when the route has no starting point.</summary>
    public QuickstartPosition? AirStart { get; init; }
    /// <summary>The website's registration page for the task (it reports the registration back to the client).</summary>
    public string RegistrationPath { get; init; } = "";
    /// <summary>What the one-press registration signs the pilot up with (null from a server without it).</summary>
    public double? AirSpeed { get; init; }
    public string? AircraftRegistration { get; init; }
    public string? ClubName { get; init; }
    public QuickstartFlight? RegisteredFlight { get; init; }
}

public sealed record QuickstartPosition
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    /// <summary>Feet above sea level; null for a start on the ground.</summary>
    public double? AltitudeFt { get; init; }
    /// <summary>True heading in degrees; null when not known.</summary>
    public double? Heading { get; init; }
}

public sealed record QuickstartFlight
{
    public int Id { get; init; }
    public string? TakeoffTime { get; init; }
    public string? TrackerStartTime { get; init; }
    public string? FinishedByTime { get; init; }
    public bool? AdaptiveStart { get; init; }
    public double? AirSpeed { get; init; }
    /// <summary>The flight order's download page, null until the server has made it (in the background, after registering).</summary>
    public string? FlightOrderPath { get; init; }
}
