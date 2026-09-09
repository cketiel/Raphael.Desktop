using System;
using Raphael.Desktop.DTOs;

namespace Raphael.Desktop.Services.Import
{
    /// <summary>
    /// The rules this application can check without asking the server.
    /// </summary>
    /// <remarks>
    /// One copy, called from two places, and that is the point. The engine calls it to hold rows
    /// back before a request is spent on them; the screen calls it the moment a file is opened, so
    /// the dispatcher is told what is wrong with the file <b>before</b> pressing Import rather than
    /// after. Two copies of these rules would disagree within a month, and the disagreement would
    /// look like the screen lying.
    ///
    /// <para>
    /// Every rule mirrors one the server states in `INTEGRATION_API_SPEC.md` §6.1 and answers it
    /// with the same code prefixed <c>LOCAL_</c>, so a screenshot says who decided.
    /// </para>
    ///
    /// <para>
    /// ⚠️ Wrong in one direction only. A rule that is too strict withholds a row the server would
    /// have taken, and the dispatcher has no way of overruling it. When a rule is not certain, it
    /// is not a blocker.
    /// </para>
    /// </remarks>
    public static class ImportPreflight
    {
        /// <summary>The first rule this row breaks, or null when it breaks none of them.</summary>
        public static string FirstProblemWith(TripImportItemDto item)
        {
            if (item == null) return ImportLocalCode.MappingFailed;

            if (string.IsNullOrWhiteSpace(item.TripId)) return ImportLocalCode.NoTripId;

            if (item.Date == default) return ImportLocalCode.NoDate;

            // The server's rule, exactly: a rider id identifies the patient on its own; without
            // one the match falls back to name plus phone, and a name on its own would merge two
            // people who happen to share it.
            //
            // WARNING: exactly, and not more. This asked for a name AND a phone, which is stricter
            // than the server - a row with a phone and no name would have been withheld here and
            // taken there. Too strict is the bad direction: the dispatcher cannot overrule it.
            if (string.IsNullOrWhiteSpace(item.RiderId)
                && string.IsNullOrWhiteSpace(item.CustomerPhone))
            {
                return ImportLocalCode.NoPatientIdentity;
            }

            if (string.IsNullOrWhiteSpace(item.PickupAddress)
                || string.IsNullOrWhiteSpace(item.DropoffAddress))
            {
                return ImportLocalCode.NoAddress;
            }

            if (item.PickupAddress.Length > ImportProblemCatalog.MaxAddressLength
                || item.DropoffAddress.Length > ImportProblemCatalog.MaxAddressLength)
            {
                return ImportLocalCode.AddressTooLong;
            }

            return null;
        }

        /// <summary>True when both ends of the journey have a position on the map.</summary>
        /// <remarks>
        /// Never a blocker. A trip with no position stores fine and the day can be dispatched;
        /// Home has a filter for exactly these, and refusing them would take away a workflow the
        /// office already relies on.
        /// </remarks>
        public static bool HasCoordinates(TripImportItemDto item) =>
            item != null
            && item.PickupLatitude != 0 && item.PickupLongitude != 0
            && item.DropoffLatitude != 0 && item.DropoffLongitude != 0;
    }
}
