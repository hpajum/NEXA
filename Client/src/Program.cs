using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Nexa
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool preview = args.Contains("--preview");
            // The workspace is shared by all Windows sessions of the same user.
            string instance;
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                instance = "Global\\NEXA-" + identity.User.Value + (preview ? "-preview" : "");
            bool first;
            using (var mutex = new Mutex(true, instance, out first))
            {
                if (!first)
                {
                    if (!args.Contains("--tray"))
                        MessageBox.Show("NEXA در حال اجراست. آن را از کنار ساعت ویندوز باز کنید.", "NEXA",
                            MessageBoxButtons.OK, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1,
                            MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(true);
                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                using (var window = new MainWindow(preview) { Icon = icon ?? System.Drawing.SystemIcons.Application })
                using (var context = new ApplicationContext())
                using (var trayMenu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes, Font = AppTypography.Body })
                using (var tray = new NotifyIcon())
                {
                    tray.Text = preview ? "NEXA (آزمایشی)" : "NEXA";
                    tray.Icon = window.Icon;
                    tray.ContextMenuStrip = trayMenu;
                    trayMenu.Items.Add("باز کردن NEXA", null, delegate { window.Reveal(); });
                    trayMenu.Items.Add("بستن برنامه", null, delegate { window.Quit(); });
                    tray.DoubleClick += delegate { window.Reveal(); };
                    window.FormClosed += delegate { context.ExitThread(); };
                    tray.Visible = true;
                    try
                    {
                        if (!args.Contains("--tray")) window.Reveal();
                        Application.Run(context);
                    }
                    finally { tray.Visible = false; }
                }
            }
        }
    }
}
