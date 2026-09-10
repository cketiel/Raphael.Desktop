using CommunityToolkit.Mvvm.ComponentModel;
using DocumentFormat.OpenXml.Wordprocessing;
using Raphael.Desktop.Models; 
using System;

namespace Raphael.Desktop.DTOs
{
    public partial class ScheduleDto : ObservableObject, Helpers.Maps.IMapMarker
    {
        // --- IMapMarker: how the map layer reads this stop's position, without a binding path.
        double Helpers.Maps.IMapMarker.MarkerLatitude => ScheduleLatitude;
        double Helpers.Maps.IMapMarker.MarkerLongitude => ScheduleLongitude;
        int Helpers.Maps.IMapMarker.MarkerOffsetIndex => VisualOffsetIndex;


        // Propiedades que no cambian o no se necesita notificar en tiempo real
        public int Id { get; set; }
        public int? TripId { get; set; }
        public string Name { get; set; }
        //public TimeSpan? Pickup { get; set; }
        //public TimeSpan? Appt { get; set; }
        public string Address { get; set; }
        public double ScheduleLatitude { get; set; }
        public double ScheduleLongitude { get; set; }
        public string? Comment { get; set; }
        public string? Phone { get; set; }
        public TimeSpan? Arrive { get; set; }
        public TimeSpan? Perform { get; set; }
        public double? ArriveDist { get; set; }
        public double? PerformDist { get; set; }
        public string? Driver { get; set; }
        public string? GPSArrive { get; set; }
        public long? Odometer { get; set; }
        public string? AuthNo { get; set; }
        public string? FundingSource { get; set; }
        public DateTime? Date { get; set; }
        //public ScheduleEventType? EventType { get; set; }
        public string? SpaceType { get; set; }
        public string? TripType { get; set; }
        public string? Patient { get; set; }
        public bool Performed { get; set; }
        public string? Run { get; set; }
        public string? Vehicle { get; set; }

        // --- Properties that DO change and need to notify the UI ---
        // They are converted to private fields with the attribute [ObservableProperty]

        [ObservableProperty]
        private ScheduleEventType? _eventType;

        [ObservableProperty]
        private TimeSpan? _pickup;

        [ObservableProperty]
        private TimeSpan? _appt;

        [ObservableProperty]
        private TimeSpan? _eTA;

        [ObservableProperty]
        private double? _distance;

        [ObservableProperty]
        private TimeSpan? _travel;

        [ObservableProperty]
        private int? _on; 

        [ObservableProperty]
        private int? _sequence;

        /// <summary>
        /// How long the driver stands at this pickup waiting for the hour to come round. Null on
        /// anything that is not a pickup, and on a pickup with no wait.
        /// </summary>
        /// <remarks>
        /// A vehicle is never shown arriving more than a quarter of an hour before the hour the
        /// patient was promised — five minutes on a return — so an earlier arrival is raised to
        /// that limit. The drive is not shortened, so the difference is the driver sitting
        /// outside a house. Until this existed the figure was computed, used, and discarded, and
        /// a dispatcher reading a column of sensible-looking hours had no way of telling that one
        /// of those drivers was parked for two of them.
        ///
        /// <para>
        /// The hour the driver could have been there is <c>ETA - Wait</c>.
        /// </para>
        /// </remarks>
        [ObservableProperty]
        private TimeSpan? _wait;

        /// <summary>
        /// Whether this row is showing its time bar instead of its columns of figures.
        /// </summary>
        /// <remarks>
        /// View state, like <see cref="IsSelectedForMap"/> and <see cref="VisualOffsetIndex"/>:
        /// nothing here is sent anywhere or read back. Pull-out and Pull-in never take it.
        /// </remarks>
        [ObservableProperty]
        private bool _showAsBar;

        /// <summary>
        /// In words: when the driver could be there, when the route says they arrive, and why the
        /// two differ. Null when there is no wait.
        /// </summary>
        [ObservableProperty]
        private string _waitExplanation;

        // --- The three parts of this stop's time bar, in minutes.
        //
        // Weights rather than pixels: the bar is laid out with star-sized columns, so a row
        // scales itself to whatever width the column ends up with and needs no measuring pass.
        // Green from leaving the previous stop to arriving, red for the wait, green again for
        // the margin the rule keeps in front of the promised hour.

        [ObservableProperty]
        private double _barTravelWeight;

        [ObservableProperty]
        private double _barWaitWeight;

        [ObservableProperty]
        private double _barMarginWeight;

        // --- The four hours the bar marks, each against its own tick.

        /// <summary>The hour the vehicle leaves the previous stop, at the left end of the bar.</summary>
        [ObservableProperty]
        private string _barStartLabel;

        /// <summary>
        /// The hour the driver could be at the door if they drove straight there. Only worth a
        /// mark of its own when there is a wait; without one it is the arrival hour.
        /// </summary>
        [ObservableProperty]
        private string _barEarliestLabel;

        /// <summary>The arrival the route shows — the hour after the rule has had its say.</summary>
        [ObservableProperty]
        private string _barEtaLabel;

        /// <summary>The hour the bar ends on: the promised one, or the arrival if it is later.</summary>
        [ObservableProperty]
        private string _barEndLabel;

        /// <summary>
        /// Whether the vehicle gets there after the hour it was promised.
        /// </summary>
        /// <remarks>
        /// ⚠️ It decides what the last stretch of the bar means, so it cannot be left out. Running
        /// early, that stretch is the cushion the rule keeps in front of the promise and runs from
        /// the arrival to the promised hour. Running late it is the overshoot and runs the other
        /// way, from the promised hour to the arrival. Drawn the same either way, the bar would
        /// show a margin on a stop that has none.
        /// </remarks>
        [ObservableProperty]
        private bool _barIsLate;

        /// <summary>The hour at the left edge of that last stretch.</summary>
        [ObservableProperty]
        private string _barLeftMarkLabel;

        /// <summary>The hour at its right edge.</summary>
        [ObservableProperty]
        private string _barRightMarkLabel;

        /// <summary>The hour the patient was promised, whichever end of the bar it fell on.</summary>
        [ObservableProperty]
        private string _barPromisedLabel;

        // --- What each stretch of the bar costs, for the tooltip that reads it out.

        /// <summary>How long the drive from the previous stop takes.</summary>
        [ObservableProperty]
        private string _barTravelText;

        /// <summary>How long the last stretch is — cushion or overshoot.</summary>
        [ObservableProperty]
        private string _barLastStretchText;

        /// <summary>
        /// Whether the wait is a big enough share of the bar to carry its own duration inside it.
        /// </summary>
        /// <remarks>
        /// A share and not a number of minutes: what decides whether "45 m" fits is how wide that
        /// red stretch is drawn, and that depends on the whole bar. Ten minutes of waiting on a
        /// twenty-minute bar is half of it; the same ten minutes on a two-hour bar is a sliver
        /// with two hour labels already pressed against its edges. When it does not fit the
        /// figure is still a hover away.
        /// </remarks>
        [ObservableProperty]
        private bool _barShowsWaitText;

        /// <summary>
        /// Whether there is room between two marks to name the hour on the earlier one.
        /// </summary>
        /// <remarks>
        /// ⚠️ An hour is dropped rather than printed on top of its neighbour. Two marks a couple
        /// of minutes apart are a couple of pixels apart, and two hours written at the same spot
        /// are not one hour badly drawn — they are an unreadable smudge where the dispatcher
        /// needed a number. What does not fit on the bar is still in the tooltip, in full.
        /// </remarks>
        [ObservableProperty]
        private bool _barShowsEarliestLabel;

        /// <summary>Whether the hour at the far end of the bar has room of its own.</summary>
        /// <remarks>
        /// When it does not, the two ends of that last stretch are the same minute anyway, or
        /// within a minute or two of it, and the arrival — which is the one that matters — keeps
        /// the space.
        /// </remarks>
        [ObservableProperty]
        private bool _barShowsRightLabel;

        /// <summary>
        /// What that last stretch is called, which depends on which side of the promise the
        /// vehicle lands: margin when it arrives early, delay when it arrives late.
        /// </summary>
        [ObservableProperty]
        private string _barLastStretchCaption;

        [ObservableProperty]
        private bool _isSelectedForMap;
        public string? Status { get; set; }

        /// <summary>
        /// Index for visual scrolling of markers on the map that overlap.
        /// 0 = no scroll, 1 = first scroll, 2 = second, etc.
        /// </summary>
        [ObservableProperty]
        private int _visualOffsetIndex = 0;

    }
}