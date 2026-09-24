using System.Windows;
using System.Windows.Controls;

namespace Raphael.Desktop.Views.Notifications
{
    public partial class CallRequestsPanel : UserControl
    {
        public CallRequestsPanel()
        {
            InitializeComponent();
        }

        /// <summary>A request opened from outside — an alert, the header counter — is scrolled into view.</summary>
        /// <remarks>
        /// The selection itself is bound to the view model, one list to one property. There used to
        /// be two lists kept in step here by hand, and keeping them in step is what lost the
        /// selection when a request was released.
        /// </remarks>
        private void QueueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (QueueList.SelectedItem is { } selected)
                QueueList.ScrollIntoView(selected);
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string topic } && !string.IsNullOrWhiteSpace(topic))
                Services.Help.HelpService.Instance.Open(topic);
        }
    }
}
