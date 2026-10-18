using Luma.Core;
using Microsoft.Data.Sqlite;
using Microsoft.UI.Xaml;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;

namespace Luma.App;

internal static class LibraryFailureVerification
{
    internal static async Task RunAsync(MainWindow host,string path)
    {
        var results=new List<object>();ViewerWindow? viewer=null;bool renamed=false;
        string report=Path.Combine(App.DataDirectory,"library-failure.json");
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        object? Sql(string sql)
        {
            using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(App.DataDirectory,"library.db")}.ToString());db.Open();using var cmd=db.CreateCommand();cmd.CommandText=sql;return cmd.ExecuteScalar();
        }
        try
        {
            await host.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(30));await host.StartupRefresh;
            await ViewerPreferences.UpdateAsync(new());
            var photos=await App.Store.PhotosAsync();Check(photos.Count==3&&photos[0].Path==path,"Requires the isolated three-photo fixture");
            Check(Convert.ToString(Sql("PRAGMA integrity_check"))!.Contains("ix_photos_opened"),"Fixture must contain real history-index corruption");
            viewer=new(path,photos,host);viewer.Activate();await viewer.VerificationReady.Task.WaitAsync(TimeSpan.FromSeconds(35));await viewer.VerificationPresentationAsync();
            Check(!viewer.VerificationErrorVisible&&viewer.VerificationLibraryNoticeOpen&&viewer.VerificationLibraryNotice.Contains("浏览记录"),"History write failure must leave the presented photo usable with an accurate notice");
            Check(viewer.VerificationFrameInfo is {Width:>0}&&viewer.VerificationRenderMetrics.Presents>0,"Photo must actually decode and present");
            results.Add(new{scenario="corrupt-history-index",actualPresentation=true,nonfatalNotice=true});
            foreach(var theme in new[]{ElementTheme.Light,ElementTheme.Dark})foreach(int width in new[]{1160,600})
                results.Add(await viewer.VerificationLibraryNoticeRenderAsync(theme,width));
            viewer.VerificationDismissLibraryNotice();Check(!viewer.VerificationLibraryNoticeOpen,"Notice must be dismissible");
            await viewer.VerificationSelectAsync(1);
            Check(!viewer.VerificationErrorVisible&&viewer.VerificationPhoto==photos[1].Path&&viewer.VerificationLibraryNoticeOpen,"Navigation must work while the history index remains corrupt");
            Sql("CREATE TRIGGER fixture_reject_favorite BEFORE UPDATE OF favorite ON photos BEGIN SELECT RAISE(ABORT,'fixture favorite write failure'); END");
            var favoriteBefore=Convert.ToInt64(Sql("SELECT SUM(favorite) FROM photos NOT INDEXED"));await viewer.VerificationFavoriteAsync();
            Check(!viewer.VerificationErrorVisible&&viewer.VerificationLibraryNotice.Contains("收藏未能保存")&&Convert.ToInt64(Sql("SELECT SUM(favorite) FROM photos NOT INDEXED"))==favoriteBefore,"Failed favorite write must not claim success or obscure the photo");
            Sql("DROP TRIGGER fixture_reject_favorite");
            results.Add(new{scenario="favorite-write-rejected",favoriteUnchanged=true,photoStillVisible=true});
            Sql("ALTER TABLE photos RENAME TO fixture_photos");renamed=true;
            await viewer.VerificationSelectAsync(0);
            Check(!viewer.VerificationErrorVisible&&viewer.VerificationLibraryNoticeOpen&&viewer.VerificationPhoto==photos[0].Path,"Favorite read and history write failures must remain separate from image decoding");
            Sql("ALTER TABLE fixture_photos RENAME TO photos");renamed=false;
            results.Add(new{scenario="metadata-read-unavailable",photoStillVisible=true});
            Sql("REINDEX ix_photos_opened");Check(Convert.ToString(Sql("PRAGMA integrity_check"))=="ok","Fixture repair must restore index integrity");
            await viewer.VerificationSelectAsync(1);
            Check(!viewer.VerificationErrorVisible&&!viewer.VerificationLibraryNoticeOpen&&Convert.ToInt64(Sql("SELECT MAX(opened) FROM photos"))>0,"Healthy navigation must save history and clear stale notices");
            var selections=Enumerable.Range(0,8).Select(n=>viewer.VerificationSelectAsync(n%2)).ToArray();await Task.WhenAll(selections);
            Check(viewer.VerificationPhoto==photos[1].Path&&!viewer.VerificationLibraryNoticeOpen,"Late metadata results must not affect the latest selection");
            await viewer.VerificationSelectAsync(2);
            Check(viewer.VerificationErrorVisible&&!viewer.VerificationLibraryNoticeOpen,"A genuinely missing photo must retain the real loading error");
            await viewer.VerificationSelectAsync(0);
            Check(!viewer.VerificationErrorVisible&&!viewer.VerificationLibraryNoticeOpen,"A valid photo must recover from a genuine loading error");
            Check(Convert.ToString(Sql("PRAGMA integrity_check"))=="ok","History and navigation must leave the repaired fixture healthy");
            results.Add(new{scenario="healthy-history-and-real-file-error",rapidSelections=8,indexIntegrity="ok"});
            File.WriteAllText(report,JsonSerializer.Serialize(new{complete=true,results},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception ex){File.WriteAllText(report,JsonSerializer.Serialize(new{complete=false,results,error=ex.ToString()},new JsonSerializerOptions{WriteIndented=true}));App.Log(ex.ToString());Environment.ExitCode=1;}
        finally{if(renamed)Sql("ALTER TABLE fixture_photos RENAME TO photos");if(viewer is not null){viewer.Close();await viewer.VerificationDisposeAsync();}host.Close();}
    }
}

public sealed partial class ViewerWindow
{
    internal async Task<object> VerificationLibraryNoticeRenderAsync(ElementTheme theme,int width)
    {
        ApplyTheme(theme);AppWindow.Resize(new((int)(width*Surface.CompositionScaleX),(int)(800*Surface.CompositionScaleY)));await Task.Delay(150);
        if(LibraryNotice.ActualWidth<=0||LibraryNotice.ActualWidth+32>Canvas.ActualWidth+1)throw new InvalidOperationException("Library notice escapes its viewport");
        var bitmap=new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();await bitmap.RenderAsync(LibraryNotice);var pixels=await bitmap.GetPixelsAsync();
        using var stream=File.OpenWrite(Path.Combine(App.DataDirectory,$"notice-{theme}-{width}.png"));using var random=stream.AsRandomAccessStream();
        var encoder=await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,random);
        encoder.SetPixelData(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,(uint)bitmap.PixelWidth,(uint)bitmap.PixelHeight,96,96,pixels.ToArray());await encoder.FlushAsync();
        return new{scenario="notice-layout",theme=theme.ToString(),width=Canvas.ActualWidth,noticeWidth=LibraryNotice.ActualWidth,noticeHeight=LibraryNotice.ActualHeight};
    }
}
