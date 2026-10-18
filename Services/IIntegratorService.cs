using Raphael.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    public interface IIntegratorService
    {
        Task<List<Integrator>> GetIntegratorsAsync();
        Task<Integrator> AddIntegratorAsync(Integrator integrator);
        Task<bool> UpdateIntegratorAsync(int id, Integrator integrator);
        Task<bool> DeleteIntegratorAsync(int id);
        /// <summary>Uncontracts the Integrator or contracts it again (RE-056). The form no longer saves IsActive.</summary>
        Task SetActiveAsync(int id, bool isActive);
    }
}
