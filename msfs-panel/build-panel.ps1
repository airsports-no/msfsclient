<#
.SYNOPSIS
    Builds the "Airsports flight display" MSFS toolbar panel: a Community folder package and a zip of it.

.DESCRIPTION
    The package is plain files (the panel page, hud.js, hud.css, fonts, the toolbar icon, manifest.json, layout.json) plus
    one compiled file, InGamePanels\InGamePanel_Airsports.spb, made by the MSFS SDK from PackageSources\InGamePanels\*.xml.
    That file only changes when the panel definition (toolbar entry, window size) changes, so it is kept in prebuilt\ and
    the rest is assembled by this script anywhere, also in CI, without the SDK.

    -Sdk compiles the .spb with the SDK's fspackagetool (needs the MSFS 2020 or 2024 SDK and the simulator) and stores it
    in prebuilt\. Run it once, and again after changing InGamePanel_Airsports.xml; commit prebuilt\InGamePanels\*.spb.

.EXAMPLE
    ./build-panel.ps1 -Sdk            # first time, or after changing the panel definition
    ./build-panel.ps1 -Version 1.0.1  # assemble dist\airsports-flight-display and the zip
#>
param(
    [string]$Version = "1.0.0",
    [switch]$Sdk,
    [string]$Out = (Join-Path $PSScriptRoot "dist")
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$name = "airsports-flight-display"
$panelDir = Join-Path $here "PackageSources\html_ui\InGamePanels\AirsportsPanel"
$hudDir = Join-Path $here "..\msfsclient.Core\Hud"

# 1. The display itself, shared with the client's own page (one source of truth in msfsclient.Core\Hud).
Copy-Item (Join-Path $hudDir "hud.js"), (Join-Path $hudDir "hud.css") $panelDir -Force
New-Item -ItemType Directory -Force (Join-Path $panelDir "fonts") | Out-Null
Copy-Item (Join-Path $hudDir "fonts\*.ttf") (Join-Path $panelDir "fonts") -Force

# 2. The compiled panel definition.
$spb = Join-Path $here "prebuilt\InGamePanels\InGamePanel_Airsports.spb"
if ($Sdk) {
    $sdkRoot = @($env:MSFS2024_SDK, $env:MSFS_SDK) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $sdkRoot) { throw "No MSFS SDK found: install it (Developer Mode > Help > SDK installer) so MSFS_SDK or MSFS2024_SDK is set." }
    $tool = Get-ChildItem $sdkRoot -Recurse -Filter fspackagetool.exe | Select-Object -First 1
    if (-not $tool) { throw "fspackagetool.exe not found under $sdkRoot" }
    Write-Host "Building with $($tool.FullName) (this starts the simulator in the background)..."
    # The tool reports progress on stderr ("Attached to EXE - waiting for completion."); with "Stop", Windows PowerShell
    # would turn that into a terminating error when the output is redirected. Its exit code says whether it worked.
    $ErrorActionPreference = "Continue"
    & $tool.FullName (Join-Path $here "$name.xml") -nopause -outputtoseparateconsole 2>&1 | ForEach-Object { "$_" }
    $exit = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($exit -ne 0) { throw "fspackagetool failed ($exit)" }
    $built = Join-Path $here "Packages\$name\InGamePanels\InGamePanel_Airsports.spb"
    if (-not (Test-Path $built)) { throw "The SDK build did not produce $built" }
    New-Item -ItemType Directory -Force (Split-Path $spb) | Out-Null
    Copy-Item $built $spb -Force
    Write-Host "Stored $spb - commit it."
}
if (-not (Test-Path $spb)) {
    throw "Missing $spb. Build it once with the MSFS SDK: ./build-panel.ps1 -Sdk (see README.md)."
}

# 3. Assemble the Community folder package.
$pkg = Join-Path $Out $name
if (Test-Path $pkg) { Remove-Item $pkg -Recurse -Force }
New-Item -ItemType Directory -Force $pkg | Out-Null
Copy-Item (Join-Path $here "PackageSources\html_ui") $pkg -Recurse
New-Item -ItemType Directory -Force (Join-Path $pkg "InGamePanels") | Out-Null
Copy-Item $spb (Join-Path $pkg "InGamePanels")
$contentInfo = Join-Path $pkg "ContentInfo\$name"
New-Item -ItemType Directory -Force $contentInfo | Out-Null
Copy-Item (Join-Path $here "PackageDefinitions\$name\ContentInfo\*") $contentInfo

$manifest = [ordered]@{
    dependencies = @()
    content_type = "MISC"
    title = "Airsports flight display"
    manufacturer = ""
    creator = "Air Sports Live Tracking"
    package_version = $Version
    minimum_game_version = "1.37.19"
    release_notes = [ordered]@{ neutral = [ordered]@{ LastUpdate = ""; OlderHistory = "" } }
}
$utf8 = New-Object System.Text.UTF8Encoding $false  # no byte-order mark, as the SDK writes them
[IO.File]::WriteAllText((Join-Path $pkg "manifest.json"), ($manifest | ConvertTo-Json -Depth 5), $utf8)

# layout.json lists every file with its size and time (Windows FILETIME), as the SDK writes it; MSFS mounts only these.
$files = Get-ChildItem $pkg -Recurse -File | Where-Object { $_.Name -notin @("layout.json", "manifest.json") } | Sort-Object FullName
$content = foreach ($f in $files) {
    [ordered]@{
        path = $f.FullName.Substring($pkg.Length + 1).Replace("\", "/")
        size = $f.Length
        date = $f.LastWriteTimeUtc.ToFileTimeUtc()
    }
}
[IO.File]::WriteAllText((Join-Path $pkg "layout.json"), (@{ content = @($content) } | ConvertTo-Json -Depth 4), $utf8)

# 4. The zip for flightsim.to (and for the client to install from).
$zip = Join-Path $Out "$name-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $pkg -DestinationPath $zip
Write-Host "Package: $pkg"
Write-Host "Zip:     $zip"
