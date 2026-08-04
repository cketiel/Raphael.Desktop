using Raphael.Desktop.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    public class GpsService
    {
        private readonly HttpClient _httpClient;
        private readonly string _endPoint = "gps";
        

        public GpsService()
        {
            _httpClient = ApiClientFactory.Create();         
        }
      
        public async Task<GpsDataDto> GetLatestGpsDataAsync(int vehicleRouteId)
        {
            var url = $"{_endPoint}/latest/{vehicleRouteId}";
            var response = await _httpClient.GetAsync(url);

            // This will throw an exception if the code is not 2xx (success)
            // Includes case 404 (NotFound) which is now possible.
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null; // It is valid not to find data, it is not a system error.
                }
                response.EnsureSuccessStatusCode();
            }

            return await response.Content.ReadFromJsonAsync<GpsDataDto>();
        }

        public async Task<List<GpsDataDto>> GetGpsHistoryAsync(int vehicleRouteId, DateTime date)
        {
            // Format the date correctly for the URL query string.
            string formattedDate = date.ToString("yyyy-MM-dd");
            var url = $"{_endPoint}/reports/history?vehicleRouteId={vehicleRouteId}&date={formattedDate}";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<List<GpsDataDto>>();
        }

    }
}
