using System.IO;
using System.Text.Json;
using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Services.Notifications;

/// <summary>
/// Read state kept in a small file under the Windows profile.
/// </summary>
/// <remarks>
/// Same folder <see cref="UserConfigService"/> already uses for the grid layout.
///
/// <para>
/// One file per <c>UserId</c>, not one per machine: several dispatchers share a
/// workstation across shifts, and the night shift must not inherit the day shift's marks.
/// </para>
///
/// <para>
/// Reads and writes are guarded because notifications arrive on the SignalR thread while
/// the panel is being clicked on the UI thread.
/// </para>
/// </remarks>
public sealed class LocalNotificationReadStateStore : INotificationReadStateStore
{
    private readonly object _gate = new();

    private readonly string _filePath;

    private readonly HashSet<Guid> _read = [];

    public LocalNotificationReadStateStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "RapphaelApp",
            "Notifications");

        Directory.CreateDirectory(folder);

        // Anonymous only if somebody built the store before signing in, which the panel
        // never does. Keeps the path valid instead of throwing.
        var userId = string.IsNullOrWhiteSpace(SessionManager.UserId)
            ? "anonymous"
            : SessionManager.UserId;

        _filePath = Path.Combine(folder, $"read-{userId}.json");

        Load();
    }

    public bool IsRead(Guid notificationId)
    {
        lock (_gate)
        {
            return _read.Contains(notificationId);
        }
    }

    public void SetRead(Guid notificationId, bool isRead)
    {
        if (Apply(notificationId, isRead))
            Save();
    }

    public void SetRead(IEnumerable<Guid> notificationIds, bool isRead)
    {
        if (notificationIds is null)
            return;

        var changed = false;

        foreach (var id in notificationIds)
            changed |= Apply(id, isRead);

        if (changed)
            Save();
    }

    public void Prune(IEnumerable<Guid> notificationIdsStillVisible)
    {
        if (notificationIdsStillVisible is null)
            return;

        var visible = new HashSet<Guid>(notificationIdsStillVisible);

        bool changed;

        lock (_gate)
        {
            // Nothing outside the inbox can ever be shown again: an office notice stops
            // being served twelve hours after it was created. Keeping its id would make
            // this file grow for the life of the installation.
            changed = _read.RemoveWhere(id => !visible.Contains(id)) > 0;
        }

        if (changed)
            Save();
    }

    private bool Apply(Guid id, bool isRead)
    {
        lock (_gate)
        {
            return isRead
                ? _read.Add(id)
                : _read.Remove(id);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;

            var ids = JsonSerializer.Deserialize<List<Guid>>(
                File.ReadAllText(_filePath));

            if (ids is null)
                return;

            lock (_gate)
            {
                foreach (var id in ids)
                    _read.Add(id);
            }
        }
        catch (Exception ex)
        {
            // A corrupt file costs a few notifications shown in bold again. It must never
            // stop the panel from opening.
            System.Diagnostics.Debug.WriteLine(
                $"Could not load notification read state: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            List<Guid> snapshot;

            lock (_gate)
            {
                snapshot = [.. _read];
            }

            File.WriteAllText(
                _filePath,
                JsonSerializer.Serialize(snapshot));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Could not save notification read state: {ex.Message}");
        }
    }
}
