using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace Nexa
{
    /// <summary>Hosts one full-window view at a time (sign-in, then the workspace) plus modals and toasts.</summary>
    internal sealed class MainWindow : Window
    {
        private readonly bool preview;
        private readonly Grid root = new Grid();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool quitting;

        public MainWindow(bool preview)
        {
            this.preview = preview;
            Title = preview ? "NEXA | آزمایشی" : "NEXA";
            FlowDirection = FlowDirection.RightToLeft;
            FontFamily = Fonts.Body;
            FontSize = 14;
            Width = 1180;
            Height = 740;
            MinWidth = 900;
            MinHeight = 600;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            SetResourceReference(BackgroundProperty, "AppBg");
            Content = root;
            NativeChrome.Attach(this);
        }

        public void Reveal()
        {
            if (root.Children.Count == 0) ShowLogin();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            Activate();
        }

        public void Quit()
        {
            quitting = true;
            lifetime.Cancel();
            if (IsLoaded) Close();
            else Application.Current.Shutdown();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!quitting)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnClosing(e);
        }

        private void ShowLogin()
        {
            var view = new LoginView(preview, lifetime.Token);
            view.SignedIn += ShowWorkspace;
            Swap(view);
        }

        private void ShowWorkspace(SessionInfo session)
        {
            var view = new WorkspaceView(session, root);
            view.SignedOut += ShowLogin;
            Swap(view);
        }

        private void Swap(UIElement next)
        {
            var old = root.Children.Count > 0 ? root.Children[0] : null;
            Action place = delegate
            {
                for (int i = root.Children.Count - 1; i >= 0; i--)
                    if (root.Children[i] != next && !(root.Children[i] is Border && ((Border)root.Children[i]).Tag as string == "toast"))
                        root.Children.RemoveAt(i);
                if (!root.Children.Contains(next)) root.Children.Insert(0, next);
                Anim.FadeIn(next, 14);
            };
            if (old == null) place();
            else Anim.FadeOut(old, place);
        }
    }
}
