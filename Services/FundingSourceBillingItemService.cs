
using System.Net.Http;
using System.Net.Http.Json;
using ClosedXML.Excel;
using Raphael.Desktop.DTOs;
using Raphael.Desktop.Models;
using Raphael.Desktop.Services;


public class FundingSourceBillingItemService : IFundingSourceBillingItemService
{
    private readonly HttpClient _httpClient;
    private readonly string EndPoint = "FundingSourceBillingItem";

    public FundingSourceBillingItemService()
    {
        _httpClient = ApiClientFactory.Create();
    }

    public async Task<List<FundingSourceBillingItemGetDto>> GetAllAsync()
    {
        var result = await _httpClient.GetFromJsonAsync<List<FundingSourceBillingItemGetDto>>(EndPoint);
        return result ?? new List<FundingSourceBillingItemGetDto>();
    }

    public async Task<List<FundingSourceBillingItemGetDto>> GetByFundingSourceIdAsync(int fundingSourceId, bool includeExpired)
    {
        var url = $"{EndPoint}/ByFundingSource/{fundingSourceId}?includeExpired={includeExpired}";
        var result = await _httpClient.GetFromJsonAsync<List<FundingSourceBillingItemGetDto>>(url);
        return result ?? new List<FundingSourceBillingItemGetDto>();
    }

    public async Task<FundingSourceBillingItem> CreateAsync(FundingSourceBillingItemDto dto)
    {
        var response = await _httpClient.PostAsJsonAsync(EndPoint, dto);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<FundingSourceBillingItem>();
    }

    public async Task UpdateAsync(int id, FundingSourceBillingItemDto dto)
    {
        var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/{id}", dto);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(int id)
    {
        var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");
        response.EnsureSuccessStatusCode();
    }

    public Task ExportToExcelAsync(List<FundingSourceBillingItem> items)
    {
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.Worksheets.Add("Billing Items");
            // Define tus cabeceras
            worksheet.Cell(1, 1).Value = "Billing Item";
            worksheet.Cell(1, 2).Value = "Space Type";
            worksheet.Cell(1, 3).Value = "Rate";
            worksheet.Cell(1, 4).Value = "Per";
            worksheet.Cell(1, 5).Value = "From";
            worksheet.Cell(1, 6).Value = "To";
            // ... añade más si lo necesitas

            int currentRow = 2;
            foreach (var item in items)
            {
                worksheet.Cell(currentRow, 1).Value = item.BillingItem?.Description;
                worksheet.Cell(currentRow, 2).Value = item.SpaceType?.Name;
                worksheet.Cell(currentRow, 3).Value = item.Rate;
                worksheet.Cell(currentRow, 4).Value = item.Per;
                worksheet.Cell(currentRow, 5).Value = item.FromDate.ToShortDateString();
                worksheet.Cell(currentRow, 6).Value = item.ToDate.ToShortDateString();
                currentRow++;
            }

            var saveFileDialog = new Microsoft.Win32.SaveFileDialog { Filter = "Excel Workbook|*.xlsx" };
            if (saveFileDialog.ShowDialog() == true)
            {
                workbook.SaveAs(saveFileDialog.FileName);
            }
        }
        return Task.CompletedTask;
    }
}