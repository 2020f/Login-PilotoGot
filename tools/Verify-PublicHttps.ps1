param(
    [uri]$BaseUrl = 'https://envirapido.com',
    [string]$ReceiptPath,
    [switch]$RequireSuccess
)
$ErrorActionPreference = 'Stop'
if ($BaseUrl.Scheme -ne 'https' -or $BaseUrl.UserInfo -or $BaseUrl.Query -or $BaseUrl.Fragment) {
    throw 'La comprobación requiere un origen HTTPS sin credenciales, consulta ni fragmento.'
}
$origin = $BaseUrl.GetLeftPart([UriPartial]::Authority)
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [timespan]::FromSeconds(25)
try {
    $root = $client.GetAsync($origin + '/').GetAwaiter().GetResult()
    $location = $root.Headers.Location
    $resolved = if ($location) { [uri]::new([uri]($origin + '/'), $location) } else { $null }
    $login = $client.GetAsync($origin + '/Identity/Account/Login').GetAwaiter().GetResult()
    $cookies = @()
    if ($login.Headers.Contains('Set-Cookie')) {
        $cookies = @($login.Headers.GetValues('Set-Cookie') | ForEach-Object {
            $parts = $_ -split ';'
            [pscustomobject]@{
                name = ($parts[0] -split '=', 2)[0]
                secure = [bool]($parts -match '^\s*secure\s*$')
                httpOnly = [bool]($parts -match '^\s*httponly\s*$')
            }
        })
    }
    $api = @('/api/auth/me', '/api/piloto/capacidades', '/api/tienda/capacidades') | ForEach-Object {
        $response = $client.GetAsync($origin + $_).GetAwaiter().GetResult()
        [pscustomobject]@{
            path = $_; status = [int]$response.StatusCode
            json = $response.Content.Headers.ContentType.MediaType -eq 'application/json'
            redirected = $null -ne $response.Headers.Location
        }
        $response.Dispose()
    }
    $antiforgery = @($cookies | Where-Object name -like '*Antiforgery*')
    $checks = [ordered]@{
        rootRedirectRemainsHttps = [int]$root.StatusCode -eq 302 -and $resolved -and
            $resolved.Scheme -eq 'https' -and $resolved.Authority -eq $BaseUrl.Authority
        loginAvailable = [int]$login.StatusCode -eq 200
        antiforgerySecureHttpOnly = $antiforgery.Count -gt 0 -and
            @($antiforgery | Where-Object { !$_.secure -or !$_.httpOnly }).Count -eq 0
        allNewCookiesSecureHttpOnly = $cookies.Count -gt 0 -and
            @($cookies | Where-Object { !$_.secure -or !$_.httpOnly }).Count -eq 0
        anonymousApi401Json = @($api | Where-Object { $_.status -ne 401 -or !$_.json -or $_.redirected }).Count -eq 0
    }
    $result = [ordered]@{
        checkedUtc = [DateTimeOffset]::UtcNow.ToString('o'); origin = $origin
        passed = @($checks.Values | Where-Object { !$_ }).Count -eq 0
        checks = $checks; rootStatus = [int]$root.StatusCode
        rootLocation = [string]$location; loginStatus = [int]$login.StatusCode
        cookies = $cookies; anonymousApi = @($api)
        scope = 'Anónimo: no acredita cookie autenticada, despliegue exacto, SQL ni flujo móvil.'
    }
    $json = $result | ConvertTo-Json -Depth 6
    if ($ReceiptPath) { $json | Set-Content -LiteralPath $ReceiptPath -Encoding utf8 }
    $json
    $root.Dispose()
    $login.Dispose()
    if ($RequireSuccess -and !$result.passed) { throw 'La validación HTTPS pública todavía no cumple todos los controles.' }
}
finally {
    $client.Dispose()
    $handler.Dispose()
}
