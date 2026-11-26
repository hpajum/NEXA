using System;
using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace Nexa
{
    /// <summary>
    /// Loads the .xaml files embedded in NEXA.exe. They are parsed at run time, so the project builds with
    /// the C# compiler that ships with Windows and needs no SDK or NuGet packages.
    /// </summary>
    internal static class XamlLoader
    {
        public static T Load<T>(string name) where T : class
        {
            string resource = "Nexa.Xaml." + name + ".xaml";
            using (Stream stream = typeof(XamlLoader).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null) throw new InvalidOperationException("Missing embedded XAML: " + resource);
                return (T)XamlReader.Load(stream);
            }
        }

        public static T Get<T>(this FrameworkElement root, string name) where T : class
        {
            var found = root.FindName(name) as T;
            if (found == null) throw new InvalidOperationException("Element not found in XAML: " + name);
            return found;
        }
    }
}
