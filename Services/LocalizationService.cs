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

        /// <summary>
        /// Looks up a key without inventing a placeholder when it is missing.
        /// </summary>
        /// <remarks>
        /// The indexer returns "##key##" so a forgotten label is visible while developing.
        /// That is the wrong answer for text that has a real fallback: a notification whose
        /// translation is missing must show the English the server already sent, not
        /// "##notification.TRIP_CANCELLED.DESKTOP_USER.body##" to a dispatcher.
        /// </remarks>
        public bool TryGetValue(string key, out string value)
        {
            if (!string.IsNullOrWhiteSpace(key) &&
                _translations != null &&
                _translations.TryGetValue(key, out var found) &&
                !string.IsNullOrWhiteSpace(found))
            {
                value = found;
                return true;
            }

            value = null;
            return false;
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
