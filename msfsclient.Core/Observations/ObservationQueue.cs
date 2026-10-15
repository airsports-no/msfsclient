using msfsclient.Core.Data;

namespace msfsclient.Core.Observations;

public sealed record PendingObservation(long Id, long ContestantId, long PhotoId, string TimeIso, double Latitude, double Longitude);

/// <summary>Photos the crew marked that the server has not acknowledged yet. Survives restarts and long offline periods.</summary>
public sealed class ObservationQueue(AirsportsDatabase db)
{
    /// <summary>A newer mark of the same photo replaces an older one that is still waiting.</summary>
    public void Add(int contestantId, int photoId, string timeIso, double latitude, double longitude) => db.Transaction((c, tx) =>
    {
        AirsportsDatabase.Command(c, "DELETE FROM PendingObservation WHERE contestantId = $c AND photoId = $p", [("$c", contestantId), ("$p", photoId)], tx).ExecuteNonQuery();
        return AirsportsDatabase.Command(c, "INSERT INTO PendingObservation(contestantId, photoId, timeIso, latitude, longitude) VALUES ($c, $p, $t, $lat, $lon)",
            [("$c", contestantId), ("$p", photoId), ("$t", timeIso), ("$lat", latitude), ("$lon", longitude)], tx).ExecuteNonQuery();
    });

    public IReadOnlyList<PendingObservation> All() => db.Use(c =>
    {
        using var cmd = AirsportsDatabase.Command(c, "SELECT * FROM PendingObservation ORDER BY id ASC", []);
        using var r = cmd.ExecuteReader();
        var rows = new List<PendingObservation>();
        while (r.Read())
        {
            rows.Add(new PendingObservation(r.GetInt64(r.GetOrdinal("id")), r.GetInt64(r.GetOrdinal("contestantId")), r.GetInt64(r.GetOrdinal("photoId")),
                r.GetString(r.GetOrdinal("timeIso")), r.GetDouble(r.GetOrdinal("latitude")), r.GetDouble(r.GetOrdinal("longitude"))));
        }
        return rows;
    });

    public void Remove(long id) => db.Execute("DELETE FROM PendingObservation WHERE id = $id", ("$id", id));

    /// <summary>Forgets a mark that has not been sent yet.</summary>
    public void RemoveForPhoto(int contestantId, int photoId) =>
        db.Execute("DELETE FROM PendingObservation WHERE contestantId = $c AND photoId = $p", ("$c", contestantId), ("$p", photoId));
}
