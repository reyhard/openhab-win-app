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
        [string]$ResponseBody
    )

    $locations = [Collections.Generic.List[string]]::new()
    foreach ($location in @($LocationHeader)) {
        if (-not [string]::IsNullOrWhiteSpace($location)) { $locations.Add($location) }
    }
    if ($locations.Count -eq 0 -and -not [string]::IsNullOrWhiteSpace($ResponseBody)) {
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
        $path = $location
        $parsed = $null
        if ([Uri]::TryCreate($location, [UriKind]::Absolute, [ref]$parsed)) { $path = $parsed.AbsolutePath }
        $segments = @($path.Trim('/').Split('/', [StringSplitOptions]::RemoveEmptyEntries))
        if ($segments.Count -gt 0) {
            $candidate = [Uri]::UnescapeDataString($segments[$segments.Count - 1])
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

function Invoke-ProductionPayloadParser {
    param([Parameter(Mandatory)][ValidateSet('sitemap', 'main-ui-pages')][string]$Mode, [Parameter(Mandatory)][string]$Payload)

    $project = Join-Path $PSScriptRoot '..\tools\OpenHab.CompatibilityProbe\OpenHab.CompatibilityProbe.csproj'
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $toolDll = @(
        (Join-Path $PSScriptRoot '..\tools\OpenHab.CompatibilityProbe\bin\Release\net10.0-windows10.0.19041.0\OpenHab.CompatibilityProbe.dll'),
        (Join-Path $PSScriptRoot '..\tools\OpenHab.CompatibilityProbe\bin\Debug\net10.0-windows10.0.19041.0\OpenHab.CompatibilityProbe.dll')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($toolDll) {
        [void]$startInfo.ArgumentList.Add((Resolve-Path $toolDll))
    }
    else {
        [void]$startInfo.ArgumentList.Add('run')
        [void]$startInfo.ArgumentList.Add('--no-restore')
        [void]$startInfo.ArgumentList.Add('--project')
        [void]$startInfo.ArgumentList.Add((Resolve-Path $project))
        [void]$startInfo.ArgumentList.Add('--')
    }
    [void]$startInfo.ArgumentList.Add($Mode)
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    [void]$process.Start()
    $process.StandardInput.Write($Payload)
    $process.StandardInput.Close()
    $output = $process.StandardOutput.ReadToEnd()
    [void]$process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'Production parser rejected the response.' }
    return $output | ConvertFrom-Json -ErrorAction Stop
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
            if ($line.StartsWith(':') -or $line.StartsWith('event:') -or $line.StartsWith('data:')) { return $line }
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
    param($Client, [Uri]$Base, $Authorization, [string]$ItemName, [string]$ExpectedState, [int]$TimeoutSeconds)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $response = Invoke-OpenHabRequest -Client $Client -Uri (New-OpenHabUri $Base "rest/items/$([Uri]::EscapeDataString($ItemName))") -Method ([System.Net.Http.HttpMethod]::Get) -Authorization $Authorization -CancellationToken ([Threading.CancellationToken]::None)
        $item = $response.Body | ConvertFrom-Json -ErrorAction Stop
        if ($item.state -eq $ExpectedState) { return $true }
        Start-Sleep -Milliseconds 250
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
        $currentStep = 'sitemap-list'
        $listResponse = Invoke-OpenHabRequest -Client $client -Uri (New-OpenHabUri $normalizedBaseUri 'rest/sitemaps') -Method ([System.Net.Http.HttpMethod]::Get) -Authorization $authorization -CancellationToken $timeoutCancellation.Token
        $sitemaps = @($listResponse.Body | ConvertFrom-Json -ErrorAction Stop)
        if (-not @($sitemaps | Where-Object { $_.name -eq $SitemapName })) { throw 'Configured sitemap was not found.' }
        $result.sitemap.list = 'passed'

        $currentStep = 'sitemap-homepage'
        $sitemapResponse = Invoke-OpenHabRequest -Client $client -Uri (New-OpenHabUri $normalizedBaseUri "rest/sitemaps/$([Uri]::EscapeDataString($SitemapName))") -Method ([System.Net.Http.HttpMethod]::Get) -Authorization $authorization -CancellationToken $timeoutCancellation.Token
        $sitemapSummary = Invoke-ProductionPayloadParser -Mode sitemap -Payload $sitemapResponse.Body
        if ($sitemapSummary.WidgetCount -lt 1 -or $sitemapSummary.WidgetIdsObserved -lt 1) { throw 'Sitemap did not expose widgets with server widget IDs.' }
        $result.sitemap.homepage = 'passed'
        $result.sitemap.widgetIdsObserved = [int]$sitemapSummary.WidgetIdsObserved

        $currentStep = 'event-subscription'
        $subscriptionResponse = Invoke-OpenHabRequest -Client $client -Uri (New-OpenHabUri $normalizedBaseUri 'rest/sitemaps/events/subscribe') -Method ([System.Net.Http.HttpMethod]::Post) -Authorization $authorization -CancellationToken $timeoutCancellation.Token
        $location = if ($subscriptionResponse.Headers.ContainsKey('Location')) { [string[]]$subscriptionResponse.Headers['Location'] } else { @() }
        $subscriptionId = Resolve-SubscriptionId -LocationHeader $location -ResponseBody $subscriptionResponse.Body
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
            if (-not (Wait-ForItemState -Client $client -Base $normalizedBaseUri -Authorization $authorization -ItemName $WritableItemName -ExpectedState $targetState -TimeoutSeconds $TimeoutSeconds)) { throw 'Writable item did not reach the requested state before timeout.' }
            $result.items.write = 'passed'
            $frame = Read-SseFrame -Session $sseSession -TimeoutSeconds 2
            if ($null -ne $frame -and $frame.Contains($WritableItemName)) { $result.events.matchingUpdateObserved = $true }
        }

        $currentStep = 'main-ui-pages'
        $mainUiResponse = Invoke-OpenHabRequest -Client $client -Uri (New-OpenHabUri $normalizedBaseUri 'rest/ui/components/ui:page') -Method ([System.Net.Http.HttpMethod]::Get) -Authorization $authorization -CancellationToken $timeoutCancellation.Token
        [void](Invoke-ProductionPayloadParser -Mode main-ui-pages -Payload $mainUiResponse.Body)
        $result.mainUiPages = 'passed'
    }
    finally { $timeoutCancellation.Dispose() }
}
catch {
    if ($result) { Add-CompatibilityFailure -Result $result -Code $currentStep }
    [Console]::Error.WriteLine('Compatibility probe failed. See the redacted report for completed checks.')
}
finally {
    if ($restoreRequired -and $client -and $originalState) {
        try {
            $restoreCancellation = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))
            try {
                $restoreUri = New-OpenHabUri $normalizedBaseUri "rest/items/$([Uri]::EscapeDataString($WritableItemName))"
                [void](Invoke-OpenHabRequest -Client $client -Uri $restoreUri -Method ([System.Net.Http.HttpMethod]::Post) -Authorization $authorization -Body $originalState -Accept 'text/plain' -CancellationToken $restoreCancellation.Token)
                if (-not (Wait-ForItemState -Client $client -Base $normalizedBaseUri -Authorization $authorization -ItemName $WritableItemName -ExpectedState $originalState -TimeoutSeconds $TimeoutSeconds)) { throw 'Restore timeout.' }
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
