using msfsclient.Core.Data;

namespace msfsclient.Core.Tracking;

public sealed record QueuedPosition(
    long Id, string DeviceId, long TimestampMillis, double Latitude, double Longitude, double SpeedKnots,
    double? Bearing, double? Altitude, double? Accuracy, double? BatteryPercent);

/// <summary>Durable offline buffer: every fix is stored first, deleted only once Traccar acknowledged it.</summary>
public sealed class PositionQueue(AirsportsDatabase db, long maxSize = 200_000)
{
    public void Enqueue(string deviceId, TrackPoint p)
    {
        db.Execute(
            "INSERT INTO QueuedPosition(deviceId, timestampMillis, latitude, longitude, speedKnots, bearing, altitude, accuracy, batteryPercent) " +
            "VALUES ($d, $t, $lat, $lon, $s, $b, $a, $acc, $batt)",
            ("$d", deviceId), ("$t", p.TimestampMillis), ("$lat", p.Latitude), ("$lon", p.Longitude),
            ("$s", p.SpeedMetersPerSecond * TrackPoint.MetersPerSecondToKnots), ("$b", p.Bearing), ("$a", p.Altitude),
            ("$acc", p.Accuracy), ("$batt", p.BatteryPercent));
        if (Size() > maxSize)
        {
            db.Execute("DELETE FROM QueuedPosition WHERE id NOT IN (SELECT id FROM QueuedPosition ORDER BY timestampMillis DESC, id DESC LIMIT $n)", ("$n", maxSize));
        }
    }

    public IReadOnlyList<QueuedPosition> Oldest(long limit) => db.Use(c =>
    {
        using var cmd = AirsportsDatabase.Command(c, "SELECT * FROM QueuedPosition ORDER BY timestampMillis ASC, id ASC LIMIT $n", [("$n", limit)]);
        using var r = cmd.ExecuteReader();
        var rows = new List<QueuedPosition>();
        while (r.Read())
        {
            rows.Add(new QueuedPosition(
                r.GetInt64(r.GetOrdinal("id")), r.GetString(r.GetOrdinal("deviceId")), r.GetInt64(r.GetOrdinal("timestampMillis")),
                r.GetDouble(r.GetOrdinal("latitude")), r.GetDouble(r.GetOrdinal("longitude")), r.GetDouble(r.GetOrdinal("speedKnots")),
                r.NullableDouble("bearing"), r.NullableDouble("altitude"), r.NullableDouble("accuracy"), r.NullableDouble("batteryPercent")));
        }
        return rows;
    });

    public void Remove(long id) => db.Execute("DELETE FROM QueuedPosition WHERE id = $id", ("$id", id));

    public long Size() => (long)(db.Scalar("SELECT COUNT(*) FROM QueuedPosition") ?? 0L);
}
