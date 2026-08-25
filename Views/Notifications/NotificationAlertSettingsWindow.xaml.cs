using System.Windows;
using Raphael.Desktop.Services;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Notifications
{
    /// <summary>
    /// What this dispatcher wants the live alerts to do.
    /// </summary>
    /// <remarks>
    /// Deliberately outside the administration area: it is a preference about this screen
    /// and this shift, so every dispatcher reaches it, not only role 1. Saved on this
    /// machine per user, next to the read state.
    /// </remarks>
    public partial class NotificationAlertSettingsWindow : Window
    {
        private static readonly TimeSpan MuteSpan = TimeSpan.FromMinutes(15);

        private readonly NotificationToastViewModel _toasts;

        private bool _loading = true;

        public NotificationAlertSettingsWindow(NotificationToastViewModel toasts)
        {
            InitializeComponent();

            _toasts = toasts;

            Title = LocalizationService.Instance["NotificationAlertsTitle"];
            TitleText.Text = Title;
            IntroText.Text = LocalizationService.Instance["NotificationAlertsIntro"];
            ActionSoundText.Text = LocalizationService.Instance["NotificationAlertsSoundAction"];
            AttentionSoundText.Text = LocalizationService.Instance["NotificationAlertsSoundAttention"];
            AmbientSoundText.Text = LocalizationService.Instance["NotificationAlertsSoundAmbient"];
            MuteButton.Content = LocalizationService.Instance["NotificationAlertsMute"];
            UnmuteButton.Content = LocalizationService.Instance["NotificationAlertsUnmute"];
            MuteNoteText.Text = LocalizationService.Instance["NotificationAlertsMuteNote"];
            CloseButton.Content = LocalizationService.Instance["NotificationToastClose"];

            ActionSound.IsChecked = _toasts.Preferences.SoundOnActionRequired;
            AttentionSound.IsChecked = _toasts.Preferences.SoundOnAttention;
            AmbientSound.IsChecked = _toasts.Preferences.SoundOnAmbient;

            _loading = false;

            RefreshMuteState();
        }

        private void Preference_Changed(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;

            _toasts.Preferences.SoundOnActionRequired = ActionSound.IsChecked == true;
            _toasts.Preferences.SoundOnAttention = AttentionSound.IsChecked == true;
            _toasts.Preferences.SoundOnAmbient = AmbientSound.IsChecked == true;

            _toasts.Preferences.Save();
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            _toasts.Mute(MuteSpan);

            RefreshMuteState();
        }

        private void Unmute_Click(object sender, RoutedEventArgs e)
        {
            _toasts.Preferences.Unmute();

            RefreshMuteState();
        }

        private void RefreshMuteState()
        {
            var muted = _toasts.Preferences.IsMuted;

            MuteButton.Visibility = muted ? Visibility.Collapsed : Visibility.Visible;
            UnmuteButton.Visibility = muted ? Visibility.Visible : Visibility.Collapsed;

            if (!muted)
            {
                MutedUntilText.Visibility = Visibility.Collapsed;
                return;
            }

            // The dispatcher's own clock: the moment is stored in UTC.
            var until = DateTime
                .SpecifyKind(_toasts.Preferences.MutedUntilUtc.Value, DateTimeKind.Utc)
                .ToLocalTime();

            MutedUntilText.Text = string.Format(
                LocalizationService.Instance["NotificationAlertsMutedUntil"],
                until.ToString("t"));

            MutedUntilText.Visibility = Visibility.Visible;
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
