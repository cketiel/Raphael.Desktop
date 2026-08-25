using Raphael.Desktop.DTOs;
using Raphael.Desktop.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    public class TripHistoryService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "TripHistory"; 

        public TripHistoryService()
        {
            _httpClient = ApiClientFactory.Create();
        }

        public async Task SaveHistoryAsync(int tripId, string field, string priorValue, string newValue)
        {
            // An instant, so it travels in UTC and every screen shows it in the hour of
            // whoever is looking. This used to send the dispatcher's own clock while the API
            // wrote the server's, which left one column meaning two different things
            // depending on which of them happened to write the row.
            DateTime changeDate = DateTime.UtcNow;
            try
            {
                var history = new TripHistoryCreateDto
                {
                    TripId = tripId,
                    ChangeDate = changeDate,
                    User = SessionManager.Username,
                    Field = field,
                    PriorValue = priorValue ?? "N/A",
                    NewValue = newValue ?? "N/A"
                };

                var response = await _httpClient.PostAsJsonAsync(EndPoint, history);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Error inserting history: {error}");                   
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in SaveHistory: {ex.Message}");
            }
        }

        public async Task<List<TripHistoryCreateDto>> GetHistoryByTripAsync(int tripId)
        {
            var result = await _httpClient.GetFromJsonAsync<List<TripHistoryCreateDto>>($"{EndPoint}/{tripId}");
            return result ?? new List<TripHistoryCreateDto>();
        }
    }
}
