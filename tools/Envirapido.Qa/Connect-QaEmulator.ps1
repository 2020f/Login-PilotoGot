param(
    [ValidateRange(1024, 65535)][int]$Port = 5019,
    [Parameter(Mandatory)][ValidatePattern('^PilotGoTest_[a-f0-9]{32}$')][string]$ExpectedDatabase,
    [ValidatePattern('^emulator-[0-9]+$')][string]$Device = 'emulator-5554'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# El APK QA usa siempre 127.0.0.1:5019. Antes de cambiar su túnel, comprobar
# que el host sirve la base preservada que realmente usa el panel.
Add-Type -AssemblyName System.Net.Http
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(10)
try {
    $response = $client.GetAsync("http://127.0.0.1:$Port/qa/environment").GetAwaiter().GetResult()
    try {
        if ([int]$response.StatusCode -ne 200) { throw 'El host no confirmó su entorno QA; el túnel no se modificó.' }
        $environment = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
    } finally { $response.Dispose() }
} finally { $client.Dispose() }
if ($environment.qa -isnot [bool] -or !$environment.qa -or
    $environment.database -cne $ExpectedDatabase -or [int]$environment.port -ne $Port -or
    $environment.mode -cne 'attached-preserved') {
    throw 'El puerto, la base o el modo QA no coinciden con el entorno preservado esperado; el túnel no se modificó.'
}

$adb = Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'
if (!(Test-Path -LiteralPath $adb -PathType Leaf)) { throw 'ADB no está disponible en el SDK Android local.' }
$state = (& $adb -s $Device get-state 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $state -cne 'device') { throw 'El emulador solicitado no está conectado; el túnel no se modificó.' }
$package = (& $adb -s $Device shell pm path com.pilotgo.app.qa 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $package -notmatch '(?m)^package:/.+\.apk\s*$') {
    throw 'Envirapido QA no está instalada en ese emulador; el túnel no se modificó.'
}

$reverseResult = & $adb -s $Device reverse tcp:5019 "tcp:$Port" 2>&1
if ($LASTEXITCODE -ne 0) { throw 'ADB no pudo establecer el túnel del emulador QA.' }
$mappings = @(& $adb -s $Device reverse --list 2>&1)
if ($LASTEXITCODE -ne 0) { throw 'No se pudo comprobar el túnel del emulador QA.' }
$verified = @($mappings | Where-Object {
    $parts = ([string]$_).Trim() -split '\s+'
    $parts.Count -eq 3 -and $parts[1] -ceq 'tcp:5019' -and $parts[2] -ceq "tcp:$Port"
}).Count -eq 1
if (!$verified) { throw 'El túnel leído por ADB no coincide con el entorno QA esperado.' }

[ordered]@{
    outcome = 'PASS'
    checkedAtUtc = [DateTime]::UtcNow.ToString('o')
    host = '127.0.0.1'
    port = $Port
    database = $ExpectedDatabase
    mode = $environment.mode
    device = $Device
    package = 'com.pilotgo.app.qa'
    reverse = [ordered]@{ devicePort = 'tcp:5019'; hostPort = "tcp:$Port"; verified = $verified }
} | ConvertTo-Json -Depth 4
