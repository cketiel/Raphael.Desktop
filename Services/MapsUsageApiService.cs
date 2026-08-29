using Raphael.Desktop.DTOs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    /// <inheritdoc cref="IMapsUsageApiService"/>
    public class MapsUsageApiService : IMapsUsageApiService
    {
        private const string UsageEndpoint = "admin/maps-usage";
        private const string SettingsEndpoint = "admin/settings";

        public async Task<MapsUsageSummaryDto> GetSummaryAsync(DateTime from, DateTime to)
        {
            using (var client = ApiClientFactory.Create())
            {
                var response = await client.GetAsync(
                    $"{UsageEndpoint}/summary?from={Iso(from)}&to={Iso(to)}");

                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<MapsUsageSummaryDto>()
                    ?? new MapsUsageSummaryDto();
            }
        }

        public async Task<List<MapsUsagePointDto>> GetDailyAsync(DateTime from, DateTime to)
        {
            using (var client = ApiClientFactory.Create())
            {
                var response = await client.GetAsync(
                    $"{UsageEndpoint}/daily?from={Iso(from)}&to={Iso(to)}");

                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<List<MapsUsagePointDto>>()
                    ?? new List<MapsUsagePointDto>();
            }
        }

        public async Task<MapsUsageTotalsDto> GetTotalsAsync()
        {
            using (var client = ApiClientFactory.Create())
            {
                var response = await client.GetAsync($"{UsageEndpoint}/totals");

                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<MapsUsageTotalsDto>()
                    ?? new MapsUsageTotalsDto();
            }
        }

        public async Task<List<MapsPricingTierDto>> GetPricingAsync()
        {
            using (var client = ApiClientFactory.Create())
            {
                var response = await client.GetAsync($"{UsageEndpoint}/pricing");

                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<List<MapsPricingTierDto>>()
                    ?? new List<MapsPricingTierDto>();
            }
        }

        public async Task<List<SystemSettingDto>> GetSettingsAsync()
        {
            using (var client = ApiClientFactory.Create())
            {
                var response = await client.GetAsync(SettingsEndpoint);

                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<List<SystemSettingDto>>()
                    ?? new List<SystemSettingDto>();
            }
        }

        public async Task<string> SetSettingAsync(string key, string value)
        {
            using (var client = ApiClientFactory.Create())
            {
                var response = await client.PutAsJsonAsync(
                    $"{SettingsEndpoint}/{key}",
                    new SystemSettingUpdateDto { Value = value });

                if (response.IsSuccessStatusCode) return null;

                // The server explains a rejected value in plain words — "the buffer must be a
                // whole percentage between 0 and 100" — and the administrator should read that
                // rather than a status code.
                var body = await response.Content.ReadAsStringAsync();

                return string.IsNullOrWhiteSpace(body)
                    ? $"The server rejected the change ({(int)response.StatusCode})."
                    : body.Trim('"');
            }
        }

        public async Task ReportUsageAsync(string sku, int count = 1)
        {
            try
            {
                using (var client = ApiClientFactory.Create())
                {
                    await client.PostAsJsonAsync("routing/usage", new MapsUsageReportDto
                    {
                        Items = new List<MapsUsageReportItemDto>
                        {
                            new MapsUsageReportItemDto { Sku = sku, Count = count }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                // ⚠️ Never surfaces. A missed tally makes the panel slightly low; an exception
                // here would interrupt a dispatcher typing an address.
                Debug.WriteLine($"Could not report Maps usage ({sku}): {ex.Message}");
            }
        }

        private static string Iso(DateTime value) =>
            value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
