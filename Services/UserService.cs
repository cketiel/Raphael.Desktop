using System.Net.Http;
using System.Net.Http.Json;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public class UserService : IUserService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "users";
        public UserService()
        {
            _httpClient = ApiClientFactory.Create();
        }
        public async Task<List<User>> GetAllAsync()
        {
            var result = await _httpClient.GetFromJsonAsync<List<User>>(EndPoint);
            return result ?? new List<User>();
        }

        public async Task<List<User>> GetUsersAsync()
        {          
            var result = await _httpClient.GetFromJsonAsync<List<User>>(EndPoint);
            return result ?? new List<User>();
        }

        public async Task<User> GetUserByIdAsync(int id)
        {
            var result = await _httpClient.GetFromJsonAsync<User>($"{EndPoint}/{id}");
            return result;
        }

        public async Task<User> AddUserAsync(UserCreateDto userDto)
        {
            var response = await _httpClient.PostAsJsonAsync($"{EndPoint}/create", userDto);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, "Error adding user");
            }
            return await response.Content.ReadFromJsonAsync<User>();
        }

        public async Task<bool> UpdateUserAsync(UserUpdateDto userDto)
        {
            var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/update", userDto);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, $"Error updating user {userDto.Id}");
            }
            return true;
        }

        public async Task<bool> DeleteUserAsync(int id)
        {
            var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, $"Error deleting user {id}");
            }
            return true;
        }

        public async Task<bool> ChangePasswordAsync(ChangePasswordDto dto)
        {
            var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/change-password", dto);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, "Error changing password");
            }
            return true;
        }
       
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
