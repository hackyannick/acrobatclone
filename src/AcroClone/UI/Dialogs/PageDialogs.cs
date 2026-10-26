using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AcroClone.UI.Dialogs
{
    public class PasswordDialog : DialogBase
    {
        private readonly TextBox pwd;

        public PasswordDialog(string fileName) : base("Kennwort", 380, 150)
        {
            PictureBox icon = new PictureBox();
            icon.Image = Icons.Get("secure", 32);
            icon.SetBounds(14, 16, 32, 32);
            Controls.Add(icon);
            Label l = AddLabel(this, "\"" + fileName + "\" ist geschützt. Geben Sie ein Kennwort zum Öffnen des Dokuments ein.", 60, 12, 300);
            l.Height = 34;
            AddLabel(this, "Kennwort eingeben:", 60, 56, 120);
            pwd = AddText(this, "", 180, 56, 180);
            pwd.UseSystemPasswordChar = true;
        }

        public string Password { get { return pwd.Text; } }
    }

    public class InputDialog : DialogBase
    {
        private readonly TextBox input;

        public InputDialog(string title, string prompt, string value) : base(title, 340, 110)
        {
            AddLabel(this, prompt, 12, 12, 316);
            input = AddText(this, value, 12, 34, 316);
        }

        public string Value { get { return input.Text; } }
    }

    public class GoToPageDialog : DialogBase
    {
        private readonly NumericUpDown page;

        public GoToPageDialog(int current, int count) : base("Gehe zu Seite", 260, 100)
        {
            AddLabel(this, "Seite:", 14, 18, 50);
            page = AddNumber(this, current + 1, 1, Math.Max(1, count), 0, 64, 16, 70);
            AddLabel(this, "von " + count, 140, 18, 80);
            Shown += delegate { page.Select(0, page.Text.Length); page.Focus(); };
        }

        public int PageIndex { get { return (int)page.Value - 1; } }
    }

    public class DeletePagesDialog : DialogBase
    {
        public readonly PageRangeGroup Range;

        public DeletePagesDialog(int count, int current, IList<int> selection) : base("Seiten löschen", 360, 160)
        {
            Range = new PageRangeGroup(count, current, selection, false);
            Range.SetBounds(12, 10, 336, 104);
            Controls.Add(Range);
        }

        protected override bool ValidateInput()
        {
            return true;
        }
    }

    public class RotatePagesDialog : DialogBase
    {
        private readonly ComboBox direction, orientation;
        public readonly PageRangeGroup Range;

        public RotatePagesDialog(int count, int current, IList<int> selection) : base("Seiten drehen", 380, 250)
        {
            AddLabel(this, "Richtung:", 14, 14, 70);
            direction = AddCombo(this, new string[] { "90 Grad im Uhrzeigersinn", "90 Grad gegen den Uhrzeigersinn", "180 Grad" }, 0, 90, 12, 270);
            Range = new PageRangeGroup(count, current, selection, true);
            Range.SetBounds(12, 44, 356, 128);
            Controls.Add(Range);
            AddLabel(this, "Drehen:", 14, 182, 70);
            orientation = AddCombo(this, new string[] { "Seiten im Hoch- und Querformat", "Nur Seiten im Hochformat", "Nur Seiten im Querformat" }, 0, 90, 180, 270);
        }

        public int QuarterTurns
        {
            get { return direction.SelectedIndex == 0 ? 1 : direction.SelectedIndex == 1 ? 3 : 2; }
        }

        /// <summary>0 = alle, 1 = nur Hochformat, 2 = nur Querformat.</summary>
        public int OrientationFilter { get { return orientation.SelectedIndex; } }
    }

    public class ExtractPagesDialog : DialogBase
    {
        public readonly PageRangeGroup Range;
        private readonly CheckBox deleteAfter, separate;

        public ExtractPagesDialog(int count, int current, IList<int> selection) : base("Seiten extrahieren", 360, 210)
        {
            Range = new PageRangeGroup(count, current, selection, false);
            Range.SetBounds(12, 10, 336, 104);
            Controls.Add(Range);
            deleteAfter = AddCheck(this, "Seiten nach dem Extrahieren löschen", false, 16, 122, 320);
            separate = AddCheck(this, "Seiten als separate Dateien extrahieren", false, 16, 144, 320);
        }

        public bool DeleteAfter { get { return deleteAfter.Checked; } }
        public bool Separate { get { return separate.Checked; } }
    }

    public class InsertPagesDialog : DialogBase
    {
        private readonly ComboBox location;
        private readonly RadioButton first, last, page;
        private readonly NumericUpDown pageNo;
        private readonly int count;

        public InsertPagesDialog(string fileName, int count, int current) : base("Seiten einfügen", 360, 200)
        {
            this.count = count;
            AddLabel(this, "Datei: " + fileName, 14, 10, 330);
            AddLabel(this, "Position:", 14, 38, 70);
            location = AddCombo(this, new string[] { "Nach", "Vor" }, 0, 90, 36, 120);
            GroupBox g = AddGroup("Seite", 12, 66, 336, 88);
            first = AddRadio(g, "Erste", false, 12, 18, 100);
            last = AddRadio(g, "Letzte", true, 12, 38, 100);
            page = AddRadio(g, "Seite:", false, 12, 58, 60);
            pageNo = AddNumber(g, current + 1, 1, Math.Max(1, count), 0, 76, 58, 60);
            AddLabel(g, "von " + count, 142, 58, 80);
            pageNo.ValueChanged += delegate { page.Checked = true; };
        }

        /// <summary>Index, an dem die neuen Seiten eingefügt werden.</summary>
        public int InsertIndex
        {
            get
            {
                int target = first.Checked ? 0 : last.Checked ? count - 1 : (int)pageNo.Value - 1;
                bool after = location.SelectedIndex == 0;
                return Math.Max(0, Math.Min(count, after ? target + 1 : target));
            }
        }
    }

    public class CropPagesDialog : DialogBase
    {
        private readonly NumericUpDown top, bottom, left, right;
        public readonly PageRangeGroup Range;

        public CropPagesDialog(int count, int current, IList<int> selection) : base("Seiten beschneiden", 380, 270)
        {
            GroupBox m = AddGroup("Randeinstellungen (Millimeter)", 12, 10, 356, 96);
            AddLabel(m, "Oben:", 14, 24, 50); top = AddNumber(m, 0, 0, 500, 1, 70, 22, 70);
            AddLabel(m, "Unten:", 14, 56, 50); bottom = AddNumber(m, 0, 0, 500, 1, 70, 54, 70);
            AddLabel(m, "Links:", 180, 24, 50); left = AddNumber(m, 0, 0, 500, 1, 236, 22, 70);
            AddLabel(m, "Rechts:", 180, 56, 50); right = AddNumber(m, 0, 0, 500, 1, 236, 54, 70);
            Range = new PageRangeGroup(count, current, selection, false);
            Range.SetBounds(12, 114, 356, 104);
            Range.SelectAll();
            Controls.Add(Range);
        }

        private static double Pt(NumericUpDown n) { return (double)n.Value / 25.4 * 72.0; }
        public double MarginTop { get { return Pt(top); } }
        public double MarginBottom { get { return Pt(bottom); } }
        public double MarginLeft { get { return Pt(left); } }
        public double MarginRight { get { return Pt(right); } }
    }

    public class SplitDialog : DialogBase
    {
        private readonly NumericUpDown pages;
        private readonly TextBox folder;

        public SplitDialog(string initialFolder) : base("Dokument teilen", 420, 160)
        {
            AddLabel(this, "Maximale Seitenanzahl pro Datei:", 14, 16, 200);
            pages = AddNumber(this, 1, 1, 10000, 0, 220, 14, 70);
            AddLabel(this, "Zielordner:", 14, 50, 80);
            folder = AddText(this, initialFolder, 96, 48, 220);
            AddButton(this, "Durchsuchen...", 322, 46, 90, delegate
            {
                using (FolderBrowserDialog d = new FolderBrowserDialog())
                {
                    d.SelectedPath = folder.Text;
                    if (d.ShowDialog(this) == DialogResult.OK) folder.Text = d.SelectedPath;
                }
            });
        }

        public int PagesPerFile { get { return (int)pages.Value; } }
        public string Folder { get { return folder.Text; } }

        protected override bool ValidateInput()
        {
            if (!Directory.Exists(folder.Text)) { Error("Der Zielordner existiert nicht."); return false; }
            return true;
        }
    }
}
