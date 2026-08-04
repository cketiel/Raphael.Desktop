using System;
using System.Collections.Generic;
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

        public async Task<Provider> AddProviderAsync(Provider provider, string localImagePath)
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(provider.Name ?? ""), "Name");
            content.Add(new StringContent(provider.Address ?? ""), "Address");
            content.Add(new StringContent(provider.Email ?? ""), "Email");
            content.Add(new StringContent(provider.Phone ?? ""), "Phone");
            content.Add(new StringContent(provider.Latitude?.ToString() ?? ""), "Latitude");
            content.Add(new StringContent(provider.Longitude?.ToString() ?? ""), "Longitude");

            if (!string.IsNullOrEmpty(localImagePath))
            {
                var fileStream = File.OpenRead(localImagePath);
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                content.Add(fileContent, "LogoFile", Path.GetFileName(localImagePath));
            }

            var response = await _httpClient.PostAsync(EndPoint, content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<Provider>();
        }

        public async Task<bool> UpdateProviderAsync(int id, Provider provider, string localImagePath)
        {
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(provider.Name ?? ""), "Name");
            content.Add(new StringContent(provider.Address ?? ""), "Address");
            content.Add(new StringContent(provider.Email ?? ""), "Email");
            content.Add(new StringContent(provider.Phone ?? ""), "Phone");
            content.Add(new StringContent(provider.Latitude?.ToString() ?? ""), "Latitude");
            content.Add(new StringContent(provider.Longitude?.ToString() ?? ""), "Longitude");

            if (!string.IsNullOrEmpty(localImagePath))
            {
                var fileStream = File.OpenRead(localImagePath);
                var fileContent = new StreamContent(fileStream);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                content.Add(fileContent, "LogoFile", Path.GetFileName(localImagePath));
            }

            var response = await _httpClient.PutAsync($"{EndPoint}/{id}", content);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> DeleteProviderAsync(int id)
        {
            var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");
            return response.IsSuccessStatusCode;
        }
    }
}