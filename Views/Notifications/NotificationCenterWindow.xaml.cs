using System.Windows;
using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Views.Notifications
{
    /// <summary>
    /// Holds the Notification Center when a dispatcher pulls it out of the tab strip.
    /// </summary>
    /// <remarks>
    /// Owned by the main window but deliberately not modal, and with its own taskbar
    /// entry: the point of pulling it out is to leave it on a second monitor while
    /// working in Dispatch on the first.
    /// </remarks>
    public partial class NotificationCenterWindow : Window
    {
        /// <summary>
        /// Raised when the window is closed, so the host can put the panel back in a tab
        /// instead of losing it.
        /// </summary>
        public event EventHandler ReturnRequested;

        public NotificationCenterWindow(NotificationCenterView panel, Window owner)
        {
            InitializeComponent();

            Title = VersionHelper.WindowTitle;

            Content = panel;
            Owner = owner;

            Closed += (_, _) =>
            {
                // Detach before handing it back: a UserControl cannot have two parents.
                Content = null;

                ReturnRequested?.Invoke(this, EventArgs.Empty);
            };
        }
    }
}
