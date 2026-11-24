using Raphael.Desktop.DTOs;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    /// <summary>RE-089: the signed-in user's preferences, <c>api/user-preferences</c>.</summary>
    public interface IUserPreferenceService
    {
        /// <summary>Null when there is none. Throws when the server cannot answer.</summary>
        Task<string?> GetAsync(string key);
        Task SaveAsync(string key, string value);
    }

    public class UserPreferenceService : IUserPreferenceService
    {
        private readonly HttpClient _httpClient = ApiClientFactory.Create();

        public async Task<string?> GetAsync(string key)
        {
            var response = await _httpClient.GetAsync($"user-preferences/{key}");
            if (response.StatusCode == HttpStatusCode.NoContent) return null;
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<UserPreferenceDto>())?.Value;
        }

        public async Task SaveAsync(string key, string value)
        {
            var response = await _httpClient.PutAsJsonAsync($"user-preferences/{key}", new UserPreferenceDto { Value = value });
            response.EnsureSuccessStatusCode();
        }
    }
}
