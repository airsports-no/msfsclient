# Create a portable, folder-based release of the MSFS Client (self-contained: nothing to install).
# This is more reliable for WinUI 3 than SingleFile mode.
param(
    # Where the client goes; by default the usual publish folder.
    [string]$Out = "",
    # Package-style version, e.g. 2.0.31.0 (the third part is the build number the client shows and reports).
    [string]$Version = ""
)
$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "msfsclient\msfsclient.csproj"
$extra = @()
if ($Out) { $extra += "-o", $Out }
if ($Version) { $extra += "-p:Version=$Version" }

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    -p:Unpackaged=true `
    -p:EnableMsixTooling=false `
    -p:PublishSingleFile=false `
    -p:DebugType=portable `
    @extra
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# An unpackaged publish leaves out the app's resource index (msfsclient.pri) and compiled XAML (*.xbf), without which the
# window cannot load ("XAML parsing failed"). The build output next to it has them: copy them over.
$build = Join-Path $PSScriptRoot "msfsclient\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64"
$publish = if ($Out) { $Out } else { Join-Path $build "publish" }
# Absolute (a relative -Out is relative to the current location, as for dotnet publish), so the files already in the
# publish folder are recognised and skipped even when it lies inside the build folder.
$publish = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($publish)
$publishPrefix = $publish.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
Get-ChildItem $build -Recurse -File -Include *.pri, *.xbf |
    Where-Object { -not $_.FullName.StartsWith($publishPrefix, [StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object {
        $target = Join-Path $publish $_.FullName.Substring($build.Length + 1)
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        Copy-Item $_.FullName $target -Force
    }
# The symbols are built for crash reports (uploaded to Sentry from the build when it is configured), not shipped.
Get-ChildItem $publish -Recurse -File -Filter *.pdb | Remove-Item
if (-not (Test-Path (Join-Path $publish "msfsclient.pri"))) { throw "msfsclient.pri is missing from $publish" }
Write-Host "Portable client: $publish"
