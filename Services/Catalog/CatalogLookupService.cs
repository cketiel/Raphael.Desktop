using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;

namespace Raphael.Desktop.Services.Catalog
{
    /// <summary>The fixed lists the catalog screens are built out of.</summary>
    public interface ICatalogLookupService
    {
        Task<List<CatalogCategoryDto>> GetIntegratorCategoriesAsync(
            CancellationToken cancellationToken = default);

        Task<List<CatalogCategoryDto>> GetProviderCategoriesAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// The counties. <paramref name="inUseOnly"/> narrows them to the ones the catalog
        /// actually holds rows for, which is what a filter dropdown wants: 67 entries when the
        /// data covers eleven is a list nobody reads.
        /// </summary>
        Task<List<CountyDto>> GetCountiesAsync(
            string? state = null,
            bool inUseOnly = true,
            CancellationToken cancellationToken = default);
    }

    /// <inheritdoc cref="ICatalogLookupService"/>
    public class CatalogLookupService : ICatalogLookupService
    {
        private readonly HttpClient _httpClient;
        private const string EndPoint = "admin/catalog";

        public CatalogLookupService()
        {
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<CatalogCategoryDto>> GetIntegratorCategoriesAsync(
            CancellationToken cancellationToken = default)
        {
            return await _httpClient.GetFromJsonAsync<List<CatalogCategoryDto>>(
                       $"{EndPoint}/integrator-categories", cancellationToken)
                   ?? new List<CatalogCategoryDto>();
        }

        public async Task<List<CatalogCategoryDto>> GetProviderCategoriesAsync(
            CancellationToken cancellationToken = default)
        {
            return await _httpClient.GetFromJsonAsync<List<CatalogCategoryDto>>(
                       $"{EndPoint}/provider-categories", cancellationToken)
                   ?? new List<CatalogCategoryDto>();
        }

        public async Task<List<CountyDto>> GetCountiesAsync(
            string? state = null,
            bool inUseOnly = true,
            CancellationToken cancellationToken = default)
        {
            var url = $"{EndPoint}/counties?inUseOnly={inUseOnly}";

            if (!string.IsNullOrWhiteSpace(state))
            {
                url += $"&state={System.Uri.EscapeDataString(state)}";
            }

            return await _httpClient.GetFromJsonAsync<List<CountyDto>>(url, cancellationToken)
                   ?? new List<CountyDto>();
        }
    }
}
