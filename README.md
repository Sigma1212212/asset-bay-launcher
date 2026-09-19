# Asset Bay Launcher

Loads the [Asset Bay menu](https://github.com/Sigma1212212/asset-bay) (`BundleMenu.dll`) into Gorilla Tag.

**Download:** [latest release](https://github.com/Sigma1212212/asset-bay-launcher/releases/latest). Extract it, start the game,
run `AssetBayLauncher.exe`, click **Inject latest**, then press Tab in game (Y on the left controller in VR).

## Where the menu comes from

1. **GitHub first.** Every Inject checks the menu repo's newest release. If it's newer than what you have,
   the launcher downloads it and **verifies its SHA-256** before injecting. A file that fails the check is never used.
2. **Built into the exe.** Each launcher release carries a copy of the newest menu at the time it was built.
   It's used when GitHub can't be reached, and it saves a download when it's already the latest version.

So publishing a new menu release updates everyone, and nobody needs to download a new launcher for it.

## The window

| Row / button | Meaning |
|---|---|
| Gorilla Tag | Green when the game is running. Shows "menu loaded" after an injection. |
| Latest release | The newest tag in the menu repo. Click it to check again (it also re-checks every 5 minutes). |
| Menu copy | What would be injected: the downloaded version, the built-in one, or "will download". |
| Local build | Your own `BundleMenu.dll`, and how long ago you built it. |
| **Inject latest** | Newest version from GitHub (or the built-in fallback), verified, injected. |
| **Test local build** | Injects the DLL you just built, without publishing. |
| **Eject** | Removes the menu, so you can load another build without restarting the game. |

The launcher uses the same four themes as the in-game menu, and switches to whichever one you last picked in the game.
Click the theme chip in the top bar to choose one yourself.

## Building

```powershell
dotnet build -c Release
```

The output is `bin\Release\net8.0-windows\AssetBayLauncher.exe`. A plain build doesn't include a built-in menu, so it downloads from GitHub.

## Releasing the launcher

```powershell
.\build-release.ps1 -Version 1.0.1
```

It does four things:
1. Downloads the newest menu DLL from GitHub and checks its SHA-256.
2. Builds a self-contained single-file exe with that DLL inside.
3. Zips it with `LICENSE` and `THIRD-PARTY-NOTICES.txt`.
4. Publishes the release.

You only need this when the launcher itself changes. Menu updates are released from the menu repo with `publish.ps1`.

## Licence

MIT (see [LICENSE](LICENSE)). It includes SharpMonoInjector v2.2 (MIT, © 2017 Biney), taken unmodified from its
official release. See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
