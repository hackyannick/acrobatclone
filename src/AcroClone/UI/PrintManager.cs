using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;
using AcroClone.Model;
using AcroClone.Pdf;

namespace AcroClone.UI
{
    /// <summary>Druckt ein Dokument inklusive Kommentaren (Seiten werden als Bild gerendert).</summary>
    public class PrintManager
    {
        private readonly DocumentSession session;
        private readonly int currentPage;
        private List<int> pages;
        private int next;

        public PrintManager(DocumentSession session, int currentPage)
        {
            this.session = session;
            this.currentPage = currentPage;
        }

        private PrintDocument CreateDocument(PrinterSettings settings)
        {
            PrintDocument doc = new PrintDocument();
            doc.DocumentName = session.DisplayName;
            if (settings != null) doc.PrinterSettings = settings;
            doc.BeginPrint += delegate
            {
                pages = new List<int>();
                PrinterSettings ps = doc.PrinterSettings;
                int n = session.PageCount;
                if (ps.PrintRange == PrintRange.CurrentPage) pages.Add(currentPage);
                else if (ps.PrintRange == PrintRange.SomePages)
                    for (int i = Math.Max(1, ps.FromPage); i <= Math.Min(n, ps.ToPage); i++) pages.Add(i - 1);
                else for (int i = 0; i < n; i++) pages.Add(i);
                next = 0;
            };
            doc.QueryPageSettings += delegate(object s, QueryPageSettingsEventArgs e)
            {
                if (pages == null || next >= pages.Count) return;
                SizeF size = session.Pdf.GetViewSize(pages[next], 0);
                e.PageSettings.Landscape = size.Width > size.Height;
            };
            doc.PrintPage += OnPrintPage;
            return doc;
        }

        private void OnPrintPage(object sender, PrintPageEventArgs e)
        {
            if (pages == null || pages.Count == 0) { e.HasMorePages = false; return; }
            int page = pages[next];
            SizeF size = session.Pdf.GetViewSize(page, 0);
            RectangleF area = e.MarginBounds;
            // An den bedruckbaren Bereich anpassen (Einheit: 1/100 Zoll)
            float pw = size.Width / 72f * 100f, ph = size.Height / 72f * 100f;
            float f = Math.Min(area.Width / pw, area.Height / ph);
            if (f > 1) f = 1;
            float w = pw * f, h = ph * f;
            RectangleF dst = new RectangleF(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);

            const double dpi = 200;
            int bw = (int)(size.Width / 72.0 * dpi * f), bh = (int)(size.Height / 72.0 * dpi * f);
            using (Bitmap bmp = session.Pdf.RenderPage(page, 0, Math.Max(1, bw), Math.Max(1, bh), true))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    Affine m = session.Pdf.GetPageTransform(page, 0).Scale(bw / size.Width);
                    foreach (Annotation a in session.AnnotationsOnPage(page))
                        if (a.Type != AnnotationType.Redaction) AnnotationPainter.Paint(g, a, m);
                }
                e.Graphics.DrawImage(bmp, dst);
            }
            next++;
            e.HasMorePages = next < pages.Count;
        }

        public void Print(IWin32Window owner)
        {
            using (PrintDialog dlg = new PrintDialog())
            {
                PrinterSettings ps = new PrinterSettings();
                ps.MinimumPage = 1;
                ps.MaximumPage = session.PageCount;
                ps.FromPage = 1;
                ps.ToPage = session.PageCount;
                dlg.PrinterSettings = ps;
                dlg.AllowSomePages = true;
                dlg.AllowCurrentPage = true;
                dlg.UseEXDialog = true;
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                using (PrintDocument doc = CreateDocument(dlg.PrinterSettings))
                {
                    try { doc.Print(); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(owner, "Fehler beim Drucken: " + ex.Message, "Drucken", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        public void Preview(IWin32Window owner)
        {
            using (PrintDocument doc = CreateDocument(null))
            using (PrintPreviewDialog dlg = new PrintPreviewDialog())
            {
                dlg.Document = doc;
                dlg.Text = "Druckvorschau – " + session.DisplayName;
                dlg.Width = 900;
                dlg.Height = 700;
                try { dlg.ShowDialog(owner); }
                catch (Exception ex)
                {
                    MessageBox.Show(owner, "Druckvorschau nicht möglich: " + ex.Message, "Drucken", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
