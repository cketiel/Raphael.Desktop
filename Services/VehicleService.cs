using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public class VehicleService: IVehicleService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "vehicles";

        public VehicleService() 
        {
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<Vehicle>> GetVehiclesAsync()
        {
            //await Task.Delay(10000); // Simulates latency
            var result = await _httpClient.GetFromJsonAsync<List<Vehicle>>(EndPoint);
            return result ?? new List<Vehicle>();
        }

        public async Task<Vehicle> AddVehicleAsync(Vehicle vehicle)
        {
            var response = await _httpClient.PostAsJsonAsync(EndPoint, vehicle);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, "Error adding vehicle");
            }
            return await response.Content.ReadFromJsonAsync<Vehicle>();
        }

        public async Task<bool> UpdateVehicleAsync(int id, Vehicle vehicle)
        {
            var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/{id}", vehicle);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, $"Error updating vehicle {id}");
            }
            return true;
        }
        public async Task<bool> DeleteVehicleAsync(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, $"Error deleting vehicle {id}");
                }

                return true;
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }
            /*var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");
            return response.IsSuccessStatusCode;*/
        }

        private async Task<ApiException> CreateApiException(HttpResponseMessage response, string context)
        {
            try
            {
                var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
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
