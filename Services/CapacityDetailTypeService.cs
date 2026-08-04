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
    public class CapacityDetailTypeService : ICapacityDetailTypeService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "capacitydetailtype";

        public CapacityDetailTypeService()
        {
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<CapacityDetailType>> GetCapacityDetailTypesAsync()
        {
            return await _httpClient.GetFromJsonAsync<List<CapacityDetailType>>(EndPoint) ?? new List<CapacityDetailType>();
        }
    }
}
