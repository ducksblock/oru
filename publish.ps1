<#
.SYNOPSIS
    Builds and packages oru for release.

.DESCRIPTION
    Publishes self-contained x64 and ARM64 builds, zips each one, and
    places the ready-to-upload ZIPs in Publish\release\.

    Usage:
        .\publish.ps1               # uses version from oru.csproj (1.0.0)
        .\publish.ps1 -Version 1.2.0

.PARAMETER Version
    Semantic version string to embed and use in the ZIP filename.
    Defaults to 1.0.0.
#>

param(
    [string]$Version = "1.0.0"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ── Paths ─────────────────────────────────────────────────────────────────────

$root      = $PSScriptRoot
$releaseDir = Join-Path $root "Publish\release"
$targets   = @(
    @{ RID = "win-x64";   Platform = "x64"   }
    @{ RID = "win-arm64"; Platform = "ARM64" }
)

# ── Pre-flight ─────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "  oru release builder  v$Version" -ForegroundColor Cyan
Write-Host "  ─────────────────────────────" -ForegroundColor DarkGray
Write-Host ""

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet CLI not found. Install the .NET 8 SDK and try again."
}

# Clean and recreate the release output folder
if (Test-Path $releaseDir) {
    Remove-Item $releaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $releaseDir | Out-Null

# ── Build each target ─────────────────────────────────────────────────────────

foreach ($target in $targets) {
    $rid      = $target.RID
    $platform = $target.Platform
    $appDir   = Join-Path $root "Publish\app\net8.0-windows10.0.22621.0\$rid\publish"

    Write-Host "  ► Building $rid ..." -ForegroundColor Yellow

    # Clean previous output
    if (Test-Path (Join-Path $root "Publish\app")) {
        Remove-Item (Join-Path $root "Publish\app") -Recurse -Force
    }

    dotnet publish "$root\oru.csproj" `
        -c Release `
        -p:Platform=$platform `
        -p:RuntimeIdentifier=$rid `
        -p:Version=$Version `
        --self-contained true `
        --nologo `
        -v quiet

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for $rid. Check output above."
    }

    # Locate the publish output (dotnet may vary the exact path slightly)
    $publishDir = Get-ChildItem -Path (Join-Path $root "Publish\app") -Filter "publish" -Recurse -Directory |
                  Select-Object -First 1 -ExpandProperty FullName

    if (-not $publishDir -or -not (Test-Path $publishDir)) {
        Write-Error "Could not find publish output for $rid at expected path."
    }

    Write-Host "    Published to: $publishDir" -ForegroundColor DarkGray

    # ── Zip it ────────────────────────────────────────────────────────────────
    $zipName = "oru-$Version-$rid.zip"
    $zipPath = Join-Path $releaseDir $zipName

    Write-Host "  ► Zipping → $zipName ..." -ForegroundColor Yellow

    Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

    $sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host "    Done. $sizeMB MB" -ForegroundColor Green
    Write-Host ""
}

# ── SHA256 checksums ──────────────────────────────────────────────────────────

Write-Host "  ► Generating checksums ..." -ForegroundColor Yellow

$checksumFile = Join-Path $releaseDir "checksums-sha256.txt"
$lines = @()

Get-ChildItem -Path $releaseDir -Filter "*.zip" | ForEach-Object {
    $hash = (Get-FileHash -Algorithm SHA256 $_.FullName).Hash.ToLower()
    $lines += "$hash  $($_.Name)"
    Write-Host "    $($_.Name)" -ForegroundColor DarkGray
    Write-Host "    $hash" -ForegroundColor DarkGray
    Write-Host ""
}

$lines | Set-Content -Path $checksumFile -Encoding UTF8

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host "  ✓ Release artifacts ready:" -ForegroundColor Green
Get-ChildItem -Path $releaseDir | ForEach-Object {
    Write-Host "    $($_.Name)" -ForegroundColor White
}
Write-Host ""
Write-Host "  Output folder: $releaseDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor White
Write-Host "    1. Push your code to GitHub" -ForegroundColor DarkGray
Write-Host "    2. Create a new Release on GitHub tagged v$Version" -ForegroundColor DarkGray
Write-Host "    3. Upload the ZIPs and checksums-sha256.txt as release assets" -ForegroundColor DarkGray
Write-Host "    4. Submit to WinGet: wingetcreate update --submit" -ForegroundColor DarkGray
Write-Host ""
