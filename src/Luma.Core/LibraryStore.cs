using Microsoft.Data.Sqlite;

namespace Luma.Core;

/// <summary>Serialized short-lived connections; originals are never moved or deleted.</summary>
public sealed class LibraryStore
{
    private readonly string connectionString;
    private readonly SemaphoreSlim gate = new(1);
    public LibraryStore(string databasePath)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(databasePath))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();
    }
    // SQLite's async methods execute synchronously. Keep database work off the UI thread.
    private Task<T> Use<T>(Func<SqliteConnection, Task<T>> action) => Task.Run(async () =>
    {
        await gate.WaitAsync();
        try { await using var c = new SqliteConnection(connectionString); await c.OpenAsync(); return await action(c); }
        finally { gate.Release(); }
    });
    private static SqliteCommand Cmd(SqliteConnection c, string sql, params (string, object?)[] values)
    {
        var cmd = c.CreateCommand(); cmd.CommandText = sql;
        foreach (var (key, value) in values) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return cmd;
    }
    public Task InitializeAsync() => Use(async c =>
    {
        using var cmd = Cmd(c, """
            PRAGMA journal_mode=WAL;
            PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS albums(id INTEGER PRIMARY KEY, name TEXT NOT NULL, folder TEXT NOT NULL COLLATE NOCASE UNIQUE, cover TEXT, offline INTEGER NOT NULL DEFAULT 0, sort TEXT NOT NULL DEFAULT 'name');
            CREATE TABLE IF NOT EXISTS photos(path TEXT PRIMARY KEY COLLATE NOCASE, name TEXT NOT NULL, size INTEGER NOT NULL, modified INTEGER NOT NULL, favorite INTEGER NOT NULL DEFAULT 0, opened INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS album_photos(album_id INTEGER NOT NULL, path TEXT NOT NULL COLLATE NOCASE, missing INTEGER NOT NULL DEFAULT 0, PRIMARY KEY(album_id,path));
            CREATE INDEX IF NOT EXISTS ix_album_photos_path ON album_photos(path);
            CREATE INDEX IF NOT EXISTS ix_photos_opened ON photos(opened);
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
            PRAGMA user_version=1;
            """);
        return await cmd.ExecuteNonQueryAsync();
    });
    public Task<long> AddFolderAsync(string folder) => Use(async c =>
    {
        folder = SupportedFormats.Normalize(folder);
        using var cmd = Cmd(c, "INSERT INTO albums(name,folder) VALUES($n,$f) ON CONFLICT(folder) DO NOTHING; SELECT id FROM albums WHERE folder=$f", ("$n", new DirectoryInfo(folder).Name), ("$f", folder));
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    });
    public Task<List<Album>> AlbumsAsync() => Use(async c =>
    {
        using var cmd = Cmd(c, """
            SELECT a.id,a.name,a.folder,COALESCE((SELECT ap.path FROM album_photos ap WHERE ap.album_id=a.id AND ap.path=a.cover AND ap.missing=0),(SELECT ap.path FROM album_photos ap WHERE ap.album_id=a.id AND ap.missing=0 ORDER BY ap.path LIMIT 1)),
            (SELECT COUNT(*) FROM album_photos ap WHERE ap.album_id=a.id AND ap.missing=0),a.offline,a.sort FROM albums a ORDER BY a.name COLLATE NOCASE
            """);
        using var r = await cmd.ExecuteReaderAsync(); var list = new List<Album>();
        while (await r.ReadAsync()) list.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetInt32(4), r.GetBoolean(5), r.GetString(6)));
        return list;
    });
    public Task<List<Photo>> PhotosAsync(PhotoScope scope = PhotoScope.All, long albumId = 0, string search = "", string sort = "name") => Use(async c =>
    {
        string filter = scope switch { PhotoScope.Album => "EXISTS(SELECT 1 FROM album_photos ap WHERE ap.path=p.path AND ap.album_id=$a AND ap.missing=0)", PhotoScope.Favorites => "p.favorite=1", PhotoScope.Recent => "p.opened>0", _ => "EXISTS(SELECT 1 FROM album_photos ap WHERE ap.path=p.path AND ap.missing=0)" };
        string order = scope == PhotoScope.Recent ? "p.opened DESC" : sort switch { "newest" => "p.modified DESC", "oldest" => "p.modified ASC", _ => "p.name COLLATE NOCASE, p.path" };
        using var cmd = Cmd(c, $"SELECT p.path,p.name,p.size,p.modified,p.favorite,p.opened,CASE WHEN EXISTS(SELECT 1 FROM album_photos ap WHERE ap.path=p.path) AND NOT EXISTS(SELECT 1 FROM album_photos ap WHERE ap.path=p.path AND ap.missing=0) THEN 1 ELSE 0 END FROM photos p WHERE {filter} AND p.name LIKE $q ESCAPE '\\' ORDER BY {order}", ("$a", albumId), ("$q", "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"));
        using var r = await cmd.ExecuteReaderAsync(); var list = new List<Photo>();
        while (await r.ReadAsync()) list.Add(new(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3), r.GetBoolean(4), r.GetInt64(5), r.GetBoolean(6)));
        return list;
    });
    public Task<bool> ToggleFavoriteAsync(Photo photo) => Use(async c =>
    {
        using var insert = Cmd(c, "INSERT INTO photos(path,name,size,modified) VALUES($p,$n,$s,$m) ON CONFLICT(path) DO NOTHING", ("$p", photo.Path), ("$n", photo.Name), ("$s", photo.Size), ("$m", photo.Modified)); await insert.ExecuteNonQueryAsync();
        using var cmd = Cmd(c, "UPDATE photos SET favorite=NOT favorite WHERE path=$p; SELECT favorite FROM photos WHERE path=$p", ("$p", photo.Path));
        return Convert.ToBoolean(await cmd.ExecuteScalarAsync());
    });
    public Task<bool> IsFavoriteAsync(string path) => Use(async c =>{using var cmd=Cmd(c,"SELECT favorite FROM photos WHERE path=$p",("$p",path));var value=await cmd.ExecuteScalarAsync();return value is not null&&Convert.ToBoolean(value);});
    public Task RecordOpenAsync(Photo photo) => Use(async c =>
    {
        using var cmd = Cmd(c, "INSERT INTO photos(path,name,size,modified,opened) VALUES($p,$n,$s,$m,$o) ON CONFLICT(path) DO UPDATE SET opened=excluded.opened,name=excluded.name,size=excluded.size,modified=excluded.modified", ("$p", photo.Path), ("$n", photo.Name), ("$s", photo.Size), ("$m", photo.Modified), ("$o", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        return await cmd.ExecuteNonQueryAsync();
    });
    public Task SetAlbumAsync(long id, string? name = null, string? cover = null, string? sort = null) => Use(async c =>
    {
        using var cmd = Cmd(c, "UPDATE albums SET name=COALESCE($n,name),cover=COALESCE($c,cover),sort=COALESCE($s,sort) WHERE id=$id", ("$id", id), ("$n", name), ("$c", cover), ("$s", sort)); return await cmd.ExecuteNonQueryAsync();
    });
    public Task RemoveAlbumAsync(long id) => Use(async c =>
    {
        using var cmd = Cmd(c, "DELETE FROM album_photos WHERE album_id=$a; DELETE FROM albums WHERE id=$a; DELETE FROM photos WHERE favorite=0 AND opened=0 AND NOT EXISTS(SELECT 1 FROM album_photos ap WHERE ap.path=photos.path)", ("$a", id));
        return await cmd.ExecuteNonQueryAsync();
    });
    public Task<string?> GetSettingAsync(string key) => Use(async c => { using var cmd = Cmd(c, "SELECT value FROM settings WHERE key=$k", ("$k", key)); return await cmd.ExecuteScalarAsync() as string; });
    public Task SetSettingAsync(string key, string value) => Use(async c => { using var cmd = Cmd(c, "INSERT INTO settings VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value", ("$k", key), ("$v", value)); return await cmd.ExecuteNonQueryAsync(); });
    public Task ApplyScanAsync(long id, IReadOnlyList<Photo> files, bool complete, bool offline) => Use(async c =>
    {
        using var tx = c.BeginTransaction();
        using var status = Cmd(c, "UPDATE albums SET offline=$o WHERE id=$a", ("$o", offline), ("$a", id)); status.Transaction = tx; await status.ExecuteNonQueryAsync();
        if (offline) { tx.Commit(); return 0; }
        // A failed/cancelled traversal must never mark unseen originals missing.
        using var staging=Cmd(c,"CREATE TEMP TABLE IF NOT EXISTS scan_paths(path TEXT PRIMARY KEY COLLATE NOCASE); DELETE FROM scan_paths");staging.Transaction=tx;await staging.ExecuteNonQueryAsync();
        using var present=Cmd(c,"INSERT OR IGNORE INTO scan_paths VALUES($p)");present.Transaction=tx;present.Parameters.Add("$p",SqliteType.Text);
        using var photo = Cmd(c, "INSERT INTO photos(path,name,size,modified) VALUES($p,$n,$s,$m) ON CONFLICT(path) DO UPDATE SET name=excluded.name,size=excluded.size,modified=excluded.modified WHERE photos.name<>excluded.name OR photos.size<>excluded.size OR photos.modified<>excluded.modified"); photo.Transaction = tx;
        photo.Parameters.Add("$p", SqliteType.Text); photo.Parameters.Add("$n", SqliteType.Text); photo.Parameters.Add("$s", SqliteType.Integer); photo.Parameters.Add("$m", SqliteType.Integer);
        using var link = Cmd(c, "INSERT INTO album_photos(album_id,path,missing) SELECT $a,$p,0 WHERE EXISTS(SELECT 1 FROM albums WHERE id=$a) ON CONFLICT(album_id,path) DO UPDATE SET missing=0 WHERE album_photos.missing<>0", ("$a", id)); link.Transaction = tx; link.Parameters.Add("$p", SqliteType.Text);
        foreach (var f in files) { photo.Parameters["$p"].Value=f.Path; photo.Parameters["$n"].Value=f.Name; photo.Parameters["$s"].Value=f.Size; photo.Parameters["$m"].Value=f.Modified; await photo.ExecuteNonQueryAsync(); link.Parameters["$p"].Value=f.Path; await link.ExecuteNonQueryAsync();present.Parameters["$p"].Value=f.Path;await present.ExecuteNonQueryAsync(); }
        if(complete){using var missing=Cmd(c,"UPDATE album_photos SET missing=1 WHERE album_id=$a AND missing=0 AND path NOT IN(SELECT path FROM scan_paths)",("$a",id));missing.Transaction=tx;await missing.ExecuteNonQueryAsync();}
        tx.Commit(); return files.Count;
    });
}
