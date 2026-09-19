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
