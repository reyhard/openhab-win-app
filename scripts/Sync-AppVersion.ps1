[CmdletBinding()]
param(
    [ValidateSet("major", "minor", "patch", "revision")]
    [string]$Increment = "patch",

    [string]$Version,

    [switch]$CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$trayProjectPath = Join-Path $repoRoot "src\OpenHab.Windows.Tray\OpenHab.Windows.Tray.csproj"
$trayManifestPath = Join-Path $repoRoot "src\OpenHab.Windows.Tray\Package.appxmanifest"
$packageManifestPath = Join-Path $repoRoot "src\OpenHab.Windows.Package\Package.appxmanifest"

function Get-VersionSegments {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    if ($Value -notmatch "^\d+\.\d+\.\d+(?:\.\d+)?$") {
        throw "Unsupported version format '$Value'. Use numeric semantic style: major.minor.patch or major.minor.patch.revision."
    }

    $parts = $Value.Split(".") | ForEach-Object { [int]$_ }
    if ($parts.Length -eq 3) {
        return @($parts[0], $parts[1], $parts[2], 0)
    }

    return $parts
}

function Format-Version {
    param([int[]]$Segments)
    return "$($Segments[0]).$($Segments[1]).$($Segments[2]).$($Segments[3])"
}

function Read-Text {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "File not found: $Path"
    }
    return [System.IO.File]::ReadAllText($Path)
}

function Write-IfChanged {
    param(
        [string]$Path,
        [string]$Original,
        [string]$Updated
    )

    if ($Original -ne $Updated) {
        $bytes = [System.IO.File]::ReadAllBytes($Path)
        $hasUtf8Bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
        [System.IO.File]::WriteAllText($Path, $Updated, [System.Text.UTF8Encoding]::new($hasUtf8Bom))
        return $true
    }

    return $false
}

function Replace-First {
    param(
        [string]$Content,
        [string]$Pattern,
        [string]$Replacement
    )

    $regex = [System.Text.RegularExpressions.Regex]::new(
        $Pattern,
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    return $regex.Replace($Content, $Replacement, 1)
}

$trayProjectOriginal = Read-Text -Path $trayProjectPath
$trayManifestOriginal = Read-Text -Path $trayManifestPath
$packageManifestOriginal = Read-Text -Path $packageManifestPath

$trayVersionMatch = [System.Text.RegularExpressions.Regex]::Match(
    $trayProjectOriginal,
    "<Version>([^<]+)</Version>")

if (-not $trayVersionMatch.Success) {
    throw "Could not find <Version>...</Version> in $trayProjectPath"
}

$trayManifestVersionMatch = [System.Text.RegularExpressions.Regex]::Match(
    $trayManifestOriginal,
    '<Identity\b[^>]*\bVersion="([^"]+)"',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)
$packageManifestVersionMatch = [System.Text.RegularExpressions.Regex]::Match(
    $packageManifestOriginal,
    '<Identity\b[^>]*\bVersion="([^"]+)"',
    [System.Text.RegularExpressions.RegexOptions]::Singleline)

if (-not $trayManifestVersionMatch.Success -or -not $packageManifestVersionMatch.Success) {
    throw "Could not find Identity Version in one or more appxmanifest files."
}

if ($CheckOnly) {
    $trayProjectVersion = (Format-Version -Segments (Get-VersionSegments -Value $trayVersionMatch.Groups[1].Value.Trim()))
    $trayManifestVersion = (Format-Version -Segments (Get-VersionSegments -Value $trayManifestVersionMatch.Groups[1].Value))
    $packageManifestVersion = (Format-Version -Segments (Get-VersionSegments -Value $packageManifestVersionMatch.Groups[1].Value))

    if ($trayProjectVersion -ne $trayManifestVersion -or $trayProjectVersion -ne $packageManifestVersion) {
        throw @"
Version mismatch detected:
- OpenHab.Windows.Tray.csproj : $($trayVersionMatch.Groups[1].Value.Trim())
- OpenHab.Windows.Tray/Package.appxmanifest : $($trayManifestVersionMatch.Groups[1].Value)
- OpenHab.Windows.Package/Package.appxmanifest : $($packageManifestVersionMatch.Groups[1].Value)
"@
    }

    Write-Host "Versions are already synchronized at $trayProjectVersion."
    exit 0
}

$currentSegments = Get-VersionSegments -Value $trayVersionMatch.Groups[1].Value.Trim()

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $newSegments = Get-VersionSegments -Value $Version
}
else {
    $newSegments = @($currentSegments[0], $currentSegments[1], $currentSegments[2], $currentSegments[3])
    switch ($Increment) {
        "major" {
            $newSegments[0]++
            $newSegments[1] = 0
            $newSegments[2] = 0
            $newSegments[3] = 0
        }
        "minor" {
            $newSegments[1]++
            $newSegments[2] = 0
            $newSegments[3] = 0
        }
        "patch" {
            $newSegments[2]++
            $newSegments[3] = 0
        }
        "revision" {
            $newSegments[3]++
        }
        default {
            throw "Unsupported increment: $Increment"
        }
    }
}

$newVersion = Format-Version -Segments $newSegments

$trayProjectUpdated = Replace-First `
    -Content $trayProjectOriginal `
    -Pattern "(<Version>)([^<]+)(</Version>)" `
    -Replacement "`${1}$newVersion`${3}"

$trayManifestUpdated = Replace-First `
    -Content $trayManifestOriginal `
    -Pattern '(<Identity\b[^>]*\bVersion=")([^"]+)(")' `
    -Replacement "`${1}$newVersion`${3}"

$packageManifestUpdated = Replace-First `
    -Content $packageManifestOriginal `
    -Pattern '(<Identity\b[^>]*\bVersion=")([^"]+)(")' `
    -Replacement "`${1}$newVersion`${3}"

$changed = $false
$changed = (Write-IfChanged -Path $trayProjectPath -Original $trayProjectOriginal -Updated $trayProjectUpdated) -or $changed
$changed = (Write-IfChanged -Path $trayManifestPath -Original $trayManifestOriginal -Updated $trayManifestUpdated) -or $changed
$changed = (Write-IfChanged -Path $packageManifestPath -Original $packageManifestOriginal -Updated $packageManifestUpdated) -or $changed

if ($changed) {
    Write-Host "Updated synchronized app version to $newVersion."
}
else {
    Write-Host "Version already set to $newVersion; no file changes."
}

if ($env:GITHUB_OUTPUT) {
    "version=$newVersion" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
}
