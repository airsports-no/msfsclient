using msfsclient.Core.Api;
using msfsclient.Core.App;
using msfsclient.Core.Ui;

namespace msfsclient.Core.Tests;

public class DayHeadingTests
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
    // Saturday 17 October 2026, 14:00 in Oslo
    private static readonly long Now = DateTimeOffset.Parse("2026-10-17T12:00:00Z").ToUnixTimeMilliseconds();

    private static string Heading(string? iso) => TimeFormat.DayHeading(iso, Now, Oslo);

    [Fact]
    public void TodayTomorrowAndYesterdayAreNamed()
    {
        Assert.Equal("Today", Heading("2026-10-17T05:30:00Z"));
        Assert.Equal("Tomorrow", Heading("2026-10-18T09:00:00Z"));
        Assert.Equal("Yesterday", Heading("2026-10-16T09:00:00Z"));
    }

    [Fact]
    public void OtherDaysShowTheWeekdayAndDate()
    {
        Assert.Equal("Sat 24 Oct", Heading("2026-10-24T09:00:00Z"));
        Assert.Equal("Mon 5 Oct", Heading("2026-10-05T09:00:00Z"));
    }

    [Fact]
    public void TheYearIsShownOnlyWhenItIsNotThisYear()
    {
        Assert.Equal("Fri 1 Jan 2027", Heading("2027-01-01T09:00:00Z"));
        Assert.Equal("Sat 4 Jul", Heading("2026-07-04T09:00:00Z"));
    }

    [Fact]
    public void TheDayIsTheOneInTheGivenZoneNotInUtc()
    {
        // 23:30 UTC on the 17th is already the 18th in Oslo
        Assert.Equal("Tomorrow", Heading("2026-10-17T23:30:00Z"));
    }

    [Fact]
    public void NoTimeOrAnUnreadableTimeIsSaidPlainly()
    {
        Assert.Equal("Time not set", Heading(null));
        Assert.Equal("soon", Heading("soon"));
    }
}

public class RegistrationWindowTests
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
    // Saturday 17 October 2026, 14:00 in Oslo
    private static readonly long Now = DateTimeOffset.Parse("2026-10-17T12:00:00Z").ToUnixTimeMilliseconds();

    private static string Window(string start, string finish) => TimeFormat.RegistrationWindow(start, finish, Now, Oslo);

    [Fact]
    public void ATodayWindowShowsTheTimesOnly() =>
        Assert.Equal("09:00 and 17:00", Window("2026-10-17T07:00:00Z", "2026-10-17T15:00:00Z"));

    [Fact]
    public void AWindowThatIsNotTodayNamesItsDay() =>
        Assert.Equal("Tomorrow 04:00 and 12:00", Window("2026-10-18T02:00:00Z", "2026-10-18T10:00:00Z"));

    [Fact]
    public void AWindowThatRunsIntoAnotherDayNamesBothDays() =>
        Assert.Equal("10:00 and Tomorrow 02:00", Window("2026-10-17T08:00:00Z", "2026-10-18T00:00:00Z"));
}

public class OpenRegistrationTests
{
    private static OpenRegistrationTask Task(int contest, int task, double? lat = null, double? lon = null) =>
        new() { ContestId = contest, ContestName = $"Contest {contest}", NavigationTaskId = task, NavigationTaskName = $"Task {task}", Latitude = lat, Longitude = lon };

    private static readonly Coordinates Oslo = new(59.914, 10.752);

    [Fact]
    public void TasksAreGroupedByContestInTheServersOrderWithoutAPosition()
    {
        var events = OpenRegistration.Group([Task(1, 10), Task(1, 11), Task(2, 20)], null);
        Assert.Equal([1, 2], events.Select(e => e.ContestId));
        Assert.Equal([10, 11], events[0].Tasks.Select(t => t.NavigationTaskId));
        Assert.Null(events[0].DistanceKm);
    }

    [Fact]
    public void TheNearestVenueComesFirstAndVenuesWithoutAPositionLast()
    {
        var events = OpenRegistration.Group([Task(1, 10, 60.39, 5.32), Task(2, 20), Task(3, 30, 59.95, 10.8)], Oslo);
        Assert.Equal([3, 1, 2], events.Select(e => e.ContestId));
        Assert.True(events[0].DistanceKm < 10);
        Assert.Null(events[2].DistanceKm);
    }

    [Fact]
    public void DistancesAreRightToAboutAKilometre()
    {
        // Oslo to Bergen is about 305 km as the crow flies
        var km = OpenRegistration.DistanceKm(Oslo, 60.39, 5.32);
        Assert.InRange(km, 300.0, 310.0);
        Assert.Equal(0.0, OpenRegistration.DistanceKm(Oslo, Oslo.Latitude, Oslo.Longitude), 3);
    }

    [Fact]
    public void DistancesAreWrittenForAPersonLookingForAPlace()
    {
        Assert.Equal("850 m", OpenRegistration.DistanceText(0.85));
        Assert.Equal("3.4 km", OpenRegistration.DistanceText(3.44));
        Assert.Equal("120 km", OpenRegistration.DistanceText(119.6));
    }

    [Fact]
    public void RegisteringOpensTheWebFormForThatTask() =>
        Assert.Equal("/schedule-flight?contestId=5&navigationTaskId=9", OpenRegistration.RegisterPath(Task(5, 9)));
}
