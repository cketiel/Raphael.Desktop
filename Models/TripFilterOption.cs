using CommunityToolkit.Mvvm.ComponentModel;

namespace Raphael.Desktop.Models
{
    /// <summary>
    /// One tickable line of a multiple-choice filter, with how many trips it would keep.
    /// </summary>
    /// <remarks>
    /// The count is not decoration. A dispatcher choosing a city wants to know that picking it
    /// leaves four trips rather than none before they pick it, which is the difference between a
    /// filter panel and a guessing game.
    /// </remarks>
    public partial class TripFilterOption : ObservableObject
    {
        public TripFilterOption(string value, string label)
        {
            Value = value;
            Label = label;
        }

        /// <summary>What the trip's own field has to equal. Never shown.</summary>
        public string Value { get; }

        /// <summary>What the dispatcher reads.</summary>
        public string Label { get; }

        [ObservableProperty] private bool _isChecked;

        [ObservableProperty] private int _count;

        public string Display => $"{Label} ({Count})";

        partial void OnCountChanged(int value) => OnPropertyChanged(nameof(Display));
    }

    /// <summary>Which end of the trip a place filter is talking about.</summary>
    public enum TripPlaceScope
    {
        Both,
        Pickup,
        Dropoff
    }

    /// <summary>A yes / no filter that is also allowed to not care.</summary>
    public enum TripFlagFilter
    {
        Any,
        Yes,
        No
    }
}
