using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Raphael.Desktop.Models;
using System.IO;

namespace Raphael.Desktop.Services
{
    public class ProviderService : IProviderService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "providers";

        public ProviderService()
        {
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<Provider>> GetProvidersAsync()
        {
            var result = await _httpClient.GetFromJsonAsync<List<Provider>>(EndPoint);
            return result ?? new List<Provider>();
        }

        /// <summary>Only Id and Name are filled: the Providers with at least one active user.</summary>
        public async Task<List<Provider>> GetAssignableProvidersAsync()
        {
            var result = await _httpClient.GetFromJsonAsync<List<Provider>>($"{EndPoint}/assignable");
            return result ?? new List<Provider>();
        }

        public async Task<Provider> AddProviderAsync(Provider provider, string localImagePath)
        {
            using var content = BuildForm(provider, localImagePath);

            var response = await _httpClient.PostAsync(EndPoint, content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<Provider>();
        }

        public async Task<bool> UpdateProviderAsync(int id, Provider provider, string localImagePath)
        {
            using var content = BuildForm(provider, localImagePath);

            var response = await _httpClient.PutAsync($"{EndPoint}/{id}", content);
            return response.IsSuccessStatusCode;
        }

        /// <summary>
        /// The provider as multipart form data, which is what the endpoint takes because of the
        /// logo file.
        /// </summary>
        /// <remarks>
        /// Built in one place since RE-027: the create and update paths had the same six lines
        /// written twice, and the fields added in that slice would have had to be added twice
        /// as well — which is exactly how one of the two ends up missing a field.
        /// </remarks>
        private static MultipartFormDataContent BuildForm(Provider provider, string localImagePath)
        {
            var content = new MultipartFormDataContent
            {
                { new StringContent(provider.Name ?? ""), "Name" },
                { new StringContent(provider.Address ?? ""), "Address" },
                { new StringContent(provider.Email ?? ""), "Email" },
                { new StringContent(provider.Phone ?? ""), "Phone" },
                { new StringContent(Number(provider.Latitude)), "Latitude" },
                { new StringContent(Number(provider.Longitude)), "Longitude" },

                // ⚠️ TimeZoneId travelled in neither direction until RE-027, so the Providers
                // screen flagged every provider as having no timezone and offered no way to
                // answer it. What a pickup time means depends on this field.
                { new StringContent(provider.TimeZoneId ?? ""), "TimeZoneId" },

                { new StringContent(provider.Website ?? ""), "Website" },
                { new StringContent(provider.ContactName ?? ""), "ContactName" },
                { new StringContent(provider.Comments ?? ""), "Comments" }
            };

            if (provider.CatalogProviderId.HasValue)
            {
                content.Add(
                    new StringContent(provider.CatalogProviderId.Value.ToString(CultureInfo.InvariantCulture)),
                    "CatalogProviderId");
            }

            if (!string.IsNullOrEmpty(localImagePath))
            {
                var fileStream = File.OpenRead(localImagePath);
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                content.Add(fileContent, "LogoFile", Path.GetFileName(localImagePath));
            }

            return content;
        }

        /// <summary>
        /// A coordinate as the server reads it, whatever the operator's regional settings say.
        /// </summary>
        /// <remarks>
        /// ⚠️ Plain <c>ToString()</c> here used the machine culture, so a dispatcher on a Spanish
        /// Windows sent "25,7617" and model binding did not get a latitude. The bug only shows
        /// on some machines, which is the worst kind.
        /// </remarks>
        private static string Number(double? value) =>
            value?.ToString(CultureInfo.InvariantCulture) ?? "";

        public async Task SetTenantAsync(int id, bool isTenant)
        {
            var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/{id}/tenant", new { isTenant });
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body);
            }
        }

        /// <summary>Raphael (0) or External (1). Going External also stops it being a tenant.</summary>
        public async Task SetWorkModeAsync(int id, int workMode)
        {
            var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/{id}/work-mode", new { workMode });
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new Exception(string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body);
            }
        }

        public async Task<bool> DeleteProviderAsync(int id)
        {
            var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");
            return response.IsSuccessStatusCode;
        }
    }
}
