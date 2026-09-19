using System.Diagnostics;

namespace AssetBayLauncher.UI;

/// <summary>
/// The Designer page: build your own menu look, see it straight away, and save it where the menu reads
/// designed themes (%APPDATA%\AssetBay\themes). In game: Settings &gt; Reload my themes, then pick it in
/// the Theme row like any built-in one.
/// </summary>
public sealed partial class LauncherForm
{
    private ThemeFile design = new();
    /// <summary>Which half of the designer is on screen: 0 the panel, 1 the buttons.</summary>
    private int designerTab;
    private string? designPath;
    private bool designDirty;
    private RectangleF previewArea;

    private void BuildDesignerPage()
    {
        var p = (int)Page.Designer;
        const float rowH = 26f, gap = 4f, tabsH = 32f; // the Panel / Buttons tabs sit above the rows
        float colW = 210f;
        float lx = ContentX, mx = ContentX + colW + 10;
        previewArea = new RectangleF(mx + colW + 16, ContentY, ContentW - (colW + 10) - (colW + 16), H - 24 - ContentY);
        int order = 0;
        float tabW = (colW * 2 + 10) / 3f;
        for (int i = 0; i < 3; i++)
        {
            int index = i;
            widgets.Add(new Widget
            {
                Rect = new RectangleF(lx + i * tabW, ContentY, tabW - 4, 28), Page = p, Order = -1, Small = true,
                Label = i == 0 ? "Panel" : i == 1 ? "Buttons" : "Layout", Selected = () => designerTab == index,
                Click = () => { designerTab = index; Invalidate(); },
            });
        }

        void Swatch(float x, int row, string label, Func<Col> get, Action<Col> set) => widgets.Add(new Widget
        {
            Rect = new RectangleF(x, ContentY + tabsH + row * (rowH + gap), colW, rowH), Page = p, Order = order++,
            Label = label, Colour = () => get().ToColor(), Small = true,
            Click = () => { if (PickColour(get().ToColor(), out var picked)) { set(new Col(picked)); Touch(); } },
        });
        void Slider(float x, int row, string label, Func<float> get, Action<float> set, float min, float max, string format = "0.#") =>
            widgets.Add(new Widget
            {
                Rect = new RectangleF(x, ContentY + tabsH + row * (rowH + gap), colW, rowH), Page = p, Order = order++,
                Label = label, Slider = true, Min = min, Max = max, SliderValue = get,
                SetSlider = v => { set(v); Touch(); }, Value = () => get().ToString(format),
            });
        void Choice(float x, int row, string label, string[] names, Func<int> get, Action<int> set) => widgets.Add(new Widget
        {
            Rect = new RectangleF(x, ContentY + tabsH + row * (rowH + gap), colW, rowH), Page = p, Order = order++,
            Label = label, Value = () => names[Math.Clamp(get() + (names == ThemeFile.Entrances ? 1 : 0), 0, names.Length - 1)],
            Click = () =>
            {
                int shift = names == ThemeFile.Entrances ? 1 : 0;
                set(((get() + shift + 1) % names.Length) - shift);
                Touch();
            },
        });

        // ---- panel tab: colours
        int panelFrom = widgets.Count;
        Swatch(lx, 0, "Panel top", () => design.PanelTop, c => design.PanelTop = c);
        Swatch(lx, 1, "Panel bottom", () => design.PanelBottom, c => design.PanelBottom = c);
        Swatch(lx, 2, "Edge top", () => design.EdgeTop, c => design.EdgeTop = c);
        Swatch(lx, 3, "Edge bottom", () => design.EdgeBottom, c => design.EdgeBottom = c);
        Swatch(lx, 4, "Accent", () => design.Accent, c => design.Accent = c);
        Swatch(lx, 5, "Accent 2", () => design.Accent2, c => design.Accent2 = c);
        Swatch(lx, 6, "Button", () => design.ButtonFill, c => design.ButtonFill = c);
        Swatch(lx, 7, "Button hover", () => design.ButtonFillHover, c => design.ButtonFillHover = c);
        Swatch(lx, 8, "Button pressed", () => design.ButtonFillPressed, c => design.ButtonFillPressed = c);
        Swatch(lx, 9, "Button edge", () => design.ButtonEdgeHover, c => design.ButtonEdgeHover = c);
        Swatch(lx, 10, "Text", () => design.Text, c => design.Text = c);
        Swatch(lx, 11, "Sub text", () => design.SubText, c => design.SubText = c);

        // ---- shape and behaviour
        Choice(mx, 0, "Menu type", ThemeFile.Styles, () => design.Style, v => design.Style = v);
        Choice(mx, 1, "Rows", ThemeFile.Layouts, () => design.Layout, v => design.Layout = v);
        Choice(mx, 2, "Hover", ThemeFile.Hovers, () => design.Hover, v => design.Hover = v);
        Choice(mx, 3, "Surface", ThemeFile.Patterns, () => design.Pattern, v => design.Pattern = v);
        Choice(mx, 4, "Entrance", ThemeFile.Entrances, () => design.EntranceOverride, v => design.EntranceOverride = v);
        Slider(mx, 5, "Panel corners", () => design.PanelRadius, v => design.PanelRadius = v, 0, 48);
        Slider(mx, 6, "Button corners", () => design.ButtonRadius, v => design.ButtonRadius = v, 0, 32);
        Slider(mx, 7, "Edge width", () => design.EdgeWidth, v => design.EdgeWidth = v, 0, 6);
        Slider(mx, 8, "Panel depth", () => design.Depth, v => design.Depth = v, 0, 32);
        Slider(mx, 9, "Button depth", () => design.ButtonDepth, v => design.ButtonDepth = v, 0, 10);
        Slider(mx, 10, "Bevel", () => design.Bevel, v => design.Bevel = v, 0, 1, "0.00");
        Slider(mx, 11, "Row height", () => design.RowHeight, v => design.RowHeight = v, 32, 96, "0");
        for (int i = panelFrom; i < widgets.Count; i++) { var w = widgets[i]; w.Shown = () => designerTab == 0; }

        // ---- buttons tab: the keys themselves
        int buttonsFrom = widgets.Count;
        Swatch(lx, 0, "Key face", () => design.ButtonFill, c => design.ButtonFill = c);
        Swatch(lx, 1, "Key hovered", () => design.ButtonFillHover, c => design.ButtonFillHover = c);
        Swatch(lx, 2, "Key pressed", () => design.ButtonFillPressed, c => design.ButtonFillPressed = c);
        Swatch(lx, 3, "Key outline", () => design.ButtonEdge, c => design.ButtonEdge = c);
        Swatch(lx, 4, "Outline hovered", () => design.ButtonEdgeHover, c => design.ButtonEdgeHover = c);
        Swatch(lx, 5, "Label", () => design.Text, c => design.Text = c);
        Swatch(lx, 6, "Value", () => design.SubText, c => design.SubText = c);
        Swatch(lx, 7, "Light on", () => design.StatusOk, c => design.StatusOk = c);
        Swatch(lx, 8, "Light off", () => design.StatusIdle, c => design.StatusIdle = c);
        Swatch(lx, 9, "Light busy", () => design.StatusBusy, c => design.StatusBusy = c);
        Swatch(lx, 10, "Light error", () => design.StatusError, c => design.StatusError = c);

        Slider(mx, 0, "Corners", () => design.ButtonRadius, v => design.ButtonRadius = v, 0, 32);
        Slider(mx, 1, "Depth (3D)", () => design.ButtonDepth, v => design.ButtonDepth = v, 0, 12);
        Slider(mx, 2, "Bevel", () => design.Bevel, v => design.Bevel = v, 0, 1, "0.00");
        Slider(mx, 3, "Top light", () => design.BevelLight, v => design.BevelLight = v, 0, 2, "0.00");
        Slider(mx, 4, "Bottom shade", () => design.BevelShadow, v => design.BevelShadow = v, 0, 2, "0.00");
        Slider(mx, 5, "Outline width", () => design.ButtonEdgeWidth, v => design.ButtonEdgeWidth = v, 0, 4, "0.0");
        Slider(mx, 6, "Row height", () => design.RowHeight, v => design.RowHeight = v, 32, 96, "0");
        Slider(mx, 7, "Row spacing", () => design.RowSpacing, v => design.RowSpacing = v, 0, 24, "0");
        Slider(mx, 8, "Label size", () => design.LabelSize, v => design.LabelSize = v, 10, 32, "0");
        Slider(mx, 9, "Value size", () => design.ValueSize, v => design.ValueSize = v, 8, 24, "0");
        Choice(mx, 10, "Label style", new[] { "Normal", "Bold", "Caps", "Bold caps" },
            () => (design.LabelStyle & 8) != 0 ? ((design.LabelStyle & 1) != 0 ? 3 : 2) : ((design.LabelStyle & 1) != 0 ? 1 : 0),
            v => design.LabelStyle = v switch { 1 => 1, 2 => 8, 3 => 9, _ => 0 });
        Choice(mx, 11, "Title style", new[] { "Normal", "Bold", "Caps", "Bold caps" },
            () => (design.TitleStyle & 8) != 0 ? ((design.TitleStyle & 1) != 0 ? 3 : 2) : ((design.TitleStyle & 1) != 0 ? 1 : 0),
            v => design.TitleStyle = v switch { 1 => 1, 2 => 8, 3 => 9, _ => 0 });
        for (int i = buttonsFrom; i < widgets.Count; i++) { var w = widgets[i]; w.Shown = () => designerTab == 1; }

        BuildLayoutTab(lx, mx, colW, rowH, gap, tabsH, p);

        // ---- file row
        float y = ContentY + tabsH + 12 * (rowH + gap) + 6;
        float bw = (colW * 2 + 10 - 12) / 4f;
        void Button(int i, string label, Action click) => widgets.Add(new Widget
        {
            Rect = new RectangleF(lx + i * (bw + 4), y, bw, 30), Page = p, Order = order++, Label = label, Small = true, Click = click,
        });
        Button(0, "New", NewDesign);
        Button(1, "Open", OpenDesign);
        Button(2, "Save", SaveDesign);
        Button(3, "Folder", () => { Directory.CreateDirectory(ThemeFile.Folder); Process.Start(new ProcessStartInfo(ThemeFile.Folder) { UseShellExecute = true }); });

        widgets.Add(new Widget
        {
            Rect = new RectangleF(lx, y + 34, colW * 2 + 10, 30), Page = p, Order = order++, Small = true,
            Label = "Rename", Value = () => design.DisplayName,
            Click = () => { if (AskText("Theme name", design.DisplayName, out var name)) { design.DisplayName = name; Touch(); } },
        });
    }

    private void Touch()
    {
        designDirty = true;
        Invalidate();
    }

    private void NewDesign()
    {
        design = ThemeFile.FromPalette(theme);
        designPath = null;
        designDirty = true;
        SetStatus("New theme started from the " + theme.Name + " palette.", StatusKind.Idle);
        Invalidate();
    }

    private void OpenDesign()
    {
        Directory.CreateDirectory(ThemeFile.Folder);
        using var dialog = new OpenFileDialog { InitialDirectory = ThemeFile.Folder, Filter = "Theme (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            design = ThemeFile.Load(dialog.FileName);
            designPath = dialog.FileName;
            designDirty = false;
            SetStatus($"Opened {Path.GetFileName(dialog.FileName)}.", StatusKind.Ok);
        }
        catch (Exception e) { SetStatus("Couldn't open that theme: " + e.Message, StatusKind.Error); }
        Invalidate();
    }

    private void SaveDesign()
    {
        try
        {
            // Renamed? The old file would otherwise linger under its previous name.
            string target = ThemeFile.PathFor(design.DisplayName);
            if (designPath != null && !string.Equals(designPath, target, StringComparison.OrdinalIgnoreCase) && File.Exists(designPath))
                File.Delete(designPath);
            design.Save();
            designPath = target;
            designDirty = false;
            SetStatus($"Saved. In game: Settings > Reload my themes, then pick \"{design.DisplayName}\".", StatusKind.Ok);
        }
        catch (Exception e) { SetStatus("Couldn't save: " + e.Message, StatusKind.Error); }
    }

    private bool PickColour(Color current, out Color picked)
    {
        using var dialog = new ColorDialog { Color = current, FullOpen = true, AnyColor = true, SolidColorOnly = false };
        picked = current;
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        picked = Color.FromArgb(current.A, dialog.Color); // keep the transparency the theme had
        return true;
    }

    /// <summary>A small text prompt (WinForms has no built-in one).</summary>
    private bool AskText(string title, string value, out string result)
    {
        using var form = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(320, 96), MinimizeBox = false, MaximizeBox = false,
        };
        var box = new TextBox { Text = value, Left = 12, Top = 16, Width = 296 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 152, Top = 52, Width = 74 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 234, Top = 52, Width = 74 };
        form.Controls.AddRange(new Control[] { box, ok, cancel });
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        bool okay = form.ShowDialog(this) == DialogResult.OK;
        result = box.Text.Trim();
        return okay && result.Length > 0;
    }
}
