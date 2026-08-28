using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ClosedXML.Excel;
using MaterialDesignThemes.Wpf;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Win32;
using Raphael.Desktop.Commands;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Notifications;

namespace Raphael.Desktop.ViewModels;

/// <summary>
/// The Notification Center: the dispatch office inbox, its filters and its actions.
/// </summary>
/// <remarks>
/// Laid out like a mail client on purpose. A list that fills the width, and the notice
/// opens over it when a dispatcher double-clicks — not a permanent side pane, which on the
/// smaller monitors in the office left neither half readable.
/// </remarks>
public sealed class NotificationCenterViewModel : BaseViewModel
{
    /// <summary>How often the Will Call countdowns are redrawn.</summary>
    private static readonly TimeSpan ClockInterval = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Silence longer than this during a shift is reported as a channel problem.
    /// </summary>
    /// <remarks>
    /// A hub that is connected but delivering nothing looks exactly like a quiet morning.
    /// It is the shape the <c>DriverRoleIds</c> misconfiguration takes: every internal user
    /// is treated as a driver, nobody joins the office broadcast, and the panel sits there
    /// perfectly healthy and permanently empty.
    /// </remarks>
    private static readonly TimeSpan SilenceThreshold = TimeSpan.FromHours(2);

    private readonly INotificationService _notifications;

    private readonly NotificationTextService _text;

    private readonly NotificationAdminApiClient _admin;

    private readonly Dictionary<int, TripReadDto> _tripCache = [];

    private readonly DispatcherTimer _clock;

    private readonly DateTime _openedAtUtc = DateTime.UtcNow;

    private readonly List<NotificationItemViewModel> _all = [];

    private NotificationTabViewModel _selectedTab;

    private NotificationItemViewModel _selectedNotification;

    private string _searchText = string.Empty;

    private string _adminMessage;

    private bool _isBusy;

    private bool _isReading;

    private bool _isAdminOpen;

    private string _adminSection = AdminSections.Channel;

    // Each administration section is fetched the first time it is opened, not when the
    // panel loads: the kept records are the one list in the system with no ceiling, and
    // nobody should pay for it just by being an administrator.
    private bool _rulesLoaded;

    private bool _archivedLoaded;

    private bool _auditLoaded;

    private bool _groupByTrip;

    private bool _isInOwnWindow;

    public ObservableCollection<NotificationTabViewModel> Tabs { get; } = [];

    public ObservableCollection<NotificationEventToggleViewModel> EventToggles { get; } = [];

    public NotificationDetailViewModel Detail { get; }

    public NotificationCenterViewModel(
        INotificationService notifications,
        NotificationTextService text)
    {
        _notifications = notifications;
        _text = text;
        _admin = new NotificationAdminApiClient();

        Detail = new NotificationDetailViewModel(
            new TripService(),
            new CustomerService(),
            new RunService(),
            new ScheduleService(),
            _tripCache);

        BuildTabs();

        SelectTabCommand = new RelayCommandObject(
            parameter => SelectedTab = parameter as NotificationTabViewModel);

        ToggleReadCommand = new RelayCommandObject(ToggleRead);

        ArchiveCommand = new AsyncRelayCommand(ToggleArchivedAsync);

        DeleteArchivedCommand = new AsyncRelayCommand(
            DeleteArchivedAsync,
            _ => IsAdmin);

        DeleteAllArchivedCommand = new AsyncRelayCommand(
            _ => DeleteAllArchivedAsync(),
            _ => IsAdmin);

        MarkAllReadCommand = new RelayCommandObject(_ => _notifications.SetAllRead(true));

        MarkAllUnreadCommand = new RelayCommandObject(_ => _notifications.SetAllRead(false));

        AcknowledgeCommand = new AsyncRelayCommand(
            _ => AcknowledgeAsync(),
            _ => SelectedNotification?.CanAcknowledge == true);

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());

        CopyTripNumberCommand = new RelayCommandObject(
            _ => CopyTripNumber(),
            _ => SelectedNotification?.TripId is not null);

        ExportCommand = new RelayCommandObject(_ => Export());

        OpenReadingCommand = new RelayCommandObject(OpenReading);

        CloseReadingCommand = new RelayCommandObject(_ => IsReading = false);

        NextMessageCommand = new RelayCommandObject(
            _ => Move(1),
            _ => CanMove(1));

        PreviousMessageCommand = new RelayCommandObject(
            _ => Move(-1),
            _ => CanMove(-1));

        NextPageCommand = new RelayCommandObject(
            _ => { SelectedTab?.NextPage(); RaisePagination(); },
            _ => SelectedTab?.CanGoNextPage == true);

        PreviousPageCommand = new RelayCommandObject(
            _ => { SelectedTab?.PreviousPage(); RaisePagination(); },
            _ => SelectedTab?.CanGoPreviousPage == true);

        RunRetentionCommand = new AsyncRelayCommand(
            _ => RunRetentionAsync(),
            _ => IsAdmin);

        OpenAdminCommand = new RelayCommandObject(
            _ => OpenAdmin(),
            _ => IsAdmin);

        CloseAdminCommand = new RelayCommandObject(_ => IsAdminOpen = false);

        SelectAdminSectionCommand = new AsyncRelayCommand(
            SelectAdminSectionAsync,
            _ => IsAdmin);

        RefreshAdminSectionCommand = new AsyncRelayCommand(
            _ => ReloadAdminSectionAsync(),
            _ => IsAdmin);

        ToggleWindowCommand = new RelayCommandObject(
            _ => ToggleWindowRequested?.Invoke());

        OpenAlertSettingsCommand = new RelayCommandObject(
            _ => AlertSettingsRequested?.Invoke());

        OpenTripCommand = new RelayCommandObject(
            _ =>
            {
                if (SelectedNotification?.TripId is int tripId)
                    OpenTripRequested?.Invoke(tripId);
            },
            _ => SelectedNotification?.TripId is not null);

        _notifications.NotificationsChanged += OnNotificationsChanged;
        _notifications.ReadStateChanged += OnReadStateChanged;
        _notifications.ConnectionStateChanged += OnConnectionStateChanged;

        _clock = new DispatcherTimer { Interval = ClockInterval };
        _clock.Tick += (_, _) => TickClock();
        _clock.Start();

        Sync();
    }

    #region Commands

    public ICommand SelectTabCommand { get; }

    public ICommand ToggleReadCommand { get; }

    public ICommand ArchiveCommand { get; }

    public ICommand DeleteArchivedCommand { get; }

    public ICommand DeleteAllArchivedCommand { get; }

    public ICommand MarkAllReadCommand { get; }

    public ICommand MarkAllUnreadCommand { get; }

    public ICommand AcknowledgeCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand CopyTripNumberCommand { get; }

    public ICommand ExportCommand { get; }

    public ICommand OpenReadingCommand { get; }

    public ICommand CloseReadingCommand { get; }

    public ICommand NextMessageCommand { get; }

    public ICommand PreviousMessageCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand RunRetentionCommand { get; }

    public ICommand OpenAdminCommand { get; }

    public ICommand CloseAdminCommand { get; }

    public ICommand SelectAdminSectionCommand { get; }

    public ICommand RefreshAdminSectionCommand { get; }

    public ICommand ToggleWindowCommand { get; }

    public ICommand OpenAlertSettingsCommand { get; }

    public ICommand OpenTripCommand { get; }

    /// <summary>
    /// Moves the panel between a tab and its own window. Wired by the view, because only
    /// the view knows which visual tree it currently lives in.
    /// </summary>
    public Action ToggleWindowRequested { get; set; }

    /// <summary>Opens the selected trip wherever the host decides. Wired by the view.</summary>
    public Action<int> OpenTripRequested { get; set; }

    /// <summary>
    /// Opens this dispatcher's live alert preferences. Wired by the host, because the
    /// alert stack belongs to the main window, not to this panel.
    /// </summary>
    public Action AlertSettingsRequested { get; set; }

    #endregion

    #region Translation

    // BaseViewModel re-raises everything when the language changes, so these are read again.

    public string PanelTitle => LocalizationService.Instance["NotificationCenter"];

    public string SearchHint => LocalizationService.Instance["NotificationSearchHint"];

    public string MarkAllReadText => LocalizationService.Instance["NotificationMarkAllRead"];

    public string MarkAllUnreadText => LocalizationService.Instance["NotificationMarkAllUnread"];

    public string RefreshText => LocalizationService.Instance["NotificationRefresh"];

    public string ExportText => LocalizationService.Instance["NotificationExport"];

    public string GroupByTripText => LocalizationService.Instance["NotificationGroupByTrip"];

    public string BackText => LocalizationService.Instance["NotificationBack"];

    public string NextText => LocalizationService.Instance["NotificationNext"];

    public string PreviousText => LocalizationService.Instance["NotificationPrevious"];

    public string EmptyListText => LocalizationService.Instance["NotificationEmptyList"];

    public string JourneySectionTitle => LocalizationService.Instance["NotificationJourneySection"];

    public string TripDataSectionTitle => LocalizationService.Instance["NotificationTripDataSection"];

    public string BillingSectionTitle => LocalizationService.Instance["NotificationBillingSection"];

    public string DriverSectionTitle => LocalizationService.Instance["Driver"];

    public string VehicleSectionTitle => LocalizationService.Instance["Vehicle"];

    public string LineSectionTitle => LocalizationService.Instance["NotificationLineSection"];

    public string LoadingPatientText => LocalizationService.Instance["NotificationLoadingPatient"];

    public string LoadingLineText => LocalizationService.Instance["NotificationLoadingLine"];

    public string DeadlineHeader => LocalizationService.Instance["NotificationDeadline"];

    public string PatientLabel => LocalizationService.Instance["NotificationPatient"];

    public string PickupLabel => LocalizationService.Instance["NotificationPickup"];

    public string DropoffLabel => LocalizationService.Instance["NotificationDropoff"];

    public string AcknowledgeText => LocalizationService.Instance["NotificationAcknowledge"];

    public string OpenTripText => LocalizationService.Instance["NotificationOpenTrip"];

    public string CopyTripText => LocalizationService.Instance["NotificationCopyTrip"];

    public string ThreadHeaderText => LocalizationService.Instance["NotificationThreadHeader"];

    public string AdminSectionTitle => LocalizationService.Instance["NotificationAdminSection"];

    public string RunRetentionText => LocalizationService.Instance["NotificationRunRetention"];

    public string RetentionPolicyText => LocalizationService.Instance["NotificationRetentionPolicy"];

    public string RulesSectionTitle => LocalizationService.Instance["NotificationRulesSection"];

    public string RulesWarningText => LocalizationService.Instance["NotificationRulesWarning"];

    public string ChannelSectionTitle => LocalizationService.Instance["NotificationChannelSection"];

    public string ArchivedSectionTitle => LocalizationService.Instance["NotificationArchivedSection"];

    public string ArchivedWarningText => LocalizationService.Instance["NotificationArchivedWarning"];

    public string DeleteArchivedText => LocalizationService.Instance["NotificationDeleteArchived"];

    public string DeleteAllArchivedText => LocalizationService.Instance["NotificationDeleteAllArchived"];

    public string AuditSectionTitle => LocalizationService.Instance["NotificationAuditSection"];

    public string AdminOpenText => LocalizationService.Instance["NotificationAdminOpen"];

    public string AlertSettingsText => LocalizationService.Instance["NotificationAlertsSettings"];

    public string HelpSectionText => LocalizationService.Instance["HelpSectionTooltip"];

    public string AdminBackText => LocalizationService.Instance["NotificationAdminBack"];

    public string AdminChannelHint => LocalizationService.Instance["NotificationAdminChannelHint"];

    public string AdminRulesHint => LocalizationService.Instance["NotificationAdminRulesHint"];

    public string AdminArchivedHint => LocalizationService.Instance["NotificationAdminArchivedHint"];

    public string AdminAuditHint => LocalizationService.Instance["NotificationAdminAuditHint"];

    public string AdminChannelExplainText => LocalizationService.Instance["NotificationAdminChannelExplain"];

    public string AdminRetentionTitle => LocalizationService.Instance["NotificationAdminRetentionTitle"];

    public string AdminWhenColumn => LocalizationService.Instance["NotificationAdminWhen"];

    public string AdminActionColumn => LocalizationService.Instance["NotificationAdminAction"];

    public string AdminWhoColumn => LocalizationService.Instance["NotificationAdminWho"];

    public string AdminDetailsColumn => LocalizationService.Instance["NotificationAdminDetails"];

    public string NoRulesText => LocalizationService.Instance["NotificationNoRules"];

    public string NoArchivedText => LocalizationService.Instance["NotificationNoArchived"];

    public string NoAuditText => LocalizationService.Instance["NotificationNoAudit"];

    /// <summary>
    /// The pop-out control says what it will do, and it is not the same thing in both
    /// places: from a tab it opens a window, from the window it puts the panel back.
    /// </summary>
    public string ToggleWindowText =>
        LocalizationService.Instance[
            IsInOwnWindow
                ? "NotificationDockBack"
                : "NotificationToggleWindow"];

    public PackIconKind ToggleWindowIcon =>
        IsInOwnWindow
            ? PackIconKind.ArrowCollapse
            : PackIconKind.OpenInNew;

    public string PendingActionLabel => string.Format(
        LocalizationService.Instance["NotificationPendingAction"],
        PendingActionCount);

    public string InProgressLabel => string.Format(
        LocalizationService.Instance["NotificationInProgress"],
        InProgressCount);

    public string CancelledTodayLabel => string.Format(
        LocalizationService.Instance["NotificationCancelledToday"],
        CancelledTodayCount);

    #endregion

    #region Selection, search, grouping and tabs

    public NotificationTabViewModel SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (value is null || !SetProperty(ref _selectedTab, value))
                return;

            foreach (var tab in Tabs)
                tab.IsSelected = tab == value;

            // Changing folder closes whatever was open, the way a mail client does.
            IsReading = false;

            RaisePagination();
        }
    }

    public NotificationItemViewModel SelectedNotification
    {
        get => _selectedNotification;
        set
        {
            if (!SetProperty(ref _selectedNotification, value))
                return;

            OnPropertyChanged(nameof(ReadingPositionLabel));

            // The trip is loaded when the notice is opened, not when the row is clicked.
            // Selecting is how a dispatcher walks the list with the arrow keys; a call to
            // the API per row would turn scrolling into a load test.
            if (IsReading)
                _ = Detail.ShowAsync(value);
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                RefreshTabs();
        }
    }

    /// <summary>
    /// Collapses every notice about the same trip into one row.
    /// </summary>
    /// <remarks>
    /// A trip that ran normally leaves four notices behind it. Two hundred notices become
    /// about fifty journeys, which is the number a person can actually work through.
    /// </remarks>
    public bool GroupByTrip
    {
        get => _groupByTrip;
        set
        {
            if (!SetProperty(ref _groupByTrip, value))
                return;

            IsReading = false;
            RefreshTabs();
        }
    }

    /// <summary>
    /// The tabs, as data. Adding or removing one is a row here, not another block of XAML.
    /// </summary>
    private void BuildTabs()
    {
        Tabs.Add(NewTab(
            "NotificationTabAll",
            PackIconKind.InboxOutline,
            _ => true));

        // Anything still waiting for somebody to take charge, whatever the event. Driven by
        // the notification type rather than a list of codes, so a notice declared as action
        // required on the server lands here without this client being taught about it.
        Tabs.Add(NewTab(
            "NotificationTabPending",
            PackIconKind.ClockAlertOutline,
            item => item.RequiresAction,
            NotificationTabViewModel.ByDeadlineFirst));

        // ⚠️ From the moment a patient says they are ready, the office has one hour to get
        // a vehicle to them, and the clock does not wait for anybody to look at a screen.
        Tabs.Add(NewTab(
            "NotificationTabWillCall",
            PackIconKind.PhoneAlert,
            item => Is(item, NotificationKeys.Events.WillCallActivated),
            NotificationTabViewModel.ByDeadlineFirst));

        Tabs.Add(NewTab(
            "NotificationTabCancellations",
            PackIconKind.CalendarRemoveOutline,
            item => Is(item, NotificationKeys.Events.TripCancelled)));

        // From here on the tabs follow the life of a trip: started → arrived → on board →
        // completed. A dispatcher reads them in the order the vehicle actually moves.
        Tabs.Add(NewTab(
            "NotificationTabStarted",
            PackIconKind.Navigation,
            item => Is(item, NotificationKeys.Events.DriverStartedTrip)));

        Tabs.Add(NewTab(
            "NotificationTabDriverArrived",
            PackIconKind.CarBack,
            item => Is(item, NotificationKeys.Events.DriverArrivedPickup)));

        Tabs.Add(NewTab(
            "NotificationTabOnBoard",
            PackIconKind.AccountCheckOutline,
            item => Is(item, NotificationKeys.Events.DriverPickedUpPassenger)));

        Tabs.Add(NewTab(
            "NotificationTabCompleted",
            PackIconKind.FlagCheckered,
            item => Is(item, NotificationKeys.Events.DriverCompletedTrip)));

        _selectedTab = Tabs[0];
        _selectedTab.IsSelected = true;
    }

    private static NotificationTabViewModel NewTab(
        string headerKey,
        PackIconKind icon,
        Func<NotificationItemViewModel, bool> filter,
        Comparison<NotificationItemViewModel>? order = null)
    {
        return new NotificationTabViewModel(headerKey, icon, filter, order);
    }

    private static bool Is(NotificationItemViewModel item, string eventCode) =>
        string.Equals(
            item.BusinessEventCode,
            eventCode,
            StringComparison.OrdinalIgnoreCase);

    #endregion

    #region Reading mode

    /// <summary>
    /// True while a notice is open over the list, as opposed to the list itself.
    /// </summary>
    public bool IsReading
    {
        get => _isReading;
        private set
        {
            if (!SetProperty(ref _isReading, value))
                return;

            OnPropertyChanged(nameof(IsListing));
            OnPropertyChanged(nameof(ReadingPositionLabel));
        }
    }

    /// <summary>
    /// True when the inbox itself is on screen: not while a notice is open over it, and not
    /// while the administration area has taken the panel.
    /// </summary>
    public bool IsListing => !IsReading && !IsAdminOpen;

    /// <summary>"9 de 237", the position of the open notice within its tab.</summary>
    public string ReadingPositionLabel
    {
        get
        {
            if (SelectedTab is null || SelectedNotification is null)
                return string.Empty;

            var position = SelectedTab.IndexOf(SelectedNotification);

            if (position < 0)
                return string.Empty;

            return string.Format(
                LocalizationService.Instance["NotificationPosition"],
                position + 1,
                SelectedTab.TotalCount);
        }
    }

    /// <summary>
    /// Opens one notification by its identifier, from outside the panel.
    /// </summary>
    /// <remarks>
    /// What the live alert calls when a dispatcher clicks the card. It lands on the notice
    /// itself, on a tab that actually contains it and on the page it is on — otherwise the
    /// panel opens on a list where the row is not, which reads as if the alert had lied.
    /// </remarks>
    public void ShowNotification(Guid notificationId)
    {
        var item = _all.FirstOrDefault(
            candidate => candidate.NotificationId == notificationId);

        if (item is null)
            return;

        // The administration area covers the panel; a notice opening underneath it would
        // never be seen.
        IsAdminOpen = false;

        var tab = Tabs.FirstOrDefault(candidate => candidate.IndexOf(item) >= 0);

        if (tab is not null && !ReferenceEquals(tab, SelectedTab))
            SelectedTab = tab;

        SelectedTab?.EnsureVisible(item);

        RaisePagination();

        OpenReading(item);
    }

    private void OpenReading(object parameter)
    {
        var item = parameter as NotificationItemViewModel ?? SelectedNotification;

        if (item is null)
            return;

        SelectedNotification = item;

        // Opening a notice reads it, the way a mail client does. Local to this dispatcher:
        // it does not clear the bold for the rest of the office, and it does not
        // acknowledge anything.
        SetRead(item, true);

        IsReading = true;

        _ = Detail.ShowAsync(item);
    }

    private bool CanMove(int step)
    {
        if (!IsReading || SelectedTab is null || SelectedNotification is null)
            return false;

        var current = SelectedTab.IndexOf(SelectedNotification);

        return current >= 0 && SelectedTab.At(current + step) is not null;
    }

    private void Move(int step)
    {
        if (!CanMove(step))
            return;

        var next = SelectedTab.At(
            SelectedTab.IndexOf(SelectedNotification) + step);

        if (next is null)
            return;

        SelectedNotification = next;

        SetRead(next, true);

        // Keep the list underneath on the page the open notice belongs to, so closing the
        // notice does not land the dispatcher somewhere else entirely.
        SelectedTab.EnsureVisible(next);

        RaisePagination();
    }

    #endregion

    #region Shift strip and channel health

    public int PendingActionCount => _notifications.PendingActionCount;

    public bool HasPendingAction => PendingActionCount > 0;

    /// <summary>
    /// The Will Call closest to running out. The one number a dispatcher must be able to
    /// read without opening anything.
    /// </summary>
    public NotificationItemViewModel MostUrgent =>
        _all.Where(item => item.HasCountdown)
            .OrderBy(item => item.WillCallDeadlineUtc)
            .FirstOrDefault();

    public bool HasMostUrgent => MostUrgent is not null;

    public int InProgressCount =>
        _all.Count(item =>
            Is(item, NotificationKeys.Events.DriverStartedTrip) ||
            Is(item, NotificationKeys.Events.DriverArrivedPickup) ||
            Is(item, NotificationKeys.Events.DriverPickedUpPassenger));

    public int CancelledTodayCount =>
        _all.Count(item =>
            Is(item, NotificationKeys.Events.TripCancelled) &&
            item.CreatedAtLocal.Date == DateTime.Today);

    public bool IsChannelHealthy =>
        _notifications.ConnectionState == HubConnectionState.Connected &&
        !IsSuspiciouslyQuiet;

    private bool IsSuspiciouslyQuiet =>
        DateTime.UtcNow - (_notifications.LastReceivedAtUtc ?? _openedAtUtc) > SilenceThreshold;

    public string ChannelStatusText
    {
        get
        {
            if (_notifications.ConnectionState != HubConnectionState.Connected)
                return LocalizationService.Instance["NotificationChannelDisconnected"];

            if (IsSuspiciouslyQuiet)
                return LocalizationService.Instance["NotificationChannelQuiet"];

            return LocalizationService.Instance["NotificationChannelConnected"];
        }
    }

    /// <summary>Hub state and when the last notice arrived, spelled out for an administrator.</summary>
    public string ChannelDiagnosticsText
    {
        get
        {
            var last = _notifications.LastReceivedAtUtc.HasValue
                ? _notifications.LastReceivedAtUtc.Value.ToLocalTime().ToString("g")
                : LocalizationService.Instance["NotificationNeverReceived"];

            return string.Format(
                LocalizationService.Instance["NotificationChannelDiagnostics"],
                _notifications.ConnectionState,
                last,
                _all.Count);
        }
    }

    #endregion

    #region Pagination passthrough

    public string RangeLabel => SelectedTab?.RangeLabel ?? string.Empty;

    private void RaisePagination()
    {
        OnPropertyChanged(nameof(RangeLabel));
        OnPropertyChanged(nameof(ReadingPositionLabel));
        CommandManager.InvalidateRequerySuggested();
    }

    #endregion

    #region Administration

    /// <summary>The four things an administrator can do here, one screen each.</summary>
    /// <remarks>
    /// They were one long scroll before. They are not the same kind of work: watching the
    /// channel is diagnosis, switching a notice off changes what every application shows,
    /// and deleting a kept record destroys evidence. Putting them side by side in one
    /// column made the destructive one as easy to reach as the harmless one.
    /// </remarks>
    public static class AdminSections
    {
        public const string Channel = "CHANNEL";

        public const string Rules = "RULES";

        public const string Archived = "ARCHIVED";

        public const string Audit = "AUDIT";
    }

    public bool IsAdmin => SessionManager.Role == "1";

    /// <summary>True while the administration area has the panel, instead of the inbox.</summary>
    public bool IsAdminOpen
    {
        get => _isAdminOpen;
        private set
        {
            if (!SetProperty(ref _isAdminOpen, value))
                return;

            OnPropertyChanged(nameof(IsListing));
        }
    }

    public string AdminSection
    {
        get => _adminSection;
        private set
        {
            if (SetProperty(ref _adminSection, value))
                RaiseAdminSectionFlags();
        }
    }

    /// <summary>
    /// Tells the rail which item is lit.
    /// </summary>
    /// <remarks>
    /// Raised even when the section did not change. The rail items are toggles bound one
    /// way, so clicking the one already selected switches its own light off; only a fresh
    /// notification puts it back.
    /// </remarks>
    private void RaiseAdminSectionFlags()
    {
        OnPropertyChanged(nameof(IsChannelSection));
        OnPropertyChanged(nameof(IsRulesSection));
        OnPropertyChanged(nameof(IsArchivedSection));
        OnPropertyChanged(nameof(IsAuditSection));
        OnPropertyChanged(nameof(AdminSectionHeader));
    }

    public bool IsChannelSection => AdminSection == AdminSections.Channel;

    public bool IsRulesSection => AdminSection == AdminSections.Rules;

    public bool IsArchivedSection => AdminSection == AdminSections.Archived;

    public bool IsAuditSection => AdminSection == AdminSections.Audit;

    /// <summary>Name of the section on screen, for the header of the area.</summary>
    public string AdminSectionHeader => AdminSection switch
    {
        AdminSections.Rules => RulesSectionTitle,
        AdminSections.Archived => ArchivedSectionTitle,
        AdminSections.Audit => AuditSectionTitle,
        _ => ChannelSectionTitle
    };

    public string RulesCountLabel => string.Format(
        LocalizationService.Instance["NotificationAdminRulesCount"],
        EventToggles.Count);

    public string AuditCountLabel => string.Format(
        LocalizationService.Instance["NotificationAdminAuditCount"],
        Audit.Count);

    public bool HasRules => EventToggles.Count > 0;

    public bool HasArchived => ArchivedGroups.Count > 0;

    public bool HasAudit => Audit.Count > 0;

    public string AdminMessage
    {
        get => _adminMessage;
        private set
        {
            if (SetProperty(ref _adminMessage, value))
                OnPropertyChanged(nameof(HasAdminMessage));
        }
    }

    public bool HasAdminMessage => !string.IsNullOrWhiteSpace(AdminMessage);

    private void OpenAdmin()
    {
        // A notice open underneath would come back when the area closes, on a list the
        // administrator may have just emptied.
        IsReading = false;

        IsAdminOpen = true;

        _ = EnsureSectionLoadedAsync(AdminSection);
    }

    private async Task SelectAdminSectionAsync(object parameter)
    {
        if (parameter is not string section || string.IsNullOrWhiteSpace(section))
            section = AdminSections.Channel;

        AdminSection = section;

        RaiseAdminSectionFlags();

        // The message belongs to whatever was done in the previous section.
        AdminMessage = null;

        await EnsureSectionLoadedAsync(section);
    }

    /// <summary>Fetches a section the first time it is opened, and only then.</summary>
    private async Task EnsureSectionLoadedAsync(string section)
    {
        switch (section)
        {
            case AdminSections.Rules when !_rulesLoaded:
                _rulesLoaded = true;
                await LoadRulesAsync();
                break;

            case AdminSections.Archived when !_archivedLoaded:
                _archivedLoaded = true;
                await LoadArchivedAsync();
                break;

            case AdminSections.Audit when !_auditLoaded:
                _auditLoaded = true;
                await LoadAuditAsync();
                break;
        }
    }

    /// <summary>Asks the server again for whatever is on screen.</summary>
    private Task ReloadAdminSectionAsync() => AdminSection switch
    {
        AdminSections.Rules => LoadRulesAsync(),
        AdminSections.Archived => LoadArchivedAsync(),
        AdminSections.Audit => LoadAuditAsync(),
        _ => RefreshAsync()
    };

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>
    /// Runs the cleanup pass now.
    /// </summary>
    /// <remarks>
    /// ⚠️ Deleting is not reversible, so this asks first and says plainly what it will
    /// take. It only reaches what has been expired long enough that nobody could
    /// legitimately still want it.
    /// </remarks>
    private async Task RunRetentionAsync()
    {
        var confirm = MessageBox.Show(
            LocalizationService.Instance["NotificationRetentionConfirm"],
            LocalizationService.Instance["NotificationRetentionTitle"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        AdminMessage = null;

        try
        {
            var result = await _admin.RunRetentionAsync();

            AdminMessage = string.Format(
                LocalizationService.Instance["NotificationRetentionResult"],
                result.Expired,
                result.Deleted);

            await _notifications.RefreshAsync();
        }
        catch (Exception ex)
        {
            AdminMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    #region Archived records

    public ObservableCollection<ArchivedNotificationGroupDto> ArchivedGroups { get; } = [];

    public ObservableCollection<NotificationAdminAuditDto> Audit { get; } = [];

    private int _archivedTotal;

    /// <summary>
    /// How many records are being kept. Not the sum of the groups: a cancellation reaches
    /// the patient, the office and the driver, and shows under each while being one row.
    /// </summary>
    public int ArchivedTotal
    {
        get => _archivedTotal;
        private set
        {
            if (SetProperty(ref _archivedTotal, value))
                OnPropertyChanged(nameof(ArchivedTotalLabel));
        }
    }

    public string ArchivedTotalLabel => string.Format(
        LocalizationService.Instance["NotificationArchivedTotal"],
        ArchivedTotal);

    private async Task LoadArchivedAsync()
    {
        IsBusy = true;
        AdminMessage = null;

        try
        {
            var archived = await _admin.GetArchivedAsync();

            ArchivedGroups.Clear();

            foreach (var group in archived.Groups)
                ArchivedGroups.Add(group);

            ArchivedTotal = archived.Total;

            OnPropertyChanged(nameof(HasArchived));
        }
        catch (Exception ex)
        {
            AdminMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Deletes one kept record for good.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not reversible. Somebody decided this record was worth keeping, so undoing that
    /// decision is spelled out before it happens and recorded against a name afterwards.
    /// </remarks>
    private async Task DeleteArchivedAsync(object parameter)
    {
        if (parameter is not ArchivedNotificationDto item)
            return;

        var confirm = MessageBox.Show(
            string.Format(
                LocalizationService.Instance["NotificationDeleteArchivedConfirm"],
                item.Title),
            LocalizationService.Instance["NotificationDeleteArchivedTitle"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;

        try
        {
            if (await _admin.DeleteArchivedAsync(item.Id))
            {
                AdminMessage = LocalizationService.Instance["NotificationDeleteArchivedDone"];

                await LoadArchivedAsync();
            }
            else
            {
                AdminMessage = LocalizationService.Instance["NotificationDeleteArchivedFailed"];
            }
        }
        catch (Exception ex)
        {
            AdminMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Deletes every kept record for good.
    /// </summary>
    /// <remarks>
    /// ⚠️ The most destructive action in the panel: it undoes every decision anybody ever
    /// made to keep a notification. It says how many it will take before it takes them.
    /// </remarks>
    private async Task DeleteAllArchivedAsync()
    {
        if (ArchivedTotal == 0)
        {
            AdminMessage = LocalizationService.Instance["NotificationNoArchived"];
            return;
        }

        var confirm = MessageBox.Show(
            string.Format(
                LocalizationService.Instance["NotificationDeleteAllArchivedConfirm"],
                ArchivedTotal),
            LocalizationService.Instance["NotificationDeleteArchivedTitle"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
            return;

        IsBusy = true;

        try
        {
            var deleted = await _admin.DeleteAllArchivedAsync();

            AdminMessage = string.Format(
                LocalizationService.Instance["NotificationDeleteAllArchivedDone"],
                deleted);

            await LoadArchivedAsync();
        }
        catch (Exception ex)
        {
            AdminMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAuditAsync()
    {
        IsBusy = true;
        AdminMessage = null;

        try
        {
            var entries = await _admin.GetAuditAsync();

            Audit.Clear();

            foreach (var entry in entries)
                Audit.Add(entry);

            OnPropertyChanged(nameof(HasAudit));
            OnPropertyChanged(nameof(AuditCountLabel));

            if (Audit.Count == 0)
                AdminMessage = LocalizationService.Instance["NotificationNoAudit"];
        }
        catch (Exception ex)
        {
            AdminMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    /// <summary>Loads which notices are currently switched on.</summary>
    private async Task LoadRulesAsync()
    {
        IsBusy = true;
        AdminMessage = null;

        try
        {
            var rules = await _admin.GetRulesAsync();

            EventToggles.Clear();

            // One event has a rule per audience. The switch works per event, so they are
            // grouped: an event counts as on if any of its rules is.
            foreach (var group in rules
                         .Where(rule => !string.IsNullOrWhiteSpace(rule.BusinessEventCode))
                         .GroupBy(rule => rule.BusinessEventCode)
                         .OrderBy(group => group.Key))
            {
                EventToggles.Add(new NotificationEventToggleViewModel(
                    group.Key,
                    group.Count(),
                    group.Any(rule => rule.IsActive),
                    _admin,
                    _text.ResolveEventName,
                    message => AdminMessage = message));
            }

            OnPropertyChanged(nameof(HasRules));
            OnPropertyChanged(nameof(RulesCountLabel));

            if (EventToggles.Count == 0)
                AdminMessage = LocalizationService.Instance["NotificationNoRules"];
        }
        catch (Exception ex)
        {
            AdminMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region Actions

    private void ToggleRead(object parameter)
    {
        if (parameter is not NotificationItemViewModel item)
            return;

        SetRead(item, !item.IsRead);
    }

    private void SetRead(NotificationItemViewModel item, bool isRead)
    {
        if (item is null || item.IsRead == isRead)
            return;

        _notifications.SetRead(item.NotificationId, isRead);

        item.IsRead = isRead;

        // A grouped row speaks for its whole trip, so reading it reads the story.
        foreach (var member in item.Thread)
        {
            if (member == item)
                continue;

            _notifications.SetRead(member.NotificationId, isRead);
            member.IsRead = isRead;
        }
    }

    /// <summary>
    /// Keeps a notice for good, or lets it go again.
    /// </summary>
    /// <remarks>
    /// ⚠️ Not what a mail client means by archiving, and the difference matters. Here it
    /// means the cleanup will never expire or delete this record — for a notice somebody
    /// will have to answer for later: a trip a patient disputes, a cancellation under
    /// investigation. It is a decision about the record, so it goes to the server and holds
    /// for every application.
    ///
    /// <para>
    /// It does not take the notice off the list. The reading window does not move, so it
    /// still leaves the office inbox twelve hours after it was raised. What changes is that
    /// the row survives, and from then on only an administrator can remove it.
    /// </para>
    /// </remarks>
    private async Task ToggleArchivedAsync(object parameter)
    {
        var item = parameter as NotificationItemViewModel ?? SelectedNotification;

        if (item is null)
            return;

        var keep = !item.IsArchived;

        try
        {
            // A grouped row speaks for its whole trip: keeping the story means keeping all
            // of it, not just the notice that happens to be on top.
            foreach (var member in item.Thread)
            {
                if (member == item)
                    continue;

                await _notifications.SetArchivedAsync(member.NotificationId, keep);
                member.RefreshArchived();
            }

            await _notifications.SetArchivedAsync(item.NotificationId, keep);

            item.RefreshArchived();

            OnPropertyChanged(nameof(SelectedNotification));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                LocalizationService.Instance["NotificationArchive"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Takes charge of the selected notification on behalf of the office.
    /// </summary>
    /// <remarks>
    /// ⚠️ Confirming a Will Call sends the patient a message telling them the office is
    /// arranging their ride. That is a promise made to somebody sitting in a clinic, so it
    /// is spelled out before it goes out and it is never chained to reading or to a bulk
    /// action.
    /// </remarks>
    private async Task AcknowledgeAsync()
    {
        var item = SelectedNotification;

        if (item?.CanAcknowledge != true)
            return;

        if (Is(item, NotificationKeys.Events.WillCallActivated))
        {
            var confirm = MessageBox.Show(
                LocalizationService.Instance["NotificationAcknowledgeWillCallConfirm"],
                LocalizationService.Instance["NotificationAcknowledgeTitle"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (confirm != MessageBoxResult.Yes)
                return;
        }

        try
        {
            await _notifications.AcknowledgeAsync(item.NotificationId);

            item.IsRead = true;
            item.RefreshState();

            RefreshTabs();
            RaiseCounters();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                LocalizationService.Instance["NotificationAcknowledgeTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task RefreshAsync()
    {
        IsBusy = true;

        try
        {
            await _notifications.RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Puts the trip number on the clipboard.
    /// </summary>
    /// <remarks>
    /// ⚠️ The trip number of this business is <c>Trip.TripId</c> — the one a broker or a
    /// funding source quotes on the phone. Not the internal identifier, and not the two
    /// joined with a dot: pasting "418 · A-99213" into somebody else's system finds
    /// nothing, and the dispatcher only discovers that at the other end of a call.
    /// </remarks>
    private void CopyTripNumber()
    {
        var text = string.IsNullOrWhiteSpace(Detail.Trip?.TripId)
            ? SelectedNotification?.BrokerTripNumber
            : Detail.Trip.TripId;

        if (string.IsNullOrWhiteSpace(text))
            return;

        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            // The clipboard can be locked by another application. Not worth a dialog.
            System.Diagnostics.Debug.WriteLine(
                $"Could not copy the trip number: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the current tab to a spreadsheet.
    /// </summary>
    /// <remarks>
    /// For a supervisor reviewing a shift, or for attaching to a complaint about a trip
    /// nobody was told about. Exports what the dispatcher is looking at — the tab and the
    /// search they have applied — rather than the whole inbox, because that is what they
    /// asked to see.
    /// </remarks>
    private void Export()
    {
        var rows = SelectedTab?.All ?? [];

        if (rows.Count == 0)
        {
            MessageBox.Show(
                LocalizationService.Instance["NoDataToExport"],
                LocalizationService.Instance["NoDataInfo"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Excel Workbook|*.xlsx",
            FileName = $"Notifications_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            using var workbook = new XLWorkbook();

            var sheet = workbook.Worksheets.Add("Notifications");

            string[] headers =
            [
                LocalizationService.Instance["NotificationTime"],
                LocalizationService.Instance["NotificationType"],
                LocalizationService.Instance["NotificationTitle"],
                LocalizationService.Instance["NotificationTrip"],
                LocalizationService.Instance["NotificationDeadline"],
                LocalizationService.Instance["NotificationStateColumn"]
            ];

            for (var i = 0; i < headers.Length; i++)
                sheet.Cell(1, i + 1).Value = headers[i];

            var headerRange = sheet.Range(1, 1, 1, headers.Length);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;

            var row = 2;

            foreach (var item in rows)
            {
                sheet.Cell(row, 1).Value = item.CreatedAtLocal;
                sheet.Cell(row, 2).Value = item.EventName;
                sheet.Cell(row, 3).Value = item.Body;
                sheet.Cell(row, 4).Value = item.TripDisplay;
                sheet.Cell(row, 5).Value = item.WillCallDeadlineUtc?.ToLocalTime();
                sheet.Cell(row, 6).Value = item.StatusLabel;

                row++;
            }

            sheet.Columns().AdjustToContents();

            workbook.SaveAs(dialog.FileName);

            MessageBox.Show(
                dialog.FileName,
                LocalizationService.Instance["NotificationExport"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                LocalizationService.Instance["NotificationExport"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    #endregion

    #region Window hosting

    /// <summary>
    /// Whether the panel is currently living in its own window. Set by the host.
    /// </summary>
    public bool IsInOwnWindow
    {
        get => _isInOwnWindow;
        set
        {
            if (!SetProperty(ref _isInOwnWindow, value))
                return;

            OnPropertyChanged(nameof(ToggleWindowText));
            OnPropertyChanged(nameof(ToggleWindowIcon));
        }
    }

    #endregion

    #region Synchronisation with the service

    private void OnNotificationsChanged(object sender, EventArgs e) => Sync();

    /// <summary>
    /// Only the badges move. The list is deliberately left alone: this fires while the grid
    /// is committing the change that marked the row read.
    /// </summary>
    private void OnReadStateChanged(object sender, EventArgs e)
    {
        foreach (var item in _all)
            item.IsRead = _notifications.IsRead(item.NotificationId);

        RefreshTabCounts();
        RaiseCounters();
    }

    private void OnConnectionStateChanged(object sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsChannelHealthy));
        OnPropertyChanged(nameof(ChannelStatusText));
        OnPropertyChanged(nameof(ChannelDiagnosticsText));
    }

    /// <summary>
    /// Reconciles the rows against the service by identifier instead of rebuilding them,
    /// so an arriving notification does not cost the dispatcher their place.
    /// </summary>
    private void Sync()
    {
        var byId = _all.ToDictionary(item => item.NotificationId);

        foreach (var dto in _notifications.Notifications)
        {
            // Kept records are not filtered out here. Archiving in Raphael means the
            // cleanup will never delete the row, not that it leaves anybody's list — it
            // still ages out of the inbox twelve hours after it was raised, like the rest.
            if (byId.TryGetValue(dto.Id, out var existing))
            {
                // ⚠️ Adopt first. A reload hands back new objects, and a row still holding
                // the old one reads a stale acknowledged flag: the Confirm button of a Will
                // Call that was already confirmed stays live.
                existing.Adopt(dto);

                existing.IsRead = _notifications.IsRead(dto.Id);
                existing.RefreshState();
                existing.RefreshArchived();

                byId.Remove(dto.Id);
                continue;
            }

            var item = new NotificationItemViewModel(
                dto,
                _text,
                _notifications.IsRead(dto.Id));

            // A trip already loaded for another notification: no reason to make the search
            // box wait for it to be opened again.
            if (item.TripId is int tripId &&
                _tripCache.TryGetValue(tripId, out var trip))
            {
                item.BrokerTripNumber = trip.TripId;
            }

            _all.Add(item);
        }

        // Whatever is left never came back: expired, or acted on elsewhere.
        foreach (var stale in byId.Values)
        {
            if (SelectedNotification == stale)
            {
                IsReading = false;
                SelectedNotification = null;
            }

            _all.Remove(stale);
        }

        RefreshTabs();
        RaiseCounters();
    }

    private void RefreshTabs()
    {
        foreach (var tab in Tabs)
            tab.Sync(_all, SearchText, GroupByTrip, IsPending);

        RaisePagination();
    }

    private void RefreshTabCounts()
    {
        foreach (var tab in Tabs)
            tab.RefreshCount(IsPending);
    }

    /// <summary>
    /// What a tab badge counts: unread, or waiting for somebody to take charge.
    /// </summary>
    private static bool IsPending(NotificationItemViewModel item) =>
        !item.IsRead || item.RequiresAction;

    private void TickClock()
    {
        foreach (var item in _all)
            item.RefreshCountdown();

        OnPropertyChanged(nameof(MostUrgent));
        OnPropertyChanged(nameof(HasMostUrgent));
        OnPropertyChanged(nameof(IsChannelHealthy));
        OnPropertyChanged(nameof(ChannelStatusText));
        OnPropertyChanged(nameof(ChannelDiagnosticsText));
    }

    private void RaiseCounters()
    {
        OnPropertyChanged(nameof(PendingActionCount));
        OnPropertyChanged(nameof(HasPendingAction));
        OnPropertyChanged(nameof(MostUrgent));
        OnPropertyChanged(nameof(HasMostUrgent));
        OnPropertyChanged(nameof(InProgressCount));
        OnPropertyChanged(nameof(CancelledTodayCount));
        OnPropertyChanged(nameof(PendingActionLabel));
        OnPropertyChanged(nameof(InProgressLabel));
        OnPropertyChanged(nameof(CancelledTodayLabel));
    }

    #endregion

    /// <summary>Stops the clock when the panel is closed, so it cannot outlive the view.</summary>
    public void Close()
    {
        _clock.Stop();

        _notifications.NotificationsChanged -= OnNotificationsChanged;
        _notifications.ReadStateChanged -= OnReadStateChanged;
        _notifications.ConnectionStateChanged -= OnConnectionStateChanged;
    }
}
