using System.Collections.ObjectModel;
using System.Windows.Input;
using Raphael.Desktop.Commands;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels.CallRequests;

/// <summary>
/// Closing a request: why, and a note when the reason is "Other".
/// </summary>
/// <remarks>
/// The reason is required because it is the data that will one day let the driver pick the cause
/// themselves. A note is only asked for with "Other", and its hint says not to name the patient:
/// it is stored, and it is the one free-text field in the whole feature.
/// </remarks>
public sealed class CallResolveViewModel : BaseViewModel
{
    private bool _isOpen;

    private string _note = string.Empty;

    public CallResolveViewModel()
    {
        foreach (var code in CallRequestReasonCodes.All)
            Reasons.Add(new CallReasonOptionViewModel(code, OnReasonChanged));

        ConfirmCommand = new RelayCommandObject(_ => Confirm(), _ => CanConfirm);
        CancelCommand = new RelayCommandObject(_ => IsOpen = false);
    }

    private static LocalizationService L => LocalizationService.Instance;

    public ObservableCollection<CallReasonOptionViewModel> Reasons { get; } = [];

    /// <summary>Raised with the reason code and the note, if any.</summary>
    public event Action<string, string?>? Confirmed;

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    public bool IsOpen
    {
        get => _isOpen;
        private set => SetProperty(ref _isOpen, value);
    }

    public string Note
    {
        get => _note;
        set
        {
            if (SetProperty(ref _note, value ?? string.Empty))
                Raise();
        }
    }

    public CallReasonOptionViewModel? Selected => Reasons.FirstOrDefault(r => r.IsSelected);

    public bool NoteRequired => Selected?.Code == CallRequestReasonCodes.Other;

    public int NoteMaxLength => CallRequestReasonCodes.NoteMaxLength;

    public bool CanConfirm =>
        Selected is not null &&
        (!NoteRequired || !string.IsNullOrWhiteSpace(Note)) &&
        Note.Length <= CallRequestReasonCodes.NoteMaxLength;

    public string Title => L["CallRequestResolveTitle"];

    public string NoteHint => L["CallRequestNoteHint"];

    public string ConfirmText => L["CallRequestResolveConfirm"];

    public string CancelText => L["CallRequestCancel"];

    public void Begin()
    {
        foreach (var reason in Reasons)
            reason.IsSelected = false;

        Note = string.Empty;
        IsOpen = true;
        Raise();
    }

    public void Close() => IsOpen = false;

    private void Confirm()
    {
        if (!CanConfirm || Selected is null)
            return;

        var note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();

        IsOpen = false;
        Confirmed?.Invoke(Selected.Code, note);
    }

    private void OnReasonChanged(CallReasonOptionViewModel option)
    {
        if (option.IsSelected)
        {
            foreach (var other in Reasons.Where(r => !ReferenceEquals(r, option)))
                other.IsSelected = false;
        }

        Raise();
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(NoteRequired));
        OnPropertyChanged(nameof(CanConfirm));
        CommandManager.InvalidateRequerySuggested();
    }
}

public sealed class CallReasonOptionViewModel : BaseViewModel
{
    private readonly Action<CallReasonOptionViewModel> _changed;

    private bool _isSelected;

    public CallReasonOptionViewModel(string code, Action<CallReasonOptionViewModel> changed)
    {
        Code = code;
        _changed = changed;
    }

    public string Code { get; }

    public string Label => CallRequestText.Reason(Code);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                _changed(this);
        }
    }
}
