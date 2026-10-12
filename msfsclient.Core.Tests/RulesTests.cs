using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.Observations;
using msfsclient.Core.Preflight;
using msfsclient.Core.Tracking;
using msfsclient.Core.Training;
using msfsclient.Core.Ui;
using static msfsclient.Core.Tests.Api;

namespace msfsclient.Core.Tests;

// Ports of OsmAndTest, ScoreTextTest, AppEventTest, ObservationRuleTest, WindowClosedTest, GateFeedbackTest, PreflightTest,
// EncodeTest and the version label: pure functions, no I/O.

public class OsmAndTest
{
    [Fact]
    public void TimestampIsSecondsAndOptionalFieldsAreOmitted()
    {
        var p = OsmAnd.Parameters("dev1", 1_700_000_123_456, 59.9, 10.7, 90.0, -1.0, null, null, null).ToDictionary(k => k.Key, k => k.Value);
        Assert.Equal("1700000123", p["timestamp"]);
        Assert.Equal("90", p["speed"]);
        Assert.Equal("dev1", p["id"]);
        Assert.False(p.ContainsKey("bearing"), "negative bearing means unknown");
        Assert.False(p.ContainsKey("batt"));
    }

    [Fact]
    public void NumbersUseADotWhateverTheRegionalSettings()
    {
        var before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("nb-NO");
            var p = OsmAnd.Parameters("d", 0, 59.91, 10.75, 101.5, 90.5, 1500.25, null, null).ToDictionary(k => k.Key, k => k.Value);
            Assert.Equal("59.91", p["lat"]);
            Assert.Equal("10.75", p["lon"]);
            Assert.Equal("101.5", p["speed"]);
            Assert.Equal("1500.25", p["altitude"]);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }
}

public class ScoreTextTest
{
    [Fact]
    public void AWholeScoreHasNoDecimalsOtherwiseOne()
    {
        Assert.Equal("17", TrackingTargets.ScoreText(17.0));
        Assert.Equal("17.5", TrackingTargets.ScoreText(17.5));
        Assert.Equal("0", TrackingTargets.ScoreText(0.0));
        Assert.Equal("12.3", TrackingTargets.ScoreText(12.34));
    }

    [Fact]
    public void APathOnTheSiteBecomesAFullAddress()
    {
        Assert.Equal("https://app.airsports.no/display/maplink/abc/", Urls.SiteUrl("/display/maplink/abc/"));
        Assert.Equal("https://app.airsports.no/competition-map/1/2", Urls.SiteUrl("https://app.airsports.no/competition-map/1/2"));
    }

    [Fact]
    public void TheVersionLabelShowsTheNameAndTheBuildNumber()
    {
        Assert.Equal("2.0.0 (100)", new DeviceInfo("msfs", 100, "2.0.0").VersionLabel);
        Assert.Equal("build 100", new DeviceInfo("msfs", 100).VersionLabel);
    }
}

public class AppEventTest
{
    [Fact] public void RegisteredCarriesTheIds() => Assert.Equal(new AppEvent.FlightRegistered(1, 2), AppEvent.Parse("airsportsapp://registered?contestId=1&navigationTaskId=2"));
    [Fact] public void IdsAreOptional() => Assert.Equal(new AppEvent.FlightRegistered(null, null), AppEvent.Parse("airsportsapp://registered"));
    [Fact] public void BadIdsAreIgnoredNotFatal() => Assert.Equal(new AppEvent.FlightRegistered(null, 2), AppEvent.Parse("airsportsapp://registered?contestId=abc&navigationTaskId=2"));
    [Fact] public void ATrailingSlashIsTolerated() => Assert.Equal(new AppEvent.FlightRegistered(null, null), AppEvent.Parse("airsportsapp://registered/"));
    [Fact] public void UnknownEventsAreIgnoredSoNewerPagesDoNotBreakOlderApps() => Assert.Null(AppEvent.Parse("airsportsapp://something-new?x=1"));

    [Fact]
    public void OnlyOurSchemeCounts()
    {
        Assert.Null(AppEvent.Parse("https://app.airsports.no/registered"));
        Assert.Null(AppEvent.Parse("evilscheme://registered"));
        Assert.True(AppEvent.IsAppEventUrl("airsportsapp://registered"));
        Assert.False(AppEvent.IsAppEventUrl("https://airsports.no/"));
    }
}

public class ObservationRuleTest
{
    private const string Closes = "2026-08-01T12:05:00Z";
    private readonly ObservationsUi _ui = new() { Enabled = true, MayRegister = true, RegistrationClosesAt = Closes, GraceMinutes = 5 };

    [Fact]
    public void TheRuleIsWrittenOutWithTheMinutesFromTheServer()
    {
        var text = ObservationRules.RuleText(_ui);
        Assert.Contains("5 minutes after your tracking window closes", text);
        Assert.Contains("nothing can be marked or changed", text);
        Assert.DoesNotContain("final", text);
        Assert.Contains("7 minutes", ObservationRules.RuleText(_ui with { GraceMinutes = 7 }));
    }

    [Fact]
    public void MarkingIsReportedClosedOnceTheDeadlineHasPassed()
    {
        Assert.Null(ObservationRules.Blocker(_ui, trackingRunning: true, hasFreshFix: true, nowMillis: Ms("2026-08-01T12:04:00Z")));
        var closed = ObservationRules.Blocker(_ui with { MayRegister = false }, trackingRunning: true, hasFreshFix: true, nowMillis: Ms("2026-08-01T12:06:00Z"));
        Assert.StartsWith("Marking has closed", closed);
        // Without a deadline from the server (older backend) the old messages apply.
        Assert.Equal("Marking opens when your tracking window starts.",
            ObservationRules.Blocker(new ObservationsUi { Enabled = true, MayRegister = false }, true, true, Ms("2026-08-01T12:06:00Z")));
    }

    [Fact]
    public void AMarkNeedsTrackingAndAFreshPosition()
    {
        Assert.StartsWith("Start tracking", ObservationRules.Blocker(_ui, trackingRunning: false, hasFreshFix: false, nowMillis: Ms("2026-08-01T12:00:00Z")));
        Assert.StartsWith("Waiting for a position", ObservationRules.Blocker(_ui, trackingRunning: true, hasFreshFix: false, nowMillis: Ms("2026-08-01T12:00:00Z")));
    }

    [Fact]
    public void TheCounterCountsStoredAndWaitingMarksOnce()
    {
        var ui = new ObservationsUi
        {
            Photos = [new() { Id = 1, Name = "A" }, new() { Id = 2, Name = "B" }, new() { Id = 3, Name = "C" }],
            Registered = System.Collections.Immutable.ImmutableDictionary<int, PhotoObservation>.Empty.Add(1, new PhotoObservation { Id = 9, Photo = 1 }),
            Pending = [1, 2],
        };
        Assert.Equal("2/3", ui.Counter);
        Assert.Null(new ObservationsUi().Counter);
    }
}

public class WindowClosedTest
{
    private const long Now = 1_000_000_000_000L;
    private static long Minutes(long n) => n * 60_000L;

    [Fact]
    public void WarnsOnceTheWindowHasBeenClosedLongerThanTheGraceTime()
    {
        Assert.Equal(30L, WindowClosed.MinutesAgo(true, [], Now - Minutes(30), [], Now));
        Assert.Null(WindowClosed.MinutesAgo(true, [], Now - Minutes(9), [], Now)); // still within the grace time
    }

    [Fact]
    public void NoWarningWhenTrackingIsOffOrNoWindowHasBeenSeen()
    {
        Assert.Null(WindowClosed.MinutesAgo(false, [], Now - Minutes(30), [], Now));
        Assert.Null(WindowClosed.MinutesAgo(true, [], null, [], Now)); // tracking started before the first window
    }

    [Fact]
    public void NoWarningWhileAFlightIsOpenOrTheNextOneOpensWithinTheHour()
    {
        Assert.Null(WindowClosed.MinutesAgo(true, [Now + Minutes(20)], Now - Minutes(30), [], Now));
        Assert.Null(WindowClosed.MinutesAgo(true, [], Now - Minutes(30), [Now + Minutes(40)], Now));
        Assert.Equal(30L, WindowClosed.MinutesAgo(true, [], Now - Minutes(30), [Now + Minutes(90)], Now));
    }

    [Fact]
    public void UsesTheLatestEndWhenSeveralFlightsClosed() =>
        Assert.Equal(15L, WindowClosed.MinutesAgo(true, [Now - Minutes(15)], Now - Minutes(60), [], Now));
}

public class GateFeedbackTest
{
    [Fact]
    public void WordsTheOffsetAgainstTheRequiredTime()
    {
        Assert.Equal("3 s late", new GateFeedback("TP1", 3.0).Text);
        Assert.Equal("5 s early", new GateFeedback("TP1", -4.6).Text);
        Assert.Equal("on time", new GateFeedback("TP1", 1.5).Text);
        Assert.Equal(GateTiming.Early, new GateFeedback("TP1", -2.5).Timing);
    }

    [Fact]
    public void UsesTheServersLastGateAndIgnoresFlightsWithoutFeedback()
    {
        var none = new ActiveContestant { Id = 1 };
        var with = new ActiveContestant { Id = 2, TrainingFeedback = new TrainingFeedback { LastGate = "TP2", LastGateTimeOffset = -7.0 } };
        Assert.Null(GateFeedback.Latest([none]));
        Assert.Equal(new GateFeedback("TP2", -7.0), GateFeedback.Latest([none, with]));
    }

    [Fact]
    public void TrainingModeIsOfferedOnlyWhenTheServerSaysTheFlightSupportsIt()
    {
        static CurrentTask Task(bool? available) => new()
        {
            NavigationTask = new NavigationTaskSummary { Id = 1 }, ActiveContestants = [new ActiveContestant { Id = 1, TrainingAvailable = available }],
        };
        Assert.True(GateFeedback.TrainingAvailable([Task(true)]));
        Assert.False(GateFeedback.TrainingAvailable([Task(false)]));
        Assert.False(GateFeedback.TrainingAvailable([Task(null)])); // older backend
        Assert.False(GateFeedback.TrainingAvailable([]));
    }
}

public class PreflightTest
{
    private static IReadOnlyList<PreflightCheck> Checks(bool flight = true, bool? online = null, ClockStatus? clock = null, SimStatus? sim = null) =>
        PreflightChecks.Of(flight, online, clock ?? new ClockStatus.Unknown(), sim ?? new SimStatus.Receiving(0));

    [Fact]
    public void LeavesOutWhatCannotBeToldAndHasNoSubscriptionRow()
    {
        Assert.Equal(["Flight", "Clock", "Simulator"], Checks().Select(c => c.Title));
        Assert.Equal(["Flight", "Internet", "Clock", "Simulator"], Checks(online: true).Select(c => c.Title));
    }

    [Fact]
    public void AClockThatIsOffIsAProblemWithAFix()
    {
        var clock = Checks(clock: ClockCheck.Status(-7_400, ClockReference.Network)).Single(c => c.Title == "Clock");
        Assert.Equal(CheckStatus.Problem, clock.Status);
        Assert.Equal(CheckAction.OpenClockSettings, clock.Action);
        Assert.Contains("7 s ahead", clock.Detail);
    }

    [Fact]
    public void ADisconnectedSimulatorIsAProblemAndSummaryCountsProblemsFirst()
    {
        var list = Checks(flight: false, online: true, sim: new SimStatus.NotConnected());
        Assert.Equal(CheckStatus.Problem, list.Single(c => c.Title == "Simulator").Status);
        Assert.Equal(CheckAction.ConnectSimulator, list.Single(c => c.Title == "Simulator").Action);
        Assert.Equal("1 to fix", PreflightChecks.Summary(list));
        Assert.Contains(list, c => c.Title == "Flight" && c.Status == CheckStatus.Warning);
    }

    [Fact]
    public void SummaryWordsWarningsAndAllClear()
    {
        Assert.Equal("1 to look at", PreflightChecks.Summary(Checks(flight: false, clock: ClockCheck.Status(0, ClockReference.Network))));
        Assert.Equal("All clear", PreflightChecks.Summary(Checks(online: true, clock: ClockCheck.Status(0, ClockReference.Network))));
    }
}

public class SimStatusTest
{
    [Fact] public void NotConnectedWhateverTheLastPosition() => Assert.Equal(new SimStatus.NotConnected(), SimStatus.Of(false, 1_000, 2_000));
    [Fact] public void ConnectedWithoutAPositionWaits() => Assert.Equal(new SimStatus.WaitingForPosition(), SimStatus.Of(true, null, 2_000));

    [Fact]
    public void PositionsUpToFiveSecondsOldAreReceivingThenLost()
    {
        Assert.Equal(new SimStatus.Receiving(5), SimStatus.Of(true, 95_000, 100_999));
        Assert.Equal(new SimStatus.Lost(6), SimStatus.Of(true, 95_000, 101_000));
    }

    [Fact] public void APositionFromTheFutureNeverGivesANegativeAge() => Assert.Equal(new SimStatus.Receiving(0), SimStatus.Of(true, 105_000, 100_000));
}

public class EncodeTest
{
    [Fact] public void EncodesReservedCharacters() => Assert.Equal("%2F%3Ftab%3Dupcoming%26x%3D1", Urls.EncodeQueryComponent("/?tab=upcoming&x=1"));
    [Fact] public void KeepsUnreserved() => Assert.Equal("Abc-1._~", Urls.EncodeQueryComponent("Abc-1._~"));
    [Fact] public void EncodesUtf8() => Assert.Equal("%C3%B8", Urls.EncodeQueryComponent("ø"));

    [Fact]
    public void TheWebViewSignsInOnTheAppHostWithTheEmbeddedPage() =>
        Assert.Equal("https://app.airsports.no/firebase_login/?next=%2Fopen-today%3Fembed%3Dapp", Urls.FirebaseLoginUrl("/open-today"));
}

public class TimerTest
{
    [Fact] public void PressedJustAfterAMinuteStartsAtThatMinute() => Assert.Equal(Ms("2026-10-10T10:03:00Z"), AdaptiveStartTimer.OriginFor(Ms("2026-10-10T10:03:20Z")));
    [Fact] public void PressedJustBeforeAMinuteStartsAtTheNextMinute() => Assert.Equal(Ms("2026-10-10T10:04:00Z"), AdaptiveStartTimer.OriginFor(Ms("2026-10-10T10:03:40Z")));
    [Fact] public void ExactlyThirtySecondsRoundsUpLikeTheBackend() => Assert.Equal(Ms("2026-10-10T10:04:00Z"), AdaptiveStartTimer.OriginFor(Ms("2026-10-10T10:03:30Z")));
    [Fact] public void JustUnderThirtySecondsRoundsDown() => Assert.Equal(Ms("2026-10-10T10:03:00Z"), AdaptiveStartTimer.OriginFor(Ms("2026-10-10T10:03:29Z") + 999));
    [Fact] public void AnExactMinuteStaysPut() => Assert.Equal(Ms("2026-10-10T10:03:00Z"), AdaptiveStartTimer.OriginFor(Ms("2026-10-10T10:03:00Z")));

    [Fact]
    public void ABufferMovesTheStartMinuteLaterByWholeMinutes()
    {
        Assert.Equal(Ms("2026-10-10T10:05:00Z"), AdaptiveStartTimer.OriginFor(Ms("2026-10-10T10:03:20Z"), bufferMinutes: 2));
        // Pressed exactly on the minute: the timer starts at exactly -2:00.
        var pressed = Ms("2026-10-10T10:03:00Z");
        Assert.Equal("-2:00", AdaptiveStartTimer.Format(AdaptiveStartTimer.ElapsedMillis(AdaptiveStartTimer.OriginFor(pressed, 2), pressed)));
        Assert.Equal(AdaptiveStartTimer.OriginFor(pressed), AdaptiveStartTimer.OriginFor(pressed, 0));
    }

    [Fact]
    public void RoundsAcrossHourAndDayBoundaries()
    {
        Assert.Equal(Ms("2026-10-11T00:00:00Z"), AdaptiveStartTimer.OriginFor(Ms("2026-10-10T23:59:45Z")));
        Assert.Equal(Ms("2026-10-10T11:00:00Z"), AdaptiveStartTimer.OriginFor(Ms("2026-10-10T10:59:31Z")));
    }

    [Fact]
    public void CountsDownBeforeTheStartMinuteAndUpAfterIt()
    {
        var origin = Ms("2026-10-10T10:04:00Z");
        Assert.Equal("-0:20", AdaptiveStartTimer.Format(AdaptiveStartTimer.ElapsedMillis(origin, Ms("2026-10-10T10:03:40Z"))));
        Assert.Equal("-0:01", AdaptiveStartTimer.Format(AdaptiveStartTimer.ElapsedMillis(origin, origin - 400)));
        Assert.Equal("0:00", AdaptiveStartTimer.Format(AdaptiveStartTimer.ElapsedMillis(origin, origin)));
        Assert.Equal("0:00", AdaptiveStartTimer.Format(AdaptiveStartTimer.ElapsedMillis(origin, origin + 999)));
        Assert.Equal("0:01", AdaptiveStartTimer.Format(AdaptiveStartTimer.ElapsedMillis(origin, origin + 1000)));
    }

    [Fact]
    public void ShowsMinutesAndHours()
    {
        Assert.Equal("2:05", AdaptiveStartTimer.Format(125_000));
        Assert.Equal("1:02:05", AdaptiveStartTimer.Format(3_725_000));
        Assert.Equal("-1:00", AdaptiveStartTimer.Format(-60_000));
    }

    [Fact]
    public void MatchesTheDocumentedExample()
    {
        // Pressed at 10:03:20 -> origin 10:03:00, so the timer already shows 0:20.
        var pressed = Ms("2026-10-10T10:03:20Z");
        Assert.Equal("0:20", AdaptiveStartTimer.Format(AdaptiveStartTimer.ElapsedMillis(AdaptiveStartTimer.OriginFor(pressed), pressed)));
    }
}

public class ClockFormatTest
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    [Fact]
    public void ShowsSecondsInTheContestZone()
    {
        // 08:03:27 UTC is 10:03:27 in Oslo in October (UTC+2): the zone flight orders use.
        Assert.Equal("10:03:27", TimeFormat.FormatClock(Ms("2026-10-10T08:03:27Z"), Oslo));
        Assert.Equal("08:03:27", TimeFormat.FormatClock(Ms("2026-10-10T08:03:27Z"), TimeZoneInfo.Utc));
    }

    [Fact]
    public void PadsAndWrapsAroundMidnight()
    {
        Assert.Equal("00:00:05", TimeFormat.FormatClock(Ms("2026-10-10T22:00:05Z"), Oslo));
        Assert.Equal("09:05:07", TimeFormat.FormatClock(Ms("2026-10-10T09:05:07Z"), TimeZoneInfo.Utc));
    }

    [Fact] public void MinuteTimesDropTheSeconds() => Assert.Equal("10:04", TimeFormat.FormatMinute(Ms("2026-10-10T08:04:00Z"), Oslo));

    [Fact]
    public void AnUnknownZoneFallsBackToTheDevice()
    {
        Assert.Equal(TimeZoneInfo.Local, TimeFormat.ZoneOrDevice("Not/AZone"));
        Assert.Equal(TimeZoneInfo.Local, TimeFormat.ZoneOrDevice(null));
        Assert.Equal("Europe/Oslo", TimeFormat.ZoneLabel(TimeFormat.ZoneOrDevice("Europe/Oslo")));
    }

    [Fact]
    public void ThePreviewShowsWhereAPressRightNowWouldStartTheTimer()
    {
        // At 10:03:40 a press starts the timer at 10:04:00; at 10:03:20 it starts at 10:03:00 (already running).
        Assert.Equal("10:04:00", TimeFormat.FormatClock(AdaptiveStartTimer.OriginFor(Ms("2026-10-10T08:03:40Z")), Oslo));
        Assert.Equal("10:03:00", TimeFormat.FormatClock(AdaptiveStartTimer.OriginFor(Ms("2026-10-10T08:03:20Z")), Oslo));
    }

    [Fact]
    public void FormatsLocalTimesAndDurations()
    {
        Assert.Equal("2026-10-10 11:05", TimeFormat.FormatLocal("2026-10-10T09:05:00Z", Oslo));
        Assert.Equal("Time not set", TimeFormat.FormatLocal(null));
        Assert.Equal("garbage", TimeFormat.FormatLocal("garbage"));
        Assert.Equal("11:05", TimeFormat.FormatTimeLocal("2026-10-10T09:05:00Z", Oslo));
        Assert.Equal("-", TimeFormat.FormatTimeLocal(null));
        Assert.Equal("1 h 12 min", TimeFormat.FormatDuration(72 * 60_000));
        Assert.Equal("7 min 30 s", TimeFormat.FormatDuration(450_000));
        Assert.Equal("45 s", TimeFormat.FormatDuration(45_000));
        Assert.Equal("less than a minute", TimeFormat.TrackedFor(59_000));
        Assert.Equal("1 h 5 min", TimeFormat.TrackedFor(65 * 60_000));
    }

    [Fact]
    public void ServerTimesAreParsedStrictly()
    {
        Assert.Equal(Ms("2026-10-10T09:05:00Z"), IsoTime.EpochMillisOrNull("2026-10-10T11:05:00+02:00"));
        Assert.Equal(Ms("2026-10-10T09:05:00Z") + 123, IsoTime.EpochMillisOrNull("2026-10-10T09:05:00.123456Z"));
        Assert.Null(IsoTime.EpochMillisOrNull("10:00"));
        Assert.Null(IsoTime.EpochMillisOrNull("garbage"));
    }
}

public class ClockCheckTest
{
    [Fact] public void UnknownWithoutAMeasurement() => Assert.Equal(new ClockStatus.Unknown(), ClockCheck.Status(null));

    [Fact]
    public void SmallDifferencesAreInSync()
    {
        Assert.Equal(new ClockStatus.InSync(0), ClockCheck.Status(0));
        Assert.Equal(new ClockStatus.InSync(1_999), ClockCheck.Status(1_999));
        Assert.Equal(new ClockStatus.InSync(-1_999), ClockCheck.Status(-1_999));
    }

    [Fact]
    public void TwoSecondsOrMoreIsDrift()
    {
        Assert.IsType<ClockStatus.Drift>(ClockCheck.Status(2_000));
        Assert.IsType<ClockStatus.Drift>(ClockCheck.Status(-2_000));
    }

    [Fact]
    public void ReportsDirectionAndWholeSeconds()
    {
        var ahead = Assert.IsType<ClockStatus.Drift>(ClockCheck.Status(-7_400));
        Assert.True(ahead.ClockIsAhead);
        Assert.Equal(7, ahead.Seconds);
        var behind = Assert.IsType<ClockStatus.Drift>(ClockCheck.Status(12_900));
        Assert.False(behind.ClockIsAhead);
        Assert.Equal(12, behind.Seconds);
    }

    [Fact]
    public void ReferenceNowCorrectsTheClock()
    {
        Assert.Equal(1_000_000L, ClockCheck.ReferenceNow(1_000_000L, null));
        Assert.Equal(1_007_000L, ClockCheck.ReferenceNow(1_000_000L, 7_000));
        Assert.Equal(993_000L, ClockCheck.ReferenceNow(1_000_000L, -7_000));
    }

    [Fact]
    public void NetworkDriftCarriesItsReference()
    {
        var d = Assert.IsType<ClockStatus.Drift>(ClockCheck.Status(9_000, ClockReference.Network));
        Assert.Equal(ClockReference.Network, d.Reference);
        Assert.Equal(9, d.Seconds);
    }

    [Fact]
    public void ANoisyMeasurementNeverWarns()
    {
        // 2.5 s apart but the round trip alone could explain 1.5 s of error on each side: not clear evidence.
        Assert.IsType<ClockStatus.InSync>(ClockCheck.Status(2_500, ClockReference.Network, uncertaintyMillis: 1_500));
        Assert.IsType<ClockStatus.Drift>(ClockCheck.Status(2_500, ClockReference.Network, uncertaintyMillis: 100));
    }
}

public class ReceivingStatusTest
{
    private static readonly long Now = Ms("2026-10-10T10:00:30Z");

    [Fact] public void NoTask() => Assert.Equal(new ReceivingStatus.NoActiveTask(), ReceivingStatus.Of(false, null, Now));
    [Fact] public void ScheduledButNothingYet() => Assert.Equal(new ReceivingStatus.WaitingForFirstPosition(), ReceivingStatus.Of(true, null, Now));
    [Fact] public void FreshPosition() => Assert.Equal(new ReceivingStatus.Receiving(5), ReceivingStatus.Of(true, "2026-10-10T10:00:25Z", Now));
    [Fact] public void OldPositionIsStale() => Assert.Equal(new ReceivingStatus.Stale(90), ReceivingStatus.Of(true, "2026-10-10T09:59:00Z", Now));
    [Fact] public void UnparseableTimeCountsAsNothingYet() => Assert.Equal(new ReceivingStatus.WaitingForFirstPosition(), ReceivingStatus.Of(true, "not a time", Now));
    [Fact] public void ClockSkewNeverGoesNegative() => Assert.Equal(new ReceivingStatus.Receiving(0), ReceivingStatus.Of(true, "2026-10-10T10:01:00Z", Now));

    private static ActiveContestant C(bool? running = null, bool? finished = null) => new() { Id = 1, CalculatorRunning = running, CalculatorFinished = finished };

    [Fact]
    public void ScoringUnknownWithoutDataOrOnOlderBackends()
    {
        Assert.Equal(ScoringStatus.Unknown, ServerStatus.Scoring([]));
        Assert.Equal(ScoringStatus.Unknown, ServerStatus.Scoring([C()]));
    }

    [Fact] public void ScoringRunningWinsOverEverythingElse() => Assert.Equal(ScoringStatus.Running, ServerStatus.Scoring([C(running: false), C(running: true)]));
    [Fact] public void ScoringNotStartedUntilThePositionsStartTheCalculator() => Assert.Equal(ScoringStatus.NotStarted, ServerStatus.Scoring([C(running: false, finished: false)]));
    [Fact] public void ScoringFinishedWhenAllCalculatorsFinished() => Assert.Equal(ScoringStatus.Finished, ServerStatus.Scoring([C(running: false, finished: true)]));

    [Fact]
    public void ScoringRunningProvesThatPositionsArrivedEvenBeforeTheirTimeIsKnown() =>
        Assert.Equal(new ReceivingStatus.ReceivingTimeNotYetKnown(), ReceivingStatus.Of(true, null, Now, scoringConfirmsPositions: true));

    [Fact]
    public void OnceTheTimeArrivesTheAgeIsShownAndKeepsUpdating()
    {
        Assert.Equal(new ReceivingStatus.Receiving(5), ReceivingStatus.Of(true, "2026-10-10T10:00:25Z", Now, scoringConfirmsPositions: true));
        Assert.Equal(new ReceivingStatus.Receiving(15), ReceivingStatus.Of(true, "2026-10-10T10:00:25Z", Now + 10_000, scoringConfirmsPositions: true));
    }

    [Fact]
    public void AnUnparseableTimeWhileScoringIsNotAWarningEither() =>
        Assert.Equal(new ReceivingStatus.ReceivingTimeNotYetKnown(), ReceivingStatus.Of(true, "garbage", Now, scoringConfirmsPositions: true));

    [Fact]
    public void ServerBatchingAndPollingDelayDoNotRaiseTheStaleWarning()
    {
        // Positions are saved every <= 10 s and polled every 10 s: ~20 s old is normal, 45 s is not.
        Assert.Equal(new ReceivingStatus.Receiving(20), ReceivingStatus.Of(true, "2026-10-10T10:00:10Z", Now));
        Assert.Equal(new ReceivingStatus.Receiving(45), ReceivingStatus.Of(true, "2026-10-10T09:59:45Z", Now));
        Assert.Equal(new ReceivingStatus.Stale(46), ReceivingStatus.Of(true, "2026-10-10T09:59:44Z", Now));
    }

    [Fact]
    public void ACalculationDelayDoesNotMakeAHealthyFlightLookStale()
    {
        var delayed = new ActiveContestant { Id = 1, LastPositionTime = "2026-10-10T11:50:00Z", LastReceivedTime = "2026-10-10T11:59:58Z", CalculationDelayMinutes = 10.0 };
        Assert.Equal("2026-10-10T11:59:58Z", ServerStatus.LastHeardIso([delayed]));
        Assert.Equal(10, ServerStatus.MapDelayMinutes([delayed]));
    }

    [Fact]
    public void StoredPositionsAreNotTrustedForReceptionWhenTheTaskDelaysThem()
    {
        Assert.Null(ServerStatus.LastHeardIso([new ActiveContestant { Id = 1, LastPositionTime = "2026-10-10T11:50:00Z", CalculationDelayMinutes = 10.0 }]));
        Assert.Equal("2026-10-10T11:59:00Z", ServerStatus.LastHeardIso([new ActiveContestant { Id = 2, LastPositionTime = "2026-10-10T11:59:00Z", CalculationDelayMinutes = 0.0 }]));
        Assert.Equal("2026-10-10T11:59:00Z", ServerStatus.LastHeardIso([new ActiveContestant { Id = 3, LastPositionTime = "2026-10-10T11:59:00Z" }]));
    }
}

public class NtpTest
{
    private const long T1 = 1_786_356_000_123L; // PC clock when the request left

    /// <summary>A server reply built like a real one: server clock = PC clock + serverAheadMillis.</summary>
    private static byte[] Reply(byte[] request, long serverAheadMillis, long oneWayMillis = 20, long processingMillis = 2,
        int mode = 4, int leap = 0, int stratum = 2, bool echo = true)
    {
        var response = new byte[Ntp.PacketSize];
        response[0] = (byte)((leap << 6) | (4 << 3) | mode);
        response[1] = (byte)stratum;
        if (echo) for (var i = 0; i < 8; i++) response[24 + i] = request[40 + i];
        var t2 = T1 + oneWayMillis + serverAheadMillis;
        Ntp.WriteTimestamp(response, 32, t2);
        Ntp.WriteTimestamp(response, 40, t2 + processingMillis);
        return response;
    }

    private static long ReceivedAt(long oneWay = 20, long processing = 2) => T1 + oneWay + processing + oneWay;

    [Fact]
    public void TimestampsRoundTripToTheMillisecond()
    {
        var buffer = new byte[8];
        foreach (var millis in new[] { 0L, 1L, 999L, 1_786_356_000_123L, 1_000_000_000_999L })
        {
            Ntp.WriteTimestamp(buffer, 0, millis);
            Assert.Equal(millis, Ntp.ReadTimestamp(buffer, 0));
        }
    }

    [Fact]
    public void RequestIsAClientModeV4Packet()
    {
        var request = Ntp.Request(T1);
        Assert.Equal(Ntp.PacketSize, request.Length);
        Assert.Equal(0x23, request[0]);
        Assert.Equal(T1, Ntp.ReadTimestamp(request, 40));
    }

    [Fact]
    public void MeasuresAClockThatIsBehindTheNetwork()
    {
        var request = Ntp.Request(T1);
        var m = Ntp.Measure(request, Reply(request, serverAheadMillis: 7_000), T1, ReceivedAt());
        Assert.NotNull(m);
        Assert.Equal(7_000, m.OffsetMillis); // symmetric delay: the offset is exact
        Assert.Equal(40, m.DelayMillis);
    }

    [Fact]
    public void MeasuresAClockThatIsAhead()
    {
        var request = Ntp.Request(T1);
        Assert.Equal(-12_500, Ntp.Measure(request, Reply(request, serverAheadMillis: -12_500), T1, ReceivedAt())!.OffsetMillis);
    }

    [Fact]
    public void AnAccurateClockMeasuresAboutZero()
    {
        var request = Ntp.Request(T1);
        Assert.Equal(0, Ntp.Measure(request, Reply(request, serverAheadMillis: 0), T1, ReceivedAt())!.OffsetMillis);
    }

    [Fact]
    public void RejectsAnythingThatIsNotATrustworthyServerAnswer()
    {
        var request = Ntp.Request(T1);
        var received = ReceivedAt();
        Assert.Null(Ntp.Measure(request, Reply(request, 0)[..20], T1, received)); // truncated
        Assert.Null(Ntp.Measure(request, Reply(request, 0, mode: 3), T1, received)); // not a server reply
        Assert.Null(Ntp.Measure(request, Reply(request, 0, leap: 3), T1, received)); // server is unsynchronised
        Assert.Null(Ntp.Measure(request, Reply(request, 0, stratum: 0), T1, received)); // kiss-o-death
        Assert.Null(Ntp.Measure(request, Reply(request, 0, stratum: 16), T1, received)); // stratum out of range
        Assert.Null(Ntp.Measure(request, Reply(request, 0, echo: false), T1, received)); // does not echo our request
    }
}

public class NtpClientTest
{
    [Fact]
    public async Task MeasuresTheOffsetOfAServerThatIsSevenSecondsAhead()
    {
        using var server = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        var port = ((System.Net.IPEndPoint)server.Client.LocalEndPoint!).Port;
        using var cts = new CancellationTokenSource();
        var serving = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                var datagram = await server.ReceiveAsync(cts.Token);
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 7_000;
                var response = new byte[Ntp.PacketSize];
                response[0] = 0x24; // leap 0, v4, mode 4 (server)
                response[1] = 2;
                for (var i = 0; i < 8; i++) response[24 + i] = datagram.Buffer[40 + i];
                Ntp.WriteTimestamp(response, 32, now);
                Ntp.WriteTimestamp(response, 40, now);
                await server.SendAsync(response, response.Length, datagram.RemoteEndPoint);
            }
        });

        var m = await new NtpClient(["127.0.0.1"], port, timeoutMillis: 2_000).MeasureAsync();

        Assert.NotNull(m);
        Assert.InRange(m.OffsetMillis, 6_900, 7_100);
        Assert.InRange(m.DelayMillis, 0, 500);
        cts.Cancel();
        try { await serving; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task GivesUpQuicklyWhenNoServerAnswers()
    {
        // A bound socket that never replies stands in for a blocked or unreachable time server.
        using var silent = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        var port = ((System.Net.IPEndPoint)silent.Client.LocalEndPoint!).Port;
        var started = Environment.TickCount64;
        var result = await new NtpClient(["127.0.0.1"], port, samplesPerServer: 1, timeoutMillis: 300).MeasureAsync();
        Assert.Null(result);
        Assert.True(Environment.TickCount64 - started < 3_000);
    }
}
