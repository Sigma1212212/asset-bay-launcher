<#
  Builds AssetBayLauncher.exe with the newest BundleMenu.dll built into it, zips it with its licences,
  and (unless -NoRelease) publishes it as a GitHub release of this launcher repo.

      .\build-release.ps1 -Version 1.0.0
      .\build-release.ps1 -Version 1.0.1 -MenuTag v1.2.0      # bundle a specific menu release
      .\build-release.ps1 -Version 1.0.1 -NoRelease            # just build dist\ locally

  The launcher still checks GitHub on every Inject and downloads anything newer; the built-in copy is
  the offline fallback. Needs: dotnet SDK and GitHub CLI (`gh auth login` once).
#>
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [string] $MenuTag = "",
    [string] $LauncherRepo = "Sigma1212212/asset-bay-launcher",
    [switch] $NoRelease
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')." }

$config = Get-Content (Join-Path $root "launcher.json") -Raw | ConvertFrom-Json
$menuRepo = $config.repository

# 1. Fetch the menu DLL + its checksum from the menu repo's release, and verify it.
$bundled = Join-Path $root "Bundled"
if (Test-Path $bundled) { Remove-Item $bundled -Recurse -Force }
New-Item -ItemType Directory $bundled | Out-Null

if ($MenuTag -eq "") {
    $MenuTag = (& gh release view --repo $menuRepo --json tagName --jq ".tagName").Trim()
    if ($LASTEXITCODE -ne 0) { throw "Couldn't find a release in $menuRepo." }
}
Write-Host "Bundling menu $MenuTag from $menuRepo..." -ForegroundColor Cyan
& gh release download $MenuTag --repo $menuRepo --pattern "BundleMenu.dll" --pattern "BundleMenu.dll.sha256" --dir $bundled
if ($LASTEXITCODE -ne 0) { throw "Download of $MenuTag failed." }

$expected = ((Get-Content (Join-Path $bundled "BundleMenu.dll.sha256") -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
$actual = (Get-FileHash (Join-Path $bundled "BundleMenu.dll") -Algorithm SHA256).Hash.ToLowerInvariant()
if ($expected -ne $actual) { throw "BundleMenu.dll from $MenuTag failed its SHA-256 check." }
[IO.File]::WriteAllText((Join-Path $bundled "version.txt"), $MenuTag)
Remove-Item (Join-Path $bundled "BundleMenu.dll.sha256")

# 2. Single-file, self-contained exe (no .NET install needed).
Write-Host "Building launcher $Version..." -ForegroundColor Cyan
$out = Join-Path $root "out"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
& dotnet publish (Join-Path $root "AssetBayLauncher.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:Version=$Version -o $out -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# Shipped settings: no developer-only local path.
$shipped = Get-Content (Join-Path $out "launcher.json") -Raw | ConvertFrom-Json
$shipped.localDllPath = ""
[IO.File]::WriteAllText((Join-Path $out "launcher.json"), ($shipped | ConvertTo-Json))

$readme = @"
Asset Bay Launcher $Version  (menu $MenuTag built in)

1. Start Gorilla Tag.
2. Run AssetBayLauncher.exe and click "Inject latest".
   It checks https://github.com/$menuRepo/releases for a newer menu, verifies its SHA-256
   and injects it. If GitHub can't be reached it uses the built-in $MenuTag.
3. In game press Tab (or Y on the left controller in VR).

Keep the files in this folder together; launcher.json holds the settings.
Licence: MIT (LICENSE). Third-party notices: THIRD-PARTY-NOTICES.txt
Source: https://github.com/$LauncherRepo

Mods in public lobbies can get your account banned - use private/modded lobbies.
"@
[IO.File]::WriteAllText((Join-Path $out "README.txt"), $readme)

# 3. Zip + checksum.
$dist = Join-Path $root "dist"
New-Item -ItemType Directory $dist -Force | Out-Null
$zip = Join-Path $dist "AssetBayLauncher-v$Version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip
$zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zip.sha256", "$zipHash  $(Split-Path $zip -Leaf)`n")
Write-Host "Built $zip" -ForegroundColor Green
Write-Host "SHA-256 $zipHash" -ForegroundColor DarkGray
if ($NoRelease) { return }

# 4. Release.
$commit = (& git -C $root rev-parse HEAD).Trim()
$notes = @"
**Download** ``AssetBayLauncher-v$Version-win-x64.zip``, extract it, start Gorilla Tag, run ``AssetBayLauncher.exe`` and click **Inject latest**. Windows 10/11 x64; no .NET install needed.

- Checks [$menuRepo releases](https://github.com/$menuRepo/releases) on every Inject, downloads anything newer and verifies its SHA-256 before injecting
- Menu **$MenuTag is built into the exe**, used when GitHub can't be reached
- **Test local build** and **Eject** for trying your own builds; themes match the in-game menu

SHA-256 of the zip: ``$zipHash``

MIT licence. Includes SharpMonoInjector v2.2 (MIT). Source for this build: $commit

Windows SmartScreen may warn because the exe isn't code-signed: *More info -> Run anyway*.
Mods in public lobbies can get your account banned; use private or modded lobbies.
"@
& gh release create "v$Version" $zip "$zip.sha256" --repo $LauncherRepo --target $commit --title "Asset Bay Launcher $Version" --notes $notes
if ($LASTEXITCODE -ne 0) { throw "gh release create failed." }
Write-Host "Published launcher v$Version." -ForegroundColor Green
