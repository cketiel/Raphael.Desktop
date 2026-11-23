using Raphael.Desktop.DTOs;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    /// <summary>RE-082: drivers' pay rates, <c>api/driver-pay-rates</c>. Administrators only.</summary>
    public interface IDriverPayRateService
    {
        /// <summary>The rate in force today per driver; empty for anyone who is not an administrator.</summary>
        Task<List<DriverPayRateDto>> GetCurrentAsync();
        /// <summary>Newest first, one page at a time.</summary>
        Task<List<DriverPayRateDto>> GetHistoryAsync(int userId, int skip = 0, int take = 10);
        /// <summary>Saves a new entry. Throws with the server's reason when it refuses it.</summary>
        Task<DriverPayRateDto> AddAsync(int userId, DriverPayRateCreateDto dto);
    }

    public class DriverPayRateService : IDriverPayRateService
    {
        private readonly HttpClient _httpClient = ApiClientFactory.Create();

        public async Task<List<DriverPayRateDto>> GetCurrentAsync()
        {
            var response = await _httpClient.GetAsync("driver-pay-rates/current");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) return new();
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<DriverPayRateDto>>() ?? new();
        }

        public async Task<List<DriverPayRateDto>> GetHistoryAsync(int userId, int skip = 0, int take = 10)
        {
            var response = await _httpClient.GetAsync($"driver-pay-rates/user/{userId}?skip={skip}&take={take}");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) return new();
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<DriverPayRateDto>>() ?? new();
        }

        public async Task<DriverPayRateDto> AddAsync(int userId, DriverPayRateCreateDto dto)
        {
            var response = await _httpClient.PostAsJsonAsync($"driver-pay-rates/user/{userId}", dto);
            if (!response.IsSuccessStatusCode)
            {
                var reason = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(reason) ? response.ReasonPhrase : reason.Trim('"'));
            }
            return (await response.Content.ReadFromJsonAsync<DriverPayRateDto>())!;
        }
    }
}
