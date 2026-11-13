using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MinePix;

public enum ClipboardKind { Text, Files, Image }

/// <summary>One captured clipboard entry.</summary>
public sealed record ClipboardItem(
    long Id, string Kind, string Content, string? ImagePath,
    bool Pinned, long CreatedAt, long LastUsedAt, int UseCount)
{
    public string Preview => Kind switch
    {
        "text" => Content.Split('\n')[0].Trim(),
        "files" => FormatFiles(Content),
        _ => System.IO.Path.GetFileName(Content),
    };

    public string Meta => Kind switch
    {
        "text" => $"{Content.Length} chars",
        "files" => CountFiles(Content) switch { 1 => "1 file", var n => $"{n} files" },
        _ => NiceSize(ImagePath),
    };

    private static string FormatFiles(string json)
    {
        try
        {
            var files = JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>();
            if (files.Length == 0) return "(empty)";
            var first = System.IO.Path.GetFileName(files[0]);
            return files.Length == 1 ? first : $"{first} (+{files.Length - 1} more)";
        }
        catch { return "(files)"; }
    }

    private static int CountFiles(string json)
    {
        try { return (JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>()).Length; }
        catch { return 0; }
    }

    private static string NiceSize(string? path)
    {
        try
        {
            var len = new FileInfo(path ?? "").Length;
            return len < 1024 * 1024 ? $"{len / 1024} KB" : $"{len / (1024 * 1024)} MB";
        }
        catch { return "image"; }
    }
}

/// <summary>
/// Local-only SQLite store for clipboard history. Everything lives in
/// %APPDATA%\MinePix\clipboard.db — nothing ever leaves the PC.
/// </summary>
public static class ClipboardStore
{
    private static readonly object Gate = new();

    /// <summary>Cap on stored items; the island sets this from settings at startup.</summary>
    public static int MaxItems { get; set; } = 100;

    public static string DbPath => Path.Combine(Settings.SettingsDir, "clipboard.db");
    public static string CacheDir => Path.Combine(Settings.SettingsDir, "clipboard-cache");

    private static SqliteConnection Open()
    {
        Directory.CreateDirectory(Settings.SettingsDir);
        var conn = new SqliteConnection($"Data Source={DbPath};Cache=Shared");
        conn.Open();
        return conn;
    }

    public static void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS items (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                kind        TEXT NOT NULL,
                content     TEXT NOT NULL,
                image_path  TEXT,
                hash        TEXT NOT NULL UNIQUE,
                pinned      INTEGER NOT NULL DEFAULT 0,
                created_at  INTEGER NOT NULL,
                last_used_at INTEGER NOT NULL,
                use_count   INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS idx_items_last_used ON items(last_used_at DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    public static string HashText(string text) => Hash(Encoding.UTF8.GetBytes(text));
    public static string HashFiles(string[] paths) => Hash(Encoding.UTF8.GetBytes(string.Join("\0", paths)));
    public static string HashBytes(byte[] bytes) => Hash(bytes);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Insert a new item or "touch" an existing one with the same hash. Returns null on failure.</summary>
    public static ClipboardItem? InsertOrTouch(ClipboardKind kind, string content, string? imagePath, string hash)
    {
        lock (Gate)
        {
            try
            {
                EnsureSchema();
                using var conn = Open();

                var kindStr = kind.ToString().ToLowerInvariant();
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

                // existing item with same hash -> touch (move to top) instead of duplicating
                using (var find = conn.CreateCommand())
                {
                    find.CommandText = "SELECT id, pinned, use_count FROM items WHERE hash = $h LIMIT 1";
                    find.Parameters.AddWithValue("$h", hash);
                    using var reader = find.ExecuteReader();
                    if (reader.Read())
                    {
                        long id = reader.GetInt64(0);
                        int uses = reader.GetInt32(2) + 1;
                        reader.Close();
                        using var upd = conn.CreateCommand();
                        upd.CommandText = "UPDATE items SET last_used_at = $t, use_count = $u, image_path = COALESCE($p, image_path) WHERE id = $id";
                        upd.Parameters.AddWithValue("$t", now);
                        upd.Parameters.AddWithValue("$u", uses);
                        upd.Parameters.AddWithValue("$p", (object?)imagePath ?? DBNull.Value);
                        upd.Parameters.AddWithValue("$id", id);
                        upd.ExecuteNonQuery();
                        return GetById(conn, id);
                    }
                }

                using var insert = conn.CreateCommand();
                insert.CommandText = """
                    INSERT INTO items(kind, content, image_path, hash, pinned, created_at, last_used_at, use_count)
                    VALUES ($k, $c, $p, $h, 0, $t, $t, 1);
                    SELECT last_insert_rowid();
                    """;
                insert.Parameters.AddWithValue("$k", kindStr);
                insert.Parameters.AddWithValue("$c", content);
                insert.Parameters.AddWithValue("$p", (object?)imagePath ?? DBNull.Value);
                insert.Parameters.AddWithValue("$h", hash);
                insert.Parameters.AddWithValue("$t", now);
                long newId = (long)(insert.ExecuteScalar() ?? 0L);

                PruneUnsafe(conn);

                return GetById(conn, newId);
            }
            catch
            {
                return null; // clipboard capture must never take the app down
            }
        }
    }

    public static List<ClipboardItem> GetRecent(string? search, int limit = 60)
    {
        lock (Gate)
        {
            var result = new List<ClipboardItem>();
            try
            {
                EnsureSchema();
                using var conn = Open();
                using var cmd = conn.CreateCommand();

                if (string.IsNullOrWhiteSpace(search))
                {
                    cmd.CommandText = "SELECT id,kind,content,image_path,pinned,created_at,last_used_at,use_count FROM items ORDER BY pinned DESC, last_used_at DESC LIMIT $lim";
                }
                else
                {
                    cmd.CommandText = "SELECT id,kind,content,image_path,pinned,created_at,last_used_at,use_count FROM items WHERE content LIKE $s ORDER BY pinned DESC, last_used_at DESC LIMIT $lim";
                    cmd.Parameters.AddWithValue("$s", $"%{search.Trim()}%");
                }
                cmd.Parameters.AddWithValue("$lim", limit);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    result.Add(new ClipboardItem(
                        reader.GetInt64(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.GetInt64(4) == 1,
                        reader.GetInt64(5),
                        reader.GetInt64(6),
                        reader.GetInt32(7)));
                }
            }
            catch { }
            return result;
        }
    }

    public static long Count()
    {
        lock (Gate)
        {
            try
            {
                EnsureSchema();
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM items";
                return (long)(cmd.ExecuteScalar() ?? 0L);
            }
            catch { return 0; }
        }
    }

    public static void SetPinned(long id, bool pinned)
    {
        lock (Gate)
        {
            try
            {
                EnsureSchema();
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE items SET pinned = $p WHERE id = $id";
                cmd.Parameters.AddWithValue("$p", pinned ? 1 : 0);
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }

    public static void Delete(long id)
    {
        lock (Gate)
        {
            try
            {
                EnsureSchema();
                using var conn = Open();
                using (var find = conn.CreateCommand())
                {
                    find.CommandText = "SELECT image_path FROM items WHERE id = $id";
                    find.Parameters.AddWithValue("$id", id);
                    var path = find.ExecuteScalar() as string;
                    if (path is not null) DeleteImageFile(path);
                }
                DeleteUnsafe(conn, id);
            }
            catch { }
        }
    }

    public static void ClearUnpinned()
    {
        lock (Gate)
        {
            try
            {
                EnsureSchema();
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT id, image_path FROM items WHERE pinned = 0 AND image_path IS NOT NULL";
                using var reader = cmd.ExecuteReader();
                var paths = new List<string>();
                while (reader.Read()) paths.Add(reader.GetString(1));
                reader.Close();
                foreach (var p in paths) DeleteImageFile(p);

                using var del = conn.CreateCommand();
                del.CommandText = "DELETE FROM items WHERE pinned = 0";
                del.ExecuteNonQuery();
            }
            catch { }
        }
    }

    public static void ClearAll()
    {
        lock (Gate)
        {
            try
            {
                EnsureSchema();
                using var conn = Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT image_path FROM items WHERE image_path IS NOT NULL";
                using var reader = cmd.ExecuteReader();
                var paths = new List<string>();
                while (reader.Read()) paths.Add(reader.GetString(0));
                reader.Close();
                foreach (var p in paths) DeleteImageFile(p);

                using var del = conn.CreateCommand();
                del.CommandText = "DELETE FROM items";
                del.ExecuteNonQuery();
            }
            catch { }
        }
    }

    public static void Prune()
    {
        lock (Gate)
        {
            try
            {
                EnsureSchema();
                using var conn = Open();
                PruneUnsafe(conn);
            }
            catch { }
        }
    }

    private static void PruneUnsafe(SqliteConnection conn)
    {
        var maxItems = Math.Max(10, MaxItems);
        var ttlSeconds = 30L * 24 * 3600;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // TTL for unpinned
        using (var ttl = conn.CreateCommand())
        {
            ttl.CommandText = "SELECT id, image_path FROM items WHERE pinned = 0 AND last_used_at < $t";
            ttl.Parameters.AddWithValue("$t", now - ttlSeconds);
            using var reader = ttl.ExecuteReader();
            var paths = new List<string>();
            var ids = new List<long>();
            while (reader.Read())
            {
                ids.Add(reader.GetInt64(0));
                if (!reader.IsDBNull(1)) paths.Add(reader.GetString(1));
            }
            reader.Close();
            foreach (var p in paths) DeleteImageFile(p);
            foreach (var id in ids) DeleteUnsafe(conn, id);
        }

        // max items, keep pinned beyond cap
        using (var cap = conn.CreateCommand())
        {
            cap.CommandText = """
                SELECT id, image_path FROM items WHERE id NOT IN (
                    SELECT id FROM (
                        SELECT id, ROW_NUMBER() OVER (ORDER BY pinned DESC, last_used_at DESC) rn
                        FROM items
                    ) WHERE rn <= $max
                ) AND pinned = 0
                """;
            cap.Parameters.AddWithValue("$max", maxItems);
            using var reader = cap.ExecuteReader();
            var paths = new List<string>();
            var ids = new List<long>();
            while (reader.Read())
            {
                ids.Add(reader.GetInt64(0));
                if (!reader.IsDBNull(1)) paths.Add(reader.GetString(1));
            }
            reader.Close();
            foreach (var p in paths) DeleteImageFile(p);
            foreach (var id in ids) DeleteUnsafe(conn, id);
        }
    }

    private static void DeleteUnsafe(SqliteConnection conn, long id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM items WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Get a single item by id, or null if it's gone.</summary>
    public static ClipboardItem? GetById(long id)
    {
        lock (Gate)
        {
            try
            {
                EnsureSchema();
                using var conn = Open();
                return GetById(conn, id);
            }
            catch { return null; }
        }
    }

    public static ClipboardItem? GetById(SqliteConnection conn, long id)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id,kind,content,image_path,pinned,created_at,last_used_at,use_count FROM items WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new ClipboardItem(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetInt64(4) == 1, reader.GetInt64(5), reader.GetInt64(6), reader.GetInt32(7));
    }

    private static void DeleteImageFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
