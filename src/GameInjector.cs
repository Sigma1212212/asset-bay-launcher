using System.Diagnostics;
using SharpMonoInjector;

namespace AssetBayLauncher;

/// <summary>
/// Thin wrapper over SharpMonoInjector (MIT). Loads the DLL's bytes into the game's Mono runtime and calls
/// its static entry method; remembers the returned assembly handle so the same session can eject it again.
/// </summary>
public sealed class GameInjector : IDisposable
{
    private readonly LauncherConfig config;
    private Injector? injector;
    private IntPtr assembly;
    private int injectedPid;

    public GameInjector(LauncherConfig config) => this.config = config;

    public bool IsInjected => assembly != IntPtr.Zero && GameProcess()?.Id == injectedPid;

    public Process? GameProcess() =>
        Process.GetProcessesByName(config.ProcessName).OrderBy(p => p.StartTime).FirstOrDefault();

    public void Inject(string dllPath)
    {
        var game = GameProcess() ?? throw new InvalidOperationException($"{config.ProcessName} isn't running.");
        if (IsInjected) throw new InvalidOperationException("Already injected - eject first to load a different build.");

        byte[] bytes = File.ReadAllBytes(dllPath);
        if (bytes.Length < 2 || bytes[0] != 'M' || bytes[1] != 'Z')
            throw new InvalidOperationException($"{Path.GetFileName(dllPath)} is not a .NET DLL.");

        ResetInjector();
        injector = new Injector(game.Id);
        if (!injector.Is64Bit)
            throw new InvalidOperationException("The game process is 32-bit; this launcher only supports 64-bit.");

        assembly = injector.Inject(bytes, config.EntryNamespace, config.EntryClass, config.EntryMethod);
        if (assembly == IntPtr.Zero)
            throw new InvalidOperationException("The game's Mono runtime refused the DLL (entry point not found?).");
        injectedPid = game.Id;
    }

    public void Eject()
    {
        if (injector == null || assembly == IntPtr.Zero)
            throw new InvalidOperationException("Nothing was injected from this launcher session.");
        try
        {
            injector.Eject(assembly, config.EntryNamespace, config.EntryClass, config.EjectMethod);
        }
        finally
        {
            ResetInjector();
        }
    }

    private void ResetInjector()
    {
        assembly = IntPtr.Zero;
        injectedPid = 0;
        injector?.Dispose();
        injector = null;
    }

    public void Dispose() => injector?.Dispose();
}
