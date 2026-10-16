using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Converters
{
    /// <summary>
    /// Shows a trip row's Accept or Reject button (parameter "Accept" or "Reject") only to whom may
    /// press it, and only while it means something (PROVIDER_ACCESS_MODEL.md §3.7):
    /// - A Provider's user (internal: it uses this app), on its own trips — any role.
    /// - An admin of the office, on Raphael's own trips (no Provider).
    /// Only on trips that came from an integration (IntegratorId): a manual or imported trip has
    /// nobody on the other side to tell. Accept while the trip is Assigned; Reject while it is
    /// Assigned or Accepted. The backend checks all of it again: this only keeps the buttons honest.
    /// </summary>
    public class TripCommitVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not TripReadDto trip || trip.IsCancelled || trip.IntegratorId == null) return Visibility.Collapsed;

            var mayAct = SessionManager.ProviderId is int providerId
                ? trip.ProviderId == providerId
                : SessionManager.IsMilanesTransport && SessionManager.Role == "1" && trip.ProviderId == null;
            if (!mayAct) return Visibility.Collapsed;

            var show = (parameter as string) switch
            {
                "Accept" => trip.Status == TripStatus.Assigned,
                "Reject" => trip.Status == TripStatus.Assigned || trip.Status == TripStatus.Accepted,
                _ => false
            };
            return show ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
