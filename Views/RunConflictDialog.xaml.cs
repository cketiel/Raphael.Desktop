using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.Views
{
    /// <summary>What the dispatcher chose in <see cref="RunConflictDialog"/>.</summary>
    public enum RunConflictChoice
    {
        /// <summary>Back to the editor, to pick another driver or login.</summary>
        Back,

        /// <summary>Leave this one and edit the route in the way instead.</summary>
        OpenOther,

        /// <summary>End the route in the way on <see cref="RunConflictDialog.EndDate"/>, then save this one.</summary>
        EndOther,

        /// <summary>Save this route for today only (a vehicle broken down); the other goes on tomorrow.</summary>
        CoverToday,
    }

    /// <summary>
    /// The route being saved clashes with another from today on (RE-063). Says which and why, and offers
    /// what fixes it: use that route instead, or end it the day before this one starts.
    /// </summary>
    /// <remarks>
    /// Ending is only offered when the server says it can be done: never over trips already routed
    /// after that day, which would reach no driver's phone. Those are counted instead, with the first
    /// day, so the dispatcher knows where to move them.
    /// </remarks>
    public partial class RunConflictDialog : Window
    {
        private static readonly Brush CardBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1)));
        private static readonly Brush CardBorder = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x00)));
        private static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F)));
        private static readonly Brush Danger = Freeze(new SolidColorBrush(Color.FromRgb(0xB4, 0x23, 0x18)));

        private static LocalizationService L => LocalizationService.Instance;

        public RunConflictChoice Choice { get; private set; } = RunConflictChoice.Back;

        /// <summary>The route the choice is about.</summary>
        public RunConflictDto Target { get; private set; }

        /// <summary>With <see cref="RunConflictChoice.EndOther"/>: the last day the route in the way stays in service.</summary>
        public DateTime EndDate { get; private set; }

        public RunConflictDialog(string driverName, IReadOnlyList<RunConflictDto> conflicts)
        {
            InitializeComponent();
            Title = L["runConflict.Title"];
            IntroText.Text = L["runConflict.Intro"];
            BackButton.Content = L["runConflict.Back"];

            foreach (var conflict in conflicts)
                ConflictsPanel.Children.Add(Card(driverName, conflict));
        }

        private UIElement Card(string driverName, RunConflictDto c)
        {
            var stack = new StackPanel();

            var until = c.ToDate is { } to ? string.Format(L["runConflict.Until"], to) : L["runConflict.NoEnd"];
            var headline = c.Kind == "login"
                ? string.Format(L["runConflict.LoginTaken"], c.RouteName, c.SmartphoneLogin, c.DriverName, until)
                : string.Format(L["runConflict.DriverTaken"], c.DriverName ?? driverName, c.RouteName, c.VehicleName, c.FromDate, until);
            stack.Children.Add(new TextBlock { Text = headline, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Foreground = Ink });
            stack.Children.Add(new TextBlock
            {
                Text = c.Kind == "login" ? L["runConflict.LoginRule"] : L["runConflict.DriverRule"],
                TextWrapping = TextWrapping.Wrap, Foreground = Ink, Margin = new Thickness(0, 4, 0, 10),
            });

            var buttons = new WrapPanel();

            var open = new Button { Content = string.Format(L["runConflict.Open"], c.RouteName), Margin = new Thickness(0, 0, 8, 6) };
            open.SetResourceReference(StyleProperty, "MaterialDesignOutlinedButton");
            open.Click += (_, _) => Close(RunConflictChoice.OpenOther, c);
            buttons.Children.Add(open);

            var end = new Button { Content = string.Format(L["runConflict.End"], c.RouteName, c.SuggestedEndDate), Margin = new Thickness(0, 0, 8, 6), IsEnabled = c.CanEnd };
            end.SetResourceReference(StyleProperty, "MaterialDesignRaisedButton");
            end.Click += (_, _) => ConfirmEnd(c, c.SuggestedEndDate, L["runConflict.EndConfirm"]);
            buttons.Children.Add(end);
            stack.Children.Add(buttons);

            // A vehicle broken down after the Pull-out (user, 2026-11-01): the driver goes on today in
            // another route. Two ways, both for today only.
            if (c.CanEndToday || c.CanCoverToday)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = L["runConflict.HandoverIntro"], TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold,
                    Foreground = Ink, Margin = new Thickness(0, 10, 0, 6),
                });
                var handover = new WrapPanel();
                if (c.CanEndToday)
                {
                    var endToday = new Button { Content = string.Format(L["runConflict.EndToday"], c.RouteName), Margin = new Thickness(0, 0, 8, 6) };
                    endToday.SetResourceReference(StyleProperty, "MaterialDesignOutlinedButton");
                    // The server's business day, not this PC's: the new route starts today, so the
                    // suggested end is yesterday.
                    endToday.Click += (_, _) => ConfirmEnd(c, c.SuggestedEndDate.AddDays(1), L["runConflict.EndTodayConfirm"]);
                    handover.Children.Add(endToday);
                }
                if (c.CanCoverToday)
                {
                    var cover = new Button { Content = L["runConflict.CoverToday"], Margin = new Thickness(0, 0, 8, 6) };
                    cover.SetResourceReference(StyleProperty, "MaterialDesignOutlinedButton");
                    cover.Click += (_, _) => Close(RunConflictChoice.CoverToday, c);
                    handover.Children.Add(cover);
                }
                stack.Children.Add(handover);
                if (c.PendingTripStopsToday > 0)
                    stack.Children.Add(new TextBlock
                    {
                        Text = string.Format(L["runConflict.PendingToday"], c.PendingTripStopsToday, c.RouteName),
                        TextWrapping = TextWrapping.Wrap, Foreground = Ink, Margin = new Thickness(0, 2, 0, 0),
                    });
            }

            if (!c.CanEnd)
            {
                var why = c.TripStopsAfterSuggestedEnd > 0
                    ? string.Format(L["runConflict.CannotEndTrips"], c.TripStopsAfterSuggestedEnd, c.SuggestedEndDate, c.FirstTripStopAfterSuggestedEnd)
                    : string.Format(L["runConflict.CannotEndFuture"], c.FromDate);
                stack.Children.Add(new TextBlock { Text = why, TextWrapping = TextWrapping.Wrap, Foreground = Danger, Margin = new Thickness(0, 2, 0, 0) });
            }

            return new Border
            {
                Child = stack, Background = CardBackground, BorderBrush = CardBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 10),
            };
        }

        private void ConfirmEnd(RunConflictDto c, DateTime day, string question)
        {
            var sure = MessageBox.Show(this, string.Format(question, c.RouteName, day), L["runConflict.Title"],
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (sure != MessageBoxResult.Yes) return;
            EndDate = day;
            Close(RunConflictChoice.EndOther, c);
        }

        private void Close(RunConflictChoice choice, RunConflictDto target)
        {
            Choice = choice;
            Target = target;
            DialogResult = true;
        }

        private void OnBack(object sender, RoutedEventArgs e) => DialogResult = false;

        private static Brush Freeze(Brush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
