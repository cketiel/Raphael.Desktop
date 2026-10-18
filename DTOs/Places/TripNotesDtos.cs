// RE-054. Copy of Raphael.Backend/Raphael.Shared/DTOs/Places/TripNotesDtos.cs: keep the two in step.
using System;
using System.Collections.Generic;

namespace Raphael.Desktop.DTOs.Places
{
    // RE-054. Everything is reached through a trip (api/trips/{tripId}/notes/...): whoever may see
    // the trip may see and edit its notes, and nothing else. Enums travel as text.

    /// <summary>One note line. <see cref="Text"/> is PHI on a home or a patient.</summary>
    public class NoteDto
    {
        public int Id { get; set; }
        public string Text { get; set; } = string.Empty;

        /// <summary>Import, Dispatcher, Driver or History.</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>Active or ToReview. Removed notes are not sent.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Set when the note is about one entrance of the place.</summary>
        public int? EntranceId { get; set; }

        public string? CreatedBy { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
    }

    /// <summary>A place as the notes window and the place search show it.</summary>
    public class PlaceSummaryDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>Home or Facility.</summary>
        public string Kind { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;
        public string? City { get; set; }

        /// <summary>The entrance this trip end stops at, when known.</summary>
        public int? EntranceId { get; set; }
        public string? EntranceLabel { get; set; }

        public int EntranceCount { get; set; }
    }

    /// <summary>One end of a trip: its place, its notes, and the text the driver reads.</summary>
    public class TripStopNotesDto
    {
        /// <summary>Pickup or Dropoff.</summary>
        public string Side { get; set; } = string.Empty;

        /// <summary>Null when the address could not be matched or created (an empty address).</summary>
        public PlaceSummaryDto? Place { get; set; }

        /// <summary>What the file, clinic or integrator sent, untouched.</summary>
        public string? FileComment { get; set; }

        /// <summary>What was written for this trip only.</summary>
        public string? TripNote { get; set; }

        public List<NoteDto> EntranceNotes { get; set; } = new();
        public List<NoteDto> PlaceNotes { get; set; } = new();

        /// <summary>The comment the driver reads at this stop (Trip.PickupComment / DropoffComment).</summary>
        public string? Composed { get; set; }
    }

    /// <summary>The notes window of one trip.</summary>
    public class TripNotesDto
    {
        public int TripId { get; set; }
        public int CustomerId { get; set; }
        public string? CustomerName { get; set; }

        /// <summary>
        /// The trip is past or closed: its own notes cannot change. The place's and the patient's still
        /// can, and reach the trips still to come.
        /// </summary>
        public bool TripReadOnly { get; set; }

        public TripStopNotesDto Pickup { get; set; } = new();
        public TripStopNotesDto Dropoff { get; set; } = new();
        public List<NoteDto> CustomerNotes { get; set; } = new();

        /// <summary>Notes of this trip's places and patient still waiting for the office to look at them.</summary>
        public int NotesToReview { get; set; }
    }

    /// <summary>Create or change a note of a place or of the patient.</summary>
    public class NoteUpsertDto
    {
        public string Text { get; set; } = string.Empty;

        /// <summary>Active (default) or ToReview. Confirming a note is sending it back as Active.</summary>
        public string? Status { get; set; }

        /// <summary>Place notes only: true to tie the note to the entrance this trip end stops at.</summary>
        public bool ForEntrance { get; set; }
    }

    /// <summary>The note of this trip only, for one end. Empty text clears it.</summary>
    public class TripNoteUpdateDto
    {
        public string? Text { get; set; }
    }

    /// <summary>Turn a line of this trip's note into a note of the place or of the patient.</summary>
    public class PromoteTripNoteDto
    {
        /// <summary>Place or Customer.</summary>
        public string Target { get; set; } = string.Empty;

        /// <summary>The line to keep. Empty takes the whole trip note.</summary>
        public string? Text { get; set; }
    }

    /// <summary>Point a trip end at another existing place.</summary>
    public class PlaceLinkDto
    {
        public int PlaceId { get; set; }
    }

    /// <summary>Rename the place of a trip end, or name the entrance it stops at.</summary>
    public class PlaceUpdateDto
    {
        public string? Name { get; set; }

        /// <summary>Home or Facility.</summary>
        public string? Kind { get; set; }

        public string? EntranceLabel { get; set; }
    }

    /// <summary>
    /// The trip form asks, before saving, what notes a new trip would carry. Reads only: no place is
    /// created and nothing is written.
    /// </summary>
    public class NotesPreviewRequestDto
    {
        public int CustomerId { get; set; }
        public string? PickupAddress { get; set; }
        public double PickupLatitude { get; set; }
        public double PickupLongitude { get; set; }
        public string? DropoffAddress { get; set; }
        public double DropoffLatitude { get; set; }
        public double DropoffLongitude { get; set; }
    }

    public class NotesPreviewDto
    {
        public string? PickupComment { get; set; }
        public string? DropoffComment { get; set; }
        public string? PickupPlaceName { get; set; }
        public string? DropoffPlaceName { get; set; }
    }
}
