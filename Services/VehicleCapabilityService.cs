using Raphael.Desktop.DTOs;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    /// <summary>RE-083: <c>api/vehicle-capability</c>. Reading is for the office; changing, for administrators.</summary>
    public interface IVehicleCapabilityService
    {
        /// <summary>Null when the server is older than the feature.</summary>
        Task<VehicleCapabilityDto?> GetAsync();
        Task UpdateClassAsync(int id, CapacityClassUpdateDto dto);
        Task UpdateSpaceTypeClassAsync(int id, int capacityTypeId);
        Task SaveConfigurationAsync(int? id, VehicleConfigurationSaveDto dto);
    }

    public class VehicleCapabilityService : IVehicleCapabilityService
    {
        private const string EndPoint = "vehicle-capability";
        private readonly HttpClient _http = ApiClientFactory.Create();

        public async Task<VehicleCapabilityDto?> GetAsync()
        {
            var response = await _http.GetAsync(EndPoint);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<VehicleCapabilityDto>();
        }

        public async Task UpdateClassAsync(int id, CapacityClassUpdateDto dto) =>
            await CheckAsync(await _http.PutAsJsonAsync($"{EndPoint}/classes/{id}", dto));

        public async Task UpdateSpaceTypeClassAsync(int id, int capacityTypeId) =>
            await CheckAsync(await _http.PutAsJsonAsync($"{EndPoint}/space-types/{id}", new SpaceTypeClassUpdateDto { CapacityTypeId = capacityTypeId }));

        public async Task SaveConfigurationAsync(int? id, VehicleConfigurationSaveDto dto) =>
            await CheckAsync(id is int existing
                ? await _http.PutAsJsonAsync($"{EndPoint}/configurations/{existing}", dto)
                : await _http.PostAsJsonAsync($"{EndPoint}/configurations", dto));

        /// <summary>Throws with the server's own reason when it gives one.</summary>
        private static async Task CheckAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;
            string? reason = null;
            try
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (json.RootElement.TryGetProperty("message", out var m)) reason = m.GetString();
            }
            catch (JsonException) { }
            throw new InvalidOperationException(reason ?? response.ReasonPhrase ?? response.StatusCode.ToString());
        }
    }
}
