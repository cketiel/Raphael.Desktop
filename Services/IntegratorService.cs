using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    public class IntegratorService : IIntegratorService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "integrators";

        public IntegratorService()
        {
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<Integrator>> GetIntegratorsAsync()
        {          
            var result = await _httpClient.GetFromJsonAsync<List<Integrator>>(EndPoint);
            return result ?? new List<Integrator>();
        }

        public async Task<Integrator> AddIntegratorAsync(Integrator integrator)
        {
            var response = await _httpClient.PostAsJsonAsync(EndPoint, integrator);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, "Error adding integrator");
            }
            return await response.Content.ReadFromJsonAsync<Integrator>();
        }

        public async Task<bool> UpdateIntegratorAsync(int id, Integrator integrator)
        {
            var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/{id}", integrator);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, $"Error updating integrator {id}");
            }
            return true;
        }
        public async Task<bool> DeleteIntegratorAsync(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, $"Error deleting integrator {id}");
                }

                return true;
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }          
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
