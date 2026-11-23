using Raphael.Desktop.Commands;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views.Admin.Employees;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>Which people the Users list shows. Drivers have their own columns and actions.</summary>
    public enum UserTab { All, Drivers, Office, External }

    /// <summary>
    /// Admin → Employees → Users. RE-082: one list filtered in memory — tab by kind of user, search,
    /// active only (on by default) and Provider — so a filter never costs a trip to the server.
    /// Drivers show their pay rate (administrators only), service state and documents.
    /// </summary>
    public class UsersViewModel : BaseViewModel
    {
        private static string T(string key) => LocalizationService.Instance[key];

        #region Translation

        public LocalizationService L => LocalizationService.Instance;

        public string AddUserToolTip => T("AddUserToolTip");
        public string EditUserToolTip => T("EditUserToolTip");
        public string ChangePasswordToolTip => T("ChangePasswordToolTip");
        public string DeleteUserToolTip => T("DeleteUserToolTip");

        public string ColumnHeaderFullName => T("ColumnHeaderFullName");
        public string ColumnHeaderUserName => T("ColumnHeaderUserName");
        public string ColumnHeaderEmail => T("ColumnHeaderEmail");
        public string ColumnHeaderRole => T("ColumnHeaderRole");
        public string ColumnHeaderActive => T("ColumnHeaderActive");
        public string ColumnHeaderPhone => T("users.ColPhone");
        public string ColumnHeaderProvider => T("users.ColProvider");
        public string ColumnHeaderPayRate => T("users.ColPayRate");

        #endregion

        private readonly IUserService _userService = new UserService();
        private readonly IRoleService _roleService = new RoleService();
        private readonly IDriverPayRateService _payRates = new DriverPayRateService();
        private readonly ObservableCollection<User> _all = new();

        /// <summary>Pay is payroll data: only an administrator sees or changes it (the API agrees).</summary>
        public bool CanSeePay => Helpers.TripGuard.IsAdmin;

        /// <summary>A tenant's admin only ever gets its own company's people: no Provider filter to offer.</summary>
        public bool ShowProviderFilter => Helpers.SessionManager.IsMilanesTransport;

        public ICollectionView UsersView { get; }

        private User? _selectedUser;
        public User? SelectedUser
        {
            get => _selectedUser;
            set => SetProperty(ref _selectedUser, value);
        }

        // ---------------------------------------------------------------- filters

        private UserTab _tab = UserTab.All;
        public UserTab Tab
        {
            get => _tab;
            set
            {
                if (!SetProperty(ref _tab, value)) return;
                foreach (var n in new[] { nameof(IsDriversTab), nameof(IsNotDriversTab), nameof(ShowPayColumn),
                                          nameof(IsTabAll), nameof(IsTabDrivers), nameof(IsTabOffice), nameof(IsTabExternal) })
                    OnPropertyChanged(n);
                Refresh();
            }
        }

        public bool IsDriversTab => Tab == UserTab.Drivers;
        public bool IsNotDriversTab => Tab != UserTab.Drivers;
        public bool ShowPayColumn => IsDriversTab && CanSeePay;

        // The tab strip is four radio buttons; each binds its own flag.
        public bool IsTabAll { get => Tab == UserTab.All; set { if (value) Tab = UserTab.All; } }
        public bool IsTabDrivers { get => Tab == UserTab.Drivers; set { if (value) Tab = UserTab.Drivers; } }
        public bool IsTabOffice { get => Tab == UserTab.Office; set { if (value) Tab = UserTab.Office; } }
        public bool IsTabExternal { get => Tab == UserTab.External; set { if (value) Tab = UserTab.External; } }

        private string _search = string.Empty;
        public string Search
        {
            get => _search;
            set { if (SetProperty(ref _search, value ?? string.Empty)) Refresh(); }
        }

        private bool _activeOnly = true;
        public bool ActiveOnly
        {
            get => _activeOnly;
            set { if (SetProperty(ref _activeOnly, value)) Refresh(); }
        }

        public ObservableCollection<ProviderOption> Providers { get; } = new();

        private ProviderOption? _provider;
        public ProviderOption? Provider
        {
            get => _provider;
            set { if (SetProperty(ref _provider, value)) Refresh(); }
        }

        public ICommand SelectTabCommand { get; }
        public ICommand ClearFiltersCommand { get; }

        // Counters on the tabs: what each would show with the search, Active and Provider as they are.
        public int AllCount => Base().Count();
        public int DriversCount => Base().Count(u => u.IsDriver);
        public int OfficeCount => Base().Count(u => !u.IsDriver && !u.IsExternal);
        public int ExternalCount => Base().Count(u => u.IsExternal);
        public int ShownCount => UsersView.Cast<object>().Count();

        public string TabAllText => $"{T("users.TabAll")}  {AllCount}";
        public string TabDriversText => $"{T("users.TabDrivers")}  {DriversCount}";
        public string TabOfficeText => $"{T("users.TabOffice")}  {OfficeCount}";
        public string TabExternalText => $"{T("users.TabExternal")}  {ExternalCount}";
        public string ShownText => string.Format(T("users.Shown"), ShownCount, _all.Count);
        public string EmptyText => _all.Count == 0 ? T("users.Loading") : T("users.NoMatch");
        public bool IsEmpty => ShownCount == 0;

        /// <summary>Every filter except the tab.</summary>
        private IEnumerable<User> Base() => _all.Where(PassesCommon);

        private bool PassesCommon(User u)
        {
            if (ActiveOnly && !u.IsActive) return false;
            if (Provider is { Id: int pid } && u.ProviderId != pid) return false;
            if (Search.Length == 0) return true;
            var q = Search.Trim();
            return Has(u.FullName, q) || Has(u.Username, q) || Has(u.Email, q) || Has(u.PhoneNumber, q) || Has(u.RoleName, q);
        }

        private static bool Has(string? text, string q) => text?.Contains(q, StringComparison.OrdinalIgnoreCase) == true;

        private bool Filter(object o)
        {
            if (o is not User u || !PassesCommon(u)) return false;
            return Tab switch
            {
                UserTab.Drivers => u.IsDriver,
                UserTab.Office => !u.IsDriver && !u.IsExternal,
                UserTab.External => u.IsExternal,
                _ => true
            };
        }

        private void Refresh()
        {
            UsersView.Refresh();
            foreach (var p in new[] { nameof(TabAllText), nameof(TabDriversText), nameof(TabOfficeText), nameof(TabExternalText),
                                      nameof(ShownText), nameof(IsEmpty), nameof(EmptyText) })
                OnPropertyChanged(p);
        }

        // ---------------------------------------------------------------- commands

        public ICommand AddUserCommand { get; }
        public ICommand EditUserCommand { get; }
        public ICommand DeleteUserCommand { get; }
        public ICommand ChangePasswordCommand { get; }
        public ICommand PayRateCommand { get; }
        public ICommand RefreshCommand { get; }

        public UsersViewModel()
        {
            UsersView = CollectionViewSource.GetDefaultView(_all);
            UsersView.Filter = Filter;
            UsersView.SortDescriptions.Add(new SortDescription(nameof(User.FullName), ListSortDirection.Ascending));

            SelectTabCommand = new RelayCommandObject(p => { if (p is UserTab t) Tab = t; else if (Enum.TryParse<UserTab>(p?.ToString(), out var x)) Tab = x; });
            ClearFiltersCommand = new RelayCommandObject(_ => { _search = string.Empty; OnPropertyChanged(nameof(Search)); _provider = null; OnPropertyChanged(nameof(Provider)); ActiveOnly = true; Refresh(); });

            AddUserCommand = new RelayCommandObject(AddUser);
            EditUserCommand = new RelayCommandObject(p => EditUser(Target(p)), p => Target(p) != null);
            DeleteUserCommand = new RelayCommandObject(p => DeleteUser(Target(p)), p => Target(p) != null);
            ChangePasswordCommand = new RelayCommandObject(p => OpenChangePasswordDialog(Target(p)), p => Target(p) != null);
            PayRateCommand = new RelayCommandObject(p => OpenPayRate(Target(p)), p => CanSeePay && Target(p) is { IsDriver: true });
            RefreshCommand = new RelayCommandObject(_ => LoadUsers());

            InitOutOfService();
            WatchFleet();
            LocalizationService.Instance.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);

            LoadUsers();
        }

        /// <summary>The row's own user when a row button passes it; otherwise the selected one.</summary>
        private User? Target(object? p) => p as User ?? SelectedUser;

        private async void LoadUsers()
        {
            try
            {
                var users = await _userService.GetUsersAsync();
                // The three side reads go together, not one after another.
                await Task.WhenAll(MarkOutOfServiceAsync(users), MarkDocumentsAsync(users), MarkPayRatesAsync(users));

                _all.Clear();
                foreach (var u in users) _all.Add(u);
                FillProviders(users);
                Refresh();
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show($"Error loading users: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Only the Providers that have somebody in the list: no empty choices.</summary>
        private void FillProviders(IEnumerable<User> users)
        {
            var keep = Provider?.Id;
            Providers.Clear();
            Providers.Add(new ProviderOption(null, T("users.AllProviders")));
            foreach (var g in users.Where(u => u.ProviderId != null).GroupBy(u => u.ProviderId!.Value)
                                   .Select(g => new ProviderOption(g.Key, g.First().ProviderName ?? $"#{g.Key}"))
                                   .OrderBy(p => p.Name))
                Providers.Add(g);
            _provider = Providers.FirstOrDefault(p => p.Id == keep) ?? Providers[0];
            OnPropertyChanged(nameof(Provider));
        }

        // ---- RE-082: pay rates ----

        private async Task MarkPayRatesAsync(IEnumerable<User> users)
        {
            if (!CanSeePay) return;
            try
            {
                var current = (await _payRates.GetCurrentAsync()).ToDictionary(r => r.UserId);
                foreach (var u in users) u.PayRate = current.GetValueOrDefault(u.Id);
            }
            catch (Exception ex)
            {
                Helpers.FileLogger.Log($"Users: could not read the pay rates. {ex.Message}");
            }
        }

        private async void OpenPayRate(User? user)
        {
            if (user == null) return;
            var dialog = new DriverPayRateView(new DriverPayRateViewModel(user, _payRates)) { Owner = Application.Current.MainWindow };
            if (dialog.ShowDialog() == true)
            {
                await MarkPayRatesAsync(_all);
                UsersView.Refresh();
            }
        }

        // ---- RE-065: compliance documents, managed in Docs; here only their status and the way there ----

        public ICommand OpenDocumentsCommand { get; } = new RelayCommandObject(p => { if (p is User u) Helpers.ComplianceUi.Open("USER", u.Id); });
        public string OpenDocumentsToolTip => T("compliance.OpenDocs");
        public string ColumnHeaderDocuments => T("compliance.ColumnDocs");

        private static async Task MarkDocumentsAsync(IEnumerable<User> users)
        {
            var summaries = await Helpers.ComplianceUi.SummariesAsync("USER");
            if (summaries is null) return;
            foreach (var u in users) u.DocsSummary = summaries.GetValueOrDefault(u.Id);
        }

        // ---- RE-053: Out of Service ----

        public ICommand OutOfServiceCommand { get; private set; } = null!;
        public ICommand ReturnToServiceCommand { get; private set; } = null!;

        public string OutOfServiceToolTip => T("oos.ActionDriver");
        public string ReturnToServiceToolTip => T("oos.ActionReturn");
        public string ColumnHeaderOutOfService => T("oos.Column");
        public string OutOfServiceBadge => T("oos.Badge");
        public string InServiceText => T("users.InService");

        private void InitOutOfService()
        {
            OutOfServiceCommand = new RelayCommandObject(async p => await TakeOutOfServiceAsync(p as User),
                p => p is User x && CanBeOutOfService(x) && !x.IsOutOfService);
            ReturnToServiceCommand = new RelayCommandObject(async p => await ReturnToServiceAsync(p as User),
                p => p is User x && x.IsOutOfService);
        }

        /// <summary>
        /// Live: whoever takes a driver out of service or puts them back, from whichever screen or
        /// computer, this list says so without being reloaded by hand.
        /// </summary>
        private void WatchFleet()
        {
            WeakEventManager<FleetBoard, FleetChangedEventArgs>.AddHandler(FleetBoard.Instance, nameof(FleetBoard.Changed), OnFleetChanged);
            FleetBoard.Instance.EnsureStarted();
        }

        /// <summary>One read of the open records; the users themselves did not change.</summary>
        private async void OnFleetChanged(object? sender, FleetChangedEventArgs e)
        {
            if (_all.Count == 0) return;
            await MarkOutOfServiceAsync(_all);
            UsersView.Refresh();
            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>One read of the open records, put on the rows they belong to.</summary>
        private async Task MarkOutOfServiceAsync(IEnumerable<User> items)
        {
            try
            {
                var open = await new OutOfServiceService().GetActiveAsync();
                foreach (var x in items)
                    x.OutOfServiceRecord = open.FirstOrDefault(r => r.Subject == Views.OutOfServiceDialog.Driver && r.SubjectId == x.Id);
            }
            catch (Exception ex)
            {
                Helpers.FileLogger.Log($"Drivers: could not read what is out of service. {ex.Message}");
            }
        }

        private async Task TakeOutOfServiceAsync(User? x)
        {
            if (x == null) return;
            if (Helpers.OutOfServiceUi.Show(Views.OutOfServiceDialog.ForDriver(x)))
                { await MarkOutOfServiceAsync(_all); UsersView.Refresh(); }
        }

        private async Task ReturnToServiceAsync(User? x)
        {
            if (x?.OutOfServiceRecord == null) return;
            if (await Helpers.OutOfServiceUi.ReturnToServiceAsync(new[] { x.OutOfServiceRecord }, x.FullName))
                { await MarkOutOfServiceAsync(_all); UsersView.Refresh(); }
        }

        /// <summary>Only a driver works a route, and only an active one can be taken out of service.</summary>
        private static bool CanBeOutOfService(User u) => u.IsActive && u.IsDriver;

        // ---- add / edit / delete / password ----

        private async void AddUser(object parameter)
        {
            try
            {
                var availableRoles = await _roleService.GetRolesAsync();
                var integrators = await new IntegratorService().GetIntegratorsAsync();
                var providers = await new ProviderService().GetProvidersAsync();

                var addEditUserView = new AddEditUserView(null, availableRoles, integrators, providers);
                if (Application.Current.MainWindow != addEditUserView)
                    addEditUserView.Owner = Application.Current.MainWindow;
                addEditUserView.WindowStartupLocation = WindowStartupLocation.CenterScreen;

                if (addEditUserView.ShowDialog() == true) LoadUsers();
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show($"Error preparing to add user: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void EditUser(User? user)
        {
            if (user == null) return;
            try
            {
                var availableRoles = await _roleService.GetRolesAsync();
                var integrators = await new IntegratorService().GetIntegratorsAsync();
                var providers = await new ProviderService().GetProvidersAsync();

                var addEditUserView = new AddEditUserView(user, availableRoles, integrators, providers) { Owner = Application.Current.MainWindow };
                if (addEditUserView.ShowDialog() == true) LoadUsers();
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show($"Error preparing to edit user: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteUser(User? user)
        {
            if (user == null) return;
            if (MessageBox.Show(string.Format(T("users.ConfirmDelete"), user.FullName), T("ConfirmDeleteTitle"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                await _userService.DeleteUserAsync(user.Id);
                LoadUsers();
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show($"Error deleting user: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void OpenChangePasswordDialog(User? user)
        {
            if (user == null) return;
            new ChangePasswordView(user.Id).ShowDialog();
        }
    }

    /// <summary>One choice in the Provider filter; a null id means every Provider.</summary>
    public record ProviderOption(int? Id, string Name);
}
