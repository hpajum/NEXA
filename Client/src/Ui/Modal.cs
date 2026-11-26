using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Nexa
{
    /// <summary>In-window dialog: dimmed backdrop, card that scales in, Esc or the close button to dismiss.</summary>
    internal sealed class Modal
    {
        private readonly Panel host;
        private readonly Grid scrim = new Grid { Focusable = true };
        private readonly Border card;
        private bool closed;

        public event EventHandler Closed;

        private Modal(Panel host, string title, UIElement body, double width)
        {
            this.host = host;
            scrim.Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));
            Panel.SetZIndex(scrim, 150);
            if (host is Grid)
            {
                Grid.SetColumnSpan(scrim, Math.Max(1, ((Grid)host).ColumnDefinitions.Count));
                Grid.SetRowSpan(scrim, Math.Max(1, ((Grid)host).RowDefinitions.Count));
            }

            var layout = new Grid { Margin = new Thickness(22, 16, 22, 22) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var header = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(Build.Text(title, 17, false, true));
            var close = Build.IconButton(Glyph.Close, "بستن", delegate { Close(); });
            Grid.SetColumn(close, 1);
            header.Children.Add(close);
            layout.Children.Add(header);
            Grid.SetRow(body, 1);
            layout.Children.Add(body);

            card = new Border
            {
                Width = width, MaxHeight = 640, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Child = layout, SnapsToDevicePixels = true,
                Effect = new DropShadowEffect { BlurRadius = 40, ShadowDepth = 8, Opacity = 0.35 }
            };
            Res.Bind(card, Border.BackgroundProperty, "SurfaceBg");
            Res.Bind(card, Border.BorderBrushProperty, "LineBrush");
            scrim.Children.Add(card);
            scrim.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.OriginalSource == scrim) Close();
            };
            scrim.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            };
        }

        public static Modal Open(Panel host, string title, UIElement body, double width = 480)
        {
            var modal = new Modal(host, title, body, width);
            host.Children.Add(modal.scrim);
            Anim.FadeIn(modal.scrim, 0);
            Anim.Scale(modal.card, 0.94, 1, 260);
            modal.scrim.Focus();
            return modal;
        }

        public void Close()
        {
            if (closed) return;
            closed = true;
            Anim.FadeOut(scrim, delegate
            {
                host.Children.Remove(scrim);
                var handler = Closed;
                if (handler != null) handler(this, EventArgs.Empty);
            });
        }

        public static void Confirm(Panel host, string title, string text, string yes, Action onYes)
        {
            var body = new StackPanel();
            body.Children.Add(Build.Text(text, 14));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
            Modal modal = null;
            actions.Children.Add(Build.Button(yes, "primary", delegate { modal.Close(); onYes(); }));
            actions.Children.Add(Build.Button("انصراف", "ghost", delegate { modal.Close(); }));
            foreach (UIElement child in actions.Children) ((FrameworkElement)child).Margin = new Thickness(0, 0, 0, 0);
            ((FrameworkElement)actions.Children[1]).Margin = new Thickness(8, 0, 0, 0);
            body.Children.Add(actions);
            modal = Open(host, title, body, 420);
        }

        public static void Prompt(Panel host, string title, string label, string action, Action<string> onValue)
        {
            var body = new StackPanel();
            var input = new TextBox { MaxLength = 80 };
            body.Children.Add(Build.Field(input, label));
            var ok = Build.Button(action, "primary");
            ok.IsEnabled = false;
            ok.Margin = new Thickness(0, 18, 0, 0);
            ok.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(ok);
            Modal modal = Open(host, title, body, 400);
            input.TextChanged += delegate { ok.IsEnabled = input.Text.Trim().Length > 0; };
            Action submit = delegate
            {
                string value = input.Text.Trim();
                if (value.Length == 0) return;
                modal.Close();
                onValue(value);
            };
            ok.Click += delegate { submit(); };
            input.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) submit(); };
            input.Loaded += delegate { input.Focus(); };
        }
    }
}
