using System.Collections.Concurrent;
using System.Net;
using msfsclient.Core.Data;
using msfsclient.Core.History;
using msfsclient.Core.Tracking;
using static msfsclient.Core.Tests.MockHandler;

namespace msfsclient.Core.Tests;

/// <summary>Port of TrackingSessionTest.</summary>
public class TrackingSessionTest
{
    private sealed class FakeSource : IPositionSource
    {
        public event Action<TrackPoint>? PositionReceived;
        public void Send(TrackPoint p) => PositionReceived?.Invoke(p);
    }

    private static TrackPoint Point(long t) => new(t, 59.0, 10.0, 5.0, 90.0, 100.0, 5.0, 3.0);

    private static async Task AwaitState(TrackingSession session, string what, Func<TrackingState, bool> condition)
    {
        var deadline = Environment.TickCount64 + 30_000;
        while (!condition(session.State))
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException($"timed out waiting for {what}; state={session.State}");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task UploadsFixesAndRecoversAfterOutage()
    {
        var queue = new PositionQueue(AirsportsDatabase.InMemory());
        var online = false;
        var received = new ConcurrentQueue<string>();
        var uploader = new TraccarUploader(new HttpClient(new MockHandler(r =>
        {
            if (!Volatile.Read(ref online)) return Status(HttpStatusCode.ServiceUnavailable);
            received.Enqueue(System.Web.HttpUtility.ParseQueryString(r.Url.Query)["timestamp"]!);
            return Status(HttpStatusCode.OK);
        })), queue, "https://t/");
        var source = new FakeSource();
        var session = new TrackingSession(source, queue, uploader, "dev", now: () => 42, initialBackoffMs: 50, maxBackoffMs: 200);

        session.Start();
        source.Send(Point(1000));
        source.Send(Point(2000));
        // Both conditions together: the error can show after the first fix, before the second has been queued.
        await AwaitState(session, "offline error with both fixes queued", s => s.LastError != null && s.Backlog == 2);
        Assert.Equal(2, session.State.Backlog); // offline: points stay queued

        Volatile.Write(ref online, true);
        await AwaitState(session, "drain", s => s.Backlog == 0 && s.LastError == null);
        Assert.Equal(["1", "2"], received);
        Assert.Equal(42L, session.State.LastUploadMillis);
        session.Stop();
    }

    [Fact]
    public async Task KeepsTheLatestClockOffsetAndIgnoresFixesWithoutOne()
    {
        var queue = new PositionQueue(AirsportsDatabase.InMemory());
        var source = new FakeSource();
        var session = new TrackingSession(source, queue, new TraccarUploader(new HttpClient(new MockHandler(_ => Status(HttpStatusCode.OK))), queue, "https://t/"), "dev");
        session.Start();

        source.Send(Point(1000) with { ClockOffsetMillis = 7_000 });
        await AwaitState(session, "first offset", s => s.ClockOffsetMillis == 7_000);
        source.Send(Point(2000)); // a fix that cannot tell keeps the last known offset
        await AwaitState(session, "second fix", s => s.LastFixMillis == 2000);
        Assert.Equal(7_000L, session.State.ClockOffsetMillis);
        source.Send(Point(3000) with { ClockOffsetMillis = -3_000 });
        await AwaitState(session, "new offset", s => s.ClockOffsetMillis == -3_000);
        session.Stop();
    }

    [Fact]
    public async Task RecordsWhenTrackingStartedAndTheLatestPosition()
    {
        var queue = new PositionQueue(AirsportsDatabase.InMemory());
        var source = new FakeSource();
        var session = new TrackingSession(source, queue, new TraccarUploader(new HttpClient(new MockHandler(_ => Status(HttpStatusCode.OK))), queue, "https://t/"), "dev", now: () => 42);

        session.Start();
        Assert.Equal(42L, session.State.StartedAtMillis);
        Assert.Null(session.State.LastFixMillis);

        source.Send(Point(1000) with { Latitude = 61.5 });
        await AwaitState(session, "first fix", s => s.LastFixMillis == 1000);
        Assert.Equal(61.5, session.State.LastLatitude);
        Assert.Equal(1, session.State.FixCount);
        session.Stop();
    }

    [Fact]
    public async Task StopsItselfAfterTheMaximumDurationAndSaysSo()
    {
        var queue = new PositionQueue(AirsportsDatabase.InMemory());
        var session = new TrackingSession(new FakeSource(), queue, new TraccarUploader(new HttpClient(new MockHandler(_ => Status(HttpStatusCode.OK))), queue, "https://t/"), "dev", maxDurationMillis: 100);
        session.Start();
        Assert.False(session.State.AutoStopped);
        await AwaitState(session, "automatic stop", s => !s.Running);
        Assert.True(session.State.AutoStopped);
        session.Start(); // starting again clears the flag
        Assert.False(session.State.AutoStopped);
        session.Stop();
        Assert.False(session.State.AutoStopped); // a manual stop is not an automatic one
    }

    [Fact]
    public async Task EveryPositionIsRecordedInTheTrackLogAndTheSessionEndsWithTracking()
    {
        var db = AirsportsDatabase.InMemory();
        var queue = new PositionQueue(db);
        var log = new TrackLog(db);
        var source = new FakeSource();
        var clock = 5_000L;
        var session = new TrackingSession(source, queue, new TraccarUploader(new HttpClient(new MockHandler(_ => Status(HttpStatusCode.OK))), queue, "https://t/"), "dev",
            now: () => clock, trackLog: log);
        session.Start();
        source.Send(Point(6_000));
        source.Send(Point(7_000));
        await AwaitState(session, "two fixes", s => s.FixCount == 2);
        Assert.True(log.Sessions().Single().Recording);
        clock = 8_000;
        session.Stop();
        var recorded = log.Sessions().Single();
        Assert.Equal(8_000L, recorded.EndedAtMillis);
        Assert.Equal(2L, recorded.PointCount);
    }

    [Fact]
    public async Task PositionsAreIgnoredWhileTrackingIsOff()
    {
        var queue = new PositionQueue(AirsportsDatabase.InMemory());
        var source = new FakeSource();
        var session = new TrackingSession(source, queue, new TraccarUploader(new HttpClient(new MockHandler(_ => Status(HttpStatusCode.ServiceUnavailable))), queue, "https://t/"), "dev");
        source.Send(Point(1_000));
        session.Start();
        session.Stop();
        source.Send(Point(2_000));
        await Task.Delay(50);
        Assert.Equal(0, queue.Size());
    }
}
