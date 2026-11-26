using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace Nexa
{
    internal static class AppTypography
    {
        private static readonly PrivateFontCollection Collection = new PrivateFontCollection();
        private static readonly FontFamily Family;
        public static readonly Font Body;

        static AppTypography()
        {
            Register("Regular");
            Register("Bold");
            Family = Collection.Families[0];
            Body = Create(10.5F, FontStyle.Regular);
        }

        public static Font Create(float size, FontStyle style)
        {
            return new Font(Family, size, style, GraphicsUnit.Point);
        }

        private static void Register(string weight)
        {
            string name = "Nexa.Fonts.Vazirmatn-" + weight + ".ttf";
            using (Stream stream = typeof(AppTypography).Assembly.GetManifestResourceStream(name))
            {
                if (stream == null) throw new InvalidOperationException("Missing embedded font: " + name);
                byte[] bytes = new byte[checked((int)stream.Length)];
                using (var reader = new BinaryReader(stream))
                    bytes = reader.ReadBytes(bytes.Length);
                IntPtr memory = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, memory, bytes.Length);
                uint fontCount;
                if (AddFontMemResourceEx(memory, (uint)bytes.Length, IntPtr.Zero, out fontCount) == IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(memory);
                    throw new InvalidOperationException("Could not register font: " + name);
                }
                // GDI+ can retain the unmanaged font data until the collection is released at process exit.
                Collection.AddMemoryFont(memory, bytes.Length);
            }
        }

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr AddFontMemResourceEx(IntPtr font, uint size, IntPtr reserved, out uint count);
    }
}
