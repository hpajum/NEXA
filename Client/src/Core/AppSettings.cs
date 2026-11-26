using System;
using System.IO;
using Microsoft.Win32;

namespace Nexa
{
    /// <summary>Non-sensitive appearance preferences, stored per Windows user in HKCU.</summary>
    internal static class AppSettings
    {
        private const string KeyPath = @"Software\NEXA\Settings";

        public static string Theme
        {
            get { return Get("Theme", "system"); }
            set { Set("Theme", value); }
        }

        public static string Accent
        {
            get { return Get("Accent", "indigo"); }
            set { Set("Accent", value); }
        }

        public static bool Animations
        {
            get { return Get("Animations", "1") == "1"; }
            set { Set("Animations", value ? "1" : "0"); }
        }

        private static string Get(string name, string fallback)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
                {
                    string value = key == null ? null : key.GetValue(name) as string;
                    return string.IsNullOrEmpty(value) ? fallback : value;
                }
            }
            catch (Exception ex)
            {
                if (!Errors.IsExpected(ex)) throw;
                return fallback;
            }
        }

        private static void Set(string name, string value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(KeyPath)) key.SetValue(name, value);
            }
            catch (Exception ex)
            {
                if (!Errors.IsExpected(ex)) throw;
            }
        }
    }
}
