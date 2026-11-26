using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Nexa
{
    public sealed class SavedLogin
    {
        public string Server { get; set; }
        public string Account { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

        internal bool IsForServer(Uri server)
        {
            return server != null && String.Equals(Server, server.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal) &&
                !String.IsNullOrWhiteSpace(Account) && !String.IsNullOrWhiteSpace(Username) &&
                !String.IsNullOrEmpty(Password);
        }
    }

    public sealed class CredentialVault
    {
        public List<WebCredential> Entries { get; set; }
        public CredentialVault() { Entries = new List<WebCredential>(); }
    }

    public sealed class WebCredential
    {
        public string Site { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

        public override string ToString() { return Site + "  —  " + Username; }

        internal static Uri SiteUrl(string input)
        {
            Uri uri;
            return Uri.TryCreate(input.Trim(), UriKind.Absolute, out uri) &&
                uri.Scheme == Uri.UriSchemeHttps && !String.IsNullOrEmpty(uri.Host) &&
                uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 ? uri : null;
        }
    }

    internal sealed partial class MainWindow
    {
        private static TextBox PasswordInput()
        {
            var input = Input("رمز عبور");
            input.MaxLength = 1024;
            input.RightToLeft = RightToLeft.No;
            input.UseSystemPasswordChar = true;
            return input;
        }

        private void ShowVault()
        {
            CredentialVault stored;
            SavedLogin login = null;
            try
            {
                stored = LocalState.Load<CredentialVault>(vaultPath);
                if (stored.Entries == null)
                    throw new System.Runtime.Serialization.SerializationException("Invalid vault.");
                if (savedLoginPath != null)
                {
                    var candidate = LocalState.Load<SavedLogin>(savedLoginPath);
                    if (candidate.IsForServer(sessionServer) && String.Equals(candidate.Account, account, StringComparison.OrdinalIgnoreCase))
                        login = candidate;
                }
            }
            catch (Exception ex)
            {
                if (!IsExpected(ex)) throw;
                Tell("صندوق رمز روی این رایانه قابل خواندن نیست.");
                return;
            }

            using (var dialog = Dialog("مدیریت رمزها", 660, 445))
            {
                var layout = Stack(18, Canvas, 45, 37, 39, 34, -1, 46);
                dialog.Controls.Add(layout);
                layout.Controls.Add(Heading("مدیریت رمزها", 15), 0, 0);
                var loginLabel = LabelFor(login == null ? "ورود ذخیره‌شدهٔ NEXA برای این حساب وجود ندارد." :
                    "ورود ذخیره‌شدهٔ NEXA · " + login.Username);
                layout.Controls.Add(loginLabel, 0, 1);
                var loginActions = Columns(165, 165, -1);
                layout.Controls.Add(loginActions, 0, 2);
                var viewLogin = ActionButton("نمایش اطلاعات ورود");
                var forgetLogin = ActionButton("حذف ورود ذخیره‌شده");
                loginActions.Controls.Add(viewLogin, 0, 0);
                loginActions.Controls.Add(forgetLogin, 1, 0);
                viewLogin.Enabled = forgetLogin.Enabled = login != null;
                viewLogin.Click += delegate
                {
                    EditWebCredential(dialog, new WebCredential
                    {
                        Site = "حساب سازمانی NEXA", Username = login.Username, Password = login.Password
                    }, true);
                };
                forgetLogin.Click += delegate
                {
                    if (Tell("اطلاعات ذخیره‌شدهٔ ورود NEXA حذف شود؟", MessageBoxButtons.YesNo, dialog) != DialogResult.Yes)
                        return;
                    try
                    {
                        File.Delete(savedLoginPath);
                        login = null;
                        viewLogin.Enabled = forgetLogin.Enabled = false;
                        loginLabel.Text = "ورود ذخیره‌شدهٔ NEXA حذف شد.";
                    }
                    catch (Exception ex)
                    {
                        if (!IsExpected(ex)) throw;
                        Tell("حذف ورود ذخیره‌شده انجام نشد.", owner: dialog);
                    }
                };

                layout.Controls.Add(Heading("حساب‌های وب", 12), 0, 3);
                var entries = List();
                entries.RightToLeft = RightToLeft.No;
                entries.AccessibleName = "حساب‌های وب";
                layout.Controls.Add(entries, 0, 4);
                var actions = Columns(138, 138, 105, -1);
                layout.Controls.Add(actions, 0, 5);
                var add = ActionButton("حساب جدید", true);
                var edit = ActionButton("مشاهده / ویرایش");
                var remove = ActionButton("حذف");
                actions.Controls.Add(add, 0, 0);
                actions.Controls.Add(edit, 1, 0);
                actions.Controls.Add(remove, 2, 0);
                Action<WebCredential> refresh = selected =>
                {
                    entries.Items.Clear();
                    foreach (var entry in stored.Entries) entries.Items.Add(entry);
                    if (selected != null) entries.SelectedItem = selected;
                    edit.Enabled = remove.Enabled = entries.SelectedIndex >= 0;
                };
                Action<CredentialVault, WebCredential> save = (next, selected) =>
                {
                    try
                    {
                        LocalState.Save(vaultPath, next);
                        stored = next;
                        refresh(selected);
                    }
                    catch (Exception ex)
                    {
                        if (!IsExpected(ex)) throw;
                        Tell("تغییرات صندوق رمز ذخیره نشد.", owner: dialog);
                    }
                };
                Action<WebCredential> update = current =>
                {
                    var updated = EditWebCredential(dialog, current, false);
                    if (updated == null) return;
                    var next = new CredentialVault { Entries = new List<WebCredential>(stored.Entries) };
                    if (next.Entries.Any(entry => entry != current && entry.Site == updated.Site && entry.Username == updated.Username))
                    {
                        Tell("این نام کاربری برای همین نشانی قبلاً ثبت شده است.", owner: dialog);
                        return;
                    }
                    if (current == null) next.Entries.Add(updated);
                    else next.Entries[next.Entries.IndexOf(current)] = updated;
                    save(next, updated);
                };
                add.Click += delegate { update(null); };
                edit.Click += delegate { update((WebCredential)entries.SelectedItem); };
                remove.Click += delegate
                {
                    var entry = (WebCredential)entries.SelectedItem;
                    if (entry == null || Tell("حساب وب انتخاب‌شده حذف شود؟", MessageBoxButtons.YesNo, dialog) != DialogResult.Yes)
                        return;
                    var next = new CredentialVault { Entries = new List<WebCredential>(stored.Entries) };
                    next.Entries.Remove(entry);
                    save(next, null);
                };
                entries.SelectedIndexChanged += delegate { edit.Enabled = remove.Enabled = entries.SelectedIndex >= 0; };
                entries.DoubleClick += delegate { edit.PerformClick(); };
                refresh(null);
                dialog.ShowDialog(this);
            }
        }

        private WebCredential EditWebCredential(Form parent, WebCredential entry, bool readOnly)
        {
            using (var dialog = Dialog(readOnly ? "اطلاعات ورود NEXA" : entry == null ? "حساب وب جدید" : "ویرایش حساب وب", 485, 361))
            {
                var layout = Stack(18, Canvas, 28, 31, 28, 31, 28, 31, 33, 35, 43);
                dialog.Controls.Add(layout);
                var site = Input("نشانی وب");
                site.RightToLeft = RightToLeft.No;
                site.MaxLength = 2048;
                site.Text = entry == null ? "" : entry.Site;
                var username = Input("نام کاربری");
                username.RightToLeft = RightToLeft.No;
                username.Text = entry == null ? "" : entry.Username;
                var secret = PasswordInput();
                secret.Text = entry == null ? "" : entry.Password;
                site.ReadOnly = username.ReadOnly = secret.ReadOnly = readOnly;
                var show = new CheckBox { Dock = DockStyle.Fill, Text = "نمایش رمز عبور" };
                show.CheckedChanged += delegate { secret.UseSystemPasswordChar = !show.Checked; };
                var status = LabelFor("");
                var done = ActionButton(readOnly ? "بستن" : "ذخیره", true);
                layout.Controls.Add(LabelFor(readOnly ? "خدمت" : "نشانی وب (HTTPS)"), 0, 0);
                layout.Controls.Add(site, 0, 1);
                layout.Controls.Add(LabelFor("نام کاربری"), 0, 2);
                layout.Controls.Add(username, 0, 3);
                layout.Controls.Add(LabelFor("رمز عبور"), 0, 4);
                layout.Controls.Add(secret, 0, 5);
                layout.Controls.Add(show, 0, 6);
                layout.Controls.Add(status, 0, 7);
                layout.Controls.Add(done, 0, 8);
                dialog.AcceptButton = done;
                WebCredential result = null;
                done.Click += delegate
                {
                    if (!readOnly)
                    {
                        Uri url = WebCredential.SiteUrl(site.Text);
                        string name = username.Text.Trim();
                        if (url == null || name.Length == 0 || secret.Text.Length == 0)
                        {
                            status.Text = "نشانی HTTPS، نام کاربری و رمز عبور را وارد کنید.";
                            return;
                        }
                        result = new WebCredential { Site = url.AbsoluteUri, Username = name, Password = secret.Text };
                    }
                    dialog.Close();
                };
                dialog.ShowDialog(parent);
                secret.Clear();
                return result;
            }
        }
    }
}
