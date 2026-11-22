using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Raphael.Desktop.Services.Maps
{
    /// <summary>
    /// Keeps the POWER UP keys in the <c>ProviderKeys</c> section of an <c>appsettings.json</c>.
    /// </summary>
    /// <remarks>
    /// Decision of 2026-11-21: the keys live in appsettings.json, read only by the person who runs
    /// the installation. The installation's own file sits under Program Files, which a normal user
    /// cannot write (the log already shows user.settings refused there), so the keys are written to
    /// <c>%LOCALAPPDATA%\Raphael\appsettings.json</c>: the same format, per Windows user, layered
    /// on top of the installation's file in <see cref="App"/>. A <c>ProviderKeys</c> section typed
    /// by hand into the installation's file is read as well.
    /// ⚠️ Values are never logged and never posted to the map page.
    /// </remarks>
    public sealed class ProviderKeyStore : IProviderKeyStore
    {
        public const string Section = "ProviderKeys";

        public static readonly string UserSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Raphael", "appsettings.json");

        public string? Get(string name)
        {
            var value = App.Configuration?[$"{Section}:{name}"];
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public IDictionary<string, bool> Status(IEnumerable<string> names) =>
            names.Distinct().ToDictionary(n => n, n => Get(n) is not null);

        public void Save(IDictionary<string, string?> keys)
        {
            var root = File.Exists(UserSettingsPath)
                ? JsonNode.Parse(File.ReadAllText(UserSettingsPath)) as JsonObject ?? new JsonObject()
                : new JsonObject();
            if (root[Section] is not JsonObject section)
            {
                section = new JsonObject();
                root[Section] = section;
            }

            foreach (var (name, value) in keys)
            {
                // Only names a key may have: letters, digits and underscores.
                if (string.IsNullOrWhiteSpace(value) || !name.All(c => char.IsLetterOrDigit(c) || c == '_')) continue;
                section[name] = value.Trim();
            }

            Directory.CreateDirectory(Path.GetDirectoryName(UserSettingsPath)!);
            var temp = UserSettingsPath + ".tmp";
            File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, UserSettingsPath, overwrite: true);

            // The configuration watches the file, but a reload can lag; ask for it now.
            (App.Configuration as Microsoft.Extensions.Configuration.IConfigurationRoot)?.Reload();
        }
    }
}
