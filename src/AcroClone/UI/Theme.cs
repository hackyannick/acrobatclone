using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AcroClone.UI
{
    /// <summary>Farben und Schriften im Stil von Acrobat 9 (Windows XP/Vista-Ära).</summary>
    public static class Theme
    {
        public static readonly Font UIFont = CreateFont("Tahoma", 8.25f, FontStyle.Regular);
        public static readonly Font UIFontBold = CreateFont("Tahoma", 8.25f, FontStyle.Bold);
        public static readonly Font SmallFont = CreateFont("Tahoma", 7.5f, FontStyle.Regular);

        public static readonly Color DocumentBackground = Color.FromArgb(118, 124, 132);
        public static readonly Color FullScreenBackground = Color.Black;
        public static readonly Color PageShadow = Color.FromArgb(70, 0, 0, 0);
        public static readonly Color PageBorder = Color.FromArgb(40, 40, 40);

        public static readonly Color ToolbarTop = Color.FromArgb(250, 251, 253);
        public static readonly Color ToolbarMiddle = Color.FromArgb(236, 239, 244);
        public static readonly Color ToolbarBottom = Color.FromArgb(214, 219, 228);
        public static readonly Color ToolbarBorder = Color.FromArgb(160, 167, 180);
        public static readonly Color PanelBackground = Color.FromArgb(226, 230, 236);

        public static readonly Color HoverTop = Color.FromArgb(255, 255, 255);
        public static readonly Color HoverBottom = Color.FromArgb(214, 229, 248);
        public static readonly Color HoverBorder = Color.FromArgb(129, 160, 210);
        public static readonly Color PressedTop = Color.FromArgb(196, 215, 242);
        public static readonly Color PressedBottom = Color.FromArgb(165, 194, 236);
        public static readonly Color PressedBorder = Color.FromArgb(86, 125, 190);

        public static readonly Color NavStripTop = Color.FromArgb(236, 239, 243);
        public static readonly Color NavStripBottom = Color.FromArgb(210, 215, 223);
        public static readonly Color PaneHeaderTop = Color.FromArgb(253, 253, 254);
        public static readonly Color PaneHeaderBottom = Color.FromArgb(222, 226, 233);

        public static readonly Color MessageBarBack = Color.FromArgb(222, 216, 238);
        public static readonly Color MessageBarBorder = Color.FromArgb(150, 136, 192);

        public static readonly Color SelectionFill = Color.FromArgb(80, 51, 120, 230);
        public static readonly Color AcrobatRed = Color.FromArgb(178, 18, 18);

        private static Font CreateFont(string family, float size, FontStyle style)
        {
            try
            {
                Font f = new Font(family, size, style, GraphicsUnit.Point);
                if (f.Name == family) return f;
                f.Dispose();
            }
            catch (Exception)
            {
            }
            return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style, GraphicsUnit.Point);
        }

        public static void FillGradient(Graphics g, Rectangle r, Color top, Color bottom)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2), top, bottom, LinearGradientMode.Vertical))
                g.FillRectangle(b, r);
        }

        public static void FillToolbarGradient(Graphics g, Rectangle r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(r.X, r.Y - 1, r.Width, r.Height + 2), ToolbarTop, ToolbarBottom, LinearGradientMode.Vertical))
            {
                ColorBlend blend = new ColorBlend(3);
                blend.Colors = new Color[] { ToolbarTop, ToolbarMiddle, ToolbarBottom };
                blend.Positions = new float[] { 0f, 0.45f, 1f };
                b.InterpolationColors = blend;
                g.FillRectangle(b, r);
            }
        }

        public static void DrawHoverBox(Graphics g, Rectangle r, bool pressed)
        {
            if (r.Width <= 2 || r.Height <= 2) return;
            Rectangle inner = new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1);
            FillGradient(g, inner, pressed ? PressedTop : HoverTop, pressed ? PressedBottom : HoverBottom);
            using (Pen p = new Pen(pressed ? PressedBorder : HoverBorder))
                g.DrawRectangle(p, inner);
        }
    }

    public class Acrobat9ColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin { get { return Theme.ToolbarTop; } }
        public override Color ToolStripGradientMiddle { get { return Theme.ToolbarMiddle; } }
        public override Color ToolStripGradientEnd { get { return Theme.ToolbarBottom; } }
        public override Color ToolStripBorder { get { return Theme.ToolbarBorder; } }
        public override Color ToolStripPanelGradientBegin { get { return Color.FromArgb(222, 226, 233); } }
        public override Color ToolStripPanelGradientEnd { get { return Color.FromArgb(206, 211, 220); } }
        public override Color ToolStripContentPanelGradientBegin { get { return Theme.DocumentBackground; } }
        public override Color ToolStripContentPanelGradientEnd { get { return Theme.DocumentBackground; } }
        public override Color MenuStripGradientBegin { get { return Color.FromArgb(246, 247, 249); } }
        public override Color MenuStripGradientEnd { get { return Color.FromArgb(226, 230, 236); } }
        public override Color MenuBorder { get { return Color.FromArgb(128, 136, 150); } }
        public override Color MenuItemBorder { get { return Theme.HoverBorder; } }
        public override Color MenuItemSelected { get { return Color.FromArgb(220, 232, 250); } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.HoverTop; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.HoverBottom; } }
        public override Color MenuItemPressedGradientBegin { get { return Color.FromArgb(240, 242, 246); } }
        public override Color MenuItemPressedGradientMiddle { get { return Color.FromArgb(232, 235, 240); } }
        public override Color MenuItemPressedGradientEnd { get { return Color.FromArgb(224, 228, 234); } }
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(250, 250, 251); } }
        public override Color ImageMarginGradientBegin { get { return Color.FromArgb(240, 241, 244); } }
        public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(236, 238, 242); } }
        public override Color ImageMarginGradientEnd { get { return Color.FromArgb(228, 231, 236); } }
        public override Color SeparatorDark { get { return Color.FromArgb(160, 166, 176); } }
        public override Color SeparatorLight { get { return Color.FromArgb(255, 255, 255); } }
        public override Color ButtonSelectedBorder { get { return Theme.HoverBorder; } }
        public override Color ButtonSelectedGradientBegin { get { return Theme.HoverTop; } }
        public override Color ButtonSelectedGradientMiddle { get { return Color.FromArgb(236, 243, 252); } }
        public override Color ButtonSelectedGradientEnd { get { return Theme.HoverBottom; } }
        public override Color ButtonSelectedHighlight { get { return Color.FromArgb(214, 229, 248); } }
        public override Color ButtonPressedBorder { get { return Theme.PressedBorder; } }
        public override Color ButtonPressedGradientBegin { get { return Theme.PressedTop; } }
        public override Color ButtonPressedGradientMiddle { get { return Color.FromArgb(180, 204, 239); } }
        public override Color ButtonPressedGradientEnd { get { return Theme.PressedBottom; } }
        public override Color ButtonPressedHighlight { get { return Theme.PressedTop; } }
        public override Color ButtonCheckedGradientBegin { get { return Color.FromArgb(222, 233, 249); } }
        public override Color ButtonCheckedGradientMiddle { get { return Color.FromArgb(206, 222, 246); } }
        public override Color ButtonCheckedGradientEnd { get { return Color.FromArgb(190, 210, 242); } }
        public override Color ButtonCheckedHighlight { get { return Color.FromArgb(206, 222, 246); } }
        public override Color CheckBackground { get { return Color.FromArgb(206, 222, 246); } }
        public override Color CheckSelectedBackground { get { return Color.FromArgb(190, 210, 242); } }
        public override Color CheckPressedBackground { get { return Color.FromArgb(170, 196, 236); } }
        public override Color GripDark { get { return Color.FromArgb(120, 128, 142); } }
        public override Color GripLight { get { return Color.White; } }
        public override Color OverflowButtonGradientBegin { get { return Color.FromArgb(220, 224, 231); } }
        public override Color OverflowButtonGradientMiddle { get { return Color.FromArgb(200, 206, 216); } }
        public override Color OverflowButtonGradientEnd { get { return Color.FromArgb(176, 184, 198); } }
        public override Color StatusStripGradientBegin { get { return Color.FromArgb(240, 242, 245); } }
        public override Color StatusStripGradientEnd { get { return Color.FromArgb(218, 222, 229); } }
    }

    public class Acrobat9Renderer : ToolStripProfessionalRenderer
    {
        public Acrobat9Renderer() : base(new Acrobat9ColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown || e.ToolStrip is MenuStrip || e.ToolStrip is StatusStrip)
            {
                base.OnRenderToolStripBackground(e);
                return;
            }
            Theme.FillToolbarGradient(e.Graphics, new Rectangle(Point.Empty, e.ToolStrip.Size));
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDown)
            {
                base.OnRenderToolStripBorder(e);
                return;
            }
            if (e.ToolStrip is MenuStrip || e.ToolStrip is StatusStrip) return;
            Rectangle r = new Rectangle(Point.Empty, e.ToolStrip.Size);
            using (Pen p = new Pen(Color.FromArgb(178, 184, 196)))
                e.Graphics.DrawLine(p, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1);
            using (Pen p = new Pen(Color.FromArgb(255, 255, 255)))
                e.Graphics.DrawLine(p, r.Left, r.Top, r.Right, r.Top);
        }

        protected override void OnRenderGrip(ToolStripGripRenderEventArgs e)
        {
            if (e.GripStyle != ToolStripGripStyle.Visible) return;
            Rectangle r = e.GripBounds;
            // Klassischer gepunkteter "Anfasser" am linken Rand einer Werkzeugleiste
            int x = r.Left + r.Width / 2 - 1;
            for (int y = r.Top + 4; y < r.Bottom - 4; y += 4)
            {
                e.Graphics.FillRectangle(Brushes.White, x + 1, y + 1, 2, 2);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(132, 140, 154)))
                    e.Graphics.FillRectangle(b, x, y, 2, 2);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDownMenu || e.Vertical == false)
            {
                base.OnRenderSeparator(e);
                return;
            }
            Rectangle r = e.Item.ContentRectangle;
            int x = r.Left + r.Width / 2;
            using (Pen dark = new Pen(Color.FromArgb(166, 172, 184)))
                e.Graphics.DrawLine(dark, x, r.Top + 3, x, r.Bottom - 3);
            e.Graphics.DrawLine(Pens.White, x + 1, r.Top + 3, x + 1, r.Bottom - 3);
        }
    }
}
