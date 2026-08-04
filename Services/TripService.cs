using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using MaterialDesignColors;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public class TripService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "trips";
        private readonly TripHistoryService _historyService;
        public TripService()
        {
            _httpClient = ApiClientFactory.Create();
            _historyService = new TripHistoryService();
        }

        public async Task<TripReadDto> GetTripByIdAsync(int id)
        {
            try
            {              
                var response = await _httpClient.GetAsync($"{EndPoint}/{id}"); 

                if (response.IsSuccessStatusCode)
                {                  
                    return await response.Content.ReadFromJsonAsync<TripReadDto>();
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }
                else
                {                  
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Error al obtener el viaje: {error}");
                }
            }
            catch (HttpRequestException ex)
            {
                // Error de conexión, timeout, etc.
                throw new Exception("Error de conexión con el servidor", ex);
            }
            
        }
        public async Task<bool> UpdateTripTypeAsync(List<TripTypeUpdateDto> updatePayload)
        {
            var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/update-types", updatePayload);
            
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, $"Error updating TripTypes...");
            }
            return true;
        }
        public async Task<List<TripReadDto>> GetAllTripsAsync()
        {
            var result = await _httpClient.GetFromJsonAsync<List<TripReadDto>>(EndPoint);
            return result ?? new List<TripReadDto>();
        }
        /*public async Task<List<TripReadDto>> GetAllTripsAsync2()
        {  
            try
            {
                var response = await _httpClient.GetAsync(EndPoint);

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, "Error getting trips");
                }

                return await response.Content.ReadFromJsonAsync<List<TripReadDto>>();
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }
        }
        public async Task<ObservableCollection<Trip>> GetTripsAsync()
        {
            var trips = await _httpClient.GetFromJsonAsync<ObservableCollection<Trip>>("api/trips");          
            return trips ?? new ObservableCollection<Trip>();
        }

        public async Task<ObservableCollection<Trip>> GetAllTrips()
        {
            ObservableCollection<Trip> Trips = new ObservableCollection<Trip>();
            Trip SelectedTrip;
            var response = await _httpClient.GetAsync("api/trips");
            if (response.IsSuccessStatusCode)
            {
                var tripList = await response.Content.ReadFromJsonAsync<List<Trip>>();
                if (tripList != null && tripList.Any())
                {
                    Trips = new ObservableCollection<Trip>(tripList);
                    SelectedTrip = Trips.First(); // 
                }
                else
                {
                    // Show message or log: no data arrived
                }
            }
            return Trips;
        }
        public async Task<List<Trip>> GetTripList()
        {
            var tripList = new List<Trip>();
            try
            {
                var response = await _httpClient.GetAsync("api/trips");
                if (response.IsSuccessStatusCode)
                {
                    tripList = await response.Content.ReadFromJsonAsync<List<Trip>>();

                }
            }
            catch (Exception ex) { 
                // throw exception 
            }

            return tripList;
        }*/

        public async Task<TripReadDto> CreateTripAsync(Trip trip)
        {          
            try
            {

                var response = await _httpClient.PostAsJsonAsync(EndPoint, trip);

                if (!response.IsSuccessStatusCode)
                {                  
                    throw await CreateApiException(response, "Error creating trip");
                }

                var createdTrip = await response.Content.ReadFromJsonAsync<TripReadDto>();

                // --- HISTORY RECORD ---
                if (createdTrip != null)
                    await _historyService.SaveHistoryAsync(createdTrip.Id, "Add_New_Trip", null, "Trip Created (Manual/Import)");

                return createdTrip;

                //return await response.Content.ReadFromJsonAsync<TripReadDto>();
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }
            
        }

        public async Task<bool> UpdateTripAsync(TripReadDto tripDto)
        {
            var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/{tripDto.Id}", tripDto);
            //var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/update", tripDto);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, $"Error updating trip {tripDto.Id}");
            }
            await _historyService.SaveHistoryAsync(tripDto.Id, "Update_Trip", null, "Trip Updated (Manual/Import)"); // salvar en la tabla history
            return true;
        }

        public async Task<List<TripReadDto>> GetTripsByDateAsync(DateTime date)
        {
            try
            {
                // You must ensure that the format is consistent regardless of the system locale.
                // Format date in ISO 8601 format (yyyy-MM-dd) using culture invariant
                string isoDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                var response = await _httpClient.GetAsync($"{EndPoint}/date/{isoDate}");

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, "Error getting trips");
                }

                return await response.Content.ReadFromJsonAsync<List<TripReadDto>>();
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }

            /*var result = await _httpClient.GetFromJsonAsync<List<TripReadDto>>($"{EndPoint}/date/{date}");
            return result ?? new List<TripReadDto>();*/
        }

        public async Task CancelTripAsync(int tripId)
        {
            var response = await _httpClient.PostAsync($"{EndPoint}/{tripId}/cancel", null);
            response.EnsureSuccessStatusCode();

            // --- HISTORY RECORD ---
            await _historyService.SaveHistoryAsync(tripId, "IsCanceled", "False", "True");
            //await _historyService.SaveHistoryAsync(tripId, "Status", "Active", "Cancelled");
        }

        public async Task UncancelTripAsync(int tripId)
        {
            var response = await _httpClient.PostAsync($"{EndPoint}/{tripId}/uncancel", null);
          
            if (!response.IsSuccessStatusCode)
            {               
                throw await CreateApiException(response, "Error restoring trip");
            }

            // --- HISTORY RECORD ---
            await _historyService.SaveHistoryAsync(tripId, "IsCanceled", "True", "False");
        }

        // This throws a generic exception that makes it difficult to tell if the error was a 409 (Conflict) or a 500 (Server Error)
        public async Task UncancelTripAsyncOld(int tripId)
        {           
            var response = await _httpClient.PostAsync($"{EndPoint}/{tripId}/uncancel", null);           
            response.EnsureSuccessStatusCode();

            // --- HISTORY RECORD ---
            await _historyService.SaveHistoryAsync(tripId, "IsCanceled", "True", "False");
            //await _historyService.SaveHistoryAsync(tripId, "Status", "Cancelled", "Active");
        }

        public async Task UpdateFromDispatchAsync(int tripId, TripDispatchUpdateDto dto)
        {
            var response = await _httpClient.PatchAsJsonAsync($"{EndPoint}/{tripId}/dispatch-update", dto);
            response.EnsureSuccessStatusCode();

            // --- HISTORY RECORD ---
            await _historyService.SaveHistoryAsync(tripId, "Update_Trip", "Some Fields", "Manual Update");
        }

        public async Task AssignRunAsync(int tripId, int? vehicleRouteId)
        {           
            var response = await _httpClient.PatchAsJsonAsync($"{EndPoint}/{tripId}/assign-run", vehicleRouteId);

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, "Error assigning run to trip");
            }
        }

        /*public async Task AssignRunAsync(int tripId, int? vehicleRouteId)
        {
            
            var response = await _httpClient.PatchAsJsonAsync($"{EndPoint}/{tripId}/assign-run", vehicleRouteId);
            response.EnsureSuccessStatusCode();
        }*/

        private async Task<ApiException> CreateApiException(HttpResponseMessage response, string context)
        {
            try
            {
                var problemDetails = await response.Content.ReadFromJsonAsync<Exceptions.ProblemDetails>();
                return new ApiException(
                    message: $"{context}: {problemDetails?.Title}",
                    statusCode: response.StatusCode,
                    details: problemDetails?.Detail);
            }
            catch
            {
                var content = await response.Content.ReadAsStringAsync();
                return new ApiException(
                    message: $"{context}: Unspecified error",
                    statusCode: response.StatusCode,
                    details: content);
            }
        }
    }
}
