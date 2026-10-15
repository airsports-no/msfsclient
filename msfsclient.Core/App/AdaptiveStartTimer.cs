namespace msfsclient.Core.App;

/// <summary>
/// The adaptive-start timer's arithmetic, kept identical to the backend (<c>round_time_minute</c> in
/// display/calculators/calculator_utilities.py): the instant is floored to whole seconds and rounded to the NEAREST WHOLE
/// MINUTE, with exactly 30 seconds rounding up.
/// </summary>
public static class AdaptiveStartTimer
{
    private const long MsPerMinute = 60_000L;

    /// <summary>
    /// The whole minute the timer counts from when Start is pressed at <paramref name="pressedAtMillis"/>. With a buffer the
    /// start minute lies that many whole minutes later, so the timer starts at -N:00 (give or take the rounding).
    /// </summary>
    public static long OriginFor(long pressedAtMillis, int bufferMinutes = 0)
    {
        var seconds = FloorDiv(pressedAtMillis, 1000L);
        return FloorDiv(seconds + 30, 60L) * MsPerMinute + Math.Max(0, bufferMinutes) * MsPerMinute;
    }

    /// <summary>Positive once the start minute has begun, negative while counting down to it.</summary>
    public static long ElapsedMillis(long originMillis, long nowMillis) => nowMillis - originMillis;

    /// <summary>
    /// "2:05" after the start minute, "-0:20" before it. The countdown rounds up (like a stopwatch) so that it shows "0:00"
    /// exactly when the start minute begins. Beyond an hour it shows "1:02:05".
    /// </summary>
    public static string Format(long elapsedMillis)
    {
        var totalSeconds = elapsedMillis >= 0 ? elapsedMillis / 1000 : -((-elapsedMillis + 999) / 1000);
        var sign = totalSeconds < 0 ? "-" : "";
        var abs = Math.Abs(totalSeconds);
        var hours = abs / 3600;
        var minutes = abs % 3600 / 60;
        var seconds = abs % 60;
        return hours > 0 ? $"{sign}{hours}:{minutes:00}:{seconds:00}" : $"{sign}{minutes}:{seconds:00}";
    }

    private static long FloorDiv(long a, long b) => a / b - (a % b != 0 && (a < 0) != (b < 0) ? 1 : 0);
}
