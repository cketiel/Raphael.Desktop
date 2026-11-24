using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels.LiveMap
{
    /// <summary>One part of the day: its arc of the ring and its row under it.</summary>
    public record LiveMapDayPart(TripPart Part, string Label, int Count, Brush Brush, Geometry Arc, string ToolTip, string Icon)
    {
        public string CountText => Count.ToString(CultureInfo.CurrentCulture);
        public bool HasTrips => Count > 0;
    }

    /// <summary>A trip under way, in the In progress list.</summary>
    public record LiveMapInProgressRow(
        int TripId, string TripNumber, string Patient, string Phase, string Times, string Line, string LateText, Brush LateBrush, int? VehicleRouteId);

    /// <summary>
    /// The "Today" panel (RE-085): the day as a ring of five parts that add up, with the share done in
    /// the middle, and one row per part that takes to its trips. Home's rule, so both screens agree.
    /// No money, as in Home.
    /// </summary>
    public partial class LiveMapDayViewModel : ObservableObject
    {
        /// <summary>The ring's box and the radius of its middle line, in pixels.</summary>
        private const double Box = 150, Radius = 62;

        private static readonly (TripPart Part, string Key, string Color, string Icon)[] Parts =
        {
            (TripPart.Done, "livemap.DayCompleted", "#2FB37A", "HomeOutline"),
            (TripPart.InProgress, "livemap.DayInProgress", "#3FA9F5", "FormatListBulleted"),
            (TripPart.NotStarted, "livemap.DayNotStarted", "#8E7CF0", "HomeOutline"),
            (TripPart.Canceled, "livemap.DayCanceled", "#7C8794", "HomeOutline"),
            (TripPart.NoRun, "livemap.DayUnrouted", "#F0A63A", "TableClock"),
        };

        private LiveMapDaySummaryDto _day = new();

        public ObservableCollection<LiveMapDayPart> Rows { get; } = new();

        /// <summary>How much of the day that can be driven is done, 0..1: the cancelled ones do not count.</summary>
        public double DoneRatio => _day.Trips == 0 ? 0 : (double)_day.Completed / Math.Max(1, _day.Trips - _day.Canceled);

        public string DoneText => $"{Math.Round(DoneRatio * 100)}%";
        public string TripsText => string.Format(LocalizationService.Instance["livemap.DayTripsCount"], _day.Trips);
        public string DoneToolTip => string.Format(LocalizationService.Instance["livemap.DayDone"], Math.Round(DoneRatio * 100));

        /// <summary>Set by the map: the day the figures are of.</summary>
        public DateTime Date { get; set; } = DateTime.Today;

        public void Apply(LiveMapDaySummaryDto day)
        {
            _day = day ?? new LiveMapDaySummaryDto();
            Refresh();
        }

        public void Refresh()
        {
            int CountOf(TripPart p) => p switch
            {
                TripPart.Done => _day.Completed,
                TripPart.InProgress => _day.InProgress,
                TripPart.NotStarted => _day.NotStarted,
                TripPart.Canceled => _day.Canceled,
                _ => _day.Unrouted
            };

            var total = Math.Max(1, Parts.Sum(p => CountOf(p.Part)));
            var at = 0.0;
            Rows.Clear();
            foreach (var p in Parts)
            {
                var n = CountOf(p.Part);
                var share = (double)n / total;
                Rows.Add(new LiveMapDayPart(p.Part, LocalizationService.Instance[p.Key], n, Frozen(p.Color), Arc(at, share),
                    LocalizationService.Instance["livemap.DayTip" + p.Part], p.Icon));
                at += share;
            }

            foreach (var n in new[] { nameof(DoneRatio), nameof(DoneText), nameof(TripsText), nameof(DoneToolTip) }) OnPropertyChanged(n);
        }

        /// <summary>Raised when the In progress row is pressed: the list is the map's, next to its vehicles.</summary>
        public event Action? InProgressRequested;

        public void AskInProgress() => InProgressRequested?.Invoke();

        [RelayCommand]
        private void Open(LiveMapDayPart? row)
        {
            if (row is not null) OpenPart(row.Part);
        }

        public void OpenPart(TripPart part)
        {
            switch (part)
            {
                case TripPart.InProgress: InProgressRequested?.Invoke(); break;
                case TripPart.NoRun: AppNavigation.ShowUnscheduled(Date, Array.Empty<int>()); break;
                default: AppNavigation.ShowPartInHome(Date, part); break;
            }
        }

        /// <summary>A part of the ring, from the top clockwise, with a small gap so neighbours do not touch.</summary>
        private static Geometry Arc(double from, double share)
        {
            if (share <= 0) return Geometry.Empty;
            var gap = share > 0.02 ? 0.004 : 0;
            double a0 = (from + gap) * 2 * Math.PI, a1 = (from + share - gap) * 2 * Math.PI;
            Point At(double a) => new(Box / 2 + Radius * Math.Sin(a), Box / 2 - Radius * Math.Cos(a));

            if (share >= 0.999)
            {
                var full = new EllipseGeometry(new Point(Box / 2, Box / 2), Radius, Radius);
                full.Freeze();
                return full;
            }

            var figure = new PathFigure { StartPoint = At(a0), IsFilled = false };
            figure.Segments.Add(new ArcSegment(At(a1), new Size(Radius, Radius), 0, a1 - a0 > Math.PI, SweepDirection.Clockwise, true));
            var g = new PathGeometry(new[] { figure });
            g.Freeze();
            return g;
        }

        private static Brush Frozen(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }
    }
}
