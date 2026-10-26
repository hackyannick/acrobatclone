using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using AcroClone.Model;

namespace AcroClone.UI
{
    /// <summary>Kommentarliste am unteren Fensterrand.</summary>
    public class CommentsPane : Control
    {
        private readonly PaneHeader header;
        private readonly ToolStrip tools;
        private readonly TreeView tree;
        private readonly ImageList images;
        private readonly ToolStripTextBox filter;
        private DocumentSession session;
        private DocumentView view;
        private string sortMode = "page";
        private bool suppressSelect;

        public event EventHandler CloseRequested;
        public event EventHandler PropertiesRequested;

        public CommentsPane()
        {
            Font = Theme.UIFont;
            header = new PaneHeader();
            header.Text = "Kommentare";
            header.CloseClicked += delegate { if (CloseRequested != null) CloseRequested(this, EventArgs.Empty); };

            tools = new ToolStrip();
            tools.GripStyle = ToolStripGripStyle.Hidden;
            tools.Renderer = new Acrobat9Renderer();
            tools.Items.Add(Button("Alle erweitern", "expandAll", delegate { tree.ExpandAll(); }));
            tools.Items.Add(Button("Alle reduzieren", "collapseAll", delegate { tree.CollapseAll(); }));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Button("Vorheriger", "prevPage", delegate { Step(-1); }, true));
            tools.Items.Add(Button("Nächster", "nextPage", delegate { Step(1); }, true));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Button("Häkchen", "check", delegate { ToggleCheck(); }));
            tools.Items.Add(Button("Löschen", "delete", delegate { DeleteSelected(); }));
            tools.Items.Add(Button("Eigenschaften", "properties", delegate { if (PropertiesRequested != null) PropertiesRequested(this, EventArgs.Empty); }));
            tools.Items.Add(new ToolStripSeparator());
            ToolStripDropDownButton sort = new ToolStripDropDownButton("Sortieren nach", Icons.Get("sort"));
            sort.DropDownItems.Add("Seite", null, delegate { sortMode = "page"; Reload(); });
            sort.DropDownItems.Add("Typ", null, delegate { sortMode = "type"; Reload(); });
            sort.DropDownItems.Add("Autor", null, delegate { sortMode = "author"; Reload(); });
            sort.DropDownItems.Add("Datum", null, delegate { sortMode = "date"; Reload(); });
            sort.DropDownItems.Add("Häkchenstatus", null, delegate { sortMode = "check"; Reload(); });
            tools.Items.Add(sort);
            ToolStripLabel fl = new ToolStripLabel(Icons.Get("find"));
            fl.Alignment = ToolStripItemAlignment.Right;
            filter = new ToolStripTextBox();
            filter.Alignment = ToolStripItemAlignment.Right;
            filter.AutoSize = false;
            filter.Size = new Size(140, 21);
            filter.ToolTipText = "Kommentare durchsuchen";
            filter.TextChanged += delegate { Reload(); };
            tools.Items.Add(filter);
            tools.Items.Add(fl);

            images = new ImageList();
            images.ColorDepth = ColorDepth.Depth32Bit;
            images.ImageSize = new Size(16, 16);
            foreach (string k in new string[] { "pages", "note", "highlight", "underline", "strikeout", "textbox", "rectangle", "ellipse", "line", "arrow", "pencil", "stamp", "sign", "redact", "check" })
                images.Images.Add(k, Icons.Get(k));

            tree = new TreeView();
            tree.Dock = DockStyle.Fill;
            tree.BorderStyle = BorderStyle.None;
            tree.ImageList = images;
            tree.HideSelection = false;
            tree.FullRowSelect = true;
            tree.ShowLines = false;
            tree.ItemHeight = 20;
            tree.Font = Theme.UIFont;
            tree.AfterSelect += OnAfterSelect;
            tree.NodeMouseDoubleClick += delegate(object s, TreeNodeMouseClickEventArgs e)
            {
                Annotation a = e.Node.Tag as Annotation;
                if (a != null && view != null) view.OpenAnnotation(a);
            };
            tree.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
            };

            Controls.Add(tree);
            Controls.Add(tools);
            Controls.Add(header);
            Height = 180;
        }

        private ToolStripButton Button(string text, string icon, EventHandler click)
        {
            return Button(text, icon, click, false);
        }

        private ToolStripButton Button(string text, string icon, EventHandler click, bool imageOnly)
        {
            ToolStripButton b = new ToolStripButton(text, Icons.Get(icon), click);
            b.DisplayStyle = imageOnly ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.ImageAndText;
            b.ToolTipText = text;
            return b;
        }

        public DocumentView View
        {
            get { return view; }
            set
            {
                if (view != null) view.SelectionChanged -= OnViewSelection;
                view = value;
                if (view != null) view.SelectionChanged += OnViewSelection;
            }
        }

        public DocumentSession Session
        {
            get { return session; }
            set
            {
                if (session != null) session.AnnotationsChanged -= OnAnnotationsChanged;
                session = value;
                if (session != null) session.AnnotationsChanged += OnAnnotationsChanged;
                Reload();
            }
        }

        private void OnAnnotationsChanged(object sender, EventArgs e)
        {
            if (Visible) Reload();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) Reload();
        }

        private static string IconFor(AnnotationType t)
        {
            switch (t)
            {
                case AnnotationType.Note: return "note";
                case AnnotationType.Highlight: return "highlight";
                case AnnotationType.Underline: return "underline";
                case AnnotationType.StrikeOut: return "strikeout";
                case AnnotationType.FreeText: return "textbox";
                case AnnotationType.Rectangle: return "rectangle";
                case AnnotationType.Ellipse: return "ellipse";
                case AnnotationType.Line: return "line";
                case AnnotationType.Arrow: return "arrow";
                case AnnotationType.Ink: return "pencil";
                case AnnotationType.Stamp: return "stamp";
                case AnnotationType.Signature: return "sign";
                default: return "redact";
            }
        }

        public static string Summary(Annotation a)
        {
            string text = a.Contents;
            if (string.IsNullOrEmpty(text) && (a.Type == AnnotationType.FreeText || a.Type == AnnotationType.Stamp || a.Type == AnnotationType.Signature))
                text = a.Text;
            text = (text ?? string.Empty).Replace("\n", " ");
            if (text.Length > 90) text = text.Substring(0, 90) + "…";
            string s = a.TypeName + "   " + a.Author + "   " + a.Modified.ToString("dd.MM.yyyy HH:mm");
            if (a.Checked) s = "✓ " + s;
            if (text.Length > 0) s += "   –   " + text;
            return s;
        }

        public void Reload()
        {
            if (!Visible && session != null) return;
            string selectedId = tree.SelectedNode != null && tree.SelectedNode.Tag is Annotation ? ((Annotation)tree.SelectedNode.Tag).Id : null;
            tree.BeginUpdate();
            tree.Nodes.Clear();
            if (session != null)
            {
                string f = filter.Text.Trim();
                IEnumerable<Annotation> list = session.Annotations.Where(a => f.Length == 0 ||
                    (a.Contents ?? "").IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    (a.Text ?? "").IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    (a.Author ?? "").IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    a.TypeName.IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0);
                Func<Annotation, string> group;
                switch (sortMode)
                {
                    case "type": group = a => a.TypeName; list = list.OrderBy(a => a.TypeName).ThenBy(a => a.Page); break;
                    case "author": group = a => string.IsNullOrEmpty(a.Author) ? "(Unbekannt)" : a.Author; list = list.OrderBy(a => a.Author).ThenBy(a => a.Page); break;
                    case "date": group = a => a.Modified.ToString("dd.MM.yyyy"); list = list.OrderByDescending(a => a.Modified); break;
                    case "check": group = a => a.Checked ? "Mit Häkchen" : "Ohne Häkchen"; list = list.OrderBy(a => a.Checked).ThenBy(a => a.Page); break;
                    default: group = a => "Seite " + (a.Page + 1); list = list.OrderBy(a => a.Page).ThenByDescending(a => a.Bounds.Top); break;
                }
                TreeNode current = null;
                string currentKey = null;
                foreach (Annotation a in list)
                {
                    string key = group(a);
                    if (key != currentKey)
                    {
                        current = new TreeNode(key);
                        current.ImageKey = current.SelectedImageKey = "pages";
                        current.NodeFont = Theme.UIFontBold;
                        tree.Nodes.Add(current);
                        currentKey = key;
                    }
                    TreeNode n = new TreeNode(Summary(a));
                    n.Tag = a;
                    n.ImageKey = n.SelectedImageKey = IconFor(a.Type);
                    current.Nodes.Add(n);
                    if (a.Id == selectedId) tree.SelectedNode = n;
                }
                tree.ExpandAll();
                int total = session.Annotations.Count;
                header.Text = "Kommentare (" + total + ")";
                header.Invalidate();
            }
            tree.EndUpdate();
        }

        private void OnAfterSelect(object sender, TreeViewEventArgs e)
        {
            if (suppressSelect) return;
            Annotation a = e.Node.Tag as Annotation;
            if (a != null && view != null) view.SelectAnnotation(a, true);
        }

        private void OnViewSelection(object sender, EventArgs e)
        {
            if (!Visible || view == null) return;
            Annotation sel = view.SelectedAnnotation;
            suppressSelect = true;
            foreach (TreeNode g in tree.Nodes)
                foreach (TreeNode n in g.Nodes)
                    if (n.Tag == sel) { tree.SelectedNode = n; n.EnsureVisible(); }
            suppressSelect = false;
        }

        private Annotation SelectedAnnotation
        {
            get { return tree.SelectedNode != null ? tree.SelectedNode.Tag as Annotation : null; }
        }

        private void Step(int dir)
        {
            List<TreeNode> all = new List<TreeNode>();
            foreach (TreeNode g in tree.Nodes) foreach (TreeNode n in g.Nodes) all.Add(n);
            if (all.Count == 0) return;
            int idx = all.IndexOf(tree.SelectedNode);
            idx = idx < 0 ? 0 : Math.Max(0, Math.Min(all.Count - 1, idx + dir));
            tree.SelectedNode = all[idx];
        }

        private void ToggleCheck()
        {
            Annotation a = SelectedAnnotation;
            if (a == null || session == null) return;
            session.BeginChange(false, "Häkchen setzen");
            a.Checked = !a.Checked;
            session.CommitAnnotationChange();
        }

        private void DeleteSelected()
        {
            Annotation a = SelectedAnnotation;
            if (a == null || session == null) return;
            if (view != null && view.SelectedAnnotation == a) view.DeleteSelected();
            else session.RemoveAnnotations(new Annotation[] { a });
        }
    }
}
