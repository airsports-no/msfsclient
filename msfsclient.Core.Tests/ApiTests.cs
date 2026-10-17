using System.Net;
using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.Tracking;
using static msfsclient.Core.Tests.Api;
using static msfsclient.Core.Tests.MockHandler;

namespace msfsclient.Core.Tests;

// Ports of AirsportsApiTest, AppHeadersTest, FlightsTest and TrackingTargetsTest.

public class AirsportsApiTest
{
    [Fact]
    public async Task SendsJwtHeaderAndParsesProfile()
    {
        string? auth = null;
        var api = With(new MockHandler(r =>
        {
            auth = r.Header("Authorization");
            return Json("""{"email":"a@b.no","validated":true,"simulator_tracking_id":"sim1","app_tracking_id":"abc","unknown_field":1}""");
        }), new FakeTokens());
        var p = await api.ProfileAsync();
        Assert.Equal("JWT stale", auth);
        Assert.Equal("sim1", p.SimulatorTrackingId);
        Assert.Equal("abc", p.AppTrackingId);
        Assert.True(p.Validated);
    }

    [Fact]
    public async Task RefreshesTokenOnceOn403()
    {
        var seen = new List<string?>();
        var tokens = new FakeTokens();
        await With(new MockHandler(r =>
        {
            seen.Add(r.Header("Authorization"));
            return r.Header("Authorization") == "JWT stale" ? Status(HttpStatusCode.Forbidden) : Json("""{"email":"a@b.no"}""");
        }), tokens).ProfileAsync();
        Assert.Equal(["JWT stale", "JWT fresh"], seen);
        Assert.Equal([false, true], tokens.Calls);
    }

    [Fact]
    public async Task RetriesOnlyOnce()
    {
        var calls = 0;
        var e = await Assert.ThrowsAsync<ApiException>(() => With(new MockHandler(_ => { calls++; return Status(HttpStatusCode.Unauthorized); }), new FakeTokens()).ProfileAsync());
        Assert.Equal(401, e.Status);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task NotFoundMeansNoCurrentTask() =>
        Assert.Empty(await With(new MockHandler(_ => Status(HttpStatusCode.NotFound))).CurrentTasksAsync());

    [Fact]
    public async Task TheSimulatorAsksForItsOwnTrackingId()
    {
        string? path = null;
        await With(new MockHandler(r => { path = r.Path; return Status(HttpStatusCode.NotFound); })).CurrentTasksAsync();
        Assert.Equal("/api/v1/userprofile/get_current_sim_navigation_task/", path);
    }

    [Fact]
    public async Task SignedOutThrows() =>
        await Assert.ThrowsAsync<NotSignedInException>(() => With(new MockHandler(_ => Json("{}")), new FakeTokens(signedIn: false)).ProfileAsync());

    [Fact]
    public async Task ParsesCurrentTaskShapeFromBackend()
    {
        // Field names taken from NavigationTasksSummarySerialiser / ContestantSerialiser (summary uses "pk").
        const string body = """
            [{"navigation_task":{"pk":7,"name":"Day 1","tracking_link":"https://app.airsports.no/competition-map/3/7","is_public":true},
              "active_contestants":[{"id":55,"tracker_start_time":"2026-10-10T09:00:00Z","finished_by_time":"2026-10-10T11:00:00Z",
              "last_position_time":null,"calculator_finished":false,"calculator_running":true,"navigation_task":7,"score":17.5,
              "training_feedback":{"last_gate":"TP1","last_gate_time_offset":-3.2},"training_available":true,
              "flight_order_path":"/display/flightorder/1/"}]}]
            """;
        var tasks = await With(new MockHandler(_ => Json(body))).CurrentTasksAsync();
        var task = Assert.Single(tasks);
        Assert.Equal(7, task.NavigationTask.Id);
        Assert.Equal("https://app.airsports.no/competition-map/3/7", task.NavigationTask.TrackingLink);
        var c = Assert.Single(task.ActiveContestants);
        Assert.Equal(55, c.Id);
        Assert.True(c.CalculatorRunning);
        Assert.False(c.CalculatorFinished);
        Assert.Equal(17.5, c.Score);
        Assert.Equal("TP1", c.TrainingFeedback?.LastGate);
        Assert.True(c.TrainingAvailable);
        Assert.Equal("/display/flightorder/1/", c.FlightOrderPath);
    }

    [Fact]
    public async Task DeleteAccountSendsEmailConfirmationAsJsonDelete()
    {
        SeenRequest? seen = null;
        await With(new MockHandler(r => { seen = r; return Status(HttpStatusCode.NoContent); })).DeleteAccountAsync("a@b.no");
        Assert.Equal("DELETE", seen!.Method);
        Assert.Equal("/api/v1/userprofile/delete_account/", seen.Path);
        Assert.Equal("""{"email":"a@b.no"}""", seen.Body);
    }

    [Fact]
    public async Task DeleteAccountSurfacesTheBackendExplanation()
    {
        var e = await Assert.ThrowsAsync<ApiException>(() =>
            With(new MockHandler(_ => Json("""{"detail":"Organizer accounts must be deleted by support."}""", HttpStatusCode.Conflict))).DeleteAccountAsync("a@b.no"));
        Assert.Equal("Organizer accounts must be deleted by support.", e.Detail);
    }

    [Fact]
    public async Task UploadingAPhotoIsAMultipartPatch()
    {
        SeenRequest? seen = null;
        var p = await With(new MockHandler(r => { seen = r; return Json("""{"picture":"https://x/p.jpg"}"""); })).UploadProfilePictureAsync([1, 2, 3]);
        Assert.Equal("PATCH", seen!.Method);
        Assert.Equal("multipart/form-data", seen.ContentType);
        Assert.Equal("https://x/p.jpg", p.Picture);
    }

    [Fact]
    public async Task ThePublicConfigNeedsNoSignInAndHasNoGateUntilTheServerAddsMsfs()
    {
        string? auth = "unset";
        var config = await With(new MockHandler(r =>
        {
            auth = r.Header("Authorization");
            return Json("""{"android":{"min_version":120},"ios":{"min_version":5}}""");
        }), new FakeTokens(signedIn: false)).MobileConfigAsync();
        Assert.Null(auth);
        Assert.Null(config.ForPlatform("msfs"));
        Assert.Equal(120, config.ForPlatform("android")!.MinVersion);
    }
}

public class AppHeadersTest
{
    private static async Task<Dictionary<string, string?>> Call(DeviceInfo? info)
    {
        var seen = new Dictionary<string, string?>();
        await With(new MockHandler(r =>
        {
            foreach (var h in new[] { "X-App-Platform", "X-App-Version", "X-App-Build" }) seen[h] = r.Header(h);
            return Json("""{"email":"a@b.no","validated":true}""");
        }), info: info).ProfileAsync();
        return seen;
    }

    [Fact]
    public async Task EveryAuthorizedRequestCarriesThePlatformVersionAndBuild()
    {
        Assert.Equal(new Dictionary<string, string?> { ["X-App-Platform"] = "msfs", ["X-App-Version"] = "2.0.0", ["X-App-Build"] = "30" }, await Call(new DeviceInfo("msfs", 30, "2.0.0")));
        var noName = await Call(new DeviceInfo("msfs", 7));
        Assert.Null(noName["X-App-Version"]);
        Assert.Equal("7", noName["X-App-Build"]);
        Assert.Equal(new Dictionary<string, string?> { ["X-App-Platform"] = null, ["X-App-Version"] = null, ["X-App-Build"] = null }, await Call(null));
    }
}

public class FlightsTest
{
    [Fact]
    public void JoinsFlightsWithContestAndTaskNames()
    {
        var flights = new[]
        {
            new Flight { Id = 1, ContestId = 10, NavigationTask = 100, TakeoffTime = "2026-10-10T09:00:00Z", DefaultMapUrl = "https://app.airsports.no/competition-map/10/100?contestantIds=1" },
            new Flight { Id = 2, ContestId = 10, NavigationTask = 101, TakeoffTime = "2026-10-11T09:00:00Z" },
            new Flight { Id = 3, ContestId = 99, NavigationTask = 5 },
        };
        var contests = new[]
        {
            new ContestSummary { Id = 10, Name = "Norwegian Cup", NavigationTasks = [new() { Pk = 100, Name = "Day 1", TrackingLink = "x" }, new() { Pk = 101, Name = "Day 2", TrackingLink = "https://app.airsports.no/competition-map/10/101" }] },
        };
        var rows = Urls.JoinFlights(flights, contests);
        Assert.Equal("Norwegian Cup", rows[0].ContestName);
        Assert.Equal("Day 1", rows[0].TaskName);
        // The map for the pilot's OWN flight; default_map_url is the printable route map and must not be used.
        Assert.Equal("/competition-map/10/100?contestantIds=1", rows[0].MapLink);
        Assert.Equal("/competition-map/10/101?contestantIds=2", rows[1].MapLink);
        Assert.Equal("/competition-map/99/5?contestantIds=3", rows[2].MapLink);
        Assert.Equal("Contest 99", rows[2].ContestName); // contest not visible: still shows something
        Assert.Equal("Task 5", rows[2].TaskName);
    }

    [Fact]
    public async Task FollowsContestPagination()
    {
        var urls = new List<Uri>();
        var contests = await With(new MockHandler(r =>
        {
            urls.Add(r.Url);
            return r.Url.Query.Contains("cursor=")
                ? Json("""{"results":[{"id":2,"name":"B"}],"next":null}""")
                : Json("""{"results":[{"id":1,"name":"A"}],"next":"https://airsports.no/api/v1/contests/?cursor=abc"}""");
        })).ContestsByIdsAsync([1, 2]);
        Assert.Equal(["A", "B"], contests.Select(c => c.Name));
        Assert.Equal(2, urls.Count);
        Assert.Contains("pks=1%2C2", urls[0].Query);
    }

    [Fact]
    public async Task PaginationNeverFollowsALinkToAnotherSite()
    {
        var hosts = new List<string>();
        var e = await Assert.ThrowsAsync<ApiException>(() => With(new MockHandler(r =>
        {
            hosts.Add(r.Url.Host);
            return Json("""{"results":[{"id":1,"name":"A"}],"next":"https://evil.example.com/api/v1/contests/?cursor=abc"}""");
        })).ContestsByIdsAsync([1]));
        Assert.Equal(["airsports.no"], hosts); // the token never went to the other host
        Assert.Contains("another site", e.Detail);
    }

    [Fact]
    public async Task NoRequestWhenThereAreNoIds() =>
        Assert.Empty(await With(new MockHandler(new Func<SeenRequest, HttpResponseMessage>(_ => throw new InvalidOperationException("must not be called")))).ContestsByIdsAsync([]));

    private static MyFlight FlightRow(bool selfManaged, string? opens) => Urls.JoinFlights(
        [new Flight { Id = 55, ContestId = 10, NavigationTask = 100, TakeoffTime = "2026-10-10T10:00:00Z", TrackerStartTime = opens }],
        [new ContestSummary { Id = 10, Name = "Cup", NavigationTasks = [new() { Pk = 100, Name = "Day 1", AllowSelfManagement = selfManaged }] }]).Single();

    private static readonly long Noon = Ms("2026-10-10T12:00:00Z");

    [Fact]
    public void AFlightCanBeCancelledInSelfManagedTasksAlsoWhileUnderway()
    {
        Assert.True(FlightRow(true, "2026-10-10T13:00:00Z").CanCancel()); // not started
        Assert.True(FlightRow(true, "2026-10-10T11:00:00Z").CanCancel()); // underway: the server terminates it
        Assert.False(FlightRow(false, "2026-10-10T13:00:00Z").CanCancel()); // organizer-managed task
    }

    [Fact]
    public void TheServersVerdictWinsOverWhatTheVisibleTaskListSays()
    {
        // A private task is missing from the contest's task list (no task to read the flag from), yet the server allows it.
        var hidden = Urls.JoinFlights([new Flight { Id = 56, ContestId = 10, NavigationTask = 101, CanCancel = true }], [new ContestSummary { Id = 10, Name = "Cup" }]).Single();
        Assert.True(hidden.CanCancel());
        var refused = Urls.JoinFlights(
            [new Flight { Id = 57, ContestId = 10, NavigationTask = 100, CanCancel = false }],
            [new ContestSummary { Id = 10, Name = "Cup", NavigationTasks = [new() { Pk = 100, Name = "Day 1", AllowSelfManagement = true }] }]).Single();
        Assert.False(refused.CanCancel());
    }

    [Fact]
    public void AFlightIsUnderwayOnceItsTrackingWindowHasOpened()
    {
        Assert.False(FlightRow(true, "2026-10-10T13:00:00Z").IsUnderway(Noon));
        Assert.True(FlightRow(true, "2026-10-10T11:00:00Z").IsUnderway(Noon));
        Assert.True(FlightRow(true, "2026-10-10T12:00:00Z").IsUnderway(Noon)); // the opening instant counts as underway
        Assert.False(FlightRow(true, null).IsUnderway(Noon)); // unknown window: do not claim it started
        Assert.False(FlightRow(true, "garbage").IsUnderway(Noon));
    }

    [Fact]
    public async Task CancelFlightIsADeleteOnTheSelfManagedContestantUrl()
    {
        SeenRequest? seen = null;
        await With(new MockHandler(r => { seen = r; return Status(HttpStatusCode.NoContent); })).CancelFlightAsync(10, 100, 55);
        Assert.Equal("DELETE", seen!.Method);
        Assert.Equal("/api/v1/contests/10/navigationtasks/100/delete_self_managed_contestant/55/", seen.Path);
    }

    [Fact]
    public async Task CancelFlightSurfacesTheServersReason()
    {
        var e = await Assert.ThrowsAsync<ApiException>(() => With(new MockHandler(_ =>
            Json("""{"detail":"You cannot delete a contestant that is not your own"}""", HttpStatusCode.Forbidden))).CancelFlightAsync(10, 100, 55));
        Assert.Equal("You cannot delete a contestant that is not your own", e.Detail);
    }

    [Fact]
    public void AFlightInAPrivateContestIsNamedFromItsOwnMetadataAndHasNoMap()
    {
        var flight = new Flight
        {
            Id = 55, ContestId = 10, NavigationTask = 100, TakeoffTime = "2026-10-10T10:00:00Z", CanCancel = false, MapViewable = false,
            ContestName = "Secret Cup", NavigationTaskName = "Hidden Day 1", TaskType = new TaskType { DisplayName = "Precision", FamilyLabel = "Legacy" },
            ContestHeaderImage = "/media/banner.jpg", ContestLocation = "60.1,11.2", ContestCountry = "NO",
        };
        var row = Urls.JoinFlights([flight], []).Single();
        Assert.Equal("Secret Cup", row.ContestName);
        Assert.Equal("Hidden Day 1", row.TaskName);
        Assert.False(row.MapViewable);
        Assert.Equal("Precision", row.Task?.TaskType?.DisplayName);
        Assert.Equal("/media/banner.jpg", row.Contest?.HeaderImage);
        Assert.False(row.CanCancel());
    }

    [Fact]
    public void TheTrackingStripShowsAPrivateFlightButOffersNoMap()
    {
        var flight = new Flight
        {
            Id = 55, ContestId = 10, NavigationTask = 100, TakeoffTime = "2026-10-10T10:00:00Z", MapViewable = false, ContestName = "Secret Cup",
            NavigationTaskName = "Hidden Day 1", ContestHeaderImage = "/media/banner.jpg", ContestLocation = "60.1,11.2",
        };
        var next = TrackingTargets.Of([], Urls.JoinFlights([flight], []), new Dictionary<int, ContestSummary>(), t => t ?? "-", t => t ?? "-").Next!;
        Assert.Equal("Secret Cup", next.ContestName);
        Assert.Equal("https://airsports.no/media/banner.jpg", next.BannerUrl);
        Assert.Null(next.MapLink);
        Assert.True(next.MapPrivate);
        Assert.NotNull(next.DirectionsUrl);
    }

    [Fact]
    public void AnActiveFlightInAPrivateContestIsNamedFromTheNowData()
    {
        var task = new CurrentTask
        {
            NavigationTask = new NavigationTaskSummary { Id = 100, Name = "Hidden Day 1", TrackingLink = "/competition-map/10/100" },
            ActiveContestants = [new ActiveContestant { Id = 1, ContestId = 10, MapViewable = false, ContestName = "Secret Cup", MapPath = "/competition-map/10/100?contestantIds=1" }],
        };
        var now = TrackingTargets.Of([task], [], new Dictionary<int, ContestSummary>(), t => t ?? "-", t => t ?? "-").Now.Single();
        Assert.Equal("Secret Cup", now.ContestName);
        Assert.Null(now.MapLink);
        Assert.True(now.MapPrivate);
    }
}

public class TrackingTargetsTest
{
    private static string Time(string? t) => t ?? "-";
    private static string DateTime(string? t) => $"on {t}";

    private static CurrentTask Task(int id, string name, params ActiveContestant[] contestants) => new()
    {
        NavigationTask = new NavigationTaskSummary { Id = id, Name = name, TrackingLink = $"https://app.airsports.no/competition-map/10/{id}" },
        ActiveContestants = contestants,
    };

    private static readonly ContestSummary Cup = new()
    {
        Id = 10, Name = "Norwegian Cup",
        NavigationTasks = [new() { Pk = 7, Name = "Day 1", TaskType = new TaskType { DisplayName = "2.A1 Curve navigation", FamilyLabel = "Precision" } }, new() { Pk = 8, Name = "Day 2" }],
        Logo = "/media/logo.png", HeaderImage = "https://cdn.example/banner.jpg", Website = "https://cup.example", Location = "59.9, 10.7", Country = "NO",
    };

    private static readonly Dictionary<int, ContestSummary> Contests = new() { [10] = Cup };

    private static TrackingTargets Now(params CurrentTask[] t) => TrackingTargets.Of(t, [], Contests, Time, DateTime);

    [Fact]
    public void OpenWindowShowsEverythingAboutTheTrackedTask()
    {
        var c = new ActiveContestant
        {
            Id = 1, ContestId = 10, TrackerStartTime = "10:00", FinishedByTime = "11:30", TakeoffTime = "10:30",
            AirSpeed = 70.0, WindSpeed = 12.0, WindDirection = 270.0, ContestantNumber = 4,
        };
        var t = Now(Task(7, "Day 1", c)).Now.Single();
        Assert.Equal("Norwegian Cup", t.ContestName);
        Assert.Equal("Day 1", t.TaskName);
        Assert.Equal("2.A1 Curve navigation", t.TaskType);
        Assert.Equal(
            [new("Tracking window", "10:00 - 11:30"), new("Take-off", "10:30"), new("Airspeed", "70 knots"), new("Wind", "270@12 knots"), new Fact("Contestant", "#4")],
            t.Facts);
        Assert.Equal("https://cdn.example/banner.jpg", t.BannerUrl);
        Assert.Equal("https://airsports.no/media/logo.png", t.LogoUrl); // relative image URLs are made absolute
        Assert.Equal("NO", t.Country);
        Assert.Equal("https://app.airsports.no/competition-map/10/7", t.MapLink);
        Assert.Equal("https://cup.example", t.WebsiteUrl);
        Assert.Equal("https://www.google.com/maps/dir/?api=1&destination=59.9,10.7", t.DirectionsUrl);
    }

    [Fact]
    public void TheScoreSoFarIsShownWhenTheServerSendsOne()
    {
        var t = Now(Task(7, "Day 1", new ActiveContestant { Id = 1, ContestId = 10, Score = 17.0 })).Now.Single();
        Assert.Equal(new Fact("Score so far", "17"), t.Facts.Last());
        Assert.DoesNotContain(Now(Task(7, "Day 1", new ActiveContestant { Id = 1, ContestId = 10 })).Now.Single().Facts, f => f.Label == "Score so far");
    }

    [Fact]
    public void TheLiveMapButtonOpensThePilotsOwnFlightWhenTheServerSaysWhere() =>
        Assert.Equal("/competition-map/10/7?contestantIds=55", Now(Task(7, "Day 1", new ActiveContestant { Id = 55, ContestId = 10, MapPath = "/competition-map/10/7?contestantIds=55" })).Now.Single().MapLink);

    [Fact]
    public void WebPathKeepsOurLinksAndRefusesOthers()
    {
        Assert.Equal("/competition-map/1/2?contestantIds=3", Urls.WebPath("/competition-map/1/2?contestantIds=3"));
        Assert.Equal("/competition-map/1/2?x=1", Urls.WebPath("https://app.airsports.no/competition-map/1/2?x=1"));
        Assert.Equal("/", Urls.WebPath("https://airsports.no"));
        Assert.Equal("/a", Urls.WebPath("https://www.airsports.no:443/a"));
        Assert.Null(Urls.WebPath("https://evil.example.com/competition-map/1/2"));
        Assert.Null(Urls.WebPath("https://notairsports.no/x"));
        Assert.Null(Urls.WebPath("airsports.no/x"));
        Assert.Null(Urls.WebPath("//evil.example.com/x")); // protocol-relative: another site
        Assert.Null(Urls.WebPath(@"/\evil.example.com/x"));
    }

    [Fact]
    public void WindIsPaddedToThreeDigitsLikeTheFlightOrders() =>
        Assert.Equal("090@5 knots", Now(Task(7, "Day 1", new ActiveContestant { Id = 1, ContestId = 10, WindSpeed = 5.0, WindDirection = 90.0 })).Now.Single().Facts.Single(f => f.Label == "Wind").Value);

    [Fact]
    public void AdaptiveStartReplacesTheTakeoffTime()
    {
        var t = Now(Task(7, "Day 1", new ActiveContestant { Id = 1, ContestId = 10, TakeoffTime = "10:30", AdaptiveStart = true })).Now.Single();
        Assert.True(t.AdaptiveStart);
        Assert.DoesNotContain(t.Facts, f => f.Label == "Take-off");
        Assert.Equal("Adaptive (nearest minute you cross the line)", t.Facts.Single(f => f.Label == "Start").Value);
    }

    [Fact]
    public void AnUnknownContestStillRendersWithoutImagesOrLinks()
    {
        var t = TrackingTargets.Of([Task(5, "Lonely", new ActiveContestant { Id = 1, ContestId = 99 })], [], new Dictionary<int, ContestSummary>(), Time, DateTime).Now.Single();
        Assert.Null(t.ContestName);
        Assert.Null(t.BannerUrl);
        Assert.Null(t.LogoUrl);
        Assert.Null(t.WebsiteUrl);
        Assert.Null(t.DirectionsUrl);
        Assert.Null(t.TaskType);
        Assert.Equal("/mission-dashboard/99", t.ContestPagePath); // the id alone is enough for the contest page
    }

    [Fact]
    public void SeveralTasksAreAllListed() =>
        Assert.Equal(["A", "B"], Now(Task(7, "A", new ActiveContestant { Id = 1, ContestId = 10 }), Task(8, "B", new ActiveContestant { Id = 2, ContestId = 10 })).Now.Select(t => t.TaskName));

    [Fact]
    public void WithNothingOpenTheNextFlightIsShownWithItsDetails()
    {
        var next = Urls.JoinFlights([new Flight { Id = 1, ContestId = 10, NavigationTask = 8, TakeoffTime = "2026-10-11T09:00:00Z", AirSpeed = 80.0, ContestantNumber = 2 }], [Cup]).Single();
        var t = TrackingTargets.Of([], [next], Contests, Time, DateTime);
        Assert.Empty(t.Now);
        var target = t.Next!;
        Assert.Equal("Norwegian Cup", target.ContestName);
        Assert.Equal("Day 2", target.TaskName);
        Assert.Equal([new("Take-off", "on 2026-10-11T09:00:00Z"), new("Airspeed", "80 knots"), new Fact("Contestant", "#2")], target.Facts);
        Assert.Equal("https://cdn.example/banner.jpg", target.BannerUrl);
        Assert.Equal("/mission-dashboard/10", target.ContestPagePath);
    }

    // Realistic text ("2026-10-17 14:00" / "14:00", UTC), unlike the fake formatters above, so the wording and the dates are what a pilot sees.
    private static string RealDateTime(string? t) => msfsclient.Core.Ui.TimeFormat.FormatLocal(t, TimeZoneInfo.Utc);
    private static string RealTime(string? t) => msfsclient.Core.Ui.TimeFormat.FormatTimeLocal(t, TimeZoneInfo.Utc);
    private static readonly DateTimeOffset Oct17At1200 = DateTimeOffset.Parse("2026-10-17T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    private static TrackingTarget NextTarget(Flight flight) =>
        TrackingTargets.Of([], Urls.JoinFlights([flight], [Cup]), Contests, RealTime, RealDateTime, Oct17At1200).Next!;

    [Fact]
    public void TheNextFlightSaysItIsNotActiveYetAndWhenTrackingOpens()
    {
        var target = NextTarget(new Flight
        {
            Id = 1, ContestId = 10, NavigationTask = 8, TakeoffTime = "2026-10-17T15:00:00Z", TrackerStartTime = "2026-10-17T14:00:00Z",
            FinishedByTime = "2026-10-17T16:00:00Z", AdaptiveStart = true,
        });
        Assert.Equal(new Fact("Tracking window", "2026-10-17 14:00 - 16:00"), target.Facts[0]);
        Assert.Equal("Not active yet: tracking for this flight opens at 2026-10-17 14:00. Until then your positions are not scored.", target.NotActiveNote);
        // A flight whose window is open has no note.
        Assert.Null(TrackingTargets.Of([Task(7, "Day 1", new ActiveContestant { Id = 1, ContestId = 10 })], [], Contests, Time, DateTime).Now.Single().NotActiveNote);
    }

    [Fact]
    public void AWindowThatHasAlreadyOpenedIsNeverCalledNotActive()
    {
        // The server may have no current flight although the window is open (the simulator's positions are ignored, or the request failed).
        Assert.Null(NextTarget(new Flight
        {
            Id = 1, ContestId = 10, NavigationTask = 8, TakeoffTime = "2026-10-17T11:00:00Z", TrackerStartTime = "2026-10-17T10:00:00Z",
            FinishedByTime = "2026-10-17T13:00:00Z",
        }).NotActiveNote);
        // Without a known start there is nothing to claim either.
        Assert.Null(NextTarget(new Flight { Id = 2, ContestId = 10, NavigationTask = 8, TakeoffTime = "x" }).NotActiveNote);
    }

    [Fact]
    public void ATrackingWindowThatRunsPastMidnightShowsBothDates()
    {
        var target = NextTarget(new Flight
        {
            Id = 1, ContestId = 10, NavigationTask = 8, TakeoffTime = "2026-10-17T22:30:00Z", TrackerStartTime = "2026-10-17T22:00:00Z",
            FinishedByTime = "2026-10-18T01:00:00Z",
        });
        Assert.Equal(new Fact("Tracking window", "2026-10-17 22:00 - 2026-10-18 01:00"), target.Facts[0]);
    }

    [Fact]
    public void AnOpenWindowHidesTheNextFlight()
    {
        var next = Urls.JoinFlights([new Flight { Id = 1, ContestId = 10, NavigationTask = 8, TakeoffTime = "x" }], [Cup]);
        Assert.Null(TrackingTargets.Of([Task(7, "Day 1", new ActiveContestant { Id = 1, ContestId = 10 })], next, Contests, Time, DateTime).Next);
    }

    [Fact]
    public void NothingScheduledGivesAnEmptyResult()
    {
        var t = TrackingTargets.Of([], [], new Dictionary<int, ContestSummary>(), Time, DateTime);
        Assert.Empty(t.Now);
        Assert.Null(t.Next);
    }

    [Fact]
    public void VenueCoordinatesIgnoreBlankOrPlaceholderLocations()
    {
        Assert.Equal((59.9, 10.7), Urls.VenueCoordinates("59.9,10.7"));
        Assert.Equal((59.9, 10.7), Urls.VenueCoordinates(" 59.9 , 10.7 "));
        Assert.Null(Urls.VenueCoordinates(""));
        Assert.Null(Urls.VenueCoordinates(null));
        Assert.Null(Urls.VenueCoordinates("0,0"));
        Assert.Null(Urls.VenueCoordinates("somewhere"));
        Assert.Null(Urls.VenueCoordinates("59.9"));
    }

    [Fact]
    public void AbsoluteUrlOnlyTouchesRelativePaths()
    {
        Assert.Equal("https://airsports.no/media/a.png", Urls.AbsoluteUrl("/media/a.png"));
        Assert.Equal("https://x/a.png", Urls.AbsoluteUrl("https://x/a.png"));
        Assert.Null(Urls.AbsoluteUrl(null));
        Assert.Null(Urls.AbsoluteUrl(" "));
    }

    [Fact]
    public void ParsesARealContestPayloadWithImagesAndTaskType()
    {
        const string json = """
            {"id":10,"name":"Cup","logo":"/media/l.png","header_image":null,"contest_website":"","location":"59.9,10.7",
             "country":"NO","time_zone":"Europe/Oslo","unknown":1,
             "navigationtask_set":[{"pk":7,"name":"Day 1","tracking_link":"x","task_subtype_definition":{"key":"k","display_name":"2.A1 Curve","coarse_family_label":"Precision"}},
                                   {"pk":8,"name":"Day 2","task_subtype_definition":null}]}
            """;
        var c = AirsportsJson.Deserialize<ContestSummary>(json)!;
        Assert.Equal("2.A1 Curve", c.NavigationTasks[0].TaskType?.DisplayName);
        Assert.Equal("Precision", c.NavigationTasks[0].TaskType?.FamilyLabel);
        Assert.Null(c.NavigationTasks[1].TaskType);
        Assert.Null(c.HeaderImage);
        Assert.Equal("/media/l.png", c.Logo);
        Assert.Equal("Europe/Oslo", c.TimeZone);
    }

    [Fact]
    public void EmbedFlagIsAddedOnceWhateverThePath()
    {
        Assert.Equal("/?embed=app&client=msfs", Urls.WithEmbedFlag("/"));
        Assert.Equal("/open-today?embed=app&client=msfs", Urls.WithEmbedFlag("/open-today"));
        Assert.Equal("/?tab=upcoming&embed=app&client=msfs", Urls.WithEmbedFlag("/?tab=upcoming"));
        // The flag goes into the query, before the fragment that carries the position.
        Assert.Equal("/open-today?embed=app&client=msfs#lat=59.914&lon=10.752", Urls.WithEmbedFlag("/open-today#lat=59.914&lon=10.752"));
        Assert.Equal("/x?a=1&embed=app&client=msfs#f", Urls.WithEmbedFlag("/x?a=1#f"));
        Assert.Equal("/x?embed=app", Urls.WithEmbedFlag("/x?embed=app"));
        // And it survives being sent as one query value to /firebase_login/?next=
        Assert.Equal("%2F%3Ftab%3Dupcoming%26embed%3Dapp%26client%3Dmsfs", Urls.EncodeQueryComponent(Urls.WithEmbedFlag("/?tab=upcoming")));
    }

    [Fact]
    public void TheOpenTodayPageGetsThePositionInTheFragmentRoundedToAbout100Metres()
    {
        Assert.Equal("/open-today", OpenTodayPage.Path(null));
        Assert.Equal("/open-today#lat=59.914&lon=10.752", OpenTodayPage.Path(new Coordinates(59.91389, 10.75224)));
        Assert.Equal("/open-today#lat=-33.869&lon=151.209", OpenTodayPage.Path(new Coordinates(-33.8688, 151.2093)));
    }
}
