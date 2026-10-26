using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using AcroClone.Pdf;

namespace AcroClone.Model
{
    /// <summary>Zeichnet Kommentare mit GDI+ (Bildschirm und Druck).</summary>
    public static class AnnotationPainter
    {
        public const float FreeTextPadding = 4f;
        public const float LineFactor = 1.2f;
        public const float Ascent = 0.905f;

        public static Color WithOpacity(Color c, float opacity)
        {
            if (c.IsEmpty) return Color.Empty;
            return Color.FromArgb(Math.Max(0, Math.Min(255, (int)(c.A * opacity))), c.R, c.G, c.B);
        }

        public static void Paint(Graphics g, Annotation a, Affine pageToDevice)
        {
            GraphicsState state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                switch (a.Type)
                {
                    case AnnotationType.Highlight:
                        PaintQuads(g, a, pageToDevice);
                        break;
                    case AnnotationType.Underline:
                    case AnnotationType.StrikeOut:
                        PaintLinesForQuads(g, a, pageToDevice);
                        break;
                    case AnnotationType.Line:
                    case AnnotationType.Arrow:
                    case AnnotationType.Ink:
                        PaintStrokes(g, a, pageToDevice);
                        break;
                    case AnnotationType.Signature:
                        if (a.Strokes.Count > 0) PaintStrokes(g, a, pageToDevice);
                        else PaintLocal(g, a, pageToDevice);
                        break;
                    default:
                        PaintLocal(g, a, pageToDevice);
                        break;
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        private static void PaintQuads(Graphics g, Annotation a, Affine m)
        {
            Color c = WithOpacity(Color.FromArgb(110, a.Color), a.Opacity);
            using (SolidBrush b = new SolidBrush(c))
            {
                foreach (PdfRect q in a.Quads)
                    g.FillRectangle(b, m.MapRect(q));
            }
        }

        private static void PaintLinesForQuads(Graphics g, Annotation a, Affine m)
        {
            Color c = WithOpacity(a.Color, a.Opacity);
            g.Transform = m.ToMatrix();
            foreach (PdfRect q in a.Quads)
            {
                double h = q.Height;
                float w = (float)Math.Max(0.6, h / 14);
                double y = a.Type == AnnotationType.Underline ? q.Bottom + h * 0.08 : q.Bottom + h * 0.45;
                using (Pen p = new Pen(c, w))
                    g.DrawLine(p, (float)q.Left, (float)y, (float)q.Right, (float)y);
            }
        }

        private static void PaintStrokes(Graphics g, Annotation a, Affine m)
        {
            Color c = WithOpacity(a.Color, a.Opacity);
            g.Transform = m.ToMatrix();
            using (Pen p = new Pen(c, Math.Max(0.3f, a.LineWidth)))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                foreach (List<PdfPoint> s in a.Strokes)
                {
                    if (s.Count == 1)
                    {
                        float r = a.LineWidth / 2;
                        using (SolidBrush b = new SolidBrush(c))
                            g.FillEllipse(b, (float)s[0].X - r, (float)s[0].Y - r, r * 2, r * 2);
                        continue;
                    }
                    PointF[] pts = new PointF[s.Count];
                    for (int i = 0; i < s.Count; i++) pts[i] = new PointF((float)s[i].X, (float)s[i].Y);
                    g.DrawLines(p, pts);
                }
                if (a.Type == AnnotationType.Arrow)
                {
                    foreach (List<PdfPoint> head in ArrowHead(a))
                    {
                        PointF[] pts = new PointF[head.Count];
                        for (int i = 0; i < head.Count; i++) pts[i] = new PointF((float)head[i].X, (float)head[i].Y);
                        g.DrawLines(p, pts);
                    }
                }
            }
        }

        /// <summary>Pfeilspitze als offener Linienzug (zwei Schenkel).</summary>
        public static List<List<PdfPoint>> ArrowHead(Annotation a)
        {
            List<List<PdfPoint>> result = new List<List<PdfPoint>>();
            if (a.Strokes.Count == 0 || a.Strokes[0].Count < 2) return result;
            List<PdfPoint> s = a.Strokes[0];
            PdfPoint end = s[s.Count - 1], start = s[s.Count - 2];
            double dx = end.X - start.X, dy = end.Y - start.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.01) return result;
            dx /= len; dy /= len;
            double size = Math.Max(8, a.LineWidth * 4);
            double ang = Math.PI / 7;
            double cos = Math.Cos(ang), sin = Math.Sin(ang);
            double lx = -(dx * cos - dy * sin), ly = -(dx * sin + dy * cos);
            double rx = -(dx * cos + dy * sin), ry = -(-dx * sin + dy * cos);
            List<PdfPoint> head = new List<PdfPoint>();
            head.Add(new PdfPoint(end.X + lx * size, end.Y + ly * size));
            head.Add(end);
            head.Add(new PdfPoint(end.X + rx * size, end.Y + ry * size));
            result.Add(head);
            return result;
        }

        private static void PaintLocal(Graphics g, Annotation a, Affine pageToDevice)
        {
            Affine local = a.LocalToPage.Then(pageToDevice);
            g.Transform = local.ToMatrix();
            SizeF size = a.LocalSize;
            switch (a.Type)
            {
                case AnnotationType.Note:
                    DrawNoteIcon(g, new RectangleF(0, 0, size.Width, size.Height), WithOpacity(a.Color, a.Opacity));
                    break;
                case AnnotationType.FreeText:
                    DrawFreeText(g, a, size);
                    break;
                case AnnotationType.Rectangle:
                case AnnotationType.Ellipse:
                    DrawShape(g, a, size);
                    break;
                case AnnotationType.Stamp:
                    DrawStamp(g, a.Text, WithOpacity(a.Color, a.Opacity), size);
                    break;
                case AnnotationType.Signature:
                    DrawTypedSignature(g, a, size);
                    break;
                case AnnotationType.Redaction:
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 255, 0, 0)))
                        g.FillRectangle(b, 0, 0, size.Width, size.Height);
                    using (Pen p = new Pen(Color.Red, 1.2f))
                        g.DrawRectangle(p, 0, 0, size.Width, size.Height);
                    break;
            }
        }

        public static void DrawNoteIcon(Graphics g, RectangleF r, Color color)
        {
            float w = r.Width, h = r.Height;
            using (GraphicsPath path = new GraphicsPath())
            {
                // Sprechblase wie das klassische Acrobat-Notizsymbol
                float bh = h * 0.78f;
                path.AddLine(r.Left + w * 0.08f, r.Top + h * 0.02f, r.Right - w * 0.04f, r.Top + h * 0.02f);
                path.AddLine(r.Right - w * 0.04f, r.Top + h * 0.02f, r.Right - w * 0.04f, r.Top + bh);
                path.AddLine(r.Right - w * 0.04f, r.Top + bh, r.Left + w * 0.45f, r.Top + bh);
                path.AddLine(r.Left + w * 0.45f, r.Top + bh, r.Left + w * 0.22f, r.Bottom - h * 0.02f);
                path.AddLine(r.Left + w * 0.22f, r.Bottom - h * 0.02f, r.Left + w * 0.28f, r.Top + bh);
                path.AddLine(r.Left + w * 0.28f, r.Top + bh, r.Left + w * 0.04f, r.Top + bh);
                path.AddLine(r.Left + w * 0.04f, r.Top + bh, r.Left + w * 0.04f, r.Top + h * 0.02f);
                path.CloseFigure();
                using (SolidBrush b = new SolidBrush(color.IsEmpty ? Color.Yellow : color))
                    g.FillPath(b, path);
                using (Pen p = new Pen(Color.FromArgb(color.A, 60, 60, 60), Math.Max(0.6f, w / 22f)))
                    g.DrawPath(p, path);
            }
            using (Pen p = new Pen(Color.FromArgb(color.A, 70, 70, 70), Math.Max(0.5f, w / 26f)))
            {
                for (int i = 0; i < 3; i++)
                {
                    float y = r.Top + h * (0.2f + i * 0.17f);
                    g.DrawLine(p, r.Left + w * 0.2f, y, r.Right - w * 0.2f, y);
                }
            }
        }

        public static List<string> FreeTextLines(Annotation a, float width)
        {
            return PdfObjects.Wrap(a.Text, PdfObjects.Helvetica, a.FontSize, Math.Max(4, width - 2 * FreeTextPadding));
        }

        private static void DrawFreeText(Graphics g, Annotation a, SizeF size)
        {
            RectangleF r = new RectangleF(0, 0, size.Width, size.Height);
            if (a.FillColor.A > 0)
                using (SolidBrush b = new SolidBrush(WithOpacity(a.FillColor, a.Opacity)))
                    g.FillRectangle(b, r);
            if (a.LineWidth > 0)
                using (Pen p = new Pen(WithOpacity(a.Color, a.Opacity), a.LineWidth))
                    g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            using (Font f = new Font("Arial", a.FontSize, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush tb = new SolidBrush(WithOpacity(Color.Black, a.Opacity)))
            {
                g.SetClip(r);
                float y = FreeTextPadding;
                foreach (string line in FreeTextLines(a, size.Width))
                {
                    g.DrawString(line, f, tb, FreeTextPadding, y, StringFormat.GenericTypographic);
                    y += a.FontSize * LineFactor;
                }
                g.ResetClip();
            }
        }

        private static void DrawShape(Graphics g, Annotation a, SizeF size)
        {
            RectangleF r = new RectangleF(0, 0, size.Width, size.Height);
            if (a.FillColor.A > 0)
                using (SolidBrush b = new SolidBrush(WithOpacity(a.FillColor, a.Opacity)))
                {
                    if (a.Type == AnnotationType.Ellipse) g.FillEllipse(b, r); else g.FillRectangle(b, r);
                }
            using (Pen p = new Pen(WithOpacity(a.Color, a.Opacity), Math.Max(0.3f, a.LineWidth)))
            {
                p.LineJoin = LineJoin.Round;
                if (a.Type == AnnotationType.Ellipse) g.DrawEllipse(p, r); else g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            }
        }

        public static float StampBorder(SizeF size)
        {
            return Math.Max(1.5f, Math.Min(size.Height * 0.07f, 4f));
        }

        public static float StampFontSize(string text, SizeF size)
        {
            float border = StampBorder(size);
            float maxW = size.Width - 4 * border - 6;
            float unit = PdfObjects.MeasureWidth(text, PdfObjects.HelveticaBold, 10f) / 10f;
            float fs = size.Height * 0.52f;
            if (unit > 0 && unit * fs > maxW) fs = maxW / unit;
            return Math.Max(2f, fs);
        }

        public static void DrawStamp(Graphics g, string text, Color color, SizeF size)
        {
            float border = StampBorder(size);
            RectangleF r = new RectangleF(border / 2, border / 2, size.Width - border, size.Height - border);
            float radius = Math.Min(r.Height, r.Width) * 0.18f;
            using (GraphicsPath path = RoundedRect(r, radius))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(color.A / 8, color)))
                    g.FillPath(b, path);
                using (Pen p = new Pen(color, border))
                    g.DrawPath(p, path);
            }
            float fs = StampFontSize(text, size);
            using (Font f = new Font("Arial", fs, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush tb = new SolidBrush(color))
            {
                float tw = PdfObjects.MeasureWidth(text, PdfObjects.HelveticaBold, fs);
                float x = (size.Width - tw) / 2;
                float baseline = size.Height / 2 + fs * 0.36f;
                g.DrawString(text, f, tb, x, baseline - fs * Ascent, StringFormat.GenericTypographic);
            }
        }

        public static float SignatureFontSize(Annotation a, SizeF size)
        {
            float unit = PdfObjects.MeasureWidth(a.Text, PdfObjects.TimesItalic, 10f) / 10f;
            float fs = size.Height * 0.7f;
            if (unit > 0 && unit * fs > size.Width * 0.96f) fs = size.Width * 0.96f / unit;
            return Math.Max(2f, fs);
        }

        private static void DrawTypedSignature(Graphics g, Annotation a, SizeF size)
        {
            float fs = SignatureFontSize(a, size);
            using (Font f = new Font("Times New Roman", fs, FontStyle.Italic, GraphicsUnit.Pixel))
            using (SolidBrush tb = new SolidBrush(WithOpacity(a.Color, a.Opacity)))
            {
                float tw = PdfObjects.MeasureWidth(a.Text, PdfObjects.TimesItalic, fs);
                float baseline = size.Height / 2 + fs * 0.3f;
                g.DrawString(a.Text, f, tb, (size.Width - tw) / 2, baseline - fs * 0.891f, StringFormat.GenericTypographic);
            }
        }

        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Max(0.1f, radius * 2);
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // ------------------------------------------------------------------
        // Auswahl
        // ------------------------------------------------------------------

        public static RectangleF DeviceBounds(Annotation a, Affine pageToDevice)
        {
            return pageToDevice.MapRect(a.Bounds);
        }

        public static PointF[] HandlePoints(RectangleF r)
        {
            return new PointF[]
            {
                new PointF(r.Left, r.Top), new PointF(r.Right, r.Top),
                new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom)
            };
        }

        public static void PaintSelection(Graphics g, Annotation a, Affine pageToDevice)
        {
            RectangleF r = DeviceBounds(a, pageToDevice);
            r.Inflate(2, 2);
            GraphicsState st = g.Save();
            g.SmoothingMode = SmoothingMode.None;
            using (Pen p = new Pen(Color.FromArgb(40, 90, 200), 1f))
            {
                p.DashStyle = DashStyle.Dot;
                g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            }
            if (a.IsResizable)
            {
                foreach (PointF h in HandlePoints(r))
                {
                    RectangleF hr = new RectangleF(h.X - 3, h.Y - 3, 6, 6);
                    g.FillRectangle(Brushes.White, hr);
                    g.DrawRectangle(Pens.RoyalBlue, hr.X, hr.Y, hr.Width, hr.Height);
                }
            }
            if (a.Type == AnnotationType.Line || a.Type == AnnotationType.Arrow)
            {
                if (a.Strokes.Count > 0 && a.Strokes[0].Count >= 2)
                {
                    List<PdfPoint> s = a.Strokes[0];
                    foreach (PdfPoint pp in new PdfPoint[] { s[0], s[s.Count - 1] })
                    {
                        PointF d = pageToDevice.Apply(pp.X, pp.Y);
                        RectangleF hr = new RectangleF(d.X - 3, d.Y - 3, 6, 6);
                        g.FillRectangle(Brushes.White, hr);
                        g.DrawRectangle(Pens.RoyalBlue, hr.X, hr.Y, hr.Width, hr.Height);
                    }
                }
            }
            g.Restore(st);
        }
    }
}
