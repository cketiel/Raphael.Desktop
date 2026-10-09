using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Exceptions;

namespace Raphael.Desktop.Services.Catalog
{
    /// <summary>
    /// The calls the two catalogs share. Only the endpoint and the types differ.
    /// </summary>
    /// <remarks>
    /// One class rather than two nearly identical ones. The integrator and provider catalogs
    /// answer the same seven calls with different row shapes, and keeping two copies in step is
    /// work that buys nothing.
    /// </remarks>
    public abstract class CatalogServiceBase<TListItem, TDetail, TSave, TImportRow>
    {
        private readonly HttpClient _httpClient;

        /// <summary>Relative to the api/ prefix the factory already applies.</summary>
        private readonly string _endPoint;

        protected CatalogServiceBase(string endPoint)
        {
            _endPoint = endPoint;
            _httpClient = ApiClientFactory.Create();
        }

        /// <summary>
        /// Searches the catalog. A POST, not a GET: the term can be a contact's name, and a
        /// name in a query string lands in the browser history and in every proxy log on the
        /// way — see SECURITY_FINDINGS.md F12.
        /// </summary>
        public async Task<CatalogPageDto<TListItem>> SearchAsync(
            CatalogSearchRequestDto request,
            CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_endPoint}/search", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, "Error searching the catalog");
            }

            return await response.Content.ReadFromJsonAsync<CatalogPageDto<TListItem>>(
                       cancellationToken: cancellationToken)
                   ?? new CatalogPageDto<TListItem>();
        }

        public async Task<TDetail?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.GetAsync($"{_endPoint}/{id}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, $"Error loading catalog entry {id}");
            }

            return await response.Content.ReadFromJsonAsync<TDetail>(
                cancellationToken: cancellationToken);
        }

        public async Task<TDetail> CreateAsync(TSave request, CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.PostAsJsonAsync(_endPoint, request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, "Error adding the catalog entry");
            }

            return (await response.Content.ReadFromJsonAsync<TDetail>(
                cancellationToken: cancellationToken))!;
        }

        public async Task<TDetail> UpdateAsync(
            int id,
            TSave request,
            CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.PutAsJsonAsync(
                $"{_endPoint}/{id}", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, $"Error saving catalog entry {id}");
            }

            return (await response.Content.ReadFromJsonAsync<TDetail>(
                cancellationToken: cancellationToken))!;
        }

        /// <summary>
        /// Sends one batch of rows read from a spreadsheet.
        /// </summary>
        /// <remarks>
        /// 207 is a normal answer, not a failure: some rows were rejected and others were not,
        /// and the verdict per row is the point. Only a status that carries no verdict is an
        /// error here.
        /// </remarks>
        public async Task<CatalogImportResultDto> ImportAsync(
            CatalogImportRequestDto<TImportRow> request,
            CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"{_endPoint}/import", request, cancellationToken);

            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MultiStatus)
            {
                throw await CreateApiException(response, "Error importing into the catalog");
            }

            return await response.Content.ReadFromJsonAsync<CatalogImportResultDto>(
                       cancellationToken: cancellationToken)
                   ?? new CatalogImportResultDto();
        }

        public async Task<CatalogGeocodeResultDto> GeocodeAsync(
            IEnumerable<int> ids,
            CancellationToken cancellationToken = default)
        {
            var request = new CatalogGeocodeRequestDto { Ids = new List<int>(ids) };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_endPoint}/geocode", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, "Error resolving coordinates");
            }

            return await response.Content.ReadFromJsonAsync<CatalogGeocodeResultDto>(
                       cancellationToken: cancellationToken)
                   ?? new CatalogGeocodeResultDto();
        }

        /// <summary>
        /// The form for adding this entity to our company, filled in from the catalog row.
        /// Nothing is written until the user saves it.
        /// </summary>
        protected async Task<TDraft?> GetCompanyDraftAsync<TDraft>(
            int id,
            CancellationToken cancellationToken = default)
        {
            var response = await _httpClient.GetAsync(
                $"{_endPoint}/{id}/company-draft", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, "Error preparing the entry");
            }

            return await response.Content.ReadFromJsonAsync<TDraft>(
                cancellationToken: cancellationToken);
        }

        /// <summary>Reads a ProblemDetails body, falling back to whatever came back.</summary>
        private static async Task<ApiException> CreateApiException(
            HttpResponseMessage response,
            string context)
        {
            try
            {
                var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
                return new ApiException(
                    message: $"{context}: {problemDetails?.Title}",
                    statusCode: response.StatusCode,
                    details: problemDetails?.Detail);
            }
            catch
            {
                var content = await response.Content.ReadAsStringAsync();
                return new ApiException(
                    message: $"{context}: Unspecified error",
                    statusCode: response.StatusCode,
                    details: content);
            }
        }
    }
}
