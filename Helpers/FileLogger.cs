using System;
using System.IO;

namespace Raphael.Desktop.Helpers
{
    public static class FileLogger
    {
        private static readonly string LogFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Raphael",
            "raphael.log");

        public static void Log(string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(LogFile, $"{DateTime.Now:O} - {message}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { /* evitar crash por logger */ }
        }

        public static void Log(Exception ex) => Log(ex.ToString());
    }
}
