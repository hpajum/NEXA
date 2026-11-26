using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Nexa
{
    /// <summary>
    /// Modern loading indicator: an accent arc that sweeps and rotates over a faint track,
    /// in the style of Windows 11 progress rings. Animation stops when the control is unloaded.
    /// </summary>
    internal sealed class Spinner : Grid
    {
        private readonly RotateTransform rotate = new RotateTransform();
        private readonly Path arc = new Path();
        private readonly double size;
        private readonly double thickness;

        public Spinner(double size = 34, double thickness = 3.5)
        {
            this.size = size;
            this.thickness = thickness;
            Width = Height = size;
            FlowDirection = FlowDirection.LeftToRight;
            var track = new Ellipse { StrokeThickness = thickness, Opacity = 0.18 };
            Res.Bind(track, Shape.StrokeProperty, "MutedBrush");
            Children.Add(track);

            arc.StrokeThickness = thickness;
            arc.StrokeStartLineCap = arc.StrokeEndLineCap = PenLineCap.Round;
            Res.Bind(arc, Shape.StrokeProperty, "AccentBrush");
            arc.RenderTransformOrigin = new Point(0.5, 0.5);
            arc.RenderTransform = rotate;
            arc.Data = Arc(120);
            Children.Add(arc);

            Loaded += delegate { Start(); };
            Unloaded += delegate { rotate.BeginAnimation(RotateTransform.AngleProperty, null); };
        }

        private void Start()
        {
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(Anim.Enabled ? 1.1 : 2.4))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = Anim.Enabled ? new SineEase { EasingMode = EasingMode.EaseInOut } : null
            };
            rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }

        private Geometry Arc(double degrees)
        {
            double radius = (size - thickness) / 2;
            var center = new Point(size / 2, size / 2);
            double end = degrees * Math.PI / 180;
            var start = new Point(center.X, center.Y - radius);
            var stop = new Point(center.X + radius * Math.Sin(end), center.Y - radius * Math.Cos(end));
            var figure = new PathFigure { StartPoint = start, IsClosed = false };
            figure.Segments.Add(new ArcSegment(stop, new Size(radius, radius), 0, degrees > 180, SweepDirection.Clockwise, true));
            var geometry = new PathGeometry(new[] { figure });
            geometry.Freeze();
            return geometry;
        }
    }

    /// <summary>
    /// A per-section loading overlay. Each section (login card, sidebar, conversation, dialogs) owns one,
    /// so loading in one place never blocks the rest of the window.
    /// </summary>
    internal sealed class LoadingOverlay : Grid
    {
        private readonly TextBlock caption = new TextBlock();
        private int depth;

        public LoadingOverlay(double spinnerSize = 34)
        {
            Visibility = Visibility.Collapsed;
            Res.Bind(this, BackgroundProperty, "OverlayVeil");
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(new Spinner(spinnerSize));
            caption.Margin = new Thickness(0, 12, 0, 0);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.FontSize = 12.5;
            Res.Bind(caption, TextBlock.ForegroundProperty, "MutedBrush");
            stack.Children.Add(caption);
            Children.Add(stack);
            Panel.SetZIndex(this, 100);
        }

        public void Show(string text)
        {
            caption.Text = text ?? "";
            caption.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            if (depth++ > 0) return;
            Visibility = Visibility.Visible;
            Anim.FadeIn(this, 0);
        }

        public void Hide()
        {
            if (depth == 0 || --depth > 0) return;
            Anim.FadeOut(this, delegate { if (depth == 0) Visibility = Visibility.Collapsed; });
        }

        /// <summary>Runs work off the UI thread while the overlay is shown.</summary>
        public async Task<T> Run<T>(string text, Func<T> work)
        {
            Show(text);
            try { return await Task.Run(work); }
            finally { Hide(); }
        }

        public async Task Run(string text, Func<Task> work)
        {
            Show(text);
            try { await work(); }
            finally { Hide(); }
        }
    }
}
