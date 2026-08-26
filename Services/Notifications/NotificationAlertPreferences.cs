using System.IO;
using System.Media;
using System.Text.Json;
using System.Text.Json.Serialization;
using Raphael.Desktop.Helpers;

namespace Raphael.Desktop.Services.Notifications;

/// <summary>
/// What this dispatcher wants the live alerts to do, kept on this machine.
/// </summary>
/// <remarks>
/// Same folder and same one-file-per-user rule as
/// <see cref="LocalNotificationReadStateStore"/>: several dispatchers share a workstation
/// across shifts, and the night shift must not inherit the day shift's choices.
///
/// <para>
/// Local and not on the server, for the same reason the read state is local: it is a
/// preference about this screen, not a fact about the notification, and putting it on the
/// server would mean a row per dispatcher and a migration.
/// </para>
/// </remarks>
public sealed class NotificationAlertPreferences
{
    private const string FilePrefix = "alerts-";

    [JsonIgnore]
    private string _filePath;

    /// <summary>
    /// A patient is waiting and an hour is running. On by default; the other two are not.
    /// </summary>
    public bool SoundOnActionRequired { get; set; } = true;

    public bool SoundOnAttention { get; set; }

    public bool SoundOnAmbient { get; set; }

    /// <summary>
    /// Alerts stay off the screen until this moment.
    /// </summary>
    /// <remarks>
    /// For a long call or a bulk import. Everything still lands in the bell — this only
    /// stops the card from appearing. Without a way to ask for quiet, the natural response
    /// is to ignore that corner of the screen for good, and then the Will Call is missed
    /// too.
    /// </remarks>
    public DateTime? MutedUntilUtc { get; set; }

    [JsonIgnore]
    public bool IsMuted =>
        MutedUntilUtc.HasValue && MutedUntilUtc.Value > DateTime.UtcNow;

    public bool ShouldSound(NotificationAlertLevel level)
    {
        if (IsMuted)
            return false;

        return level switch
        {
            NotificationAlertLevel.ActionRequired => SoundOnActionRequired,
            NotificationAlertLevel.Attention => SoundOnAttention,
            _ => SoundOnAmbient
        };
    }

    /// <summary>
    /// The system sounds, so nothing is added to the repository and the office volume
    /// control keeps working.
    /// </summary>
    public static void Play(NotificationAlertLevel level)
    {
        try
        {
            if (level == NotificationAlertLevel.ActionRequired)
                SystemSounds.Exclamation.Play();
            else
                SystemSounds.Asterisk.Play();
        }
        catch
        {
            // A machine with no sound device must not take the alert down with it.
        }
    }

    public void MuteFor(TimeSpan span)
    {
        MutedUntilUtc = DateTime.UtcNow.Add(span);
        Save();
    }

    public void Unmute()
    {
        MutedUntilUtc = null;
        Save();
    }

    public static NotificationAlertPreferences Load()
    {
        var path = ResolvePath();

        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<NotificationAlertPreferences>(
                    File.ReadAllText(path));

                if (loaded is not null)
                {
                    loaded._filePath = path;
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            // A corrupt file costs the defaults. It must never stop the alerts.
            System.Diagnostics.Debug.WriteLine(
                $"Could not load alert preferences: {ex.Message}");
        }

        return new NotificationAlertPreferences { _filePath = path };
    }

    public void Save()
    {
        try
        {
            _filePath ??= ResolvePath();

            File.WriteAllText(
                _filePath,
                JsonSerializer.Serialize(this));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Could not save alert preferences: {ex.Message}");
        }
    }

    private static string ResolvePath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "RapphaelApp",
            "Notifications");

        Directory.CreateDirectory(folder);

        var userId = string.IsNullOrWhiteSpace(SessionManager.UserId)
            ? "anonymous"
            : SessionManager.UserId;

        return Path.Combine(folder, $"{FilePrefix}{userId}.json");
    }
}
