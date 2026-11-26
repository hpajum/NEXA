using System.Windows;
using System.Windows.Controls;

namespace Nexa
{
    /// <summary>Password field with a show/hide button. The text is cleared from both controls on Clear().</summary>
    internal sealed class SecretBox : Grid
    {
        private readonly PasswordBox hidden = new PasswordBox { MaxLength = 1024 };
        private readonly TextBox visible = new TextBox { MaxLength = 1024, Visibility = Visibility.Collapsed };
        private readonly Button eye;
        private bool shown;

        public SecretBox()
        {
            FlowDirection = FlowDirection.LeftToRight;
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Children.Add(hidden);
            Children.Add(visible);
            eye = Build.IconButton("\uE7B3", "نمایش یا پنهان‌کردن رمز");
            eye.Margin = new Thickness(6, 0, 0, 0);
            SetColumn(eye, 1);
            Children.Add(eye);
            eye.Click += delegate { Reveal(!shown); };
        }

        public string Text
        {
            get { return shown ? visible.Text : hidden.Password; }
            set { hidden.Password = value ?? ""; visible.Text = value ?? ""; }
        }

        public bool ReadOnly
        {
            set { visible.IsReadOnly = value; hidden.IsEnabled = !value; if (value) Reveal(true); }
        }

        public void Reveal(bool show)
        {
            if (show == shown) return;
            if (show) visible.Text = hidden.Password; else hidden.Password = visible.Text;
            shown = show;
            visible.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            hidden.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        }

        public void Clear()
        {
            hidden.Clear();
            visible.Clear();
        }

        public new bool Focus()
        {
            return shown ? visible.Focus() : hidden.Focus();
        }

        public event RoutedEventHandler EnterPressed
        {
            add
            {
                hidden.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) value(s, e); };
                visible.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) value(s, e); };
            }
            remove { }
        }
    }
}
