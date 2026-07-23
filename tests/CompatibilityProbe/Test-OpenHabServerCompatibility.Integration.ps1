[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent | Split-Path -Parent
$probe = Join-Path $repoRoot 'scripts\Test-OpenHabServerCompatibility.ps1'
$server = Join-Path $PSScriptRoot 'FakeOpenHabCompatibilityServer.ps1'
$port = 18991
$report = Join-Path ([IO.Path]::GetTempPath()) 'openhab-compatibility-probe-integration.json'

function Start-FakeServer([switch]$FailRestore) {
    $arguments = @('-NoProfile', '-File', $server, '-Port', $port)
    if ($FailRestore) { $arguments += '-FailRestore' }
    $process = Start-Process -FilePath 'pwsh' -ArgumentList $arguments -PassThru -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        try {
            $client = [Net.Sockets.TcpClient]::new()
            $client.Connect('127.0.0.1', $port)
            $client.Dispose()
            return $process
        }
        catch { Start-Sleep -Milliseconds 100 }
    } while ([DateTime]::UtcNow -lt $deadline)
    $process.Dispose()
    throw 'Fake server did not start.'
}

function Stop-FakeServer($Process) {
    if ($Process -and -not $Process.HasExited) { Stop-Process -Id $Process.Id -Force }
    if ($Process) { $Process.Dispose() }
}

try {
    $fake = Start-FakeServer
    try {
        & $probe -BaseUri "http://127.0.0.1:$port/" -SitemapName compatibility -WritableItemName Compatibility_Switch -OutputPath $report -TimeoutSeconds 10
        if ($LASTEXITCODE -ne 0) { throw "Expected successful probe, got $LASTEXITCODE." }
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if ($result.failures.Count -ne 0 -or $result.items.restore -ne 'passed' -or $result.sitemap.widgetIdsObserved -ne 1) { throw 'Successful probe report did not contain expected redacted results.' }
        if ((Get-Content -LiteralPath $report -Raw) -match 'Compatibility_Switch|Authorization|OFF') { throw 'Report exposed private request or state data.' }
    }
    finally { Stop-FakeServer $fake }

    $fake = Start-FakeServer -FailRestore
    try {
        & $probe -BaseUri "http://127.0.0.1:$port/" -SitemapName compatibility -WritableItemName Compatibility_Switch -OutputPath $report -TimeoutSeconds 10
        if ($LASTEXITCODE -ne 1) { throw "Expected restoration failure exit 1, got $LASTEXITCODE." }
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if ($result.items.restore -ne 'failed' -or $result.failures -notcontains 'restore') { throw 'Restoration failure was not recorded.' }
    }
    finally { Stop-FakeServer $fake }
}
finally {
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }
}

exit 0
