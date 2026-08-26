using System.Net.Http;
using System.Net.Http.Json;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services.Notifications;

/// <summary>
/// Result of one cleanup pass.
/// </summary>
/// <param name="Expired">Rows that stopped being served.</param>
/// <param name="Deleted">Rows that are gone for good, with their children.</param>
public sealed record RetentionRunResult(int Expired, int Deleted);

/// <summary>
/// The administration side of the notification system.
/// </summary>
/// <remarks>
/// ⚠️ Every endpoint here is guarded by <c>[Authorize(Roles = "1")]</c> on the server.
/// The panel hides this area for anybody else, but the hiding is a courtesy: the
/// authorisation is what actually stops it.
/// </remarks>
public sealed class NotificationAdminApiClient
{
    private const string CatalogEndpoint = "admin/notification/catalog";

    private const string AdminEndpoint = "admin/notification";

    private readonly HttpClient _httpClient;

    public NotificationAdminApiClient()
    {
        _httpClient = ApiClientFactory.Create();
    }

    /// <summary>
    /// Runs the cleanup now instead of waiting for the background pass.
    /// </summary>
    /// <remarks>
    /// ⚠️ Expiring is reversible — the row stays and stops being served. Deleting is not.
    /// What this deletes is what has been expired long enough that nobody could legitimately
    /// want it: seven days for office and driver notices, thirty for patients and
    /// integrations. See <c>_meta/NOTIFICATIONS_RETENTION.md</c>.
    /// </remarks>
    public async Task<RetentionRunResult> RunRetentionAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsync(
            $"{CatalogEndpoint}/retention/run",
            content: null,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<RetentionRunResult>(cancellationToken);

        return result ?? new RetentionRunResult(0, 0);
    }

    /// <summary>
    /// Everything archived, grouped by the application it was addressed to.
    /// </summary>
    /// <remarks>
    /// Archived rows are the only ones the cleanup never removes, so this is the one list
    /// in the system that grows without a ceiling. Somebody has to be able to look at it.
    /// </remarks>
    public async Task<ArchivedNotificationsDto> GetArchivedAsync(
        CancellationToken cancellationToken = default)
    {
        var archived = await _httpClient.GetFromJsonAsync<ArchivedNotificationsDto>(
            $"{AdminEndpoint}/archived",
            cancellationToken);

        return archived ?? new ArchivedNotificationsDto();
    }

    /// <summary>
    /// Deletes one archived notification for good.
    /// </summary>
    /// <remarks>⚠️ Not reversible. Recorded against the name of whoever ran it.</remarks>
    public async Task<bool> DeleteArchivedAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync(
            $"{AdminEndpoint}/archived/{notificationId}",
            cancellationToken);

        return response.IsSuccessStatusCode;
    }

    /// <summary>Deletes every archived notification. Returns how many went.</summary>
    /// <remarks>⚠️ Not reversible. Recorded against the name of whoever ran it.</remarks>
    public async Task<int> DeleteAllArchivedAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.DeleteAsync(
            $"{AdminEndpoint}/archived",
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<DeleteArchivedResult>(cancellationToken);

        return result?.Deleted ?? 0;
    }

    private sealed record DeleteArchivedResult(int Deleted);

    /// <summary>
    /// Who archived, purged or deleted, newest first.
    /// </summary>
    public async Task<IReadOnlyList<NotificationAdminAuditDto>> GetAuditAsync(
        CancellationToken cancellationToken = default)
    {
        var audit = await _httpClient.GetFromJsonAsync<List<NotificationAdminAuditDto>>(
            $"{AdminEndpoint}/audit",
            cancellationToken);

        return audit ?? [];
    }

    /// <summary>
    /// Every notification rule, so the panel can show which notices are switched on.
    /// </summary>
    /// <remarks>
    /// One event has one rule per audience, so the same notice appears several times. The
    /// panel groups them by event, because that is the granularity the switch works at.
    /// </remarks>
    public async Task<IReadOnlyList<NotificationRuleSummaryDto>> GetRulesAsync(
        CancellationToken cancellationToken = default)
    {
        var rules = await _httpClient.GetFromJsonAsync<List<NotificationRuleSummaryDto>>(
            $"{CatalogEndpoint}/rules",
            cancellationToken);

        return rules ?? [];
    }

    /// <summary>
    /// Silences or restores one business event across every application it reaches.
    /// </summary>
    /// <remarks>
    /// The emergency stop for a notice that turns out to be wrong or too noisy, without
    /// deploying anything.
    ///
    /// <para>
    /// ⚠️ It does not survive a catalog synchronisation: re-seeding the rules imposes what
    /// the catalog declares, including whether they are active. A silence meant to last has
    /// to be written into the catalog too.
    /// </para>
    /// </remarks>
    public async Task<bool> SetEventActiveAsync(
        string businessEventCode,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PatchAsync(
            $"{CatalogEndpoint}/events/{businessEventCode}/active?isActive={isActive}",
            content: null,
            cancellationToken);

        return response.IsSuccessStatusCode;
    }
}
