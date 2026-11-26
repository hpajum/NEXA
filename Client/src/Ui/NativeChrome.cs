using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nexa
{
    /// <summary>Asks Windows 11 to draw a dark or light title bar and rounded corners that match the theme.</summary>
    internal static class NativeChrome
    {
        private const int DarkMode = 20;          // DWMWA_USE_IMMERSIVE_DARK_MODE
        private const int CornerPreference = 33;  // DWMWA_WINDOW_CORNER_PREFERENCE

        public static void Attach(Window window)
        {
            window.SourceInitialized += delegate { Apply(window, ThemeManager.IsDark); };
        }

        public static void Apply(Window window, bool dark)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            try
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(handle, DarkMode, ref value, sizeof(int));
                int round = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(handle, CornerPreference, ref round, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    }
}
