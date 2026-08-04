using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services
{
    public class VehicleTypeService : IVehicleTypeService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "vehicletypes";

        public VehicleTypeService()
        {
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<VehicleType>> GetVehicleTypesAsync()
        {
            return await _httpClient.GetFromJsonAsync<List<VehicleType>>(EndPoint) ?? new List<VehicleType>();
        }
    }
}
