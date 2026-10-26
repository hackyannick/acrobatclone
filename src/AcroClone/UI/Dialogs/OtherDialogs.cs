using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using AcroClone.Model;
using AcroClone.Pdf;

namespace AcroClone.UI.Dialogs
{
    public class DocumentPropertiesDialog : DialogBase
    {
        private readonly DocumentSession s;
        private readonly TextBox title, author, subject, keywords;

        public DocumentPropertiesDialog(DocumentSession session, int currentPage) : base("Dokumenteigenschaften", 500, 470)
        {
            s = session;
            TabControl tabs = new TabControl();
            tabs.SetBounds(10, 10, 480, 410);
            TabPage desc = new TabPage("Beschreibung");
            TabPage sec = new TabPage("Sicherheit");
            TabPage adv = new TabPage("Erweitert");
            tabs.TabPages.AddRange(new TabPage[] { desc, sec, adv });
            Controls.Add(tabs);

            GroupBox g = new GroupBox();
            g.Text = "Beschreibung";
            g.SetBounds(8, 8, 456, 150);
            desc.Controls.Add(g);
            AddLabel(g, "Datei:", 12, 20, 80);
            AddLabel(g, s.DisplayName, 100, 20, 340);
            AddLabel(g, "Titel:", 12, 44, 80);
            title = AddText(g, s.GetInfo("Title"), 100, 42, 340);
            AddLabel(g, "Verfasser:", 12, 70, 80);
            author = AddText(g, s.GetInfo("Author"), 100, 68, 340);
            AddLabel(g, "Thema:", 12, 96, 80);
            subject = AddText(g, s.GetInfo("Subject"), 100, 94, 340);
            AddLabel(g, "Stichwörter:", 12, 122, 80);
            keywords = AddText(g, s.GetInfo("Keywords"), 100, 120, 340);

            int y = 168;
            Row(desc, "Erstellt:", FormatDate(s.GetInfo("CreationDate")), ref y);
            Row(desc, "Geändert:", FormatDate(s.GetInfo("ModDate")), ref y);
            Row(desc, "Anwendung:", s.GetInfo("Creator"), ref y);
            y += 6;
            GroupBox ext = new GroupBox();
            ext.Text = "Erweitert";
            ext.SetBounds(8, y, 456, 150);
            desc.Controls.Add(ext);
            int y2 = 18;
            Row(ext, "PDF-Produzent:", s.GetInfo("Producer"), ref y2);
            Row(ext, "PDF-Version:", s.Pdf.FileVersion, ref y2);
            Row(ext, "Speicherort:", s.FilePath != null ? Path.GetDirectoryName(s.FilePath) : "(nicht gespeichert)", ref y2);
            Row(ext, "Dateigröße:", s.FileSize > 0 ? NavigationPane.FormatSize(s.FileSize) + " (" + s.FileSize.ToString("N0") + " Byte)" : "–", ref y2);
            SizeF ps = s.Pdf.GetPageSize(Math.Max(0, Math.Min(currentPage, s.PageCount - 1)));
            Row(ext, "Seitenformat:", (ps.Width / 72 * 25.4).ToString("0.0") + " x " + (ps.Height / 72 * 25.4).ToString("0.0") + " mm", ref y2);
            Row(ext, "Anzahl Seiten:", s.PageCount.ToString(), ref y2);

            // Sicherheit
            uint perm = s.Pdf.Permissions;
            int rev = s.Pdf.SecurityRevision;
            bool secured = rev >= 0 && perm != 0xFFFFFFFF;
            int sy = 12;
            Row(sec, "Sicherheitsmethode:", rev < 0 ? "Keine Sicherheit" : "Kennwortschutz (Revision " + rev + ")", ref sy);
            sy += 10;
            Label h = AddLabel(sec, "Übersicht über Dokumenteinschränkungen", 12, sy, 400);
            h.Font = Theme.UIFontBold;
            sy += 24;
            Row(sec, "Drucken:", Allowed(!secured || (perm & 4) != 0), ref sy);
            Row(sec, "Ändern des Dokuments:", Allowed(!secured || (perm & 8) != 0), ref sy);
            Row(sec, "Kopieren von Inhalt:", Allowed(!secured || (perm & 16) != 0), ref sy);
            Row(sec, "Kommentieren:", Allowed(!secured || (perm & 32) != 0), ref sy);
            Row(sec, "Ausfüllen von Formularfeldern:", Allowed(!secured || (perm & 256) != 0), ref sy);
            Row(sec, "Zusammenstellen von Dokumenten:", Allowed(!secured || (perm & 1024) != 0), ref sy);
            Row(sec, "Drucken mit hoher Auflösung:", Allowed(!secured || (perm & 2048) != 0), ref sy);

            int ay = 12;
            Row(adv, "Formulartyp:", s.Pdf.HasForm ? "AcroForm (interaktive Felder)" : "Keine Formularfelder", ref ay);
            Row(adv, "Kommentare:", s.Annotations.Count.ToString(), ref ay);
            Row(adv, "Lesezeichen:", s.Pdf.GetBookmarks().Count > 0 ? "Vorhanden" : "Keine", ref ay);
            Row(adv, "Anlagen:", s.Pdf.GetAttachments().Count.ToString(), ref ay);
            string label = s.Pdf.GetPageLabel(0);
            Row(adv, "Seitenbeschriftungen:", string.IsNullOrEmpty(label) ? "Keine" : "Vorhanden (z. B. \"" + label + "\")", ref ay);
        }

        private void Row(Control parent, string label, string value, ref int y)
        {
            AddLabel(parent, label, 12, y, 180);
            Label v = AddLabel(parent, string.IsNullOrEmpty(value) ? "–" : value, 196, y, 260);
            v.AutoEllipsis = true;
            y += 22;
        }

        private static string Allowed(bool ok) { return ok ? "Zulässig" : "Nicht zulässig"; }

        private static string FormatDate(string pdfDate)
        {
            DateTime? d = InfoDictionaryWriter.ParseDate(pdfDate);
            return d.HasValue ? d.Value.ToString("dd.MM.yyyy HH:mm:ss") : (pdfDate ?? "");
        }

        public void Apply()
        {
            s.SetInfo("Title", title.Text);
            s.SetInfo("Author", author.Text);
            s.SetInfo("Subject", subject.Text);
            s.SetInfo("Keywords", keywords.Text);
        }
    }

    public class PreferencesDialog : DialogBase
    {
        private readonly ListBox categories;
        private readonly Panel host;
        private readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
        private CheckBox showWelcome, keepTool, highlightForms, defaultSelect;
        private NumericUpDown recentCount, fontSize, shapeWidth, inkWidth;
        private ComboBox layout, zoom;
        private TextBox name, org, email;

        public static readonly string[] LayoutNames = { "Automatisch", "Einzelne Seite", "Einzelne Seite fortlaufend", "Zweiseitenansicht", "Zwei Seiten fortlaufend" };
        public static readonly string[] ZoomNames = { "Automatisch", "Fensterbreite", "Ganze Seite", "50 %", "75 %", "100 %", "125 %", "150 %", "200 %" };

        public PreferencesDialog() : base("Voreinstellungen", 600, 400)
        {
            AddLabel(this, "Kategorien:", 12, 8, 120);
            categories = new ListBox();
            categories.SetBounds(12, 28, 150, 320);
            categories.IntegralHeight = false;
            categories.Items.AddRange(new object[] { "Allgemein", "Identität", "Kommentieren", "Seitenanzeige" });
            Controls.Add(categories);
            host = new Panel();
            host.SetBounds(172, 28, 416, 320);
            host.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(host);
            categories.SelectedIndexChanged += delegate { ShowPage((string)categories.SelectedItem); };

            Panel p = NewPage("Allgemein");
            showWelcome = AddCheck(p, "Begrüßungsbildschirm anzeigen, wenn kein Dokument geöffnet ist", AppSettings.GetBool("ShowWelcome", true), 12, 32, 390);
            keepTool = AddCheck(p, "Kommentarwerkzeug nach Verwendung ausgewählt lassen", AppSettings.GetBool("KeepTool", false), 12, 56, 390);
            defaultSelect = AddCheck(p, "Auswahlwerkzeug statt Hand-Werkzeug als Standard", AppSettings.GetBool("DefaultToolSelect", false), 12, 80, 390);
            AddLabel(p, "Liste der zuletzt verwendeten Dokumente:", 12, 112, 240);
            recentCount = AddNumber(p, AppSettings.GetInt("RecentCount", 8), 1, 20, 0, 256, 110, 50);
            AddLabel(p, "Einträge", 312, 112, 80);

            p = NewPage("Identität");
            AddLabel(p, "Name (Autor für Kommentare):", 12, 32, 200);
            name = AddText(p, AppSettings.Author, 12, 52, 300);
            AddLabel(p, "Organisation:", 12, 84, 200);
            org = AddText(p, AppSettings.Get("Organization", ""), 12, 104, 300);
            AddLabel(p, "E-Mail-Adresse:", 12, 136, 200);
            email = AddText(p, AppSettings.Get("Email", ""), 12, 156, 300);

            p = NewPage("Kommentieren");
            AddLabel(p, "Schriftgröße für Textfelder (pt):", 12, 32, 230);
            fontSize = AddNumber(p, AppSettings.GetInt("FreeText.FontSize", 12), 4, 72, 0, 250, 30, 60);
            AddLabel(p, "Linienstärke für Formen (pt):", 12, 62, 230);
            shapeWidth = AddNumber(p, AppSettings.GetInt("Width.Shapes", 2), 1, 24, 0, 250, 60, 60);
            AddLabel(p, "Linienstärke für Bleistift (pt):", 12, 92, 230);
            inkWidth = AddNumber(p, AppSettings.GetInt("Width.Ink", 2), 1, 24, 0, 250, 90, 60);

            p = NewPage("Seitenanzeige");
            AddLabel(p, "Seitenlayout:", 12, 32, 120);
            layout = AddCombo(p, LayoutNames, AppSettings.GetInt("DefaultLayout", 0), 140, 30, 220);
            AddLabel(p, "Zoom:", 12, 62, 120);
            zoom = AddCombo(p, ZoomNames, AppSettings.GetInt("DefaultZoom", 0), 140, 60, 220);
            highlightForms = AddCheck(p, "Formularfelder hervorheben", AppSettings.GetBool("HighlightForms", true), 12, 100, 300);

            categories.SelectedIndex = 0;
        }

        private Panel NewPage(string title)
        {
            Panel p = new Panel();
            p.Dock = DockStyle.Fill;
            Label h = AddLabel(p, title, 10, 4, 380);
            h.Font = Theme.UIFontBold;
            pages[title] = p;
            return p;
        }

        private void ShowPage(string title)
        {
            host.Controls.Clear();
            Panel p;
            if (title != null && pages.TryGetValue(title, out p)) host.Controls.Add(p);
        }

        public void Apply()
        {
            AppSettings.SetBool("ShowWelcome", showWelcome.Checked);
            AppSettings.SetBool("KeepTool", keepTool.Checked);
            AppSettings.SetBool("DefaultToolSelect", defaultSelect.Checked);
            AppSettings.SetInt("RecentCount", (int)recentCount.Value);
            AppSettings.Author = name.Text;
            AppSettings.Set("Organization", org.Text);
            AppSettings.Set("Email", email.Text);
            AppSettings.SetInt("FreeText.FontSize", (int)fontSize.Value);
            AppSettings.SetInt("Width.Shapes", (int)shapeWidth.Value);
            AppSettings.SetInt("Width.Ink", (int)inkWidth.Value);
            AppSettings.SetInt("DefaultLayout", layout.SelectedIndex);
            AppSettings.SetInt("DefaultZoom", zoom.SelectedIndex);
            AppSettings.SetBool("HighlightForms", highlightForms.Checked);
            AppSettings.Save();
        }
    }

    /// <summary>Zeichenfläche für Unterschriften.</summary>
    public class SignaturePad : Control
    {
        public readonly List<List<PointF>> Strokes = new List<List<PointF>>();
        private List<PointF> current;
        public Color InkColor = Color.FromArgb(20, 40, 120);

        public SignaturePad()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Cursor = Cursors.Cross;
        }

        public void Clear() { Strokes.Clear(); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.White);
            using (Pen line = new Pen(Color.FromArgb(180, 180, 190)))
            {
                line.DashStyle = DashStyle.Dash;
                g.DrawLine(line, 20, Height - 36, Width - 20, Height - 36);
            }
            TextRenderer.DrawText(g, "Hier unterschreiben", Theme.SmallFont, new Point(22, Height - 30), Color.Gray);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(InkColor, 2.4f))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                foreach (List<PointF> s in Strokes)
                    if (s.Count > 1) g.DrawLines(p, s.ToArray());
                    else if (s.Count == 1) g.FillEllipse(new SolidBrush(InkColor), s[0].X - 1.2f, s[0].Y - 1.2f, 2.4f, 2.4f);
            }
            g.DrawRectangle(Pens.DimGray, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            current = new List<PointF>();
            current.Add(e.Location);
            Strokes.Add(current);
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (current == null) return;
            PointF last = current[current.Count - 1];
            if (Math.Abs(last.X - e.X) + Math.Abs(last.Y - e.Y) >= 2) { current.Add(e.Location); Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            current = null;
            Capture = false;
            Invalidate();
        }
    }

    public class SignatureDialog : DialogBase
    {
        private readonly TabControl tabs;
        private readonly SignaturePad pad;
        private readonly TextBox name;
        private readonly PictureBox preview;
        private readonly ColorButton color;

        public SignatureDialog() : base("Unterschrift erstellen", 470, 330)
        {
            tabs = new TabControl();
            tabs.SetBounds(10, 10, 450, 240);
            TabPage draw = new TabPage("Zeichnen");
            TabPage type = new TabPage("Eingeben");
            tabs.TabPages.Add(draw);
            tabs.TabPages.Add(type);
            Controls.Add(tabs);

            pad = new SignaturePad();
            pad.SetBounds(8, 8, 426, 170);
            draw.Controls.Add(pad);
            AddButton(draw, "Löschen", 8, 184, 80, delegate { pad.Clear(); });

            AddLabel(type, "Name:", 10, 14, 50);
            name = AddText(type, AppSettings.Author, 64, 12, 360);
            preview = new PictureBox();
            preview.SetBounds(10, 46, 422, 150);
            preview.BorderStyle = BorderStyle.FixedSingle;
            preview.BackColor = Color.White;
            type.Controls.Add(preview);
            name.TextChanged += delegate { UpdatePreview(); };

            AddLabel(this, "Farbe:", 14, 264, 50);
            color = AddColor(this, AnnotationDefaults.SignatureColor, 64, 260);
            color.ColorChanged += delegate { pad.InkColor = color.Color; pad.Invalidate(); UpdatePreview(); };
            pad.InkColor = color.Color;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            Bitmap b = new Bitmap(preview.Width - 2, preview.Height - 2);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.White);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                using (Font f = new Font("Times New Roman", 40, FontStyle.Italic, GraphicsUnit.Pixel))
                using (SolidBrush br = new SolidBrush(color.Color.IsEmpty ? Color.Navy : color.Color))
                {
                    StringFormat sf = new StringFormat();
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(name.Text, f, br, new RectangleF(0, 0, b.Width, b.Height), sf);
                }
            }
            Image old = preview.Image;
            preview.Image = b;
            if (old != null) old.Dispose();
        }

        protected override bool ValidateInput()
        {
            if (tabs.SelectedIndex == 0 && pad.Strokes.Count == 0) { Error("Bitte zeichnen Sie Ihre Unterschrift in das Feld."); return false; }
            if (tabs.SelectedIndex == 1 && name.Text.Trim().Length == 0) { Error("Bitte geben Sie Ihren Namen ein."); return false; }
            return true;
        }

        /// <summary>Vorlage: Koordinaten im Bereich (0,0)-(Breite,Höhe), y nach oben.</summary>
        public Annotation CreateTemplate()
        {
            Annotation a = new Annotation();
            a.Type = AnnotationType.Signature;
            a.Color = color.Color.IsEmpty ? AnnotationDefaults.SignatureColor : color.Color;
            a.Author = AppSettings.Author;
            if (tabs.SelectedIndex == 0)
            {
                const double scale = 0.42;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (List<PointF> s in pad.Strokes)
                    foreach (PointF p in s)
                    {
                        minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                        minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                    }
                double h = (maxY - minY) * scale + 6;
                foreach (List<PointF> s in pad.Strokes)
                {
                    List<PdfPoint> pts = new List<PdfPoint>();
                    foreach (PointF p in s) pts.Add(new PdfPoint((p.X - minX) * scale + 3, h - ((p.Y - minY) * scale + 3)));
                    a.Strokes.Add(pts);
                }
                a.LineWidth = 1.2f;
                a.Rect = new PdfRect(0, 0, (maxX - minX) * scale + 6, h);
            }
            else
            {
                a.Text = name.Text.Trim();
                double w = PdfObjects.MeasureWidth(a.Text, PdfObjects.TimesItalic, 26) + 12;
                a.Rect = new PdfRect(0, 0, w, 40);
            }
            return a;
        }
    }

    public static class AnnotationDefaults
    {
        public static Color SignatureColor { get { return DocumentView.DefaultColor(AnnotationType.Signature); } }
    }

    public class CombineDialog : DialogBase
    {
        private readonly ListView list;

        public CombineDialog(IEnumerable<string> initial) : base("Dateien zusammenführen", 640, 380)
        {
            Label intro = AddLabel(this, "Fügen Sie Dateien hinzu und ordnen Sie sie in der gewünschten Reihenfolge an.", 12, 8, 600);
            intro.Font = Theme.UIFontBold;
            list = new ListView();
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.SetBounds(12, 32, 500, 290);
            list.Columns.Add("Name", 180);
            list.Columns.Add("Seiten", 55, HorizontalAlignment.Right);
            list.Columns.Add("Größe", 70, HorizontalAlignment.Right);
            list.Columns.Add("Ordner", 180);
            list.AllowDrop = true;
            list.DragEnter += delegate(object s, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; };
            list.DragDrop += delegate(object s, DragEventArgs e) { AddFiles((string[])e.Data.GetData(DataFormats.FileDrop)); };
            Controls.Add(list);
            AddButton(this, "Dateien hinzufügen...", 520, 32, 110, delegate { Browse(); });
            AddButton(this, "Entfernen", 520, 64, 110, delegate
            {
                foreach (ListViewItem i in list.SelectedItems) list.Items.Remove(i);
            });
            AddButton(this, "Nach oben", 520, 104, 110, delegate { MoveSelected(-1); });
            AddButton(this, "Nach unten", 520, 136, 110, delegate { MoveSelected(1); });
            OkButton.Text = "Zusammenführen";
            OkButton.SetBounds(ClientSize.Width - 216, ClientSize.Height - 34, 120, 24);
            if (initial != null) AddFiles(initial);
        }

        private void Browse()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Multiselect = true;
                d.Title = "Dateien hinzufügen";
                d.Filter = "Alle unterstützten Dateien|*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.txt|PDF-Dateien (*.pdf)|*.pdf|Bilder|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|Textdateien (*.txt)|*.txt|Alle Dateien (*.*)|*.*";
                if (d.ShowDialog(this) == DialogResult.OK) AddFiles(d.FileNames);
            }
        }

        public void AddFiles(IEnumerable<string> files)
        {
            foreach (string f in files)
            {
                if (!File.Exists(f)) continue;
                string pages = "1";
                if (PdfCreator.IsPdf(f))
                {
                    try { using (PdfDocument d = PdfDocument.Load(File.ReadAllBytes(f), null)) pages = d.PageCount.ToString(); }
                    catch (Exception) { pages = "?"; }
                }
                else if (PdfCreator.IsText(f)) pages = "–";
                ListViewItem item = new ListViewItem(Path.GetFileName(f));
                item.SubItems.Add(pages);
                item.SubItems.Add(NavigationPane.FormatSize(new FileInfo(f).Length));
                item.SubItems.Add(Path.GetDirectoryName(f));
                item.Tag = f;
                list.Items.Add(item);
            }
        }

        private void MoveSelected(int dir)
        {
            if (list.SelectedItems.Count != 1) return;
            ListViewItem item = list.SelectedItems[0];
            int idx = item.Index + dir;
            if (idx < 0 || idx >= list.Items.Count) return;
            list.Items.Remove(item);
            list.Items.Insert(idx, item);
            item.Selected = true;
            list.Focus();
        }

        public List<string> Files
        {
            get
            {
                List<string> files = new List<string>();
                foreach (ListViewItem i in list.Items) files.Add((string)i.Tag);
                return files;
            }
        }

        protected override bool ValidateInput()
        {
            if (list.Items.Count == 0) { Error("Bitte fügen Sie mindestens eine Datei hinzu."); return false; }
            return true;
        }
    }

    public class AboutDialog : Form
    {
        public AboutDialog()
        {
            Text = "Über AcroClone 9 Pro";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 320);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            Font = Theme.UIFont;
            KeyPreview = true;
            KeyDown += delegate { Close(); };
            Click += delegate { Close(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = ClientRectangle;
            using (LinearGradientBrush b = new LinearGradientBrush(r, Color.FromArgb(40, 40, 44), Color.FromArgb(10, 10, 12), LinearGradientMode.Vertical))
                g.FillRectangle(b, r);
            using (LinearGradientBrush glow = new LinearGradientBrush(r, Color.FromArgb(150, 200, 20, 20), Color.FromArgb(0, 200, 20, 20), 30f))
                g.FillRectangle(glow, r);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Dekorative Bögen
            using (Pen p = new Pen(Color.FromArgb(50, 255, 255, 255), 1.5f))
                for (int i = 0; i < 6; i++) g.DrawArc(p, 260 + i * 18, -120 + i * 10, 420, 420, 100, 80);
            g.DrawImage(Icons.Get("logo", 96), 36, 40, 96, 96);
            using (Font big = new Font("Arial", 30f, FontStyle.Bold))
            using (Font mid = new Font("Arial", 12f, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, "AcroClone 9 Pro", big, new Point(150, 52), Color.White);
                TextRenderer.DrawText(g, "Version 9.0.0 – PDF-Programm im Stil der Acrobat-9-Ära", mid, new Point(154, 102), Color.FromArgb(220, 220, 220));
            }
            string[] lines =
            {
                "PDF-Darstellung und -Bearbeitung: PDFium (BSD-3-Clause / Apache-2.0)",
                "Entwickelt mit C# und Windows Forms (.NET Framework 4.0, Visual Studio 2010)",
                "",
                "Dies ist ein eigenständiges Hobbyprojekt und kein Produkt der Adobe Inc.",
                "Adobe und Acrobat sind Marken der Adobe Inc. in den USA und/oder anderen Ländern.",
            };
            int y = 170;
            foreach (string l in lines)
            {
                TextRenderer.DrawText(g, l, Theme.UIFont, new Point(40, y), Color.FromArgb(200, 200, 200));
                y += 18;
            }
            g.FillRectangle(new SolidBrush(Theme.AcrobatRed), 0, r.Height - 6, r.Width, 6);
            TextRenderer.DrawText(g, "Zum Schließen klicken", Theme.SmallFont, new Rectangle(0, r.Height - 26, r.Width - 12, 16), Color.Gray, TextFormatFlags.Right);
            using (Pen p = new Pen(Color.Black)) g.DrawRectangle(p, 0, 0, r.Width - 1, r.Height - 1);
        }
    }
}
