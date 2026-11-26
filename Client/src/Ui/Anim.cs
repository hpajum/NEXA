using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nexa
{
    /// <summary>Small animation helpers. All of them become instant when animations are turned off.</summary>
    internal static class Anim
    {
        public static bool Enabled { get { return AppSettings.Animations; } }

        private static TimeSpan Ms(double value) { return TimeSpan.FromMilliseconds(value); }

        public static void FadeIn(UIElement element, double offsetY)
        {
            if (!Enabled) return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            element.Opacity = 0;
            element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, Ms(240)) { EasingFunction = ease });
            if (offsetY == 0) return;
            var move = new TranslateTransform(0, offsetY);
            element.RenderTransform = move;
            move.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(offsetY, 0, Ms(300)) { EasingFunction = ease });
        }

        public static void FadeOut(UIElement element, Action done)
        {
            if (!Enabled) { done(); return; }
            var fade = new DoubleAnimation(element.Opacity, 0, Ms(160));
            fade.Completed += delegate { done(); };
            element.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        public static void Scale(FrameworkElement element, double from, double to, double milliseconds)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = new ScaleTransform(from, from);
            element.RenderTransform = scale;
            if (!Enabled) { scale.ScaleX = scale.ScaleY = to; return; }
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(from, to, Ms(milliseconds)) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(from, to, Ms(milliseconds)) { EasingFunction = ease });
        }

        public static void Spin(UIElement element)
        {
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            var rotate = new RotateTransform(0);
            element.RenderTransform = rotate;
            if (!Enabled) return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-180, 0, Ms(420)) { EasingFunction = ease });
        }
    }
}
