using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BanditDuels.Stats;

/// <summary>
/// SQLite-backed stats store. Append-only per match (single INSERT, microseconds),
/// indexed for fast leaderboard and per-player aggregations. Replaces the v1 JSON
/// store which had to rewrite the entire file on every match end.
///
/// On first load, if a legacy <c>stats.json</c> exists, its contents are bulk-imported
/// into the DB and the JSON file is renamed to <c>stats.json.migrated</c>.
///
/// All methods assume main-thread access (driven by the FourKit scheduler and
/// event listeners) and aren't thread-safe.
/// </summary>
public sealed class StatsRepo : IDisposable
{
    public const string DataFolder     = "plugins/BanditDuels-data";
    public const string DbFile         = "duels.db";
    public const string LegacyJsonFile = "stats.json";

    private SqliteConnection? _conn;

    public bool Loaded { get; private set; }

    public void load()
    {
        try
        {
            Directory.CreateDirectory(DataFolder);
            var dbPath = Path.Combine(DataFolder, DbFile);
            var alreadyExisted = File.Exists(dbPath);

            _conn = new SqliteConnection("Data Source=" + dbPath);
            _conn.Open();

            applyPragmas();
            createSchema();

            if (!alreadyExisted)
                migrateLegacyJsonIfPresent();

            Loaded = true;
            Console.WriteLine("[BanditDuels] stats DB ready at " + dbPath + " (" + totalMatches() + " matches)");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BanditDuels] failed to open stats DB: " + ex.Message);
            Console.WriteLine("[BanditDuels] stats commands will be unavailable; matches will not be recorded");
            _conn?.Dispose();
            _conn = null;
        }
    }

    private void applyPragmas()
    {
        // WAL gives durable writes without blocking readers; NORMAL is the right
        // sync level for our usage (we don't need full fsync per insert).
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
        cmd.ExecuteNonQuery();
    }

    private void createSchema()
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS matches (
    id               TEXT PRIMARY KEY,
    kit_id           TEXT NOT NULL,
    arena_name       TEXT NOT NULL,
    player_a         TEXT NOT NULL,
    player_a_name    TEXT NOT NULL,
    player_b         TEXT NOT NULL,
    player_b_name    TEXT NOT NULL,
    winner_uuid      TEXT NOT NULL DEFAULT '',
    end_reason       TEXT NOT NULL,
    started_at       TEXT NOT NULL,
    ended_at         TEXT NOT NULL,
    duration_seconds INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_matches_kit       ON matches(kit_id);
CREATE INDEX IF NOT EXISTS idx_matches_player_a  ON matches(player_a);
CREATE INDEX IF NOT EXISTS idx_matches_player_b  ON matches(player_b);
CREATE INDEX IF NOT EXISTS idx_matches_winner    ON matches(winner_uuid);

CREATE TABLE IF NOT EXISTS player_names (
    uuid TEXT PRIMARY KEY,
    name TEXT NOT NULL
);";
        cmd.ExecuteNonQuery();
    }

    // ---- recording ----

    public void recordMatch(MatchRecord m)
    {
        if (_conn == null) return;
        try
        {
            using var tx = _conn.BeginTransaction();

            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT OR REPLACE INTO matches
(id, kit_id, arena_name, player_a, player_a_name, player_b, player_b_name,
 winner_uuid, end_reason, started_at, ended_at, duration_seconds)
VALUES
(@id, @kit, @arena, @pa, @pan, @pb, @pbn, @win, @reason, @started, @ended, @duration)";
                cmd.Parameters.AddWithValue("@id",       m.Id);
                cmd.Parameters.AddWithValue("@kit",      m.KitId);
                cmd.Parameters.AddWithValue("@arena",    m.ArenaName);
                cmd.Parameters.AddWithValue("@pa",       m.PlayerA);
                cmd.Parameters.AddWithValue("@pan",      m.PlayerAName);
                cmd.Parameters.AddWithValue("@pb",       m.PlayerB);
                cmd.Parameters.AddWithValue("@pbn",      m.PlayerBName);
                cmd.Parameters.AddWithValue("@win",      m.WinnerUuid);
                cmd.Parameters.AddWithValue("@reason",   m.EndReason);
                cmd.Parameters.AddWithValue("@started",  m.StartedAt);
                cmd.Parameters.AddWithValue("@ended",    m.EndedAt);
                cmd.Parameters.AddWithValue("@duration", m.DurationSeconds);
                cmd.ExecuteNonQuery();
            }

            upsertPlayerName(tx, m.PlayerA, m.PlayerAName);
            upsertPlayerName(tx, m.PlayerB, m.PlayerBName);

            tx.Commit();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BanditDuels] recordMatch failed: " + ex.Message);
        }
    }

    private void upsertPlayerName(SqliteTransaction tx, string uuid, string name)
    {
        if (string.IsNullOrEmpty(uuid)) return;
        using var cmd = _conn!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO player_names (uuid, name) VALUES (@uuid, @name)
ON CONFLICT(uuid) DO UPDATE SET name = excluded.name";
        cmd.Parameters.AddWithValue("@uuid", uuid);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.ExecuteNonQuery();
    }

    // ---- queries ----

    public int totalMatches()
    {
        if (_conn == null) return 0;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM matches";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    public string? findUuidByName(string name)
    {
        if (_conn == null) return null;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT uuid FROM player_names WHERE name = @name COLLATE NOCASE LIMIT 1";
        cmd.Parameters.AddWithValue("@name", name);
        return cmd.ExecuteScalar() as string;
    }

    public PlayerStats getStats(string uuid, string displayName)
    {
        var stats = new PlayerStats { Uuid = uuid, Name = displayName };
        if (_conn == null) return stats;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT kit_id, winner_uuid, end_reason
FROM matches
WHERE player_a = @uuid OR player_b = @uuid";
        cmd.Parameters.AddWithValue("@uuid", uuid);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var kit    = reader.GetString(0);
            var winner = reader.GetString(1);
            var reason = reader.GetString(2);

            if (reason == MatchEndReason.Draw) stats.recordDraw(kit);
            else if (winner == uuid)           stats.recordWin(kit);
            else                                stats.recordLoss(kit);
        }
        return stats;
    }

    public List<LeaderboardEntry> top(int n, string? kitFilter)
    {
        var result = new List<LeaderboardEntry>();
        if (_conn == null) return result;

        // Use a UNION ALL to flatten player A and player B into one column,
        // then aggregate. The COALESCE pulls the latest known name from
        // player_names (falling back to '?' for the unlikely case it's missing).
        var kitClause = kitFilter != null ? " WHERE kit_id = @kit COLLATE NOCASE" : "";

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
WITH appearances AS (
    SELECT player_a AS uuid,
           CASE WHEN winner_uuid = player_a THEN 1 ELSE 0 END AS won
    FROM matches" + kitClause + @"
    UNION ALL
    SELECT player_b AS uuid,
           CASE WHEN winner_uuid = player_b THEN 1 ELSE 0 END AS won
    FROM matches" + kitClause + @"
)
SELECT a.uuid,
       COALESCE(pn.name, '?') AS name,
       SUM(a.won) AS wins,
       COUNT(*) AS total
FROM appearances a
LEFT JOIN player_names pn ON pn.uuid = a.uuid
GROUP BY a.uuid
ORDER BY wins DESC, total DESC
LIMIT @n";
        cmd.Parameters.AddWithValue("@n", n);
        if (kitFilter != null) cmd.Parameters.AddWithValue("@kit", kitFilter);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new LeaderboardEntry
            {
                Uuid         = reader.GetString(0),
                Name         = reader.GetString(1),
                Wins         = Convert.ToInt32(reader.GetValue(2)),
                TotalMatches = Convert.ToInt32(reader.GetValue(3)),
            });
        }
        return result;
    }

    // ---- one-shot legacy JSON import ----

    private sealed class LegacyStatsDatabase
    {
        public List<MatchRecord> Matches { get; set; } = new();
        public Dictionary<string, string> PlayerNames { get; set; } = new();
    }

    private void migrateLegacyJsonIfPresent()
    {
        var jsonPath = Path.Combine(DataFolder, LegacyJsonFile);
        if (!File.Exists(jsonPath)) return;

        try
        {
            LegacyStatsDatabase? legacy;
            using (var stream = File.OpenRead(jsonPath))
                legacy = JsonSerializer.Deserialize<LegacyStatsDatabase>(stream);

            if (legacy == null) return;

            int matchCount = 0;
            using (var tx = _conn!.BeginTransaction())
            {
                foreach (var m in legacy.Matches)
                {
                    using var cmd = _conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
INSERT OR IGNORE INTO matches
(id, kit_id, arena_name, player_a, player_a_name, player_b, player_b_name,
 winner_uuid, end_reason, started_at, ended_at, duration_seconds)
VALUES
(@id, @kit, @arena, @pa, @pan, @pb, @pbn, @win, @reason, @started, @ended, @duration)";
                    cmd.Parameters.AddWithValue("@id",       m.Id);
                    cmd.Parameters.AddWithValue("@kit",      m.KitId);
                    cmd.Parameters.AddWithValue("@arena",    m.ArenaName);
                    cmd.Parameters.AddWithValue("@pa",       m.PlayerA);
                    cmd.Parameters.AddWithValue("@pan",      m.PlayerAName);
                    cmd.Parameters.AddWithValue("@pb",       m.PlayerB);
                    cmd.Parameters.AddWithValue("@pbn",      m.PlayerBName);
                    cmd.Parameters.AddWithValue("@win",      m.WinnerUuid);
                    cmd.Parameters.AddWithValue("@reason",   m.EndReason);
                    cmd.Parameters.AddWithValue("@started",  m.StartedAt);
                    cmd.Parameters.AddWithValue("@ended",    m.EndedAt);
                    cmd.Parameters.AddWithValue("@duration", m.DurationSeconds);
                    matchCount += cmd.ExecuteNonQuery();
                }

                foreach (var kv in legacy.PlayerNames)
                    upsertPlayerName(tx, kv.Key, kv.Value);

                tx.Commit();
            }

            var renamed = jsonPath + ".migrated";
            if (File.Exists(renamed)) File.Delete(renamed);
            File.Move(jsonPath, renamed);

            Console.WriteLine("[BanditDuels] migrated " + matchCount + " matches from " + LegacyJsonFile + " into the SQLite DB");
            Console.WriteLine("[BanditDuels] legacy file renamed to " + Path.GetFileName(renamed));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BanditDuels] legacy JSON migration failed: " + ex.Message + " (continuing with the data already in the DB)");
        }
    }

    public void Dispose()
    {
        _conn?.Dispose();
        _conn = null;
    }
}
