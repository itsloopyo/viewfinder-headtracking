#!/usr/bin/env pwsh
#Requires -Version 5.1
<#
.SYNOPSIS
    Release workflow for Viewfinder Head Tracking. Unattended end to end.

.DESCRIPTION
    Invoking it is the authorisation; nothing here asks. Preconditions (semver,
    main branch, clean tree, tag absent) fail fast with exit 1. Then it writes the
    changelog, bumps every version string, builds and packages through pixi, runs
    the content gates, commits, tags and pushes. The tag push triggers
    .github/workflows/release.yml.

.PARAMETER Version
    major | minor | patch | nightly | X.Y.Z. With no argument, prints the current
    version and usage.

.PARAMETER Force
    Ship even when no user-facing commits landed since the last tag, writing a
    maintenance changelog entry instead of aborting.

.EXAMPLE
    pixi run release patch
#>
param(
    [Parameter(Position=0)]
    [string]$Version = "",
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir
$csprojPath = Join-Path $projectDir "src\ViewfinderHeadTracking\ViewfinderHeadTracking.csproj"
$pluginPath = Join-Path $projectDir "src\ViewfinderHeadTracking\Core\HeadTrackingPlugin.cs"
$installCmdPath = Join-Path $projectDir "scripts\install.cmd"
$manifestPath = Join-Path $projectDir "launcher-manifest.json"
$pixiTomlPath = Join-Path $projectDir "pixi.toml"
$changelogPath = Join-Path $projectDir "CHANGELOG.md"
$noticesPath = Join-Path $projectDir "THIRD-PARTY-NOTICES.md"

Import-Module (Join-Path $projectDir "cameraunlock-core\powershell\ReleaseWorkflow.psm1") -Force

function Write-Utf8NoBom {
    param([string]$Path, [string]$Content)
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function Fail {
    param([string]$Message)
    Write-Host "Error: $Message" -ForegroundColor Red
    exit 1
}

Write-Host "=== Viewfinder Head Tracking Release ===" -ForegroundColor Cyan
Write-Host ""

$currentVersion = Get-CsprojVersion $csprojPath

if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "Current version: $currentVersion"
    Write-Host "Usage: pixi run release <major|minor|patch|nightly|X.Y.Z>"
    exit 0
}

if ($Version -eq 'nightly') {
    & (Join-Path $PSScriptRoot 'release-nightly.ps1')
    exit $LASTEXITCODE
}

# Step 1: resolve and validate the version.
try {
    $Version = Resolve-ReleaseVersion -Argument $Version -CurrentVersion $currentVersion
} catch {
    Fail $_.Exception.Message
}
# Bare X.Y.Z only: a prerelease suffix produces a tag release.yml and the
# launcher manifest cannot both agree on.
if ($Version -notmatch '^\d+\.\d+\.\d+$') { Fail "Resolved version '$Version' is not a bare X.Y.Z semver" }
$tagName = "v$Version"

# Step 2: preconditions. These are the whole safety net; there is no prompt.
Push-Location $projectDir
try {
    $currentBranch = git rev-parse --abbrev-ref HEAD
    if ($currentBranch -ne "main") { Fail "Must be on 'main' to release (currently on '$currentBranch')" }

    $status = git status --porcelain
    if ($status) {
        Write-Host $status -ForegroundColor Gray
        Fail "Working tree has uncommitted changes; commit or stash them first"
    }

    if (git tag -l $tagName) { Fail "Tag '$tagName' already exists" }

    Write-Host "Current version: $currentVersion" -ForegroundColor Gray
    Write-Host "New version:     $Version" -ForegroundColor Green
    Write-Host ""

    # THIRD-PARTY-NOTICES.md names the cameraunlock-core commit compiled into the
    # release ZIP, and a submodule bump does not touch it. The packager refuses a
    # mismatch, so re-sync it here and let this release carry the correction.
    & (Join-Path $projectDir 'cameraunlock-core\scripts\sync-core-notices.ps1') -Repo $projectDir
    if ($LASTEXITCODE -ne 0) { Fail "sync-core-notices.ps1 exited $LASTEXITCODE; fix THIRD-PARTY-NOTICES.md before releasing" }

    # Step 3: changelog. This is the gate that aborts when there are no
    # user-facing commits, so it runs before any version string is touched.
    Write-Host "Generating CHANGELOG..." -ForegroundColor Cyan
    $changelog = [System.IO.File]::ReadAllText($changelogPath)
    $date = Get-Date -Format 'yyyy-MM-dd'
    $hasVersionTags = git tag -l 'v[0-9]*'
    if (-not $hasVersionTags -and $changelog -match '(?m)^## \[0\.0\.0\][^\r\n]*') {
        # First release: the unreleased [0.0.0] section is the hand-written notes
        # for it, and dumping every commit since the first one would bury them.
        $changelog = $changelog -replace '(?m)^## \[0\.0\.0\][^\r\n]*', "## [$Version] - $date"
        Write-Utf8NoBom $changelogPath $changelog
        Write-Host "  First release - retitled the [0.0.0] section to [$Version]" -ForegroundColor Gray
    } else {
        try {
            New-ChangelogFromCommits -ChangelogPath $changelogPath -Version $Version -ArtifactPaths @(
                "src/ViewfinderHeadTracking/",
                "cameraunlock-core",
                "scripts/install.cmd",
                "scripts/uninstall.cmd"
            ) | Out-Null
        } catch {
            if (-not $Force) {
                Write-Host $_.Exception.Message -ForegroundColor Red
                Fail "No user-facing changes to release. Re-run with -Force for a maintenance release."
            }
            Write-Host "No user-facing commits since the last tag - writing a maintenance entry (-Force)." -ForegroundColor Yellow
            $entry = "## [$Version] - $date`n`n### Changed`n`n- Maintenance release (no user-facing changes).`n`n"
            $changelog = [System.IO.File]::ReadAllText($changelogPath)
            if ($changelog -notmatch '(?m)^## \[') { Fail "CHANGELOG.md has no version section to insert above" }
            $changelog = ([regex]'(?m)^## \[').Replace($changelog, "$entry## [", 1)
            Write-Utf8NoBom $changelogPath $changelog
        }
    }

    # Step 4: version strings. The csproj is canonical; the rest are kept in step.
    Write-Host "Updating version to $Version..." -ForegroundColor Cyan
    Set-CsprojVersion $csprojPath $Version

    $pluginContent = [System.IO.File]::ReadAllText($pluginPath)
    $updatedPlugin = $pluginContent -replace 'PluginVersion = "[^"]+"', "PluginVersion = `"$Version`""
    if ($updatedPlugin -notmatch ('PluginVersion = "' + [regex]::Escape($Version) + '"')) { Fail "Could not stamp PluginVersion in $pluginPath" }
    Write-Utf8NoBom $pluginPath $updatedPlugin

    # Byte-level so install.cmd keeps its CRLF line endings.
    $installCmdContent = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($installCmdPath))
    $updatedInstallCmd = $installCmdContent -replace 'set "MOD_VERSION=[^"]+"', "set `"MOD_VERSION=$Version`""
    if ($updatedInstallCmd -notmatch ('set "MOD_VERSION=' + [regex]::Escape($Version) + '"')) { Fail "Could not stamp MOD_VERSION in $installCmdPath" }
    Write-Utf8NoBom $installCmdPath $updatedInstallCmd

    if (-not (Test-Path $manifestPath)) { Fail "launcher-manifest.json not found: $manifestPath" }
    $manifestContent = [System.IO.File]::ReadAllText($manifestPath)
    $updatedManifest = $manifestContent -replace '("mod_info":\s*\{[^}]*?"version":\s*")\d+\.\d+\.\d+(")', "`${1}$Version`${2}"
    if ($updatedManifest -notmatch ('"mod_info":\s*\{[^}]*?"version":\s*"' + [regex]::Escape($Version) + '"')) { Fail "Could not stamp mod_info.version in $manifestPath" }
    Write-Utf8NoBom $manifestPath $updatedManifest

    $pixiContent = [System.IO.File]::ReadAllText($pixiTomlPath)
    $updatedPixi = $pixiContent -replace '(?m)^version = "\d+\.\d+\.\d+"', "version = `"$Version`""
    Write-Utf8NoBom $pixiTomlPath $updatedPixi

    # Step 5: build and package through the same pixi chain CI runs, then the
    # content gates. Packaging rather than only building, because what the
    # packager asserts is checked nowhere else before a tag that cannot be taken back.
    Write-Host "Building and packaging..." -ForegroundColor Cyan
    pixi run package
    if ($LASTEXITCODE -ne 0) { Fail "pixi run package failed" }
    foreach ($gate in @("validate-manifest", "validate-notices")) {
        Write-Host "Running $gate..." -ForegroundColor Cyan
        pixi run $gate
        if ($LASTEXITCODE -ne 0) { Fail "$gate failed" }
    }

    # Step 6: commit.
    Write-Host "Committing..." -ForegroundColor Cyan
    git add -- $csprojPath $pluginPath $installCmdPath $manifestPath $pixiTomlPath $changelogPath $noticesPath
    if ($LASTEXITCODE -ne 0) { Fail "git add failed" }
    git commit -m "Release v$Version"
    if ($LASTEXITCODE -ne 0) { Fail "git commit failed" }

    # Step 7: annotated tag.
    git tag -a $tagName -m "Release $tagName"
    if ($LASTEXITCODE -ne 0) { Fail "Tag creation failed" }

    # Step 8: push. A failed push leaves the tag local and CI never runs.
    Write-Host "Pushing..." -ForegroundColor Cyan
    git push origin main
    if ($LASTEXITCODE -ne 0) { Fail "Push of main failed" }
    git push origin $tagName
    if ($LASTEXITCODE -ne 0) { Fail "Push of tag $tagName failed" }
} finally {
    Pop-Location
}

Write-Host ""
Write-Host "Release $tagName pushed. .github/workflows/release.yml builds and publishes it:" -ForegroundColor Green
Write-Host "  https://github.com/itsloopyo/viewfinder-headtracking/actions" -ForegroundColor Cyan
