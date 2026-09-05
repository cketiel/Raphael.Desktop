namespace Raphael.Desktop.Models
{
    /// <summary>
    /// What the Home tab is doing right now. This is the only thing that decides which panels
    /// are on screen and how wide its columns are.
    /// </summary>
    /// <remarks>
    /// Before RE-010 that state was spread over direct <c>Visibility</c> assignments in
    /// HomeView's code-behind, and a dispatcher who opened a trip had no way back: the method
    /// that undid them ran only after a successful save. Anything that needs to know whether
    /// the screen is booking, editing or importing reads this — it does not keep a flag of
    /// its own.
    /// </remarks>
    public enum HomeMode
    {
        /// <summary>Looking at the day's trips. The trip form is closed.</summary>
        Browsing,

        /// <summary>Booking a new trip for the patient on screen.</summary>
        CreatingTrip,

        /// <summary>Editing the trip selected in the grid.</summary>
        EditingTrip,

        /// <summary>The CSV import view has taken over the tab.</summary>
        Importing
    }
}
