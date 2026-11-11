using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// The vehicle inspections of a route-day and their evidence (RE-062). The photos and the
    /// signature come through the API, which decides who may see them; nothing is kept on disk.
    /// </summary>
    public class VehicleInspectionService
    {
        private const string EndPoint = "api/vehicle-inspections";
        private readonly HttpClient _httpClient = ApiClientFactory.Create();

        public async Task<List<VehicleInspectionDetailDto>> GetForRouteDayAsync(int vehicleRouteId, DateTime date)
        {
            var response = await _httpClient.GetAsync($"{EndPoint}?vehicleRouteId={vehicleRouteId}&date={date:yyyy-MM-dd}");
            if (!response.IsSuccessStatusCode)
                throw new Exception(response.ReasonPhrase);
            return await response.Content.ReadFromJsonAsync<List<VehicleInspectionDetailDto>>() ?? new();
        }

        /// <summary>The signed inspections of a period, newest first (at most 62 days): the driver's signature for the Manifest Report.</summary>
        public async Task<List<VehicleInspectionDetailDto>> GetSignedAsync(DateTime startDate, DateTime endDate)
        {
            var response = await _httpClient.GetAsync($"{EndPoint}/signed?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}");
            if (!response.IsSuccessStatusCode)
                throw new Exception(response.ReasonPhrase);
            return await response.Content.ReadFromJsonAsync<List<VehicleInspectionDetailDto>>() ?? new();
        }

        public async Task<VehicleInspectionDetailDto> GetAsync(int id)
        {
            var response = await _httpClient.GetAsync($"{EndPoint}/{id}");
            if (!response.IsSuccessStatusCode)
                throw new Exception(response.ReasonPhrase);
            return await response.Content.ReadFromJsonAsync<VehicleInspectionDetailDto>();
        }

        public Task<byte[]> GetPhotoAsync(int id, string code) =>
            GetBytesAsync($"{EndPoint}/{id}/items/{Uri.EscapeDataString(code)}/photo");

        public Task<byte[]> GetSignatureAsync(int id) =>
            GetBytesAsync($"{EndPoint}/{id}/signature");

        private async Task<byte[]> GetBytesAsync(string url)
        {
            var response = await _httpClient.GetAsync(url);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : null;
        }
    }
}
