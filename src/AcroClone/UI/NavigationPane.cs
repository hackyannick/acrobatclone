using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using AcroClone.Model;
using AcroClone.Pdf;

namespace AcroClone.UI
{
    /// <summary>Senkrechte Schaltflächenleiste am linken Rand (Navigationsfenster).</summary>
    public class NavStrip : Control
    {
        public class Item
        {
            public string Key;
            public string Icon;
            public string Tip;
            public Rectangle Bounds;
            public bool Bottom;
        }

        private readonly List<Item> items = new List<Item>();
        private readonly ToolTip tip = new ToolTip();
        private Item hover;
        public string ActiveKey;
        public HashSet<string> CheckedKeys = new HashSet<string>();
        public event EventHandler<ItemEventArgs> ItemClicked;

        public NavStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Width = 30;
        }

        public void Add(string key, string icon, string tooltip, bool bottom)
        {
            Item i = new Item();
            i.Key = key; i.Icon = icon; i.Tip = tooltip; i.Bottom = bottom;
            items.Add(i);
            Invalidate();
        }

        private void LayoutItems()
        {
            int y = 6;
            foreach (Item i in items)
                if (!i.Bottom) { i.Bounds = new Rectangle(3, y, 24, 24); y += 28; }
            y = Height - 30;
            for (int k = items.Count - 1; k >= 0; k--)
                if (items[k].Bottom) { items[k].Bounds = new Rectangle(3, y, 24, 24); y -= 28; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            LayoutItems();
            Graphics g = e.Graphics;
            using (System.Drawing.Drawing2D.LinearGradientBrush b = new System.Drawing.Drawing2D.LinearGradientBrush(
                ClientRectangle, Theme.NavStripTop, Theme.NavStripBottom, System.Drawing.Drawing2D.LinearGradientMode.Horizontal))
                g.FillRectangle(b, ClientRectangle);
            using (Pen p = new Pen(Theme.ToolbarBorder)) g.DrawLine(p, Width - 1, 0, Width - 1, Height);
            foreach (Item i in items)
            {
                bool active = i.Key == ActiveKey || CheckedKeys.Contains(i.Key);
                if (active) Theme.DrawHoverBox(g, i.Bounds, true);
                else if (i == hover) Theme.DrawHoverBox(g, i.Bounds, false);
                g.DrawImage(Icons.Get(i.Icon, 16), i.Bounds.X + 4, i.Bounds.Y + 4, 16, 16);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Item h = null;
            foreach (Item i in items) if (i.Bounds.Contains(e.Location)) h = i;
            if (h != hover)
            {
                hover = h;
                tip.SetToolTip(this, h != null ? h.Tip : null);
                Invalidate();
            }
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
            foreach (Item i in items)
                if (i.Bounds.Contains(e.Location) && ItemClicked != null) ItemClicked(this, new ItemEventArgs(i.Key));
        }
    }

    public class ItemEventArgs : EventArgs
    {
        public string Key { get; private set; }
        public ItemEventArgs(string key) { Key = key; }
    }

    /// <summary>Kopfzeile eines Fensterbereichs mit Titel, Optionen-Menü und Schließen-Schaltfläche.</summary>
    public class PaneHeader : Control
    {
        private bool hoverOptions, hoverClose;
        public ContextMenuStrip OptionsMenu;
        public event EventHandler CloseClicked;
        public bool ShowClose = true;

        public PaneHeader()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 24;
            Dock = DockStyle.Top;
            Font = Theme.UIFontBold;
        }

        private Rectangle OptionsRect
        {
            get { return OptionsMenu == null ? Rectangle.Empty : new Rectangle(Width - (ShowClose ? 92 : 72), 3, 68, 18); }
        }

        private Rectangle CloseRect { get { return ShowClose ? new Rectangle(Width - 22, 4, 16, 16) : Rectangle.Empty; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Theme.FillGradient(g, ClientRectangle, Theme.PaneHeaderTop, Theme.PaneHeaderBottom);
            using (Pen p = new Pen(Theme.ToolbarBorder)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(6, 0, Math.Max(10, Width - 100), Height), Color.FromArgb(30, 30, 30),
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (OptionsMenu != null)
            {
                Rectangle o = OptionsRect;
                if (hoverOptions) Theme.DrawHoverBox(g, o, false);
                TextRenderer.DrawText(g, "Optionen", Theme.UIFont, new Rectangle(o.X + 3, o.Y, o.Width - 12, o.Height), Color.Black, TextFormatFlags.VerticalCenter);
                g.FillPolygon(Brushes.Black, new Point[] { new Point(o.Right - 10, o.Y + 8), new Point(o.Right - 4, o.Y + 8), new Point(o.Right - 7, o.Y + 11) });
            }
            if (ShowClose)
            {
                Rectangle c = CloseRect;
                if (hoverClose) Theme.DrawHoverBox(g, c, false);
                g.DrawImage(Icons.Get("close", 16), c.X, c.Y, 16, 16);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool o = OptionsRect.Contains(e.Location), c = CloseRect.Contains(e.Location);
            if (o != hoverOptions || c != hoverClose) { hoverOptions = o; hoverClose = c; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverOptions = hoverClose = false;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (OptionsMenu != null && OptionsRect.Contains(e.Location))
                OptionsMenu.Show(this, new Point(OptionsRect.Left, OptionsRect.Bottom));
            else if (CloseRect.Contains(e.Location) && CloseClicked != null)
                CloseClicked(this, EventArgs.Empty);
        }
    }

    /// <summary>Navigationsfenster: Seitenminiaturen, Lesezeichen und Anlagen.</summary>
    public class NavigationPane : Control
    {
        private readonly NavStrip strip;
        private readonly Panel content;
        private readonly PaneHeader header;
        private readonly ThumbnailView thumbnails;
        private readonly TreeView bookmarks;
        private readonly Label noBookmarks;
        private readonly Panel bookmarkPanel;
        private readonly ListView attachments;
        private readonly Panel attachmentPanel;
        private DocumentSession session;
        private DocumentView view;
        private string active;
        private int panelWidth = 190;

        public event EventHandler ExpandedChanged;
        public event EventHandler<ItemEventArgs> StripCommand;
        public ContextMenuStrip PagesOptions { get { return header.OptionsMenu; } }

        public NavigationPane()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Font = Theme.UIFont;
            strip = new NavStrip();
            strip.Dock = DockStyle.Left;
            strip.Add("pages", "pages", "Seiten", false);
            strip.Add("bookmarks", "bookmarks", "Lesezeichen", false);
            strip.Add("attachments", "attachments", "Anlagen", false);
            strip.Add("comments", "comments", "Kommentare", true);
            strip.ItemClicked += OnStripClicked;

            content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = Color.White;

            header = new PaneHeader();
            header.CloseClicked += delegate { Collapse(); };

            thumbnails = new ThumbnailView();
            thumbnails.Dock = DockStyle.Fill;

            bookmarks = new TreeView();
            bookmarks.Dock = DockStyle.Fill;
            bookmarks.BorderStyle = BorderStyle.None;
            bookmarks.HideSelection = false;
            bookmarks.ShowLines = true;
            bookmarks.FullRowSelect = false;
            bookmarks.Font = Theme.UIFont;
            bookmarks.NodeMouseClick += OnBookmarkClick;
            bookmarks.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && bookmarks.SelectedNode != null) NavigateBookmark(bookmarks.SelectedNode);
            };
            noBookmarks = new Label();
            noBookmarks.Text = "Dieses Dokument enthält keine Lesezeichen.";
            noBookmarks.Dock = DockStyle.Top;
            noBookmarks.Height = 40;
            noBookmarks.ForeColor = Color.DimGray;
            noBookmarks.Padding = new Padding(6);
            bookmarkPanel = new Panel();
            bookmarkPanel.Dock = DockStyle.Fill;
            bookmarkPanel.Controls.Add(bookmarks);
            bookmarkPanel.Controls.Add(noBookmarks);

            attachments = new ListView();
            attachments.Dock = DockStyle.Fill;
            attachments.View = System.Windows.Forms.View.Details;
            attachments.FullRowSelect = true;
            attachments.BorderStyle = BorderStyle.None;
            attachments.Columns.Add("Name", 120);
            attachments.Columns.Add("Größe", 60, HorizontalAlignment.Right);
            attachments.DoubleClick += delegate { OpenAttachment(); };
            ToolStrip attTools = new ToolStrip();
            attTools.GripStyle = ToolStripGripStyle.Hidden;
            attTools.Renderer = new Acrobat9Renderer();
            ToolStripButton openAtt = new ToolStripButton("Öffnen", Icons.Get("open"));
            openAtt.Click += delegate { OpenAttachment(); };
            ToolStripButton saveAtt = new ToolStripButton("Speichern", Icons.Get("save"));
            saveAtt.Click += delegate { SaveAttachment(); };
            attTools.Items.Add(openAtt);
            attTools.Items.Add(saveAtt);
            attachmentPanel = new Panel();
            attachmentPanel.Dock = DockStyle.Fill;
            attachmentPanel.Controls.Add(attachments);
            attachmentPanel.Controls.Add(attTools);

            Controls.Add(content);
            Controls.Add(strip);
            Width = strip.Width;
        }

        public ThumbnailView Thumbnails { get { return thumbnails; } }
        public bool IsExpanded { get { return active != null; } }
        public string ActivePanel { get { return active; } }

        public int PanelWidth
        {
            get { return panelWidth; }
            set { panelWidth = Math.Max(120, value); }
        }

        public void SetCommentsChecked(bool on)
        {
            if (on) strip.CheckedKeys.Add("comments"); else strip.CheckedKeys.Remove("comments");
            strip.Invalidate();
        }

        public DocumentView View
        {
            get { return view; }
            set { view = value; thumbnails.View = value; }
        }

        public DocumentSession Session
        {
            get { return session; }
            set
            {
                if (session != null)
                {
                    session.StructureChanged -= OnStructureChanged;
                    session.AnnotationsChanged -= OnAnnotationsChanged;
                }
                session = value;
                if (session != null)
                {
                    session.StructureChanged += OnStructureChanged;
                    session.AnnotationsChanged += OnAnnotationsChanged;
                }
                thumbnails.Session = value;
                LoadBookmarks();
                LoadAttachments();
            }
        }

        private void OnStructureChanged(object sender, EventArgs e)
        {
            LoadBookmarks();
            LoadAttachments();
        }

        private void OnAnnotationsChanged(object sender, EventArgs e)
        {
            thumbnails.ResetThumbnails();
        }

        public bool HasBookmarks { get { return bookmarks.Nodes.Count > 0; } }

        private void OnStripClicked(object sender, ItemEventArgs e)
        {
            if (e.Key == "comments")
            {
                if (StripCommand != null) StripCommand(this, e);
                return;
            }
            if (active == e.Key) Collapse();
            else ShowPanel(e.Key);
        }

        public void ShowPanel(string key)
        {
            bool wasExpanded = IsExpanded;
            active = key;
            strip.ActiveKey = key;
            content.SuspendLayout();
            content.Controls.Clear();
            Control body;
            switch (key)
            {
                case "bookmarks": header.Text = "Lesezeichen"; body = bookmarkPanel; header.OptionsMenu = BookmarkOptions(); break;
                case "attachments": header.Text = "Anlagen"; body = attachmentPanel; header.OptionsMenu = null; break;
                default: header.Text = "Seiten"; body = thumbnails; header.OptionsMenu = pagesOptions; break;
            }
            content.Controls.Add(body);
            content.Controls.Add(header);
            content.ResumeLayout();
            header.Invalidate();
            Width = strip.Width + panelWidth;
            strip.Invalidate();
            if (!wasExpanded && ExpandedChanged != null) ExpandedChanged(this, EventArgs.Empty);
        }

        public void Collapse()
        {
            if (active == null) return;
            panelWidth = Math.Max(120, Width - strip.Width);
            active = null;
            strip.ActiveKey = null;
            content.Controls.Clear();
            Width = strip.Width;
            strip.Invalidate();
            if (ExpandedChanged != null) ExpandedChanged(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (active != null && Width > strip.Width + 60) panelWidth = Width - strip.Width;
        }

        private ContextMenuStrip pagesOptions;

        public void SetPagesOptions(ContextMenuStrip menu)
        {
            pagesOptions = menu;
            if (active == "pages" || active == null) header.OptionsMenu = menu;
        }

        private ContextMenuStrip BookmarkOptions()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new Acrobat9Renderer();
            m.Items.Add("Alle Lesezeichen erweitern", Icons.Get("expandAll"), delegate { bookmarks.ExpandAll(); });
            m.Items.Add("Alle Lesezeichen reduzieren", Icons.Get("collapseAll"), delegate { bookmarks.CollapseAll(); });
            return m;
        }

        // ------------------------------------------------------------------
        // Lesezeichen
        // ------------------------------------------------------------------

        private void LoadBookmarks()
        {
            bookmarks.BeginUpdate();
            bookmarks.Nodes.Clear();
            if (session != null)
            {
                try { AddNodes(bookmarks.Nodes, session.Pdf.GetBookmarks()); }
                catch (Exception) { }
            }
            bookmarks.EndUpdate();
            noBookmarks.Visible = bookmarks.Nodes.Count == 0;
            bookmarks.Visible = bookmarks.Nodes.Count > 0;
        }

        private static void AddNodes(TreeNodeCollection target, List<BookmarkNode> nodes)
        {
            foreach (BookmarkNode b in nodes)
            {
                TreeNode n = new TreeNode(string.IsNullOrEmpty(b.Title) ? "(ohne Titel)" : b.Title);
                n.Tag = b;
                target.Add(n);
                AddNodes(n.Nodes, b.Children);
            }
        }

        private void OnBookmarkClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Button == MouseButtons.Left) NavigateBookmark(e.Node);
        }

        private void NavigateBookmark(TreeNode node)
        {
            BookmarkNode b = node.Tag as BookmarkNode;
            if (b == null || view == null) return;
            LinkTarget t = new LinkTarget();
            t.PageIndex = b.PageIndex;
            t.Y = b.Y;
            t.Uri = b.Uri;
            view.FollowLink(t);
        }

        // ------------------------------------------------------------------
        // Anlagen
        // ------------------------------------------------------------------

        private void LoadAttachments()
        {
            attachments.Items.Clear();
            if (session == null) return;
            try
            {
                foreach (AttachmentInfo a in session.Pdf.GetAttachments())
                {
                    ListViewItem item = new ListViewItem(a.Name);
                    item.SubItems.Add(FormatSize(a.Size));
                    item.Tag = a;
                    attachments.Items.Add(item);
                }
            }
            catch (Exception) { }
        }

        public bool HasAttachments { get { return attachments.Items.Count > 0; } }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " Byte";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("0.00") + " MB";
        }

        private AttachmentInfo SelectedAttachment
        {
            get { return attachments.SelectedItems.Count > 0 ? attachments.SelectedItems[0].Tag as AttachmentInfo : null; }
        }

        private void SaveAttachment()
        {
            AttachmentInfo a = SelectedAttachment;
            if (a == null) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.FileName = a.Name;
                d.Title = "Anlage speichern";
                if (d.ShowDialog(FindForm()) != DialogResult.OK) return;
                byte[] data = session.Pdf.GetAttachmentData(a.Index);
                if (data != null) File.WriteAllBytes(d.FileName, data);
            }
        }

        private void OpenAttachment()
        {
            AttachmentInfo a = SelectedAttachment;
            if (a == null) return;
            DialogResult r = MessageBox.Show(FindForm(),
                "Die Anlage \"" + a.Name + "\" wird mit der zugeordneten Anwendung geöffnet.\n\nAnlagen können Viren oder andere schädliche Inhalte enthalten. Öffnen Sie nur Dateien aus vertrauenswürdigen Quellen.\n\nMöchten Sie fortfahren?",
                "Anlage öffnen", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
            byte[] data = session.Pdf.GetAttachmentData(a.Index);
            if (data == null) return;
            string dir = Path.Combine(Path.GetTempPath(), "AcroClone");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, Path.GetFileName(a.Name));
            File.WriteAllBytes(path, data);
            try { Process.Start(path); }
            catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
