using System;

namespace Raphael.Desktop.Models.Import
{
    /// <summary>Where the import screen is.</summary>
    /// <remarks>
    /// Three states, not four. Choosing the file and choosing the funding source are the same
    /// step — neither is any use without the other — and the summary and the repair grid are the
    /// same step too, because a run with no failures still has to say what it did.
    /// </remarks>
    public enum ImportStep
    {
        /// <summary>Pick a funding source and a file, and see what is in it before sending it.</summary>
        Prepare = 1,

        /// <summary>Geocoding, mapping and sending. The only step that cannot be left.</summary>
        Running = 2,

        /// <summary>What happened, and the rows that need a hand.</summary>
        Review = 3
    }

    /// <summary>Where one problem row has got to.</summary>
    public enum ImportRowState
    {
        /// <summary>Rejected, and not corrected yet.</summary>
        Failed = 0,

        /// <summary>Edited so the reason it was rejected no longer applies. Not sent again yet.</summary>
        Fixed = 1,

        /// <summary>Sent again and accepted. It is in the system.</summary>
        Imported = 2
    }

    /// <summary>How loud one line of the running log is.</summary>
    public enum ImportSeverity
    {
        Info = 0,
        Success = 1,
        Warning = 2,
        Error = 3
    }

    /// <summary>
    /// One line of what the import is doing, as it does it.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is a screen, not a log. It carries patient names and addresses on purpose — it is
    /// the panel a dispatcher opens when something went wrong and they need to see what — and for
    /// exactly that reason <b>nothing here may be written to FileLogger</b>. PHI stays on the
    /// screen of the person already entitled to see it. `../CLAUDE.md` §3.
    /// </remarks>
    public class ImportLogEntry
    {
        public DateTime At { get; set; }

        public ImportSeverity Severity { get; set; }

        /// <summary>Which pass of the import this belongs to: reading, geocoding, sending…</summary>
        public string Stage { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public string Time => At.ToString("HH:mm:ss");
    }

    /// <summary>
    /// The fields a given problem is fixed by editing.
    /// </summary>
    /// <remarks>
    /// Drives the editor: a row rejected for having no patient identity opens on the patient
    /// fields, not on all twenty. Showing a dispatcher every field of a row and letting them work
    /// out which one the server meant is how a correction screen becomes a puzzle.
    /// </remarks>
    [Flags]
    public enum ImportField
    {
        None = 0,
        TripId = 1,
        Date = 2,
        Window = 4,
        Patient = 8,
        RiderId = 16,
        Phone = 32,
        PickupAddress = 64,
        DropoffAddress = 128,
        SpaceType = 256
    }

    /// <summary>
    /// What one rejection means, said in the office's language, and what to do about it.
    /// </summary>
    /// <remarks>
    /// The server answers with a stable code — the twelve of `INTEGRATION_API_SPEC.md` §6.1 — and
    /// a sentence written for an integrator's developer. This turns that into something a
    /// dispatcher can act on: what went wrong, what to do, whether it can be done here, and which
    /// fields to open.
    /// </remarks>
    public class ImportProblem
    {
        public string Code { get; set; } = string.Empty;

        /// <summary>The problem in one line, in the office's words.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>What to do about it. Always an instruction, never a restatement.</summary>
        public string Suggestion { get; set; } = string.Empty;

        /// <summary>True when this can be corrected on this screen and sent again.</summary>
        public bool FixableHere { get; set; }

        /// <summary>Which fields the editor opens on.</summary>
        public ImportField Fields { get; set; }

        /// <summary>
        /// True when the row can be sent again untouched — the failure was the moment, not the row.
        /// </summary>
        public bool RetryAsIs { get; set; }
    }
}
