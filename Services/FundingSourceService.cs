

using Raphael.Desktop.DTOs; 
using Raphael.Desktop.Models;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using ClosedXML.Excel; 
using System.IO;

namespace Raphael.Desktop.Services
{
    public class FundingSourceService : IFundingSourceService
    {
        private readonly HttpClient _httpClient;
        private readonly string EndPoint = "fundingsources";

        public FundingSourceService()
        {
            _httpClient = ApiClientFactory.Create();
        }

        public async Task<List<FundingSource>> GetFundingSourcesAsync(bool includeInactive)
        {
            // Añadimos el parámetro a la URL
            var url = $"{EndPoint}?includeInactive={includeInactive}";
            var result = await _httpClient.GetFromJsonAsync<List<FundingSource>>(url);
            return result ?? new List<FundingSource>();
        }

        public async Task<FundingSource> CreateFundingSourceAsync(FundingSourceDto dto)
        {
            var response = await _httpClient.PostAsJsonAsync(EndPoint, dto);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<FundingSource>();
        }

        public async Task<FundingSource> UpdateFundingSourceAsync(int id, FundingSourceDto dto)
        {
            var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/{id}", dto);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<FundingSource>();
        }

        public async Task DeleteFundingSourceAsync(int id)
        {
            var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");
            response.EnsureSuccessStatusCode();
        }

        public Task ExportToExcelAsync(List<FundingSource> fundingSources)
        {
            using (var workbook = new XLWorkbook())
            {
                var worksheet = workbook.Worksheets.Add("Funding Sources");
                // Crear cabeceras
                worksheet.Cell(1, 1).Value = "Name";
                worksheet.Cell(1, 2).Value = "Account Number";
                worksheet.Cell(1, 3).Value = "Address";
                worksheet.Cell(1, 4).Value = "Phone";
                worksheet.Cell(1, 5).Value = "Email";
                worksheet.Cell(1, 6).Value = "Is Active";

                // Añadir datos
                int currentRow = 2;
                foreach (var fs in fundingSources)
                {
                    worksheet.Cell(currentRow, 1).Value = fs.Name;
                    worksheet.Cell(currentRow, 2).Value = fs.AccountNumber;
                    worksheet.Cell(currentRow, 3).Value = fs.Address;
                    worksheet.Cell(currentRow, 4).Value = fs.Phone;
                    worksheet.Cell(currentRow, 5).Value = fs.Email;
                    worksheet.Cell(currentRow, 6).Value = fs.IsActive;
                    currentRow++;
                }

                // Guardar el archivo
                // Este método se llamará desde el ViewModel, que se encargará del diálogo de guardado.
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Workbook|*.xlsx",
                    Title = "Save Funding Sources Export"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    workbook.SaveAs(saveFileDialog.FileName);
                }
            }
            return Task.CompletedTask;
        }
    }
}