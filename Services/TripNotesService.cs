using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.DTOs.Places;
using Raphael.Desktop.Exceptions;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// The notes of a trip's places and patient, the guide the driver reads at each stop (RE-054).
    /// </summary>
    /// <remarks>
    /// Every call answers with the whole notes window again, so the screen never works out on its own
    /// what the driver will read. Note texts are PHI: never logged.
    /// </remarks>
    public interface ITripNotesService
    {
        Task<TripNotesDto> GetAsync(int tripId);
        Task<TripNotesDto> SetTripNoteAsync(int tripId, string side, string? text);
        Task<TripNotesDto> PromoteTripNoteAsync(int tripId, string side, string target, string? text);
        Task<TripNotesDto> AddPlaceNoteAsync(int tripId, string side, NoteUpsertDto note);
        Task<TripNotesDto> UpdatePlaceNoteAsync(int tripId, int noteId, NoteUpsertDto note);
        Task<TripNotesDto> RemovePlaceNoteAsync(int tripId, int noteId);
        Task<TripNotesDto> AddCustomerNoteAsync(int tripId, NoteUpsertDto note);
        Task<TripNotesDto> UpdateCustomerNoteAsync(int tripId, int noteId, NoteUpsertDto note);
        Task<TripNotesDto> RemoveCustomerNoteAsync(int tripId, int noteId);
        Task<TripNotesDto> LinkPlaceAsync(int tripId, string side, int placeId);
        Task<TripNotesDto> UpdatePlaceAsync(int tripId, string side, PlaceUpdateDto update);
        Task<List<PlaceSummaryDto>> SearchPlacesAsync(string? search);

        /// <summary>What a trip about to be created would carry. Writes nothing.</summary>
        Task<NotesPreviewDto> PreviewAsync(NotesPreviewRequestDto request);
    }

    public class TripNotesService : ITripNotesService
    {
        private readonly HttpClient _httpClient;

        public TripNotesService()
        {
            _httpClient = ApiClientFactory.Create();
        }

        private static string Notes(int tripId) => $"trips/{tripId}/notes";

        public Task<TripNotesDto> GetAsync(int tripId) =>
            ReadAsync<TripNotesDto>(() => _httpClient.GetAsync(Notes(tripId)), "Error reading the trip notes");

        public Task<TripNotesDto> SetTripNoteAsync(int tripId, string side, string? text) =>
            ReadAsync<TripNotesDto>(() => _httpClient.PutAsJsonAsync($"{Notes(tripId)}/{side}/trip-note", new TripNoteUpdateDto { Text = text }),
                "Error saving the trip note");

        public Task<TripNotesDto> PromoteTripNoteAsync(int tripId, string side, string target, string? text) =>
            ReadAsync<TripNotesDto>(() => _httpClient.PostAsJsonAsync($"{Notes(tripId)}/{side}/trip-note/promote",
                new PromoteTripNoteDto { Target = target, Text = text }), "Error keeping the note");

        public Task<TripNotesDto> AddPlaceNoteAsync(int tripId, string side, NoteUpsertDto note) =>
            ReadAsync<TripNotesDto>(() => _httpClient.PostAsJsonAsync($"{Notes(tripId)}/{side}/place-notes", note), "Error adding the note");

        public Task<TripNotesDto> UpdatePlaceNoteAsync(int tripId, int noteId, NoteUpsertDto note) =>
            ReadAsync<TripNotesDto>(() => _httpClient.PutAsJsonAsync($"{Notes(tripId)}/place-notes/{noteId}", note), "Error saving the note");

        public Task<TripNotesDto> RemovePlaceNoteAsync(int tripId, int noteId) =>
            ReadAsync<TripNotesDto>(() => _httpClient.DeleteAsync($"{Notes(tripId)}/place-notes/{noteId}"), "Error removing the note");

        public Task<TripNotesDto> AddCustomerNoteAsync(int tripId, NoteUpsertDto note) =>
            ReadAsync<TripNotesDto>(() => _httpClient.PostAsJsonAsync($"{Notes(tripId)}/customer-notes", note), "Error adding the note");

        public Task<TripNotesDto> UpdateCustomerNoteAsync(int tripId, int noteId, NoteUpsertDto note) =>
            ReadAsync<TripNotesDto>(() => _httpClient.PutAsJsonAsync($"{Notes(tripId)}/customer-notes/{noteId}", note), "Error saving the note");

        public Task<TripNotesDto> RemoveCustomerNoteAsync(int tripId, int noteId) =>
            ReadAsync<TripNotesDto>(() => _httpClient.DeleteAsync($"{Notes(tripId)}/customer-notes/{noteId}"), "Error removing the note");

        public Task<TripNotesDto> LinkPlaceAsync(int tripId, string side, int placeId) =>
            ReadAsync<TripNotesDto>(() => _httpClient.PutAsJsonAsync($"{Notes(tripId)}/{side}/place", new PlaceLinkDto { PlaceId = placeId }),
                "Error changing the place");

        public Task<TripNotesDto> UpdatePlaceAsync(int tripId, string side, PlaceUpdateDto update) =>
            ReadAsync<TripNotesDto>(() => _httpClient.PatchAsJsonAsync($"{Notes(tripId)}/{side}/place", update), "Error saving the place");

        public async Task<List<PlaceSummaryDto>> SearchPlacesAsync(string? search) =>
            await ReadAsync<List<PlaceSummaryDto>>(
                () => _httpClient.GetAsync($"places?take=30&search={System.Uri.EscapeDataString(search ?? string.Empty)}"),
                "Error searching places") ?? new List<PlaceSummaryDto>();

        public Task<NotesPreviewDto> PreviewAsync(NotesPreviewRequestDto request) =>
            ReadAsync<NotesPreviewDto>(() => _httpClient.PostAsJsonAsync("places/notes-preview", request), "Error reading the notes");

        private static async Task<T> ReadAsync<T>(System.Func<Task<HttpResponseMessage>> send, string context)
        {
            HttpResponseMessage response;
            try
            {
                response = await send();
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Connection error with the server.", ex);
            }

            if (response.IsSuccessStatusCode)
                return (await response.Content.ReadFromJsonAsync<T>())!;

            // 400 carries { message }; 409 (past or closed trip) a ProblemDetails with a readable title.
            string? message = null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("message", out var m)) message = m.GetString();
                else if (doc.RootElement.TryGetProperty("title", out var t)) message = t.GetString();
            }
            catch
            {
                // Not JSON: the context is all that can be said.
            }
            throw new ApiException(message ?? context, response.StatusCode, null);
        }
    }
}
