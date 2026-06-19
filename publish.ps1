# ──────────────────────────────────────────────────────────────────────────────
# oru — publish.ps1
# Builds self-contained releases for x64 and ARM64, then creates ZIP archives.
# Usage: .\publish.ps1
# ──────────────────────────────────────────────────────────────────────────────

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectDir = $PSScriptRoot
$projectFile = Join-Path $projectDir 'oru.csproj'
$publishRoot = Join-Path $projectDir 'Publish'
$releaseDir = Join-Path $publishRoot 'release'

# Clean previous release ZIPs
if (Test-Path $releaseDir) { Remove-Item $releaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

$targets = @(
    @{ Rid = 'win-x64';   Platform = 'x64';   ZipName = 'oru-x64.zip' },
    @{ Rid = 'win-arm64'; Platform = 'ARM64'; ZipName = 'oru-arm64.zip' }
)

foreach ($target in $targets) {
    $rid      = $target.Rid
    $platform = $target.Platform
    $zipName  = $target.ZipName
    $outDir   = Join-Path $publishRoot "app-$rid"

    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "  Building $rid ($platform)" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan

    # Clean previous publish output for this target
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }

    # Publish
    dotnet publish $projectFile `
        -c Release `
        -r $rid `
        -p:Platform=$platform `
        -p:PublishDir=$outDir `
        --self-contained true

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: dotnet publish failed for $rid" -ForegroundColor Red
        exit 1
    }

    # ── Strip unnecessary files ──────────────────────────────────────────
    # Remove PDB debug symbols
    Get-ChildItem -Path $outDir -Filter '*.pdb' -Recurse | Remove-Item -Force

    # Remove crash dump tool
    $createdump = Join-Path $outDir 'createdump.exe'
    if (Test-Path $createdump) { Remove-Item $createdump -Force }

    # Remove non-English locale satellite folders (keep en-us only)
    Get-ChildItem -Path $outDir -Directory | Where-Object {
        $_.Name -match '^[a-z]{2}(-[A-Za-z]{2,}){0,2}$' -and
        $_.Name -notin @('en-us', 'en-US') -and
        $_.Name -notin @('Views', 'Themes', 'Assets', 'runtimes', 'Microsoft.UI.Xaml')
    } | Remove-Item -Recurse -Force

    # ── Create ZIP ───────────────────────────────────────────────────────
    $zipPath = Join-Path $releaseDir $zipName
    Write-Host "`nCreating $zipName ..." -ForegroundColor Yellow
    Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -Force

    $sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
    Write-Host "  -> $zipPath ($sizeMB MB)" -ForegroundColor Green
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "  Done! Release ZIPs are in:" -ForegroundColor Cyan
Write-Host "  $releaseDir" -ForegroundColor White
Write-Host "========================================`n" -ForegroundColor Cyan
