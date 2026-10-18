using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using Raphael.Desktop.DTOs.Places;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.Views
{
    /// <summary>
    /// The notes of one trip (RE-054): what the driver reads at each stop, this trip's own note, the
    /// entrance's and the place's notes, and the patient's.
    /// </summary>
    /// <remarks>
    /// Every change is saved at once and the server answers with the whole window again, which is
    /// redrawn from that answer: the screen never works out what the driver will read. A note of the
    /// place or of the patient reaches every trip still to come; this trip's own note, only this one.
    /// </remarks>
    public partial class TripNotesDialog : Window
    {
        public const string Pickup = "Pickup";
        public const string Dropoff = "Dropoff";

        private static readonly Brush Ink = Freeze(new SolidColorBrush(Color.FromRgb(0x37, 0x47, 0x4F)));
        private static readonly Brush Quiet = Freeze(new SolidColorBrush(Color.FromRgb(0x78, 0x90, 0x9C)));
        private static readonly Brush CardBorder = Freeze(new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC)));
        private static readonly Brush DriverBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)));
        private static readonly Brush DriverBorder = Freeze(new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A)));
        private static readonly Brush ReviewBackground = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xE1)));
        private static readonly Brush ReviewBorder = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xA0, 0x00)));
        private static readonly Brush ReviewInk = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)));
        private static readonly Brush Danger = Freeze(new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)));
        private static readonly Brush Primary = Freeze(new SolidColorBrush(Color.FromRgb(0x67, 0x3A, 0xB7)));

        private static LocalizationService L => LocalizationService.Instance;

        private readonly ITripNotesService _service = new TripNotesService();
        private readonly int _tripId;
        private TripNotesDto? _notes;
        private bool _busy;

        /// <param name="tripId">The trip, already saved.</param>
        /// <param name="side"><see cref="Pickup"/>, <see cref="Dropoff"/> or null for the patient's tab.</param>
        public TripNotesDialog(int tripId, string? side)
        {
            InitializeComponent();
            _tripId = tripId;
            Owner = Application.Current?.MainWindow;

            Title = L["notes.Title"];
            PickupTab.Header = L["notes.TabPickup"];
            DropoffTab.Header = L["notes.TabDropoff"];
            PatientTab.Header = L["notes.TabPatient"];
            CloseButton.Content = L["notes.Close"];
            Tabs.SelectedItem = side == Pickup ? PickupTab : side == Dropoff ? DropoffTab : PatientTab;

            // F1 opens the topic about this window.
            HelpAssist.SetTopicId(this, "desktop/home/trip-notes");

            Loaded += async (_, _) => await RunAsync(() => _service.GetAsync(_tripId));
        }

        /// <summary>The last answer from the server, so the form can show the composed comments. Null if it never loaded.</summary>
        public TripNotesDto? Result => _notes;

        /// <summary>Whether anything was saved while the window was open.</summary>
        public bool Changed { get; private set; }

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        // =========================================================================================
        // Server round trips
        // =========================================================================================

        private async Task RunAsync(Func<Task<TripNotesDto>> call, bool isChange = false)
        {
            if (_busy) return;
            _busy = true;
            BusyText.Text = L["notes.Saving"];
            ErrorText.Visibility = Visibility.Collapsed;
            try
            {
                _notes = await call();
                if (isChange) Changed = true;
                Render();
            }
            catch (ApiException ex)
            {
                ErrorText.Text = ex.Message;
                ErrorText.Visibility = Visibility.Visible;
            }
            finally
            {
                _busy = false;
                BusyText.Text = string.Empty;
            }
        }

        private Task SaveAsync(Func<Task<TripNotesDto>> call) => RunAsync(call, isChange: true);

        // =========================================================================================
        // Drawing
        // =========================================================================================

        private void Render()
        {
            if (_notes == null) return;

            HeaderText.Text = string.Format(L["notes.Header"], _notes.CustomerName ?? string.Empty);
            SubHeaderText.Text = _notes.TripReadOnly ? L["notes.ReadOnlyTrip"] : L["notes.SubHeader"];
            ReviewBadge.Visibility = _notes.NotesToReview > 0 ? Visibility.Visible : Visibility.Collapsed;
            ReviewBadgeText.Text = string.Format(L["notes.ToReviewCount"], _notes.NotesToReview);

            RenderSide(PickupPanel, _notes.Pickup, Pickup);
            RenderSide(DropoffPanel, _notes.Dropoff, Dropoff);
            RenderPatient();
        }

        private void RenderSide(StackPanel panel, TripStopNotesDto stop, string side)
        {
            panel.Children.Clear();

            // 1. What the driver reads at this stop: the reason this window exists.
            panel.Children.Add(Section(L["notes.DriverReads"], "Steering"));
            panel.Children.Add(new Border
            {
                Background = DriverBackground, BorderBrush = DriverBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 14),
                Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(stop.Composed) ? L["notes.NothingYet"] : stop.Composed.Replace(" | ", Environment.NewLine),
                    TextWrapping = TextWrapping.Wrap, Foreground = Ink, FontSize = 13,
                },
            });

            // 2. The place.
            panel.Children.Add(Section(L["notes.Place"], "MapMarker"));
            panel.Children.Add(PlaceCard(stop, side));

            // 3. This trip only.
            panel.Children.Add(Section(L["notes.ThisTripOnly"], "CalendarToday"));
            panel.Children.Add(TripNoteEditor(stop, side));

            if (stop.Place != null)
            {
                // 4. This entrance.
                if (stop.Place.EntranceId != null)
                {
                    panel.Children.Add(Section(string.Format(L["notes.EntranceNotes"], EntranceName(stop.Place)), "Door"));
                    foreach (var note in stop.EntranceNotes) panel.Children.Add(NoteRow(note, isCustomer: false, side, forEntrance: true));
                    panel.Children.Add(AddNoteBox(side, forEntrance: true));
                }

                // 5. The whole place.
                panel.Children.Add(Section(string.Format(L["notes.PlaceNotes"], stop.Place.Name), "OfficeBuildingMarker"));
                foreach (var note in stop.PlaceNotes) panel.Children.Add(NoteRow(note, isCustomer: false, side, forEntrance: false));
                panel.Children.Add(AddNoteBox(side, forEntrance: false));
            }

            // 6. What the file brought, untouched: the only text the clinic and the Rider app see.
            if (!string.IsNullOrWhiteSpace(stop.FileComment))
            {
                panel.Children.Add(Section(L["notes.FromFile"], "FileDocumentOutline"));
                panel.Children.Add(new TextBlock
                {
                    Text = stop.FileComment, TextWrapping = TextWrapping.Wrap, Foreground = Quiet, Margin = new Thickness(0, 0, 0, 8),
                });
            }
        }

        private void RenderPatient()
        {
            PatientPanel.Children.Clear();
            PatientPanel.Children.Add(new TextBlock
            {
                Text = L["notes.PatientIntro"], TextWrapping = TextWrapping.Wrap, Foreground = Quiet, Margin = new Thickness(0, 0, 0, 10),
            });
            PatientPanel.Children.Add(Section(string.Format(L["notes.PatientNotes"], _notes!.CustomerName ?? string.Empty), "AccountHeart"));
            foreach (var note in _notes.CustomerNotes) PatientPanel.Children.Add(NoteRow(note, isCustomer: true, side: null, forEntrance: false));
            PatientPanel.Children.Add(AddNoteBox(side: null, forEntrance: false));
        }

        private UIElement PlaceCard(TripStopNotesDto stop, string side)
        {
            var card = new Border
            {
                BorderBrush = CardBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 14),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            card.Child = grid;

            var info = new StackPanel();
            grid.Children.Add(info);
            if (stop.Place == null)
            {
                info.Children.Add(new TextBlock { Text = L["notes.NoPlace"], Foreground = Quiet, TextWrapping = TextWrapping.Wrap });
                return card;
            }

            var kind = stop.Place.Kind == "Home" ? L["notes.KindHome"] : L["notes.KindFacility"];
            info.Children.Add(new TextBlock { Text = stop.Place.Name, FontWeight = FontWeights.SemiBold, Foreground = Ink, TextWrapping = TextWrapping.Wrap });
            info.Children.Add(new TextBlock { Text = $"{kind} · {stop.Place.Address}", Foreground = Quiet, TextWrapping = TextWrapping.Wrap });
            if (stop.Place.EntranceId != null)
                info.Children.Add(new TextBlock
                {
                    Text = string.Format(L["notes.EntranceOf"], EntranceName(stop.Place), stop.Place.EntranceCount),
                    Foreground = Quiet, TextWrapping = TextWrapping.Wrap,
                });

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            actions.Children.Add(IconButton("Pencil", L["notes.RenamePlace"], async () =>
            {
                var name = Ask(L["notes.RenamePlace"], L["notes.RenamePlacePrompt"], stop.Place.Name);
                if (name != null) await SaveAsync(() => _service.UpdatePlaceAsync(_tripId, side, new PlaceUpdateDto { Name = name }));
            }));
            if (stop.Place.EntranceId != null)
                actions.Children.Add(IconButton("Door", L["notes.NameEntrance"], async () =>
                {
                    var label = Ask(L["notes.NameEntrance"], L["notes.NameEntrancePrompt"], stop.Place.EntranceLabel ?? string.Empty);
                    if (label != null) await SaveAsync(() => _service.UpdatePlaceAsync(_tripId, side, new PlaceUpdateDto { EntranceLabel = label }));
                }));
            if (_notes is { TripReadOnly: false })
                actions.Children.Add(IconButton("SwapHorizontal", L["notes.ChangePlace"], () => ChangePlaceAsync(side)));
            return card;
        }

        private UIElement TripNoteEditor(TripStopNotesDto stop, string side)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            var box = new TextBox
            {
                Text = stop.TripNote ?? string.Empty, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 54,
                MaxLength = 2000, IsReadOnly = _notes!.TripReadOnly,
                Style = (Style)FindResource("MaterialDesignOutlinedTextBox"),
            };
            HintAssist.SetHint(box, L["notes.TripNoteHint"]);
            panel.Children.Add(box);

            if (_notes.TripReadOnly) return panel;

            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            buttons.Children.Add(TextButton(L["notes.SaveTripNote"], async () =>
                await SaveAsync(() => _service.SetTripNoteAsync(_tripId, side, box.Text)), raised: true));
            if (!string.IsNullOrWhiteSpace(stop.TripNote))
            {
                if (stop.Place != null)
                    buttons.Children.Add(TextButton(L["notes.KeepInPlace"], async () =>
                        await SaveAsync(() => _service.PromoteTripNoteAsync(_tripId, side, "Place", null))));
                buttons.Children.Add(TextButton(L["notes.KeepInPatient"], async () =>
                    await SaveAsync(() => _service.PromoteTripNoteAsync(_tripId, side, "Customer", null))));
            }
            panel.Children.Add(buttons);
            return panel;
        }

        private UIElement NoteRow(NoteDto note, bool isCustomer, string? side, bool forEntrance)
        {
            var toReview = note.Status == "ToReview";
            var row = new Border
            {
                Background = toReview ? ReviewBackground : Brushes.Transparent,
                BorderBrush = toReview ? ReviewBorder : CardBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 6, 6), Margin = new Thickness(0, 0, 0, 6),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Child = grid;

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = note.Text, TextWrapping = TextWrapping.Wrap, Foreground = Ink });
            var who = note.UpdatedBy ?? note.CreatedBy;
            var when = (note.UpdatedAtUtc ?? note.CreatedAtUtc).ToLocalTime();
            text.Children.Add(new TextBlock
            {
                Text = $"{SourceName(note.Source)} · {who} · {when:g}" + (toReview ? "  ·  " + L["notes.ToReview"] : string.Empty),
                FontSize = 11, Foreground = toReview ? ReviewInk : Quiet, TextWrapping = TextWrapping.Wrap,
            });
            grid.Children.Add(text);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            NoteUpsertDto Upsert(string text, string status) => new() { Text = text, Status = status, ForEntrance = forEntrance };

            if (toReview)
                actions.Children.Add(IconButton("CheckBold", L["notes.Confirm"], async () =>
                    await SaveAsync(() => isCustomer
                        ? _service.UpdateCustomerNoteAsync(_tripId, note.Id, Upsert(note.Text, "Active"))
                        : _service.UpdatePlaceNoteAsync(_tripId, note.Id, Upsert(note.Text, "Active")))));

            actions.Children.Add(IconButton("Pencil", L["notes.Edit"], async () =>
            {
                var edited = Ask(L["notes.Edit"], L["notes.EditPrompt"], note.Text);
                if (edited == null || edited == note.Text) return;
                // Editing is reviewing: an edited note is confirmed.
                await SaveAsync(() => isCustomer
                    ? _service.UpdateCustomerNoteAsync(_tripId, note.Id, Upsert(edited, "Active"))
                    : _service.UpdatePlaceNoteAsync(_tripId, note.Id, Upsert(edited, "Active")));
            }));

            actions.Children.Add(IconButton("DeleteOutline", L["notes.Remove"], async () =>
            {
                var answer = MessageBox.Show(this, string.Format(L["notes.RemoveConfirm"], note.Text), L["notes.Remove"],
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes) return;
                await SaveAsync(() => isCustomer
                    ? _service.RemoveCustomerNoteAsync(_tripId, note.Id)
                    : _service.RemovePlaceNoteAsync(_tripId, note.Id));
            }, Danger));

            return row;
        }

        private UIElement AddNoteBox(string? side, bool forEntrance)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 16) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var box = new TextBox { MaxLength = 500, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            HintAssist.SetHint(box, side == null ? L["notes.AddPatientHint"] : forEntrance ? L["notes.AddEntranceHint"] : L["notes.AddPlaceHint"]);
            grid.Children.Add(box);

            async Task AddAsync()
            {
                var text = box.Text.Trim();
                if (text.Length == 0) return;
                var dto = new NoteUpsertDto { Text = text, Status = "Active", ForEntrance = forEntrance };
                await SaveAsync(() => side == null ? _service.AddCustomerNoteAsync(_tripId, dto) : _service.AddPlaceNoteAsync(_tripId, side, dto));
            }

            box.KeyDown += async (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; await AddAsync(); }
            };
            var add = TextButton(L["notes.Add"], AddAsync, raised: true);
            add.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(add, 1);
            grid.Children.Add(add);
            return grid;
        }

        private async Task ChangePlaceAsync(string side)
        {
            var search = Ask(L["notes.ChangePlace"], L["notes.ChangePlacePrompt"], string.Empty);
            if (search == null) return;

            List<PlaceSummaryDto> found;
            try
            {
                found = await _service.SearchPlacesAsync(search);
            }
            catch (ApiException ex)
            {
                ErrorText.Text = ex.Message;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }
            if (found.Count == 0)
            {
                MessageBox.Show(this, L["notes.NoPlacesFound"], L["notes.ChangePlace"], MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var picker = new ListBox { MaxHeight = 320 };
            foreach (var place in found)
                picker.Items.Add(new ListBoxItem { Content = $"{place.Name} — {place.Address}", Tag = place.Id });

            var window = new Window
            {
                Title = L["notes.ChangePlace"], Owner = this, Width = 560, SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, ResizeMode = ResizeMode.NoResize,
            };
            var ok = new Button { Content = L["notes.Use"], IsDefault = true, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            ok.Click += (_, _) => window.DialogResult = picker.SelectedItem != null;
            picker.MouseDoubleClick += (_, _) => window.DialogResult = picker.SelectedItem != null;
            window.Content = new StackPanel { Margin = new Thickness(14), Children = { picker, ok } };

            if (window.ShowDialog() == true && picker.SelectedItem is ListBoxItem { Tag: int placeId })
                await SaveAsync(() => _service.LinkPlaceAsync(_tripId, side, placeId));
        }

        // =========================================================================================
        // Small pieces
        // =========================================================================================

        /// <summary>Asks for one line of text. Null when cancelled or left empty.</summary>
        private string? Ask(string title, string prompt, string initial)
        {
            var dialog = new ReasonDialog(title, prompt, L["notes.Save"], L["notes.Cancel"], L["notes.TextRequired"]) { Owner = this };
            dialog.ReasonBox.Text = initial;
            dialog.ReasonBox.SelectAll();
            return dialog.ShowDialog() == true ? dialog.Reason : null;
        }

        private static string EntranceName(PlaceSummaryDto place) =>
            string.IsNullOrWhiteSpace(place.EntranceLabel) ? L["notes.UnnamedEntrance"] : place.EntranceLabel;

        private static string SourceName(string source) => source switch
        {
            "Import" => L["notes.SourceImport"],
            "Driver" => L["notes.SourceDriver"],
            "History" => L["notes.SourceHistory"],
            _ => L["notes.SourceDispatcher"],
        };

        private static UIElement Section(string text, string icon)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            if (Enum.TryParse<PackIconKind>(icon, out var kind))
                panel.Children.Add(new PackIcon { Kind = kind, Foreground = Primary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            panel.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            return panel;
        }

        private Button IconButton(string icon, string tooltip, Func<Task> action, Brush? color = null)
        {
            var button = new Button
            {
                Style = (Style)FindResource("MaterialDesignIconButton"), Width = 32, Height = 32, Padding = new Thickness(0), ToolTip = tooltip,
                Content = new PackIcon { Kind = Enum.Parse<PackIconKind>(icon), Foreground = color ?? Quiet, Width = 18, Height = 18 },
            };
            button.Click += async (_, _) => await action();
            return button;
        }

        private Button TextButton(string text, Func<Task> action, bool raised = false)
        {
            var button = new Button
            {
                Content = text, Margin = new Thickness(6, 0, 0, 0),
                Style = (Style)FindResource(raised ? "MaterialDesignRaisedButton" : "MaterialDesignOutlinedButton"),
            };
            button.Click += async (_, _) => await action();
            return button;
        }

        private static Brush Freeze(Brush brush)
        {
            brush.Freeze();
            return brush;
        }
    }
}
