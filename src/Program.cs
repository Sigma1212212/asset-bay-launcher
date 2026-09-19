using AssetBayLauncher.UI;

namespace AssetBayLauncher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // Dev aid: AssetBayLauncher.exe --screenshot <folder>  paints every page to PNGs and exits.
        if (args.Length == 2 && args[0] == "--screenshot")
        {
            using var form = new LauncherForm(LauncherConfig.Load());
            form.RenderPages(args[1]);
            return;
        }
        Application.Run(new LauncherForm(LauncherConfig.Load()));
    }
}
