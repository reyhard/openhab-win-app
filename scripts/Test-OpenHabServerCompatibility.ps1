[CmdletBinding()]
param(
    [string]$BaseUri,
    [string]$SitemapName,
    [string]$WritableItemName,
    [string]$ApiToken,
    [string]$UserName,
    [string]$Password,
    [string]$OutputPath = ".\artifacts\openhab-server-compatibility.json",
    [string]$ExpectedVersionPrefix,
    [Parameter(DontShow = $true)]
    [string]$ParserToolPath,
    [ValidateRange(1, 300)]
    [int]$TimeoutSeconds = 20,
    [switch]$SkipWriteProbe,
    [switch]$AllowUntrustedCertificateForLocalTestOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Normalize-OpenHabBaseUri {
    [CmdletBinding()]
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw [System.ArgumentException]::new('BaseUri is required.')
    }

    $uri = $null
    if (-not [Uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -notin @('http', 'https')) {
        throw [System.ArgumentException]::new('BaseUri must be an absolute HTTP(S) URI.')
    }
    if (-not [string]::IsNullOrWhiteSpace($uri.UserInfo)) {
        throw [System.ArgumentException]::new('BaseUri must not contain user-info.')
    }

    $builder = [UriBuilder]::new($uri)
    $builder.Path = $builder.Path.TrimEnd('/') + '/'
    $builder.Query = ''
    $builder.Fragment = ''
    return $builder.Uri
}

function Get-RedactedUri {
    [CmdletBinding()]
    param([Parameter(Mandatory)][Uri]$Uri)

    $builder = [UriBuilder]::new($Uri)
    $builder.UserName = ''
    $builder.Password = ''
    $builder.Query = ''
    $builder.Fragment = ''
    return $builder.Uri.AbsoluteUri
}

function New-OpenHabAuthorizationHeader {
    [CmdletBinding()]
    param(
        [string]$Token,
        [string]$BasicUserName,
        [string]$BasicPassword
    )

    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        return [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Token)
    }
    if (-not [string]::IsNullOrWhiteSpace($BasicUserName)) {
        $encoded = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("$BasicUserName`:$BasicPassword"))
        return [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Basic', $encoded)
    }
    return $null
}

function Resolve-SubscriptionId {
    [CmdletBinding()]
    param(
        [string[]]$LocationHeader,
        [string]$ResponseBody,
        [switch]$LocationHeaderPresent
    )

    $locations = [Collections.Generic.List[string]]::new()
    foreach ($location in @($LocationHeader)) {
        if (-not [string]::IsNullOrWhiteSpace($location)) { $locations.Add($location) }
    }
    if (-not $LocationHeaderPresent -and -not [string]::IsNullOrWhiteSpace($ResponseBody)) {
        try {
            $body = $ResponseBody | ConvertFrom-Json -ErrorAction Stop
            $candidates = [Collections.Generic.List[object]]::new()
            if ($body.PSObject.Properties['context'] -and $body.context.PSObject.Properties['headers']) {
                foreach ($name in @('Location', 'location')) {
                    if ($body.context.headers.PSObject.Properties[$name]) { $candidates.Add($body.context.headers.$name) }
                }
            }
            foreach ($name in @('location', 'Location')) {
                if ($body.PSObject.Properties[$name]) { $candidates.Add($body.$name) }
            }
            foreach ($candidate in $candidates) {
                foreach ($value in @($candidate)) {
                    if ($value -is [string] -and -not [string]::IsNullOrWhiteSpace($value)) { $locations.Add($value) }
                }
            }
        }
        catch {
            if ($ResponseBody.TrimStart().StartsWith('/')) { $locations.Add($ResponseBody.Trim()) }
        }
    }

    foreach ($location in $locations) {
        $path = ($location -split '[?#]', 2)[0]
        $parsed = $null
        if ([Uri]::TryCreate($location, [UriKind]::Absolute, [ref]$parsed)) { $path = $parsed.AbsolutePath }
        $segments = @($path.Trim('/').Split('/', [StringSplitOptions]::RemoveEmptyEntries))
        $firstExpectedSegment = $segments.Count - 4
        if ($segments.Count -ge 4 -and
            [string]::Equals($segments[$firstExpectedSegment], 'rest', [StringComparison]::Ordinal) -and
            [string]::Equals($segments[$firstExpectedSegment + 1], 'sitemaps', [StringComparison]::Ordinal) -and
            [string]::Equals($segments[$firstExpectedSegment + 2], 'events', [StringComparison]::Ordinal)) {
            $candidate = [Uri]::UnescapeDataString($segments[$firstExpectedSegment + 3])
            if (-not [string]::IsNullOrWhiteSpace($candidate)) { return $candidate }
        }
    }
    return $null
}

function New-CompatibilityResult {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Endpoint, [Parameter(Mandatory)][string]$Authentication)

    return [ordered]@{
        timestampUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        endpoint = $Endpoint
        authentication = $Authentication
        serverVersion = $null
        sitemap = [ordered]@{ list = 'not-run'; homepage = 'not-run'; widgetIdsObserved = 0 }
        events = [ordered]@{ subscription = 'not-run'; stream = 'not-run'; matchingUpdateObserved = $false }
        items = [ordered]@{ read = 'not-run'; write = 'not-run'; restore = 'not-run' }
        mainUiPages = 'not-run'
        failures = [Collections.Generic.List[string]]::new()
    }
}

function Add-CompatibilityFailure {
    param([Parameter(Mandatory)]$Result, [Parameter(Mandatory)][string]$Code)
    if (-not $Result.failures.Contains($Code)) { $Result.failures.Add($Code) }
}

function New-OpenHabUri {
    param([Parameter(Mandatory)][Uri]$Base, [Parameter(Mandatory)][string]$Relative)
    return [Uri]::new($Base, $Relative.TrimStart('/'))
}

function Invoke-OpenHabRequest {
    param(
        [Parameter(Mandatory)][System.Net.Http.HttpClient]$Client,
        [Parameter(Mandatory)][Uri]$Uri,
        [Parameter(Mandatory)][System.Net.Http.HttpMethod]$Method,
        [System.Net.Http.Headers.AuthenticationHeaderValue]$Authorization,
        [string]$Accept = 'application/json',
        [string]$Body,
        [switch]$ResponseHeadersRead,
        [Parameter(Mandatory)][Threading.CancellationToken]$CancellationToken
    )

    $request = [System.Net.Http.HttpRequestMessage]::new($Method, $Uri)
    try {
        if ($Authorization) { $request.Headers.Authorization = $Authorization }
        if ($Accept) { [void]$request.Headers.Accept.ParseAdd($Accept) }
        if ($PSBoundParameters.ContainsKey('Body')) {
            $request.Content = [System.Net.Http.StringContent]::new($Body, [Text.Encoding]::UTF8, 'text/plain')
        }
        $completion = if ($ResponseHeadersRead) { [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead } else { [System.Net.Http.HttpCompletionOption]::ResponseContentRead }
        $response = $Client.SendAsync($request, $completion, $CancellationToken).GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) {
            $status = [int]$response.StatusCode
            $response.Dispose()
            throw "HTTP $status"
        }
        if ($ResponseHeadersRead) { return $response }

        $headers = @{}
        foreach ($header in $response.Headers) { $headers[$header.Key] = @($header.Value) }
        $contentType = if ($response.Content.Headers.ContentType) { $response.Content.Headers.ContentType.MediaType } else { $null }
        $responseBody = $response.Content.ReadAsStringAsync($CancellationToken).GetAwaiter().GetResult()
        $statusCode = [int]$response.StatusCode
        $response.Dispose()
        return [pscustomobject]@{ StatusCode = $statusCode; Headers = $headers; ContentType = $contentType; Body = $responseBody }
    }
    finally { $request.Dispose() }
}

function Stop-HelperProcess {
    param($Process)

    if ($null -eq $Process -or $Process.HasExited) { return }
    try { $Process.Kill($true) }
    catch { }
    try { [void]$Process.WaitForExit(2000) }
    catch { }
}

function Invoke-HelperProcess {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string[]]$CommandArguments,
        [string]$InputPayload,
        [switch]$WriteInput,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][Threading.CancellationToken]$CancellationToken,
        [Parameter(Mandatory)][string]$Description
    )

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $WriteInput
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $CommandArguments) { [void]$startInfo.ArgumentList.Add([string]$argument) }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $helperCancellation = [Threading.CancellationTokenSource]::CreateLinkedTokenSource($CancellationToken)
    $stdoutTask = $null
    $stderrTask = $null
    try {
        $helperCancellation.CancelAfter([TimeSpan]::FromSeconds($TimeoutSeconds))
        if (-not $process.Start()) { throw "$Description could not be started." }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if ($WriteInput) {
            try {
                [void]$process.StandardInput.WriteAsync($InputPayload).WaitAsync($helperCancellation.Token).GetAwaiter().GetResult()
            }
            finally {
                $process.StandardInput.Close()
            }
        }

        [void]$process.WaitForExitAsync($helperCancellation.Token).GetAwaiter().GetResult()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        [void]$stderrTask.GetAwaiter().GetResult()
        return [pscustomobject]@{ ExitCode = $process.ExitCode; StandardOutput = $stdout }
    }
    catch [OperationCanceledException] {
        Stop-HelperProcess $process
        throw [TimeoutException]::new("$Description timed out or was canceled after $TimeoutSeconds seconds and was stopped.")
    }
    finally {
        if ($null -ne $stdoutTask -or $null -ne $stderrTask) {
            Stop-HelperProcess $process
        }
        $process.Dispose()
        $helperCancellation.Dispose()
    }
}

function New-ProductionPayloadParserTool {
    param([Parameter(Mandatory)][string]$ToolPath)

    $extension = [IO.Path]::GetExtension($ToolPath)
    if ($extension -eq '.ps1') {
        return [pscustomobject]@{ FileName = 'pwsh'; PrefixArguments = @('-NoProfile', '-File', $ToolPath) }
    }
    if ($extension -eq '.dll') {
        return [pscustomobject]@{ FileName = 'dotnet'; PrefixArguments = @($ToolPath) }
    }
    throw 'ParserToolPath must point to a .dll or .ps1 helper.'
}

function Initialize-ProductionPayloadParser {
    param([Parameter(Mandatory)][int]$TimeoutSeconds)

    if (-not [string]::IsNullOrWhiteSpace($ParserToolPath)) {
        if (-not (Test-Path -LiteralPath $ParserToolPath -PathType Leaf)) { throw 'Configured production parser helper is missing.' }
        return New-ProductionPayloadParserTool -ToolPath ([string](Resolve-Path -LiteralPath $ParserToolPath))
    }

    $project = Join-Path $PSScriptRoot '..\tools\OpenHab.CompatibilityProbe\OpenHab.CompatibilityProbe.csproj'
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw 'Production parser helper source is missing.' }
    $resolvedProject = Resolve-Path $project
    $assets = Join-Path (Split-Path $resolvedProject -Parent) 'obj\project.assets.json'
    $commands = [Collections.Generic.List[object[]]]::new()
    if (-not (Test-Path -LiteralPath $assets -PathType Leaf)) {
        # A fresh checkout must restore explicitly. Existing assets avoid unnecessary NuGet metadata access.
        $commands.Add(@('restore', $resolvedProject))
    }
    $commands.Add(@('build', $resolvedProject, '--no-restore'))

    foreach ($arguments in $commands) {
        $helperResult = Invoke-HelperProcess -FileName 'dotnet' -CommandArguments $arguments -TimeoutSeconds $TimeoutSeconds -CancellationToken ([Threading.CancellationToken]::None) -Description 'Production parser helper build'
        if ($helperResult.ExitCode -ne 0) {
            if ($arguments[0] -eq 'restore') { throw 'Production parser helper restore failed; make NuGet restore available and retry.' }
            throw 'Production parser helper build failed using current sources; run dotnet restore tools/OpenHab.CompatibilityProbe and retry.'
        }
    }
    $toolPath = Join-Path $PSScriptRoot '..\tools\OpenHab.CompatibilityProbe\bin\Debug\net10.0-windows10.0.19041.0\OpenHab.CompatibilityProbe.dll'
    if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) { throw 'Production parser helper build did not produce the expected executable.' }
    return New-ProductionPayloadParserTool -ToolPath ([string](Resolve-Path $toolPath))
}

function Invoke-ProductionPayloadParser {
    param(
        [Parameter(Mandatory)][ValidateSet('sitemap', 'main-ui-pages')][string]$Mode,
        [Parameter(Mandatory)][string]$Payload,
        [Parameter(Mandatory)]$ParserTool,
        [Parameter(Mandatory)][int]$TimeoutSeconds,
        [Parameter(Mandatory)][Threading.CancellationToken]$CancellationToken
    )

    $arguments = [Collections.Generic.List[string]]::new()
    foreach ($argument in $ParserTool.PrefixArguments) { $arguments.Add([string]$argument) }
    $arguments.Add($Mode)
    $helperResult = Invoke-HelperProcess -FileName $ParserTool.FileName -CommandArguments $arguments.ToArray() -InputPayload $Payload -WriteInput -TimeoutSeconds $TimeoutSeconds -CancellationToken $CancellationToken -Description 'Production parser helper validation'
    if ($helperResult.ExitCode -ne 0) { throw 'Production parser helper rejected the response.' }
    return $helperResult.StandardOutput | ConvertFrom-Json -ErrorAction Stop
}

function Read-SseFrame {
    param([Parameter(Mandatory)]$Session, [Parameter(Mandatory)][int]$TimeoutSeconds)
    $readCancellation = [Threading.CancellationTokenSource]::CreateLinkedTokenSource($Session.Cancellation.Token)
    try {
        $readCancellation.CancelAfter([TimeSpan]::FromSeconds($TimeoutSeconds))
        while ($true) {
            try { $line = $Session.Reader.ReadLineAsync($readCancellation.Token).AsTask().GetAwaiter().GetResult() }
            catch [OperationCanceledException] { return $null }
            if ($null -eq $line) { return $null }
            if ($line.StartsWith(':') -or $line.StartsWith('data:')) { return $line }
        }
    }
    finally { $readCancellation.Dispose() }
}

function Close-SseSession {
    param($Session)
    if ($null -eq $Session) { return }
    $Session.Cancellation.Cancel()
    $Session.Reader.Dispose()
    $Session.Response.Dispose()
    $Session.Cancellation.Dispose()
}

function Test-IsLoopbackEndpoint {
    param([Parameter(Mandatory)][Uri]$Uri)
    return $Uri.IsLoopback -or $Uri.DnsSafeHost -in @('localhost', '127.0.0.1', '::1')
}

function Wait-ForItemState {
    param($Client, [Uri]$Base, $Authorization, [string]$ItemName, [string]$ExpectedState, [int]$TimeoutSeconds, [Parameter(Mandatory)][Threading.CancellationToken]$CancellationToken)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $CancellationToken.ThrowIfCancellationRequested()
        $remaining = $deadline - [DateTime]::UtcNow
        if ($remaining -le [TimeSpan]::Zero) { return $false }
        $requestCancellation = [Threading.CancellationTokenSource]::CreateLinkedTokenSource($CancellationToken)
        try {
            $requestCancellation.CancelAfter($remaining)
            $response = Invoke-OpenHabRequest -Client $Client -Uri (New-OpenHabUri $Base "rest/items/$([Uri]::EscapeDataString($ItemName))") -Method ([System.Net.Http.HttpMethod]::Get) -Authorization $Authorization -CancellationToken $requestCancellation.Token
        }
        finally { $requestCancellation.Dispose() }
        $item = $response.Body | ConvertFrom-Json -ErrorAction Stop
        if ($item.state -eq $ExpectedState) { return $true }
        if ($CancellationToken.WaitHandle.WaitOne(250)) { $CancellationToken.ThrowIfCancellationRequested() }
    } while ([DateTime]::UtcNow -lt $deadline)
    return $false
}

function Test-CompatibilityInvocation {
    if (-not [string]::IsNullOrWhiteSpace($ApiToken) -and (-not [string]::IsNullOrWhiteSpace($UserName) -or -not [string]::IsNullOrWhiteSpace($Password))) { throw 'Choose token or Basic authentication, not both.' }
    if ([string]::IsNullOrWhiteSpace($UserName) -xor [string]::IsNullOrWhiteSpace($Password)) { throw 'Basic authentication requires both UserName and Password.' }
    if ([string]::IsNullOrWhiteSpace($SitemapName)) { throw 'SitemapName is required.' }
    if (-not $SkipWriteProbe -and [string]::IsNullOrWhiteSpace($WritableItemName)) { throw 'WritableItemName is required unless SkipWriteProbe is set.' }
    $normalized = Normalize-OpenHabBaseUri $BaseUri
    if (Test-Path -LiteralPath $OutputPath -PathType Container) { throw 'OutputPath must name a file, not a directory.' }
    if ($AllowUntrustedCertificateForLocalTestOnly -and -not (Test-IsLoopbackEndpoint $normalized)) { throw 'Untrusted certificates are permitted only for a loopback local test endpoint.' }
    return $normalized
}

$result = $null
$client = $null
$sseSession = $null
$outputFile = $null
$restoreRequired = $false
$originalState = $null
$exitCode = 1
$currentStep = 'probe'

try {
    try { $normalizedBaseUri = Test-CompatibilityInvocation }
    catch {
        [Console]::Error.WriteLine("Invalid invocation: $($_.Exception.Message)")
        exit 2
    }

    $outputFile = [IO.Path]::GetFullPath($OutputPath)
    $parentDirectory = [IO.Path]::GetDirectoryName($outputFile)
    if (-not [string]::IsNullOrWhiteSpace($parentDirectory)) { [IO.Directory]::CreateDirectory($parentDirectory) | Out-Null }

    $authentication = if (-not [string]::IsNullOrWhiteSpace($ApiToken)) { 'Bearer' } elseif (-not [string]::IsNullOrWhiteSpace($UserName)) { 'Basic' } else { 'None' }
    $result = New-CompatibilityResult -Endpoint (Get-RedactedUri $normalizedBaseUri) -Authentication $authentication
    $currentStep = 'helper'
    $parserTool = Initialize-ProductionPayloadParser -TimeoutSeconds $TimeoutSeconds
    $authorization = New-OpenHabAuthorizationHeader -Token $ApiToken -BasicUserName $UserName -BasicPassword $Password
    $handler = [System.Net.Http.HttpClientHandler]::new()
    if ($AllowUntrustedCertificateForLocalTestOnly) {
        Write-Warning 'UNTRUSTED TLS ENABLED FOR THIS LOOPBACK TEST ONLY. Do not use this option for production or myopenHAB.'
        $handler.ServerCertificateCustomValidationCallback = { param($request, $certificate, $chain, $errors) return $true }
    }
    $client = [System.Net.Http.HttpClient]::new($handler, $true)
    $client.Timeout = [Threading.Timeout]::InfiniteTimeSpan
    $timeoutCancellation = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))
    try {
        if (-not [string]::IsNullOrWhiteSpace($ExpectedVersionPrefix)) {
            $currentStep = 'version-unavailable'
            $systemInfo = Invoke-OpenHabRequest -Client $client -Uri (New-OpenHabUri $normalizedBaseUri 'rest/systeminfo') -Method ([System.Net.Http.HttpMethod]::Get) -Authorization $authorization -CancellationToken $timeoutCancellation.Token
            $systemInfoDocument = [Text.Json.JsonDocument]::Parse($systemInfo.Body)
            try {
                $versionElement = [Text.Json.JsonElement]::new()
                if ($systemInfoDocument.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Object -or -not $systemInfoDocument.RootElement.TryGetProperty('version', [ref]$versionElement) -or $versionElement.ValueKind -ne [Text.Json.JsonValueKind]::String -or [string]::IsNullOrWhiteSpace($versionElement.GetString())) { throw 'Version unavailable.' }
                $serverVersion = $versionElement.GetString()
            }
            finally { $systemInfoDocument.Dispose() }
            $result.serverVersion = $serverVersion
            if (-not $serverVersion.StartsWith($ExpectedVersionPrefix, [StringComparison]::OrdinalIgnoreCase)) { $currentStep = 'version-mismatch'; throw 'Version prefix mismatch.' }
        }

        $currentStep = 'sitemap-list'
        $listResponse = Invoke-OpenHabRequest -Client $client -Uri (New-OpenHabUri $normalizedBaseUri 'rest/sitemaps') -Method ([System.Net.Http.HttpMethod]::Get) -Authorization $authorization -CancellationToken $timeoutCancellation.Token
        $listDocument = [Text.Json.JsonDocument]::Parse($listResponse.Body)
        try {
            if ($listDocument.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Array) { throw 'Sitemap list response must be a JSON array.' }
        }
        finally { $listDocument.Dispose() }
        $sitemaps = @($listResponse.Body | ConvertFrom-Json -ErrorAction Stop)
        if (-not @($sitemaps | Where-Object { $_.name -eq $SitemapName })) { throw 'Configured sitemap was not found.' }
        $result.sitemap.list = 'passed'

        $currentStep = 'sitemap-homepage'
        $sitemapResponse = Invoke-OpenHabRequest -Client $client -Uri (New-OpenHabUri $normalizedBaseUri "rest/sitemaps/$([Uri]::EscapeDataString($SitemapName))") -Method ([System.Net.Http.HttpMethod]::Get) -Authorization $authorization -CancellationToken $timeoutCancellation.Token
        $sitemapSummary = Invoke-ProductionPayloadParser -Mode sitemap -Payload $sitemapResponse.Body -ParserTool $parserTool -TimeoutSeconds $TimeoutSeconds -CancellationToken $timeoutCancellation.Token
        if ($sitemapSummary.WidgetCount -lt 1 -or $sitemapSummary.WidgetIdsObserved -lt 1) { throw 'Sitemap did not expose widgets with server widget IDs.' }
        $result.sitemap.homepage = 'passed'
        $result.sitemap.widgetIdsObserved = [int]$sitemapSummary.WidgetIdsObserved

        $currentStep = 'event-subscription'
        $subscriptionResponse = Invoke-OpenHabRequest -Client $client -Uri (New-OpenHabUri $normalizedBaseUri 'rest/sitemaps/events/subscribe') -Method ([System.Net.Http.HttpMethod]::Post) -Authorization $authorization -CancellationToken $timeoutCancellation.Token
        $locationHeaderPresent = $subscriptionResponse.Headers.ContainsKey('Location')
        $location = if ($locationHeaderPresent) { [string[]]$subscriptionResponse.Headers['Location'] } else { @() }
        $subscriptionId = Resolve-SubscriptionId -LocationHeader $location -ResponseBody $subscriptionResponse.Body -LocationHeaderPresent:$locationHeaderPresent
        if ([string]::IsNullOrWhiteSpace($subscriptionId)) { throw 'Subscription did not provide a location.' }
        $result.events.subscription = 'passed'

        $currentStep = 'event-stream'
        $pageId = $SitemapName
        $sseRelative = "rest/sitemaps/events/$([Uri]::EscapeDataString($subscriptionId))?sitemap=$([Uri]::EscapeDataString($SitemapName))&pageid=$([Uri]::EscapeDataString($pageId))"
        $sseCancellation = [Threading.CancellationTokenSource]::CreateLinkedTokenSource($timeoutCancellation.Token)
        $sseResponse = Invoke-OpenHabRequest -Client $client -Uri (New-OpenHabUri $normalizedBaseUri $sseRelative) -Method ([System.Net.Http.HttpMethod]::Get) -Authorization $authorization -Accept 'text/event-stream' -ResponseHeadersRead -CancellationToken $sseCancellation.Token
        if ($null -eq $sseResponse.Content.Headers.ContentType -or $sseResponse.Content.Headers.ContentType.MediaType -notlike 'text/event-stream*') { $sseResponse.Dispose(); $sseCancellation.Dispose(); throw 'SSE response content type was not event-stream.' }
        $sseSession = [pscustomobject]@{ Response = $sseResponse; Reader = [IO.StreamReader]::new($sseResponse.Content.ReadAsStream($sseCancellation.Token)); Cancellation = $sseCancellation }
        if ($null -eq (Read-SseFrame -Session $sseSession -TimeoutSeconds $TimeoutSeconds)) { throw 'SSE did not produce a heartbeat or event frame before timeout.' }
        $result.events.stream = 'passed'

        if ($SkipWriteProbe) {
            $result.items.read = 'skipped'; $result.items.write = 'skipped'; $result.items.restore = 'not-required'
        }
        else {
            $currentStep = 'item-read'
            $itemUri = New-OpenHabUri $normalizedBaseUri "rest/items/$([Uri]::EscapeDataString($WritableItemName))"
            $itemResponse = Invoke-OpenHabRequest -Client $client -Uri $itemUri -Method ([System.Net.Http.HttpMethod]::Get) -Authorization $authorization -CancellationToken $timeoutCancellation.Token
            $item = $itemResponse.Body | ConvertFrom-Json -ErrorAction Stop
            $originalState = [string]$item.state
            if ($originalState -notin @('ON', 'OFF')) { throw 'Writable item is not safely reversible as an OnOff item.' }
            $result.items.read = 'passed'
            $targetState = if ($originalState -eq 'ON') { 'OFF' } else { 'ON' }
            $restoreRequired = $true
            $currentStep = 'item-write'
            [void](Invoke-OpenHabRequest -Client $client -Uri $itemUri -Method ([System.Net.Http.HttpMethod]::Post) -Authorization $authorization -Body $targetState -Accept 'text/plain' -CancellationToken $timeoutCancellation.Token)
            if (-not (Wait-ForItemState -Client $client -Base $normalizedBaseUri -Authorization $authorization -ItemName $WritableItemName -ExpectedState $targetState -TimeoutSeconds $TimeoutSeconds -CancellationToken $timeoutCancellation.Token)) { throw 'Writable item did not reach the requested state before timeout.' }
            $result.items.write = 'passed'
            $frame = Read-SseFrame -Session $sseSession -TimeoutSeconds 2
            if ($null -ne $frame -and $frame.Contains($WritableItemName)) { $result.events.matchingUpdateObserved = $true }
        }

        $currentStep = 'main-ui-pages'
        $mainUiResponse = Invoke-OpenHabRequest -Client $client -Uri (New-OpenHabUri $normalizedBaseUri 'rest/ui/components/ui:page') -Method ([System.Net.Http.HttpMethod]::Get) -Authorization $authorization -CancellationToken $timeoutCancellation.Token
        [void](Invoke-ProductionPayloadParser -Mode main-ui-pages -Payload $mainUiResponse.Body -ParserTool $parserTool -TimeoutSeconds $TimeoutSeconds -CancellationToken $timeoutCancellation.Token)
        $result.mainUiPages = 'passed'
    }
    finally { $timeoutCancellation.Dispose() }
}
catch {
    if ($result) { Add-CompatibilityFailure -Result $result -Code $currentStep }
    if ($_.Exception -is [TimeoutException]) {
        [Console]::Error.WriteLine("Compatibility probe failed: $($_.Exception.Message)")
    }
    elseif ($_.Exception -is [OperationCanceledException]) {
        [Console]::Error.WriteLine("Compatibility probe timed out or was canceled during $currentStep. See the redacted report for completed checks.")
    }
    else {
        [Console]::Error.WriteLine('Compatibility probe failed. See the redacted report for completed checks.')
    }
}
finally {
    if ($restoreRequired -and $client -and $originalState) {
        try {
            $restoreCancellation = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))
            try {
                $restoreUri = New-OpenHabUri $normalizedBaseUri "rest/items/$([Uri]::EscapeDataString($WritableItemName))"
                [void](Invoke-OpenHabRequest -Client $client -Uri $restoreUri -Method ([System.Net.Http.HttpMethod]::Post) -Authorization $authorization -Body $originalState -Accept 'text/plain' -CancellationToken $restoreCancellation.Token)
                if (-not (Wait-ForItemState -Client $client -Base $normalizedBaseUri -Authorization $authorization -ItemName $WritableItemName -ExpectedState $originalState -TimeoutSeconds $TimeoutSeconds -CancellationToken $restoreCancellation.Token)) { throw 'Restore timeout.' }
                $result.items.restore = 'passed'
            }
            finally { $restoreCancellation.Dispose() }
        }
        catch {
            $result.items.restore = 'failed'
            Add-CompatibilityFailure -Result $result -Code 'restore'
            Write-Warning 'RESTORATION FAILED. The explicit test item may not have been returned to its original state.'
        }
    }
    elseif ($result -and $result.items.restore -eq 'not-run') { $result.items.restore = 'not-required' }

    Close-SseSession $sseSession
    if ($client) { $client.Dispose() }
    if ($result -and $outputFile) {
        $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $outputFile -Encoding utf8NoBOM
        if ($result.failures.Count -eq 0) { $exitCode = 0 }
    }
}

exit $exitCode
