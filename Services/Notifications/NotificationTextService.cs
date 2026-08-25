using System.Globalization;
using System.Text;
using Raphael.Desktop.Helpers;
using Raphael.Desktop.Models;

namespace Raphael.Desktop.Services.Notifications;

/// <summary>
/// Renders a notification in the language the dispatcher picked.
/// </summary>
/// <remarks>
/// The server stores the text in English because that is what a push carries: it is
/// rendered once, on the server, and lands on a lock screen. The in-app inbox is a
/// different thing — it is read by somebody already authenticated, in the language they
/// chose — so every notification also carries a <c>MessageKey</c> and its parameters, and
/// each application renders its own text from them. A dispatcher who switches to Spanish
/// sees their whole history switch with them.
///
/// <para>
/// ⚠️ When a key is missing this falls back to the English the server sent. Never to
/// "##key##": a dispatcher reading an unfamiliar placeholder instead of "Trip 27138 was
/// cancelled" is worse off than one reading English.
/// </para>
/// </remarks>
public sealed class NotificationTextService
{
    private const string TitleSuffix = ".title";

    private const string BodySuffix = ".body";

    private const string EventPrefix = "notification.event.";

    private const string CancelledByPrefix = "notification.cancelledBy.";

    private readonly LocalizationService _localization;

    public NotificationTextService(LocalizationService? localization = null)
    {
        _localization = localization ?? LocalizationService.Instance;
    }

    /// <summary>
    /// Title and body, translated when possible, English otherwise.
    /// </summary>
    public (string Title, string Body) Resolve(NotificationDto notification)
    {
        if (notification is null)
            return (string.Empty, string.Empty);

        var messageKey = Read(notification, NotificationKeys.Metadata.MessageKey);

        if (string.IsNullOrWhiteSpace(messageKey))
            return (notification.Title, notification.Message);

        var parameters = BuildParameters(notification);

        var title = _localization.TryGetValue(messageKey + TitleSuffix, out var titleTemplate)
            ? Substitute(titleTemplate, parameters)
            : notification.Title;

        var body = _localization.TryGetValue(messageKey + BodySuffix, out var bodyTemplate)
            ? Substitute(bodyTemplate, parameters)
            : notification.Message;

        return (title, body);
    }

    /// <summary>
    /// Human name of the business event, for the Type column and the tab headers.
    /// </summary>
    public string ResolveEventName(string businessEventCode)
    {
        if (string.IsNullOrWhiteSpace(businessEventCode))
            return string.Empty;

        return _localization.TryGetValue(EventPrefix + businessEventCode, out var name)
            ? name
            : businessEventCode;
    }

    /// <summary>
    /// Who cancelled, in words. Falls back to the raw code so an origin added on the
    /// server without a translation here still says something.
    /// </summary>
    public string ResolveCancelledBy(string cancelledByCode)
    {
        if (string.IsNullOrWhiteSpace(cancelledByCode))
            return string.Empty;

        return _localization.TryGetValue(CancelledByPrefix + cancelledByCode, out var text)
            ? text
            : cancelledByCode;
    }

    /// <summary>
    /// The parameters a template can use, already formatted for the active culture.
    /// </summary>
    private Dictionary<string, string> BuildParameters(NotificationDto notification)
    {
        var culture = CurrentCulture();

        var parameters = new Dictionary<string, string>(
            notification.Metadata,
            StringComparer.OrdinalIgnoreCase);

        // The server writes dates as yyyy-MM-dd and times as HH:mm, unambiguous on the
        // wire. Neither is how anybody reads a date, so both are reformatted here.
        if (parameters.TryGetValue(NotificationKeys.Metadata.TripDate, out var rawDate) &&
            DateTime.TryParseExact(
                rawDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            parameters[NotificationKeys.Metadata.TripDate] = date.ToString("d", culture);
        }

        if (parameters.TryGetValue(NotificationKeys.Metadata.TripTime, out var rawTime) &&
            TimeSpan.TryParseExact(
                rawTime,
                @"hh\:mm",
                CultureInfo.InvariantCulture,
                out var time))
        {
            parameters[NotificationKeys.Metadata.TripTime] =
                DateTime.Today.Add(time).ToString("t", culture);
        }

        if (parameters.TryGetValue(NotificationKeys.Metadata.CancelledBy, out var cancelledBy))
            parameters[NotificationKeys.Metadata.CancelledBy] = ResolveCancelledBy(cancelledBy);

        // The Will Call deadline travels in UTC. Showing it as such would tell a dispatcher
        // in Miami that the vehicle is due four hours after it really is.
        if (parameters.TryGetValue(
                NotificationKeys.Metadata.WillCallDeadlineUtc,
                out var rawDeadline) &&
            TryParseUtc(rawDeadline, out var deadline))
        {
            parameters[NotificationKeys.Metadata.WillCallDeadlineUtc] =
                deadline.ToLocalTime().ToString("t", culture);
        }

        return parameters;
    }

    /// <summary>
    /// Replaces every <c>{Name}</c> with its parameter. A placeholder with no parameter is
    /// left as it is, so a template that outran the payload is visible instead of silently
    /// rendering a gap in the middle of a sentence.
    /// </summary>
    private static string Substitute(
        string template,
        IReadOnlyDictionary<string, string> parameters)
    {
        if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0)
            return template;

        var result = new StringBuilder(template.Length);

        var index = 0;

        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);

            if (open < 0)
            {
                result.Append(template, index, template.Length - index);
                break;
            }

            var close = template.IndexOf('}', open + 1);

            if (close < 0)
            {
                result.Append(template, index, template.Length - index);
                break;
            }

            result.Append(template, index, open - index);

            var name = template.Substring(open + 1, close - open - 1);

            result.Append(
                parameters.TryGetValue(name, out var value)
                    ? value
                    : template.Substring(open, close - open + 1));

            index = close + 1;
        }

        return result.ToString();
    }

    public static bool TryParseUtc(string value, out DateTime utc)
    {
        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            utc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            return true;
        }

        utc = default;
        return false;
    }

    private static string? Read(NotificationDto notification, string key)
    {
        return notification.Metadata.TryGetValue(key, out var value)
            ? value
            : null;
    }

    private CultureInfo CurrentCulture()
    {
        try
        {
            return CultureInfo.GetCultureInfo(
                _localization.CurrentLanguage ?? "en");
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.CurrentCulture;
        }
    }
}
