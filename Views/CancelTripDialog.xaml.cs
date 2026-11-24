using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Raphael.Desktop.Views
{
    /// <summary>RE-091: confirms a cancellation and asks why. It does not close without an answer.</summary>
    public partial class CancelTripDialog : Window
    {
        public sealed record ReasonChoice(string Code, string Label);

        public string ReasonCode { get; private set; } = string.Empty;
        public string? ReasonText { get; private set; }

        private static string T(string key) => LocalizationService.Instance[key];

        public CancelTripDialog(string patient, DateTime date, TimeSpan? time)
        {
            InitializeComponent();
            Title = HeadText.Text = T("cancel.Title");
            TripText.Text = string.Join(" · ", new[] { patient, date.ToString("MM/dd/yyyy"), time?.ToString(@"hh\:mm") }.Where(p => !string.IsNullOrWhiteSpace(p)));
            AskText.Text = T("cancel.Ask");
            KeepButton.Content = T("cancel.Keep");
            CancelTripButton.Content = T("cancel.Do");
            materialDesignHint();
            Reasons.ItemsSource = Cancellations.OfficeReasons.Select(c => new ReasonChoice(c, Cancellations.ReasonLabel(c))).ToList();
            Owner = Application.Current?.MainWindow;

            void materialDesignHint() => MaterialDesignThemes.Wpf.HintAssist.SetHint(OtherBox, T("cancel.OtherHint"));
        }

        /// <summary>Null when the dispatcher keeps the trip.</summary>
        public static (string Code, string? Text)? Ask(string patient, DateTime date, TimeSpan? time)
        {
            var dialog = new CancelTripDialog(patient, date, time);
            return dialog.ShowDialog() == true ? (dialog.ReasonCode, dialog.ReasonText) : null;
        }

        private void OnChosen(object sender, RoutedEventArgs e)
        {
            ReasonCode = (string)((RadioButton)sender).Tag;
            ErrorText.Visibility = Visibility.Collapsed;
            var other = ReasonCode == Cancellations.Other;
            OtherBox.Visibility = other ? Visibility.Visible : Visibility.Collapsed;
            if (other) Dispatcher.BeginInvoke(() => OtherBox.Focus());
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            var text = OtherBox.Text.Trim();
            if (ReasonCode.Length == 0) { Say("cancel.PickOne"); return; }
            if (ReasonCode == Cancellations.Other && text.Length == 0) { Say("cancel.WriteIt"); return; }
            ReasonText = ReasonCode == Cancellations.Other ? text : null;
            DialogResult = true;
        }

        private void Say(string key)
        {
            ErrorText.Text = T(key);
            ErrorText.Visibility = Visibility.Visible;
        }
    }
}
