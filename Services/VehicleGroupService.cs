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
    public class VehicleGroupService: IVehicleGroupService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "vehiclegroups";
        public VehicleGroupService()
        {
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<VehicleGroup>> GetGroupsAsync()
        {
            var result = await _httpClient.GetFromJsonAsync<List<VehicleGroup>>(EndPoint);
            return result ?? new List<VehicleGroup>();
        }

        public async Task<VehicleGroup> CreateGroupAsync(VehicleGroup vehicleGroup)
        {
            try
            {

                var response = await _httpClient.PostAsJsonAsync(EndPoint, vehicleGroup);

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, "Error creating Group");
                }

                return await response.Content.ReadFromJsonAsync<VehicleGroup>();
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }

        }

        public async Task<bool> DeleteGroupAsync(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, $"Error deleting group {id}");
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
