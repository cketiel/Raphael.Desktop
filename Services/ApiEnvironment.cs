using System;
using Raphael.Desktop.Exceptions;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// Which server this installation talks to, and which environment that is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Doctrine: <c>_meta/CLIENT_CONFIG_POLICY.md</c>. Both endpoints are declared and the
    /// selected one is named explicitly, so pointing a machine at DEV is changing a word rather
    /// than finding a URL. When that costs effort, people test against production instead.
    /// </para>
    /// <para>
    /// Before this existed, five places read <c>ApiAddress:ApiTest</c> by string key — a key
    /// whose name said "test" and whose value was the production server.
    /// </para>
    /// </remarks>
    public static class ApiEnvironment
    {
        /// <summary>The environment this installation selected: <c>Prod</c> or <c>Dev</c>.</summary>
        public static string Name { get; private set; } = string.Empty;

        /// <summary>Base address of the API, always with a trailing slash.</summary>
        public static string BaseUrl { get; private set; } = string.Empty;

        /// <summary>
        /// True when this is not production, and therefore when the application must say so on
        /// screen. A machine that quietly stayed on DEV writes real trips into the wrong
        /// database and nothing about it looks broken.
        /// </summary>
        public static bool IsProduction =>
            string.Equals(Name, "Prod", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Reads the configuration once, at startup. Throws rather than guessing: an
        /// application that cannot say which server it is for should not open a window.
        /// </summary>
        public static void Initialise()
        {
            var configured = App.Configuration["Api:Environment"];

            if (!string.IsNullOrWhiteSpace(configured))
            {
                var url = App.Configuration[$"Api:Endpoints:{configured}"];

                if (string.IsNullOrWhiteSpace(url))
                {
                    throw new ApiException(
                        $"appsettings.json selects the '{configured}' environment but declares no " +
                        $"Api:Endpoints:{configured} address.");
                }

                Name = configured;
                BaseUrl = Normalise(url);
                return;
            }

            // ⚠️ Fallback, and it is temporary. appsettings.json is gitignored and lives on each
            // machine, so a workstation updated to this build still has the old file and no
            // "Api" section. Without this it would start with no server at all, which turns a
            // version upgrade into a visit to every desk. Removed one release after every
            // installation has been migrated; the release gate already refuses any artifact
            // that ships without the new section.
            var legacy = App.Configuration["ApiAddress:ApiTest"];

            if (string.IsNullOrWhiteSpace(legacy))
            {
                throw new ApiException(
                    "appsettings.json declares neither Api:Environment nor ApiAddress:ApiTest, " +
                    "so there is no server to talk to.");
            }

            Name = "Legacy";
            BaseUrl = Normalise(legacy);
        }

        /// <summary>
        /// One trailing slash, never two and never none. Callers concatenate <c>"api/"</c> onto
        /// this and <c>new Uri</c> is unforgiving about both.
        /// </summary>
        private static string Normalise(string url) => url.TrimEnd('/') + "/";
    }
}
