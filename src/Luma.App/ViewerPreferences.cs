using System.Text.Json;

namespace Luma.App;

internal sealed record ViewerPreferences(bool Hdr=false,bool Vsr=false,int Preset=1,bool AutoBrightness=true,bool AutoPeak=true,uint MiddleGray=100,uint Peak=1000,bool NativeAutoBrightness=true,bool NativeAutoPeak=true,float NativeMiddleGray=44,float NativePeak=1000,bool LiveMuted=true)
{
    private const string Key="viewer.preferences";
    private static readonly SemaphoreSlim save=new(1);
    internal static ViewerPreferences Current {get;private set;}=new();
    internal static async Task LoadAsync()
    {
        try{Current=JsonSerializer.Deserialize<ViewerPreferences>(await App.Store.GetSettingAsync(Key)??"null")??new();}
        catch(JsonException){Current=new();}
        Current=Current with{Preset=Math.Clamp(Current.Preset,0,2),MiddleGray=Math.Clamp(Current.MiddleGray,10u,100u),Peak=Math.Clamp(Current.Peak,400u,2000u),NativeMiddleGray=Math.Clamp(Current.NativeMiddleGray,10f,300f),NativePeak=Math.Clamp(Current.NativePeak,80f,10000f)};
    }
    internal static async Task UpdateAsync(ViewerPreferences value)
    {
        Current=value;
        await save.WaitAsync();
        try{await App.Store.SetSettingAsync(Key,JsonSerializer.Serialize(Current));}
        finally{save.Release();}
    }
}
