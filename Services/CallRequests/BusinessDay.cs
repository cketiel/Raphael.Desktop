using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Services.CallRequests;

/// <summary>
/// Which day it is for the business, not for this computer.
/// </summary>
/// <remarks>
/// The server stamps each call request with the day it belongs to in the zone where trips are
/// operated (IOperationClock). This machine may be somewhere else: a request made after midnight
/// in New York is "tomorrow" for a clock an hour or two behind, and a queue that compared with
/// DateTime.Today exported nothing and called that day's requests "from another day".
///
/// <para>
/// Resolved the way the server does it, so both halves agree (TIME_POLICY): the signed-in user's
/// provider, or the broker when they have none; that provider's zone; and when it has none, the
/// default the server is configured with. ⚠️ The last two numbers mirror the server's
/// <c>Operations:BrokerProviderId</c> and <c>Operations:DefaultTimeZone</c>; if those ever change
/// there, they change here too.
/// </para>
/// </remarks>
public static class BusinessDay
{
    /// <summary>Mirrors <c>Operations:DefaultTimeZone</c> on the server.</summary>
    public const string DefaultZoneId = "America/New_York";

    /// <summary>Mirrors <c>Operations:BrokerProviderId</c> on the server.</summary>
    public const int BrokerProviderId = 1;

    private static TimeZoneInfo _zone = Find(DefaultZoneId) ?? TimeZoneInfo.Local;

    /// <summary>Today, where the trips are operated.</summary>
    public static DateTime Today => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _zone).Date;

    /// <summary>
    /// Reads the provider's zone. Until it has answered, the server's default is used, which is
    /// the right answer for the broker's own dispatchers.
    /// </summary>
    public static async Task LoadAsync()
    {
        try
        {
            var providerId = SessionManager.ProviderId ?? BrokerProviderId;
            var providers = await new ProviderService().GetProvidersAsync();
            var declared = providers.FirstOrDefault(p => p.Id == providerId)?.TimeZoneId;

            if (!string.IsNullOrWhiteSpace(declared) && Find(declared) is { } zone)
                _zone = zone;
        }
        catch (Exception ex)
        {
            // The default zone stays. An hour of error around midnight, not a broken queue.
            FileLogger.Log($"Call requests: could not read the provider's time zone. {ex.Message}");
        }
    }

    private static TimeZoneInfo? Find(string id)
    {
        try
        {
            // IANA ids ("America/New_York") resolve on Windows through ICU, as they do on the server.
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch
        {
            return null;
        }
    }
}
