using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// The colour and the icon that go with each severity.
    /// </summary>
    /// <remarks>
    /// Deliberately the same reading Raphael.Rider gives severity
    /// (<c>src/screens/main/NotificationsScreen.tsx</c>): red for critical and error,
    /// amber for warning, blue for information. A dispatcher on the phone with a patient
    /// must not be looking at a red badge while the patient looks at a blue one.
    ///
    /// <para>
    /// In one place so the panel, the tab badges and the real-time toast cannot drift into
    /// three different palettes.
    /// </para>
    /// </remarks>
    public static class NotificationSeverityPalette
    {
        // Frozen once and handed out. These are read from a binding on every row of every
        // tab, and every invalidation re-reads them: minting a brush per read would fill
        // gen0 with identical objects for no reason.
        private static readonly Dictionary<string, SolidColorBrush> Foregrounds = [];

        private static readonly Dictionary<string, SolidColorBrush> Backgrounds = [];

        public static SolidColorBrush Foreground(string severity)
        {
            return Get(Foregrounds, Normalize(severity), ForegroundColor);
        }

        public static SolidColorBrush Background(string severity)
        {
            return Get(Backgrounds, Normalize(severity), BackgroundColor);
        }

        private static SolidColorBrush Get(
            Dictionary<string, SolidColorBrush> cache,
            string severity,
            Func<string, Color> colorFor)
        {
            lock (cache)
            {
                if (cache.TryGetValue(severity, out var cached))
                    return cached;

                var brush = new SolidColorBrush(colorFor(severity));
                brush.Freeze();

                cache[severity] = brush;

                return brush;
            }
        }

        public static PackIconKind Icon(string severity)
        {
            return Normalize(severity) switch
            {
                NotificationKeys.Severity.Critical => PackIconKind.AlertOctagonOutline,
                NotificationKeys.Severity.Error => PackIconKind.AlertCircleOutline,
                NotificationKeys.Severity.Warning => PackIconKind.AlertOutline,
                NotificationKeys.Severity.Success => PackIconKind.CheckCircleOutline,
                _ => PackIconKind.InformationOutline
            };
        }

        private static Color ForegroundColor(string severity)
        {
            return Normalize(severity) switch
            {
                NotificationKeys.Severity.Critical => Color.FromRgb(0xD3, 0x2F, 0x2F),
                NotificationKeys.Severity.Error => Color.FromRgb(0xD3, 0x2F, 0x2F),
                NotificationKeys.Severity.Warning => Color.FromRgb(0xB4, 0x7C, 0x00),
                NotificationKeys.Severity.Success => Color.FromRgb(0x2E, 0x7D, 0x32),
                _ => Color.FromRgb(0x1E, 0x69, 0xB4)
            };
        }

        private static Color BackgroundColor(string severity)
        {
            return Normalize(severity) switch
            {
                NotificationKeys.Severity.Critical => Color.FromRgb(0xFE, 0xF2, 0xF2),
                NotificationKeys.Severity.Error => Color.FromRgb(0xFE, 0xF2, 0xF2),
                NotificationKeys.Severity.Warning => Color.FromRgb(0xFE, 0xFC, 0xE8),
                NotificationKeys.Severity.Success => Color.FromRgb(0xF0, 0xFD, 0xF4),
                _ => Color.FromRgb(0xF0, 0xF9, 0xFF)
            };
        }

        /// <summary>
        /// The API sends the enumeration <c>Name</c> ("Critical"), not its <c>Code</c>
        /// ("CRITICAL"). Matching case-insensitively means a change of convention on the
        /// server turns every icon blue instead of throwing, which is the safer failure.
        /// </summary>
        private static string Normalize(string severity)
        {
            if (string.IsNullOrWhiteSpace(severity))
                return NotificationKeys.Severity.Information;

            foreach (var known in new[]
                     {
                         NotificationKeys.Severity.Critical,
                         NotificationKeys.Severity.Error,
                         NotificationKeys.Severity.Warning,
                         NotificationKeys.Severity.Success,
                         NotificationKeys.Severity.Information
                     })
            {
                if (string.Equals(severity, known, StringComparison.OrdinalIgnoreCase))
                    return known;
            }

            return NotificationKeys.Severity.Information;
        }
    }
}
