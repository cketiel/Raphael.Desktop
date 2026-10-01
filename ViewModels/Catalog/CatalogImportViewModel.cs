using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models.Catalog;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Catalog;

// This project has a RelayCommand of its own in Helpers, from before the toolkit arrived.
// Both are in scope here, so the one meant is named outright.
using RelayCommand = CommunityToolkit.Mvvm.Input.RelayCommand;

namespace Raphael.Desktop.ViewModels.Catalog
{
    /// <summary>Where the dialog is in its one job.</summary>
    public enum CatalogImportStage
    {
        /// <summary>No file chosen yet.</summary>
        Empty = 0,

        /// <summary>A file is read and waiting to be sent.</summary>
        Ready = 1,

        /// <summary>Sending.</summary>
        Running = 2,

        /// <summary>Finished. Nothing more to send until another file is chosen.</summary>
        Done = 3
    }

    /// <summary>
    /// Loads a source spreadsheet into the catalog: pick the file, look at what came out of it,
    /// send it, and see what the catalog did with each entity.
    /// </summary>
    /// <remarks>
    /// The same screen fills either catalog; what differs is behind
    /// <see cref="ICatalogImportTarget"/>.
    ///
    /// <para>
    /// The sending rules are the ones the trip import learned the hard way (IMPORT_SPEC.md
    /// §2.1): one request in flight, 100 rows a batch, a quarter of a second between batches.
    /// The batch size is about each batch finishing inside the server's timeout, not about
    /// payload size.
    /// </para>
    /// </remarks>
    public class CatalogImportViewModel : ObservableObject
    {
        /// <summary>Rows per request. See IMPORT_SPEC.md §2.1.</summary>
        private const int BatchSize = 100;

        /// <summary>
        /// Pause between batches, so a ten-thousand-row file does not read as a machine
        /// hammering the door.
        /// </summary>
        private const int PauseBetweenBatchesMs = 250;

        private readonly ICatalogImportTarget _target;
        private readonly IReadOnlyList<CatalogCategoryDto> _categories;

        private CatalogReadResult? _read;
        private CancellationTokenSource? _cancellation;

        public CatalogImportViewModel(
            ICatalogImportTarget target,
            IEnumerable<CatalogCategoryDto> categories)
        {
            _target = target;
            _categories = categories.ToList();

            Templates = new ObservableCollection<CatalogMappingTemplate>(
                XlsxCatalogReader.LoadTemplates(target.Kind));

            FileRows = new ObservableCollection<CatalogPreviewRow>();
            Outcomes = new ObservableCollection<CatalogImportOutcomeRow>();

            BrowseCommand = new RelayCommand(Browse, () => !IsRunning);
            ImportCommand = new AsyncRelayCommand(ImportAsync, () => CanImport);
            StopCommand = new RelayCommand(StopRun, () => IsRunning);
            CloseCommand = new RelayCommand<Window>(Close);

            SelectedTemplate = Templates.FirstOrDefault();

            // No LanguageChanged subscription: this lives in a modal dialog that cannot outlive
            // a language switch, and the singleton holding a lambda back to it would keep every
            // row it read alive for the rest of the session.
        }

        public ObservableCollection<CatalogMappingTemplate> Templates { get; }

        /// <summary>
        /// Every row the file yielded, as it will be sent.
        /// </summary>
        /// <remarks>
        /// All of them, not a sample. It showed the first fifty once, which made it impossible
        /// to tell a short list from a truncated one. The grid is virtualised; two thousand
        /// rows cost nothing to show.
        /// </remarks>
        public ObservableCollection<CatalogPreviewRow> FileRows { get; }

        /// <summary>
        /// What the catalog did with each entity, filled as the batches come back.
        /// </summary>
        /// <remarks>
        /// One line per entity, not per row of the file: the whole point is to be able to see
        /// that three rows became one hospital, which is the single most confusing thing these
        /// files do.
        /// </remarks>
        public ObservableCollection<CatalogImportOutcomeRow> Outcomes { get; }

        public IRelayCommand BrowseCommand { get; }
        public IAsyncRelayCommand ImportCommand { get; }
        public IRelayCommand StopCommand { get; }
        public IRelayCommand<Window> CloseCommand { get; }

        /// <summary>Whether anything reached the catalog, so the list behind can refresh.</summary>
        public bool AnythingImported { get; private set; }

        private CatalogImportStage _stage = CatalogImportStage.Empty;
        public CatalogImportStage Stage
        {
            get => _stage;
            private set
            {
                if (SetProperty(ref _stage, value))
                {
                    RaiseState();
                }
            }
        }

        public bool IsRunning => Stage == CatalogImportStage.Running;
        public bool IsDone => Stage == CatalogImportStage.Done;
        public bool HasFile => _read is not null;
        public bool HasOutcomes => Outcomes.Count > 0;

        private CatalogMappingTemplate? _selectedTemplate;
        public CatalogMappingTemplate? SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (SetProperty(ref _selectedTemplate, value))
                {
                    // Rows read under the old template say nothing about the new one.
                    ReadFile();
                }
            }
        }

        private string? _filePath;
        public string? FilePath
        {
            get => _filePath;
            private set => SetProperty(ref _filePath, value);
        }

        private string? _summary;

        /// <summary>What the file turned out to hold, before anything is sent.</summary>
        public string? Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        private string? _progress;

        /// <summary>Running commentary while sending, then the final tally.</summary>
        public string? Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }

        private int _mergedCount;

        /// <summary>How many rows of the file turned out to repeat another row.</summary>
        public int MergedCount
        {
            get => _mergedCount;
            private set
            {
                if (SetProperty(ref _mergedCount, value))
                {
                    OnPropertyChanged(nameof(HasMerged));
                    OnPropertyChanged(nameof(DuplicatesExplained));
                }
            }
        }

        public bool HasMerged => MergedCount > 0;

        /// <summary>
        /// Why some rows did not become entities of their own, spelled out.
        /// </summary>
        /// <remarks>
        /// ⚠️ This panel exists because the number is alarming on its own. A 381-row file that
        /// produces 331 entities reads as fifty failures unless something says, in the same
        /// breath, that the fifty were repeats of rows already in the file and that nothing was
        /// lost. Leaving the user to work that out is how a correct import gets undone.
        /// </remarks>
        public string DuplicatesExplained =>
            string.Format(LocalizationService.Instance["catalog.DuplicatesExplained"], MergedCount);

        public string DuplicatesTitle => LocalizationService.Instance["catalog.DuplicatesTitle"];

        public bool CanImport =>
            Stage == CatalogImportStage.Ready
            && _read is not null
            && _read.Rows.Count > 0
            && ResolveCategoryId() is not null;

        private int? ResolveCategoryId()
        {
            if (SelectedTemplate is null)
            {
                return null;
            }

            var match = _categories.FirstOrDefault(c =>
                string.Equals(c.Code, SelectedTemplate.CategoryCode, StringComparison.OrdinalIgnoreCase));

            return match?.Id;
        }

        private void Browse()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                Title = LocalizationService.Instance["catalog.ChooseFile"]
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            FilePath = dialog.FileName;
            ReadFile();
        }

        /// <summary>
        /// Parses the file locally and shows every row it yielded. Nothing leaves the machine.
        /// </summary>
        private void ReadFile()
        {
            FileRows.Clear();
            Outcomes.Clear();
            Progress = null;
            AnythingImported = false;
            MergedCount = 0;

            if (SelectedTemplate is null || string.IsNullOrWhiteSpace(FilePath))
            {
                _read = null;
                Summary = null;
                Stage = CatalogImportStage.Empty;
                RaiseState();
                return;
            }

            try
            {
                _read = XlsxCatalogReader.Read(FilePath, SelectedTemplate);

                foreach (var row in _read.Rows)
                {
                    FileRows.Add(new CatalogPreviewRow
                    {
                        RowNumber = row.RowNumber,
                        Name = row.Get("Name"),
                        City = row.Get("City"),
                        County = row.Get("County"),
                        Zip = row.Get("Zip"),
                        Phone = row.Get("Phone"),
                        Email = row.Get("Email")
                    });
                }

                Summary = string.Format(
                    LocalizationService.Instance["catalog.ReadSummary"],
                    _read.Rows.Count,
                    _read.HeaderRow,
                    _read.BlankRowsSkipped);

                if (_read.UnmappedColumns.Count > 0)
                {
                    // Not an error. Said out loud because a column the template does not know
                    // is a column whose data silently does not arrive.
                    Summary += Environment.NewLine + string.Format(
                        LocalizationService.Instance["catalog.UnmappedColumns"],
                        string.Join(", ", _read.UnmappedColumns));
                }

                Stage = CatalogImportStage.Ready;
            }
            catch (Exception exception)
            {
                _read = null;
                Summary = null;
                Stage = CatalogImportStage.Empty;

                UiError.Show(
                    exception.Message,
                    LocalizationService.Instance["catalog.ReadFailed"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            RaiseState();
        }

        private async Task ImportAsync()
        {
            if (_read is null || SelectedTemplate is null)
            {
                return;
            }

            var categoryId = ResolveCategoryId();

            if (categoryId is null)
            {
                MessageBox.Show(
                    string.Format(
                        LocalizationService.Instance["catalog.UnknownGroup"],
                        SelectedTemplate.CategoryCode),
                    LocalizationService.Instance["catalog.Import"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var batchId = Guid.NewGuid();
            var sourceFile = System.IO.Path.GetFileName(FilePath) ?? string.Empty;

            _cancellation = new CancellationTokenSource();
            Stage = CatalogImportStage.Running;
            Outcomes.Clear();
            AnythingImported = false;
            MergedCount = 0;

            var created = 0;
            var updated = 0;
            var failed = 0;
            var merged = 0;
            var sent = 0;
            var stopped = false;

            try
            {
                // Batched by entity, not every hundred rows: see CatalogRowBatcher for
                // why a hospital must not straddle a boundary.
                foreach (var batch in CatalogRowBatcher.Batch(_read.Rows, BatchSize))
                {
                    _cancellation.Token.ThrowIfCancellationRequested();

                    var result = await _target.SendAsync(
                        batchId, sourceFile, categoryId.Value, batch, _cancellation.Token);

                    created += result.Created;
                    updated += result.Updated;
                    failed += result.Failed;
                    merged += result.Merged;
                    sent += batch.Count;

                    foreach (var line in result.Rows)
                    {
                        Outcomes.Add(CatalogImportOutcomeRow.From(line));
                    }

                    Progress = string.Format(
                        LocalizationService.Instance["catalog.ImportProgress"],
                        sent, _read.Rows.Count, created, updated, merged, failed);

                    // One request in flight, with a pause between. Concurrency here is what
                    // broke the trip import before it was written down.
                    await Task.Delay(PauseBetweenBatchesMs, _cancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Stopping is not losing: everything sent is already saved, and running the
                // file again updates those rows rather than duplicating them.
                stopped = true;
            }
            catch (Exception exception)
            {
                UiError.Show(
                    exception.Message,
                    LocalizationService.Instance["catalog.ImportFailed"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _cancellation?.Dispose();
                _cancellation = null;

                AnythingImported = created + updated > 0;
                MergedCount = merged;

                Progress = string.Format(
                    LocalizationService.Instance[
                        stopped ? "catalog.ImportStopped" : "catalog.ImportDone"],
                    created, updated, merged, failed);

                Stage = CatalogImportStage.Done;
                OnPropertyChanged(nameof(HasOutcomes));
            }
        }

        private void StopRun()
        {
            _cancellation?.Cancel();
        }

        private static void Close(Window? window)
        {
            window?.Close();
        }

        private void RaiseState()
        {
            OnPropertyChanged(nameof(CanImport));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(HasFile));
            OnPropertyChanged(nameof(HasOutcomes));

            BrowseCommand.NotifyCanExecuteChanged();
            ImportCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }

        // ---- Translation -----------------------------------------------------------------

        public string WindowTitle => LocalizationService.Instance["catalog.ImportTitle"];
        public string BrowseText => LocalizationService.Instance["catalog.Browse"];
        public string TemplateText => LocalizationService.Instance["catalog.Template"];
        public string ImportText => LocalizationService.Instance["catalog.Import"];
        public string CloseText => LocalizationService.Instance["catalog.Close"];
        public string StopText => LocalizationService.Instance["catalog.Stop"];

        public string FileRowsHeader => LocalizationService.Instance["catalog.FileRowsHeader"];
        public string FileRowsHint => LocalizationService.Instance["catalog.FileRowsHint"];
        public string OutcomesHeader => LocalizationService.Instance["catalog.OutcomesHeader"];
        public string OutcomesHint => LocalizationService.Instance["catalog.OutcomesHint"];

        public string ColumnRow => LocalizationService.Instance["catalog.ColumnRow"];
        public string ColumnName => LocalizationService.Instance["catalog.ColumnName"];
        public string ColumnCity => LocalizationService.Instance["catalog.ColumnCity"];
        public string ColumnCounty => LocalizationService.Instance["catalog.ColumnCounty"];
        public string ColumnZip => LocalizationService.Instance["catalog.ColumnZip"];
        public string ColumnPhone => LocalizationService.Instance["catalog.ColumnPhone"];
        public string ColumnEmail => LocalizationService.Instance["catalog.ColumnEmail"];
        public string ColumnResult => LocalizationService.Instance["catalog.ColumnResult"];
        public string ColumnDetail => LocalizationService.Instance["catalog.ColumnDetail"];
    }

    /// <summary>One row of the file, as the dialog shows it before sending.</summary>
    public class CatalogPreviewRow
    {
        public int RowNumber { get; set; }
        public string? Name { get; set; }
        public string? City { get; set; }
        public string? County { get; set; }
        public string? Zip { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
    }

    /// <summary>What the catalog did with one entity.</summary>
    public class CatalogImportOutcomeRow
    {
        public int RowNumber { get; set; }

        /// <summary>Added, updated or rejected, in the user's language.</summary>
        public string Result { get; set; } = string.Empty;

        /// <summary>Why it was rejected, or which rows were folded together.</summary>
        public string? Detail { get; set; }

        /// <summary>Used by the view to colour the rejections.</summary>
        public bool IsProblem { get; set; }

        public static CatalogImportOutcomeRow From(CatalogImportRowResultDto line)
        {
            var row = new CatalogImportOutcomeRow
            {
                RowNumber = line.RowNumber,
                IsProblem = line.Outcome == CatalogImportOutcome.Rejected
            };

            row.Result = line.Outcome switch
            {
                CatalogImportOutcome.Created => LocalizationService.Instance["catalog.outcome.Created"],
                CatalogImportOutcome.Updated => LocalizationService.Instance["catalog.outcome.Updated"],
                _ => LocalizationService.Instance["catalog.outcome.Rejected"]
            };

            if (line.Outcome == CatalogImportOutcome.Rejected)
            {
                row.Detail = CatalogProblems.Describe(line.ErrorCode);
            }
            else if (line.MergedRowCount > 1 && line.MergedRowNumbers is not null)
            {
                // The single most confusing thing these files do, said plainly.
                row.Detail = string.Format(
                    LocalizationService.Instance["catalog.outcome.Merged"],
                    line.MergedRowCount,
                    string.Join(", ", line.MergedRowNumbers));
            }

            return row;
        }
    }

    /// <summary>
    /// Turns the server's rejection codes into words, in the user's language.
    /// </summary>
    /// <remarks>
    /// The server sends a code and not a sentence, which is what lets the wording live here —
    /// in the two language files — instead of being baked into an English string on a machine
    /// nobody reading it will ever see. Same split as ImportProblemCatalog does for trips.
    /// </remarks>
    public static class CatalogProblems
    {
        public static string Describe(string? errorCode)
        {
            if (string.IsNullOrWhiteSpace(errorCode))
            {
                return LocalizationService.Instance["catalog.problem.Unknown"];
            }

            var key = "catalog.problem." + errorCode;

            return LocalizationService.Instance.TryGetValue(key, out var text)
                ? text
                : string.Format(LocalizationService.Instance["catalog.problem.Coded"], errorCode);
        }
    }
}
