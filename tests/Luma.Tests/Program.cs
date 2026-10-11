using Luma.Core;
var root=Path.Combine(Path.GetFullPath(args.FirstOrDefault()??"artifacts"),"core-test-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);var source=Path.Combine(root,"source");Directory.CreateDirectory(source);Directory.CreateDirectory(Path.Combine(source,"child"));
await File.WriteAllTextAsync(Path.Combine(source,"one.JPG"),"fixture");await File.WriteAllTextAsync(Path.Combine(source,"child","two.png"),"fixture");await File.WriteAllTextAsync(Path.Combine(source,"ignored.txt"),"fixture");
var store=new LibraryStore(Path.Combine(root,"library.db"));await store.InitializeAsync();using var indexer=new FolderIndexer(store);int checks=0;
void Assert(bool condition,string name){if(!condition)throw new Exception("FAILED: "+name);checks++;Console.WriteLine("PASS: "+name);}
Assert(new DirectoryInfo(SupportedFormats.Normalize(Path.GetPathRoot(root)!)).FullName==Path.GetPathRoot(root),"drive root stays absolute");
long id=await store.AddFolderAsync(source);Assert(await store.AddFolderAsync(source)==id,"repeat import is idempotent");
var album=(await store.AlbumsAsync()).Single();var scan=await indexer.RefreshAsync(album);Assert(scan.Added==2,"recursive supported format index");
var photos=await store.PhotosAsync();Assert(photos.Count==2,"all photos indexed");var photo=photos[0];await store.ToggleFavoriteAsync(photo);await store.RecordOpenAsync(photo);
await store.SetAlbumAsync(id,name:"My album",cover:photo.Path,sort:"newest");album=(await store.AlbumsAsync()).Single();Assert(album.Name=="My album"&&album.Cover==photo.Path&&album.Sort=="newest","album metadata persisted");
long childId=await store.AddFolderAsync(Path.Combine(source,"child"));var child=(await store.AlbumsAsync()).Single(a=>a.Id==childId);await indexer.RefreshAsync(child);Assert((await store.PhotosAsync()).Count==2,"overlapping folders deduplicate");
await File.AppendAllTextAsync(photo.Path,"changed");await indexer.RefreshAsync(album);Assert((await store.PhotosAsync(PhotoScope.Favorites)).Single().Favorite,"refresh preserves favorite");
Assert((await store.PhotosAsync(PhotoScope.Recent)).Count==1,"recent opened persisted");
await store.ApplyScanAsync(id,[],false,true);album=(await store.AlbumsAsync()).Single(a=>a.Id==id);Assert(album.Offline&&album.Count==2,"offline album retains photos");
await store.ApplyScanAsync(id,[],false,false);Assert((await store.PhotosAsync(PhotoScope.Album,id)).All(p=>!p.Missing),"incomplete scan does not mark photos missing");
var cts=new CancellationTokenSource();cts.Cancel();try{await indexer.RefreshAsync(album,cts.Token);}catch(OperationCanceledException){}
Assert((await store.PhotosAsync(PhotoScope.Album,id)).Count==2,"cancelled scan preserves index");
indexer.Forget(id);await store.RemoveAlbumAsync(id);Assert(File.Exists(photo.Path),"removing album preserves originals");Assert((await store.PhotosAsync(PhotoScope.Favorites)).Count==1,"removed album preserves favorite history");
await store.SetSettingAsync("theme","dark");Assert(await store.GetSettingAsync("theme")=="dark","settings round trip");
Assert((await store.PhotosAsync(PhotoScope.All,search:"%" )).Count==0,"search escapes wildcard input");
Console.WriteLine($"{checks} core checks passed. Artifacts: {root}");
