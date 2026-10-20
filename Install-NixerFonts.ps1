[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$fontRoot = Join-Path $PSScriptRoot 'OfficeCopilot.Slides.Core\SlideAssets\fonts'
$manifestPath = Join-Path $fontRoot 'fonts.json'
$installRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Fonts'
$licenseRoot = Join-Path $installRoot 'NixerOfficeCopilot\licenses'
$stateDirectory = Join-Path $env:LOCALAPPDATA 'NixerOfficeCopilot\fonts'
$statePath = Join-Path $stateDirectory 'installed.json'
$registryPath = 'HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts'
$fontFilePrefix = 'NixerOfficeCopilot_' # Keep in sync with FontInstallConstants.FileNamePrefix.

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Font manifest not found: $manifestPath"
}

$manifest = [System.IO.File]::ReadAllText($manifestPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$faces = @()
foreach ($family in $manifest) {
    if ($family.license -ne 'OFL-1.1') {
        throw "Refusing to install non-OFL font family: $($family.family)"
    }

    $familyRoot = Join-Path $fontRoot $family.folder
    $licensePath = Join-Path $familyRoot 'OFL.txt'
    if (-not (Test-Path -LiteralPath $licensePath -PathType Leaf)) {
        throw "OFL license file is missing for $($family.family): $licensePath"
    }
    $licenseText = [System.IO.File]::ReadAllText($licensePath, [System.Text.Encoding]::UTF8)
    if (-not $licenseText.Contains('SIL OPEN FONT LICENSE Version 1.1')) {
        throw "License verification failed for $($family.family)"
    }

    foreach ($face in $family.faces) {
        $source = Join-Path $familyRoot $face.file
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "Font face is missing: $source"
        }
        $style = if ($face.style -eq 'italic') { 'Italic' } else { 'Regular' }
        $fileName = $fontFilePrefix + [System.IO.Path]::GetFileName($face.file)
        $faces += [pscustomobject]@{
            Family = $family.family
            Source = $source
            FileName = $fileName
            RegistryName = [System.IO.Path]::GetFileNameWithoutExtension($fileName) + ' (TrueType)'
            License = $licensePath
            LicenseName = $family.folder + '-OFL.txt'
        }
    }
}

if (Test-Path -LiteralPath $statePath -PathType Leaf) {
    $installedState = [System.IO.File]::ReadAllText($statePath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    if ($installedState.schemaVersion -ne 1) {
        throw "Unsupported installed font manifest version: $statePath"
    }
} else {
    $installedState = [pscustomobject]@{
        schemaVersion = 1
        updatedUtc = [DateTime]::UtcNow.ToString('o')
        fonts = @()
        pendingDeletionPaths = @()
    }
}
if ($null -eq $installedState.fonts) { $installedState.fonts = @() }
if ($null -eq $installedState.pendingDeletionPaths) { $installedState.pendingDeletionPaths = @() }

function Save-InstalledFontState {
    if (-not (Test-Path -LiteralPath $stateDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
    }
    $installedState.updatedUtc = [DateTime]::UtcNow.ToString('o')
    $temporaryPath = $statePath + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
    $json = $installedState | ConvertTo-Json -Depth 8
    try {
        [System.IO.File]::WriteAllText($temporaryPath, $json, [System.Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $statePath -PathType Leaf) {
            [System.IO.File]::Replace($temporaryPath, $statePath, $null)
        } else {
            [System.IO.File]::Move($temporaryPath, $statePath)
        }
    } finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

if (-not ('NixerFontInstallerNativeMethods' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
public static class NixerFontInstallerNativeMethods
{
    public static string ReadFontVersion(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < 12) return null;
        int tables = ReadUInt16(data, 4);
        int nameOffset = -1;
        int nameLength = 0;
        for (int i = 0; i < tables; i++)
        {
            int record = 12 + 16 * i;
            if (record + 16 > data.Length) return null;
            if (Encoding.ASCII.GetString(data, record, 4) == "name")
            {
                nameOffset = (int)ReadUInt32(data, record + 8);
                nameLength = (int)ReadUInt32(data, record + 12);
                break;
            }
        }
        if (nameOffset < 0 || nameOffset + 6 > data.Length) return null;
        int count = ReadUInt16(data, nameOffset + 2);
        int storage = nameOffset + ReadUInt16(data, nameOffset + 4);
        string fallback = null;
        for (int i = 0; i < count; i++)
        {
            int record = nameOffset + 6 + 12 * i;
            if (record + 12 > data.Length) return null;
            if (ReadUInt16(data, record) != 3 || ReadUInt16(data, record + 6) != 5) continue;
            int length = ReadUInt16(data, record + 8);
            int start = storage + ReadUInt16(data, record + 10);
            if (start < 0 || start + length > data.Length || start + length > nameOffset + nameLength) continue;
            string value = Encoding.BigEndianUnicode.GetString(data, start, length);
            if (ReadUInt16(data, record + 4) == 0x0409) return value;
            if (fallback == null) fallback = value;
        }
        return fallback;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int AddFontResourceEx(string fileName, uint flags, IntPtr reserved);

    private static int ReadUInt16(byte[] data, int offset)
    {
        return (data[offset] << 8) | data[offset + 1];
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool RemoveFontResourceEx(string fileName, uint flags, IntPtr reserved);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint message, UIntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
}
'@ -ErrorAction Stop
}

function Send-FontChangeNotification {
    try {
        $result = [IntPtr]::Zero
        [void][NixerFontInstallerNativeMethods]::SendMessageTimeout([IntPtr]0xffff, 0x001D, [UIntPtr]::Zero, [IntPtr]::Zero, 2, 5000, [ref]$result)
    } catch {
        Write-Warning 'Font registration changed, but Windows font cache notification failed. Sign out or restart Office apps.'
    }
}

if ($Uninstall) {
    foreach ($face in $faces) {
        $destination = Join-Path $installRoot $face.FileName
        $registered = $false
        if (Test-Path -LiteralPath $registryPath) {
            $property = Get-ItemProperty -LiteralPath $registryPath
            if ($property.PSObject.Properties.Name -contains $face.RegistryName) {
                if ($PSCmdlet.ShouldProcess($face.RegistryName, 'Remove current-user font registration')) {
                    Remove-ItemProperty -LiteralPath $registryPath -Name $face.RegistryName
                    $registered = $true
                    if ('NixerFontInstallerNativeMethods' -as [type]) {
                        [void][NixerFontInstallerNativeMethods]::RemoveFontResourceEx($destination, 0, [IntPtr]::Zero)
                    }
                }
            }
        }
        if (Test-Path -LiteralPath $destination -PathType Leaf) {
            if ($PSCmdlet.ShouldProcess($destination, 'Remove installed font file')) {
                try {
                    Remove-Item -LiteralPath $destination -Force
                    $installedState.pendingDeletionPaths = @($installedState.pendingDeletionPaths | Where-Object {
                        -not [string]::Equals($_, $destination, [System.StringComparison]::OrdinalIgnoreCase)
                    })
                } catch [System.IO.IOException] {
                    if ($destination -notin $installedState.pendingDeletionPaths) {
                        $installedState.pendingDeletionPaths += $destination
                    }
                    Write-Warning "Font registration was removed but the file is still in use; cleanup is pending: $destination"
                } catch [System.UnauthorizedAccessException] {
                    if ($destination -notin $installedState.pendingDeletionPaths) {
                        $installedState.pendingDeletionPaths += $destination
                    }
                    Write-Warning "Font registration was removed but the file could not be deleted; cleanup is pending: $destination"
                }
            }
        }

        if (($registered -or -not (Test-Path -LiteralPath $destination -PathType Leaf)) -and $PSCmdlet.ShouldProcess($statePath, 'Update installed font manifest')) {
            $installedState.fonts = @($installedState.fonts | Where-Object {
                -not [string]::Equals($_.path, $destination, [System.StringComparison]::OrdinalIgnoreCase)
            })
            Save-InstalledFontState
        }
    }
    if (Test-Path -LiteralPath $licenseRoot -PathType Container) {
        foreach ($family in $manifest) {
            $licenseCopy = Join-Path $licenseRoot ($family.folder + '-OFL.txt')
            if ((Test-Path -LiteralPath $licenseCopy -PathType Leaf) -and $PSCmdlet.ShouldProcess($licenseCopy, 'Remove copied OFL license')) {
                Remove-Item -LiteralPath $licenseCopy -Force
            }
        }
    }
    if ($WhatIfPreference) {
        Write-Output ("WhatIf complete: validated removal of {0} font faces; no files, registry values, or manifest entries were changed." -f $faces.Count)
        return
    }
    Send-FontChangeNotification
    Write-Output 'Nixer Office Copilot fonts removed for the current user.'
    return
}

if (-not (Test-Path -LiteralPath $installRoot -PathType Container)) {
    if ($PSCmdlet.ShouldProcess($installRoot, 'Create current-user Fonts directory')) {
        New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
    }
}
if (-not (Test-Path -LiteralPath $licenseRoot -PathType Container)) {
    if ($PSCmdlet.ShouldProcess($licenseRoot, 'Create OFL license directory')) {
        New-Item -ItemType Directory -Path $licenseRoot -Force | Out-Null
    }
}
if (-not (Test-Path -LiteralPath $registryPath)) {
    if ($PSCmdlet.ShouldProcess($registryPath, 'Create current-user font registry key')) {
        New-Item -Path $registryPath -Force | Out-Null
    }
}

foreach ($face in $faces) {
    $destination = Join-Path $installRoot $face.FileName
    if ($PSCmdlet.ShouldProcess($destination, 'Install current-user font ' + $face.Family)) {
        Copy-Item -LiteralPath $face.Source -Destination $destination -Force
        $loadedFaces = [NixerFontInstallerNativeMethods]::AddFontResourceEx($destination, 0, [IntPtr]::Zero)
        if ($loadedFaces -eq 0) {
            throw "Windows could not load installed font face: $destination"
        }
        try {
            New-ItemProperty -LiteralPath $registryPath -Name $face.RegistryName -Value $destination -PropertyType String -Force | Out-Null
        } catch {
            [void][NixerFontInstallerNativeMethods]::RemoveFontResourceEx($destination, 0, [IntPtr]::Zero)
            throw
        }
        $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
        $fontVersion = [NixerFontInstallerNativeMethods]::ReadFontVersion($destination)
        $installedState.fonts = @($installedState.fonts | Where-Object {
            -not [string]::Equals($_.path, $destination, [System.StringComparison]::OrdinalIgnoreCase)
        })
        $installedState.fonts += [pscustomobject]@{
            family = $face.Family
            fileName = $face.FileName
            path = $destination
            sha256 = $hash
            fontVersion = $fontVersion
            registered = $true
        }
        Save-InstalledFontState
    }
}
foreach ($license in ($faces | Group-Object LicenseName | ForEach-Object { $_.Group[0] })) {
    $destination = Join-Path $licenseRoot $license.LicenseName
    if ($PSCmdlet.ShouldProcess($destination, 'Copy OFL license notice')) {
        Copy-Item -LiteralPath $license.License -Destination $destination -Force
    }
}

if ($WhatIfPreference) {
    Write-Output ("WhatIf complete: validated {0} OFL-1.1 font faces; no files or registry values were changed." -f $faces.Count)
    return
}

Send-FontChangeNotification
Write-Output ("Installed {0} OFL-1.1 font faces for the current user. Restart Office applications to use them." -f $faces.Count)
