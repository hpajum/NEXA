using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexa
{
    internal sealed partial class WorkspaceView
    {
        private void ShowSettings()
        {
            var body = new StackPanel();
            body.Children.Add(Build.Text("پوسته", 12.5, true, true));
            var themes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 16) };
            AddSegment(themes, "theme", "خودکار", "system", AppSettings.Theme, ThemeManager.SetTheme);
            AddSegment(themes, "theme", "روشن", "light", AppSettings.Theme, ThemeManager.SetTheme);
            AddSegment(themes, "theme", "تاریک", "dark", AppSettings.Theme, ThemeManager.SetTheme);
            body.Children.Add(themes);

            body.Children.Add(Build.Text("رنگ تأکید", 12.5, true, true));
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 18) };
            foreach (var pair in ThemeManager.Accents)
            {
                string key = pair.Key;
                var radio = new RadioButton { GroupName = "accent", Margin = new Thickness(0, 0, 10, 0), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = key, IsChecked = AppSettings.Accent == key };
                var color = (Color)ColorConverter.ConvertFromString(pair.Value);
                radio.Template = SwatchTemplate(color);
                radio.Checked += delegate { ThemeManager.SetAccent(key); };
                swatches.Children.Add(radio);
            }
            body.Children.Add(swatches);

            var animations = new CheckBox { Content = "انیمیشن‌ها", IsChecked = AppSettings.Animations, Margin = new Thickness(0, 0, 0, 14) };
            animations.SetResourceReference(StyleProperty, "ToggleSwitch");
            animations.Click += delegate { AppSettings.Animations = animations.IsChecked == true; };
            body.Children.Add(animations);

            var startup = new CheckBox { Content = "اجرای NEXA کنار ساعت هنگام ورود به ویندوز", Margin = new Thickness(0, 0, 0, 14) };
            startup.SetResourceReference(StyleProperty, "ToggleSwitch");
            try { startup.IsChecked = LocalState.StartsWithWindows(); }
            catch (Exception ex) { if (!Errors.IsExpected(ex)) throw; startup.IsEnabled = false; }
            startup.IsEnabled &= !session.Preview;
            startup.Click += delegate
            {
                try { LocalState.SetStartsWithWindows(startup.IsChecked == true); }
                catch (Exception ex)
                {
                    if (!Errors.IsExpected(ex)) throw;
                    startup.IsChecked = !startup.IsChecked;
                    Toast.Show(host, "تغییر تنظیم اجرای خودکار انجام نشد.", true);
                }
            };
            body.Children.Add(startup);

            body.Children.Add(Build.Divider());
            var server = Build.Text(session.Preview ? "سرور: حالت آزمایشی" : "سرور: " + (session.Server == null ? "تنظیم نشده" : session.Server.AbsoluteUri), 12.5, true);
            body.Children.Add(server);
            Modal.Open(host, "تنظیمات", body, 480);
        }

        private static void AddSegment(Panel panel, string group, string label, string value, string current, Action<string> apply)
        {
            var radio = new RadioButton { Content = label, GroupName = group, IsChecked = current == value };
            radio.SetResourceReference(StyleProperty, "Segment");
            radio.Checked += delegate { apply(value); };
            panel.Children.Add(radio);
        }

        private static ControlTemplate SwatchTemplate(Color color)
        {
            var template = new ControlTemplate(typeof(RadioButton));
            var grid = new FrameworkElementFactory(typeof(Grid));
            grid.SetValue(FrameworkElement.WidthProperty, 30.0);
            grid.SetValue(FrameworkElement.HeightProperty, 30.0);
            var dot = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
            dot.SetValue(System.Windows.Shapes.Shape.FillProperty, new SolidColorBrush(color));
            dot.SetValue(FrameworkElement.MarginProperty, new Thickness(4));
            var ring = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse), "Ring");
            ring.SetValue(System.Windows.Shapes.Shape.StrokeProperty, new SolidColorBrush(color));
            ring.SetValue(System.Windows.Shapes.Shape.StrokeThicknessProperty, 2.0);
            ring.SetValue(UIElement.OpacityProperty, 0.0);
            grid.AppendChild(dot);
            grid.AppendChild(ring);
            template.VisualTree = grid;
            var checkedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "Ring"));
            template.Triggers.Add(checkedTrigger);
            return template;
        }
    }
}
