using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Raphael.Desktop.DTOs;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// The compliance documents of drivers, vehicles and the dispatcher's own (RE-065), and the
    /// admin's review. Files come and go only through the API; a file opened to look at is kept in
    /// a temporary folder only until the next one is opened.
    /// </summary>
    public class ComplianceService
    {
        private const string EndPoint = "compliance"; // ApiClientFactory already adds "api/"
        private readonly HttpClient _httpClient = ApiClientFactory.Create();

        public Task<List<ComplianceSummaryDto>> GetOverviewAsync(string subject) =>
            GetAsync<List<ComplianceSummaryDto>>($"{EndPoint}/overview/{subject}");

        public Task<ComplianceSubjectDto> GetSubjectAsync(string subject, int subjectId) =>
            GetAsync<ComplianceSubjectDto>($"{EndPoint}/subjects/{subject}/{subjectId}");

        public Task<List<ComplianceDocumentDto>> GetPendingAsync() =>
            GetAsync<List<ComplianceDocumentDto>>($"{EndPoint}/pending");

        public Task<List<ComplianceDocumentDto>> GetExpiringAsync(int days) =>
            GetAsync<List<ComplianceDocumentDto>>($"{EndPoint}/expiring?days={days}");

        /// <summary>Creates the document, then uploads each file. Its id when done, else the reason.</summary>
        public async Task<(int Id, string Error)> UploadAsync(ComplianceDocumentCreateDto document, IReadOnlyList<(string Slot, string Path)> files, IProgress<int> progress = null)
        {
            var created = await _httpClient.PostAsJsonAsync($"{EndPoint}/documents", document);
            if (!created.IsSuccessStatusCode) return (0, await ErrorOfAsync(created));
            var doc = await created.Content.ReadFromJsonAsync<ComplianceDocumentDto>();
            var error = await PutFilesAsync(doc.Id, files, progress);
            return (doc.Id, error);
        }

        /// <summary>Uploads or replaces files of a document still waiting. Null when done, else the reason.</summary>
        public async Task<string> PutFilesAsync(int documentId, IReadOnlyList<(string Slot, string Path)> files, IProgress<int> progress = null)
        {
            var sent = 0;
            foreach (var (slot, path) in files)
            {
                progress?.Report(sent++);
                using var body = new ByteArrayContent(await File.ReadAllBytesAsync(path));
                body.Headers.ContentType = new MediaTypeHeaderValue(ContentTypeOf(path));
                var put = await _httpClient.PutAsync($"{EndPoint}/documents/{documentId}/files/{slot}", body);
                if (!put.IsSuccessStatusCode) return await ErrorOfAsync(put);
            }
            return null;
        }

        /// <summary>
        /// One document's files at a time on this computer: the previous ones go when another is opened.
        /// Called once per document, before its files come down.
        /// </summary>
        public static void ClearDownloads()
        {
            var folder = Path.Combine(Path.GetTempPath(), "raphael-compliance");
            foreach (var f in Directory.Exists(folder) ? Directory.GetFiles(folder) : [])
            {
                try { File.Delete(f); } catch (IOException) { /* still open in a viewer */ } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>Corrects the expiry of a document still waiting. Null when done, else the reason.</summary>
        public async Task<string> UpdateExpiryAsync(int documentId, DateTime expiresOn)
        {
            var response = await _httpClient.PutAsJsonAsync($"{EndPoint}/documents/{documentId}/expiry", new { expiresOn = DateTime.SpecifyKind(expiresOn.Date, DateTimeKind.Unspecified) });
            return response.IsSuccessStatusCode ? null : await ErrorOfAsync(response);
        }

        /// <summary>Every upload of one document type of a subject, newest first.</summary>
        public Task<List<ComplianceDocumentDto>> GetHistoryAsync(string subject, int subjectId, int documentTypeId) =>
            GetAsync<List<ComplianceDocumentDto>>($"{EndPoint}/subjects/{subject}/{subjectId}/history/{documentTypeId}");

        /// <summary>Downloads a file to the temporary folder and returns its path, to open it with the system viewer.</summary>
        public async Task<string> DownloadAsync(int documentId, string slot, string contentType)
        {
            var response = await _httpClient.GetAsync($"{EndPoint}/documents/{documentId}/files/{slot}");
            if (!response.IsSuccessStatusCode) throw new Exception(await ErrorOfAsync(response));

            var folder = Path.Combine(Path.GetTempPath(), "raphael-compliance");
            Directory.CreateDirectory(folder);
            var extension = contentType == "application/pdf" ? "pdf" : contentType == "image/png" ? "png" : "jpg";
            var path = Path.Combine(folder, $"document-{documentId}-{slot.ToLowerInvariant()}.{extension}");
            await File.WriteAllBytesAsync(path, await response.Content.ReadAsByteArrayAsync());
            return path;
        }

        /// <summary>Null when done, else the reason.</summary>
        public async Task<string> ApproveAsync(int documentId)
        {
            var response = await _httpClient.PostAsync($"{EndPoint}/documents/{documentId}/approve", null);
            return response.IsSuccessStatusCode ? null : await ErrorOfAsync(response);
        }

        public async Task<string> RejectAsync(int documentId, string reason)
        {
            var response = await _httpClient.PostAsJsonAsync($"{EndPoint}/documents/{documentId}/reject", new ComplianceRejectDto { Reason = reason });
            return response.IsSuccessStatusCode ? null : await ErrorOfAsync(response);
        }

        /// <summary>How many drivers it reached, or the reason it did not go.</summary>
        public async Task<(int Sent, string Error)> SendNoticeAsync(List<int> driverIds, string message)
        {
            var response = await _httpClient.PostAsJsonAsync($"{EndPoint}/notices", new ComplianceNoticeDto { DriverIds = driverIds, Message = message });
            if (!response.IsSuccessStatusCode) return (0, await ErrorOfAsync(response));
            var result = await response.Content.ReadFromJsonAsync<ComplianceNoticeResultDto>();
            return (result?.Sent ?? 0, null);
        }

        public static string ContentTypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            _ => "image/jpeg"
        };

        private async Task<T> GetAsync<T>(string url)
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) throw new Exception(await ErrorOfAsync(response));
            return await response.Content.ReadFromJsonAsync<T>();
        }

        private static async Task<string> ErrorOfAsync(HttpResponseMessage response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return LocalizationService.Instance["compliance.Forbidden"];
            try
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (doc.RootElement.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } message) return message;
            }
            catch (JsonException)
            {
            }
            return response.ReasonPhrase ?? "Error";
        }
    }
}
