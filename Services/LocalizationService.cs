using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Raphael.Desktop.Services
{
    public class LocalizationService
    {
        private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
        public static LocalizationService Instance => _instance.Value;

        private Dictionary<string, string> _translations = new();
        private string _currentLanguage = "en";

        public event Action LanguageChanged;

        public string this[string key]
        { 
            get
            {               
                if (_translations.TryGetValue(key, out var value))
                    return value;
                return $"##{key}##"; // Show something visible if missing
            }
        }

        private LocalizationService() { }

        public void LoadLanguage(string language)
        {
            
            try
            {      
                string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets","Languages", $"{language}.json");
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"Language file {filePath} not found.");

                string json = File.ReadAllText(filePath); 
                _translations = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                _currentLanguage = language;
                LanguageChanged?.Invoke();

                // Save language to settings
                Properties.Settings.Default.Language = language;
                Properties.Settings.Default.Save();
            }
            catch (Exception ex)
            {
                // Handle loading error
                System.Diagnostics.Debug.WriteLine($"Failed to load language: {ex.Message}");
            }
        }

        public string CurrentLanguage => _currentLanguage;
    }
}
