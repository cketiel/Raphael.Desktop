using System.IO;
using System.Reflection;

namespace Raphael.Desktop.Helpers;

public static class VersionHelper
{
    private static readonly Assembly Assembly = Assembly.GetExecutingAssembly();

    /// <summary>
    /// Returns the application version (Major.Minor.Build).
    /// Example: 1.2.0
    /// </summary>

    public static string Version
    {
        get
        {
            var version = Assembly.GetName().Version;

            if (version == null)
                return "Unknown";

            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public static string WindowTitle =>
        $"Raphael Desktop v{Version}";

    public static string Build =>File.GetLastWriteTime(Assembly.Location).ToString("yyyy.MM.dd");


    /*public static string Version =>
        Assembly
            .GetExecutingAssembly()
            .GetName()
            .Version?
            .ToString(3)
        ?? "Unknown";

    public static string FullVersion =>
        Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? Version;*/
}