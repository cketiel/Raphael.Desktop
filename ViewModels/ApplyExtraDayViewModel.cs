using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;
using System.Globalization;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// Adds an additional rate (a holiday fee, say) to every open trip of a day at once, or takes it
    /// off (RE-074). It shows how many trips will change and how much money before doing anything.
    /// </summary>
    public partial class ApplyExtraDayViewModel : ObservableObject
    {
        private static string L(string key) => LocalizationService.Instance[key];

        private readonly ITripChargesService _service;
        private readonly int _ruleId;

        public ApplyExtraDayViewModel(ITripChargesService service, int ruleId, string description, string spaceTypeName)
        {
            _service = service;
            _ruleId = ruleId;
            TextIntro = string.Format(L("extraDay.Intro"), description, spaceTypeName);
            _ = PreviewAsync();
        }

        public string TextTitle => L("extraDay.Title");
        public string TextIntro { get; }
        public string TextDate => L("extraDay.Date");
        public string TextAdd => L("extraDay.ModeAdd");
        public string TextRemove => L("extraDay.ModeRemove");
        public string TextApply => L("extraDay.Apply");
        public string TextCancel => L("extraDay.Close");
        public string TextUndo => L("extraDay.Undo");

        [ObservableProperty] private bool _canUndo;
        [ObservableProperty] private string _undoText = "";

        [ObservableProperty] private DateTime _date = DateTime.Today;
        [ObservableProperty] private bool _removeMode;
        [ObservableProperty] private string _previewText = "…";
        [ObservableProperty] private bool _canApply;

        public event Action<bool>? RequestClose;

        partial void OnDateChanged(DateTime value) => _ = PreviewAsync();
        partial void OnRemoveModeChanged(bool value) => _ = PreviewAsync();

        private ExtraBulkPreviewDto? _preview;

        private async Task PreviewAsync()
        {
            CanApply = false;
            try
            {
                _preview = await _service.PreviewDayAsync(_ruleId, Date.Date, RemoveMode);
                if (_preview == null) { PreviewText = ""; return; }

                ShowPreview();
            }
            catch (Exception ex)
            {
                PreviewText = ex.Message;
            }
        }

        private void ShowPreview()
        {
            if (_preview == null) return;
            PreviewText = string.Format(L(RemoveMode ? "extraDay.PreviewRemove" : "extraDay.PreviewAdd"),
                _preview.Trips, Money(_preview.Amount), _preview.Unchanged, _preview.Frozen);
            CanApply = _preview.Trips > 0;

            // The last application to this day, ready to be taken back.
            CanUndo = _preview.LastBatchAtUtc != null && _preview.LastBatchTrips > 0;
            UndoText = CanUndo
                ? string.Format(L("extraDay.LastBatch"), _preview.LastBatchTrips, Money(_preview.LastBatchAmount),
                    DateTime.SpecifyKind(_preview.LastBatchAtUtc!.Value, DateTimeKind.Utc).ToLocalTime().ToString("MM/dd/yyyy HH:mm", CultureInfo.InvariantCulture),
                    _preview.LastBatchBy ?? "")
                : "";
        }

        /// <summary>
        /// Takes back the last application: only the trips that got the rate then lose it, never one
        /// that had it added by hand. For the dispatcher who applied it to the wrong day.
        /// </summary>
        [RelayCommand]
        private async Task UndoAsync()
        {
            if (_preview?.LastBatchAtUtc is not DateTime at) return;

            var question = string.Format(L("extraDay.ConfirmUndo"), _preview.Description, _preview.LastBatchTrips,
                Date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture), Money(_preview.LastBatchAmount));
            if (MessageBox.Show(question, TextTitle, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            try
            {
                var undone = _preview.LastBatchTrips;
                _preview = await _service.UndoDayAsync(_ruleId, Date.Date, at);
                RemoveMode = false;
                ShowPreview();
                MessageBox.Show(string.Format(L("extraDay.Undone"), undone), TextTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show(ex.Message, TextTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private async Task ApplyAsync()
        {
            if (_preview == null || _preview.Trips == 0) return;

            var question = string.Format(L(RemoveMode ? "extraDay.ConfirmRemove" : "extraDay.ConfirmAdd"),
                _preview.Description, _preview.Trips, Date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture), Money(_preview.Amount));
            if (MessageBox.Show(question, TextTitle, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            try
            {
                var done = await _service.ApplyDayAsync(_ruleId, Date.Date, RemoveMode);
                MessageBox.Show(string.Format(L("extraDay.Done"), done?.Trips ?? 0), TextTitle, MessageBoxButton.OK, MessageBoxImage.Information);

                // The dialog stays open on the day as it is now, with "Undo" ready if it was a mistake.
                if (done != null) { _preview = done; ShowPreview(); }
            }
            catch (Exception ex)
            {
                Helpers.UiError.Show(ex.Message, TextTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [RelayCommand]
        private void Cancel() => RequestClose?.Invoke(false);

        private static string Money(decimal v) => "$" + v.ToString("N2", CultureInfo.InvariantCulture);
    }
}
