using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace AcroClone.UI
{
    /// <summary>Begrüßungsbildschirm, solange kein Dokument geöffnet ist (Acrobat 9 "Erste Schritte").</summary>
    public class WelcomePanel : Control
    {
        private class Entry
        {
            public string Command;
            public string Icon;
            public string Title;
            public string Sub;
            public Rectangle Bounds;
        }

        private readonly List<Entry> entries = new List<Entry>();
        private Entry hover;
        private Rectangle box;

        public event EventHandler<ItemEventArgs> CommandClicked;

        public WelcomePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = Theme.UIFont;
        }

        private void BuildEntries()
        {
            entries.Clear();
            Add("open", "open", "Öffnen...", "Eine vorhandene PDF-Datei öffnen");
            foreach (string f in AppSettings.RecentFiles)
            {
                if (entries.Count >= 7) break;
                Add("recent:" + f, "pages", Path.GetFileName(f), Path.GetDirectoryName(f));
            }
            Add("create", "create", "PDF erstellen", "Aus Bildern, Textdateien oder der Zwischenablage");
            Add("combine", "combine", "Dateien zusammenführen", "Mehrere Dateien zu einem PDF zusammenführen");
            Add("blank", "blankPage", "Leeres Dokument", "Ein neues, leeres PDF-Dokument anlegen");
            Add("sample", "logo", "Beispieldokument öffnen", "Die Funktionen an einem Beispiel ausprobieren");
        }

        private void Add(string cmd, string icon, string title, string sub)
        {
            Entry e = new Entry();
            e.Command = cmd; e.Icon = icon; e.Title = title; e.Sub = sub;
            entries.Add(e);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) { BuildEntries(); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (entries.Count == 0) BuildEntries();
            Graphics g = e.Graphics;
            g.Clear(Theme.DocumentBackground);
            int w = Math.Min(680, Width - 40), h = 420;
            box = new Rectangle((Width - w) / 2, Math.Max(20, (Height - h) / 2), w, h);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                g.FillRectangle(shadow, box.X + 5, box.Y + 5, box.Width, box.Height);

            // Kopfbereich in dunklem Anthrazit mit rotem Streifen
            Rectangle head = new Rectangle(box.X, box.Y, box.Width, 92);
            Theme.FillGradient(g, head, Color.FromArgb(70, 70, 74), Color.FromArgb(24, 24, 26));
            using (LinearGradientBrush glow = new LinearGradientBrush(head, Color.FromArgb(90, 190, 20, 20), Color.FromArgb(0, 190, 20, 20), LinearGradientMode.Horizontal))
                g.FillRectangle(glow, head);
            g.FillRectangle(new SolidBrush(Theme.AcrobatRed), box.X, head.Bottom, box.Width, 4);
            g.DrawImage(Icons.Get("logo", 56), head.X + 22, head.Y + 18, 56, 56);
            using (Font big = new Font("Arial", 22f, FontStyle.Bold))
            using (Font small = new Font("Arial", 10f, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, "AcroClone 9 Pro", big, new Point(head.X + 92, head.Y + 20), Color.White);
                TextRenderer.DrawText(g, "Erste Schritte", small, new Point(head.X + 95, head.Y + 58), Color.FromArgb(210, 210, 210));
            }

            Rectangle body = new Rectangle(box.X, head.Bottom + 4, box.Width, box.Height - head.Height - 4);
            Theme.FillGradient(g, body, Color.FromArgb(252, 252, 253), Color.FromArgb(228, 231, 236));
            using (Pen p = new Pen(Color.FromArgb(60, 60, 60))) g.DrawRectangle(p, box.X, box.Y, box.Width - 1, box.Height - 1);

            int colW = (body.Width - 60) / 2;
            Rectangle left = new Rectangle(body.X + 20, body.Y + 16, colW, body.Height - 30);
            Rectangle right = new Rectangle(left.Right + 20, body.Y + 16, colW, body.Height - 30);
            using (Pen p = new Pen(Color.FromArgb(200, 204, 210))) g.DrawLine(p, left.Right + 10, left.Top + 4, left.Right + 10, left.Bottom - 10);
            TextRenderer.DrawText(g, "Zuletzt verwendete Dateien", Theme.UIFontBold, new Point(left.X, left.Y), Color.FromArgb(150, 20, 20));
            TextRenderer.DrawText(g, "Aufgaben", Theme.UIFontBold, new Point(right.X, right.Y), Color.FromArgb(150, 20, 20));

            int ly = left.Y + 24, ry = right.Y + 24;
            foreach (Entry en in entries)
            {
                bool isTask = en.Command == "create" || en.Command == "combine" || en.Command == "blank" || en.Command == "sample";
                Rectangle col = isTask ? right : left;
                int y = isTask ? ry : ly;
                int eh = isTask ? 50 : 36;
                en.Bounds = new Rectangle(col.X - 4, y, col.Width + 8, eh - 4);
                if (en == hover) Theme.DrawHoverBox(g, en.Bounds, false);
                int icon = isTask ? 32 : 16;
                g.DrawImage(Icons.Get(en.Icon, icon), en.Bounds.X + 6, en.Bounds.Y + (en.Bounds.Height - icon) / 2, icon, icon);
                int tx = en.Bounds.X + icon + 14;
                TextRenderer.DrawText(g, en.Title, isTask ? Theme.UIFontBold : Theme.UIFont, new Rectangle(tx, en.Bounds.Y + (isTask ? 8 : 3), en.Bounds.Right - tx - 4, 16),
                    Color.FromArgb(20, 50, 120), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, en.Sub ?? "", Theme.SmallFont, new Rectangle(tx, en.Bounds.Y + (isTask ? 25 : 17), en.Bounds.Right - tx - 4, 14),
                    Color.DimGray, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);
                if (isTask) ry += eh; else ly += eh;
            }
            if (AppSettings.RecentFiles.Count == 0)
                TextRenderer.DrawText(g, "Noch keine Dateien geöffnet.", Theme.UIFont, new Point(left.X + 4, ly + 6), Color.Gray);

            TextRenderer.DrawText(g, "Hinweis: Ziehen Sie PDF-Dateien einfach in dieses Fenster, um sie zu öffnen.", Theme.SmallFont,
                new Rectangle(body.X + 20, body.Bottom - 22, body.Width - 40, 16), Color.DimGray, TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Entry h = null;
            foreach (Entry en in entries) if (en.Bounds.Contains(e.Location)) h = en;
            if (h != hover) { hover = h; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hover = null;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            foreach (Entry en in entries)
                if (en.Bounds.Contains(e.Location) && CommandClicked != null) { CommandClicked(this, new ItemEventArgs(en.Command)); return; }
        }
    }
}
