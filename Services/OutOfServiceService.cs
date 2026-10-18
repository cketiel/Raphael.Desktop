using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Exceptions;

namespace Raphael.Desktop.Services
{
    /// <summary>Vehicles and drivers out of service, and so their routes (RE-053).</summary>
    public interface IOutOfServiceService
    {
        Task<List<OutOfServiceRecordDto>> GetActiveAsync();

        Task<OutOfServiceReasonCatalogDto> GetReasonsAsync();

        /// <summary>What it would do. Nothing is saved.</summary>
        Task<OutOfServicePreviewDto> PreviewAsync(OutOfServiceRequestDto request);

        /// <summary>Does it, with the preview's fingerprint. Refused with a 409 if the trips changed since.</summary>
        Task<OutOfServicePreviewDto> ExecuteAsync(OutOfServiceRequestDto request);

        Task<OutOfServiceRecordDto> ReturnToServiceAsync(int recordId);
    }

    public class OutOfServiceService : IOutOfServiceService
    {
        private const string EndPoint = "out-of-service";

        private readonly HttpClient _httpClient;

        public OutOfServiceService()
        {
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<OutOfServiceRecordDto>> GetActiveAsync() =>
            await SendAsync(() => _httpClient.GetAsync($"{EndPoint}/active"), "Error reading what is out of service")
                .ReadAsync<List<OutOfServiceRecordDto>>() ?? new List<OutOfServiceRecordDto>();

        public async Task<OutOfServiceReasonCatalogDto> GetReasonsAsync() =>
            await SendAsync(() => _httpClient.GetAsync($"{EndPoint}/reasons"), "Error reading the reasons")
                .ReadAsync<OutOfServiceReasonCatalogDto>() ?? new OutOfServiceReasonCatalogDto();

        public Task<OutOfServicePreviewDto> PreviewAsync(OutOfServiceRequestDto request) =>
            SendAsync(() => _httpClient.PostAsJsonAsync($"{EndPoint}/preview", request), "Error working out the preview")
                .ReadAsync<OutOfServicePreviewDto>();

        public Task<OutOfServicePreviewDto> ExecuteAsync(OutOfServiceRequestDto request) =>
            SendAsync(() => _httpClient.PostAsJsonAsync(EndPoint, request), "Error taking it out of service")
                .ReadAsync<OutOfServicePreviewDto>();

        public Task<OutOfServiceRecordDto> ReturnToServiceAsync(int recordId) =>
            SendAsync(() => _httpClient.PostAsync($"{EndPoint}/{recordId}/return", null), "Error putting it back in service")
                .ReadAsync<OutOfServiceRecordDto>();

        private static async Task<HttpResponseMessage> SendAsync(System.Func<Task<HttpResponseMessage>> send, string context)
        {
            HttpResponseMessage response;
            try
            {
                response = await send();
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException("Connection error with the server.", ex);
            }

            if (response.IsSuccessStatusCode) return response;

            // The server answers its refusals as ProblemDetails with a readable title: say that.
            try
            {
                var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
                throw new ApiException(problem?.Title ?? context, response.StatusCode, problem?.Detail);
            }
            catch (ApiException) { throw; }
            catch
            {
                var text = await response.Content.ReadAsStringAsync();
                throw new ApiException(string.IsNullOrWhiteSpace(text) ? context : text, response.StatusCode, null);
            }
        }
    }

    internal static class OutOfServiceResponseExtensions
    {
        public static async Task<T> ReadAsync<T>(this Task<HttpResponseMessage> response) =>
            await (await response).Content.ReadFromJsonAsync<T>();
    }
}
