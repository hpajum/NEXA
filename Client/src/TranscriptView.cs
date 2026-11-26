using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Nexa
{
    internal sealed partial class MainWindow
    {
        // The width-limited, content-sized bubbles adapt ChatItem.ResizeBubbles from
        // https://github.com/agreentejada/winforms-chat (MIT, copyright 2020 agreentejada).
        private sealed class TranscriptView : ListBox
        {
            private const int SetItemHeight = 0x01A0; // LB_SETITEMHEIGHT
            private const TextFormatFlags Wrap = TextFormatFlags.RightToLeft | TextFormatFlags.Right |
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
            private int measuredWidth;

            public TranscriptView()
            {
                Dock = DockStyle.Fill;
                Margin = new Padding(0, 5, 0, 10);
                BackColor = WorkspaceColors.Paper;
                ForeColor = WorkspaceColors.Ink;
                BorderStyle = BorderStyle.None;
                IntegralHeight = false;
                DrawMode = DrawMode.OwnerDrawVariable;
            }

            public void Append(object item)
            {
                int index = Items.Add(item);
                if (!IsHandleCreated) return;
                using (Graphics graphics = CreateGraphics())
                    SendMessage(Handle, SetItemHeight, new IntPtr(index),
                        new IntPtr(BubbleSize(graphics, item).Height + 8));
            }

            protected override void OnMeasureItem(MeasureItemEventArgs e)
            {
                e.ItemHeight = e.Index >= 0 && e.Index < Items.Count
                    ? BubbleSize(e.Graphics, Items[e.Index]).Height + 8 : 40;
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                if (IsHandleCreated && Width != measuredWidth) MeasureRows();
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                MeasureRows();
            }

            private void MeasureRows()
            {
                measuredWidth = Width;
                using (Graphics graphics = CreateGraphics())
                    for (int i = 0; i < Items.Count; i++)
                        SendMessage(Handle, SetItemHeight, new IntPtr(i),
                            new IntPtr(BubbleSize(graphics, Items[i]).Height + 8));
                Invalidate();
            }

            protected override void OnDrawItem(DrawItemEventArgs e)
            {
                if (e.Index < 0 || e.Index >= Items.Count) return;
                object item = Items[e.Index];
                Size bubbleSize = BubbleSize(e.Graphics, item);
                var bubble = new Rectangle(e.Bounds.Right - bubbleSize.Width - 7, e.Bounds.Y + 3,
                    bubbleSize.Width, Math.Min(bubbleSize.Height, e.Bounds.Height - 5));
                bool selected = (e.State & DrawItemState.Selected) != 0;
                bool file = item is FileEntry;
                e.Graphics.FillRectangle(Brushes.White, e.Bounds);
                Surface.Paint(e.Graphics, bubble, file ? WorkspaceColors.Background : WorkspaceColors.AccentSurface,
                    selected ? WorkspaceColors.Accent : Color.Empty, 11);
                var padding = Rectangle.Inflate(bubble, -12, -8);
                TextRenderer.DrawText(e.Graphics, DisplayText(item), Font, padding,
                    file ? WorkspaceColors.Accent : ForeColor, Wrap | TextFormatFlags.EndEllipsis);
                if ((e.State & DrawItemState.Focus) != 0)
                    ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bubble, -3, -3));
            }

            private Size BubbleSize(Graphics graphics, object item)
            {
                string text = DisplayText(item);
                int available = Math.Max(26, ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 10);
                int maximum = Math.Min(670, Math.Max(26, available * 4 / 5));
                int unwrapped = TextRenderer.MeasureText(graphics, text, Font,
                    new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix).Width;
                int width = Math.Min(maximum, Math.Max(80, unwrapped + 25));
                int height = TextRenderer.MeasureText(graphics, text, Font,
                    new Size(Math.Max(1, width - 24), int.MaxValue), Wrap).Height;
                return new Size(width, Math.Min(232, Math.Max(38, height + 16)));
            }

            private static string DisplayText(object item)
            {
                var message = item as MessageEntry;
                if (message == null) return item.ToString();
                string text = message.Text;
                return text.Length > 600 ? text.Substring(0, 599) + "…" : text;
            }

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr item, IntPtr height);
        }
    }
}
