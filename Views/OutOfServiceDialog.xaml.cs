using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.Views
{
    /// <summary>
    /// Takes a vehicle, a driver or both out of service (RE-053).
    /// </summary>
    /// <remarks>
    /// Three steps in one window: why (a reason per subject, and a note), what it does (the
    /// server's preview of every route and trip still pending, with another vehicle or driver per
    /// route as the alternative to taking its trips off), and what was done (with a link to the
    /// unscheduled list of each day, the trips picked out). Every figure is read off the server's
    /// answer, never worked out here. Nothing is saved until the dispatcher confirms twice.
    /// </remarks>
    public partial class OutOfServiceDialog : Window
    {
        public const string Vehicle = "VEHICLE";
        public const string Driver = "DRIVER";
        private const string Other = "OTHER";
        private const string Unrouted = "UNROUTED";
        private const string Onboard = "EXCLUDED_ONBOARD";
        private const string Kept = "KEPT_BY_SUBSTITUTION";

        private static readonly Brush CardBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1)));
        private static readonly Brush CardBorder = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x00)));
        private static readonly Brush DangerBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE)));
        private static readonly Brush Danger = Freeze(new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)));
        private static readonly Brush KeptInk = Freeze(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)));
        private static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F)));
        private static readonly Brush Quiet = Freeze(new SolidColorBrush(Color.FromRgb(0x78, 0x90, 0x9C)));
        private static readonly Brush Primary = Freeze(new SolidColorBrush(Color.FromRgb(0x67, 0x3A, 0xB7)));
        private static readonly Brush Pending = Freeze(new SolidColorBrush(Color.FromRgb(0xB0, 0xBE, 0xC5)));
        private static readonly Brush DoneBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)));

        private static LocalizationService L => LocalizationService.Instance;

        private readonly IOutOfServiceService _service = new OutOfServiceService();
        private readonly VehicleRoute? _route;
        private readonly Vehicle? _vehicle;
        private readonly User? _driver;

        private readonly Dictionary<string, (CheckBox? Pick, ComboBox Reason, TextBox Note)> _subjects = new();
        private readonly Dictionary<int, (ComboBox? Vehicle, ComboBox? Driver)> _substitutions = new();
        private readonly Dictionary<int, (int? Vehicle, int? Driver)> _chosen = new();

        private List<Vehicle> _allVehicles = new();
        private List<User> _allDrivers = new();
        private OutOfServicePreviewDto? _preview;
        private OutOfServiceRequestDto? _previewedRequest;
        private bool _busy;

        /// <summary>Something changed after the preview: it no longer describes what would be done.</summary>
        private bool _previewStale;

        /// <summary>Why the form cannot be previewed yet, as BuildRequest last found it.</summary>
        private string? _problem;

        /// <summary>From Runs: the route's vehicle, its driver or both.</summary>
        public static OutOfServiceDialog ForRoute(VehicleRoute route) => new(route, route.Vehicle, route.Driver);

        public static OutOfServiceDialog ForVehicle(Vehicle vehicle) => new(null, vehicle, null);

        public static OutOfServiceDialog ForDriver(User driver) => new(null, null, driver);

        private OutOfServiceDialog(VehicleRoute? route, Vehicle? vehicle, User? driver)
        {
            InitializeComponent();
            _route = route;
            _vehicle = vehicle;
            _driver = driver;

            Title = L["oos.Title"];
            IntroText.Text = route is not null ? string.Format(L["oos.IntroRoute"], route.Name)
                : vehicle is not null ? string.Format(L["oos.IntroVehicle"], vehicle.Name)
                : string.Format(L["oos.IntroDriver"], driver?.FullName);

            CancelButton.Content = L["Cancel"];
            PreviewButton.Content = L["oos.Preview"];
            ExecuteButton.Content = L["oos.Execute"];
            Step1Text.Text = "1  " + L["oos.Step1"];
            Step2Text.Text = "2  " + L["oos.Step2"];
            Step3Text.Text = "3  " + L["oos.Step3"];

            // Whoever turns the red button on or off, the cursor and the tooltip follow.
            ExecuteButton.IsEnabledChanged += (_, _) => UpdateExecuteHost();
            UpdateGuide();

            // F1 opens the topic about this window.
            HelpAssist.SetTopicId(this, "desktop/data/out-of-service");

            Loaded += async (_, _) => await LoadAsync();
        }

        /// <summary>The trips sent back, and their days, once it is done. Null until then.</summary>
        public OutOfServicePreviewDto? Result { get; private set; }

        private async Task LoadAsync()
        {
            try
            {
                var reasons = await _service.GetReasonsAsync();

                if (_vehicle is not null) AddSubject(Vehicle, string.Format(L["oos.SubjectVehicle"], _vehicle.Name), reasons.Vehicle, reasons.NoteMaxLength);
                if (_driver is not null) AddSubject(Driver, string.Format(L["oos.SubjectDriver"], _driver.FullName), reasons.Driver, reasons.NoteMaxLength);

                // The substitutes: in service, active, and a driver only among drivers.
                var vehicles = new VehicleService().GetVehiclesAsync();
                var users = new UserService().GetAllAsync();
                var active = await _service.GetActiveAsync();
                var outVehicles = active.Where(a => a.Subject == Vehicle).Select(a => a.SubjectId).ToHashSet();
                var outDrivers = active.Where(a => a.Subject == Driver).Select(a => a.SubjectId).ToHashSet();

                _allVehicles = (await vehicles).Where(v => !v.IsInactive && !outVehicles.Contains(v.Id) && v.Id != _vehicle?.Id)
                    .OrderBy(v => v.Name).ToList();
                _allDrivers = (await users).Where(u => u.IsActive && !outDrivers.Contains(u.Id) && u.Id != _driver?.Id
                                                       && (u.RoleName ?? string.Empty).Contains("Driver", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(u => u.FullName).ToList();
            }
            catch (Exception ex)
            {
                ShowNotice(ex.Message);
            }

            UpdateGuide();
        }

        private void AddSubject(string subject, string label, List<string> reasons, int noteMax)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            CheckBox? pick = null;
            if (_route is not null)
            {
                // From a route the dispatcher says which of the two is out: the vehicle, the driver or both.
                pick = new CheckBox { Content = label, FontWeight = FontWeights.Bold, IsChecked = subject == Vehicle };
                // A substitute chosen for what is no longer out would replace something that works.
                pick.Checked += (_, _) => { _chosen.Clear(); InvalidatePreview(); UpdateSubjectState(); UpdateGuide(); };
                pick.Unchecked += (_, _) => { _chosen.Clear(); InvalidatePreview(); UpdateSubjectState(); UpdateGuide(); };
                panel.Children.Add(pick);
            }
            else
            {
                panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.Bold, Foreground = Ink });
            }

            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var reason = new ComboBox { Width = 260, Margin = new Thickness(0, 0, 10, 0), DisplayMemberPath = "Text", SelectedValuePath = "Code" };
            reason.ItemsSource = reasons.Select(code => new { Code = code, Text = L["oos.reason." + code] }).ToList();
            reason.SelectionChanged += (_, _) => { InvalidatePreview(); UpdateGuide(); };
            materialDesignHint(reason, L["oos.Reason"]);
            DockPanel.SetDock(reason, Dock.Left);
            row.Children.Add(reason);

            var note = new TextBox { MaxLength = noteMax, TextWrapping = TextWrapping.Wrap };
            materialDesignHint(note, L["oos.Note"]);
            note.TextChanged += (_, _) => { InvalidatePreview(); UpdateGuide(); };
            row.Children.Add(note);
            panel.Children.Add(row);

            // ⚠️ A driver's reason is about an employee's health or HR file.
            if (subject == Driver)
                panel.Children.Add(new TextBlock { Text = L["oos.DriverNoteWarning"], Foreground = Quiet, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });

            SubjectsPanel.Children.Add(panel);
            _subjects[subject] = (pick, reason, note);
            UpdateSubjectState();
        }

        private void UpdateSubjectState()
        {
            foreach (var (pick, reason, note) in _subjects.Values)
            {
                var on = pick?.IsChecked != false;
                reason.IsEnabled = on;
                note.IsEnabled = on;
            }
        }

        private static void materialDesignHint(DependencyObject control, string hint) =>
            MaterialDesignThemes.Wpf.HintAssist.SetHint(control, hint);

        // ---- Preview ----

        private OutOfServiceRequestDto? BuildRequest(bool showErrors)
        {
            var request = new OutOfServiceRequestDto { OriginVehicleRouteId = _route?.Id };

            foreach (var (subject, (pick, reason, note)) in _subjects)
            {
                if (pick?.IsChecked == false) continue;

                var id = subject == Vehicle ? _vehicle!.Id : _driver!.Id;
                var code = reason.SelectedValue as string;

                if (code is null) return Fail(L["oos.ChooseReason"], showErrors);

                if (code == Other && string.IsNullOrWhiteSpace(note.Text)) return Fail(L["oos.OtherNeedsNote"], showErrors);

                request.Subjects.Add(new OutOfServiceSubjectDto
                {
                    Subject = subject,
                    SubjectId = id,
                    ReasonCode = code,
                    ReasonNote = string.IsNullOrWhiteSpace(note.Text) ? null : note.Text.Trim()
                });
            }

            if (request.Subjects.Count == 0) return Fail(L["oos.ChooseSubject"], showErrors);

            foreach (var (routeId, (vehicle, driver)) in _chosen)
            {
                if (vehicle is null && driver is null) continue;
                request.Substitutions.Add(new OutOfServiceSubstitutionDto { VehicleRouteId = routeId, NewVehicleId = vehicle, NewDriverId = driver });
            }

            _problem = null;
            return request;
        }

        private OutOfServiceRequestDto? Fail(string problem, bool showErrors)
        {
            _problem = problem;
            if (showErrors) ShowNotice(problem);
            return null;
        }

        private async void OnPreview(object sender, RoutedEventArgs e) => await PreviewAsync(notice: null);

        private async Task PreviewAsync(string? notice)
        {
            if (_busy) return;
            var request = BuildRequest(showErrors: true);
            if (request is null) return;

            await RunAsync(async () =>
            {
                var preview = await _service.PreviewAsync(request);
                _preview = preview;
                _previewedRequest = request;
                RenderPreview(preview);
                _previewStale = false;
                ExecuteButton.IsEnabled = true;
                PreviewButton.Content = L["oos.RefreshPreview"];
                if (notice is not null) ShowNotice(notice); else HideNotice();
            });
        }

        /// <summary>Anything changed in the form: the preview no longer describes what would be done.</summary>
        private void InvalidatePreview()
        {
            if (_preview is null) return;
            _previewStale = true;
            ExecuteButton.IsEnabled = false;
            SummaryText.Text = L["oos.PreviewOutdated"];
        }

        // ---- Guide ----

        /// <summary>
        /// Which of the three steps the dispatcher is on, and what is missing, in words.
        /// </summary>
        /// <remarks>
        /// The red button only wakes up after the preview. Without saying so, a disabled red
        /// button reads as a field left empty somewhere, and nothing on the form is.
        /// </remarks>
        private void UpdateGuide()
        {
            if (Result is not null)
            {
                StepsPanel.Visibility = Visibility.Collapsed;
                GuideBox.Visibility = Visibility.Collapsed;
                UpdateExecuteHost();
                return;
            }

            var formReady = BuildRequest(showErrors: false) is not null;
            var step = !formReady ? 1 : _preview is null || _previewStale ? 2 : 3;

            GuideText.Text = step switch
            {
                1 => (_problem ?? L["oos.ChooseSubject"]) + " " + L["oos.GuideThenPreview"],
                2 when _previewStale => L["oos.GuideRefresh"],
                2 => L["oos.GuidePreview"],
                _ => L["oos.GuideReady"]
            };

            PaintStep(Step1, Step1Text, 1, step);
            PaintStep(Step2, Step2Text, 2, step);
            PaintStep(Step3, Step3Text, 3, step);

            // The preview is the next thing to press: it looks like it.
            PreviewButton.Style = (Style)FindResource(step == 2 ? "MaterialDesignRaisedButton" : "MaterialDesignOutlinedButton");

            UpdateExecuteHost();
        }

        private static void PaintStep(Border chip, TextBlock text, int which, int current)
        {
            if (which < current)
            {
                chip.Background = DoneBackground;
                chip.BorderBrush = KeptInk;
                text.Foreground = KeptInk;
            }
            else if (which == current)
            {
                chip.Background = Primary;
                chip.BorderBrush = Primary;
                text.Foreground = Brushes.White;
            }
            else
            {
                chip.Background = Brushes.Transparent;
                chip.BorderBrush = Pending;
                text.Foreground = Quiet;
            }
        }

        /// <summary>A "no" cursor and a tooltip that says why, while the red button is off.</summary>
        private void UpdateExecuteHost()
        {
            var off = !ExecuteButton.IsEnabled && Result is null;
            ExecuteHost.Cursor = off ? System.Windows.Input.Cursors.No : null;
            ExecuteHost.ToolTip = !off ? null
                : _busy ? L["oos.ExecuteBusy"]
                : L["oos.ExecuteLocked"] + Environment.NewLine + GuideText.Text;
        }

        private void RenderPreview(OutOfServicePreviewDto preview)
        {
            RoutesPanel.Children.Clear();
            _substitutions.Clear();

            if (preview.Routes.Count == 0)
                RoutesPanel.Children.Add(Paragraph(L["oos.NoRoutes"]));

            foreach (var route in preview.Routes)
                RoutesPanel.Children.Add(RouteCard(route));

            SummaryText.Text = Summary(preview);
        }

        private string Summary(OutOfServicePreviewDto p)
        {
            var text = string.Format(L["oos.SummaryUnrouted"], p.TripsUnrouted, p.EventsUnrouted);
            if (p.TripsKept > 0) text += " " + string.Format(L["oos.SummaryKept"], p.TripsKept);
            if (p.TripsOnboard > 0) text += " " + string.Format(L["oos.SummaryOnboard"], p.TripsOnboard);
            return text;
        }

        private UIElement RouteCard(OutOfServiceRouteDto route)
        {
            var panel = new StackPanel();

            panel.Children.Add(new TextBlock
            {
                Text = string.Format(L["oos.RouteHeader"], route.RouteName, route.VehicleName, route.DriverName),
                FontWeight = FontWeights.Bold, Foreground = Ink, TextWrapping = TextWrapping.Wrap
            });

            if (Result is null) panel.Children.Add(SubstitutionRow(route));

            if (route.Trips.Count == 0)
                panel.Children.Add(Paragraph(L["oos.RouteNoTrips"]));

            foreach (var day in route.Trips.GroupBy(t => t.Date.Date).OrderBy(g => g.Key))
            {
                panel.Children.Add(new TextBlock { Text = day.Key.ToString("D"), FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0, 8, 0, 2) });

                foreach (var trip in day)
                {
                    var (label, brush) = trip.Outcome switch
                    {
                        Onboard => (L["oos.OutcomeOnboard"], Danger),
                        Kept => (L["oos.OutcomeKept"], KeptInk),
                        _ => (L["oos.OutcomeUnrouted"], Ink)
                    };

                    var line = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 2, 0, 0), Foreground = brush };
                    line.Inlines.Add(new Run(string.Format(L["oos.TripLine"], trip.TripId, trip.PatientName)) { FontWeight = FontWeights.SemiBold });
                    line.Inlines.Add(new Run("  — " + label));
                    panel.Children.Add(line);

                    foreach (var ev in trip.Events)
                    {
                        panel.Children.Add(new TextBlock
                        {
                            Text = string.Format(L["oos.EventLine"], ev.EventType, Hour(ev.ScheduledTime), Hour(ev.Eta), ev.Address)
                                   + (ev.Performed ? "  ✓ " + L["oos.Performed"] : string.Empty),
                            TextWrapping = TextWrapping.Wrap, Foreground = ev.Performed ? Quiet : Ink, FontSize = 12,
                            Margin = new Thickness(24, 1, 0, 0)
                        });
                    }
                }
            }

            var onboard = route.Trips.Any(t => t.Outcome == Onboard);
            return new Border
            {
                Background = onboard ? DangerBackground : CardBackground,
                BorderBrush = onboard ? Danger : CardBorder,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Child = panel
            };
        }

        /// <summary>
        /// Another vehicle and/or driver for this route: the alternative to taking its trips off.
        /// Only the one that is out can be replaced; the route keeps the other.
        /// </summary>
        private UIElement SubstitutionRow(OutOfServiceRouteDto route)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 2) };
            row.Children.Add(new TextBlock { Text = L["oos.Substitute"], VerticalAlignment = VerticalAlignment.Center, Foreground = Ink, Margin = new Thickness(0, 0, 8, 0) });

            _chosen.TryGetValue(route.VehicleRouteId, out var chosen);
            ComboBox? vehicleBox = null, driverBox = null;

            if (IsOut(Vehicle) && route.VehicleId == _vehicle?.Id)
            {
                vehicleBox = Picker(L["oos.NoSubstituteVehicle"], _allVehicles.Select(v => (v.Id, v.Name)), chosen.Vehicle);
                vehicleBox.SelectionChanged += (_, _) => Choose(route.VehicleRouteId);
                row.Children.Add(vehicleBox);
            }

            if (IsOut(Driver) && route.DriverId == _driver?.Id)
            {
                driverBox = Picker(L["oos.NoSubstituteDriver"], _allDrivers.Select(d => (d.Id, d.FullName)), chosen.Driver);
                driverBox.SelectionChanged += (_, _) => Choose(route.VehicleRouteId);
                row.Children.Add(driverBox);
            }

            _substitutions[route.VehicleRouteId] = (vehicleBox, driverBox);
            return row;
        }

        private bool IsOut(string subject) =>
            _subjects.TryGetValue(subject, out var s) && s.Pick?.IsChecked != false;

        private static ComboBox Picker(string none, IEnumerable<(int Id, string Name)> items, int? selected)
        {
            var list = new List<KeyValuePair<int?, string>> { new(null, none) };
            list.AddRange(items.Select(i => new KeyValuePair<int?, string>(i.Id, i.Name)));
            return new ComboBox
            {
                Width = 220, Margin = new Thickness(0, 0, 8, 0), ItemsSource = list,
                DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = selected
            };
        }

        private void Choose(int routeId)
        {
            var (vehicle, driver) = _substitutions[routeId];
            _chosen[routeId] = (vehicle?.SelectedValue as int?, driver?.SelectedValue as int?);
            InvalidatePreview();
        }

        // ---- Execute ----

        private async void OnExecute(object sender, RoutedEventArgs e)
        {
            if (_busy || _preview is null || _previewedRequest is null) return;

            var request = BuildRequest(showErrors: true);
            if (request is null) return;

            var who = string.Join(" + ", request.Subjects.Select(s => s.Subject == Vehicle
                ? string.Format(L["oos.SubjectVehicle"], _vehicle!.Name)
                : string.Format(L["oos.SubjectDriver"], _driver!.FullName)));

            var confirm = MessageBox.Show(this,
                string.Format(L["oos.ConfirmText"], who) + Environment.NewLine + Environment.NewLine + Summary(_preview),
                L["oos.ConfirmTitle"], MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (confirm != MessageBoxResult.Yes) return;

            request.Fingerprint = _preview.Fingerprint;

            await RunAsync(async () =>
            {
                try
                {
                    var result = await _service.ExecuteAsync(request);
                    ShowResult(result);
                }
                catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict && _preview is not null)
                {
                    // The trips moved under the preview: nothing was done. Show what is true now.
                    _busy = false;
                    await PreviewAsync(ex.Message);
                }
            });
        }

        private void ShowResult(OutOfServicePreviewDto result)
        {
            Result = result;
            UpdateGuide();
            Title = L["oos.DoneTitle"];
            SubjectsPanel.Visibility = Visibility.Collapsed;
            PreviewButton.Visibility = Visibility.Collapsed;
            ExecuteButton.Visibility = Visibility.Collapsed;
            CancelButton.Content = L["oos.Close"];
            HideNotice();

            IntroText.Text = string.Format(L["oos.DoneUnrouted"], result.TripsUnrouted)
                + (result.TripsKept > 0 ? " " + string.Format(L["oos.SummaryKept"], result.TripsKept) : string.Empty);

            if (result.TripsOnboard > 0)
                ShowNotice(string.Format(L["oos.DoneOnboard"], result.TripsOnboard));

            RoutesPanel.Children.Clear();

            var unroutedIds = result.Routes.SelectMany(r => r.Trips).Where(t => t.Outcome == Unrouted).Select(t => t.TripId).ToList();

            foreach (var day in result.UnroutedDates)
            {
                var link = new Hyperlink(new Run(string.Format(L["oos.OpenUnscheduled"], day.ToString("D"))));
                var date = day;
                link.Click += (_, _) =>
                {
                    AppNavigation.ShowUnscheduled(date, unroutedIds);
                    DialogResult = true;
                };
                RoutesPanel.Children.Add(new TextBlock(link) { Margin = new Thickness(0, 0, 0, 6), FontSize = 14 });
            }

            foreach (var route in result.Routes)
                RoutesPanel.Children.Add(RouteCard(route));

            SummaryText.Text = string.Empty;
        }

        // ---- Plumbing ----

        private async Task RunAsync(Func<Task> work)
        {
            _busy = true;
            PreviewButton.IsEnabled = false;
            ExecuteButton.IsEnabled = false;
            Cursor = System.Windows.Input.Cursors.Wait;
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                ShowNotice(ex.Message);
            }
            finally
            {
                _busy = false;
                Cursor = null;
                PreviewButton.IsEnabled = true;
                ExecuteButton.IsEnabled = _preview is not null && Result is null && !_previewStale;
                UpdateGuide();
            }
        }

        private void ShowNotice(string text)
        {
            NoticeText.Text = text;
            NoticeBox.Visibility = Visibility.Visible;
        }

        private void HideNotice() => NoticeBox.Visibility = Visibility.Collapsed;

        private static TextBlock Paragraph(string text) =>
            new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Ink, Margin = new Thickness(0, 2, 0, 0) };

        /// <summary>An hour as the Schedule grid prints it; a dash for none.</summary>
        private static string Hour(TimeSpan? t) => t.HasValue ? t.Value.ToString(@"hh\:mm") : "—";

        private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = Result is not null;

        private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    }
}
