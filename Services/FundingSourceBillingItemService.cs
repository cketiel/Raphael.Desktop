
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
        await EnsureAsync(response);
        return await response.Content.ReadFromJsonAsync<FundingSourceBillingItem>();
    }

    public async Task UpdateAsync(int id, FundingSourceBillingItemDto dto)
    {
        var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/{id}", dto);
        await EnsureAsync(response);
    }

    /// <summary>
    /// Removes a rate. The API erases it only if it never priced a trip; otherwise it retires it
    /// (To Date = yesterday). Returns "deleted" or "retired" (RE-072).
    /// </summary>
    public async Task<string> RemoveAsync(int id)
    {
        var response = await _httpClient.DeleteAsync($"{EndPoint}/{id}");
        await EnsureAsync(response);
        var body = await response.Content.ReadFromJsonAsync<OutcomeDto>();
        return body?.Outcome ?? "deleted";
    }

    public async Task NewPeriodAsync(int id, FundingSourceBillingItemDto dto)
    {
        var response = await _httpClient.PostAsJsonAsync($"{EndPoint}/{id}/new-period", dto);
        await EnsureAsync(response);
    }

    public async Task RenewAsync(int id)
    {
        var response = await _httpClient.PostAsync($"{EndPoint}/{id}/renew", null);
        await EnsureAsync(response);
    }

    public async Task<BillingRatesCopyResultDto> CopyAsync(BillingRatesCopyRequestDto request)
    {
        var response = await _httpClient.PostAsJsonAsync($"{EndPoint}/copy", request);
        await EnsureAsync(response);
        return await response.Content.ReadFromJsonAsync<BillingRatesCopyResultDto>() ?? new BillingRatesCopyResultDto();
    }

    public async Task<BillingSimulationResultDto> SimulateAsync(BillingSimulationRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync($"{EndPoint}/simulate", request, cancellationToken);
        await EnsureAsync(response);
        return await response.Content.ReadFromJsonAsync<BillingSimulationResultDto>(cancellationToken: cancellationToken) ?? new BillingSimulationResultDto();
    }

    public async Task<List<BillingRateExpiringDto>> GetExpiringAsync(int months = 6) =>
        await _httpClient.GetFromJsonAsync<List<BillingRateExpiringDto>>($"{EndPoint}/expiring?months={months}") ?? new();

    public async Task<List<BillingRatesHealthDto>> GetHealthAsync() =>
        await _httpClient.GetFromJsonAsync<List<BillingRatesHealthDto>>($"{EndPoint}/health") ?? new();

    /// <summary>
    /// Throws with the API's own explanation when it refused the request: a billing rule the API
    /// rejects says why (overlap, history, dates), and that is what the user needs to read.
    /// </summary>
    private static async Task EnsureAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        string? message = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDto>();
            message = problem?.Problems is { Count: > 1 }
                ? string.Join(Environment.NewLine + Environment.NewLine, problem.Problems.Select(p => p.Message))
                : problem?.Message;
        }
        catch
        {
            // Not the API's problem shape: fall back to the status code below.
        }

        throw new BillingRuleApiException(message ?? $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    private sealed class OutcomeDto { public string? Outcome { get; set; } }
    private sealed class ProblemDto
    {
        public string? Code { get; set; }
        public string? Message { get; set; }
        public List<ProblemItemDto>? Problems { get; set; }
    }
    private sealed class ProblemItemDto { public string? Code { get; set; } public string Message { get; set; } = ""; }

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

/// <summary>The API refused a billing change; <see cref="Exception.Message"/> is its explanation, ready to show.</summary>
public sealed class BillingRuleApiException : Exception
{
    public BillingRuleApiException(string message) : base(message) { }
}
