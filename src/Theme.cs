using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace AspectSwitch
{
    internal static class Theme
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        internal static readonly Icon AppIcon = CreateIcon();
        private static Icon CreateIcon()
        {
            using (var resource = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("AspectSwitch.AppIcon"))
                if (resource != null) using (var icon = new Icon(resource)) return (Icon)icon.Clone();
            using (Bitmap bitmap = new Bitmap(32, 32)) using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath shape = Round(new RectangleF(1, 1, 30, 30), 7)) using (Brush b = new SolidBrush(Color.FromArgb(0, 103, 192))) g.FillPath(b, shape);
                using (Pen p = new Pen(Color.White, 2)) { g.DrawRectangle(p, 6, 8, 20, 13); g.DrawLine(p, 16, 21, 16, 25); g.DrawLine(p, 11, 25, 21, 25); }
                IntPtr handle = bitmap.GetHicon(); try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { DestroyIcon(handle); }
            }
        }
        internal static readonly Color Background = Color.FromArgb(247, 247, 250);
        internal static readonly Color Sidebar = Color.FromArgb(240, 242, 247);
        internal static readonly Color Text = Color.FromArgb(29, 34, 44);
        internal static readonly Color Muted = Color.FromArgb(99, 108, 123);
        internal static readonly Color Accent = Color.FromArgb(0, 103, 192);
        internal static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath(); float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
        }
        internal static Label Label(string text, float size, bool bold, Color color)
        { return new Label { Text = text, AutoSize = false, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, BackColor = Color.Transparent, AutoEllipsis = true }; }
        internal static ComboBox Combo(Choice[] choices, int value)
        {
            ComboBox box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Text, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0), IntegralHeight = false, DropDownHeight = 240 };
            foreach (Choice choice in choices) { box.Items.Add(choice); if (choice.Value == value) box.SelectedItem = choice; }
            if (box.SelectedIndex < 0 && box.Items.Count > 0) box.SelectedIndex = 0;
            return box;
        }
    }
    internal sealed class Card : Panel
    {
        internal Card() { DoubleBuffered = true; BackColor = Color.Transparent; Padding = new Padding(20); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor.A < 255 ? Color.White : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Theme.Round(new RectangleF(.5F, .5F, Width - 1, Height - 1), 8))
            using (Brush brush = new SolidBrush(Color.White)) using (Pen pen = new Pen(Color.FromArgb(227, 230, 237)))
            { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
        }
    }
    internal sealed class ModernButton : Button
    {
        internal bool Primary;
        internal bool Selected;
        private bool hover;
        internal ModernButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; Height = 36; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Primary ? (hover ? Color.FromArgb(0, 88, 165) : Theme.Accent) : Selected ? Color.FromArgb(225, 235, 247) : hover ? Color.FromArgb(238, 241, 247) : Color.White;
            if (!Enabled) fill = Color.FromArgb(235, 237, 241);
            e.Graphics.Clear(Parent == null ? Theme.Background : Parent.BackColor.A < 255 ? Color.White : Parent.BackColor);
            using (GraphicsPath path = Theme.Round(new RectangleF(.5F, .5F, Width - 1, Height - 1), 6))
            using (Brush brush = new SolidBrush(fill)) using (Pen pen = new Pen(Primary || Selected ? fill : Color.FromArgb(218, 222, 230))) { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            if (Selected) using (Brush b = new SolidBrush(Theme.Accent)) e.Graphics.FillRectangle(b, 0, 10, 3, Height - 20);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, !Enabled ? Theme.Muted : Primary ? Color.White : Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
        }
    }
    internal sealed class Toggle : CheckBox
    {
        internal Toggle() { AutoSize = false; Size = new Size(48, 26); Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.CheckButton; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.Clear(Parent == null || Parent.BackColor.A < 255 ? Color.White : Parent.BackColor);
            RectangleF r = new RectangleF(1, 2, Width - 3, Height - 5);
            using (GraphicsPath p = Theme.Round(r, r.Height / 2)) using (Brush b = new SolidBrush(Checked ? Theme.Accent : Color.FromArgb(141, 150, 164))) e.Graphics.FillPath(b, p);
            float d = r.Height - 6; using (Brush b = new SolidBrush(Color.White)) e.Graphics.FillEllipse(b, Checked ? r.Right - d - 3 : r.Left + 3, r.Top + 3, d, d);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
        }
    }
}
