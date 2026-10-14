using Luma.Core;
using System.Diagnostics;
using System.Text.Json;

namespace Luma.App;
internal static class PhotoDetailsVerification
{
    internal static async Task RunAsync(MainWindow owner,string? extraPhoto)
    {
        ViewerWindow? viewer=null;var results=new List<object>();
        void Check(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
        void Save(bool complete)=>File.WriteAllText(Path.Combine(App.DataDirectory,"photo-details.json"),JsonSerializer.Serialize(new{complete,results},new JsonSerializerOptions{WriteIndented=true}));
        try{
            await owner.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(20));await ViewerPreferences.UpdateAsync(new(false,false,1));
            var folder=Path.Combine(App.DataDirectory,"fixtures");var paths=new[]{"camera.jpg","plain.png","ordinary.jpg"}.Select(n=>Path.Combine(folder,n)).ToList();
            foreach(var name in new[]{"hdr.heic","gain.jpg"}){var p=Path.Combine(folder,name);if(File.Exists(p))paths.Add(p);}
            if(extraPhoto is not null)paths.Add(extraPhoto);
            var photos=paths.Select(p=>{var f=new FileInfo(p);return new Photo(p,f.Name,f.Length,f.LastWriteTimeUtc.Ticks);}).ToList();
            viewer=new ViewerWindow(paths[0],photos,owner);viewer.Activate();await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Check(PhotoMetadataReader.Reads==0,"Closed info panels must not read metadata at startup");
            for(int n=0;n<paths.Count;n++){
                if(n!=0)await viewer.VerificationSelectAsync(n);await viewer.VerificationPresentationAsync();
                var before=viewer.VerificationStatus;int jobs=viewer.VerificationWorkerJobs;var watch=Stopwatch.StartNew();var data=await viewer.VerificationOpenInfoAsync();watch.Stop();
                Check(data is not null&&data.Path==paths[n]&&viewer.VerificationInfoFile==Path.GetFileName(paths[n]),"Details must belong to the displayed photo");
                Check(viewer.VerificationWorkerJobs==jobs&&viewer.VerificationStatus.HdrEvaluations==before.HdrEvaluations&&viewer.VerificationStatus.VsrEvaluations==before.VsrEvaluations,"Reading metadata must not decode, initialize AI, or enqueue renderer jobs");
                if(n==0){
                    Check(data!.Value("System.Photo.CameraModel")=="Luma Fixture Camera"&&data.Value("System.Photo.LensModel")=="Luma Fixture 28-200mm","The OS must read real camera/lens EXIF");
                    Check(data.Value("System.Photo.ExposureTime")=="1/4000 秒"&&data.Value("System.Photo.FNumber")=="f/3.5"&&data.Value("System.Photo.ISOSpeed")=="ISO 160"&&data.Value("System.Photo.ExposureBias")=="0 EV","EXIF quantities, reciprocal shutter and zero compensation must remain correct");
                    Check(data.Taken?.Year==2026&&data.Taken?.Month==10&&data.Taken?.Day==1&&data.Latitude==25&&data.Longitude==100,"Taken date and GPS must read correctly");
                    await viewer.VerificationInfoFilterAsync("ISO");Check(viewer.VerificationInfoFilterText.Contains("ISO 160")&&!viewer.VerificationInfoFilterText.Contains("焦距"),"Loaded metadata search must filter without changing the photo");
                    await viewer.VerificationInfoFilterAsync("");Check(viewer.VerificationInfoRows.Count>=20,"Rich fixture should expose more than a basic summary");
                }
                if(n==1)Check(data!.Taken is null&&data.Value("System.Photo.CameraModel") is null,"No EXIF must not inherit another photo's shooting data");
                Check(viewer.VerificationInfoCopyText.Contains(paths[n]),"Copied details must include the current full path");
                results.Add(new{file=Path.GetFileName(paths[n]),openWaitMilliseconds=watch.Elapsed.TotalMilliseconds,metadataMilliseconds=data!.ReadMilliseconds,rows=data.Rows.Count,taken=data.Taken is not null,gps=data.Latitude is not null,cached=data.Cached,layout=viewer.VerificationInfoLayout,noRenderJobs=true,hdrCalls=before.HdrEvaluations,vsrCalls=before.VsrEvaluations});Save(false);
            }
            int reads=PhotoMetadataReader.Reads;await viewer.VerificationSelectAsync(0);var cached=await viewer.VerificationOpenInfoAsync();Check(cached?.Cached==true&&PhotoMetadataReader.Reads==reads,"Revisiting a photo must reuse metadata");
            var pending=new List<Task>();for(int n=0;n<12;n++)pending.Add(viewer.VerificationSelectAsync(n%3));await Task.WhenAll(pending);
            var final=await viewer.VerificationOpenInfoAsync();Check(final?.Path==paths[2]&&viewer.VerificationInfoFile=="ordinary.jpg"&&final.Value("System.Photo.CameraModel") is null,"Rapid navigation must never leave stale metadata");
            // Only generated fixture metadata is changed for cache invalidation.
            File.SetLastWriteTimeUtc(paths[0],File.GetLastWriteTimeUtc(paths[0]).AddSeconds(1));var invalidated=await PhotoMetadataReader.ReadAsync(paths[0],CancellationToken.None);Check(!invalidated.Cached&&PhotoMetadataReader.Reads==reads+1,"Changed file stamps must invalidate metadata");
            await viewer.VerificationSelectAsync(paths.Count-1);await viewer.VerificationOpenInfoAsync();
            foreach(var theme in new[]{Microsoft.UI.Xaml.ElementTheme.Light,Microsoft.UI.Xaml.ElementTheme.Dark}){
                viewer.ApplyTheme(theme);await viewer.VerificationOpenInfoAsync();await Task.Delay(150);await viewer.VerificationPresentationAsync();await viewer.VerificationRenderInfoAsync("details-"+theme.ToString().ToLowerInvariant()+".png");results.Add(new{scenario="theme",theme=theme.ToString(),layout=viewer.VerificationInfoLayout});
            }
            await viewer.VerificationInfoAdvancedRenderAsync();
            results.Add(new{scenario="responsive-details",layouts=await viewer.VerificationInfoResponsiveAsync()});
            viewer.VerificationCloseInfo();Check(!viewer.VerificationInfoVisible,"Details must close independently of the photo");
            results.Add(new{scenario="cache-and-navigation",cacheEntries=PhotoMetadataReader.CachedCount,cacheBound=64,rapidSelections=12,staleResultsPrevented=true,fileStampInvalidated=true});
            viewer.Close();await viewer.VerificationDisposeAsync();viewer=null;
            for(int n=0;n<5;n++){
                string copy=Path.Combine(folder,$"close-while-reading-{n}.jpg");File.Copy(paths[0],copy);viewer=new ViewerWindow(copy,[new(copy,Path.GetFileName(copy),new FileInfo(copy).Length,File.GetLastWriteTimeUtc(copy).Ticks)],owner);viewer.Activate();await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(20));
                var pendingInfo=viewer.VerificationOpenInfoAsync();viewer.Close();await viewer.VerificationDisposeAsync();await pendingInfo;viewer=null;
                results.Add(new{scenario="close-during-metadata",iteration=n+1});Save(false);
            }
            Save(true);
        }catch{Save(false);throw;}finally{if(viewer is not null){viewer.Close();await viewer.VerificationDisposeAsync();}owner.Close();}
    }
}
