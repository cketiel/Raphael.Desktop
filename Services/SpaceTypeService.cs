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
    public class SpaceTypeService : ISpaceTypeService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "spacetypes";
        public SpaceTypeService()
        {
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<SpaceType>> GetSpaceTypesAsync()
        {
            var result = await _httpClient.GetFromJsonAsync<List<SpaceType>>(EndPoint);
            return result ?? new List<SpaceType>();
        }

        public async Task<SpaceType> CreateSpaceTypeAsync(SpaceType spaceType)
        {
            try
            {

                var response = await _httpClient.PostAsJsonAsync(EndPoint, spaceType);

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, "Error creating SpaceType");
                }

                return await response.Content.ReadFromJsonAsync<SpaceType>();
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }

        }

        public async Task<SpaceType> GetSpaceTypeByNameAsync(string name)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{EndPoint}/ByName/{name}");

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, $"Error getting Space Type {name}");
                }

                return await response.Content.ReadFromJsonAsync<SpaceType>();
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }
            //return await _httpClient.GetFromJsonAsync<Customer>($"{EndPoint}/{id}");
        }

        public async Task DeleteSpaceTypeAsync(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, $"Error deleting SpaceType with ID {id}");
                }
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
