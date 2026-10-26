using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using AcroClone.Model;
using AcroClone.Pdf;

namespace AcroClone.UI
{
    /// <summary>Seitenminiaturen mit Mehrfachauswahl und Drag &amp; Drop zum Neuanordnen.</summary>
    public class ThumbnailView : Control
    {
        private DocumentSession session;
        private DocumentView view;
        private readonly Dictionary<int, Bitmap> thumbs = new Dictionary<int, Bitmap>();
        private readonly SortedSet<int> selection = new SortedSet<int>();
        private int thumbSize = 96;
        private int anchorIndex = -1;
        private int columns = 1;
        private Size cell;
        private Point mouseDownAt;
        private int mouseDownIndex = -1;
        private bool dragging;
        private int dropIndex = -1;
        private readonly VScrollBar vbar;
        private int scrollY;
        private int contentHeight;

        public event EventHandler<MouseEventArgs> ContextRequested;
        public event EventHandler DeleteRequested;

        public ThumbnailView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(242, 243, 246);
            TabStop = true;
            Font = Theme.UIFont;
            vbar = new VScrollBar();
            vbar.Dock = DockStyle.Right;
            vbar.Visible = false;
            vbar.ValueChanged += delegate { scrollY = vbar.Value; Invalidate(); };
            Controls.Add(vbar);
        }

        private int ScrollY
        {
            get { return scrollY; }
            set
            {
                int max = Math.Max(0, contentHeight - ClientSize.Height);
                scrollY = Math.Max(0, Math.Min(max, value));
                if (vbar.Visible && vbar.Value != scrollY) vbar.Value = Math.Min(scrollY, Math.Max(0, vbar.Maximum - vbar.LargeChange + 1));
                Invalidate();
            }
        }

        private int AvailableWidth { get { return Math.Max(1, ClientSize.Width - (vbar.Visible ? vbar.Width : 0)); } }

        public DocumentView View
        {
            get { return view; }
            set
            {
                if (view != null) { view.CurrentPageChanged -= OnViewChanged; view.ViewChanged -= OnViewChanged; }
                view = value;
                if (view != null) { view.CurrentPageChanged += OnViewChanged; view.ViewChanged += OnViewChanged; }
            }
        }

        public DocumentSession Session
        {
            get { return session; }
            set
            {
                if (session != null) session.StructureChanged -= OnStructureChanged;
                session = value;
                if (session != null) session.StructureChanged += OnStructureChanged;
                selection.Clear();
                ResetThumbnails();
            }
        }

        public int ThumbSize
        {
            get { return thumbSize; }
            set
            {
                thumbSize = Math.Max(48, Math.Min(240, value));
                ResetThumbnails();
            }
        }

        public IList<int> SelectedPages
        {
            get { return selection.ToList(); }
        }

        private int PageCount { get { return session == null ? 0 : session.PageCount; } }

        private void OnStructureChanged(object sender, EventArgs e)
        {
            selection.RemoveWhere(p => p >= PageCount);
            ResetThumbnails();
        }

        private void OnViewChanged(object sender, EventArgs e)
        {
            Invalidate();
            if (view != null && sender == view && !dragging) EnsureVisible(view.CurrentPage);
        }

        public void ResetThumbnails()
        {
            foreach (Bitmap b in thumbs.Values) b.Dispose();
            thumbs.Clear();
            UpdateLayout();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateLayout();
        }

        private void UpdateLayout()
        {
            cell = new Size(thumbSize + 24, thumbSize + 34);
            for (int pass = 0; pass < 2; pass++)
            {
                columns = Math.Max(1, AvailableWidth / cell.Width);
                int rowsCount = (PageCount + columns - 1) / columns;
                contentHeight = rowsCount * cell.Height + 8;
                bool need = contentHeight > ClientSize.Height && ClientSize.Height > 0;
                if (need == vbar.Visible) break;
                vbar.Visible = need;
            }
            if (vbar.Visible)
            {
                vbar.Minimum = 0;
                vbar.Maximum = Math.Max(0, contentHeight - 1);
                vbar.LargeChange = Math.Max(1, ClientSize.Height);
                vbar.SmallChange = 30;
            }
            ScrollY = scrollY;
        }

        private Rectangle CellRect(int index)
        {
            int col = index % columns, row = index / columns;
            int totalW = columns * cell.Width;
            int x0 = Math.Max(0, (AvailableWidth - totalW) / 2);
            return new Rectangle(x0 + col * cell.Width, 4 + row * cell.Height - scrollY, cell.Width, cell.Height);
        }

        private Rectangle ThumbRect(int index)
        {
            Rectangle c = CellRect(index);
            SizeF s = session.Pdf.GetViewSize(index, 0);
            double f = thumbSize / Math.Max(s.Width, s.Height);
            int w = Math.Max(4, (int)(s.Width * f)), h = Math.Max(4, (int)(s.Height * f));
            return new Rectangle(c.X + (c.Width - w) / 2, c.Y + 6 + (thumbSize - h), w, h);
        }

        private int IndexAt(Point p)
        {
            for (int i = 0; i < PageCount; i++)
                if (CellRect(i).Contains(p)) return i;
            return -1;
        }

        /// <summary>Einfügeposition für Drag &amp; Drop (zwischen zwei Miniaturen).</summary>
        private int DropIndexAt(Point p)
        {
            int best = PageCount;
            double bestDist = double.MaxValue;
            for (int i = 0; i <= PageCount; i++)
            {
                Rectangle c = i < PageCount ? CellRect(i) : CellRect(PageCount - 1);
                Point lineAt = i < PageCount ? new Point(columns > 1 ? c.Left : c.X + c.Width / 2, columns > 1 ? c.Y + c.Height / 2 : c.Top)
                    : new Point(columns > 1 ? c.Right : c.X + c.Width / 2, columns > 1 ? c.Y + c.Height / 2 : c.Bottom);
                double d = Math.Pow(lineAt.X - p.X, 2) + Math.Pow(lineAt.Y - p.Y, 2);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        public void EnsureVisible(int index)
        {
            if (index < 0 || index >= PageCount) return;
            Rectangle c = CellRect(index);
            if (c.Top < 0 || c.Bottom > ClientSize.Height)
            {
                ScrollY = scrollY + c.Top - Math.Max(0, (ClientSize.Height - c.Height) / 2);
            }
        }

        private Bitmap GetThumb(int index, Rectangle tr)
        {
            Bitmap b;
            if (thumbs.TryGetValue(index, out b) && b.Width == tr.Width && b.Height == tr.Height) return b;
            if (b != null) b.Dispose();
            try
            {
                b = session.Pdf.RenderPage(index, 0, tr.Width, tr.Height, false);
                using (Graphics g = Graphics.FromImage(b))
                {
                    double f = tr.Width / (double)session.Pdf.GetViewSize(index, 0).Width;
                    Affine m = session.Pdf.GetPageTransform(index, 0).Scale(f);
                    foreach (Annotation a in session.AnnotationsOnPage(index)) AnnotationPainter.Paint(g, a, m);
                }
            }
            catch (Exception)
            {
                b = new Bitmap(tr.Width, tr.Height);
                using (Graphics g = Graphics.FromImage(b)) g.Clear(Color.White);
            }
            thumbs[index] = b;
            return b;
        }

        public void InvalidateThumbnail(int index)
        {
            Bitmap b;
            if (thumbs.TryGetValue(index, out b)) { b.Dispose(); thumbs.Remove(index); }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (session == null) return;
            int current = view != null ? view.CurrentPage : -1;
            for (int i = 0; i < PageCount; i++)
            {
                Rectangle c = CellRect(i);
                if (c.Bottom < 0) continue;
                if (c.Top > ClientSize.Height) break;
                Rectangle tr = ThumbRect(i);
                bool sel = selection.Contains(i);
                if (sel || i == current)
                {
                    Rectangle hl = new Rectangle(tr.X - 5, tr.Y - 5, tr.Width + 10, tr.Height + 10);
                    using (GraphicsPath p = AnnotationPainter.RoundedRect(hl, 4))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        using (SolidBrush b = new SolidBrush(sel ? Color.FromArgb(180, 205, 245) : Color.FromArgb(220, 228, 240)))
                            g.FillPath(b, p);
                        using (Pen pen = new Pen(sel ? Color.FromArgb(70, 110, 190) : Color.FromArgb(160, 175, 200)))
                            g.DrawPath(pen, p);
                        g.SmoothingMode = SmoothingMode.None;
                    }
                }
                using (SolidBrush sh = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                    g.FillRectangle(sh, tr.X + 2, tr.Y + 2, tr.Width, tr.Height);
                g.DrawImageUnscaled(GetThumb(i, tr), tr.Location);
                g.DrawRectangle(Pens.DimGray, tr.X - 1, tr.Y - 1, tr.Width + 1, tr.Height + 1);

                // Roter Rahmen für den sichtbaren Bereich (wie in Acrobat)
                PdfRect vis;
                if (view != null && view.GetVisibleRegion(i, out vis))
                {
                    double f = tr.Width / (double)session.Pdf.GetViewSize(i, 0).Width;
                    Affine m = session.Pdf.GetPageTransform(i, 0).Scale(f).Translate(tr.X, tr.Y);
                    RectangleF vr = m.MapRect(vis);
                    vr.Intersect(tr);
                    if (vr.Width < tr.Width - 2 || vr.Height < tr.Height - 2)
                        using (Pen p = new Pen(Color.FromArgb(220, 20, 20), 2f))
                            g.DrawRectangle(p, vr.X, vr.Y, Math.Max(2, vr.Width - 1), Math.Max(2, vr.Height - 1));
                }

                string label = (i + 1).ToString();
                Rectangle lr = new Rectangle(c.X, tr.Bottom + 6, c.Width, 16);
                if (i == current)
                {
                    Size ts = TextRenderer.MeasureText(label, Font);
                    Rectangle bg = new Rectangle(c.X + (c.Width - ts.Width) / 2 - 3, lr.Y, ts.Width + 6, 15);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(50, 90, 170))) g.FillRectangle(b, bg);
                    TextRenderer.DrawText(g, label, Font, lr, Color.White, TextFormatFlags.HorizontalCenter);
                }
                else
                {
                    TextRenderer.DrawText(g, label, Font, lr, Color.Black, TextFormatFlags.HorizontalCenter);
                }
            }

            if (dragging && dropIndex >= 0)
            {
                Rectangle c = dropIndex < PageCount ? CellRect(dropIndex) : CellRect(PageCount - 1);
                using (Pen p = new Pen(Color.FromArgb(30, 60, 200), 3f))
                {
                    if (columns > 1)
                    {
                        int x = dropIndex < PageCount ? c.Left + 2 : c.Right - 2;
                        g.DrawLine(p, x, c.Top + 4, x, c.Bottom - 4);
                    }
                    else
                    {
                        int y = dropIndex < PageCount ? c.Top + 1 : c.Bottom - 1;
                        g.DrawLine(p, c.Left + 8, y, c.Right - 8, y);
                    }
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (session == null) return;
            int idx = IndexAt(e.Location);
            mouseDownAt = e.Location;
            mouseDownIndex = idx;
            if (idx < 0) return;
            if (e.Button == MouseButtons.Right)
            {
                if (!selection.Contains(idx)) { selection.Clear(); selection.Add(idx); anchorIndex = idx; }
                Invalidate();
                return;
            }
            if ((ModifierKeys & Keys.Control) != 0)
            {
                if (!selection.Remove(idx)) selection.Add(idx);
                anchorIndex = idx;
            }
            else if ((ModifierKeys & Keys.Shift) != 0 && anchorIndex >= 0)
            {
                selection.Clear();
                for (int i = Math.Min(anchorIndex, idx); i <= Math.Max(anchorIndex, idx); i++) selection.Add(i);
            }
            else if (!selection.Contains(idx))
            {
                selection.Clear();
                selection.Add(idx);
                anchorIndex = idx;
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (e.Button != MouseButtons.Left || mouseDownIndex < 0 || session == null) return;
            if (!dragging && (Math.Abs(e.X - mouseDownAt.X) > 5 || Math.Abs(e.Y - mouseDownAt.Y) > 5))
            {
                dragging = true;
                Capture = true;
            }
            if (dragging)
            {
                dropIndex = DropIndexAt(e.Location);
                if (e.Y < 10) ScrollY = scrollY - 20;
                else if (e.Y > ClientSize.Height - 10) ScrollY = scrollY + 20;
                Cursor = Cursors.Hand;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Capture = false;
            Cursor = Cursors.Default;
            if (session == null) return;
            if (dragging)
            {
                dragging = false;
                List<int> moved = selection.ToList();
                if (moved.Count > 0 && dropIndex >= 0)
                {
                    int before = moved.Count(p => p < dropIndex);
                    int dest = dropIndex - before;
                    session.MovePages(moved, dest);
                    selection.Clear();
                    for (int i = 0; i < moved.Count; i++) selection.Add(dest + i);
                    if (view != null) view.GoToPage(dest);
                }
                dropIndex = -1;
                Invalidate();
                return;
            }
            int idx = IndexAt(e.Location);
            if (e.Button == MouseButtons.Right)
            {
                if (ContextRequested != null) ContextRequested(this, e);
                return;
            }
            if (idx >= 0 && idx == mouseDownIndex && (ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
            {
                selection.Clear();
                selection.Add(idx);
                anchorIndex = idx;
                if (view != null) view.GoToPage(idx);
                Invalidate();
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Up || k == Keys.Down || k == Keys.Left || k == Keys.Right) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (session == null) return;
            int cur = selection.Count > 0 ? selection.Max : (view != null ? view.CurrentPage : 0);
            int next = cur;
            switch (e.KeyCode)
            {
                case Keys.Delete:
                    if (DeleteRequested != null) DeleteRequested(this, EventArgs.Empty);
                    return;
                case Keys.Left: next = cur - 1; break;
                case Keys.Right: next = cur + 1; break;
                case Keys.Up: next = cur - columns; break;
                case Keys.Down: next = cur + columns; break;
                case Keys.A:
                    if (e.Control) { selection.Clear(); for (int i = 0; i < PageCount; i++) selection.Add(i); Invalidate(); }
                    return;
                default: return;
            }
            next = Math.Max(0, Math.Min(PageCount - 1, next));
            selection.Clear();
            selection.Add(next);
            anchorIndex = next;
            if (view != null) view.GoToPage(next);
            EnsureVisible(next);
            Invalidate();
            e.Handled = true;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollY = scrollY - e.Delta * 60 / 120;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (Bitmap b in thumbs.Values) b.Dispose();
            base.Dispose(disposing);
        }
    }
}
