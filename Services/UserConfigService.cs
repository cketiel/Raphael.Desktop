using Raphael.Desktop.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Raphael.Desktop.Services
{
    public class UserConfigService
    {
        private readonly string _configFilePath;

        public UserConfigService()
        {
            // Build a safe path to save the configuration file.
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, "RapphaelApp");
            Directory.CreateDirectory(appFolder); // Ensures that the folder exists.
            _configFilePath = Path.Combine(appFolder, "SchedulesGridConfig.json");
        }

        public void SaveColumnConfig(IEnumerable<ColumnConfig> columns)
        {
            var json = JsonSerializer.Serialize(columns, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configFilePath, json);
        }

        public List<ColumnConfig> LoadColumnConfig()
        {
            if (!File.Exists(_configFilePath))
            {
                return null; // There is no configuration, the default will be used.
            }

            try
            {
                var json = File.ReadAllText(_configFilePath);
                return JsonSerializer.Deserialize<List<ColumnConfig>>(json);
            }
            catch (Exception)
            {
                // The file could be corrupt, we return null.
                return null;
            }
        }

        /// <summary>
        /// Keeps any small preference under a name of its own.
        /// </summary>
        /// <remarks>
        /// One file per key rather than one big document: a screen that writes its column layout
        /// cannot then corrupt another screen's saved filters, and a file that will not parse
        /// costs the preference it holds and nothing else.
        ///
        /// ⚠️ Preferences only. Nothing here is backed up, shared between machines or seen by the
        /// server, and nothing carrying patient data belongs in it.
        /// </remarks>
        public void Save<T>(string key, T value)
        {
            try
            {
                File.WriteAllText(
                    PathFor(key),
                    JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception)
            {
                // A preference that cannot be written is not worth interrupting anyone over.
            }
        }

        /// <summary>Reads back what <see cref="Save{T}"/> kept, or default when there is none.</summary>
        public T Load<T>(string key)
        {
            var path = PathFor(key);

            if (!File.Exists(path)) return default;

            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return default;
            }
        }

        public void SaveColumnConfig(string key, IEnumerable<ColumnConfig> columns) => Save(key, columns);

        public List<ColumnConfig> LoadColumnConfig(string key) => Load<List<ColumnConfig>>(key);

        private string PathFor(string key)
        {
            var safe = string.Join("_", key.Split(Path.GetInvalidFileNameChars()));

            return Path.Combine(Path.GetDirectoryName(_configFilePath)!, $"{safe}.json");
        }
    }
}