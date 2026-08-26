namespace Raphael.Desktop.Services.Notifications;

/// <summary>
/// What this dispatcher has already read.
/// </summary>
/// <remarks>
/// An office notice is stored once on the server and read by the whole office, so the
/// server has nowhere to record that <i>this</i> dispatcher opened it: marking the shared
/// row as viewed would clear the unread mark for all their colleagues. Read state is
/// therefore personal and lives on the client.
///
/// <para>
/// Archiving is <b>not</b> here, and that is the point. In Raphael archiving does not mean
/// "take it off my list" the way a mail client means it: it means the record is kept and
/// the cleanup will never delete it. That is a decision about the record itself, it holds
/// for the whole system, and it goes to the server.
/// </para>
///
/// <para>
/// Being an interface is the point. Client-side read state costs no schema and no
/// migration, and the price is that it does not follow a dispatcher to another machine.
/// When there is a test server to migrate against, a server backed implementation replaces
/// this one without a ViewModel changing.
/// </para>
/// </remarks>
public interface INotificationReadStateStore
{
    bool IsRead(Guid notificationId);

    void SetRead(Guid notificationId, bool isRead);

    void SetRead(IEnumerable<Guid> notificationIds, bool isRead);

    /// <summary>
    /// Drops what is no longer in any inbox, so the file cannot grow without bound.
    /// </summary>
    void Prune(IEnumerable<Guid> notificationIdsStillVisible);
}
