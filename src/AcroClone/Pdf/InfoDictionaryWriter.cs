using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace AcroClone.Pdf
{
    /// <summary>
    /// Schreibt ein neues Dokumentinfo-Wörterbuch (/Info) als inkrementelle Aktualisierung
    /// an eine bestehende PDF-Datei. PDFium bietet dafür keine eigene API.
    /// </summary>
    public static class InfoDictionaryWriter
    {
        public static readonly string[] Keys = { "Title", "Author", "Subject", "Keywords", "Creator", "Producer", "CreationDate", "ModDate" };

        public static byte[] Apply(byte[] pdf, IDictionary<string, string> info)
        {
            try
            {
                return ApplyCore(pdf, info);
            }
            catch (Exception)
            {
                // Im Zweifel unveränderte Datei zurückgeben – das Speichern darf nicht scheitern.
                return pdf;
            }
        }

        private static byte[] ApplyCore(byte[] pdf, IDictionary<string, string> info)
        {
            Encoding latin = Encoding.GetEncoding(28591);
            int tailLen = Math.Min(pdf.Length, 2048);
            string tail = latin.GetString(pdf, pdf.Length - tailLen, tailLen);
            MatchCollection sx = Regex.Matches(tail, @"startxref\s+(\d+)");
            if (sx.Count == 0) return pdf;
            long prevXref = long.Parse(sx[sx.Count - 1].Groups[1].Value, CultureInfo.InvariantCulture);
            if (prevXref <= 0 || prevXref >= pdf.Length) return pdf;

            // Trailer bzw. XRef-Stream-Wörterbuch lesen
            int scanLen = (int)Math.Min(pdf.Length - prevXref, 1 << 20);
            string section = latin.GetString(pdf, (int)prevXref, scanLen);
            string dict;
            if (section.StartsWith("xref"))
            {
                int t = section.IndexOf("trailer", StringComparison.Ordinal);
                if (t < 0) return pdf;
                dict = section.Substring(t);
            }
            else
            {
                int s = section.IndexOf("stream", StringComparison.Ordinal);
                dict = s > 0 ? section.Substring(0, s) : section;
            }

            Match size = Regex.Match(dict, @"/Size\s+(\d+)");
            Match root = Regex.Match(dict, @"/Root\s+(\d+)\s+(\d+)\s+R");
            if (!size.Success || !root.Success) return pdf;
            int objNum = int.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture);
            Match id = Regex.Match(dict, @"/ID\s*\[\s*(<[0-9A-Fa-f]*>)\s*(<[0-9A-Fa-f]*>)\s*\]");

            MemoryStream ms = new MemoryStream();
            ms.Write(pdf, 0, pdf.Length);
            StringBuilder sb = new StringBuilder();
            if (pdf.Length > 0 && pdf[pdf.Length - 1] != '\n') sb.Append('\n');
            long objOffset = ms.Length + sb.Length;
            sb.Append(objNum).Append(" 0 obj\n<<");
            foreach (string key in Keys)
            {
                string v;
                if (!info.TryGetValue(key, out v) || string.IsNullOrEmpty(v)) continue;
                if (key == "CreationDate" || key == "ModDate")
                    sb.Append(" /").Append(key).Append(" (").Append(v).Append(')');
                else
                    sb.Append(" /").Append(key).Append(' ').Append(EncodeText(v));
            }
            sb.Append(" >>\nendobj\n");
            long xrefOffset = ms.Length + sb.Length;
            sb.Append("xref\n0 1\n0000000000 65535 f \n");
            sb.Append(objNum).Append(" 1\n");
            sb.Append(objOffset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size ").Append(objNum + 1);
            sb.Append(" /Root ").Append(root.Groups[1].Value).Append(' ').Append(root.Groups[2].Value).Append(" R");
            sb.Append(" /Info ").Append(objNum).Append(" 0 R");
            sb.Append(" /Prev ").Append(prevXref.ToString(CultureInfo.InvariantCulture));
            if (id.Success) sb.Append(" /ID [").Append(id.Groups[1].Value).Append(' ').Append(id.Groups[2].Value).Append(']');
            sb.Append(" >>\nstartxref\n").Append(xrefOffset.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF\n");

            // Verschlüsselte Dokumente nicht anfassen (Strings müssten verschlüsselt werden)
            if (Regex.IsMatch(dict, @"/Encrypt\s")) return pdf;

            byte[] extra = latin.GetBytes(sb.ToString());
            ms.Write(extra, 0, extra.Length);
            return ms.ToArray();
        }

        /// <summary>Kodiert einen Textstring als UTF-16BE-Hexstring mit BOM.</summary>
        public static string EncodeText(string s)
        {
            StringBuilder sb = new StringBuilder("<FEFF");
            foreach (char c in s) sb.Append(((int)c).ToString("X4"));
            sb.Append('>');
            return sb.ToString();
        }

        public static string FormatDate(DateTime t)
        {
            TimeSpan off = TimeZoneInfo.Local.GetUtcOffset(t);
            string sign = off < TimeSpan.Zero ? "-" : "+";
            off = off.Duration();
            return "D:" + t.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + sign +
                off.Hours.ToString("00") + "'" + off.Minutes.ToString("00") + "'";
        }

        public static DateTime? ParseDate(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            if (s.StartsWith("D:")) s = s.Substring(2);
            string digits = string.Empty;
            foreach (char c in s)
            {
                if (char.IsDigit(c)) digits += c; else break;
            }
            if (digits.Length < 4) return null;
            digits = digits.PadRight(14, '0');
            if (digits.Substring(4, 2) == "00") digits = digits.Substring(0, 4) + "01" + digits.Substring(6);
            if (digits.Substring(6, 2) == "00") digits = digits.Substring(0, 6) + "01" + digits.Substring(8);
            DateTime dt;
            if (DateTime.TryParseExact(digits.Substring(0, 14), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return dt;
            return null;
        }
    }
}
