using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AcroClone.Pdf
{
    public class PdfException : Exception
    {
        public uint ErrorCode { get; private set; }

        public PdfException(string message, uint code) : base(message)
        {
            ErrorCode = code;
        }

        public static string Describe(uint code)
        {
            switch ((int)code)
            {
                case Native.FPDF_ERR_FILE: return "Die Datei wurde nicht gefunden oder konnte nicht geöffnet werden.";
                case Native.FPDF_ERR_FORMAT: return "Die Datei ist keine gültige PDF-Datei oder ist beschädigt.";
                case Native.FPDF_ERR_PASSWORD: return "Für dieses Dokument ist ein Kennwort erforderlich.";
                case Native.FPDF_ERR_SECURITY: return "Das Sicherheitsverfahren dieses Dokuments wird nicht unterstützt.";
                default: return "Unbekannter Fehler beim Öffnen des Dokuments.";
            }
        }
    }

    public class BookmarkNode
    {
        public string Title;
        public int PageIndex = -1;
        public double? Y;
        public string Uri;
        public List<BookmarkNode> Children = new List<BookmarkNode>();
    }

    public class LinkTarget
    {
        public int PageIndex = -1;
        public double? Y;
        public string Uri;
    }

    public class TextMatch
    {
        public int Page;
        public int Start;
        public int Count;
        public string Context;
        public int ContextMatchStart;
    }

    public class AttachmentInfo
    {
        public int Index;
        public string Name;
        public long Size;
    }

    /// <summary>
    /// Kapselt ein PDFium-Dokument: Laden, Speichern, Rendern, Text, Navigation,
    /// Seitenoperationen und die Formular-Umgebung.
    /// </summary>
    public sealed class PdfDocument : IDisposable
    {
        private IntPtr doc;
        private IntPtr memory;
        private readonly Dictionary<int, IntPtr> pages = new Dictionary<int, IntPtr>();
        private readonly Dictionary<int, IntPtr> textPages = new Dictionary<int, IntPtr>();
        private readonly Dictionary<long, Affine> transforms = new Dictionary<long, Affine>();
        private SizeF[] sizes;
        private FormEnvironment form;

        public event EventHandler<PageEventArgs> FormInvalidated;
        public event EventHandler FormChanged;
        public event EventHandler<LinkEventArgs> FormAction;

        private PdfDocument(IntPtr doc, IntPtr memory)
        {
            this.doc = doc;
            this.memory = memory;
        }

        public IntPtr Handle { get { return doc; } }

        public static PdfDocument Load(byte[] data, string password)
        {
            Native.EnsureInitialized();
            IntPtr mem = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, mem, data.Length);
            IntPtr d = Native.FPDF_LoadMemDocument64(mem, new UIntPtr((ulong)data.Length), password);
            if (d == IntPtr.Zero)
            {
                uint err = Native.FPDF_GetLastError();
                Marshal.FreeHGlobal(mem);
                throw new PdfException(PdfException.Describe(err), err);
            }
            return new PdfDocument(d, mem);
        }

        public static PdfDocument CreateNew()
        {
            Native.EnsureInitialized();
            IntPtr d = Native.FPDF_CreateNewDocument();
            if (d == IntPtr.Zero) throw new PdfException("Neues Dokument konnte nicht erstellt werden.", 1);
            return new PdfDocument(d, IntPtr.Zero);
        }

        public void Dispose()
        {
            if (doc == IntPtr.Zero) return;
            ClosePages();
            if (form != null)
            {
                form.Dispose();
                form = null;
            }
            Native.FPDF_CloseDocument(doc);
            doc = IntPtr.Zero;
            if (memory != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(memory);
                memory = IntPtr.Zero;
            }
        }

        // ------------------------------------------------------------------
        // Formulare
        // ------------------------------------------------------------------

        public bool HasForm
        {
            get { return Native.FPDF_GetFormType(doc) != Native.FORMTYPE_NONE; }
        }

        /// <summary>Aktiviert die interaktive Formular-Umgebung (falls das Dokument Formularfelder enthält).</summary>
        public void EnableForms()
        {
            if (form != null || !HasForm) return;
            ClosePages();
            form = new FormEnvironment(this);
        }

        public IntPtr FormHandle { get { return form == null ? IntPtr.Zero : form.Handle; } }

        public void SetFormHighlight(bool on)
        {
            if (form == null) return;
            if (on)
            {
                // COLORREF 0x00BBGGRR – helles Blau wie in Acrobat
                Native.FPDF_SetFormFieldHighlightColor(form.Handle, 0, 0x00FFDCC8);
                Native.FPDF_SetFormFieldHighlightAlpha(form.Handle, 110);
            }
            else
            {
                Native.FPDF_RemoveFormFieldHighlight(form.Handle);
            }
        }

        public void KillFormFocus()
        {
            if (form != null) Native.FORM_ForceToKillFocus(form.Handle);
        }

        internal void RaiseFormInvalidated(IntPtr page)
        {
            int index = -1;
            foreach (KeyValuePair<int, IntPtr> kv in pages)
                if (kv.Value == page) { index = kv.Key; break; }
            if (FormInvalidated != null) FormInvalidated(this, new PageEventArgs(index));
        }

        internal void RaiseFormChanged()
        {
            if (FormChanged != null) FormChanged(this, EventArgs.Empty);
        }

        internal void RaiseFormAction(LinkTarget target)
        {
            if (FormAction != null) FormAction(this, new LinkEventArgs(target));
        }

        // ------------------------------------------------------------------
        // Seiten
        // ------------------------------------------------------------------

        public int PageCount
        {
            get { return Native.FPDF_GetPageCount(doc); }
        }

        public IntPtr GetPage(int index)
        {
            IntPtr p;
            if (pages.TryGetValue(index, out p)) return p;
            p = Native.FPDF_LoadPage(doc, index);
            if (p == IntPtr.Zero) throw new PdfException("Seite " + (index + 1) + " konnte nicht geladen werden.", 1);
            if (form != null) Native.FORM_OnAfterLoadPage(p, form.Handle);
            pages[index] = p;
            return p;
        }

        /// <summary>Schließt alle geladenen Seiten und verwirft gecachte Geometrien.</summary>
        public void ClosePages()
        {
            foreach (IntPtr tp in textPages.Values) Native.FPDFText_ClosePage(tp);
            textPages.Clear();
            foreach (IntPtr p in pages.Values)
            {
                if (form != null) Native.FORM_OnBeforeClosePage(p, form.Handle);
                Native.FPDF_ClosePage(p);
            }
            pages.Clear();
            transforms.Clear();
            sizes = null;
        }

        /// <summary>Seitengröße in Punkt, inklusive /Rotate des Dokuments.</summary>
        public SizeF GetPageSize(int index)
        {
            if (sizes == null || sizes.Length != PageCount)
            {
                int n = PageCount;
                sizes = new SizeF[n];
                for (int i = 0; i < n; i++)
                {
                    Native.FS_SIZEF s;
                    if (Native.FPDF_GetPageSizeByIndexF(doc, i, out s) != 0)
                        sizes[i] = new SizeF(s.width, s.height);
                    else
                        sizes[i] = new SizeF(612, 792);
                }
            }
            return sizes[index];
        }

        public int GetPageRotation(int index)
        {
            return Native.FPDFPage_GetRotation(GetPage(index));
        }

        public string GetPageLabel(int index)
        {
            return Native.ReadUtf16(delegate(byte[] b, uint l) { return Native.FPDF_GetPageLabel(doc, index, b, l); });
        }

        /// <summary>
        /// Liefert die Transformation von Seitenkoordinaten (Punkt) in Ansichtskoordinaten
        /// (Punkt, Ursprung oben links, y nach unten) unter Berücksichtigung der Ansichtsdrehung (0..3).
        /// </summary>
        public Affine GetPageTransform(int index, int viewRotation)
        {
            long key = (long)index * 4 + (viewRotation & 3);
            Affine t;
            if (transforms.TryGetValue(key, out t)) return t;

            IntPtr page = GetPage(index);
            SizeF size = GetPageSize(index);
            double w = size.Width, h = size.Height;
            if ((viewRotation & 1) == 1) { double tmp = w; w = h; h = tmp; }
            const double k = 100.0;
            int sx = (int)Math.Round(w * k), sy = (int)Math.Round(h * k);
            int x0, y0, x1, y1, x2, y2;
            Native.FPDF_PageToDevice(page, 0, 0, sx, sy, viewRotation, 0, 0, out x0, out y0);
            Native.FPDF_PageToDevice(page, 0, 0, sx, sy, viewRotation, 100, 0, out x1, out y1);
            Native.FPDF_PageToDevice(page, 0, 0, sx, sy, viewRotation, 0, 100, out x2, out y2);
            t = new Affine(
                (x1 - x0) / (100.0 * k), (y1 - y0) / (100.0 * k),
                (x2 - x0) / (100.0 * k), (y2 - y0) / (100.0 * k),
                x0 / k, y0 / k);
            // Kleine Rundungsfehler bei den linearen Anteilen glätten
            t.A = Snap(t.A); t.B = Snap(t.B); t.C = Snap(t.C); t.D = Snap(t.D);
            transforms[key] = t;
            return t;
        }

        private static double Snap(double v)
        {
            double r = Math.Round(v);
            return Math.Abs(v - r) < 0.002 ? r : v;
        }

        /// <summary>Gedrehte Seitengröße für die Ansicht.</summary>
        public SizeF GetViewSize(int index, int viewRotation)
        {
            SizeF s = GetPageSize(index);
            return (viewRotation & 1) == 1 ? new SizeF(s.Height, s.Width) : s;
        }

        // ------------------------------------------------------------------
        // Rendering
        // ------------------------------------------------------------------

        /// <summary>
        /// Rendert einen Ausschnitt einer Seite. fullSize ist die Gesamtgröße der Seite in Pixeln,
        /// region der gewünschte Ausschnitt innerhalb dieser Fläche.
        /// </summary>
        public Bitmap Render(int index, int viewRotation, Size fullSize, Rectangle region, bool printing)
        {
            IntPtr page = GetPage(index);
            Bitmap bmp = new Bitmap(Math.Max(1, region.Width), Math.Max(1, region.Height), PixelFormat.Format32bppPArgb);
            BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                IntPtr fb = Native.FPDFBitmap_CreateEx(bmp.Width, bmp.Height, Native.FPDFBitmap_BGRA, data.Scan0, data.Stride);
                Native.FPDFBitmap_FillRect(fb, 0, 0, bmp.Width, bmp.Height, 0xFFFFFFFF);
                int flags = Native.FPDF_ANNOT | (printing ? Native.FPDF_PRINTING : 0);
                Native.FPDF_RenderPageBitmap(fb, page, -region.X, -region.Y, fullSize.Width, fullSize.Height, viewRotation, flags);
                if (form != null && !printing)
                    Native.FPDF_FFLDraw(form.Handle, fb, page, -region.X, -region.Y, fullSize.Width, fullSize.Height, viewRotation, flags);
                Native.FPDFBitmap_Destroy(fb);
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return bmp;
        }

        /// <summary>Rendert eine komplette Seite mit der angegebenen Pixelgröße.</summary>
        public Bitmap RenderPage(int index, int viewRotation, int width, int height, bool printing)
        {
            Size full = new Size(width, height);
            return Render(index, viewRotation, full, new Rectangle(0, 0, width, height), printing);
        }

        // ------------------------------------------------------------------
        // Text
        // ------------------------------------------------------------------

        public IntPtr GetTextPage(int index)
        {
            IntPtr tp;
            if (textPages.TryGetValue(index, out tp)) return tp;
            tp = Native.FPDFText_LoadPage(GetPage(index));
            textPages[index] = tp;
            return tp;
        }

        public int CountChars(int index)
        {
            return Native.FPDFText_CountChars(GetTextPage(index));
        }

        public int CharIndexAt(int index, double x, double y, double tolerance)
        {
            return Native.FPDFText_GetCharIndexAtPos(GetTextPage(index), x, y, tolerance, tolerance);
        }

        public char GetChar(int index, int charIndex)
        {
            return (char)Native.FPDFText_GetUnicode(GetTextPage(index), charIndex);
        }

        public PdfRect GetCharBox(int index, int charIndex)
        {
            double l, r, b, t;
            Native.FPDFText_GetCharBox(GetTextPage(index), charIndex, out l, out r, out b, out t);
            return new PdfRect(l, b, r, t);
        }

        public string GetText(int index, int start, int count)
        {
            if (count <= 0) return string.Empty;
            IntPtr tp = GetTextPage(index);
            byte[] buffer = new byte[(count + 1) * 2];
            int written = Native.FPDFText_GetText(tp, start, count, buffer);
            if (written <= 0) return string.Empty;
            string s = Encoding.Unicode.GetString(buffer, 0, Math.Max(0, (written - 1) * 2));
            return s.Replace("\0", "").Replace("￾", "").Replace("\u0002", "");
        }

        public string GetPageText(int index)
        {
            return GetText(index, 0, CountChars(index));
        }

        public List<PdfRect> GetTextRects(int index, int start, int count)
        {
            List<PdfRect> list = new List<PdfRect>();
            if (count <= 0) return list;
            IntPtr tp = GetTextPage(index);
            int n = Native.FPDFText_CountRects(tp, start, count);
            for (int i = 0; i < n; i++)
            {
                double l, t, r, b;
                if (Native.FPDFText_GetRect(tp, i, out l, out t, out r, out b) != 0)
                    list.Add(new PdfRect(l, b, r, t));
            }
            return list;
        }

        /// <summary>Sucht alle Vorkommen eines Begriffs auf einer Seite.</summary>
        public List<TextMatch> FindOnPage(int index, string query, bool matchCase, bool wholeWord)
        {
            List<TextMatch> result = new List<TextMatch>();
            if (string.IsNullOrEmpty(query)) return result;
            IntPtr tp = GetTextPage(index);
            uint flags = 0;
            if (matchCase) flags |= Native.FPDF_MATCHCASE;
            if (wholeWord) flags |= Native.FPDF_MATCHWHOLEWORD;
            IntPtr h = Native.FPDFText_FindStart(tp, query, flags, 0);
            if (h == IntPtr.Zero) return result;
            try
            {
                int total = -1;
                while (Native.FPDFText_FindNext(h) != 0)
                {
                    TextMatch m = new TextMatch();
                    m.Page = index;
                    m.Start = Native.FPDFText_GetSchResultIndex(h);
                    m.Count = Native.FPDFText_GetSchCount(h);
                    if (total < 0) total = Native.FPDFText_CountChars(tp);
                    int cs = Math.Max(0, m.Start - 30);
                    int ce = Math.Min(total, m.Start + m.Count + 40);
                    string before = GetText(index, cs, m.Start - cs);
                    string match = GetText(index, m.Start, m.Count);
                    string after = GetText(index, m.Start + m.Count, ce - m.Start - m.Count);
                    before = Flatten(before);
                    m.Context = before + Flatten(match) + Flatten(after);
                    m.ContextMatchStart = before.Length;
                    result.Add(m);
                    if (result.Count > 5000) break;
                }
            }
            finally
            {
                Native.FPDFText_FindClose(h);
            }
            return result;
        }

        private static string Flatten(string s)
        {
            return s.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        }

        // ------------------------------------------------------------------
        // Metadaten, Lesezeichen, Links, Anlagen
        // ------------------------------------------------------------------

        public string GetMeta(string tag)
        {
            return Native.ReadUtf16(delegate(byte[] b, uint l) { return Native.FPDF_GetMetaText(doc, tag, b, l); });
        }

        public string FileVersion
        {
            get
            {
                int v;
                if (Native.FPDF_GetFileVersion(doc, out v) == 0) return "?";
                return (v / 10) + "." + (v % 10);
            }
        }

        public uint Permissions { get { return Native.FPDF_GetDocPermissions(doc); } }

        public int SecurityRevision { get { return Native.FPDF_GetSecurityHandlerRevision(doc); } }

        public List<BookmarkNode> GetBookmarks()
        {
            List<BookmarkNode> list = new List<BookmarkNode>();
            ReadBookmarks(IntPtr.Zero, list, 0);
            return list;
        }

        private void ReadBookmarks(IntPtr parent, List<BookmarkNode> list, int depth)
        {
            if (depth > 32) return;
            IntPtr child = Native.FPDFBookmark_GetFirstChild(doc, parent);
            int guard = 0;
            while (child != IntPtr.Zero && guard++ < 10000)
            {
                IntPtr current = child;
                BookmarkNode node = new BookmarkNode();
                node.Title = Native.ReadUtf16(delegate(byte[] b, uint l) { return Native.FPDFBookmark_GetTitle(current, b, l); });
                IntPtr dest = Native.FPDFBookmark_GetDest(doc, current);
                if (dest == IntPtr.Zero)
                {
                    IntPtr action = Native.FPDFBookmark_GetAction(current);
                    if (action != IntPtr.Zero)
                    {
                        uint type = Native.FPDFAction_GetType(action);
                        if (type == Native.PDFACTION_GOTO) dest = Native.FPDFAction_GetDest(doc, action);
                        else if (type == Native.PDFACTION_URI)
                            node.Uri = Native.ReadAscii(delegate(byte[] b, uint l) { return Native.FPDFAction_GetURIPath(doc, action, b, l); });
                    }
                }
                if (dest != IntPtr.Zero) FillDest(dest, out node.PageIndex, out node.Y);
                ReadBookmarks(current, node.Children, depth + 1);
                list.Add(node);
                child = Native.FPDFBookmark_GetNextSibling(doc, current);
            }
        }

        private void FillDest(IntPtr dest, out int pageIndex, out double? y)
        {
            pageIndex = Native.FPDFDest_GetDestPageIndex(doc, dest);
            y = null;
            int hasX, hasY, hasZoom;
            float fx, fy, fz;
            if (Native.FPDFDest_GetLocationInPage(dest, out hasX, out hasY, out hasZoom, out fx, out fy, out fz) != 0 && hasY != 0)
                y = fy;
        }

        public LinkTarget GetLinkAt(int index, double x, double y)
        {
            IntPtr link = Native.FPDFLink_GetLinkAtPoint(GetPage(index), x, y);
            if (link == IntPtr.Zero) return null;
            LinkTarget target = new LinkTarget();
            IntPtr dest = Native.FPDFLink_GetDest(doc, link);
            if (dest == IntPtr.Zero)
            {
                IntPtr action = Native.FPDFLink_GetAction(link);
                if (action != IntPtr.Zero)
                {
                    uint type = Native.FPDFAction_GetType(action);
                    if (type == Native.PDFACTION_GOTO) dest = Native.FPDFAction_GetDest(doc, action);
                    else if (type == Native.PDFACTION_URI)
                        target.Uri = Native.ReadAscii(delegate(byte[] b, uint l) { return Native.FPDFAction_GetURIPath(doc, action, b, l); });
                }
            }
            if (dest != IntPtr.Zero) FillDest(dest, out target.PageIndex, out target.Y);
            if (target.PageIndex < 0 && string.IsNullOrEmpty(target.Uri)) return null;
            return target;
        }

        public List<AttachmentInfo> GetAttachments()
        {
            List<AttachmentInfo> list = new List<AttachmentInfo>();
            int n = Native.FPDFDoc_GetAttachmentCount(doc);
            for (int i = 0; i < n; i++)
            {
                IntPtr a = Native.FPDFDoc_GetAttachment(doc, i);
                if (a == IntPtr.Zero) continue;
                AttachmentInfo info = new AttachmentInfo();
                info.Index = i;
                info.Name = Native.ReadUtf16(delegate(byte[] b, uint l) { return Native.FPDFAttachment_GetName(a, b, l); });
                ulong len = 0;
                Native.FPDFAttachment_GetFile(a, null, 0, ref len);
                info.Size = (long)(len & 0xFFFFFFFF);
                list.Add(info);
            }
            return list;
        }

        public byte[] GetAttachmentData(int index)
        {
            IntPtr a = Native.FPDFDoc_GetAttachment(doc, index);
            if (a == IntPtr.Zero) return null;
            ulong len = 0;
            if (Native.FPDFAttachment_GetFile(a, null, 0, ref len) == 0) return null;
            int size = (int)(len & 0xFFFFFFFF);
            byte[] buffer = new byte[size];
            ulong outLen = 0;
            Native.FPDFAttachment_GetFile(a, buffer, (uint)size, ref outLen);
            return buffer;
        }

        // ------------------------------------------------------------------
        // Speichern
        // ------------------------------------------------------------------

        public byte[] SaveToBytes()
        {
            KillFormFocus();
            MemoryStream ms = new MemoryStream();
            Native.WriteBlockHandler handler = delegate(IntPtr pThis, IntPtr data, uint size)
            {
                byte[] chunk = new byte[size];
                Marshal.Copy(data, chunk, 0, (int)size);
                ms.Write(chunk, 0, chunk.Length);
                return 1;
            };
            Native.FPDF_FILEWRITE fw = new Native.FPDF_FILEWRITE();
            fw.version = 1;
            fw.WriteBlock = Marshal.GetFunctionPointerForDelegate(handler);
            int ok = Native.FPDF_SaveAsCopy(doc, ref fw, Native.FPDF_NO_INCREMENTAL);
            GC.KeepAlive(handler);
            if (ok == 0) throw new PdfException("Das Dokument konnte nicht gespeichert werden.", Native.FPDF_GetLastError());
            return ms.ToArray();
        }

        // ------------------------------------------------------------------
        // Seitenoperationen
        // ------------------------------------------------------------------

        public void DeletePage(int index)
        {
            ClosePages();
            Native.FPDFPage_Delete(doc, index);
        }

        public void SetPageRotation(int index, int rotation)
        {
            IntPtr p = GetPage(index);
            Native.FPDFPage_SetRotation(p, ((rotation % 4) + 4) % 4);
            ClosePages();
        }

        public bool MovePages(int[] indices, int destIndex)
        {
            ClosePages();
            return Native.FPDF_MovePages(doc, indices, (uint)indices.Length, destIndex) != 0;
        }

        /// <summary>Importiert Seiten aus einem anderen Dokument. indices == null: alle Seiten.</summary>
        public bool ImportPages(PdfDocument source, int[] indices, int insertAt)
        {
            ClosePages();
            if (indices == null)
                return Native.FPDF_ImportPages(doc, source.doc, null, insertAt) != 0;
            return Native.FPDF_ImportPagesByIndex(doc, source.doc, indices, (uint)indices.Length, insertAt) != 0;
        }

        public void InsertBlankPage(int index, double width, double height)
        {
            ClosePages();
            IntPtr p = Native.FPDFPage_New(doc, index, width, height);
            if (p != IntPtr.Zero) Native.FPDF_ClosePage(p);
        }

        public void GetCropBox(int index, out PdfRect box)
        {
            IntPtr p = GetPage(index);
            float l, b, r, t;
            if (Native.FPDFPage_GetCropBox(p, out l, out b, out r, out t) == 0 &&
                Native.FPDFPage_GetMediaBox(p, out l, out b, out r, out t) == 0)
            {
                SizeF s = GetPageSize(index);
                l = 0; b = 0; r = s.Width; t = s.Height;
            }
            box = new PdfRect(l, b, r, t);
        }

        public void SetCropBox(int index, PdfRect box)
        {
            IntPtr p = GetPage(index);
            Native.FPDFPage_SetCropBox(p, (float)box.Left, (float)box.Bottom, (float)box.Right, (float)box.Top);
            ClosePages();
        }

        /// <summary>Muss nach dem Einfügen von Seitenobjekten aufgerufen werden.</summary>
        public void CommitPageContent(int index)
        {
            Native.FPDFPage_GenerateContent(GetPage(index));
        }

        // ------------------------------------------------------------------
        // Formular-Umgebung
        // ------------------------------------------------------------------

        private sealed class FormEnvironment : IDisposable
        {
            private readonly PdfDocument owner;
            private IntPtr info;
            public IntPtr Handle;

            // Delegaten müssen am Leben bleiben, solange PDFium sie aufrufen kann.
            private readonly Native.FfiInvalidate invalidate;
            private readonly Native.FfiSetCursor setCursor;
            private readonly Native.FfiSetTimer setTimer;
            private readonly Native.FfiKillTimer killTimer;
            private readonly Native.FfiOnChange onChange;
            private readonly Native.FfiExecuteNamedAction namedAction;
            private readonly Native.FfiDoUriAction uriAction;
            private readonly Native.FfiDoGoToAction gotoAction;
            private readonly Dictionary<int, System.Windows.Forms.Timer> timers = new Dictionary<int, System.Windows.Forms.Timer>();
            private int nextTimer = 1;

            public FormEnvironment(PdfDocument owner)
            {
                this.owner = owner;
                invalidate = OnInvalidate;
                setCursor = OnSetCursor;
                setTimer = OnSetTimer;
                killTimer = OnKillTimer;
                onChange = OnChange;
                namedAction = OnNamedAction;
                uriAction = OnUriAction;
                gotoAction = OnGoToAction;

                Native.FPDF_FORMFILLINFO fi = new Native.FPDF_FORMFILLINFO();
                fi.version = 1;
                fi.FFI_Invalidate = Marshal.GetFunctionPointerForDelegate(invalidate);
                fi.FFI_SetCursor = Marshal.GetFunctionPointerForDelegate(setCursor);
                fi.FFI_SetTimer = Marshal.GetFunctionPointerForDelegate(setTimer);
                fi.FFI_KillTimer = Marshal.GetFunctionPointerForDelegate(killTimer);
                fi.FFI_OnChange = Marshal.GetFunctionPointerForDelegate(onChange);
                fi.FFI_ExecuteNamedAction = Marshal.GetFunctionPointerForDelegate(namedAction);
                fi.FFI_DoURIAction = Marshal.GetFunctionPointerForDelegate(uriAction);
                fi.FFI_DoGoToAction = Marshal.GetFunctionPointerForDelegate(gotoAction);

                info = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.FPDF_FORMFILLINFO)));
                Marshal.StructureToPtr(fi, info, false);
                Handle = Native.FPDFDOC_InitFormFillEnvironment(owner.doc, info);
            }

            private void OnInvalidate(IntPtr pThis, IntPtr page, double l, double t, double r, double b)
            {
                owner.RaiseFormInvalidated(page);
            }

            private void OnSetCursor(IntPtr pThis, int type) { }

            private int OnSetTimer(IntPtr pThis, int elapse, IntPtr func)
            {
                int id = nextTimer++;
                Native.TimerCallback cb = (Native.TimerCallback)Marshal.GetDelegateForFunctionPointer(func, typeof(Native.TimerCallback));
                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                timer.Interval = Math.Max(10, elapse);
                timer.Tick += delegate { if (Handle != IntPtr.Zero) cb(id); };
                timers[id] = timer;
                timer.Start();
                return id;
            }

            private void OnKillTimer(IntPtr pThis, int id)
            {
                System.Windows.Forms.Timer timer;
                if (timers.TryGetValue(id, out timer))
                {
                    timer.Stop();
                    timer.Dispose();
                    timers.Remove(id);
                }
            }

            private void OnChange(IntPtr pThis)
            {
                owner.RaiseFormChanged();
            }

            private void OnNamedAction(IntPtr pThis, string action)
            {
                LinkTarget t = new LinkTarget();
                t.Uri = "named:" + action;
                owner.RaiseFormAction(t);
            }

            private void OnUriAction(IntPtr pThis, string uri)
            {
                LinkTarget t = new LinkTarget();
                t.Uri = uri;
                owner.RaiseFormAction(t);
            }

            private void OnGoToAction(IntPtr pThis, int pageIndex, int zoomMode, IntPtr pos, int size)
            {
                LinkTarget t = new LinkTarget();
                t.PageIndex = pageIndex;
                owner.RaiseFormAction(t);
            }

            public void Dispose()
            {
                foreach (System.Windows.Forms.Timer t in timers.Values)
                {
                    t.Stop();
                    t.Dispose();
                }
                timers.Clear();
                if (Handle != IntPtr.Zero)
                {
                    Native.FPDFDOC_ExitFormFillEnvironment(Handle);
                    Handle = IntPtr.Zero;
                }
                if (info != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(info);
                    info = IntPtr.Zero;
                }
                GC.KeepAlive(invalidate);
                GC.KeepAlive(setCursor);
                GC.KeepAlive(setTimer);
                GC.KeepAlive(killTimer);
                GC.KeepAlive(onChange);
                GC.KeepAlive(namedAction);
                GC.KeepAlive(uriAction);
                GC.KeepAlive(gotoAction);
            }
        }

        // Formularereignisse an das Formular weiterleiten -----------------------------

        public bool FormMouseMove(int index, int modifiers, double x, double y)
        {
            if (form == null) return false;
            return Native.FORM_OnMouseMove(form.Handle, GetPage(index), modifiers, x, y) != 0;
        }

        public bool FormMouseDown(int index, int modifiers, double x, double y)
        {
            if (form == null) return false;
            return Native.FORM_OnLButtonDown(form.Handle, GetPage(index), modifiers, x, y) != 0;
        }

        public bool FormMouseUp(int index, int modifiers, double x, double y)
        {
            if (form == null) return false;
            return Native.FORM_OnLButtonUp(form.Handle, GetPage(index), modifiers, x, y) != 0;
        }

        public bool FormDoubleClick(int index, int modifiers, double x, double y)
        {
            if (form == null) return false;
            return Native.FORM_OnLButtonDoubleClick(form.Handle, GetPage(index), modifiers, x, y) != 0;
        }

        public bool FormKeyDown(int index, int keyCode, int modifiers)
        {
            if (form == null) return false;
            return Native.FORM_OnKeyDown(form.Handle, GetPage(index), keyCode, modifiers) != 0;
        }

        public bool FormChar(int index, int charCode, int modifiers)
        {
            if (form == null) return false;
            return Native.FORM_OnChar(form.Handle, GetPage(index), charCode, modifiers) != 0;
        }

        /// <summary>Feldtyp an der Position (FPDF_FORMFIELD_*), -1 wenn kein Feld.</summary>
        public int FormFieldAt(int index, double x, double y)
        {
            if (form == null) return -1;
            return Native.FPDFPage_HasFormFieldAtPoint(form.Handle, GetPage(index), x, y);
        }
    }

    public class PageEventArgs : EventArgs
    {
        public int PageIndex { get; private set; }
        public PageEventArgs(int page) { PageIndex = page; }
    }

    public class LinkEventArgs : EventArgs
    {
        public LinkTarget Target { get; private set; }
        public LinkEventArgs(LinkTarget target) { Target = target; }
    }
}
