using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using AcroClone.Model;
using AcroClone.Pdf;

namespace AcroClone.UI
{
    public enum ToolMode
    {
        Hand, Select, Zoom, Snapshot, Note, Highlight, Underline, StrikeOut, FreeText,
        Rectangle, Ellipse, Line, Arrow, Pencil, Stamp, Signature, Redaction, Measure
    }

    public enum PageLayoutMode { SinglePage, Continuous, TwoUp, TwoUpContinuous }

    public enum ZoomMode { Custom, FitPage, FitWidth, FitVisible }

    public class ViewContextEventArgs : EventArgs
    {
        public Point Location;
        public Annotation Annotation;
        public bool HasTextSelection;
        public int Page;
    }

    /// <summary>
    /// Die Dokumentansicht: Seitenlayout, Zoom, Rendering und alle Mauswerkzeuge.
    /// </summary>
    public class DocumentView : Control
    {
        private class CacheEntry
        {
            public double Ppp;
            public int Rotation;
            public Rectangle Region;
            public Bitmap Bitmap;
            public long Stamp;
            public bool Stale;
        }

        private enum DragKind { None, Pan, TextSelect, Marquee, Ink, LineDraw, MoveAnnot, ResizeAnnot, MoveLineEnd, Form, Click }

        public const double MinZoom = 0.0833;
        public const double MaxZoom = 64.0;
        private static readonly double[] ZoomSteps = { 0.0833, 0.125, 0.25, 0.3333, 0.5, 0.6667, 0.75, 1.0, 1.25, 1.5, 2.0, 3.0, 4.0, 6.0, 8.0, 12.0, 16.0, 24.0, 32.0, 64.0 };

        private readonly VScrollBar vbar;
        private readonly HScrollBar hbar;
        private readonly Timer renderTimer;
        private readonly double dpi;

        private DocumentSession session;
        private double zoom = 1.0;
        private ZoomMode zoomMode = ZoomMode.FitWidth;
        private int rotation;
        private PageLayoutMode layout = PageLayoutMode.Continuous;
        private bool coverPage;
        private bool fullScreen;
        private ToolMode tool = ToolMode.Hand;

        private Rectangle[] pageRects = new Rectangle[0];
        private List<int[]> rows = new List<int[]>();
        private int singleRow;
        private Size canvas;
        private int scrollX, scrollY;
        private int currentPage;
        private bool layoutBusy;

        private readonly Dictionary<int, CacheEntry> cache = new Dictionary<int, CacheEntry>();
        private readonly HashSet<int> renderQueue = new HashSet<int>();
        private long stampCounter;

        // Auswahl und Bearbeitung
        private Annotation selected;
        private int selPage = -1, selAnchor = -1, selFocus = -1;
        private List<PdfRect> selRects = new List<PdfRect>();
        private int searchPage = -1;
        private List<PdfRect> searchRects = new List<PdfRect>();

        // Ziehen
        private DragKind drag = DragKind.None;
        private Point dragStartClient;
        private Point lastClient;
        private int dragPage = -1;
        private PdfPoint dragStartPt;
        private PdfPoint lastPt;
        private bool dragMoved;
        private List<PdfPoint> inkPoints;
        private PdfRect resizeOrigRect;
        private int resizeHandle;
        private int lineEndIndex;
        private LinkTarget pendingLink;
        private int formPage = -1;
        private bool formFocused;
        
        private readonly Dictionary<string, NotePopup> popups = new Dictionary<string, NotePopup>();
        private TextBox inlineEditor;
        private Annotation inlineAnnotation;
        private bool inlineIsNew;

        public string StampText = "GENEHMIGT";
        public Color StampColor = Color.FromArgb(30, 130, 40);
        public Annotation SignatureTemplate;
        public bool KeepToolSelected;

        public event EventHandler CurrentPageChanged;
        public event EventHandler ZoomChanged;
        public event EventHandler ToolChanged;
        public event EventHandler SelectionChanged;
        public event EventHandler ViewChanged;
        public event EventHandler<ViewContextEventArgs> ViewContextMenu;
        public event EventHandler<StatusEventArgs> StatusMessage;
        public event EventHandler FullScreenExitRequested;

        public DocumentView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.DocumentBackground;
            TabStop = true;

            vbar = new VScrollBar();
            vbar.Dock = DockStyle.Right;
            vbar.Visible = false;
            vbar.Scroll += delegate { SetScroll(scrollX, vbar.Value); };
            vbar.ValueChanged += delegate { if (vbar.Value != scrollY) SetScroll(scrollX, vbar.Value); };
            hbar = new HScrollBar();
            hbar.Dock = DockStyle.Bottom;
            hbar.Visible = false;
            hbar.Scroll += delegate { SetScroll(hbar.Value, scrollY); };
            hbar.ValueChanged += delegate { if (hbar.Value != scrollX) SetScroll(hbar.Value, scrollY); };
            Controls.Add(vbar);
            Controls.Add(hbar);

            renderTimer = new Timer();
            renderTimer.Interval = 15;
            renderTimer.Tick += OnRenderTimer;

            using (Graphics g = CreateGraphics()) dpi = g.DpiX > 0 ? g.DpiX : 96;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClearCache();
                renderTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------
        // Eigenschaften
        // ------------------------------------------------------------------

        public DocumentSession Session
        {
            get { return session; }
            set
            {
                if (session != null)
                {
                    session.StructureChanged -= OnStructureChanged;
                    session.AnnotationsChanged -= OnAnnotationsChanged;
                    session.DocumentReplaced -= OnDocumentReplaced;
                    if (session.Pdf != null) UnhookPdf(session.Pdf);
                }
                CloseAllPopups();
                CancelInlineEditor(true);
                session = value;
                selected = null;
                ClearTextSelection();
                ClearSearchHighlight();
                ClearCache();
                currentPage = 0;
                singleRow = 0;
                scrollX = scrollY = 0;
                if (session != null)
                {
                    session.StructureChanged += OnStructureChanged;
                    session.AnnotationsChanged += OnAnnotationsChanged;
                    session.DocumentReplaced += OnDocumentReplaced;
                    HookPdf(session.Pdf);
                }
                RelayoutKeepAnchor(false);
                Invalidate();
                RaiseCurrentPageChanged();
            }
        }

        private PdfDocument Pdf { get { return session == null ? null : session.Pdf; } }

        private void HookPdf(PdfDocument pdf)
        {
            pdf.FormInvalidated += OnFormInvalidated;
            pdf.FormAction += OnFormAction;
        }

        private void UnhookPdf(PdfDocument pdf)
        {
            pdf.FormInvalidated -= OnFormInvalidated;
            pdf.FormAction -= OnFormAction;
        }

        public double Zoom { get { return zoom; } }

        /// <summary>Im Vollbildmodus wird immer die ganze Seite angezeigt.</summary>
        private ZoomMode EffectiveZoomMode { get { return fullScreen ? ZoomMode.FitPage : zoomMode; } }
        public ZoomMode ZoomMode { get { return zoomMode; } }
        public int ViewRotation { get { return rotation; } }
        public PageLayoutMode PageLayout { get { return layout; } }
        public bool CoverPage { get { return coverPage; } }
        public bool IsFullScreen { get { return fullScreen; } }
        public ToolMode Tool { get { return tool; } }
        public int CurrentPage { get { return currentPage; } }
        public int PageCount { get { return Pdf == null ? 0 : Pdf.PageCount; } }
        public Annotation SelectedAnnotation { get { return selected; } }
        private double Ppp { get { return zoom * dpi / 72.0; } }
        private int PageMargin { get { return fullScreen ? 0 : 10; } }
        private int Gap { get { return fullScreen ? 0 : 8; } }

        public Size ViewportSize
        {
            get
            {
                return new Size(Math.Max(1, ClientSize.Width - (vbar.Visible ? vbar.Width : 0)),
                    Math.Max(1, ClientSize.Height - (hbar.Visible ? hbar.Height : 0)));
            }
        }

        public void SetTool(ToolMode t)
        {
            CommitInlineEditor();
            if (t != ToolMode.Select && t != ToolMode.Highlight && t != ToolMode.Underline && t != ToolMode.StrikeOut)
                ClearTextSelection();
            tool = t;
            UpdateCursor(PointToClient(MousePosition));
            if (ToolChanged != null) ToolChanged(this, EventArgs.Empty);
        }

        public void SetZoom(double z)
        {
            SetZoom(z, ZoomMode.Custom, new Point(ViewportSize.Width / 2, ViewportSize.Height / 2));
        }

        public void SetZoom(double z, ZoomMode mode, Point anchorClient)
        {
            z = Math.Max(MinZoom, Math.Min(MaxZoom, z));
            zoomMode = mode;
            if (Math.Abs(z - zoom) < 1e-6 && mode == ZoomMode.Custom) { RaiseZoomChanged(); return; }
            ViewAnchor a = CaptureAnchor(anchorClient);
            zoom = z;
            MarkCacheStale();
            Relayout();
            RestoreAnchor(a);
            Invalidate();
            RaiseZoomChanged();
        }

        public void SetZoomMode(ZoomMode mode)
        {
            zoomMode = mode;
            if (mode != ZoomMode.Custom)
            {
                ViewAnchor a = CaptureAnchor(Point.Empty);
                zoom = ComputeFitZoom(mode);
                MarkCacheStale();
                Relayout();
                if (a != null) { a.ViewPoint = Point.Empty; a.TopOfPage = true; }
                RestoreAnchor(a);
                if (mode == ZoomMode.FitPage) ScrollToPageTop(currentPage);
            }
            Invalidate();
            RaiseZoomChanged();
        }

        public void ZoomIn()
        {
            double next = ZoomSteps.FirstOrDefault(s => s > zoom * 1.001);
            SetZoom(next > 0 ? next : MaxZoom);
        }

        public void ZoomOut()
        {
            double prev = ZoomSteps.LastOrDefault(s => s < zoom / 1.001);
            SetZoom(prev > 0 ? prev : MinZoom);
        }

        public void SetRotation(int r)
        {
            r = ((r % 4) + 4) % 4;
            if (r == rotation) return;
            int page = currentPage;
            rotation = r;
            MarkCacheStale();
            if (EffectiveZoomMode != ZoomMode.Custom) zoom = ComputeFitZoom(EffectiveZoomMode);
            Relayout();
            ScrollToPageTop(page);
            Invalidate();
            RaiseZoomChanged();
        }

        public void SetPageLayout(PageLayoutMode mode, bool cover)
        {
            int page = currentPage;
            layout = mode;
            coverPage = cover;
            BuildRows();
            singleRow = RowOf(page);
            if (EffectiveZoomMode != ZoomMode.Custom) zoom = ComputeFitZoom(EffectiveZoomMode);
            Relayout();
            ScrollToPageTop(page);
            Invalidate();
            RaiseZoomChanged();
        }

        public void SetFullScreen(bool on)
        {
            fullScreen = on;
            BackColor = on ? Theme.FullScreenBackground : Theme.DocumentBackground;
            CommitInlineEditor();
            CloseAllPopups();
            int page = currentPage;
            BuildRows();
            singleRow = RowOf(page);
            if (EffectiveZoomMode != ZoomMode.Custom) zoom = ComputeFitZoom(EffectiveZoomMode);
            MarkCacheStale();
            Relayout();
            ScrollToPageTop(page);
            Invalidate();
        }

        // ------------------------------------------------------------------
        // Layout
        // ------------------------------------------------------------------

        private bool IsContinuous
        {
            get { return !fullScreen && (layout == PageLayoutMode.Continuous || layout == PageLayoutMode.TwoUpContinuous); }
        }

        private bool IsTwoUp
        {
            get { return layout == PageLayoutMode.TwoUp || layout == PageLayoutMode.TwoUpContinuous; }
        }

        private void BuildRows()
        {
            rows = new List<int[]>();
            int n = PageCount;
            if (!IsTwoUp || fullScreen)
            {
                for (int i = 0; i < n; i++) rows.Add(new int[] { i });
                return;
            }
            int start = 0;
            if (coverPage && n > 0) { rows.Add(new int[] { 0 }); start = 1; }
            for (int i = start; i < n; i += 2)
                rows.Add(i + 1 < n ? new int[] { i, i + 1 } : new int[] { i });
        }

        private int RowOf(int page)
        {
            for (int r = 0; r < rows.Count; r++)
                if (Array.IndexOf(rows[r], page) >= 0) return r;
            return 0;
        }

        private SizeF RowSizePoints(int r)
        {
            if (r < 0 || r >= rows.Count) return new SizeF(612, 792);
            float w = 0, h = 0;
            foreach (int p in rows[r])
            {
                SizeF s = Pdf.GetViewSize(p, rotation);
                w += s.Width;
                h = Math.Max(h, s.Height);
            }
            return new SizeF(w, h);
        }

        private double ComputeFitZoom(ZoomMode mode)
        {
            if (Pdf == null || PageCount == 0) return zoom;
            int r = RowOf(currentPage);
            SizeF rs = RowSizePoints(r);
            int gaps = (rows.Count > r && rows[r].Length > 1) ? Gap : 0;
            int sbw = fullScreen ? 0 : SystemInformation.VerticalScrollBarWidth;
            double availW = ClientSize.Width - sbw - 2 * PageMargin - gaps;
            double availH = ClientSize.Height - 2 * PageMargin;
            double pppW = availW / Math.Max(1, rs.Width);
            double pppH = availH / Math.Max(1, rs.Height);
            double ppp;
            switch (mode)
            {
                case ZoomMode.FitPage: ppp = Math.Min(pppW, pppH); break;
                case ZoomMode.FitVisible: ppp = pppW * 1.08; break;
                default: ppp = pppW; break;
            }
            double z = ppp * 72.0 / dpi;
            return Math.Max(MinZoom, Math.Min(MaxZoom, z));
        }

        private void Relayout()
        {
            if (layoutBusy) return;
            layoutBusy = true;
            try
            {
                if (Pdf == null)
                {
                    pageRects = new Rectangle[0];
                    canvas = Size.Empty;
                    vbar.Visible = hbar.Visible = false;
                    return;
                }
                int n = PageCount;
                if (rows.Count == 0 || rows.Sum(r => r.Length) != n) BuildRows();
                singleRow = Math.Max(0, Math.Min(singleRow, rows.Count - 1));
                double ppp = Ppp;
                Size[] px = new Size[n];
                for (int i = 0; i < n; i++)
                {
                    SizeF s = Pdf.GetViewSize(i, rotation);
                    px[i] = new Size(Math.Max(1, (int)Math.Round(s.Width * ppp)), Math.Max(1, (int)Math.Round(s.Height * ppp)));
                }
                pageRects = new Rectangle[n];

                // Zweimal: Sichtbarkeit der Bildlaufleisten beeinflusst die Viewportgröße
                for (int pass = 0; pass < 2; pass++)
                {
                    Size vp = ViewportSize;
                    int m = PageMargin, gap = Gap;
                    if (IsContinuous)
                    {
                        int contentW = 0;
                        int[] rowW = new int[rows.Count], rowH = new int[rows.Count];
                        for (int r = 0; r < rows.Count; r++)
                        {
                            foreach (int p in rows[r]) { rowW[r] += px[p].Width; rowH[r] = Math.Max(rowH[r], px[p].Height); }
                            rowW[r] += gap * (rows[r].Length - 1);
                            contentW = Math.Max(contentW, rowW[r]);
                        }
                        int cw = Math.Max(vp.Width, contentW + 2 * m);
                        int y = m;
                        for (int r = 0; r < rows.Count; r++)
                        {
                            int x = (cw - rowW[r]) / 2;
                            if (rows[r].Length == 1 && IsTwoUp && coverPage && r == 0)
                                x = cw / 2 + gap / 2; // Deckblatt rechts
                            foreach (int p in rows[r])
                            {
                                pageRects[p] = new Rectangle(x, y + (rowH[r] - px[p].Height) / 2, px[p].Width, px[p].Height);
                                x += px[p].Width + gap;
                            }
                            y += rowH[r] + gap;
                        }
                        canvas = new Size(cw, Math.Max(vp.Height, y - gap + m));
                    }
                    else
                    {
                        int r = singleRow;
                        int rw = 0, rh = 0;
                        foreach (int p in rows[r]) { rw += px[p].Width; rh = Math.Max(rh, px[p].Height); }
                        rw += gap * (rows[r].Length - 1);
                        int cw = Math.Max(vp.Width, rw + 2 * m);
                        int ch = Math.Max(vp.Height, rh + 2 * m);
                        int x = (cw - rw) / 2;
                        int y = rh + 2 * m <= vp.Height ? (ch - rh) / 2 : m;
                        if (fullScreen) y = (ch - rh) / 2;
                        foreach (int p in rows[r])
                        {
                            pageRects[p] = new Rectangle(x, y + (rh - px[p].Height) / 2, px[p].Width, px[p].Height);
                            x += px[p].Width + gap;
                        }
                        canvas = new Size(cw, ch);
                    }
                    bool needV = !fullScreen && canvas.Height > ClientSize.Height - (hbar.Visible ? hbar.Height : 0);
                    bool needH = !fullScreen && canvas.Width > ClientSize.Width - (needV ? SystemInformation.VerticalScrollBarWidth : 0);
                    if (needV == vbar.Visible && needH == hbar.Visible) break;
                    vbar.Visible = needV;
                    hbar.Visible = needH;
                }
                UpdateScrollBars();
                SetScroll(scrollX, scrollY);
            }
            finally
            {
                layoutBusy = false;
            }
        }

        private void UpdateScrollBars()
        {
            Size vp = ViewportSize;
            if (vbar.Visible)
            {
                vbar.Minimum = 0;
                vbar.Maximum = Math.Max(0, canvas.Height - 1);
                vbar.LargeChange = Math.Max(1, vp.Height);
                vbar.SmallChange = 40;
            }
            if (hbar.Visible)
            {
                hbar.Minimum = 0;
                hbar.Maximum = Math.Max(0, canvas.Width - 1);
                hbar.LargeChange = Math.Max(1, vp.Width);
                hbar.SmallChange = 40;
            }
        }

        private void SetScroll(int x, int y)
        {
            Size vp = ViewportSize;
            x = Math.Max(0, Math.Min(x, canvas.Width - vp.Width));
            y = Math.Max(0, Math.Min(y, canvas.Height - vp.Height));
            bool changed = x != scrollX || y != scrollY;
            scrollX = x;
            scrollY = y;
            if (vbar.Visible && vbar.Value != y) vbar.Value = Math.Min(Math.Max(vbar.Minimum, y), Math.Max(vbar.Minimum, vbar.Maximum - vbar.LargeChange + 1));
            if (hbar.Visible && hbar.Value != x) hbar.Value = Math.Min(Math.Max(hbar.Minimum, x), Math.Max(hbar.Minimum, hbar.Maximum - hbar.LargeChange + 1));
            if (changed)
            {
                RepositionChildren();
                Invalidate();
            }
            UpdateCurrentPage();
            if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
        }

        private void UpdateCurrentPage()
        {
            if (Pdf == null || pageRects.Length == 0) return;
            int best = currentPage;
            if (IsContinuous)
            {
                Size vp = ViewportSize;
                Rectangle view = new Rectangle(scrollX, scrollY, vp.Width, vp.Height);
                long bestArea = -1;
                int probeY = scrollY + vp.Height / 3;
                for (int i = 0; i < pageRects.Length; i++)
                {
                    Rectangle r = pageRects[i];
                    if (r.Bottom < scrollY) continue;
                    if (r.Top > view.Bottom) break;
                    Rectangle inter = Rectangle.Intersect(r, view);
                    long area = (long)inter.Width * inter.Height;
                    if (r.Top <= probeY && r.Bottom + Gap >= probeY) area += (long)vp.Width * vp.Height;
                    if (area > bestArea) { bestArea = area; best = i; }
                }
            }
            else if (rows.Count > 0)
            {
                best = rows[Math.Min(singleRow, rows.Count - 1)][0];
            }
            if (best != currentPage)
            {
                currentPage = best;
                RaiseCurrentPageChanged();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Pdf == null) return;
            RelayoutKeepAnchor(true);
        }

        private void RelayoutKeepAnchor(bool refit)
        {
            if (Pdf == null) { Relayout(); return; }
            ViewAnchor a = CaptureAnchor(Point.Empty);
            if (refit && EffectiveZoomMode != ZoomMode.Custom)
            {
                double z = ComputeFitZoom(EffectiveZoomMode);
                if (Math.Abs(z - zoom) > 1e-4) { zoom = z; MarkCacheStale(); RaiseZoomChanged(); }
            }
            Relayout();
            RestoreAnchor(a);
            Invalidate();
        }

        // ------------------------------------------------------------------
        // Anker (Position beim Zoomen erhalten)
        // ------------------------------------------------------------------

        private class ViewAnchor
        {
            public int Page;
            public PdfPoint Point;
            public Point ViewPoint;
            public bool TopOfPage;
        }

        private ViewAnchor CaptureAnchor(Point viewPoint)
        {
            if (Pdf == null || pageRects.Length == 0) return null;
            Point c = new Point(viewPoint.X + scrollX, viewPoint.Y + scrollY);
            int page = PageAtCanvas(c, true);
            if (page < 0) page = currentPage;
            if (page < 0 || page >= pageRects.Length) return null;
            ViewAnchor a = new ViewAnchor();
            a.Page = page;
            a.ViewPoint = viewPoint;
            Rectangle r = pageRects[page];
            if (r.IsEmpty) { a.TopOfPage = true; return a; }
            Point clamped = new Point(Math.Max(r.Left, Math.Min(r.Right, c.X)), Math.Max(r.Top - Gap, Math.Min(r.Bottom, c.Y)));
            a.Point = CanvasToPage(page, clamped);
            if (c.Y < r.Top) a.ViewPoint = new Point(viewPoint.X, viewPoint.Y + (r.Top - c.Y));
            return a;
        }

        private void RestoreAnchor(ViewAnchor a)
        {
            if (a == null || a.Page >= pageRects.Length) return;
            if (!IsContinuous && RowOf(a.Page) != singleRow)
            {
                singleRow = RowOf(a.Page);
                Relayout();
            }
            Rectangle r = pageRects[a.Page];
            if (a.TopOfPage)
            {
                SetScroll(r.Left - PageMargin, r.Top - PageMargin);
                return;
            }
            PointF dev = PageToCanvas(a.Page).Apply(a.Point.X, a.Point.Y);
            SetScroll((int)Math.Round(dev.X - a.ViewPoint.X), (int)Math.Round(dev.Y - a.ViewPoint.Y));
        }

        // ------------------------------------------------------------------
        // Koordinaten
        // ------------------------------------------------------------------

        public Affine PageToCanvas(int page)
        {
            Rectangle r = pageRects[page];
            return Pdf.GetPageTransform(page, rotation).Scale(Ppp).Translate(r.X, r.Y);
        }

        public Affine PageToClient(int page)
        {
            return PageToCanvas(page).Translate(-scrollX, -scrollY);
        }

        private PdfPoint CanvasToPage(int page, Point c)
        {
            return PageToCanvas(page).Invert().ApplyD(c.X, c.Y);
        }

        public PdfPoint ClientToPage(int page, Point client)
        {
            return CanvasToPage(page, new Point(client.X + scrollX, client.Y + scrollY));
        }

        private int PageAtCanvas(Point c, bool nearest)
        {
            int best = -1;
            long bestDist = long.MaxValue;
            for (int i = 0; i < pageRects.Length; i++)
            {
                Rectangle r = pageRects[i];
                if (r.IsEmpty) continue;
                if (r.Contains(c)) return i;
                if (!nearest) continue;
                long dx = c.X < r.Left ? r.Left - c.X : (c.X > r.Right ? c.X - r.Right : 0);
                long dy = c.Y < r.Top ? r.Top - c.Y : (c.Y > r.Bottom ? c.Y - r.Bottom : 0);
                long d = dx * dx + dy * dy;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        public int PageAtClient(Point client)
        {
            return PageAtCanvas(new Point(client.X + scrollX, client.Y + scrollY), false);
        }

        /// <summary>Sichtbarer Bereich einer Seite in Seitenkoordinaten (für die Miniaturansicht).</summary>
        public bool GetVisibleRegion(int page, out PdfRect region)
        {
            region = new PdfRect();
            if (Pdf == null || page < 0 || page >= pageRects.Length || pageRects[page].IsEmpty) return false;
            Size vp = ViewportSize;
            Rectangle view = new Rectangle(scrollX, scrollY, vp.Width, vp.Height);
            Rectangle inter = Rectangle.Intersect(view, pageRects[page]);
            if (inter.IsEmpty) return false;
            Affine inv = PageToCanvas(page).Invert();
            PdfPoint a = inv.ApplyD(inter.Left, inter.Top), b = inv.ApplyD(inter.Right, inter.Bottom);
            region = new PdfRect(a.X, a.Y, b.X, b.Y);
            return true;
        }

        // ------------------------------------------------------------------
        // Navigation
        // ------------------------------------------------------------------

        public void GoToPage(int page)
        {
            GoToPage(page, null);
        }

        public void GoToPage(int page, double? yPt)
        {
            if (Pdf == null || PageCount == 0) return;
            page = Math.Max(0, Math.Min(PageCount - 1, page));
            if (!IsContinuous)
            {
                singleRow = RowOf(page);
                if (EffectiveZoomMode != ZoomMode.Custom) zoom = ComputeFitZoom(EffectiveZoomMode);
                Relayout();
            }
            if (yPt.HasValue)
            {
                PointF p = PageToCanvas(page).Apply(0, yPt.Value);
                SetScroll(scrollX, (int)p.Y - 4);
            }
            else
            {
                ScrollToPageTop(page);
            }
            if (currentPage != page)
            {
                currentPage = page;
                RaiseCurrentPageChanged();
            }
            Invalidate();
        }

        private void ScrollToPageTop(int page)
        {
            if (page < 0 || page >= pageRects.Length) return;
            Rectangle r = pageRects[page];
            if (r.IsEmpty) return;
            int y = IsContinuous ? r.Top - PageMargin : 0;
            int x = r.Width > ViewportSize.Width ? r.Left - PageMargin : scrollX;
            SetScroll(x, y);
            currentPage = page;
        }

        public void NextPage()
        {
            if (Pdf == null) return;
            int r = RowOf(currentPage);
            if (r + 1 < rows.Count) GoToPage(rows[r + 1][0]);
        }

        public void PreviousPage()
        {
            if (Pdf == null) return;
            int r = RowOf(currentPage);
            if (r > 0) GoToPage(rows[r - 1][0]);
        }

        public void FirstPage() { GoToPage(0); }
        public void LastPage() { GoToPage(PageCount - 1); }

        public void ScrollBy(int dx, int dy)
        {
            SetScroll(scrollX + dx, scrollY + dy);
        }

        /// <summary>Zeigt einen Bereich einer Seite an (scrollt ihn in den sichtbaren Bereich).</summary>
        public void EnsureVisible(int page, PdfRect rect)
        {
            if (page < 0 || page >= PageCount) return;
            if (!IsContinuous && RowOf(page) != singleRow) GoToPage(page);
            RectangleF r = PageToCanvas(page).MapRect(rect);
            Size vp = ViewportSize;
            int x = scrollX, y = scrollY;
            if (r.Left < scrollX || r.Right > scrollX + vp.Width) x = (int)(r.Left + r.Width / 2 - vp.Width / 2);
            if (r.Top < scrollY + 20 || r.Bottom > scrollY + vp.Height - 20) y = (int)(r.Top + r.Height / 2 - vp.Height / 2);
            SetScroll(x, y);
        }

        public void ZoomToRect(int page, PdfRect rect)
        {
            if (rect.Width < 2 || rect.Height < 2) return;
            Size vp = ViewportSize;
            SizeF s = new SizeF((float)rect.Width, (float)rect.Height);
            if ((rotation & 1) == 1) s = new SizeF(s.Height, s.Width);
            double ppp = Math.Min((vp.Width - 20) / s.Width, (vp.Height - 20) / s.Height);
            zoom = Math.Max(MinZoom, Math.Min(MaxZoom, ppp * 72 / dpi));
            zoomMode = ZoomMode.Custom;
            MarkCacheStale();
            Relayout();
            RectangleF r = PageToCanvas(page).MapRect(rect);
            SetScroll((int)(r.Left + r.Width / 2 - vp.Width / 2), (int)(r.Top + r.Height / 2 - vp.Height / 2));
            Invalidate();
            RaiseZoomChanged();
        }

        // ------------------------------------------------------------------
        // Ereignisse aus Sitzung und Formular
        // ------------------------------------------------------------------

        private void OnStructureChanged(object sender, EventArgs e)
        {
            RefreshAll();
        }

        private void OnDocumentReplaced(object sender, EventArgs e)
        {
            HookPdf(session.Pdf);
        }

        public void RefreshAll()
        {
            ClearCache();
            ClearTextSelection();
            ClearSearchHighlight();
            if (selected != null && session != null && !session.Annotations.Contains(selected)) selected = null;
            int page = Math.Min(currentPage, Math.Max(0, PageCount - 1));
            BuildRows();
            singleRow = RowOf(page);
            ViewAnchor a = CaptureAnchor(Point.Empty);
            if (EffectiveZoomMode != ZoomMode.Custom) zoom = ComputeFitZoom(EffectiveZoomMode);
            Relayout();
            if (a != null && a.Page < PageCount) RestoreAnchor(a);
            if (currentPage >= PageCount) currentPage = Math.Max(0, PageCount - 1);
            Invalidate();
            RaiseCurrentPageChanged();
        }

        private void OnAnnotationsChanged(object sender, EventArgs e)
        {
            if (selected != null && !session.Annotations.Contains(selected))
            {
                Annotation same = session.FindAnnotation(selected.Id);
                selected = same;
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
            List<string> gone = new List<string>();
            foreach (KeyValuePair<string, NotePopup> kv in popups)
            {
                if (session.FindAnnotation(kv.Key) == null) gone.Add(kv.Key);
                else kv.Value.RefreshFromModel();
            }
            foreach (string id in gone) ClosePopup(id);
            RepositionChildren();
            Invalidate();
        }

        private void OnFormInvalidated(object sender, PageEventArgs e)
        {
            if (e.PageIndex >= 0) RemoveCacheEntry(e.PageIndex);
            else ClearCache();
            Invalidate();
        }

        private void OnFormAction(object sender, LinkEventArgs e)
        {
            FollowLink(e.Target);
        }

        public void FollowLink(LinkTarget t)
        {
            if (t == null) return;
            if (!string.IsNullOrEmpty(t.Uri))
            {
                if (t.Uri.StartsWith("named:"))
                {
                    string action = t.Uri.Substring(6);
                    if (action == "NextPage") NextPage();
                    else if (action == "PrevPage") PreviousPage();
                    else if (action == "FirstPage") FirstPage();
                    else if (action == "LastPage") LastPage();
                    return;
                }
                DialogResult r = MessageBox.Show(FindForm(),
                    "Das Dokument versucht, eine Verbindung zu folgender Adresse herzustellen:\n\n" + t.Uri +
                    "\n\nWenn Sie dieser Website vertrauen, klicken Sie auf \"Zulassen\" (OK).",
                    "Sicherheitswarnung", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (r == DialogResult.OK)
                {
                    try { Process.Start(t.Uri); }
                    catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
                return;
            }
            if (t.PageIndex >= 0) GoToPage(t.PageIndex, t.Y);
        }

        // ------------------------------------------------------------------
        // Render-Cache
        // ------------------------------------------------------------------

        private void ClearCache()
        {
            foreach (CacheEntry e in cache.Values)
                if (e.Bitmap != null) e.Bitmap.Dispose();
            cache.Clear();
            renderQueue.Clear();
        }

        private void RemoveCacheEntry(int page)
        {
            CacheEntry e;
            if (cache.TryGetValue(page, out e))
            {
                if (e.Bitmap != null) e.Bitmap.Dispose();
                cache.Remove(page);
            }
        }

        private void MarkCacheStale()
        {
            foreach (CacheEntry e in cache.Values) e.Stale = true;
        }

        public void InvalidatePage(int page)
        {
            RemoveCacheEntry(page);
            Invalidate();
        }

        private Bitmap RenderRegion(int page, Rectangle pr, Rectangle need, out Rectangle region)
        {
            long area = (long)pr.Width * pr.Height;
            if (area <= 9000000L) region = new Rectangle(0, 0, pr.Width, pr.Height);
            else
            {
                Rectangle inflated = need;
                inflated.Inflate(256, 256);
                region = Rectangle.Intersect(inflated, new Rectangle(0, 0, pr.Width, pr.Height));
            }
            return Pdf.Render(page, rotation, pr.Size, region, false);
        }

        private CacheEntry EnsureRendered(int page, Rectangle pr, Rectangle need, bool allowDeferred, out bool deferred)
        {
            deferred = false;
            CacheEntry e;
            cache.TryGetValue(page, out e);
            double ppp = Ppp;
            bool valid = e != null && !e.Stale && Math.Abs(e.Ppp - ppp) < 1e-9 && e.Rotation == rotation && e.Region.Contains(need);
            if (valid)
            {
                e.Stamp = ++stampCounter;
                return e;
            }
            if (allowDeferred && e != null && e.Bitmap != null && e.Rotation == rotation)
            {
                deferred = true;
                renderQueue.Add(page);
                renderTimer.Start();
                return e;
            }
            Rectangle region;
            Bitmap bmp;
            try
            {
                bmp = RenderRegion(page, pr, need, out region);
            }
            catch (Exception)
            {
                return null;
            }
            if (e != null && e.Bitmap != null) e.Bitmap.Dispose();
            e = new CacheEntry();
            e.Ppp = ppp;
            e.Rotation = rotation;
            e.Region = region;
            e.Bitmap = bmp;
            e.Stamp = ++stampCounter;
            cache[page] = e;
            TrimCache();
            return e;
        }

        private void TrimCache()
        {
            if (cache.Count <= 14) return;
            foreach (int k in cache.OrderBy(kv => kv.Value.Stamp).Take(cache.Count - 14).Select(kv => kv.Key).ToList())
                RemoveCacheEntry(k);
        }

        private void OnRenderTimer(object sender, EventArgs e)
        {
            renderTimer.Stop();
            if (Pdf == null) { renderQueue.Clear(); return; }
            Size vp = ViewportSize;
            Rectangle view = new Rectangle(scrollX, scrollY, vp.Width, vp.Height);
            foreach (int page in renderQueue.ToList())
            {
                renderQueue.Remove(page);
                if (page >= pageRects.Length) continue;
                Rectangle pr = pageRects[page];
                Rectangle vis = Rectangle.Intersect(pr, view);
                if (vis.IsEmpty) { RemoveCacheEntry(page); continue; }
                vis.Offset(-pr.X, -pr.Y);
                bool deferred;
                CacheEntry old;
                if (cache.TryGetValue(page, out old)) old.Stale = true;
                RemoveCacheEntryKeepNone(page);
                EnsureRendered(page, pr, vis, false, out deferred);
                Invalidate(new Rectangle(pr.X - scrollX, pr.Y - scrollY, pr.Width, pr.Height));
                break;
            }
            if (renderQueue.Count > 0) renderTimer.Start();
        }

        private void RemoveCacheEntryKeepNone(int page)
        {
            RemoveCacheEntry(page);
        }

        // ------------------------------------------------------------------
        // Zeichnen
        // ------------------------------------------------------------------

        protected override void OnPaint(PaintEventArgs e)
        {
            try
            {
                PaintDocument(e);
            }
            catch (Exception ex)
            {
                // Ein Fehler beim Zeichnen darf die Anwendung nicht beenden
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private void PaintDocument(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (Pdf == null || pageRects.Length == 0) return;
            Size vp = ViewportSize;
            Rectangle view = new Rectangle(scrollX, scrollY, vp.Width, vp.Height);
            g.SetClip(new Rectangle(0, 0, vp.Width, vp.Height));
            double ppp = Ppp;

            for (int i = 0; i < pageRects.Length; i++)
            {
                Rectangle pr = pageRects[i];
                if (pr.IsEmpty || !pr.IntersectsWith(view)) continue;
                Rectangle client = new Rectangle(pr.X - scrollX, pr.Y - scrollY, pr.Width, pr.Height);
                if (!client.IntersectsWith(e.ClipRectangle)) continue;

                if (!fullScreen)
                {
                    using (SolidBrush sh = new SolidBrush(Theme.PageShadow))
                    {
                        g.FillRectangle(sh, client.Right, client.Top + 3, 3, client.Height);
                        g.FillRectangle(sh, client.Left + 3, client.Bottom, client.Width - 3, 3);
                    }
                }

                Rectangle need = Rectangle.Intersect(pr, view);
                need.Offset(-pr.X, -pr.Y);
                bool deferred;
                CacheEntry entry = EnsureRendered(i, pr, need, true, out deferred);
                if (entry != null && entry.Bitmap != null)
                {
                    if (!deferred)
                    {
                        g.DrawImageUnscaled(entry.Bitmap, client.X + entry.Region.X, client.Y + entry.Region.Y);
                    }
                    else
                    {
                        g.FillRectangle(Brushes.White, client);
                        double s = ppp / entry.Ppp;
                        RectangleF dst = new RectangleF((float)(client.X + entry.Region.X * s), (float)(client.Y + entry.Region.Y * s),
                            (float)(entry.Region.Width * s), (float)(entry.Region.Height * s));
                        g.InterpolationMode = InterpolationMode.Low;
                        g.DrawImage(entry.Bitmap, dst);
                    }
                }
                else
                {
                    g.FillRectangle(Brushes.White, client);
                }
                if (!fullScreen)
                    using (Pen border = new Pen(Theme.PageBorder))
                        g.DrawRectangle(border, client.X - 1, client.Y - 1, client.Width + 1, client.Height + 1);

                PaintOverlays(g, i, client);
            }
            g.ResetClip();
            if (vbar.Visible && hbar.Visible)
                g.FillRectangle(SystemBrushes.Control, vp.Width, vp.Height, vbar.Width, hbar.Height);
        }

        private void PaintOverlays(Graphics g, int page, Rectangle client)
        {
            GraphicsState st = g.Save();
            g.IntersectClip(client);
            Affine m = PageToClient(page);

            foreach (Annotation a in session.AnnotationsOnPage(page))
            {
                if (a == inlineAnnotation) continue;
                try { AnnotationPainter.Paint(g, a, m); }
                catch (Exception) { }
            }

            if (searchPage == page && searchRects.Count > 0)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(110, 40, 110, 230)))
                    foreach (PdfRect r in searchRects) g.FillRectangle(b, m.MapRect(r));
            }
            if (selPage == page && selRects.Count > 0)
            {
                using (SolidBrush b = new SolidBrush(Theme.SelectionFill))
                    foreach (PdfRect r in selRects) g.FillRectangle(b, m.MapRect(r));
            }
            if (selected != null && selected.Page == page && selected != inlineAnnotation)
                AnnotationPainter.PaintSelection(g, selected, m);

            if (dragPage == page) PaintToolPreview(g, m);
            g.Restore(st);
        }

        private void PaintToolPreview(Graphics g, Affine m)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            switch (drag)
            {
                case DragKind.Marquee:
                    {
                        RectangleF r = m.MapRect(new PdfRect(dragStartPt.X, dragStartPt.Y, lastPt.X, lastPt.Y));
                        if (tool == ToolMode.Ellipse)
                        {
                            using (Pen p = new Pen(ToolColor(AnnotationType.Ellipse), Math.Max(1f, (float)(ToolWidth() * Ppp))))
                                g.DrawEllipse(p, r);
                        }
                        else if (tool == ToolMode.Rectangle)
                        {
                            using (Pen p = new Pen(ToolColor(AnnotationType.Rectangle), Math.Max(1f, (float)(ToolWidth() * Ppp))))
                                g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                        }
                        else if (tool == ToolMode.Redaction)
                        {
                            using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 255, 0, 0))) g.FillRectangle(b, r);
                            g.DrawRectangle(Pens.Red, r.X, r.Y, r.Width, r.Height);
                        }
                        else
                        {
                            using (Pen p = new Pen(Color.FromArgb(40, 40, 40), 1f))
                            {
                                p.DashStyle = DashStyle.Dash;
                                g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                            }
                            if (tool == ToolMode.Zoom || tool == ToolMode.Snapshot)
                                using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 60, 120, 230))) g.FillRectangle(b, r);
                        }
                        break;
                    }
                case DragKind.Ink:
                    if (inkPoints != null && inkPoints.Count > 1)
                    {
                        PointF[] pts = inkPoints.Select(p => m.Apply(p.X, p.Y)).ToArray();
                        using (Pen p = new Pen(ToolColor(AnnotationType.Ink), Math.Max(1f, (float)(ToolWidth() * Ppp))))
                        {
                            p.StartCap = p.EndCap = LineCap.Round;
                            p.LineJoin = LineJoin.Round;
                            g.DrawLines(p, pts);
                        }
                    }
                    break;
                case DragKind.LineDraw:
                    {
                        PointF a = m.Apply(dragStartPt.X, dragStartPt.Y), b = m.Apply(lastPt.X, lastPt.Y);
                        if (tool == ToolMode.Measure)
                        {
                            using (Pen p = new Pen(Color.FromArgb(20, 60, 160), 1.5f))
                            {
                                p.StartCap = p.EndCap = LineCap.Flat;
                                g.DrawLine(p, a, b);
                                g.FillEllipse(Brushes.RoyalBlue, a.X - 3, a.Y - 3, 6, 6);
                                g.FillEllipse(Brushes.RoyalBlue, b.X - 3, b.Y - 3, 6, 6);
                            }
                            double dx = lastPt.X - dragStartPt.X, dy = lastPt.Y - dragStartPt.Y;
                            double mm = Math.Sqrt(dx * dx + dy * dy) / 72.0 * 25.4;
                            string label = mm >= 100 ? (mm / 10).ToString("0.00") + " cm" : mm.ToString("0.0") + " mm";
                            using (Font f = new Font(Theme.UIFont, FontStyle.Bold))
                            {
                                SizeF sz = g.MeasureString(label, f);
                                RectangleF lr = new RectangleF((a.X + b.X) / 2 + 6, (a.Y + b.Y) / 2 - sz.Height - 4, sz.Width + 6, sz.Height + 2);
                                g.FillRectangle(new SolidBrush(Color.FromArgb(235, 255, 255, 225)), lr);
                                g.DrawRectangle(Pens.Gray, lr.X, lr.Y, lr.Width, lr.Height);
                                g.DrawString(label, f, Brushes.Black, lr.X + 3, lr.Y + 1);
                            }
                        }
                        else
                        {
                            AnnotationType t = tool == ToolMode.Arrow ? AnnotationType.Arrow : AnnotationType.Line;
                            using (Pen p = new Pen(ToolColor(t), Math.Max(1f, (float)(ToolWidth() * Ppp))))
                            {
                                if (t == AnnotationType.Arrow) p.CustomEndCap = new AdjustableArrowCap(4, 4, false);
                                g.DrawLine(p, a, b);
                            }
                        }
                        break;
                    }
            }
        }

        // ------------------------------------------------------------------
        // Werkzeug-Standardwerte
        // ------------------------------------------------------------------

        public static Color DefaultColor(AnnotationType t)
        {
            switch (t)
            {
                case AnnotationType.Note: return AppSettings.GetColor("Color.Note", Color.FromArgb(255, 230, 50));
                case AnnotationType.Highlight: return AppSettings.GetColor("Color.Highlight", Color.FromArgb(255, 240, 0));
                case AnnotationType.Underline: return AppSettings.GetColor("Color.Underline", Color.FromArgb(0, 150, 60));
                case AnnotationType.StrikeOut: return AppSettings.GetColor("Color.StrikeOut", Color.FromArgb(220, 0, 0));
                case AnnotationType.Signature: return AppSettings.GetColor("Color.Signature", Color.FromArgb(20, 40, 120));
                default: return AppSettings.GetColor("Color." + t, Color.FromArgb(220, 0, 0));
            }
        }

        private Color ToolColor(AnnotationType t) { return DefaultColor(t); }

        private float ToolWidth()
        {
            if (tool == ToolMode.Pencil) return AppSettings.GetInt("Width.Ink", 2);
            return AppSettings.GetInt("Width.Shapes", 2);
        }

        private Annotation NewAnnotation(AnnotationType type, int page)
        {
            Annotation a = new Annotation();
            a.Type = type;
            a.Page = page;
            a.Author = AppSettings.Author;
            a.Color = DefaultColor(type);
            a.Rotation = Pdf.GetPageRotation(page);
            a.LineWidth = ToolWidth();
            if (type == AnnotationType.FreeText)
            {
                a.LineWidth = 1;
                a.FontSize = AppSettings.GetInt("FreeText.FontSize", 12);
                a.FillColor = Color.Empty;
            }
            return a;
        }

        // ------------------------------------------------------------------
        // Maus
        // ------------------------------------------------------------------

        private static int Modifiers()
        {
            int m = 0;
            if ((ModifierKeys & Keys.Shift) != 0) m |= Native.FWL_EVENTFLAG_ShiftKey;
            if ((ModifierKeys & Keys.Control) != 0) m |= Native.FWL_EVENTFLAG_ControlKey;
            if ((ModifierKeys & Keys.Alt) != 0) m |= Native.FWL_EVENTFLAG_AltKey;
            return m;
        }

        private double Tolerance { get { return 4 / Ppp; } }

        private Annotation HitAnnotation(int page, PdfPoint pt)
        {
            if (session == null || page < 0) return null;
            List<Annotation> list = session.AnnotationsOnPage(page).ToList();
            if (selected != null && selected.Page == page && selected.HitTest(pt.X, pt.Y, Tolerance)) return selected;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].HitTest(pt.X, pt.Y, Tolerance)) return list[i];
            return null;
        }

        /// <summary>Index des Anfassers des ausgewählten Kommentars unter dem Mauszeiger (-1 = keiner).</summary>
        private int HitHandle(Point client)
        {
            if (selected == null || selected.Page >= pageRects.Length) return -1;
            Affine m = PageToClient(selected.Page);
            if (selected.Type == AnnotationType.Line || selected.Type == AnnotationType.Arrow)
            {
                if (selected.Strokes.Count > 0 && selected.Strokes[0].Count >= 2)
                {
                    List<PdfPoint> s = selected.Strokes[0];
                    PointF a = m.Apply(s[0].X, s[0].Y), b = m.Apply(s[s.Count - 1].X, s[s.Count - 1].Y);
                    if (Math.Abs(a.X - client.X) <= 5 && Math.Abs(a.Y - client.Y) <= 5) return 10;
                    if (Math.Abs(b.X - client.X) <= 5 && Math.Abs(b.Y - client.Y) <= 5) return 11;
                }
                return -1;
            }
            if (!selected.IsResizable) return -1;
            RectangleF r = AnnotationPainter.DeviceBounds(selected, m);
            r.Inflate(2, 2);
            PointF[] hs = AnnotationPainter.HandlePoints(r);
            for (int i = 0; i < hs.Length; i++)
                if (Math.Abs(hs[i].X - client.X) <= 5 && Math.Abs(hs[i].Y - client.Y) <= 5) return i;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (inlineEditor != null) CommitInlineEditor();
            if (!Focused) Focus();
            if (Pdf == null) return;
            if (fullScreen)
            {
                if (e.Button == MouseButtons.Left) NextPage();
                else if (e.Button == MouseButtons.Right) PreviousPage();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            int page = PageAtClient(e.Location);
            PdfPoint pt = page >= 0 ? ClientToPage(page, e.Location) : new PdfPoint();
            dragStartClient = e.Location;
            lastClient = e.Location;
            dragMoved = false;
            dragPage = page;
            dragStartPt = pt;
            lastPt = pt;
            pendingLink = null;

            switch (tool)
            {
                case ToolMode.Hand:
                case ToolMode.Select:
                    {
                        int handle = HitHandle(e.Location);
                        if (handle >= 0)
                        {
                            if (handle >= 10)
                            {
                                drag = DragKind.MoveLineEnd;
                                lineEndIndex = handle == 10 ? 0 : 1;
                            }
                            else
                            {
                                drag = DragKind.ResizeAnnot;
                                resizeHandle = handle;
                                resizeOrigRect = selected.Rect;
                            }
                            dragPage = selected.Page;
                            Capture = true;
                            return;
                        }
                        if (page < 0)
                        {
                            KillFormFocus();
                            Deselect();
                            drag = tool == ToolMode.Hand ? DragKind.Pan : DragKind.None;
                            if (drag == DragKind.Pan) Cursor = Icons.HandClosedCursor;
                            return;
                        }
                        Annotation hit = HitAnnotation(page, pt);
                        if (hit != null)
                        {
                            KillFormFocus();
                            SelectAnnotation(hit, false);
                            drag = DragKind.MoveAnnot;
                            Capture = true;
                            if (e.Clicks >= 2) { drag = DragKind.None; OpenAnnotation(hit); }
                            return;
                        }
                        if (Pdf.FormFieldAt(page, pt.X, pt.Y) >= 0)
                        {
                            Deselect();
                            drag = DragKind.Form;
                            formPage = page;
                            formFocused = true;
                            if (e.Clicks >= 2) Pdf.FormDoubleClick(page, Modifiers(), pt.X, pt.Y);
                            else Pdf.FormMouseDown(page, Modifiers(), pt.X, pt.Y);
                            Capture = true;
                            return;
                        }
                        KillFormFocus();
                        pendingLink = Pdf.GetLinkAt(page, pt.X, pt.Y);
                        Deselect();
                        if (tool == ToolMode.Hand)
                        {
                            drag = DragKind.Pan;
                            Cursor = Icons.HandClosedCursor;
                        }
                        else
                        {
                            drag = DragKind.TextSelect;
                            if (e.Clicks == 2) { SelectWordAt(page, pt); drag = DragKind.None; }
                            else BeginTextSelection(page, pt);
                        }
                        Capture = true;
                        return;
                    }
                case ToolMode.Highlight:
                case ToolMode.Underline:
                case ToolMode.StrikeOut:
                    if (page < 0) return;
                    Deselect();
                    drag = DragKind.TextSelect;
                    BeginTextSelection(page, pt);
                    Capture = true;
                    return;
                case ToolMode.Zoom:
                case ToolMode.Snapshot:
                case ToolMode.Redaction:
                case ToolMode.FreeText:
                case ToolMode.Rectangle:
                case ToolMode.Ellipse:
                    if (page < 0 && tool != ToolMode.Zoom) return;
                    if (page < 0) { drag = DragKind.Click; return; }
                    Deselect();
                    drag = DragKind.Marquee;
                    Capture = true;
                    return;
                case ToolMode.Pencil:
                    if (page < 0) return;
                    Deselect();
                    drag = DragKind.Ink;
                    inkPoints = new List<PdfPoint>();
                    inkPoints.Add(pt);
                    Capture = true;
                    return;
                case ToolMode.Line:
                case ToolMode.Arrow:
                case ToolMode.Measure:
                    if (page < 0) return;
                    Deselect();
                    drag = DragKind.LineDraw;
                    Capture = true;
                    return;
                case ToolMode.Note:
                case ToolMode.Stamp:
                case ToolMode.Signature:
                    if (page < 0) return;
                    drag = DragKind.Click;
                    return;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Pdf == null) return;
            if (drag == DragKind.None || drag == DragKind.Click)
            {
                UpdateCursor(e.Location);
                int hp = PageAtClient(e.Location);
                if (hp >= 0 && Pdf.FormHandle != IntPtr.Zero)
                {
                    PdfPoint hpt = ClientToPage(hp, e.Location);
                    Pdf.FormMouseMove(hp, Modifiers(), hpt.X, hpt.Y);
                }
                return;
            }
            if (Math.Abs(e.X - dragStartClient.X) > 2 || Math.Abs(e.Y - dragStartClient.Y) > 2) dragMoved = true;

            // Automatisches Scrollen am Rand
            Size vp = ViewportSize;
            if (drag != DragKind.Pan)
            {
                int dy = e.Y < 0 ? e.Y : (e.Y > vp.Height ? e.Y - vp.Height : 0);
                int dx = e.X < 0 ? e.X : (e.X > vp.Width ? e.X - vp.Width : 0);
                if (dx != 0 || dy != 0) SetScroll(scrollX + dx, scrollY + dy);
            }

            PdfPoint pt = dragPage >= 0 ? ClientToPage(dragPage, e.Location) : new PdfPoint();
            if (dragPage >= 0) pt = ClampToPage(dragPage, pt);

            switch (drag)
            {
                case DragKind.Pan:
                    SetScroll(scrollX - (e.X - lastClient.X), scrollY - (e.Y - lastClient.Y));
                    break;
                case DragKind.TextSelect:
                    UpdateTextSelection(pt);
                    break;
                case DragKind.Marquee:
                    if ((ModifierKeys & Keys.Shift) != 0 && (tool == ToolMode.Rectangle || tool == ToolMode.Ellipse))
                    {
                        double w = pt.X - dragStartPt.X, h = pt.Y - dragStartPt.Y;
                        double s = Math.Max(Math.Abs(w), Math.Abs(h));
                        pt = new PdfPoint(dragStartPt.X + Math.Sign(w) * s, dragStartPt.Y + Math.Sign(h) * s);
                    }
                    lastPt = pt;
                    Invalidate();
                    break;
                case DragKind.Ink:
                    if (inkPoints.Count == 0 || Distance(inkPoints[inkPoints.Count - 1], pt) * Ppp >= 1.5)
                        inkPoints.Add(pt);
                    lastPt = pt;
                    Invalidate();
                    break;
                case DragKind.LineDraw:
                    if ((ModifierKeys & Keys.Shift) != 0) pt = SnapAngle(dragStartPt, pt);
                    lastPt = pt;
                    Invalidate();
                    break;
                case DragKind.MoveAnnot:
                    if (selected != null && dragMoved)
                    {
                        PdfPoint cur = ClientToPage(selected.Page, e.Location);
                        PdfPoint prev = ClientToPage(selected.Page, lastClient);
                        if (!annotChangeOpen) { session.BeginChange(false, "Kommentar verschieben"); annotChangeOpen = true; }
                        selected.Offset(cur.X - prev.X, cur.Y - prev.Y);
                        RepositionChildren();
                        Invalidate();
                    }
                    break;
                case DragKind.ResizeAnnot:
                    if (selected != null)
                    {
                        if (!annotChangeOpen) { session.BeginChange(false, "Kommentargröße ändern"); annotChangeOpen = true; }
                        ResizeSelected(e.Location);
                        Invalidate();
                    }
                    break;
                case DragKind.MoveLineEnd:
                    if (selected != null && selected.Strokes.Count > 0)
                    {
                        if (!annotChangeOpen) { session.BeginChange(false, "Linie bearbeiten"); annotChangeOpen = true; }
                        List<PdfPoint> s = selected.Strokes[0];
                        PdfPoint p = ClientToPage(selected.Page, e.Location);
                        if (lineEndIndex == 0) s[0] = p; else s[s.Count - 1] = p;
                        Invalidate();
                    }
                    break;
                case DragKind.Form:
                    Pdf.FormMouseMove(formPage, Modifiers(), pt.X, pt.Y);
                    break;
            }
            lastClient = e.Location;
        }

        private bool annotChangeOpen;

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Capture = false;
            if (Pdf == null) return;
            if (e.Button == MouseButtons.Right && !fullScreen)
            {
                int page = PageAtClient(e.Location);
                Annotation hit = null;
                if (page >= 0)
                {
                    hit = HitAnnotation(page, ClientToPage(page, e.Location));
                    if (hit != null) SelectAnnotation(hit, false);
                }
                if (ViewContextMenu != null)
                {
                    ViewContextEventArgs args = new ViewContextEventArgs();
                    args.Location = e.Location;
                    args.Annotation = hit;
                    args.HasTextSelection = HasTextSelection;
                    args.Page = page;
                    ViewContextMenu(this, args);
                }
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            DragKind d = drag;
            drag = DragKind.None;
            int dp = dragPage;
            dragPage = -1;
            PdfPoint pt = dp >= 0 ? ClampToPage(dp, ClientToPage(dp, e.Location)) : new PdfPoint();

            switch (d)
            {
                case DragKind.Pan:
                    if (!dragMoved && pendingLink != null) FollowLink(pendingLink);
                    break;
                case DragKind.TextSelect:
                    if (!dragMoved && pendingLink != null) { ClearTextSelection(); FollowLink(pendingLink); break; }
                    if (tool == ToolMode.Highlight || tool == ToolMode.Underline || tool == ToolMode.StrikeOut)
                        CreateMarkupFromSelection(dp, pt);
                    break;
                case DragKind.Marquee:
                    FinishMarquee(dp, pt);
                    break;
                case DragKind.Click:
                    if (tool == ToolMode.Zoom)
                    {
                        if ((ModifierKeys & (Keys.Control | Keys.Alt)) != 0) ZoomOut(); else ZoomIn();
                    }
                    else if (dp >= 0) CreateClickAnnotation(dp, dragStartPt);
                    break;
                case DragKind.Ink:
                    if (inkPoints != null && inkPoints.Count > 0)
                    {
                        Annotation a = NewAnnotation(AnnotationType.Ink, dp);
                        a.Strokes.Add(SimplifyStroke(inkPoints));
                        a.Rect = a.Bounds;
                        session.AddAnnotation(a);
                    }
                    inkPoints = null;
                    break;
                case DragKind.LineDraw:
                    if (tool == ToolMode.Measure) { Invalidate(); break; }
                    if (Distance(dragStartPt, lastPt) * Ppp > 4)
                    {
                        Annotation a = NewAnnotation(tool == ToolMode.Arrow ? AnnotationType.Arrow : AnnotationType.Line, dp);
                        a.Strokes.Add(new List<PdfPoint> { dragStartPt, lastPt });
                        a.Rect = a.Bounds;
                        session.AddAnnotation(a);
                        SelectAnnotation(a, false);
                        RevertTool();
                    }
                    break;
                case DragKind.MoveAnnot:
                case DragKind.ResizeAnnot:
                case DragKind.MoveLineEnd:
                    if (annotChangeOpen && selected != null)
                    {
                        selected.Modified = DateTime.Now;
                        if (selected.UsesStrokes) selected.Rect = selected.Bounds;
                        session.CommitAnnotationChange();
                    }
                    break;
                case DragKind.Form:
                    if (formPage >= 0)
                    {
                        PdfPoint fp = ClientToPage(formPage, e.Location);
                        Pdf.FormMouseUp(formPage, Modifiers(), fp.X, fp.Y);
                    }
                    break;
            }
            annotChangeOpen = false;
            UpdateCursor(e.Location);
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Pdf == null) return;
            if ((ModifierKeys & Keys.Control) != 0)
            {
                double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
                SetZoom(zoom * factor, ZoomMode.Custom, e.Location);
                return;
            }
            int step = -e.Delta * Math.Max(40, SystemInformation.MouseWheelScrollLines * 20) / 120;
            if ((ModifierKeys & Keys.Shift) != 0) { SetScroll(scrollX + step, scrollY); return; }
            if (!IsContinuous)
            {
                Size vp = ViewportSize;
                bool atBottom = scrollY >= canvas.Height - vp.Height;
                bool atTop = scrollY <= 0;
                if (step > 0 && atBottom) { NextPage(); return; }
                if (step < 0 && atTop)
                {
                    int r = RowOf(currentPage);
                    if (r > 0)
                    {
                        GoToPage(rows[r - 1][0]);
                        SetScroll(scrollX, canvas.Height);
                    }
                    return;
                }
            }
            SetScroll(scrollX, scrollY + step);
        }

        private void UpdateCursor(Point client)
        {
            if (Pdf == null) { Cursor = Cursors.Default; return; }
            if (fullScreen) { Cursor = Cursors.Default; return; }
            if (client.X >= ViewportSize.Width || client.Y >= ViewportSize.Height) { Cursor = Cursors.Default; return; }
            int page = PageAtClient(client);
            PdfPoint pt = page >= 0 ? ClientToPage(page, client) : new PdfPoint();
            switch (tool)
            {
                case ToolMode.Hand:
                case ToolMode.Select:
                    {
                        int h = HitHandle(client);
                        if (h >= 0)
                        {
                            Cursor = h >= 10 ? Cursors.Cross : ((h == 0 || h == 2) ? Cursors.SizeNWSE : Cursors.SizeNESW);
                            return;
                        }
                        if (page >= 0)
                        {
                            if (HitAnnotation(page, pt) != null) { Cursor = Cursors.SizeAll; return; }
                            int field = Pdf.FormFieldAt(page, pt.X, pt.Y);
                            if (field >= 0)
                            {
                                Cursor = field == Native.FPDF_FORMFIELD_TEXTFIELD || field == Native.FPDF_FORMFIELD_COMBOBOX ? Cursors.IBeam : Cursors.Hand;
                                return;
                            }
                            if (Pdf.GetLinkAt(page, pt.X, pt.Y) != null) { Cursor = Cursors.Hand; return; }
                            if (tool == ToolMode.Select)
                            {
                                Cursor = Pdf.CharIndexAt(page, pt.X, pt.Y, 3 / Ppp) >= 0 ? Cursors.IBeam : Cursors.Arrow;
                                return;
                            }
                        }
                        Cursor = tool == ToolMode.Hand ? Icons.HandOpenCursor : Cursors.Arrow;
                        return;
                    }
                case ToolMode.Zoom:
                    Cursor = (ModifierKeys & (Keys.Control | Keys.Alt)) != 0 ? Icons.ZoomOutCursor : Icons.ZoomInCursor;
                    return;
                case ToolMode.Highlight:
                case ToolMode.Underline:
                case ToolMode.StrikeOut:
                    Cursor = Cursors.IBeam;
                    return;
                default:
                    Cursor = page >= 0 ? Cursors.Cross : Cursors.No;
                    return;
            }
        }

        private PdfPoint ClampToPage(int page, PdfPoint p)
        {
            SizeF s = Pdf.GetViewSize(page, 0);
            Affine m = Pdf.GetPageTransform(page, 0);
            Affine inv = m.Invert();
            PdfPoint a = inv.ApplyD(0, 0), b = inv.ApplyD(s.Width, s.Height);
            PdfRect r = new PdfRect(a.X, a.Y, b.X, b.Y);
            return new PdfPoint(Math.Max(r.Left, Math.Min(r.Right, p.X)), Math.Max(r.Bottom, Math.Min(r.Top, p.Y)));
        }

        private static double Distance(PdfPoint a, PdfPoint b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static PdfPoint SnapAngle(PdfPoint a, PdfPoint b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            double ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
            return new PdfPoint(a.X + Math.Cos(ang) * len, a.Y + Math.Sin(ang) * len);
        }

        private List<PdfPoint> SimplifyStroke(List<PdfPoint> pts)
        {
            if (pts.Count < 3) return new List<PdfPoint>(pts);
            List<PdfPoint> result = new List<PdfPoint>();
            result.Add(pts[0]);
            double min = 0.6 / Ppp + 0.3;
            for (int i = 1; i < pts.Count - 1; i++)
                if (Distance(result[result.Count - 1], pts[i]) >= min) result.Add(pts[i]);
            result.Add(pts[pts.Count - 1]);
            return result;
        }

        private void ResizeSelected(Point client)
        {
            Affine m = PageToClient(selected.Page);
            RectangleF orig = m.MapRect(resizeOrigRect);
            PointF fixedCorner;
            switch (resizeHandle)
            {
                case 0: fixedCorner = new PointF(orig.Right, orig.Bottom); break;
                case 1: fixedCorner = new PointF(orig.Left, orig.Bottom); break;
                case 2: fixedCorner = new PointF(orig.Left, orig.Top); break;
                default: fixedCorner = new PointF(orig.Right, orig.Top); break;
            }
            Affine inv = m.Invert();
            PdfPoint a = inv.ApplyD(fixedCorner.X, fixedCorner.Y);
            PdfPoint b = ClampToPage(selected.Page, inv.ApplyD(client.X, client.Y));
            PdfRect nr = new PdfRect(a.X, a.Y, b.X, b.Y);
            if (nr.Width < 6 || nr.Height < 6) return;
            selected.MapRect(selected.Rect, nr);
        }

        // ------------------------------------------------------------------
        // Kommentare erzeugen
        // ------------------------------------------------------------------

        private void RevertTool()
        {
            if (!KeepToolSelected) SetTool(AppSettings.GetBool("DefaultToolSelect", false) ? ToolMode.Select : ToolMode.Hand);
        }

        private void FinishMarquee(int page, PdfPoint end)
        {
            PdfRect r = new PdfRect(dragStartPt.X, dragStartPt.Y, lastPt.X, lastPt.Y);
            double pxW = r.Width * Ppp, pxH = r.Height * Ppp;
            bool isClick = pxW < 4 && pxH < 4;
            switch (tool)
            {
                case ToolMode.Zoom:
                    if (isClick)
                    {
                        if ((ModifierKeys & (Keys.Control | Keys.Alt)) != 0) SetZoom(zoom / 1.5, ZoomMode.Custom, dragStartClient);
                        else SetZoom(zoom * 1.5, ZoomMode.Custom, dragStartClient);
                    }
                    else ZoomToRect(page, r);
                    break;
                case ToolMode.Snapshot:
                    if (!isClick) CopySnapshot(page, r);
                    break;
                case ToolMode.Redaction:
                    if (!isClick)
                    {
                        Annotation a = NewAnnotation(AnnotationType.Redaction, page);
                        a.Rect = r;
                        a.Color = Color.Red;
                        session.AddAnnotation(a);
                    }
                    break;
                case ToolMode.Rectangle:
                case ToolMode.Ellipse:
                    if (!isClick)
                    {
                        Annotation a = NewAnnotation(tool == ToolMode.Rectangle ? AnnotationType.Rectangle : AnnotationType.Ellipse, page);
                        a.Rect = r;
                        session.AddAnnotation(a);
                        SelectAnnotation(a, false);
                        RevertTool();
                    }
                    break;
                case ToolMode.FreeText:
                    {
                        Annotation a = NewAnnotation(AnnotationType.FreeText, page);
                        if (isClick || pxW < 20 || pxH < 12)
                        {
                            double w = 180, h = a.FontSize * AnnotationPainter.LineFactor + 2 * AnnotationPainter.FreeTextPadding + 2;
                            Affine inv = PageToClient(page).Invert();
                            PdfPoint p1 = inv.ApplyD(dragStartClient.X, dragStartClient.Y);
                            PdfPoint p2 = inv.ApplyD(dragStartClient.X + w * Ppp, dragStartClient.Y + h * Ppp);
                            r = new PdfRect(p1.X, p1.Y, p2.X, p2.Y);
                        }
                        a.Rect = r;
                        session.AddAnnotation(a);
                        SelectAnnotation(a, false);
                        RevertTool();
                        BeginInlineEdit(a, true);
                        break;
                    }
            }
            Invalidate();
        }

        private void CreateClickAnnotation(int page, PdfPoint pt)
        {
            Affine m = PageToClient(page);
            Affine inv = m.Invert();
            PointF c = m.Apply(pt.X, pt.Y);
            switch (tool)
            {
                case ToolMode.Note:
                    {
                        Annotation a = NewAnnotation(AnnotationType.Note, page);
                        double s = 20;
                        PdfPoint p2 = inv.ApplyD(c.X + s * Ppp, c.Y + s * Ppp);
                        a.Rect = new PdfRect(pt.X, pt.Y, p2.X, p2.Y);
                        session.AddAnnotation(a);
                        SelectAnnotation(a, false);
                        NotePopup popup = OpenPopup(a);
                        if (popup != null) popup.FocusText();
                        RevertTool();
                        break;
                    }
                case ToolMode.Stamp:
                    {
                        Annotation a = NewAnnotation(AnnotationType.Stamp, page);
                        a.Text = StampText;
                        a.Color = StampColor;
                        double w = Math.Max(110, PdfObjects.MeasureWidth(StampText, PdfObjects.HelveticaBold, 22) + 36), h = 42;
                        PdfPoint p1 = inv.ApplyD(c.X - w * Ppp / 2, c.Y - h * Ppp / 2);
                        PdfPoint p2 = inv.ApplyD(c.X + w * Ppp / 2, c.Y + h * Ppp / 2);
                        a.Rect = new PdfRect(p1.X, p1.Y, p2.X, p2.Y);
                        session.AddAnnotation(a);
                        SelectAnnotation(a, false);
                        RevertTool();
                        break;
                    }
                case ToolMode.Signature:
                    {
                        if (SignatureTemplate == null) return;
                        Annotation a = SignatureTemplate.Clone();
                        a.Id = Guid.NewGuid().ToString("N");
                        a.Page = page;
                        a.Author = AppSettings.Author;
                        a.Created = a.Modified = DateTime.Now;
                        a.Rotation = Pdf.GetPageRotation(page);
                        // Vorlage liegt im Bereich (0,0)-(w,h); auf Klickposition zentrieren
                        PdfRect tr = a.Bounds;
                        double w = tr.Width, h = tr.Height;
                        PdfPoint p1 = inv.ApplyD(c.X - w * Ppp / 2, c.Y - h * Ppp / 2);
                        PdfPoint p2 = inv.ApplyD(c.X + w * Ppp / 2, c.Y + h * Ppp / 2);
                        PdfRect target = new PdfRect(p1.X, p1.Y, p2.X, p2.Y);
                        if (a.Strokes.Count > 0)
                        {
                            // Striche aufrecht drehen, falls die Seite gedreht ist
                            Affine local = new Affine(1, 0, 0, -1, 0, h); // Vorlage: y nach oben
                            Annotation tmp = new Annotation();
                            tmp.Rotation = a.Rotation;
                            tmp.Rect = target;
                            Affine l2p = tmp.LocalToPage;
                            foreach (List<PdfPoint> s in a.Strokes)
                                for (int i = 0; i < s.Count; i++)
                                {
                                    PdfPoint lp = local.ApplyD(s[i].X - tr.Left, s[i].Y - tr.Bottom);
                                    s[i] = l2p.ApplyD(lp.X, lp.Y);
                                }
                        }
                        a.Rect = target;
                        session.AddAnnotation(a);
                        SelectAnnotation(a, false);
                        RevertTool();
                        break;
                    }
            }
        }

        private void CreateMarkupFromSelection(int page, PdfPoint end)
        {
            AnnotationType type = tool == ToolMode.Highlight ? AnnotationType.Highlight :
                tool == ToolMode.Underline ? AnnotationType.Underline : AnnotationType.StrikeOut;
            List<PdfRect> rects = new List<PdfRect>(selRects);
            if (rects.Count == 0 && type == AnnotationType.Highlight && page >= 0)
            {
                PdfRect r = new PdfRect(dragStartPt.X, dragStartPt.Y, end.X, end.Y);
                if (r.Width * Ppp > 4 && r.Height * Ppp > 4) rects.Add(r);
            }
            if (rects.Count == 0 || page < 0) { ClearTextSelection(); return; }
            Annotation a = NewAnnotation(type, page);
            a.Quads.AddRange(MergeRects(rects));
            a.Rect = a.Bounds;
            a.Contents = selAnchor >= 0 ? SelectedText : string.Empty;
            ClearTextSelection();
            session.AddAnnotation(a);
        }

        /// <summary>Fügt Zeichenrechtecke derselben Zeile zusammen.</summary>
        private static List<PdfRect> MergeRects(List<PdfRect> rects)
        {
            List<PdfRect> result = new List<PdfRect>();
            foreach (PdfRect r in rects)
            {
                bool merged = false;
                for (int i = 0; i < result.Count; i++)
                {
                    PdfRect o = result[i];
                    double overlap = Math.Min(o.Top, r.Top) - Math.Max(o.Bottom, r.Bottom);
                    if (overlap > 0.6 * Math.Min(o.Height, r.Height) && r.Left - o.Right < r.Height * 1.5 && o.Left - r.Right < r.Height * 1.5)
                    {
                        result[i] = o.Union(r);
                        merged = true;
                        break;
                    }
                }
                if (!merged) result.Add(r);
            }
            return result;
        }

        private void CopySnapshot(int page, PdfRect r)
        {
            RectangleF dev = PageToCanvas(page).MapRect(r);
            Rectangle pr = pageRects[page];
            Rectangle region = Rectangle.Round(new RectangleF(dev.X - pr.X, dev.Y - pr.Y, dev.Width, dev.Height));
            region.Intersect(new Rectangle(0, 0, pr.Width, pr.Height));
            if (region.Width < 2 || region.Height < 2) return;
            using (Bitmap bmp = Pdf.Render(page, rotation, pr.Size, region, false))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    Affine m = PageToCanvas(page).Translate(-pr.X - region.X, -pr.Y - region.Y);
                    foreach (Annotation a in session.AnnotationsOnPage(page)) AnnotationPainter.Paint(g, a, m);
                }
                try
                {
                    Clipboard.SetImage(bmp);
                    RaiseStatus("Der ausgewählte Bereich wurde kopiert.");
                    MessageBox.Show(FindForm(), "Der ausgewählte Bereich wurde kopiert.", "AcroClone", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(FindForm(), "Kopieren nicht möglich: " + ex.Message, "AcroClone", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // ------------------------------------------------------------------
        // Auswahl von Kommentaren
        // ------------------------------------------------------------------

        public void SelectAnnotation(Annotation a, bool scrollIntoView)
        {
            if (a != null) ClearTextSelection();
            if (selected == a)
            {
                if (a != null && scrollIntoView) EnsureVisible(a.Page, a.Bounds);
                return;
            }
            selected = a;
            if (a != null && scrollIntoView) EnsureVisible(a.Page, a.Bounds);
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        public void Deselect()
        {
            if (selected == null) return;
            selected = null;
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        public void DeleteSelected()
        {
            if (selected == null) return;
            Annotation a = selected;
            ClosePopup(a.Id);
            selected = null;
            session.RemoveAnnotations(new Annotation[] { a });
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        /// <summary>Doppelklick: Textfeld bearbeiten bzw. Pop-up öffnen.</summary>
        public void OpenAnnotation(Annotation a)
        {
            if (a.Type == AnnotationType.FreeText) BeginInlineEdit(a, false);
            else
            {
                NotePopup p = OpenPopup(a);
                if (p != null) p.FocusText();
            }
        }

        // ------------------------------------------------------------------
        // Pop-up-Notizen
        // ------------------------------------------------------------------

        public NotePopup OpenPopup(Annotation a)
        {
            NotePopup p;
            if (popups.TryGetValue(a.Id, out p)) { p.BringToFront(); return p; }
            p = new NotePopup(session, a);
            p.CloseRequested += delegate(object s, EventArgs e) { ClosePopup(((NotePopup)s).AnnotationId); };
            popups[a.Id] = p;
            Controls.Add(p);
            p.BringToFront();
            RepositionChildren();
            return p;
        }

        public void ClosePopup(string id)
        {
            NotePopup p;
            if (!popups.TryGetValue(id, out p)) return;
            popups.Remove(id);
            p.Commit();
            Controls.Remove(p);
            p.Dispose();
            Focus();
        }

        public void CloseAllPopups()
        {
            foreach (string id in popups.Keys.ToList()) ClosePopup(id);
        }

        private void RepositionChildren()
        {
            if (session == null) return;
            Size vp = ViewportSize;
            foreach (NotePopup p in popups.Values)
            {
                Annotation a = session.FindAnnotation(p.AnnotationId);
                if (a == null || a.Page >= pageRects.Length || pageRects[a.Page].IsEmpty) { p.Visible = false; continue; }
                RectangleF r = PageToClient(a.Page).MapRect(a.Bounds);
                Rectangle pr = pageRects[a.Page];
                int x = (int)r.Right + 12;
                if (x + p.Width > pr.Right - scrollX + 60) x = (int)r.Left - p.Width - 12;
                int y = (int)r.Top;
                p.Location = new Point(x + p.UserOffset.X, y + p.UserOffset.Y);
                p.Visible = true;
            }
            if (inlineEditor != null && inlineAnnotation != null)
            {
                RectangleF r = PageToClient(inlineAnnotation.Page).MapRect(inlineAnnotation.Rect);
                inlineEditor.Bounds = Rectangle.Round(r);
            }
        }

        // ------------------------------------------------------------------
        // Textfeld direkt bearbeiten
        // ------------------------------------------------------------------

        public void BeginInlineEdit(Annotation a, bool isNew)
        {
            CommitInlineEditor();
            inlineAnnotation = a;
            inlineIsNew = isNew;
            inlineEditor = new TextBox();
            inlineEditor.Multiline = true;
            inlineEditor.BorderStyle = BorderStyle.FixedSingle;
            inlineEditor.AcceptsReturn = true;
            float px = (float)Math.Max(6, a.FontSize * Ppp);
            inlineEditor.Font = new Font("Arial", px, FontStyle.Regular, GraphicsUnit.Pixel);
            inlineEditor.Text = (a.Text ?? "").Replace("\n", "\r\n");
            inlineEditor.BackColor = a.FillColor.A > 0 ? a.FillColor : Color.White;
            inlineEditor.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.Handled = true; CommitInlineEditor(); }
            };
            TextBox thisEditor = inlineEditor;
            inlineEditor.Leave += delegate
            {
                BeginInvoke(new MethodInvoker(delegate { if (inlineEditor == thisEditor && !thisEditor.Focused) CommitInlineEditor(); }));
            };
            Controls.Add(inlineEditor);
            RepositionChildren();
            inlineEditor.BringToFront();
            inlineEditor.Focus();
            inlineEditor.SelectionStart = inlineEditor.TextLength;
            BeginInvoke(new MethodInvoker(delegate { if (inlineEditor != null && !inlineEditor.Focused) inlineEditor.Focus(); }));
            Invalidate();
        }

        public void CommitInlineEditor()
        {
            if (inlineEditor == null) return;
            TextBox editor = inlineEditor;
            Annotation a = inlineAnnotation;
            inlineEditor = null;
            inlineAnnotation = null;
            string text = editor.Text.Replace("\r\n", "\n").TrimEnd();
            Controls.Remove(editor);
            editor.Dispose();
            if (a == null || session == null) return;
            if (text.Length == 0)
            {
                if (inlineIsNew && session.Annotations.Contains(a))
                {
                    session.Annotations.Remove(a);
                    selected = null;
                    session.RaiseAnnotationsChanged();
                }
                Invalidate();
                return;
            }
            if (text != a.Text)
            {
                if (!inlineIsNew) session.BeginChange(false, "Textfeld bearbeiten");
                a.Text = text;
                a.Modified = DateTime.Now;
                // Höhe an den Inhalt anpassen
                SizeF ls = a.LocalSize;
                int lines = AnnotationPainter.FreeTextLines(a, ls.Width).Count;
                double needed = lines * a.FontSize * AnnotationPainter.LineFactor + 2 * AnnotationPainter.FreeTextPadding;
                if (needed > ls.Height + 0.5 && (a.Rotation & 3) == 0)
                    a.Rect = new PdfRect(a.Rect.Left, a.Rect.Top - needed, a.Rect.Right, a.Rect.Top);
                session.CommitAnnotationChange();
            }
            Invalidate();
            if (!Focused && FindForm() != null && FindForm().ContainsFocus == false) return;
        }

        private void CancelInlineEditor(bool silent)
        {
            if (inlineEditor == null) return;
            Controls.Remove(inlineEditor);
            inlineEditor.Dispose();
            inlineEditor = null;
            inlineAnnotation = null;
        }

        // ------------------------------------------------------------------
        // Textauswahl
        // ------------------------------------------------------------------

        public bool HasTextSelection { get { return selPage >= 0 && selAnchor >= 0 && selFocus >= 0; } }

        public string SelectedText
        {
            get
            {
                if (!HasTextSelection) return string.Empty;
                int s = Math.Min(selAnchor, selFocus), e = Math.Max(selAnchor, selFocus);
                return Pdf.GetText(selPage, s, e - s + 1);
            }
        }

        public void ClearTextSelection()
        {
            bool had = selRects.Count > 0 || selPage >= 0;
            selPage = selAnchor = selFocus = -1;
            selRects = new List<PdfRect>();
            if (had) Invalidate();
        }

        private void BeginTextSelection(int page, PdfPoint pt)
        {
            ClearTextSelection();
            selPage = page;
            selAnchor = Pdf.CharIndexAt(page, pt.X, pt.Y, 4 / Ppp);
            selFocus = -1;
        }

        private void UpdateTextSelection(PdfPoint pt)
        {
            if (selPage < 0) return;
            int idx = Pdf.CharIndexAt(selPage, pt.X, pt.Y, 6 / Ppp);
            if (idx < 0) idx = NearestCharIndex(selPage, pt);
            if (idx < 0) return;
            if (selAnchor < 0) selAnchor = idx;
            selFocus = idx;
            int s = Math.Min(selAnchor, selFocus), e = Math.Max(selAnchor, selFocus);
            selRects = Pdf.GetTextRects(selPage, s, e - s + 1);
            Invalidate();
        }

        /// <summary>Sucht das nächstgelegene Zeichen, wenn der Mauszeiger zwischen Zeilen steht.</summary>
        private int NearestCharIndex(int page, PdfPoint pt)
        {
            for (double tol = 12; tol <= 48; tol *= 2)
            {
                int idx = Pdf.CharIndexAt(page, pt.X, pt.Y, tol);
                if (idx >= 0) return idx;
            }
            return -1;
        }

        private void SelectWordAt(int page, PdfPoint pt)
        {
            int idx = Pdf.CharIndexAt(page, pt.X, pt.Y, 4 / Ppp);
            if (idx < 0) return;
            int n = Pdf.CountChars(page);
            int s = idx, e = idx;
            while (s > 0 && char.IsLetterOrDigit(Pdf.GetChar(page, s - 1))) s--;
            while (e < n - 1 && char.IsLetterOrDigit(Pdf.GetChar(page, e + 1))) e++;
            selPage = page;
            selAnchor = s;
            selFocus = e;
            selRects = Pdf.GetTextRects(page, s, e - s + 1);
            Invalidate();
        }

        public void SelectAllText()
        {
            if (Pdf == null) return;
            int n = Pdf.CountChars(currentPage);
            if (n <= 0) return;
            selPage = currentPage;
            selAnchor = 0;
            selFocus = n - 1;
            selRects = Pdf.GetTextRects(currentPage, 0, n);
            Invalidate();
        }

        public bool CopySelectedText()
        {
            string t = SelectedText;
            if (string.IsNullOrEmpty(t)) return false;
            try { Clipboard.SetText(t); return true; }
            catch (Exception) { return false; }
        }

        /// <summary>Erzeugt aus der aktuellen Textauswahl eine Markierung des angegebenen Typs.</summary>
        public void MarkupSelection(AnnotationType type)
        {
            if (!HasTextSelection) return;
            ToolMode old = tool;
            tool = type == AnnotationType.Highlight ? ToolMode.Highlight : type == AnnotationType.Underline ? ToolMode.Underline : ToolMode.StrikeOut;
            CreateMarkupFromSelection(selPage, new PdfPoint());
            tool = old;
        }

        // ------------------------------------------------------------------
        // Suche
        // ------------------------------------------------------------------

        public void ShowSearchMatch(TextMatch m)
        {
            if (m == null || Pdf == null) { ClearSearchHighlight(); return; }
            searchPage = m.Page;
            searchRects = Pdf.GetTextRects(m.Page, m.Start, m.Count);
            if (searchRects.Count > 0)
            {
                PdfRect u = searchRects[0];
                foreach (PdfRect r in searchRects) u = u.Union(r);
                EnsureVisible(m.Page, u);
            }
            else GoToPage(m.Page);
            Invalidate();
        }

        public void ClearSearchHighlight()
        {
            searchPage = -1;
            searchRects = new List<PdfRect>();
            Invalidate();
        }

        // ------------------------------------------------------------------
        // Tastatur
        // ------------------------------------------------------------------

        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            if (k == Keys.Up || k == Keys.Down || k == Keys.Left || k == Keys.Right || k == Keys.Tab ||
                k == Keys.PageUp || k == Keys.PageDown || k == Keys.Home || k == Keys.End)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override bool IsInputChar(char charCode)
        {
            // Zeichen gehören dem Formularfeld bzw. der Ansicht – nicht den Menü-Kürzeln.
            return true;
        }

        private void KillFormFocus()
        {
            if (formFocused && Pdf != null) Pdf.KillFormFocus();
            formFocused = false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Pdf == null) return;
            if (formFocused && formPage >= 0)
            {
                int mods = (e.Shift ? Native.FWL_EVENTFLAG_ShiftKey : 0) | (e.Control ? Native.FWL_EVENTFLAG_ControlKey : 0) | (e.Alt ? Native.FWL_EVENTFLAG_AltKey : 0);
                if (e.KeyCode == Keys.Escape) { KillFormFocus(); e.Handled = true; return; }
                Pdf.FormKeyDown(formPage, (int)e.KeyCode, mods);
                e.Handled = true;
                return;
            }
            if (fullScreen)
            {
                switch (e.KeyCode)
                {
                    case Keys.Escape: if (FullScreenExitRequested != null) FullScreenExitRequested(this, EventArgs.Empty); break;
                    case Keys.Right: case Keys.Down: case Keys.PageDown: case Keys.Space: case Keys.Enter: NextPage(); break;
                    case Keys.Left: case Keys.Up: case Keys.PageUp: case Keys.Back: PreviousPage(); break;
                    case Keys.Home: FirstPage(); break;
                    case Keys.End: LastPage(); break;
                }
                e.Handled = true;
                return;
            }
            Size vp = ViewportSize;
            switch (e.KeyCode)
            {
                case Keys.Delete:
                    if (selected != null) { DeleteSelected(); e.Handled = true; }
                    break;
                case Keys.Escape:
                    Deselect();
                    ClearTextSelection();
                    if (drag != DragKind.None) { drag = DragKind.None; dragPage = -1; Capture = false; Invalidate(); }
                    break;
                case Keys.Down: ScrollBy(0, 40); break;
                case Keys.Up: ScrollBy(0, -40); break;
                case Keys.Left:
                    if (!IsContinuous && !hbar.Visible) PreviousPage(); else ScrollBy(-40, 0);
                    break;
                case Keys.Right:
                    if (!IsContinuous && !hbar.Visible) NextPage(); else ScrollBy(40, 0);
                    break;
                case Keys.PageDown:
                    if (!IsContinuous && scrollY >= canvas.Height - vp.Height) NextPage();
                    else ScrollBy(0, vp.Height - 30);
                    break;
                case Keys.PageUp:
                    if (!IsContinuous && scrollY <= 0) PreviousPage();
                    else ScrollBy(0, -(vp.Height - 30));
                    break;
                case Keys.Space:
                    ScrollBy(0, e.Shift ? -(vp.Height - 30) : vp.Height - 30);
                    break;
                case Keys.Home: FirstPage(); break;
                case Keys.End: LastPage(); break;
                case Keys.H: if (!e.Control && !e.Alt) SetTool(ToolMode.Hand); break;
                case Keys.V: if (!e.Control && !e.Alt) SetTool(ToolMode.Select); break;
                case Keys.Z: if (!e.Control && !e.Alt) SetTool(ToolMode.Zoom); break;
                case Keys.S: if (!e.Control && !e.Alt) SetTool(ToolMode.Note); break;
                case Keys.U: if (!e.Control && !e.Alt) SetTool(ToolMode.Highlight); break;
                case Keys.ControlKey:
                case Keys.Menu:
                    UpdateCursor(PointToClient(MousePosition));
                    break;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu) UpdateCursor(PointToClient(MousePosition));
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (Pdf == null || !formFocused || formPage < 0) return;
            int mods = (ModifierKeys & Keys.Shift) != 0 ? Native.FWL_EVENTFLAG_ShiftKey : 0;
            Pdf.FormChar(formPage, e.KeyChar, mods);
            e.Handled = true;
        }

        public bool FormHasFocus { get { return formFocused; } }

        // ------------------------------------------------------------------
        // Hilfen
        // ------------------------------------------------------------------

        private void RaiseCurrentPageChanged()
        {
            if (CurrentPageChanged != null) CurrentPageChanged(this, EventArgs.Empty);
        }

        private void RaiseZoomChanged()
        {
            if (ZoomChanged != null) ZoomChanged(this, EventArgs.Empty);
        }

        private void RaiseStatus(string text)
        {
            if (StatusMessage != null) StatusMessage(this, new StatusEventArgs(text));
        }
    }

    public class StatusEventArgs : EventArgs
    {
        public string Text { get; private set; }
        public StatusEventArgs(string text) { Text = text; }
    }
}
