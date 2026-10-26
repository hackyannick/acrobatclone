using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using AcroClone.Pdf;

namespace AcroClone.Model
{
    public enum AnnotationType
    {
        Note,
        Highlight,
        Underline,
        StrikeOut,
        FreeText,
        Rectangle,
        Ellipse,
        Line,
        Arrow,
        Ink,
        Stamp,
        Signature,
        Redaction
    }

    /// <summary>
    /// Ein Kommentar bzw. eine Markierung. Alle Koordinaten liegen im PDF-Seitenraum (Punkt).
    /// </summary>
    public class Annotation
    {
        public string Id = Guid.NewGuid().ToString("N");
        public AnnotationType Type;
        public int Page;
        /// <summary>Drehung (/Rotate der Seite bei Erstellung, 0..3), damit Text aufrecht erscheint.</summary>
        public int Rotation;
        public PdfRect Rect;
        public List<PdfRect> Quads = new List<PdfRect>();
        public List<List<PdfPoint>> Strokes = new List<List<PdfPoint>>();
        public Color Color = Color.Red;
        public Color FillColor = Color.Empty;
        public float Opacity = 1f;
        public float LineWidth = 1f;
        public float FontSize = 12f;
        public string Text = string.Empty;
        public string Contents = string.Empty;
        public string Author = string.Empty;
        public DateTime Created = DateTime.Now;
        public DateTime Modified = DateTime.Now;
        public bool Checked;

        public string TypeName
        {
            get { return GetTypeName(Type); }
        }

        public static string GetTypeName(AnnotationType t)
        {
            switch (t)
            {
                case AnnotationType.Note: return "Notiz";
                case AnnotationType.Highlight: return "Hervorheben";
                case AnnotationType.Underline: return "Unterstreichen";
                case AnnotationType.StrikeOut: return "Durchstreichen";
                case AnnotationType.FreeText: return "Textfeld";
                case AnnotationType.Rectangle: return "Rechteck";
                case AnnotationType.Ellipse: return "Ellipse";
                case AnnotationType.Line: return "Linie";
                case AnnotationType.Arrow: return "Pfeil";
                case AnnotationType.Ink: return "Bleistift";
                case AnnotationType.Stamp: return "Stempel";
                case AnnotationType.Signature: return "Unterschrift";
                case AnnotationType.Redaction: return "Schwärzung";
            }
            return t.ToString();
        }

        public bool IsTextMarkup
        {
            get { return Type == AnnotationType.Highlight || Type == AnnotationType.Underline || Type == AnnotationType.StrikeOut; }
        }

        public bool UsesStrokes
        {
            get
            {
                return Type == AnnotationType.Ink || Type == AnnotationType.Line || Type == AnnotationType.Arrow ||
                    (Type == AnnotationType.Signature && Strokes.Count > 0);
            }
        }

        /// <summary>Rechteckbasierte Typen, die über Anfasser in der Größe geändert werden können.</summary>
        public bool IsResizable
        {
            get
            {
                return Type == AnnotationType.FreeText || Type == AnnotationType.Rectangle || Type == AnnotationType.Ellipse ||
                    Type == AnnotationType.Stamp || Type == AnnotationType.Redaction ||
                    (Type == AnnotationType.Signature && Strokes.Count == 0);
            }
        }

        public PdfRect Bounds
        {
            get
            {
                if (IsTextMarkup && Quads.Count > 0)
                {
                    PdfRect r = Quads[0];
                    for (int i = 1; i < Quads.Count; i++) r = r.Union(Quads[i]);
                    return r;
                }
                if (UsesStrokes)
                {
                    double l = double.MaxValue, b = double.MaxValue, rr = double.MinValue, t = double.MinValue;
                    foreach (List<PdfPoint> s in Strokes)
                        foreach (PdfPoint p in s)
                        {
                            l = Math.Min(l, p.X); rr = Math.Max(rr, p.X);
                            b = Math.Min(b, p.Y); t = Math.Max(t, p.Y);
                        }
                    if (l == double.MaxValue) return Rect;
                    double w = LineWidth / 2 + (Type == AnnotationType.Arrow ? Math.Max(6, LineWidth * 4) : 0);
                    return new PdfRect(l - w, b - w, rr + w, t + w);
                }
                return Rect;
            }
        }

        public void Offset(double dx, double dy)
        {
            Rect = Rect.Offset(dx, dy);
            for (int i = 0; i < Quads.Count; i++) Quads[i] = Quads[i].Offset(dx, dy);
            foreach (List<PdfPoint> s in Strokes)
                for (int i = 0; i < s.Count; i++) s[i] = new PdfPoint(s[i].X + dx, s[i].Y + dy);
        }

        /// <summary>Skaliert die Geometrie von einem Rechteck auf ein anderes (für Größenänderung).</summary>
        public void MapRect(PdfRect from, PdfRect to)
        {
            double sx = from.Width > 0 ? to.Width / from.Width : 1;
            double sy = from.Height > 0 ? to.Height / from.Height : 1;
            Rect = to;
            foreach (List<PdfPoint> s in Strokes)
                for (int i = 0; i < s.Count; i++)
                    s[i] = new PdfPoint(to.Left + (s[i].X - from.Left) * sx, to.Bottom + (s[i].Y - from.Bottom) * sy);
        }

        // ------------------------------------------------------------------
        // Lokales Koordinatensystem (oben links, x nach rechts, y nach unten, Einheit Punkt)
        // ------------------------------------------------------------------

        public SizeF LocalSize
        {
            get
            {
                return (Rotation & 1) == 1 ? new SizeF((float)Rect.Height, (float)Rect.Width) : new SizeF((float)Rect.Width, (float)Rect.Height);
            }
        }

        /// <summary>Abbildung lokales System -> Seitenraum.</summary>
        public Affine LocalToPage
        {
            get
            {
                switch (Rotation & 3)
                {
                    case 1: return new Affine(0, 1, 1, 0, Rect.Left, Rect.Bottom);
                    case 2: return new Affine(-1, 0, 0, 1, Rect.Right, Rect.Bottom);
                    case 3: return new Affine(0, -1, -1, 0, Rect.Right, Rect.Top);
                    default: return new Affine(1, 0, 0, -1, Rect.Left, Rect.Top);
                }
            }
        }

        // ------------------------------------------------------------------
        // Trefferprüfung
        // ------------------------------------------------------------------

        public bool HitTest(double x, double y, double tolerance)
        {
            switch (Type)
            {
                case AnnotationType.Highlight:
                case AnnotationType.Underline:
                case AnnotationType.StrikeOut:
                    foreach (PdfRect q in Quads)
                        if (q.Inflate(tolerance).Contains(x, y)) return true;
                    return false;
                case AnnotationType.Rectangle:
                    if (FillColor.A > 0) return Rect.Inflate(tolerance).Contains(x, y);
                    {
                        PdfRect outer = Rect.Inflate(tolerance + LineWidth / 2);
                        PdfRect inner = Rect.Inflate(-(tolerance + LineWidth / 2));
                        return outer.Contains(x, y) && (inner.IsEmpty || !inner.Contains(x, y));
                    }
                case AnnotationType.Ellipse:
                    {
                        double cx = (Rect.Left + Rect.Right) / 2, cy = (Rect.Bottom + Rect.Top) / 2;
                        double rx = Math.Max(1, Rect.Width / 2), ry = Math.Max(1, Rect.Height / 2);
                        double nx = (x - cx) / rx, ny = (y - cy) / ry;
                        double d = Math.Sqrt(nx * nx + ny * ny);
                        if (FillColor.A > 0) return d <= 1 + tolerance / Math.Min(rx, ry);
                        double tol = (tolerance + LineWidth / 2) / Math.Min(rx, ry);
                        return Math.Abs(d - 1) <= tol;
                    }
                case AnnotationType.Line:
                case AnnotationType.Arrow:
                case AnnotationType.Ink:
                    foreach (List<PdfPoint> s in Strokes)
                    {
                        if (s.Count == 1 && Dist(s[0], x, y) <= tolerance + LineWidth) return true;
                        for (int i = 1; i < s.Count; i++)
                            if (SegmentDistance(s[i - 1], s[i], x, y) <= tolerance + LineWidth / 2) return true;
                    }
                    return false;
                default:
                    return Bounds.Inflate(tolerance).Contains(x, y);
            }
        }

        private static double Dist(PdfPoint a, double x, double y)
        {
            double dx = a.X - x, dy = a.Y - y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double SegmentDistance(PdfPoint a, PdfPoint b, double x, double y)
        {
            double vx = b.X - a.X, vy = b.Y - a.Y;
            double len2 = vx * vx + vy * vy;
            if (len2 < 1e-9) return Dist(a, x, y);
            double t = ((x - a.X) * vx + (y - a.Y) * vy) / len2;
            t = Math.Max(0, Math.Min(1, t));
            return Dist(new PdfPoint(a.X + t * vx, a.Y + t * vy), x, y);
        }

        // ------------------------------------------------------------------
        // Serialisierung (wird im PDF unter /AcroCloneData abgelegt)
        // ------------------------------------------------------------------

        public Annotation Clone()
        {
            return Deserialize(Serialize());
        }

        public string Serialize()
        {
            StringBuilder sb = new StringBuilder();
            Put(sb, "v", "1");
            Put(sb, "id", Id);
            Put(sb, "type", Type.ToString());
            Put(sb, "page", Page.ToString(CultureInfo.InvariantCulture));
            Put(sb, "rot", Rotation.ToString(CultureInfo.InvariantCulture));
            Put(sb, "rect", Rect.ToString());
            if (Quads.Count > 0)
            {
                List<string> q = new List<string>();
                foreach (PdfRect r in Quads) q.Add(r.ToString());
                Put(sb, "quads", string.Join(";", q.ToArray()));
            }
            if (Strokes.Count > 0)
            {
                List<string> strokes = new List<string>();
                foreach (List<PdfPoint> s in Strokes)
                {
                    List<string> pts = new List<string>();
                    foreach (PdfPoint p in s)
                        pts.Add(p.X.ToString("0.##", CultureInfo.InvariantCulture) + " " + p.Y.ToString("0.##", CultureInfo.InvariantCulture));
                    strokes.Add(string.Join(";", pts.ToArray()));
                }
                Put(sb, "strokes", string.Join("|", strokes.ToArray()));
            }
            Put(sb, "color", ColorToString(Color));
            Put(sb, "fill", ColorToString(FillColor));
            Put(sb, "opacity", Opacity.ToString("0.###", CultureInfo.InvariantCulture));
            Put(sb, "width", LineWidth.ToString("0.###", CultureInfo.InvariantCulture));
            Put(sb, "fontsize", FontSize.ToString("0.###", CultureInfo.InvariantCulture));
            Put(sb, "text", Text);
            Put(sb, "contents", Contents);
            Put(sb, "author", Author);
            Put(sb, "created", Created.ToString("o", CultureInfo.InvariantCulture));
            Put(sb, "modified", Modified.ToString("o", CultureInfo.InvariantCulture));
            if (Checked) Put(sb, "checked", "1");
            return sb.ToString();
        }

        public static Annotation Deserialize(string data)
        {
            Annotation a = new Annotation();
            foreach (string line in data.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq);
                string value = Unescape(line.Substring(eq + 1));
                try
                {
                    switch (key)
                    {
                        case "id": a.Id = value; break;
                        case "type": a.Type = (AnnotationType)Enum.Parse(typeof(AnnotationType), value); break;
                        case "page": a.Page = int.Parse(value, CultureInfo.InvariantCulture); break;
                        case "rot": a.Rotation = int.Parse(value, CultureInfo.InvariantCulture); break;
                        case "rect": a.Rect = PdfRect.Parse(value); break;
                        case "quads":
                            foreach (string q in value.Split(';'))
                                if (q.Length > 0) a.Quads.Add(PdfRect.Parse(q));
                            break;
                        case "strokes":
                            foreach (string s in value.Split('|'))
                            {
                                List<PdfPoint> pts = new List<PdfPoint>();
                                foreach (string p in s.Split(';'))
                                {
                                    string[] xy = p.Split(' ');
                                    if (xy.Length != 2) continue;
                                    pts.Add(new PdfPoint(double.Parse(xy[0], CultureInfo.InvariantCulture), double.Parse(xy[1], CultureInfo.InvariantCulture)));
                                }
                                if (pts.Count > 0) a.Strokes.Add(pts);
                            }
                            break;
                        case "color": a.Color = ParseColor(value); break;
                        case "fill": a.FillColor = ParseColor(value); break;
                        case "opacity": a.Opacity = float.Parse(value, CultureInfo.InvariantCulture); break;
                        case "width": a.LineWidth = float.Parse(value, CultureInfo.InvariantCulture); break;
                        case "fontsize": a.FontSize = float.Parse(value, CultureInfo.InvariantCulture); break;
                        case "text": a.Text = value; break;
                        case "contents": a.Contents = value; break;
                        case "author": a.Author = value; break;
                        case "created": a.Created = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); break;
                        case "modified": a.Modified = DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); break;
                        case "checked": a.Checked = value == "1"; break;
                    }
                }
                catch (Exception)
                {
                    // Einzelne fehlerhafte Felder ignorieren
                }
            }
            return a;
        }

        private static void Put(StringBuilder sb, string key, string value)
        {
            sb.Append(key).Append('=').Append(Escape(value ?? string.Empty)).Append('\n');
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n");
        }

        private static string Unescape(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 'n' ? '\n' : n);
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        public static string ColorToString(Color c)
        {
            if (c.IsEmpty || c.A == 0) return "none";
            return "#" + c.A.ToString("X2") + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        public static Color ParseColor(string s)
        {
            if (string.IsNullOrEmpty(s) || s == "none") return Color.Empty;
            s = s.TrimStart('#');
            int v = int.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (s.Length <= 6) v |= unchecked((int)0xFF000000);
            return Color.FromArgb(v);
        }
    }
}
