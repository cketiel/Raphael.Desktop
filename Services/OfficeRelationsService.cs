using Raphael.Desktop.DTOs;
using Raphael.Desktop.Exceptions;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// Who works with whom, as the office sees and corrects it (PROVIDER_ACCESS_MODEL.md §3.6): a
    /// clinic's contracted Providers and its catalog row, a Provider's clinics. Office admins only.
    /// </summary>
    public interface IOfficeRelationsService
    {
        Task<IntegratorRelationsDto> GetIntegratorRelationsAsync(int integratorId);
        Task<IntegratorRelationsDto> AddProviderAsync(int integratorId, int catalogProviderId);
        Task<IntegratorRelationsDto> RemoveProviderAsync(int integratorId, int catalogProviderId);
        Task<IntegratorRelationsDto> LinkCatalogAsync(int integratorId, int? catalogIntegratorId);
        Task<List<ProviderClinicRowDto>> GetProviderClinicsAsync(int providerId);
    }

    public class OfficeRelationsService : IOfficeRelationsService
    {
        private readonly HttpClient _httpClient = ApiClientFactory.Create();

        public Task<IntegratorRelationsDto> GetIntegratorRelationsAsync(int integratorId) =>
            Read<IntegratorRelationsDto>(_httpClient.GetAsync($"integrators/{integratorId}/relations"));

        public Task<IntegratorRelationsDto> AddProviderAsync(int integratorId, int catalogProviderId) =>
            Read<IntegratorRelationsDto>(_httpClient.PostAsync($"integrators/{integratorId}/providers/{catalogProviderId}", null));

        public Task<IntegratorRelationsDto> RemoveProviderAsync(int integratorId, int catalogProviderId) =>
            Read<IntegratorRelationsDto>(_httpClient.DeleteAsync($"integrators/{integratorId}/providers/{catalogProviderId}"));

        public Task<IntegratorRelationsDto> LinkCatalogAsync(int integratorId, int? catalogIntegratorId) =>
            Read<IntegratorRelationsDto>(_httpClient.PutAsJsonAsync($"integrators/{integratorId}/catalog-link", new { catalogIntegratorId }));

        public Task<List<ProviderClinicRowDto>> GetProviderClinicsAsync(int providerId) =>
            Read<List<ProviderClinicRowDto>>(_httpClient.GetAsync($"providers/{providerId}/clinics"));

        /// <remarks>A refusal of the backend (400) carries its reason as plain text: that is what the user reads.</remarks>
        private static async Task<T> Read<T>(Task<HttpResponseMessage> call) where T : new()
        {
            HttpResponseMessage response;
            try
            {
                response = await call;
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Server connection error", ex);
            }
            if (!response.IsSuccessStatusCode)
            {
                var text = (await response.Content.ReadAsStringAsync()).Trim('"');
                throw new ApiException(string.IsNullOrWhiteSpace(text) ? response.ReasonPhrase ?? "Error" : text, response.StatusCode, text);
            }
            return await response.Content.ReadFromJsonAsync<T>() ?? new T();
        }
    }
}
