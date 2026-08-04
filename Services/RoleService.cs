
using System.Net.Http;
using System.Net.Http.Json;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public class RoleService : IRoleService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "roles";
        public RoleService() 
        {
            _httpClient = ApiClientFactory.Create();
        }
        public async Task<List<Role>> GetRolesAsync()
        {
            var result = await _httpClient.GetFromJsonAsync<List<Role>>(EndPoint);
            return result ?? new List<Role>();
        }
        public async Task<Role> AddRoleAsync(Role role)
        {
            var response = await _httpClient.PostAsJsonAsync(EndPoint, role);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, "Error adding role");
            }
            return await response.Content.ReadFromJsonAsync<Role>();
        }

        public async Task<bool> UpdateRoleAsync(int id, Role role)
        {
            var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/{id}", role);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, $"Error updating role {id}");
            }
            return true;
        }
        public async Task<bool> DeleteRoleAsync(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiException(response, $"Error deleting role {id}");
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
