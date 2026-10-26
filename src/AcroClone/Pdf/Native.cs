using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AcroClone.Pdf
{
    /// <summary>
    /// P/Invoke-Deklarationen für PDFium (pdfium.dll).
    /// Alle exportierten Funktionen der pdfium-binaries verwenden cdecl.
    /// </summary>
    internal static class Native
    {
        private const string Lib = "pdfium";

        public const int FPDF_ANNOT = 0x01;
        public const int FPDF_LCD_TEXT = 0x02;
        public const int FPDF_GRAYSCALE = 0x08;
        public const int FPDF_PRINTING = 0x800;

        public const int FPDFBitmap_Gray = 1;
        public const int FPDFBitmap_BGR = 2;
        public const int FPDFBitmap_BGRx = 3;
        public const int FPDFBitmap_BGRA = 4;

        public const int FPDF_ERR_SUCCESS = 0;
        public const int FPDF_ERR_UNKNOWN = 1;
        public const int FPDF_ERR_FILE = 2;
        public const int FPDF_ERR_FORMAT = 3;
        public const int FPDF_ERR_PASSWORD = 4;
        public const int FPDF_ERR_SECURITY = 5;

        public const int FPDF_INCREMENTAL = 1;
        public const int FPDF_NO_INCREMENTAL = 2;

        public const int FPDF_MATCHCASE = 0x1;
        public const int FPDF_MATCHWHOLEWORD = 0x2;

        public const int FPDF_ANNOT_TEXT = 1;
        public const int FPDF_ANNOT_LINK = 2;
        public const int FPDF_ANNOT_FREETEXT = 3;
        public const int FPDF_ANNOT_SQUARE = 5;
        public const int FPDF_ANNOT_CIRCLE = 6;
        public const int FPDF_ANNOT_HIGHLIGHT = 9;
        public const int FPDF_ANNOT_UNDERLINE = 10;
        public const int FPDF_ANNOT_STRIKEOUT = 12;
        public const int FPDF_ANNOT_STAMP = 13;
        public const int FPDF_ANNOT_INK = 15;
        public const int FPDF_ANNOT_POPUP = 16;
        public const int FPDF_ANNOT_WIDGET = 20;

        public const int FPDF_ANNOT_FLAG_PRINT = 1 << 2;

        public const int FPDFANNOT_COLORTYPE_Color = 0;
        public const int FPDFANNOT_COLORTYPE_InteriorColor = 1;

        public const int FPDF_FILLMODE_NONE = 0;
        public const int FPDF_FILLMODE_ALTERNATE = 1;
        public const int FPDF_FILLMODE_WINDING = 2;

        public const int PDFACTION_GOTO = 1;
        public const int PDFACTION_REMOTEGOTO = 2;
        public const int PDFACTION_URI = 3;
        public const int PDFACTION_LAUNCH = 4;

        public const int FPDF_LINECAP_ROUND = 1;
        public const int FPDF_LINEJOIN_ROUND = 1;

        public const int FWL_EVENTFLAG_ShiftKey = 1;
        public const int FWL_EVENTFLAG_ControlKey = 2;
        public const int FWL_EVENTFLAG_AltKey = 4;

        public const int FPDF_FORMFIELD_UNKNOWN = 0;
        public const int FPDF_FORMFIELD_PUSHBUTTON = 1;
        public const int FPDF_FORMFIELD_CHECKBOX = 2;
        public const int FPDF_FORMFIELD_RADIOBUTTON = 3;
        public const int FPDF_FORMFIELD_COMBOBOX = 4;
        public const int FPDF_FORMFIELD_LISTBOX = 5;
        public const int FPDF_FORMFIELD_TEXTFIELD = 6;
        public const int FPDF_FORMFIELD_SIGNATURE = 7;

        public const int FORMTYPE_NONE = 0;

        // ---------------------------------------------------------------
        // Strukturen
        // ---------------------------------------------------------------

        [StructLayout(LayoutKind.Sequential)]
        public struct FS_RECTF
        {
            public float left, top, right, bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FS_SIZEF
        {
            public float width, height;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FS_POINTF
        {
            public float x, y;
            public FS_POINTF(float x, float y) { this.x = x; this.y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FS_QUADPOINTSF
        {
            public float x1, y1, x2, y2, x3, y3, x4, y4;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct FPDF_FILEWRITE
        {
            public int version;
            public IntPtr WriteBlock;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int WriteBlockHandler(IntPtr pThis, IntPtr data, uint size);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int GetBlockHandler(IntPtr param, uint position, IntPtr buffer, uint size);

        /// <summary>FPDF_FORMFILLINFO, Version 1.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct FPDF_FORMFILLINFO
        {
            public int version;
            public IntPtr Release;
            public IntPtr FFI_Invalidate;
            public IntPtr FFI_OutputSelectedRect;
            public IntPtr FFI_SetCursor;
            public IntPtr FFI_SetTimer;
            public IntPtr FFI_KillTimer;
            public IntPtr FFI_GetLocalTime;
            public IntPtr FFI_OnChange;
            public IntPtr FFI_GetPage;
            public IntPtr FFI_GetCurrentPage;
            public IntPtr FFI_GetRotation;
            public IntPtr FFI_ExecuteNamedAction;
            public IntPtr FFI_SetTextFieldFocus;
            public IntPtr FFI_DoURIAction;
            public IntPtr FFI_DoGoToAction;
            public IntPtr m_pJsPlatform;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void FfiInvalidate(IntPtr pThis, IntPtr page, double left, double top, double right, double bottom);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void FfiSetCursor(IntPtr pThis, int cursorType);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate int FfiSetTimer(IntPtr pThis, int elapse, IntPtr timerFunc);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void FfiKillTimer(IntPtr pThis, int timerId);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void FfiOnChange(IntPtr pThis);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void FfiExecuteNamedAction(IntPtr pThis, [MarshalAs(UnmanagedType.LPStr)] string action);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void FfiDoUriAction(IntPtr pThis, [MarshalAs(UnmanagedType.LPStr)] string uri);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void FfiDoGoToAction(IntPtr pThis, int pageIndex, int zoomMode, IntPtr posArray, int arraySize);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void TimerCallback(int timerId);

        // ---------------------------------------------------------------
        // Bibliothek / Dokument
        // ---------------------------------------------------------------

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_InitLibrary();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_DestroyLibrary();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, UIntPtr size, [MarshalAs(UnmanagedType.LPStr)] string password);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDF_CreateNewDocument();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_CloseDocument(IntPtr document);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDF_GetLastError();

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_GetPageCount(IntPtr document);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_GetPageSizeByIndexF(IntPtr document, int index, out FS_SIZEF size);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_GetFileVersion(IntPtr document, out int version);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDF_GetDocPermissions(IntPtr document);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_GetSecurityHandlerRevision(IntPtr document);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDF_GetMetaText(IntPtr document, [MarshalAs(UnmanagedType.LPStr)] string tag, byte[] buffer, uint buflen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDF_GetPageLabel(IntPtr document, int pageIndex, byte[] buffer, uint buflen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_GetFormType(IntPtr document);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_SaveAsCopy(IntPtr document, ref FPDF_FILEWRITE fileWrite, uint flags);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_ImportPages(IntPtr destDoc, IntPtr srcDoc, [MarshalAs(UnmanagedType.LPStr)] string pageRange, int index);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_ImportPagesByIndex(IntPtr destDoc, IntPtr srcDoc, int[] pageIndices, uint length, int index);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_MovePages(IntPtr document, int[] pageIndices, uint length, int destPageIndex);

        // ---------------------------------------------------------------
        // Seiten
        // ---------------------------------------------------------------

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDF_LoadPage(IntPtr document, int index);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_ClosePage(IntPtr page);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern float FPDF_GetPageWidthF(IntPtr page);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern float FPDF_GetPageHeightF(IntPtr page);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFPage_New(IntPtr document, int index, double width, double height);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFPage_Delete(IntPtr document, int index);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPage_GetRotation(IntPtr page);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFPage_SetRotation(IntPtr page, int rotate);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPage_GetCropBox(IntPtr page, out float left, out float bottom, out float right, out float top);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPage_GetMediaBox(IntPtr page, out float left, out float bottom, out float right, out float top);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFPage_SetCropBox(IntPtr page, float left, float bottom, float right, float top);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPage_GenerateContent(IntPtr page);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPage_InsertObject(IntPtr page, IntPtr pageObject);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPage_Flatten(IntPtr page, int flag);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDF_PageToDevice(IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate,
            double pageX, double pageY, out int deviceX, out int deviceY);

        // ---------------------------------------------------------------
        // Rendering
        // ---------------------------------------------------------------

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format, IntPtr firstScan, int stride);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFBitmap_Destroy(IntPtr bitmap);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_FFLDraw(IntPtr formHandle, IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

        // ---------------------------------------------------------------
        // Text
        // ---------------------------------------------------------------

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFText_LoadPage(IntPtr page);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFText_ClosePage(IntPtr textPage);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_CountChars(IntPtr textPage);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDFText_GetUnicode(IntPtr textPage, int index);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_GetCharBox(IntPtr textPage, int index, out double left, out double right, out double bottom, out double top);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_GetCharIndexAtPos(IntPtr textPage, double x, double y, double xTolerance, double yTolerance);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_GetText(IntPtr textPage, int startIndex, int count, byte[] result);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_CountRects(IntPtr textPage, int startIndex, int count);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_GetRect(IntPtr textPage, int rectIndex, out double left, out double top, out double right, out double bottom);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFText_FindStart(IntPtr textPage, [MarshalAs(UnmanagedType.LPWStr)] string findWhat, uint flags, int startIndex);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_FindNext(IntPtr handle);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_GetSchResultIndex(IntPtr handle);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_GetSchCount(IntPtr handle);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFText_FindClose(IntPtr handle);

        // ---------------------------------------------------------------
        // Lesezeichen, Links, Aktionen
        // ---------------------------------------------------------------

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFBookmark_GetFirstChild(IntPtr document, IntPtr bookmark);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFBookmark_GetNextSibling(IntPtr document, IntPtr bookmark);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDFBookmark_GetTitle(IntPtr bookmark, byte[] buffer, uint buflen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFBookmark_GetDest(IntPtr document, IntPtr bookmark);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFBookmark_GetAction(IntPtr bookmark);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDFAction_GetType(IntPtr action);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFAction_GetDest(IntPtr document, IntPtr action);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDFAction_GetURIPath(IntPtr document, IntPtr action, byte[] buffer, uint buflen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFDest_GetDestPageIndex(IntPtr document, IntPtr dest);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFDest_GetLocationInPage(IntPtr dest, out int hasX, out int hasY, out int hasZoom,
            out float x, out float y, out float zoom);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFLink_GetLinkAtPoint(IntPtr page, double x, double y);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFLink_GetDest(IntPtr document, IntPtr link);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFLink_GetAction(IntPtr link);

        // ---------------------------------------------------------------
        // Anlagen
        // ---------------------------------------------------------------

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFDoc_GetAttachmentCount(IntPtr document);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFDoc_GetAttachment(IntPtr document, int index);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDFAttachment_GetName(IntPtr attachment, byte[] buffer, uint buflen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAttachment_GetFile(IntPtr attachment, byte[] buffer, uint buflen, ref ulong outBuflen);

        // ---------------------------------------------------------------
        // Anmerkungen
        // ---------------------------------------------------------------

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFPage_CreateAnnot(IntPtr page, int subtype);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPage_GetAnnotCount(IntPtr page);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFPage_GetAnnot(IntPtr page, int index);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFPage_CloseAnnot(IntPtr annot);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPage_RemoveAnnot(IntPtr page, int index);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_GetSubtype(IntPtr annot);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_SetColor(IntPtr annot, int type, uint r, uint g, uint b, uint a);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_SetRect(IntPtr annot, ref FS_RECTF rect);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_GetRect(IntPtr annot, out FS_RECTF rect);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_AppendAttachmentPoints(IntPtr annot, ref FS_QUADPOINTSF quad);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_AddInkStroke(IntPtr annot, FS_POINTF[] points, UIntPtr pointCount);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_SetStringValue(IntPtr annot, [MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPWStr)] string value);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint FPDFAnnot_GetStringValue(IntPtr annot, [MarshalAs(UnmanagedType.LPStr)] string key, byte[] buffer, uint buflen);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_HasKey(IntPtr annot, [MarshalAs(UnmanagedType.LPStr)] string key);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_SetBorder(IntPtr annot, float horizontalRadius, float verticalRadius, float borderWidth);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_SetFlags(IntPtr annot, int flags);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFAnnot_AppendObject(IntPtr annot, IntPtr pageObject);

        // ---------------------------------------------------------------
        // Seitenobjekte
        // ---------------------------------------------------------------

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFPageObj_NewTextObj(IntPtr document, [MarshalAs(UnmanagedType.LPStr)] string font, float fontSize);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFText_SetText(IntPtr textObject, [MarshalAs(UnmanagedType.LPWStr)] string text);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFPageObj_CreateNewPath(float x, float y);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFPageObj_CreateNewRect(float x, float y, float w, float h);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPath_MoveTo(IntPtr path, float x, float y);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPath_LineTo(IntPtr path, float x, float y);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPath_BezierTo(IntPtr path, float x1, float y1, float x2, float y2, float x3, float y3);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPath_Close(IntPtr path);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPath_SetDrawMode(IntPtr path, int fillMode, int stroke);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPageObj_SetFillColor(IntPtr pageObject, uint r, uint g, uint b, uint a);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPageObj_SetStrokeColor(IntPtr pageObject, uint r, uint g, uint b, uint a);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPageObj_SetStrokeWidth(IntPtr pageObject, float width);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPageObj_SetLineJoin(IntPtr pageObject, int lineJoin);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPageObj_SetLineCap(IntPtr pageObject, int lineCap);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFPageObj_Transform(IntPtr pageObject, double a, double b, double c, double d, double e, double f);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPageObj_GetBounds(IntPtr pageObject, out float left, out float bottom, out float right, out float top);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFPageObj_Destroy(IntPtr pageObject);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFPageObj_NewImageObj(IntPtr document);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFImageObj_SetBitmap(IntPtr[] pages, int count, IntPtr imageObject, IntPtr bitmap);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFImageObj_LoadJpegFileInline(IntPtr[] pages, int count, IntPtr imageObject, IntPtr fileAccess);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFImageObj_SetMatrix(IntPtr imageObject, double a, double b, double c, double d, double e, double f);

        // ---------------------------------------------------------------
        // Formulare
        // ---------------------------------------------------------------

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFDOC_InitFormFillEnvironment(IntPtr document, IntPtr formInfo);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFDOC_ExitFormFillEnvironment(IntPtr formHandle);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FORM_OnAfterLoadPage(IntPtr page, IntPtr formHandle);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FORM_OnBeforeClosePage(IntPtr page, IntPtr formHandle);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FORM_OnMouseMove(IntPtr formHandle, IntPtr page, int modifier, double pageX, double pageY);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FORM_OnLButtonDown(IntPtr formHandle, IntPtr page, int modifier, double pageX, double pageY);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FORM_OnLButtonUp(IntPtr formHandle, IntPtr page, int modifier, double pageX, double pageY);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FORM_OnLButtonDoubleClick(IntPtr formHandle, IntPtr page, int modifier, double pageX, double pageY);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FORM_OnKeyDown(IntPtr formHandle, IntPtr page, int keyCode, int modifier);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FORM_OnChar(IntPtr formHandle, IntPtr page, int charCode, int modifier);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FORM_ForceToKillFocus(IntPtr formHandle);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPage_HasFormFieldAtPoint(IntPtr formHandle, IntPtr page, double pageX, double pageY);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_SetFormFieldHighlightColor(IntPtr formHandle, int fieldType, uint color);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_SetFormFieldHighlightAlpha(IntPtr formHandle, byte alpha);

        [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_RemoveFormFieldHighlight(IntPtr formHandle);

        // ---------------------------------------------------------------
        // Hilfsfunktionen
        // ---------------------------------------------------------------

        /// <summary>Liest einen UTF-16LE-String über das übliche "zweimal aufrufen"-Muster.</summary>
        public static string ReadUtf16(Func<byte[], uint, uint> call)
        {
            uint len = call(null, 0);
            if (len <= 2) return string.Empty;
            byte[] buffer = new byte[len];
            call(buffer, len);
            string s = Encoding.Unicode.GetString(buffer, 0, (int)len);
            int zero = s.IndexOf('\0');
            return zero >= 0 ? s.Substring(0, zero) : s;
        }

        /// <summary>Liest einen nullterminierten 7-Bit-ASCII-String (z. B. URI).</summary>
        public static string ReadAscii(Func<byte[], uint, uint> call)
        {
            uint len = call(null, 0);
            if (len <= 1) return string.Empty;
            byte[] buffer = new byte[len];
            call(buffer, len);
            string s = Encoding.ASCII.GetString(buffer, 0, (int)len);
            int zero = s.IndexOf('\0');
            return zero >= 0 ? s.Substring(0, zero) : s;
        }

        // ---------------------------------------------------------------
        // Laden der richtigen DLL (x86/x64)
        // ---------------------------------------------------------------

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        private static bool initialized;

        public static bool IsWindows
        {
            get
            {
                PlatformID p = Environment.OSVersion.Platform;
                return p == PlatformID.Win32NT || p == PlatformID.Win32Windows;
            }
        }

        /// <summary>
        /// Lädt pdfium.dll passend zur Prozessarchitektur aus dem Unterordner x86 bzw. x64
        /// und initialisiert die Bibliothek.
        /// </summary>
        public static void EnsureInitialized()
        {
            if (initialized) return;
            if (IsWindows)
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string arch = IntPtr.Size == 8 ? "x64" : "x86";
                string candidate = Path.Combine(Path.Combine(baseDir, arch), "pdfium.dll");
                if (!File.Exists(candidate)) candidate = Path.Combine(baseDir, "pdfium.dll");
                if (File.Exists(candidate))
                {
                    if (LoadLibrary(candidate) == IntPtr.Zero)
                        throw new DllNotFoundException("pdfium.dll konnte nicht geladen werden: " + candidate +
                            " (Fehlercode " + Marshal.GetLastWin32Error() + ")");
                }
            }
            FPDF_InitLibrary();
            initialized = true;
        }
    }
}
