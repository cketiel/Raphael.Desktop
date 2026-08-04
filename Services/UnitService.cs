using Raphael.Desktop.Models;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    public class UnitService : IUnitService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "units"; 

        public UnitService()
        {
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<Unit>> GetUnitsAsync()
        {
            var result = await _httpClient.GetFromJsonAsync<List<Unit>>(EndPoint);
            return result ?? new List<Unit>();
        }
    }
}