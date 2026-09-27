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

# The config tests run the legacy .cfg import on the BepInEx.Core the game runs, the one in the
# vendored loader archive, rather than the NuGet build the plugin compiles against.
# The published reader the differential test runs saves, and ConfigFile.Save loads SemanticVersioning.
$testLibsPath = Join-Path $projectRoot "tests/ViewfinderHeadTracking.ConfigTests/libs"
New-Item -ItemType Directory -Path $testLibsPath -Force | Out-Null
Get-ChildItem $testLibsPath -Filter '*.dll' -File | Remove-Item -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
$loaderZip = Join-Path $projectRoot "vendor/bepinex/BepInEx_UnityIL2CPP_x64.zip"
$archive = [System.IO.Compression.ZipFile]::OpenRead($loaderZip)
try {
    foreach ($name in @('BepInEx.Core.dll', 'SemanticVersioning.dll')) {
        $entry = $archive.Entries | Where-Object { $_.FullName -eq "BepInEx/core/$name" }
        if (-not $entry) {
            throw "BepInEx/core/$name is not in $loaderZip"
        }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $testLibsPath $name), $true)
    }
} finally {
    $archive.Dispose()
}
Write-Host "Extracted the vendored BepInEx.Core.dll and SemanticVersioning.dll into $testLibsPath" -ForegroundColor Green
