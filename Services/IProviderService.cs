using System.Collections.Generic;
using System.Threading.Tasks;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public interface IProviderService
    {
        Task<List<Provider>> GetProvidersAsync();
        Task<Provider> AddProviderAsync(Provider provider, string localImagePath);
        Task<bool> UpdateProviderAsync(int id, Provider provider, string localPath);
        Task<bool> DeleteProviderAsync(int id);
    }
}