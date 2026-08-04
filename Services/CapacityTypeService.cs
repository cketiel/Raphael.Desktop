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
    public class CapacityTypeService : ICapacityTypeService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "capacitytypes";
        public CapacityTypeService()
        {
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<CapacityType>> GetCapacityTypesAsync()
        {
            var result = await _httpClient.GetFromJsonAsync<List<CapacityType>>(EndPoint);
            return result ?? new List<CapacityType>();
        }
    }
}
