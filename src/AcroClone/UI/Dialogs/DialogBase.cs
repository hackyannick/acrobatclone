using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AcroClone.UI.Dialogs
{
    /// <summary>Basisklasse für Dialoge mit kleinen Hilfsfunktionen für das Layout.</summary>
    public class DialogBase : Form
    {
        protected readonly Button OkButton;
        protected readonly Button CancelBtn;

        public DialogBase(string title, int width, int height)
        {
            Text = title;
            Font = Theme.UIFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(width, height);
            BackColor = Color.FromArgb(240, 240, 240);

            OkButton = new Button();
            OkButton.Text = "OK";
            OkButton.DialogResult = DialogResult.OK;
            OkButton.SetBounds(width - 176, height - 34, 80, 24);
            OkButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            OkButton.Click += delegate(object s, EventArgs e)
            {
                if (!ValidateInput()) DialogResult = DialogResult.None;
            };
            CancelBtn = new Button();
            CancelBtn.Text = "Abbrechen";
            CancelBtn.DialogResult = DialogResult.Cancel;
            CancelBtn.SetBounds(width - 90, height - 34, 80, 24);
            CancelBtn.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            Controls.Add(OkButton);
            Controls.Add(CancelBtn);
            AcceptButton = OkButton;
            CancelButton = CancelBtn;
        }

        protected virtual bool ValidateInput() { return true; }

        protected Label AddLabel(Control parent, string text, int x, int y, int w)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = false;
            l.SetBounds(x, y + 3, w, 16);
            parent.Controls.Add(l);
            return l;
        }

        protected TextBox AddText(Control parent, string text, int x, int y, int w)
        {
            TextBox t = new TextBox();
            t.Text = text ?? string.Empty;
            t.SetBounds(x, y, w, 20);
            parent.Controls.Add(t);
            return t;
        }

        protected NumericUpDown AddNumber(Control parent, decimal value, decimal min, decimal max, int decimals, int x, int y, int w)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = min;
            n.Maximum = max;
            n.DecimalPlaces = decimals;
            n.Value = Math.Max(min, Math.Min(max, value));
            n.SetBounds(x, y, w, 20);
            n.TextAlign = HorizontalAlignment.Right;
            parent.Controls.Add(n);
            return n;
        }

        protected ComboBox AddCombo(Control parent, string[] items, int selected, int x, int y, int w)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Items.AddRange(items);
            c.SetBounds(x, y, w, 21);
            if (items.Length > 0) c.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, selected));
            parent.Controls.Add(c);
            return c;
        }

        protected CheckBox AddCheck(Control parent, string text, bool value, int x, int y, int w)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Checked = value;
            c.SetBounds(x, y, w, 20);
            parent.Controls.Add(c);
            return c;
        }

        protected RadioButton AddRadio(Control parent, string text, bool value, int x, int y, int w)
        {
            RadioButton r = new RadioButton();
            r.Text = text;
            r.Checked = value;
            r.SetBounds(x, y, w, 20);
            parent.Controls.Add(r);
            return r;
        }

        protected GroupBox AddGroup(string text, int x, int y, int w, int h)
        {
            GroupBox g = new GroupBox();
            g.Text = text;
            g.SetBounds(x, y, w, h);
            Controls.Add(g);
            return g;
        }

        protected Button AddButton(Control parent, string text, int x, int y, int w, EventHandler click)
        {
            Button b = new Button();
            b.Text = text;
            b.SetBounds(x, y, w, 24);
            b.Click += click;
            parent.Controls.Add(b);
            return b;
        }

        /// <summary>Farbauswahl-Schaltfläche.</summary>
        protected ColorButton AddColor(Control parent, Color color, int x, int y)
        {
            ColorButton b = new ColorButton();
            b.Color = color;
            b.SetBounds(x, y, 46, 22);
            parent.Controls.Add(b);
            return b;
        }

        protected void Error(string message)
        {
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public class ColorButton : Button
    {
        private Color color = Color.Red;
        public bool AllowNone;
        public event EventHandler ColorChanged;

        public Color Color
        {
            get { return color; }
            set { color = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            Rectangle r = new Rectangle(5, 5, Width - 20, Height - 10);
            if (color.IsEmpty || color.A == 0)
            {
                pevent.Graphics.FillRectangle(Brushes.White, r);
                pevent.Graphics.DrawLine(Pens.Red, r.Left, r.Bottom, r.Right, r.Top);
            }
            else
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(255, color))) pevent.Graphics.FillRectangle(b, r);
            }
            pevent.Graphics.DrawRectangle(Pens.DimGray, r);
            pevent.Graphics.FillPolygon(Brushes.Black, new Point[] { new Point(Width - 12, Height / 2 - 1), new Point(Width - 6, Height / 2 - 1), new Point(Width - 9, Height / 2 + 2) });
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new Acrobat9Renderer();
            Color[] palette =
            {
                Color.Black, Color.FromArgb(128, 128, 128), Color.White, Color.FromArgb(220, 0, 0), Color.FromArgb(255, 128, 0),
                Color.FromArgb(255, 230, 50), Color.FromArgb(255, 240, 0), Color.FromArgb(0, 150, 60), Color.FromArgb(0, 200, 200),
                Color.FromArgb(20, 70, 200), Color.FromArgb(20, 40, 120), Color.FromArgb(140, 40, 160), Color.FromArgb(255, 150, 200)
            };
            if (AllowNone)
            {
                m.Items.Add("Keine Farbe", null, delegate { Color = Color.Empty; Raise(); });
                m.Items.Add(new ToolStripSeparator());
            }
            foreach (Color c in palette)
            {
                Color cc = c;
                Bitmap sw = new Bitmap(16, 16);
                using (Graphics g = Graphics.FromImage(sw))
                {
                    using (SolidBrush b = new SolidBrush(cc)) g.FillRectangle(b, 1, 1, 14, 14);
                    g.DrawRectangle(Pens.DimGray, 1, 1, 13, 13);
                }
                m.Items.Add("#" + cc.R.ToString("X2") + cc.G.ToString("X2") + cc.B.ToString("X2"), sw, delegate { Color = cc; Raise(); });
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Weitere Farben...", null, delegate
            {
                using (ColorDialog d = new ColorDialog())
                {
                    d.FullOpen = true;
                    if (!color.IsEmpty) d.Color = color;
                    if (d.ShowDialog(FindForm()) == DialogResult.OK) { Color = d.Color; Raise(); }
                }
            });
            m.Show(this, new Point(0, Height));
        }

        private void Raise()
        {
            if (ColorChanged != null) ColorChanged(this, EventArgs.Empty);
        }
    }

    /// <summary>Gruppe "Seitenbereich" mit Alle / Aktuelle / Ausgewählte / Von-Bis.</summary>
    public class PageRangeGroup : GroupBox
    {
        private readonly RadioButton all, current, selected, range;
        private readonly NumericUpDown from, to;
        private readonly int pageCount, currentPage;
        private readonly IList<int> selection;
        private readonly ComboBox subset;

        public PageRangeGroup(int pageCount, int currentPage, IList<int> selection, bool withSubset)
        {
            this.pageCount = pageCount;
            this.currentPage = currentPage;
            this.selection = selection ?? new List<int>();
            Text = "Seitenbereich";
            Height = withSubset ? 128 : 104;
            all = new RadioButton(); all.Text = "Alle"; all.SetBounds(12, 20, 120, 20);
            current = new RadioButton(); current.Text = "Aktuelle Seite"; current.SetBounds(12, 40, 120, 20);
            selected = new RadioButton(); selected.Text = "Ausgewählte Seiten (" + this.selection.Count + ")"; selected.SetBounds(140, 40, 180, 20);
            selected.Enabled = this.selection.Count > 0;
            range = new RadioButton(); range.Text = "Seiten von:"; range.SetBounds(12, 64, 90, 20);
            from = new NumericUpDown(); from.Minimum = 1; from.Maximum = Math.Max(1, pageCount); from.Value = currentPage + 1; from.SetBounds(104, 64, 56, 20);
            Label bis = new Label(); bis.Text = "bis:"; bis.SetBounds(166, 67, 26, 16);
            to = new NumericUpDown(); to.Minimum = 1; to.Maximum = Math.Max(1, pageCount); to.Value = currentPage + 1; to.SetBounds(194, 64, 56, 20);
            Label von = new Label(); von.Text = "von " + pageCount; von.SetBounds(256, 67, 70, 16);
            from.ValueChanged += delegate { range.Checked = true; if (to.Value < from.Value) to.Value = from.Value; };
            to.ValueChanged += delegate { range.Checked = true; if (from.Value > to.Value) from.Value = to.Value; };
            Controls.AddRange(new Control[] { all, current, selected, range, from, bis, to, von });
            if (withSubset)
            {
                Label l = new Label(); l.Text = "Teilmenge:"; l.SetBounds(12, 97, 70, 16);
                subset = new ComboBox();
                subset.DropDownStyle = ComboBoxStyle.DropDownList;
                subset.Items.AddRange(new object[] { "Gerade und ungerade Seiten", "Nur gerade Seiten", "Nur ungerade Seiten" });
                subset.SelectedIndex = 0;
                subset.SetBounds(104, 94, 200, 21);
                Controls.Add(l);
                Controls.Add(subset);
            }
            if (this.selection.Count > 1) selected.Checked = true;
            else current.Checked = true;
        }

        public void SelectAll() { all.Checked = true; }

        public List<int> GetPages()
        {
            List<int> pages = new List<int>();
            if (all.Checked) for (int i = 0; i < pageCount; i++) pages.Add(i);
            else if (current.Checked) pages.Add(currentPage);
            else if (selected.Checked) pages.AddRange(selection);
            else for (int i = (int)from.Value - 1; i <= (int)to.Value - 1; i++) pages.Add(i);
            if (subset != null && subset.SelectedIndex > 0)
            {
                bool even = subset.SelectedIndex == 1;
                pages = pages.FindAll(p => ((p + 1) % 2 == 0) == even);
            }
            return pages;
        }

        public string Description
        {
            get
            {
                if (all.Checked) return "alle Seiten";
                if (current.Checked) return "Seite " + (currentPage + 1);
                if (selected.Checked) return selection.Count + " ausgewählte Seite(n)";
                return "Seiten " + from.Value + " bis " + to.Value;
            }
        }
    }
}
