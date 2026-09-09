using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Services.Import;

namespace Raphael.Desktop.Models.Import
{
    /// <summary>
    /// One row that did not go in, and everything needed to put it right.
    /// </summary>
    /// <remarks>
    /// It carries the mapped item rather than the CSV line, because that is what gets sent again:
    /// editing writes straight through to the object the retry posts, so there is no third copy
    /// of the row to fall out of step with the other two.
    ///
    /// <para>
    /// <see cref="SourceIndex"/> is the line it came from in the original file, counted from the
    /// first row of data. It is what lets the export write the failures back out in the exact
    /// format they were read in — by copying the original lines rather than rebuilding them.
    /// </para>
    /// </remarks>
    public partial class ImportRow : ObservableObject
    {
        private readonly TripImportItemDto _item;
        private readonly string? _riderIdAsRejected;

        public ImportRow(int sourceIndex, TripImportItemDto item, TripImportItemResultDto result)
        {
            SourceIndex = sourceIndex;
            _item = item;
            _riderIdAsRejected = item.RiderId;

            ErrorCode = result.ErrorCode;
            ServerMessage = result.Message;
            CorrelationId = result.CorrelationId;
            Conflict = result.Conflict;

            Problem = ImportProblemCatalog.Describe(ErrorCode);
        }

        /// <summary>A row this application refused before anything was sent.</summary>
        public ImportRow(int sourceIndex, TripImportItemDto item, string localCode, string? detail = null)
        {
            SourceIndex = sourceIndex;
            _item = item;
            _riderIdAsRejected = item.RiderId;

            ErrorCode = localCode;
            ServerMessage = detail;

            Problem = ImportProblemCatalog.Describe(localCode);
        }

        /// <summary>Zero-based index of the row in the file's data lines.</summary>
        public int SourceIndex { get; }

        /// <summary>The row as it will be sent. Editing below writes into this.</summary>
        public TripImportItemDto Item => _item;

        public string? ErrorCode { get; }

        /// <summary>What the server said, verbatim. Shown under the plain-language title.</summary>
        public string? ServerMessage { get; }

        /// <summary>Handed to support; it is the key to the full record of the failure.</summary>
        public string? CorrelationId { get; }

        /// <summary>The trip this one clashed with, when that is why it was refused.</summary>
        public TripImportConflictDto? Conflict { get; }

        public ImportProblem Problem { get; }

        [ObservableProperty] private ImportRowState _state = ImportRowState.Failed;

        /// <summary>Filled in when a retry of this row is refused again, so the row can say why.</summary>
        [ObservableProperty] private string? _lastRetryMessage;

        // ------------------------------------------------------------------ the editable face

        public string TripId
        {
            get => _item.TripId;
            set => Edit(() => _item.TripId = value?.Trim() ?? string.Empty);
        }

        public DateTime Date
        {
            get => _item.Date;
            set => Edit(() => _item.Date = value.Date);
        }

        public TimeSpan? FromTime
        {
            get => _item.FromTime;
            set => Edit(() => _item.FromTime = value);
        }

        public TimeSpan? ToTime
        {
            get => _item.ToTime;
            set => Edit(() => _item.ToTime = value);
        }

        public string PatientName
        {
            get => _item.CustomerFullName;
            set => Edit(() => _item.CustomerFullName = value?.Trim() ?? string.Empty);
        }

        public string? PatientPhone
        {
            get => _item.CustomerPhone;
            set => Edit(() => _item.CustomerPhone = value?.Trim());
        }

        public string? RiderId
        {
            get => _item.RiderId;
            set => Edit(() => _item.RiderId = value?.Trim());
        }

        public string PickupAddress
        {
            get => _item.PickupAddress;
            set => Edit(() => _item.PickupAddress = value?.Trim() ?? string.Empty);
        }

        public string DropoffAddress
        {
            get => _item.DropoffAddress;
            set => Edit(() => _item.DropoffAddress = value?.Trim() ?? string.Empty);
        }

        public string SpaceTypeName
        {
            get => _item.SpaceTypeName;
            set => Edit(() => _item.SpaceTypeName = value?.Trim() ?? string.Empty);
        }

        // ------------------------------------------------------------------ what the screen asks

        /// <summary>True when both ends of the journey have a position on the map.</summary>
        public bool HasCoordinates =>
            _item.PickupLatitude != 0 && _item.PickupLongitude != 0
            && _item.DropoffLatitude != 0 && _item.DropoffLongitude != 0;

        /// <summary>
        /// Whether the correction actually answers the objection.
        /// </summary>
        /// <remarks>
        /// The gate lives in <see cref="ImportProblemCatalog"/> and not here on purpose: it is a
        /// rule about a code, and codes are shared with the file that will one day validate rows
        /// before they are ever sent. This is only the row handing over its own state.
        /// </remarks>
        public bool CanRetry =>
            State != ImportRowState.Imported
            && (Problem.RetryAsIs || Problem.FixableHere)
            && ImportProblemCatalog.IsAnswered(ErrorCode, Snapshot());

        public bool IsImported => State == ImportRowState.Imported;

        /// <summary>
        /// True when this application refused the row itself, before a request was spent on it.
        /// </summary>
        /// <remarks>
        /// Worth saying on screen. "No TripId" caught here and "No TripId" answered by the server
        /// read identically otherwise, and the difference matters: one cost nothing and was known
        /// before the import ran, the other cost a round trip.
        /// </remarks>
        public bool CaughtBeforeSending =>
            ErrorCode != null && ErrorCode.StartsWith("LOCAL_", StringComparison.Ordinal);

        // ---- which fields the editor should paint red ------------------------------------
        //
        // The editor shows the whole row, always. Hiding the fields that are not the problem
        // left a dispatcher looking at one empty box with no idea what trip it belonged to.
        // Everything is on screen, filled in, and only the field in question is marked.

        public bool TripIdIsProblem => InQuestion(ImportField.TripId);
        public bool DateIsProblem => InQuestion(ImportField.Date);
        public bool WindowIsProblem => InQuestion(ImportField.Window);
        public bool PatientIsProblem => InQuestion(ImportField.Patient);
        public bool PhoneIsProblem => InQuestion(ImportField.Phone);
        public bool RiderIdIsProblem => InQuestion(ImportField.RiderId);
        public bool PickupIsProblem => InQuestion(ImportField.PickupAddress);
        public bool DropoffIsProblem => InQuestion(ImportField.DropoffAddress);
        public bool SpaceTypeIsProblem => InQuestion(ImportField.SpaceType);

        private bool InQuestion(ImportField field) =>
            State != ImportRowState.Imported && Problem.Fields.HasFlag(field);

        /// <summary>The window, as one line, because it is read as one thing.</summary>
        public string WindowText =>
            $"{Describe(FromTime)} - {Describe(ToTime)}";

        private static string Describe(TimeSpan? time) =>
            time.HasValue ? DateTime.Today.Add(time.Value).ToString("HH:mm") : "--:--";

        public ImportRowCheck Snapshot() => new ImportRowCheck
        {
            TripId = _item.TripId,
            Date = _item.Date,
            PatientName = _item.CustomerFullName,
            PatientPhone = _item.CustomerPhone,
            RiderId = _item.RiderId,
            PickupAddress = _item.PickupAddress,
            DropoffAddress = _item.DropoffAddress,
            HasCoordinates = HasCoordinates,
            RiderIdWasChanged = !string.Equals(_item.RiderId, _riderIdAsRejected, StringComparison.Ordinal),
            MatchesConflictTripId =
                Conflict != null
                && !string.IsNullOrWhiteSpace(Conflict.TripId)
                && string.Equals(_item.TripId, Conflict.TripId, StringComparison.OrdinalIgnoreCase),
            DiffersFromConflictJourney =
                Conflict != null
                && (_item.Date.Date != Conflict.Date.Date
                    || _item.FromTime != Conflict.FromTime
                    || _item.ToTime != Conflict.ToTime
                    || !string.Equals(_item.PickupAddress, Conflict.PickupAddress, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(_item.DropoffAddress, Conflict.DropoffAddress, StringComparison.OrdinalIgnoreCase))
        };

        /// <summary>Marks the row as gone in, which is also what takes it out of the pending filter.</summary>
        public void MarkImported()
        {
            LastRetryMessage = null;
            State = ImportRowState.Imported;
            RaiseEverything();
        }

        public void MarkRefusedAgain(string? message)
        {
            LastRetryMessage = message;
            State = ImportRowState.Failed;
            RaiseEverything();
        }

        /// <summary>
        /// Applies an edit and re-asks every question the screen hangs off it.
        /// </summary>
        /// <remarks>
        /// One notification for the property and one for the whole verdict, because a single
        /// keystroke in the phone box can be what turns the retry button on — and a button that
        /// lights up one field later feels broken.
        /// </remarks>
        private void Edit(Action apply, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
        {
            apply();

            OnPropertyChanged(property);
            RaiseEverything();
        }

        private void RaiseEverything()
        {
            OnPropertyChanged(nameof(CanRetry));
            OnPropertyChanged(nameof(HasCoordinates));
            OnPropertyChanged(nameof(IsImported));
            OnPropertyChanged(nameof(WindowText));

            foreach (var flag in new[]
                     {
                         nameof(TripIdIsProblem), nameof(DateIsProblem), nameof(WindowIsProblem),
                         nameof(PatientIsProblem), nameof(PhoneIsProblem), nameof(RiderIdIsProblem),
                         nameof(PickupIsProblem), nameof(DropoffIsProblem), nameof(SpaceTypeIsProblem)
                     })
            {
                OnPropertyChanged(flag);
            }
        }

        partial void OnStateChanged(ImportRowState value) => RaiseEverything();
    }
}
