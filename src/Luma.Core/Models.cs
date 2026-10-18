namespace Luma.Core;

public sealed record Photo(string Path, string Name, long Size, long Modified, bool Favorite = false, long Opened = 0, bool Missing = false)
{
    public string Detail => Missing ? "文件暂不可用" : $"{Size / 1048576.0:0.0} MB";
    public override string ToString() => Name;
}
public sealed record Album(long Id, string Name, string Folder, string? Cover, int Count, bool Offline, string Sort, bool Pinned = false, int? MatchedCount = null)
{
    public string CountLabel => (MatchedCount is int matched?$"匹配 {matched} / 共 {Count} 张":$"{Count} 张")+(Offline?" · 文件夹离线":"");
    public override string ToString() => Name;
}
public static class AlbumSort
{
    public static string Normalize(string? value) => value is "name-desc" or "newest" or "oldest" or "most" or "least" ? value : "name";
}
public sealed record ScanResult(long AlbumId, int Added, int Updated, int Missing, bool Offline, string? Error = null);
public enum PhotoScope { All, Album, Favorites, Recent }
public static class SupportedFormats
{
    // HEIC/HEIF uses the installed Windows HEIF + HEVC extensions, including
    // Apple/ISO gain maps and WIC floating-point PQ/HLG representations.
    public static readonly string[] Extensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".jxr", ".wdp", ".hdp", ".ico", ".heic", ".heif"];
    private static readonly HashSet<string> Set = new(Extensions, StringComparer.OrdinalIgnoreCase);
    public static bool IsSupported(string path) => !System.IO.Path.GetFileName(path).StartsWith("._",StringComparison.Ordinal) && Set.Contains(System.IO.Path.GetExtension(path));
    public static string Normalize(string path)
    {
        var full=System.IO.Path.GetFullPath(path);
        // C:\ must remain absolute; trimming the root separator would turn it into C:.
        return full.Length==System.IO.Path.GetPathRoot(full)?.Length?full:full.TrimEnd(System.IO.Path.DirectorySeparatorChar,System.IO.Path.AltDirectorySeparatorChar);
    }
}
