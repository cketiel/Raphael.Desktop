using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views.Notifications
{
    /// <summary>
    /// Draws a column of live alert cards.
    /// </summary>
    /// <remarks>
    /// The same control is used inside the main window and inside the floating window, so
    /// an alert looks identical wherever it lands.
    /// </remarks>
    public partial class NotificationToastHost : UserControl
    {
        public NotificationToastHost()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Holds the card while the pointer is on it.
        /// </summary>
        /// <remarks>
        /// A notice that disappears halfway through the sentence being read is worse than
        /// no notice: the dispatcher knows something happened and has no way back to it
        /// except hunting through the panel.
        /// </remarks>
        private void Card_MouseEnter(object sender, MouseEventArgs e)
        {
            Item(sender)?.Pause();
        }

        private void Card_MouseLeave(object sender, MouseEventArgs e)
        {
            Item(sender)?.Resume();
        }

        /// <summary>
        /// Opens the notice in the Notification Center.
        /// </summary>
        /// <remarks>
        /// The close button inside the card handles its own click and marks it handled, so
        /// dismissing never opens anything by accident.
        /// </remarks>
        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            var item = Item(sender);

            if (item?.OpenCommand.CanExecute(null) == true)
                item.OpenCommand.Execute(null);
        }

        private static NotificationToastItemViewModel Item(object sender) =>
            (sender as FrameworkElement)?.DataContext as NotificationToastItemViewModel;
    }
}
