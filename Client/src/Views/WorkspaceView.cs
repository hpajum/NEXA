using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexa
{
    /// <summary>Projects and chats in the sidebar, conversation and composer in the main area.</summary>
    internal sealed partial class WorkspaceView : Grid
    {
        private readonly SessionInfo session;
        private readonly Panel host;
        private readonly ListBox projects = new ListBox();
        private readonly ListBox chats = new ListBox();
        private readonly LoadingOverlay sidebarLoading = new LoadingOverlay(26);
        private readonly LoadingOverlay chatLoading = new LoadingOverlay();
        private readonly Border chatArea = new Border();
        private readonly List<string> attachments = new List<string>();
        private Button newChat;
        private Button folderButton;
        private Button themeButton;

        public event Action SignedOut;

        private Workspace State { get { return session.State; } }
        private Project ActiveProject { get { return projects.SelectedItem as Project; } }
        private Conversation ActiveChat { get { return chats.SelectedItem as Conversation; } }

        public WorkspaceView(SessionInfo session, Panel host)
        {
            this.session = session;
            this.host = host;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            ColumnDefinitions.Add(new ColumnDefinition());
            var sidebar = BuildSidebar();
            Children.Add(sidebar);
            var main = BuildMain();
            SetColumn(main, 1);
            Children.Add(main);
            ThemeManager.Changed += RefreshThemeIcon;
            Unloaded += delegate { ThemeManager.Changed -= RefreshThemeIcon; };

            foreach (var project in State.Projects) projects.Items.Add(project);
            projects.SelectionChanged += delegate { SelectProject(); };
            chats.SelectionChanged += delegate { ShowChat(); };
            if (projects.Items.Count > 0) projects.SelectedIndex = 0; else SelectProject();
        }

        // ---- Sidebar ----------------------------------------------------------------------------------

        private UIElement BuildSidebar()
        {
            var shell = new Border { BorderThickness = new Thickness(0, 0, 1, 0) };
            Res.Bind(shell, Border.BackgroundProperty, "SidebarBg");
            Res.Bind(shell, Border.BorderBrushProperty, "LineBrush");
            var grid = new Grid();
            var layout = new Grid { Margin = new Thickness(14, 16, 14, 14) };
            for (int i = 0; i < 6; i++)
                layout.RowDefinitions.Add(new RowDefinition { Height = i == 2 || i == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

            var brand = new TextBlock
            {
                Text = "NEXA", FontSize = 22, FontWeight = FontWeights.Bold, FlowDirection = FlowDirection.LeftToRight,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(6, 0, 6, 14)
            };
            Res.Bind(brand, TextBlock.ForegroundProperty, "AccentBrush");
            layout.Children.Add(brand);

            var sectionProjects = Section("پروژه‌ها", Build.IconButton(Glyph.Add, "پروژهٔ جدید", delegate { NewProject(); }));
            Grid.SetRow(sectionProjects, 1);
            layout.Children.Add(sectionProjects);
            projects.SetResourceReference(StyleProperty, "NavList");
            Grid.SetRow(projects, 2);
            layout.Children.Add(projects);

            folderButton = Build.IconTextButton(Glyph.FolderOpen, "پوشه و دسترسی", "", delegate { ShowFolder(); });
            folderButton.Margin = new Thickness(0, 8, 0, 14);
            Grid.SetRow(folderButton, 3);
            layout.Children.Add(folderButton);

            newChat = Build.IconButton(Glyph.Add, "گفتگوی جدید", delegate { NewChat(); });
            var sectionChats = Section("گفتگوها", newChat);
            Grid.SetRow(sectionChats, 4);
            layout.Children.Add(sectionChats);
            chats.SetResourceReference(StyleProperty, "NavList");
            Grid.SetRow(chats, 5);
            layout.Children.Add(chats);

            grid.Children.Add(layout);
            grid.Children.Add(sidebarLoading);
            shell.Child = grid;
            return shell;
        }

        private static UIElement Section(string title, UIElement action)
        {
            var row = new Grid { Margin = new Thickness(6, 2, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Build.Text(title, 12.5, true, true));
            Grid.SetColumn(action, 1);
            row.Children.Add(action);
            return row;
        }

        // ---- Main area --------------------------------------------------------------------------------

        private UIElement BuildMain()
        {
            var main = new Grid();
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition());

            var bar = new Grid { Margin = new Thickness(22, 12, 22, 4) };
            bar.ColumnDefinitions.Add(new ColumnDefinition());
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var who = Build.Text(session.Account, 12.5, true);
            who.VerticalAlignment = VerticalAlignment.Center;
            who.FlowDirection = FlowDirection.LeftToRight;
            who.HorizontalAlignment = HorizontalAlignment.Right;
            bar.Children.Add(who);
            var tools = new StackPanel { Orientation = Orientation.Horizontal };
            themeButton = Build.IconButton(ThemeManager.IsDark ? Glyph.Sun : Glyph.Moon, "تغییر تم", delegate { ToggleTheme(); });
            tools.Children.Add(themeButton);
            tools.Children.Add(Build.IconButton(Glyph.Lock, "مدیریت رمزها", delegate { ShowVault(); }));
            tools.Children.Add(Build.IconButton(Glyph.Settings, "تنظیمات", delegate { ShowSettings(); }));
            tools.Children.Add(Build.IconButton(Glyph.Power, "خروج از حساب", delegate { SignOut(); }));
            Grid.SetColumn(tools, 1);
            bar.Children.Add(tools);
            main.Children.Add(bar);

            var body = new Grid();
            body.Children.Add(chatArea);
            body.Children.Add(chatLoading);
            Grid.SetRow(body, 1);
            main.Children.Add(body);
            return main;
        }

        private void ToggleTheme()
        {
            ThemeManager.Toggle();
        }

        private void RefreshThemeIcon(object sender, EventArgs e)
        {
            themeButton.Content = Res.Icon(ThemeManager.IsDark ? Glyph.Sun : Glyph.Moon, 15);
            Anim.Spin((UIElement)themeButton.Content);
        }

        private void SignOut()
        {
            session.State = null;
            attachments.Clear();
            var handler = SignedOut;
            if (handler != null) handler();
        }

        // ---- Projects and chats -----------------------------------------------------------------------

        private void NewProject()
        {
            Modal.Prompt(host, "پروژهٔ جدید", "نام پروژه", "ایجاد", name =>
            {
                var item = new Project { Name = name };
                State.Projects.Add(item);
                if (!Persist()) { State.Projects.Remove(item); return; }
                projects.Items.Add(item);
                projects.SelectedItem = item;
            });
        }

        private void NewChat()
        {
            var project = ActiveProject;
            if (project == null) return;
            Modal.Prompt(host, "گفتگوی جدید", "نام گفتگو", "ایجاد", name =>
            {
                var item = new Conversation { Name = name };
                project.Chats.Add(item);
                if (!Persist()) { project.Chats.Remove(item); return; }
                chats.Items.Add(item);
                chats.SelectedItem = item;
            });
        }

        private async void SelectProject()
        {
            var project = ActiveProject;
            newChat.IsEnabled = folderButton.IsEnabled = project != null;
            sidebarLoading.Show(null);
            try { await System.Threading.Tasks.Task.Delay(Anim.Enabled ? 140 : 0); }
            finally { sidebarLoading.Hide(); }
            if (ActiveProject != project) return;
            chats.Items.Clear();
            if (project != null) foreach (var chat in project.Chats) chats.Items.Add(chat);
            if (chats.Items.Count > 0) chats.SelectedIndex = 0; else ShowChat();
        }

        private bool Persist()
        {
            try { LocalState.Save(session.StatePath, State); return true; }
            catch (Exception ex)
            {
                if (!Errors.IsExpected(ex)) throw;
                Toast.Show(host, "ذخیره روی این رایانه انجام نشد. دسترسی و فضای دیسک را بررسی کنید.", true);
                return false;
            }
        }
    }
}
