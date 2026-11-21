using System;
using System.Windows;
using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Views.LiveMap
{
    /// <summary>
    /// Holds the live map when it is pulled out of the tab strip (RE-080), the same way the
    /// Notification Center does: not modal, its own taskbar entry, for a second monitor.
    /// </summary>
    public partial class LiveMapWindow : Window
    {
        /// <summary>Raised on close, so the host puts the map back in a tab instead of losing it.</summary>
        public event EventHandler ReturnRequested;

        public LiveMapWindow(LiveMapView view, Window owner)
        {
            InitializeComponent();

            Title = VersionHelper.WindowTitle;
            Content = view;
            Owner = owner;

            Closed += (_, _) =>
            {
                // A UserControl cannot have two parents.
                Content = null;
                ReturnRequested?.Invoke(this, EventArgs.Empty);
            };
        }
    }
}
