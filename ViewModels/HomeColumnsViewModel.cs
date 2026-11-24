using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>One column of Home's trips, in the panel.</summary>
    public partial class HomeColumnItem : ObservableObject
    {
        public string Key { get; init; } = string.Empty;
        [ObservableProperty] private string _header = string.Empty;
        [ObservableProperty] private bool _isVisible;

        /// <summary>Its place among the columns on screen; empty while hidden.</summary>
        [ObservableProperty] private string _place = string.Empty;

        /// <summary>The one being dragged.</summary>
        [ObservableProperty] private bool _isMoving;
    }

    /// <summary>
    /// RE-089: which columns of Home's trips a user sees, and in what order. Each change shows at once
    /// and is kept for that user: on the server, with a copy on this computer so the next Home opens
    /// right away and an offline change is not lost.
    /// </summary>
    public partial class HomeColumnsViewModel : ObservableObject
    {
        public const string PreferenceKey = "home.trips.columns";

        private sealed record SavedColumn(string K, bool V);

        private sealed class SavedLayout
        {
            public List<SavedColumn> Columns { get; set; } = new();

            /// <summary>Changed here and not on the server yet.</summary>
            public bool Pending { get; set; }
        }

        private readonly IUserPreferenceService _service;
        private readonly UserConfigService _local = new();
        private readonly Func<IReadOnlyList<(string Key, string Header, bool Visible)>> _defaults;
        private readonly string _localKey;
        private readonly DispatcherTimer _saveTimer;
        private bool _applying;

        private static string T(string key) => LocalizationService.Instance[key];

        public HomeColumnsViewModel(Func<IReadOnlyList<(string Key, string Header, bool Visible)>> defaults, string legacyKey, IUserPreferenceService? service = null)
        {
            _defaults = defaults;
            _service = service ?? new UserPreferenceService();
            _localKey = $"HomeColumns_{SessionManager.UserId}";

            // One save for a burst: a drag across ten places is one change.
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _saveTimer.Tick += async (_, _) => { _saveTimer.Stop(); await PushAsync(); };

            View = CollectionViewSource.GetDefaultView(Items);
            View.Filter = o => string.IsNullOrWhiteSpace(Search) || ((HomeColumnItem)o).Header.Contains(Search.Trim(), StringComparison.CurrentCultureIgnoreCase);

            var saved = _local.Load<SavedLayout>(_localKey);
            Fill(saved?.Columns ?? Legacy(legacyKey));
            _ = SyncAsync(saved?.Pending == true);
        }

        public ObservableCollection<HomeColumnItem> Items { get; } = new();
        public ICollectionView View { get; }

        /// <summary>The order or the visible ones changed: the grid takes it.</summary>
        public event Action? Changed;

        [ObservableProperty] private bool _isOpen;
        partial void OnIsOpenChanged(bool value) { if (!value) Search = string.Empty; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanDrag))]
        private string _search = string.Empty;
        partial void OnSearchChanged(string value) => View.Refresh();

        /// <summary>A filtered list hides what would be between two places, so it is not reordered.</summary>
        public bool CanDrag => string.IsNullOrWhiteSpace(Search);

        public string TitleText => T("home.columns.Title");
        public string HintText => T("home.columns.Hint");
        public string SearchText => T("home.columns.Search");
        public string ShowAllText => T("home.columns.ShowAll");
        public string ResetText => T("home.columns.Reset");
        public string CloseText => T("home.columns.Close");
        public string DragText => T("home.columns.Drag");
        public string UpText => T("home.columns.Up");
        public string DownText => T("home.columns.Down");
        public string SummaryText => string.Format(T("home.columns.Summary"), Items.Count(i => i.IsVisible), Items.Count);

        public bool IsShown(string key) => Items.FirstOrDefault(i => i.Key == key)?.IsVisible == true;

        public IEnumerable<ColumnConfig> Layout => Items.Select(i => new ColumnConfig { PropertyName = i.Key, Header = i.Header, IsVisible = i.IsVisible });

        /// <summary>The whole order, hidden ones at the end: what is frozen must be what is seen.</summary>
        public IReadOnlyList<string> Order => Items.Where(i => i.IsVisible).Concat(Items.Where(i => !i.IsVisible)).Select(i => i.Key).ToList();

        // ---- Actions ---------------------------------------------------------------------------

        [RelayCommand] private void Toggle() => IsOpen = !IsOpen;
        [RelayCommand] private void Close() => IsOpen = false;

        [RelayCommand]
        private void ShowAll() => Batch(() => { foreach (var i in Items) i.IsVisible = true; });

        [RelayCommand]
        private void Reset() => Batch(() => Arrange(_defaults().Select(d => new SavedColumn(d.Key, d.Visible)).ToList()));

        [RelayCommand] private void Up(HomeColumnItem item) => MoveBy(item, -1);
        [RelayCommand] private void Down(HomeColumnItem item) => MoveBy(item, 1);

        private void MoveBy(HomeColumnItem item, int step)
        {
            if (item is null || !CanDrag) return;
            Move(item, Items.IndexOf(item) + step);
        }

        public void Move(HomeColumnItem item, int to)
        {
            var from = Items.IndexOf(item);
            if (from < 0 || to < 0 || to >= Items.Count || from == to) return;
            Items.Move(from, to);
            Touched();
        }

        /// <summary>Headers dragged on the grid itself: the ones on screen, in their new order.</summary>
        public void TakeVisibleOrder(IReadOnlyList<string> shown)
        {
            // Each hidden column stays behind the one it was behind.
            var order = shown.Where(k => Items.Any(i => i.Key == k)).ToList();
            foreach (var hidden in Items.Where(i => !i.IsVisible).ToList())
            {
                var before = Items.Take(Items.IndexOf(hidden)).LastOrDefault();
                order.Insert(before is null ? 0 : order.IndexOf(before.Key) + 1, hidden.Key);
            }
            Batch(() => Arrange(order.Select(k => new SavedColumn(k, IsShown(k))).ToList()));
        }

        public void RefreshTexts()
        {
            var headers = _defaults().ToDictionary(d => d.Key, d => d.Header);
            foreach (var i in Items) if (headers.TryGetValue(i.Key, out var h)) i.Header = h;
            OnPropertyChanged(string.Empty);
            View.Refresh();
        }

        // ---- Keeping it ------------------------------------------------------------------------

        private void Fill(IReadOnlyList<SavedColumn> saved)
        {
            foreach (var d in _defaults())
            {
                var item = new HomeColumnItem { Key = d.Key, Header = d.Header, IsVisible = d.Visible };
                item.PropertyChanged += OnItemChanged;
                Items.Add(item);
            }
            _applying = true;
            try { Arrange(saved); }
            finally { _applying = false; }
            Renumber();
        }

        /// <summary>A column added to the grid after the layout was saved takes its default place.</summary>
        private void Arrange(IReadOnlyList<SavedColumn> saved)
        {
            var known = saved.Where(s => Items.Any(i => i.Key == s.K)).GroupBy(s => s.K).Select(g => g.First()).ToList();
            var order = known.Select(s => s.K).ToList();
            var defaults = _defaults().Select(d => d.Key).ToList();
            foreach (var key in defaults.Where(k => !order.Contains(k)))
            {
                var before = defaults.Take(defaults.IndexOf(key)).LastOrDefault(order.Contains);
                order.Insert(before is null ? 0 : order.IndexOf(before) + 1, key);
            }
            for (var to = 0; to < order.Count; to++)
            {
                var from = Items.IndexOf(Items.First(i => i.Key == order[to]));
                if (from != to) Items.Move(from, to);
            }
            foreach (var s in known) Items.First(i => i.Key == s.K).IsVisible = s.V;
            if (Items.All(i => !i.IsVisible)) foreach (var d in _defaults()) Items.First(i => i.Key == d.Key).IsVisible = d.Visible;
        }

        /// <summary>What this computer remembered before: which were hidden. The comments start hidden now.</summary>
        private List<SavedColumn> Legacy(string key)
        {
            var old = _local.LoadColumnConfig(key);
            return _defaults().Select(d => new SavedColumn(d.Key, d.Visible && old?.FirstOrDefault(o => o.PropertyName == d.Key)?.IsVisible != false)).ToList();
        }

        private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(HomeColumnItem.IsVisible) || _applying || sender is not HomeColumnItem item) return;
            // A grid with no columns has nothing to bring them back with.
            if (Items.All(i => !i.IsVisible)) { item.IsVisible = true; return; }
            Touched();
        }

        private void Batch(Action change)
        {
            _applying = true;
            try { change(); }
            finally { _applying = false; }
            Touched();
        }

        private void Touched()
        {
            Renumber();
            Changed?.Invoke();
            SaveLocal(pending: true);
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void Renumber()
        {
            var n = 0;
            foreach (var i in Items) i.Place = i.IsVisible ? (++n).ToString() : string.Empty;
            OnPropertyChanged(nameof(SummaryText));
        }

        private List<SavedColumn> Current() => Items.Select(i => new SavedColumn(i.Key, i.IsVisible)).ToList();

        private void SaveLocal(bool pending) => _local.Save(_localKey, new SavedLayout { Columns = Current(), Pending = pending });

        private async Task PushAsync()
        {
            var sent = JsonSerializer.Serialize(Current());
            try
            {
                await _service.SaveAsync(PreferenceKey, sent);
                if (JsonSerializer.Serialize(Current()) == sent) SaveLocal(pending: false);
            }
            catch (Exception ex)
            {
                // Kept on this computer, and sent the next time Home opens.
                FileLogger.Log($"Home columns: not saved on the server. {ex.Message}");
            }
        }

        /// <summary>After Home is on screen: what the server has, unless this computer has something newer.</summary>
        private async Task SyncAsync(bool pending)
        {
            try
            {
                if (pending) { await PushAsync(); return; }
                var before = JsonSerializer.Serialize(Current());
                var json = await _service.GetAsync(PreferenceKey);
                if (json is null || json == before || JsonSerializer.Serialize(Current()) != before) return;
                var saved = JsonSerializer.Deserialize<List<SavedColumn>>(json);
                if (saved is null || saved.Count == 0) return;
                _applying = true;
                try { Arrange(saved); }
                finally { _applying = false; }
                Renumber();
                Changed?.Invoke();
                SaveLocal(pending: false);
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Home columns: could not read them. {ex.Message}");
            }
        }
    }
}
