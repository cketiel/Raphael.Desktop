using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Raphael.Desktop.Services.Notifications;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Notifications
{
    /// <summary>
    /// The Notification Center. Lives inside a tab or inside its own window.
    /// </summary>
    /// <remarks>
    /// Being a UserControl rather than a Window is what lets it move between the two
    /// without losing its state: the very same instance is handed from the tab to the
    /// window and back, so the selection, the scroll position and the loaded trip survive
    /// the move.
    /// </remarks>
    public partial class NotificationCenterView : UserControl
    {
        public NotificationCenterViewModel ViewModel { get; }

        public NotificationCenterView(
            INotificationService notifications,
            NotificationTextService text)
        {
            InitializeComponent();

            ViewModel = new NotificationCenterViewModel(notifications, text);

            DataContext = ViewModel;
        }

        /// <summary>
        /// Opens the notice, the way a double-click opens a message in a mail client.
        /// </summary>
        /// <remarks>
        /// Deliberately not a single click. A dispatcher scrolling a list with the arrow
        /// keys or clicking to reach the row actions is not asking to read anything, and
        /// opening on every touch would make the list unusable.
        /// </remarks>
        private void InboxList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // A double-click that landed on one of the row's buttons already did its job.
            if (e.OriginalSource is DependencyObject source && IsInsideButton(source))
                return;

            Open();
        }

        /// <summary>Enter opens, the same key a mail client uses.</summary>
        private void InboxList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            Open();

            e.Handled = true;
        }

        private void Open()
        {
            if (InboxList.SelectedItem is not NotificationItemViewModel item)
                return;

            if (ViewModel.OpenReadingCommand.CanExecute(item))
                ViewModel.OpenReadingCommand.Execute(item);
        }

        private static bool IsInsideButton(DependencyObject source)
        {
            for (var node = source; node is not null; node = VisualTreeHelperParent(node))
            {
                if (node is ButtonBase)
                    return true;
            }

            return false;
        }

        private static DependencyObject VisualTreeHelperParent(DependencyObject node)
        {
            return node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                : null;
        }
    }
}
