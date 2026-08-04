

using Raphael.Desktop.DTOs;
using Raphael.Desktop.Exceptions;
using Raphael.Desktop.Models;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    public class BillingItemService : IBillingItemService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "billingitems"; 

        public BillingItemService()
        {           
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<BillingItemGetDto>> GetBillingItemsAsync()
        {
            // El tipo genérico ahora es el DTO
            var result = await _httpClient.GetFromJsonAsync<List<BillingItemGetDto>>(EndPoint);
            return result ?? new List<BillingItemGetDto>();
        }

        public async Task<BillingItem> CreateBillingItemAsync(BillingItem billingItem)
        {
            var dto = new BillingItemDto
            {
                Description = billingItem.Description,
                UnitId = billingItem.UnitId,
                IsCopay = billingItem.IsCopay,
                ARAccount = billingItem.ARAccount,
                ARSubAccount = billingItem.ARSubAccount,
                ARCompany = billingItem.ARCompany,
                APAccount = billingItem.APAccount,
                APSubAccount = billingItem.APSubAccount,
                APCompany = billingItem.APCompany
            };

            var response = await _httpClient.PostAsJsonAsync(EndPoint, dto);

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, "Error creating Billing Item");
            }
            return await response.Content.ReadFromJsonAsync<BillingItem>();
        }

        public async Task<BillingItem> UpdateBillingItemAsync(BillingItem billingItem)
        {
            var dto = new BillingItemDto
            {
                Description = billingItem.Description,
                UnitId = billingItem.UnitId,
                IsCopay = billingItem.IsCopay,
                ARAccount = billingItem.ARAccount,
                ARSubAccount = billingItem.ARSubAccount,
                ARCompany = billingItem.ARCompany,
                APAccount = billingItem.APAccount,
                APSubAccount = billingItem.APSubAccount,
                APCompany = billingItem.APCompany
            };

            var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/{billingItem.Id}", dto);

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, "Error updating Billing Item");
            }
            return await response.Content.ReadFromJsonAsync<BillingItem>();
        }

        public async Task DeleteBillingItemAsync(int id)
        {
            var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiException(response, "Error deleting Billing Item");
            }
        }
       
        private async Task<ApiException> CreateApiException(HttpResponseMessage response, string context)
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

    public class ProblemDetails
    {
        public string Title { get; set; }
        public string Detail { get; set; }
    }
}