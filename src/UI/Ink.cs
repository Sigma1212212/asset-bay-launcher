using System.Drawing.Text;

namespace AssetBayLauncher.UI;

/// <summary>
/// Text drawing that respects transparency. WinForms' TextRenderer (GDI) ignores the alpha channel, so every
/// fade drew text at full strength and cross-fades stacked old and new text on top of each other. This uses
/// GDI+ instead, with the same call shapes as TextRenderer so call sites read the same.
/// </summary>
internal static class Ink
{
    private static readonly Dictionary<TextFormatFlags, StringFormat> Formats = new();

    public static void DrawText(Graphics g, string text, Font? font, Point at, Color color)
    {
        if (font == null || string.IsNullOrEmpty(text) || color.A == 0) return;
        Prepare(g);
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, at.X, at.Y + 1, Format(TextFormatFlags.Left | TextFormatFlags.SingleLine));
    }

    public static void DrawText(Graphics g, string text, Font? font, Rectangle rect, Color color, TextFormatFlags flags = TextFormatFlags.Left)
    {
        if (font == null || string.IsNullOrEmpty(text) || color.A == 0 || rect.Width <= 0 || rect.Height <= 0) return;
        Prepare(g);
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, rect, Format(flags));
    }

    public static Size MeasureText(string text, Font? font)
    {
        if (font == null || string.IsNullOrEmpty(text)) return Size.Empty;
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        Prepare(g);
        var s = g.MeasureString(text, font, int.MaxValue, Format(TextFormatFlags.Left | TextFormatFlags.SingleLine));
        return new Size((int)Math.Ceiling(s.Width), (int)Math.Ceiling(s.Height));
    }

    private static void Prepare(Graphics g)
    {
        // Grayscale anti-aliasing: blends correctly at any alpha (ClearType can't).
        if (g.TextRenderingHint != TextRenderingHint.AntiAlias) g.TextRenderingHint = TextRenderingHint.AntiAlias;
    }

    private static StringFormat Format(TextFormatFlags f)
    {
        if (Formats.TryGetValue(f, out var cached)) return cached;
        var sf = (StringFormat)StringFormat.GenericTypographic.Clone();
        sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        sf.Alignment = f.HasFlag(TextFormatFlags.HorizontalCenter) ? StringAlignment.Center
                     : f.HasFlag(TextFormatFlags.Right) ? StringAlignment.Far : StringAlignment.Near;
        sf.LineAlignment = f.HasFlag(TextFormatFlags.VerticalCenter) ? StringAlignment.Center
                         : f.HasFlag(TextFormatFlags.Bottom) ? StringAlignment.Far : StringAlignment.Near;
        if (!f.HasFlag(TextFormatFlags.WordBreak)) sf.FormatFlags |= StringFormatFlags.NoWrap;
        sf.Trimming = f.HasFlag(TextFormatFlags.EndEllipsis) ? StringTrimming.EllipsisCharacter : StringTrimming.None;
        if (!f.HasFlag(TextFormatFlags.EndEllipsis) && !f.HasFlag(TextFormatFlags.WordBreak)) sf.FormatFlags |= StringFormatFlags.NoClip;
        Formats[f] = sf;
        return sf;
    }
}
