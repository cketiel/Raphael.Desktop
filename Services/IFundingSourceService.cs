using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public interface IFundingSourceService
    {
        Task<List<FundingSource>> GetFundingSourcesAsync(bool includeInactive);
        Task<FundingSource> CreateFundingSourceAsync(FundingSourceDto dto);
        Task<FundingSource> UpdateFundingSourceAsync(int id, FundingSourceDto dto);
        Task DeleteFundingSourceAsync(int id);
        Task ExportToExcelAsync(List<FundingSource> fundingSources);
    }
}
