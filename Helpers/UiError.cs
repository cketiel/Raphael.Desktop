using System.Windows;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// Shows an error to the user, unless the session has just ended — in which case there is
    /// nothing useful to say and it stays quiet.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ Why this exists. When a session ends, every call in flight fails in the same second,
    /// and each one used to raise its own box. A dispatcher was told "your session has ended,
    /// please sign in again" and then, on top of it, "an error occurred while loading trips:
    /// unspecified error". The second message is true, useless and alarming: nothing is
    /// broken, the credential simply ran out. Reported on 2026-09-19 with a screenshot.
    /// </para>
    /// <para>
    /// The signature matches <see cref="MessageBox"/> exactly so the call sites are a
    /// substitution and nothing else, and it returns
    /// <see cref="MessageBoxResult.None"/> when it suppresses — the same thing a dismissed
    /// dialog returns, so a caller that inspects the result reads "no answer" rather than an
    /// answer nobody gave.
    /// </para>
    /// <para>
    /// This is the second half of the fix. The first is in <c>AuthRefreshHandler</c>, which
    /// does not send a request it already knows will fail, so in most cases there is no error
    /// to suppress at all.
    /// </para>
    /// </remarks>
    public static class UiError
    {
        public static MessageBoxResult Show(
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon)
        {
            if (SessionManager.HasEnded)
            {
                return MessageBoxResult.None;
            }

            return MessageBox.Show(messageBoxText, caption, button, icon);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
        {
            if (SessionManager.HasEnded)
            {
                return MessageBoxResult.None;
            }

            return MessageBox.Show(messageBoxText, caption, button);
        }

        public static MessageBoxResult Show(string messageBoxText, string caption)
        {
            if (SessionManager.HasEnded)
            {
                return MessageBoxResult.None;
            }

            return MessageBox.Show(messageBoxText, caption);
        }

        public static MessageBoxResult Show(string messageBoxText)
        {
            if (SessionManager.HasEnded)
            {
                return MessageBoxResult.None;
            }

            return MessageBox.Show(messageBoxText);
        }
    }
}
