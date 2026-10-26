using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using AcroClone.Model;
using AcroClone.Pdf;

namespace AcroClone.UI.Dialogs
{
    public class WatermarkDialog : DialogBase
    {
        private readonly TextBox text;
        private readonly ComboBox font;
        private readonly NumericUpDown size, opacity, angle;
        private readonly ColorButton color;
        private readonly PictureBox preview;
        private readonly Bitmap page;
        private readonly SizeF pageSizePt;
        public readonly PageRangeGroup Range;

        private static readonly string[] Fonts = { PdfObjects.HelveticaBold, PdfObjects.Helvetica, PdfObjects.TimesBold, PdfObjects.TimesRoman, "Courier-Bold" };

        public WatermarkDialog(Bitmap pagePreview, SizeF pageSize, int count, int current, IList<int> selection)
            : base("Wasserzeichen hinzufügen", 600, 400)
        {
            page = pagePreview;
            pageSizePt = pageSize;
            GroupBox src = AddGroup("Quelle", 12, 10, 330, 76);
            AddLabel(src, "Text:", 12, 22, 60);
            text = AddText(src, AppSettings.Get("Watermark.Text", "ENTWURF"), 80, 20, 236);
            AddLabel(src, "Schrift:", 12, 48, 60);
            font = AddCombo(src, new string[] { "Helvetica fett", "Helvetica", "Times fett", "Times", "Courier fett" }, 0, 80, 46, 140);
            size = AddNumber(src, AppSettings.GetInt("Watermark.Size", 72), 6, 400, 0, 228, 46, 52);
            AddLabel(src, "pt", 284, 48, 20);
            color = AddColor(src, AppSettings.GetColor("Watermark.Color", Color.FromArgb(200, 0, 0)), 0, 0);
            color.SetBounds(-100, -100, 1, 1);

            GroupBox look = AddGroup("Darstellung", 12, 92, 330, 82);
            AddLabel(look, "Farbe:", 12, 22, 60);
            ColorButton cb = AddColor(look, color.Color, 80, 20);
            cb.ColorChanged += delegate { color.Color = cb.Color; UpdatePreview(); };
            AddLabel(look, "Deckkraft:", 150, 22, 70);
            opacity = AddNumber(look, AppSettings.GetInt("Watermark.Opacity", 30), 1, 100, 0, 222, 20, 56);
            AddLabel(look, "%", 282, 22, 20);
            AddLabel(look, "Drehung:", 12, 52, 60);
            angle = AddNumber(look, AppSettings.GetInt("Watermark.Angle", 45), -180, 180, 0, 80, 50, 60);
            AddLabel(look, "Grad", 146, 52, 40);

            Range = new PageRangeGroup(count, current, selection, false);
            Range.SetBounds(12, 182, 330, 104);
            Range.SelectAll();
            Controls.Add(Range);

            Label pl = AddLabel(this, "Vorschau", 356, 8, 100);
            pl.Font = Theme.UIFontBold;
            preview = new PictureBox();
            preview.SetBounds(356, 28, 230, 320);
            preview.BorderStyle = BorderStyle.FixedSingle;
            preview.BackColor = Theme.DocumentBackground;
            Controls.Add(preview);

            EventHandler upd = delegate { UpdatePreview(); };
            text.TextChanged += upd; font.SelectedIndexChanged += upd; size.ValueChanged += upd;
            opacity.ValueChanged += upd; angle.ValueChanged += upd;
            UpdatePreview();
        }

        public WatermarkOptions Options
        {
            get
            {
                WatermarkOptions o = new WatermarkOptions();
                o.Text = text.Text;
                o.Font = Fonts[Math.Max(0, font.SelectedIndex)];
                o.FontSize = (float)size.Value;
                o.Color = color.Color.IsEmpty ? Color.Red : color.Color;
                o.Opacity = (float)opacity.Value / 100f;
                o.Angle = (float)angle.Value;
                return o;
            }
        }

        public void Remember()
        {
            AppSettings.Set("Watermark.Text", text.Text);
            AppSettings.SetInt("Watermark.Size", (int)size.Value);
            AppSettings.SetColor("Watermark.Color", color.Color);
            AppSettings.SetInt("Watermark.Opacity", (int)opacity.Value);
            AppSettings.SetInt("Watermark.Angle", (int)angle.Value);
        }

        private void UpdatePreview()
        {
            Bitmap bmp = new Bitmap(preview.Width - 2, preview.Height - 2);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Theme.DocumentBackground);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                float f = Math.Min((bmp.Width - 16) / pageSizePt.Width, (bmp.Height - 16) / pageSizePt.Height);
                float w = pageSizePt.Width * f, h = pageSizePt.Height * f;
                RectangleF r = new RectangleF((bmp.Width - w) / 2, (bmp.Height - h) / 2, w, h);
                if (page != null) g.DrawImage(page, r); else g.FillRectangle(Brushes.White, r);
                g.DrawRectangle(Pens.Black, r.X, r.Y, r.Width, r.Height);
                WatermarkOptions o = Options;
                if (!string.IsNullOrEmpty(o.Text))
                {
                    using (Font fo = new Font(PdfObjects.GdiFamilyFor(o.Font), Math.Max(1f, o.FontSize * f), PdfObjects.GdiStyleFor(o.Font), GraphicsUnit.Pixel))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(255 * o.Opacity), o.Color)))
                    {
                        g.TranslateTransform(r.X + w / 2, r.Y + h / 2);
                        g.RotateTransform(-o.Angle);
                        SizeF ts = g.MeasureString(o.Text, fo, PointF.Empty, StringFormat.GenericTypographic);
                        g.DrawString(o.Text, fo, b, -ts.Width / 2, -ts.Height / 2, StringFormat.GenericTypographic);
                    }
                }
            }
            Image old = preview.Image;
            preview.Image = bmp;
            if (old != null) old.Dispose();
        }

        protected override bool ValidateInput()
        {
            if (text.Text.Trim().Length == 0) { Error("Bitte geben Sie einen Text für das Wasserzeichen ein."); return false; }
            return true;
        }
    }

    public class HeaderFooterDialog : DialogBase
    {
        private readonly TextBox hl, hc, hr, fl, fc, fr;
        private readonly NumericUpDown size, top, bottom, left, right, start;
        private TextBox lastFocused;
        public readonly PageRangeGroup Range;

        public HeaderFooterDialog(int count, int current, IList<int> selection) : base("Kopf- und Fußzeile hinzufügen", 600, 420)
        {
            GroupBox font = AddGroup("Schrift und Ränder", 12, 10, 576, 80);
            AddLabel(font, "Größe:", 12, 22, 50);
            size = AddNumber(font, 10, 4, 72, 1, 64, 20, 56);
            AddLabel(font, "pt", 124, 22, 20);
            AddLabel(font, "Startnummer:", 12, 50, 80);
            start = AddNumber(font, 1, 1, 100000, 0, 96, 48, 60);
            AddLabel(font, "Ränder (mm) – oben:", 180, 22, 120);
            top = AddNumber(font, 12.7m, 0, 200, 1, 300, 20, 56);
            AddLabel(font, "unten:", 364, 22, 44);
            bottom = AddNumber(font, 12.7m, 0, 200, 1, 410, 20, 56);
            AddLabel(font, "links:", 180, 50, 44);
            left = AddNumber(font, 25.4m, 0, 200, 1, 300, 48, 56);
            AddLabel(font, "rechts:", 364, 50, 44);
            right = AddNumber(font, 25.4m, 0, 200, 1, 410, 48, 56);

            GroupBox texts = AddGroup("Text", 12, 96, 576, 150);
            AddLabel(texts, "Links", 90, 16, 120);
            AddLabel(texts, "Mitte", 250, 16, 120);
            AddLabel(texts, "Rechts", 410, 16, 120);
            AddLabel(texts, "Kopfzeile:", 12, 38, 74);
            hl = AddText(texts, "", 90, 36, 154); hc = AddText(texts, "", 250, 36, 154); hr = AddText(texts, "", 410, 36, 154);
            AddLabel(texts, "Fußzeile:", 12, 68, 74);
            fl = AddText(texts, "", 90, 66, 154); fc = AddText(texts, "Seite <<Seite>> von <<Seiten>>", 250, 66, 154); fr = AddText(texts, "", 410, 66, 154);
            foreach (TextBox t in new TextBox[] { hl, hc, hr, fl, fc, fr }) { TextBox tt = t; tt.Enter += delegate { lastFocused = tt; }; }
            lastFocused = fc;
            AddLabel(texts, "Einfügen:", 12, 108, 70);
            AddButton(texts, "Seitenzahl", 90, 104, 90, delegate { Insert("<<Seite>>"); });
            AddButton(texts, "Seitenanzahl", 186, 104, 90, delegate { Insert("<<Seiten>>"); });
            AddButton(texts, "Datum", 282, 104, 80, delegate { Insert("<<Datum>>"); });
            AddButton(texts, "Dateiname", 368, 104, 90, delegate { Insert("<<Dateiname>>"); });

            Range = new PageRangeGroup(count, current, selection, false);
            Range.SetBounds(12, 254, 340, 104);
            Range.SelectAll();
            Controls.Add(Range);
        }

        private void Insert(string token)
        {
            if (lastFocused == null) return;
            int pos = lastFocused.SelectionStart;
            lastFocused.Text = lastFocused.Text.Insert(pos, token);
            lastFocused.SelectionStart = pos + token.Length;
            lastFocused.Focus();
        }

        private static float Pt(NumericUpDown n) { return (float)((double)n.Value / 25.4 * 72.0); }

        public HeaderFooterOptions Options
        {
            get
            {
                HeaderFooterOptions o = new HeaderFooterOptions();
                o.HeaderLeft = hl.Text; o.HeaderCenter = hc.Text; o.HeaderRight = hr.Text;
                o.FooterLeft = fl.Text; o.FooterCenter = fc.Text; o.FooterRight = fr.Text;
                o.FontSize = (float)size.Value;
                o.MarginTop = Pt(top); o.MarginBottom = Pt(bottom); o.MarginLeft = Pt(left); o.MarginRight = Pt(right);
                o.StartNumber = (int)start.Value;
                return o;
            }
        }
    }

    public class AnnotationPropertiesDialog : DialogBase
    {
        private readonly Annotation a;
        private readonly ColorButton color, fill;
        private readonly NumericUpDown opacity, width, fontSize;
        private readonly TextBox author, contents, text;
        private readonly CheckBox makeDefault;

        public AnnotationPropertiesDialog(Annotation annotation) : base(annotation.TypeName + " – Eigenschaften", 420, 360)
        {
            a = annotation;
            TabControl tabs = new TabControl();
            tabs.SetBounds(10, 10, 400, 270);
            TabPage look = new TabPage("Darstellung");
            TabPage general = new TabPage("Allgemein");
            tabs.TabPages.Add(look);
            tabs.TabPages.Add(general);
            Controls.Add(tabs);

            int y = 16;
            AddLabel(look, "Farbe:", 12, y, 100);
            color = AddColor(look, a.Color, 140, y);
            y += 32;
            bool hasFill = a.Type == AnnotationType.Rectangle || a.Type == AnnotationType.Ellipse || a.Type == AnnotationType.FreeText;
            if (hasFill)
            {
                AddLabel(look, "Füllfarbe:", 12, y, 100);
                fill = AddColor(look, a.FillColor, 140, y);
                fill.AllowNone = true;
                y += 32;
            }
            AddLabel(look, "Deckkraft (%):", 12, y, 120);
            opacity = AddNumber(look, (decimal)Math.Round(a.Opacity * 100), 5, 100, 0, 140, y, 60);
            y += 32;
            bool hasWidth = !a.IsTextMarkup && a.Type != AnnotationType.Note && a.Type != AnnotationType.Stamp;
            if (hasWidth)
            {
                AddLabel(look, "Linienstärke (pt):", 12, y, 120);
                width = AddNumber(look, (decimal)a.LineWidth, 0, 24, 1, 140, y, 60);
                y += 32;
            }
            if (a.Type == AnnotationType.FreeText)
            {
                AddLabel(look, "Schriftgröße (pt):", 12, y, 120);
                fontSize = AddNumber(look, (decimal)a.FontSize, 4, 96, 1, 140, y, 60);
                y += 32;
            }
            if (a.Type == AnnotationType.FreeText || a.Type == AnnotationType.Stamp || (a.Type == AnnotationType.Signature && a.Strokes.Count == 0))
            {
                AddLabel(look, a.Type == AnnotationType.FreeText ? "Text:" : "Beschriftung:", 12, y, 120);
                text = AddText(look, a.Text, 140, y, 230);
                if (a.Type == AnnotationType.FreeText) { text.Multiline = true; text.Height = 60; text.Text = a.Text.Replace("\n", "\r\n"); }
                y += 32;
            }
            makeDefault = AddCheck(look, "Eigenschaften als Standard verwenden", false, 12, 214, 300);

            AddLabel(general, "Autor:", 12, 16, 80);
            author = AddText(general, a.Author, 100, 14, 270);
            AddLabel(general, "Betreff:", 12, 46, 80);
            AddLabel(general, a.TypeName, 100, 46, 270);
            AddLabel(general, "Geändert:", 12, 72, 80);
            AddLabel(general, a.Modified.ToString("dd.MM.yyyy HH:mm:ss"), 100, 72, 270);
            AddLabel(general, "Kommentar:", 12, 100, 80);
            contents = AddText(general, (a.Contents ?? "").Replace("\n", "\r\n"), 100, 98, 270);
            contents.Multiline = true;
            contents.ScrollBars = ScrollBars.Vertical;
            contents.Height = 120;
        }

        /// <summary>Überträgt die Werte in die Anmerkung (BeginChange vorher aufrufen).</summary>
        public void Apply()
        {
            a.Color = color.Color.IsEmpty ? a.Color : color.Color;
            if (fill != null) a.FillColor = fill.Color;
            a.Opacity = (float)opacity.Value / 100f;
            if (width != null) a.LineWidth = (float)width.Value;
            if (fontSize != null) a.FontSize = (float)fontSize.Value;
            if (text != null) a.Text = text.Text.Replace("\r\n", "\n");
            a.Author = author.Text;
            a.Contents = contents.Text.Replace("\r\n", "\n");
            a.Modified = DateTime.Now;
            if (makeDefault.Checked)
            {
                AppSettings.SetColor("Color." + a.Type, a.Color);
                if (width != null)
                {
                    if (a.Type == AnnotationType.Ink) AppSettings.SetInt("Width.Ink", (int)Math.Max(1, Math.Round(a.LineWidth)));
                    else AppSettings.SetInt("Width.Shapes", (int)Math.Max(1, Math.Round(a.LineWidth)));
                }
                if (fontSize != null) AppSettings.SetInt("FreeText.FontSize", (int)Math.Round(a.FontSize));
                AppSettings.Save();
            }
        }
    }
}
