using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Nexa
{
    internal sealed partial class MainWindow
    {
        private void ShowFolder()
        {
            var project = ActiveProject;
            if (project == null) return;
            using (var dialog = Dialog("پوشه و سطح دسترسی پروژه", 575, 460))
            {
                var layout = Stack(16, Canvas, 37, 41, 41, 38, -1, 41);
                dialog.Controls.Add(layout);
                layout.Controls.Add(Heading("پوشهٔ پروژه", 15), 0, 0);
                var path = LabelFor("");
                layout.Controls.Add(path, 0, 1);
                var choices = Columns(160, 112, -1);
                layout.Controls.Add(choices, 0, 2);
                var select = ActionButton("انتخاب پوشه");
                var unlink = ActionButton("قطع اتصال");
                choices.Controls.Add(select, 0, 0);
                choices.Controls.Add(unlink, 1, 0);
                var write = new CheckBox { Text = "اجازهٔ کپی پیوست به پوشه", Dock = DockStyle.Fill };
                layout.Controls.Add(write, 0, 3);
                var files = List();
                files.AccessibleName = "فایل‌های پوشه";
                layout.Controls.Add(files, 0, 4);
                var open = ActionButton("باز کردن فایل انتخاب‌شده");
                layout.Controls.Add(open, 0, 5);

                Action refresh = delegate
                {
                    path.Text = project.Folder ?? "پوشه‌ای متصل نیست.";
                    unlink.Enabled = write.Enabled = project.Folder != null;
                    write.Checked = project.CanWrite;
                    files.Items.Clear();
                    open.Enabled = false;
                    if (project.Folder == null) return;
                    try
                    {
                        string folder = LocalFolder(project.Folder);
                        foreach (string file in Directory.EnumerateFiles(folder).Take(500))
                        {
                            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0)
                                files.Items.Add(new FileEntry(file));
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!IsExpected(ex)) throw;
                        path.Text = "پوشه در دسترس نیست: " + project.Folder;
                    }
                };

                select.Click += delegate
                {
                    using (var picker = new FolderBrowserDialog
                    {
                        Description = "پوشه‌ای از دیسک محلی را انتخاب کنید.", ShowNewFolderButton = false
                    })
                    {
                        if (picker.ShowDialog(dialog) != DialogResult.OK) return;
                        try
                        {
                            string folder = LocalFolder(picker.SelectedPath);
                            if (ChangeFolder(project, folder, false)) refresh();
                        }
                        catch (Exception ex) { if (!IsExpected(ex)) throw; Tell("پوشهٔ انتخاب‌شده محلی یا در دسترس نیست."); }
                    }
                };
                unlink.Click += delegate { if (ChangeFolder(project, null, false)) refresh(); };
                write.Click += delegate
                {
                    if (!ChangeFolder(project, project.Folder, write.Checked)) write.Checked = project.CanWrite;
                };
                files.SelectedIndexChanged += delegate { open.Enabled = files.SelectedItem is FileEntry; };
                open.Click += delegate
                {
                    var file = files.SelectedItem as FileEntry;
                    if (file == null) return;
                    try
                    {
                        string folder = LocalFolder(project.Folder);
                        string target = Path.Combine(folder, Path.GetFileName(file.Path));
                        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                            throw new IOException("Reparse point.");
                        ShowAttachment(target, false);
                    }
                    catch (Exception ex) { if (!IsExpected(ex)) throw; Tell("فایل انتخاب‌شده در دسترس نیست."); }
                };
                files.DoubleClick += delegate { open.PerformClick(); };
                refresh();
                dialog.ShowDialog(this);
            }
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

        private void ShowAttachment(string path, bool allowCopy = true)
        {
            var project = ActiveProject;
            using (var dialog = Dialog("فایل", 585, 175))
            {
                var layout = Stack(15, Canvas, 45, 43, 44);
                dialog.Controls.Add(layout);
                layout.Controls.Add(Heading(Path.GetFileName(path), 13), 0, 0);
                layout.Controls.Add(LabelFor(path), 0, 1);
                var actions = Columns(178, -1);
                layout.Controls.Add(actions, 0, 2);
                var open = ActionButton("باز کردن با برنامهٔ ویندوز", true);
                actions.Controls.Add(open, 0, 0);
                open.Click += delegate
                {
                    try
                    {
                        if (!File.Exists(path)) throw new FileNotFoundException();
                        using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })) { }
                    }
                    catch (Exception ex) { if (!IsExpected(ex)) throw; Tell("فایل موجود نیست یا برنامه‌ای برای باز کردن آن پیدا نشد."); }
                };
                if (allowCopy && project != null && project.CanWrite)
                {
                    var copy = ActionButton("کپی به پوشهٔ پروژه");
                    actions.Controls.Add(copy, 1, 0);
                    copy.Click += delegate
                    {
                        try
                        {
                            string folder = LocalFolder(project.Folder);
                            File.Copy(path, Path.Combine(folder, Path.GetFileName(path)), false);
                            Tell("پیوست در پوشهٔ پروژه کپی شد.");
                        }
                        catch (Exception ex)
                        {
                            if (!IsExpected(ex)) throw;
                            Tell("کپی انجام نشد. دسترسی، وجود فایل مبدا و فایل هم‌نام مقصد را بررسی کنید.");
                        }
                    };
                }
                dialog.ShowDialog(this);
            }
        }

        private static string LocalFolder(string path)
        {
            string fullPath = Path.GetFullPath(path);
            if (fullPath.Length < 3 || fullPath[1] != ':' || fullPath[2] != '\\' ||
                new DriveInfo(Path.GetPathRoot(fullPath)).DriveType != DriveType.Fixed)
                throw new IOException("Fixed local drive required.");

            var directory = new DirectoryInfo(fullPath);
            for (var current = directory; current != null; current = current.Parent)
            {
                var attributes = File.GetAttributes(current.FullName);
                if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Ordinary directory required.");
            }
            return directory.FullName;
        }
    }
}
