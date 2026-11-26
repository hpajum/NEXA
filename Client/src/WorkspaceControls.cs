using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Nexa
{
    internal static class WorkspaceColors
    {
        public static readonly Color Background = Color.FromArgb(238, 242, 247);
        public static readonly Color Paper = Color.White;
        public static readonly Color Ink = Color.FromArgb(22, 35, 54);
        public static readonly Color Muted = Color.FromArgb(80, 100, 124);
        public static readonly Color Line = Color.FromArgb(214, 224, 235);
        public static readonly Color Navy = Color.FromArgb(24, 39, 58);
        public static readonly Color SidebarText = Color.FromArgb(234, 243, 254);
        public static readonly Color SidebarSelection = Color.FromArgb(53, 77, 110);
        public static readonly Color Accent = Color.FromArgb(35, 77, 179);
        public static readonly Color AccentSurface = Color.FromArgb(231, 239, 254);
    }

    internal static class Surface
    {
        public static void Paint(Graphics graphics, Rectangle bounds, Color fill, Color stroke, int radius)
        {
            if (bounds.Width < 2 || bounds.Height < 2) return;
            int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            using (var path = new GraphicsPath())
            {
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                SmoothingMode previous = graphics.SmoothingMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var background = new SolidBrush(fill)) graphics.FillPath(background, path);
                if (stroke != Color.Empty)
                    using (var outline = new Pen(stroke)) graphics.DrawPath(outline, path);
                graphics.SmoothingMode = previous;
            }
        }
    }

    internal sealed class WorkspaceButton : Button
    {
        private readonly bool primary;
        private bool hovered;
        private bool pressed;

        public WorkspaceButton(bool primary)
        {
            this.primary = primary;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var fill = Enabled ? BackColor : WorkspaceColors.Background;
            if (Enabled && hovered)
                fill = primary ? Color.FromArgb(28, 64, 157) : WorkspaceColors.AccentSurface;
            if (Enabled && pressed) fill = primary ? Color.FromArgb(22, 53, 135) : Color.FromArgb(217, 229, 252);
            var ink = Enabled ? ForeColor : WorkspaceColors.Muted;
            var border = Enabled && primary ? Color.Empty : WorkspaceColors.Line;
            e.Graphics.Clear(Parent == null ? WorkspaceColors.Background : Parent.BackColor);
            var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            Surface.Paint(e.Graphics, bounds, fill, border, 9);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            if (RightToLeft == RightToLeft.Yes) flags |= TextFormatFlags.RightToLeft;
            TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(bounds, -6, 0), ink, flags);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4), ink, fill);
        }
    }

    internal sealed class NavigationList : ListBox
    {
        public NavigationList()
        {
            Dock = DockStyle.Fill;
            BorderStyle = BorderStyle.None;
            IntegralHeight = false;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 34;
            BackColor = WorkspaceColors.Navy;
            ForeColor = WorkspaceColors.SidebarText;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            ItemHeight = Math.Max(34, Font.Height + 15);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            using (var background = new SolidBrush(BackColor)) e.Graphics.FillRectangle(background, e.Bounds);
            if ((e.State & DrawItemState.Selected) != 0)
                Surface.Paint(e.Graphics, Rectangle.Inflate(e.Bounds, -2, -2), WorkspaceColors.SidebarSelection, Color.Empty, 8);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, Rectangle.Inflate(e.Bounds, -12, 0),
                ForeColor, TextFormatFlags.RightToLeft | TextFormatFlags.Right | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }
    }
}
