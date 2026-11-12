param(
    [string]$SecretPath = (Join-Path $PSScriptRoot '..\..\artifacts\net10-20261112\compat-secrets.json'),
    [ValidateRange(1,2147483647)][int]$ForeignTenantOrderId = 1
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Only the two fictional Tenant B accounts captured before the runtime change.
# No sign-in, order mutation, session revocation or physical-device operations.
$taskRepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$taskArtifactRoot = [IO.Path]::GetFullPath((Join-Path $taskRepoRoot 'artifacts\net10-20261112'))
$taskSecretFile = [IO.Path]::GetFullPath($SecretPath)
if (!$taskSecretFile.StartsWith($taskArtifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'El archivo de tokens debe estar dentro del directorio de artifacts QA ignorado.'
}
$taskCaptured = Get-Content -LiteralPath $taskSecretFile -Raw | ConvertFrom-Json
if ($taskCaptured.environment.database -cne 'PilotGoTest_4612d89bd20d464394bd3724a9fea91a' -or
    $taskCaptured.runtime -notmatch '^8[,.]' -or @($taskCaptured.accounts).Count -ne 2) {
    throw 'La captura no corresponde al entorno QA .NET 8 esperado.'
}
$taskListener = Get-NetTCPConnection -LocalPort 5019 -State Listen | Select-Object -First 1
if (!$taskListener) { throw 'El host QA no está disponible.' }
$taskCore = (Get-Process -Id $taskListener.OwningProcess).Modules | Where-Object ModuleName -eq 'coreclr.dll' | Select-Object -First 1
if (!$taskCore -or $taskCore.FileVersionInfo.ProductVersion -notmatch '^10[,.]') {
    throw 'No se confirmó que el proceso QA esté ejecutándose en .NET 10.'
}
$taskRuntime = $taskCore.FileVersionInfo.ProductVersion
Add-Type -AssemblyName System.Net.Http
$taskHandler = [Net.Http.HttpClientHandler]::new()
$taskHandler.AllowAutoRedirect = $false
$taskHttp = [Net.Http.HttpClient]::new($taskHandler)
$taskHttp.Timeout = [TimeSpan]::FromSeconds(15)

function Invoke-QaCompatibilityRequest([string]$Method, [string]$Path, [string]$Token = '', [object]$Body = $null) {
    $taskRequest = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::new($Method), 'http://127.0.0.1:5019' + $Path)
    try {
        if ($Token) { $taskRequest.Headers.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $Token) }
        if ($null -ne $Body) { $taskRequest.Content = [Net.Http.StringContent]::new(($Body | ConvertTo-Json -Compress), [Text.Encoding]::UTF8, 'application/json') }
        $taskResponse = $taskHttp.SendAsync($taskRequest).GetAwaiter().GetResult()
        try {
            $taskPayload = $taskResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $taskParsed = if ([string]::IsNullOrWhiteSpace($taskPayload)) { $null } else { $taskPayload | ConvertFrom-Json }
            return [pscustomobject]@{ Status = [int]$taskResponse.StatusCode; Json = $taskParsed; Empty = [string]::IsNullOrWhiteSpace($taskPayload); Redirect = $null -ne $taskResponse.Headers.Location }
        } finally { $taskResponse.Dispose() }
    } finally { $taskRequest.Dispose() }
}

function Assert-QaIdentity([object]$Response, [string]$UserId, [string]$Role) {
    if ($Response.Status -ne 200 -or $Response.Redirect -or $Response.Json.userId -cne $UserId -or
        @($Response.Json.roles).Count -ne 1 -or $Response.Json.roles[0] -cne $Role) {
        throw 'No se conservó la identidad o el rol de la sesión capturada.'
    }
}

try {
    $taskEnvironment = Invoke-QaCompatibilityRequest 'GET' '/qa/environment'
    if ($taskEnvironment.Status -ne 200 -or !$taskEnvironment.Json.qa -or
        $taskEnvironment.Json.database -cne $taskCaptured.environment.database -or $taskEnvironment.Json.mode -cne 'attached-preserved') {
        throw 'La base QA cambió; no se probarán ni rotarán los tokens capturados.'
    }
    $taskChecks = @()
    foreach ($taskEntry in $taskCaptured.accounts) {
        if ($taskEntry.userId -notin @('pilot-b','shop-b')) { throw 'La captura contiene una cuenta no autorizada para esta prueba.' }
        if ($taskEntry.PSObject.Properties.Name -contains 'refreshRotatedAtUtc') {
            throw 'La captura ya fue utilizada: no volver a rotar ni confundir los tokens nuevos con tokens emitidos por .NET 8.'
        }
        $taskExpiration = if ($taskEntry.expiresAt -is [DateTime]) { [DateTimeOffset]$taskEntry.expiresAt }
            else { [DateTimeOffset]::Parse([string]$taskEntry.expiresAt, [Globalization.CultureInfo]::InvariantCulture) }
        if ($taskExpiration -le [DateTimeOffset]::UtcNow) {
            throw 'El JWT emitido por .NET 8 ya expiró; no es posible afirmar compatibilidad de ese JWT mediante una emisión nueva.'
        }
        $taskRole = if ($taskEntry.userId -ceq 'pilot-b') { 'Piloto' } else { 'Cliente' }
        $taskWrongRolePath = if ($taskRole -eq 'Piloto') { '/api/tienda/ordenes' } else { '/api/piloto/mis-ordenes' }
        $taskWorkPath = if ($taskRole -eq 'Piloto') { '/api/piloto/mis-ordenes' } else { '/api/tienda/ordenes' }
        $taskOldMe = Invoke-QaCompatibilityRequest 'GET' '/api/auth/me' $taskEntry.token
        Assert-QaIdentity $taskOldMe $taskEntry.userId $taskRole
        $taskWork = Invoke-QaCompatibilityRequest 'GET' $taskWorkPath $taskEntry.token
        if ($taskWork.Status -ne 200 -or $taskWork.Redirect) { throw 'La sesión capturada no puede consultar su operación autorizada.' }
        $taskWrongRole = Invoke-QaCompatibilityRequest 'GET' $taskWrongRolePath $taskEntry.token
        if ($taskWrongRole.Status -ne 403 -or $taskWrongRole.Redirect) { throw 'El token capturado no conserva el aislamiento de roles.' }
        $taskForeignPath = if ($taskRole -eq 'Piloto') { "/api/piloto/ordenes/$ForeignTenantOrderId" } else { "/api/tienda/ordenes/$ForeignTenantOrderId/qr" }
        $taskForeign = Invoke-QaCompatibilityRequest 'GET' $taskForeignPath $taskEntry.token
        if ($taskRole -eq 'Piloto') {
            if ($taskForeign.Status -notin @(200,204,403,404) -or ($taskForeign.Status -in @(200,204) -and $null -ne $taskForeign.Json)) {
                throw 'La sesión del piloto pudo consultar una orden ajena o no respondió de forma segura.'
            }
        } elseif ($taskForeign.Status -notin @(400,403,404)) { throw 'La sesión del comercio pudo consultar los códigos de una orden ajena.' }
        $taskRefreshed = Invoke-QaCompatibilityRequest 'POST' '/api/auth/refresh' '' @{ refreshToken = $taskEntry.refreshToken }
        if ($taskRefreshed.Status -ne 200 -or [string]::IsNullOrWhiteSpace($taskRefreshed.Json.token) -or [string]::IsNullOrWhiteSpace($taskRefreshed.Json.refreshToken)) {
            throw 'El refresh token emitido por .NET 8 no pudo renovarse en .NET 10.'
        }
        # Persist the successor immediately: the original refresh is single-use.
        $taskEntry | Add-Member -NotePropertyName tokenNet10 -NotePropertyValue $taskRefreshed.Json.token -Force
        $taskEntry | Add-Member -NotePropertyName refreshTokenNet10 -NotePropertyValue $taskRefreshed.Json.refreshToken -Force
        $taskEntry | Add-Member -NotePropertyName refreshRotatedAtUtc -NotePropertyValue ([DateTime]::UtcNow.ToString('o')) -Force
        $taskCaptured | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $taskSecretFile -Encoding utf8
        $taskNewMe = Invoke-QaCompatibilityRequest 'GET' '/api/auth/me' $taskEntry.tokenNet10
        Assert-QaIdentity $taskNewMe $taskEntry.userId $taskRole
        $taskReplay = Invoke-QaCompatibilityRequest 'POST' '/api/auth/refresh' '' @{ refreshToken = $taskEntry.refreshToken }
        if ($taskReplay.Status -ne 401 -or $taskReplay.Redirect) { throw 'El refresh token anterior pudo reutilizarse.' }
        $taskOldStillValid = Invoke-QaCompatibilityRequest 'GET' '/api/auth/me' $taskEntry.token
        Assert-QaIdentity $taskOldStillValid $taskEntry.userId $taskRole
        $taskChecks += [ordered]@{
            userId = $taskEntry.userId; role = $taskRole; jwtIssuedNet8AcceptedNet10 = $true; originalJwtAfterRefresh = 200;
            authorizedRead = $taskWork.Status; foreignRoleDenied = $taskWrongRole.Status; foreignTenantRead = $taskForeign.Status;
            refreshIssuedNet8AcceptedNet10 = $true; refreshedJwtAccepted = 200; oldRefreshReplayDenied = $taskReplay.Status
        }
    }
    $taskOutcome = [ordered]@{
        outcome = 'PASS'; checkedAtUtc = [DateTime]::UtcNow.ToString('o'); sourceRuntime = $taskCaptured.runtime; destinationRuntime = $taskRuntime;
        database = $taskCaptured.environment.database; checks = $taskChecks; ordersChanged = $false; activeAppPilotTouched = $false;
        cookieLimit = 'Las cookies y CSRF del host QA usan claves efímeras. No se afirma continuidad de cookies 8→10; se valida por separado recrear proveedores con claves y application name constantes en pruebas .NET 10.'
    }
    $taskOutcome | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $taskArtifactRoot 'compat-net8-net10-result.json') -Encoding utf8
    $taskOutcome | ConvertTo-Json -Depth 8
} finally { $taskHttp.Dispose() }
