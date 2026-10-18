using Luma.Core;
using Microsoft.Data.Sqlite;

internal static class AlbumOrganizationTests
{
    internal static async Task RunAsync(string root,Action<bool,string> assert)
    {
        string legacyPath=Path.Combine(root,"legacy-albums.db"),original=Path.Combine(root,"original.jpg");
        await File.WriteAllTextAsync(original,"original must remain untouched");
        using(var connection=new SqliteConnection($"Data Source={legacyPath}"))
        {
            connection.Open();using var command=connection.CreateCommand();command.CommandText="""
                CREATE TABLE albums(id INTEGER PRIMARY KEY,name TEXT NOT NULL,folder TEXT NOT NULL COLLATE NOCASE UNIQUE,cover TEXT,offline INTEGER NOT NULL DEFAULT 0,sort TEXT NOT NULL DEFAULT 'name');
                CREATE TABLE photos(path TEXT PRIMARY KEY COLLATE NOCASE,name TEXT NOT NULL,size INTEGER NOT NULL,modified INTEGER NOT NULL,favorite INTEGER NOT NULL DEFAULT 0,opened INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE album_photos(album_id INTEGER NOT NULL,path TEXT NOT NULL COLLATE NOCASE,missing INTEGER NOT NULL DEFAULT 0,PRIMARY KEY(album_id,path));
                CREATE TABLE settings(key TEXT PRIMARY KEY,value TEXT NOT NULL);
                INSERT INTO albums VALUES(42,'Existing album',$folder,$photo,1,'oldest');
                INSERT INTO photos VALUES($photo,'original.jpg',33,123,1,456);
                INSERT INTO album_photos VALUES(42,$photo,0);
                INSERT INTO settings VALUES('theme','dark');
                PRAGMA user_version=1;
                """;
            command.Parameters.AddWithValue("$folder",root);command.Parameters.AddWithValue("$photo",original);command.ExecuteNonQuery();
        }
        var legacy=new LibraryStore(legacyPath);await legacy.InitializeAsync();await legacy.InitializeAsync();
        var migrated=(await legacy.AlbumsAsync()).Single();
        assert(!migrated.Pinned&&migrated.Id==42&&migrated.Cover==original&&migrated.Count==1&&migrated.Offline&&migrated.Sort=="oldest","old library migrates additively and initialization is repeatable");
        assert((await legacy.PhotosAsync(PhotoScope.Favorites)).Single().Opened==456&&await legacy.GetSettingAsync("theme")=="dark","migration preserves favorites, history and preferences");
        await legacy.SetAlbumAsync(42,pinned:true);await legacy.SetSettingAsync("albumOverviewSort","most");
        legacy=new(legacyPath);await legacy.InitializeAsync();migrated=(await legacy.AlbumsAsync()).Single();
        assert(migrated.Pinned&&migrated.Sort=="oldest"&&await legacy.GetSettingAsync("albumOverviewSort")=="most","pin and overview sort persist across store reopening independently of photo sort");
        await legacy.SetAlbumAsync(42,name:"Renamed");
        assert((await legacy.AlbumsAsync()).Single().Pinned,"unrelated album edits preserve pin state");
        await legacy.SetAlbumAsync(42,pinned:false);
        assert(!(await legacy.AlbumsAsync()).Single().Pinned&&await File.ReadAllTextAsync(original)=="original must remain untouched","unpin persists false and leaves original unchanged");

        var store=new LibraryStore(Path.Combine(root,"album-order.db"));await store.InitializeAsync();
        string[] names=["Beta","Alpha","Gamma","alpha","Empty"];int[] counts=[3,1,0,2,1];var ids=new long[names.Length];
        for(int n=0;n<names.Length;n++)
        {
            ids[n]=await store.AddFolderAsync(Path.Combine(root,"sort-fixtures",n.ToString()));
            await store.SetAlbumAsync(ids[n],name:names[n],pinned:n is 0 or 2,sort:"oldest");
            await store.ApplyScanAsync(ids[n],Enumerable.Range(0,counts[n]).Select(i=>new Photo(Path.Combine(root,$"sort-{n}-{i}.jpg"),$"{i}.jpg",10,i)).ToList(),true,false);
        }
        var expected=new Dictionary<string,int[]>{{"name",[0,2,1,3,4]},{"name-desc",[2,0,4,1,3]},{"newest",[2,0,4,3,1]},{"oldest",[0,2,1,3,4]},{"most",[0,2,3,1,4]},{"least",[2,0,1,4,3]}};
        foreach(var (sort,order) in expected)
            assert((await store.AlbumsAsync(sort)).Select(a=>a.Id).SequenceEqual(order.Select(n=>ids[n])),$"album order {sort}: pinned first, stable ties and empty albums");
        assert((await store.AlbumsAsync("invalid; DROP TABLE albums")).Select(a=>a.Id).SequenceEqual((await store.AlbumsAsync()).Select(a=>a.Id)),"unknown sort falls back safely without SQL interpolation");
        assert((await store.AlbumsAsync()).All(a=>a.Sort=="oldest"),"overview sorting never changes sorting inside albums");
        assert(await store.AddFolderAsync(Path.Combine(root,"sort-fixtures","0"))==ids[0]&&(await store.AlbumsAsync()).First(a=>a.Id==ids[0]).Pinned,"repeat folder import preserves pin and import order");
        await store.ApplyScanAsync(ids[0],[],false,true);
        assert((await store.AlbumsAsync("most")).First().Id==ids[0],"offline pinned album retains its count and order");
        await store.ApplyScanAsync(ids[3],[],true,false);
        assert((await store.AlbumsAsync("most")).Last().Id==ids[3],"photo-count sorting excludes missing links and keeps deterministic ties");
    }
}
