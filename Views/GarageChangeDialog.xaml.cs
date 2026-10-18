using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.Views
{
    /// <summary>
    /// Shows what a new garage does to the planned days of a route, and asks before doing it (RE-052).
    /// </summary>
    /// <remarks>
    /// Every line is read off the server's answer, never worked out here: the server re-chained each
    /// day exactly as it will when the route is saved, and this only says it. Shown a second time,
    /// with nothing to confirm, when what was applied differs from what was previewed.
    /// </remarks>
    public partial class GarageChangeDialog : Window
    {
        private static readonly Brush CardBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1)));
        private static readonly Brush CardBorder = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x00)));
        private static readonly Brush QuietBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xEC, 0xEF, 0xF1)));
        private static readonly Brush QuietBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x90, 0xA4, 0xAE)));
        private static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F)));

        private static LocalizationService L => LocalizationService.Instance;

        /// <param name="applied">True to show what was saved, after it differed from the preview.</param>
        /// <param name="notice">A warning above everything else, such as the route having moved while it was reviewed.</param>
        public GarageChangeDialog(string routeName, GarageChangePreviewDto change, bool applied, string notice = null)
        {
            InitializeComponent();

            NoticeText.Text = notice;
            NoticeBox.Visibility = string.IsNullOrWhiteSpace(notice) ? Visibility.Collapsed : Visibility.Visible;

            Title = applied ? L["garage.AppliedTitle"] : L["garage.Title"];

            var touched = change.Days.Count(d => !d.DayFinished);
            IntroText.Text = (applied
                    ? L["garage.AppliedIntro"]
                    : string.Format(L["garage.Intro"], routeName, touched))
                + (change.PastDaysUntouched > 0 ? " " + string.Format(L["garage.PastDays"], change.PastDaysUntouched) : string.Empty);

            if (change.Days.Count == 0)
                DaysPanel.Children.Add(Paragraph(L["garage.NoPlannedDays"]));

            foreach (var day in change.Days)
                DaysPanel.Children.Add(DayCard(day));

            FooterText.Text = applied ? L["garage.AppliedFooter"] : L["garage.Footer"];

            OkButton.Content = applied ? L["garage.Close"] : L["garage.Confirm"];
            CancelButton.Content = L["Cancel"];
            CancelButton.Visibility = applied ? Visibility.Collapsed : Visibility.Visible;
        }

        private static UIElement DayCard(GarageChangeDayDto day)
        {
            var quiet = day.DayFinished;
            var panel = new StackPanel();

            var header = (day.IsToday ? L["garage.Today"] + ", " : string.Empty) + day.Date.ToString("D");
            panel.Children.Add(new TextBlock { Text = header, FontWeight = FontWeights.Bold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 4) });

            if (day.DayFinished)
            {
                panel.Children.Add(Paragraph(L["garage.DayFinished"]));
            }
            else
            {
                panel.Children.Add(Paragraph(day.PullOutAlreadyLeft
                    ? string.Format(L["garage.PullOutLeft"], Hour(day.PullOutEtaBefore))
                    : string.Format(L["garage.PullOutChanges"], Hour(day.PullOutEtaBefore), Hour(day.PullOutEtaAfter),
                        Span(day.FirstLegBefore), Span(day.FirstLegAfter))));

                if (day.PullInUpdated)
                    panel.Children.Add(Paragraph(string.Format(L["garage.PullInChanges"], Hour(day.PullInEtaBefore), Hour(day.PullInEtaAfter),
                        Span(day.LastLegBefore), Span(day.LastLegAfter))));

                if (day.ChangedStops.Count == 0)
                {
                    panel.Children.Add(Paragraph(L["garage.NoStopChanges"]));
                }
                else
                {
                    panel.Children.Add(Paragraph(string.Format(L["garage.StopsChange"], day.ChangedStops.Count)));
                    foreach (var stop in day.ChangedStops)
                    {
                        panel.Children.Add(Paragraph("• " + string.Format(L["garage.StopLine"], stop.Name,
                            Hour(stop.EtaBefore), Hour(stop.EtaAfter), Span(stop.WaitBefore), Span(stop.WaitAfter)), 12));
                    }
                }
            }

            return new Border
            {
                Background = quiet ? QuietBackground : CardBackground,
                BorderBrush = quiet ? QuietBorder : CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = panel
            };
        }

        private static TextBlock Paragraph(string text, double indent = 0) =>
            new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Ink, Margin = new Thickness(indent, 2, 0, 0) };

        /// <summary>An hour exactly as the Schedule grid prints it (<c>hh\:mm</c>, cut, not rounded); a dash for none.</summary>
        private static string Hour(TimeSpan? t) => t.HasValue ? t.Value.ToString(@"hh\:mm") : "—";

        /// <summary>A drive or a wait as the dispatcher says it: "45 m", "1 h 05 m"; a dash for none.</summary>
        private static string Span(TimeSpan? t)
        {
            if (!t.HasValue || t.Value <= TimeSpan.Zero) return "—";
            var m = (int)Math.Floor(t.Value.TotalMinutes);
            return m >= 60 ? $"{m / 60} h {m % 60:00} m" : $"{m} m";
        }

        private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;

        private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }
}
