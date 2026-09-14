#!/usr/bin/env pwsh
# Populate src/ViewfinderHeadTracking/libs/ with the Unity reference DLLs that
# CameraUnlock.Core.Unity's HintPath references resolve to. The plugin itself takes
# BepInEx, HarmonyX and Il2CppInterop straight from its PackageReferences.
#
# Sources are REPO FILES ONLY - never a game install. A contributor who owns
# Viewfinder and a CI runner that does not must compile against byte-identical
# references, otherwise a member missing from the reference set passes locally
# and fails on push.
#
# libs/ is wiped first so a local run reproduces the runner's empty-libs/ start
# and a stale DLL cannot survive into the build.
#
# Run order: dotnet restore -> setup -> dotnet build. The pixi `build` task wires
# this, and CI calls the same script through `pixi run package`.

$ErrorActionPreference = "Stop"

$scriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir
$libsPath    = Join-Path $projectRoot "src/ViewfinderHeadTracking/libs"

New-Item -ItemType Directory -Path $libsPath -Force | Out-Null
Get-ChildItem $libsPath -Filter '*.dll' -File | Remove-Item -Force

$nugetRoot = (& dotnet nuget locals global-packages -l) -replace '^global-packages: ', ''
if (-not (Test-Path $nugetRoot)) {
    throw "NuGet global-packages root not found: $nugetRoot. Run 'dotnet restore' first."
}

$unityModulesDir = Join-Path $nugetRoot 'unityengine.modules/2021.3.45/lib/netstandard2.0'
if (-not (Test-Path $unityModulesDir)) {
    throw "UnityEngine.Modules NuGet package not found at $unityModulesDir."
}
Copy-Item (Join-Path $unityModulesDir '*.dll') $libsPath -Force

$dlls = Get-ChildItem $libsPath -Filter '*.dll'
Write-Host "Populated $($dlls.Count) DLLs in $libsPath" -ForegroundColor Green
