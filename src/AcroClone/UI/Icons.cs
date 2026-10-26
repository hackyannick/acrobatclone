using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AcroClone.UI
{
    /// <summary>
    /// Zur Laufzeit gezeichnete Symbole im Stil der Acrobat-9-Werkzeugleisten.
    /// Alle Symbole werden auf einem 16x16-Raster entworfen und beliebig skaliert.
    /// </summary>
    public static class Icons
    {
        private static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        public static Bitmap Get(string name)
        {
            return Get(name, 16);
        }

        public static Bitmap Get(string name, int size)
        {
            string key = name + "@" + size;
            Bitmap bmp;
            if (cache.TryGetValue(key, out bmp)) return bmp;
            bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.ScaleTransform(size / 16f, size / 16f);
                Draw(g, name);
            }
            cache[key] = bmp;
            return bmp;
        }

        // ------------------------------------------------------------------
        // Grundformen
        // ------------------------------------------------------------------

        private static Brush Grad(float x, float y, float w, float h, Color a, Color b)
        {
            return new LinearGradientBrush(new RectangleF(x, y - 0.5f, Math.Max(0.5f, w), h + 1), a, b, LinearGradientMode.Vertical);
        }

        private static GraphicsPath Round(float x, float y, float w, float h, float r)
        {
            GraphicsPath p = new GraphicsPath();
            float d = r * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private static void Page(Graphics g, float x, float y, float w, float h)
        {
            float f = Math.Min(w, h) * 0.32f;
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddLine(x, y, x + w - f, y);
                p.AddLine(x + w - f, y, x + w, y + f);
                p.AddLine(x + w, y + f, x + w, y + h);
                p.AddLine(x + w, y + h, x, y + h);
                p.CloseFigure();
                using (Brush b = Grad(x, y, w, h, Color.White, Color.FromArgb(226, 230, 238)))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(110, 118, 132), 0.8f))
                    g.DrawPath(pen, p);
            }
            using (Pen pen = new Pen(Color.FromArgb(110, 118, 132), 0.7f))
            {
                g.DrawLine(pen, x + w - f, y, x + w - f, y + f);
                g.DrawLine(pen, x + w - f, y + f, x + w, y + f);
            }
        }

        private static void TextLines(Graphics g, float x, float y, float w, int n, float step)
        {
            using (Pen p = new Pen(Color.FromArgb(150, 158, 172), 0.8f))
                for (int i = 0; i < n; i++) g.DrawLine(p, x, y + i * step, x + w * (i == n - 1 ? 0.6f : 1f), y + i * step);
        }

        private static void Circle(Graphics g, float x, float y, float d, Color top, Color bottom, Color border)
        {
            using (Brush b = Grad(x, y, d, d, top, bottom)) g.FillEllipse(b, x, y, d, d);
            using (Pen p = new Pen(border, 0.8f)) g.DrawEllipse(p, x, y, d, d);
            using (Brush shine = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
                g.FillEllipse(shine, x + d * 0.2f, y + d * 0.08f, d * 0.6f, d * 0.4f);
        }

        private static void Plus(Graphics g, float cx, float cy, float s, Color c, float w)
        {
            using (Pen p = new Pen(c, w))
            {
                g.DrawLine(p, cx - s, cy, cx + s, cy);
                g.DrawLine(p, cx, cy - s, cx, cy + s);
            }
        }

        private static void RedX(Graphics g, float x, float y, float s)
        {
            using (Pen p = new Pen(Color.FromArgb(200, 30, 30), 2.2f))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                g.DrawLine(p, x, y, x + s, y + s);
                g.DrawLine(p, x + s, y, x, y + s);
            }
        }

        private static void Arrow(Graphics g, PointF from, PointF to, Color c, float width)
        {
            using (Pen p = new Pen(c, width))
            {
                p.CustomEndCap = new AdjustableArrowCap(2.2f, 2.2f, true);
                g.DrawLine(p, from, to);
            }
        }

        private static void BigT(Graphics g, Color c)
        {
            using (Font f = new Font("Times New Roman", 12f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush b = new SolidBrush(c))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                g.DrawString("T", f, b, new RectangleF(0, -0.5f, 16, 14), sf);
            }
        }

        private static void Logo(Graphics g, float x, float y, float s)
        {
            using (GraphicsPath p = Round(x, y, s, s, s * 0.18f))
            {
                using (Brush b = Grad(x, y, s, s, Color.FromArgb(232, 52, 40), Color.FromArgb(150, 10, 10)))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(110, 0, 0), s / 22f)) g.DrawPath(pen, p);
            }
            // Weißes Dokument mit Eselsohr (eigenes Logo)
            float l = x + s * 0.26f, t = y + s * 0.17f, r = x + s * 0.74f, bt = y + s * 0.82f, f = s * 0.17f;
            g.FillPolygon(Brushes.White, new PointF[] { new PointF(l, t), new PointF(r - f, t), new PointF(r, t + f), new PointF(r, bt), new PointF(l, bt) });
            using (SolidBrush ear = new SolidBrush(Color.FromArgb(220, 200, 200)))
                g.FillPolygon(ear, new PointF[] { new PointF(r - f, t), new PointF(r - f, t + f), new PointF(r, t + f) });
            using (SolidBrush grey = new SolidBrush(Color.FromArgb(120, 120, 120)))
                g.FillRectangle(grey, l + s * 0.07f, y + s * 0.29f, s * 0.19f, s * 0.043f);
            using (SolidBrush red = new SolidBrush(Color.FromArgb(190, 30, 30)))
            {
                g.FillRectangle(red, l + s * 0.07f, y + s * 0.41f, (r - l) - s * 0.14f, s * 0.043f);
                g.FillRectangle(red, l + s * 0.07f, y + s * 0.51f, (r - l) - s * 0.14f, s * 0.043f);
                g.FillRectangle(red, l + s * 0.07f, y + s * 0.61f, (r - l) - s * 0.28f, s * 0.043f);
            }
            using (Brush shine = new SolidBrush(Color.FromArgb(50, 255, 255, 255)))
                g.FillRectangle(shine, x + s * 0.06f, y + s * 0.06f, s * 0.88f, s * 0.36f);
        }

        // ------------------------------------------------------------------
        // Symbole
        // ------------------------------------------------------------------

        private static void Draw(Graphics g, string name)
        {
            switch (name)
            {
                case "logo":
                    Logo(g, 0.5f, 0.5f, 15f);
                    break;

                case "open":
                    using (Brush b = Grad(1, 3, 13, 10, Color.FromArgb(240, 196, 92), Color.FromArgb(214, 152, 40)))
                    {
                        g.FillPolygon(b, new PointF[] { new PointF(1, 3), new PointF(5.5f, 3), new PointF(6.5f, 4.5f), new PointF(13, 4.5f), new PointF(13, 13), new PointF(1, 13) });
                    }
                    Page(g, 3.5f, 2f, 8, 8);
                    using (Brush b = Grad(1, 7, 15, 6, Color.FromArgb(255, 226, 130), Color.FromArgb(232, 178, 60)))
                        g.FillPolygon(b, new PointF[] { new PointF(3, 7), new PointF(15.5f, 7), new PointF(13, 13.5f), new PointF(0.8f, 13.5f) });
                    using (Pen p = new Pen(Color.FromArgb(160, 110, 20), 0.8f))
                        g.DrawPolygon(p, new PointF[] { new PointF(3, 7), new PointF(15.5f, 7), new PointF(13, 13.5f), new PointF(0.8f, 13.5f) });
                    break;

                case "save":
                case "saveAs":
                    using (GraphicsPath p = Round(1.5f, 1.5f, 13, 13, 1.5f))
                    {
                        using (Brush b = Grad(1.5f, 1.5f, 13, 13, Color.FromArgb(90, 132, 214), Color.FromArgb(32, 72, 150)))
                            g.FillPath(b, p);
                        using (Pen pen = new Pen(Color.FromArgb(20, 44, 100), 0.8f)) g.DrawPath(pen, p);
                    }
                    g.FillRectangle(Brushes.White, 4, 2, 8, 5.5f);
                    TextLines(g, 5, 3.5f, 6, 2, 1.8f);
                    using (Brush b = new SolidBrush(Color.FromArgb(200, 208, 220))) g.FillRectangle(b, 4.5f, 10, 7, 4.5f);
                    using (Brush b = new SolidBrush(Color.FromArgb(40, 60, 100))) g.FillRectangle(b, 9, 10.8f, 1.6f, 3);
                    if (name == "saveAs")
                    {
                        using (Pen p = new Pen(Color.FromArgb(230, 160, 20), 2f)) g.DrawLine(p, 9.5f, 15, 15, 9.5f);
                    }
                    break;

                case "print":
                    Page(g, 4, 0.8f, 8, 6);
                    using (GraphicsPath p = Round(1, 6, 14, 6.5f, 1.5f))
                    {
                        using (Brush b = Grad(1, 6, 14, 6.5f, Color.FromArgb(220, 224, 230), Color.FromArgb(140, 148, 160)))
                            g.FillPath(b, p);
                        using (Pen pen = new Pen(Color.FromArgb(80, 86, 96), 0.8f)) g.DrawPath(pen, p);
                    }
                    g.FillRectangle(Brushes.White, 4, 10.5f, 8, 4.5f);
                    using (Pen pen = new Pen(Color.FromArgb(110, 118, 132), 0.7f)) g.DrawRectangle(pen, 4, 10.5f, 8, 4.5f);
                    TextLines(g, 5, 12, 6, 2, 1.6f);
                    using (Brush b = new SolidBrush(Color.FromArgb(60, 200, 60))) g.FillEllipse(b, 12, 7.5f, 1.6f, 1.6f);
                    break;

                case "create":
                    Page(g, 2, 1, 10, 13);
                    TextLines(g, 4, 5, 6, 3, 2.2f);
                    Circle(g, 8.5f, 8.5f, 7, Color.FromArgb(120, 210, 90), Color.FromArgb(40, 140, 30), Color.FromArgb(30, 100, 20));
                    Plus(g, 12, 12, 2.2f, Color.White, 1.6f);
                    break;

                case "combine":
                    Page(g, 1, 1, 8, 10);
                    Page(g, 7, 5, 8, 10);
                    using (Brush b = new SolidBrush(Color.FromArgb(220, 40, 40))) g.FillRectangle(b, 2.5f, 3.5f, 3.5f, 1.5f);
                    Arrow(g, new PointF(4.5f, 13.5f), new PointF(7, 13.5f), Color.FromArgb(40, 110, 200), 1.4f);
                    break;

                case "secure":
                    using (Pen p = new Pen(Color.FromArgb(120, 120, 128), 2f)) g.DrawArc(p, 4.5f, 1.5f, 7, 8, 180, 180);
                    using (GraphicsPath p = Round(2.5f, 6.5f, 11, 8.5f, 1.5f))
                    {
                        using (Brush b = Grad(2.5f, 6.5f, 11, 8.5f, Color.FromArgb(255, 220, 110), Color.FromArgb(214, 150, 20)))
                            g.FillPath(b, p);
                        using (Pen pen = new Pen(Color.FromArgb(150, 100, 10), 0.8f)) g.DrawPath(pen, p);
                    }
                    using (Brush b = new SolidBrush(Color.FromArgb(90, 60, 10))) g.FillEllipse(b, 7, 9, 2, 2);
                    g.FillRectangle(new SolidBrush(Color.FromArgb(90, 60, 10)), 7.6f, 10.5f, 0.8f, 2.2f);
                    break;

                case "sign":
                    using (Pen p = new Pen(Color.FromArgb(30, 60, 140), 1.1f))
                    {
                        g.DrawBezier(p, 1, 13, 3, 9, 4, 15, 6, 12);
                        g.DrawBezier(p, 6, 12, 7, 10, 8, 14, 10, 13);
                    }
                    using (Brush b = Grad(7, 1, 8, 9, Color.FromArgb(70, 70, 80), Color.FromArgb(20, 20, 26)))
                        g.FillPolygon(b, new PointF[] { new PointF(13, 1), new PointF(15.5f, 3.5f), new PointF(9.5f, 9.5f), new PointF(7, 10.5f), new PointF(8, 8) });
                    using (Brush b = new SolidBrush(Color.FromArgb(220, 180, 60)))
                        g.FillPolygon(b, new PointF[] { new PointF(9.5f, 9.5f), new PointF(7, 10.5f), new PointF(8, 8) });
                    break;

                case "forms":
                case "form":
                    Page(g, 1.5f, 1, 12, 14);
                    using (Pen p = new Pen(Color.FromArgb(70, 110, 190), 0.9f))
                    {
                        g.DrawRectangle(p, 3.5f, 4, 7, 2.2f);
                        g.DrawRectangle(p, 3.5f, 8, 7, 2.2f);
                        g.DrawRectangle(p, 3.5f, 12, 2, 2);
                    }
                    using (Brush b = new SolidBrush(Color.FromArgb(200, 215, 245))) g.FillRectangle(b, 4f, 4.5f, 6, 1.4f);
                    using (Pen p = new Pen(Color.FromArgb(40, 140, 40), 1f)) g.DrawLines(p, new PointF[] { new PointF(3.8f, 13), new PointF(4.5f, 13.8f), new PointF(6, 11.8f) });
                    break;

                case "comment":
                case "comments":
                    using (GraphicsPath p = new GraphicsPath())
                    {
                        p.AddPath(Round(1, 1.5f, 14, 9.5f, 2), false);
                        using (Brush b = Grad(1, 1.5f, 14, 11, Color.FromArgb(255, 248, 170), Color.FromArgb(244, 214, 60)))
                        {
                            g.FillPath(b, p);
                            g.FillPolygon(b, new PointF[] { new PointF(4, 10.5f), new PointF(3.5f, 14.5f), new PointF(8, 10.5f) });
                        }
                        using (Pen pen = new Pen(Color.FromArgb(170, 130, 20), 0.8f))
                        {
                            g.DrawPath(pen, p);
                            g.DrawLines(pen, new PointF[] { new PointF(4, 11), new PointF(3.5f, 14.5f), new PointF(8, 11) });
                        }
                    }
                    TextLines(g, 3.5f, 4, 9, 3, 2.1f);
                    break;

                case "note":
                    using (Brush b = Grad(2, 1.5f, 12, 13, Color.FromArgb(255, 252, 170), Color.FromArgb(248, 220, 70)))
                        g.FillRectangle(b, 2, 1.5f, 12, 13);
                    using (Pen p = new Pen(Color.FromArgb(170, 130, 20), 0.8f)) g.DrawRectangle(p, 2, 1.5f, 12, 13);
                    TextLines(g, 4, 5, 8, 3, 2.3f);
                    using (Brush b = new SolidBrush(Color.FromArgb(220, 60, 40))) g.FillRectangle(b, 2, 1.5f, 12, 1.8f);
                    break;

                case "prevPage":
                case "nextPage":
                    Circle(g, 1, 1, 14, Color.FromArgb(130, 175, 240), Color.FromArgb(36, 92, 186), Color.FromArgb(24, 66, 140));
                    using (Brush b = new SolidBrush(Color.White))
                    {
                        if (name == "prevPage") g.FillPolygon(b, new PointF[] { new PointF(8, 4), new PointF(12, 9.5f), new PointF(4, 9.5f) });
                        else g.FillPolygon(b, new PointF[] { new PointF(4, 6.5f), new PointF(12, 6.5f), new PointF(8, 12) });
                    }
                    break;

                case "firstPage":
                case "lastPage":
                    Circle(g, 1, 1, 14, Color.FromArgb(130, 175, 240), Color.FromArgb(36, 92, 186), Color.FromArgb(24, 66, 140));
                    using (Brush b = new SolidBrush(Color.White))
                    {
                        if (name == "firstPage")
                        {
                            g.FillRectangle(b, 4, 4, 8, 1.4f);
                            g.FillPolygon(b, new PointF[] { new PointF(8, 6), new PointF(12, 11), new PointF(4, 11) });
                        }
                        else
                        {
                            g.FillRectangle(b, 4, 10.6f, 8, 1.4f);
                            g.FillPolygon(b, new PointF[] { new PointF(4, 5), new PointF(12, 5), new PointF(8, 10) });
                        }
                    }
                    break;

                case "hand":
                    DrawHand(g, false);
                    break;

                case "select":
                    using (Pen p = new Pen(Color.Black, 1f))
                    {
                        g.DrawLine(p, 9, 2, 9, 14);
                        g.DrawLine(p, 7, 2, 11, 2);
                        g.DrawLine(p, 7, 14, 11, 14);
                    }
                    using (Brush b = new SolidBrush(Color.White))
                    using (Pen p = new Pen(Color.Black, 0.8f))
                    {
                        PointF[] arrow = { new PointF(1.5f, 1.5f), new PointF(1.5f, 10), new PointF(3.5f, 8), new PointF(5, 11), new PointF(6.2f, 10.4f), new PointF(4.8f, 7.6f), new PointF(7.3f, 7.4f) };
                        g.FillPolygon(b, arrow);
                        g.DrawPolygon(p, arrow);
                    }
                    break;

                case "zoomTool":
                case "zoomIn":
                case "zoomOut":
                    if (name == "zoomTool")
                    {
                        using (Pen p = new Pen(Color.FromArgb(90, 70, 50), 2.6f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, 10, 10, 14.5f, 14.5f); }
                        Circle(g, 1, 1, 10, Color.FromArgb(235, 245, 255), Color.FromArgb(170, 205, 240), Color.FromArgb(60, 80, 110));
                        Plus(g, 6, 6, 2.6f, Color.FromArgb(30, 60, 120), 1.4f);
                    }
                    else
                    {
                        Circle(g, 1, 1, 14, Color.FromArgb(130, 175, 240), Color.FromArgb(36, 92, 186), Color.FromArgb(24, 66, 140));
                        using (Pen p = new Pen(Color.White, 2f))
                        {
                            g.DrawLine(p, 4.5f, 8, 11.5f, 8);
                            if (name == "zoomIn") g.DrawLine(p, 8, 4.5f, 8, 11.5f);
                        }
                    }
                    break;

                case "fitPage":
                    Page(g, 3.5f, 1, 9, 14);
                    using (Pen p = new Pen(Color.FromArgb(40, 90, 180), 1f))
                    {
                        p.CustomEndCap = new AdjustableArrowCap(2, 2, true);
                        g.DrawLine(p, 8, 7, 8, 3);
                        g.DrawLine(p, 8, 9, 8, 13);
                    }
                    break;

                case "fitWidth":
                    Page(g, 1, 2.5f, 14, 11);
                    using (Pen p = new Pen(Color.FromArgb(40, 90, 180), 1f))
                    {
                        p.CustomEndCap = new AdjustableArrowCap(2, 2, true);
                        g.DrawLine(p, 7, 8, 3, 8);
                        g.DrawLine(p, 9, 8, 13, 8);
                    }
                    break;

                case "actualSize":
                    Page(g, 2.5f, 1, 11, 14);
                    using (Font f = new Font("Arial", 5.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                        g.DrawString("1:1", f, Brushes.Black, 3.6f, 6);
                    break;

                case "rotateCw":
                case "rotateCcw":
                    {
                        Page(g, 4, 4, 8, 10);
                        using (Pen p = new Pen(Color.FromArgb(40, 140, 40), 1.6f))
                        {
                            p.CustomEndCap = new AdjustableArrowCap(2.2f, 2.2f, true);
                            if (name == "rotateCw") g.DrawArc(p, 1.5f, 1, 13, 10, 200, 120);
                            else g.DrawArc(p, 1.5f, 1, 13, 10, 340, -120);
                        }
                    }
                    break;

                case "find":
                case "search":
                    using (Brush b = Grad(1, 3, 14, 10, Color.FromArgb(90, 96, 110), Color.FromArgb(30, 34, 42)))
                    {
                        g.FillEllipse(b, 0.8f, 7, 6.5f, 6.5f);
                        g.FillEllipse(b, 8.7f, 7, 6.5f, 6.5f);
                        g.FillRectangle(b, 2, 3, 3.5f, 6);
                        g.FillRectangle(b, 10.5f, 3, 3.5f, 6);
                        g.FillRectangle(b, 5.5f, 5, 5, 3);
                    }
                    using (Brush b = new SolidBrush(Color.FromArgb(140, 190, 240)))
                    {
                        g.FillEllipse(b, 2, 8.2f, 4, 4);
                        g.FillEllipse(b, 10, 8.2f, 4, 4);
                    }
                    break;

                case "pages":
                    Page(g, 4.5f, 0.8f, 9, 11);
                    Page(g, 2.5f, 3.8f, 9, 11);
                    TextLines(g, 4, 8, 6, 3, 2);
                    break;

                case "bookmarks":
                    using (Brush b = Grad(4, 1, 8, 14, Color.FromArgb(110, 160, 235), Color.FromArgb(30, 80, 170)))
                        g.FillPolygon(b, new PointF[] { new PointF(4, 1), new PointF(12, 1), new PointF(12, 15), new PointF(8, 11), new PointF(4, 15) });
                    using (Pen p = new Pen(Color.FromArgb(20, 50, 120), 0.8f))
                        g.DrawPolygon(p, new PointF[] { new PointF(4, 1), new PointF(12, 1), new PointF(12, 15), new PointF(8, 11), new PointF(4, 15) });
                    break;

                case "attachments":
                    using (Pen p = new Pen(Color.FromArgb(100, 106, 120), 1.4f))
                    {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                        g.DrawLine(p, 6, 4, 6, 11);
                        g.DrawArc(p, 6, 11 - 2.5f, 5, 5, 0, 180);
                        g.DrawLine(p, 11, 11, 11, 3);
                        g.DrawArc(p, 3.5f, 1, 7.5f, 5, 180, 180);
                        g.DrawLine(p, 3.5f, 3.5f, 3.5f, 11);
                        g.DrawArc(p, 3.5f, 8, 9.5f, 7, 0, 180);
                        g.DrawLine(p, 13, 11.5f, 13, 6);
                    }
                    break;

                case "signatures":
                    Page(g, 2, 1, 12, 14);
                    using (Pen p = new Pen(Color.FromArgb(30, 60, 140), 1f))
                        g.DrawBezier(p, 3.5f, 11, 6, 6, 7, 14, 12, 9);
                    break;

                case "highlight":
                    using (Brush b = new SolidBrush(Color.FromArgb(255, 238, 40))) g.FillRectangle(b, 0.5f, 11, 11, 4);
                    using (Brush b = Grad(5, 1, 9, 10, Color.FromArgb(120, 130, 150), Color.FromArgb(60, 66, 80)))
                        g.FillPolygon(b, new PointF[] { new PointF(10.5f, 1), new PointF(15, 5.5f), new PointF(8.5f, 12), new PointF(4, 7.5f) });
                    using (Brush b = new SolidBrush(Color.FromArgb(250, 220, 30)))
                        g.FillPolygon(b, new PointF[] { new PointF(4, 7.5f), new PointF(8.5f, 12), new PointF(5, 13), new PointF(3, 11) });
                    break;

                case "underline":
                    BigT(g, Color.FromArgb(40, 40, 40));
                    using (Pen p = new Pen(Color.FromArgb(30, 100, 220), 1.6f)) g.DrawLine(p, 2, 14.5f, 14, 14.5f);
                    break;

                case "strikeout":
                    BigT(g, Color.FromArgb(40, 40, 40));
                    using (Pen p = new Pen(Color.FromArgb(220, 30, 30), 1.6f)) g.DrawLine(p, 2, 7.5f, 14, 7.5f);
                    break;

                case "textbox":
                    g.FillRectangle(Brushes.White, 1.5f, 1.5f, 13, 13);
                    using (Pen p = new Pen(Color.FromArgb(200, 40, 40), 1f)) g.DrawRectangle(p, 1.5f, 1.5f, 13, 13);
                    BigT(g, Color.FromArgb(40, 40, 40));
                    break;

                case "rectangle":
                    using (Pen p = new Pen(Color.FromArgb(40, 80, 180), 1.6f)) g.DrawRectangle(p, 2, 3.5f, 12, 9);
                    break;

                case "ellipse":
                    using (Pen p = new Pen(Color.FromArgb(40, 80, 180), 1.6f)) g.DrawEllipse(p, 1.5f, 3, 13, 10);
                    break;

                case "line":
                    using (Pen p = new Pen(Color.FromArgb(40, 80, 180), 1.6f)) g.DrawLine(p, 2, 14, 14, 2);
                    break;

                case "arrow":
                    Arrow(g, new PointF(2, 14), new PointF(13.5f, 2.5f), Color.FromArgb(40, 80, 180), 1.6f);
                    break;

                case "pencil":
                    using (Brush b = Grad(3, 1, 12, 12, Color.FromArgb(255, 210, 90), Color.FromArgb(230, 150, 30)))
                        g.FillPolygon(b, new PointF[] { new PointF(12, 1), new PointF(15, 4), new PointF(5.5f, 13.5f), new PointF(2.5f, 10.5f) });
                    using (Brush b = new SolidBrush(Color.FromArgb(240, 200, 160)))
                        g.FillPolygon(b, new PointF[] { new PointF(2.5f, 10.5f), new PointF(5.5f, 13.5f), new PointF(1, 15) });
                    g.FillPolygon(Brushes.Black, new PointF[] { new PointF(1.8f, 13), new PointF(3, 14.2f), new PointF(1, 15) });
                    using (Brush b = new SolidBrush(Color.FromArgb(230, 120, 140)))
                        g.FillPolygon(b, new PointF[] { new PointF(12, 1), new PointF(15, 4), new PointF(13.6f, 5.4f), new PointF(10.6f, 2.4f) });
                    using (Pen p = new Pen(Color.FromArgb(140, 90, 20), 0.6f))
                        g.DrawPolygon(p, new PointF[] { new PointF(12, 1), new PointF(15, 4), new PointF(5.5f, 13.5f), new PointF(1, 15), new PointF(2.5f, 10.5f) });
                    break;

                case "stamp":
                    using (Brush b = Grad(5, 1, 6, 8, Color.FromArgb(170, 110, 60), Color.FromArgb(110, 66, 30)))
                    {
                        g.FillEllipse(b, 5, 0.8f, 6, 4.5f);
                        g.FillRectangle(b, 6.8f, 4, 2.4f, 5);
                    }
                    using (Brush b = Grad(1.5f, 9, 13, 4, Color.FromArgb(150, 150, 160), Color.FromArgb(80, 80, 90)))
                        g.FillRectangle(b, 1.5f, 9, 13, 3);
                    using (Brush b = new SolidBrush(Color.FromArgb(200, 30, 30))) g.FillRectangle(b, 1.5f, 12.5f, 13, 2.5f);
                    break;

                case "snapshot":
                    using (GraphicsPath p = Round(1, 4, 14, 10, 1.5f))
                    {
                        using (Brush b = Grad(1, 4, 14, 10, Color.FromArgb(120, 126, 140), Color.FromArgb(50, 54, 62)))
                            g.FillPath(b, p);
                    }
                    using (Brush b = new SolidBrush(Color.FromArgb(80, 84, 96))) g.FillRectangle(b, 5, 2.5f, 6, 2);
                    Circle(g, 4.5f, 5.5f, 7, Color.FromArgb(160, 210, 255), Color.FromArgb(30, 70, 140), Color.Black);
                    break;

                case "delete":
                    RedX(g, 3, 3, 10);
                    break;

                case "close":
                    using (Pen p = new Pen(Color.FromArgb(70, 70, 80), 1.5f)) { g.DrawLine(p, 4, 4, 12, 12); g.DrawLine(p, 12, 4, 4, 12); }
                    break;

                case "properties":
                case "info":
                    Circle(g, 1, 1, 14, Color.FromArgb(130, 175, 240), Color.FromArgb(36, 92, 186), Color.FromArgb(24, 66, 140));
                    using (Font f = new Font("Times New Roman", 11f, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
                        g.DrawString("i", f, Brushes.White, new RectangleF(1, 1.5f, 14, 14), sf);
                    }
                    break;

                case "help":
                    Circle(g, 1, 1, 14, Color.FromArgb(130, 175, 240), Color.FromArgb(36, 92, 186), Color.FromArgb(24, 66, 140));
                    using (Font f = new Font("Arial", 10f, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
                        g.DrawString("?", f, Brushes.White, new RectangleF(1, 2, 14, 14), sf);
                    }
                    break;

                case "undo":
                case "redo":
                    using (Pen p = new Pen(Color.FromArgb(40, 90, 180), 1.8f))
                    {
                        p.CustomEndCap = new AdjustableArrowCap(2.2f, 2.2f, true);
                        if (name == "undo") g.DrawArc(p, 3, 4, 11, 9, 60, -240);
                        else g.DrawArc(p, 2, 4, 11, 9, 120, 240);
                    }
                    break;

                case "fullscreen":
                    using (Brush b = Grad(1, 2, 14, 10, Color.FromArgb(90, 96, 110), Color.FromArgb(40, 44, 52)))
                        g.FillRectangle(b, 1, 2, 14, 10);
                    using (Brush b = Grad(2, 3, 12, 8, Color.FromArgb(130, 180, 240), Color.FromArgb(50, 100, 190)))
                        g.FillRectangle(b, 2, 3, 12, 8);
                    g.FillRectangle(Brushes.DimGray, 6, 12, 4, 2);
                    g.FillRectangle(Brushes.DimGray, 4, 14, 8, 1);
                    break;

                case "readMode":
                    Page(g, 1, 2, 7, 11);
                    Page(g, 8, 2, 7, 11);
                    break;

                case "insertPages":
                    Page(g, 4, 1, 11, 14);
                    TextLines(g, 6, 5, 7, 4, 2);
                    Circle(g, 0.5f, 7, 7.5f, Color.FromArgb(120, 210, 90), Color.FromArgb(40, 140, 30), Color.FromArgb(30, 100, 20));
                    Plus(g, 4.25f, 10.75f, 2.2f, Color.White, 1.5f);
                    break;

                case "blankPage":
                    Page(g, 3, 1, 10, 14);
                    Plus(g, 8, 9, 2.5f, Color.FromArgb(40, 140, 40), 1.6f);
                    break;

                case "extractPages":
                    Page(g, 1, 1, 10, 14);
                    TextLines(g, 3, 5, 6, 4, 2);
                    Arrow(g, new PointF(7, 10), new PointF(15, 10), Color.FromArgb(40, 140, 40), 1.8f);
                    break;

                case "deletePages":
                    Page(g, 2, 1, 10, 14);
                    TextLines(g, 4, 5, 6, 3, 2);
                    RedX(g, 8, 8, 6.5f);
                    break;

                case "cropPages":
                    using (Pen p = new Pen(Color.FromArgb(60, 64, 76), 1.6f))
                    {
                        g.DrawLines(p, new PointF[] { new PointF(4, 1), new PointF(4, 12), new PointF(15, 12) });
                        g.DrawLines(p, new PointF[] { new PointF(1, 4), new PointF(12, 4), new PointF(12, 15) });
                    }
                    break;

                case "watermark":
                    Page(g, 2, 1, 12, 14);
                    using (Font f = new Font("Arial", 7f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(130, 200, 40, 40)))
                    {
                        GraphicsState st = g.Save();
                        g.TranslateTransform(8, 9);
                        g.RotateTransform(-45);
                        g.DrawString("WZ", f, b, -5, -4);
                        g.Restore(st);
                    }
                    break;

                case "headerFooter":
                    Page(g, 2, 1, 12, 14);
                    using (Brush b = new SolidBrush(Color.FromArgb(60, 110, 200)))
                    {
                        g.FillRectangle(b, 4, 3, 6, 1.5f);
                        g.FillRectangle(b, 6, 12.3f, 6, 1.5f);
                    }
                    TextLines(g, 4, 6.5f, 8, 2, 2);
                    break;

                case "redact":
                    Page(g, 1.5f, 1, 13, 14);
                    TextLines(g, 3.5f, 4, 9, 2, 2);
                    g.FillRectangle(Brushes.Black, 3, 8, 10, 2.6f);
                    TextLines(g, 3.5f, 12.5f, 9, 1, 2);
                    break;

                case "measure":
                    using (Brush b = Grad(1, 5, 14, 6, Color.FromArgb(255, 230, 140), Color.FromArgb(220, 180, 60)))
                        g.FillRectangle(b, 1, 5, 14, 6);
                    using (Pen p = new Pen(Color.FromArgb(120, 90, 20), 0.8f))
                    {
                        g.DrawRectangle(p, 1, 5, 14, 6);
                        for (int i = 3; i < 15; i += 2) g.DrawLine(p, i, 5, i, i % 4 == 3 ? 8.5f : 7);
                    }
                    break;

                case "singlePage":
                    Page(g, 4, 1, 8, 14);
                    break;

                case "continuous":
                    Page(g, 4, -4, 8, 8);
                    Page(g, 4, 6, 8, 8);
                    break;

                case "twoUp":
                    Page(g, 0.5f, 3, 7, 10);
                    Page(g, 8.5f, 3, 7, 10);
                    break;

                case "expandAll":
                case "collapseAll":
                    g.FillRectangle(Brushes.White, 3, 3, 10, 10);
                    using (Pen p = new Pen(Color.FromArgb(110, 118, 132), 1f)) g.DrawRectangle(p, 3, 3, 10, 10);
                    using (Pen p = new Pen(Color.FromArgb(30, 60, 120), 1.4f))
                    {
                        g.DrawLine(p, 5.5f, 8, 10.5f, 8);
                        if (name == "expandAll") g.DrawLine(p, 8, 5.5f, 8, 10.5f);
                    }
                    break;

                case "check":
                    using (Pen p = new Pen(Color.FromArgb(40, 150, 40), 2.2f))
                    {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                        g.DrawLines(p, new PointF[] { new PointF(2.5f, 8.5f), new PointF(6, 12), new PointF(13.5f, 3.5f) });
                    }
                    break;

                case "copy":
                    Page(g, 1, 1, 9, 11);
                    Page(g, 6, 4, 9, 11);
                    break;

                case "sort":
                    using (Pen p = new Pen(Color.FromArgb(40, 90, 180), 1.4f))
                    {
                        p.CustomEndCap = new AdjustableArrowCap(2, 2, true);
                        g.DrawLine(p, 4, 2, 4, 14);
                    }
                    using (Font f = new Font("Arial", 6f, FontStyle.Bold, GraphicsUnit.Pixel))
                    {
                        g.DrawString("A", f, Brushes.Black, 7, 1);
                        g.DrawString("Z", f, Brushes.Black, 7, 8.5f);
                    }
                    break;

                case "email":
                    using (Brush b = Grad(1, 3, 14, 10, Color.White, Color.FromArgb(220, 226, 236)))
                        g.FillRectangle(b, 1, 3, 14, 10);
                    using (Pen p = new Pen(Color.FromArgb(100, 108, 122), 0.9f))
                    {
                        g.DrawRectangle(p, 1, 3, 14, 10);
                        g.DrawLines(p, new PointF[] { new PointF(1, 3), new PointF(8, 9), new PointF(15, 3) });
                    }
                    break;

                case "multimedia":
                    using (Brush b = Grad(1, 2, 14, 12, Color.FromArgb(90, 96, 110), Color.FromArgb(30, 34, 42)))
                        g.FillRectangle(b, 1, 2, 14, 12);
                    g.FillPolygon(Brushes.White, new PointF[] { new PointF(6, 5), new PointF(11, 8), new PointF(6, 11) });
                    break;

                case "collaborate":
                    Circle(g, 1, 2, 7, Color.FromArgb(130, 175, 240), Color.FromArgb(36, 92, 186), Color.FromArgb(24, 66, 140));
                    Circle(g, 8, 2, 7, Color.FromArgb(120, 210, 90), Color.FromArgb(40, 140, 30), Color.FromArgb(30, 100, 20));
                    using (Brush b = Grad(1, 9, 14, 6, Color.FromArgb(200, 206, 216), Color.FromArgb(120, 128, 142)))
                        g.FillRectangle(b, 1, 10, 14, 5);
                    break;

                default:
                    Page(g, 3, 1, 10, 14);
                    break;
            }
        }

        private static void DrawHand(Graphics g, bool closed)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                if (!closed)
                {
                    // Geöffnete Hand (vier Finger + Daumen)
                    p.AddLine(4.2f, 14.5f, 2.2f, 9.5f);
                    p.AddArc(1.2f, 7.5f, 2.2f, 2.2f, 200, 160);
                    p.AddLine(3.4f, 8.8f, 4.6f, 10.3f);
                    p.AddLine(4.6f, 10.3f, 4.6f, 3.3f);
                    p.AddArc(4.6f, 2.2f, 2f, 2f, 180, 180);
                    p.AddLine(6.6f, 3.2f, 6.6f, 7.5f);
                    p.AddLine(6.6f, 7.5f, 6.6f, 2.0f);
                    p.AddArc(6.6f, 1f, 2f, 2f, 180, 180);
                    p.AddLine(8.6f, 2f, 8.6f, 7.5f);
                    p.AddLine(8.6f, 7.5f, 8.6f, 2.8f);
                    p.AddArc(8.6f, 1.8f, 2f, 2f, 180, 180);
                    p.AddLine(10.6f, 2.8f, 10.6f, 7.8f);
                    p.AddLine(10.6f, 7.8f, 10.6f, 4.6f);
                    p.AddArc(10.6f, 3.6f, 2f, 2f, 180, 180);
                    p.AddLine(12.6f, 4.6f, 12.6f, 10.5f);
                    p.AddLine(12.6f, 10.5f, 11.2f, 14.5f);
                }
                else
                {
                    p.AddLine(4.2f, 14.5f, 3, 9.5f);
                    p.AddLine(3, 9.5f, 4.6f, 7);
                    p.AddArc(4.6f, 5.6f, 2f, 2f, 180, 180);
                    p.AddArc(6.6f, 5f, 2f, 2f, 180, 180);
                    p.AddArc(8.6f, 5.3f, 2f, 2f, 180, 180);
                    p.AddArc(10.6f, 6f, 2f, 2f, 180, 180);
                    p.AddLine(12.6f, 7f, 12.6f, 10.5f);
                    p.AddLine(12.6f, 10.5f, 11.2f, 14.5f);
                }
                p.CloseFigure();
                g.FillPath(Brushes.White, p);
                using (Pen pen = new Pen(Color.Black, 0.9f)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, p); }
            }
        }

        // ------------------------------------------------------------------
        // Mauszeiger
        // ------------------------------------------------------------------

        private static Cursor handOpen, handClosed, zoomIn, zoomOut;

        public static Cursor HandOpenCursor { get { return handOpen ?? (handOpen = MakeCursor("handOpen")); } }
        public static Cursor HandClosedCursor { get { return handClosed ?? (handClosed = MakeCursor("handClosed")); } }
        public static Cursor ZoomInCursor { get { return zoomIn ?? (zoomIn = MakeCursor("zoomInCursor")); } }
        public static Cursor ZoomOutCursor { get { return zoomOut ?? (zoomOut = MakeCursor("zoomOutCursor")); } }

        private static Cursor MakeCursor(string kind)
        {
            if (!AcroClone.Pdf.Native.IsWindows) return kind.StartsWith("hand") ? Cursors.Hand : Cursors.Cross;
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TranslateTransform(8, 8);
                        if (kind == "handOpen") DrawHand(g, false);
                        else if (kind == "handClosed") DrawHand(g, true);
                        else
                        {
                            g.FillEllipse(Brushes.White, 0, 0, 11, 11);
                            using (Pen p = new Pen(Color.Black, 1.2f)) g.DrawEllipse(p, 0, 0, 11, 11);
                            using (Pen p = new Pen(Color.Black, 2.2f)) g.DrawLine(p, 10, 10, 15, 15);
                            using (Pen p = new Pen(Color.Black, 1.4f))
                            {
                                g.DrawLine(p, 3, 5.5f, 8, 5.5f);
                                if (kind == "zoomInCursor") g.DrawLine(p, 5.5f, 3, 5.5f, 8);
                            }
                        }
                    }
                    IntPtr h = bmp.GetHicon();
                    Cursor c = new Cursor(h);
                    return c;
                }
            }
            catch (Exception)
            {
                return kind.StartsWith("hand") ? Cursors.Hand : Cursors.Cross;
            }
        }
    }
}
