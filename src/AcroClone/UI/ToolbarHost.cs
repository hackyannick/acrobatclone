using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace AcroClone.UI
{
    /// <summary>
    /// Nimmt mehrere Werkzeugleisten in Zeilen auf (wie die andockbaren Leisten in Acrobat 9)
    /// und zeichnet den durchgehenden Verlaufshintergrund jeder Zeile.
    /// </summary>
    public class ToolbarHost : FlowLayoutPanel
    {
        private readonly List<List<ToolStrip>> rows = new List<List<ToolStrip>>();

        public ToolbarHost()
        {
            Dock = DockStyle.Top;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            FlowDirection = FlowDirection.LeftToRight;
            WrapContents = true;
            Padding = new Padding(0);
            Margin = new Padding(0);
            BackColor = Theme.ToolbarMiddle;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void AddRow(params ToolStrip[] strips)
        {
            List<ToolStrip> row = new List<ToolStrip>(strips);
            rows.Add(row);
            foreach (ToolStrip t in strips)
            {
                t.Dock = DockStyle.None;
                t.AutoSize = true;
                t.Margin = new Padding(0, 0, 2, 0);
                t.GripStyle = ToolStripGripStyle.Visible;
                t.Stretch = false;
                t.VisibleChanged += delegate { UpdateBreaks(); };
                Controls.Add(t);
            }
            UpdateBreaks();
        }

        public void UpdateBreaks()
        {
            foreach (List<ToolStrip> row in rows)
            {
                foreach (ToolStrip t in row) SetFlowBreak(t, false);
                ToolStrip last = row.LastOrDefault(t => t.Visible);
                if (last != null) SetFlowBreak(last, true);
            }
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.ToolbarMiddle);
            foreach (IGrouping<int, Control> row in Controls.Cast<Control>().Where(c => c.Visible).GroupBy(c => c.Top))
            {
                int h = row.Max(c => c.Height);
                Rectangle r = new Rectangle(0, row.Key, Width, h);
                Theme.FillToolbarGradient(g, r);
                using (Pen p = new Pen(Color.FromArgb(178, 184, 196))) g.DrawLine(p, 0, r.Bottom - 1, Width, r.Bottom - 1);
                g.DrawLine(Pens.White, 0, r.Top, Width, r.Top);
            }
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            Invalidate();
        }
    }
}
