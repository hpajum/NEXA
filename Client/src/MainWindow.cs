using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Nexa
{
    internal sealed partial class MainWindow : Form
    {
        private static readonly Color Canvas = WorkspaceColors.Background;
        private static readonly Color Ink = WorkspaceColors.Ink;
        private static readonly Color Accent = WorkspaceColors.Accent;

        private readonly bool preview;
        private string statePath;
        private string vaultPath;
        private string savedLoginPath;
        private Uri sessionServer;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly List<string> attachments = new List<string>();
        private Workspace state;
        private string account;
        private bool quitting;

        private ListBox projects;
        private ListBox chats;
        private Panel chatPanel;
        private Button newChat;
        private Button folderButton;
        private TextBox draft;
        private ListBox draftFiles;
        private TranscriptView history;

        public MainWindow(bool preview)
        {
            this.preview = preview;
            Text = preview ? "NEXA | آزمایشی" : "NEXA";
            Font = AppTypography.Body;
            ForeColor = Ink;
            BackColor = Canvas;
            RightToLeft = RightToLeft.Yes;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1100, 670);
            MinimumSize = new Size(755, 570);
            AutoScaleMode = AutoScaleMode.Dpi;
        }

        public void Reveal()
        {
            if (Controls.Count == 0) ShowLogin();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
        }

        public void Quit()
        {
            quitting = true;
            lifetime.Cancel();
            if (IsHandleCreated) Close();
            else Application.ExitThread();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!quitting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            lifetime.Cancel();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) lifetime.Dispose();
            base.Dispose(disposing);
        }

        private Project ActiveProject { get { return projects.SelectedItem as Project; } }
        private Conversation ActiveChat { get { return chats.SelectedItem as Conversation; } }

        private void ShowLogin()
        {
            ClearControls(this);
            Uri server = preview ? null : CompanyLogin.Server();
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 696));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, preview ? 324 : 475));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            var landing = Columns(222, -1);
            grid.Controls.Add(landing, 1, 1);
            var brand = Stack(22, WorkspaceColors.Navy, -1, 54, 49, -1);
            var name = Heading("NEXA", 27);
            var description = LabelFor("فضای کار سازمانی");
            name.ForeColor = description.ForeColor = WorkspaceColors.SidebarText;
            brand.Controls.Add(name, 0, 1);
            brand.Controls.Add(description, 0, 2);
            landing.Controls.Add(brand, 0, 0);
            var card = preview ? Stack(29, Color.White, 16, 51, 53, 48, 64) :
                Stack(26, Color.White, 14, 49, 43, 30, 42, 29, 43, 53, 70);
            landing.Controls.Add(card, 1, 0);
            Controls.Add(grid);

            card.Controls.Add(Heading(preview ? "فضای کار آزمایشی" : "ورود سازمانی", 18), 0, 1);
            var serverHint = LabelFor(preview ? "داده‌ها در فضای آزمایشی همین رایانه ذخیره می‌شوند." :
                server == null ? "آدرس سرور سازمان تنظیم نشده یا معتبر نیست." : server.AbsoluteUri);
            if (!preview && server != null)
            {
                serverHint.RightToLeft = RightToLeft.No;
                serverHint.TextAlign = ContentAlignment.MiddleRight;
            }
            card.Controls.Add(serverHint, 0, 2);
            var signIn = ActionButton(preview ? "باز کردن فضای آزمایشی" : "ورود و ذخیره روی این رایانه", true);
            var status = LabelFor("");
            status.ForeColor = WorkspaceColors.Muted;
            var username = Input("نام کاربری");
            var password = PasswordInput();
            var reuse = ActionButton("ورود ذخیره‌شده");
            var actions = Columns(-1, 162);
            string storedUsername = null;
            string loginPath = server == null ? null : Path.Combine(LocalState.AccountDirectory(server, null), "login.bin");
            if (preview)
            {
                username.Dispose();
                password.Dispose();
                reuse.Dispose();
                actions.Dispose();
                card.Controls.Add(signIn, 0, 3);
                card.Controls.Add(status, 0, 4);
            }
            else
            {
                username.RightToLeft = RightToLeft.No;
                card.Controls.Add(LabelFor("نام کاربری"), 0, 3);
                card.Controls.Add(username, 0, 4);
                card.Controls.Add(LabelFor("رمز عبور"), 0, 5);
                card.Controls.Add(password, 0, 6);
                card.Controls.Add(actions, 0, 7);
                actions.Controls.Add(signIn, 0, 0);
                actions.Controls.Add(reuse, 1, 0);
                card.Controls.Add(status, 0, 8);
                try
                {
                    if (loginPath != null)
                    {
                        var lastLogin = LocalState.Load<SavedLogin>(loginPath);
                        if (lastLogin.IsForServer(server)) storedUsername = lastLogin.Username;
                    }
                }
                catch (Exception ex)
                {
                    if (!IsExpected(ex)) throw;
                    status.Text = "ورود ذخیره‌شده قابل خواندن نیست. نام کاربری و رمز را وارد کنید.";
                }
                signIn.Enabled = server != null;
                reuse.Enabled = storedUsername != null;
                if (storedUsername != null) username.Text = storedUsername;
            }
            AcceptButton = signIn;

            Action<bool> enableForm = enabled =>
            {
                signIn.Enabled = enabled && (preview || server != null);
                if (!preview)
                {
                    username.Enabled = password.Enabled = enabled;
                    reuse.Enabled = enabled && storedUsername != null;
                }
            };
            Func<bool, System.Threading.Tasks.Task> enter = async useStored =>
            {
                enableForm(false);
                status.Text = "در حال ورود…";
                try
                {
                    var stored = useStored ? LocalState.Load<SavedLogin>(loginPath) : null;
                    if (useStored && !stored.IsForServer(server))
                    {
                        storedUsername = null;
                        throw new LoginException("ورود ذخیره‌شده موجود نیست. نام کاربری و رمز را وارد کنید.");
                    }
                    string loginName = preview ? null : useStored ? stored.Username : username.Text.Trim();
                    string secret = preview ? null : useStored ? stored.Password : password.Text;
                    if (!preview && (String.IsNullOrWhiteSpace(loginName) || String.IsNullOrEmpty(secret)))
                        throw new LoginException("نام کاربری و رمز را وارد کنید.");
                    string confirmed = preview ? "کاربر آزمایشی" :
                        await CompanyLogin.Authenticate(server, loginName, secret, lifetime.Token);
                    if (quitting || IsDisposed) return;
                    if (useStored && !String.Equals(confirmed, stored.Account, StringComparison.OrdinalIgnoreCase))
                        throw new LoginException("هویت حساب ذخیره‌شده تغییر کرده است. با نام کاربری و رمز وارد شوید.");
                    string directory = LocalState.AccountDirectory(server, confirmed);
                    string workspacePath = Path.Combine(directory, "workspace.bin");
                    var workspace = LocalState.Load<Workspace>(workspacePath);
                    if (workspace.Projects == null)
                        throw new System.Runtime.Serialization.SerializationException("Invalid workspace.");
                    if (!preview && !useStored)
                    {
                        try
                        {
                            LocalState.Save(loginPath, new SavedLogin
                            {
                                Server = server.AbsoluteUri.TrimEnd('/'), Account = confirmed, Username = loginName, Password = secret
                            });
                        }
                        catch (Exception ex)
                        {
                            if (!IsExpected(ex)) throw;
                            throw new LoginException("سرور ورود را پذیرفت، اما ذخیرهٔ رمز روی این رایانه انجام نشد.");
                        }
                    }
                    if (!preview) password.Clear();
                    statePath = workspacePath;
                    vaultPath = Path.Combine(directory, "credentials.bin");
                    savedLoginPath = loginPath;
                    sessionServer = server;
                    state = workspace;
                    account = confirmed;
                    ShowWorkspace();
                }
                catch (LoginException ex) { if (!IsDisposed) status.Text = ex.Message; }
                catch (Exception ex)
                {
                    if (!IsExpected(ex)) throw;
                    if (!IsDisposed)
                    {
                        bool connection = ex is System.Net.Http.HttpRequestException || ex is OperationCanceledException;
                        status.Text = connection ? "ارتباط امن با سرور برقرار نشد. اتصال و گواهی سرور را بررسی کنید." :
                            "ورود انجام نشد. پاسخ سرور یا دادهٔ ذخیره‌شده قابل خواندن نیست.";
                    }
                }
                finally
                {
                    if (!signIn.IsDisposed)
                    {
                        if (!preview) password.Clear();
                        enableForm(true);
                    }
                }
            };
            signIn.Click += async delegate { await enter(false); };
            if (!preview) reuse.Click += async delegate { await enter(true); };
        }

        private void ShowWorkspace()
        {
            AcceptButton = null;
            ClearControls(this);
            var frame = Stack(12, Canvas, 61, -1);
            Controls.Add(frame);
            var header = Columns(104, -1, 147, 104, 79);
            header.Padding = new Padding(13, 3, 13, 6);
            header.BackColor = WorkspaceColors.Paper;
            header.Margin = new Padding(0, 0, 0, 9);
            frame.Controls.Add(header, 0, 0);
            var wordmark = Heading("NEXA", 17);
            wordmark.ForeColor = Accent;
            header.Controls.Add(wordmark, 0, 0);
            var identity = LabelFor(account);
            identity.ForeColor = WorkspaceColors.Muted;
            header.Controls.Add(identity, 1, 0);
            var settings = ActionButton("تنظیمات");
            var signOut = ActionButton("خروج");
            var vault = ActionButton("مدیریت رمزها");
            header.Controls.Add(vault, 2, 0);
            header.Controls.Add(settings, 3, 0);
            header.Controls.Add(signOut, 4, 0);
            vault.Click += delegate { ShowVault(); };
            settings.Click += delegate { ShowSettings(); };
            signOut.Click += delegate
            {
                account = null;
                state = null;
                vaultPath = savedLoginPath = statePath = null;
                sessionServer = null;
                attachments.Clear();
                ShowLogin();
            };

            var content = Columns(254, -1);
            frame.Controls.Add(content, 0, 1);
            var sidebar = Stack(15, WorkspaceColors.Navy, 36, 42, -1, 46, 38, 42, -1);
            content.Controls.Add(sidebar, 0, 0);
            var projectHeading = Heading("پروژه‌ها", 13);
            projectHeading.ForeColor = WorkspaceColors.SidebarText;
            sidebar.Controls.Add(projectHeading, 0, 0);
            var newProject = ActionButton("پروژهٔ جدید", true);
            sidebar.Controls.Add(newProject, 0, 1);
            projects = new NavigationList { AccessibleName = "پروژه‌ها" };
            sidebar.Controls.Add(projects, 0, 2);
            folderButton = ActionButton("پوشه و سطح دسترسی");
            sidebar.Controls.Add(folderButton, 0, 3);
            var chatHeading = Heading("گفتگوها", 13);
            chatHeading.ForeColor = WorkspaceColors.SidebarText;
            sidebar.Controls.Add(chatHeading, 0, 4);
            newChat = ActionButton("گفتگوی جدید");
            sidebar.Controls.Add(newChat, 0, 5);
            chats = new NavigationList { AccessibleName = "گفتگوها" };
            sidebar.Controls.Add(chats, 0, 6);
            chatPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, 10, 0) };
            content.Controls.Add(chatPanel, 1, 0);

            projects.SelectedIndexChanged += delegate { SelectProject(); };
            chats.SelectedIndexChanged += delegate { ShowChat(); };
            newProject.Click += delegate
            {
                string name = AskName("پروژهٔ جدید", "نام پروژه");
                if (name == null) return;
                var item = new Project { Name = name };
                state.Projects.Add(item);
                if (!Persist()) { state.Projects.Remove(item); return; }
                projects.Items.Add(item);
                projects.SelectedItem = item;
            };
            newChat.Click += delegate
            {
                var project = ActiveProject;
                if (project == null) return;
                string name = AskName("گفتگوی جدید", "نام گفتگو");
                if (name == null) return;
                var item = new Conversation { Name = name };
                project.Chats.Add(item);
                if (!Persist()) { project.Chats.Remove(item); return; }
                chats.Items.Add(item);
                chats.SelectedItem = item;
            };
            folderButton.Click += delegate { ShowFolder(); };
            foreach (var project in state.Projects) projects.Items.Add(project);
            if (projects.Items.Count > 0) projects.SelectedIndex = 0;
            else SelectProject();
        }

        private void SelectProject()
        {
            var project = ActiveProject;
            chats.Items.Clear();
            newChat.Enabled = folderButton.Enabled = project != null;
            if (project != null)
                foreach (var chat in project.Chats) chats.Items.Add(chat);
            if (chats.Items.Count > 0) chats.SelectedIndex = 0;
            else ShowChat();
        }

        private void ShowChat()
        {
            attachments.Clear();
            ClearControls(chatPanel);
            var chat = ActiveChat;
            var body = Stack(22, Color.White, 51, 36, -1, 175);
            chatPanel.Controls.Add(body);
            body.Controls.Add(Heading(chat == null ? "فضای کار" : chat.Name, 17), 0, 0);
            if (chat == null)
            {
                body.Controls.Add(LabelFor(ActiveProject == null ? "برای شروع یک پروژه بسازید." :
                    "یک گفتگو برای این پروژه بسازید."), 0, 1);
                return;
            }
            var chatInfo = LabelFor(ActiveProject.Name + "  /  گفتگو روی این رایانه");
            chatInfo.ForeColor = WorkspaceColors.Muted;
            body.Controls.Add(chatInfo, 0, 1);
            history = new TranscriptView { AccessibleName = "تاریخچهٔ گفتگو" };
            body.Controls.Add(history, 0, 2);
            var composer = Stack(10, Canvas, 27, -1, 0, 42);
            body.Controls.Add(composer, 0, 3);
            var prompt = LabelFor("متن پیام");
            prompt.ForeColor = WorkspaceColors.Muted;
            composer.Controls.Add(prompt, 0, 0);
            draft = Input("متن پیام", true);
            composer.Controls.Add(draft, 0, 1);
            draftFiles = List();
            draftFiles.AccessibleName = "پیوست‌های در انتظار ثبت";
            composer.Controls.Add(draftFiles, 0, 2);
            draftFiles.VisibleChanged += delegate { composer.RowStyles[2].Height = draftFiles.Visible ? 48 : 0; };
            var actions = Columns(102, 121, 134, -1);
            composer.Controls.Add(actions, 0, 3);
            var save = ActionButton("ثبت پیام", true);
            var attach = ActionButton("افزودن پیوست");
            var remove = ActionButton("برداشتن پیوست");
            actions.Controls.Add(save, 0, 0);
            actions.Controls.Add(attach, 1, 0);
            actions.Controls.Add(remove, 2, 0);
            draftFiles.Visible = false;
            remove.Enabled = false;
            attach.Click += delegate { ChooseAttachments(); };
            remove.Click += delegate
            {
                if (draftFiles.SelectedIndex < 0) return;
                attachments.RemoveAt(draftFiles.SelectedIndex);
                RefreshDraftFiles();
            };
            draftFiles.SelectedIndexChanged += delegate { remove.Enabled = draftFiles.SelectedIndex >= 0; };
            save.Click += delegate
            {
                string value = draft.Text.Trim();
                if (value.Length == 0 && attachments.Count == 0) return;
                var message = new Message { Text = value, Files = attachments.ToArray() };
                chat.Messages.Add(message);
                if (!Persist()) { chat.Messages.Remove(message); return; }
                draft.Clear();
                attachments.Clear();
                RefreshDraftFiles();
                AddMessage(message);
            };
            Action openHistoryItem = delegate
            {
                var file = history.SelectedItem as FileEntry;
                if (file != null) ShowAttachment(file.Path);
                else if (history.SelectedItem is MessageEntry) ShowMessage(((MessageEntry)history.SelectedItem).Text);
            };
            history.DoubleClick += delegate { openHistoryItem(); };
            history.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                openHistoryItem();
            };
            foreach (var message in chat.Messages) AddMessage(message);
        }

        private void AddMessage(Message message)
        {
            if (message.Text.Length > 0) history.Append(new MessageEntry(message.Text));
            foreach (string file in message.Files) history.Append(new FileEntry(file, "پیوست · "));
            if (history.Items.Count > 0) history.TopIndex = history.Items.Count - 1;
        }

        private void ShowMessage(string text)
        {
            using (var dialog = Dialog("متن پیام", 520, 300))
            {
                var field = Input("متن پیام", true);
                field.Text = text;
                field.ReadOnly = true;
                dialog.Controls.Add(field);
                dialog.ShowDialog(this);
            }
        }

        private void ChooseAttachments()
        {
            using (var picker = new OpenFileDialog { Title = "انتخاب فایل‌های پیوست", Multiselect = true })
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                foreach (string path in picker.FileNames)
                    if (!attachments.Contains(path, StringComparer.OrdinalIgnoreCase)) attachments.Add(path);
                RefreshDraftFiles();
            }
        }

        private void RefreshDraftFiles()
        {
            draftFiles.Items.Clear();
            foreach (string path in attachments) draftFiles.Items.Add(new FileEntry(path));
            draftFiles.Visible = attachments.Count != 0;
        }

        private bool Persist()
        {
            try { LocalState.Save(statePath, state); return true; }
            catch (Exception ex)
            {
                if (!IsExpected(ex)) throw;
                Tell("ذخیره روی این رایانه انجام نشد. دسترسی و فضای دیسک را بررسی کنید.");
                return false;
            }
        }

        private void ShowSettings()
        {
            using (var dialog = Dialog("تنظیمات", 490, 235))
            {
                var layout = Stack(18, Canvas, 44, 48, 42, 42);
                dialog.Controls.Add(layout);
                layout.Controls.Add(Heading("تنظیمات NEXA", 15), 0, 0);
                var startup = new CheckBox { Text = "اجرای NEXA کنار ساعت هنگام ورود به ویندوز", Dock = DockStyle.Fill };
                try { startup.Checked = LocalState.StartsWithWindows(); }
                catch (Exception ex) { if (!IsExpected(ex)) throw; startup.Enabled = false; }
                startup.Enabled &= !preview;
                layout.Controls.Add(startup, 0, 1);
                var server = CompanyLogin.Server();
                layout.Controls.Add(LabelFor(preview ? "سرور: حالت آزمایشی" :
                    "سرور: " + (server == null ? "تنظیم نشده" : server.AbsoluteUri)), 0, 2);
                var done = ActionButton("بستن");
                layout.Controls.Add(done, 0, 3);
                done.Click += delegate { dialog.Close(); };
                startup.CheckedChanged += delegate
                {
                    try { LocalState.SetStartsWithWindows(startup.Checked); }
                    catch (Exception ex)
                    {
                        if (!IsExpected(ex)) throw;
                        dialog.Close();
                        Tell("تغییر تنظیم اجرای خودکار انجام نشد.");
                    }
                };
                dialog.ShowDialog(this);
            }
        }

        private string AskName(string title, string label)
        {
            using (var dialog = Dialog(title, 410, 165))
            {
                var layout = Stack(16, Canvas, 35, 33, 42);
                var input = Input(label);
                input.MaxLength = 80;
                var okay = ActionButton("ایجاد", true);
                okay.Enabled = false;
                input.TextChanged += delegate { okay.Enabled = input.Text.Trim().Length > 0; };
                okay.DialogResult = DialogResult.OK;
                layout.Controls.Add(LabelFor(label), 0, 0);
                layout.Controls.Add(input, 0, 1);
                layout.Controls.Add(okay, 0, 2);
                dialog.Controls.Add(layout);
                dialog.AcceptButton = okay;
                dialog.Shown += delegate { input.Focus(); };
                return dialog.ShowDialog(this) == DialogResult.OK ? input.Text.Trim() : null;
            }
        }

        private Form Dialog(string title, int width, int height)
        {
            return new Form
            {
                Text = title, Font = Font, Icon = Icon, ForeColor = Ink, BackColor = Canvas,
                ClientSize = new Size(width, height), StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                ShowInTaskbar = false, RightToLeft = RightToLeft.Yes, AutoScaleMode = AutoScaleMode.Dpi
            };
        }

        private DialogResult Tell(string text, MessageBoxButtons buttons = MessageBoxButtons.OK, IWin32Window owner = null)
        {
            return MessageBox.Show(owner ?? this, text, "NEXA", buttons, MessageBoxIcon.Information,
                buttons == MessageBoxButtons.YesNo ? MessageBoxDefaultButton.Button2 : MessageBoxDefaultButton.Button1,
                MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading);
        }

        private static bool IsExpected(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException ||
                ex is System.Security.Cryptography.CryptographicException || ex is System.Runtime.Serialization.SerializationException ||
                ex is System.Net.Http.HttpRequestException || ex is OperationCanceledException ||
                ex is System.ComponentModel.Win32Exception || ex is ArgumentException || ex is NotSupportedException;
        }

        private static TableLayoutPanel Stack(int padding, Color background, params int[] heights)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = heights.Length, Margin = Padding.Empty,
                Padding = new Padding(padding), BackColor = background, GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in heights)
                grid.RowStyles.Add(height < 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
            return grid;
        }

        private static TableLayoutPanel Columns(params int[] widths)
        {
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = widths.Length, RowCount = 1, Margin = Padding.Empty,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (int width in widths)
                grid.ColumnStyles.Add(width < 0 ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.Absolute, width));
            return grid;
        }

        private static Button ActionButton(string text, bool primary = false)
        {
            return new WorkspaceButton(primary)
            {
                Text = text, Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 4),
                BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink,
                Cursor = Cursors.Hand
            };
        }

        private static Label LabelFor(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        }

        private static Label Heading(string text, float size)
        {
            var label = LabelFor(text);
            Font font = AppTypography.Create(size, FontStyle.Bold);
            label.Font = font;
            label.Disposed += delegate { font.Dispose(); };
            return label;
        }

        private static TextBox Input(string name, bool multiline = false)
        {
            return new TextBox
            {
                AccessibleName = name, Dock = DockStyle.Fill, Multiline = multiline,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None,
                BorderStyle = BorderStyle.FixedSingle, BackColor = WorkspaceColors.Paper, ForeColor = Ink,
                MaxLength = multiline ? 16000 : 260, Margin = new Padding(3, 4, 3, 4)
            };
        }

        private static ListBox List()
        {
            return new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false,
                HorizontalScrollbar = true, BackColor = Canvas
            };
        }

        private static void ClearControls(Control parent)
        {
            while (parent.Controls.Count > 0) parent.Controls[0].Dispose();
        }

        private sealed class MessageEntry
        {
            public readonly string Text;
            public MessageEntry(string text) { Text = text; }
            public override string ToString() { return Text.Replace("\r", "").Replace("\n", " "); }
        }

        private sealed class FileEntry
        {
            public readonly string Path;
            private readonly string title;
            public FileEntry(string path, string prefix = "")
            {
                Path = path;
                title = prefix + System.IO.Path.GetFileName(path);
            }
            public override string ToString() { return title; }
        }
    }
}
