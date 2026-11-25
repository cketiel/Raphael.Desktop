using MaterialDesignThemes.Wpf;
using Raphael.Desktop.Services.Help;
using Raphael.Desktop.ViewModels;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Raphael.Desktop.Views.Reports
{
    /// <summary>The explanation of one figure of the report (RE-092). The texts are <c>info.{Key}.*</c>.</summary>
    public partial class InfoDot : UserControl
    {
        public static readonly DependencyProperty KeyProperty = DependencyProperty.Register(nameof(Key), typeof(string), typeof(InfoDot), new PropertyMetadata(null, (d, _) => ((InfoDot)d).Name_()));

        /// <summary>The figure's own numbers in its formula; empty hides them.</summary>
        public static readonly DependencyProperty ExampleProperty = DependencyProperty.Register(nameof(Example), typeof(string), typeof(InfoDot));

        public string? Key { get => (string?)GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
        public string? Example { get => (string?)GetValue(ExampleProperty); set => SetValue(ExampleProperty, value); }

        public InfoDot() => InitializeComponent();

        /// <summary>Where the F1 help tells the rest, and the warning about this figure.</summary>
        private static readonly Dictionary<string, (string? Topic, string? Warning)> Links = new()
        {
            ["t.days"] = ("desktop/reports/productivity", "open_shift"),
            ["t.paid"] = ("desktop/reports/productivity-hours", "outside_allowed_hours"),
            ["t.miles"] = ("desktop/reports/productivity-miles", "pull_in_impossible"),
            ["t.loaded"] = ("desktop/reports/productivity-miles", "incomplete_gps"),
            ["t.trips"] = ("desktop/reports/productivity", null),
            ["t.pay"] = ("desktop/reports/productivity-pay", "open_shift"),
            ["h.shift"] = ("desktop/reports/productivity-hours", "outside_allowed_hours"),
            ["h.real"] = ("desktop/reports/productivity-hours", "long_shift"),
            ["h.unpaid"] = ("desktop/reports/productivity-hours", "outside_allowed_hours"),
            ["h.loaded"] = ("desktop/reports/productivity-hours", "far_from_stop"),
            ["m.odometer"] = ("desktop/reports/productivity-miles", "pull_in_impossible"),
            ["m.gps"] = ("desktop/reports/productivity-miles", "odometer_vs_gps"),
            ["m.loaded"] = ("desktop/reports/productivity-miles", "incomplete_gps"),
            ["m.passenger"] = ("desktop/reports/productivity-pay", "incomplete_gps"),
            ["m.offroute"] = ("desktop/reports/productivity-miles", "off_route_miles"),
            ["r.reliability"] = ("desktop/reports/warnings", "")
        };

        private (string? Topic, string? Warning) Link => Key is null ? default : Links.TryGetValue(Key, out var l) ? l
            : (Key.StartsWith("h.") ? "desktop/reports/productivity-hours" : "desktop/reports/productivity-miles", null);

        private static string T(string key) => WarningLook.T(key);

        // An assistive reader, or anything that drives the screen, knows the dot by its figure.
        private void Name_()
        {
            if (Key is null) return;
            AutomationProperties.SetName(Dot, $"{T("info.Tip")}: {T($"info.{Key}.Title")}");
            AutomationProperties.SetHelpText(Dot, T($"info.{Key}.What"));
            Dot.ToolTip = T("info.Tip");
        }

        private void OnDot(object sender, RoutedEventArgs e)
        {
            if (Key is null) return;
            TitleText.Text = T($"info.{Key}.Title");
            WhatText.Text = T($"info.{Key}.What");
            FormulaLabel.Text = T("info.Formula");
            FormulaText.Text = T($"info.{Key}.Formula");
            PayText.Text = T($"info.{Key}.Pay");

            // Only three figures are what pay is made of; the six groups are where the paid shift went.
            var paid = Key is "t.paid" or "t.pay" or "t.loaded" or "h.shift" or "m.passenger";
            var part = Key is "h.loaded" or "h.inspection" or "h.hourwait" or "h.patientwait" or "h.empty" or "h.other";
            var not = Key is "h.real" or "h.unpaid" or "h.patienthours";
            PayPill.Background = WarningLook.Of(paid ? "#DCF2E3" : part ? "#EDE7F6" : not ? "#FBE3E1" : "#EEF1F5");
            PayText.Foreground = PayIcon.Foreground = WarningLook.Of(paid ? "#1B7F3B" : part ? "#4527A0" : not ? "#B3261E" : "#4B5A69");
            PayIcon.Kind = paid ? PackIconKind.CashCheck : part ? PackIconKind.ChartDonut : not ? PackIconKind.CashRemove : PackIconKind.InformationOutline;

            ExampleBox.Visibility = string.IsNullOrWhiteSpace(Example) ? Visibility.Collapsed : Visibility.Visible;
            ExampleLabel.Text = T(Key.StartsWith("t.") ? "info.Period" : "info.Today");
            ExampleText.Text = Example;

            HelpText.Text = T("info.MoreHelp");
            WarningsText.Text = T("info.Warnings");
            WarningsLink.Visibility = Link.Warning is null ? Visibility.Collapsed : Visibility.Visible;
            e.Handled = true;
            // After the click has run its course: a card opened inside it is closed by the grid taking the mouse.
            Dispatcher.BeginInvoke(new Action(() => Card.IsOpen = true), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void OnClose(object sender, RoutedEventArgs e) => Card.IsOpen = false;

        private void OnHelp(object sender, RoutedEventArgs e)
        {
            Card.IsOpen = false;
            if (Link.Topic is { } topic) HelpService.Instance.Open(topic);
        }

        private void OnWarnings(object sender, RoutedEventArgs e)
        {
            Card.IsOpen = false;
            WarningLegendWindow.ShowLegend(Link.Warning);
        }
    }
}
