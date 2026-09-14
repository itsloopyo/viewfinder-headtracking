#!/usr/bin/env pwsh
#Requires -Version 5.1
[CmdletBinding()]
param([switch]$AllowDirty)
$ErrorActionPreference = 'Stop'
$ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Import-Module (Join-Path $ProjectRoot 'cameraunlock-core\powershell\ReleaseWorkflow.psm1') -Force
Import-Module (Join-Path $ProjectRoot 'cameraunlock-core\powershell\NightlyRelease.psm1') -Force

$version = Get-CsprojVersion (Join-Path $ProjectRoot 'src\ViewfinderHeadTracking\ViewfinderHeadTracking.csproj')

# -NoNexusZip: this mod is installer-only (see the package-release.ps1 header),
# and Publish-NightlyBuild otherwise treats the missing Nexus ZIP as fatal.
Publish-NightlyBuild `
    -NoNexusZip `
    -ModId 'viewfinder' `
    -ModName 'ViewfinderHeadTracking' `
    -Version $version `
    -ProjectRoot $ProjectRoot `
    -BuildCommand 'pixi run build' `
    -AllowDirty:$AllowDirty
