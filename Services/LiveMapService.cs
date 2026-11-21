using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;

namespace Raphael.Desktop.Services
{
    public interface ILiveMapService
    {
        /// <summary>RE-080: every line, its group and last position, in one call.</summary>
        Task<LiveMapSnapshotDto> GetSnapshotAsync(CancellationToken ct = default);

        /// <summary>RE-080: the route card. Null when the line is not visible to this user.</summary>
        Task<LiveMapRouteDetailDto> GetRouteDetailAsync(int vehicleRouteId, CancellationToken ct = default);

        /// <summary>RE-080: the line's day on the map, stops and legs.</summary>
        Task<LiveMapRouteLegsDto> GetRouteLegsAsync(int vehicleRouteId, CancellationToken ct = default);

        /// <summary>RE-080: the traffic from the line's vehicle to its next stop. Null when there is nothing to show.</summary>
        Task<LiveMapRouteTrafficDto> GetRouteTrafficAsync(int vehicleRouteId, CancellationToken ct = default);
    }

    /// <summary>Reads the live map's opening snapshot (<c>GET api/live-map</c>).</summary>
    public class LiveMapService : ILiveMapService
    {
        private readonly HttpClient _httpClient = ApiClientFactory.Create();

        public async Task<LiveMapSnapshotDto> GetSnapshotAsync(CancellationToken ct = default)
        {
            var response = await _httpClient.GetAsync("live-map", ct);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<LiveMapSnapshotDto>(cancellationToken: ct)
                   ?? new LiveMapSnapshotDto();
        }

        public async Task<LiveMapRouteDetailDto> GetRouteDetailAsync(int vehicleRouteId, CancellationToken ct = default)
        {
            var response = await _httpClient.GetAsync($"live-map/routes/{vehicleRouteId}", ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<LiveMapRouteDetailDto>(cancellationToken: ct);
        }

        public async Task<LiveMapRouteLegsDto> GetRouteLegsAsync(int vehicleRouteId, CancellationToken ct = default)
        {
            var response = await _httpClient.GetAsync($"live-map/routes/{vehicleRouteId}/legs", ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<LiveMapRouteLegsDto>(cancellationToken: ct);
        }

        public async Task<LiveMapRouteTrafficDto> GetRouteTrafficAsync(int vehicleRouteId, CancellationToken ct = default)
        {
            var response = await _httpClient.GetAsync($"live-map/routes/{vehicleRouteId}/traffic", ct);
            if (response.StatusCode is System.Net.HttpStatusCode.NoContent or System.Net.HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<LiveMapRouteTrafficDto>(cancellationToken: ct);
        }
    }
}
