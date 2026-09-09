namespace Raphael.Desktop.Models
{
    /// <summary>
    /// The order a trip gets booked in, as one value.
    /// </summary>
    /// <remarks>
    /// The three ways the screen guides someone — the numbered badges, the stepper above the form
    /// and the first-run tour — all read this and none of them works anything out for itself. That
    /// is deliberate: whichever of the three turns out to be the one people use, the other two can
    /// be switched off without touching a line of logic.
    /// </remarks>
    public enum TripCreationStep
    {
        /// <summary>Who is travelling.</summary>
        Patient = 1,

        /// <summary>From where, to where.</summary>
        Addresses = 2,

        /// <summary>When, and what kind of trip it is.</summary>
        Schedule = 3,

        /// <summary>Everything the server will insist on is present.</summary>
        Create = 4
    }
}
