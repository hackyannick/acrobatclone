using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace AcroClone.Pdf
{
    /// <summary>Hilfsfunktionen zum Erzeugen von PDF-Seitenobjekten (Text, Pfade, Bilder).</summary>
    public static class PdfObjects
    {
        public const string Helvetica = "Helvetica";
        public const string HelveticaBold = "Helvetica-Bold";
        public const string HelveticaOblique = "Helvetica-Oblique";
        public const string TimesItalic = "Times-Italic";
        public const string TimesRoman = "Times-Roman";
        public const string TimesBold = "Times-Bold";

        public static IntPtr Text(PdfDocument doc, string font, float size, string text, Color color)
        {
            IntPtr obj = Native.FPDFPageObj_NewTextObj(doc.Handle, font, size);
            Native.FPDFText_SetText(obj, text);
            Native.FPDFPageObj_SetFillColor(obj, color.R, color.G, color.B, color.A);
            return obj;
        }

        public static PdfRect Bounds(IntPtr obj)
        {
            float l, b, r, t;
            Native.FPDFPageObj_GetBounds(obj, out l, out b, out r, out t);
            return new PdfRect(l, b, r, t);
        }

        /// <summary>Breite eines Textobjekts in Punkt (Objekt im Ursprung).</summary>
        public static double TextWidth(PdfDocument doc, string font, float size, string text)
        {
            IntPtr obj = Text(doc, font, size, text, Color.Black);
            PdfRect r = Bounds(obj);
            Native.FPDFPageObj_Destroy(obj);
            return r.Width;
        }

        public static void Transform(IntPtr obj, Affine m)
        {
            Native.FPDFPageObj_Transform(obj, m.A, m.B, m.C, m.D, m.E, m.F);
        }

        public static void Style(IntPtr path, Color stroke, float width, Color fill)
        {
            bool hasStroke = stroke.A > 0 && width > 0;
            bool hasFill = fill.A > 0;
            if (hasStroke)
            {
                Native.FPDFPageObj_SetStrokeColor(path, stroke.R, stroke.G, stroke.B, stroke.A);
                Native.FPDFPageObj_SetStrokeWidth(path, width);
                Native.FPDFPageObj_SetLineJoin(path, Native.FPDF_LINEJOIN_ROUND);
                Native.FPDFPageObj_SetLineCap(path, Native.FPDF_LINECAP_ROUND);
            }
            if (hasFill) Native.FPDFPageObj_SetFillColor(path, fill.R, fill.G, fill.B, fill.A);
            Native.FPDFPath_SetDrawMode(path, hasFill ? Native.FPDF_FILLMODE_WINDING : Native.FPDF_FILLMODE_NONE, hasStroke ? 1 : 0);
        }

        public static IntPtr Rect(PdfRect r, Color stroke, float width, Color fill)
        {
            IntPtr p = Native.FPDFPageObj_CreateNewRect((float)r.Left, (float)r.Bottom, (float)r.Width, (float)r.Height);
            Style(p, stroke, width, fill);
            return p;
        }

        public static IntPtr RoundRect(PdfRect r, double radius, Color stroke, float width, Color fill)
        {
            double k = 0.5523 * radius;
            float l = (float)r.Left, b = (float)r.Bottom, rr = (float)r.Right, t = (float)r.Top, rad = (float)radius, kk = (float)k;
            IntPtr p = Native.FPDFPageObj_CreateNewPath(l + rad, b);
            Native.FPDFPath_LineTo(p, rr - rad, b);
            Native.FPDFPath_BezierTo(p, rr - rad + kk, b, rr, b + rad - kk, rr, b + rad);
            Native.FPDFPath_LineTo(p, rr, t - rad);
            Native.FPDFPath_BezierTo(p, rr, t - rad + kk, rr - rad + kk, t, rr - rad, t);
            Native.FPDFPath_LineTo(p, l + rad, t);
            Native.FPDFPath_BezierTo(p, l + rad - kk, t, l, t - rad + kk, l, t - rad);
            Native.FPDFPath_LineTo(p, l, b + rad);
            Native.FPDFPath_BezierTo(p, l, b + rad - kk, l + rad - kk, b, l + rad, b);
            Native.FPDFPath_Close(p);
            Style(p, stroke, width, fill);
            return p;
        }

        public static IntPtr Ellipse(PdfRect r, Color stroke, float width, Color fill)
        {
            double cx = (r.Left + r.Right) / 2, cy = (r.Bottom + r.Top) / 2;
            double rx = r.Width / 2, ry = r.Height / 2;
            double kx = 0.5523 * rx, ky = 0.5523 * ry;
            IntPtr p = Native.FPDFPageObj_CreateNewPath((float)(cx + rx), (float)cy);
            Native.FPDFPath_BezierTo(p, (float)(cx + rx), (float)(cy + ky), (float)(cx + kx), (float)(cy + ry), (float)cx, (float)(cy + ry));
            Native.FPDFPath_BezierTo(p, (float)(cx - kx), (float)(cy + ry), (float)(cx - rx), (float)(cy + ky), (float)(cx - rx), (float)cy);
            Native.FPDFPath_BezierTo(p, (float)(cx - rx), (float)(cy - ky), (float)(cx - kx), (float)(cy - ry), (float)cx, (float)(cy - ry));
            Native.FPDFPath_BezierTo(p, (float)(cx + kx), (float)(cy - ry), (float)(cx + rx), (float)(cy - ky), (float)(cx + rx), (float)cy);
            Native.FPDFPath_Close(p);
            Style(p, stroke, width, fill);
            return p;
        }

        public static IntPtr Polyline(IList<PdfPoint> points, Color stroke, float width, bool closed, Color fill)
        {
            if (points.Count == 0) return IntPtr.Zero;
            IntPtr p = Native.FPDFPageObj_CreateNewPath((float)points[0].X, (float)points[0].Y);
            if (points.Count == 1) Native.FPDFPath_LineTo(p, (float)points[0].X + 0.01f, (float)points[0].Y);
            for (int i = 1; i < points.Count; i++)
                Native.FPDFPath_LineTo(p, (float)points[i].X, (float)points[i].Y);
            if (closed) Native.FPDFPath_Close(p);
            Style(p, stroke, width, fill);
            return p;
        }

        // ------------------------------------------------------------------
        // Bilder
        // ------------------------------------------------------------------

        /// <summary>Kodiert ein Bild (auf weißem Hintergrund) als JPEG.</summary>
        public static byte[] ToJpeg(Image image, long quality)
        {
            using (Bitmap flat = new Bitmap(image.Width, image.Height, PixelFormat.Format24bppRgb))
            {
                flat.SetResolution(image.HorizontalResolution > 0 ? image.HorizontalResolution : 96,
                    image.VerticalResolution > 0 ? image.VerticalResolution : 96);
                using (Graphics g = Graphics.FromImage(flat))
                {
                    g.Clear(Color.White);
                    g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));
                }
                ImageCodecInfo codec = null;
                foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders())
                    if (c.FormatID == ImageFormat.Jpeg.Guid) codec = c;
                using (MemoryStream ms = new MemoryStream())
                {
                    if (codec != null)
                    {
                        EncoderParameters ep = new EncoderParameters(1);
                        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                        flat.Save(ms, codec, ep);
                    }
                    else
                    {
                        flat.Save(ms, ImageFormat.Jpeg);
                    }
                    return ms.ToArray();
                }
            }
        }

        /// <summary>Erzeugt ein Bildobjekt aus JPEG-Daten. Die Daten werden sofort eingebettet.</summary>
        public static IntPtr JpegImage(PdfDocument doc, IntPtr page, byte[] jpeg)
        {
            IntPtr img = Native.FPDFPageObj_NewImageObj(doc.Handle);
            GCHandle pin = GCHandle.Alloc(jpeg, GCHandleType.Pinned);
            Native.GetBlockHandler getBlock = delegate(IntPtr param, uint position, IntPtr buffer, uint size)
            {
                if (position + size > jpeg.Length) return 0;
                Marshal.Copy(jpeg, (int)position, buffer, (int)size);
                return 1;
            };
            // FPDF_FILEACCESS: { unsigned long m_FileLen; m_GetBlock; void* m_Param }
            int ps = IntPtr.Size;
            IntPtr access = Marshal.AllocHGlobal(ps * 3);
            try
            {
                for (int i = 0; i < ps * 3; i++) Marshal.WriteByte(access, i, 0);
                Marshal.WriteInt32(access, 0, jpeg.Length);
                Marshal.WriteIntPtr(access, ps, Marshal.GetFunctionPointerForDelegate(getBlock));
                Marshal.WriteIntPtr(access, ps * 2, IntPtr.Zero);
                IntPtr[] pages = page == IntPtr.Zero ? null : new IntPtr[] { page };
                if (Native.FPDFImageObj_LoadJpegFileInline(pages, pages == null ? 0 : 1, img, access) == 0)
                {
                    Native.FPDFPageObj_Destroy(img);
                    return IntPtr.Zero;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(access);
                pin.Free();
                GC.KeepAlive(getBlock);
            }
            return img;
        }

        // ------------------------------------------------------------------
        // Textumbruch (gemessen mit GDI+, Arial ~ Helvetica)
        // ------------------------------------------------------------------

        private static Bitmap measureBitmap;
        private static Graphics measureGraphics;

        private static Graphics MeasureGraphics
        {
            get
            {
                if (measureGraphics == null)
                {
                    measureBitmap = new Bitmap(1, 1);
                    measureGraphics = Graphics.FromImage(measureBitmap);
                    measureGraphics.PageUnit = GraphicsUnit.Point;
                    measureGraphics.TextRenderingHint = TextRenderingHint.AntiAlias;
                }
                return measureGraphics;
            }
        }

        public static string GdiFamilyFor(string pdfFont)
        {
            if (pdfFont.StartsWith("Times")) return "Times New Roman";
            if (pdfFont.StartsWith("Courier")) return "Courier New";
            return "Arial";
        }

        public static FontStyle GdiStyleFor(string pdfFont)
        {
            FontStyle s = FontStyle.Regular;
            if (pdfFont.Contains("Bold")) s |= FontStyle.Bold;
            if (pdfFont.Contains("Italic") || pdfFont.Contains("Oblique")) s |= FontStyle.Italic;
            return s;
        }

        public static float MeasureWidth(string text, string pdfFont, float size)
        {
            using (Font f = new Font(GdiFamilyFor(pdfFont), size, GdiStyleFor(pdfFont), GraphicsUnit.Point))
            {
                return MeasureGraphics.MeasureString(text, f, PointF.Empty, StringFormat.GenericTypographic).Width;
            }
        }

        /// <summary>Bricht Text in Zeilen um, die maximal maxWidth Punkt breit sind.</summary>
        public static List<string> Wrap(string text, string pdfFont, float size, double maxWidth)
        {
            List<string> lines = new List<string>();
            if (text == null) return lines;
            using (Font f = new Font(GdiFamilyFor(pdfFont), size, GdiStyleFor(pdfFont), GraphicsUnit.Point))
            {
                Graphics g = MeasureGraphics;
                string[] paragraphs = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                foreach (string para in paragraphs)
                {
                    if (para.Length == 0) { lines.Add(string.Empty); continue; }
                    string[] words = para.Split(' ');
                    string current = string.Empty;
                    foreach (string word in words)
                    {
                        string candidate = current.Length == 0 ? word : current + " " + word;
                        if (g.MeasureString(candidate, f, PointF.Empty, StringFormat.GenericTypographic).Width <= maxWidth || current.Length == 0)
                        {
                            current = candidate;
                            // Einzelnes überlanges Wort hart umbrechen
                            while (current.Length > 1 && g.MeasureString(current, f, PointF.Empty, StringFormat.GenericTypographic).Width > maxWidth)
                            {
                                int cut = current.Length - 1;
                                while (cut > 1 && g.MeasureString(current.Substring(0, cut), f, PointF.Empty, StringFormat.GenericTypographic).Width > maxWidth) cut--;
                                lines.Add(current.Substring(0, cut));
                                current = current.Substring(cut);
                            }
                        }
                        else
                        {
                            lines.Add(current);
                            current = word;
                        }
                    }
                    lines.Add(current);
                }
            }
            return lines;
        }

        /// <summary>Fügt einen umbrochenen Textblock in eine Seite ein. Gibt die y-Position unter dem Block zurück.</summary>
        public static double AddTextBlock(PdfDocument doc, IntPtr page, string text, string font, float size, Color color,
            double left, double top, double width, double lineFactor)
        {
            double y = top;
            foreach (string line in Wrap(text, font, size, width))
            {
                y -= size * lineFactor;
                if (line.Length > 0)
                {
                    IntPtr obj = Text(doc, font, size, line, color);
                    Transform(obj, new Affine(1, 0, 0, 1, left, y + size * 0.22));
                    Native.FPDFPage_InsertObject(page, obj);
                }
            }
            return y;
        }
    }
}
