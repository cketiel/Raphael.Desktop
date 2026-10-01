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
    /// <summary>
    /// Loads a source spreadsheet into the catalog: pick the file, pick the template, look at
    /// what came out, then send it.
    /// </summary>
    /// <remarks>
    /// The same screen fills either catalog; what differs is behind
    /// <see cref="ICatalogImportTarget"/>.
    ///
    /// <para>
    /// The rules this follows are the ones the trip import learned the hard way, written down
    /// in IMPORT_SPEC.md §2.1: one request in flight at a time, 100 rows a batch, a quarter of
    /// a second between batches. The batch size is not about payload — it is about each batch
    /// finishing well inside the server's timeout.
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

            Preview = new ObservableCollection<CatalogPreviewRow>();
            Problems = new ObservableCollection<CatalogImportProblem>();

            BrowseCommand = new RelayCommand(Browse);
            ImportCommand = new AsyncRelayCommand(ImportAsync, () => CanImport);
            CancelCommand = new RelayCommand(CancelRun, () => IsRunning);

            SelectedTemplate = Templates.FirstOrDefault();

            // No LanguageChanged subscription here, unlike the list view models. This one lives
            // in a modal dialog that cannot outlive a language switch, and the singleton holding
            // a lambda back to it would keep every row it read alive for the rest of the session.
        }

        public ObservableCollection<CatalogMappingTemplate> Templates { get; }

        /// <summary>The first rows as they will be sent, so somebody can see before sending.</summary>
        public ObservableCollection<CatalogPreviewRow> Preview { get; }

        public ObservableCollection<CatalogImportProblem> Problems { get; }

        public IRelayCommand BrowseCommand { get; }
        public IAsyncRelayCommand ImportCommand { get; }
        public IRelayCommand CancelCommand { get; }

        /// <summary>Whether anything actually reached the catalog, so the list can refresh.</summary>
        public bool AnythingImported { get; private set; }

        private CatalogMappingTemplate? _selectedTemplate;
        public CatalogMappingTemplate? SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (SetProperty(ref _selectedTemplate, value))
                {
                    // Rows read under the old template say nothing about the new one.
                    ResetRead();
                    RaiseState();
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
        public string? Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    RaiseState();
                }
            }
        }

        public bool CanImport =>
            !IsRunning
            && _read is not null
            && _read.Rows.Count > 0
            && SelectedTemplate is not null
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
        /// Parses the file locally and shows what came out. Nothing leaves the machine yet.
        /// </summary>
        private void ReadFile()
        {
            if (SelectedTemplate is null || string.IsNullOrWhiteSpace(FilePath))
            {
                return;
            }

            try
            {
                _read = XlsxCatalogReader.Read(FilePath, SelectedTemplate);

                Preview.Clear();

                foreach (var row in _read.Rows.Take(50))
                {
                    Preview.Add(new CatalogPreviewRow
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

                Problems.Clear();

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
            }
            catch (Exception exception)
            {
                _read = null;
                Preview.Clear();
                Summary = null;

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
            IsRunning = true;
            Problems.Clear();
            AnythingImported = false;

            var created = 0;
            var updated = 0;
            var failed = 0;
            var sent = 0;

            try
            {
                foreach (var batch in Chunk(_read.Rows, BatchSize))
                {
                    _cancellation.Token.ThrowIfCancellationRequested();

                    var result = await _target.SendAsync(
                        batchId, sourceFile, categoryId.Value, batch, _cancellation.Token);

                    created += result.Created;
                    updated += result.Updated;
                    failed += result.Failed;
                    sent += batch.Count;

                    foreach (var line in result.Rows.Where(r =>
                                 r.Outcome == CatalogImportOutcome.Rejected))
                    {
                        Problems.Add(new CatalogImportProblem
                        {
                            RowNumber = line.RowNumber,
                            Field = line.Field,
                            Reason = CatalogProblems.Describe(line.ErrorCode)
                        });
                    }

                    Progress = string.Format(
                        LocalizationService.Instance["catalog.ImportProgress"],
                        sent, _read.Rows.Count, created, updated, failed);

                    // One request in flight, with a pause between. Concurrency here is what
                    // broke the trip import before it was written down.
                    await Task.Delay(PauseBetweenBatchesMs, _cancellation.Token);
                }

                AnythingImported = created + updated > 0;

                MessageBox.Show(
                    string.Format(
                        LocalizationService.Instance["catalog.ImportDone"], created, updated, failed),
                    LocalizationService.Instance["catalog.Import"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                // Stopping is not losing: everything sent so far is already saved, and running
                // the file again updates those rows rather than duplicating them.
                AnythingImported = created + updated > 0;

                MessageBox.Show(
                    string.Format(
                        LocalizationService.Instance["catalog.ImportStopped"], created, updated),
                    LocalizationService.Instance["catalog.Import"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception exception)
            {
                AnythingImported = created + updated > 0;

                UiError.Show(
                    exception.Message,
                    LocalizationService.Instance["catalog.ImportFailed"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsRunning = false;
                _cancellation?.Dispose();
                _cancellation = null;
            }
        }

        private void CancelRun()
        {
            _cancellation?.Cancel();
        }

        private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> source, int size)
        {
            for (var i = 0; i < source.Count; i += size)
            {
                yield return source.Skip(i).Take(size).ToList();
            }
        }

        private void ResetRead()
        {
            _read = null;
            Preview.Clear();
            Problems.Clear();
            Summary = null;
            Progress = null;

            if (!string.IsNullOrWhiteSpace(FilePath))
            {
                ReadFile();
            }
        }

        private void RaiseState()
        {
            OnPropertyChanged(nameof(CanImport));
            ImportCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }

        // ---- Translation -----------------------------------------------------------------

        public string WindowTitle => LocalizationService.Instance["catalog.ImportTitle"];
        public string BrowseText => LocalizationService.Instance["catalog.Browse"];
        public string TemplateText => LocalizationService.Instance["catalog.Template"];
        public string ImportText => LocalizationService.Instance["catalog.Import"];
        public string CancelText => LocalizationService.Instance["catalog.Cancel"];
        public string StopText => LocalizationService.Instance["catalog.Stop"];
        public string PreviewText => LocalizationService.Instance["catalog.Preview"];
        public string ProblemsText => LocalizationService.Instance["catalog.Problems"];
    }

    /// <summary>One row as it will be sent, for the preview grid.</summary>
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

    /// <summary>One row the server would not take, and why.</summary>
    public class CatalogImportProblem
    {
        public int RowNumber { get; set; }
        public string? Field { get; set; }
        public string? Reason { get; set; }
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
