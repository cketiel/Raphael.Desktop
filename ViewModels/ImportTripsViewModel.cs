using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Models.Csv;
using Raphael.Desktop.Models.Import;
using Raphael.Desktop.Services;
using Raphael.Desktop.Services.Import;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>Which rows the repair grid is showing.</summary>
    public enum ImportRowFilter
    {
        /// <summary>Still to be dealt with. Where the screen starts, because it is the work.</summary>
        Pending = 0,

        /// <summary>Corrected and sent again, successfully.</summary>
        Imported = 1,

        All = 2
    }

    /// <summary>
    /// The import screen: choose, run, and put right what did not go in.
    /// </summary>
    /// <remarks>
    /// The engine is RE-009's and is not touched here — geocoding in one batch, mapping in memory,
    /// chunks of a hundred with one request in flight, and a back-off of 5s/15s/45s when the host
    /// says no. What this adds is everything the dispatcher sees, because the engine already
    /// worked and the screen already did not.
    ///
    /// <para>
    /// Three things it deliberately does NOT do. It does not start on file selection — choosing a
    /// file and importing it are separate acts and the second one has a button. It does not keep
    /// anything on disk: the repair session lives here and dies with the screen, because it holds
    /// patient names, phones and addresses, and the documented way to carry work over is to export
    /// the failures and re-import them. And it does not treat a warning as a failure.
    /// </para>
    /// </remarks>
    public partial class ImportTripsViewModel : ObservableObject
    {
        private readonly HomeViewModel _home;
        private readonly ITripImportService _import;

        private List<CsvTripRawModel> _records = new();
        private CsvType _csvType;
        private bool _fileIsSaferide;
        private CancellationTokenSource _cancellation;
        private Stopwatch _clock;

        /// <summary>
        /// Coupled to Home on purpose.
        /// </summary>
        /// <remarks>
        /// The import screen is a mode of the Home tab, not a window of its own, and the mapper
        /// needs the collections Home has already loaded — trips, space types, capacity types,
        /// patients, funding sources. Loading a second copy here would double the traffic on
        /// entering a screen whose entire reason for existing is that it made too many requests.
        /// </remarks>
        public ImportTripsViewModel(HomeViewModel home, ITripImportService import = null)
        {
            _home = home;
            _import = import ?? new TripImportService();

            Rows = new ObservableCollection<ImportRow>();
            RowsView = CollectionViewSource.GetDefaultView(Rows);
            RowsView.Filter = MatchesFilter;

            Log = new ObservableCollection<ImportLogEntry>();
            Steps = new ObservableCollection<ImportRequestStep>();
            PreflightRows = new ObservableCollection<ImportRow>();
        }

        /// <summary>
        /// One bar per request the import makes, plus the overall figure they add up to.
        /// </summary>
        /// <remarks>
        /// Declared before the first request is sent, so the overall percentage only ever goes
        /// forward. The single bar this replaced ran 0 to 100 for the addresses and then 0 to 100
        /// again for the trips: on a file costing three requests it reached the end twice.
        /// </remarks>
        public ObservableCollection<ImportRequestStep> Steps { get; }

        // ================================================================ where we are

        [ObservableProperty] private ImportStep _step = ImportStep.Prepare;

        partial void OnStepChanged(ImportStep value)
        {
            OnPropertyChanged(nameof(IsPreparing));
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsReviewing));
            OnPropertyChanged(nameof(CanImport));
            OnPropertyChanged(nameof(CanLeave));
        }

        public bool IsPreparing => Step == ImportStep.Prepare;
        public bool IsRunning => Step == ImportStep.Running;
        public bool IsReviewing => Step == ImportStep.Review;

        /// <summary>Leaving mid-import would orphan the run, so the way out closes while it runs.</summary>
        public bool CanLeave => Step != ImportStep.Running;

        // ================================================================ step 1: what to import

        public IEnumerable<FundingSource> FundingSources => _home.FundingSources;

        [ObservableProperty] private FundingSource _selectedFundingSource;

        partial void OnSelectedFundingSourceChanged(FundingSource value)
        {
            ReviewChoice();
        }

        [ObservableProperty] private string _filePath;
        [ObservableProperty] private string _fileName;
        [ObservableProperty] private string _fileSizeText;
        [ObservableProperty] private string _brokerName;
        [ObservableProperty] private string _dateRangeText;
        [ObservableProperty] private int _fileRowCount;

        /// <summary>What is stopping this import, in one line. Null when nothing is.</summary>
        [ObservableProperty] private string _blocker;

        public bool HasFile => !string.IsNullOrEmpty(FilePath);

        public bool CanImport =>
            Step == ImportStep.Prepare
            && HasFile
            && SelectedFundingSource != null
            && FileRowCount > 0
            && Blocker == null;

        /// <summary>
        /// Reads the chosen file and says what is in it, without sending anything.
        /// </summary>
        /// <remarks>
        /// This is the step the old screen did not have. It used to start importing the moment
        /// the file dialog closed — no confirmation, no sight of what had been picked, and no way
        /// back. Reading is cheap and local: it costs no request, and it is what lets the screen
        /// say "eight hundred and twelve trips, 3rd to 9th of September, SafeRide layout" before
        /// anybody commits to anything.
        /// </remarks>
        public async Task UseFileAsync(string path)
        {
            Clear();

            FilePath = path;
            FileName = Path.GetFileName(path);

            try
            {
                var info = new FileInfo(path);
                FileSizeText = info.Length < 1024 * 1024
                    ? $"{info.Length / 1024.0:0.#} KB"
                    : $"{info.Length / (1024.0 * 1024.0):0.##} MB";

                var reader = new CsvReaderService(path);

                _fileIsSaferide = reader.IsSaferide();
                _csvType = reader.GetCsvType();

                var mapping = MappingFileFor(_csvType);

                if (mapping == null)
                {
                    Blocker = Text("import.blocker.UnknownLayout");
                    return;
                }

                if (_csvType == CsvType.Ride2md && !reader.IsRide2mdCorrectFormat())
                {
                    Blocker = Text("import.blocker.Ride2mdFormat");
                    return;
                }

                BrokerName = _csvType.ToString();

                _records = await Task.Run(() => reader.ReadCsvWithDuplicateColumns(mapping));

                FileRowCount = _records?.Count ?? 0;

                if (FileRowCount == 0)
                {
                    Blocker = Text("import.blocker.Empty");
                    return;
                }

                DateRangeText = DescribeDates(_records);

                await CheckBeforeImportingAsync();
            }
            catch (Exception ex)
            {
                Blocker = string.Format(Text("import.blocker.Unreadable"), ex.Message);
            }
            finally
            {
                ReviewChoice();
            }
        }

        /// <summary>
        /// The rows this application can already see will be refused, found on opening the file.
        /// </summary>
        /// <remarks>
        /// ⚠️ This is the part that was missing, and the omission made the whole idea useless. The
        /// engine held these rows back correctly, but it did so <b>after</b> the dispatcher had
        /// pressed Import — so the promise of "you will be told before anything is sent" was true
        /// of the requests and false of the person. Now the file is checked the moment it is
        /// opened, on the same rules, and the warning is on screen before there is anything to
        /// press.
        ///
        /// <para>
        /// It costs no request. Mapping is pure, and the four rules it checks — an identifier, a
        /// date, a way of telling the patient apart, two addresses — need no coordinates, so the
        /// empty dictionary below is not a shortcut: geocoding simply has nothing to say about
        /// any of them.
        /// </para>
        /// </remarks>
        private async Task CheckBeforeImportingAsync()
        {
            PreflightRows.Clear();

            if (_records.Count == 0) return;

            var mapper = BuildMapper();
            var empty = new Dictionary<string, Coordinates>();
            var found = new List<ImportRow>();

            await Task.Run(() =>
            {
                for (var index = 0; index < _records.Count; index++)
                {
                    TripImportItemDto item;

                    try
                    {
                        item = mapper.MapToImportItem(_records[index], _fileIsSaferide, _csvType, empty);
                    }
                    catch (Exception ex)
                    {
                        found.Add(new ImportRow(
                            index,
                            new TripImportItemDto { TripId = _records[index].RideId ?? string.Empty },
                            ImportLocalCode.MappingFailed,
                            ex.Message));

                        continue;
                    }

                    var code = ImportPreflight.FirstProblemWith(item);

                    if (code != null) found.Add(new ImportRow(index, item, code));
                }
            });

            foreach (var row in found) PreflightRows.Add(row);

            OnPropertyChanged(nameof(HasPreflightFindings));
            OnPropertyChanged(nameof(PreflightSummary));
        }

        /// <summary>
        /// Rows that will not be sent, known before the import runs.
        /// </summary>
        public ObservableCollection<ImportRow> PreflightRows { get; }

        public bool HasPreflightFindings => PreflightRows.Count > 0;

        public string PreflightSummary =>
            PreflightRows.Count == 0
                ? null
                : string.Format(
                    Text("import.preflight.Summary"),
                    PreflightRows.Count,
                    FileRowCount - PreflightRows.Count);

        /// <summary>
        /// The mapper, built with the collections Home has already loaded.
        /// </summary>
        /// <remarks>
        /// On this path it is used for its pure mapping only: it makes no call of its own.
        /// </remarks>
        private CsvTripMapper BuildMapper() => new CsvTripMapper(
            _home.Trips, _home.SpaceTypes, _home.CapacityTypes, _home.Customers, _home.FundingSources,
            new GoogleMapsService(), new SpaceTypeService(), new CapacityTypeService(),
            new CustomerService(), new FundingSourceService(), new TripService());

        /// <summary>Forgets the file, so a wrong pick is one click to undo rather than a restart.</summary>
        [RelayCommand]
        private void Clear()
        {
            FilePath = null;
            FileName = null;
            FileSizeText = null;
            BrokerName = null;
            DateRangeText = null;
            FileRowCount = 0;
            Blocker = null;
            _records = new List<CsvTripRawModel>();
            PreflightRows.Clear();

            OnPropertyChanged(nameof(HasPreflightFindings));
            OnPropertyChanged(nameof(PreflightSummary));

            ReviewChoice();
        }

        /// <summary>
        /// Re-checks everything that could stop the import, including the one nobody expects.
        /// </summary>
        /// <remarks>
        /// The layout of the file and the funding source have to agree. A SafeRide file filed
        /// under a funding source that is not SafeRide imports without complaint and puts a whole
        /// day of somebody else's trips under the wrong payer, which is a billing problem
        /// discovered a month later. The old screen checked this too, but only after reading the
        /// file and only as a message box in the middle of the run.
        /// </remarks>
        private void ReviewChoice()
        {
            if (HasFile && SelectedFundingSource != null && Blocker == null)
            {
                var sourceIsSaferide =
                    SelectedFundingSource.Name?.StartsWith("SAFERIDE", StringComparison.OrdinalIgnoreCase) ?? false;

                if (sourceIsSaferide != _fileIsSaferide)
                {
                    Blocker = string.Format(
                        Text("import.blocker.Mismatch"), BrokerName, SelectedFundingSource.Name);
                }
            }

            OnPropertyChanged(nameof(HasFile));
            OnPropertyChanged(nameof(CanImport));
            OnPropertyChanged(nameof(FileSummary));

            // A property notification is not enough for a command: the button reads CanExecute,
            // and CanExecute is only re-asked when the command says so.
            ImportCommand.NotifyCanExecuteChanged();
        }

        public string FileSummary =>
            FileRowCount == 0
                ? null
                : string.Format(Text("import.file.Summary"), FileRowCount, BrokerName, DateRangeText);

        private static string MappingFileFor(CsvType type) => type switch
        {
            CsvType.Saferide => "SAFERIDE.json",
            CsvType.Saferide2 => "SAFERIDE2.json",
            CsvType.Ride2md => "Ride2md.json",
            _ => null
        };

        private static string DescribeDates(List<CsvTripRawModel> records)
        {
            var dates = records
                .Select(r => DateTime.TryParse(r.Date, out var parsed) ? parsed.Date : (DateTime?)null)
                .Where(d => d.HasValue)
                .Select(d => d.Value)
                .ToList();

            if (dates.Count == 0) return "—";

            var from = dates.Min();
            var to = dates.Max();

            return from == to ? from.ToString("MM/dd/yyyy") : $"{from:MM/dd/yyyy} – {to:MM/dd/yyyy}";
        }

        // ================================================================ step 2: running

        [ObservableProperty] private string _progressCaption;
        [ObservableProperty] private bool _showDetail = true;

        /// <summary>The sum of every declared request. Never resets, never goes backwards.</summary>
        public double ProgressMaximum => Steps.Sum(s => s.Total);

        public double ProgressValue => Steps.Sum(s => s.Completed);

        public string ProgressPercentText =>
            ProgressMaximum <= 0 ? "0%" : $"{Math.Round(ProgressValue / ProgressMaximum * 100)}%";

        private void RaiseOverall()
        {
            OnPropertyChanged(nameof(ProgressMaximum));
            OnPropertyChanged(nameof(ProgressValue));
            OnPropertyChanged(nameof(ProgressPercentText));
        }

        /// <summary>
        /// The running account of what the import is doing.
        /// </summary>
        /// <remarks>
        /// ⚠️ Holds patient names and addresses, deliberately — it is what a dispatcher reads when
        /// a row will not go in. It is a screen and it stays one: <b>nothing here is written to
        /// FileLogger</b>, and the only way it leaves the machine is somebody copying it, which is
        /// their decision to make. `../CLAUDE.md` §3.
        /// </remarks>
        public ObservableCollection<ImportLogEntry> Log { get; }

        [RelayCommand]
        private void ToggleDetail() => ShowDetail = !ShowDetail;

        [RelayCommand(CanExecute = nameof(CanImport))]
        private async Task ImportAsync()
        {
            Step = ImportStep.Running;

            Rows.Clear();
            Log.Clear();
            Steps.Clear();

            ProgressCaption = Text("import.progress.Starting");
            RaiseOverall();

            _clock = Stopwatch.StartNew();
            _cancellation = new CancellationTokenSource();

            Note(ImportSeverity.Info, "Start",
                string.Format(Text("import.log.Started"), FileName, SelectedFundingSource.Name));

            var reporter = new Progress<TripImportProgress>(OnProgress);

            try
            {
                var outcome = await _import.ImportAsync(
                    _records, SelectedFundingSource, _fileIsSaferide, _csvType, BuildMapper(),
                    reporter, _cancellation.Token);

                Absorb(outcome);
            }
            catch (Exception ex)
            {
                Note(ImportSeverity.Error, "Start", string.Format(Text("import.log.Crashed"), ex.Message));
                Aborted = true;
                AbortReason = ex.Message;
            }
            finally
            {
                _clock.Stop();
                ElapsedText = Describe(_clock.Elapsed);
                Step = ImportStep.Review;
                RefreshCounts();
            }
        }

        private void OnProgress(TripImportProgress report)
        {
            if (report.StepKey != null)
            {
                var step = Steps.FirstOrDefault(s => s.Key == report.StepKey);

                if (step == null)
                {
                    step = new ImportRequestStep(report.StepKey, report.StepLabel, report.StepTotal);
                    Steps.Add(step);
                }

                step.Label = report.StepLabel ?? step.Label;
                step.Total = report.StepTotal;
                step.Completed = report.StepCompleted;

                if (report.StepState.HasValue) step.State = report.StepState.Value;
                if (report.StepSummary != null) step.Summary = report.StepSummary;

                if (step.State == ImportStepState.Running)
                {
                    ProgressCaption = step.Label;
                }

                RaiseOverall();
            }

            if (!string.IsNullOrEmpty(report.Message))
            {
                Note(report.Severity, report.Stage, report.Message);
            }
        }

        private void Note(ImportSeverity severity, string stage, string text)
        {
            Log.Add(new ImportLogEntry
            {
                At = DateTime.Now,
                Severity = severity,
                Stage = stage,
                Text = text
            });
        }

        // ================================================================ step 3: what happened

        [ObservableProperty] private int _createdCount;
        [ObservableProperty] private int _updatedCount;
        [ObservableProperty] private int _failedCount;
        [ObservableProperty] private int _warningCount;
        [ObservableProperty] private int _requestCount;
        [ObservableProperty] private string _elapsedText;
        [ObservableProperty] private bool _aborted;
        [ObservableProperty] private string _abortReason;

        public ObservableCollection<ImportRow> Rows { get; }

        public ICollectionView RowsView { get; }

        [ObservableProperty] private ImportRowFilter _rowFilter = ImportRowFilter.Pending;

        partial void OnRowFilterChanged(ImportRowFilter value) => RowsView.Refresh();

        [ObservableProperty] private ImportRow _selectedRow;

        public int PendingCount => Rows.Count(r => r.State != ImportRowState.Imported);
        public int RepairedCount => Rows.Count(r => r.State == ImportRowState.Imported);
        public int ReadyToRetryCount => Rows.Count(r => r.CanRetry);

        public bool HasProblems => Rows.Count > 0;
        public bool AllRepaired => Rows.Count > 0 && PendingCount == 0;

        private bool MatchesFilter(object candidate)
        {
            if (candidate is not ImportRow row) return false;

            return RowFilter switch
            {
                ImportRowFilter.Pending => row.State != ImportRowState.Imported,
                ImportRowFilter.Imported => row.State == ImportRowState.Imported,
                _ => true
            };
        }

        /// <summary>Turns the engine's account of the run into what the screen shows.</summary>
        private void Absorb(TripImportOutcome outcome)
        {
            CreatedCount = outcome.CreatedCount;
            UpdatedCount = outcome.UpdatedCount;
            FailedCount = outcome.FailedCount;
            WarningCount = outcome.Warnings.Count;
            RequestCount = outcome.RequestCount;
            Aborted = outcome.Aborted;
            AbortReason = outcome.AbortReason;

            foreach (var row in outcome.Rows.Where(r => r.Status == TripImportStatus.Failed))
            {
                if (row.Item == null)
                {
                    // Nothing to correct here: the line could not be turned into a trip at all,
                    // so there is no object to edit. It still belongs on the grid — it is one of
                    // the rows that did not go in — and it is still exportable.
                    continue;
                }

                Rows.Add(row.Result != null
                    ? new ImportRow(row.SourceIndex, row.Item, row.Result)
                    : new ImportRow(row.SourceIndex, row.Item, row.LocalCode ?? ImportLocalCode.MappingFailed, row.Reason));
            }

            // The request count is the whole point of RE-009, so it is said out loud on screen
            // rather than left in a debug line nobody reads. An import that starts costing
            // hundreds of requests again is a regression that would otherwise surface as the host
            // blocking the application.
            Note(ImportSeverity.Success, "Done",
                string.Format(Text("import.log.Finished"),
                    outcome.StoredCount, outcome.CreatedCount, outcome.UpdatedCount,
                    outcome.FailedCount, outcome.RequestCount));

            if (Aborted)
            {
                Note(ImportSeverity.Warning, "Done", Text("import.log.AbortedAdvice"));
            }
        }

        private void RefreshCounts()
        {
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(RepairedCount));
            OnPropertyChanged(nameof(ReadyToRetryCount));
            OnPropertyChanged(nameof(HasProblems));
            OnPropertyChanged(nameof(AllRepaired));

            RetryAllCommand.NotifyCanExecuteChanged();
            RowsView.Refresh();
        }

        // ================================================================ putting it right

        /// <summary>Sends one corrected row again.</summary>
        [RelayCommand]
        private Task RetryRowAsync(ImportRow row) => SendAgainAsync(new[] { row });

        public bool CanRetryAll => ReadyToRetryCount > 0 && Step != ImportStep.Running;

        /// <summary>
        /// Sends every corrected row in one request.
        /// </summary>
        /// <remarks>
        /// Forty corrections must not be forty requests. That is precisely the burst RE-009
        /// removed, and rebuilding it one repair at a time would be the same mistake wearing a
        /// different hat.
        /// </remarks>
        [RelayCommand(CanExecute = nameof(CanRetryAll))]
        private Task RetryAllAsync() => SendAgainAsync(Rows.Where(r => r.CanRetry).ToList());

        private async Task SendAgainAsync(IReadOnlyCollection<ImportRow> rows)
        {
            if (rows == null || rows.Count == 0 || SelectedFundingSource == null) return;

            var items = rows.Select(r => r.Item).ToList();
            var reporter = new Progress<TripImportProgress>(OnProgress);

            try
            {
                var result = await _import.RetryAsync(items, SelectedFundingSource, reporter);

                for (var i = 0; i < rows.Count; i++)
                {
                    var row = rows.ElementAt(i);
                    var verdict = i < result.Results.Count ? result.Results[i] : null;

                    if (verdict != null && verdict.Status != TripImportStatus.Failed)
                    {
                        row.MarkImported();

                        if (verdict.Status == TripImportStatus.Created) CreatedCount++;
                        else UpdatedCount++;

                        FailedCount = Math.Max(0, FailedCount - 1);
                    }
                    else
                    {
                        row.MarkRefusedAgain(verdict?.Message);
                    }
                }

                RequestCount++;
            }
            catch (Exception ex)
            {
                Note(ImportSeverity.Error, "Retry", string.Format(Text("import.log.RetryFailed"), ex.Message));
            }
            finally
            {
                RefreshCounts();
            }
        }

        /// <summary>The indexes of the rows that still have not gone in, for the export.</summary>
        public IEnumerable<int> PendingSourceIndexes =>
            Rows.Where(r => r.State != ImportRowState.Imported)
                .Select(r => r.SourceIndex)
                .OrderBy(i => i);

        public string SuggestedExportName =>
            FailedRowsCsvWriter.SuggestName(
                SelectedFundingSource?.Name, DateTime.Now, Text("import.export.NotImportedWord"));

        /// <summary>Writes the rows still outstanding back out as the file they came from.</summary>
        public int ExportPending(string destination)
        {
            var written = FailedRowsCsvWriter.Write(FilePath, destination, PendingSourceIndexes);

            Note(ImportSeverity.Info, "Export",
                string.Format(Text("import.log.Exported"), written, Path.GetFileName(destination)));

            return written;
        }

        // ================================================================ leaving

        [RelayCommand]
        private void StartOver()
        {
            Rows.Clear();
            Log.Clear();
            Clear();

            CreatedCount = UpdatedCount = FailedCount = WarningCount = RequestCount = 0;
            Aborted = false;
            AbortReason = null;
            ElapsedText = null;
            Step = ImportStep.Prepare;

            RefreshCounts();
        }

        private static string Describe(TimeSpan elapsed) =>
            elapsed.TotalMinutes < 1
                ? $"{elapsed.TotalSeconds:0.#}s"
                : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";

        private static string Text(string key) => LocalizationService.Instance[key];

        /// <summary>
        /// Re-reads every string from the language file.
        /// </summary>
        /// <remarks>
        /// Same reason and same shape as HomeViewModel: this derives from ObservableObject, not
        /// BaseViewModel, so nothing subscribes it to the language service. The view does that on
        /// Loaded and drops it on Unloaded, which is what keeps a tab that is opened fifty times
        /// from leaving fifty view models alive.
        /// </remarks>
        public void RefreshLocalizedText()
        {
            OnPropertyChanged(string.Empty);

            // The log lines are sentences already built, so no notification reaches inside them.
            // They keep the language they were written in, which is honest: they are a record of
            // what happened at the time it happened.
        }

        // ================================================================ the words

        public string HeaderLabel => Text("import.Header");
        public string BackLabel => Text("home.BackToHome");
        public string StepChooseLabel => Text("import.step.Choose");
        public string StepRunLabel => Text("import.step.Run");
        public string StepReviewLabel => Text("import.step.Review");
        public string FundingSourceHint => Text("import.FundingSourceHint");
        public string BrowseLabel => Text("import.Browse");
        public string ChangeFileLabel => Text("import.ChangeFile");
        public string NoFileLabel => Text("import.NoFile");
        public string NoFileHint => Text("import.NoFileHint");
        public string ImportNowLabel => Text("import.ImportNow");
        public string SafeToRepeatLabel => Text("import.SafeToRepeat");
        public string DetailLabel => Text("import.Detail");
        public string EvidenceLabel => Text("import.Evidence");
        public string PreflightHeader => Text("import.preflight.Header");
        public string OverallLabel => Text("import.Overall");
        public string CaughtHereLabel => Text("import.CaughtHere");
        public string ResultCreatedLabel => Text("import.result.Created");
        public string ResultUpdatedLabel => Text("import.result.Updated");
        public string ResultFailedLabel => Text("import.result.Failed");
        public string ResultWarningsLabel => Text("import.result.Warnings");
        public string ResultRequestsLabel => Text("import.result.Requests");
        public string ResultElapsedLabel => Text("import.result.Elapsed");
        public string ProblemsHeader => Text("import.problems.Header");
        public string FilterPendingLabel => Text("import.filter.Pending");
        public string FilterImportedLabel => Text("import.filter.Imported");
        public string FilterAllLabel => Text("import.filter.All");
        public string RetryLabel => Text("import.Retry");
        public string RetryAllLabel => Text("import.RetryAll");
        public string ExportFailedLabel => Text("import.ExportFailed");
        public string StartOverLabel => Text("import.StartOver");
        public string ConflictHeader => Text("import.ConflictHeader");
        public string SuggestionHeader => Text("import.SuggestionHeader");
        public string AllRepairedLabel => Text("import.AllRepaired");
        public string NoProblemsLabel => Text("import.NoProblems");
        public string ColumnRowLabel => Text("import.column.Row");
        public string ColumnTripIdLabel => Text("import.column.TripId");
        public string ColumnPatientLabel => Text("import.column.Patient");
        public string ColumnDateLabel => Text("import.column.Date");
        public string ColumnPickupLabel => Text("import.column.Pickup");
        public string ColumnDropoffLabel => Text("import.column.Dropoff");
        public string ColumnProblemLabel => Text("import.column.Problem");
        public string ColumnStateLabel => Text("import.column.State");
        public string CorrelationLabel => Text("import.Correlation");
        public string CannotFixHereLabel => Text("import.CannotFixHere");
    }
}
