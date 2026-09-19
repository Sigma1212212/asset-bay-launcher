using AssetBayLauncher.UI;

namespace AssetBayLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherForm(LauncherConfig.Load()));
    }
}
