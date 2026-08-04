using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public interface IVehicleTypeService
    {
        Task<List<VehicleType>> GetVehicleTypesAsync();
    }
}
