using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using AcroClone.Model;
using AcroClone.Pdf;
using AcroClone.UI.Dialogs;

namespace AcroClone.UI
{
    /// <summary>Hauptfenster im Stil von Acrobat 9.</summary>
    public class MainForm : Form
    {
        private DocumentSession session;

        private MenuStrip menu;
        private ToolbarHost toolbars;
        private ToolStrip tasksBar, fileBar, navBar, zoomBar, displayBar, findBar, commentBar;
        private StatusStrip status;
        private ToolStripStatusLabel statusText, pageSizeLabel;

        private Panel workArea, center;
        private NavigationPane nav;
        private Splitter navSplitter, searchSplitter, commentsSplitter;
        private SearchPane search;
        private CommentsPane comments;
        private Panel messageBar;
        private Label messageText;
        private Button messageButton;
        private DocumentView view;
        private WelcomePanel welcome;

        private ToolStripTextBox pageBox;
        private ToolStripLabel pageCountLabel;
        private ToolStripComboBox zoomBox;
        private ToolStripTextBox findBox;
        private ToolStripMenuItem undoItem, redoItem, recentSeparator, windowMenu, fileMenu;
        private readonly List<ToolStripItem> recentItems = new List<ToolStripItem>();
        private readonly List<ToolStripItem> docItems = new List<ToolStripItem>();
        private readonly Dictionary<ToolMode, List<ToolStripItem>> toolItems = new Dictionary<ToolMode, List<ToolStripItem>>();
        private readonly Dictionary<PageLayoutMode, ToolStripMenuItem> layoutItems = new Dictionary<PageLayoutMode, ToolStripMenuItem>();
        private ToolStripMenuItem coverItem, highlightFieldsItem, commentBarItem, commentsListItem;
        private readonly Dictionary<ToolStrip, ToolStripMenuItem> toolbarItems = new Dictionary<ToolStrip, ToolStripMenuItem>();
        private ContextMenuStrip pagesMenu;
        private bool highlightForms = true;

        // Suchen in der Werkzeugleiste
        private string findQuery;
        private readonly Dictionary<int, List<TextMatch>> findCache = new Dictionary<int, List<TextMatch>>();
        private int findPage = -1, findIndex = -1;

        // Vollbild / Lesemodus
        private bool fullScreen, readMode;
        private FormWindowState savedState;
        private FormBorderStyle savedBorder;
        private bool savedComments, savedSearch;

        public MainForm()
        {
            Text = "AcroClone 9 Pro";
            Font = Theme.UIFont;
            Icon = MakeIcon();
            KeyPreview = true;
            AllowDrop = true;
            StartPosition = FormStartPosition.WindowsDefaultLocation;
            Size = new Size(AppSettings.GetInt("Window.Width", 1100), AppSettings.GetInt("Window.Height", 800));
            MinimumSize = new Size(560, 400);
            if (AppSettings.GetBool("Window.Maximized", false)) WindowState = FormWindowState.Maximized;
            highlightForms = AppSettings.GetBool("HighlightForms", true);

            ToolStripManager.Renderer = new Acrobat9Renderer();
            BuildLayout();
            BuildMenus();
            BuildToolbars();
            UpdateUI();
        }

        private static Icon MakeIcon()
        {
            try
            {
                if (Native.IsWindows)
                {
                    Icon exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                    if (exeIcon != null) return exeIcon;
                }
            }
            catch (Exception)
            {
            }
            try
            {
                using (Bitmap b = new Bitmap(Icons.Get("logo", 32)))
                    return Icon.FromHandle(b.GetHicon());
            }
            catch (Exception)
            {
                return null;
            }
        }

        public DocumentSession Session { get { return session; } }

        // ==================================================================
        // Aufbau
        // ==================================================================

        private void BuildLayout()
        {
            view = new DocumentView();
            view.Dock = DockStyle.Fill;
            view.Visible = true;
            view.CurrentPageChanged += delegate { UpdatePageInfo(); };
            view.ZoomChanged += delegate { UpdateZoomBox(); };
            view.ToolChanged += delegate { UpdateToolChecks(); };
            view.SelectionChanged += delegate { UpdateUI(); };
            view.ViewContextMenu += OnViewContextMenu;
            view.StatusMessage += delegate(object s, StatusEventArgs e) { statusText.Text = e.Text; };
            view.FullScreenExitRequested += delegate { ToggleFullScreen(); };

            welcome = new WelcomePanel();
            welcome.Dock = DockStyle.Fill;
            welcome.CommandClicked += OnWelcomeCommand;

            messageBar = new Panel();
            messageBar.Dock = DockStyle.Top;
            messageBar.Height = 30;
            messageBar.BackColor = Theme.MessageBarBack;
            messageBar.Visible = false;
            messageBar.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Theme.MessageBarBorder)) e.Graphics.DrawLine(p, 0, messageBar.Height - 1, messageBar.Width, messageBar.Height - 1);
                e.Graphics.DrawImage(Icons.Get("forms", 16), 8, 7, 16, 16);
            };
            messageText = new Label();
            messageText.AutoSize = false;
            messageText.SetBounds(30, 8, 520, 16);
            messageText.Text = "Dieses Formular enthält ausfüllbare Felder. Klicken Sie in ein Feld, um es auszufüllen.";
            messageButton = new Button();
            messageButton.Text = "Vorhandene Felder markieren";
            messageButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            messageButton.FlatStyle = FlatStyle.System;
            messageButton.Click += delegate { ToggleFormHighlight(); };
            messageBar.Controls.Add(messageText);
            messageBar.Controls.Add(messageButton);
            messageBar.Resize += delegate { messageButton.SetBounds(messageBar.Width - 196, 3, 188, 23); };

            center = new Panel();
            center.Dock = DockStyle.Fill;
            center.Controls.Add(view);
            center.Controls.Add(welcome);
            center.Controls.Add(messageBar);
            welcome.BringToFront();

            nav = new NavigationPane();
            nav.Dock = DockStyle.Left;
            nav.View = view;
            nav.PanelWidth = AppSettings.GetInt("NavWidth", 200);
            nav.ExpandedChanged += delegate { navSplitter.Visible = nav.IsExpanded; };
            nav.StripCommand += delegate(object s, ItemEventArgs e) { if (e.Key == "comments") ToggleComments(); };
            nav.Thumbnails.ContextRequested += delegate(object s, MouseEventArgs e) { UpdateUI(); pagesMenu.Show(nav.Thumbnails, e.Location); };
            nav.Thumbnails.DeleteRequested += delegate { DeletePages(); };
            navSplitter = new Splitter();
            navSplitter.Dock = DockStyle.Left;
            navSplitter.Width = 4;
            navSplitter.Visible = false;
            navSplitter.BackColor = Theme.PanelBackground;
            navSplitter.MinSize = 60;

            search = new SearchPane();
            search.Dock = DockStyle.Right;
            search.View = view;
            search.Visible = false;
            search.CloseRequested += delegate { ShowSearchPane(false); };
            searchSplitter = new Splitter();
            searchSplitter.Dock = DockStyle.Right;
            searchSplitter.Width = 4;
            searchSplitter.Visible = false;
            searchSplitter.BackColor = Theme.PanelBackground;

            comments = new CommentsPane();
            comments.Dock = DockStyle.Bottom;
            comments.View = view;
            comments.Visible = false;
            comments.Height = AppSettings.GetInt("CommentsHeight", 190);
            comments.CloseRequested += delegate { ShowComments(false); };
            comments.PropertiesRequested += delegate { AnnotationProperties(); };
            commentsSplitter = new Splitter();
            commentsSplitter.Dock = DockStyle.Bottom;
            commentsSplitter.Height = 4;
            commentsSplitter.Visible = false;
            commentsSplitter.BackColor = Theme.PanelBackground;

            workArea = new Panel();
            workArea.Dock = DockStyle.Fill;
            workArea.BackColor = Theme.DocumentBackground;
            // Reihenfolge: zuletzt hinzugefügte Steuerelemente werden zuerst angedockt
            workArea.Controls.Add(center);
            workArea.Controls.Add(commentsSplitter);
            workArea.Controls.Add(comments);
            workArea.Controls.Add(searchSplitter);
            workArea.Controls.Add(search);
            workArea.Controls.Add(navSplitter);
            workArea.Controls.Add(nav);

            status = new StatusStrip();
            status.SizingGrip = true;
            statusText = new ToolStripStatusLabel();
            statusText.Spring = true;
            statusText.TextAlign = ContentAlignment.MiddleLeft;
            pageSizeLabel = new ToolStripStatusLabel();
            pageSizeLabel.BorderSides = ToolStripStatusLabelBorderSides.Left;
            status.Items.Add(statusText);
            status.Items.Add(pageSizeLabel);

            toolbars = new ToolbarHost();
            menu = new MenuStrip();
            menu.GripStyle = ToolStripGripStyle.Hidden;
            MainMenuStrip = menu;

            Controls.Add(workArea);
            Controls.Add(status);
            Controls.Add(toolbars);
            Controls.Add(menu);
        }

        // ------------------------------------------------------------------
        // Menü-Hilfen
        // ------------------------------------------------------------------

        private ToolStripMenuItem Item(string text, string icon, Keys keys, string keyText, EventHandler click, bool needsDoc)
        {
            ToolStripMenuItem i = new ToolStripMenuItem(text);
            if (icon != null) i.Image = Icons.Get(icon);
            if (keys != Keys.None) i.ShortcutKeys = keys;
            if (keyText != null) i.ShortcutKeyDisplayString = keyText;
            if (click != null) i.Click += click;
            if (needsDoc) docItems.Add(i);
            return i;
        }

        private ToolStripMenuItem Item(string text, string icon, EventHandler click, bool needsDoc)
        {
            return Item(text, icon, Keys.None, null, click, needsDoc);
        }

        private ToolStripMenuItem Sub(string text, string icon, params ToolStripItem[] items)
        {
            ToolStripMenuItem m = new ToolStripMenuItem(text);
            if (icon != null) m.Image = Icons.Get(icon);
            m.DropDownItems.AddRange(items);
            return m;
        }

        private static ToolStripSeparator Sep() { return new ToolStripSeparator(); }

        private ToolStripMenuItem ToolItem(string text, string icon, ToolMode tool, string keyText)
        {
            ToolStripMenuItem i = Item(text, icon, Keys.None, keyText, delegate { SelectTool(tool); }, true);
            RegisterTool(tool, i);
            return i;
        }

        private void RegisterTool(ToolMode tool, ToolStripItem item)
        {
            List<ToolStripItem> list;
            if (!toolItems.TryGetValue(tool, out list)) toolItems[tool] = list = new List<ToolStripItem>();
            list.Add(item);
        }

        private ToolStripItem[] StampItems()
        {
            string[][] stamps =
            {
                new[] { "GENEHMIGT", "1E8228" }, new[] { "NICHT GENEHMIGT", "C81414" }, new[] { "ENTWURF", "1E46B4" },
                new[] { "VERTRAULICH", "C81414" }, new[] { "ENDGÜLTIG", "1E8228" }, new[] { "VORLÄUFIG", "D26E00" },
                new[] { "ZUR KOMMENTIERUNG", "1E46B4" }, new[] { "NUR ZUR INFORMATION", "1E46B4" }, new[] { "ABGELAUFEN", "C81414" },
                new[] { "GEPRÜFT", "1E8228" }, new[] { "ERLEDIGT", "1E8228" }
            };
            List<ToolStripItem> items = new List<ToolStripItem>();
            foreach (string[] s in stamps)
            {
                string text = s[0];
                Color c = Color.FromArgb(int.Parse(s[1], NumberStyles.HexNumber) | unchecked((int)0xFF000000));
                Bitmap sw = new Bitmap(16, 16);
                using (Graphics g = Graphics.FromImage(sw))
                {
                    using (Pen p = new Pen(c, 2)) g.DrawRectangle(p, 1, 4, 14, 8);
                }
                ToolStripMenuItem i = new ToolStripMenuItem(text, sw);
                i.Click += delegate
                {
                    if (view != null) { view.StampText = text; view.StampColor = c; }
                    SelectTool(ToolMode.Stamp);
                };
                docItems.Add(i);
                items.Add(i);
            }
            items.Add(Sep());
            items.Add(Item("Eigener Stempel...", null, delegate { CustomStamp(); }, true));
            return items.ToArray();
        }

        // ------------------------------------------------------------------
        // Menüs
        // ------------------------------------------------------------------

        private void BuildMenus()
        {
            fileMenu = new ToolStripMenuItem("&Datei");
            recentSeparator = new ToolStripMenuItem();
            fileMenu.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("Öffnen...", "open", Keys.Control | Keys.O, "Strg+O", delegate { OpenDialog(); }, false),
                Sub("PDF erstellen", "create",
                    Item("Aus Datei...", null, delegate { CreateFromFiles(); }, false),
                    Item("Aus Zwischenablage", null, delegate { CreateFromClipboard(); }, false),
                    Item("Leeres Dokument", "blankPage", delegate { CreateBlank(); }, false),
                    Sep(),
                    Item("Beispieldokument", "logo", delegate { CreateSample(); }, false)),
                Item("Dateien zu einem einzigen PDF zusammenführen...", "combine", delegate { CombineFiles(); }, false),
                Sep(),
                Item("Speichern", "save", Keys.Control | Keys.S, "Strg+S", delegate { Save(); }, true),
                Item("Speichern unter...", "saveAs", Keys.Control | Keys.Shift | Keys.S, "Umschalt+Strg+S", delegate { SaveAs(); }, true),
                Sub("Exportieren", null,
                    Item("Seiten als Bilder (PNG)...", null, delegate { ExportImages(ImageFormat.Png); }, true),
                    Item("Seiten als Bilder (JPEG)...", null, delegate { ExportImages(ImageFormat.Jpeg); }, true),
                    Item("Text (Nur-Text)...", null, delegate { ExportText(); }, true)),
                Sep(),
                Item("Schließen", "close", Keys.Control | Keys.W, "Strg+W", delegate { CloseDocument(); }, true),
                Sep(),
                Item("Eigenschaften...", "properties", Keys.Control | Keys.D, "Strg+D", delegate { DocumentProperties(0); }, true),
                Sep(),
                Item("Drucken...", "print", Keys.Control | Keys.P, "Strg+P", delegate { Print(); }, true),
                Item("Druckvorschau...", null, delegate { PrintPreview(); }, true),
                Sep(),
                recentSeparator,
                Item("Beenden", null, Keys.Control | Keys.Q, "Strg+Q", delegate { Program.ExitAll(); }, false)
            });
            recentSeparator.Visible = false;
            fileMenu.DropDownOpening += delegate { BuildRecentMenu(); };

            undoItem = Item("Rückgängig", "undo", Keys.Control | Keys.Z, "Strg+Z", delegate { Undo(); }, true);
            redoItem = Item("Wiederherstellen", "redo", Keys.Control | Keys.Y, "Strg+Y", delegate { Redo(); }, true);
            ToolStripMenuItem edit = new ToolStripMenuItem("&Bearbeiten");
            edit.DropDownItems.AddRange(new ToolStripItem[]
            {
                undoItem, redoItem, Sep(),
                Item("Kopieren", "copy", Keys.None, "Strg+C", delegate { Copy(); }, true),
                Item("Alles auswählen", null, Keys.None, "Strg+A", delegate { if (view.Visible) view.SelectAllText(); }, true),
                Item("Auswahl aufheben", null, Keys.Control | Keys.Shift | Keys.A, "Umschalt+Strg+A", delegate { view.ClearTextSelection(); view.Deselect(); }, true),
                Sep(),
                Item("Kommentar löschen", "delete", Keys.None, "Entf", delegate { view.DeleteSelected(); }, true),
                Sep(),
                Item("Suchen", "find", Keys.Control | Keys.F, "Strg+F", delegate { FocusFind(); }, true),
                Item("Erweiterte Suche", "search", Keys.Control | Keys.Shift | Keys.F, "Umschalt+Strg+F", delegate { ShowSearchPane(true); }, true),
                Sep(),
                Item("Voreinstellungen...", null, Keys.Control | Keys.K, "Strg+K", delegate { Preferences(); }, false)
            });

            ToolStripMenuItem viewMenu = new ToolStripMenuItem("&Anzeige");
            ToolStripMenuItem layoutMenu = Sub("Seitenanzeige", null);
            foreach (PageLayoutMode m in new PageLayoutMode[] { PageLayoutMode.SinglePage, PageLayoutMode.Continuous, PageLayoutMode.TwoUp, PageLayoutMode.TwoUpContinuous })
            {
                PageLayoutMode mm = m;
                string[] names = { "Einzelne Seite", "Einzelne Seite fortlaufend", "Zweiseitenansicht", "Zwei Seiten fortlaufend" };
                string[] icons = { "singlePage", "continuous", "twoUp", "twoUp" };
                ToolStripMenuItem li = Item(names[(int)m], icons[(int)m], delegate { view.SetPageLayout(mm, view.CoverPage); UpdateUI(); }, true);
                layoutItems[m] = li;
                layoutMenu.DropDownItems.Add(li);
            }
            coverItem = Item("Deckblatt bei Zweiseitenansicht anzeigen", null, delegate { view.SetPageLayout(view.PageLayout, !view.CoverPage); UpdateUI(); }, true);
            layoutMenu.DropDownItems.Add(Sep());
            layoutMenu.DropDownItems.Add(coverItem);

            ToolStripMenuItem toolbarsMenu = Sub("Werkzeugleisten", null);
            toolbarsMenu.DropDownOpening += delegate { foreach (KeyValuePair<ToolStrip, ToolStripMenuItem> kv in toolbarItems) kv.Value.Checked = kv.Key.Visible; };

            viewMenu.DropDownItems.AddRange(new ToolStripItem[]
            {
                Sub("Gehe zu", null,
                    Item("Erste Seite", "firstPage", Keys.None, "Pos1", delegate { view.FirstPage(); }, true),
                    Item("Vorherige Seite", "prevPage", Keys.None, "Bild auf", delegate { view.PreviousPage(); }, true),
                    Item("Nächste Seite", "nextPage", Keys.None, "Bild ab", delegate { view.NextPage(); }, true),
                    Item("Letzte Seite", "lastPage", Keys.None, "Ende", delegate { view.LastPage(); }, true),
                    Sep(),
                    Item("Seite...", null, Keys.Control | Keys.Shift | Keys.N, "Umschalt+Strg+N", delegate { GoToPageDialog(); }, true)),
                Sep(),
                Sub("Zoom", "zoomTool",
                    Item("Vergrößern", "zoomIn", Keys.Control | Keys.Oemplus, "Strg++", delegate { view.ZoomIn(); }, true),
                    Item("Verkleinern", "zoomOut", Keys.Control | Keys.OemMinus, "Strg+-", delegate { view.ZoomOut(); }, true),
                    Sep(),
                    Item("Tatsächliche Größe", "actualSize", Keys.Control | Keys.D1, "Strg+1", delegate { view.SetZoom(1.0); }, true),
                    Item("Zoomstufe ganze Seite", "fitPage", Keys.Control | Keys.D0, "Strg+0", delegate { view.SetZoomMode(ZoomMode.FitPage); }, true),
                    Item("Fensterbreite", "fitWidth", Keys.Control | Keys.D2, "Strg+2", delegate { view.SetZoomMode(ZoomMode.FitWidth); }, true),
                    Item("Sichtbaren Bereich anpassen", null, Keys.Control | Keys.D3, "Strg+3", delegate { view.SetZoomMode(ZoomMode.FitVisible); }, true)),
                layoutMenu,
                Sub("Ansicht drehen", "rotateCw",
                    Item("Im Uhrzeigersinn", "rotateCw", Keys.Control | Keys.Shift | Keys.Oemplus, "Umschalt+Strg++", delegate { view.SetRotation(view.ViewRotation + 1); }, true),
                    Item("Gegen den Uhrzeigersinn", "rotateCcw", Keys.Control | Keys.Shift | Keys.OemMinus, "Umschalt+Strg+-", delegate { view.SetRotation(view.ViewRotation - 1); }, true)),
                Sep(),
                toolbarsMenu,
                Sub("Navigationsfenster", null,
                    Item("Seiten", "pages", delegate { nav.ShowPanel("pages"); }, true),
                    Item("Lesezeichen", "bookmarks", delegate { nav.ShowPanel("bookmarks"); }, true),
                    Item("Anlagen", "attachments", delegate { nav.ShowPanel("attachments"); }, true),
                    Item("Kommentare", "comments", delegate { ToggleComments(); }, true),
                    Sep(),
                    Item("Navigationsfenster ein-/ausblenden", null, Keys.F4, "F4", delegate { if (nav.IsExpanded) nav.Collapse(); else nav.ShowPanel("pages"); }, true)),
                Sep(),
                Item("Vollbildmodus", "fullscreen", Keys.Control | Keys.L, "Strg+L", delegate { ToggleFullScreen(); }, true),
                Item("Lesemodus", "readMode", Keys.Control | Keys.H, "Strg+H", delegate { ToggleReadMode(); }, true)
            });

            ToolStripMenuItem doc = new ToolStripMenuItem("D&okument");
            doc.DropDownItems.AddRange(new ToolStripItem[]
            {
                Sub("Kopf- und Fußzeile", "headerFooter", Item("Hinzufügen...", null, delegate { AddHeaderFooter(); }, true)),
                Sub("Wasserzeichen", "watermark", Item("Hinzufügen...", null, delegate { AddWatermark(); }, true)),
                Sep(),
                Sub("Seiten einfügen", "insertPages",
                    Item("Aus Datei...", null, Keys.Control | Keys.Shift | Keys.I, "Umschalt+Strg+I", delegate { InsertPagesFromFile(); }, true),
                    Item("Aus Zwischenablage", null, delegate { InsertFromClipboard(); }, true),
                    Item("Leere Seite", "blankPage", delegate { InsertBlankPage(); }, true)),
                Item("Seiten extrahieren...", "extractPages", delegate { ExtractPages(); }, true),
                Item("Seiten löschen...", "deletePages", Keys.Control | Keys.Shift | Keys.D, "Umschalt+Strg+D", delegate { DeletePages(); }, true),
                Item("Seiten drehen...", "rotateCw", Keys.Control | Keys.Shift | Keys.R, "Umschalt+Strg+R", delegate { RotatePages(); }, true),
                Item("Seiten beschneiden...", "cropPages", Keys.Control | Keys.Shift | Keys.T, "Umschalt+Strg+T", delegate { CropPages(); }, true),
                Item("Dokument teilen...", null, delegate { SplitDocument(); }, true),
                Sep(),
                Sub("Unterschreiben", "sign",
                    Item("Unterschrift platzieren...", null, delegate { PlaceSignature(false); }, true),
                    Item("Neue Unterschrift erstellen...", null, delegate { PlaceSignature(true); }, true))
            });

            commentBarItem = Item("Werkzeugleiste \"Kommentieren und markieren\" einblenden", "pencil", delegate { ToggleToolbar(commentBar); }, false);
            commentsListItem = Item("Kommentarliste einblenden", "comments", delegate { ToggleComments(); }, true);
            ToolStripMenuItem commentMenu = new ToolStripMenuItem("&Kommentare");
            commentMenu.DropDownItems.AddRange(new ToolStripItem[]
            {
                ToolItem("Notiz hinzufügen", "note", ToolMode.Note, "S"),
                ToolItem("Text hervorheben", "highlight", ToolMode.Highlight, "U"),
                ToolItem("Text unterstreichen", "underline", ToolMode.Underline, null),
                ToolItem("Text durchstreichen", "strikeout", ToolMode.StrikeOut, null),
                ToolItem("Textfeld hinzufügen", "textbox", ToolMode.FreeText, null),
                Sep(),
                Sub("Kommentieren und markieren-Werkzeuge", "comment",
                    ToolItem("Rechteck-Werkzeug", "rectangle", ToolMode.Rectangle, null),
                    ToolItem("Ellipse-Werkzeug", "ellipse", ToolMode.Ellipse, null),
                    ToolItem("Linien-Werkzeug", "line", ToolMode.Line, null),
                    ToolItem("Pfeil-Werkzeug", "arrow", ToolMode.Arrow, null),
                    ToolItem("Bleistift-Werkzeug", "pencil", ToolMode.Pencil, null),
                    Sep(),
                    Sub("Stempel", "stamp", StampItems())),
                commentBarItem,
                Sep(),
                commentsListItem,
                Sep(),
                Item("Kommentare reduzieren (in Seiten einbetten)...", null, delegate { FlattenComments(); }, true),
                Item("Alle Kommentare löschen...", "delete", delegate { DeleteAllComments(); }, true)
            });

            highlightFieldsItem = Item("Vorhandene Felder markieren", "forms", delegate { ToggleFormHighlight(); }, true);
            ToolStripMenuItem forms = new ToolStripMenuItem("&Formulare");
            forms.DropDownItems.AddRange(new ToolStripItem[] { highlightFieldsItem });

            ToolStripMenuItem tools = new ToolStripMenuItem("&Werkzeuge");
            tools.DropDownItems.AddRange(new ToolStripItem[]
            {
                Sub("Auswählen und zoomen", null,
                    ToolItem("Hand-Werkzeug", "hand", ToolMode.Hand, "H"),
                    ToolItem("Auswahl-Werkzeug", "select", ToolMode.Select, "V"),
                    ToolItem("Auswahlrechteck-Zoom", "zoomTool", ToolMode.Zoom, "Z"),
                    ToolItem("Schnappschuss-Werkzeug", "snapshot", ToolMode.Snapshot, null)),
                Sub("Analyse", null, ToolItem("Messwerkzeug (Abstand)", "measure", ToolMode.Measure, null)),
                Sub("Kommentieren und markieren", "comment",
                    ToolItem("Notiz", "note", ToolMode.Note, null),
                    ToolItem("Text hervorheben", "highlight", ToolMode.Highlight, null),
                    ToolItem("Textfeld", "textbox", ToolMode.FreeText, null),
                    ToolItem("Bleistift", "pencil", ToolMode.Pencil, null))
            });

            ToolStripMenuItem advanced = new ToolStripMenuItem("&Erweitert");
            advanced.DropDownItems.AddRange(new ToolStripItem[]
            {
                Sub("Schwärzung", "redact",
                    ToolItem("Zum Schwärzen markieren", "redact", ToolMode.Redaction, null),
                    Item("Schwärzungen anwenden...", null, delegate { ApplyRedactions(); }, true)),
                Sub("Sicherheit", "secure",
                    Item("Sicherheitseigenschaften anzeigen...", null, delegate { DocumentProperties(1); }, true)),
                Sub("Dokumentverarbeitung", null,
                    Item("Seiten als Bilder exportieren...", null, delegate { ExportImages(ImageFormat.Png); }, true),
                    Item("Text exportieren...", null, delegate { ExportText(); }, true),
                    Item("Dokument teilen...", null, delegate { SplitDocument(); }, true))
            });

            windowMenu = new ToolStripMenuItem("Fe&nster");
            windowMenu.DropDownOpening += delegate { BuildWindowMenu(); };
            windowMenu.DropDownItems.Add("(leer)");

            ToolStripMenuItem help = new ToolStripMenuItem("&Hilfe");
            help.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("AcroClone-Hilfe", "help", Keys.F1, "F1", delegate { ShowHelp(); }, false),
                Sep(),
                Item("Über AcroClone 9 Pro...", "logo", delegate { using (AboutDialog d = new AboutDialog()) d.ShowDialog(this); }, false)
            });

            menu.Items.AddRange(new ToolStripItem[] { fileMenu, edit, viewMenu, doc, commentMenu, forms, tools, advanced, windowMenu, help });

            pagesMenu = new ContextMenuStrip();
            pagesMenu.Items.AddRange(new ToolStripItem[]
            {
                Item("Seiten einfügen...", "insertPages", delegate { InsertPagesFromFile(); }, true),
                Item("Leere Seite einfügen", "blankPage", delegate { InsertBlankPage(); }, true),
                Item("Seiten extrahieren...", "extractPages", delegate { ExtractPages(); }, true),
                Item("Seiten löschen...", "deletePages", delegate { DeletePages(); }, true),
                Item("Seiten drehen...", "rotateCw", delegate { RotatePages(); }, true),
                Item("Seiten beschneiden...", "cropPages", delegate { CropPages(); }, true),
                Sep(),
                Item("Seitenminiaturen vergrößern", "zoomIn", delegate { nav.Thumbnails.ThumbSize += 24; }, true),
                Item("Seitenminiaturen verkleinern", "zoomOut", delegate { nav.Thumbnails.ThumbSize -= 24; }, true),
                Sep(),
                Item("Seiten drucken...", "print", delegate { Print(); }, true)
            });
            nav.SetPagesOptions(pagesMenu);
        }

        // ------------------------------------------------------------------
        // Werkzeugleisten
        // ------------------------------------------------------------------

        private ToolStrip NewBar(string name)
        {
            ToolStrip t = new ToolStrip();
            t.Text = name;
            t.ImageScalingSize = new Size(16, 16);
            t.Padding = new Padding(2, 1, 2, 1);
            t.Renderer = new Acrobat9Renderer();
            return t;
        }

        private ToolStripButton Btn(string tip, string icon, EventHandler click, bool needsDoc)
        {
            ToolStripButton b = new ToolStripButton(Icons.Get(icon));
            b.ToolTipText = tip;
            b.Click += click;
            b.DisplayStyle = ToolStripItemDisplayStyle.Image;
            if (needsDoc) docItems.Add(b);
            return b;
        }

        private ToolStripButton ToolBtn(string tip, string icon, ToolMode tool)
        {
            ToolStripButton b = Btn(tip, icon, delegate { SelectTool(tool); }, true);
            RegisterTool(tool, b);
            return b;
        }

        private ToolStripDropDownButton TaskBtn(string text, string icon, params ToolStripItem[] items)
        {
            ToolStripDropDownButton b = new ToolStripDropDownButton(text, Icons.Get(icon, 24));
            b.ImageScaling = ToolStripItemImageScaling.None;
            b.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            b.TextImageRelation = TextImageRelation.ImageBeforeText;
            b.DropDownItems.AddRange(items);
            b.Padding = new Padding(2, 0, 2, 0);
            return b;
        }

        private void BuildToolbars()
        {
            tasksBar = NewBar("Aufgaben");
            tasksBar.ImageScalingSize = new Size(24, 24);
            tasksBar.Items.AddRange(new ToolStripItem[]
            {
                TaskBtn("Erstellen", "create",
                    Item("PDF aus Datei...", null, delegate { CreateFromFiles(); }, false),
                    Item("PDF aus Zwischenablage", null, delegate { CreateFromClipboard(); }, false),
                    Item("Leeres Dokument", "blankPage", delegate { CreateBlank(); }, false),
                    Sep(),
                    Item("Beispieldokument", "logo", delegate { CreateSample(); }, false)),
                TaskBtn("Zusammenführen", "combine",
                    Item("Dateien zu einem einzigen PDF zusammenführen...", "combine", delegate { CombineFiles(); }, false),
                    Item("Seiten aus Datei einfügen...", "insertPages", delegate { InsertPagesFromFile(); }, true)),
                TaskBtn("Sichern", "secure",
                    ToolItem("Zum Schwärzen markieren", "redact", ToolMode.Redaction, null),
                    Item("Schwärzungen anwenden...", null, delegate { ApplyRedactions(); }, true),
                    Sep(),
                    Item("Sicherheitseigenschaften anzeigen...", null, delegate { DocumentProperties(1); }, true)),
                TaskBtn("Unterschreiben", "sign",
                    Item("Unterschrift platzieren...", null, delegate { PlaceSignature(false); }, true),
                    Item("Neue Unterschrift erstellen...", null, delegate { PlaceSignature(true); }, true)),
                TaskBtn("Formulare", "forms",
                    Item("Vorhandene Felder markieren", "forms", delegate { ToggleFormHighlight(); }, true)),
                TaskBtn("Kommentieren", "comment",
                    ToolItem("Notiz hinzufügen", "note", ToolMode.Note, null),
                    ToolItem("Text hervorheben", "highlight", ToolMode.Highlight, null),
                    Sep(),
                    Item("Werkzeugleiste \"Kommentieren und markieren\" einblenden", "pencil", delegate { ToggleToolbar(commentBar); }, false),
                    Item("Kommentarliste einblenden", "comments", delegate { ToggleComments(); }, true))
            });

            fileBar = NewBar("Datei");
            fileBar.Items.AddRange(new ToolStripItem[]
            {
                Btn("Öffnen", "open", delegate { OpenDialog(); }, false),
                Btn("Speichern", "save", delegate { Save(); }, true),
                Btn("Drucken", "print", delegate { Print(); }, true),
            });

            navBar = NewBar("Seitennavigation");
            pageBox = new ToolStripTextBox();
            pageBox.AutoSize = false;
            pageBox.Size = new Size(46, 21);
            pageBox.TextBox.TextAlign = HorizontalAlignment.Center;
            pageBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    int p;
                    if (int.TryParse(pageBox.Text, out p)) view.GoToPage(p - 1);
                    UpdatePageInfo();
                    view.Focus();
                }
            };
            pageBox.Leave += delegate { UpdatePageInfo(); };
            docItems.Add(pageBox);
            pageCountLabel = new ToolStripLabel("/ 0");
            navBar.Items.AddRange(new ToolStripItem[]
            {
                Btn("Vorherige Seite", "prevPage", delegate { view.PreviousPage(); }, true),
                Btn("Nächste Seite", "nextPage", delegate { view.NextPage(); }, true),
                pageBox,
                pageCountLabel
            });

            zoomBar = NewBar("Auswählen und zoomen");
            zoomBox = new ToolStripComboBox();
            zoomBox.AutoSize = false;
            zoomBox.Size = new Size(76, 21);
            zoomBox.Items.AddRange(new object[] { "8,33 %", "12,5 %", "25 %", "33,33 %", "50 %", "66,67 %", "75 %", "100 %", "125 %", "150 %", "200 %", "300 %", "400 %", "800 %", "1600 %", "3200 %", "6400 %" });
            zoomBox.SelectedIndexChanged += delegate { if (zoomBox.Focused) ApplyZoomText(); };
            zoomBox.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyZoomText(); view.Focus(); } };
            docItems.Add(zoomBox);
            ToolStripDropDownButton zoomTools = new ToolStripDropDownButton(Icons.Get("zoomTool"));
            zoomTools.ToolTipText = "Zoom-Werkzeuge";
            zoomTools.DropDownItems.AddRange(new ToolStripItem[]
            {
                ToolItem("Auswahlrechteck-Zoom", "zoomTool", ToolMode.Zoom, "Z"),
                ToolItem("Schnappschuss-Werkzeug", "snapshot", ToolMode.Snapshot, null),
                ToolItem("Messwerkzeug", "measure", ToolMode.Measure, null)
            });
            docItems.Add(zoomTools);
            zoomBar.Items.AddRange(new ToolStripItem[]
            {
                ToolBtn("Auswahl-Werkzeug", "select", ToolMode.Select),
                ToolBtn("Hand-Werkzeug", "hand", ToolMode.Hand),
                zoomTools,
                new ToolStripSeparator(),
                Btn("Verkleinern", "zoomOut", delegate { view.ZoomOut(); }, true),
                Btn("Vergrößern", "zoomIn", delegate { view.ZoomIn(); }, true),
                zoomBox
            });

            displayBar = NewBar("Seitenanzeige");
            displayBar.Items.AddRange(new ToolStripItem[]
            {
                Btn("Bildlauf-Modus: Fensterbreite", "fitWidth", delegate { view.SetZoomMode(ZoomMode.FitWidth); }, true),
                Btn("Ganze Seite anzeigen", "fitPage", delegate { view.SetZoomMode(ZoomMode.FitPage); }, true),
                Btn("Ansicht im Uhrzeigersinn drehen", "rotateCw", delegate { view.SetRotation(view.ViewRotation + 1); }, true)
            });

            findBar = NewBar("Suchen");
            findBox = new ToolStripTextBox();
            findBox.AutoSize = false;
            findBox.Size = new Size(140, 21);
            findBox.ToolTipText = "Suchen (Eingabetaste: weitersuchen)";
            findBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindNext(!e.Shift); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; view.ClearSearchHighlight(); view.Focus(); }
            };
            docItems.Add(findBox);
            ToolStripDropDownButton findOptions = new ToolStripDropDownButton();
            findOptions.DisplayStyle = ToolStripItemDisplayStyle.None;
            findOptions.DropDownItems.AddRange(new ToolStripItem[]
            {
                Item("Weitersuchen", "nextPage", delegate { FindNext(true); }, true),
                Item("Vorheriges suchen", "prevPage", delegate { FindNext(false); }, true),
                Sep(),
                Item("Erweiterte Suche öffnen", "search", delegate { ShowSearchPane(true); }, true)
            });
            findBar.Items.AddRange(new ToolStripItem[]
            {
                findBox,
                findOptions,
                Btn("Vorheriges suchen", "prevPage", delegate { FindNext(false); }, true),
                Btn("Weitersuchen", "nextPage", delegate { FindNext(true); }, true)
            });

            commentBar = NewBar("Kommentieren und markieren");
            ToolStripSplitButton stamp = new ToolStripSplitButton(Icons.Get("stamp"));
            stamp.ToolTipText = "Stempel-Werkzeug";
            stamp.ButtonClick += delegate { SelectTool(ToolMode.Stamp); };
            stamp.DropDownItems.AddRange(StampItems());
            RegisterTool(ToolMode.Stamp, stamp);
            docItems.Add(stamp);
            commentBar.Items.AddRange(new ToolStripItem[]
            {
                ToolBtn("Notiz-Werkzeug", "note", ToolMode.Note),
                stamp,
                new ToolStripSeparator(),
                ToolBtn("Text hervorheben", "highlight", ToolMode.Highlight),
                ToolBtn("Text unterstreichen", "underline", ToolMode.Underline),
                ToolBtn("Text durchstreichen", "strikeout", ToolMode.StrikeOut),
                new ToolStripSeparator(),
                ToolBtn("Textfeld-Werkzeug", "textbox", ToolMode.FreeText),
                ToolBtn("Rechteck-Werkzeug", "rectangle", ToolMode.Rectangle),
                ToolBtn("Ellipse-Werkzeug", "ellipse", ToolMode.Ellipse),
                ToolBtn("Linien-Werkzeug", "line", ToolMode.Line),
                ToolBtn("Pfeil-Werkzeug", "arrow", ToolMode.Arrow),
                ToolBtn("Bleistift-Werkzeug", "pencil", ToolMode.Pencil),
                new ToolStripSeparator(),
                Btn("Kommentarliste einblenden", "comments", delegate { ToggleComments(); }, true)
            });
            commentBar.Visible = AppSettings.GetBool("Toolbar.Comment", false);

            toolbars.AddRow(tasksBar);
            toolbars.AddRow(fileBar, navBar, zoomBar, displayBar, findBar);
            toolbars.AddRow(commentBar);

            ToolStripMenuItem toolbarsMenu = null;
            foreach (ToolStripItem i in menu.Items)
            {
                ToolStripMenuItem mi = i as ToolStripMenuItem;
                if (mi == null || mi.Text != "&Anzeige") continue;
                foreach (ToolStripItem s in mi.DropDownItems)
                    if (s.Text == "Werkzeugleisten") toolbarsMenu = (ToolStripMenuItem)s;
            }
            foreach (ToolStrip t in new ToolStrip[] { tasksBar, fileBar, navBar, zoomBar, displayBar, findBar, commentBar })
            {
                ToolStrip tt = t;
                tt.Visible = tt == commentBar ? tt.Visible : AppSettings.GetBool("Toolbar." + tt.Text, true);
                ToolStripMenuItem mi = new ToolStripMenuItem(tt.Text);
                mi.Click += delegate { ToggleToolbar(tt); };
                toolbarItems[tt] = mi;
                if (toolbarsMenu != null) toolbarsMenu.DropDownItems.Add(mi);
            }
            if (toolbarsMenu != null)
            {
                toolbarsMenu.DropDownItems.Add(Sep());
                toolbarsMenu.DropDownItems.Add(Item("Werkzeugleisten zurücksetzen", null, delegate
                {
                    foreach (ToolStrip t in toolbarItems.Keys) t.Visible = t != commentBar;
                    toolbars.UpdateBreaks();
                }, false));
            }
            toolbars.UpdateBreaks();
        }

        private void ToggleToolbar(ToolStrip t)
        {
            t.Visible = !t.Visible;
            toolbars.UpdateBreaks();
            UpdateUI();
        }

        // ==================================================================
        // Dokument laden / schließen
        // ==================================================================

        public void OpenDialog()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Öffnen";
                d.Filter = "Adobe PDF-Dateien (*.pdf)|*.pdf|Alle Dateien (*.*)|*.*";
                d.Multiselect = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                foreach (string f in d.FileNames) OpenFile(f);
            }
        }

        /// <summary>Öffnet eine Datei – in diesem Fenster, falls es leer ist, sonst in einem neuen.</summary>
        public void OpenFile(string path)
        {
            MainForm existing = Program.FindWindow(path);
            if (existing != null) { existing.Activate(); return; }
            if (session != null) { Program.OpenInNewWindow(path); return; }
            if (!PdfCreator.IsPdf(path) && (PdfCreator.IsImage(path) || PdfCreator.IsText(path)))
            {
                CreateFromFileList(new string[] { path });
                return;
            }
            try
            {
                Cursor = Cursors.WaitCursor;
                DocumentSession s = DocumentSession.Open(path, delegate
                {
                    using (PasswordDialog pd = new PasswordDialog(Path.GetFileName(path)))
                        return pd.ShowDialog(this) == DialogResult.OK ? pd.Password : null;
                });
                if (s == null) return;
                AppSettings.AddRecentFile(path);
                LoadSession(s);
            }
            catch (PdfException ex)
            {
                MessageBox.Show(this, "Beim Öffnen des Dokuments ist ein Fehler aufgetreten.\n\n" + ex.Message, "AcroClone", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Die Datei \"" + Path.GetFileName(path) + "\" konnte nicht geöffnet werden.\n\n" + ex.Message, "AcroClone", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        /// <summary>Zeigt eine Sitzung an (in diesem oder einem neuen Fenster).</summary>
        public void ShowSession(DocumentSession s)
        {
            if (session != null) { Program.OpenSessionInNewWindow(s); return; }
            LoadSession(s);
        }

        private void LoadSession(DocumentSession s)
        {
            session = s;
            session.ModifiedChanged += OnSessionModified;
            session.AnnotationsChanged += OnSessionAnnotations;
            session.StructureChanged += OnSessionStructure;
            session.DocumentReplaced += OnSessionReplaced;

            welcome.Visible = false;
            view.Visible = true;
            view.BringToFront();
            ApplyDefaultView();
            view.Session = s;
            nav.Session = s;
            comments.Session = s;
            search.Session = s;
            ResetFind();
            view.KeepToolSelected = AppSettings.GetBool("KeepTool", false);
            view.SetTool(AppSettings.GetBool("DefaultToolSelect", false) ? ToolMode.Select : ToolMode.Hand);

            ApplyFormState();
            if (nav.HasBookmarks) nav.ShowPanel("bookmarks");
            else if (AppSettings.GetBool("NavOpen", false)) nav.ShowPanel("pages");
            if (s.Annotations.Count > 0 && AppSettings.GetBool("CommentsOpen", false)) ShowComments(true);
            UpdateTitle();
            UpdateUI();
            view.Focus();
        }

        private void ApplyDefaultView()
        {
            int layoutIdx = AppSettings.GetInt("DefaultLayout", 0);
            PageLayoutMode mode = PageLayoutMode.Continuous;
            if (layoutIdx == 1) mode = PageLayoutMode.SinglePage;
            else if (layoutIdx == 3) mode = PageLayoutMode.TwoUp;
            else if (layoutIdx == 4) mode = PageLayoutMode.TwoUpContinuous;
            view.SetPageLayout(mode, false);
            int z = AppSettings.GetInt("DefaultZoom", 0);
            double[] fixedZoom = { 0, 0, 0, 0.5, 0.75, 1.0, 1.25, 1.5, 2.0 };
            if (z == 2) view.SetZoomMode(ZoomMode.FitPage);
            else if (z >= 3) view.SetZoom(fixedZoom[z]);
            else view.SetZoomMode(ZoomMode.FitWidth);
        }

        private void ApplyFormState()
        {
            bool hasForm = session != null && session.Pdf.HasForm;
            messageBar.Visible = hasForm;
            if (hasForm) session.Pdf.SetFormHighlight(highlightForms);
            messageButton.Text = highlightForms ? "Feldmarkierung aufheben" : "Vorhandene Felder markieren";
            view.RefreshAll();
        }

        private void OnSessionModified(object sender, EventArgs e) { UpdateTitle(); UpdateUI(); }
        private void OnSessionAnnotations(object sender, EventArgs e) { UpdateUI(); }
        private void OnSessionStructure(object sender, EventArgs e) { ResetFind(); UpdatePageInfo(); UpdateUI(); }
        private void OnSessionReplaced(object sender, EventArgs e) { ApplyFormState(); }

        private bool ConfirmClose()
        {
            if (session == null || !session.Modified) return true;
            view.CommitInlineEditor();
            DialogResult r = MessageBox.Show(this,
                "Möchten Sie die Änderungen an \"" + session.DisplayName + "\" vor dem Schließen speichern?",
                "AcroClone", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) return Save();
            return true;
        }

        public bool CloseDocument()
        {
            if (session == null) return true;
            if (!ConfirmClose()) return false;
            if (fullScreen) ToggleFullScreen();
            DocumentSession s = session;
            session = null;
            s.ModifiedChanged -= OnSessionModified;
            s.AnnotationsChanged -= OnSessionAnnotations;
            s.StructureChanged -= OnSessionStructure;
            s.DocumentReplaced -= OnSessionReplaced;
            view.Session = null;
            nav.Session = null;
            comments.Session = null;
            search.Session = null;
            nav.Collapse();
            ShowComments(false);
            ShowSearchPane(false);
            messageBar.Visible = false;
            s.Dispose();
            welcome.Visible = true;
            welcome.BringToFront();
            welcome.Invalidate();
            UpdateTitle();
            UpdateUI();
            if (Program.WindowCount > 1) Close();
            return true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            if (!ConfirmClose()) { e.Cancel = true; return; }
            SaveWindowSettings();
            if (session != null)
            {
                view.Session = null;
                nav.Session = null;
                comments.Session = null;
                search.Session = null;
                session.Dispose();
                session = null;
            }
        }

        private void SaveWindowSettings()
        {
            if (fullScreen) return;
            AppSettings.SetBool("Window.Maximized", WindowState == FormWindowState.Maximized);
            if (WindowState == FormWindowState.Normal)
            {
                AppSettings.SetInt("Window.Width", Width);
                AppSettings.SetInt("Window.Height", Height);
            }
            AppSettings.SetInt("NavWidth", nav.PanelWidth);
            AppSettings.SetInt("CommentsHeight", comments.Height);
            AppSettings.SetBool("NavOpen", nav.IsExpanded);
            AppSettings.SetBool("Toolbar.Comment", commentBar.Visible);
            foreach (ToolStrip t in toolbarItems.Keys)
                if (t != commentBar) AppSettings.SetBool("Toolbar." + t.Text, t.Visible);
            AppSettings.Save();
        }

        // ==================================================================
        // Speichern / Drucken / Exportieren
        // ==================================================================

        public bool Save()
        {
            if (session == null) return false;
            if (session.IsUntitled) return SaveAs();
            return SaveTo(session.FilePath);
        }

        public bool SaveAs()
        {
            if (session == null) return false;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "Speichern unter";
                d.Filter = "Adobe PDF-Dateien (*.pdf)|*.pdf";
                d.FileName = session.DisplayName;
                if (session.FilePath != null) d.InitialDirectory = Path.GetDirectoryName(session.FilePath);
                if (d.ShowDialog(this) != DialogResult.OK) return false;
                return SaveTo(d.FileName);
            }
        }

        private bool SaveTo(string path)
        {
            view.CommitInlineEditor();
            try
            {
                Cursor = Cursors.WaitCursor;
                session.Save(path);
                AppSettings.AddRecentFile(path);
                statusText.Text = "Gespeichert: " + path;
                UpdateTitle();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Das Dokument konnte nicht gespeichert werden.\n\n" + ex.Message, "Speichern", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void Print()
        {
            if (session == null) return;
            view.CommitInlineEditor();
            new PrintManager(session, view.CurrentPage).Print(this);
        }

        private void PrintPreview()
        {
            if (session == null) return;
            new PrintManager(session, view.CurrentPage).Preview(this);
        }

        private void ExportImages(ImageFormat format)
        {
            if (session == null) return;
            bool png = format.Guid == ImageFormat.Png.Guid;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "Seiten als Bilder exportieren";
                d.Filter = png ? "PNG-Bilder (*.png)|*.png" : "JPEG-Bilder (*.jpg)|*.jpg";
                d.FileName = Path.GetFileNameWithoutExtension(session.DisplayName) + (png ? ".png" : ".jpg");
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string dir = Path.GetDirectoryName(d.FileName);
                string baseName = Path.GetFileNameWithoutExtension(d.FileName);
                string ext = png ? ".png" : ".jpg";
                Cursor = Cursors.WaitCursor;
                try
                {
                    const double dpi = 150;
                    for (int i = 0; i < session.PageCount; i++)
                    {
                        SizeF s = session.Pdf.GetViewSize(i, 0);
                        int w = (int)(s.Width / 72 * dpi), h = (int)(s.Height / 72 * dpi);
                        using (Bitmap b = session.Pdf.RenderPage(i, 0, w, h, true))
                        {
                            using (Graphics g = Graphics.FromImage(b))
                            {
                                Affine m = session.Pdf.GetPageTransform(i, 0).Scale(dpi / 72);
                                foreach (Annotation a in session.AnnotationsOnPage(i))
                                    if (a.Type != AnnotationType.Redaction) AnnotationPainter.Paint(g, a, m);
                            }
                            string name = session.PageCount == 1 ? baseName + ext : baseName + "_Seite_" + (i + 1).ToString("D3") + ext;
                            if (png) b.Save(Path.Combine(dir, name), ImageFormat.Png);
                            else File.WriteAllBytes(Path.Combine(dir, name), PdfObjects.ToJpeg(b, 90));
                        }
                        statusText.Text = "Exportiere Seite " + (i + 1) + " von " + session.PageCount + " …";
                        Application.DoEvents();
                    }
                    statusText.Text = session.PageCount + " Bild(er) exportiert nach " + dir;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Export fehlgeschlagen: " + ex.Message, "Exportieren", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
        }

        private void ExportText()
        {
            if (session == null) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "Text exportieren";
                d.Filter = "Textdateien (*.txt)|*.txt";
                d.FileName = Path.GetFileNameWithoutExtension(session.DisplayName) + ".txt";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < session.PageCount; i++)
                {
                    if (i > 0) sb.AppendLine().AppendLine("\f");
                    sb.AppendLine(session.Pdf.GetPageText(i));
                }
                File.WriteAllText(d.FileName, sb.ToString(), Encoding.UTF8);
                statusText.Text = "Text exportiert: " + d.FileName;
            }
        }

        // ==================================================================
        // Erstellen / Zusammenführen
        // ==================================================================

        private void CreateFromFiles()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "PDF aus Datei erstellen";
                d.Multiselect = true;
                d.Filter = "Alle unterstützten Formate|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.txt;*.pdf|Bilder|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|Textdateien (*.txt)|*.txt|PDF-Dateien (*.pdf)|*.pdf";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                CreateFromFileList(d.FileNames);
            }
        }

        private void CreateFromFileList(IList<string> files)
        {
            if (files.Count == 1 && PdfCreator.IsPdf(files[0])) { OpenFile(files[0]); return; }
            try
            {
                Cursor = Cursors.WaitCursor;
                PdfDocument doc = PdfDocument.CreateNew();
                foreach (string f in files)
                {
                    statusText.Text = "Konvertiere " + Path.GetFileName(f) + " …";
                    Application.DoEvents();
                    PdfCreator.AppendFile(doc, f);
                }
                string name = Path.GetFileNameWithoutExtension(files[0]) + ".pdf";
                ShowSession(DocumentSession.FromNewDocument(doc, name));
                statusText.Text = "PDF erstellt.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Die PDF-Datei konnte nicht erstellt werden.\n\n" + ex.Message, "PDF erstellen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void CreateFromClipboard()
        {
            try
            {
                PdfDocument doc = PdfDocument.CreateNew();
                if (Clipboard.ContainsImage())
                {
                    using (Image img = Clipboard.GetImage()) PdfCreator.AddImagePage(doc, img, 0);
                }
                else if (Clipboard.ContainsText())
                {
                    PdfCreator.AddTextPages(doc, Clipboard.GetText(), "Zwischenablage");
                }
                else
                {
                    doc.Dispose();
                    MessageBox.Show(this, "Die Zwischenablage enthält weder ein Bild noch Text.", "PDF aus Zwischenablage", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                ShowSession(DocumentSession.FromNewDocument(doc, "Zwischenablage.pdf"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "PDF aus Zwischenablage", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CreateBlank()
        {
            ShowSession(DocumentSession.FromNewDocument(PdfCreator.CreateBlank(), "Unbenannt" + Program.NextUntitled() + ".pdf"));
        }

        private void CreateSample()
        {
            try
            {
                ShowSession(DocumentSession.FromNewDocument(PdfCreator.CreateSample(), "Willkommen.pdf"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Beispieldokument", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CombineFiles()
        {
            using (CombineDialog d = new CombineDialog(null))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Cursor = Cursors.WaitCursor;
                    PdfDocument doc = PdfDocument.CreateNew();
                    foreach (string f in d.Files)
                    {
                        statusText.Text = "Füge " + Path.GetFileName(f) + " hinzu …";
                        Application.DoEvents();
                        PdfCreator.AppendFile(doc, f);
                    }
                    ShowSession(DocumentSession.FromNewDocument(doc, "Sammelmappe" + Program.NextUntitled() + ".pdf"));
                    statusText.Text = d.Files.Count + " Dateien zusammengeführt.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Die Dateien konnten nicht zusammengeführt werden.\n\n" + ex.Message, "Zusammenführen", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
        }

        private void OnWelcomeCommand(object sender, ItemEventArgs e)
        {
            switch (e.Key)
            {
                case "open": OpenDialog(); break;
                case "create": CreateFromFiles(); break;
                case "combine": CombineFiles(); break;
                case "blank": CreateBlank(); break;
                case "sample": CreateSample(); break;
                default:
                    if (e.Key.StartsWith("recent:"))
                    {
                        string path = e.Key.Substring(7);
                        if (File.Exists(path)) OpenFile(path);
                        else MessageBox.Show(this, "Die Datei wurde nicht gefunden:\n" + path, "AcroClone", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    break;
            }
        }

        // ==================================================================
        // Bearbeiten
        // ==================================================================

        private void Undo()
        {
            if (session == null || !session.CanUndo) return;
            view.CommitInlineEditor();
            session.Undo();
            UpdateUI();
        }

        private void Redo()
        {
            if (session == null || !session.CanRedo) return;
            session.Redo();
            UpdateUI();
        }

        private void Copy()
        {
            if (session == null) return;
            if (view.CopySelectedText()) statusText.Text = "Text in die Zwischenablage kopiert.";
        }

        private void Preferences()
        {
            using (PreferencesDialog d = new PreferencesDialog())
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                d.Apply();
                highlightForms = AppSettings.GetBool("HighlightForms", true);
                foreach (MainForm f in Program.Windows) f.OnPreferencesChanged();
            }
        }

        public void OnPreferencesChanged()
        {
            highlightForms = AppSettings.GetBool("HighlightForms", true);
            view.KeepToolSelected = AppSettings.GetBool("KeepTool", false);
            if (session != null) ApplyFormState();
            welcome.Invalidate();
        }

        // ==================================================================
        // Suchen
        // ==================================================================

        private void FocusFind()
        {
            if (!findBar.Visible) { findBar.Visible = true; toolbars.UpdateBreaks(); }
            if (view.HasTextSelection) findBox.Text = view.SelectedText.Trim();
            findBox.Focus();
            findBox.SelectAll();
        }

        private void ResetFind()
        {
            findCache.Clear();
            findPage = findIndex = -1;
            findQuery = null;
        }

        private List<TextMatch> FindMatches(int page)
        {
            List<TextMatch> list;
            if (!findCache.TryGetValue(page, out list))
            {
                list = session.Pdf.FindOnPage(page, findQuery, false, false);
                findCache[page] = list;
            }
            return list;
        }

        private void FindNext(bool forward)
        {
            if (session == null) return;
            string q = findBox.Text;
            if (string.IsNullOrEmpty(q)) return;
            if (q != findQuery) { ResetFind(); findQuery = q; }
            int n = session.PageCount;
            int dir = forward ? 1 : -1;
            if (findPage >= 0)
            {
                List<TextMatch> cur = FindMatches(findPage);
                int ni = findIndex + dir;
                if (ni >= 0 && ni < cur.Count) { ShowFind(findPage, ni); return; }
            }
            else
            {
                int startPage = view.CurrentPage;
                List<TextMatch> cur = FindMatches(startPage);
                if (cur.Count > 0) { ShowFind(startPage, forward ? 0 : cur.Count - 1); return; }
                findPage = startPage;
            }
            int origin = findPage;
            Cursor = Cursors.WaitCursor;
            try
            {
                for (int k = 1; k <= n; k++)
                {
                    int p = ((origin + dir * k) % n + n) % n;
                    List<TextMatch> list = FindMatches(p);
                    if (list.Count > 0)
                    {
                        bool wrapped = forward ? p <= origin : p >= origin;
                        ShowFind(p, forward ? 0 : list.Count - 1);
                        if (wrapped) statusText.Text = forward ? "Das Ende des Dokuments wurde erreicht. Die Suche wurde am Anfang fortgesetzt." : "Der Anfang des Dokuments wurde erreicht. Die Suche wurde am Ende fortgesetzt.";
                        return;
                    }
                }
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            view.ClearSearchHighlight();
            MessageBox.Show(this, "Das Dokument wurde vollständig durchsucht.\nEs wurden keine Treffer für \"" + q + "\" gefunden.", "AcroClone", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ShowFind(int page, int index)
        {
            findPage = page;
            findIndex = index;
            view.ShowSearchMatch(FindMatches(page)[index]);
            statusText.Text = "Treffer " + (index + 1) + " von " + FindMatches(page).Count + " auf Seite " + (page + 1);
        }

        private void ShowSearchPane(bool show)
        {
            if (show && session == null) return;
            search.Visible = show;
            searchSplitter.Visible = show;
            if (show) search.Activate(findBox.Text.Length > 0 ? findBox.Text : (view.HasTextSelection ? view.SelectedText.Trim() : null));
        }

        // ==================================================================
        // Ansicht
        // ==================================================================

        private void SelectTool(ToolMode t)
        {
            if (session == null) return;
            if (t == ToolMode.Signature && view.SignatureTemplate == null) { PlaceSignature(false); return; }
            view.SetTool(t);
            if (t == ToolMode.Stamp) statusText.Text = "Klicken Sie auf die Seite, um den Stempel \"" + view.StampText + "\" zu platzieren.";
            else if (t == ToolMode.Redaction) statusText.Text = "Ziehen Sie ein Rechteck um den zu schwärzenden Bereich. Anwenden über Erweitert › Schwärzung.";
            else if (t == ToolMode.Measure) statusText.Text = "Ziehen Sie eine Linie, um den Abstand zu messen.";
            else statusText.Text = string.Empty;
        }

        private void GoToPageDialog()
        {
            if (session == null) return;
            using (GoToPageDialog d = new GoToPageDialog(view.CurrentPage, session.PageCount))
                if (d.ShowDialog(this) == DialogResult.OK) view.GoToPage(d.PageIndex);
        }

        private void ApplyZoomText()
        {
            string t = zoomBox.Text.Replace("%", "").Trim().Replace('.', ',');
            double v;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.GetCultureInfo("de-DE"), out v) && v > 0)
                view.SetZoom(v / 100.0);
            UpdateZoomBox();
        }

        private void ToggleComments()
        {
            ShowComments(!comments.Visible);
        }

        private void ShowComments(bool show)
        {
            if (show && session == null) return;
            comments.Visible = show;
            commentsSplitter.Visible = show;
            nav.SetCommentsChecked(show);
            if (session != null) AppSettings.SetBool("CommentsOpen", show);
            UpdateUI();
        }

        private void ToggleFormHighlight()
        {
            highlightForms = !highlightForms;
            AppSettings.SetBool("HighlightForms", highlightForms);
            ApplyFormState();
            UpdateUI();
        }

        private void ToggleFullScreen()
        {
            if (session == null && !fullScreen) return;
            if (!fullScreen)
            {
                fullScreen = true;
                savedState = WindowState;
                savedBorder = FormBorderStyle;
                savedComments = comments.Visible;
                savedSearch = search.Visible;
                menu.Visible = toolbars.Visible = status.Visible = false;
                nav.Visible = navSplitter.Visible = false;
                comments.Visible = commentsSplitter.Visible = false;
                search.Visible = searchSplitter.Visible = false;
                messageBar.Visible = false;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                WindowState = FormWindowState.Maximized;
                view.SetFullScreen(true);
                view.Focus();
            }
            else
            {
                fullScreen = false;
                view.SetFullScreen(false);
                FormBorderStyle = savedBorder;
                WindowState = savedState;
                menu.Visible = status.Visible = true;
                toolbars.Visible = !readMode;
                nav.Visible = !readMode;
                navSplitter.Visible = nav.IsExpanded && !readMode;
                ShowComments(savedComments);
                ShowSearchPane(savedSearch);
                messageBar.Visible = session != null && session.Pdf.HasForm;
            }
        }

        private void ToggleReadMode()
        {
            if (session == null && !readMode) return;
            readMode = !readMode;
            toolbars.Visible = !readMode;
            nav.Visible = !readMode;
            navSplitter.Visible = !readMode && nav.IsExpanded;
            if (readMode) { ShowComments(false); ShowSearchPane(false); }
            statusText.Text = readMode ? "Lesemodus – Strg+H zum Beenden" : string.Empty;
        }

        private void OnViewContextMenu(object sender, ViewContextEventArgs e)
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Renderer = new Acrobat9Renderer();
            if (e.Annotation != null)
            {
                Annotation a = e.Annotation;
                if (a.Type == AnnotationType.FreeText)
                    m.Items.Add("Text bearbeiten", Icons.Get("textbox"), delegate { view.BeginInlineEdit(a, false); });
                m.Items.Add("Pop-up-Notiz öffnen", Icons.Get("note"), delegate { NotePopup p = view.OpenPopup(a); if (p != null) p.FocusText(); });
                m.Items.Add(a.Checked ? "Häkchen entfernen" : "Mit Häkchen versehen", Icons.Get("check"), delegate
                {
                    session.BeginChange(false, "Häkchen setzen");
                    a.Checked = !a.Checked;
                    session.CommitAnnotationChange();
                });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Löschen", Icons.Get("delete"), delegate { view.DeleteSelected(); });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Eigenschaften...", Icons.Get("properties"), delegate { AnnotationProperties(); });
            }
            else if (e.HasTextSelection)
            {
                m.Items.Add("Kopieren", Icons.Get("copy"), delegate { Copy(); });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Text hervorheben", Icons.Get("highlight"), delegate { view.MarkupSelection(AnnotationType.Highlight); });
                m.Items.Add("Text unterstreichen", Icons.Get("underline"), delegate { view.MarkupSelection(AnnotationType.Underline); });
                m.Items.Add("Text durchstreichen", Icons.Get("strikeout"), delegate { view.MarkupSelection(AnnotationType.StrikeOut); });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Suchen nach \"" + Shorten(view.SelectedText, 24) + "\"", Icons.Get("find"), delegate { findBox.Text = view.SelectedText.Trim(); FindNext(true); });
                m.Items.Add("Alles auswählen", null, delegate { view.SelectAllText(); });
            }
            else
            {
                m.Items.Add("Hand-Werkzeug", Icons.Get("hand"), delegate { SelectTool(ToolMode.Hand); });
                m.Items.Add("Auswahl-Werkzeug", Icons.Get("select"), delegate { SelectTool(ToolMode.Select); });
                m.Items.Add("Auswahlrechteck-Zoom", Icons.Get("zoomTool"), delegate { SelectTool(ToolMode.Zoom); });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Vorherige Seite", Icons.Get("prevPage"), delegate { view.PreviousPage(); });
                m.Items.Add("Nächste Seite", Icons.Get("nextPage"), delegate { view.NextPage(); });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Notiz hinzufügen", Icons.Get("note"), delegate { SelectTool(ToolMode.Note); });
                m.Items.Add("Drucken...", Icons.Get("print"), delegate { Print(); });
                m.Items.Add("Ansicht drehen", Icons.Get("rotateCw"), delegate { view.SetRotation(view.ViewRotation + 1); });
                m.Items.Add(new ToolStripSeparator());
                m.Items.Add("Dokumenteigenschaften...", Icons.Get("properties"), delegate { DocumentProperties(0); });
            }
            m.Show(view, e.Location);
        }

        private static string Shorten(string s, int max)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > max ? s.Substring(0, max) + "…" : s;
        }

        // ==================================================================
        // Dokument-Befehle
        // ==================================================================

        private IList<int> ThumbSelection { get { return nav.IsExpanded && nav.ActivePanel == "pages" ? nav.Thumbnails.SelectedPages : new List<int>(); } }

        private void DeletePages()
        {
            if (session == null) return;
            using (DeletePagesDialog d = new DeletePagesDialog(session.PageCount, view.CurrentPage, ThumbSelection))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                List<int> pages = d.Range.GetPages();
                if (pages.Count >= session.PageCount)
                {
                    MessageBox.Show(this, "Es können nicht alle Seiten gelöscht werden. Mindestens eine Seite muss im Dokument verbleiben.", "Seiten löschen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (MessageBox.Show(this, "Möchten Sie " + d.Range.Description + " aus dem Dokument löschen?", "AcroClone",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                session.DeletePages(pages);
            }
        }

        private void RotatePages()
        {
            if (session == null) return;
            using (RotatePagesDialog d = new RotatePagesDialog(session.PageCount, view.CurrentPage, ThumbSelection))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                List<int> pages = d.Range.GetPages();
                if (d.OrientationFilter > 0)
                    pages = pages.FindAll(p =>
                    {
                        SizeF s = session.Pdf.GetPageSize(p);
                        bool portrait = s.Height >= s.Width;
                        return d.OrientationFilter == 1 ? portrait : !portrait;
                    });
                session.RotatePages(pages, d.QuarterTurns);
            }
        }

        private void InsertPagesFromFile()
        {
            if (session == null) return;
            string file;
            using (OpenFileDialog o = new OpenFileDialog())
            {
                o.Title = "Datei zum Einfügen auswählen";
                o.Filter = "Alle unterstützten Formate|*.pdf;*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.txt|PDF-Dateien (*.pdf)|*.pdf|Bilder|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff";
                if (o.ShowDialog(this) != DialogResult.OK) return;
                file = o.FileName;
            }
            using (InsertPagesDialog d = new InsertPagesDialog(Path.GetFileName(file), session.PageCount, view.CurrentPage))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Cursor = Cursors.WaitCursor;
                    using (PdfDocument src = LoadInsertSource(file))
                    {
                        if (src == null) return;
                        int at = d.InsertIndex;
                        session.InsertDocument(src, at, "Seiten einfügen");
                        view.GoToPage(at);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Die Seiten konnten nicht eingefügt werden.\n\n" + ex.Message, "Seiten einfügen", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    Cursor = Cursors.Default;
                }
            }
        }

        private PdfDocument LoadInsertSource(string file)
        {
            if (PdfCreator.IsPdf(file))
            {
                byte[] data = File.ReadAllBytes(file);
                try { return PdfDocument.Load(data, null); }
                catch (PdfException ex)
                {
                    if (ex.ErrorCode != Native.FPDF_ERR_PASSWORD) throw;
                    using (PasswordDialog pd = new PasswordDialog(Path.GetFileName(file)))
                    {
                        if (pd.ShowDialog(this) != DialogResult.OK) return null;
                        return PdfDocument.Load(data, pd.Password);
                    }
                }
            }
            PdfDocument doc = PdfDocument.CreateNew();
            PdfCreator.AppendFile(doc, file);
            return doc;
        }

        private void InsertFromClipboard()
        {
            if (session == null) return;
            PdfDocument doc = PdfDocument.CreateNew();
            try
            {
                if (Clipboard.ContainsImage())
                    using (Image img = Clipboard.GetImage()) PdfCreator.AddImagePage(doc, img, 0);
                else if (Clipboard.ContainsText())
                    PdfCreator.AddTextPages(doc, Clipboard.GetText(), null);
                else
                {
                    MessageBox.Show(this, "Die Zwischenablage enthält weder ein Bild noch Text.", "Seiten einfügen", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                int at = view.CurrentPage + 1;
                session.InsertDocument(doc, at, "Aus Zwischenablage einfügen");
                view.GoToPage(at);
            }
            finally
            {
                doc.Dispose();
            }
        }

        private void InsertBlankPage()
        {
            if (session == null) return;
            int at = view.CurrentPage + 1;
            SizeF s = session.Pdf.GetPageSize(view.CurrentPage);
            session.InsertBlankPage(at, s.Width, s.Height);
            view.GoToPage(at);
        }

        private void ExtractPages()
        {
            if (session == null) return;
            using (ExtractPagesDialog d = new ExtractPagesDialog(session.PageCount, view.CurrentPage, ThumbSelection))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                List<int> pages = d.Range.GetPages();
                if (pages.Count == 0) return;
                try
                {
                    if (d.Separate)
                    {
                        using (FolderBrowserDialog f = new FolderBrowserDialog())
                        {
                            f.Description = "Zielordner für die extrahierten Seiten auswählen";
                            if (f.ShowDialog(this) != DialogResult.OK) return;
                            string baseName = Path.GetFileNameWithoutExtension(session.DisplayName);
                            foreach (int p in pages)
                                File.WriteAllBytes(Path.Combine(f.SelectedPath, baseName + " " + (p + 1) + ".pdf"), session.ExtractPages(new int[] { p }));
                            statusText.Text = pages.Count + " Seite(n) als einzelne Dateien extrahiert.";
                        }
                    }
                    else
                    {
                        byte[] bytes = session.ExtractPages(pages);
                        DocumentSession s = DocumentSession.FromBytes(bytes, null, null);
                        s.DisplayName = "Seiten aus " + Path.GetFileNameWithoutExtension(session.DisplayName) + ".pdf";
                        s.Modified = true;
                        Program.OpenSessionInNewWindow(s);
                    }
                    if (d.DeleteAfter && pages.Count < session.PageCount) session.DeletePages(pages);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Die Seiten konnten nicht extrahiert werden.\n\n" + ex.Message, "Seiten extrahieren", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void CropPages()
        {
            if (session == null) return;
            using (CropPagesDialog d = new CropPagesDialog(session.PageCount, view.CurrentPage, ThumbSelection))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                DocumentTools.CropPages(session, d.Range.GetPages(), d.MarginTop, d.MarginBottom, d.MarginLeft, d.MarginRight);
            }
        }

        private void SplitDocument()
        {
            if (session == null) return;
            string folder = session.FilePath != null ? Path.GetDirectoryName(session.FilePath) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            using (SplitDialog d = new SplitDialog(folder))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string baseName = Path.GetFileNameWithoutExtension(session.DisplayName);
                int part = 0;
                for (int start = 0; start < session.PageCount; start += d.PagesPerFile)
                {
                    List<int> pages = new List<int>();
                    for (int p = start; p < Math.Min(session.PageCount, start + d.PagesPerFile); p++) pages.Add(p);
                    part++;
                    File.WriteAllBytes(Path.Combine(d.Folder, baseName + "_Teil" + part + ".pdf"), session.ExtractPages(pages));
                }
                MessageBox.Show(this, "Das Dokument wurde in " + part + " Dateien aufgeteilt.", "Dokument teilen", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void AddWatermark()
        {
            if (session == null) return;
            int page = view.CurrentPage;
            SizeF size = session.Pdf.GetViewSize(page, 0);
            float f = 300f / Math.Max(size.Width, size.Height);
            using (Bitmap preview = session.Pdf.RenderPage(page, 0, (int)(size.Width * f), (int)(size.Height * f), false))
            using (WatermarkDialog d = new WatermarkDialog(preview, size, session.PageCount, page, ThumbSelection))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                d.Remember();
                DocumentTools.AddWatermark(session, d.Range.GetPages(), d.Options);
            }
        }

        private void AddHeaderFooter()
        {
            if (session == null) return;
            using (HeaderFooterDialog d = new HeaderFooterDialog(session.PageCount, view.CurrentPage, ThumbSelection))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                DocumentTools.AddHeaderFooter(session, d.Range.GetPages(), d.Options);
            }
        }

        private void DocumentProperties(int tab)
        {
            if (session == null) return;
            using (DocumentPropertiesDialog d = new DocumentPropertiesDialog(session, view.CurrentPage))
            {
                foreach (Control c in d.Controls)
                    if (c is TabControl) ((TabControl)c).SelectedIndex = tab;
                if (d.ShowDialog(this) == DialogResult.OK) d.Apply();
            }
        }

        private void PlaceSignature(bool forceNew)
        {
            if (session == null) return;
            if (forceNew || view.SignatureTemplate == null)
            {
                string saved = AppSettings.Get("Signature", null);
                if (!forceNew && !string.IsNullOrEmpty(saved))
                {
                    view.SignatureTemplate = Annotation.Deserialize(saved);
                }
                else
                {
                    using (SignatureDialog d = new SignatureDialog())
                    {
                        if (d.ShowDialog(this) != DialogResult.OK) return;
                        view.SignatureTemplate = d.CreateTemplate();
                        AppSettings.Set("Signature", view.SignatureTemplate.Serialize());
                        AppSettings.Save();
                    }
                }
            }
            view.SetTool(ToolMode.Signature);
            statusText.Text = "Klicken Sie auf die Stelle, an der die Unterschrift platziert werden soll.";
        }

        private void CustomStamp()
        {
            string text;
            using (InputDialog d = new InputDialog("Eigener Stempel", "Text des Stempels:", AppSettings.Get("CustomStamp", "GEPRÜFT")))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Value.Trim().Length == 0) return;
                text = d.Value.Trim();
                AppSettings.Set("CustomStamp", text);
            }
            view.StampText = text.ToUpperInvariant();
            view.StampColor = Color.FromArgb(30, 70, 180);
            SelectTool(ToolMode.Stamp);
        }

        private void ApplyRedactions()
        {
            if (session == null) return;
            int count = session.Annotations.Count(a => a.Type == AnnotationType.Redaction);
            if (count == 0)
            {
                MessageBox.Show(this, "Es sind keine Bereiche zum Schwärzen markiert.\n\nVerwenden Sie zuerst \"Zum Schwärzen markieren\".", "Schwärzung", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this,
                "Es werden " + count + " markierte Bereich(e) dauerhaft geschwärzt.\n\nDie betroffenen Seiten werden dabei in Bilder umgewandelt, der ursprüngliche Inhalt dieser Seiten wird vollständig entfernt. Text auf diesen Seiten ist danach nicht mehr durchsuchbar.\n\nMöchten Sie fortfahren?",
                "Schwärzungen anwenden", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            Cursor = Cursors.WaitCursor;
            try
            {
                int pages = DocumentTools.ApplyRedactions(session);
                statusText.Text = "Schwärzungen auf " + pages + " Seite(n) angewendet.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Schwärzung", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void AnnotationProperties()
        {
            Annotation a = view.SelectedAnnotation;
            if (a == null || session == null) return;
            using (AnnotationPropertiesDialog d = new AnnotationPropertiesDialog(a))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                session.BeginChange(false, "Kommentareigenschaften ändern");
                d.Apply();
                session.CommitAnnotationChange();
            }
        }

        private void FlattenComments()
        {
            if (session == null) return;
            if (MessageBox.Show(this,
                "Alle Kommentare und Formularfelder werden fest in die Seiten eingebettet und können danach nicht mehr bearbeitet werden.\n\nMöchten Sie fortfahren?",
                "Kommentare reduzieren", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            view.CloseAllPopups();
            session.FlattenAnnotations();
        }

        private void DeleteAllComments()
        {
            if (session == null || session.Annotations.Count == 0) return;
            if (MessageBox.Show(this, "Möchten Sie alle " + session.Annotations.Count + " Kommentare löschen?", "Kommentare löschen",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            view.CloseAllPopups();
            session.RemoveAnnotations(session.Annotations.ToList());
        }

        private void ShowHelp()
        {
            MessageBox.Show(this,
                "AcroClone 9 Pro – Kurzhilfe\n\n" +
                "Öffnen: Strg+O oder Datei per Drag & Drop in das Fenster ziehen\n" +
                "Speichern: Strg+S   Drucken: Strg+P   Eigenschaften: Strg+D\n\n" +
                "Werkzeuge: H = Hand, V = Auswahl, Z = Zoom, S = Notiz, U = Hervorheben\n" +
                "Zoom: Strg+Plus / Strg+Minus / Strg+Mausrad, Strg+0 ganze Seite, Strg+1 100 %, Strg+2 Fensterbreite\n" +
                "Navigation: Bild auf/ab, Pos1, Ende, Umschalt+Strg+N (Gehe zu Seite)\n" +
                "Suchen: Strg+F (Leiste), Umschalt+Strg+F (erweiterte Suche)\n" +
                "Vollbild: Strg+L (Esc beendet)   Lesemodus: Strg+H\n\n" +
                "Kommentare: Doppelklick öffnet die Pop-up-Notiz bzw. bearbeitet ein Textfeld,\n" +
                "Entf löscht den ausgewählten Kommentar, Rechtsklick zeigt weitere Befehle.\n" +
                "Seiten: In den Seitenminiaturen per Drag & Drop neu anordnen, Rechtsklick für Seitenbefehle.",
                "AcroClone-Hilfe", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ==================================================================
        // Menüs dynamisch
        // ==================================================================

        private void BuildRecentMenu()
        {
            foreach (ToolStripItem i in recentItems) fileMenu.DropDownItems.Remove(i);
            recentItems.Clear();
            List<string> files = AppSettings.RecentFiles;
            int idx = fileMenu.DropDownItems.IndexOf(recentSeparator);
            for (int k = 0; k < files.Count; k++)
            {
                string path = files[k];
                ToolStripMenuItem i = new ToolStripMenuItem("&" + (k + 1) + " " + Shorten(path, 60));
                i.ToolTipText = path;
                i.Click += delegate
                {
                    if (File.Exists(path)) OpenFile(path);
                    else MessageBox.Show(this, "Die Datei wurde nicht gefunden:\n" + path, "AcroClone", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };
                fileMenu.DropDownItems.Insert(idx + k, i);
                recentItems.Add(i);
            }
            if (files.Count > 0)
            {
                ToolStripSeparator sep = new ToolStripSeparator();
                fileMenu.DropDownItems.Insert(idx + files.Count, sep);
                recentItems.Add(sep);
            }
        }

        private void BuildWindowMenu()
        {
            windowMenu.DropDownItems.Clear();
            windowMenu.DropDownItems.Add(Item("Alle schließen", null, delegate { Program.CloseAllDocuments(); }, false));
            windowMenu.DropDownItems.Add(Sep());
            int k = 1;
            foreach (MainForm f in Program.Windows)
            {
                MainForm ff = f;
                ToolStripMenuItem i = new ToolStripMenuItem("&" + k + " " + (f.Session != null ? f.Session.DisplayName : "(Kein Dokument)"));
                i.Checked = f == this;
                i.Click += delegate { ff.Activate(); };
                windowMenu.DropDownItems.Add(i);
                k++;
            }
        }

        // ==================================================================
        // Status der Oberfläche
        // ==================================================================

        private void UpdateTitle()
        {
            if (session == null) Text = "AcroClone 9 Pro";
            else Text = session.DisplayName + (session.Modified ? "*" : "") + " - AcroClone 9 Pro";
        }

        private void UpdateUI()
        {
            bool hasDoc = session != null;
            foreach (ToolStripItem i in docItems) i.Enabled = hasDoc;
            if (hasDoc)
            {
                undoItem.Enabled = session.CanUndo;
                undoItem.Text = session.CanUndo ? "Rückgängig: " + session.UndoDescription : "Rückgängig";
                redoItem.Enabled = session.CanRedo;
                redoItem.Text = session.CanRedo ? "Wiederherstellen: " + session.RedoDescription : "Wiederherstellen";
                foreach (KeyValuePair<PageLayoutMode, ToolStripMenuItem> kv in layoutItems) kv.Value.Checked = view.PageLayout == kv.Key;
                coverItem.Checked = view.CoverPage;
                highlightFieldsItem.Checked = highlightForms;
                highlightFieldsItem.Enabled = session.Pdf.HasForm;
            }
            commentBarItem.Checked = commentBar.Visible;
            commentsListItem.Checked = comments.Visible;
            UpdatePageInfo();
            UpdateZoomBox();
            UpdateToolChecks();
        }

        private void UpdateToolChecks()
        {
            foreach (KeyValuePair<ToolMode, List<ToolStripItem>> kv in toolItems)
            {
                bool on = session != null && view.Tool == kv.Key;
                foreach (ToolStripItem i in kv.Value)
                {
                    ToolStripButton b = i as ToolStripButton;
                    if (b != null) b.Checked = on;
                    ToolStripMenuItem m = i as ToolStripMenuItem;
                    if (m != null) m.Checked = on;
                }
            }
        }

        private void UpdatePageInfo()
        {
            if (session == null)
            {
                pageBox.Text = string.Empty;
                pageCountLabel.Text = "/ 0";
                pageSizeLabel.Text = string.Empty;
                return;
            }
            int p = view.CurrentPage;
            if (!pageBox.Focused) pageBox.Text = (p + 1).ToString();
            pageCountLabel.Text = "/ " + session.PageCount;
            if (p >= 0 && p < session.PageCount)
            {
                SizeF s = session.Pdf.GetPageSize(p);
                pageSizeLabel.Text = (s.Width / 72 * 25.4).ToString("0.0") + " x " + (s.Height / 72 * 25.4).ToString("0.0") + " mm";
            }
        }

        private void UpdateZoomBox()
        {
            if (session == null) { zoomBox.Text = string.Empty; return; }
            if (zoomBox.Focused) return;
            double pct = view.Zoom * 100;
            zoomBox.Text = (Math.Abs(pct - Math.Round(pct)) < 0.05 ? Math.Round(pct).ToString("0") : pct.ToString("0.##", CultureInfo.GetCultureInfo("de-DE"))) + " %";
        }

        // ==================================================================
        // Tastatur und Drag & Drop
        // ==================================================================

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Strg+C / Strg+A im Dokument, ohne Textfelder zu stören
            if (session != null && view.ContainsFocus && !view.FormHasFocus && !(ActiveControl is TextBoxBase))
            {
                if (keyData == (Keys.Control | Keys.C)) { Copy(); return true; }
                if (keyData == (Keys.Control | Keys.A)) { view.SelectAllText(); return true; }
                if (keyData == (Keys.Control | Keys.Add)) { view.ZoomIn(); return true; }
                if (keyData == (Keys.Control | Keys.Subtract)) { view.ZoomOut(); return true; }
            }
            if (keyData == Keys.Escape && fullScreen) { ToggleFullScreen(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            List<string> others = new List<string>();
            foreach (string f in files)
            {
                if (PdfCreator.IsPdf(f)) OpenFile(f);
                else if (PdfCreator.IsImage(f) || PdfCreator.IsText(f)) others.Add(f);
            }
            if (others.Count > 0) CreateFromFileList(others);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            welcome.Visible = session == null;
            if (session == null) welcome.BringToFront();
        }
    }
}
