using System.Globalization;
using Raphael.Desktop.Services;

namespace Raphael.Desktop.ViewModels.CallRequests;

/// <summary>How a call request reads on screen, in the dispatcher's language and clock.</summary>
/// <remarks>
/// Two kinds of time, as everywhere in Raphael. When something happened is an instant, stored in
/// UTC and shown on the dispatcher's clock. A stop's schedule or ETA is business wall-clock time
/// and is shown as it is, never converted.
/// </remarks>
public static class CallRequestText
{
    private static LocalizationService L => LocalizationService.Instance;

    public static CultureInfo Culture
    {
        get
        {
            try
            {
                return CultureInfo.GetCultureInfo(L.CurrentLanguage ?? "en");
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.CurrentCulture;
            }
        }
    }

    public static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public static DateTime Local(DateTime utc) => Utc(utc).ToLocalTime();

    /// <summary>"10:32:14". The seconds are there on purpose: the office works the queue in order of arrival.</summary>
    public static string ExactTime(DateTime utc) => Local(utc).ToString("T", Culture);

    public static string ShortTime(DateTime? utc) =>
        utc.HasValue ? Local(utc.Value).ToString("t", Culture) : string.Empty;

    /// <summary>A stop's scheduled time or ETA: business wall-clock time, shown as it is.</summary>
    public static string WallClock(TimeSpan? time) =>
        time.HasValue ? DateTime.Today.Add(time.Value).ToString("t", Culture) : "—";

    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.FromMinutes(1))
            return L["CallRequestLessThanAMinute"];

        if (span.TotalHours >= 1)
            return string.Format(L["CallRequestHoursMinutes"], (int)span.TotalHours, span.Minutes);

        return string.Format(L["CallRequestMinutes"], (int)span.TotalMinutes);
    }

    public static string Ago(DateTime? utc) =>
        utc.HasValue
            ? string.Format(L["CallRequestAgo"], Duration(DateTime.UtcNow - Utc(utc.Value)))
            : string.Empty;

    public static string Reason(string? code) => Lookup("CallRequestReason_", code);

    public static string Change(string? type) => Lookup("CallRequestChange_", type);

    public static string StopKind(string? kind) => Lookup("CallRequestStop_", kind);

    /// <summary>A translated label, or the raw code when this build does not know it yet.</summary>
    private static string Lookup(string prefix, string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        return L.TryGetValue(prefix + code, out var text) ? text : code;
    }
}
