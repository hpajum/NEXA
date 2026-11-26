using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Nexa
{
    internal sealed partial class WorkspaceView
    {
        private async void ShowVault()
        {
            CredentialVault stored;
            SavedLogin login = null;
            try
            {
                var loaded = await sidebarLoading.Run("در حال خواندن صندوق رمز…", () =>
                {
                    var vault = LocalState.Load<CredentialVault>(session.VaultPath);
                    if (vault.Entries == null) throw new System.Runtime.Serialization.SerializationException("Invalid vault.");
                    SavedLogin saved = null;
                    if (session.SavedLoginPath != null)
                    {
                        var candidate = LocalState.Load<SavedLogin>(session.SavedLoginPath);
                        if (candidate.IsForServer(session.Server) &&
                            string.Equals(candidate.Account, session.Account, StringComparison.OrdinalIgnoreCase)) saved = candidate;
                    }
                    return Tuple.Create(vault, saved);
                });
                stored = loaded.Item1;
                login = loaded.Item2;
            }
            catch (Exception ex)
            {
                if (!Errors.IsExpected(ex)) throw;
                Toast.Show(host, "صندوق رمز روی این رایانه قابل خواندن نیست.", true);
                return;
            }

            var body = new StackPanel();
            var loginLabel = Build.Text(login == null ? "ورود ذخیره‌شدهٔ NEXA برای این حساب وجود ندارد." :
                "ورود ذخیره‌شدهٔ NEXA · " + login.Username, 13, true);
            body.Children.Add(loginLabel);
            var loginActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var viewLogin = Build.Button("نمایش اطلاعات ورود");
            var forgetLogin = Build.Button("حذف ورود ذخیره‌شده");
            forgetLogin.Margin = new Thickness(8, 0, 0, 0);
            loginActions.Children.Add(viewLogin);
            loginActions.Children.Add(forgetLogin);
            viewLogin.IsEnabled = forgetLogin.IsEnabled = login != null;
            body.Children.Add(loginActions);
            body.Children.Add(Build.Divider());

            body.Children.Add(Build.Text("حساب‌های وب", 14, false, true));
            var entries = new ListBox { Height = 170, Margin = new Thickness(0, 8, 0, 10), FlowDirection = FlowDirection.LeftToRight };
            entries.SetResourceReference(StyleProperty, "NavList");
            Res.Bind(entries, Control.ForegroundProperty, "InkBrush");
            body.Children.Add(entries);
            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            var add = Build.IconTextButton(Glyph.Add, "حساب جدید", "primary");
            var edit = Build.Button("مشاهده / ویرایش");
            var remove = Build.Button("حذف");
            edit.Margin = remove.Margin = new Thickness(8, 0, 0, 0);
            actions.Children.Add(add);
            actions.Children.Add(edit);
            actions.Children.Add(remove);
            body.Children.Add(actions);
            var modal = Modal.Open(host, "مدیریت رمزها", body, 600);

            Action<WebCredential> refresh = selected =>
            {
                entries.Items.Clear();
                foreach (var entry in stored.Entries) entries.Items.Add(entry);
                if (selected != null) entries.SelectedItem = selected;
                edit.IsEnabled = remove.IsEnabled = entries.SelectedIndex >= 0;
            };
            Action<CredentialVault, WebCredential> save = (next, selected) =>
            {
                try { LocalState.Save(session.VaultPath, next); stored = next; refresh(selected); }
                catch (Exception ex)
                {
                    if (!Errors.IsExpected(ex)) throw;
                    Toast.Show(host, "تغییرات صندوق رمز ذخیره نشد.", true);
                }
            };
            Action<WebCredential> update = current =>
            {
                EditWebCredential(current, false, updated =>
                {
                    var next = new CredentialVault { Entries = new List<WebCredential>(stored.Entries) };
                    if (next.Entries.Any(e => e != current && e.Site == updated.Site && e.Username == updated.Username))
                    {
                        Toast.Show(host, "این نام کاربری برای همین نشانی قبلاً ثبت شده است.", true);
                        return;
                    }
                    if (current == null) next.Entries.Add(updated);
                    else next.Entries[next.Entries.IndexOf(current)] = updated;
                    save(next, updated);
                });
            };
            add.Click += delegate { update(null); };
            edit.Click += delegate { update(entries.SelectedItem as WebCredential); };
            remove.Click += delegate
            {
                var entry = entries.SelectedItem as WebCredential;
                if (entry == null) return;
                Modal.Confirm(host, "حذف حساب وب", "حساب وب انتخاب‌شده حذف شود؟", "حذف", delegate
                {
                    var next = new CredentialVault { Entries = new List<WebCredential>(stored.Entries) };
                    next.Entries.Remove(entry);
                    save(next, null);
                });
            };
            entries.SelectionChanged += delegate { edit.IsEnabled = remove.IsEnabled = entries.SelectedIndex >= 0; };
            viewLogin.Click += delegate
            {
                EditWebCredential(new WebCredential { Site = "حساب سازمانی NEXA", Username = login.Username, Password = login.Password }, true, null);
            };
            forgetLogin.Click += delegate
            {
                Modal.Confirm(host, "حذف ورود ذخیره‌شده", "اطلاعات ذخیره‌شدهٔ ورود NEXA حذف شود؟", "حذف", delegate
                {
                    try
                    {
                        File.Delete(session.SavedLoginPath);
                        login = null;
                        viewLogin.IsEnabled = forgetLogin.IsEnabled = false;
                        loginLabel.Text = "ورود ذخیره‌شدهٔ NEXA حذف شد.";
                    }
                    catch (Exception ex)
                    {
                        if (!Errors.IsExpected(ex)) throw;
                        Toast.Show(host, "حذف ورود ذخیره‌شده انجام نشد.", true);
                    }
                });
            };
            refresh(null);
        }

        private void EditWebCredential(WebCredential entry, bool readOnly, Action<WebCredential> done)
        {
            var body = new StackPanel();
            var site = new TextBox { FlowDirection = FlowDirection.LeftToRight, MaxLength = 2048, Text = entry == null ? "" : entry.Site, IsReadOnly = readOnly };
            var username = new TextBox { FlowDirection = FlowDirection.LeftToRight, MaxLength = 260, Text = entry == null ? "" : entry.Username, IsReadOnly = readOnly };
            var secret = new SecretBox { Text = entry == null ? "" : entry.Password };
            secret.ReadOnly = readOnly;
            body.Children.Add(Build.Labeled(readOnly ? "خدمت" : "نشانی وب (HTTPS)", Build.Field(site, "https://example.org")));
            body.Children.Add(Build.Labeled("نام کاربری", Build.Field(username, "")));
            body.Children.Add(Build.Labeled("رمز عبور", secret));
            var status = Build.Text("", 12.5, true);
            Res.Bind(status, TextBlock.ForegroundProperty, "DangerBrush");
            body.Children.Add(status);
            var ok = Build.Button(readOnly ? "بستن" : "ذخیره", "primary");
            ok.HorizontalAlignment = HorizontalAlignment.Left;
            ok.Margin = new Thickness(0, 10, 0, 0);
            body.Children.Add(ok);
            var modal = Modal.Open(host, readOnly ? "اطلاعات ورود NEXA" : entry == null ? "حساب وب جدید" : "ویرایش حساب وب", body, 460);
            modal.Closed += delegate { secret.Clear(); };
            ok.Click += delegate
            {
                if (readOnly) { modal.Close(); return; }
                Uri url = WebCredential.SiteUrl(site.Text);
                string name = username.Text.Trim();
                string value = secret.Text;
                if (url == null || name.Length == 0 || value.Length == 0)
                {
                    status.Text = "نشانی HTTPS، نام کاربری و رمز عبور را وارد کنید.";
                    return;
                }
                modal.Close();
                done(new WebCredential { Site = url.AbsoluteUri, Username = name, Password = value });
            };
        }
    }
}
