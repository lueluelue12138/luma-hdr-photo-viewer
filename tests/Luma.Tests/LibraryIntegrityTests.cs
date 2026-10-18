using Luma.Core;
using Microsoft.Data.Sqlite;

internal static class LibraryIntegrityTests
{
    internal static async Task RunAsync(string root,Action<bool,string> assert)
    {
        string path=Path.Combine(root,"history-integrity.db");
        var stores=Enumerable.Range(0,3).Select(_=>new LibraryStore(path)).ToArray();
        await stores[0].InitializeAsync();long album=await stores[0].AddFolderAsync(Path.Combine(root,"history-photos"));
        var photos=Enumerable.Range(0,400).Select(n=>new Photo(Path.Combine(root,$"history-{n:000}.jpg"),$"history-{n:000}.jpg",100,n)).ToArray();
        await stores[0].ApplyScanAsync(album,photos,true,false);
        var writes=Enumerable.Range(0,96).Select(n=>stores[n%3].RecordOpenAsync(photos[n] with{Path=photos[n].Path.ToUpperInvariant()}));
        await Task.WhenAll(writes.Append(stores[1].ApplyScanAsync(album,photos,true,false)));
        await Task.WhenAll(Enumerable.Range(0,24).Select(n=>stores[n%3].ToggleFavoriteAsync(photos[n])));
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path}.ToString()))
        {
            db.Open();using var check=db.CreateCommand();check.CommandText="PRAGMA integrity_check";
            using var reader=check.ExecuteReader();var errors=new List<string>();while(reader.Read())errors.Add(reader.GetString(0));
            assert(errors.SequenceEqual(new[]{"ok"}),"concurrent history/favorite writes, case-insensitive upserts and scans preserve all index integrity");
        }
        assert((await stores[0].PhotosAsync(PhotoScope.Recent)).Count==96&&(await stores[0].PhotosAsync(PhotoScope.Favorites)).Count==24,"recent and favorite indexes agree with the completed metadata writes");
        var reopened=new LibraryStore(path);await reopened.InitializeAsync();
        assert((await reopened.PhotosAsync()).Count==400&&(await reopened.PhotosAsync(PhotoScope.Recent)).Count==96,"reinitialization retains healthy indexed photo and history rows");
    }
}
