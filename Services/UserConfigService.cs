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
    }
}