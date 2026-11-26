using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexa
{
    internal sealed class LoginView : Grid
    {
        private readonly bool preview;
        private readonly CancellationToken cancellation;
        private readonly Uri server;
        private readonly string loginPath;
        private readonly TextBox username = new TextBox { FlowDirection = FlowDirection.LeftToRight };
        private readonly SecretBox password = new SecretBox();
        private readonly Button signIn;
        private readonly Button reuse;
        private readonly TextBlock status;
        private readonly LoadingOverlay loading = new LoadingOverlay();
        private string storedUsername;

        public event Action<SessionInfo> SignedIn;

        public LoginView(bool preview, CancellationToken cancellation)
        {
            this.preview = preview;
            this.cancellation = cancellation;
            server = preview ? null : CompanyLogin.Server();
            loginPath = server == null ? null : Path.Combine(LocalState.AccountDirectory(server, null), "login.bin");

            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5, GridUnitType.Star), MinWidth = 340 });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Star) });
            Children.Add(BuildBrandPanel());

            signIn = Build.Button(preview ? "باز کردن فضای آزمایشی" : "ورود و ذخیره روی این رایانه", "primary");
            signIn.Height = 44;
            reuse = Build.Button("ورود ذخیره‌شده");
            reuse.Height = 44;
            status = Build.Text("", 12.5, true);
            status.Margin = new Thickness(2, 14, 0, 0);

            var form = BuildForm();
            SetColumn(form, 1);
            Children.Add(form);
            SetColumnSpan(loading, 2);
            Children.Add(loading);

            LoadStoredLogin();
            signIn.Click += async delegate { await Enter(false); };
            reuse.Click += async delegate { await Enter(true); };
            username.KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e) { if (e.Key == System.Windows.Input.Key.Enter) password.Focus(); };
            password.EnterPressed += async delegate { await Enter(false); };
            Loaded += delegate { if (!preview) { if (string.IsNullOrEmpty(username.Text)) username.Focus(); else password.Focus(); } };
        }

        private UIElement BuildBrandPanel()
        {
            var panel = new Grid { ClipToBounds = true };
            panel.SetResourceReference(BackgroundProperty, "AccentGradient");
            // Soft drifting rings: the one decorative moment on this screen.
            var rings = new Canvas { IsHitTestVisible = false, FlowDirection = FlowDirection.LeftToRight };
            for (int i = 0; i < 4; i++)
            {
                double size = 220 + i * 150;
                var ring = new System.Windows.Shapes.Ellipse
                {
                    Width = size, Height = size, StrokeThickness = 1.2,
                    Stroke = new SolidColorBrush(Color.FromArgb((byte)(70 - i * 14), 255, 255, 255))
                };
                Canvas.SetLeft(ring, -size / 2 + 40);
                Canvas.SetTop(ring, 560 - size / 2);
                ring.RenderTransformOrigin = new Point(0.5, 0.5);
                var scale = new ScaleTransform(1, 1);
                ring.RenderTransform = scale;
                if (Anim.Enabled)
                {
                    var pulse = new System.Windows.Media.Animation.DoubleAnimation(1, 1.06, TimeSpan.FromSeconds(4 + i))
                    {
                        AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                        EasingFunction = new System.Windows.Media.Animation.SineEase()
                    };
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
                }
                rings.Children.Add(ring);
            }
            panel.Children.Add(rings);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(56, 0, 56, 0) };
            var mark = new TextBlock
            {
                Text = "NEXA", FontSize = 56, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Right
            };
            var tagline = new TextBlock
            {
                Text = "فضای کار هوشمند سازمان", FontSize = 20, Foreground = Brushes.White, Margin = new Thickness(0, 6, 0, 0)
            };
            var detail = new TextBlock
            {
                Text = "پروژه‌ها، گفتگوها و فایل‌های شما، امن روی همین رایانه.", FontSize = 14, TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromArgb(215, 255, 255, 255)), Margin = new Thickness(0, 14, 0, 0), MaxWidth = 340
            };
            text.Children.Add(mark);
            text.Children.Add(tagline);
            text.Children.Add(detail);
            panel.Children.Add(text);
            return panel;
        }

        private UIElement BuildForm()
        {
            var card = new StackPanel { Width = 380, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            card.Children.Add(Build.Text(preview ? "فضای کار آزمایشی" : "ورود سازمانی", 26, false, true));
            string hint = preview ? "داده‌ها در فضای آزمایشی همین رایانه ذخیره می‌شوند." :
                server == null ? "نشانی سرور سازمان تنظیم نشده یا معتبر نیست. با مدیر سیستم تماس بگیرید." : server.AbsoluteUri;
            var hintText = Build.Text(hint, 13, true);
            hintText.Margin = new Thickness(0, 6, 0, 26);
            if (!preview && server != null) { hintText.FlowDirection = FlowDirection.LeftToRight; hintText.HorizontalAlignment = HorizontalAlignment.Right; }
            card.Children.Add(hintText);

            if (!preview)
            {
                card.Children.Add(Build.Labeled("نام کاربری", Build.Field(username, "DOMAIN\\username")));
                card.Children.Add(Build.Labeled("رمز عبور", password));
            }
            var actions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition());
            if (!preview)
            {
                actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                reuse.Margin = new Thickness(10, 0, 0, 0);
                reuse.MinWidth = 130;
                SetColumn(reuse, 1);
                actions.Children.Add(reuse);
            }
            actions.Children.Add(signIn);
            card.Children.Add(actions);
            card.Children.Add(status);
            Anim.FadeIn(card, 18);
            return card;
        }

        private void LoadStoredLogin()
        {
            signIn.IsEnabled = preview || server != null;
            reuse.IsEnabled = false;
            if (preview || loginPath == null) return;
            try
            {
                var last = LocalState.Load<SavedLogin>(loginPath);
                if (last.IsForServer(server)) storedUsername = last.Username;
            }
            catch (Exception ex)
            {
                if (!Errors.IsExpected(ex)) throw;
                status.Text = "ورود ذخیره‌شده قابل خواندن نیست. نام کاربری و رمز را وارد کنید.";
            }
            reuse.IsEnabled = storedUsername != null;
            if (storedUsername != null) username.Text = storedUsername;
        }

        private void SetBusy(bool busy)
        {
            signIn.IsEnabled = !busy && (preview || server != null);
            reuse.IsEnabled = !busy && storedUsername != null;
            username.IsEnabled = password.IsEnabled = !busy;
        }

        private async System.Threading.Tasks.Task Enter(bool useStored)
        {
            if (loading.Visibility == Visibility.Visible || !signIn.IsEnabled && !reuse.IsEnabled) return;
            SetBusy(true);
            status.Text = "";
            loading.Show("در حال ورود…");
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
                if (!preview && (string.IsNullOrWhiteSpace(loginName) || string.IsNullOrEmpty(secret)))
                    throw new LoginException("نام کاربری و رمز را وارد کنید.");
                string confirmed = preview ? "کاربر آزمایشی" :
                    await CompanyLogin.Authenticate(server, loginName, secret, cancellation);
                if (cancellation.IsCancellationRequested) return;
                if (useStored && !string.Equals(confirmed, stored.Account, StringComparison.OrdinalIgnoreCase))
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
                        if (!Errors.IsExpected(ex)) throw;
                        throw new LoginException("سرور ورود را پذیرفت، اما ذخیرهٔ رمز روی این رایانه انجام نشد.");
                    }
                }
                password.Clear();
                var handler = SignedIn;
                if (handler != null)
                    handler(new SessionInfo
                    {
                        Preview = preview, Server = server, Account = confirmed, State = workspace,
                        StatePath = workspacePath, VaultPath = Path.Combine(directory, "credentials.bin"),
                        SavedLoginPath = loginPath
                    });
            }
            catch (LoginException ex) { status.Text = ex.Message; }
            catch (Exception ex)
            {
                if (!Errors.IsExpected(ex)) throw;
                bool connection = ex is System.Net.Http.HttpRequestException || ex is OperationCanceledException;
                status.Text = connection ? "ارتباط امن با سرور برقرار نشد. اتصال و گواهی سرور را بررسی کنید." :
                    "ورود انجام نشد. پاسخ سرور یا دادهٔ ذخیره‌شده قابل خواندن نیست.";
            }
            finally
            {
                loading.Hide();
                password.Clear();
                SetBusy(false);
            }
        }
    }
}
