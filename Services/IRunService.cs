using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public interface IRunService
    {
        Task<List<VehicleRoute>> GetAllAsync();
        Task<VehicleRoute> GetByIdAsync(int id);
        Task<VehicleRoute> CreateAsync(VehicleRouteDto dto);
        Task UpdateAsync(int id, VehicleRouteDto dto);
        Task DeleteAsync(int id);
        Task<bool> CancelAsync(int id);
    }
}
