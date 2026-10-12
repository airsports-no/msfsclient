using System.Net;
using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.Data;
using msfsclient.Core.Observations;
using msfsclient.Core.Tracking;
using static msfsclient.Core.Tests.Api;
using static msfsclient.Core.Tests.MockHandler;

namespace msfsclient.Core.Tests;

/// <summary>Port of AppControllerTest (push, subscription and location-permission cases have no place on the simulator).</summary>
public class AppControllerTest
{
    private HttpStatusCode _deleteStatus = HttpStatusCode.NoContent;
    private readonly List<string> _profileRequests = [];
    private int _futureFlightRequests;
    private bool _flightCancelled;
    private HttpStatusCode _cancelStatus = HttpStatusCode.NoContent;
    private readonly List<string> _cancelRequests = [];
    private string? _configJson;

    private static readonly DeviceInfo Device = new("msfs", 30, "2.0.0");

    private AirsportsApi Server(bool validated, string tasks = "[]") => With(new MockHandler(r =>
    {
        var path = r.Path;
        if (path.Contains("retrieve_profile"))
            return Json($$"""{"email":"pilot@example.com","validated":{{(validated ? "true" : "false")}},"simulator_tracking_id":"sim1"}""");
        if (path.Contains("get_current_sim_navigation_task"))
            return tasks == "[]" ? Status(HttpStatusCode.NotFound) : Json(tasks);
        if (path.Contains("mobile/config"))
            return _configJson != null ? Json(_configJson) : Status(HttpStatusCode.NotFound);
        if (path.Contains("delete_self_managed_contestant"))
        {
            lock (_cancelRequests) _cancelRequests.Add(r.Method + " " + path);
            if ((int)_cancelStatus < 300)
            {
                _flightCancelled = true;
                return Status(_cancelStatus);
            }
            return Json("""{"detail":"You cannot delete a contestant that is not your own"}""", _cancelStatus);
        }
        if (path.Contains("my_future_flights"))
        {
            Interlocked.Increment(ref _futureFlightRequests);
            return Json(_flightCancelled ? "[]" : """[{"id":1,"contest_id":10,"navigation_task":100,"takeoff_time":"2026-10-10T09:00:00Z","tracker_start_time":"2099-01-01T00:00:00Z"}]""");
        }
        if (path.EndsWith("/contests/"))
            return Json("""{"results":[{"id":10,"name":"Cup","navigationtask_set":[{"pk":100,"name":"Day 1","tracking_link":"https://app.airsports.no/competition-map/10/100","allow_self_management":true}]}],"next":null}""");
        if (path.Contains("partial_update_profile"))
        {
            lock (_profileRequests) _profileRequests.Add(r.Method + " " + r.ContentType);
            return Json("""{"email":"pilot@example.com","validated":true,"first_name":"New","last_name":"Name","biography":"Hi","picture":"https://x/p.jpg","simulator_tracking_id":"sim1"}""");
        }
        if (path.Contains("delete_account"))
            return (int)_deleteStatus < 300 ? Status(_deleteStatus) : Json("""{"detail":"Contact support."}""", _deleteStatus);
        if (path.Contains("announcements")) return Json("[]");
        return Status(HttpStatusCode.NotFound);
    }));

    private static async Task<AppController> Started(AirsportsApi api, FakeAuth auth, DeviceInfo? device = null, INetworkClock? clock = null)
    {
        var c = new AppController(api, auth, device ?? Device, networkClock: clock);
        await c.Start();
        return c;
    }

    [Fact]
    public async Task SignedOutShowsLogin() => Assert.Equal(Screen.Login, (await Started(Server(true), new FakeAuth(signedIn: false))).State.Screen);

    [Fact]
    public async Task UnverifiedEmailShowsVerifyScreenWithAddress()
    {
        var s = (await Started(Server(true), new FakeAuth(verified: false))).State;
        Assert.Equal(Screen.VerifyEmail, s.Screen);
        Assert.Equal("pilot@example.com", s.Email);
    }

    [Fact]
    public async Task UnvalidatedProfileShowsOnboarding() => Assert.Equal(Screen.Onboarding, (await Started(Server(false), new FakeAuth())).State.Screen);

    [Fact]
    public async Task ValidatedProfileShowsHomeWithTasks()
    {
        var s = (await Started(Server(true, """[{"navigation_task":{"pk":7,"name":"Day 1"},"active_contestants":[{"id":1}]}]"""), new FakeAuth())).State;
        Assert.Equal(Screen.Home, s.Screen);
        Assert.Equal(7, s.Tasks.Single().NavigationTask.Id);
        Assert.Equal("sim1", s.Person?.SimulatorTrackingId);
    }

    [Fact]
    public async Task SignInErrorIsShownWithFriendlyText()
    {
        var auth = new FakeAuth(signedIn: false) { FailWith = new InvalidOperationException("bad password") };
        var c = await Started(Server(true), auth);
        await c.SignInWithPasswordAsync("a@b.no", "x");
        Assert.Equal("nice: bad password", c.State.Error);
        Assert.False(c.State.Busy);
    }

    [Fact]
    public async Task CreatingAnAccountLeadsToTheVerifyEmailScreen()
    {
        var c = await Started(Server(true), new FakeAuth(signedIn: false));
        await c.CreateAccountAsync("a@b.no", "secret1");
        Assert.Equal(Screen.VerifyEmail, c.State.Screen);
        await c.CheckVerifiedAsync();
        Assert.Equal(Screen.Home, c.State.Screen);
    }

    [Fact]
    public async Task ALinkOpenedBeforeSignInOpensOnceThePilotReachesHome()
    {
        var c = await Started(Server(true), new FakeAuth(signedIn: false));
        c.OpenLink("/schedule-flight?contestId=10&navigationTaskId=100");
        Assert.Equal(Screen.Login, c.State.Screen);

        await c.SignInWithPasswordAsync("a@b.no", "x");
        Assert.Equal(Screen.Map, c.State.Screen);
        Assert.Equal("/schedule-flight?contestId=10&navigationTaskId=100", c.State.MapPath);
        Assert.Equal(Screen.Home, c.State.MapReturn);
    }

    [Fact]
    public async Task OpenMapAcceptsOurLinksAndIgnoresOtherSites()
    {
        var c = await Started(Server(true), new FakeAuth());
        c.OpenMap("https://app.airsports.no/competition-map/3/7");
        Assert.Equal("/competition-map/3/7", c.State.MapPath);
        c.CloseMap();
        c.OpenMap("/competition-map/3/8?contestantIds=5"); // a relative path is ours too
        Assert.Equal("/competition-map/3/8?contestantIds=5", c.State.MapPath);
        c.CloseMap();
        // A link to anywhere else must not silently show the dashboard: nothing happens.
        c.OpenMap("https://evil.example.com/x");
        Assert.Equal(Screen.Home, c.State.Screen);
        c.OpenMap(null); // explicit "open the website" is the dashboard
        Assert.Equal("/", c.State.MapPath);
        c.CloseMap();
        Assert.Equal(Screen.Home, c.State.Screen);
    }

    private static string TaskJson(bool adaptive) => $$"""[{"navigation_task":{"pk":7,"name":"Day 1"},"active_contestants":[{"id":1,"adaptive_start":{{(adaptive ? "true" : "false")}}}]}]""";

    [Fact]
    public async Task StartTimerIsUnavailableWithoutAdaptiveStart()
    {
        var c = await Started(Server(true, TaskJson(adaptive: false)), new FakeAuth());
        await c.OpenTimerAsync();
        Assert.Equal(Screen.Home, c.State.Screen);
    }

    [Fact]
    public async Task StartTimerOpensStartsAtTheNearestMinuteAndResets()
    {
        var c = await Started(Server(true, TaskJson(adaptive: true)), new FakeAuth());
        await c.OpenTimerAsync();
        Assert.Equal(Screen.Timer, c.State.Screen);
        Assert.Null(c.State.TimerOriginMillis);

        c.StartTimer(Ms("2026-10-10T10:03:40Z"));
        Assert.Equal(Ms("2026-10-10T10:04:00Z"), c.State.TimerOriginMillis);

        c.CloseTimer(); // leaving the page keeps the running timer
        Assert.Equal(Screen.Home, c.State.Screen);
        Assert.NotNull(c.State.TimerOriginMillis);
        await c.OpenTimerAsync();
        c.ResetTimer();
        Assert.Null(c.State.TimerOriginMillis);
    }

    [Fact]
    public async Task TheTimerBufferIsKeptAndUsed()
    {
        var c = await Started(Server(true, TaskJson(adaptive: true)), new FakeAuth());
        c.SetTimerBufferMinutes(45);
        Assert.Equal(30, c.State.TimerBufferMinutes);
        c.SetTimerBufferMinutes(2);
        c.StartTimer(Ms("2026-10-10T10:03:20Z"));
        Assert.Equal(Ms("2026-10-10T10:05:00Z"), c.State.TimerOriginMillis);
    }

    private sealed class FixedClock(NtpMeasurement? m, bool fail = false) : INetworkClock
    {
        public Task<NtpMeasurement?> MeasureAsync() => fail ? throw new InvalidOperationException("offline") : Task.FromResult(m);
    }

    [Fact]
    public async Task OpeningTheTimerMeasuresTheNetworkClock()
    {
        var c = await Started(Server(true, TaskJson(adaptive: true)), new FakeAuth(), clock: new FixedClock(new NtpMeasurement(-9_000, 80)));
        await c.OpenTimerAsync();
        Assert.Equal(-9_000L, c.State.NetworkClockOffsetMillis);
        Assert.Equal(40L, c.State.NetworkClockUncertaintyMillis);
    }

    [Fact]
    public async Task OpeningPreflightMeasuresTheNetworkClockToo()
    {
        var c = await Started(Server(true), new FakeAuth(), clock: new FixedClock(new NtpMeasurement(3_000, 10)));
        await c.OpenPreflightAsync();
        Assert.Equal(Screen.Preflight, c.State.Screen);
        Assert.Equal(3_000L, c.State.NetworkClockOffsetMillis);
    }

    [Fact]
    public async Task AnUnreachableTimeServerLeavesTheTimerWorking()
    {
        var c = await Started(Server(true, TaskJson(adaptive: true)), new FakeAuth(), clock: new FixedClock(null, fail: true));
        await c.OpenTimerAsync();
        Assert.Equal(Screen.Timer, c.State.Screen);
        Assert.Null(c.State.NetworkClockOffsetMillis);
    }

    [Fact]
    public async Task TheTimerClosesWhenTheAdaptiveFlightIsNoLongerOpen()
    {
        var tasks = TaskJson(adaptive: true);
        var api = With(new MockHandler(r => r.Path.Contains("get_current_sim_navigation_task")
            ? (tasks == "[]" ? Status(HttpStatusCode.NotFound) : Json(tasks))
            : r.Path.Contains("retrieve_profile") ? Json("""{"validated":true,"simulator_tracking_id":"sim1"}""") : Json("[]")));
        var c = await Started(api, new FakeAuth());
        await c.OpenTimerAsync();
        c.StartTimer(Ms("2026-10-10T10:03:40Z"));
        tasks = "[]";
        await c.RefreshTasksAsync();
        Assert.Equal(Screen.Home, c.State.Screen);
        Assert.Null(c.State.TimerOriginMillis);
    }

    [Fact]
    public async Task SavingTheProfileStaysOnTheProfileScreenWithTheNewValues()
    {
        var c = await Started(Server(true), new FakeAuth());
        c.OpenProfile();
        await c.UpdateProfileAsync(new PersonUpdate { FirstName = "New", LastName = "Name", Biography = "Hi" });
        var s = c.State;
        Assert.Equal(Screen.Profile, s.Screen);
        Assert.Equal("New", s.Person?.FirstName);
        Assert.Equal("Hi", s.Person?.Biography);
        Assert.Equal("Profile saved.", s.ProfileNotice);
        Assert.False(s.ProfileSaving);
        c.CloseProfile();
        Assert.Equal(Screen.Home, c.State.Screen);
    }

    [Fact]
    public async Task UploadingAPhotoSendsMultipartAndShowsTheNewPicture()
    {
        var c = await Started(Server(true), new FakeAuth());
        c.OpenProfile();
        await c.UploadPhotoAsync([1, 2, 3]);
        Assert.Equal("https://x/p.jpg", c.State.Person?.Picture);
        Assert.Equal("Photo updated.", c.State.ProfileNotice);
        Assert.Equal("PATCH multipart/form-data", Assert.Single(_profileRequests));
    }

    [Fact]
    public async Task HomeLoadsTheNextFlightsAndContestNames()
    {
        var s = (await Started(Server(true), new FakeAuth())).State;
        Assert.Equal("Cup", s.Upcoming.Single().ContestName);
        Assert.Equal("Day 1", s.Upcoming.Single().TaskName);
        Assert.Equal("Cup", s.Contests[10].Name); // the whole contest is kept for images and details
    }

    private static string FlightJson(bool positions, bool? viewable, bool running = true, string score = "") => $$"""
        [{"navigation_task":{"pk":7,"name":"Day 1","tracking_link":"https://app.airsports.no/competition-map/10/7"},
          "active_contestants":[{"id":55,"contest_id":10,"calculator_running":{{(running ? "true" : "false")}}{{score}},
          "last_position_time":{{(positions ? "\"2026-10-10T10:00:00Z\"" : "null")}},
          "map_path":"/competition-map/10/7?contestantIds=55"{{(viewable != null ? $",\"map_viewable\":{(viewable.Value ? "true" : "false")}" : "")}}}]}]
        """;

    [Fact]
    public async Task StoppingAfterAMatchedFlightOffersToViewIt()
    {
        var c = await Started(Server(true, FlightJson(positions: true, viewable: true, score: ",\"score\":42.5")), new FakeAuth());
        await c.TrackingStoppedAsync();
        var f = c.State.FinishedFlight!;
        Assert.Equal("Day 1", f.TaskName);
        Assert.True(f.Viewable);
        Assert.Equal("/competition-map/10/7?contestantIds=55", f.MapPath);
        Assert.Equal(42.5, f.Score);

        c.ViewFinishedFlight();
        Assert.Equal(Screen.Map, c.State.Screen);
        Assert.Equal("/competition-map/10/7?contestantIds=55", c.State.MapPath);
        Assert.Null(c.State.FinishedFlight); // the offer is shown once
    }

    [Fact]
    public async Task WithoutPermissionThePilotIsToldToWaitForTheResults()
    {
        var c = await Started(Server(true, FlightJson(positions: true, viewable: false)), new FakeAuth());
        await c.TrackingStoppedAsync();
        Assert.False(c.State.FinishedFlight!.Viewable);
        c.DismissFinishedFlight();
        Assert.Null(c.State.FinishedFlight);
        Assert.Equal(Screen.Home, c.State.Screen);
    }

    [Fact]
    public async Task AnOlderBackendThatDoesNotSayStillOffersTheLinkAndTheWebsiteDecides()
    {
        var c = await Started(Server(true, FlightJson(positions: true, viewable: null)), new FakeAuth());
        await c.TrackingStoppedAsync();
        Assert.Null(c.State.FinishedFlight!.Viewable);
    }

    [Fact]
    public async Task NoDialogWhenNoPositionsWereMatchedToATask()
    {
        var c = await Started(Server(true, FlightJson(positions: false, viewable: true, running: false)), new FakeAuth());
        await c.TrackingStoppedAsync();
        Assert.Null(c.State.FinishedFlight);
    }

    [Fact]
    public async Task NoDialogWhenNothingIsScheduled()
    {
        var c = await Started(Server(true), new FakeAuth());
        await c.TrackingStoppedAsync();
        Assert.Null(c.State.FinishedFlight);
    }

    [Fact]
    public async Task StoppingTrackingEndsTheStartTimer()
    {
        var c = await Started(Server(true, TaskJson(adaptive: true)), new FakeAuth());
        await c.OpenTimerAsync();
        c.StartTimer(Ms("2026-10-10T10:03:20Z"));
        Assert.NotNull(c.State.TimerOriginMillis);
        await c.TrackingStoppedAsync();
        Assert.Null(c.State.TimerOriginMillis);
    }

    [Fact]
    public async Task TrackingStoppedByItselfTellsThePilotAndLeavesTheTimer()
    {
        var c = await Started(Server(true, TaskJson(adaptive: true)), new FakeAuth());
        await c.OpenTimerAsync();
        await c.TrackingAutoStoppedAsync();
        Assert.Equal(Screen.Home, c.State.Screen);
        Assert.StartsWith("Tracking was stopped automatically after 12 hours", c.State.HomeNotice);
    }

    [Fact]
    public async Task RegisteringAFlightInTheWebViewReturnsToTheAppHomeAndRefreshes()
    {
        var c = await Started(Server(true), new FakeAuth());
        c.OpenPath("/schedule-flight?contestId=10&navigationTaskId=100");
        Assert.Equal(Screen.Map, c.State.Screen);
        var before = _futureFlightRequests;

        // Task 999 is not the flight Home shows (the next flight is task 100): the note tells where it went.
        await c.OnAppEventAsync(new AppEvent.FlightRegistered(10, 999));

        var s = c.State;
        Assert.Equal(Screen.Home, s.Screen);
        Assert.Equal("Flight registered. It is listed under your next flights.", s.HomeNotice);
        Assert.Equal("Cup", s.Upcoming.Single().ContestName);
        Assert.True(_futureFlightRequests > before); // the upcoming flights were fetched again
        c.DismissHomeNotice();
        Assert.Null(c.State.HomeNotice);
    }

    [Fact]
    public async Task NoNoteWhenTheRegisteredFlightIsAlreadyShownAsTheNextFlight()
    {
        var c = await Started(Server(true), new FakeAuth());
        c.OpenPath("/schedule-flight?contestId=10&navigationTaskId=100");
        await c.OnAppEventAsync(new AppEvent.FlightRegistered(10, 100));
        Assert.Equal(Screen.Home, c.State.Screen);
        Assert.Null(c.State.HomeNotice); // the next-flight strip already shows it
    }

    [Fact]
    public async Task CancellingAnUpcomingFlightRemovesItFromTheListAndTheHomeStrip()
    {
        var c = await Started(Server(true), new FakeAuth());
        await c.OpenFlightsAsync();
        var flight = Assert.Single(c.State.Flights);

        await c.CancelFlightAsync(flight);

        var s = c.State;
        Assert.Equal("Flight cancelled.", s.FlightsNotice);
        Assert.Empty(s.Flights);
        Assert.False(s.FlightsLoading);
        Assert.Null(s.CancellingFlightId);
        Assert.Equal(["DELETE /api/v1/contests/10/navigationtasks/100/delete_self_managed_contestant/1/"], _cancelRequests);
        Assert.Empty(s.Upcoming); // the Home strip no longer shows the cancelled flight
    }

    [Fact]
    public async Task ARefusedCancellationKeepsTheFlightAndExplainsWhy()
    {
        _cancelStatus = HttpStatusCode.Forbidden;
        var c = await Started(Server(true), new FakeAuth());
        await c.OpenFlightsAsync();
        await c.CancelFlightAsync(c.State.Flights.Single());
        var s = c.State;
        Assert.Equal("Could not cancel the flight: You cannot delete a contestant that is not your own", s.Error);
        Assert.Single(s.Flights);
        Assert.Null(s.CancellingFlightId);
    }

    [Fact]
    public async Task PastFlightsAreListedNewestFirst()
    {
        var api = With(new MockHandler(r =>
            r.Path.Contains("my_previous_flights") ? Json("""[{"id":1,"contest_id":10,"navigation_task":100,"takeoff_time":"2026-01-01T09:00:00Z"},{"id":2,"contest_id":10,"navigation_task":100,"takeoff_time":"2026-02-01T09:00:00Z"}]""")
            : r.Path.Contains("retrieve_profile") ? Json("""{"validated":true}""")
            : r.Path.EndsWith("/contests/") ? Json("""{"results":[],"next":null}""")
            : Status(HttpStatusCode.NotFound)));
        var c = await Started(api, new FakeAuth());
        await c.OpenFlightsAsync();
        await c.ShowPastFlightsAsync(true);
        Assert.Equal([2, 1], c.State.Flights.Select(f => f.Id));
    }

    [Fact]
    public async Task SignOutReturnsToLogin()
    {
        var auth = new FakeAuth();
        var c = await Started(Server(true), auth);
        c.SignOut();
        Assert.Equal(Screen.Login, c.State.Screen);
        Assert.False(auth.SignedIn);
    }

    [Fact]
    public async Task DeletingTheAccountSignsOutWithANote()
    {
        var auth = new FakeAuth();
        var c = await Started(Server(true), auth);
        await c.DeleteAccountAsync();
        Assert.Equal(Screen.Login, c.State.Screen);
        Assert.Equal("Your account has been deleted.", c.State.Notice);
        Assert.False(auth.SignedIn);
    }

    [Fact]
    public async Task ARefusedDeletionShowsTheServersExplanation()
    {
        _deleteStatus = HttpStatusCode.Conflict;
        var c = await Started(Server(true), new FakeAuth());
        await c.DeleteAccountAsync();
        Assert.Equal("Contact support.", c.State.Error);
        Assert.Equal(Screen.Home, c.State.Screen);
    }

    [Fact]
    public async Task TooOldAppIsBlockedWithTheStoreLink()
    {
        _configJson = """{"android":{"min_version":1},"msfs":{"min_version":120,"latest_version":130,"store_url":"ms-windows-store://pdp/?productid=x"}}""";
        var s = (await Started(Server(true), new FakeAuth(), new DeviceInfo("msfs", 100))).State;
        Assert.Equal(Screen.UpdateRequired, s.Screen);
        Assert.Equal("ms-windows-store://pdp/?productid=x", s.UpdateUrl);
    }

    [Fact]
    public async Task CurrentAppPassesTheVersionCheck()
    {
        _configJson = """{"android":{"min_version":999},"msfs":{"min_version":100}}""";
        Assert.Equal(Screen.Home, (await Started(Server(true), new FakeAuth(), new DeviceInfo("msfs", 100))).State.Screen);
    }

    [Fact]
    public async Task OtherPlatformsMinimumVersionsDoNotApplyToTheSimulatorClient()
    {
        _configJson = """{"android":{"min_version":999},"ios":{"min_version":999}}""";
        Assert.Equal(Screen.Home, (await Started(Server(true), new FakeAuth(), new DeviceInfo("msfs", 1))).State.Screen);
    }

    [Fact]
    public async Task UnreachableConfigNeverLocksThePilotOut()
    {
        _configJson = null; // endpoint answers 404
        Assert.Equal(Screen.Home, (await Started(Server(true), new FakeAuth(), new DeviceInfo("msfs", 1))).State.Screen);
    }

    [Fact]
    public async Task TheRegisterTodayListIsOpenedWithTheAircraftsPositionInTheFragment()
    {
        var c = new AppController(Server(true), new FakeAuth(), Device, location: new FixedLocation(new Coordinates(59.91389, 10.75224)));
        await c.Start();
        c.OpenToday();
        Assert.Equal(Screen.Map, c.State.Screen);
        Assert.Equal("/open-today#lat=59.914&lon=10.752", c.State.MapPath);
    }

    [Fact]
    public async Task TheRegisterTodayListStillOpensWithoutAPosition()
    {
        foreach (ICurrentLocation? location in new ICurrentLocation?[] { new FixedLocation(null), null })
        {
            var c = new AppController(Server(true), new FakeAuth(), Device, location: location);
            await c.Start();
            c.OpenToday();
            Assert.Equal("/open-today", c.State.MapPath);
        }
    }

    private sealed class FixedLocation(Coordinates? here) : ICurrentLocation
    {
        public Coordinates? Current() => here;
    }
}

/// <summary>Port of CachedStartTest.</summary>
public class CachedStartTest
{
    private static AirsportsApi Server(Task answer) => With(new MockHandler(async r =>
    {
        await answer; // the server answers only when the test lets it
        if (r.Path.Contains("retrieve_profile")) return Json("""{"email":"pilot@example.com","validated":true,"first_name":"Pia","simulator_tracking_id":"sim1"}""");
        if (r.Path.Contains("announcements")) return Json("""[{"id":5,"contest_name":"Cup","message":"Briefing 08:30"}]""");
        if (r.Path.Contains("mobile/config")) return Json("""{"android":{"min_version":1},"ios":{"min_version":1}}""");
        if (r.Path.Contains("get_current_sim_navigation_task")) return Status(HttpStatusCode.NotFound);
        if (r.Path.EndsWith("/contests/")) return Json("""{"results":[],"next":null}""");
        return Json("[]");
    }));

    private static readonly DeviceInfo Device = new("msfs", 30, "2.0.0");

    [Fact]
    public async Task APilotWhoWasSignedInSeesHomeAtOnceFromTheSavedProfileBeforeTheServerAnswers()
    {
        var settings = new AppSettings(AirsportsDatabase.InMemory()) { TourSeen = true };
        settings.CachedProfileJson = """{"email":"pilot@example.com","validated":true,"first_name":"Cached","simulator_tracking_id":"sim1"}""";
        var answer = new TaskCompletionSource();
        var c = new AppController(Server(answer.Task), new FakeAuth(), Device, settings: settings);

        Assert.Equal(Screen.Home, c.State.Screen);
        Assert.Equal("Cached", c.State.Person?.FirstName);
        Assert.False(c.State.HomeLoaded); // Home says it is still checking the flights

        var start = c.Start();
        answer.SetResult(); // now the fresh profile arrives and replaces the saved one
        await start;
        Assert.True(c.State.HomeLoaded);
        Assert.Equal("Pia", c.State.Person?.FirstName);
        Assert.Equal("Pia", AirsportsJson.Deserialize<Person>(settings.CachedProfileJson!)!.FirstName);
    }

    [Fact]
    public async Task NoSavedProfileWhenSignedOutAndTheSavedOneIsDroppedOnSignOut()
    {
        var settings = new AppSettings(AirsportsDatabase.InMemory());
        settings.CachedProfileJson = """{"email":"pilot@example.com","validated":true}""";
        var c = new AppController(Server(Task.CompletedTask), new FakeAuth(signedIn: false), Device, settings: settings);
        await c.Start();
        Assert.Equal(Screen.Login, c.State.Screen);
        Assert.Null(settings.CachedProfileJson);
        Assert.Null(c.State.Person);
    }

    [Fact]
    public async Task TheTourIsShownOnFirstStartOnceAndNotAgainAfterItIsFinished()
    {
        var settings = new AppSettings(AirsportsDatabase.InMemory());
        var c = new AppController(Server(Task.CompletedTask), new FakeAuth(), Device, settings: settings);
        await c.Start();
        Assert.Equal(Screen.Tour, c.State.Screen);

        c.FinishTour();
        Assert.Equal(Screen.Home, c.State.Screen);
        Assert.True(settings.TourSeen);

        var second = new AppController(Server(Task.CompletedTask), new FakeAuth(), Device, settings: settings);
        await second.Start();
        Assert.Equal(Screen.Home, second.State.Screen); // no tour the second time
    }

    [Fact]
    public async Task ALinkGoesBeforeTheTourAndTheTourWaitsForTheNextStart()
    {
        var settings = new AppSettings(AirsportsDatabase.InMemory());
        var answer = new TaskCompletionSource();
        var c = new AppController(Server(answer.Task), new FakeAuth(), Device, settings: settings);
        var start = c.Start();
        c.OpenLink("/schedule-flight?contestId=1&navigationTaskId=2"); // before the profile has loaded: kept until Home
        answer.SetResult();
        await start;

        Assert.Equal(Screen.Map, c.State.Screen);
        Assert.Equal("/schedule-flight?contestId=1&navigationTaskId=2", c.State.MapPath);
        Assert.False(settings.TourSeen); // the tour is still to come
    }

    [Fact]
    public async Task AnAnnouncementShowsUntilItIsDismissedAndStaysHiddenAfterARestart()
    {
        var settings = new AppSettings(AirsportsDatabase.InMemory()) { TourSeen = true };
        var c = new AppController(Server(Task.CompletedTask), new FakeAuth(), Device, settings: settings);
        await c.Start();
        Assert.Equal(["Briefing 08:30"], c.State.Announcements.Select(a => a.Message));

        c.DismissAnnouncement(5);
        Assert.Empty(c.State.Announcements);

        var restarted = new AppController(Server(Task.CompletedTask), new FakeAuth(), Device, settings: settings);
        await restarted.Start();
        Assert.Empty(restarted.State.Announcements);
        await restarted.RefreshAnnouncementsAsync();
        Assert.Empty(restarted.State.Announcements);
    }
}

/// <summary>The desktop has no push: announcements that arrive while the client runs are raised for a Windows notification.</summary>
public class AnnouncementNotificationTest
{
    private string _announcements = """[{"id":1,"contest_name":"Cup","message":"Briefing 08:30"}]""";

    private AirsportsApi Server() => With(new MockHandler(r =>
        r.Path.Contains("announcements") ? Json(_announcements)
        : r.Path.Contains("retrieve_profile") ? Json("""{"validated":true,"simulator_tracking_id":"sim1"}""")
        : r.Path.EndsWith("/contests/") ? Json("""{"results":[],"next":null}""")
        : r.Path.Contains("my_future_flights") ? Json("[]")
        : Status(HttpStatusCode.NotFound)));

    [Fact]
    public async Task OnlyAnnouncementsArrivingAfterStartAreRaisedEachOnce()
    {
        var c = new AppController(Server(), new FakeAuth(), new DeviceInfo("msfs", 30));
        var raised = new List<string>();
        c.NewAnnouncement += a => raised.Add(a.Message);
        await c.Start();
        Assert.Empty(raised); // what is there at start is on Home already

        _announcements = """[{"id":2,"contest_name":"Cup","message":"Start delayed 30 min"},{"id":1,"contest_name":"Cup","message":"Briefing 08:30"}]""";
        await c.RefreshAnnouncementsAsync();
        await c.RefreshAnnouncementsAsync();
        Assert.Equal(["Start delayed 30 min"], raised);
        Assert.Equal(2, c.State.Announcements.Count);
    }

    [Fact]
    public async Task DismissedAnnouncementsAreNotRaisedAndANewPilotIsNotToldAboutOldOnes()
    {
        var settings = new AppSettings(AirsportsDatabase.InMemory()) { TourSeen = true };
        settings.DismissedAnnouncementIds = new HashSet<int> { 3 };
        var auth = new FakeAuth();
        var c = new AppController(Server(), auth, new DeviceInfo("msfs", 30), settings: settings);
        var raised = new List<int>();
        c.NewAnnouncement += a => raised.Add(a.Id);
        await c.Start();

        _announcements = """[{"id":3,"message":"Dismissed before"},{"id":1,"message":"Briefing 08:30"}]""";
        await c.RefreshAnnouncementsAsync();
        Assert.Empty(raised);

        c.SignOut();
        _announcements = """[{"id":4,"message":"Old news for the next pilot"}]""";
        await c.SignInWithPasswordAsync("other@example.com", "secret1");
        Assert.Empty(raised);
    }
}

/// <summary>Port of ObservationControllerTest.</summary>
public class ObservationControllerTest
{
    private readonly List<string> _requests = [];

    private (AppController Controller, ObservationQueue Queue) Controller(Func<bool> online, bool observationRegistration = true, bool storedMark = false)
    {
        var queue = new ObservationQueue(AirsportsDatabase.InMemory());
        var api = With(new MockHandler(r =>
        {
            lock (_requests) _requests.Add($"{r.Method} {r.Path} {r.Body}");
            var path = r.Path;
            if (r.Method == "DELETE") return Status(HttpStatusCode.NoContent);
            if (path.Contains("retrieve_profile")) return Json("""{"email":"pilot@example.com","validated":true,"simulator_tracking_id":"sim1"}""");
            if (path.Contains("get_current_sim_navigation_task"))
                return Json($$"""[{"navigation_task":{"pk":7,"name":"Day 1"},"active_contestants":[{"id":42,"observation_registration":{{(observationRegistration ? "true" : "false")}}}]}]""");
            if (path.Contains("observation_photos"))
                return Json($$"""{"enabled":true,"may_register":true,"photos":[{"id":5,"name":"A","file":"https://x/a.jpg"}],"observations":[{{(storedMark ? """{"id":9,"photo":5,"photo_name":"A","time":"2026-10-10T10:00:00Z"}""" : "")}}]}""");
            if (path.EndsWith("/photo_observations/"))
                return online() ? Json("""{"id":9,"photo":5,"photo_name":"A","time":"2026-10-10T10:00:00Z","status":"ok"}""", HttpStatusCode.Created) : Status(HttpStatusCode.ServiceUnavailable);
            return Status(HttpStatusCode.NotFound);
        }));
        return (new AppController(api, new FakeAuth(), new DeviceInfo("msfs", 30), observationQueue: queue), queue);
    }

    [Fact]
    public async Task MarksAreKeptOnThePcWhileOfflineAndSentWhenTheConnectionReturns()
    {
        var online = false;
        var (c, queue) = Controller(() => online);
        await c.Start();
        await c.OpenObservationsAsync();
        Assert.Equal(Screen.Observations, c.State.Screen);
        Assert.Equal(42, c.State.Observations.ContestantId);

        await c.MarkObservationAsync(c.State.Observations.Photos.Single(), new ObservationFix(59.0, 10.0, 1_786_000_000_000));
        Assert.Contains(5, c.State.Observations.Pending);
        Assert.Empty(c.State.Observations.Registered);
        Assert.Single(queue.All());
        Assert.Equal("1/1", c.State.Observations.Counter);

        online = true;
        await c.FlushObservationsAsync();
        Assert.True(c.State.Observations.Registered.ContainsKey(5));
        Assert.Empty(c.State.Observations.Pending);
        Assert.Empty(queue.All());
    }

    [Fact]
    public async Task ThePhotosPageIsNotOpenedWhenTheTaskDoesNotUseIt()
    {
        var (c, _) = Controller(() => true, observationRegistration: false);
        await c.Start();
        await c.OpenObservationsAsync();
        Assert.Equal(Screen.Home, c.State.Screen);
    }

    [Fact]
    public async Task CancellingAStoredMarkRemovesItOnTheServerToo()
    {
        var (c, _) = Controller(() => true, storedMark: true);
        await c.Start();
        await c.OpenObservationsAsync();
        Assert.True(c.State.Observations.Registered.ContainsKey(5));
        await c.CancelObservationAsync(c.State.Observations.Photos.Single());
        Assert.False(c.State.Observations.Registered.ContainsKey(5));
        Assert.Contains(_requests, r => r.StartsWith("DELETE /api/v1/contestant/42/photo_observations/9/"));
    }

    [Fact]
    public async Task ATypedTimeIsSentWithoutAPositionAndAsACorrection()
    {
        var (c, _) = Controller(() => true);
        await c.Start();
        await c.OpenObservationsAsync();
        await c.MarkObservationAtTimeAsync(c.State.Observations.Photos.Single(), 1_786_000_000_000);
        Assert.Equal(9, c.State.Observations.Registered[5].Id);
        var post = _requests.First(r => r.StartsWith("POST /api/v1/contestant/42/photo_observations/"));
        Assert.Contains("\"replace\":true", post);
        Assert.DoesNotContain("latitude", post);
        Assert.DoesNotContain("longitude", post);
    }
}
