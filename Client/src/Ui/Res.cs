using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexa
{
    internal static class Res
    {
        public static void Bind(FrameworkElement element, DependencyProperty property, string key)
        {
            element.SetResourceReference(property, key);
        }

        public static Brush Brush(string key)
        {
            return (Brush)Application.Current.Resources[key];
        }

        public static TextBlock Icon(string glyph, double size)
        {
            var text = new TextBlock
            {
                Text = glyph, FontSize = size, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                VerticalAlignment = VerticalAlignment.Center
            };
            return text;
        }
    }

    /// <summary>Segoe Fluent Icons code points used by the interface.</summary>
    internal static class Glyph
    {
        public const string Add = "\uE710";
        public const string Close = "\uE711";
        public const string Attach = "\uE723";
        public const string Send = "\uE724";
        public const string Settings = "\uE713";
        public const string Lock = "\uE72E";
        public const string Folder = "\uE8B7";
        public const string FolderOpen = "\uED25";
        public const string Document = "\uE8A5";
        public const string Chat = "\uE8BD";
        public const string Sun = "\uE706";
        public const string Moon = "\uE708";
        public const string Power = "\uE7E8";
        public const string Check = "\uE73E";
        public const string Delete = "\uE74D";
        public const string Edit = "\uE70F";
        public const string Contact = "\uE77B";
    }
}
