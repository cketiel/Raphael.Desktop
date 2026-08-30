using Raphael.Desktop.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Raphael.Desktop.Services
{
    /// <summary>
    /// Reads the Google Maps consumption figures, and reports what this machine spent itself.
    /// </summary>
    public interface IMapsUsageApiService
    {
        /// <summary>Headline figures and per-product split for a billing period.</summary>
        Task<MapsUsageSummaryDto> GetSummaryAsync(DateTime from, DateTime to);

        /// <summary>One point per day and product, for the charts.</summary>
        Task<List<MapsUsagePointDto>> GetDailyAsync(DateTime from, DateTime to);

        /// <summary>Everything ever counted, with no date filter.</summary>
        Task<MapsUsageTotalsDto> GetTotalsAsync();

        /// <summary>Google's volume bands as configured on the server.</summary>
        Task<List<MapsPricingTierDto>> GetPricingAsync();

        /// <summary>Every setting the server holds.</summary>
        Task<List<SystemSettingDto>> GetSettingsAsync();

        /// <summary>
        /// Changes one setting. Live everywhere within a minute, with no deployment.
        /// </summary>
        /// <returns>The error the server gave, or null when it was saved.</returns>
        Task<string> SetSettingAsync(string key, string value);

        /// <summary>
        /// Tells the server about Google calls this machine's map pages made on their own.
        /// </summary>
        /// <remarks>
        /// Fire and forget: the panel being slightly behind is worth nothing next to a dispatcher
        /// waiting. Failures are swallowed.
        /// </remarks>
        Task ReportUsageAsync(string sku, int count = 1);
    }
}
