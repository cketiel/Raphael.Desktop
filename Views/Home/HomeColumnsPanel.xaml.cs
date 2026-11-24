using Raphael.Desktop.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Raphael.Desktop.Views.Home
{
    /// <summary>RE-089: the drag of a column by its handle. Everything else is in the view model.</summary>
    public partial class HomeColumnsPanel : UserControl
    {
        private HomeColumnItem? _moving;

        public HomeColumnsPanel() => InitializeComponent();

        private HomeColumnsViewModel? Columns => DataContext as HomeColumnsViewModel;

        private void Handle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (Columns?.CanDrag != true || (sender as FrameworkElement)?.DataContext is not HomeColumnItem item) return;
            _moving = item;
            item.IsMoving = true;
            // On the list, not on the handle: its row is moved from under the mouse on the way.
            Mouse.Capture(List, CaptureMode.SubTree);
            e.Handled = true;
        }

        private void List_MouseMove(object sender, MouseEventArgs e)
        {
            if (_moving is null || Columns is not { } columns) return;

            var y = e.GetPosition(List).Y;
            if (Scroller() is { } scroller)
            {
                if (y < 14) scroller.LineUp();
                else if (y > List.ActualHeight - 14) scroller.LineDown();
            }

            for (var i = 0; i < columns.Items.Count; i++)
            {
                if (List.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement row || row.ActualHeight == 0) continue;
                var at = e.GetPosition(row).Y;
                if (at < 0 || at >= row.ActualHeight) continue;
                if (!ReferenceEquals(columns.Items[i], _moving)) columns.Move(_moving, i);
                break;
            }
        }

        private ScrollViewer? Scroller()
        {
            DependencyObject? d = List;
            while (d is not null and not ScrollViewer)
                d = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d) > 0 ? System.Windows.Media.VisualTreeHelper.GetChild(d, 0) : null;
            return d as ScrollViewer;
        }

        private void List_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => Drop(release: true);
        private void List_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (!ReferenceEquals(Mouse.Captured, List)) Drop(release: false);
        }

        private void Drop(bool release)
        {
            if (_moving is null) return;
            _moving.IsMoving = false;
            _moving = null;
            if (release) List.ReleaseMouseCapture();
        }
    }
}
