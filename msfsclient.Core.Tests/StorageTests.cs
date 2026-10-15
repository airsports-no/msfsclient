using System.Net;
using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.Data;
using msfsclient.Core.History;
using msfsclient.Core.Observations;
using msfsclient.Core.Tracking;
using static msfsclient.Core.Tests.Api;
using static msfsclient.Core.Tests.MockHandler;

namespace msfsclient.Core.Tests;

// Ports of AppSettingsTest, TrackLogTest, GpxTest, TraccarUploaderTest, ObservationTest and ManualTimeTest.

public class AppSettingsTest
{
    [Fact]
    public void TheTimerBufferDefaultsToZeroAndIsKeptWithinLimits()
    {
        var s = new AppSettings(AirsportsDatabase.InMemory());
        Assert.Equal(0, s.TimerBufferMinutes);
        s.TimerBufferMinutes = 2;
        Assert.Equal(2, s.TimerBufferMinutes);
        s.TimerBufferMinutes = 99;
        Assert.Equal(30, s.TimerBufferMinutes);
        s.TimerBufferMinutes = -3;
        Assert.Equal(0, s.TimerBufferMinutes);
    }

    [Fact]
    public void TheOtherSettingsRoundTrip()
    {
        var s = new AppSettings(AirsportsDatabase.InMemory());
        Assert.False(s.TourSeen);
        s.TourSeen = true;
        Assert.True(s.TourSeen);
        Assert.Empty(s.DismissedAnnouncementIds);
        s.DismissedAnnouncementIds = new HashSet<int> { 5, 7 };
        Assert.Equal([5, 7], s.DismissedAnnouncementIds.Order());
        s.CachedProfileJson = "{}";
        Assert.Equal("{}", s.CachedProfileJson);
        s.CachedProfileJson = null;
        Assert.Null(s.CachedProfileJson);
    }

    [Fact]
    public void TheDatabaseFileKeepsItsContentsAcrossRestarts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"airsports-test-{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new AirsportsDatabase(path)) new AppSettings(db).TimerBufferMinutes = 4;
            using (var db = new AirsportsDatabase(path)) Assert.Equal(4, new AppSettings(db).TimerBufferMinutes);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(f)) File.Delete(f);
        }
    }
}

public class TrackLogTest
{
    private static TrackPoint Point(long t) => new(t, 59.0, 10.0, 5.0, 90.0, 100.0, 5.0, 80.0);

    [Fact]
    public void RecordsASessionAndListsItNewestFirst()
    {
        var log = new TrackLog(AirsportsDatabase.InMemory());
        var a = log.StartSession("dev", 1_000);
        log.Append(a, Point(1_000));
        log.Append(a, Point(2_000));
        log.EndSession(a, 2_500);
        var b = log.StartSession("dev", 10_000);
        log.Append(b, Point(10_000));
        var sessions = log.Sessions();
        Assert.Equal([b, a], sessions.Select(s => s.Id));
        Assert.True(sessions[0].Recording);
        Assert.Equal(2L, sessions[1].PointCount);
        Assert.Equal(1_000L, sessions[1].DurationMillis);
        Assert.Equal([1_000L, 2_000L], log.Points(a).Select(p => p.TimestampMillis));
    }

    [Fact]
    public void ASessionLeftOpenByACrashEndsAtItsLastPointWhenTheNextOneStarts()
    {
        var log = new TrackLog(AirsportsDatabase.InMemory());
        var a = log.StartSession("dev", 1_000);
        log.Append(a, Point(1_000));
        log.Append(a, Point(4_000));
        log.StartSession("dev", 9_000);
        Assert.Equal(4_000L, log.Sessions().First(s => s.Id == a).EndedAtMillis);
    }

    [Fact]
    public void ASessionLeftOpenByACrashIsClosedAtTheNextStartOfTheApp()
    {
        var log = new TrackLog(AirsportsDatabase.InMemory());
        var a = log.StartSession("dev", 1_000);
        log.Append(a, Point(3_000));
        log.CloseAbandonedSessions();
        Assert.Equal(3_000L, log.Sessions().Single().EndedAtMillis);
    }

    [Fact]
    public void DeletesOneSessionOrAllFinishedOnesButNeverTheOneBeingRecorded()
    {
        var log = new TrackLog(AirsportsDatabase.InMemory());
        var a = log.StartSession("dev", 1_000); log.Append(a, Point(1_000)); log.EndSession(a, 2_000);
        var b = log.StartSession("dev", 3_000); log.Append(b, Point(3_000)); log.EndSession(b, 4_000);
        var c = log.StartSession("dev", 5_000); log.Append(c, Point(5_000));
        log.Delete(a);
        Assert.Equal([c, b], log.Sessions().Select(s => s.Id));
        Assert.Empty(log.Points(a));
        log.DeleteAllFinished();
        Assert.Equal([c], log.Sessions().Select(s => s.Id));
        Assert.Single(log.Points(c));
        Assert.Null(log.Sessions().Single().EndedAtMillis);
    }
}

public class GpxTest
{
    // 2026-10-11T15:55:22Z
    private const long T = 1_791_734_122_000L;

    [Fact]
    public void TimesAreUtcWithTheZDesignator()
    {
        Assert.Equal("2026-10-11T15:55:22Z", Gpx.Time(T));
        Assert.Equal("2026-10-11T15:55:22.500Z", Gpx.Time(T + 500));
        Assert.Equal("airsports-2026-10-11-1555Z.gpx", Gpx.FileName(T));
    }

    [Fact]
    public void NumbersAreFixedDecimalsNeverExponents()
    {
        Assert.Equal("59.9139000", Gpx.Fixed(59.9139, 7));
        Assert.Equal("-0.0000100", Gpx.Fixed(-0.00001, 7));
        Assert.Equal("0.0000000", Gpx.Fixed(-0.00000001, 7));
        Assert.Equal("1234.6", Gpx.Fixed(1234.56, 1));
    }

    [Fact]
    public void WritesAGpx11TrackWithOnePointPerFix()
    {
        var gpx = Gpx.Build("Day 1 & <test>",
        [
            new LoggedPoint(T, 59.9139, 10.7522, 105.26, 40.0, 90.0, 5.0),
            new LoggedPoint(T + 1000, 59.9140, 10.7523, null, 40.0, 90.0, 5.0),
        ]);
        Assert.Contains("""<gpx version="1.1" creator="Airsports" xmlns="http://www.topografix.com/GPX/1/1" """, gpx);
        Assert.Contains("<name>Day 1 &amp; &lt;test&gt;</name>", gpx);
        Assert.Contains("""<trkpt lat="59.9139000" lon="10.7522000">""", gpx);
        Assert.Contains("<ele>105.3</ele>", gpx);
        Assert.Contains("<time>2026-10-11T15:55:22Z</time>", gpx);
        Assert.Contains("<time>2026-10-11T15:55:23Z</time>", gpx);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(gpx, "<trkpt ").Count);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(gpx, "<ele>"));
    }

    [Fact]
    public void BrokenValuesFromTheSimulatorNeverMakeACorruptFile()
    {
        var gpx = Gpx.Build("x",
        [
            new LoggedPoint(T, double.NaN, 10.7522, 100, 40.0, 90.0, 5.0), // no position: left out
            new LoggedPoint(T + 1000, 59.9, 200.0, 100, 40.0, 90.0, 5.0), // impossible longitude: left out
            new LoggedPoint(T + 2000, 59.9140, 10.7523, double.PositiveInfinity, 40.0, 90.0, 5.0), // kept, without altitude
        ]);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(gpx, "<trkpt "));
        Assert.Contains("""<trkpt lat="59.9140000" lon="10.7523000">""", gpx);
        Assert.DoesNotContain("<ele>", gpx);
    }

    [Fact]
    public void TheDecimalSeparatorIsADotWhateverTheRegionalSettings()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Contains("""lat="59.9139000" """.TrimEnd(), Gpx.Build("x", [new LoggedPoint(T, 59.9139, 10.0, 1.5, 0, null, null)]));
            Assert.Equal("1.5", Gpx.Fixed(1.5, 1));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }
}

public class TraccarUploaderTest
{
    private static TrackPoint Point(long t) => new(t, 59.0, 10.0, 10.0, 90.0, 100.0, 5.0, 3.0);

    [Fact]
    public void SameTimestampFixesDoNotOverwriteEachOther()
    {
        var q = new PositionQueue(AirsportsDatabase.InMemory());
        q.Enqueue("d", Point(1000));
        q.Enqueue("d", Point(1000));
        Assert.Equal(2, q.Size());
    }

    [Fact]
    public void TheQueueIsTrimmedToTheNewestPositions()
    {
        var q = new PositionQueue(AirsportsDatabase.InMemory(), maxSize: 3);
        for (var i = 1; i <= 5; i++) q.Enqueue("d", Point(i * 1000));
        Assert.Equal([3000L, 4000L, 5000L], q.Oldest(10).Select(p => p.TimestampMillis));
    }

    [Fact]
    public async Task DrainsInTimeOrderWithoutAuthHeader()
    {
        var q = new PositionQueue(AirsportsDatabase.InMemory());
        q.Enqueue("d", Point(3000)); q.Enqueue("d", Point(1000)); q.Enqueue("d", Point(2000));
        var times = new List<string>();
        string? authHeader = "unset";
        var uploader = new TraccarUploader(new HttpClient(new MockHandler(r =>
        {
            times.Add(System.Web.HttpUtility.ParseQueryString(r.Url.Query)["timestamp"]!);
            authHeader = r.Header("Authorization");
            return Status(HttpStatusCode.OK);
        })), q, "https://t.example/");
        Assert.Equal(new FlushResult.Done(3), await uploader.FlushAsync());
        Assert.Equal(["1", "2", "3"], times);
        Assert.Null(authHeader);
        Assert.Equal(0, q.Size());
    }

    [Fact]
    public async Task SendsSpeedInKnotsAsAPostWithQueryParameters()
    {
        var q = new PositionQueue(AirsportsDatabase.InMemory());
        q.Enqueue("sim1", new TrackPoint(1_700_000_000_000, 59.5, 10.25, 100 * TrackPoint.KnotsToMetersPerSecond, 270.0, 1500.0));
        SeenRequest? seen = null;
        await new TraccarUploader(new HttpClient(new MockHandler(r => { seen = r; return Status(HttpStatusCode.OK); })), q, "https://t.example/").FlushAsync();
        var query = System.Web.HttpUtility.ParseQueryString(seen!.Url.Query);
        Assert.Equal("POST", seen.Method);
        Assert.Equal("sim1", query["id"]);
        Assert.Equal("1700000000", query["timestamp"]);
        Assert.Equal("59.5", query["lat"]);
        Assert.Equal(100.0, double.Parse(query["speed"]!, System.Globalization.CultureInfo.InvariantCulture), 3);
        Assert.Equal("270", query["bearing"]);
        Assert.Equal("1500", query["altitude"]);
    }

    [Fact]
    public async Task StopsAtFirstFailureAndKeepsRemainder()
    {
        var q = new PositionQueue(AirsportsDatabase.InMemory());
        for (var i = 1; i <= 3; i++) q.Enqueue("d", Point(1000L * i));
        var n = 0;
        var result = await new TraccarUploader(new HttpClient(new MockHandler(_ => ++n == 2 ? Status(HttpStatusCode.InternalServerError) : Status(HttpStatusCode.OK))), q, "https://t.example/").FlushAsync();
        Assert.Equal(1, Assert.IsType<FlushResult.Failed>(result).Sent);
        Assert.Equal(2, q.Size());
    }
}

public class ObservationTest
{
    private const string Stored = """{"id":9,"photo":5,"photo_name":"A","time":"2026-10-10T10:00:00Z","status":"ok"}""";

    [Fact]
    public void TheDatabaseSchemaHasTheObservationTable() => Assert.Equal(4, AirsportsDatabase.SchemaVersion);

    [Fact]
    public void ANewerMarkOfThePhotoReplacesAnOlderOneThatIsStillWaiting()
    {
        var q = new ObservationQueue(AirsportsDatabase.InMemory());
        q.Add(1, 5, "2026-10-10T10:00:00Z", 59.0, 10.0);
        q.Add(1, 6, "2026-10-10T10:01:00Z", 59.1, 10.1);
        q.Add(1, 5, "2026-10-10T10:02:00Z", 59.2, 10.2);
        Assert.Equal([6L, 5L], q.All().Select(o => o.PhotoId));
        Assert.Equal("2026-10-10T10:02:00Z", q.All().Last().TimeIso);
    }

    [Fact]
    public async Task SendsMarksInOrderAndForgetsTheAcknowledgedOnes()
    {
        var q = new ObservationQueue(AirsportsDatabase.InMemory());
        q.Add(1, 5, "2026-10-10T10:00:00Z", 59.5, 10.0);
        var sent = new List<string>();
        var result = await new ObservationSender(With(new MockHandler(r => { sent.Add($"{r.Path} {r.Body}"); return Json(Stored, HttpStatusCode.Created); })), q).FlushAsync();
        Assert.Single(result.Sent);
        Assert.Empty(q.All());
        var only = Assert.Single(sent);
        Assert.StartsWith("/api/v1/contestant/1/photo_observations/", only);
        Assert.Contains("\"photo\":5", only);
        Assert.Contains("\"latitude\":59.5", only);
        Assert.DoesNotContain("replace", only);
    }

    [Fact]
    public async Task KeepsTheMarksWhenTheServerCannotBeReachedAndSendsThemLater()
    {
        var q = new ObservationQueue(AirsportsDatabase.InMemory());
        q.Add(1, 5, "2026-10-10T10:00:00Z", 59.0, 10.0);
        q.Add(1, 6, "2026-10-10T10:01:00Z", 59.0, 10.0);
        var online = false;
        var sender = new ObservationSender(With(new MockHandler(_ => online ? Json(Stored, HttpStatusCode.Created) : Json("{}", HttpStatusCode.ServiceUnavailable))), q);
        Assert.Equal(2, (await sender.FlushAsync()).StillWaiting);
        Assert.Equal(2, q.All().Count);
        online = true;
        Assert.Equal(2, (await sender.FlushAsync()).Sent.Count);
        Assert.Empty(q.All());
    }

    [Fact]
    public async Task GivesUpOnMarksTheServerRefusesForGoodAndSaysWhy()
    {
        var q = new ObservationQueue(AirsportsDatabase.InMemory());
        q.Add(1, 5, "2026-10-10T10:00:00Z", 59.0, 10.0);
        var result = await new ObservationSender(With(new MockHandler(_ => Json("""{"detail":"This flight is not one that you are currently tracking"}""", HttpStatusCode.Conflict))), q).FlushAsync();
        Assert.Empty(q.All());
        var rejected = Assert.Single(result.Rejected);
        Assert.Equal(5, rejected.PhotoId);
        Assert.Equal("This flight is not one that you are currently tracking", rejected.Reason);
    }

    [Fact]
    public async Task ReadsTheCrewPhotoListFromTheBackend()
    {
        const string body = """
            {"enabled":true,"may_register":true,"photos":[{"id":5,"name":"A","file":"https://x/a.jpg"},{"id":6,"name":"B","file":null}],
             "observations":[{"id":9,"photo":5,"photo_name":"A","time":"2026-10-10T10:00:00Z","scored":true,"status":"ok","error_mm":1.0}]}
            """;
        var path = "";
        var data = await With(new MockHandler(r => { path = r.Path; return Json(body); })).ObservationPhotosAsync(12);
        Assert.Equal("/api/v1/contestant/12/observation_photos/", path);
        Assert.Equal(["A", "B"], data.Photos.Select(p => p.Name));
        Assert.Null(data.Photos[1].File);
        Assert.Equal(5, data.Observations.Single().Photo);
        Assert.True(data.MayRegister);
    }

    [Fact]
    public async Task ReadsWhenRegistrationCloses()
    {
        var data = await With(new MockHandler(_ => Json("""{"enabled":true,"may_register":true,"registration_closes_at":"2026-08-01T12:05:00Z","registration_grace_minutes":5,"photos":[],"observations":[]}"""))).ObservationPhotosAsync(1);
        Assert.Equal("2026-08-01T12:05:00Z", data.RegistrationClosesAt);
        Assert.Equal(5, data.RegistrationGraceMinutes);
    }

    [Fact]
    public async Task RegisteringSurfacesTheServersReasonAsAnApiException()
    {
        var e = await Assert.ThrowsAsync<ApiException>(() => With(new MockHandler(_ => Json("""{"detail":"Observation registration is not enabled for this navigation task"}""", HttpStatusCode.Forbidden)))
            .RegisterObservationAsync(1, new PhotoObservationRequest { Photo = 5, Time = "t", Latitude = 1.0, Longitude = 2.0 }));
        Assert.Equal(403, e.Status);
        Assert.Equal("Observation registration is not enabled for this navigation task", e.Detail);
    }
}

public class ManualTimeTest
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
    private static readonly long Reference = Ms("2026-10-10T12:00:00Z"); // 14:00 in Oslo

    [Fact]
    public void ParsesATimeOfDayOnTheDayOfTheReference()
    {
        var parsed = ObservationRules.ParseTimeOfDay("14:03:27", Reference, Oslo);
        Assert.Equal(Ms("2026-10-10T12:03:27Z"), parsed);
        Assert.Equal(Ms("2026-10-10T12:03:00Z"), ObservationRules.ParseTimeOfDay("14:03", Reference, Oslo));
        Assert.Equal(parsed, ObservationRules.ParseTimeOfDay("14.03.27", Reference, Oslo));
    }

    [Theory]
    [InlineData("")]
    [InlineData("25:00")]
    [InlineData("14:60")]
    [InlineData("14:03:61")]
    [InlineData("abc")]
    [InlineData("14")]
    [InlineData("1:2:3:4")]
    [InlineData("14:3x")]
    public void RefusesWhatIsNotATime(string bad) => Assert.Null(ObservationRules.ParseTimeOfDay(bad, Reference, Oslo));

    [Fact]
    public void ShowsATimeAsHoursMinutesSeconds() => Assert.Equal("14:03:07", ObservationRules.TimeOfDayText(Ms("2026-10-10T12:03:07Z"), Oslo));
}
