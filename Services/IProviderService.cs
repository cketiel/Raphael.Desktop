using System.Collections.Generic;
using System.Threading.Tasks;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public interface IProviderService
    {
        Task<List<Provider>> GetProvidersAsync();
        /// <summary>The Providers a trip may be given to: at least one active user (PROVIDER_ACCESS_MODEL.md §3.3).</summary>
        Task<List<Provider>> GetAssignableProvidersAsync();
        Task<Provider> AddProviderAsync(Provider provider, string localImagePath);
        Task<bool> UpdateProviderAsync(int id, Provider provider, string localPath);
        Task<bool> DeleteProviderAsync(int id);
        /// <summary>Makes the Provider a client company of the platform, or stops it being one (RE-049).</summary>
        Task SetTenantAsync(int id, bool isTenant);
        /// <summary>Raphael (0) or External (1); going External also stops it being a tenant.</summary>
        Task SetWorkModeAsync(int id, int workMode);
        /// <summary>Trips the Provider still has from today on, to warn before switching it off (RE-055).</summary>
        Task<int> GetPendingTripsAsync(int id);
        /// <summary>Switches the Provider off or back on (RE-055). Its users stop at their next sign-in or renewal.</summary>
        Task SetActiveAsync(int id, bool isActive);
    }
}