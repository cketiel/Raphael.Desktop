using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    // Implement the interface to promote decoupling
    public class RunService : IRunService
    {
        private readonly HttpClient _httpClient;
        private readonly string _endPoint = "runs"; 

        public RunService()
        {           
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<VehicleRoute>> GetAllAsync()
        {
            try
            {
                var result = await _httpClient.GetFromJsonAsync<List<VehicleRoute>>(_endPoint);
                return result ?? new List<VehicleRoute>();
            }
            catch (HttpRequestException ex)
            {
                // This error occurs if the server is unavailable
                throw new ApiException("Connection error with the server.", ex);
            }
        }

        public async Task<VehicleRoute> GetByIdAsync(int id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_endPoint}/{id}");
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null; 
                }

                response.EnsureSuccessStatusCode(); // Throws exception for other error codes (500, 401, etc.)
                return await response.Content.ReadFromJsonAsync<VehicleRoute>();
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Connection error with the server.", ex);
            }
        }

        public async Task<VehicleRoute> CreateAsync(VehicleRouteDto dto)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(_endPoint, dto);

                if (!response.IsSuccessStatusCode)
                {                   
                    throw await CreateApiException(response, "Error creating route");
                }

                // The API returns the created entity with its new ID
                return await response.Content.ReadFromJsonAsync<VehicleRoute>();
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Connection error with the server.", ex);
            }
        }

        public async Task UpdateAsync(int id, VehicleRouteDto dto)
        {
            try
            {
                var response = await _httpClient.PutAsJsonAsync($"{_endPoint}/{id}", dto);

                // We do not expect content, just a success code (204 No Content)
                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, "Error updating route");
                }
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Connection error with the server.", ex);
            }
        }

        public async Task DeleteAsync(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{_endPoint}/{id}");

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, "Error deleting route");
                }
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Connection error with the server.", ex);
            }
        }

        public async Task<bool> CancelAsync(int id)
        {            
            var requestUrl = $"{_endPoint}/{id}/cancel";

            try
            {
                // Make the PATCH request.
                // We do not need to send a body for this operation, so the second argument is null.
                HttpResponseMessage response = await _httpClient.PatchAsync(requestUrl, null);

                // Check the server response.
                // The API returns 204 NoContent on success. IsSuccessStatusCode will detect it.
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                // If the response is 404 Not Found, the route did not exist.
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Console.WriteLine($"Error: Route with ID:  {id} not found");
                }
                else
                {
                    // For other error codes (e.g. 500 Internal Server Error).
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Error canceling the route. Status: {response.StatusCode}. Content: {errorContent}");
                }

                return false;
            }
            catch (HttpRequestException ex)
            {
                // Capture network errors (e.g. server is not available).
                Console.WriteLine($"Network error when trying to cancel the route: {ex.Message}");
                return false;
            }
        }

        // Helper to create custom and detailed exceptions from HTTP response
        private async Task<ApiException> CreateApiException(HttpResponseMessage response, string context)
        {
            try
            {
                var problemDetails = await response.Content.ReadFromJsonAsync<DTOs.ProblemDetails>();

                // Construct a clearer error message, using the issue title if available
                var errorMessage = $"{context}: {problemDetails?.Title ?? "Error not specified by the API."}";
               
                return new ApiException(
                    message: errorMessage,
                    statusCode: response.StatusCode,
                    details: problemDetails?.Detail);
            }
            catch // If the response body is not valid ProblemDetails JSON
            {
                var content = await response.Content.ReadAsStringAsync();
                return new ApiException(
                    message: $"{context}. Unexpected server response.",
                    statusCode: response.StatusCode,
                    details: content);
            }
        }
    }
}