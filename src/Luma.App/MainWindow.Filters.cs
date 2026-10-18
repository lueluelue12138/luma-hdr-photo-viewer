using Luma.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace Luma.App;

public sealed partial class MainWindow
{
    private PhotoType selectedTypes;
    private readonly PhotoTypeIndex typeIndex;
    private Func<Photo,CancellationToken,Task<PhotoType>>? verificationTypeProbe;
    private async Task<PhotoType> ReadPhotoTypeAsync(Photo photo,CancellationToken token)=>verificationTypeProbe is {} probe
        ?await probe(photo,token):PhotoTypeFilter.FromFeatures(await PhotoFeaturesCache.ReadAsync(photo.Path,token));
    private sealed record TypeQuery(string Page,long Album,string Search,string Sort,string AlbumSort,PhotoType Types);
    private sealed class TypeRequest(TypeQuery key,List<Photo> source,long revision)
    {
        internal readonly TypeQuery Key=key;
        internal readonly List<Photo> Source=source;
        internal readonly long Revision=revision;
        internal PhotoFilterResult? Result;
    }
    private TypeRequest? typeRequest;
    private CancellationTokenSource? typeCancellation;
    private Task typeFilterTask=Task.CompletedTask,typeSave=Task.CompletedTask;
    private Dictionary<long,List<string>> typeAlbumPaths=[];
    private bool typeInvalidationPending;
    private bool HasTypeFilter=>selectedTypes is not (PhotoType.Unknown or PhotoType.All);
    private string TypeLabel=>string.Join("、",new[]{(PhotoType.Hdr,"HDR"),(PhotoType.Live,"实况"),(PhotoType.Standard,"普通静态")}.Where(x=>(selectedTypes&x.Item1)!=0).Select(x=>x.Item2));
    private async Task LoadTypeFilterAsync()
    {
        selectedTypes=int.TryParse(await App.Store.GetSettingAsync("photoTypeFilter"),out int value)?PhotoTypeFilter.Normalize(value):PhotoType.Unknown;
        UpdateTypeControls();
    }
    private void UpdateTypeControls()
    {
        bool previous=ready;ready=false;
        HdrTypeCheck.IsChecked=(selectedTypes&PhotoType.Hdr)!=0;LiveTypeCheck.IsChecked=(selectedTypes&PhotoType.Live)!=0;StandardTypeCheck.IsChecked=(selectedTypes&PhotoType.Standard)!=0;
        ready=previous;TypeFilterDot.Visibility=HasTypeFilter?Visibility.Visible:Visibility.Collapsed;
        string label=HasTypeFilter?$"筛选照片类型：{TypeLabel}":"筛选照片类型";
        ToolTipService.SetToolTip(TypeFilterButton,label);AutomationProperties.SetName(TypeFilterButton,label);
    }
    private async void TypeFilterChanged(object sender,RoutedEventArgs args)
    {
        if(!ready)return;
        selectedTypes=(HdrTypeCheck.IsChecked==true?PhotoType.Hdr:0)|(LiveTypeCheck.IsChecked==true?PhotoType.Live:0)|(StandardTypeCheck.IsChecked==true?PhotoType.Standard:0);
        await SaveTypeSelectionAsync();
    }
    private async void ClearTypeFilter(object sender,RoutedEventArgs args)
    {
        selectedTypes=PhotoType.Unknown;UpdateTypeControls();await SaveTypeSelectionAsync();
    }
    private async Task SaveTypeSelectionAsync()
    {
        CancelTypeFilter();UpdateTypeControls();PhotoType selected=selectedTypes;
        typeSave=SaveTypePreferenceAsync(typeSave,selected);
        try{await typeSave;if(!closed&&selectedTypes==selected)await ReloadAsync();}
        catch(Exception ex){App.Log(ex.ToString());if(!closed)await ErrorAsync("无法保存筛选条件","请稍后重试。");}
    }
    private static async Task SaveTypePreferenceAsync(Task previous,PhotoType selected)
    {
        try{await previous;}catch(Exception){/* Previous handler reports failure. */}
        await App.Store.SetSettingAsync("photoTypeFilter",((int)selected).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
    private void CancelTypeFilter()
    {
        typeCancellation?.Cancel();typeCancellation?.Dispose();typeCancellation=null;typeRequest=null;
        TypeFilterBusy.IsActive=false;TypeFilterBusy.Visibility=Visibility.Collapsed;
    }
    private void TypeFeaturesInvalidated(string prefix)
    {
        typeIndex.InvalidateFolder(prefix);if(closed||!HasTypeFilter||page=="settings")return;
        CancelTypeFilter();if(typeInvalidationPending)return;typeInvalidationPending=true;
        DispatcherQueue.TryEnqueue(async()=>
        {
            try{await Task.Delay(150,lifetime.Token);typeInvalidationPending=false;if(!closed&&HasTypeFilter&&page!="settings")await ReloadAsync();}
            catch(OperationCanceledException){}
        });
    }
    private void StartTypeFilter(List<Photo> source,Dictionary<long,List<string>>? albumPaths=null)
    {
        if(albumPaths is not null)typeAlbumPaths=albumPaths;
        var key=new TypeQuery(page,albumId,Search.Text,sort,albumSort,selectedTypes);
        if(typeRequest is {} current&&current.Key==key&&current.Revision==typeIndex.Revision&&current.Source.SequenceEqual(source))
        {
            if(current.Result is {} cached)ApplyTypeResult(cached,current);return;
        }
        CancelTypeFilter();var request=typeRequest=new(key,source,typeIndex.Revision);typeCancellation=new();
        TypeFilterBar.Visibility=Visibility.Visible;TypeFilterBusy.Visibility=Visibility.Visible;TypeFilterBusy.IsActive=true;
        TypeFilterStatus.Text=$"{TypeLabel} · 正在识别照片类型…";CountText.Text="正在筛选…";EmptyState.Visibility=Visibility.Collapsed;
        if(page=="albums")AlbumsRepeat.ItemsSource=new List<Album>();else{photos=[];PhotosRepeat.ItemsSource=photos;}
        typeFilterTask=RunTypeFilterAsync(request,typeCancellation.Token);
    }
    private async Task RunTypeFilterAsync(TypeRequest request,CancellationToken token)
    {
        bool Current()=>!closed&&ReferenceEquals(typeRequest,request)&&!token.IsCancellationRequested;
        var progress=new Progress<PhotoFilterProgress>(p=>{if(Current()&&request.Result is null)TypeFilterStatus.Text=$"{TypeLabel} · 正在识别 {p.Completed:N0} / {p.Total:N0}…";});
        try
        {
            var result=await typeIndex.FilterAsync(request.Source,request.Key.Types,progress,token);
            if(!Current())return;request.Result=result;ApplyTypeResult(result,request);
        }
        catch(OperationCanceledException){}
        catch(Exception ex)
        {
            App.Log("Photo type filter: "+ex);
            if(Current()){TypeFilterStatus.Text="暂时无法完成筛选，请刷新重试，或显示全部。";CountText.Text="筛选未完成";typeRequest=null;}
        }
        finally{if(Current()||!closed&&typeRequest is null){TypeFilterBusy.IsActive=false;TypeFilterBusy.Visibility=Visibility.Collapsed;}}
    }
    private void ApplyTypeResult(PhotoFilterResult result,TypeRequest request)
    {
        bool empty;
        if(request.Key.Page=="albums")
        {
            var matches=result.Photos.Select(p=>p.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var eligible=albums.Where(a=>a.Name.Contains(Search.Text,StringComparison.CurrentCultureIgnoreCase)).ToList();var shown=new List<Album>();
            foreach(var a in eligible)
            {
                if(!typeAlbumPaths.TryGetValue(a.Id,out var paths))continue;
                var selected=paths.Where(matches.Contains).ToList();if(selected.Count==0)continue;
                shown.Add(a with{Cover=a.Cover is not null&&matches.Contains(a.Cover)?a.Cover:selected[0],MatchedCount=selected.Count});
            }
            if(AlbumsRepeat.ItemsSource is not List<Album> displayed||!displayed.SequenceEqual(shown))AlbumsRepeat.ItemsSource=shown;
            CountText.Text=$"{shown.Count} / {eligible.Count} 个相册";empty=shown.Count==0;
            TypeFilterStatus.Text=$"{TypeLabel} · 仅显示含匹配照片的相册";
        }
        else
        {
            if(!photos.SequenceEqual(result.Photos)){photos=result.Photos;PhotosRepeat.ItemsSource=photos;}
            CountText.Text=$"{photos.Count} / {request.Source.Count} 张照片";empty=photos.Count==0;
            TypeFilterStatus.Text=$"{TypeLabel} · 已筛选";
        }
        if(result.Unavailable>0)TypeFilterStatus.Text+=$" · {result.Unavailable} 张暂时无法识别，可刷新重试";
        EmptyTitle.Text=request.Key.Page=="albums"?"没有符合筛选条件的相册":"没有符合筛选条件的照片";
        EmptyDescription.Text="试试其他类型，或点击“显示全部”。";EmptyState.Visibility=empty?Visibility.Visible:Visibility.Collapsed;
        TypeFilterBar.Visibility=Visibility.Visible;TypeFilterBusy.IsActive=false;TypeFilterBusy.Visibility=Visibility.Collapsed;
    }
}
