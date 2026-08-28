namespace Raphael.Desktop.Helpers;

/// <summary>
/// The help topic ids this application knows by name.
/// </summary>
/// <remarks>
/// ⚠️ These strings are a contract, not labels. The same id is written into the help corpus
/// (<c>help/topics/</c> in the ecosystem repository), travels in the Portal's deep links, and ends
/// up quoted in support emails. It is never renamed: a retired id gets an entry in
/// <c>help/redirects.json</c> instead.
///
/// <para>
/// Renaming one here without redirecting it there means F1 opens a blank page on the machine of a
/// dispatcher who has not updated yet — which is the worst possible moment for the help to fail.
/// See <c>_meta/HELP_POLICY.md</c> section 6.
/// </para>
/// </remarks>
public static class HelpTopics
{
    /// <summary>Where F1 lands when nothing more specific is declared anywhere.</summary>
    public const string Home = "desktop/getting-started/overview";

    public const string UsingHelp = "desktop/getting-started/help-itself";

    public const string Notifications = "desktop/notifications/overview";
    public const string NotificationCenter = "desktop/notifications/center";
    public const string NotificationAlerts = "desktop/notifications/live-alerts";
    public const string NotificationAlertPreferences = "desktop/notifications/alert-preferences";
    public const string NotificationWillCall = "desktop/notifications/will-call";
    public const string NotificationAdmin = "desktop/notifications/admin";
    public const string NotificationTroubleshooting = "desktop/notifications/troubleshooting";

    /// <summary>
    /// The tab a topic answers for, when nothing deeper is declared.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="MENU"/>. The help build checks the other side of this table
    /// (<c>help/apps.json</c>) and fails when a tab has no topic, so a tab added to the application
    /// cannot quietly ship without somewhere for F1 to land.
    /// </remarks>
    public static string ForMenu(MENU menu) => menu switch
    {
        MENU.Home => "desktop/home/overview",
        MENU.Data => "desktop/data/overview",
        MENU.Schedules => "desktop/schedules/overview",
        MENU.Dispatch => "desktop/dispatch/overview",
        MENU.Reports => "desktop/reports/overview",
        MENU.Admin => "desktop/admin/overview",
        MENU.Notification => Notifications,
        _ => Home
    };
}
