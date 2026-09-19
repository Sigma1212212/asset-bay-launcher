using System.Drawing.Drawing2D;

namespace AssetBayLauncher.UI;

/// <summary>
/// Draws what the designed theme will look like in game: the same shapes the menu builds (panel with its
/// thickness and shadow, lit keys, and the layout the chosen menu type uses), close enough to judge
/// colours, corners and depth without putting the headset on.
/// </summary>
public sealed partial class LauncherForm
{
    private static readonly string[] PreviewRows = { "Videos", "Fly", "Gun", "Props", "Effects", "Platforms" };
    private static readonly string[] PreviewValues = { "3", "on", "Place", "1/3", "0/4", "" };

    private static int Alpha(float v) => (int)Math.Clamp(v, 0, 255);

    private void DrawDesignerPreview(Graphics g)
    {
        if (page != Page.Designer || previewArea.Width < 50) return;
        var d = design;
        if (designerTab == 1) { DrawKeyStudio(g, d); return; }

        // Panel size in menu units, then scaled to fit the space we have.
        bool book = d.Style == 6;
        float panelW = book ? 560f : 300f;
        int rowCount = PreviewRows.Length;
        int lines = d.Layout == 1 || book ? (rowCount + 1) / 2 : rowCount;
        float rowH = Math.Max(24f, d.RowHeight * 0.75f);
        float panelH = 96f + lines * (rowH + 8f) + 56f;
        float scale = Math.Min(previewArea.Width / (panelW + 60f), previewArea.Height / (panelH + 60f));
        float w = panelW * scale, h = panelH * scale;
        var r = new RectangleF(previewArea.X + (previewArea.Width - w) / 2f, previewArea.Y + (previewArea.Height - h) / 2f, w, h);

        Color Top = d.PanelTop.ToColor(), Bottom = d.PanelBottom.ToColor(), EdgeA = d.EdgeTop.ToColor(), EdgeB = d.EdgeBottom.ToColor();
        Color Accent = d.Accent.ToColor(), Text = d.Text.ToColor(), Sub = d.SubText.ToColor();
        float radius = d.PanelRadius * scale, depth = d.Depth * scale;

        // Behind the panel: floor shadow, then the panel's own thickness.
        if (depth > 0.5f)
        {
            using (var shadow = Rounded(new RectangleF(r.X + 3, r.Y + depth + 4, r.Width - 6, r.Height), radius))
            using (var sb = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                g.FillPath(sb, shadow);
            using var side = Rounded(new RectangleF(r.X, r.Y + depth, r.Width, r.Height), radius);
            using var sideBrush = new SolidBrush(Lerp(Lerp(Bottom, EdgeB, 0.35f), Color.Black, 0.4f));
            g.FillPath(sideBrush, side);
        }

        using var panel = Rounded(r, radius);
        using (var fill = new LinearGradientBrush(r, Top, Bottom, 90f)) g.FillPath(fill, panel);
        DrawSurface(g, panel, r, d);
        if (d.EdgeWidth > 0.05f)
            using (var pen = new Pen(EdgeA, Math.Max(1f, d.EdgeWidth * scale))) g.DrawPath(pen, panel);

        float pad = 14f * scale;
        // Header: brand, title, and a hint of the corner buttons.
        Ink.DrawText(g, "ASSET BAY", valueFont, Rectangle.Round(new RectangleF(r.X + pad, r.Y + 6 * scale, r.Width, 18)), Sub);
        using (var title = new Font(fontFamily ?? "Segoe UI", Math.Max(10f, 16f * scale), (d.TitleStyle & 1) != 0 ? FontStyle.Bold : FontStyle.Regular))
            Ink.DrawText(g, (d.TitleStyle & 8) != 0 ? "LIBRARY" : "Library", title,
                Rectangle.Round(new RectangleF(r.X + pad, r.Y + 26 * scale, r.Width - pad * 2, 32 * scale)), Text);
        using (var line = new SolidBrush(Accent))
            g.FillRectangle(line, r.X + pad, r.Y + 58 * scale, r.Width - pad * 2, Math.Max(1f, 2.5f * scale));

        // Rows.
        float top = r.Y + 70 * scale, rowW = r.Width - pad * 2;
        int columns = d.Layout == 1 || book ? 2 : 1;
        float cellW = (rowW - (columns - 1) * 6f * scale) / columns;
        for (int i = 0; i < rowCount; i++)
        {
            int col = columns == 1 ? 0 : i % columns, line = columns == 1 ? i : i / columns;
            var cell = new RectangleF(r.X + pad + col * (cellW + 6f * scale), top + line * (rowH * scale + 6f * scale), cellW, rowH * scale);
            DrawPreviewRow(g, cell, d, i, scale);
        }

        // Footer: whatever that menu type uses.
        float footY = top + ((rowCount + columns - 1) / columns) * (rowH * scale + 6f * scale) + 4f * scale;
        DrawPreviewFooter(g, new RectangleF(r.X + pad, footY, rowW, 26f * scale), d, scale);

        // Caption under the preview.
        string caption = $"{ThemeFile.Styles[Math.Clamp(d.Style, 0, ThemeFile.Styles.Length - 1)]} · {design.DisplayName}" + (designDirty ? " · unsaved" : "");
        Ink.DrawText(g, caption, valueFont, Rectangle.Round(new RectangleF(previewArea.X, previewArea.Bottom - 20, previewArea.Width, 18)),
            P(t => t.SubText), TextFormatFlags.HorizontalCenter);
    }

    /// <summary>The surface pattern, faked with simple lines / speckles.</summary>
    private static void DrawSurface(Graphics g, GraphicsPath panel, RectangleF r, ThemeFile d)
    {
        if (d.Pattern == 0) return;
        var clip = g.Clip;
        g.SetClip(panel, CombineMode.Intersect);
        using var pen = new Pen(d.PatternColor.ToColor());
        switch (d.Pattern)
        {
            case 1: // wood: long grain
                for (float y = r.Y + 4; y < r.Bottom; y += 7)
                    g.DrawBezier(pen, new PointF(r.X, y), new PointF(r.X + r.Width * 0.3f, y + 3), new PointF(r.X + r.Width * 0.7f, y - 3), new PointF(r.Right, y));
                break;
            case 2: // scanlines
                for (float y = r.Y; y < r.Bottom; y += 4) g.DrawLine(pen, r.X, y, r.Right, y);
                break;
            case 3: // grid
                for (float x = r.X; x < r.Right; x += 16) g.DrawLine(pen, x, r.Y, x, r.Bottom);
                for (float y = r.Y; y < r.Bottom; y += 16) g.DrawLine(pen, r.X, y, r.Right, y);
                break;
            default: // paper speckle
                var rand = new Random(7);
                using (var dot = new SolidBrush(d.PatternColor.ToColor()))
                    for (int i = 0; i < 300; i++)
                        g.FillRectangle(dot, r.X + (float)rand.NextDouble() * r.Width, r.Y + (float)rand.NextDouble() * r.Height, 1.5f, 1.5f);
                break;
        }
        g.Clip = clip;
    }

    private void DrawPreviewRow(Graphics g, RectangleF cell, ThemeFile d, int index, float scale)
    {
        Color fill = index == 3 ? d.ButtonFillHover.ToColor() : d.ButtonFill.ToColor();
        Color text = d.Text.ToColor(), sub = d.SubText.ToColor(), accent = d.Accent.ToColor();
        float radius = d.ButtonRadius * scale, depth = d.ButtonDepth * scale;
        bool borderless = d.Style is 3 or 4;   // Checklist, Switchboard

        var body = cell;
        if (!borderless && depth > 0.3f)
        {
            using var basePath = Rounded(new RectangleF(cell.X, cell.Y + depth, cell.Width, cell.Height), radius);
            using var baseBrush = new SolidBrush(Lerp(d.ButtonFillPressed.ToColor(), Color.Black, 0.45f));
            g.FillPath(baseBrush, basePath);
        }
        using var path = Rounded(body, radius);
        if (!borderless)
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            if (d.Bevel > 0.01f)
            {
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                var lit = new RectangleF(body.X, body.Y, body.Width, body.Height * 0.55f);
                using (var sheen = new LinearGradientBrush(lit, Color.FromArgb(Alpha(34 * d.Bevel * d.BevelLight), 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(sheen, lit);
                var shade = new RectangleF(body.X, body.Y + body.Height * 0.5f, body.Width, body.Height * 0.5f);
                using (var sh = new LinearGradientBrush(shade, Color.FromArgb(0, 0, 0, 0), Color.FromArgb(Alpha(46 * d.Bevel * d.BevelShadow), 0, 0, 0), 90f))
                    g.FillRectangle(sh, shade);
                g.Clip = clip;
            }
            if (d.ButtonEdgeWidth > 0.05f && index == 3)
                using (var pen = new Pen(d.ButtonEdgeHover.ToColor(), d.ButtonEdgeWidth * scale)) g.DrawPath(pen, path);
        }

        float textLeft = body.X + 10 * scale;
        switch (d.Style)
        {
            case 3: // Checklist: tick box on the left
            {
                float box = body.Height * 0.55f;
                var rect = new RectangleF(body.X + 4 * scale, body.Y + (body.Height - box) / 2f, box, box);
                using (var pen = new Pen(index % 2 == 1 ? accent : sub, Math.Max(1f, 2f * scale))) g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                if (index % 2 == 1) using (var b = new SolidBrush(accent)) g.FillRectangle(b, rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6);
                textLeft = rect.Right + 8 * scale;
                break;
            }
            case 4: // Switchboard: switch square on the right
            {
                float box = body.Height * 0.7f;
                var rect = new RectangleF(body.Right - box - 4 * scale, body.Y + (body.Height - box) / 2f, box, box);
                using var b = new SolidBrush(index % 2 == 1 ? d.StatusOk.ToColor() : d.StatusError.ToColor());
                using var sw = Rounded(rect, radius * 0.6f);
                g.FillPath(b, sw);
                break;
            }
            case 5: // Cards: chevron
                Ink.DrawText(g, ">", valueFont, Rectangle.Round(new RectangleF(body.Right - 18 * scale, body.Y, 14 * scale, body.Height)), sub,
                    TextFormatFlags.VerticalCenter);
                break;
        }

        using var font = new Font(fontFamily ?? "Segoe UI", Math.Max(7f, d.LabelSize * 0.55f * scale),
            (d.LabelStyle & 1) != 0 ? FontStyle.Bold : FontStyle.Regular);
        string label = (d.LabelStyle & 8) != 0 ? PreviewRows[index].ToUpperInvariant() : PreviewRows[index];
        var textRect = Rectangle.Round(new RectangleF(textLeft, body.Y, body.Width - (textLeft - body.X) - 24 * scale, body.Height));
        Ink.DrawText(g, label, font, textRect, text, TextFormatFlags.VerticalCenter |
            (d.Style == 7 ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left));
        if (PreviewValues[index].Length > 0 && d.Style != 4)
            Ink.DrawText(g, PreviewValues[index], valueFont, textRect, sub, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }

    private void DrawPreviewFooter(Graphics g, RectangleF r, ThemeFile d, float scale)
    {
        Color accent = d.Accent.ToColor(), sub = d.SubText.ToColor(), fill = d.ButtonFill.ToColor();
        float radius = d.ButtonRadius * scale;
        void Key(RectangleF rect, string text)
        {
            using (var path = Rounded(rect, radius))
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            Ink.DrawText(g, text, smallFont, Rectangle.Round(rect), d.Text.ToColor(),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        switch (d.Style)
        {
            case 1: // Pillars: tall bars either side (drawn as wide keys here)
                Key(new RectangleF(r.X, r.Y, r.Width * 0.22f, r.Height), "<");
                Key(new RectangleF(r.Right - r.Width * 0.22f, r.Y, r.Width * 0.22f, r.Height), ">");
                break;
            case 2: // Console: one segmented bar
                Key(new RectangleF(r.X, r.Y, r.Width * 0.24f, r.Height), "<");
                Key(new RectangleF(r.X + r.Width * 0.26f, r.Y, r.Width * 0.48f, r.Height), Cased("Back"));
                Key(new RectangleF(r.Right - r.Width * 0.24f, r.Y, r.Width * 0.24f, r.Height), ">");
                break;
            case 5: // Cards: pill dock
                Key(new RectangleF(r.X + r.Width * 0.2f, r.Y, r.Width * 0.28f, r.Height), "< PREV");
                Key(new RectangleF(r.X + r.Width * 0.52f, r.Y, r.Width * 0.28f, r.Height), "NEXT >");
                break;
            default:
                Key(new RectangleF(r.X, r.Y, r.Width * 0.28f, r.Height), "<");
                Ink.DrawText(g, "1 / 2", valueFont, Rectangle.Round(r), sub, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                Key(new RectangleF(r.Right - r.Width * 0.28f, r.Y, r.Width * 0.28f, r.Height), ">");
                break;
        }
    }

    // ------------------------------------------------------------------ designer widgets

    private void DrawSwatch(Graphics g, Widget w, RectangleF r, float a)
    {
        using var path = Rounded(r, Radius(t => t.ButtonRadius));
        using (var b = new SolidBrush(Fade(Lerp(P(t => t.Button), P(t => t.ButtonHover), w.Hover), a))) g.FillPath(b, path);
        Ink.DrawText(g, Cased(w.Label), valueFont, Rectangle.Round(new RectangleF(r.X + 10, r.Y, r.Width - 60, r.Height)),
            Fade(P(t => t.Text), a), TextFormatFlags.VerticalCenter);

        var chip = new RectangleF(r.Right - 44, r.Y + 5, 36, r.Height - 10);
        using (var checker = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillRectangle(checker, chip); // shows transparency
        using (var b = new SolidBrush(w.Colour!())) g.FillRectangle(b, chip);
        using (var pen = new Pen(Fade(P(t => t.SubText), a))) g.DrawRectangle(pen, chip.X, chip.Y, chip.Width, chip.Height);
    }

    private void DrawSlider(Graphics g, Widget w, RectangleF r, float a)
    {
        Ink.DrawText(g, Cased(w.Label), valueFont, Rectangle.Round(new RectangleF(r.X, r.Y, r.Width - 34, r.Height * 0.55f)),
            Fade(P(t => t.SubText), a), TextFormatFlags.VerticalCenter);
        Ink.DrawText(g, w.ValueText.Current, valueFont, Rectangle.Round(new RectangleF(r.X, r.Y, r.Width, r.Height * 0.55f)),
            Fade(P(t => t.Text), a), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        var track = new RectangleF(r.X, r.Bottom - 9, r.Width, 4);
        using (var b = new SolidBrush(Fade(P(t => t.Button), a))) g.FillRectangle(b, track);
        float f = Math.Clamp((w.SliderValue!() - w.Min) / Math.Max(0.0001f, w.Max - w.Min), 0f, 1f);
        using (var b = new SolidBrush(Fade(P(t => t.Accent), a))) g.FillRectangle(b, track.X, track.Y, track.Width * f, track.Height);
        using (var b = new SolidBrush(Fade(Lerp(P(t => t.Accent), Color.White, w.Hover * 0.5f), a)))
            g.FillEllipse(b, track.X + track.Width * f - 5, track.Y - 4, 11, 11);
    }

    /// <summary>Dragging anywhere on a slider sets its value.</summary>
    private void DragSlider(Widget w, Point mouse)
    {
        float f = Math.Clamp((mouse.X - w.Rect.X) / Math.Max(1f, w.Rect.Width), 0f, 1f);
        float value = w.Min + (w.Max - w.Min) * f;
        if (w.Max - w.Min > 20f) value = MathF.Round(value);            // whole numbers for the big ranges
        else if (w.Max - w.Min > 2f) value = MathF.Round(value * 2f) / 2f;
        else value = MathF.Round(value * 100f) / 100f;
        w.SetSlider?.Invoke(value);
    }
}

public sealed partial class LauncherForm
{
    /// <summary>
    /// The buttons tab preview: one key close up in all three states, plus a tilted view so the depth,
    /// the light on top and the shade underneath are easy to judge while the sliders move.
    /// </summary>
    private void DrawKeyStudio(Graphics g, ThemeFile d)
    {
        var area = previewArea;
        float w = Math.Min(area.Width - 24f, 260f);
        float h = Math.Clamp(d.RowHeight * 0.7f, 30f, 46f);
        float x = area.X + (area.Width - w) / 2f;
        Ink.DrawText(g, $"key · {d.ButtonDepth:0.#} deep · bevel {d.Bevel:0.00}", valueFont,
            Rectangle.Round(new RectangleF(area.X, area.Y, area.Width, 16)), P(t => t.SubText), TextFormatFlags.HorizontalCenter);
        float y = area.Y + 22f;

        (string label, int state)[] states = { ("Normal", 0), ("Hovered", 1), ("Pressed", 2) };
        foreach (var (label, state) in states)
        {
            Ink.DrawText(g, label, valueFont, Rectangle.Round(new RectangleF(x, y, w, 16)), P(t => t.SubText));
            DrawKeySample(g, new RectangleF(x, y + 18f, w, h), d, state, false);
            y += h + d.ButtonDepth + 28f;
        }

        Ink.DrawText(g, "From the side", valueFont, Rectangle.Round(new RectangleF(x, y, w, 16)), P(t => t.SubText));
        DrawKeySample(g, new RectangleF(x, y + 18f, w, h), d, 0, true);

    }

    private void DrawKeySample(Graphics g, RectangleF r, ThemeFile d, int state, bool tilted)
    {
        float radius = d.ButtonRadius, depth = d.ButtonDepth;
        Color face = state switch { 1 => d.ButtonFillHover.ToColor(), 2 => d.ButtonFillPressed.ToColor(), _ => d.ButtonFill.ToColor() };
        Color outline = state == 1 ? d.ButtonEdgeHover.ToColor() : d.ButtonEdge.ToColor();
        var body = state == 2 ? r with { Y = r.Y + depth * 0.85f } : r; // pressed keys sit down on their base

        if (depth > 0.3f)
        {
            using var basePath = Rounded(r with { Y = r.Y + depth }, radius);
            using var baseBrush = new SolidBrush(Lerp(d.ButtonFillPressed.ToColor(), Color.Black, 0.45f));
            g.FillPath(baseBrush, basePath);
            if (tilted)
            {
                // A wedge joining the base to the face: the "side" of the key.
                using var side = new GraphicsPath();
                side.AddPolygon(new[]
                {
                    new PointF(r.X + radius * 0.5f, r.Bottom), new PointF(r.Right - radius * 0.5f, r.Bottom),
                    new PointF(r.Right - radius * 0.5f, r.Bottom + depth), new PointF(r.X + radius * 0.5f, r.Bottom + depth),
                });
                using var sideBrush = new SolidBrush(Lerp(face, Color.Black, 0.55f));
                g.FillPath(sideBrush, side);
            }
        }

        using var path = Rounded(body, radius);
        using (var b = new SolidBrush(face)) g.FillPath(b, path);
        if (d.Bevel > 0.01f)
        {
            var clip = g.Clip;
            g.SetClip(path, System.Drawing.Drawing2D.CombineMode.Intersect);
            var lit = new RectangleF(body.X, body.Y, body.Width, body.Height * 0.55f);
            using (var sheen = new LinearGradientBrush(lit, Color.FromArgb(Alpha(46 * d.Bevel * d.BevelLight), 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                g.FillRectangle(sheen, lit);
            var shade = new RectangleF(body.X, body.Y + body.Height * 0.45f, body.Width, body.Height * 0.55f);
            using (var sh = new LinearGradientBrush(shade, Color.FromArgb(0, 0, 0, 0), Color.FromArgb(Alpha(60 * d.Bevel * d.BevelShadow), 0, 0, 0), 90f))
                g.FillRectangle(sh, shade);
            g.Clip = clip;
            using var hl = new Pen(Color.FromArgb(Alpha(90 * d.Bevel * d.BevelLight), 255, 255, 255));
            g.DrawLine(hl, body.X + radius, body.Y + 1.5f, body.Right - radius, body.Y + 1.5f);
        }
        if (d.ButtonEdgeWidth > 0.05f && outline.A > 0)
            using (var pen = new Pen(outline, d.ButtonEdgeWidth)) g.DrawPath(pen, path);

        using (var light = new SolidBrush(d.StatusOk.ToColor()))
            g.FillEllipse(light, body.X + 14, body.Y + body.Height / 2f - 4, 8, 8);
        using var font = new Font(fontFamily ?? "Segoe UI", Math.Max(8f, d.LabelSize * 0.5f),
            (d.LabelStyle & 1) != 0 ? FontStyle.Bold : FontStyle.Regular);
        string label = (d.LabelStyle & 8) != 0 ? "FLY" : "Fly";
        Ink.DrawText(g, label, font, Rectangle.Round(new RectangleF(body.X + 30, body.Y, body.Width - 60, body.Height)),
            d.Text.ToColor(), TextFormatFlags.VerticalCenter);
        using var valueFont2 = new Font(fontFamily ?? "Segoe UI", Math.Max(7f, d.ValueSize * 0.5f));
        Ink.DrawText(g, "on", valueFont2, Rectangle.Round(new RectangleF(body.X, body.Y, body.Width - 14, body.Height)),
            d.SubText.ToColor(), TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
    }
}
