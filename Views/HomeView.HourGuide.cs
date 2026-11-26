using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views
{
    /// <summary>
    /// RE-094: where the guide of the hours sits. On the notice layer, like the RE-051 notice, so the
    /// form keeps its room: the chip just above the top right of the form, clear of the tab headers, the card over its left column,
    /// clear of the hours being typed.
    /// </summary>
    public partial class HomeView
    {
        private void InitHourGuide()
        {
            ViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(HomeViewModel.ShowTripNoticeChip) or nameof(HomeViewModel.IsTripFormOpen))
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PlaceHourGuide));
            };
            ViewModel.HourGuide.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(Helpers.HourGuideModel.HasHourGuide) or nameof(Helpers.HourGuideModel.IsHourGuideOpen) or nameof(Helpers.HourGuideModel.HourGuideChip))
                    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PlaceHourGuide));
            };
            TripTabs.SizeChanged += (_, _) => PlaceHourGuide();
            HomeRoot.SizeChanged += (_, _) => PlaceHourGuide();
        }

        private void HourGuideChip_Click(object sender, MouseButtonEventArgs e) => ViewModel.HourGuide.ToggleHourGuideCommand.Execute(null);

        private void PlaceHourGuide()
        {
            if (!TripTabs.IsVisible) return;
            var form = TripTabs.TransformToAncestor(HomeRoot).TransformBounds(new Rect(TripTabs.RenderSize));
            if (HourGuideChip.IsVisible)
            {
                HourGuideChip.UpdateLayout();
                var right = form.Right - 4 - (TripNoticeChip.IsVisible ? TripNoticeChip.ActualWidth + 8 : 0);
                Canvas.SetLeft(HourGuideChip, Math.Max(form.Left, right - HourGuideChip.ActualWidth));
                Canvas.SetTop(HourGuideChip, Math.Max(0, form.Top - HourGuideChip.ActualHeight - 3));
            }
            if (HourGuideCard.IsVisible)
            {
                HourGuideCard.Width = Math.Max(320, Math.Min(440, form.Width * 0.58));
                Canvas.SetLeft(HourGuideCard, form.Left + 4);
                Canvas.SetTop(HourGuideCard, form.Top + 36);
            }
        }
    }
}
