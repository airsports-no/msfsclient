using msfsclient.Core.Tracking;

namespace msfsclient.Core.Preflight;

public enum CheckStatus { Ok, Warning, Problem, Unknown }

/// <summary>What a pre-flight row can offer to fix its problem.</summary>
public enum CheckAction
{
    /// <summary>Opens the Windows date and time settings (the PC clock is off).</summary>
    OpenClockSettings,
    /// <summary>Tries to connect to the simulator again.</summary>
    ConnectSimulator,
}

public sealed record PreflightCheck(string Title, CheckStatus Status, string Detail, CheckAction? Action = null);

/// <summary>
/// The "ready to fly" list for the simulator (port of preflight/Preflight.kt). Location, battery and subscription rows are
/// left out: the simulator needs none of them. The PC clock matters more than on a phone, because positions are stamped
/// with it.
/// </summary>
public static class PreflightChecks
{
    /// <param name="online">Whether the PC has a connection; null when it cannot be told (the row is left out).</param>
    public static IReadOnlyList<PreflightCheck> Of(bool hasFlight, bool? online, ClockStatus clock, SimStatus sim)
    {
        var list = new List<PreflightCheck>
        {
            hasFlight
                ? new("Flight", CheckStatus.Ok, "A flight is scheduled for you.")
                : new("Flight", CheckStatus.Warning, "No flight is scheduled for you, so your positions would not be scored."),
        };
        if (online != null)
        {
            list.Add(online.Value
                ? new("Internet", CheckStatus.Ok, "Connected.")
                : new("Internet", CheckStatus.Problem, "No connection. Positions are kept and sent when it returns, but you will not see the server status."));
        }
        list.Add(clock switch
        {
            ClockStatus.InSync => new("Clock", CheckStatus.Ok, "The PC clock matches network time."),
            ClockStatus.Drift d => new("Clock", CheckStatus.Problem,
                $"The PC clock is {d.Seconds} s {(d.ClockIsAhead ? "ahead" : "behind")}. Your positions are stamped with it, so turn on Set time automatically and sync now.",
                CheckAction.OpenClockSettings),
            _ => new("Clock", CheckStatus.Unknown, "Not checked yet."),
        });
        list.Add(sim switch
        {
            SimStatus.Receiving => new("Simulator", CheckStatus.Ok, "Connected and sending your position."),
            SimStatus.WaitingForPosition => new("Simulator", CheckStatus.Warning, "Connected, but no position yet. Load a flight in MSFS."),
            SimStatus.Lost l => new("Simulator", CheckStatus.Warning, $"No position for {l.SecondsAgo} s. Is the simulator paused or in the menus?"),
            _ => new("Simulator", CheckStatus.Problem, "Not connected. Start MSFS and load a flight.", CheckAction.ConnectSimulator),
        });
        return list;
    }

    /// <summary>One line for the Home tile.</summary>
    public static string Summary(IReadOnlyCollection<PreflightCheck> checks)
    {
        var problems = checks.Count(c => c.Status == CheckStatus.Problem);
        var warnings = checks.Count(c => c.Status == CheckStatus.Warning);
        return problems > 0 ? $"{problems} to fix" : warnings > 0 ? $"{warnings} to look at" : "All clear";
    }
}
