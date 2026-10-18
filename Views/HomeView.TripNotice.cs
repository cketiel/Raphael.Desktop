using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views
{
    /// <summary>
    /// The floating notice over the trip form (RE-051): where it opens, how it is dragged, and the
    /// lock chip it closes down to.
    /// </summary>
    /// <remarks>
    /// ⚠️ It lives on a Canvas laid over HomeRoot, never inside the form. The form is sized to what
    /// the divider leaves; a banner inside it pushed the tabs and the calendar out of sight. A
    /// Popup was not used either: it is a window of its own, stays over other applications and is
    /// left behind when the main window moves.
    ///
    /// ⚠️ The map is a WebView2, a native window, and WPF cannot draw over it: a card dragged onto
    /// the map would vanish under it. Every position is pushed back off the map.
    /// </remarks>
    public partial class HomeView
    {
        private const double TripNoticeMinWidth = 320;

        /// <summary>The dispatcher moved the card: it stays where they left it until the next trip.</summary>
        private bool _tripNoticeMoved;

        private void InitTripNotice()
        {
            ViewModel.PropertyChanged += TripNotice_ViewModelChanged;
            TripTabs.SizeChanged += (_, _) => FollowTripForm();
            HomeRoot.SizeChanged += (_, _) => FollowTripForm();
        }

        private void TripNotice_ViewModelChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(HomeViewModel.TripBeingEdited):
                    _tripNoticeMoved = false;
                    goto case nameof(HomeViewModel.ShowTripNotice);
                case nameof(HomeViewModel.ShowTripNotice):
                case nameof(HomeViewModel.ShowTripNoticeChip):
                case nameof(HomeViewModel.IsTripFormOpen):
                    // After the layout pass: the card and the chip measure nothing while collapsed.
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FollowTripForm));
                    break;
            }
        }

        /// <summary>Puts the card over the top of the form, where the banners used to be, unless it was moved.</summary>
        private void FollowTripForm()
        {
            if (!TripTabs.IsVisible) return;

            var form = TripTabs.TransformToAncestor(HomeRoot).TransformBounds(new Rect(TripTabs.RenderSize));

            if (TripNoticeCard.IsVisible)
            {
                if (!_tripNoticeMoved)
                {
                    TripNoticeCard.Width = Math.Max(TripNoticeMinWidth, form.Width);
                    TripNoticeCard.UpdateLayout();
                    // Under the tab headers, so the dispatcher can still change tab with it open.
                    PlaceTripNotice(form.Left, form.Top + 36);
                }
                else
                {
                    PlaceTripNotice(Canvas.GetLeft(TripNoticeCard), Canvas.GetTop(TripNoticeCard));
                }
            }

            if (TripNoticeChip.IsVisible)
            {
                TripNoticeChip.UpdateLayout();
                Canvas.SetLeft(TripNoticeChip, Math.Max(form.Left, form.Right - TripNoticeChip.ActualWidth - 4));
                Canvas.SetTop(TripNoticeChip, form.Top + 4);
            }
        }

        /// <summary>A locked calendar takes no keys either: arrows would move the trip's date.</summary>
        private void ForDateCalendar_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (ViewModel.IsCalendarLocked && e.Key != System.Windows.Input.Key.Escape) e.Handled = true;
        }

        private void TripNoticeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            _tripNoticeMoved = true;
            PlaceTripNotice(Canvas.GetLeft(TripNoticeCard) + e.HorizontalChange, Canvas.GetTop(TripNoticeCard) + e.VerticalChange);
        }

        /// <summary>Inside Home, and off the map.</summary>
        private void PlaceTripNotice(double left, double top)
        {
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            var width = TripNoticeCard.ActualWidth > 0 ? TripNoticeCard.ActualWidth : TripNoticeCard.Width;
            var height = TripNoticeCard.ActualHeight;

            left = Math.Max(0, Math.Min(left, HomeRoot.ActualWidth - width));
            top = Math.Max(0, Math.Min(top, HomeRoot.ActualHeight - height));

            if (MapaWebView.IsVisible && MapaWebView.ActualWidth > 0)
            {
                var map = MapaWebView.TransformToAncestor(HomeRoot).TransformBounds(new Rect(MapaWebView.RenderSize));
                var card = new Rect(left, top, width, height);
                if (card.IntersectsWith(map))
                {
                    // Whichever way out is shorter: to the left of the map, or below it.
                    var toLeft = Math.Max(0, map.Left - width);
                    var below = map.Bottom;
                    if (left - toLeft <= below - top || below + height > HomeRoot.ActualHeight) left = toLeft;
                    else top = below;
                }
            }

            Canvas.SetLeft(TripNoticeCard, left);
            Canvas.SetTop(TripNoticeCard, top);
        }
    }
}
