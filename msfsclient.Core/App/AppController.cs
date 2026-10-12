using System.Collections.Immutable;
using msfsclient.Core.Api;
using msfsclient.Core.Auth;
using msfsclient.Core.Observations;
using msfsclient.Core.Tracking;

namespace msfsclient.Core.App;

public enum Screen { Loading, UpdateRequired, Login, VerifyEmail, Onboarding, Home, Flights, Map, Timer, Profile, Observations, Preflight, History, Tour, Debug }

/// <summary>Everything the screens show. Immutable: every change makes a new one and raises <see cref="AppController.StateChanged"/>.</summary>
public sealed record UiState
{
    public Screen Screen { get; init; } = Screen.Loading;
    public ObservationsUi Observations { get; init; } = new();
    public Person? Person { get; init; }
    /// <summary>The flights open now (the "Now" endpoint for the simulator tracking id).</summary>
    public IReadOnlyList<CurrentTask> Tasks { get; init; } = [];
    public string Email { get; init; } = "";
    /// <summary>Store page shown on the UpdateRequired screen.</summary>
    public string? UpdateUrl { get; init; }
    public bool Busy { get; init; }
    public string? Notice { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<MyFlight> Flights { get; init; } = [];
    public bool FlightsLoading { get; init; }
    /// <summary>The flight being cancelled right now, and a one-off confirmation on the flights list.</summary>
    public int? CancellingFlightId { get; init; }
    public string? FlightsNotice { get; init; }
    public bool ShowPastFlights { get; init; }
    /// <summary>Epoch millis of the whole minute the adaptive-start timer counts from; null until Start is pressed.</summary>
    public long? TimerOriginMillis { get; init; }
    /// <summary>Network time minus PC clock from the last NTP check (positive: PC behind); null until measured.</summary>
    public long? NetworkClockOffsetMillis { get; init; }
    /// <summary>Error bar of that measurement (half the round trip).</summary>
    public long NetworkClockUncertaintyMillis { get; init; }
    /// <summary>The pilot's next scheduled flights (soonest first), shown when nothing is open right now.</summary>
    public IReadOnlyList<MyFlight> Upcoming { get; init; } = [];
    /// <summary>Contests (name, images, tasks, venue) by id, for the flights open now and the upcoming ones.</summary>
    public IReadOnlyDictionary<int, ContestSummary> Contests { get; init; } = new Dictionary<int, ContestSummary>();
    public bool ProfileSaving { get; init; }
    public string? ProfileNotice { get; init; }
    /// <summary>One-off confirmation shown at the top of Home (e.g. after registering a flight).</summary>
    public string? HomeNotice { get; init; }
    /// <summary>Training mode: live feedback on every gate passed. Off at every start so a competition flight never has it by accident.</summary>
    public bool TrainingMode { get; init; }
    /// <summary>Whole minutes of countdown buffer before the start minute on the timer (0 = none).</summary>
    public int TimerBufferMinutes { get; init; }
    /// <summary>The first load of the pilot's flights has finished (Home shows a quiet "checking" line until then).</summary>
    public bool HomeLoaded { get; init; }
    /// <summary>The latest end of a tracking window seen while tracking, to warn when tracking stays on after it closed.</summary>
    public long? LastWindowEndMillis { get; init; }
    /// <summary>Announcements from the organizers that the pilot has not dismissed.</summary>
    public IReadOnlyList<Announcement> Announcements { get; init; } = [];
    /// <summary>Set when tracking was stopped after a flight that was matched to a navigation task; shown once.</summary>
    public FinishedFlight? FinishedFlight { get; init; }
    /// <summary>Where Close on the Map screen returns to.</summary>
    public Screen MapReturn { get; init; } = Screen.Home;
    /// <summary>Path (on app.airsports.no) that the Map screen opens.</summary>
    public string MapPath { get; init; } = "/";

    /// <summary>Sign-in and set-up screens: the pilot is not "inside" the app yet.</summary>
    public bool IsOutside => Screen is Screen.Loading or Screen.UpdateRequired or Screen.Login or Screen.VerifyEmail or Screen.Onboarding;

    public IReadOnlyList<ActiveContestant> ActiveContestants => Tasks.SelectMany(t => t.ActiveContestants).ToList();
}

/// <summary>A flight that tracking has just ended for, with whether the pilot may look at it on the live map right away.</summary>
/// <param name="Viewable">False: the pilot has no permission yet and must wait for the results. Null: unknown (older backend).</param>
/// <param name="Score">The pilot's score so far, when the results are theirs to see; null otherwise.</param>
public sealed record FinishedFlight(string? ContestName, string TaskName, string? MapPath, bool? Viewable, double? Score = null);

/// <summary>
/// The app state machine (port of app/AppController.kt): Login -> VerifyEmail -> Onboarding -> Home -> the rest. UI-agnostic:
/// methods run on any thread and return a task that completes when the change is done (the UI may ignore it).
/// </summary>
public sealed class AppController
{
    private readonly AirsportsApi _api;
    private readonly IAuthService _auth;
    private readonly DeviceInfo _device;
    private readonly INetworkClock? _networkClock;
    private readonly ObservationQueue? _observationQueue;
    private readonly ObservationSender? _observationSender;
    private readonly ICurrentLocation? _location;
    private readonly AppSettings? _settings;
    private readonly Func<long> _now;
    private readonly SemaphoreSlim _observationFlush = new(1, 1);
    private readonly object _lock = new();
    private UiState _state = new();

    /// <summary>A page from outside the app waiting for the pilot to be signed in and on Home.</summary>
    private string? _pendingLink;

    public AppController(
        AirsportsApi api, IAuthService auth, DeviceInfo device, INetworkClock? networkClock = null, ObservationQueue? observationQueue = null,
        ICurrentLocation? location = null, AppSettings? settings = null, Func<long>? now = null)
    {
        _api = api;
        _auth = auth;
        _device = device;
        _networkClock = networkClock;
        _observationQueue = observationQueue;
        _observationSender = observationQueue != null ? new ObservationSender(api, observationQueue) : null;
        _location = location;
        _settings = settings;
        _now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (settings != null)
        {
            // A pilot who was signed in last time sees Home at once, from the saved profile; the fresh data replaces it a moment later.
            Person? cached = null;
            if (auth.IsSignedIn() && auth.IsEmailVerified() && settings.CachedProfileJson is { } json)
            {
                try { cached = AirsportsJson.Deserialize<Person>(json); } catch (Exception) { }
            }
            _state = _state with { TimerBufferMinutes = settings.TimerBufferMinutes, Person = cached, Screen = cached != null ? Screen.Home : _state.Screen };
        }
    }

    public UiState State { get { lock (_lock) return _state; } }

    /// <summary>Raised on any thread after every change of <see cref="State"/>.</summary>
    public event Action<UiState>? StateChanged;

    /// <summary>The app version for the screens that show it ("2.0.0 (30)").</summary>
    public string AppVersionLabel => _device.VersionLabel;

    /// <summary>Loads everything for the first time. Call once after the UI subscribed to <see cref="StateChanged"/>.</summary>
    public Task Start() => RefreshAsync();

    private void Update(Func<UiState, UiState> change)
    {
        UiState next;
        lock (_lock) next = _state = change(_state);
        StateChanged?.Invoke(next);
    }

    private IReadOnlyList<Announcement> Undismissed(IEnumerable<Announcement> all)
    {
        var dismissed = _settings?.DismissedAnnouncementIds ?? new HashSet<int>();
        return all.Where(a => !dismissed.Contains(a.Id)).ToList();
    }

    /// <summary>Fetches the organizers' announcements again (the window came to the front).</summary>
    public async Task RefreshAnnouncementsAsync()
    {
        IReadOnlyList<Announcement> list;
        try { list = await _api.AnnouncementsAsync(); }
        catch (Exception) { return; }
        var shown = Undismissed(list);
        Update(s => s with { Announcements = shown });
        NotifyNew(shown);
    }

    /// <summary>Announcements already seen in this run (shown at start or notified), so each one is notified at most once.</summary>
    private readonly HashSet<int> _knownAnnouncements = [];
    private bool _announcementsLoaded;

    /// <summary>
    /// Raised (on any thread) for announcements that arrived while the app runs; the desktop has no push, so the app shows a
    /// Windows notification instead. The ones there at start are on Home already and never raised.
    /// </summary>
    public event Action<Announcement>? NewAnnouncement;

    /// <summary>The next pilot to sign in is not told about what was already there.</summary>
    private void ForgetAnnouncements()
    {
        lock (_knownAnnouncements)
        {
            _knownAnnouncements.Clear();
            _announcementsLoaded = false;
        }
    }

    private void NotifyNew(IEnumerable<Announcement> shown)
    {
        List<Announcement> fresh;
        lock (_knownAnnouncements)
        {
            fresh = shown.Where(a => _knownAnnouncements.Add(a.Id)).ToList();
            if (!_announcementsLoaded)
            {
                _announcementsLoaded = true;
                return;
            }
        }
        foreach (var a in fresh) NewAnnouncement?.Invoke(a);
    }

    /// <summary>Hides an announcement for good on this PC.</summary>
    public void DismissAnnouncement(int id)
    {
        if (_settings != null) _settings.DismissedAnnouncementIds = _settings.DismissedAnnouncementIds.Append(id).ToHashSet();
        Update(s => s with { Announcements = s.Announcements.Where(a => a.Id != id).ToList() });
    }

    private static long? MaxOfNotNull(long? a, long? b) => a == null ? b : b == null ? a : Math.Max(a.Value, b.Value);

    private sealed record Account(Person Person, IReadOnlyList<CurrentTask> Tasks, IReadOnlyList<Flight> Flights, IReadOnlyList<Announcement> Announcements);

    private static async Task<T> OrDefault<T>(Func<Task<T>> call, T fallback)
    {
        try { return await call(); }
        catch (Exception) { return fallback; }
    }

    private async Task<IReadOnlyList<Flight>> NextFlightsAsync() =>
        (await _api.MyFutureFlightsAsync()).OrderBy(f => f.TakeoffTime, StringComparer.Ordinal).Take(3).ToList();

    /// <summary>Everything Home needs from the server, fetched at the same time (not one request after the other).</summary>
    private async Task<Account> FetchAccountAsync()
    {
        var person = _api.ProfileAsync();
        var tasks = OrDefault(_api.CurrentTasksAsync, []);
        var flights = OrDefault(NextFlightsAsync, []);
        var announcements = OrDefault(_api.AnnouncementsAsync, []);
        await Task.WhenAll(tasks, flights, announcements);
        return new Account(await person, await tasks, await flights, await announcements);
    }

    public async Task RefreshAsync()
    {
        // The version check, the profile, the current tasks and the next flights all go out together.
        var configLoad = OrDefault<MobileConfig?>(async () => await _api.MobileConfigAsync(), null);
        var signedIn = _auth.IsSignedIn();
        var accountLoad = signedIn && _auth.IsEmailVerified() ? FetchAccountAsync() : null;
        // Offline or a failing config endpoint must never lock a pilot out, so errors are ignored here.
        var config = (await configLoad)?.ForPlatform(_device.Platform);
        if (config != null && _device.AppVersion < config.MinVersion)
        {
            _ = accountLoad?.ContinueWith(t => t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            Update(s => s with { Screen = Screen.UpdateRequired, UpdateUrl = config.StoreUrl });
            return;
        }
        Update(s => s with { Email = _auth.Email() ?? "" });
        if (!signedIn)
        {
            if (_settings != null) _settings.CachedProfileJson = null;
            Update(s => s with { Screen = Screen.Login, Person = null });
            return;
        }
        if (accountLoad == null)
        {
            Update(s => s with { Screen = Screen.VerifyEmail, Error = null });
            return;
        }
        Account account;
        try
        {
            account = await accountLoad;
        }
        catch (Exception e)
        {
            Update(s => s with { Screen = s.Person == null ? Screen.Home : s.Screen, Error = $"Could not reach Airsports: {e.Message}", HomeLoaded = true });
            return;
        }
        var person = account.Person;
        if (!person.Validated)
        {
            Update(s => s with { Screen = Screen.Onboarding, Person = person, Error = null });
            return;
        }
        if (_settings != null) _settings.CachedProfileJson = AirsportsJson.Serialize(person);
        Update(s => s with
        {
            // A refresh while the pilot is elsewhere in the app (Refresh on Home, a cancelled flight) leaves them there.
            Screen = s.IsOutside ? Screen.Home : s.Screen,
            Person = person, Tasks = account.Tasks, Error = null, HomeLoaded = true,
            LastWindowEndMillis = MaxOfNotNull(s.LastWindowEndMillis, WindowClosed.LatestWindowEnd(account.Tasks)),
            Announcements = Undismissed(account.Announcements),
        });
        NotifyNew(Undismissed(account.Announcements));
        var link = _pendingLink;
        if (link != null)
        {
            _pendingLink = null;
            OpenPath(link); // a page asked for before sign-in first: the tour waits for the next start
        }
        else if (_settings is { TourSeen: false } && State.Screen == Screen.Home)
        {
            Update(s => s with { Screen = Screen.Tour });
        }
        await LoadHomeExtrasAsync(account.Tasks, account.Flights);
    }

    private async Task RunAsync(Func<Task> block)
    {
        Update(s => s with { Busy = true, Error = null, Notice = null });
        try
        {
            await block();
            Update(s => s with { Busy = false });
        }
        catch (Exception e)
        {
            Update(s => s with { Busy = false, Error = _auth.DescribeError(e) });
        }
    }

    public Task SignInWithPasswordAsync(string email, string password) => RunAsync(async () =>
    {
        await _auth.SignInWithPasswordAsync(email, password);
        await RefreshAsync();
    });

    public Task CreateAccountAsync(string email, string password) => RunAsync(async () =>
    {
        await _auth.CreateAccountAsync(email, password);
        await RefreshAsync();
    });

    public Task ResetPasswordAsync(string email) => RunAsync(async () =>
    {
        await _auth.SendPasswordResetAsync(email);
        Update(s => s with { Notice = $"Password reset email sent to {email}." });
    });

    public Task ResendVerificationAsync() => RunAsync(async () =>
    {
        await _auth.ResendVerificationAsync();
        Update(s => s with { Notice = "Verification email sent again." });
    });

    public Task CheckVerifiedAsync() => RunAsync(async () =>
    {
        if (await _auth.CheckVerifiedAsync()) await RefreshAsync();
        else Update(s => s with { Error = "Your email is not verified yet. Open the link in the email, then try again." });
    });

    /// <summary>First-start profile completion: saves, then reloads (which leads on to Home once the profile is validated).</summary>
    public async Task SaveProfileAsync(PersonUpdate update)
    {
        Update(s => s with { Busy = true, Error = null });
        try
        {
            await _api.UpdateProfileAsync(update);
            Update(s => s with { Busy = false });
            await RefreshAsync();
        }
        catch (Exception e)
        {
            Update(s => s with { Busy = false, Error = e.Message });
        }
    }

    /// <summary>
    /// Opens a live-map/results link in the web view. <paramref name="link"/> may be a relative path or an airsports.no URL; a
    /// link to anywhere else is ignored rather than silently showing the dashboard. Null opens the dashboard.
    /// </summary>
    public void OpenMap(string? link)
    {
        var path = link == null ? "/" : Urls.WebPath(link);
        if (path != null) OpenPath(path);
    }

    /// <summary>
    /// A web page asked something of the app. After registering a flight the pilot is returned to Home (not the website's
    /// live map) and the upcoming flights and the flight strip are refreshed to include the registration.
    /// </summary>
    public async Task OnAppEventAsync(AppEvent appEvent)
    {
        if (appEvent is not AppEvent.FlightRegistered registered) return;
        Update(s => s with { Screen = Screen.Home, Flights = [], HomeNotice = null });
        await RefreshAsync();
        // The note is redundant when the registered flight is already the one Home shows (open now, or the next flight).
        Update(s => s.Screen == Screen.Home && !IsShownOnHome(s, registered.NavigationTaskId)
            ? s with { HomeNotice = "Flight registered. It is listed under your next flights." }
            : s);
    }

    /// <summary>Home's flight strip shows the flights open now, or else the soonest upcoming one. Unknown task: not known to be shown.</summary>
    private static bool IsShownOnHome(UiState state, int? navigationTaskId)
    {
        if (navigationTaskId == null) return false;
        if (state.Tasks.Count > 0) return state.Tasks.Any(t => t.NavigationTask.Id == navigationTaskId);
        var next = state.Upcoming.FirstOrDefault();
        if (next == null) return false;
        return (next.Flight?.NavigationTask ?? next.Task?.Pk) == navigationTaskId;
    }

    /// <summary>Tracking stopped itself after the longest allowed time: tell the pilot, then do what a manual stop does.</summary>
    public Task TrackingAutoStoppedAsync()
    {
        Update(s => s with
        {
            HomeNotice = "Tracking was stopped automatically after 12 hours. Start it again if you are still flying.",
            Screen = s.Screen is Screen.Timer or Screen.Observations ? Screen.Home : s.Screen,
        });
        return TrackingStoppedAsync();
    }

    public void DismissHomeNotice() => Update(s => s with { HomeNotice = null });

    /// <summary>Opens the tasks open for registration today, nearest the simulator aircraft first when its position is known.</summary>
    public void OpenToday()
    {
        Coordinates? here = null;
        try { here = _location?.Current(); } catch (Exception) { }
        OpenPath(OpenTodayPage.Path(here));
    }

    /// <summary>
    /// Opens <paramref name="path"/> (a page on the site) in the web view. Before sign-in it is kept and opened as soon as the
    /// pilot reaches Home.
    /// </summary>
    public void OpenLink(string path)
    {
        var s = State;
        if (s.Person != null && !s.IsOutside) OpenPath(path);
        else _pendingLink = path;
    }

    public void OpenPath(string path) =>
        Update(s => s with { Screen = Screen.Map, MapPath = path, MapReturn = s.Screen == Screen.Flights ? Screen.Flights : Screen.Home });

    public void CloseMap() => Update(s => s with { Screen = s.MapReturn });

    public Task OpenFlightsAsync()
    {
        Update(s => s with { Screen = Screen.Flights, ShowPastFlights = false });
        return LoadFlightsAsync(past: false);
    }

    public Task ShowPastFlightsAsync(bool past)
    {
        Update(s => s with { ShowPastFlights = past, FlightsNotice = null });
        return LoadFlightsAsync(past);
    }

    /// <summary>The start timer exists only for adaptive-start tasks; it is ignored (not an error) for any other task.</summary>
    public Task OpenTimerAsync()
    {
        if (!State.Tasks.Any(t => t.ActiveContestants.Any(c => c.AdaptiveStart))) return Task.CompletedTask;
        Update(s => s with { Screen = Screen.Timer });
        return MeasureNetworkClockAsync();
    }

    /// <summary>Compares the PC clock with network time so the timer and pre-flight can warn about a wrong clock. Never blocks the UI.</summary>
    public async Task MeasureNetworkClockAsync()
    {
        if (_networkClock == null) return;
        NtpMeasurement? m;
        try { m = await _networkClock.MeasureAsync(); }
        catch (Exception) { return; }
        if (m == null) return;
        Update(s => s with { NetworkClockOffsetMillis = m.OffsetMillis, NetworkClockUncertaintyMillis = m.DelayMillis / 2 });
    }

    /// <summary>Start pressed at <paramref name="nowMillis"/>: the timer counts from the nearest whole minute, before or after.</summary>
    public void StartTimer(long nowMillis) => Update(s => s with { TimerOriginMillis = AdaptiveStartTimer.OriginFor(nowMillis, s.TimerBufferMinutes) });

    /// <summary>Ends the first-start tour (finished or skipped) and goes to Home; it is not shown again by itself.</summary>
    public void FinishTour()
    {
        if (_settings != null) _settings.TourSeen = true;
        Update(s => s with { Screen = Screen.Home });
    }

    /// <summary>Shows the tour again (from the profile).</summary>
    public void OpenTour() => Update(s => s with { Screen = Screen.Tour });

    public void SetTimerBufferMinutes(int minutes)
    {
        var value = Math.Clamp(minutes, 0, AppSettings.MaxTimerBufferMinutes);
        if (_settings != null) _settings.TimerBufferMinutes = value;
        Update(s => s with { TimerBufferMinutes = value });
    }

    public void ResetTimer() => Update(s => s with { TimerOriginMillis = null });

    public void CloseTimer() => Update(s => s with { Screen = Screen.Home });

    /// <summary>Cancels one of the pilot's own flights, then reloads the list and Home's flight strip.</summary>
    public async Task CancelFlightAsync(MyFlight flight)
    {
        var f = flight.Flight;
        if (f == null) return;
        Update(s => s with { CancellingFlightId = flight.Id, FlightsNotice = null, Error = null });
        try
        {
            await _api.CancelFlightAsync(f.ContestId, f.NavigationTask, f.Id);
            // From My flights the note shows there; from the Home strip it shows on Home.
            Update(s => s with { CancellingFlightId = null, FlightsNotice = "Flight cancelled.", HomeNotice = s.Screen == Screen.Home ? "Flight cancelled." : s.HomeNotice });
            if (State.Screen == Screen.Flights) await LoadFlightsAsync(past: false);
            // A cancelled flight in progress is over: the strip moves on to the next task, the next-flight strip too.
            await RefreshTasksAsync();
            await LoadHomeExtrasAsync(State.Tasks);
        }
        catch (ApiException e)
        {
            Update(s => s with { CancellingFlightId = null, Error = $"Could not cancel the flight: {e.Detail}" });
        }
        catch (Exception e)
        {
            Update(s => s with { CancellingFlightId = null, Error = $"Could not cancel the flight: {e.Message}" });
        }
    }

    public void CloseFlights() => Update(s => s with { Screen = Screen.Home, Flights = [] });

    private async Task LoadFlightsAsync(bool past)
    {
        Update(s => s with { FlightsLoading = true, Error = null, Flights = [] });
        try
        {
            var flights = past ? await _api.MyPreviousFlightsAsync() : await _api.MyFutureFlightsAsync();
            var contests = await _api.ContestsByIdsAsync(flights.Select(f => f.ContestId).Distinct().ToList());
            var rows = Urls.JoinFlights(flights, contests);
            var sorted = past
                ? rows.OrderByDescending(r => r.TakeoffTime, StringComparer.Ordinal).ToList()
                : rows.OrderBy(r => r.TakeoffTime, StringComparer.Ordinal).ToList();
            // Ignore a slow response for the tab the pilot has already left.
            Update(s => s.ShowPastFlights == past ? s with { Flights = sorted, FlightsLoading = false } : s);
        }
        catch (Exception e)
        {
            Update(s => s with { FlightsLoading = false, Error = $"Could not load flights: {e.Message}" });
        }
    }

    // ---- Photo observations -------------------------------------------------------------------------------------

    /// <summary>The active flight whose task lets the crew mark photos in the app, if any.</summary>
    private static ActiveContestant? ObservationContestant(IEnumerable<CurrentTask> tasks) =>
        tasks.SelectMany(t => t.ActiveContestants).FirstOrDefault(c => c.ObservationRegistration == true);

    public Task OpenObservationsAsync()
    {
        var contestant = ObservationContestant(State.Tasks);
        if (contestant == null) return Task.CompletedTask;
        Update(s => s with
        {
            Screen = Screen.Observations,
            Observations = new ObservationsUi { ContestantId = contestant.Id, Loading = true, Thumbnails = s.Observations.Thumbnails },
        });
        return LoadObservationsAsync();
    }

    public void CloseObservations() => Update(s => s with { Screen = Screen.Home });

    public void ShowObservationThumbnails(bool thumbnails) => Update(s => s with { Observations = s.Observations with { Thumbnails = thumbnails } });

    public async Task LoadObservationsAsync()
    {
        var id = State.Observations.ContestantId;
        if (id == null) return;
        Update(s => s with { Observations = s.Observations with { Loading = true, Error = null } });
        try
        {
            var data = await _api.ObservationPhotosAsync(id.Value);
            Update(s => s with
            {
                Observations = s.Observations with
                {
                    Loading = false, Enabled = data.Enabled, MayRegister = data.MayRegister, Photos = data.Photos,
                    RegistrationClosesAt = data.RegistrationClosesAt, GraceMinutes = data.RegistrationGraceMinutes ?? 5,
                    Registered = data.Observations.GroupBy(o => o.Photo).ToImmutableDictionaryLast(), Pending = PendingPhotoIds(id.Value),
                },
            });
        }
        catch (Exception e)
        {
            Update(s => s with { Observations = s.Observations with { Loading = false, Error = $"Could not load the photos: {ErrorText(e)}" } });
        }
        await FlushObservationsAsync();
    }

    /// <summary>The crew marks <paramref name="photo"/> now: it is stamped with the latest simulator position, kept on this PC and sent as soon as possible.</summary>
    public Task MarkObservationAsync(ObservationPhoto photo, ObservationFix fix)
    {
        var id = State.Observations.ContestantId;
        if (id == null || _observationQueue == null) return Task.CompletedTask;
        _observationQueue.Add(id.Value, photo.Id, IsoTime.Format(fix.TimeMillis), fix.Latitude, fix.Longitude);
        Update(s => s with { Observations = s.Observations with { Pending = PendingPhotoIds(id.Value), Error = null } });
        return FlushObservationsAsync();
    }

    /// <summary>
    /// Cancels the mark of <paramref name="photo"/>: a mark still waiting on this PC is just forgotten; one the server has
    /// stored is removed there too (needs a connection).
    /// </summary>
    public async Task CancelObservationAsync(ObservationPhoto photo)
    {
        var ui = State.Observations;
        if (ui.ContestantId is not { } id) return;
        await _observationFlush.WaitAsync();
        try { _observationQueue?.RemoveForPhoto(id, photo.Id); }
        finally { _observationFlush.Release(); }
        if (ui.Registered.TryGetValue(photo.Id, out var stored))
        {
            try
            {
                await _api.CancelObservationAsync(id, stored.Id);
            }
            catch (Exception e)
            {
                Update(s => s with { Observations = s.Observations with { Pending = PendingPhotoIds(id), Error = $"Could not cancel {photo.Name}: {ErrorText(e)}" } });
                return;
            }
        }
        Update(s => s with { Observations = s.Observations with { Registered = s.Observations.Registered.Remove(photo.Id), Pending = PendingPhotoIds(id), Error = null } });
    }

    /// <summary>
    /// The crew gives the time they saw <paramref name="photo"/>; the server finds where they were from the position reports
    /// of that time, so this needs a connection. It replaces the earlier mark, also when the new time is earlier.
    /// </summary>
    public async Task MarkObservationAtTimeAsync(ObservationPhoto photo, long timeMillis)
    {
        if (State.Observations.ContestantId is not { } id) return;
        Update(s => s with { Observations = s.Observations with { Error = null } });
        try
        {
            var stored = await _api.RegisterObservationAsync(id, new PhotoObservationRequest { Photo = photo.Id, Time = IsoTime.Format(timeMillis), Replace = true });
            await _observationFlush.WaitAsync();
            try { _observationQueue?.RemoveForPhoto(id, photo.Id); }
            finally { _observationFlush.Release(); }
            Update(s => s with { Observations = s.Observations with { Registered = s.Observations.Registered.SetItem(photo.Id, stored), Pending = PendingPhotoIds(id) } });
        }
        catch (Exception e)
        {
            Update(s => s with { Observations = s.Observations with { Error = $"{photo.Name}: {ErrorText(e)}" } });
        }
    }

    private static string ErrorText(Exception e) => e is ApiException { Detail: var d } && !string.IsNullOrWhiteSpace(d) ? d : e.Message;

    /// <summary>Sends what is waiting. Also called while tracking, so marks made while offline go out once there is a connection.</summary>
    public async Task FlushObservationsAsync()
    {
        if (_observationSender == null || _observationQueue == null) return;
        await _observationFlush.WaitAsync();
        try
        {
            if (_observationQueue.All().Count == 0) return;
            var result = await _observationSender.FlushAsync();
            Update(st =>
            {
                var ui = st.Observations;
                var names = ui.Photos.ToDictionary(p => p.Id, p => p.Name);
                var problem = result.Rejected.Count > 0
                    ? $"{names.GetValueOrDefault(result.Rejected[0].PhotoId) ?? $"Photo {result.Rejected[0].PhotoId}"} was not registered: {result.Rejected[0].Reason}"
                    : null;
                var registered = ui.Registered;
                foreach (var o in result.Sent) registered = registered.SetItem(o.Photo, o);
                return st with
                {
                    Observations = ui with
                    {
                        Registered = registered,
                        Pending = ui.ContestantId is { } id ? PendingPhotoIds(id) : ui.Pending,
                        Error = problem ?? ui.Error,
                    },
                };
            });
        }
        finally
        {
            _observationFlush.Release();
        }
    }

    private System.Collections.Immutable.ImmutableHashSet<int> PendingPhotoIds(int contestantId) =>
        (_observationQueue?.All() ?? []).Where(o => o.ContestantId == contestantId).Select(o => (int)o.PhotoId).ToImmutableHashSet();

    /// <summary>Lightweight refresh of just the flight strip (used while tracking to show whether the server receives positions).</summary>
    public async Task RefreshTasksAsync()
    {
        _ = FlushObservationsAsync();
        IReadOnlyList<CurrentTask> tasks;
        try { tasks = await _api.CurrentTasksAsync(); }
        catch (Exception) { return; }
        Update(s =>
        {
            var stillAdaptive = tasks.Any(t => t.ActiveContestants.Any(c => c.AdaptiveStart));
            return s with
            {
                Tasks = tasks,
                LastWindowEndMillis = MaxOfNotNull(s.LastWindowEndMillis, WindowClosed.LatestWindowEnd(tasks)),
                TimerOriginMillis = stillAdaptive ? s.TimerOriginMillis : null,
                Screen = s.Screen == Screen.Timer && !stillAdaptive ? Screen.Home : s.Screen,
            };
        });
    }

    /// <summary>Deletes the account on the server, then signs out locally. The server's text is shown if it refuses.</summary>
    public async Task DeleteAccountAsync()
    {
        var email = _auth.Email();
        if (email == null)
        {
            Update(s => s with { Error = "Not signed in." });
            return;
        }
        Update(s => s with { Busy = true, Error = null });
        try
        {
            await _api.DeleteAccountAsync(email);
            if (_settings != null) _settings.CachedProfileJson = null;
            _auth.SignOut();
            ForgetAnnouncements();
            Update(_ => new UiState { Screen = Screen.Login, Notice = "Your account has been deleted." });
        }
        catch (ApiException e)
        {
            Update(s => s with { Busy = false, Error = e.Detail });
        }
        catch (Exception e)
        {
            Update(s => s with { Busy = false, Error = $"Could not delete the account: {e.Message}" });
        }
    }

    /// <summary>Best effort: the next flights and the contest details for Home's flight strip.</summary>
    private async Task LoadHomeExtrasAsync(IReadOnlyList<CurrentTask> tasks, IReadOnlyList<Flight>? knownFlights = null)
    {
        var flights = knownFlights ?? await OrDefault(NextFlightsAsync, []);
        var ids = tasks.SelectMany(t => t.ActiveContestants.Select(c => c.ContestId)).Where(i => i != null).Select(i => i!.Value)
            .Concat(flights.Select(f => f.ContestId)).Distinct().ToList();
        var contests = await OrDefault(() => _api.ContestsByIdsAsync(ids), []);
        var byId = new Dictionary<int, ContestSummary>();
        foreach (var c in contests) byId.TryAdd(c.Id, c);
        Update(s => s with { Upcoming = Urls.JoinFlights(flights, contests), Contests = byId });
    }

    public void OpenProfile() => Update(s => s with { Screen = Screen.Profile, ProfileNotice = null, Error = null });

    /// <summary>
    /// Called when the pilot has stopped tracking. If the positions were matched to a navigation task (the server has
    /// received positions, or its calculator ran), remember that flight so the app can offer to show it.
    /// </summary>
    public async Task TrackingStoppedAsync()
    {
        // The start timer runs for as long as tracking does, so ending tracking ends it too.
        Update(s => s with { TimerOriginMillis = null, LastWindowEndMillis = null });
        var fresh = await OrDefault<IReadOnlyList<CurrentTask>?>(async () => await _api.CurrentTasksAsync(), null);
        var tasks = fresh is { Count: > 0 } ? fresh : State.Tasks;
        var match = tasks.SelectMany(t => t.ActiveContestants.Select(c => (Task: t, Contestant: c)))
            .FirstOrDefault(p => p.Contestant.LastPositionTime != null || p.Contestant.LastReceivedTime != null
                || p.Contestant.CalculatorRunning == true || p.Contestant.CalculatorFinished == true);
        if (match.Task == null) return;
        var (task, contestant) = match;
        var contest = contestant.ContestId is { } cid ? State.Contests.GetValueOrDefault(cid) : null;
        Update(s => s with
        {
            FinishedFlight = new FinishedFlight(
                ContestName: Urls.NotBlank(contest?.Name),
                TaskName: task.NavigationTask.Name ?? $"Task {task.NavigationTask.Id}",
                MapPath: contestant.MapPath ?? (task.NavigationTask.TrackingLink is { } l ? Urls.WebPath(l) : null),
                Viewable: contestant.MapViewable,
                Score: contestant.Score),
        });
    }

    public Task OpenPreflightAsync()
    {
        Update(s => s with { Screen = Screen.Preflight });
        return MeasureNetworkClockAsync();
    }

    public void OpenHistory() => Update(s => s with { Screen = Screen.History });

    /// <summary>The hidden debug view (opened by clicking the app version seven times).</summary>
    public void OpenDebug() => Update(s => s with { Screen = Screen.Debug });

    public void CloseDebug() => Update(s => s with { Screen = Screen.Home });

    public void CloseHistory() => Update(s => s with { Screen = Screen.Home });

    public void ClosePreflight() => Update(s => s with { Screen = Screen.Home });

    public void SetTrainingMode(bool on) => Update(s => s with { TrainingMode = on });

    public void DismissFinishedFlight() => Update(s => s with { FinishedFlight = null });

    public void ViewFinishedFlight()
    {
        var path = State.FinishedFlight?.MapPath;
        DismissFinishedFlight();
        if (path != null) OpenPath(path);
    }

    public void CloseProfile() => Update(s => s with { Screen = Screen.Home, ProfileNotice = null });

    /// <summary>Saves name and biography and stays on the profile screen.</summary>
    public async Task UpdateProfileAsync(PersonUpdate update)
    {
        Update(s => s with { ProfileSaving = true, Error = null, ProfileNotice = null });
        try
        {
            var person = await _api.UpdateProfileAsync(update);
            Update(s => s with { ProfileSaving = false, Person = person, ProfileNotice = "Profile saved." });
        }
        catch (Exception e)
        {
            Update(s => s with { ProfileSaving = false, Error = $"Could not save the profile: {e.Message}" });
        }
    }

    /// <summary>Uploads a new profile photo (<paramref name="jpeg"/> bytes, already resized) and shows it.</summary>
    public async Task UploadPhotoAsync(byte[] jpeg)
    {
        Update(s => s with { ProfileSaving = true, Error = null, ProfileNotice = null });
        try
        {
            var person = await _api.UploadProfilePictureAsync(jpeg);
            Update(s => s with { ProfileSaving = false, Person = person, ProfileNotice = "Photo updated." });
        }
        catch (Exception e)
        {
            Update(s => s with { ProfileSaving = false, Error = $"Could not upload the photo: {e.Message}" });
        }
    }

    /// <summary>Signs out at once (the UI never waits on the network).</summary>
    public void SignOut()
    {
        if (_settings != null) _settings.CachedProfileJson = null;
        _auth.SignOut();
        ForgetAnnouncements();
        Update(_ => new UiState { Screen = Screen.Login });
    }

    /// <summary>The token the web view sends (in a header, never in the URL) to sign the pilot in on the website.</summary>
    public async Task<string?> IdTokenForWebAsync()
    {
        try { return await _auth.Tokens.IdTokenAsync(forceRefresh: false); }
        catch (Exception) { return null; }
    }

    /// <summary>The time "now" on the controller's clock (tests replace it).</summary>
    public long NowMillis() => _now();
}

internal static class CollectionExtensions
{
    /// <summary>Photo id -> its observation; the last one wins when the server lists a photo twice.</summary>
    public static System.Collections.Immutable.ImmutableDictionary<int, PhotoObservation> ToImmutableDictionaryLast(this IEnumerable<IGrouping<int, PhotoObservation>> groups) =>
        System.Collections.Immutable.ImmutableDictionary.CreateRange(groups.Select(g => KeyValuePair.Create(g.Key, g.Last())));
}
