using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using AcroClone.Model;
using AcroClone.Pdf;
using AcroClone.UI;

namespace AcroClone
{
    internal static class Program
    {
        private static readonly List<MainForm> windows = new List<MainForm>();
        private static ApplicationContext context;
        private static int untitled;

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { ShowError(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null) ShowError(ex);
            };

            try
            {
                Native.EnsureInitialized();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Die PDF-Bibliothek (pdfium.dll) konnte nicht geladen werden.\n\n" +
                    "Bitte stellen Sie sicher, dass sich die Ordner \"x86\" und \"x64\" mit pdfium.dll neben AcroClone.exe befinden " +
                    "(PDFium benötigt Windows 10 oder neuer).\n\nDetails: " + ex.Message,
                    "AcroClone 9 Pro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            AppSettings.Load();
            context = new ApplicationContext();
            MainForm first = CreateWindow();
            first.Show();
            foreach (string a in args)
            {
                if (File.Exists(a)) first.OpenFile(Path.GetFullPath(a));
            }
            Application.Run(context);
            AppSettings.Save();
        }

        private static void ShowError(Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            MessageBox.Show("Ein unerwarteter Fehler ist aufgetreten:\n\n" + ex.Message + "\n\n" + ex.GetType().FullName,
                "AcroClone 9 Pro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        public static IList<MainForm> Windows { get { return windows.ToArray(); } }

        public static int WindowCount { get { return windows.Count; } }

        public static int NextUntitled()
        {
            return ++untitled;
        }

        private static MainForm CreateWindow()
        {
            MainForm f = new MainForm();
            windows.Add(f);
            f.FormClosed += delegate
            {
                windows.Remove(f);
                if (windows.Count == 0) context.ExitThread();
            };
            return f;
        }

        public static MainForm FindWindow(string path)
        {
            foreach (MainForm f in windows)
                if (f.Session != null && f.Session.FilePath != null &&
                    string.Equals(Path.GetFullPath(f.Session.FilePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    return f;
            return null;
        }

        public static void OpenInNewWindow(string path)
        {
            MainForm f = CreateWindow();
            f.Show();
            f.OpenFile(path);
            if (f.Session == null && windows.Count > 1) f.Close();
        }

        public static void OpenSessionInNewWindow(DocumentSession s)
        {
            MainForm f = CreateWindow();
            f.Show();
            f.ShowSession(s);
        }

        public static void CloseAllDocuments()
        {
            foreach (MainForm f in windows.ToArray())
                if (!f.CloseDocument()) return;
        }

        public static void ExitAll()
        {
            foreach (MainForm f in windows.ToArray())
            {
                f.Close();
                if (windows.Contains(f)) return; // Benutzer hat abgebrochen
            }
        }
    }
}
