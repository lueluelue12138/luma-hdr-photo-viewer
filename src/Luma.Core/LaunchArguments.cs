namespace Luma.Core;

public static class LaunchArguments
{
    // Selecting an activation path must not touch disk before a window exists.
    // Decode reports missing/unreadable files in the viewer itself.
    public static string? PhotoPath(IReadOnlyList<string> arguments)
    {
        for(int i=0;i<arguments.Count;i++)
        {
            if(arguments[i] is "--test-library" or "--import"){i++;continue;}
            if(!arguments[i].StartsWith("--",StringComparison.Ordinal)&&SupportedFormats.IsSupported(arguments[i]))return arguments[i];
        }
        return null;
    }
}
