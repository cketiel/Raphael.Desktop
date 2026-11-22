using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels.LiveMap
{
    /// <summary>One tile of the "Today" panel: a label and a figure.</summary>
    public record LiveMapTile(string Label, string Value);

    /// <summary>
    /// The "Today" panel (RE-080): the same figures Home prints under its list, for the whole day.
    /// </summary>
    public partial class LiveMapDayViewModel : ObservableObject
    {
        private LiveMapDaySummaryDto _day = new();

        public IReadOnlyList<LiveMapTile> Tiles
        {
            get
            {
                var l = LocalizationService.Instance;
                return new[]
                {
                    new LiveMapTile(l["livemap.DayTrips"], _day.Trips.ToString(CultureInfo.CurrentCulture)),
                    new LiveMapTile(l["livemap.DayCharges"], Money(_day.Charges)),
                    new LiveMapTile(l["livemap.DayCompleted"], _day.Completed.ToString(CultureInfo.CurrentCulture)),
                    new LiveMapTile(l["livemap.DayInProgress"], _day.InProgress.ToString(CultureInfo.CurrentCulture)),
                    new LiveMapTile(l["livemap.DayCanceled"], _day.Canceled.ToString(CultureInfo.CurrentCulture)),
                    new LiveMapTile(l["livemap.DayUnrouted"], _day.Unrouted.ToString(CultureInfo.CurrentCulture))
                };
            }
        }

        /// <summary>How much of the day is done, 0..1, for the bar under the tiles.</summary>
        public double DoneRatio => _day.Trips == 0 ? 0 : (double)_day.Completed / Math.Max(1, _day.Trips - _day.Canceled);

        public string DoneText => string.Format(LocalizationService.Instance["livemap.DayDone"], Math.Round(DoneRatio * 100));

        public void Apply(LiveMapDaySummaryDto day)
        {
            _day = day ?? new LiveMapDaySummaryDto();
            Refresh();
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(Tiles));
            OnPropertyChanged(nameof(DoneRatio));
            OnPropertyChanged(nameof(DoneText));
        }

        /// <summary>
        /// Exactly as Home's summary prints the same figure (HomeViewModel.Money): cents included.
        /// It was rounded to whole dollars here, so Home said $1,559.52 and the map $1,560 for one sum.
        /// </summary>
        private static string Money(double amount) =>
            "$" + amount.ToString("N2", CultureInfo.InvariantCulture);
    }
}
