using System.Globalization;

namespace Luma.Core;

public sealed record PhotoProperty(string Key,string Group,string Label,string Value);
public sealed record PhotoPropertyGroup(string Name,IReadOnlyList<PhotoProperty> Rows);
public static class PhotoMetadataFormatting
{
    private static readonly (string Group,string Label,string Key)[] fields=[
        ("相机与镜头","相机制造商","System.Photo.CameraManufacturer"),("相机与镜头","相机型号","System.Photo.CameraModel"),
        ("相机与镜头","镜头制造商","System.Photo.LensManufacturer"),("相机与镜头","镜头型号","System.Photo.LensModel"),
        ("相机与镜头","相机序列号","System.Photo.CameraSerialNumber"),("相机与镜头","焦距","System.Photo.FocalLength"),
        ("相机与镜头","35 mm 等效焦距","System.Photo.FocalLengthInFilm"),("相机与镜头","数字变焦","System.Photo.DigitalZoom"),
        ("相机与镜头","最大光圈（APEX）","System.Photo.MaxAperture"),("相机与镜头","拍摄距离","System.Photo.SubjectDistance"),
        ("曝光","快门","System.Photo.ExposureTime"),("曝光","光圈","System.Photo.FNumber"),("曝光","ISO","System.Photo.ISOSpeed"),
        ("曝光","曝光补偿","System.Photo.ExposureBias"),("曝光","曝光程序","System.Photo.ExposureProgram"),
        ("曝光","测光模式","System.Photo.MeteringMode"),("曝光","白平衡","System.Photo.WhiteBalance"),
        ("曝光","闪光灯","System.Photo.Flash"),("曝光","光源","System.Photo.LightSource"),("曝光","亮度（APEX）","System.Photo.Brightness"),
        ("图像","系统图像宽度","System.Image.HorizontalSize"),("图像","系统图像高度","System.Image.VerticalSize"),
        ("图像","水平分辨率","System.Image.HorizontalResolution"),("图像","垂直分辨率","System.Image.VerticalResolution"),
        ("图像","每像素位数","System.Image.BitDepth"),("图像","EXIF 色彩空间","System.Image.ColorSpace"),
        ("图像","EXIF 方向","System.Photo.Orientation"),("图像","压缩方式","System.Image.CompressionText"),
        ("图像","对比度","System.Photo.Contrast"),("图像","饱和度","System.Photo.Saturation"),("图像","锐度","System.Photo.Sharpness"),
        ("其他","EXIF 版本","System.Photo.EXIFVersion"),("其他","处理软件","System.SoftwareUsed"),
        ("其他","标题","System.Title"),("其他","作者","System.Author"),("其他","版权","System.Copyright"),
        ("其他","关键词","System.Keywords"),("其他","备注","System.Comment"),("其他","评分","System.Rating")
    ];
    public static IReadOnlyList<string> PropertyKeys {get;}=fields.Select(f=>f.Key).Append("System.Photo.DateTaken").ToArray();
    public static IReadOnlyList<PhotoProperty> Rows(IReadOnlyDictionary<string,object> properties)
    {
        var rows=new List<PhotoProperty>();
        foreach(var f in fields)if(properties.TryGetValue(f.Key,out var value)&&value is not null){var text=Format(f.Key,value);if(!string.IsNullOrWhiteSpace(text))rows.Add(new(f.Key,f.Group,f.Label,text));}
        return rows;
    }
    public static string Size(long bytes)
    {
        if(bytes<0)return "未知";double n=bytes;string[] units=["B","KiB","MiB","GiB","TiB"];int i=0;
        while(n>=1024&&i<units.Length-1){n/=1024;i++;}return $"{n.ToString(i==0?"0":"0.##",CultureInfo.InvariantCulture)} {units[i]} · {bytes.ToString("N0",CultureInfo.CurrentCulture)} 字节";
    }
    public static string Date(DateTimeOffset value)=>value.ToLocalTime().ToString("yyyy年M月d日 HH:mm:ss",CultureInfo.CurrentCulture);
    public static IReadOnlyList<PhotoProperty> Filter(IEnumerable<PhotoProperty> rows,string query)=>rows.Where(r=>string.IsNullOrWhiteSpace(query)||$"{r.Group} {r.Label} {r.Value} {r.Key}".Contains(query.Trim(),StringComparison.OrdinalIgnoreCase)).ToArray();
    public static string? Format(string key,object value)
    {
        string text=value is IEnumerable<string> list?string.Join("、",list.Take(32)):Convert.ToString(value,CultureInfo.InvariantCulture)?.Trim('\0',' ','\r','\n','\t')??"";
        if(text.Length>2048)text=text[..2048]+"…";
        if(key is "System.Image.CompressionText" or "System.Photo.EXIFVersion" or "System.SoftwareUsed" or "System.Title" or "System.Author" or "System.Copyright" or "System.Keywords" or "System.Comment"||key.Contains("Manufacturer")||key.Contains("Model")||key.Contains("SerialNumber"))return text;
        if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double n)||!double.IsFinite(n))return null;
        string F(string format="0.###")=>n.ToString(format,CultureInfo.InvariantCulture);
        string Enum(IReadOnlyDictionary<int,string> names)=>names.TryGetValue((int)n,out var name)?name:$"未识别（{F()}）";
        return key switch{
            "System.Photo.ExposureTime"=>n<=0?null:n<1?$"1/{(1/n).ToString("0.##",CultureInfo.InvariantCulture)} 秒":$"{F()} 秒",
            "System.Photo.FNumber"=>n>0?$"f/{F()}":null,
            "System.Photo.FocalLength" or "System.Photo.FocalLengthInFilm"=>n>0?$"{F()} mm":null,
            "System.Photo.ISOSpeed"=>n>0?$"ISO {F("0")}":null,
            "System.Photo.ExposureBias"=>$"{(n>0?"+":"")}{F("0.##")} EV",
            "System.Photo.DigitalZoom"=>n>0?$"{F()}×":null,
            "System.Photo.SubjectDistance"=>n>0?$"{F()} m":null,
            "System.Image.HorizontalResolution" or "System.Image.VerticalResolution"=>n>0?$"{F("0.##")} dpi":null,
            "System.Image.HorizontalSize" or "System.Image.VerticalSize"=>n>0?$"{F("0")} 像素":null,
            "System.Image.BitDepth"=>n>0?$"{F("0")} 位 / 像素（系统报告）":null,
            "System.Image.ColorSpace"=>n==1?"sRGB":n==65535?"未标定（不代表没有 ICC 配置）":$"EXIF 值 {F("0")}",
            "System.Photo.WhiteBalance"=>Enum(new Dictionary<int,string>{{0,"自动"},{1,"手动"}}),
            "System.Photo.ExposureProgram"=>Enum(new Dictionary<int,string>{{0,"未指定"},{1,"手动"},{2,"程序自动"},{3,"光圈优先"},{4,"快门优先"},{5,"创意程序"},{6,"运动程序"},{7,"人像"},{8,"风景"}}),
            "System.Photo.MeteringMode"=>Enum(new Dictionary<int,string>{{0,"未知"},{1,"平均"},{2,"中央重点"},{3,"点测光"},{4,"多点"},{5,"多区"},{6,"局部"},{255,"其他"}}),
            "System.Photo.Orientation"=>Enum(new Dictionary<int,string>{{1,"正常"},{2,"水平镜像"},{3,"旋转 180°"},{4,"垂直镜像"},{5,"镜像并旋转 90°"},{6,"旋转 90°"},{7,"镜像并旋转 270°"},{8,"旋转 270°"}}),
            "System.Photo.Flash"=>$"{(((int)n&1)==1?"已闪光":"未闪光")} · {(((int)n&24)==24?"自动":((int)n&24)==16?"强制关闭":((int)n&24)==8?"强制开启":"未指定模式")}{(((int)n&64)!=0?" · 防红眼":"")}",
            "System.Photo.LightSource"=>Enum(new Dictionary<int,string>{{0,"未知"},{1,"日光"},{2,"荧光灯"},{3,"钨丝灯"},{4,"闪光灯"},{9,"晴天"},{10,"阴天"},{11,"阴影"},{255,"其他"}}),
            "System.Photo.Contrast" or "System.Photo.Saturation" or "System.Photo.Sharpness"=>Enum(new Dictionary<int,string>{{0,"正常"},{1,"较低 / 柔和"},{2,"较高 / 强烈"}}),
            "System.Rating"=>n<=0?null:new string('★',n>=99?5:n>=75?4:n>=50?3:n>=25?2:1),
            _=>F()
        };
    }
}
