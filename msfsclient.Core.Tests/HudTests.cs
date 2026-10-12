using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.Hud;
using msfsclient.Core.Observations;
using msfsclient.Core.Tracking;
using static msfsclient.Core.Tests.Api;
using static msfsclient.Core.Tests.MockHandler;

namespace msfsclient.Core.Tests;

public class HudServerTest
{
    private sealed class FakeHost : IHudHost
    {
        public List<HudCommand> Commands { get; } = [];
        public HudSnapshot State { get; set; } = new() { SignedIn = true, Version = "2.0.0 (30)", Flight = "Cup - Day 1" };
        public HudSnapshot Snapshot() => State;

        public Task<HudResult> ExecuteAsync(HudCommand command)
        {
            lock (Commands) Commands.Add(command);
            return Task.FromResult(command.Action == "startTracking" ? new HudResult(true, "Tracking started.") : new HudResult(false, "No."));
        }
    }

    private static HudServer Started(FakeHost host)
    {
        var server = new HudServer(host, port: 0); // any free port
        server.Start();
        Assert.True(server.Running, server.Error);
        return server;
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("null", "localhost:47911", true)]
    [InlineData("coui://html_ui", "localhost:47911", true)]
    [InlineData("http://localhost:47911", "localhost:47911", true)]
    [InlineData("http://127.0.0.1:47911", "127.0.0.1:47911", true)]
    [InlineData("http://[::1]:47911", "[::1]:47911", true)]
    [InlineData("https://evil.example", "localhost:47911", false)]
    [InlineData("http://localhost:3000", "localhost:47911", false)]
    [InlineData(null, "evil.example:47911", false)]
    public void OnlyTheClientsOwnPagesAndTheSimulatorMayConnect(string? origin, string? host, bool allowed) =>
        Assert.Equal(allowed, HudServer.IsAllowed(origin, host, 47911));

    [Fact]
    public async Task ServesThePageAndTheStateAsCamelCaseJson()
    {
        using var server = Started(new FakeHost());
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{server.Port}/") };
        Assert.Contains("airsports-hud", await http.GetStringAsync("/"));
        Assert.Contains("new WebSocket", await http.GetStringAsync("/hud.js"));
        Assert.Contains("#airsports-hud", await http.GetStringAsync("/hud.css"));
        using var doc = JsonDocument.Parse(await http.GetStringAsync("/state"));
        Assert.Equal("Cup - Day 1", doc.RootElement.GetProperty("flight").GetString());
        Assert.True(doc.RootElement.GetProperty("signedIn").GetBoolean());
        var font = await http.GetByteArrayAsync("/fonts/barlow_regular.ttf");
        Assert.True(font.Length > 100_000);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/fonts/../hud.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/fonts/missing.ttf")).StatusCode);
    }

    [Fact]
    public async Task ACommandFromAWebPageIsRefused()
    {
        var host = new FakeHost();
        using var server = Started(host);
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{server.Port}/command")
        {
            Content = new StringContent("""{"action":"stopTracking","confirm":true}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Origin", "https://evil.example");
        using var response = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Commands);
    }

    [Fact]
    public async Task CommandsArePassedOnWithTheirResult()
    {
        var host = new FakeHost();
        using var server = Started(host);
        using var http = new HttpClient();
        using var ok = await http.PostAsync($"http://localhost:{server.Port}/command", new StringContent("""{"action":"startTracking"}"""));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("Tracking started.", await ok.Content.ReadAsStringAsync());
        using var bad = await http.PostAsync($"http://localhost:{server.Port}/command", new StringContent("not json"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(["startTracking"], host.Commands.Select(c => c.Action));
    }

    [Fact]
    public async Task TheWebSocketSendsTheStateTakesCommandsAndPushesChanges()
    {
        var host = new FakeHost();
        using var server = Started(host);
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://localhost:{server.Port}/ws"), CancellationToken.None);

        var first = await Receive(socket);
        Assert.Equal("state", first.GetProperty("type").GetString());
        Assert.Equal("Cup - Day 1", first.GetProperty("state").GetProperty("flight").GetString());

        await socket.SendAsync(Encoding.UTF8.GetBytes("""{"action":"startTracking"}"""), WebSocketMessageType.Text, true, CancellationToken.None);
        var result = await Receive(socket);
        Assert.Equal("result", result.GetProperty("type").GetString());
        Assert.True(result.GetProperty("ok").GetBoolean());

        host.State = host.State with { Flight = "Cup - Day 2" };
        server.Publish();
        JsonElement pushed;
        do pushed = await Receive(socket);
        while (pushed.GetProperty("state").GetProperty("flight").GetString() != "Cup - Day 2");
        Assert.Equal(1, server.ClientCount);
    }

    [Fact]
    public async Task AWebSocketFromAWebPageIsRefused()
    {
        using var server = Started(new FakeHost());
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", "https://evil.example");
        await Assert.ThrowsAnyAsync<WebSocketException>(() => socket.ConnectAsync(new Uri($"ws://localhost:{server.Port}/ws"), CancellationToken.None));
    }

    [Fact]
    public void ABusyPortIsReportedNotThrown()
    {
        using var first = Started(new FakeHost());
        using var second = new HudServer(new FakeHost(), first.Port);
        second.Start();
        Assert.False(second.Running);
        Assert.Contains(first.Port.ToString(), second.Error);
    }

    private static async Task<JsonElement> Receive(ClientWebSocket socket)
    {
        var buffer = new byte[64 * 1024];
        using var cts = new CancellationTokenSource(10_000);
        var sb = new StringBuilder();
        WebSocketReceiveResult r;
        do
        {
            r = await socket.ReceiveAsync(buffer, cts.Token);
            sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
        } while (!r.EndOfMessage);
        return JsonDocument.Parse(sb.ToString()).RootElement.Clone();
    }
}

public class HudHostTest
{
    private const string Tasks = """
        [{"navigation_task":{"pk":7,"name":"Day 1"},"active_contestants":[{"id":42,"contest_id":10,"adaptive_start":true,"observation_registration":true,
          "training_available":true,"contest_name":"Cup","training_feedback":{"last_gate":"TP3","last_gate_time_offset":-3.4}}]}]
        """;

    private static AppController Controller() => new(With(new MockHandler(r =>
        r.Path.Contains("retrieve_profile") ? Json("""{"validated":true,"first_name":"Pia","simulator_tracking_id":"sim1"}""")
        : r.Path.Contains("get_current_sim_navigation_task") ? Json(Tasks)
        : r.Path.Contains("observation_photos") ? Json("""{"enabled":true,"may_register":true,"photos":[{"id":5,"name":"Church"}],"observations":[]}""")
        : r.Path.EndsWith("/photo_observations/") ? Json("""{"id":9,"photo":5,"time":"2026-10-10T10:00:00Z"}""", HttpStatusCode.Created)
        : r.Path.EndsWith("/contests/") ? Json("""{"results":[],"next":null}""")
        : Json("[]"))), new FakeAuth(), new DeviceInfo("msfs", 30, "2.0.0"), observationQueue: new ObservationQueue(Data.AirsportsDatabase.InMemory()));

    private sealed class Harness
    {
        public AppController C { get; } = Controller();
        public TrackingState Tracking { get; set; } = new();
        public long Now { get; set; } = Ms("2026-10-10T10:00:00Z");
        public int Starts, Stops;
        public HudHost Host { get; }

        public Harness()
        {
            Host = new HudHost(C, () => new FlightStatus(C.State, Tracking, true, Now - 500, Now), () => { Starts++; Tracking = Tracking with { Running = true }; }, () => Stops++);
        }
    }

    [Fact]
    public async Task StoppingNeedsConfirmationAndStartingNeedsATrackingId()
    {
        var h = new Harness();
        await h.C.Start();
        Assert.True((await h.Host.ExecuteAsync(new HudCommand { Action = "startTracking" })).Ok);
        Assert.Equal(1, h.Starts);
        var refused = await h.Host.ExecuteAsync(new HudCommand { Action = "stopTracking" });
        Assert.False(refused.Ok);
        Assert.Equal(0, h.Stops);
        Assert.True((await h.Host.ExecuteAsync(new HudCommand { Action = "stopTracking", Confirm = true })).Ok);
        Assert.Equal(1, h.Stops);
    }

    [Fact]
    public async Task NothingHappensBeforeSignIn()
    {
        var h = new Harness();
        Assert.False((await h.Host.ExecuteAsync(new HudCommand { Action = "startTracking" })).Ok);
        Assert.Equal(0, h.Starts);
        Assert.False(h.Host.Snapshot().SignedIn);
    }

    [Fact]
    public async Task TheTimerStartsOnceAndResetsOnlyWhenConfirmed()
    {
        var h = new Harness();
        await h.C.Start();
        Assert.True((await h.Host.ExecuteAsync(new HudCommand { Action = "startTimer" })).Ok);
        Assert.Equal(Ms("2026-10-10T10:00:00Z"), h.C.State.TimerOriginMillis);
        Assert.False((await h.Host.ExecuteAsync(new HudCommand { Action = "startTimer" })).Ok); // already running
        Assert.False((await h.Host.ExecuteAsync(new HudCommand { Action = "resetTimer" })).Ok);
        Assert.True((await h.Host.ExecuteAsync(new HudCommand { Action = "resetTimer", Confirm = true })).Ok);
        Assert.Null(h.C.State.TimerOriginMillis);
    }

    [Fact]
    public async Task AMarkNeedsTrackingAndAFreshPositionAndThenIsStored()
    {
        var h = new Harness();
        await h.C.Start();
        var refused = await h.Host.ExecuteAsync(new HudCommand { Action = "mark", PhotoId = 5 });
        Assert.False(refused.Ok);
        Assert.StartsWith("Start tracking", refused.Message);

        h.Tracking = new TrackingState { Running = true, LastFixMillis = h.Now - 2_000, LastLatitude = 59.9, LastLongitude = 10.7 };
        var ok = await h.Host.ExecuteAsync(new HudCommand { Action = "mark", PhotoId = 5 });
        Assert.True(ok.Ok, ok.Message);
        Assert.True(h.C.State.Observations.Registered.ContainsKey(5));
        Assert.False((await h.Host.ExecuteAsync(new HudCommand { Action = "mark", PhotoId = 99 })).Ok);
    }

    [Fact]
    public async Task TheSnapshotCarriesTheTimerTrainingAndPhotos()
    {
        var h = new Harness();
        await h.C.Start();
        h.C.SetTrainingMode(true);
        await h.C.OpenObservationsAsync();
        h.Tracking = new TrackingState { Running = true, LastFixMillis = h.Now - 1_000, LastLatitude = 59.9, LastLongitude = 10.7 };
        var snap = h.Host.Snapshot();
        Assert.Equal("Cup - Day 1", snap.Flight);
        Assert.NotNull(snap.Timer);
        Assert.Null(snap.Timer!.OriginMillis);
        Assert.Equal("TP3", snap.Training?.Gate);
        Assert.Equal("3 s early", snap.Training?.Text);
        Assert.Equal("Early", snap.Training?.Timing);
        var photo = Assert.Single(snap.Photos!.Items);
        Assert.True(photo.CanMark);
        Assert.Equal("Tracking is on", snap.Tracking.Lines[0].Text);
        // Nothing from the route: the snapshot has no gates beyond the server's own last-gate feedback.
        Assert.DoesNotContain("latitude", JsonSerializer.Serialize(snap, HudServer.Json));
    }

    [Fact]
    public async Task TrainingModeIsRefusedWhereTheServerDoesNotOfferIt()
    {
        var h = new Harness();
        Assert.False((await h.Host.ExecuteAsync(new HudCommand { Action = "setTraining", On = true })).Ok); // not signed in
        await h.C.Start();
        Assert.True((await h.Host.ExecuteAsync(new HudCommand { Action = "setTraining", On = true })).Ok);
        Assert.True(h.C.State.TrainingMode);
        Assert.False((await h.Host.ExecuteAsync(new HudCommand { Action = "fly" })).Ok);
    }
}

public class SimMessageTrackerTest
{
    private static readonly long T0 = Ms("2026-10-10T10:00:00Z");

    private static UiState Signed(string tasks = "[]") => new()
    {
        Screen = Screen.Home,
        Person = new Person { Validated = true, SimulatorTrackingId = "sim1" },
        Tasks = AirsportsJson.Deserialize<List<CurrentTask>>(tasks)!,
    };

    private static FlightStatus S(UiState state, TrackingState tracking, long now) => new(state, tracking, true, now, now);

    [Fact]
    public void TheFirstCallOnlyNotesTheStartingPoint()
    {
        var tracker = new SimMessageTracker();
        Assert.Empty(tracker.Next(S(Signed(), new TrackingState { Running = true }, T0)));
    }

    [Fact]
    public void TrackingOnAndOffAreSaidOnce()
    {
        var tracker = new SimMessageTracker();
        var state = Signed();
        tracker.Next(S(state, new TrackingState(), T0));
        Assert.Equal(["Airsports: tracking started"], tracker.Next(S(state, new TrackingState { Running = true }, T0 + 1000)).Select(m => m.Text));
        Assert.Empty(tracker.Next(S(state, new TrackingState { Running = true }, T0 + 2000)));
        Assert.Equal(["Airsports: tracking stopped"], tracker.Next(S(state, new TrackingState(), T0 + 3000)).Select(m => m.Text));
    }

    [Fact]
    public void TheAutomaticStopIsAnAlert()
    {
        var tracker = new SimMessageTracker();
        var state = Signed();
        tracker.Next(S(state, new TrackingState { Running = true }, T0));
        var m = Assert.Single(tracker.Next(S(state, new TrackingState { AutoStopped = true }, T0 + 1000)));
        Assert.Equal(SimMessageKind.Alert, m.Kind);
        Assert.Contains("12 hours", m.Text);
    }

    [Fact]
    public void ReceptionIsAnnouncedLostAndRegained()
    {
        static string Tasks(string received) => $$"""[{"navigation_task":{"pk":7,"name":"Day 1"},"active_contestants":[{"id":1,"last_received_time":"{{received}}"}]}]""";
        var tracker = new SimMessageTracker();
        var on = new TrackingState { Running = true };
        tracker.Next(S(Signed("""[{"navigation_task":{"pk":7,"name":"Day 1"},"active_contestants":[{"id":1}]}]"""), on, T0));
        Assert.Contains("Airsports: the server is receiving your positions",
            tracker.Next(S(Signed(Tasks("2026-10-10T10:00:01Z")), on, T0 + 2000)).Select(m => m.Text));
        var lost = tracker.Next(S(Signed(Tasks("2026-10-10T10:00:01Z")), on, T0 + 60_000));
        Assert.Equal(SimMessageKind.Alert, Assert.Single(lost).Kind);
        Assert.Empty(tracker.Next(S(Signed(Tasks("2026-10-10T10:00:01Z")), on, T0 + 70_000))); // still lost: said once
        Assert.Equal(["Airsports: positions are getting through again"],
            tracker.Next(S(Signed(Tasks("2026-10-10T10:01:15Z")), on, T0 + 80_000)).Select(m => m.Text));
    }

    [Fact]
    public void EachNewGateResultIsShownInTrainingModeOnly()
    {
        static UiState WithGate(string gate, double offset, bool training) => Signed($$$"""
            [{"navigation_task":{"pk":7,"name":"Day 1"},"active_contestants":[{"id":1,"training_available":true,
              "training_feedback":{"last_gate":"{{{gate}}}","last_gate_time_offset":{{{offset.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}]}]
            """) with { TrainingMode = training };
        var tracker = new SimMessageTracker();
        var on = new TrackingState { Running = true };
        tracker.Next(S(WithGate("SP", 0.5, training: false), on, T0));
        Assert.Empty(tracker.Next(S(WithGate("TP1", 4, training: false), on, T0 + 1000)));
        var m = Assert.Single(tracker.Next(S(WithGate("TP1", 4, training: true), on, T0 + 2000)));
        Assert.Equal("TP1: 4 s late", m.Text);
        Assert.Empty(tracker.Next(S(WithGate("TP1", 4, training: true), on, T0 + 3000)));
        Assert.Equal(SimMessageKind.Good, Assert.Single(tracker.Next(S(WithGate("TP2", -1, training: true), on, T0 + 4000))).Kind);
    }

    [Fact]
    public void TheTimerCallsOutOneMinuteToGoAndTheStartMinute()
    {
        var tracker = new SimMessageTracker();
        var origin = T0 + 120_000;
        var state = Signed() with { TimerOriginMillis = origin };
        tracker.Next(S(Signed(), new TrackingState { Running = true }, T0));
        Assert.Contains("timer started", tracker.Next(S(state, new TrackingState { Running = true }, T0 + 1000)).Single().Text);
        Assert.Empty(tracker.Next(S(state, new TrackingState { Running = true }, origin - 61_000)));
        Assert.Equal(["Airsports: one minute to your start minute"], tracker.Next(S(state, new TrackingState { Running = true }, origin - 59_500)).Select(m => m.Text));
        Assert.Empty(tracker.Next(S(state, new TrackingState { Running = true }, origin - 500)));
        Assert.Equal(["Airsports: start minute now"], tracker.Next(S(state, new TrackingState { Running = true }, origin + 400)).Select(m => m.Text));
        Assert.Empty(tracker.Next(S(state, new TrackingState { Running = true }, origin + 1400)));
    }

    [Fact]
    public void NewAnnouncementsArePrintedButNotTheOnesThereAtStart()
    {
        var tracker = new SimMessageTracker();
        var old = new Announcement { Id = 1, ContestName = "Cup", Message = "Briefing 08:30" };
        tracker.Next(S(Signed() with { Announcements = [old] }, new TrackingState(), T0));
        var fresh = new Announcement { Id = 2, ContestName = "Cup", Message = "Start delayed 30 min" };
        Assert.Equal(["Cup: Start delayed 30 min"], tracker.Next(S(Signed() with { Announcements = [fresh, old] }, new TrackingState(), T0 + 1000)).Select(m => m.Text));
    }
}
