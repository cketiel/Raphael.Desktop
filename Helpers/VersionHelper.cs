using System.Linq;
using System.Reflection;

namespace Raphael.Desktop.Helpers;

public static class VersionHelper
{
    /// <summary>Metadata key the project file stamps the build date under.</summary>
    private const string BuildDateKey = "BuildDate";

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

    /// <summary>
    /// The day this build was compiled, as the project file stamped it. "—" if absent.
    /// </summary>
    /// <remarks>
    /// ⚠️ Deliberately not the file's timestamp on disk. That is rewritten by copying the
    /// folder, so a dispatcher running a month-old build saw the day it was copied onto
    /// their machine and read it as the day it was made — a build date that lies is worse
    /// than none, because it is believed.
    ///
    /// <para>
    /// It also read <c>Assembly.Location</c>, which a single-file publish returns as an
    /// empty string. <c>File.GetLastWriteTime("")</c> throws, and it threw inside the login
    /// window's constructor: the application would have started with no way in.
    /// </para>
    /// </remarks>
    public static string Build =>
        Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == BuildDateKey)
            ?.Value
        ?? "—";


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