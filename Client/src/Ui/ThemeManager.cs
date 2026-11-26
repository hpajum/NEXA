using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Nexa
{
    /// <summary>
    /// Swaps the light/dark palette and the accent colour at run time. Every control uses DynamicResource,
    /// so the whole window repaints without being rebuilt. "system" follows the Windows app theme.
    /// </summary>
    internal static class ThemeManager
    {
        public static readonly IDictionary<string, string> Accents = new Dictionary<string, string>
        {
            { "indigo", "#5B5BD6" }, { "blue", "#2563EB" }, { "teal", "#0D9488" },
            { "violet", "#8B5CF6" }, { "rose", "#E11D48" }, { "amber", "#D97706" }
        };

        private static ResourceDictionary palette;
        public static event EventHandler Changed;

        public static bool IsDark { get; private set; }

        public static void Initialize(Application app)
        {
            app.Resources.MergedDictionaries.Add(XamlLoader.Load<ResourceDictionary>("Themes.Styles"));
            SystemEvents.UserPreferenceChanged += delegate(object sender, UserPreferenceChangedEventArgs e)
            {
                if (e.Category == UserPreferenceCategory.General && AppSettings.Theme == "system")
                    app.Dispatcher.BeginInvoke(new Action(Apply));
            };
            Apply();
        }

        public static void SetTheme(string theme) { AppSettings.Theme = theme; Apply(); }
        public static void SetAccent(string accent) { AppSettings.Accent = accent; Apply(); }

        public static void Toggle()
        {
            SetTheme(IsDark ? "light" : "dark");
        }

        public static void Apply()
        {
            var resources = Application.Current.Resources;
            string theme = AppSettings.Theme;
            IsDark = theme == "dark" || (theme == "system" && SystemPrefersDark());
            var next = XamlLoader.Load<ResourceDictionary>(IsDark ? "Themes.Dark" : "Themes.Light");
            if (palette != null) resources.MergedDictionaries.Remove(palette);
            resources.MergedDictionaries.Insert(0, next);
            palette = next;

            string hex;
            if (!Accents.TryGetValue(AppSettings.Accent, out hex)) hex = Accents["indigo"];
            var accent = (Color)ColorConverter.ConvertFromString(hex);
            resources["AccentColor"] = accent;
            resources["AccentBrush"] = Frozen(new SolidColorBrush(accent));
            resources["AccentSoft"] = Frozen(new SolidColorBrush(Color.FromArgb((byte)(IsDark ? 0x40 : 0x22), accent.R, accent.G, accent.B)));
            resources["OnAccentBrush"] = Frozen(new SolidColorBrush(Colors.White));
            var glow = new LinearGradientBrush(accent, Color.FromArgb(0xFF,
                (byte)Math.Min(255, accent.R + 60), (byte)Math.Min(255, accent.G + 40), (byte)Math.Min(255, accent.B + 20)), 45);
            resources["AccentGradient"] = Frozen(glow);

            foreach (Window window in Application.Current.Windows) NativeChrome.Apply(window, IsDark);
            var handler = Changed;
            if (handler != null) handler(null, EventArgs.Empty);
        }

        private static Brush Frozen(Brush brush) { brush.Freeze(); return brush; }

        private static bool SystemPrefersDark()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 0;
                }
            }
            catch (Exception ex)
            {
                if (!Errors.IsExpected(ex)) throw;
                return false;
            }
        }
    }
}
