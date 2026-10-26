using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AcroClone.Model;

namespace AcroClone.UI
{
    /// <summary>Gelbes Pop-up-Fenster einer Notiz (wie in Acrobat 9).</summary>
    public class NotePopup : Control
    {
        private readonly TextBox text;
        private readonly DocumentSession session;
        private bool changeOpen;
        private Point dragStart;
        private bool dragging;

        public string AnnotationId { get; private set; }
        /// <summary>Vom Benutzer verschobene Position relativ zur automatischen Position.</summary>
        public Point UserOffset { get; set; }
        public event EventHandler CloseRequested;
        public event EventHandler Committed;

        private const int HeaderHeight = 20;

        public NotePopup(DocumentSession session, Annotation a)
        {
            this.session = session;
            AnnotationId = a.Id;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(230, 150);
            Font = Theme.UIFont;
            text = new TextBox();
            text.Multiline = true;
            text.BorderStyle = BorderStyle.None;
            text.ScrollBars = ScrollBars.Vertical;
            text.BackColor = Color.FromArgb(255, 255, 220);
            text.Font = Theme.UIFont;
            text.Text = (a.Contents ?? string.Empty).Replace("\r\n", "\n").Replace("\n", "\r\n");
            text.TextChanged += OnTextChanged;
            text.Leave += delegate { Commit(); };
            Controls.Add(text);
            LayoutText();
        }

        public void FocusText()
        {
            text.Focus();
            text.SelectionStart = text.TextLength;
        }

        public void RefreshFromModel()
        {
            Annotation a = session.FindAnnotation(AnnotationId);
            if (a == null || text.Focused) return;
            string t = (a.Contents ?? string.Empty).Replace("\r\n", "\n").Replace("\n", "\r\n");
            if (text.Text != t)
            {
                text.TextChanged -= OnTextChanged;
                text.Text = t;
                text.TextChanged += OnTextChanged;
            }
            Invalidate();
        }

        private void OnTextChanged(object sender, EventArgs e)
        {
            Annotation a = session.FindAnnotation(AnnotationId);
            if (a == null) return;
            if (!changeOpen)
            {
                session.BeginChange(false, "Kommentar bearbeiten");
                changeOpen = true;
            }
            a.Contents = text.Text.Replace("\r\n", "\n");
            a.Modified = DateTime.Now;
            session.Modified = true;
        }

        public void Commit()
        {
            if (!changeOpen) return;
            changeOpen = false;
            session.RaiseAnnotationsChanged();
            if (Committed != null) Committed(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutText();
        }

        private void LayoutText()
        {
            if (text != null) text.Bounds = new Rectangle(4, HeaderHeight + 2, Width - 8, Height - HeaderHeight - 10);
        }

        private Rectangle CloseBox { get { return new Rectangle(Width - 18, 3, 14, 14); } }
        private Rectangle GripBox { get { return new Rectangle(Width - 10, Height - 10, 10, 10); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Annotation a = session.FindAnnotation(AnnotationId);
            Color baseColor = a != null && !a.Color.IsEmpty ? a.Color : Color.Yellow;
            Color head = ControlPaint.Light(baseColor, 0.2f);
            g.Clear(Color.FromArgb(255, 255, 220));
            Theme.FillGradient(g, new Rectangle(0, 0, Width, HeaderHeight), ControlPaint.Light(head, 0.6f), head);
            using (Pen p = new Pen(Color.FromArgb(120, 110, 60)))
                g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            string author = a != null ? a.Author : "";
            string date = a != null ? a.Modified.ToString("dd.MM.yyyy HH:mm") : "";
            using (Font bold = new Font(Theme.UIFont, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, author, bold, new Rectangle(5, 3, Width - 120, 15), Color.Black, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            TextRenderer.DrawText(g, date, Theme.SmallFont, new Rectangle(Width - 128, 4, 106, 14), Color.FromArgb(60, 60, 60), TextFormatFlags.Right | TextFormatFlags.NoPrefix);
            Rectangle cb = CloseBox;
            using (Pen p = new Pen(Color.FromArgb(80, 70, 40), 1.4f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawLine(p, cb.Left + 3, cb.Top + 3, cb.Right - 3, cb.Bottom - 3);
                g.DrawLine(p, cb.Right - 3, cb.Top + 3, cb.Left + 3, cb.Bottom - 3);
                g.SmoothingMode = SmoothingMode.None;
            }
            Rectangle gr = GripBox;
            using (Pen p = new Pen(Color.FromArgb(150, 140, 90)))
                for (int i = 2; i < 9; i += 3) g.DrawLine(p, gr.Right - i, gr.Bottom - 1, gr.Right - 1, gr.Bottom - i);
        }

        private bool resizing;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (CloseBox.Contains(e.Location))
            {
                Commit();
                if (CloseRequested != null) CloseRequested(this, EventArgs.Empty);
                return;
            }
            if (GripBox.Contains(e.Location)) { resizing = true; dragStart = e.Location; return; }
            if (e.Y < HeaderHeight) { dragging = true; dragStart = e.Location; Capture = true; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                Point d = new Point(e.X - dragStart.X, e.Y - dragStart.Y);
                Location = new Point(Left + d.X, Top + d.Y);
                UserOffset = new Point(UserOffset.X + d.X, UserOffset.Y + d.Y);
            }
            else if (resizing)
            {
                Size = new Size(Math.Max(150, e.X + 4), Math.Max(80, e.Y + 4));
            }
            else
            {
                Cursor = GripBox.Contains(e.Location) ? Cursors.SizeNWSE : (e.Y < HeaderHeight && !CloseBox.Contains(e.Location) ? Cursors.SizeAll : Cursors.Default);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
            resizing = false;
            Capture = false;
        }
    }
}
