namespace msfsclient.Core.Tracking;

/// <summary>
/// One position from the simulator. <see cref="TimestampMillis"/> is UTC epoch milliseconds from the PC clock (the
/// simulator has no GPS time of its own), so the PC clock check matters. Speed is metres per second.
/// </summary>
public sealed record TrackPoint(
    long TimestampMillis,
    double Latitude,
    double Longitude,
    double SpeedMetersPerSecond,
    double? Bearing,
    double? Altitude,
    double? Accuracy = null,
    double? BatteryPercent = null,
    /// <summary>Reference time minus the PC clock at the moment of the fix; null when it cannot be told.</summary>
    long? ClockOffsetMillis = null)
{
    public const double MetersPerSecondToKnots = 1.943844;
    public const double KnotsToMetersPerSecond = 0.514444;
    public const double FeetToMeters = 0.3048;
}

/// <summary>Where positions come from: the simulator (SimConnect) in the app, a fake in tests.</summary>
public interface IPositionSource
{
    event Action<TrackPoint>? PositionReceived;
}
