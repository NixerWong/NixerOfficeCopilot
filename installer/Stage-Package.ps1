<#
.SYNOPSIS
    Stages the Word, Excel and PowerPoint copilots and the shared Broker into one directory tree
    for Advanced Installer.

.DESCRIPTION
    Copies the bin\<Configuration> output of each add-in and the OfficeCopilot.Agent.Broker net472
    output into installer\staging\, keeping the relative sub-folder layout intact (SlideAssets,
    DevExpress culture folders and runtimes\win-* must not be flattened).
    Also creates the flat fonts\ payload and root licenses\ payload consumed by Advanced Installer.

    A file that appears in several sources must be byte-identical; otherwise the script stops.
    Excluded by default: *.pdb, *.pfx, *.application, *.vshost.exe.
    publish\ is never read.

.PARAMETER Configuration
    Release (default) or Debug.

.PARAMETER SkipBuild
    Do not run build.ps1 before staging.

.PARAMETER IncludePdb
    Keep *.pdb symbol files in the staging directory.

.EXAMPLE
    .\installer\Stage-Package.ps1
    .\installer\Stage-Package.ps1 -SkipBuild
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [switch]$SkipBuild,
    [switch]$IncludePdb
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$root = Split-Path -Parent $PSScriptRoot
$stagingDir = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'staging'))

$sources = @(
    [pscustomobject]@{ Name = 'Word';       Dir = Join-Path $root "NixerWordCopilot\bin\$Configuration" }
    [pscustomobject]@{ Name = 'Excel';      Dir = Join-Path $root "NixerExcelCopilot\bin\$Configuration" }
    [pscustomobject]@{ Name = 'PowerPoint'; Dir = Join-Path $root "NixerPowerpointCopilot\bin\$Configuration" }
    [pscustomobject]@{ Name = 'Broker';     Dir = Join-Path $root "OfficeCopilot.Agent.Broker\bin\$Configuration\net472" }
)

if (-not $SkipBuild) {
    Write-Host "==== Building $Configuration (build.ps1 -Rebuild) ====" -ForegroundColor Cyan
    & (Join-Path $root 'build.ps1') -Configuration $Configuration -Rebuild
}

foreach ($s in $sources) {
    if (-not (Test-Path -LiteralPath $s.Dir)) {
        throw "Missing $($s.Name) output: $($s.Dir). Build first or remove -SkipBuild."
    }
}

$excluded = @('*.pfx', '*.application', '*.vshost.exe')
if (-not $IncludePdb) { $excluded += '*.pdb' }

$plan = @{}
$conflicts = New-Object System.Collections.Generic.List[string]
foreach ($s in $sources) {
    $files = Get-ChildItem -LiteralPath $s.Dir -Recurse -File -Force | Where-Object {
        $name = $_.Name
        -not ($excluded | Where-Object { $name -like $_ })
    }
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($s.Dir.Length).TrimStart('\')
        if ($plan.ContainsKey($rel)) {
            $existing = $plan[$rel]
            if ((Get-FileHash -LiteralPath $existing.FullName).Hash -ne (Get-FileHash -LiteralPath $f.FullName).Hash) {
                $conflicts.Add("$rel  ($($existing.Source) vs $($s.Name))")
            }
        }
        else {
            $plan[$rel] = [pscustomobject]@{ FullName = $f.FullName; Source = $s.Name }
        }
    }
}

if ($conflicts.Count -gt 0) {
    throw "Relative paths with different content:`n" + ($conflicts -join "`n")
}

$fontAssetsDir = Join-Path $root 'OfficeCopilot.Slides.Core\SlideAssets\fonts'
$fontManifestPath = Join-Path $fontAssetsDir 'fonts.json'
if (-not (Test-Path -LiteralPath $fontManifestPath -PathType Leaf)) {
    throw "Font manifest not found: $fontManifestPath"
}
$fontManifest = Get-Content -LiteralPath $fontManifestPath -Raw | ConvertFrom-Json
$fontFaceFiles = @{}
$fontLicenseFiles = @{}
foreach ($family in $fontManifest) {
    foreach ($face in $family.faces) {
        $fileName = [System.IO.Path]::GetFileName($face.file)
        if ($fileName -ne $face.file) {
            throw "Font face must be a file name, not a path: $($face.file)"
        }
        $source = Join-Path (Join-Path $fontAssetsDir $family.folder) $fileName
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Font face is missing: $source"
        }
        $relativePath = Join-Path 'fonts' $fileName
        if ($fontFaceFiles.ContainsKey($relativePath)) {
            if ((Get-FileHash -LiteralPath $fontFaceFiles[$relativePath]).Hash -ne
                (Get-FileHash -LiteralPath $source).Hash) {
                throw "Font faces collide at $relativePath"
            }
        }
        else {
            $fontFaceFiles[$relativePath] = $source
        }
    }

    $licenseSource = Join-Path (Join-Path $fontAssetsDir $family.folder) 'OFL.txt'
    if (-not (Test-Path -LiteralPath $licenseSource -PathType Leaf)) {
        throw "Font license is missing: $licenseSource"
    }
    $licenseRelativePath = Join-Path 'licenses' ($family.folder + '-OFL.txt')
    if ($fontLicenseFiles.ContainsKey($licenseRelativePath)) {
        if ((Get-FileHash -LiteralPath $fontLicenseFiles[$licenseRelativePath]).Hash -ne
            (Get-FileHash -LiteralPath $licenseSource).Hash) {
            throw "Font licenses collide at $licenseRelativePath"
        }
    }
    else {
        $fontLicenseFiles[$licenseRelativePath] = $licenseSource
    }
}

foreach ($relativePath in $fontFaceFiles.Keys) {
    if ($plan.ContainsKey($relativePath)) {
        if ((Get-FileHash -LiteralPath $plan[$relativePath].FullName).Hash -ne
            (Get-FileHash -LiteralPath $fontFaceFiles[$relativePath]).Hash) {
            throw "Relative path with different content: $relativePath (package output vs FontCatalog)"
        }
    }
    else {
        $plan[$relativePath] = [pscustomobject]@{
            FullName = $fontFaceFiles[$relativePath]
            Source = 'FontCatalog'
        }
    }
}
foreach ($relativePath in $fontLicenseFiles.Keys) {
    if ($plan.ContainsKey($relativePath)) {
        if ((Get-FileHash -LiteralPath $plan[$relativePath].FullName).Hash -ne
            (Get-FileHash -LiteralPath $fontLicenseFiles[$relativePath]).Hash) {
            throw "Relative path with different content: $relativePath (package output vs FontCatalog)"
        }
    }
    else {
        $plan[$relativePath] = [pscustomobject]@{
            FullName = $fontLicenseFiles[$relativePath]
            Source = 'FontCatalog'
        }
    }
}
$expectedFontPaths = @{}
foreach ($relativePath in $fontFaceFiles.Keys) { $expectedFontPaths[$relativePath] = $true }
$staleFlatFonts = @($plan.Keys | Where-Object {
    $_ -match '^fonts\\[^\\]+\.ttf$' -and -not $expectedFontPaths.ContainsKey($_)
})
foreach ($relativePath in $staleFlatFonts) { [void]$plan.Remove($relativePath) }
$expectedLicensePaths = @{}
foreach ($relativePath in $fontLicenseFiles.Keys) { $expectedLicensePaths[$relativePath] = $true }
$staleFontLicenses = @($plan.Keys | Where-Object {
    $_ -match '^licenses\\[^\\]+-OFL\.txt$' -and -not $expectedLicensePaths.ContainsKey($_)
})
foreach ($relativePath in $staleFontLicenses) { [void]$plan.Remove($relativePath) }

if (Test-Path -LiteralPath $stagingDir) {
    Remove-Item -LiteralPath $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDir | Out-Null

foreach ($rel in ($plan.Keys | Sort-Object)) {
    $dest = Join-Path $stagingDir $rel
    $destDir = Split-Path -Parent $dest
    if (-not (Test-Path -LiteralPath $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }
    Copy-Item -LiteralPath $plan[$rel].FullName -Destination $dest
}

$required = @(
    'NixerWordCopilot.vsto', 'NixerWordCopilot.dll.manifest',
    'NixerExcelCopilot.vsto', 'NixerExcelCopilot.dll.manifest',
    'NixerPowerpointCopilot.vsto', 'NixerPowerpointCopilot.dll.manifest',
    'OfficeCopilot.Agent.Broker.exe'
)
$required += @($fontFaceFiles.Keys)
$required += @($fontLicenseFiles.Keys)
$required += (Join-Path 'zh-Hans' 'OfficeCopilot.Localization.resources.dll')
$required += (Join-Path 'ja' 'OfficeCopilot.Localization.resources.dll')
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $stagingDir $_)) })
if ($missing.Count -gt 0) {
    throw "Staging is missing required entries: $($missing -join ', ')"
}

$totalBytes = ($plan.Keys | ForEach-Object { (Get-Item -LiteralPath (Join-Path $stagingDir $_)).Length } | Measure-Object -Sum).Sum
Write-Host ''
Write-Host ("Staged {0} files, {1:N1} MB -> {2}" -f $plan.Count, ($totalBytes / 1MB), $stagingDir) -ForegroundColor Green
$plan.Values | Group-Object Source | ForEach-Object { Write-Host ("  {0,-10} {1,5} files" -f $_.Name, $_.Count) }
