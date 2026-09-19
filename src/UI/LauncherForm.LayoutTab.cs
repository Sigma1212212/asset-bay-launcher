using System.Drawing.Drawing2D;

namespace AssetBayLauncher.UI;

/// <summary>
/// Designer &gt; Layout: drag the pieces of the menu where you want them. Pick a piece in the list (or
/// click it on the canvas), drag it to move, drag its bottom-right corner to resize, and the sliders
/// nudge it exactly. Saving turns the theme's menu type into Custom, and the menu draws it as arranged.
/// </summary>
public sealed partial class LauncherForm
{
    private const float Snap = 4f;
    private string picked = "Rows";
    private RectangleF canvas;          // where the layout is drawn on screen
    private float canvasScale = 1f;     // screen pixels per menu unit
    private string? dragging;
    private bool resizing;
    private PointF dragFrom;
    private RectangleF dragStart;

    private CustomLayout Layout
    {
        get
        {
            design.Custom ??= new CustomLayout();
            return design.Custom;
        }
    }

    private Slot Picked => Slot(picked);

    private Slot Slot(string name)
    {
        foreach (var (n, slot) in Layout.Pieces()) if (n == name) return slot;
        return Layout.rows;
    }

    private void BuildLayoutTab(float lx, float mx, float colW, float rowH, float gap, float tabsH, int p)
    {
        int from = widgets.Count, order = 0;   // entrance order within this tab
        float top = ContentY + tabsH;

        // The pieces, as a list you can pick from. The tick shows whether it's drawn at all.
        var names = new[] { "Panel", "Rows", "Title", "Brand", "Page", "Status", "Back", "Settings", "Close", "Prev", "Next" };
        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i];
            widgets.Add(new Widget
            {
                Rect = new RectangleF(lx, top + i * (rowH + gap), colW, rowH), Page = p, Order = order++,
                Label = name, Toggle = true, Selected = () => Slot(name).Used,
                Value = () => picked == name ? "editing" : "",
                Click = () =>
                {
                    var slot = Slot(name);
                    if (picked == name) { if (slot.Used) { slot.w = slot.h = 0f; } else Restore(name); } // second click hides / shows
                    picked = name;
                    UseCustom();
                },
            });
        }

        // Numbers for the piece you're on, plus the panel itself.
        void Slider(int row, string label, Func<float> get, Action<float> set, float min, float max) => widgets.Add(new Widget
        {
            Rect = new RectangleF(mx, top + row * (rowH + gap), colW, rowH), Page = p, Order = order++,
            Label = label, Slider = true, Min = min, Max = max, SliderValue = get,
            SetSlider = v => { set(v); UseCustom(); }, Value = () => get().ToString("0"),
        });
        Slider(0, "X", () => Picked.x, v => Picked.x = v, 0, 800);
        Slider(1, "Y", () => Picked.y, v => Picked.y = v, 0, 900);
        Slider(2, "Width", () => Picked.w, v => Picked.w = v, 0, 800);
        Slider(3, "Height", () => Picked.h, v => Picked.h = v, 0, 900);
        widgets.Add(new Widget
        {
            Rect = new RectangleF(mx, top + 4 * (rowH + gap), colW, rowH), Page = p, Order = order++,
            Label = "Text align", Value = () => new[] { "Left", "Centre", "Right" }[Math.Clamp(Picked.align, 0, 2)],
            Click = () => { Picked.align = (Picked.align + 1) % 3; UseCustom(); },
        });
        Slider(5, "Panel width", () => Layout.width, v => Layout.width = v, 200, 900);
        Slider(6, "Panel height", () => Layout.height, v => Layout.height = v, 200, 900);
        Slider(7, "Row columns", () => Layout.columns, v => Layout.columns = (int)Math.Clamp(v, 1, 3), 1, 3);
        widgets.Add(new Widget
        {
            Rect = new RectangleF(mx, top + 8 * (rowH + gap), colW, rowH), Page = p, Order = order++,
            Label = "Row look", Value = () => ThemeFile.RowLooks[Math.Clamp(Layout.rowLook, 0, 5)],
            Click = () => { Layout.rowLook = (Layout.rowLook + 1) % ThemeFile.RowLooks.Length; UseCustom(); },
        });
        Slider(9, "Row height", () => Layout.rowHeight, v => Layout.rowHeight = v, 0, 96);
        Slider(10, "Row spacing", () => Layout.rowSpacing < 0 ? 0 : Layout.rowSpacing, v => Layout.rowSpacing = v, 0, 24);

        widgets.Add(new Widget
        {
            Rect = new RectangleF(mx, top + 11 * (rowH + gap), colW, rowH), Page = p, Order = order++, Small = true,
            Label = "Start from a menu type", Click = StartFromStyle,
        });

        for (int i = from; i < widgets.Count; i++) { var w = widgets[i]; w.Shown = () => designerTab == 2; }
    }

    /// <summary>Bring a hidden piece back at a sensible size.</summary>
    private void Restore(string name)
    {
        var fresh = new CustomLayout();
        foreach (var (n, slot) in fresh.Pieces())
            if (n == name)
            {
                var mine = Slot(name);
                mine.w = slot.w > 1 ? slot.w : 80f;
                mine.h = slot.h > 1 ? slot.h : 40f;
                return;
            }
    }

    /// <summary>Any layout edit means this theme is a Custom one.</summary>
    private void UseCustom()
    {
        design.Style = ThemeFile.CustomStyle;
        Touch();
    }

    /// <summary>Lay the pieces out like one of the built-in menu types, as a starting point.</summary>
    private void StartFromStyle()
    {
        var l = new CustomLayout();
        // A tidy default: title top left, rows filling the middle, controls along the bottom.
        l.width = 440f; l.height = 560f;
        l.body = new Slot(0, 0, 440f, 560f);
        l.brand = new Slot(20f, 16f, 280f, 18f);
        l.title = new Slot(20f, 36f, 280f, 44f);
        l.close = new Slot(388f, 20f, 36f, 36f);
        l.settings = new Slot(346f, 20f, 36f, 36f);
        l.back = new Slot(304f, 20f, 36f, 36f);
        l.rows = new Slot(20f, 96f, 400f, 380f);
        l.prev = new Slot(20f, 488f, 100f, 44f);
        l.next = new Slot(320f, 488f, 100f, 44f);
        l.page = new Slot(140f, 488f, 160f, 44f, 1);
        l.status = new Slot(20f, 536f, 400f, 20f, 1);
        design.Custom = l;
        picked = "Rows";
        UseCustom();
        SetStatus("Layout reset to a simple arrangement - drag the pieces from here.", StatusKind.Idle);
    }

    // ================================================================== the canvas

    private void DrawLayoutCanvas(Graphics g)
    {
        var l = Layout;
        var area = previewArea;
        canvasScale = Math.Min((area.Width - 24f) / l.width, (area.Height - 48f) / l.height);
        float w = l.width * canvasScale, h = l.height * canvasScale;
        canvas = new RectangleF(area.X + (area.Width - w) / 2f, area.Y + 24f, w, h);

        // Board behind the layout, with a faint grid to drag against.
        using (var back = new SolidBrush(Lerp(P(t => t.PanelBottom), Color.Black, 0.35f))) g.FillRectangle(back, canvas);
        using (var grid = new Pen(Fade(P(t => t.ButtonHover), 0.5f)))
            for (float x = 0; x <= l.width; x += 20f)
            {
                g.DrawLine(grid, canvas.X + x * canvasScale, canvas.Y, canvas.X + x * canvasScale, canvas.Bottom);
                if (x <= l.height) g.DrawLine(grid, canvas.X, canvas.Y + x * canvasScale, canvas.Right, canvas.Y + x * canvasScale);
            }

        foreach (var (name, slot) in l.Pieces())
        {
            if (!slot.Used) continue;
            var r = ToScreen(slot.Rect);
            bool on = name == picked;
            Color colour = name switch
            {
                "Panel" => P(t => t.SubText),
                "Rows" => P(t => t.Accent),
                "Title" or "Brand" or "Page" or "Status" => P(t => t.Text),
                _ => P(t => t.Busy),
            };
            using (var fill = new SolidBrush(Fade(colour, name == "Panel" ? 0.06f : 0.18f))) g.FillRectangle(fill, r);
            using (var pen = new Pen(on ? P(t => t.Accent) : Fade(colour, 0.8f), on ? 2f : 1f)) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            Ink.DrawText(g, name, valueFont, Rectangle.Round(r), Fade(P(t => t.Text), on ? 1f : 0.75f),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            if (on) // resize grip
                using (var grip = new SolidBrush(P(t => t.Accent)))
                    g.FillRectangle(grip, r.Right - 8, r.Bottom - 8, 8, 8);
        }

        Ink.DrawText(g, "drag to move · corner to resize · list toggles a piece",
            valueFont, Rectangle.Round(new RectangleF(area.X, canvas.Bottom + 4, area.Width, 18)),
            P(t => t.SubText), TextFormatFlags.HorizontalCenter);
    }

    private RectangleF ToScreen(RectangleF r) =>
        new(canvas.X + r.X * canvasScale, canvas.Y + r.Y * canvasScale, r.Width * canvasScale, r.Height * canvasScale);

    private static float Snapped(float v) => MathF.Round(v / Snap) * Snap;

    /// <summary>True when the click was on the canvas (so the form leaves it to us).</summary>
    private bool LayoutMouseDown(Point mouse)
    {
        if (page != Page.Designer || designerTab != 2 || !canvas.Contains(mouse)) return false;

        // Topmost piece under the pointer wins; the panel is only picked when nothing else is there.
        var pieces = Layout.Pieces();
        for (int i = pieces.Length - 1; i >= 0; i--)
        {
            var (name, slot) = pieces[i];
            if (!slot.Used) continue;
            var r = ToScreen(slot.Rect);
            bool grip = name == picked && new RectangleF(r.Right - 10, r.Bottom - 10, 12, 12).Contains(mouse);
            if (!grip && !r.Contains(mouse)) continue;
            picked = name;
            dragging = name;
            resizing = grip;
            dragFrom = mouse;
            dragStart = slot.Rect;
            Invalidate();
            return true;
        }
        return true; // clicked empty board: swallow it so nothing else reacts
    }

    private void LayoutMouseMove(Point mouse)
    {
        if (dragging == null) return;
        var slot = Slot(dragging);
        float dx = (mouse.X - dragFrom.X) / canvasScale, dy = (mouse.Y - dragFrom.Y) / canvasScale;
        if (resizing)
        {
            slot.w = Math.Max(8f, Snapped(dragStart.Width + dx));
            slot.h = Math.Max(8f, Snapped(dragStart.Height + dy));
        }
        else
        {
            slot.x = Math.Max(-200f, Snapped(dragStart.X + dx));
            slot.y = Math.Max(-200f, Snapped(dragStart.Y + dy));
        }
        UseCustom();
    }

    private void LayoutMouseUp() => dragging = null;
}
