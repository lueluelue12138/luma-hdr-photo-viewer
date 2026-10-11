namespace Luma.Core;

public sealed class FolderIndexer(LibraryStore store) : IDisposable
{
    private readonly SemaphoreSlim scanGate = new(1);
    private readonly Dictionary<long, FileSystemWatcher> watchers = [];
    private readonly Dictionary<long, CancellationTokenSource> pending = [];
    private readonly object sync = new();
    private bool disposed;
    public event Action<long>? FolderChanged;
    public async Task<ScanResult> RefreshAsync(Album album, CancellationToken cancellation = default)
    {
        await scanGate.WaitAsync(cancellation);
        try
        {
            if (!Directory.Exists(album.Folder)) { Forget(album.Id);await store.ApplyScanAsync(album.Id, [], false, true); return new(album.Id,0,0,0,true); }
            var old = (await store.PhotosAsync(PhotoScope.Album, album.Id)).ToDictionary(p=>p.Path,StringComparer.OrdinalIgnoreCase);
            var files = new List<Photo>(); string? error=null;
            await Task.Run(() =>
            {
                var stack=new Stack<string>(); stack.Push(album.Folder);
                while(stack.TryPop(out var dir))
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        foreach(var path in Directory.EnumerateFiles(dir))
                        {
                            cancellation.ThrowIfCancellationRequested(); if(!SupportedFormats.IsSupported(path)) continue;
                            try { var f=new FileInfo(path); files.Add(new(SupportedFormats.Normalize(f.FullName), f.Name, f.Length, f.LastWriteTimeUtc.Ticks)); }
                            catch(IOException ex) { error=ex.Message; }
                        }
                        foreach(var child in Directory.EnumerateDirectories(dir)) if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0) stack.Push(child);
                    }
                    catch(Exception ex) when(ex is UnauthorizedAccessException or IOException) { error=ex.Message; }
                }
            },cancellation);
            cancellation.ThrowIfCancellationRequested();
            await store.ApplyScanAsync(album.Id, files, error is null, false);
            Watch(album);
            return new(album.Id,files.Count(p=>!old.ContainsKey(p.Path)),files.Count(p=>old.TryGetValue(p.Path,out var o)&&(o.Modified!=p.Modified||o.Size!=p.Size)),error is null ? old.Keys.Except(files.Select(p=>p.Path),StringComparer.OrdinalIgnoreCase).Count() : 0,false,error);
        }
        finally { scanGate.Release(); }
    }
    private void Watch(Album album)
    {
        lock(sync)
        {
            if(disposed || watchers.ContainsKey(album.Id)) return;
            try
            {
                var w=new FileSystemWatcher(album.Folder) { IncludeSubdirectories=true, NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite|NotifyFilters.Size };
                w.Changed+=(_,_)=>Schedule(album.Id); w.Created+=(_,_)=>Schedule(album.Id); w.Deleted+=(_,_)=>Schedule(album.Id); w.Renamed+=(_,_)=>Schedule(album.Id); w.Error+=(_,_)=>{Forget(album.Id);Schedule(album.Id);};
                watchers.Add(album.Id,w); w.EnableRaisingEvents=true;
            }
            catch(Exception ex) when(ex is IOException or ArgumentException or UnauthorizedAccessException) { /* Manual refresh remains available. */ }
        }
    }
    private void Schedule(long id)
    {
        CancellationToken token;
        lock(sync) { if(disposed) return; if(pending.Remove(id,out var old)){old.Cancel();old.Dispose();} var next=new CancellationTokenSource(); pending[id]=next; token=next.Token; }
        _=Task.Run(async()=>{try{await Task.Delay(1200,token);if(!token.IsCancellationRequested)FolderChanged?.Invoke(id);}catch(OperationCanceledException){}});
    }
    public void Forget(long id) { lock(sync){if(watchers.Remove(id,out var w))w.Dispose();if(pending.Remove(id,out var c)){c.Cancel();c.Dispose();}} }
    public void Dispose() { lock(sync){disposed=true;foreach(var w in watchers.Values)w.Dispose();foreach(var c in pending.Values){c.Cancel();c.Dispose();}watchers.Clear();pending.Clear();} }
    public static List<Photo> Siblings(string file)
    {
        var list=new List<Photo>();
        foreach(var p in Directory.EnumerateFiles(System.IO.Path.GetDirectoryName(file)!)) if(SupportedFormats.IsSupported(p))
        { try { var f=new FileInfo(p);list.Add(new(SupportedFormats.Normalize(p),f.Name,f.Length,f.LastWriteTimeUtc.Ticks)); }catch(IOException){} }
        return list.OrderBy(p=>p.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
