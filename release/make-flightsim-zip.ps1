<#
.SYNOPSIS
    Makes the zip for flightsim.to: the Airsports client (portable, no installer, no .NET to install) and the MSFS toolbar
    panel, with installation notes.

.DESCRIPTION
    Airsports-MSFS-<version>.zip
      Airsports client\                 the client; start msfsclient.exe (it also installs the panel: Profile > Toolbar panel)
      Community\airsports-flight-display\  the toolbar panel, for copying into the MSFS Community folder by hand
      README.txt

    The build number goes into the client's version ("Airsports 2.0.0 (<build>)"), which the server's minimum version for
    the simulator client is compared with; use the same numbers as for the Microsoft Store.

    Unsigned, Windows shows "Windows protected your PC" on the first start (More info > Run anyway). The release workflow
    signs the client and zips it again when signing is set up (see .github/workflows/flightsim.yml).

.EXAMPLE
    ./release/make-flightsim-zip.ps1 -Build 31
#>
param(
    [Parameter(Mandatory)][int]$Build,
    [string]$VersionName = "2.0.0",
    [string]$Out = (Join-Path $PSScriptRoot "..\dist")
)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$assemblyVersion = "$($VersionName.Split('.')[0]).$($VersionName.Split('.')[1]).$Build.0"  # the client reads the third part as the build

# 1. The toolbar panel (also bundled into the client, so the client can install it).
& (Join-Path $repo "msfs-panel\build-panel.ps1") -Version "1.0.$Build"

# 2. The client, self-contained and unpackaged.
$publish = Join-Path $Out "publish"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
& (Join-Path $repo "publish_portable.ps1") -Out $publish -Version $assemblyVersion

# 3. The bundle.
$name = "Airsports-MSFS-$VersionName-$Build"
$bundle = Join-Path $Out $name
if (Test-Path $bundle) { Remove-Item $bundle -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $bundle "Community") | Out-Null
Move-Item $publish (Join-Path $bundle "Airsports client")
Copy-Item (Join-Path $repo "msfs-panel\dist\airsports-flight-display") (Join-Path $bundle "Community") -Recurse
$readme = (Get-Content (Join-Path $PSScriptRoot "README-flightsim.txt") -Raw).Replace("{VERSION}", "$VersionName ($Build)")
[IO.File]::WriteAllText((Join-Path $bundle "README.txt"), $readme.Replace("`r`n", "`n").Replace("`n", "`r`n"), (New-Object System.Text.UTF8Encoding $false))

$zip = Join-Path $Out "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $bundle "*") -DestinationPath $zip
Write-Host "Zip: $zip"
