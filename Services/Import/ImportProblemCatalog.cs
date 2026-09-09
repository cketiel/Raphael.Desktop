using System;
using Raphael.Desktop.Models.Import;

namespace Raphael.Desktop.Services.Import
{
    /// <summary>
    /// The codes this application decides for itself, before anything is sent.
    /// </summary>
    /// <remarks>
    /// Four of the server's twelve can be seen in the file: a row with no TripId, a row with no
    /// way of telling the patient apart, a row missing something it cannot be stored without, and
    /// an address longer than the column. Catching those here costs no request at all and — the
    /// part that matters — puts the problem in front of the dispatcher while the file is still in
    /// their hand, instead of after a round trip.
    ///
    /// <para>
    /// Prefixed <c>LOCAL_</c> so nobody reading a screenshot has to wonder whether the server said
    /// it. These are ours and they are not part of the integrator contract.
    /// </para>
    /// </remarks>
    public static class ImportLocalCode
    {
        public const string NoTripId = "LOCAL_NO_TRIP_ID";
        public const string NoPatientIdentity = "LOCAL_NO_PATIENT_IDENTITY";
        public const string NoDate = "LOCAL_NO_DATE";
        public const string NoAddress = "LOCAL_NO_ADDRESS";
        public const string AddressTooLong = "LOCAL_ADDRESS_TOO_LONG";
        public const string NoCoordinates = "LOCAL_NO_COORDINATES";
        public const string MappingFailed = "LOCAL_MAPPING_FAILED";

        /// <summary>Sent, but the answer never arrived. Not the same thing as rejected.</summary>
        public const string NotConfirmed = "LOCAL_NOT_CONFIRMED";
    }

    /// <summary>The server's twelve, echoed so they can be compared without magic strings.</summary>
    /// <remarks>
    /// Mirror of <c>Raphael.Api/Services/Integration/IntegrationErrorCode.cs</c>. ⚠️ These are
    /// contract: `INTEGRATION_API_SPEC.md` §6.1. Renaming one here breaks nothing at compile time
    /// and everything at run time.
    /// </remarks>
    public static class ImportServerCode
    {
        public const string DuplicateActiveTrip = "DUPLICATE_ACTIVE_TRIP";
        public const string TripCancelled = "TRIP_CANCELLED";
        public const string PatientNotIdentifiable = "PATIENT_NOT_IDENTIFIABLE";
        public const string InvalidTripId = "INVALID_TRIP_ID";
        public const string DuplicateRiderId = "DUPLICATE_RIDER_ID";
        public const string DuplicateRecord = "DUPLICATE_RECORD";
        public const string InvalidReference = "INVALID_REFERENCE";
        public const string MissingRequiredField = "MISSING_REQUIRED_FIELD";
        public const string FieldTooLong = "FIELD_TOO_LONG";
        public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
        public const string Timeout = "TIMEOUT";
        public const string InternalError = "INTERNAL_ERROR";
    }

    /// <summary>
    /// Turns a rejection code into something a dispatcher can act on.
    /// </summary>
    /// <remarks>
    /// This is the heart of the repair step. Everything the screen does with a failed row — the
    /// sentence it shows, the advice under it, whether the retry button lights up, which fields
    /// the editor opens — comes from here, so there is one place to argue with when the advice is
    /// wrong, and adding a code is adding a row.
    /// </remarks>
    public static class ImportProblemCatalog
    {
        /// <summary>The longest an address column will take. `INTEGRATION_API_SPEC.md` §6.1.</summary>
        public const int MaxAddressLength = 450;

        private static string Text(string key) => LocalizationService.Instance[key];

        /// <summary>What this code means and what to do about it.</summary>
        /// <param name="code">A code from either <see cref="ImportServerCode"/> or <see cref="ImportLocalCode"/>.</param>
        public static ImportProblem Describe(string? code)
        {
            var key = string.IsNullOrWhiteSpace(code) ? ImportServerCode.InternalError : code!;

            var problem = new ImportProblem
            {
                Code = key,
                Title = Localized(key, "title"),
                Suggestion = Localized(key, "fix")
            };

            switch (key)
            {
                // ---- fixable here, and obvious what to change -------------------------------
                case ImportLocalCode.NoTripId:
                case ImportServerCode.InvalidTripId:
                    problem.FixableHere = true;
                    problem.Fields = ImportField.TripId;
                    break;

                case ImportLocalCode.NoPatientIdentity:
                case ImportServerCode.PatientNotIdentifiable:
                    problem.FixableHere = true;
                    problem.Fields = ImportField.Patient | ImportField.RiderId | ImportField.Phone;
                    break;

                case ImportLocalCode.NoDate:
                    problem.FixableHere = true;
                    problem.Fields = ImportField.Date | ImportField.Window;
                    break;

                case ImportLocalCode.NoAddress:
                case ImportLocalCode.NoCoordinates:
                    problem.FixableHere = true;
                    problem.Fields = ImportField.PickupAddress | ImportField.DropoffAddress;
                    break;

                case ImportLocalCode.AddressTooLong:
                case ImportServerCode.FieldTooLong:
                    problem.FixableHere = true;
                    problem.Fields = ImportField.PickupAddress | ImportField.DropoffAddress;
                    break;

                case ImportServerCode.MissingRequiredField:
                    problem.FixableHere = true;
                    problem.Fields = ImportField.TripId | ImportField.Date | ImportField.Patient
                                   | ImportField.PickupAddress | ImportField.DropoffAddress
                                   | ImportField.SpaceType;
                    break;

                case ImportServerCode.DuplicateRiderId:
                    problem.FixableHere = true;
                    problem.Fields = ImportField.RiderId;
                    break;

                // ---- fixable here, but only by making the journey a different one -----------
                case ImportServerCode.DuplicateActiveTrip:
                    problem.FixableHere = true;
                    problem.Fields = ImportField.TripId | ImportField.Date | ImportField.Window
                                   | ImportField.PickupAddress | ImportField.DropoffAddress;
                    break;

                // ---- nothing was wrong with the row; the moment was wrong -------------------
                case ImportServerCode.ConcurrencyConflict:
                case ImportServerCode.Timeout:
                case ImportLocalCode.NotConfirmed:
                    problem.RetryAsIs = true;
                    break;

                // ---- not answerable from this screen ----------------------------------------
                // TRIP_CANCELLED, INVALID_REFERENCE, DUPLICATE_RECORD, INTERNAL_ERROR and a row
                // that could not be mapped at all. Each has an instruction of its own in the
                // language file, and none of them is "press the button again".
            }

            return problem;
        }

        /// <summary>
        /// Whether a corrected row may be sent again.
        /// </summary>
        /// <remarks>
        /// The gate the office asked for: the retry button does not light up until the correction
        /// actually answers the objection. A dispatcher who edits a comment on a row rejected for
        /// having no patient has not fixed it, and letting them send it again would buy a second
        /// identical rejection and a little less trust in the screen.
        ///
        /// <para>
        /// It is checked against the row as it stands, not against what was edited: what matters
        /// is whether the row is now acceptable, not whether somebody typed.
        /// </para>
        /// </remarks>
        public static bool IsAnswered(string? code, ImportRowCheck row)
        {
            switch (code)
            {
                case ImportLocalCode.NoTripId:
                case ImportServerCode.InvalidTripId:
                    return !string.IsNullOrWhiteSpace(row.TripId);

                case ImportLocalCode.NoPatientIdentity:
                case ImportServerCode.PatientNotIdentifiable:
                    // The server's rule, and the reason for it: with no rider id the patient is
                    // matched on name plus phone, and a name on its own would merge two people
                    // who happen to share one.
                    return !string.IsNullOrWhiteSpace(row.RiderId)
                        || !string.IsNullOrWhiteSpace(row.PatientPhone);

                case ImportLocalCode.NoDate:
                    return row.Date != default;

                case ImportLocalCode.NoAddress:
                    return !string.IsNullOrWhiteSpace(row.PickupAddress)
                        && !string.IsNullOrWhiteSpace(row.DropoffAddress);

                case ImportLocalCode.NoCoordinates:
                    return row.HasCoordinates;

                case ImportLocalCode.AddressTooLong:
                case ImportServerCode.FieldTooLong:
                    return (row.PickupAddress?.Length ?? 0) <= MaxAddressLength
                        && (row.DropoffAddress?.Length ?? 0) <= MaxAddressLength;

                case ImportServerCode.MissingRequiredField:
                    return !string.IsNullOrWhiteSpace(row.TripId)
                        && row.Date != default
                        && !string.IsNullOrWhiteSpace(row.PatientName)
                        && !string.IsNullOrWhiteSpace(row.PickupAddress)
                        && !string.IsNullOrWhiteSpace(row.DropoffAddress);

                case ImportServerCode.DuplicateRiderId:
                    return !string.IsNullOrWhiteSpace(row.RiderId) && row.RiderIdWasChanged;

                case ImportServerCode.DuplicateActiveTrip:
                    // Two honest ways out, and no third. Either this row IS the existing trip and
                    // should update it — which is what putting its TripId on the row means — or it
                    // is a different journey, and then something about the journey has to differ:
                    // the day, the window, or one of the two addresses. Anything else sends the
                    // same clash back at the same index.
                    return row.MatchesConflictTripId || row.DiffersFromConflictJourney;

                case ImportServerCode.ConcurrencyConflict:
                case ImportServerCode.Timeout:
                case ImportLocalCode.NotConfirmed:
                    return true;

                default:
                    return false;
            }
        }

        private static string Localized(string code, string part)
        {
            var key = $"import.problem.{code}.{part}";

            return LocalizationService.Instance.TryGetValue(key, out var value)
                ? value
                : Text($"import.problem.{ImportServerCode.InternalError}.{part}");
        }
    }

    /// <summary>
    /// The state of one row, as the gate above needs to see it.
    /// </summary>
    /// <remarks>
    /// A plain snapshot rather than the row object itself, so the rule stays a pure function and
    /// can be reasoned about — and tested, when there is somewhere to test it — without a view
    /// model, a grid or a server anywhere near it.
    /// </remarks>
    public class ImportRowCheck
    {
        public string? TripId { get; set; }
        public DateTime Date { get; set; }
        public string? PatientName { get; set; }
        public string? PatientPhone { get; set; }
        public string? RiderId { get; set; }
        public string? PickupAddress { get; set; }
        public string? DropoffAddress { get; set; }
        public bool HasCoordinates { get; set; }

        /// <summary>True when the rider id is not the one the row was rejected with.</summary>
        public bool RiderIdWasChanged { get; set; }

        /// <summary>True when the row now carries the identifier of the trip it clashed with.</summary>
        public bool MatchesConflictTripId { get; set; }

        /// <summary>True when the day, the window or an address is no longer the clashing one.</summary>
        public bool DiffersFromConflictJourney { get; set; }
    }
}
