using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AcroClone.Model;
using AcroClone.Pdf;

namespace AcroClone.UI
{
    /// <summary>Erweiterte Suche (rechter Fensterbereich).</summary>
    public class SearchPane : Control
    {
        private readonly PaneHeader header;
        private readonly TextBox query;
        private readonly CheckBox wholeWords, matchCase;
        private readonly Button searchButton;
        private readonly Label status;
        private readonly ListBox results;
        private readonly ProgressBar progress;
        private DocumentSession session;
        private DocumentView view;
        private bool searching, cancel;

        public event EventHandler CloseRequested;

        public SearchPane()
        {
            Font = Theme.UIFont;
            BackColor = Color.FromArgb(245, 246, 248);
            header = new PaneHeader();
            header.Text = "Suchen";
            header.CloseClicked += delegate { cancel = true; if (CloseRequested != null) CloseRequested(this, EventArgs.Empty); };

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 150;
            top.Padding = new Padding(8);

            Label l1 = new Label();
            l1.Text = "Wonach möchten Sie suchen?";
            l1.SetBounds(8, 8, 220, 16);
            query = new TextBox();
            query.SetBounds(8, 28, 200, 20);
            query.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            query.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; StartSearch(); } };
            wholeWords = new CheckBox();
            wholeWords.Text = "Nur ganze Wörter";
            wholeWords.SetBounds(8, 54, 200, 18);
            matchCase = new CheckBox();
            matchCase.Text = "Groß-/Kleinschreibung beachten";
            matchCase.SetBounds(8, 74, 220, 18);
            searchButton = new Button();
            searchButton.Text = "Suchen";
            searchButton.SetBounds(8, 98, 90, 24);
            searchButton.Click += delegate { if (searching) cancel = true; else StartSearch(); };
            progress = new ProgressBar();
            progress.SetBounds(106, 101, 100, 18);
            progress.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            progress.Visible = false;
            status = new Label();
            status.SetBounds(8, 128, 220, 18);
            status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            status.Font = Theme.UIFontBold;
            top.Controls.AddRange(new Control[] { l1, query, wholeWords, matchCase, searchButton, progress, status });

            results = new ListBox();
            results.Dock = DockStyle.Fill;
            results.BorderStyle = BorderStyle.None;
            results.DrawMode = DrawMode.OwnerDrawFixed;
            results.ItemHeight = 34;
            results.IntegralHeight = false;
            results.DrawItem += OnDrawItem;
            results.SelectedIndexChanged += delegate
            {
                TextMatch m = results.SelectedItem as TextMatch;
                if (m != null && view != null) view.ShowSearchMatch(m);
            };

            Controls.Add(results);
            Controls.Add(top);
            Controls.Add(header);
            Width = 250;
        }

        public DocumentView View { get { return view; } set { view = value; } }

        public DocumentSession Session
        {
            get { return session; }
            set
            {
                cancel = true;
                session = value;
                results.Items.Clear();
                status.Text = string.Empty;
            }
        }

        public void Activate(string text)
        {
            if (!string.IsNullOrEmpty(text)) query.Text = text;
            query.Focus();
            query.SelectAll();
        }

        private void StartSearch()
        {
            if (session == null || searching || query.Text.Length == 0) return;
            searching = true;
            cancel = false;
            searchButton.Text = "Stopp";
            results.Items.Clear();
            int n = session.PageCount;
            progress.Maximum = Math.Max(1, n);
            progress.Value = 0;
            progress.Visible = true;
            int found = 0, pagesWithHits = 0;
            DocumentSession s = session;
            try
            {
                for (int i = 0; i < n && !cancel; i++)
                {
                    List<TextMatch> list = s.Pdf.FindOnPage(i, query.Text, matchCase.Checked, wholeWords.Checked);
                    if (list.Count > 0) pagesWithHits++;
                    foreach (TextMatch m in list) results.Items.Add(m);
                    found += list.Count;
                    progress.Value = i + 1;
                    status.Text = "Seite " + (i + 1) + " von " + n + " …";
                    if (i % 5 == 0) Application.DoEvents();
                    if (session != s) return;
                }
                status.Text = cancel ? "Suche abgebrochen: " + found + " Treffer" :
                    found == 0 ? "Keine Treffer gefunden." : found + " Treffer auf " + pagesWithHits + (pagesWithHits == 1 ? " Seite" : " Seiten");
            }
            finally
            {
                searching = false;
                progress.Visible = false;
                searchButton.Text = "Suchen";
            }
            if (results.Items.Count > 0) results.SelectedIndex = 0;
        }

        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            TextMatch m = (TextMatch)results.Items[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            Color fore = sel ? SystemColors.HighlightText : Color.Black;
            Rectangle r = e.Bounds;
            TextRenderer.DrawText(e.Graphics, "Seite " + (m.Page + 1), Theme.UIFontBold, new Rectangle(r.X + 4, r.Y + 2, r.Width - 8, 14), sel ? fore : Color.FromArgb(40, 70, 140), TextFormatFlags.NoPrefix);
            string ctx = m.Context ?? string.Empty;
            int ms = Math.Min(m.ContextMatchStart, ctx.Length);
            int ml = Math.Min(m.Count, ctx.Length - ms);
            string before = ctx.Substring(0, ms), match = ctx.Substring(ms, ml), after = ctx.Substring(ms + ml);
            int x = r.X + 4;
            int y = r.Y + 17;
            TextFormatFlags f = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            Size sb = TextRenderer.MeasureText(e.Graphics, before, Theme.UIFont, Size.Empty, f);
            TextRenderer.DrawText(e.Graphics, before, Theme.UIFont, new Point(x, y), fore, f);
            x += sb.Width;
            Size sm = TextRenderer.MeasureText(e.Graphics, match, Theme.UIFontBold, Size.Empty, f);
            TextRenderer.DrawText(e.Graphics, match, Theme.UIFontBold, new Point(x, y), fore, f);
            x += sm.Width;
            TextRenderer.DrawText(e.Graphics, after, Theme.UIFont, new Rectangle(x, y, Math.Max(0, r.Right - x), 15), fore, f | TextFormatFlags.EndEllipsis);
            using (Pen p = new Pen(Color.FromArgb(225, 228, 232))) e.Graphics.DrawLine(p, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
        }
    }
}
