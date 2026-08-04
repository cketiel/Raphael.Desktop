using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    public interface IBillingItemService
    {
        Task<List<BillingItemGetDto>> GetBillingItemsAsync();
        Task<BillingItem> CreateBillingItemAsync(BillingItem billingItem);
        Task<BillingItem> UpdateBillingItemAsync(BillingItem billingItem);
        Task DeleteBillingItemAsync(int id);
    }
}