using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexa
{
    /// <summary>Tiny factory for the controls the dialogs are made of.</summary>
    internal static class Build
    {
        public static TextBlock Text(string text, double size = 13.5, bool muted = false, bool bold = false)
        {
            var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            if (bold) block.FontWeight = FontWeights.Bold;
            Res.Bind(block, TextBlock.ForegroundProperty, muted ? "MutedBrush" : "InkBrush");
            return block;
        }

        /// <summary>kind: "" (neutral), "primary", "ghost".</summary>
        public static Button Button(string text, string kind = "", RoutedEventHandler click = null)
        {
            var button = new Button { Content = text, Tag = kind };
            if (click != null) button.Click += click;
            return button;
        }

        public static Button IconButton(string glyph, string tip, RoutedEventHandler click = null)
        {
            var button = new Button { Content = Res.Icon(glyph, 15), ToolTip = tip };
            button.SetResourceReference(FrameworkElement.StyleProperty, "IconButton");
            System.Windows.Automation.AutomationProperties.SetName(button, tip);
            if (click != null) button.Click += click;
            return button;
        }

        public static Button IconTextButton(string glyph, string text, string kind = "", RoutedEventHandler click = null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Res.Icon(glyph, 14));
            var label = new TextBlock { Text = text, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(label);
            var button = new Button { Content = row, Tag = kind };
            if (click != null) button.Click += click;
            return button;
        }

        /// <summary>Single-line input with a placeholder that disappears while typing.</summary>
        public static Grid Field(TextBox box, string hint)
        {
            var grid = new Grid();
            grid.Children.Add(box);
            var placeholder = new TextBlock
            {
                Text = hint, IsHitTestVisible = false, VerticalAlignment = box.AcceptsReturn ? VerticalAlignment.Top : VerticalAlignment.Center,
                Margin = new Thickness(13, box.AcceptsReturn ? 10 : 0, 13, 0)
            };
            Res.Bind(placeholder, TextBlock.ForegroundProperty, "MutedBrush");
            placeholder.Opacity = 0.8;
            grid.Children.Add(placeholder);
            Action sync = delegate { placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            box.TextChanged += delegate { sync(); };
            sync();
            return grid;
        }

        public static StackPanel Labeled(string label, UIElement input)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            var caption = Text(label, 12.5, true);
            caption.Margin = new Thickness(2, 0, 0, 6);
            stack.Children.Add(caption);
            stack.Children.Add(input);
            return stack;
        }

        public static Border Card(UIElement child, double radius = 16, string background = "SurfaceBg")
        {
            var border = new Border { CornerRadius = new CornerRadius(radius), BorderThickness = new Thickness(1), Child = child };
            Res.Bind(border, Border.BackgroundProperty, background);
            Res.Bind(border, Border.BorderBrushProperty, "LineBrush");
            return border;
        }

        public static Border Divider()
        {
            var line = new Border { Height = 1, Margin = new Thickness(0, 10, 0, 10) };
            Res.Bind(line, Border.BackgroundProperty, "LineBrush");
            return line;
        }

        public static Grid Row(params UIElement[] children)
        {
            var grid = new Grid();
            foreach (var child in children)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(child, grid.ColumnDefinitions.Count - 1);
                grid.Children.Add(child);
            }
            return grid;
        }
    }
}
