using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Spektra.Core;

/// Persistent audit-result cache (SQLite, WAL). Disposable data: safe to
/// delete at any time, never a source of truth. Entries are valid only when
/// path, size, mtime, and AnalysisVersion all match.
public sealed class AuditCache : IDisposable
{
    /// Bump whenever verdict or integrity analysis changes so stale rows
    /// re-analyze (see the release checklist in CONTRIBUTING.md).
    /// 2: transcode-based problem semantics + the Channels row field.
    public const int AnalysisVersion = 2;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spektra", "audit-cache.db");

    private readonly SqliteConnection _db;
    private readonly object _gate = new();

    private AuditCache(SqliteConnection db) => _db = db;

    /// Opens (creating schema as needed). A corrupt database is deleted and
    /// recreated once; a second failure propagates so callers can fall back
    /// to running uncached.
    public static AuditCache Open(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        try
        {
            return OpenOnce(dbPath);
        }
        catch (SqliteException)
        {
            foreach (var f in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
                if (File.Exists(f)) File.Delete(f);
            return OpenOnce(dbPath);
        }
    }

    private static AuditCache OpenOnce(string dbPath)
    {
        // Pooling=False: pooled handles keep the file locked after Dispose,
        // which breaks corrupt-db recreation and temp cleanup.
        var db = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        try
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                PRAGMA journal_mode=WAL;
                CREATE TABLE IF NOT EXISTS files(
                    path TEXT PRIMARY KEY,
                    size INTEGER NOT NULL,
                    mtime_ticks INTEGER NOT NULL,
                    analysis_version INTEGER NOT NULL,
                    has_problem INTEGER NOT NULL,
                    row_json TEXT NOT NULL);
                """;
            cmd.ExecuteNonQuery();
            return new AuditCache(db);
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    public AuditEntry? TryGet(AuditTarget target)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT size, mtime_ticks, analysis_version, has_problem, row_json
                FROM files WHERE path = $path
                """;
            cmd.Parameters.AddWithValue("$path", target.Path);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            if (r.GetInt64(0) != target.SizeBytes
                || r.GetInt64(1) != target.MtimeTicks
                || r.GetInt32(2) != AnalysisVersion)
                return null;
            var row = JsonSerializer.Deserialize<AuditRow>(r.GetString(4));
            return row is null ? null : new AuditEntry(target, row, r.GetInt64(3) != 0, FromCache: true);
        }
    }

    public void Put(AuditTarget target, AuditRow row, bool hasProblem)
    {
        lock (_gate)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO files(path, size, mtime_ticks, analysis_version, has_problem, row_json)
                VALUES($path, $size, $mtime, $ver, $prob, $json)
                ON CONFLICT(path) DO UPDATE SET
                    size = $size, mtime_ticks = $mtime, analysis_version = $ver,
                    has_problem = $prob, row_json = $json
                """;
            cmd.Parameters.AddWithValue("$path", target.Path);
            cmd.Parameters.AddWithValue("$size", target.SizeBytes);
            cmd.Parameters.AddWithValue("$mtime", target.MtimeTicks);
            cmd.Parameters.AddWithValue("$ver", AnalysisVersion);
            cmd.Parameters.AddWithValue("$prob", hasProblem ? 1 : 0);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(row));
            cmd.ExecuteNonQuery();
        }
    }

    /// Deletes rows for files under `folder` that no longer exist there
    /// (`livePaths` is the current enumeration). Rows for other folders are
    /// untouched, so entries for unmounted drives survive. Call only after a
    /// completed (not cancelled) scan.
    public void PruneFolder(string folder, IReadOnlyCollection<string> livePaths)
    {
        var prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        var live = new HashSet<string>(livePaths, StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            var stale = new List<string>();
            using (var select = _db.CreateCommand())
            {
                select.CommandText = "SELECT path FROM files";
                using var r = select.ExecuteReader();
                while (r.Read())
                {
                    var p = r.GetString(0);
                    if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !live.Contains(p))
                        stale.Add(p);
                }
            }
            foreach (var p in stale)
            {
                using var delete = _db.CreateCommand();
                delete.CommandText = "DELETE FROM files WHERE path = $path";
                delete.Parameters.AddWithValue("$path", p);
                delete.ExecuteNonQuery();
            }
        }
    }

    public void Dispose() => _db.Dispose();
}
