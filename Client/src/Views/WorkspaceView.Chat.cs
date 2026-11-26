using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Nexa
{
    internal sealed partial class WorkspaceView
    {
        private StackPanel messageList;
        private ScrollViewer scroller;
        private StackPanel chips;
        private TextBox draft;

        private void ShowChat()
        {
            attachments.Clear();
            var chat = ActiveChat;
            var layout = new Grid { Margin = new Thickness(22, 4, 22, 20) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new StackPanel { Margin = new Thickness(2, 0, 0, 12) };
            title.Children.Add(Build.Text(chat == null ? "فضای کار" : chat.Name, 22, false, true));
            if (chat != null) title.Children.Add(Build.Text(ActiveProject.Name + "  /  گفتگو روی این رایانه", 12.5, true));
            layout.Children.Add(title);

            if (chat == null)
            {
                var empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                var icon = Res.Icon(Glyph.Chat, 40);
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                Res.Bind(icon, TextBlock.ForegroundProperty, "AccentBrush");
                empty.Children.Add(icon);
                var text = Build.Text(ActiveProject == null ? "برای شروع، یک پروژه بسازید." : "یک گفتگو برای این پروژه بسازید.", 15, true);
                text.Margin = new Thickness(0, 14, 0, 0);
                text.TextAlignment = TextAlignment.Center;
                empty.Children.Add(text);
                Grid.SetRow(empty, 1);
                layout.Children.Add(empty);
                SetChatArea(layout);
                return;
            }

            messageList = new StackPanel();
            scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = messageList, Padding = new Thickness(0, 0, 6, 0) };
            Grid.SetRow(scroller, 1);
            layout.Children.Add(scroller);
            foreach (var message in chat.Messages) AddBubble(message, false);

            var composer = Build.Card(BuildComposer(chat), 16);
            composer.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(composer, 2);
            layout.Children.Add(composer);
            SetChatArea(layout);
            draft.Focus();
        }

        private void SetChatArea(UIElement content)
        {
            chatArea.Child = content;
            Anim.FadeIn(content, 10);
        }

        private UIElement BuildComposer(Conversation chat)
        {
            var box = new StackPanel { Margin = new Thickness(10) };
            draft = new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56, MaxHeight = 160, MaxLength = 16000,
                BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            box.Children.Add(Build.Field(draft, "پیام خود را بنویسید…"));
            chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 4, 4, 0) };
            var chipScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = chips, Visibility = Visibility.Collapsed };
            box.Children.Add(chipScroll);

            var row = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var send = Build.IconTextButton(Glyph.Send, "ثبت پیام", "primary");
            var attach = Build.IconButton(Glyph.Attach, "افزودن پیوست", delegate { ChooseAttachments(chipScroll); });
            row.Children.Add(send);
            attach.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(attach, 1);
            row.Children.Add(attach);
            box.Children.Add(row);

            Action submit = delegate
            {
                string text = draft.Text.Trim();
                if (text.Length == 0 && attachments.Count == 0) return;
                var message = new Message { Text = text, Files = attachments.ToArray() };
                chat.Messages.Add(message);
                if (!Persist()) { chat.Messages.Remove(message); return; }
                draft.Clear();
                attachments.Clear();
                RefreshChips(chipScroll);
                AddBubble(message, true);
            };
            send.Click += delegate { submit(); };
            draft.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; submit(); }
            };
            return box;
        }

        private void AddBubble(Message message, bool animate)
        {
            if (!string.IsNullOrEmpty(message.Text))
            {
                var bubble = new Border
                {
                    CornerRadius = new CornerRadius(16, 16, 4, 16), Padding = new Thickness(16, 11, 16, 11), MaxWidth = 640,
                    HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 4), Cursor = Cursors.Arrow
                };
                Res.Bind(bubble, Border.BackgroundProperty, "AccentBrush");
                var text = new TextBlock { Text = message.Text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = 14 };
                bubble.Child = text;
                messageList.Children.Add(bubble);
                if (animate) Anim.FadeIn(bubble, 12);
            }
            foreach (string path in message.Files ?? new string[0])
            {
                var chip = new Button { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 4), MaxWidth = 420 };
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                var icon = Res.Icon(Glyph.Document, 15);
                Res.Bind(icon, TextBlock.ForegroundProperty, "AccentBrush");
                row.Children.Add(icon);
                row.Children.Add(new TextBlock { Text = Path.GetFileName(path), Margin = new Thickness(8, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
                chip.Content = row;
                string captured = path;
                chip.Click += delegate { ShowAttachment(captured, true); };
                messageList.Children.Add(chip);
                if (animate) Anim.FadeIn(chip, 12);
            }
            scroller.ScrollToEnd();
        }

        private void ChooseAttachments(ScrollViewer chipScroll)
        {
            var picker = new OpenFileDialog { Title = "انتخاب فایل‌های پیوست", Multiselect = true };
            if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
            foreach (string path in picker.FileNames)
                if (!attachments.Contains(path, StringComparer.OrdinalIgnoreCase)) attachments.Add(path);
            RefreshChips(chipScroll);
        }

        private void RefreshChips(ScrollViewer chipScroll)
        {
            chips.Children.Clear();
            foreach (string path in attachments)
            {
                string captured = path;
                var chip = Build.IconTextButton(Glyph.Close, Path.GetFileName(path), "", delegate
                {
                    attachments.Remove(captured);
                    RefreshChips(chipScroll);
                });
                chip.MinHeight = 32;
                chip.Margin = new Thickness(0, 0, 6, 0);
                chip.ToolTip = "برداشتن پیوست: " + path;
                chips.Children.Add(chip);
            }
            chipScroll.Visibility = attachments.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
    }
}
