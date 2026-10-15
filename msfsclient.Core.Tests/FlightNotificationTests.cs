using System.Net;
using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.Data;
using static msfsclient.Core.Tests.Api;
using static msfsclient.Core.Tests.MockHandler;

namespace msfsclient.Core.Tests;

/// <summary>Flight notifications (userprofile/client_notifications/): what the phones get by push, fetched by the client.</summary>
public class FlightNotificationTest
{
    private readonly long _now = Ms("2026-10-13T12:00:00Z");
    private string _notifications = "[]";
    private bool _endpointMissing;
    private readonly List<string> _queries = [];
    private readonly AppSettings _settings = new(AirsportsDatabase.InMemory()) { TourSeen = true };

    private string Note(int id, long createdMillis, string title = "Tracking opens soon") =>
        $$"""{"id":{{id}},"kind":"tracking_window_opens","title":"{{title}}","body":"Day 1: press play.","contestant_id":1,"navigation_task_id":7,"created_at":"{{IsoTime.Format(createdMillis)}}"}""";

    private AppController Controller()
    {
        var api = With(new MockHandler(r =>
        {
            if (!r.Url.AbsolutePath.Contains("client_notifications")) return Json("[]");
            _queries.Add(r.Url.Query);
            return _endpointMissing ? Status(HttpStatusCode.NotFound) : Json(_notifications);
        }));
        return new AppController(api, new FakeAuth(), new DeviceInfo("msfs", 30), settings: _settings, now: () => _now);
    }

    private static List<ClientNotification> Raised(AppController c)
    {
        var raised = new List<ClientNotification>();
        c.FlightNotification += raised.Add;
        return raised;
    }

    [Fact]
    public async Task TheFirstCheckOnlyNotesWhereTheListStands()
    {
        _notifications = $"[{Note(5, _now - 60_000)}]";
        var c = Controller();
        var raised = Raised(c);
        await c.PollFlightNotificationsAsync();
        Assert.Empty(raised); // a new install or account is not shown what came before
        Assert.Equal(5, _settings.LastClientNotificationId);
        Assert.Equal([""], _queries);
    }

    [Fact]
    public async Task NewNotificationsAreRaisedOldestFirst()
    {
        _settings.LastClientNotificationId = 5;
        _notifications = $"[{Note(7, _now - 10_000, "B")},{Note(6, _now - 60_000, "A")}]"; // the server sends newest first
        var c = Controller();
        var raised = Raised(c);
        await c.PollFlightNotificationsAsync();
        Assert.Equal(["A", "B"], raised.Select(n => n.Title));
        Assert.Equal("Day 1: press play.", raised[0].Body);
        Assert.Equal(7, _settings.LastClientNotificationId);
        Assert.Equal(["?after=5"], _queries);
    }

    [Fact]
    public async Task StaleNotificationsAreSkippedButCounted()
    {
        _settings.LastClientNotificationId = 5;
        _notifications = $"[{Note(6, _now - AppController.FlightNotificationMaxAgeMillis - 1)}]"; // arrived while the client was closed
        var c = Controller();
        var raised = Raised(c);
        await c.PollFlightNotificationsAsync();
        Assert.Empty(raised);
        Assert.Equal(6, _settings.LastClientNotificationId);
    }

    [Fact]
    public async Task AnEmptyFirstCheckStillStartsCounting()
    {
        var c = Controller();
        var raised = Raised(c);
        await c.PollFlightNotificationsAsync();
        Assert.Equal(0, _settings.LastClientNotificationId);
        _notifications = $"[{Note(1, _now - 5_000)}]";
        await c.PollFlightNotificationsAsync();
        Assert.Single(raised); // the first notification ever is not swallowed
    }

    [Fact]
    public async Task AServerWithoutTheEndpointIsQuiet()
    {
        _endpointMissing = true;
        _settings.LastClientNotificationId = 3;
        var c = Controller();
        var raised = Raised(c);
        await c.PollFlightNotificationsAsync();
        Assert.Empty(raised);
        Assert.Equal(3, _settings.LastClientNotificationId);
    }

    [Fact]
    public async Task SigningOutForgetsWhereTheListStood()
    {
        _settings.LastClientNotificationId = 9;
        var c = Controller();
        await c.Start();
        c.SignOut();
        Assert.Null(_settings.LastClientNotificationId);
    }
}
