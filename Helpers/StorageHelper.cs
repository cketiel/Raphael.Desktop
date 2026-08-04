using System.IO;

namespace Raphael.Desktop.Helpers
{
    public static class StorageHelper
    {
        private static string FilePath => "user.settings";

        public static void SaveUsername(string username)
        {
            File.WriteAllText(FilePath, username);
        }

        public static string LoadUsername()
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath) : string.Empty;
        }
    }
}
