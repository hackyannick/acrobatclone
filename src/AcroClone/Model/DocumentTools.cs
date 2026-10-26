using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using AcroClone.Pdf;

namespace AcroClone.Model
{
    public class WatermarkOptions
    {
        public string Text = "ENTWURF";
        public string Font = PdfObjects.HelveticaBold;
        public float FontSize = 72;
        public Color Color = Color.FromArgb(255, 200, 0, 0);
        public float Opacity = 0.3f;
        public float Angle = 45;
    }

    public class HeaderFooterOptions
    {
        public string HeaderLeft = "", HeaderCenter = "", HeaderRight = "";
        public string FooterLeft = "", FooterCenter = "Seite <<Seite>> von <<Seiten>>", FooterRight = "";
        public float FontSize = 10;
        public float MarginTop = 36, MarginBottom = 36, MarginLeft = 72, MarginRight = 72;
        public Color Color = Color.Black;
        public int StartNumber = 1;
    }

    /// <summary>Dokumentweite Bearbeitungsfunktionen (Wasserzeichen, Kopf-/Fußzeile, Beschneiden, Schwärzen).</summary>
    public static class DocumentTools
    {
        /// <summary>Basisvektoren der "sichtbaren" Seite im Seitenraum: rechts, oben und Ursprung oben links.</summary>
        private static void VisualFrame(PdfDocument pdf, int page, out Affine viewToPage, out SizeF visualSize)
        {
            Affine m = pdf.GetPageTransform(page, 0);
            viewToPage = m.Invert();
            visualSize = pdf.GetViewSize(page, 0);
        }

        private static void PlaceTextVisual(IntPtr obj, Affine viewToPage, double vx, double vyBaseline, double angleDeg)
        {
            PdfPoint e1 = viewToPage.ApplyVector(1, 0);
            PdfPoint e2 = viewToPage.ApplyVector(0, -1);
            double rad = angleDeg * Math.PI / 180;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            PdfPoint a = new PdfPoint(cos * e1.X + sin * e2.X, cos * e1.Y + sin * e2.Y);
            PdfPoint c = new PdfPoint(-sin * e1.X + cos * e2.X, -sin * e1.Y + cos * e2.Y);
            PdfPoint o = viewToPage.ApplyD(vx, vyBaseline);
            PdfObjects.Transform(obj, new Affine(a.X, a.Y, c.X, c.Y, o.X, o.Y));
        }

        public static void AddWatermark(DocumentSession s, IList<int> pages, WatermarkOptions o)
        {
            if (string.IsNullOrEmpty(o.Text) || pages.Count == 0) return;
            s.BeginChange(true, "Wasserzeichen hinzufügen");
            PdfDocument pdf = s.Pdf;
            Color c = Color.FromArgb((int)(255 * Math.Max(0.02f, Math.Min(1f, o.Opacity))), o.Color);
            foreach (int i in pages)
            {
                Affine v2p; SizeF vs;
                VisualFrame(pdf, i, out v2p, out vs);
                double tw = PdfObjects.TextWidth(pdf, o.Font, o.FontSize, o.Text);
                IntPtr obj = PdfObjects.Text(pdf, o.Font, o.FontSize, o.Text, c);
                // Text um den Seitenmittelpunkt gedreht platzieren
                double rad = o.Angle * Math.PI / 180;
                double ox = -tw / 2, oy = -o.FontSize * 0.35;
                double cx = vs.Width / 2 + (ox * Math.Cos(rad) - oy * Math.Sin(rad));
                double cy = vs.Height / 2 - (ox * Math.Sin(rad) + oy * Math.Cos(rad));
                PlaceTextVisual(obj, v2p, cx, cy, o.Angle);
                Native.FPDFPage_InsertObject(pdf.GetPage(i), obj);
                pdf.CommitPageContent(i);
            }
            s.Modified = true;
            s.RaiseStructureChanged();
        }

        public static string ExpandTokens(string text, int pageNumber, int pageCount, string fileName)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return text.Replace("<<Seite>>", pageNumber.ToString())
                .Replace("<<Seiten>>", pageCount.ToString())
                .Replace("<<Datum>>", DateTime.Now.ToString("dd.MM.yyyy"))
                .Replace("<<Dateiname>>", fileName ?? string.Empty);
        }

        public static void AddHeaderFooter(DocumentSession s, IList<int> pages, HeaderFooterOptions o)
        {
            if (pages.Count == 0) return;
            s.BeginChange(true, "Kopf- und Fußzeile hinzufügen");
            PdfDocument pdf = s.Pdf;
            int total = pdf.PageCount;
            int number = o.StartNumber;
            foreach (int i in pages.OrderBy(p => p))
            {
                Affine v2p; SizeF vs;
                VisualFrame(pdf, i, out v2p, out vs);
                string[] texts = { o.HeaderLeft, o.HeaderCenter, o.HeaderRight, o.FooterLeft, o.FooterCenter, o.FooterRight };
                for (int k = 0; k < 6; k++)
                {
                    string t = ExpandTokens(texts[k], number, total + o.StartNumber - 1, s.DisplayName);
                    if (string.IsNullOrEmpty(t)) continue;
                    double tw = PdfObjects.TextWidth(pdf, PdfObjects.Helvetica, o.FontSize, t);
                    double x;
                    int col = k % 3;
                    if (col == 0) x = o.MarginLeft;
                    else if (col == 1) x = (vs.Width - tw) / 2;
                    else x = vs.Width - o.MarginRight - tw;
                    double y = k < 3 ? o.MarginTop + o.FontSize * 0.75 : vs.Height - o.MarginBottom;
                    IntPtr obj = PdfObjects.Text(pdf, PdfObjects.Helvetica, o.FontSize, t, o.Color);
                    PlaceTextVisual(obj, v2p, x, y, 0);
                    Native.FPDFPage_InsertObject(pdf.GetPage(i), obj);
                }
                pdf.CommitPageContent(i);
                number++;
            }
            s.Modified = true;
            s.RaiseStructureChanged();
        }

        /// <summary>Beschneidet Seiten um die angegebenen sichtbaren Ränder (Punkt).</summary>
        public static void CropPages(DocumentSession s, IList<int> pages, double top, double bottom, double left, double right)
        {
            if (pages.Count == 0) return;
            PdfDocument pdf = s.Pdf;
            Dictionary<int, PdfRect> boxes = new Dictionary<int, PdfRect>();
            foreach (int i in pages)
            {
                Affine v2p; SizeF vs;
                VisualFrame(pdf, i, out v2p, out vs);
                if (left + right >= vs.Width - 10 || top + bottom >= vs.Height - 10) continue;
                PdfPoint a = v2p.ApplyD(left, top);
                PdfPoint b = v2p.ApplyD(vs.Width - right, vs.Height - bottom);
                boxes[i] = new PdfRect(a.X, a.Y, b.X, b.Y);
            }
            if (boxes.Count == 0) return;
            s.BeginChange(true, "Seiten beschneiden");
            foreach (KeyValuePair<int, PdfRect> kv in boxes) pdf.SetCropBox(kv.Key, kv.Value);
            s.Modified = true;
            s.RaiseStructureChanged();
        }

        /// <summary>
        /// Wendet Schwärzungen an: Betroffene Seiten werden gerastert, die Bereiche geschwärzt
        /// und die Seite durch das Bild ersetzt. Der Originalinhalt ist danach nicht mehr enthalten.
        /// </summary>
        public static int ApplyRedactions(DocumentSession s)
        {
            List<Annotation> marks = s.Annotations.Where(a => a.Type == AnnotationType.Redaction).ToList();
            if (marks.Count == 0) return 0;
            s.BeginChange(true, "Schwärzungen anwenden");
            PdfDocument pdf = s.Pdf;
            const double dpi = 200;
            double scale = dpi / 72.0;
            List<int> pages = marks.Select(m => m.Page).Distinct().OrderBy(p => p).ToList();
            foreach (int i in pages)
            {
                Affine m = pdf.GetPageTransform(i, 0);
                SizeF vs = pdf.GetViewSize(i, 0);
                int pageRot = pdf.GetPageRotation(i);
                int w = (int)Math.Round(vs.Width * scale), h = (int)Math.Round(vs.Height * scale);
                byte[] jpeg;
                pdf.KillFormFocus();
                using (Bitmap bmp = pdf.RenderPage(i, 0, w, h, true))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        Affine dev = m.Scale(scale);
                        foreach (Annotation r in marks.Where(x => x.Page == i))
                        {
                            RectangleF rr = dev.MapRect(r.Rect);
                            g.FillRectangle(Brushes.Black, rr);
                        }
                    }
                    jpeg = PdfObjects.ToJpeg(bmp, 90);
                }

                // Neue Seite mit sichtbarer Größe einfügen, alte Seite entfernen
                pdf.InsertBlankPage(i, vs.Width, vs.Height);
                IntPtr page = pdf.GetPage(i);
                IntPtr img = PdfObjects.JpegImage(pdf, page, jpeg);
                if (img != IntPtr.Zero)
                {
                    Native.FPDFImageObj_SetMatrix(img, vs.Width, 0, 0, vs.Height, 0, 0);
                    Native.FPDFPage_InsertObject(page, img);
                    pdf.CommitPageContent(i);
                }
                pdf.DeletePage(i + 1);

                // Übrige Kommentare dieser Seite in das neue (ungedrehte) Koordinatensystem überführen
                foreach (Annotation a in s.Annotations.Where(x => x.Page == i && x.Type != AnnotationType.Redaction))
                    TransformAnnotation(a, m, vs.Height, pageRot);
            }
            s.Annotations.RemoveAll(a => a.Type == AnnotationType.Redaction);
            s.Modified = true;
            s.RaiseStructureChanged();
            s.RaiseAnnotationsChanged();
            return pages.Count;
        }

        private static void TransformAnnotation(Annotation a, Affine pageToView, double viewHeight, int pageRot)
        {
            Func<double, double, PdfPoint> map = delegate(double x, double y)
            {
                PdfPoint v = pageToView.ApplyD(x, y);
                return new PdfPoint(v.X, viewHeight - v.Y);
            };
            PdfPoint p1 = map(a.Rect.Left, a.Rect.Bottom), p2 = map(a.Rect.Right, a.Rect.Top);
            a.Rect = new PdfRect(p1.X, p1.Y, p2.X, p2.Y);
            for (int q = 0; q < a.Quads.Count; q++)
            {
                PdfPoint q1 = map(a.Quads[q].Left, a.Quads[q].Bottom), q2 = map(a.Quads[q].Right, a.Quads[q].Top);
                a.Quads[q] = new PdfRect(q1.X, q1.Y, q2.X, q2.Y);
            }
            foreach (List<PdfPoint> st in a.Strokes)
                for (int k = 0; k < st.Count; k++) st[k] = map(st[k].X, st[k].Y);
            a.Rotation = ((a.Rotation - pageRot) % 4 + 4) % 4;
        }
    }
}
