[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent | Split-Path -Parent
$probe = Join-Path $repoRoot 'scripts\Test-OpenHabServerCompatibility.ps1'
$server = Join-Path $PSScriptRoot 'FakeOpenHabCompatibilityServer.ps1'
$port = 18991
$report = Join-Path ([IO.Path]::GetTempPath()) 'openhab-compatibility-probe-integration.json'

function Start-FakeServer([string[]]$Options = @()) {
    $arguments = @('-NoProfile', '-File', $server, '-Port', $port) + $Options
    $process = Start-Process -FilePath 'pwsh' -ArgumentList $arguments -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        try {
            $client = [Net.Sockets.TcpClient]::new(); $client.Connect('127.0.0.1', $port); $client.Dispose(); return $process
        }
        catch { Start-Sleep -Milliseconds 100 }
    } while ([DateTime]::UtcNow -lt $deadline)
    $process.Dispose(); throw 'Fake server did not start.'
}

function Stop-FakeServer($Process) {
    if ($Process -and -not $Process.HasExited) { Stop-Process -Id $Process.Id -Force }
    if ($Process) { $Process.Dispose() }
}

function Invoke-Probe([hashtable]$Options) {
    $allArguments = @{ BaseUri = "http://127.0.0.1:$port/"; SitemapName = 'compatibility'; OutputPath = $report }
    foreach ($name in $Options.Keys) { $allArguments[$name] = $Options[$name] }
    & $probe @allArguments
    return $LASTEXITCODE
}

function Read-Report { Get-Content -LiteralPath $report -Raw | ConvertFrom-Json }

try {
    $fake = Start-FakeServer
    try {
        if ((Invoke-Probe @{ WritableItemName = 'Compatibility_Switch'; TimeoutSeconds = 10 }) -ne 0) { throw 'Expected successful probe.' }
        $result = Read-Report
        if ($result.failures.Count -ne 0 -or $result.items.restore -ne 'passed' -or $result.sitemap.widgetIdsObserved -ne 1) { throw 'Successful report did not contain expected results.' }
        if ((Get-Content $report -Raw) -match 'Compatibility_Switch|Authorization|OFF') { throw 'Report exposed private request or state data.' }
    }
    finally { Stop-FakeServer $fake }

    $fake = Start-FakeServer @('-FailRestore')
    try {
        if ((Invoke-Probe @{ WritableItemName = 'Compatibility_Switch'; TimeoutSeconds = 10 }) -ne 1) { throw 'Expected restoration failure exit 1.' }
        $result = Read-Report
        if ($result.items.restore -ne 'failed' -or $result.failures -notcontains 'restore') { throw 'Restoration failure was not recorded.' }
    }
    finally { Stop-FakeServer $fake }

    $fake = Start-FakeServer @('-SitemapListObject')
    try {
        if ((Invoke-Probe @{ SkipWriteProbe = $true; TimeoutSeconds = 5 }) -ne 1) { throw 'A sitemap-list object must fail.' }
        if ((Read-Report).failures -notcontains 'sitemap-list') { throw 'Sitemap-list object failure was not reported.' }
    }
    finally { Stop-FakeServer $fake }

    foreach ($versionCase in @(
        @{ Options = @('-Version', '5.2.0'); Expected = '5.2'; ExitCode = 0; Failure = $null },
        @{ Options = @('-Version', '5.2.0'); Expected = '5.3'; ExitCode = 1; Failure = 'version-mismatch' },
        @{ Options = @('-OmitVersion'); Expected = '5.2'; ExitCode = 1; Failure = 'version-unavailable' }
    )) {
        $fake = Start-FakeServer $versionCase.Options
        try {
            if ((Invoke-Probe @{ SkipWriteProbe = $true; ExpectedVersionPrefix = $versionCase.Expected; TimeoutSeconds = 5 }) -ne $versionCase.ExitCode) { throw 'Version preflight exit code was unexpected.' }
            if ($versionCase.Failure -and ((Read-Report).failures -notcontains $versionCase.Failure)) { throw 'Version preflight failure was not reported.' }
        }
        finally { Stop-FakeServer $fake }
    }

    foreach ($authCase in @(
        @{ Options = @('-UseLocationHeader', '-RequireFakeBearer'); Arguments = @{ SkipWriteProbe = $true; ApiToken = 'fake-token'; TimeoutSeconds = 5 } },
        @{ Options = @('-UseLocationHeader', '-RequireFakeBasic'); Arguments = @{ SkipWriteProbe = $true; UserName = 'fake-user'; Password = 'fake-password'; TimeoutSeconds = 5 } }
    )) {
        $fake = Start-FakeServer $authCase.Options
        try {
            if ((Invoke-Probe $authCase.Arguments) -ne 0) { throw 'Authenticated fake probe failed.' }
            if ((Get-Content $report -Raw) -match 'fake-token|fake-user|fake-password|ZmFrZS11c2VyOmZha2UtcGFzc3dvcmQ=') { throw 'Authentication value leaked into report.' }
        }
        finally { Stop-FakeServer $fake }
    }

    $fake = Start-FakeServer @('-StallItemReadAfterWrite')
    try {
        $started = [DateTime]::UtcNow
        if ((Invoke-Probe @{ WritableItemName = 'Compatibility_Switch'; TimeoutSeconds = 2 }) -ne 1) { throw 'Stalled item read must fail.' }
        if (([DateTime]::UtcNow - $started).TotalSeconds -gt 9) { throw 'Stalled item read was not bounded.' }
        $failures = (Read-Report).failures
        if ($failures -notcontains 'item-write' -or $failures -notcontains 'restore') { throw 'Stalled poll and restore failures were not recorded.' }
    }
    finally { Stop-FakeServer $fake }
}
finally {
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }
}

exit 0
