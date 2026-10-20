<#
.SYNOPSIS
    Unified build script for NixerOfficeCopilot (item E7).

.DESCRIPTION
    Works around two known build traps:
      1. The solution contains legacy VSTO projects, so `dotnet build` fails with MSB4019 / MSB4803.
         Visual Studio's own MSBuild must be used instead.
      2. "Broker rebuild trap": OfficeCopilot.Agent.Broker is a standalone exe and is NOT a dependency
         of any add-in, so incremental builds do not reliably copy a fresh Agent.Runtime.dll into
         Broker\bin. When that happens Excel silently runs stale runtime code.
    This script force-rebuilds the Broker after the solution build and verifies that the Runtime.dll
    inside Broker\bin is newer than the newest runtime source file.

    NOTE: this file is intentionally ASCII-only. Windows PowerShell 5.1 decodes BOM-less .ps1 files as
    ANSI, which would corrupt non-ASCII characters, and the repository requires UTF-8 without BOM.

.PARAMETER Configuration
    Debug (default) or Release.

.PARAMETER Rebuild
    Use /t:Rebuild instead of /t:Build.

.PARAMETER Test
    Run OfficeCopilot.Agent.Tests after a successful build.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Release -Rebuild
    .\build.ps1 -Test
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Rebuild,
    [switch]$Test
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$solution = Join-Path $root 'NixerOfficeCopilot.sln'
$brokerOutputDir = Join-Path $root "OfficeCopilot.Agent.Broker\bin\$Configuration\net472"
$brokerRuntimeDll = Join-Path $brokerOutputDir 'OfficeCopilot.Agent.Runtime.dll'
$runtimeSourceDir = Join-Path $root 'OfficeCopilot.Agent.Runtime'
$target = if ($Rebuild) { 'Rebuild' } else { 'Build' }

function Write-Step([string]$text) {
    Write-Host ''
    Write-Host "==== $text ====" -ForegroundColor Cyan
}

function Resolve-VsWhere {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path $vswhere)) {
        throw "vswhere.exe not found at $vswhere. Install Visual Studio 2022 or later."
    }
    return $vswhere
}

function Resolve-MSBuild {
    $msbuild = & (Resolve-VsWhere) -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
        Select-Object -First 1
    if (-not $msbuild -or -not (Test-Path $msbuild)) {
        throw 'VS MSBuild not found. Legacy VSTO projects cannot be built with dotnet build.'
    }
    return $msbuild
}

function Invoke-MSBuild([string]$msbuild, [string]$projectPath, [string]$buildTarget) {
    & $msbuild $projectPath "/t:$buildTarget" "/p:Configuration=$Configuration" '/p:Platform=Any CPU' '/m' '/v:m' '/nologo'
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed (ExitCode=$LASTEXITCODE): $projectPath"
    }
}

$msbuild = Resolve-MSBuild
Write-Host "MSBuild       : $msbuild"
Write-Host "Configuration : $Configuration / $target"

# Purging the Broker output before the build is what defeats the trap: MSBuild's copy-to-output step then has
# to re-copy every dependency. Rebuilding the csproj on its own is not an option because the legacy projects in
# its dependency chain only define 'Debug|AnyCPU', not the solution's 'Debug|Any CPU'.
Write-Step 'Purging Broker output (defeats the Broker rebuild trap)'
if (Test-Path $brokerOutputDir) {
    Remove-Item $brokerOutputDir -Recurse -Force
    Write-Host "Removed $brokerOutputDir"
}
else {
    Write-Host 'Nothing to purge.'
}

Write-Step 'Restoring NuGet packages'
Invoke-MSBuild $msbuild $solution 'Restore'

Write-Step 'Building solution'
Invoke-MSBuild $msbuild $solution $target

Write-Step 'Verifying Broker runtime output freshness'
if (-not (Test-Path $brokerRuntimeDll)) {
    throw "Broker-side runtime assembly was not produced: $brokerRuntimeDll"
}

$dllTime = (Get-Item $brokerRuntimeDll).LastWriteTimeUtc
$newestSource = Get-ChildItem $runtimeSourceDir -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Sort-Object LastWriteTimeUtc -Descending |
    Select-Object -First 1

Write-Host ("Runtime.dll   : {0:yyyy-MM-dd HH:mm:ss}Z" -f $dllTime)
Write-Host ("Newest source : {0:yyyy-MM-dd HH:mm:ss}Z  ({1})" -f $newestSource.LastWriteTimeUtc, $newestSource.Name)

if ($dllTime -lt $newestSource.LastWriteTimeUtc) {
    throw 'Agent.Runtime.dll in Broker bin is older than the newest source change; the output is stale.'
}
Write-Host 'Freshness check passed.' -ForegroundColor Green

if ($Test) {
    Write-Step 'Running unit tests'
    $vstest = & (Resolve-VsWhere) -latest -find 'Common7\IDE\Extensions\TestPlatform\vstest.console.exe' |
        Select-Object -First 1
    $testDll = Join-Path $root "OfficeCopilot.Agent.Tests\bin\$Configuration\OfficeCopilot.Agent.Tests.dll"
    if (-not $vstest -or -not (Test-Path $vstest)) {
        throw 'vstest.console.exe not found.'
    }
    if (-not (Test-Path $testDll)) {
        throw "Test assembly not found: $testDll"
    }
    & $vstest $testDll '/Logger:console;verbosity=minimal'
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed (ExitCode=$LASTEXITCODE)."
    }
}

Write-Step 'Done'
Write-Host 'All steps succeeded.' -ForegroundColor Green
