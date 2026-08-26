using System.Collections.ObjectModel;
using MaterialDesignThemes.Wpf;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels;

/// <summary>
/// One tab of the Notification Center: its own slice of the inbox, in its own order.
/// </summary>
/// <remarks>
/// The dispatchers asked for these tabs because the office receives more notices than
/// anybody can read in order, and one of them — the Will Call — starts a one hour clock
/// the moment it arrives. Going straight to its tab is the difference between answering
/// in five minutes and finding it after forty.
///
/// <para>
/// ⚠️ Each tab holds a real <see cref="ObservableCollection{T}"/>, not a filtered
/// <c>ICollectionView</c> over a shared one. The first attempt did the latter, built with
/// <c>new CollectionViewSource { Source = shared }.View</c>, and it crashed the
/// application: an <c>ICollectionView</c> belongs to a single <c>ItemsControl</c>, and the
/// <c>CollectionViewSource</c> that owned it was a temporary the collector took away. Every
/// later refresh — a tab switch, or the expiry sweep — went through a view whose insides
/// had been pulled out and died in <c>ListCollectionView.PrepareLocalArray</c>.
/// A collection per tab costs a handful of references and cannot fail that way.
/// </para>
/// </remarks>
public sealed class NotificationTabViewModel : BaseViewModel
{
    /// <summary>
    /// Rows per page.
    /// </summary>
    /// <remarks>
    /// Fifty, the same page a mail client uses, and for the same reason: a list of several
    /// hundred rows is slow to lay out and impossible to work through. The office window is
    /// twelve hours, so the whole set already fits in memory — this paginates the rendering,
    /// not the fetch, which is why it needs nothing from the API.
    /// </remarks>
    public const int PageSize = 50;

    private readonly Func<NotificationItemViewModel, bool> _filter;

    private readonly Comparison<NotificationItemViewModel> _order;

    private readonly string _headerKey;

    /// <summary>Everything that belongs in this tab, in order, across all pages.</summary>
    private readonly List<NotificationItemViewModel> _matching = [];

    private int _pageIndex;

    private int _count;

    public PackIconKind Icon { get; }

    public string Header => LocalizationService.Instance[_headerKey];

    /// <summary>The rows of the current page, already in order.</summary>
    public ObservableCollection<NotificationItemViewModel> Items { get; } = [];

    /// <summary>Everything in this tab, across all pages.</summary>
    public IReadOnlyList<NotificationItemViewModel> All => _matching;

    /// <summary>Position of a row within the whole tab, or -1. For "9 de 237".</summary>
    public int IndexOf(NotificationItemViewModel item) => _matching.IndexOf(item);

    /// <summary>The row at a position within the whole tab, or null.</summary>
    public NotificationItemViewModel? At(int index) =>
        index >= 0 && index < _matching.Count
            ? _matching[index]
            : null;

    public NotificationTabViewModel(
        string headerKey,
        PackIconKind icon,
        Func<NotificationItemViewModel, bool> filter,
        Comparison<NotificationItemViewModel>? order = null)
    {
        _headerKey = headerKey;
        Icon = icon;
        _filter = filter;
        _order = order ?? ByNewestFirst;
    }

    #region Badge

    /// <summary>
    /// What still needs attention here. Shown next to the header so a dispatcher can see
    /// where the work is without opening every tab.
    /// </summary>
    public int Count
    {
        get => _count;
        private set
        {
            if (SetProperty(ref _count, value))
                OnPropertyChanged(nameof(HasCount));
        }
    }

    public bool HasCount => Count > 0;

    private bool _isSelected;

    /// <summary>Which chip is lit. Kept by the panel, read by the chip's trigger.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>False when this tab has nothing to show, so the empty message can appear.</summary>
    public bool HasRows => TotalCount > 0;

    #endregion

    #region Pagination

    public int TotalCount => _matching.Count;

    public int PageIndex
    {
        get => _pageIndex;
        private set
        {
            if (SetProperty(ref _pageIndex, value))
                RaisePagination();
        }
    }

    public bool CanGoPreviousPage => PageIndex > 0;

    public bool CanGoNextPage => (PageIndex + 1) * PageSize < TotalCount;

    /// <summary>"1–50 de 237", or the empty-inbox text.</summary>
    public string RangeLabel
    {
        get
        {
            if (TotalCount == 0)
                return LocalizationService.Instance["NotificationNoMessages"];

            var first = PageIndex * PageSize + 1;
            var last = Math.Min(first + PageSize - 1, TotalCount);

            return string.Format(
                LocalizationService.Instance["NotificationRange"],
                first,
                last,
                TotalCount);
        }
    }

    public void NextPage()
    {
        if (!CanGoNextPage)
            return;

        PageIndex++;
        FillPage();
    }

    public void PreviousPage()
    {
        if (!CanGoPreviousPage)
            return;

        PageIndex--;
        FillPage();
    }

    /// <summary>Brings the given row into view, changing page if it is on another one.</summary>
    public void EnsureVisible(NotificationItemViewModel item)
    {
        var position = _matching.IndexOf(item);

        if (position < 0)
            return;

        var page = position / PageSize;

        if (page == PageIndex)
            return;

        PageIndex = page;
        FillPage();
    }

    #endregion

    /// <summary>
    /// Brings this tab in line with the inbox, the search box and the grouping switch.
    /// </summary>
    public void Sync(
        IEnumerable<NotificationItemViewModel> all,
        string search,
        bool groupByTrip,
        Func<NotificationItemViewModel, bool> pending)
    {
        var matching = all
            .Where(_filter)
            .Where(item => item.Matches(search))
            .ToList();

        _matching.Clear();
        _matching.AddRange(groupByTrip ? Collapse(matching) : Ungrouped(matching));
        _matching.Sort(_order);

        // A page that no longer exists after notices expired would show an empty list with
        // no way back.
        var lastPage = Math.Max(0, (TotalCount - 1) / PageSize);

        if (PageIndex > lastPage)
            _pageIndex = lastPage;

        FillPage();

        Count = _matching.Count(pending);

        RaisePagination();

        OnPropertyChanged(nameof(Header));
    }

    /// <summary>
    /// Recounts without touching the rows.
    /// </summary>
    /// <remarks>
    /// No tab filters on read state, so a read mark cannot change which rows belong here —
    /// only the badge. Rebuilding anyway would reorder the list in the middle of the
    /// selection change that marked the row read, and drop that selection.
    /// </remarks>
    public void RefreshCount(Func<NotificationItemViewModel, bool> pending)
    {
        Count = _matching.Count(pending);

        OnPropertyChanged(nameof(Header));
    }

    /// <summary>
    /// One row per trip, spoken for by its newest notice, carrying the rest as its thread.
    /// </summary>
    /// <remarks>
    /// The direct answer to "son muchas notificaciones y les resulta engorroso": a trip
    /// that produced four notices takes one row instead of four. Notices with no trip
    /// behind them — there should be none today, but the payload does not guarantee it —
    /// stay on their own rather than being lumped together.
    /// </remarks>
    private static IEnumerable<NotificationItemViewModel> Collapse(
        List<NotificationItemViewModel> matching)
    {
        foreach (var group in matching.GroupBy(item => item.TripId))
        {
            if (group.Key is null)
            {
                foreach (var loose in group)
                {
                    loose.Thread = [];
                    yield return loose;
                }

                continue;
            }

            var story = group
                .OrderBy(item => item.CreatedAtUtc)
                .ToList();

            // The newest speaks for the trip, so the row shows where the journey is now.
            var face = story[^1];

            face.Thread = story;

            yield return face;
        }
    }

    private static IEnumerable<NotificationItemViewModel> Ungrouped(
        List<NotificationItemViewModel> matching)
    {
        foreach (var item in matching)
        {
            item.Thread = [];
            yield return item;
        }
    }

    /// <summary>
    /// Reconciles the visible page in place instead of clearing and refilling.
    /// </summary>
    /// <remarks>
    /// Refilling would be shorter and would also drop the dispatcher's selection every time
    /// a notification arrived — which, in an office that receives one every few seconds,
    /// means never managing to read one.
    /// </remarks>
    private void FillPage()
    {
        var desired = _matching
            .Skip(PageIndex * PageSize)
            .Take(PageSize)
            .ToList();

        var keep = new HashSet<NotificationItemViewModel>(desired);

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Items[i]))
                Items.RemoveAt(i);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var current = Items.IndexOf(desired[i]);

            if (current < 0)
                Items.Insert(i, desired[i]);
            else if (current != i)
                Items.Move(current, i);
        }
    }

    private void RaisePagination()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(RangeLabel));
        OnPropertyChanged(nameof(CanGoNextPage));
        OnPropertyChanged(nameof(CanGoPreviousPage));
    }

    /// <summary>Newest first, the way a mail client orders an inbox.</summary>
    public static int ByNewestFirst(
        NotificationItemViewModel a,
        NotificationItemViewModel b)
    {
        return b.CreatedAtUtc.CompareTo(a.CreatedAtUtc);
    }

    /// <summary>
    /// Unanswered first, then by how little time is left.
    /// </summary>
    /// <remarks>
    /// Deliberately not by arrival. The Will Call that arrived first is the one closest to
    /// running out, and a dispatcher scanning the top of the list has to be looking at the
    /// patient who has been waiting longest, not at the one who just called.
    /// </remarks>
    public static int ByDeadlineFirst(
        NotificationItemViewModel a,
        NotificationItemViewModel b)
    {
        var acknowledged = a.IsAcknowledged.CompareTo(b.IsAcknowledged);

        if (acknowledged != 0)
            return acknowledged;

        var deadline =
            (a.WillCallDeadlineUtc ?? DateTime.MaxValue)
            .CompareTo(b.WillCallDeadlineUtc ?? DateTime.MaxValue);

        return deadline != 0
            ? deadline
            : ByNewestFirst(a, b);
    }
}
