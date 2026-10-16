using msfsclient.Core.Api;
using msfsclient.Core.Warnings;

namespace msfsclient.Core.Tests;

public class IgnoredPositionsTests
{
    private static IReadOnlyList<CurrentTask> Tasks(params string[] flights) =>
        [AirsportsJson.Deserialize<CurrentTask>($"{{\"navigation_task\":{{\"pk\":1}},\"active_contestants\":[{string.Join(",", flights)}]}}")!];

    [Fact]
    public void NothingIsSaidWhenPositionsAreUsedOrTheBackendIsOld()
    {
        Assert.Empty(IgnoredPositions.Notices(Tasks("{\"id\":1,\"positions_accepted\":true}", "{\"id\":2}")));
    }

    [Fact]
    public void TheServersReasonIsShownOncePerReason()
    {
        const string reason = "This flight was booked as a real aircraft flight, so your simulator positions are ignored.";
        var flight = $"{{\"id\":1,\"positions_accepted\":false,\"positions_ignored_reason\":\"{reason}\"}}";
        Assert.Equal([reason], IgnoredPositions.Notices(Tasks(flight, flight.Replace("\"id\":1", "\"id\":2"))));
    }

    [Fact]
    public void AMissingReasonGetsAGenericLine()
    {
        Assert.Equal(["The positions from the simulator are not used for your flight."], IgnoredPositions.Notices(Tasks("{\"id\":1,\"positions_accepted\":false}")));
    }
}
