using msfsclient.Core.Data;
using msfsclient.Core.Tracking;

namespace msfsclient.Core.History;

/// <summary>One recorded tracking session. <see cref="EndedAtMillis"/> is null while it is being recorded.</summary>
public sealed record TrackSessionSummary(long Id, long StartedAtMillis, long? EndedAtMillis, long PointCount, long? FirstPointMillis, long? LastPointMillis)
{
    public bool Recording => EndedAtMillis == null;

    /// <summary>Duration of the recorded track (first to last fix), or of the session when it has no fixes.</summary>
    public long DurationMillis => FirstPointMillis != null && LastPointMillis != null
        ? LastPointMillis.Value - FirstPointMillis.Value
        : (EndedAtMillis ?? StartedAtMillis) - StartedAtMillis;
}

/// <summary>One recorded fix. <see cref="TimestampMillis"/> is the fix's own time (UTC epoch).</summary>
public sealed record LoggedPoint(long TimestampMillis, double Latitude, double Longitude, double? Altitude, double SpeedMetersPerSecond, double? Bearing, double? Accuracy);

/// <summary>The record of every tracking session on this PC: written while tracking, read by the history view and the GPX export.</summary>
public sealed class TrackLog(AirsportsDatabase db)
{
    private const string CloseOpenSessions =
        "UPDATE TrackSession SET endedAtMillis = COALESCE((SELECT MAX(timestampMillis) FROM TrackLogPoint WHERE sessionId = TrackSession.id), startedAtMillis) " +
        "WHERE endedAtMillis IS NULL";

    /// <summary>A session left open by a crash ends at its last fix. Called at start-up; also done before every new session.</summary>
    public void CloseAbandonedSessions() => db.Execute(CloseOpenSessions);

    /// <summary>Starts a session; any session a crash left open is closed first.</summary>
    public long StartSession(string deviceId, long startedAtMillis) => db.Transaction((c, tx) =>
    {
        AirsportsDatabase.Command(c, CloseOpenSessions, [], tx).ExecuteNonQuery();
        AirsportsDatabase.Command(c, "INSERT INTO TrackSession(deviceId, startedAtMillis) VALUES ($d, $t)", [("$d", deviceId), ("$t", startedAtMillis)], tx).ExecuteNonQuery();
        return (long)AirsportsDatabase.Command(c, "SELECT last_insert_rowid()", [], tx).ExecuteScalar()!;
    });

    public void Append(long sessionId, TrackPoint p) => db.Execute(
        "INSERT INTO TrackLogPoint(sessionId, timestampMillis, latitude, longitude, altitude, speedMetersPerSecond, bearing, accuracy) " +
        "VALUES ($s, $t, $lat, $lon, $alt, $v, $b, $acc)",
        ("$s", sessionId), ("$t", p.TimestampMillis), ("$lat", p.Latitude), ("$lon", p.Longitude), ("$alt", p.Altitude),
        ("$v", p.SpeedMetersPerSecond), ("$b", p.Bearing), ("$acc", p.Accuracy));

    public void EndSession(long sessionId, long endedAtMillis) =>
        db.Execute("UPDATE TrackSession SET endedAtMillis = $e WHERE id = $id", ("$e", endedAtMillis), ("$id", sessionId));

    /// <summary>Newest first.</summary>
    public IReadOnlyList<TrackSessionSummary> Sessions() => db.Use(c =>
    {
        using var cmd = AirsportsDatabase.Command(c, """
            SELECT s.id, s.startedAtMillis, s.endedAtMillis,
                (SELECT COUNT(*) FROM TrackLogPoint p WHERE p.sessionId = s.id) AS pointCount,
                (SELECT MIN(timestampMillis) FROM TrackLogPoint p WHERE p.sessionId = s.id) AS firstPoint,
                (SELECT MAX(timestampMillis) FROM TrackLogPoint p WHERE p.sessionId = s.id) AS lastPoint
            FROM TrackSession s ORDER BY s.startedAtMillis DESC, s.id DESC
            """, []);
        using var r = cmd.ExecuteReader();
        var rows = new List<TrackSessionSummary>();
        while (r.Read())
        {
            rows.Add(new TrackSessionSummary(r.GetInt64(0), r.GetInt64(1), r.NullableLong("endedAtMillis"), r.GetInt64(3), r.NullableLong("firstPoint"), r.NullableLong("lastPoint")));
        }
        return rows;
    });

    public IReadOnlyList<LoggedPoint> Points(long sessionId) => db.Use(c =>
    {
        using var cmd = AirsportsDatabase.Command(c, "SELECT * FROM TrackLogPoint WHERE sessionId = $s ORDER BY timestampMillis ASC, id ASC", [("$s", sessionId)]);
        using var r = cmd.ExecuteReader();
        var rows = new List<LoggedPoint>();
        while (r.Read())
        {
            rows.Add(new LoggedPoint(
                r.GetInt64(r.GetOrdinal("timestampMillis")), r.GetDouble(r.GetOrdinal("latitude")), r.GetDouble(r.GetOrdinal("longitude")),
                r.NullableDouble("altitude"), r.GetDouble(r.GetOrdinal("speedMetersPerSecond")), r.NullableDouble("bearing"), r.NullableDouble("accuracy")));
        }
        return rows;
    });

    public void Delete(long sessionId) => db.Transaction((c, tx) =>
    {
        AirsportsDatabase.Command(c, "DELETE FROM TrackLogPoint WHERE sessionId = $s", [("$s", sessionId)], tx).ExecuteNonQuery();
        return AirsportsDatabase.Command(c, "DELETE FROM TrackSession WHERE id = $s", [("$s", sessionId)], tx).ExecuteNonQuery();
    });

    /// <summary>Deletes every finished session; the one being recorded stays.</summary>
    public void DeleteAllFinished() => db.Transaction((c, tx) =>
    {
        AirsportsDatabase.Command(c, "DELETE FROM TrackLogPoint WHERE sessionId IN (SELECT id FROM TrackSession WHERE endedAtMillis IS NOT NULL)", [], tx).ExecuteNonQuery();
        return AirsportsDatabase.Command(c, "DELETE FROM TrackSession WHERE endedAtMillis IS NOT NULL", [], tx).ExecuteNonQuery();
    });
}
