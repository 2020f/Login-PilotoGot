param(
    [uri]$BaseUrl = 'https://envirapido.com',
    [string]$ReceiptPath,
    [switch]$RequireSuccess,
    [ValidateSet('CrmRedirect','Marketing')][string]$RootMode = 'CrmRedirect'
)
$ErrorActionPreference = 'Stop'
if ($BaseUrl.Scheme -ne 'https' -or $BaseUrl.UserInfo -or $BaseUrl.Query -or $BaseUrl.Fragment) {
    throw 'La comprobación requiere un origen HTTPS sin credenciales, consulta ni fragmento.'
}
$origin = $BaseUrl.GetLeftPart([UriPartial]::Authority)
$httpOriginBuilder = [UriBuilder]::new($origin)
$httpOriginBuilder.Scheme = 'http'
$httpOriginBuilder.Port = 80
$httpOrigin = $httpOriginBuilder.Uri.GetLeftPart([UriPartial]::Authority)
$handler = [Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$handler.UseCookies = $false
$client = [Net.Http.HttpClient]::new($handler)
$client.Timeout = [timespan]::FromSeconds(25)
try {
    $root = $client.GetAsync($origin + '/').GetAwaiter().GetResult()
    $location = $root.Headers.Location
    $resolved = if ($location) { [uri]::new([uri]($origin + '/'), $location) } else { $null }
    $rootHtml = if ($RootMode -eq 'Marketing') { $root.Content.ReadAsStringAsync().GetAwaiter().GetResult() } else { '' }
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
    # GET anonimos, sin seguir Location ni enviar cookies o credenciales.
    # La consulta es fija y ficticia: permite comprobar su conservacion sin
    # registrar valores de consultas, cookies o cabeceras de autenticacion.
    $httpProbePaths = @('/', '/Identity/Account/Login?ReturnUrl=%2FOperaciones%3Ftab%3Dmonitoreo')
    $httpRedirects = @($httpProbePaths | ForEach-Object {
        $httpRequestUri = [uri]::new([uri]($httpOrigin + '/'), $_)
        $httpResponse = $client.GetAsync($httpRequestUri).GetAwaiter().GetResult()
        try {
            $httpLocation = $httpResponse.Headers.Location
            $httpDestination = if ($httpLocation) { [uri]::new($httpRequestUri, $httpLocation) } else { $null }
            $acceptedStatus = @(301, 302, 307, 308) -contains [int]$httpResponse.StatusCode
            $sameHost = $httpDestination -and $httpDestination.Host -ieq $BaseUrl.Host
            $https443 = $httpDestination -and $httpDestination.Scheme -eq 'https' -and
                $httpDestination.Port -eq 443 -and !$httpDestination.UserInfo -and !$httpDestination.Fragment
            $preservesPathAndQuery = $httpDestination -and
                $httpDestination.AbsolutePath -ceq $httpRequestUri.AbsolutePath -and
                $httpDestination.Query -ceq $httpRequestUri.Query
            [pscustomobject]@{
                path = $httpRequestUri.AbsolutePath
                hasProbeQuery = ![string]::IsNullOrEmpty($httpRequestUri.Query)
                status = [int]$httpResponse.StatusCode
                targetScheme = if ($httpDestination) { $httpDestination.Scheme } else { $null }
                targetHost = if ($httpDestination) { $httpDestination.Host } else { $null }
                targetPort = if ($httpDestination) { $httpDestination.Port } else { $null }
                sameHost = [bool]$sameHost
                https443 = [bool]$https443
                preservesPathAndQuery = [bool]$preservesPathAndQuery
                passed = [bool]($acceptedStatus -and $sameHost -and $https443 -and $preservesPathAndQuery)
            }
        }
        finally { $httpResponse.Dispose() }
    })
    $antiforgery = @($cookies | Where-Object name -like '*Antiforgery*')
    $checks = [ordered]@{
        httpToHttpsSameHost443PreservesPathQuery = $httpRedirects.Count -eq $httpProbePaths.Count -and
            @($httpRedirects | Where-Object { !$_.passed }).Count -eq 0
        rootMatchesExpectedMode = if ($RootMode -eq 'Marketing') {
            [int]$root.StatusCode -eq 200 -and !$location -and
                $root.Content.Headers.ContentType.MediaType -eq 'text/html' -and $rootHtml -match 'Envirapido'
        } else {
            [int]$root.StatusCode -eq 302 -and $resolved -and
                $resolved.Scheme -eq 'https' -and $resolved.Authority -eq $BaseUrl.Authority
        }
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
        checks = $checks; rootMode = $RootMode; rootStatus = [int]$root.StatusCode
        rootLocation = [string]$location; loginStatus = [int]$login.StatusCode
        cookies = $cookies; anonymousApi = @($api); httpRedirects = $httpRedirects
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
