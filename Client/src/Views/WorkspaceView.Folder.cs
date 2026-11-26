using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;

namespace Nexa
{
    internal sealed partial class WorkspaceView
    {
        private void ShowFolder()
        {
            var project = ActiveProject;
            if (project == null) return;
            var body = new StackPanel();
            var path = Build.Text("", 12.5, true);
            path.FlowDirection = FlowDirection.LeftToRight;
            path.HorizontalAlignment = HorizontalAlignment.Right;
            path.Margin = new Thickness(0, 0, 0, 12);
            body.Children.Add(path);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            var select = Build.IconTextButton(Glyph.Folder, "انتخاب پوشه");
            var unlink = Build.Button("قطع اتصال");
            unlink.Margin = new Thickness(8, 0, 0, 0);
            buttons.Children.Add(select);
            buttons.Children.Add(unlink);
            body.Children.Add(buttons);

            var write = new CheckBox { Content = "اجازهٔ کپی پیوست به این پوشه", Margin = new Thickness(0, 0, 0, 12) };
            write.SetResourceReference(StyleProperty, "ToggleSwitch");
            body.Children.Add(write);

            var files = new ListBox { Height = 210, FlowDirection = FlowDirection.LeftToRight };
            files.SetResourceReference(StyleProperty, "NavList");
            Res.Bind(files, Control.ForegroundProperty, "InkBrush");
            var filesLoading = new LoadingOverlay(26);
            var filesHost = new Grid();
            filesHost.Children.Add(files);
            filesHost.Children.Add(filesLoading);
            body.Children.Add(filesHost);
            var open = Build.Button("باز کردن فایل انتخاب‌شده", "primary");
            open.Margin = new Thickness(0, 12, 0, 0);
            open.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(open);

            var modal = Modal.Open(host, "پوشه و سطح دسترسی پروژه", body, 560);
            bool updating = false;
            Action refresh = null;
            refresh = async delegate
            {
                updating = true;
                path.Text = project.Folder ?? "پوشه‌ای متصل نیست.";
                path.FlowDirection = project.Folder == null ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
                unlink.IsEnabled = write.IsEnabled = project.Folder != null;
                write.IsChecked = project.CanWrite;
                updating = false;
                files.Items.Clear();
                open.IsEnabled = false;
                if (project.Folder == null) return;
                try
                {
                    string folder = project.Folder;
                    var found = await filesLoading_Run(filesLoading, delegate
                    {
                        string local = FolderAccess.LocalFolder(folder);
                        return Directory.EnumerateFiles(local).Take(500)
                            .Where(f => !FolderAccess.IsReparsePoint(f)).Select(f => new FileEntry(f)).ToList();
                    });
                    files.Items.Clear();
                    foreach (var entry in found) files.Items.Add(entry);
                }
                catch (Exception ex)
                {
                    if (!Errors.IsExpected(ex)) throw;
                    path.Text = "پوشه در دسترس نیست: " + project.Folder;
                }
            };

            select.Click += delegate
            {
                using (var picker = new Forms.FolderBrowserDialog { Description = "پوشه‌ای از دیسک محلی را انتخاب کنید.", ShowNewFolderButton = false })
                {
                    if (picker.ShowDialog() != Forms.DialogResult.OK) return;
                    try
                    {
                        if (ChangeFolder(project, FolderAccess.LocalFolder(picker.SelectedPath), false)) refresh();
                    }
                    catch (Exception ex)
                    {
                        if (!Errors.IsExpected(ex)) throw;
                        Toast.Show(host, "پوشهٔ انتخاب‌شده محلی یا در دسترس نیست.", true);
                    }
                }
            };
            unlink.Click += delegate { if (ChangeFolder(project, null, false)) refresh(); };
            write.Click += delegate
            {
                if (updating) return;
                if (!ChangeFolder(project, project.Folder, write.IsChecked == true)) write.IsChecked = project.CanWrite;
            };
            files.SelectionChanged += delegate { open.IsEnabled = files.SelectedItem is FileEntry; };
            open.Click += delegate
            {
                var file = files.SelectedItem as FileEntry;
                if (file == null) return;
                try
                {
                    string target = Path.Combine(FolderAccess.LocalFolder(project.Folder), file.Name);
                    if (FolderAccess.IsReparsePoint(target)) throw new IOException("Reparse point.");
                    ShowAttachment(target, false);
                }
                catch (Exception ex)
                {
                    if (!Errors.IsExpected(ex)) throw;
                    Toast.Show(host, "فایل انتخاب‌شده در دسترس نیست.", true);
                }
            };
            refresh();
        }

        private static async System.Threading.Tasks.Task<T> filesLoading_Run<T>(LoadingOverlay overlay, Func<T> work)
        {
            return await overlay.Run(null, work);
        }

        private bool ChangeFolder(Project project, string folder, bool canWrite)
        {
            string previousFolder = project.Folder;
            bool previousAccess = project.CanWrite;
            project.Folder = folder;
            project.CanWrite = canWrite;
            if (Persist()) return true;
            project.Folder = previousFolder;
            project.CanWrite = previousAccess;
            return false;
        }

        private void ShowAttachment(string path, bool allowCopy)
        {
            var project = ActiveProject;
            var body = new StackPanel();
            var location = Build.Text(path, 12.5, true);
            location.FlowDirection = FlowDirection.LeftToRight;
            location.HorizontalAlignment = HorizontalAlignment.Right;
            body.Children.Add(location);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 18, 0, 0) };
            var open = Build.IconTextButton(Glyph.Document, "باز کردن با برنامهٔ ویندوز", "primary");
            actions.Children.Add(open);
            open.Click += delegate
            {
                try
                {
                    if (!File.Exists(path)) throw new FileNotFoundException();
                    using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })) { }
                }
                catch (Exception ex)
                {
                    if (!Errors.IsExpected(ex)) throw;
                    Toast.Show(host, "فایل موجود نیست یا برنامه‌ای برای باز کردن آن پیدا نشد.", true);
                }
            };
            if (allowCopy && project != null && project.CanWrite)
            {
                var copy = Build.Button("کپی به پوشهٔ پروژه");
                copy.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(copy);
                copy.Click += delegate
                {
                    try
                    {
                        string folder = FolderAccess.LocalFolder(project.Folder);
                        File.Copy(path, Path.Combine(folder, Path.GetFileName(path)), false);
                        Toast.Show(host, "پیوست در پوشهٔ پروژه کپی شد.");
                    }
                    catch (Exception ex)
                    {
                        if (!Errors.IsExpected(ex)) throw;
                        Toast.Show(host, "کپی انجام نشد. دسترسی، وجود فایل مبدا و فایل هم‌نام مقصد را بررسی کنید.", true);
                    }
                };
            }
            body.Children.Add(actions);
            Modal.Open(host, Path.GetFileName(path), body, 520);
        }
    }
}
