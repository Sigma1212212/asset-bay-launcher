using AssetBayLauncher.UI;

namespace AssetBayLauncher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // Dev aid: AssetBayLauncher.exe --screenshot <folder>  paints every page to PNGs and exits.
        // Dev aid: write a sample designed theme, to check the menu reads what the designer writes.
        if (args.Length >= 1 && args[0] == "--theme-sample")
        {
            var sample = ThemeFile.FromPalette(UI.LauncherTheme.Velvet);
            sample.DisplayName = args.Length > 1 ? args[1] : "Designer sample";
            sample.Style = 5;         // Cards
            sample.Pattern = 2;       // Scanlines
            sample.Depth = 18f; sample.ButtonDepth = 7f; sample.PanelRadius = 26f;
            if (args.Length > 2 && args[2] == "layout")
            {
                // Arrows up top, rows below, status under them: clearly not one of the built-in types.
                sample.DisplayName += " layout";
                sample.Style = ThemeFile.CustomStyle;
                sample.Custom = new CustomLayout
                {
                    width = 460f, height = 520f,
                    body = new Slot(0, 0, 460f, 520f),
                    title = new Slot(120f, 24f, 220f, 40f, 1),
                    brand = new Slot(120f, 64f, 220f, 18f, 1),
                    prev = new Slot(16f, 20f, 92f, 56f),
                    next = new Slot(352f, 20f, 92f, 56f),
                    rows = new Slot(28f, 96f, 404f, 340f),
                    close = new Slot(28f, 448f, 120f, 48f),
                    settings = new Slot(160f, 448f, 60f, 48f),
                    back = new Slot(228f, 448f, 60f, 48f),
                    page = new Slot(300f, 448f, 132f, 48f, 1),
                    status = new Slot(28f, 500f, 404f, 16f, 1),
                    columns = 1, rowLook = 3,
                };
            }
            sample.Save();
            Console.WriteLine(ThemeFile.PathFor(sample.DisplayName));
            return;
        }
        if (args.Length == 2 && args[0] == "--screenshot")
        {
            using var form = new LauncherForm(LauncherConfig.Load());
            form.RenderPages(args[1]);
            return;
        }
        Application.Run(new LauncherForm(LauncherConfig.Load()));
    }
}
