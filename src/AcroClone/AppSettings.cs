using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace AcroClone
{
    /// <summary>Einfache Einstellungsdatei unter %APPDATA%\AcroClone\settings.ini.</summary>
    public static class AppSettings
    {
        private static readonly Dictionary<string, string> values = new Dictionary<string, string>();
        private static bool loaded;

        public static string Folder
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AcroClone");
                return dir;
            }
        }

        private static string FilePath { get { return Path.Combine(Folder, "settings.ini"); } }

        public static void Load()
        {
            loaded = true;
            values.Clear();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Replace("\\n", "\n");
                }
            }
            catch (Exception)
            {
                // Beschädigte Einstellungen ignorieren
            }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in values)
                    lines.Add(kv.Key + "=" + (kv.Value ?? string.Empty).Replace("\r", "").Replace("\n", "\\n"));
                File.WriteAllLines(FilePath, lines.ToArray(), Encoding.UTF8);
            }
            catch (Exception)
            {
                // Einstellungen sind nicht kritisch
            }
        }

        public static string Get(string key, string def)
        {
            if (!loaded) Load();
            string v;
            return values.TryGetValue(key, out v) ? v : def;
        }

        public static void Set(string key, string value)
        {
            if (!loaded) Load();
            values[key] = value;
        }

        public static int GetInt(string key, int def)
        {
            int v;
            return int.TryParse(Get(key, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def;
        }

        public static void SetInt(string key, int value)
        {
            Set(key, value.ToString(CultureInfo.InvariantCulture));
        }

        public static bool GetBool(string key, bool def)
        {
            string v = Get(key, null);
            if (v == null) return def;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public static void SetBool(string key, bool value)
        {
            Set(key, value ? "1" : "0");
        }

        public static Color GetColor(string key, Color def)
        {
            string v = Get(key, null);
            if (string.IsNullOrEmpty(v)) return def;
            try { return Model.Annotation.ParseColor(v); }
            catch (Exception) { return def; }
        }

        public static void SetColor(string key, Color c)
        {
            Set(key, Model.Annotation.ColorToString(c));
        }

        // Häufig verwendete Werte -------------------------------------------

        public static string Author
        {
            get { return Get("Author", Environment.UserName); }
            set { Set("Author", value); }
        }

        public static List<string> RecentFiles
        {
            get
            {
                List<string> list = new List<string>();
                foreach (string s in Get("RecentFiles", "").Split('|'))
                    if (s.Length > 0) list.Add(s);
                return list;
            }
        }

        public static void AddRecentFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            List<string> list = RecentFiles;
            list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, path);
            int max = GetInt("RecentCount", 8);
            if (list.Count > max) list.RemoveRange(max, list.Count - max);
            Set("RecentFiles", string.Join("|", list.ToArray()));
            Save();
        }

        public static void ClearRecentFiles()
        {
            Set("RecentFiles", "");
            Save();
        }
    }
}
