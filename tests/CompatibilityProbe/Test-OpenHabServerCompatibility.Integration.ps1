[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent | Split-Path -Parent
$probe = Join-Path $repoRoot 'scripts\Test-OpenHabServerCompatibility.ps1'
$server = Join-Path $PSScriptRoot 'FakeOpenHabCompatibilityServer.ps1'
$stalledParser = Join-Path $PSScriptRoot 'StalledCompatibilityParser.ps1'
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

function Invoke-ProbeWithOutput([hashtable]$Options) {
    $allArguments = @{ BaseUri = "http://127.0.0.1:$port/"; SitemapName = 'compatibility'; OutputPath = $report }
    foreach ($name in $Options.Keys) { $allArguments[$name] = $Options[$name] }
    $output = (& pwsh -NoProfile -File $probe @allArguments 2>&1 | Out-String)
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

function Read-Report { Get-Content -LiteralPath $report -Raw | ConvertFrom-Json }

function Get-FakeItemState {
    $client = [Net.Http.HttpClient]::new()
    try {
        $response = $client.GetAsync("http://127.0.0.1:$port/rest/items/Compatibility_Switch").GetAwaiter().GetResult()
        try {
            if (-not $response.IsSuccessStatusCode) { throw 'Fake item state endpoint failed.' }
            return (($response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json -ErrorAction Stop).state)
        }
        finally { $response.Dispose() }
    }
    finally { $client.Dispose() }
}

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

    $fake = Start-FakeServer @('-UseLocationHeader')
    try {
        if ((Invoke-Probe @{ SkipWriteProbe = $true; TimeoutSeconds = 5 }) -ne 0) { throw 'Header-selected subscription route must succeed.' }
        if ((Read-Report).events.stream -ne 'passed') { throw 'Header-selected subscription route was not used.' }
    }
    finally { Stop-FakeServer $fake }

    foreach ($malformedLocationCase in @(@(), @('-UseLocationHeader'))) {
        $fake = Start-FakeServer ($malformedLocationCase + '-MalformedSubscriptionLocation')
        try {
            if ((Invoke-Probe @{ SkipWriteProbe = $true; TimeoutSeconds = 5 }) -ne 1) { throw 'Malformed subscription location must fail.' }
            if ((Read-Report).failures -notcontains 'event-subscription') { throw 'Malformed subscription location was not rejected at subscription validation.' }
        }
        finally { Stop-FakeServer $fake }
    }

    $fake = Start-FakeServer @('-EventOnlySseStall')
    try {
        $started = [DateTime]::UtcNow
        if ((Invoke-Probe @{ SkipWriteProbe = $true; TimeoutSeconds = 2 }) -ne 1) { throw 'Event-only SSE stream must fail.' }
        if (([DateTime]::UtcNow - $started).TotalSeconds -gt 9) { throw 'Event-only SSE stream was not bounded.' }
        if ((Read-Report).failures -notcontains 'event-stream') { throw 'Event-only SSE stream did not fail readiness validation.' }
    }
    finally { Stop-FakeServer $fake }

    $fake = Start-FakeServer @('-StallItemReadAfterWrite')
    try {
        $started = [DateTime]::UtcNow
        if ((Invoke-Probe @{ WritableItemName = 'Compatibility_Switch'; TimeoutSeconds = 2 }) -ne 1) { throw 'Stalled item read must fail.' }
        if (([DateTime]::UtcNow - $started).TotalSeconds -gt 9) { throw 'Stalled item read was not bounded.' }
        $failures = (Read-Report).failures
        if ($failures -notcontains 'item-write' -or $failures -notcontains 'restore') { throw 'Stalled poll and restore failures were not recorded.' }
    }
    finally { Stop-FakeServer $fake }

    $fake = Start-FakeServer
    try {
        $started = [DateTime]::UtcNow
        $probeResult = Invoke-ProbeWithOutput @{ WritableItemName = 'Compatibility_Switch'; TimeoutSeconds = 2; ParserToolPath = $stalledParser }
        if ($probeResult.ExitCode -ne 1) { throw 'Stalled validator must fail the probe.' }
        if (([DateTime]::UtcNow - $started).TotalSeconds -gt 9) { throw 'Stalled validator blocked restoration beyond the helper timeout.' }
        $result = Read-Report
        if ($result.failures -notcontains 'main-ui-pages' -or $result.items.write -ne 'passed' -or $result.items.restore -ne 'passed') { throw 'Stalled validator did not record failure and restoration.' }
        if ((Get-FakeItemState) -ne 'OFF') { throw 'Stalled validator did not restore the explicit writable item.' }
        if ($probeResult.Output -notmatch 'Production parser helper validation timed out or was canceled after 2 seconds and was stopped.') { throw 'Stalled validator did not return a redacted actionable timeout failure.' }
        if ($probeResult.Output -match 'Compatibility_Switch|OFF|homepage') { throw 'Stalled validator output exposed a private payload or item state.' }
    }
    finally { Stop-FakeServer $fake }
}
finally {
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }
}

exit 0
