namespace Raphael.Desktop.Models
{
    /// <summary>
    /// What the patient panel is holding, said out loud.
    /// </summary>
    /// <remarks>
    /// The panel looks the same whether it is showing a patient on file, one being typed from
    /// scratch, or one on file with unsaved edits over the top. A dispatcher who cannot tell those
    /// apart books a trip against a record the server has never seen.
    /// </remarks>
    public enum CustomerFormState
    {
        /// <summary>Nothing typed and nobody chosen.</summary>
        Empty,

        /// <summary>Being typed from scratch. Nothing on the server yet.</summary>
        NewUnsaved,

        /// <summary>Exactly as the server has it.</summary>
        Existing,

        /// <summary>On file, but with edits that have not been saved.</summary>
        ModifiedUnsaved
    }
}
