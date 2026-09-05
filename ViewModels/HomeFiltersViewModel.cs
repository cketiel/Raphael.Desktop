using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// The filters behind the sliding panel of the Home tab.
    /// </summary>
    /// <remarks>
    /// It holds no trips and fetches nothing: it answers <see cref="Matches"/> about one trip at a
    /// time, and the grid's ICollectionView does the rest. Everything it filters on is already in
    /// the DTO the day was loaded with, so opening the panel costs no request and changing a tick
    /// costs no round trip.
    ///
    /// ⚠️ Two things that look filterable are not, and are deliberately absent. County is in no
    /// table and no cache — it needs a column, a migration and a backfill, and it is in BACKLOG.
    /// Zip and State are parsed out of the address string, which is dependable on imported trips
    /// and not on ones typed through Google Places, so the panel says so where it asks for them.
    /// </remarks>
    public partial class HomeFiltersViewModel : ObservableObject
    {
        private readonly UserConfigService _config = new();

        /// <summary>Raised whenever anything here would change which trips are shown.</summary>
        public event Action Changed;

        public HomeFiltersViewModel()
        {
            SavedViews = new ObservableCollection<string>(_config.Load<List<string>>(SavedViewsKey) ?? new List<string>());
        }

        #region The filters themselves

        public ObservableCollection<TripFilterOption> SpaceTypes { get; } = new();
        public ObservableCollection<TripFilterOption> FundingSources { get; } = new();
        public ObservableCollection<TripFilterOption> Statuses { get; } = new();
        public ObservableCollection<TripFilterOption> Cities { get; } = new();
        public ObservableCollection<TripFilterOption> Types { get; } = new();

        [ObservableProperty] private TripPlaceScope _cityScope = TripPlaceScope.Both;
        [ObservableProperty] private TimeSpan? _pickupFrom;
        [ObservableProperty] private TimeSpan? _pickupTo;
        [ObservableProperty] private TripFlagFilter _willCall = TripFlagFilter.Any;
        [ObservableProperty] private TripFlagFilter _hasRoute = TripFlagFilter.Any;
        [ObservableProperty] private bool _onlyMissingCoordinates;
        [ObservableProperty] private string _zip;
        [ObservableProperty] private string _state;

        partial void OnCityScopeChanged(TripPlaceScope value) => Raise();
        partial void OnPickupFromChanged(TimeSpan? value) => Raise();
        partial void OnPickupToChanged(TimeSpan? value) => Raise();
        partial void OnWillCallChanged(TripFlagFilter value) => Raise();
        partial void OnHasRouteChanged(TripFlagFilter value) => Raise();
        partial void OnOnlyMissingCoordinatesChanged(bool value) => Raise();
        partial void OnZipChanged(string value) => Raise();
        partial void OnStateChanged(string value) => Raise();

        #endregion

        #region Does this trip get through?

        public bool Matches(TripReadDto trip)
        {
            if (!Allows(SpaceTypes, trip.SpaceTypeName)) return false;
            if (!Allows(FundingSources, trip.FundingSourceName)) return false;
            if (!Allows(Statuses, trip.Status)) return false;
            if (!Allows(Types, trip.Type)) return false;

            if (!AllowsCity(trip)) return false;

            if (PickupFrom.HasValue && (trip.FromTime == null || trip.FromTime < PickupFrom)) return false;
            if (PickupTo.HasValue && (trip.FromTime == null || trip.FromTime > PickupTo)) return false;

            if (WillCall != TripFlagFilter.Any && trip.WillCall != (WillCall == TripFlagFilter.Yes)) return false;

            if (HasRoute != TripFlagFilter.Any)
            {
                var routed = !string.IsNullOrWhiteSpace(trip.RunName);
                if (routed != (HasRoute == TripFlagFilter.Yes)) return false;
            }

            // A trip whose pickup or dropoff sits at 0,0 is one the import failed to geocode. It
            // will be routed to the middle of the Atlantic unless someone finds it first, and
            // nothing else on this screen makes it stand out.
            if (OnlyMissingCoordinates && !LacksCoordinates(trip)) return false;

            if (!string.IsNullOrWhiteSpace(Zip) && !AddressHolds(trip, Zip)) return false;
            if (!string.IsNullOrWhiteSpace(State) && !AddressHolds(trip, State)) return false;

            return true;
        }

        private static bool Allows(ObservableCollection<TripFilterOption> options, string value)
        {
            // Nothing ticked means the filter is not being used, not that nothing passes.
            var chosen = options.Where(o => o.IsChecked).ToList();
            if (chosen.Count == 0) return true;

            return chosen.Any(o => string.Equals(o.Value, value ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        private bool AllowsCity(TripReadDto trip)
        {
            var chosen = Cities.Where(o => o.IsChecked).Select(o => o.Value).ToList();
            if (chosen.Count == 0) return true;

            var pickup = chosen.Contains(trip.PickupCity ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            var dropoff = chosen.Contains(trip.DropoffCity ?? string.Empty, StringComparer.OrdinalIgnoreCase);

            return CityScope switch
            {
                TripPlaceScope.Pickup => pickup,
                TripPlaceScope.Dropoff => dropoff,
                _ => pickup || dropoff
            };
        }

        private static bool LacksCoordinates(TripReadDto trip) =>
            trip.PickupLatitude == 0 || trip.PickupLongitude == 0 ||
            trip.DropoffLatitude == 0 || trip.DropoffLongitude == 0;

        private static bool AddressHolds(TripReadDto trip, string part)
        {
            var needle = part.Trim();

            return Holds(trip.PickupAddress, needle) || Holds(trip.DropoffAddress, needle);
        }

        private static bool Holds(string text, string part) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        #endregion

        #region Rebuilding the choices from the day on screen

        /// <summary>
        /// Refills the tick lists from the trips currently loaded, keeping what was ticked.
        /// </summary>
        /// <remarks>
        /// The choices come from the day rather than from the catalogues on purpose: a panel
        /// offering forty funding sources when the day has three is a panel nobody reads. What was
        /// ticked survives even when it no longer appears, so moving to the next day does not
        /// silently widen the filter.
        /// </remarks>
        public void Rebuild(IEnumerable<TripReadDto> trips)
        {
            var all = trips?.ToList() ?? new List<TripReadDto>();

            Fill(SpaceTypes, all.Select(t => t.SpaceTypeName));
            Fill(FundingSources, all.Select(t => t.FundingSourceName));
            Fill(Statuses, all.Select(t => t.Status));
            Fill(Types, all.Select(t => t.Type));
            Fill(Cities, all.SelectMany(t => new[] { t.PickupCity, t.DropoffCity }));

            RaiseChipsChanged();
        }

        private static void Fill(ObservableCollection<TripFilterOption> options, IEnumerable<string> values)
        {
            var counted = values
                .Select(v => string.IsNullOrWhiteSpace(v) ? null : v.Trim())
                .Where(v => v != null)
                .GroupBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            // A ticked option that has left the day stays on the list, at zero, so the dispatcher
            // can see why the grid is empty instead of guessing.
            foreach (var stale in options.Where(o => !o.IsChecked && !counted.ContainsKey(o.Value)).ToList())
                options.Remove(stale);

            foreach (var option in options)
                option.Count = counted.TryGetValue(option.Value, out var n) ? n : 0;

            foreach (var pair in counted.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                if (options.Any(o => string.Equals(o.Value, pair.Key, StringComparison.OrdinalIgnoreCase)))
                    continue;

                options.Add(new TripFilterOption(pair.Key, pair.Key) { Count = pair.Value });
            }
        }

        /// <summary>
        /// Hooks every option so ticking one refilters. Called by the owner after Rebuild.
        /// </summary>
        public void WatchOptions()
        {
            foreach (var option in AllOptions())
            {
                option.PropertyChanged -= OnOptionChanged;
                option.PropertyChanged += OnOptionChanged;
            }
        }

        private void OnOptionChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TripFilterOption.IsChecked)) Raise();
        }

        private IEnumerable<TripFilterOption> AllOptions() =>
            SpaceTypes.Concat(FundingSources).Concat(Statuses).Concat(Cities).Concat(Types);

        #endregion

        #region What is switched on, said out loud

        /// <summary>
        /// The chips above the grid. They exist so that a list filtered down to nothing is never
        /// a mystery: whatever is hiding rows is on screen, with its own way to remove it.
        /// </summary>
        public ObservableCollection<string> ActiveChips { get; } = new();

        public bool HasActiveFilters => ActiveChips.Count > 0;

        /// <summary>
        /// Rebuilds the chips, which is also how they follow a change of language.
        /// </summary>
        /// <remarks>
        /// They are built strings rather than bindings, so notifying a property does not reach
        /// inside them: the words only change when the list is made again.
        /// </remarks>
        public void RaiseChipsChanged()
        {
            ActiveChips.Clear();

            foreach (var option in AllOptions().Where(o => o.IsChecked))
                ActiveChips.Add(option.Label);

            if (PickupFrom.HasValue || PickupTo.HasValue)
                ActiveChips.Add($"{PickupFrom:hh\\:mm} – {PickupTo:hh\\:mm}");

            if (WillCall != TripFlagFilter.Any)
                ActiveChips.Add($"{Text("home.WillCallLabel")}: {Flag(WillCall)}");

            if (HasRoute != TripFlagFilter.Any)
                ActiveChips.Add($"{Text("Run")}: {Flag(HasRoute)}");

            if (OnlyMissingCoordinates) ActiveChips.Add(Text("home.MissingCoordinates"));
            if (!string.IsNullOrWhiteSpace(Zip)) ActiveChips.Add($"{Text("Zip")} {Zip}");
            if (!string.IsNullOrWhiteSpace(State)) ActiveChips.Add($"{Text("State")} {State}");

            OnPropertyChanged(nameof(HasActiveFilters));
        }

        private static string Text(string key) => LocalizationService.Instance[key];

        private static string Flag(TripFlagFilter value) => value switch
        {
            TripFlagFilter.Yes => Text("home.FlagYes"),
            TripFlagFilter.No => Text("home.FlagNo"),
            _ => Text("home.FlagAny")
        };

        private void Raise()
        {
            RaiseChipsChanged();
            Changed?.Invoke();
        }

        [RelayCommand]
        public void ClearAll()
        {
            foreach (var option in AllOptions()) option.IsChecked = false;

            CityScope = TripPlaceScope.Both;
            PickupFrom = null;
            PickupTo = null;
            WillCall = TripFlagFilter.Any;
            HasRoute = TripFlagFilter.Any;
            OnlyMissingCoordinates = false;
            Zip = null;
            State = null;

            Raise();
        }

        #endregion

        #region Saved views

        private const string SavedViewsKey = "HomeFilterViews";

        public ObservableCollection<string> SavedViews { get; }

        [ObservableProperty] private string _newViewName;

        /// <summary>
        /// Keeps the ticked options under a name, on this machine.
        /// </summary>
        /// <remarks>
        /// Local rather than on the server because it is a preference, not a record: nothing about
        /// a dispatcher's favourite filter belongs in a table that carries patient data.
        /// </remarks>
        [RelayCommand]
        private void SaveView()
        {
            if (string.IsNullOrWhiteSpace(NewViewName)) return;

            var name = NewViewName.Trim();

            if (!SavedViews.Contains(name)) SavedViews.Add(name);

            _config.Save(SavedViewsKey, SavedViews.ToList());
            _config.Save(ViewKey(name), AllOptions().Where(o => o.IsChecked).Select(o => o.Value).ToList());

            NewViewName = null;
        }

        [RelayCommand]
        private void ApplyView(string name)
        {
            var chosen = _config.Load<List<string>>(ViewKey(name));
            if (chosen == null) return;

            foreach (var option in AllOptions())
                option.IsChecked = chosen.Contains(option.Value, StringComparer.OrdinalIgnoreCase);

            Raise();
        }

        [RelayCommand]
        private void DeleteView(string name)
        {
            SavedViews.Remove(name);
            _config.Save(SavedViewsKey, SavedViews.ToList());
        }

        private static string ViewKey(string name) => $"HomeFilterView_{name}";

        #endregion
    }
}
