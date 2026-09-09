using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Raphael.Desktop.Services;
using Raphael.Desktop.ViewModels;

namespace Raphael.Desktop.Views
{
    /// <summary>
    /// The import screen. Everything with a decision in it is in the view model; this holds the
    /// three things that cannot be: two file dialogs and the stepper's paint.
    /// </summary>
    public partial class ImportTripsView : UserControl
    {
        public ImportTripsView()
        {
            InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        public ImportTripsViewModel ViewModel => DataContext as ImportTripsViewModel;

        /// <summary>Raised when the dispatcher asks to go back to the trips list.</summary>
        public event EventHandler BackRequested;

        /// <summary>Hands this screen the Home view model it shares its collections with.</summary>
        public void Attach(HomeViewModel home)
        {
            var model = new ImportTripsViewModel(home);

            DataContext = model;

            // ⚠️ Watching the view model for "Log" never fired: the collection is the same object
            // throughout, so the property never changes - only its contents do. The console has to
            // listen to the collection itself or it never follows what it is printing.
            model.Log.CollectionChanged += (_, _) => ScrollLogToEnd();
        }

        // ------------------------------------------------------------------ language

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

            ViewModel?.RefreshLocalizedText();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
            => LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;

        private void OnLanguageChanged() => ViewModel?.RefreshLocalizedText();

        // ------------------------------------------------------------------ the stepper

        private bool _scrollQueued;

        /// <summary>
        /// Keeps the newest line of the running account in view.
        /// </summary>
        /// <remarks>
        /// ⚠️ Never scroll from inside the CollectionChanged handler itself. The ListBox's own
        /// container generator is another subscriber to that same event, and when this handler ran
        /// first, ScrollIntoView forced containers to be generated for a collection the generator
        /// had not been told about yet - which WPF reports as "the accumulated count is different
        /// from the actual count" and throws.
        ///
        /// <para>
        /// Queued at Background priority instead, so it runs after every subscriber has finished
        /// and the generator agrees with the collection. Coalesced, because an import writes
        /// hundreds of lines and each one queuing its own scroll would spend more time scrolling
        /// than importing.
        /// </para>
        /// </remarks>
        private void ScrollLogToEnd()
        {
            if (_scrollQueued) return;

            _scrollQueued = true;

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    _scrollQueued = false;

                    var count = LogList?.Items.Count ?? 0;

                    if (count == 0) return;

                    LogList.ScrollIntoView(LogList.Items[count - 1]);
                }),
                DispatcherPriority.Background);
        }

        // ------------------------------------------------------------------ the two dialogs

        private void Back_Click(object sender, RoutedEventArgs e)
            => BackRequested?.Invoke(this, EventArgs.Empty);

        private async void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Title = LocalizationService.Instance["import.Browse"]
            };

            if (dialog.ShowDialog() != true) return;

            await ViewModel.UseFileAsync(dialog.FileName);
        }

        /// <summary>
        /// Writes the rows still outstanding back out as the file they came from.
        /// </summary>
        /// <remarks>
        /// ⚠️ It holds patient names, addresses and phone numbers. It goes exactly where the
        /// dispatcher points it and nowhere else — never a temporary folder, never a default path
        /// somebody forgets about. `../CLAUDE.md` §3.
        /// </remarks>
        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var model = ViewModel;

            if (model == null || !model.PendingSourceIndexes.Any()) return;

            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = model.SuggestedExportName,
                AddExtension = true,
                DefaultExt = ".csv",
                OverwritePrompt = true
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var written = model.ExportPending(dialog.FileName);

                MessageBox.Show(
                    string.Format(LocalizationService.Instance["import.export.Done"], written, dialog.FileName),
                    LocalizationService.Instance["import.ExportFailed"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(LocalizationService.Instance["import.export.Failed"], ex.Message),
                    LocalizationService.Instance["import.ExportFailed"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
