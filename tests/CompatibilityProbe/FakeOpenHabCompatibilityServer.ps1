param(
    [Parameter(Mandatory)][int]$Port,
    [switch]$FailRestore,
    [switch]$SitemapListObject,
    [switch]$OmitVersion,
    [string]$Version = '5.2.0',
    [switch]$UseLocationHeader,
    [switch]$MalformedSubscriptionLocation,
    [switch]$ProxyPath,
    [switch]$RequireFakeBearer,
    [switch]$RequireFakeBasic,
    [switch]$StallItemReadAfterWrite,
    [switch]$EventOnlySseStall
)

Set-StrictMode -Version Latest
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
$listener.Start()
$state = if ($FailRestore) { 'ON' } else { 'OFF' }
$writeObserved = $false
$routePrefix = if ($ProxyPath) { '/openhab' } else { '' }

function Send-Response($Writer, [int]$StatusCode, [string]$ContentType = $null, [string]$Body = '', [hashtable]$AdditionalHeaders = @{}) {
    $reason = if ($StatusCode -eq 200) { 'OK' } elseif ($StatusCode -eq 204) { 'No Content' } elseif ($StatusCode -eq 401) { 'Unauthorized' } elseif ($StatusCode -eq 404) { 'Not Found' } else { 'Internal Server Error' }
    $bytes = [Text.Encoding]::UTF8.GetBytes($Body)
    $headers = "HTTP/1.1 $StatusCode $reason`r`nConnection: close`r`nContent-Length: $($bytes.Length)`r`n"
    if ($ContentType) { $headers += "Content-Type: $ContentType`r`n" }
    foreach ($name in $AdditionalHeaders.Keys) { $headers += "${name}: $($AdditionalHeaders[$name])`r`n" }
    $headerBytes = [Text.Encoding]::ASCII.GetBytes("$headers`r`n")
    $Writer.Write($headerBytes, 0, $headerBytes.Length)
    if ($bytes.Length -gt 0) { $Writer.Write($bytes, 0, $bytes.Length) }
    $Writer.Flush()
}

function Send-EventOnlyFrameThenStall($Writer) {
    $headers = "HTTP/1.1 200 OK`r`nConnection: keep-alive`r`nContent-Type: text/event-stream`r`n`r`n"
    $headerBytes = [Text.Encoding]::ASCII.GetBytes($headers)
    $frameBytes = [Text.Encoding]::UTF8.GetBytes("event: message`n`n")
    $Writer.Write($headerBytes, 0, $headerBytes.Length)
    $Writer.Write($frameBytes, 0, $frameBytes.Length)
    $Writer.Flush()
    Start-Sleep -Seconds 30
}

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
            $requestLine = $reader.ReadLine()
            if ([string]::IsNullOrWhiteSpace($requestLine)) { continue }
            $parts = $requestLine.Split(' ')
            $method = $parts[0]
            $target = $parts[1]
            $headers = @{}
            while (($line = $reader.ReadLine()) -ne '') {
                $separator = $line.IndexOf(':')
                if ($separator -gt 0) { $headers[$line.Substring(0, $separator)] = $line.Substring($separator + 1).Trim() }
            }
            $expectedAuthorization = if ($RequireFakeBearer) { 'Bearer fake-token' } elseif ($RequireFakeBasic) { 'Basic ZmFrZS11c2VyOmZha2UtcGFzc3dvcmQ=' } else { $null }
            if ($expectedAuthorization -and $headers['Authorization'] -ne $expectedAuthorization) {
                Send-Response $stream 401
                continue
            }
            $body = ''
            if ($headers.ContainsKey('Content-Length') -and [int]$headers['Content-Length'] -gt 0) {
                $characters = New-Object char[] ([int]$headers['Content-Length'])
                [void]$reader.ReadBlock($characters, 0, $characters.Length)
                $body = -join $characters
            }
            $path = $target.Split('?')[0]
            if ($ProxyPath) {
                if (-not $path.StartsWith("$routePrefix/", [StringComparison]::Ordinal)) {
                    Send-Response $stream 404
                    continue
                }
                $path = $path.Substring($routePrefix.Length)
            }
            if ($method -eq 'GET' -and $path -eq '/rest/sitemaps') {
                if ($SitemapListObject) { Send-Response $stream 200 'application/json' '{"name":"compatibility","label":"Compatibility"}' }
                else { Send-Response $stream 200 'application/json' '[{"name":"compatibility","label":"Compatibility"}]' }
            }
            elseif ($method -eq 'GET' -and $path -eq '/rest/systeminfo') {
                if ($OmitVersion) { Send-Response $stream 200 'application/json' '{"locale":"en"}' }
                else { Send-Response $stream 200 'application/json' (([ordered]@{ version = $Version; locale = 'en' }) | ConvertTo-Json -Compress) }
            }
            elseif ($method -eq 'GET' -and $path -eq '/rest/sitemaps/compatibility') {
                Send-Response $stream 200 'application/json' '{"homepage":{"id":"compatibility","widgets":[{"type":"Switch","label":"Compatibility","widgetId":"2_000611","item":{"name":"Compatibility_Switch","state":"OFF"}}]}}'
            }
            elseif ($method -eq 'POST' -and $path -eq '/rest/sitemaps/events/subscribe') {
                $headerLocation = if ($MalformedSubscriptionLocation) { "${routePrefix}/wrong/sitemaps/events/header-subscription" } else { "${routePrefix}/rest/sitemaps/events/header-subscription" }
                $bodyLocation = if ($MalformedSubscriptionLocation -and -not $UseLocationHeader) { "${routePrefix}/wrong/sitemaps/events/probe-subscription" } else { "${routePrefix}/rest/sitemaps/events/probe-subscription" }
                $subscriptionHeaders = if ($UseLocationHeader) { @{ Location = $headerLocation } } else { @{} }
                $subscriptionBody = @{ context = @{ headers = @{ Location = @($bodyLocation) } } } | ConvertTo-Json -Compress
                Send-Response $stream 200 'application/json' $subscriptionBody $subscriptionHeaders
            }
            elseif ($method -eq 'GET' -and (($UseLocationHeader -and $path -eq '/rest/sitemaps/events/header-subscription') -or (-not $UseLocationHeader -and $path -eq '/rest/sitemaps/events/probe-subscription'))) {
                if ($EventOnlySseStall) { Send-EventOnlyFrameThenStall $stream }
                else { Send-Response $stream 200 'text/event-stream' ": heartbeat`n`n" }
            }
            elseif ($method -eq 'GET' -and $path -eq '/rest/items/Compatibility_Switch') {
                if ($StallItemReadAfterWrite -and $writeObserved) { Start-Sleep -Seconds 30 }
                Send-Response $stream 200 'application/json' (([ordered]@{ name = 'Compatibility_Switch'; type = 'Switch'; state = $state }) | ConvertTo-Json -Compress)
            }
            elseif ($method -eq 'POST' -and $path -eq '/rest/items/Compatibility_Switch') {
                if ($FailRestore -and $body.Trim() -eq 'ON') { Send-Response $stream 500 }
                else { $state = $body.Trim(); $writeObserved = $true; Send-Response $stream 204 }
            }
            elseif ($method -eq 'GET' -and $path -eq '/rest/ui/components/ui:page') { Send-Response $stream 200 'application/json' '[]' }
            else { Send-Response $stream 404 }
        }
        finally { $client.Dispose() }
    }
}
finally { $listener.Stop() }
