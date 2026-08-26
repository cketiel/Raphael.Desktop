using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Raphael.Desktop.Helpers
{
    /// <summary>
    /// Catches what nobody caught, writes it down, and keeps the application alive when
    /// it can.
    /// </summary>
    /// <remarks>
    /// Without this the application simply vanishes: a dispatcher reports "se cerró sola"
    /// and the only trace is the Windows event log, which nobody in the office is going to
    /// read. A WPF exception that reaches the dispatcher kills the process by default, and
    /// plenty of them — a control style animating a property the element does not have, a
    /// binding that throws — are entirely survivable.
    ///
    /// <para>
    /// ⚠️ Handling an exception is not the same as fixing it. Every entry in this log is a
    /// defect that still has to be found; the point is that the office keeps working while
    /// somebody does.
    /// </para>
    ///
    /// <para>
    /// The log holds exception text and stack traces. Nothing here should ever carry
    /// patient data, and no code should put it there.
    /// </para>
    /// </remarks>
    public static class CrashReporter
    {
        /// <summary>
        /// Errors shown to the user in one session before it goes quiet.
        /// </summary>
        /// <remarks>
        /// A broken style trigger can fire on every mouse move. Twenty modal dialogs in a
        /// row is its own kind of crash, so past this point it only logs.
        /// </remarks>
        private const int MaxDialogsPerSession = 3;

        private static readonly object Gate = new();

        private static int _dialogsShown;

        public static string LogFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RapphaelApp",
            "logs");

        /// <summary>
        /// Hooks the three places an unhandled exception can surface.
        /// </summary>
        public static void Install(Application application)
        {
            ArgumentNullException.ThrowIfNull(application);

            // The UI thread. Survivable: the application carries on.
            application.DispatcherUnhandledException += OnDispatcherUnhandledException;

            // Any other thread. Not survivable — the runtime is already on its way out —
            // but it can still be written down.
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

            // A Task nobody awaited. Silent by default, which is how a failed background
            // refresh disappears without trace.
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private static void OnDispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            Report(e.Exception, "UI thread");

            // Keep the office working. The defect is in the log.
            e.Handled = true;
        }

        private static void OnAppDomainUnhandledException(
            object sender,
            UnhandledExceptionEventArgs e)
        {
            Report(e.ExceptionObject as Exception, "background thread, fatal");
        }

        private static void OnUnobservedTaskException(
            object sender,
            UnobservedTaskExceptionEventArgs e)
        {
            Write(e.Exception, "unobserved task");

            e.SetObserved();
        }

        /// <summary>Writes the failure down and, the first few times, says so.</summary>
        public static void Report(Exception exception, string origin)
        {
            if (exception is null)
                return;

            var path = Write(exception, origin);

            bool show;

            lock (Gate)
            {
                show = _dialogsShown < MaxDialogsPerSession;

                if (show)
                    _dialogsShown++;
            }

            if (!show)
                return;

            try
            {
                MessageBox.Show(
                    $"{exception.Message}\n\n{Localized("CrashReportSaved")}\n{path}",
                    Localized("CrashReportTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch
            {
                // If even showing the message fails there is nothing further to try.
            }
        }

        /// <summary>Appends the failure to today's log. Returns the file it went to.</summary>
        public static string Write(Exception exception, string origin)
        {
            var path = Path.Combine(
                LogFolder,
                $"crash-{DateTime.Now:yyyyMMdd}.log");

            try
            {
                Directory.CreateDirectory(LogFolder);

                var entry = new StringBuilder()
                    .AppendLine(new string('-', 78))
                    .AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{origin}]")
                    .AppendLine($"User: {Session()}  Version: {VersionHelper.WindowTitle}")
                    .AppendLine()
                    .AppendLine(exception.ToString())
                    .AppendLine();

                File.AppendAllText(path, entry.ToString());
            }
            catch
            {
                // A disk that will not take the log must not become a second crash.
            }

            return path;
        }

        private static string Session()
        {
            return string.IsNullOrWhiteSpace(SessionManager.UserId)
                ? "not signed in"
                : $"{SessionManager.Username} (id {SessionManager.UserId}, role {SessionManager.Role})";
        }

        /// <summary>
        /// Translated text, falling back to English. The crash handler cannot assume the
        /// language files loaded: failing to load them is one of the things it has to report.
        /// </summary>
        private static string Localized(string key)
        {
            try
            {
                return Services.LocalizationService.Instance.TryGetValue(key, out var text)
                    ? text
                    : Fallback(key);
            }
            catch
            {
                return Fallback(key);
            }
        }

        private static string Fallback(string key) => key switch
        {
            "CrashReportTitle" => "Unexpected error",
            "CrashReportSaved" => "The details were saved to:",
            _ => key
        };
    }
}
