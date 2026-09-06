using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
            model.PropertyChanged += OnViewModelChanged;
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

        private void OnViewModelChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ImportTripsViewModel.Log))
            {
                ScrollLogToEnd();
            }
        }

        /// <summary>Keeps the newest line of the running account in view.</summary>
        private void ScrollLogToEnd()
        {
            if (LogList?.Items.Count > 0)
            {
                LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
            }
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
