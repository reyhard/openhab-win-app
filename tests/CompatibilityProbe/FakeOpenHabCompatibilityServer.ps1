param(
    [Parameter(Mandatory)][int]$Port,
    [switch]$FailRestore
)

Set-StrictMode -Version Latest
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
$listener.Start()
$state = if ($FailRestore) { 'ON' } else { 'OFF' }

function Send-Response($Writer, [int]$StatusCode, [string]$ContentType = $null, [string]$Body = '') {
    $reason = if ($StatusCode -eq 200) { 'OK' } elseif ($StatusCode -eq 204) { 'No Content' } elseif ($StatusCode -eq 404) { 'Not Found' } else { 'Internal Server Error' }
    $bytes = [Text.Encoding]::UTF8.GetBytes($Body)
    $headers = "HTTP/1.1 $StatusCode $reason`r`nConnection: close`r`nContent-Length: $($bytes.Length)`r`n"
    if ($ContentType) { $headers += "Content-Type: $ContentType`r`n" }
    $Writer.Write([Text.Encoding]::ASCII.GetBytes("$headers`r`n"), 0, $headers.Length + 2)
    if ($bytes.Length -gt 0) { $Writer.Write($bytes, 0, $bytes.Length) }
    $Writer.Flush()
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
            $body = ''
            if ($headers.ContainsKey('Content-Length') -and [int]$headers['Content-Length'] -gt 0) {
                $characters = New-Object char[] ([int]$headers['Content-Length'])
                [void]$reader.ReadBlock($characters, 0, $characters.Length)
                $body = -join $characters
            }
            $path = $target.Split('?')[0]
            if ($method -eq 'GET' -and $path -eq '/rest/sitemaps') {
                Send-Response $stream 200 'application/json' '[{"name":"compatibility","label":"Compatibility"}]'
            }
            elseif ($method -eq 'GET' -and $path -eq '/rest/sitemaps/compatibility') {
                Send-Response $stream 200 'application/json' '{"homepage":{"id":"compatibility","widgets":[{"type":"Switch","label":"Compatibility","widgetId":"2_000611","item":{"name":"Compatibility_Switch","state":"OFF"}}]}}'
            }
            elseif ($method -eq 'POST' -and $path -eq '/rest/sitemaps/events/subscribe') {
                Send-Response $stream 200 'application/json' '{"context":{"headers":{"Location":["/rest/sitemaps/events/probe-subscription"]}}}'
            }
            elseif ($method -eq 'GET' -and $path -eq '/rest/sitemaps/events/probe-subscription') {
                Send-Response $stream 200 'text/event-stream' ": heartbeat`n`n"
            }
            elseif ($method -eq 'GET' -and $path -eq '/rest/items/Compatibility_Switch') {
                Send-Response $stream 200 'application/json' (([ordered]@{ name = 'Compatibility_Switch'; type = 'Switch'; state = $state }) | ConvertTo-Json -Compress)
            }
            elseif ($method -eq 'POST' -and $path -eq '/rest/items/Compatibility_Switch') {
                if ($FailRestore -and $body.Trim() -eq 'ON') { Send-Response $stream 500 }
                else { $state = $body.Trim(); Send-Response $stream 204 }
            }
            elseif ($method -eq 'GET' -and $path -eq '/rest/ui/components/ui:page') {
                Send-Response $stream 200 'application/json' '[]'
            }
            else { Send-Response $stream 404 }
        }
        finally { $client.Dispose() }
    }
}
finally {
    $listener.Stop()
}
