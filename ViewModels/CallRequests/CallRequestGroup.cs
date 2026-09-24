using MaterialDesignThemes.Wpf;

namespace Raphael.Desktop.ViewModels.CallRequests;

/// <summary>A heading of the call queue: waiting, being handled, or closed today.</summary>
/// <remarks>
/// One instance per heading for the life of the panel. The list groups rows by reference to it, so
/// a row that changes state changes heading without the list being rebuilt under the dispatcher.
/// The count is the whole group's, not the page's: "Waiting 3" means three drivers, wherever they
/// fall in the pages.
/// </remarks>
public sealed class CallRequestGroup : BaseViewModel
{
    private int _count;

    public CallRequestGroup(int rank, string titleKey, PackIconKind icon)
    {
        Rank = rank;
        TitleKey = titleKey;
        Icon = icon;
    }

    /// <summary>Order on screen: waiting first, then being handled, then closed.</summary>
    public int Rank { get; }

    public string TitleKey { get; }

    public string Title => Services.LocalizationService.Instance[TitleKey];

    public PackIconKind Icon { get; }

    public int Count
    {
        get => _count;
        set => SetProperty(ref _count, value);
    }
}
