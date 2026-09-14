#!/usr/bin/env pwsh
#Requires -Version 5.1
# Development deploy: copies the built plugin and its CameraUnlock dependencies
# into BepInEx/plugins of EVERY Viewfinder install on this machine, extracting
# the vendored loader first where BepInEx is not there yet. install.cmd is what
# players run.

param(
    [string]$Configuration = 'Release',
    [string]$GamePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

Import-Module (Join-Path $projectRoot 'cameraunlock-core/powershell/DevDeploy.psm1')

# The game holds the plugin DLLs open, so a copy over a running game fails, and a
# deploy that fails right before a launch looks exactly like a change that did
# nothing.
if (Get-Process -Name 'Viewfinder' -ErrorAction SilentlyContinue) {
    throw "Viewfinder is running. Close it before deploying, or the plugin DLLs cannot be overwritten."
}

$deployArgs = @{
    GameId          = 'viewfinder'
    GameDisplayName = 'Viewfinder'
    BuildOutputPath = (Join-Path $projectRoot "src/ViewfinderHeadTracking/bin/$Configuration/net6.0")
    ModDllName      = 'ViewfinderHeadTracking.dll'
    ExtraDlls       = @('CameraUnlock.Core.dll', 'CameraUnlock.Core.Unity.dll')
    EnsureLoader    = $true
    MajorVersion    = 6
    VendorZip       = (Join-Path $projectRoot 'vendor/bepinex/BepInEx_UnityIL2CPP_x64.zip')
}
if ($GamePath) { $deployArgs.GivenPath = $GamePath }

Invoke-DevDeployBepInEx @deployArgs | Out-Null
