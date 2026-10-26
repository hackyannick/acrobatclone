using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AcroClone.Pdf;

namespace AcroClone.Model
{
    /// <summary>
    /// Ein geöffnetes Dokument mit Kommentaren, Änderungsstatus und Rückgängig-Verlauf.
    /// </summary>
    public sealed class DocumentSession : IDisposable
    {
        private class Snapshot
        {
            public byte[] PdfBytes;
            public List<string> Annotations;
            public Dictionary<string, string> Info;
            public string Description;
        }

        private readonly Stack<Snapshot> undo = new Stack<Snapshot>();
        private readonly Stack<Snapshot> redo = new Stack<Snapshot>();
        private bool modified;

        public PdfDocument Pdf { get; private set; }
        public string FilePath { get; private set; }
        public string DisplayName { get; set; }
        public string Password { get; private set; }
        public long FileSize { get; private set; }
        public List<Annotation> Annotations { get; private set; }
        public Dictionary<string, string> InfoOverrides { get; private set; }

        /// <summary>Struktur des Dokuments wurde verändert (Seiten, Drehung, Inhalt) – Ansichten neu aufbauen.</summary>
        public event EventHandler StructureChanged;
        /// <summary>Kommentare wurden hinzugefügt, entfernt oder geändert.</summary>
        public event EventHandler AnnotationsChanged;
        public event EventHandler ModifiedChanged;
        /// <summary>Das PdfDocument-Objekt wurde ersetzt (z. B. durch Rückgängig).</summary>
        public event EventHandler DocumentReplaced;

        private DocumentSession(PdfDocument pdf, string path, string password)
        {
            Pdf = pdf;
            FilePath = path;
            Password = password;
            DisplayName = path != null ? Path.GetFileName(path) : "Unbenannt.pdf";
            Annotations = new List<Annotation>();
            InfoOverrides = new Dictionary<string, string>();
            HookPdf();
        }

        public static DocumentSession Open(string path, Func<string> askPassword)
        {
            byte[] data = File.ReadAllBytes(path);
            DocumentSession s = FromBytes(data, path, askPassword);
            s.FileSize = data.Length;
            return s;
        }

        public static DocumentSession FromBytes(byte[] data, string path, Func<string> askPassword)
        {
            string password = null;
            PdfDocument pdf = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    pdf = PdfDocument.Load(data, password);
                    break;
                }
                catch (PdfException ex)
                {
                    if (ex.ErrorCode != Native.FPDF_ERR_PASSWORD || askPassword == null) throw;
                    password = askPassword();
                    if (password == null) return null;
                }
            }
            if (pdf == null) throw new PdfException("Das Kennwort ist falsch.", Native.FPDF_ERR_PASSWORD);
            DocumentSession s = new DocumentSession(pdf, path, password);
            s.FileSize = data.Length;
            if (ContainsMarker(data))
                s.Annotations.AddRange(AnnotationWriter.Extract(pdf, 0, pdf.PageCount - 1));
            pdf.EnableForms();
            return s;
        }

        /// <summary>Übernimmt ein neu erzeugtes Dokument als unbenannte Sitzung.</summary>
        public static DocumentSession FromNewDocument(PdfDocument pdf, string displayName)
        {
            byte[] bytes = pdf.SaveToBytes();
            pdf.Dispose();
            DocumentSession s = FromBytes(bytes, null, null);
            s.DisplayName = displayName;
            s.modified = true;
            return s;
        }

        private static bool ContainsMarker(byte[] data)
        {
            byte[] marker = System.Text.Encoding.ASCII.GetBytes(AnnotationWriter.DataKey);
            for (int i = 0; i <= data.Length - marker.Length; i++)
            {
                if (data[i] != marker[0]) continue;
                int k = 1;
                while (k < marker.Length && data[i + k] == marker[k]) k++;
                if (k == marker.Length) return true;
            }
            return false;
        }

        private void HookPdf()
        {
            Pdf.FormChanged += delegate { Modified = true; };
        }

        public void Dispose()
        {
            if (Pdf != null)
            {
                Pdf.Dispose();
                Pdf = null;
            }
        }

        public bool IsUntitled { get { return FilePath == null; } }

        public bool Modified
        {
            get { return modified; }
            set
            {
                if (modified == value) return;
                modified = value;
                if (ModifiedChanged != null) ModifiedChanged(this, EventArgs.Empty);
            }
        }

        public int PageCount { get { return Pdf.PageCount; } }

        // ------------------------------------------------------------------
        // Metadaten
        // ------------------------------------------------------------------

        public string GetInfo(string key)
        {
            string v;
            if (InfoOverrides.TryGetValue(key, out v)) return v;
            return Pdf.GetMeta(key);
        }

        public void SetInfo(string key, string value)
        {
            if (GetInfo(key) == value) return;
            InfoOverrides[key] = value;
            Modified = true;
        }

        // ------------------------------------------------------------------
        // Rückgängig / Wiederherstellen
        // ------------------------------------------------------------------

        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public string UndoDescription { get { return undo.Count > 0 ? undo.Peek().Description : null; } }
        public string RedoDescription { get { return redo.Count > 0 ? redo.Peek().Description : null; } }

        private Snapshot Capture(bool structural, string description)
        {
            Snapshot s = new Snapshot();
            s.Description = description;
            s.Annotations = Annotations.Select(a => a.Serialize()).ToList();
            s.Info = new Dictionary<string, string>(InfoOverrides);
            if (structural) s.PdfBytes = Pdf.SaveToBytes();
            return s;
        }

        /// <summary>Vor jeder Änderung aufrufen. structural = PDF-Inhalt ändert sich.</summary>
        public void BeginChange(bool structural, string description)
        {
            undo.Push(Capture(structural, description));
            redo.Clear();
            while (undo.Count > 50) TrimOldest();
        }

        private void TrimOldest()
        {
            Snapshot[] items = undo.ToArray();
            undo.Clear();
            for (int i = items.Length - 2; i >= 0; i--) undo.Push(items[i]);
        }

        public void Undo()
        {
            if (undo.Count == 0) return;
            Snapshot s = undo.Pop();
            redo.Push(Capture(s.PdfBytes != null, s.Description));
            Restore(s);
        }

        public void Redo()
        {
            if (redo.Count == 0) return;
            Snapshot s = redo.Pop();
            undo.Push(Capture(s.PdfBytes != null, s.Description));
            Restore(s);
        }

        private void Restore(Snapshot s)
        {
            Annotations = s.Annotations.Select(x => Annotation.Deserialize(x)).ToList();
            InfoOverrides = new Dictionary<string, string>(s.Info);
            if (s.PdfBytes != null) ReplacePdf(s.PdfBytes);
            Modified = true;
            if (s.PdfBytes != null) RaiseStructureChanged();
            RaiseAnnotationsChanged();
        }

        private void ReplacePdf(byte[] bytes)
        {
            PdfDocument old = Pdf;
            Pdf = PdfDocument.Load(bytes, Password);
            Pdf.EnableForms();
            HookPdf();
            old.Dispose();
            if (DocumentReplaced != null) DocumentReplaced(this, EventArgs.Empty);
        }

        public void RaiseStructureChanged()
        {
            Pdf.ClosePages();
            if (StructureChanged != null) StructureChanged(this, EventArgs.Empty);
        }

        public void RaiseAnnotationsChanged()
        {
            if (AnnotationsChanged != null) AnnotationsChanged(this, EventArgs.Empty);
        }

        // ------------------------------------------------------------------
        // Kommentare
        // ------------------------------------------------------------------

        public void AddAnnotation(Annotation a)
        {
            BeginChange(false, a.TypeName + " hinzufügen");
            Annotations.Add(a);
            Modified = true;
            RaiseAnnotationsChanged();
        }

        public void RemoveAnnotations(ICollection<Annotation> list)
        {
            if (list.Count == 0) return;
            BeginChange(false, "Kommentar löschen");
            foreach (Annotation a in list) Annotations.Remove(a);
            Modified = true;
            RaiseAnnotationsChanged();
        }

        /// <summary>Nach einer direkten Änderung an Kommentarobjekten aufrufen (BeginChange vorher!).</summary>
        public void CommitAnnotationChange()
        {
            Modified = true;
            RaiseAnnotationsChanged();
        }

        public Annotation FindAnnotation(string id)
        {
            return Annotations.FirstOrDefault(a => a.Id == id);
        }

        public IEnumerable<Annotation> AnnotationsOnPage(int page)
        {
            return Annotations.Where(a => a.Page == page);
        }

        // ------------------------------------------------------------------
        // Seitenoperationen
        // ------------------------------------------------------------------

        public void DeletePages(IEnumerable<int> pages)
        {
            List<int> list = pages.Distinct().Where(p => p >= 0 && p < PageCount).OrderByDescending(p => p).ToList();
            if (list.Count == 0 || list.Count >= PageCount) return;
            BeginChange(true, "Seiten löschen");
            foreach (int p in list) Pdf.DeletePage(p);
            HashSet<int> removed = new HashSet<int>(list);
            Annotations.RemoveAll(a => removed.Contains(a.Page));
            foreach (Annotation a in Annotations)
                a.Page -= list.Count(p => p < a.Page);
            Modified = true;
            RaiseStructureChanged();
            RaiseAnnotationsChanged();
        }

        public void RotatePages(IEnumerable<int> pages, int quarterTurns)
        {
            List<int> list = pages.Distinct().Where(p => p >= 0 && p < PageCount).ToList();
            if (list.Count == 0) return;
            BeginChange(true, "Seiten drehen");
            foreach (int p in list)
                Pdf.SetPageRotation(p, Pdf.GetPageRotation(p) + quarterTurns);
            Modified = true;
            RaiseStructureChanged();
        }

        /// <summary>Verschiebt Seiten, sodass die erste davon anschließend an Position destIndex steht.</summary>
        public void MovePages(IList<int> pages, int destIndex)
        {
            List<int> moved = pages.Distinct().OrderBy(p => p).ToList();
            int n = PageCount;
            if (moved.Count == 0 || moved.Count >= n + 1) return;
            destIndex = Math.Max(0, Math.Min(destIndex, n - moved.Count));
            List<int> order = Enumerable.Range(0, n).Where(p => !moved.Contains(p)).ToList();
            order.InsertRange(destIndex, moved);
            bool unchanged = true;
            for (int i = 0; i < n; i++) if (order[i] != i) unchanged = false;
            if (unchanged) return;

            BeginChange(true, "Seiten verschieben");
            if (!Pdf.MovePages(moved.ToArray(), destIndex)) return;
            int[] newIndexOf = new int[n];
            for (int i = 0; i < n; i++) newIndexOf[order[i]] = i;
            foreach (Annotation a in Annotations) a.Page = newIndexOf[a.Page];
            Modified = true;
            RaiseStructureChanged();
            RaiseAnnotationsChanged();
        }

        public void InsertDocument(PdfDocument source, int insertAt, string description)
        {
            int count = source.PageCount;
            if (count == 0) return;
            BeginChange(true, description ?? "Seiten einfügen");
            if (!Pdf.ImportPages(source, null, insertAt)) throw new PdfException("Die Seiten konnten nicht eingefügt werden.", 1);
            foreach (Annotation a in Annotations)
                if (a.Page >= insertAt) a.Page += count;
            Annotations.AddRange(AnnotationWriter.Extract(Pdf, insertAt, insertAt + count - 1));
            Modified = true;
            RaiseStructureChanged();
            RaiseAnnotationsChanged();
        }

        public void InsertBlankPage(int index, double width, double height)
        {
            BeginChange(true, "Leere Seite einfügen");
            Pdf.InsertBlankPage(index, width, height);
            foreach (Annotation a in Annotations)
                if (a.Page >= index) a.Page += 1;
            Modified = true;
            RaiseStructureChanged();
            RaiseAnnotationsChanged();
        }

        /// <summary>Überträgt alle Kommentare (und Formularfelder) fest in den Seiteninhalt.</summary>
        public void FlattenAnnotations()
        {
            BeginChange(true, "Kommentare reduzieren");
            byte[] bytes = Pdf.SaveToBytes();
            PdfDocument temp = PdfDocument.Load(bytes, Password);
            try
            {
                AnnotationWriter.WriteAll(temp, Annotations);
                for (int i = 0; i < temp.PageCount; i++)
                    Native.FPDFPage_Flatten(temp.GetPage(i), 0);
                temp.ClosePages();
                bytes = temp.SaveToBytes();
            }
            finally
            {
                temp.Dispose();
            }
            Annotations.Clear();
            ReplacePdf(bytes);
            Modified = true;
            RaiseStructureChanged();
            RaiseAnnotationsChanged();
        }

        /// <summary>Erstellt ein neues PDF (Bytes) aus den angegebenen Seiten inkl. Kommentaren.</summary>
        public byte[] ExtractPages(IList<int> pages)
        {
            PdfDocument target = PdfDocument.CreateNew();
            try
            {
                int[] idx = pages.ToArray();
                target.ImportPages(Pdf, idx, 0);
                List<Annotation> copies = new List<Annotation>();
                for (int i = 0; i < idx.Length; i++)
                {
                    foreach (Annotation a in Annotations.Where(x => x.Page == idx[i]))
                    {
                        Annotation c = a.Clone();
                        c.Page = i;
                        copies.Add(c);
                    }
                }
                AnnotationWriter.WriteAll(target, copies);
                return target.SaveToBytes();
            }
            finally
            {
                target.Dispose();
            }
        }

        // ------------------------------------------------------------------
        // Speichern
        // ------------------------------------------------------------------

        /// <summary>Erzeugt die vollständige PDF-Datei inkl. Kommentaren und Metadaten.</summary>
        public byte[] BuildOutput()
        {
            byte[] bytes = Pdf.SaveToBytes();
            if (Annotations.Count > 0)
            {
                PdfDocument temp = PdfDocument.Load(bytes, Password);
                try
                {
                    AnnotationWriter.WriteAll(temp, Annotations);
                    bytes = temp.SaveToBytes();
                }
                finally
                {
                    temp.Dispose();
                }
            }
            Dictionary<string, string> info = new Dictionary<string, string>();
            foreach (string key in InfoDictionaryWriter.Keys)
            {
                string v = GetInfo(key);
                if (key == "CreationDate" || key == "ModDate")
                {
                    if (!string.IsNullOrEmpty(v) && !v.StartsWith("D:")) v = null;
                }
                if (!string.IsNullOrEmpty(v)) info[key] = v;
            }
            if (!info.ContainsKey("CreationDate")) info["CreationDate"] = InfoDictionaryWriter.FormatDate(DateTime.Now);
            info["ModDate"] = InfoDictionaryWriter.FormatDate(DateTime.Now);
            info["Producer"] = "AcroClone 9.0 (PDFium)";
            if (!info.ContainsKey("Creator")) info["Creator"] = "AcroClone 9 Pro";
            return InfoDictionaryWriter.Apply(bytes, info);
        }

        public void Save(string path)
        {
            byte[] bytes = BuildOutput();
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
            FilePath = path;
            DisplayName = Path.GetFileName(path);
            FileSize = bytes.Length;
            Modified = false;
        }
    }
}
