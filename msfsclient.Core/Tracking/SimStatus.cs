namespace msfsclient.Core.Tracking;

/// <summary>
/// What the simulator connection is doing, as opposed to what the server has heard (see <see cref="ReceivingStatus"/>).
/// Replaces the mobile app's GPS status: there is no satellite fix or accuracy, only "is MSFS sending positions".
/// </summary>
public abstract record SimStatus
{
    /// <summary>MSFS is not running, or SimConnect has not connected yet.</summary>
    public sealed record NotConnected : SimStatus;
    /// <summary>Connected, but no position has arrived (still loading, in the menus).</summary>
    public sealed record WaitingForPosition : SimStatus;
    public sealed record Receiving(long SecondsAgo) : SimStatus;
    /// <summary>Positions have stopped arriving (paused, back in the menus, SimConnect hung).</summary>
    public sealed record Lost(long SecondsAgo) : SimStatus;

    /// <summary>Positions arrive once a second; a few missed ones are normal, this many are not.</summary>
    public const long LostAfterSeconds = 5;

    public static SimStatus Of(bool connected, long? lastPositionMillis, long nowMillis)
    {
        if (!connected) return new NotConnected();
        if (lastPositionMillis == null) return new WaitingForPosition();
        var age = Math.Max(0, (nowMillis - lastPositionMillis.Value) / 1000);
        return age > LostAfterSeconds ? new Lost(age) : new Receiving(age);
    }

    /// <summary>One line for the dock and the pre-flight list.</summary>
    public string Text => this switch
    {
        NotConnected => "Simulator not connected. Start MSFS and load a flight.",
        WaitingForPosition => "Simulator connected. Waiting for a position…",
        Receiving => "Simulator position OK",
        Lost l => $"No position from the simulator for {l.SecondsAgo} s",
        _ => "",
    };

    public bool IsProblem => this is NotConnected or Lost;
}
