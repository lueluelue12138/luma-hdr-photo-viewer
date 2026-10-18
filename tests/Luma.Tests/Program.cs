using Luma.Core;
using Microsoft.Data.Sqlite;
var root=Path.Combine(Path.GetFullPath(args.FirstOrDefault()??"artifacts"),"core-test-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);var source=Path.Combine(root,"source");Directory.CreateDirectory(source);Directory.CreateDirectory(Path.Combine(source,"child"));
await File.WriteAllTextAsync(Path.Combine(source,"one.JPG"),"fixture");await File.WriteAllTextAsync(Path.Combine(source,"child","two.png"),"fixture");await File.WriteAllTextAsync(Path.Combine(source,"ignored.txt"),"fixture");await File.WriteAllTextAsync(Path.Combine(source,"._one.JPG"),"AppleDouble metadata, not a photo");
var store=new LibraryStore(Path.Combine(root,"library.db"));await store.InitializeAsync();using var indexer=new FolderIndexer(store);int checks=0;
void Assert(bool condition,string name){if(!condition)throw new Exception("FAILED: "+name);checks++;Console.WriteLine("PASS: "+name);}
Assert(new DirectoryInfo(SupportedFormats.Normalize(Path.GetPathRoot(root)!)).FullName==Path.GetPathRoot(root),"drive root stays absolute");
Assert(LaunchArguments.PhotoPath(["--test-library","test.jpg","--import","album.png","--verify-startup","missing.jpg"])=="missing.jpg","activation skips folder arguments without checking disk");
Assert(LaunchArguments.PhotoPath(["--import","album.jpg"]) is null,"image-like import folder still launches gallery");
Assert(LaunchArguments.PhotoPath(["--test-library","test.jpg","--gallery-source","album.png"]) is null,"read-only gallery source cannot become photo activation");
Assert(LaunchArguments.PhotoPath(["--verify-startup","._one.JPG","photo.PNG"])=="photo.PNG","activation respects supported formats and metadata exclusion");
Assert(LaunchArguments.PhotoPath(["camera.HEIC"])=="camera.HEIC"&&SupportedFormats.IsSupported("camera.heif"),"HEIC and HEIF support activation and indexing");
long id=await store.AddFolderAsync(source);Assert(await store.AddFolderAsync(source)==id,"repeat import is idempotent");
var album=(await store.AlbumsAsync()).Single();var scan=await indexer.RefreshAsync(album);Assert(scan.Added==2,"recursive supported format index");
var photos=await store.PhotosAsync();Assert(photos.Count==2,"all photos indexed");var photo=photos[0];await store.ToggleFavoriteAsync(photo);await store.RecordOpenAsync(photo);
using(var observer=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(root,"library.db")}.ToString()))
{
    observer.Open();using var version=observer.CreateCommand();version.CommandText="PRAGMA data_version";
    var before=version.ExecuteScalar();var unchanged=await indexer.RefreshAsync(album);
    Assert(unchanged.Added==0&&unchanged.Updated==0&&unchanged.Missing==0&&Equals(before,version.ExecuteScalar()),"unchanged refresh performs no database writes");
    await store.ApplyScanAsync(id,[],false,true);await indexer.RefreshAsync(album);
    Assert(!(await store.AlbumsAsync()).Single().Offline,"unchanged files restore offline status even with a stale album record");
}
await store.SetAlbumAsync(id,name:"My album",cover:photo.Path,sort:"newest");album=(await store.AlbumsAsync()).Single();Assert(album.Name=="My album"&&album.Cover==photo.Path&&album.Sort=="newest","album metadata persisted");
long childId=await store.AddFolderAsync(Path.Combine(source,"child"));var child=(await store.AlbumsAsync()).Single(a=>a.Id==childId);await indexer.RefreshAsync(child);Assert((await store.PhotosAsync()).Count==2,"overlapping folders deduplicate");
await File.AppendAllTextAsync(photo.Path,"changed");await indexer.RefreshAsync(album);Assert((await store.PhotosAsync(PhotoScope.Favorites)).Single().Favorite,"refresh preserves favorite");
Assert((await store.PhotosAsync(PhotoScope.Recent)).Count==1,"recent opened persisted");
await store.ApplyScanAsync(id,[],false,true);album=(await store.AlbumsAsync()).Single(a=>a.Id==id);Assert(album.Offline&&album.Count==2,"offline album retains photos");
await store.ApplyScanAsync(id,[],false,false);Assert((await store.PhotosAsync(PhotoScope.Album,id)).All(p=>!p.Missing),"incomplete scan does not mark photos missing");
var cts=new CancellationTokenSource();cts.Cancel();try{await indexer.RefreshAsync(album,cts.Token);}catch(OperationCanceledException){}
Assert((await store.PhotosAsync(PhotoScope.Album,id)).Count==2,"cancelled scan preserves index");
Assert(!SupportedFormats.IsSupported(Path.Combine(source,"._one.JPG"))&&FolderIndexer.Siblings(photo.Path).All(p=>!p.Name.StartsWith("._")),"AppleDouble excluded from albums and sibling navigation");
var removed=photos.Single(p=>p.Path!=photo.Path);await store.SetAlbumAsync(id,cover:removed.Path);File.Delete(removed.Path);await indexer.RefreshAsync(album);
Assert((await store.PhotosAsync(PhotoScope.Album,id)).Count==1,"complete online refresh hides deleted album entries");
Assert((await store.AlbumsAsync()).Single(a=>a.Id==id).Cover==photo.Path,"deleted explicit cover falls back to a present photo");
Assert((await store.PhotosAsync()).Count==2,"unrefreshed overlapping album retains its independent index");
await indexer.RefreshAsync(child);Assert((await store.PhotosAsync()).Count==1,"all photos hides deleted entries after both source scans");
await store.ApplyScanAsync(id,[],false,true);Assert((await store.PhotosAsync(PhotoScope.Album,id)).Single().Path==photo.Path,"offline source keeps its last complete contents");
indexer.Forget(id);await store.RemoveAlbumAsync(id);Assert(File.Exists(photo.Path),"removing album preserves originals");Assert((await store.PhotosAsync(PhotoScope.Favorites)).Count==1,"removed album preserves favorite history");
await store.SetSettingAsync("theme","dark");Assert(await store.GetSettingAsync("theme")=="dark","settings round trip");
Assert((await store.PhotosAsync(PhotoScope.All,search:"%" )).Count==0,"search escapes wildcard input");
Assert(PhotoMetadataFormatting.Format("System.Photo.ExposureTime",.00025)=="1/4000 秒"&&PhotoMetadataFormatting.Format("System.Photo.ExposureTime",2.5)=="2.5 秒","photo metadata shutter seconds and reciprocal formatting");
Assert(PhotoMetadataFormatting.Format("System.Photo.ExposureBias",0)=="0 EV"&&PhotoMetadataFormatting.Format("System.Photo.WhiteBalance",0)=="自动","photo metadata preserves meaningful zero values");
Assert(PhotoMetadataFormatting.Format("System.Photo.FNumber",3.5)=="f/3.5"&&PhotoMetadataFormatting.Format("System.Photo.FocalLength",48)=="48 mm","photo metadata units distinguish f-number from APEX");
Assert(PhotoMetadataFormatting.Format("System.Photo.ExposureTime",double.NaN) is null&&PhotoMetadataFormatting.Format("System.Photo.ISOSpeed",0) is null,"invalid metadata values cannot pretend to be capture settings");
Assert(PhotoMetadataFormatting.Rows(new Dictionary<string,object>()).Count==0,"missing EXIF stays absent");
var metadataRows=PhotoMetadataFormatting.Rows(new Dictionary<string,object>{{"System.Photo.ISOSpeed",160},{"System.Photo.ExposureBias",0},{"System.Photo.LensModel","Fixture Lens"},{"System.Image.ColorSpace",65535}});
Assert(metadataRows.Count==4&&PhotoMetadataFormatting.Filter(metadataRows,"ISO").Single().Value=="ISO 160"&&PhotoMetadataFormatting.Filter(metadataRows,"missing").Count==0,"photo metadata search and uncalibrated colour remain honest");
LivePhotoTests.Run(root,Assert);
await AlbumOrganizationTests.RunAsync(root,Assert);
await PhotoTypeFilterTests.RunAsync(root,Assert);
Console.WriteLine($"{checks} core checks passed. Artifacts: {root}");
