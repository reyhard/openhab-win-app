param([Parameter(Mandatory)][ValidateSet('sitemap', 'main-ui-pages')][string]$Mode)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

[void][Console]::In.ReadToEnd()
if ($Mode -eq 'sitemap') {
    [Console]::Out.Write('{"WidgetCount":1,"WidgetIdsObserved":1}')
    exit 0
}

Start-Sleep -Seconds 30
exit 0
