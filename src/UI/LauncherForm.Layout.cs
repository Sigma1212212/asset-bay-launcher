using System.Diagnostics;

namespace AssetBayLauncher.UI;

/// <summary>
/// Layout: sidebar navigation on the left, a header with the page title, a toolbar of actions that is
/// always there, page content in the middle, and a console of everything that happened at the bottom.
///
///   ┌────────┬───────────────────────────────────────────────┐
///   │ logo   │ Page title                     [theme] [–][×] │
///   │ Home   │ [Inject latest] [Test local] [Eject] [...]    │
///   │ Menu   │ ┌ card ┐ ┌ card ┐ ┌ card ┐                    │
///   │ Online │ └──────┘ └──────┘ └──────┘                    │
///   │ Logs   │ ┌ console ─────────────────────────── clear ┐ │
///   │ ●game  │ │ [12:01:02] Injected v1.9.0                │ │
///   └────────┴───────────────────────────────────────────────┘
/// </summary>
public sealed partial class LauncherForm
{
    private const int SideW = 208, ContentX = SideW + 24, ContentW = W - SideW - 48;
    private const int ToolbarY = 90, ContentY = 148, ConsoleTop = H - 178;

    private enum Page { Home, Menu, Online, Logs, Settings }
    private static readonly string[] PageTitles = { "Home", "Menu", "Online", "Logs", "Settings" };
    private static readonly string[] PageSubtitles =
    {
        "Inject the menu into Gorilla Tag and see what's going on.",
        "Which version of the menu you'll get, and your local test build.",
        "Your Asset Bay server: health and free-tier usage.",
        "Everything the launcher did this session.",
        "Launcher preferences.",
    };
    private Page page = Page.Home;
    private readonly Crossfade pageTitle = new("Home");

    private void ShowPage(Page next)
    {
        if (next == page) return;
        page = next;
        pageTitle.Set(PageTitles[(int)next]);
        entranceStart = Now; // page content slides in again
        if (next == Page.Online) backend.RefreshAsync(force: true);
    }

    private static StatusKind? Lit(bool? running) => running == null ? StatusKind.Idle : running.Value ? StatusKind.Ok : StatusKind.Error;

    private bool OnPage(Widget w) => w.Page < 0 || w.Page == (int)page;

    // ================================================================== widgets

    private void BuildWidgets()
    {
        // ---- sidebar navigation (always visible)
        for (int i = 0; i < PageTitles.Length; i++)
        {
            var p = (Page)i;
            widgets.Add(new Widget
            {
                Rect = new RectangleF(14, 104 + i * 46, SideW - 28, 40), Label = PageTitles[i], Nav = true, Order = -1,
                Selected = () => page == p, Click = () => ShowPage(p),
            });
        }

        // ---- window controls
        widgets.Add(new Widget { Rect = new RectangleF(W - 46, 16, 30, 30), Label = "×", Small = true, Order = -1, Click = Close });
        widgets.Add(new Widget { Rect = new RectangleF(W - 82, 16, 30, 30), Label = "–", Small = true, Order = -1, Click = Minimize });
        widgets.Add(new Widget { Rect = new RectangleF(W - 196, 18, 104, 26), Label = "theme", Small = true, Order = -1, Click = CycleTheme });

        // ---- toolbar (always visible)
        float x = ContentX;
        widgets.Add(new Widget
        {
            Rect = new RectangleF(x, ToolbarY, 190, 42), Label = "Inject latest", Primary = true, Order = -1,
            Value = () => "",
            Enabled = () => needsElevation || (!busy && gameRunning && !injector.IsInjected && (config.RepositoryConfigured || BundledMenu.Available)),
            Click = () => { if (needsElevation) RestartElevated(); else RunAsync(InjectLatestAsync); },
        });
        x += 198;
        Tool(ref x, 140, "Test local build", () => !busy && gameRunning && !injector.IsInjected, () => RunAsync(InjectLocalAsync));
        Tool(ref x, 90, "Eject", () => !busy && injector.IsInjected, () => RunAsync(EjectAsync));
        Tool(ref x, 140, "Check updates", () => !busy, () => { nextReleaseCheck = DateTime.MinValue; CheckReleaseAsync(force: true); });
        Tool(ref x, 110, "Game log", null, OpenGameLog);

        BuildHome();
        BuildMenuPage();
        BuildOnlinePage();
        BuildSettingsPage();

        // ---- console
        widgets.Add(new Widget { Rect = new RectangleF(W - 24 - 70, ConsoleTop + 5, 64, 22), Label = "clear", Small = true, Order = -1,
                                 Click = () => { log.Clear(); Invalidate(); } });

        foreach (var wd in widgets) wd.ValueText.Current = wd.Value?.Invoke() ?? "";
    }

    private void Tool(ref float x, float w, string label, Func<bool>? enabled, Action click)
    {
        widgets.Add(new Widget { Rect = new RectangleF(x, ToolbarY, w, 42), Label = label, Small = true, Order = -1, Enabled = enabled, Click = click });
        x += w + 8;
    }

    private RectangleF Cell(int col, int row, int cols = 3, float h = 88, float gap = 12, float top = ContentY)
    {
        float cw = (ContentW - gap * (cols - 1)) / cols;
        return new RectangleF(ContentX + col * (cw + gap), top + row * (h + gap), cw, h);
    }

    private void Card(Page p, RectangleF r, int order, string title, Func<string> value, Func<string>? sub = null,
                      Func<StatusKind?>? light = null, Action? click = null, Func<float>? meter = null)
    {
        widgets.Add(new Widget
        {
            Rect = r, Label = title, Card = true, Page = (int)p, Order = order, Info = click == null,
            Value = value, Sub = sub, Light = light, Click = click, Meter = meter,
        });
    }

    private void BuildHome()
    {
        var p = Page.Home;
        Card(p, Cell(0, 0), 0, "Gorilla Tag", () => gameRunning ? "Running" : "Not running",
            () => injector.IsInjected ? $"menu loaded · {injectedLabel}" : gameRunning ? "ready to inject" : "start the game first",
            () => gameRunning ? StatusKind.Ok : StatusKind.Idle);
        Card(p, Cell(1, 0), 1, "Latest release", () => release?.Tag ?? "—",
            () => release == null ? releaseText : $"published {release.Published.LocalDateTime:d MMM} · click to check again",
            () => releaseState, click: () => { nextReleaseCheck = DateTime.MinValue; CheckReleaseAsync(force: true); });
        Card(p, Cell(2, 0), 2, "Menu copy", () => releaseCached ? "Downloaded" : BundledMenu.Available ? "Built in" : "Will download",
            () => menuCopyText, () => releaseCached || BundledMenu.Available ? StatusKind.Ok : StatusKind.Idle);
        Card(p, Cell(0, 1), 3, "Server", () => backend.HealthText, () => backend.Host,
            () => backend.Healthy == null ? StatusKind.Busy : backend.Healthy.Value ? StatusKind.Ok : StatusKind.Error,
            click: () => ShowPage(Page.Online));
        Card(p, Cell(1, 1), 4, "Free-tier usage", () => backend.UsageSummary, () => "of the free tier · stops before it costs money",
            () => backend.AnyPaused ? StatusKind.Error : backend.Healthy == true ? StatusKind.Ok : StatusKind.Idle,
            click: () => ShowPage(Page.Online), meter: () => backend.WorstUsage);
        Card(p, Cell(2, 1), 5, "Local build", () => localExists ? "Found" : "None", () => localBuildText,
            () => localExists ? StatusKind.Ok : StatusKind.Idle, click: () => ShowPage(Page.Menu));

        widgets.Add(new Widget
        {
            Rect = new RectangleF(ContentX, ContentY + 200, ContentW, 64), Page = (int)p, Order = 6,
            Label = "In game", Card = true, Info = true, Value = () => "",
            Sub = () => "Tab  opens the menu   ·   H  opens safe mode (fix settings, sync code)   ·   Y on the left controller in VR",
        });
    }

    private void BuildMenuPage()
    {
        var p = Page.Menu;
        Card(p, Cell(0, 0, 2), 0, "Latest release on GitHub", () => release?.Tag ?? "—",
            () => release == null ? releaseText : $"published {release.Published.LocalDateTime:d MMM yyyy} · signed",
            () => releaseState, click: OpenReleases);
        Card(p, Cell(1, 0, 2), 1, "Your copy", () => releaseCached ? "Downloaded" : BundledMenu.Available ? $"Built in {BundledMenu.Version}" : "Not yet",
            () => menuCopyText, () => releaseCached || BundledMenu.Available ? StatusKind.Ok : StatusKind.Idle);
        Card(p, Cell(0, 1, 2), 2, "Local test build", () => localExists ? Path.GetFileName(config.ResolvedLocalDll) : "Not set",
            () => localBuildText, () => localExists ? StatusKind.Ok : StatusKind.Idle, click: PickLocalDll);
        Card(p, Cell(1, 1, 2), 3, "Injected now", () => injector.IsInjected ? injectedLabel : "Nothing",
            () => injector.IsInjected ? "Eject before injecting another build" : "Use Inject latest or Test local build",
            () => injector.IsInjected ? StatusKind.Ok : StatusKind.Idle);
    }

    private void BuildOnlinePage()
    {
        var p = Page.Online;
        Card(p, Cell(0, 0), 0, "Requests today", () => backend.Line("requests"), () => "all features pause at the limit",
            () => Lit(backend.Running("all")), meter: () => backend.Fraction("requests"));
        Card(p, Cell(1, 0), 1, "Room + sync calls today", () => backend.Line("doCalls"), () => "menus seeing each other, settings sync",
            () => Lit(backend.Running("presence")), meter: () => backend.Fraction("doCalls"));
        Card(p, Cell(2, 0), 2, "Video + feed reads (month)", () => backend.Line("r2Reads"), () => "videos and the feed pause at the limit",
            () => Lit(backend.Running("r2")), meter: () => backend.Fraction("r2Reads"));
        Card(p, Cell(0, 1, 2), 3, "Server", () => backend.HealthText, () => backend.Host,
            () => backend.Healthy == null ? StatusKind.Busy : backend.Healthy.Value ? StatusKind.Ok : StatusKind.Error,
            click: () => backend.RefreshAsync(force: true));
        Card(p, Cell(1, 1, 2), 4, "How it resets", () => "Daily at 00:00 UTC", () => "monthly limits reset on the 1st · click a card to refresh",
            click: () => backend.RefreshAsync(force: true));
    }

    private void BuildSettingsPage()
    {
        var p = Page.Settings;
        float y = ContentY, h = 46, gap = 8;
        widgets.Add(new Widget
        {
            Rect = new RectangleF(ContentX, y, ContentW, h), Page = (int)p, Order = 0, Toggle = true,
            Label = "Match the in-game menu's theme", Selected = () => config.FollowGameTheme,
            Click = () => { config.FollowGameTheme = !config.FollowGameTheme; config.Save(); },
        });
        y += h + gap;
        widgets.Add(new Widget
        {
            Rect = new RectangleF(ContentX, y, ContentW, h), Page = (int)p, Order = 1, Toggle = true,
            Label = "Offer pre-release versions", Selected = () => config.IncludePrereleases,
            Click = () => { config.IncludePrereleases = !config.IncludePrereleases; config.Save(); nextReleaseCheck = DateTime.MinValue; },
        });
        y += h + gap;
        widgets.Add(new Widget
        {
            Rect = new RectangleF(ContentX, y, ContentW, h), Page = (int)p, Order = 2, Label = "Theme",
            Value = () => theme.Name + "  (click to change)", Click = CycleTheme,
        });
        y += h + gap;
        widgets.Add(new Widget
        {
            Rect = new RectangleF(ContentX, y, ContentW, h), Page = (int)p, Order = 3, Label = "Menu comes from",
            Value = () => $"github.com/{config.Repository}", Click = OpenReleases,
        });
        y += h + gap;
        widgets.Add(new Widget
        {
            Rect = new RectangleF(ContentX, y, ContentW, h), Page = (int)p, Order = 4, Label = "Licences",
            Value = () => "MIT · third-party notices", Click = OpenNotices,
        });
    }

    // ================================================================== extra actions

    private void OpenGameLog()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            @"AppData\LocalLow\Another Axiom\Gorilla Tag\Player.log");
        if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        else SetStatus("No game log yet - start Gorilla Tag once.", StatusKind.Error);
    }

    private void OpenReleases()
    {
        if (config.RepositoryConfigured)
            Process.Start(new ProcessStartInfo($"https://github.com/{config.Repository}/releases") { UseShellExecute = true });
    }

    private void PickLocalDll()
    {
        using var dialog = new OpenFileDialog { Filter = "Menu DLL (*.dll)|*.dll", Title = "Pick the BundleMenu.dll you built" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        config.LocalDllPath = dialog.FileName;
        config.Save();
        RefreshFileState();
        SetStatus($"Local build set: {dialog.FileName}", StatusKind.Idle);
    }
}
