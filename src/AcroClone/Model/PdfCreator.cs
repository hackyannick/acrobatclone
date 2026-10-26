using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using AcroClone.Pdf;

namespace AcroClone.Model
{
    /// <summary>Erstellt neue PDF-Dokumente (leer, aus Bildern, aus Text, Beispieldokument).</summary>
    public static class PdfCreator
    {
        public const double A4Width = 595.28;
        public const double A4Height = 841.89;

        public static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };
        public static readonly string[] TextExtensions = { ".txt", ".log", ".csv", ".ini", ".xml", ".cs", ".md" };

        public static bool IsImage(string path)
        {
            return Array.IndexOf(ImageExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;
        }

        public static bool IsText(string path)
        {
            return Array.IndexOf(TextExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;
        }

        public static bool IsPdf(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() == ".pdf";
        }

        public static PdfDocument CreateBlank()
        {
            PdfDocument doc = PdfDocument.CreateNew();
            doc.InsertBlankPage(0, A4Width, A4Height);
            return doc;
        }

        /// <summary>Hängt eine Datei (PDF, Bild oder Text) an ein Dokument an.</summary>
        public static void AppendFile(PdfDocument doc, string path)
        {
            if (IsPdf(path))
            {
                using (PdfDocument src = PdfDocument.Load(File.ReadAllBytes(path), null))
                    doc.ImportPages(src, null, doc.PageCount);
            }
            else if (IsImage(path))
            {
                using (Image img = Image.FromFile(path))
                {
                    int frames = 1;
                    try { frames = img.GetFrameCount(System.Drawing.Imaging.FrameDimension.Page); }
                    catch (Exception) { frames = 1; }
                    for (int f = 0; f < frames; f++)
                    {
                        if (frames > 1) img.SelectActiveFrame(System.Drawing.Imaging.FrameDimension.Page, f);
                        AddImagePage(doc, img, doc.PageCount);
                    }
                }
            }
            else
            {
                AddTextPages(doc, File.ReadAllText(path), Path.GetFileName(path));
            }
        }

        public static void AddImagePage(PdfDocument doc, Image img, int index)
        {
            double dpiX = img.HorizontalResolution > 1 ? img.HorizontalResolution : 96;
            double dpiY = img.VerticalResolution > 1 ? img.VerticalResolution : 96;
            double w = img.Width * 72.0 / dpiX, h = img.Height * 72.0 / dpiY;
            // Sehr große oder sehr kleine Bilder auf sinnvolle Seitengrößen bringen
            double max = 14400, min = 72;
            double f = 1;
            if (Math.Max(w, h) > max) f = max / Math.Max(w, h);
            if (Math.Max(w, h) * f < min) f = min / Math.Max(w, h);
            w *= f; h *= f;
            byte[] jpeg = PdfObjects.ToJpeg(img, 92);
            doc.InsertBlankPage(index, w, h);
            IntPtr page = doc.GetPage(index);
            IntPtr obj = PdfObjects.JpegImage(doc, page, jpeg);
            if (obj == IntPtr.Zero) return;
            Native.FPDFImageObj_SetMatrix(obj, w, 0, 0, h, 0, 0);
            Native.FPDFPage_InsertObject(page, obj);
            doc.CommitPageContent(index);
            doc.ClosePages();
        }

        public static void AddTextPages(PdfDocument doc, string text, string title)
        {
            const float size = 10.5f;
            const double margin = 64;
            double width = A4Width - 2 * margin;
            List<string> lines = PdfObjects.Wrap(text.Replace("\t", "    "), PdfObjects.Helvetica, size, width);
            int perPage = (int)((A4Height - 2 * margin - 24) / (size * 1.35));
            int index = 0;
            int pageNo = 0;
            do
            {
                int pi = doc.PageCount;
                doc.InsertBlankPage(pi, A4Width, A4Height);
                IntPtr page = doc.GetPage(pi);
                pageNo++;
                if (!string.IsNullOrEmpty(title))
                {
                    IntPtr head = PdfObjects.Text(doc, PdfObjects.HelveticaOblique, 8, title + " – Seite " + pageNo, Color.Gray);
                    PdfObjects.Transform(head, new Affine(1, 0, 0, 1, margin, A4Height - margin + 18));
                    Native.FPDFPage_InsertObject(page, head);
                }
                double y = A4Height - margin;
                for (int n = 0; n < perPage && index < lines.Count; n++, index++)
                {
                    y -= size * 1.35;
                    if (lines[index].Length == 0) continue;
                    IntPtr t = PdfObjects.Text(doc, PdfObjects.Helvetica, size, lines[index], Color.Black);
                    PdfObjects.Transform(t, new Affine(1, 0, 0, 1, margin, y));
                    Native.FPDFPage_InsertObject(page, t);
                }
                doc.CommitPageContent(pi);
                doc.ClosePages();
            } while (index < lines.Count);
        }

        // ------------------------------------------------------------------
        // Beispieldokument
        // ------------------------------------------------------------------

        public static PdfDocument CreateSample()
        {
            PdfDocument doc = PdfDocument.CreateNew();
            Color red = Color.FromArgb(178, 20, 20);
            Color dark = Color.FromArgb(40, 40, 40);
            Color grey = Color.FromArgb(110, 110, 110);
            double m = 60;

            // Seite 1 -----------------------------------------------------
            doc.InsertBlankPage(0, A4Width, A4Height);
            IntPtr p = doc.GetPage(0);
            Native.FPDFPage_InsertObject(p, PdfObjects.Rect(new PdfRect(0, A4Height - 120, A4Width, A4Height), Color.Empty, 0, Color.FromArgb(60, 60, 60)));
            Native.FPDFPage_InsertObject(p, PdfObjects.Rect(new PdfRect(0, A4Height - 126, A4Width, A4Height - 120), Color.Empty, 0, red));
            Native.FPDFPage_InsertObject(p, PdfObjects.RoundRect(new PdfRect(m, A4Height - 100, m + 60, A4Height - 40), 10, Color.Empty, 0, red));
            IntPtr logo = PdfObjects.Text(doc, PdfObjects.HelveticaBold, 34, "A", Color.White);
            PdfObjects.Transform(logo, new Affine(1, 0, 0, 1, m + 18, A4Height - 83));
            Native.FPDFPage_InsertObject(p, logo);
            IntPtr title = PdfObjects.Text(doc, PdfObjects.HelveticaBold, 26, "Willkommen bei AcroClone 9 Pro", Color.White);
            PdfObjects.Transform(title, new Affine(1, 0, 0, 1, m + 76, A4Height - 70));
            Native.FPDFPage_InsertObject(p, title);
            IntPtr sub = PdfObjects.Text(doc, PdfObjects.Helvetica, 12, "Ein PDF-Programm im Stil der Acrobat-9-Ära", Color.FromArgb(220, 220, 220));
            PdfObjects.Transform(sub, new Affine(1, 0, 0, 1, m + 76, A4Height - 92));
            Native.FPDFPage_InsertObject(p, sub);

            double y = A4Height - 170;
            y = Heading(doc, p, "Was Sie mit diesem Programm tun können", m, y, red);
            y = PdfObjects.AddTextBlock(doc, p,
                "Dieses Beispieldokument wurde direkt von AcroClone erzeugt. Probieren Sie die Werkzeuge an diesem Text aus: " +
                "Markieren Sie Text mit dem Auswahlwerkzeug, heben Sie Passagen hervor, fügen Sie Notizen hinzu oder " +
                "zeichnen Sie Formen und Freihandlinien auf die Seite.",
                PdfObjects.Helvetica, 11, dark, m, y - 4, A4Width - 2 * m, 1.45);
            y -= 10;
            string[] features =
            {
                "PDF-Dateien anzeigen: Einzelseite, fortlaufend, Zweiseitenansicht, Vollbildmodus",
                "Zoom: Ganze Seite, Fensterbreite, tatsächliche Größe, Auswahlrechteck-Zoom",
                "Kommentare: Notizen, Hervorheben, Unterstreichen, Durchstreichen, Textfelder, Stempel",
                "Zeichnen: Rechteck, Ellipse, Linie, Pfeil und Bleistift",
                "Seiten einfügen, extrahieren, löschen, drehen, beschneiden und neu anordnen",
                "Dateien zusammenführen, PDF aus Bildern und Text erstellen",
                "Wasserzeichen, Kopf- und Fußzeilen, Schwärzen und Unterschreiben",
                "Suchen, Lesezeichen, Anlagen, Formulare ausfüllen und Drucken"
            };
            foreach (string f in features)
            {
                Native.FPDFPage_InsertObject(p, PdfObjects.Rect(new PdfRect(m + 4, y - 9, m + 9, y - 4), Color.Empty, 0, red));
                y = PdfObjects.AddTextBlock(doc, p, f, PdfObjects.Helvetica, 11, dark, m + 18, y + 2, A4Width - 2 * m - 18, 1.5) - 2;
            }
            y -= 16;
            y = Heading(doc, p, "Tastenkombinationen", m, y, red);
            string[,] keys =
            {
                { "Strg+O", "Datei öffnen" }, { "Strg+S", "Speichern" }, { "Strg+P", "Drucken" },
                { "Strg+F", "Suchen" }, { "Strg+L", "Vollbildmodus" }, { "Strg+H", "Lesemodus" },
                { "Strg+0 / Strg+1 / Strg+2", "Ganze Seite / 100 % / Fensterbreite" },
                { "Strg+Umschalt+Plus", "Ansicht im Uhrzeigersinn drehen" }, { "H / V / Z", "Hand / Auswahl / Zoom" }
            };
            for (int i = 0; i < keys.GetLength(0); i++)
            {
                y -= 17;
                IntPtr k = PdfObjects.Text(doc, PdfObjects.HelveticaBold, 10.5f, keys[i, 0], dark);
                PdfObjects.Transform(k, new Affine(1, 0, 0, 1, m + 4, y));
                Native.FPDFPage_InsertObject(p, k);
                IntPtr d = PdfObjects.Text(doc, PdfObjects.Helvetica, 10.5f, keys[i, 1], dark);
                PdfObjects.Transform(d, new Affine(1, 0, 0, 1, m + 190, y));
                Native.FPDFPage_InsertObject(p, d);
                Native.FPDFPage_InsertObject(p, PdfObjects.Rect(new PdfRect(m, y - 5, A4Width - m, y - 4.6), Color.Empty, 0, Color.FromArgb(220, 220, 220)));
            }
            Footer(doc, p, 1, grey);
            doc.CommitPageContent(0);

            // Seite 2 -----------------------------------------------------
            doc.InsertBlankPage(1, A4Width, A4Height);
            p = doc.GetPage(1);
            y = A4Height - 70;
            y = Heading(doc, p, "Übungstext zum Kommentieren", m, y, red);
            string lorem =
                "Die Geschichte des Portable Document Format beginnt Anfang der 1990er-Jahre. Ziel war es, Dokumente " +
                "unabhängig von Anwendung, Betriebssystem und Hardware originalgetreu darzustellen. Heute ist PDF das " +
                "Standardformat für den Austausch von Dokumenten in Unternehmen, Behörden und im privaten Bereich.\n\n" +
                "Mit dem Werkzeug »Text hervorheben« können Sie wichtige Stellen markieren. Ziehen Sie dazu einfach mit der " +
                "Maus über den gewünschten Text. Unterstreichen und Durchstreichen funktionieren auf die gleiche Weise.\n\n" +
                "Notizen eignen sich für längere Anmerkungen. Klicken Sie mit dem Notiz-Werkzeug an eine beliebige Stelle der " +
                "Seite und geben Sie Ihren Kommentar ein. Alle Kommentare erscheinen außerdem in der Kommentarliste am unteren " +
                "Fensterrand, wo Sie sie sortieren, durchsuchen und abhaken können.\n\n" +
                "Beim Speichern werden Kommentare als echte PDF-Anmerkungen in die Datei geschrieben. Andere PDF-Programme " +
                "zeigen sie daher ebenfalls an. Wenn Sie die Datei später wieder in AcroClone öffnen, bleiben die Kommentare " +
                "vollständig bearbeitbar.";
            y = PdfObjects.AddTextBlock(doc, p, lorem, PdfObjects.TimesRoman, 12.5f, dark, m, y - 6, A4Width - 2 * m, 1.5);
            y -= 30;
            y = Heading(doc, p, "Formen", m, y, red);
            y -= 20;
            Native.FPDFPage_InsertObject(p, PdfObjects.Rect(new PdfRect(m, y - 90, m + 140, y), Color.FromArgb(40, 80, 160), 2, Color.FromArgb(220, 230, 250)));
            Native.FPDFPage_InsertObject(p, PdfObjects.Ellipse(new PdfRect(m + 175, y - 90, m + 315, y), Color.FromArgb(30, 130, 60), 2, Color.FromArgb(220, 245, 225)));
            Native.FPDFPage_InsertObject(p, PdfObjects.RoundRect(new PdfRect(m + 350, y - 90, A4Width - m, y), 14, red, 2, Color.FromArgb(250, 225, 225)));
            Footer(doc, p, 2, grey);
            doc.CommitPageContent(1);

            // Seite 3 (Querformat) ----------------------------------------
            doc.InsertBlankPage(2, A4Height, A4Width);
            p = doc.GetPage(2);
            y = A4Width - 70;
            y = Heading(doc, p, "Eine Seite im Querformat", m, y, red);
            y = PdfObjects.AddTextBlock(doc, p,
                "Diese Seite liegt im Querformat vor. Mit »Dokument › Seiten drehen« oder per Rechtsklick in den Seitenminiaturen " +
                "lässt sich die Ausrichtung dauerhaft ändern. »Anzeige › Ansicht drehen« dreht dagegen nur die Darstellung.",
                PdfObjects.Helvetica, 11, dark, m, y - 4, A4Height - 2 * m, 1.45);
            y -= 20;
            string[] cols = { "Quartal", "Umsatz", "Kosten", "Ergebnis" };
            string[,] rows = { { "Q1", "120.000 €", "80.000 €", "40.000 €" }, { "Q2", "135.000 €", "82.500 €", "52.500 €" },
                { "Q3", "128.000 €", "79.000 €", "49.000 €" }, { "Q4", "150.000 €", "90.000 €", "60.000 €" } };
            double colW = (A4Height - 2 * m) / 4;
            Native.FPDFPage_InsertObject(p, PdfObjects.Rect(new PdfRect(m, y - 22, A4Height - m, y), Color.Empty, 0, Color.FromArgb(60, 60, 60)));
            for (int c = 0; c < 4; c++)
            {
                IntPtr t = PdfObjects.Text(doc, PdfObjects.HelveticaBold, 11, cols[c], Color.White);
                PdfObjects.Transform(t, new Affine(1, 0, 0, 1, m + 8 + c * colW, y - 15));
                Native.FPDFPage_InsertObject(p, t);
            }
            y -= 22;
            for (int r = 0; r < 4; r++)
            {
                if (r % 2 == 1)
                    Native.FPDFPage_InsertObject(p, PdfObjects.Rect(new PdfRect(m, y - 22, A4Height - m, y), Color.Empty, 0, Color.FromArgb(240, 240, 240)));
                for (int c = 0; c < 4; c++)
                {
                    IntPtr t = PdfObjects.Text(doc, PdfObjects.Helvetica, 11, rows[r, c], dark);
                    PdfObjects.Transform(t, new Affine(1, 0, 0, 1, m + 8 + c * colW, y - 15));
                    Native.FPDFPage_InsertObject(p, t);
                }
                y -= 22;
            }
            Native.FPDFPage_InsertObject(p, PdfObjects.Rect(new PdfRect(m, y, A4Height - m, y + 110), Color.FromArgb(60, 60, 60), 1, Color.Empty));
            IntPtr f3 = PdfObjects.Text(doc, PdfObjects.HelveticaOblique, 9, "AcroClone 9 Pro – Beispieldokument – Seite 3", grey);
            PdfObjects.Transform(f3, new Affine(1, 0, 0, 1, m, 30));
            Native.FPDFPage_InsertObject(p, f3);
            doc.CommitPageContent(2);
            doc.ClosePages();
            return doc;
        }

        private static double Heading(PdfDocument doc, IntPtr page, string text, double x, double y, Color color)
        {
            IntPtr t = PdfObjects.Text(doc, PdfObjects.HelveticaBold, 15, text, color);
            PdfObjects.Transform(t, new Affine(1, 0, 0, 1, x, y - 15));
            Native.FPDFPage_InsertObject(page, t);
            Native.FPDFPage_InsertObject(page, PdfObjects.Rect(new PdfRect(x, y - 22, x + 200, y - 21), Color.Empty, 0, color));
            return y - 26;
        }

        private static void Footer(PdfDocument doc, IntPtr page, int number, Color color)
        {
            IntPtr t = PdfObjects.Text(doc, PdfObjects.HelveticaOblique, 9, "AcroClone 9 Pro – Beispieldokument – Seite " + number, color);
            PdfObjects.Transform(t, new Affine(1, 0, 0, 1, 60, 30));
            Native.FPDFPage_InsertObject(page, t);
        }
    }
}
