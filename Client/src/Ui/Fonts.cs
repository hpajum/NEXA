using System;
using System.IO;
using System.Windows.Media;

namespace Nexa
{
    internal static class Fonts
    {
        public static FontFamily Body { get; private set; }

        /// <summary>
        /// WPF cannot load fonts straight from a raw assembly resource, so the two Vazirmatn weights are
        /// written once to %LOCALAPPDATA%\NEXA\Fonts (not installed into Windows) and loaded from there.
        /// </summary>
        public static void Initialize()
        {
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NEXA", "Fonts");
                Directory.CreateDirectory(directory);
                Extract(directory, "Regular");
                Extract(directory, "Bold");
                Body = new FontFamily(new Uri(directory + Path.DirectorySeparatorChar), "./#Vazirmatn, Segoe UI");
            }
            catch (Exception ex)
            {
                if (!Errors.IsExpected(ex)) throw;
                Body = new FontFamily("Segoe UI");
            }
        }

        private static void Extract(string directory, string weight)
        {
            string resource = "Nexa.Fonts.Vazirmatn-" + weight + ".ttf";
            string target = Path.Combine(directory, "Vazirmatn-" + weight + ".ttf");
            using (Stream stream = typeof(Fonts).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null) throw new InvalidOperationException("Missing embedded font: " + resource);
                if (File.Exists(target) && new FileInfo(target).Length == stream.Length) return;
                string pending = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var file = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        stream.CopyTo(file);
                    if (File.Exists(target)) File.Delete(target);
                    File.Move(pending, target);
                }
                finally
                {
                    if (File.Exists(pending))
                    {
                        try { File.Delete(pending); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }
        }
    }
}
