using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public interface IVehicleService
    {
        Task<List<Vehicle>> GetVehiclesAsync();
        Task<Vehicle> AddVehicleAsync(Vehicle vehicle);
        Task<bool> UpdateVehicleAsync(int id, Vehicle vehicle);
        Task<bool> DeleteVehicleAsync(int id);
    }
}
