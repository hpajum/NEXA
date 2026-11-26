using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Nexa
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool preview = args.Contains("--preview");
            bool tray = args.Contains("--tray");
            // The workspace is shared by all Windows sessions of the same user.
            string instance;
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                instance = "Global\\NEXA-" + identity.User.Value + (preview ? "-preview" : "");
            bool first;
            using (var mutex = new Mutex(true, instance, out first))
            {
                if (!first)
                {
                    if (!tray)
                        MessageBox.Show("NEXA در حال اجراست. آن را از کنار ساعت ویندوز باز کنید.", "NEXA",
                            MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK,
                            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                    return;
                }

                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Fonts.Initialize();
                ThemeManager.Initialize(app);

                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetEntryAssembly().Location))
                using (var menu = new System.Windows.Forms.ContextMenuStrip
                {
                    RightToLeft = System.Windows.Forms.RightToLeft.Yes,
                    Font = new System.Drawing.Font("Segoe UI", 9.5F)
                })
                using (var notify = new System.Windows.Forms.NotifyIcon())
                {
                    var window = new MainWindow(preview);
                    if (icon != null)
                        window.Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                    notify.Text = preview ? "NEXA (آزمایشی)" : "NEXA";
                    notify.Icon = icon ?? System.Drawing.SystemIcons.Application;
                    notify.ContextMenuStrip = menu;
                    menu.Items.Add("باز کردن NEXA", null, delegate { window.Reveal(); });
                    menu.Items.Add("بستن برنامه", null, delegate { window.Quit(); });
                    notify.MouseDoubleClick += delegate { window.Reveal(); };
                    window.Closed += delegate { app.Shutdown(); };
                    notify.Visible = true;
                    try
                    {
                        if (!tray) window.Reveal();
                        app.Run();
                    }
                    finally { notify.Visible = false; }
                }
            }
        }
    }
}
