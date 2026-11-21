using Raphael.Desktop.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    /// <summary>RE-081: hours and miles per driver and day, from <c>GET api/reports/productivity</c>.</summary>
    public interface IProductivityReportService
    {
        Task<List<ProductivityReportRowDto>> GetAsync(DateTime startDate, DateTime endDate, IReadOnlyCollection<int> driverIds, IReadOnlyCollection<int> vehicleIds);
    }

    public class ProductivityReportService : IProductivityReportService
    {
        private readonly HttpClient _httpClient = ApiClientFactory.Create();

        public async Task<List<ProductivityReportRowDto>> GetAsync(DateTime startDate, DateTime endDate, IReadOnlyCollection<int> driverIds, IReadOnlyCollection<int> vehicleIds)
        {
            // Ids and dates only: nothing about a patient ever goes in the query string.
            var uri = $"reports/productivity?startDate={startDate:yyyy-MM-dd}&endDate={endDate:yyyy-MM-dd}";
            if (driverIds?.Count > 0) uri += "&driverIds=" + string.Join(",", driverIds);
            if (vehicleIds?.Count > 0) uri += "&vehicleIds=" + string.Join(",", vehicleIds);

            var response = await _httpClient.GetAsync(uri);
            if (response.StatusCode == HttpStatusCode.NotFound) return new List<ProductivityReportRowDto>();   // not office staff
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<ProductivityReportRowDto>>() ?? new List<ProductivityReportRowDto>();
        }
    }
}
