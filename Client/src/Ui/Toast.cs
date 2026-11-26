using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Nexa
{
    /// <summary>Non-blocking notification that slides up from the bottom of the window.</summary>
    internal static class Toast
    {
        public static void Show(Panel host, string text, bool error = false)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(16, 11, 16, 11),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 26), BorderThickness = new Thickness(1), MaxWidth = 520,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.22 }
            };
            Res.Bind(border, Border.BackgroundProperty, "SurfaceBg");
            Res.Bind(border, Border.BorderBrushProperty, error ? "DangerBrush" : "LineBrush");
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Res.Icon(error ? "\uE783" : Glyph.Check, 14);
            icon.Margin = new Thickness(0, 0, 0, 0);
            Res.Bind(icon, TextBlock.ForegroundProperty, error ? "DangerBrush" : "SuccessBrush");
            var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10, 0, 0, 0), MaxWidth = 470 };
            Res.Bind(label, TextBlock.ForegroundProperty, "InkBrush");
            row.Children.Add(icon);
            row.Children.Add(label);
            border.Child = row;
            Panel.SetZIndex(border, 200);
            if (host is Grid)
            {
                Grid.SetColumnSpan(border, Math.Max(1, ((Grid)host).ColumnDefinitions.Count));
                Grid.SetRowSpan(border, Math.Max(1, ((Grid)host).RowDefinitions.Count));
            }
            host.Children.Add(border);
            Anim.FadeIn(border, 18);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(error ? 5 : 3) };
            timer.Tick += delegate
            {
                timer.Stop();
                Anim.FadeOut(border, delegate { host.Children.Remove(border); });
            };
            timer.Start();
        }
    }
}
