#!/usr/bin/env pwsh
#Requires -Version 5.1
# Packaging for Viewfinder Head Tracking.
#
# There is deliberately no Nexus ZIP stage. The payload is a BepInEx 6 IL2CPP
# loader plus plugin DLLs, all of which have to land under the GAME ROOT, and
# Vortex only deploys into the subtree its per-game extension names in
# queryModPath. Vortex ships no Viewfinder extension (checked against its
# bundledPlugins set), so no mod manager route puts these files where the game
# loads them. Do not add one back: the release ZIP is an installer.
#
# The BepInEx 6 IL2CPP vendor zip is BepInEx_UnityIL2CPP_x64.zip rather than the
# BepInEx_win_x64.zip the shared BepInEx packager assumes, so the ZIP is staged
# here.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = 'SilentlyContinue'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir

Import-Module (Join-Path $projectDir "cameraunlock-core\powershell\ReleaseWorkflow.psm1") -Force

$csprojPath = Join-Path $projectDir "src\ViewfinderHeadTracking\ViewfinderHeadTracking.csproj"
$version = Get-CsprojVersion $csprojPath

$buildOutputDir = Join-Path $projectDir "src\ViewfinderHeadTracking\bin\Release\net6.0"
$scriptsDir = Join-Path $projectDir "scripts"
$releaseDir = Join-Path $projectDir "release"

$modDlls = @("ViewfinderHeadTracking.dll", "CameraUnlock.Core.dll", "CameraUnlock.Core.Unity.dll")

Write-Host "=== Viewfinder Head Tracking - Package Release ===" -ForegroundColor Magenta
Write-Host "Version: $version" -ForegroundColor Cyan

foreach ($dll in $modDlls) {
    $dllPath = Join-Path $buildOutputDir $dll
    if (-not (Test-Path $dllPath)) { throw "Required DLL not found: $dllPath. Run 'pixi run build' first." }
}

foreach ($script in @("install.cmd", "uninstall.cmd")) {
    $scriptPath = Join-Path $scriptsDir $script
    if (-not (Test-Path $scriptPath)) { throw "Required script not found: $scriptPath" }
}

$manifestPath = Join-Path $projectDir "launcher-manifest.json"
if (-not (Test-Path $manifestPath)) { throw "Required launcher manifest not found: $manifestPath" }

foreach ($doc in @("README.md", "LICENSE", "CHANGELOG.md", "THIRD-PARTY-NOTICES.md")) {
    if (-not (Test-Path (Join-Path $projectDir $doc))) {
        throw "Required document not found: $doc. Every published ZIP is a binary distribution and must carry it."
    }
}

# Vendoring is the install-time source of truth; refresh with `pixi run update-deps`.
$vendorBepDir = Join-Path $projectDir "vendor\bepinex"
$vendorFiles = @("BepInEx_UnityIL2CPP_x64.zip", "LICENSE", "README.md")
foreach ($vendorFile in $vendorFiles) {
    $src = Join-Path $vendorBepDir $vendorFile
    if (-not (Test-Path $src)) {
        throw "Required vendor file missing: $src. Run 'pixi run update-deps' to refresh."
    }
}

Assert-ManifestSeedsMatchShipped -ManifestPath $manifestPath -ProjectRoot $projectDir

if (-not (Test-Path $releaseDir)) { New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null }

$stagingDir = Join-Path $releaseDir "staging-installer"
if (Test-Path $stagingDir) { Remove-Item -Recurse -Force $stagingDir }
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

foreach ($script in @("install.cmd", "uninstall.cmd")) {
    Copy-Item (Join-Path $scriptsDir $script) -Destination $stagingDir -Force
    Write-Host "  $script" -ForegroundColor Green
}

# Stamp mod_info.version with the release version as the manifest is staged.
# Scoped to mod_info: -replace is global, so an unanchored pattern would also
# rewrite a version a dependencies[] or runtime_requirements[] entry pins.
$manifestJson = [System.IO.File]::ReadAllText($manifestPath)
$manifestJson = $manifestJson -replace '("mod_info":\s*\{[^}]*?"version":\s*")\d+\.\d+\.\d+(")', "`${1}$version`${2}"
if ($manifestJson -notmatch ('"mod_info":\s*\{[^}]*?"version":\s*"' + [regex]::Escape($version) + '"')) {
    throw "Failed to stamp mod_info.version in launcher-manifest.json"
}
# No BOM: under Windows PowerShell 5.1 Set-Content -Encoding UTF8 writes one, and
# the launcher's JSON parser rejects a manifest whose first byte is EF.
[System.IO.File]::WriteAllText(
    (Join-Path $stagingDir "launcher-manifest.json"), $manifestJson, [System.Text.UTF8Encoding]::new($false))
Write-Host "  launcher-manifest.json (version $version)" -ForegroundColor Green

$pluginsDir = Join-Path $stagingDir "plugins"
New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null
foreach ($dll in $modDlls) {
    Copy-Item (Join-Path $buildOutputDir $dll) -Destination $pluginsDir -Force
    Write-Host "  plugins/$dll" -ForegroundColor Green
}

$stageVendorDir = Join-Path $stagingDir "vendor\bepinex"
New-Item -ItemType Directory -Path $stageVendorDir -Force | Out-Null
foreach ($vendorFile in $vendorFiles) {
    Copy-Item (Join-Path $vendorBepDir $vendorFile) -Destination $stageVendorDir -Force
    Write-Host "  vendor/bepinex/$vendorFile" -ForegroundColor Green
}

# install.cmd and uninstall.cmd resolve the game through shared/find-game.ps1 on
# every run, so a ZIP without shared/ fails on startup for every user.
Copy-SharedBundle -StagingDir $stagingDir -CoreRoot (Join-Path $projectDir 'cameraunlock-core')

foreach ($doc in @("README.md", "LICENSE", "CHANGELOG.md", "THIRD-PARTY-NOTICES.md")) {
    Copy-Item (Join-Path $projectDir $doc) -Destination $stagingDir -Force
    Write-Host "  $doc" -ForegroundColor Green
}

$zipName = "ViewfinderHeadTracking-v$version-installer.zip"
$zipPath = Join-Path $releaseDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Push-Location $stagingDir
try {
    Compress-Archive -Path ".\*" -DestinationPath $zipPath -Force
} finally {
    Pop-Location
}
Remove-Item -Recurse -Force $stagingDir

$zipSize = (Get-Item $zipPath).Length / 1KB
Write-Host ""
Write-Host ("=== Package Complete: $zipPath ({0:N1} KB) ===" -f $zipSize) -ForegroundColor Magenta

Write-Output $zipPath
