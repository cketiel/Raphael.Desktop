using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>
    /// RE-082: one driver's pay. Hourly or per passenger mile, exclusive; both amounts are kept.
    /// A change starts on a date and never rewrites the days before it, so past reports keep
    /// the rate they were paid at.
    /// </summary>
    /// <remarks>
    /// Opening the window reads nothing: the rate in force comes with the Users list. The history
    /// is read only when asked for, ten entries at a time, so a driver with years of changes
    /// neither slows the window down nor stretches it past the screen.
    /// </remarks>
    public partial class DriverPayRateViewModel : ObservableObject
    {
        /// <summary>Entries per page of the history.</summary>
        public const int PageSize = 10;

        private readonly IDriverPayRateService _service;
        private static string T(string key) => LocalizationService.Instance[key];

        public DriverPayRateViewModel(User driver, IDriverPayRateService service)
        {
            Driver = driver;
            _service = service;
            _effectiveFrom = DateTime.Today;
            Current = driver.PayRate;

            // The form starts from what is in force, so a change touches only what the user edits.
            if (Current != null)
            {
                _isHourly = Current.RateType == DriverPayRateType.Hourly;
                _hourlyText = Current.HourlyRate.ToString("0.00", CultureInfo.InvariantCulture);
                _perMileText = Current.PerMileRate.ToString("0.00", CultureInfo.InvariantCulture);
            }
            LocalizationService.Instance.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);
        }

        public User Driver { get; }
        public LocalizationService L => LocalizationService.Instance;

        // ---------------------------------------------------------------- what is in force

        public DriverPayRateDto? Current { get; }
        public bool HasCurrent => Current != null;
        public string CurrentText => Current?.Label ?? T("pay.NoRate");
        public string CurrentSince => Current is null ? T("pay.NoRateHint") : string.Format(T("pay.Since"), Current.EffectiveFrom.ToString("MM/dd/yyyy"));
        public string CurrentKind => Current is null ? string.Empty : T(Current.RateType == DriverPayRateType.Hourly ? "pay.Hourly" : "pay.PerMile");

        // ---------------------------------------------------------------- the change

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPerMile), nameof(Preview), nameof(IsChange))]
        private bool _isHourly = true;

        public bool IsPerMile
        {
            get => !IsHourly;
            set => IsHourly = !value;
        }

        [ObservableProperty][NotifyPropertyChangedFor(nameof(Preview), nameof(IsChange))] private string _hourlyText = "15.00";
        [ObservableProperty][NotifyPropertyChangedFor(nameof(Preview), nameof(IsChange))] private string _perMileText = "1.00";
        [ObservableProperty][NotifyPropertyChangedFor(nameof(Preview))] private DateTime _effectiveFrom;
        [ObservableProperty] private string _note = string.Empty;
        [ObservableProperty] private string _errorText = string.Empty;
        [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SaveCommand))] private bool _isBusy;

        /// <summary>What the change does, in one sentence, before saving it.</summary>
        public string Preview
        {
            get
            {
                if (!TryAmount(IsHourly ? HourlyText : PerMileText, out var amount)) return T("pay.BadAmount");
                var label = IsHourly ? $"${amount:N2}/h" : $"${amount:N2}/mi";
                return string.Format(T(IsHourly ? "pay.PreviewHourly" : "pay.PreviewPerMile"), label, EffectiveFrom.ToString("MM/dd/yyyy"));
            }
        }

        /// <summary>False while the form still says exactly what is in force: nothing to save yet.</summary>
        public bool IsChange
        {
            get
            {
                if (Current is null) return true;
                if (!TryAmount(HourlyText, out var h) || !TryAmount(PerMileText, out var m)) return true;
                return (IsHourly ? DriverPayRateType.Hourly : DriverPayRateType.PerMile) != Current.RateType
                       || h != Current.HourlyRate || m != Current.PerMileRate;
            }
        }

        [RelayCommand(CanExecute = nameof(CanSave))]
        private async Task SaveAsync(Window? window)
        {
            ErrorText = string.Empty;
            if (!TryAmount(HourlyText, out var hourly) || !TryAmount(PerMileText, out var perMile)) { ErrorText = T("pay.BadAmount"); return; }
            if ((IsHourly ? hourly : perMile) <= 0) { ErrorText = T("pay.ZeroInForce"); return; }

            IsBusy = true;
            try
            {
                await _service.AddAsync(Driver.Id, new DriverPayRateCreateDto
                {
                    RateType = IsHourly ? DriverPayRateType.Hourly : DriverPayRateType.PerMile,
                    HourlyRate = hourly,
                    PerMileRate = perMile,
                    EffectiveFrom = EffectiveFrom.Date,
                    Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim()
                });
                if (window != null) { window.DialogResult = true; window.Close(); }
            }
            catch (Exception ex)
            {
                ErrorText = ex.Message;
            }
            finally { IsBusy = false; }
        }

        private bool CanSave(Window? _) => !IsBusy;

        [RelayCommand]
        private static void Cancel(Window? window) => window?.Close();

        // ---------------------------------------------------------------- history, on demand

        public ObservableCollection<PayRateHistoryLine> History { get; } = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HistoryToggleText))]
        private bool _isHistoryOpen;

        [ObservableProperty] private bool _canLoadMore;
        [ObservableProperty] private bool _isHistoryLoading;

        public string HistoryToggleText => T(IsHistoryOpen ? "pay.HideHistory" : "pay.ShowHistory");
        public bool HistoryIsEmpty => IsHistoryOpen && !IsHistoryLoading && History.Count == 0;

        [RelayCommand]
        private async Task ToggleHistoryAsync()
        {
            IsHistoryOpen = !IsHistoryOpen;
            // Read once; closing and opening it again shows what was already read.
            if (IsHistoryOpen && History.Count == 0) await LoadPageAsync();
        }

        [RelayCommand]
        private Task LoadMoreAsync() => LoadPageAsync();

        private async Task LoadPageAsync()
        {
            IsHistoryLoading = true;
            OnPropertyChanged(nameof(HistoryIsEmpty));
            try
            {
                var page = await _service.GetHistoryAsync(Driver.Id, History.Count, PageSize);
                var today = DateTime.Today;
                foreach (var r in page)
                    History.Add(new PayRateHistoryLine(r, isFuture: r.EffectiveFrom > today, isCurrent: Current != null && r.Id == Current.Id));
                CanLoadMore = page.Count == PageSize;
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Pay rate: could not read the history of user {Driver.Id}. {ex.Message}");
                ErrorText = T("pay.LoadError");
            }
            finally
            {
                IsHistoryLoading = false;
                OnPropertyChanged(nameof(HistoryIsEmpty));
            }
        }

        /// <summary>Accepts "15", "15.5" or "15,50"; from 0 to 1000 (the API's own guard).</summary>
        private static bool TryAmount(string? text, out decimal value)
        {
            var t = (text ?? string.Empty).Trim().Replace("$", "").Replace(',', '.');
            return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out value) && value >= 0 && value <= 1000;
        }
    }

    /// <summary>One line of the history: what, from when, who set it.</summary>
    public class PayRateHistoryLine
    {
        public PayRateHistoryLine(DriverPayRateDto data, bool isFuture, bool isCurrent)
        {
            Data = data;
            IsFuture = isFuture;
            IsCurrent = isCurrent;
        }

        public DriverPayRateDto Data { get; }
        public bool IsFuture { get; }
        public bool IsCurrent { get; }

        private static string T(string key) => LocalizationService.Instance[key];

        public string Since => Data.EffectiveFrom.ToString("MM/dd/yyyy");
        public string Label => Data.Label;
        public string Kind => T(Data.RateType == DriverPayRateType.Hourly ? "pay.Hourly" : "pay.PerMile");
        public bool IsPerMile => Data.RateType == DriverPayRateType.PerMile;
        public string Other => Data.RateType == DriverPayRateType.Hourly
            ? string.Format(T("pay.OtherKept"), $"${Data.PerMileRate:N2}/mi")
            : string.Format(T("pay.OtherKept"), $"${Data.HourlyRate:N2}/h");
        public string Who => string.Format(T("pay.SetBy"), Data.CreatedByName ?? T("pay.System"),
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(Data.CreatedAtUtc, DateTimeKind.Utc), TimeZoneInfo.Local).ToString("MM/dd/yyyy HH:mm"));
        public string? Note => Data.Note;
        public bool HasNote => !string.IsNullOrWhiteSpace(Data.Note);
        public string State => IsFuture ? T("pay.Scheduled") : IsCurrent ? T("pay.InForce") : string.Empty;
        public bool HasState => State.Length > 0;
    }
}
