using System.Collections.Generic;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels.LiveMap
{
    /// <summary>
    /// The live map's colors (RE-080). Color says the group and nothing else: motion is the
    /// shape, speeding is a ring, an old fix is faded. Two meanings on one channel would make a red
    /// vehicle either "fast" or "out of service".
    /// </summary>
    public static class LiveMapPalette
    {
        /// <summary>Order of the filters and the legend: what runs today first.</summary>
        public static readonly IReadOnlyList<string> Order = new[]
        {
            LiveMapGroups.OnRoute,
            LiveMapGroups.Idle,
            LiveMapGroups.Suspended,
            LiveMapGroups.OutOfService,
            LiveMapGroups.Expired
        };

        public static readonly Brush SpeedingRing = Frozen("#D93B3B");

        /// <summary>
        /// One colour per line for its route (RE-080), so ten lines on the map are ten roads you can
        /// tell apart. No purples, reds or greens: those are the Dispatch stop colours (purple arrived,
        /// red drop-off, green pickup), and a route must never read as a stop's state. The road already
        /// driven is gray, whatever the line.
        /// </summary>
        public static readonly IReadOnlyList<string> RouteColors = new[]
        {
            "#2563EB", "#0891B2", "#EA580C", "#0F766E", "#B45309", "#0284C7", "#CA8A04", "#DB2777", "#1E3A8A", "#0E7490"
        };

        private static readonly Dictionary<string, Brush> Brushes = new()
        {
            [LiveMapGroups.OnRoute] = Frozen("#1F6FD1"),
            [LiveMapGroups.Idle] = Frozen("#169C8A"),
            [LiveMapGroups.Suspended] = Frozen("#D98A12"),
            [LiveMapGroups.OutOfService] = Frozen("#C2410C"),
            [LiveMapGroups.Expired] = Frozen("#8A96A3")
        };

        public static Brush For(string group) =>
            group is not null && Brushes.TryGetValue(group, out var brush) ? brush : Brushes[LiveMapGroups.Expired];

        public static string NameOf(string group) => LocalizationService.Instance[$"livemap.Group.{group}"];

        private static Brush Frozen(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>One group in the filter panel: its swatch, name, count and on/off.</summary>
    public partial class LiveMapGroupFilter : ObservableObject
    {
        public LiveMapGroupFilter(string group, bool isOn)
        {
            Group = group;
            _isOn = isOn;
        }

        public string Group { get; }

        public Brush Brush => LiveMapPalette.For(Group);

        public string Name => LiveMapPalette.NameOf(Group);

        [ObservableProperty]
        private bool _isOn;

        [ObservableProperty]
        private int _count;

        public void RefreshText() => OnPropertyChanged(nameof(Name));
    }
}
