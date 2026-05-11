[CmdletBinding()]
param(
    [string] $Solution = (Join-Path $PSScriptRoot 'OpenHab.Windows.sln'),
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [string] $Platform = 'x64',
    [string[]] $MSBuildArgs = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$installerRoots = @(
    ${env:ProgramFiles(x86)},
    $env:ProgramFiles
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$vswhere = $installerRoots |
    ForEach-Object { Join-Path $_ 'Microsoft Visual Studio\Installer\vswhere.exe' } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1

if (-not $vswhere) {
    throw 'vswhere.exe was not found. Install Visual Studio Installer or add vswhere.exe to the standard Visual Studio Installer path.'
}

$vswhereArgs = @(
    '-prerelease',
    '-products', '*',
    '-requires', 'Microsoft.Component.MSBuild',
    '-property', 'installationPath'
)

$installationPaths = @(& $vswhere @vswhereArgs) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

if (-not $installationPaths) {
    throw 'No Visual Studio installation with MSBuild was found. Install the MSBuild component in Visual Studio Installer.'
}

$selected = $null
foreach ($installationPath in $installationPaths) {
    $msbuild = Join-Path $installationPath 'MSBuild\Current\Bin\MSBuild.exe'
    $desktopBridgeProps = Join-Path $installationPath 'MSBuild\Microsoft\DesktopBridge\Microsoft.DesktopBridge.props'

    if ((Test-Path -LiteralPath $msbuild) -and (Test-Path -LiteralPath $desktopBridgeProps)) {
        $selected = [PSCustomObject]@{
            InstallationPath = $installationPath
            MSBuild = $msbuild
            DesktopBridgeProps = $desktopBridgeProps
        }
        break
    }
}

if (-not $selected) {
    $checked = $installationPaths | ForEach-Object {
        Join-Path $_ 'MSBuild\Microsoft\DesktopBridge\Microsoft.DesktopBridge.props'
    }

    throw "MSBuild was found, but Microsoft.DesktopBridge.props was not. Install the Visual Studio MSIX packaging/DesktopBridge tooling. Checked:`n$($checked -join "`n")"
}

if (-not (Test-Path -LiteralPath $Solution)) {
    throw "Solution file was not found: $Solution"
}

$arguments = @(
    $Solution,
    "/p:Configuration=$Configuration",
    "/p:Platform=$Platform"
) + $MSBuildArgs

Write-Host "Using MSBuild: $($selected.MSBuild)"
Write-Host "Using DesktopBridge props: $($selected.DesktopBridgeProps)"

& $selected.MSBuild @arguments
exit $LASTEXITCODE
