using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using Raphael.Desktop.Views;
using Raphael.Desktop.Views.Compliance;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>One line of a subject's checklist, as the office reads it.</summary>
    public class ComplianceDocRow
    {
        public ComplianceItemDto Item { get; init; }
        public ComplianceSubjectDto Owner { get; init; }
        public string Name => Item.Type.Name;
        /// <summary>valid, soon, expired, pending, rejected or missing: the colour of the badge.</summary>
        public string State { get; init; }
        public string StateText { get; init; }
        public string ExpiresText { get; init; }
        public string Detail { get; init; }
        public string Note { get; init; }
        public string FilesText { get; init; }
        public string UploadedText { get; init; }

        public ComplianceDocumentDto Viewable => Item.Current ?? Item.Latest;
        public bool CanView => Viewable?.Files.Count > 0;
        public bool CanUpload => Owner.CanUpload;
        public string UploadToolTip => LocalizationService.Instance[Item.Current is not null ? "compliance.Renew" : "compliance.Upload"];

        /// <summary>The upload waiting for the admin, when there is one.</summary>
        public ComplianceDocumentDto Pending => Item.Latest?.Status == "PENDING" ? Item.Latest : null;
        public bool CanReview => Pending is not null && ComplianceViewModel.IsAdminUser;
        /// <summary>Only what is still waiting can be corrected; an approved one is renewed.</summary>
        public bool CanEdit => Pending is not null && Owner.CanUpload;
        public bool HasHistory => Item.Current is not null || Item.Latest is not null;

        /// <summary>RE-069: the document a notice pointed at, marked so the eye finds it at once.</summary>
        public bool IsHighlighted { get; set; }
    }

    /// <summary>A line of the People and Vehicles lists, with a tick to notify.</summary>
    public partial class ComplianceSummaryRow : ObservableObject
    {
        public ComplianceSummaryDto Summary { get; init; }
        [ObservableProperty] private bool _isChecked;

        public string Name => Summary.SubjectName;
        /// <summary>Only a driver can be written to; a vehicle or a dispatcher has no driver app.</summary>
        public bool CanNotify => Summary.Role == "DRIVER";
        public string RoleText => Summary.Role switch
        {
            "DRIVER" => LocalizationService.Instance["compliance.RoleDriver"],
            "DISPATCHER" => LocalizationService.Instance["compliance.RoleDispatcher"],
            _ => string.Empty
        };
        public string Attention => ComplianceUi.StateOf(Summary);
        public string NextExpiryText => Summary.NextExpiry?.ToString("MM/dd/yyyy") ?? "—";
        public string ActiveText => Summary.IsActive ? string.Empty : LocalizationService.Instance["compliance.Inactive"];
    }

    /// <summary>A row of To review / Expiring: one document, with a tick to notify its driver.</summary>
    public partial class ComplianceListRow : ObservableObject
    {
        public ComplianceDocumentDto Doc { get; init; }
        [ObservableProperty] private bool _isChecked;
        public string ExpiresText => Doc.ExpiresOn.ToString("MM/dd/yyyy");
        public string UploadedText => Doc.UploadedAtUtc.ToLocalTime().ToString("MM/dd/yyyy h:mm tt");
        public string State => Doc.Status == "PENDING" ? "pending" : Doc.DaysToExpiry < 0 ? "expired" : "soon";
        public string DaysText => Doc.DaysToExpiry < 0
            ? string.Format(LocalizationService.Instance["compliance.ExpiredAgo"], -Doc.DaysToExpiry)
            : Doc.DaysToExpiry == 0 ? LocalizationService.Instance["compliance.ExpiresToday"]
            : string.Format(LocalizationService.Instance["compliance.DaysLeft"], Doc.DaysToExpiry);
        public string SubjectKind => Doc.Subject == "VEHICLE" ? LocalizationService.Instance["compliance.KindVehicle"] : LocalizationService.Instance["compliance.KindPerson"];
        public bool IsAdmin => ComplianceViewModel.IsAdminUser;
    }

    /// <summary>
    /// Docs (RE-065): every compliance document of the company in one place. What waits for the
    /// admin, what expires, every person's (drivers and the office) and every vehicle's documents,
    /// and the user's own. The office uploads, corrects what waits and renews; only an
    /// administrator approves or rejects. Users and Vehicles link here, they do not repeat it.
    /// </summary>
    public partial class ComplianceViewModel : ObservableObject
    {
        private const int SoonDays = 30;
        public const int TabReview = 0, TabExpiring = 1, TabPeople = 2, TabVehicles = 3, TabMine = 4;

        private readonly ComplianceService _service = new();
        private static string T(string key) => LocalizationService.Instance[key];

        /// <summary>Role 1: the one who approves.</summary>
        public static bool IsAdminUser => SessionManager.Role == "1";
        public bool IsAdmin => IsAdminUser;

        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _message;
        [ObservableProperty] private int _selectedTab = TabReview;

        // ---- To review ----
        public ObservableCollection<ComplianceListRow> Pending { get; } = new();
        [ObservableProperty] private int _pendingCount;

        // ---- Expiring ----
        public ObservableCollection<ComplianceListRow> Expiring { get; } = new();
        public int[] DayOptions { get; } = [30, 60, 90];
        [ObservableProperty] private int _expiringDays = 30;
        partial void OnExpiringDaysChanged(int value) => _ = Run(LoadExpiringCoreAsync);
        [ObservableProperty] private int _expiringCount;

        // ---- People / Vehicles ----
        private List<ComplianceSummaryRow> _allPeople = new();
        private List<ComplianceSummaryRow> _allVehicles = new();
        public ObservableCollection<ComplianceSummaryRow> People { get; } = new();
        public ObservableCollection<ComplianceSummaryRow> Vehicles { get; } = new();
        [ObservableProperty] private string _peopleSearch;
        [ObservableProperty] private string _vehicleSearch;
        public string[] RoleFilters { get; } = [T("compliance.RoleAll"), T("compliance.RoleDriver"), T("compliance.RoleDispatcher")];
        [ObservableProperty] private int _roleFilter;
        partial void OnPeopleSearchChanged(string value) => FilterPeople();
        partial void OnRoleFilterChanged(int value) => FilterPeople();
        partial void OnVehicleSearchChanged(string value) => Filter(_allVehicles, Vehicles, VehicleSearch, _ => true);

        [ObservableProperty] private ComplianceSummaryRow _selectedPerson;
        [ObservableProperty] private ComplianceSummaryRow _selectedVehicle;
        partial void OnSelectedPersonChanged(ComplianceSummaryRow value) { if (value != null && !_reselecting) _ = Run(() => LoadPanelCoreAsync(PersonPanel, "USER", value.Summary.SubjectId)); }
        partial void OnSelectedVehicleChanged(ComplianceSummaryRow value) { if (value != null && !_reselecting) _ = Run(() => LoadPanelCoreAsync(VehiclePanel, "VEHICLE", value.Summary.SubjectId)); }

        /// <summary>Putting back the same selection after a live update: its panel is reloaded only if it changed.</summary>
        private bool _reselecting;

        public ObservableCollection<ComplianceDocRow> PersonPanel { get; } = new();
        public ObservableCollection<ComplianceDocRow> VehiclePanel { get; } = new();
        public ObservableCollection<ComplianceDocRow> MinePanel { get; } = new();
        [ObservableProperty] private string _personPanelTitle;
        [ObservableProperty] private string _vehiclePanelTitle;

        /// <summary>Where Users or Vehicles asked to land: that person or vehicle, selected.</summary>
        private readonly (string Subject, int Id)? _focus;
        /// <summary>A notice's document: the row selected once its owner's documents are shown.</summary>
        private int? _focusDocumentId;
        /// <summary>RE-069: the notice's document, marked in its panel (unlike the selection, it stays).</summary>
        private readonly int? _highlightDocumentId;

        /// <summary>The selected document row of the panel on screen (one selection for the three panels).</summary>
        [ObservableProperty] private ComplianceDocRow _selectedDoc;

        public ComplianceViewModel(string focusSubject = null, int focusId = 0, int? focusDocumentId = null)
        {
            if (focusSubject != null) _focus = (focusSubject, focusId);
            _focusDocumentId = focusDocumentId;
            _highlightDocumentId = focusDocumentId;
            _ = LoadAllAsync();

            // RE-069: the screen updates itself when anyone changes a document. Weak: a closed tab
            // must not be kept alive, and reloading, by the shared channel.
            WeakEventManager<ComplianceBoard, ComplianceChangedEventArgs>.AddHandler(ComplianceBoard.Instance, nameof(ComplianceBoard.Changed), OnComplianceChanged);
            ComplianceBoard.Instance.EnsureStarted();
        }

        // ---- Live updates (RE-069) -------------------------------------------------------------

        private readonly HashSet<(string Subject, int Id)> _liveSubjects = new();
        private bool _liveEverything;
        private System.Windows.Threading.DispatcherTimer _liveTimer;

        /// <summary>
        /// Collects what changed for a moment before reading it again: an upload with five photos is
        /// six saves, and the screen should read once, not six times.
        /// </summary>
        private void OnComplianceChanged(object sender, ComplianceChangedEventArgs e)
        {
            if (e.Message is null) _liveEverything = true;
            else _liveSubjects.Add((e.Message.Subject, e.Message.SubjectId));

            if (_liveTimer is null)
            {
                _liveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
                _liveTimer.Tick += async (_, _) => { _liveTimer.Stop(); await ApplyLiveAsync(); };
            }
            _liveTimer.Stop();
            _liveTimer.Start();
        }

        /// <summary>
        /// Reads again only what the change can touch, without the busy bar and keeping what the user
        /// ticked and selected: the lists of documents, the overview of that kind of subject, and the
        /// panel on screen only when it is that subject's.
        /// </summary>
        private async Task ApplyLiveAsync()
        {
            var everything = _liveEverything;
            var subjects = _liveSubjects.ToList();
            _liveEverything = false;
            _liveSubjects.Clear();

            try
            {
                var tickedDocs = Pending.Concat(Expiring).Where(r => r.IsChecked).Select(r => r.Doc.Id).ToHashSet();
                await LoadPendingAsync();
                await LoadExpiringCoreAsync();
                foreach (var r in Pending.Concat(Expiring)) r.IsChecked = tickedDocs.Contains(r.Doc.Id);

                bool Touches(string subject, int id) => everything || subjects.Contains((subject, id));

                if (everything || subjects.Any(s => s.Subject == "USER"))
                {
                    var ticked = _allPeople.Where(r => r.IsChecked).Select(r => r.Summary.SubjectId).ToHashSet();
                    _allPeople = (await _service.GetOverviewAsync("USER")).Select(s => new ComplianceSummaryRow { Summary = s, IsChecked = ticked.Contains(s.SubjectId) }).ToList();
                    var keep = SelectedPerson?.Summary.SubjectId;
                    FilterPeople();
                    Reselect(() => SelectedPerson = People.FirstOrDefault(p => p.Summary.SubjectId == keep));
                    if (keep is int person && Touches("USER", person)) await LoadPanelCoreAsync(PersonPanel, "USER", person);
                    if (int.TryParse(SessionManager.UserId, out var me) && Touches("USER", me)) await LoadPanelCoreAsync(MinePanel, "USER", me);
                }

                if (everything || subjects.Any(s => s.Subject == "VEHICLE"))
                {
                    _allVehicles = (await _service.GetOverviewAsync("VEHICLE")).Select(s => new ComplianceSummaryRow { Summary = s }).ToList();
                    var keep = SelectedVehicle?.Summary.SubjectId;
                    Filter(_allVehicles, Vehicles, VehicleSearch, _ => true);
                    Reselect(() => SelectedVehicle = Vehicles.FirstOrDefault(v => v.Summary.SubjectId == keep));
                    if (keep is int vehicle && Touches("VEHICLE", vehicle)) await LoadPanelCoreAsync(VehiclePanel, "VEHICLE", vehicle);
                }
            }
            catch (Exception ex)
            {
                // Never in the user's way: the next change, or the next time the tab opens, reads it again.
                FileLogger.Log($"Compliance live update: {ex.Message}");
            }
        }

        private void Reselect(Action select)
        {
            _reselecting = true;
            try { select(); }
            finally { _reselecting = false; }
        }

        [RelayCommand]
        private async Task LoadAllAsync()
        {
            var keepPerson = SelectedPerson?.Summary.SubjectId;
            var keepVehicle = SelectedVehicle?.Summary.SubjectId;
            await Run(async () =>
            {
                await LoadPendingAsync();
                await LoadExpiringCoreAsync();
                _allPeople = (await _service.GetOverviewAsync("USER")).Select(s => new ComplianceSummaryRow { Summary = s }).ToList();
                _allVehicles = (await _service.GetOverviewAsync("VEHICLE")).Select(s => new ComplianceSummaryRow { Summary = s }).ToList();
                FilterPeople();
                Filter(_allVehicles, Vehicles, VehicleSearch, _ => true);
                if (int.TryParse(SessionManager.UserId, out var me))
                    await LoadPanelCoreAsync(MinePanel, "USER", me);
            });

            // Back on what was selected, or on what Users / Vehicles asked for.
            if (_focus is { } f && keepPerson is null && keepVehicle is null)
            {
                if (f.Subject == "VEHICLE") { SelectedTab = TabVehicles; SelectedVehicle = Vehicles.FirstOrDefault(v => v.Summary.SubjectId == f.Id); }
                else { SelectedTab = TabPeople; SelectedPerson = People.FirstOrDefault(p => p.Summary.SubjectId == f.Id); }
                return;
            }
            if (keepPerson != null) SelectedPerson = People.FirstOrDefault(p => p.Summary.SubjectId == keepPerson);
            if (keepVehicle != null) SelectedVehicle = Vehicles.FirstOrDefault(v => v.Summary.SubjectId == keepVehicle);
        }

        private async Task LoadPendingAsync()
        {
            var docs = await _service.GetPendingAsync();
            Pending.Clear();
            foreach (var d in docs) Pending.Add(new ComplianceListRow { Doc = d });
            PendingCount = Pending.Count;
        }

        private async Task LoadExpiringCoreAsync()
        {
            var docs = await _service.GetExpiringAsync(ExpiringDays);
            Expiring.Clear();
            foreach (var d in docs.OrderBy(d => d.DaysToExpiry)) Expiring.Add(new ComplianceListRow { Doc = d });
            ExpiringCount = Expiring.Count;
        }

        private async Task LoadPanelCoreAsync(ObservableCollection<ComplianceDocRow> panel, string subject, int id)
        {
            var data = await _service.GetSubjectAsync(subject, id);
            panel.Clear();
            foreach (var item in data.Items)
            {
                var row = RowOf(item, data);
                // The document a notice pointed at stays marked while that panel is on screen.
                if (_highlightDocumentId is int h && (item.Current?.Id == h || item.Latest?.Id == h))
                    row.IsHighlighted = true;
                panel.Add(row);
            }
            if (panel == PersonPanel) PersonPanelTitle = data.SubjectName;
            if (panel == VehiclePanel) VehiclePanelTitle = data.SubjectName;

            // From a notice: its document, the approved one or the one waiting. One replaced since
            // has no line of its own; its owner's documents are on screen all the same.
            if (_focusDocumentId is int docId)
            {
                var row = panel.FirstOrDefault(r => r.Item.Current?.Id == docId || r.Item.Latest?.Id == docId);
                SelectedDoc = row;
                if (row != null) _focusDocumentId = null;
            }
        }

        private void FilterPeople() => Filter(_allPeople, People, PeopleSearch,
            r => RoleFilter == 0 || (RoleFilter == 1 ? r.Summary.Role == "DRIVER" : r.Summary.Role == "DISPATCHER"));

        private static void Filter(List<ComplianceSummaryRow> all, ObservableCollection<ComplianceSummaryRow> shown, string text, Func<ComplianceSummaryRow, bool> keep)
        {
            shown.Clear();
            foreach (var row in all.Where(r => keep(r) && (string.IsNullOrWhiteSpace(text) || (r.Name ?? "").Contains(text.Trim(), StringComparison.OrdinalIgnoreCase))))
                shown.Add(row);
        }

        // ---- Actions ---------------------------------------------------------------------------

        [RelayCommand]
        private async Task ViewDocAsync(ComplianceDocumentDto doc)
        {
            if (doc is null || doc.Files.Count == 0) return;
            await Run(() => ComplianceFileViewer.ShowAsync(_service, doc));
        }

        [RelayCommand] private Task ViewRowAsync(ComplianceDocRow row) => ViewDocAsync(row?.Viewable);
        [RelayCommand] private Task ViewListRowAsync(ComplianceListRow row) => ViewDocAsync(row?.Doc);

        /// <summary>
        /// RE-069: saves the document's files where the user chooses. One file asks for its name;
        /// several (a vehicle's photos) ask for a folder. The file name says what it is and whose.
        /// </summary>
        [RelayCommand]
        private async Task DownloadDocAsync(ComplianceDocumentDto doc)
        {
            if (doc is null || doc.Files.Count == 0) return;

            static string Safe(string s) => string.Concat((s ?? "").Split(System.IO.Path.GetInvalidFileNameChars())).Trim();
            static string Ext(string contentType) => contentType switch
            {
                "application/pdf" => ".pdf",
                "image/png" => ".png",
                _ => ".jpg"
            };
            var baseName = Safe($"{doc.DocumentTypeName} - {doc.SubjectName}");

            List<(ComplianceDocumentFileDto File, string Target)> plan;
            if (doc.Files.Count == 1)
            {
                var file = doc.Files[0];
                var save = new Microsoft.Win32.SaveFileDialog
                {
                    Title = T("compliance.Download"),
                    FileName = baseName + Ext(file.ContentType),
                    Filter = $"{T("compliance.Download")}|*{Ext(file.ContentType)}"
                };
                if (save.ShowDialog() != true) return;
                plan = [(file, save.FileName)];
            }
            else
            {
                var folder = new Microsoft.Win32.OpenFolderDialog { Title = T("compliance.DownloadFolder") };
                if (folder.ShowDialog() != true) return;
                plan = doc.Files.Select(f => (f, System.IO.Path.Combine(folder.FolderName,
                    $"{baseName} - {Safe(T($"compliance.Slot.{f.Slot}"))}{Ext(f.ContentType)}"))).ToList();
            }

            var saved = false;
            await Run(async () =>
            {
                foreach (var (file, target) in plan)
                {
                    var temp = await _service.DownloadAsync(doc.Id, file.Slot, file.ContentType);
                    System.IO.File.Copy(temp, target, overwrite: true);
                }
                saved = true;
            });
            if (saved)
                MessageBox.Show(string.Format(T("compliance.Downloaded"), plan.Count), T("compliance.Download"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand] private Task DownloadRowAsync(ComplianceDocRow row) => DownloadDocAsync(row?.Viewable);
        [RelayCommand] private Task DownloadListRowAsync(ComplianceListRow row) => DownloadDocAsync(row?.Doc);

        [RelayCommand]
        private async Task ApproveAsync(ComplianceDocumentDto doc)
        {
            if (doc is null || !IsAdmin) return;
            var question = string.Format(T("compliance.ApproveConfirm"), doc.DocumentTypeName, doc.SubjectName, doc.ExpiresOn.ToString("MM/dd/yyyy"));
            if (MessageBox.Show(question, T("compliance.Approve"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await Act(() => _service.ApproveAsync(doc.Id));
        }

        [RelayCommand]
        private async Task RejectAsync(ComplianceDocumentDto doc)
        {
            if (doc is null || !IsAdmin) return;
            var dialog = new ReasonDialog(T("compliance.RejectTitle"),
                string.Format(T("compliance.RejectPrompt"), doc.DocumentTypeName, doc.SubjectName),
                T("compliance.Reject"), T("compliance.Cancel"), T("compliance.ReasonRequired"));
            if (dialog.ShowDialog() != true) return;
            await Act(() => _service.RejectAsync(doc.Id, dialog.Reason));
        }

        [RelayCommand] private Task ApproveListRowAsync(ComplianceListRow row) => ApproveAsync(row?.Doc);
        [RelayCommand] private Task RejectListRowAsync(ComplianceListRow row) => RejectAsync(row?.Doc);
        [RelayCommand] private Task ApproveRowAsync(ComplianceDocRow row) => ApproveAsync(row?.Pending);
        [RelayCommand] private Task RejectRowAsync(ComplianceDocRow row) => RejectAsync(row?.Pending);

        /// <summary>Upload, or renew: a new version; the approved one keeps counting until this is approved.</summary>
        [RelayCommand]
        private async Task UploadAsync(ComplianceDocRow row)
        {
            if (row is null || !row.CanUpload) return;
            var dialog = new ComplianceUploadDialog(_service, row.Item.Type, row.Owner, null);
            if (dialog.ShowDialog() == true) await LoadAllAsync();
        }

        /// <summary>Correct the one still waiting: its expiry, or any of its files.</summary>
        [RelayCommand]
        private async Task EditAsync(ComplianceDocRow row)
        {
            if (row?.Pending is null || !row.CanEdit) return;
            var dialog = new ComplianceUploadDialog(_service, row.Item.Type, row.Owner, row.Pending);
            if (dialog.ShowDialog() == true) await LoadAllAsync();
        }

        [RelayCommand]
        private async Task HistoryAsync(ComplianceDocRow row)
        {
            if (row is null) return;
            List<ComplianceDocumentDto> versions = null;
            await Run(async () => versions = await _service.GetHistoryAsync(row.Owner.Subject, row.Owner.SubjectId, row.Item.Type.Id));
            if (versions is null) return;
            new ComplianceHistoryDialog(_service, $"{row.Name} · {row.Owner.SubjectName}", versions).ShowDialog();
        }

        /// <summary>To the ticked drivers of People.</summary>
        [RelayCommand]
        private Task NotifyPeopleAsync() =>
            NotifyAsync(_allPeople.Where(r => r.IsChecked && r.CanNotify).Select(r => r.Summary.SubjectId).ToList(), null);

        /// <summary>To the drivers of the ticked rows of Expiring; a vehicle's row has no driver here.</summary>
        [RelayCommand]
        private Task NotifyExpiringAsync()
        {
            var rows = Expiring.Where(r => r.IsChecked && r.Doc.Subject == "USER").ToList();
            var ids = rows.Select(r => r.Doc.SubjectId).Distinct().ToList();
            // One driver, one document: the message says which and when.
            var template = rows.Count == 1
                ? string.Format(T("compliance.NoticeTemplateOne"), rows[0].Doc.DocumentTypeName, rows[0].ExpiresText)
                : null;
            return NotifyAsync(ids, template);
        }

        private async Task NotifyAsync(List<int> driverIds, string template)
        {
            if (driverIds.Count == 0)
            {
                MessageBox.Show(T("compliance.NoticeNoDrivers"), T("compliance.NoticeTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var dialog = new ReasonDialog(T("compliance.NoticeTitle"), string.Format(T("compliance.NoticePrompt"), driverIds.Count),
                T("compliance.NoticeSend"), T("compliance.Cancel"), T("compliance.NoticeRequired"));
            dialog.ReasonBox.Text = template ?? T("compliance.NoticeTemplate");
            if (dialog.ShowDialog() != true) return;

            int? delivered = null;
            await Run(async () =>
            {
                var (sent, error) = await _service.SendNoticeAsync(driverIds, dialog.Reason);
                if (error != null) { Message = error; return; }
                foreach (var r in _allPeople) r.IsChecked = false;
                foreach (var r in Expiring) r.IsChecked = false;
                delivered = sent;
            });
            // After the work, not inside it: the busy bar would keep running behind the message.
            if (delivered is int n)
                MessageBox.Show(string.Format(T("compliance.NoticeSent"), n), T("compliance.NoticeTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ---- Plumbing --------------------------------------------------------------------------

        private async Task Act(Func<Task<string>> action)
        {
            string error = null;
            await Run(async () => error = await action());
            if (error != null)
                MessageBox.Show(error, T("compliance.Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
            else
                await LoadAllAsync();
        }

        private async Task Run(Func<Task> work)
        {
            IsBusy = true;
            Message = null;
            try { await work(); }
            catch (Exception ex) { Message = ex.Message; }
            finally { IsBusy = false; }
        }

        private static ComplianceDocRow RowOf(ComplianceItemDto item, ComplianceSubjectDto owner)
        {
            var current = item.Current;
            var latest = item.Latest;
            string Uploaded(ComplianceDocumentDto d) => d is null ? "—" : $"{d.UploadedAtUtc.ToLocalTime():MM/dd/yyyy} · {d.UploadedByName}";

            if (current is not null)
            {
                var days = current.DaysToExpiry;
                var (state, text) = days < 0 ? ("expired", T("compliance.Expired")) : days <= SoonDays ? ("soon", T("compliance.Expiring")) : ("valid", T("compliance.Valid"));
                return new ComplianceDocRow
                {
                    Item = item, Owner = owner, State = state, StateText = text,
                    ExpiresText = current.ExpiresOn.ToString("MM/dd/yyyy"),
                    Detail = days < 0 ? string.Format(T("compliance.ExpiredAgo"), -days) : days == 0 ? T("compliance.ExpiresToday") : string.Format(T("compliance.DaysLeft"), days),
                    Note = latest?.Status switch
                    {
                        "PENDING" => string.Format(T("compliance.RenewalWaiting"), latest.ExpiresOn.ToString("MM/dd/yyyy")),
                        "REJECTED" => string.Format(T("compliance.RenewalRejected"), latest.RejectionReason),
                        _ => null
                    },
                    FilesText = ComplianceUi.FilesText(current),
                    UploadedText = Uploaded(current)
                };
            }
            if (latest?.Status == "PENDING")
                return new ComplianceDocRow
                {
                    Item = item, Owner = owner, State = "pending", StateText = T("compliance.InReview"),
                    ExpiresText = latest.ExpiresOn.ToString("MM/dd/yyyy"),
                    Detail = string.Format(T("compliance.DaysLeft"), latest.DaysToExpiry),
                    Note = T("compliance.NotValidYet"),
                    FilesText = ComplianceUi.FilesText(latest), UploadedText = Uploaded(latest)
                };
            if (latest?.Status == "REJECTED")
                return new ComplianceDocRow
                {
                    Item = item, Owner = owner, State = "rejected", StateText = T("compliance.Rejected"),
                    ExpiresText = "—", Detail = string.Empty,
                    Note = string.Format(T("compliance.RejectedBecause"), latest.RejectionReason),
                    FilesText = ComplianceUi.FilesText(latest), UploadedText = Uploaded(latest)
                };
            return new ComplianceDocRow
            {
                Item = item, Owner = owner, State = "missing", StateText = T("compliance.NotUploaded"),
                ExpiresText = "—", Detail = T(item.Type.IsRequired ? "compliance.Required" : "compliance.Optional"),
                FilesText = "—", UploadedText = "—"
            };
        }
    }
}
