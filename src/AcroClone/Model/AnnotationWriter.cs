using System;
using System.Collections.Generic;
using System.Drawing;
using AcroClone.Pdf;

namespace AcroClone.Model
{
    /// <summary>
    /// Schreibt Kommentare als echte PDF-Anmerkungen und liest eigene Anmerkungen wieder ein.
    /// Jede Anmerkung enthält zusätzlich unter /AcroCloneData die vollständigen Modelldaten,
    /// damit sie nach erneutem Öffnen wieder bearbeitbar ist.
    /// </summary>
    public static class AnnotationWriter
    {
        public const string DataKey = "AcroCloneData";

        public static void WriteAll(PdfDocument doc, IEnumerable<Annotation> annotations)
        {
            HashSet<int> touched = new HashSet<int>();
            foreach (Annotation a in annotations)
            {
                if (a.Page < 0 || a.Page >= doc.PageCount) continue;
                Write(doc, a);
                touched.Add(a.Page);
            }
            // Seiten neu laden und einmal rendern, damit PDFium fehlende Erscheinungsbilder (/AP)
            // für Notizen, Markierungen, Formen und Freihandlinien erzeugt.
            doc.ClosePages();
            foreach (int page in touched)
            {
                using (Bitmap b = doc.Render(page, 0, new Size(8, 8), new Rectangle(0, 0, 1, 1), false)) { }
            }
            doc.ClosePages();
        }

        private static Native.FS_RECTF ToRectF(PdfRect r)
        {
            Native.FS_RECTF f = new Native.FS_RECTF();
            f.left = (float)r.Left;
            f.right = (float)r.Right;
            f.top = (float)r.Top;
            f.bottom = (float)r.Bottom;
            return f;
        }

        private static void SetColor(IntPtr annot, int type, Color c, float opacity)
        {
            if (c.IsEmpty) return;
            uint alpha = (uint)Math.Max(0, Math.Min(255, (int)(255 * opacity)));
            Native.FPDFAnnot_SetColor(annot, type, c.R, c.G, c.B, alpha);
        }

        public static void Write(PdfDocument doc, Annotation a)
        {
            IntPtr page = doc.GetPage(a.Page);
            IntPtr annot = IntPtr.Zero;
            Native.FS_RECTF rect;
            switch (a.Type)
            {
                case AnnotationType.Note:
                    annot = Native.FPDFPage_CreateAnnot(page, Native.FPDF_ANNOT_TEXT);
                    rect = ToRectF(a.Rect);
                    Native.FPDFAnnot_SetRect(annot, ref rect);
                    SetColor(annot, Native.FPDFANNOT_COLORTYPE_Color, a.Color, a.Opacity);
                    break;

                case AnnotationType.Highlight:
                case AnnotationType.Underline:
                case AnnotationType.StrikeOut:
                    {
                        int subtype = a.Type == AnnotationType.Highlight ? Native.FPDF_ANNOT_HIGHLIGHT :
                            a.Type == AnnotationType.Underline ? Native.FPDF_ANNOT_UNDERLINE : Native.FPDF_ANNOT_STRIKEOUT;
                        annot = Native.FPDFPage_CreateAnnot(page, subtype);
                        SetColor(annot, Native.FPDFANNOT_COLORTYPE_Color, a.Color, a.Opacity);
                        foreach (PdfRect q in a.Quads)
                        {
                            Native.FS_QUADPOINTSF qp = new Native.FS_QUADPOINTSF();
                            qp.x1 = (float)q.Left; qp.y1 = (float)q.Top;
                            qp.x2 = (float)q.Right; qp.y2 = (float)q.Top;
                            qp.x3 = (float)q.Left; qp.y3 = (float)q.Bottom;
                            qp.x4 = (float)q.Right; qp.y4 = (float)q.Bottom;
                            Native.FPDFAnnot_AppendAttachmentPoints(annot, ref qp);
                        }
                        rect = ToRectF(a.Bounds);
                        Native.FPDFAnnot_SetRect(annot, ref rect);
                        break;
                    }

                case AnnotationType.Rectangle:
                case AnnotationType.Ellipse:
                    annot = Native.FPDFPage_CreateAnnot(page, a.Type == AnnotationType.Rectangle ? Native.FPDF_ANNOT_SQUARE : Native.FPDF_ANNOT_CIRCLE);
                    rect = ToRectF(a.Rect.Inflate(a.LineWidth / 2));
                    Native.FPDFAnnot_SetRect(annot, ref rect);
                    SetColor(annot, Native.FPDFANNOT_COLORTYPE_Color, a.Color, a.Opacity);
                    if (a.FillColor.A > 0) SetColor(annot, Native.FPDFANNOT_COLORTYPE_InteriorColor, a.FillColor, a.Opacity);
                    Native.FPDFAnnot_SetBorder(annot, 0, 0, a.LineWidth);
                    break;

                case AnnotationType.Line:
                case AnnotationType.Arrow:
                case AnnotationType.Ink:
                    annot = Native.FPDFPage_CreateAnnot(page, Native.FPDF_ANNOT_INK);
                    rect = ToRectF(a.Bounds);
                    Native.FPDFAnnot_SetRect(annot, ref rect);
                    SetColor(annot, Native.FPDFANNOT_COLORTYPE_Color, a.Color, a.Opacity);
                    Native.FPDFAnnot_SetBorder(annot, 0, 0, a.LineWidth);
                    foreach (List<PdfPoint> s in a.Strokes) AddStroke(annot, s);
                    if (a.Type == AnnotationType.Arrow)
                        foreach (List<PdfPoint> s in AnnotationPainter.ArrowHead(a)) AddStroke(annot, s);
                    break;

                case AnnotationType.Signature:
                    if (a.Strokes.Count > 0)
                    {
                        annot = Native.FPDFPage_CreateAnnot(page, Native.FPDF_ANNOT_INK);
                        rect = ToRectF(a.Bounds);
                        Native.FPDFAnnot_SetRect(annot, ref rect);
                        SetColor(annot, Native.FPDFANNOT_COLORTYPE_Color, a.Color, a.Opacity);
                        Native.FPDFAnnot_SetBorder(annot, 0, 0, a.LineWidth);
                        foreach (List<PdfPoint> s in a.Strokes) AddStroke(annot, s);
                    }
                    else
                    {
                        annot = CreateStampWithObjects(doc, page, a);
                    }
                    break;

                case AnnotationType.Redaction:
                    // Noch nicht angewendete Schwärzung: als rote Rechteck-Markierung speichern
                    annot = Native.FPDFPage_CreateAnnot(page, Native.FPDF_ANNOT_SQUARE);
                    rect = ToRectF(a.Rect);
                    Native.FPDFAnnot_SetRect(annot, ref rect);
                    SetColor(annot, Native.FPDFANNOT_COLORTYPE_Color, Color.Red, 1f);
                    Native.FPDFAnnot_SetBorder(annot, 0, 0, 1);
                    break;

                default: // FreeText, Stamp
                    annot = CreateStampWithObjects(doc, page, a);
                    break;
            }

            if (annot == IntPtr.Zero) return;
            Native.FPDFAnnot_SetFlags(annot, Native.FPDF_ANNOT_FLAG_PRINT);
            Native.FPDFAnnot_SetStringValue(annot, "Contents", a.Type == AnnotationType.FreeText && string.IsNullOrEmpty(a.Contents) ? a.Text : a.Contents ?? string.Empty);
            Native.FPDFAnnot_SetStringValue(annot, "T", a.Author ?? string.Empty);
            Native.FPDFAnnot_SetStringValue(annot, "Subj", a.TypeName);
            Native.FPDFAnnot_SetStringValue(annot, "NM", a.Id);
            Native.FPDFAnnot_SetStringValue(annot, "M", InfoDictionaryWriter.FormatDate(a.Modified));
            Native.FPDFAnnot_SetStringValue(annot, DataKey, a.Serialize());
            Native.FPDFPage_CloseAnnot(annot);
        }

        private static void AddStroke(IntPtr annot, List<PdfPoint> s)
        {
            if (s.Count == 0) return;
            List<Native.FS_POINTF> pts = new List<Native.FS_POINTF>();
            foreach (PdfPoint p in s) pts.Add(new Native.FS_POINTF((float)p.X, (float)p.Y));
            if (pts.Count == 1) pts.Add(new Native.FS_POINTF(pts[0].x + 0.1f, pts[0].y));
            Native.FPDFAnnot_AddInkStroke(annot, pts.ToArray(), new UIntPtr((uint)pts.Count));
        }

        /// <summary>Stempel-Anmerkung, deren Erscheinungsbild aus Seitenobjekten besteht (Textfeld, Stempel, getippte Unterschrift).</summary>
        private static IntPtr CreateStampWithObjects(PdfDocument doc, IntPtr page, Annotation a)
        {
            IntPtr annot = Native.FPDFPage_CreateAnnot(page, Native.FPDF_ANNOT_STAMP);
            if (annot == IntPtr.Zero) return annot;
            Native.FS_RECTF rect = ToRectF(a.Rect);
            Native.FPDFAnnot_SetRect(annot, ref rect);

            Affine local = a.LocalToPage;
            SizeF size = a.LocalSize;
            Color color = AnnotationPainter.WithOpacity(a.Color, a.Opacity);

            if (a.Type == AnnotationType.FreeText)
            {
                PdfRect box = new PdfRect(a.Rect.Left, a.Rect.Bottom, a.Rect.Right, a.Rect.Top);
                if (a.FillColor.A > 0 || a.LineWidth > 0)
                {
                    PdfRect inner = box.Inflate(-a.LineWidth / 2);
                    Append(annot, PdfObjects.Rect(inner, a.LineWidth > 0 ? color : Color.Empty, a.LineWidth,
                        AnnotationPainter.WithOpacity(a.FillColor, a.Opacity)));
                }
                float y = AnnotationPainter.FreeTextPadding;
                foreach (string line in AnnotationPainter.FreeTextLines(a, size.Width))
                {
                    if (y + a.FontSize > size.Height + 0.5f) break;
                    if (line.Length > 0)
                    {
                        IntPtr t = PdfObjects.Text(doc, PdfObjects.Helvetica, a.FontSize, line,
                            AnnotationPainter.WithOpacity(Color.Black, a.Opacity));
                        PlaceText(t, local, AnnotationPainter.FreeTextPadding, y + a.FontSize * AnnotationPainter.Ascent);
                        Append(annot, t);
                    }
                    y += a.FontSize * AnnotationPainter.LineFactor;
                }
            }
            else if (a.Type == AnnotationType.Stamp)
            {
                float border = AnnotationPainter.StampBorder(size);
                PdfRect inner = a.Rect.Inflate(-border / 2);
                double radius = Math.Min(inner.Width, inner.Height) * 0.18;
                Append(annot, PdfObjects.RoundRect(inner, radius, color, border, Color.FromArgb(color.A / 8, color)));
                float fs = AnnotationPainter.StampFontSize(a.Text, size);
                IntPtr t = PdfObjects.Text(doc, PdfObjects.HelveticaBold, fs, a.Text, color);
                float tw = PdfObjects.MeasureWidth(a.Text, PdfObjects.HelveticaBold, fs);
                PlaceText(t, local, (size.Width - tw) / 2, size.Height / 2 + fs * 0.36f);
                Append(annot, t);
            }
            else if (a.Type == AnnotationType.Signature)
            {
                float fs = AnnotationPainter.SignatureFontSize(a, size);
                IntPtr t = PdfObjects.Text(doc, PdfObjects.TimesItalic, fs, a.Text, color);
                float tw = PdfObjects.MeasureWidth(a.Text, PdfObjects.TimesItalic, fs);
                PlaceText(t, local, (size.Width - tw) / 2, size.Height / 2 + fs * 0.3f);
                Append(annot, t);
            }
            SetColor(annot, Native.FPDFANNOT_COLORTYPE_Color, a.Color, 1f);
            return annot;
        }

        /// <summary>Positioniert ein Textobjekt (Ursprung = Grundlinie) im lokalen System der Anmerkung.</summary>
        private static void PlaceText(IntPtr textObj, Affine localToPage, double x, double baselineY)
        {
            PdfPoint origin = localToPage.ApplyD(x, baselineY);
            PdfPoint right = localToPage.ApplyVector(1, 0);
            PdfPoint up = localToPage.ApplyVector(0, -1);
            PdfObjects.Transform(textObj, new Affine(right.X, right.Y, up.X, up.Y, origin.X, origin.Y));
        }

        private static void Append(IntPtr annot, IntPtr obj)
        {
            if (obj == IntPtr.Zero) return;
            if (Native.FPDFAnnot_AppendObject(annot, obj) == 0) Native.FPDFPageObj_Destroy(obj);
        }

        /// <summary>
        /// Liest alle von AcroClone erzeugten Anmerkungen aus dem Dokument ein und entfernt sie dort,
        /// damit sie im Programm bearbeitet werden können.
        /// </summary>
        public static List<Annotation> Extract(PdfDocument doc, int firstPage, int lastPage)
        {
            List<Annotation> result = new List<Annotation>();
            for (int i = firstPage; i <= lastPage && i < doc.PageCount; i++)
            {
                IntPtr page = doc.GetPage(i);
                int n = Native.FPDFPage_GetAnnotCount(page);
                List<Annotation> pageList = new List<Annotation>();
                for (int j = n - 1; j >= 0; j--)
                {
                    IntPtr annot = Native.FPDFPage_GetAnnot(page, j);
                    if (annot == IntPtr.Zero) continue;
                    string data = null;
                    if (Native.FPDFAnnot_HasKey(annot, DataKey) != 0)
                        data = Native.ReadUtf16(delegate(byte[] b, uint l) { return Native.FPDFAnnot_GetStringValue(annot, DataKey, b, l); });
                    Native.FPDFPage_CloseAnnot(annot);
                    if (string.IsNullOrEmpty(data)) continue;
                    Annotation a = Annotation.Deserialize(data);
                    a.Page = i;
                    pageList.Insert(0, a);
                    Native.FPDFPage_RemoveAnnot(page, j);
                }
                result.AddRange(pageList);
            }
            doc.ClosePages();
            return result;
        }
    }
}
