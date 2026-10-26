using System;
using System.Drawing;
using System.Globalization;

namespace AcroClone.Pdf
{
    /// <summary>Rechteck im PDF-Koordinatensystem (Ursprung unten links, Einheit Punkt).</summary>
    public struct PdfRect
    {
        public double Left, Bottom, Right, Top;

        public PdfRect(double left, double bottom, double right, double top)
        {
            Left = Math.Min(left, right);
            Right = Math.Max(left, right);
            Bottom = Math.Min(bottom, top);
            Top = Math.Max(bottom, top);
        }

        public double Width { get { return Right - Left; } }
        public double Height { get { return Top - Bottom; } }
        public bool IsEmpty { get { return Width <= 0 || Height <= 0; } }

        public bool Contains(double x, double y)
        {
            return x >= Left && x <= Right && y >= Bottom && y <= Top;
        }

        public PdfRect Inflate(double d)
        {
            return new PdfRect(Left - d, Bottom - d, Right + d, Top + d);
        }

        public PdfRect Offset(double dx, double dy)
        {
            return new PdfRect(Left + dx, Bottom + dy, Right + dx, Top + dy);
        }

        public PdfRect Union(PdfRect other)
        {
            return new PdfRect(Math.Min(Left, other.Left), Math.Min(Bottom, other.Bottom),
                Math.Max(Right, other.Right), Math.Max(Top, other.Top));
        }

        public bool IntersectsWith(PdfRect o)
        {
            return o.Left <= Right && o.Right >= Left && o.Bottom <= Top && o.Top >= Bottom;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##},{2:0.##},{3:0.##}", Left, Bottom, Right, Top);
        }

        public static PdfRect Parse(string s)
        {
            string[] p = s.Split(',');
            return new PdfRect(
                double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture),
                double.Parse(p[2], CultureInfo.InvariantCulture), double.Parse(p[3], CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Punkt im PDF-Koordinatensystem.</summary>
    public struct PdfPoint
    {
        public double X, Y;
        public PdfPoint(double x, double y) { X = x; Y = y; }
    }

    /// <summary>Affine Transformation [A B C D E F] (wie in PDF): x' = A*x + C*y + E, y' = B*x + D*y + F.</summary>
    public struct Affine
    {
        public double A, B, C, D, E, F;

        public Affine(double a, double b, double c, double d, double e, double f)
        {
            A = a; B = b; C = c; D = d; E = e; F = f;
        }

        public static Affine Identity { get { return new Affine(1, 0, 0, 1, 0, 0); } }

        public PointF Apply(double x, double y)
        {
            return new PointF((float)(A * x + C * y + E), (float)(B * x + D * y + F));
        }

        public PdfPoint ApplyD(double x, double y)
        {
            return new PdfPoint(A * x + C * y + E, B * x + D * y + F);
        }

        /// <summary>Wendet nur den linearen Anteil an (für Richtungsvektoren).</summary>
        public PdfPoint ApplyVector(double x, double y)
        {
            return new PdfPoint(A * x + C * y, B * x + D * y);
        }

        public Affine Invert()
        {
            double det = A * D - B * C;
            if (Math.Abs(det) < 1e-12) return Identity;
            double ia = D / det, ib = -B / det, ic = -C / det, id = A / det;
            double ie = -(ia * E + ic * F);
            double iF = -(ib * E + id * F);
            return new Affine(ia, ib, ic, id, ie, iF);
        }

        /// <summary>Ergebnis: zuerst this, danach other.</summary>
        public Affine Then(Affine o)
        {
            return new Affine(
                A * o.A + B * o.C, A * o.B + B * o.D,
                C * o.A + D * o.C, C * o.B + D * o.D,
                E * o.A + F * o.C + o.E, E * o.B + F * o.D + o.F);
        }

        public Affine Scale(double s)
        {
            return new Affine(A * s, B * s, C * s, D * s, E * s, F * s);
        }

        public Affine Translate(double dx, double dy)
        {
            return new Affine(A, B, C, D, E + dx, F + dy);
        }

        /// <summary>Gerundeter Drehwinkel (0, 90, 180, 270) dieser Transformation (im Uhrzeigersinn auf dem Bildschirm).</summary>
        public double LinearScale
        {
            get { return Math.Sqrt(Math.Abs(A * D - B * C)); }
        }

        public RectangleF MapRect(PdfRect r)
        {
            PointF p1 = Apply(r.Left, r.Bottom);
            PointF p2 = Apply(r.Right, r.Top);
            PointF p3 = Apply(r.Left, r.Top);
            PointF p4 = Apply(r.Right, r.Bottom);
            float l = Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X));
            float t = Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y));
            float rr = Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X));
            float b = Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y));
            return RectangleF.FromLTRB(l, t, rr, b);
        }

        public System.Drawing.Drawing2D.Matrix ToMatrix()
        {
            return new System.Drawing.Drawing2D.Matrix((float)A, (float)B, (float)C, (float)D, (float)E, (float)F);
        }
    }
}
