using System.Diagnostics;
using SharpMonoInjector;

namespace AssetBayLauncher;

/// <summary>
/// Thin wrapper over SharpMonoInjector (MIT). Loads the DLL's bytes into the game's Mono runtime and calls
/// its static entry method; remembers the returned assembly handle so the same session can eject it again.
///
/// Finding the game means enumerating every process on the PC, so that only happens in <see cref="Refresh"/>
/// (about once a second). Everything the UI asks every frame reads cached values.
/// </summary>
public sealed class GameInjector : IDisposable
{
    private readonly LauncherConfig config;
    private Injector? injector;
    private IntPtr assembly;
    private volatile int injectedPid;
    private volatile int gamePid;

    public GameInjector(LauncherConfig config) => this.config = config;

    public bool GameRunning => gamePid != 0;
    public bool IsInjected => assembly != IntPtr.Zero && injectedPid != 0 && injectedPid == gamePid;

    /// <summary>Re-scan for the game. Call about once a second, never per frame.</summary>
    public void Refresh()
    {
        Process[] found;
        try { found = Process.GetProcessesByName(config.ProcessName); }
        catch { found = Array.Empty<Process>(); }

        try
        {
            // Lowest PID is a stable choice that needs no special access (StartTime throws for elevated processes).
            int pid = 0;
            foreach (var p in found)
            {
                try { if (!p.HasExited && (pid == 0 || p.Id < pid)) pid = p.Id; }
                catch { if (pid == 0 || p.Id < pid) pid = p.Id; } // HasExited can be denied; the PID is still valid
            }
            gamePid = pid;
        }
        finally
        {
            foreach (var p in found) p.Dispose(); // each Process holds an OS handle
        }

        if (gamePid == 0 && injectedPid != 0) ResetInjector(); // game closed: drop the stale handle
    }

    public void Inject(string dllPath)
    {
        Refresh();
        if (gamePid == 0) throw new InvalidOperationException($"{config.ProcessName} isn't running.");
        if (IsInjected) throw new InvalidOperationException("Already injected - eject first to load a different build.");

        byte[] bytes = File.ReadAllBytes(dllPath);
        if (bytes.Length < 2 || bytes[0] != 'M' || bytes[1] != 'Z')
            throw new InvalidOperationException($"{Path.GetFileName(dllPath)} is not a .NET DLL.");

        ResetInjector();
        injector = new Injector(gamePid);
        if (!injector.Is64Bit)
            throw new InvalidOperationException("The game process is 32-bit; this launcher only supports 64-bit.");

        assembly = injector.Inject(bytes, config.EntryNamespace, config.EntryClass, config.EntryMethod);
        if (assembly == IntPtr.Zero)
            throw new InvalidOperationException("The game's Mono runtime refused the DLL (entry point not found?).");
        injectedPid = gamePid;
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
