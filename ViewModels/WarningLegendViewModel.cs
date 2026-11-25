using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Raphael.Desktop.ViewModels
{
    /// <summary>The limits were saved: an open report asks for its warnings again.</summary>
    public sealed class WarningLimitsSaved { }

    /// <summary>RE-092: what each warning is, why it warns and when; the platform administrator also sets the limits.</summary>
    public partial class WarningLegendViewModel : ObservableObject
    {
        private readonly IProductivityWarningService _service = new ProductivityWarningService();
        private string? _openOn;
        private CancellationTokenSource? _preview;

        public WarningLegendViewModel(string? openOn = null)
        {
            _openOn = openOn;
            View = CollectionViewSource.GetDefaultView(Items);
            View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(WarningLegendItem.Group)));
        }

        public ObservableCollection<WarningLegendItem> Items { get; } = new();
        public ICollectionView View { get; }
        public bool CanEdit => SessionManager.IsPlatformAdmin;
        public bool CannotEdit => !CanEdit;

        [ObservableProperty] private WarningLegendItem? _selected;
        [ObservableProperty] private string _message = "";
        [ObservableProperty] private bool _isBusy;

        public async Task LoadAsync()
        {
            IsBusy = true;
            try
            {
                Fill(await _service.LimitsAsync() ?? new());
                await LoadRuleAsync();
                Selected = Items.FirstOrDefault(i => i.Code == _openOn) ?? Items.FirstOrDefault();
                if (Items.Count == 0) Message = WarningLook.T("warn.LoadError");
                else if (CanEdit) SchedulePreview();
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Warning limits: {ex.Message}");
                Message = WarningLook.T("warn.LoadError");
            }
            finally { IsBusy = false; }
        }

        /// <summary>The window is already open and another warning is asked for.</summary>
        public void OpenOn(string? code)
        {
            _openOn = code;
            if (!string.IsNullOrEmpty(code) && Items.FirstOrDefault(i => i.Code == code) is { } item) Selected = item;
        }

        // ---- the inspection minutes: they decide pay, so they are apart and ask before saving ----
        private PayRuleDto? _rule;
        [ObservableProperty] private string _ruleText = "";
        [ObservableProperty] private string _ruleHint = "";
        [ObservableProperty] private string _ruleMessage = "";
        [ObservableProperty] private string _ruleChanged = "";

        private async Task LoadRuleAsync()
        {
            if (!CanEdit) return;
            Show(await _service.PayRuleAsync());
        }

        private void Show(PayRuleDto? rule)
        {
            _rule = rule;
            if (rule is null) return;
            RuleText = rule.InspectionMinutes.ToString(CultureInfo.CurrentCulture);
            RuleHint = string.Format(WarningLook.T("warn.PayRuleHint"), rule.PreparationMinutes);
            RuleChanged = rule.UpdatedBy is null || rule.UpdatedAtUtc is not DateTime at ? ""
                : string.Format(WarningLook.T("warn.ChangedBy"), rule.UpdatedBy, DateTime.SpecifyKind(at, DateTimeKind.Utc).ToLocalTime().ToString("g"));
        }

        /// <summary>Asks first: set by the window, which owns the question.</summary>
        public Func<string, bool>? Confirm { get; set; }

        [RelayCommand]
        private async Task SaveRuleAsync()
        {
            RuleMessage = "";
            if (_rule is null) return;
            if (!int.TryParse(RuleText, NumberStyles.None, CultureInfo.CurrentCulture, out var minutes) || minutes < _rule.Min || minutes > _rule.Max)
            { RuleMessage = string.Format(WarningLook.T("warn.Between"), _rule.Min, _rule.Max); return; }
            if (minutes == _rule.InspectionMinutes) return;
            if (Confirm?.Invoke(string.Format(WarningLook.T("warn.PayRuleConfirm"), _rule.InspectionMinutes, minutes)) != true) return;
            try
            {
                Show(await _service.SavePayRuleAsync(minutes));
                RuleMessage = string.Format(WarningLook.T("warn.PayRuleSaved"), minutes);
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Pay rule save: {ex.Message}");
                RuleMessage = WarningLook.T("warn.SaveError");
            }
        }

        private void Fill(List<WarningLimitDto> limits)
        {
            foreach (var i in Items) i.PropertyChanged -= OnItem;
            Items.Clear();
            foreach (var d in limits.OrderBy(l => WarningLook.Rank(l.Level)))
            {
                var item = new WarningLegendItem(d);
                item.PropertyChanged += OnItem;
                Items.Add(item);
            }
        }

        private void OnItem(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(WarningLegendItem.LimitText) or nameof(WarningLegendItem.IsOn))) return;
            Message = "";
            SchedulePreview();
        }

        private List<WarningLimitUpdateDto> Changes() =>
            Items.Select(i => new WarningLimitUpdateDto { Code = i.Code, Enabled = i.IsOn, Limit = i.Limit }).ToList();

        /// <summary>After a pause in the typing, and only the last one counts.</summary>
        private async void SchedulePreview()
        {
            _preview?.Cancel();
            if (!CanEdit || Items.Any(i => !i.IsValid)) return;
            var cts = _preview = new CancellationTokenSource();
            try
            {
                foreach (var i in Items) i.PreviewText = WarningLook.T("warn.Previewing");
                await Task.Delay(500, cts.Token);
                foreach (var p in await _service.PreviewAsync(Changes(), cts.Token))
                    if (Items.FirstOrDefault(i => i.Code == p.Code) is { } item) item.Show(p);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                FileLogger.Log($"Warning preview: {ex.Message}");
                foreach (var i in Items) i.PreviewText = "";
            }
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            if (Items.FirstOrDefault(i => !i.IsValid) is { } bad) { Selected = bad; Message = WarningLook.T("warn.BadNumber"); return; }
            IsBusy = true;
            try
            {
                var code = Selected?.Code;
                Fill(await _service.SaveLimitsAsync(Changes()));
                Selected = Items.FirstOrDefault(i => i.Code == code) ?? Items.FirstOrDefault();
                WeakReferenceMessenger.Default.Send(new WarningLimitsSaved());
                SchedulePreview();
                Message = WarningLook.T("warn.Saved");
            }
            catch (Exception ex)
            {
                FileLogger.Log($"Warning limits save: {ex.Message}");
                Message = WarningLook.T("warn.SaveError");
            }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        private void Reset()
        {
            foreach (var i in Items) i.Reset();
        }
    }

    public partial class WarningLegendItem : ObservableObject
    {
        private readonly WarningLimitDto _data;

        public WarningLegendItem(WarningLimitDto data)
        {
            _data = data;
            _isOn = data.Enabled;
            _limitText = data.Limit?.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
        }

        public string Code => _data.Code;
        public string Title => WarningLook.Text(Code, "Title");
        public string What => WarningLook.Text(Code, "What");
        public string Why => WarningLook.Text(Code, "Why");
        public string How => WarningLook.Text(Code, "How");
        public string Pay => WarningLook.Text(Code, "Pay");
        public string Do => WarningLook.Text(Code, "Do");
        public bool IsShift => Code == "outside_allowed_hours";
        public bool IsNotShift => !IsShift;
        public ShiftCalculationDto? ShiftExample => IsShift ? Views.Reports.ShiftPicture.Example() : null;
        public string When => IsOn ? WarningLook.When(Code, Limit ?? _data.Limit) : WarningLook.T("warn.Off");
        public string Group => WarningLook.T("warn.Group." + _data.Level);
        public string LevelText => WarningLook.T("warn.Level." + _data.Level);
        public string Icon => WarningLook.Icon(_data.Level);
        public Brush Ink => WarningLook.Ink(_data.Level);
        public Brush Fill => WarningLook.Fill(_data.Level);
        public double Faint => IsOn ? 1 : 0.5;

        public bool HasLimit => _data.Limit is not null;
        public bool HasNoLimit => !HasLimit;
        public string UnitText => _data.Unit is null ? "" : WarningLook.T("warn.Unit." + _data.Unit);
        public string FactoryText => HasLimit ? string.Format(WarningLook.T("warn.Factory"), WarningLook.N(_data.DefaultLimit), UnitText)
            + " · " + string.Format(WarningLook.T("warn.Between"), WarningLook.N(_data.Min), WarningLook.N(_data.Max)) : "";
        public string ChangedText => _data.UpdatedBy is null || _data.UpdatedAtUtc is not DateTime at ? ""
            : string.Format(WarningLook.T("warn.ChangedBy"), _data.UpdatedBy, DateTime.SpecifyKind(at, DateTimeKind.Utc).ToLocalTime().ToString("g"));

        [ObservableProperty] [NotifyPropertyChangedFor(nameof(When), nameof(Faint))] private bool _isOn;
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(When), nameof(ErrorText))] private string _limitText;
        [ObservableProperty] private string _previewText = "";

        public double? Limit => double.TryParse(LimitText?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var v) ? v
            : double.TryParse(LimitText?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : null;

        public bool IsValid => !HasLimit || (Limit is double v && v >= _data.Min && v <= _data.Max);
        public string ErrorText => IsValid ? "" : string.Format(WarningLook.T("warn.Between"), WarningLook.N(_data.Min), WarningLook.N(_data.Max));

        public void Show(WarningPreviewDto p) => PreviewText = !IsOn ? "" : p.Cases == p.Days
            ? string.Format(WarningLook.T("warn.PreviewOne"), p.Days, p.OfDays)
            : string.Format(WarningLook.T("warn.Preview"), p.Days, p.OfDays, p.Cases);

        public void Reset()
        {
            IsOn = true;
            LimitText = _data.DefaultLimit?.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
        }
    }
}
