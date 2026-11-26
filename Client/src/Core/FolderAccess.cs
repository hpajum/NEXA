using System;
using System.IO;

namespace Nexa
{
    internal static class FolderAccess
    {
        /// <summary>Accepts only ordinary directories on fixed local drives; no junctions or symlinks anywhere in the path.</summary>
        public static string LocalFolder(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (fullPath.Length < 3 || fullPath[1] != ':' || fullPath[2] != '\\' ||
                new DriveInfo(Path.GetPathRoot(fullPath)).DriveType != DriveType.Fixed)
                throw new IOException("Fixed local drive required.");

            var directory = new DirectoryInfo(fullPath);
            for (var current = directory; current != null; current = current.Parent)
            {
                var attributes = File.GetAttributes(current.FullName);
                if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Ordinary directory required.");
            }
            return directory.FullName;
        }

        public static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
    }
}
