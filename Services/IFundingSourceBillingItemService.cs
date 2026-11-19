
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public interface IFundingSourceBillingItemService
    {
        Task<List<FundingSourceBillingItemGetDto>> GetAllAsync();
        Task<List<FundingSourceBillingItemGetDto>> GetByFundingSourceIdAsync(int fundingSourceId, bool includeExpired);

        Task<FundingSourceBillingItem> CreateAsync(FundingSourceBillingItemDto dto);
        Task UpdateAsync(int id, FundingSourceBillingItemDto dto);
        Task<string> RemoveAsync(int id);
        Task NewPeriodAsync(int id, FundingSourceBillingItemDto dto);
        Task RenewAsync(int id);
        Task<BillingRatesCopyResultDto> CopyAsync(BillingRatesCopyRequestDto request);
        Task<BillingSimulationResultDto> SimulateAsync(BillingSimulationRequestDto request, CancellationToken cancellationToken = default);
        Task<List<BillingRateExpiringDto>> GetExpiringAsync(int months = 6);
        Task<List<BillingRatesHealthDto>> GetHealthAsync();

        Task ExportToExcelAsync(List<FundingSourceBillingItem> items);
    }
}
