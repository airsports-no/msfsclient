using Microsoft.Data.Sqlite;

namespace msfsclient.Core.Data;

/// <summary>
/// The local SQLite database: position queue, track log, photo observation queue and settings (the same tables as the
/// mobile app's sqldelight schema). One connection, used under a lock: the volumes are small (one fix a second) and the
/// callers come from the UI thread, the uploader and the SimConnect pump.
/// </summary>
public sealed class AirsportsDatabase : IDisposable
{
    public const int SchemaVersion = 4;

    private readonly SqliteConnection _connection;
    private readonly object _lock = new();

    /// <summary>Opens (and creates) the database at <paramref name="path"/>; ":memory:" for tests.</summary>
    public AirsportsDatabase(string path)
    {
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        _connection.Open();
        Execute("PRAGMA journal_mode = WAL;");
        CreateSchema();
    }

    public static AirsportsDatabase InMemory() => new(":memory:");

    private void CreateSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS QueuedPosition (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                deviceId TEXT NOT NULL,
                timestampMillis INTEGER NOT NULL,
                latitude REAL NOT NULL,
                longitude REAL NOT NULL,
                speedKnots REAL NOT NULL,
                bearing REAL,
                altitude REAL,
                accuracy REAL,
                batteryPercent REAL
            );
            CREATE TABLE IF NOT EXISTS Setting (
                key TEXT NOT NULL PRIMARY KEY,
                value TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS TrackSession (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                deviceId TEXT NOT NULL,
                startedAtMillis INTEGER NOT NULL,
                endedAtMillis INTEGER
            );
            CREATE TABLE IF NOT EXISTS TrackLogPoint (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                sessionId INTEGER NOT NULL,
                timestampMillis INTEGER NOT NULL,
                latitude REAL NOT NULL,
                longitude REAL NOT NULL,
                altitude REAL,
                speedMetersPerSecond REAL NOT NULL,
                bearing REAL,
                accuracy REAL
            );
            CREATE INDEX IF NOT EXISTS TrackLogPoint_session ON TrackLogPoint(sessionId, timestampMillis);
            CREATE TABLE IF NOT EXISTS PendingObservation (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                contestantId INTEGER NOT NULL,
                photoId INTEGER NOT NULL,
                timeIso TEXT NOT NULL,
                latitude REAL NOT NULL,
                longitude REAL NOT NULL
            );
            """);
        Execute($"PRAGMA user_version = {SchemaVersion};");
    }

    /// <summary>Runs <paramref name="work"/> with exclusive use of the connection.</summary>
    public T Use<T>(Func<SqliteConnection, T> work)
    {
        lock (_lock) return work(_connection);
    }

    public void Use(Action<SqliteConnection> work)
    {
        lock (_lock) work(_connection);
    }

    /// <summary>Runs <paramref name="work"/> in one transaction.</summary>
    public T Transaction<T>(Func<SqliteConnection, SqliteTransaction, T> work)
    {
        lock (_lock)
        {
            using var tx = _connection.BeginTransaction();
            var result = work(_connection, tx);
            tx.Commit();
            return result;
        }
    }

    public int Execute(string sql, params (string Name, object? Value)[] parameters) => Use(c =>
    {
        using var command = Command(c, sql, parameters);
        return command.ExecuteNonQuery();
    });

    public object? Scalar(string sql, params (string Name, object? Value)[] parameters) => Use(c =>
    {
        using var command = Command(c, sql, parameters);
        return command.ExecuteScalar();
    });

    internal static SqliteCommand Command(SqliteConnection c, string sql, (string Name, object? Value)[] parameters, SqliteTransaction? tx = null)
    {
        var command = c.CreateCommand();
        command.CommandText = sql;
        command.Transaction = tx;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    public void Dispose() => _connection.Dispose();
}

internal static class ReaderExtensions
{
    public static double? NullableDouble(this SqliteDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetDouble(i);
    }

    public static long? NullableLong(this SqliteDataReader r, string column)
    {
        var i = r.GetOrdinal(column);
        return r.IsDBNull(i) ? null : r.GetInt64(i);
    }
}
