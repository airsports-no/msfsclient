using System.Net;
using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.Data;
using static msfsclient.Core.Tests.Api;
using static msfsclient.Core.Tests.MockHandler;

namespace msfsclient.Core.Tests;

/// <summary>The practice flight offered to new simulator pilots (server: userprofile/msfs_quickstart/).</summary>
public class QuickstartTest
{
    private const string Offered = """
        {"id":3,"title":"Practice around Kjeller","description":"A short route to learn the ropes.","contest_id":10,"contest_name":"MSFS practice",
         "navigation_task_id":100,"navigation_task_name":"Practice","start_location":"Kjeller (ENKJ), runway 30",
         "start_instructions":"Take off and fly to the starting point.",
         "ground_start":{"latitude":59.97,"longitude":11.03,"heading":300.0},
         "air_start":{"latitude":59.97,"longitude":11.13,"altitude_ft":2500,"heading":90.0},
         "registration_path":"/schedule-flight?contestId=10&navigationTaskId=100","registered_flight":null}
        """;

    private string? _quickstart = Offered;
    private string _future = "[]";
    private string _tasks = "[]";

    private AirsportsApi Server() => With(new MockHandler(r =>
        r.Path.Contains("msfs_quickstart") ? (_quickstart == null ? Status(HttpStatusCode.NotFound) : Json(_quickstart))
        : r.Path.Contains("retrieve_profile") ? Json("""{"validated":true,"simulator_tracking_id":"sim1"}""")
        : r.Path.Contains("get_current_sim_navigation_task") ? (_tasks == "[]" ? Status(HttpStatusCode.NotFound) : Json(_tasks))
        : r.Path.Contains("my_future_flights") ? Json(_future)
        : r.Path.EndsWith("/contests/") ? Json("""{"results":[],"next":null}""")
        : Json("[]")));

    private async Task<AppController> Started(AppSettings? settings = null)
    {
        var c = new AppController(Server(), new FakeAuth(), new DeviceInfo("msfs", 30), settings: settings ?? new AppSettings(AirsportsDatabase.InMemory()) { TourSeen = true });
        await c.Start();
        return c;
    }

    [Fact]
    public async Task APilotWithoutAFlightIsOfferedThePracticeFlight()
    {
        var c = await Started();
        var offer = c.State.QuickstartOffer;
        Assert.NotNull(offer);
        Assert.Equal("Kjeller (ENKJ), runway 30", offer.StartLocation);
        Assert.Equal(2500, offer.AirStart?.AltitudeFt);
        Assert.Equal(300.0, offer.GroundStart?.Heading);
        Assert.Null(offer.GroundStart?.AltitudeFt);
    }

    [Fact]
    public async Task NothingIsOfferedWhenTheServerHasNone()
    {
        _quickstart = null;
        var c = await Started();
        Assert.Null(c.State.Quickstart);
        Assert.Null(c.State.QuickstartOffer);
    }

    [Fact]
    public async Task APilotWithAFlightOfTheirOwnIsNotOfferedIt()
    {
        _future = """[{"id":1,"contest_id":20,"navigation_task":200,"takeoff_time":"2099-01-01T10:00:00Z"}]""";
        Assert.Null((await Started()).State.QuickstartOffer);
        _future = "[]";
        _tasks = """[{"navigation_task":{"pk":7,"name":"Day 1"},"active_contestants":[{"id":1}]}]""";
        Assert.Null((await Started()).State.QuickstartOffer);
    }

    [Fact]
    public async Task OnceRegisteredItStaysToHelpThePilotToTheStart()
    {
        _quickstart = Offered.Replace("\"registered_flight\":null", "\"registered_flight\":{\"id\":55,\"takeoff_time\":\"2099-01-01T10:00:00Z\"}");
        _future = """[{"id":55,"contest_id":10,"navigation_task":100,"takeoff_time":"2099-01-01T10:00:00Z"}]""";
        var offer = (await Started()).State.QuickstartOffer;
        Assert.Equal(55, offer?.RegisteredFlight?.Id);
    }

    [Fact]
    public async Task RegisterOpensTheTasksRegistrationPage()
    {
        var c = await Started();
        c.OpenQuickstartRegistration();
        Assert.Equal(Screen.Map, c.State.Screen);
        Assert.Equal("/schedule-flight?contestId=10&navigationTaskId=100", c.State.MapPath);
    }

    [Fact]
    public async Task NotNowHidesItForGoodButANewPracticeFlightIsOfferedAgain()
    {
        var settings = new AppSettings(AirsportsDatabase.InMemory()) { TourSeen = true };
        var c = await Started(settings);
        c.DismissQuickstart();
        Assert.Null(c.State.QuickstartOffer);
        Assert.Null((await Started(settings)).State.QuickstartOffer); // after a restart too

        _quickstart = Offered.Replace("\"id\":3", "\"id\":4");
        Assert.NotNull((await Started(settings)).State.QuickstartOffer);
    }

    // ---- One-press registration ("Go flying") and the flight order --------------------------------------------------

    private const string RegisteredJson = "\"registered_flight\":{\"id\":55,\"takeoff_time\":\"2099-01-01T10:00:00Z\",\"adaptive_start\":true,\"air_speed\":70.0,\"flight_order_path\":ORDER}";

    private static string Registered(string? orderPath) =>
        Offered.Replace("\"registered_flight\":null", RegisteredJson.Replace("ORDER", orderPath == null ? "null" : $"\"{orderPath}\""));

    private int _registerCalls;
    private HttpStatusCode _registerStatus = HttpStatusCode.Created;
    private string _registerBody = "";
    private long _now = Ms("2026-10-12T12:00:00Z");

    private AirsportsApi ServerWithRegistration() => With(new MockHandler(r =>
    {
        if (r.Url.AbsolutePath.EndsWith("msfs_quickstart_register/"))
        {
            _registerCalls++;
            if (_registerStatus != HttpStatusCode.Created) return Json(_registerBody, _registerStatus);
            _quickstart = Registered(null);
            return Json(_quickstart, HttpStatusCode.Created);
        }
        return r.Url.AbsolutePath.Contains("msfs_quickstart") ? (_quickstart == null ? Status(HttpStatusCode.NotFound) : Json(_quickstart))
            : r.Url.AbsolutePath.Contains("retrieve_profile") ? Json("""{"validated":true,"simulator_tracking_id":"sim1"}""")
            : r.Url.AbsolutePath.Contains("get_current_sim_navigation_task") ? Status(HttpStatusCode.NotFound)
            : r.Url.AbsolutePath.Contains("my_future_flights") ? Json(_future)
            : r.Url.AbsolutePath.EndsWith("/contests/") ? Json("""{"results":[],"next":null}""")
            : Json("[]");
    }));

    private async Task<(AppController, List<string>)> StartedWithRegistration()
    {
        var c = new AppController(ServerWithRegistration(), new FakeAuth(), new DeviceInfo("msfs", 30),
            settings: new AppSettings(AirsportsDatabase.InMemory()) { TourSeen = true }, now: () => _now);
        var opened = new List<string>();
        c.FlightOrderReady += opened.Add;
        await c.Start();
        return (c, opened);
    }

    [Fact]
    public async Task GoFlyingRegistersInOnePressAndTurnsTrainingOn()
    {
        var (c, opened) = await StartedWithRegistration();
        await c.RegisterQuickstartAsync();
        Assert.Equal(1, _registerCalls);
        Assert.Equal(55, c.State.Quickstart?.RegisteredFlight?.Id);
        Assert.Equal(55, c.State.QuickstartOffer?.RegisteredFlight?.Id);
        Assert.True(c.State.TrainingMode);
        Assert.False(c.State.QuickstartRegistering);
        Assert.Equal(Screen.Home, c.State.Screen);
        Assert.Equal(55, c.State.AwaitingFlightOrderFor);
        Assert.Empty(opened); // the flight order is still being made
    }

    [Fact]
    public async Task TheFlightOrderOpensOnceWhenItIsReady()
    {
        var (c, opened) = await StartedWithRegistration();
        await c.RegisterQuickstartAsync();
        await c.PollFlightOrderAsync();
        Assert.Empty(opened);

        _quickstart = Registered("/display/maplink/abc/");
        await c.PollFlightOrderAsync();
        Assert.Equal(["https://app.airsports.no/display/maplink/abc/"], opened);
        Assert.Null(c.State.AwaitingFlightOrderFor);
        await c.PollFlightOrderAsync();
        Assert.Single(opened);
    }

    [Fact]
    public async Task AFlightOrderThatTakesTooLongIsNotWaitedForForever()
    {
        var (c, opened) = await StartedWithRegistration();
        await c.RegisterQuickstartAsync();
        _now += AppController.FlightOrderWaitMillis + 1;
        await c.PollFlightOrderAsync();
        Assert.Null(c.State.AwaitingFlightOrderFor);
        Assert.True(c.State.FlightOrderSlow);
        _quickstart = Registered("/display/maplink/abc/");
        await c.PollFlightOrderAsync();
        Assert.Empty(opened);
    }

    [Fact]
    public async Task TheServersReasonIsShownWhenItRefuses()
    {
        _registerStatus = HttpStatusCode.BadRequest;
        _registerBody = """["No practice flight is offered right now"]""";
        var (c, _) = await StartedWithRegistration();
        await c.RegisterQuickstartAsync();
        Assert.Equal("Could not register: No practice flight is offered right now", c.State.QuickstartError);
        Assert.False(c.State.QuickstartRegistering);
        Assert.Null(c.State.AwaitingFlightOrderFor);
        Assert.False(c.State.TrainingMode);
    }

    [Fact]
    public async Task AnOlderServerGetsTheRegistrationPageInstead()
    {
        _registerStatus = HttpStatusCode.NotFound;
        var (c, _) = await StartedWithRegistration();
        await c.RegisterQuickstartAsync();
        Assert.Equal(Screen.Map, c.State.Screen);
        Assert.Equal("/schedule-flight?contestId=10&navigationTaskId=100", c.State.MapPath);
    }

    [Fact]
    public async Task RegisteringOnThePageAlsoOpensTheFlightOrder()
    {
        var (c, opened) = await StartedWithRegistration();
        _quickstart = Registered("/display/maplink/xyz/");
        await c.OnAppEventAsync(new AppEvent.FlightRegistered(10, 100));
        Assert.Equal(["https://app.airsports.no/display/maplink/xyz/"], opened);
    }
}
